using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBM
{
    /// <summary>
    /// Crash guard for a patrol vanilla has no template for. RBM Campaign's settlement patrols raise patrols for
    /// castles and for towns without a Guard House; with that feature (or the whole campaign module) switched off
    /// later, such a patrol is still on the map until the day's keep-gate stands it down -- and a town's still has
    /// a size limit (vanilla gives 10 for a level-0 Guard House building), so one under it resting at home makes
    /// vanilla's hourly roll call <c>PatrolPartiesCampaignBehavior.ReplenishParty</c>, whose template comes back
    /// null and <c>FindAppropriateInitialRosterForMobileParty</c> throws. This skips that one call.
    ///
    /// In the main module, patched by <c>com.rbmmain</c>'s <c>PatchAll</c> on every game start, so it holds with
    /// RBM Campaign off. It cannot help once RBM itself is removed from a save that still carries such a patrol.
    ///
    /// Runs last and only reads: when RBM Campaign's own refill prefix has already taken the call
    /// (<c>__runOriginal</c> false) it does nothing, and with a template it lets vanilla run.
    /// </summary>
    [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "ReplenishParty")]
    internal static class PatrolReplenishGuard
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(MobileParty party, bool __runOriginal)
        {
            if (!__runOriginal)
            {
                return false;
            }
            if (party == null || party.PatrolPartyComponent == null || Campaign.Current == null)
            {
                return true;
            }
            // The settlement vanilla asks the template for (its caller only refills a patrol resting at home).
            Settlement settlement = party.CurrentSettlement;
            return settlement != null
                && Campaign.Current.Models.SettlementPatrolModel.GetPartyTemplateForPatrolParty(settlement, party.PatrolPartyComponent.IsNaval) != null;
        }
    }
}
