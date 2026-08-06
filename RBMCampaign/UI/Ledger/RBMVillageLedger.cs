using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace RBMCampaign
{
    // Persistent per-village history store for the Ledger's Villages tab. Keeps a rolling
    // 14-day series of four metrics (production/wealth/hearth/militia) plus discrete day-stamped
    // events (raid, villager dispatch, ...), keyed by Settlement.StringId.
    //
    // Storage follows RBM's established CSV-in-Dictionary<string,string> save pattern (see
    // RBMCaravanRegister) -- no int[]-valued dictionaries, which would need a SaveableTypeDefiner
    // RBM doesn't have. Metric series are CSV of ints, oldest->newest. Events are absolute-day
    // stamped ("<day>:<token>" joined by '|') so they never have to stay index-aligned with the
    // metric arrays -- the VM maps them onto day columns by absolute campaign-day at display time.
    public static class RBMVillageLedger
    {
        public const int HistoryDays = 14;

        private static Dictionary<string, string> _prod = new Dictionary<string, string>();
        private static Dictionary<string, string> _wealth = new Dictionary<string, string>();
        private static Dictionary<string, string> _hearth = new Dictionary<string, string>();
        private static Dictionary<string, string> _militia = new Dictionary<string, string>();
        private static Dictionary<string, string> _events = new Dictionary<string, string>();

        // Absolute campaign-day index of the newest snapshot column (shared by all villages,
        // since snapshots run on the global DailyTick).
        private static int _lastDay = -1;

        public static int LastDay => _lastDay;

        // Event tokens.
        public const string EvRaidStart = "raid";
        public const string EvLooted = "looted";
        public const string EvDispatch = "dispatch";
        public const string EvArrive = "arrive";

        // --- Recording -------------------------------------------------------

        // One snapshot column per village, appended on the global DailyTick.
        public static void RecordDailySnapshot()
        {
            if (Campaign.Current == null)
            {
                return;
            }
            int day = (int)CampaignTime.Now.ToDays;
            _lastDay = day;

            // One pass over parties to tally militia currently OUT as convoy escorts, keyed by home
            // village -- so the militia column reports total strength (home + deployed), not just who's
            // standing at home while escorts are on the road (RBM borrows village militia for convoys).
            var deployed = new Dictionary<string, int>();
            foreach (MobileParty party in MobileParty.AllVillagerParties)
            {
                if (party == null || party.HomeSettlement == null || !party.HomeSettlement.IsVillage)
                {
                    continue;
                }
                int escort = VillagerEscort.CountEscortMilitia(party);
                if (escort <= 0)
                {
                    continue;
                }
                string homeId = party.HomeSettlement.StringId;
                deployed.TryGetValue(homeId, out int running);
                deployed[homeId] = running + escort;
            }

            foreach (Village village in Village.All)
            {
                Settlement settlement = village.Settlement;
                if (settlement == null)
                {
                    continue;
                }
                string id = settlement.StringId;
                deployed.TryGetValue(id, out int escortOut);
                AppendInt(_prod, id, ComputeProduction(village));
                AppendInt(_wealth, id, SettlementWealth.GetSettlementWealth(settlement));
                AppendInt(_hearth, id, (int)MathF.Round(village.Hearth));
                AppendInt(_militia, id, (int)MathF.Round(settlement.Militia) + escortOut);
            }

            PruneEvents(day - (HistoryDays - 1));
        }

        // Expected daily units of production, matching the number RBM's economy uses
        // (per-Hearth total rate * Hearth). Zero while the village isn't producing.
        private static int ComputeProduction(Village village)
        {
            if (village.VillageState != Village.VillageStates.Normal)
            {
                return 0;
            }
            return (int)MathF.Round(RBMVillageProduction.GetTotalRate(village) * village.Hearth);
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

        private static void AppendInt(Dictionary<string, string> dict, string id, int value)
        {
            if (dict.TryGetValue(id, out string csv) && !string.IsNullOrEmpty(csv))
            {
                string[] parts = csv.Split(',');
                if (parts.Length >= HistoryDays)
                {
                    // Drop the oldest, keep the last (HistoryDays-1), append today's.
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

        // Metric series for a village, oldest->newest (may be shorter than HistoryDays early on).
        public static int[] GetSeries(string metric, string settlementId)
        {
            Dictionary<string, string> dict = MetricDict(metric);
            if (dict == null || !dict.TryGetValue(settlementId, out string csv) || string.IsNullOrEmpty(csv))
            {
                return new int[0];
            }
            string[] parts = csv.Split(',');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                int.TryParse(parts[i], out result[i]);
            }
            return result;
        }

        // Event tokens that occurred on a given absolute campaign-day for a village.
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
                case "prod": return _prod;
                case "wealth": return _wealth;
                case "hearth": return _hearth;
                case "militia": return _militia;
                default: return null;
            }
        }

        // --- Persistence -----------------------------------------------------

        public static void SyncData(IDataStore dataStore)
        {
            // Clear process-static state before a load so a stale in-memory ledger from a
            // previously-open save can't leak into this one (missing keys -> empty, not carried).
            if (dataStore.IsLoading)
            {
                _prod = null;
                _wealth = null;
                _hearth = null;
                _militia = null;
                _events = null;
                _lastDay = -1;
            }

            dataStore.SyncData("RBM_villageProdHist", ref _prod);
            dataStore.SyncData("RBM_villageWealthHist", ref _wealth);
            dataStore.SyncData("RBM_villageHearthHist", ref _hearth);
            dataStore.SyncData("RBM_villageMilitiaHist", ref _militia);
            dataStore.SyncData("RBM_villageEventHist", ref _events);
            dataStore.SyncData("RBM_villageLedgerLastDay", ref _lastDay);

            if (_prod == null) _prod = new Dictionary<string, string>();
            if (_wealth == null) _wealth = new Dictionary<string, string>();
            if (_hearth == null) _hearth = new Dictionary<string, string>();
            if (_militia == null) _militia = new Dictionary<string, string>();
            if (_events == null) _events = new Dictionary<string, string>();
        }

        // Wipe everything (new game).
        public static void Reset()
        {
            _prod = new Dictionary<string, string>();
            _wealth = new Dictionary<string, string>();
            _hearth = new Dictionary<string, string>();
            _militia = new Dictionary<string, string>();
            _events = new Dictionary<string, string>();
            _lastDay = -1;
        }
    }
}
