using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using TaleWorlds.CampaignSystem;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// The campaign economy is a chain of quiet daily arithmetic -- hearths make goods, villagers walk
    /// them to a town, the town eats them, prosperity follows -- and none of its links are visible from
    /// inside the game. This writes the whole chain down, under logs/economy, apart from the spoils and
    /// simulation logs because it is about a different subject entirely.
    ///
    /// Seven kinds of line, one category each:
    /// <list type="bullet">
    /// <item>PRODUCE  -- a village's daily output, good by good (<see cref="RBMVillageProduction"/>).</item>
    /// <item>DISPATCH -- a villager party setting out: its size, composition, cargo and escort.</item>
    /// <item>DELIVER  -- that party arriving and selling into a town, and the purse it sold against
    /// (<see cref="VillagerDelivery"/>).</item>
    /// <item>FOOD     -- a town's daily rations: what it delivered, ate, and failed to buy
    /// (<see cref="RBMTownFoodSupply"/>).</item>
    /// <item>DAILY    -- the end-of-day state of every settlement: prosperity against its countryside
    /// equilibrium, hearth, food stock and measured food change.</item>
    /// <item>PROSPER  -- a fief against that equilibrium, broken down: the gap, the rate closing it,
    /// and every term pushing on prosperity (<see cref="RBMProsperityEquilibrium"/>).</item>
    /// <item>WORKSHOP -- a town's initial or re-rolled workshop pick and the bound-village types that
    /// biased it (<see cref="WorkshopVillageBias"/>).</item>
    /// <item>BUILD    -- a fief's building day: the project, its labour ceiling, the points free labour,
    /// bought materials and wages each paid for, and the reserve left (<see cref="Construction"/>).</item>
    /// </list>
    ///
    /// Enabled by the EconomyLogging config flag. One file per play session, opened lazily on the first
    /// line so switching the flag on mid-session starts a log from that moment.
    /// </summary>
    internal static class EconomyLog
    {
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;
        private static bool _fileOpened;

        // The campaign day is printed once as a divider when it rolls over, rather than on every line.
        private static int _lastDayKey = -1;
        private static string _lastCategory;

        private const int CategoryWidth = 8;
        private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

        private static string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static bool IsEnabled
        {
            get { return RC.rbmCampaignEnabled && RC.economyLoggingEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "economy"); }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogFolderPath, "rbm_economy_" + _launchStamp + ".log"); }
        }

        /// <summary>
        /// A campaign launching -- new game or loaded save -- rolls the log over to a fresh file, so each
        /// play session stands in its own log. The file itself is opened on the first line written.
        /// </summary>
        public static void StartCampaignLog()
        {
            lock (_fileLock)
            {
                _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                _fileLogFailed = false;
                _fileOpened = false;
                _lastDayKey = -1;
                _lastCategory = null;
            }
            EnsureFileOpen();
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
                    LogRetention.PruneOldest(LogFolderPath, "rbm_economy_*.log");

                    StringBuilder header = new StringBuilder();
                    header.Append("RBM economy log — ").Append(DateTime.Now).Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("The village-to-town food and goods chain, written down as it runs.").Append(Environment.NewLine);
                    header.Append("Columns:  time  ·  category  ·  settlement  ·  message").Append(Environment.NewLine);
                    header.Append("  (campaign day is shown in the ═══ dividers, not on every line)").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Categories:").Append(Environment.NewLine);
                    header.Append("  PRODUCE   a village's daily output, good by good").Append(Environment.NewLine);
                    header.Append("  DISPATCH  a villager party setting out: size, composition, cargo, escort").Append(Environment.NewLine);
                    header.Append("  DELIVER   a villager party selling into a town: cargo sold, food landed, town purse").Append(Environment.NewLine);
                    header.Append("  FOOD      a town's daily rations: delivered, eaten, unmet").Append(Environment.NewLine);
                    header.Append("  DAILY     end-of-day settlement state: prosperity, hearth, food").Append(Environment.NewLine);
                    header.Append("  PROSPER   a fief against its countryside equilibrium: gap, rate, and every term moving it").Append(Environment.NewLine);
                    header.Append("  WORKSHOP  a town's initial/re-rolled workshop pick and the bound-village types biasing it").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Settings:").Append(Environment.NewLine);
                    header.Append("  rbmCampaignEnabled          = ").Append(RC.rbmCampaignEnabled).Append(Environment.NewLine);
                    header.Append("  realisticTradeGoodPrices    = ").Append(RC.realisticTradeGoodPrices).Append(Environment.NewLine);
                    header.Append("  prosperityPerBoundHearth    = ").Append(Fmt(RBMProsperityEquilibrium.ProsperityPerBoundHearth)).Append(Environment.NewLine);
                    header.Append("  vanillaProsperityScale      = ").Append(Fmt(RBMProsperityEquilibrium.VanillaProsperityScale)).Append(Environment.NewLine);
                    header.Append("  townTreasuryScale           = ").Append(Fmt(RBMProsperityEquilibrium.TownTreasuryScale)).Append(Environment.NewLine);
                    header.Append("  (castles: vanilla prosperity)").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);

                    File.WriteAllText(LogFilePath, header.ToString());
                    _fileOpened = true;
                }
                catch
                {
                    _fileLogFailed = true;
                }
            }
        }

        /// <summary>One aligned line, tagged with its category and the settlement it is about.</summary>
        public static void Log(string category, string settlement, string message)
        {
            if (!IsEnabled)
            {
                return;
            }

            string line = DateTime.Now.ToString("HH:mm:ss") + "  "
                + (category ?? "").PadRight(CategoryWidth) + "  "
                + Clip(settlement ?? "", 22).PadRight(22) + "  "
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

        public static string Fmt(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static string Clip(string text, int width)
        {
            if (text == null)
            {
                return "";
            }
            return (text.Length <= width) ? text : text.Substring(0, width);
        }

        /// <summary>
        /// A dated divider the first time a line is written on a new campaign day, else null. Reads the
        /// campaign clock, which does not exist before a campaign starts and can throw mid-init; either
        /// case simply yields no divider.
        /// </summary>
        private static string DayDividerIfChanged()
        {
            if (Campaign.Current == null)
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

                // Season comes from the clock rather than being derived from the day number.
                // GetDayOfYear is 0-based (it is a modulo), so the old (dayOfYear - 1) / 21 rolled
                // over a day early and mislabelled the first day of every season -- in a log whose
                // whole purpose is reading a campaign back against its calendar.
                int season = (int)now.GetSeasonOfYear;
                season = Math.Max(0, Math.Min(SeasonNames.Length - 1, season));

                // Days printed 1-based, as the game's own date display shows them.
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
            // Logging may have been switched on since the campaign started; open the file now if so.
            EnsureFileOpen();
            lock (_fileLock)
            {
                if (!_fileOpened)
                {
                    return;
                }
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        File.AppendAllText(LogFilePath, message + Environment.NewLine);
                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(2);
                    }
                    catch
                    {
                        _fileLogFailed = true;
                        return;
                    }
                }
            }
        }
    }
}
