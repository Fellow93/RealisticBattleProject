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
    /// The rules, per the design:
    ///   * ARMOUR (head/body/legs/gloves/cape) is culture-matched -- a lord upgrades into his own culture's
    ///     look (or a culture-neutral piece), never a foreign kit. Empty armour slots may be filled.
    ///   * WEAPONS upgrade in place only: a filled weapon slot is replaced solely by a better item of the SAME
    ///     item type (a sword for a sword, a bow for a bow, arrows for arrows), of any culture. Empty weapon
    ///     slots are left alone so a coherent loadout (bow+arrows+sidearm+shield) is never broken.
    ///   * HORSE + HARNESS are any culture: a mounted lord trades up to a better warhorse and barding. A lord
    ///     with no horse is left on foot (his troop role is not changed); harness is only bought for a horsed lord.
    ///
    /// "Better" is <see cref="EquipmentElement.ItemValue"/> -- the same slot-by-slot metric the spoils upgrade
    /// economy already ranks kit by (<c>SpoilsPool.GetUpgradedSlots</c>). "Can use" is vanilla's own
    /// <see cref="CharacterHelper.CanUseItem"/> (item difficulty vs the hero's relevant skill, plus gender and
    /// rideability), so a lord never buys a bow he can't draw or a warhorse he can't ride.
    ///
    /// All buying goes through the vanilla <c>SellItemsAction</c> against the town's roster -- money-safe under
    /// RBM's settlement ledger, exactly as <see cref="LordPackTrain"/> buys animals. The displaced old piece is
    /// sold back to the same town so its value is recovered rather than dropped. Spending is bounded by a
    /// per-visit gold fraction and a reserve, so a lord upgrades gradually over many town visits and never
    /// spends himself out of wages.
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

        // Culture policy. Armour is matched to the lord's culture; a neutral (culture-less) piece is allowed
        // too, otherwise most market armour is off-limits. Weapons and mounts are any culture.
        private const bool AllowNeutralCultureArmor = true;

        // The slots we try to improve, in priority order: survivability first (body, head, mount), then the
        // rest of the armour, the barding, and finally weapons. Budget is spent top-down, so the pieces that
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
        /// Walks the lord's slots in priority order and buys the best strictly-better, usable, culture-legal
        /// market item for each, within a per-visit gold budget. Buys stop as soon as the budget runs out.
        /// </summary>
        private static void UpgradeGear(MobileParty party, Settlement settlement)
        {
            Hero lord = party.LeaderHero;
            Town town = settlement.Town;
            ItemRoster stock = town.Owner.ItemRoster;

            int gold = Math.Min(GoldConsideredCap, party.PartyTradeGold);
            if (gold < MinGoldToBuyGear)
            {
                return;
            }
            int budget = (int)(gold * GearSpendFractionPerVisit);
            if (budget <= 0)
            {
                return;
            }

            int spent = 0;
            int upgraded = 0;
            foreach (EquipmentIndex slot in SlotOrder)
            {
                if (budget - spent <= 0)
                {
                    break;
                }
                int cost = TryUpgradeSlot(lord, party, settlement, town, stock, slot, budget - spent);
                if (cost > 0)
                {
                    spent += cost;
                    upgraded++;
                }
            }

            if (SpoilsLog.IsEnabled && upgraded > 0)
            {
                SpoilsLog.Log("HEROKIT", party.Party,
                    PartyLabel(party) + " upgraded " + upgraded + " slot(s) for " + spent + "d at " + settlement.Name);
            }
        }

        /// <summary>
        /// Finds and buys the best upgrade for a single slot from the town's stock, moves it into the slot and
        /// sells the displaced piece back to the town. Returns the gold spent buying the new piece (0 if no
        /// upgrade was made). Money-safe throughout: every gold move is a <c>SellItemsAction</c> against the
        /// settlement, which RBM's ledger funnels.
        /// </summary>
        private static int TryUpgradeSlot(Hero lord, MobileParty party, Settlement settlement, Town town, ItemRoster stock, EquipmentIndex slot, int budget)
        {
            Equipment eq = lord.BattleEquipment;
            EquipmentElement current = eq[slot];
            bool slotFilled = !current.IsEmpty && current.Item != null;

            bool isWeapon = slot >= EquipmentIndex.WeaponItemBeginSlot && slot < EquipmentIndex.NumPrimaryWeaponSlots;
            bool isArmor = slot >= EquipmentIndex.ArmorItemBeginSlot && slot < EquipmentIndex.ArmorItemEndSlot;
            bool isMount = slot == EquipmentIndex.Horse;
            bool isHarness = slot == EquipmentIndex.HorseHarness;

            // Weapons and the ridden warhorse are upgraded in place only -- we never arm an empty weapon slot
            // (loadout coherence) or mount a lord who fights on foot (troop role).
            if ((isWeapon || isMount) && !slotFilled)
            {
                return 0;
            }
            // Barding is only for a horsed lord; an empty harness slot on a mounted lord may still be filled.
            if (isHarness && eq[EquipmentIndex.Horse].IsEmpty)
            {
                return 0;
            }
            // A weapon slot is refreshed only with the SAME item type it already holds (sword->sword,
            // bow->bow, arrows->arrows); the slot-fit check alone would let any weapon class in.
            ItemObject.ItemTypeEnum requiredWeaponType = slotFilled ? current.Item.ItemType : ItemObject.ItemTypeEnum.Invalid;

            int currentValue = slotFilled ? current.ItemValue : 0;

            int bestIndex = -1;
            int bestValue = currentValue;
            int bestPrice = 0;
            for (int j = 0; j < stock.Count; j++)
            {
                ItemObject item = stock.GetItemAtIndex(j);
                if (item == null || !Equipment.IsItemFitsToSlot(slot, item))
                {
                    continue;
                }
                if (isWeapon && item.ItemType != requiredWeaponType)
                {
                    continue;
                }
                if (isArmor && !CultureOk(item, lord))
                {
                    continue;
                }
                ItemRosterElement element = stock.GetElementCopyAtIndex(j);
                if (element.Amount <= 0)
                {
                    continue;
                }
                int value = element.EquipmentElement.ItemValue;
                if (value <= bestValue)
                {
                    continue; // not strictly better than the current piece (or a rival candidate)
                }
                if (!CharacterHelper.CanUseItem(lord.CharacterObject, element.EquipmentElement))
                {
                    continue; // skill / gender / rideability gate
                }
                int price = town.GetItemPrice(element.EquipmentElement, party, isSelling: false);
                if (price <= 0 || price > budget)
                {
                    continue;
                }
                bestValue = value;
                bestIndex = j;
                bestPrice = price;
            }
            if (bestIndex < 0)
            {
                return 0;
            }

            ItemRosterElement chosen = stock.GetElementCopyAtIndex(bestIndex);

            // Buy one into the party roster (settlement is the seller -> lord pays, ledger-funnelled), then
            // lift it out of the roster into the equipment slot.
            SellItemsAction.Apply(town.Owner, party.Party, chosen, 1, town.Owner.Settlement);
            party.Party.ItemRoster.AddToCounts(chosen.EquipmentElement, -1);

            // Sell the displaced piece back to the town so its value is recovered, not dropped.
            if (slotFilled)
            {
                party.Party.ItemRoster.AddToCounts(current, 1);
                SellItemsAction.Apply(party.Party, settlement.Party, new ItemRosterElement(current, 1), 1, settlement);
            }

            lord.BattleEquipment[slot] = chosen.EquipmentElement;

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.LogVerbose("HEROKIT", party.Party,
                    PartyLabel(party) + " " + slot + ": "
                    + (slotFilled ? current.Item.Name.ToString() + " (" + currentValue + ")" : "(empty)")
                    + " -> " + chosen.EquipmentElement.Item.Name + " (" + bestValue + ") for " + bestPrice + "d");
            }
            return bestPrice;
        }

        /// <summary>
        /// Armour culture rule: the item must be of the lord's own culture, or (when allowed) culture-neutral.
        /// </summary>
        private static bool CultureOk(ItemObject item, Hero lord)
        {
            if (item.Culture == null)
            {
                return AllowNeutralCultureArmor;
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
