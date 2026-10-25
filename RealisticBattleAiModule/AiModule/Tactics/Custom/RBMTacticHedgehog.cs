using RBMAI;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

/// <summary>
/// Hedgehog (defender, signature battania): against a strong enemy cavalry arm, with weak own cavalry and spear/shield
/// infantry, the infantry squares up (circle when surrounded or with the archers inside) at the battle position, archers
/// inside, cavalry in reserve. Defender only: an attacker that squares up and waits is not attacking.
/// Hold: infantry/archers RBMBehaviorHedgehog (HoldHighGround/Regroup and ArcherSkirmish fallbacks), cavalry ProtectFlank
/// or HoldPosition Reserve when both line ends are covered, horse archers ScreenedSkirmish + MountedSkirmish. A
/// player-controlled infantry never charges late (old battle-joined rule engages the army).
/// Engage (the defend tactics' Engage) starts only on the late charge (ChargedFor, HoldLateCharge): line gone, pushed
/// back, bypassed, out-shot, beaten/breaking enemy in contact, enemy cavalry mostly gone, or significant enemy FOOT within 15 m.
/// Enemy cavalry never starts contact. A charge for a contact reason is LOCAL (infantry alone) and re-forms via
/// CheckRecall; the other engages latch until CheckRecover. "Cavalry" here is MELEE cavalry only (horse archers are no reason to square up).
/// Weight: 0.01 without TerrainHedgehog's read or when not Fit, or once the enemy melee cavalry is under 30% of the publish's;
/// else Lerp(0.6, 1.2) of threat and polearm/shield share, / sqrt(remaining power).
/// THREADING: TickOccasionally and GetTacticWeight run on the async AI thread: volatile reads of immutable objects, query
/// systems, cached formation data; no engine call, no MBRandom.
/// </summary>
public class RBMTacticHedgehog : TacticComponent
{

    // Graded from a floor over a span: enemy mounted share / power ratio, polearm-shield share.
    public const float ThreatShareSpan = 0.3f;
    public const float ThreatPowerSpan = 2.5f;
    public const float PikeSpan = 0.3f;

    public const float RecallReturnDistance = 80f; // m; farther: squares up in place

    private bool _engaged;
    private string _engagedReason;
    private float _engagedAt = float.NaN;
    private float _noFootSince = float.NaN;
    /// <summary>Pursuit leash's anchor.</summary>
    private Vec2 _chargeStart = Vec2.Invalid;
    private bool _reformInPlace;
    private float _recalledAt = float.NaN; // NaN = none; quiet for RecallQuietSeconds after it
    // For HoldLateCharge.RecoverReason.
    private int _infantryPeak;
    private int _engagedMen;

    /// <summary>Bumped on every OnApply and re-form: the behaviors restart their hold.</summary>
    private int _applyGeneration;

    public RBMTacticHedgehog(Team team)
        : base(team)
    {
    }

    /// <summary>AI thread: the square's infantry in a local charge (infantry only, never riders).</summary>
    internal bool IsLocalChargeOf(Formation formation)
    {
        return _engaged && formation != null && formation == _mainInfantry && HoldLateCharge.IsLocalChargeReason(_engagedReason);
    }

    protected override void OnApply()
    {
        base.OnApply();
        _applyGeneration++;
        _engaged = false;
        _engagedReason = null;
        _engagedAt = float.NaN;
        _noFootSince = float.NaN;
        _chargeStart = Vec2.Invalid;
        _reformInPlace = false;
        _recalledAt = float.NaN;
        _infantryPeak = 0;
        _engagedMen = 0;
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
        if (_mainInfantry != null)
        {
            _mainInfantry.AI.ResetBehaviorWeights();
            _mainInfantry.AI.SetBehaviorWeight<BehaviorHoldHighGround>(1f);
            _mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
            float quietUntil = float.IsNaN(_recalledAt) ? float.NegativeInfinity : _recalledAt + HoldLateCharge.RecallQuietSeconds;
            RBMBehaviorHedgehog.TryAssign(_mainInfantry, this, _applyGeneration, RBMBehaviorHedgehog.Role.Infantry, null, _reformInPlace, quietUntil);
        }
        HoldSupport();
    }

    /// <summary>Everyone but the infantry (kept through a local charge).</summary>
    private void HoldSupport()
    {
        if (_archers != null)
        {
            _archers.AI.ResetBehaviorWeights();
            _archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
            RBMBehaviorHedgehog.TryAssign(_archers, this, _applyGeneration, RBMBehaviorHedgehog.Role.Archers, _mainInfantry);
        }
        AssignCavalry(_leftCavalry, FormationAI.BehaviorSide.Left, _rightCavalry == null);
        AssignCavalry(_rightCavalry, FormationAI.BehaviorSide.Right, false);
        if (_rangedCavalry != null)
        {
            _rangedCavalry.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(_rangedCavalry);
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
            _rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        }
    }

    /// <summary>The cavalry: ProtectFlank, and in reserve at the battle position's reserve place only when both line ends are covered (a released reserve falls back to the flank guard).</summary>
    private void AssignCavalry(Formation cavalry, FormationAI.BehaviorSide side, bool lone)
    {
        if (cavalry == null)
        {
            return;
        }
        cavalry.AI.ResetBehaviorWeights();
        TacticComponent.SetDefaultBehaviorWeights(cavalry);
        cavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = TerrainFlankPlan.CavalrySide(this, cavalry, side, lone);
        if (TerrainBattlePosition.ReserveWanted(Team))
        {
            RBMBehaviorHoldPosition.TryAssign(cavalry, this, _applyGeneration, RBMBehaviorHoldPosition.Role.Reserve);
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
        if (HoldLateCharge.IsLocalChargeReason(_engagedReason))
        {
            // A LOCAL charge is the infantry's alone (the full Engage would send the weak cavalry into the enemy cavalry); CheckRecall re-forms it.
            HoldSupport();
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
        // Re-apply when the battle position appears or moves.
        if (TerrainBattlePosition.NeedsReapply(this))
        {
            IsTacticReapplyNeeded = true;
        }
        // Only the hedgehog's late charge engages (no HasBattleBeenJoined / distance / melee switch here).
        Mission mission = Mission.Current;
        float now = (mission != null) ? mission.CurrentTime : 0f;
        int infantryMen = (_mainInfantry != null) ? _mainInfantry.CountOfUnits : 0;
        if (infantryMen > _infantryPeak)
        {
            _infantryPeak = infantryMen;
        }
        string reason = "noInfantry";
        bool engageNow = false;
        if (!_engaged)
        {
            if (_mainInfantry == null || _mainInfantry.CountOfUnits == 0)
            {
                engageNow = true;
            }
            else if (!_mainInfantry.IsAIControlled)
            {
                // A player-controlled infantry never ticks the hedgehog behavior; the old battle-joined rule engages the army.
                if (RBMAI.Utilities.HasBattleBeenJoined(_mainInfantry, false, RBMTacticHoldChoke.BattleJoinRange))
                {
                    engageNow = true;
                    reason = PlayerLineWord;
                }
            }
            else
            {
                engageNow = RBMBehaviorHedgehog.ChargedFor(_mainInfantry, this, out reason);
            }
        }
        if (engageNow)
        {
            _engaged = true;
            _engagedReason = reason;
            _engagedAt = now;
            _engagedMen = infantryMen;
            _noFootSince = float.NaN;
            _chargeStart = (_mainInfantry != null && _mainInfantry.CountOfUnits > 0) ? RBMAI.Utilities.GetFormationCenter(_mainInfantry) : Vec2.Invalid;
            IsTacticReapplyNeeded = true;
            TerrainHedgehog.Enqueue(Team, _mainInfantry, now, "Hold->Engage", "squareCharged " + (reason ?? "-")
                + (HoldLateCharge.IsLocalChargeReason(reason) ? " (local: infantry only)" : ""));
        }
        else if (_engaged && HoldLateCharge.IsLocalChargeReason(_engagedReason))
        {
            CheckRecall(mission, now);
        }
        else if (_engaged)
        {
            CheckRecover(mission, now);
        }
        if (IsTacticReapplyNeeded)
        {
            ApplyPhase();
        }
        base.TickOccasionally();
    }

    /// <summary>
    /// AI thread, while engaged: a charge for a contact reason is local; while the enemy cavalry is still a threat the
    /// hedgehog re-forms (Engage->Hold, new generation) on the first of: significant enemy melee cavalry close (at once, out
    /// of melee, no significant foot at the square; in place), the pursuit leash (at once), or no significant enemy foot
    /// within RecallFootGap for RecallSeconds. Leash/no-foot re-form at the battle position within RecallReturnDistance, else in place.
    /// </summary>
    private void CheckRecall(Mission mission, float now)
    {
        string word = _engagedReason;
        if (mission == null || _mainInfantry == null || _mainInfantry.CountOfUnits < HoldLateCharge.LineGoneMen
            || !HoldLateCharge.IsLocalChargeReason(word) || float.IsNaN(_engagedAt))
        {
            _noFootSince = float.NaN;
            return;
        }
        TerrainHedgehog.SidePlan plan = TerrainHedgehog.ForSide(Team.Side);
        // MELEE cavalry only.
        float enemyMounted = TerrainHedgehog.EnemyMeleeCavalryNow(Team);
        if (plan == null || plan.EnemyMounted <= 0 || enemyMounted < RBMBehaviorHedgehog.CavalryGoneShare * plan.EnemyMounted)
        {
            _noFootSince = float.NaN;
            return;
        }
        Vec2 centre = RBMAI.Utilities.GetFormationCenter(_mainInfantry);
        if (!centre.IsValid)
        {
            return;
        }
        float nearestFootOld;
        float nearestFoot = HoldLateCharge.NearestEnemyFootGap(mission, _mainInfantry, out nearestFootOld);
        TerrainBattlePosition.Plan position = TerrainBattlePosition.PlanFor(Team);
        float toHold = (position != null) ? centre.Distance(position.Hold) : float.MaxValue;
        bool farFromPosition = toHold > RecallReturnDistance;

        // At once, out of melee: significant enemy cavalry close (not with enemy foot at the square), or the pursuit leash.
        float cavalryGap = float.MaxValue;
        float cavalryGapOld = float.NaN;
        Formation cavalry = (nearestFoot > HoldLateCharge.ContactGap) ? HoldLateCharge.NearestSignificantEnemyCavalry(mission, _mainInfantry, out cavalryGap, out cavalryGapOld) : null;
        bool cavalryNear = cavalry != null && cavalryGap <= HoldLateCharge.CavalryRecallDistance;
        float leash = _chargeStart.IsValid ? centre.Distance(_chargeStart) : 0f;
        // The leash never pulls the infantry out of a fight (significant enemy foot within ContactGap).
        if (cavalryNear || (leash > HoldLateCharge.LeashDistance && nearestFoot > HoldLateCharge.ContactGap))
        {
            float melee = HoldLateCharge.MeleeShare(_mainInfantry);
            if (melee < HoldLateCharge.ContactMeleeShare)
            {
                if (cavalryNear)
                {
                    // The march halts and squares up in place while the riders are within the behavior's MarchHaltCavalry.
                    Recall(now, farFromPosition, "recall cavalry " + ThreatSignificance.Describe(_mainInfantry, cavalry) + " gap=" + FormationGeometry.Shadow(cavalryGap, cavalryGapOld)
                        + " (<=" + TerrainHedgehog.Fmt0(HoldLateCharge.CavalryRecallDistance) + ") " + word + " charge melee=" + TerrainHedgehog.Fmt2(melee)
                        + " foot=" + FootText(nearestFoot, nearestFootOld) + MountedText(enemyMounted, plan) + ReformText(farFromPosition, toHold), true);
                }
                else
                {
                    Recall(now, farFromPosition, "recall leash " + word + " charge: " + TerrainHedgehog.Fmt0(leash) + "m from its start (>"
                        + TerrainHedgehog.Fmt0(HoldLateCharge.LeashDistance) + ") melee=" + TerrainHedgehog.Fmt2(melee) + " foot=" + FootText(nearestFoot, nearestFootOld)
                        + MountedText(enemyMounted, plan) + ReformText(farFromPosition, toHold), true);
                }
                return;
            }
        }

        if (now - _engagedAt < HoldLateCharge.RecallMinCharge || nearestFoot <= HoldLateCharge.RecallFootGap)
        {
            _noFootSince = float.NaN;
            return;
        }
        if (float.IsNaN(_noFootSince))
        {
            _noFootSince = now;
        }
        if (now - _noFootSince < HoldLateCharge.RecallSeconds)
        {
            return;
        }
        Recall(now, farFromPosition,
            "recall " + word + " charge: no enemy foot within " + TerrainHedgehog.Fmt0(HoldLateCharge.RecallFootGap) + "m for "
            + TerrainHedgehog.Fmt0(HoldLateCharge.RecallSeconds) + "s (nearest foot=" + FootText(nearestFoot, nearestFootOld) + ")" + MountedText(enemyMounted, plan)
            + ReformText(farFromPosition, toHold), true);
    }

    /// <summary>AI thread, while a NON-local engage is latched: re-forms (Engage->Hold) once reinforcements brought the infantry back and the enemy melee cavalry is still a threat.</summary>
    private void CheckRecover(Mission mission, float now)
    {
        TerrainHedgehog.SidePlan plan = TerrainHedgehog.ForSide(Team.Side);
        if (mission == null || plan == null || plan.EnemyMounted <= 0 || _mainInfantry == null)
        {
            return;
        }
        float enemyMounted = TerrainHedgehog.EnemyMeleeCavalryNow(Team);
        if (enemyMounted < RBMBehaviorHedgehog.CavalryGoneShare * plan.EnemyMounted)
        {
            return;
        }
        Vec2 centre = RBMAI.Utilities.GetFormationCenter(_mainInfantry);
        TerrainBattlePosition.Plan position = TerrainBattlePosition.PlanFor(Team);
        float toHold = (position != null && centre.IsValid) ? centre.Distance(position.Hold) : float.MaxValue;
        bool farFromPosition = toHold > RecallReturnDistance;
        if (_engagedReason == PlayerLineWord)
        {
            if (_mainInfantry.CountOfUnits > 0 && _mainInfantry.IsAIControlled)
            {
                Recall(now, farFromPosition, "recall " + PlayerLineWord + ": the infantry is AI-controlled again" + MountedText(enemyMounted, plan) + ReformText(farFromPosition, toHold), false);
            }
            return;
        }
        string recover = HoldLateCharge.RecoverReason(mission, _mainInfantry, _infantryPeak, _engagedMen);
        if (recover != null)
        {
            Recall(now, farFromPosition, recover + " was " + (_engagedReason ?? "-") + MountedText(enemyMounted, plan) + ReformText(farFromPosition, toHold), false);
            return;
        }
        if (_engagedReason != "outshot" || _mainInfantry.CountOfUnits < HoldLateCharge.LineGoneMen || float.IsNaN(_engagedAt)
            || now - _engagedAt < HoldLateCharge.RecallMinCharge)
        {
            _noFootSince = float.NaN;
            return;
        }
        // Out-shot charge: re-form once no significant enemy foot is within RecallFootGap and no shooters within OutshotStandoff for RecallSeconds.
        string calm = HoldLateCharge.CalmReason(mission, _mainInfantry, true);
        if (calm == null)
        {
            _noFootSince = float.NaN;
            return;
        }
        if (float.IsNaN(_noFootSince))
        {
            _noFootSince = now;
        }
        if (now - _noFootSince >= HoldLateCharge.RecallSeconds)
        {
            Recall(now, farFromPosition, "recall outshot engage: calm " + calm + " for " + TerrainHedgehog.Fmt0(HoldLateCharge.RecallSeconds) + "s"
                + MountedText(enemyMounted, plan) + ReformText(farFromPosition, toHold), true);
        }
    }

    private const string PlayerLineWord = "playerLine";

    private static string FootText(float nearestFoot, float old)
    {
        return FormationGeometry.Shadow(nearestFoot, old);
    }

    private static string MountedText(float enemyMounted, TerrainHedgehog.SidePlan plan)
    {
        return " mounted=" + TerrainHedgehog.Fmt0(enemyMounted) + " of " + plan.EnemyMounted.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ReformText(bool inPlace, float toHold)
    {
        return inPlace ? " reform inPlace" : " reform at position " + TerrainHedgehog.Fmt0(toHold) + "m";
    }

    /// <summary>AI thread: Engage->Hold (new generation), logged with why; quiet (a recall after a local charge) keeps the square quiet for RecallQuietSeconds.</summary>
    private void Recall(float now, bool inPlace, string why, bool quiet)
    {
        _reformInPlace = inPlace;
        _engaged = false;
        _engagedReason = null;
        _engagedAt = float.NaN;
        _noFootSince = float.NaN;
        _chargeStart = Vec2.Invalid;
        _recalledAt = quiet ? now : float.NaN;
        _applyGeneration++;
        IsTacticReapplyNeeded = true;
        TerrainHedgehog.Enqueue(Team, _mainInfantry, now, "Engage->Hold",
            why + (quiet ? " quiet " + TerrainHedgehog.Fmt0(HoldLateCharge.RecallQuietSeconds) + "s" : "")
            + " men=" + ((_mainInfantry != null) ? _mainInfantry.CountOfUnits : 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    protected override float GetTacticWeight()
    {
        TeamQuerySystem q = Team?.QuerySystem;
        if (q == null || Team.Side != BattleSideEnum.Defender)
        {
            return 0.01f;
        }
        TerrainHedgehog.SidePlan plan = TerrainHedgehog.ForSide(Team.Side);
        if (plan == null || !plan.Fit)
        {
            return 0.01f;
        }
        // The enemy's MELEE cavalry is mostly gone: no reason left for a hedgehog.
        float enemyMounted = TerrainHedgehog.EnemyMeleeCavalryNow(Team);
        if (plan.EnemyMounted > 0 && enemyMounted < RBMBehaviorHedgehog.CavalryGoneShare * plan.EnemyMounted)
        {
            return 0.01f;
        }
        float threat = 0.5f * MBMath.ClampFloat((plan.EnemyMountedShare - TerrainHedgehog.HedgehogEnemyCav) / ThreatShareSpan, 0f, 1f)
            + 0.5f * MBMath.ClampFloat((plan.CavPowerRatio - TerrainHedgehog.HedgehogCavPowerRatio) / ThreatPowerSpan, 0f, 1f);
        float pikes = MBMath.ClampFloat((plan.PikeShare - TerrainHedgehog.HedgehogPikeShare) / PikeSpan, 0f, 1f);
        float weight = MBMath.Lerp(0.6f, 1.2f, 0.6f * threat + 0.4f * pikes) / MathF.Sqrt(MathF.Max(q.RemainingPowerRatio, 0.05f));
        return MathF.Max(0.01f, weight);
    }
}
