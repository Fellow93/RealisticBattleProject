using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The hold phase of RBMTacticHoldChoke: a formation takes its place at the defender's choke hold (TerrainChokeHold) and stands there
    /// facing the attacker through the gap. Weight 0 unless a tactic sets it (with a fallback behavior), and 0 without a plan / place for
    /// its role or once the line has charged, so the fallback takes over. The tactic ends the hold by giving the engage behaviors.
    ///
    /// Roles (set by the tactic through Assign):
    ///  - Line (main infantry): Move to the hold point, facing the gap; custom width = the hold width (walkable width x 0.95), at least
    ///    MinWidth and at most what keeps MinRanks ranks. Line while moving; at the point a shield wall when RBM's rule qualifies it
    ///    (FormationRatioShieldWallEligible) AND it is under missile fire (FireEnter / FireKeep) AND it is not ranged, else Line; at most one
    ///    arrangement change per ArrangementMinSeconds, none while in melee (Contact under ContactMeleeShare still may).
    ///    RE-FACE (HoldReface): holding at its point and out of melee it turns in place toward the enemy's main concentration, but only
    ///    within RefaceMaxOffPlan of the gap's facing (farther, RBMTacticHoldChoke drops the bypassed choke). pushedBack / behind read the turned facing and point.
    ///  - Archers: Move to the archer place (a spot behind / beside the line, or a rise behind it), facing as the line, Line at the post,
    ///    Loose under fire / broken ground (ChooseRangedArrangement), fire at will.
    ///  - Reserve (cavalry, when the choke covers both flanks): Move to the reserve place behind the line, facing as the line; released to the tactic's
    ///    ProtectFlank while threatened (RBMBehaviorHoldPosition.ReserveRelease).
    ///  Archers / Reserve: the place turns with a re-faced line round its pivot (HoldReface.TurnedPlace, turnPlace / unturnPlace).
    ///  Archers / Reserve: a place overlapping a friend's destination by more than TakeMaxShare is moved ONCE along the facing (FriendlyOverlap.PlaceShift).
    ///  Archers: a (shifted) place beyond the FormationCohesion limit is pulled toward the main infantry (Pull; the cavalry Reserve is exempt).
    ///  Archers: ArcherCover - behind / beside friendly infantry from a closing significant enemy melee formation no infantry screens (point=cover).
    /// States: Wait (no plan / no place) -> Moving -> Holding (within ArriveDistance + a quarter depth; Moving again beyond LeaveDistance).
    /// Changes are BEHAVIOR lines (TerrainChokeHold.Enqueue); StateName feeds the decision layer.
    ///
    /// LATE CHARGE (Line role; user rule: charge as late as possible): HoldLateCharge's Holding -> Contact (keeps Move order, place, facing,
    /// width; fights from its slots) -> Charge on its triggers (lineGone / pushedBack / behind / advantage / broken; enemy cavalry never counts)
    /// or this behavior's own: infantry (wait for cavalry, charge SIGNIFICANT enemy FOOT within ContactStartGap with the line in melee or that
    /// foot closing; CheckRecall brings it back, quiet for RecallQuietSeconds), allyFighting (a friend within LateAllyFightGap in melee with enemy
    /// foot significant to the line; local, recalled the same way), outshot (OutshotReason: enemy ranged power far higher AND hits land and kill).
    /// Charge is latched per assignment; the tactic reads ChargedFor. Changes are BEHAVIOR lines; the decision label shows "contact" / "charge reason".
    ///
    /// THREADING: CalculateCurrentOrder / TickOccasionally / GetAiWeight run on the async AI thread (CalculateCurrentOrder also from
    /// PrecalculateMovementOrder): immutable published plan, cached formation data, copies of the plan's cached WorldPositions (a shifted place:
    /// one SetVec2MT on a fresh copy, cached); no engine query, no WorldPosition construction, no MBRandom. StateName is a volatile string for the main thread.
    /// </summary>
    internal class RBMBehaviorHoldChoke : BehaviorComponent
    {
        internal enum Role
        {
            Line,
            Archers,
            Reserve
        }

        internal enum HoldState
        {
            Wait,
            Moving,
            Holding
        }


        private const float LiveWeight = 20f; // above every fallback behavior the tactic gives
        private const float ArriveDistance = 15f; // m, + a quarter of the depth
        private const float LeaveDistance = 35f; // m
        private const float MinWidth = 10f; // m
        private const float MinRanks = 3f;
        private const float WidthRefreshSeconds = 5f; // and only when the width moved by more than 1 m
        private const float ArrangementMinSeconds = 10f;
        private const float FireEnter = 0.15f; // UnderRangedAttackRatio: enters the shield wall ...
        private const float FireKeep = 0.05f; // ... keeps it

        private Role _role;
        private TacticComponent _owner;
        private int _ownerGeneration = -1;
        private TerrainChokeHold.Plan _plan;
        private HoldState _state = HoldState.Wait;
        private float _stateSince = float.NaN;
        private float _arrangedAt = float.NegativeInfinity;
        private float _widthAt = float.NegativeInfinity;
        private float _widthSet = float.NaN;
        /// <summary>Once-per-activation shape (reserve line; archers outside the ranged rule loose) is set.</summary>
        private bool _shaped;
        /// <summary>Line role. AI thread.</summary>
        private readonly HoldLateCharge _fight = new HoldLateCharge();
        private volatile bool _charged;
        /// <summary>Archers / Reserve roles. AI thread.</summary>
        private readonly FriendlyOverlap.PlaceShift _shift = new FriendlyOverlap.PlaceShift();
        /// <summary>Archers role. AI thread.</summary>
        private readonly FormationCohesion.Pull _cohesion = new FormationCohesion.Pull();
        /// <summary>Archers role: cover, and support fire once the frontlines connect and the place no longer serves. AI thread.</summary>
        private readonly CoverSupport _glue = new CoverSupport("HoldChoke", "point");
        /// <summary>Line role: ReactionMonitor's line-answers handshake. AI thread.</summary>
        private Formation _allyAnswered;
        /// <summary>Line role. AI thread.</summary>
        private readonly HoldReface _reface = new HoldReface();
        /// <summary>Archers / Reserve roles: the place turned with a re-faced line. AI thread.</summary>
        private readonly HoldReface.TurnedPlace _turnedPlace = new HoldReface.TurnedPlace();
        /// <summary>Line role: the front point it holds now (the plan's hold, turned by the re-face). AI thread.</summary>
        private Vec2 _linePoint = Vec2.Invalid;
        /// <summary>Reserve role. AI thread.</summary>
        private readonly RBMBehaviorHoldPosition.ReserveRelease _reserveRelease = new RBMBehaviorHoldPosition.ReserveRelease();
        private volatile string _stateName = "wait";

        /// <summary>deg from the gap's facing; beyond it the choke is bypassed (RBMTacticHoldChoke).</summary>
        private const float RefaceMaxOffPlan = RBMTacticHoldChoke.BypassOffDegrees;

        public RBMBehaviorHoldChoke(Formation formation)
            : base(formation)
        {
            _behaviorSide = formation.AI.Side;
            base.CurrentOrder = MovementOrder.MovementOrderStop;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        }

        /// <summary>Map view text ("holding", "moving 45m", ...). Any thread.</summary>
        internal string StateName => _stateName;

        internal Role CurrentRole => _role;

        /// <summary>Line role charge latched (any owner). AI thread.</summary>
        internal bool Charged => _charged;

        /// <summary>AI thread.</summary>
        internal Formation AllyAnswered => _charged ? _allyAnswered : null;

        /// <summary>Read by the reaction monitor's balance re-pick. AI thread.</summary>
        internal HoldLateCharge.FightState FightState => _fight.State;

        /// <summary>AI thread: the line assigned by owner has charged (latched): the tactic should engage.</summary>
        internal static bool ChargedFor(Formation formation, TacticComponent owner, out string reason)
        {
            reason = null;
            if (formation == null || formation.AI == null || owner == null)
            {
                return false;
            }
            RBMBehaviorHoldChoke behavior = formation.AI.GetBehavior<RBMBehaviorHoldChoke>();
            if (behavior == null || behavior._owner != owner || behavior._role != Role.Line || !behavior._charged)
            {
                return false;
            }
            reason = behavior._fight.ChargeReason;
            return true;
        }

        /// <summary>AI thread, from the tactic: a new instance, generation or role restarts from Wait (charge un-latched); the same keeps the state. quietUntil (re-form after a local charge): until then only melee can charge.</summary>
        internal void Assign(TacticComponent owner, int generation, Role role, float quietUntil = float.NegativeInfinity)
        {
            if (owner == _owner && generation == _ownerGeneration && role == _role)
            {
                return;
            }
            _owner = owner;
            _ownerGeneration = generation;
            _role = role;
            _plan = null;
            _widthSet = float.NaN;
            _widthAt = float.NegativeInfinity;
            _arrangedAt = float.NegativeInfinity;
            _shaped = false;
            _fight.Reset(base.Formation, quietUntil);
            _charged = false;
            _allyAnswered = null;
            _shift.Reset();
            _cohesion.Reset();
            _reface.Reset();
            _turnedPlace.Reset();
            _linePoint = Vec2.Invalid;
            _reserveRelease.Reset();
            ReleaseCover("assigned");
            SetState(HoldState.Wait, float.NaN, null);
        }

        /// <summary>AI thread: the facing the choke Line (assigned by owner) holds now - the gap's, or re-faced toward the enemy's concentration - and its depth (pivot of a turned place); false without one. Archers / Reserve face the same way.</summary>
        private static bool LineTurn(Team team, TacticComponent owner, out Vec2 facing, out float depth)
        {
            facing = Vec2.Invalid;
            depth = 0f;
            if (team == null || owner == null)
            {
                return false;
            }
            foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
            {
                if (f == null || f.CountOfUnits <= 0 || f.AI == null)
                {
                    continue;
                }
                RBMBehaviorHoldChoke line = f.AI.GetBehavior<RBMBehaviorHoldChoke>();
                if (line != null && line._owner == owner && line._role == Role.Line && !line._charged)
                {
                    facing = line._reface.Facing;
                    depth = f.Depth;
                    return facing.IsValid;
                }
            }
            return false;
        }

        public override void TickOccasionally()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder(CurrentFacingOrder);
            ApplyShape();
            CheckFight();
        }

        private void CheckFight()
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            TerrainChokeHold.Plan plan = _plan;
            if (_charged || _role != Role.Line || plan == null || _state == HoldState.Wait || mission == null || formation == null || formation.Team == null)
            {
                return;
            }
            float now = mission.CurrentTime;
            // facing and front point it holds now (the re-face turns both)
            Vec2 facing = _reface.Facing.IsValid ? _reface.Facing : plan.Facing;
            Vec2 holdPoint = _linePoint.IsValid ? _linePoint : plan.Hold;
            // behind: enemy FOOT only (cavalry riding round the line does not draw it out)
            _fight.Read(mission, formation, facing, false, true);
            // user rules: enemy foot at the line is a charge (the tactic brings the line back); an enemy that only shoots draws it out only by OutshotReason;
            // a friend within LateAllyFightGap in melee with significant enemy foot is a LOCAL charge (CheckRecall re-forms)
            Formation answered = null;
            string own = _fight.InfantryReason();
            if (own == null)
            {
                own = HoldLateCharge.AllyFightingReason(mission, formation, HoldLateCharge.LateAllyFightGap, false, out answered);
            }
            if (own == null)
            {
                own = _fight.OutshotReason(mission, formation, now);
            }
            string why;
            string what = _fight.Decide(formation, now, holdPoint, facing, own, out why);
            if (what == null)
            {
                return;
            }
            if (_fight.State == HoldLateCharge.FightState.Charge)
            {
                _charged = true;
                _allyAnswered = (_fight.ChargeReason == "allyFighting") ? answered : null;
            }
            TerrainChokeHold.Enqueue(formation.Team, formation, now, what, "hold", why + " men=" + formation.CountOfUnits.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _stateName = _fight.Label() ?? "holding";
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
            if (_charged && _role == Role.Line)
            {
                // charged: the tactic's engage takes the formation over (weight 0 here)
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            TerrainChokeHold.Plan plan = TerrainChokeHold.HoldFor(formation.Team);
            _plan = plan;
            Vec2 point;
            WorldPosition world;
            string pointName;
            if (!TryGetPoint(plan, out point, out world, out pointName))
            {
                if (_state != HoldState.Wait)
                {
                    SetState(HoldState.Wait, now, (plan == null) ? "noPlan" : "noPlace");
                }
                ReleaseCover((plan == null) ? "noPlan" : "noPlace");
                ReleaseSupport((plan == null) ? "noPlan" : "noPlace");
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            Vec2 placeFacing = plan.Facing;
            if (_role != Role.Line)
            {
                // the line re-faced (HoldReface): the place turns with it round the line's pivot and faces its way
                Vec2 lineFacing;
                float lineDepth;
                if (LineTurn(formation.Team, _owner, out lineFacing, out lineDepth))
                {
                    Vec2 pivot = plan.Hold - plan.Facing.Normalized() * (MathF.Max(lineDepth, 1f) * 0.5f);
                    string turnWhat;
                    string turnWhy;
                    world = _turnedPlace.Resolve(world, point, pivot, plan.Facing, lineFacing, now, out point, out turnWhat, out turnWhy);
                    if (turnWhat != null)
                    {
                        TerrainChokeHold.Enqueue(formation.Team, formation, now, turnWhat, pointName, turnWhy);
                    }
                    placeFacing = lineFacing;
                }
                // user rule: the archer / reserve place moved once along the facing off a friend standing there
                string shiftWhat;
                string shiftWhy;
                world = _shift.Resolve(formation, world, placeFacing, out point, out shiftWhat, out shiftWhy);
                if (shiftWhat != null)
                {
                    TerrainChokeHold.Enqueue(formation.Team, formation, now, shiftWhat, pointName, shiftWhy);
                }
                // user rule: foot never stands beyond its cohesion limit from the main infantry (the cavalry reserve is exempt)
                world = _cohesion.Resolve(formation, world, placeFacing, FormationCohesion.Kind.Auto, null, out point, out shiftWhat, out shiftWhy);
                if (shiftWhat != null)
                {
                    TerrainChokeHold.Enqueue(formation.Team, formation, now, shiftWhat, pointName, shiftWhy);
                }
            }
            Vec2 facing = placeFacing;
            if (_role == Role.Line)
            {
                // holding at its point, the line turns in place toward the enemy's main concentration within RefaceMaxOffPlan of the gap's facing; only the active behavior turns
                bool mayTurn = formation.IsAIControlled && formation.AI != null && formation.AI.ActiveBehavior == this
                    && _state == HoldState.Holding && _fight.State != HoldLateCharge.FightState.Charge;
                string refaceWhy;
                string reface = _reface.Update(mission, formation, 1, plan.Facing, now, mayTurn, RefaceMaxOffPlan, out refaceWhy);
                if (reface != null)
                {
                    TerrainChokeHold.Enqueue(formation.Team, formation, now, reface, "hold", refaceWhy);
                }
                world = _reface.Point(world, plan.Hold, plan.Facing, formation.Depth, now, out point);
                _linePoint = point;
                if (_reface.Facing.IsValid)
                {
                    facing = _reface.Facing;
                }
            }
            // user rule: archers approached by the enemy take cover (ArcherCover); the place's states wait and resume
            if (_role == Role.Archers && CoverOrder(formation, now))
            {
                return;
            }
            // SupportFire: once the frontlines connect the place is kept while it still serves, else ONE support place behind / beside the main
            if (_role == Role.Archers && SupportOrder(formation, now, point, facing))
            {
                return;
            }
            Vec2 position = RBMAI.Utilities.GetFormationCenter(formation);
            if (position.IsValid)
            {
                float distance = position.Distance(point);
                float arrive = ArriveDistance + formation.Depth * 0.25f;
                if (_state == HoldState.Wait)
                {
                    if (distance <= arrive)
                    {
                        SetState(HoldState.Holding, now, "atPoint d=" + TerrainChokeHold.Fmt0(distance) + "m" + WidthNote(plan));
                    }
                    else
                    {
                        SetState(HoldState.Moving, now, "toPoint d=" + TerrainChokeHold.Fmt0(distance) + "m");
                    }
                }
                else if (_state == HoldState.Moving && distance <= arrive)
                {
                    SetState(HoldState.Holding, now, "arrived after " + TerrainChokeHold.Fmt0(now - _stateSince) + "s" + WidthNote(plan));
                }
                else if (_state == HoldState.Holding && distance > LeaveDistance + formation.Depth * 0.25f)
                {
                    SetState(HoldState.Moving, now, "offPoint d=" + TerrainChokeHold.Fmt0(distance) + "m");
                }
                else if (_state == HoldState.Moving)
                {
                    _stateName = RolePrefix() + "moving " + TerrainChokeHold.Fmt0(distance) + "m";
                }
            }
            if (_role == Role.Line)
            {
                if (_state == HoldState.Holding)
                {
                    _fight.ReachedHold = true;
                }
                // in contact the line fights where it stands: same Move order, facing the gap
                string fight = _fight.Label();
                if (fight != null)
                {
                    _stateName = fight;
                }
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(world);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(facing);
        }

        /// <summary>AI thread: false = the place's own order.</summary>
        private bool CoverOrder(Formation formation, float now)
        {
            WorldPosition target;
            Vec2 facing;
            // at the post only while Holding at it (ArcherCover checks the line really covers it)
            CoverSupport.Step step = _glue.CoverStep(this, formation, _state == HoldState.Holding && !_glue.Supporting, now, out target, out facing);
            return GlueOrder(step, target, facing, "cover");
        }

        /// <summary>AI thread: false = the place's own order.</summary>
        private bool SupportOrder(Formation formation, float now, Vec2 post, Vec2 postFacing)
        {
            WorldPosition target;
            Vec2 facing;
            CoverSupport.Step step = _glue.SupportStep(this, formation, null, post, postFacing, FormationAI.BehaviorSide.BehaviorSideNotSet, now, out target, out facing);
            return GlueOrder(step, target, facing, CoverSupport.IsOn(step) ? (_glue.Support.Label ?? "support") : null);
        }

        private bool GlueOrder(CoverSupport.Step step, WorldPosition target, Vec2 facing, string stateName)
        {
            if (!CoverSupport.IsOn(step))
            {
                if (step == CoverSupport.Step.Ended)
                {
                    _stateName = RolePrefix() + _state.ToString().ToLowerInvariant();
                }
                return false;
            }
            _stateName = RolePrefix() + stateName;
            base.CurrentOrder = MovementOrder.MovementOrderMove(target);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(facing);
            return true;
        }

        /// <summary>AI thread, or wherever native cancels the behavior.</summary>
        private void ReleaseCover(string reason)
        {
            _glue.ReleaseCover(base.Formation, reason);
        }

        /// <summary>AI thread, or wherever native cancels the behavior.</summary>
        private void ReleaseSupport(string reason)
        {
            _glue.ReleaseSupport(base.Formation, reason);
        }

        public override void OnBehaviorCanceled()
        {
            ReleaseCover("behaviorEnded");
            ReleaseSupport("behaviorEnded");
            base.OnBehaviorCanceled();
        }

        private bool TryGetPoint(TerrainChokeHold.Plan plan, out Vec2 point, out WorldPosition world, out string name)
        {
            point = Vec2.Invalid;
            world = WorldPosition.Invalid;
            name = null;
            if (plan == null)
            {
                return false;
            }
            switch (_role)
            {
                case Role.Archers:
                    if (!plan.HasArcher)
                    {
                        return false;
                    }
                    point = plan.Data.Archer;
                    world = plan.ArcherWorld;
                    name = "archer";
                    return true;
                case Role.Reserve:
                    if (!plan.HasReserve)
                    {
                        return false;
                    }
                    point = plan.Data.Reserve;
                    world = plan.ReserveWorld;
                    name = "reserve";
                    return true;
                default:
                    point = plan.Hold;
                    world = plan.HoldWorld;
                    name = "hold";
                    return true;
            }
        }

        /// <summary>AI-controlled formations only.</summary>
        private void ApplyShape()
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (mission == null || formation == null || !formation.IsAIControlled || _plan == null || (_state == HoldState.Wait && !_glue.Covering) || (_charged && _role == Role.Line))
            {
                return;
            }
            float now = mission.CurrentTime;
            switch (_role)
            {
                case Role.Line:
                    {
                        if (now - _widthAt >= WidthRefreshSeconds)
                        {
                            _widthAt = now;
                            float width = LineWidth(formation, _plan.HoldWidth);
                            if (float.IsNaN(_widthSet) || MathF.Abs(width - _widthSet) > 1f)
                            {
                                _widthSet = width;
                                formation.SetFormOrder(FormOrder.FormOrderCustom(width));
                            }
                        }
                        // in melee the arrangement stays (a change re-forms the line mid-fight); in Contact but not yet fighting the shield wall decision still runs
                        HoldLateCharge.FightState fightState = _fight.State;
                        bool mayArrange = fightState == HoldLateCharge.FightState.Holding
                            || (fightState == HoldLateCharge.FightState.Contact && _fight.LastMeleeShare < HoldLateCharge.ContactMeleeShare);
                        if (now - _arrangedAt >= ArrangementMinSeconds && mayArrange)
                        {
                            ArrangementOrder.ArrangementOrderEnum current = formation.ArrangementOrder.OrderEnum;
                            bool shieldWall = _state == HoldState.Holding && WantsShieldWall(formation, current);
                            ArrangementOrder.ArrangementOrderEnum wanted = shieldWall ? ArrangementOrder.ArrangementOrderEnum.ShieldWall : ArrangementOrder.ArrangementOrderEnum.Line;
                            if (current != wanted)
                            {
                                _arrangedAt = now;
                                formation.SetArrangementOrder(shieldWall ? ArrangementOrder.ArrangementOrderShieldWall : ArrangementOrder.ArrangementOrderLine);
                                TerrainChokeHold.Enqueue(formation.Team, formation, now, shieldWall ? "shieldWall" : "line", "hold",
                                    "shields=" + RBMAI.Utilities.FormationRatioShieldWallEligible(formation).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                                    + " underFire=" + formation.QuerySystem.UnderRangedAttackRatio.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                            }
                        }
                        break;
                    }
                case Role.Archers:
                    // a fixed post behind the line: Line / Loose by the situation (ChooseRangedArrangement, every tick); a formation outside that rule keeps once-per-activation Loose
                    if (!RBMAI.Utilities.TryApplyRangedArrangement(formation, ArrangementOrder.ArrangementOrderEnum.Line, true) && !_shaped)
                    {
                        formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
                    }
                    _shaped = true;
                    break;
                case Role.Reserve:
                    if (!_shaped)
                    {
                        _shaped = true;
                        formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
                    }
                    break;
            }
        }

        /// <summary>RBM's eligibility and under missile fire, each with hysteresis; never a ranged formation.</summary>
        private static bool WantsShieldWall(Formation formation, ArrangementOrder.ArrangementOrderEnum current)
        {
            if (RBMAI.Utilities.IsRangedForArrangement(formation))
            {
                return false;
            }
            bool inWall = current == ArrangementOrder.ArrangementOrderEnum.ShieldWall;
            float share = RBMConfig.RBMConfig.shieldWallShare;
            if (!(RBMAI.Utilities.FormationRatioShieldWallEligible(formation) > (inWall ? share - 0.1f : share)))
            {
                return false;
            }
            return formation.QuerySystem.UnderRangedAttackRatio > (inWall ? FireKeep : FireEnter);
        }

        private static float LineWidth(Formation formation, float holdWidth)
        {
            int units = System.Math.Max(1, formation.CountOfUnitsWithoutDetachedOnes);
            int files = System.Math.Max(1, (int)(units / MinRanks));
            float maxWidth = (files - 1) * (formation.Interval + formation.UnitDiameter) + formation.UnitDiameter;
            float width = float.IsNaN(holdWidth) ? maxWidth : MathF.Min(holdWidth, maxWidth);
            return MathF.Max(width, MathF.Min(MinWidth, maxWidth));
        }

        /// <summary>" width=W/gap G" for the line's arrival lines; "" for the other roles.</summary>
        private string WidthNote(TerrainChokeHold.Plan plan)
        {
            if (_role != Role.Line || plan == null)
            {
                return "";
            }
            return " width=" + TerrainChokeHold.Fmt0(LineWidth(base.Formation, plan.HoldWidth)) + "/gap " + TerrainChokeHold.Fmt0(plan.HoldWidth) + "m";
        }

        private string RolePrefix()
        {
            return (_role == Role.Archers) ? "archers " : (_role == Role.Reserve) ? "reserve " : "";
        }

        private void SetState(HoldState state, float now, string why)
        {
            HoldState old = _state;
            _state = state;
            _stateSince = now;
            _stateName = RolePrefix() + state.ToString().ToLowerInvariant();
            if (why != null && old != state && base.Formation != null)
            {
                string point = (_role == Role.Archers) ? "archer" : (_role == Role.Reserve) ? "reserve" : "hold";
                TerrainChokeHold.Enqueue(base.Formation.Team, base.Formation, now, old + "->" + state, point, why);
            }
        }

        public override TextObject GetBehaviorString()
        {
            return new TextObject("{=RBMHoldChoke01}holding the pass");
        }

        protected override float GetAiWeight()
        {
            if (base.Formation.CountOfUnits == 0 || (_charged && _role == Role.Line))
            {
                return 0f;
            }
            Vec2 point;
            WorldPosition world;
            string name;
            if (!TryGetPoint(TerrainChokeHold.HoldFor(base.Formation.Team), out point, out world, out name))
            {
                return 0f;
            }
            Mission mission = Mission.Current;
            if (_role == Role.Reserve && mission != null && base.Formation.Team != null)
            {
                if (!RBMBehaviorHoldPosition.HasFootLeft(base.Formation.Team))
                {
                    return 0f;
                }
                // a threatened reserve goes to the tactic's fallback for a while (ReserveRelease)
                string what;
                string why;
                bool released = _reserveRelease.Released(mission, base.Formation, mission.CurrentTime, out what, out why);
                if (what != null)
                {
                    TerrainChokeHold.Enqueue(base.Formation.Team, base.Formation, mission.CurrentTime, what, "reserve", why);
                }
                if (released)
                {
                    return 0f;
                }
            }
            return LiveWeight;
        }
    }
}
