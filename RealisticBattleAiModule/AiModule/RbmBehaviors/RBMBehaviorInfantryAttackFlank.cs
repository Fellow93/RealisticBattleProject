using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    class RBMBehaviorInfantryAttackFlank : BehaviorComponent
	{
		private FlankMode flankMode;

		//private Timer returnTimer = null;
		//private Timer feintTimer = null;
		//private Timer attackTimer = null;

		public FormationAI.BehaviorSide FlankSide = FormationAI.BehaviorSide.Middle;

		private float mobilityModifier = 1.25f;
		private enum FlankMode
		{
			Flank,
			Feint,
			Attack
		}

		private bool _isEnemyReachable = true;

		public RBMBehaviorInfantryAttackFlank(Formation formation)
			: base(formation)
		{
			flankMode = FlankMode.Flank;
			behaviorSide = formation.AI.Side;
			CalculateCurrentOrder();
			base.BehaviorCoherence = 0.5f;
		}

		protected override float GetAiWeight()
		{
			return 2f;
		}

		protected override void OnBehaviorActivatedAux()
		{
			CalculateCurrentOrder();
			base.Formation.SetMovementOrder(base.CurrentOrder);
			base.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
			base.Formation.FacingOrder = FacingOrder.FacingOrderLookAtEnemy;
			base.Formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
			base.Formation.FormOrder = FormOrder.FormOrderDeep;
			base.Formation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;
		}

		protected override void CalculateCurrentOrder()
		{
			WorldPosition position = base.Formation.QuerySystem.MedianPosition;
			_isEnemyReachable = base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && (!(base.Formation.Team.TeamAI is TeamAISiegeComponent) || !TeamAISiegeComponent.IsFormationInsideCastle(base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false));
			Vec2 averagePosition = base.Formation.QuerySystem.AveragePosition;

			if (!_isEnemyReachable)
			{
				position.SetVec2(averagePosition);
			}
			else
			{
				float flankRange = 45f;
				float feintRange = 30f;

				Formation enemyFormation = base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation;
				Formation allyFormation = RBMAI.Utilities.FindSignificantAlly(base.Formation, true, true, false, false, false);

				if (base.Formation != null && base.Formation.QuerySystem.IsInfantryFormation)
				{
					enemyFormation = RBMAI.Utilities.FindSignificantEnemyToPosition(base.Formation, position, true, true, false, false, false, false);
				}

				if(enemyFormation == null)
                {
					base.CurrentOrder = MovementOrder.MovementOrderStop;
					return;
				}

				Vec2 averageAllyFormationPosition = base.Formation.QuerySystem.Team.AveragePosition;
				WorldPosition medianTargetFormationPosition = base.Formation.QuerySystem.Team.MedianTargetFormationPosition;
				Vec2 enemyDirection = (medianTargetFormationPosition.AsVec2 - averageAllyFormationPosition).Normalized();

				switch (flankMode)
                {
					case FlankMode.Flank:
                        {
							if (averagePosition.Distance(enemyFormation.QuerySystem.AveragePosition) < flankRange)
                            {
								flankMode = FlankMode.Attack;
							}
							//if (averagePosition.Distance(base.Formation.OrderPosition) < 5f)
							//{
							//	flankMode = FlankMode.Attack;
							//}

							if (behaviorSide == FormationAI.BehaviorSide.Right || FlankSide == FormationAI.BehaviorSide.Right)
							{
								if (enemyFormation != null)
								{
									Vec2 calcPosition = enemyFormation.CurrentPosition + enemyDirection.RightVec().Normalized() * (enemyFormation.Width * 0.5f + flankRange);
									position.SetVec2(calcPosition);
								}
								else
								{
									position.SetVec2(medianTargetFormationPosition.AsVec2 + enemyDirection.Normalized() * 140f);
								}
							}
							else if (behaviorSide == FormationAI.BehaviorSide.Left || FlankSide == FormationAI.BehaviorSide.Left)
							{
								if (enemyFormation != null)
								{
									Vec2 calcPosition = enemyFormation.CurrentPosition + enemyDirection.LeftVec().Normalized() * (enemyFormation.Width * 0.5f + flankRange);
									position.SetVec2(calcPosition);
								}
								else
								{
									position.SetVec2(medianTargetFormationPosition.AsVec2 + enemyDirection.Normalized() * 140f);
								}
							}
                            else
                            {
								if (enemyFormation != null)
								{
									position = enemyFormation.QuerySystem.MedianPosition;
								}
								else
                                {
									position.SetVec2(medianTargetFormationPosition.AsVec2 + enemyDirection.Normalized() * 140f);
								}
							}
							break;
                        }
					//case FlankMode.Feint:
					//	{
					//		attackTimer = null;
					//		if (returnTimer == null)
					//		{
					//			returnTimer = new Timer(Mission.Current.CurrentTime, 10f/ mobilityModifier);
					//		}
					//		if (returnTimer != null && returnTimer.Check(Mission.Current.CurrentTime))
					//		{
					//				flankMode = FlankMode.Flank;
					//		}

					//		if (behaviorSide == FormationAI.BehaviorSide.Right || FlankSide == FormationAI.BehaviorSide.Right)
					//		{
					//			if (allyFormation != null)
					//			{
					//				Vec2 calcPosition = allyFormation.CurrentPosition + enemyDirection.RightVec().Normalized() * (allyFormation.Width + base.Formation.Width + flankRange);
					//				position.SetVec2(calcPosition);
					//			}
					//			else
     //                           {
					//				position.SetVec2(medianTargetFormationPosition.AsVec2 + enemyDirection.Normalized() * 150f);
					//			}
					//		}
					//		else if (behaviorSide == FormationAI.BehaviorSide.Left || FlankSide == FormationAI.BehaviorSide.Left)
					//		{
					//			if (allyFormation != null)
					//			{
					//				Vec2 calcPosition = allyFormation.CurrentPosition + enemyDirection.LeftVec().Normalized() * (allyFormation.Width + base.Formation.Width + flankRange);
					//				position.SetVec2(calcPosition);
					//			}
					//			else
					//			{
					//				position.SetVec2(medianTargetFormationPosition.AsVec2 + enemyDirection.Normalized() * 150f);
					//			}
					//		}
					//		else
					//		{
					//			if (allyFormation != null)
					//			{
					//				position = allyFormation.QuerySystem.MedianPosition;

					//			}
					//			else
					//			{
					//				position.SetVec2(medianTargetFormationPosition.AsVec2 + enemyDirection.Normalized() * 150f);
					//			}
					//		}

					//		break;
					//	}
					case FlankMode.Attack:
						{
							if(enemyFormation != null)
                            {
								base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(enemyFormation);
								return;
							}
							break;
						}
				}
			}
			base.CurrentOrder = MovementOrder.MovementOrderMove(position);
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
