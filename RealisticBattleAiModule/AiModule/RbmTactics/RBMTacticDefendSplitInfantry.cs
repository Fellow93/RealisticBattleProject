using RBMAI;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

public class RBMTacticDefendSplitInfantry : TacticComponent
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
        IEnumerable<Formation> nonEmptyQuery = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0);
        foreach (Formation formation in nonEmptyQuery.ToList())
        {
            if (formation.QuerySystem.IsInfantryFormation)
            {
                infCount += formation.CountOfUnits;
            }
        }
        isDoubleFlank = true;
        //ManageFormationCounts(1, 1, 2, 1);
        ManageFormationCounts(3, 1, 2, 1);
        _mainInfantry = ChooseAndSortByPriority(nonEmptyQuery, (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        if (_mainInfantry != null && _mainInfantry.IsAIControlled)
        {
            _mainInfantry.AI.IsMainFormation = true;
            _mainInfantry.AI.Side = FormationAI.BehaviorSide.Middle;

            List<Agent> flankersList = new List<Agent>();
            List<Agent> mainList = new List<Agent>();

            int i = 0;
            foreach (Formation formation in nonEmptyQuery.ToList())
            {
                formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    if (formation.QuerySystem.IsInfantryFormation)
                    {
                        bool isFlanker = false;

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
                    }
                });
                if (isDoubleFlank && i != 0)
                {
                    if (leftflankersIndex != -1)
                    {
                        if (formation.QuerySystem.IsInfantryFormation)
                        {
                            rightflankersIndex = i;
                        }
                    }
                    else
                    {
                        if (formation.QuerySystem.IsInfantryFormation)
                        {
                            leftflankersIndex = i;
                        }
                    }
                }
                else if (i != 0)
                {
                    flankersIndex = i;
                }
                i++;
            }

            flankersList = flankersList.OrderBy(o => o.CharacterPowerCached).ToList();

            if ((flankersIndex < 0) || (flankersIndex > (FormationsIncludingEmpty.Count - 1)))
            {
                flankersIndex = 0;
            }
            if ((leftflankersIndex < 0) || (leftflankersIndex > (FormationsIncludingEmpty.Count - 1)))
            {
                leftflankersIndex = 0;
            }
            if ((rightflankersIndex < 0) || (rightflankersIndex > (FormationsIncludingEmpty.Count - 1)))
            {
                rightflankersIndex = 0;
            }
            List<Formation> nonEmptyCached = nonEmptyQuery.ToList();
            List<Formation> allFormations = FormationsIncludingSpecialAndEmpty.ToList();
            List<Formation> formationPool;
            if (nonEmptyCached.Count > 0)
            {
                formationPool = nonEmptyCached;
            }
            else
            {
                formationPool = allFormations;
            }
            if (formationPool.Count > 0)
            {
                int j = 0;
                foreach (Agent agent in flankersList.ToList())
                {
                    if (isDoubleFlank)
                    {
                        if (j < infCount / 6)
                        {
                            agent.Formation = formationPool[leftflankersIndex];
                        }
                        else if (j < infCount / 3)
                        {
                            agent.Formation = formationPool[rightflankersIndex];
                        }
                        else
                        {
                            agent.Formation = formationPool[0];
                        }
                    }
                    else
                    {
                        if (j < infCount / 3)
                        {
                            agent.Formation = formationPool[flankersIndex];
                        }
                        else
                        {
                            agent.Formation = formationPool[0];
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
                            agent.Formation = formationPool[leftflankersIndex];
                        }
                        else if (j < infCount / 3)
                        {
                            agent.Formation = formationPool[rightflankersIndex];
                        }
                        else
                        {
                            agent.Formation = formationPool[0];
                        }
                    }
                    else
                    {
                        if (j < infCount / 3)
                        {
                            agent.Formation = formationPool[flankersIndex];
                        }
                        else
                        {
                            agent.Formation = formationPool[0];
                        }
                    }
                    j++;
                }
            }
        }
        _archers = ChooseAndSortByPriority(nonEmptyQuery, (Formation f) => f.QuerySystem.IsRangedFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        List<Formation> list = ChooseAndSortByPriority(nonEmptyQuery, (Formation f) => f.QuerySystem.IsCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower);
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
        _rangedCavalry = ChooseAndSortByPriority(nonEmptyQuery, (Formation f) => f.QuerySystem.IsRangedCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();

        List<Formation> nonEmptyPostLoop = nonEmptyQuery.ToList();
        if (isDoubleFlank)
        {
            if (leftflankersIndex != -1 && nonEmptyPostLoop.Count > leftflankersIndex && nonEmptyPostLoop[leftflankersIndex].QuerySystem.IsInfantryFormation)
            {
                _leftFlankingInfantry = nonEmptyPostLoop[leftflankersIndex];
                _leftFlankingInfantry.AI.IsMainFormation = false;
            }
            if (rightflankersIndex != -1 && nonEmptyPostLoop.Count > rightflankersIndex && nonEmptyPostLoop[rightflankersIndex].QuerySystem.IsInfantryFormation)
            {
                _rightFlankingInfantry = nonEmptyPostLoop[rightflankersIndex];
                _rightFlankingInfantry.AI.IsMainFormation = false;
            }
        }
        else
        {
            if (flankersIndex != -1 && nonEmptyPostLoop.Count > flankersIndex && nonEmptyPostLoop[flankersIndex].QuerySystem.IsInfantryFormation)
            {
                _flankingInfantry = nonEmptyPostLoop[flankersIndex];
                _flankingInfantry.AI.IsMainFormation = false;
            }
        }

        foreach (Formation formation in nonEmptyPostLoop)
        {
            if (formation.CountOfUnits == 1)
            {
                formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    if (!agent.IsRangedCached && !agent.HasMount && _mainInfantry != null)
                    {
                        agent.Formation = _mainInfantry;
                    }
                });
            }
        }

        IsTacticReapplyNeeded = true;
    }

    private bool _hasBattleBeenJoined;

    public RBMTacticDefendSplitInfantry(Team team)
        : base(team)
    {
        _hasBattleBeenJoined = false;
    }

    protected override void ManageFormationCounts()
    {
        AssignTacticFormations();
    }

    private void Defend()
    {
        if (Team.IsPlayerTeam && !Team.IsPlayerGeneral && Team.IsPlayerSergeant)
        {
            SoundTacticalHorn(TacticComponent.MoveHornSoundIndex);
        }
        if (_flankingInfantry != null)
        {
            _flankingInfantry.AI.ResetBehaviorWeights();
            if (side == 0)
            {
                _flankingInfantry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;
                _flankingInfantry.AI.Side = FormationAI.BehaviorSide.Left;
            }
            else
            {
                _flankingInfantry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
                _flankingInfantry.AI.Side = FormationAI.BehaviorSide.Right;
            }
        }
        if (_leftFlankingInfantry != null)
        {
            _leftFlankingInfantry.AI.ResetBehaviorWeights();
            _leftFlankingInfantry.AI.SetBehaviorWeight<BehaviorProtectFlank>(5f).FlankSide = FormationAI.BehaviorSide.Left;
            _leftFlankingInfantry.AI.Side = FormationAI.BehaviorSide.Left;
        }
        if (_rightFlankingInfantry != null)
        {
            _rightFlankingInfantry.AI.ResetBehaviorWeights();
            _rightFlankingInfantry.AI.SetBehaviorWeight<BehaviorProtectFlank>(5f).FlankSide = FormationAI.BehaviorSide.Right;
            _rightFlankingInfantry.AI.Side = FormationAI.BehaviorSide.Right;
        }
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.ResetBehaviorWeights();
            _mainInfantry.AI.SetBehaviorWeight<BehaviorHoldHighGround>(1f);
            _mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
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
            //_leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        if (_rightCavalry != null)
        {
            _rightCavalry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_rightCavalry);
            _rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
            //_rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        if (_rangedCavalry != null)
        {
            _rangedCavalry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_rangedCavalry);
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
    }

    private void Engage()
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
            //SetDefaultBehaviorWeights(_leftCavalry);
            _leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
            _leftCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
        }
        if (_rightCavalry != null)
        {
            _rightCavalry.AI.ResetBehaviorWeights();
            //SetDefaultBehaviorWeights(_rightCavalry);
            _rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
            _rightCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
        }
        if (_rangedCavalry != null)
        {
            _rangedCavalry.AI.ResetBehaviorWeights();
            //TacticComponent.SetDefaultBehaviorWeights(_rangedCavalry);
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
    }

    private bool HasBattleBeenJoined()
    {
        return RBMAI.Utilities.HasBattleBeenJoined(_mainInfantry, _hasBattleBeenJoined);
    }

    protected override bool CheckAndSetAvailableFormationsChanged()
    {
        int num = base.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).Count((Formation f) => f.IsAIControlled);
        bool hasChanged = num != _AIControlledFormationCount;
        if (hasChanged)
        {
            _AIControlledFormationCount = num;
            IsTacticReapplyNeeded = true;
        }
        if (!hasChanged)
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

    public override void TickOccasionally()
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
                Engage();
            }
            else
            {
                Defend();
            }
            IsTacticReapplyNeeded = false;
        }
        bool flag = HasBattleBeenJoined();
        if (flag != _hasBattleBeenJoined || IsTacticReapplyNeeded)
        {
            _hasBattleBeenJoined = flag;
            if (_hasBattleBeenJoined)
            {
                Engage();
            }
            else
            {
                Defend();
            }
            IsTacticReapplyNeeded = false;
        }
        base.TickOccasionally();
    }

    protected override float GetTacticWeight()
    {
        if (Mission.Current != null && !Mission.Current.IsTeleportingAgents && Team.TeamAI.IsCurrentTactic(this) && Team.QuerySystem.InfantryRatio > 0.3f)
        {
            return 10f;
        }
        if (Team.QuerySystem.InfantryRatio > 0.5f)
        {
            return 5f;
        }
        else
        {
            return 0.2f;
        }
    }
}