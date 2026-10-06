using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace RBMCombat
{
    /// <summary>
    /// The recorder behind the troop perk log (troopPerkLoggingEnabled, written by <see cref="TroopPerkLogLogic"/>):
    /// every answer the GetPerkValue postfixes in <see cref="TroopPerkEffects"/> give for a listed (troop, perk) pair
    /// during one mission. A perk the game never asks about for a troop has no effect on it, whatever the troop
    /// perks file says, and this is the only way to see which ones those are.
    ///
    /// Cost: while no <see cref="TroopPerkLogLogic"/> is running, <see cref="Recording"/> is false and the postfixes
    /// pay one bool read on a grant, nothing else. While recording, a repeat answer is one dictionary lookup and an
    /// interlocked increment; only the first answer for a key in a mission walks the stack to name the caller.
    ///
    /// Threads: GetPerkValue can be called off the main thread during missions (agent stat updates), so the store is
    /// a ConcurrentDictionary and the recording path touches nothing but the objects it is handed and reflection on
    /// the managed stack -- no engine or game call. Names are only read when the file is written, on the main thread.
    /// </summary>
    public static class TroopPerkLog
    {
        /// <summary>
        /// True while a <see cref="TroopPerkLogLogic"/> is recording a mission. The postfixes check this before
        /// calling <see cref="Record"/>; volatile so a worker thread sees the logic switch it off.
        /// </summary>
        public static volatile bool Recording;

        /// <summary>One distinct question: which troop, which perk, through which overload, and what was answered.</summary>
        internal struct Key : IEquatable<Key>
        {
            public readonly CharacterObject Troop;
            public readonly PerkObject Perk;

            /// <summary>False: GetPerkValue(perk). True: GetPerkValue(perk, environment, isPrimaryEffect, out value).</summary>
            public readonly bool WithEnvironment;

            public readonly BattleEnvironment Environment;
            public readonly bool IsPrimary;

            /// <summary>True: answered yes. False: the environment overload, refused because the effect does not apply there.</summary>
            public readonly bool Granted;

            public Key(CharacterObject troop, PerkObject perk, bool withEnvironment, BattleEnvironment environment, bool isPrimary, bool granted)
            {
                Troop = troop;
                Perk = perk;
                WithEnvironment = withEnvironment;
                Environment = withEnvironment ? environment : BattleEnvironment.None;
                IsPrimary = withEnvironment && isPrimary;
                Granted = granted;
            }

            public bool Equals(Key other)
            {
                return ReferenceEquals(Troop, other.Troop)
                    && ReferenceEquals(Perk, other.Perk)
                    && WithEnvironment == other.WithEnvironment
                    && Environment == other.Environment
                    && IsPrimary == other.IsPrimary
                    && Granted == other.Granted;
            }

            public override bool Equals(object obj)
            {
                return obj is Key && Equals((Key)obj);
            }

            // Reference hashes: MBObjectBase hashes by Id, which is fine too, but identity is what Equals compares.
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = RuntimeHelpers.GetHashCode(Troop);
                    hash = hash * 31 + RuntimeHelpers.GetHashCode(Perk);
                    hash = hash * 31 + (int)Environment;
                    hash = hash * 8 + (WithEnvironment ? 4 : 0) + (IsPrimary ? 2 : 0) + (Granted ? 1 : 0);
                    return hash;
                }
            }
        }

        internal sealed class Entry
        {
            /// <summary>How many times this exact question was answered this mission. Interlocked.</summary>
            public int Count;

            /// <summary>The first few meaningful callers of the first occurrence, innermost first.</summary>
            public readonly string[] Callers;

            public Entry(string[] callers)
            {
                Callers = callers;
            }
        }

        /// <summary>How many stack frames above the perk check name the caller.</summary>
        private const int CallerFrames = 3;

        private static readonly ConcurrentDictionary<Key, Entry> _entries = new ConcurrentDictionary<Key, Entry>();

        // Cached so the first-time path allocates no closure; the key itself is not needed to build an entry.
        private static readonly Func<Key, Entry> _newEntry = key => new Entry(CaptureCallers());

        /// <summary>
        /// Notes one answer. Called by the postfixes only while <see cref="Recording"/>; safe from any thread.
        /// </summary>
        public static void Record(CharacterObject troop, PerkObject perk, bool withEnvironment, BattleEnvironment environment,
            bool isPrimary, bool granted)
        {
            Key key = new Key(troop, perk, withEnvironment, environment, isPrimary, granted);
            Entry entry;
            if (!_entries.TryGetValue(key, out entry))
            {
                // Two threads can both get here for a new key; GetOrAdd keeps one entry and the other walk is wasted.
                entry = _entries.GetOrAdd(key, _newEntry);
            }
            Interlocked.Increment(ref entry.Count);
        }

        /// <summary>Starts a fresh mission's record.</summary>
        public static void Begin()
        {
            _entries.Clear();
            Recording = true;
        }

        /// <summary>
        /// Stops recording and drops everything. Called at mission end after the file is written, and unconditionally
        /// before every mission's behaviors are added (RBM SubModule.OnBeforeMissionBehaviorInitialize), so a mission
        /// that ended without OnEndMission cannot leave the recorder running into the next one.
        /// </summary>
        public static void Reset()
        {
            Recording = false;
            _entries.Clear();
        }

        /// <summary>Everything recorded so far, as a stable copy (the dictionary may still be written from other threads).</summary>
        internal static KeyValuePair<Key, Entry>[] Snapshot()
        {
            return _entries.ToArray();
        }

        /// <summary>
        /// The first <see cref="CallerFrames"/> methods above the perk check that say who asked: the recorder, the
        /// patch, CharacterObject.GetPerkValue and its Harmony replacement, the PerkHelper wrappers and framework
        /// frames are skipped. Managed reflection only; no file or line info.
        /// </summary>
        private static string[] CaptureCallers()
        {
            List<string> callers = new List<string>(CallerFrames);
            try
            {
                StackFrame[] frames = new StackTrace(1, false).GetFrames();
                if (frames != null)
                {
                    foreach (StackFrame frame in frames)
                    {
                        MethodBase method = frame.GetMethod();
                        if (method == null || IsSkipped(method))
                        {
                            continue;
                        }
                        callers.Add(TypeName(method.DeclaringType) + "." + method.Name);
                        if (callers.Count >= CallerFrames)
                        {
                            break;
                        }
                    }
                }
            }
            catch
            {
                // A diagnostic must never break the perk check it is watching.
            }
            return callers.ToArray();
        }

        private static bool IsSkipped(MethodBase method)
        {
            // Harmony's replacement of a patched method is a dynamic method, often with no declaring type; its name
            // carries the original's (e.g. "DMD<...CharacterObject::GetPerkValue>"), as does CharacterObject's own.
            if (method.Name.IndexOf("GetPerkValue", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            Type type = method.DeclaringType;
            if (type == null)
            {
                return true;
            }
            if (type == typeof(TroopPerkLog) || IsNestedIn(type, typeof(TroopPerkLog)) || IsNestedIn(type, typeof(TroopPerkEffects)))
            {
                return true;
            }
            if (type.Name == "PerkHelper")
            {
                return true;
            }
            string ns = type.Namespace ?? "";
            return ns.StartsWith("System", StringComparison.Ordinal)
                || ns.StartsWith("HarmonyLib", StringComparison.Ordinal)
                || ns.StartsWith("MonoMod", StringComparison.Ordinal);
        }

        private static bool IsNestedIn(Type type, Type outer)
        {
            for (Type t = type.DeclaringType; t != null; t = t.DeclaringType)
            {
                if (t == outer)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The type name with its enclosing types (Outer.Inner), without the namespace.</summary>
        private static string TypeName(Type type)
        {
            return type.DeclaringType != null ? TypeName(type.DeclaringType) + "." + type.Name : type.Name;
        }

        // ---- the file ------------------------------------------------------------------------------------------

        /// <summary>How many troop perk logs the folder keeps. The oldest are deleted as a new one is written.</summary>
        private const int MaxFiles = 10;

        private const string FilePrefix = "rbm_troopperks_";

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "troopperks"); }
        }

        /// <summary>
        /// Writes one mission's log in a single go (it is a few hundred lines at most, so nothing is buffered).
        /// Main thread, at mission end. Every failure is swallowed: a log is never a reason to break a mission.
        /// </summary>
        internal static void WriteFile(string sceneName, string text)
        {
            try
            {
                Directory.CreateDirectory(LogFolderPath);
                PruneOldest();
                string path = Path.Combine(LogFolderPath,
                    FilePrefix + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + SafeFileName(sceneName) + ".log");
                File.WriteAllText(path, text);
            }
            catch
            {
            }
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "unknown";
            }
            char[] chars = name.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }
            return new string(chars);
        }

        /// <summary>Keeps the folder at <see cref="MaxFiles"/>, counting the file about to be written. Failures swallowed.</summary>
        private static void PruneOldest()
        {
            try
            {
                string[] paths = Directory.GetFiles(LogFolderPath, FilePrefix + "*.log");
                if (paths.Length < MaxFiles)
                {
                    return;
                }
                Array.Sort(paths, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
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
    }
}
