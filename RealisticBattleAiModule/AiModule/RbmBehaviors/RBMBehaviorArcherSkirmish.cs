﻿using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RealisticBattleAiModule.AiModule.RbmBehaviors
{
	class RBMBehaviorArcherSkirmish : BehaviorComponent
	{

		private int flankCooldownMax = 40;

		public int side = MBRandom.RandomInt(2);
		public int cooldown = 0;

		private enum BehaviorState
		{
			Approaching,
			Shooting,
			PullingBack
		}

		private bool _cantShoot;

		private float _cantShootDistance = float.MaxValue;

		private BehaviorState _behaviorState = BehaviorState.Shooting;

		private Timer _cantShootTimer;

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
			Vec2 vec;
			if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
			{
				vec = base.Formation.Direction;
				medianPosition.SetVec2(base.Formation.QuerySystem.AveragePosition);
			}
			else
			{
				Formation significantEnemy = null;
				if (base.Formation != null && base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
				{
					significantEnemy = Utilities.FindSignificantEnemy(base.Formation, true, true, false, false, false);
				}
				if (significantEnemy == null)
				{
					significantEnemy = base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation;
				}
				vec = significantEnemy.QuerySystem.MedianPosition.AsVec2 - base.Formation.QuerySystem.AveragePosition;
				float distance = vec.Normalize();
				//float ratioOfShooting = MBMath.Lerp(0.1f, 0.33f, 1f - MBMath.ClampFloat(base.Formation.CountOfUnits, 1f, 50f) * 0.02f) * base.Formation.QuerySystem.RangedUnitRatio;
				float ratioOfShooting = 0.33f;
				float effectiveShootingRange = base.Formation.QuerySystem.MissileRange / 1.9f;
				switch (_behaviorState)
				{
					case BehaviorState.Shooting:
						if (distance > effectiveShootingRange * 1.1f)
						{
							_behaviorState = BehaviorState.Approaching;
							_cantShootDistance = MathF.Min(_cantShootDistance, effectiveShootingRange);
							break;
						}
						if (base.Formation.QuerySystem.MakingRangedAttackRatio <= ratioOfShooting)
						{
							if (distance > effectiveShootingRange)
							{
								_behaviorState = BehaviorState.Approaching;
								_cantShootDistance = MathF.Min(_cantShootDistance, effectiveShootingRange);
								break;
							}
							else if (!_cantShoot)
							{
								_cantShoot = true;
								_cantShootTimer.Reset(Mission.Current.CurrentTime, MBMath.Lerp(5f, 10f, (MBMath.ClampFloat(base.Formation.CountOfUnits, 10f, 60f) - 10f) * 0.02f));
								break;
							}
							else if (_cantShootTimer.Check(Mission.Current.CurrentTime))
							{
								_behaviorState = BehaviorState.Approaching;
								_cantShootDistance = MathF.Min(_cantShootDistance, distance);
								break;
							}

							_cantShootDistance = MathF.Max(_cantShootDistance, distance);
							_cantShoot = false;
							if (base.Formation.QuerySystem.IsRangedFormation && distance < MathF.Min(effectiveShootingRange * 0.4f, _cantShootDistance * 0.666f))
							{
								Formation meleeFormation = Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);
								if (meleeFormation != null && meleeFormation.QuerySystem.IsInfantryFormation)
								{
									_behaviorState = BehaviorState.PullingBack;
									break;
								}
							}

							//if (cooldown < flankCooldownMax)
							//{
							//	cooldown++;
							//}
							//else
							//{
							//	if (side == 0)
							//	{
							//		medianPosition.SetVec2(base.Formation.QuerySystem.AveragePosition + base.Formation.QuerySystem.AveragePosition.LeftVec().Normalized() * 20f);
							//	}
							//	else if (side == 1)
							//	{
							//		medianPosition.SetVec2(base.Formation.QuerySystem.AveragePosition + base.Formation.QuerySystem.AveragePosition.RightVec().Normalized() * 20f);
							//	}

							//	if (!base.CurrentOrder.GetPosition(base.Formation).IsValid || _behaviorState != BehaviorState.Shooting || flag)
							//	{
							//		base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition);
							//	}
							//	if (!CurrentFacingOrder.GetDirection(base.Formation).IsValid || _behaviorState != BehaviorState.Shooting || flag)
							//	{
							//		CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
							//	}

							//	cooldown = 0;

							//	return;
							//}
						}
						else
						{
							_cantShootDistance = MathF.Max(_cantShootDistance, distance);
							_cantShoot = false;
							if (base.Formation.QuerySystem.IsRangedFormation && distance < MathF.Min(effectiveShootingRange * 0.4f, _cantShootDistance * 0.666f))
							{
								Formation meleeFormation = Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);
								if (meleeFormation != null && meleeFormation.QuerySystem.IsInfantryFormation)
                                {
									_behaviorState = BehaviorState.PullingBack;
									break;
								}
							}
							
						}
						break;
					case BehaviorState.Approaching:
						if (distance < _cantShootDistance * 0.8f && distance < effectiveShootingRange * 0.9f)
						{
							_behaviorState = BehaviorState.Shooting;
							_cantShoot = false;
							flag = true;
						}
						else if (base.Formation.QuerySystem.MakingRangedAttackRatio >= ratioOfShooting * 1.2f && distance < effectiveShootingRange * 0.9f)
						{
							_behaviorState = BehaviorState.Shooting;
							_cantShoot = false;
							flag = true;
						}
						break;
					case BehaviorState.PullingBack:
						if (distance > MathF.Min(_cantShootDistance, effectiveShootingRange) * 0.9f)
						{
							_behaviorState = BehaviorState.Shooting;
							_cantShoot = false;
							flag = true;
						}
						break;
				}
				switch (_behaviorState)
				{
					case BehaviorState.Shooting:
						medianPosition.SetVec2(base.Formation.QuerySystem.AveragePosition);
						break;
					case BehaviorState.Approaching:
						medianPosition = significantEnemy.QuerySystem.ClosestSignificantlyLargeEnemyFormation.MedianPosition;
						medianPosition.SetVec2(base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.AveragePosition);
						break;
					case BehaviorState.PullingBack:
						medianPosition = significantEnemy.QuerySystem.ClosestSignificantlyLargeEnemyFormation.MedianPosition;
						medianPosition.SetVec2(medianPosition.AsVec2 - vec * (effectiveShootingRange - base.Formation.Depth * 0.5f - 10f));
						break;
				}
			}
			if (!base.CurrentOrder.GetPosition(base.Formation).IsValid || _behaviorState != BehaviorState.Shooting || flag)
			{
				base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition);
			}
			if (!CurrentFacingOrder.GetDirection(base.Formation).IsValid || _behaviorState != BehaviorState.Shooting || flag)
			{
				CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
			}
		}

		public override void TickOccasionally()
		{
			CalculateCurrentOrder();
			base.Formation.SetMovementOrder(base.CurrentOrder);
			base.Formation.FacingOrder = CurrentFacingOrder;
		}

		protected override void OnBehaviorActivatedAux()
		{
			_cantShoot = false;
			_cantShootDistance = float.MaxValue;
			_behaviorState = BehaviorState.Shooting;
			_cantShootTimer.Reset(Mission.Current.CurrentTime, MBMath.Lerp(5f, 10f, (MBMath.ClampFloat(base.Formation.CountOfUnits, 10f, 60f) - 10f) * 0.02f));
			CalculateCurrentOrder();
			base.Formation.SetMovementOrder(base.CurrentOrder);
			base.Formation.FacingOrder = CurrentFacingOrder;
			base.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
			base.Formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
			base.Formation.FormOrder = FormOrder.FormOrderWide;
			base.Formation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;
		}

		protected override float GetAiWeight()
		{
			FormationQuerySystem querySystem = base.Formation.QuerySystem;
			return MBMath.Lerp(0.1f, 1f, MBMath.ClampFloat(querySystem.RangedUnitRatio + querySystem.RangedCavalryUnitRatio, 0f, 0.5f) * 2f);
		}
	}
}