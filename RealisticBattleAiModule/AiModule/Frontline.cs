
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static RBMAI.Tactics;
using static TaleWorlds.MountAndBlade.HumanAIComponent;
using static TaleWorlds.MountAndBlade.Source.Objects.Siege.AgentPathNavMeshChecker;

namespace RBMAI
{
    class Frontline
    {
        [HarmonyPatch(typeof(Formation))]
        class OverrideFormation
        {

            //private static int aiDecisionCooldownTime = 2;
            private static int aiDecisionCooldownTimeSiege = 0;

            [HarmonyPrefix]
            [HarmonyPatch("GetOrderPositionOfUnit")]
            static bool PrefixGetOrderPositionOfUnit(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
            {
                //if (__instance.MovementOrder.OrderType == OrderType.ChargeWithTarget && __instance.QuerySystem.IsInfantryFormation && !___detachedUnits.Contains(unit))
                if (unit != null && (__instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge) && (__instance.QuerySystem.IsInfantryFormation || __instance.QuerySystem.IsRangedFormation) && !____detachedUnits.Contains(unit))
                {
                    int aiDecisionCooldownTime = 2;
                    bool isFieldBattle = Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle;
                    AIDecision aiDecision;

                    if (aiDecisionCooldownDict.TryGetValue(unit, out aiDecision))
                    {
                        if (isFieldBattle)
                        {
                            if (aiDecision.customMaxCoolDown != -1)
                            {
                                if (aiDecision.cooldown < aiDecision.customMaxCoolDown)
                                {
                                    aiDecisionCooldownDict[unit].cooldown += 1;
                                    if (!aiDecision.position.IsValid)
                                    {
                                        return true;
                                    }
                                    __result = aiDecision.position;
                                    return false;
                                }
                                else
                                {
                                    aiDecision.customMaxCoolDown = -1;
                                    aiDecisionCooldownDict[unit].cooldown = 0;
                                }
                            }
                            if (aiDecision.cooldown < aiDecisionCooldownTime)
                            {
                                aiDecisionCooldownDict[unit].cooldown += 1;
                                if (!aiDecision.position.IsValid)
                                {
                                    return true;
                                }
                                __result = aiDecision.position;
                                return false;
                            }
                            else
                            {
                                aiDecisionCooldownDict[unit].cooldown = 0;
                            }
                        }
                        //else
                        //{
                        //    if (aiDecision.cooldown < aiDecisionCooldownTimeSiege)
                        //    {
                        //        __result = aiDecision.position;
                        //        aiDecisionCooldownDict[unit].cooldown += 1;
                        //        return false;
                        //    }
                        //    else
                        //    {
                        //        aiDecisionCooldownDict[unit].cooldown = 0;
                        //    }
                        //}
                    }
                    else
                    {
                        aiDecisionCooldownDict[unit] = new AIDecision();
                    }
                    bool isTargetArcher = false;
                    bool isAgentInDefensiveOrder = __instance.ArrangementOrder == ArrangementOrder.ArrangementOrderShieldWall ||
                        __instance.ArrangementOrder == ArrangementOrder.ArrangementOrderCircle ||
                        __instance.ArrangementOrder == ArrangementOrder.ArrangementOrderSquare;
                    var targetAgent = Utilities.GetCorrectTarget(unit);
                    var vanillaTargetAgent = unit.GetTargetAgent();
                    Mission mission = Mission.Current;
                    
                    int allyAgentsCountTreshold = 3;
                    int enemyAgentsCountTreshold = 3;
                    int enemyAgentsCountDangerousTreshold = 4;
                    int enemyAgentsCountCriticalTreshold = 6;
                    int hasShieldBonusNumber = 40;
                    int isAttackingArcherNumber = -60;
                    int aggresivnesModifier = 30;
                    float backStepDistance = 0.35f;
                    if (isAgentInDefensiveOrder)
                    {
                        allyAgentsCountTreshold = 3;
                        enemyAgentsCountTreshold = 3;
                        enemyAgentsCountDangerousTreshold = 4;
                        enemyAgentsCountCriticalTreshold = 6;
                        backStepDistance = 0.35f;
                        hasShieldBonusNumber = 40;
                        aggresivnesModifier = 30;
                    }

                    if (targetAgent != null && vanillaTargetAgent != null)
                    {
                        if (vanillaTargetAgent.Formation != null && vanillaTargetAgent.Formation == targetAgent.Formation)
                        {
                            targetAgent = vanillaTargetAgent;
                        }

                        Vec2 lookDirection = unit.LookDirection.AsVec2;
                        Vec2 unitPosition = unit.GetWorldPosition().AsVec2;
                        //float distance = unitPosition.Distance(targetAgent.GetWorldPosition().AsVec2);
                        Vec2 direction = (targetAgent.GetWorldPosition().AsVec2 - unitPosition).Normalized();
                        //float distance = targetAgent.GetWorldPosition().GetGroundVec3().Distance(unit.GetWorldPosition().GetGroundVec3());
                        //if (unit.WieldedWeapon.CurrentUsageItem != null)
                        //{
                        //    float rwl = unit.WieldedWeapon.CurrentUsageItem.GetRealWeaponLength();
                        //    if (distance < 1.15f)
                        //    {
                        //        WorldPosition pos = unit.GetWorldPosition();
                        //        __result = pos;
                        //        aiDecisionCooldownDict[unit].customMaxCoolDown = 0;
                        //        aiDecisionCooldownDict[unit].position = __result; return false;
                        //    }
                        //    if (rwl * 0.9f > distance)
                        //    {
                        //        WorldPosition pos = unit.GetWorldPosition();
                        //        pos.SetVec2(pos.AsVec2 - direction * (rwl * 0.9f - distance));
                        //        __result = pos;
                        //        aiDecisionCooldownDict[unit].customMaxCoolDown = 0;
                        //        aiDecisionCooldownDict[unit].position = __result; return false;
                        //    }
                        //}
                        //var dot = __instance.Direction.x * direction.x + __instance.Direction.y * direction.y;

                        if (targetAgent != vanillaTargetAgent && vanillaTargetAgent.HasMount || vanillaTargetAgent.IsRunningAway)
                        {
                            __result = targetAgent.GetWorldPosition();
                            aiDecisionCooldownDict[unit].position = __result;
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 10f, 2f, 10f, 20f, 10f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0f, 2f, 0f, 20f, 0f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);
                            return false;
                        }
                        else
                        {
                            if ((vanillaTargetAgent.Formation != null && vanillaTargetAgent.Formation.QuerySystem.IsRangedFormation))
                            // || (targetAgent.Formation != null && targetAgent.Formation.QuerySystem.IsRangedFormation))
                            {
                                isTargetArcher = true;
                            }
                            if (!isFieldBattle)
                            {
                                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1.1f, 10f, 0.01f);
                                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0.03f);
                            }
                            else
                            {
                                unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5f, 1.5f, 1.1f, 10f, 0.01f);
                                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 8f, 0.8f, 20f, 20f);
                            }
                        }
                        //}

                        IEnumerable<Agent> agents;
                        if (isFieldBattle)
                        {
                            agents = mission.GetNearbyAllyAgents(unitPosition + direction * 1.1f, 1.1f, unit.Team);
                        }
                        else
                        {
                            agents = mission.GetNearbyAllyAgents(unitPosition + direction * 1f, 1f, unit.Team);
                        }
                        Agent tempAgent = unit;
                        agents = agents.Where(a => a != tempAgent).ToList();
                        int agentsCount = agents.Count();

                        //if (!isFieldBattle)
                        //{
                        //    allyAgentsCountTreshold = 2;
                        //    if (agentsCount > allyAgentsCountTreshold)
                        //    {
                        //        int relevantAgentCount = 0;
                        //        foreach (Agent agent in agents)
                        //        {
                        //            if (Math.Abs(unit.VisualPosition.Z - agent.VisualPosition.Z) < 0.1f)
                        //            {
                        //                relevantAgentCount++;
                        //            }
                        //        }
                        //        if (relevantAgentCount > allyAgentsCountTreshold)
                        //        {
                        //            __result = unit.GetWorldPosition();
                        //            aiDecisionCooldownDict[unit].position = __result; return false;
                        //        }
                        //        else
                        //        {
                        //            return true;
                        //        }
                        //    }
                            
                        //    return true;
                        //}
                        if (agentsCount > allyAgentsCountTreshold && !unit.IsDoingPassiveAttack)
                        {
                            //if (MBRandom.RandomInt(100) == 0)
                            //{
                            //    return true;
                            //}

                            //unit.LookDirection = direction.ToVec3();
                            //unit.SetDirectionChangeTendency(10f);
                            if (true)
                            {
                                if (unit != null)
                                {
                                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 5f, 2f, 4f, 10f, 6f);
                                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 4.9f, 1.5f, 1.1f, 10f, 0.01f);
                                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);

                                    //Vec2 leftVec = unit.Formation.Direction.LeftVec().Normalized();
                                    //Vec2 rightVec = unit.Formation.Direction.RightVec().Normalized();
                                    Vec2 leftVec = direction.LeftVec();
                                    Vec2 rightVec = direction.RightVec();
                                    IEnumerable<Agent> agentsLeft = mission.GetNearbyAllyAgents(unitPosition + leftVec * 1.35f, 1.35f, unit.Team);
                                    IEnumerable<Agent> agentsRight = mission.GetNearbyAllyAgents(unitPosition + rightVec * 1.35f, 1.35f, unit.Team);
                                    IEnumerable<Agent> furtherAllyAgents = mission.GetNearbyAllyAgents(unitPosition + direction * 2.2f, 1.1f, unit.Team);

                                    int agentsLeftCount = agentsLeft.Count();
                                    int agentsRightCount = agentsRight.Count();
                                    int furtherAllyAgentsCount = furtherAllyAgents.Count();

                                    if (isFieldBattle && furtherAllyAgentsCount > allyAgentsCountTreshold)
                                    {
                                        if (agentsLeftCount < agentsRightCount)
                                        {
                                            if (agentsLeftCount <= allyAgentsCountTreshold)
                                            {
                                                WorldPosition leftPosition = unit.GetWorldPosition();
                                                leftPosition.SetVec2(unitPosition + leftVec * 4f);
                                                __result = leftPosition;
                                                aiDecisionCooldownDict[unit].customMaxCoolDown = 3;
                                                aiDecisionCooldownDict[unit].position = __result; return false;
                                            }
                                        }
                                        else if (agentsLeftCount > agentsRightCount)
                                        {
                                            if (agentsRightCount <= allyAgentsCountTreshold)
                                            {
                                                WorldPosition rightPosition = unit.GetWorldPosition();
                                                rightPosition.SetVec2(unitPosition + rightVec * 4f);
                                                __result = rightPosition;
                                                aiDecisionCooldownDict[unit].customMaxCoolDown = 3;
                                                aiDecisionCooldownDict[unit].position = __result; return false;
                                            }
                                        }
                                    }
                                    //if (agentsLeftCount > 4 && agentsRightCount > 4)
                                    //{
                                    //    __result = unit.GetWorldPosition();
                                    //    aiDecisionCooldownDict[unit].position = __result; return false;
                                    //}
                                    else if (agentsLeftCount <= allyAgentsCountTreshold && agentsRightCount <= allyAgentsCountTreshold)
                                    {
                                        if (agentsLeftCount < agentsRightCount)
                                        {
                                            WorldPosition leftPosition = unit.GetWorldPosition();
                                            leftPosition.SetVec2(unitPosition + leftVec * 2f);
                                            __result = leftPosition;
                                            aiDecisionCooldownDict[unit].customMaxCoolDown = 2;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                        else if (agentsLeftCount > agentsRightCount)
                                        {
                                            WorldPosition rightPosition = unit.GetWorldPosition();
                                            rightPosition.SetVec2(unitPosition + rightVec * 2f);
                                            __result = rightPosition;
                                            aiDecisionCooldownDict[unit].customMaxCoolDown = 2;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                    }
                                    if (isFieldBattle)
                                    {
                                        int unitPower = MBMath.ClampInt((int)Math.Floor(unit.CharacterPowerCached * (unit.Health / unit.HealthLimit) * 65), 75, 200);
                                        int randInt = MBRandom.RandomInt(unitPower + aggresivnesModifier);
                                        int defensivnesModifier = 0;
                                        if (unit.WieldedOffhandWeapon.IsShield())
                                        {
                                            defensivnesModifier += hasShieldBonusNumber;
                                        }
                                        if (randInt < (unitPower / 3f + defensivnesModifier))
                                        {
                                            __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                        else
                                        {
                                            if (MBRandom.RandomInt(unitPower ) == 0)
                                            {
                                                aiDecisionCooldownDict[unit].position = WorldPosition.Invalid;
                                                aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                                return true;
                                            }
                                            else
                                            {
                                                WorldPosition backPosition = unit.GetWorldPosition();
                                                backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * (backStepDistance + 0.5f));
                                                __result = backPosition;
                                                aiDecisionCooldownDict[unit].customMaxCoolDown = 2;
                                                aiDecisionCooldownDict[unit].position = __result; return false;
                                            }
                                        }
                                    }
                                    //else
                                    //{
                                    //    __result = unit.GetWorldPosition();
                                    //    aiDecisionCooldownDict[unit].position = __result; return false;
                                    //}
                                }
                                else
                                {
                                    aiDecisionCooldownDict[unit].position = WorldPosition.Invalid;
                                    aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                    return true;
                                }
                                
                                //}
                            }
                        }
                        if (!isFieldBattle)
                        {
                            //unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                            //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
                            //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0.03f);
                        }
                        else
                        {
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5f, 1.5f, 1f, 10f, 0.01f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 8f, 0.8f, 20f, 20f);
                        }
                        if (isFieldBattle)
                        {
                            IEnumerable<Agent> enemyAgents10f;
                            IEnumerable<Agent> enemyAgents0f = mission.GetNearbyEnemyAgents(unitPosition, 4.5f, unit.Team);
                            //IEnumerable<Agent> enemyAgentsImmidiate = null;

                            int enemyAgentsImmidiateCount = 0;
                            int enemyAgents10fCount = 0;
                            int powerSumImmidiate = (int)Math.Floor(RBMAI.Utilities.GetPowerOfAgentsSum(enemyAgents0f));

                            if (!isTargetArcher)
                            {
                                enemyAgents10f = mission.GetNearbyEnemyAgents(unitPosition + direction * 4.5f, 4.5f, unit.Team);
                                //enemyAgentsImmidiate = mission.GetNearbyEnemyAgents(unitPosition, 3f, unit.Team);

                                enemyAgentsImmidiateCount = enemyAgents0f.Count();
                                enemyAgents10fCount = enemyAgents10f.Count();
                            }
                            else
                            {
                                enemyAgentsImmidiateCount = 0;
                                enemyAgents10fCount = 0;
                            }
                            if (enemyAgentsImmidiateCount > enemyAgentsCountTreshold || enemyAgents10fCount > enemyAgentsCountTreshold)
                            {
                                //unit.LookDirection = direction.ToVec3();
                                //unit.SetDirectionChangeTendency(10f);
                                int unitPower = MBMath.ClampInt((int)Math.Floor(unit.CharacterPowerCached * (unit.Health / unit.HealthLimit) * 65), 75, 200);
                                int randInt = MBRandom.RandomInt((int)unitPower + aggresivnesModifier);
                                int defensivnesModifier = 0;

                                if (unit.WieldedOffhandWeapon.IsShield())
                                {
                                    defensivnesModifier += hasShieldBonusNumber;
                                }
                                if (isTargetArcher)
                                {
                                    defensivnesModifier += isAttackingArcherNumber;
                                }
                                if (randInt < 0)
                                {
                                    __result = unit.GetWorldPosition();
                                    aiDecisionCooldownDict[unit].position = __result; return false;
                                }

                                if (!isTargetArcher)
                                {
                                    int randImmidiate = MBRandom.RandomInt(powerSumImmidiate);
                                    if (unitPower * 2 < randImmidiate)
                                    {
                                        WorldPosition backPosition = unit.GetWorldPosition();
                                        backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * backStepDistance);
                                        __result = backPosition;
                                        //aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                        aiDecisionCooldownDict[unit].position = __result; return false;
                                    }
                                }
                                if (enemyAgentsImmidiateCount > enemyAgentsCountCriticalTreshold)
                                {
                                    //int randImmidiate = MBRandom.RandomInt(powerSumImmidiate);
                                    //if(unitPower / 2 < randImmidiate)
                                    //{
                                    WorldPosition backPosition = unit.GetWorldPosition();
                                    backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * backStepDistance);
                                    __result = backPosition;
                                    //aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                    aiDecisionCooldownDict[unit].position = __result; return false;
                                    //}
                                }
                                else if (randInt < (unitPower / 2f + defensivnesModifier))
                                {
                                    if (randInt < (unitPower / 2f + defensivnesModifier))
                                    {
                                        if (enemyAgentsImmidiateCount > enemyAgentsCountDangerousTreshold)
                                        {
                                            //int randImmidiate = MBRandom.RandomInt(powerSumImmidiate);
                                            //if (unitPower / 2 < randImmidiate)
                                            //{
                                            WorldPosition backPosition = unit.GetWorldPosition();
                                            backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * backStepDistance);
                                            __result = backPosition;
                                            //aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                            //}
                                        }
                                        __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                                        aiDecisionCooldownDict[unit].position = __result; return false;
                                    }
                                    else
                                    {
                                        if (MBRandom.RandomInt((int)(unitPower / 4)) == 0)
                                        {
                                            __result = unit.GetWorldPosition();
                                            //__result = WorldPosition.Invalid;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                        else
                                        {
                                            WorldPosition backPosition = unit.GetWorldPosition();
                                            backPosition.SetVec2(unitPosition - (unit.Formation.Direction + direction) * backStepDistance);
                                            __result = backPosition;
                                            //aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                    }

                                }
                                else if (randInt < unitPower)
                                {
                                    aiDecisionCooldownDict[unit].position = WorldPosition.Invalid;
                                    aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                    return true;
                                }
                            }
                        }
                        //else
                        //{
                        //    aiDecisionCooldownDict[unit].position = WorldPosition.Invalid;
                        //    aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                        //    return true;
                        //}
                        //}
                    }
                    aiDecisionCooldownDict[unit].position = __result; return false;
                }
                //aiDecisionCooldownDict[unit].customMaxCoolDown = 3;
                return true;
            }

            public static WorldPosition getNearbyAllyWorldPosition(Mission mission, Vec2 unitPosition, Agent unit)
            {
                IEnumerable<Agent> nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 5f, unit.Team);
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
                    if (distance > 1.25f)
                    {
                        result.SetVec2(unitPosition + direction * 0.9f);
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

            [HarmonyPrefix]
            [HarmonyPatch("GetOrderPositionOfUnitAux")]
            static bool PrefixGetOrderPositionOfUnitAux(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
            {
                //if (Mission.Current.IsFieldBattle && unit != null && (__instance.QuerySystem.IsInfantryFormation) && (__instance.AI != null || __instance.IsAIControlled == false) && __instance.AI.ActiveBehavior != null)
                //{
                //    bool isAdvance =  (__instance.AI.ActiveBehavior.GetType().Name.Contains("Advance"));
                //    if (isAdvance)
                //    {
                //        float distance = unit.GetWorldPosition().AsVec2.Distance(__instance.QuerySystem.AveragePosition);
                //        if (__instance.OrderPositionIsValid && distance > 60f)
                //        {
                //            WorldPosition pos = unit.GetWorldPosition();
                //            pos.SetVec2(__instance.QuerySystem.AveragePosition);
                //            __result = pos;
                //            return false;
                //        }
                //    }
                //}
                //if (__instance.IsCavalry())
                //{
                //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0.01f, 7f, 0.01f, 110f, 1f);
                //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 0.01f, 7f, 0.01f, 20f, 0.01f);
                //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 7f, 10f, 20f, 1f);
                //    //__instance.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                //    //{
                //    //    if (MathF.Min(MathF.Max(MathF.Ceiling(((float)agent.Character.Level - 5f) / 5f), 0), 6) > 3)
                //    //    {
                //    //        agent.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0.01f, 7f, 0.01f, 20f, 1f);
                //    //        agent.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 7f, 10f, 20f, 0.01f);
                //    //    }
                //    //});
                //}
                //if (!Mission.Current.IsFieldBattle && unit != null && (__instance.QuerySystem.IsInfantryFormation) && (__instance.AI != null || __instance.IsAIControlled == false) && __instance.AI.ActiveBehavior != null)
                //{
                //    if (__instance.QuerySystem.ClosestEnemyFormation != null)
                //    {
                //        if (__instance.OrderPositionIsValid && __instance.OrderPosition.Distance(__instance.QuerySystem.AveragePosition) < 9f)
                //        //if(__instance.QuerySystem.ClosestEnemyFormation.AveragePosition.Distance(__instance.QuerySystem.AveragePosition) < 25f)
                //        {
                //            //InformationManager.DisplayMessage(new InformationMessage(__instance.AI.ActiveBehavior.GetType().Name + " " + __instance.MovementOrder.OrderType.ToString()));
                //            //bool exludedWhenAiControl = !(__instance.IsAIControlled && (__instance.AI.ActiveBehavior.GetType().Name.Contains("Regroup") || __instance.AI.ActiveBehavior.GetType().Name.Contains("Advance")));
                //            //bool exludedWhenPlayerControl = !(!__instance.IsAIControlled && (__instance.GetReadonlyMovementOrderReference().OrderType.ToString().Contains("Advance")));

                //            if (!____detachedUnits.Contains(unit))
                //            {
                //                Mission mission = Mission.Current;
                //                if (mission.Mode != MissionMode.Deployment)
                //                {
                //                    var targetAgent = unit.GetTargetAgent();
                //                    if (targetAgent != null)
                //                    {
                //                        Vec2 unitPosition = unit.GetWorldPosition().AsVec2;
                //                        //Vec2 direction = (targetAgent.GetWorldPosition().AsVec2 - unitPosition).Normalized();
                //                        Vec2 direction = unit.LookDirection.AsVec2;

                //                        IEnumerable<Agent> agents = mission.GetNearbyAllyAgents(unitPosition + direction * 0.8f, 1f, unit.Team);
                //                        if (agents.Count() > 2)
                //                        {
                //                            int relevantAgentCount = 0;
                //                            foreach (Agent agent in agents)
                //                            {
                //                                if (Math.Abs(unit.VisualPosition.Z - agent.VisualPosition.Z) < 0.1f && unit.Formation == agent.Formation)
                //                                {
                //                                    relevantAgentCount++;
                //                                }
                //                            }

                //                            if (relevantAgentCount > 2)
                //                            {
                //                                //if (MBRandom.RandomInt(100) == 0)
                //                                //{
                //                                //    return true;
                //                                //}
                //                                //else
                //                                //{
                //                                __result = unit.GetWorldPosition();
                //                                return false;
                //                                //}
                //                            }
                //                        }
                //                    }
                //                }
                //            }
                //        }
                //    }
                //}
                return true;
            }
        }
        
    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    class OverrideFormation
    {
        [HarmonyPrefix]
        [HarmonyPatch("UpdateFormationMovement")]
        static void PostfixUpdateFormationMovement(ref HumanAIComponent __instance, ref Agent ___Agent)
        {
            if (___Agent.Controller == Agent.ControllerType.AI && ___Agent.Formation != null && ___Agent.Formation.GetReadonlyMovementOrderReference().OrderEnum == MovementOrder.MovementOrderEnum.Move)
            {
                PropertyInfo propertyShouldCatchUpWithFormation = typeof(HumanAIComponent).GetProperty("ShouldCatchUpWithFormation");
                propertyShouldCatchUpWithFormation.DeclaringType.GetProperty("ShouldCatchUpWithFormation");
                propertyShouldCatchUpWithFormation.SetValue(__instance, true, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                Vec2 currentGlobalPositionOfUnit = ___Agent.Formation.GetCurrentGlobalPositionOfUnit(___Agent, false);
                FormationQuerySystem.FormationIntegrityDataGroup formationIntegrityData = ___Agent.Formation.QuerySystem.FormationIntegrityData;
                ___Agent.SetFormationIntegrityData(currentGlobalPositionOfUnit, ___Agent.Formation.CurrentDirection, formationIntegrityData.AverageVelocityExcludeFarAgents, formationIntegrityData.AverageMaxUnlimitedSpeedExcludeFarAgents, formationIntegrityData.DeviationOfPositionsExcludeFarAgents);
            }
            //___Agent.SetDirectionChangeTendency(0.9f);
        }
    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    class OverrideFormationMovementComponent
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
        static bool PrefixGetFormationFrame(ref bool __result, ref Agent ___Agent, ref HumanAIComponent __instance, ref WorldPosition formationPosition, ref Vec2 formationDirection, ref float speedLimit, ref bool isSettingDestinationSpeed, ref bool limitIsMultiplier)
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
                            isSettingDestinationSpeed = false;
                            formationPosition = formation.GetOrderPositionOfUnit(___Agent);
                            formationDirection = formation.GetDirectionOfUnit(___Agent);
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
