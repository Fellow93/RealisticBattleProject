using RBMAI;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

public class RBMTacticAttackSplitInfantry : TacticComponent
{
    protected Formation _flankingInfantry = null;
    protected Formation _leftFlankingInfantry = null;
    protected Formation _rightFlankingInfantry = null;
    private bool _membershipSplitDone;
    private int side = MBRandom.RandomInt(2);
    // The flank plan the last split read (null before the terrain publish); a re-apply after the publish re-splits.
    private TerrainFlankPlan.SidePlan _splitPlan;
    private float _lastPlanResplitAt = float.NaN;
    private const float PlanResplitInterval = 30f;
    // NaN: no split yet. After battle joined / within SplitRefreshInterval, a count change only refreshes references (ManageFormationCounts pulls every foot man back into Infantry).
    private float _lastSplitAt = float.NaN;
    private const float SplitRefreshInterval = 30f;

    protected void AssignTacticFormations()
    {
        _splitPlan = TerrainFlankPlan.ForTeam(Team);
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
                PickFlankSlots(flankingSlots, previousLeft, previousRight, out leftSlot, out rightSlot);

                // Read the three infantry slots directly: QuerySystem can be stale right after ManageFormationCounts.
                List<Agent> allInfantry = new List<Agent>();
                _mainInfantry.ApplyActionOnEachUnitViaBackupList((Agent a) => allInfantry.Add(a));
                if (leftSlot.CountOfUnits > 0)
                    leftSlot.ApplyActionOnEachUnitViaBackupList((Agent a) => allInfantry.Add(a));
                if (rightSlot.CountOfUnits > 0)
                    rightSlot.ApplyActionOnEachUnitViaBackupList((Agent a) => allInfantry.Add(a));
                List<Formation> strayFormations = CollectStrayInfantry(flankingSlots, leftSlot, rightSlot, allInfantry);
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

                // Under BattleMiniMap (IsFormationReshufflingUnsafe) the agent moves happen once, never again.
                bool doReassign = !RBMAI.Tactics.IsFormationReshufflingUnsafe || !_membershipSplitDone;
                if (doReassign)
                {
                    // No guards under MinInfantryToSplit or on a flank TerrainFlankPlan covers.
                    bool split = ShouldSplit(infCount);
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
                    _lastSplitAt = (Mission.Current != null) ? Mission.Current.CurrentTime : 0f;
                    this.Team.TriggerOnFormationsChanged(leftSlot);
                    this.Team.TriggerOnFormationsChanged(rightSlot);
                    this.Team.TriggerOnFormationsChanged(_mainInfantry);
                    foreach (Formation stray in strayFormations)
                        this.Team.TriggerOnFormationsChanged(stray);
                }
            }
        }

        AssignSupportFormations();
    }

    /// <summary>The archer / cavalry / horse archer references from the formations as they are now (no man moved).</summary>
    private void AssignSupportFormations()
    {
        List<Formation> nonEmptyFormations = FormationsIncludingEmpty
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

    // User rule: with fewer melee infantry than this there are no flank guards.
    internal const int MinInfantryToSplit = 100;

    internal static bool ShouldSplit(int infCount)
    {
        return infCount >= MinInfantryToSplit;
    }

    // Native ManageFormationCounts also fills a third infantry slot the split does not deal; its foot men are added to the split.
    // A formation the player ordered to dismount stays together.
    internal static List<Formation> CollectStrayInfantry(List<Formation> flankingSlots, Formation leftSlot, Formation rightSlot, List<Agent> allInfantry)
    {
        List<Formation> strays = new List<Formation>();
        foreach (Formation f in flankingSlots)
        {
            if (f == leftSlot || f == rightSlot || f.CountOfUnits == 0 || f.RidingOrder.OrderEnum == RidingOrder.RidingOrderEnum.Dismount)
            {
                continue;
            }
            f.ApplyActionOnEachUnitViaBackupList((Agent a) =>
            {
                if (!a.HasMount)
                    allInfantry.Add(a);
            });
            strays.Add(f);
        }
        return strays;
    }

    // An EMPTY formation reads as infantry, so previous flanks keep their slots and sides and an open slot prefers a formation with men.
    internal static void PickFlankSlots(List<Formation> flankingSlots, Formation previousLeft, Formation previousRight, out Formation leftSlot, out Formation rightSlot)
    {
        leftSlot = flankingSlots.Contains(previousLeft) ? previousLeft : null;
        rightSlot = flankingSlots.Contains(previousRight) ? previousRight : null;
        foreach (Formation f in flankingSlots.OrderBy((Formation slot) => slot.CountOfUnits > 0 ? 0 : 1))
        {
            if (f == leftSlot || f == rightSlot)
            {
                continue;
            }
            if (leftSlot == null)
            {
                leftSlot = f;
            }
            else if (rightSlot == null)
            {
                rightSlot = f;
            }
        }
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
            // A guard left on a covered flank advances with the main line.
            if (TerrainFlankPlan.GuardSkipped(this, FormationAI.BehaviorSide.Left, false))
            {
                _leftFlankingInfantry.AI.SetBehaviorWeight<BehaviorAdvance>(1f);
                _leftFlankingInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.5f);
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
                _rightFlankingInfantry.AI.SetBehaviorWeight<BehaviorAdvance>(1f);
                _rightFlankingInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.5f);
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
            _leftCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = TerrainFlankPlan.CavalrySide(this, _leftCavalry, FormationAI.BehaviorSide.Left, _rightCavalry == null);
            _leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        if (_rightCavalry != null)
        {
            _rightCavalry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_rightCavalry);
            _rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = TerrainFlankPlan.CavalrySide(this, _rightCavalry, FormationAI.BehaviorSide.Right, false);
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
            _leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
            _leftCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
        }
        if (_rightCavalry != null)
        {
            _rightCavalry.AI.ResetBehaviorWeights();
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
        return RBMAI.Utilities.HasAttackBeenJoined(_mainInfantry, Team, _hasBattleBeenJoined, 70f);
    }

    /// <summary>References from the formations as they are now, no man moved and no ManageFormationCounts (its RBM prefix pulls foot men back into Infantry).</summary>
    private void RefreshReferences()
    {
        _flankingInfantry = null;
        if (_leftFlankingInfantry != null && (_leftFlankingInfantry.CountOfUnits <= 0 || !_leftFlankingInfantry.IsAIControlled))
        {
            _leftFlankingInfantry = null;
        }
        if (_rightFlankingInfantry != null && (_rightFlankingInfantry.CountOfUnits <= 0 || !_rightFlankingInfantry.IsAIControlled))
        {
            _rightFlankingInfantry = null;
        }
        List<Formation> nonEmptyFormations = FormationsIncludingEmpty
            .Where((Formation f) => f.CountOfUnits > 0 && f != _leftFlankingInfantry && f != _rightFlankingInfantry).ToList();
        _mainInfantry = ChooseAndSortByPriority(nonEmptyFormations, (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        if (_mainInfantry == null)
        {
            _mainInfantry = (_leftFlankingInfantry != null && (_rightFlankingInfantry == null || _leftFlankingInfantry.CountOfUnits >= _rightFlankingInfantry.CountOfUnits))
                ? _leftFlankingInfantry : _rightFlankingInfantry;
            if (_mainInfantry == _leftFlankingInfantry)
            {
                _leftFlankingInfantry = null;
            }
            else
            {
                _rightFlankingInfantry = null;
            }
        }
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.IsMainFormation = true;
            _mainInfantry.AI.Side = FormationAI.BehaviorSide.Middle;
        }
        if (_leftFlankingInfantry != null)
        {
            _leftFlankingInfantry.AI.IsMainFormation = false;
        }
        if (_rightFlankingInfantry != null)
        {
            _rightFlankingInfantry.AI.IsMainFormation = false;
        }
        AssignSupportFormations();
        RBMAI.Tactics.MergeStrayFormations(Team, _mainInfantry, _archers, _leftCavalry, _rangedCavalry, _leftFlankingInfantry, _rightFlankingInfantry, _rightCavalry);
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
        // A re-apply after a changed flank plan re-splits only before the battle is joined and once per PlanResplitInterval; otherwise weights only.
        bool planResplit = false;
        TerrainFlankPlan.SidePlan plan = IsTacticReapplyNeeded ? TerrainFlankPlan.ForTeam(Team) : _splitPlan;
        if (plan != _splitPlan)
        {
            float now = (Mission.Current != null) ? Mission.Current.CurrentTime : 0f;
            string skipped = _hasBattleBeenJoined ? "battleJoined"
                : (!float.IsNaN(_lastPlanResplitAt) && now - _lastPlanResplitAt < PlanResplitInterval) ? "interval " + MathF.Round(now - _lastPlanResplitAt) + "s"
                : null;
            if (skipped == null)
            {
                planResplit = true;
                _lastPlanResplitAt = now;
            }
            else if (_mainInfantry != null)
            {
                TerrainSpots.EnqueueBehavior(Team, _mainInfantry, now, "AttackSplitInfantry", "resplitSkipped", "flankPlan", skipped + " (weights only)");
            }
        }
        if (CheckAndSetAvailableFormationsChanged() || planResplit)
        {
            float now = (Mission.Current != null) ? Mission.Current.CurrentTime : 0f;
            string refreshOnly = planResplit ? null
                : _hasBattleBeenJoined ? "battleJoined"
                : (!float.IsNaN(_lastSplitAt) && now - _lastSplitAt < SplitRefreshInterval) ? "interval " + MathF.Round(now - _lastSplitAt) + "s"
                : null;
            if (refreshOnly != null)
            {
                RefreshReferences();
                if (_mainInfantry != null)
                {
                    TerrainSpots.EnqueueBehavior(Team, _mainInfantry, now, "AttackSplitInfantry", "refreshOnly", "formations", refreshOnly + " (no men moved)");
                }
            }
            else
            {
                ManageFormationCounts();
            }
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

    // Graded on the native scale by how far our infantry outnumbers the enemy's; TacticSelection applies culture/commander/jitter on top.
    protected override float GetTacticWeight()
    {
        float t;
        if (!TacticSelection.TryGetInfantrySuperiority(Team, out t))
        {
            return TacticSelection.UnfitWeight;
        }
        return MBMath.Lerp(0.45f, 1.1f, t) * MathF.Sqrt(Team.QuerySystem.RemainingPowerRatio);
    }
}