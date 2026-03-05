using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

public class RBMBehaviorCavalryCharge : BehaviorComponent
{
    private enum ChargeState
    {
        Undetermined,
        Charging,
        ChargingPast,
        Reforming
    }

    private ChargeState _chargeState;

    private FormationQuerySystem _lastTarget;

    private Vec2 _initialChargeDirection;

    private float _desiredChargeStopDistance;

    private WorldPosition _lastReformDestination;

    private Timer _chargingPastTimer;

    private Timer _reformTimer;

    private Timer _chargeTimer;

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
        base.BehaviorCoherence = 0.5f;
        _desiredChargeStopDistance = 110f;
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
        FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);

        object tacticValue = base.Formation?.Team?.TeamAI != null ? _currentTacticField.GetValue(base.Formation.Team.TeamAI) : null;
        if (tacticValue?.ToString().Contains("Embolon") == true)
        {
            _desiredChargeStopDistance = 50f;
            base.BehaviorCoherence = 0.85f;
        }
        else
        {
            _desiredChargeStopDistance = 110f;
            base.BehaviorCoherence = 0.5f;
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
                        if ((!base.Formation.QuerySystem.IsCavalryFormation && !base.Formation.QuerySystem.IsRangedCavalryFormation) || base.Formation.CachedAveragePosition.Distance(base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.CachedMedianPosition.AsVec2) / base.Formation.QuerySystem.MovementSpeedMaximum <= 5f)
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
                            _initialChargeDirection = _lastTarget.Formation.CachedMedianPosition.AsVec2 - base.Formation.CachedAveragePosition;
                            //result = ChargeState.Undetermined;
                        }
                        else if (_initialChargeDirection.DotProduct(_lastTarget.Formation.CachedMedianPosition.AsVec2 - base.Formation.CachedAveragePosition) <= 0f)
                        {
                            if (_chargeTimer == null)
                            {
                                _chargeTimer = new Timer(Mission.Current.CurrentTime, 3f);
                            }
                            //result = ChargeState.ChargingPast;
                        }
                        if (base.Formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents < 5f)
                        {
                            Vec2 chargeDir = (_lastTarget.Formation.CachedMedianPosition.AsVec2 - base.Formation.CachedAveragePosition).Normalized();
                            WorldPosition newReformDest = _lastTarget.Formation.CachedMedianPosition;
                            newReformDest.SetVec2(_lastTarget.Formation.CachedMedianPosition.AsVec2 + chargeDir * (_desiredChargeStopDistance + _lastTarget.Formation.Depth));
                            _lastReformDestination = newReformDest;
                            result = ChargeState.ChargingPast;
                            _chargeTimer = null;
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
                        float distToTarget = base.Formation.CachedAveragePosition.Distance(_lastTarget.Formation.CachedMedianPosition.AsVec2);
                        if (distToTarget >= (_desiredChargeStopDistance + _lastTarget.Formation.Depth))
                        {
                            _lastReformDestination = base.Formation.CachedMedianPosition;
                            result = ChargeState.Reforming;
                        }
                        else if (_chargingPastTimer.Check(Mission.Current.CurrentTime))
                        {
                            // Timer fired but didn't reach destination — recalculate reform point from current positions
                            Vec2 awayDir = (base.Formation.CachedAveragePosition - _lastTarget.Formation.CachedMedianPosition.AsVec2).Normalized();
                            WorldPosition newReformDest = _lastTarget.Formation.CachedMedianPosition;
                            newReformDest.SetVec2(_lastTarget.Formation.CachedMedianPosition.AsVec2 + awayDir * (_desiredChargeStopDistance + _lastTarget.Formation.Depth));
                            _lastReformDestination = newReformDest;
                            result = ChargeState.Reforming;
                        }
                        break;
                    }
                case ChargeState.Reforming:
                    {
                        float distToEnemy = base.Formation.CachedAveragePosition.Distance(_lastTarget.Formation.CachedMedianPosition.AsVec2);
                        bool safeDistanceFromEnemy = distToEnemy >= 30f;
                        if (_reformTimer.Check(Mission.Current.CurrentTime) || (base.Formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents < 12f && safeDistanceFromEnemy) || base.Formation.QuerySystem.UnderRangedAttackRatio > 0.2f)
                        {
                            result = ChargeState.Charging;
                        }
                        break;
                    }
            }
        }
        return result;
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
        _initialChargeDirection = _lastTarget.Formation.CachedMedianPosition.AsVec2 - base.Formation.CachedAveragePosition;
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
                        if (_lastTarget != null && _lastTarget.Formation != null)
                        {
                            base.Formation.SetFormOrder(FormOrder.FormOrderCustom(_lastTarget.Formation.Width));
                        }
                        Vec2 vec4 = (_lastTarget.Formation.CachedMedianPosition.AsVec2 - base.Formation.CachedAveragePosition).Normalized();
                        WorldPosition medianPosition3 = _lastTarget.Formation.CachedMedianPosition;
                        Vec2 vec5 = medianPosition3.AsVec2 + vec4 * (_desiredChargeStopDistance + _lastTarget.Formation.Depth);
                        medianPosition3.SetVec2(vec5);
                        _lastReformDestination = medianPosition3;
                        //base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition3);
                        base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(_lastTarget.Formation);
                        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec4);
                        break;
                    }
                case ChargeState.ChargingPast:
                    {
                        _chargingPastTimer = new Timer(Mission.Current.CurrentTime, 19f);
                        //Vec2 vec2 = (base.Formation.CachedAveragePosition - _lastTarget.MedianPosition.AsVec2).Normalized();
                        //_lastReformDestination = _lastTarget.MedianPosition;
                        //Vec2 vec3 = _lastTarget.MedianPosition.AsVec2 + vec2 * (_desiredChargeStopDistance + _lastTarget.Formation.Depth);
                        //_lastReformDestination.SetVec2(vec3);
                        base.CurrentOrder = MovementOrder.MovementOrderMove(_lastReformDestination);
                        //CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(_initialChargeDirection);
                        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                        break;
                    }
                case ChargeState.Reforming:
                    _reformTimer = new Timer(Mission.Current.CurrentTime, 10f);
                    base.CurrentOrder = MovementOrder.MovementOrderMove(_lastReformDestination);
                    CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                    break;

            }
            newTarget = false;
        }
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
        float num = querySystem.Formation.CachedAveragePosition.Distance(querySystem.ClosestSignificantlyLargeEnemyFormation.Formation.CachedMedianPosition.AsVec2) / querySystem.MovementSpeedMaximum;
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
        WorldPosition medianPosition = querySystem.Formation.CachedMedianPosition;
        medianPosition.SetVec2(querySystem.Formation.CachedAveragePosition);
        float num6 = medianPosition.GetNavMeshZ() - querySystem.ClosestSignificantlyLargeEnemyFormation.Formation.CachedMedianPosition.GetNavMeshZ();
        float num7 = 1f;
        if (num <= 4f)
        {
            float value = num6 / (querySystem.Formation.CachedAveragePosition - querySystem.ClosestSignificantlyLargeEnemyFormation.Formation.CachedMedianPosition.AsVec2).Length;
            num7 = MBMath.Lerp(0.9f, 1.1f, (MBMath.ClampFloat(value, -0.58f, 0.58f) + 0.58f) / 1.16f);
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