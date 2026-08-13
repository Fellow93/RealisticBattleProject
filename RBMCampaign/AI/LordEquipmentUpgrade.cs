using System;
using HarmonyLib;
using Helpers;
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
    /// Lets an AI lord refresh his OWN battle kit off a town's market. Vanilla only ever re-equips a hero on
    /// a ruling-clan change (<c>NPCEquipmentsCampaignBehavior</c>); a lord otherwise fights his whole career in
    /// the gear he spawned with, no matter how much better a helmet or warhorse is sitting in the market he's
    /// standing in. This walks a managed lord's slots on settlement entry and, wherever the town stocks a
    /// strictly better item he can actually use, buys it and puts it on.
    ///
    /// EVERY item touched must be of the lord's OWN culture (or culture-neutral): a lord never uses, nor buys,
    /// gear outside his culture. Beyond that:
    ///   * ARMOUR (head/body/legs/gloves/cape): upgraded on tier; empty armour slots may be filled.
    ///   * WEAPONS upgrade in place only, by EXACT weapon class: a two-handed sword is replaced solely by a
    ///     higher-tier two-handed sword (never a two-handed axe), a bow by a bow, arrows by arrows. Empty weapon
    ///     slots are never armed, so a coherent loadout (weapon + ammo + shield) is never reshaped.
    ///   * HORSE + HARNESS: a mounted lord trades up to a better warhorse and barding. A lord with no horse is
    ///     left on foot (his troop role is not changed); harness is only bought for a horsed lord.
    ///
    /// Before shopping he first looks in his OWN baggage: a strictly-better culture-legal piece he already
    /// carries (battle loot he hasn't sold yet) is put on for free, and only when neither his baggage nor his
    /// worn gear answers does he reach for the market. This runs before the loot-sale behavior fires, so he
    /// grabs the good loot before it is sold off.
    ///
    /// "Better" is the item's continuous TIER (<see cref="ItemObject.Tierf"/>: 2.1, 3.3, ... -- not the rounded
    /// <see cref="ItemObject.Tier"/> enum, which would tie most same-bucket swaps), driven by RBM's own tier
    /// model (<c>ItemValuesTiers</c>). Gold value is deliberately NOT the yardstick, so a lord chases combat
    /// quality rather than a dear-but-weak trinket. "Can use" is vanilla's own
    /// <see cref="CharacterHelper.CanUseItem"/> (item difficulty vs the hero's relevant skill, plus gender and
    /// rideability), so a lord never buys a bow he can't draw or a warhorse he can't ride.
    ///
    /// All buying goes through the vanilla <c>SellItemsAction</c> against the town's roster -- money-safe under
    /// RBM's settlement ledger, exactly as <see cref="LordPackTrain"/> buys animals. The displaced old piece is
    /// dropped back into the baggage, where the loot-sale behavior that fires straight after this handler sells
    /// it properly (gold-capped, keeping food and clean mounts). Spending is bounded by a per-visit gold
    /// fraction and a reserve, so a lord upgrades gradually over many town visits and never spends out of wages.
    ///
    /// Gated on <c>rbmCampaignEnabled</c> and hung off a Postfix of
    /// <c>PartiesBuyHorseCampaignBehavior.OnSettlementEntered</c> (a Postfix runs independently of whether
    /// <see cref="LordPackTrain"/>'s Prefix took over that handler, so the two never entangle). AI lords only;
    /// the player's own party and the player clan's heroes are left to the player.
    /// </summary>
    public static class LordEquipmentUpgrade
    {
        // Buying reserve: never touch a lord under this much gold, and spend at most this fraction of his
        // (capped) gold on gear per settlement visit. Kit is dear, so the reserve is higher than the pack
        // train's -- wages, recruiting and ransom money stay intact and the upgrade stays gradual.
        private const int MinGoldToBuyGear = 5000;
        private const float GearSpendFractionPerVisit = 0.25f;
        private const int GoldConsideredCap = 100000; // as vanilla: ignore hoards beyond this when sizing spend

        // Culture policy. EVERY slot is matched to the lord's own culture -- he neither wears nor buys foreign
        // gear. A neutral (culture-less) piece is allowed too, otherwise most of the market is off-limits and
        // many culture-less items (basic horses, some tools) could never be equipped.
        private const bool AllowNeutralCulture = true;

        // The slots we try to improve, in priority order: survivability first (body, head, mount), then the
        // rest of the armour and the barding, and finally weapons. Budget is spent top-down, so the pieces that
        // keep a lord alive are bought before his sidearm.
        private static readonly EquipmentIndex[] SlotOrder =
        {
            EquipmentIndex.Body,
            EquipmentIndex.Head,
            EquipmentIndex.Horse,
            EquipmentIndex.Leg,
            EquipmentIndex.Gloves,
            EquipmentIndex.Cape,
            EquipmentIndex.HorseHarness,
            EquipmentIndex.Weapon0,
            EquipmentIndex.Weapon1,
            EquipmentIndex.Weapon2,
            EquipmentIndex.Weapon3,
        };

        // ------------------------------------------------------------------ eligibility

        /// <summary>
        /// The lords whose personal kit RBM manages: a real AI lord party (never the player's main party, and
        /// never a hero of the player's own clan) with a living leader, standing in a town it isn't at war
        /// with. No party-size floor -- a hero upgrades his own gear whatever his headcount.
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
            Hero lord = mobileParty.LeaderHero;
            if (!lord.IsAlive || lord.Clan == Clan.PlayerClan)
            {
                return false; // leave the player's own heroes to the player
            }
            if (settlement == null || !settlement.IsTown || settlement.Town == null)
            {
                return false;
            }
            if (mobileParty.MapFaction == null || mobileParty.MapFaction.IsAtWarWith(settlement.MapFaction))
            {
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ the upgrade pass

        /// <summary>
        /// Walks the lord's slots in priority order. For each, it first equips the best strictly-better piece
        /// he already carries in his baggage (free), and only reaches for the town market when the market
        /// stocks something better still than both what he wears AND what he owns -- within a per-visit gold
        /// budget. The baggage swap runs even for a lord too poor to shop, since it costs nothing.
        /// </summary>
        private static void UpgradeGear(MobileParty party, Settlement settlement)
        {
            Hero lord = party.LeaderHero;
            Town town = settlement.Town;
            ItemRoster stock = town.Owner.ItemRoster;

            // Gold budget for MARKET buys only. A lord too poor to shop (budget 0) can still put on better
            // gear already in his baggage -- that spends nothing -- so a thin purse never blocks the swap.
            int gold = Math.Min(GoldConsideredCap, party.PartyTradeGold);
            int budget = gold >= MinGoldToBuyGear ? (int)(gold * GearSpendFractionPerVisit) : 0;

            int spent = 0;
            int upgraded = 0;
            foreach (EquipmentIndex slot in SlotOrder)
            {
                int cost = TryUpgradeSlot(lord, party, town, stock, slot, Math.Max(0, budget - spent));
                if (cost >= 0)
                {
                    spent += cost;
                    upgraded++;
                }
            }

            if (SpoilsLog.IsEnabled && upgraded > 0)
            {
                SpoilsLog.Log("HEROKIT", party.Party,
                    PartyLabel(party) + " upgraded " + upgraded + " slot(s), spent " + spent + "d at " + settlement.Name);
            }
        }

        /// <summary>
        /// Improves a single slot from the best available source. Prefers a strictly-better piece the lord
        /// already carries (free); failing that, buys a still-better piece off the town market (money-safe,
        /// budget-bounded). The displaced piece drops into the baggage for the loot-sale behavior to clear.
        /// Returns the gold spent (0 for a free baggage swap), or -1 when the slot was left unchanged.
        /// </summary>
        private static int TryUpgradeSlot(Hero lord, MobileParty party, Town town, ItemRoster stock, EquipmentIndex slot, int budget)
        {
            Equipment eq = lord.BattleEquipment;
            EquipmentElement current = eq[slot];
            bool slotFilled = !current.IsEmpty && current.Item != null;

            bool isWeapon = slot >= EquipmentIndex.WeaponItemBeginSlot && slot < EquipmentIndex.NumPrimaryWeaponSlots;
            bool isMount = slot == EquipmentIndex.Horse;
            bool isHarness = slot == EquipmentIndex.HorseHarness;

            // Weapons and the ridden warhorse are upgraded in place only -- we never arm an empty weapon slot
            // (loadout coherence) or mount a lord who fights on foot (his troop role is not changed).
            if ((isWeapon || isMount) && !slotFilled)
            {
                return -1;
            }
            // Barding is only for a horsed lord; an empty harness slot on a mounted lord may still be filled.
            if (isHarness && eq[EquipmentIndex.Horse].IsEmpty)
            {
                return -1;
            }
            // A weapon slot is refreshed only by the EXACT weapon class it already holds (two-handed sword ->
            // two-handed sword, bow -> bow, arrows -> arrows), so a loadout's shape never shifts. A weapon with
            // no primary usage we can't classify, so leave it be.
            WeaponClass? requiredWeaponClass = null;
            if (isWeapon)
            {
                WeaponComponentData primary = current.Item.PrimaryWeapon;
                if (primary == null)
                {
                    return -1;
                }
                requiredWeaponClass = primary.WeaponClass;
            }
            // Rank by continuous item TIER (Tierf: 2.1, 3.3, ...), not gold value -- so a lord chases combat
            // quality, never a dear-but-weak trinket, and RBM's own tier model (ItemValuesTiers) drives it.
            // NOTE the currently-worn piece is NEVER culture-checked -- only its tier matters here. The culture
            // gate applies solely to candidates, so a higher-tier FOREIGN piece the lord already wears is kept:
            // it is displaced only by a same-culture item of strictly higher tier (ties keep the incumbent).
            float currentTier = slotFilled ? current.Item.Tierf : 0f;

            // 1. The best strictly-better usable piece the lord already carries in his baggage -- free to wear.
            ItemRoster bag = party.Party.ItemRoster;
            float ownedTier;
            int ownedIndex = FindBestUsable(bag, slot, requiredWeaponClass, lord, currentTier, out ownedTier);

            // 2. The best affordable market piece -- but it must beat BOTH what he wears AND what he already
            //    owns, or there is no reason to spend gold when the baggage already answers.
            float marketFloor = ownedIndex >= 0 ? ownedTier : currentTier;
            float marketTier;
            int marketPrice;
            int marketIndex = FindBestAffordable(stock, slot, requiredWeaponClass, lord, party, town, marketFloor, budget, out marketTier, out marketPrice);

            if (marketIndex >= 0)
            {
                ItemRosterElement chosen = stock.GetElementCopyAtIndex(marketIndex);
                // Buy one into the party roster (settlement is the seller -> lord pays, ledger-funnelled),
                // then lift it out of the roster into the equipment slot.
                SellItemsAction.Apply(town.Owner, party.Party, chosen, 1, town.Owner.Settlement);
                bag.AddToCounts(chosen.EquipmentElement, -1);
                EquipDisplacing(lord, bag, slot, current, slotFilled, chosen.EquipmentElement);
                LogSlot(party, slot, slotFilled, current, currentTier, chosen.EquipmentElement, marketTier, marketPrice);
                return marketPrice;
            }

            if (ownedIndex >= 0)
            {
                ItemRosterElement chosen = bag.GetElementCopyAtIndex(ownedIndex);
                bag.AddToCounts(chosen.EquipmentElement, -1); // take the better piece out of the baggage
                EquipDisplacing(lord, bag, slot, current, slotFilled, chosen.EquipmentElement);
                LogSlot(party, slot, slotFilled, current, currentTier, chosen.EquipmentElement, ownedTier, 0);
                return 0;
            }

            return -1;
        }

        /// <summary>
        /// Best strictly-higher-tier usable item for the slot in a roster, ignoring price -- used to scan the
        /// lord's own baggage, where wearing what he already carries costs nothing. Returns the roster index,
        /// and the item's tier via <paramref name="bestTier"/>; -1 (and <paramref name="tierFloor"/>) when none beats it.
        /// </summary>
        private static int FindBestUsable(ItemRoster roster, EquipmentIndex slot, WeaponClass? requiredWeaponClass, Hero lord, float tierFloor, out float bestTier)
        {
            bestTier = tierFloor;
            int bestIndex = -1;
            for (int j = 0; j < roster.Count; j++)
            {
                ItemObject item = roster.GetItemAtIndex(j);
                if (!ItemFitsRules(item, slot, requiredWeaponClass, lord))
                {
                    continue;
                }
                ItemRosterElement element = roster.GetElementCopyAtIndex(j);
                if (element.Amount <= 0)
                {
                    continue;
                }
                float tier = item.Tierf;
                if (tier <= bestTier)
                {
                    continue; // not strictly higher tier than the current piece (or a rival candidate)
                }
                if (!CharacterHelper.CanUseItem(lord.CharacterObject, element.EquipmentElement))
                {
                    continue; // skill / gender / rideability gate
                }
                bestTier = tier;
                bestIndex = j;
            }
            return bestIndex;
        }

        /// <summary>
        /// Best strictly-higher-tier usable item for the slot the lord can also afford at this town, above the
        /// given tier floor and within <paramref name="budget"/>. Returns the market index, plus the item's tier
        /// and price; -1 when nothing qualifies (including a zero budget).
        /// </summary>
        private static int FindBestAffordable(ItemRoster stock, EquipmentIndex slot, WeaponClass? requiredWeaponClass, Hero lord, MobileParty party, Town town, float tierFloor, int budget, out float bestTier, out int bestPrice)
        {
            bestTier = tierFloor;
            bestPrice = 0;
            int bestIndex = -1;
            if (budget <= 0)
            {
                return -1;
            }
            for (int j = 0; j < stock.Count; j++)
            {
                ItemObject item = stock.GetItemAtIndex(j);
                if (!ItemFitsRules(item, slot, requiredWeaponClass, lord))
                {
                    continue;
                }
                ItemRosterElement element = stock.GetElementCopyAtIndex(j);
                if (element.Amount <= 0)
                {
                    continue;
                }
                float tier = item.Tierf;
                if (tier <= bestTier)
                {
                    continue;
                }
                if (!CharacterHelper.CanUseItem(lord.CharacterObject, element.EquipmentElement))
                {
                    continue;
                }
                int price = town.GetItemPrice(element.EquipmentElement, party, isSelling: false);
                if (price <= 0 || price > budget)
                {
                    continue;
                }
                bestTier = tier;
                bestPrice = price;
                bestIndex = j;
            }
            return bestIndex;
        }

        /// <summary>The shared per-item filter: fits the slot, is of the lord's culture (or neutral), and -- for
        /// weapon slots -- is the EXACT same weapon class already worn. Usability and tier are checked by the
        /// callers against each candidate.</summary>
        private static bool ItemFitsRules(ItemObject item, EquipmentIndex slot, WeaponClass? requiredWeaponClass, Hero lord)
        {
            if (item == null || !Equipment.IsItemFitsToSlot(slot, item))
            {
                return false;
            }
            if (!CultureOk(item, lord))
            {
                return false; // no foreign gear in any slot
            }
            if (requiredWeaponClass.HasValue)
            {
                WeaponComponentData primary = item.PrimaryWeapon;
                if (primary == null || primary.WeaponClass != requiredWeaponClass.Value)
                {
                    return false; // a weapon slot takes only the exact same weapon class
                }
            }
            return true;
        }

        /// <summary>
        /// Puts <paramref name="incoming"/> on and drops the displaced piece back into the baggage, where the
        /// loot-sale behavior that fires straight after this handler sells it (gold-capped, keeping food and
        /// clean mounts). The incoming item must already have been lifted out of the baggage by the caller.
        /// </summary>
        private static void EquipDisplacing(Hero lord, ItemRoster bag, EquipmentIndex slot, EquipmentElement current, bool slotFilled, EquipmentElement incoming)
        {
            if (slotFilled)
            {
                bag.AddToCounts(current, 1);
            }
            lord.BattleEquipment[slot] = incoming;
        }

        private static void LogSlot(MobileParty party, EquipmentIndex slot, bool slotFilled, EquipmentElement current, float currentTier, EquipmentElement chosen, float newTier, int price)
        {
            if (!SpoilsLog.IsEnabled)
            {
                return;
            }
            SpoilsLog.LogVerbose("HEROKIT", party.Party,
                PartyLabel(party) + " " + slot + ": "
                + (slotFilled ? current.Item.Name.ToString() + " (t" + currentTier.ToString("0.0") + ")" : "(empty)")
                + " -> " + chosen.Item.Name + " (t" + newTier.ToString("0.0") + ") "
                + (price > 0 ? "bought for " + price + "d" : "from baggage"));
        }

        /// <summary>
        /// Culture rule (applied to EVERY slot): the item must be of the lord's own culture, or (when allowed)
        /// culture-neutral. A lord neither wears nor buys gear belonging to another culture.
        /// </summary>
        private static bool CultureOk(ItemObject item, Hero lord)
        {
            if (item.Culture == null)
            {
                return AllowNeutralCulture;
            }
            return item.Culture == lord.Culture;
        }

        private static string PartyLabel(MobileParty mobileParty)
        {
            if (mobileParty.LeaderHero != null && mobileParty.LeaderHero.Name != null)
            {
                return mobileParty.LeaderHero.Name.ToString();
            }
            return mobileParty.Name != null ? mobileParty.Name.ToString() : mobileParty.StringId;
        }

        // ------------------------------------------------------------------ patch

        /// <summary>
        /// Runs after <c>PartiesBuyHorseCampaignBehavior.OnSettlementEntered</c> (whether or not
        /// <see cref="LordPackTrain"/> replaced its body), giving the entering lord a chance to trade up his
        /// personal kit. Non-managed parties and a disabled module do nothing.
        /// </summary>
        [HarmonyPatch(typeof(PartiesBuyHorseCampaignBehavior), "OnSettlementEntered")]
        private static class UpgradeLordKitOnSettlementEntered
        {
            private static void Postfix(MobileParty mobileParty, Settlement settlement, Hero hero)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !IsManaged(mobileParty, settlement))
                {
                    return;
                }
                UpgradeGear(mobileParty, settlement);
            }
        }
    }
}
