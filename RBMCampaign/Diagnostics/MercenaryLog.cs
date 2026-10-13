using System;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using RBMConfig.Logging;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// The mercenary companies' own log, under logs/mercenaries next to the config. One file per process
    /// launch, opened lazily on the first line, written through <see cref="BufferedLogWriter"/> (flushed hourly
    /// and before a save, closed on game end) and pruned by <see cref="LogRetention"/>. Built like
    /// <see cref="GarrisonRefillLog"/>. Gated by its own Debug &amp; Logging toggle, <c>mercenaryLoggingEnabled</c>
    /// (off by default), on top of <c>rbmCampaignEnabled</c> and <c>mercenaryCompaniesEnabled</c>.
    ///
    /// Every line is <c>time  TAG  company  message</c>; the tags are listed in the file header and are stable,
    /// so a test session can be grepped by tag (e.g. <c>CONTRACT|END|LEAVE</c> for one contract's life).
    /// </summary>
    internal static class MercenaryLog
    {
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;
        private static bool _fileOpened;
        private static BufferedLogWriter _writer;
        private static int _lastDayKey = -1;

        private const int CategoryWidth = 9;
        private const int PartyWidth = 26;
        private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };
        private static readonly string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static bool IsEnabled
        {
            get { return RC.rbmCampaignEnabled && RC.mercenaryCompaniesEnabled && RC.mercenaryLoggingEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "mercenaries"); }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogFolderPath, "rbm_mercenaries_" + _launchStamp + ".log"); }
        }

        private static void EnsureFileOpen()
        {
            lock (_fileLock)
            {
                if (_fileOpened || _fileLogFailed)
                {
                    return;
                }
                try
                {
                    Directory.CreateDirectory(LogFolderPath);
                    LogRetention.PruneOldest(LogFolderPath, "rbm_mercenaries_*.log");

                    StringBuilder header = new StringBuilder();
                    header.Append("RBM mercenary companies log — ").Append(DateTime.Now).Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Columns:  time  ·  tag  ·  company  ·  message").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Tags:").Append(Environment.NewLine);
                    header.Append("  CREATE    a company (clan) was created: id, culture, home town, leader, gold").Append(Environment.NewLine);
                    header.Append("  SPAWN     a company party was spawned (new company or respawn): town, men, size limit, gold").Append(Environment.NewLine);
                    header.Append("  WAIT      a company without a party, or an empty slot, is waiting to respawn (once per change)").Append(Environment.NewLine);
                    header.Append("  GONE      a company was found destroyed (its slot starts the respawn clock)").Append(Environment.NewLine);
                    header.Append("  ROAM      an idle company's new roaming target (recruit town or bandit) and its score; only on change").Append(Environment.NewLine);
                    header.Append("  TAVERN    tavern mercenaries hired: town, troop, count, price, gold left").Append(Environment.NewLine);
                    header.Append("  BLOCK     a non-mercenary troop kept out (notable volunteers, prisoner recruits per troop); once per company and source per game session").Append(Environment.NewLine);
                    header.Append("  PURGE     a non-mercenary troop found in a company party and dismissed (backstop)").Append(Environment.NewLine);
                    header.Append("  DESERT    men deserted a company: count, troops, wage vs payment limit, gold, morale, unpaid wages").Append(Environment.NewLine);
                    header.Append("  STATUS    daily per company: men, full and paid wage (half while idle), limit, gold, morale, food").Append(Environment.NewLine);
                    header.Append("  CONTRACT  a contract started: employer, kingdom, army leader, hire price, daily pay, AI or player").Append(Environment.NewLine);
                    header.Append("  PAY       a day's contract pay").Append(Environment.NewLine);
                    header.Append("  END       a contract ended, and why").Append(Environment.NewLine);
                    header.Append("  DEFER     leaving the kingdom deferred: the party is in a battle or siege").Append(Environment.NewLine);
                    header.Append("  LEAVE     the company actually left the kingdom").Append(Environment.NewLine);
                    header.Append("  DISBAND   Mercenary Companies switched off: a company leaving its kingdom and being destroyed (or deferred)").Append(Environment.NewLine);
                    header.Append("  ARMY-NEW  every army the ArmyCreated event reports (player's too), when it fires").Append(Environment.NewLine);
                    header.Append("  AI-HIRE   an AI army's hiring pass: one line per army, hires and every company passed over with why").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);

                    _writer = BufferedLogWriter.Open(LogFilePath);
                    _writer.Write(header.ToString());
                    _writer.Flush();
                    _fileOpened = true;
                }
                catch
                {
                    _fileLogFailed = true;
                }
            }
        }

        public static void Log(string tag, MobileParty party, string message)
        {
            Log(tag, party != null ? Who(party) : "", message);
        }

        public static void Log(string tag, Clan clan, string message)
        {
            Log(tag, clan != null ? Who(clan) : "", message);
        }

        public static void Log(string tag, string company, string message)
        {
            if (!IsEnabled)
            {
                return;
            }
            Write(tag, company, message);
        }

        /// <summary>
        /// The disband cleanup's lines: they are written while the feature itself is switched off, so they only
        /// need RBM Campaign and the logging toggle.
        /// </summary>
        public static void LogCleanup(string tag, string company, string message)
        {
            if (!RC.rbmCampaignEnabled || !RC.mercenaryLoggingEnabled)
            {
                return;
            }
            Write(tag, company, message);
        }

        private static void Write(string tag, string company, string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  "
                + (tag ?? "").PadRight(CategoryWidth) + "  "
                + Clip(company ?? "", PartyWidth).PadRight(PartyWidth) + "  "
                + message;
            lock (_fileLock)
            {
                string divider = DayDividerIfChanged();
                WriteToFile(divider != null ? Environment.NewLine + divider + Environment.NewLine + line : line);
            }
        }

        /// <summary>The company's name and clan id, e.g. "The Free Lances [vlandia_0_g0]".</summary>
        public static string Who(Clan clan)
        {
            if (clan == null)
            {
                return "?";
            }
            string id = clan.StringId ?? "";
            if (id.StartsWith(MercenaryCompanies.IdPrefix, StringComparison.Ordinal))
            {
                id = id.Substring(MercenaryCompanies.IdPrefix.Length);
            }
            return (clan.Name != null ? clan.Name.ToString() : "?") + " [" + id + "]";
        }

        public static string Who(MobileParty party)
        {
            if (party == null)
            {
                return "?";
            }
            return party.ActualClan != null ? Who(party.ActualClan) : (party.Name != null ? party.Name.ToString() : party.StringId);
        }

        private static string Clip(string text, int width)
        {
            return text.Length <= width ? text : text.Substring(0, width);
        }

        private static string DayDividerIfChanged()
        {
            if (!CampaignLogClock.CampaignClockReady())
            {
                return null;
            }
            try
            {
                CampaignTime now = CampaignTime.Now;
                int year = now.GetYear;
                int dayOfYear = now.GetDayOfYear;
                int dayKey = year * 1000 + dayOfYear;
                if (dayKey == _lastDayKey)
                {
                    return null;
                }
                _lastDayKey = dayKey;
                int season = Math.Max(0, Math.Min(SeasonNames.Length - 1, (int)now.GetSeasonOfYear));
                return string.Format("════════ campaign {0}-{1:000}  ·  {2} ════════", year, dayOfYear + 1, SeasonNames[season]);
            }
            catch
            {
                return null;
            }
        }

        private static void WriteToFile(string message)
        {
            if (_fileLogFailed)
            {
                return;
            }
            EnsureFileOpen();
            lock (_fileLock)
            {
                if (!_fileOpened)
                {
                    return;
                }
                if (_writer == null || !_writer.WriteLine(message))
                {
                    _fileLogFailed = true;
                }
            }
        }
    }
}
