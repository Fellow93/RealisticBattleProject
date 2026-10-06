using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// The record behind the RBM Ledger's Clan finances tab: what actually happened to the player's gold,
    /// day by day, over the last <see cref="HistoryDays"/> days. A record, never a projection -- nothing here
    /// feeds the denar tooltip or Expected Gold.
    ///
    /// Three flows are captured, and a fourth is derived so the days always add up:
    /// <list type="number">
    /// <item><b>The daily finance apply pass.</b> <c>ClanVariablesCampaignBehavior.DailyTickClan</c> asks the
    /// clan finance model for the day's change with <c>includeDescriptions: false</c> and hands the rounded
    /// result to the leader. For the player's clan only, <see cref="CaptureApplyPassLines"/> turns the
    /// descriptions ON for that one call, so the very pass that pays the gold also names its lines -- the
    /// model is never asked a second time, so nothing RBM charges on the apply pass (maintenance, accrual
    /// pools, mercenary pay) can run twice. The lines are read after the pass from the same explainer, and
    /// whatever the player's gold moved by that the lines do not cover (a purse that could not pay in full,
    /// rounding, a leaderless party's wage taken straight from the leader) is booked as an adjustment line.</item>
    /// <item><b>One-off event gold</b>, read from <see cref="ClanEventGoldLedger"/> (not copied): only the part
    /// that touched the player's own purse (<see cref="ClanEventGoldLedger.GetDayElsewhere"/>).</item>
    /// <item><b>The player's own trades</b>, per item: the inventory screen's commit
    /// (<c>InventoryLogic.DoneLogic</c>) and any <c>SellItemsAction</c> with the main party as buyer or seller.
    /// The gold is the purse's actual movement; where the screen's per-item prices disagree with it (a
    /// merchant who could not pay in full, an accepted trader offer) the difference is its own line.</item>
    /// <item><b>Other</b> = the day's real change in the player's gold minus all of the above: loot, quests,
    /// ransoms, gambling, anything no hook names.</item>
    /// </list>
    ///
    /// Day boundaries. Every flow is filed under the campaign day it happens on, the same day key
    /// <see cref="ClanEventGoldLedger"/> uses. The gold balance that closes a day is read the first time the
    /// day is seen to have changed: before any gold moves for the player through <c>GiveGoldAction</c>, before
    /// each tracked hook records, every campaign hour, and when the ledger opens. So every TRACKED flow lands
    /// on the same side of the boundary as the gold it moved; an untracked change in the gap between midnight
    /// and that first look is simply counted in the previous day's Other, which nets out over the two days.
    /// The first day ever tracked (a new game, or a save made before this ledger existed) starts at the
    /// moment tracking starts, with the event gold already recorded that day held back as a baseline, so it
    /// never shows a fake Other.
    ///
    /// Storage: RBM's CSV-in-dictionary pattern. <c>RBM_clanFinanceHist</c> holds one CSV series per field,
    /// one column per closed day, trimmed with <see cref="RBMTownLedger"/>'s amortised trim;
    /// <c>RBM_clanFinanceDay</c> holds the open day's running totals, so a mid-day save keeps them.
    /// Finance lines are keyed by their display text (the explainer exposes no id), event kinds by their
    /// <see cref="EventGoldKind"/> name and goods by item string id.
    /// </summary>
    public static class RBMClanFinanceLedger
    {
        /// <summary>Days kept, today included: the same window as the other ledgers.</summary>
        public const int HistoryDays = RBMTownLedger.HistoryDays;

        // Row ids. The first character tells the row's kind; a finance line's display text is escaped so it
        // can never contain a reserved character, and never starts with '#'.
        public const char RowLinePrefix = 'L';
        public const char RowEventPrefix = 'E';
        public const string RowSold = "Tsold";
        public const string RowBought = "Tbought";
        public const string RowOther = "#other";
        /// <summary>The apply pass's gold movement its lines do not cover.</summary>
        public const string RowApplyAdjust = "#adj";
        /// <summary>The whole apply pass, when its lines could not be read (a replaced finance model).</summary>
        public const string RowApplyWhole = "#apply";
        /// <summary>A trade whose actual gold differs from the screen's per-item prices.</summary>
        public const string RowTradeAdjust = "#tradeadj";

        // Banked series (one column per closed day, oldest first).
        private const string SeriesDay = "day";
        private const string SeriesStart = "start";
        private const string SeriesEnd = "end";
        private const string SeriesOther = "other";
        private const string SeriesLines = "lines";
        private const string SeriesTrade = "trade";
        private const string SeriesBase = "evbase";
        private const string SeriesFlags = "flags";
        // A single value, not a series: the hero whose purse is tracked.
        private const string HeroKey = "hero";

        // Open-day keys in _day.
        private const string LineKey = "L|";
        private const string TradeKey = "T|";
        private const string BaseKey = "B|";
        private const string MetaDay = "#day";
        private const string MetaGold = "#gold";
        private const string MetaPartial = "#partial";

        private static Dictionary<string, string> _hist = new Dictionary<string, string>();
        private static Dictionary<string, int> _day = new Dictionary<string, int>();

        // The open day, its opening balance, and whether tracking began part-way through it.
        private static int _openDay = -1;
        private static int _openGold;
        private static bool _openPartial;

        private static readonly EventGoldKind[] Kinds = (EventGoldKind[])Enum.GetValues(typeof(EventGoldKind));

        private static bool Enabled
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled && Campaign.Current != null; }
        }

        private static int Today()
        {
            return (int)CampaignTime.Now.ToDays;
        }

        // Hero.MainHero dereferences the player troop, which is not there outside a campaign or while one is
        // still being created; GiveGoldAction can run in both.
        private static Hero MainHero()
        {
            if (Campaign.Current == null)
            {
                return null;
            }
            CharacterObject player = CharacterObject.PlayerCharacter;
            return player != null ? player.HeroObject : null;
        }

        private static MobileParty MainParty()
        {
            return Campaign.Current != null ? Campaign.Current.MainParty : null;
        }

        // --- Day boundary -----------------------------------------------------------------------------------

        /// <summary>
        /// Closes the open day if the campaign day has moved on, reading the balance that ends it now -- before
        /// the caller moves any gold. Cheap when nothing changed (one comparison), so it is safe on every
        /// gold movement.
        /// </summary>
        public static void RollIfNeeded()
        {
            if (!Enabled)
            {
                return;
            }
            Hero main = MainHero();
            if (main == null)
            {
                return;
            }
            int today = Today();
            if (today == _openDay)
            {
                return;
            }

            if (_openDay < 0 || today < _openDay)
            {
                // Nothing tracked yet (new game, older save), or a clock that ran backwards: start fresh.
                Open(today, main.Gold, partial: true);
                return;
            }

            // The player's hero changed (an heir took over): close the old purse where it stands and start
            // the new one, rather than book the jump between the two purses as a day's Other.
            string trackedHero;
            _hist.TryGetValue(HeroKey, out trackedHero);
            if (!string.IsNullOrEmpty(trackedHero) && trackedHero != main.StringId)
            {
                Hero previous = Hero.Find(trackedHero);
                Bank(_openDay, previous != null ? previous.Gold : _openGold + Tracked(_openDay));
                Open(today, main.Gold, partial: true);
                return;
            }

            int gold = main.Gold;
            Bank(_openDay, gold);
            // Days with no look at all (nothing moved): closed flat, so every column is one calendar day.
            int firstGap = Math.Max(_openDay + 1, today - HistoryDays);
            for (int d = firstGap; d < today; d++)
            {
                _openDay = d;
                _openGold = gold;
                _openPartial = false;
                Bank(d, gold);
            }
            Open(today, gold, partial: false);
        }

        private static void Open(int day, int gold, bool partial)
        {
            _day = new Dictionary<string, int>();
            _openDay = day;
            _openGold = gold;
            _openPartial = partial;
            Hero main = MainHero();
            _hist[HeroKey] = main != null ? main.StringId : string.Empty;
            if (!partial)
            {
                return;
            }
            // Event gold already recorded today, before tracking began: held back, or the day's reconciliation
            // would count gold that moved before its opening balance was read.
            foreach (EventGoldKind kind in Kinds)
            {
                int inPurse = EventInPurse(kind, day);
                if (inPurse != 0)
                {
                    _day[BaseKey + kind] = inPurse;
                }
            }
        }

        // Appends the open day as a closed column, ending at endGold.
        private static void Bank(int day, int endGold)
        {
            int other = (endGold - _openGold) - Tracked(day);
            RBMTownLedger.AppendInt(_hist, SeriesDay, day);
            RBMTownLedger.AppendInt(_hist, SeriesStart, _openGold);
            RBMTownLedger.AppendInt(_hist, SeriesEnd, endGold);
            RBMTownLedger.AppendInt(_hist, SeriesOther, other);
            RBMTownLedger.AppendStr(_hist, SeriesLines, LinesColumn());
            RBMTownLedger.AppendStr(_hist, SeriesTrade, TradeColumn());
            RBMTownLedger.AppendStr(_hist, SeriesBase, BaseColumn());
            RBMTownLedger.AppendStr(_hist, SeriesFlags, _openPartial ? "p" : "-");
            _day = new Dictionary<string, int>();
        }

        // Everything the open day's hooks and the event ledger account for, signed.
        private static int Tracked(int day)
        {
            long sum = 0L;
            foreach (KeyValuePair<string, int> kv in _day)
            {
                if (kv.Key.StartsWith(LineKey, StringComparison.Ordinal))
                {
                    sum += kv.Value;
                }
                else if (kv.Key.StartsWith(TradeKey, StringComparison.Ordinal))
                {
                    if (kv.Key.EndsWith("|sg", StringComparison.Ordinal))
                    {
                        sum += kv.Value;
                    }
                    else if (kv.Key.EndsWith("|bg", StringComparison.Ordinal))
                    {
                        sum -= kv.Value;
                    }
                }
            }
            foreach (EventGoldKind kind in Kinds)
            {
                sum += SignedEvent(kind, day, OpenBase(kind));
            }
            return (int)sum;
        }

        private static int OpenBase(EventGoldKind kind)
        {
            int b;
            return _day.TryGetValue(BaseKey + kind, out b) ? b : 0;
        }

        // The event gold of one kind that touched the player's purse that day, less any baseline, signed.
        private static int SignedEvent(EventGoldKind kind, int day, int baseline)
        {
            int gold = EventInPurse(kind, day) - baseline;
            return ClanEventGoldLedger.IsDrain(kind) ? -gold : gold;
        }

        private static int EventInPurse(EventGoldKind kind, int day)
        {
            return ClanEventGoldLedger.GetDay(kind, day) - ClanEventGoldLedger.GetDayElsewhere(kind, day);
        }

        // --- Recording (hooks below) -------------------------------------------------------------------------

        private static void AddLine(string rowId, int gold)
        {
            if (gold == 0)
            {
                return;
            }
            string key = LineKey + rowId;
            int running;
            _day.TryGetValue(key, out running);
            _day[key] = running + gold;
        }

        private static void AddTrade(ItemObject item, int boughtUnits, int boughtGold, int soldUnits, int soldGold)
        {
            if (item == null || string.IsNullOrEmpty(item.StringId))
            {
                return;
            }
            string prefix = TradeKey + item.StringId;
            Accumulate(prefix + "|bu", boughtUnits);
            Accumulate(prefix + "|bg", boughtGold);
            Accumulate(prefix + "|su", soldUnits);
            Accumulate(prefix + "|sg", soldGold);
        }

        private static void Accumulate(string key, int value)
        {
            if (value == 0)
            {
                return;
            }
            int running;
            _day.TryGetValue(key, out running);
            _day[key] = running + value;
        }

        // A committed trade-screen session: every item at the price the screen charged, plus the difference
        // to what the purse actually moved, so the day's trade total is the real gold.
        private static void RecordScreenTrade(List<(ItemRosterElement, int)> bought, List<(ItemRosterElement, int)> sold, int actualNet)
        {
            int listed = 0;
            if (bought != null)
            {
                for (int i = 0; i < bought.Count; i++)
                {
                    int gold = Math.Abs(bought[i].Item2);
                    AddTrade(bought[i].Item1.EquipmentElement.Item, bought[i].Item1.Amount, gold, 0, 0);
                    listed -= gold;
                }
            }
            if (sold != null)
            {
                for (int i = 0; i < sold.Count; i++)
                {
                    int gold = Math.Abs(sold[i].Item2);
                    AddTrade(sold[i].Item1.EquipmentElement.Item, 0, 0, sold[i].Item1.Amount, gold);
                    listed += gold;
                }
            }
            AddLine(RowTradeAdjust, actualNet - listed);
        }

        // The apply pass just ran for the player's clan: book its lines, and the gold they do not explain.
        private static void RecordApplyPass(int goldDelta, bool haveLines, ExplainedNumber result)
        {
            if (!haveLines)
            {
                AddLine(RowApplyWhole, goldDelta);
                return;
            }
            int booked = 0;
            // (name, value) pairs, one per distinct label: the explainer sums repeats of the same label.
            foreach (var line in result.GetLines())
            {
                int gold = MathF.Round(line.Item2);
                if (gold == 0)
                {
                    continue;
                }
                AddLine(Escape(string.IsNullOrEmpty(line.Item1) ? "?" : line.Item1), gold);
                booked += gold;
            }
            AddLine(RowApplyAdjust, goldDelta - booked);
        }

        // --- Columns ----------------------------------------------------------------------------------------

        // "rowId=gold;..." for the open day's finance lines, or "-".
        private static string LinesColumn()
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in _day)
            {
                if (kv.Value == 0 || !kv.Key.StartsWith(LineKey, StringComparison.Ordinal))
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(';');
                }
                sb.Append(kv.Key, LineKey.Length, kv.Key.Length - LineKey.Length).Append('=').Append(kv.Value);
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }

        // "itemId=boughtUnits=boughtGold=soldUnits=soldGold;..." (item ids carry no ';', '=' or ','), or "-".
        private static string TradeColumn()
        {
            Dictionary<string, int[]> items = OpenTrades();
            if (items.Count == 0)
            {
                return "-";
            }
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, int[]> kv in items)
            {
                if (sb.Length > 0)
                {
                    sb.Append(';');
                }
                sb.Append(kv.Key).Append('=').Append(kv.Value[0]).Append('=').Append(kv.Value[1])
                  .Append('=').Append(kv.Value[2]).Append('=').Append(kv.Value[3]);
            }
            return sb.ToString();
        }

        // "Kind=gold;..." for the partial first day's held-back event gold, or "-".
        private static string BaseColumn()
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in _day)
            {
                if (kv.Value == 0 || !kv.Key.StartsWith(BaseKey, StringComparison.Ordinal))
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(';');
                }
                sb.Append(kv.Key, BaseKey.Length, kv.Key.Length - BaseKey.Length).Append('=').Append(kv.Value);
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }

        // The open day's trades, itemId -> [bought units, bought gold, sold units, sold gold].
        private static Dictionary<string, int[]> OpenTrades()
        {
            var items = new Dictionary<string, int[]>();
            foreach (KeyValuePair<string, int> kv in _day)
            {
                if (!kv.Key.StartsWith(TradeKey, StringComparison.Ordinal))
                {
                    continue;
                }
                int bar = kv.Key.LastIndexOf('|');
                if (bar <= TradeKey.Length)
                {
                    continue;
                }
                string itemId = kv.Key.Substring(TradeKey.Length, bar - TradeKey.Length);
                int slot;
                switch (kv.Key.Substring(bar + 1))
                {
                    case "bu": slot = 0; break;
                    case "bg": slot = 1; break;
                    case "su": slot = 2; break;
                    case "sg": slot = 3; break;
                    default: continue;
                }
                int[] row;
                if (!items.TryGetValue(itemId, out row))
                {
                    row = new int[4];
                    items[itemId] = row;
                }
                row[slot] += kv.Value;
            }
            return items;
        }

        // Display text -> a row id safe inside "id=gold;..." columns and the ',' separated series.
        private static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            sb.Append(RowLinePrefix);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '%': sb.Append("%25"); break;
                    case ';': sb.Append("%3B"); break;
                    case '=': sb.Append("%3D"); break;
                    case ',': sb.Append("%2C"); break;
                    case '|': sb.Append("%7C"); break;
                    case '#': sb.Append("%23"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>A finance-line row id back to the display text it was recorded under.</summary>
        public static string LineName(string rowId)
        {
            if (string.IsNullOrEmpty(rowId) || rowId[0] != RowLinePrefix)
            {
                return rowId;
            }
            string text = rowId.Substring(1);
            if (text.IndexOf('%') < 0)
            {
                return text;
            }
            return text.Replace("%3B", ";").Replace("%3D", "=").Replace("%2C", ",").Replace("%7C", "|")
                .Replace("%23", "#").Replace("%25", "%");
        }

        // --- Reading (for the VM) ---------------------------------------------------------------------------

        /// <summary>One day of the record, closed or still open.</summary>
        public sealed class DayRecord
        {
            public int Day;
            public int StartGold;
            public int EndGold;
            /// <summary>Tracking began part-way through this day.</summary>
            public bool Partial;
            /// <summary>Today, still running: EndGold is the purse as it stands.</summary>
            public bool IsOpen;
            /// <summary>Row id -> signed gold: finance lines, event kinds, goods sold/bought, adjustments, Other.</summary>
            public readonly Dictionary<string, int> Rows = new Dictionary<string, int>();
            /// <summary>Event kind -> gold that went to (or came from) another clan member's purse.</summary>
            public readonly Dictionary<EventGoldKind, int> Elsewhere = new Dictionary<EventGoldKind, int>();
            /// <summary>Item id -> [bought units, bought gold, sold units, sold gold].</summary>
            public Dictionary<string, int[]> Trades = new Dictionary<string, int[]>();

            public int Net { get { return EndGold - StartGold; } }
        }

        /// <summary>
        /// The tracked days, oldest first: the newest closed days and today, at most <see cref="HistoryDays"/>.
        /// Closes a finished day first, so a day that ended while nothing moved still shows as closed.
        /// </summary>
        public static List<DayRecord> GetDays()
        {
            var result = new List<DayRecord>();
            Hero main = MainHero();
            if (!Enabled || main == null)
            {
                return result;
            }
            RollIfNeeded();
            if (_openDay < 0)
            {
                return result;
            }

            string[] days = RBMTownLedger.GetStringSeries(_hist, SeriesDay);
            string[] starts = RBMTownLedger.GetStringSeries(_hist, SeriesStart);
            string[] ends = RBMTownLedger.GetStringSeries(_hist, SeriesEnd);
            string[] others = RBMTownLedger.GetStringSeries(_hist, SeriesOther);
            string[] lines = RBMTownLedger.GetStringSeries(_hist, SeriesLines);
            string[] trades = RBMTownLedger.GetStringSeries(_hist, SeriesTrade);
            string[] bases = RBMTownLedger.GetStringSeries(_hist, SeriesBase);
            string[] flags = RBMTownLedger.GetStringSeries(_hist, SeriesFlags);

            int count = days.Length;
            int first = Math.Max(0, count - (HistoryDays - 1));
            for (int i = first; i < count; i++)
            {
                var rec = new DayRecord();
                int.TryParse(days[i], out rec.Day);
                if (rec.Day < _openDay - (HistoryDays - 1))
                {
                    continue;
                }
                rec.StartGold = IntAt(starts, i);
                rec.EndGold = IntAt(ends, i);
                rec.Partial = StrAt(flags, i) == "p";
                ParseLines(StrAt(lines, i), rec.Rows);
                rec.Trades = ParseTrades(StrAt(trades, i));
                AddTradeRows(rec);
                Dictionary<string, int> baseline = ParseBase(StrAt(bases, i));
                AddEventRows(rec, baseline);
                AddRow(rec.Rows, RowOther, IntAt(others, i));
                result.Add(rec);
            }

            // Today, as it stands.
            var open = new DayRecord
            {
                Day = _openDay,
                StartGold = _openGold,
                EndGold = main.Gold,
                Partial = _openPartial,
                IsOpen = true
            };
            foreach (KeyValuePair<string, int> kv in _day)
            {
                if (kv.Key.StartsWith(LineKey, StringComparison.Ordinal))
                {
                    AddRow(open.Rows, kv.Key.Substring(LineKey.Length), kv.Value);
                }
            }
            open.Trades = OpenTrades();
            AddTradeRows(open);
            var openBase = new Dictionary<string, int>();
            foreach (EventGoldKind kind in Kinds)
            {
                int b = OpenBase(kind);
                if (b != 0)
                {
                    openBase[kind.ToString()] = b;
                }
            }
            AddEventRows(open, openBase);
            AddRow(open.Rows, RowOther, open.Net - Tracked(_openDay));
            result.Add(open);
            return result;
        }

        private static void AddTradeRows(DayRecord rec)
        {
            int bought = 0;
            int sold = 0;
            foreach (int[] row in rec.Trades.Values)
            {
                bought += row[1];
                sold += row[3];
            }
            AddRow(rec.Rows, RowBought, -bought);
            AddRow(rec.Rows, RowSold, sold);
        }

        private static void AddEventRows(DayRecord rec, Dictionary<string, int> baseline)
        {
            foreach (EventGoldKind kind in Kinds)
            {
                int b;
                baseline.TryGetValue(kind.ToString(), out b);
                AddRow(rec.Rows, RowEventPrefix + kind.ToString(), SignedEvent(kind, rec.Day, b));
                int elsewhere = ClanEventGoldLedger.GetDayElsewhere(kind, rec.Day);
                if (elsewhere > 0)
                {
                    rec.Elsewhere[kind] = elsewhere;
                }
            }
        }

        private static void AddRow(Dictionary<string, int> rows, string id, int gold)
        {
            if (gold == 0)
            {
                return;
            }
            int running;
            rows.TryGetValue(id, out running);
            rows[id] = running + gold;
        }

        private static void ParseLines(string column, Dictionary<string, int> rows)
        {
            if (string.IsNullOrEmpty(column) || column == "-")
            {
                return;
            }
            foreach (string entry in column.Split(';'))
            {
                int eq = entry.LastIndexOf('=');
                int gold;
                if (eq <= 0 || !int.TryParse(entry.Substring(eq + 1), out gold))
                {
                    continue;
                }
                AddRow(rows, entry.Substring(0, eq), gold);
            }
        }

        private static Dictionary<string, int[]> ParseTrades(string column)
        {
            var items = new Dictionary<string, int[]>();
            if (string.IsNullOrEmpty(column) || column == "-")
            {
                return items;
            }
            foreach (string entry in column.Split(';'))
            {
                string[] f = entry.Split('=');
                if (f.Length != 5 || string.IsNullOrEmpty(f[0]))
                {
                    continue;
                }
                int[] row = new int[4];
                for (int k = 0; k < 4; k++)
                {
                    int.TryParse(f[k + 1], out row[k]);
                }
                items[f[0]] = row;
            }
            return items;
        }

        private static Dictionary<string, int> ParseBase(string column)
        {
            var result = new Dictionary<string, int>();
            ParseLines(column, result);
            return result;
        }

        private static int IntAt(string[] series, int i)
        {
            int v;
            return (i >= 0 && i < series.Length && int.TryParse(series[i], out v)) ? v : 0;
        }

        private static string StrAt(string[] series, int i)
        {
            return (i >= 0 && i < series.Length) ? series[i] : "-";
        }

        // --- Persistence ------------------------------------------------------------------------------------

        /// <summary>
        /// Drops the previous campaign's record. Called from the behaviour's constructor, which runs before a
        /// loaded save's SyncData -- so a real save repopulates it and a new or older campaign starts empty.
        /// </summary>
        public static void Reset()
        {
            _hist = new Dictionary<string, string>();
            _day = new Dictionary<string, int>();
            _openDay = -1;
            _openGold = 0;
            _openPartial = false;
            _applyActive = false;
            _applyHasResult = false;
            _applyResult = default(ExplainedNumber);
        }

        public static void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsLoading)
            {
                _hist = null;
                _day = null;
            }
            else
            {
                // The open day's identity rides in its own running totals, so one key carries the lot.
                _day[MetaDay] = _openDay;
                _day[MetaGold] = _openGold;
                _day[MetaPartial] = _openPartial ? 1 : 0;
            }

            dataStore.SyncData("RBM_clanFinanceHist", ref _hist);
            dataStore.SyncData("RBM_clanFinanceDay", ref _day);

            if (_hist == null)
            {
                _hist = new Dictionary<string, string>();
            }
            if (_day == null)
            {
                _day = new Dictionary<string, int>();
            }
            if (dataStore.IsLoading)
            {
                int v;
                _openDay = _day.TryGetValue(MetaDay, out v) ? v : -1;
                _openGold = _day.TryGetValue(MetaGold, out v) ? v : 0;
                _openPartial = _day.TryGetValue(MetaPartial, out v) && v != 0;
            }
            _day.Remove(MetaDay);
            _day.Remove(MetaGold);
            _day.Remove(MetaPartial);
        }

        // --- Hooks ------------------------------------------------------------------------------------------

        // The player clan's apply pass in progress, and the result its model call returned.
        private static bool _applyActive;
        private static int _applyGoldBefore;
        private static bool _applyHasResult;
        private static ExplainedNumber _applyResult;

        /// <summary>
        /// Brackets the player clan's daily tick, the one place the clan finance model's apply pass runs
        /// (<c>ClanVariablesCampaignBehavior.DailyTickClan</c>, which calls <c>CalculateClanGoldChange(clan,
        /// includeDescriptions: false, applyWithdrawals: true)</c> and gives the leader the rounded result).
        /// The purse is read before and after, so what the pass actually moved is known exactly.
        /// </summary>
        [HarmonyPatch(typeof(ClanVariablesCampaignBehavior), "DailyTickClan")]
        private static class PlayerApplyPassWindow
        {
            private static void Prefix(Clan clan)
            {
                _applyActive = false;
                Hero main = MainHero();
                if (!Enabled || main == null || clan == null || clan != Clan.PlayerClan || clan.Leader != main)
                {
                    return;
                }
                RollIfNeeded();
                _applyGoldBefore = main.Gold;
                _applyHasResult = false;
                _applyResult = default(ExplainedNumber);
                _applyActive = true;
            }

            private static void Postfix()
            {
                if (!_applyActive)
                {
                    return;
                }
                _applyActive = false;
                Hero main = MainHero();
                if (main != null)
                {
                    RecordApplyPass(main.Gold - _applyGoldBefore, _applyHasResult, _applyResult);
                }
                _applyHasResult = false;
                _applyResult = default(ExplainedNumber);
            }

            private static void Finalizer()
            {
                _applyActive = false;
            }
        }

        /// <summary>
        /// Names the lines of the apply pass the bracket above is watching, from that pass itself.
        ///
        /// Turning <c>includeDescriptions</c> on is side-effect free: <c>ExplainedNumber</c> keeps its sum in
        /// <c>BaseNumber</c>/<c>SumOfFactors</c> whether or not it has an explainer, and the explainer only
        /// records (name, value) pairs. <c>DefaultClanFinanceModel</c> branches on <c>IncludeDescriptions</c>
        /// only to choose between <c>Add(value, label)</c> and <c>Add(value)</c> with the same value; no RBM
        /// postfix on this method reads it. So the gold the pass pays is unchanged to the denar.
        ///
        /// The postfix runs last and keeps a copy of the result. <c>ExplainedNumber</c> is a struct whose
        /// explainer is a reference, so the copy still sees any line a wrapping model (War Sails'
        /// <c>NavalDLCClanFinanceModel</c>) adds afterwards; it is read once the daily tick returns.
        /// </summary>
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange")]
        private static class CaptureApplyPassLines
        {
            private static void Prefix(Clan clan, ref bool includeDescriptions, bool applyWithdrawals)
            {
                if (_applyActive && applyWithdrawals && clan == Clan.PlayerClan)
                {
                    includeDescriptions = true;
                }
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Clan clan, bool applyWithdrawals, ExplainedNumber __result)
            {
                if (_applyActive && applyWithdrawals && clan == Clan.PlayerClan)
                {
                    _applyResult = __result;
                    _applyHasResult = __result.IncludeDescriptions;
                }
            }
        }

        /// <summary>
        /// Closes a finished day before any gold moves for the player through <c>GiveGoldAction</c> -- to or
        /// from his hero, or the main party, whose trade gold IS his hero's gold. First, so no other prefix
        /// moves the purse ahead of the reading. One or two reference comparisons for everyone else.
        /// </summary>
        [HarmonyPatch(typeof(GiveGoldAction), "ApplyInternal")]
        private static class RollBeforeGoldMoves
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Hero giverHero, PartyBase giverParty, Hero recipientHero, PartyBase recipientParty)
            {
                Hero main = MainHero();
                if (main == null)
                {
                    return;
                }
                if (giverHero == main || recipientHero == main)
                {
                    RollIfNeeded();
                    return;
                }
                if (giverParty == null && recipientParty == null)
                {
                    return;
                }
                MobileParty mainParty = MainParty();
                PartyBase mainBase = mainParty != null ? mainParty.Party : null;
                if (mainBase != null && (giverParty == mainBase || recipientParty == mainBase))
                {
                    RollIfNeeded();
                }
            }
        }

        /// <summary>
        /// The player's trade screen commit. <c>DoneLogic</c> settles the whole visit with one
        /// <c>GiveGoldAction</c> of <c>min(-TotalAmount, merchant gold)</c> to the player, so the purse is read
        /// either side of it; the items come from the screen's own transaction history (net of anything
        /// dragged back), at the prices it charged. Only a real trade (<c>IsTrading</c>) by the main party that
        /// went through (<c>__result</c>). Not a hot path: it runs once per Done press.
        /// </summary>
        [HarmonyPatch(typeof(InventoryLogic), "DoneLogic")]
        private static class PlayerTradeScreen
        {
            [ThreadStatic] private static bool _armed;
            [ThreadStatic] private static int _goldBefore;

            private static void Prefix(InventoryLogic __instance)
            {
                _armed = false;
                Hero main = MainHero();
                MobileParty mainParty = MainParty();
                if (!Enabled || main == null || mainParty == null || __instance == null || !__instance.IsTrading
                    || __instance.OwnerParty != mainParty)
                {
                    return;
                }
                RollIfNeeded();
                _goldBefore = main.Gold;
                _armed = true;
            }

            private static void Postfix(InventoryLogic __instance, bool __result)
            {
                if (!_armed)
                {
                    return;
                }
                _armed = false;
                Hero main = MainHero();
                if (!__result || main == null)
                {
                    return;
                }
                RecordScreenTrade(__instance.GetBoughtItems(), __instance.GetSoldItems(), main.Gold - _goldBefore);
            }

            private static void Finalizer()
            {
                _armed = false;
            }
        }

        /// <summary>
        /// A <c>SellItemsAction</c> with the main party on either side. Vanilla's own trading behaviours all
        /// skip the main party, so this is a safety net for any other path; it books the purse's actual
        /// movement against the item. Every market trade in the game passes here, so the carry is
        /// allocation-free (thread-static, as <see cref="RBMTownLedger"/>'s own hook on this method) and the
        /// filter is two reference comparisons.
        /// </summary>
        [HarmonyPatch(typeof(SellItemsAction), "ApplyInternal")]
        private static class MainPartyItemTrade
        {
            [ThreadStatic] private static bool _armed;
            [ThreadStatic] private static int _goldBefore;

            private static void Prefix(PartyBase sellerParty, PartyBase buyerParty)
            {
                _armed = false;
                MobileParty mainParty = MainParty();
                if (mainParty == null || (sellerParty != mainParty.Party && buyerParty != mainParty.Party))
                {
                    return;
                }
                Hero main = MainHero();
                if (!Enabled || main == null)
                {
                    return;
                }
                RollIfNeeded();
                _goldBefore = main.Gold;
                _armed = true;
            }

            private static void Postfix(PartyBase sellerParty, ItemRosterElement itemRosterElement, int number)
            {
                if (!_armed)
                {
                    return;
                }
                _armed = false;
                Hero main = MainHero();
                MobileParty mainParty = MainParty();
                if (main == null || mainParty == null)
                {
                    return;
                }
                int delta = main.Gold - _goldBefore;
                ItemObject item = itemRosterElement.EquipmentElement.Item;
                bool sold = sellerParty == mainParty.Party;
                if (sold)
                {
                    AddTrade(item, 0, 0, number, Math.Max(0, delta));
                    AddLine(RowTradeAdjust, Math.Min(0, delta));
                }
                else
                {
                    AddTrade(item, number, Math.Max(0, -delta), 0, 0);
                    AddLine(RowTradeAdjust, Math.Max(0, delta));
                }
            }

            private static void Finalizer()
            {
                _armed = false;
            }
        }
    }
}
