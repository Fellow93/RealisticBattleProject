using HarmonyLib;
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
        [HarmonyPatch(typeof(Formation))]
        private class OverrideFormation
        {
            private static readonly PropertyInfo LastRangedAttackTimeProperty =
                typeof(Agent).GetProperty("LastRangedAttackTime");

            // Mission.GetNearby*Agents clears the list it is handed before filling it (verified against
            // TaleWorlds.MountAndBlade.Mission), so these scratch buffers can be reused instead of allocating a
            // fresh MBList per query -- this prefix ran 4-7 allocations per agent per tick.
            // [ThreadStatic] is load-bearing: the prefix runs on the parallel formation-movement job, so a single
            // shared buffer would be torn between worker threads. Each worker gets its own, lazily created.
            [ThreadStatic] private static MBList<Agent> _scratchAlliesFront;
            [ThreadStatic] private static MBList<Agent> _scratchAlliesLeft;
            [ThreadStatic] private static MBList<Agent> _scratchAlliesRight;
            [ThreadStatic] private static MBList<Agent> _scratchEnemiesFront;
            [ThreadStatic] private static MBList<Agent> _scratchEnemyQuery;
            [ThreadStatic] private static MBList<Agent> _scratchOccupancy;
            [ThreadStatic] private static MBList<Agent> _scratchNearbyAllies;

            private static MBList<Agent> ScratchAlliesFront => _scratchAlliesFront ?? (_scratchAlliesFront = new MBList<Agent>());
            private static MBList<Agent> ScratchAlliesLeft => _scratchAlliesLeft ?? (_scratchAlliesLeft = new MBList<Agent>());
            private static MBList<Agent> ScratchAlliesRight => _scratchAlliesRight ?? (_scratchAlliesRight = new MBList<Agent>());
            private static MBList<Agent> ScratchEnemiesFront => _scratchEnemiesFront ?? (_scratchEnemiesFront = new MBList<Agent>());
            private static MBList<Agent> ScratchEnemyQuery => _scratchEnemyQuery ?? (_scratchEnemyQuery = new MBList<Agent>());
            private static MBList<Agent> ScratchOccupancy => _scratchOccupancy ?? (_scratchOccupancy = new MBList<Agent>());
            private static MBList<Agent> ScratchNearbyAllies => _scratchNearbyAllies ?? (_scratchNearbyAllies = new MBList<Agent>());

            [HarmonyPostfix]
            [HarmonyPatch("GetDirectionOfUnit")]
            public static void Postfix_GetDirectionOfUnit(Formation __instance, Agent unit, ref Vec2 __result)
            {
                try
                {
                    Mission mission = Mission.Current;
                    if (mission == null || (!mission.IsFieldBattle && !mission.IsNavalBattle) || unit == null || !__instance.QuerySystem.IsInfantryFormation)
                    {
                        return;
                    }
                    MovementOrder order = __instance.GetReadonlyMovementOrderReference();
                    if (order.OrderType != OrderType.Charge && order.OrderType != OrderType.ChargeWithTarget && order.OrderType != OrderType.Advance && order.OrderType != OrderType.FollowMe && order.OrderType != OrderType.FollowEntity)
                    {
                        return;
                    }
                    Agent targetAgent = Utilities.GetCorrectTarget(unit);
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

            [HarmonyPrefix]
            [HarmonyPatch("GetOrderPositionOfUnit")]
            private static bool PrefixGetOrderPositionOfUnit(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
            {
                Mission mission = Mission.Current;

                if (unit == null || !unit.IsActive() || mission == null || !mission.IsDeploymentFinished || unit.IsPlayerControlled)
                {
                    return true;
                }
                if (mission.IsSiegeBattle)
                {
                    if (unit.Position == null || unit.Team == null)
                    {
                        return true;
                    }
                    //everyone charge if close to enemy in siege battle
                    MBList<Agent> enemiesCloseBy = ScratchEnemyQuery;
                    mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 2.25f, unit.Team, enemiesCloseBy);
                    if (enemiesCloseBy.Count > 0)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                }
                //for cavalry
                if (mission != null && mission.IsFieldBattle && unit != null && __instance.IsAIControlled && (__instance.QuerySystem.IsCavalryFormation || __instance.QuerySystem.IsRangedCavalryFormation))
                {
                    //cav cahrge if no mount
                    if (unit != null && unit.MountAgent == null)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                    // Both checks below produce the same outcome, so the cavalry one is evaluated first and the
                    // infantry query is skipped entirely when it already fires. That also lets both share one buffer.
                    MBList<Agent> enemiesCloseBy = ScratchEnemyQuery;

                    //cav charge if close to enemy cavalry
                    mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 15f, unit.Team, enemiesCloseBy);
                    if (CountByMounted(enemiesCloseBy, true) > 2)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                    //cav charge if close to enemy infantry
                    mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 7f, unit.Team, enemiesCloseBy);
                    if (CountByMounted(enemiesCloseBy, false) > 2)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                }
                if (mission != null && mission.IsFieldBattle && unit != null && __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget && __instance.QuerySystem.IsCavalryFormation)
                {
                    var targetAgent = unit.GetTargetAgent();
                    if (__instance.IsAIControlled)
                    {
                        targetAgent = Utilities.GetCorrectTarget(unit);
                    }
                    if (targetAgent != null)
                    {
                        float distance = (targetAgent.Position - unit.Position).Length;
                        if (distance > 60f)
                        {
                            __result = targetAgent.GetWorldPosition();
                            return false;
                        }
                    }
                }
                //for range
                if (mission != null && unit != null && __instance.IsAIControlled && mission.IsFieldBattle && __instance.QuerySystem.IsRangedFormation)
                {
                    //ranged charge if close to enemy
                    MBList<Agent> enemiesCloseBy = ScratchEnemyQuery;
                    mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 2.5f, unit.Team, enemiesCloseBy);
                    if (enemiesCloseBy.Count > 0)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                    //ranged charge if they are skirmishing but not attacking
                    if (__instance.AI != null && __instance.AI.ActiveBehavior != null)
                    {
                        if (unit.LastRangedAttackTime > 0)
                        {
                            Type activeBehaviorType = __instance.AI.ActiveBehavior.GetType();
                            if (activeBehaviorType == typeof(RBMBehaviorArcherFlank) || activeBehaviorType == typeof(RBMBehaviorArcherSkirmish)
                                || activeBehaviorType == typeof(BehaviorSkirmish) || activeBehaviorType == typeof(BehaviorSkirmishBehindFormation) || activeBehaviorType == typeof(BehaviorSkirmishLine))
                            {
                                MBList<Agent> enemyCloseBy = ScratchEnemyQuery;
                                mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 15f, unit.Team, enemyCloseBy);
                                float currentTime = MBCommon.GetTotalMissionTime();
                                if (currentTime - unit.LastMeleeAttackTime > 10f && currentTime - unit.LastMeleeHitTime > 10f)
                                {
                                    if (currentTime - unit.LastRangedAttackTime > 50f)
                                    {
                                        LastRangedAttackTimeProperty.SetValue(unit, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                                    }
                                    if (currentTime - unit.LastRangedAttackTime > 20f && enemyCloseBy.Count < 3)
                                    {
                                        __result = WorldPosition.Invalid;
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }

                AIDecisionState aiDecision;
                bool hasDecisionState = aiDecisionCooldownDict.TryGetValue(unit, out aiDecision);

                if (hasDecisionState && aiDecision.AIMindset.shouldClearTargetFrame)
                {
                    unit.ClearTargetFrame();
                    aiDecision.AIMindset.shouldClearTargetFrame = false;
                }

                // Below this size the system does nothing, so don't build decision state for the unit.
                // The pending clear above still runs first: formations shrink as they take casualties, so
                // a unit can fall under the threshold still owing a frame clear from an earlier tick.
                if (__instance.CountOfUnitsWithoutDetachedOnes <= 25)
                {
                    return true;
                }

                if (!hasDecisionState)
                {
                    aiDecision = new AIDecisionState();
                    aiDecisionCooldownDict[unit] = aiDecision;
                }

                if (mission != null && mission.IsFieldBattle && (__instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge) && (__instance.QuerySystem.IsInfantryFormation || __instance.QuerySystem.IsRangedFormation) && !____detachedUnits.Contains(unit))
                {
                    Agent targetAgent;
                    var vanillaTargetAgent = targetAgent = unit.GetTargetAgent();
                    if (__instance.IsAIControlled)
                    {
                        targetAgent = Utilities.GetCorrectTarget(unit);
                    }
                    else
                    {
                        if (__instance.TargetFormation == null)
                        {
                            targetAgent = Utilities.GetCorrectTarget(unit);
                        }
                        else
                        {
                            targetAgent = Utilities.NearestAgentFromFormation(unit.GetWorldPosition().AsVec2, __instance.TargetFormation);
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

                        // These four must be distinct buffers: every Count below is read after all four are filled.
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
                        if (RBMConfig.RBMConfig.postureEnabled)
                        {
                            Stance stance = null;
                            AgentStances.values.TryGetValue(unit, out stance);
                            // RecalculatePosture rebuilds these maxima additively from skills and gear, so a
                            // zero is reachable. Dividing by it yields NaN, which Lerp propagates straight into
                            // every decision score. Leave the modifier at its neutral 1f instead.
                            if (unit != null && stance != null)
                            {
                                if (stance.maxPosture > 0f)
                                {
                                    postureModifier = MathF.Lerp(0.1f, 1f, stance.posture / stance.maxPosture);
                                }
                                if (stance.maxStamina > 0f)
                                {
                                    staminaModifier = MathF.Lerp(0.33f, 1f, stance.stamina / stance.maxStamina);
                                }
                            }
                        }

                        float healthModifier = MathF.Lerp(0.33f, 1f, unit.Health / unit.HealthLimit);
                        bool isSoldier = unit.Character.IsSoldier;

                        int alliesFrontCount = LimitCount(alliesFront.Count, 10);
                        int alliesLeftCount = LimitCount(alliesLeft.Count, 5);
                        int alliesRightCount = LimitCount(alliesRight.Count, 5);
                        int enemiesFrontCount = LimitCount(enemiesFront.Count, 10);

                        int hasShieldAdditive = 0;
                        int hasTwoHandedEquippedAddtive = 0;
                        if (!unit.WieldedOffhandWeapon.IsEmpty && unit.WieldedOffhandWeapon.IsShield())
                        {
                            hasShieldAdditive += 1;
                        }
                        if (__instance.ArrangementOrder == ArrangementOrder.ArrangementOrderShieldWall)
                        {
                            hasShieldAdditive += 2;
                        }
                        if (__instance.ArrangementOrder == ArrangementOrder.ArrangementOrderLoose)
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
                        float flankAllyLeft = (alliesFrontCount * 1.25f) + (alliesRightCount) - (alliesLeftCount) - enemiesFrontCount;
                        float flankAllyRight = (alliesFrontCount * 1.25f) + (alliesLeftCount) - (alliesRightCount) - enemiesFrontCount;

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

                        aiDecision.AIMindset.SetValue(AIMindset.AIDecision.Attack, attack > 0 ? attack * (postureModifier * healthModifier) : attack);
                        aiDecision.AIMindset.SetValue(AIMindset.AIDecision.BackStep, fallback > 0 ? (fallback * (2 - postureModifier)) : fallback);
                        aiDecision.AIMindset.SetValue(AIMindset.AIDecision.FindAlly, findAlly > 0 ? (findAlly * (2 - postureModifier)) : findAlly);
                        aiDecision.AIMindset.SetValue(AIMindset.AIDecision.FlankAllyLeft, flankAllyLeft > 0 ? (flankAllyLeft) : flankAllyLeft);
                        aiDecision.AIMindset.SetValue(AIMindset.AIDecision.FlankAllyRight, flankAllyRight > 0 ? (flankAllyRight) : flankAllyRight);

                        //bool checkTimer = aiDecision.AIMindset.AIDecisionTimer != null ? aiDecision.AIMindset.AIDecisionTimer.Check(Mission.Current.CurrentTime) : true;
                        //aiDecision.AIMindset.AIDecisionTimer = null;
                        if (aiDecision.AIMindset.AIDecisionTimer == null)
                        {
                            if (postureModifier < 0.5f && enemiesFrontCount == 0)
                            {
                                aiDecision.AIMindset.currentDecision = AIMindset.AIDecision.Rest;
                            }
                            else
                            {
                                aiDecision.AIMindset.getDecision(out aiDecision.AIMindset.currentDecision);
                            }
                            aiDecision.AIMindset.AIDecisionTimer = new Timer(Mission.Current.CurrentTime, MBRandom.RandomFloatRanged(0f, 2f), false);
                        }
                        else
                        {
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
                                            if (IsPositionOccupied(mission, targetVec2, unit))
                                            {
                                                __result = unit.GetWorldPosition();
                                                unit.SetTargetPosition(unitPosition);
                                                return false;
                                            }
                                            targetPosition.SetVec2(targetVec2);
                                            __result = targetAgent.GetWorldPosition();
                                            unit.SetTargetPosition(targetPosition.AsVec2);
                                            return false;
                                        }
                                    }
                                    return true;
                                }
                            case AIMindset.AIDecision.BackStep:
                                {
                                    WorldPosition backPosition = unit.GetWorldPosition();
                                    Vec2 backVec2 = unitPosition - ((direction + __instance.Direction) / 2f) * MBRandom.RandomFloatRanged(0, 0.3f);
                                    if (IsPositionOccupied(mission, backVec2, unit))
                                    {
                                        __result = unit.GetWorldPosition();
                                        unit.SetTargetPosition(unitPosition);
                                        return false;
                                    }
                                    backPosition.SetVec2(backVec2);
                                    unit.SetTargetPosition(backPosition.AsVec2);
                                    __result = backPosition;
                                    return false;
                                }
                            case AIMindset.AIDecision.FindAlly:
                                {
                                    WorldPosition allyPosition = GetStepTowardNearbyAlly(mission, unitPosition, unit);
                                    Vec2 allyVec2 = allyPosition.AsVec2;
                                    if (IsPositionOccupied(mission, allyVec2, unit))
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
                                    if (IsPositionOccupied(mission, leftTargetVec2, unit))
                                    {
                                        __result = unit.GetWorldPosition();
                                        unit.SetTargetPosition(unitPosition);
                                        return false;
                                    }
                                    leftPosition.SetVec2(leftTargetVec2);
                                    __result = leftPosition;
                                    unit.SetTargetPosition(leftPosition.AsVec2);
                                    return false;
                                }
                            case AIMindset.AIDecision.FlankAllyRight:
                                {
                                    WorldPosition rightPosition = unit.GetWorldPosition();
                                    Vec2 rightTargetVec2 = unitPosition + rightVec * MBRandom.RandomFloatRanged(0.15f, 0.3f);
                                    if (IsPositionOccupied(mission, rightTargetVec2, unit))
                                    {
                                        __result = unit.GetWorldPosition();
                                        unit.SetTargetPosition(unitPosition);
                                        return false;
                                    }
                                    rightPosition.SetVec2(rightTargetVec2);
                                    __result = rightPosition;
                                    unit.SetTargetPosition(rightPosition.AsVec2);
                                    return false;
                                }
                        }
                    }
                }

                return true;
            }

            // Returns a finished target position -- a short step toward the nearest ally, not the ally's own
            // position. Callers must use it as-is; scaling it again shrinks the step to nothing.
            public static WorldPosition GetStepTowardNearbyAlly(Mission mission, Vec2 unitPosition, Agent unit)
            {
                MBList<Agent> nearbyAllyAgents = ScratchNearbyAllies;
                mission.GetNearbyAllyAgents(unitPosition, 1.5f, unit.Team, nearbyAllyAgents);
                if (nearbyAllyAgents.Count == 0)
                {
                    mission.GetNearbyAllyAgents(unitPosition, 3f, unit.Team, nearbyAllyAgents);
                }
                if (nearbyAllyAgents.Count == 0)
                {
                    mission.GetNearbyAllyAgents(unitPosition, 20f, unit.Team, nearbyAllyAgents);
                }

                Agent nearestAlly = null;
                float nearestDistance = 10000f;
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

                WorldPosition result = unit.GetWorldPosition();
                if (nearestAlly == null)
                {
                    return result;
                }
                // Already close enough -- hold rather than push into them. Roughly aligned with
                // IsPositionOccupied: a max step (0.3m) toward an ally nearer than ~1.0m would land inside its
                // 0.7m probe and be vetoed anyway, so attempting it only burned the decision window in place.
                if (nearestDistance <= 0.9f)
                {
                    result.SetVec2(unitPosition);
                    return result;
                }

                Vec2 direction = (nearestAlly.Position.AsVec2 - unitPosition).Normalized();
                result.SetVec2(unitPosition + direction * MBRandom.RandomFloatRanged(0.15f, 0.3f));
                return result;
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

            // The steps this system takes (0.15-0.5m) are far shorter than the 0.7m probe, so a plain
            // "is anyone near the destination" test re-detects the neighbours the unit is already standing
            // next to -- in a formation at ~1m spacing that is always true, and every branch collapsed to
            // stand-still. Judge the step against the status quo instead: a neighbour the unit is already
            // pressed against does not block a step that keeps or widens the gap to them.
            public static bool IsPositionOccupied(Mission mission, Vec2 position, Agent self)
            {
                Vec2 currentPosition = self.Position.AsVec2;
                MBList<Agent> nearbyAgents = ScratchOccupancy;
                mission.GetNearbyAgents(position, 0.7f, nearbyAgents);
                for (int i = 0; i < nearbyAgents.Count; i++)
                {
                    Agent other = nearbyAgents[i];
                    if (other == self || !other.IsActive())
                    {
                        continue;
                    }
                    Vec2 otherPosition = other.Position.AsVec2;
                    if (otherPosition.Distance(position) < otherPosition.Distance(currentPosition))
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
