using System.Collections.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace RBMCampaign
{
    // One day-column in a village's expandable 14-day history.
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

    // A single village row: current values, plus an expandable 14-day history.
    public class RBMLedgerVillageVM : ViewModel
    {
        private bool _isExpanded;

        public RBMLedgerVillageVM(string villageName, string production, string wealth, string hearth,
            string militia, MBBindingList<RBMLedgerVillageDayVM> history, BasicTooltipViewModel productionHint)
        {
            VillageName = villageName;
            Production = production;
            Wealth = wealth;
            Hearth = hearth;
            Militia = militia;
            History = history;
            ProductionHint = productionHint;
        }

        [DataSourceProperty] public string VillageName { get; }
        [DataSourceProperty] public string Production { get; }
        [DataSourceProperty] public string Wealth { get; }
        [DataSourceProperty] public string Hearth { get; }
        [DataSourceProperty] public string Militia { get; }
        [DataSourceProperty] public MBBindingList<RBMLedgerVillageDayVM> History { get; }

        // Item-by-item production breakdown shown on hover over the production value.
        [DataSourceProperty] public BasicTooltipViewModel ProductionHint { get; }

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
