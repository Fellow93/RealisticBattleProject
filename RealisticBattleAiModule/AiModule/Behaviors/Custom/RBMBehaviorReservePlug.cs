using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The infantry reserve of RBMTacticReservePlug (defender): it waits out of sight behind the battle position's line at the plan's plug point
    /// (TerrainBattlePosition.Plan.PlugWorld: hidden from the attacker's approach, close enough to reach either line end) and COMMITS (plugs) into the
    /// fight when the line needs it. Weight 0 unless the tactic sets it (TryAssign); LiveWeight while it waits or is committed, 0 once Done, so the
    /// tactic's fallback (BehaviorCharge) takes over after the plug.
    ///
    /// States: Wait -> Moving -> Holding (within ArriveDistance + a quarter depth; Moving again beyond LeaveDistance or on a new plan version) ->
    /// Committed -> Done. While waiting: Move to the plug point facing the plan's facing, Line, a custom width WaitRanks deep. FOLLOW: without a
    /// usable plug point, or before contact while the line's front-centre is more than FollowAwayDistance from the plan's hold (back to the plug within
    /// FollowBackDistance), or while the line faces more than ~45 deg off the plan's ("lineTurned", back within ~30 deg) - after contact the mode no
    /// longer switches - it walks FollowGap behind its line's rear edge facing the line's way (FollowTarget: shorter gaps / either side on walkable ground
    /// off the edge; a copy of the line's cached median with ONE SetVec2MT, rebuilt past FollowRebuildDistance after FollowRebuildSeconds). With no line to
    /// follow it holds where it stands (MovementOrderStop), still watching for the commit. A point overlapping a friend's destination by more than
    /// TakeMaxShare is moved ONCE along the facing (PlaceShift). Cohesion (Kind.Reserve): the (shifted) point stays within ReserveLimit of the line,
    /// pulled toward it otherwise (Pull, cached).
    ///
    /// COMMIT, the first of (checked every tick on the AI thread; the line = the formation the tactic named):
    ///  - selfThreat: an enemy FOOT formation significant to the reserve (ThreatSignificance) within SelfThreatDistance (edge gap): that one;
    ///  - flankTurn: such a formation within TurnDistance of a line end, beyond the line's front plane and out at the end (>= TurnEndShare of the half width off the middle): that one;
    ///  BRACE: the same two by significant enemy MELEE cavalry do not commit - the reserve stops facing the riders (shield wall when eligible, else Line; facing follows
    ///  them only past BraceTurnCos) until none is within BraceKeepDistance / turning an end, then goes back to its point; BEHAVIOR "brace" / "unbrace";
    ///  - power: the line >= MeleeRatio in melee and its power ratio below PlugPowerRatio. Native LocalPowerRatio's formula sqrt((ours + 1) / (theirs + 1)) over cached
    ///    FormationPower of the line and enemy formations within ContactGap (native's own runs an engine proximity query: not on the AI thread);
    ///  - casualties: after contact, the line lost more than PlugCasualty of its most men (native's CasualtyRatio writes CasualtyHandler's dictionary: not from the AI thread);
    ///  - timeout: PlugMaxWait s after contact (the line ContactMeleeRatio in melee with enemy FOOT significant to the LINE within ContactGap, or such a formation
    ///    within ContactStartGap; bait / riders start no clock), only with an enemy in contact to go for. The clock resets after ContactResetSeconds with neither;
    ///  - lineGone: the line has no men left.
    /// Target: the trigger's own formation for selfThreat / flankTurn; otherwise the LARGEST enemy FOOT formation in contact with the line (within ContactGap; cavalry only
    /// when no foot; no horse archers), else (not for the timeout) the nearest non-horse-archer enemy, foot first. ChargeToTarget until the target is gone, CommitMaxSeconds,
    /// or the target mounted / retreating and more than TargetAwayGap from the line for TargetAwaySeconds ("targetAway"), then Done (normal Charge via the fallback).
    ///
    /// Each state change, the commit and Done is a BEHAVIOR line (TerrainBattlePosition.Enqueue, behavior "ReservePlug").
    ///
    /// THREADING: CalculateCurrentOrder / TickOccasionally / GetAiWeight run on the async AI thread: immutable published plan, cached formation data and query systems,
    /// a copy of the plan's cached WorldPosition (shifted: one SetVec2MT on a fresh copy, cached); no engine query, no WorldPosition construction, no MBRandom. StateName is a volatile string.
    /// </summary>
    internal class RBMBehaviorReservePlug : BehaviorComponent
    {
        internal enum PlugState
        {
            Wait,
            Moving,
            Holding,
            Committed,
            Done
        }


        // Gaps are edge gaps in m; the class comment says how each is used.
        private const float LiveWeight = 20f;
        private const float ArriveDistance = 15f;
        private const float LeaveDistance = 35f;
        private const float WaitRanks = 4f;
        private const float MinWidth = 10f; // m
        private const float WidthRefreshSeconds = 5f;
        internal const float PlugPowerRatio = 0.8f; // native LocalPowerRatio's formula, while in melee
        internal const float PlugCasualty = 0.25f; // share of the line's most men lost since the assignment, after contact
        internal const float PlugMaxWait = 45f; // s after contact
        internal const float TurnDistance = 40f; // to a line end's end point, beyond the front plane
        private const float TurnEndShare = 0.8f; // of the half width off the line's middle
        internal const float SelfThreatDistance = 50f;
        private const float ContactGap = 30f; // to the line's segment
        private const float ContactMeleeRatio = 0.1f;
        private const float ContactStartGap = 15f;
        private const float MeleeRatio = 0.15f; // the power trigger needs this melee share of the line
        private const float CommitMaxSeconds = 90f;
        private const float ContactResetSeconds = 10f;
        private const float BraceKeepDistance = 80f;
        private const float BraceTurnCos = 0.866f; // facing follows the riders past ~30 deg
        internal const float FollowGap = 40f; // front behind the line's rear edge
        private static readonly float[] FollowGaps = { FollowGap, 25f, 12f }; // shorter gaps tried when not walkable / near the edge
        private static readonly float[] FollowAcross = { 0f, -0.5f, 0.5f }; // shares of the line's half width
        private const float FollowEdgeMin = 15f; // m from the map edge
        internal const float FollowAwayDistance = 60f; // follow while the line's front-centre is this far from the plan's hold ...
        internal const float FollowBackDistance = 40f; // ... back to the plug within this
        internal const float FollowTurnCos = 0.707f; // follow while the line's facing is ~45 deg off the plan's ...
        internal const float FollowTurnBackCos = 0.866f; // ... back within ~30 deg
        internal const float TargetAwayGap = 60f; // mounted / retreating committed target this far from the line ...
        internal const float TargetAwaySeconds = 10f; // ... this long: Done
        private const float FollowRebuildDistance = 5f; // m, and FollowRebuildSeconds
        private const float FollowRebuildSeconds = 2f;

        private TacticComponent _owner;
        private int _ownerGeneration = -1;
        private Formation _line;
        private TerrainBattlePosition.Plan _plan;
        private int _planVersion;
        private PlugState _state = PlugState.Wait;
        private float _stateSince = float.NaN;
        private int _linePeak;
        private float _contactSince = float.NaN;
        /// <summary>For the contact clock's reset. AI thread.</summary>
        private float _lastContactAt = float.NaN;
        /// <summary>Enemy cavalry braced against (null = not braced). AI thread.</summary>
        private Formation _braced;
        private Vec2 _braceFacing = Vec2.Invalid;
        private float _braceSince = float.NaN;
        private Formation _target;
        private float _committedAt = float.NaN;
        /// <summary>TargetAway clock. AI thread.</summary>
        private float _awaySince = float.NaN;
        private bool _shaped;
        private float _widthAt = float.NegativeInfinity;
        private float _widthSet = float.NaN;
        private volatile bool _committed;
        /// <summary>AI thread.</summary>
        private readonly FriendlyOverlap.PlaceShift _shift = new FriendlyOverlap.PlaceShift();
        /// <summary>Kept within FormationCohesion.ReserveLimit of the line. AI thread.</summary>
        private readonly FormationCohesion.Pull _cohesion = new FormationCohesion.Pull();
        private volatile string _stateName = "wait";
        /// <summary>FOLLOW mode: why, else null. AI thread.</summary>
        private string _followWhy;
        /// <summary>Cached follow point (copy of the line's cached median, ONE SetVec2MT) and the facing built with it. AI thread.</summary>
        private readonly CachedMovePoint _follow = new CachedMovePoint();
        private Vec2 _followFacing = Vec2.Invalid;
        private string _followNote;

        public RBMBehaviorReservePlug(Formation formation)
            : base(formation)
        {
            _behaviorSide = formation.AI.Side;
            base.CurrentOrder = MovementOrder.MovementOrderStop;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        }

        /// <summary>Map view text ("holding", "committed flankTurn", ...). Any thread.</summary>
        internal string StateName => _stateName;

        /// <summary>AI thread, from RBMTacticReservePlug (weight 1; LiveWeight until Done). Works without a plan (it follows its line).</summary>
        internal static bool TryAssign(Formation formation, TacticComponent owner, int generation, Formation line)
        {
            if (formation == null || formation.AI == null)
            {
                return false;
            }
            RBMBehaviorReservePlug behavior = formation.AI.GetBehavior<RBMBehaviorReservePlug>();
            if (behavior == null)
            {
                return false;
            }
            formation.AI.SetBehaviorWeight<RBMBehaviorReservePlug>(1f);
            behavior.Assign(owner, generation, line);
            return true;
        }

        /// <summary>AI thread: the reserve has committed (or is done).</summary>
        internal static bool HasCommitted(Formation formation, TacticComponent owner)
        {
            if (formation == null || formation.AI == null || owner == null)
            {
                return false;
            }
            RBMBehaviorReservePlug behavior = formation.AI.GetBehavior<RBMBehaviorReservePlug>();
            return behavior != null && behavior._owner == owner && behavior._committed;
        }

        /// <summary>AI thread, from the tactic: a new instance or generation restarts from Wait; the same keeps its state and only follows a new line.</summary>
        internal void Assign(TacticComponent owner, int generation, Formation line)
        {
            if (owner == _owner && generation == _ownerGeneration)
            {
                if (line != null && line != _line)
                {
                    _line = line;
                    _linePeak = line.CountOfUnits;
                }
                return;
            }
            _owner = owner;
            _ownerGeneration = generation;
            _line = line;
            _linePeak = (line != null) ? line.CountOfUnits : 0;
            _plan = null;
            _planVersion = 0;
            _contactSince = float.NaN;
            _lastContactAt = float.NaN;
            _braced = null;
            _braceFacing = Vec2.Invalid;
            _braceSince = float.NaN;
            _target = null;
            _committedAt = float.NaN;
            _awaySince = float.NaN;
            _committed = false;
            _shaped = false;
            _widthAt = float.NegativeInfinity;
            _widthSet = float.NaN;
            _shift.Reset();
            _cohesion.Reset();
            ResetFollow();
            SetState(PlugState.Wait, float.NaN, null);
        }

        private void ResetFollow()
        {
            _followWhy = null;
            _follow.Reset();
            _followFacing = Vec2.Invalid;
            _followNote = null;
        }

        public override void TickOccasionally()
        {
            CheckCommit();
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder(CurrentFacingOrder);
            ApplyShape();
        }

        protected override void OnBehaviorActivatedAux()
        {
            _shaped = false;
            _widthSet = float.NaN;
            _widthAt = float.NegativeInfinity;
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder(CurrentFacingOrder);
            base.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            ApplyShape();
        }

        protected override void CalculateCurrentOrder()
        {
            Mission mission = Mission.Current;
            Formation formation = base.Formation;
            if (mission == null || formation == null || formation.CountOfUnits == 0 || formation.Team == null)
            {
                return;
            }
            float now = mission.CurrentTime;
            if (_state == PlugState.Committed || _state == PlugState.Done)
            {
                Formation target = _target;
                base.CurrentOrder = (target != null && target.CountOfUnits > 0) ? MovementOrder.MovementOrderChargeToTarget(target) : MovementOrder.MovementOrderCharge;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            if (_braced != null && _braceFacing.IsValid)
            {
                // braced against riders: stand where it is, facing them
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(_braceFacing);
                _stateName = "braced";
                return;
            }
            TerrainBattlePosition.Plan plan = TerrainBattlePosition.PlanFor(formation.Team);
            _plan = plan;
            // FOLLOW: without a usable plug point, or while the line is far from the plan's hold before contact, walk FollowGap behind the line (FollowTarget)
            string followWhy = FollowReason(plan);
            bool modeChanged = (followWhy != null) != (_followWhy != null);
            _followWhy = followWhy;
            bool following = followWhy != null;
            string pointName = following ? "follow" : "plug";
            WorldPosition moveTo;
            Vec2 facing;
            string where;
            if (following)
            {
                if (!FollowTarget(formation, plan, now, out moveTo, out facing))
                {
                    // nothing to follow: hold where it stands, still ready to commit
                    if (_state != PlugState.Holding || _stateName != "holding (no point)")
                    {
                        SetState(PlugState.Holding, now, "noFollow " + followWhy);
                        _stateName = "holding (no point)";
                    }
                    base.CurrentOrder = MovementOrder.MovementOrderStop;
                    CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                    return;
                }
                where = "follow " + (_followNote ?? "-");
            }
            else
            {
                moveTo = plan.PlugWorld;
                facing = plan.Facing;
                BattleTerrain.PositionCandidate plugData = plan.PlugData;
                where = (plugData.PlugTier ?? "-") + (plugData.PlugHidden ? " hidden" : " seen") + " dLine=" + TerrainBattlePosition.Fmt0(plugData.PlugLineDistance) + "m";
            }
            if (modeChanged)
            {
                TerrainBattlePosition.Enqueue(formation.Team, formation, now, "ReservePlug", pointName, pointName,
                    following ? followWhy + " (" + where + ")" : "line at the hold, to the plug v" + plan.Version.ToString(CultureInfo.InvariantCulture) + " " + where);
            }
            // user rule: the point moved once along the facing off a friend standing there (PlaceShift)
            Vec2 point;
            string shiftWhat;
            string shiftWhy;
            WorldPosition place = _shift.Resolve(formation, moveTo, facing, out point, out shiftWhat, out shiftWhy,
                FormationCohesion.Kind.Reserve, _line);
            if (shiftWhat != null)
            {
                TerrainBattlePosition.Enqueue(formation.Team, formation, now, "ReservePlug", shiftWhat, pointName, shiftWhy);
            }
            // user rule: the reserve stays within FormationCohesion.ReserveLimit of its line (Pull)
            place = _cohesion.Resolve(formation, place, facing, FormationCohesion.Kind.Reserve, _line, out point, out shiftWhat, out shiftWhy);
            if (shiftWhat != null)
            {
                TerrainBattlePosition.Enqueue(formation.Team, formation, now, "ReservePlug", shiftWhat, pointName, shiftWhy);
            }
            Vec2 position = RBMAI.Utilities.GetFormationCenter(formation);
            if (position.IsValid)
            {
                float distance = position.Distance(point);
                float arrive = ArriveDistance + formation.Depth * 0.25f;
                if (modeChanged && _state != PlugState.Wait && _stateName != "holding (no point)")
                {
                    // follow <-> plug: the state follows the new point
                    PlugState next = (distance <= arrive) ? PlugState.Holding : PlugState.Moving;
                    if (next != _state)
                    {
                        SetState(next, now, (following ? "follow" : "toPlug") + " d=" + TerrainBattlePosition.Fmt0(distance) + "m " + where);
                    }
                }
                else if (!following && _state != PlugState.Wait && _planVersion != 0 && plan.Version != _planVersion && distance > arrive)
                {
                    SetState(PlugState.Moving, now, "replan v" + _planVersion.ToString(CultureInfo.InvariantCulture)
                        + "->v" + plan.Version.ToString(CultureInfo.InvariantCulture) + " d=" + TerrainBattlePosition.Fmt0(distance) + "m " + where);
                }
                else if (_state == PlugState.Wait || _stateName == "holding (no point)")
                {
                    SetState(distance <= arrive ? PlugState.Holding : PlugState.Moving, now,
                        (distance <= arrive ? "atPoint" : "toPoint") + " d=" + TerrainBattlePosition.Fmt0(distance) + "m " + where);
                }
                else if (_state == PlugState.Moving && distance <= arrive)
                {
                    SetState(PlugState.Holding, now, "arrived after " + TerrainBattlePosition.Fmt0(now - _stateSince) + "s " + where);
                }
                else if (_state == PlugState.Holding && distance > LeaveDistance + formation.Depth * 0.25f)
                {
                    SetState(PlugState.Moving, now, "offPoint d=" + TerrainBattlePosition.Fmt0(distance) + "m " + pointName);
                }
                else if (_state == PlugState.Moving)
                {
                    _stateName = "moving " + TerrainBattlePosition.Fmt0(distance) + "m";
                }
                if (following)
                {
                    _stateName = (_state == PlugState.Moving) ? "follow " + TerrainBattlePosition.Fmt0(distance) + "m" : "follow";
                }
                if (plan != null)
                {
                    _planVersion = plan.Version;
                }
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(place);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(facing);
        }

        /// <summary>AI thread: why it follows its line instead of going to the plug point, or null. After contact the mode stays as it is.</summary>
        private string FollowReason(TerrainBattlePosition.Plan plan)
        {
            if (plan == null)
            {
                return "noPlan";
            }
            if (!plan.HasPlug)
            {
                return "noPoint " + (plan.PlugDropped ?? "noCell");
            }
            Formation line = _line;
            if (line == null || line.CountOfUnits <= 0)
            {
                return null;
            }
            string current = _followWhy;
            if (!float.IsNaN(_contactSince) || RBMAI.Utilities.FormationFightingInMelee(line, ContactMeleeRatio))
            {
                return current;
            }
            Vec2 front;
            Vec2 facing;
            if (!LineFront(line, plan, out front, out facing))
            {
                return current;
            }
            float d = front.Distance(plan.Hold);
            bool awayBefore = current != null && current.StartsWith("lineAway");
            bool away = awayBefore ? d > FollowBackDistance : d > FollowAwayDistance;
            if (away)
            {
                return "lineAway " + TerrainBattlePosition.Fmt0(d) + "m from the hold v" + plan.Version.ToString(CultureInfo.InvariantCulture);
            }
            // the line turned in place (HoldReface): the plug point may now lie beside or in front of it - follow behind the line as it faces now (back at FollowTurnBackCos)
            Vec2 planFacing = plan.Facing;
            if (!planFacing.IsValid || planFacing.LengthSquared < 0.01f)
            {
                return null;
            }
            float dot = Vec2.DotProduct(facing, planFacing.Normalized());
            bool turnedBefore = current != null && current.StartsWith("lineTurned");
            bool turned = turnedBefore ? dot < FollowTurnBackCos : dot < FollowTurnCos;
            return turned
                ? "lineTurned " + TerrainBattlePosition.Fmt0((float)(System.Math.Acos(MBMath.ClampFloat(dot, -1f, 1f)) * 180.0 / System.Math.PI)) + "deg off the plan's facing v"
                    + plan.Version.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        /// <summary>AI thread: facing is its Direction, else the plan's; false without a line.</summary>
        private static bool LineFront(Formation line, TerrainBattlePosition.Plan plan, out Vec2 front, out Vec2 facing)
        {
            front = Vec2.Invalid;
            facing = line.Direction;
            if (!facing.IsValid || facing.LengthSquared < 0.01f)
            {
                facing = (plan != null) ? plan.Facing : Vec2.Invalid;
            }
            if (!facing.IsValid || facing.LengthSquared < 0.01f)
            {
                return false;
            }
            facing = facing.Normalized();
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(line);
            if (!centre.IsValid)
            {
                return false;
            }
            front = centre + facing * (line.Depth * 0.5f);
            return true;
        }

        /// <summary>AI thread, FOLLOW: Move point (front-centre) = line front-centre - (depth + gap) along its facing; the first of FollowGaps x FollowAcross on walkable ground (none = shortest gap unchecked). Copy of the line's cached median with ONE SetVec2MT. False without a line or its geometry.</summary>
        private bool FollowTarget(Formation formation, TerrainBattlePosition.Plan plan, float now, out WorldPosition target, out Vec2 facing)
        {
            target = WorldPosition.Invalid;
            facing = Vec2.Invalid;
            Formation line = _line;
            if (line == null || line == formation || line.CountOfUnits <= 0)
            {
                return false;
            }
            Vec2 front;
            Vec2 f;
            if (!LineFront(line, plan, out front, out f))
            {
                return false;
            }
            WorldPosition source = line.CachedMedianPosition;
            if (!source.AsVec2.IsValid)
            {
                return false;
            }
            BattleTerrain terrain = BattleTerrain.Current;
            Vec2 across = f.LeftVec();
            float half = MathF.Max(line.Width, 1f) * 0.5f;
            Vec2 wanted = Vec2.Invalid;
            string note = null;
            foreach (float gap in FollowGaps)
            {
                foreach (float share in FollowAcross)
                {
                    Vec2 p = front - f * (line.Depth + gap) + across * (share * half);
                    if (terrain == null || (terrain.IsWalkable(p) && terrain.EdgeDistanceAt(p) >= FollowEdgeMin))
                    {
                        wanted = p;
                        note = TerrainBattlePosition.Fmt0(gap) + "m" + ((share == 0f) ? "" : (share > 0f ? " left" : " right"));
                        break;
                    }
                }
                if (wanted.IsValid)
                {
                    break;
                }
            }
            if (!wanted.IsValid)
            {
                float last = FollowGaps[FollowGaps.Length - 1];
                wanted = front - f * (line.Depth + last);
                note = TerrainBattlePosition.Fmt0(last) + "m unchecked";
            }
            bool rebuilt;
            _follow.TryGet(wanted, now, source, false, FollowRebuildDistance, FollowRebuildSeconds, out target, out rebuilt);
            if (rebuilt)
            {
                _followFacing = f;
                _followNote = note + " behind " + TerrainCavalry.FormationName(line);
            }
            facing = _followFacing;
            return true;
        }

        private void CheckCommit()
        {
            Mission mission = Mission.Current;
            Formation formation = base.Formation;
            if (mission == null || formation == null || formation.Team == null || formation.CountOfUnits == 0 || _owner == null)
            {
                return;
            }
            float now = mission.CurrentTime;
            if (_state == PlugState.Done)
            {
                return;
            }
            if (_state == PlugState.Committed)
            {
                Formation target = _target;
                if (target == null || target.CountOfUnits <= 0)
                {
                    SetState(PlugState.Done, now, "targetGone after " + TerrainBattlePosition.Fmt0(now - _committedAt) + "s");
                }
                else if (now - _committedAt >= CommitMaxSeconds)
                {
                    SetState(PlugState.Done, now, "timeout " + TerrainBattlePosition.Fmt0(CommitMaxSeconds) + "s");
                }
                else
                {
                    // no chase after riders or routers
                    string away = TargetAway(target, _line, formation);
                    if (away == null)
                    {
                        _awaySince = float.NaN;
                    }
                    else if (float.IsNaN(_awaySince))
                    {
                        _awaySince = now;
                    }
                    else if (now - _awaySince >= TargetAwaySeconds)
                    {
                        SetState(PlugState.Done, now, "targetAway " + away + " for " + TerrainBattlePosition.Fmt0(now - _awaySince) + "s after "
                            + TerrainBattlePosition.Fmt0(now - _committedAt) + "s");
                    }
                }
                return;
            }
            Formation line = _line;
            Formation enemy;
            string why = CommitReason(mission, formation, line, now, out enemy);
            if (why == null)
            {
                return;
            }
            _braced = null;
            if (enemy == null)
            {
                enemy = PressingEnemy(mission, formation, line);
            }
            _target = enemy;
            _committedAt = now;
            _committed = true;
            SetState(PlugState.Committed, now, why + " target=" + ((enemy != null) ? TerrainCavalry.FormationName(enemy) + " men=" + enemy.CountOfUnits.ToString(CultureInfo.InvariantCulture) : "-(charge)"));
            _stateName = "committed " + why.Split(' ')[0];
            if (formation.IsAIControlled)
            {
                formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
                formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            }
        }

        /// <summary>AI thread. enemy = the trigger's formation, when it names one.</summary>
        private string CommitReason(Mission mission, Formation formation, Formation line, float now, out Formation enemy)
        {
            enemy = null;
            if (line == null || line.CountOfUnits <= 0)
            {
                return "lineGone";
            }
            if (line.CountOfUnits > _linePeak)
            {
                _linePeak = line.CountOfUnits;
            }
            Vec2 lineCentre = RBMAI.Utilities.GetFormationCenter(line);
            Vec2 own = RBMAI.Utilities.GetFormationCenter(formation);
            if (!lineCentre.IsValid || !own.IsValid)
            {
                return null;
            }
            Vec2 facing = line.Direction;
            if (!facing.IsValid || facing.LengthSquared < 0.01f)
            {
                facing = (_plan != null) ? _plan.Facing : new Vec2(0f, 1f);
            }
            facing = facing.Normalized();
            Vec2 lineDir = new Vec2(-facing.y, facing.x);
            float halfWidth = MathF.Max(line.Width, 1f) * 0.5f;
            float halfDepth = line.Depth * 0.5f;

            float contactPower = 0f;
            float nearestGap = float.MaxValue;
            Formation nearest = null;
            // riders to brace against (selfThreat / flankTurn by melee cavalry: no commit)
            Formation brace = null;
            float braceGap = float.MaxValue;
            string braceWhy = null;
            Footprint ownShape;
            if (!FormationGeometry.TryGet(formation, FootprintMode.Current, out ownShape))
            {
                return null;
            }
            Footprint lineShape = Footprint.Box(lineCentre, facing, halfWidth, MathF.Max(line.Depth, 1f) * 0.5f);
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (e == null || e.CountOfUnits <= 0 || e.QuerySystem.IsRangedCavalryFormation)
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(e);
                    Footprint shape;
                    if (!c.IsValid || !FormationGeometry.TryGet(e, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    bool mounted = e.QuerySystem.IsCavalryFormation;
                    // only a significant enemy (relative to the reserve) makes it commit on its own
                    bool significant = ThreatSignificance.IsSignificant(formation, e);
                    float selfGap = FormationGeometry.Gap(ownShape, shape);
                    if (significant && selfGap <= SelfThreatDistance)
                    {
                        if (!mounted)
                        {
                            enemy = e;
                            return "selfThreat " + ThreatSignificance.Describe(formation, e) + " gap=" + FormationGeometry.GapText(selfGap);
                        }
                        if (selfGap < braceGap)
                        {
                            brace = e;
                            braceGap = selfGap;
                            braceWhy = "selfThreat " + ThreatSignificance.Describe(formation, e) + " gap=" + FormationGeometry.GapText(selfGap);
                        }
                    }
                    else if (mounted && e == _braced && selfGap <= BraceKeepDistance && selfGap < braceGap)
                    {
                        // the riders it faces, still near: stay braced
                        brace = e;
                        braceGap = selfGap;
                        braceWhy = "keep gap=" + FormationGeometry.GapText(selfGap);
                    }
                    Vec2 rel = c - lineCentre;
                    float along = Vec2.DotProduct(rel, lineDir);
                    float front = Vec2.DotProduct(rel, facing);
                    float lineGap = FormationGeometry.Gap(lineShape, shape);
                    // contact (the timeout clock): only enemy FOOT significant to the LINE
                    if (!mounted && lineGap < nearestGap && ThreatSignificance.IsSignificant(line, e))
                    {
                        nearestGap = lineGap;
                        nearest = e;
                    }
                    if (lineGap <= ContactGap)
                    {
                        contactPower += e.QuerySystem.FormationPower;
                    }
                    // turning a line end: out at the end, level with or behind the front edge, near the end point
                    if (significant && MathF.Abs(along) >= halfWidth * TurnEndShare && front <= halfDepth)
                    {
                        Vec2 end = lineCentre + lineDir * (along >= 0f ? halfWidth : -halfWidth);
                        float endGap = FormationGeometry.GapToPoint(shape, end);
                        if (endGap <= TurnDistance)
                        {
                            string turn = "flankTurn " + ThreatSignificance.Describe(formation, e) + " " + (along >= 0f ? "left" : "right") + "End gap="
                                + FormationGeometry.GapText(endGap);
                            if (!mounted)
                            {
                                enemy = e;
                                return turn;
                            }
                            if (brace == null || (brace != e && endGap < braceGap))
                            {
                                brace = e;
                                braceGap = endGap;
                                braceWhy = turn;
                            }
                        }
                    }
                }
            }
            UpdateBrace(formation, own, brace, braceWhy, now);

            bool inMelee = RBMAI.Utilities.FormationFightingInMelee(line, ContactMeleeRatio);
            bool footAtLine = nearestGap <= ContactGap;
            bool contactNow = (inMelee && footAtLine) || nearestGap <= ContactStartGap;
            if (contactNow)
            {
                _lastContactAt = now;
            }
            if (float.IsNaN(_contactSince) && contactNow)
            {
                _contactSince = now;
                TerrainBattlePosition.Enqueue(formation.Team, formation, now, "ReservePlug", "contact", "plug",
                    (inMelee ? "lineMelee " : "enemyNear ") + ThreatSignificance.Describe(line, nearest) + " gap=" + FormationGeometry.GapText(nearestGap)
                    + " line=" + TerrainCavalry.FormationName(line));
            }
            else if (!float.IsNaN(_contactSince) && !contactNow && now - _lastContactAt >= ContactResetSeconds)
            {
                TerrainBattlePosition.Enqueue(formation.Team, formation, now, "ReservePlug", "contactLost", "plug",
                    "no melee with significant foot at the line for " + TerrainBattlePosition.Fmt0(now - _lastContactAt) + "s (clock ran "
                    + TerrainBattlePosition.Fmt0(now - _contactSince) + "s) nearestFoot=" + FormationGeometry.GapText(nearestGap));
                _contactSince = float.NaN;
            }
            if (contactPower > 0f && RBMAI.Utilities.FormationFightingInMelee(line, MeleeRatio))
            {
                float ratio = MathF.Sqrt((line.QuerySystem.FormationPower + 1f) / (contactPower + 1f));
                if (ratio < PlugPowerRatio)
                {
                    return "power ratio=" + TerrainBattlePosition.Fmt2(ratio) + " (<" + TerrainBattlePosition.Fmt2(PlugPowerRatio) + ")";
                }
            }
            if (!float.IsNaN(_contactSince))
            {
                float lost = (_linePeak > 0) ? 1f - (float)line.CountOfUnits / _linePeak : 0f;
                if (lost > PlugCasualty)
                {
                    return "casualties lost=" + TerrainBattlePosition.Fmt2(lost) + " of " + _linePeak.ToString(CultureInfo.InvariantCulture);
                }
                if (now - _contactSince >= PlugMaxWait)
                {
                    // only with an enemy in contact with the line to go for (else it keeps waiting)
                    Formation pressing = PressingEnemy(mission, formation, line, true);
                    if (pressing != null)
                    {
                        enemy = pressing;
                        return "timeout " + TerrainBattlePosition.Fmt0(now - _contactSince) + "s after contact";
                    }
                }
            }
            return null;
        }

        /// <summary>AI thread: brace null = none now.</summary>
        private void UpdateBrace(Formation formation, Vec2 own, Formation brace, string why, float now)
        {
            if (brace == null)
            {
                if (_braced != null)
                {
                    TerrainBattlePosition.Enqueue(formation.Team, formation, now, "ReservePlug", "unbrace", "plug",
                        TerrainCavalry.FormationName(_braced) + " left or gone after " + TerrainBattlePosition.Fmt0(now - _braceSince) + "s: back to the point, commit armed");
                    _braced = null;
                    _braceFacing = Vec2.Invalid;
                    // back to Line at the point (ApplyShape's shape)
                    _shaped = false;
                    _stateName = _state.ToString().ToLowerInvariant();
                }
                return;
            }
            Vec2 c = RBMAI.Utilities.GetFormationCenter(brace);
            Vec2 dir = c.IsValid ? c - own : Vec2.Invalid;
            if (!dir.IsValid || dir.LengthSquared < 1f)
            {
                return;
            }
            dir = dir.Normalized();
            if (_braced != brace)
            {
                bool first = _braced == null;
                _braced = brace;
                _braceFacing = dir;
                if (first)
                {
                    _braceSince = now;
                }
                TerrainBattlePosition.Enqueue(formation.Team, formation, now, "ReservePlug", "brace", "plug",
                    why + (first ? "" : " (switch)") + " - riders: face them in place, no charge");
                if (formation.IsAIControlled)
                {
                    bool wall = !RBMAI.Utilities.IsRangedForArrangement(formation)
                        && RBMAI.Utilities.FormationRatioShieldWallEligible(formation) > RBMConfig.RBMConfig.shieldWallShare;
                    formation.SetArrangementOrder(wall ? ArrangementOrder.ArrangementOrderShieldWall : ArrangementOrder.ArrangementOrderLine);
                }
            }
            else if (Vec2.DotProduct(dir, _braceFacing) < BraceTurnCos)
            {
                _braceFacing = dir;
            }
        }

        /// <summary>AI thread: mounted or retreating target farther than TargetAwayGap from the line (the reserve without one), else null.</summary>
        private static string TargetAway(Formation target, Formation line, Formation formation)
        {
            if (target == null || target.CountOfUnits <= 0)
            {
                return null;
            }
            bool mounted = target.QuerySystem.IsCavalryFormation || target.QuerySystem.IsRangedCavalryFormation;
            bool retreating = target.GetReadonlyMovementOrderReference().OrderType == OrderType.Retreat;
            if (!mounted && !retreating)
            {
                return null;
            }
            Formation anchor = (line != null && line.CountOfUnits > 0) ? line : formation;
            Vec2 a = RBMAI.Utilities.GetFormationCenter(anchor);
            Vec2 c = RBMAI.Utilities.GetFormationCenter(target);
            if (!a.IsValid || !c.IsValid)
            {
                return null;
            }
            float gap = FormationGeometry.Gap(anchor, FootprintMode.ScatterAware, target, FootprintMode.ScatterAware);
            if (gap <= TargetAwayGap)
            {
                return null;
            }
            return TerrainCavalry.FormationName(target) + (mounted ? " mounted" : " retreating") + " gap="
                + FormationGeometry.GapText(gap);
        }

        /// <summary>Largest non-HA enemy within ContactGap of the line, else (unless contactOnly) the nearest; FOOT first. AI thread.</summary>
        private static Formation PressingEnemy(Mission mission, Formation formation, Formation line, bool contactOnly = false)
        {
            Formation anchorFormation = (line != null && line.CountOfUnits > 0) ? line : formation;
            Vec2 anchor = RBMAI.Utilities.GetFormationCenter(anchorFormation);
            Footprint anchorShape;
            if (!anchor.IsValid || !FormationGeometry.TryGet(anchorFormation, FootprintMode.ScatterAware, out anchorShape))
            {
                return null;
            }
            // without a line the reserve's own centre is the anchor point (no extent), as before
            if (anchorFormation != line)
            {
                anchorShape = Footprint.Circle(anchor, 0f);
            }
            Formation largest = null;
            Formation nearest = null;
            float nearestDistance = float.MaxValue;
            Formation largestFoot = null;
            Formation nearestFoot = null;
            float nearestFootDistance = float.MaxValue;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (e == null || e.CountOfUnits <= 0 || e.QuerySystem.IsRangedCavalryFormation)
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(e);
                    Footprint shape;
                    if (!c.IsValid || !FormationGeometry.TryGet(e, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    float distance = c.Distance(anchor);
                    float gap = FormationGeometry.Gap(anchorShape, shape);
                    bool foot = !e.QuerySystem.IsCavalryFormation;
                    if (gap <= ContactGap && (largest == null || e.CountOfUnits > largest.CountOfUnits))
                    {
                        largest = e;
                    }
                    if (foot && gap <= ContactGap && (largestFoot == null || e.CountOfUnits > largestFoot.CountOfUnits))
                    {
                        largestFoot = e;
                    }
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = e;
                    }
                    if (foot && distance < nearestFootDistance)
                    {
                        nearestFootDistance = distance;
                        nearestFoot = e;
                    }
                }
            }
            Formation contact = largestFoot ?? largest;
            return contactOnly ? contact : (contact ?? nearestFoot ?? nearest);
        }

        /// <summary>AI-controlled formations only.</summary>
        private void ApplyShape()
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (mission == null || formation == null || !formation.IsAIControlled || _state == PlugState.Wait
                || _state == PlugState.Committed || _state == PlugState.Done)
            {
                return;
            }
            if (!_shaped)
            {
                _shaped = true;
                formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
            }
            float now = mission.CurrentTime;
            if (now - _widthAt < WidthRefreshSeconds)
            {
                return;
            }
            _widthAt = now;
            int units = System.Math.Max(1, formation.CountOfUnitsWithoutDetachedOnes);
            int files = System.Math.Max(1, (int)(units / WaitRanks));
            float width = (files - 1) * (formation.Interval + formation.UnitDiameter) + formation.UnitDiameter;
            TerrainBattlePosition.Plan plan = _plan;
            if (plan != null && !float.IsNaN(plan.LineWidth))
            {
                width = MathF.Min(width, plan.LineWidth);
            }
            width = MathF.Max(width, MinWidth);
            if (float.IsNaN(_widthSet) || MathF.Abs(width - _widthSet) > 1f)
            {
                _widthSet = width;
                formation.SetFormOrder(FormOrder.FormOrderCustom(width));
            }
        }

        private void SetState(PlugState state, float now, string why)
        {
            PlugState old = _state;
            _state = state;
            _stateSince = now;
            _stateName = state.ToString().ToLowerInvariant();
            if (why != null && base.Formation != null && (old != state || why.StartsWith("replan")))
            {
                TerrainBattlePosition.Enqueue(base.Formation.Team, base.Formation, now, "ReservePlug", old + "->" + state, "plug", why);
            }
        }

        public override TextObject GetBehaviorString()
        {
            return new TextObject("{=RBMReservePlug01}waiting in reserve");
        }

        protected override float GetAiWeight()
        {
            if (base.Formation.CountOfUnits == 0 || _owner == null || _state == PlugState.Done)
            {
                return 0f;
            }
            return LiveWeight;
        }
    }
}
