using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Every column heading the ledger prefab used to carry as a literal <c>Text="..."</c>. Gauntlet
    /// never resolves a <c>{=id}</c> marker inside <c>Text=</c>, so the prefab binds to string getters
    /// on the view model that owns the scope and those getters read from here.
    ///
    /// These are TextObjects, not pre-resolved strings: a heading is only turned into text when a
    /// getter runs, so a language change between two openings of the ledger is picked up.
    /// </summary>
    internal static class RBMLedgerHeadings
    {
        // -------- village tab: the list header and the per-village history header ----------------
        public static readonly TextObject Village = new TextObject("{=RBM_LEDGER_H_VILLAGE}Village");
        public static readonly TextObject Production = new TextObject("{=RBM_LEDGER_H_PRODUCTION}Production");
        public static readonly TextObject Wealth = new TextObject("{=RBM_LEDGER_H_WEALTH}Wealth");
        public static readonly TextObject Hearth = new TextObject("{=RBM_LEDGER_H_HEARTH}Hearth");
        public static readonly TextObject Militia = new TextObject("{=RBM_LEDGER_H_MILITIA}Militia");
        public static readonly TextObject Day = new TextObject("{=RBM_LEDGER_H_DAY}Day");
        public static readonly TextObject Prod = new TextObject("{=RBM_LEDGER_H_PROD}Prod");
        public static readonly TextObject Events = new TextObject("{=RBM_LEDGER_H_EVENTS}Events");

        // -------- town tab: the list header --------------------------------------------------------
        public static readonly TextObject Town = new TextObject("{=RBM_LEDGER_H_TOWN}Town");
        public static readonly TextObject Prosperity = new TextObject("{=RBM_LEDGER_H_PROSPERITY}Prosperity");
        public static readonly TextObject Citizen = new TextObject("{=RBM_LEDGER_H_CITIZEN}Citizen");
        public static readonly TextObject Treasury = new TextObject("{=RBM_LEDGER_H_TREASURY}Treasury");
        public static readonly TextObject Food = new TextObject("{=RBM_LEDGER_H_FOOD}Food");
        public static readonly TextObject Garrison = new TextObject("{=RBM_LEDGER_H_GARRISON}Garrison");

        // -------- town tab: the per-town history header (abbreviated, the columns are narrow) -------
        public static readonly TextObject Prosp = new TextObject("{=RBM_LEDGER_H_PROSP}Prosp");
        public static readonly TextObject CitIn = new TextObject("{=RBM_LEDGER_H_CITIN}Cit In");
        public static readonly TextObject CitOut = new TextObject("{=RBM_LEDGER_H_CITOUT}Cit Out");
        public static readonly TextObject Treas = new TextObject("{=RBM_LEDGER_H_TREAS}Treas");
        public static readonly TextObject TrsIn = new TextObject("{=RBM_LEDGER_H_TRSIN}Trs In");
        public static readonly TextObject TrsOut = new TextObject("{=RBM_LEDGER_H_TRSOUT}Trs Out");
        public static readonly TextObject Eaten = new TextObject("{=RBM_LEDGER_H_EATEN}Eaten");
        public static readonly TextObject Garr = new TextObject("{=RBM_LEDGER_H_GARR}Garr");
        public static readonly TextObject Mil = new TextObject("{=RBM_LEDGER_H_MIL}Mil");
        public static readonly TextObject Deliv = new TextObject("{=RBM_LEDGER_H_DELIV}Deliv");
        public static readonly TextObject Party = new TextObject("{=RBM_LEDGER_H_PARTY}Party");
        public static readonly TextObject Carav = new TextObject("{=RBM_LEDGER_H_CARAV}Carav");

        // -------- town tab: the demand, workshop and goods sub-tables ------------------------------
        public static readonly TextObject Demand = new TextObject("{=RBM_LEDGER_H_DEMAND}Demand");
        public static readonly TextObject WantedPerDay = new TextObject("{=RBM_LEDGER_H_WANTEDDAY}Wanted/day");
        public static readonly TextObject Filled = new TextObject("{=RBM_LEDGER_H_FILLED}Filled");
        public static readonly TextObject Workshops = new TextObject("{=RBM_LEDGER_H_WORKSHOPS}Workshops");
        public static readonly TextObject ConsumedPerDay = new TextObject("{=RBM_LEDGER_H_CONSUMEDDAY}Consumed/day");
        public static readonly TextObject ProducedPerDay = new TextObject("{=RBM_LEDGER_H_PRODUCEDDAY}Produced/day");
        public static readonly TextObject Goods = new TextObject("{=RBM_LEDGER_H_GOODS}Goods (demand vs stock)");
        public static readonly TextObject DemandPerDay = new TextObject("{=RBM_LEDGER_H_DEMANDDAY}Demand/day");
        public static readonly TextObject Stock = new TextObject("{=RBM_LEDGER_H_STOCK}Stock");
        public static readonly TextObject Days = new TextObject("{=RBM_LEDGER_H_DAYS}Days");
        public static readonly TextObject Equipment = new TextObject("{=RBM_LEDGER_H_EQUIPMENT}Equipment & materials");
        public static readonly TextObject Value = new TextObject("{=RBM_LEDGER_H_VALUE}Value");
    }
}
