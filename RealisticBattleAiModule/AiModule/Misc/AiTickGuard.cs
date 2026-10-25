using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Crash guard of the AI ticks. Team and formation AI run on the engine's async AI thread and native catches nothing there, so one
    /// exception from any RBM behavior, tactic, prefix or postfix ends the game. Finalizers on the native dispatch points
    /// (TeamAIComponent.MakeDecision / TickOccasionally / Tick, FormationAI.TickOccasionally, Formation.Tick) swallow and count it: the rest
    /// of THAT tick of THAT team / formation is skipped, everything else ticks as usual. OutOfMemoryException is not swallowed.
    ///
    /// Never silent: each distinct failure (site, exception type, throwing method) is counted (Interlocked) and written at its 1st, 10th,
    /// 100th, 1000th and every 10000th occurrence (the first with its stack) to the rgl log and, while open, the AI behavior log.
    /// Lines are queued on any thread and written on the main thread (<see cref="Flush"/>); <see cref="Clear"/> (MissionStartReset) writes
    /// the last mission's totals and starts over.
    /// </summary>
    public static class AiTickGuard
    {
        private const int MaxKeys = 64; // more are counted under "site|other"

        // Queued lines beyond this are dropped (counted).
        private const int MaxQueuedLines = 256;

        private sealed class Site
        {
            public long Count;
        }

        private static readonly ConcurrentDictionary<string, Site> Sites = new ConcurrentDictionary<string, Site>();

        private static readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();

        private static int _queued;

        private static int _droppedLines;

        // Any thread; never throws.
        public static void Report(string site, Exception e, string context)
        {
            try
            {
                string where = Where(e);
                string key = site + "|" + e.GetType().Name + "|" + where;
                Site s;
                if (!Sites.TryGetValue(key, out s))
                {
                    if (Sites.Count >= MaxKeys)
                    {
                        key = site + "|other";
                    }
                    s = Sites.GetOrAdd(key, k => new Site());
                }
                long n = Interlocked.Increment(ref s.Count);
                if (!Logged(n))
                {
                    return;
                }
                Mission mission = Mission.Current;
                string line = "[RBM AI] GUARD " + site + " threw " + e.GetType().Name + ": " + OneLine(e.Message)
                    + " at " + where
                    + ((context != null) ? " (" + context + ")" : "")
                    + " t=" + ((mission != null) ? mission.CurrentTime.ToString("0.0", CultureInfo.InvariantCulture) : "-")
                    + " count=" + n.ToString(CultureInfo.InvariantCulture)
                    + ((n == 1) ? " stack: " + OneLine(e.StackTrace) : "");
                if (Interlocked.Increment(ref _queued) > MaxQueuedLines)
                {
                    Interlocked.Decrement(ref _queued);
                    Interlocked.Increment(ref _droppedLines);
                    return;
                }
                Lines.Enqueue(line);
            }
            catch
            {
                // The guard itself never throws into the tick it guards.
            }
        }

        private static bool Logged(long n)
        {
            return n == 1 || n == 10 || n == 100 || n == 1000 || n % 10000 == 0;
        }

        private static string Where(Exception e)
        {
            try
            {
                System.Reflection.MethodBase site = e.TargetSite;
                if (site == null)
                {
                    return "-";
                }
                return ((site.DeclaringType != null) ? site.DeclaringType.Name + "." : "") + site.Name;
            }
            catch
            {
                return "-";
            }
        }

        private static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "-";
            }
            return text.Replace("\r", "").Replace("\n", " <- ").Replace('\t', ' ');
        }

        // Main thread only.
        public static void Flush()
        {
            if (Volatile.Read(ref _queued) == 0 && Volatile.Read(ref _droppedLines) == 0)
            {
                return;
            }
            string line;
            while (Lines.TryDequeue(out line))
            {
                Interlocked.Decrement(ref _queued);
                Write(line);
            }
            int dropped = Interlocked.Exchange(ref _droppedLines, 0);
            if (dropped > 0)
            {
                Write("[RBM AI] GUARD " + dropped.ToString(CultureInfo.InvariantCulture) + " guard lines dropped (queue full)");
            }
        }

        private static void Write(string line)
        {
            try
            {
                TaleWorlds.Library.Debug.Print(line);
            }
            catch
            {
            }
            try
            {
                AiBehaviorLog.Write("# " + line);
            }
            catch
            {
            }
        }

        // Main thread (MissionStartReset).
        internal static void Clear()
        {
            Flush();
            if (!Sites.IsEmpty)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder("[RBM AI] GUARD last mission totals:");
                foreach (System.Collections.Generic.KeyValuePair<string, Site> entry in Sites)
                {
                    sb.Append(' ').Append(entry.Key).Append(" x").Append(Interlocked.Read(ref entry.Value.Count).ToString(CultureInfo.InvariantCulture)).Append(';');
                }
                Write(sb.ToString());
            }
            Sites.Clear();
        }

        private static Exception Swallow(string site, Exception exception, string context)
        {
            if (exception == null || exception is OutOfMemoryException)
            {
                return exception;
            }
            Report(site, exception, context);
            return null;
        }

        private static string Describe(Formation formation)
        {
            try
            {
                if (formation == null)
                {
                    return "-";
                }
                Team team = formation.Team;
                BehaviorComponent behavior = (formation.AI != null) ? formation.AI.ActiveBehavior : null;
                return ((team != null) ? team.Side + "#" + team.TeamIndex.ToString(CultureInfo.InvariantCulture) : "-")
                    + " f" + ((int)formation.FormationIndex).ToString(CultureInfo.InvariantCulture)
                    + " ai=" + (formation.IsAIControlled ? 1 : 0)
                    + " beh=" + ((behavior != null) ? behavior.GetType().Name : "-");
            }
            catch
            {
                return "?";
            }
        }

        private static string Describe(TeamAIComponent ai)
        {
            try
            {
                if (ai == null)
                {
                    return "-";
                }
                string team = "-";
                Mission mission = Mission.Current;
                if (mission != null)
                {
                    foreach (Team t in mission.Teams)
                    {
                        if (t != null && t.TeamAI == ai)
                        {
                            team = t.Side + "#" + t.TeamIndex.ToString(CultureInfo.InvariantCulture);
                            break;
                        }
                    }
                }
                string tactic;
                try
                {
                    TacticComponent current = TacticSelection.CurrentTacticRef(ai);
                    tactic = (current != null) ? current.GetType().Name : "-";
                }
                catch
                {
                    tactic = "?";
                }
                return team + " " + ai.GetType().Name + " tactic=" + tactic;
            }
            catch
            {
                return "?";
            }
        }

        [HarmonyPatch(typeof(TeamAIComponent), "MakeDecision")]
        internal static class MakeDecisionGuard
        {
            private static Exception Finalizer(Exception __exception, TeamAIComponent __instance)
            {
                return (__exception == null) ? null : Swallow("TeamAIComponent.MakeDecision", __exception, Describe(__instance));
            }
        }

        [HarmonyPatch(typeof(TeamAIComponent), nameof(TeamAIComponent.TickOccasionally))]
        internal static class TeamTickOccasionallyGuard
        {
            private static Exception Finalizer(Exception __exception, TeamAIComponent __instance)
            {
                return (__exception == null) ? null : Swallow("TeamAIComponent.TickOccasionally", __exception, Describe(__instance));
            }
        }

        [HarmonyPatch(typeof(TeamAIComponent), "Tick")]
        internal static class TeamTickGuard
        {
            private static Exception Finalizer(Exception __exception, TeamAIComponent __instance)
            {
                return (__exception == null) ? null : Swallow("TeamAIComponent.Tick", __exception, Describe(__instance));
            }
        }

        [HarmonyPatch(typeof(FormationAI), "TickOccasionally")]
        internal static class FormationAITickGuard
        {
            private static Exception Finalizer(Exception __exception, FormationAI __instance)
            {
                if (__exception == null)
                {
                    return null;
                }
                BehaviorComponent behavior = null;
                try
                {
                    behavior = (__instance != null) ? __instance.ActiveBehavior : null;
                }
                catch
                {
                }
                return Swallow("FormationAI.TickOccasionally", __exception, (behavior != null) ? Describe(behavior.Formation) : "-");
            }
        }

        [HarmonyPatch(typeof(Formation), nameof(Formation.Tick))]
        internal static class FormationTickGuard
        {
            private static Exception Finalizer(Exception __exception, Formation __instance)
            {
                return (__exception == null) ? null : Swallow("Formation.Tick", __exception, Describe(__instance));
            }
        }
    }
}
