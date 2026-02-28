using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.Formation;
using static TaleWorlds.MountAndBlade.MovementOrder;

namespace RBMAI
{
    public class AgentPanicFix : MissionLogic
    {
        public override void OnAgentPanicked(Agent affectedAgent)
        {
            affectedAgent.ClearTargetFrame();
        }

        public override void OnAgentControllerSetToPlayer(Agent agent)
        {
            agent.ClearTargetFrame();
        }
    }

    public static class Frontline
    {
        public static ConcurrentDictionary<Agent, AIDecision> aiDecisionCooldownDict = new ConcurrentDictionary<Agent, AIDecision>();

        public class AIMindset
        {
            public Timer AIDecisionTimer = null;
            public AIDecision currentDecision = AIDecision.Attack;

            public Boolean shouldClearTargetFrame = false;

            public enum AIDecision
            {
                Attack,
                BackStep,
                FindAlly,
                FlankAllyLeft,
                FlankAllyRight,
                Rest
            }

            public float fallback = 0;
            public float attack = 50;
            public float findAlly = 0;
            public float flankAllyLeft = 0;
            public float flankAllyRight = 0;

            public float fallBackBase = 0;
            public float attackBase = 8;
            public float findAllyBase = 0;
            public float flankAllyLeftBase = 0;
            public float flankAllyRightBase = 0;

            public void SetValue(AIDecision decision, float value)
            {
                float changedValue = 0;
                float changedValueBase = 0;
                float changedValueFromBase = 0;
                switch (decision)
                {
                    case AIDecision.Attack:
                        {
                            changedValue = attack + value;
                            changedValueBase = attackBase;
                            changedValueFromBase = changedValue - changedValueBase;
                            break;
                        }
                    case AIDecision.BackStep:
                        {
                            changedValue = fallback + value;
                            changedValueBase = fallBackBase;
                            changedValueFromBase = changedValue - changedValueBase;
                            break;
                        }
                    case AIDecision.FindAlly:
                        {
                            changedValue = findAlly + value;
                            changedValueBase = findAllyBase;
                            changedValueFromBase = changedValue - changedValueBase;
                            break;
                        }
                    case AIDecision.FlankAllyLeft:
                        {
                            changedValue = flankAllyLeft + value;
                            changedValueBase = flankAllyLeftBase;
                            changedValueFromBase = changedValue - changedValueBase;
                            break;
                        }
                    case AIDecision.FlankAllyRight:
                        {
                            changedValue = flankAllyRight + value;
                            changedValueBase = flankAllyRightBase;
                            changedValueFromBase = changedValue - changedValueBase;
                            break;
                        }
                }
                if (changedValueFromBase > 0)
                {
                    float valueToReduce = (float)Math.Floor(Math.Sqrt(Math.Abs(changedValueFromBase)));
                    changedValue -= valueToReduce;
                }
                else
                {
                    float valueToAdd = (float)Math.Floor(Math.Sqrt(Math.Abs(changedValueFromBase)));
                    changedValue += valueToAdd;
                }
                changedValue = Math.Min(100, changedValue);
                changedValue = Math.Max(0, changedValue);

                switch (decision)
                {
                    case AIDecision.Attack:
                        {
                            attack = changedValue;
                            break;
                        }
                    case AIDecision.BackStep:
                        {
                            fallback = changedValue;
                            break;
                        }
                    case AIDecision.FindAlly:
                        {
                            findAlly = changedValue;
                            break;
                        }
                    case AIDecision.FlankAllyLeft:
                        {
                            flankAllyLeft = changedValue;
                            break;
                        }
                    case AIDecision.FlankAllyRight:
                        {
                            flankAllyRight = changedValue;
                            break;
                        }
                }
            }

            public void getDecision(out AIDecision decisionType)
            {
                Dictionary<AIDecision, float> decisionValues = new Dictionary<AIDecision, float>
                {
                    { AIDecision.Attack, attack },
                    { AIDecision.BackStep, fallback },
                    { AIDecision.FindAlly, findAlly },
                    { AIDecision.FlankAllyLeft, flankAllyLeft },
                    { AIDecision.FlankAllyRight, flankAllyRight }
                };
                decisionType = decisionValues.Aggregate((l, r) => l.Value > r.Value ? l : r).Key;
            }
        }

        public class AIDecision
        {
            public AIMindset AIMindset = new AIMindset();
        }

        public static bool IsActivelyAttacking(Agent agent)
        {
            //switch (agent.AttackDirection)
            //{
            //    case Agent.UsageDirection.AttackDown:
            //    case Agent.UsageDirection.AttackLeft:
            //    case Agent.UsageDirection.AttackRight:
            //    case Agent.UsageDirection.AttackEnd:
            //    case Agent.UsageDirection.AttackAny:
            //        {
            //            return true;
            //        }
            //}
            return false;
            Agent.ActionCodeType currentActionType = agent.GetCurrentActionType(1);
            if (
                currentActionType == Agent.ActionCodeType.ReadyMelee ||
                currentActionType == Agent.ActionCodeType.ReleaseRanged ||
                currentActionType == Agent.ActionCodeType.ReleaseThrowing)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static float NormalizeCount(int count, int max)
        {
            return MathF.Min(count / (float)max, 1f);
        }

        public static int LimitCount(int count, int max)
        {
            return MathF.Min(max, count);
        }

        [HarmonyPatch(typeof(Formation))]
        private class OverrideFormation
        {
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
                            Vec2 directionToEnemy = (__result = (targetAgent.Position.AsVec2 - unit.Position.AsVec2).Normalized());
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
                    MBList<Agent> enemiesCloseBy = new MBList<Agent>();
                    enemiesCloseBy = mission?.GetNearbyEnemyAgents(unit.Position.AsVec2, 2.25f, unit.Team, enemiesCloseBy);
                    if (enemiesCloseBy?.Count > 0)
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
                    MBList<Agent> enemyCavalryCloseBy = new MBList<Agent>();
                    enemyCavalryCloseBy = mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 15f, unit.Team, enemyCavalryCloseBy);
                    enemyCavalryCloseBy.RemoveAll(x => x.MountAgent == null);
                    MBList<Agent> enemyInfantryCloseBy = new MBList<Agent>();
                    enemyInfantryCloseBy = mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 7f, unit.Team, enemyInfantryCloseBy);
                    enemyInfantryCloseBy.RemoveAll(x => x.MountAgent != null);
                    //cav charge if close to enemy cavalry
                    if (enemyCavalryCloseBy.Count > 2)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                    //cav charge if close to enemy infantry
                    if (enemyInfantryCloseBy.Count > 2)
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
                    MBList<Agent> enemiesCloseBy = new MBList<Agent>();
                    enemiesCloseBy = mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 2.5f, unit.Team, enemiesCloseBy);
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
                                MBList<Agent> enemyCloseBy = new MBList<Agent>();
                                enemyCloseBy = mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 15f, unit.Team, enemyCloseBy);
                                float currentTime = MBCommon.GetTotalMissionTime();
                                if (currentTime - unit.LastMeleeAttackTime > 10f && currentTime - unit.LastMeleeHitTime > 10f)
                                {
                                    if (currentTime - unit.LastRangedAttackTime > 50f)
                                    {
                                        PropertyInfo LastRangedAttackTime = typeof(Agent).GetProperty("LastRangedAttackTime");
                                        LastRangedAttackTime.DeclaringType.GetProperty("LastRangedAttackTime");
                                        LastRangedAttackTime.SetValue(unit, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
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

                AIDecision aiDecision;
                if (!aiDecisionCooldownDict.TryGetValue(unit, out aiDecision))
                {
                    aiDecisionCooldownDict[unit] = new AIDecision();
                    aiDecisionCooldownDict.TryGetValue(unit, out aiDecision);
                }

                if (aiDecision.AIMindset.shouldClearTargetFrame)
                {
                    unit.ClearTargetFrame();
                    aiDecision.AIMindset.shouldClearTargetFrame = false;
                }

                if (__instance.CountOfUnitsWithoutDetachedOnes <= 25)
                {
                    return true;
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

                        MBList<Agent> alliesFront = new MBList<Agent>();
                        MBList<Agent> alliesLeft = new MBList<Agent>();
                        MBList<Agent> alliesRight = new MBList<Agent>();

                        MBList<Agent> enemiesFront = new MBList<Agent>();

                        alliesFront = mission.GetNearbyAllyAgents(unitPosition + direction * 1.35f, 1.35f, unit.Team, alliesFront);
                        alliesLeft = mission.GetNearbyAllyAgents(unitPosition + leftVec * 1.35f, 1.35f, unit.Team, alliesLeft);
                        alliesRight = mission.GetNearbyAllyAgents(unitPosition + rightVec * 1.35f, 1.35f, unit.Team, alliesRight);

                        enemiesFront = mission.GetNearbyEnemyAgents(unitPosition + direction * 1.5f, 2f, unit.Team, enemiesFront);

                        float postureModifier = 1f;
                        float staminaModifier = 1f;
                        if (RBMConfig.RBMConfig.postureEnabled)
                        {
                            __result = targetAgent != null ? targetAgent.GetWorldPosition() : WorldPosition.Invalid;
                            Stance stance = null;
                            AgentStances.values.TryGetValue(unit, out stance);
                            if (unit != null && stance != null)
                            {
                                postureModifier = MathF.Lerp(0.1f, 1f, stance.posture / stance.maxPosture);
                                staminaModifier = MathF.Lerp(0.33f, 1f, stance.stamina / stance.maxStamina);
                            }
                        }

                        int unitTier = unit.Character.GetBattleTier();
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

                        bool isBannerBearer = false;
                        if (!unit.WieldedOffhandWeapon.IsEmpty && unit.WieldedOffhandWeapon.CurrentUsageItem != null && unit.WieldedOffhandWeapon.CurrentUsageItem.WeaponClass == WeaponClass.Banner)
                        {
                            isBannerBearer = true;
                        }
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
                                    WorldPosition allyPosition = unit.GetWorldPosition();
                                    Vec2 directionToAlly = getNearbyAllyWorldPosition(mission, unitPosition, unit).AsVec2 - unitPosition;
                                    Vec2 allyVec2 = unitPosition + directionToAlly * MBRandom.RandomFloatRanged(0.15f, 0.3f);
                                    if (IsPositionOccupied(mission, allyVec2, unit))
                                    {
                                        __result = unit.GetWorldPosition();
                                        unit.SetTargetPosition(unitPosition);
                                        return false;
                                    }
                                    allyPosition.SetVec2(allyVec2);
                                    __result = allyPosition;
                                    unit.SetTargetPosition(allyPosition.AsVec2);
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

            public static WorldPosition getNearbyAllyWorldPosition(Mission mission, Vec2 unitPosition, Agent unit)
            {
                MBList<Agent> nearbyAllyAgents = new MBList<Agent>();
                nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 1.5f, unit.Team, nearbyAllyAgents);
                if (nearbyAllyAgents.Count == 0)
                {
                    nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 3f, unit.Team, nearbyAllyAgents);
                }
                if (nearbyAllyAgents.Count == 0)
                {
                    nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 20f, unit.Team, nearbyAllyAgents);
                }
                if (nearbyAllyAgents.Count > 0)
                {
                    List<Agent> allyAgentList = nearbyAllyAgents.ToList();
                    allyAgentList.Remove(unit);
                    if (allyAgentList.Count == 0)
                    {
                        return unit.GetWorldPosition();
                    }
                    if (allyAgentList.Count == 1)
                    {
                        return allyAgentList[0].GetWorldPosition();
                    }
                    float dist = 10000f;
                    WorldPosition result = unit.GetWorldPosition();

                    //result = allyAgentList.GetRandomElement().GetWorldPosition();

                    foreach (Agent agent in allyAgentList)
                    {
                        if (agent != unit)
                        {
                            float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                            if (dist > newDist)
                            {
                                result = agent.GetWorldPosition();
                                dist = newDist;
                            }
                        }
                    }

                    Vec2 direction = (result.AsVec2 - unitPosition).Normalized();
                    float distance = unitPosition.Distance(result.AsVec2);
                    if (distance > 0.6f)
                    {
                        result.SetVec2(unitPosition + direction * MBRandom.RandomFloatRanged(0.15f, 0.3f));
                    }
                    else
                    {
                        result.SetVec2(unitPosition);
                    }

                    return result;
                }
                else
                {
                    return unit.GetWorldPosition();
                }
            }

            public static bool IsPositionOccupied(Mission mission, Vec2 position, Agent self)
            {
                MBList<Agent> nearbyAgents = new MBList<Agent>();
                nearbyAgents = mission.GetNearbyAgents(position, 0.7f, nearbyAgents);
                for (int i = 0; i < nearbyAgents.Count; i++)
                {
                    if (nearbyAgents[i] != self && nearbyAgents[i].IsActive())
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    internal class OverrideFormation
    {
        [HarmonyPostfix]
        [HarmonyPatch("ParallelUpdateFormationMovement")]
        private static void PostfixParallelUpdateFormationMovement(ref HumanAIComponent __instance, ref Agent ___Agent)
        {
            if (___Agent.IsActive() == false || ___Agent.Formation == null)
            {
                return;
            }
            MovementOrderEnum orderType = ___Agent.Formation.GetReadonlyMovementOrderReference().OrderEnum;
            if (___Agent.Controller == AgentControllerType.AI && orderType == MovementOrderEnum.Move && ___Agent.Formation.ArrangementOrder != ArrangementOrder.ArrangementOrderColumn)
            {
                PropertyInfo propertyShouldCatchUpWithFormation = typeof(HumanAIComponent).GetProperty("ShouldCatchUpWithFormation");
                propertyShouldCatchUpWithFormation.DeclaringType.GetProperty("ShouldCatchUpWithFormation");
                propertyShouldCatchUpWithFormation.SetValue(__instance, true, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                Vec2 currentGlobalPositionOfUnit = ___Agent.Formation.GetCurrentGlobalPositionOfUnit(___Agent, false);
                FormationIntegrityDataGroup formationIntegrityData = ___Agent.Formation.CachedFormationIntegrityData;
                ___Agent.SetFormationIntegrityData(currentGlobalPositionOfUnit, ___Agent.Formation.CurrentDirection, formationIntegrityData.AverageVelocityExcludeFarAgents, formationIntegrityData.AverageMaxUnlimitedSpeedExcludeFarAgents, formationIntegrityData.DeviationOfPositionsExcludeFarAgents, true);
            }
            if (orderType == MovementOrderEnum.Charge || orderType == MovementOrderEnum.ChargeToTarget)
            {
                ___Agent.SetFormationFrameDisabled();
            }
        }
    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    internal class OverrideFormationMovementComponent
    {
        internal enum MovementOrderEnum
        {
            Invalid,
            Attach,
            AttackEntity,
            Charge,
            ChargeToTarget,
            Follow,
            FollowEntity,
            Guard,
            Move,
            Retreat,
            Stop,
            Advance,
            FallBack
        }

        internal enum MovementStateEnum
        {
            Charge,
            Hold,
            Retreat,
            StandGround
        }

        private static readonly MethodInfo IsUnitDetachedForDebug =
            typeof(Formation).GetMethod("IsUnitDetachedForDebug", BindingFlags.Instance | BindingFlags.NonPublic);

        [HarmonyPrefix]
        [HarmonyPatch("GetFormationFrame")]
        private static bool PrefixGetFormationFrame(ref bool __result, ref Agent ___Agent, ref HumanAIComponent __instance, ref WorldPosition formationPosition, ref Vec2 formationDirection, ref float speedLimit, ref bool limitIsMultiplier)
        {
            if (___Agent != null)
            {
                var formation = ___Agent.Formation;
                if (!___Agent.IsMount && formation != null && (formation.QuerySystem.IsCavalryFormation || formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && !(bool)IsUnitDetachedForDebug.Invoke(formation, new object[] { ___Agent }))
                {
                    if (formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
                    {
                        if (___Agent != null && formation != null)
                        {
                            formationPosition = formation.GetOrderPositionOfUnit(___Agent);
                            if (___Agent.GetTargetAgent() != null)
                            {
                                formationDirection = ___Agent.GetTargetAgent().Position.AsVec2 - ___Agent.Position.AsVec2;
                            }
                            else
                            {
                                formationDirection = formation.GetDirectionOfUnit(___Agent);
                            }
                            limitIsMultiplier = true;
                            speedLimit = __instance != null && HumanAIComponent.FormationSpeedAdjustmentEnabled ? __instance.GetDesiredSpeedInFormation(false) : -1f;
                            __result = true;
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                }
            }

            return true;
        }
    }
}