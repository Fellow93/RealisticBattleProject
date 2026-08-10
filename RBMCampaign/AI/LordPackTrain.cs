using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Gives AI lord parties a proper baggage train. Vanilla leaves lords cargo-starved and only lightly
    /// horsed: no behavior ever buys pack animals for a lord, and TWO behaviors actively sell every looted
    /// mule (<c>PartiesSellLootCampaignBehavior</c> and the loot-sell half of
    /// <c>PartiesBuyHorseCampaignBehavior</c>), while spare rideable mounts are bought only up to ~1 per man
    /// and trimmed back to ~10% of the lord's gold. So loot capacity is hostage to headcount and horse-loot
    /// luck, and infantry-heavy parties crawl.
    ///
    /// This replaces the animal side of both native settlement-entry handlers with two targets:
    ///   * PACK ANIMALS -- a proactive baggage train scaled to party size (~1 per <see cref="PackPerMan"/>
    ///     men, capped at 1 per <see cref="MaxPackPerMan"/> to stay well under the man-count herding
    ///     threshold). Each pack animal is +100 carrying capacity, so this is the cargo lever. Lords now
    ///     BUY up to the target from a town's stock AND HOLD looted mules up to it instead of dumping them.
    ///   * SPARE RIDEABLE MOUNTS -- ~1 loose mount per <see cref="MountPerFootmenDenom"/> dismounted
    ///     footmen, so an infantry party can "ride" a share of its foot (vanilla's mounted-footmen speed
    ///     bonus, <c>min(footmen, looseMounts)</c>) and moves noticeably faster, without turning every lord
    ///     into fast cavalry or draining town horse markets.
    ///
    /// All buying goes through the vanilla <c>SellItemsAction</c> against the town's own roster, which is
    /// money-safe under RBM's settlement ledger (the <c>SettlementGoldFunnel</c> catches the settlement-side
    /// write; the party side pays from the lord's gold). Buying is bounded by a per-visit gold fraction and
    /// a reserve, so lords never spend themselves broke. Surplus above target is sold back.
    ///
    /// Gated on <c>rbmCampaignEnabled</c>: with the module off, both Prefixes return true and vanilla runs
    /// untouched. Only takes over for AI lord parties (never the main party) of a decent size standing in a
    /// friendly town; everyone else falls through to vanilla. Generic loot (weapons, armour, food) is left
    /// entirely to the native sellers -- this only ever touches horses and pack animals.
    /// </summary>
    public static class LordPackTrain
    {
        // Pack-animal baggage train baseline: ~1 per PackPerMan men.
        private const int PackPerMan = 8;

        // Herding-penalty safety. DefaultPartySpeedCalculatingModel.GetHerdingModifier slows a party once
        // its herd -- loose pack animals + livestock + any loose mounts beyond its footmen -- exceeds its
        // man count (herdSize > TotalManCount). We hold the pack train to this fraction of the man count, so
        // the herd stays comfortably under that line even before the other two terms are driven to zero
        // (livestock is dumped, spare mounts are held at/under the footman count). Retune-proof: the pack
        // target can never be raised past this cap however PackPerMan is tuned.
        private const float MaxHerdFractionOfMen = 0.2f;

        // Spare rideable mounts: ~1 loose mount per this many dismounted footmen. 2 == mount half the foot,
        // a moderate speed lift. Held at or under the footman count, so surplus mounts never feed the herd:
        // the speed model only herds loose mounts BEYOND footmen -- max(0, mounts - footmen).
        private const int MountPerFootmenDenom = 2;

        // Don't bother managing a baggage train for a skirmisher-sized party.
        private const int MinManagedPartySize = 10;

        // Buying reserve: never touch a lord under this much gold, and spend at most this fraction of his
        // (capped) gold on animals per settlement visit. Keeps wages/recruiting/ransom money intact.
        private const int MinGoldToBuyAnimals = 2000;
        private const float AnimalSpendFractionPerVisit = 0.15f;
        private const int GoldConsideredCap = 100000; // as vanilla: ignore hoards beyond this when sizing spend

        // Safety bound on the cheapest-first buy loop per category.
        private const int MaxBuyPasses = 8;

        // ------------------------------------------------------------------ eligibility + targets

        /// <summary>
        /// The parties whose baggage train RBM manages: a real AI lord party (never the player's main
        /// party) of a decent size, standing in a town it isn't at war with. Same gate for both handlers,
        /// so the buy side and the keep side always agree on who they're steering.
        /// </summary>
        public static bool IsManaged(MobileParty mobileParty, Settlement settlement)
        {
            if (!Campaign.Current.GameStarted || mobileParty == null || mobileParty == MobileParty.MainParty)
            {
                return false;
            }
            if (!mobileParty.IsLordParty || mobileParty.LeaderHero == null || mobileParty.IsDisbanding)
            {
                return false;
            }
            if (settlement == null || !settlement.IsTown || settlement.Town == null)
            {
                return false;
            }
            if (mobileParty.MapFaction == null || mobileParty.MapFaction.IsAtWarWith(settlement.MapFaction))
            {
                return false;
            }
            return mobileParty.Party.NumberOfRegularMembers >= MinManagedPartySize;
        }

        /// <summary>
        /// Proactive pack-animal target: ~men/PackPerMan, floored at 1, but never above the herding-safe
        /// cap (a fraction of TotalManCount -- the threshold the speed model's herd is measured against).
        /// </summary>
        public static int PackTarget(MobileParty mobileParty)
        {
            int men = mobileParty.Party.NumberOfRegularMembers;
            if (men < MinManagedPartySize)
            {
                return 0;
            }
            int herdSafeCap = Math.Max(1, (int)(mobileParty.MemberRoster.TotalManCount * MaxHerdFractionOfMen));
            int target = Math.Max(1, men / PackPerMan);
            return Math.Min(target, herdSafeCap);
        }

        /// <summary>
        /// Spare loose-mount target: ~footmen/MountPerFootmenDenom, held at or under the footman count so no
        /// mount ever spills into the herd. Footmen counted exactly as the speed model does
        /// (NumberOfMenWithoutHorse).
        /// </summary>
        public static int MountTarget(MobileParty mobileParty)
        {
            int footmen = mobileParty.Party.NumberOfMenWithoutHorse;
            if (footmen < 0)
            {
                footmen = 0;
            }
            return Math.Min(footmen / MountPerFootmenDenom, footmen);
        }

        // ------------------------------------------------------------------ the buy/sell policy

        /// <summary>
        /// Buys pack animals then spare mounts up to their targets (pack first -- cargo is the scarcer
        /// need), then sells any animal surplus above target back to the town. Runs in place of vanilla's
        /// <c>PartiesBuyHorseCampaignBehavior.OnSettlementEntered</c> for managed parties.
        /// </summary>
        private static void ManageAnimals(MobileParty party, Settlement settlement)
        {
            Town town = settlement.Town;
            int packTarget = PackTarget(party);
            int mountTarget = MountTarget(party);

            int gold = Math.Min(GoldConsideredCap, party.PartyTradeGold);
            int budget = gold >= MinGoldToBuyAnimals ? (int)(gold * AnimalSpendFractionPerVisit) : 0;

            int packBought = 0;
            int mountBought = 0;
            int spent = 0;

            int needPack = packTarget - party.Party.NumberOfPackAnimals;
            if (needPack > 0 && budget > 0)
            {
                spent += BuyCategory(party, town, DefaultItemCategories.PackAnimal, needPack, budget, out packBought);
                budget -= spent;
            }

            int needMount = mountTarget - party.Party.NumberOfMounts;
            if (needMount > 0 && budget > 0)
            {
                int mountSpent = BuyCategory(party, town, DefaultItemCategories.Horse, needMount, budget, out mountBought);
                spent += mountSpent;
                budget -= mountSpent;
            }

            // Trim anything above target back to the market (cheapest first, so the best animals stay), and
            // dump livestock outright -- it gives a lord no capacity and only feeds the herd. Between these
            // and the pack/mount targets, the manager itself guarantees herdSize < TotalManCount on exit.
            int packSold = SellSurplus(party, settlement, DefaultItemCategories.PackAnimal, packTarget, isPack: true);
            int mountSold = SellSurplus(party, settlement, DefaultItemCategories.Horse, mountTarget, isPack: false);
            int livestockSold = SellAllLivestock(party, settlement);

            if (SpoilsLog.IsEnabled && (packBought + mountBought + packSold + mountSold + livestockSold) > 0)
            {
                SpoilsLog.Log("PACKTRAIN", party.Party,
                    PartyLabel(party)
                    + "  ·  packs " + party.Party.NumberOfPackAnimals + "/" + packTarget
                    + " (+" + packBought + " -" + packSold + ")"
                    + "  ·  mounts " + party.Party.NumberOfMounts + "/" + mountTarget
                    + " (+" + mountBought + " -" + mountSold + ")"
                    + (livestockSold > 0 ? "  ·  livestock -" + livestockSold : "")
                    + "  ·  spent " + spent + "d at " + settlement.Name
                    + "  ·  cap " + party.InventoryCapacity + " load " + (int)party.TotalWeightCarried);
            }
        }

        /// <summary>
        /// Sells off every head of livestock (cows, sheep, hogs -- HorseComponent.IsLiveStock). A lord has
        /// no use for it: it adds zero carrying capacity and counts toward the herd that triggers the speed
        /// penalty, so it is pure drag. Gold-capped per element like the loot sale. Returns head sold.
        /// </summary>
        private static int SellAllLivestock(MobileParty party, Settlement settlement)
        {
            int settlementGold = settlement.SettlementComponent.Gold;
            int sold = 0;
            for (int i = party.ItemRoster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement subject = party.ItemRoster[i];
                ItemObject item = subject.EquipmentElement.Item;
                if (item == null || !item.HasHorseComponent || !item.HorseComponent.IsLiveStock)
                {
                    continue;
                }
                int price = settlement.Town.GetItemPrice(subject.EquipmentElement, party, isSelling: true);
                if (price <= 0)
                {
                    continue;
                }
                int n = subject.Amount;
                int affordable = settlementGold / price;
                if (n > affordable)
                {
                    n = affordable;
                }
                if (n <= 0)
                {
                    continue;
                }
                SellItemsAction.Apply(party.Party, settlement.Party, subject, n, settlement);
                settlementGold -= n * price;
                sold += n;
            }
            return sold;
        }

        /// <summary>
        /// Buys up to <paramref name="wantCount"/> of the cheapest clean (unmodified, so they count toward
        /// capacity) items of <paramref name="cat"/> from the town's own roster, within
        /// <paramref name="budget"/>. Money-safe: the settlement-as-seller SellItemsAction is exactly the
        /// vanilla buy idiom, and RBM's ledger funnels the settlement-side gold. Returns gold spent.
        /// </summary>
        private static int BuyCategory(MobileParty party, Town town, ItemCategory cat, int wantCount, int budget, out int bought)
        {
            bought = 0;
            if (wantCount <= 0 || budget <= 0 || town.MarketData.GetItemCountOfCategory(cat) <= 0)
            {
                return 0;
            }
            ItemRoster stock = town.Owner.ItemRoster;
            int spent = 0;
            for (int pass = 0; pass < MaxBuyPasses && wantCount > 0 && budget - spent > 0; pass++)
            {
                int bestIndex = -1;
                int bestPrice = int.MaxValue;
                for (int j = 0; j < stock.Count; j++)
                {
                    ItemObject item = stock.GetItemAtIndex(j);
                    if (item == null || item.ItemCategory != cat)
                    {
                        continue;
                    }
                    ItemRosterElement element = stock.GetElementCopyAtIndex(j);
                    if (element.EquipmentElement.ItemModifier != null)
                    {
                        continue; // a modified animal doesn't add capacity; skip it
                    }
                    int price = town.GetItemPrice(element.EquipmentElement, party, isSelling: false);
                    if (price > 0 && price < bestPrice)
                    {
                        bestPrice = price;
                        bestIndex = j;
                    }
                }
                if (bestIndex < 0)
                {
                    break;
                }
                ItemRosterElement chosen = stock.GetElementCopyAtIndex(bestIndex);
                int affordable = (budget - spent) / bestPrice;
                int n = Math.Min(Math.Min(chosen.Amount, wantCount), affordable);
                if (n <= 0)
                {
                    break;
                }
                SellItemsAction.Apply(town.Owner, party.Party, chosen, n, town.Owner.Settlement);
                spent += n * bestPrice;
                wantCount -= n;
                bought += n;
            }
            return spent;
        }

        /// <summary>
        /// Sells clean animals of <paramref name="cat"/> beyond <paramref name="target"/> back to the town,
        /// cheapest first so the party keeps its best stock. Gold-capped per element like vanilla's loot
        /// sale. Returns how many head were sold.
        /// </summary>
        private static int SellSurplus(MobileParty party, Settlement settlement, ItemCategory cat, int target, bool isPack)
        {
            int current = isPack ? party.Party.NumberOfPackAnimals : party.Party.NumberOfMounts;
            int toSell = current - target;
            if (toSell <= 0)
            {
                return 0;
            }
            int settlementGold = settlement.SettlementComponent.Gold;
            int sold = 0;
            for (int i = party.ItemRoster.Count - 1; i >= 0 && toSell > 0; i--)
            {
                ItemRosterElement subject = party.ItemRoster[i];
                ItemObject item = subject.EquipmentElement.Item;
                if (item == null || subject.EquipmentElement.ItemModifier != null || item.ItemCategory != cat)
                {
                    continue;
                }
                int price = settlement.Town.GetItemPrice(subject.EquipmentElement, party, isSelling: true);
                if (price <= 0)
                {
                    continue;
                }
                int n = Math.Min(toSell, subject.Amount);
                int affordable = settlementGold / price;
                if (n > affordable)
                {
                    n = affordable;
                }
                if (n <= 0)
                {
                    continue;
                }
                SellItemsAction.Apply(party.Party, settlement.Party, subject, n, settlement);
                settlementGold -= n * price;
                toSell -= n;
                sold += n;
            }
            return sold;
        }

        /// <summary>
        /// Sells a managed lord's loot on entering a town, but KEEPS his clean pack animals (and, as
        /// vanilla already did, his clean rideable spare mounts) instead of dumping them. A verbatim port of
        /// <c>PartiesSellLootCampaignBehavior.OnSettlementEntered</c> with one added keep-branch for pack
        /// animals -- everything else (weapons, armour, livestock, modified horses) still sells exactly as
        /// before, at the same gold-capped price. Trimming the kept animals back to target is left to
        /// <see cref="ManageAnimals"/>, which owns the animal counts.
        /// </summary>
        private static void SellLootKeepingAnimals(MobileParty party, Settlement settlement)
        {
            int gold = settlement.SettlementComponent.Gold;
            int keptPack = 0;
            for (int i = party.ItemRoster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement subject = party.ItemRoster[i];
                ItemObject item = subject.EquipmentElement.Item;
                if (item == null || item.IsFood)
                {
                    continue; // keep food, as vanilla
                }
                if (subject.EquipmentElement.ItemModifier == null && item.HasHorseComponent)
                {
                    // Keep a clean spare rideable mount (vanilla) or a clean pack animal (RBM addition).
                    if (item.HorseComponent.IsRideable && !item.HorseComponent.IsPackAnimal)
                    {
                        continue;
                    }
                    if (item.HorseComponent.IsPackAnimal)
                    {
                        keptPack += subject.Amount;
                        continue;
                    }
                    // otherwise livestock (neither rideable nor pack) -- falls through and sells, as vanilla
                }
                int itemPrice = settlement.Town.GetItemPrice(subject.EquipmentElement, party, isSelling: true);
                if (itemPrice <= 0)
                {
                    continue;
                }
                int amount = subject.Amount;
                int num = (itemPrice * amount < gold) ? amount : (gold / itemPrice);
                if (num > 0)
                {
                    SellItemsAction.Apply(party.Party, settlement.Party, subject, num, settlement);
                }
            }

            if (SpoilsLog.IsEnabled && keptPack > 0)
            {
                SpoilsLog.LogVerbose("PACKTRAIN", party.Party,
                    PartyLabel(party) + " kept " + keptPack + " looted pack animal(s) at " + settlement.Name
                    + " (target " + PackTarget(party) + ")");
            }
        }

        private static string PartyLabel(MobileParty mobileParty)
        {
            if (mobileParty.LeaderHero != null && mobileParty.LeaderHero.Name != null)
            {
                return mobileParty.LeaderHero.Name.ToString();
            }
            return mobileParty.Name != null ? mobileParty.Name.ToString() : mobileParty.StringId;
        }

        // ------------------------------------------------------------------ patches

        /// <summary>
        /// Replaces the animal-management side of <c>PartiesBuyHorseCampaignBehavior</c> for managed
        /// parties. Vanilla's version also sold generic loot here; we drop that (the loot still sells via
        /// the sibling <c>PartiesSellLootCampaignBehavior</c> path), so this only ever moves horses and
        /// pack animals. Non-managed parties fall through to vanilla.
        /// </summary>
        [HarmonyPatch(typeof(PartiesBuyHorseCampaignBehavior), "OnSettlementEntered")]
        private static class OverrideLordAnimalManagement
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement, Hero hero)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !IsManaged(mobileParty, settlement))
                {
                    return true; // vanilla
                }
                ManageAnimals(mobileParty, settlement);
                return false;
            }
        }

        /// <summary>
        /// Replaces <c>PartiesSellLootCampaignBehavior.OnSettlementEntered</c> for managed parties so looted
        /// pack animals are kept rather than sold. Non-managed parties fall through to vanilla.
        /// </summary>
        [HarmonyPatch(typeof(PartiesSellLootCampaignBehavior), "OnSettlementEntered")]
        private static class KeepPackAnimalsInLootSale
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement, Hero hero)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !IsManaged(mobileParty, settlement))
                {
                    return true; // vanilla
                }
                SellLootKeepingAnimals(mobileParty, settlement);
                return false;
            }
        }
    }
}
