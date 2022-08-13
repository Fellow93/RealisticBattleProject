using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
	class RBMBehaviorArcherSkirmish : BehaviorComponent
	{

		private int flankCooldownMax = 40;

		public int side = MBRandom.RandomInt(2);
		public int cooldown = 0;

		public bool wasShootingBefore = false;
		private enum BehaviorState
		{
			Approaching,
			Shooting,
			PullingBack
		}

		private bool _cantShoot;

		private float _cantShootDistance = float.MaxValue;

		private BehaviorState _behaviorState = BehaviorState.PullingBack;

		private Timer _cantShootTimer;

		private bool switchedToSkirmish = true;

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
					significantEnemy = RBMAI.Utilities.FindSignificantEnemy(base.Formation, true, true, false, false, false, true, 0.20f);
				}
				if (significantEnemy == null)
				{
					significantEnemy = base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation;
				}
				Formation significantAlly = null;
				significantAlly = RBMAI.Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);

				vec = significantEnemy.QuerySystem.MedianPosition.AsVec2 - base.Formation.QuerySystem.AveragePosition;
				float distance = vec.Normalize();
				//float ratioOfShooting = MBMath.Lerp(0.1f, 0.33f, 1f - MBMath.ClampFloat(base.Formation.CountOfUnits, 1f, 50f) * 0.02f) * base.Formation.QuerySystem.RangedUnitRatio;
				float ratioOfShooting = 0.3f;
				//base.Formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
				//{
				//	agent.SetScriptedCombatFlags(agent.GetScriptedCombatFlags() | Agent.AISpecialCombatModeFlags.IgnoreAmmoLimitForRangeCalculation);
				//});
				float effectiveShootingRange = base.Formation.QuerySystem.MissileRange / 1.8f;
				if (significantAlly != null && (significantAlly == base.Formation || !significantAlly.QuerySystem.IsInfantryFormation))
				{
					effectiveShootingRange *= 1.9f;
				}

				switch (_behaviorState)
				{
					case BehaviorState.Shooting:
						{
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
								//_cantShoot = false;
								if (base.Formation.QuerySystem.IsRangedFormation && distance < MathF.Min(effectiveShootingRange * 0.5f, _cantShootDistance * 0.666f))
								{
									Formation meleeFormation = RBMAI.Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);
									if (meleeFormation != null && meleeFormation.QuerySystem.IsInfantryFormation)
									{
										_behaviorState = BehaviorState.PullingBack;
										break;
									}
								}

							}
							else
							{
								_cantShootDistance = MathF.Max(_cantShootDistance, distance);

								if (base.Formation.QuerySystem.IsRangedFormation && distance < MathF.Min(effectiveShootingRange * 0.4f, _cantShootDistance * 0.666f))
								{
									Formation meleeFormation = RBMAI.Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);
									if (meleeFormation != null && meleeFormation.QuerySystem.IsInfantryFormation)
									{
										_cantShoot = true;
										_behaviorState = BehaviorState.PullingBack;
										break;
									}
								}
							}
							break;
						}
					case BehaviorState.Approaching:
						{
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
							else if (distance < effectiveShootingRange * 0.4f)
							{
								_behaviorState = BehaviorState.PullingBack;
								_cantShoot = false;
								flag = true;
							}
							else if (distance < effectiveShootingRange * 0.9f && !wasShootingBefore)
							{
								_behaviorState = BehaviorState.Shooting;
								_cantShoot = false;
								flag = true;
								wasShootingBefore = true;
							}
							break;
						}
					case BehaviorState.PullingBack:
						{
							Formation meleeFormationPull = RBMAI.Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);
							if (meleeFormationPull == null || !meleeFormationPull.QuerySystem.IsInfantryFormation)
							{
								_behaviorState = BehaviorState.Shooting;
								_cantShoot = false;
								flag = true;
							}
							if (distance > MathF.Min(_cantShootDistance, effectiveShootingRange) * 0.9f)
							{
								_behaviorState = BehaviorState.Shooting;
								_cantShoot = false;
								flag = true;
							}
							break;
						}
				}
				bool isOnlyCavReamining = RBMAI.Utilities.CheckIfOnlyCavRemaining(base.Formation);
				if (isOnlyCavReamining)
				{
					_behaviorState = BehaviorState.Shooting;
					_cantShoot = false;
				}
				//if (effectiveShootingRange <= 0f)
				//{
				//	_behaviorState = BehaviorState.PullingBack;
				//	medianPosition.SetVec2(base.Formation.QuerySystem.AveragePosition);
				//	if (!base.CurrentOrder.GetPosition(base.Formation).IsValid || _behaviorState != BehaviorState.Shooting || flag)
				//	{
				//		base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition);
				//	}
				//	if (!CurrentFacingOrder.GetDirection(base.Formation).IsValid || _behaviorState != BehaviorState.Shooting || flag)
				//	{
				//		CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
				//	}
				//	return;
				//}
				switch (_behaviorState)
				{
					case BehaviorState.Shooting:
						medianPosition.SetVec2(base.Formation.QuerySystem.AveragePosition);
						break;
					case BehaviorState.Approaching:
						//medianPosition = significantEnemy.QuerySystem.MedianPosition;
						medianPosition.SetVec2(significantEnemy.QuerySystem.AveragePosition);
						break;
					case BehaviorState.PullingBack:
						medianPosition = significantEnemy.QuerySystem.MedianPosition;
						//medianPosition.SetVec2(medianPosition.AsVec2 - vec * (effectiveShootingRange - base.Formation.Depth * 0.5f - 10f));
						if (side == 0)
						{
							medianPosition.SetVec2((medianPosition.AsVec2 - vec * (effectiveShootingRange - base.Formation.Depth * 0.5f - 10f)) + base.Formation.QuerySystem.AveragePosition.LeftVec().Normalized() * MBRandom.RandomFloat * 20f);
						}
						else if (side == 1)
						{
							medianPosition.SetVec2((medianPosition.AsVec2 - vec * (effectiveShootingRange - base.Formation.Depth * 0.5f - 10f)) + base.Formation.QuerySystem.AveragePosition.RightVec().Normalized() * MBRandom.RandomFloat * 20f);
						}
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
			switchedToSkirmish = true;
			_cantShoot = false;
			_cantShootDistance = float.MaxValue;
			_behaviorState = BehaviorState.PullingBack;
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
