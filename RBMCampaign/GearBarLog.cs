using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// The gear bar is assembled from pieces that each fail silently on their own: the prefab
    /// injection, the widget type registration, and the data binding. These traces say which one
    /// did not happen. They always go to a file next to the RBM config, and to the in-game message
    /// log under developerMode.
    /// </summary>
    internal static class GearBarLog
    {
        private static readonly HashSet<string> _oncePerKey = new HashSet<string>();
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;

        private static string LogFilePath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "rbm_gearbar.log"); }
        }

        public static void Reset()
        {
            lock (_fileLock)
            {
                _oncePerKey.Clear();
                _fileLogFailed = false;
                try
                {
                    File.WriteAllText(LogFilePath, "RBM gear bar trace, " + DateTime.Now + Environment.NewLine);
                }
                catch
                {
                    _fileLogFailed = true;
                }
            }
        }

        public static void Trace(string message)
        {
            Debug.Print("[RBM][GearBar] " + message);
            WriteToFile(message);
            if (!RBMConfig.RBMConfig.developerMode)
            {
                return;
            }
            try
            {
                // The earliest traces run from OnSubModuleLoad, before the message log exists.
                InformationManager.DisplayMessage(new InformationMessage("[RBM] " + message));
            }
            catch
            {
            }
        }

        public static void TraceOnce(string key, string message)
        {
            bool isFirst;
            lock (_fileLock)
            {
                isFirst = _oncePerKey.Add(key);
            }
            if (isFirst)
            {
                Trace(message);
            }
        }

        /// <summary>
        /// Prefabs load on a loading thread while the campaign starts on the main one, so these
        /// writes genuinely race. A transient sharing violation must not silently end the trace.
        /// </summary>
        private static void WriteToFile(string message)
        {
            if (_fileLogFailed)
            {
                return;
            }
            lock (_fileLock)
            {
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
