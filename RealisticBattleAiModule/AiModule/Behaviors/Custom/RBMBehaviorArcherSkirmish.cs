using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    internal class RBMBehaviorArcherSkirmish : BehaviorComponent
    {
        public Timer repositionTimer = null;

        public Timer refreshPositionTimer = null;
        public int side = MBRandom.RandomInt(2);

        public bool wasShootingBefore = false;

        private enum BehaviorState
        {
            Approaching,
            Shooting,
            PullingBack
        }

        private BehaviorState _behaviorState = BehaviorState.PullingBack;

        private bool firstTime = true;

        private bool _onSpot;

        /// <summary>AI thread.</summary>
        private readonly CoverSupport _glue = new CoverSupport("ArcherSkirmish", "spot");

        /// <summary>Own Move target before the cohesion clamp; Invalid while on a spot, covering or charging. AI thread.</summary>
        private WorldPosition _ownTarget = WorldPosition.Invalid;

        /// <summary>AI thread.</summary>
        private readonly FormationCohesion.Pull _cohesion = new FormationCohesion.Pull();

        private bool _clamped;

        /// <summary>0..1 x LateralOffsetMax, fixed per activation; deterministic (no MBRandom on the AI thread).</summary>
        private float _lateralRoll = 0.5f;

        private int _activations;

        /// <summary>A re-planned spot is ordered again when it moved.</summary>
        private Vec2 _spotOrdered = Vec2.Invalid;

        private const float LateralOffsetMax = 70f; // m

        private bool _charging;

        /// <summary>NaN: not on a spot / still walking.</summary>
        private float _spotArrivedAt = float.NaN;

        private const float SpotArriveDistance = 10f; // m, front-centre to the ordered spot point

        private const float SpotSettleSeconds = 10f;

        private const float FacingRefresh = 0.5f; // rad

        private Formation _counterFire;

        /// <summary>NaN: never.</summary>
        private float _counterFireHitAt = float.NaN;

        private const float CounterFireHitRatio = 0.05f;

        private const float CounterFireHoldSeconds = 10f;

        private const float CounterFireRangeFactor = 1.3f; // x shooting range; centre distance, half depths included

        // INITIATIVE (no melee-infantry main): idle in reach after SupportFire found nothing, or dry (ArmyComposition.IsDry) -> charge, and the team's mounted
        // ProtectFlank guards with them. Only the active behavior of an AI formation latches it, never a token archer force or an ArmyScale NoPlans side.
        private const float InitiativeIdleSeconds = 20f;
        private const float InitiativeNoFireRatio = 0.05f; // MakingRangedAttackRatio

        /// <summary>Latched for the battle (ProtectFlank reads it on the AI thread); cleared in MissionStartReset.</summary>
        private static readonly ConcurrentDictionary<Team, float> InitiativeTeams = new ConcurrentDictionary<Team, float>();

        /// <summary>Latched for the behavior's life; dropped once the team has a melee-infantry main again.</summary>
        private bool _initiative;

        private float _idleSince = float.NaN;

        // INEFFECTIVE FIRE (user rule 2026-10-30, attacker only): no melee-infantry main, shooting enemy foot in reach that loses under IneffectiveLossShare
        // of its men per IneffectiveSeconds, and not weaker than the enemy: the initiative (a shield wall on a hill would otherwise be shot at forever)
        private const float IneffectiveSeconds = 60f;
        private const float IneffectiveLossShare = 0.05f;
        private float _ineffectiveSince = float.NaN;
        private Formation _ineffectiveEnemy;
        private int _ineffectiveEnemyMen;

        /// <summary>AI thread: the team's archers took the initiative and it still has no melee-infantry main.</summary>
        internal static bool HasInitiative(Team team)
        {
            return team != null && InitiativeTeams.ContainsKey(team) && ArmyComposition.NoMainRoles(team);
        }

        internal static void ClearInitiative()
        {
            InitiativeTeams.Clear();
        }

        /// <summary>AI thread: why this behavior may not take the team's initiative (not active / player / token archers / NoPlans), else null.</summary>
        private string InitiativeBlocked()
        {
            Formation formation = base.Formation;
            if (formation?.Team == null || formation.AI == null || formation.AI.ActiveBehavior != this)
            {
                return "notActive";
            }
            if (!formation.IsAIControlled)
            {
                return "player";
            }
            if (ArmyComposition.Limited(formation.Team))
            {
                return "scale";
            }
            if (ArmyComposition.IsToken(formation.Team, ArmyComposition.Category.Archers))
            {
                return "tokenArchers";
            }
            return null;
        }

        /// <summary>AI thread: the position anchor (main melee infantry, else the strongest other AI foot formation); null when that is itself.</summary>
        private Formation MeleeAnchor()
        {
            return ArmyComposition.AnchorFor(base.Formation);
        }

        /// <summary>AI thread: the kCav either/or place (shared with ArcherFlank).</summary>
        private readonly CavalryBehindPlace _cavPlace = new CavalryBehindPlace("ArcherSkirmish", "-");
        private bool _placeWasBehind;

        private static Vec2 FrontCentre(Formation formation)
        {
            Vec2 centre = formation.CachedAveragePosition;
            Vec2 facing = formation.Direction;
            if (facing.IsValid && facing.LengthSquared > 0.01f)
            {
                centre += facing.Normalized() * (formation.Depth * 0.5f);
            }
            return centre;
        }

        private bool SpotSettling()
        {
            if (!_onSpot || !_spotOrdered.IsValid)
            {
                return false;
            }
            return float.IsNaN(_spotArrivedAt) || Mission.Current.CurrentTime - _spotArrivedAt < SpotSettleSeconds;
        }

        /// <summary>AI thread, cached data only: null unless we are being hit (held 10 s).</summary>
        private Formation CounterFireTarget()
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (formation?.Team == null || mission == null)
            {
                return null;
            }
            float now = mission.CurrentTime;
            float hits = formation.QuerySystem.UnderRangedAttackRatio;
            if (hits >= CounterFireHitRatio)
            {
                _counterFireHitAt = now;
            }
            Formation best = null;
            float bestScore = float.MaxValue;
            float bestDistance = 0f;
            if (!float.IsNaN(_counterFireHitAt) && now - _counterFireHitAt <= CounterFireHoldSeconds)
            {
                Vec2 own = formation.CachedAveragePosition;
                float ownRange = formation.Depth * 0.5f + formation.QuerySystem.MaximumMissileRange * 0.5f;
                foreach (Team team in mission.Teams)
                {
                    if (team == null || !team.IsEnemyOf(formation.Team))
                    {
                        continue;
                    }
                    foreach (Formation enemy in team.FormationsIncludingEmpty)
                    {
                        if (enemy == null || enemy.CountOfUnits <= 0)
                        {
                            continue;
                        }
                        FormationQuerySystem q = enemy.QuerySystem;
                        if (q.IsCavalryFormation || q.IsRangedCavalryFormation || !RBMAI.Utilities.IsRangedForArrangement(enemy)
                            || !ThreatSignificance.IsSignificant(formation, enemy))
                        {
                            continue;
                        }
                        float distance = own.Distance(enemy.CachedAveragePosition);
                        if (distance > CounterFireRangeFactor * (ownRange + enemy.Depth * 0.5f))
                        {
                            continue;
                        }
                        float score = (enemy == _counterFire) ? distance * 0.8f : distance;
                        if (score < bestScore)
                        {
                            best = enemy;
                            bestScore = score;
                            bestDistance = distance;
                        }
                    }
                }
            }
            if (best != _counterFire)
            {
                string what = (best == null) ? "counter end" : (_counterFire == null) ? "counter take" : "counter change";
                string why = (best == null)
                    ? ("hits=" + hits.ToString("0.00") + (_counterFire != null && _counterFire.CountOfUnits <= 0 ? " targetGone" : ""))
                    : ("f" + (int)best.FormationIndex + ":" + best.RepresentativeClass + " d=" + bestDistance.ToString("0") + " hits=" + hits.ToString("0.00"));
                TerrainSpots.EnqueueBehavior(formation.Team, formation, now, "ArcherSkirmish", what, "counter", why);
                _counterFire = best;
                if (best != null)
                {
                    firstTime = true;
                }
            }
            return best;
        }

        public RBMBehaviorArcherSkirmish(Formation formation)
            : base(formation)
        {
            base.BehaviorCoherence = 0.5f;
            CalculateCurrentOrder();
        }

        protected override void CalculateCurrentOrder()
        {
            WorldPosition medianPosition = base.Formation.CachedMedianPosition;
            bool flag = false;
            Vec2 enemyFormationVector;


            if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
            {
                TerrainSpots.Release(this, "charge");
                ReleaseCover("charge");
                ReleaseSupport("charge");
                _onSpot = false;
                _ownTarget = WorldPosition.Invalid;
                _clamped = false;
                base.CurrentOrder = MovementOrder.MovementOrderCharge;
                return;
            }
            else
            {
                if (_initiative)
                {
                    // a TeamMain that is this formation is no reason to stop (troop types make that impossible, kept as a guard against a flip each tick)
                    Formation teamMain = FormationCohesion.TeamMain(base.Formation.Team);
                    if (teamMain == null || teamMain == base.Formation)
                    {
                        ChargeInitiative(RBMAI.Utilities.FindSignificantEnemy(base.Formation, includeInfantry: true, false, false, false, false, unitCountMatters: true)
                            ?? base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                        return;
                    }
                    _initiative = false;
                    firstTime = true;
                    ArmyComposition.Note(base.Formation.Team, base.Formation, ArmyComposition.KindRole, "initiative end", "mainInfantry " + TerrainCavalry.FormationName(teamMain), "ArcherSkirmish");
                }

                // user rule: defender archers approached by the enemy seek cover (ArcherCover); the states below resume afterwards
                if (CoverOrder())
                {
                    return;
                }

                Formation significantEnemy = null;

                if (base.Formation != null && base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    significantEnemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, includeInfantry: true, false, false, false, false, unitCountMatters: true);
                }

                if (significantEnemy == null)
                {
                    significantEnemy = base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation;
                }

                // counter-fire: while hit, the nearest significant enemy foot ranged formation within 1.3 x range is the target, held 10 s after the last hit
                Formation counterFire = CounterFireTarget();

                // no melee-infantry main, in reach but not firing long after SupportFire found nothing: take the initiative
                string idle = InitiativeIdleReason(significantEnemy) ?? IneffectiveFireReason(significantEnemy);
                if (idle != null)
                {
                    TakeInitiative(significantEnemy, idle);
                    return;
                }

                // SupportFire (user rule): once the frontlines connect, one place behind / beside the engaged main; counter-fire then only turns the facing
                if (SupportOrder(counterFire))
                {
                    return;
                }
                if (counterFire != null)
                {
                    significantEnemy = counterFire;
                }

                // position anchor; never itself
                Formation significantAlly = MeleeAnchor();

                enemyFormationVector = significantEnemy.SmoothedAverageUnitPosition - base.Formation.SmoothedAverageUnitPosition;

                float distanceToEnemyFormation = enemyFormationVector.Normalize();

                bool isFormationShooting = Utilities.IsFormationShooting(base.Formation);

                float effectiveShootingRange = (Formation.Depth / 2f) + (Formation.QuerySystem.MaximumMissileRange / 2f) + (significantEnemy.Depth / 2f);

                // special condition if split archers tactics is applied
                TeamAIComponent teamAi = base.Formation?.Team?.TeamAI;
                if (teamAi != null)
                {
                    TacticComponent currentTactic = TacticSelection.CurrentTacticRef(teamAi);
                    if (currentTactic is RBMTacticAttackSplitArchers || currentTactic is RBMTacticDefendSplitArchers)
                    {
                        if (significantEnemy != null && base.Formation?.Team?.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).Count((Formation f) => f.QuerySystem.IsRangedFormation) > 1)
                        {
                            effectiveShootingRange += significantEnemy.Width / 3.5f;
                        }
                    }
                }

                // an enemy of melee cavalry: behind the main melee infantry instead of the approach / pull-back place (kCav, either/or)
                Vec2 behindMain = _cavPlace.Place(this, significantAlly, RBMAI.Utilities.GetFormationCenter(significantEnemy), effectiveShootingRange);
                if (behindMain.IsValid != _placeWasBehind)
                {
                    // the choice switched: the place is rebuilt now, not at the next reposition
                    _placeWasBehind = behindMain.IsValid;
                    firstTime = true;
                }

                // stand vs mounted (user rule): a mounted target never starts a pull-back or a close charge; one under way stops
                bool enemyMounted = significantEnemy.QuerySystem.IsCavalryFormation || significantEnemy.QuerySystem.IsRangedCavalryFormation;

                // no melee infantry left: the initiative once dry (ArmyComposition.IsDry); charge when enemy foot is too close
                if (significantAlly == null || !FormationCohesion.IsMeleeInfantry(significantAlly))
                {
                    if (ArmyComposition.IsDry(base.Formation, out float dryAmmo))
                    {
                        string ammoText = "dry ammo=" + ArmyComposition.F2(dryAmmo) + "<" + ArmyComposition.F2(ArmyComposition.DryAmmoShare);
                        string blocked = InitiativeBlocked();
                        if (blocked == null)
                        {
                            TakeInitiative(significantEnemy, ammoText);
                        }
                        else
                        {
                            // archers out of ammunition still charge (user rule), without the team's initiative
                            if (!_charging && blocked != "notActive")
                            {
                                ArmyComposition.Note(base.Formation.Team, base.Formation, ArmyComposition.KindRole, "initiative suppressed", blocked + " " + ammoText + " (own charge)");
                            }
                            ChargeInitiative(significantEnemy);
                        }
                        return;
                    }
                    if (distanceToEnemyFormation < effectiveShootingRange * 0.4f && !enemyMounted)
                    {
                        if (!_charging && base.Formation.Team != null)
                        {
                            TerrainSpots.EnqueueBehavior(base.Formation.Team, base.Formation, Mission.Current.CurrentTime, "ArcherSkirmish", "charge", "-",
                                "enemyClose noInfantry");
                        }
                        _charging = true;
                        TerrainSpots.Release(this, "charge");
                        ReleaseCover("charge");
                        ReleaseSupport("charge");
                        _onSpot = false;
                        _ownTarget = WorldPosition.Invalid;
                        _clamped = false;
                        base.CurrentOrder = MovementOrder.MovementOrderCharge;
                        return;
                    }
                }
                if (_charging)
                {
                    _charging = false;
                    firstTime = true;
                }

                float rollPullBackAngle = 0f;
                BehaviorState previousBehavior = _behaviorState;
                switch (_behaviorState)
                {
                    case BehaviorState.Shooting:
                        {
                            if (isFormationShooting)
                            {
                                if (distanceToEnemyFormation > effectiveShootingRange * 1.1f)
                                {
                                    _behaviorState = BehaviorState.Approaching;
                                    break;
                                }

                                if (!enemyMounted && base.Formation.QuerySystem.IsRangedFormation && distanceToEnemyFormation < effectiveShootingRange * 0.4f)
                                {
                                    Formation meleeFormation = MeleeAnchor();
                                    if (meleeFormation != null && meleeFormation.QuerySystem.IsInfantryFormation)
                                    {
                                        rollPullBackAngle = _lateralRoll;
                                        _behaviorState = BehaviorState.PullingBack;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                if (!enemyMounted && base.Formation.QuerySystem.IsRangedFormation && distanceToEnemyFormation < effectiveShootingRange * 0.4f)
                                {
                                    Formation meleeFormation = MeleeAnchor();
                                    if (meleeFormation != null && meleeFormation.QuerySystem.IsInfantryFormation &&meleeFormation.CachedMedianPosition.AsVec2.Distance(base.Formation.CachedMedianPosition.AsVec2) <= base.Formation.QuerySystem.MissileRangeAdjusted)
                                    {
                                        rollPullBackAngle = _lateralRoll;
                                        _behaviorState = BehaviorState.PullingBack;
                                        break;
                                    }
                                }
                                else if (SpotSettling())
                                {
                                    // walking to / just arrived on a spot: no not-shooting refresh (it would release the spot mid-walk)
                                }
                                else
                                {
                                    if (refreshPositionTimer == null)
                                    {
                                        refreshPositionTimer = new Timer(Mission.Current.CurrentTime, 15f);
                                        _behaviorState = BehaviorState.Approaching;
                                    }
                                    else
                                    {
                                        if (refreshPositionTimer.Check(Mission.Current.CurrentTime))
                                        {
                                            refreshPositionTimer = null;
                                        }
                                    }
                                }
                            }
                            break;
                        }
                    case BehaviorState.Approaching:
                        {
                            if (distanceToEnemyFormation < effectiveShootingRange * 0.4f)
                            {
                                rollPullBackAngle = _lateralRoll;
                                _behaviorState = enemyMounted ? BehaviorState.Shooting : BehaviorState.PullingBack;
                                flag = true;
                            }
                            else if (distanceToEnemyFormation < effectiveShootingRange * 0.9f)
                            {
                                _behaviorState = BehaviorState.Shooting;
                                flag = true;
                            }
                            else if (Utilities.IsFormationShooting(base.Formation, 0.2f) && distanceToEnemyFormation < effectiveShootingRange * 0.9f)
                            {
                                _behaviorState = BehaviorState.Shooting;
                                flag = true;
                            }
                            else if (distanceToEnemyFormation < effectiveShootingRange * 0.9f && !wasShootingBefore)
                            {
                                _behaviorState = BehaviorState.Shooting;
                                flag = true;
                                wasShootingBefore = true;
                            }
                            break;
                        }
                    case BehaviorState.PullingBack:
                        {
                            Formation meleeFormationPull = MeleeAnchor();
                            if (meleeFormationPull != null && meleeFormationPull.CachedMedianPosition.AsVec2.Distance(base.Formation.CachedMedianPosition.AsVec2) > base.Formation.QuerySystem.MissileRangeAdjusted)
                            {
                                _behaviorState = BehaviorState.Shooting;
                                flag = true;
                            }
                            if (meleeFormationPull == null || !meleeFormationPull.QuerySystem.IsInfantryFormation)
                            {
                                _behaviorState = BehaviorState.Shooting;
                                flag = true;
                            }
                            if (distanceToEnemyFormation > effectiveShootingRange * 0.9f)
                            {
                                _behaviorState = BehaviorState.Shooting;
                                flag = true;
                            }
                            if (isFormationShooting && distanceToEnemyFormation > effectiveShootingRange * 0.5f)
                            {
                                _behaviorState = BehaviorState.Shooting;
                                flag = true;
                            }
                            if (enemyMounted)
                            {
                                _behaviorState = BehaviorState.Shooting;
                                flag = true;
                            }
                            break;
                        }
                }
                bool isOnlyCavReamining = RBMAI.Utilities.CheckIfOnlyCavRemaining(base.Formation);
                if (isOnlyCavReamining)
                {
                    _behaviorState = BehaviorState.Shooting;
                }

                bool shouldReposition = false;
                if (_behaviorState == BehaviorState.PullingBack || _behaviorState == BehaviorState.Approaching)
                {
                    if (repositionTimer == null)
                    {
                        repositionTimer = new Timer(Mission.Current.CurrentTime, 5f);
                    }
                    else
                    {
                        if (repositionTimer.Check(Mission.Current.CurrentTime))
                        {
                            shouldReposition = true;
                            repositionTimer = null;
                        }
                    }
                }
                // no periodic reposition while the cohesion pull holds the archers at the limit's edge (it slides the order point)
                if (shouldReposition && _clamped && _cohesion.IsPulled)
                {
                    shouldReposition = false;
                }
                if (firstTime || previousBehavior != _behaviorState || shouldReposition)
                {
                    // lateral offset fixed per activation
                    rollPullBackAngle = _lateralRoll;
                    switch (_behaviorState)
                    {
                        case BehaviorState.Shooting:
                            // hold at its FRONT-centre (a Move point is the front-centre)
                            medianPosition = FrontHoldPosition(base.Formation);
                            break;

                        case BehaviorState.Approaching:
                            if (behindMain.IsValid)
                            {
                                medianPosition.SetVec2(behindMain);
                            }
                            else if (side == 0)
                            {
                                medianPosition.SetVec2(significantEnemy.CachedAveragePosition + significantEnemy.Direction.LeftVec().Normalized() * rollPullBackAngle * LateralOffsetMax);
                            }
                            else if (side == 1)
                            {
                                medianPosition.SetVec2(significantEnemy.CachedAveragePosition + significantEnemy.Direction.RightVec().Normalized() * rollPullBackAngle * LateralOffsetMax);
                            }
                            break;

                        case BehaviorState.PullingBack:
                            medianPosition = significantEnemy.CachedMedianPosition;
                            if (behindMain.IsValid)
                            {
                                medianPosition.SetVec2(behindMain);
                            }
                            else if (side == 0)
                            {
                                medianPosition.SetVec2(medianPosition.AsVec2 - (enemyFormationVector * (effectiveShootingRange - base.Formation.Depth * 0.5f)) + (significantEnemy.Direction.LeftVec().Normalized() * (rollPullBackAngle * LateralOffsetMax)));
                            }
                            else if (side == 1)
                            {
                                medianPosition.SetVec2(medianPosition.AsVec2 - (enemyFormationVector * (effectiveShootingRange - base.Formation.Depth * 0.5f)) + (significantEnemy.Direction.RightVec().Normalized() * (rollPullBackAngle * LateralOffsetMax)));
                            }
                            break;
                    }
                    if (!base.CurrentOrder.GetPosition(base.Formation).IsValid || _behaviorState != BehaviorState.Shooting || flag)
                    {
                        base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition);
                        _ownTarget = medianPosition;
                    }
                    if (!CurrentFacingOrder.GetDirection(base.Formation).IsValid || _behaviorState != BehaviorState.Shooting || flag)
                    {
                        WorldPosition medianTargetFormationPosition = base.Formation.QuerySystem.Team.MedianTargetFormationPosition;
                        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection((medianTargetFormationPosition.AsVec2 - base.Formation.CachedAveragePosition).Normalized());
                    }
                    firstTime = false;
                }

                // archer spot (TerrainSpots): only while holding / shooting, near the hold position, enemy in range
                bool onSpot = false;
                WorldPosition spotPosition = WorldPosition.Invalid;
                if (_behaviorState == BehaviorState.Shooting)
                {
                    onSpot = TerrainSpots.TryHoldSpot(this, "ArcherSkirmish", base.Formation.CachedAveragePosition, FormationAI.BehaviorSide.BehaviorSideNotSet,
                        significantAlly, TerrainSpots.SpotMinFire, significantEnemy.CachedAveragePosition,
                        effectiveShootingRange * TerrainSpots.SkirmishTakeRange, effectiveShootingRange * TerrainSpots.SkirmishKeepRange, out spotPosition);
                }
                else
                {
                    TerrainSpots.Release(this, (_behaviorState == BehaviorState.PullingBack) ? "pullingBack" : "approaching");
                }
                if (onSpot && (!_onSpot || !_spotOrdered.IsValid || _spotOrdered.DistanceSquared(spotPosition.AsVec2) > 1f))
                {
                    // taken, or the held spot moved (a re-plan rebased the claim)
                    base.CurrentOrder = MovementOrder.MovementOrderMove(spotPosition);
                    _spotOrdered = spotPosition.AsVec2;
                    WorldPosition medianTargetFormationPosition = base.Formation.QuerySystem.Team.MedianTargetFormationPosition;
                    CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection((medianTargetFormationPosition.AsVec2 - spotPosition.AsVec2).Normalized());
                    _ownTarget = WorldPosition.Invalid;
                    _clamped = false;
                }
                else if (!onSpot && _onSpot && _behaviorState == BehaviorState.Shooting)
                {
                    // left the spot while shooting: hold at its front-centre
                    medianPosition = FrontHoldPosition(base.Formation);
                    base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition);
                    _ownTarget = medianPosition;
                }
                if (!onSpot)
                {
                    _spotOrdered = Vec2.Invalid;
                    _spotArrivedAt = float.NaN;
                }
                else if (float.IsNaN(_spotArrivedAt) && _spotOrdered.IsValid && FrontCentre(base.Formation).DistanceSquared(_spotOrdered) <= SpotArriveDistance * SpotArriveDistance)
                {
                    _spotArrivedAt = Mission.Current.CurrentTime;
                }
                _onSpot = onSpot;

                // facing follows the target; an attacker still approaching out of range faces where the army advances
                Vec2 toTarget = significantEnemy.CachedAveragePosition - base.Formation.CachedAveragePosition;
                Vec2 approachFacing;
                if (_behaviorState == BehaviorState.Approaching && !onSpot
                    && RBMBehaviorArcherFlank.ApproachFacing(base.Formation, significantAlly, base.CurrentOrder.GetPosition(base.Formation), out approachFacing))
                {
                    toTarget = approachFacing;
                }
                if (toTarget.LengthSquared > 1e-4f)
                {
                    toTarget.Normalize();
                    Vec2 currentFacing = CurrentFacingOrder.GetDirection(base.Formation);
                    float facingOff = currentFacing.IsValid ? currentFacing.AngleBetween(toTarget) : MathF.PI;
                    if (facingOff > FacingRefresh || facingOff < -FacingRefresh)
                    {
                        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(toTarget);
                    }
                }
                if (!onSpot)
                {
                    ClampToCohesion(significantEnemy);
                }
            }
        }

        /// <summary>AI thread: no melee-infantry main, SupportFire exhausted (none / no place), a significant enemy within the shooting reach and MakingRangedAttackRatio under InitiativeNoFireRatio for InitiativeIdleSeconds: the reason, else null.</summary>
        private string InitiativeIdleReason(Formation enemy)
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            // candidates are calculated too (FindBestBehavior): only the active behavior of an AI formation of a real archer force times it
            if (mission == null || enemy == null || formation.Team == null || FormationCohesion.TeamMain(formation.Team) != null || !_glue.Support.Exhausted
                || !ThreatSignificance.IsSignificant(formation, enemy) || InitiativeBlocked() != null)
            {
                _idleSince = float.NaN;
                return null;
            }
            float now = mission.CurrentTime;
            float distance = formation.SmoothedAverageUnitPosition.Distance(enemy.SmoothedAverageUnitPosition);
            float reach = formation.Depth * 0.5f + formation.QuerySystem.MaximumMissileRange * 0.5f + enemy.Depth * 0.5f;
            float shooting = formation.QuerySystem.MakingRangedAttackRatio;
            if (distance > reach || shooting >= InitiativeNoFireRatio)
            {
                _idleSince = float.NaN;
                return null;
            }
            if (float.IsNaN(_idleSince))
            {
                _idleSince = now;
                return null;
            }
            if (now - _idleSince < InitiativeIdleSeconds)
            {
                return null;
            }
            return "idle shooting=" + shooting.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " for " + (now - _idleSince).ToString("0") + "s d="
                + distance.ToString("0") + "/" + reach.ToString("0") + "m enemy=" + TerrainCavalry.FormationName(enemy) + " supportExhausted";
        }

        /// <summary>AI thread, attacker only: the INEFFECTIVE FIRE reason once the enemy foot in reach lost under IneffectiveLossShare of its men over IneffectiveSeconds of our shooting, else null.</summary>
        private string IneffectiveFireReason(Formation enemy)
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (mission == null || enemy == null || formation.Team == null || formation.Team.Side != BattleSideEnum.Attacker
                || FormationCohesion.TeamMain(formation.Team) != null || InitiativeBlocked() != null || !ThreatSignificance.IsSignificant(formation, enemy)
                || ArmyComposition.IsMounted(enemy) || !ArmyComposition.NotWeaker(formation.Team))
            {
                _ineffectiveSince = float.NaN;
                return null;
            }
            float distance = formation.SmoothedAverageUnitPosition.Distance(enemy.SmoothedAverageUnitPosition);
            float reach = formation.Depth * 0.5f + formation.QuerySystem.MaximumMissileRange * 0.5f + enemy.Depth * 0.5f;
            if (distance > reach || formation.QuerySystem.MakingRangedAttackRatio < InitiativeNoFireRatio)
            {
                _ineffectiveSince = float.NaN;
                return null;
            }
            float now = mission.CurrentTime;
            if (float.IsNaN(_ineffectiveSince) || _ineffectiveEnemy != enemy)
            {
                _ineffectiveSince = now;
                _ineffectiveEnemy = enemy;
                _ineffectiveEnemyMen = enemy.CountOfUnits;
                return null;
            }
            if (now - _ineffectiveSince < IneffectiveSeconds)
            {
                return null;
            }
            int lost = _ineffectiveEnemyMen - enemy.CountOfUnits;
            if (lost >= IneffectiveLossShare * _ineffectiveEnemyMen)
            {
                // the fire works: a new window
                _ineffectiveSince = now;
                _ineffectiveEnemyMen = enemy.CountOfUnits;
                return null;
            }
            return "ineffectiveFire lost=" + lost.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + _ineffectiveEnemyMen.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " in " + (now - _ineffectiveSince).ToString("0") + "s d=" + distance.ToString("0") + "/" + reach.ToString("0") + "m enemy=" + TerrainCavalry.FormationName(enemy);
        }

        /// <summary>AI thread: latches the behavior and the team (the cavalry ProtectFlank charges too), logs once, then charges.</summary>
        private void TakeInitiative(Formation enemy, string why)
        {
            Formation formation = base.Formation;
            if (!_initiative && formation.Team != null && Mission.Current != null)
            {
                _initiative = true;
                bool first = InitiativeTeams.TryAdd(formation.Team, Mission.Current.CurrentTime);
                ArmyComposition.Note(formation.Team, formation, ArmyComposition.KindRole, "initiative",
                    why + " target=" + TerrainCavalry.FormationName(enemy) + (first ? " team=take" : " team=held") + " " + ArmyComposition.Describe(formation.Team), "ArcherSkirmish");
            }
            ChargeInitiative(enemy);
        }

        /// <summary>ChargeToTarget the enemy (ChargeGuard walks it in beyond 50 m); the spot, cover and support are released.</summary>
        private void ChargeInitiative(Formation enemy)
        {
            _charging = true;
            _idleSince = float.NaN;
            TerrainSpots.Release(this, "initiative");
            ReleaseCover("initiative");
            ReleaseSupport("initiative");
            _onSpot = false;
            _ownTarget = WorldPosition.Invalid;
            _clamped = false;
            base.CurrentOrder = (enemy != null && enemy.CountOfUnits > 0) ? MovementOrder.MovementOrderChargeToTarget(enemy) : MovementOrder.MovementOrderCharge;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        }

        /// <summary>AI thread, active behavior only (user rule: cohesion limit).</summary>
        private void ClampToCohesion(Formation significantEnemy)
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (formation == null || mission == null || !_ownTarget.IsValid || formation.AI == null || formation.AI.ActiveBehavior != this)
            {
                return;
            }
            Vec2 facing = (significantEnemy != null) ? significantEnemy.CachedAveragePosition - _ownTarget.AsVec2 : Vec2.Invalid;
            Vec2 point;
            string what;
            string why;
            WorldPosition target = _cohesion.Resolve(formation, _ownTarget, facing, FormationCohesion.Kind.Auto, null, out point, out what, out why);
            if (what != null)
            {
                TerrainSpots.EnqueueBehavior(formation.Team, formation, mission.CurrentTime, "ArcherSkirmish", what, "cohesion", why);
            }
            if (_cohesion.IsPulled)
            {
                base.CurrentOrder = MovementOrder.MovementOrderMove(target);
                _clamped = true;
            }
            else if (_clamped)
            {
                base.CurrentOrder = MovementOrder.MovementOrderMove(_ownTarget);
                _clamped = false;
            }
        }

        public override void TickOccasionally()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder( CurrentFacingOrder);
            // under cover / in support fire a fixed post (Line unless fire / ground / cavalry say otherwise)
            bool post = _glue.Covering || _glue.Supporting;
            RBMAI.Utilities.TryApplyRangedArrangement(base.Formation, post ? ArrangementOrder.ArrangementOrderEnum.Line : ArrangementOrder.ArrangementOrderEnum.Loose, post);
        }

        /// <summary>The support-fire state for the decision layer ("support behind"), else null. Any thread.</summary>
        internal string SupportLabel => _glue.Support.Label;

        /// <summary>AI thread: false = no support fire; the state's own order is issued again.</summary>
        private bool SupportOrder(Formation preferTarget)
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (formation == null || mission == null)
            {
                return false;
            }
            WorldPosition target;
            Vec2 facing;
            switch (_glue.SupportStep(this, formation, preferTarget, Vec2.Invalid, Vec2.Invalid, FormationAI.BehaviorSide.BehaviorSideNotSet, mission.CurrentTime,
                out target, out facing))
            {
                case CoverSupport.Step.Off:
                    return false;
                case CoverSupport.Step.Ended:
                    HoldAfterGlue(formation);
                    return false;
                case CoverSupport.Step.Started:
                    TerrainSpots.Release(this, "support");
                    _onSpot = false;
                    _spotOrdered = Vec2.Invalid;
                    _spotArrivedAt = float.NaN;
                    _ownTarget = WorldPosition.Invalid;
                    _clamped = false;
                    _cohesion.Reset();
                    break;
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(target);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(facing);
            return true;
        }

        /// <summary>Out of cover / support: hold where it stands unless the current state places the formation again now.</summary>
        private void HoldAfterGlue(Formation formation)
        {
            firstTime = true;
            WorldPosition hold = FrontHoldPosition(formation);
            base.CurrentOrder = MovementOrder.MovementOrderMove(hold);
            _ownTarget = hold;
        }

        /// <summary>Ends the support fire; AI thread or wherever native cancels the behavior.</summary>
        private void ReleaseSupport(string reason)
        {
            if (_glue.ReleaseSupport(base.Formation, reason))
            {
                firstTime = true;
            }
        }

        /// <summary>AI thread: false = no cover; the state's own order is issued again.</summary>
        private bool CoverOrder()
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (formation == null || mission == null)
            {
                return false;
            }
            WorldPosition target;
            Vec2 facing;
            switch (_glue.CoverStep(this, formation, false, mission.CurrentTime, out target, out facing))
            {
                case CoverSupport.Step.Off:
                    return false;
                case CoverSupport.Step.Ended:
                    HoldAfterGlue(formation);
                    return false;
                case CoverSupport.Step.Started:
                    TerrainSpots.Release(this, "cover");
                    _onSpot = false;
                    // covering archers are never pulled
                    _ownTarget = WorldPosition.Invalid;
                    _clamped = false;
                    break;
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(target);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(facing);
            return true;
        }

        /// <summary>Ends a cover; AI thread or wherever native cancels the behavior.</summary>
        private void ReleaseCover(string reason)
        {
            if (_glue.ReleaseCover(base.Formation, reason))
            {
                firstTime = true;
            }
        }

        /// <summary>AI thread: FRONT-centre on a copy of the cached median, moved once (no native call).</summary>
        private static WorldPosition FrontHoldPosition(Formation formation)
        {
            WorldPosition hold = formation.CachedMedianPosition;
            Vec2 centre = formation.CachedAveragePosition;
            Vec2 facing = formation.Direction;
            if (!hold.IsValid || !centre.IsValid)
            {
                return hold;
            }
            if (facing.IsValid && facing.LengthSquared > 0.01f)
            {
                centre += facing.Normalized() * (formation.Depth * 0.5f);
            }
            hold.SetVec2(centre);
            return hold;
        }

        protected override void OnBehaviorActivatedAux()
        {
            // new lateral offset per activation (golden-ratio steps from the formation index: deterministic)
            _activations++;
            float roll = ((int)base.Formation.FormationIndex + 1) * 0.618034f + _activations * 0.381966f;
            _lateralRoll = roll - (float)System.Math.Floor(roll);
            _spotOrdered = Vec2.Invalid;
            _spotArrivedAt = float.NaN;
            _counterFire = null;
            _counterFireHitAt = float.NaN;
            _charging = false;
            _idleSince = float.NaN;
            _behaviorState = BehaviorState.PullingBack;
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder( CurrentFacingOrder);
            // Line / Loose by the situation for a ranged formation (Utilities.ChooseRangedArrangement)
            if (!RBMAI.Utilities.TryApplyRangedArrangement(base.Formation, ArrangementOrder.ArrangementOrderEnum.Loose))
            {
                base.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
            }
            base.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            // ~4 ranks for foot archers (TrySetRangedWidth), else native Wide
            if (!RBMAI.Utilities.TrySetRangedWidth(base.Formation))
            {
                base.Formation.SetFormOrder(FormOrder.FormOrderWide);
            }
        }

        public override void OnBehaviorCanceled()
        {
            TerrainSpots.Release(this, "behaviorEnded");
            ReleaseCover("behaviorEnded");
            ReleaseSupport("behaviorEnded");
            _onSpot = false;
            _clamped = false;
            _cohesion.Reset();
            _spotArrivedAt = float.NaN;
            _counterFire = null;
            _charging = false;
            base.OnBehaviorCanceled();
        }

        protected override float GetAiWeight()
        {
            // the archer stand owns the defender's archer anchor (no pull-back, no cover steps)
            if (RBMBehaviorArcherStand.Owns(base.Formation))
            {
                return 0f;
            }
            FormationQuerySystem querySystem = base.Formation.QuerySystem;
            return MBMath.Lerp(0.1f, 1f, MBMath.ClampFloat(querySystem.RangedUnitRatio + querySystem.RangedCavalryUnitRatio, 0f, 0.5f) * 2f);
        }
    }
}