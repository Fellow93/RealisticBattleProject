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
    /// The clan-screen workshop card lists a "Daily Wage" of the vanilla flat overhead (100/day) and
    /// nothing else, while <see cref="WorkshopPurse"/> also draws a per-batch payroll out of the same
    /// capital. A busy shop could lose several hundred a day to wages the card never mentioned, so its
    /// capital fell faster than any figure on screen explained. This adds a "Production Wages" row
    /// under the vanilla one, reporting the last day's batches and what they cost.
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
            bool known = WorkshopPurse.TryGetLastPayroll(shop, out cycles, out paid);
            int rate = WorkshopPurse.WagePerCycle;

            string name = new TextObject("{=RBM_wsPayroll}Production Wages").ToString();
            string value = known ? paid.ToString() : "-";

            BasicTooltipViewModel hint = new BasicTooltipViewModel(delegate
            {
                TextObject text = known
                    ? new TextObject("{=RBM_wsPayrollHint}Paid from the workshop's capital to the townspeople who work it: {RATE} denars per batch. Last day: {CYCLES} batches, {PAID} denars. This is on top of the daily wage above.")
                    : new TextObject("{=RBM_wsPayrollHintIdle}Paid from the workshop's capital to the townspeople who work it: {RATE} denars per batch, on top of the daily wage above. No batch has run yet this session.");
                text.SetTextVariable("RATE", rate);
                text.SetTextVariable("CYCLES", cycles);
                text.SetTextVariable("PAID", paid);
                return text.ToString();
            });

            __instance.ItemProperties.Add(new SelectableItemPropertyVM(name, value, false, hint));
        }
    }
}
