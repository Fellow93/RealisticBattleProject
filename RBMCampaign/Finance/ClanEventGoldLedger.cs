using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace RBMCampaign
{
    /// <summary>The kinds of gold the player's clan gains or loses outside the daily finance model.</summary>
    public enum EventGoldKind
    {
        /// <summary>The commander's cut of a gather -- battle loot, raid plunder or a sack (<see cref="SpoilsPool.ApplyLeaderCut"/>).</summary>
        LeaderCut,
        /// <summary>The spoils share companions in a clan party claim straight into the clan's gold.</summary>
        CompanionSpoils,
        /// <summary>The owner's and ruler's cuts of a settlement's mint output (<see cref="Minting"/>).</summary>
        Minting,
        /// <summary>Gold a clan party's leader was billed for troop promotions its spoils could not cover (a drain).</summary>
        UpgradeGold,
        /// <summary>Blood money paid to a ransom broker to end a feud (a drain; see <see cref="BloodMoney"/>).</summary>
        BloodMoney,
        /// <summary>Scrap value of a disbanded clan party's ships paid to the player (see <see cref="ShipScrapGold"/>).</summary>
        ShipScrap,
        /// <summary>
        /// What a ruling player pays the mercenary companies in his kingdom's service, taken on THEIR clans'
        /// apply passes (a drain; see <see cref="MercenaryContractPay"/>), so it is never on his own.
        /// </summary>
        MercenaryPay
    }

    /// <summary>
    /// A rolling record of the gold RBM pays the player's clan -- or takes from it -- per EVENT rather than
    /// per day. The leader's cut of spoils, the companions' share, a mint's cut and a clan party's gold-paid
    /// promotions all move through <c>GiveGoldAction</c> the moment they happen, which the finance model
    /// never sees; so do two one-off vanilla v1.5 flows RBM only notes, blood money paid to end a feud and
    /// the scrap value of a disbanded clan party's ships. Kept for the last <see cref="HistoryDays"/> days.
    ///
    /// NOT shown on any finance breakdown, and it must not be: those breakdowns are projections of what the
    /// clan's daily apply pass will do, and this gold is never on that pass -- it was paid when the event
    /// fired. Folding its averages into the denar tooltip and the Finances tab's Expected Gold made both
    /// promise a daily change the day never paid. Its one reader is the RBM Ledger's Clan finances tab
    /// (<see cref="RBMClanFinanceLedger"/>), which reports it as what it is: past event gold, day by day.
    /// Player clan only, so the store stays tiny.
    ///
    /// Gold paid to a clan member other than the player -- a companion leading his own party takes that
    /// party's leader's cut, and pays its promotions -- lands in HIS purse, not the clan treasury, and only
    /// reaches the player later through the finance model's party income. So that share is also kept apart
    /// (<see cref="GetDayElsewhere"/>), and the finance ledger counts only what touched the player's gold.
    /// </summary>
    public static class ClanEventGoldLedger
    {
        /// <summary>
        /// How many campaign days are kept (today included), and the window the ledger tab shows. The same
        /// 30 days as the ledger's town and village histories (<see cref="RBMTownLedger.HistoryDays"/>).
        /// </summary>
        public const int HistoryDays = 30;

        // "kind#day" -> gold that kind paid (or took) that campaign day. Only the player's clan is recorded.
        private static Dictionary<string, int> _byDay = new Dictionary<string, int>();
        // The part of each "kind#day" total that was paid to (or taken from) a clan member other than the
        // player, so it never moved the player's own gold. Same keys, same pruning.
        private static Dictionary<string, int> _elsewhereByDay = new Dictionary<string, int>();
        // The first campaign day anything was recorded, so a window younger than HistoryDays is averaged
        // over the days actually tracked rather than diluted by days that never happened.
        private static int _firstDay = -1;
        private static int _lastPruneDay = int.MinValue;

        /// <summary>
        /// Notes <paramref name="gold"/> of <paramref name="kind"/> paid to (or taken from)
        /// <paramref name="hero"/> today. Ignored unless the hero belongs to the player's clan.
        /// </summary>
        public static void Record(Hero hero, EventGoldKind kind, int gold)
        {
            if (hero == null || gold <= 0 || Campaign.Current == null || !RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            if (hero.Clan == null || hero.Clan != Clan.PlayerClan)
            {
                return;
            }
            int today = Today();
            if (_firstDay < 0 || _firstDay > today)
            {
                _firstDay = today;
            }
            string key = Key(kind, today);
            int current;
            _byDay.TryGetValue(key, out current);
            _byDay[key] = current + gold;
            if (hero != Hero.MainHero)
            {
                int elsewhere;
                _elsewhereByDay.TryGetValue(key, out elsewhere);
                _elsewhereByDay[key] = elsewhere + gold;
            }
            PruneIfNeeded(today);
        }

        /// <summary>
        /// The daily average of <paramref name="kind"/> over the last <see cref="HistoryDays"/> days (or over
        /// the days tracked so far, when the record is younger than the window). Zero before anything was
        /// ever recorded.
        /// </summary>
        public static int DailyAverage(EventGoldKind kind)
        {
            if (Campaign.Current == null || _firstDay < 0)
            {
                return 0;
            }
            int today = Today();
            PruneIfNeeded(today);
            long sum = 0L;
            for (int day = today - HistoryDays + 1; day <= today; day++)
            {
                int gold;
                if (_byDay.TryGetValue(Key(kind, day), out gold))
                {
                    sum += gold;
                }
            }
            int days = Math.Min(HistoryDays, today - _firstDay + 1);
            if (days < 1)
            {
                days = 1;
            }
            return (int)Math.Round((double)sum / days);
        }

        /// <summary>The current campaign day, as the record keys its days. Zero outside a campaign.</summary>
        public static int CurrentDay
        {
            get { return Campaign.Current != null ? Today() : 0; }
        }

        /// <summary>
        /// The gold of <paramref name="kind"/> recorded on campaign <paramref name="day"/>, as an unsigned
        /// amount (see <see cref="IsDrain"/> for its direction). Zero for a day with none, or one older than
        /// the window.
        /// </summary>
        public static int GetDay(EventGoldKind kind, int day)
        {
            int gold;
            return _byDay.TryGetValue(Key(kind, day), out gold) ? gold : 0;
        }

        /// <summary>
        /// The part of <see cref="GetDay"/> that was paid to (or taken from) a clan member other than the
        /// player -- gold that never moved the player's own purse. Unsigned, like <see cref="GetDay"/>.
        /// </summary>
        public static int GetDayElsewhere(EventGoldKind kind, int day)
        {
            int gold;
            return _elsewhereByDay.TryGetValue(Key(kind, day), out gold) ? gold : 0;
        }

        /// <summary>True for the kinds that take gold from the clan rather than pay it.</summary>
        public static bool IsDrain(EventGoldKind kind)
        {
            return kind == EventGoldKind.UpgradeGold || kind == EventGoldKind.BloodMoney
                || kind == EventGoldKind.MercenaryPay;
        }

        private static int Today()
        {
            return (int)CampaignTime.Now.ToDays;
        }

        private static string Key(EventGoldKind kind, int day)
        {
            return kind.ToString() + "#" + day;
        }

        // Drops entries that have fallen out of the window. Once per campaign day is plenty.
        private static void PruneIfNeeded(int today)
        {
            if (today == _lastPruneDay)
            {
                return;
            }
            _lastPruneDay = today;
            int oldest = today - HistoryDays + 1;
            List<string> stale = null;
            foreach (string key in _byDay.Keys)
            {
                int hash = key.IndexOf('#');
                int day;
                if (hash < 0 || !int.TryParse(key.Substring(hash + 1), out day) || day >= oldest)
                {
                    continue;
                }
                if (stale == null)
                {
                    stale = new List<string>();
                }
                stale.Add(key);
            }
            if (stale != null)
            {
                foreach (string key in stale)
                {
                    _byDay.Remove(key);
                    // Every elsewhere key also has a _byDay key (Record writes both), so this prunes it too.
                    _elsewhereByDay.Remove(key);
                }
            }
        }

        /// <summary>
        /// Drops the previous campaign's record before this one's save is read -- called from
        /// <see cref="RBMSpoilsCampaignBehavior"/>'s constructor, the same reset-before-load ordering
        /// <see cref="SpoilsPool.Reset"/> relies on.
        /// </summary>
        public static void Reset()
        {
            _byDay = new Dictionary<string, int>();
            _elsewhereByDay = new Dictionary<string, int>();
            _firstDay = -1;
            _lastPruneDay = int.MinValue;
        }

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_clanEventGoldByDay", ref _byDay);
            dataStore.SyncData("RBM_clanEventGoldElsewhereByDay", ref _elsewhereByDay);
            dataStore.SyncData("RBM_clanEventGoldFirstDay", ref _firstDay);
            if (_byDay == null)
            {
                _byDay = new Dictionary<string, int>();
            }
            if (_elsewhereByDay == null)
            {
                _elsewhereByDay = new Dictionary<string, int>();
            }
        }
    }
}
