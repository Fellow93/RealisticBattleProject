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
        private int flankCooldownMax = 40;

        //public float customWidth = 110f;
        public Timer repositionTimer = null;

        public Timer refreshPositionTimer = null;
        public Timer flankinTimer = null;
        public int side = MBRandom.RandomInt(2);
        public int cooldown = 0;
        public bool nudgeFormation;

        public bool wasShootingBefore = false;

        private enum BehaviorState
        {
            Approaching,
            Shooting,
            PullingBack,
            Flanking
        }

        private BehaviorState _behaviorState = BehaviorState.PullingBack;

        private Timer _cantShootTimer;

        private bool firstTime = true;

        public RBMBehaviorArcherSkirmish(Formation formation)
            : base(formation)
        {
            base.BehaviorCoherence = 0.5f;
            _cantShootTimer = new Timer(0f, 0f);
            CalculateCurrentOrder();
        }

        protected override void CalculateCurrentOrder()
        {
            WorldPosition medianPosition = base.Formation.QuerySystem.MedianPosition;
            bool flag = false;
            Vec2 enemyFormationVector;

            //if mostly ranged left, charge
            if(base.Formation.Team.QuerySystem.RangedRatio >= 0.90f)
            {
                base.CurrentOrder = MovementOrder.MovementOrderCharge;
                return;
            }

            //if no enemy formation detected, charge
            if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
            {
                base.CurrentOrder = MovementOrder.MovementOrderCharge;
                return;
            }
            else
            {
                Formation significantEnemy = null;

                //find closest, significant enemy infantry formation
                if (base.Formation != null && base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    significantEnemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, includeInfantry: true, false, false, false, false, unitCountMatters: true);
                }

                //if no enemy infantry then find and significant enemy formation
                if (significantEnemy == null)
                {
                    significantEnemy = base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation;
                }

                Formation significantAlly = null;
                //find closest, significant enemy infantry formation
                significantAlly = RBMAI.Utilities.FindSignificantAlly(base.Formation, includeInfantry: true, false, false, false, false, unitCountMatters:true);

                enemyFormationVector = significantEnemy.SmoothedAverageUnitPosition - base.Formation.SmoothedAverageUnitPosition;

                float distanceToEnemyFormation = enemyFormationVector.Normalize();

                bool isFormationShooting = Utilities.IsFormationShooting(base.Formation);

                float effectiveShootingRange = (Formation.Depth / 2f) + (Formation.QuerySystem.MaximumMissileRange / 2f) + (significantEnemy.Depth / 2f);

                // special condition if split archers tactics is applied
                FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
                _currentTacticField.DeclaringType.GetField("_currentTactic");
                if (base.Formation?.Team?.TeamAI != null)
                {
                    if (_currentTacticField.GetValue(base.Formation?.Team?.TeamAI) != null && _currentTacticField.GetValue(base.Formation?.Team?.TeamAI).ToString().Contains("SplitArchers"))
                    {
                        if (significantEnemy != null && base.Formation?.Team?.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).Count((Formation f) => f.QuerySystem.IsRangedFormation) > 1)
                        {
                            effectiveShootingRange += significantEnemy.Width / 3.5f;
                        }
                    }
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

                                if (base.Formation.QuerySystem.IsRangedFormation && distanceToEnemyFormation < effectiveShootingRange * 0.4f)
                                {
                                    Formation meleeFormation = RBMAI.Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);
                                    if (meleeFormation != null && meleeFormation.QuerySystem.IsInfantryFormation)
                                    {
                                        rollPullBackAngle = MBRandom.RandomFloat;
                                        _behaviorState = BehaviorState.PullingBack;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                if (base.Formation.QuerySystem.IsRangedFormation && distanceToEnemyFormation < effectiveShootingRange * 0.4f)
                                {
                                    Formation meleeFormation = RBMAI.Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);
                                    if (meleeFormation != null && meleeFormation.QuerySystem.IsInfantryFormation && meleeFormation.QuerySystem.MedianPosition.AsVec2.Distance(base.Formation.QuerySystem.MedianPosition.AsVec2) <= base.Formation.QuerySystem.MissileRangeAdjusted)
                                    {
                                        rollPullBackAngle = MBRandom.RandomFloat;
                                        _behaviorState = BehaviorState.PullingBack;
                                        break;
                                    }
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
                                rollPullBackAngle = MBRandom.RandomFloat;
                                _behaviorState = BehaviorState.PullingBack;
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
                            Formation meleeFormationPull = RBMAI.Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);
                            if (meleeFormationPull != null && meleeFormationPull.QuerySystem.MedianPosition.AsVec2.Distance(base.Formation.QuerySystem.MedianPosition.AsVec2) > base.Formation.QuerySystem.MissileRangeAdjusted)
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
                if (firstTime || previousBehavior != _behaviorState || shouldReposition)
                {
                    switch (_behaviorState)
                    {
                        case BehaviorState.Shooting:
                            medianPosition.SetVec2(base.Formation.QuerySystem.AveragePosition);
                            break;

                        case BehaviorState.Approaching:
                            rollPullBackAngle = MBRandom.RandomFloat;

                            if (side == 0)
                            {
                                medianPosition.SetVec2(significantEnemy.QuerySystem.AveragePosition + significantEnemy.Direction.LeftVec().Normalized() * rollPullBackAngle * 70f);
                            }
                            else if (side == 1)
                            {
                                medianPosition.SetVec2(significantEnemy.QuerySystem.AveragePosition + significantEnemy.Direction.RightVec().Normalized() * rollPullBackAngle * 70f);
                            }
                            break;

                        case BehaviorState.PullingBack:
                            medianPosition = significantEnemy.QuerySystem.MedianPosition;
                            rollPullBackAngle = MBRandom.RandomFloat;
                            if (side == 0)
                            {
                                medianPosition.SetVec2(medianPosition.AsVec2 - (enemyFormationVector * (effectiveShootingRange - base.Formation.Depth * 0.5f)) + (significantEnemy.Direction.LeftVec().Normalized() * (rollPullBackAngle * 70f)));
                            }
                            else if (side == 1)
                            {
                                medianPosition.SetVec2(medianPosition.AsVec2 - (enemyFormationVector * (effectiveShootingRange - base.Formation.Depth * 0.5f)) + (significantEnemy.Direction.RightVec().Normalized() * (rollPullBackAngle * 70f)));
                            }
                            break;
                    }
                    if (!base.CurrentOrder.GetPosition(base.Formation).IsValid || _behaviorState != BehaviorState.Shooting || flag)
                    {
                        base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition);
                    }
                    if (!CurrentFacingOrder.GetDirection(base.Formation).IsValid || _behaviorState != BehaviorState.Shooting || flag)
                    {
                        Vec2 averageAllyFormationPosition = base.Formation.QuerySystem.Team.AveragePosition;
                        WorldPosition medianTargetFormationPosition = base.Formation.QuerySystem.Team.MedianTargetFormationPosition;
                        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection((medianTargetFormationPosition.AsVec2 - base.Formation.QuerySystem.AveragePosition).Normalized());
                    }
                    firstTime = false;
                }
            }
        }

        public override void TickOccasionally()
        {
            CalculateCurrentOrder();
            //if(base.Formation.Width > customWidth)
            //{
            //    base.Formation.FormOrder = FormOrder.FormOrderCustom(customWidth);
            //}
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.FacingOrder = CurrentFacingOrder;
        }

        protected override void OnBehaviorActivatedAux()
        {
            //_cantShootDistance = float.MaxValue;
            _behaviorState = BehaviorState.PullingBack;
            _cantShootTimer.Reset(Mission.Current.CurrentTime, MBMath.Lerp(5f, 10f, (MBMath.ClampFloat(base.Formation.CountOfUnits, 10f, 60f) - 10f) * 0.02f));
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.FacingOrder = CurrentFacingOrder;
            base.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
            base.Formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
            base.Formation.FormOrder = FormOrder.FormOrderWide;
        }

        protected override float GetAiWeight()
        {
            FormationQuerySystem querySystem = base.Formation.QuerySystem;
            return MBMath.Lerp(0.1f, 1f, MBMath.ClampFloat(querySystem.RangedUnitRatio + querySystem.RangedCavalryUnitRatio, 0f, 0.5f) * 2f);
        }
    }
}