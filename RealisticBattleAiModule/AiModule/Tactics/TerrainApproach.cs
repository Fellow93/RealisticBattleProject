using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The attacker's approach (terrainTacticsEnabled, none under ArmyScale.NoPlans, AI attacker teams only): the way its main
    /// infantry walks to the defender's line, scored on the worker (BattleTerrainRead.Approach.cs) and published here with a
    /// navmesh-checked WorldPosition per waypoint. A straight plan changes nothing.
    /// Use (AI thread, OverrideBehaviorAdvance.CalculateCurrentOrder, active BehaviorAdvance of the team's TeamMain only): the Move
    /// point is on the leg to the next waypoint at the advance's own reach (the waypoint itself when nearer), facing along the leg
    /// (toward the enemy on the last leg); next waypoint when the front is within WaypointArrive of it or past it along the leg.
    /// The route is dropped (straight from then on) for the battle on enemyNear (a significant enemy within DropGap edge gap),
    /// melee, player, stalled, timeout, arrived; for that plan version on blocked (a significant enemy formation, not horse
    /// archers, within BlockGap of the remaining route), offRoute (joined more than JoinGap from the route) and noGain (a
    /// re-plan found no detour still >= ApproachKeepGain better).
    /// Re-plan (MAIN thread, Tick): every CheckInterval; due ReplanMinSeconds after the last plan when the defender's main moved
    /// > DefenderMove or turned > DefenderTurn, or ReplanPeriod after it when either main moved > TimerMove; <= ReplanMax per
    /// battle; stops once the mains are within DropGap or the attacker's main is in melee. Runs EvaluateApproach on a background
    /// Task (generation token) from the armies now; WorldPositions built at its publish.
    /// THREADING: Build / Publish / Tick / Clear on the MAIN thread. TryLeg on the AI thread: a volatile read of an immutable plan,
    /// cached formation data, per-formation state touched only by that formation's own tick, a copy of a cached WorldPosition with
    /// ONE SetVec2MT (rebuilt past RebuildDistance after RebuildSeconds or on a new waypoint), a queue drained on the main thread.
    /// Cleared at mission end and in MissionStartReset.
    /// </summary>
    internal static class TerrainApproach
    {
        public const float WaypointArrive = 25f;

        public const float DropGap = 150f;

        public const float BlockGap = 60f;

        public const float MeleeShare = 0.1f;

        public const float JoinGap = 120f; // m from the main's centre to the route

        public const float StallSeconds = 15f; // moved less than StallMove in this long while following: dropped

        public const float StallMove = 5f;

        public const float CallGapReset = 3f; // s without a call (another branch ran): the stall clock restarts

        public const float MaxFollowSeconds = 300f;

        public const float RebuildDistance = 5f;

        public const float RebuildSeconds = 2f;

        public const float CheckInterval = 1f;

        public const float ReplanMinSeconds = 20f;

        public const float ReplanPeriod = 30f;

        public const float DefenderMove = 60f;

        public const float DefenderTurn = 20f;

        public const float TimerMove = 30f;

        public const int ReplanMax = 8;

        public const float PositionLeftDistance = 100f; // the defender's main this far from its battle position's hold: its own line is scored

        public const float ChokeHeldDistance = 150f; // the defender's main within this of the choke hold: the choke counts as held

        private const int MaxShooters = 4;

        private static volatile SidePlan _attacker;

        private static int _generation;
        private static Task<BattleTerrain.ApproachResult> _replan;
        private static int _replanGeneration;
        private static string _replanReason;
        private static long _replanStartTicks;
        private static float _nextCheckAt;
        private static float _lastAttemptAt = float.NegativeInfinity;
        private static int _replans;
        private static bool _stopped;
        private static bool _capLogged;

        internal sealed class Waypoint
        {
            public readonly Vec2 Position;
            public readonly WorldPosition World;

            internal Waypoint(Vec2 position, WorldPosition world)
            {
                Position = position;
                World = world;
            }
        }

        /// <summary>The attacker's approach. Immutable.</summary>
        internal sealed class SidePlan
        {
            public readonly int Version; // 1 at the publish, + 1 per re-plan
            public readonly string Note; // publish | replan
            public readonly BattleTerrain.ApproachResult Result;
            public readonly BattleTerrain.ApproachKind Kind;
            public readonly IReadOnlyList<Waypoint> Waypoints; // empty for straight
            public readonly Vec2 From;
            public readonly string Dropped; // waypoints off the navmesh / outside ("2:noNavmesh"), or null
            public readonly string Why; // why straight, else null
            public readonly float PlannedAt;
            public readonly Vec2 AttackerRef; // both mains' centres and the defender main's facing when planned
            public readonly Vec2 DefenderRef;
            public readonly Vec2 DefenderFacingRef;

            internal SidePlan(int version, string note, BattleTerrain.ApproachResult result, BattleTerrain.ApproachKind kind, IReadOnlyList<Waypoint> waypoints,
                Vec2 from, string dropped, string why, float plannedAt, Vec2 attackerRef, Vec2 defenderRef, Vec2 defenderFacingRef)
            {
                Version = version;
                Note = note;
                Result = result;
                Kind = kind;
                Waypoints = waypoints;
                From = from;
                Dropped = dropped;
                Why = why;
                PlannedAt = plannedAt;
                AttackerRef = attackerRef;
                DefenderRef = defenderRef;
                DefenderFacingRef = defenderFacingRef;
            }

            public bool IsRoute => Kind != BattleTerrain.ApproachKind.Straight && Waypoints.Count > 0;

            public string KindName => BattleTerrain.ApproachKindName(Kind);
        }

        /// <summary>The attacker's plan as the advance reads it: null with terrainTacticsEnabled off, before the publish, under ArmyScale.NoPlans, or for the defender. Any thread.</summary>
        public static SidePlan ForSide(BattleSideEnum side)
        {
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || side != BattleSideEnum.Attacker || ArmyScale.NoPlans(side))
            {
                return null;
            }
            return _attacker;
        }

        public static SidePlan ForTeam(Team team)
        {
            return (team != null) ? ForSide(team.Side) : null;
        }

        /// <summary>Whatever the toggle (log and map).</summary>
        internal static SidePlan Published => _attacker;

        /// <summary>Main thread, at the terrain publish (and a re-plan's finish).</summary>
        internal static void Publish(SidePlan attacker)
        {
            _attacker = attacker;
        }

        /// <summary>Main thread: mission end and MissionStartReset. Drops the use states, queued events and the re-plan state (a running re-plan is abandoned by generation).</summary>
        public static void Clear()
        {
            _attacker = null;
            States.Clear();
            UseEvent ignored;
            while (UseEvents.TryDequeue(out ignored))
            {
            }
            _generation++;
            if (_replan != null)
            {
                _replan.ContinueWith(t => { var unused = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                _replan = null;
            }
            _replanReason = null;
            _nextCheckAt = 0f;
            _lastAttemptAt = float.NegativeInfinity;
            _replans = 0;
            _stopped = false;
            _capLogged = false;
        }

        // build (main thread)

        /// <summary>MAIN thread: the plan from an evaluation; each waypoint gets a navmesh-checked WorldPosition. The route falls back to straight when its last waypoint or two others fail.</summary>
        internal static SidePlan Build(Mission mission, BattleTerrain terrain, BattleTerrain.ApproachResult result, int version, string note)
        {
            float now = (mission != null) ? mission.CurrentTime : 0f;
            BattleTerrain.ApproachCandidate chosen = (result != null) ? result.Chosen : null;
            BattleTerrain.ApproachKind kind = (chosen != null) ? chosen.Kind : BattleTerrain.ApproachKind.Straight;
            string why = (result == null) ? "noResult" : (chosen == null ? (result.Why ?? "noRoute") : result.Why);
            List<Waypoint> waypoints = new List<Waypoint>();
            StringBuilder dropped = null;
            if (kind != BattleTerrain.ApproachKind.Straight && mission != null && mission.Scene != null && terrain != null)
            {
                int failed = 0;
                bool lastFailed = false;
                for (int i = 0; i < chosen.Points.Count; i++)
                {
                    string fail;
                    WorldPosition world = TerrainBattlePosition.MakeWorld(mission, terrain, chosen.Points[i], out fail);
                    if (fail != null)
                    {
                        failed++;
                        lastFailed |= i == chosen.Points.Count - 1;
                        dropped = (dropped == null) ? new StringBuilder() : dropped.Append(' ');
                        dropped.Append(i.ToString(CultureInfo.InvariantCulture)).Append(':').Append(fail);
                        continue;
                    }
                    waypoints.Add(new Waypoint(chosen.Points[i], world));
                }
                if (lastFailed || failed >= 2 || waypoints.Count == 0)
                {
                    waypoints.Clear();
                    why = "noNavmesh(" + (dropped != null ? dropped.ToString() : "-") + ")";
                    kind = BattleTerrain.ApproachKind.Straight;
                }
            }
            else
            {
                kind = BattleTerrain.ApproachKind.Straight;
            }
            Vec2 attackerRef = Vec2.Invalid;
            Vec2 defenderRef = Vec2.Invalid;
            Vec2 defenderFacing = Vec2.Invalid;
            if (mission != null)
            {
                Formation atk = TerrainFlankPlan.MainFormation(mission, BattleSideEnum.Attacker, true);
                Formation def = TerrainFlankPlan.MainFormation(mission, BattleSideEnum.Defender, true);
                attackerRef = (atk != null) ? RBMAI.Utilities.GetFormationCenter(atk) : Vec2.Invalid;
                defenderRef = (def != null) ? RBMAI.Utilities.GetFormationCenter(def) : Vec2.Invalid;
                defenderFacing = Facing(def);
            }
            return new SidePlan(version, note, result, kind, Array.AsReadOnly(waypoints.ToArray()), (result != null) ? result.From : Vec2.Invalid,
                dropped?.ToString(), (kind == BattleTerrain.ApproachKind.Straight) ? (why ?? "noDetour") : null, now, attackerRef, defenderRef, defenderFacing);
        }

        private static Vec2 Facing(Formation formation)
        {
            if (formation == null)
            {
                return Vec2.Invalid;
            }
            Vec2 facing = formation.Direction;
            if (!facing.IsValid || facing.Normalize() < 0.001f)
            {
                return Vec2.Invalid;
            }
            return facing;
        }

        // re-plan (main thread)

        /// <summary>MAIN thread, every tick after the publish: finishes a running re-plan or starts one when due. Adds log line fields to lines; runs log on or off.</summary>
        internal static void Tick(Mission mission, BattleTerrain terrain, float now, List<string> lines)
        {
            if (mission == null || terrain == null)
            {
                return;
            }
            if (_replan != null)
            {
                FinishReplan(mission, terrain, lines);
                return;
            }
            SidePlan plan = _attacker;
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || plan == null || ArmyScale.NoPlans(BattleSideEnum.Attacker) || _stopped || now < _nextCheckAt)
            {
                return;
            }
            _nextCheckAt = now + CheckInterval;
            Formation atk = TerrainFlankPlan.MainFormation(mission, BattleSideEnum.Attacker, true);
            Formation def = TerrainFlankPlan.MainFormation(mission, BattleSideEnum.Defender, true);
            if (atk == null || def == null)
            {
                return;
            }
            float gap = FormationGeometry.Gap(atk, FootprintMode.Current, def, FootprintMode.Current);
            float melee = HoldLateCharge.MeleeShare(atk);
            if (gap < DropGap || melee >= MeleeShare)
            {
                _stopped = true;
                lines.Add("APPROACH\treplanStop\t" + BattleSideEnum.Attacker + "\t" + ((gap < DropGap) ? "close gap=" + Fmt0(gap) + "m<" + Fmt0(DropGap) + "m" : "melee " + Fmt2(melee))
                    + "\treplans=" + _replans.ToString(CultureInfo.InvariantCulture) + "/" + ReplanMax.ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (_replans >= ReplanMax)
            {
                if (!_capLogged)
                {
                    _capLogged = true;
                    lines.Add("APPROACH\treplanStop\t" + BattleSideEnum.Attacker + "\tcap " + ReplanMax.ToString(CultureInfo.InvariantCulture));
                }
                return;
            }
            Vec2 atkPos = RBMAI.Utilities.GetFormationCenter(atk);
            Vec2 defPos = RBMAI.Utilities.GetFormationCenter(def);
            if (!atkPos.IsValid || !defPos.IsValid)
            {
                return;
            }
            float since = now - Math.Max(plan.PlannedAt, _lastAttemptAt);
            float defMoved = plan.DefenderRef.IsValid ? defPos.Distance(plan.DefenderRef) : 0f;
            float atkMoved = plan.AttackerRef.IsValid ? atkPos.Distance(plan.AttackerRef) : 0f;
            float turned = AngleDegrees(Facing(def), plan.DefenderFacingRef);
            string why;
            if (since >= ReplanMinSeconds && (defMoved > DefenderMove || turned > DefenderTurn))
            {
                why = "defender";
            }
            else if (since >= ReplanPeriod && (defMoved > TimerMove || atkMoved > TimerMove))
            {
                why = "timer";
            }
            else
            {
                return;
            }
            BattleTerrainRead.ApproachArmies armies = Gather(mission, terrain, atk, def, plan);
            if (armies == null)
            {
                return;
            }
            _replans++;
            _lastAttemptAt = now;
            _replanGeneration = _generation;
            _replanStartTicks = DateTime.UtcNow.Ticks;
            _replanReason = why + " def moved " + Fmt0(defMoved) + "m turned " + Fmt0(turned) + "deg atk moved " + Fmt0(atkMoved) + "m gap " + Fmt0(gap)
                + "m after " + Fmt0(since) + "s n=" + _replans.ToString(CultureInfo.InvariantCulture) + "/" + ReplanMax.ToString(CultureInfo.InvariantCulture)
                + (armies.KeepKind.HasValue ? " keep=" + BattleTerrain.ApproachKindName(armies.KeepKind.Value) : "");
            // background: pure grid work over the immutable snapshot
            _replan = Task.Run(() => terrain.ReplanApproach(armies));
        }

        private static void FinishReplan(Mission mission, BattleTerrain terrain, List<string> lines)
        {
            Task<BattleTerrain.ApproachResult> task = _replan;
            if (!task.IsCompleted)
            {
                return;
            }
            _replan = null;
            if (_replanGeneration != _generation)
            {
                return;
            }
            string ms = ((DateTime.UtcNow.Ticks - _replanStartTicks) / (double)TimeSpan.TicksPerMillisecond).ToString("0", CultureInfo.InvariantCulture);
            if (task.IsFaulted || task.IsCanceled)
            {
                Exception e = (task.Exception != null) ? task.Exception.GetBaseException() : new OperationCanceledException("approach re-plan canceled");
                lines.Add("APPROACH\treplan\t" + BattleSideEnum.Attacker + "\tfailed=" + e.GetType().Name + ":" + BattleTerrainRead.Sanitize(e.Message) + "\treason=" + _replanReason + "\twallMs=" + ms);
                return;
            }
            BattleTerrain.ApproachResult result = task.Result;
            if (result == null || mission.Scene == null)
            {
                return;
            }
            SidePlan old = _attacker;
            SidePlan plan = Build(mission, terrain, result, (old != null) ? old.Version + 1 : 1, "replan");
            _attacker = plan;
            lines.Add("APPROACH\treplan\t" + BattleSideEnum.Attacker + "\treason=" + _replanReason + "\twallMs=" + ms + "\t" + Describe(result, plan));
        }

        /// <summary>Main thread: the armies now. The defender's battle position while its main is within PositionLeftDistance of the hold, else its main's own line; the choke only while held.</summary>
        private static BattleTerrainRead.ApproachArmies Gather(Mission mission, BattleTerrain terrain, Formation atk, Formation def, SidePlan plan)
        {
            Vec2 attacker = RBMAI.Utilities.GetFormationCenter(atk);
            Vec2 defCentre = RBMAI.Utilities.GetFormationCenter(def);
            if (!attacker.IsValid || !defCentre.IsValid)
            {
                return null;
            }
            BattleTerrainRead.ApproachArmies a = new BattleTerrainRead.ApproachArmies
            {
                Attacker = attacker,
                AttackerFrontage = Math.Max(20f, atk.Width),
                Pace = BattleTerrainRead.ApproachPace((terrain.Attacker != null) ? terrain.Attacker.FootSpeed : float.NaN),
                RangedShare = (terrain.Defender != null && terrain.Defender.Men > 0) ? (float)terrain.Defender.Ranged / terrain.Defender.Men : 0f
            };
            List<Vec2> shooters = new List<Vec2>();
            TerrainBattlePosition.Plan position = TerrainBattlePosition.Published;
            BattleTerrain.PositionCandidate data = (position != null) ? position.Data : null;
            if (data != null && data.Hold.IsValid && data.Facing.IsValid && data.LineDirection.IsValid && defCentre.Distance(data.Hold) <= PositionLeftDistance)
            {
                a.Line = data.Hold;
                a.Facing = data.Facing;
                a.LineDirection = data.LineDirection;
                a.HalfWidth = Math.Max(10f, float.IsNaN(data.LineWidth) ? def.Width * 0.5f : data.LineWidth * 0.5f);
                a.LeftCovered = data.LeftCovered;
                a.RightCovered = data.RightCovered;
                a.LineSource = "position v" + position.Version.ToString(CultureInfo.InvariantCulture);
                if (position.HasArcher && data.Archer.IsValid)
                {
                    shooters.Add(data.Archer);
                }
            }
            else
            {
                Vec2 toAttacker = attacker - defCentre;
                if (toAttacker.Normalize() < 1f)
                {
                    return null;
                }
                Vec2 facing = Facing(def);
                if (!facing.IsValid || Vec2.DotProduct(facing, toAttacker) < 0f)
                {
                    facing = toAttacker;
                }
                a.Line = defCentre + facing * (def.Depth * 0.5f);
                a.Facing = facing;
                a.LineDirection = new Vec2(-facing.y, facing.x);
                a.HalfWidth = Math.Max(10f, def.Width * 0.5f);
                TerrainFlankPlan.SidePlan flanks = TerrainFlankPlan.Published(BattleSideEnum.Defender);
                if (flanks != null)
                {
                    a.LeftCovered = flanks.OwnLeftCovered;
                    a.RightCovered = flanks.OwnRightCovered;
                }
                a.LineSource = "army";
            }
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != BattleSideEnum.Defender)
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (shooters.Count >= MaxShooters)
                    {
                        break;
                    }
                    if (f == null || f.CountOfUnits < 10 || f.QuerySystem.IsCavalryFormation || f.QuerySystem.IsRangedCavalryFormation || !RBMAI.Utilities.IsRangedForArrangement(f))
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(f);
                    if (c.IsValid)
                    {
                        shooters.Add(c);
                    }
                }
            }
            shooters.Add(a.Line);
            a.Shooters = shooters.ToArray();
            TerrainChokeHold.Plan choke = TerrainChokeHold.Published;
            if (choke != null && !TerrainChokeHold.IsBypassed && choke.Data != null && choke.Data.Choke.IsValid && defCentre.Distance(choke.Hold) <= ChokeHeldDistance)
            {
                a.Choke = choke.Data.Choke;
                a.ChokeWidth = float.IsNaN(choke.Data.LineWidth) ? 0f : choke.Data.LineWidth;
                a.ChokeId = choke.Data.ChokeId;
            }
            if (plan != null && plan.IsRoute && AnyFollowing(plan.Version))
            {
                a.KeepKind = plan.Kind;
            }
            return a;
        }

        private static bool AnyFollowing(int version)
        {
            foreach (KeyValuePair<Formation, UseState> entry in States)
            {
                UseState s = entry.Value;
                if (s != null && s.Following && s.Version == version && s.BattleDrop == null)
                {
                    return true;
                }
            }
            return false;
        }

        // use (async AI thread)

        /// <summary>Per formation; written only by that formation's own AI tick.</summary>
        internal sealed class UseState
        {
            public bool Following;
            public int Version;
            public volatile int Index;
            public string BattleDrop;
            public int DroppedVersion = -1;
            public int RefusedVersion = -1;
            public float TakenAt;
            public Vec2 SamplePos = Vec2.Invalid;
            public float SampleAt;
            public float LastCallAt = float.NegativeInfinity;
            public WorldPosition Target = WorldPosition.Invalid;
            public Vec2 TargetPoint = Vec2.Invalid;
            public float BuiltAt = float.NegativeInfinity;
            public int TargetIndex = -1;
            public int TargetVersion = -1;
        }

        private static readonly ConcurrentDictionary<Formation, UseState> States = new ConcurrentDictionary<Formation, UseState>();

        internal sealed class UseEvent
        {
            public Team Team;
            public int FormationIndex;
            public float Time;
            public string What;
            public string Why;
        }

        private static readonly ConcurrentQueue<UseEvent> UseEvents = new ConcurrentQueue<UseEvent>();

        internal static bool TryDequeueUse(out UseEvent e)
        {
            return UseEvents.TryDequeue(out e);
        }

        /// <summary>
        /// AI thread, from OverrideBehaviorAdvance.CalculateCurrentOrder: true with the Move point (front-centre) and facing of the
        /// current leg when the formation follows the attacker's route now; false = advance straight as before. advanceOffset = the
        /// advance's own reach from the centre this tick. lastLeg: the leg to the deploy point (face the enemy).
        /// </summary>
        internal static bool TryLeg(BehaviorComponent behavior, Formation formation, float advanceOffset, out WorldPosition target, out Vec2 facing, out bool lastLeg)
        {
            target = WorldPosition.Invalid;
            facing = Vec2.Invalid;
            lastLeg = false;
            Team team = formation?.Team;
            if (team == null || team.Side != BattleSideEnum.Attacker || !team.HasTeamAi)
            {
                return false;
            }
            SidePlan plan = ForTeam(team);
            UseState s;
            States.TryGetValue(formation, out s);
            if (plan == null)
            {
                return false;
            }
            float now = Now();
            if (!formation.IsAIControlled)
            {
                if (s != null && s.Following)
                {
                    Drop(formation, s, plan, now, "player", true);
                }
                return false;
            }
            if (formation.AI == null || formation.AI.ActiveBehavior != behavior)
            {
                return false;
            }
            if (FormationCohesion.TeamMain(team) != formation)
            {
                if (s != null && s.Following)
                {
                    Drop(formation, s, plan, now, "notMain", false);
                }
                return false;
            }
            if (s == null)
            {
                s = States.GetOrAdd(formation, f => new UseState());
            }
            if (s.BattleDrop != null)
            {
                return false;
            }
            if (!plan.IsRoute)
            {
                if (s.Following && s.Version != plan.Version)
                {
                    Drop(formation, s, plan, now, "noGain v" + plan.Version.ToString(CultureInfo.InvariantCulture) + " " + (plan.Why ?? "-"), false);
                }
                return false;
            }
            if (s.DroppedVersion == plan.Version)
            {
                return false;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            if (!centre.IsValid)
            {
                return false;
            }
            if (now - s.LastCallAt > CallGapReset)
            {
                s.SamplePos = centre;
                s.SampleAt = now;
            }
            s.LastCallAt = now;
            bool battle;
            string drop;
            if (!s.Following || s.Version != plan.Version)
            {
                float off;
                int index = JoinIndex(plan, centre, out off);
                if (off > JoinGap)
                {
                    if (s.Following)
                    {
                        Drop(formation, s, plan, now, "offRoute " + Fmt0(off) + "m>" + Fmt0(JoinGap) + "m", false);
                    }
                    else if (s.RefusedVersion != plan.Version)
                    {
                        s.RefusedVersion = plan.Version;
                        Enqueue(formation, now, "refuse", "offRoute " + Fmt0(off) + "m>" + Fmt0(JoinGap) + "m v" + plan.Version.ToString(CultureInfo.InvariantCulture));
                    }
                    return false;
                }
                drop = DropCheck(formation, plan, centre, index, out battle);
                if (drop != null)
                {
                    if (s.RefusedVersion != plan.Version)
                    {
                        s.RefusedVersion = plan.Version;
                        Enqueue(formation, now, "refuse", drop + " v" + plan.Version.ToString(CultureInfo.InvariantCulture));
                    }
                    if (battle)
                    {
                        s.BattleDrop = drop;
                    }
                    else
                    {
                        s.DroppedVersion = plan.Version;
                    }
                    return false;
                }
                bool adopt = s.Following;
                int oldVersion = s.Version;
                s.Following = true;
                s.Version = plan.Version;
                s.Index = index;
                s.Target = WorldPosition.Invalid;
                s.TargetPoint = Vec2.Invalid;
                if (!adopt)
                {
                    s.TakenAt = now;
                    s.SamplePos = centre;
                    s.SampleAt = now;
                }
                Enqueue(formation, now, adopt ? "adopt" : "take", (adopt ? "v" + oldVersion.ToString(CultureInfo.InvariantCulture) + "->" : "") + "v" + plan.Version.ToString(CultureInfo.InvariantCulture)
                    + " " + plan.KindName + " wp " + (index + 1).ToString(CultureInfo.InvariantCulture) + "/" + plan.Waypoints.Count.ToString(CultureInfo.InvariantCulture)
                    + " off=" + Fmt0(off) + "m gain=" + Fmt2(GainOf(plan)) + " at=" + Fmt0(centre.x) + "," + Fmt0(centre.y));
            }
            drop = DropCheck(formation, plan, centre, s.Index, out battle);
            if (drop == null)
            {
                if (centre.Distance(s.SamplePos) >= StallMove)
                {
                    s.SamplePos = centre;
                    s.SampleAt = now;
                }
                else if (now - s.SampleAt >= StallSeconds)
                {
                    drop = "stalled moved<" + Fmt0(StallMove) + "m in " + Fmt0(now - s.SampleAt) + "s";
                    battle = true;
                }
                else if (now - s.TakenAt > MaxFollowSeconds)
                {
                    drop = "timeout " + Fmt0(now - s.TakenAt) + "s";
                    battle = true;
                }
            }
            if (drop != null)
            {
                Drop(formation, s, plan, now, drop, battle);
                return false;
            }

            // next waypoint: the front within WaypointArrive of it, or past it along the leg
            Vec2 direction = formation.Direction;
            if (!direction.IsValid || direction.Normalize() < 0.001f)
            {
                direction = Vec2.Invalid;
            }
            Vec2 front = direction.IsValid ? centre + direction * (formation.Depth * 0.5f) : centre;
            int count = plan.Waypoints.Count;
            while (s.Index < count)
            {
                Vec2 wp = plan.Waypoints[s.Index].Position;
                Vec2 previous = (s.Index == 0) ? (plan.From.IsValid ? plan.From : centre) : plan.Waypoints[s.Index - 1].Position;
                Vec2 leg = wp - previous;
                bool passed = leg.Normalize() > 1f && Vec2.DotProduct(front - wp, leg) > 0f;
                if (front.Distance(wp) > WaypointArrive && !passed)
                {
                    break;
                }
                s.Index = s.Index + 1;
                if (s.Index < count)
                {
                    Enqueue(formation, now, "waypoint", (s.Index + 1).ToString(CultureInfo.InvariantCulture) + "/" + count.ToString(CultureInfo.InvariantCulture)
                        + " v" + plan.Version.ToString(CultureInfo.InvariantCulture) + (passed ? " passed" : " reached") + " at=" + Fmt0(centre.x) + "," + Fmt0(centre.y));
                }
            }
            if (s.Index >= count)
            {
                Drop(formation, s, plan, now, "arrived v" + plan.Version.ToString(CultureInfo.InvariantCulture), true);
                return false;
            }
            Waypoint w = plan.Waypoints[s.Index];
            lastLeg = s.Index == count - 1;
            Vec2 toWaypoint = w.Position - centre;
            float distance = toWaypoint.Normalize();
            if (distance < 0.5f)
            {
                toWaypoint = direction.IsValid ? direction : new Vec2(0f, 1f);
            }
            float reach = Math.Max(3f, advanceOffset);
            Vec2 wanted = (distance <= reach) ? w.Position : centre + toWaypoint * reach;
            if (s.TargetIndex != s.Index || s.TargetVersion != plan.Version || !s.TargetPoint.IsValid
                || (s.TargetPoint.DistanceSquared(wanted) > RebuildDistance * RebuildDistance && now - s.BuiltAt >= RebuildSeconds))
            {
                // ONE SetVec2MT on a fresh copy of the waypoint's cached WorldPosition, kept until the next rebuild
                WorldPosition built = w.World;
                if (wanted != w.Position)
                {
                    built.SetVec2MT(wanted);
                }
                s.Target = built;
                s.TargetPoint = wanted;
                s.BuiltAt = now;
                s.TargetIndex = s.Index;
                s.TargetVersion = plan.Version;
            }
            target = s.Target;
            facing = toWaypoint;
            return target.IsValid;
        }

        /// <summary>Segment of From -> waypoints nearest p: the index of the waypoint it leads to, and the distance off it.</summary>
        private static int JoinIndex(SidePlan plan, Vec2 p, out float off)
        {
            off = float.MaxValue;
            int best = 0;
            Vec2 previous = plan.From.IsValid ? plan.From : plan.Waypoints[0].Position;
            for (int i = 0; i < plan.Waypoints.Count; i++)
            {
                Vec2 next = plan.Waypoints[i].Position;
                float d = SegmentDistance(p, previous, next);
                if (d < off)
                {
                    off = d;
                    best = i;
                }
                previous = next;
            }
            return best;
        }

        private static float SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            float length2 = ab.LengthSquared;
            if (length2 < 1e-4f)
            {
                return p.Distance(a);
            }
            float t = Math.Max(0f, Math.Min(1f, Vec2.DotProduct(p - a, ab) / length2));
            return p.Distance(a + ab * t);
        }

        /// <summary>Why the route must be dropped now (battle: for the rest of the battle), else null. Cached formation data and footprints only.</summary>
        private static string DropCheck(Formation formation, SidePlan plan, Vec2 centre, int index, out bool battle)
        {
            battle = true;
            Mission mission = Mission.Current;
            if (mission == null)
            {
                return "noMission";
            }
            string blocked = null;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits <= 0 || !ThreatSignificance.IsSignificant(formation, enemy))
                    {
                        continue;
                    }
                    float gap = FormationGeometry.Gap(formation, FootprintMode.ScatterAware, enemy, FootprintMode.ScatterAware);
                    if (gap <= DropGap)
                    {
                        return "enemyNear " + TerrainCavalry.FormationName(enemy) + " gap=" + Fmt0(gap) + "m";
                    }
                    if (blocked != null || enemy.QuerySystem.IsRangedCavalryFormation)
                    {
                        continue;
                    }
                    Footprint shape;
                    if (!FormationGeometry.TryGet(enemy, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    Vec2 previous = centre;
                    for (int i = index; i < plan.Waypoints.Count; i++)
                    {
                        Vec2 next = plan.Waypoints[i].Position;
                        float d = FormationGeometry.GapToSegment(shape, previous, next);
                        if (d <= BlockGap)
                        {
                            blocked = "blocked " + TerrainCavalry.FormationName(enemy) + " gap=" + Fmt0(d) + "m wp " + (i + 1).ToString(CultureInfo.InvariantCulture);
                            break;
                        }
                        previous = next;
                    }
                }
            }
            if (RBMAI.Utilities.FormationFightingInMelee(formation, MeleeShare))
            {
                return "melee";
            }
            if (blocked != null)
            {
                battle = false;
                return blocked;
            }
            return null;
        }

        private static void Drop(Formation formation, UseState s, SidePlan plan, float now, string why, bool battle)
        {
            s.Following = false;
            s.Target = WorldPosition.Invalid;
            s.TargetPoint = Vec2.Invalid;
            if (battle)
            {
                s.BattleDrop = why;
            }
            else
            {
                s.DroppedVersion = plan.Version;
            }
            Enqueue(formation, now, "drop", why + (battle ? " (battle)" : " (version)") + " followed=" + Fmt0(now - s.TakenAt) + "s");
        }

        private static float GainOf(SidePlan plan)
        {
            BattleTerrain.ApproachCandidate chosen = (plan.Result != null) ? plan.Result.Chosen : null;
            return (chosen != null) ? chosen.Gain : float.NaN;
        }

        private static void Enqueue(Formation formation, float time, string what, string why)
        {
            DecisionTrace.Note(formation, "Approach", what, why);
            if (!RBMConfig.RBMConfig.battleTerrainLoggingEnabled || formation == null)
            {
                return;
            }
            UseEvents.Enqueue(new UseEvent
            {
                Team = formation.Team,
                FormationIndex = (int)formation.FormationIndex,
                Time = time,
                What = what,
                Why = why
            });
        }

        private static float Now()
        {
            Mission mission = Mission.Current;
            return (mission != null) ? mission.CurrentTime : 0f;
        }

        // log (main thread)

        /// <summary>The APPROACH line's fields after the side: applied, version, chosen, gain, the line scored against, every candidate with its terms, waypoints.</summary>
        internal static string Describe(BattleTerrain.ApproachResult result, SidePlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("applied=").Append(RBMConfig.RBMConfig.terrainTacticsEnabled && !ArmyScale.NoPlans(BattleSideEnum.Attacker) ? 1 : 0);
            sb.Append("\tv=").Append((plan != null) ? plan.Version.ToString(CultureInfo.InvariantCulture) : "-");
            sb.Append("\tchosen=").Append((plan != null) ? plan.KindName : "-");
            if (result == null)
            {
                sb.Append("\twhy=notRun");
                return sb.ToString();
            }
            BattleTerrain.ApproachCandidate chosen = result.Chosen;
            sb.Append("\tgain=").Append(Fmt2((chosen != null) ? chosen.Gain : float.NaN));
            sb.Append("\tminGain=").Append(Fmt2(result.MinGain));
            sb.Append("\twhy=").Append((plan != null && plan.Why != null) ? plan.Why : (result.Why ?? "-"));
            sb.Append("\tfrom=").Append(Fmt0(result.From.x)).Append(',').Append(Fmt0(result.From.y));
            sb.Append("\tline=").Append(result.LineSource ?? "-").Append(" at=").Append(Fmt0(result.Line.x)).Append(',').Append(Fmt0(result.Line.y))
              .Append(" facing=").Append(Fmt2(result.Facing.x)).Append(',').Append(Fmt2(result.Facing.y))
              .Append(" halfWidth=").Append(Fmt0(result.HalfWidth))
              .Append(" covered=L").Append(result.LeftCovered ? 1 : 0).Append('R').Append(result.RightCovered ? 1 : 0);
            sb.Append("\tchoke=").Append(result.HeldChoke ?? "-");
            sb.Append("\tpace=").Append(Fmt2(result.Pace)).Append("\tarcherScale=").Append(Fmt2(result.ArcherScale)).Append("\tshooters=").Append(result.Shooters.ToString(CultureInfo.InvariantCulture));
            foreach (BattleTerrain.ApproachCandidate c in result.Candidates)
            {
                sb.Append("\tcand=").Append(BattleTerrain.ApproachKindName(c.Kind));
                if (float.IsNaN(c.Total))
                {
                    sb.Append(" rejected=").Append(c.Why ?? "-");
                    continue;
                }
                sb.Append(" len=").Append(Fmt0(c.Length)).Append("(route ").Append(Fmt0(c.RouteLength)).Append(" last ").Append(Fmt0(c.AssaultLength)).Append(')')
                  .Append(" time=").Append(Fmt0(c.Seconds))
                  .Append(" walk=").Append(Fmt0(c.TermWalk))
                  .Append(" fire=").Append(Fmt0(c.TermFire)).Append('(').Append(Fmt0(c.FireSeconds)).Append("s)")
                  .Append(" climb=").Append(Fmt0(c.TermClimb)).Append('(').Append(Fmt1(c.Climb)).Append("m)")
                  .Append(" bad=").Append(Fmt0(c.TermBad)).Append('(').Append(Fmt0(c.BadSeconds)).Append("s)")
                  .Append(" choke=").Append(Fmt0(c.TermChoke)).Append('(').Append(c.Choke ?? "-").Append(')')
                  .Append(" narrow=").Append(Fmt0(c.TermNarrow)).Append('(').Append(Fmt0(c.Bottleneck)).Append("m)")
                  .Append(" flank=").Append(Fmt0(c.TermFlank))
                  .Append(" total=").Append(Fmt0(c.Total))
                  .Append(" gain=").Append(Fmt2(c.Gain))
                  .Append(" deploy=").Append(Fmt0(c.Deploy.x)).Append(',').Append(Fmt0(c.Deploy.y))
                  .Append(" expanded=").Append(c.Expanded.ToString(CultureInfo.InvariantCulture))
                  .Append(' ').Append(c == chosen ? "CHOSEN" : (c.Why ?? "-"));
            }
            if (plan != null && plan.IsRoute)
            {
                sb.Append("\twaypoints=").Append(plan.Waypoints.Count.ToString(CultureInfo.InvariantCulture)).Append(" [");
                for (int i = 0; i < plan.Waypoints.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(' ');
                    }
                    sb.Append(Fmt0(plan.Waypoints[i].Position.x)).Append(',').Append(Fmt0(plan.Waypoints[i].Position.y));
                }
                sb.Append(']');
            }
            if (plan != null && plan.Dropped != null)
            {
                sb.Append("\tdropped=").Append(plan.Dropped);
            }
            sb.Append("\tms=").Append(Fmt1(result.Ms));
            return sb.ToString();
        }

        private static float AngleDegrees(Vec2 a, Vec2 b)
        {
            if (!a.IsValid || !b.IsValid || a.LengthSquared < 1e-6f || b.LengthSquared < 1e-6f)
            {
                return 0f;
            }
            float dot = Vec2.DotProduct(a.Normalized(), b.Normalized());
            return (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * 180.0 / Math.PI);
        }

        internal static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Fmt1(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Fmt2(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
