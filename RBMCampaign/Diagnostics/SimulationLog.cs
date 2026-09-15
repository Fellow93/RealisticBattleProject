using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// The auto-resolve model can only be judged by what it does to battles, and a battle resolves in a single
    /// silent instant on the campaign map. This writes each one out to its own log -- under logs/simulation,
    /// apart from the spoils log, which is about something else entirely -- as it was actually fought, so what
    /// the model did can be read rather than assumed.
    ///
    /// Enabled by the SimulationLogging config flag. One file per play session.
    /// </summary>
    internal static class SimulationLog
    {
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;
        private static bool _fileOpened;

        private static string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static bool IsEnabled
        {
            get { return RC.rbmCampaignEnabled && RC.simulationLoggingEnabled; }
        }

        // Its own folder next to the campaign one: this log is about the battle model, not the purse.
        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "simulation"); }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogFolderPath, "rbm_simulation_" + _launchStamp + ".log"); }
        }

        /// <summary>
        /// A campaign launching -- new game or loaded save -- rolls the log over to a fresh file. The file itself
        /// is not opened here: logging is off by default and can be switched on from the config screen in the
        /// middle of a session, and a log that only began working after a reload would look simply broken.
        /// <see cref="EnsureFileOpen"/> opens it on the first battle that actually has something to say.
        /// </summary>
        public static void StartCampaignLog()
        {
            lock (_fileLock)
            {
                _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                _fileLogFailed = false;
                _fileOpened = false;
            }
            EnsureFileOpen();
        }

        /// <summary>
        /// Opens the log and writes its header, the first time there is anything to write. Does nothing while
        /// logging is off, so switching it on mid-session starts a log from that moment rather than never.
        /// The settings are stamped at the top because the numbers below mean nothing without them.
        /// </summary>
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
                    LogRetention.PruneOldest(LogFolderPath, "rbm_simulation_*.log");
                    StringBuilder header = new StringBuilder();
                    header.Append("RBM simulation log — ").Append(DateTime.Now).Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Every auto-resolved battle is written down as it was fought: who stood on each side,")
                          .Append(Environment.NewLine);
                    header.Append("what they carried, how it ended, and then every blow of it as it landed.")
                          .Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Settings:").Append(Environment.NewLine);
                    header.Append("  rbmCombatEnabled              = ").Append(RC.rbmCombatEnabled).Append(Environment.NewLine);
                    header.Append("     (picks the armour equation the model prices kit with:")
                          .Append(RC.rbmCombatEnabled ? " RBM's" : " vanilla's").Append(")").Append(Environment.NewLine);
                    header.Append("  simulationEquipmentEnabled    = ").Append(RC.simulationEquipmentEnabled).Append(Environment.NewLine);
                    header.Append("  simulationEquipmentPowerWeight= ").Append(Fmt(RC.simulationEquipmentPowerWeight)).Append(Environment.NewLine);
                    header.Append("  simulationArmTargeting        = ").Append(RC.simulationArmTargeting).Append(Environment.NewLine);
                    header.Append("     (picks striker/struck by phase and arm; when on, VolleyFocus stands down)").Append(Environment.NewLine);
                    header.Append("  simulationLogHits             = ").Append(RC.simulationLogHits).Append(Environment.NewLine);
                    header.Append("  armorMultiplier               = ").Append(Fmt(RC.armorMultiplier)).Append(Environment.NewLine);
                    header.Append("  armorThresholdModifier        = ").Append(Fmt(RC.armorThresholdModifier)).Append(Environment.NewLine);
                    header.Append(Environment.NewLine);

                    // Not a toggle -- a standing fact of the model, stated because it shapes every field blow below.
                    // On a FIELD battle the terrain-vs-arm context modifier (cavalry a quarter more in the open, and
                    // so on) is lifted back out and folded into each blow's Correction; an arm's edge comes from its
                    // kit, already priced, not the ground. A SIEGE keeps its full vanilla context. So a field
                    // Correction already has the terrain removed from it -- read it against a Vanilla figure that
                    // still carries the terrain, which is why the two can differ even where the kit is even.
                    header.Append("Field terrain: neutralized (arm-vs-terrain context lifted; siege keeps its own).")
                          .Append(Environment.NewLine);
                    header.Append(Environment.NewLine);

                    // Every correction is a ratio against these. A wrong baseline makes every blow wrong without
                    // making any single blow LOOK wrong, so it is written out where it can be checked at a glance.
                    header.Append(SimulationEquipmentPower.DescribeBaselines());
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

        public static void Write(string block)
        {
            if (!IsEnabled)
            {
                return;
            }
            WriteToFile(block);
            Debug.Print("[RBM][Sim] " + block);
        }

        public static string Fmt(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
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

        /// <summary>The campaign date a battle was fought on, for lining a record up against a save.</summary>
        public static string CampaignDate()
        {
            try
            {
                if (Campaign.Current == null)
                {
                    return "----";
                }
                CampaignTime now = CampaignTime.Now;
                return string.Format("{0}-{1:000}", now.GetYear, now.GetDayOfYear);
            }
            catch
            {
                return "----";
            }
        }
    }
}
