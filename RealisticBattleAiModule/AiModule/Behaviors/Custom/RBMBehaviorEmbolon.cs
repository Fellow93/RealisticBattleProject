using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    internal class RBMBehaviorEmbolon : BehaviorComponent
    {
        private Formation _mainFormation;

        public FormationAI.BehaviorSide FlankSide = FormationAI.BehaviorSide.Middle;

        public RBMBehaviorEmbolon(Formation formation)
            : base(formation)
        {
            _behaviorSide = formation.AI.Side;
            _mainFormation = formation.Team.FormationsIncludingEmpty.FirstOrDefaultQ((Formation f) => f.CountOfUnits > 0 && f.AI.IsMainFormation);
            CalculateCurrentOrder();
        }

        protected override void CalculateCurrentOrder()
        {
            Vec2 direction;
            WorldPosition medianPosition;
            if (_mainFormation != null)
            {
                direction = _mainFormation.Direction;
                Vec2 vec = (base.Formation.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - _mainFormation.CachedMedianPosition.AsVec2).Normalized();
                medianPosition = _mainFormation.CachedMedianPosition;
                medianPosition.SetVec2(_mainFormation.CurrentPosition + vec * ((_mainFormation.Depth + base.Formation.Depth) * 0.5f + 20f));
            }
            else
            {
                direction = base.Formation.Direction;
                medianPosition = base.Formation.CachedMedianPosition;
                medianPosition.SetVec2(base.Formation.CachedAveragePosition);
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);
        }

        public override void OnValidBehaviorSideChanged()
        {
            base.OnValidBehaviorSideChanged();
            _mainFormation = base.Formation.Team.FormationsIncludingEmpty.FirstOrDefaultQ((Formation f) => f.CountOfUnits > 0 && f.AI.IsMainFormation);
        }

        public override void TickOccasionally()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder( CurrentFacingOrder);
            //if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && base.Formation.CachedAveragePosition.DistanceSquared(base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.MedianPosition.AsVec2) > 1600f && base.Formation.QuerySystem.UnderRangedAttackRatio > 0.2f - ((base.Formation.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Loose) ? 0.1f : 0f))
            //{
            //	base.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose;
            //}
            //else
            //{
            //	base.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine;
            //}
        }

        protected override void OnBehaviorActivatedAux()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder( CurrentFacingOrder);
            base.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderSkein);
            base.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            base.Formation.SetFormOrder( FormOrder.FormOrderDeep);
        }

        public override TextObject GetBehaviorString()
        {
            TextObject behaviorString = base.GetBehaviorString();
            TextObject variable = GameTexts.FindText("str_formation_ai_side_strings", base.Formation.AI.Side.ToString());
            behaviorString.SetTextVariable("SIDE_STRING", variable);
            if (_mainFormation != null)
            {
                behaviorString.SetTextVariable("AI_SIDE", GameTexts.FindText("str_formation_ai_side_strings", _mainFormation.AI.Side.ToString()));
                behaviorString.SetTextVariable("CLASS", GameTexts.FindText("str_formation_class_string", _mainFormation.RepresentativeClass.GetName()));
            }
            return behaviorString;
        }

        protected override float GetAiWeight()
        {
            if (_mainFormation == null || !_mainFormation.AI.IsMainFormation)
            {
                _mainFormation = base.Formation.Team.FormationsIncludingEmpty.FirstOrDefaultQ((Formation f) => f.CountOfUnits > 0 && f.AI.IsMainFormation);
            }
            if (_mainFormation == null || base.Formation.AI.IsMainFormation)
            {
                return 0f;
            }
            return 1.2f;
        }
    }
}