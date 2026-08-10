using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Removes the garrison wage cap on AI-owned fortifications, so an AI clan's garrisons are sized by
    /// the ideal-strength model rather than throttled by an economy-scaled payment limit.
    ///
    /// Vanilla's <c>ClanVariablesCampaignBehavior.UpdateClanSettlementsPaymentLimit</c> sets each AI
    /// fief's <c>GarrisonWagePaymentLimit</c> to a small value derived from the clan's economy,
    /// prosperity and food. A limited wage does two things downstream: it caps the garrison's ideal
    /// strength (<c>DefaultSettlementGarrisonModel</c>, <c>PaymentLimit / AverageWage</c>) and it forces
    /// wage-driven desertion whenever the running wage exceeds the limit (<c>DefaultPartyDesertionModel</c>).
    /// The net effect is AI garrisons that bleed men and never fill out.
    ///
    /// Lifting the limit to the wage model's maximum makes <c>MobileParty.HasLimitedWage()</c> return
    /// false for these garrisons, exactly as it already does for the player's, so they fall back to the
    /// ideal-strength model and stop deserting for wage. The men are still paid in full -- under the
    /// ledger that bill lands on the fief's own treasury (see <see cref="GarrisonUpkeep"/>), so a garrison
    /// a fief cannot afford simply pressures its wealth rather than shedding troops.
    ///
    /// Patched at the update method, which runs on the daily/weekly clan tick and at new-game creation for
    /// every non-player clan, rather than at the getter -- one write per fief per tick, at the single point
    /// vanilla lowers the value, instead of an override on a hot property read.
    /// </summary>
    [HarmonyPatch(typeof(ClanVariablesCampaignBehavior), "UpdateClanSettlementsPaymentLimit")]
    internal static class GarrisonWageLimit
    {
        private static void Postfix(Clan clan)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || clan == null || clan == Clan.PlayerClan)
            {
                return;
            }

            int max = Campaign.Current.Models.PartyWageModel.MaxWagePaymentLimit;
            foreach (Town fief in clan.Fiefs)
            {
                if (fief.Settlement != null && fief.Settlement.GarrisonWagePaymentLimit != max)
                {
                    fief.Settlement.SetGarrisonWagePaymentLimit(max);
                }
            }
        }
    }
}
