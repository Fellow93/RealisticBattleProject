using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// What a man costs the population he is drawn from. Every villager who leaves with a convoy,
    /// every volunteer a notable raises and every recruit a garrison arms comes off the settlement's
    /// head count: <see cref="PopulationPerMan"/> hearth in a village, the same in prosperity in a
    /// town or castle. Militia are exempt -- they are armed locals who never left.
    ///
    /// Vanilla charges villagers half a hearth each and recruits nothing at all.
    /// </summary>
    public static class ManpowerCost
    {
        public const float PopulationPerMan = 0.25f;

        /// <summary>Takes <paramref name="men"/> off the settlement's hearth or prosperity.</summary>
        public static void Charge(Settlement settlement, int men, string reason)
        {
            if (settlement == null || men <= 0)
            {
                return;
            }
            float cost = men * PopulationPerMan;
            if (settlement.IsVillage)
            {
                float before = settlement.Village.Hearth;
                settlement.Village.Hearth = MathF.Max(0f, before - cost);
                LogCharge(settlement, men, reason, "hearth", before, settlement.Village.Hearth);
            }
            else if (settlement.Town != null)
            {
                float before = settlement.Town.Prosperity;
                // Already on RBM's prosperity scale -- keep the discrete-write divisor off it.
                RBMProsperityEquilibrium.EnterScaleExempt();
                try
                {
                    settlement.Town.Prosperity = MathF.Max(0f, before - cost);
                }
                finally
                {
                    RBMProsperityEquilibrium.ExitScaleExempt();
                }
                LogCharge(settlement, men, reason, "prosperity", before, settlement.Town.Prosperity);
            }
        }

        private static void LogCharge(Settlement settlement, int men, string reason, string pool, float before, float after)
        {
            if (!EconomyLog.IsEnabled || settlement == null)
            {
                return;
            }
            EconomyLog.Log("MANPOWER", settlement.Name.ToString(),
                reason + "  ·  " + men + " men"
                + "  ·  " + pool + " " + EconomyLog.Fmt(before) + " -> " + EconomyLog.Fmt(after)
                + "  (-" + EconomyLog.Fmt(before - after) + ")");
        }

        /// <summary>
        /// A fresh volunteer costs his settlement a quarter head. Counted as filled slots before and
        /// after, which is invariant under both the native re-sort and a promotion in place, so only
        /// genuinely new men are charged.
        /// </summary>
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior))]
        [HarmonyPatch("UpdateVolunteersOfNotablesInSettlement")]
        private class ChargeNewVolunteers
        {
            private static void Prefix(Settlement settlement, out int __state)
            {
                __state = CountFilledSlots(settlement);
            }

            private static void Postfix(Settlement settlement, int __state)
            {
                Charge(settlement, CountFilledSlots(settlement) - __state, "volunteers raised");
            }

            private static int CountFilledSlots(Settlement settlement)
            {
                int filled = 0;
                if (settlement == null || settlement.Notables == null)
                {
                    return filled;
                }
                foreach (Hero notable in settlement.Notables)
                {
                    CharacterObject[] volunteers = (notable != null) ? notable.VolunteerTypes : null;
                    if (volunteers == null)
                    {
                        continue;
                    }
                    for (int i = 0; i < volunteers.Length; i++)
                    {
                        if (volunteers[i] != null)
                        {
                            filled++;
                        }
                    }
                }
                return filled;
            }
        }

        // Size of the party VillagerPartyComponent.CreateVillagerParty last built, read back by the
        // behavior-level postfix below, which knows the hearth but not the party.
        private static int _lastCreatedVillagers;

        [HarmonyPatch(typeof(VillagerPartyComponent))]
        [HarmonyPatch("CreateVillagerParty")]
        private class RecordCreatedVillagers
        {
            private static void Postfix(MobileParty __result)
            {
                _lastCreatedVillagers = (__result != null && __result.MemberRoster != null) ? __result.MemberRoster.TotalManCount : 0;
            }
        }

        /// <summary>Replaces the native half-hearth-a-man charge for a new villager party.</summary>
        [HarmonyPatch(typeof(VillagerCampaignBehavior))]
        [HarmonyPatch("CreateVillagerParty")]
        private class ChargeCreatedVillagerParty
        {
            private static void Prefix(Village village, out float __state)
            {
                __state = (village != null) ? village.Hearth : 0f;
                _lastCreatedVillagers = 0;
            }

            private static void Postfix(Village village, float __state)
            {
                if (village != null && _lastCreatedVillagers > 0)
                {
                    village.Hearth = MathF.Max(0f, __state - _lastCreatedVillagers * PopulationPerMan);
                    LogCharge(village.Settlement, _lastCreatedVillagers, "villager party raised", "hearth", __state, village.Hearth);
                }
            }
        }

        /// <summary>Replaces the native half-hearth-a-man charge for topping a villager party up.</summary>
        [HarmonyPatch(typeof(VillagerCampaignBehavior))]
        [HarmonyPatch("AddVillagersToParty")]
        private class ChargeAddedVillagers
        {
            private static void Prefix(MobileParty villagerParty, out float[] __state)
            {
                Village village = (villagerParty != null && villagerParty.HomeSettlement != null) ? villagerParty.HomeSettlement.Village : null;
                __state = (village != null) ? new[] { village.Hearth, villagerParty.MemberRoster.TotalManCount } : null;
            }

            private static void Postfix(MobileParty villagerParty, float[] __state)
            {
                if (__state == null)
                {
                    return;
                }
                int added = MathF.Max(0, villagerParty.MemberRoster.TotalManCount - (int)__state[1]);
                Village village = villagerParty.HomeSettlement.Village;
                village.Hearth = MathF.Max(0f, __state[0] - added * PopulationPerMan);
                if (added > 0)
                {
                    LogCharge(village.Settlement, added, "villager party topped up", "hearth", __state[0], village.Hearth);
                }
            }
        }
    }
}
