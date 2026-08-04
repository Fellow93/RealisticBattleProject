using System;
using System.IO;
using System.Text;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// A lightweight verification log for the garrison-refill behavior, under logs/garrison next to the
    /// config. One file per process launch, opened lazily on the first line. Trimmed from
    /// <see cref="CaravanLog"/> and gated only by <c>rbmCampaignEnabled</c> -- there is no separate
    /// config toggle, since this is a debug aid and refill events are rare (only badly depleted lords).
    ///
    /// Categories:
    /// <list type="bullet">
    /// <item>DIVERT -- the behavior steered a depleted lord toward one of his own surplus garrisons.</item>
    /// <item>REFILL -- a garrison actually released troops into an arriving party (vanilla's transfer).</item>
    /// </list>
    /// </summary>
    internal static class GarrisonRefillLog
    {
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;
        private static bool _fileOpened;
        private static int _lastDayKey = -1;
        private static string _lastCategory;

        private const int CategoryWidth = 8;
        private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

        private static readonly string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static bool IsEnabled
        {
            get { return RC.rbmCampaignEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "garrison"); }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogFolderPath, "rbm_garrison_" + _launchStamp + ".log"); }
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
                    LogRetention.PruneOldest(LogFolderPath, "rbm_garrison_*.log");

                    StringBuilder header = new StringBuilder();
                    header.Append("RBM garrison-refill log — ").Append(DateTime.Now).Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Depleted AI lords steered to their own surplus garrisons, and the top-ups that follow.").Append(Environment.NewLine);
                    header.Append("Columns:  time  ·  category  ·  party  ·  message").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Categories:").Append(Environment.NewLine);
                    header.Append("  DIVERT    a depleted lord was routed toward one of his clan's surplus garrisons").Append(Environment.NewLine);
                    header.Append("  REFILL    a garrison released troops into an arriving party (vanilla transfer)").Append(Environment.NewLine);
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

        public static void Log(string category, string party, string message)
        {
            if (!IsEnabled)
            {
                return;
            }

            string line = DateTime.Now.ToString("HH:mm:ss") + "  "
                + (category ?? "").PadRight(CategoryWidth) + "  "
                + Clip(party ?? "", 22).PadRight(22) + "  "
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

        public static string Clip(string text, int width)
        {
            if (text == null)
            {
                return "";
            }
            return (text.Length <= width) ? text : text.Substring(0, width);
        }

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
