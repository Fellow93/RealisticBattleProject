using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using RC = RBMConfig.RBMConfig;

namespace RBMCombat
{
    /// <summary>
    /// The blows of a REAL battle -- the one fought on the field, with the player in it -- written down in the same
    /// shape as the auto-resolve trace, so the two can be laid side by side.
    ///
    /// That comparison is the whole reason this exists. The auto-resolve model is a claim about what a battle IS:
    /// that archers rule the approach and are helpless once it is crossed, that a javelin is worth more than the man
    /// who throws it, that a charge is spent once, that armour decides who walks away. Every one of those claims can
    /// be checked against a battle actually fought under RBM's combat model -- but only if somebody writes down what
    /// happened in one, blow by blow, in a format that lines up column for column.
    ///
    /// Its own folder, logs/battles, next to logs/simulation: one is the field, the other the map.
    ///
    /// Blows are buffered and flushed in blocks. A real battle lands thousands of them, and a file append per blow
    /// would be felt in the frame time.
    /// </summary>
    internal static class BattleHitLog
    {
        private static readonly object _fileLock = new object();
        private static readonly List<string> _pending = new List<string>();

        private static bool _fileLogFailed;
        private static string _filePath;

        /// <summary>Blows held before the file is touched. A battle throws thousands; the disk is not asked for each.</summary>
        private const int FlushThreshold = 256;

        /// <summary>How many battle logs the folder keeps. The rest are deleted as a new one opens.</summary>
        private const int MaxFiles = 10;

        public static bool IsEnabled
        {
            get { return RC.battleHitLoggingEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "battles"); }
        }

        /// <summary>
        /// A battle is starting: open its own file. One file per battle, not per session -- unlike the campaign
        /// logs, a mission is a single self-contained fight and there is nothing to be gained by running several
        /// of them together in one document.
        /// </summary>
        public static void StartBattle(string header)
        {
            lock (_fileLock)
            {
                _pending.Clear();
                _fileLogFailed = false;
                _filePath = null;

                if (!IsEnabled)
                {
                    return;
                }

                try
                {
                    Directory.CreateDirectory(LogFolderPath);
                    PruneOldest();
                    _filePath = Path.Combine(LogFolderPath,
                        "rbm_battle_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".log");
                    File.WriteAllText(_filePath, header);
                }
                catch
                {
                    _fileLogFailed = true;
                    _filePath = null;
                }
            }
        }

        public static void Write(string line)
        {
            if (!IsEnabled || _fileLogFailed || _filePath == null)
            {
                return;
            }
            lock (_fileLock)
            {
                _pending.Add(line);
                if (_pending.Count >= FlushThreshold)
                {
                    FlushLocked();
                }
            }
        }

        /// <summary>The battle is over: everything still in hand goes to the file, and the file is let go.</summary>
        public static void EndBattle(string footer)
        {
            lock (_fileLock)
            {
                if (footer != null)
                {
                    _pending.Add(footer);
                }
                FlushLocked();
                _filePath = null;
            }
        }

        private static void FlushLocked()
        {
            if (_fileLogFailed || _filePath == null || _pending.Count == 0)
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (string line in _pending)
            {
                sb.Append(line).Append(Environment.NewLine);
            }
            _pending.Clear();

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.AppendAllText(_filePath, sb.ToString());
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

        /// <summary>
        /// Every battle opens a fresh file, so the folder would grow without bound over a campaign. Housekeeping is
        /// never a reason to lose a log: every failure here is swallowed.
        /// </summary>
        private static void PruneOldest()
        {
            try
            {
                string[] paths = Directory.GetFiles(LogFolderPath, "rbm_battle_*.log");
                if (paths.Length < MaxFiles)
                {
                    return;
                }
                Array.Sort(paths, (a, b) => File.GetCreationTimeUtc(a).CompareTo(File.GetCreationTimeUtc(b)));
                for (int i = 0; i <= paths.Length - MaxFiles; i++)
                {
                    try
                    {
                        File.Delete(paths[i]);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>The same two-decimal figure the simulation log prints, so columns of one can be read against the other.</summary>
        public static string Fmt(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>Mission time as a clock, since a real battle has no rounds to number.</summary>
        public static string Clock(float seconds)
        {
            int whole = (int)seconds;
            return (whole / 60).ToString() + ":" + (whole % 60).ToString("00");
        }
    }
}
