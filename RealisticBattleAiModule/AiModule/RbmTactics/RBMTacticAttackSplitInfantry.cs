using RBMAI;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

public class RBMTacticAttackSplitInfantry : TacticComponent
{
    protected Formation _flankingInfantry = null;
    protected Formation _leftFlankingInfantry = null;
    protected Formation _rightFlankingInfantry = null;
    private int side = MBRandom.RandomInt(2);

    protected void AssignTacticFormations()
    {
        int flankersIndex = -1;
        int leftflankersIndex = -1;
        int rightflankersIndex = -1;
        bool isDoubleFlank = false;
        int infCount = 0;
        foreach (Formation formation in FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
        {
            if (formation.QuerySystem.IsInfantryFormation)
            {
                infCount += formation.CountOfUnits;
            }
        }
        isDoubleFlank = true;
        ManageFormationCounts(3, 1, 2, 1);
        if (FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).Count() > 0 && FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList().FirstOrDefault().QuerySystem.IsInfantryFormation)
        {
            _mainInfantry = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList().FirstOrDefault();
        }
        else
        {
            _mainInfantry = ChooseAndSortByPriority(FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        }
        if (_mainInfantry != null && _mainInfantry.IsAIControlled)
        {
            _mainInfantry.AI.IsMainFormation = true;
            _mainInfantry.AI.Side = FormationAI.BehaviorSide.Middle;

            List<Agent> flankersList = new List<Agent>();
            List<Agent> mainList = new List<Agent>();

            //_mainInfantry.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
            //{
            //	bool isFlanker = false;
            //             //for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            //             //{
            //             //	if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
            //             //	{
            //             //		if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown)
            //             //		{
            //             //			isSkirmisher = true;
            //             //			break;
            //             //		}
            //             //	}
            //             //}

            //             if (!agent.HasShieldCached)
            //             {
            //		isFlanker = true;
            //	}

            //	if (isFlanker)
            //	{
            //		flankersList.Add(agent);
            //	}
            //	else
            //	{
            //		mainList.Add(agent);
            //	}
            //});

            int i = 0;
            foreach (Formation formation in FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
            {
                formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    if (formation.QuerySystem.IsInfantryFormation && formation.IsAIControlled)
                    {
                        bool isFlanker = false;
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

                        if (agent.WieldedWeapon.CurrentUsageItem?.WeaponClass == WeaponClass.TwoHandedAxe || agent.WieldedWeapon.CurrentUsageItem?.WeaponClass == WeaponClass.TwoHandedPolearm)
                        {
                            isFlanker = true;
                        }

                        if (isFlanker)
                        {
                            flankersList.Add(agent);
                        }
                        else
                        {
                            mainList.Add(agent);
                        }

                        if (isDoubleFlank && i != 0)
                        {
                            if (leftflankersIndex != -1)
                            {
                                rightflankersIndex = i;
                            }
                            else
                            {
                                leftflankersIndex = i;
                            }
                        }
                        else if (i != 0)
                        {
                            flankersIndex = i;
                        }
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

            flankersList = flankersList.OrderBy(o => o.CharacterPowerCached).ToList();

            int j = 0;
            //int infCount = Formations.ToList()[0].CountOfUnits + Formations.ToList()[flankersIndex].CountOfUnits;
            //if(Formations.ToList()[flankersIndex].CountOfUnits > infCount / 4)
            //         {
            //	int countToTransfer = Formations.ToList()[flankersIndex].CountOfUnits - infCount / 4;
            //	Formations.ToList()[flankersIndex].TransferUnits(Formations.ToList()[0], countToTransfer);
            //}else if (Formations.ToList()[flankersIndex].CountOfUnits < infCount / 4)
            //         {
            //	int countToTransfer = infCount / 4 - Formations.ToList()[flankersIndex].CountOfUnits;
            //	Formations.ToList()[0].TransferUnits(Formations.ToList()[flankersIndex], countToTransfer);
            //}
            if (leftflankersIndex > 0 && rightflankersIndex > 0)
            {
                foreach (Agent agent in flankersList.ToList())
                {
                    if (isDoubleFlank)
                    {
                        if (j < infCount / 6)
                        {
                            //agent.Formation.TransferUnits(Formations.ToList()[flankersIndex], 1);
                            agent.Formation = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[leftflankersIndex];
                        }
                        else if (j < infCount / 3)
                        {
                            //agent.Formation.TransferUnits(Formations.ToList()[0], 1);
                            agent.Formation = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[rightflankersIndex];
                        }
                        else
                        {
                            //agent.Formation.TransferUnits(Formations.ToList()[0], 1);
                            agent.Formation = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[0];
                        }
                    }
                    else
                    {
                        if (j < infCount / 3)
                        {
                            //agent.Formation.TransferUnits(Formations.ToList()[flankersIndex], 1);
                            agent.Formation = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[flankersIndex];
                        }
                        else
                        {
                            //agent.Formation.TransferUnits(Formations.ToList()[0], 1);
                            agent.Formation = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[0];
                        }
                    }

                    j++;
                }
                foreach (Agent agent in mainList.ToList())
                {
                    if (isDoubleFlank)
                    {
                        if (j < infCount / 6)
                        {
                            //agent.Formation.TransferUnits(Formations.ToList()[flankersIndex], 1);
                            agent.Formation = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[leftflankersIndex];
                        }
                        else if (j < infCount / 3)
                        {
                            //agent.Formation.TransferUnits(Formations.ToList()[0], 1);
                            agent.Formation = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[rightflankersIndex];
                        }
                        else
                        {
                            //agent.Formation.TransferUnits(Formations.ToList()[0], 1);
                            agent.Formation = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[0];
                        }
                    }
                    else
                    {
                        if (j < infCount / 3)
                        {
                            //agent.Formation.TransferUnits(Formations.ToList()[flankersIndex], 1);
                            agent.Formation = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[flankersIndex];
                        }
                        else
                        {
                            //agent.Formation.TransferUnits(Formations.ToList()[0], 1);
                            agent.Formation = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[0];
                        }
                    }
                    j++;
                }
            }
        }
        _archers = ChooseAndSortByPriority(FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsRangedFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        List<Formation> list = ChooseAndSortByPriority(FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower);
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
        _rangedCavalry = ChooseAndSortByPriority(FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsRangedCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();

        if (isDoubleFlank)
        {
            if (leftflankersIndex != -1 && FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).Count() > leftflankersIndex && FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[leftflankersIndex].QuerySystem.IsInfantryFormation)
            {
                _leftFlankingInfantry = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[leftflankersIndex];
                _leftFlankingInfantry.AI.IsMainFormation = false;
            }
            if (rightflankersIndex != -1 && FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).Count() > rightflankersIndex && FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[rightflankersIndex].QuerySystem.IsInfantryFormation)
            {
                _rightFlankingInfantry = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[rightflankersIndex];
                _rightFlankingInfantry.AI.IsMainFormation = false;
            }
        }
        else
        {
            if (flankersIndex != -1 && FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).Count() > flankersIndex && FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[flankersIndex].QuerySystem.IsInfantryFormation)
            {
                _flankingInfantry = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[flankersIndex];
                _flankingInfantry.AI.IsMainFormation = false;
            }
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

    public RBMTacticAttackSplitInfantry(Team team)
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
        if (Team.IsPlayerTeam && !Team.IsPlayerGeneral && Team.IsPlayerSergeant)
        {
            SoundTacticalHorn(TacticComponent.MoveHornSoundIndex);
        }
        if (_flankingInfantry != null)
        {
            _flankingInfantry.AI.ResetBehaviorWeights();
            //TacticComponent.SetDefaultBehaviorWeights(_flankingInfantry);
            //_skirmishers.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
            if (side == 0)
            {
                _flankingInfantry.AI.SetBehaviorWeight<RBMBehaviorInfantryAttackFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;
                _flankingInfantry.AI.Side = FormationAI.BehaviorSide.Left;
            }
            else
            {
                _flankingInfantry.AI.SetBehaviorWeight<RBMBehaviorInfantryAttackFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
                _flankingInfantry.AI.Side = FormationAI.BehaviorSide.Right;
            }
        }
        if (_leftFlankingInfantry != null)
        {
            _leftFlankingInfantry.AI.ResetBehaviorWeights();
            //TacticComponent.SetDefaultBehaviorWeights(_flankingInfantry);
            //_skirmishers.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
            //_leftFlankingInfantry.AI.SetBehaviorWeight<RBMBehaviorInfantryAttackFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;
            _leftFlankingInfantry.AI.SetBehaviorWeight<BehaviorProtectFlank>(5f).FlankSide = FormationAI.BehaviorSide.Left;
            _leftFlankingInfantry.AI.Side = FormationAI.BehaviorSide.Left;
        }
        if (_rightFlankingInfantry != null)
        {
            _rightFlankingInfantry.AI.ResetBehaviorWeights();
            //TacticComponent.SetDefaultBehaviorWeights(_flankingInfantry);
            //_skirmishers.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
            //_rightFlankingInfantry.AI.SetBehaviorWeight<RBMBehaviorInfantryAttackFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
            _rightFlankingInfantry.AI.SetBehaviorWeight<BehaviorProtectFlank>(5f).FlankSide = FormationAI.BehaviorSide.Right;
            _rightFlankingInfantry.AI.Side = FormationAI.BehaviorSide.Right;
        }
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.ResetBehaviorWeights();
            //TacticComponent.SetDefaultBehaviorWeights(_mainInfantry);
            _mainInfantry.AI.SetBehaviorWeight<BehaviorAdvance>(1f);
            _mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.5f);
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
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
    }

    private void Attack()
    {
        if (Team.IsPlayerTeam && !Team.IsPlayerGeneral && Team.IsPlayerSergeant)
        {
            SoundTacticalHorn(TacticComponent.AttackHornSoundIndex);
        }
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.ResetBehaviorWeights();
            _mainInfantry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
        }
        if (_leftFlankingInfantry != null)
        {
            _leftFlankingInfantry.AI.ResetBehaviorWeights();
            _leftFlankingInfantry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
            _leftFlankingInfantry.AI.Side = FormationAI.BehaviorSide.Left;
        }
        if (_rightFlankingInfantry != null)
        {
            _rightFlankingInfantry.AI.ResetBehaviorWeights();
            _rightFlankingInfantry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
            _rightFlankingInfantry.AI.Side = FormationAI.BehaviorSide.Right;
        }
        if (_flankingInfantry != null)
        {
            _flankingInfantry.AI.ResetBehaviorWeights();
            _flankingInfantry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
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
    }

    private bool HasBattleBeenJoined()
    {
        return RBMAI.Utilities.HasBattleBeenJoined(_mainInfantry, _hasBattleBeenJoined, 70f);
    }

    protected override bool CheckAndSetAvailableFormationsChanged()
    {
        int num = base.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).Count((Formation f) => f.IsAIControlled);
        bool num2 = num != _AIControlledFormationCount;
        if (num2)
        {
            _AIControlledFormationCount = num;
            IsTacticReapplyNeeded = true;
        }
        if (!num2)
        {
            if ((_mainInfantry == null || (_mainInfantry.CountOfUnits != 0 && _mainInfantry.QuerySystem.IsInfantryFormation)) &&
                (_leftFlankingInfantry == null || (_leftFlankingInfantry.CountOfUnits != 0 && _leftFlankingInfantry.QuerySystem.IsInfantryFormation)) &&
                (_rightFlankingInfantry == null || (_rightFlankingInfantry.CountOfUnits != 0 && _rightFlankingInfantry.QuerySystem.IsInfantryFormation)) &&
                (_archers == null || (_archers.CountOfUnits != 0 && _archers.QuerySystem.IsRangedFormation)) &&
                (_leftCavalry == null || (_leftCavalry.CountOfUnits != 0 && _leftCavalry.QuerySystem.IsCavalryFormation)) &&
                (_rightCavalry == null || (_rightCavalry.CountOfUnits != 0 && _rightCavalry.QuerySystem.IsCavalryFormation)))
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
            IsTacticReapplyNeeded = false;
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
            IsTacticReapplyNeeded = false;
        }
        base.TickOccasionally();
    }

    protected override float GetTacticWeight()
    {
        float allyInfatryPower = 0f;
        float allyCavalryPower = 0f;
        float enemyInfatryPower = 0f;
        float enemyArcherPower = 0f;

        int allyInfCount = 0;

        foreach (Team team in Mission.Current.Teams.ToList())
        {
            if (Team.IsEnemyOf(base.Team))
            {
                foreach (Formation formation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                {
                    if (formation.QuerySystem.IsInfantryFormation)
                    {
                        enemyInfatryPower += formation.QuerySystem.FormationPower;
                    }
                    if (formation.QuerySystem.IsRangedFormation)
                    {
                        enemyArcherPower += formation.QuerySystem.FormationPower;
                    }
                }
            }
        }

        foreach (Team team in Mission.Current.Teams.ToList())
        {
            if (!Team.IsEnemyOf(base.Team))
            {
                foreach (Formation formation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                {
                    if (formation.QuerySystem.IsInfantryFormation)
                    {
                        allyInfatryPower += formation.QuerySystem.FormationPower;
                        allyInfCount += formation.CountOfUnits;
                    }
                    if (formation.QuerySystem.IsCavalryFormation)
                    {
                        allyCavalryPower += formation.QuerySystem.FormationPower;
                    }
                }
            }
        }

        if (allyInfatryPower > enemyInfatryPower * 1.25f && allyInfCount > 60)
        {
            return 10f;
        }
        else
        {
            return 0.01f;
        }
        //return allyInfatryPower / ((enemyArcherPower * 0.5f) + enemyInfatryPower / (allyCavalryPower * 0.5f));
    }
}