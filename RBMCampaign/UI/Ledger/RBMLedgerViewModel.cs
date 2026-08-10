using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace RBMCampaign
{
    // Backing view model for the RBM Ledger screen (RBMLedger.xml). Extensible shell: a data-bound
    // tab strip on the left and a content area on the right that shows EITHER a plain text pane
    // (placeholder tabs) OR the villages panel, toggled by the selected tab's id.
    public class RBMLedgerViewModel : ViewModel
    {
        private const string TabVillages = "villages";

        private MBBindingList<RBMLedgerTabVM> _tabs;
        private MBBindingList<RBMLedgerTownGroupVM> _townGroups;
        private string _titleText;
        private string _closeText;
        private string _currentTitle;
        private string _currentContent;
        private bool _showVillages;
        private bool _showText;

        public RBMLedgerViewModel()
        {
            _tabs = new MBBindingList<RBMLedgerTabVM>();
            _townGroups = new MBBindingList<RBMLedgerTownGroupVM>();
            TitleText = new TextObject("{=RBM_LEDGER_TITLE}RBM Ledger").ToString();
            CloseText = new TextObject("{=RBM_LEDGER_CLOSE}Close").ToString();
            BuildVillages();
            BuildTabs();
        }

        private void BuildTabs()
        {
            AddTab("overview",
                new TextObject("{=RBM_LEDGER_TAB_OVERVIEW}Overview").ToString(),
                new TextObject("{=RBM_LEDGER_TAB_OVERVIEW}Overview").ToString(),
                new TextObject("{=RBM_LEDGER_PLACEHOLDER}This tab is not implemented yet. Statistics will appear here.").ToString());
            AddTab(TabVillages,
                new TextObject("{=RBM_LEDGER_TAB_VILLAGES}Villages").ToString(),
                new TextObject("{=RBM_LEDGER_TAB_VILLAGES}Villages").ToString(),
                string.Empty);

            if (Tabs.Count > 0)
            {
                SelectTab(Tabs[0]);
            }
        }

        private void AddTab(string id, string name, string title, string content)
        {
            Tabs.Add(new RBMLedgerTabVM(id, name, title, content, SelectTab));
        }

        private void SelectTab(RBMLedgerTabVM tab)
        {
            if (tab == null)
            {
                return;
            }
            foreach (RBMLedgerTabVM t in Tabs)
            {
                t.IsSelected = t == tab;
            }
            CurrentTitle = tab.Title;
            CurrentContent = tab.Content;
            ShowVillages = tab.TabId == TabVillages;
            ShowText = !ShowVillages;
        }

        // --- Villages projection --------------------------------------------

        // Projects RBMVillageLedger's persisted history onto the town-group -> village -> day tree.
        // Every current village is listed (even with no history yet), grouped by its trade-bound town.
        private void BuildVillages()
        {
            if (Campaign.Current == null)
            {
                return;
            }

            var groupsByKey = new Dictionary<string, RBMLedgerTownGroupVM>();
            var orderedGroups = new List<RBMLedgerTownGroupVM>();

            IEnumerable<Village> villages = Village.All
                .OrderBy(v => GroupName(v))
                .ThenBy(v => v.Settlement != null ? v.Settlement.Name.ToString() : string.Empty);

            foreach (Village village in villages)
            {
                if (village.Settlement == null)
                {
                    continue;
                }
                string key = village.TradeBound != null ? village.TradeBound.StringId : "__unbound";
                if (!groupsByKey.TryGetValue(key, out RBMLedgerTownGroupVM group))
                {
                    group = new RBMLedgerTownGroupVM(GroupName(village));
                    groupsByKey[key] = group;
                    orderedGroups.Add(group);
                }
                group.Villages.Add(BuildVillageRow(village));
            }

            foreach (RBMLedgerTownGroupVM g in orderedGroups)
            {
                TownGroups.Add(g);
            }
        }

        private static string GroupName(Village village)
        {
            return village.TradeBound != null
                ? village.TradeBound.Name.ToString()
                : new TextObject("{=RBM_LEDGER_UNBOUND}Unbound").ToString();
        }

        private static RBMLedgerVillageVM BuildVillageRow(Village village)
        {
            string id = village.Settlement.StringId;
            int[] prod = RBMVillageLedger.GetSeries("prod", id);
            int[] wealth = RBMVillageLedger.GetSeries("wealth", id);
            int[] hearth = RBMVillageLedger.GetSeries("hearth", id);
            int[] militia = RBMVillageLedger.GetSeries("militia", id);

            int n = prod.Length;
            n = System.Math.Max(n, wealth.Length);
            n = System.Math.Max(n, hearth.Length);
            n = System.Math.Max(n, militia.Length);

            // Chart x-axis metadata, oldest->newest (bars read left-to-right, unlike the newest-first table).
            var barLabels = new string[n];
            var barRaided = new bool[n];
            int lastDayForBars = RBMVillageLedger.LastDay;
            for (int i = 0; i < n; i++)
            {
                int barOffset = (n - 1) - i;
                int barDay = lastDayForBars - barOffset;
                barLabels[i] = barOffset == 0 ? "T" : "-" + barOffset;
                List<string> barEvents = RBMVillageLedger.GetEventsForDay(id, barDay);
                barRaided[i] = barEvents.Contains(RBMVillageLedger.EvRaidStart) || barEvents.Contains(RBMVillageLedger.EvLooted);
            }

            var history = new MBBindingList<RBMLedgerVillageDayVM>();
            int lastDay = RBMVillageLedger.LastDay;
            // Newest day first.
            for (int i = n - 1; i >= 0; i--)
            {
                int offset = (n - 1) - i;
                int day = lastDay - offset;
                string label = offset == 0
                    ? new TextObject("{=RBM_LEDGER_TODAY}Today").ToString()
                    : "-" + offset + "d";
                List<string> evPretty = FormatEventList(RBMVillageLedger.GetEventsForDay(id, day));
                string evCount = evPretty.Count > 0 ? evPretty.Count.ToString() : "-";
                BasicTooltipViewModel evHint = null;
                if (evPretty.Count > 0)
                {
                    string evText = string.Join("\n", evPretty);
                    evHint = new BasicTooltipViewModel(() => evText);
                }
                int prodDay = At(prod, i);
                int hearthDay = At(hearth, i);
                // No per-day item detail is stored; reconstruct from that day's hearth. A stored 0
                // with a live hearth means the village wasn't producing that day (raided/looted).
                bool halted = prodDay <= 0 && hearthDay > 0;
                string dayText = BuildBreakdown(village, hearthDay, halted, prodDay);
                var dayHint = new BasicTooltipViewModel(() => dayText);
                history.Add(new RBMLedgerVillageDayVM(label, prodDay, At(wealth, i), hearthDay, At(militia, i), evCount, evHint, dayHint));
            }

            return new RBMLedgerVillageVM(
                village.Settlement.Name.ToString(),
                Latest(prod),
                VillageProductionIcon.StyleId(village),
                Latest(wealth),
                Latest(hearth),
                Latest(militia),
                history,
                BuildProductionHint(village),
                prod, wealth, hearth, militia, barLabels, barRaided);
        }

        // Item-by-item production breakdown for the hover tooltip on the current (summary) production value.
        private static BasicTooltipViewModel BuildProductionHint(Village village)
        {
            bool halted = village.VillageState != Village.VillageStates.Normal;
            int hearth = (int)MathF.Round(village.Hearth);
            int total = halted ? 0 : (int)MathF.Round(RBMVillageProduction.GetTotalRate(village) * village.Hearth);
            string text = BuildBreakdown(village, hearth, halted, total);
            return new BasicTooltipViewModel(() => text);
        }

        // Shared breakdown formatter. Reconstructs the per-item split from rate*hearth (rates are
        // deterministic per village type); `total` is shown verbatim so it matches the displayed value.
        private static string BuildBreakdown(Village village, int hearth, bool halted, int total)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(new TextObject("{=RBM_LEDGER_PROD_HINT_HDR}Daily production").ToString());
            sb.Append(" (").Append(new TextObject("{=RBM_LEDGER_HEARTH}Hearth").ToString())
              .Append(' ').Append(hearth).Append(')');
            if (halted)
            {
                sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_PROD_HINT_HALTED}(production halted)").ToString());
            }
            else
            {
                var lines = new List<KeyValuePair<string, int>>();
                foreach (var kv in RBMVillageProduction.GetRates(village))
                {
                    int perDay = (int)MathF.Round(kv.Value * hearth);
                    if (perDay <= 0)
                    {
                        continue;
                    }
                    string name = (kv.Key != null && kv.Key.Name != null) ? kv.Key.Name.ToString() : "?";
                    lines.Add(new KeyValuePair<string, int>(name, perDay));
                }
                lines.Sort((a, b) => b.Value.CompareTo(a.Value));
                foreach (var l in lines)
                {
                    sb.Append('\n').Append(l.Key).Append(": ").Append(l.Value);
                }
            }
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_PROD_HINT_TOTAL}Total").ToString()).Append(": ").Append(total);
            return sb.ToString();
        }

        private static int At(int[] arr, int i)
        {
            return (i >= 0 && i < arr.Length) ? arr[i] : 0;
        }

        private static string Latest(int[] arr)
        {
            return arr.Length > 0 ? arr[arr.Length - 1].ToString() : "-";
        }

        private static List<string> FormatEventList(List<string> tokens)
        {
            var parts = new List<string>(tokens != null ? tokens.Count : 0);
            if (tokens == null)
            {
                return parts;
            }
            foreach (string t in tokens)
            {
                switch (t)
                {
                    case RBMVillageLedger.EvRaidStart: parts.Add(new TextObject("{=RBM_LEDGER_EV_RAID}Raided").ToString()); break;
                    case RBMVillageLedger.EvLooted: parts.Add(new TextObject("{=RBM_LEDGER_EV_LOOTED}Looted").ToString()); break;
                    case RBMVillageLedger.EvDispatch: parts.Add(new TextObject("{=RBM_LEDGER_EV_DISPATCH}Dispatch sent").ToString()); break;
                    case RBMVillageLedger.EvArrive: parts.Add(new TextObject("{=RBM_LEDGER_EV_ARRIVE}Dispatch arrived").ToString()); break;
                    default: parts.Add(t); break;
                }
            }
            return parts;
        }

        // Bound from the prefab's close button (Command.Click).
        private void ExecuteClose()
        {
            ScreenManager.PopScreen();
        }

        [DataSourceProperty]
        public MBBindingList<RBMLedgerTabVM> Tabs
        {
            get => _tabs;
            set { if (value != _tabs) { _tabs = value; OnPropertyChangedWithValue(value, "Tabs"); } }
        }

        [DataSourceProperty]
        public MBBindingList<RBMLedgerTownGroupVM> TownGroups
        {
            get => _townGroups;
            set { if (value != _townGroups) { _townGroups = value; OnPropertyChangedWithValue(value, "TownGroups"); } }
        }

        [DataSourceProperty]
        public bool ShowVillages
        {
            get => _showVillages;
            set { if (value != _showVillages) { _showVillages = value; OnPropertyChangedWithValue(value, "ShowVillages"); } }
        }

        [DataSourceProperty]
        public bool ShowText
        {
            get => _showText;
            set { if (value != _showText) { _showText = value; OnPropertyChangedWithValue(value, "ShowText"); } }
        }

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set { if (value != _titleText) { _titleText = value; OnPropertyChangedWithValue(value, "TitleText"); } }
        }

        [DataSourceProperty]
        public string CloseText
        {
            get => _closeText;
            set { if (value != _closeText) { _closeText = value; OnPropertyChangedWithValue(value, "CloseText"); } }
        }

        [DataSourceProperty]
        public string CurrentTitle
        {
            get => _currentTitle;
            set { if (value != _currentTitle) { _currentTitle = value; OnPropertyChangedWithValue(value, "CurrentTitle"); } }
        }

        [DataSourceProperty]
        public string CurrentContent
        {
            get => _currentContent;
            set { if (value != _currentContent) { _currentContent = value; OnPropertyChangedWithValue(value, "CurrentContent"); } }
        }
    }
}
