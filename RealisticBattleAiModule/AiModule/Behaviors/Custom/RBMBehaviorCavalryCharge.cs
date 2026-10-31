using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

/// <summary>
/// Cavalry charge as a four-state loop: Undetermined -> Charging -> ChargingPast -> Reforming -> Charging ...
///
/// Undetermined: plain charge order until the closest enemy is within StartChargeTimeToContactSeconds.
/// Charging: pick a target once per charge (AcquireTarget; only significant formations while any exist, chase-timeout
///   exclusions), form up to its width, ChargeToTarget. "Through" once the target centre is behind us, then
///   ChargingPast after ChargeThroughGraceSeconds; real melee contact for ChargeContactTimeoutSeconds also ends it.
///   FORMED APPROACH (TickChargeApproach, AI only): while the target is farther than FormedApproach.GoDistance the charge is a Move in Line to
///   the target's near edge at full pace; the ChargeToTarget "go" comes within it, in melee, or earlier against closing riders.
///   The through / contact timers start only at the go.
/// ChargingPast: ride on to a reform point; a cohesive wedge with enough run-up skips the reform and charges again.
/// Reforming: hold the reform point until the wedge is cohesive (counted over all riders) or ReformTimeoutSeconds; under fire
///   only once regrouped or after ReformMinHoldSeconds.
///
/// Reform point: CavalryReform picks it from the live situation (through / left / right / back, enemies and friends first),
/// reachable before ChargingPastTimeoutSeconds, re-checked every 2 s; none usable = skip the reform. While reforming only, a
/// significant enemy within ReformThreatDistance (or closing, IsEnemyClosingIn) ends the reform.
///
/// Terrain (TerrainCavalry, terrainTacticsEnabled): moves only the target scoring and the lane approach, never states or
/// timers: bad-ground targets x3, lane targets x0.85; a lane's run-up point is ridden to before the charge (TickLaneApproach).
/// </summary>
public class RBMBehaviorCavalryCharge : BehaviorComponent
{
    private enum ChargeState
    {
        Undetermined,
        Charging,
        ChargingPast,
        Reforming
    }

    private const float MinChargeWidth = 20f; // m; the charge copies the target's width
    private const float ChargeStopDistance = 140f;
    private const float ChargeStopDistanceEmbolon = 70f;
    private const float ChargeCoherence = 0.5f;
    private const float ChargeCoherenceEmbolon = 0.85f;
    private const float StartChargeTimeToContactSeconds = 5f;
    private const float ChargeThroughGraceSeconds = 3f;
    private const float ChargeContactTimeoutSeconds = 8f;
    private const float ContactMeleeRecentSeconds = 3f;
    private const float ContactMeleeMinRatio = 0.2f;
    // Target pick (AcquireTarget): lowest distance / (type weight * power weight) wins; cavalry, then horse archers, archers, infantry.
    private const float TargetWeightCavalry = 4f;
    private const float TargetWeightHorseArchers = 2f;
    private const float TargetWeightArchers = 1f;
    private const float TargetWeightInfantry = 0.5f;
    // Power weight = sqrt(enemy / own FormationPower), clamped
    private const float TargetPowerWeightMin = 0.3f;
    private const float TargetPowerWeightMax = 1.25f;
    // Facing weight for foot formations: 1 facing us, 1 + TargetRearBonus facing away, linear between. Riders don't get it.
    private const float TargetRearBonus = 1f;
    // Chase = no contact for ChaseTimeoutSeconds and no closing by ChaseMinClosing; the target is excluded for ChaseExcludeSeconds (taken anyway when the only one).
    private const float ChaseTimeoutSeconds = 20f;
    private const float ChaseMinClosing = 30f; // m
    private const float ChaseExcludeSeconds = 20f;
    // no chase timeout while the target is this close (edge gap) or while we are scattered (a new target would scatter us more)
    private const float ChaseNearGap = 80f; // m
    private const float ChaseScatteredDevFactor = 3f; // x idealDisp
    // at least MinChargeRanks deep: the width never copies a wide target into one rank; re-set only past the dead band
    private const int MinChargeRanks = 2;
    private const float ChargeWidthDeadBand = 5f; // m
    private const float ChargingPastTimeoutSeconds = 19f;
    // a reform point is taken only when reachable before ChargingPastTimeoutSeconds at this share of top speed, and never farther than ReformMaxRide
    private const float ReformRideTimeShare = 0.75f;
    private const float ReformMaxRide = 150f; // m
    // Reforming: under fire it re-charges only once regrouped (dev <= factor x idealDisp, or cohesive) or after the minimum hold
    private const float ReformRegroupDevFactor = 2f;
    private const float ReformMinHoldSeconds = 3f;
    private const float ReformTimeoutSeconds = 10f;
    // a back point (up to CavalryReform.BackMaxRide) gets the time to ride the rest of the way on top, at most this long in all
    private const float ReformBackMaxTimeoutSeconds = 30f;
    private const float ReformCohesionRadius = 20f;
    private const float ReformCohesionMinRatio = 0.9f;
    private const float RechargeMinRunUpDistance = 100f;
    private const float ReformAbortRangedAttackRatio = 0.2f;
    private const float ReformThreatDistance = 35f;
    private const float ReformApproachDistanceFactor = 2f;
    private const float ReformApproachMinSpeedSq = 1f;
    private const float ReformApproachMinDot = 0.7f;
    // Formed approach (USER RULE 2026-10-30): go rule, lead and constants shared with RBMBehaviorReact in RBMAI.FormedApproach.
    // A go is kept until the gap stays above ChargeKeepDistance for ChargeKeepSeconds without melee or contact.
    private const float ChargeKeepDistance = RBMAI.FormedApproach.KeepDistance;
    private const float ChargeKeepSeconds = RBMAI.FormedApproach.KeepSeconds;
    private const float ApproachFacingTurnCos = RBMAI.FormedApproach.FacingTurnCos;

    private ChargeState _chargeState;

    private FormationQuerySystem _lastTarget;

    private Vec2 _initialChargeDirection;

    private bool _isEmbolonTactic;

    private bool _hasNewTarget;

    private readonly RBMAI.CavalryReform _reform = new RBMAI.CavalryReform();

    private Timer _chargingPastTimer;

    private Timer _reformTimer;

    private float _reformSince = float.NaN;

    // why the last ChargingPast / Reforming ended (reform release log)
    private string _rechargeWhy;

    private Timer _chargeTimer;

    private Timer _contactTimer;

    private bool _laneApproach;

    // formed approach: _go = this charge's ChargeToTarget was let go; _approaching / _approachTarget are read from any thread
    private bool _go;
    private volatile bool _approaching;
    private volatile Formation _approachTarget;
    private float _approachSince;
    private float _farSince = float.NaN;
    private Vec2 _approachFacing = Vec2.Invalid;
    private Formation _approachPointTarget;
    private readonly RBMAI.CachedMovePoint _approachPoint = new RBMAI.CachedMovePoint();

    private float _chaseSince = float.NaN;
    private float _chaseDistance;

    // Exclusion is per target and escalates (doubled per timeout up to ChaseExcludeMaxSeconds); after two timeouts within
    // ChaseFootOnlyWindow s only foot targets for ChaseFootOnlySeconds (filters dropped if they would leave nothing). AI thread.
    private sealed class ChaseExclusion
    {
        public float Until;
        public int Count;
    }

    private readonly Dictionary<Formation, ChaseExclusion> _chaseExclusions = new Dictionary<Formation, ChaseExclusion>();
    private float _lastChaseTimeoutAt = float.NaN;
    private float _footOnlyUntil = float.NaN;
    private const float ChaseExcludeMaxSeconds = 80f;
    private const float ChaseFootOnlyWindow = 60f;
    private const float ChaseFootOnlySeconds = 60f;

    private const string TerrainBehaviorName = "CavalryCharge";

    private static readonly PropertyInfo CurrentTacticProperty = typeof(TeamAIComponent).GetProperty("CurrentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo CurrentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);

    private static TacticComponent GetCurrentTactic(TeamAIComponent teamAi)
    {
        if (teamAi == null)
        {
            return null;
        }
        if (CurrentTacticProperty != null)
        {
            return CurrentTacticProperty.GetValue(teamAi) as TacticComponent;
        }
        return CurrentTacticField?.GetValue(teamAi) as TacticComponent;
    }

    public bool ChargeArchers = true;
    public bool ChargeInfantry = true;
    public bool ChargeCavalry = true;
    public bool ChargeHorseArchers = true;

    public override float NavmeshlessTargetPositionPenalty => 1f;

    private float ChargeStopDistanceForTactic => _isEmbolonTactic ? ChargeStopDistanceEmbolon : ChargeStopDistance;

    /// <summary>The formation it charges / last charged (ReactionMonitor: "already answering", R4 skip), or null. AI thread.</summary>
    internal Formation TargetFormation => _lastTarget?.Formation;

    /// <summary>Reforming or without a decided charge: free for a ReactionMonitor task whatever its target. AI thread.</summary>
    internal bool IsReformingOrIdle => _chargeState == ChargeState.Reforming || _chargeState == ChargeState.Undetermined;

    /// <summary>The reform point's kind (through / left / right / back) while riding to or holding one, else null (map label).</summary>
    internal string ReformKindName => (_chargeState == ChargeState.ChargingPast || _chargeState == ChargeState.Reforming) ? _reform.KindName : null;

    /// <summary>Any thread: the target an AI formation rides at in this behavior's formed approach (its order is a Move), else null.</summary>
    internal static Formation FormedApproachTarget(Formation formation)
    {
        RBMBehaviorCavalryCharge charge = (formation != null && formation.IsAIControlled && formation.AI != null) ? formation.AI.ActiveBehavior as RBMBehaviorCavalryCharge : null;
        return (charge != null && charge._approaching) ? charge._approachTarget : null;
    }

    public RBMBehaviorCavalryCharge(Formation formation)
        : base(formation)
    {
        _lastTarget = null;
        base.CurrentOrder = MovementOrder.MovementOrderCharge;
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        _chargeState = ChargeState.Charging;
        base.BehaviorCoherence = ChargeCoherence;
    }

    private void RefreshTacticSettings()
    {
        _isEmbolonTactic = GetCurrentTactic(base.Formation?.Team?.TeamAI) is RBMTacticEmbolon;
        base.BehaviorCoherence = _isEmbolonTactic ? ChargeCoherenceEmbolon : ChargeCoherence;
    }

    public override void TickOccasionally()
    {
        base.TickOccasionally();
        if (base.Formation.AI.ActiveBehavior == this)
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder( CurrentFacingOrder);
        }
    }

    private ChargeState DecideNextState()
    {
        RefreshTacticSettings();
        ChargeState result = _chargeState;
        if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
        {
            result = ChargeState.Undetermined;
        }
        else
        {
            switch (_chargeState)
            {
                case ChargeState.Undetermined:
                    {
                        if ((!base.Formation.QuerySystem.IsCavalryFormation && !base.Formation.QuerySystem.IsRangedCavalryFormation) || RBMAI.Utilities.GetFormationDistance(base.Formation, base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation) / base.Formation.QuerySystem.MovementSpeedMaximum <= StartChargeTimeToContactSeconds)
                        {
                            result = ChargeState.Charging;
                        }
                        break;
                    }
                case ChargeState.Charging:
                    {
                        if (IsTargetGone())
                        {
                            _hasNewTarget = true;
                            break;
                        }
                        if (_laneApproach)
                        {
                            // still riding to the lane's run-up point: the charge has not started
                            break;
                        }
                        Vec2 toTarget =RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation);
                        if (_approaching)
                        {
                            // formed approach: no through / contact timers before the go, only the chase window
                            if (IsChaseTimedOut(toTarget.Length, IsChaseHeld()))
                            {
                                _hasNewTarget = true;
                            }
                            break;
                        }
                        bool passedThrough = _initialChargeDirection.DotProduct(toTarget) <= 0f;
                        bool inContact = toTarget.Length <= ContactDistance(_lastTarget.Formation);
                        if (passedThrough && _chargeTimer == null)
                        {
                            _chargeTimer = new Timer(Mission.Current.CurrentTime, ChargeThroughGraceSeconds);
                        }
                        if (inContact && _contactTimer == null && IsFightingInMelee())
                        {
                            _contactTimer = new Timer(Mission.Current.CurrentTime, ChargeContactTimeoutSeconds);
                        }
                        bool chargeOver = _chargeTimer != null && _chargeTimer.Check(Mission.Current.CurrentTime);
                        bool stalledInMelee = _contactTimer != null && _contactTimer.Check(Mission.Current.CurrentTime);
                        if (chargeOver || stalledInMelee)
                        {
                            result = ChargeState.ChargingPast;
                            break;
                        }
                        if (IsChaseTimedOut(toTarget.Length, inContact || _contactTimer != null || _chargeTimer != null || IsChaseHeld()))
                        {
                            _hasNewTarget = true;
                        }
                        break;
                    }
                case ChargeState.ChargingPast:
                    {
                        if (IsTargetGone())
                        {
                            _rechargeWhy = "targetGone";
                            result = ChargeState.Charging;
                            break;
                        }
                        float distToTarget = RBMAI.Utilities.GetFormationDistance(base.Formation, _lastTarget.Formation);
                        if (IsReadyToRecharge(distToTarget))
                        {
                            _rechargeWhy = "ready";
                            result = ChargeState.Charging;
                        }
                        else if (distToTarget >= (ChargeStopDistanceForTactic + _lastTarget.Formation.Depth))
                        {
                            result = ChargeState.Reforming;
                        }
                        else if (_chargingPastTimer.Check(Mission.Current.CurrentTime))
                        {
                            result = ChargeState.Reforming;
                        }
                        break;
                    }
                case ChargeState.Reforming:
                    {
                        if (IsTargetGone())
                        {
                            _rechargeWhy = "targetGone";
                            result = ChargeState.Charging;
                            break;
                        }
                        float distToEnemy = RBMAI.Utilities.GetFormationDistance(base.Formation, _lastTarget.Formation);
                        bool underFire = base.Formation.QuerySystem.UnderRangedAttackRatio > ReformAbortRangedAttackRatio;
                        string why = IsEnemyClosingIn() ? "enemyClose"
                            : _reformTimer.Check(Mission.Current.CurrentTime) ? "timeout"
                            : IsReadyToRecharge(distToEnemy) ? "ready"
                            // under fire: not while still scattered, unless it has held the minimum
                            : (underFire && (Mission.Current.CurrentTime - _reformSince >= ReformMinHoldSeconds || IsRegrouped())) ? "underFire"
                            : null;
                        if (why != null)
                        {
                            _rechargeWhy = why;
                            result = ChargeState.Charging;
                        }
                        break;
                    }
            }
        }
        return result;
    }

    private bool IsTargetGone()
    {
        return _lastTarget?.Formation == null || _lastTarget.Formation.CountOfUnits == 0;
    }

    private void ResetChase()
    {
        _chaseSince = Mission.Current.CurrentTime;
        _chaseDistance = _lastTarget?.Formation != null
            ? (RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation)).Length
            : 0f;
    }

    // True = timed out, target excluded; contact or closing restarts the window.
    private bool IsChaseTimedOut(float distance, bool engaged)
    {
        float now = Mission.Current.CurrentTime;
        if (float.IsNaN(_chaseSince) || engaged)
        {
            _chaseSince = now;
            _chaseDistance = distance;
            return false;
        }
        if (now - _chaseSince < ChaseTimeoutSeconds)
        {
            return false;
        }
        if (_chaseDistance - distance >= ChaseMinClosing)
        {
            _chaseSince = now;
            _chaseDistance = distance;
            return false;
        }
        Formation excluded = _lastTarget?.Formation;
        float excludeSeconds = ChaseExcludeSeconds;
        int count = 1;
        if (excluded != null)
        {
            ChaseExclusion exclusion;
            if (!_chaseExclusions.TryGetValue(excluded, out exclusion))
            {
                exclusion = new ChaseExclusion();
                _chaseExclusions[excluded] = exclusion;
            }
            exclusion.Count++;
            count = exclusion.Count;
            int doublings = (count > 3) ? 2 : count - 1;
            excludeSeconds = MathF.Min(ChaseExcludeMaxSeconds, ChaseExcludeSeconds * (1 << doublings));
            exclusion.Until = now + excludeSeconds;
        }
        bool footOnly = !float.IsNaN(_lastChaseTimeoutAt) && now - _lastChaseTimeoutAt <= ChaseFootOnlyWindow;
        _lastChaseTimeoutAt = now;
        if (footOnly)
        {
            _footOnlyUntil = now + ChaseFootOnlySeconds;
        }
        RBMAI.TerrainCavalry.NoteEvent(this, TerrainBehaviorName, "chaseTimeout", RBMAI.TerrainCavalry.FormationName(excluded)
            + " " + RBMAI.TerrainCavalry.Fmt0(now - _chaseSince) + "s dist " + RBMAI.TerrainCavalry.Fmt0(_chaseDistance) + "->" + RBMAI.TerrainCavalry.Fmt0(distance)
            + " excluded " + RBMAI.TerrainCavalry.Fmt0(excludeSeconds) + "s (x" + count + ")"
            + (footOnly ? " footOnly " + RBMAI.TerrainCavalry.Fmt0(ChaseFootOnlySeconds) + "s" : ""));
        _chaseSince = float.NaN;
        return true;
    }

    // counted as engaged (window restarts): the target is near, or we are scattered
    private bool IsChaseHeld()
    {
        return RBMAI.FormationGeometry.Gap(base.Formation, RBMAI.FootprintMode.ScatterAware, _lastTarget.Formation, RBMAI.FootprintMode.ScatterAware) <= ChaseNearGap
            || Deviation() > ChaseScatteredDevFactor * IdealDisplacement();
    }

    private float Deviation()
    {
        return base.Formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents;
    }

    private float IdealDisplacement()
    {
        return base.Formation.QuerySystem.IdealAverageDisplacement;
    }

    private bool IsRegrouped()
    {
        return Deviation() <= ReformRegroupDevFactor * IdealDisplacement() || IsFormationCohesive();
    }

    private bool IsChaseExcluded(Formation candidate, float now, bool footOnly)
    {
        if (footOnly && (candidate.QuerySystem.IsCavalryFormation || candidate.QuerySystem.IsRangedCavalryFormation))
        {
            return true;
        }
        ChaseExclusion exclusion;
        return _chaseExclusions.TryGetValue(candidate, out exclusion) && now < exclusion.Until;
    }

    private float ContactDistance(Formation target)
    {
        return (target.Width + target.Depth + base.Formation.Depth) * 0.5f;
    }

    // LastMeleeHitTime / LastRecievedMeleeHitTime are set only when a blow lands: riders actually trading blows
    private bool IsFightingInMelee()
    {
        float now = Mission.Current.CurrentTime;
        int total = 0;
        int fighting = 0;
        base.Formation.ApplyActionOnEachUnit(agent =>
        {
            total++;
            if (now - agent.LastMeleeHitTime < ContactMeleeRecentSeconds || now - agent.LastRecievedMeleeHitTime < ContactMeleeRecentSeconds)
            {
                fighting++;
            }
        });
        return total > 0 && fighting >= total * ContactMeleeMinRatio;
    }

    private bool IsReadyToRecharge(float distToTarget)
    {
        return distToTarget >= RechargeMinRunUpDistance && IsFormationCohesive();
    }

    private bool IsFormationCohesive()
    {
        Vec2 center = RBMAI.Utilities.GetFormationCenter(base.Formation);
        float radius = ReformCohesionRadius + MathF.Max(base.Formation.Width, base.Formation.Depth) * 0.5f;
        float radiusSq = radius * radius;
        int total = 0;
        int near = 0;
        base.Formation.ApplyActionOnEachUnit(agent =>
        {
            if (agent.IsDetachedFromFormation || agent.IsRunningAway)
            {
                return;
            }
            total++;
            if (agent.Position.AsVec2.DistanceSquared(center) <= radiusSq)
            {
                near++;
            }
        });
        return total > 0 && near >= total * ReformCohesionMinRatio;
    }

    private void AcquireTarget()
    {
        Formation enemy = null;
        Mission mission = Mission.Current;
        if (mission != null)
        {
            float ownPower = MathF.Max(0.01f, base.Formation.QuerySystem.FormationPower);
            float bestScore = float.MaxValue;
            // the pick without the terrain factor, to log when the ground changed it
            Formation plain = null;
            float plainScore = float.MaxValue;
            string pickNote = null;
            string plainNote = null;
            // filters (each dropped when it would leave nothing): chase-timeout targets, then formations not significant to us
            float now = mission.CurrentTime;
            // chase-timeout targets and, in a foot-only window, every mounted one
            bool footOnly = !float.IsNaN(_footOnlyUntil) && now < _footOnlyUntil;
            bool useExclusions = true;
            bool anyOther = false;
            bool anySignificant = false;
            foreach (Formation candidate in OtherFormations(mission, enemiesOnly: true))
            {
                if (TargetTypeWeight(candidate.QuerySystem) <= 0f || IsChaseExcluded(candidate, now, footOnly))
                {
                    continue;
                }
                anyOther = true;
                if (RBMAI.ThreatSignificance.IsSignificant(base.Formation, candidate))
                {
                    anySignificant = true;
                }
            }
            if (!anyOther)
            {
                useExclusions = false;
                foreach (Formation candidate in OtherFormations(mission, enemiesOnly: true))
                {
                    if (TargetTypeWeight(candidate.QuerySystem) > 0f && RBMAI.ThreatSignificance.IsSignificant(base.Formation, candidate))
                    {
                        anySignificant = true;
                        break;
                    }
                }
            }
            foreach (Formation candidate in OtherFormations(mission, enemiesOnly: true))
            {
                float typeWeight = TargetTypeWeight(candidate.QuerySystem);
                if (typeWeight <= 0f || (useExclusions && IsChaseExcluded(candidate, now, footOnly)))
                {
                    continue;
                }
                if (anySignificant && !RBMAI.ThreatSignificance.IsSignificant(base.Formation, candidate))
                {
                    RBMAI.TerrainCavalry.NoteTarget(this, TerrainBehaviorName, "insignificant", RBMAI.TerrainCavalry.FormationName(candidate) + " skipped");
                    continue;
                }
                float powerWeight = MBMath.ClampFloat(MathF.Sqrt(candidate.QuerySystem.FormationPower / ownPower), TargetPowerWeightMin, TargetPowerWeightMax);
                float facingWeight = TargetFacingWeight(candidate);
                float score = RBMAI.Utilities.GetFormationDistance(base.Formation, candidate) / (typeWeight * powerWeight * facingWeight);
                // terrain (1 with the toggle off): bad ground scores worse, a lane target better
                string note;
                float terrainFactor = RBMAI.TerrainCavalry.TargetFactor(base.Formation.Team, candidate, out note);
                if (score < plainScore)
                {
                    plainScore = score;
                    plain = candidate;
                    plainNote = note;
                }
                if (note != null && note.StartsWith("badGround"))
                {
                    RBMAI.TerrainCavalry.NoteTarget(this, TerrainBehaviorName, "badGroundTarget", RBMAI.TerrainCavalry.FormationName(candidate)
                        + " score x" + RBMAI.TerrainCavalry.Fmt1(terrainFactor));
                }
                score *= terrainFactor;
                if (score < bestScore)
                {
                    bestScore = score;
                    enemy = candidate;
                    pickNote = note;
                }
            }
            if (enemy != plain && enemy != null && plain != null)
            {
                RBMAI.TerrainCavalry.NoteTarget(this, TerrainBehaviorName, "target", "pick=" + RBMAI.TerrainCavalry.FormationName(enemy) + "(" + (pickNote ?? "-")
                    + ") instead of " + RBMAI.TerrainCavalry.FormationName(plain) + "(" + (plainNote ?? "-") + ")");
            }
        }

        _lastTarget = enemy != null ? enemy.QuerySystem : base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
        _hasNewTarget = true;
        if (_lastTarget?.Formation != null)
        {
            _initialChargeDirection = RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation);
        }
    }

    // Formation.Direction is the facing its facing order sets
    private float TargetFacingWeight(Formation enemy)
    {
        FormationQuerySystem query = enemy.QuerySystem;
        if (query.IsCavalryFormation || query.IsRangedCavalryFormation)
        {
            return 1f;
        }
        Vec2 toUs = RBMAI.Utilities.GetFormationCenter(base.Formation) - RBMAI.Utilities.GetFormationCenter(enemy);
        Vec2 facing = enemy.Direction;
        if (toUs.LengthSquared < 0.01f || facing.LengthSquared < 0.01f)
        {
            return 1f;
        }
        float dot = MBMath.ClampFloat(Vec2.DotProduct(facing.Normalized(), toUs.Normalized()), -1f, 1f);
        return 1f + TargetRearBonus * (1f - dot) * 0.5f;
    }

    // 0 = not a charge target (Charge* flag off, or no class)
    private float TargetTypeWeight(FormationQuerySystem enemy)
    {
        if (enemy.IsRangedCavalryFormation)
        {
            return ChargeHorseArchers ? TargetWeightHorseArchers : 0f;
        }
        if (enemy.IsCavalryFormation)
        {
            return ChargeCavalry ? TargetWeightCavalry : 0f;
        }
        if (enemy.IsRangedFormation)
        {
            return ChargeArchers ? TargetWeightArchers : 0f;
        }
        if (enemy.IsInfantryFormation)
        {
            return ChargeInfantry ? TargetWeightInfantry : 0f;
        }
        return 0f;
    }

    protected override void CalculateCurrentOrder()
    {
        if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
        {
            _approaching = false;
            base.CurrentOrder = MovementOrder.MovementOrderCharge;
            return;
        }

        if (WearDownHolds())
        {
            _approaching = false;
            GuardOrder();
            return;
        }
        if (_wearReopened)
        {
            // the gate opened: a fresh charge (timers and target from before the hold are stale)
            _wearReopened = false;
            EnterState(ChargeState.Charging);
        }

        ChargeState nextState = DecideNextState();
        if (nextState != _chargeState || _hasNewTarget)
        {
            EnterState(nextState);
        }

        if (_chargeState == ChargeState.Charging && _laneApproach)
        {
            TickLaneApproach();
        }

        if (_chargeState == ChargeState.ChargingPast || _chargeState == ChargeState.Reforming)
        {
            TickReformMovement();
        }

        // last: the reform tick may start a new charge
        if (_chargeState == ChargeState.Charging && !_laneApproach)
        {
            TickChargeApproach();
        }
    }

    private void EnterState(ChargeState state)
    {
        _chargeState = state;
        _approaching = false;
        switch (state)
        {
            case ChargeState.Undetermined:
                EnterUndetermined();
                break;
            case ChargeState.Charging:
                EnterCharging();
                break;
            case ChargeState.ChargingPast:
                EnterChargingPast();
                break;
            case ChargeState.Reforming:
                EnterReforming();
                break;
        }
        _hasNewTarget = false;
    }

    private void EnterUndetermined()
    {
        _laneApproach = false;
        RBMAI.TerrainCavalry.Release(this, "undetermined");
        _reform.Release(this, TerrainBehaviorName, "undetermined");
        base.CurrentOrder = MovementOrder.MovementOrderCharge;
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    private void EnterCharging()
    {
        _chargeTimer = null;
        _contactTimer = null;
        _go = false;
        _farSince = float.NaN;
        // a new charge gives up any reform point / lane
        if (_reform.Holding)
        {
            _reform.Release(this, TerrainBehaviorName, "newCharge(" + (_rechargeWhy ?? "-") + " dev=" + RBMAI.TerrainCavalry.Fmt0(Deviation())
                + "/" + RBMAI.TerrainCavalry.Fmt0(IdealDisplacement()) + (float.IsNaN(_reformSince) ? "" : " held=" + RBMAI.TerrainCavalry.Fmt0(Mission.Current.CurrentTime - _reformSince) + "s") + ")");
        }
        _rechargeWhy = null;
        _reformSince = float.NaN;
        _laneApproach = false;
        RBMAI.TerrainCavalry.Release(this, "newCharge");
        AcquireTarget();
        ResetChase();
        if (_lastTarget?.Formation == null)
        {
            base.CurrentOrder = MovementOrder.MovementOrderCharge;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            return;
        }
        // also reached for player-led formations (CalculateCurrentOrder)
        if (base.Formation.IsAIControlled)
        {
            // never narrower than MinChargeWidth (a tiny target would pack the cavalry into a deep block), never under MinChargeRanks deep
            float width = MathF.Min(MathF.Max(MinChargeWidth, _lastTarget.Formation.Width), MaxChargeWidth());
            FormOrder current = base.Formation.FormOrder;
            if (current.OrderEnum != FormOrder.FormOrderEnum.Custom || MathF.Abs(current.CustomFlankWidth - width) > ChargeWidthDeadBand)
            {
                base.Formation.SetFormOrder(FormOrder.FormOrderCustom(width));
            }
        }
        Vec2 chargeDirection = (RBMAI.Utilities.GetFormationCenter(_lastTarget.Formation) - RBMAI.Utilities.GetFormationCenter(base.Formation)).Normalized();
        // terrain: ride to a lane's run-up point first (TickLaneApproach)
        RBMAI.TerrainCavalry.Lane lane;
        RBMAI.TerrainCavalry.LaneEntry entry;
        if (base.Formation.IsAIControlled && RBMAI.TerrainCavalry.TryTakeLane(this, TerrainBehaviorName, _lastTarget.Formation, out lane, out entry))
        {
            _laneApproach = true;
            base.CurrentOrder = MovementOrder.MovementOrderMove(entry.World);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(chargeDirection);
            return;
        }
        base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(_lastTarget.Formation);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(chargeDirection);
    }

    // Line width of ceil(riders / MinChargeRanks) files at the Line interval (the charge's arrangement; as RBMBehaviorArcherFlank.NominalLineWidth)
    private float MaxChargeWidth()
    {
        Formation formation = base.Formation;
        float diameter = formation.UnitDiameter;
        float interval = Formation.GetDefaultUnitInterval(formation.CalculateHasSignificantNumberOfMounted && formation.RidingOrder != RidingOrder.RidingOrderDismount,
            ArrangementOrder.GetUnitSpacingOf(ArrangementOrder.ArrangementOrderEnum.Line));
        float files = MathF.Max(1f, (float)MathF.Ceiling(formation.CountOfUnits / (float)MinChargeRanks));
        return MathF.Max(diameter, files * (diameter + interval) - interval);
    }

    // Each lane-approach tick: keep riding, or (arrived, timed out, melee, enemy close, target off the lane, inactive) charge from here with a fresh direction.
    private void TickLaneApproach()
    {
        Formation target = _lastTarget?.Formation;
        RBMAI.TerrainCavalry.Hold hold = RBMAI.TerrainCavalry.HeldBy(this);
        if (hold != null && hold.Entry != null && RBMAI.TerrainCavalry.CheckLaneApproach(this, target, IsFightingInMelee()))
        {
            // read again: a re-plan may have moved the hold during the check
            hold = RBMAI.TerrainCavalry.HeldBy(this) ?? hold;
            if (hold.Entry != null)
            {
                base.CurrentOrder = MovementOrder.MovementOrderMove(hold.Entry.World);
                return;
            }
        }
        _laneApproach = false;
        RBMAI.TerrainCavalry.Release(this, "lost");
        if (target == null || target.CountOfUnits == 0)
        {
            base.CurrentOrder = MovementOrder.MovementOrderCharge;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            return;
        }
        _chargeTimer = null;
        _contactTimer = null;
        ResetChase();
        _initialChargeDirection = RBMAI.Utilities.GetFormationCenter(target) - RBMAI.Utilities.GetFormationCenter(base.Formation);
        base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(target);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(_initialChargeDirection.Normalized());
    }

    // Each Charging tick off a lane: the formed approach (a Move in Line to the target's near edge, led by its velocity, facing it), or the go.
    private void TickChargeApproach()
    {
        Formation target = _lastTarget?.Formation;
        Mission mission = Mission.Current;
        if (target == null || target.CountOfUnits <= 0)
        {
            if (_approaching)
            {
                _approaching = false;
                base.CurrentOrder = MovementOrder.MovementOrderCharge;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            }
            return;
        }
        Vec2 centre = RBMAI.Utilities.GetFormationCenter(base.Formation);
        Vec2 targetCentre = RBMAI.Utilities.GetFormationCenter(target);
        RBMAI.Footprint shape;
        // player formations, sieges and missing geometry keep the plain ChargeToTarget
        if (mission == null || !mission.IsFieldBattle || !base.Formation.IsAIControlled || !centre.IsValid || !targetCentre.IsValid
            || !RBMAI.FormationGeometry.TryGet(target, RBMAI.FootprintMode.Current, out shape))
        {
            if (_approaching)
            {
                _approaching = false;
                base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(target);
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(_initialChargeDirection.Normalized());
            }
            return;
        }
        float now = mission.CurrentTime;
        float gap = RBMAI.FormedApproach.Gap(base.Formation, target);
        bool keepLost = false;
        if (_go)
        {
            if (gap <= ChargeKeepDistance || _chargeTimer != null || _contactTimer != null || IsFightingInMelee())
            {
                _farSince = float.NaN;
                return;
            }
            if (float.IsNaN(_farSince))
            {
                _farSince = now;
            }
            if (now - _farSince < ChargeKeepSeconds)
            {
                return;
            }
            // rode clear of it (it got away) before any contact: formed again
            _go = false;
            keepLost = true;
        }
        else
        {
            string why = GoReason(target, gap, centre, targetCentre);
            if (why != null)
            {
                Go(target, centre, targetCentre, why, now);
                return;
            }
        }

        Vec2 u;
        Vec2 edge;
        if (!RBMAI.FormedApproach.TryLeadEdge(base.Formation, target, shape, centre, targetCentre, gap, out edge, out u))
        {
            Go(target, centre, targetCentre, "gap=0m", now);
            return;
        }
        Vec2 aim = edge - centre;
        Vec2 facing = (aim.LengthSquared > 0.01f) ? aim.Normalized() : u;
        if (_approachPointTarget != target)
        {
            // a new target rebuilds at once and never keeps the old target's point
            _approachPoint.Reset();
            _approachFacing = facing;
        }
        else if (!_approachFacing.IsValid || _approachFacing.DotProduct(facing) < ApproachFacingTurnCos)
        {
            _approachFacing = facing;
        }
        WorldPosition point;
        if (!_approachPoint.TryGet(edge, now, target.CachedMedianPosition, base.Formation.CachedMedianPosition, false, true, false, out point))
        {
            Go(target, centre, targetCentre, "noPoint", now);
            return;
        }
        _approachPointTarget = target;
        base.CurrentOrder = MovementOrder.MovementOrderMove(point);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(_approachFacing);
        _approachTarget = target;
        if (!_approaching)
        {
            _approaching = true;
            _approachSince = now;
            RBMAI.TerrainCavalry.NoteEvent(this, TerrainBehaviorName, "charge advance", RBMAI.TerrainCavalry.FormationName(base.Formation) + " -> "
                + RBMAI.TerrainCavalry.FormationName(target) + " gap=" + RBMAI.TerrainCavalry.Fmt0(gap) + "m" + (keepLost ? " keepLost" : ""));
        }
    }

    // The go rule's log text, null while the formed approach holds.
    private string GoReason(Formation target, float gap, Vec2 centre, Vec2 targetCentre)
    {
        // the melee test walks every rider: only needed past the distance go
        bool ownMelee = gap > RBMAI.FormedApproach.GoDistance && IsFightingInMelee();
        return RBMAI.FormedApproach.GoReason(base.Formation, target, gap, centre, targetCentre, ownMelee);
    }

    // The real ChargeToTarget: the through / contact timers and the through direction start here.
    private void Go(Formation target, Vec2 centre, Vec2 targetCentre, string why, float now)
    {
        bool wasApproaching = _approaching;
        _approaching = false;
        _go = true;
        _farSince = float.NaN;
        _chargeTimer = null;
        _contactTimer = null;
        _initialChargeDirection = targetCentre - centre;
        base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(target);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(_initialChargeDirection.Normalized());
        if (wasApproaching)
        {
            RBMAI.TerrainCavalry.NoteEvent(this, TerrainBehaviorName, "charge go", RBMAI.TerrainCavalry.FormationName(target) + " " + why
                + " after " + RBMAI.TerrainCavalry.Fmt0(now - _approachSince) + "s");
        }
    }

    private void EnterChargingPast()
    {
        _chargingPastTimer = new Timer(Mission.Current.CurrentTime, ChargingPastTimeoutSeconds);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    private void EnterReforming()
    {
        float timeout = ReformTimeoutSeconds;
        float speed = base.Formation.QuerySystem.MovementSpeedMaximum;
        Vec2 held = _reform.HeldPoint;
        if (_reform.HoldingBack && held.IsValid && speed > 0f && !float.IsNaN(speed))
        {
            float left = held.Distance(RBMAI.Utilities.GetFormationCenter(base.Formation));
            timeout = MathF.Min(ReformBackMaxTimeoutSeconds, ReformTimeoutSeconds + left / (speed * ReformRideTimeShare));
        }
        _reformTimer = new Timer(Mission.Current.CurrentTime, timeout);
        _reformSince = Mission.Current.CurrentTime;
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    private void TickReformMovement()
    {
        WorldPosition point;
        float speed = base.Formation.QuerySystem.MovementSpeedMaximum;
        float maxRide = (speed > 0f && !float.IsNaN(speed)) ? MathF.Min(ReformMaxRide, ChargingPastTimeoutSeconds * speed * ReformRideTimeShare) : ReformMaxRide;
        if (!_reform.Tick(this, TerrainBehaviorName, _lastTarget?.Formation, _initialChargeDirection, maxRide, out point))
        {
            EnterState(ChargeState.Charging);
            return;
        }
        base.CurrentOrder = MovementOrder.MovementOrderMove(point);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    private bool IsEnemyClosingIn()
    {
        Mission mission = Mission.Current;
        if (mission == null || base.Formation == null)
        {
            return false;
        }
        Vec2 myCenter = RBMAI.Utilities.GetFormationCenter(base.Formation);
        float threatSq = ReformThreatDistance * ReformThreatDistance;
        float approachSq = threatSq * ReformApproachDistanceFactor * ReformApproachDistanceFactor;
        foreach (Formation enemy in OtherFormations(mission, enemiesOnly: true))
        {
            Vec2 enemyCenter = RBMAI.Utilities.GetFormationCenter(enemy);
            float distSq = myCenter.DistanceSquared(enemyCenter);
            if (distSq > approachSq || !RBMAI.ThreatSignificance.IsSignificant(base.Formation, enemy))
            {
                continue;
            }
            if (distSq <= threatSq)
            {
                return true;
            }
            Vec2 toMe = myCenter - enemyCenter;
            Vec2 vel = RBMAI.EnemyMotion.Velocity(enemy);
            if (distSq <= approachSq && vel.LengthSquared > ReformApproachMinSpeedSq && Vec2.DotProduct(vel.Normalized(), toMe.Normalized()) > ReformApproachMinDot)
            {
                return true;
            }
        }
        return false;
    }

    private IEnumerable<Formation> OtherFormations(Mission mission, bool enemiesOnly = false)
    {
        foreach (Team team in mission.Teams)
        {
            if (enemiesOnly && !team.IsEnemyOf(base.Formation.Team))
            {
                continue;
            }
            foreach (Formation other in team.FormationsIncludingSpecialAndEmpty)
            {
                if (other != null && other != base.Formation && other.CountOfUnits > 0)
                {
                    yield return other;
                }
            }
        }
    }

    protected override void OnBehaviorActivatedAux()
    {
        // Wide first: the charge's capped custom width (EnterCharging) must not be overwritten
        base.Formation.SetFormOrder( FormOrder.FormOrderWide);
        // start a fresh charge: timers survive deactivation, an old expired one would send it straight to ChargingPast
        EnterState(ChargeState.Charging);
        CalculateCurrentOrder();
        base.Formation.SetMovementOrder(base.CurrentOrder);
        base.Formation.SetFacingOrder( CurrentFacingOrder);
        base.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
        base.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
    }

    public override void OnBehaviorCanceled()
    {
        _laneApproach = false;
        _approaching = false;
        RBMAI.TerrainCavalry.Release(this, "behaviorEnded");
        _reform.Release(this, TerrainBehaviorName, "behaviorEnded");
        base.OnBehaviorCanceled();
    }

    public override TextObject GetBehaviorString()
    {
        TextObject behaviorString = base.GetBehaviorString();
        if (base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
        {
            behaviorString.SetTextVariable("AI_SIDE", GameTexts.FindText("str_formation_ai_side_strings", base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.AI.Side.ToString()));
            behaviorString.SetTextVariable("CLASS", GameTexts.FindText("str_formation_class_string", base.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.RepresentativeClass.GetName()));
        }
        return behaviorString;
    }

    protected override float GetAiWeight()
    {
        FormationQuerySystem querySystem = base.Formation.QuerySystem;
        if (querySystem.ClosestSignificantlyLargeEnemyFormation == null)
        {
            return 0f;
        }
        RBMAI.CavalryCommit.Verdict commit = Commit();
        if (commit.Gate == RBMAI.CavalryCommit.Gate.WearDown)
        {
            return RBMAI.CavalryCommit.WearDownHeldWeight;
        }
        FormationQuerySystem enemy = querySystem.ClosestSignificantlyLargeEnemyFormation;
        float timeToContact = RBMAI.Utilities.GetFormationDistance(querySystem.Formation, enemy.Formation) / querySystem.MovementSpeedMaximum;
        float proximityFactor;
        if (!querySystem.IsCavalryFormation && !querySystem.IsRangedCavalryFormation)
        {
            float clamped = MBMath.ClampFloat(timeToContact, 4f, 10f);
            proximityFactor = MBMath.Lerp(0.8f, 1f, 1f - (clamped - 4f) / 6f);
        }
        else if (timeToContact <= 4f)
        {
            float clamped = MBMath.ClampFloat(timeToContact, 0f, 4f);
            proximityFactor = MBMath.Lerp(0.8f, 1.2f, clamped / 4f);
        }
        else
        {
            float clamped = MBMath.ClampFloat(timeToContact, 4f, 10f);
            proximityFactor = MBMath.Lerp(0.8f, 1.2f, 1f - (clamped - 4f) / 6f);
        }
        float slopeFactor = 1f;
        if (timeToContact <= 4f)
        {
            float length = (RBMAI.Utilities.GetFormationCenter(querySystem.Formation) - RBMAI.Utilities.GetFormationCenter(enemy.Formation)).Length;
            // coincident centres divide by zero / GetNavMeshZ NaN off the navmesh: a NaN weight silently loses every comparison; fall back to 1f
            if (length > float.Epsilon)
            {
                WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(querySystem.Formation);
                // enemy height from the raw median (valid Z, no navmesh query)
                float slope = (medianPosition.GetNavMeshZ() - enemy.Formation.CachedMedianPosition.GetNavMeshZ()) / length;
                if (!float.IsNaN(slope))
                {
                    slopeFactor = MBMath.Lerp(0.9f, 1.1f, (MBMath.ClampFloat(slope, -0.58f, 0.58f) + 0.58f) / 1.16f);
                }
            }
        }
        float imminentContactFactor = (timeToContact <= 4f && timeToContact >= 1.5f) ? 1.2f : 1f;
        float unengagedEnemyFactor = (timeToContact <= 4f && enemy.ClosestSignificantlyLargeEnemyFormation != querySystem) ? 1.2f : 1f;
        float classFactor = querySystem.GetClassWeightedFactor(1f, 1f, 1.5f, 1.5f) * enemy.GetClassWeightedFactor(1f, 1f, 0.5f, 0.5f);
        return (proximityFactor * slopeFactor * imminentContactFactor * unengagedEnemyFactor * classFactor) * 2f * commit.Factor;
    }

    private string _weakLogged;

    // WEAR DOWN (CavalryWearDown, kHA > 0): melee cavalry waits at a guard post while the horse archers work (CavalryCommit).
    private const float GuardSideGap = 10f; // m beside the anchor's flank
    private const float GuardBehindHorseArchers = 40f; // m behind the horse archers (no foot anchor)

    private static readonly System.Func<Formation, bool> HorseArcherFormation = f => RBMAI.ArmyComposition.IsMounted(f) && RBMAI.ArmyComposition.IsHorseArchers(f);

    private bool _wearHeld;
    private bool _wearReopened;
    private string _wearLogged;
    private float _guardSign;
    private readonly RBMAI.CachedMovePoint _guardPoint = new RBMAI.CachedMovePoint();

    /// <summary>Held by CavalryWearDown (guarding, not charging). AI thread.</summary>
    internal bool IsWearDownHeld => _wearHeld;

    /// <summary>AI thread: the shared commit gate (CavalryCommit) for this formation, with the wear-down hold's own transitions and the gate's log lines (on a change of kind, while active).</summary>
    private RBMAI.CavalryCommit.Verdict Commit()
    {
        Formation formation = base.Formation;
        RBMAI.CavalryCommit.Verdict v = RBMAI.CavalryCommit.For(formation);
        bool held = v.Gate == RBMAI.CavalryCommit.Gate.WearDown;
        string text = v.WearText;
        if (held != _wearHeld)
        {
            _wearHeld = held;
            if (held)
            {
                _laneApproach = false;
                RBMAI.TerrainCavalry.Release(this, "wearDown");
                _reform.Release(this, TerrainBehaviorName, "wearDown");
                _guardPoint.Reset();
                _guardSign = 0f;
            }
            else
            {
                _wearReopened = true;
            }
        }
        bool active = formation?.AI != null && formation.AI.ActiveBehavior == this;
        string kind = held ? "hold" : ((text != null) ? "open" : null);
        if (kind != _wearLogged && active)
        {
            _wearLogged = kind;
            if (kind != null)
            {
                RBMAI.ArmyComposition.Note(formation.Team, formation, RBMAI.ArmyComposition.KindGate, "wearDown " + kind, text + " " + RBMAI.ArmyComposition.DescribeOwnK(formation.Team), TerrainBehaviorName);
            }
        }
        // the factor moves every second: logged on a change of kind only
        if (!held && v.WeakKind != _weakLogged && active)
        {
            _weakLogged = v.WeakKind;
            RBMAI.ArmyComposition.Note(formation.Team, formation, RBMAI.ArmyComposition.KindGate, "kWeak", (v.WeakText ?? "off") + " kWeak=" + RBMAI.ArmyComposition.F2(v.KWeak)
                + " " + RBMAI.ArmyComposition.DescribeK(formation.Team), TerrainBehaviorName);
        }
        return v;
    }

    private bool WearDownHolds()
    {
        return Commit().Gate == RBMAI.CavalryCommit.Gate.WearDown;
    }

    /// <summary>Held: beside the position anchor's flank (the side we are on), level with it, facing the enemy; no foot anchor: behind our horse archers.</summary>
    private void GuardOrder()
    {
        Formation formation = base.Formation;
        Formation enemy = formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation?.Formation;
        Formation reference = RBMAI.ArmyComposition.AnchorFor(formation);
        bool behind = false;
        if (reference == null)
        {
            reference = RBMAI.ArmyComposition.Strongest(formation.Team, HorseArcherFormation);
            behind = true;
        }
        RBMAI.Footprint shape;
        if (enemy == null || reference == null || !RBMAI.FormationGeometry.TryGet(reference, RBMAI.FootprintMode.Current, out shape))
        {
            base.CurrentOrder = MovementOrder.MovementOrderStop;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            return;
        }
        Vec2 toward = RBMAI.Utilities.GetFormationCenter(enemy) - shape.Centre;
        toward = (toward.LengthSquared > 0.01f) ? toward.Normalized() : reference.Direction;
        Vec2 desired;
        if (behind)
        {
            desired = shape.Centre - toward * (RBMAI.FormationGeometry.ExtentAlong(shape, -toward) + GuardBehindHorseArchers + formation.Depth * 0.5f);
        }
        else
        {
            Vec2 right = toward.RightVec();
            if (_guardSign == 0f)
            {
                _guardSign = ((RBMAI.Utilities.GetFormationCenter(formation) - shape.Centre).DotProduct(right) >= 0f) ? 1f : -1f;
            }
            Vec2 side = right * _guardSign;
            desired = shape.Centre + side * (RBMAI.FormationGeometry.ExtentAlong(shape, side) + GuardSideGap + formation.Width * 0.5f);
        }
        // the Move point is the front-centre
        Vec2 front = desired + toward * (formation.Depth * 0.5f);
        // own median, else the reference's; no source keeps the last point
        WorldPosition point;
        if (!_guardPoint.TryGet(front, Mission.Current.CurrentTime, formation.CachedMedianPosition, reference.CachedMedianPosition, false, true, false, out point))
        {
            base.CurrentOrder = MovementOrder.MovementOrderStop;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            return;
        }
        base.CurrentOrder = MovementOrder.MovementOrderMove(point);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(toward);
    }

}