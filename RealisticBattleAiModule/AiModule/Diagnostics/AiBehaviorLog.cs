using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using RC = RBMConfig.RBMConfig;

namespace RBMAI
{
    /// <summary>
    /// The file sink behind <see cref="AiBehaviorLogic"/>: one file per mission, under logs/ai, next to the
    /// combat module's logs/battles.
    ///
    /// It exists to answer one question that cannot be answered by watching a battle: why the enemy team's
    /// infantry does one thing and the player's delegated team does another. Both are the same code path with
    /// different inputs -- a different tactic, a different behavior winning the weight contest, a different
    /// movement order landing on the formation. None of that is visible on screen. All of it is written here.
    ///
    /// Lines are buffered and flushed on the second, because a per-line append would be felt in the frame time.
    /// </summary>
    internal static class AiBehaviorLog
    {
        private static readonly object _fileLock = new object();
        private static readonly List<string> _pending = new List<string>();

        private static bool _fileLogFailed;
        private static string _filePath;

        /// <summary>How many AI logs the folder keeps. The rest are deleted as a new one opens.</summary>
        private const int MaxFiles = 10;

        public static bool IsEnabled
        {
            get { return RC.aiBehaviorLogEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "ai"); }
        }

        /// <summary>The path of the file currently open, or null. Only for the header line the logic prints.</summary>
        public static string CurrentFilePath
        {
            get { return _filePath; }
        }

        public static void StartMission(string header)
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
                        "ai_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".log");
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
            }
        }

        /// <summary>Everything held goes to the file. Called once a second by the logic, and at mission end.</summary>
        public static void Flush()
        {
            lock (_fileLock)
            {
                FlushLocked();
            }
        }

        public static void EndMission(string footer)
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

        /// <summary>Housekeeping is never a reason to lose a log: every failure here is swallowed.</summary>
        private static void PruneOldest()
        {
            try
            {
                string[] paths = Directory.GetFiles(LogFolderPath, "ai_*.log");
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

        public static string Fmt(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
