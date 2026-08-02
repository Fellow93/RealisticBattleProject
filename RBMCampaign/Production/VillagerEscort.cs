using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Gives villager trade parties an armed escort that scales with the worth of what they are
    /// hauling: a village walking a cartload of grain to market travels unguarded, one moving silver
    /// or warhorses sends militia along -- and the richer the load, the more of that escort is elite
    /// militia rather than levy. Vanilla villager parties are pure unarmed villagers no matter what
    /// is in the cargo hold, which makes high-value villages free money for bandits.
    ///
    /// Runs as a postfix on <c>VillagerCampaignBehavior.LoadAndSendVillagerParty</c>, i.e. after
    /// <c>MoveItemsToVillagerParty</c> has filled the party roster, so the cargo value is final.
    ///
    /// The escort is a TARGET, not a per-trip addition: the militia already riding along count
    /// toward it, so repeat trips top the guard back up to strength (replacing losses) instead of
    /// stacking a fresh escort every run.
    ///
    /// The guard is BORROWED from the village's standing militia, not raised fresh: dispatching a
    /// convoy debits <c>Settlement.Militia</c>, and every guard that walks back through the gate is
    /// credited back on arrival. A village with no militia sends its goods out unescorted, and one
    /// whose escort is ridden down by bandits is left that many defenders short until it trains
    /// replacements -- the same men are either on the walls or on the road, never both.
    /// </summary>
    internal static class VillagerEscort
    {
        // One militiaman per this much cargo value, up to MaxEscort guards.
        private const int GoldPerEscort = 400;
        private const int MaxEscort = 12;

        // Escort composition: two melee militia per ranged one.
        private const int MeleePerRanged = 2;

        // Cargo value at which elite militia start appearing in the escort, and the value at which
        // the whole escort is elite. Between the two the elite share ramps linearly, so a grain run
        // is guarded by ordinary militia and a silver or warhorse run by veterans.
        private const int EliteStartValue = 2000;
        private const int EliteFullValue = 8000;

        [HarmonyPatch(typeof(VillagerCampaignBehavior), "LoadAndSendVillagerParty")]
        private static class LoadAndSendVillagerPartyPatch
        {
            private static void Postfix(Village village, MobileParty villagerParty)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || village == null || villagerParty == null)
                {
                    return;
                }

                string escortNote = AddEscort(village, villagerParty);

                // The whole dispatch -- party, cargo, escort -- written down once the guards are aboard.
                VillagerDispatchLog.LogDispatch(village, villagerParty, escortNote);

                // Note the dispatch in the village's Ledger history for the day it happened.
                RBMVillageLedger.AddEvent(village.Settlement, RBMVillageLedger.EvDispatch);
            }
        }

        // Notes in the Ledger when a village's convoy reaches its trade-bound town. Keyed to the
        // origin village (mobileParty.HomeSettlement), not the town, so it shows in that village's
        // history. Separate from the homecoming patch above, which fires on return to the village.
        [HarmonyPatch(typeof(VillagerCampaignBehavior), "OnSettlementEntered")]
        private static class ArrivalLedgerPatch
        {
            private static void Postfix(MobileParty mobileParty, Settlement settlement)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || mobileParty == null || !mobileParty.IsVillager
                    || settlement == null || !settlement.IsTown
                    || mobileParty.HomeSettlement == null || !mobileParty.HomeSettlement.IsVillage)
                {
                    return;
                }

                RBMVillageLedger.AddEvent(mobileParty.HomeSettlement, RBMVillageLedger.EvArrive);
            }
        }

        /// <summary>
        /// Puts the guard back on the walls when the convoy reaches home.
        ///
        /// Every militiaman still standing is taken out of the convoy roster and credited back to
        /// <c>Settlement.Militia</c>, wounded included -- they walked home, they can hold a gate while
        /// they mend. Men lost on the road simply never come back, which is the whole point of the
        /// loan: the village bears the cost of its own escorting. Convoys that are wiped out or
        /// destroyed away from home return nothing.
        /// </summary>
        [HarmonyPatch(typeof(VillagerCampaignBehavior), "OnSettlementEntered")]
        private static class EscortHomecomingPatch
        {
            private static void Postfix(MobileParty mobileParty, Settlement settlement)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || mobileParty == null || !mobileParty.IsVillager
                    || settlement == null || !settlement.IsVillage || settlement.Village == null
                    || mobileParty.HomeSettlement != settlement)
                {
                    return;
                }

                ReturnEscort(settlement.Village, mobileParty);
            }
        }

        /// <summary>
        /// Number of militia currently riding in a villager party as its borrowed escort (0 if none).
        /// Lets the Ledger report a village's TOTAL militia -- men at home plus men out guarding convoys.
        /// </summary>
        internal static int CountEscortMilitia(MobileParty villagerParty)
        {
            if (villagerParty == null || villagerParty.HomeSettlement == null)
            {
                return 0;
            }
            CultureObject culture = villagerParty.HomeSettlement.Culture;
            if (culture == null)
            {
                return 0;
            }
            CharacterObject[] escortTroops =
            {
                culture.MeleeMilitiaTroop,
                culture.RangedMilitiaTroop,
                culture.MeleeEliteMilitiaTroop,
                culture.RangedEliteMilitiaTroop
            };
            TroopRoster roster = villagerParty.MemberRoster;
            if (roster == null)
            {
                return 0;
            }
            int count = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                CharacterObject character = roster.GetCharacterAtIndex(i);
                if (character == null)
                {
                    continue;
                }
                for (int j = 0; j < escortTroops.Length; j++)
                {
                    if (character == escortTroops[j])
                    {
                        count += roster.GetElementNumber(i);
                        break;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Strips the militia back out of a convoy roster and hands them to the village. Safe to call on
        /// a convoy that never carried an escort.
        /// </summary>
        private static void ReturnEscort(Village village, MobileParty villagerParty)
        {
            CultureObject culture = village.Settlement.Culture;
            if (culture == null)
            {
                return;
            }

            CharacterObject[] escortTroops =
            {
                culture.MeleeMilitiaTroop,
                culture.RangedMilitiaTroop,
                culture.MeleeEliteMilitiaTroop,
                culture.RangedEliteMilitiaTroop
            };

            TroopRoster roster = villagerParty.MemberRoster;
            int returned = 0;
            // Backwards: RemoveTroop drops emptied elements out of the roster and shifts the rest down.
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                CharacterObject character = roster.GetCharacterAtIndex(i);
                if (character == null)
                {
                    continue;
                }
                for (int j = 0; j < escortTroops.Length; j++)
                {
                    if (character != escortTroops[j])
                    {
                        continue;
                    }

                    int count = roster.GetElementNumber(i);
                    if (count > 0)
                    {
                        roster.RemoveTroop(character, count);
                        returned += count;
                    }
                    break;
                }
            }

            if (returned <= 0)
            {
                return;
            }

            float before = village.Settlement.Militia;
            village.Settlement.Militia = before + returned;

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("ESCORT", village.Settlement.Name != null ? village.Settlement.Name.ToString() : village.Settlement.StringId,
                    "escort home  ·  " + returned + " militia returned"
                    + "  ·  militia " + (int)before + " → " + (int)village.Settlement.Militia);
            }
        }

        /// <summary>
        /// Returns a one-line account of the guard it added, for the economy log, or null when it added
        /// none. The escort is decided from figures (the cargo's worth, the guard already aboard) that
        /// exist only inside this method, so it is described here and printed by the caller.
        /// </summary>
        private static string AddEscort(Village village, MobileParty villagerParty)
        {
            CultureObject culture = village.Settlement.Culture;
            if (culture == null)
            {
                return null;
            }

            CharacterObject melee = culture.MeleeMilitiaTroop;
            CharacterObject ranged = culture.RangedMilitiaTroop;
            CharacterObject eliteMelee = culture.MeleeEliteMilitiaTroop;
            CharacterObject eliteRanged = culture.RangedEliteMilitiaTroop;
            if (melee == null && ranged == null && eliteMelee == null && eliteRanged == null)
            {
                return null;
            }

            int cargoValue = villagerParty.Party.ItemRoster.TotalValue;
            int desired = MathF.Min(cargoValue / GoldPerEscort, MaxEscort);
            int missing = desired - CountEscort(villagerParty.MemberRoster, melee, ranged, eliteMelee, eliteRanged);
            if (missing <= 0)
            {
                return null;
            }

            // The guard is a loan from the standing militia -- the village can only send out men it
            // actually has under arms, and it keeps none back: an empty militia means an unescorted run.
            int available = (int)village.Settlement.Militia;
            missing = MathF.Min(missing, available);
            if (missing <= 0)
            {
                return null;
            }

            // The elite share applies to the guards being added now. Militia already aboard keep
            // whatever tier they were recruited at -- a party is upgraded by attrition, not retrained.
            float eliteShare = MathF.Clamp((float)(cargoValue - EliteStartValue) / (EliteFullValue - EliteStartValue), 0f, 1f);
            int eliteCount = MathF.Round(missing * eliteShare);
            if (eliteMelee == null && eliteRanged == null)
            {
                eliteCount = 0;
            }
            else if (melee == null && ranged == null)
            {
                eliteCount = missing;
            }

            AddTier(villagerParty.MemberRoster, eliteMelee, eliteRanged, eliteCount);
            AddTier(villagerParty.MemberRoster, melee, ranged, missing - eliteCount);

            // Taken off the walls, not off the population -- Hearth is untouched, the militia pays.
            village.Settlement.Militia = MathF.Max(0f, village.Settlement.Militia - missing);

            if (!EconomyLog.IsEnabled)
            {
                return null;
            }
            return "+" + missing + " militia (" + eliteCount + " elite)"
                + " for cargo worth " + cargoValue + "d"
                + "  ·  target guard " + desired + " of max " + MaxEscort
                + ", elite share " + EconomyLog.Fmt(eliteShare)
                + "  ·  borrowed from militia " + available + " → " + (int)village.Settlement.Militia;
        }

        /// <summary>
        /// Adds <paramref name="count"/> militia of one tier, split <see cref="MeleePerRanged"/> melee
        /// to each ranged. If only one of the two troop types exists for the culture it takes the lot.
        /// </summary>
        private static void AddTier(TroopRoster roster, CharacterObject melee, CharacterObject ranged, int count)
        {
            if (count <= 0)
            {
                return;
            }

            int rangedCount = (melee != null && ranged != null) ? count / (MeleePerRanged + 1) : (melee != null ? 0 : count);
            int meleeCount = count - rangedCount;

            if (meleeCount > 0 && melee != null)
            {
                roster.AddToCounts(melee, meleeCount);
            }
            if (rangedCount > 0 && ranged != null)
            {
                roster.AddToCounts(ranged, rangedCount);
            }
        }

        private static int CountEscort(TroopRoster roster, params CharacterObject[] escortTroops)
        {
            int count = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                CharacterObject character = roster.GetCharacterAtIndex(i);
                for (int j = 0; j < escortTroops.Length; j++)
                {
                    if (character == escortTroops[j] && character != null)
                    {
                        count += roster.GetElementNumber(i);
                        break;
                    }
                }
            }
            return count;
        }
    }
}
