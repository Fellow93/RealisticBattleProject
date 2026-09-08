using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Makes the Clan screen's Finances tab agree with the denar tooltip.
    ///
    /// Every RBM finance line -- settlement wealth tax, troop maintenance, garrison subsidies, the mercenary
    /// contract -- is fed into <c>DefaultClanFinanceModel.CalculateClanGoldChange</c>, which is what the map
    /// bar's gold tooltip and the daily apply pass read. But the Finances tab's "Total Income" / "Total
    /// Expenses" / "Expected Gold" figures come from the two SEPARATE wrappers <c>CalculateClanIncome</c> and
    /// <c>CalculateClanExpenses</c> (<c>ClanManagementVM.RefreshDailyValues</c>), which none of those
    /// postfixes touch, so the tab silently omitted all of RBM's money. This routes the same lines into
    /// whichever wrapper their sign belongs to, display pass only, player clan only.
    ///
    /// It also books the event-paid gold <see cref="ClanEventGoldLedger"/> averages -- the leader's cut of
    /// spoils, the companions' share, mint cuts, gold-paid promotions -- into all three calls, so the sources
    /// the player gets most of his gold from finally show on the breakdown.
    /// </summary>
    /// <remarks>
    /// Held out of <c>PatchAll</c> and applied from <see cref="ApplyDeferred"/> once a game is live, for the
    /// <c>DefaultClanFinanceModel</c> static-initializer trap <see cref="MercenaryContractPay"/> documents.
    /// </remarks>
    public static class ClanFinanceTabLines
    {
        public static void ApplyDeferred(Harmony harmony)
        {
            if (Game.Current == null)
            {
                return;
            }
            RuntimeHelpers.RunClassConstructor(typeof(DefaultClanFinanceModel).TypeHandle);

            harmony.Patch(
                AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateClanIncome"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ClanFinanceTabLines), nameof(IncomePostfix))));
            harmony.Patch(
                AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateClanExpenses"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ClanFinanceTabLines), nameof(ExpensesPostfix))));
            harmony.Patch(
                AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ClanFinanceTabLines), nameof(GoldChangePostfix))));
        }

        private static bool IsPlayerDisplayPass(Clan clan, bool applyWithdrawals)
        {
            return !applyWithdrawals && clan != null && clan == Clan.PlayerClan
                && RBMConfig.RBMConfig.rbmCampaignEnabled;
        }

        /// <summary>The Finances tab's income total: RBM's revenue lines plus the averaged event gains.</summary>
        private static void IncomePostfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
        {
            if (!IsPlayerDisplayPass(clan, applyWithdrawals))
            {
                return;
            }
            int wealthTax = WealthTax.GetClanDailyOwnerIncome(clan);
            if (wealthTax > 0)
            {
                __result.Add(wealthTax, new TextObject("{=RBM_wealth_income}Settlement wealth tax"));
            }
            MercenaryContractPay.AddDisplayLines(clan, ref __result, income: true, expense: false);
            ClanEventGoldLedger.AddDisplayLines(ref __result, income: true, expense: false);
        }

        /// <summary>The Finances tab's expense total: RBM's cost lines plus the averaged event drains.</summary>
        private static void ExpensesPostfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
        {
            if (!IsPlayerDisplayPass(clan, applyWithdrawals))
            {
                return;
            }
            if (SpoilsPool.IsEnabled && RBMConfig.RBMConfig.troopMaintenanceFraction > 0f)
            {
                MaintenanceResult projected = SpoilsPool.ChargeClanMaintenance(clan, apply: false);
                SpoilsPool.AddMaintenanceBreakdown(ref __result, projected, -1f);
            }
            int subsidies = GarrisonSubsidy.PaidTodayBy(clan);
            if (subsidies > 0)
            {
                __result.Add(-subsidies, new TextObject("{=rbm_garr_subsidy_line}Garrison subsidies"));
            }
            MercenaryContractPay.AddDisplayLines(clan, ref __result, income: false, expense: true);
            ClanEventGoldLedger.AddDisplayLines(ref __result, income: false, expense: true);
        }

        /// <summary>
        /// The denar tooltip and Daily Gold Change breakdown. Only the event averages go here -- the other
        /// RBM lines already reach this call through their own postfixes.
        /// </summary>
        private static void GoldChangePostfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
        {
            if (!IsPlayerDisplayPass(clan, applyWithdrawals))
            {
                return;
            }
            ClanEventGoldLedger.AddDisplayLines(ref __result, income: true, expense: true);
        }
    }
}
