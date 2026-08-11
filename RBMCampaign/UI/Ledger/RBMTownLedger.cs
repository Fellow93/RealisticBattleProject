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

        private static int _lastDay = -1;
        public static int LastDay => _lastDay;

        // Event tokens.
        public const string EvSiege = "siege";
        public const string EvCaptured = "captured";

        // --- Flow accumulation (called from hooks) --------------------------

        public static void AddVillagerBrought(Settlement settlement, int gold) => Accumulate(_dayVillager, settlement, gold);
        public static void AddPartyBought(Settlement settlement, int gold) => Accumulate(_dayParty, settlement, gold);
        public static void AddCaravanBought(Settlement settlement, int gold) => Accumulate(_dayCaravan, settlement, gold);

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

                // Per-item demand vs stock, one column per modelled good. Runs once a day (not per frame).
                ItemRoster marketRoster = town.Owner != null ? town.Owner.ItemRoster : null;
                foreach (string gid in CitizenDemand.ModelledGoods)
                {
                    int dUnits = (int)MathF.Round(CitizenDemand.DailyUnits(town, gid));
                    ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(gid);
                    int stock = (item != null && marketRoster != null) ? marketRoster.GetItemNumber(item) : 0;
                    string key = id + "#" + gid;
                    AppendInt(_itemDemand, key, dUnits);
                    AppendInt(_itemSupply, key, stock);
                }
            }

            PruneEvents(day - (HistoryDays - 1));
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

        private static void AppendInt(Dictionary<string, string> dict, string id, int value)
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
                _events = null;
                _itemDemand = null;
                _itemSupply = null;
                _dayVillager = null;
                _dayParty = null;
                _dayCaravan = null;
                _lastDay = -1;
            }

            dataStore.SyncData("RBM_townProsperityHist", ref _prosperity);
            dataStore.SyncData("RBM_townCitizenHist", ref _citizen);
            dataStore.SyncData("RBM_townSettlementHist", ref _settlement);
            dataStore.SyncData("RBM_townFoodHist", ref _food);
            dataStore.SyncData("RBM_townGarrisonHist", ref _garrison);
            dataStore.SyncData("RBM_townMilitiaHist", ref _militia);
            dataStore.SyncData("RBM_townVillagerHist", ref _villager);
            dataStore.SyncData("RBM_townPartyHist", ref _party);
            dataStore.SyncData("RBM_townCaravanHist", ref _caravan);
            dataStore.SyncData("RBM_townEventHist", ref _events);
            dataStore.SyncData("RBM_townItemDemandHist", ref _itemDemand);
            dataStore.SyncData("RBM_townItemSupplyHist", ref _itemSupply);
            dataStore.SyncData("RBM_townDayVillager", ref _dayVillager);
            dataStore.SyncData("RBM_townDayParty", ref _dayParty);
            dataStore.SyncData("RBM_townDayCaravan", ref _dayCaravan);
            dataStore.SyncData("RBM_townLedgerLastDay", ref _lastDay);

            if (_prosperity == null) _prosperity = new Dictionary<string, string>();
            if (_citizen == null) _citizen = new Dictionary<string, string>();
            if (_settlement == null) _settlement = new Dictionary<string, string>();
            if (_food == null) _food = new Dictionary<string, string>();
            if (_garrison == null) _garrison = new Dictionary<string, string>();
            if (_militia == null) _militia = new Dictionary<string, string>();
            if (_villager == null) _villager = new Dictionary<string, string>();
            if (_party == null) _party = new Dictionary<string, string>();
            if (_caravan == null) _caravan = new Dictionary<string, string>();
            if (_events == null) _events = new Dictionary<string, string>();
            if (_itemDemand == null) _itemDemand = new Dictionary<string, string>();
            if (_itemSupply == null) _itemSupply = new Dictionary<string, string>();
            if (_dayVillager == null) _dayVillager = new Dictionary<string, int>();
            if (_dayParty == null) _dayParty = new Dictionary<string, int>();
            if (_dayCaravan == null) _dayCaravan = new Dictionary<string, int>();
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
            _events = new Dictionary<string, string>();
            _itemDemand = new Dictionary<string, string>();
            _itemSupply = new Dictionary<string, string>();
            _dayVillager = new Dictionary<string, int>();
            _dayParty = new Dictionary<string, int>();
            _dayCaravan = new Dictionary<string, int>();
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
