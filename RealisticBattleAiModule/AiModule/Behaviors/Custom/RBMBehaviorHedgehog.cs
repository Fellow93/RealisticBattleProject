using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The hold phase of RBMTacticHedgehog: the main infantry stands in a CIRCLE with the archers inside (a SQUARE without archers,
    /// a circle too when surrounded) against a strong enemy cavalry arm. Weight 0 unless the tactic sets it (TryAssign);
    /// LiveWeight while it holds, 0 once engaged, so the tactic's fallback takes over.
    ///
    /// Roles (set by the tactic):
    ///  - Infantry: Move to the defender's battle position hold point (a new plan version sends it Moving) or, without a plan, a copy
    ///    of its own cached median at assignment (no WorldPosition is built here). States Wait -> Moving -> Holding (within
    ///    ArriveDistance + a quarter depth; back to Moving beyond LeaveDistance). Line while moving; significant enemy melee cavalry
    ///    within MarchHaltCavalry HALTS the march and forms up until none is within MarchResumeCavalry (UpdateHalt). At the point: a
    ///    CIRCLE while archers stand inside (native's Square is RectilinearSchiltronFormation: ignores width, forms a solid block), else
    ///    Square; Circle too when enemy formations (>= SurroundMinMen, within SurroundRange) fill >= CircleQuadrants of the four quadrants,
    ///    back to Square at SquareQuadrants or fewer; at most one arrangement change per ArrangementMinSeconds (first forming immediate).
    ///    The circle's hollow is sized for the archers inside (native BehaviorDefensiveRing's formula), refreshed every WidthRefreshSeconds.
    ///  - Archers: inside the hedgehog, fire at will. The Move point is the FRONT-centre, so the archer point is the infantry's point moved back
    ///    along its facing by (infantry depth - archer depth) / 2 in a square / circle, by depth + ArcherBehindGap while it marches: a copy of
    ///    its WorldPosition with ONE SetVec2MT, rebuilt only when the point / facing / offset moves. Shape (ChooseRangedArrangement, fixed post):
    ///    LINE, LOOSE under fire / broken ground, sqrt(men) files; a LOOSE CIRCLE ("ring") while the infantry holds a CIRCLE. During the
    ///    infantry's LOCAL charge they hold their last point unless significant enemy melee cavalry is within RangedCavalryWait: then they keep
    ///    ArcherBehindGap behind the charging infantry (FollowInfantry). A player-controlled infantry gives them no point (TryGetCentre).
    ///  - Infantry in-place re-form (the tactic's recall far from the position) ends (InPlaceEnd) on a new plan version or once no significant
    ///    enemy melee cavalry is within InPlaceCalmCavalry - never with significant enemy foot within 60 m.
    ///    Native BehaviorFireFromInfantryCover is registered on field formations but unused: it stands 0.33 x the main's depth behind, which
    ///    in a square / circle is in the ranks (and sets Square).
    ///    A mostly-missile Infantry-role formation never squares up: Line / Loose (or the loose circle when surrounded without infantry cover).
    ///
    /// LATE CHARGE (Infantry role; user rule: charge as late as possible, and it does NOT break for enemy cavalry): HoldLateCharge's
    /// Holding -> Contact (fights from its slots) -> Charge, latched per assignment, on its triggers (lineGone / pushedBack / behind - foot
    /// only / advantage / broken) or this behavior's own:
    ///  - outshot (HoldLateCharge.OutshotReason): ranged power >= 2.5 x ours with hits landing and killing, no significant melee coming in, before contact;
    ///  - infantry (wait for cavalry, charge infantry): significant enemy FOOT within ContactStartGap with the square in melee or that foot closing;
    ///    the tactic re-forms the square after it (CheckRecall: leash, cavalry recall), quiet for RecallQuietSeconds;
    ///  - allyFighting: a friend within LateAllyFightGap in melee with significant enemy foot and no significant melee cavalry within RangedCavalryWait; local;
    ///  - cavalryGone: enemy MELEE cavalry (TerrainHedgehog.EnemyMeleeCavalryNow) under CavalryGoneShare of the publish's AND significant enemy foot in contact.
    /// The owning tactic reads ChargedFor and runs its engage. State changes are BEHAVIOR lines (TerrainHedgehog.Enqueue); StateName feeds the decision layer.
    ///
    /// THREADING: CalculateCurrentOrder / TickOccasionally / GetAiWeight run on the async AI thread: immutable published plans, cached formation data,
    /// copies of cached WorldPositions; no engine query, no WorldPosition construction, no MBRandom. StateName is a volatile string for the main thread.
    /// </summary>
    internal class RBMBehaviorHedgehog : BehaviorComponent
    {
        internal enum Role
        {
            Infantry,
            Archers
        }

        internal enum HedgeState
        {
            Wait,
            Moving,
            Holding
        }


        private const float LiveWeight = 20f;
        private const float ArriveDistance = 15f; // m
        private const float LeaveDistance = 35f; // m
        private const float ArrangementMinSeconds = 10f;
        private const float WidthRefreshSeconds = 5f;

        // Spacing of a MELEE infantry circle: the Square's / ShieldWall's 0 (native gives Circle 2). ArrangementOrder.OnApply resets spacing on every arrangement change, so it is re-asserted.
        private const int MeleeCircleSpacing = 0;

        private const float MinWidth = 10f; // m
        private const float HollowMargin = 2f; // m round the archers inside the circle
        private const float MinHollowRadius = 3f; // m, without archers
        internal const float CavalryGoneShare = 0.3f; // enemy mounted men under this share of the publish's: with foot in contact, charge
        private const float SurroundRange = 150f; // m
        private const int SurroundMinMen = 5;
        private const int CircleQuadrants = 3;
        private const int SquareQuadrants = 1;
        private const float ArcherBehindGap = 3f; // m behind the infantry's line while it marches
        private const float ArcherTargetSlack = 1.5f; // m; the archers' point is rebuilt only past this ...
        private const float ArcherTargetTurnCos = 0.985f; // ... or when the infantry's facing turned ~10 deg
        private const float MarchHaltCavalry = 150f; // m edge gap, significant melee cavalry: halt and form up
        private const float MarchResumeCavalry = 200f; // m
        private const float InPlaceCalmCavalry = 200f; // m; an in-place re-form ends once none is this close
        private const float FollowRebuildDistance = 5f; // m (FollowInfantry's cached point)
        private const float FollowRebuildSeconds = 2f;

        private Role _role;
        private TacticComponent _owner;
        private int _ownerGeneration = -1;
        /// <summary>Archers: the infantry they stand inside.</summary>
        private Formation _partner;
        /// <summary>Infantry: hold where it stood at the assignment, ignoring the battle position (until InPlaceEnd).</summary>
        private bool _inPlace;
        /// <summary>0 = none.</summary>
        private int _inPlaceVersion;
        /// <summary>Archers following the charging infantry: cached point (copy of its median, ONE SetVec2MT) and the facing built with it; stale = rebuild at the next follow. AI thread.</summary>
        private bool _following;
        private readonly CachedMovePoint _follow = new CachedMovePoint();
        private bool _followStale;
        private Vec2 _followFacing = Vec2.Invalid;
        private TerrainBattlePosition.Plan _plan;
        private int _planVersion;
        private WorldPosition _anchor = WorldPosition.Invalid;
        private bool _hasAnchor;
        private WorldPosition _centreWorld = WorldPosition.Invalid;
        private bool _hasCentre;
        private HedgeState _state = HedgeState.Wait;
        private float _stateSince = float.NaN;
        private bool _circle;
        private float _arrangedAt = float.NegativeInfinity;
        private float _widthAt = float.NegativeInfinity;
        private float _widthSet = float.NaN;
        private bool _shaped;
        private ArrangementOrder.ArrangementOrderEnum _archerArrangement = ArrangementOrder.ArrangementOrderEnum.Line;
        /// <summary>Copy of the infantry's WorldPosition, moved once; the fields below record what it was built from.</summary>
        private WorldPosition _archerTarget = WorldPosition.Invalid;
        private bool _hasArcherTarget;
        private Vec2 _archerTargetSource = Vec2.Invalid;
        private float _archerTargetBack;
        private Vec2 _archerTargetFacing = Vec2.Invalid;
        /// <summary>AI thread.</summary>
        private readonly HoldLateCharge _fight = new HoldLateCharge();
        /// <summary>Set in CalculateCurrentOrder (pushedBack). AI thread.</summary>
        private Vec2 _holdPoint = Vec2.Invalid;
        private Vec2 _holdFacing = Vec2.Invalid;
        /// <summary>Halted on the march by enemy cavalry, formed up at _haltAnchor (a copy of its cached median). AI thread.</summary>
        private bool _halted;
        private bool _hasHaltAnchor;
        private WorldPosition _haltAnchor = WorldPosition.Invalid;
        private volatile bool _engaged;
        /// <summary>The friend allyFighting fired for this tick / the latched charge went to (ReactionMonitor's line-answers handshake). AI thread.</summary>
        private Formation _allyFightingFor;
        private Formation _allyAnswered;
        private volatile string _stateName = "wait";

        public RBMBehaviorHedgehog(Formation formation)
            : base(formation)
        {
            _behaviorSide = formation.AI.Side;
            base.CurrentOrder = MovementOrder.MovementOrderStop;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        }

        /// <summary>"square", "circle", "moving 40m", "contact square", "charge advantage", "archers inside". Any thread.</summary>
        internal string StateName => _stateName;

        internal Role CurrentRole => _role;

        /// <summary>Charge latched (any owner). AI thread.</summary>
        internal bool Engaged => _engaged;

        /// <summary>AI thread.</summary>
        internal Formation AllyAnswered => _engaged ? _allyAnswered : null;

        /// <summary>Read by the reaction monitor's balance re-pick. AI thread.</summary>
        internal HoldLateCharge.FightState FightState => _fight.State;

        /// <summary>AI thread, from RBMTacticHedgehog (weight 1; LiveWeight while it holds). partner = the infantry for the Archers role.</summary>
        internal static bool TryAssign(Formation formation, TacticComponent owner, int generation, Role role, Formation partner, bool inPlace = false,
            float quietUntil = float.NegativeInfinity)
        {
            if (formation == null || formation.AI == null)
            {
                return false;
            }
            RBMBehaviorHedgehog behavior = formation.AI.GetBehavior<RBMBehaviorHedgehog>();
            if (behavior == null)
            {
                return false;
            }
            formation.AI.SetBehaviorWeight<RBMBehaviorHedgehog>(1f);
            behavior.Assign(owner, generation, role, partner, inPlace, quietUntil);
            return true;
        }

        /// <summary>AI thread: the infantry hedgehog assigned by owner has charged (latched); reason word.</summary>
        internal static bool ChargedFor(Formation formation, TacticComponent owner, out string reason)
        {
            reason = null;
            if (formation == null || formation.AI == null || owner == null)
            {
                return false;
            }
            RBMBehaviorHedgehog behavior = formation.AI.GetBehavior<RBMBehaviorHedgehog>();
            if (behavior == null || behavior._owner != owner || behavior._role != Role.Infantry || !behavior._engaged)
            {
                return false;
            }
            reason = behavior._fight.ChargeReason;
            return true;
        }

        /// <summary>A new tactic instance, application or role restarts from Wait (un-latches the engage). inPlace = square up where it stands. quietUntil (re-form after a local charge): until then only the square in melee can charge.</summary>
        internal void Assign(TacticComponent owner, int generation, Role role, Formation partner, bool inPlace, float quietUntil = float.NegativeInfinity)
        {
            _partner = partner;
            if (owner == _owner && generation == _ownerGeneration && role == _role)
            {
                return;
            }
            _inPlace = inPlace;
            TerrainBattlePosition.Plan positionNow = (base.Formation != null) ? TerrainBattlePosition.PlanFor(base.Formation.Team) : null;
            _inPlaceVersion = (positionNow != null) ? positionNow.Version : 0;
            _following = false;
            _follow.Reset();
            _followStale = false;
            _followFacing = Vec2.Invalid;
            _owner = owner;
            _ownerGeneration = generation;
            _role = role;
            _plan = null;
            _planVersion = 0;
            _hasAnchor = false;
            _anchor = WorldPosition.Invalid;
            _hasCentre = false;
            _centreWorld = WorldPosition.Invalid;
            _circle = false;
            _arrangedAt = float.NegativeInfinity;
            _widthAt = float.NegativeInfinity;
            _widthSet = float.NaN;
            _shaped = false;
            _hasArcherTarget = false;
            _archerTarget = WorldPosition.Invalid;
            _fight.Reset(base.Formation, quietUntil);
            _halted = false;
            _hasHaltAnchor = false;
            _haltAnchor = WorldPosition.Invalid;
            _holdPoint = Vec2.Invalid;
            _holdFacing = Vec2.Invalid;
            _engaged = false;
            _allyAnswered = null;
            SetState(HedgeState.Wait, float.NaN, null);
        }

        /// <summary>AI thread. False before it has one.</summary>
        private bool TryGetCentre(out WorldPosition world)
        {
            world = _centreWorld;
            // a player-controlled infantry never ticks this behavior: its point would be stale (the archers fall back to ArcherSkirmish)
            return _hasCentre && !_engaged && base.Formation != null && base.Formation.IsAIControlled;
        }

        /// <summary>Local reason: the archers keep their last point inside. AI thread.</summary>
        private bool ChargingLocally => _engaged && HoldLateCharge.IsLocalChargeReason(_fight.ChargeReason);

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
            if (_role == Role.Archers)
            {
                CalculateArcherOrder();
                return;
            }
            float now = mission.CurrentTime;
            if (_engaged)
            {
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            if (_inPlace)
            {
                // an in-place re-form (after a recall far from the position) must end
                string end = InPlaceEnd(mission, formation);
                if (end != null)
                {
                    _inPlace = false;
                    _hasAnchor = false;
                    _fight.ReachedHold = false;
                    TerrainHedgehog.Enqueue(formation.Team, formation, now, "inPlace end", end + ": back to the battle position");
                    SetState(HedgeState.Wait, now, null);
                }
            }
            TerrainBattlePosition.Plan plan = _inPlace ? null : TerrainBattlePosition.PlanFor(formation.Team);
            _plan = plan;
            Vec2 point;
            WorldPosition world;
            string where;
            if (plan != null)
            {
                point = plan.Hold;
                world = plan.HoldWorld;
                where = "position " + plan.KindName;
            }
            else
            {
                if (!_hasAnchor)
                {
                    // where it stands now: a copy of the cached median position (no WorldPosition built here)
_anchor = formation.CachedMedianPosition;
                    _hasAnchor = _anchor.AsVec2.IsValid;
                }
                if (!_hasAnchor)
                {
                    base.CurrentOrder = MovementOrder.MovementOrderStop;
                    CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                    return;
                }
                point = _anchor.AsVec2;
                world = _anchor;
                where = "inPlace";
            }
            _holdPoint = point;
            _holdFacing = (plan != null) ? plan.Facing : formation.Direction;
            Vec2 position = RBMAI.Utilities.GetFormationCenter(formation);
            if (position.IsValid)
            {
                float distance = position.Distance(point);
                float arrive = ArriveDistance + formation.Depth * 0.25f;
                if (_state != HedgeState.Wait && plan != null && _planVersion != 0 && plan.Version != _planVersion && distance > arrive)
                {
                    // a new hold: not held yet (a stale ReachedHold would read the march to a further hold as "pushedBack")
                    _fight.ReachedHold = false;
                    SetState(HedgeState.Moving, now, "replan v" + _planVersion.ToString(CultureInfo.InvariantCulture)
                        + "->v" + plan.Version.ToString(CultureInfo.InvariantCulture) + " d=" + TerrainHedgehog.Fmt0(distance) + "m");
                }
                else if (_state == HedgeState.Wait)
                {
                    SetState(distance <= arrive ? HedgeState.Holding : HedgeState.Moving, now,
                        (distance <= arrive ? "atPoint" : "toPoint") + " d=" + TerrainHedgehog.Fmt0(distance) + "m " + where);
                }
                else if (_state == HedgeState.Moving && distance <= arrive)
                {
                    SetState(HedgeState.Holding, now, "arrived after " + TerrainHedgehog.Fmt0(now - _stateSince) + "s " + where);
                }
                else if (_state == HedgeState.Holding && distance > LeaveDistance + formation.Depth * 0.25f)
                {
                    SetState(HedgeState.Moving, now, "offPoint d=" + TerrainHedgehog.Fmt0(distance) + "m");
                }
                else if (_state == HedgeState.Moving)
                {
                    _stateName = "moving " + TerrainHedgehog.Fmt0(distance) + "m";
                }
                if (plan != null)
                {
                    _planVersion = plan.Version;
                }
            }
            // marching with enemy cavalry about: form up where it stands instead (UpdateHalt)
            UpdateHalt(mission, formation, now);
            if (_halted && _hasHaltAnchor)
            {
                world = _haltAnchor;
                _stateName = "halted " + (_circle ? "circle" : "square");
            }
            _centreWorld = world;
            _hasCentre = true;
            if (_state == HedgeState.Holding)
            {
                _fight.ReachedHold = true;
                _stateName = _circle ? "circle" : "square";
            }
            string fight = _fight.Label();
            if (fight != null)
            {
                // in contact it fights where it stands: same Move order, same shape
                _stateName = fight + " " + (_circle ? "circle" : "square");
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(world);
            CurrentFacingOrder = (plan != null) ? FacingOrder.FacingOrderLookAtDirection(plan.Facing) : FacingOrder.FacingOrderLookAtEnemy;
        }

        /// <summary>AI thread: halt while significant MELEE cavalry is within MarchHaltCavalry, resume past MarchResumeCavalry. Arriving, holding or a ranged formation ends the halt.</summary>
        private void UpdateHalt(Mission mission, Formation formation, float now)
        {
            if (_state != HedgeState.Moving || RBMAI.Utilities.IsRangedArrangementSubject(formation) || RBMAI.Utilities.IsRangedForArrangement(formation))
            {
                if (_halted)
                {
                    _halted = false;
                    _hasHaltAnchor = false;
                    TerrainHedgehog.Enqueue(formation.Team, formation, now, "march resume", "state=" + _state.ToString().ToLowerInvariant());
                }
                return;
            }
            float gap;
            Formation cavalry = HoldLateCharge.NearestSignificantEnemyCavalry(mission, formation, out gap);
            if (!_halted)
            {
                if (cavalry == null || gap > MarchHaltCavalry)
                {
                    return;
                }
                // where it stands now: a copy of the cached median position (no WorldPosition built here)
                _haltAnchor = formation.CachedMedianPosition;
                _hasHaltAnchor = _haltAnchor.AsVec2.IsValid;
                if (!_hasHaltAnchor)
                {
                    return;
                }
                _halted = true;
                TerrainHedgehog.Enqueue(formation.Team, formation, now, "march halt",
                    "cavalry " + ThreatSignificance.Describe(formation, cavalry) + " gap=" + FormationGeometry.GapText(gap) + " (<="
                    + TerrainHedgehog.Fmt0(MarchHaltCavalry) + "): form up where it stands, " + TerrainHedgehog.Fmt0(RBMAI.Utilities.GetFormationCenter(formation).Distance(_holdPoint)) + "m short of the point");
            }
            else if (cavalry == null || gap > MarchResumeCavalry)
            {
                _halted = false;
                _hasHaltAnchor = false;
                TerrainHedgehog.Enqueue(formation.Team, formation, now, "march resume",
                    "no significant enemy melee cavalry within " + TerrainHedgehog.Fmt0(MarchResumeCavalry) + "m"
                    + (cavalry != null ? " (nearest gap=" + FormationGeometry.GapText(gap) + ")" : ""));
            }
        }

        /// <summary>AI thread: why the in-place re-form ends ("replan" / "calm"), else null. Never with significant enemy foot within RecallFootGap.</summary>
        private string InPlaceEnd(Mission mission, Formation formation)
        {
            TerrainBattlePosition.Plan plan = TerrainBattlePosition.PlanFor(formation.Team);
            if (plan == null)
            {
                return null;
            }
            float foot = HoldLateCharge.NearestEnemyFootGap(mission, formation);
            if (foot <= HoldLateCharge.RecallFootGap)
            {
                return null;
            }
            string footText = " foot=" + FormationGeometry.GapText(foot);
            if (_inPlaceVersion != 0 && plan.Version != _inPlaceVersion)
            {
                return "replan v" + _inPlaceVersion.ToString(CultureInfo.InvariantCulture) + "->v" + plan.Version.ToString(CultureInfo.InvariantCulture) + footText;
            }
            float cavalryGap;
            Formation cavalry = HoldLateCharge.NearestSignificantEnemyCavalry(mission, formation, out cavalryGap);
            if (cavalry == null || cavalryGap > InPlaceCalmCavalry)
            {
                return "calm cavalry=" + (cavalry != null ? FormationGeometry.GapText(cavalryGap) : "none") + footText;
            }
            return null;
        }

        /// <summary>AI thread: Move point (front-centre) ArcherBehindGap behind the charging infantry's rear. Copy of its cached median with ONE SetVec2MT; false without the infantry's geometry.</summary>
        private bool FollowInfantry(Formation infantry, float now, out WorldPosition target, out Vec2 facing)
        {
            target = WorldPosition.Invalid;
            facing = Vec2.Invalid;
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(infantry);
            Vec2 dir = infantry.Direction;
            if (!dir.IsValid || dir.LengthSquared < 0.01f)
            {
                dir = _archerTargetFacing;
            }
            WorldPosition source = infantry.CachedMedianPosition;
            if (!centre.IsValid || !dir.IsValid || dir.LengthSquared < 0.01f || !source.AsVec2.IsValid)
            {
                return false;
            }
            dir = dir.Normalized();
            Vec2 wanted = centre - dir * (infantry.Depth * 0.5f + ArcherBehindGap);
            BattleTerrain terrain = BattleTerrain.Current;
            if (terrain != null && !terrain.IsWalkable(wanted))
            {
                wanted = centre;
            }
            bool rebuilt;
            _follow.TryGet(wanted, now, source, _followStale, FollowRebuildDistance, FollowRebuildSeconds, out target, out rebuilt);
            _followStale = false;
            if (rebuilt)
            {
                _followFacing = dir;
            }
            facing = _followFacing;
            return true;
        }

        /// <summary>Infantry's centre; during its LOCAL charge their last point, or behind it with enemy cavalry near (FollowInfantry).</summary>
        private void CalculateArcherOrder()
        {
            WorldPosition centre;
            RBMBehaviorHedgehog infantry = InfantryHedgehog();
            if (infantry != null && infantry.TryGetCentre(out centre))
            {
                base.CurrentOrder = MovementOrder.MovementOrderMove(ArcherTarget(infantry, centre));
                CurrentFacingOrder = (_hasArcherTarget && _archerTargetFacing.IsValid) ? FacingOrder.FacingOrderLookAtDirection(_archerTargetFacing) : FacingOrder.FacingOrderLookAtEnemy;
                _stateName = "archers inside";
                if (_state == HedgeState.Wait)
                {
                    SetState(HedgeState.Holding, Mission.Current.CurrentTime, "toCentre of " + TerrainCavalry.FormationName(_partner));
                    _stateName = "archers inside";
                }
            }
            else if (infantry != null && infantry.ChargingLocally && _hasArcherTarget)
            {
                // a local charge is the infantry's alone: hold the last point (cached copy) - unless significant enemy melee cavalry is near the archers: then keep behind the charging infantry
                Mission mission = Mission.Current;
                float now = (mission != null) ? mission.CurrentTime : 0f;
                float cavalryGap = float.MaxValue;
                Formation cavalry = (mission != null) ? HoldLateCharge.NearestSignificantEnemyCavalry(mission, base.Formation, out cavalryGap) : null;
                WorldPosition followTarget;
                Vec2 followFacing;
                if (cavalry != null && cavalryGap <= HoldLateCharge.RangedCavalryWait && FollowInfantry(infantry.Formation, now, out followTarget, out followFacing))
                {
                    if (!_following)
                    {
                        _following = true;
                        TerrainHedgehog.Enqueue(base.Formation.Team, base.Formation, now, "archers follow",
                            "infantry charging, cavalry " + ThreatSignificance.Describe(base.Formation, cavalry) + " gap=" + FormationGeometry.GapText(cavalryGap));
                    }
                    base.CurrentOrder = MovementOrder.MovementOrderMove(followTarget);
                    CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(followFacing);
                    _stateName = "archers follow (infantry charging)";
                }
                else
                {
                    if (_following)
                    {
                        _following = false;
                        _followStale = true;
                        TerrainHedgehog.Enqueue(base.Formation.Team, base.Formation, now, "archers hold",
                            "no significant enemy melee cavalry within " + TerrainHedgehog.Fmt0(HoldLateCharge.RangedCavalryWait) + "m: hold here");
                        // hold where they are now (the last point would walk them back alone)
                        if (_follow.Point.AsVec2.IsValid)
                        {
                            _archerTarget = _follow.Point;
                            _archerTargetFacing = _followFacing;
                            // not the inside point any more: ArcherTarget builds it again once the square re-forms
                            _archerTargetSource = Vec2.Invalid;
                        }
                    }
                    base.CurrentOrder = MovementOrder.MovementOrderMove(_archerTarget);
                    CurrentFacingOrder = _archerTargetFacing.IsValid ? FacingOrder.FacingOrderLookAtDirection(_archerTargetFacing) : FacingOrder.FacingOrderLookAtEnemy;
                    _stateName = "archers hold (infantry charging)";
                }
            }
            else
            {
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                _stateName = "archers wait";
            }
        }

        /// <summary>Front-centre rule: the infantry's point moved back along its facing. A copy moved ONCE (SetVec2MT), kept until point / facing / offset move enough. AI thread: managed reads only.</summary>
        private WorldPosition ArcherTarget(RBMBehaviorHedgehog infantry, WorldPosition centre)
        {
            Formation partner = infantry.Formation;
            Formation own = base.Formation;
            Vec2 source = centre.AsVec2;
            Vec2 facing = partner.Direction;
            if (!facing.IsValid || facing.LengthSquared < 0.01f)
            {
                facing = infantry._holdFacing;
            }
            if (!source.IsValid || !facing.IsValid || facing.LengthSquared < 0.01f)
            {
                _hasArcherTarget = false;
                return centre;
            }
            facing = facing.Normalized();
            ArrangementOrder.ArrangementOrderEnum shape = partner.ArrangementOrder.OrderEnum;
            bool hedged = shape == ArrangementOrder.ArrangementOrderEnum.Square || shape == ArrangementOrder.ArrangementOrderEnum.Circle;
            float back = hedged ? MathF.Max(0f, (partner.Depth - own.Depth) * 0.5f) : partner.Depth + ArcherBehindGap;
            if (_hasArcherTarget && source == _archerTargetSource && MathF.Abs(back - _archerTargetBack) <= ArcherTargetSlack
                && facing.DotProduct(_archerTargetFacing) >= ArcherTargetTurnCos)
            {
                return _archerTarget;
            }
            WorldPosition target = centre;
            if (back > 0.05f)
            {
                target.SetVec2MT(source - facing * back);
            }
            _archerTarget = target;
            _archerTargetSource = source;
            _archerTargetBack = back;
            _archerTargetFacing = facing;
            _hasArcherTarget = true;
            return target;
        }

        /// <summary>Archers role: the partner's hedgehog behavior when it was assigned by the same tactic, else null.</summary>
        private RBMBehaviorHedgehog InfantryHedgehog()
        {
            Formation partner = _partner;
            if (partner == null || partner.CountOfUnits <= 0 || partner.AI == null)
            {
                return null;
            }
            RBMBehaviorHedgehog infantry = partner.AI.GetBehavior<RBMBehaviorHedgehog>();
            return (infantry != null && infantry._owner == _owner && infantry._role == Role.Infantry) ? infantry : null;
        }

        /// <summary>Infantry role, AI thread: the late charge; logs each change, latches the charge.</summary>
        private void CheckEngage()
        {
            if (_engaged || _role != Role.Infantry || _state == HedgeState.Wait)
            {
                return;
            }
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (mission == null || formation == null || formation.Team == null)
            {
                return;
            }
            float now = mission.CurrentTime;
            _fight.Read(mission, formation, _holdFacing, true, true);
            _allyFightingFor = null;
            string own = OwnChargeReason(mission, formation);
            string why;
            string what = _fight.Decide(formation, now, _holdPoint, _holdFacing, own, out why);
            if (what == null)
            {
                return;
            }
            if (_fight.State == HoldLateCharge.FightState.Charge)
            {
                _engaged = true;
                _allyAnswered = (_fight.ChargeReason == "allyFighting") ? _allyFightingFor : null;
            }
            TerrainHedgehog.Enqueue(formation.Team, formation, now, what, why + " men=" + formation.CountOfUnits.ToString(CultureInfo.InvariantCulture));
            string fight = _fight.Label();
            _stateName = (fight != null) ? (_engaged ? fight : fight + " " + (_circle ? "circle" : "square")) : (_circle ? "circle" : "square");
        }

        /// <summary>AI thread.</summary>
        private string OwnChargeReason(Mission mission, Formation formation)
        {
            Team own = formation.Team;
            // MELEE cavalry only (TerrainHedgehog counts the same at the publish)
            float enemyMounted = TerrainHedgehog.EnemyMeleeCavalryNow(own);
            TerrainHedgehog.SidePlan plan = TerrainHedgehog.ForSide(own.Side);
            if (_fight.FootInContact && plan != null && plan.EnemyMounted > 0 && enemyMounted < CavalryGoneShare * plan.EnemyMounted)
            {
                return "cavalryGone mounted=" + TerrainHedgehog.Fmt0(enemyMounted) + " of " + plan.EnemyMounted.ToString(CultureInfo.InvariantCulture) + " + foot in contact";
            }
            // user rule: wait for cavalry, charge infantry (CheckRecall re-forms the square); enemy archers at the square while their cavalry is near are bait (rangedWaitsForCavalry)
            string infantry = _fight.InfantryReason(mission, true);
            if (infantry != null)
            {
                return infantry;
            }
            // a friend within LateAllyFightGap in melee with significant enemy foot: a LOCAL charge, only while no significant melee cavalry is within RangedCavalryWait
            Formation answered;
            string ally = HoldLateCharge.AllyFightingReason(mission, formation, HoldLateCharge.LateAllyFightGap, true, out answered);
            if (ally != null)
            {
                _allyFightingFor = answered;
                return ally;
            }
            // user rule: out-shot only when the enemy's ranged power is far higher and its hits land and kill
            return _fight.OutshotReason(mission, formation, mission.CurrentTime);
        }

        /// <summary>Arrangement and width; AI-controlled formations only.</summary>
        private void ApplyShape()
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (mission == null || formation == null || !formation.IsAIControlled || _state == HedgeState.Wait || _engaged)
            {
                return;
            }
            float now = mission.CurrentTime;
            if (_role == Role.Archers)
            {
                // never a shield wall or a tight square / circle. Fixed post inside the infantry: Line, Loose under fire / broken ground (ChooseRangedArrangement, every tick),
                // sqrt(men) files wide (re-sized when the arrangement changes). While the infantry holds a CIRCLE: the helper's LOOSE circle ("ring"); the infantry's hollow is sized for it (CircleWidth).
                // the helper owns that circle's spacing and width order, so no block width is set then
                Formation partner = _partner;
                bool ring = partner != null && partner.CountOfUnits > 0 && partner.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Circle;
                if (ring)
                {
                    RBMBehaviorHedgehog infantry = InfantryHedgehog();
                    ring = infantry != null && infantry._state == HedgeState.Holding && !infantry._engaged;
                }
                if (!RBMAI.Utilities.TryApplyRangedArrangement(formation, ArrangementOrder.ArrangementOrderEnum.Line, true, ring) && !_shaped)
                {
                    formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
                }
                ArrangementOrder.ArrangementOrderEnum shapedAs = formation.ArrangementOrder.OrderEnum;
                if (!_shaped || shapedAs != _archerArrangement)
                {
                    _shaped = true;
                    _archerArrangement = shapedAs;
                    formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
                    if (shapedAs != ArrangementOrder.ArrangementOrderEnum.Circle)
                    {
                        int files = System.Math.Max(1, (int)MathF.Sqrt(formation.CountOfUnits));
                        formation.SetFormOrder(FormOrder.FormOrderCustom(files * formation.UnitDiameter + (files - 1) * formation.Interval));
                    }
                    TerrainHedgehog.Enqueue(formation.Team, formation, now, shapedAs == ArrangementOrder.ArrangementOrderEnum.Circle ? "archers looseCircle" : "archers " + shapedAs.ToString().ToLowerInvariant(),
                        (ring ? "ring" : "block") + " men=" + formation.CountOfUnits.ToString(CultureInfo.InvariantCulture));
                }
                if (shapedAs == ArrangementOrder.ArrangementOrderEnum.Circle)
                {
                    _stateName = "archers ring";
                }
                return;
            }
            ArrangementOrder.ArrangementOrderEnum current = formation.ArrangementOrder.OrderEnum;
            bool hedged = current == ArrangementOrder.ArrangementOrderEnum.Square || current == ArrangementOrder.ArrangementOrderEnum.Circle;
            if (RBMAI.Utilities.IsRangedArrangementSubject(formation))
            {
                // mostly-missile Infantry role: never a square / tight circle; Line / Loose by the situation, a fixed post once it holds; the helper's loose circle when surrounded with no infantry near
                RBMAI.Utilities.TryApplyRangedArrangement(formation, ArrangementOrder.ArrangementOrderEnum.Line, _state == HedgeState.Holding);
                ArrangementOrder.ArrangementOrderEnum applied = formation.ArrangementOrder.OrderEnum;
                if (applied != current)
                {
                    _arrangedAt = now;
                    _widthSet = float.NaN;
                    if (applied != ArrangementOrder.ArrangementOrderEnum.Circle)
                    {
                        formation.SetFormOrder(FormOrder.FormOrderWide);
                    }
                    TerrainHedgehog.Enqueue(formation.Team, formation, now, applied == ArrangementOrder.ArrangementOrderEnum.Circle ? "looseCircle"
                        : applied == ArrangementOrder.ArrangementOrderEnum.Loose ? "loose" : "line", "rangedFormation");
                }
                return;
            }
            if ((_state == HedgeState.Moving && !_halted) || RBMAI.Utilities.IsRangedForArrangement(formation))
            {
                // march in a line; a square crawls
                if (current != ArrangementOrder.ArrangementOrderEnum.Line && (!hedged || now - _arrangedAt >= ArrangementMinSeconds))
                {
                    _arrangedAt = now;
                    _widthSet = float.NaN;
                    formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
                    formation.SetFormOrder(FormOrder.FormOrderWide);
                    TerrainHedgehog.Enqueue(formation.Team, formation, now, "line", (_state == HedgeState.Moving) ? "moving" : "rangedFormation");
                }
                return;
            }
            // holding (or halted): a CIRCLE with archers inside (native's Square is a solid block with no hollow; the circle's width sets its hollow, CircleWidth), else a square, or a circle when surrounded (hysteresis)
            int quadrants = RBMAI.Utilities.EnemyQuadrants(mission, formation, SurroundMinMen, SurroundRange);
            Formation inside = ArchersInside(formation);
            bool wantCircle = inside != null || (_circle ? quadrants > SquareQuadrants : quadrants >= CircleQuadrants);
            ArrangementOrder.ArrangementOrderEnum wanted = wantCircle ? ArrangementOrder.ArrangementOrderEnum.Circle : ArrangementOrder.ArrangementOrderEnum.Square;
            if (current != wanted && (!hedged || now - _arrangedAt >= ArrangementMinSeconds))
            {
                _arrangedAt = now;
                _circle = wantCircle;
                _widthSet = float.NaN;
                _widthAt = float.NegativeInfinity;
                formation.SetArrangementOrder(wantCircle ? ArrangementOrder.ArrangementOrderCircle : ArrangementOrder.ArrangementOrderSquare);
                TerrainHedgehog.Enqueue(formation.Team, formation, now, wantCircle ? "circle" : "square",
                    (inside != null ? "archersInside " + TerrainCavalry.FormationName(inside) + " " : "")
                    + "quadrants=" + quadrants.ToString(CultureInfo.InvariantCulture) + " men=" + formation.CountOfUnits.ToString(CultureInfo.InvariantCulture)
                    + (_halted ? " halted" : ""));
            }
            // a melee circle closes up to the square's spacing; the width below is worked out at that interval
            if (current == ArrangementOrder.ArrangementOrderEnum.Circle && !RBMAI.Utilities.IsRangedForArrangement(formation)
                && formation.UnitSpacing != MeleeCircleSpacing)
            {
                formation.SetPositioning(null, null, MeleeCircleSpacing);
                _widthSet = float.NaN;
                _widthAt = float.NegativeInfinity;
            }
            string fight = _fight.Label();
            _stateName = (fight != null ? fight + " " : "") + (_halted ? "halted " : "") + (_circle ? "circle" : "square");
            // only the circle takes a width (its hollow); the square forms at its maximum ranks whatever the width order
            if (now - _widthAt >= WidthRefreshSeconds && current == ArrangementOrder.ArrangementOrderEnum.Circle)
            {
                _widthAt = now;
                float width = CircleWidth(formation, inside);
                if (float.IsNaN(_widthSet) || MathF.Abs(width - _widthSet) > 1f)
                {
                    _widthSet = width;
                    formation.SetFormOrder(FormOrder.FormOrderCustom(width));
                }
            }
        }

        /// <summary>The Archers-role formation of the same tactic with this one as partner (the largest if several), or null. AI thread: managed reads only.</summary>
        private Formation ArchersInside(Formation formation)
        {
            Formation best = null;
            foreach (Formation f in formation.Team.FormationsIncludingEmpty)
            {
                if (f == null || f == formation || f.CountOfUnits <= 0 || f.AI == null || (best != null && f.CountOfUnits <= best.CountOfUnits))
                {
                    continue;
                }
                RBMBehaviorHedgehog archers = f.AI.GetBehavior<RBMBehaviorHedgehog>();
                if (archers != null && archers._role == Role.Archers && archers._owner == _owner && archers._partner == formation && archers.WeightFactor > 0f)
                {
                    best = f;
                }
            }
            return best;
        }

        /// <summary>m; sqrt(men) files at their current interval, or Line's while they stand in the loose circle.</summary>
        private static float ArcherBlockSide(Formation archers)
        {
            float interval = (archers.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Circle)
                ? Formation.InfantryInterval(ArrangementOrder.GetUnitSpacingOf(ArrangementOrder.ArrangementOrderEnum.Line))
                : archers.Interval;
            float files = MathF.Sqrt(archers.CountOfUnits);
            return archers.UnitDiameter * files + interval * MathF.Max(0f, files - 1f);
        }

        /// <summary>m, spacing included; 0 for a formation the helper does not shape.</summary>
        private static float ArcherRingDiameter(Formation archers)
        {
            if (!RBMAI.Utilities.IsRangedArrangementSubject(archers))
            {
                return 0f;
            }
            return RBMAI.Utilities.PredictLooseCircleDiameter(archers.CountOfUnitsWithoutDetachedOnes, archers.UnitDiameter);
        }

        /// <summary>Native BehaviorDefensiveRing's formula; null archers = the smallest hollow.</summary>
        private static float CircleWidth(Formation formation, Formation archers)
        {
            float hollow = MinHollowRadius;
            if (archers != null)
            {
                hollow = MathF.Max(ArcherBlockSide(archers) * 0.5f * 1.414213f, ArcherRingDiameter(archers) * 0.5f) + HollowMargin;
            }
            int remaining = formation.Arrangement.UnitCount;
            int rings = 0;
            while (remaining > 0 && rings < 50)
            {
                double circumference = (hollow + formation.Distance * rings + formation.UnitDiameter * (rings + 1)) * System.Math.PI * 2.0
                    / (formation.UnitDiameter + formation.Interval);
                remaining -= System.Math.Max(1, (int)System.Math.Ceiling(circumference));
                rings++;
            }
            float radius = hollow + rings * formation.UnitDiameter + (rings - 1) * formation.Distance;
            return MathF.Max(radius * 2f, MinWidth);
        }

        private void SetState(HedgeState state, float now, string why)
        {
            HedgeState old = _state;
            _state = state;
            _stateSince = now;
            _stateName = (_role == Role.Archers ? "archers " : "") + state.ToString().ToLowerInvariant();
            if (why != null && base.Formation != null && (old != state || why.StartsWith("replan")))
            {
                TerrainHedgehog.Enqueue(base.Formation.Team, base.Formation, now, old + "->" + state, why);
            }
        }

        public override TextObject GetBehaviorString()
        {
            return new TextObject("{=RBMHedgehog01}forming a hedgehog");
        }

        protected override float GetAiWeight()
        {
            if (base.Formation.CountOfUnits == 0 || _owner == null)
            {
                return 0f;
            }
            if (_role == Role.Archers)
            {
                WorldPosition centre;
                RBMBehaviorHedgehog infantry = InfantryHedgehog();
                return (infantry != null && (infantry.TryGetCentre(out centre) || (infantry.ChargingLocally && _hasArcherTarget))) ? LiveWeight : 0f;
            }
            return _engaged ? 0f : LiveWeight;
        }
    }
}
