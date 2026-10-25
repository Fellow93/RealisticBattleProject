using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The defender's choke hold for RBMTacticHoldChoke / RBMBehaviorHoldChoke: the narrow passage its infantry holds, with
    /// archer and cavalry reserve places. Scored on the terrain read's background task (BattleTerrainRead.ChokeHold.cs) and
    /// published for the DEFENDER side only (terrainTacticsEnabled). The hold point must be on the navmesh and inside the
    /// boundary, else no plan; an archer or reserve place failing the check is dropped.
    /// THREADING: Build runs on the MAIN thread (WorldPositions created and navmesh-checked there), then Publish (volatile
    /// writes). The tactic and behavior read on the async AI thread: volatile reads of an immutable plan, copies of cached
    /// WorldPositions, and a queue for BEHAVIOR events drained on the main thread. Cleared at mission end and in MissionStartReset.
    /// </summary>
    internal static class TerrainChokeHold
    {

        private static volatile Plan _defender;

        private static readonly ConcurrentQueue<UseEvent> UseEvents = new ConcurrentQueue<UseEvent>();

        /// <summary>The defender's hold. Immutable. Data = the worker's raw place.</summary>
        internal sealed class Plan
        {
            public readonly BattleTerrain.ChokeHoldPlace Data;
            public readonly WorldPosition HoldWorld;
            public readonly WorldPosition ArcherWorld; // Invalid when HasArcher / HasReserve is false
            public readonly WorldPosition ReserveWorld;
            public readonly bool HasArcher;
            public readonly bool HasReserve;
            public readonly string ArcherDropped; // why dropped at the publish ("outside", "noNavmesh"), else null
            public readonly string ReserveDropped;

            internal Plan(BattleTerrain.ChokeHoldPlace data, WorldPosition holdWorld, WorldPosition archerWorld, bool hasArcher, string archerDropped,
                WorldPosition reserveWorld, bool hasReserve, string reserveDropped)
            {
                Data = data;
                HoldWorld = holdWorld;
                ArcherWorld = archerWorld;
                HasArcher = hasArcher;
                ArcherDropped = archerDropped;
                ReserveWorld = reserveWorld;
                HasReserve = hasReserve;
                ReserveDropped = reserveDropped;
            }

            public Vec2 Hold => Data.Hold;

            public Vec2 Facing => Data.Facing;

            public float HoldWidth => Data.HoldWidth;
        }

        /// <summary>The team's hold: null with terrainTacticsEnabled off, before the publish, for an attacker, without a plan, or once BYPASSED (MarkBypassed). Any thread.</summary>
        public static Plan HoldFor(Team team)
        {
            // None for a defender without terrain plans (ArmyScale.NoPlans).
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || team == null || team.Side != BattleSideEnum.Defender || ArmyScale.NoPlans(team))
            {
                return null;
            }
            Plan plan = _defender;
            return (plan != null && plan == _bypassed) ? null : plan;
        }

        private static volatile Plan _bypassed;

        /// <summary>The enemy's concentration went round it. Any thread.</summary>
        public static bool IsBypassed
        {
            get
            {
                Plan plan = _defender;
                return plan != null && plan == _bypassed;
            }
        }

        /// <summary>AI thread (CheckBypass): latches this plan as bypassed (a new publish is a new hold). False when it already was.</summary>
        internal static bool MarkBypassed(Team team, Plan plan, float time, string why)
        {
            if (plan == null || plan != _defender || plan == _bypassed)
            {
                return false;
            }
            _bypassed = plan;
            Enqueue(team, null, time, "bypassed", "hold", why);
            return true;
        }

        /// <summary>Whatever the toggle (log and map).</summary>
        internal static Plan Published => _defender;

        /// <summary>Main thread, at the terrain publish.</summary>
        internal static void Publish(Plan defender)
        {
            _defender = defender;
        }

        /// <summary>Main thread: mission end (BattleTerrainLogic) and MissionStartReset. Also drops the queued events.</summary>
        public static void Clear()
        {
            _defender = null;
            _bypassed = null;
            UseEvent ignored;
            while (UseEvents.TryDequeue(out ignored))
            {
            }
        }

        // build (main thread)

        /// <summary>MAIN thread, at the terrain publish: the defender's plan from the snapshot, or null (why says why).</summary>
        internal static Plan Build(Mission mission, BattleTerrain terrain, out string why)
        {
            why = null;
            if (mission == null || mission.Scene == null || terrain == null)
            {
                why = "noTerrain";
                return null;
            }
            BattleTerrain.ChokeHoldPlace data = terrain.ChokeHold.Defender;
            if (data == null)
            {
                why = "noChoke";
                return null;
            }
            string holdWhy;
            WorldPosition hold = TerrainBattlePosition.MakeWorld(mission, terrain, data.Hold, out holdWhy);
            if (holdWhy != null)
            {
                why = "hold:" + holdWhy;
                return null;
            }
            string archerWhy = null;
            WorldPosition archer = WorldPosition.Invalid;
            if (data.Archer.IsValid)
            {
                archer = TerrainBattlePosition.MakeWorld(mission, terrain, data.Archer, out archerWhy);
            }
            string reserveWhy = null;
            WorldPosition reserve = WorldPosition.Invalid;
            if (data.Reserve.IsValid)
            {
                reserve = TerrainBattlePosition.MakeWorld(mission, terrain, data.Reserve, out reserveWhy);
            }
            return new Plan(data, hold, archer, data.Archer.IsValid && archerWhy == null, archerWhy,
                reserve, data.Reserve.IsValid && reserveWhy == null, reserveWhy);
        }

        // behavior / tactic events (AI thread writes, main thread drains)

        /// <summary>HoldChoke state change or the tactic's engage. Immutable.</summary>
        internal sealed class UseEvent
        {
            public Team Team;
            public int FormationIndex; // -1 for a tactic-level event
            public float Time;
            public string What;
            public string Point;
            public string Why;
        }

        /// <summary>AI thread; only with the terrain log on. formation null = the tactic's own event.</summary>
        internal static void Enqueue(Team team, Formation formation, float time, string what, string point, string why)
        {
            DecisionTrace.Note(formation, "HoldChoke", what, why);
            if (!RBMConfig.RBMConfig.battleTerrainLoggingEnabled || team == null)
            {
                return;
            }
            UseEvents.Enqueue(new UseEvent
            {
                Team = team,
                FormationIndex = (formation != null) ? (int)formation.FormationIndex : -1,
                Time = time,
                What = what,
                Point = point,
                Why = why
            });
        }

        /// <summary>Main thread (terrain log).</summary>
        internal static bool TryDequeueUse(out UseEvent e)
        {
            return UseEvents.TryDequeue(out e);
        }

        // log (main thread)

        /// <summary>The CHOKEHOLD line's fields after the side: applied, the choke and its places, or none and why.</summary>
        internal static string Describe(BattleTerrain.ChokeHoldInfo info, Plan plan, string buildWhy)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("applied=").Append(RBMConfig.RBMConfig.terrainTacticsEnabled ? 1 : 0);
            if (info == null)
            {
                sb.Append("\tchoke=-\twhy=notBuilt");
                return sb.ToString();
            }
            sb.Append("\tinfantry=").Append(info.Infantry.ToString(CultureInfo.InvariantCulture))
              .Append(" front=").Append(Fmt0(info.InfantryFrontage)).Append('m')
              .Append(" outnumbered=").Append(Fmt2(info.Outnumbered)).Append(" k=").Append(Fmt2(info.OutnumberedK))
              .Append('(').Append(info.OutnumberedSource ?? "-").Append(')')
              .Append(" maxDist=").Append(Fmt0(info.MaxDistance)).Append(" reach=").Append(Fmt0(info.ReachMargin)).Append('s')
              .Append(" cavReach=").Append(info.CavalryCheck ? 1 : 0);
            BattleTerrain.ChokeHoldPlace data = info.Defender;
            if (data == null)
            {
                sb.Append("\tchoke=-");
            }
            else
            {
                sb.Append("\tchoke=").Append(data.ChokeId)
                  .Append(" line=").Append(Fmt0(data.LineWidth)).Append("m(feature ").Append(Fmt0(data.FeatureWidth)).Append("m)")
                  .Append(" vsFront=").Append(Fmt2(data.LineWidth / Math.Max(1f, data.InfantryFrontage)))
                  .Append(" vsAtkFront=").Append(Fmt2(data.LineWidth / Math.Max(1f, data.AttackerFrontage)))
                  .Append(" at=").Append(Fmt0(data.Choke.x)).Append(',').Append(Fmt0(data.Choke.y));
                sb.Append("\thold=").Append(Fmt0(data.Hold.x)).Append(',').Append(Fmt0(data.Hold.y))
                  .Append(" back=").Append(Fmt0(data.HoldBack)).Append('m')
                  .Append(" span=").Append(Fmt0(data.HoldSpan)).Append("m(").Append(EndName(data.HoldLeftEnd)).Append('/').Append(EndName(data.HoldRightEnd)).Append(')')
                  .Append(" width=").Append(Fmt0(data.HoldWidth)).Append('m')
                  .Append(" flanksCovered=").Append(data.BothFlanksCovered ? 1 : 0)
                  .Append(" mode=").Append(data.AtMouth ? "mouth" : "gap")
                  .Append(" crossing=").Append(data.Crossing ? 1 : 0)
                  .Append(" facing=").Append(Fmt2(data.Facing.x)).Append(',').Append(Fmt2(data.Facing.y))
                  .Append(" dDef=").Append(Fmt0(data.DefenderDistance)).Append('m');
                sb.Append("\treach=def ").Append(Fmt0(data.DefenderSeconds)).Append("s atk ").Append(Fmt0(data.AttackerSeconds)).Append('s');
                sb.Append("\tarcher=");
                if (!data.Archer.IsValid)
                {
                    sb.Append('-');
                }
                else
                {
                    sb.Append(data.ArcherSource).Append(' ').Append(Fmt0(data.Archer.x)).Append(',').Append(Fmt0(data.Archer.y))
                      .Append(" gain=").Append(Fmt1(data.ArcherGain)).Append('m');
                    if (plan != null && !plan.HasArcher)
                    {
                        sb.Append(" dropped=").Append(plan.ArcherDropped ?? "-");
                    }
                }
                sb.Append("\treserve=");
                if (!data.Reserve.IsValid)
                {
                    sb.Append('-');
                }
                else
                {
                    sb.Append(Fmt0(data.Reserve.x)).Append(',').Append(Fmt0(data.Reserve.y));
                    if (plan != null && !plan.HasReserve)
                    {
                        sb.Append(" dropped=").Append(plan.ReserveDropped ?? "-");
                    }
                }
                sb.Append("\tscore=").Append(Fmt2(data.Score));
            }
            sb.Append("\tplan=").Append((plan != null) ? "published" : "none(" + (buildWhy ?? "-") + ")");
            sb.Append("\twhy=").Append(string.IsNullOrEmpty(info.Why) ? "-" : info.Why);
            sb.Append("\tpaths=").Append(info.PathRuns.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tms=").Append(Fmt1(info.Ms));
            if (info.Error != null)
            {
                sb.Append("\terror=").Append(info.Error);
            }
            return sb.ToString();
        }

        private static string EndName(BattleTerrain.End end)
        {
            return end.ToString().ToLowerInvariant();
        }

        internal static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Fmt1(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string Fmt2(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
