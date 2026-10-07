using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Pays a lord his wealth-tax and castle-surplus income through the clan finance model, and shows it
    /// on the finance breakdown. <see cref="WealthTax"/> debits the levy from the market on the settlement
    /// daily tick and books the owner's share to a pending pool; this hands that pool over on the clan's
    /// finance apply pass -- the once-a-day call whose result becomes the leader's gold -- so the income
    /// lands in the Daily Gold Change beside every other clan revenue. Before this the money was paid
    /// straight to gold via <c>ChangeHeroGold</c>, which the finance model never saw, so the screen and
    /// the daily message both understated a fief's real yield.
    /// </summary>
    /// <remarks>
    /// The apply leg runs for EVERY clan, not just the player: every clan's fiefs accrue this income and
    /// must be paid it, exactly as <see cref="MaintenanceFinanceLine"/> charges every clan's maintenance
    /// on the apply pass. Consuming the pool there is what turns it into gold, and it is drained once so
    /// the pay happens once. The display leg is player-only and moves no coin: it shows what the NEXT
    /// apply pass will pay (<see cref="WealthTax.ProjectNextOwnerPayment"/>) -- the levies already in the
    /// pool, plus the owner's share of the next levy of every fief that has not ticked since the clan was
    /// last paid, projected off its current balance by the same formula the levy uses. Not yesterday's
    /// take: a hoarding town's surplus shrinks by a fifth a day, so that figure ran a quarter high.
    /// </remarks>
    public static class SettlementIncomeFinanceLine
    {
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange")]
        private class PaySettlementIncomeThroughFinance
        {
            private static void Postfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
            {
                if (clan == null || !RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }

                if (applyWithdrawals)
                {
                    // The authoritative once-a-day pass: hand the clan everything its fiefs have accrued
                    // since it was last paid and empty the pool, so the finance model turns it into the
                    // leader's gold. A positive contribution, the sign revenue is counted with.
                    int paid = WealthTax.ConsumePendingOwnerIncome(clan);
                    if (paid != 0)
                    {
                        __result.Add(paid, LabelText);
                    }
                    return;
                }

                // Display only, and only the player reads a finance breakdown. What the next apply pass
                // will pay; nothing is consumed and no coin moves. Named apart from vanilla's trade-based
                // "Settlement Income" line so the two read as the distinct taxes they are (this the stock
                // levy, that the trade one).
                if (clan != Clan.PlayerClan)
                {
                    return;
                }
                int income = WealthTax.ProjectNextOwnerPayment(clan);
                if (income > 0)
                {
                    __result.Add(income, LabelText);
                }
            }
        }

        // Built once: the breakdown is rebuilt several times a second by the map bar's denar tooltip, and a
        // constant label (no variables) is safe to share -- it still localises on every ToString.
        private static readonly TextObject LabelText = new TextObject("{=RBM_wealth_income}Settlement wealth tax");
    }
}
