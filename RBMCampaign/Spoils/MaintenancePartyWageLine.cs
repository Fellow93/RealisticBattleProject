using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace RBMCampaign
{
    /// <summary>
    /// Adds the day's maintenance to a party's own expense tooltip -- the wage breakdown the Clan
    /// Management parties tab and the map's party-wage indicator both draw. The clan finance line
    /// (see <see cref="MaintenanceFinanceLine"/>) accounts for it at the treasury; this shows it beside
    /// wages on the party it is charged to, so a single party's upkeep reads where the player looks for it.
    /// </summary>
    public static class MaintenancePartyWageLine
    {
        /// <summary>
        /// The party-wage tooltip renders <c>MobileParty.TotalWageExplained</c>, which asks
        /// <see cref="DefaultPartyWageModel.GetTotalWage"/> for the wage with its descriptions. The number
        /// the economy actually withdraws asks the same method without them, so gating on
        /// <c>IncludeDescriptions</c> keeps this a display-only line: it shows in the tooltip and never
        /// touches the wage the party is charged.
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartyWageModel), "GetTotalWage")]
        private class ShowMaintenanceOnPartyWage
        {
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                if (!__result.IncludeDescriptions || !SpoilsPool.IsEnabled)
                {
                    return;
                }
                if (RBMConfig.RBMConfig.troopMaintenanceFraction <= 0f || mobileParty == null)
                {
                    return;
                }
                MaintenanceResult m = SpoilsPool.ProjectDailyMaintenance(mobileParty.Party);
                if (m.Total <= 0)
                {
                    return;
                }
                // Only the leader-paid remainder is real gold on top of wages; the spoils-met share is shown
                // in the line's text, not added to the total, so the tooltip's foot stays the coin the party
                // truly lays out.
                __result.Add(-m.Shortfall, SpoilsPool.BuildMaintenanceLineText(m.Total, m.Covered));
            }
        }
    }
}
