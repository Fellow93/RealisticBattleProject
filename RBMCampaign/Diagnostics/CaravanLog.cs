using System;
using System.IO;
using System.Text;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// Writes the intra-kingdom supply-caravan system down as it runs, under logs/caravans next to the
    /// config. One file per play session, opened lazily on the first line so switching the flag on
    /// mid-session starts a log from that moment. A near-copy of <see cref="EconomyLog"/>, kept apart
    /// because it is about a different subject.
    ///
    /// Categories:
    /// <list type="bullet">
    /// <item>DISPATCH -- a caravan set out: source, destination, good, load, the shortage it fills.</item>
    /// <item>ARRIVE   -- a caravan sold into its destination: units sold of units carried, revenue, purse.</item>
    /// <item>RETURN   -- a caravan home at its source, paying the takings to the source citizens.</item>
    /// <item>ABORT    -- a caravan reached a destination it could no longer sell into (siege, gone).</item>
    /// <item>LOST     -- a caravan destroyed on the road, outbound or on the way home.</item>
    /// </list>
    /// </summary>
    internal static class CaravanLog
    {
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;
        private static bool _fileOpened;
        private static int _lastDayKey = -1;
        private static string _lastCategory;

        private const int CategoryWidth = 8;
        private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

        private static string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static bool IsEnabled
        {
            get { return RC.rbmCampaignEnabled && RC.kingdomCaravansEnabled && RC.caravanLoggingEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "caravans"); }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogFolderPath, "rbm_caravans_" + _launchStamp + ".log"); }
        }

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
                    LogRetention.PruneOldest(LogFolderPath, "rbm_caravans_*.log");

                    StringBuilder header = new StringBuilder();
                    header.Append("RBM intra-kingdom supply caravans log — ").Append(DateTime.Now).Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Caravans this module spawns to move a surplus good to a short town of the same kingdom.").Append(Environment.NewLine);
                    header.Append("Columns:  time  ·  category  ·  settlement  ·  message").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Categories:").Append(Environment.NewLine);
                    header.Append("  DISPATCH  a caravan set out: source, destination, good, load").Append(Environment.NewLine);
                    header.Append("  ARRIVE    a caravan sold into its destination: units of units, revenue, purse").Append(Environment.NewLine);
                    header.Append("  RETURN    a caravan home at its source, paying the takings to the source citizens").Append(Environment.NewLine);
                    header.Append("  ABORT     a caravan could no longer sell into its destination").Append(Environment.NewLine);
                    header.Append("  LOST      a caravan destroyed on the road, outbound or homebound").Append(Environment.NewLine);
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

        /// <summary>
        /// A managed caravan destroyed in transit. Resolves the errand's names itself since the register
        /// hands it only ids.
        /// </summary>
        public static void Lost(RBMCaravanRegister.Order order)
        {
            if (!IsEnabled || order == null)
            {
                return;
            }
            Settlement src = RBMCaravanRegister.FindSettlement(order.SourceId);
            Settlement dst = RBMCaravanRegister.FindSettlement(order.DestId);
            if (order.State == RBMCaravanRegister.StateReturning)
            {
                Log("LOST", Name(src),
                    "caravan lost returning from " + Name(dst)
                    + "  ·  " + order.Proceeds + "d of takings never reached " + Name(src) + "'s citizens");
            }
            else
            {
                Log("LOST", Name(src),
                    "caravan lost in transit  ·  " + RBMCaravanRegister.DescribeGoods(order.Goods)
                    + " bound for " + Name(dst) + " never delivered");
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
