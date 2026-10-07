using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RBMCampaign
{
    /// <summary>
    /// One open, buffered file handle per campaign log, shared by every log under Diagnostics/ (and
    /// Power/StrategicPowerLog). The logs used to call File.AppendAllText per line -- open, seek, write,
    /// close, inside a lock, with Thread.Sleep retries on a sharing violation -- and with logging on that
    /// was the main cause of map stutter. Now a line only lands in a 64 KB buffer; the disk is touched when
    /// the buffer fills or when <see cref="FlushAll"/> runs.
    ///
    /// Flushed: every campaign hour and before a save (RBMSimulationCampaignBehavior), and closed on game end
    /// and module unload (RBM.SubModule) with a ProcessExit fallback. A log that rolls to a new file closes its
    /// old writer itself. The tradeoff: a hard crash loses whatever was written since the last flush, i.e. up to
    /// one in-game hour of lines.
    ///
    /// Thread-safe: SpoilsLog is written from the prefab-loading thread during campaign start, so every writer
    /// has its own lock. Lock order is always writer -> registry; the registry lock is never held while a
    /// writer's lock is taken, so the flush pass (registry snapshot, then each writer) cannot deadlock a writer.
    ///
    /// The file is shared ReadWrite | Delete so it can be tailed while the game runs. Encoding is UTF-8 without
    /// a BOM, which is what File.AppendAllText/WriteAllText wrote before.
    /// </summary>
    public sealed class BufferedLogWriter
    {
        private const int BufferSize = 64 * 1024;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly object _registryLock = new object();
        private static readonly List<BufferedLogWriter> _open = new List<BufferedLogWriter>();
        private static bool _processExitHooked;

        private readonly object _lock = new object();
        private StreamWriter _writer;

        // Set when a write or flush threw. A failed writer stays dead: the line is dropped and the log that owns
        // it gives up until its next session, exactly as the old per-line writer did on a non-sharing error.
        private bool _failed;

        /// <summary>The absolute path this writer appends to.</summary>
        public string FilePath { get; private set; }

        private BufferedLogWriter(string fullPath)
        {
            FilePath = fullPath;
        }

        /// <summary>
        /// Opens <paramref name="path"/> for appending (creating it if missing) and registers it for the flush
        /// passes. Throws if the file cannot be opened; the logs' existing catch turns that into "log failed".
        /// </summary>
        public static BufferedLogWriter Open(string path)
        {
            BufferedLogWriter writer = new BufferedLogWriter(Path.GetFullPath(path));
            lock (writer._lock)
            {
                writer._writer = CreateStream(writer.FilePath);
                Register(writer);
            }
            return writer;
        }

        /// <summary>Writes <paramref name="text"/> as is. False if the line was dropped.</summary>
        public bool Write(string text)
        {
            return WriteCore(text, false);
        }

        /// <summary>Writes <paramref name="text"/> and a line break. False if the line was dropped.</summary>
        public bool WriteLine(string text)
        {
            return WriteCore(text, true);
        }

        private bool WriteCore(string text, bool newLine)
        {
            lock (_lock)
            {
                if (_failed)
                {
                    return false;
                }
                try
                {
                    // Closed by a game-end/unload pass while its log still holds it (GarrisonRefillLog keeps one
                    // file per process): reopen in append mode rather than lose the log for the rest of the run.
                    if (_writer == null)
                    {
                        _writer = CreateStream(FilePath);
                        Register(this);
                    }
                    if (newLine)
                    {
                        _writer.WriteLine(text);
                    }
                    else
                    {
                        _writer.Write(text);
                    }
                    return true;
                }
                catch
                {
                    Fail();
                    return false;
                }
            }
        }

        /// <summary>Pushes the buffer to disk. Never throws.</summary>
        public void Flush()
        {
            lock (_lock)
            {
                if (_writer == null)
                {
                    return;
                }
                try
                {
                    _writer.Flush();
                }
                catch
                {
                    Fail();
                }
            }
        }

        /// <summary>
        /// Flushes and releases the file handle. A later write reopens it in append mode; a log rolling to a new
        /// file drops its reference to the old writer, so that one is simply gone. Never throws.
        /// </summary>
        public void Close()
        {
            lock (_lock)
            {
                if (_writer == null)
                {
                    return;
                }
                try
                {
                    _writer.Flush();
                }
                catch
                {
                    _failed = true;
                }
                DisposeWriter();
                Unregister(this);
            }
        }

        /// <summary>Flushes every open log. Called on the campaign hourly tick and before a save.</summary>
        public static void FlushAll()
        {
            foreach (BufferedLogWriter writer in Snapshot())
            {
                writer.Flush();
            }
        }

        /// <summary>Flushes and closes every open log. Called on game end, module unload and process exit.</summary>
        public static void CloseAll()
        {
            foreach (BufferedLogWriter writer in Snapshot())
            {
                writer.Close();
            }
        }

        /// <summary>
        /// True if a writer currently holds <paramref name="path"/> open. LogRetention skips such a file rather
        /// than deleting it from under a live log (the Delete share would let it, as a pending delete).
        /// </summary>
        public static bool IsOpen(string path)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return false;
            }
            lock (_registryLock)
            {
                foreach (BufferedLogWriter writer in _open)
                {
                    if (string.Equals(writer.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static StreamWriter CreateStream(string fullPath)
        {
            FileStream stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            try
            {
                return new StreamWriter(stream, Utf8NoBom, BufferSize) { AutoFlush = false };
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        // Caller holds this writer's lock.
        private void Fail()
        {
            _failed = true;
            DisposeWriter();
            Unregister(this);
        }

        // Caller holds this writer's lock. Dispose flushes first; if that throws the stream is still closed (the
        // close sits in StreamWriter.Dispose's finally), and the unwritten buffer is dropped.
        private void DisposeWriter()
        {
            if (_writer == null)
            {
                return;
            }
            try
            {
                _writer.Dispose();
            }
            catch
            {
            }
            _writer = null;
        }

        private static void Register(BufferedLogWriter writer)
        {
            lock (_registryLock)
            {
                if (!_processExitHooked)
                {
                    _processExitHooked = true;
                    try
                    {
                        AppDomain.CurrentDomain.ProcessExit += (sender, args) => CloseAll();
                    }
                    catch
                    {
                    }
                }
                if (!_open.Contains(writer))
                {
                    _open.Add(writer);
                }
            }
        }

        private static void Unregister(BufferedLogWriter writer)
        {
            lock (_registryLock)
            {
                _open.Remove(writer);
            }
        }

        private static BufferedLogWriter[] Snapshot()
        {
            lock (_registryLock)
            {
                return _open.ToArray();
            }
        }
    }
}
