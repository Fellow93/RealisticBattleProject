using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace RBMCampaign
{
    /// <summary>
    /// Runs the day's troop maintenance through the clan's daily gold change. On the model's apply pass --
    /// the one authoritative call each day (`ClanVariablesCampaignBehavior.DailyTickClan`) whose result is
    /// handed to the leader as gold and shown in the "Daily Gold Change" message -- it drains the men's
    /// spoils for their share and folds the shortfall into the number, so the leader pays it once, in plain
    /// sight, beside wages. On every display pass it projects the same figure without moving a coin, so the
    /// denar tooltip and the clan finance screen read a line that matches what the day will actually cost.
    /// </summary>
    public static class MaintenanceFinanceLine
    {
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange")]
        private class ChargeMaintenanceThroughFinance
        {
            /// <summary>
            /// <paramref name="applyWithdrawals"/> is true on exactly one call per clan per day -- the daily
            /// tick that applies the result to the leader's gold. Only there is the spoils drain done, so it
            /// happens once; every other call is a display and merely reads the projection. The shortfall is
            /// added to <paramref name="__result"/> as a negative, the same as any expense, so it lowers the
            /// applied gold change (real payment) and, on the display pass, the breakdown the player reads.
            /// </summary>
            private static void Postfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
            {
                if (clan == null || !SpoilsPool.IsEnabled || RBMConfig.RBMConfig.troopMaintenanceFraction <= 0f)
                {
                    return;
                }

                if (applyWithdrawals)
                {
                    // The authoritative once-a-day pass: drain the purses and charge the remainder to the
                    // clan through its daily gold change, so it lands in the Daily Gold Change message. The
                    // cost and its spoils credit net to the shortfall; descriptions are off here, so only
                    // that net reaches the number.
                    MaintenanceResult charged = SpoilsPool.ChargeClanMaintenance(clan, apply: true);
                    SpoilsPool.AddMaintenanceBreakdown(ref __result, charged, -1f);
                    return;
                }

                // Display only, and only the player reads a finance breakdown. Projected, never drained; the
                // maintenance cost and the spoils that met it both show, so a stack whose purse covers its
                // upkeep in full still reads its maintenance rather than vanishing.
                if (clan != Clan.PlayerClan)
                {
                    return;
                }
                MaintenanceResult projected = SpoilsPool.ChargeClanMaintenance(clan, apply: false);
                SpoilsPool.AddMaintenanceBreakdown(ref __result, projected, -1f);
            }
        }
    }
}
