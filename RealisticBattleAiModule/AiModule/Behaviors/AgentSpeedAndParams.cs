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

            // A dismounting formation keeps vanilla pacing, which slows riders into their slots so they can get off.
            if ((agent.Formation.QuerySystem.IsRangedCavalryFormation || agent.Formation.QuerySystem.IsCavalryFormation)
                && agent.Formation.RidingOrder.OrderEnum != RidingOrder.RidingOrderEnum.Dismount)
            {
                // desiredSpeed < 0 is the "clear the limit" call (OnRetreating passes -1): let vanilla lift it
                // instead of pinning a fleeing rider to MountSpeed. A dying/removed mount goes to vanilla too.
                if (agent.MountAgent != null && agent.MountAgent.IsActive() && desiredSpeed >= 0f)
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
                if (limitIsMultiplier && desiredSpeed < 0.9f)
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
                // Regrouping men run flat out: the point of a regroup is to close up before the enemy
                // arrives, and a paced regroup (native applies the line's cached pace here too) is what let
                // a pursuing line catch stragglers. Full personal max, no formation pacing.
                if (limitIsMultiplier)
                {
                    desiredSpeed = 1f;
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
                    // Siege shooters draw a sidearm only for a man actually on them. Melee used to fade out at 15m,
                    // and attackers at a ladder's foot are about that far from the wall walk once height is counted,
                    // so defenders dropped their bows after the first shot. Now melee holds within 3m and is gone by
                    // 6m: a climber topping the wall, not the crowd below it.
                    __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 8f, 3f, 5f, 6f, 0.01f);
                    __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0.02f, 3f, 0.04f, 6f, 0.03f);
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
                    // AI only: Column arrangement maps to Follow, and Melee 35 / ChargeHorseback 8 peel a player's
                    // horse archers out of column at any nearby enemy. Player-held formations keep vanilla Follow
                    // (ChargeHorseback 0).
                    if (___Agent.Formation.IsAIControlled && ___Agent.Formation.QuerySystem.IsRangedCavalryFormation)
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
                    // Player-held Move/Stop: mounted archers hold their slot. Decided per agent as well as per formation,
                    // because IsRangedCavalryFormation is a 5s cached ratio that flips to IsCavalryFormation once enough
                    // quivers run dry (or javelin riders are mixed in), and that fell through to vanilla DefaultMove,
                    // whose ChargeHorseback 100 breaks the rider out of formation at any enemy within ~8m.
                    // HasMount on both sides: a rider who has lost his horse is foot, and the zero Melee weight below
                    // would leave him holding his bow while infantry cut him down.
                    if (!___Agent.Formation.IsAIControlled && ___Agent.HasMount && (___Agent.Formation.QuerySystem.IsRangedCavalryFormation || ___Agent.IsRangedCached))
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 3f, 15f, 5f, 20f, 5f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 0f, 2f, 0f, 20f, 0f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 0.01f, 2f, 0.01f, 30f, 0.01f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 1f, 15f, 0.065f, 30f, 0.065f);
                        return;
                    }
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
                        return;
                    }
                    if (___Agent.Formation.QuerySystem.IsRangedFormation)
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 5.5f, 5f, 4f, 10f, 0.01f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 3f, 5f, 200f, 1f);
                    }
                    // Everything else keeps vanilla DefaultMove (Melee 8,7,5,20,0.01). The tighter infantry melee curves
                    // that used to follow here sat behind an unconditional return and never ran; 3746497e dropped the
                    // return instead of the block and made archers step toward any enemy within ~20 m.
                    return;
                }
            }
        }
    }

    // The SetBehaviorValueSet postfix above picks AI or player weights from Formation.IsAIControlled, but vanilla only
    // refreshes behavior values when an order is applied or a unit joins, never when control changes hands. So weights
    // chosen while AI-controlled (before the player takes command, after delegating and taking back, or in sergeant
    // mode) outlived the handover. Re-issuing Stop can't clear them either, since Stop->Stop is "practically the same"
    // order and skips OnApply. Refresh on every change, mapping the order the same way MovementOrder.OnApply does.
    [HarmonyPatch(typeof(Formation))]
    [HarmonyPatch("SetControlledByAI")]
    internal class RefreshBehaviorValuesOnControlChange
    {
        private static void Prefix(Formation __instance, out bool __state)
        {
            __state = __instance.IsAIControlled;
        }

        private static void Postfix(Formation __instance, bool __state)
        {
            if (__state == __instance.IsAIControlled || __instance.CountOfUnits == 0)
            {
                return;
            }
            MovementOrder order = __instance.GetReadonlyMovementOrderReference();
            MovementOrder.MovementOrderEnum orderEnum = order.OrderEnum;
            if ((orderEnum == MovementOrder.MovementOrderEnum.Charge || orderEnum == MovementOrder.MovementOrderEnum.ChargeToTarget) && order.GetPosition(__instance).IsValid)
            {
                orderEnum = MovementOrder.MovementOrderEnum.Move;
            }
            ArrangementOrderEnum arrangementEnum = __instance.ArrangementOrder.OrderEnum;
            __instance.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
            {
                agent.RefreshBehaviorValues(orderEnum, arrangementEnum);
            });
        }
    }
}