using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.TownManagement;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Makes the per-fief money the player reads agree with what RBM actually moves.
    ///
    /// The Clan screen's Fiefs tab lists each fief's profit as Taxes + Tariffs - Garrison Wages + villages
    /// + governor effects. Two of those rows are wrong under the ledger:
    ///
    ///   TARIFFS reads the town's accumulated trade commission, which <see cref="TradeTariff"/> zeroes --
    ///   RBM's market fee stays in the fief's own treasury and never reaches the lord. The row was a
    ///   permanent zero. It is replaced by the fief's WEALTH TAX owner share (<see cref="WealthTax"/>),
    ///   the levy that actually pays him, or a castle's surplus skim.
    ///
    ///   GARRISON WAGES reads the garrison party's whole wage bill, but <see cref="GarrisonUpkeep"/> has
    ///   the fief's treasury pay it first and bills the owner only the residual. The row is re-priced to
    ///   that residual, with a hint showing the split.
    ///
    /// The town management screen shows a single "Population Tax" figure; an "Owner's income" row is
    /// added beside it carrying the same wealth-tax share, so a governor's screen tells the lord what the
    /// place is worth to him.
    ///
    /// Display only -- every figure here is a projection the money model already made; nothing moves.
    /// </summary>
    public static class FiefProfitLines
    {
        [HarmonyPatch(typeof(ClanSettlementItemVM), "UpdateProfitProperties")]
        private static class FiefsTabProfit
        {
            private static void Postfix(ClanSettlementItemVM __instance)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || __instance == null || Campaign.Current == null)
                {
                    return;
                }
                Settlement settlement = __instance.Settlement;
                MBBindingList<ProfitItemPropertyVM> rows = __instance.ProfitItemProperties;
                if (settlement == null || settlement.Town == null || rows == null)
                {
                    return;
                }

                // Drop vanilla's dead tariff row and its overstated garrison row.
                for (int i = rows.Count - 1; i >= 0; i--)
                {
                    int type = rows[i].Type;
                    if (type == (int)ProfitItemPropertyVM.PropertyType.Tariff
                        || type == (int)ProfitItemPropertyVM.PropertyType.Garrison)
                    {
                        rows.RemoveAt(i);
                    }
                }

                // The wealth tax, in the tariff's place (after Taxes, which is always the first row).
                int wealthTax = WealthTax.GetSettlementDailyOwnerIncome(settlement);
                if (wealthTax != 0)
                {
                    int at = (rows.Count > 0 && rows[0].Type == (int)ProfitItemPropertyVM.PropertyType.Tax) ? 1 : 0;
                    rows.Insert(at, new ProfitItemPropertyVM(WealthTaxTitle(settlement).ToString(), wealthTax,
                        ProfitItemPropertyVM.PropertyType.Tariff, null,
                        new BasicTooltipViewModel(() => WealthTaxHint(settlement))));
                }

                // The garrison wage the owner is actually billed, before the village rows.
                int wage, fiefPaid, ownerPart;
                GarrisonUpkeep.ProjectWageSplit(settlement, out wage, out fiefPaid, out ownerPart);
                if (ownerPart > 0)
                {
                    int at = rows.Count;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (rows[i].Type == (int)ProfitItemPropertyVM.PropertyType.Village
                            || rows[i].Type == (int)ProfitItemPropertyVM.PropertyType.Governor)
                        {
                            at = i;
                            break;
                        }
                    }
                    rows.Insert(at, new ProfitItemPropertyVM(new TextObject("{=5dkPxmZG}Garrison Wages").ToString(), -ownerPart,
                        ProfitItemPropertyVM.PropertyType.Garrison, null,
                        new BasicTooltipViewModel(() => GarrisonHint(settlement))));
                }

                int total = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    total += rows[i].Value;
                }
                if (__instance.TotalProfit != null)
                {
                    __instance.TotalProfit.Value = total;
                }
            }
        }

        [HarmonyPatch(typeof(TownManagementVM), "RefreshTownManagementStats")]
        private static class TownManagementOwnerIncome
        {
            private static readonly AccessTools.FieldRef<TownManagementVM, Settlement> SettlementRef =
                AccessTools.FieldRefAccess<TownManagementVM, Settlement>("_settlement");

            private static void Postfix(TownManagementVM __instance)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || __instance == null || Campaign.Current == null)
                {
                    return;
                }
                Settlement settlement = SettlementRef(__instance);
                MBBindingList<TownManagementDescriptionItemVM> list = __instance.MiddleFirstTextList;
                if (settlement == null || settlement.Town == null || list == null)
                {
                    return;
                }
                int wealthTax = WealthTax.GetSettlementDailyOwnerIncome(settlement);
                BasicTooltipViewModel hint = new BasicTooltipViewModel(() => WealthTaxHint(settlement));
                // Beside vanilla's Population Tax, which is always the first row.
                int at = (list.Count > 0) ? 1 : 0;
                list.Insert(at, new TownManagementDescriptionItemVM(WealthTaxTitle(settlement), wealthTax, 0,
                    TownManagementDescriptionItemVM.DescriptionType.Gold, hint));
            }
        }

        private static TextObject WealthTaxTitle(Settlement settlement)
        {
            return settlement.IsCastle
                ? new TextObject("{=RBM_fief_castle_surplus}Castle surplus")
                : new TextObject("{=RBM_fief_wealth_tax}Wealth tax");
        }

        private static List<TooltipProperty> WealthTaxHint(Settlement settlement)
        {
            List<TooltipProperty> lines = new List<TooltipProperty>();
            int owner = WealthTax.GetSettlementDailyOwnerIncome(settlement);
            lines.Add(new TooltipProperty("", WealthTaxTitle(settlement).ToString(), 0, false, TooltipProperty.TooltipPropertyFlags.Title));
            if (settlement.IsCastle)
            {
                lines.Add(new TooltipProperty(new TextObject("{=RBM_fief_hint_castle}A tenth of the wealth the castle holds above what it needs, remitted to you each day.").ToString(), " ", 0));
            }
            else
            {
                lines.Add(new TooltipProperty(new TextObject("{=RBM_fief_hint_wealth}A daily levy on the money standing in the market. Your share leaves the town; the fief's own share pays its garrison, militia and clerks. Trade tariffs stay in the fief's treasury.").ToString(), " ", 0));
            }
            lines.Add(new TooltipProperty("", string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.RundownSeperator));
            lines.Add(new TooltipProperty(new TextObject("{=RBM_fief_hint_citizen}Citizen wealth").ToString(), SettlementWealth.GetCitizenWealth(settlement).ToString(), 0));
            lines.Add(new TooltipProperty(new TextObject("{=RBM_wealth_settlement}Settlement wealth").ToString(), SettlementWealth.GetSettlementWealth(settlement).ToString(), 0));
            lines.Add(new TooltipProperty(new TextObject("{=RBM_fief_hint_owner}Paid to you today").ToString(), owner.ToString(), 0));
            return lines;
        }

        private static List<TooltipProperty> GarrisonHint(Settlement settlement)
        {
            int wage, fiefPaid, ownerPart;
            GarrisonUpkeep.ProjectWageSplit(settlement, out wage, out fiefPaid, out ownerPart);
            List<TooltipProperty> lines = new List<TooltipProperty>();
            lines.Add(new TooltipProperty("", new TextObject("{=5dkPxmZG}Garrison Wages").ToString(), 0, false, TooltipProperty.TooltipPropertyFlags.Title));
            lines.Add(new TooltipProperty(new TextObject("{=RBM_fief_hint_garrison}The fief pays its garrison out of its own treasury first; only what the treasury cannot cover is billed to you.").ToString(), " ", 0));
            lines.Add(new TooltipProperty("", string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.RundownSeperator));
            lines.Add(new TooltipProperty(new TextObject("{=RBM_fief_hint_wage}Daily wage bill").ToString(), wage.ToString(), 0));
            lines.Add(new TooltipProperty(new TextObject("{=RBM_fief_hint_fief_pays}Paid by the fief's treasury").ToString(), fiefPaid.ToString(), 0));
            lines.Add(new TooltipProperty(new TextObject("{=RBM_fief_hint_owner_pays}Billed to you").ToString(), ownerPart.ToString(), 0));
            return lines;
        }
    }
}
