using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Every column heading in the ledger's tables.
    ///
    /// These used to be literal Text="..." attributes in RBMLedger.xml, where no language
    /// file can reach them: Gauntlet treats Text= as a plain string and never resolves
    /// {=id}. Routing them through TextObject puts them on the same footing as the rest of
    /// the screen, which has always been localized.
    ///
    /// A widget can only bind to the data source of the scope it sits in, so each heading is
    /// surfaced by whichever view model owns its scope: the screen for the two table headers,
    /// and the row view models for the history tables nested inside a row.
    ///
    /// Resolved on every read rather than cached, so an in-game language switch reaches the
    /// headings the next time the ledger opens, like the rest of the screen.
    /// </summary>
    internal static class RBMLedgerHeaders
    {
        // Villages and Towns tab column headings.
        public static string Village => new TextObject("{=RBM_LEDGER_H_VILLAGE}Village").ToString();
        public static string Production => new TextObject("{=RBM_LEDGER_H_PRODUCTION}Production").ToString();
        public static string Wealth => new TextObject("{=RBM_LEDGER_H_WEALTH}Wealth").ToString();
        public static string Hearth => new TextObject("{=RBM_LEDGER_H_HEARTH}Hearth").ToString();
        public static string Militia => new TextObject("{=RBM_LEDGER_H_MILITIA}Militia").ToString();
        public static string Town => new TextObject("{=RBM_LEDGER_H_TOWN}Town").ToString();
        public static string Prosperity => new TextObject("{=RBM_LEDGER_H_PROSPERITY}Prosperity").ToString();
        public static string Citizen => new TextObject("{=RBM_LEDGER_H_CITIZEN}Citizen").ToString();
        public static string Treasury => new TextObject("{=RBM_LEDGER_H_TREASURY}Treasury").ToString();
        public static string Food => new TextObject("{=RBM_LEDGER_H_FOOD}Food").ToString();
        public static string Garrison => new TextObject("{=RBM_LEDGER_H_GARRISON}Garrison").ToString();

        // Per-row history tables. Abbreviated on purpose: these columns are narrow,
        // so a translation should stay about as short as the English.
        public static string Day => new TextObject("{=RBM_LEDGER_H_DAY}Day").ToString();
        public static string Prod => new TextObject("{=RBM_LEDGER_H_PROD}Prod").ToString();
        public static string Events => new TextObject("{=RBM_LEDGER_H_EVENTS}Events").ToString();
        public static string Prosp => new TextObject("{=RBM_LEDGER_H_PROSP}Prosp").ToString();
        public static string CitIn => new TextObject("{=RBM_LEDGER_H_CIT_IN}Cit In").ToString();
        public static string CitOut => new TextObject("{=RBM_LEDGER_H_CIT_OUT}Cit Out").ToString();
        public static string Treas => new TextObject("{=RBM_LEDGER_H_TREAS}Treas").ToString();
        public static string TrsIn => new TextObject("{=RBM_LEDGER_H_TRS_IN}Trs In").ToString();
        public static string TrsOut => new TextObject("{=RBM_LEDGER_H_TRS_OUT}Trs Out").ToString();
        public static string Eaten => new TextObject("{=RBM_LEDGER_H_EATEN}Eaten").ToString();
        public static string Garr => new TextObject("{=RBM_LEDGER_H_GARR}Garr").ToString();
        public static string Mil => new TextObject("{=RBM_LEDGER_H_MIL}Mil").ToString();
        public static string Deliv => new TextObject("{=RBM_LEDGER_H_DELIV}Deliv").ToString();
        public static string Party => new TextObject("{=RBM_LEDGER_H_PARTY}Party").ToString();
        public static string Carav => new TextObject("{=RBM_LEDGER_H_CARAV}Carav").ToString();

        // Demand, workshop and goods sub-tables.
        public static string Demand => new TextObject("{=RBM_LEDGER_H_DEMAND}Demand").ToString();
        public static string WantedPerDay => new TextObject("{=RBM_LEDGER_H_WANTED_DAY}Wanted/day").ToString();
        public static string Filled => new TextObject("{=RBM_LEDGER_H_FILLED}Filled").ToString();
        public static string Workshops => new TextObject("{=RBM_LEDGER_H_WORKSHOPS}Workshops").ToString();
        public static string ConsumedPerDay => new TextObject("{=RBM_LEDGER_H_CONSUMED_DAY}Consumed/day").ToString();
        public static string ProducedPerDay => new TextObject("{=RBM_LEDGER_H_PRODUCED_DAY}Produced/day").ToString();
        public static string Goods => new TextObject("{=RBM_LEDGER_H_GOODS}Goods (demand vs stock)").ToString();
        public static string DemandPerDay => new TextObject("{=RBM_LEDGER_H_DEMAND_DAY}Demand/day").ToString();
        public static string Stock => new TextObject("{=RBM_LEDGER_H_STOCK}Stock").ToString();
        public static string Days => new TextObject("{=RBM_LEDGER_H_DAYS}Days").ToString();
        public static string Equipment => new TextObject("{=RBM_LEDGER_H_EQUIPMENT}Equipment & materials").ToString();
        public static string Value => new TextObject("{=RBM_LEDGER_H_VALUE}Value").ToString();

        // Clan finances tab.
        public static string FinanceIncome => new TextObject("{=RBM_LEDGER_H_CF_INCOME}Income").ToString();
        public static string FinanceExpenses => new TextObject("{=RBM_LEDGER_H_CF_EXPENSES}Expenses").ToString();
        public static string FinanceNet => new TextObject("{=RBM_LEDGER_H_CF_NET}Net").ToString();
        public static string FinanceGold => new TextObject("{=RBM_LEDGER_H_CF_GOLD}Gold").ToString();
        public static string FinanceSource => new TextObject("{=RBM_LEDGER_H_CF_SOURCE}Source").ToString();
        public static string FinanceItem => new TextObject("{=RBM_LEDGER_H_CF_ITEM}Goods traded").ToString();
        public static string FinanceBought => new TextObject("{=RBM_LEDGER_H_CF_BOUGHT}Bought").ToString();
        public static string FinancePaid => new TextObject("{=RBM_LEDGER_H_CF_PAID}Paid").ToString();
        public static string FinanceSold => new TextObject("{=RBM_LEDGER_H_CF_SOLD}Sold").ToString();
        public static string FinanceReceived => new TextObject("{=RBM_LEDGER_H_CF_RECEIVED}Received").ToString();
    }
}
