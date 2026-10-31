using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Which flank each side's flank groups go to, read from the feature map's Flank features (BattleTerrain.Flanks): the end of
    /// each army's line on its own left / right and the enemy's route round it. Applied where RBM sets a side itself
    /// (terrainTacticsEnabled): split infantry tactics leave out the guard on a COVERED own side (GuardSkipped; if men cannot be
    /// moved the guard gets the main line's behaviors); the forward skirmishers' side = AttackSide when set; cavalry flank guards
    /// (CavalrySide) move off a covered side onto the open one, a LONE cavalry formation goes to AttackSide; archer flank groups
    /// are left alone. Off, before the publish or without a plan for the side, every helper returns the tactic's own choice.
    /// Own left / right are the army's own, facing the enemy (same as BehaviorProtectFlank / FormationAI.BehaviorSide: Left guard
    /// at main + LeftVec = (-y, x) toward the enemy; the defender's own left = -BattleTerrain.Left). A flank feature's EnemyRoute
    /// is the OTHER army's route round that end, so the enemy's own left is our right.
    /// Rules: covered own side = the line's end is deep water / no-navmesh / too steep, at most CoveredMaxSpare x frontage beyond it
    /// (map edge only within EdgeCoveredMaxSpare), AND the enemy has no route round that side or one CoveredRouteFactor x longer
    /// than round the other. AttackSide = the uncovered enemy flank we have the shorter reachable route round; none within
    /// AttackSideTie. Routes never enter cells within half of EdgeMargin of the map edge.
    /// Re-apply: the first side choices happen at the first tactic apply, long before the publish, so TacticSelection's terrain
    /// re-pick calls RequestReapplyIfChanged when it keeps the tactic: if the plan changes a recorded choice, native's
    /// IsTacticReapplyNeeded is set. Later plan VERSIONS re-apply the same way via FollowPlan (PlanSeen; a version that only drops
    /// AttackSide never moves a side).
    /// Staying current: Tick (MAIN thread, every CheckInterval) marks a side's plan stale once the enemy's main formation
    /// (EnemyReference) moved StaleMove or turned StaleTurn; moved = both sides re-planned from the armies' centres NOW
    /// (BattleTerrain.ReplanFlanks on a background Task, generation token; limits ReplanMinSeconds, ReplanMax, ReplanMinGap, else
    /// AttackSide is dropped); turned = AttackSide dropped, latched until a re-plan restores it. Covered-flank watch: a covered own
    /// side is latched "threatened" (open) once a SIGNIFICANT enemy cavalry/HA formation is beyond our main's half width on that
    /// side, within WatchRange of the line end and closing (own position samples) for WatchConfirmChecks checks.
    /// THREADING: Compute / ComputeFrom read only immutable data and build immutable SidePlans; BattleTerrainLogic computes them on
    /// the MAIN thread and publishes them (two volatile writes); Tick runs on the MAIN thread. The helpers run on the async AI
    /// thread: one volatile read, an immutable plan, concurrent-dictionary writes and a queue; no engine call, no WorldPosition,
    /// no MBRandom. FLANKUSE events are queued and written on the main thread. Cleared at mission end and in MissionStartReset.
    /// </summary>
    internal static class TerrainFlankPlan
    {

        public const float CoveredMaxSpare = 1f; // x own frontage: room beyond a water / no-navmesh / steep line end that still covers the flank

        public const float EdgeCoveredMaxSpare = 0.5f; // the map edge covers a flank only this close (x own frontage)

        public const float CoveredRouteFactor = 1.5f; // also needs the enemy's route round it missing, or this x its route round the other side

        public const float AttackSideTie = 0.1f; // AttackSide: open enemy flanks whose route times differ by at most this share of the shorter = tie (none)


        public const float StaleMove = 100f; // m the enemy's main formation moved since the plan: re-plan ...

        public const float StaleTurn = 30f; // ... or degrees it turned (AttackSide dropped)

        public const float ReplanMinSeconds = 20f; // between re-plans, and after the publish

        public const int ReplanMax = 10; // per battle, timed ones included; a later stale move drops AttackSide instead

        // TIMER: ReplanPeriod after the last plan, re-plan when the enemy moved TimedMinMove or turned TimedMinTurn since (below the stale thresholds too; also restores a dropped AttackSide). A timed re-plan that cannot run drops nothing.
        public const float ReplanPeriod = 30f;

        public const float TimedMinMove = 20f;

        public const float TimedMinTurn = 10f;

        public const float ReplanMinGap = 150f; // no re-plan once the armies' centres are closer (lines about to meet); a stale AttackSide is dropped instead

        public const float CheckInterval = 1f; // stale and watch checks; the watch's speeds come from samples this far apart

        // Covered-flank watch: a significant enemy mounted formation beyond our half width on a covered side counts within WatchRange
        // of that line end, closing on it or riding toward our rear at WatchClosingSpeed (m/s), on WatchConfirmChecks checks in a row.
        public const float WatchRange = 250f;

        public const float WatchClosingSpeed = 1.5f;

        public const int WatchConfirmChecks = 2;


        private static readonly PlanSlot<SidePlan> Plans = new PlanSlot<SidePlan>();

        private static readonly ReplanJob<List<BattleTerrain.FlankFeature>> Replan = new ReplanJob<List<BattleTerrain.FlankFeature>>("flank re-plan canceled");
        private static int _replans;
        private static float _lastReplanAt = float.NegativeInfinity;
        // The armies' centres the running re-plan started from (kept on its plans for the map view).
        private static Vec2 _replanAttackerAt = Vec2.Invalid;
        private static Vec2 _replanDefenderAt = Vec2.Invalid;
        private static float _firstTickAt = float.NaN;
        private static float _nextCheckAt;
        private static float _lastSampleAt = float.NaN;
        private static SideState _attackerState = new SideState();
        private static SideState _defenderState = new SideState();
        // Watch samples of enemy mounted formations and their closing streaks (main thread only).
        private static readonly Dictionary<Formation, Vec2> WatchLast = new Dictionary<Formation, Vec2>();
        private static readonly Dictionary<Formation, int> WatchHits = new Dictionary<Formation, int>();

        /// <summary>One side's re-plan reference and latches. Main thread only.</summary>
        private sealed class SideState
        {
            // The enemy's main formation's position / facing when the side's current plan was made.
            public bool HasReference;
            public Vec2 RefPosition;
            public Vec2 RefFacing;
            // AttackSide dropped until the next successful re-plan; own side threatened, latched for the battle.
            public bool AttackDropped;
            public string AttackDroppedWhy;
            public bool LeftThreatened;
            public string LeftThreatWhy;
            public bool RightThreatened;
            public string RightThreatWhy;
            public string MovedWhy; // why the side is due a re-plan this check (null = not)
            public bool MovedIsTimer; // the due re-plan is only the timed one: when it cannot run, nothing is dropped
        }

        /// <summary>The side's plan as the tactics read it: null with terrainTacticsEnabled off, before the publish, or for another side. Any thread.</summary>
        public static SidePlan ForSide(BattleSideEnum side)
        {
            // none for a side without terrain plans: no flank choices, no re-apply on a new plan version
            return Plans.ForSide(side);
        }

        /// <summary>ForSide of the team's side.</summary>
        public static SidePlan ForTeam(Team team)
        {
            return Plans.ForTeam(team);
        }

        /// <summary>Whatever the toggle (the log's prediction).</summary>
        internal static SidePlan Published(BattleSideEnum side)
        {
            return Plans.Published(side);
        }

        /// <summary>Main thread, at the terrain publish.</summary>
        internal static void Publish(SidePlan attacker, SidePlan defender)
        {
            Plans.Publish(attacker, defender);
        }

        /// <summary>Main thread: mission end and MissionStartReset. Also drops the tactic- / team-keyed records, queued log lines, the watch's samples and the re-plan state (a running re-plan is abandoned by generation).</summary>
        public static void Clear()
        {
            Plans.Clear();
            Uses.Clear();
            Logged.Clear();
            ReapplyRequested.Clear();
            PlanSeen.Clear();
            TeamSeen.Clear();
            TerrainLog.Clear();
            Replan.Clear();
            _replans = 0;
            _lastReplanAt = float.NegativeInfinity;
            _firstTickAt = float.NaN;
            _nextCheckAt = 0f;
            _lastSampleAt = float.NaN;
            _attackerState = new SideState();
            _defenderState = new SideState();
            WatchLast.Clear();
            WatchHits.Clear();
        }

        /// <summary>One side's plan. Immutable.</summary>
        internal sealed class SidePlan
        {
            public readonly BattleSideEnum Side;
            public readonly bool OwnLeftCovered;
            public readonly bool OwnRightCovered;
            public readonly string OwnLeftReason; // ASCII ("water noRoute", "blocked far", "max")
            public readonly string OwnRightReason;
            public readonly FormationAI.BehaviorSide AttackSide; // own Left / Right to send flank attacks to; BehaviorSideNotSet = none
            public readonly string AttackReason;
            public readonly string Inputs; // raw feature-map inputs, ASCII
            public readonly int Version; // 1 at the publish, + 1 per re-plan / stale drop / threatened side
            public readonly string Note; // publish | replan | stale | threatened
            public readonly IReadOnlyList<BattleTerrain.FlankFeature> Flanks; // the four features the plan was made from, for the map view
            public readonly Vec2 Centre; // army centre the plan was made from; Invalid = the snapshot's centroid (the publish)
            public readonly Vec2 Forward; // attacker-to-defender axis; Invalid = the snapshot's Forward (the publish)

            internal SidePlan(BattleSideEnum side, bool ownLeftCovered, string ownLeftReason, bool ownRightCovered, string ownRightReason,
                FormationAI.BehaviorSide attackSide, string attackReason, string inputs, int version = 1, string note = "publish",
                IReadOnlyList<BattleTerrain.FlankFeature> flanks = null, Vec2? centre = null, Vec2? forward = null)
            {
                Flanks = flanks;
                Centre = centre ?? Vec2.Invalid;
                Forward = forward ?? Vec2.Invalid;
                Side = side;
                OwnLeftCovered = ownLeftCovered;
                OwnLeftReason = ownLeftReason;
                OwnRightCovered = ownRightCovered;
                OwnRightReason = ownRightReason;
                AttackSide = attackSide;
                AttackReason = attackReason;
                Inputs = inputs;
                Version = version;
                Note = note;
            }

            public bool HasAttackSide => AttackSide == FormationAI.BehaviorSide.Left || AttackSide == FormationAI.BehaviorSide.Right;

            public bool IsCovered(FormationAI.BehaviorSide side)
            {
                return (side == FormationAI.BehaviorSide.Left) ? OwnLeftCovered : (side == FormationAI.BehaviorSide.Right) && OwnRightCovered;
            }

            public string ReasonFor(FormationAI.BehaviorSide side)
            {
                return (side == FormationAI.BehaviorSide.Left) ? OwnLeftReason : (side == FormationAI.BehaviorSide.Right) ? OwnRightReason : "-";
            }
        }

        // compute (main thread, immutable snapshot only)

        /// <summary>Both null when the snapshot is null or has no flank feature.</summary>
        public static void Compute(BattleTerrain terrain, out SidePlan attacker, out SidePlan defender)
        {
            attacker = null;
            defender = null;
            if (terrain == null)
            {
                return;
            }
            ComputeFrom(terrain.Flanks, (terrain.Attacker != null) ? terrain.Attacker.Frontage : float.NaN,
                (terrain.Defender != null) ? terrain.Defender.Frontage : float.NaN, 1, 1, "publish", Vec2.Invalid, Vec2.Invalid, out attacker, out defender);
        }

        /// <summary>Both sides' plans from the four flank features (the snapshot's, or a re-plan's); both null without any. attackerAt / defenderAt = the armies' centres a re-plan used (Invalid at the publish), kept for the map view. Pure.</summary>
        private static void ComputeFrom(IReadOnlyList<BattleTerrain.FlankFeature> flanks, float aFront, float dFront, int attackerVersion, int defenderVersion,
            string note, Vec2 attackerAt, Vec2 defenderAt, out SidePlan attacker, out SidePlan defender)
        {
            attacker = null;
            defender = null;
            if (flanks == null || flanks.Count == 0)
            {
                return;
            }
            BattleTerrain.FlankFeature aLeft = FindFlank(flanks, BattleTerrain.ArmySet.Attacker, true);
            BattleTerrain.FlankFeature aRight = FindFlank(flanks, BattleTerrain.ArmySet.Attacker, false);
            BattleTerrain.FlankFeature dLeft = FindFlank(flanks, BattleTerrain.ArmySet.Defender, true);
            BattleTerrain.FlankFeature dRight = FindFlank(flanks, BattleTerrain.ArmySet.Defender, false);

            bool aLeftCovered, aRightCovered, dLeftCovered, dRightCovered;
            string aLeftReason, aRightReason, dLeftReason, dRightReason;
            Cover(aLeft, aRight, aFront, out aLeftCovered, out aLeftReason);
            Cover(aRight, aLeft, aFront, out aRightCovered, out aRightReason);
            Cover(dLeft, dRight, dFront, out dLeftCovered, out dLeftReason);
            Cover(dRight, dLeft, dFront, out dRightCovered, out dRightReason);

            string aAttackReason, dAttackReason;
            FormationAI.BehaviorSide aAttack = AttackSideFor(dLeft, dLeftCovered, dRight, dRightCovered, out aAttackReason);
            FormationAI.BehaviorSide dAttack = AttackSideFor(aLeft, aLeftCovered, aRight, aRightCovered, out dAttackReason);

            Vec2 forward = Vec2.Invalid;
            if (attackerAt.IsValid && defenderAt.IsValid)
            {
                Vec2 axis = defenderAt - attackerAt;
                if (axis.Normalize() >= 1f)
                {
                    forward = axis;
                }
            }
            attacker = new SidePlan(BattleSideEnum.Attacker, aLeftCovered, aLeftReason, aRightCovered, aRightReason, aAttack, aAttackReason,
                DescribeInputs(aFront, aLeft, aRight, dLeft, dRight), attackerVersion, note, flanks, attackerAt, forward);
            defender = new SidePlan(BattleSideEnum.Defender, dLeftCovered, dLeftReason, dRightCovered, dRightReason, dAttack, dAttackReason,
                DescribeInputs(dFront, dLeft, dRight, aLeft, aRight), defenderVersion, note, flanks, defenderAt, forward);
        }

        private static BattleTerrain.FlankFeature FindFlank(IReadOnlyList<BattleTerrain.FlankFeature> flanks, BattleTerrain.ArmySet army, bool ownLeft)
        {
            foreach (BattleTerrain.FlankFeature flank in flanks)
            {
                if (flank != null && flank.Army == army && flank.OwnLeft == ownLeft)
                {
                    return flank;
                }
            }
            return null;
        }

        /// <summary>other = the same army's other end.</summary>
        private static void Cover(BattleTerrain.FlankFeature flank, BattleTerrain.FlankFeature other, float frontage, out bool covered, out string reason)
        {
            covered = false;
            if (flank == null)
            {
                reason = "unmeasured";
                return;
            }
            string end = EndName(flank.End);
            bool terrainEnd = flank.End == BattleTerrain.End.Water || flank.End == BattleTerrain.End.Blocked || flank.End == BattleTerrain.End.Steep;
            bool edge = flank.End == BattleTerrain.End.Edge;
            if (!terrainEnd && !edge)
            {
                reason = end;
                return;
            }
            if (float.IsNaN(frontage) || float.IsNaN(flank.Spare))
            {
                reason = end + " spareUnknown";
                return;
            }
            float maxSpare = (terrainEnd ? CoveredMaxSpare : EdgeCoveredMaxSpare) * frontage;
            if (flank.Spare > maxSpare)
            {
                reason = end + " far";
                return;
            }
            if (!flank.Turnable)
            {
                covered = true;
                reason = end + " noRoute";
                return;
            }
            float here = flank.EnemyRoute.Seconds;
            float there = (other != null && other.Turnable) ? other.EnemyRoute.Seconds : float.NaN;
            if (!float.IsNaN(here) && !float.IsNaN(there) && there > 0f && here >= CoveredRouteFactor * there)
            {
                covered = true;
                reason = end + " longRoute x" + Fmt(here / there);
                return;
            }
            reason = end + " routeOpen";
        }

        /// <summary>The own side to send flank attacks to, from the ENEMY's flank features (their EnemyRoute is our route round them): the enemy's own left is our right.</summary>
        private static FormationAI.BehaviorSide AttackSideFor(BattleTerrain.FlankFeature enemyLeft, bool enemyLeftCovered,
            BattleTerrain.FlankFeature enemyRight, bool enemyRightCovered, out string reason)
        {
            float viaOurRight = Candidate(enemyLeft, enemyLeftCovered);
            float viaOurLeft = Candidate(enemyRight, enemyRightCovered);
            bool right = !float.IsNaN(viaOurRight);
            bool left = !float.IsNaN(viaOurLeft);
            if (!right && !left)
            {
                reason = "noOpenRoute";
                return FormationAI.BehaviorSide.BehaviorSideNotSet;
            }
            if (right && !left)
            {
                reason = "onlyRight";
                return FormationAI.BehaviorSide.Right;
            }
            if (left && !right)
            {
                reason = "onlyLeft";
                return FormationAI.BehaviorSide.Left;
            }
            float shorter = (viaOurLeft < viaOurRight) ? viaOurLeft : viaOurRight;
            if (System.Math.Abs(viaOurLeft - viaOurRight) <= AttackSideTie * shorter)
            {
                reason = "tie";
                return FormationAI.BehaviorSide.BehaviorSideNotSet;
            }
            reason = "shorter";
            return (viaOurLeft < viaOurRight) ? FormationAI.BehaviorSide.Left : FormationAI.BehaviorSide.Right;
        }

        /// <summary>Our route time round this enemy flank, or NaN when it is covered or we have no reachable route.</summary>
        private static float Candidate(BattleTerrain.FlankFeature enemyFlank, bool covered)
        {
            if (enemyFlank == null || covered || !enemyFlank.Turnable)
            {
                return float.NaN;
            }
            float seconds = enemyFlank.EnemyRoute.Seconds;
            return (float.IsNaN(seconds) || float.IsInfinity(seconds)) ? float.NaN : seconds;
        }

        private static string DescribeInputs(float frontage, BattleTerrain.FlankFeature ownLeft, BattleTerrain.FlankFeature ownRight,
            BattleTerrain.FlankFeature enemyLeft, BattleTerrain.FlankFeature enemyRight)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("frontage=").Append(Fmt(frontage));
            sb.Append(" ownL[").Append(DescribeFlank(ownLeft, "enemyRoute")).Append(']');
            sb.Append(" ownR[").Append(DescribeFlank(ownRight, "enemyRoute")).Append(']');
            sb.Append(" enemyL[").Append(DescribeFlank(enemyLeft, "ourRoute")).Append(']');
            sb.Append(" enemyR[").Append(DescribeFlank(enemyRight, "ourRoute")).Append(']');
            return sb.ToString();
        }

        private static string DescribeFlank(BattleTerrain.FlankFeature flank, string routeName)
        {
            if (flank == null)
            {
                return "-";
            }
            return "end=" + EndName(flank.End) + " dist=" + Fmt(flank.EndDistance) + " spare=" + Fmt(flank.Spare) + " " + routeName + "=" + Route(flank.EnemyRoute);
        }

        /// <summary>"-" not measured, "0" unreachable, else seconds.</summary>
        private static string Route(BattleTerrain.FlankRoute route)
        {
            if (route == null)
            {
                return "-";
            }
            return route.Reachable ? Fmt(route.Seconds) + "s" : "0";
        }

        private static string EndName(BattleTerrain.End end)
        {
            return end.ToString().ToLowerInvariant();
        }

        // staying current: re-plan, stale drop, covered-flank watch (MAIN thread)

        /// <summary>MAIN thread, every tick after the terrain publish: finishes a running re-plan and, every CheckInterval, runs the stale check and the covered-flank watch. Adds the log lines' fields to lines; runs whether or not the log is on. Nothing with terrainTacticsEnabled off or without a published plan.</summary>
        internal static void Tick(Mission mission, BattleTerrain terrain, float now, List<string> lines)
        {
            if (mission == null || terrain == null)
            {
                return;
            }
            if (Replan.Running)
            {
                FinishReplan(mission, terrain, now, lines);
            }
            if (!TerrainPlans.AnyActive || Plans.IsEmpty || now < _nextCheckAt)
            {
                return;
            }
            _nextCheckAt = now + CheckInterval;
            if (float.IsNaN(_firstTickAt))
            {
                _firstTickAt = now;
            }
            CheckStale(mission, terrain, BattleSideEnum.Attacker, now, lines);
            CheckStale(mission, terrain, BattleSideEnum.Defender, now, lines);
            if (!Replan.Running && (_attackerState.MovedWhy != null || _defenderState.MovedWhy != null))
            {
                StartReplanOrDrop(mission, terrain, now, lines);
            }
            Watch(mission, now, lines);
        }

        private static SideState StateFor(BattleSideEnum side)
        {
            return (side == BattleSideEnum.Attacker) ? _attackerState : _defenderState;
        }

        /// <summary>Main thread: the side's reference (first call) or its stale test; a turn drops AttackSide here, a move is left in MovedWhy.</summary>
        private static void CheckStale(Mission mission, BattleTerrain terrain, BattleSideEnum side, float now, List<string> lines)
        {
            SideState state = StateFor(side);
            state.MovedWhy = null;
            state.MovedIsTimer = false;
            SidePlan plan = Published(side);
            Vec2 position;
            Vec2 facing;
            float halfFront;
            if (plan == null || !EnemyReference(mission, side, out position, out facing, out halfFront))
            {
                return;
            }
            if (!state.HasReference)
            {
                SetReference(state, position, facing);
                return;
            }
            float moved = position.Distance(state.RefPosition);
            float turned = AngleDegrees(facing, state.RefFacing);
            if (turned > StaleTurn && plan.HasAttackSide && !state.AttackDropped)
            {
                DropAttackSide(side, "turned " + Fmt0(turned) + "deg moved " + Fmt0(moved) + "m", now, lines);
                return;
            }
            if (moved > StaleMove)
            {
                state.MovedWhy = "moved " + Fmt0(moved) + "m turned " + Fmt0(turned) + "deg";
            }
            else if (now - Math.Max(_lastReplanAt, _firstTickAt) >= ReplanPeriod && (moved > TimedMinMove || turned > TimedMinTurn))
            {
                state.MovedWhy = "timer moved " + Fmt0(moved) + "m turned " + Fmt0(turned) + "deg";
                state.MovedIsTimer = true;
            }
        }

        private static void SetReference(SideState state, Vec2 position, Vec2 facing)
        {
            state.HasReference = true;
            state.RefPosition = position;
            state.RefFacing = facing;
        }

        /// <summary>Main thread: starts the re-plan when allowed, else drops each moved side's AttackSide.</summary>
        private static void StartReplanOrDrop(Mission mission, BattleTerrain terrain, float now, List<string> lines)
        {
            if (now - _lastReplanAt < ReplanMinSeconds || now - _firstTickAt < ReplanMinSeconds)
            {
                return;
            }
            Vec2 attackerAt;
            Vec2 defenderAt;
            bool centres = ArmyCentre(mission, BattleSideEnum.Attacker, out attackerAt);
            centres = ArmyCentre(mission, BattleSideEnum.Defender, out defenderAt) && centres;
            float gap = centres ? attackerAt.Distance(defenderAt) : float.NaN;
            string blocked = !centres ? "noCentre" : (_replans >= ReplanMax) ? "cap " + ReplanMax.ToString(CultureInfo.InvariantCulture)
                : (gap < ReplanMinGap) ? "close gap=" + Fmt0(gap) + "m" : null;
            if (blocked != null)
            {
                foreach (BattleSideEnum side in new[] { BattleSideEnum.Attacker, BattleSideEnum.Defender })
                {
                    SideState state = StateFor(side);
                    SidePlan plan = Published(side);
                    if (state.MovedWhy != null && !state.MovedIsTimer && plan != null && plan.HasAttackSide && !state.AttackDropped)
                    {
                        DropAttackSide(side, state.MovedWhy + " " + blocked, now, lines);
                    }
                }
                return;
            }
            BattleTerrain.SideInfo a = terrain.Attacker;
            BattleTerrain.SideInfo d = terrain.Defender;
            if (a == null || d == null)
            {
                return;
            }
            float aFront = a.Frontage;
            float dFront = d.Frontage;
            float aRide = (a.Mounted > 0) ? a.MountedSpeed : a.FootSpeed;
            float dRide = (d.Mounted > 0) ? d.MountedSpeed : d.FootSpeed;
            _replans++;
            _lastReplanAt = now;
            _replanAttackerAt = attackerAt;
            _replanDefenderAt = defenderAt;
            string reason = "n=" + _replans.ToString(CultureInfo.InvariantCulture) + "/" + ReplanMax.ToString(CultureInfo.InvariantCulture)
                + " gap=" + Fmt0(gap) + "m" + Due(BattleSideEnum.Attacker) + Due(BattleSideEnum.Defender);
            // Background: the read's flank walker and routes over the immutable grid only (no engine call).
            Replan.Start(reason, () => terrain.ReplanFlanks(attackerAt, defenderAt, aFront, dFront, aRide, dRide));
        }

        private static string Due(BattleSideEnum side)
        {
            SideState state = StateFor(side);
            return (state.MovedWhy != null) ? " " + side + "(enemy " + state.MovedWhy + ")" : "";
        }

        /// <summary>Main thread: publishes a finished re-plan as both sides' next versions (latches applied) and resets both references.</summary>
        private static void FinishReplan(Mission mission, BattleTerrain terrain, float now, List<string> lines)
        {
            List<BattleTerrain.FlankFeature> flanks;
            string ms;
            string failure;
            ReplanJob<List<BattleTerrain.FlankFeature>>.Outcome outcome = Replan.TryFinish(out flanks, out ms, out failure);
            string reasonText = Replan.Reason;
            if (outcome == ReplanJob<List<BattleTerrain.FlankFeature>>.Outcome.Failed)
            {
                lines.Add("FLANKPLAN\treplan\tfailed=" + failure + "\treason=" + reasonText + "\tms=" + ms);
                return;
            }
            if (outcome != ReplanJob<List<BattleTerrain.FlankFeature>>.Outcome.Done)
            {
                return;
            }
            SidePlan oldAttacker = Plans.Attacker;
            SidePlan oldDefender = Plans.Defender;
            SidePlan attacker;
            SidePlan defender;
            ComputeFrom(flanks, (terrain.Attacker != null) ? terrain.Attacker.Frontage : float.NaN, (terrain.Defender != null) ? terrain.Defender.Frontage : float.NaN,
                (oldAttacker != null) ? oldAttacker.Version + 1 : 1, (oldDefender != null) ? oldDefender.Version + 1 : 1, "replan",
                _replanAttackerAt, _replanDefenderAt, out attacker, out defender);
            if (attacker == null || defender == null)
            {
                lines.Add("FLANKPLAN\treplan\tfailed=noFlanks\treason=" + reasonText + "\tms=" + ms);
                return;
            }
            // A plan from the armies' current centres fits again: a dropped AttackSide comes back (the threatened sides stay).
            _attackerState.AttackDropped = false;
            _defenderState.AttackDropped = false;
            attacker = ApplyLatches(attacker, _attackerState, attacker.Version, "replan");
            defender = ApplyLatches(defender, _defenderState, defender.Version, "replan");
            Publish(attacker, defender);
            foreach (BattleSideEnum side in new[] { BattleSideEnum.Attacker, BattleSideEnum.Defender })
            {
                Vec2 position;
                Vec2 facing;
                float halfFront;
                if (EnemyReference(mission, side, out position, out facing, out halfFront))
                {
                    SetReference(StateFor(side), position, facing);
                }
            }
            lines.Add("FLANKPLAN\treplan\t" + BattleSideEnum.Attacker + "\t" + Describe(attacker) + "\treason=" + reasonText + "\tms=" + ms);
            lines.Add("FLANKPLAN\treplan\t" + BattleSideEnum.Defender + "\t" + Describe(defender) + "\treason=" + reasonText + "\tms=" + ms);
        }

        private static void DropAttackSide(BattleSideEnum side, string why, float now, List<string> lines)
        {
            SideState state = StateFor(side);
            SidePlan plan = Published(side);
            if (plan == null || state.AttackDropped)
            {
                return;
            }
            state.AttackDropped = true;
            state.AttackDroppedWhy = why;
            SidePlan next = ApplyLatches(plan, state, plan.Version + 1, "stale");
            PublishSide(next);
            lines.Add("FLANKPLAN\tstale\t" + side + "\tattackSide=none(was " + SideName(plan.AttackSide) + " " + plan.AttackReason + ")\twhy=" + why
                + "\tv=" + next.Version.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>The plan with the side's latches applied (threatened own sides open, a dropped AttackSide none), as the given version.</summary>
        private static SidePlan ApplyLatches(SidePlan plan, SideState state, int version, string note)
        {
            bool left = plan.OwnLeftCovered && !state.LeftThreatened;
            bool right = plan.OwnRightCovered && !state.RightThreatened;
            string leftReason = (plan.OwnLeftCovered && state.LeftThreatened) ? "threatened " + state.LeftThreatWhy : plan.OwnLeftReason;
            string rightReason = (plan.OwnRightCovered && state.RightThreatened) ? "threatened " + state.RightThreatWhy : plan.OwnRightReason;
            FormationAI.BehaviorSide attack = state.AttackDropped ? FormationAI.BehaviorSide.BehaviorSideNotSet : plan.AttackSide;
            string attackReason = (state.AttackDropped && plan.HasAttackSide) ? "stale " + state.AttackDroppedWhy
                : state.AttackDropped ? plan.AttackReason + " (stale)" : plan.AttackReason;
            return new SidePlan(plan.Side, left, leftReason, right, rightReason, attack, attackReason, plan.Inputs, version, note,
                plan.Flanks, plan.Centre, plan.Forward);
        }

        private static void PublishSide(SidePlan plan)
        {
            Plans.Set(plan.Side, plan);
        }

        /// <summary>Main thread: the covered-flank watch. Samples every enemy mounted formation's cached position each check (closing speed = change since the last sample) and latches a threatened side.</summary>
        private static void Watch(Mission mission, float now, List<string> lines)
        {
            float dt = float.IsNaN(_lastSampleAt) ? float.NaN : now - _lastSampleAt;
            _lastSampleAt = now;
            foreach (BattleSideEnum side in new[] { BattleSideEnum.Attacker, BattleSideEnum.Defender })
            {
                SidePlan plan = Published(side);
                SideState state = StateFor(side);
                bool watchLeft = plan != null && plan.OwnLeftCovered && !state.LeftThreatened;
                bool watchRight = plan != null && plan.OwnRightCovered && !state.RightThreatened;
                Formation main = (watchLeft || watchRight) ? MainFormation(mission, side, true) : null;
                Vec2 enemyAt;
                Vec2 enemyFacing;
                float enemyHalf;
                Vec2 centre = (main != null) ? RBMAI.Utilities.GetFormationCenter(main) : Vec2.Invalid;
                Vec2 toward = Vec2.Invalid;
                if (centre.IsValid && EnemyReference(mission, side, out enemyAt, out enemyFacing, out enemyHalf))
                {
                    toward = enemyAt - centre;
                    if (toward.Normalize() < 1f)
                    {
                        toward = Vec2.Invalid;
                    }
                }
                // Own left as BehaviorProtectFlank's: toward the enemy, LeftVec = (-y, x).
                Vec2 ownLeft = toward.IsValid ? new Vec2(-toward.y, toward.x) : Vec2.Invalid;
                float half = (main != null) ? main.Width * 0.5f : 0f;
                foreach (Team team in mission.Teams)
                {
                    if (team == null || team.Side == side || (team.Side != BattleSideEnum.Attacker && team.Side != BattleSideEnum.Defender))
                    {
                        continue;
                    }
                    foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                    {
                        if (enemy == null || enemy.CountOfUnits <= 0 || !(enemy.QuerySystem.IsCavalryFormation || enemy.QuerySystem.IsRangedCavalryFormation))
                        {
                            continue;
                        }
                        Vec2 at = RBMAI.Utilities.GetFormationCenter(enemy);
                        Vec2 last;
                        bool sampled = WatchLast.TryGetValue(enemy, out last);
                        WatchLast[enemy] = at;
                        if (!ownLeft.IsValid || !at.IsValid || !sampled || float.IsNaN(dt) || dt < CheckInterval * 0.5f)
                        {
                            WatchHits[enemy] = 0;
                            continue;
                        }
                        Vec2 rel = at - centre;
                        float lateral = Vec2.DotProduct(rel, ownLeft);
                        bool onLeft = lateral > half;
                        bool onRight = lateral < -half;
                        if (!((onLeft && watchLeft) || (onRight && watchRight)) || !ThreatSignificance.IsSignificant(main, enemy))
                        {
                            WatchHits[enemy] = 0;
                            continue;
                        }
                        Vec2 lineEnd = centre + ownLeft * (onLeft ? half : -half);
                        Vec2 toEnd = lineEnd - at;
                        float distance = toEnd.Normalize();
                        if (distance > WatchRange)
                        {
                            WatchHits[enemy] = 0;
                            continue;
                        }
                        Vec2 velocity = (at - last) * (1f / dt);
                        float closing = Math.Max(Vec2.DotProduct(velocity, toEnd), -Vec2.DotProduct(velocity, toward));
                        int hits;
                        WatchHits.TryGetValue(enemy, out hits);
                        hits = (closing >= WatchClosingSpeed) ? hits + 1 : 0;
                        WatchHits[enemy] = hits;
                        if (hits < WatchConfirmChecks)
                        {
                            continue;
                        }
                        string why = ThreatSignificance.Describe(main, enemy) + " lat=" + Fmt0(Math.Abs(lateral)) + "m dist=" + Fmt0(distance)
                            + "m closing=" + Fmt(closing) + "m/s";
                        string was = onLeft ? plan.OwnLeftReason : plan.OwnRightReason;
                        if (onLeft)
                        {
                            state.LeftThreatened = true;
                            state.LeftThreatWhy = why;
                            watchLeft = false;
                        }
                        else
                        {
                            state.RightThreatened = true;
                            state.RightThreatWhy = why;
                            watchRight = false;
                        }
                        SidePlan current = Published(side) ?? plan;
                        SidePlan next = ApplyLatches(current, state, current.Version + 1, "threatened");
                        PublishSide(next);
                        lines.Add("FLANKWATCH\t" + side + "\t" + (onLeft ? "ownLeft" : "ownRight") + "\tthreatened\tenemy=" + why
                            + "\twas=covered(" + was + ")\tv=" + next.Version.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            // Formations gone or no longer mounted lose their samples.
            if (WatchLast.Count > 0)
            {
                List<Formation> gone = null;
                foreach (KeyValuePair<Formation, Vec2> entry in WatchLast)
                {
                    Formation f = entry.Key;
                    if (f == null || f.CountOfUnits <= 0 || !(f.QuerySystem.IsCavalryFormation || f.QuerySystem.IsRangedCavalryFormation))
                    {
                        (gone ?? (gone = new List<Formation>())).Add(f);
                    }
                }
                if (gone != null)
                {
                    foreach (Formation f in gone)
                    {
                        WatchLast.Remove(f);
                        WatchHits.Remove(f);
                    }
                }
            }
        }

        /// <summary>Main thread: the member-weighted mean position of a side's teams (as TerrainBattlePosition's re-pick); false with nobody on it. Also TerrainPlanRefresh's.</summary>
        internal static bool ArmyCentre(Mission mission, BattleSideEnum side, out Vec2 centre)
        {
            centre = Vec2.Invalid;
            float x = 0f;
            float y = 0f;
            int men = 0;
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != side || team.QuerySystem == null)
                {
                    continue;
                }
                int count = team.QuerySystem.MemberCount;
                Vec2 p = team.QuerySystem.AveragePosition;
                if (count <= 0 || !p.IsValid)
                {
                    continue;
                }
                x += p.x * count;
                y += p.y * count;
                men += count;
            }
            if (men <= 0)
            {
                return false;
            }
            centre = new Vec2(x / men, y / men);
            return true;
        }

        private const float MaxReferenceHalf = 150f;

        /// <summary>Main thread: the other side's largest infantry (else largest formation), its cached position, facing (Formation.Direction, else toward our own largest) and half width. False with none.</summary>
        internal static bool EnemyReference(Mission mission, BattleSideEnum side, out Vec2 position, out Vec2 facing, out float halfFront)
        {
            position = Vec2.Invalid;
            facing = Vec2.Invalid;
            halfFront = 0f;
            if (mission == null)
            {
                return false;
            }
            Formation enemy = MainFormation(mission, side, false);
            if (enemy == null)
            {
                return false;
            }
            position = RBMAI.Utilities.GetFormationCenter(enemy);
            if (!position.IsValid)
            {
                return false;
            }
            facing = enemy.Direction;
            if (!facing.IsValid || facing.Normalize() < 0.001f)
            {
                Formation own = MainFormation(mission, side, true);
                Vec2 ownCentre = (own != null) ? RBMAI.Utilities.GetFormationCenter(own) : Vec2.Invalid;
                facing = ownCentre.IsValid ? ownCentre - position : Vec2.Invalid;
                if (!facing.IsValid || facing.Normalize() < 0.001f)
                {
                    facing = Vec2.Invalid;
                    return false;
                }
            }
            halfFront = Math.Min(MaxReferenceHalf, Math.Max(0f, enemy.Width * 0.5f));
            return true;
        }

        /// <summary>Main thread: the largest infantry formation (else the largest formation) of our side (own) or the other.</summary>
        internal static Formation MainFormation(Mission mission, BattleSideEnum side, bool own)
        {
            Formation infantry = null;
            Formation largest = null;
            foreach (Team team in mission.Teams)
            {
                if (team == null || (team.Side != BattleSideEnum.Attacker && team.Side != BattleSideEnum.Defender) || (team.Side == side) != own)
                {
                    continue;
                }
                foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (formation == null || formation.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    if (largest == null || formation.CountOfUnits > largest.CountOfUnits)
                    {
                        largest = formation;
                    }
                    if (formation.QuerySystem.IsInfantryFormation && (infantry == null || formation.CountOfUnits > infantry.CountOfUnits))
                    {
                        infantry = formation;
                    }
                }
            }
            return infantry ?? largest;
        }

        /// <summary>0 when either is not a direction.</summary>
        private static float AngleDegrees(Vec2 a, Vec2 b)
        {
            if (!a.IsValid || !b.IsValid || a.LengthSquared < 1e-6f || b.LengthSquared < 1e-6f)
            {
                return 0f;
            }
            float dot = Vec2.DotProduct(a.Normalized(), b.Normalized());
            return (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * 180.0 / Math.PI);
        }

        /// <summary>FLANKPLAN fields after the side (log text, any thread).</summary>
        internal static string Describe(SidePlan plan)
        {
            if (plan == null)
            {
                return "-";
            }
            return "applied=" + (RBMConfig.RBMConfig.terrainTacticsEnabled ? 1 : 0)
                + "\townLeft=" + (plan.OwnLeftCovered ? "covered" : "open") + "(" + plan.OwnLeftReason + ")"
                + "\townRight=" + (plan.OwnRightCovered ? "covered" : "open") + "(" + plan.OwnRightReason + ")"
                + "\tattackSide=" + SideName(plan.AttackSide) + "\treason=" + plan.AttackReason
                + "\tin=" + plan.Inputs
                + "\tv=" + plan.Version.ToString(CultureInfo.InvariantCulture) + "(" + plan.Note + ")";
        }

        private static string SideName(FormationAI.BehaviorSide side)
        {
            return (side == FormationAI.BehaviorSide.Left) ? "left" : (side == FormationAI.BehaviorSide.Right) ? "right" : "none";
        }

        private static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        // the helpers the tactics call (async AI thread)

        /// <summary>What a tactic asked these helpers for, before any plan (for RequestReapplyIfChanged).</summary>
        private const int UseGuardLeft = 1;
        private const int UseGuardRight = 2;
        private const int UseCavLeft = 4;
        private const int UseCavRight = 8;
        private const int UseCavLoneLeft = 16;
        private const int UseCavLoneRight = 32;
        private const int UseSkirmLeft = 64;
        private const int UseSkirmRight = 128;

        private const int LogGuardLeftSplit = 1;
        private const int LogGuardRightSplit = 2;
        private const int LogGuardLeftJoins = 4;
        private const int LogGuardRightJoins = 8;
        private const int LogCavLeftToRight = 16;
        private const int LogCavRightToLeft = 32;
        private const int LogSkirmLeft = 64;
        private const int LogSkirmRight = 128;

        private static readonly ConcurrentDictionary<TacticComponent, int> Uses = new ConcurrentDictionary<TacticComponent, int>();
        private static readonly ConcurrentDictionary<TacticComponent, int> Logged = new ConcurrentDictionary<TacticComponent, int>();
        private static readonly ConcurrentDictionary<TacticComponent, byte> ReapplyRequested = new ConcurrentDictionary<TacticComponent, byte>();
        /// <summary>The plan version (null = none yet) a tactic last made its recorded side choices with (Record).</summary>
        private static readonly ConcurrentDictionary<TacticComponent, SidePlan> PlanSeen = new ConcurrentDictionary<TacticComponent, SidePlan>();
        /// <summary>The plan version each team's decision last saw (FollowPlan).</summary>
        private static readonly ConcurrentDictionary<Team, SidePlan> TeamSeen = new ConcurrentDictionary<Team, SidePlan>();

        /// <summary>Native's protected flag; its own TickOccasionally re-applies the tactic when it is set.</summary>
        private static readonly AccessTools.FieldRef<TacticComponent, bool> IsTacticReapplyNeededRef = TryReapplyNeededRef();

        /// <summary>Null when a game update renamed the field (FieldRefAccess would throw and fail this class's type initialization); RequestReapplyIfChanged then skips the re-apply alone.</summary>
        private static AccessTools.FieldRef<TacticComponent, bool> TryReapplyNeededRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<TacticComponent, bool>("IsTacticReapplyNeeded");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A FLANKUSE line (TerrainLog).</summary>
        private static void EnqueueUse(Team team, string tactic, string text, bool reapply)
        {
            Mission mission = Mission.Current;
            TerrainLog.Enqueue(new TerrainLog.Event
            {
                Tag = TerrainLog.Tag.FlankUse,
                Team = team,
                Time = (mission != null) ? mission.CurrentTime : float.NaN,
                Extra = tactic,
                Why = text,
                Reapply = reapply
            });
        }

        /// <summary>Infantry flank guard on this own side: true when the plan says to leave it out (the side is covered). atSplit: the share stays in the main line; otherwise an existing guard gets the main line's behaviors. Records the request.</summary>
        internal static bool GuardSkipped(TacticComponent tactic, FormationAI.BehaviorSide side, bool atSplit)
        {
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || tactic == null)
            {
                return false;
            }
            bool left = side == FormationAI.BehaviorSide.Left;
            Record(tactic, left ? UseGuardLeft : UseGuardRight);
            SidePlan plan = ForTeam(tactic.Team);
            if (plan == null || !plan.IsCovered(side))
            {
                return false;
            }
            string name = left ? "guardLeft" : "guardRight";
            if (atSplit)
            {
                LogOnce(tactic, left ? LogGuardLeftSplit : LogGuardRightSplit,
                    name + "=skipped(covered " + plan.ReasonFor(side) + ") men stay in the main line");
            }
            else
            {
                LogOnce(tactic, left ? LogGuardLeftJoins : LogGuardRightJoins,
                    name + "=joinsMain(covered " + plan.ReasonFor(side) + ") formation kept, main line's behaviors");
            }
            return true;
        }

        /// <summary>The own side a cavalry flank guard goes to. wanted = the tactic's own choice; lone = no second cavalry formation (AttackSide preferred). A lone guard on a covered side with the other open moves there; with two guards each keeps its side. When the side changes the formation's AI.Side follows (native's guard reads AI.Side before FlankSide). Records the request.</summary>
        internal static FormationAI.BehaviorSide CavalrySide(TacticComponent tactic, Formation cavalry, FormationAI.BehaviorSide wanted, bool lone)
        {
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || tactic == null || cavalry == null)
            {
                return wanted;
            }
            bool wantedLeft = wanted == FormationAI.BehaviorSide.Left;
            if (!wantedLeft && wanted != FormationAI.BehaviorSide.Right)
            {
                return wanted;
            }
            Record(tactic, lone ? (wantedLeft ? UseCavLoneLeft : UseCavLoneRight) : (wantedLeft ? UseCavLeft : UseCavRight));
            SidePlan plan = ForTeam(tactic.Team);
            if (plan == null)
            {
                return wanted;
            }
            string why;
            FormationAI.BehaviorSide side = DecideCavalry(plan, wanted, lone, plan.AttackSide, out why);
            if (cavalry.AI.Side != side)
            {
                cavalry.AI.Side = side;
            }
            if (side != wanted)
            {
                LogOnce(tactic, wantedLeft ? LogCavLeftToRight : LogCavRightToLeft,
                    "cavalry " + wanted + "->" + side + "(" + why + ")" + (lone ? " lone" : ""));
            }
            return side;
        }

        /// <summary>The forward skirmishers' own side: AttackSide when the plan has one, else the tactic's own (random) choice. Records the request.</summary>
        internal static FormationAI.BehaviorSide SkirmishSide(TacticComponent tactic, FormationAI.BehaviorSide wanted)
        {
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || tactic == null)
            {
                return wanted;
            }
            Record(tactic, (wanted == FormationAI.BehaviorSide.Left) ? UseSkirmLeft : UseSkirmRight);
            SidePlan plan = ForTeam(tactic.Team);
            if (plan == null || !plan.HasAttackSide)
            {
                return wanted;
            }
            bool left = plan.AttackSide == FormationAI.BehaviorSide.Left;
            LogOnce(tactic, left ? LogSkirmLeft : LogSkirmRight,
                "skirmishSide=" + (left ? "left" : "right") + "(plan " + plan.AttackReason + ")" + ((plan.AttackSide != wanted) ? " was " + wanted.ToString().ToLowerInvariant() : " same"));
            return plan.AttackSide;
        }

        /// <summary>AI thread, from TacticSelection's terrain re-pick (tactic kept) and from FollowPlan on a new plan version: when the plan changes a choice this tactic recorded (vs the version it last made them with), sets its native IsTacticReapplyNeeded. Returns whether it did.</summary>
        internal static bool RequestReapplyIfChanged(TacticComponent tactic, Team team)
        {
            if (tactic == null || IsTacticReapplyNeededRef == null)
            {
                return false;
            }
            SidePlan plan = ForTeam(team);
            int uses;
            if (plan == null || !Uses.TryGetValue(tactic, out uses))
            {
                return false;
            }
            SidePlan seen;
            PlanSeen.TryGetValue(tactic, out seen);
            if (seen == plan || !WouldChange(seen, plan, uses))
            {
                return false;
            }
            ReapplyRequested[tactic] = 1;
            IsTacticReapplyNeededRef(tactic) = true;
            return true;
        }

        /// <summary>AI thread, TacticSelection's MakeDecision prefix (after the terrain re-pick): on the first decision that sees a NEW plan version of the team's side, asks the current tactic to re-apply when it changes one of its side choices; FLANKUSE "followPlan" line when it does. The first version a team sees is the publish's, handled by the re-pick / OnApply.</summary>
        internal static void FollowPlan(TacticComponent current, Team team)
        {
            if (team == null)
            {
                return;
            }
            SidePlan plan = ForTeam(team);
            if (plan == null)
            {
                return;
            }
            SidePlan seen;
            if (TeamSeen.TryGetValue(team, out seen) && seen == plan)
            {
                return;
            }
            TeamSeen[team] = plan;
            if (seen == null || current == null || !RequestReapplyIfChanged(current, team) || !RBMConfig.RBMConfig.battleTerrainLoggingEnabled)
            {
                return;
            }
            EnqueueUse(team, current.GetType().Name, "followPlan v" + seen.Version.ToString(CultureInfo.InvariantCulture) + "->v" + plan.Version.ToString(CultureInfo.InvariantCulture)
                + "(" + plan.Note + ") re-applied", true);
        }

        /// <summary>Does the plan change any recorded choice compared with seen (null = the tactic's own choices)? A plan without an AttackSide never changes an attack-side choice by itself, so a stale drop alone re-applies nothing.</summary>
        private static bool WouldChange(SidePlan seen, SidePlan plan, int uses)
        {
            if (((uses & UseGuardLeft) != 0 && Covered(seen, FormationAI.BehaviorSide.Left) != plan.OwnLeftCovered)
                || ((uses & UseGuardRight) != 0 && Covered(seen, FormationAI.BehaviorSide.Right) != plan.OwnRightCovered))
            {
                return true;
            }
            FormationAI.BehaviorSide seenAttack = (seen != null && seen.HasAttackSide) ? seen.AttackSide : FormationAI.BehaviorSide.BehaviorSideNotSet;
            FormationAI.BehaviorSide planAttack = plan.HasAttackSide ? plan.AttackSide : seenAttack;
            if (CavalryChanged(seen, plan, uses, UseCavLeft, FormationAI.BehaviorSide.Left, false, seenAttack, planAttack)
                || CavalryChanged(seen, plan, uses, UseCavRight, FormationAI.BehaviorSide.Right, false, seenAttack, planAttack)
                || CavalryChanged(seen, plan, uses, UseCavLoneLeft, FormationAI.BehaviorSide.Left, true, seenAttack, planAttack)
                || CavalryChanged(seen, plan, uses, UseCavLoneRight, FormationAI.BehaviorSide.Right, true, seenAttack, planAttack))
            {
                return true;
            }
            if (plan.HasAttackSide)
            {
                if (((uses & UseSkirmLeft) != 0 && plan.AttackSide != ((seenAttack != FormationAI.BehaviorSide.BehaviorSideNotSet) ? seenAttack : FormationAI.BehaviorSide.Left))
                    || ((uses & UseSkirmRight) != 0 && plan.AttackSide != ((seenAttack != FormationAI.BehaviorSide.BehaviorSideNotSet) ? seenAttack : FormationAI.BehaviorSide.Right)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Covered(SidePlan plan, FormationAI.BehaviorSide side)
        {
            return plan != null && plan.IsCovered(side);
        }

        private static bool CavalryChanged(SidePlan seen, SidePlan plan, int uses, int bit, FormationAI.BehaviorSide wanted, bool lone,
            FormationAI.BehaviorSide seenAttack, FormationAI.BehaviorSide planAttack)
        {
            if ((uses & bit) == 0)
            {
                return false;
            }
            string ignored;
            FormationAI.BehaviorSide before = (seen == null) ? wanted : DecideCavalry(seen, wanted, lone, seenAttack, out ignored);
            return DecideCavalry(plan, wanted, lone, planAttack, out ignored) != before;
        }

        /// <summary>attackSide = the plan's AttackSide (NotSet = none).</summary>
        private static FormationAI.BehaviorSide DecideCavalry(SidePlan plan, FormationAI.BehaviorSide wanted, bool lone, FormationAI.BehaviorSide attackSide, out string why)
        {
            FormationAI.BehaviorSide side = wanted;
            why = null;
            bool hasAttack = attackSide == FormationAI.BehaviorSide.Left || attackSide == FormationAI.BehaviorSide.Right;
            if (lone && hasAttack && attackSide != wanted)
            {
                side = attackSide;
                why = "attackSide " + plan.AttackReason;
            }
            FormationAI.BehaviorSide other = (side == FormationAI.BehaviorSide.Left) ? FormationAI.BehaviorSide.Right : FormationAI.BehaviorSide.Left;
            // with a second guard the open side already has one; moving this one there stacks both on one point
            if (lone && plan.IsCovered(side) && !plan.IsCovered(other))
            {
                why = "covered " + plan.ReasonFor(side);
                side = other;
            }
            return side;
        }

        /// <summary>Same thread as every other writer of the tactic's entry (its team's AI tick), so a plain read-modify-write. Also notes the plan version the choice is made with (PlanSeen).</summary>
        private static void Record(TacticComponent tactic, int bit)
        {
            SidePlan plan = ForTeam(tactic.Team);
            SidePlan seen;
            if (!PlanSeen.TryGetValue(tactic, out seen) || seen != plan)
            {
                PlanSeen[tactic] = plan;
            }
            int old;
            Uses.TryGetValue(tactic, out old);
            if ((old & bit) != bit)
            {
                Uses[tactic] = old | bit;
            }
        }

        private static void LogOnce(TacticComponent tactic, int bit, string text)
        {
            if (!RBMConfig.RBMConfig.battleTerrainLoggingEnabled)
            {
                return;
            }
            int old;
            Logged.TryGetValue(tactic, out old);
            if ((old & bit) != 0)
            {
                return;
            }
            Logged[tactic] = old | bit;
            EnqueueUse(tactic.Team, tactic.GetType().Name, text, ReapplyRequested.ContainsKey(tactic));
        }

        private static string Fmt(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
