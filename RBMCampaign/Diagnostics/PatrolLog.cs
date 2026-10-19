using System;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using RBMConfig.Logging;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// The settlement-funded patrols (<see cref="PatrolUpkeep"/>), under logs/patrols next to the config. One
    /// file per process launch, opened lazily on the first line. Trimmed from <see cref="GarrisonRefillLog"/>.
    /// Gated by its own Debug &amp; Logging toggle, <c>patrolLoggingEnabled</c> (off by default), on top of
    /// <see cref="PatrolUpkeep.IsEnabled"/> (<c>rbmCampaignEnabled</c>, <c>settlementPatrolsEnabled</c> and the
    /// spoils system).
    ///
    /// Categories:
    /// <list type="bullet">
    /// <item>SPAWN -- a fief raised a patrol: its size, template level, boost, the men drawn from the garrison,
    /// the men bought and their kit, and any it could not find.</item>
    /// <item>REFILL -- a patrol home at its fief manned back up toward its size, the same way.</item>
    /// <item>TRIM -- a patrol at home bigger than its fief's size sent the surplus back to the garrison.</item>
    /// <item>PAY -- one line per patrol per day: the wage paid against the wage owed, and the maintenance met.</item>
    /// <item>DISBAND / LOST -- a patrol stood down (why, and how many men went back to the garrison) or destroyed
    /// in the field.</item>
    /// <item>STATE -- a fief's funding pot, daily budget and patrol size, only on a day its patrol state changed.</item>
    /// </list>
    /// </summary>
    internal static class PatrolLog
    {
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;
        private static bool _fileOpened;

        // The process's open, buffered log file (see BufferedLogWriter). A game-end close only releases the
        // handle; the next line reopens the same file in append mode.
        private static BufferedLogWriter _writer;
        private static int _lastDayKey = -1;
        private static string _lastCategory;

        private const int CategoryWidth = 8;
        private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

        private static readonly string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static bool IsEnabled
        {
            get { return RC.patrolLoggingEnabled && PatrolUpkeep.IsEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "patrols"); }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogFolderPath, "rbm_patrols_" + _launchStamp + ".log"); }
        }

        private static void EnsureFileOpen()
        {
            if (!IsEnabled)
            {
                return;
            }
            lock (_fileLock)
            {
                if (_fileOpened || _fileLogFailed)
                {
                    return;
                }
                try
                {
                    Directory.CreateDirectory(LogFolderPath);
                    LogRetention.PruneOldest(LogFolderPath, "rbm_patrols_*.log");

                    StringBuilder header = new StringBuilder();
                    header.Append("RBM settlement patrol log — ").Append(DateTime.Now).Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Patrols raised, refilled, paid and stood down out of their fiefs' own funds.").Append(Environment.NewLine);
                    header.Append("Columns:  time  ·  category  ·  settlement  ·  message").Append(Environment.NewLine);
                    header.Append("pot = treasury + citizens' money above the reserve floor (towns); B = budget boost; L = template level").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Categories:").Append(Environment.NewLine);
                    header.Append("  SPAWN     a fief raised a patrol: size, template level, boost; men from the garrison, men bought (kit paid), men short (pool / funds)").Append(Environment.NewLine);
                    header.Append("  REFILL    a patrol at home manned back toward its size, the same way").Append(Environment.NewLine);
                    header.Append("  TRIM      a patrol at home over its size sent the surplus back to the garrison").Append(Environment.NewLine);
                    header.Append("  PAY       a patrol's day: wage paid / owed, maintenance met (purse + pot) / owed").Append(Environment.NewLine);
                    header.Append("  DISBAND   a patrol was stood down, why, and how many men went back to the garrison").Append(Environment.NewLine);
                    header.Append("  LOST      a patrol was destroyed in the field").Append(Environment.NewLine);
                    header.Append("  STATE     a fief's pot, budget and patrol size, on a day its patrol state changed").Append(Environment.NewLine);
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

        public static void Log(string category, Settlement settlement, string message)
        {
            if (!IsEnabled)
            {
                return;
            }

            string line = DateTime.Now.ToString("HH:mm:ss") + "  "
                + (category ?? "").PadRight(CategoryWidth) + "  "
                + Clip(Name(settlement), 22).PadRight(22) + "  "
                + message;

            lock (_fileLock)
            {
                StringBuilder block = new StringBuilder();
                string divider = DayDividerIfChanged();
                if (divider != null)
                {
                    block.Append(Environment.NewLine).Append(divider).Append(Environment.NewLine).Append(Environment.NewLine);
                }
                else if (_lastCategory != null && _lastCategory != category)
                {
                    block.Append(Environment.NewLine);
                }
                block.Append(line);
                _lastCategory = category;
                WriteToFile(block.ToString());
            }
        }

        public static string Name(Settlement settlement)
        {
            return settlement == null ? "?" : (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId);
        }

        private static string Clip(string text, int width)
        {
            if (text == null)
            {
                return "";
            }
            return (text.Length <= width) ? text : text.Substring(0, width);
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

                int season = (int)now.GetSeasonOfYear;
                season = Math.Max(0, Math.Min(SeasonNames.Length - 1, season));
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
                // Buffered (see BufferedLogWriter); a failed write drops the line and ends the log for the run.
                if (_writer == null || !_writer.WriteLine(message))
                {
                    _fileLogFailed = true;
                }
            }
        }
    }
}
