using RBMAI;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

public class RBMTacticEmbolon : TacticComponent
{
    private Formation _cavalry;

    private bool _hasBattleBeenJoined;

    /// <summary>Inputs of the last GetTacticWeight, for the AI behavior log. Plain floats: written on the AI thread, read on the main one.</summary>
    internal float LastCavCount;
    internal float LastHeavyShare;

    /// <summary>The wedge cavalry (ReactionMonitor never sends it elsewhere).</summary>
    internal Formation Wedge => _cavalry;

    public RBMTacticEmbolon(Team team)
        : base(team)
    {
    }

    protected override void ManageFormationCounts()
    {
        ManageFormationCounts(1, 1, 1, 1);
        _mainInfantry = TacticComponent.ChooseAndSortByPriority(base.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.IsMainFormation = true;
        }
        _archers = TacticComponent.ChooseAndSortByPriority(base.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsRangedFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        _cavalry = TacticComponent.ChooseAndSortByPriority(base.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        _rangedCavalry = TacticComponent.ChooseAndSortByPriority(base.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0), (Formation f) => f.QuerySystem.IsRangedCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
    }

    private void Advance()
    {
        if (Team.IsPlayerTeam && !Team.IsPlayerGeneral && Team.IsPlayerSergeant)
        {
            SoundTacticalHorn(TacticComponent.MoveHornSoundIndex);
        }
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_mainInfantry);
            _mainInfantry.AI.SetBehaviorWeight<BehaviorAdvance>(1f);
            _mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
        }
        if (_archers != null)
        {
            _archers.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_archers);
            _archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
        }
        if (_cavalry != null)
        {
            _cavalry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_cavalry);
            _cavalry.AI.SetBehaviorWeight<RBMBehaviorEmbolon>(1.5f);
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
        RBMAI.Utilities.FixCharge(ref _mainInfantry);
        if (_archers != null)
        {
            _archers.AI.ResetBehaviorWeights();
            _archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
            _archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
            _archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
        }
        if (_cavalry != null)
        {
            _cavalry.AI.ResetBehaviorWeights();
            SetDefaultBehaviorWeights(_cavalry);
            // RBM's charge loop (charge through, pull clear, reform): native defaults alone leave the wedge stuck in the melee.
            _cavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
        }
        if (_rangedCavalry != null)
        {
            _rangedCavalry.AI.ResetBehaviorWeights();
            //SetDefaultBehaviorWeights(_rangedCavalry);
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
    }

    private bool HasBattleBeenJoined()
    {
        return RBMAI.Utilities.HasAttackBeenJoined(_mainInfantry, Team, _hasBattleBeenJoined, 95f);
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
            if ((_mainInfantry == null || (_mainInfantry.CountOfUnits != 0 && _mainInfantry.QuerySystem.IsInfantryFormation)) && (_archers == null || (_archers.CountOfUnits != 0 && _archers.QuerySystem.IsRangedFormation)) && (_cavalry == null || (_cavalry.CountOfUnits != 0 && _cavalry.QuerySystem.IsCavalryFormation)))
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
        float heavyCavCount = 0;
        float cavCount = 0;

        if (Team != null)
        {
            foreach (Formation formation in Team.FormationsIncludingEmpty)
            {
                if (formation.CountOfUnits > 0 && formation.QuerySystem.IsCavalryFormation)
                {
                    formation.ApplyActionOnEachUnitViaBackupList((Agent agent) =>
                    {
                        if (agent.HasMount)
                        {
                            if (agent.Character?.Level >= 21 && agent.MountAgent != null)
                            {
                                EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.HorseHarness];
                                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness
                                    && (float)equipmentElement.GetModifiedArmArmor() > 40f)
                                {
                                    heavyCavCount++;
                                }
                            }
                            cavCount++;
                        }
                    });
                }
            }
        }

        // Graded on the native scale: needs a real, mostly barded cavalry wing; TacticSelection applies culture/commander/jitter on top.
        LastCavCount = cavCount;
        LastHeavyShare = (cavCount > 0f) ? heavyCavCount / cavCount : 0f;
        if (cavCount < 20f)
        {
            return 0.01f;
        }
        float heavyShare = heavyCavCount / cavCount;
        float cavalryFit = MBMath.ClampFloat((Team.QuerySystem.CavalryRatio - 0.1f) / 0.2f, 0f, 1f);
        float heavyFit = MBMath.ClampFloat((heavyShare - 0.3f) / 0.4f, 0f, 1f);
        return MathF.Max(0.01f, cavalryFit * heavyFit * MathF.Sqrt(Team.QuerySystem.RemainingPowerRatio));
    }
}