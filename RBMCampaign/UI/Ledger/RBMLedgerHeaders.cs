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
    /// </summary>
    internal static class RBMLedgerHeaders
    {
        // Villages and Towns tab column headings.
        public static readonly string Village = new TextObject("{=RBM_LEDGER_H_VILLAGE}Village").ToString();
        public static readonly string Production = new TextObject("{=RBM_LEDGER_H_PRODUCTION}Production").ToString();
        public static readonly string Wealth = new TextObject("{=RBM_LEDGER_H_WEALTH}Wealth").ToString();
        public static readonly string Hearth = new TextObject("{=RBM_LEDGER_H_HEARTH}Hearth").ToString();
        public static readonly string Militia = new TextObject("{=RBM_LEDGER_H_MILITIA}Militia").ToString();
        public static readonly string Town = new TextObject("{=RBM_LEDGER_H_TOWN}Town").ToString();
        public static readonly string Prosperity = new TextObject("{=RBM_LEDGER_H_PROSPERITY}Prosperity").ToString();
        public static readonly string Citizen = new TextObject("{=RBM_LEDGER_H_CITIZEN}Citizen").ToString();
        public static readonly string Treasury = new TextObject("{=RBM_LEDGER_H_TREASURY}Treasury").ToString();
        public static readonly string Food = new TextObject("{=RBM_LEDGER_H_FOOD}Food").ToString();
        public static readonly string Garrison = new TextObject("{=RBM_LEDGER_H_GARRISON}Garrison").ToString();

        // Per-row history tables. Abbreviated on purpose: these columns are narrow,
        // so a translation should stay about as short as the English.
        public static readonly string Day = new TextObject("{=RBM_LEDGER_H_DAY}Day").ToString();
        public static readonly string Prod = new TextObject("{=RBM_LEDGER_H_PROD}Prod").ToString();
        public static readonly string Events = new TextObject("{=RBM_LEDGER_H_EVENTS}Events").ToString();
        public static readonly string Prosp = new TextObject("{=RBM_LEDGER_H_PROSP}Prosp").ToString();
        public static readonly string CitIn = new TextObject("{=RBM_LEDGER_H_CIT_IN}Cit In").ToString();
        public static readonly string CitOut = new TextObject("{=RBM_LEDGER_H_CIT_OUT}Cit Out").ToString();
        public static readonly string Treas = new TextObject("{=RBM_LEDGER_H_TREAS}Treas").ToString();
        public static readonly string TrsIn = new TextObject("{=RBM_LEDGER_H_TRS_IN}Trs In").ToString();
        public static readonly string TrsOut = new TextObject("{=RBM_LEDGER_H_TRS_OUT}Trs Out").ToString();
        public static readonly string Eaten = new TextObject("{=RBM_LEDGER_H_EATEN}Eaten").ToString();
        public static readonly string Garr = new TextObject("{=RBM_LEDGER_H_GARR}Garr").ToString();
        public static readonly string Mil = new TextObject("{=RBM_LEDGER_H_MIL}Mil").ToString();
        public static readonly string Deliv = new TextObject("{=RBM_LEDGER_H_DELIV}Deliv").ToString();
        public static readonly string Party = new TextObject("{=RBM_LEDGER_H_PARTY}Party").ToString();
        public static readonly string Carav = new TextObject("{=RBM_LEDGER_H_CARAV}Carav").ToString();

        // Demand, workshop and goods sub-tables.
        public static readonly string Demand = new TextObject("{=RBM_LEDGER_H_DEMAND}Demand").ToString();
        public static readonly string WantedPerDay = new TextObject("{=RBM_LEDGER_H_WANTED_DAY}Wanted/day").ToString();
        public static readonly string Filled = new TextObject("{=RBM_LEDGER_H_FILLED}Filled").ToString();
        public static readonly string Workshops = new TextObject("{=RBM_LEDGER_H_WORKSHOPS}Workshops").ToString();
        public static readonly string ConsumedPerDay = new TextObject("{=RBM_LEDGER_H_CONSUMED_DAY}Consumed/day").ToString();
        public static readonly string ProducedPerDay = new TextObject("{=RBM_LEDGER_H_PRODUCED_DAY}Produced/day").ToString();
        public static readonly string Goods = new TextObject("{=RBM_LEDGER_H_GOODS}Goods (demand vs stock)").ToString();
        public static readonly string DemandPerDay = new TextObject("{=RBM_LEDGER_H_DEMAND_DAY}Demand/day").ToString();
        public static readonly string Stock = new TextObject("{=RBM_LEDGER_H_STOCK}Stock").ToString();
        public static readonly string Days = new TextObject("{=RBM_LEDGER_H_DAYS}Days").ToString();
        public static readonly string Equipment = new TextObject("{=RBM_LEDGER_H_EQUIPMENT}Equipment & materials").ToString();
        public static readonly string Value = new TextObject("{=RBM_LEDGER_H_VALUE}Value").ToString();
    }
}
