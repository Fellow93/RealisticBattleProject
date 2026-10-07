using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;

namespace RBMCampaign
{
    // Persistent per-TOWN history store for the Ledger's Towns tab. Mirrors RBMVillageLedger: a rolling
    // 30-day series of seven metrics plus discrete day-stamped events, keyed by Settlement.StringId.
    //
    // Four metrics are point-in-time STATE read straight off the town each daily tick (prosperity,
    // citizen wealth, settlement treasury, food units in market). Three are FLOW totals accumulated
    // through the day and banked on the daily tick (gold value villagers delivered, gold parties spent
    // buying in the market, gold caravans spent) -- these use per-town day-accumulators fed by the
    // hooks below (VillagerDelivery for deliveries; the nested SellItemsAction patch for market buys).
    //
    // Storage follows RBM's CSV-in-Dictionary<string,string> save pattern (no int[]-valued dicts). The
    // day-accumulators are Dictionary<string,int> (which the save system has a defined container for)
    // so a mid-day save keeps the partial day.
    public static class RBMTownLedger
    {
        public const int HistoryDays = 30;

        private static Dictionary<string, string> _prosperity = new Dictionary<string, string>();
        private static Dictionary<string, string> _citizen = new Dictionary<string, string>();
        private static Dictionary<string, string> _settlement = new Dictionary<string, string>();
        private static Dictionary<string, string> _food = new Dictionary<string, string>();
        private static Dictionary<string, string> _garrison = new Dictionary<string, string>();
        private static Dictionary<string, string> _militia = new Dictionary<string, string>();
        private static Dictionary<string, string> _villager = new Dictionary<string, string>();
        private static Dictionary<string, string> _party = new Dictionary<string, string>();
        private static Dictionary<string, string> _caravan = new Dictionary<string, string>();
        // Daily food ration eaten, split three ways (see RBMTownFoodSupply.GetFoodConsumption). Stored as
        // components so the history can break a day's total down on hover; the "eaten" total the chart and
        // the history column show is their per-day sum, computed in the VM.
        private static Dictionary<string, string> _foodCitizens = new Dictionary<string, string>();
        private static Dictionary<string, string> _foodGarrison = new Dictionary<string, string>();
        private static Dictionary<string, string> _foodMilitia = new Dictionary<string, string>();
        private static Dictionary<string, string> _events = new Dictionary<string, string>();

        // Per-item demand (full-appetite units/day) and market stock, keyed "settlementId#itemId", one
        // CSV column per day. This is the heavy series -- ~modelled-goods x towns keys -- but it is what
        // the per-item supply/demand history readout needs.
        private static Dictionary<string, string> _itemDemand = new Dictionary<string, string>();
        private static Dictionary<string, string> _itemSupply = new Dictionary<string, string>();

        // In-progress day totals (gold), banked and cleared on the daily snapshot.
        private static Dictionary<string, int> _dayVillager = new Dictionary<string, int>();
        private static Dictionary<string, int> _dayParty = new Dictionary<string, int>();
        private static Dictionary<string, int> _dayCaravan = new Dictionary<string, int>();

        // The goods behind the villager "Delivered" gold, so the history column can hover to what was
        // actually sold into the town that day. In-progress units and gold per good, bucketed by town
        // (settId -> itemId -> value), banked on the daily snapshot into one compact string column per
        // day per town: "itemId=units=gold;itemId=units=gold" (item ids carry no ',', ';' or '=', so the
        // encoding is safe; days with no delivery store "-"). Item names are resolved at read time.
        //
        // The per-town buckets let each town's snapshot drain only its own entries (the old flat
        // "settId#itemId" maps were scanned whole, with a prefix test, once per town: towns x entries at
        // midnight). The SAVE format is still the flat "settId#key" Dictionary<string,int> under the same
        // keys -- SyncData flattens on save and re-buckets on load -- so old and new saves read either way.
        private static Dictionary<string, Dictionary<string, int>> _dayVillagerGoodsUnits = new Dictionary<string, Dictionary<string, int>>();
        private static Dictionary<string, Dictionary<string, int>> _dayVillagerGoodsGold = new Dictionary<string, Dictionary<string, int>>();
        private static Dictionary<string, string> _villagerGoods = new Dictionary<string, string>();

        // Per-day income/expense breakdown for the two wealth pools. Both pools funnel every denar through
        // SettlementWealth.Apply/ApplyCitizens (see SettlementGoldFunnel), which feed these in-progress
        // signed-by-source day maps, bucketed by town (settId -> source -> net; saved flat as "settId#source",
        // see above). Banked on the daily snapshot into one compact "source=net;..." string column per day
        // per town (net > 0 is income, net < 0 is expense; a day with no movement stores "-"). The VM splits
        // each column into income/expense totals and category hints.
        private static Dictionary<string, Dictionary<string, int>> _daySettlementFlow = new Dictionary<string, Dictionary<string, int>>();
        private static Dictionary<string, Dictionary<string, int>> _dayCitizenFlow = new Dictionary<string, Dictionary<string, int>>();
        private static Dictionary<string, string> _settlementFlow = new Dictionary<string, string>();
        private static Dictionary<string, string> _citizenFlow = new Dictionary<string, string>();

        // Snapshot scratch (main thread, daily tick only): reused across towns so the column builders add
        // no per-town StringBuilder/List garbage.
        private static readonly System.Text.StringBuilder _columnBuilder = new System.Text.StringBuilder();
        private static readonly List<KeyValuePair<string, int>> _goodsScratch = new List<KeyValuePair<string, int>>();

        // Per-town "settId#itemId" keys of the per-item demand/supply series, aligned with
        // CitizenDemand.ModelledGoods, so the snapshot doesn't concatenate towns x goods key strings every
        // day. Session cache only (the keys are pure functions of the ids); not saved.
        private static readonly Dictionary<string, string[]> _itemKeysByTown = new Dictionary<string, string[]>();

        private static int _lastDay = -1;
        public static int LastDay => _lastDay;

        // Event tokens.
        public const string EvSiege = "siege";
        public const string EvCaptured = "captured";

        // --- Flow accumulation (called from hooks) --------------------------

        public static void AddVillagerBrought(Settlement settlement, int gold) => Accumulate(_dayVillager, settlement, gold);
        public static void AddPartyBought(Settlement settlement, int gold) => Accumulate(_dayParty, settlement, gold);
        public static void AddCaravanBought(Settlement settlement, int gold) => Accumulate(_dayCaravan, settlement, gold);

        // Records one villager sale into today's per-good breakdown for the town's Delivered column.
        public static void AddVillagerGood(Settlement settlement, ItemObject item, int units, int gold)
        {
            if (settlement == null || item == null || units <= 0 || !settlement.IsTown)
            {
                return;
            }
            string itemId = item.StringId;
            Dictionary<string, int> unitsBucket = TownBucket(_dayVillagerGoodsUnits, settlement.StringId);
            unitsBucket.TryGetValue(itemId, out int u);
            unitsBucket[itemId] = u + units;
            Dictionary<string, int> goldBucket = TownBucket(_dayVillagerGoodsGold, settlement.StringId);
            goldBucket.TryGetValue(itemId, out int g);
            goldBucket[itemId] = g + (gold > 0 ? gold : 0);
        }

        // The in-progress bucket for one town, created on first use. Buckets are cleared (not removed) when
        // a snapshot drains them, so a town's inner map is allocated once per session.
        private static Dictionary<string, int> TownBucket(Dictionary<string, Dictionary<string, int>> day, string id)
        {
            if (!day.TryGetValue(id, out Dictionary<string, int> bucket))
            {
                bucket = new Dictionary<string, int>();
                day[id] = bucket;
            }
            return bucket;
        }

        // Records a signed money movement into today's per-source flow map for a town's TREASURY pool.
        public static void AddSettlementFlow(Settlement settlement, string source, int delta) => AccumulateFlow(_daySettlementFlow, settlement, source, delta);

        // Records a signed money movement into today's per-source flow map for a town's CITIZEN-WEALTH pool.
        public static void AddCitizenFlow(Settlement settlement, string source, int delta) => AccumulateFlow(_dayCitizenFlow, settlement, source, delta);

        private static void AccumulateFlow(Dictionary<string, Dictionary<string, int>> day, Settlement settlement, string source, int delta)
        {
            if (day == null || settlement == null || string.IsNullOrEmpty(source) || delta == 0 || !settlement.IsTown)
            {
                return;
            }
            Dictionary<string, int> bucket = TownBucket(day, settlement.StringId);
            bucket.TryGetValue(source, out int running);
            bucket[source] = running + delta;
        }

        private static void Accumulate(Dictionary<string, int> day, Settlement settlement, int gold)
        {
            if (day == null || settlement == null || gold <= 0 || !settlement.IsTown)
            {
                return;
            }
            string id = settlement.StringId;
            day.TryGetValue(id, out int running);
            day[id] = running + gold;
        }

        // --- Recording ------------------------------------------------------

        // One snapshot column per town, appended on the global DailyTick.
        public static void RecordDailySnapshot()
        {
            if (Campaign.Current == null)
            {
                return;
            }
            int day = (int)CampaignTime.Now.ToDays;
            _lastDay = day;

            // Resolve the modelled goods' items once per snapshot rather than once per town.
            string[] goods = CitizenDemand.ModelledGoods;
            ItemObject[] goodItems = new ItemObject[goods.Length];
            for (int i = 0; i < goods.Length; i++)
            {
                goodItems[i] = MBObjectManager.Instance.GetObject<ItemObject>(goods[i]);
            }

            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null || !settlement.IsTown || settlement.Town == null)
                {
                    continue;
                }
                Town town = settlement.Town;
                string id = settlement.StringId;
                AppendInt(_prosperity, id, (int)MathF.Round(town.Prosperity));
                AppendInt(_citizen, id, SettlementWealth.GetCitizenWealth(settlement));
                AppendInt(_settlement, id, SettlementWealth.GetSettlementWealth(settlement));
                AppendInt(_food, id, RBMTownFoodSupply.FoodUnitsInMarket(town));
                AppendInt(_garrison, id, town.GarrisonParty != null ? town.GarrisonParty.MemberRoster.TotalManCount : 0);
                AppendInt(_militia, id, (int)MathF.Round(settlement.Militia));
                AppendInt(_villager, id, TakeDay(_dayVillager, id));
                AppendInt(_party, id, TakeDay(_dayParty, id));
                AppendInt(_caravan, id, TakeDay(_dayCaravan, id));

                RBMTownFoodSupply.FoodConsumptionBreakdown eaten = RBMTownFoodSupply.GetFoodConsumption(town);
                AppendInt(_foodCitizens, id, eaten.Citizens);
                AppendInt(_foodGarrison, id, eaten.Garrison);
                AppendInt(_foodMilitia, id, eaten.Militia);

                AppendStr(_villagerGoods, id, TakeVillagerGoodsColumn(id));
                AppendStr(_settlementFlow, id, TakeFlowColumn(_daySettlementFlow, id));
                AppendStr(_citizenFlow, id, TakeFlowColumn(_dayCitizenFlow, id));

                // Per-item demand vs stock, one column per modelled good. Runs once a day (not per frame).
                ItemRoster marketRoster = town.Owner != null ? town.Owner.ItemRoster : null;
                string[] itemKeys = ItemKeysForTown(id, goods);
                for (int i = 0; i < goods.Length; i++)
                {
                    int dUnits = (int)MathF.Round(CitizenDemand.DailyUnits(town, goods[i]));
                    ItemObject item = goodItems[i];
                    int stock = (item != null && marketRoster != null) ? marketRoster.GetItemNumber(item) : 0;
                    string key = itemKeys[i];
                    AppendInt(_itemDemand, key, dUnits);
                    AppendInt(_itemSupply, key, stock);
                }
            }

            PruneEvents(day - (HistoryDays - 1));
        }

        // The "settId#itemId" series keys for one town, aligned with goods; built once per town per session.
        private static string[] ItemKeysForTown(string id, string[] goods)
        {
            if (!_itemKeysByTown.TryGetValue(id, out string[] keys) || keys.Length != goods.Length)
            {
                keys = new string[goods.Length];
                for (int i = 0; i < goods.Length; i++)
                {
                    keys[i] = id + "#" + goods[i];
                }
                _itemKeysByTown[id] = keys;
            }
            return keys;
        }

        // Reads and zeroes the day accumulator for one town, so the next day starts fresh.
        private static int TakeDay(Dictionary<string, int> day, string id)
        {
            if (day != null && day.TryGetValue(id, out int v))
            {
                day[id] = 0;
                return v;
            }
            return 0;
        }

        // Builds today's villager-goods breakdown column for one town and clears its in-progress entries.
        // "itemId=units=gold;..." ordered by gold contribution, or "-" for a day with no delivery (kept
        // non-empty so the CSV columns stay aligned with the numeric series).
        private static string TakeVillagerGoodsColumn(string id)
        {
            // Only this town's own bucket is touched: O(its goods), not O(every town's entries).
            if (!_dayVillagerGoodsUnits.TryGetValue(id, out Dictionary<string, int> units) || units.Count == 0)
            {
                return "-";
            }
            _dayVillagerGoodsGold.TryGetValue(id, out Dictionary<string, int> golds);

            List<KeyValuePair<string, int>> goods = _goodsScratch; // itemId -> gold, for ordering
            goods.Clear();
            foreach (var kv in units)
            {
                int gold = 0;
                if (golds != null)
                {
                    golds.TryGetValue(kv.Key, out gold);
                }
                goods.Add(new KeyValuePair<string, int>(kv.Key, gold));
            }
            goods.Sort((a, b) => b.Value.CompareTo(a.Value));
            System.Text.StringBuilder sb = _columnBuilder;
            sb.Clear();
            foreach (var g in goods)
            {
                if (sb.Length > 0)
                {
                    sb.Append(';');
                }
                sb.Append(g.Key).Append('=').Append(units[g.Key]).Append('=').Append(g.Value);
            }
            goods.Clear();
            // Drain this town's harvested entries so the next day starts clean (the bucket is kept for reuse).
            units.Clear();
            if (golds != null)
            {
                golds.Clear();
            }
            return sb.ToString();
        }

        // Builds today's income/expense breakdown column for one pool of one town and clears its
        // in-progress entries. "source=net;..." (net signed, zero-net sources dropped) or "-" for a day with
        // no movement. Source tokens carry no ';' '=' or ',', so the encoding is unambiguous.
        private static string TakeFlowColumn(Dictionary<string, Dictionary<string, int>> day, string id)
        {
            // Only this town's own bucket is touched: O(its sources), not O(every town's entries).
            if (!day.TryGetValue(id, out Dictionary<string, int> flows) || flows.Count == 0)
            {
                return "-";
            }
            System.Text.StringBuilder sb = _columnBuilder;
            sb.Clear();
            foreach (var kv in flows)
            {
                if (kv.Value == 0)
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(';');
                }
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            flows.Clear();
            return sb.Length > 0 ? sb.ToString() : "-";
        }

        // Save-format bridge for the bucketed day maps: the save holds them flat as "settId#key" -> value
        // (the format every earlier RBM save already uses, under the same SyncData keys).
        private static Dictionary<string, int> FlattenByTown(Dictionary<string, Dictionary<string, int>> buckets)
        {
            var flat = new Dictionary<string, int>();
            if (buckets == null)
            {
                return flat;
            }
            foreach (var town in buckets)
            {
                if (town.Value == null)
                {
                    continue;
                }
                foreach (var kv in town.Value)
                {
                    flat[town.Key + "#" + kv.Key] = kv.Value;
                }
            }
            return flat;
        }

        // Inverse of FlattenByTown: splits each "settId#key" at its first '#' (settlement ids carry no '#';
        // the key part may, and keeps it -- same split the old prefix match made).
        private static Dictionary<string, Dictionary<string, int>> BucketByTown(Dictionary<string, int> flat)
        {
            var buckets = new Dictionary<string, Dictionary<string, int>>();
            if (flat == null)
            {
                return buckets;
            }
            foreach (var kv in flat)
            {
                if (kv.Key == null)
                {
                    continue;
                }
                int hash = kv.Key.IndexOf('#');
                if (hash <= 0)
                {
                    continue;
                }
                TownBucket(buckets, kv.Key.Substring(0, hash))[kv.Key.Substring(hash + 1)] = kv.Value;
            }
            return buckets;
        }

        // String-valued counterpart of AppendInt: appends one CSV column, sharing the amortized trim. Also
        // used by RBMClanFinanceLedger, which keeps the same 30-day window.
        internal static void AppendStr(Dictionary<string, string> dict, string id, string value)
        {
            if (dict.TryGetValue(id, out string csv) && !string.IsNullOrEmpty(csv))
            {
                if (CountColumns(csv) >= TrimWatermark)
                {
                    string[] parts = csv.Split(',');
                    int start = parts.Length - (HistoryDays - 1);
                    var kept = new List<string>(HistoryDays);
                    for (int i = start; i < parts.Length; i++)
                    {
                        kept.Add(parts[i]);
                    }
                    kept.Add(value);
                    dict[id] = string.Join(",", kept);
                }
                else
                {
                    dict[id] = csv + "," + value;
                }
            }
            else
            {
                dict[id] = value;
            }
        }

        // Log a discrete event against the day it happened.
        public static void AddEvent(Settlement settlement, string token)
        {
            if (settlement == null || Campaign.Current == null)
            {
                return;
            }
            string id = settlement.StringId;
            int day = (int)CampaignTime.Now.ToDays;
            string entry = day + ":" + token;
            if (_events.TryGetValue(id, out string existing) && !string.IsNullOrEmpty(existing))
            {
                _events[id] = existing + "|" + entry;
            }
            else
            {
                _events[id] = entry;
            }
        }

        // Let a series grow to this many columns before trimming it back to HistoryDays. The trim -- Split
        // into an array, copy the tail into a List, Join back -- is the one costly step here, and the old
        // code ran it on EVERY append once a series was full: every day, for every metric of every town,
        // the per-item demand/supply series (town x good keys) included. Amortizing to a fixed headroom
        // runs it once per (TrimWatermark - HistoryDays) days instead; the other days are a single concat.
        private const int TrimWatermark = HistoryDays * 2;

        internal static void AppendInt(Dictionary<string, string> dict, string id, int value)
        {
            if (dict.TryGetValue(id, out string csv) && !string.IsNullOrEmpty(csv))
            {
                if (CountColumns(csv) >= TrimWatermark)
                {
                    // Rebuild once we hit the watermark: keep the newest (HistoryDays - 1) plus today's.
                    string[] parts = csv.Split(',');
                    int start = parts.Length - (HistoryDays - 1);
                    var kept = new List<string>(HistoryDays);
                    for (int i = start; i < parts.Length; i++)
                    {
                        kept.Add(parts[i]);
                    }
                    kept.Add(value.ToString());
                    dict[id] = string.Join(",", kept);
                }
                else
                {
                    dict[id] = csv + "," + value;
                }
            }
            else
            {
                dict[id] = value.ToString();
            }
        }

        // Column count without allocating: one comma-scan of the CSV (the values never contain commas).
        private static int CountColumns(string csv)
        {
            int columns = 1;
            for (int i = 0; i < csv.Length; i++)
            {
                if (csv[i] == ',')
                {
                    columns++;
                }
            }
            return columns;
        }

        private static void PruneEvents(int oldestDayToKeep)
        {
            var keys = new List<string>(_events.Keys);
            foreach (string id in keys)
            {
                string csv = _events[id];
                if (string.IsNullOrEmpty(csv))
                {
                    continue;
                }
                string[] entries = csv.Split('|');
                var kept = new List<string>(entries.Length);
                foreach (string e in entries)
                {
                    int colon = e.IndexOf(':');
                    if (colon <= 0)
                    {
                        continue;
                    }
                    if (int.TryParse(e.Substring(0, colon), out int d) && d >= oldestDayToKeep)
                    {
                        kept.Add(e);
                    }
                }
                if (kept.Count == 0)
                {
                    _events.Remove(id);
                }
                else
                {
                    _events[id] = string.Join("|", kept);
                }
            }
        }

        // --- Reading (for the VM) -------------------------------------------

        // Metric series for a town, oldest->newest (may be shorter than HistoryDays early on).
        public static int[] GetSeries(string metric, string settlementId)
        {
            Dictionary<string, string> dict = MetricDict(metric);
            if (dict == null || !dict.TryGetValue(settlementId, out string csv) || string.IsNullOrEmpty(csv))
            {
                return new int[0];
            }
            string[] parts = csv.Split(',');
            // The stored CSV may temporarily hold more than HistoryDays columns (see AppendInt's amortized
            // trim); expose only the newest HistoryDays so the window the VM charts is unchanged.
            int count = parts.Length < HistoryDays ? parts.Length : HistoryDays;
            int startIdx = parts.Length - count;
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                int.TryParse(parts[startIdx + i], out result[i]);
            }
            return result;
        }

        // Per-day villager-goods breakdown columns for a town, oldest->newest, each "itemId=units=gold;..."
        // or "-" for a day with no delivery (may be shorter than HistoryDays early on).
        public static string[] GetVillagerGoodsSeries(string settlementId)
        {
            return GetStringSeries(_villagerGoods, settlementId);
        }

        // Per-day income/expense breakdown columns for a town's TREASURY / CITIZEN-WEALTH pool, oldest->
        // newest, each "source=net;..." or "-" for a still day.
        public static string[] GetSettlementFlowSeries(string settlementId) => GetStringSeries(_settlementFlow, settlementId);
        public static string[] GetCitizenFlowSeries(string settlementId) => GetStringSeries(_citizenFlow, settlementId);

        internal static string[] GetStringSeries(Dictionary<string, string> dict, string settlementId)
        {
            if (!dict.TryGetValue(settlementId, out string csv) || string.IsNullOrEmpty(csv))
            {
                return new string[0];
            }
            string[] parts = csv.Split(',');
            int count = parts.Length < HistoryDays ? parts.Length : HistoryDays;
            int startIdx = parts.Length - count;
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = parts[startIdx + i];
            }
            return result;
        }

        // Event tokens that occurred on a given absolute campaign-day for a town.
        public static List<string> GetEventsForDay(string settlementId, int day)
        {
            var result = new List<string>();
            if (!_events.TryGetValue(settlementId, out string csv) || string.IsNullOrEmpty(csv))
            {
                return result;
            }
            foreach (string e in csv.Split('|'))
            {
                int colon = e.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }
                if (int.TryParse(e.Substring(0, colon), out int d) && d == day)
                {
                    result.Add(e.Substring(colon + 1));
                }
            }
            return result;
        }

        private static Dictionary<string, string> MetricDict(string metric)
        {
            switch (metric)
            {
                case "prosperity": return _prosperity;
                case "citizen": return _citizen;
                case "settlement": return _settlement;
                case "food": return _food;
                case "garrison": return _garrison;
                case "militia": return _militia;
                case "villager": return _villager;
                case "party": return _party;
                case "caravan": return _caravan;
                case "foodCitizens": return _foodCitizens;
                case "foodGarrison": return _foodGarrison;
                case "foodMilitia": return _foodMilitia;
                case "itemDemand": return _itemDemand;
                case "itemSupply": return _itemSupply;
                default: return null;
            }
        }

        // --- Persistence ----------------------------------------------------

        public static void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsLoading)
            {
                _prosperity = null;
                _citizen = null;
                _settlement = null;
                _food = null;
                _garrison = null;
                _militia = null;
                _villager = null;
                _party = null;
                _caravan = null;
                _foodCitizens = null;
                _foodGarrison = null;
                _foodMilitia = null;
                _events = null;
                _itemDemand = null;
                _itemSupply = null;
                _dayVillager = null;
                _dayParty = null;
                _dayCaravan = null;
                _villagerGoods = null;
                _settlementFlow = null;
                _citizenFlow = null;
                _lastDay = -1;
            }

            // The four bucketed day maps go to/from the save in their original flat "settId#key" form, so
            // the save keys and container type are unchanged (old saves load, new saves load in old builds).
            Dictionary<string, int> flatGoodsUnits = dataStore.IsLoading ? null : FlattenByTown(_dayVillagerGoodsUnits);
            Dictionary<string, int> flatGoodsGold = dataStore.IsLoading ? null : FlattenByTown(_dayVillagerGoodsGold);
            Dictionary<string, int> flatSettlementFlow = dataStore.IsLoading ? null : FlattenByTown(_daySettlementFlow);
            Dictionary<string, int> flatCitizenFlow = dataStore.IsLoading ? null : FlattenByTown(_dayCitizenFlow);

            dataStore.SyncData("RBM_townProsperityHist", ref _prosperity);
            dataStore.SyncData("RBM_townCitizenHist", ref _citizen);
            dataStore.SyncData("RBM_townSettlementHist", ref _settlement);
            dataStore.SyncData("RBM_townFoodHist", ref _food);
            dataStore.SyncData("RBM_townGarrisonHist", ref _garrison);
            dataStore.SyncData("RBM_townMilitiaHist", ref _militia);
            dataStore.SyncData("RBM_townVillagerHist", ref _villager);
            dataStore.SyncData("RBM_townPartyHist", ref _party);
            dataStore.SyncData("RBM_townCaravanHist", ref _caravan);
            dataStore.SyncData("RBM_townFoodCitizensHist", ref _foodCitizens);
            dataStore.SyncData("RBM_townFoodGarrisonHist", ref _foodGarrison);
            dataStore.SyncData("RBM_townFoodMilitiaHist", ref _foodMilitia);
            dataStore.SyncData("RBM_townEventHist", ref _events);
            dataStore.SyncData("RBM_townItemDemandHist", ref _itemDemand);
            dataStore.SyncData("RBM_townItemSupplyHist", ref _itemSupply);
            dataStore.SyncData("RBM_townDayVillager", ref _dayVillager);
            dataStore.SyncData("RBM_townDayParty", ref _dayParty);
            dataStore.SyncData("RBM_townDayCaravan", ref _dayCaravan);
            dataStore.SyncData("RBM_townDayVillagerGoodsUnits", ref flatGoodsUnits);
            dataStore.SyncData("RBM_townDayVillagerGoodsGold", ref flatGoodsGold);
            dataStore.SyncData("RBM_townVillagerGoodsHist", ref _villagerGoods);
            dataStore.SyncData("RBM_townDaySettlementFlow", ref flatSettlementFlow);
            dataStore.SyncData("RBM_townDayCitizenFlow", ref flatCitizenFlow);
            dataStore.SyncData("RBM_townSettlementFlowHist", ref _settlementFlow);
            dataStore.SyncData("RBM_townCitizenFlowHist", ref _citizenFlow);
            dataStore.SyncData("RBM_townLedgerLastDay", ref _lastDay);

            if (dataStore.IsLoading)
            {
                _dayVillagerGoodsUnits = BucketByTown(flatGoodsUnits);
                _dayVillagerGoodsGold = BucketByTown(flatGoodsGold);
                _daySettlementFlow = BucketByTown(flatSettlementFlow);
                _dayCitizenFlow = BucketByTown(flatCitizenFlow);
            }

            if (_prosperity == null) _prosperity = new Dictionary<string, string>();
            if (_citizen == null) _citizen = new Dictionary<string, string>();
            if (_settlement == null) _settlement = new Dictionary<string, string>();
            if (_food == null) _food = new Dictionary<string, string>();
            if (_garrison == null) _garrison = new Dictionary<string, string>();
            if (_militia == null) _militia = new Dictionary<string, string>();
            if (_villager == null) _villager = new Dictionary<string, string>();
            if (_party == null) _party = new Dictionary<string, string>();
            if (_caravan == null) _caravan = new Dictionary<string, string>();
            if (_foodCitizens == null) _foodCitizens = new Dictionary<string, string>();
            if (_foodGarrison == null) _foodGarrison = new Dictionary<string, string>();
            if (_foodMilitia == null) _foodMilitia = new Dictionary<string, string>();
            if (_events == null) _events = new Dictionary<string, string>();
            if (_itemDemand == null) _itemDemand = new Dictionary<string, string>();
            if (_itemSupply == null) _itemSupply = new Dictionary<string, string>();
            if (_dayVillager == null) _dayVillager = new Dictionary<string, int>();
            if (_dayParty == null) _dayParty = new Dictionary<string, int>();
            if (_dayCaravan == null) _dayCaravan = new Dictionary<string, int>();
            if (_villagerGoods == null) _villagerGoods = new Dictionary<string, string>();
            if (_settlementFlow == null) _settlementFlow = new Dictionary<string, string>();
            if (_citizenFlow == null) _citizenFlow = new Dictionary<string, string>();
        }

        // Wipe everything (new game).
        public static void Reset()
        {
            _prosperity = new Dictionary<string, string>();
            _citizen = new Dictionary<string, string>();
            _settlement = new Dictionary<string, string>();
            _food = new Dictionary<string, string>();
            _garrison = new Dictionary<string, string>();
            _militia = new Dictionary<string, string>();
            _villager = new Dictionary<string, string>();
            _party = new Dictionary<string, string>();
            _caravan = new Dictionary<string, string>();
            _foodCitizens = new Dictionary<string, string>();
            _foodGarrison = new Dictionary<string, string>();
            _foodMilitia = new Dictionary<string, string>();
            _events = new Dictionary<string, string>();
            _itemDemand = new Dictionary<string, string>();
            _itemSupply = new Dictionary<string, string>();
            _dayVillager = new Dictionary<string, int>();
            _dayParty = new Dictionary<string, int>();
            _dayCaravan = new Dictionary<string, int>();
            _dayVillagerGoodsUnits = new Dictionary<string, Dictionary<string, int>>();
            _dayVillagerGoodsGold = new Dictionary<string, Dictionary<string, int>>();
            _villagerGoods = new Dictionary<string, string>();
            _daySettlementFlow = new Dictionary<string, Dictionary<string, int>>();
            _dayCitizenFlow = new Dictionary<string, Dictionary<string, int>>();
            _settlementFlow = new Dictionary<string, string>();
            _citizenFlow = new Dictionary<string, string>();
            _itemKeysByTown.Clear();
            _lastDay = -1;
        }

        // --- Market-buy hook -------------------------------------------------

        // Always-on capture of goods bought FROM a town market by mobile parties, split caravan vs other
        // party, by weighing the settlement purse either side of the sale (same technique as
        // PartyTradeFlow, but ungated by the economy log so the ledger always sees it). Only inflows to
        // the settlement (delta > 0 -- the counterparty paid, i.e. bought) are banked.
        [HarmonyPatch(typeof(SellItemsAction), "ApplyInternal")]
        private static class TownBuyFlowPatch
        {
            // Allocation-free carry between prefix and postfix. This runs on EVERY market trade in the
            // game, so it must add no per-trade garbage: thread-static fields replace a per-call object[].
            // Trades never nest within a single ApplyInternal, so the two fields are safe to reuse; the
            // worst a hypothetical nested call could do is drop one tally (postfix sees a null settlement),
            // never crash.
            [ThreadStatic] private static Settlement _preSettlement;
            [ThreadStatic] private static int _preGold;

            private static void Prefix(PartyBase sellerParty, PartyBase buyerParty)
            {
                _preSettlement = null;
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }
                Settlement settlement = SettlementSide(sellerParty, buyerParty);
                if (settlement == null || !settlement.IsTown || settlement.SettlementComponent == null)
                {
                    return;
                }
                _preSettlement = settlement;
                _preGold = settlement.SettlementComponent.Gold;
            }

            private static void Postfix(PartyBase sellerParty, PartyBase buyerParty)
            {
                Settlement settlement = _preSettlement;
                _preSettlement = null;
                if (settlement == null || settlement.SettlementComponent == null)
                {
                    return;
                }
                int delta = settlement.SettlementComponent.Gold - _preGold;
                if (delta <= 0)
                {
                    return; // settlement lost or unchanged gold => a party SOLD to town, not bought
                }

                // Whoever was NOT the settlement is the buyer that just paid.
                PartyBase counterparty = (sellerParty != null && sellerParty.IsSettlement) ? buyerParty : sellerParty;
                MobileParty mobileParty = counterparty != null ? counterparty.MobileParty : null;
                if (mobileParty == null)
                {
                    return;
                }
                if (mobileParty.IsCaravan)
                {
                    AddCaravanBought(settlement, delta);
                }
                else if (!mobileParty.IsVillager)
                {
                    // Lord/player/garrison/other buying in the market. Villagers reach the market outside
                    // SellItemsAction (see VillagerDelivery), so they never land here.
                    AddPartyBought(settlement, delta);
                }
            }
        }

        private static Settlement SettlementSide(PartyBase sellerParty, PartyBase buyerParty)
        {
            if (sellerParty != null && sellerParty.IsSettlement)
            {
                return sellerParty.Settlement;
            }
            if (buyerParty != null && buyerParty.IsSettlement)
            {
                return buyerParty.Settlement;
            }
            return null;
        }
    }
}
