using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Everything the gear system does is invisible from inside the game: pools live in a side
    /// dictionary, loot is awarded during a map event that has already closed, and the party screen
    /// only ever shows a bar. This writes it all to rbm_gear.log next to the RBM config so the
    /// numbers can be checked after the fact.
    ///
    /// Enabled by the GearLogging config flag. Truncated once per launch.
    /// </summary>
    internal static class GearLog
    {
        private static readonly HashSet<string> _oncePerKey = new HashSet<string>();
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;

        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.gearLoggingEnabled; }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "rbm_gear.log"); }
        }

        public static void Reset()
        {
            lock (_fileLock)
            {
                _oncePerKey.Clear();
                _fileLogFailed = false;
                if (!IsEnabled)
                {
                    return;
                }
                try
                {
                    File.WriteAllText(LogFilePath, "RBM gear log, " + DateTime.Now + Environment.NewLine);
                }
                catch
                {
                    _fileLogFailed = true;
                }
            }
        }

        public static void Log(string category, string message)
        {
            if (!IsEnabled)
            {
                return;
            }
            string line = "[" + category + "] " + message;
            Debug.Print("[RBM][Gear] " + line);
            WriteToFile(line);
            if (!RBMConfig.RBMConfig.developerMode)
            {
                return;
            }
            try
            {
                // The earliest lines run from OnSubModuleLoad, before the message log exists.
                InformationManager.DisplayMessage(new InformationMessage("[RBM] " + line));
            }
            catch
            {
            }
        }

        /// <summary>For lines that would otherwise repeat every frame or every troop refresh.</summary>
        public static void LogOnce(string key, string category, string message)
        {
            if (!IsEnabled)
            {
                return;
            }
            bool isFirst;
            lock (_fileLock)
            {
                isFirst = _oncePerKey.Add(key);
            }
            if (isFirst)
            {
                Log(category, message);
            }
        }

        /// <summary>Shorthand for the party screen and prefab plumbing, which is all one category.</summary>
        public static void Trace(string message)
        {
            Log("UI", message);
        }

        public static void TraceOnce(string key, string message)
        {
            LogOnce(key, "UI", message);
        }

        public static string Describe(PartyBase party)
        {
            if (party == null)
            {
                return "<null party>";
            }
            if (party == PartyBase.MainParty)
            {
                return "MainParty";
            }
            return party.Name != null ? party.Name.ToString() : party.Id;
        }

        public static string Describe(CharacterObject character)
        {
            if (character == null)
            {
                return "<null troop>";
            }
            return character.StringId + " (T" + character.Tier + ")";
        }

        /// <summary>
        /// Prefabs load on a loading thread while the campaign starts on the main one, so these
        /// writes genuinely race. A transient sharing violation must not silently end the log.
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
