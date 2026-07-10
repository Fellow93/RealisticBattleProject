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
    /// Everything the spoils system does is invisible from inside the game: pools live in a side
    /// dictionary, loot is awarded during a map event that has already closed, and the party screen
    /// only ever shows a bar. This writes it all to rbm_spoils.log next to the RBM config so the
    /// numbers can be checked after the fact.
    ///
    /// Enabled by the SpoilsLogging config flag. Truncated once per launch.
    /// </summary>
    internal static class SpoilsLog
    {
        private static readonly HashSet<string> _oncePerKey = new HashSet<string>();
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;

        /// <summary>
        /// The timestamp is fixed once per launch so every line of a single run lands in the same
        /// file, and a fresh run gets a new name rather than overwriting the last one.
        /// </summary>
        private static readonly string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.spoilsLoggingEnabled; }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "rbm_spoils_" + _launchStamp + ".log"); }
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
                    File.WriteAllText(LogFilePath,
                        "RBM spoils log, " + DateTime.Now + Environment.NewLine
                        + "[wall clock][campaign year-day hour][category][MAIN or AI party] message" + Environment.NewLine);
                }
                catch
                {
                    _fileLogFailed = true;
                }
            }
        }

        /// <summary>
        /// The wall clock, to line a log up against a crash or a screenshot, and the campaign date,
        /// to line it up against the battle that caused it. Neither alone is enough: a day of
        /// campaign time can pass in a second, and a second of real time can span a battle.
        /// </summary>
        /// <remarks>
        /// CampaignTime.Now reads Campaign.Current, which is null for the lines written from
        /// OnSubModuleLoad and from the prefab loading thread before a campaign exists.
        /// </remarks>
        private static string Timestamp()
        {
            string wallClock = DateTime.Now.ToString("HH:mm:ss");
            if (Campaign.Current == null)
            {
                return "[" + wallClock + "][no campaign]";
            }
            CampaignTime now = CampaignTime.Now;
            return string.Format("[{0}][{1}-{2:000} {3:00}h]", wallClock, now.GetYear, now.GetDayOfYear, now.GetHourOfDay);
        }

        /// <summary>
        /// Which party a line is about, since the log carries every party in the world and the player
        /// only ever wants to read his own. Tagged rather than filtered so an AI lord's spoils can
        /// still be checked against his battles.
        /// </summary>
        private static string PartyTag(PartyBase party)
        {
            if (Campaign.Current == null || party == null)
            {
                return "[----] ";
            }
            return (party == PartyBase.MainParty) ? "[MAIN] " : "[ AI ] ";
        }

        public static void Log(string category, PartyBase party, string message)
        {
            if (IsEnabled)
            {
                Log(category, PartyTag(party) + message);
            }
        }

        public static void Log(string category, string message)
        {
            if (!IsEnabled)
            {
                return;
            }
            string line = Timestamp() + "[" + category + "] " + message;
            Debug.Print("[RBM][Spoils] " + line);
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

        public static void LogOnce(string key, string category, PartyBase party, string message)
        {
            LogOnce(key, category, PartyTag(party) + message);
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
