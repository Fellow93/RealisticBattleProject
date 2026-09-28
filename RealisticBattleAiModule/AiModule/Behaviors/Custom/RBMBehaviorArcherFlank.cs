using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    internal class RBMBehaviorArcherFlank : BehaviorComponent
    {
        //private Timer returnTimer = null;
        //private Timer feintTimer = null;
        //private Timer attackTimer = null;

        public FormationAI.BehaviorSide FlankSide = FormationAI.BehaviorSide.Middle;

        public RBMBehaviorArcherFlank(Formation formation)
            : base(formation)
        {
            _behaviorSide = formation.AI.Side;
            CalculateCurrentOrder();
            base.BehaviorCoherence = 0.5f;
            base.NavmeshlessTargetPositionPenalty = 1f;
        }

        protected override float GetAiWeight()
        {
            return 1000f;
        }

        protected override void OnBehaviorActivatedAux()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
            base.Formation.SetFacingOrder( FacingOrder.FacingOrderLookAtEnemy);
            base.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            base.Formation.SetFormOrder( FormOrder.FormOrderWide);
        }

        protected override void CalculateCurrentOrder()
        {
            WorldPosition position = base.Formation.CachedMedianPosition;
            Vec2 averagePosition = base.Formation.CachedAveragePosition;

            float flankRange = 10f;

            Formation allyFormation = RBMAI.Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);

            Vec2 averageAllyFormationPosition = base.Formation.QuerySystem.Team.AveragePosition;
            WorldPosition medianTargetFormationPosition = base.Formation.QuerySystem.Team.MedianTargetFormationPosition;
            Vec2 enemyDirection = (medianTargetFormationPosition.AsVec2 - averagePosition).Normalized();
            // With no infantry to flank, stand off 150m on OUR side of the enemy. enemyDirection points from us to
            // them, so this is minus: the old plus put the order 150m behind the enemy and the archers marched
            // straight through the enemy line to reach it.
            Vec2 noAllyStandOff = medianTargetFormationPosition.AsVec2 - enemyDirection * 150f;
            // Backing off can leave the map where pushing past the enemy rarely did; hold in place if it does, the
            // same fallback idea as the ally branches below.
            WorldPosition standOffProbe = position;
            standOffProbe.SetVec2(noAllyStandOff);
            if (!Mission.Current.IsPositionInsideBoundaries(noAllyStandOff) || standOffProbe.GetNavMesh() == UIntPtr.Zero)
            {
                noAllyStandOff = position.AsVec2;
            }

            if (_behaviorSide == FormationAI.BehaviorSide.Right || FlankSide == FormationAI.BehaviorSide.Right)
            {
                if (allyFormation != null)
                {
                    Vec2 calcPosition = allyFormation.CurrentPosition + allyFormation.Direction.RightVec().Normalized() * (allyFormation.Width * 0.5f + base.Formation.Width * 0.5f + flankRange);

                    position.SetVec2(calcPosition);
                    if (!Mission.Current.IsPositionInsideBoundaries(calcPosition) || position.GetNavMesh() == UIntPtr.Zero)
                    {
                        calcPosition = allyFormation.CurrentPosition;
                        calcPosition -= allyFormation.Direction * 5f;
                        position.SetVec2(calcPosition);
                    }
                }
                else
                {
                    position.SetVec2(noAllyStandOff);
                }
            }
            else if (_behaviorSide == FormationAI.BehaviorSide.Left || FlankSide == FormationAI.BehaviorSide.Left)
            {
                if (allyFormation != null)
                {
                    Vec2 calcPosition = allyFormation.CurrentPosition + allyFormation.Direction.LeftVec().Normalized() * (allyFormation.Width * 0.5f + base.Formation.Width * 0.5f + flankRange);
                    position.SetVec2(calcPosition);
                    if (!Mission.Current.IsPositionInsideBoundaries(calcPosition) || position.GetNavMesh() == UIntPtr.Zero)
                    {
                        calcPosition = allyFormation.CurrentPosition;
                        calcPosition -= allyFormation.Direction * 10f;
                        position.SetVec2(calcPosition);
                    }
                }
                else
                {
                    position.SetVec2(noAllyStandOff);
                }
            }
            else
            {
                if (allyFormation != null)
                {
                    position = allyFormation.CachedMedianPosition;
                }
                else
                {
                    position.SetVec2(noAllyStandOff);
                }
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(position);
            float angle = CurrentFacingOrder.GetDirection(Formation).AngleBetween(enemyDirection);
            if (angle > 0.2f || angle < -0.2f)
            {
                base.CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
            }
        }

        public override void TickOccasionally()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
        }

        public override TextObject GetBehaviorString()
        {
            string name = GetType().Name;
            return GameTexts.FindText("str_formation_ai_sergeant_instruction_behavior_text", name);
        }
    }
}