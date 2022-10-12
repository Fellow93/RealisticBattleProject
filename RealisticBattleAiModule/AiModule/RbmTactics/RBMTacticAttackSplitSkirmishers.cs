using RBMAI;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

public class RBMTacticAttackSplitSkirmishers : TacticComponent
{
	protected Formation _skirmishers;
	int side = MBRandom.RandomInt(2);
	int waitCountMainFormation = 0;
	int waitCountMainFormationMax = 25;

	protected void AssignTacticFormations()
	{
		int skirmIndex = -1;
		ManageFormationCounts(2, 1, 2, 1);
        _mainInfantry = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
		if (_mainInfantry != null)
		{
			_mainInfantry.AI.IsMainFormation = true;
			_mainInfantry.AI.Side = FormationAI.BehaviorSide.Middle;

			List<Agent> skirmishersList = new List<Agent>();
			List<Agent> meleeList = new List<Agent>();

			_mainInfantry.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
			{
				bool isSkirmisher = false;
                //for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                //{
                //	if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                //	{
                //		if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown)
                //		{
                //			isSkirmisher = true;
                //			break;
                //		}
                //	}
                //}

                if (RBMAI.Utilities.CheckIfSkirmisherAgent(agent))
                {
					isSkirmisher = true;
				}


				if (isSkirmisher)
				{
					skirmishersList.Add(agent);
				}
				else
				{
					meleeList.Add(agent);
				}
			});

			int i = 0;
			foreach(Formation formation in Formations.ToList())
            {
				formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
				{
					if (i != 0 && formation.IsInfantry())
					{
						bool isSkirmisher = false;
						//for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
						//{
						//	if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
						//	{
						//		if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown)
						//		{
						//			isSkirmisher = true;
						//			break;
						//		}
						//	}
						//}

						if (RBMAI.Utilities.CheckIfSkirmisherAgent(agent,2))
						{
							isSkirmisher = true;
						}


						if (isSkirmisher)
						{
							skirmishersList.Add(agent);
						}
						else
						{
							meleeList.Add(agent);
						}
						skirmIndex = i;
					}
				});
				i++;
            }

			//Formations.ToList()[1].ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
			//{
			//	bool isSkirmisher = false;
			//	//for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
			//	//{
			//	//	if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
			//	//	{
			//	//		if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown)
			//	//		{
			//	//			isSkirmisher = true;
			//	//			break;
			//	//		}
			//	//	}
			//	//}

			//	if (agent.HasThrownCached)
			//	{
			//		isSkirmisher = true;
			//	}


			//	if (isSkirmisher)
			//	{
			//		skirmishersList.Add(agent);
			//	}
			//	else
			//	{
			//		meleeList.Add(agent);
			//	}
			//});

			skirmishersList = skirmishersList.OrderBy(o => o.CharacterPowerCached).ToList();
			if(skirmIndex != -1)
            {
				int j = 0;
				int infCount = Formations.ToList()[0].CountOfUnits + Formations.ToList()[skirmIndex].CountOfUnits;
				foreach (Agent agent in skirmishersList.ToList())
				{
					if (j < infCount / 4f)
					{
						agent.Formation = Formations.ToList()[skirmIndex];
					}
					else
					{
						agent.Formation = Formations.ToList()[0];
					}
					j++;
				}

				foreach (Agent agent in meleeList.ToList())
				{
					if (j < infCount / 4f)
					{
						agent.Formation = Formations.ToList()[skirmIndex];
					}
					else
					{
						agent.Formation = Formations.ToList()[0];
					}
					j++;
				}
				if(Formations.ToList().Count > skirmIndex)
				{
                    this.team.TriggerOnFormationsChanged(Formations.ToList()[skirmIndex]);
                    this.team.TriggerOnFormationsChanged(Formations.ToList()[0]);
                }
            }
			
		}

		_archers = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsRangedFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
		List<Formation> list = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower);
		if (list.Count > 0)
		{
			_leftCavalry = list[0];
			_leftCavalry.AI.Side = FormationAI.BehaviorSide.Left;
			if (list.Count > 1)
			{
				_rightCavalry = list[1];
				_rightCavalry.AI.Side = FormationAI.BehaviorSide.Right;
			}
			else
			{
				_rightCavalry = null;
			}
		}
		else
		{
			_leftCavalry = null;
			_rightCavalry = null;
		}
		_rangedCavalry = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsRangedCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();

		if(skirmIndex != -1 && Formations.Count() > skirmIndex && Formations.ToList()[skirmIndex].QuerySystem.IsInfantryFormation)
        {
			_skirmishers = Formations.ToList()[skirmIndex];
            _skirmishers.AI.IsMainFormation = false;

        }
        //if (_skirmishers != null)
        //{
        //_skirmishers.AI.Side = FormationAI.BehaviorSide.BehaviorSideNotSet;
        //_skirmishers.AI.IsMainFormation = false;
        //_skirmishers.AI.ResetBehaviorWeights();
        //SetDefaultBehaviorWeights(_skirmishers);
        //team.ClearRecentlySplitFormations(_skirmishers);

        //_skirmishers = Formations.ToList()[1];
        //}

        IsTacticReapplyNeeded = true;
    }

	private bool _hasBattleBeenJoined;

	public RBMTacticAttackSplitSkirmishers(Team team)
		: base(team)
	{
		_hasBattleBeenJoined = false;
	}

	protected override void ManageFormationCounts()
	{
		AssignTacticFormations();
	}

	private void Advance()
	{
		if (team.IsPlayerTeam && !team.IsPlayerGeneral && team.IsPlayerSergeant)
		{
			SoundTacticalHorn(TacticComponent.MoveHornSoundIndex);
		}
		if(_skirmishers != null)
        {
			_skirmishers.AI.ResetBehaviorWeights();
			//TacticComponent.SetDefaultBehaviorWeights(_skirmishers);
            //_skirmishers.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
            if (side == 0)
            {
				_skirmishers.AI.Side = FormationAI.BehaviorSide.Left;
				_skirmishers.AI.SetBehaviorWeight<RBMAI.RBMBehaviorForwardSkirmish>(1f).FlankSide = FormationAI.BehaviorSide.Left;
			}
			else
            {
				_skirmishers.AI.Side = FormationAI.BehaviorSide.Right;
				_skirmishers.AI.SetBehaviorWeight<RBMAI.RBMBehaviorForwardSkirmish>(1f).FlankSide = FormationAI.BehaviorSide.Right;
			}
		}
		if (_mainInfantry != null)
		{
			if(waitCountMainFormation < waitCountMainFormationMax)
            {
				_mainInfantry.AI.ResetBehaviorWeights();
				TacticComponent.SetDefaultBehaviorWeights(_mainInfantry);
				_mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.5f);
				waitCountMainFormation++;
				IsTacticReapplyNeeded = true;
			}
			else
            {
				_mainInfantry.AI.SetBehaviorWeight<BehaviorAdvance>(1f);
				IsTacticReapplyNeeded = false;
			}

		}
		if (_archers != null)
		{
			_archers.AI.ResetBehaviorWeights();
			TacticComponent.SetDefaultBehaviorWeights(_archers);
			_archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
			_archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
			_archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
			_archers.AI.SetBehaviorWeight<BehaviorRegroup>(1.25f);
		}
		if (_leftCavalry != null)
		{
			_leftCavalry.AI.ResetBehaviorWeights();
			TacticComponent.SetDefaultBehaviorWeights(_leftCavalry);
			_leftCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;
			_leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
		}
		if (_rightCavalry != null)
		{
			_rightCavalry.AI.ResetBehaviorWeights();
			TacticComponent.SetDefaultBehaviorWeights(_rightCavalry);
			_rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
			_rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
		}
		if (_rangedCavalry != null)
		{
			_rangedCavalry.AI.ResetBehaviorWeights();
			TacticComponent.SetDefaultBehaviorWeights(_rangedCavalry);
			_rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
		}
	}

	private void Attack()
	{
		if (team.IsPlayerTeam && !team.IsPlayerGeneral && team.IsPlayerSergeant)
		{
			SoundTacticalHorn(TacticComponent.AttackHornSoundIndex);
		}
		if (_mainInfantry != null)
		{
			_mainInfantry.AI.ResetBehaviorWeights();
			_mainInfantry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
		}
		if (_skirmishers != null)
		{
			_skirmishers.AI.ResetBehaviorWeights();
			_skirmishers.AI.SetBehaviorWeight<BehaviorCharge>(1f);
			//if (side == 0)
			//{
			//	_skirmishers.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f).FlankSide = FormationAI.BehaviorSide.Left;
			//}
			//else
			//{
			//	_skirmishers.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f).FlankSide = FormationAI.BehaviorSide.Right;
			//}
		}
		if (_archers != null)
		{
			_archers.AI.ResetBehaviorWeights();
			_archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
			_archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
			_archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
		}
		if (_leftCavalry != null)
		{
			_leftCavalry.AI.ResetBehaviorWeights();
            SetDefaultBehaviorWeights(_leftCavalry);
            _leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
			_leftCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
		}
		if (_rightCavalry != null)
		{
			_rightCavalry.AI.ResetBehaviorWeights();
            SetDefaultBehaviorWeights(_rightCavalry);
            _rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
			_rightCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
		}
		if (_rangedCavalry != null)
		{
			_rangedCavalry.AI.ResetBehaviorWeights();
			TacticComponent.SetDefaultBehaviorWeights(_rangedCavalry);
			_rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
		}
		IsTacticReapplyNeeded = false;
	}

	private bool HasBattleBeenJoined()
	{
		return RBMAI.Utilities.HasBattleBeenJoined(_mainInfantry, _hasBattleBeenJoined, 85f);
	}

	protected override bool CheckAndSetAvailableFormationsChanged()
	{
		int num = base.Formations.Count((Formation f) => f.IsAIControlled);
		bool num2 = num != _AIControlledFormationCount;
		if (num2)
		{
			_AIControlledFormationCount = num;
			IsTacticReapplyNeeded = true;
		}
		if (!num2)
		{
			if ((_mainInfantry == null || (_mainInfantry.CountOfUnits != 0 && _mainInfantry.QuerySystem.IsInfantryFormation)) && (_archers == null || (_archers.CountOfUnits != 0 && _archers.QuerySystem.IsRangedFormation)) && (_leftCavalry == null || (_leftCavalry.CountOfUnits != 0 && _leftCavalry.QuerySystem.IsCavalryFormation)) && (_rightCavalry == null || (_rightCavalry.CountOfUnits != 0 && _rightCavalry.QuerySystem.IsCavalryFormation)))
			{
				if (_rangedCavalry != null)
				{
					if (_rangedCavalry.CountOfUnits != 0)
					{
						return !_rangedCavalry.QuerySystem.IsRangedCavalryFormation;
					}
					return true;
				}
				return false;
			}
			return true;
		}
		return true;
	}

	protected override void TickOccasionally()
	{
		if (!base.AreFormationsCreated)
		{
			return;
		}
		if (CheckAndSetAvailableFormationsChanged())
		{
			ManageFormationCounts();
			if (_hasBattleBeenJoined)
			{
				Attack();
			}
			else
			{
				Advance();
			}
			//IsTacticReapplyNeeded = false;
		}
		bool flag = HasBattleBeenJoined();
		if (flag != _hasBattleBeenJoined || IsTacticReapplyNeeded)
		{
			_hasBattleBeenJoined = flag;
			if (_hasBattleBeenJoined)
			{
				Attack();
			}
			else
			{
				Advance();
			}
			//IsTacticReapplyNeeded = false;
		}
		base.TickOccasionally();
	}

	protected override float GetTacticWeight()
	{
		float skirmisherCount = 0;
		float infCount = 0;
		foreach(Agent agent in team.ActiveAgents.ToList())
        {
            if (agent.Formation != null && agent.Formation.QuerySystem.IsInfantryFormation) {
                if (RBMAI.Utilities.CheckIfSkirmisherAgent(agent, 2))
                {
					skirmisherCount++;
				}
				infCount++;
			}
        }
		if(infCount < 60)
        {
			return 0.01f;
		}
		float num = team.QuerySystem.RangedCavalryRatio * (float)team.QuerySystem.MemberCount;
		float skirmisherRatio = skirmisherCount / infCount;
		if(team.QuerySystem.InfantryRatio > 0.45f)
        {
			return team.QuerySystem.InfantryRatio * skirmisherRatio * 1.7f * (float)team.QuerySystem.MemberCount / ((float)team.QuerySystem.MemberCount - num) * (float)Math.Sqrt(team.QuerySystem.TotalPowerRatio);
        }
        else
        {
			return 0.01f;
        }
	}
}
