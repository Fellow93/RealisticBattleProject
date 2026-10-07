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
    /// whichever wrapper their sign belongs to, display pass only, player clan only. Each line reads the
    /// same projection its <c>CalculateClanGoldChange</c> postfix shows, so the tab's Expected Gold is what
    /// the apply pass will actually do.
    ///
    /// Event-paid gold -- the leader's cut of spoils, the companions' share, mint cuts, gold-paid promotions
    /// -- is deliberately NOT here: it reaches the purse when the event fires, never on the apply pass, so a
    /// recent average of it in the breakdown made Expected Gold promise a daily change the day never paid.
    /// </summary>
    /// <remarks>
    /// Held out of <c>PatchAll</c> and applied from <see cref="ApplyDeferred"/> once a game is live, for the
    /// <c>DefaultClanFinanceModel</c> static-initializer trap <see cref="MercenaryContractPay"/> documents.
    /// </remarks>
    public static class ClanFinanceTabLines
    {
        // Constant label (no text variables), so one shared instance instead of a new TextObject on every
        // Finances-tab refresh. Localized lazily at ToString time, so a language change still applies.
        private static readonly TextObject WealthIncomeText = new TextObject("{=RBM_wealth_income}Settlement wealth tax");

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
        }

        private static bool IsPlayerDisplayPass(Clan clan, bool applyWithdrawals)
        {
            return !applyWithdrawals && clan != null && clan == Clan.PlayerClan
                && RBMConfig.RBMConfig.rbmCampaignEnabled;
        }

        /// <summary>The Finances tab's income total: RBM's revenue lines.</summary>
        private static void IncomePostfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
        {
            if (!IsPlayerDisplayPass(clan, applyWithdrawals))
            {
                return;
            }
            int wealthTax = WealthTax.ProjectNextOwnerPayment(clan);
            if (wealthTax > 0)
            {
                __result.Add(wealthTax, WealthIncomeText);
            }
            MercenaryContractPay.AddDisplayLines(clan, ref __result, income: true, expense: false);
        }

        /// <summary>The Finances tab's expense total: RBM's cost lines.</summary>
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
            int subsidies = GarrisonSubsidy.ProjectNextOwnerMaintenance(clan);
            if (subsidies > 0)
            {
                __result.Add(-subsidies, GarrisonSubsidyFinanceLine.Label());
            }
            MercenaryContractPay.AddDisplayLines(clan, ref __result, income: false, expense: true);
        }
    }
}
