using System;
using System.IO;

namespace RBMCampaign
{
    /// <summary>
    /// Every play session opens a fresh timestamped log, so a folder would otherwise grow without bound
    /// over a long campaign. Each log folder keeps only its most recent files; the rest are deleted when
    /// the folder is opened.
    /// </summary>
    internal static class LogRetention
    {
        /// <summary>How many log files a single log folder is allowed to hold, the newest one included.</summary>
        public const int MaxFilesPerFolder = 10;

        /// <summary>
        /// Deletes the oldest files matching <paramref name="searchPattern"/> in <paramref name="folderPath"/>
        /// until at most <see cref="MaxFilesPerFolder"/> remain. Called just before a new log is created, so the
        /// budget is one short of the maximum to leave room for the file about to be written.
        ///
        /// Pruning is housekeeping: a folder that cannot be read, or a file that is locked by an editor or a
        /// previous run, must never take the log itself down, so every failure is swallowed.
        /// </summary>
        public static void PruneOldest(string folderPath, string searchPattern, int keep = MaxFilesPerFolder - 1)
        {
            if (keep < 0)
            {
                keep = 0;
            }
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    return;
                }
                string[] paths = Directory.GetFiles(folderPath, searchPattern);
                if (paths.Length <= keep)
                {
                    return;
                }

                // Sorted by write time rather than by the timestamp in the name: the name is only a
                // convention, and a file whose name was hand-edited should still be aged correctly.
                DateTime[] times = new DateTime[paths.Length];
                for (int i = 0; i < paths.Length; i++)
                {
                    try
                    {
                        times[i] = File.GetLastWriteTimeUtc(paths[i]);
                    }
                    catch
                    {
                        times[i] = DateTime.MinValue;
                    }
                }
                Array.Sort(times, paths);

                for (int i = 0; i < paths.Length - keep; i++)
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
    }
}
