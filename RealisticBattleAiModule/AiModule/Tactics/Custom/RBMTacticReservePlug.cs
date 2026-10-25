using RBMAI;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

/// <summary>
/// Reserve Plug (defender, signature empire): the infantry holds the battle position with about two thirds of its men while
/// the other third waits out of sight at the plan's plug point and goes in where the line needs it (RBMBehaviorReservePlug).
/// SPLIT (as RBMTacticDefendSplitInfantry, honouring IsFormationReshufflingUnsafe / _membershipSplitDone): the reserve gets
/// ReserveShare of the infantry, the STRONGEST by CharacterPowerCached (it fights where the enemy already wins). Under
/// MinInfantryCount melee infantry there is no reserve. After the first deal a re-split keeps the reserve's men and sends
/// newcomers/strays to the line, engaged or not; once the line engaged or the reserve committed, no NEW reserve is dealt.
/// Hold: the line, archers, cavalry and horse archers exactly as RBMTacticDefendSplitInfantry's Defend; the reserve
/// RBMBehaviorReservePlug. Engage: the line engages like the other battle-position tactics (RBMBehaviorHoldPosition.BattleJoinedFor);
/// the reserve keeps RBMBehaviorReservePlug and its own commit logic until it commits.
/// Weight: defender only; 0.01 without minimum infantry or a published battle position (so only the terrain re-pick picks it);
/// else graded by size and attacker superiority, / sqrt(remaining power). A position without a plug point stays usable
/// (the reserve follows its line); a later re-pick that finds one re-applies the tactic.
/// THREADING: TickOccasionally and GetTacticWeight run on the async AI thread: volatile read of an immutable plan, query
/// systems, cached formation data. No engine spatial query, no MBRandom.
/// </summary>
public class RBMTacticReservePlug : TacticComponent
{

    public const float MinInfantryRatio = 0.35f;
    public const float MinInfantryCount = 120f;
    public const float FullInfantryCount = 300f;
    public const float ReserveShare = 1f / 3f;
    public const float OutnumberedNone = 0.8f;
    public const float OutnumberedFull = 2f;

    protected Formation _reserveInfantry;
    private bool _membershipSplitDone;
    private bool _reserveDealt; // later re-splits keep its men
    private bool _hasBattleBeenJoined;

    /// <summary>Bumped on every OnApply: the hold and the reserve restart.</summary>
    private int _applyGeneration;

    public RBMTacticReservePlug(Team team)
        : base(team)
    {
    }

    protected override void OnApply()
    {
        base.OnApply();
        _applyGeneration++;
        _hasBattleBeenJoined = false;
    }

    protected override void ManageFormationCounts()
    {
        AssignTacticFormations();
    }

    private void AssignTacticFormations()
    {
        // Under BattleMiniMap (IsFormationReshufflingUnsafe) the agent moves happen once.
        bool doReassign = !RBMAI.Tactics.IsFormationReshufflingUnsafe || !_membershipSplitDone;
        // A line that engaged (or a reserve in the fight): no NEW reserve; one already dealt keeps its men.
        bool spent = _hasBattleBeenJoined || RBMBehaviorReservePlug.HasCommitted(_reserveInfantry, this);

        ManageFormationCounts(2, 1, 2, 1);

        List<Formation> nonEmptyFormations = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList();
        _mainInfantry = ChooseAndSortByPriority(nonEmptyFormations, (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        Formation previousReserve = _reserveInfantry;
        _reserveInfantry = null;

        if (_mainInfantry != null)
        {
            _mainInfantry.AI.IsMainFormation = true;
            _mainInfantry.AI.Side = FormationAI.BehaviorSide.Middle;

            List<Formation> slots = FormationsIncludingEmpty
                .Where((Formation f) => f != _mainInfantry && f.IsAIControlled && f.QuerySystem.IsInfantryFormation)
                .ToList();

            // Never reshuffle men the player commands.
            if (_mainInfantry.IsAIControlled && slots.Count >= 1)
            {
                Formation reserveSlot;
                Formation unused;
                RBMTacticAttackSplitInfantry.PickFlankSlots(slots, previousReserve, null, out reserveSlot, out unused);

                List<Agent> allInfantry = new List<Agent>();
                _mainInfantry.ApplyActionOnEachUnitViaBackupList((Agent a) => allInfantry.Add(a));
                if (reserveSlot.CountOfUnits > 0)
                    reserveSlot.ApplyActionOnEachUnitViaBackupList((Agent a) => allInfantry.Add(a));
                List<Formation> strayFormations = RBMTacticAttackSplitInfantry.CollectStrayInfantry(slots, reserveSlot, null, allInfantry);
                int infCount = allInfantry.Count;

                if (doReassign && _reserveDealt && reserveSlot == previousReserve && reserveSlot.CountOfUnits > 0)
                {
                    // After the first split (and once engaged/committed) a re-split sends newcomers/strays to the line and never moves a man out of the reserve.
                    int kept = 0;
                    int toLine = 0;
                    foreach (Agent agent in allInfantry)
                    {
                        if (agent.Formation == reserveSlot)
                        {
                            kept++;
                        }
                        else if (agent.Formation != _mainInfantry)
                        {
                            agent.Formation = _mainInfantry;
                            toLine++;
                        }
                    }
                    if (toLine > 0)
                    {
                        Mission mission = Mission.Current;
                        TerrainBattlePosition.Enqueue(Team, reserveSlot, (mission != null) ? mission.CurrentTime : 0f, "ReservePlug", "resplit", "plug",
                            "reserve kept=" + kept.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + " newcomers to the line=" + toLine.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                else if (doReassign)
                {
                    int reserveCount = (!spent && RBMTacticAttackSplitInfantry.ShouldSplit(infCount) && infCount >= MinInfantryCount)
                        ? (int)(infCount * ReserveShare)
                        : 0;
                    List<Agent> ordered = allInfantry.OrderByDescending((Agent a) => a.CharacterPowerCached).ToList();
                    for (int j = 0; j < ordered.Count; j++)
                    {
                        ordered[j].Formation = (j < reserveCount) ? reserveSlot : _mainInfantry;
                    }
                    _reserveDealt = reserveCount > 0;
                }
                if (RBMAI.Tactics.IsFormationReshufflingUnsafe)
                {
                    _membershipSplitDone = true;
                }

                if (reserveSlot.CountOfUnits > 0)
                {
                    _reserveInfantry = reserveSlot;
                    _reserveInfantry.AI.IsMainFormation = false;
                    _reserveInfantry.AI.Side = FormationAI.BehaviorSide.Middle;
                }

                if (doReassign)
                {
                    this.Team.TriggerOnFormationsChanged(reserveSlot);
                    this.Team.TriggerOnFormationsChanged(_mainInfantry);
                    foreach (Formation stray in strayFormations)
                        this.Team.TriggerOnFormationsChanged(stray);
                }
            }
        }

        nonEmptyFormations = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList();

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
                if (formation.CountOfUnits == 1 && formation.IsAIControlled && formation != _reserveInfantry)
                {
                    formation.ApplyActionOnEachUnitViaBackupList((Agent agent) =>
                    {
                        if (!agent.IsRangedCached && !agent.HasMount && _mainInfantry != null && _mainInfantry.IsAIControlled)
                            agent.Formation = _mainInfantry;
                    });
                }
            }
        }
        if (_reserveInfantry != null && _reserveInfantry.CountOfUnits == 0)
        {
            _reserveInfantry = null;
        }

        IsTacticReapplyNeeded = true;
    }

    private void Hold()
    {
        if (Team.IsPlayerTeam && !Team.IsPlayerGeneral && Team.IsPlayerSergeant)
        {
            SoundTacticalHorn(TacticComponent.MoveHornSoundIndex);
        }
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.ResetBehaviorWeights();
            _mainInfantry.AI.SetBehaviorWeight<BehaviorHoldHighGround>(1f);
            _mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
            RBMBehaviorHoldPosition.TryAssign(_mainInfantry, this, _applyGeneration, RBMBehaviorHoldPosition.Role.Line);
        }
        AssignReserve(false);
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
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.ResetBehaviorWeights();
            _mainInfantry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
        }
        AssignReserve(true);
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
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
    }

    /// <summary>The reserve: RBMBehaviorReservePlug (state carries on across Hold -> Engage: same owner and generation) with BehaviorCharge after the plug; native fallbacks when the behavior is missing.</summary>
    private void AssignReserve(bool engaged)
    {
        if (_reserveInfantry == null)
        {
            return;
        }
        _reserveInfantry.AI.ResetBehaviorWeights();
        if (RBMBehaviorReservePlug.TryAssign(_reserveInfantry, this, _applyGeneration, _mainInfantry))
        {
            _reserveInfantry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
        }
        else if (engaged)
        {
            _reserveInfantry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
        }
        else
        {
            _reserveInfantry.AI.SetBehaviorWeight<BehaviorReserve>(1f);
        }
    }

    private void ApplyPhase()
    {
        if (_hasBattleBeenJoined)
        {
            Engage();
        }
        else
        {
            Hold();
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
            if ((_mainInfantry == null || (_mainInfantry.CountOfUnits != 0 && _mainInfantry.QuerySystem.IsInfantryFormation)) &&
                (_reserveInfantry == null || (_reserveInfantry.CountOfUnits != 0 && _reserveInfantry.QuerySystem.IsInfantryFormation)) &&
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
            ApplyPhase();
        }
        // Re-apply when the battle position (and its plug point) appears or moves.
        if (TerrainBattlePosition.NeedsReapply(this))
        {
            IsTacticReapplyNeeded = true;
        }
        bool joined = HasBattleBeenJoined();
        if (joined != _hasBattleBeenJoined || IsTacticReapplyNeeded)
        {
            _hasBattleBeenJoined = joined;
            ApplyPhase();
        }
        base.TickOccasionally();
    }

    protected override float GetTacticWeight()
    {
        TeamQuerySystem q = Team?.QuerySystem;
        if (q == null || Team.Side != BattleSideEnum.Defender)
        {
            return 0.01f;
        }
        float infantry = q.InfantryRatio * q.MemberCount;
        if (q.InfantryRatio < MinInfantryRatio || infantry < MinInfantryCount || !RBMTacticAttackSplitInfantry.ShouldSplit((int)infantry))
        {
            return 0.01f;
        }
        // The plug point is part of the battle position: none with terrain tactics off, before the publish, or without a plan.
        if (TerrainBattlePosition.PlanFor(Team) == null)
        {
            return 0.01f;
        }
        float size = MBMath.ClampFloat((infantry - MinInfantryCount) / (FullInfantryCount - MinInfantryCount), 0f, 1f);
        float outnumbered = (q.MemberCount > 0) ? (float)q.EnemyUnitCount / q.MemberCount : 1f;
        float numbers = MBMath.ClampFloat((outnumbered - OutnumberedNone) / (OutnumberedFull - OutnumberedNone), 0f, 1f);
        float weight = MBMath.Lerp(0.6f, 1.2f, 0.5f * size + 0.5f * numbers) / MathF.Sqrt(MathF.Max(q.RemainingPowerRatio, 0.05f));
        return MathF.Max(0.01f, weight);
    }
}
