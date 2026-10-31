using System.Collections.Concurrent;
using System.Globalization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The one AI-thread queue of terrain-log lines (BEHAVIOR, CHOKEHOLD bypassed, APPROACH use, FORMSHAPE, FLANKUSE): any thread
    /// enqueues, BattleTerrainLogic drains and formats on the main thread. Behavior lines also note the decision trace, with the
    /// log off too. Cleared at mission end and in MissionStartReset (through the plan classes' Clear).
    /// </summary>
    internal static class TerrainLog
    {
        internal enum Tag
        {
            /// <summary>"BEHAVIOR team formation=F Behavior what [pointKey=point] why=..."</summary>
            Behavior,
            /// <summary>"CHOKEHOLD bypassed team why=..."</summary>
            ChokeBypassed,
            /// <summary>"APPROACH use team formation=F what why=..."</summary>
            Approach,
            /// <summary>"FORMSHAPE team formation=F Behavior what why=... values"</summary>
            Shape,
            /// <summary>"FLANKUSE team tactic text reapply=0|1"</summary>
            FlankUse
        }

        /// <summary>Immutable once queued.</summary>
        internal sealed class Event
        {
            public Tag Tag;
            public Team Team;
            public int FormationIndex = -1;
            public float Time;
            public string Behavior;
            public string What;
            /// <summary>"spot" / "point"; null = no point field.</summary>
            public string PointKey;
            public string Point;
            public string Why;
            /// <summary>FORMSHAPE values / FLANKUSE tactic name.</summary>
            public string Extra;
            public bool Reapply;
        }

        private static readonly ConcurrentQueue<Event> Events = new ConcurrentQueue<Event>();

        internal static bool On => RBMConfig.RBMConfig.battleTerrainLoggingEnabled;

        /// <summary>Any thread: a behavior's decision (trace note always, line only with the log on). formation null = a tactic-level event.</summary>
        internal static void Behavior(Team team, Formation formation, float time, string behavior, string what, string pointKey, string point, string why)
        {
            DecisionTrace.Note(formation, behavior, what, why);
            if (!On || team == null)
            {
                return;
            }
            Events.Enqueue(new Event
            {
                Tag = Tag.Behavior,
                Team = team,
                FormationIndex = (formation != null) ? (int)formation.FormationIndex : -1,
                Time = time,
                Behavior = behavior,
                What = what,
                PointKey = pointKey,
                Point = point,
                Why = why
            });
        }

        /// <summary>Any thread; only with the log on (no trace note).</summary>
        internal static void Enqueue(Event e)
        {
            if (On && e != null && e.Team != null)
            {
                Events.Enqueue(e);
            }
        }

        internal static bool TryDequeue(out Event e)
        {
            return Events.TryDequeue(out e);
        }

        internal static void Clear()
        {
            Event ignored;
            while (Events.TryDequeue(out ignored))
            {
            }
        }

        /// <summary>Main thread: the line after "t=T\t" (team = the log's team name).</summary>
        internal static string Format(Event e, string team)
        {
            string formation = (e.FormationIndex >= 0) ? e.FormationIndex.ToString(CultureInfo.InvariantCulture) : "-";
            switch (e.Tag)
            {
                case Tag.ChokeBypassed:
                    return "CHOKEHOLD\tbypassed\t" + team + "\twhy=" + e.Why;
                case Tag.Approach:
                    return "APPROACH\tuse\t" + team + "\tformation=" + formation + "\t" + e.What + "\twhy=" + e.Why;
                case Tag.Shape:
                    return "FORMSHAPE\t" + team + "\tformation=" + formation + "\t" + e.Behavior + "\t" + e.What + "\twhy=" + e.Why + "\t" + e.Extra;
                case Tag.FlankUse:
                    return "FLANKUSE\t" + team + "\t" + e.Extra + "\t" + e.Why + "\treapply=" + (e.Reapply ? 1 : 0);
                default:
                    return "BEHAVIOR\t" + team + "\tformation=" + formation + "\t" + e.Behavior + "\t" + e.What
                        + ((e.PointKey != null) ? "\t" + e.PointKey + "=" + e.Point : "") + "\twhy=" + e.Why;
            }
        }
    }
}
