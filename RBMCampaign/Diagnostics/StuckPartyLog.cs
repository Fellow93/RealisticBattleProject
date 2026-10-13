using System;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using RBMConfig.Logging;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// The stuck-party diagnostic, under logs/stuckparties next to the config. One file per process launch,
    /// opened lazily on the first line, like <see cref="GarrisonRefillLog"/>. Gated by its own Debug &amp; Logging
    /// toggle, <c>stuckPartyLoggingEnabled</c> (off by default), on top of <c>rbmCampaignEnabled</c>. The lines
    /// come from <see cref="StuckPartyLogBehavior"/>, which says what counts as stuck.
    ///
    /// Categories:
    /// <list type="bullet">
    /// <item>SESSION -- a campaign was started or loaded; tracking starts from zero.</item>
    /// <item>STUCK   -- a party with somewhere to go has not moved for a day; every field we can read about it.</item>
    /// <item>UNSTUCK -- a flagged party moved again.</item>
    /// <item>CLEARED -- a flagged party stopped trying to go anywhere (entered a settlement, battle, army...).</item>
    /// <item>GONE    -- a flagged party was destroyed or left the map.</item>
    /// <item>DAILY   -- once a day: how many parties are being watched and how many are flagged.</item>
    /// </list>
    /// </summary>
    internal static class StuckPartyLog
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
        private const int PartyWidth = 24;
        private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

        private static readonly string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static bool IsEnabled
        {
            get { return RC.rbmCampaignEnabled && RC.stuckPartyLoggingEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "stuckparties"); }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogFolderPath, "rbm_stuckparties_" + _launchStamp + ".log"); }
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
                    LogRetention.PruneOldest(LogFolderPath, "rbm_stuckparties_*.log");

                    string nl = Environment.NewLine;
                    StringBuilder header = new StringBuilder();
                    header.Append("RBM stuck party log — ").Append(DateTime.Now).Append(nl);
                    header.Append(nl);
                    header.Append("Every map party that has somewhere to go but has moved less than ")
                        .Append(StuckPartyLogBehavior.MoveThreshold.ToString("0.0")).Append(" map units in ")
                        .Append(StuckPartyLogBehavior.StuckHours).Append(" in-game hours.").Append(nl);
                    header.Append("Checked once per in-game hour; tracking restarts on every load, so a party already stuck").Append(nl);
                    header.Append("when the save was loaded is reported a day after the load.").Append(nl);
                    header.Append("Columns:  game time (year/day hour)  ·  category  ·  party  ·  fields").Append(nl);
                    header.Append(nl);
                    header.Append("Categories:").Append(nl);
                    header.Append("  SESSION   a campaign was started or loaded; tracking starts from zero").Append(nl);
                    header.Append("  STUCK     a party with a move target has not moved for a day (written once until it moves)").Append(nl);
                    header.Append("  UNSTUCK   a flagged party moved again; how long it was stuck").Append(nl);
                    header.Append("  CLEARED   a flagged party no longer tries to go anywhere (settlement, battle, army, no target)").Append(nl);
                    header.Append("  GONE      a flagged party was destroyed or left the map").Append(nl);
                    header.Append("  DAILY     parties watched (moving with a target) and parties currently flagged").Append(nl);
                    header.Append(nl);
                    header.Append("STUCK fields:").Append(nl);
                    header.Append("  kind      lord / caravan / rbm-caravan (RBM supply caravan) / villager / bandit / deserter / merc-company /").Append(nl);
                    header.Append("            patrol / custom / other; '+army-leader' if it leads an army").Append(nl);
                    header.Append("  pos       map position; land=1 on land, 0 at sea; near = closest settlement and its distance").Append(nl);
                    header.Append("  ai        DefaultBehavior / ShortTermBehavior, PartyMoveMode, Ai.IsDisabled, Ai.DoNotMakeNewDecisions").Append(nl);
                    header.Append("  target    TargetSettlement (port=1 if IsTargetingPort), TargetParty, TargetPosition; dist = to the").Append(nl);
                    header.Append("            target point (port or gate of the settlement); moveTo/next = MoveTargetPoint / NextTargetPosition").Append(nl);
                    header.Append("  nav       DesiredAiNavigationType, NavigationCapability, HasNavalNavigationCapability, IsCurrentlyAtSea,").Append(nl);
                    header.Append("            IsInRaftState, IsTransitionInProgress, StartTransitionNextFrameToExitFromPort, Anchor, ships").Append(nl);
                    header.Append("  path      pathNotFound = the engine's last ComputePath failed (the party then walks straight at the target),").Append(nl);
                    header.Append("            pathMode = following a computed path, lastPathNav = nav type of that path,").Append(nl);
                    header.Append("            exists(desired/all) = MapDistanceModel.PathExistBetweenPoints from here to the target point").Append(nl);
                    header.Append(nl);

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

        public static void Log(string category, string party, string message)
        {
            if (!IsEnabled)
            {
                return;
            }

            string line = GameStamp() + "  "
                + (category ?? "").PadRight(CategoryWidth) + "  "
                + Clip(party ?? "", PartyWidth).PadRight(PartyWidth) + "  "
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

        public static string Clip(string text, int width)
        {
            if (text == null)
            {
                return "";
            }
            return (text.Length <= width) ? text : text.Substring(0, width);
        }

        // In-game time, "y1084 d157 14h" -- the log is about game hours, so the wall clock is left out.
        private static string GameStamp()
        {
            if (!CampaignLogClock.CampaignClockReady())
            {
                return "y---- d--- --h";
            }
            try
            {
                CampaignTime now = CampaignTime.Now;
                return string.Format("y{0} d{1:000} {2:00}h", now.GetYear, now.GetDayOfYear + 1, now.GetHourOfDay);
            }
            catch
            {
                return "y---- d--- --h";
            }
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
