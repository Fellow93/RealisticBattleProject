using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.ScreenSystem;

namespace RBMCampaign
{
    // Backing view model for the RBM Ledger screen (RBMLedger.xml). Extensible shell: a data-bound
    // tab strip on the left and a content area on the right that shows EITHER a plain text pane
    // (placeholder tabs) OR the villages panel, toggled by the selected tab's id.
    public class RBMLedgerViewModel : ViewModel
    {
        private const string TabVillages = "villages";
        private const string TabTowns = "towns";

        private MBBindingList<RBMLedgerTabVM> _tabs;
        private MBBindingList<RBMLedgerTownGroupVM> _townGroups;
        private MBBindingList<RBMLedgerFactionGroupVM> _factionGroups;
        private string _titleText;
        private string _closeText;
        private string _currentTitle;
        private string _currentContent;
        private bool _showVillages;
        private bool _showTowns;
        private bool _showText;

        public RBMLedgerViewModel()
        {
            _tabs = new MBBindingList<RBMLedgerTabVM>();
            _townGroups = new MBBindingList<RBMLedgerTownGroupVM>();
            _factionGroups = new MBBindingList<RBMLedgerFactionGroupVM>();
            TitleText = new TextObject("{=RBM_LEDGER_TITLE}RBM Ledger").ToString();
            CloseText = new TextObject("{=RBM_LEDGER_CLOSE}Close").ToString();
            BuildVillages();
            BuildTowns();
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
            AddTab(TabTowns,
                new TextObject("{=RBM_LEDGER_TAB_TOWNS}Towns").ToString(),
                new TextObject("{=RBM_LEDGER_TAB_TOWNS}Towns").ToString(),
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
            ShowTowns = tab.TabId == TabTowns;
            ShowText = !ShowVillages && !ShowTowns;
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

        // Copies a series into an array of length n with its NEWEST entry at index n-1, padding the front
        // with zeros. Used for series added after others so their short early history still lines up with
        // the shared "today at the right" day axis.
        private static int[] RightAlign(int[] src, int n)
        {
            int[] dst = new int[n];
            for (int j = 0; j < src.Length && j < n; j++)
            {
                dst[n - 1 - j] = src[src.Length - 1 - j];
            }
            return dst;
        }

        // String counterpart of RightAlign; empty columns fill with "-" so a missing day reads as "no
        // delivery" rather than a null that BuildVillagerGoodsHint would have to special-case downstream.
        private static string[] RightAlignStr(string[] src, int n)
        {
            var dst = new string[n];
            for (int i = 0; i < n; i++)
            {
                dst[i] = "-";
            }
            for (int j = 0; j < src.Length && j < n; j++)
            {
                dst[n - 1 - j] = src[src.Length - 1 - j];
            }
            return dst;
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

        // --- Towns projection ------------------------------------------------

        // Projects RBMTownLedger's persisted history + live economy state onto the faction -> town tree.
        // Every town is listed, grouped by its current map faction (kingdom, or owning clan when independent).
        private void BuildTowns()
        {
            if (Campaign.Current == null)
            {
                return;
            }

            var groupsByKey = new Dictionary<string, RBMLedgerFactionGroupVM>();
            var orderedGroups = new List<RBMLedgerFactionGroupVM>();

            IEnumerable<Settlement> towns = Settlement.All
                .Where(s => s.IsTown && s.Town != null)
                .OrderBy(s => TownFactionName(s))
                .ThenBy(s => s.Name != null ? s.Name.ToString() : string.Empty);

            foreach (Settlement s in towns)
            {
                string key = TownFactionName(s);
                if (!groupsByKey.TryGetValue(key, out RBMLedgerFactionGroupVM group))
                {
                    group = new RBMLedgerFactionGroupVM(key);
                    groupsByKey[key] = group;
                    orderedGroups.Add(group);
                }
                group.Towns.Add(BuildTownRow(s.Town));
            }

            foreach (RBMLedgerFactionGroupVM g in orderedGroups)
            {
                FactionGroups.Add(g);
            }
        }

        private static string TownFactionName(Settlement settlement)
        {
            return settlement.MapFaction != null && settlement.MapFaction.Name != null
                ? settlement.MapFaction.Name.ToString()
                : new TextObject("{=RBM_LEDGER_INDEPENDENT}Independent").ToString();
        }

        private static RBMLedgerTownVM BuildTownRow(Town town)
        {
            Settlement settlement = town.Settlement;
            string id = settlement.StringId;
            int[] prosperity = RBMTownLedger.GetSeries("prosperity", id);
            int[] citizen = RBMTownLedger.GetSeries("citizen", id);
            int[] settlementW = RBMTownLedger.GetSeries("settlement", id);
            int[] food = RBMTownLedger.GetSeries("food", id);
            int[] garrison = RBMTownLedger.GetSeries("garrison", id);
            int[] militia = RBMTownLedger.GetSeries("militia", id);
            int[] villager = RBMTownLedger.GetSeries("villager", id);
            int[] party = RBMTownLedger.GetSeries("party", id);
            int[] caravan = RBMTownLedger.GetSeries("caravan", id);
            int[] foodCitizens = RBMTownLedger.GetSeries("foodCitizens", id);
            int[] foodGarrison = RBMTownLedger.GetSeries("foodGarrison", id);
            int[] foodMilitia = RBMTownLedger.GetSeries("foodMilitia", id);
            string[] villagerGoods = RBMTownLedger.GetVillagerGoodsSeries(id);
            string[] citizenFlow = RBMTownLedger.GetCitizenFlowSeries(id);
            string[] settlementFlow = RBMTownLedger.GetSettlementFlowSeries(id);

            int n = prosperity.Length;
            n = System.Math.Max(n, citizen.Length);
            n = System.Math.Max(n, settlementW.Length);
            n = System.Math.Max(n, food.Length);
            n = System.Math.Max(n, garrison.Length);
            n = System.Math.Max(n, militia.Length);
            n = System.Math.Max(n, villager.Length);
            n = System.Math.Max(n, party.Length);
            n = System.Math.Max(n, caravan.Length);
            n = System.Math.Max(n, foodCitizens.Length);
            n = System.Math.Max(n, villagerGoods.Length);
            n = System.Math.Max(n, citizenFlow.Length);
            n = System.Math.Max(n, settlementFlow.Length);

            // The string breakdown series were added later than the numeric ones, so they right-align to
            // today the same way, padded with "-" for the pre-feature days.
            villagerGoods = RightAlignStr(villagerGoods, n);
            citizenFlow = RightAlignStr(citizenFlow, n);
            settlementFlow = RightAlignStr(settlementFlow, n);

            // The food series were added later than the others, so on an existing save they are shorter
            // than the full window for their first 30 days. The history table and chart index every series
            // by the same day axis (i = n-1 is today), so a shorter series has to be right-aligned to today
            // rather than left-aligned to the oldest column, or a town's recent food would read as zero and
            // its old columns would show today's figures. Padded with leading zeros for the pre-feature days.
            foodCitizens = RightAlign(foodCitizens, n);
            foodGarrison = RightAlign(foodGarrison, n);
            foodMilitia = RightAlign(foodMilitia, n);

            // The eaten total the chart and history column show is the per-day sum of the three legs; the
            // legs themselves stay around for the per-day breakdown hint.
            int[] foodEaten = new int[n];
            for (int i = 0; i < n; i++)
            {
                foodEaten[i] = foodCitizens[i] + foodGarrison[i] + foodMilitia[i];
            }

            int lastDay = RBMTownLedger.LastDay;
            var barLabels = new string[n];
            var barEvent = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int offset = (n - 1) - i;
                int day = lastDay - offset;
                barLabels[i] = offset == 0 ? "T" : "-" + offset;
                barEvent[i] = RBMTownLedger.GetEventsForDay(id, day).Count > 0;
            }

            var history = new MBBindingList<RBMLedgerTownDayVM>();
            for (int i = n - 1; i >= 0; i--)
            {
                int offset = (n - 1) - i;
                int day = lastDay - offset;
                string label = offset == 0
                    ? new TextObject("{=RBM_LEDGER_TODAY}Today").ToString()
                    : "-" + offset + "d";
                List<string> evPretty = FormatTownEventList(RBMTownLedger.GetEventsForDay(id, day));
                string evCount = evPretty.Count > 0 ? evPretty.Count.ToString() : "-";
                BasicTooltipViewModel evHint = null;
                if (evPretty.Count > 0)
                {
                    string evText = string.Join("\n", evPretty);
                    evHint = new BasicTooltipViewModel(() => evText);
                }
                BasicTooltipViewModel eatenHint = BuildEatenHint(foodEaten[i], foodCitizens[i], foodGarrison[i], foodMilitia[i]);
                BasicTooltipViewModel villagerHint = BuildVillagerGoodsHint(villagerGoods[i]);
                FlowDay citFlow = BuildFlowDay(citizenFlow[i]);
                FlowDay setFlow = BuildFlowDay(settlementFlow[i]);
                history.Add(new RBMLedgerTownDayVM(label, At(prosperity, i), At(citizen, i), At(settlementW, i),
                    At(food, i), At(garrison, i), At(militia, i), At(villager, i), villagerHint, At(party, i), At(caravan, i),
                    foodEaten[i], eatenHint,
                    citFlow.Income, citFlow.IncomeHint, citFlow.Expense, citFlow.ExpenseHint,
                    setFlow.Income, setFlow.IncomeHint, setFlow.Expense, setFlow.ExpenseHint,
                    evCount, evHint));
            }

            int garrisonNow = town.GarrisonParty != null ? town.GarrisonParty.MemberRoster.TotalManCount : 0;

            // Headline state columns read live so they populate on first open (before any daily
            // snapshot); the three flow columns show the last completed day's total.
            return new RBMLedgerTownVM(
                settlement.Name.ToString(),
                ((int)MathF.Round(town.Prosperity)).ToString(),
                SettlementWealth.GetCitizenWealth(settlement).ToString(),
                SettlementWealth.GetSettlementWealth(settlement).ToString(),
                RBMTownFoodSupply.FoodUnitsInMarket(town).ToString(),
                garrisonNow.ToString(),
                ((int)MathF.Round(settlement.Militia)).ToString(),
                Latest(villager), Latest(party), Latest(caravan),
                history, BuildFoodHint(town), BuildDemandTiers(town), BuildGoods(town), BuildOtherGoods(town), BuildWorkshops(town),
                prosperity, citizen, settlementW, food, garrison, militia, villager, party, caravan, foodEaten,
                barLabels, barEvent);
        }

        // Per-food-item breakdown of what's physically on the town market roster, for the food hover.
        private static BasicTooltipViewModel BuildFoodHint(Town town)
        {
            var lines = new List<KeyValuePair<string, int>>();
            int total = 0;
            ItemRoster roster = town.Owner != null ? town.Owner.ItemRoster : null;
            if (roster != null)
            {
                for (int i = roster.Count - 1; i >= 0; i--)
                {
                    ItemRosterElement e = roster.GetElementCopyAtIndex(i);
                    ItemObject item = e.EquipmentElement.Item;
                    if (item != null && item.ItemCategory != null
                        && item.ItemCategory.Properties == ItemCategory.Property.BonusToFoodStores)
                    {
                        string name = item.Name != null ? item.Name.ToString() : item.StringId;
                        lines.Add(new KeyValuePair<string, int>(name, e.Amount));
                        total += e.Amount;
                    }
                }
            }
            lines.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder();
            sb.Append(new TextObject("{=RBM_LEDGER_FOOD_HINT_HDR}Food in market").ToString())
              .Append(" (").Append(total).Append(')');
            if (lines.Count == 0)
            {
                sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_NONE}(none)").ToString());
            }
            else
            {
                foreach (var l in lines)
                {
                    sb.Append('\n').Append(l.Key).Append(": ").Append(l.Value);
                }
            }

            // Today's ration, read live, broken down by who eats it -- the same figure the chart's Eaten
            // metric and the history column carry, shown here against the stock it is drawn from.
            RBMTownFoodSupply.FoodConsumptionBreakdown eaten = RBMTownFoodSupply.GetFoodConsumption(town);
            sb.Append('\n').Append('\n');
            AppendEatenLines(sb, eaten.Total, eaten.Citizens, eaten.Garrison, eaten.Militia);

            string text = sb.ToString();
            return new BasicTooltipViewModel(() => text);
        }

        // The food-eaten breakdown: the day's whole-town ration and the three mouths it feeds. Shared by the
        // live Food tooltip and each history row's Eaten cell so the two never word it differently.
        private static void AppendEatenLines(System.Text.StringBuilder sb, int total, int citizens, int garrison, int militia)
        {
            sb.Append(new TextObject("{=RBM_LEDGER_EATEN_HDR}Food eaten / day").ToString())
              .Append(" (").Append(total).Append(')');
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_T_CITIZEN}Citizen").ToString()).Append(": ").Append(citizens);
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_T_GARRISON}Garrison").ToString()).Append(": ").Append(garrison);
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_T_MILITIA}Militia").ToString()).Append(": ").Append(militia);
        }

        private static BasicTooltipViewModel BuildEatenHint(int total, int citizens, int garrison, int militia)
        {
            var sb = new System.Text.StringBuilder();
            AppendEatenLines(sb, total, citizens, garrison, militia);
            string text = sb.ToString();
            return new BasicTooltipViewModel(() => text);
        }

        // The goods behind a day's villager "Delivered" gold, parsed from the stored "itemId=units=gold;..."
        // column into a name/units/gold list ordered by contribution. Null when the day saw no delivery, so
        // the Delivered cell simply has no tooltip that day.
        private static BasicTooltipViewModel BuildVillagerGoodsHint(string column)
        {
            if (string.IsNullOrEmpty(column) || column == "-")
            {
                return null;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append(new TextObject("{=RBM_LEDGER_DELIVERED_HDR}Delivered by villagers").ToString());
            int totalGold = 0;
            foreach (string entry in column.Split(';'))
            {
                string[] f = entry.Split('=');
                if (f.Length != 3)
                {
                    continue;
                }
                int.TryParse(f[1], out int units);
                int.TryParse(f[2], out int gold);
                totalGold += gold;
                ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(f[0]);
                string name = (item != null && item.Name != null) ? item.Name.ToString() : f[0];
                sb.Append('\n').Append(name).Append(": ")
                  .Append(new TextObject("{=RBM_LEDGER_UNITS_GOLD}{UNITS} ({GOLD}g)")
                      .SetTextVariable("UNITS", units).SetTextVariable("GOLD", gold).ToString());
            }
            sb.Append('\n').Append('\n')
              .Append(new TextObject("{=RBM_LEDGER_TOTAL}Total").ToString()).Append(": ").Append(totalGold).Append('g');
            string text = sb.ToString();
            return new BasicTooltipViewModel(() => text);
        }

        // A day's income and expense for one wealth pool, parsed from its "source=net;..." column: the two
        // totals for the columns, and a per-category hint for each (null when that side saw nothing).
        private struct FlowDay
        {
            public int Income;
            public int Expense;
            public BasicTooltipViewModel IncomeHint;
            public BasicTooltipViewModel ExpenseHint;
        }

        private static FlowDay BuildFlowDay(string column)
        {
            var day = default(FlowDay);
            var income = new List<KeyValuePair<string, int>>();
            var expense = new List<KeyValuePair<string, int>>();
            if (!string.IsNullOrEmpty(column) && column != "-")
            {
                foreach (string entry in column.Split(';'))
                {
                    int eq = entry.IndexOf('=');
                    if (eq <= 0 || !int.TryParse(entry.Substring(eq + 1), out int net) || net == 0)
                    {
                        continue;
                    }
                    string source = entry.Substring(0, eq);
                    if (net > 0)
                    {
                        income.Add(new KeyValuePair<string, int>(source, net));
                        day.Income += net;
                    }
                    else
                    {
                        expense.Add(new KeyValuePair<string, int>(source, -net));
                        day.Expense += -net;
                    }
                }
            }
            day.IncomeHint = BuildFlowHint(new TextObject("{=RBM_LEDGER_INCOME}Income").ToString(), income, day.Income);
            day.ExpenseHint = BuildFlowHint(new TextObject("{=RBM_LEDGER_EXPENSE}Expense").ToString(), expense, day.Expense);
            return day;
        }

        // A titled, gold-descending list of one side's categories, or null when the side is empty so the
        // cell simply has no tooltip that day. Source tokens are prettified for display ("garrison-wage" ->
        // "Garrison wage").
        private static BasicTooltipViewModel BuildFlowHint(string header, List<KeyValuePair<string, int>> lines, int total)
        {
            if (lines.Count == 0)
            {
                return null;
            }
            lines.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder();
            sb.Append(header).Append(" (").Append(total).Append("g)");
            foreach (var l in lines)
            {
                sb.Append('\n').Append(PrettifySource(l.Key)).Append(": ").Append(l.Value).Append('g');
            }
            string text = sb.ToString();
            return new BasicTooltipViewModel(() => text);
        }

        // Precise display names for the ledger's income/expense categories, keyed off the SettlementWealth
        // source tokens so the table cannot drift from the tokens it labels. A token with no entry falls
        // back to its auto-prettified form (see PrettifySource), so a source added later still reads
        // sensibly until it is named here.
        private static readonly Dictionary<string, string> SourceNames = new Dictionary<string, string>
        {
            { SettlementWealth.Source.Tariff,         "Market tariff" },
            { SettlementWealth.Source.Trade,          "Market trade" },
            { SettlementWealth.Source.Commission,     "Stall commission" },
            { SettlementWealth.Source.Delivery,       "Villager deliveries" },
            { SettlementWealth.Source.Homecoming,     "Village earnings" },
            { SettlementWealth.Source.VillageDemand,  "Village spending" },
            { SettlementWealth.Source.Maintenance,    "Troop kit maintenance" },
            { SettlementWealth.Source.Upgrade,        "Troop upgrades" },
            { SettlementWealth.Source.TroopGoods,     "Troop provisions" },
            { SettlementWealth.Source.Carousing,      "Soldiers carousing" },
            { SettlementWealth.Source.Surgery,        "Field surgery" },
            { SettlementWealth.Source.GarrisonWage,   "Garrison wages" },
            { SettlementWealth.Source.GarrisonFood,   "Garrison food" },
            { SettlementWealth.Source.GarrisonRecruit,"Garrison recruit kit" },
            { SettlementWealth.Source.Militia,        "Militia upkeep" },
            { SettlementWealth.Source.Admin,          "Administration" },
            { SettlementWealth.Source.Construction,   "Construction" },
            { SettlementWealth.Source.Boost,          "Construction labour" },
            { SettlementWealth.Source.Recruit,        "Recruit fees" },
            { SettlementWealth.Source.TownArms,       "Volunteer kit" },
            { SettlementWealth.Source.VillageArms,    "Village recruit kit" },
            { SettlementWealth.Source.CastleArms,     "Castle militia kit" },
            { SettlementWealth.Source.Caravan,        "Supply caravan" },
            { SettlementWealth.Source.CaravanInvest,  "Caravan investment" },
            { SettlementWealth.Source.CaravanRepay,   "Caravan repayment" },
            { SettlementWealth.Source.WealthTax,      "Wealth tax" },
            { SettlementWealth.Source.Ransom,         "Prisoner ransom" },
            { SettlementWealth.Source.WorkshopWages,  "Workshop wages" },
            { SettlementWealth.Source.CastleIncome,   "Castle income" },
            { SettlementWealth.Source.Dearth,         "Emergency food" },
            { SettlementWealth.Source.Seed,           "World seeding" },
            { SettlementWealth.Source.Raid,           "Raid losses" },
            { SettlementWealth.Source.Siege,          "Siege losses" },
            { SettlementWealth.Source.Sack,           "Sack losses" },
        };

        // The display label for a ledger source token: the precise name where one is defined, else the token
        // auto-prettified ("garrison-wage" -> "Garrison wage").
        private static string PrettifySource(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }
            if (SourceNames.TryGetValue(source, out string name))
            {
                return name;
            }
            string spaced = source.Replace('-', ' ');
            return char.ToUpperInvariant(spaced[0]) + spaced.Substring(1);
        }

        // Per-tier household demand (Basic/Medium/Luxury, RBM's consumption grouping): the units the
        // town's people wanted last tick and the fraction the market filled; hover for consumed/missing.
        private static MBBindingList<RBMLedgerDemandTierVM> BuildDemandTiers(Town town)
        {
            var result = new MBBindingList<RBMLedgerDemandTierVM>();
            AddDemandTier(result, new TextObject("{=RBM_LEDGER_TIER_BASIC}Basic").ToString(),
                CitizenDemand.BaseWanted(town), CitizenDemand.BaseFilled(town));
            AddDemandTier(result, new TextObject("{=RBM_LEDGER_TIER_MEDIUM}Medium").ToString(),
                CitizenDemand.MediumWanted(town), CitizenDemand.MediumFilled(town));
            AddDemandTier(result, new TextObject("{=RBM_LEDGER_TIER_LUXURY}Luxury").ToString(),
                CitizenDemand.LuxuryWanted(town), CitizenDemand.LuxuryFilled(town));
            return result;
        }

        private static void AddDemandTier(MBBindingList<RBMLedgerDemandTierVM> list, string name, int wanted, int filled)
        {
            int missing = wanted > filled ? wanted - filled : 0;
            string pct = wanted > 0 ? ((int)MathF.Round(100f * filled / wanted)).ToString() + "%" : "-";
            var sb = new System.Text.StringBuilder();
            sb.Append(name).Append(" ").Append(new TextObject("{=RBM_LEDGER_DEMAND}demand").ToString());
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_WANTED}Wanted").ToString()).Append(": ")
              .Append(wanted).Append("/").Append(new TextObject("{=RBM_LEDGER_DAY}day").ToString());
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_CONSUMED}Consumed").ToString()).Append(": ").Append(filled);
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_MISSING}Missing").ToString()).Append(": ").Append(missing);
            if (wanted == 0)
            {
                sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_TIER_INACTIVE}(tier not demanded yet)").ToString());
            }
            string hintText = sb.ToString();
            list.Add(new RBMLedgerDemandTierVM(name, wanted.ToString(), pct, new BasicTooltipViewModel(() => hintText)));
        }

        // Per-item demand-vs-stock rows for the town, sorted by current demand. Each row carries the
        // full 30-day demand/stock history in its hover tooltip.
        private static MBBindingList<RBMLedgerGoodVM> BuildGoods(Town town)
        {
            var result = new MBBindingList<RBMLedgerGoodVM>();
            string id = town.Settlement.StringId;
            ItemRoster roster = town.Owner != null ? town.Owner.ItemRoster : null;

            var rows = new List<GoodRow>();
            foreach (string gid in CitizenDemand.ModelledGoods)
            {
                string key = id + "#" + gid;
                int[] dSeries = RBMTownLedger.GetSeries("itemDemand", key);
                int[] sSeries = RBMTownLedger.GetSeries("itemSupply", key);
                ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(gid);
                int demand = (int)MathF.Round(CitizenDemand.DailyUnits(town, gid));
                int stock = (item != null && roster != null) ? roster.GetItemNumber(item) : 0;
                if (demand <= 0 && stock <= 0 && dSeries.Length == 0 && sSeries.Length == 0)
                {
                    continue; // good this town never demands or stocks
                }
                string name = item != null && item.Name != null ? item.Name.ToString() : gid;
                rows.Add(new GoodRow { Name = name, Demand = demand, Stock = stock, DemandSeries = dSeries, StockSeries = sSeries });
            }
            rows.Sort((a, b) => b.Demand.CompareTo(a.Demand));

            foreach (GoodRow r in rows)
            {
                string days = r.Demand > 0 ? ((float)r.Stock / r.Demand).ToString("0.0") : "-";
                string hintText = BuildGoodHistory(r.Name, r.DemandSeries, r.StockSeries);
                result.Add(new RBMLedgerGoodVM(r.Name, r.Demand.ToString(), r.Stock.ToString(), days,
                    new BasicTooltipViewModel(() => hintText)));
            }
            return result;
        }

        private struct GoodRow
        {
            public string Name;
            public int Demand;
            public int Stock;
            public int[] DemandSeries;
            public int[] StockSeries;
        }

        private static string BuildGoodHistory(string name, int[] demand, int[] supply)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(name).Append("  (").Append(new TextObject("{=RBM_LEDGER_HIST_OLDNEW}oldest→newest").ToString()).Append(')');
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_DEMAND}demand").ToString()).Append(": ").Append(SeriesText(demand));
            sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_SUPPLY}stock").ToString()).Append(": ").Append(SeriesText(supply));
            return sb.ToString();
        }

        private static string SeriesText(int[] s)
        {
            if (s == null || s.Length == 0)
            {
                return "-";
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // Goods physically on the town market that the citizen basket does NOT model -- war gear, mounts
        // and raw materials, bought by workshops and passing parties rather than households, so they
        // never appear in the demand table above. Split by equipment category (armour, horses, melee /
        // thrown / ranged weapons, ammo, materials, ...): each row aggregates the category's stock and
        // total market value, with the per-item breakdown on hover. Category order is fixed; sorted rows
        // are dropped when empty.
        private static readonly string[] OtherGoodCategoryOrder =
        {
            "armor", "shields", "harness", "horses", "packanimals", "livestock",
            "melee", "thrown", "ranged", "ammo", "materials", "other"
        };

        private static MBBindingList<RBMLedgerOtherGoodVM> BuildOtherGoods(Town town)
        {
            var result = new MBBindingList<RBMLedgerOtherGoodVM>();
            ItemRoster roster = town.Owner != null ? town.Owner.ItemRoster : null;
            if (roster == null)
            {
                return result;
            }

            var groups = new Dictionary<string, OtherGoodGroup>();
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement e = roster.GetElementCopyAtIndex(i);
                ItemObject item = e.EquipmentElement.Item;
                if (item == null || e.Amount <= 0)
                {
                    continue;
                }
                if (CitizenDemand.CoversItem(item))
                {
                    continue; // modelled by the citizen basket -- already in the demand table above
                }
                string cat = ClassifyOtherGood(item);
                if (!groups.TryGetValue(cat, out OtherGoodGroup g))
                {
                    g = new OtherGoodGroup { Items = new List<KeyValuePair<string, long>>() };
                    groups[cat] = g;
                }
                long value = (long)e.Amount * town.GetItemPrice(item);
                g.Stock += e.Amount;
                g.Value += value;
                string name = item.Name != null ? item.Name.ToString() : item.StringId;
                g.Items.Add(new KeyValuePair<string, long>(name + " x" + e.Amount, value));
            }

            foreach (string cat in OtherGoodCategoryOrder)
            {
                if (!groups.TryGetValue(cat, out OtherGoodGroup g) || g.Stock <= 0)
                {
                    continue;
                }
                string label = OtherGoodCategoryName(cat);
                g.Items.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new System.Text.StringBuilder();
                sb.Append(label).Append(" (").Append(g.Stock).Append(')');
                foreach (KeyValuePair<string, long> it in g.Items)
                {
                    sb.Append('\n').Append(it.Key).Append(": ").Append(it.Value);
                }
                string hintText = sb.ToString();
                result.Add(new RBMLedgerOtherGoodVM(label, g.Stock.ToString(), g.Value.ToString(),
                    new BasicTooltipViewModel(() => hintText)));
            }
            return result;
        }

        private class OtherGoodGroup
        {
            public int Stock;
            public long Value;
            public List<KeyValuePair<string, long>> Items;
        }

        // Buckets a non-basket market item into one of the fixed equipment/materials categories.
        private static string ClassifyOtherGood(ItemObject item)
        {
            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.HeadArmor:
                case ItemObject.ItemTypeEnum.BodyArmor:
                case ItemObject.ItemTypeEnum.LegArmor:
                case ItemObject.ItemTypeEnum.HandArmor:
                case ItemObject.ItemTypeEnum.ChestArmor:
                case ItemObject.ItemTypeEnum.Cape:
                    return "armor";
                case ItemObject.ItemTypeEnum.Shield:
                    return "shields";
                case ItemObject.ItemTypeEnum.HorseHarness:
                    return "harness"; // horse armour / saddles
                case ItemObject.ItemTypeEnum.Horse:
                case ItemObject.ItemTypeEnum.Animal:
                {
                    // Both mounts and beasts are typed Horse/Animal; the HorseComponent flags tell them
                    // apart -- pack animals (mules, sumpters), livestock (cattle, sheep), else rideable mount.
                    HorseComponent hc = item.HorseComponent;
                    if (hc != null && hc.IsPackAnimal)
                    {
                        return "packanimals";
                    }
                    if (hc != null && hc.IsLiveStock)
                    {
                        return "livestock";
                    }
                    return "horses";
                }
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                case ItemObject.ItemTypeEnum.Polearm:
                    return "melee";
                case ItemObject.ItemTypeEnum.Thrown:
                    return "thrown";
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Crossbow:
                case ItemObject.ItemTypeEnum.Sling:
                case ItemObject.ItemTypeEnum.Pistol:
                case ItemObject.ItemTypeEnum.Musket:
                    return "ranged";
                case ItemObject.ItemTypeEnum.Arrows:
                case ItemObject.ItemTypeEnum.Bolts:
                case ItemObject.ItemTypeEnum.SlingStones:
                case ItemObject.ItemTypeEnum.Bullets:
                    return "ammo";
                case ItemObject.ItemTypeEnum.Goods:
                    return "materials";
                default:
                    return "other";
            }
        }

        private static string OtherGoodCategoryName(string cat)
        {
            switch (cat)
            {
                case "armor": return new TextObject("{=RBM_LEDGER_OG_ARMOR}Armor").ToString();
                case "shields": return new TextObject("{=RBM_LEDGER_OG_SHIELDS}Shields").ToString();
                case "harness": return new TextObject("{=RBM_LEDGER_OG_HARNESS}Horse harness").ToString();
                case "horses": return new TextObject("{=RBM_LEDGER_OG_HORSES}Horses").ToString();
                case "packanimals": return new TextObject("{=RBM_LEDGER_OG_PACK}Pack animals").ToString();
                case "melee": return new TextObject("{=RBM_LEDGER_OG_MELEE}Melee weapons").ToString();
                case "thrown": return new TextObject("{=RBM_LEDGER_OG_THROWN}Thrown").ToString();
                case "ranged": return new TextObject("{=RBM_LEDGER_OG_RANGED}Ranged weapons").ToString();
                case "ammo": return new TextObject("{=RBM_LEDGER_OG_AMMO}Ammo").ToString();
                case "livestock": return new TextObject("{=RBM_LEDGER_OG_LIVESTOCK}Livestock").ToString();
                case "materials": return new TextObject("{=RBM_LEDGER_OG_MATERIALS}Materials").ToString();
                default: return new TextObject("{=RBM_LEDGER_OG_OTHER}Other").ToString();
            }
        }

        // One row per workshop: its consumed (all recipe inputs) and produced (all outputs) units/day,
        // with the per-good (category) input/output breakdown on hover. NOTE: workshop recipes produce
        // ItemCategories (e.g. "Tools", "Wine"), not specific tiered items, so goods group by category --
        // there is no per-item tier at the workshop-output level.
        private static MBBindingList<RBMLedgerWorkshopVM> BuildWorkshops(Town town)
        {
            var result = new MBBindingList<RBMLedgerWorkshopVM>();
            Workshop[] shops = town.Workshops;
            if (shops == null)
            {
                return result;
            }
            var model = Campaign.Current != null ? Campaign.Current.Models.WorkshopModel : null;
            foreach (Workshop shop in shops)
            {
                if (shop == null || shop.WorkshopType == null)
                {
                    continue;
                }
                var inTotals = new Dictionary<string, float>();
                var outTotals = new Dictionary<string, float>();
                float inSum = 0f, outSum = 0f;
                foreach (WorkshopType.Production p in shop.WorkshopType.Productions)
                {
                    float speed = model != null
                        ? model.GetEffectiveConversionSpeedOfProduction(shop, p.ConversionSpeed, false).ResultNumber
                        : p.ConversionSpeed;
                    AccumulateCategories(inTotals, p.Inputs, speed, ref inSum);
                    AccumulateCategories(outTotals, p.Outputs, speed, ref outSum);
                }

                string name = shop.WorkshopType.Name != null ? shop.WorkshopType.Name.ToString() : shop.WorkshopType.StringId;

                // Separate consumed/produced tooltips so hovering a column shows only that side.
                var consumedSb = new System.Text.StringBuilder();
                consumedSb.Append(name);
                AppendCategoryLines(consumedSb, new TextObject("{=RBM_LEDGER_WS_CONSUMED_HDR}Consumed / day").ToString(), inTotals);
                var producedSb = new System.Text.StringBuilder();
                producedSb.Append(name);
                AppendCategoryLines(producedSb, new TextObject("{=RBM_LEDGER_WS_PRODUCED_HDR}Produced / day").ToString(), outTotals);
                string consumedText = consumedSb.ToString();
                string producedText = producedSb.ToString();

                result.Add(new RBMLedgerWorkshopVM(name, ((int)MathF.Round(inSum)).ToString(),
                    ((int)MathF.Round(outSum)).ToString(),
                    new BasicTooltipViewModel(() => consumedText), new BasicTooltipViewModel(() => producedText)));
            }
            return result;
        }

        private static void AccumulateCategories(Dictionary<string, float> totals,
            MBReadOnlyList<(ItemCategory, int)> list, float speed, ref float sum)
        {
            if (list == null)
            {
                return;
            }
            foreach (var pair in list)
            {
                float units = speed * pair.Item2;
                if (units <= 0f)
                {
                    continue;
                }
                ItemCategory c = pair.Item1;
                string name = c != null && c.GetName() != null ? c.GetName().ToString() : "?";
                totals.TryGetValue(name, out float had);
                totals[name] = had + units;
                sum += units;
            }
        }

        // Appends a "<header>\n good: n/day\n ..." section to a workshop's hover tooltip.
        private static void AppendCategoryLines(System.Text.StringBuilder sb, string header, Dictionary<string, float> totals)
        {
            sb.Append('\n').Append(header);
            if (totals.Count == 0)
            {
                sb.Append('\n').Append(new TextObject("{=RBM_LEDGER_NONE}(none)").ToString());
                return;
            }
            var lines = new List<KeyValuePair<string, float>>(totals);
            lines.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var l in lines)
            {
                sb.Append('\n').Append(l.Key).Append(": ").Append(l.Value.ToString("0.0"));
            }
        }

        private static List<string> FormatTownEventList(List<string> tokens)
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
                    case RBMTownLedger.EvSiege: parts.Add(new TextObject("{=RBM_LEDGER_EV_SIEGE}Besieged").ToString()); break;
                    case RBMTownLedger.EvCaptured: parts.Add(new TextObject("{=RBM_LEDGER_EV_CAPTURED}Changed hands").ToString()); break;
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
        public MBBindingList<RBMLedgerFactionGroupVM> FactionGroups
        {
            get => _factionGroups;
            set { if (value != _factionGroups) { _factionGroups = value; OnPropertyChangedWithValue(value, "FactionGroups"); } }
        }

        [DataSourceProperty]
        public bool ShowVillages
        {
            get => _showVillages;
            set { if (value != _showVillages) { _showVillages = value; OnPropertyChangedWithValue(value, "ShowVillages"); } }
        }

        [DataSourceProperty]
        public bool ShowTowns
        {
            get => _showTowns;
            set { if (value != _showTowns) { _showTowns = value; OnPropertyChangedWithValue(value, "ShowTowns"); } }
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
