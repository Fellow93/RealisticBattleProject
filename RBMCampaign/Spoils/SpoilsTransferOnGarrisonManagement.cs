using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// The men a lord's party leaves in a fortification's garrison, or takes out of it, as it enters --
    /// vanilla's own garrison management for AI parties and armies, and RBM's surplus top-up
    /// (<see cref="RBMGarrisonRefillBehavior"/>), which moves its men through the same
    /// <c>TakeTroopsFromGarrison</c> -- carry their share of their stack's purse with them, as men moved on
    /// the party screen do (<see cref="SpoilsTransferOnPartyScreen"/>). Without this the share stayed with
    /// the party they left, handed to whichever men of the same troop remained there, and the movers
    /// arrived penniless. Vanilla picks the men one at a time by weight, so the source roster is diffed
    /// across the whole call and each troop's purse is split once, against its stack before the call.
    /// </summary>
    public static class SpoilsTransferOnGarrisonManagement
    {
        [HarmonyPatch(typeof(GarrisonTroopsCampaignBehavior), "LeaveTroopsToGarrison")]
        private static class LeaveTroopsToGarrisonSpoilsPatch
        {
            private static void Prefix(MobileParty mobileParty, out Dictionary<CharacterObject, int> __state)
            {
                __state = SpoilsPool.IsEnabled ? SpoilsPool.SnapshotStacks(mobileParty?.Party) : null;
            }

            private static void Postfix(MobileParty mobileParty, Settlement settlement, Dictionary<CharacterObject, int> __state)
            {
                // The garrison party may have been created inside the call, so it is read only now.
                MoveLeavers(mobileParty?.Party, settlement?.Town?.GarrisonParty?.Party, __state);
            }
        }

        [HarmonyPatch(typeof(GarrisonTroopsCampaignBehavior), "TakeTroopsFromGarrison")]
        private static class TakeTroopsFromGarrisonSpoilsPatch
        {
            private static void Prefix(Settlement settlement, out Dictionary<CharacterObject, int> __state)
            {
                __state = SpoilsPool.IsEnabled ? SpoilsPool.SnapshotStacks(settlement?.Town?.GarrisonParty?.Party) : null;
            }

            private static void Postfix(MobileParty mobileParty, Settlement settlement, Dictionary<CharacterObject, int> __state)
            {
                MoveLeavers(settlement?.Town?.GarrisonParty?.Party, mobileParty?.Party, __state);
            }
        }

        /// <summary>
        /// Moves, for every troop <paramref name="from"/> has fewer of than in <paramref name="before"/>, the
        /// leavers' share of its purse over to <paramref name="to"/>. The call moves men only between the
        /// two, so whatever the source lost went to the destination.
        /// </summary>
        private static void MoveLeavers(PartyBase from, PartyBase to, Dictionary<CharacterObject, int> before)
        {
            if (before == null || before.Count == 0 || from == null || to == null)
            {
                return;
            }
            int carried = 0;
            int moved = 0;
            foreach (KeyValuePair<CharacterObject, int> stack in before)
            {
                int left = stack.Value - SpoilsPool.GetStackSize(from, stack.Key);
                if (left <= 0)
                {
                    continue;
                }
                carried += SpoilsPool.TransferSpoils(from, to, stack.Key, left);
                moved += left;
            }
            if (carried > 0 && SpoilsLog.Verbose && (from == PartyBase.MainParty || to == PartyBase.MainParty))
            {
                SpoilsLog.LogVerbose("TRANSFER", from, SpoilsLog.Describe(from) + " -> " + SpoilsLog.Describe(to)
                    + ": " + moved + " men carried " + carried + " spoils");
            }
        }
    }
}
