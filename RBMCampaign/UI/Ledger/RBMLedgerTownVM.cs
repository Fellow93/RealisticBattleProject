using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    // One day-column in a town's expandable 30-day history table.
    public class RBMLedgerTownDayVM : ViewModel
    {
        public RBMLedgerTownDayVM(string dayLabel, int prosperity, int citizen, int settlement, int food,
            int garrison, int militia, int villager, BasicTooltipViewModel villagerHint, int party, int caravan,
            int eaten, BasicTooltipViewModel eatenHint,
            int citIncome, BasicTooltipViewModel citIncomeHint, int citExpense, BasicTooltipViewModel citExpenseHint,
            int setIncome, BasicTooltipViewModel setIncomeHint, int setExpense, BasicTooltipViewModel setExpenseHint,
            string eventCount, BasicTooltipViewModel eventsHint)
        {
            DayLabel = dayLabel;
            Prosperity = prosperity.ToString();
            Citizen = citizen.ToString();
            Settlement = settlement.ToString();
            Food = food.ToString();
            Garrison = garrison.ToString();
            Militia = militia.ToString();
            Villager = villager.ToString();
            VillagerHint = villagerHint;
            Party = party.ToString();
            Caravan = caravan.ToString();
            Eaten = eaten.ToString();
            EatenHint = eatenHint;
            CitIncome = citIncome.ToString();
            CitIncomeHint = citIncomeHint;
            CitExpense = citExpense.ToString();
            CitExpenseHint = citExpenseHint;
            SetIncome = setIncome.ToString();
            SetIncomeHint = setIncomeHint;
            SetExpense = setExpense.ToString();
            SetExpenseHint = setExpenseHint;
            Events = eventCount;
            EventsHint = eventsHint;
        }

        [DataSourceProperty] public string DayLabel { get; }
        [DataSourceProperty] public string Prosperity { get; }
        [DataSourceProperty] public string Citizen { get; }
        [DataSourceProperty] public string Settlement { get; }
        [DataSourceProperty] public string Food { get; }
        [DataSourceProperty] public string Garrison { get; }
        [DataSourceProperty] public string Militia { get; }
        [DataSourceProperty] public string Villager { get; }
        [DataSourceProperty] public BasicTooltipViewModel VillagerHint { get; }
        [DataSourceProperty] public string Party { get; }
        [DataSourceProperty] public string Caravan { get; }
        [DataSourceProperty] public string Eaten { get; }
        [DataSourceProperty] public BasicTooltipViewModel EatenHint { get; }
        [DataSourceProperty] public string CitIncome { get; }
        [DataSourceProperty] public BasicTooltipViewModel CitIncomeHint { get; }
        [DataSourceProperty] public string CitExpense { get; }
        [DataSourceProperty] public BasicTooltipViewModel CitExpenseHint { get; }
        [DataSourceProperty] public string SetIncome { get; }
        [DataSourceProperty] public BasicTooltipViewModel SetIncomeHint { get; }
        [DataSourceProperty] public string SetExpense { get; }
        [DataSourceProperty] public BasicTooltipViewModel SetExpenseHint { get; }
        [DataSourceProperty] public string Events { get; }
        [DataSourceProperty] public BasicTooltipViewModel EventsHint { get; }
    }

    // One demand tier (Basic / Medium / Luxury, per RBM's consumption model): the daily units the town's
    // households wanted and the fraction the market filled; hover for consumed-vs-missing detail.
    public class RBMLedgerDemandTierVM : ViewModel
    {
        public RBMLedgerDemandTierVM(string tierName, string demand, string filled, BasicTooltipViewModel hint)
        {
            TierName = tierName;
            Demand = demand;
            Filled = filled;
            Hint = hint;
        }

        [DataSourceProperty] public string TierName { get; }
        [DataSourceProperty] public string Demand { get; }
        [DataSourceProperty] public string Filled { get; }
        // "Consumed N / Missing M" detail on hover.
        [DataSourceProperty] public BasicTooltipViewModel Hint { get; }
    }

    // One consumer good: its current daily demand and market stock, with the 30-day demand/stock
    // history on hover.
    public class RBMLedgerGoodVM : ViewModel
    {
        public RBMLedgerGoodVM(string itemName, string demand, string stock, string days, BasicTooltipViewModel hint)
        {
            ItemName = itemName;
            Demand = demand;
            Stock = stock;
            Days = days;
            Hint = hint;
        }

        [DataSourceProperty] public string ItemName { get; }
        [DataSourceProperty] public string Demand { get; }
        [DataSourceProperty] public string Stock { get; }
        [DataSourceProperty] public string Days { get; }
        [DataSourceProperty] public BasicTooltipViewModel Hint { get; }
    }

    // One workshop: its type name plus the daily units it consumes (all recipe inputs) and produces
    // (all outputs); hover for the per-good input/output breakdown.
    public class RBMLedgerWorkshopVM : ViewModel
    {
        public RBMLedgerWorkshopVM(string workshopName, string consumed, string produced,
            BasicTooltipViewModel consumedHint, BasicTooltipViewModel producedHint)
        {
            WorkshopName = workshopName;
            Consumed = consumed;
            Produced = produced;
            ConsumedHint = consumedHint;
            ProducedHint = producedHint;
        }

        [DataSourceProperty] public string WorkshopName { get; }
        [DataSourceProperty] public string Consumed { get; }
        [DataSourceProperty] public string Produced { get; }
        // Separate breakdowns so the tooltip matches the hovered column.
        [DataSourceProperty] public BasicTooltipViewModel ConsumedHint { get; }
        [DataSourceProperty] public BasicTooltipViewModel ProducedHint { get; }
    }

    // A single town row: current headline values, plus an expandable panel holding a switchable-metric
    // bar chart, a 30-day history table, a demand-tier table and workshop consumed/produced totals.
    public class RBMLedgerTownVM : ViewModel
    {
        private const int BarMaxPx = 110;

        private bool _isExpanded;

        private readonly int[] _prosperitySeries;
        private readonly int[] _citizenSeries;
        private readonly int[] _settlementSeries;
        private readonly int[] _foodSeries;
        private readonly int[] _garrisonSeries;
        private readonly int[] _militiaSeries;
        private readonly int[] _villagerSeries;
        private readonly int[] _partySeries;
        private readonly int[] _caravanSeries;
        private readonly int[] _foodEatenSeries;
        private readonly string[] _barLabels;
        private readonly bool[] _barEvent;

        private string _selectedMetric = "prosperity";
        private MBBindingList<RBMLedgerBarVM> _bars;
        private string _metricName;
        private string _axisMax;
        private string _axisMid;
        private bool _isProsperitySelected;
        private bool _isCitizenSelected;
        private bool _isSettlementSelected;
        private bool _isFoodSelected;
        private bool _isGarrisonSelected;
        private bool _isMilitiaSelected;
        private bool _isVillagerSelected;
        private bool _isPartySelected;
        private bool _isCaravanSelected;
        private bool _isFoodEatenSelected;

        public RBMLedgerTownVM(string townName, string prosperity, string citizen, string settlement, string food,
            string garrison, string militia, string villager, string party, string caravan,
            MBBindingList<RBMLedgerTownDayVM> history, BasicTooltipViewModel foodHint,
            MBBindingList<RBMLedgerDemandTierVM> demandTiers, MBBindingList<RBMLedgerGoodVM> goods,
            MBBindingList<RBMLedgerWorkshopVM> workshops,
            int[] prosperitySeries, int[] citizenSeries, int[] settlementSeries, int[] foodSeries,
            int[] garrisonSeries, int[] militiaSeries, int[] villagerSeries, int[] partySeries, int[] caravanSeries,
            int[] foodEatenSeries, string[] barLabels, bool[] barEvent)
        {
            TownName = townName;
            Prosperity = prosperity;
            Citizen = citizen;
            SettlementWealthText = settlement;
            Food = food;
            Garrison = garrison;
            Militia = militia;
            Villager = villager;
            Party = party;
            Caravan = caravan;
            History = history;
            FoodHint = foodHint;
            DemandTiers = demandTiers;
            Goods = goods;
            HasGoods = goods != null && goods.Count > 0;
            Workshops = workshops;
            HasWorkshops = workshops != null && workshops.Count > 0;

            _prosperitySeries = prosperitySeries ?? new int[0];
            _citizenSeries = citizenSeries ?? new int[0];
            _settlementSeries = settlementSeries ?? new int[0];
            _foodSeries = foodSeries ?? new int[0];
            _garrisonSeries = garrisonSeries ?? new int[0];
            _militiaSeries = militiaSeries ?? new int[0];
            _villagerSeries = villagerSeries ?? new int[0];
            _partySeries = partySeries ?? new int[0];
            _caravanSeries = caravanSeries ?? new int[0];
            _foodEatenSeries = foodEatenSeries ?? new int[0];
            _barLabels = barLabels ?? new string[0];
            _barEvent = barEvent ?? new bool[0];

            _bars = new MBBindingList<RBMLedgerBarVM>();
            ProsperityButtonText = new TextObject("{=RBM_LEDGER_T_PROSPERITY}Prosperity").ToString();
            CitizenButtonText = new TextObject("{=RBM_LEDGER_T_CITIZEN}Citizen").ToString();
            SettlementButtonText = new TextObject("{=RBM_LEDGER_T_TREASURY}Treasury").ToString();
            FoodButtonText = new TextObject("{=RBM_LEDGER_T_FOOD}Food").ToString();
            GarrisonButtonText = new TextObject("{=RBM_LEDGER_T_GARRISON}Garrison").ToString();
            MilitiaButtonText = new TextObject("{=RBM_LEDGER_T_MILITIA}Militia").ToString();
            VillagerButtonText = new TextObject("{=RBM_LEDGER_T_VILLAGER}Delivered").ToString();
            PartyButtonText = new TextObject("{=RBM_LEDGER_T_PARTY}Party buys").ToString();
            CaravanButtonText = new TextObject("{=RBM_LEDGER_T_CARAVAN}Caravan buys").ToString();
            FoodEatenButtonText = new TextObject("{=RBM_LEDGER_T_EATEN}Eaten").ToString();
            SelectMetric("prosperity");
        }

        [DataSourceProperty] public string TownName { get; }
        [DataSourceProperty] public string Prosperity { get; }
        [DataSourceProperty] public string Citizen { get; }
        [DataSourceProperty] public string SettlementWealthText { get; }
        [DataSourceProperty] public string Food { get; }
        [DataSourceProperty] public string Garrison { get; }
        [DataSourceProperty] public string Militia { get; }
        [DataSourceProperty] public string Villager { get; }
        [DataSourceProperty] public string Party { get; }
        [DataSourceProperty] public string Caravan { get; }
        [DataSourceProperty] public MBBindingList<RBMLedgerTownDayVM> History { get; }
        [DataSourceProperty] public BasicTooltipViewModel FoodHint { get; }
        [DataSourceProperty] public MBBindingList<RBMLedgerDemandTierVM> DemandTiers { get; }

        // Per-item demand vs stock table (each row hovers to its 30-day history).
        [DataSourceProperty] public bool HasGoods { get; }
        [DataSourceProperty] public MBBindingList<RBMLedgerGoodVM> Goods { get; }

        // One row per workshop: its consumed/produced units/day, with a per-good breakdown on hover.
        [DataSourceProperty] public bool HasWorkshops { get; }
        [DataSourceProperty] public MBBindingList<RBMLedgerWorkshopVM> Workshops { get; }

        [DataSourceProperty] public string ProsperityButtonText { get; }
        [DataSourceProperty] public string CitizenButtonText { get; }
        [DataSourceProperty] public string SettlementButtonText { get; }
        [DataSourceProperty] public string FoodButtonText { get; }
        [DataSourceProperty] public string GarrisonButtonText { get; }
        [DataSourceProperty] public string MilitiaButtonText { get; }
        [DataSourceProperty] public string VillagerButtonText { get; }
        [DataSourceProperty] public string PartyButtonText { get; }
        [DataSourceProperty] public string CaravanButtonText { get; }
        [DataSourceProperty] public string FoodEatenButtonText { get; }

        [DataSourceProperty]
        public MBBindingList<RBMLedgerBarVM> Bars
        {
            get => _bars;
            set { if (value != _bars) { _bars = value; OnPropertyChangedWithValue(value, "Bars"); } }
        }

        [DataSourceProperty]
        public string MetricName
        {
            get => _metricName;
            set { if (value != _metricName) { _metricName = value; OnPropertyChangedWithValue(value, "MetricName"); } }
        }

        // Y-axis scale labels for the chart: top = the selected series' max, plus its midpoint.
        [DataSourceProperty]
        public string AxisMax
        {
            get => _axisMax;
            set { if (value != _axisMax) { _axisMax = value; OnPropertyChangedWithValue(value, "AxisMax"); } }
        }

        [DataSourceProperty]
        public string AxisMid
        {
            get => _axisMid;
            set { if (value != _axisMid) { _axisMid = value; OnPropertyChangedWithValue(value, "AxisMid"); } }
        }

        [DataSourceProperty]
        public bool IsProsperitySelected
        {
            get => _isProsperitySelected;
            set { if (value != _isProsperitySelected) { _isProsperitySelected = value; OnPropertyChangedWithValue(value, "IsProsperitySelected"); } }
        }

        [DataSourceProperty]
        public bool IsCitizenSelected
        {
            get => _isCitizenSelected;
            set { if (value != _isCitizenSelected) { _isCitizenSelected = value; OnPropertyChangedWithValue(value, "IsCitizenSelected"); } }
        }

        [DataSourceProperty]
        public bool IsSettlementSelected
        {
            get => _isSettlementSelected;
            set { if (value != _isSettlementSelected) { _isSettlementSelected = value; OnPropertyChangedWithValue(value, "IsSettlementSelected"); } }
        }

        [DataSourceProperty]
        public bool IsFoodSelected
        {
            get => _isFoodSelected;
            set { if (value != _isFoodSelected) { _isFoodSelected = value; OnPropertyChangedWithValue(value, "IsFoodSelected"); } }
        }

        [DataSourceProperty]
        public bool IsGarrisonSelected
        {
            get => _isGarrisonSelected;
            set { if (value != _isGarrisonSelected) { _isGarrisonSelected = value; OnPropertyChangedWithValue(value, "IsGarrisonSelected"); } }
        }

        [DataSourceProperty]
        public bool IsMilitiaSelected
        {
            get => _isMilitiaSelected;
            set { if (value != _isMilitiaSelected) { _isMilitiaSelected = value; OnPropertyChangedWithValue(value, "IsMilitiaSelected"); } }
        }

        [DataSourceProperty]
        public bool IsVillagerSelected
        {
            get => _isVillagerSelected;
            set { if (value != _isVillagerSelected) { _isVillagerSelected = value; OnPropertyChangedWithValue(value, "IsVillagerSelected"); } }
        }

        [DataSourceProperty]
        public bool IsPartySelected
        {
            get => _isPartySelected;
            set { if (value != _isPartySelected) { _isPartySelected = value; OnPropertyChangedWithValue(value, "IsPartySelected"); } }
        }

        [DataSourceProperty]
        public bool IsCaravanSelected
        {
            get => _isCaravanSelected;
            set { if (value != _isCaravanSelected) { _isCaravanSelected = value; OnPropertyChangedWithValue(value, "IsCaravanSelected"); } }
        }

        [DataSourceProperty]
        public bool IsFoodEatenSelected
        {
            get => _isFoodEatenSelected;
            set { if (value != _isFoodEatenSelected) { _isFoodEatenSelected = value; OnPropertyChangedWithValue(value, "IsFoodEatenSelected"); } }
        }

        [DataSourceProperty]
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (value != _isExpanded) { _isExpanded = value; OnPropertyChangedWithValue(value, "IsExpanded"); } }
        }

        private void ExecuteToggle() => IsExpanded = !IsExpanded;

        private void ExecuteMetricProsperity() => SelectMetric("prosperity");
        private void ExecuteMetricCitizen() => SelectMetric("citizen");
        private void ExecuteMetricSettlement() => SelectMetric("settlement");
        private void ExecuteMetricFood() => SelectMetric("food");
        private void ExecuteMetricGarrison() => SelectMetric("garrison");
        private void ExecuteMetricMilitia() => SelectMetric("militia");
        private void ExecuteMetricVillager() => SelectMetric("villager");
        private void ExecuteMetricParty() => SelectMetric("party");
        private void ExecuteMetricCaravan() => SelectMetric("caravan");
        private void ExecuteMetricFoodEaten() => SelectMetric("foodEaten");

        private void SelectMetric(string metric)
        {
            _selectedMetric = metric;
            IsProsperitySelected = metric == "prosperity";
            IsCitizenSelected = metric == "citizen";
            IsSettlementSelected = metric == "settlement";
            IsFoodSelected = metric == "food";
            IsGarrisonSelected = metric == "garrison";
            IsMilitiaSelected = metric == "militia";
            IsVillagerSelected = metric == "villager";
            IsPartySelected = metric == "party";
            IsCaravanSelected = metric == "caravan";
            IsFoodEatenSelected = metric == "foodEaten";
            MetricName = MetricDisplayName(metric);
            RebuildBars();
        }

        private int[] MetricSeries(string metric)
        {
            switch (metric)
            {
                case "citizen": return _citizenSeries;
                case "settlement": return _settlementSeries;
                case "food": return _foodSeries;
                case "garrison": return _garrisonSeries;
                case "militia": return _militiaSeries;
                case "villager": return _villagerSeries;
                case "party": return _partySeries;
                case "caravan": return _caravanSeries;
                case "foodEaten": return _foodEatenSeries;
                default: return _prosperitySeries;
            }
        }

        private string MetricDisplayName(string metric)
        {
            switch (metric)
            {
                case "citizen": return CitizenButtonText;
                case "settlement": return SettlementButtonText;
                case "food": return FoodButtonText;
                case "garrison": return GarrisonButtonText;
                case "militia": return MilitiaButtonText;
                case "villager": return VillagerButtonText;
                case "party": return PartyButtonText;
                case "caravan": return CaravanButtonText;
                case "foodEaten": return FoodEatenButtonText;
                default: return ProsperityButtonText;
            }
        }

        private void RebuildBars()
        {
            int[] series = MetricSeries(_selectedMetric);
            int max = 1;
            for (int i = 0; i < series.Length; i++)
            {
                if (series[i] > max)
                {
                    max = series[i];
                }
            }
            AxisMax = max.ToString();
            AxisMid = (max / 2).ToString();

            string caption = MetricName;
            var bars = new MBBindingList<RBMLedgerBarVM>();
            for (int i = 0; i < series.Length; i++)
            {
                int val = series[i];
                int h = val <= 0 ? 0 : (int)MathF.Round((float)val / max * 100f);
                if (val > 0 && h < 2)
                {
                    h = 2;
                }
                string label = i < _barLabels.Length ? _barLabels[i] : string.Empty;
                bool ev = i < _barEvent.Length && _barEvent[i];
                string dayPart = label.Length > 0 ? " (" + label + ")" : string.Empty;
                string hintText = caption + ": " + val + dayPart;
                var hint = new BasicTooltipViewModel(() => hintText);
                bars.Add(new RBMLedgerBarVM(h, label, ev, hint));
            }
            Bars = bars;
        }
    }

    // A faction (kingdom, or the owning clan when independent) and the towns it currently holds.
    public class RBMLedgerFactionGroupVM : ViewModel
    {
        public RBMLedgerFactionGroupVM(string factionName)
        {
            FactionName = factionName;
            Towns = new MBBindingList<RBMLedgerTownVM>();
        }

        [DataSourceProperty] public string FactionName { get; }
        [DataSourceProperty] public MBBindingList<RBMLedgerTownVM> Towns { get; }
    }
}
