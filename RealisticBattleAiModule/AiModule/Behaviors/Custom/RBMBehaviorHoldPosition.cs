using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The defender's battle position (TerrainBattlePosition, re-picked while the attacker is far): a formation takes its place there and stands
    /// facing the attacker's approach. Weight 0 unless a tactic sets it (TryAssign: DefendSplitInfantry / DefendSplitArchers Defend, native
    /// defensive tactics through RBM's Defend postfixes, each with its old behaviors as fallback), and 0 without a plan / place or once the line
    /// has engaged. With terrainTacticsEnabled off there is no plan and TryAssign sets nothing.
    ///
    /// Roles (set by the tactic through Assign), as RBMBehaviorHoldChoke's:
    ///  - Line (main infantry): Move to the hold point facing the plan's facing; custom width = the plan's line width, at least MinWidth and at
    ///    most what keeps MinRanks ranks; Line while moving; at the point a shield wall when RBM's rule qualifies AND under missile fire AND not
    ///    ranged, else Line; at most one arrangement change per ArrangementMinSeconds. RE-FACE (HoldReface): holding and out of melee it turns in
    ///    place toward the enemy's main concentration (> 35 deg off for 5 s, at most one turn per 10 s); the local turn wins until the next plan version.
    ///  - Archers: Move to the archer place (a spot near the line, or the highest ground behind it), facing as the line, Line at the post, Loose under
    ///    fire / broken ground (ChooseRangedArrangement), fire at will.
    ///  - Reserve (cavalry, when both line ends are covered): Move to the reserve place behind the line. RELEASED (weight 0, the tactic's fallback takes over)
    ///    while ReserveRelease.Threat holds and FreeSeconds after.
    ///  Archers / Reserve: the place turns with a re-faced line (HoldReface.TurnedPlace); a place overlapping a friend's destination by more than
    ///  TakeMaxShare is moved ONCE along the facing (PlaceShift).
    ///  Archers: a place beyond the FormationCohesion limit is pulled toward the main infantry (Pull; the cavalry Reserve is exempt); ArcherCover when a
    ///  significant enemy melee formation closes in with no infantry between.
    /// States: Wait -> Moving -> Holding (within ArriveDistance + a quarter depth; Moving again beyond LeaveDistance); a new plan version sends it Moving.
    ///
    /// ENGAGE (user rule: the defender must still fight when the enemy comes close), Line role only (the choke and hedgehog charge late; this keeps the early
    /// engage). Every trigger counts only enemy FOOT SIGNIFICANT to the line (ThreatSignificance): all-mounted enemies never pull the line out:
    ///  - battleJoined: a significant enemy MELEE foot formation within BattleJoinRange (centre distance - both half depths);
    ///  - enemyNear: one within EngageDistance (edge gap) of the hold point;
    ///  - melee: EngageMeleeRatio of the line in melee with a significant enemy foot formation within ContactGap;
    ///  - allyFighting (AllyFightingReason): another friendly formation within AllyFightRange of the line in melee with enemy foot significant to the LINE;
    ///  - outshot (the strict shared HoldLateCharge.OutshotReason, at its point only).
    /// Latched: the weight drops to 0 and the owning tactic reads EngagedFor. While a Line assignment holds (HoldsLineFor), the tactic's "battle joined" is this
    /// engage alone (BattleJoinedFor). RELEASE: BattleJoinedFor un-latches it (TryRelease, "Engage->Hold recall") once the line, engaged at least RecallMinCharge and
    /// within ReleaseReturnDistance of the hold, is calm (CalmReason) for RecallSeconds; for RecallQuietSeconds after, only melee engages it.
    /// A player-controlled line is never assigned (TryAssign sets nothing; the tactic keeps its old rule). Changes are BEHAVIOR lines (TerrainBattlePosition.Enqueue).
    ///
    /// THREADING: CalculateCurrentOrder / TickOccasionally / GetAiWeight run on the async AI thread (CalculateCurrentOrder also from PrecalculateMovementOrder):
    /// immutable published plan, cached formation data, copies of the plan's cached WorldPositions (a shifted place: one SetVec2MT on a fresh copy, cached);
    /// no engine query, no WorldPosition construction, no MBRandom. StateName is a volatile string for the main thread.
    /// </summary>
    internal class RBMBehaviorHoldPosition : BehaviorComponent
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


        private const float LiveWeight = 20f; // above every fallback behavior the tactics give
        private const float ArriveDistance = 15f; // m
        private const float LeaveDistance = 35f; // m
        private const float MinWidth = 10f; // m
        private const float MinRanks = 3f;
        private const float WidthRefreshSeconds = 5f;
        private const float ArrangementMinSeconds = 10f;
        private const float FireEnter = 0.15f; // UnderRangedAttackRatio
        private const float FireKeep = 0.05f;

        // Engage triggers: the values RBMTacticHoldChoke keeps for this behavior.
        private const float BattleJoinRange = RBMTacticHoldChoke.BattleJoinRange;

        private const float EngageDistance = RBMTacticHoldChoke.ChokeEngageDistance;

        private const float EngageMeleeRatio = RBMTacticHoldChoke.EngageMeleeRatio;

        // Edge gap to a friend fighting significant enemy foot. Was 150: the line left its post for a cavalry fight 91 m off (log 2026-10-23 22:09).
        internal const float AllyFightRange = 60f;

        /// <summary>TryRelease only with the line this close (m) to the hold.</summary>
        internal const float ReleaseReturnDistance = RBMTacticHoldChoke.RecallReturnDistance;

        private Role _role;
        private TacticComponent _owner;
        private int _ownerGeneration = -1;
        private TerrainBattlePosition.Plan _plan;
        private int _planVersion;
        private HoldState _state = HoldState.Wait;
        private float _stateSince = float.NaN;
        private float _arrangedAt = float.NegativeInfinity;
        private float _widthAt = float.NegativeInfinity;
        private float _widthSet = float.NaN;
        private bool _shaped;
        private volatile bool _engaged;
        /// <summary>Line role: only its shared out-shot window; its fight state stays Holding. AI thread.</summary>
        private readonly HoldLateCharge _outshot = new HoldLateCharge();
        /// <summary>Archers / Reserve roles. AI thread.</summary>
        private readonly FriendlyOverlap.PlaceShift _shift = new FriendlyOverlap.PlaceShift();
        /// <summary>Archers role. AI thread.</summary>
        private readonly FormationCohesion.Pull _cohesion = new FormationCohesion.Pull();
        /// <summary>Archers role: cover, and support fire once the frontlines connect and the place no longer serves. AI thread.</summary>
        private readonly CoverSupport _glue = new CoverSupport(CoverSupport.LogQueue.BattlePosition, null);
        /// <summary>Line role: ReactionMonitor's line-answers handshake. AI thread.</summary>
        private Formation _allyAnswered;
        /// <summary>Line role: any angle; reset by a new plan version. AI thread.</summary>
        private readonly HoldReface _reface = new HoldReface();
        // Line role engage state (AI thread): when it latched, its trigger word, since when the line is calm (TryRelease), the quiet window after a release.
        private float _engagedAt = float.NaN;
        private string _engagedWord;
        private float _calmSince = float.NaN;
        private float _quietUntil = float.NegativeInfinity;
        /// <summary>Archers / Reserve roles. AI thread.</summary>
        private readonly HoldReface.TurnedPlace _turnedPlace = new HoldReface.TurnedPlace();
        /// <summary>Reserve role. AI thread.</summary>
        private readonly ReserveRelease _reserveRelease = new ReserveRelease();
        private volatile string _stateName = "wait";

        public RBMBehaviorHoldPosition(Formation formation)
            : base(formation)
        {
            _behaviorSide = formation.AI.Side;
            base.CurrentOrder = MovementOrder.MovementOrderStop;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        }

        /// <summary>Map view text ("holding", "archers moving 45m", ...). Any thread.</summary>
        internal string StateName => _stateName;

        internal Role CurrentRole => _role;

        /// <summary>Line role engage latched (any owner). AI thread.</summary>
        internal bool Engaged => _engaged;

        /// <summary>AI thread.</summary>
        internal Formation AllyAnswered => _engaged ? _allyAnswered : null;

        /// <summary>AI thread: after a recall only melee engages the line (ReactionMonitor's handshake must not wait for it).</summary>
        internal bool InQuiet => Mission.Current != null && Mission.Current.CurrentTime < _quietUntil;

        /// <summary>AI thread, from a tactic's Defend (weight 1; LiveWeight while it has a place). False without a plan or behavior, or for a player-controlled formation (its behaviors never tick; the tactic's old rule runs).</summary>
        internal static bool TryAssign(Formation formation, TacticComponent owner, int generation, Role role)
        {
            if (formation == null || formation.AI == null || !formation.IsAIControlled || TerrainBattlePosition.PlanFor(formation.Team) == null)
            {
                return false;
            }
            RBMBehaviorHoldPosition behavior = formation.AI.GetBehavior<RBMBehaviorHoldPosition>();
            if (behavior == null)
            {
                return false;
            }
            formation.AI.SetBehaviorWeight<RBMBehaviorHoldPosition>(1f);
            behavior.Assign(owner, generation, role);
            return true;
        }

        /// <summary>AI thread: the line assigned by owner has engaged (latched): the tactic should engage.</summary>
        internal static bool EngagedFor(Formation formation, TacticComponent owner)
        {
            if (formation == null || formation.AI == null || owner == null)
            {
                return false;
            }
            RBMBehaviorHoldPosition behavior = formation.AI.GetBehavior<RBMBehaviorHoldPosition>();
            return behavior != null && behavior._owner == owner && behavior._role == Role.Line && behavior._engaged;
        }

        /// <summary>AI thread: owner's Line assignment holds the formation (plan, not engaged): the hold's own engage is the only "battle joined".</summary>
        internal static bool HoldsLineFor(Formation formation, TacticComponent owner)
        {
            if (formation == null || formation.AI == null || owner == null)
            {
                return false;
            }
            RBMBehaviorHoldPosition behavior = formation.AI.GetBehavior<RBMBehaviorHoldPosition>();
            return behavior != null && behavior._owner == owner && behavior._role == Role.Line && !behavior._engaged
                && behavior.WeightFactor > 0f && formation.IsAIControlled && TerrainBattlePosition.PlanFor(formation.Team) != null;
        }

        /// <summary>AI thread, the defend tactics' "battle joined": while owner's Line assignment holds the main infantry (HoldsLineFor) only the hold's engage (EngagedFor); otherwise the old rule (Utilities.HasBattleBeenJoined) or the latch. An engaged line of this owner that is calm again is released here (TryRelease): false, so the tactic holds again.</summary>
        internal static bool BattleJoinedFor(Formation main, TacticComponent owner, bool hasBattleBeenJoined, float battleJoinRange = 75f)
        {
            if (main != null && main.AI != null && owner != null)
            {
                RBMBehaviorHoldPosition line = main.AI.GetBehavior<RBMBehaviorHoldPosition>();
                if (line != null && line._owner == owner && line._role == Role.Line && line._engaged && line.TryRelease())
                {
                    return false;
                }
            }
            if (HoldsLineFor(main, owner))
            {
                return EngagedFor(main, owner);
            }
            return RBMAI.Utilities.HasBattleBeenJoined(main, hasBattleBeenJoined, battleJoinRange) || EngagedFor(main, owner);
        }

        /// <summary>AI thread, from the tactic: a new instance, generation or role restarts from Wait (un-latches the engage); the same keeps the state.</summary>
        internal void Assign(TacticComponent owner, int generation, Role role)
        {
            if (owner == _owner && generation == _ownerGeneration && role == _role)
            {
                return;
            }
            _owner = owner;
            _ownerGeneration = generation;
            _role = role;
            _plan = null;
            _planVersion = 0;
            _widthSet = float.NaN;
            _widthAt = float.NegativeInfinity;
            _arrangedAt = float.NegativeInfinity;
            _shaped = false;
            _engaged = false;
            _engagedAt = float.NaN;
            _engagedWord = null;
            _calmSince = float.NaN;
            _quietUntil = float.NegativeInfinity;
            _allyAnswered = null;
            _outshot.Reset(base.Formation);
            _shift.Reset();
            _cohesion.Reset();
            _reface.Reset();
            _turnedPlace.Reset();
            _reserveRelease.Reset();
            ReleaseCover("assigned");
            SetState(HoldState.Wait, float.NaN, null);
        }

        /// <summary>AI thread, from BattleJoinedFor while engaged: true once it un-latched the engage (calm for RecallSeconds, hold within ReleaseReturnDistance). Quiet for RecallQuietSeconds after.</summary>
        private bool TryRelease()
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (mission == null || formation == null || formation.Team == null || !formation.IsAIControlled || formation.CountOfUnits < HoldLateCharge.LineGoneMen)
            {
                _calmSince = float.NaN;
                return false;
            }
            float now = mission.CurrentTime;
            TerrainBattlePosition.Plan plan = TerrainBattlePosition.PlanFor(formation.Team);
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            if (plan == null || !centre.IsValid || float.IsNaN(_engagedAt) || now - _engagedAt < HoldLateCharge.RecallMinCharge)
            {
                _calmSince = float.NaN;
                return false;
            }
            float toHold = centre.Distance(plan.Hold);
            string calm = (toHold <= ReleaseReturnDistance) ? HoldLateCharge.CalmReason(mission, formation, _engagedWord == "outshot") : null;
            if (calm == null)
            {
                _calmSince = float.NaN;
                return false;
            }
            if (float.IsNaN(_calmSince))
            {
                _calmSince = now;
                return false;
            }
            if (now - _calmSince < HoldLateCharge.RecallSeconds)
            {
                return false;
            }
            string word = _engagedWord ?? "-";
            float engagedFor = now - _engagedAt;
            _engaged = false;
            _engagedAt = float.NaN;
            _engagedWord = null;
            _calmSince = float.NaN;
            _allyAnswered = null;
            _quietUntil = now + HoldLateCharge.RecallQuietSeconds;
            _outshot.Reset(formation, _quietUntil);
            SetState(HoldState.Wait, now, null);
            TerrainBattlePosition.Enqueue(formation.Team, formation, now, "Engage->Hold", "hold",
                "recall " + word + " engage after " + TerrainBattlePosition.Fmt0(engagedFor) + "s: calm " + calm + " for "
                + TerrainBattlePosition.Fmt0(HoldLateCharge.RecallSeconds) + "s, back to the hold " + TerrainBattlePosition.Fmt0(toHold) + "m v"
                + plan.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + " quiet " + TerrainBattlePosition.Fmt0(HoldLateCharge.RecallQuietSeconds)
                + "s men=" + formation.CountOfUnits.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }

        /// <summary>AI thread: the facing the team's battle-position Line (assigned by owner) holds now - the plan's, or turned toward the enemy's concentration - and its depth (pivot of a turned place); false without one. Archers / Reserve face the same way.</summary>
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
                RBMBehaviorHoldPosition line = f.AI.GetBehavior<RBMBehaviorHoldPosition>();
                if (line != null && line._owner == owner && line._role == Role.Line && !line._engaged)
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
            CheckEngage();
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
            TerrainBattlePosition.Plan plan = TerrainBattlePosition.PlanFor(formation.Team);
            _plan = plan;
            Vec2 point;
            WorldPosition world;
            string pointName;
            if (_engaged || !TryGetPoint(plan, out point, out world, out pointName))
            {
                if (_state != HoldState.Wait && !_engaged)
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
                // the line turned (HoldReface): the place turns with it round the line's pivot and faces its way
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
                        TerrainBattlePosition.Enqueue(formation.Team, formation, now, turnWhat, pointName, turnWhy);
                    }
                    placeFacing = lineFacing;
                }
                // user rule: the archer / reserve place moved once along the facing off a friend standing there
                string shiftWhat;
                string shiftWhy;
                world = _shift.Resolve(formation, world, placeFacing, out point, out shiftWhat, out shiftWhy);
                if (shiftWhat != null)
                {
                    TerrainBattlePosition.Enqueue(formation.Team, formation, now, shiftWhat, pointName, shiftWhy);
                }
                // user rule: foot never stands beyond its cohesion limit from the main infantry (the cavalry reserve is exempt)
                world = _cohesion.Resolve(formation, world, placeFacing, FormationCohesion.Kind.Auto, null, out point, out shiftWhat, out shiftWhy);
                if (shiftWhat != null)
                {
                    TerrainBattlePosition.Enqueue(formation.Team, formation, now, shiftWhat, pointName, shiftWhy);
                }
            }
            Vec2 facing = placeFacing;
            if (_role == Role.Line)
            {
                // holding at its point, the line turns in place toward the enemy's main concentration; a new plan version brings back the plan's facing; only the active behavior turns
                bool mayTurn = formation.IsAIControlled && formation.AI != null && formation.AI.ActiveBehavior == this
                    && _state == HoldState.Holding && !_engaged && (_planVersion == 0 || _planVersion == plan.Version);
                string refaceWhy;
                string reface = _reface.Update(mission, formation, plan.Version, plan.Facing, now, mayTurn, 180f, out refaceWhy);
                if (reface != null)
                {
                    TerrainBattlePosition.Enqueue(formation.Team, formation, now, reface, "hold", refaceWhy);
                }
                world = _reface.Point(world, plan.Hold, plan.Facing, formation.Depth, now, out point);
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
                if (_state != HoldState.Wait && _planVersion != 0 && plan.Version != _planVersion && distance > arrive)
                {
                    // a re-pick moved the position: walk to the new one
                    SetState(HoldState.Moving, now, "replan v" + _planVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "->v" + plan.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + " d=" + TerrainBattlePosition.Fmt0(distance) + "m");
                }
                else if (_state == HoldState.Wait)
                {
                    if (distance <= arrive)
                    {
                        SetState(HoldState.Holding, now, "atPoint d=" + TerrainBattlePosition.Fmt0(distance) + "m " + plan.KindName + WidthNote(plan));
                    }
                    else
                    {
                        SetState(HoldState.Moving, now, "toPoint d=" + TerrainBattlePosition.Fmt0(distance) + "m " + plan.KindName);
                    }
                }
                else if (_state == HoldState.Moving && distance <= arrive)
                {
                    SetState(HoldState.Holding, now, "arrived after " + TerrainBattlePosition.Fmt0(now - _stateSince) + "s" + WidthNote(plan));
                }
                else if (_state == HoldState.Holding && distance > LeaveDistance + formation.Depth * 0.25f)
                {
                    SetState(HoldState.Moving, now, "offPoint d=" + TerrainBattlePosition.Fmt0(distance) + "m");
                }
                else if (_state == HoldState.Moving)
                {
                    _stateName = RolePrefix() + "moving " + TerrainBattlePosition.Fmt0(distance) + "m";
                }
                _planVersion = plan.Version;
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(world);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(facing);
        }

        /// <summary>AI thread: false = the place's own order.</summary>
        private bool CoverOrder(Formation formation, float now)
        {
            WorldPosition target;
            Vec2 facing;
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

        private bool TryGetPoint(TerrainBattlePosition.Plan plan, out Vec2 point, out WorldPosition world, out string name)
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

        private void CheckEngage()
        {
            if (_engaged || _role != Role.Line || _plan == null || _state == HoldState.Wait)
            {
                return;
            }
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (mission == null || formation == null || formation.Team == null)
            {
                return;
            }
            string why = EngageReason(mission, formation, _plan);
            if (why == null)
            {
                return;
            }
            _engaged = true;
            _engagedAt = mission.CurrentTime;
            _engagedWord = why.Split(' ')[0];
            _calmSince = float.NaN;
            _stateName = "engaged";
            TerrainBattlePosition.Enqueue(formation.Team, formation, mission.CurrentTime, "Hold->Engage", "hold", why);
        }

        /// <summary>AI thread.</summary>
        private string EngageReason(Mission mission, Formation formation, TerrainBattlePosition.Plan plan)
        {
            if (formation.CountOfUnits <= 0)
            {
                return null;
            }
            Team own = formation.Team;
            // quiet after a release: only melee engages the line again (allyFighting re-engaged it 0.3 s after a recall, log 2026-10-23 22:09)
            bool quiet = mission.CurrentTime < _quietUntil;
            Vec2 lineCentre = RBMAI.Utilities.GetFormationCenter(formation);
            float ownHalfDepth = formation.Depth * 0.5f;
            Footprint lineShape;
            FormationGeometry.TryGet(formation, FootprintMode.Current, out lineShape);
            bool shadow = FormationGeometry.ShadowOn;
            Formation contactFoot = null;
            float contactGap = float.MaxValue;
            float contactGapOld = float.NaN;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(own))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    // enemy FOOT significant to the line only: cavalry, horse archers and bait men never pull it out
                    if (enemy == null || enemy.CountOfUnits <= 0 || enemy.QuerySystem.IsCavalryFormation || enemy.QuerySystem.IsRangedCavalryFormation
                        || !ThreatSignificance.IsSignificant(formation, enemy))
                    {
                        continue;
                    }
                    Vec2 centre = RBMAI.Utilities.GetFormationCenter(enemy);
                    Footprint shape;
                    if (!centre.IsValid || !FormationGeometry.TryGet(enemy, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    float lineGap = FormationGeometry.Gap(lineShape, shape);
                    // the former measure: centre distance minus both half depths
                    float lineGapOld = (shadow && lineCentre.IsValid) ? MathF.Max(0f, centre.Distance(lineCentre) - ownHalfDepth - enemy.Depth * 0.5f) : float.NaN;
                    if (lineGap <= HoldLateCharge.ContactGap && lineGap < contactGap)
                    {
                        contactGap = lineGap;
                        contactGapOld = lineGapOld;
                        contactFoot = enemy;
                    }
                    // enemy archers standing off and shooting are no reason to leave (outshot below is); melee foot is
                    if (RBMAI.Utilities.IsRangedForArrangement(enemy) || quiet)
                    {
                        continue;
                    }
                    if (lineGap <= BattleJoinRange)
                    {
                        return "battleJoined " + ThreatSignificance.Describe(formation, enemy) + " gap=" + FormationGeometry.Shadow(lineGap, lineGapOld);
                    }
                    float gap = FormationGeometry.GapToPoint(shape, plan.Hold);
                    if (gap <= EngageDistance)
                    {
                        return "enemyNear " + ThreatSignificance.Describe(formation, enemy) + " gap="
                            + FormationGeometry.Shadow(gap, shadow ? centre.Distance(plan.Hold) - MathF.Max(enemy.Width, enemy.Depth) * 0.5f : float.NaN);
                    }
                }
            }
            // melee only with significant enemy foot at the line (a fight with cavalry is not a reason to leave)
            if (contactFoot != null && RBMAI.Utilities.FormationFightingInMelee(formation, EngageMeleeRatio))
            {
                return "melee " + ThreatSignificance.Describe(formation, contactFoot) + " gap=" + FormationGeometry.Shadow(contactGap, contactGapOld);
            }
            // a friendly formation near the line (reserve, archers, flank guard) in melee with enemy foot significant to the line: the line goes in
            Formation answered = null;
            string ally = quiet ? null : HoldLateCharge.AllyFightingReason(mission, formation, AllyFightRange, false, out answered);
            if (ally != null)
            {
                _allyAnswered = answered;
                return ally;
            }
            // out-shot: the shared strict rule (HoldLateCharge.OutshotReason), only once the line stands at its point
            _outshot.ReachedHold = _state == HoldState.Holding && !quiet;
            return _outshot.OutshotReason(mission, formation, mission.CurrentTime);
        }

        /// <summary>AI-controlled formations only.</summary>
        private void ApplyShape()
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (mission == null || formation == null || !formation.IsAIControlled || _plan == null || (_state == HoldState.Wait && !_glue.Covering) || _engaged)
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
                            float width = LineWidth(formation, _plan.LineWidth);
                            if (float.IsNaN(_widthSet) || MathF.Abs(width - _widthSet) > 1f)
                            {
                                _widthSet = width;
                                formation.SetFormOrder(FormOrder.FormOrderCustom(width));
                            }
                        }
                        if (now - _arrangedAt >= ArrangementMinSeconds)
                        {
                            ArrangementOrder.ArrangementOrderEnum current = formation.ArrangementOrder.OrderEnum;
                            bool shieldWall = _state == HoldState.Holding && WantsShieldWall(formation, current);
                            ArrangementOrder.ArrangementOrderEnum wanted = shieldWall ? ArrangementOrder.ArrangementOrderEnum.ShieldWall : ArrangementOrder.ArrangementOrderEnum.Line;
                            if (current != wanted)
                            {
                                _arrangedAt = now;
                                formation.SetArrangementOrder(shieldWall ? ArrangementOrder.ArrangementOrderShieldWall : ArrangementOrder.ArrangementOrderLine);
                                TerrainBattlePosition.Enqueue(formation.Team, formation, now, shieldWall ? "shieldWall" : "line", "hold",
                                    "shields=" + RBMAI.Utilities.FormationRatioShieldWallEligible(formation).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                                    + " underFire=" + formation.QuerySystem.UnderRangedAttackRatio.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                            }
                        }
                        break;
                    }
                case Role.Archers:
                    // a fixed post behind the line (the cover point too): Line / Loose by the situation (ChooseRangedArrangement, every tick); a formation outside that rule keeps once-per-activation Loose
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

        private static float LineWidth(Formation formation, float planWidth)
        {
            int units = System.Math.Max(1, formation.CountOfUnitsWithoutDetachedOnes);
            int files = System.Math.Max(1, (int)(units / MinRanks));
            float maxWidth = (files - 1) * (formation.Interval + formation.UnitDiameter) + formation.UnitDiameter;
            float width = float.IsNaN(planWidth) ? maxWidth : MathF.Min(planWidth, maxWidth);
            return MathF.Max(width, MathF.Min(MinWidth, maxWidth));
        }

        /// <summary>" width=W/line L" for the line's arrival lines; "" for the other roles.</summary>
        private string WidthNote(TerrainBattlePosition.Plan plan)
        {
            if (_role != Role.Line || plan == null)
            {
                return "";
            }
            return " width=" + TerrainBattlePosition.Fmt0(LineWidth(base.Formation, plan.LineWidth)) + "/line " + TerrainBattlePosition.Fmt0(plan.LineWidth) + "m";
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
            if (why != null && base.Formation != null && (old != state || why.StartsWith("replan")))
            {
                string point = (_role == Role.Archers) ? "archer" : (_role == Role.Reserve) ? "reserve" : "hold";
                TerrainBattlePosition.Enqueue(base.Formation.Team, base.Formation, now, old + "->" + state, point, why);
            }
        }

        public override TextObject GetBehaviorString()
        {
            return new TextObject("{=RBMHoldPosition01}holding the position");
        }

        protected override float GetAiWeight()
        {
            if (base.Formation.CountOfUnits == 0 || _engaged)
            {
                return 0f;
            }
            Vec2 point;
            WorldPosition world;
            string name;
            if (!TryGetPoint(TerrainBattlePosition.PlanFor(base.Formation.Team), out point, out world, out name))
            {
                return 0f;
            }
            Mission mission = Mission.Current;
            if (_role == Role.Reserve && mission != null && base.Formation.Team != null)
            {
                // a threatened reserve goes to the tactic's fallback for a while
                string what;
                string why;
                bool released = _reserveRelease.Released(mission, base.Formation, mission.CurrentTime, out what, out why);
                if (what != null)
                {
                    TerrainBattlePosition.Enqueue(base.Formation.Team, base.Formation, mission.CurrentTime, what, "reserve", why);
                }
                if (released)
                {
                    return 0f;
                }
            }
            return LiveWeight;
        }

        /// <summary>Cavalry Reserve release: while Threat holds, and FreeSeconds after, the behavior weighs 0 and the tactic's fallback takes the formation. Horse archers release it only when they hit the reserve itself hard and close (the fallback can only charge). One per behavior, AI thread (GetAiWeight): cached data and managed melee-hit times; no engine call.</summary>
        internal sealed class ReserveRelease
        {
            // Edge gaps in m.
            internal const float CavalryGap = 60f; // significant melee cavalry closing at HoldLateCharge.ClosingSpeed ...
            internal const float CavalryCloseGap = 30f; // ... or this close at all
            internal const float HorseArcherRange = 80f; // significant HA ...
            internal const float ShotRatio = 0.15f; // ... whose arrows land on the RESERVE ITSELF (UnderRangedAttackRatio)
            internal const float FreeSeconds = 15f; // released this long after the threat last held
            private const float CheckSeconds = 1f;

            private float _checkedAt = float.NegativeInfinity;
            private float _freeUntil = float.NegativeInfinity;
            private bool _released;

            internal void Reset()
            {
                _checkedAt = float.NegativeInfinity;
                _freeUntil = float.NegativeInfinity;
                _released = false;
            }

            /// <summary>what = "reserve release" / "reserve resume" on a change, else null.</summary>
            internal bool Released(Mission mission, Formation reserve, float now, out string what, out string why)
            {
                what = null;
                why = null;
                if (now - _checkedAt < CheckSeconds)
                {
                    return _released;
                }
                _checkedAt = now;
                string threat = Threat(mission, reserve);
                if (threat != null)
                {
                    if (!_released)
                    {
                        what = "reserve release";
                        why = threat;
                    }
                    _released = true;
                    _freeUntil = now + FreeSeconds;
                }
                else if (_released && now >= _freeUntil)
                {
                    _released = false;
                    what = "reserve resume";
                    why = "no threat for " + TerrainBattlePosition.Fmt0(FreeSeconds) + "s";
                }
                return _released;
            }

            internal static string Threat(Mission mission, Formation reserve)
            {
                if (mission == null || reserve == null || reserve.Team == null || reserve.CountOfUnits <= 0)
                {
                    return null;
                }
                Vec2 centre = RBMAI.Utilities.GetFormationCenter(reserve);
                if (!centre.IsValid)
                {
                    return null;
                }
                float ownRadius = MathF.Max(reserve.Width, reserve.Depth) * 0.5f;
                Footprint ownShape;
                if (!FormationGeometry.TryGet(reserve, FootprintMode.Current, out ownShape))
                {
                    return null;
                }
                foreach (Team team in mission.Teams)
                {
                    if (team == null || !team.IsEnemyOf(reserve.Team))
                    {
                        continue;
                    }
                    foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
                    {
                        if (e == null || e.CountOfUnits <= 0)
                        {
                            continue;
                        }
                        bool horseArchers = e.QuerySystem.IsRangedCavalryFormation;
                        if (!horseArchers && !e.QuerySystem.IsCavalryFormation)
                        {
                            continue;
                        }
                        Vec2 c = RBMAI.Utilities.GetFormationCenter(e);
                        Footprint shape;
                        if (!c.IsValid || !FormationGeometry.TryGet(e, FootprintMode.ScatterAware, out shape))
                        {
                            continue;
                        }
                        Vec2 toUs = centre - c;
                        float distance = toUs.Normalize();
                        float gap = FormationGeometry.Gap(ownShape, shape);
                        if (gap > HorseArcherRange || !ThreatSignificance.IsSignificant(reserve, e))
                        {
                            continue;
                        }
                        float gapOld = FormationGeometry.ShadowOn ? MathF.Max(0f, distance - MathF.Max(e.Width, e.Depth) * 0.5f - ownRadius) : float.NaN;
                        if (!horseArchers)
                        {
                            Vec2 v = e.CachedCurrentVelocity;
                            float closing = (v.IsValid && distance > 0.01f) ? Vec2.DotProduct(v, toUs) : 0f;
                            if (gap <= CavalryCloseGap || (gap <= CavalryGap && closing >= HoldLateCharge.ClosingSpeed))
                            {
                                return "cavalry " + ThreatSignificance.Describe(reserve, e) + " gap=" + FormationGeometry.Shadow(gap, gapOld)
                                    + " closing=" + closing.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "m/s";
                            }
                            continue;
                        }
                        if (reserve.QuerySystem.UnderRangedAttackRatio >= ShotRatio)
                        {
                            return "horseArchers " + ThreatSignificance.Describe(reserve, e) + " gap=" + FormationGeometry.Shadow(gap, gapOld)
                                + " hitting " + TerrainCavalry.FormationName(reserve) + " underFire=" + TerrainBattlePosition.Fmt2(reserve.QuerySystem.UnderRangedAttackRatio);
                        }
                    }
                }
                float melee = HoldLateCharge.MeleeShare(reserve);
                if (melee >= HoldLateCharge.ContactMeleeShare)
                {
                    return "melee=" + TerrainBattlePosition.Fmt2(melee);
                }
                return null;
            }
        }
    }
}
