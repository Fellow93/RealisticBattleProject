using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The player pays his hired mercenary companies through his clan's daily finances: one expense line per
    /// company ("Mercenary company: {NAME}") in <c>DefaultClanFinanceModel.CalculateClanGoldChange</c>, so the
    /// denar tooltip, the Clan screen's Finances tab (via <see cref="ClanFinanceTabLines"/>) and the RBM
    /// Ledger's Clan finances record (which reads the apply pass's lines by label) all show it. Both passes list
    /// the same companies (<see cref="MercenaryContracts.PlayerPaidContracts"/>) and run the same affordability
    /// walk, so the projection is what the apply pass does.
    ///
    /// On the apply pass each company's leader is credited exactly its line (a null-giver GiveGoldAction), and
    /// the line itself is what takes the gold off the player: vanilla turns the day's total into one
    /// GiveGoldAction for the clan leader, so the coin moves from the player to the company, none minted. A
    /// company the player cannot pay (his gold, less the companies already charged before it, short of its
    /// fee) gets no line and its contract ends; it leaves the kingdom on the next hourly reconcile, never from
    /// inside the finance computation. AI payers keep paying by direct transfer (MercenaryContracts.PayDaily).
    /// </summary>
    public static class MercenaryCompanyPayLines
    {
        /// <summary>
        /// Adds the lines to <paramref name="result"/> (player clan only). With <paramref name="apply"/> the
        /// companies are also paid and unpaid contracts ended.
        /// </summary>
        public static void AddPlayerPayLines(Clan clan, ref ExplainedNumber result, bool apply)
        {
            if (clan == null || clan != Clan.PlayerClan || !MercenaryCompanies.IsEnabled || Hero.MainHero == null)
            {
                return;
            }
            List<(Clan clan, int pay)> contracts = MercenaryContracts.PlayerPaidContracts();
            if (contracts.Count == 0)
            {
                return;
            }
            int available = Hero.MainHero.Gold;
            foreach ((Clan company, int pay) in contracts)
            {
                if (pay > available)
                {
                    if (apply)
                    {
                        MercenaryContracts.EndUnpaidByPlayer(company, pay, available);
                    }
                    continue;
                }
                available -= pay;
                result.Add(-pay, Label(company));
                if (apply)
                {
                    GiveGoldAction.ApplyBetweenCharacters(null, company.Leader, pay, disableNotification: true);
                    MercenaryContracts.LogPlayerPay(company, pay);
                }
            }
        }

        private static TextObject Label(Clan company)
        {
            TextObject label = new TextObject("{=RBM_merc_pay_line}Mercenary company: {NAME}");
            label.SetTextVariable("NAME", company.Name);
            return label;
        }
    }
}
