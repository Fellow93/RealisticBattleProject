using HarmonyLib;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.ClanFinance;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Shows the player what their workshop actually pays its hands.
    ///
    /// The clan-screen workshop card lists a "Daily Wage" of the standing overhead and nothing else,
    /// while <see cref="RBMWorkshopExpense"/> also takes the hands' share out of every sale. A busy shop
    /// could hand over half its sales to wages the card never mentioned, so its capital grew slower than
    /// any figure on screen explained. This adds a "Production Wages" row under the vanilla one,
    /// reporting the shop's share rate and the last day's batches and salary.
    /// </summary>
    [HarmonyPatch(typeof(ClanFinanceWorkshopItemVM), "PopulateStatsList")]
    public static class WorkshopCardPayrollLine
    {
        private static void Postfix(ClanFinanceWorkshopItemVM __instance)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || __instance == null || __instance.ItemProperties == null)
            {
                return;
            }
            Workshop shop = __instance.Workshop;
            if (shop == null || shop.WorkshopType == null || shop.WorkshopType.IsHidden)
            {
                return;
            }

            int cycles;
            int paid;
            bool known = RBMWorkshopExpense.TryGetLastPayroll(shop, out cycles, out paid);
            int rate = (int)(RBMWorkshopExpense.SalaryShare(shop) * 100f + 0.5f);
            int threshold = RBMWorkshopExpense.SalaryCapitalThreshold;

            string name = new TextObject("{=RBM_wsPayroll}Production Wages").ToString();
            string value = known ? paid.ToString() : "-";

            BasicTooltipViewModel hint = new BasicTooltipViewModel(delegate
            {
                TextObject text = known
                    ? new TextObject("{=RBM_wsPayrollHint}Paid to the townspeople who work the shop as {RATE}% of every sale, while its capital is above {THRESHOLD}. Last day: {CYCLES} batches, {PAID} denars. This is on top of the daily wage above.")
                    : new TextObject("{=RBM_wsPayrollHintIdle}Paid to the townspeople who work the shop as {RATE}% of every sale, while its capital is above {THRESHOLD}, on top of the daily wage above. No batch has run yet this session.");
                text.SetTextVariable("RATE", rate);
                text.SetTextVariable("THRESHOLD", threshold);
                text.SetTextVariable("CYCLES", cycles);
                text.SetTextVariable("PAID", paid);
                return text.ToString();
            });

            __instance.ItemProperties.Add(new SelectableItemPropertyVM(name, value, false, hint));
        }
    }
}
