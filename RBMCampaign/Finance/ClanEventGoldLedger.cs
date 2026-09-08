using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>The kinds of gold RBM hands the player's clan outside the daily finance model.</summary>
    public enum EventGoldKind
    {
        /// <summary>The commander's cut of a gather -- battle loot, raid plunder or a sack (<see cref="SpoilsPool.ApplyLeaderCut"/>).</summary>
        LeaderCut,
        /// <summary>The spoils share companions in a clan party claim straight into the clan's gold.</summary>
        CompanionSpoils,
        /// <summary>The owner's and ruler's cuts of a settlement's mint output (<see cref="Minting"/>).</summary>
        Minting,
        /// <summary>Gold a clan party's leader was billed for troop promotions its spoils could not cover (a drain).</summary>
        UpgradeGold
    }

    /// <summary>
    /// A rolling record of the gold RBM pays the player's clan -- or takes from it -- per EVENT rather than
    /// per day, so the clan finance breakdown can show it. The leader's cut of spoils, the companions' share,
    /// a mint's cut and a clan party's gold-paid promotions all move through <c>GiveGoldAction</c> the moment
    /// they happen, which the finance model never sees; without this they are the largest swings in the
    /// player's purse that the Daily Gold Change cannot account for.
    ///
    /// What the breakdown shows is a <see cref="WindowDays"/>-day daily average, labelled as such: a battle
    /// does not come every day, so "today's" figure would be a meaningless spike, but the average is an honest
    /// projection of what the clan's fighting has been earning it. Display only -- the gold was paid when the
    /// event fired, so nothing here is ever applied to the clan's gold again. Player clan only: no one reads an
    /// AI clan's finance screen, so the store stays tiny.
    /// </summary>
    public static class ClanEventGoldLedger
    {
        /// <summary>The averaging window, in campaign days.</summary>
        public const int WindowDays = 14;

        // "kind#day" -> gold that kind paid (or took) that campaign day. Only the player's clan is recorded.
        private static Dictionary<string, int> _byDay = new Dictionary<string, int>();
        // The first campaign day anything was recorded, so a window younger than WindowDays is averaged
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
            PruneIfNeeded(today);
        }

        /// <summary>
        /// The daily average of <paramref name="kind"/> over the last <see cref="WindowDays"/> days (or over
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
            for (int day = today - WindowDays + 1; day <= today; day++)
            {
                int gold;
                if (_byDay.TryGetValue(Key(kind, day), out gold))
                {
                    sum += gold;
                }
            }
            int days = Math.Min(WindowDays, today - _firstDay + 1);
            if (days < 1)
            {
                days = 1;
            }
            return (int)Math.Round((double)sum / days);
        }

        /// <summary>
        /// Adds the averaged event lines to a finance breakdown: the gains when <paramref name="income"/> is
        /// set, the drains when <paramref name="expense"/> is. Display only; the caller guards the pass.
        /// </summary>
        public static void AddDisplayLines(ref ExplainedNumber breakdown, bool income, bool expense)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            if (income)
            {
                AddLine(ref breakdown, EventGoldKind.LeaderCut, 1, "{=RBM_fin_leader_cut}Your cut of the spoils ({DAYS}-day avg.)");
                AddLine(ref breakdown, EventGoldKind.CompanionSpoils, 1, "{=RBM_fin_companion_spoils}Companions' spoils share ({DAYS}-day avg.)");
                AddLine(ref breakdown, EventGoldKind.Minting, 1, "{=RBM_fin_minting}Mint revenue ({DAYS}-day avg.)");
            }
            if (expense)
            {
                AddLine(ref breakdown, EventGoldKind.UpgradeGold, -1, "{=RBM_fin_upgrade_gold}Clan party promotions paid in gold ({DAYS}-day avg.)");
            }
        }

        private static void AddLine(ref ExplainedNumber breakdown, EventGoldKind kind, int sign, string text)
        {
            int average = DailyAverage(kind);
            if (average <= 0)
            {
                return;
            }
            TextObject label = new TextObject(text);
            label.SetTextVariable("DAYS", WindowDays);
            breakdown.Add(sign * average, label);
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
            int oldest = today - WindowDays + 1;
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
            _firstDay = -1;
            _lastPruneDay = int.MinValue;
        }

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_clanEventGoldByDay", ref _byDay);
            dataStore.SyncData("RBM_clanEventGoldFirstDay", ref _firstDay);
            if (_byDay == null)
            {
                _byDay = new Dictionary<string, int>();
            }
        }
    }
}
