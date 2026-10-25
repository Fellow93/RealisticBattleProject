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
/// ChargingPast: ride on to a reform point; a cohesive wedge with enough run-up skips the reform and charges again.
/// Reforming: hold the reform point until the wedge is cohesive (counted over all riders) or ReformTimeoutSeconds.
///
/// Reform point: CavalryReform picks it from the live situation (through / left / right / back, enemies and friends first),
/// re-checked every 2 s; none usable = skip the reform. While reforming only, an enemy within ReformThreatDistance (or
/// closing, IsEnemyClosingIn) ends the reform.
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
    private const float ChargingPastTimeoutSeconds = 19f;
    private const float ReformTimeoutSeconds = 10f;
    private const float ReformCohesionRadius = 20f;
    private const float ReformCohesionMinRatio = 0.9f;
    private const float RechargeMinRunUpDistance = 100f;
    private const float ReformAbortRangedAttackRatio = 0.2f;
    private const float ReformThreatDistance = 35f;
    private const float ReformApproachDistanceFactor = 2f;
    private const float ReformApproachMinSpeedSq = 1f;
    private const float ReformApproachMinDot = 0.7f;

    private ChargeState _chargeState;

    private FormationQuerySystem _lastTarget;

    private Vec2 _initialChargeDirection;

    private bool _isEmbolonTactic;

    private bool _hasNewTarget;

    private readonly RBMAI.CavalryReform _reform = new RBMAI.CavalryReform();

    private Timer _chargingPastTimer;

    private Timer _reformTimer;

    private Timer _chargeTimer;

    private Timer _contactTimer;

    private bool _laneApproach;

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
                        if (IsChaseTimedOut(toTarget.Length, inContact || _contactTimer != null || _chargeTimer != null))
                        {
                            _hasNewTarget = true;
                        }
                        break;
                    }
                case ChargeState.ChargingPast:
                    {
                        if (IsTargetGone())
                        {
                            result = ChargeState.Charging;
                            break;
                        }
                        float distToTarget = RBMAI.Utilities.GetFormationDistance(base.Formation, _lastTarget.Formation);
                        if (IsReadyToRecharge(distToTarget))
                        {
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
                            result = ChargeState.Charging;
                            break;
                        }
                        float distToEnemy = RBMAI.Utilities.GetFormationDistance(base.Formation, _lastTarget.Formation);
                        bool underFire = base.Formation.QuerySystem.UnderRangedAttackRatio > ReformAbortRangedAttackRatio;
                        if (_reformTimer.Check(Mission.Current.CurrentTime) || IsReadyToRecharge(distToEnemy) || underFire || IsEnemyClosingIn())
                        {
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
            base.CurrentOrder = MovementOrder.MovementOrderCharge;
            return;
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
    }

    private void EnterState(ChargeState state)
    {
        _chargeState = state;
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
        // a new charge gives up any reform point / lane
        _reform.Release(this, TerrainBehaviorName, "newCharge");
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
            // never narrower than MinChargeWidth (a tiny target would pack the cavalry into a deep block)
            base.Formation.SetFormOrder(FormOrder.FormOrderCustom(MathF.Max(MinChargeWidth, _lastTarget.Formation.Width)));
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

    // Each lane-approach tick: keep riding, or (arrived, timed out, melee, enemy close, target off the lane, inactive) charge from here with a fresh direction.
    private void TickLaneApproach()
    {
        Formation target = _lastTarget?.Formation;
        RBMAI.TerrainCavalry.Hold hold = RBMAI.TerrainCavalry.HeldBy(this, RBMAI.TerrainCavalry.HoldKind.Lane);
        if (hold != null && hold.Entry != null && RBMAI.TerrainCavalry.CheckLaneApproach(this, target, IsFightingInMelee()))
        {
            // read again: a re-plan may have moved the hold during the check
            hold = RBMAI.TerrainCavalry.HeldBy(this, RBMAI.TerrainCavalry.HoldKind.Lane) ?? hold;
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

    private void EnterChargingPast()
    {
        _chargingPastTimer = new Timer(Mission.Current.CurrentTime, ChargingPastTimeoutSeconds);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    private void EnterReforming()
    {
        _reformTimer = new Timer(Mission.Current.CurrentTime, ReformTimeoutSeconds);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    private void TickReformMovement()
    {
        WorldPosition point;
        if (!_reform.Tick(this, TerrainBehaviorName, _lastTarget?.Formation, _initialChargeDirection, out point))
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
            if (distSq <= threatSq)
            {
                return true;
            }
            Vec2 toMe = myCenter - enemyCenter;
            Vec2 vel = enemy.CachedCurrentVelocity;
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
        // start a fresh charge: timers survive deactivation, an old expired one would send it straight to ChargingPast
        EnterState(ChargeState.Charging);
        CalculateCurrentOrder();
        base.Formation.SetMovementOrder(base.CurrentOrder);
        base.Formation.SetFacingOrder( CurrentFacingOrder);
        base.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
        base.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
        base.Formation.SetFormOrder( FormOrder.FormOrderWide);
    }

    public override void OnBehaviorCanceled()
    {
        _laneApproach = false;
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
        return (proximityFactor * slopeFactor * imminentContactFactor * unengagedEnemyFactor * classFactor) * 2f;
    }
}