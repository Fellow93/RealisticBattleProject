using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Shows the player what his fiefs are costing him beyond their garrisons' wages.
    ///
    /// A garrison's WAGE residual already reaches the clan finance screen on vanilla's own
    /// "{SETTLEMENT} Garrison" line, because it flows through <c>CalculatePartyWage</c>. The other two
    /// legs of a subsidy -- the day's kit maintenance and a garrison's promotions (see
    /// <see cref="GarrisonSubsidy"/>) -- are charged straight against the leader's gold outside the
    /// finance model, so without this they would drain the player's purse invisibly and he would be left
    /// wondering where the money went.
    /// </summary>
    public static class GarrisonSubsidyFinanceLine
    {
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange")]
        private class ShowGarrisonSubsidies
        {
            /// <summary>
            /// Display passes only, and only the player's own clan reads a breakdown. Never on the apply
            /// pass: the subsidy has already been taken from the leader's gold by the time it is tallied,
            /// so charging it again here would take it twice. As a projection the line is honest --
            /// today's subsidies are the best guess at tomorrow's, and they are a real standing cost that
            /// vanilla's breakdown would otherwise hide entirely.
            /// </summary>
            private static void Postfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
            {
                if (applyWithdrawals || clan == null || clan != Clan.PlayerClan
                    || !RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }
                int paid = GarrisonSubsidy.PaidTodayBy(clan);
                if (paid > 0)
                {
                    __result.Add(-paid, new TextObject("{=rbm_garr_subsidy_line}Garrison subsidies"));
                }
            }
        }
    }
}
