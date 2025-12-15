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

namespace RBMAI
{
    public static class Frontline
    {
        public static ConcurrentDictionary<Agent, AIDecision> aiDecisionCooldownDict = new ConcurrentDictionary<Agent, AIDecision>();
        public static Random rnd = new Random();
        public static int aiDecisionCooldownTime = 3;

        public enum AIState
        {
            NORMAL,
            ENTHUSIASTIC,
            FRENZY
        }

        //
        public class AIMindset
        {
            public enum AIDecision
            {
                Attack,
                BackStep,
                FindAlly,
                FlankAllyLeft,
                FlankAllyRight
            }

            public int fallback = 0;
            public int attack = 50;
            public int findAlly = 0;
            public int flankAllyLeft = 0;
            public int flankAllyRight = 0;

            public int fallBackBase = 0;
            public int attackBase = 20;
            public int findAllyBase = 0;
            public int flankAllyLeftBase = 0;
            public int flankAllyRightBase = 0;

            public void SetValue(AIDecision decision, int value)
            {
                int changedValue = 0;
                int changedValueBase = 0;
                int changedValueFromBase = 0;
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
                    int valueToReduce = (int)Math.Floor((Math.Sqrt(Math.Abs(changedValueFromBase))));
                    changedValue -= valueToReduce;
                }
                else
                {
                    int valueToAdd = (int)Math.Floor((Math.Sqrt(Math.Abs(changedValueFromBase))));
                    changedValue += valueToAdd;
                }
                changedValue = Math.Min(1000, changedValue);
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
                Dictionary<AIDecision, int> decisionValues = new Dictionary<AIDecision, int>
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
            public int cooldown = 0;
            public WorldPosition position = WorldPosition.Invalid;
            public int customMaxCoolDown = -1;
            public AIDecisionType decisionType = AIDecisionType.None;

            public enum AIDecisionType
            {
                None,
                FrontlineBackStep,
                FindAlly,
                FlankAllyLeft,
                FlankAllyRight
            }
        }

        public static bool defensiveCommand = false;
        public static bool normalCommand = true;
        public static bool aggressiveCommand = false;

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

        public static void ResetAiDecision(ref AIDecision aiDecision)
        {
            aiDecision.decisionType = AIDecision.AIDecisionType.None;
            aiDecision.cooldown = 0;
            aiDecision.customMaxCoolDown = -1;
        }

        [HarmonyPatch(typeof(Formation))]
        private class OverrideFormation
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetOrderPositionOfUnit")]
            private static bool PrefixGetOrderPositionOfUnit(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
            {
                if (!unit.IsActive())
                {
                    return true;
                }
                Mission mission = Mission.Current;
                //if (mission != null && !mission.IsFieldBattle)
                //{
                //    //everyone charge if close to enemy in non-field battle
                //    MBList<Agent> enemiesCloseBy = new MBList<Agent>();
                //    enemiesCloseBy = mission?.GetNearbyEnemyAgents(unit.Position.AsVec2, 2.5f, unit.Team, enemiesCloseBy);
                //    if (enemiesCloseBy?.Count() > 0)
                //    {
                //        __result = WorldPosition.Invalid;
                //        return false;
                //    }
                //}
                //for cavalry
                if (mission != null && mission.IsFieldBattle && unit != null && (__instance.QuerySystem.IsCavalryFormation || __instance.QuerySystem.IsRangedCavalryFormation))
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
                    if (enemyCavalryCloseBy.Count() > 2)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                    //cav charge if close to enemy infantry
                    if (enemyInfantryCloseBy.Count() > 2)
                    {
                        __result = WorldPosition.Invalid;
                        return false;
                    }
                }
                if (mission != null && mission.IsFieldBattle && unit != null && __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget && __instance.QuerySystem.IsCavalryFormation)
                {
                    var targetAgent = Utilities.GetCorrectTarget(unit);
                    if (targetAgent != null)
                    {
                        float distance = targetAgent != null ? (targetAgent.Position - unit.Position).Length : 0f;
                        if (distance > 60f)
                        {
                            //InformationManager.DisplayMessage(new InformationMessage(unit.Name.ToString() + " " + distance+" "+targetAgent.Name.ToString()));
                            __result = targetAgent != null ? targetAgent.GetWorldPosition() : WorldPosition.Invalid;
                            return false;
                        }
                    }
                }
                //for range
                if (mission != null && unit != null && mission.IsFieldBattle && __instance.QuerySystem.IsRangedFormation)
                {
                    //ranged charge if close to enemy
                    MBList<Agent> enemiesCloseBy = new MBList<Agent>();
                    enemiesCloseBy = mission.GetNearbyEnemyAgents(unit.Position.AsVec2, 2.5f, unit.Team, enemiesCloseBy);
                    if (enemiesCloseBy.Count() > 0)
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
                                    if (currentTime - unit.LastRangedAttackTime > 20f && enemyCloseBy.Count() < 3)
                                    {
                                        __result = WorldPosition.Invalid;
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }

                //if (__instance.Team.ActiveAgents.Count() * __instance.Team.QuerySystem.InfantryRatio <= 30) { return true; } // frontline system disabled for small infantry battles
                if (mission != null && mission.IsFieldBattle && unit != null && (__instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge) && (__instance.QuerySystem.IsInfantryFormation || __instance.QuerySystem.IsRangedFormation) && !____detachedUnits.Contains(unit))
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

                        Vec2 unitPosition = unit.GetWorldPosition().AsVec2;
                        Vec2 direction = (targetAgent.GetWorldPosition().AsVec2 - unitPosition).Normalized();
                        Vec2 leftVec = direction.LeftVec();
                        Vec2 rightVec = direction.RightVec();

                        MBList<Agent> alliesFront = new MBList<Agent>();
                        MBList<Agent> alliesLeft = new MBList<Agent>();
                        MBList<Agent> alliesRight = new MBList<Agent>();

                        MBList<Agent> enemiesFront = new MBList<Agent>();

                        alliesFront = mission.GetNearbyAllyAgents(unitPosition + direction * 1.4f, 1.4f, unit.Team, alliesFront);
                        alliesLeft = mission.GetNearbyAllyAgents(unitPosition + leftVec * 1.35f, 1.35f, unit.Team, alliesLeft);
                        alliesRight = mission.GetNearbyAllyAgents(unitPosition + rightVec * 1.35f, 1.35f, unit.Team, alliesRight);

                        enemiesFront = mission.GetNearbyEnemyAgents(unitPosition + direction, 2f, unit.Team, enemiesFront);

                        AIDecision aiDecision;
                        if (!aiDecisionCooldownDict.TryGetValue(unit, out aiDecision))
                        {
                            aiDecisionCooldownDict[unit] = new AIDecision();
                            aiDecisionCooldownDict.TryGetValue(unit, out aiDecision);
                        }

                        float postureModifier = 1f;
                        if (RBMConfig.RBMConfig.postureEnabled)
                        {
                            __result = targetAgent != null ? targetAgent.GetWorldPosition() : WorldPosition.Invalid;
                            aiDecision.position = __result;
                            Posture posture = null;
                            AgentPostures.values.TryGetValue(unit, out posture);
                            if (unit != null && posture != null)
                            {
                                postureModifier = ((posture.posture / posture.maxPosture));
                            }
                        }

                        int unitTier = unit.Character.GetBattleTier();
                        float healthModifier = unit.Health / unit.HealthLimit;
                        bool isSoldier = unit.Character.IsSoldier;

                        int findAlly = enemiesFront.Count() - alliesLeft.Count() - alliesRight.Count() - alliesFront.Count();
                        int fallback = (int)(alliesFront.Count() * 1.5f) + enemiesFront.Count();
                        int attack = -(int)(alliesFront.Count() * 2.5f) + alliesLeft.Count() + alliesRight.Count() - enemiesFront.Count() + Math.Max(0, 3 - (unitTier - 1)) + (isSoldier ? 0 : 2);
                        int flankAllyLeft = (int)(alliesFront.Count() * 1.5f) + alliesRight.Count() - enemiesFront.Count();
                        int flankAllyRight = (int)(alliesFront.Count() * 1.5f) + alliesLeft.Count() - enemiesFront.Count();

                        aiDecision.AIMindset.SetValue(AIMindset.AIDecision.Attack, attack > 0 ? (int)(attack * (postureModifier * healthModifier)) : attack);
                        aiDecision.AIMindset.SetValue(AIMindset.AIDecision.BackStep, (int)(fallback * (2 - postureModifier) * (2 - healthModifier)));
                        aiDecision.AIMindset.SetValue(AIMindset.AIDecision.FindAlly, findAlly);
                        aiDecision.AIMindset.SetValue(AIMindset.AIDecision.FlankAllyLeft, (int)(flankAllyLeft * (2 - postureModifier)));
                        aiDecision.AIMindset.SetValue(AIMindset.AIDecision.FlankAllyRight, (int)(flankAllyRight * (2 - postureModifier)));

                        aiDecision.AIMindset.getDecision(out AIMindset.AIDecision decision);

                        //float weaponLengthModifier = unit.WieldedWeapon.CurrentUsageItem != null ? (unit.WieldedWeapon.CurrentUsageItem.GetRealWeaponLength()) : 1f;

                        switch (decision)
                        {
                            case AIMindset.AIDecision.Attack:
                                {
                                    if (__instance.IsAIControlled && targetAgent != null && targetAgent.IsActive() && vanillaTargetAgent.IsActive() && unit != null && targetAgent != vanillaTargetAgent && vanillaTargetAgent.HasMount || vanillaTargetAgent.IsRunningAway)
                                    {
                                        __result = targetAgent != null ? targetAgent.GetWorldPosition() : WorldPosition.Invalid;
                                        aiDecision.position = __result;
                                        return false;
                                    }
                                    return true;
                                }
                            case AIMindset.AIDecision.BackStep:
                                {
                                    WorldPosition backPosition = unit.GetWorldPosition();
                                    backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * 0.23f);
                                    unit.SetLookAgent(targetAgent);
                                    __result = backPosition;
                                    return false;
                                }
                            case AIMindset.AIDecision.FindAlly:
                                {
                                    WorldPosition allyPosition = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                                    __result = allyPosition;
                                    unit.SetLookAgent(targetAgent);
                                    return false;
                                }
                            case AIMindset.AIDecision.FlankAllyLeft:
                                {
                                    WorldPosition leftPosition = unit.GetWorldPosition();
                                    leftPosition.SetVec2(unitPosition + leftVec * 0.5f);
                                    unit.SetLookAgent(targetAgent);
                                    return false;
                                }
                            case AIMindset.AIDecision.FlankAllyRight:
                                {
                                    WorldPosition rightPosition = unit.GetWorldPosition();
                                    rightPosition.SetVec2(unitPosition + rightVec * 0.5f);
                                    __result = rightPosition;
                                    unit.SetLookAgent(targetAgent);
                                    return false;
                                }
                        }

                    }
                }
                //{
                //    AIDecision aiDecision;
                //    bool isTargetArcher = false;
                //    bool isAgentInDefensiveOrder = __instance.ArrangementOrder == ArrangementOrder.ArrangementOrderShieldWall || __instance.ArrangementOrder == ArrangementOrder.ArrangementOrderCircle || __instance.ArrangementOrder == ArrangementOrder.ArrangementOrderSquare;
                //    var targetAgent = Utilities.GetCorrectTarget(unit);
                //    //unit.SetAutomaticTargetSelection(false);
                //    //if (targetAgent !=null)
                //    //{
                //    //    unit.SetTargetAgent(targetAgent);
                //    //}
                //    var vanillaTargetAgent = unit.GetTargetAgent();
                //    int allyAgentsCountTreshold = 3;
                //    int enemyAgentsCountTreshold = 3;
                //    int enemyAgentsCountDangerousTreshold = 4;
                //    int enemyAgentsCountCriticalTreshold = 5;
                //    int hasShieldBonusNumber = 40;
                //    int isAttackingArcherNumber = -60;
                //    int aggresivnesModifier = -15;
                //    float backStepDistance = 0.2f;

                //    if (isAgentInDefensiveOrder)
                //    {
                //        allyAgentsCountTreshold = 3;
                //        enemyAgentsCountTreshold = 3;
                //        enemyAgentsCountDangerousTreshold = 4;
                //        enemyAgentsCountCriticalTreshold = 5;
                //        backStepDistance = 0.2f;
                //        hasShieldBonusNumber = 40;
                //        aggresivnesModifier = -15;
                //    }

                //    float weaponLengthModifier = unit.WieldedWeapon.CurrentUsageItem != null ? (unit.WieldedWeapon.CurrentUsageItem.GetRealWeaponLength() + 0.5f) : 1f;
                //    float enemyWeaponLengthModifier = -1f;
                //    if (targetAgent != null)
                //    {
                //        enemyWeaponLengthModifier = targetAgent.WieldedWeapon.CurrentUsageItem != null ? (targetAgent.WieldedWeapon.CurrentUsageItem.GetRealWeaponLength()) : 1f;
                //    }

                //    int weaponLengthAgressivnessModifier = 10;
                //    if (!unit.WieldedWeapon.IsEmpty && unit.WieldedWeapon.CurrentUsageItem != null)
                //    {
                //        float weaponLength = unit.WieldedWeapon.CurrentUsageItem.GetRealWeaponLength();
                //        float weaponLengthRelative = (2f - weaponLength) * 2f;
                //        weaponLengthRelative = MBMath.ClampFloat(weaponLengthRelative, 1f, 3f);
                //        weaponLengthAgressivnessModifier = (int)Math.Round(weaponLengthAgressivnessModifier * weaponLengthRelative);
                //    }

                //    aggresivnesModifier += weaponLengthAgressivnessModifier;
                //    aggresivnesModifier *= (int)MBMath.ClampFloat(enemyWeaponLengthModifier / weaponLengthModifier, 1f, 2f);

                //    if (__instance.Captain != null && __instance.Captain.IsPlayerTroop)
                //    {
                //        if (aggressiveCommand)
                //        {
                //            aggresivnesModifier += 50;
                //        }
                //        else if (defensiveCommand)
                //        {
                //            aggresivnesModifier += -50;
                //        }
                //        else if (normalCommand)
                //        {
                //            aggresivnesModifier += 0;
                //        }
                //    }

                //    if (targetAgent != null && vanillaTargetAgent != null)
                //    {
                //        if (vanillaTargetAgent.Formation != null && vanillaTargetAgent.Formation == targetAgent.Formation)
                //        {
                //            targetAgent = vanillaTargetAgent;
                //        }

                //        Vec2 lookDirection = unit.LookDirection.AsVec2;
                //        Vec2 unitPosition = unit.GetWorldPosition().AsVec2;
                //        Vec2 direction = (targetAgent.GetWorldPosition().AsVec2 - unitPosition).Normalized();
                //        Vec2 leftVec = direction.LeftVec();
                //        Vec2 rightVec = direction.RightVec();

                //        if (aiDecisionCooldownDict.TryGetValue(unit, out aiDecision))
                //        {
                //            if (aiDecision.customMaxCoolDown != -1)
                //            {
                //                if (aiDecision.cooldown < aiDecision.customMaxCoolDown)
                //                {
                //                    aiDecision.cooldown += 1;
                //                    if (!aiDecision.position.IsValid)
                //                    {
                //                        return true;
                //                    }
                //                    if (aiDecision.decisionType == AIDecision.AIDecisionType.FrontlineBackStep)
                //                    {
                //                        WorldPosition backPosition = unit.GetWorldPosition();
                //                        backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * 0.2f);
                //                        __result = backPosition;
                //                        return false;
                //                    }
                //                    __result = aiDecision.position;
                //                    return false;
                //                }
                //                else
                //                {
                //                    ResetAiDecision(ref aiDecision);
                //                }
                //            }
                //            else
                //            {
                //                if (aiDecision.cooldown < aiDecisionCooldownTime)
                //                {
                //                    aiDecision.cooldown += 1;
                //                    if (!aiDecision.position.IsValid)
                //                    {
                //                        return true;
                //                    }
                //                    if (aiDecision.decisionType == AIDecision.AIDecisionType.FrontlineBackStep)
                //                    {
                //                        WorldPosition backPosition = unit.GetWorldPosition();
                //                        backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * 0.2f);
                //                        __result = backPosition;
                //                        return false;
                //                    }
                //                    if (aiDecision.decisionType == AIDecision.AIDecisionType.FindAlly)
                //                    {
                //                        __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                //                        return false;
                //                    }
                //                    __result = aiDecision.position;
                //                    return false;
                //                }
                //                else
                //                {
                //                    ResetAiDecision(ref aiDecision);
                //                }
                //            }
                //        }
                //        else
                //        {
                //            aiDecisionCooldownDict[unit] = new AIDecision();
                //            aiDecisionCooldownDict.TryGetValue(unit, out aiDecision);
                //        }

                //        if (targetAgent != null && unit != null && targetAgent != vanillaTargetAgent && vanillaTargetAgent.HasMount || vanillaTargetAgent.IsRunningAway)
                //        {
                //            __result = targetAgent != null ? targetAgent.GetWorldPosition() : WorldPosition.Invalid;
                //            aiDecision.position = __result;
                //            if (!unit.IsRangedCached)
                //            {
                //                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 5f, weaponLengthModifier, 10f, 20f, 10f);
                //                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 5f, weaponLengthModifier, 0f, 20f, 0f);
                //                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);
                //            }
                //            return false;
                //        }
                //        else
                //        {
                //            if ((vanillaTargetAgent.Formation != null && vanillaTargetAgent.Formation.QuerySystem.IsRangedFormation))
                //            // || (targetAgent.Formation != null && targetAgent.Formation.QuerySystem.IsRangedFormation))
                //            {
                //                isTargetArcher = true;
                //            }
                //            if (!unit.IsRangedCached)
                //            {
                //                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, weaponLengthModifier, 4f, 10f, 6f);
                //                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 5f, weaponLengthModifier, 1f, 10f, 0.01f);
                //                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0.01f);
                //            }
                //            //if (IsInImportantFrontlineAction(unit))
                //            //{
                //            //    aiDecision.position = WorldPosition.Invalid;
                //            //    return true;
                //            //}
                //        }
                //        //}

                //        MBList<Agent> agents = new MBList<Agent>();
                //        agents = mission.GetNearbyAllyAgents(unitPosition + direction * 1.35f, 1.35f, unit.Team, agents);
                //        Agent tempAgent = unit;
                //        agents = new MBList<Agent>(agents.Where(a => a != tempAgent).ToList());
                //        int agentsCount = agents.Count();

                //        MBList<Agent> agentsLeft = new MBList<Agent>();
                //        MBList<Agent> agentsRight = new MBList<Agent>();
                //        agentsLeft = mission.GetNearbyAllyAgents(unitPosition + leftVec * 1.35f, 1.35f, unit.Team, agentsLeft);
                //        agentsRight = mission.GetNearbyAllyAgents(unitPosition + rightVec * 1.35f, 1.35f, unit.Team, agentsRight);

                //        int attackingTogether = 0;
                //        foreach (Agent agent in agentsLeft)
                //        {
                //            Agent.ActionCodeType currentActionType = agent.GetCurrentActionType(1);

                //            if (
                //                currentActionType == Agent.ActionCodeType.ReleaseMelee ||
                //                currentActionType == Agent.ActionCodeType.ReleaseRanged ||
                //                currentActionType == Agent.ActionCodeType.ReleaseThrowing)
                //            {
                //                attackingTogether += 10;
                //            }
                //        }
                //        foreach (Agent agent in agentsRight)
                //        {
                //            Agent.ActionCodeType currentActionType = agent.GetCurrentActionType(1);
                //            if (
                //                currentActionType == Agent.ActionCodeType.ReleaseMelee ||
                //                currentActionType == Agent.ActionCodeType.ReleaseRanged ||
                //                currentActionType == Agent.ActionCodeType.ReleaseThrowing)
                //            {
                //                attackingTogether += 10;
                //            }
                //        }

                //        //if(MBRandom.RandomFloat >= (attackingTogether / attackingTogetherLimit){
                //        //    attackingTogether += 50;
                //        //}

                //        if (attackingTogether > 50)
                //        {
                //            //unit.SetWantsToYell();
                //            attackingTogether = 50;

                //        }
                //        attackingTogether = 0;
                //        aggresivnesModifier = 0;
                //        if (agentsCount > allyAgentsCountTreshold && !unit.IsDoingPassiveAttack)
                //        {
                //            //if (MBRandom.RandomInt(100) == 0)
                //            //{
                //            //    return true;
                //            //}

                //            //unit.LookDirection = direction.ToVec3();
                //            //unit.SetDirectionChangeTendency(10f);
                //            if (true)
                //            {
                //                if (unit != null)
                //                {
                //                    //if (!unit.IsRangedCached)
                //                    //{
                //                    //    unit.HumanAIComponent?.SetBehaviorParams(AISimpleBehaviorKind.GoToPos, 5f, 2f, 4f, 10f, 6f);
                //                    //    unit.HumanAIComponent?.SetBehaviorParams(AISimpleBehaviorKind.Melee, 5f, 2f, 1.1f, 10f, 0.01f);
                //                    //    unit.HumanAIComponent?.SetBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0.8f);
                //                    //}

                //                    //Vec2 leftVec = unit.Formation.Direction.LeftVec().Normalized();
                //                    //Vec2 rightVec = unit.Formation.Direction.RightVec().Normalized();

                //                    MBList<Agent> furtherAllyAgents = new MBList<Agent>();

                //                    furtherAllyAgents = mission.GetNearbyAllyAgents(unitPosition + direction * 3f, 1.5f, unit.Team, furtherAllyAgents);

                //                    int agentsLeftCount = agentsLeft.Count();
                //                    int agentsRightCount = agentsRight.Count();
                //                    int furtherAllyAgentsCount = furtherAllyAgents.Count();

                //                    if (furtherAllyAgentsCount > allyAgentsCountTreshold)
                //                    {
                //                        if (agentsLeftCount < agentsRightCount)
                //                        {
                //                            if (agentsLeftCount <= allyAgentsCountTreshold)
                //                            {
                //                                WorldPosition leftPosition = unit.GetWorldPosition();
                //                                leftPosition.SetVec2(unitPosition + leftVec * 3f);
                //                                __result = leftPosition;
                //                                aiDecision.decisionType = AIDecision.AIDecisionType.FlankAllyLeft;
                //                                aiDecision.position = __result; return false;
                //                            }
                //                        }
                //                        else if (agentsLeftCount > agentsRightCount)
                //                        {
                //                            if (agentsRightCount <= allyAgentsCountTreshold)
                //                            {
                //                                WorldPosition rightPosition = unit.GetWorldPosition();
                //                                rightPosition.SetVec2(unitPosition + rightVec * 3f);
                //                                __result = rightPosition;
                //                                aiDecision.decisionType = AIDecision.AIDecisionType.FlankAllyRight;
                //                                aiDecision.position = __result; return false;
                //                            }
                //                        }
                //                    }
                //                    //if (agentsLeftCount > 4 && agentsRightCount > 4)
                //                    //{
                //                    //    __result = unit.GetWorldPosition();
                //                    //    aiDecision.position = __result; return false;
                //                    //}
                //                    else if (agentsLeftCount <= allyAgentsCountTreshold && agentsRightCount <= allyAgentsCountTreshold)
                //                    {
                //                        if (agentsLeftCount < agentsRightCount)
                //                        {
                //                            WorldPosition leftPosition = unit.GetWorldPosition();
                //                            leftPosition.SetVec2(unitPosition + leftVec * 2f);
                //                            __result = leftPosition;
                //                            aiDecision.decisionType = AIDecision.AIDecisionType.FlankAllyLeft;
                //                            aiDecision.position = __result; return false;
                //                        }
                //                        else if (agentsLeftCount > agentsRightCount)
                //                        {
                //                            WorldPosition rightPosition = unit.GetWorldPosition();
                //                            rightPosition.SetVec2(unitPosition + rightVec * 2f);
                //                            __result = rightPosition;
                //                            aiDecision.decisionType = AIDecision.AIDecisionType.FlankAllyRight;
                //                            aiDecision.position = __result; return false;
                //                        }
                //                    }
                //                    float hpModifier = (unit.Health) / unit.HealthLimit;
                //                    float postureModifier = 1f;
                //                    if (RBMConfig.RBMConfig.postureEnabled)
                //                    {
                //                        Posture posture = null;
                //                        AgentPostures.values.TryGetValue(unit, out posture);
                //                        if (unit != null && posture != null)
                //                        {
                //                            postureModifier = ((posture.posture / posture.maxPosture) * 0.3f);
                //                        }
                //                    }
                //                    int unitPower = MBMath.ClampInt((int)Math.Floor(unit.CharacterPowerCached * hpModifier * postureModifier * 8 * 65), 80, 180);
                //                    int randInt = MBRandom.RandomInt(unitPower + aggresivnesModifier);
                //                    int defensivnesModifier = 0;
                //                    if (unit.WieldedOffhandWeapon.IsShield())
                //                    {
                //                        defensivnesModifier += hasShieldBonusNumber;
                //                    }
                //                    if (randInt < (unitPower / 3f + defensivnesModifier))
                //                    {
                //                        if (!unit.IsRangedCached)
                //                        {
                //                            unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, weaponLengthModifier, 4f, 10f, 6f);
                //                            unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 4f, weaponLengthModifier, 1f, 10f, 0.01f);
                //                            unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 8f, 0f, 20f, 0f);
                //                        }
                //                        aiDecision.decisionType = AIDecision.AIDecisionType.FindAlly;
                //                        __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                //                        aiDecision.position = __result; return false;
                //                    }
                //                    else
                //                    {
                //                        if (MBRandom.RandomInt(unitPower) == 0)
                //                        {
                //                            if (!unit.IsRangedCached)
                //                            {
                //                                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, weaponLengthModifier, 4f, 10f, 6f);
                //                                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 4f, weaponLengthModifier, 1f, 10f, 0.01f);
                //                                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 8f, 0f, 20f, 0f);
                //                            }
                //                            aiDecision.decisionType = AIDecision.AIDecisionType.FindAlly;
                //                            __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                //                            aiDecision.position = __result; return false;
                //                        }
                //                        else
                //                        {
                //                            if (!unit.IsRangedCached)
                //                            {
                //                                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, weaponLengthModifier, 4f, 10f, 6f);
                //                                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 4f, weaponLengthModifier, 1f, 10f, 0.01f);
                //                                unit.HumanAIComponent?.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 8f, 0f, 20f, 0f);
                //                            }
                //                            WorldPosition backPosition = unit.GetWorldPosition();
                //                            backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * (backStepDistance + 2f));
                //                            __result = backPosition;
                //                            aiDecision.decisionType = AIDecision.AIDecisionType.FrontlineBackStep;
                //                            aiDecision.position = __result; return false;
                //                        }
                //                    }
                //                }
                //                else
                //                {
                //                    aiDecision.position = WorldPosition.Invalid;
                //                    return true;
                //                }
                //            }
                //        }

                //        MBList<Agent> enemyAgentsImmidiate = new MBList<Agent>();
                //        MBList<Agent> enemyAgentsClose = new MBList<Agent>();
                //        //float searchArea = 4.5f;
                //        float searchArea = weaponLengthModifier + 2.5f;
                //        enemyAgentsImmidiate = mission.GetNearbyEnemyAgents(unitPosition, searchArea, unit.Team, enemyAgentsImmidiate);
                //        //IEnumerable<Agent> enemyAgentsImmidiate = null;

                //        int enemyAgentsImmidiateCount = 0;
                //        int enemyAgentsCloseCount = 0;
                //        int powerSumImmidiate = (int)Math.Floor(RBMAI.Utilities.GetPowerOfAgentsSum(enemyAgentsImmidiate));
                //        int powerSumClose = (int)Math.Floor(RBMAI.Utilities.GetPowerOfAgentsSum(enemyAgentsClose));

                //        if (!isTargetArcher)
                //        {
                //            enemyAgentsClose = mission.GetNearbyEnemyAgents(unitPosition + direction * searchArea, searchArea / 2f, unit.Team, enemyAgentsClose);

                //            enemyAgentsImmidiateCount = enemyAgentsImmidiate.Count();
                //            enemyAgentsCloseCount = enemyAgentsClose.Count();
                //        }
                //        else
                //        {
                //            enemyAgentsImmidiateCount = 0;
                //            enemyAgentsCloseCount = 0;
                //        }
                //        if (attackingTogether == 50)
                //        {
                //            enemyAgentsCountCriticalTreshold *= 1;
                //            enemyAgentsCountDangerousTreshold *= 1;
                //        }
                //        attackingTogether = 0;
                //        if (enemyAgentsImmidiateCount > enemyAgentsCountTreshold || enemyAgentsCloseCount > enemyAgentsCountTreshold)
                //        {
                //            //unit.LookDirection = direction.ToVec3();
                //            //unit.SetDirectionChangeTendency(10f);
                //            float hpModifier = (unit.Health) / unit.HealthLimit;
                //            float postureModifier = 1f;
                //            if (RBMConfig.RBMConfig.postureEnabled)
                //            {
                //                Posture posture = null;
                //                AgentPostures.values.TryGetValue(unit, out posture);
                //                if (unit != null && posture != null)
                //                {
                //                    postureModifier = ((posture.posture / posture.maxPosture) * 0.3f);
                //                }
                //            }
                //            int unitPower = MBMath.ClampInt((int)Math.Floor(unit.CharacterPowerCached * hpModifier * postureModifier * 40) + attackingTogether, 50, 200);
                //            int randInt = MBRandom.RandomInt((int)unitPower + aggresivnesModifier);
                //            int defensivnesModifier = 0;

                //            if (unit.WieldedOffhandWeapon.IsShield())
                //            {
                //                defensivnesModifier += hasShieldBonusNumber;
                //            }
                //            if (isTargetArcher)
                //            {
                //                defensivnesModifier += isAttackingArcherNumber;
                //            }
                //            if (randInt < 0)
                //            {
                //                __result = unit.GetWorldPosition();
                //                aiDecision.position = __result; return false;
                //            }

                //            if (!isTargetArcher)
                //            {
                //                int randImmidiate = MBRandom.RandomInt(powerSumImmidiate);
                //                if (unitPower * 2 < randImmidiate)
                //                {
                //                    if (IsActivelyAttacking(unit))
                //                    {
                //                        //aiDecision.position = WorldPosition.Invalid;
                //                        ResetAiDecision(ref aiDecision);
                //                        return false;
                //                    }
                //                    WorldPosition backPosition = unit.GetWorldPosition();
                //                    backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * backStepDistance);
                //                    __result = backPosition;
                //                    aiDecision.customMaxCoolDown = 1;
                //                    aiDecision.decisionType = AIDecision.AIDecisionType.FrontlineBackStep;
                //                    aiDecision.position = __result; return false;
                //                }
                //            }
                //            if (enemyAgentsImmidiateCount > enemyAgentsCountCriticalTreshold)
                //            {
                //                //int randImmidiate = MBRandom.RandomInt(powerSumImmidiate);
                //                //if(unitPower / 2 < randImmidiate)
                //                //{
                //                if (IsActivelyAttacking(unit))
                //                {
                //                    //aiDecision.position = WorldPosition.Invalid;
                //                    ResetAiDecision(ref aiDecision);
                //                    return false;
                //                }
                //                if (rnd.Next(3) == 0)
                //                {
                //                    WorldPosition backPosition = unit.GetWorldPosition();
                //                    backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * backStepDistance);
                //                    __result = backPosition;
                //                    aiDecision.decisionType = AIDecision.AIDecisionType.FrontlineBackStep;
                //                    aiDecision.position = __result; return false;
                //                }
                //                else
                //                {
                //                    aiDecision.decisionType = AIDecision.AIDecisionType.FindAlly;
                //                    __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                //                    aiDecision.position = __result; return false;
                //                }
                //            }
                //            else if (randInt < (unitPower / 2f + defensivnesModifier))
                //            {
                //                if (randInt < (unitPower / 2f + defensivnesModifier))
                //                {
                //                    if (enemyAgentsImmidiateCount > enemyAgentsCountDangerousTreshold)
                //                    {
                //                        //int randImmidiate = MBRandom.RandomInt(powerSumImmidiate);
                //                        //if (unitPower / 2 < randImmidiate)
                //                        //{
                //                        if (IsActivelyAttacking(unit))
                //                        {
                //                            //aiDecision.position = WorldPosition.Invalid;
                //                            ResetAiDecision(ref aiDecision);
                //                            return false;
                //                        }
                //                        if (rnd.Next(2) == 0)
                //                        {
                //                            WorldPosition backPosition = unit.GetWorldPosition();
                //                            backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * backStepDistance);
                //                            __result = backPosition;
                //                            aiDecision.decisionType = AIDecision.AIDecisionType.FrontlineBackStep;
                //                            aiDecision.position = __result; return false;
                //                        }
                //                        else
                //                        {
                //                            aiDecision.decisionType = AIDecision.AIDecisionType.FindAlly;
                //                            __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                //                            aiDecision.position = __result; return false;
                //                        }
                //                        //}
                //                    }
                //                    if (IsActivelyAttacking(unit))
                //                    {
                //                        //aiDecision.position = WorldPosition.Invalid;
                //                        ResetAiDecision(ref aiDecision);
                //                        return false;
                //                    }
                //                    aiDecision.decisionType = AIDecision.AIDecisionType.FindAlly;
                //                    __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                //                    aiDecision.position = __result; return false;
                //                }
                //                else
                //                {
                //                    if (MBRandom.RandomInt((int)(unitPower / 4)) == 0)
                //                    {
                //                        if (IsActivelyAttacking(unit))
                //                        {
                //                            //aiDecision.position = WorldPosition.Invalid;
                //                            ResetAiDecision(ref aiDecision);
                //                            return false;
                //                        }
                //                        if (rnd.Next(2) == 0)
                //                        {
                //                            __result = unit.GetWorldPosition();
                //                            aiDecision.position = __result; return false;
                //                        }
                //                        else
                //                        {
                //                            aiDecision.decisionType = AIDecision.AIDecisionType.FindAlly;
                //                            __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                //                            aiDecision.position = __result; return false;
                //                        }
                //                    }
                //                    else
                //                    {
                //                        if (IsActivelyAttacking(unit))
                //                        {
                //                            //aiDecision.position = WorldPosition.Invalid;
                //                            ResetAiDecision(ref aiDecision);
                //                            return false;
                //                        }
                //                        if (rnd.Next(2) == 0)
                //                        {
                //                            WorldPosition backPosition = unit.GetWorldPosition();
                //                            backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * backStepDistance);
                //                            __result = backPosition;
                //                            aiDecision.decisionType = AIDecision.AIDecisionType.FrontlineBackStep;
                //                            aiDecision.position = __result; return false;
                //                        }
                //                        else
                //                        {
                //                            aiDecision.decisionType = AIDecision.AIDecisionType.FindAlly;
                //                            __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                //                            aiDecision.position = __result; return false;
                //                        }
                //                    }
                //                }
                //            }
                //            else if (randInt < unitPower)
                //            {
                //                //__result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                //                //aiDecision.position = __result; return false;
                //                aiDecision.position = WorldPosition.Invalid;
                //                return true;
                //            }
                //        }
                //    }
                //    if (!aiDecisionCooldownDict.TryGetValue(unit, out aiDecision))
                //    {
                //        aiDecisionCooldownDict[unit] = new AIDecision();
                //        aiDecisionCooldownDict.TryGetValue(unit, out aiDecision);
                //    }
                //    aiDecision.position = __result; return false;
                //}
                //return true;
                return true;
            }

            public static WorldPosition getNearbyAllyWorldPosition(Mission mission, Vec2 unitPosition, Agent unit)
            {
                MBList<Agent> nearbyAllyAgents = new MBList<Agent>();
                nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 1.5f, unit.Team, nearbyAllyAgents);
                if (nearbyAllyAgents.Count() == 0)
                {
                    nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 3f, unit.Team, nearbyAllyAgents);
                }
                if (nearbyAllyAgents.Count() == 0)
                {
                    nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 20f, unit.Team, nearbyAllyAgents);
                }
                if (nearbyAllyAgents.Count() > 0)
                {
                    List<Agent> allyAgentList = nearbyAllyAgents.ToList();
                    if (allyAgentList.Count() == 1)
                    {
                        return allyAgentList.ElementAt(0).GetWorldPosition();
                    }
                    allyAgentList.Remove(unit);
                    float dist = 10000f;
                    WorldPosition result = unit.GetWorldPosition();
                    //foreach (Agent agent in allyAgentList)
                    //{
                    //    if (agent != unit)
                    //    {
                    //        float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                    //        if (dist > newDist)
                    //        {
                    //            result = agent.GetWorldPosition();
                    //            dist = newDist;
                    //        }
                    //    }
                    //}

                    result = allyAgentList.GetRandomElement().GetWorldPosition();

                    Vec2 direction = (result.AsVec2 - unitPosition).Normalized();
                    float distance = unitPosition.Distance(result.AsVec2);
                    if (distance > 1.25f)
                    {
                        result.SetVec2(unitPosition + direction * 0.5f);
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
        }
    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    internal class OverrideFormation
    {
        [HarmonyPrefix]
        [HarmonyPatch("ParallelUpdateFormationMovement")]
        private static void PostfixParallelUpdateFormationMovement(ref HumanAIComponent __instance, ref Agent ___Agent)
        {
            if (___Agent.Controller == AgentControllerType.AI && ___Agent.Formation != null && ___Agent.Formation.GetReadonlyMovementOrderReference().OrderEnum == MovementOrder.MovementOrderEnum.Move)
            {
                PropertyInfo propertyShouldCatchUpWithFormation = typeof(HumanAIComponent).GetProperty("ShouldCatchUpWithFormation");
                propertyShouldCatchUpWithFormation.DeclaringType.GetProperty("ShouldCatchUpWithFormation");
                propertyShouldCatchUpWithFormation.SetValue(__instance, true, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                Vec2 currentGlobalPositionOfUnit = ___Agent.Formation.GetCurrentGlobalPositionOfUnit(___Agent, false);
                FormationIntegrityDataGroup formationIntegrityData = ___Agent.Formation.CachedFormationIntegrityData;
                ___Agent.SetFormationIntegrityData(currentGlobalPositionOfUnit, ___Agent.Formation.CurrentDirection, formationIntegrityData.AverageVelocityExcludeFarAgents, formationIntegrityData.AverageMaxUnlimitedSpeedExcludeFarAgents, formationIntegrityData.DeviationOfPositionsExcludeFarAgents, true);
            }
            //___Agent.SetDirectionChangeTendency(0.9f);
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