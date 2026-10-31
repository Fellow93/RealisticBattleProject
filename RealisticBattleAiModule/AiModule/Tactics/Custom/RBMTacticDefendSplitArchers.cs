using RBMAI;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

public class RBMTacticDefendSplitArchers : TacticComponent
{
    private Formation leftArchers;
    private Formation rightArchers;
    private bool _hasBattleBeenJoined;
    // Never reassigns agent.Formation, so no one-time-split latch is needed.
    private bool _archersSplit;

    private int waitCountMainFormation = 0;
    private int waitCountMainFormationMax = 25;

    /// <summary>Bumped on every OnApply: the battle position's hold (RBMBehaviorHoldPosition) starts again on a new application.</summary>
    private int _applyGeneration;

    public RBMTacticDefendSplitArchers(Team team)
        : base(team)
    {
    }

    protected override void OnApply()
    {
        base.OnApply();
        _applyGeneration++;
        RBMBehaviorHoldPosition.ResetEngage(this);
    }

    protected void AssignTacticFormations()
    {
        _archersSplit = RBMTacticAttackSplitArchers.ShouldSplitArchers(Team, _archersSplit);
        ManageFormationCounts(1, _archersSplit ? 2 : 1, 2, 1);
        _mainInfantry = ChooseAndSortByPriority(FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.IsMainFormation = true;
            _mainInfantry.AI.Side = FormationAI.BehaviorSide.Middle;
        }
        List<Formation> cavFormationsList = ChooseAndSortByPriority(FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower);
        if (cavFormationsList.Count > 0)
        {
            _leftCavalry = cavFormationsList[0];
            _leftCavalry.AI.Side = FormationAI.BehaviorSide.Left;
            if (cavFormationsList.Count > 1)
            {
                _rightCavalry = cavFormationsList[1];
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
        List<Formation> archerFormationsList = ChooseAndSortByPriority(FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsRangedFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower);
        if (archerFormationsList.Count > 0)
        {
            leftArchers = archerFormationsList[0];
            leftArchers.AI.Side = FormationAI.BehaviorSide.Left;
            if (archerFormationsList.Count > 1)
            {
                rightArchers = archerFormationsList[1];
                rightArchers.AI.Side = FormationAI.BehaviorSide.Right;
            }
            else
            {
                rightArchers = null;
            }
        }
        else
        {
            leftArchers = null;
            rightArchers = null;
        }

        IsTacticReapplyNeeded = true;
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
        // No melee-infantry main: the archer anchor stands (RBMBehaviorArcherStand owns it in both phases).
        Formation line = ArmyComposition.LineHolder(Team);
        bool archerLine = line != null;
        if (_mainInfantry != null && !archerLine)
        {
            if (waitCountMainFormation < waitCountMainFormationMax)
            {
                _mainInfantry.AI.ResetBehaviorWeights();
                TacticComponent.SetDefaultBehaviorWeights(_mainInfantry);
                _mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
                waitCountMainFormation++;
                IsTacticReapplyNeeded = true;
            }
            else
            {
                _mainInfantry.AI.SetBehaviorWeight<BehaviorHoldHighGround>(1f);
                // The defender's battle position (needs a published plan; outweighs the high ground); no plan sets nothing.
                RBMBehaviorHoldPosition.TryAssign(_mainInfantry, this, _applyGeneration, RBMBehaviorHoldPosition.Role.Line);
                IsTacticReapplyNeeded = false;
            }
        }
        else if (_mainInfantry != null)
        {
            // token infantry beside an archer line: defaults; RBMBehaviorArcherStand's escort outweighs them
            _mainInfantry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_mainInfantry);
        }
        if (leftArchers != null)
        {
            leftArchers.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(leftArchers);
            leftArchers.AI.SetBehaviorWeight<RBMBehaviorArcherFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;
        }
        if (rightArchers != null)
        {
            rightArchers.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(rightArchers);
            rightArchers.AI.SetBehaviorWeight<RBMBehaviorArcherFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
        }
        if (archerLine && RBMBehaviorHoldPosition.YieldToArcherStand(Team, this, _applyGeneration, _mainInfantry, null))
        {
            if (leftArchers != null && leftArchers != line)
            {
                RBMBehaviorHoldPosition.TryAssign(leftArchers, this, _applyGeneration, RBMBehaviorHoldPosition.Role.Archers);
            }
            if (rightArchers != null && rightArchers != line)
            {
                RBMBehaviorHoldPosition.TryAssign(rightArchers, this, _applyGeneration, RBMBehaviorHoldPosition.Role.Archers);
            }
        }
        if (_leftCavalry != null)
        {
            _leftCavalry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_leftCavalry);
            _leftCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = TerrainFlankPlan.CavalrySide(this, _leftCavalry, FormationAI.BehaviorSide.Left, _rightCavalry == null);
        }
        if (_rightCavalry != null)
        {
            _rightCavalry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_rightCavalry);
            _rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = TerrainFlankPlan.CavalrySide(this, _rightCavalry, FormationAI.BehaviorSide.Right, false);
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
        RBMAI.Utilities.FixCharge(ref _mainInfantry);
        if (leftArchers != null)
        {
            leftArchers.AI.ResetBehaviorWeights();
            leftArchers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
        }
        if (rightArchers != null)
        {
            rightArchers.AI.ResetBehaviorWeights();
            rightArchers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
        }
        if (_leftCavalry != null)
        {
            _leftCavalry.AI.ResetBehaviorWeights();
            //TacticComponent.SetDefaultBehaviorWeights(_leftCavalry);
            _leftCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
            _leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        if (_rightCavalry != null)
        {
            _rightCavalry.AI.ResetBehaviorWeights();
            //TacticComponent.SetDefaultBehaviorWeights(_rightCavalry);
            _rightCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
            _rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        if (_rangedCavalry != null)
        {
            _rangedCavalry.AI.ResetBehaviorWeights();
            //TacticComponent.SetDefaultBehaviorWeights(_rangedCavalry);
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        IsTacticReapplyNeeded = false;
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
            if ((_mainInfantry == null || (_mainInfantry.CountOfUnits != 0 && _mainInfantry.QuerySystem.IsInfantryFormation)) && (leftArchers == null || (leftArchers.CountOfUnits != 0 && leftArchers.QuerySystem.IsRangedFormation)) && (rightArchers == null || (rightArchers.CountOfUnits != 0 && rightArchers.QuerySystem.IsRangedFormation)) && (_leftCavalry == null || (_leftCavalry.CountOfUnits != 0 && _leftCavalry.QuerySystem.IsCavalryFormation)) && (_rightCavalry == null || (_rightCavalry.CountOfUnits != 0 && _rightCavalry.QuerySystem.IsCavalryFormation)))
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
        // Re-apply when the battle position appears or moves.
        if (TerrainBattlePosition.NeedsReapply(this))
        {
            IsTacticReapplyNeeded = true;
        }
        bool flag = HasBattleBeenJoined();
        if (CheckAndSetAvailableFormationsChanged())
        {
            _hasBattleBeenJoined = flag;
            ManageFormationCounts();
            if (_hasBattleBeenJoined)
            {
                Engage();
            }
            else
            {
                Defend();
            }
            //IsTacticReapplyNeeded = false;
        }
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
            //IsTacticReapplyNeeded = false;
        }
        base.TickOccasionally();
    }

    // Graded on the native scale by the army's archer share (grows as our side weakens); TacticSelection applies culture/commander/jitter on top.
    protected override float GetTacticWeight()
    {
        float fit;
        bool fits = TacticSelection.TryGetArcherFit(Team, out fit);
        // no melee-infantry main: native DefensiveLine / DefensiveEngagement weigh 0 without infantry, so this tactic hosts the archer stand
        if (ArmyComposition.LineHolder(Team) != null)
        {
            fit = MathF.Max(fits ? fit : 0f, NoMainFit);
            fits = true;
        }
        if (!fits)
        {
            return TacticSelection.UnfitWeight;
        }
        return fit * 1.1f / MathF.Sqrt(MathF.Max(Team.QuerySystem.RemainingPowerRatio, 0.05f));
    }

    private const float NoMainFit = 0.5f;
}