using RBMAI;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

public class RBMTacticDefendSplitInfantry : TacticComponent
{
    protected Formation _flankingInfantry = null;
    protected Formation _leftFlankingInfantry = null;
    protected Formation _rightFlankingInfantry = null;
    private bool _membershipSplitDone;
    private int side = MBRandom.RandomInt(2);
    // The flank plan the last split read (null before the terrain publish); a re-apply after the publish re-splits.
    private TerrainFlankPlan.SidePlan _splitPlan;

    protected void AssignTacticFormations()
    {
        _splitPlan = TerrainFlankPlan.ForTeam(Team);

        // Under BattleMiniMap (IsFormationReshufflingUnsafe) the agent moves happen once, never again.
        bool doReassign = !RBMAI.Tactics.IsFormationReshufflingUnsafe || !_membershipSplitDone;

        ManageFormationCounts(3, 1, 2, 1);

        List<Formation> nonEmptyFormations = FormationsIncludingEmpty
            .Where((Formation f) => f.CountOfUnits > 0).ToList();

        _mainInfantry = ChooseAndSortByPriority(nonEmptyFormations, (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        Formation previousLeft = _leftFlankingInfantry;
        Formation previousRight = _rightFlankingInfantry;
        _flankingInfantry = null;
        _leftFlankingInfantry = null;
        _rightFlankingInfantry = null;

        if (_mainInfantry != null)
        {
            _mainInfantry.AI.IsMainFormation = true;
            _mainInfantry.AI.Side = FormationAI.BehaviorSide.Middle;

            List<Formation> flankingSlots = FormationsIncludingEmpty
            .Where((Formation f) => f != _mainInfantry && f.IsAIControlled && f.QuerySystem.IsInfantryFormation)
            .ToList();

            // ChooseAndSortByPriority only prefers AI formations: never reshuffle player-commanded men.
            if (_mainInfantry.IsAIControlled && flankingSlots.Count >= 2)
            {
                Formation leftSlot;
                Formation rightSlot;
                RBMTacticAttackSplitInfantry.PickFlankSlots(flankingSlots, previousLeft, previousRight, out leftSlot, out rightSlot);

                // Read the three infantry slots directly: QuerySystem can be stale right after ManageFormationCounts.
                List<Agent> allInfantry = new List<Agent>();
                _mainInfantry.ApplyActionOnEachUnitViaBackupList((Agent a) => allInfantry.Add(a));
                if (leftSlot.CountOfUnits > 0)
                    leftSlot.ApplyActionOnEachUnitViaBackupList((Agent a) => allInfantry.Add(a));
                if (rightSlot.CountOfUnits > 0)
                    rightSlot.ApplyActionOnEachUnitViaBackupList((Agent a) => allInfantry.Add(a));
                List<Formation> strayFormations = RBMTacticAttackSplitInfantry.CollectStrayInfantry(flankingSlots, leftSlot, rightSlot, allInfantry);
                int infCount = allInfantry.Count;

                List<Agent> flankersList = new List<Agent>();
                List<Agent> mainList = new List<Agent>();
                foreach (Agent agent in allInfantry)
                {
                    WeaponClass wc = agent.WieldedWeapon.CurrentUsageItem?.WeaponClass ?? WeaponClass.Undefined;
                    if (wc == WeaponClass.TwoHandedAxe || wc == WeaponClass.TwoHandedPolearm)
                        flankersList.Add(agent);
                    else
                        mainList.Add(agent);
                }
                flankersList = flankersList.OrderBy((Agent o) => o.CharacterPowerCached).ToList();

                if (doReassign)
                {
                    // No guards under MinInfantryToSplit or on a flank TerrainFlankPlan covers.
                    bool split = RBMTacticAttackSplitInfantry.ShouldSplit(infCount);
                    int leftEnd = (!split || TerrainFlankPlan.GuardSkipped(this, FormationAI.BehaviorSide.Left, true)) ? 0 : infCount / 6;
                    int rightEnd = leftEnd + ((!split || TerrainFlankPlan.GuardSkipped(this, FormationAI.BehaviorSide.Right, true)) ? 0 : infCount / 3 - infCount / 6);
                    int j = 0;
                    foreach (Agent agent in flankersList)
                    {
                        if (j < leftEnd)
                            agent.Formation = leftSlot;
                        else if (j < rightEnd)
                            agent.Formation = rightSlot;
                        else
                            agent.Formation = _mainInfantry;
                        j++;
                    }
                    foreach (Agent agent in mainList)
                    {
                        if (j < leftEnd)
                            agent.Formation = leftSlot;
                        else if (j < rightEnd)
                            agent.Formation = rightSlot;
                        else
                            agent.Formation = _mainInfantry;
                        j++;
                    }
                }
                if (RBMAI.Tactics.IsFormationReshufflingUnsafe)
                {
                    _membershipSplitDone = true;
                }

                if (leftSlot.CountOfUnits > 0)
                {
                    _leftFlankingInfantry = leftSlot;
                    _leftFlankingInfantry.AI.IsMainFormation = false;
                }
                if (rightSlot.CountOfUnits > 0)
                {
                    _rightFlankingInfantry = rightSlot;
                    _rightFlankingInfantry.AI.IsMainFormation = false;
                }

                if (doReassign)
                {
                    this.Team.TriggerOnFormationsChanged(leftSlot);
                    this.Team.TriggerOnFormationsChanged(rightSlot);
                    this.Team.TriggerOnFormationsChanged(_mainInfantry);
                    foreach (Formation stray in strayFormations)
                        this.Team.TriggerOnFormationsChanged(stray);
                }
            }
        }

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

        if (doReassign)
        {
            foreach (Formation formation in nonEmptyFormations)
            {
                if (formation.CountOfUnits == 1 && formation.IsAIControlled)
                {
                    formation.ApplyActionOnEachUnitViaBackupList((Agent agent) =>
                    {
                        if (!agent.IsRangedCached && !agent.HasMount && _mainInfantry != null && _mainInfantry.IsAIControlled)
                            agent.Formation = _mainInfantry;
                    });
                }
            }
        }
        // A flank the merge just emptied is dropped, else the empty flank re-splits the infantry every tick.
        if (_leftFlankingInfantry != null && _leftFlankingInfantry.CountOfUnits == 0)
        {
            _leftFlankingInfantry = null;
        }
        if (_rightFlankingInfantry != null && _rightFlankingInfantry.CountOfUnits == 0)
        {
            _rightFlankingInfantry = null;
        }

        IsTacticReapplyNeeded = true;
    }

    private bool _hasBattleBeenJoined;

    /// <summary>Bumped on every OnApply: the battle position's hold (RBMBehaviorHoldPosition) starts again on a new application.</summary>
    private int _applyGeneration;

    public RBMTacticDefendSplitInfantry(Team team)
        : base(team)
    {
        _hasBattleBeenJoined = false;
    }

    protected override void OnApply()
    {
        base.OnApply();
        _applyGeneration++;
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
            // A guard left on a covered flank holds with the main line.
            if (TerrainFlankPlan.GuardSkipped(this, FormationAI.BehaviorSide.Left, false))
            {
                _leftFlankingInfantry.AI.SetBehaviorWeight<BehaviorHoldHighGround>(1f);
                _leftFlankingInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
            }
            else
            {
                _leftFlankingInfantry.AI.SetBehaviorWeight<BehaviorProtectFlank>(5f).FlankSide = FormationAI.BehaviorSide.Left;
            }
            _leftFlankingInfantry.AI.Side = FormationAI.BehaviorSide.Left;
        }
        if (_rightFlankingInfantry != null)
        {
            _rightFlankingInfantry.AI.ResetBehaviorWeights();
            if (TerrainFlankPlan.GuardSkipped(this, FormationAI.BehaviorSide.Right, false))
            {
                _rightFlankingInfantry.AI.SetBehaviorWeight<BehaviorHoldHighGround>(1f);
                _rightFlankingInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
            }
            else
            {
                _rightFlankingInfantry.AI.SetBehaviorWeight<BehaviorProtectFlank>(5f).FlankSide = FormationAI.BehaviorSide.Right;
            }
            _rightFlankingInfantry.AI.Side = FormationAI.BehaviorSide.Right;
        }
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.ResetBehaviorWeights();
            _mainInfantry.AI.SetBehaviorWeight<BehaviorHoldHighGround>(1f);
            _mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
            // The defender's battle position (needs a published plan; outweighs both, they are its fallback); no plan sets nothing.
            RBMBehaviorHoldPosition.TryAssign(_mainInfantry, this, _applyGeneration, RBMBehaviorHoldPosition.Role.Line);
        }
        if (_archers != null)
        {
            _archers.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_archers);
            _archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
            _archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
            _archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
            _archers.AI.SetBehaviorWeight<BehaviorRegroup>(1.25f);
            RBMBehaviorHoldPosition.TryAssign(_archers, this, _applyGeneration, RBMBehaviorHoldPosition.Role.Archers);
        }
        if (_leftCavalry != null)
        {
            _leftCavalry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_leftCavalry);
            _leftCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = TerrainFlankPlan.CavalrySide(this, _leftCavalry, FormationAI.BehaviorSide.Left, _rightCavalry == null);
            //_leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        if (_rightCavalry != null)
        {
            _rightCavalry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_rightCavalry);
            _rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = TerrainFlankPlan.CavalrySide(this, _rightCavalry, FormationAI.BehaviorSide.Right, false);
            //_rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        // Both line ends covered: the cavalry waits in reserve behind it.
        if (TerrainBattlePosition.ReserveWanted(Team))
        {
            RBMBehaviorHoldPosition.TryAssign(_leftCavalry, this, _applyGeneration, RBMBehaviorHoldPosition.Role.Reserve);
            RBMBehaviorHoldPosition.TryAssign(_rightCavalry, this, _applyGeneration, RBMBehaviorHoldPosition.Role.Reserve);
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
        // While the battle position's line holds, only its own latched engage; otherwise the usual rule.
        return RBMBehaviorHoldPosition.BattleJoinedFor(_mainInfantry, this, _hasBattleBeenJoined);
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
        // A re-apply after a changed flank plan re-splits so a guard on a covered flank rejoins the main line.
        if (CheckAndSetAvailableFormationsChanged() || (IsTacticReapplyNeeded && TerrainFlankPlan.ForTeam(Team) != _splitPlan))
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
        // Re-apply when the battle position appears or moves.
        if (TerrainBattlePosition.NeedsReapply(this))
        {
            IsTacticReapplyNeeded = true;
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

    // Graded on the native scale by how far our infantry outnumbers the enemy's (grows as our side weakens); TacticSelection applies culture/commander/jitter on top.
    protected override float GetTacticWeight()
    {
        float t;
        if (!TacticSelection.TryGetInfantrySuperiority(Team, out t))
        {
            return TacticSelection.UnfitWeight;
        }
        return MBMath.Lerp(0.6f, 1.3f, t) / MathF.Sqrt(MathF.Max(Team.QuerySystem.RemainingPowerRatio, 0.05f));
    }
}