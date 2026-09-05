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
///   charge direction flips); ChargeThroughGraceSeconds later we go to ChargingPast.
/// ChargingPast: ride on to a reform point ChargeStopDistance + target depth beyond the enemy, on the side we
///   came out on. If the wedge is already tight (ReformDoneMaxDeviation) with RechargeMinRunUpDistance of
///   room, skip the reform and charge straight away; otherwise switch to Reforming on arrival or after
///   ChargingPastTimeoutSeconds.
/// Reforming: hold the reform point only until the wedge is tight with enough run-up, or
///   ReformTimeoutSeconds runs out, then charge again. Idle time here is kept as short as possible.
///
/// Two overrides apply while pulling clear or reforming:
///  - The reform point is resolved every tick (ResolveReformDestination). If it lies inside any formation's
///    footprint, or off navmesh / outside the map, a ring search around it finds the nearest clear, reachable
///    point that is not closer to the target; if none exists the reform is skipped and the wedge charges again.
///  - Any enemy formation within ReformThreatDistance, or within ReformApproachDistanceFactor times that and
///    moving toward us (IsEnemyClosingIn), ends the reform immediately and triggers a new charge.
///
/// Normal charge: 8 lancers vs a 40-man bandit line at 200 m. Time to contact drops under 5 s -> Charging.
/// They hit the line, the bandit centre passes behind them, 3 s later -> ChargingPast toward a point ~115 m
/// past the bandits. If they come out still in order, they turn and charge again as soon as they are 60 m
/// clear; if scattered they ride on to the reform point -> Reforming, tighten within 12 m -> Charging again
/// from the far side. Repeat until one side is gone.
///
/// Interrupted charge: same lancers, but a second bandit group is standing where the reform point lands.
/// ResolveReformDestination finds the point contested, ring-searches around it and moves the reform 40 m
/// aside; the lancers reform there instead. While they reform, the first bandit line turns and walks after
/// them. At 70 m and closing IsEnemyClosingIn fires -> Charging immediately, ignoring the reform timer.
/// If instead the whole area around the reform point were blocked (formations on all sides, map edge, no
/// navmesh), ResolveReformDestination returns Invalid and SkipReformAndCharge drops the reform entirely:
/// new target, ChargeToTarget, no standing still.
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

    private const float ChargeStopDistance = 110f;
    private const float ChargeStopDistanceEmbolon = 50f;
    private const float ChargeCoherence = 0.5f;
    private const float ChargeCoherenceEmbolon = 0.85f;
    private const float StartChargeTimeToContactSeconds = 5f;
    private const float ChargeThroughGraceSeconds = 3f;
    private const float ChargingPastTimeoutSeconds = 19f;
    private const float ReformTimeoutSeconds = 6f;
    private const float ReformDoneMaxDeviation = 12f;
    private const float RechargeMinRunUpDistance = 60f;
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

    private float _desiredChargeStopDistance;

    private WorldPosition _lastReformDestination;

    private Timer _chargingPastTimer;

    private Timer _reformTimer;

    private Timer _chargeTimer;

    // Resolved once: GetField is slow enough to matter on a per-formation occasional tick, and a null
    // result (field renamed by a game update) used to NRE on the line that consumed it.
    private static readonly FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);

    public bool ChargeArchers = true;
    public bool ChargeInfantry = true;
    public bool ChargeCavalry = false;
    public bool ChargeHorseArchers = false;

    public bool newTarget = false;

    public override float NavmeshlessTargetPositionPenalty => 1f;

    public RBMBehaviorCavalryCharge(Formation formation)
        : base(formation)
    {
        _lastTarget = null;
        base.CurrentOrder = MovementOrder.MovementOrderCharge;
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        _chargeState = ChargeState.Charging;
        base.BehaviorCoherence = ChargeCoherence;
        _desiredChargeStopDistance = ChargeStopDistance;
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

    private ChargeState CheckAndChangeState()
    {
        object tacticValue = (_currentTacticField != null && base.Formation?.Team?.TeamAI != null) ? _currentTacticField.GetValue(base.Formation.Team.TeamAI) : null;
        if (tacticValue?.ToString().Contains("Embolon") == true)
        {
            _desiredChargeStopDistance = ChargeStopDistanceEmbolon;
            base.BehaviorCoherence = ChargeCoherenceEmbolon;
        }
        else
        {
            _desiredChargeStopDistance = ChargeStopDistance;
            base.BehaviorCoherence = ChargeCoherence;
        }
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
                        if (_lastTarget == null || _lastTarget.Formation.CountOfUnits == 0)
                        {
                            Formation correctEnemy = null;
                            if (ChargeInfantry)
                                correctEnemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, true, false, false, false, false);
                            if (correctEnemy == null && ChargeArchers)
                                correctEnemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, false, true, false, false, false);
                            if (correctEnemy == null && ChargeHorseArchers)
                                correctEnemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, false, false, false, true, false);
                            if (correctEnemy == null && ChargeCavalry)
                                correctEnemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, false, false, true, false, false);
                            if (correctEnemy != null)
                            {
                                _lastTarget = correctEnemy.QuerySystem;
                            }
                            else
                            {
                                _lastTarget = base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                            }
                            newTarget = true;
                            _initialChargeDirection = RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation);
                            //result = ChargeState.Undetermined;
                        }
                        else if (_initialChargeDirection.DotProduct(RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation)) <= 0f)
                        {
                            if (_chargeTimer == null)
                            {
                                _chargeTimer = new Timer(Mission.Current.CurrentTime, ChargeThroughGraceSeconds);
                            }
                            //result = ChargeState.ChargingPast;
                        }
                        if (_chargeTimer != null && _chargeTimer.Check(Mission.Current.CurrentTime))
                        {
                            result = ChargeState.ChargingPast;
                            _chargeTimer = null;
                        }
                        break;
                    }
                case ChargeState.ChargingPast:
                    {
                        float distToTarget = RBMAI.Utilities.GetFormationDistance(base.Formation, _lastTarget.Formation);
                        if (IsEnemyClosingIn(excludeLastTarget: true) || IsReadyToRecharge(distToTarget))
                        {
                            result = ChargeState.Charging;
                        }
                        else if (distToTarget >= (_desiredChargeStopDistance + _lastTarget.Formation.Depth))
                        {
                            result = ChargeState.Reforming;
                        }
                        else if (_chargingPastTimer.Check(Mission.Current.CurrentTime))
                        {
                            Vec2 awayDir = (RBMAI.Utilities.GetFormationCenter(base.Formation) - RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation)).Normalized();
                            WorldPosition newReformDest = RBMAI.Utilities.GetFormationCenterWorldPosition(_lastTarget.Formation);
                            newReformDest.SetVec2(RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) + awayDir * (_desiredChargeStopDistance + _lastTarget.Formation.Depth));
                            _lastReformDestination = newReformDest;
                            result = ChargeState.Reforming;
                        }
                        break;
                    }
                case ChargeState.Reforming:
                    {
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

    private bool IsReadyToRecharge(float distToTarget)
    {
        return distToTarget >= RechargeMinRunUpDistance
            && base.Formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents < ReformDoneMaxDeviation;
    }

    public void CheckForNewChargeTarget()
    {
        Formation correctEnemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, true, true, false, false, false);
        if (correctEnemy != null)
        {
            _lastTarget = correctEnemy.QuerySystem;
        }
        else
        {
            _lastTarget = base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
        }
        newTarget = true;
        _initialChargeDirection = RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation);
    }

    protected override void CalculateCurrentOrder()
    {
        if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
        {
            base.CurrentOrder = MovementOrder.MovementOrderCharge;
            return;
        }

        ChargeState chargeState = CheckAndChangeState();

        bool isChargingCav = false;
        if (_lastTarget != null && (_lastTarget.IsCavalryFormation || _lastTarget.IsRangedCavalryFormation))
        {
            isChargingCav = true;
        }

        if (chargeState != _chargeState || newTarget || (isChargingCav && chargeState == ChargeState.Charging))
        {
            _chargeState = chargeState;

            switch (_chargeState)
            {
                case ChargeState.Undetermined:
                    {
                        base.CurrentOrder = MovementOrder.MovementOrderCharge;
                        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                        break;
                    }
                case ChargeState.Charging:
                    {
                        CheckForNewChargeTarget();
                        if (_lastTarget == null || _lastTarget.Formation == null)
                        {
                            base.CurrentOrder = MovementOrder.MovementOrderCharge;
                            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                            break;
                        }
                        base.Formation.SetFormOrder(FormOrder.FormOrderCustom(_lastTarget.Formation.Width));
                        Vec2 vec4 = (RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation)).Normalized();
                        base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(_lastTarget.Formation);
                        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec4);
                        break;
                    }
                case ChargeState.ChargingPast:
                    {
                        _chargingPastTimer = new Timer(Mission.Current.CurrentTime, ChargingPastTimeoutSeconds);
                        if (_lastTarget != null && _lastTarget.Formation != null)
                        {
                            Vec2 awayDir = (RBMAI.Utilities.GetFormationCenter(base.Formation) - RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation)).Normalized();
                            WorldPosition reformDest = RBMAI.Utilities.GetFormationCenterWorldPosition(_lastTarget.Formation);
                            reformDest.SetVec2(RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) + awayDir * (_desiredChargeStopDistance + _lastTarget.Formation.Depth));
                            _lastReformDestination = reformDest;
                        }
                        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                        break;
                    }
                case ChargeState.Reforming:
                    _reformTimer = new Timer(Mission.Current.CurrentTime, ReformTimeoutSeconds);
                    CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                    break;

            }
            newTarget = false;
        }

        if (_chargeState == ChargeState.ChargingPast || _chargeState == ChargeState.Reforming)
        {
            WorldPosition resolved = _lastReformDestination.IsValid ? ResolveReformDestination(_lastReformDestination) : WorldPosition.Invalid;
            if (resolved.IsValid)
            {
                _lastReformDestination = resolved;
                base.CurrentOrder = MovementOrder.MovementOrderMove(_lastReformDestination);
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            }
            else
            {
                SkipReformAndCharge();
            }
        }
    }

    private bool IsEnemyClosingIn(bool excludeLastTarget = false)
    {
        Mission mission = Mission.Current;
        if (mission == null || base.Formation == null)
        {
            return false;
        }
        Formation lastTargetFormation = excludeLastTarget && _lastTarget != null ? _lastTarget.Formation : null;
        Vec2 myCenter = RBMAI.Utilities.GetFormationCenter(base.Formation);
        float threatSq = ReformThreatDistance * ReformThreatDistance;
        float approachSq = threatSq * ReformApproachDistanceFactor * ReformApproachDistanceFactor;
        foreach (Team team in mission.Teams)
        {
            if (!team.IsEnemyOf(base.Formation.Team))
            {
                continue;
            }
            foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
            {
                if (enemy == null || enemy.CountOfUnits == 0 || enemy == lastTargetFormation)
                {
                    continue;
                }
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
        }
        return false;
    }

    private void SkipReformAndCharge()
    {
        _chargeState = ChargeState.Charging;
        _chargeTimer = null;
        _lastReformDestination = WorldPosition.Invalid;
        CheckForNewChargeTarget();
        if (_lastTarget != null && _lastTarget.Formation != null)
        {
            base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(_lastTarget.Formation);
        }
        else
        {
            base.CurrentOrder = MovementOrder.MovementOrderCharge;
        }
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        newTarget = false;
    }

    private WorldPosition ResolveReformDestination(WorldPosition dest)
    {
        Mission mission = Mission.Current;
        if (mission == null || base.Formation == null)
        {
            return dest;
        }
        Vec2 origin = dest.AsVec2;
        if (IsReformPointClear(mission, origin) && IsReformPointUsable(mission, dest))
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
                if (!IsReformPointClear(mission, candidate))
                {
                    continue;
                }
                WorldPosition candidatePos = dest;
                candidatePos.SetVec2(candidate);
                if (!IsReformPointUsable(mission, candidatePos))
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
        if (pushed.IsValid && IsReformPointClear(mission, pushed.AsVec2) && IsReformPointUsable(mission, pushed))
        {
            return pushed;
        }
        return WorldPosition.Invalid;
    }

    private static bool IsReformPointUsable(Mission mission, WorldPosition pos)
    {
        return pos.IsValid && pos.GetNavMesh() != System.UIntPtr.Zero && mission.IsPositionInsideBoundaries(pos.AsVec2);
    }

    private bool IsReformPointClear(Mission mission, Vec2 point)
    {
        float myHalfWidth = base.Formation.Width * 0.5f;
        foreach (Team team in mission.Teams)
        {
            foreach (Formation other in team.FormationsIncludingSpecialAndEmpty)
            {
                if (other == null || other == base.Formation || other.CountOfUnits == 0)
                {
                    continue;
                }
                float clearance = other.Width * 0.5f + other.Depth * 0.5f + myHalfWidth + ReformClearanceMargin;
                if (point.DistanceSquared(RBMAI.Utilities.GetFormationCenter(other)) < clearance * clearance)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private WorldPosition PushClearSingleDirection(Mission mission, WorldPosition dest)
    {
        Vec2 point = dest.AsVec2;
        Vec2 fallbackDir = Vec2.Zero;
        if (_lastTarget != null && _lastTarget.Formation != null)
        {
            fallbackDir = (point - RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation)).Normalized();
        }
        float myHalfWidth = base.Formation.Width * 0.5f;
        bool moved = false;
        for (int pass = 0; pass < ReformPushMaxPasses; pass++)
        {
            bool movedThisPass = false;
            foreach (Team team in mission.Teams)
            {
                foreach (Formation other in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (other == null || other == base.Formation || other.CountOfUnits == 0)
                    {
                        continue;
                    }
                    Vec2 otherCenter = RBMAI.Utilities.GetFormationCenter(other);
                    float clearance = other.Width * 0.5f + other.Depth * 0.5f + myHalfWidth + ReformClearanceMargin;
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
        float num = RBMAI.Utilities.GetFormationDistance(querySystem.Formation, querySystem.ClosestSignificantlyLargeEnemyFormation.Formation) / querySystem.MovementSpeedMaximum;
        float num3;
        if (!querySystem.IsCavalryFormation && !querySystem.IsRangedCavalryFormation)
        {
            float num2 = MBMath.ClampFloat(num, 4f, 10f);
            num3 = MBMath.Lerp(0.8f, 1f, 1f - (num2 - 4f) / 6f);
        }
        else if (num <= 4f)
        {
            float num4 = MBMath.ClampFloat(num, 0f, 4f);
            num3 = MBMath.Lerp(0.8f, 1.2f, num4 / 4f);
        }
        else
        {
            float num5 = MBMath.ClampFloat(num, 4f, 10f);
            num3 = MBMath.Lerp(0.8f, 1.2f, 1f - (num5 - 4f) / 6f);
        }
        float num7 = 1f;
        if (num <= 4f)
        {
            float length = (RBMAI.Utilities.GetFormationCenter(querySystem.Formation) - RBMAI.Utilities.GetFormationCenter(querySystem.ClosestSignificantlyLargeEnemyFormation.Formation)).Length;
            // Coincident formation centres divide by zero and GetNavMeshZ returns NaN off the navmesh; either
            // one poisons the slope term, and a NaN weight loses every behaviour comparison silently, so the
            // charge would just stop being picked rather than fail loudly. Fall back to the neutral 1f.
            if (length > float.Epsilon)
            {
                WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(querySystem.Formation);
                // Sample the enemy's height off the raw median: it already carries a valid Z, so it costs no navmesh
                // query, and for a slope estimate one soldier's ground height is as good as the centre's.
                float value = (medianPosition.GetNavMeshZ() - querySystem.ClosestSignificantlyLargeEnemyFormation.Formation.CachedMedianPosition.GetNavMeshZ()) / length;
                if (!float.IsNaN(value))
                {
                    num7 = MBMath.Lerp(0.9f, 1.1f, (MBMath.ClampFloat(value, -0.58f, 0.58f) + 0.58f) / 1.16f);
                }
            }
        }
        float num8 = 1f;
        if (num <= 4f && num >= 1.5f)
        {
            num8 = 1.2f;
        }
        float num9 = 1f;
        if (num <= 4f && querySystem.ClosestSignificantlyLargeEnemyFormation.ClosestSignificantlyLargeEnemyFormation != querySystem)
        {
            num9 = 1.2f;
        }
        float num10 = querySystem.GetClassWeightedFactor(1f, 1f, 1.5f, 1.5f) * querySystem.ClosestSignificantlyLargeEnemyFormation.GetClassWeightedFactor(1f, 1f, 0.5f, 0.5f);
        return (num3 * num7 * num8 * num9 * num10) * 2f;
    }
}