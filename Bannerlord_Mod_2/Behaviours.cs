using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RealisticBattle
{
    class Behaviours
    {
		
		public class BehaviorSkirmishFlank : BehaviorComponent
		{

			public Formation attackingFormation;
			public Formation victimFormation;

			public BehaviorSkirmishFlank(Formation formation)
			{
				attackingFormation = formation;
				//attackingFormation = formation;
				//victimFormation = enemyFormation;
				//base.BehaviorCoherence = 0.5f;
				CalculateCurrentOrder();
			}

			protected override void CalculateCurrentOrder()
			{
				FieldInfo property = typeof(BehaviorComponent).GetField("formation", BindingFlags.NonPublic | BindingFlags.Instance);
				property.DeclaringType.GetField("formation");
				property.SetValue(this, attackingFormation, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null);

				PropertyInfo PreserveExpireTimeInfo = typeof(BehaviorComponent).GetProperty("PreserveExpireTime", BindingFlags.NonPublic | BindingFlags.Instance);
				PreserveExpireTimeInfo.DeclaringType.GetProperty("PreserveExpireTime");
				PreserveExpireTimeInfo.SetValue(this, 0f, BindingFlags.NonPublic, null, null, null);

				FieldInfo _navmeshlessTargetPenaltyTimeInfo = typeof(BehaviorComponent).GetField("_navmeshlessTargetPenaltyTime", BindingFlags.NonPublic | BindingFlags.Instance);
				_navmeshlessTargetPenaltyTimeInfo.DeclaringType.GetField("_navmeshlessTargetPenaltyTime");
				_navmeshlessTargetPenaltyTimeInfo.SetValue(this, new Timer(MBCommon.GetTime(MBCommon.TimeType.Mission), 20f), BindingFlags.NonPublic, null, null);

				WorldPosition position;
				if (attackingFormation != null && victimFormation != null && attackingFormation.QuerySystem.ClosestEnemyFormation != null)
				{
					Vec2 vec;
					if (attackingFormation.AI.Side == FormationAI.BehaviorSide.Left)
					{
						Vec2 v = (attackingFormation.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - victimFormation.QuerySystem.MedianPosition.AsVec2).Normalized();
						vec = victimFormation.CurrentPosition + v.LeftVec().Normalized() * (victimFormation.Width + attackingFormation.Width + 5f);
						vec -= v * (victimFormation.Depth + attackingFormation.Depth);
					}
					else
					{
						Vec2 v = (attackingFormation.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - victimFormation.QuerySystem.MedianPosition.AsVec2).Normalized();
						vec = victimFormation.CurrentPosition + v.RightVec().Normalized() * (victimFormation.Width + attackingFormation.Width + 5f);
						vec -= v * (victimFormation.Depth + attackingFormation.Depth);
					}

					WorldPosition medianPosition = victimFormation.QuerySystem.MedianPosition;
					medianPosition.SetVec2(vec);
					//position = (attackingFormation.AI.Side == FormationAI.BehaviorSide.Right) ? victimFormation.QuerySystem.Team.RightFlankEdgePosition : victimFormation.QuerySystem.Team.LeftFlankEdgePosition;
					//Vec2 direction = (position.AsVec2 - attackingFormation.QuerySystem.AveragePosition).Normalized();
					base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition);
				}
				//CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);
			}

			protected internal void TickOccasionally()
			{
				CalculateCurrentOrder();
				if (attackingFormation != null)
				{
					attackingFormation.MovementOrder = base.CurrentOrder;
					attackingFormation.FacingOrder = CurrentFacingOrder;
				}
			}


			protected override void OnBehaviorActivatedAux()
			{
				CalculateCurrentOrder();
				attackingFormation.MovementOrder = base.CurrentOrder;
				attackingFormation.FacingOrder = CurrentFacingOrder;
				attackingFormation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
				attackingFormation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
				attackingFormation.FormOrder = FormOrder.FormOrderDeep;
				attackingFormation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;
			}

			protected override float GetAiWeight()
			{
				FormationQuerySystem querySystem = attackingFormation.QuerySystem;
				if (querySystem.ClosestEnemyFormation == null || querySystem.ClosestEnemyFormation.ClosestEnemyFormation == querySystem)
				{
					return 0f;
				}
				Vec2 vec = (querySystem.ClosestEnemyFormation.MedianPosition.AsVec2 - querySystem.AveragePosition).Normalized();
				Vec2 v = (querySystem.ClosestEnemyFormation.ClosestEnemyFormation.MedianPosition.AsVec2 - querySystem.ClosestEnemyFormation.MedianPosition.AsVec2).Normalized();
				if (vec.DotProduct(v) > -0.5f)
				{
					return 0f;
				}
				return 1.2f;
			}
		}

	//	_rightFlankEdgePosition = new QueryData<WorldPosition>(delegate
	//{
	//	Vec2 v = (MedianTargetFormationPosition.AsVec2 - AveragePosition).RightVec();
	//	v.Normalize();
	//	WorldPosition medianTargetFormationPosition = MedianTargetFormationPosition;
	//	medianTargetFormationPosition.SetVec2(medianTargetFormationPosition.AsVec2 + v* 50f);
	//	return medianTargetFormationPosition;
	//}, 5f);
    }
}
