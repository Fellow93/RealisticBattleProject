
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

namespace RBMAI
{
    class Frontline
    {
        [HarmonyPatch(typeof(Formation))]
        class OverrideFormation
        {

            private static int aiDecisionCooldownTime = 2;
            private static int aiDecisionCooldownTimeSiege = 0;

            [HarmonyPrefix]
            [HarmonyPatch("GetOrderPositionOfUnit")]
            static bool PrefixGetOrderPositionOfUnit(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
            {
                //if (__instance.MovementOrder.OrderType == OrderType.ChargeWithTarget && __instance.QuerySystem.IsInfantryFormation && !___detachedUnits.Contains(unit))
                if (unit != null && (__instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge) && (__instance.QuerySystem.IsInfantryFormation || __instance.QuerySystem.IsRangedFormation) && !____detachedUnits.Contains(unit))
                {
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
                                    __result = aiDecision.position;
                                    aiDecisionCooldownDict[unit].cooldown += 1;
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
                                __result = aiDecision.position;
                                aiDecisionCooldownDict[unit].cooldown += 1;
                                return false;
                            }
                            else
                            {
                                aiDecisionCooldownDict[unit].cooldown = 0;
                            }
                        }
                        else
                        {
                            if (aiDecision.cooldown < aiDecisionCooldownTimeSiege)
                            {
                                __result = aiDecision.position;
                                aiDecisionCooldownDict[unit].cooldown += 1;
                                return false;
                            }
                            else
                            {
                                aiDecisionCooldownDict[unit].cooldown = 0;
                            }
                        }
                    }
                    else
                    {
                        aiDecisionCooldownDict[unit] = new AIDecision();
                    }
                    bool isTargetArcher = false;
                    var targetAgent = Utilities.GetCorrectTarget(unit);
                    var vanillaTargetAgent = unit.GetTargetAgent();
                    Mission mission = Mission.Current;

                    int allyAgentsCountTreshold = 3;
                    int enemyAgentsCountTreshold = 3;
                    int enemyAgentsCountDangerousTreshold = 6;
                    int enemyAgentsCountCriticalTreshold = 9;
                    int hasShieldBonusNumber = 30;
                    int isAttackingArcherNumber = -60;
                    int aggresivnesModifier = 20;

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
                        //var dot = __instance.Direction.x * direction.x + __instance.Direction.y * direction.y;

                        if (targetAgent != vanillaTargetAgent && vanillaTargetAgent.HasMount)
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
                                unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
                                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0.03f);
                            }
                            else
                            {
                                unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5f, 2f, 1f, 10f, 0.01f);
                                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 8f, 0.8f, 20f, 20f);
                            }
                        }
                        //}

                        IEnumerable<Agent> agents;
                        if (isFieldBattle)
                        {
                            agents = mission.GetNearbyAllyAgents(unitPosition + direction * 1.1f, 1f, unit.Team);

                        }
                        else
                        {
                            agents = mission.GetNearbyAllyAgents(unitPosition + lookDirection * 1.1f, 1f, unit.Team);
                        }

                        int agentsCount = agents.Count();

                        if (!isFieldBattle)
                        {
                            if (agentsCount > allyAgentsCountTreshold)
                            {
                                int relevantAgentCount = 0;
                                foreach (Agent agent in agents)
                                {
                                    if (Math.Abs(unit.VisualPosition.Z - agent.VisualPosition.Z) < 0.1f)
                                    {
                                        relevantAgentCount++;
                                    }
                                }
                                if (relevantAgentCount > allyAgentsCountTreshold)
                                {
                                    __result = unit.GetWorldPosition();
                                    aiDecisionCooldownDict[unit].position = __result; return false;
                                }
                                else
                                {
                                    return true;
                                }
                            }
                            return true;
                        }
                        if (agentsCount > allyAgentsCountTreshold && !unit.IsDoingPassiveAttack)
                        {
                            //if (MBRandom.RandomInt(100) == 0)
                            //{
                            //    return true;
                            //}
                            if (true)
                            {
                                if (unit != null)
                                {
                                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 10f, 2f, 10f, 20f, 10f);
                                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0f, 2f, 0f, 20f, 0f);
                                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);

                                    Vec2 leftVec = direction.LeftVec() + direction * 1f;
                                    Vec2 rightVec = direction.RightVec() + direction * 1f;
                                    IEnumerable<Agent> agentsLeft = mission.GetNearbyAllyAgents(unitPosition + leftVec * 1.1f, 1f, unit.Team);
                                    IEnumerable<Agent> agentsRight = mission.GetNearbyAllyAgents(unitPosition + rightVec * 1.1f, 1f, unit.Team);
                                    IEnumerable<Agent> furtherAllyAgents = mission.GetNearbyAllyAgents(unitPosition + direction * 3f, 2f, unit.Team);

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
                                                leftPosition.SetVec2(unitPosition + leftVec * 5f);
                                                __result = leftPosition;
                                                aiDecisionCooldownDict[unit].customMaxCoolDown = 10;
                                                aiDecisionCooldownDict[unit].position = __result; return false;
                                            }
                                        }
                                        else if (agentsLeftCount > agentsRightCount)
                                        {
                                            if (agentsRightCount <= allyAgentsCountTreshold)
                                            {
                                                WorldPosition rightPosition = unit.GetWorldPosition();
                                                rightPosition.SetVec2(unitPosition + rightVec * 5f);
                                                __result = rightPosition;
                                                aiDecisionCooldownDict[unit].customMaxCoolDown = 10;
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
                                            leftPosition.SetVec2(unitPosition + leftVec * 3f);
                                            __result = leftPosition;
                                            aiDecisionCooldownDict[unit].customMaxCoolDown = 8;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                        else if (agentsLeftCount > agentsRightCount)
                                        {
                                            WorldPosition rightPosition = unit.GetWorldPosition();
                                            rightPosition.SetVec2(unitPosition + rightVec * 3f);
                                            __result = rightPosition;
                                            aiDecisionCooldownDict[unit].customMaxCoolDown = 8;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                    }
                                    if (isFieldBattle)
                                    {
                                        int unitPower = (int)Math.Floor(unit.Character.GetPower() * 100);
                                        int randInt = MBRandom.RandomInt(unitPower + aggresivnesModifier);
                                        int defensivnesModifier = 0;
                                        if (unit.HasShieldCached)
                                        {
                                            defensivnesModifier += hasShieldBonusNumber;
                                        }
                                        if (randInt < (unitPower / 2 + defensivnesModifier))
                                        {
                                            __result = getNearbyAllyWorldPosition(mission, unitPosition, unit);
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                        else
                                        {
                                            if (MBRandom.RandomInt(unitPower / 4) == 0)
                                            {
                                                return true;
                                            }
                                            else
                                            {
                                                WorldPosition backPosition = unit.GetWorldPosition();
                                                backPosition.SetVec2(unitPosition - unit.Formation.Direction * 1.15f);
                                                __result = backPosition;
                                                aiDecisionCooldownDict[unit].customMaxCoolDown = 3;
                                                aiDecisionCooldownDict[unit].position = __result; return false;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        __result = unit.GetWorldPosition();
                                        aiDecisionCooldownDict[unit].position = __result; return false;
                                    }
                                }
                                else
                                {
                                    return true;
                                }
                                //}
                            }
                        }
                        if (!isFieldBattle)
                        {
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0.03f);
                        }
                        else
                        {
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5f, 2f, 1f, 10f, 0.01f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 8f, 0.8f, 20f, 20f);
                        }
                        if (isFieldBattle)
                        {
                            IEnumerable<Agent> enemyAgents10f;
                            IEnumerable<Agent> enemyAgents0f = mission.GetNearbyEnemyAgents(unitPosition, 5f, unit.Team);
                            //IEnumerable<Agent> enemyAgentsImmidiate = null;

                            int enemyAgentsImmidiateCount = 0;
                            int enemyAgents10fCount = 0;
                            int powerSumImmidiate = (int)Math.Floor(RBMAI.Utilities.GetPowerOfAgentsSum(enemyAgents0f) * 100);

                            if (!isTargetArcher)
                            {
                                enemyAgents10f = mission.GetNearbyEnemyAgents(unitPosition + direction * 5f, 5f, unit.Team);
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
                                unit.LookDirection = direction.ToVec3();
                                int unitPower = (int)Math.Floor(unit.Character.GetPower() * 100);
                                int randInt = MBRandom.RandomInt(unitPower + aggresivnesModifier);
                                int defensivnesModifier = 0;

                                if (unit.HasShieldCached)
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
                                        backPosition.SetVec2(unitPosition - unit.Formation.Direction * 1.15f);
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
                                    backPosition.SetVec2(unitPosition - unit.Formation.Direction * 1.15f);
                                    __result = backPosition;
                                    //aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                    aiDecisionCooldownDict[unit].position = __result; return false;
                                    //}
                                }
                                else if (randInt < (unitPower / 2 + defensivnesModifier))
                                {
                                    if (randInt < (unitPower / 2 + defensivnesModifier))
                                    {
                                        if (enemyAgentsImmidiateCount > enemyAgentsCountDangerousTreshold)
                                        {
                                            //int randImmidiate = MBRandom.RandomInt(powerSumImmidiate);
                                            //if (unitPower / 2 < randImmidiate)
                                            //{
                                            WorldPosition backPosition = unit.GetWorldPosition();
                                            backPosition.SetVec2(unitPosition - unit.Formation.Direction * 1.15f);
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
                                        if (MBRandom.RandomInt(unitPower / 4) == 0)
                                        {
                                            __result = unit.GetWorldPosition();
                                            //__result = WorldPosition.Invalid;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                        else
                                        {
                                            WorldPosition backPosition = unit.GetWorldPosition();
                                            backPosition.SetVec2(unitPosition - unit.Formation.Direction * 1.15f);
                                            __result = backPosition;
                                            //aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                    }

                                }
                                else if (randInt < unitPower)
                                {
                                    return true;
                                }
                            }
                        }
                        else
                        {
                            return true;
                        }
                        //}
                    }
                    aiDecisionCooldownDict[unit].position = __result; return false;
                }
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

                if (!Mission.Current.IsFieldBattle && unit != null && (__instance.QuerySystem.IsInfantryFormation) && (__instance.AI != null || __instance.IsAIControlled == false) && __instance.AI.ActiveBehavior != null)
                {
                    if (__instance.QuerySystem.ClosestEnemyFormation != null)
                    {
                        if (__instance.OrderPositionIsValid && __instance.OrderPosition.Distance(__instance.QuerySystem.AveragePosition) < 9f)
                        //if(__instance.QuerySystem.ClosestEnemyFormation.AveragePosition.Distance(__instance.QuerySystem.AveragePosition) < 25f)
                        {
                            //InformationManager.DisplayMessage(new InformationMessage(__instance.AI.ActiveBehavior.GetType().Name + " " + __instance.MovementOrder.OrderType.ToString()));
                            //bool exludedWhenAiControl = !(__instance.IsAIControlled && (__instance.AI.ActiveBehavior.GetType().Name.Contains("Regroup") || __instance.AI.ActiveBehavior.GetType().Name.Contains("Advance")));
                            //bool exludedWhenPlayerControl = !(!__instance.IsAIControlled && (__instance.GetReadonlyMovementOrderReference().OrderType.ToString().Contains("Advance")));

                            if (!____detachedUnits.Contains(unit))
                            {
                                Mission mission = Mission.Current;
                                if (mission.Mode != MissionMode.Deployment)
                                {
                                    var targetAgent = unit.GetTargetAgent();
                                    if (targetAgent != null)
                                    {
                                        Vec2 unitPosition = unit.GetWorldPosition().AsVec2;
                                        //Vec2 direction = (targetAgent.GetWorldPosition().AsVec2 - unitPosition).Normalized();
                                        Vec2 direction = unit.LookDirection.AsVec2;

                                        IEnumerable<Agent> agents = mission.GetNearbyAllyAgents(unitPosition + direction * 0.8f, 1f, unit.Team);
                                        if (agents.Count() > 2)
                                        {
                                            int relevantAgentCount = 0;
                                            foreach (Agent agent in agents)
                                            {
                                                if (Math.Abs(unit.VisualPosition.Z - agent.VisualPosition.Z) < 0.1f && unit.Formation == agent.Formation)
                                                {
                                                    relevantAgentCount++;
                                                }
                                            }

                                            if (relevantAgentCount > 2)
                                            {
                                                //if (MBRandom.RandomInt(100) == 0)
                                                //{
                                                //    return true;
                                                //}
                                                //else
                                                //{
                                                __result = unit.GetWorldPosition();
                                                return false;
                                                //}
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                return true;
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
}
