using RBMAI;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

/// <summary>
/// Hold the choke (defender, signature sturgia): the infantry holds a narrow passage on the attacker's way (TerrainChokeHold),
/// archers behind or beside it, cavalry in reserve when the choke covers both flanks. Weighs 0.01 without a published
/// hold, so only the terrain re-pick picks it.
/// Hold: infantry/archers/cavalry RBMBehaviorHoldChoke (Line / Archers / Reserve) with the defend tactics' fallbacks;
/// cavalry ProtectFlank otherwise. Engage (the defend tactics' Engage) starts only on the late charge (ChargedFor,
/// HoldLateCharge): line gone, pushed back, bypassed, out-shot, beaten/breaking enemy in contact, or significant enemy
/// FOOT within 15 m. Enemy cavalry never draws the line out. A local charge returns to the choke (CheckRecall,
/// CheckCalmRecall); other engages latch until CheckRecover. Without a plan the old battleJoined/melee rule applies, as
/// for a player-controlled line.
/// BYPASS (CheckBypass): the enemy concentration behind the hold line, or beside it within BypassRange when the flanks are
/// open, for BypassSeconds with the line out of melee: the hold is dropped for the battle (TerrainChokeHold.MarkBypassed)
/// and the team re-picks at once.
/// Weight: infantry minimums and a published hold, else 0.01; graded by narrowness and the attacker's numbers, / sqrt(remaining power).
/// THREADING: TickOccasionally and GetTacticWeight run on the async AI thread: volatile read of an immutable plan, query
/// systems, cached formation positions; no engine call, no MBRandom.
/// </summary>
public class RBMTacticHoldChoke : TacticComponent
{

    public const float MinInfantryRatio = 0.3f;
    public const float MinInfantryCount = 40f;
    // Hold line width ÷ attacker frontage: full narrowness at or below NarrowFullRatio, none at NarrowNoneRatio.
    public const float NarrowFullRatio = 0.3f;
    public const float NarrowNoneRatio = 1f;
    // Enemy men ÷ ours.
    public const float OutnumberedNone = 0.8f;
    public const float OutnumberedFull = 2f;
    // Without a plan only (the old battleJoined / melee rule).
    public const float BattleJoinRange = 75f; // m, edge gap
    public const float ChokeEngageDistance = 60f; // m, edge gap to the hold point
    public const float EngageMeleeRatio = 0.2f;
    public const float RecallReturnDistance = 150f; // m from the hold
    public const float LeashDistance = 40f; // m from the charge start; out of melee, goes back at once


    // Bypass beside: concentration off the hold's facing by more than BypassOffDegrees and within BypassRange (farther the straight bearing says nothing: the way to a choke bends).
    public const float BypassOffDegrees = 60f;
    public const float BypassRange = 300f; // m
    public const float BypassBehindMargin = 10f; // m behind the hold line
    public const float BypassFootRange = 200f; // m of the hold
    public const float BypassSeconds = 8f; // in a row (hysteresis)
    public const float BypassFootShare = 0.4f; // share of the enemy foot concentration's weight

    private float _bypassSince = float.NaN;

    private bool _engaged;
    /// <summary>HoldLateCharge.ChargeReason when engaged, else null.</summary>
    private string _engagedWord;
    private float _engagedAt = float.NaN;
    /// <summary>The recall rules' timers (no foot / calm, separate).</summary>
    private readonly HoldRecall _recall = new HoldRecall();
    /// <summary>Pursuit leash's anchor.</summary>
    private Vec2 _chargeStart = Vec2.Invalid;
    private float _recalledAt = float.NaN; // NaN = none; quiet for RecallQuietSeconds after it
    // For RecoverReason.
    private int _infantryPeak;
    private int _engagedMen;

    /// <summary>Bumped on every OnApply: the behaviors restart their hold.</summary>
    private int _applyGeneration;

    private TerrainChokeHold.Plan _planSeen; // a new plan re-applies

    public RBMTacticHoldChoke(Team team)
        : base(team)
    {
    }

    /// <summary>AI thread: the choke line in a local charge (infantry only, never riders).</summary>
    internal bool IsLocalChargeOf(Formation formation)
    {
        return _engaged && formation != null && formation == _mainInfantry && HoldLateCharge.IsLocalChargeReason(_engagedWord);
    }

    protected override void OnApply()
    {
        base.OnApply();
        _applyGeneration++;
        _engaged = false;
        _engagedWord = null;
        _engagedAt = float.NaN;
        _recall.Reset();
        _chargeStart = Vec2.Invalid;
        _recalledAt = float.NaN;
        _infantryPeak = 0;
        _engagedMen = 0;
        _bypassSince = float.NaN;
    }

    protected override void ManageFormationCounts()
    {
        ManageFormationCounts(1, 1, 2, 1);
        List<Formation> nonEmpty = FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList();
        _mainInfantry = ChooseAndSortByPriority(nonEmpty, (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.IsMainFormation = true;
            _mainInfantry.AI.Side = FormationAI.BehaviorSide.Middle;
        }
        _archers = ChooseAndSortByPriority(nonEmpty, (Formation f) => f.QuerySystem.IsRangedFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        List<Formation> cavalry = ChooseAndSortByPriority(nonEmpty, (Formation f) => f.QuerySystem.IsCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower);
        if (cavalry.Count > 0)
        {
            _leftCavalry = cavalry[0];
            _leftCavalry.AI.Side = FormationAI.BehaviorSide.Left;
            if (cavalry.Count > 1)
            {
                _rightCavalry = cavalry[1];
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
        _rangedCavalry = ChooseAndSortByPriority(nonEmpty, (Formation f) => f.QuerySystem.IsRangedCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
    }

    private void Hold()
    {
        if (Team.IsPlayerTeam && !Team.IsPlayerGeneral && Team.IsPlayerSergeant)
        {
            SoundTacticalHorn(TacticComponent.MoveHornSoundIndex);
        }
        TerrainChokeHold.Plan plan = TerrainChokeHold.HoldFor(Team);
        _planSeen = plan;
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.ResetBehaviorWeights();
            // The hold (weight 20 while it has a plan); without one the defend tactics' hold.
            float quietUntil = float.IsNaN(_recalledAt) ? float.NegativeInfinity : _recalledAt + HoldLateCharge.RecallQuietSeconds;
            _mainInfantry.AI.SetBehaviorWeight<RBMBehaviorHoldChoke>(1f).Assign(this, _applyGeneration, RBMBehaviorHoldChoke.Role.Line, quietUntil);
            _mainInfantry.AI.SetBehaviorWeight<BehaviorHoldHighGround>(1f);
            _mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
        }
        HoldSupport(plan);
    }

    /// <summary>Everyone but the line (kept through a local charge).</summary>
    private void HoldSupport(TerrainChokeHold.Plan plan)
    {
        if (_archers != null)
        {
            _archers.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_archers);
            _archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
            _archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
            _archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
            _archers.AI.SetBehaviorWeight<RBMBehaviorHoldChoke>(1f).Assign(this, _applyGeneration, RBMBehaviorHoldChoke.Role.Archers);
            _archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
        }
        bool reserve = plan != null && plan.Data.BothFlanksCovered && plan.HasReserve;
        AssignCavalry(_leftCavalry, FormationAI.BehaviorSide.Left, _rightCavalry == null, reserve);
        AssignCavalry(_rightCavalry, FormationAI.BehaviorSide.Right, false, reserve);
        if (_rangedCavalry != null)
        {
            _rangedCavalry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_rangedCavalry);
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
    }

    /// <summary>A reserve behind the line when the choke covers both flanks; else a flank guard (ProtectFlank).</summary>
    private void AssignCavalry(Formation cavalry, FormationAI.BehaviorSide side, bool lone, bool reserve)
    {
        if (cavalry == null)
        {
            return;
        }
        cavalry.AI.ResetBehaviorWeights();
        TacticComponent.SetDefaultBehaviorWeights(cavalry);
        FormationAI.BehaviorSide flankSide = TerrainFlankPlan.CavalrySide(this, cavalry, side, lone);
        cavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = flankSide;
        if (reserve)
        {
            cavalry.AI.SetBehaviorWeight<RBMBehaviorHoldChoke>(1f).Assign(this, _applyGeneration, RBMBehaviorHoldChoke.Role.Reserve);
        }
    }

    private void Engage()
    {
        if (Team.IsPlayerTeam && !Team.IsPlayerGeneral && Team.IsPlayerSergeant)
        {
            SoundTacticalHorn(TacticComponent.AttackHornSoundIndex);
        }
        _planSeen = TerrainChokeHold.HoldFor(Team);
        RBMAI.Utilities.FixCharge(ref _mainInfantry);
        if (HoldLateCharge.IsLocalChargeReason(_engagedWord))
        {
            // A LOCAL charge is the line's alone; the others keep their roles (CheckRecall brings the line back).
            HoldSupport(_planSeen);
            return;
        }
        if (_archers != null)
        {
            _archers.AI.ResetBehaviorWeights();
            _archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
        }
        if (_leftCavalry != null)
        {
            _leftCavalry.AI.ResetBehaviorWeights();
            _leftCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
            _leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        if (_rightCavalry != null)
        {
            _rightCavalry.AI.ResetBehaviorWeights();
            _rightCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
            _rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
        if (_rangedCavalry != null)
        {
            _rangedCavalry.AI.ResetBehaviorWeights();
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
    }

    /// <summary>Why the hold ends now, or null. AI thread. With a plan only the line's late charge; without one the old rule.</summary>
    private string EngageReason(TerrainChokeHold.Plan plan, out string word)
    {
        word = null;
        // No line (no infantry left) or a player-controlled line (its behaviors never tick): the old rule.
        if (plan != null && _mainInfantry != null && _mainInfantry.CountOfUnits > 0 && _mainInfantry.IsAIControlled)
        {
            string reason;
            if (!RBMBehaviorHoldChoke.ChargedFor(_mainInfantry, this, out reason))
            {
                return null;
            }
            word = reason;
            return "lineCharged " + (reason ?? "-");
        }
        if (RBMAI.Utilities.HasBattleBeenJoined(_mainInfantry, false, BattleJoinRange))
        {
            return "battleJoined (no plan/AI line)";
        }
        if (_mainInfantry != null && _mainInfantry.CountOfUnits > 0 && RBMAI.Utilities.FormationFightingInMelee(_mainInfantry, EngageMeleeRatio))
        {
            return "melee (no plan/AI line)";
        }
        return null;
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
            if ((_mainInfantry == null || (_mainInfantry.CountOfUnits != 0 && _mainInfantry.QuerySystem.IsInfantryFormation))
                && (_archers == null || (_archers.CountOfUnits != 0 && _archers.QuerySystem.IsRangedFormation))
                && (_leftCavalry == null || (_leftCavalry.CountOfUnits != 0 && _leftCavalry.QuerySystem.IsCavalryFormation))
                && (_rightCavalry == null || (_rightCavalry.CountOfUnits != 0 && _rightCavalry.QuerySystem.IsCavalryFormation)))
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

    private void ApplyPhase()
    {
        if (_engaged)
        {
            Engage();
        }
        else
        {
            Hold();
        }
        IsTacticReapplyNeeded = false;
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
        TerrainChokeHold.Plan plan = TerrainChokeHold.HoldFor(Team);
        // Re-apply when the hold appears or goes.
        if (plan != _planSeen)
        {
            IsTacticReapplyNeeded = true;
        }
        Mission mission = Mission.Current;
        float now = (mission != null) ? mission.CurrentTime : 0f;
        int infantryMen = (_mainInfantry != null) ? _mainInfantry.CountOfUnits : 0;
        if (infantryMen > _infantryPeak)
        {
            _infantryPeak = infantryMen;
        }
        if (!_engaged && plan != null && CheckBypass(mission, plan, now))
        {
            // Bypassed: HoldFor is null from now; the re-apply below drops the hold until the re-pick switches.
            IsTacticReapplyNeeded = true;
        }
        if (!_engaged)
        {
            string word;
            string why = EngageReason(plan, out word);
            if (why != null)
            {
                _engaged = true;
                _engagedWord = word;
                _engagedAt = now;
                _engagedMen = infantryMen;
                _recall.Reset();
                _chargeStart = (_mainInfantry != null && _mainInfantry.CountOfUnits > 0) ? RBMAI.Utilities.GetFormationCenter(_mainInfantry) : Vec2.Invalid;
                IsTacticReapplyNeeded = true;
                TerrainChokeHold.Enqueue(Team, _mainInfantry, now, "Hold->Engage", "hold",
                    why + (HoldLateCharge.IsLocalChargeReason(word) ? " (local: line only)" : ""));
            }
        }
        else if (HoldLateCharge.IsLocalChargeReason(_engagedWord))
        {
            CheckRecall(mission, plan, now);
        }
        else if (plan != null)
        {
            // A latched engage: hold again once reinforcements brought the line back.
            string recover = HoldLateCharge.RecoverReason(mission, _mainInfantry, _infantryPeak, _engagedMen);
            if (recover != null)
            {
                Recall(now, recover + " was " + (_engagedWord ?? "noLine"), false);
            }
            else if (_engagedWord == "outshot" || _engagedWord == FarFightWord)
            {
                CheckCalmRecall(mission, plan, now);
            }
        }
        if (IsTacticReapplyNeeded)
        {
            ApplyPhase();
        }
        base.TickOccasionally();
    }

    /// <summary>
    /// AI thread, while engaged: a line charge for a local reason goes back to the choke (Engage->Hold, new generation) while
    /// the hold is within RecallReturnDistance (farther: FarFightWord or CheckCalmRecall), on the first of: the leash (at once,
    /// out of melee, no significant foot within ContactGap) or no significant enemy foot within RecallFootGap for RecallSeconds.
    /// </summary>
    private void CheckRecall(Mission mission, TerrainChokeHold.Plan plan, float now)
    {
        string word = _engagedWord;
        if (mission == null || plan == null || _mainInfantry == null || _mainInfantry.CountOfUnits < HoldLateCharge.LineGoneMen
            || !HoldLateCharge.IsLocalChargeReason(word) || float.IsNaN(_engagedAt))
        {
            _recall.Reset();
            return;
        }
        Vec2 centre = RBMAI.Utilities.GetFormationCenter(_mainInfantry);
        if (!centre.IsValid)
        {
            return;
        }
        float toHold = centre.Distance(plan.Hold);
        if (toHold > RecallReturnDistance)
        {
            // Beyond RecallReturnDistance: still fighting = the full engage (FarFightWord); calm for RecallSeconds = back to the hold.
            _recall.ResetNoFoot();
            float farMelee = HoldLateCharge.MeleeShare(_mainInfantry);
            if (farMelee >= HoldLateCharge.ContactMeleeShare)
            {
                TerrainChokeHold.Enqueue(Team, _mainInfantry, now, "Engage->Engage", "hold", FarFightWord + " was " + word + " local charge fighting "
                    + TerrainChokeHold.Fmt0(toHold) + "m from the hold (>" + TerrainChokeHold.Fmt0(RecallReturnDistance) + ") melee="
                    + farMelee.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ": full engage");
                _engagedWord = FarFightWord;
                _recall.Reset();
                IsTacticReapplyNeeded = true;
                return;
            }
            CheckCalmRecall(mission, plan, now);
            return;
        }
        _recall.ResetCalm();
        float nearestFoot = HoldLateCharge.NearestEnemyFootGap(mission, _mainInfantry);
        float leash;
        float melee;
        if (HoldRecall.Leashed(_mainInfantry, centre, _chargeStart, LeashDistance, nearestFoot, out leash, out melee))
        {
            Recall(now, "recall leash " + word + " charge: " + TerrainChokeHold.Fmt0(leash) + "m from its start (>"
                + TerrainChokeHold.Fmt0(LeashDistance) + ") melee=" + melee.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + " foot=" + FootText(nearestFoot) + " back to the hold " + TerrainChokeHold.Fmt0(toHold) + "m", true);
            return;
        }
        if (!_recall.NoFootFor(now, _engagedAt, nearestFoot))
        {
            return;
        }
        Recall(now, "recall " + word + " charge: no enemy foot within " + TerrainChokeHold.Fmt0(HoldLateCharge.RecallFootGap) + "m for "
            + TerrainChokeHold.Fmt0(HoldLateCharge.RecallSeconds) + "s (nearest foot=" + FootText(nearestFoot)
            + ") back to the hold " + TerrainChokeHold.Fmt0(toHold) + "m", true);
    }

    /// <summary>AI thread: Engage->Hold (new generation), logged with why; quiet (a recall after a local charge) keeps the line quiet for RecallQuietSeconds.</summary>
    private void Recall(float now, string why, bool quiet)
    {
        _engaged = false;
        _engagedWord = null;
        _engagedAt = float.NaN;
        _recall.Reset();
        _chargeStart = Vec2.Invalid;
        _recalledAt = quiet ? now : float.NaN;
        _applyGeneration++;
        IsTacticReapplyNeeded = true;
        TerrainChokeHold.Enqueue(Team, _mainInfantry, now, "Engage->Hold", "hold",
            why + (quiet ? " quiet " + TerrainChokeHold.Fmt0(HoldLateCharge.RecallQuietSeconds) + "s" : "")
            + " men=" + ((_mainInfantry != null) ? _mainInfantry.CountOfUnits : 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // Engage word: local charge still fighting beyond RecallReturnDistance (a full engage).
    private const string FarFightWord = "farFight";

    /// <summary>AI thread: a far local charge, FarFightWord or out-shot engage goes back to the hold (Engage->Hold, quiet) once it has run RecallMinCharge and the line is calm (HoldLateCharge.CalmReason) for RecallSeconds.</summary>
    private void CheckCalmRecall(Mission mission, TerrainChokeHold.Plan plan, float now)
    {
        string word = _engagedWord;
        if (mission == null || plan == null || _mainInfantry == null || _mainInfantry.CountOfUnits < HoldLateCharge.LineGoneMen
            || float.IsNaN(_engagedAt) || now - _engagedAt < HoldLateCharge.RecallMinCharge)
        {
            _recall.ResetCalm();
            return;
        }
        string calm = _recall.CalmFor(now, _engagedAt, HoldLateCharge.CalmReason(mission, _mainInfantry, word == "outshot"));
        if (calm == null)
        {
            return;
        }
        Vec2 centre = RBMAI.Utilities.GetFormationCenter(_mainInfantry);
        float toHold = centre.IsValid ? centre.Distance(plan.Hold) : float.NaN;
        Recall(now, "recall " + (word ?? "-") + " engage: calm " + calm + " for " + TerrainChokeHold.Fmt0(HoldLateCharge.RecallSeconds)
            + "s, back to the hold " + TerrainChokeHold.Fmt0(toHold) + "m", true);
    }

    private static string FootText(float nearestFoot)
    {
        return FormationGeometry.GapText(nearestFoot);
    }

    /// <summary>
    /// AI thread, while holding with a plan: the CHOKE BYPASS. Bypassed when, for BypassSeconds in a row with the line out of
    /// melee, the enemy's main FOOT concentration (riders never bypass) lies more than BypassBehindMargin behind the hold
    /// line, or (only when the flanks are not both covered) within BypassRange and more than BypassOffDegrees off the hold's
    /// facing; the nearest significant melee foot counts too when it carries BypassFootShare of the concentration's weight.
    /// Then TerrainChokeHold.MarkBypassed, an immediate tactic re-pick and a battle-position re-evaluation without the choke.
    /// True when it latched now.
    /// </summary>
    private bool CheckBypass(Mission mission, TerrainChokeHold.Plan plan, float now)
    {
        if (mission == null || plan == null || _mainInfantry == null || _mainInfantry.CountOfUnits < HoldLateCharge.LineGoneMen)
        {
            _bypassSince = float.NaN;
            return false;
        }
        string why = BypassReason(mission, plan);
        if (why == null)
        {
            _bypassSince = float.NaN;
            return false;
        }
        if (float.IsNaN(_bypassSince))
        {
            _bypassSince = now;
            return false;
        }
        if (now - _bypassSince < BypassSeconds)
        {
            return false;
        }
        float melee = HoldLateCharge.MeleeShare(_mainInfantry);
        if (melee >= HoldLateCharge.ContactMeleeShare)
        {
            _bypassSince = float.NaN;
            return false;
        }
        string text = why + " for " + TerrainChokeHold.Fmt0(now - _bypassSince) + "s melee=" + melee.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            + " flanksCovered=" + (plan.Data.BothFlanksCovered ? 1 : 0);
        _bypassSince = float.NaN;
        if (!TerrainChokeHold.MarkBypassed(Team, plan, now, text))
        {
            return false;
        }
        TacticSelection.RequestRepick(Team, "chokeBypassed " + why);
        TerrainBattlePosition.RequestRepick("chokeBypassed");
        return true;
    }

    /// <summary>Why the hold is bypassed right now (before CheckBypass's time and melee checks), else null. AI thread.</summary>
    private string BypassReason(Mission mission, TerrainChokeHold.Plan plan)
    {
        Vec2 facing = plan.Facing;
        if (!facing.IsValid || facing.LengthSquared < 0.01f)
        {
            return null;
        }
        facing = facing.Normalized();
        Vec2 hold = plan.Hold;
        bool flanksCovered = plan.Data.BothFlanksCovered;
        // Enemy FOOT only: riders round the hold must not cancel the choke.
        EnemyConcentration.Result conc;
        bool hasConc = EnemyConcentration.Find(mission, _mainInfantry, EnemyConcentration.Range, out conc, true);
        if (hasConc)
        {
            string where = Where(conc.Centre, hold, facing, flanksCovered, BypassRange);
            if (where != null)
            {
                return where + " " + EnemyConcentration.Describe(conc);
            }
        }
        if (!hasConc)
        {
            return null;
        }
        Formation nearest = null;
        float nearestDistance = BypassFootRange;
        foreach (Team team in mission.Teams)
        {
            if (team == null || !team.IsEnemyOf(Team))
            {
                continue;
            }
            foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
            {
                if (e == null || e.CountOfUnits <= 0 || e.QuerySystem.IsCavalryFormation || e.QuerySystem.IsRangedCavalryFormation
                    || RBMAI.Utilities.IsRangedForArrangement(e) || e.GetReadonlyMovementOrderReference().OrderType == OrderType.Retreat)
                {
                    continue;
                }
                Vec2 c = RBMAI.Utilities.GetFormationCenter(e);
                if (!c.IsValid)
                {
                    continue;
                }
                float d = c.Distance(hold);
                if (d < nearestDistance && ThreatSignificance.IsSignificant(_mainInfantry, e))
                {
                    nearestDistance = d;
                    nearest = e;
                }
            }
        }
        // Only a group carrying BypassFootShare of the enemy foot: a flanking detachment is the line's own behind/allyFighting business.
        float share = (nearest != null && conc.Weight > 0f) ? EnemyConcentration.WeightOf(nearest) / conc.Weight : 0f;
        if (nearest != null && share >= BypassFootShare)
        {
            string where = Where(RBMAI.Utilities.GetFormationCenter(nearest), hold, facing, flanksCovered, BypassFootRange);
            if (where != null)
            {
                return "foot" + where.Substring(0, 1).ToUpperInvariant() + where.Substring(1) + " " + ThreatSignificance.Describe(_mainInfantry, nearest)
                    + " share=" + share.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        return null;
    }

    /// <summary>"behind along=-35m dist=120m off=118deg" / "beside ..." when p is behind / beside the hold, else null.</summary>
    private static string Where(Vec2 p, Vec2 hold, Vec2 facing, bool flanksCovered, float besideRange)
    {
        Vec2 rel = p - hold;
        float distance = rel.Length;
        if (distance < 1f)
        {
            return null;
        }
        float along = Vec2.DotProduct(rel, facing);
        float off = (float)(System.Math.Acos(MBMath.ClampFloat(along / distance, -1f, 1f)) * 180.0 / System.Math.PI);
        string text = " along=" + TerrainChokeHold.Fmt0(along) + "m dist=" + TerrainChokeHold.Fmt0(distance) + "m off=" + TerrainChokeHold.Fmt0(off) + "deg";
        if (along < -BypassBehindMargin)
        {
            return "behind" + text;
        }
        if (!flanksCovered && distance <= besideRange && off > BypassOffDegrees)
        {
            return "beside" + text;
        }
        return null;
    }

    protected override float GetTacticWeight()
    {
        TeamQuerySystem q = Team?.QuerySystem;
        if (q == null)
        {
            return 0.01f;
        }
        float infantry = q.InfantryRatio * q.MemberCount;
        if (q.InfantryRatio < MinInfantryRatio || infantry < MinInfantryCount)
        {
            return 0.01f;
        }
        // The hold needs the ground's choke: none with terrain tactics off, before the publish, or when none qualifies.
        TerrainChokeHold.Plan plan = TerrainChokeHold.HoldFor(Team);
        if (plan == null)
        {
            return 0.01f;
        }
        float attackerFrontage = plan.Data.AttackerFrontage;
        float ratio = (float.IsNaN(attackerFrontage) || attackerFrontage <= 1f) ? NarrowNoneRatio : plan.Data.LineWidth / attackerFrontage;
        float narrow = MBMath.ClampFloat((NarrowNoneRatio - ratio) / (NarrowNoneRatio - NarrowFullRatio), 0f, 1f);
        float outnumbered = (q.MemberCount > 0) ? (float)q.EnemyUnitCount / q.MemberCount : 1f;
        float numbers = MBMath.ClampFloat((outnumbered - OutnumberedNone) / (OutnumberedFull - OutnumberedNone), 0f, 1f);
        float weight = MBMath.Lerp(0.6f, 1.3f, 0.6f * narrow + 0.4f * numbers) / MathF.Sqrt(MathF.Max(q.RemainingPowerRatio, 0.05f));
        return MathF.Max(0.01f, weight);
    }
}
