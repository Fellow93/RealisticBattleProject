using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RBMConfig.Logging;
using RC = RBMConfig.RBMConfig;

namespace RBMAI
{
    /// <summary>File sink behind <see cref="AiBehaviorLogic"/>: one file per mission under logs/ai, buffered, flushed each second.</summary>
    internal static class AiBehaviorLog
    {
        private static readonly object _fileLock = new object();
        private static readonly List<string> _pending = new List<string>();

        private static bool _fileLogFailed;
        private static string _filePath;
        private static BufferedLogWriter _writer;

        private const int MaxFiles = 10;

        public static bool IsEnabled
        {
            get { return RC.aiBehaviorLogEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "ai"); }
        }

        public static string CurrentFilePath
        {
            get { return _filePath; }
        }

        public static void StartMission(string header)
        {
            lock (_fileLock)
            {
                CloseWriter();
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
                    _writer = BufferedLogWriter.Open(_filePath);
                    if (!_writer.Write(header))
                    {
                        throw new IOException("header write failed");
                    }
                    _writer.Flush();
                }
                catch
                {
                    CloseWriter();
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
                CloseWriter();
                _filePath = null;
            }
        }

        private static void FlushLocked()
        {
            if (_fileLogFailed || _writer == null || _pending.Count == 0)
            {
                return;
            }

            foreach (string line in _pending)
            {
                if (!_writer.WriteLine(line))
                {
                    _fileLogFailed = true;
                    break;
                }
            }
            _pending.Clear();
            _writer.Flush();
        }

        // Caller holds _fileLock.
        private static void CloseWriter()
        {
            if (_writer != null)
            {
                _writer.Close();
                _writer = null;
            }
        }

        // Failures swallowed.
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
