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
    /// stacking a fresh escort every run. Militia cost Hearth at the same rate vanilla charges for
    /// villagers -- they are the same villagers, just carrying a spear.
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

            // Never strip the village below the population the guards are drawn from.
            missing = MathF.Min(missing, (int)village.Hearth);
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

            // Same Hearth price vanilla pays per villager pulled into the party.
            village.Hearth = MathF.Max(0f, village.Hearth - (missing + 1) / 2);

            if (!EconomyLog.IsEnabled)
            {
                return null;
            }
            return "+" + missing + " militia (" + eliteCount + " elite)"
                + " for cargo worth " + cargoValue + "d"
                + "  ·  target guard " + desired + " of max " + MaxEscort
                + ", elite share " + EconomyLog.Fmt(eliteShare);
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
