using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.ArrangementOrder;
using static TaleWorlds.MountAndBlade.HumanAIComponent;
namespace RBMAI
{
    [MBCallback]
    [HarmonyPatch(typeof(HumanAIComponent))]
    internal class AdjustSpeedLimitPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("AdjustSpeedLimit")]
        private static bool AdjustSpeedLimitPrefix(ref HumanAIComponent __instance, ref Agent agent, ref float desiredSpeed, ref bool limitIsMultiplier, ref Agent ___Agent)
        {
            if (___Agent == null ||
                !___Agent.IsActive() ||
                agent.Formation == null ||
                agent.Formation?.QuerySystem == null ||
                agent.Formation?.AI == null)
            {
                return true;
            }

            if (agent.Formation.QuerySystem.IsRangedCavalryFormation || agent.Formation.QuerySystem.IsCavalryFormation)
            {
                if (agent.MountAgent != null)
                {
                    float speed = agent.MountAgent.AgentDrivenProperties.MountSpeed;
                    ___Agent.SetMaximumSpeedLimit(speed, false);
                    agent.MountAgent.SetMaximumSpeedLimit(speed, false);
                    return false;
                    //if (limitIsMultiplier && desiredSpeed < 0.95f)
                    //{
                    //    desiredSpeed = 0.95f;
                    //}
                }
            }
            if (agent.Formation?.AI?.ActiveBehavior == null)
            {
                return true;
            }
            bool isFormationUnderRangedAttack = agent.Formation.QuerySystem?.UnderRangedAttackRatio >= 0.33f;

            if (agent.Formation.AI.ActiveBehavior.GetType() == typeof(RBMBehaviorForwardSkirmish) ||
                agent.Formation.AI.ActiveBehavior.GetType() == typeof(RBMBehaviorInfantryAttackFlank))
            {
                if (limitIsMultiplier && desiredSpeed < 0.9f)
                {
                    desiredSpeed = 0.9f;
                }
            }
            if (agent.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorProtectFlank))
            {
                if (desiredSpeed < 0.9f)
                {
                    desiredSpeed = 0.9f;
                }
            }
            if (agent.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorAdvance))
            {
                if (limitIsMultiplier)
                {
                    if (desiredSpeed < 0.6f && isFormationUnderRangedAttack)
                    {
                        desiredSpeed = 0.6f;
                    }
                }
            }
            if (agent.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorRegroup))
            {
                if (limitIsMultiplier)
                {
                    if (desiredSpeed < 0.6f && isFormationUnderRangedAttack)
                    {
                        desiredSpeed = 0.6f;
                    }
                }
            }
            if (agent.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorCharge))
            {
                float currentTime = MBCommon.GetTotalMissionTime();
                if (agent.Formation.ArrangementOrder.OrderType == OrderType.ArrangementCloseOrder && !isFormationUnderRangedAttack)
                {
                    if (limitIsMultiplier && desiredSpeed > 0.5f)
                    {
                        desiredSpeed = 0.5f;
                    }
                }
                else
                {
                    if (limitIsMultiplier && desiredSpeed < 0.9f)
                    {
                        desiredSpeed = 0.9f;
                    }
                }
            }
            if (agent.Formation.AI.ActiveBehavior.GetType() == typeof(RBMBehaviorArcherFlank))
            {
                if (limitIsMultiplier && desiredSpeed < 0.9f)
                {
                    desiredSpeed = 0.9f;
                }
            }
            if (agent.Formation.AI.ActiveBehavior.GetType() == typeof(RBMBehaviorArcherSkirmish))
            {
                if (limitIsMultiplier && desiredSpeed < 0.9f)
                {
                    desiredSpeed = 0.9f;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    internal class OverrideHumanAIComponent
    {
        // AISimpleBehaviorKind., zero range / point blank (invisible), weight, range , weight , range, weight, infinity range (invisible)
        //nulte (neviditelne cislo) = vzdialenost 0, prve cislo = vaha akcie, druhe cislo = vzdialenost, tretie cislo = vaha akcie, stvrte cislo = vzdialenostny treshold, piate cislo = vaha akcie, sieste neviditlene cislo = vzdialenost nekonecno
        [HarmonyPostfix]
        [HarmonyPatch("SetBehaviorValueSet")]
        private static void SetBehaviorValueSet(HumanAIComponent __instance, BehaviorValueSet behaviorValueSet, Agent ___Agent)
        {
            if (Mission.Current.IsSiegeBattle || Mission.Current.IsSallyOutBattle)
            {
                if (___Agent != null && ___Agent.Equipment != null && ___Agent.IsRangedCached)
                {
                    __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 8f, 5f, 5f, 15f, 0.01f);
                    __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0.02f, 5f, 0.04f, 15f, 0.03f);
                    return;
                }
            }
            if (Mission.Current.SceneName.Contains("arena"))
            {
                if (___Agent != null && ___Agent.SpawnEquipment != null && ___Agent.IsRangedCached)
                {
                    __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                    __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 5.5f, 3f, 4f, 10f, 0.01f);
                    __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 3f, 2f, 10f, 20f);
                }
            }
            if (___Agent != null && ___Agent.Formation != null)
            {
                if (behaviorValueSet == BehaviorValueSet.Charge)
                {
                    if (___Agent.Formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 0.01f, 7f, 4f, 20f, 6f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 50f, 2f, 30f, 4f, 0.55f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 30f, 5f, 20f, 9f, 0.55f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 1f, 10f, 30f, 100f, 30f);

                        if (___Agent.HasMount)
                        {
                            if (RBMAI.Utilities.GetHarnessTier(___Agent) > 3)
                            {
                                __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 5f, 5f, 40f, 20f, 5f);
                            }
                        }

                        return;
                    }
                    if (___Agent.Formation.QuerySystem.IsCavalryFormation)
                    {
                        if (___Agent.HasMount)
                        {
                            if (RBMAI.Utilities.GetHarnessTier(___Agent) > 3)
                            {
                                __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 8f, 7f, 4f, 20f, 1f);
                                __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
                            }
                            else
                            {
                                __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 1f, 2f, 1f, 20f, 1f);
                                __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
                            }
                        }
                        else
                        {
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
                        }
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 1f, 7f, 4f, 20f, 6f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 2f, 7f, 4f, 20f, 5f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 0f, 10f, 3f, 20f, 6f);
                        return;
                    }
                    if (___Agent.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || ___Agent.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge)
                    {
                        if (___Agent.Formation.QuerySystem.IsInfantryFormation)
                        {
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 7f, 0.8f, 20f, 20f);
                        }
                        if (___Agent.Formation.QuerySystem.IsRangedFormation)
                        {
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 5.5f, 5f, 4f, 10f, 0.01f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 3f, 2f, 10f, 20f);
                        }
                        return;
                    }
                }
                if (behaviorValueSet == BehaviorValueSet.Follow)
                {
                    if (___Agent.Formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 35f, 4f, 20f, 6f, 0.55f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0.5f, 10f, 1f, 30f, 30f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 8f, 10f, 0.55f, 30f, 0.55f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 10f, 15f, 0.065f, 30f, 0.065f);
                        return;
                    }
                    if (___Agent.Formation.QuerySystem.IsCavalryFormation)
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 3f, 7f, 4f, 20f, 6f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 0.0f, 2f, 0f, 20f, 0f);
                        return;
                    }
                }
                if (behaviorValueSet == BehaviorValueSet.DefaultMove)
                {
                    if (___Agent.Formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        if (___Agent.Formation.IsAIControlled)
                        {
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 3f, 15f, 5f, 20f, 5f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 50f, 4f, 20f, 6f, 0.55f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 40f, 5f, 20f, 30f, 0.55f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 1f, 10f, 30f, 120f, 0.5f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0.5f, 10f, 1f, 30f, 30f);

                            if (___Agent.HasMount)
                            {
                                if (RBMAI.Utilities.GetHarnessTier(___Agent) > 3)
                                {
                                    __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 5f, 20f, 30f, 20f, 0.5f);
                                }
                            }
                        }
                        else
                        {
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 3f, 15f, 5f, 20f, 5f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 0f, 2f, 0f, 20f, 0f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 0.01f, 2f, 0.01f, 30f, 0.01f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 1f, 15f, 0.065f, 30f, 0.065f);
                        }
                        return;
                    }
                    if (___Agent.Formation.QuerySystem.IsRangedFormation)
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 5.5f, 5f, 4f, 10f, 0.01f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 3f, 5f, 200f, 1f);
                    }
                    return;
                    if (Mission.Current.IsSiegeBattle || Mission.Current.IsSallyOutBattle)
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 8f, 4f, 3f, 20f, 0.01f);
                        return;
                    }
                    if (___Agent.Formation.GetReadonlyMovementOrderReference().OrderEnum == MovementOrder.MovementOrderEnum.FallBack)
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 0f, 4f, 0f, 20f, 0f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);
                    }
                    else
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 8f, 5f, 3f, 20f, 0.01f);
                    }
                    return;
                }
            }
        }
    }
}
