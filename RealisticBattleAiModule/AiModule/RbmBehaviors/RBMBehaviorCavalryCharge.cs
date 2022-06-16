using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Engine;

public class RBMBehaviorCavalryCharge : BehaviorComponent
{
	private enum ChargeState
	{
		Undetermined,
		Charging,
		ChargingPast,
		Reforming,
		Bracing
	}

	private ChargeState _chargeState;

	private FormationQuerySystem _lastTarget;

	private Vec2 _initialChargeDirection;

	private float _desiredChargeStopDistance;

	private WorldPosition _lastReformDestination;

	private Timer _chargingPastTimer;

	private Timer _reformTimer;

	private Vec2 _bracePosition = Vec2.Invalid;

	public bool ChargeArchers = false;
	public bool ChargeInfantry = false;
	public bool ChargeCavalry = false;
	public bool ChargeHorseArchers = false;

	public override float NavmeshlessTargetPositionPenalty => 1f;

	public RBMBehaviorCavalryCharge(Formation formation)
		: base(formation)
	{
		_lastTarget = null;
		base.CurrentOrder = MovementOrder.MovementOrderCharge;
		CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
		_chargeState = ChargeState.Undetermined;
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
			base.Formation.FacingOrder = CurrentFacingOrder;
		}
	}

	private ChargeState CheckAndChangeState()
	{
		ChargeState result = _chargeState;
		if (base.Formation.QuerySystem.ClosestEnemyFormation == null)
		{
			result = ChargeState.Undetermined;
		}
		else
		{
			switch (_chargeState)
			{
				case ChargeState.Undetermined:
                    {
						if (base.Formation.QuerySystem.ClosestEnemyFormation != null && ((!base.Formation.QuerySystem.IsCavalryFormation && !base.Formation.QuerySystem.IsRangedCavalryFormation) || base.Formation.QuerySystem.AveragePosition.Distance(base.Formation.QuerySystem.ClosestEnemyFormation.MedianPosition.AsVec2) / base.Formation.QuerySystem.MovementSpeedMaximum <= 5f))
						{
							result = ChargeState.Charging;
						}
						break;
					}
				case ChargeState.Charging:
                    {
						if (_lastTarget == null || _lastTarget.Formation.CountOfUnits == 0)
						{
							result = ChargeState.Undetermined;
						}
						else if (_initialChargeDirection.DotProduct(_lastTarget.MedianPosition.AsVec2 - base.Formation.QuerySystem.AveragePosition) <= 0f)
						{
							result = ChargeState.ChargingPast;
						}
						break;
					}
				case ChargeState.ChargingPast:
					if (_chargingPastTimer.Check(Mission.Current.CurrentTime) || base.Formation.QuerySystem.AveragePosition.Distance(_lastTarget.MedianPosition.AsVec2) >= _desiredChargeStopDistance)
					{
						result = ChargeState.Reforming;
					}
					break;
				case ChargeState.Reforming:
					if (_reformTimer.Check(Mission.Current.CurrentTime) ) //|| base.Formation.QuerySystem.AveragePosition.Distance(_lastTarget.MedianPosition.AsVec2) <= 30f)
					{
						result = ChargeState.Charging;
					}
					break;
				case ChargeState.Bracing:
					{
						bool flag = false;
						if (!flag)
						{
							_bracePosition = Vec2.Invalid;
							_chargeState = ChargeState.Charging;
						}
						break;
					}
			}
		}
		return result;
	}

	protected override void CalculateCurrentOrder()
	{
		if (base.Formation.QuerySystem.ClosestEnemyFormation == null)
		{
			base.CurrentOrder = MovementOrder.MovementOrderCharge;
			return;
		}

		ChargeState chargeState = CheckAndChangeState();	

		if (chargeState != _chargeState)
		{
			_chargeState = chargeState;
			switch (_chargeState)
			{
				case ChargeState.Undetermined:
					base.CurrentOrder = MovementOrder.MovementOrderCharge;
					break;
				case ChargeState.Charging:
                    {
						Formation correctEnemy = null;
						if (ChargeInfantry)
						{
							correctEnemy = RealisticBattleAiModule.Utilities.FindSignificantEnemy(base.Formation, true, false, false, false, false);
						}
						else if (ChargeArchers)
						{
							correctEnemy = RealisticBattleAiModule.Utilities.FindSignificantEnemy(base.Formation, false, true, false, false, false);
						}
						else if (ChargeCavalry)
						{
							correctEnemy = RealisticBattleAiModule.Utilities.FindSignificantEnemy(base.Formation, false, false, true, true, true);
						}
						else
						{
							correctEnemy = RealisticBattleAiModule.Utilities.FindSignificantEnemy(base.Formation, true, true, false, false, false);
						}
						if (correctEnemy != null)
						{
							_lastTarget = correctEnemy.QuerySystem;
						}
						else
						{
							_lastTarget = base.Formation.QuerySystem.ClosestEnemyFormation;
						}
						_initialChargeDirection = _lastTarget.MedianPosition.AsVec2 - base.Formation.QuerySystem.AveragePosition;
						float value = _initialChargeDirection.Normalize();
						_desiredChargeStopDistance = 110f;
						break;
					}
				case ChargeState.ChargingPast:
					_chargingPastTimer = new Timer(Mission.Current.CurrentTime, 22f);
					break;
				case ChargeState.Reforming:
					_reformTimer = new Timer(Mission.Current.CurrentTime, 12f);
					break;
				case ChargeState.Bracing:
					{
						Vec2 vec = (base.Formation.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - base.Formation.QuerySystem.AveragePosition).Normalized();
						_bracePosition = base.Formation.QuerySystem.AveragePosition + vec * 5f;
						break;
					}
			}
		}

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
					Vec2 vec4 = (_lastTarget.MedianPosition.AsVec2 - base.Formation.QuerySystem.AveragePosition).Normalized();
					WorldPosition medianPosition3 = _lastTarget.MedianPosition;
					Vec2 vec5 = medianPosition3.AsVec2 + vec4 * _desiredChargeStopDistance;
					medianPosition3.SetVec2(vec5);
					base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition3);
					CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec4);
					break;
				}
			case ChargeState.ChargingPast:
				{
					Vec2 vec2 = (base.Formation.QuerySystem.AveragePosition - _lastTarget.MedianPosition.AsVec2).Normalized();
					_lastReformDestination = _lastTarget.MedianPosition;
					Vec2 vec3 = _lastTarget.MedianPosition.AsVec2 + vec2 * _desiredChargeStopDistance;
					_lastReformDestination.SetVec2(vec3);
					base.CurrentOrder = MovementOrder.MovementOrderMove(_lastReformDestination);
					CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec2);
					break;
				}
			case ChargeState.Reforming:
				base.CurrentOrder = MovementOrder.MovementOrderMove(_lastReformDestination);
				CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
				break;
			case ChargeState.Bracing:
				{
					WorldPosition medianPosition = base.Formation.QuerySystem.MedianPosition;
					medianPosition.SetVec2(_bracePosition);
					base.CurrentOrder = MovementOrder.MovementOrderMove(medianPosition);
					break;
				}
		}
	}

	protected override void OnBehaviorActivatedAux()
	{
		CalculateCurrentOrder();
		base.Formation.SetMovementOrder(base.CurrentOrder);
		base.Formation.FacingOrder = CurrentFacingOrder;
		base.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
		base.Formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
		base.Formation.FormOrder = FormOrder.FormOrderWide;
		base.Formation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;
	}

	public override TextObject GetBehaviorString()
	{
		TextObject behaviorString = base.GetBehaviorString();
		if (base.Formation.QuerySystem.ClosestEnemyFormation != null)
		{
			behaviorString.SetTextVariable("AI_SIDE", GameTexts.FindText("str_formation_ai_side_strings", base.Formation.QuerySystem.ClosestEnemyFormation.Formation.AI.Side.ToString()));
			behaviorString.SetTextVariable("CLASS", GameTexts.FindText("str_formation_class_string", base.Formation.QuerySystem.ClosestEnemyFormation.Formation.PrimaryClass.GetName()));
		}
		return behaviorString;
	}

	protected override float GetAiWeight()
	{
		FormationQuerySystem querySystem = base.Formation.QuerySystem;
		if (querySystem.ClosestEnemyFormation == null)
		{
			return 0f;
		}
		float num = querySystem.AveragePosition.Distance(querySystem.ClosestEnemyFormation.MedianPosition.AsVec2) / querySystem.MovementSpeedMaximum;
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
		WorldPosition medianPosition = querySystem.MedianPosition;
		medianPosition.SetVec2(querySystem.AveragePosition);
		float num6 = medianPosition.GetNavMeshZ() - querySystem.ClosestEnemyFormation.MedianPosition.GetNavMeshZ();
		float num7 = 1f;
		if (num <= 4f)
		{
			float value = num6 / (querySystem.AveragePosition - querySystem.ClosestEnemyFormation.MedianPosition.AsVec2).Length;
			num7 = MBMath.Lerp(0.9f, 1.1f, (MBMath.ClampFloat(value, -0.58f, 0.58f) + 0.58f) / 1.16f);
		}
		float num8 = 1f;
		if (num <= 4f && num >= 1.5f)
		{
			num8 = 1.2f;
		}
		float num9 = 1f;
		if (num <= 4f && querySystem.ClosestEnemyFormation.ClosestEnemyFormation != querySystem)
		{
			num9 = 1.2f;
		}
		float num10 = querySystem.GetClassWeightedFactor(1f, 1f, 1.5f, 1.5f) * querySystem.ClosestEnemyFormation.GetClassWeightedFactor(1f, 1f, 0.5f, 0.5f);
		return num3 * num7 * num8 * num9 * num10;
	}
}
