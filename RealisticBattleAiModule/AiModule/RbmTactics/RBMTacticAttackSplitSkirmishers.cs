using RBMAI;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

public class RBMTacticAttackSplitSkirmishers : TacticComponent
{
    protected Formation _skirmishers;
    private int side = MBRandom.RandomInt(2);
    private int waitCountMainFormation = 0;
    private int waitCountMainFormationMax = 25;

    protected void AssignTacticFormations()
    {
        ManageFormationCounts(2, 1, 2, 1);

        // Materialize immediately — lazy re-evaluation after agent moves gives inconsistent results
        List<Formation> nonEmptyFormations = FormationsIncludingEmpty
            .Where((Formation f) => f.CountOfUnits > 0).ToList();

        _mainInfantry = ChooseAndSortByPriority(nonEmptyFormations, (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        _skirmishers = null;

        if (_mainInfantry != null)
        {
            _mainInfantry.AI.IsMainFormation = true;
            _mainInfantry.AI.Side = FormationAI.BehaviorSide.Middle;

            // ManageFormationCounts(2,...) reserves two infantry slots (indices 0 and 1).
            // The second slot is often empty (all units share FormationClass.Infantry and land in slot 0).
            // We must search FormationsIncludingEmpty — not just non-empty formations — to find it.
            Formation skirmisherSlot = FormationsIncludingEmpty
                .Take(2)   // indices 0 and 1 are the two infantry slots
                .FirstOrDefault((Formation f) => f != _mainInfantry && f.IsAIControlled);

            if (skirmisherSlot != null)
            {
                // Collect agents from the two known infantry slots directly.
                // Avoid IsInfantryFormation check — QuerySystem can be stale right after
                // ManageFormationCounts, causing the main slot to be skipped and giving a
                // near-empty allInfantry list (which shrinks skirmTarget to 1–2).
                List<Agent> allInfantry = new List<Agent>();
                _mainInfantry.ApplyActionOnEachUnitViaBackupList((Agent a) => allInfantry.Add(a));
                if (skirmisherSlot.CountOfUnits > 0)
                    skirmisherSlot.ApplyActionOnEachUnitViaBackupList((Agent a) => allInfantry.Add(a));

                // Separate actual skirmishers (javelin users) from melee
                List<Agent> skirmishersList = new List<Agent>();
                List<Agent> meleeList = new List<Agent>();
                foreach (Agent agent in allInfantry)
                {
                    if (RBMAI.Utilities.CheckIfSkirmisherAgent(agent))
                        skirmishersList.Add(agent);
                    else
                        meleeList.Add(agent);
                }

                // Sort weakest first — skirmisher slot gets actual skirmishers,
                // padded with the weakest melee up to 25 % of total infantry.
                skirmishersList = skirmishersList.OrderBy((Agent o) => o.CharacterPowerCached).ToList();
                meleeList = meleeList.OrderBy((Agent o) => o.CharacterPowerCached).ToList();

                int totalCount = allInfantry.Count;
                int skirmTarget = Math.Max(1, totalCount / 4);

                int assigned = 0;
                foreach (Agent agent in skirmishersList)
                {
                    if (assigned < skirmTarget)
                    {
                        agent.Formation = skirmisherSlot;
                        assigned++;
                    }
                    else
                    {
                        agent.Formation = _mainInfantry;
                    }
                }
                int fillCount = Math.Max(0, skirmTarget - assigned);
                for (int j = 0; j < meleeList.Count; j++)
                {
                    meleeList[j].Formation = (j < fillCount) ? skirmisherSlot : _mainInfantry;
                }

                _skirmishers = skirmisherSlot;
                _skirmishers.AI.IsMainFormation = false;

                this.Team.TriggerOnFormationsChanged(skirmisherSlot);
                this.Team.TriggerOnFormationsChanged(_mainInfantry);
            }
        }

        // Re-query after agent moves so cavalry/archer picks reflect current state
        nonEmptyFormations = FormationsIncludingEmpty
            .Where((Formation f) => f.CountOfUnits > 0).ToList();

        _archers = ChooseAndSortByPriority(nonEmptyFormations, (Formation f) => f.QuerySystem.IsRangedFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        List<Formation> list = ChooseAndSortByPriority(nonEmptyFormations, (Formation f) => f.QuerySystem.IsCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower);
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
        _rangedCavalry = ChooseAndSortByPriority(nonEmptyFormations, (Formation f) => f.QuerySystem.IsRangedCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();

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
        if (Team.IsPlayerTeam && !Team.IsPlayerGeneral && Team.IsPlayerSergeant)
        {
            SoundTacticalHorn(TacticComponent.MoveHornSoundIndex);
        }
        if (_skirmishers != null)
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
            if (waitCountMainFormation < waitCountMainFormationMax)
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
            //TacticComponent.SetDefaultBehaviorWeights(_leftCavalry);
            _leftCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;
            _leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        if (_rightCavalry != null)
        {
            _rightCavalry.AI.ResetBehaviorWeights();
            //TacticComponent.SetDefaultBehaviorWeights(_rightCavalry);
            _rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
            _rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        if (_rangedCavalry != null)
        {
            _rangedCavalry.AI.ResetBehaviorWeights();
            //TacticComponent.SetDefaultBehaviorWeights(_rangedCavalry);
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
        int num = base.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).Count((Formation f) => f.IsAIControlled);
        bool hasChanged = num != _AIControlledFormationCount;
        if (hasChanged)
        {
            _AIControlledFormationCount = num;
            IsTacticReapplyNeeded = true;
        }
        if (!hasChanged)
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
        float allyInfantryPower = 0f;
        float enemyInfantryPower = 0f;
        int allyInfCount = 0;

        // Single pass over all teams — avoids two separate ToList() allocations
        foreach (Team team in Mission.Current.Teams)
        {
            bool isEnemy = team.IsEnemyOf(base.Team);
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation.CountOfUnits <= 0 || !formation.QuerySystem.IsInfantryFormation)
                    continue;

                if (isEnemy)
                {
                    enemyInfantryPower += formation.QuerySystem.FormationPower;
                }
                else
                {
                    allyInfantryPower += formation.QuerySystem.FormationPower;
                    allyInfCount += formation.CountOfUnits;
                }
            }
        }

        if (allyInfantryPower < enemyInfantryPower * 1.25f || allyInfCount < 60)
        {
            return 0.01f;
        }

        // Count skirmishers by iterating infantry formations only — avoids allocating
        // Team.ActiveAgents.ToList() (large copy) and checking IsInfantryFormation per agent.
        foreach (Formation formation in base.Team.FormationsIncludingEmpty)
        {
            if (formation.CountOfUnits > 0 && formation.QuerySystem.IsInfantryFormation)
            {
                formation.ApplyActionOnEachUnitViaBackupList((Agent agent) =>
                {
                    if (RBMAI.Utilities.CheckIfSkirmisherAgent(agent, 2))
                        skirmisherCount++;
                });
            }
        }

        float num = Team.QuerySystem.RangedCavalryRatio * (float)Team.QuerySystem.MemberCount;
        float skirmisherRatio = skirmisherCount / allyInfCount;
        if (Team.QuerySystem.InfantryRatio > 0.45f)
        {
            return Team.QuerySystem.InfantryRatio * skirmisherRatio * 1.7f * (float)Team.QuerySystem.MemberCount / ((float)Team.QuerySystem.MemberCount - num) * (float)Math.Sqrt(Team.QuerySystem.TotalPowerRatio);
        }
        else
        {
            return 0.01f;
        }
    }
}