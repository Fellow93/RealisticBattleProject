using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    // One day of the Clan finances history table: what came in, what went out, the net and the closing balance,
    // each cell hovering to the rows that make it up.
    public class RBMLedgerFinanceDayVM : ViewModel
    {
        public RBMLedgerFinanceDayVM(string dayLabel, string income, BasicTooltipViewModel incomeHint,
            string expenses, BasicTooltipViewModel expensesHint, string net, BasicTooltipViewModel netHint,
            string gold, BasicTooltipViewModel goldHint)
        {
            DayLabel = dayLabel;
            Income = income;
            IncomeHint = incomeHint;
            Expenses = expenses;
            ExpensesHint = expensesHint;
            Net = net;
            NetHint = netHint;
            Gold = gold;
            GoldHint = goldHint;
        }

        [DataSourceProperty] public string DayLabel { get; }
        [DataSourceProperty] public string Income { get; }
        [DataSourceProperty] public BasicTooltipViewModel IncomeHint { get; }
        [DataSourceProperty] public string Expenses { get; }
        [DataSourceProperty] public BasicTooltipViewModel ExpensesHint { get; }
        [DataSourceProperty] public string Net { get; }
        [DataSourceProperty] public BasicTooltipViewModel NetHint { get; }
        [DataSourceProperty] public string Gold { get; }
        [DataSourceProperty] public BasicTooltipViewModel GoldHint { get; }
    }

    // One row of the category table -- a finance line, an event kind, goods sold or bought, an adjustment or
    // Other -- with its income, expense and net over the window; hover for the day-by-day amounts. The Net
    // row at the foot uses the same shape.
    public class RBMLedgerFinanceRowVM : ViewModel
    {
        public RBMLedgerFinanceRowVM(string name, string income, string expenses, string net, BasicTooltipViewModel hint)
        {
            Name = name;
            Income = income;
            Expenses = expenses;
            Net = net;
            Hint = hint;
        }

        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string Income { get; }
        [DataSourceProperty] public string Expenses { get; }
        [DataSourceProperty] public string Net { get; }
        [DataSourceProperty] public BasicTooltipViewModel Hint { get; }
    }

    // One item the player bought or sold over the window: units and gold each way; hover for the days.
    public class RBMLedgerTradeItemVM : ViewModel
    {
        public RBMLedgerTradeItemVM(string itemName, string boughtUnits, string paid, string soldUnits, string received,
            BasicTooltipViewModel hint)
        {
            ItemName = itemName;
            BoughtUnits = boughtUnits;
            Paid = paid;
            SoldUnits = soldUnits;
            Received = received;
            Hint = hint;
        }

        [DataSourceProperty] public string ItemName { get; }
        [DataSourceProperty] public string BoughtUnits { get; }
        [DataSourceProperty] public string Paid { get; }
        [DataSourceProperty] public string SoldUnits { get; }
        [DataSourceProperty] public string Received { get; }
        [DataSourceProperty] public BasicTooltipViewModel Hint { get; }
    }

    // The traded goods of one category (the Towns tab's equipment groups, trade goods first in their place).
    public class RBMLedgerTradeGroupVM : ViewModel
    {
        public RBMLedgerTradeGroupVM(string groupName)
        {
            GroupName = groupName;
            Items = new MBBindingList<RBMLedgerTradeItemVM>();
        }

        [DataSourceProperty] public string GroupName { get; }
        [DataSourceProperty] public MBBindingList<RBMLedgerTradeItemVM> Items { get; }
    }

    /// <summary>
    /// The RBM Ledger's Clan finances tab: a projection of <see cref="RBMClanFinanceLedger"/> (built once when
    /// the screen opens; nothing here is stored). A headline, a switchable bar chart (income, expenses, net,
    /// gold), a day-by-day history, one row per source of money with a Net row, and the goods the player
    /// traded. Every day's income minus its expenses is exactly its change in gold, because the untracked
    /// remainder is shown as its own row (Other).
    ///
    /// The chart shows a losing day's net as a red bar of its size (the town and village charts' raid-bar
    /// pattern): a bar cannot grow below its baseline, and one bar per day keeps the chart the same shape as
    /// the other ledgers' while the colour carries the sign. Expenses are drawn red throughout for the same
    /// reason.
    /// </summary>
    public class RBMLedgerClanFinanceVM : ViewModel
    {
        private const string MetricIncome = "income";
        private const string MetricExpenses = "expenses";
        private const string MetricNet = "net";
        private const string MetricGold = "gold";

        // Sort groups for the category table: finance lines, one-off events, trading, then Other last.
        private const int GroupLines = 0;
        private const int GroupEvents = 1;
        private const int GroupTrade = 2;
        private const int GroupOther = 3;

        private readonly int[] _income;
        private readonly int[] _expenses;
        private readonly int[] _net;
        private readonly int[] _gold;
        private readonly string[] _barLabels;
        private readonly string[] _barDays;

        private MBBindingList<RBMLedgerBarVM> _bars;
        private string _metricName;
        private string _axisMax;
        private string _axisMid;
        private bool _isIncomeSelected;
        private bool _isExpensesSelected;
        private bool _isNetSelected;
        private bool _isGoldSelected;

        public RBMLedgerClanFinanceVM()
        {
            History = new MBBindingList<RBMLedgerFinanceDayVM>();
            Categories = new MBBindingList<RBMLedgerFinanceRowVM>();
            TradeGroups = new MBBindingList<RBMLedgerTradeGroupVM>();
            _bars = new MBBindingList<RBMLedgerBarVM>();
            IncomeButtonText = new TextObject("{=RBM_LEDGER_CF_M_INCOME}Income").ToString();
            ExpensesButtonText = new TextObject("{=RBM_LEDGER_CF_M_EXPENSES}Expenses").ToString();
            NetButtonText = new TextObject("{=RBM_LEDGER_CF_M_NET}Net").ToString();
            GoldButtonText = new TextObject("{=RBM_LEDGER_CF_M_GOLD}Gold").ToString();

            List<RBMClanFinanceLedger.DayRecord> days = RBMClanFinanceLedger.GetDays();
            int n = days.Count;
            _income = new int[n];
            _expenses = new int[n];
            _net = new int[n];
            _gold = new int[n];
            _barLabels = new string[n];
            _barDays = new string[n];
            int today = n > 0 ? days[n - 1].Day : 0;
            for (int i = 0; i < n; i++)
            {
                RBMClanFinanceLedger.DayRecord d = days[i];
                foreach (int gold in d.Rows.Values)
                {
                    if (gold > 0)
                    {
                        _income[i] += gold;
                    }
                    else
                    {
                        _expenses[i] -= gold;
                    }
                }
                _net[i] = d.Net;
                _gold[i] = d.EndGold;
                int offset = today - d.Day;
                _barLabels[i] = offset == 0 ? "T" : "-" + offset;
                _barDays[i] = DayName(offset);
            }

            HasData = n > 0;
            BuildHeadline(days);
            BuildHistory(days);
            BuildCategories(days);
            BuildTrades(days);
            SelectMetric(MetricNet);
        }

        // --- Headline -------------------------------------------------------------------------------------

        private void BuildHeadline(List<RBMClanFinanceLedger.DayRecord> days)
        {
            var intro = new StringBuilder();
            intro.Append(new TextObject("{=RBM_LEDGER_CF_INTRO}What actually happened to your gold over the last {DAYS} days: the daily finance lines from the gold tooltip, one-off payments as they happened, your own trading, and Other for everything no line names (loot, quests, ransoms, gambling). Each day's income minus its expenses is its real change in gold. This is a record of the past; it is not part of the daily gold change or Expected Gold.")
                .SetTextVariable("DAYS", RBMClanFinanceLedger.HistoryDays).ToString());
            if (days.Count == 0)
            {
                intro.Append("\n\n").Append(new TextObject("{=RBM_LEDGER_CF_EMPTY}Nothing recorded yet.").ToString());
                Intro = intro.ToString();
                GoldText = string.Empty;
                NetText = string.Empty;
                AverageText = string.Empty;
                return;
            }
            if (days[0].Partial && days.Count < RBMClanFinanceLedger.HistoryDays)
            {
                int ago = days[days.Count - 1].Day - days[0].Day;
                intro.Append("\n\n").Append(ago == 0
                    ? new TextObject("{=RBM_LEDGER_CF_STARTED_TODAY}Tracking began today.").ToString()
                    : new TextObject("{=RBM_LEDGER_CF_STARTED}Tracking began {DAYS} days ago, part-way through that day.")
                        .SetTextVariable("DAYS", ago).ToString());
            }
            Intro = intro.ToString();

            RBMClanFinanceLedger.DayRecord first = days[0];
            RBMClanFinanceLedger.DayRecord last = days[days.Count - 1];
            long net = (long)last.EndGold - first.StartGold;
            GoldText = new TextObject("{=RBM_LEDGER_CF_HL_GOLD}Gold: {GOLD}").SetTextVariable("GOLD", last.EndGold).ToString();
            NetText = new TextObject("{=RBM_LEDGER_CF_HL_NET}Net over {DAYS} days: {NET}")
                .SetTextVariable("DAYS", days.Count).SetTextVariable("NET", Signed(net)).ToString();
            AverageText = new TextObject("{=RBM_LEDGER_CF_HL_AVG}Average per day: {NET}")
                .SetTextVariable("NET", Signed((long)Math.Round((double)net / days.Count))).ToString();
        }

        // --- History table ---------------------------------------------------------------------------------

        private void BuildHistory(List<RBMClanFinanceLedger.DayRecord> days)
        {
            int today = days.Count > 0 ? days[days.Count - 1].Day : 0;
            string incomeHeader = new TextObject("{=RBM_LEDGER_INCOME}Income").ToString();
            string expenseHeader = new TextObject("{=RBM_LEDGER_EXPENSE}Expense").ToString();
            for (int i = days.Count - 1; i >= 0; i--)
            {
                RBMClanFinanceLedger.DayRecord d = days[i];
                int offset = today - d.Day;
                var income = new List<KeyValuePair<string, int>>();
                var expense = new List<KeyValuePair<string, int>>();
                var all = new List<KeyValuePair<string, int>>();
                foreach (KeyValuePair<string, int> row in d.Rows)
                {
                    string name = RowName(row.Key);
                    all.Add(new KeyValuePair<string, int>(name, row.Value));
                    if (row.Value > 0)
                    {
                        income.Add(new KeyValuePair<string, int>(name, row.Value));
                    }
                    else if (row.Value < 0)
                    {
                        expense.Add(new KeyValuePair<string, int>(name, -row.Value));
                    }
                }

                string label = offset == 0
                    ? new TextObject("{=RBM_LEDGER_TODAY}Today").ToString()
                    : "-" + offset + "d";
                History.Add(new RBMLedgerFinanceDayVM(label,
                    _income[i].ToString(), SideHint(incomeHeader, income, _income[i]),
                    _expenses[i].ToString(), SideHint(expenseHeader, expense, _expenses[i]),
                    Signed(d.Net), NetHint(d, all),
                    d.EndGold.ToString(), GoldHint(d)));
            }
        }

        // One side of a day (income or expense): its rows, largest first, or no tooltip for an empty side.
        private static BasicTooltipViewModel SideHint(string header, List<KeyValuePair<string, int>> lines, int total)
        {
            if (lines.Count == 0)
            {
                return null;
            }
            lines.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new StringBuilder();
            sb.Append(new TextObject("{=RBM_LEDGER_FLOW_HEADER}{HEADER} ({GOLD}g)")
                .SetTextVariable("HEADER", header).SetTextVariable("GOLD", total).ToString());
            foreach (KeyValuePair<string, int> l in lines)
            {
                sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_FLOW_LINE}{NAME}: {GOLD}g")
                    .SetTextVariable("NAME", l.Key).SetTextVariable("GOLD", l.Value).ToString());
            }
            string text = sb.ToString();
            return new BasicTooltipViewModel(() => text);
        }

        // Every row of the day, signed, so the hover adds up to the net shown.
        private static BasicTooltipViewModel NetHint(RBMClanFinanceLedger.DayRecord d, List<KeyValuePair<string, int>> lines)
        {
            lines.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
            var sb = new StringBuilder();
            sb.Append(new TextObject("{=RBM_LEDGER_CF_NET_HDR}Change in gold: {NET}").SetTextVariable("NET", Signed(d.Net)).ToString());
            foreach (KeyValuePair<string, int> l in lines)
            {
                sb.Append('\n').Append(l.Key).Append(": ").Append(Signed(l.Value));
            }
            string text = sb.ToString();
            return new BasicTooltipViewModel(() => text);
        }

        private static BasicTooltipViewModel GoldHint(RBMClanFinanceLedger.DayRecord d)
        {
            var sb = new StringBuilder();
            sb.Append(new TextObject("{=RBM_LEDGER_CF_OPENING}Opening balance: {GOLD}").SetTextVariable("GOLD", d.StartGold).ToString());
            sb.Append('\n').Append((d.IsOpen
                    ? new TextObject("{=RBM_LEDGER_CF_NOW}Now: {GOLD}")
                    : new TextObject("{=RBM_LEDGER_CF_CLOSING}Closing balance: {GOLD}"))
                .SetTextVariable("GOLD", d.EndGold).ToString());
            if (d.Partial)
            {
                sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_CF_PARTIAL}Tracking began during this day.").ToString());
            }
            string text = sb.ToString();
            return new BasicTooltipViewModel(() => text);
        }

        // --- Category table --------------------------------------------------------------------------------

        private sealed class CategoryTotals
        {
            public string Id;
            public long Income;
            public long Expenses;
            public readonly List<KeyValuePair<int, int>> ByDay = new List<KeyValuePair<int, int>>(); // offset -> signed
            public readonly Dictionary<EventGoldKind, int> Elsewhere = new Dictionary<EventGoldKind, int>();
        }

        private void BuildCategories(List<RBMClanFinanceLedger.DayRecord> days)
        {
            string netName = new TextObject("{=RBM_LEDGER_CF_NET}Net").ToString();
            if (days.Count == 0)
            {
                // Never null: the panel binds a widget to it even while hidden.
                NetRow = new RBMLedgerFinanceRowVM(netName, "0", "0", "0", null);
                return;
            }
            int today = days[days.Count - 1].Day;
            var totals = new Dictionary<string, CategoryTotals>();
            long netIncome = 0L;
            long netExpenses = 0L;
            var netByDay = new List<KeyValuePair<int, int>>();
            for (int i = days.Count - 1; i >= 0; i--)
            {
                RBMClanFinanceLedger.DayRecord d = days[i];
                int offset = today - d.Day;
                foreach (KeyValuePair<string, int> row in d.Rows)
                {
                    CategoryTotals t = Totals(totals, row.Key);
                    if (row.Value > 0)
                    {
                        t.Income += row.Value;
                    }
                    else
                    {
                        t.Expenses -= row.Value;
                    }
                    t.ByDay.Add(new KeyValuePair<int, int>(offset, row.Value));
                }
                // Event gold that went to another clan member's purse: not a row of its own (it never touched
                // the player's gold), noted on the kind's row instead.
                foreach (KeyValuePair<EventGoldKind, int> kv in d.Elsewhere)
                {
                    CategoryTotals t = Totals(totals, RBMClanFinanceLedger.RowEventPrefix + kv.Key.ToString());
                    int had;
                    t.Elsewhere.TryGetValue(kv.Key, out had);
                    t.Elsewhere[kv.Key] = had + kv.Value;
                }
                netIncome += _income[i];
                netExpenses += _expenses[i];
                if (d.Net != 0)
                {
                    netByDay.Add(new KeyValuePair<int, int>(offset, d.Net));
                }
            }

            var ordered = new List<CategoryTotals>(totals.Values);
            ordered.Sort((a, b) =>
            {
                int g = RowGroup(a.Id).CompareTo(RowGroup(b.Id));
                if (g != 0)
                {
                    return g;
                }
                return (b.Income + b.Expenses).CompareTo(a.Income + a.Expenses);
            });
            foreach (CategoryTotals t in ordered)
            {
                string name = RowName(t.Id);
                Categories.Add(new RBMLedgerFinanceRowVM(name, t.Income.ToString(), t.Expenses.ToString(),
                    Signed(t.Income - t.Expenses), CategoryHint(name, t)));
            }

            RBMClanFinanceLedger.DayRecord first = days[0];
            RBMClanFinanceLedger.DayRecord last = days[days.Count - 1];
            var sb = new StringBuilder();
            sb.Append(new TextObject("{=RBM_LEDGER_CF_HINT_HDR}{NAME}, last {DAYS} days")
                .SetTextVariable("NAME", netName).SetTextVariable("DAYS", days.Count).ToString());
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_CF_OPENING}Opening balance: {GOLD}").SetTextVariable("GOLD", first.StartGold).ToString());
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_CF_NOW}Now: {GOLD}").SetTextVariable("GOLD", last.EndGold).ToString());
            AppendDays(sb, netByDay);
            string netText = sb.ToString();
            NetRow = new RBMLedgerFinanceRowVM(netName, netIncome.ToString(), netExpenses.ToString(),
                Signed(netIncome - netExpenses), new BasicTooltipViewModel(() => netText));
        }

        private static CategoryTotals Totals(Dictionary<string, CategoryTotals> totals, string id)
        {
            CategoryTotals t;
            if (!totals.TryGetValue(id, out t))
            {
                t = new CategoryTotals { Id = id };
                totals[id] = t;
            }
            return t;
        }

        private static BasicTooltipViewModel CategoryHint(string name, CategoryTotals t)
        {
            var sb = new StringBuilder();
            sb.Append(new TextObject("{=RBM_LEDGER_CF_HINT_HDR}{NAME}, last {DAYS} days")
                .SetTextVariable("NAME", name).SetTextVariable("DAYS", RBMClanFinanceLedger.HistoryDays).ToString());
            string note = RowNote(t.Id);
            if (!string.IsNullOrEmpty(note))
            {
                sb.Append('\n').Append(note);
            }
            foreach (KeyValuePair<EventGoldKind, int> kv in t.Elsewhere)
            {
                sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_CF_ELSEWHERE}Also {GOLD} to or from other clan members' purses, not counted here (it reaches you through party income).")
                    .SetTextVariable("GOLD", kv.Value).ToString());
            }
            AppendDays(sb, t.ByDay);
            string text = sb.ToString();
            return new BasicTooltipViewModel(() => text);
        }

        // "Today: +120", "-3d: -40", newest first; the list is already newest first.
        private static void AppendDays(StringBuilder sb, List<KeyValuePair<int, int>> byDay)
        {
            foreach (KeyValuePair<int, int> l in byDay)
            {
                sb.Append('\n').Append(DayName(l.Key)).Append(": ").Append(Signed(l.Value));
            }
        }

        private static int RowGroup(string id)
        {
            if (id == RBMClanFinanceLedger.RowOther)
            {
                return GroupOther;
            }
            if (id == RBMClanFinanceLedger.RowSold || id == RBMClanFinanceLedger.RowBought || id == RBMClanFinanceLedger.RowTradeAdjust)
            {
                return GroupTrade;
            }
            if (id.Length > 0 && id[0] == RBMClanFinanceLedger.RowEventPrefix)
            {
                return GroupEvents;
            }
            return GroupLines;
        }

        // The display name of a row id.
        private static string RowName(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "?";
            }
            switch (id)
            {
                case RBMClanFinanceLedger.RowOther: return new TextObject("{=RBM_LEDGER_CF_OTHER}Other (not itemised)").ToString();
                case RBMClanFinanceLedger.RowApplyAdjust: return new TextObject("{=RBM_LEDGER_CF_ADJ}Daily gold change, unlisted part").ToString();
                case RBMClanFinanceLedger.RowApplyWhole: return new TextObject("{=RBM_LEDGER_CF_APPLY}Daily gold change").ToString();
                case RBMClanFinanceLedger.RowTradeAdjust: return new TextObject("{=RBM_LEDGER_CF_TRADE_ADJ}Trade settlement difference").ToString();
                case RBMClanFinanceLedger.RowSold: return new TextObject("{=RBM_LEDGER_CF_SOLD}Goods sold").ToString();
                case RBMClanFinanceLedger.RowBought: return new TextObject("{=RBM_LEDGER_CF_BOUGHT}Goods bought").ToString();
            }
            if (id[0] == RBMClanFinanceLedger.RowEventPrefix)
            {
                EventGoldKind kind;
                if (Enum.TryParse(id.Substring(1), out kind))
                {
                    return KindName(kind);
                }
                return id.Substring(1);
            }
            return RBMClanFinanceLedger.LineName(id);
        }

        // A one-line explanation for the rows that are not self-explanatory.
        private static string RowNote(string id)
        {
            switch (id)
            {
                case RBMClanFinanceLedger.RowOther:
                    return new TextObject("{=RBM_LEDGER_CF_OTHER_NOTE}The rest of the change in your gold: loot, quests, ransoms, gambling, gifts and anything else no line names.").ToString();
                case RBMClanFinanceLedger.RowApplyAdjust:
                    return new TextObject("{=RBM_LEDGER_CF_ADJ_NOTE}What the daily gold change moved beyond its listed lines: rounding, a bill your purse could not pay in full, or a wage taken straight from your purse.").ToString();
                case RBMClanFinanceLedger.RowApplyWhole:
                    return new TextObject("{=RBM_LEDGER_CF_APPLY_NOTE}The daily gold change, whose lines could not be read.").ToString();
                case RBMClanFinanceLedger.RowTradeAdjust:
                    return new TextObject("{=RBM_LEDGER_CF_TRADE_ADJ_NOTE}The difference between the prices on the trade screen and the gold that actually changed hands, as when a merchant could not pay in full.").ToString();
                default:
                    return null;
            }
        }

        private static string KindName(EventGoldKind kind)
        {
            switch (kind)
            {
                case EventGoldKind.LeaderCut: return new TextObject("{=RBM_LEDGER_CF_EV_LEADER_CUT}Leader's cut of spoils").ToString();
                case EventGoldKind.CompanionSpoils: return new TextObject("{=RBM_LEDGER_CF_EV_COMPANION}Companions' share of spoils").ToString();
                case EventGoldKind.Minting: return new TextObject("{=RBM_LEDGER_CF_EV_MINTING}Mint cuts").ToString();
                case EventGoldKind.UpgradeGold: return new TextObject("{=RBM_LEDGER_CF_EV_UPGRADE}Promotions paid in gold").ToString();
                case EventGoldKind.BloodMoney: return new TextObject("{=RBM_LEDGER_CF_EV_BLOOD_MONEY}Blood money").ToString();
                case EventGoldKind.ShipScrap: return new TextObject("{=RBM_LEDGER_CF_EV_SHIP_SCRAP}Ship scrap").ToString();
                case EventGoldKind.MercenaryPay: return new TextObject("{=RBM_LEDGER_CF_EV_MERC_PAY}Mercenary companies' pay").ToString();
                default: return kind.ToString();
            }
        }

        // --- Traded goods ----------------------------------------------------------------------------------

        private sealed class TradeTotals
        {
            public ItemObject Item;
            public string Name;
            public int BoughtUnits;
            public long Paid;
            public int SoldUnits;
            public long Received;
            public readonly List<KeyValuePair<int, int[]>> ByDay = new List<KeyValuePair<int, int[]>>();
        }

        private void BuildTrades(List<RBMClanFinanceLedger.DayRecord> days)
        {
            if (days.Count == 0)
            {
                return;
            }
            int today = days[days.Count - 1].Day;
            var items = new Dictionary<string, TradeTotals>();
            for (int i = days.Count - 1; i >= 0; i--)
            {
                RBMClanFinanceLedger.DayRecord d = days[i];
                foreach (KeyValuePair<string, int[]> kv in d.Trades)
                {
                    TradeTotals t;
                    if (!items.TryGetValue(kv.Key, out t))
                    {
                        ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(kv.Key);
                        t = new TradeTotals
                        {
                            Item = item,
                            Name = (item != null && item.Name != null) ? item.Name.ToString() : kv.Key
                        };
                        items[kv.Key] = t;
                    }
                    t.BoughtUnits += kv.Value[0];
                    t.Paid += kv.Value[1];
                    t.SoldUnits += kv.Value[2];
                    t.Received += kv.Value[3];
                    t.ByDay.Add(new KeyValuePair<int, int[]>(today - d.Day, kv.Value));
                }
            }

            var groups = new Dictionary<string, List<TradeTotals>>();
            foreach (TradeTotals t in items.Values)
            {
                string cat = t.Item != null ? RBMLedgerViewModel.ClassifyOtherGood(t.Item) : "other";
                List<TradeTotals> list;
                if (!groups.TryGetValue(cat, out list))
                {
                    list = new List<TradeTotals>();
                    groups[cat] = list;
                }
                list.Add(t);
            }

            // Trade goods lead: they are what a trader moves most. Then the Towns tab's equipment order.
            var order = new List<string> { "materials" };
            foreach (string cat in RBMLedgerViewModel.OtherGoodCategoryOrder)
            {
                if (cat != "materials")
                {
                    order.Add(cat);
                }
            }
            foreach (string cat in order)
            {
                List<TradeTotals> list;
                if (!groups.TryGetValue(cat, out list) || list.Count == 0)
                {
                    continue;
                }
                list.Sort((a, b) => (b.Paid + b.Received).CompareTo(a.Paid + a.Received));
                string groupName = cat == "materials"
                    ? new TextObject("{=RBM_LEDGER_CF_TRADE_GOODS}Trade goods").ToString()
                    : RBMLedgerViewModel.OtherGoodCategoryName(cat);
                var group = new RBMLedgerTradeGroupVM(groupName);
                foreach (TradeTotals t in list)
                {
                    group.Items.Add(new RBMLedgerTradeItemVM(t.Name,
                        t.BoughtUnits.ToString(), t.Paid.ToString(), t.SoldUnits.ToString(), t.Received.ToString(),
                        TradeHint(t)));
                }
                TradeGroups.Add(group);
            }
            HasTrades = TradeGroups.Count > 0;
        }

        private static BasicTooltipViewModel TradeHint(TradeTotals t)
        {
            var sb = new StringBuilder();
            sb.Append(new TextObject("{=RBM_LEDGER_CF_HINT_HDR}{NAME}, last {DAYS} days")
                .SetTextVariable("NAME", t.Name).SetTextVariable("DAYS", RBMClanFinanceLedger.HistoryDays).ToString());
            foreach (KeyValuePair<int, int[]> l in t.ByDay)
            {
                sb.Append('\n').Append(DayName(l.Key)).Append(": ");
                bool any = false;
                if (l.Value[0] > 0 || l.Value[1] > 0)
                {
                    sb.Append(new TextObject("{=RBM_LEDGER_CF_TRADE_BOUGHT}bought {UNITS} for {GOLD}g")
                        .SetTextVariable("UNITS", l.Value[0]).SetTextVariable("GOLD", l.Value[1]).ToString());
                    any = true;
                }
                if (l.Value[2] > 0 || l.Value[3] > 0)
                {
                    if (any)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(new TextObject("{=RBM_LEDGER_CF_TRADE_SOLD}sold {UNITS} for {GOLD}g")
                        .SetTextVariable("UNITS", l.Value[2]).SetTextVariable("GOLD", l.Value[3]).ToString());
                }
            }
            string text = sb.ToString();
            return new BasicTooltipViewModel(() => text);
        }

        // --- Chart -----------------------------------------------------------------------------------------

        private void ExecuteMetricIncome() => SelectMetric(MetricIncome);
        private void ExecuteMetricExpenses() => SelectMetric(MetricExpenses);
        private void ExecuteMetricNet() => SelectMetric(MetricNet);
        private void ExecuteMetricGold() => SelectMetric(MetricGold);

        private void SelectMetric(string metric)
        {
            IsIncomeSelected = metric == MetricIncome;
            IsExpensesSelected = metric == MetricExpenses;
            IsNetSelected = metric == MetricNet;
            IsGoldSelected = metric == MetricGold;
            switch (metric)
            {
                case MetricIncome: MetricName = IncomeButtonText; break;
                case MetricExpenses: MetricName = ExpensesButtonText; break;
                case MetricGold: MetricName = GoldButtonText; break;
                default: MetricName = NetButtonText; break;
            }
            RebuildBars(metric);
        }

        // Bars scaled to the selected series' largest magnitude. Net draws a losing day as a red bar of its
        // size; expenses are red throughout.
        private void RebuildBars(string metric)
        {
            int[] series;
            switch (metric)
            {
                case MetricIncome: series = _income; break;
                case MetricExpenses: series = _expenses; break;
                case MetricGold: series = _gold; break;
                default: series = _net; break;
            }
            int max = 1;
            for (int i = 0; i < series.Length; i++)
            {
                int magnitude = Math.Abs(series[i]);
                if (magnitude > max)
                {
                    max = magnitude;
                }
            }
            // For Net the axis reads the size of a day either way; the bar's colour carries the sign.
            bool signed = metric == MetricNet;
            AxisMax = max.ToString();
            AxisMid = (max / 2).ToString();

            var bars = new MBBindingList<RBMLedgerBarVM>();
            for (int i = 0; i < series.Length; i++)
            {
                int val = series[i];
                int magnitude = Math.Abs(val);
                int h = magnitude == 0 ? 0 : (int)MathF.Round((float)magnitude / max * 100f);
                if (magnitude > 0 && h < 2)
                {
                    h = 2;
                }
                bool red = metric == MetricExpenses || (signed && val < 0);
                string shown = signed ? Signed(val) : val.ToString();
                string hintText = MetricName + ": " + shown + " (" + _barDays[i] + ")";
                bars.Add(new RBMLedgerBarVM(h, _barLabels[i], red, new BasicTooltipViewModel(() => hintText)));
            }
            Bars = bars;
        }

        // --- Helpers ---------------------------------------------------------------------------------------

        private static string DayName(int offset)
        {
            return offset == 0 ? new TextObject("{=RBM_LEDGER_TODAY}Today").ToString() : "-" + offset + "d";
        }

        private static string Signed(long gold)
        {
            return gold > 0 ? "+" + gold : gold.ToString();
        }

        // --- Bindings --------------------------------------------------------------------------------------

        [DataSourceProperty] public bool HasData { get; }
        [DataSourceProperty] public string Intro { get; private set; }
        [DataSourceProperty] public string GoldText { get; private set; }
        [DataSourceProperty] public string NetText { get; private set; }
        [DataSourceProperty] public string AverageText { get; private set; }
        [DataSourceProperty] public MBBindingList<RBMLedgerFinanceDayVM> History { get; }
        [DataSourceProperty] public MBBindingList<RBMLedgerFinanceRowVM> Categories { get; }
        [DataSourceProperty] public RBMLedgerFinanceRowVM NetRow { get; private set; }
        [DataSourceProperty] public MBBindingList<RBMLedgerTradeGroupVM> TradeGroups { get; }
        [DataSourceProperty] public bool HasTrades { get; private set; }

        [DataSourceProperty] public string IncomeButtonText { get; }
        [DataSourceProperty] public string ExpensesButtonText { get; }
        [DataSourceProperty] public string NetButtonText { get; }
        [DataSourceProperty] public string GoldButtonText { get; }

        // Column headings (a widget inside this scope can only bind to this view model).
        [DataSourceProperty] public string DayHeader => RBMLedgerHeaders.Day;
        [DataSourceProperty] public string IncomeHeader => RBMLedgerHeaders.FinanceIncome;
        [DataSourceProperty] public string ExpensesHeader => RBMLedgerHeaders.FinanceExpenses;
        [DataSourceProperty] public string NetHeader => RBMLedgerHeaders.FinanceNet;
        [DataSourceProperty] public string GoldHeader => RBMLedgerHeaders.FinanceGold;
        [DataSourceProperty] public string SourceHeader => RBMLedgerHeaders.FinanceSource;
        [DataSourceProperty] public string ItemHeader => RBMLedgerHeaders.FinanceItem;
        [DataSourceProperty] public string BoughtHeader => RBMLedgerHeaders.FinanceBought;
        [DataSourceProperty] public string PaidHeader => RBMLedgerHeaders.FinancePaid;
        [DataSourceProperty] public string SoldHeader => RBMLedgerHeaders.FinanceSold;
        [DataSourceProperty] public string ReceivedHeader => RBMLedgerHeaders.FinanceReceived;

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
        public bool IsIncomeSelected
        {
            get => _isIncomeSelected;
            set { if (value != _isIncomeSelected) { _isIncomeSelected = value; OnPropertyChangedWithValue(value, "IsIncomeSelected"); } }
        }

        [DataSourceProperty]
        public bool IsExpensesSelected
        {
            get => _isExpensesSelected;
            set { if (value != _isExpensesSelected) { _isExpensesSelected = value; OnPropertyChangedWithValue(value, "IsExpensesSelected"); } }
        }

        [DataSourceProperty]
        public bool IsNetSelected
        {
            get => _isNetSelected;
            set { if (value != _isNetSelected) { _isNetSelected = value; OnPropertyChangedWithValue(value, "IsNetSelected"); } }
        }

        [DataSourceProperty]
        public bool IsGoldSelected
        {
            get => _isGoldSelected;
            set { if (value != _isGoldSelected) { _isGoldSelected = value; OnPropertyChangedWithValue(value, "IsGoldSelected"); } }
        }
    }
}
