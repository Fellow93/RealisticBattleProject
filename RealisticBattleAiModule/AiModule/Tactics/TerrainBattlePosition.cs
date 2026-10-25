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
    /// The defender's battle position: the place its infantry line holds, with archer place, cavalry reserve and infantry reserve
    /// plug point, for the defend tactics and RBMBehaviorHoldPosition. Scored on the terrain read's background task
    /// (BattleTerrainRead.BattlePosition.cs) and published here for the DEFENDER only (terrainTacticsEnabled; off = null = the
    /// old behaviors), with WorldPositions built on the main thread. The hold point must be on the navmesh and inside the
    /// boundary (else the next runner-up, else no plan); an archer, reserve or plug place failing that is dropped.
    /// RE-PICK (main thread, BattleTerrainLogic's tick): the same evaluation runs on a background task from the armies' current
    /// centres, published as the next immutable Plan version. Triggers: the attacker's bearing from the hold swung > 45 deg while
    /// it is beyond RepickFar; a TIMER (RepickPeriod, smaller change); a PULL-BACK (gap grew PullbackGap, attacker not closing and
    /// beyond RepickNear); RequestRepick (the choke bypass). A result that MOVES the hold is published only within the move budget
    /// (RepickMaxMoves, refilled by pull-backs up to RepickHardMaxMoves), with no main infantry in melee, the attacker beyond
    /// RepickNear, and the defender set up within MarginShare of the attacker's time (MoveBlocked); otherwise only the facing is
    /// re-evaluated (old hold re-scored, published via BuildFrom; WithFacing only when the old hold fails its navmesh check). A
    /// result at the same place and facing is not published unless it adds a plug point. A BYPASSED choke hold is never kept.
    /// THREADING: Build / TickRepick / Publish / Clear run on the MAIN thread (the re-pick's scoring on a background task over
    /// immutable grid data). The tactics and behavior read on the AI thread: volatile reads of an immutable plan, copies of cached
    /// WorldPositions, and a queue for BEHAVIOR events drained on the main thread. Cleared at mission end and in MissionStartReset
    /// (a running re-pick is abandoned by generation).
    /// </summary>
    internal static class TerrainBattlePosition
    {

        internal const float RepickFar = 300f; // re-pick only while the attacker's centre is farther than this from the hold

        internal const float RepickBearingCos = 0.707f; // cos 45 deg: bearing swing since the plan

        internal const float RepickMinSeconds = 20f; // between re-picks, and after a plan

        internal const float RepickPeriod = 30f; // TIMER: after the last plan / evaluation, a re-pick also runs on TimedMinMove or a ~15 deg swing (TimedSwingCos)

        internal const float TimedMinMove = 40f;

        internal const float TimedSwingCos = 0.966f; // cos 15 deg

        internal const int RepickMaxMoves = 2; // re-picks per battle that may MOVE the hold (> KeepHoldDistance); a pull-back refills it (PullbackRefill) up to RepickHardMaxMoves

        // The attacker's pull-back: the farther the armies, the more time the defender has to move and set up.

        internal const float PullbackGap = 60f; // gap grew this much over its smallest value since the last evaluation, attacker not closing: re-evaluate at once

        internal const float PullbackRefill = 100f; // a pull-back of this much refills the move budget by one

        internal const int RepickHardMaxMoves = 5; // per battle in all

        internal const float GapSampleSeconds = 10f; // "not closing" = gap now at least the sample 10-20 s old minus NotClosingSlack

        internal const float NotClosingSlack = 5f; // m

        internal const float RepickNear = 200f; // pull-back / bypass re-evaluation only with the attacker farther than this from the hold; no move published nearer

        internal const float SetupSeconds = 20f; // a MOVE is published only when the defender reaches the hold and sets up (this) ...

        internal const float MarginShare = 0.7f; // ... within this share of the attacker's time to it

        internal const float KeepHoldDistance = 10f; // m; a result this close with facing within KeepFacingCos is kept: nothing published

        internal const float KeepFacingCos = 0.966f;


        private static volatile Plan _defender;
        private static volatile bool _published;

        private static readonly ConcurrentQueue<UseEvent> UseEvents = new ConcurrentQueue<UseEvent>();

        // NeedsReapply: the plan each defend tactic last applied.
        private static readonly ConcurrentDictionary<TacticComponent, Plan> SeenBy = new ConcurrentDictionary<TacticComponent, Plan>();


        private static Task<BattleTerrain.BattlePositionResult> _repick;
        private static int _generation;
        private static int _repickGeneration;
        private static float _lastRepickAt = float.NegativeInfinity;
        private static string _repickReason;
        private static int _moves; // re-picks that moved the hold this battle
        private static int _moveBudget = RepickMaxMoves;
        private static string _requested; // from the AI thread (RequestRepick: choke bypass), taken by TickRepick; volatile
        private static float _minGap = float.NaN; // armies' gap: smallest since the last evaluation
        private static float _closestGap = float.NaN; // closest since the last refill (PullbackRefill)
        private static float _sampleAt = float.NegativeInfinity;
        private static float _sampleGap = float.NaN;
        private static float _prevSampleGap = float.NaN;
        // The attacker's centre and its bearing from the hold at the last evaluation (or the first check after the publish).
        private static bool _hasRef;
        private static Vec2 _refAttacker;
        private static Vec2 _refBearing;

        /// <summary>The defender's battle position. Immutable. Data = the worker's candidate that holds.</summary>
        internal sealed class Plan
        {
            public readonly BattleTerrain.BattlePositionResult Result;
            public readonly BattleTerrain.PositionCandidate Data;
            public readonly WorldPosition HoldWorld;
            public readonly WorldPosition ArcherWorld; // Invalid when HasArcher / HasReserve is false
            public readonly WorldPosition ReserveWorld;
            public readonly bool HasArcher;
            public readonly bool HasReserve;
            public readonly string ArcherDropped; // why dropped at the build ("outside", "noNavmesh"), else null
            public readonly string ReserveDropped;
            public readonly int Version; // 1 at the terrain publish, + 1 per re-pick
            public readonly Vec2 Bearing; // unit vector hold -> attacker's centre when planned (the re-pick's reference)
            public readonly float PlannedAt;
            public readonly bool Fallback; // the winner failed the navmesh check; this runner-up holds
            public readonly WorldPosition PlugWorld; // infantry reserve plug point; Invalid when HasPlug is false
            public readonly bool HasPlug;
            public readonly string PlugDropped;
            public readonly BattleTerrain.PositionCandidate PlugData; // describes PlugWorld (Data)

            private readonly Vec2 _facing; // facing-only re-pick after the cap (WithFacing); Invalid = Data's

            private readonly bool _stayKept; // still the line the defender stood on at the first plan (KeepStay)

            internal Plan(BattleTerrain.BattlePositionResult result, BattleTerrain.PositionCandidate data, WorldPosition holdWorld,
                WorldPosition archerWorld, bool hasArcher, string archerDropped, WorldPosition reserveWorld, bool hasReserve, string reserveDropped,
                WorldPosition plugWorld, bool hasPlug, string plugDropped, int version, Vec2 bearing, float plannedAt, bool fallback)
                : this(result, data, holdWorld, archerWorld, hasArcher, archerDropped, reserveWorld, hasReserve, reserveDropped,
                      plugWorld, hasPlug, plugDropped, data, version, bearing, plannedAt, fallback, Vec2.Invalid, false)
            {
            }

            private Plan(BattleTerrain.BattlePositionResult result, BattleTerrain.PositionCandidate data, WorldPosition holdWorld,
                WorldPosition archerWorld, bool hasArcher, string archerDropped, WorldPosition reserveWorld, bool hasReserve, string reserveDropped,
                WorldPosition plugWorld, bool hasPlug, string plugDropped, BattleTerrain.PositionCandidate plugData, int version, Vec2 bearing,
                float plannedAt, bool fallback, Vec2 facing, bool stayKept)
            {
                _facing = facing;
                _stayKept = stayKept;
                PlugData = plugData ?? data;
                Result = result;
                Data = data;
                HoldWorld = holdWorld;
                ArcherWorld = archerWorld;
                HasArcher = hasArcher;
                ArcherDropped = archerDropped;
                ReserveWorld = reserveWorld;
                HasReserve = hasReserve;
                ReserveDropped = reserveDropped;
                PlugWorld = plugWorld;
                HasPlug = hasPlug;
                PlugDropped = plugDropped;
                Version = version;
                Bearing = bearing;
                PlannedAt = plannedAt;
                Fallback = fallback;
            }

            public Vec2 Hold => Data.Hold;

            public Vec2 Facing => _facing.IsValid ? _facing : Data.Facing;

            /// <summary>Main thread: same plan facing another way as the next version (facing-only re-pick after the cap).</summary>
            internal Plan WithFacing(Vec2 facing, int version, Vec2 bearing, float plannedAt)
            {
                return new Plan(Result, Data, HoldWorld, ArcherWorld, HasArcher, ArcherDropped, ReserveWorld, HasReserve, ReserveDropped,
                    PlugWorld, HasPlug, PlugDropped, PlugData, version, bearing, plannedAt, Fallback, facing, _stayKept);
            }

            /// <summary>Main thread: same plan, still the line the defender stood on (a re-scored "previous" of a stay plan).</summary>
            internal Plan KeepStay()
            {
                return new Plan(Result, Data, HoldWorld, ArcherWorld, HasArcher, ArcherDropped, ReserveWorld, HasReserve, ReserveDropped,
                    PlugWorld, HasPlug, PlugDropped, PlugData, Version, Bearing, PlannedAt, Fallback, _facing, true);
            }

            public float LineWidth => Data.LineWidth;

            public bool IsChoke => Data.IsChoke;

            public bool IsStay => Data.Kind == BattleTerrain.PositionKind.Current || _stayKept;

            public bool BothFlanksCovered => Data.LeftCovered && Data.RightCovered;

            public string KindName => BattleTerrain.PositionKindName(Data.Kind);
        }

        /// <summary>The team's battle position: null with terrainTacticsEnabled off, before the publish, for an attacker, or without a plan. Any thread.</summary>
        public static Plan PlanFor(Team team)
        {
            // None for a defender without terrain plans (ArmyScale.NoPlans): it holds where it is.
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || team == null || team.Side != BattleSideEnum.Defender || ArmyScale.NoPlans(team))
            {
                return null;
            }
            return _defender;
        }

        /// <summary>Whatever the toggle (log and map).</summary>
        internal static Plan Published => _defender;

        /// <summary>Published, whether or not the defender has a plan. Any thread.</summary>
        public static bool IsPublished => _published;

        /// <summary>Both line ends covered and a reserve place exists. Any thread.</summary>
        internal static bool ReserveWanted(Team team)
        {
            Plan plan = PlanFor(team);
            return plan != null && plan.BothFlanksCovered && plan.HasReserve;
        }

        /// <summary>AI thread: true once each time the team's plan differs from the one the tactic last saw (the publish lands after its first application). False with no plan ever.</summary>
        internal static bool NeedsReapply(TacticComponent tactic)
        {
            if (tactic == null)
            {
                return false;
            }
            Plan plan = PlanFor(tactic.Team);
            Plan seen;
            SeenBy.TryGetValue(tactic, out seen);
            if (seen == plan)
            {
                return false;
            }
            SeenBy[tactic] = plan;
            return true;
        }

        /// <summary>Main thread: the terrain publish and each re-pick.</summary>
        internal static void Publish(Plan defender)
        {
            _defender = defender;
            _published = true;
        }

        /// <summary>Main thread: mission end (BattleTerrainLogic) and MissionStartReset. Drops the queued events and abandons a running re-pick.</summary>
        public static void Clear()
        {
            _defender = null;
            _published = false;
            UseEvent ignored;
            while (UseEvents.TryDequeue(out ignored))
            {
            }
            SeenBy.Clear();
            _generation++;
            if (_repick != null)
            {
                // Observe a late fault so it is never reported as an unobserved task exception.
                _repick.ContinueWith(t => { var unused = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                _repick = null;
            }
            _lastRepickAt = float.NegativeInfinity;
            _repickReason = null;
            _moves = 0;
            _moveBudget = RepickMaxMoves;
            System.Threading.Volatile.Write(ref _requested, null);
            _minGap = float.NaN;
            _closestGap = float.NaN;
            _sampleAt = float.NegativeInfinity;
            _sampleGap = float.NaN;
            _prevSampleGap = float.NaN;
            _hasRef = false;
        }

        /// <summary>Choke bypass: the next TickRepick re-evaluates at once without the choke hold, +1 move budget. Any thread.</summary>
        internal static void RequestRepick(string reason)
        {
            System.Threading.Volatile.Write(ref _requested, reason ?? "-");
        }

        // build (main thread)

        /// <summary>MAIN thread: the plan for an evaluation (the winner, else the first runner-up whose hold point is usable), or null (why says why).</summary>
        internal static Plan Build(Mission mission, BattleTerrain terrain, BattleTerrain.BattlePositionResult result, int version, float now, out string why)
        {
            why = null;
            if (mission == null || mission.Scene == null || terrain == null || result == null)
            {
                why = "noTerrain";
                return null;
            }
            if (result.Winner == null)
            {
                why = result.Why ?? "noWinner";
                return null;
            }
            List<BattleTerrain.PositionCandidate> order = new List<BattleTerrain.PositionCandidate> { result.Winner };
            order.AddRange(result.RunnersUp);
            string firstWhy = null;
            foreach (BattleTerrain.PositionCandidate c in order)
            {
                string holdWhy;
                Plan plan = BuildFrom(mission, terrain, result, c, version, now, c != result.Winner, out holdWhy);
                if (plan != null)
                {
                    return plan;
                }
                if (firstWhy == null)
                {
                    firstWhy = holdWhy;
                }
            }
            why = "hold:" + (firstWhy ?? "-");
            return null;
        }

        /// <summary>MAIN thread: the plan for one candidate (hold, archer place, cavalry reserve and plug point as navmesh-checked WorldPositions), or null when its hold is unusable (why: "outside" / "noHeight" / "noNavmesh").</summary>
        internal static Plan BuildFrom(Mission mission, BattleTerrain terrain, BattleTerrain.BattlePositionResult result, BattleTerrain.PositionCandidate c,
            int version, float now, bool fallback, out string why)
        {
            string holdWhy;
            WorldPosition hold = MakeWorld(mission, terrain, c.Hold, out holdWhy);
            if (holdWhy != null)
            {
                why = holdWhy;
                return null;
            }
            why = null;
            string archerWhy = null;
            WorldPosition archer = WorldPosition.Invalid;
            if (c.Archer.IsValid)
            {
                archer = MakeWorld(mission, terrain, c.Archer, out archerWhy);
            }
            string reserveWhy = null;
            WorldPosition reserve = WorldPosition.Invalid;
            if (c.Reserve.IsValid)
            {
                reserve = MakeWorld(mission, terrain, c.Reserve, out reserveWhy);
            }
            // The infantry reserve plug is placed for the winner (and a re-pick's old hold) only: a runner-up has none.
            string plugWhy = null;
            WorldPosition plug = WorldPosition.Invalid;
            if (c.Plug.IsValid)
            {
                plug = MakeWorld(mission, terrain, c.Plug, out plugWhy);
            }
            Vec2 bearing = result.AttackerAt.IsValid ? result.AttackerAt - c.Hold : Vec2.Invalid;
            bearing = (bearing.IsValid && bearing.LengthSquared > 1f) ? bearing.Normalized() : c.Facing;
            return new Plan(result, c, hold, archer, c.Archer.IsValid && archerWhy == null, archerWhy,
                reserve, c.Reserve.IsValid && reserveWhy == null, reserveWhy,
                plug, c.Plug.IsValid && plugWhy == null, plugWhy, version, bearing, now, fallback);
        }

        /// <summary>Main thread: a WorldPosition at p on the grid's height; why = "outside" / "noHeight" / "noNavmesh" when unusable. Shared by the terrain plans' builds.</summary>
        internal static WorldPosition MakeWorld(Mission mission, BattleTerrain terrain, Vec2 p, out string why)
        {
            why = null;
            if (!mission.IsPositionInsideBoundaries(p))
            {
                why = "outside";
                return WorldPosition.Invalid;
            }
            float h = terrain.SampleHeight(p);
            if (float.IsNaN(h) || float.IsInfinity(h))
            {
                h = terrain.HeightAt(p);
            }
            if (float.IsNaN(h) || float.IsInfinity(h))
            {
                why = "noHeight";
                return WorldPosition.Invalid;
            }
            WorldPosition position = new WorldPosition(mission.Scene, UIntPtr.Zero, new Vec3(p, h), false);
            if (position.GetNavMesh() == UIntPtr.Zero)
            {
                why = "noNavmesh";
                return WorldPosition.Invalid;
            }
            return position;
        }

        // re-pick (main thread)

        /// <summary>MAIN thread, every tick after the publish: finishes a running re-pick (builds and publishes the next version, returns its BATTLEPOS repick line's fields) or starts one when due. Null when nothing is to be logged.</summary>
        internal static string TickRepick(Mission mission, BattleTerrain terrain, float now)
        {
            // No re-pick work for a defender without terrain plans (PlanFor reads null for it).
            if (mission == null || terrain == null || ArmyScale.NoPlans(BattleSideEnum.Defender))
            {
                return null;
            }
            Plan plan = _defender;
            BattleTerrain.BattlePositionInfo info = terrain.BattlePosition;
            if (_repick != null)
            {
                if (!_repick.IsCompleted)
                {
                    return null;
                }
                Task<BattleTerrain.BattlePositionResult> task = _repick;
                _repick = null;
                if (_repickGeneration != _generation)
                {
                    return null;
                }
                if (task.IsFaulted || task.IsCanceled)
                {
                    Exception e = (task.Exception != null) ? task.Exception.GetBaseException() : new OperationCanceledException("re-pick canceled");
                    return "reason=" + (_repickReason ?? "-") + "\tfailed=" + e.GetType().Name + ":" + BattleTerrainRead.Sanitize(e.Message);
                }
                BattleTerrain.BattlePositionResult result = task.Result;
                string why;
                Plan next = Build(mission, terrain, result, (plan != null) ? plan.Version + 1 : 1, now, out why);
                string outcome = "";
                // A move is published only within the budget, out of melee, with the attacker not near and the defender set up in time (MoveBlocked).
                string blocked = null;
                if (plan != null && next != null && next.Hold.Distance(plan.Hold) > KeepHoldDistance)
                {
                    blocked = MoveBlocked(mission, next, plan);
                }
                else if (plan != null && next == null && _moves >= _moveBudget)
                {
                    blocked = "capped " + _moves.ToString(CultureInfo.InvariantCulture) + "/" + _moveBudget.ToString(CultureInfo.InvariantCulture);
                }
                if (plan != null && blocked != null)
                {
                    // BLOCKED (capped / melee / near / late): the hold does not move; only its facing is re-evaluated. The re-scored old hold
                    // (result.Previous) is published as a plan of its own (BuildFrom) so line width, flanks, archer place, reserve and plug
                    // follow the new facing. A BYPASSED choke hold is never kept: the current line instead (result.Current).
                    bool chokeDropped = plan.IsChoke && TerrainChokeHold.IsBypassed;
                    BattleTerrain.PositionCandidate oldHold = chokeDropped ? (result.Current ?? result.Previous) : result.Previous;
                    Vec2 facing = (oldHold != null && oldHold.Facing.IsValid && oldHold.Facing.LengthSquared > 0.5f) ? oldHold.Facing : Vec2.Invalid;
                    string winnerText = (next != null) ? " winner " + Fmt0(next.Hold.Distance(plan.Hold)) + "m away" : " no winner";
                    next = null;
                    if (!facing.IsValid)
                    {
                        why = "capped";
                        outcome = "\tpublished=0(" + blocked + winnerText + ", no facing for the hold)";
                    }
                    else
                    {
                        float capTurn = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vec2.DotProduct(facing, plan.Facing)))) * 180.0 / Math.PI);
                        // The old hold's plug placed again for that facing: published on its own when the plan had none.
                        bool plugNew = !plan.HasPlug && oldHold.Plug.IsValid;
                        if (Vec2.DotProduct(facing, plan.Facing) >= KeepFacingCos && !chokeDropped && !plugNew)
                        {
                            why = "capped";
                            outcome = "\tpublished=0(" + blocked + winnerText + ", facing " + Fmt0(capTurn) + "deg kept)";
                        }
                        else
                        {
                            string rebuildWhy;
                            Plan rebuilt = BuildFrom(mission, terrain, result, oldHold, plan.Version + 1, now, false, out rebuildWhy);
                            if (rebuilt != null)
                            {
                                if (plan.IsStay && oldHold.Kind == BattleTerrain.PositionKind.Previous)
                                {
                                    rebuilt = rebuilt.KeepStay();
                                }
                                next = rebuilt;
                                outcome = "\tpublished=1(" + blocked + " facing " + Fmt0(capTurn) + "deg, "
                                    + (chokeDropped ? "bypassed choke dropped for the current line " + Fmt0(oldHold.Hold.Distance(plan.Hold)) + "m" : "hold stays") + ";"
                                    + winnerText + ", re-placed archer=" + (rebuilt.HasArcher ? Fmt0(rebuilt.Data.Archer.Distance(rebuilt.Hold)) + "m" : "-")
                                    + " reserve=" + (rebuilt.HasReserve ? Fmt0(rebuilt.Data.Reserve.Distance(rebuilt.Hold)) + "m" : "-")
                                    + " plug=" + (rebuilt.HasPlug ? (oldHold.PlugTier ?? "-") : "-") + " line=" + Fmt0(rebuilt.LineWidth) + "m)";
                            }
                            else
                            {
                                // The old hold failed its navmesh check this time: the facing alone, the places kept.
                                Vec2 capBearing = result.AttackerAt.IsValid ? result.AttackerAt - plan.Hold : Vec2.Invalid;
                                capBearing = (capBearing.IsValid && capBearing.LengthSquared > 1f) ? capBearing.Normalized() : facing;
                                next = plan.WithFacing(facing, plan.Version + 1, capBearing, now);
                                outcome = "\tpublished=1(" + blocked + " facing " + Fmt0(capTurn) + "deg, hold stays;" + winnerText
                                    + ", places kept: hold " + (rebuildWhy ?? "-") + ")";
                            }
                        }
                    }
                    if (next != null)
                    {
                        Publish(next);
                    }
                    return "reason=" + (_repickReason ?? "-") + "\t" + Describe(info, result, next, why) + outcome;
                }
                if (next != null && plan != null)
                {
                    if (plan.IsStay && next.Data.Kind == BattleTerrain.PositionKind.Previous)
                    {
                        // The re-scored old hold of a stay plan is still the line the defender stood on.
                        next = next.KeepStay();
                    }
                    float shift = next.Hold.Distance(plan.Hold);
                    float turn = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vec2.DotProduct(next.Facing, plan.Facing)))) * 180.0 / Math.PI);
                    if (shift <= KeepHoldDistance && Vec2.DotProduct(next.Facing, plan.Facing) >= KeepFacingCos)
                    {
                        if (!plan.HasPlug && next.HasPlug)
                        {
                            // The same place, but the plan had no plug and this evaluation has one: published for the plug.
                            outcome = "\tpublished=1(plug placed " + (next.PlugData.PlugTier ?? "-") + ": hold " + Fmt0(shift) + "m facing " + Fmt0(turn) + "deg)";
                        }
                        else if (plan.IsChoke && TerrainChokeHold.IsBypassed && !next.IsChoke)
                        {
                            // A bypassed choke hold is never kept, even at the same place.
                            outcome = "\tpublished=1(bypassed choke dropped: hold " + Fmt0(shift) + "m facing " + Fmt0(turn) + "deg)";
                        }
                        else
                        {
                            // The same place facing the same way: nothing is published (no re-apply churn every timed check).
                            outcome = "\tpublished=0(kept: hold " + Fmt0(shift) + "m facing " + Fmt0(turn) + "deg)";
                            next = null;
                            why = "kept";
                        }
                    }
                    else if (shift > KeepHoldDistance)
                    {
                        _moves++;
                        outcome = "\tpublished=1(move " + _moves.ToString(CultureInfo.InvariantCulture) + "/" + _moveBudget.ToString(CultureInfo.InvariantCulture)
                            + " hold " + Fmt0(shift) + "m facing " + Fmt0(turn) + "deg " + MarginText(next) + ")";
                    }
                    else
                    {
                        outcome = "\tpublished=1(facing " + Fmt0(turn) + "deg, no march)";
                    }
                }
                if (next != null)
                {
                    Publish(next);
                }
                return "reason=" + (_repickReason ?? "-") + "\t" + Describe(info, result, next, why) + outcome;
            }
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || plan == null || info == null || info.Ground == null || info.Armies == null)
            {
                return null;
            }
            Vec2 attacker;
            Vec2 defender;
            if (!ArmyCentre(mission, BattleSideEnum.Attacker, out attacker) || !ArmyCentre(mission, BattleSideEnum.Defender, out defender))
            {
                return null;
            }
            // The armies' gap, every tick: its smallest value since the last evaluation and a sample every GapSampleSeconds.
            float gap = attacker.Distance(defender);
            if (float.IsNaN(_minGap) || gap < _minGap)
            {
                _minGap = gap;
            }
            if (float.IsNaN(_closestGap) || gap < _closestGap)
            {
                _closestGap = gap;
            }
            if (now - _sampleAt >= GapSampleSeconds)
            {
                _prevSampleGap = _sampleGap;
                _sampleGap = gap;
                _sampleAt = now;
            }
            if (!_hasRef)
            {
                // The reference: where the attacker stood at the first check after the publish, the plan's bearing.
                _refAttacker = attacker;
                _refBearing = plan.Bearing;
                _minGap = gap;
                _hasRef = true;
                return null;
            }
            Vec2 bearing = attacker - plan.Hold;
            float distance = bearing.Normalize();
            // PULL-BACK: the gap grew PullbackGap over its smallest value since the last evaluation and the attacker is not closing.
            float pullback = gap - _minGap;
            bool notClosing = !float.IsNaN(_prevSampleGap) && gap >= _prevSampleGap - NotClosingSlack;
            if (gap - _closestGap >= PullbackRefill && notClosing && _moves >= _moveBudget && _moveBudget < RepickHardMaxMoves)
            {
                // A real pull-back (PullbackRefill) gives the defender time again: one more move (up to RepickHardMaxMoves).
                _moveBudget = Math.Min(RepickHardMaxMoves, _moves + 1);
                _closestGap = gap;
            }
            string request = System.Threading.Volatile.Read(ref _requested);
            string reason = null;
            if (request != null)
            {
                // The choke bypass (RequestRepick): at once, against the attacker where it stands now; one more move.
                System.Threading.Interlocked.Exchange(ref _requested, null);
                if (_moves >= _moveBudget && _moveBudget < RepickHardMaxMoves)
                {
                    _moveBudget = Math.Min(RepickHardMaxMoves, _moves + 1);
                }
                reason = request;
            }
            else
            {
                if (now - _lastRepickAt < RepickMinSeconds || now - plan.PlannedAt < RepickMinSeconds)
                {
                    return null;
                }
                // Against the last evaluation (not the plan: a "kept" result publishes nothing but still moves the reference).
                float dot = Vec2.DotProduct(bearing, _refBearing);
                float swing = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * 180.0 / Math.PI);
                float moved = attacker.Distance(_refAttacker);
                if (pullback >= PullbackGap && notClosing && distance > RepickNear)
                {
                    reason = "pullback gap +" + Fmt0(pullback) + "m (min " + Fmt0(_minGap) + "m now " + Fmt0(gap) + "m, " + Fmt0(_prevSampleGap) + "m 10-20s ago)";
                }
                else if (distance <= RepickFar)
                {
                    return null;
                }
                else if (dot < RepickBearingCos)
                {
                    reason = "swing " + Fmt0(swing) + "deg";
                }
                else if (now - Math.Max(_lastRepickAt, plan.PlannedAt) >= RepickPeriod && (moved > TimedMinMove || dot < TimedSwingCos))
                {
                    // TIMER: every RepickPeriod s, a smaller change since the last evaluation is enough.
                    reason = "timer moved " + Fmt0(moved) + "m swing " + Fmt0(swing) + "deg";
                }
                else
                {
                    return null;
                }
            }
            _lastRepickAt = now;
            _refAttacker = attacker;
            _refBearing = bearing;
            _minGap = gap;
            _repickReason = reason + " atk " + Fmt0(distance) + "m moves " + _moves.ToString(CultureInfo.InvariantCulture) + "/"
                + _moveBudget.ToString(CultureInfo.InvariantCulture) + " (v" + plan.Version.ToString(CultureInfo.InvariantCulture) + ")";
            BattleTerrainRead.PositionGround ground = info.Ground;
            BattleTerrainRead.PositionArmies armies = info.Armies.MovedTo(ground.Grid, defender, attacker);
            float ratio;
            string ratioSource;
            if (CurrentOutnumbered(mission, out ratio, out ratioSource))
            {
                armies = armies.WithOutnumbered(ratio, ratioSource);
            }
            bool skipChoke = TerrainChokeHold.IsBypassed;
            // A bypassed choke hold is no reference a move must beat: the current line is.
            Vec2 previous = (skipChoke && plan.IsChoke) ? Vec2.Invalid : plan.Hold;
            _repickGeneration = _generation;
            // Background: the same evaluation over the immutable grid / snapshot (no engine call), published next tick(s).
            _repick = Task.Run(() => BattleTerrainRead.EvaluateBattlePosition(ground, armies, previous, skipChoke));
            return null;
        }

        /// <summary>Main thread, a re-pick's result that would MOVE the hold: why it may not ("capped 2/2", "melee fN:Class", "near atk 180m", "late def 50s+20s > 0.7x atk 80s"), else null. The defender must reach the new hold and set up within MarginShare of the attacker's time; never while a main formation fights or the attacker is within RepickNear.</summary>
        private static string MoveBlocked(Mission mission, Plan next, Plan plan)
        {
            if (_moves >= _moveBudget)
            {
                return "capped " + _moves.ToString(CultureInfo.InvariantCulture) + "/" + _moveBudget.ToString(CultureInfo.InvariantCulture);
            }
            Vec2 attacker;
            if (ArmyCentre(mission, BattleSideEnum.Attacker, out attacker))
            {
                float near = Math.Min(attacker.Distance(plan.Hold), attacker.Distance(next.Hold));
                if (near < RepickNear)
                {
                    return "near atk " + Fmt0(near) + "m";
                }
            }
            string melee = MainInMelee(mission);
            if (melee != null)
            {
                return "melee " + melee;
            }
            float def = next.Data.DefenderSeconds;
            float atk = next.Data.AttackerSeconds;
            if (!float.IsNaN(def) && !float.IsInfinity(def) && !float.IsNaN(atk) && !float.IsInfinity(atk) && def + SetupSeconds > MarginShare * atk)
            {
                return "late " + MarginText(next);
            }
            return null;
        }

        /// <summary>"def 34s+20s vs 0.7x atk 140s=98s": the time margin of a move.</summary>
        private static string MarginText(Plan next)
        {
            float def = next.Data.DefenderSeconds;
            float atk = next.Data.AttackerSeconds;
            return "def " + Fmt0(def) + "s+" + Fmt0(SetupSeconds) + "s vs " + Fmt1(MarginShare) + "x atk " + Fmt0(atk) + "s=" + Fmt0(MarginShare * atk) + "s";
        }

        /// <summary>Main thread: a side's main infantry (its largest infantry formation) at least HoldLateCharge.ContactMeleeShare in melee: its name, else null.</summary>
        private static string MainInMelee(Mission mission)
        {
            foreach (Team team in mission.Teams)
            {
                if (team == null)
                {
                    continue;
                }
                Formation main = null;
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f != null && f.CountOfUnits > 0 && f.QuerySystem.IsInfantryFormation && (main == null || f.CountOfUnits > main.CountOfUnits))
                    {
                        main = f;
                    }
                }
                if (main != null && HoldLateCharge.MeleeShare(main) >= HoldLateCharge.ContactMeleeShare)
                {
                    return team.Side + ":" + TerrainCavalry.FormationName(main);
                }
            }
            return null;
        }

        /// <summary>Main thread: attacker / defender strength now = the men on the field's GetPower plus the men still to spawn at their mean; false (the latched ratio stays) when a side has nobody on the field.</summary>
        private static bool CurrentOutnumbered(Mission mission, out float ratio, out string source)
        {
            ratio = float.NaN;
            source = "-";
            double atkPower = 0.0, defPower = 0.0;
            int atkMen = 0, defMen = 0;
            foreach (Agent agent in mission.Agents)
            {
                if (agent == null || !agent.IsActive() || !agent.IsHuman || agent.Team == null || agent.Character == null)
                {
                    continue;
                }
                if (agent.Team.Side == BattleSideEnum.Attacker)
                {
                    atkPower += agent.Character.GetPower();
                    atkMen++;
                }
                else if (agent.Team.Side == BattleSideEnum.Defender)
                {
                    defPower += agent.Character.GetPower();
                    defMen++;
                }
            }
            if (atkMen == 0 || defMen == 0)
            {
                return false;
            }
            IBattleMissionAgentSpawnLogic spawn = mission.GetMissionBehavior<IBattleMissionAgentSpawnLogic>();
            int atkLeft = (spawn != null && spawn.AttackerActivePhase != null) ? Math.Max(0, spawn.AttackerActivePhase.RemainingSpawnNumber) : 0;
            int defLeft = (spawn != null && spawn.DefenderActivePhase != null) ? Math.Max(0, spawn.DefenderActivePhase.RemainingSpawnNumber) : 0;
            float atk = (float)(atkPower * (atkMen + atkLeft) / atkMen);
            float def = (float)(defPower * (defMen + defLeft) / defMen);
            ratio = BattleTerrainRead.OutnumberRatio(atk, def, atkMen + atkLeft, defMen + defLeft, "now", out source);
            return !float.IsNaN(ratio);
        }

        /// <summary>Main thread: the member-weighted mean position of a side's teams; false with nobody on it.</summary>
        private static bool ArmyCentre(Mission mission, BattleSideEnum side, out Vec2 centre)
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
                if (count <= 0)
                {
                    continue;
                }
                Vec2 p = team.QuerySystem.AveragePosition;
                if (!p.IsValid)
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

        // behavior events (AI thread writes, main thread drains)

        /// <summary>HoldPosition / ReservePlug state change or engage. Immutable.</summary>
        internal sealed class UseEvent
        {
            public Team Team;
            public int FormationIndex;
            public float Time;
            public string Behavior;
            public string What;
            public string Point;
            public string Why;
        }

        /// <summary>AI thread; only with the terrain log on.</summary>
        internal static void Enqueue(Team team, Formation formation, float time, string what, string point, string why)
        {
            Enqueue(team, formation, time, "HoldPosition", what, point, why);
        }

        /// <summary>AI thread; only with the terrain log on.</summary>
        internal static void Enqueue(Team team, Formation formation, float time, string behavior, string what, string point, string why)
        {
            DecisionTrace.Note(formation, behavior, what, why);
            if (!RBMConfig.RBMConfig.battleTerrainLoggingEnabled || team == null)
            {
                return;
            }
            UseEvents.Enqueue(new UseEvent
            {
                Team = team,
                FormationIndex = (formation != null) ? (int)formation.FormationIndex : -1,
                Time = time,
                Behavior = behavior,
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

        /// <summary>The BATTLEPOS line's fields after the side: applied, the armies, the winner and its terms, the runners-up, the plan.</summary>
        internal static string Describe(BattleTerrain.BattlePositionInfo info, BattleTerrain.BattlePositionResult result, Plan plan, string buildWhy)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("applied=").Append(RBMConfig.RBMConfig.terrainTacticsEnabled ? 1 : 0);
            if (info != null && info.Armies != null)
            {
                BattleTerrainRead.PositionArmies a = info.Armies;
                sb.Append("\tin=inf ").Append(a.Infantry.ToString(CultureInfo.InvariantCulture))
                  .Append(" front ").Append(Fmt0(a.InfantryFrontage)).Append("m atkFront ").Append(Fmt0(a.AttackerFrontage))
                  .Append("m ranged ").Append(Fmt2(a.RangedShare)).Append(" enemyMounted ").Append(Fmt2(a.EnemyMountedShare))
                  .Append(" pace def ").Append(Fmt1(a.DefenderSpeed)).Append(" atk ").Append(Fmt1(a.AttackerSpeed));
            }
            if (result == null)
            {
                sb.Append("\twinner=-\twhy=").Append((info != null && info.Why != null) ? info.Why : "notBuilt");
                return sb.ToString();
            }
            if (result.Weights != null)
            {
                sb.Append('\t').Append(result.Weights.Describe());
            }
            sb.Append("\tarmies=def ").Append(Point(result.DefenderAt)).Append(" atk ").Append(Point(result.AttackerAt));
            BattleTerrain.PositionCandidate w = (plan != null) ? plan.Data : result.Winner;
            if (w == null)
            {
                sb.Append("\twinner=-\twhy=").Append(result.Why ?? "-");
            }
            else
            {
                sb.Append("\twinner=").Append(CandidateName(w)).Append(" at=").Append(Point(w.Hold))
                  .Append(" facing=").Append(Fmt2(w.Facing.x)).Append(',').Append(Fmt2(w.Facing.y))
                  .Append(" span=").Append(Fmt0(w.Span)).Append("m(").Append(EndName(w.LeftEnd)).Append('/').Append(EndName(w.RightEnd)).Append(')')
                  .Append(" line=").Append(Fmt0(w.LineWidth)).Append('m')
                  .Append(" flanks=").Append(w.LeftCovered ? w.LeftCover : "open").Append('/').Append(w.RightCovered ? w.RightCover : "open")
                  .Append(" reach=def ").Append(Fmt0(w.DefenderSeconds)).Append("s atk ").Append(Fmt0(w.AttackerSeconds)).Append("s walk ").Append(Fmt0(w.Distance)).Append('m');
                if (plan != null && plan.Fallback)
                {
                    sb.Append(" fallback(winner ").Append(CandidateName(result.Winner)).Append(" unusable)");
                }
                sb.Append("\tterms=").Append(TermsText(w));
                sb.Append("\ttotal=").Append(Fmt2(w.Total));
                sb.Append("\tcurrent=").Append((result.Current != null) ? Fmt2(result.Current.Total) : "-");
                if (result.Previous != null)
                {
                    sb.Append("\tprevious=").Append(Fmt2(result.Previous.Total)).Append(" at ").Append(Point(result.Previous.Hold));
                }
                sb.Append("\tgain=").Append(Fmt2(result.Gain)).Append(" (min ").Append(Fmt2((result.Weights != null) ? result.Weights.MinGain : BattleTerrainRead.PosMinGain)).Append(')');
                sb.Append("\tarcher=");
                if (!w.Archer.IsValid)
                {
                    sb.Append('-');
                }
                else
                {
                    sb.Append(w.ArcherSource ?? "-").Append(' ').Append(Point(w.Archer)).Append(" gain=").Append(Fmt1(w.ArcherGain)).Append('m');
                    if (plan != null && !plan.HasArcher)
                    {
                        sb.Append(" dropped=").Append(plan.ArcherDropped ?? "-");
                    }
                }
                sb.Append("\treserve=");
                if (!w.Reserve.IsValid)
                {
                    sb.Append('-');
                }
                else
                {
                    sb.Append(Point(w.Reserve));
                    if (plan != null && !plan.HasReserve)
                    {
                        sb.Append(" dropped=").Append(plan.ReserveDropped ?? "-");
                    }
                }
                sb.Append("\talt=");
                if (result.RunnersUp.Count == 0)
                {
                    sb.Append('-');
                }
                for (int i = 0; i < result.RunnersUp.Count; i++)
                {
                    BattleTerrain.PositionCandidate r = result.RunnersUp[i];
                    if (i > 0)
                    {
                        sb.Append(' ');
                    }
                    sb.Append(CandidateName(r)).Append('@').Append(Point(r.Hold)).Append(':').Append(Fmt2(r.Total));
                }
            }
            sb.Append("\tcand=").Append(result.Generated.ToString(CultureInfo.InvariantCulture))
              .Append(" scored=").Append(result.Scored.ToString(CultureInfo.InvariantCulture))
              .Append(" rej=").Append(result.Rejects ?? "-");
            sb.Append("\tplan=").Append((plan != null) ? "published v" + plan.Version.ToString(CultureInfo.InvariantCulture) : "none(" + (buildWhy ?? "-") + ")");
            sb.Append("\texpanded=").Append(result.Expanded.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tms=").Append(Fmt1(result.Ms));
            if (info != null && info.Error != null)
            {
                sb.Append("\terror=").Append(info.Error);
            }
            return sb.ToString();
        }

        /// <summary>The RESERVE line's fields (the infantry reserve plug of the plan's position): applied, the point, the fallback tier, hidden, distances, score, scan counts and whether its WorldPosition was built.</summary>
        internal static string DescribePlug(BattleTerrain.BattlePositionResult result, Plan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("applied=").Append(RBMConfig.RBMConfig.terrainTacticsEnabled ? 1 : 0);
            BattleTerrain.PositionCandidate c = (plan != null) ? plan.PlugData : (result != null ? result.Winner : null);
            if (c == null)
            {
                sb.Append("\tplug=-\twhy=noPosition");
                return sb.ToString();
            }
            sb.Append("\tposition=").Append(CandidateName(c)).Append(" at=").Append(Point(c.Hold)).Append(" line=").Append(Fmt0(c.LineWidth)).Append('m');
            if (plan != null && plan.PlugData != plan.Data)
            {
                sb.Append(" facing=").Append(Fmt2(c.Facing.x)).Append(',').Append(Fmt2(c.Facing.y)).Append("(re-placed)");
            }
            if (!c.Plug.IsValid)
            {
                sb.Append("\tplug=-\twhy=noCell (follows the line)");
            }
            else
            {
                sb.Append("\tplug=").Append(Point(c.Plug))
                  .Append("\ttier=").Append(c.PlugTier ?? "-")
                  .Append("\thidden=").Append(c.PlugHidden ? 1 : 0)
                  .Append("\tdLine=").Append(Fmt0(c.PlugLineDistance)).Append('m')
                  .Append("\treach=").Append(Fmt1(c.PlugReach)).Append("s(max ").Append(Fmt0(BattleTerrainRead.PlugReachSeconds)).Append(')')
                  .Append("\tdrop=").Append(Signed1(c.PlugDrop)).Append('m')
                  .Append("\tscore=").Append(Fmt2(c.PlugScore));
            }
            sb.Append("\tcells=").Append(c.PlugCandidates.ToString(CultureInfo.InvariantCulture))
              .Append(" hidden=").Append(c.PlugHiddenCells.ToString(CultureInfo.InvariantCulture))
              .Append(" eyes=").Append(c.PlugEyes.ToString(CultureInfo.InvariantCulture))
              .Append(" rej=").Append(c.PlugRejects ?? "-")
              .Append(" tiers=").Append(c.PlugTiers ?? "-");
            sb.Append("\tplan=");
            if (plan == null)
            {
                sb.Append("none");
            }
            else
            {
                sb.Append('v').Append(plan.Version.ToString(CultureInfo.InvariantCulture));
                if (c.Plug.IsValid)
                {
                    sb.Append(plan.HasPlug ? " usable" : " dropped=" + (plan.PlugDropped ?? "-"));
                }
            }
            return sb.ToString();
        }

        /// <summary>"hill(hill2)", "lattice", "current".</summary>
        internal static string CandidateName(BattleTerrain.PositionCandidate c)
        {
            if (c == null)
            {
                return "-";
            }
            string kind = BattleTerrain.PositionKindName(c.Kind);
            return (c.Kind == BattleTerrain.PositionKind.Hill || c.Kind == BattleTerrain.PositionKind.Crest
                || c.Kind == BattleTerrain.PositionKind.ReverseSlope || c.Kind == BattleTerrain.PositionKind.Choke)
                ? kind + "(" + (c.Source ?? "-") + ")"
                : kind;
        }

        /// <summary>Each term's raw value with its measurement: "h=0.42(+6.3m) front=0.2(span 60m) ...".</summary>
        private static string TermsText(BattleTerrain.PositionCandidate c)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("h=").Append(Fmt2(c.TermHeight)).Append('(').Append(Signed1(c.HeightAdvantage)).Append("m)");
            sb.Append(" front=").Append(Fmt2(c.TermFront)).Append("(span ").Append(Fmt0(c.Span)).Append("m)");
            sb.Append(" flanks=").Append(Fmt2(c.TermFlanks));
            sb.Append(" fire=").Append(Fmt2(c.TermFire));
            sb.Append(" approach=").Append(Fmt2(c.TermApproach)).Append("(slow ").Append(Fmt2(c.SlowShare)).Append(')');
            sb.Append(" cav=").Append(Fmt2(c.TermCavalry)).Append("(bad ").Append(Fmt2(c.BadShare)).Append(" runup ").Append(Fmt0(c.RunUp)).Append("m)");
            sb.Append(" dead=").Append(Fmt2(c.TermDead));
            sb.Append(" march=").Append(Fmt2(c.TermMarch));
            sb.Append(" caught=").Append(Fmt2(c.TermCaught));
            return sb.ToString();
        }

        private static string EndName(BattleTerrain.End end)
        {
            return end.ToString().ToLowerInvariant();
        }

        private static string Point(Vec2 p)
        {
            return p.IsValid ? Fmt0(p.x) + "," + Fmt0(p.y) : "-";
        }

        private static string Signed1(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "-";
            }
            return (value >= 0f ? "+" : "") + value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        internal static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Fmt1(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        internal static string Fmt2(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
