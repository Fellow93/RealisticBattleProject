using HarmonyLib;
using RBMStance;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.Formation;
using static TaleWorlds.MountAndBlade.MovementOrder;

namespace RBMAI
{
    public static partial class Frontline
    {
        // Step-gate counters: Interlocked on the movement workers, reset in MissionStartReset. Rejects = step frames turned into a hold; probes = engine queries.
        internal static long ObstacleUnreachableRejects;
        internal static long ObstacleDeepWaterRejects;
        internal static long ObstacleProbeQueries;
        internal static long ObstaclePressReleases;

        internal static void ResetObstacleCounters()
        {
            System.Threading.Interlocked.Exchange(ref ObstacleUnreachableRejects, 0L);
            System.Threading.Interlocked.Exchange(ref ObstacleDeepWaterRejects, 0L);
            System.Threading.Interlocked.Exchange(ref ObstacleProbeQueries, 0L);
            System.Threading.Interlocked.Exchange(ref ObstaclePressReleases, 0L);
        }

        [HarmonyPatch(typeof(Formation))]
        private class OverrideFormation
        {
            // Mission.GetNearby*Agents clears the list it is handed, so scratch buffers are reused.
            // [ThreadStatic] is load-bearing: the prefix runs on the parallel movement job; a shared buffer would be torn.
            [ThreadStatic] private static MBList<Agent> _scratchAlliesFront;
            [ThreadStatic] private static MBList<Agent> _scratchAlliesLeft;
            [ThreadStatic] private static MBList<Agent> _scratchAlliesRight;
            [ThreadStatic] private static MBList<Agent> _scratchEnemiesFront;
            [ThreadStatic] private static MBList<Agent> _scratchEnemyQuery;
            [ThreadStatic] private static MBList<Agent> _scratchOccupancy;
            [ThreadStatic] private static MBList<Agent> _scratchNearbyAllies;
            [ThreadStatic] private static MBList<Agent> _scratchClumpAhead;

            private static MBList<Agent> ScratchAlliesFront => _scratchAlliesFront ?? (_scratchAlliesFront = new MBList<Agent>());
            private static MBList<Agent> ScratchAlliesLeft => _scratchAlliesLeft ?? (_scratchAlliesLeft = new MBList<Agent>());
            private static MBList<Agent> ScratchAlliesRight => _scratchAlliesRight ?? (_scratchAlliesRight = new MBList<Agent>());
            private static MBList<Agent> ScratchEnemiesFront => _scratchEnemiesFront ?? (_scratchEnemiesFront = new MBList<Agent>());
            private static MBList<Agent> ScratchEnemyQuery => _scratchEnemyQuery ?? (_scratchEnemyQuery = new MBList<Agent>());
            private static MBList<Agent> ScratchOccupancy => _scratchOccupancy ?? (_scratchOccupancy = new MBList<Agent>());
            private static MBList<Agent> ScratchNearbyAllies => _scratchNearbyAllies ?? (_scratchNearbyAllies = new MBList<Agent>());
            private static MBList<Agent> ScratchClumpAhead => _scratchClumpAhead ?? (_scratchClumpAhead = new MBList<Agent>());

            [HarmonyPostfix]
            [HarmonyPatch("GetDirectionOfUnit")]
            public static void Postfix_GetDirectionOfUnit(Formation __instance, Agent unit, ref Vec2 __result)
            {
                try
                {
                    Mission mission = Mission.Current;
                    if (mission == null || (!mission.IsFieldBattle && !mission.IsNavalBattle) || unit == null || !__instance.QuerySystem.IsInfantryFormationReadOnly)
                    {
                        return;
                    }
                    MovementOrder order = __instance.GetReadonlyMovementOrderReference();
                    // Facing only applies under charge orders, like the mindset block below.
                    if (order.OrderType != OrderType.Charge && order.OrderType != OrderType.ChargeWithTarget)
                    {
                        return;
                    }
                    Agent targetAgent = GetCachedCorrectTarget(unit, GetOrCreateDecisionState(unit));
                    if (targetAgent != null)
                    {
                        float distanceToEnemy = unit.Position.AsVec2.Distance(targetAgent.Position.AsVec2);
                        if (distanceToEnemy < 20f)
                        {
                            __result = (targetAgent.Position.AsVec2 - unit.Position.AsVec2).Normalized();
                        }
                    }
                }
                catch
                {
                }
            }

            // Where a rider > 60 m out steers under ChargeWithTarget: his target soldier pulled toward the target formation's
            // centre and led by its velocity (steering at the soldier chased a moving line along a pursuit curve).
            // Parallel movement job: cached formation fields only, one SetVec2MT on a fresh copy (no navmesh query).
            private const float ChargeAimCentrePull = 0.3f;
            private const float ChargeAimClosingSpeed = 11f;
            private const float ChargeAimMaxLeadSeconds = 4f;
            private const float ChargeAimMaxLeadSpeed = 4f;

            private static WorldPosition ChargeHeading(Agent targetAgent, float distance)
            {
                WorldPosition heading = targetAgent.GetWorldPosition();
                Formation targetFormation = targetAgent.Formation;
                if (targetFormation == null)
                {
                    return heading;
                }
                Vec2 aim = heading.AsVec2;
                Vec2 centre = Utilities.GetFormationCenter(targetFormation);
                if (centre.IsValid)
                {
                    aim += (centre - aim) * ChargeAimCentrePull;
                }
                // CachedCurrentVelocity is a ~0.1 s difference of the unit average and jumps when men die or join; capped.
                Vec2 velocity = targetFormation.CachedCurrentVelocity;
                if (velocity.IsValid && velocity.LengthSquared > ChargeAimMaxLeadSpeed * ChargeAimMaxLeadSpeed)
                {
                    velocity = velocity.Normalized() * ChargeAimMaxLeadSpeed;
                }
                if (velocity.IsValid)
                {
                    aim += velocity * MathF.Min(distance / ChargeAimClosingSpeed, ChargeAimMaxLeadSeconds);
                }
                heading.SetVec2MT(aim);
                return heading;
            }

            // Parallel movement job: an escaping exception tears down the worker. Swallow and fall through to native, __result untouched.
            [HarmonyPrefix]
            [HarmonyPatch("GetOrderPositionOfUnit")]
            private static bool PrefixGetOrderPositionOfUnit(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
            {
                WorldPosition originalResult = __result;
                try
                {
                    return GetOrderPositionOfUnitCore(__instance, ref ____orderPosition, ref ____arrangement, ref unit, ____detachedUnits, ref __result);
                }
                catch
                {
                    __result = originalResult;
                    return true;
                }
            }

            private static bool GetOrderPositionOfUnitCore(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
            {
                Mission mission = Mission.Current;

                if (unit == null || !unit.IsActive() || mission == null || !mission.IsDeploymentFinished || unit.IsPlayerControlled)
                {
                    return true;
                }
                // Release a pending frontline pin before ANY early return (SetTargetPosition leaves the agent PositionLocked
                // until ClearTargetFrame). TryGetValue: units never pinned get no decision state.
                if (aiDecisionCooldownDict.TryGetValue(unit, out AIDecisionState pendingClear) && pendingClear != null && pendingClear.AIMindset.shouldClearTargetFrame)
                {
                    unit.ClearTargetFrame();
                    pendingClear.AIMindset.shouldClearTargetFrame = false;
                }
                // Vanilla's own early returns (Follow target, detached units, unreachable units); the branches below must not override them.
                MovementOrder movementOrder = __instance.GetReadonlyMovementOrderReference();
                if ((movementOrder.OrderEnum == MovementOrder.MovementOrderEnum.Follow && movementOrder._targetAgent == unit)
                    || unit.IsDetachedFromFormation
                    || unit.GetAgentFlags().HasAnyFlag(AgentFlag.UnreachableViaNavMesh))
                {
                    return true;
                }
                // Branches below pass unit.Team to Mission.GetNearby*Agents, which dereferences it: a teamless agent would NRE on the worker.
                if (unit.Team == null)
                {
                    return true;
                }
                // Only AI formations, never out of a shieldwall/square/circle; player formations hold as vanilla does.
                ArrangementOrder.ArrangementOrderEnum siegeArrangement = __instance.ArrangementOrder.OrderEnum;
                if (mission.IsSiegeBattle && __instance.IsAIControlled
                    && siegeArrangement != ArrangementOrder.ArrangementOrderEnum.ShieldWall
                    && siegeArrangement != ArrangementOrder.ArrangementOrderEnum.Square
                    && siegeArrangement != ArrangementOrder.ArrangementOrderEnum.Circle)
                {
                    if (unit.Position == null)
                    {
                        return true;
                    }
                    MBList<Agent> enemiesCloseBy = ScratchEnemyQuery;
                    mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 2.25f, unit.Team, enemiesCloseBy);
                    if (enemiesCloseBy.Count > 0)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                }
                // Skipped for a formation ordered to dismount (its first men off would lose their slot and charge).
                // Class flags use the *ReadOnly getters on this worker path (as native does): the plain getters re-evaluate
                // the whole unit-ratio sync group from every worker at once.
                if (mission != null && mission.IsFieldBattle && unit != null && __instance.IsAIControlled
                    && __instance.RidingOrder.OrderEnum != RidingOrder.RidingOrderEnum.Dismount
                    && (__instance.QuerySystem.IsCavalryFormationReadOnly || __instance.QuerySystem.IsRangedCavalryFormationReadOnly))
                {
                    if (unit != null && unit.MountAgent == null)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                    // The proximity queries take Mission's global agent-query lock: skipped while no enemy can be in range,
                    // else the verdict is reused for 0.25-0.5 s (jittered).
                    if (IsEnemyFormationWithin(mission, unit, CavalryEnemyCloseRadius + CavalryEnemyCloseGateMargin))
                    {
                        AIDecisionState cavalryState = GetOrCreateDecisionState(unit);
                        float now = mission.CurrentTime;
                        if (now >= cavalryState.cavalryEnemyCloseExpiry)
                        {
                            cavalryState.cavalryEnemyClose = AreEnemiesCloseToRider(mission, unit);
                            cavalryState.cavalryEnemyCloseExpiry = now + MBRandom.RandomFloatRanged(0.25f, 0.5f);
                        }
                        if (cavalryState.cavalryEnemyClose)
                        {
                            __result = WorldPosition.Invalid;
                            return false;
                        }
                    }
                }
                if (mission != null && mission.IsFieldBattle && unit != null && __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget && __instance.QuerySystem.IsCavalryFormationReadOnly)
                {
                    AIDecisionState headingState = GetOrCreateDecisionState(unit);
                    var targetAgent = unit.GetTargetAgent();
                    if (__instance.IsAIControlled)
                    {
                        targetAgent = GetCachedCorrectTarget(unit, headingState);
                    }
                    if (targetAgent != null)
                    {
                        float distance = (targetAgent.Position - unit.Position).Length;
                        if (distance > 60f)
                        {
                            // GetWorldPosition is an engine call; beyond 60 m a heading up to 0.5 s old steers the same, so it is reused.
                            float now = mission.CurrentTime;
                            if (headingState == null)
                            {
                                __result = ChargeHeading(targetAgent, distance);
                            }
                            else
                            {
                                if (headingState.cachedHeadingTarget != targetAgent || now >= headingState.cachedHeadingExpiry)
                                {
                                    headingState.cachedHeadingPosition = ChargeHeading(targetAgent, distance);
                                    headingState.cachedHeadingTarget = targetAgent;
                                    headingState.cachedHeadingExpiry = now + MBRandom.RandomFloatRanged(0.25f, 0.5f);
                                }
                                __result = headingState.cachedHeadingPosition;
                            }
                            return false;
                        }
                    }
                }
                if (mission != null && unit != null && __instance.IsAIControlled && mission.IsFieldBattle && __instance.QuerySystem.IsRangedFormationReadOnly)
                {
                    MBList<Agent> enemiesCloseBy = ScratchEnemyQuery;
                    mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 2.5f, unit.Team, enemiesCloseBy);
                    if (enemiesCloseBy.Count > 0)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                    //ranged charge if they are skirmishing but not attacking
                    // Engagement-range gate: the stall test is also true of an archer marching toward a distant enemy, which
                    // would send individuals sprinting the whole gap. Only fire once the formation is in contact range.
                    if (__instance.AI != null && __instance.AI.ActiveBehavior != null && IsFormationInEngagementRange(__instance, 30f))
                    {
                        if (unit.LastRangedHitTime > 0)
                        {
                            Type activeBehaviorType = __instance.AI.ActiveBehavior.GetType();
                            if (activeBehaviorType == typeof(RBMBehaviorArcherFlank) || activeBehaviorType == typeof(RBMBehaviorArcherSkirmish)
                                || activeBehaviorType == typeof(BehaviorSkirmish) || activeBehaviorType == typeof(BehaviorSkirmishBehindFormation) || activeBehaviorType == typeof(BehaviorSkirmishLine))
                            {
                                MBList<Agent> enemyCloseBy = ScratchEnemyQuery;
                                mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 15f, unit.Team, enemyCloseBy);
                                float currentTime = MBCommon.GetTotalMissionTime();
                                if (currentTime - unit.LastMeleeHitTime > 10f && currentTime - unit.LastRecievedMeleeHitTime > 10f)
                                {
                                    // Engine state is not written from the parallel job: the 50 s clock is kept RBM-side
                                    // (effective last-shot time = later of the engine's value and our reset stamp).
                                    AIDecisionState rangedState = GetOrCreateDecisionState(unit);
                                    float lastRangedTime = unit.LastRangedHitTime;
                                    if (rangedState != null && rangedState.stallResetTime > lastRangedTime)
                                    {
                                        lastRangedTime = rangedState.stallResetTime;
                                    }
                                    if (currentTime - lastRangedTime > 50f)
                                    {
                                        if (rangedState != null)
                                        {
                                            rangedState.stallResetTime = currentTime;
                                        }
                                        lastRangedTime = currentTime;
                                    }
                                    if (currentTime - lastRangedTime > 20f && enemyCloseBy.Count < 3)
                                    {
                                        __result = WorldPosition.Invalid;
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }

                // Never pin a man who is getting away (a charge rout keeps him in his formation, and a pin would lock him mid-flight).
                // Nor a man in the water: native asks for a swimmer's slot only on land, so a pin set via a side path would never be released.
                // Nor a man the deep-water blocker is driving out (scripted move; may be wading, IsOnLand misses him): a pin is a
                // movement lock. Concurrent index-set read only (DeepWaterSlots.IsForcedExit), safe on this worker thread.
                if (unit.IsRunningAway || (unit.CommonAIComponent != null && unit.CommonAIComponent.IsRetreating) || !unit.IsOnLand()
                    || DeepWaterSlots.IsForcedExit(unit.Index))
                {
                    return true;
                }

                AIDecisionState aiDecision = GetOrCreateDecisionState(unit);

                // Only the mindset/decision block below is switched off by frontlineEnabled; the free-charge gates above stay on.
                if (!RBMConfig.RBMConfig.frontlineEnabled)
                {
                    return true;
                }

                // Below this size the system does nothing (the pending clear above still runs first).
                if (__instance.CountOfUnitsWithoutDetachedOnes <= RBMConfig.RBMConfig.frontlineMinFormationSize)
                {
                    return true;
                }

                if (mission != null && mission.IsFieldBattle && (__instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge) && (__instance.QuerySystem.IsInfantryFormationReadOnly || __instance.QuerySystem.IsRangedFormationReadOnly) && !____detachedUnits.Contains(unit))
                {
                    Agent targetAgent;
                    var vanillaTargetAgent = targetAgent = unit.GetTargetAgent();
                    if (__instance.IsAIControlled)
                    {
                        targetAgent = GetCachedCorrectTarget(unit, aiDecision);
                    }
                    else
                    {
                        if (__instance.TargetFormation == null)
                        {
                            targetAgent = GetCachedCorrectTarget(unit, aiDecision);
                        }
                        else
                        {
                            targetAgent = Utilities.NearestAgentFromFormation(unit.Position.AsVec2, __instance.TargetFormation);
                        }
                    }

                    if (targetAgent != null && vanillaTargetAgent != null)
                    {
                        if (vanillaTargetAgent.Formation != null && vanillaTargetAgent.Formation == targetAgent.Formation)
                        {
                            targetAgent = vanillaTargetAgent;
                        }

                        Vec2 unitPosition = unit.Position.AsVec2;
                        Vec2 direction = (targetAgent.Position.AsVec2 - unitPosition).Normalized();
                        Vec2 leftVec = direction.LeftVec();
                        Vec2 rightVec = direction.RightVec();

                        // Scores matter only on decision frames, so the four proximity queries (global agent-query lock) run then;
                        // skipped frames are counted and replayed (see DecideMindset).
                        if (aiDecision.AIMindset.AIDecisionTimer == null)
                        {
                            DecideMindset(mission, __instance, unit, aiDecision.AIMindset, unitPosition, direction, leftVec, rightVec);
                            aiDecision.AIMindset.AIDecisionTimer = new Timer(Mission.Current.CurrentTime, MBRandom.RandomFloatRanged(0f, RBMConfig.RBMConfig.frontlineDecisionTimerMax), false);
                        }
                        else
                        {
                            if (aiDecision.AIMindset.pendingScoreUpdates < AIMindset.MaxPendingScoreUpdates)
                            {
                                aiDecision.AIMindset.pendingScoreUpdates++;
                            }
                            bool checkTimer = aiDecision.AIMindset.AIDecisionTimer.Check(Mission.Current.CurrentTime);
                            if (checkTimer)
                            {
                                aiDecision.AIMindset.AIDecisionTimer = null;
                            }
                        }

                        aiDecision.AIMindset.shouldClearTargetFrame = true;

                        switch (aiDecision.AIMindset.currentDecision)
                        {
                            case AIMindset.AIDecision.Rest:
                                {
                                    __result = unit.GetWorldPosition();
                                    unit.SetTargetPosition(unit.GetWorldPosition().AsVec2);
                                    return false;
                                }
                            case AIMindset.AIDecision.Attack:
                                {
                                    if (__instance.IsAIControlled && targetAgent != null && targetAgent.IsActive() && vanillaTargetAgent.IsActive() && unit != null && targetAgent != vanillaTargetAgent && (vanillaTargetAgent.HasMount || vanillaTargetAgent.IsRunningAway))
                                    {
                                        if (targetAgent != null)
                                        {
                                            WorldPosition targetPosition = unit.GetWorldPosition();
                                            Vec2 targetVec2 = unitPosition + direction * MBRandom.RandomFloatRanged(0.15f, 0.5f);
                                            // Rock, wall or deep water in the way: no step, native's own charge instead.
                                            if (IsStepBlocked(mission, unit, aiDecision, AIMindset.AIDecision.Attack, unitPosition, targetVec2))
                                            {
                                                return true;
                                            }
                                            if (IsPositionOccupied(mission, targetVec2, unit))
                                            {
                                                __result = unit.GetWorldPosition();
                                                unit.SetTargetPosition(unitPosition);
                                                return false;
                                            }
                                            if (IsPressingAgainstObstacle(mission, unit, aiDecision, AIMindset.AIDecision.Attack, unitPosition, targetVec2))
                                            {
                                                return true;
                                            }
                                            targetPosition.SetVec2MT(targetVec2);
                                            __result = targetAgent.GetWorldPosition();
                                            unit.SetTargetPosition(targetPosition.AsVec2);
                                            return false;
                                        }
                                    }
                                    // Ally in his lane nearer than his target: walk up behind him and wait (native's charge runs through him).
                                    // Walks straight ahead along his own line (keeps his lateral place), hysteresis + per-man stop gap.
                                    AIMindset closeUpMindset = aiDecision.AIMindset;
                                    Agent aheadAlly = closeUpMindset.aheadAlly;
                                    if (aheadAlly != null && targetAgent != null)
                                    {
                                        if (!aheadAlly.IsActive())
                                        {
                                            closeUpMindset.aheadAlly = null;
                                            closeUpMindset.closingUp = false;
                                            closeUpMindset.closeUpWaitSince = float.MinValue;
                                            return true;
                                        }
                                        Vec2 toAlly = aheadAlly.Position.AsVec2 - unitPosition;
                                        float allyDistance = toAlly.Length;
                                        float forwardGap = toAlly.DotProduct(direction);
                                        if (allyDistance <= CloseUpMaxRange
                                            && forwardGap >= allyDistance * CloseUpMinCos
                                            && allyDistance * allyDistance < targetAgent.Position.AsVec2.DistanceSquared(unitPosition))
                                        {
                                            float now = mission.CurrentTime;
                                            // Impatience: a shove presses 0.5 m forward into the man ahead; only walls / deep water stop it.
                                            if (now < closeUpMindset.closeUpShoveUntil)
                                            {
                                                Vec2 shoveVec2 = unitPosition + direction * CloseUpStep;
                                                if (!IsStepBlocked(mission, unit, aiDecision, AIMindset.AIDecision.Attack, unitPosition, shoveVec2))
                                                {
                                                    closeUpMindset.closingUp = false;
                                                    WorldPosition shovePosition = unit.GetWorldPosition();
                                                    shovePosition.SetVec2MT(shoveVec2);
                                                    __result = shovePosition;
                                                    unit.SetTargetPosition(shoveVec2);
                                                    return false;
                                                }
                                                closeUpMindset.closeUpShoveUntil = float.MinValue;
                                            }
                                            float stopGap = CloseUpGap + CloseUpGapJitter * IndexJitter(unit.Index);
                                            // An ally ahead who started moving is followed at once (no resume margin).
                                            PaceThresholds(aheadAlly, out float allyStill, out float allyMoving);
                                            bool allyAdvancing = aheadAlly.AverageVelocity.AsVec2.DotProduct(direction) >= allyStill;
                                            if (forwardGap <= stopGap
                                                || (!closeUpMindset.closingUp && !allyAdvancing && forwardGap <= stopGap + CloseUpResumeExtra))
                                            {
                                                closeUpMindset.closingUp = false;
                                                TickCloseUpPatience(closeUpMindset, now, unit.Index);
                                                __result = unit.GetWorldPosition();
                                                unit.SetTargetPosition(unitPosition);
                                                return false;
                                            }
                                            Vec2 closeUpVec2 = unitPosition + direction * MathF.Min(CloseUpStep, forwardGap - stopGap);
                                            if (IsStepBlocked(mission, unit, aiDecision, AIMindset.AIDecision.Attack, unitPosition, closeUpVec2)
                                                || IsPositionOccupied(mission, closeUpVec2, unit)
                                                || IsPressingAgainstObstacle(mission, unit, aiDecision, AIMindset.AIDecision.Attack, unitPosition, closeUpVec2))
                                            {
                                                closeUpMindset.closingUp = false;
                                                TickCloseUpPatience(closeUpMindset, now, unit.Index);
                                                __result = unit.GetWorldPosition();
                                                unit.SetTargetPosition(unitPosition);
                                                return false;
                                            }
                                            closeUpMindset.closeUpWaitSince = float.MinValue;
                                            closeUpMindset.closingUp = true;
                                            WorldPosition closeUpPosition = unit.GetWorldPosition();
                                            closeUpPosition.SetVec2MT(closeUpVec2);
                                            __result = closeUpPosition;
                                            unit.SetTargetPosition(closeUpVec2);
                                            return false;
                                        }
                                    }
                                    closeUpMindset.closingUp = false;
                                    return true;
                                }
                            case AIMindset.AIDecision.BackStep:
                                {
                                    WorldPosition backPosition = unit.GetWorldPosition();
                                    Vec2 backVec2 = unitPosition - ((direction + __instance.Direction) / 2f) * MBRandom.RandomFloatRanged(0, 0.3f);
                                    // Backed against an obstacle (or pressing): hold where he stands, as for an occupied spot.
                                    if (IsStepBlocked(mission, unit, aiDecision, AIMindset.AIDecision.BackStep, unitPosition, backVec2)
                                        || IsPositionOccupied(mission, backVec2, unit)
                                        || IsPressingAgainstObstacle(mission, unit, aiDecision, AIMindset.AIDecision.BackStep, unitPosition, backVec2))
                                    {
                                        __result = unit.GetWorldPosition();
                                        unit.SetTargetPosition(unitPosition);
                                        return false;
                                    }
                                    backPosition.SetVec2MT(backVec2);
                                    unit.SetTargetPosition(backPosition.AsVec2);
                                    __result = backPosition;
                                    return false;
                                }
                            case AIMindset.AIDecision.FindAlly:
                                {
                                    WorldPosition allyPosition = GetStepTowardNearbyAlly(mission, unitPosition, unit);
                                    Vec2 allyVec2 = allyPosition.AsVec2;
                                    // Obstacle or pressing: hold where he stands. Not native's frame (under a charge it walks him through the men in front).
                                    if (IsStepBlocked(mission, unit, aiDecision, AIMindset.AIDecision.FindAlly, unitPosition, allyVec2)
                                        || IsPositionOccupied(mission, allyVec2, unit)
                                        || IsPressingAgainstObstacle(mission, unit, aiDecision, AIMindset.AIDecision.FindAlly, unitPosition, allyVec2))
                                    {
                                        __result = unit.GetWorldPosition();
                                        unit.SetTargetPosition(unitPosition);
                                        return false;
                                    }
                                    __result = allyPosition;
                                    unit.SetTargetPosition(allyVec2);
                                    return false;
                                }
                            case AIMindset.AIDecision.FlankAllyLeft:
                                {
                                    WorldPosition leftPosition = unit.GetWorldPosition();
                                    Vec2 leftTargetVec2 = unitPosition + leftVec * MBRandom.RandomFloatRanged(0.15f, 0.3f);
                                    // Blocked, occupied or pressing: hold (see FindAlly).
                                    if (IsStepBlocked(mission, unit, aiDecision, AIMindset.AIDecision.FlankAllyLeft, unitPosition, leftTargetVec2)
                                        || IsPositionOccupied(mission, leftTargetVec2, unit)
                                        || IsPressingAgainstObstacle(mission, unit, aiDecision, AIMindset.AIDecision.FlankAllyLeft, unitPosition, leftTargetVec2))
                                    {
                                        __result = unit.GetWorldPosition();
                                        unit.SetTargetPosition(unitPosition);
                                        return false;
                                    }
                                    leftPosition.SetVec2MT(leftTargetVec2);
                                    __result = leftPosition;
                                    unit.SetTargetPosition(leftPosition.AsVec2);
                                    return false;
                                }
                            case AIMindset.AIDecision.FlankAllyRight:
                                {
                                    WorldPosition rightPosition = unit.GetWorldPosition();
                                    Vec2 rightTargetVec2 = unitPosition + rightVec * MBRandom.RandomFloatRanged(0.15f, 0.3f);
                                    // Blocked, occupied or pressing: hold (see FindAlly).
                                    if (IsStepBlocked(mission, unit, aiDecision, AIMindset.AIDecision.FlankAllyRight, unitPosition, rightTargetVec2)
                                        || IsPositionOccupied(mission, rightTargetVec2, unit)
                                        || IsPressingAgainstObstacle(mission, unit, aiDecision, AIMindset.AIDecision.FlankAllyRight, unitPosition, rightTargetVec2))
                                    {
                                        __result = unit.GetWorldPosition();
                                        unit.SetTargetPosition(unitPosition);
                                        return false;
                                    }
                                    rightPosition.SetVec2MT(rightTargetVec2);
                                    __result = rightPosition;
                                    unit.SetTargetPosition(rightPosition.AsVec2);
                                    return false;
                                }
                        }
                    }
                }

                return true;
            }

            // Scores the surroundings and picks the next frontline decision; only on a decision frame (AIDecisionTimer == null).
            // AIMindset.SetValue integrates (nudges toward the input, decays toward base), so skipped frames are replayed here.
            private static void DecideMindset(Mission mission, Formation formation, Agent unit, AIMindset mindset, Vec2 unitPosition, Vec2 direction, Vec2 leftVec, Vec2 rightVec)
            {
                // Four distinct buffers: every Count below is read after all are filled.
                MBList<Agent> alliesFront = ScratchAlliesFront;
                MBList<Agent> alliesLeft = ScratchAlliesLeft;
                MBList<Agent> alliesRight = ScratchAlliesRight;
                MBList<Agent> enemiesFront = ScratchEnemiesFront;

                mission.GetNearbyAllyAgents(unitPosition + direction * 1.35f, 1.35f, unit.Team, alliesFront);
                mission.GetNearbyAllyAgents(unitPosition + leftVec * 1.35f, 1.35f, unit.Team, alliesLeft);
                mission.GetNearbyAllyAgents(unitPosition + rightVec * 1.35f, 1.35f, unit.Team, alliesRight);

                mission.GetNearbyEnemyAgents(unitPosition + direction * 1.5f, 2f, unit.Team, enemiesFront);

                float postureModifier = 1f;
                float staminaModifier = 1f;
                if (RBMConfig.RBMConfig.rbmStanceEnabled && RBMConfig.RBMConfig.postureEnabled)
                {
                    Stance stance = null;
                    if (unit != null)
                    {
                        AgentStances.values.TryGetValue(unit, out stance);
                    }
                    // A zero max would put NaN into every decision score; the modifier stays neutral.
                    if (stance != null)
                    {
                        if (stance.maxPosture > 0f)
                        {
                            postureModifier = MathF.Lerp(0.1f, 1f, stance.posture / stance.maxPosture);
                        }
                        if (stance.maxStamina > 0f)
                        {
                            staminaModifier = MathF.Lerp(0.33f, 1f, MathF.Clamp(stance.stamina / stance.maxStamina, 0f, 1f));
                        }
                    }
                }

                float healthModifier = MathF.Lerp(0.33f, 1f, unit.Health / unit.HealthLimit);
                bool isSoldier = unit.Character.IsSoldier;

                // Allies ahead running forward count less (CountFrontBlockers) so rear ranks follow an advance, unless a clump
                // stands just beyond them (IsClumpStandingAhead): then the front has stopped and all count in full.
                // The look-ahead query (ScanAhead) runs only when the discount lowered the count or the decision is Attack.
                float alliesFrontCount = LimitCount(alliesFront.Count, 10);
                float discountedFrontCount = MathF.Min(10f, CountFrontBlockers(alliesFront, unit, direction));
                bool scannedAhead = false;
                int standingAhead = 0;
                Agent nearestAhead = null;
                if (discountedFrontCount < alliesFrontCount)
                {
                    ScanAhead(mission, unit, unitPosition, direction, out standingAhead, out nearestAhead);
                    scannedAhead = true;
                    if (standingAhead < ClumpMinStanding)
                    {
                        alliesFrontCount = discountedFrontCount;
                    }
                }
                int alliesLeftCount = LimitCount(alliesLeft.Count, 5);
                int alliesRightCount = LimitCount(alliesRight.Count, 5);
                int enemiesFrontCount = LimitCount(enemiesFront.Count, 10);

                int hasShieldAdditive = 0;
                int hasTwoHandedEquippedAddtive = 0;
                if (!unit.WieldedOffhandWeapon.IsEmpty && unit.WieldedOffhandWeapon.IsShield())
                {
                    hasShieldAdditive += 1;
                }
                if (formation.ArrangementOrder == ArrangementOrder.ArrangementOrderShieldWall)
                {
                    hasShieldAdditive += 2;
                }
                if (formation.ArrangementOrder == ArrangementOrder.ArrangementOrderLoose)
                {
                    hasShieldAdditive -= 1;
                }
                if (!unit.WieldedWeapon.IsEmpty && unit.WieldedWeapon.CurrentUsageItem != null && unit.WieldedWeapon.CurrentUsageItem.IsTwoHanded)
                {
                    hasTwoHandedEquippedAddtive += 1;
                }

                bool isBannerBearer = RBMAI.Utilities.IsBannerBearer(unit);
                bool isHero = unit.Character.IsHero;

                bool shouldAttackMore = alliesFrontCount <= 1 && enemiesFrontCount <= 1;

                float findAlly = (alliesFrontCount * 0.5f) + (enemiesFrontCount) - alliesRightCount - alliesLeftCount + (enemiesFrontCount > 0 && (alliesRightCount < 2 || alliesLeftCount < 2) ? 3 : 0);
                float fallback = (alliesFrontCount) + enemiesFrontCount;
                float attack = -(alliesFrontCount * 0.5f) + alliesLeftCount + alliesRightCount - enemiesFrontCount + (isSoldier ? 0 : 2) + (isHero ? -3 : 0);//+ Math.Max(0, 3 - (unitTier))
                // The side a man would flank toward counts twice, so a man hemmed in on both sides does not pick flanking inside a pile.
                float flankAllyLeft = (alliesFrontCount * 1.25f) + (alliesRightCount) - (2f * alliesLeftCount) - enemiesFrontCount;
                float flankAllyRight = (alliesFrontCount * 1.25f) + (alliesLeftCount) - (2f * alliesRightCount) - enemiesFrontCount;

                if (isBannerBearer)
                {
                    attack -= 3;
                    flankAllyLeft *= flankAllyLeft > 0 ? -1f : 1f;
                    flankAllyRight *= flankAllyRight > 0 ? -1f : 1f;
                }

                if (shouldAttackMore && !isBannerBearer)
                {
                    attack += 3;
                }

                if (hasShieldAdditive > 0)
                {
                    findAlly += hasShieldAdditive;
                    flankAllyLeft -= (hasShieldAdditive / 2f);
                    flankAllyRight -= (hasShieldAdditive / 2f);
                }

                if (hasTwoHandedEquippedAddtive > 0)
                {
                    attack += hasTwoHandedEquippedAddtive;
                    flankAllyLeft += hasTwoHandedEquippedAddtive;
                    flankAllyRight += hasTwoHandedEquippedAddtive;
                }

                attack = attack > 0 ? (attack * staminaModifier) : attack;

                // Config weights are applied last, to the finished score. Both flank directions share one weight (else the line drifts sideways).
                float attackValue = (attack > 0 ? attack * (postureModifier * healthModifier) : attack) * RBMConfig.RBMConfig.frontlineAttackWeight;
                float backStepValue = (fallback > 0 ? (fallback * (2 - postureModifier)) : fallback) * RBMConfig.RBMConfig.frontlineBackStepWeight;
                float findAllyValue = (findAlly > 0 ? (findAlly * (2 - postureModifier)) : findAlly) * RBMConfig.RBMConfig.frontlineFindAllyWeight;
                float flankLeftValue = flankAllyLeft * RBMConfig.RBMConfig.frontlineFlankWeight;
                float flankRightValue = flankAllyRight * RBMConfig.RBMConfig.frontlineFlankWeight;

                // This frame's update plus one per frame skipped since the last decision.
                int scoreUpdates = mindset.pendingScoreUpdates + 1;
                mindset.pendingScoreUpdates = 0;
                for (int i = 0; i < scoreUpdates; i++)
                {
                    mindset.SetValue(AIMindset.AIDecision.Attack, attackValue);
                    mindset.SetValue(AIMindset.AIDecision.BackStep, backStepValue);
                    mindset.SetValue(AIMindset.AIDecision.FindAlly, findAllyValue);
                    mindset.SetValue(AIMindset.AIDecision.FlankAllyLeft, flankLeftValue);
                    mindset.SetValue(AIMindset.AIDecision.FlankAllyRight, flankRightValue);
                }

                if (postureModifier < 0.5f && enemiesFrontCount == 0)
                {
                    mindset.currentDecision = AIMindset.AIDecision.Rest;
                }
                else
                {
                    mindset.getDecision(out mindset.currentDecision);
                }

                // Attack = native's charge; remember the nearest ally in his lane so the Attack branch closes up behind him instead.
                if (mindset.currentDecision == AIMindset.AIDecision.Attack)
                {
                    if (!scannedAhead)
                    {
                        ScanAhead(mission, unit, unitPosition, direction, out standingAhead, out nearestAhead);
                    }
                    mindset.aheadAlly = nearestAhead;
                }
                else
                {
                    mindset.aheadAlly = null;
                    mindset.closingUp = false;
                    mindset.closeUpWaitSince = float.MinValue;
                    mindset.closeUpShoveUntil = float.MinValue;
                }
            }

            private const float StepAllyReuseMin = 0.25f;
            private const float StepAllyReuseJitter = 0.15f;
            private const float StepAllyReuseMaxRange = 3f;

            // Returns a finished target position (a short step toward the nearest ally); use as-is, do not scale it again.
            public static WorldPosition GetStepTowardNearbyAlly(Mission mission, Vec2 unitPosition, Agent unit)
            {
                Agent nearestAlly = null;
                float nearestDistance = 10000f;

                // Nearest ally remembered 0.25-0.4 s and reused while alive and inside the radius he was found in (capped at
                // StepAllyReuseMaxRange). Direction and distance stay live.
                aiDecisionCooldownDict.TryGetValue(unit, out AIDecisionState memo);
                float now = mission.CurrentTime;
                if (memo != null)
                {
                    Agent cachedAlly = memo.stepAllyCached;
                    if (cachedAlly != null && now < memo.stepAllyExpiry && cachedAlly.IsActive())
                    {
                        float cachedDistance = unitPosition.Distance(cachedAlly.Position.AsVec2);
                        if (cachedDistance <= MathF.Min(memo.stepAllyRadius, StepAllyReuseMaxRange))
                        {
                            nearestAlly = cachedAlly;
                            nearestDistance = cachedDistance;
                        }
                    }
                }

                if (nearestAlly == null)
                {
                    float searchRadius = 1.5f;
                    MBList<Agent> nearbyAllyAgents = ScratchNearbyAllies;
                    mission.GetNearbyAllyAgents(unitPosition, 1.5f, unit.Team, nearbyAllyAgents);
                    if (nearbyAllyAgents.Count == 0)
                    {
                        searchRadius = 3f;
                        mission.GetNearbyAllyAgents(unitPosition, 3f, unit.Team, nearbyAllyAgents);
                    }
                    if (nearbyAllyAgents.Count == 0)
                    {
                        searchRadius = 20f;
                        mission.GetNearbyAllyAgents(unitPosition, 20f, unit.Team, nearbyAllyAgents);
                    }

                    for (int i = 0; i < nearbyAllyAgents.Count; i++)
                    {
                        Agent ally = nearbyAllyAgents[i];
                        if (ally == unit)
                        {
                            continue;
                        }
                        float allyDistance = unitPosition.Distance(ally.Position.AsVec2);
                        if (allyDistance < nearestDistance)
                        {
                            nearestAlly = ally;
                            nearestDistance = allyDistance;
                        }
                    }

                    if (memo != null && nearestAlly != null)
                    {
                        memo.stepAllyCached = nearestAlly;
                        memo.stepAllyRadius = searchRadius;
                        memo.stepAllyExpiry = now + StepAllyReuseMin + StepAllyReuseJitter * IndexJitter(unit.Index);
                    }
                }

                WorldPosition result = unit.GetWorldPosition();
                if (nearestAlly == null)
                {
                    return result;
                }
                // Already close enough: hold rather than push into them (aligned with IsPositionOccupied's 0.7 m probe).
                if (nearestDistance <= 0.9f)
                {
                    result.SetVec2MT(unitPosition);
                    return result;
                }

                Vec2 direction = (nearestAlly.Position.AsVec2 - unitPosition).Normalized();
                result.SetVec2MT(unitPosition + direction * MBRandom.RandomFloatRanged(0.15f, 0.3f));
                return result;
            }

            // True when the formation is within `range` of its nearest enemy formation. Cached per-tick state only (the same
            // fields native reads on the movement worker). Fails closed (false) with no enemy formation.
            private static bool IsFormationInEngagementRange(Formation formation, float range)
            {
                if (formation == null)
                {
                    return false;
                }
                FormationQuerySystem closestEnemy = formation.CachedClosestEnemyFormation;
                if (closestEnemy == null || closestEnemy.Formation == null)
                {
                    return false;
                }
                float distance = formation.CachedAveragePosition.Distance(closestEnemy.Formation.CachedMedianPosition.AsVec2);
                return distance <= range;
            }

            private const float CavalryEnemyCloseRadius = 15f; // riders; foot is 7 m
            // Slack for engine vs managed position differences.
            private const float CavalryEnemyCloseGateMargin = 2f;

            // Cavalry free-charge test: > 2 enemy riders within 15 m or > 2 enemy foot within 7 m (rider query first, shares one buffer).
            private static bool AreEnemiesCloseToRider(Mission mission, Agent unit)
            {
                MBList<Agent> enemiesCloseBy = ScratchEnemyQuery;
                mission.GetNearbyEnemyAgents(unit.Position.AsVec2, CavalryEnemyCloseRadius, unit.Team, enemiesCloseBy);
                if (CountByMounted(enemiesCloseBy, true) > 2)
                {
                    return true;
                }
                mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 7f, unit.Team, enemiesCloseBy);
                return CountByMounted(enemiesCloseBy, false) > 2;
            }

            // False only when no unit of any enemy formation can be within `radius`: the distance to a formation's bounding box
            // (from unit positions at most 0.1 s old) is a lower bound for each of its men. Formation-less enemies are not counted.
            private static bool IsEnemyFormationWithin(Mission mission, Agent unit, float radius)
            {
                Team ownTeam = unit.Team;
                Vec2 position = unit.Position.AsVec2;
                float radiusSquared = radius * radius;
                List<Team> teams = mission.Teams;
                for (int t = 0; t < teams.Count; t++)
                {
                    Team team = teams[t];
                    if (team == null || !team.IsEnemyOf(ownTeam))
                    {
                        continue;
                    }
                    MBList<Formation> formations = team.FormationsIncludingSpecialAndEmpty;
                    for (int f = 0; f < formations.Count; f++)
                    {
                        Formation formation = formations[f];
                        if (formation == null || formation.CountOfUnits == 0)
                        {
                            continue;
                        }
                        Utilities.FormationUnitSnapshot snapshot = Utilities.GetFormationUnitSnapshot(formation, Utilities.SnapshotMaxAge);
                        if (snapshot.Agents.Length > 0 && snapshot.DistanceSquaredToBounds(position) <= radiusSquared)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            // Approach discount: a friend moving toward the enemy at >= FrontMovingSpeed counts as FrontMovingBlockerWeight of a
            // blocker; below FrontStillSpeed a full one, linear between. Not applied while a clump stands ahead (ScanAhead).
            // Thresholds scale with the ally formation's run restriction (ShieldWall/Square 0.3, Circle 0.5, Line 0.8, Loose 0.9);
            // FrontStillFloor / FrontMovingMinSpan keep pile shoving reading as standing and the ramp from collapsing.
            private const float FrontStillSpeed = 1f;
            private const float FrontMovingSpeed = 2.5f;
            private const float FrontStillFloor = 0.5f;
            private const float FrontMovingMinSpan = 0.5f;
            private const float FrontMovingBlockerWeight = 0.25f;

            // Still / moving speed thresholds for this ally, scaled by his formation's run restriction. Managed reads only (worker-safe).
            private static void PaceThresholds(Agent ally, out float still, out float moving)
            {
                float pace = 1f;
                Formation allyFormation = ally.Formation;
                if (allyFormation != null)
                {
                    allyFormation.ArrangementOrder.GetMovementSpeedRestriction(out float? runRestriction, out float? walkRestriction);
                    pace = MathF.Clamp(runRestriction ?? 1f, 0.1f, 1f);
                }
                still = MathF.Max(FrontStillFloor, FrontStillSpeed * pace);
                moving = MathF.Max(still + FrontMovingMinSpan, FrontMovingSpeed * pace);
            }
            // Look-ahead probe: circle around the point ahead (reaches 1-8 m).
            private const float AheadProbeDistance = 4.5f;
            private const float AheadProbeRadius = 3.5f;
            // Standing clump: >= ClumpMinStanding foot allies 1+ m ahead within ClumpHalfWidth, slower than FrontStillSpeed (no discount).
            private const float ClumpMinAhead = 1f;
            private const float ClumpHalfWidth = 3f;
            private const int ClumpMinStanding = 4;
            // Close-up (Attack branch): nearest foot ally CloseUpMinAhead+ m ahead within CloseUpLaneHalfWidth of his line to the target.
            // Walks straight ahead until the ally is CloseUpGap (+ jitter) ahead, waits until the gap opens CloseUpResumeExtra more,
            // while the ally is within CloseUpMaxRange, ahead (cos >= CloseUpMinCos) and nearer than the target.
            private const float CloseUpMinAhead = 0.5f;
            private const float CloseUpLaneHalfWidth = 1.1f;
            private const float CloseUpMaxRange = 9f;
            private const float CloseUpMinCos = 0.7f;
            private const float CloseUpGap = 0.8f;
            private const float CloseUpGapJitter = 0.4f;
            private const float CloseUpResumeExtra = 1f;
            private const float CloseUpStep = 0.5f;
            // Impatience: after waiting CloseUpPatience (+ jitter), presses forward for CloseUpShoveSeconds, then waits again.
            private const float CloseUpPatience = 3f;
            private const float CloseUpPatienceJitter = 3f;
            private const float CloseUpShoveSeconds = 1.5f;

            // A close-up frame spent waiting: starts the wait clock, and a shove once his patience has run out.
            private static void TickCloseUpPatience(AIMindset mindset, float now, int index)
            {
                if (mindset.closeUpWaitSince == float.MinValue || now < mindset.closeUpWaitSince)
                {
                    mindset.closeUpWaitSince = now;
                }
                else if (now - mindset.closeUpWaitSince >= CloseUpPatience + CloseUpPatienceJitter * IndexJitter(index * 7 + 3))
                {
                    mindset.closeUpWaitSince = float.MinValue;
                    mindset.closeUpShoveUntil = now + CloseUpShoveSeconds;
                }
            }

            // One proximity query (global agent-query lock), only on decision frames when the discount applies or the decision is Attack:
            // standing allies ahead (clump test) and the nearest ally in his lane (close-up target).
            private static void ScanAhead(Mission mission, Agent unit, Vec2 unitPosition, Vec2 direction, out int standing, out Agent nearestInLane)
            {
                standing = 0;
                nearestInLane = null;
                float nearestDistanceSquared = float.MaxValue;
                Vec2 left = direction.LeftVec();
                MBList<Agent> ahead = ScratchClumpAhead;
                mission.GetNearbyAllyAgents(unitPosition + direction * AheadProbeDistance, AheadProbeRadius, unit.Team, ahead);
                for (int i = 0; i < ahead.Count; i++)
                {
                    Agent ally = ahead[i];
                    if (ally == unit || ally.HasMount || !ally.IsActive())
                    {
                        continue;
                    }
                    Vec2 offset = ally.Position.AsVec2 - unitPosition;
                    float forward = offset.DotProduct(direction);
                    float lateral = MathF.Abs(offset.DotProduct(left));
                    if (forward >= ClumpMinAhead && lateral <= ClumpHalfWidth)
                    {
                        PaceThresholds(ally, out float still, out float moving);
                        if (ally.AverageVelocity.AsVec2.DotProduct(direction) < still)
                        {
                            standing++;
                        }
                    }
                    if (forward >= CloseUpMinAhead && lateral <= CloseUpLaneHalfWidth)
                    {
                        float distanceSquared = offset.LengthSquared;
                        if (distanceSquared < nearestDistanceSquared)
                        {
                            nearestDistanceSquared = distanceSquared;
                            nearestInLane = ally;
                        }
                    }
                }
                // Don't keep agents reachable from a thread-static between frames.
                ahead.Clear();
            }

            // Allies ahead weighted by how much they block: an ally advancing the same way opens the space as fast as the unit fills it.
            // Uses AverageVelocity (smoothed; the raw one jitters). The unit itself keeps its full count.
            private static float CountFrontBlockers(MBList<Agent> alliesFront, Agent unit, Vec2 direction)
            {
                float count = 0f;
                for (int i = 0; i < alliesFront.Count; i++)
                {
                    Agent ally = alliesFront[i];
                    if (ally == unit)
                    {
                        count += 1f;
                        continue;
                    }
                    float forwardSpeed = ally.AverageVelocity.AsVec2.DotProduct(direction);
                    PaceThresholds(ally, out float stillSpeed, out float movingSpeed);
                    float moving = MathF.Clamp((forwardSpeed - stillSpeed) / (movingSpeed - stillSpeed), 0f, 1f);
                    count += MathF.Lerp(1f, FrontMovingBlockerWeight, moving);
                }
                return count;
            }

            private static int CountByMounted(MBList<Agent> agents, bool mounted)
            {
                int count = 0;
                for (int i = 0; i < agents.Count; i++)
                {
                    if ((agents[i].MountAgent != null) == mounted)
                    {
                        count++;
                    }
                }
                return count;
            }

            // Steps (0.15-0.5 m) are shorter than the 0.7 m probe, so a plain "anyone near the destination" test always fires in
            // a ~1 m-spaced formation. Judge against the status quo: a neighbour already pressed against does not block a step
            // that keeps or widens the gap. Agents are judged where they will be OccupancyLookahead from now (shift capped at
            // OccupancyMaxShift, query widened by the same amount); AverageVelocity is smoothed, the raw one jitters in melee.
            private const float OccupancyProbeRadius = 0.7f;
            private const float OccupancyLookahead = 0.5f;
            private const float OccupancyMaxShift = 0.5f;

            // Verdict reuse: 0.1-0.15 s, jittered by agent index; a spot more than OccupancyReuseMaxDrift from the one the verdict was taken for is probed afresh.
            private const float OccupancyReuseMin = 0.1f;
            private const float OccupancyReuseJitter = 0.05f;
            private const float OccupancyReuseMaxDrift = 0.5f;
            // A probe this close to the man's own spot says nothing about his neighbours: neither cached nor reused for a real step.
            private const float OccupancyMemoMinStep = 0.1f;

            // Deterministic per-agent value in [0, 1) for spreading memo expiries (MBRandom is not thread-safe; movement workers).
            private static float IndexJitter(int index)
            {
                return unchecked(((uint)index * 2654435761u) >> 16) * (1f / 65536f);
            }

            public static bool IsPositionOccupied(Mission mission, Vec2 position, Agent self)
            {
                // Deep-water gate: "occupied" makes the caller pin the man where he stands, so no pin lands in walled-off water.
                // Immutable grid read only (DeepWaterSlots.LiveGrid); false whenever the blocker is not live.
                if (DeepWaterSlots.IsBlocked(position))
                {
                    return true;
                }
                // No decision state: the plain query.
                if (!aiDecisionCooldownDict.TryGetValue(self, out AIDecisionState memo) || memo == null
                    || position.DistanceSquared(self.Position.AsVec2) < OccupancyMemoMinStep * OccupancyMemoMinStep)
                {
                    return QueryPositionOccupied(mission, position, self);
                }
                AIMindset mindset = memo.AIMindset;
                float now = mission.CurrentTime;
                if (now < memo.occupancyExpiry
                    && memo.occupancyDecision == mindset.currentDecision
                    && memo.occupancyDecisionTimer == mindset.AIDecisionTimer
                    && memo.occupancyPosition.IsValid
                    && memo.occupancyPosition.DistanceSquared(position) <= OccupancyReuseMaxDrift * OccupancyReuseMaxDrift)
                {
                    return memo.occupancyVerdict;
                }
                bool verdict = QueryPositionOccupied(mission, position, self);
                memo.occupancyVerdict = verdict;
                memo.occupancyPosition = position;
                memo.occupancyDecision = mindset.currentDecision;
                memo.occupancyDecisionTimer = mindset.AIDecisionTimer;
                memo.occupancyExpiry = now + OccupancyReuseMin + OccupancyReuseJitter * IndexJitter(self.Index);
                return verdict;
            }

            private static bool QueryPositionOccupied(Mission mission, Vec2 position, Agent self)
            {
                Vec2 currentPosition = self.Position.AsVec2;
                MBList<Agent> nearbyAgents = ScratchOccupancy;
                mission.GetNearbyAgents(position, OccupancyProbeRadius + OccupancyMaxShift, nearbyAgents);
                for (int i = 0; i < nearbyAgents.Count; i++)
                {
                    Agent other = nearbyAgents[i];
                    if (other == self || !other.IsActive())
                    {
                        continue;
                    }
                    Vec2 shift = other.AverageVelocity.AsVec2 * OccupancyLookahead;
                    float shiftLength = shift.Length;
                    if (shiftLength > OccupancyMaxShift)
                    {
                        shift *= OccupancyMaxShift / shiftLength;
                    }
                    Vec2 otherPosition = other.Position.AsVec2 + shift;
                    float distanceToStep = otherPosition.Distance(position);
                    if (distanceToStep < OccupancyProbeRadius && distanceToStep < otherPosition.Distance(currentPosition))
                    {
                        return true;
                    }
                }
                return false;
            }

            // ---- Static obstacles: rocks, walls, fences, RBM's deep-water walls ----
            // Every step branch re-pins the man 0.15-0.5 m ahead every frame (a moving carrot), so an obstacle in that direction
            // would be pressed into for the whole decision. Three gates, cheapest first:
            //  (b) deep water: the line to a probe point StepProbeLength ahead crosses a deep cell (immutable grid reads only).
            //  (a) navmesh: probe point not reachable within a short path budget (Mission.IsFormationUnitPositionAvailableMT,
            //      the one spatial query native itself makes from this parallel job; read-locked). Positions are fresh LOCAL
            //      WorldPosition copies (never a formation's cached order position). Memoised per agent (Reach* constants).
            //  (c) pressing: obstacles the navmesh does not cut out: a run of step pins that moved him less than PressMinProgress
            //      in PressWindow releases the step, with a PressReleaseCooldown. Positions and mission clock only.
            private const float StepProbeLength = 0.6f;
            private const float StepProbeSlack = 0.5f;
            private const float ReachReuseMin = 0.35f;
            private const float ReachReuseJitter = 0.15f;
            private const float ReachReuseMaxAge = 1.5f;
            private const float ReachReuseMaxDrift = 0.4f;
            private const float ReachReuseMinDot = 0.9f;
            private const float PressWindow = 2f;
            private const float PressMinProgress = 0.15f;
            private const float PressMaxGap = 0.3f;
            private const float PressMinDot = 0.7f;
            private const float PressReleaseCooldown = 2.5f;
            private const float PressReleaseRadius = 1.5f;

            // True when the step must not be taken (press-release cooldown, deep water on the line, or no navmesh reachability).
            // A step shorter than OccupancyMemoMinStep is a hold, never blocked. Never throws (worker thread).
            private static bool IsStepBlocked(Mission mission, Agent unit, AIDecisionState state, AIMindset.AIDecision decision, Vec2 unitPosition, Vec2 stepTarget)
            {
                if (state == null)
                {
                    return false;
                }
                try
                {
                    Vec2 delta = stepTarget - unitPosition;
                    float length = delta.Length;
                    if (!(length >= OccupancyMemoMinStep))
                    {
                        return false;
                    }
                    Vec2 direction = delta * (1f / length);
                    float now = mission.CurrentTime;

                    if (now < state.pressReleaseUntil && state.pressReleaseDecision == decision && state.pressReleasePosition.IsValid
                        && unitPosition.DistanceSquared(state.pressReleasePosition) <= PressReleaseRadius * PressReleaseRadius)
                    {
                        return true;
                    }

                    Vec2 probe = unitPosition + direction * StepProbeLength;
                    DeepWaterGrid grid = DeepWaterSlots.LiveGrid;
                    if (grid != null && DeepWaterSteering.CrossesDeepWater(grid, unitPosition, probe))
                    {
                        System.Threading.Interlocked.Increment(ref ObstacleDeepWaterRejects);
                        return true;
                    }

                    if (IsStepUnreachable(mission, unit, state, decision, unitPosition, direction, probe, now))
                    {
                        System.Threading.Interlocked.Increment(ref ObstacleUnreachableRejects);
                        return true;
                    }
                }
                catch
                {
                }
                return false;
            }

            // Navmesh verdict for a step, reused within a decision while younger than a jittered ReachReuseMin, or younger than
            // ReachReuseMaxAge with the man within ReachReuseMaxDrift and facing within ReachReuseMinDot. ~3 queries/s at most.
            private static bool IsStepUnreachable(Mission mission, Agent unit, AIDecisionState state, AIMindset.AIDecision decision, Vec2 unitPosition, Vec2 direction, Vec2 probe, float now)
            {
                if (state.reachOrigin.IsValid && state.reachDecision == decision && now >= state.reachTime)
                {
                    float age = now - state.reachTime;
                    if (age < ReachReuseMin + ReachReuseJitter * IndexJitter(unit.Index))
                    {
                        return state.reachBlocked;
                    }
                    if (age < ReachReuseMaxAge
                        && state.reachOrigin.DistanceSquared(unitPosition) <= ReachReuseMaxDrift * ReachReuseMaxDrift
                        && state.reachDirection.DotProduct(direction) >= ReachReuseMinDot)
                    {
                        return state.reachBlocked;
                    }
                }
                bool blocked = QueryStepUnreachable(mission, unit, probe);
                state.reachBlocked = blocked;
                state.reachOrigin = unitPosition;
                state.reachDirection = direction;
                state.reachDecision = decision;
                state.reachTime = now;
                return blocked;
            }

            // One engine check: is `probe` reachable within a path budget barely above the straight distance? Fails open
            // (false = not blocked) when his position has no navmesh or anything throws.
            private static bool QueryStepUnreachable(Mission mission, Agent unit, Vec2 probe)
            {
                System.Threading.Interlocked.Increment(ref ObstacleProbeQueries);
                try
                {
                    WorldPosition from = unit.GetWorldPosition();
                    if (!from.IsValid || from.GetNavMeshMT() == UIntPtr.Zero)
                    {
                        return false;
                    }
                    Vec2 origin = from.AsVec2;
                    WorldPosition to = from;
                    to.SetVec2MT(probe);
                    WorldPosition nearest = WorldPosition.Invalid;
                    float manhattan = MathF.Abs(probe.x - origin.x) + MathF.Abs(probe.y - origin.y) + StepProbeSlack;
                    return !mission.IsFormationUnitPositionAvailableMT(ref from, ref to, ref nearest, manhattan, unit.Team);
                }
                catch
                {
                    return false;
                }
            }

            // Called when a step pin is about to be set. True = pinned this way for PressWindow without PressMinProgress: drop the
            // step and start the cooldown. Never throws (worker thread).
            private static bool IsPressingAgainstObstacle(Mission mission, Agent unit, AIDecisionState state, AIMindset.AIDecision decision, Vec2 unitPosition, Vec2 stepTarget)
            {
                if (state == null)
                {
                    return false;
                }
                try
                {
                    Vec2 delta = stepTarget - unitPosition;
                    float length = delta.Length;
                    if (!(length >= OccupancyMemoMinStep))
                    {
                        return false;
                    }
                    Vec2 direction = delta * (1f / length);
                    float now = mission.CurrentTime;
                    if (!state.pressAnchor.IsValid || state.pressDecision != decision || now < state.pressAnchorTime
                        || now - state.pressLastSeen > PressMaxGap || state.pressDirection.DotProduct(direction) < PressMinDot)
                    {
                        state.pressAnchor = unitPosition;
                        state.pressDirection = direction;
                        state.pressDecision = decision;
                        state.pressAnchorTime = now;
                        state.pressLastSeen = now;
                        return false;
                    }
                    state.pressLastSeen = now;
                    if (now - state.pressAnchorTime < PressWindow)
                    {
                        return false;
                    }
                    float progress = (unitPosition - state.pressAnchor).DotProduct(state.pressDirection);
                    state.pressAnchor = unitPosition;
                    state.pressDirection = direction;
                    state.pressAnchorTime = now;
                    if (progress >= PressMinProgress)
                    {
                        return false;
                    }
                    state.pressAnchor = Vec2.Invalid;
                    state.pressReleaseDecision = decision;
                    state.pressReleasePosition = unitPosition;
                    state.pressReleaseUntil = now + PressReleaseCooldown;
                    System.Threading.Interlocked.Increment(ref ObstaclePressReleases);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
