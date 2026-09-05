using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

/// <summary>
/// Cavalry charge as a four-state loop: Undetermined -> Charging -> ChargingPast -> Reforming -> Charging ...
///
/// Undetermined: no enemy in reach; plain charge order until the closest enemy is within
///   StartChargeTimeToContactSeconds at full speed.
/// Charging: pick a target (infantry, then archers, then per the Charge* flags), form up to its width and
///   ChargeToTarget. The charge is "through" once the target centre is behind us (dot product of the initial
///   charge direction flips); ChargeThroughGraceSeconds later we go to ChargingPast. If the wedge is in
///   contact with the target for ChargeContactTimeoutSeconds without ever passing through (bogged down in the
///   melee), it goes to ChargingPast anyway so it pulls out instead of dying in place.
/// ChargingPast: ride on to a reform point ChargeStopDistance + target depth beyond the enemy, on the side we
///   came out on. If the wedge is already cohesive (ReformCohesionMinRatio of riders within
///   ReformCohesionRadius of the centre) with RechargeMinRunUpDistance of room, skip the reform and charge
///   straight away; otherwise switch to Reforming on arrival or after ChargingPastTimeoutSeconds.
/// Reforming: hold the reform point only until the wedge is cohesive with enough run-up, or
///   ReformTimeoutSeconds runs out, then charge again. Idle time here is kept as short as possible.
///   Cohesion is counted over all riders, not the native "exclude far agents" deviation, which reads a
///   scattered wedge with a few bunched riders as tight.
///
/// Two overrides apply:
///  - While pulling clear or reforming, the reform point is resolved every tick (ResolveReformDestination).
///    If it lies inside any formation's footprint, or off navmesh / outside the map, a ring search around it
///    finds the nearest clear, reachable point that is not closer to the target; if none exists the reform is
///    skipped and the wedge charges again.
///  - While reforming only, any enemy formation within ReformThreatDistance, or within
///    ReformApproachDistanceFactor times that and moving toward us (IsEnemyClosingIn), ends the reform
///    immediately and triggers a new charge. It is not checked while pulling clear, where every enemy is
///    close by definition.
///
/// Normal charge: 8 lancers vs a 40-man bandit line at 200 m. Time to contact drops under 5 s -> Charging.
/// They hit the line, the bandit centre passes behind them, 3 s later -> ChargingPast toward a point ~145 m
/// past the bandits. If they come out still together, they turn and charge again as soon as they are 80 m
/// clear; if scattered they ride on to the reform point -> Reforming, gather within 20 m -> Charging again
/// from the far side. Repeat until one side is gone.
///
/// Interrupted charge: same lancers, but a second bandit group is standing where the reform point lands.
/// ResolveReformDestination finds the point contested, ring-searches around it and moves the reform 40 m
/// aside; the lancers reform there instead. While they reform, the first bandit line turns and walks after
/// them. At 70 m and closing IsEnemyClosingIn fires -> Charging immediately, ignoring the reform timer.
/// If instead the whole area around the reform point were blocked (formations on all sides, map edge, no
/// navmesh), ResolveReformDestination returns Invalid and TickReformMovement re-enters Charging, dropping the
/// reform entirely: new target, ChargeToTarget, no standing still.
/// </summary>
public class RBMBehaviorCavalryCharge : BehaviorComponent
{
    private enum ChargeState
    {
        Undetermined,
        Charging,
        ChargingPast,
        Reforming
    }

    private const float ChargeStopDistance = 140f;
    private const float ChargeStopDistanceEmbolon = 70f;
    private const float ChargeCoherence = 0.5f;
    private const float ChargeCoherenceEmbolon = 0.85f;
    private const float StartChargeTimeToContactSeconds = 5f;
    private const float ChargeThroughGraceSeconds = 3f;
    private const float ChargeContactTimeoutSeconds = 8f;
    private const float ChargingPastTimeoutSeconds = 19f;
    private const float ReformTimeoutSeconds = 8f;
    private const float ReformCohesionRadius = 20f;
    private const float ReformCohesionMinRatio = 0.8f;
    private const float RechargeMinRunUpDistance = 80f;
    private const float ReformAbortRangedAttackRatio = 0.2f;
    private const float ReformThreatDistance = 35f;
    private const float ReformApproachDistanceFactor = 2f;
    private const float ReformApproachMinSpeedSq = 1f;
    private const float ReformApproachMinDot = 0.7f;
    private const float ReformClearanceMargin = 20f;
    private static readonly float[] ReformSearchRadii = { 20f, 40f, 60f, 90f, 130f };
    private const int ReformSearchDirections = 12;
    private const int ReformPushMaxPasses = 3;

    private ChargeState _chargeState;

    private FormationQuerySystem _lastTarget;

    private Vec2 _initialChargeDirection;

    private bool _isEmbolonTactic;

    private bool _hasNewTarget;

    private WorldPosition _lastReformDestination;

    private Timer _chargingPastTimer;

    private Timer _reformTimer;

    private Timer _chargeTimer;

    private Timer _contactTimer;

    // Resolved once: GetField is slow enough to matter on a per-formation occasional tick, and a null
    // result (field renamed by a game update) used to NRE on the line that consumed it.
    private static readonly FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);

    public bool ChargeArchers = true;
    public bool ChargeInfantry = true;
    public bool ChargeCavalry = false;
    public bool ChargeHorseArchers = false;

    public override float NavmeshlessTargetPositionPenalty => 1f;

    private float ChargeStopDistanceForTactic => _isEmbolonTactic ? ChargeStopDistanceEmbolon : ChargeStopDistance;

    public RBMBehaviorCavalryCharge(Formation formation)
        : base(formation)
    {
        _lastTarget = null;
        base.CurrentOrder = MovementOrder.MovementOrderCharge;
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        _chargeState = ChargeState.Charging;
        base.BehaviorCoherence = ChargeCoherence;
    }

    private void RefreshTacticSettings()
    {
        object tactic = (_currentTacticField != null && base.Formation?.Team?.TeamAI != null) ? _currentTacticField.GetValue(base.Formation.Team.TeamAI) : null;
        _isEmbolonTactic = tactic?.ToString().Contains("Embolon") == true;
        base.BehaviorCoherence = _isEmbolonTactic ? ChargeCoherenceEmbolon : ChargeCoherence;
    }

    public override void TickOccasionally()
    {
        base.TickOccasionally();
        if (base.Formation.AI.ActiveBehavior == this)
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder( CurrentFacingOrder);
        }
    }

    private ChargeState DecideNextState()
    {
        RefreshTacticSettings();
        ChargeState result = _chargeState;
        if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
        {
            result = ChargeState.Undetermined;
        }
        else
        {
            switch (_chargeState)
            {
                case ChargeState.Undetermined:
                    {
                        if ((!base.Formation.QuerySystem.IsCavalryFormation && !base.Formation.QuerySystem.IsRangedCavalryFormation) || RBMAI.Utilities.GetFormationDistance(base.Formation, base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation) / base.Formation.QuerySystem.MovementSpeedMaximum <= StartChargeTimeToContactSeconds)
                        {
                            result = ChargeState.Charging;
                        }
                        break;
                    }
                case ChargeState.Charging:
                    {
                        if (IsTargetGone())
                        {
                            _hasNewTarget = true;
                            break;
                        }
                        Vec2 toTarget = RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation);
                        bool passedThrough = _initialChargeDirection.DotProduct(toTarget) <= 0f;
                        bool inContact = toTarget.Length <= ContactDistance(_lastTarget.Formation);
                        if (passedThrough && _chargeTimer == null)
                        {
                            _chargeTimer = new Timer(Mission.Current.CurrentTime, ChargeThroughGraceSeconds);
                        }
                        if (inContact && _contactTimer == null)
                        {
                            _contactTimer = new Timer(Mission.Current.CurrentTime, ChargeContactTimeoutSeconds);
                        }
                        bool chargeOver = _chargeTimer != null && _chargeTimer.Check(Mission.Current.CurrentTime);
                        bool stalledInMelee = _contactTimer != null && _contactTimer.Check(Mission.Current.CurrentTime);
                        if (chargeOver || stalledInMelee)
                        {
                            result = ChargeState.ChargingPast;
                        }
                        break;
                    }
                case ChargeState.ChargingPast:
                    {
                        if (IsTargetGone())
                        {
                            result = ChargeState.Charging;
                            break;
                        }
                        float distToTarget = RBMAI.Utilities.GetFormationDistance(base.Formation, _lastTarget.Formation);
                        if (IsReadyToRecharge(distToTarget))
                        {
                            result = ChargeState.Charging;
                        }
                        else if (distToTarget >= (ChargeStopDistanceForTactic + _lastTarget.Formation.Depth))
                        {
                            result = ChargeState.Reforming;
                        }
                        else if (_chargingPastTimer.Check(Mission.Current.CurrentTime))
                        {
                            _lastReformDestination = ComputeReformDestination();
                            result = ChargeState.Reforming;
                        }
                        break;
                    }
                case ChargeState.Reforming:
                    {
                        if (IsTargetGone())
                        {
                            result = ChargeState.Charging;
                            break;
                        }
                        float distToEnemy = RBMAI.Utilities.GetFormationDistance(base.Formation, _lastTarget.Formation);
                        bool underFire = base.Formation.QuerySystem.UnderRangedAttackRatio > ReformAbortRangedAttackRatio;
                        if (_reformTimer.Check(Mission.Current.CurrentTime) || IsReadyToRecharge(distToEnemy) || underFire || IsEnemyClosingIn())
                        {
                            result = ChargeState.Charging;
                        }
                        break;
                    }
            }
        }
        return result;
    }

    private bool IsTargetGone()
    {
        return _lastTarget?.Formation == null || _lastTarget.Formation.CountOfUnits == 0;
    }

    private float ContactDistance(Formation target)
    {
        return (target.Width + target.Depth + base.Formation.Depth) * 0.5f;
    }

    private bool IsReadyToRecharge(float distToTarget)
    {
        return distToTarget >= RechargeMinRunUpDistance && IsFormationCohesive();
    }

    private bool IsFormationCohesive()
    {
        Vec2 center = RBMAI.Utilities.GetFormationCenter(base.Formation);
        float radius = ReformCohesionRadius + MathF.Max(base.Formation.Width, base.Formation.Depth) * 0.5f;
        float radiusSq = radius * radius;
        int total = 0;
        int near = 0;
        base.Formation.ApplyActionOnEachUnit(agent =>
        {
            if (agent.IsDetachedFromFormation || agent.IsRunningAway)
            {
                return;
            }
            total++;
            if (agent.Position.AsVec2.DistanceSquared(center) <= radiusSq)
            {
                near++;
            }
        });
        return total > 0 && near >= total * ReformCohesionMinRatio;
    }

    private void AcquireTarget()
    {
        Formation enemy = null;
        if (ChargeInfantry)
            enemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, true, false, false, false, false);
        if (enemy == null && ChargeArchers)
            enemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, false, true, false, false, false);
        if (enemy == null && ChargeHorseArchers)
            enemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, false, false, false, true, false);
        if (enemy == null && ChargeCavalry)
            enemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, false, false, true, false, false);

        _lastTarget = enemy != null ? enemy.QuerySystem : base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
        _hasNewTarget = true;
        if (_lastTarget?.Formation != null)
        {
            _initialChargeDirection = RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation);
        }
    }

    private WorldPosition ComputeReformDestination()
    {
        if (_lastTarget?.Formation == null)
        {
            return WorldPosition.Invalid;
        }
        Vec2 enemyCenter = RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation);
        Vec2 awayDir = (RBMAI.Utilities.GetFormationCenter(base.Formation) - enemyCenter).Normalized();
        WorldPosition dest = RBMAI.Utilities.GetFormationCenterWorldPosition(_lastTarget.Formation);
        dest.SetVec2(enemyCenter + awayDir * (ChargeStopDistanceForTactic + _lastTarget.Formation.Depth));
        return dest;
    }

    protected override void CalculateCurrentOrder()
    {
        if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
        {
            base.CurrentOrder = MovementOrder.MovementOrderCharge;
            return;
        }

        ChargeState nextState = DecideNextState();
        if (nextState != _chargeState || _hasNewTarget)
        {
            EnterState(nextState);
        }

        if (_chargeState == ChargeState.ChargingPast || _chargeState == ChargeState.Reforming)
        {
            TickReformMovement();
        }
    }

    private void EnterState(ChargeState state)
    {
        _chargeState = state;
        switch (state)
        {
            case ChargeState.Undetermined:
                EnterUndetermined();
                break;
            case ChargeState.Charging:
                EnterCharging();
                break;
            case ChargeState.ChargingPast:
                EnterChargingPast();
                break;
            case ChargeState.Reforming:
                EnterReforming();
                break;
        }
        _hasNewTarget = false;
    }

    private void EnterUndetermined()
    {
        base.CurrentOrder = MovementOrder.MovementOrderCharge;
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    private void EnterCharging()
    {
        _chargeTimer = null;
        _contactTimer = null;
        _lastReformDestination = WorldPosition.Invalid;
        AcquireTarget();
        if (_lastTarget?.Formation == null)
        {
            base.CurrentOrder = MovementOrder.MovementOrderCharge;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            return;
        }
        base.Formation.SetFormOrder(FormOrder.FormOrderCustom(_lastTarget.Formation.Width));
        Vec2 chargeDirection = (RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation)).Normalized();
        base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(_lastTarget.Formation);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(chargeDirection);
    }

    private void EnterChargingPast()
    {
        _chargingPastTimer = new Timer(Mission.Current.CurrentTime, ChargingPastTimeoutSeconds);
        _lastReformDestination = ComputeReformDestination();
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    private void EnterReforming()
    {
        _reformTimer = new Timer(Mission.Current.CurrentTime, ReformTimeoutSeconds);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    private void TickReformMovement()
    {
        WorldPosition resolved = _lastReformDestination.IsValid ? ResolveReformDestination(_lastReformDestination) : WorldPosition.Invalid;
        if (!resolved.IsValid)
        {
            EnterState(ChargeState.Charging);
            return;
        }
        _lastReformDestination = resolved;
        base.CurrentOrder = MovementOrder.MovementOrderMove(_lastReformDestination);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    private bool IsEnemyClosingIn()
    {
        Mission mission = Mission.Current;
        if (mission == null || base.Formation == null)
        {
            return false;
        }
        Vec2 myCenter = RBMAI.Utilities.GetFormationCenter(base.Formation);
        float threatSq = ReformThreatDistance * ReformThreatDistance;
        float approachSq = threatSq * ReformApproachDistanceFactor * ReformApproachDistanceFactor;
        foreach (Formation enemy in OtherFormations(mission, enemiesOnly: true))
        {
            Vec2 enemyCenter = RBMAI.Utilities.GetFormationCenter(enemy);
            float distSq = myCenter.DistanceSquared(enemyCenter);
            if (distSq <= threatSq)
            {
                return true;
            }
            Vec2 toMe = myCenter - enemyCenter;
            Vec2 vel = enemy.CachedCurrentVelocity;
            if (distSq <= approachSq && vel.LengthSquared > ReformApproachMinSpeedSq && Vec2.DotProduct(vel.Normalized(), toMe.Normalized()) > ReformApproachMinDot)
            {
                return true;
            }
        }
        return false;
    }

    private WorldPosition ResolveReformDestination(WorldPosition dest)
    {
        Mission mission = Mission.Current;
        if (mission == null || base.Formation == null)
        {
            return dest;
        }
        Vec2 origin = dest.AsVec2;
        if (IsReformPointAcceptable(mission, dest))
        {
            return dest;
        }

        Vec2 enemyCenter = Vec2.Zero;
        bool hasEnemy = _lastTarget != null && _lastTarget.Formation != null;
        float minEnemyDistSq = 0f;
        if (hasEnemy)
        {
            enemyCenter = RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation);
            minEnemyDistSq = origin.DistanceSquared(enemyCenter);
        }

        WorldPosition best = WorldPosition.Invalid;
        float bestDistSq = float.MaxValue;
        float step = MathF.PI * 2f / ReformSearchDirections;
        foreach (float radius in ReformSearchRadii)
        {
            for (int i = 0; i < ReformSearchDirections; i++)
            {
                float angle = i * step;
                Vec2 candidate = origin + new Vec2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                if (hasEnemy && candidate.DistanceSquared(enemyCenter) < minEnemyDistSq)
                {
                    continue;
                }
                WorldPosition candidatePos = dest;
                candidatePos.SetVec2(candidate);
                if (!IsReformPointAcceptable(mission, candidatePos))
                {
                    continue;
                }
                float distSq = candidate.DistanceSquared(origin);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = candidatePos;
                }
            }
            if (best.IsValid)
            {
                break;
            }
        }
        if (best.IsValid)
        {
            return best;
        }
        WorldPosition pushed = PushClearSingleDirection(mission, dest);
        return IsReformPointAcceptable(mission, pushed) ? pushed : WorldPosition.Invalid;
    }

    private bool IsReformPointAcceptable(Mission mission, WorldPosition pos)
    {
        if (!pos.IsValid || pos.GetNavMesh() == System.UIntPtr.Zero || !mission.IsPositionInsideBoundaries(pos.AsVec2))
        {
            return false;
        }
        Vec2 point = pos.AsVec2;
        foreach (Formation other in OtherFormations(mission))
        {
            float clearance = ClearanceRadius(other);
            if (point.DistanceSquared(RBMAI.Utilities.GetFormationCenter(other)) < clearance * clearance)
            {
                return false;
            }
        }
        return true;
    }

    private IEnumerable<Formation> OtherFormations(Mission mission, bool enemiesOnly = false)
    {
        foreach (Team team in mission.Teams)
        {
            if (enemiesOnly && !team.IsEnemyOf(base.Formation.Team))
            {
                continue;
            }
            foreach (Formation other in team.FormationsIncludingSpecialAndEmpty)
            {
                if (other != null && other != base.Formation && other.CountOfUnits > 0)
                {
                    yield return other;
                }
            }
        }
    }

    private float ClearanceRadius(Formation other)
    {
        return other.Width * 0.5f + other.Depth * 0.5f + base.Formation.Width * 0.5f + ReformClearanceMargin;
    }

    private WorldPosition PushClearSingleDirection(Mission mission, WorldPosition dest)
    {
        Vec2 point = dest.AsVec2;
        Vec2 fallbackDir = Vec2.Zero;
        if (_lastTarget != null && _lastTarget.Formation != null)
        {
            fallbackDir = (point - RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation)).Normalized();
        }
        bool moved = false;
        for (int pass = 0; pass < ReformPushMaxPasses; pass++)
        {
            bool movedThisPass = false;
            foreach (Formation other in OtherFormations(mission))
            {
                Vec2 otherCenter = RBMAI.Utilities.GetFormationCenter(other);
                float clearance = ClearanceRadius(other);
                Vec2 offset = point - otherCenter;
                float dist = offset.Length;
                if (dist >= clearance)
                {
                    continue;
                }
                Vec2 pushDir = dist > 1f ? offset / dist : (fallbackDir.LengthSquared > 0.01f ? fallbackDir : Vec2.Forward);
                point = otherCenter + pushDir * clearance;
                movedThisPass = true;
                moved = true;
            }
            if (!movedThisPass)
            {
                break;
            }
        }
        if (!moved)
        {
            return dest;
        }
        WorldPosition result = dest;
        result.SetVec2(point);
        return result;
    }

    protected override void OnBehaviorActivatedAux()
    {
        CalculateCurrentOrder();
        base.Formation.SetMovementOrder(base.CurrentOrder);
        base.Formation.SetFacingOrder( CurrentFacingOrder);
        base.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
        base.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
        base.Formation.SetFormOrder( FormOrder.FormOrderWide);
    }

    public override TextObject GetBehaviorString()
    {
        TextObject behaviorString = base.GetBehaviorString();
        if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
        {
            behaviorString.SetTextVariable("AI_SIDE", GameTexts.FindText("str_formation_ai_side_strings", base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.AI.Side.ToString()));
            behaviorString.SetTextVariable("CLASS", GameTexts.FindText("str_formation_class_string", base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.RepresentativeClass.GetName()));
        }
        return behaviorString;
    }

    protected override float GetAiWeight()
    {
        FormationQuerySystem querySystem = base.Formation.QuerySystem;
        if (querySystem.ClosestSignificantlyLargeEnemyFormation == null)
        {
            return 0f;
        }
        FormationQuerySystem enemy = querySystem.ClosestSignificantlyLargeEnemyFormation;
        float timeToContact = RBMAI.Utilities.GetFormationDistance(querySystem.Formation, enemy.Formation) / querySystem.MovementSpeedMaximum;
        float proximityFactor;
        if (!querySystem.IsCavalryFormation && !querySystem.IsRangedCavalryFormation)
        {
            float clamped = MBMath.ClampFloat(timeToContact, 4f, 10f);
            proximityFactor = MBMath.Lerp(0.8f, 1f, 1f - (clamped - 4f) / 6f);
        }
        else if (timeToContact <= 4f)
        {
            float clamped = MBMath.ClampFloat(timeToContact, 0f, 4f);
            proximityFactor = MBMath.Lerp(0.8f, 1.2f, clamped / 4f);
        }
        else
        {
            float clamped = MBMath.ClampFloat(timeToContact, 4f, 10f);
            proximityFactor = MBMath.Lerp(0.8f, 1.2f, 1f - (clamped - 4f) / 6f);
        }
        float slopeFactor = 1f;
        if (timeToContact <= 4f)
        {
            float length = (RBMAI.Utilities.GetFormationCenter(querySystem.Formation) - RBMAI.Utilities.GetFormationCenter(enemy.Formation)).Length;
            // Coincident formation centres divide by zero and GetNavMeshZ returns NaN off the navmesh; either
            // one poisons the slope term, and a NaN weight loses every behaviour comparison silently, so the
            // charge would just stop being picked rather than fail loudly. Fall back to the neutral 1f.
            if (length > float.Epsilon)
            {
                WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(querySystem.Formation);
                // Sample the enemy's height off the raw median: it already carries a valid Z, so it costs no navmesh
                // query, and for a slope estimate one soldier's ground height is as good as the centre's.
                float slope = (medianPosition.GetNavMeshZ() - enemy.Formation.CachedMedianPosition.GetNavMeshZ()) / length;
                if (!float.IsNaN(slope))
                {
                    slopeFactor = MBMath.Lerp(0.9f, 1.1f, (MBMath.ClampFloat(slope, -0.58f, 0.58f) + 0.58f) / 1.16f);
                }
            }
        }
        float imminentContactFactor = (timeToContact <= 4f && timeToContact >= 1.5f) ? 1.2f : 1f;
        float unengagedEnemyFactor = (timeToContact <= 4f && enemy.ClosestSignificantlyLargeEnemyFormation != querySystem) ? 1.2f : 1f;
        float classFactor = querySystem.GetClassWeightedFactor(1f, 1f, 1.5f, 1.5f) * enemy.GetClassWeightedFactor(1f, 1f, 0.5f, 0.5f);
        return (proximityFactor * slopeFactor * imminentContactFactor * unengagedEnemyFactor * classFactor) * 2f;
    }
}