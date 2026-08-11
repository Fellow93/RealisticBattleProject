using System.Collections.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    // One day-column in a village's expandable 30-day history.
    public class RBMLedgerVillageDayVM : ViewModel
    {
        public RBMLedgerVillageDayVM(string dayLabel, int production, int wealth, int hearth, int militia,
            string eventCount, BasicTooltipViewModel eventsHint, BasicTooltipViewModel productionHint)
        {
            DayLabel = dayLabel;
            Production = production.ToString();
            Wealth = wealth.ToString();
            Hearth = hearth.ToString();
            Militia = militia.ToString();
            Events = eventCount;
            EventsHint = eventsHint;
            ProductionHint = productionHint;
        }

        [DataSourceProperty] public string DayLabel { get; }
        [DataSourceProperty] public string Production { get; }
        [DataSourceProperty] public string Wealth { get; }
        [DataSourceProperty] public string Hearth { get; }
        [DataSourceProperty] public string Militia { get; }

        // Number of events that day ("-" if none); hover EventsHint for the full list.
        [DataSourceProperty] public string Events { get; }
        [DataSourceProperty] public BasicTooltipViewModel EventsHint { get; }

        // Item-by-item breakdown for this day, reconstructed from the day's hearth. Hover on the value.
        [DataSourceProperty] public BasicTooltipViewModel ProductionHint { get; }
    }

    // One vertical bar in a village's metric chart. Height is pre-normalized to pixels against the
    // selected metric's series max; the raw value lives in the hover tooltip.
    public class RBMLedgerBarVM : ViewModel
    {
        public RBMLedgerBarVM(int barFill, string dayLabel, bool raided, BasicTooltipViewModel valueHint)
        {
            BarFill = barFill;
            DayLabel = dayLabel;
            IsRaided = raided;
            IsNormalBar = !raided;
            ValueHint = valueHint;
        }

        // Bar fill amount, 0..100, bound to a vertical FillBar's InitialAmount (MaxAmount=100). NOTE:
        // SuggestedHeight is NOT data-bindable in Gauntlet (no native prefab binds it) -- the earlier
        // BrushWidget+SuggestedHeight approach silently left every bar at height 0. FillBar is how the
        // engine does variable bars (see RBM's own CombatUI/UnitStatus health/posture/stamina bars).
        [DataSourceProperty] public int BarFill { get; }

        // Short x-axis label ("T" for today, "-3" for three days ago).
        [DataSourceProperty] public string DayLabel { get; }

        // Raid/loot on that day -> red bar; otherwise the normal amber bar. Two stacked widgets
        // toggled by these flags (Gauntlet brushes are fixed at parse time, so we swap visibility).
        [DataSourceProperty] public bool IsRaided { get; }
        [DataSourceProperty] public bool IsNormalBar { get; }

        [DataSourceProperty] public BasicTooltipViewModel ValueHint { get; }
    }

    // A single village row: current values, plus an expandable panel holding a switchable-metric
    // bar chart and a 30-day history table.
    public class RBMLedgerVillageVM : ViewModel
    {
        // Tallest bar in pixels; every series is scaled so its own max reaches this height.
        private const int BarMaxPx = 110;

        private bool _isExpanded;

        // Raw metric series (oldest->newest), the per-column x-axis labels, and the per-column
        // raid flags -- kept so the chart can re-normalize when the user switches metric.
        private readonly int[] _prodSeries;
        private readonly int[] _wealthSeries;
        private readonly int[] _hearthSeries;
        private readonly int[] _militiaSeries;
        private readonly string[] _barLabels;
        private readonly bool[] _barRaided;

        private string _selectedMetric = "prod";
        private MBBindingList<RBMLedgerBarVM> _bars;
        private string _metricName;
        private string _axisMax;
        private string _axisMid;
        private bool _isProdSelected;
        private bool _isWealthSelected;
        private bool _isHearthSelected;
        private bool _isMilitiaSelected;

        public RBMLedgerVillageVM(string villageName, string production, string productionIcon, string wealth,
            string hearth, string militia, MBBindingList<RBMLedgerVillageDayVM> history, BasicTooltipViewModel productionHint,
            int[] prodSeries, int[] wealthSeries, int[] hearthSeries, int[] militiaSeries, string[] barLabels, bool[] barRaided)
        {
            VillageName = villageName;
            Production = production;
            ProductionIcon = productionIcon;
            Wealth = wealth;
            Hearth = hearth;
            Militia = militia;
            History = history;
            ProductionHint = productionHint;

            _prodSeries = prodSeries ?? new int[0];
            _wealthSeries = wealthSeries ?? new int[0];
            _hearthSeries = hearthSeries ?? new int[0];
            _militiaSeries = militiaSeries ?? new int[0];
            _barLabels = barLabels ?? new string[0];
            _barRaided = barRaided ?? new bool[0];

            _bars = new MBBindingList<RBMLedgerBarVM>();
            ProdButtonText = new TextObject("{=RBM_LEDGER_M_PROD}Prod").ToString();
            WealthButtonText = new TextObject("{=RBM_LEDGER_M_WEALTH}Wealth").ToString();
            HearthButtonText = new TextObject("{=RBM_LEDGER_M_HEARTH}Hearth").ToString();
            MilitiaButtonText = new TextObject("{=RBM_LEDGER_M_MILITIA}Militia").ToString();
            SelectMetric("prod");
        }

        [DataSourceProperty] public string VillageName { get; }
        [DataSourceProperty] public string Production { get; }

        // Nameplate production-icon style id for this village's primary good (planks -> hardwood,
        // crude iron -> iron). Bound as AdditionalParameters on the production-icon brush widget.
        [DataSourceProperty] public string ProductionIcon { get; }
        [DataSourceProperty] public string Wealth { get; }
        [DataSourceProperty] public string Hearth { get; }
        [DataSourceProperty] public string Militia { get; }
        [DataSourceProperty] public MBBindingList<RBMLedgerVillageDayVM> History { get; }

        // Item-by-item production breakdown shown on hover over the production value.
        [DataSourceProperty] public BasicTooltipViewModel ProductionHint { get; }

        // Metric-selector button captions.
        [DataSourceProperty] public string ProdButtonText { get; }
        [DataSourceProperty] public string WealthButtonText { get; }
        [DataSourceProperty] public string HearthButtonText { get; }
        [DataSourceProperty] public string MilitiaButtonText { get; }

        [DataSourceProperty]
        public MBBindingList<RBMLedgerBarVM> Bars
        {
            get => _bars;
            set { if (value != _bars) { _bars = value; OnPropertyChangedWithValue(value, "Bars"); } }
        }

        // Currently-charted metric's display name (chart caption).
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
        public bool IsProdSelected
        {
            get => _isProdSelected;
            set { if (value != _isProdSelected) { _isProdSelected = value; OnPropertyChangedWithValue(value, "IsProdSelected"); } }
        }

        [DataSourceProperty]
        public bool IsWealthSelected
        {
            get => _isWealthSelected;
            set { if (value != _isWealthSelected) { _isWealthSelected = value; OnPropertyChangedWithValue(value, "IsWealthSelected"); } }
        }

        [DataSourceProperty]
        public bool IsHearthSelected
        {
            get => _isHearthSelected;
            set { if (value != _isHearthSelected) { _isHearthSelected = value; OnPropertyChangedWithValue(value, "IsHearthSelected"); } }
        }

        [DataSourceProperty]
        public bool IsMilitiaSelected
        {
            get => _isMilitiaSelected;
            set { if (value != _isMilitiaSelected) { _isMilitiaSelected = value; OnPropertyChangedWithValue(value, "IsMilitiaSelected"); } }
        }

        [DataSourceProperty]
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (value != _isExpanded)
                {
                    _isExpanded = value;
                    OnPropertyChangedWithValue(value, "IsExpanded");
                }
            }
        }

        // Bound from the row button's Command.Click.
        private void ExecuteToggle()
        {
            IsExpanded = !IsExpanded;
        }

        // Metric-selector buttons.
        private void ExecuteMetricProd() => SelectMetric("prod");
        private void ExecuteMetricWealth() => SelectMetric("wealth");
        private void ExecuteMetricHearth() => SelectMetric("hearth");
        private void ExecuteMetricMilitia() => SelectMetric("militia");

        private void SelectMetric(string metric)
        {
            _selectedMetric = metric;
            IsProdSelected = metric == "prod";
            IsWealthSelected = metric == "wealth";
            IsHearthSelected = metric == "hearth";
            IsMilitiaSelected = metric == "militia";
            MetricName = MetricDisplayName(metric);
            RebuildBars();
        }

        private int[] MetricSeries(string metric)
        {
            switch (metric)
            {
                case "wealth": return _wealthSeries;
                case "hearth": return _hearthSeries;
                case "militia": return _militiaSeries;
                default: return _prodSeries;
            }
        }

        private static string MetricDisplayName(string metric)
        {
            switch (metric)
            {
                case "wealth": return new TextObject("{=RBM_LEDGER_M_WEALTH}Wealth").ToString();
                case "hearth": return new TextObject("{=RBM_LEDGER_M_HEARTH}Hearth").ToString();
                case "militia": return new TextObject("{=RBM_LEDGER_M_MILITIA}Militia").ToString();
                default: return new TextObject("{=RBM_LEDGER_M_PROD}Prod").ToString();
            }
        }

        // Re-scale the selected metric's series into pixel-height bars. Each series is normalized to
        // its own max so a chart always uses the full height regardless of the metric's magnitude.
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
                    h = 2; // keep a tiny non-zero value visible
                }
                string label = i < _barLabels.Length ? _barLabels[i] : string.Empty;
                bool raided = i < _barRaided.Length && _barRaided[i];
                string dayPart = label.Length > 0 ? " (" + label + ")" : string.Empty;
                string hintText = caption + ": " + val + dayPart;
                var hint = new BasicTooltipViewModel(() => hintText);
                bars.Add(new RBMLedgerBarVM(h, label, raided, hint));
            }
            Bars = bars;
        }
    }

    // A trade-bound town and the villages that trade with it.
    public class RBMLedgerTownGroupVM : ViewModel
    {
        public RBMLedgerTownGroupVM(string townName)
        {
            TownName = townName;
            Villages = new MBBindingList<RBMLedgerVillageVM>();
        }

        [DataSourceProperty] public string TownName { get; }
        [DataSourceProperty] public MBBindingList<RBMLedgerVillageVM> Villages { get; }
    }
}
