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

            // a dismounting formation keeps vanilla pacing
            // *ReadOnly flags: movement worker threads; the plain getters re-evaluate the unit-ratio group from several threads
            if ((agent.Formation.QuerySystem.IsRangedCavalryFormationReadOnly || agent.Formation.QuerySystem.IsCavalryFormationReadOnly)
                && agent.Formation.RidingOrder.OrderEnum != RidingOrder.RidingOrderEnum.Dismount)
            {
                // desiredSpeed < 0 = "clear the limit" call: leave it to vanilla; so does a dying mount
                if (agent.MountAgent != null && agent.MountAgent.IsActive() && desiredSpeed >= 0f)
                {
                    float speed = agent.MountAgent.AgentDrivenProperties.MountSpeed;
                    ___Agent.SetMaximumSpeedLimit(speed, false);
                    agent.MountAgent.SetMaximumSpeedLimit(speed, false);
                    return false;
                }
            }
            if (agent.Formation?.AI?.ActiveBehavior == null)
            {
                return true;
            }
            bool isFormationUnderRangedAttack = agent.Formation.QuerySystem?.UnderRangedAttackRatio >= 0.33f;

            // the floors lift native's pace, but a man ahead of his slot keeps native's low value (else the fastest walk out in front). Charge is left alone.
            if (limitIsMultiplier && agent.Formation.AI.ActiveBehavior.GetType() != typeof(BehaviorCharge) && IsAheadOfSlot(agent))
            {
                return true;
            }

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
                // regrouping men run flat out, no formation pacing
                if (limitIsMultiplier)
                {
                    desiredSpeed = 1f;
                }
            }
            if (agent.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorCharge))
            {
                float currentTime = MBCommon.GetTotalMissionTime();
                // shield wall walk-in only near the enemy
                if (agent.Formation.ArrangementOrder.OrderType == OrderType.ArrangementCloseOrder && !isFormationUnderRangedAttack
                    && IsNearEnemy(agent))
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

        private const float ShieldWallWalkDistance = 30f; // m

        private const float AheadOfSlotSlack = 1.5f; // m past his slot along the facing before the floors stop lifting his pace

        // movement worker thread: same read native's GetDesiredSpeedInFormation does on this job
        private static bool IsAheadOfSlot(Agent agent)
        {
            Formation formation = agent.Formation;
            if (formation.Arrangement is ColumnFormation || agent.IsDetachedFromFormation)
            {
                return false;
            }
            Vec2 direction = formation.CurrentDirection;
            if (!direction.IsValid || direction.LengthSquared < 0.01f)
            {
                return false;
            }
            Vec2 slot = formation.GetCurrentGlobalPositionOfUnit(agent, true);
            return (agent.Position.AsVec2 - slot).DotProduct(direction) > AheadOfSlotSlack;
        }

        // movement worker thread: cached closest enemy and the agent's own target only
        private static bool IsNearEnemy(Agent agent)
        {
            Vec2 position = agent.Position.AsVec2;
            float limitSquared = ShieldWallWalkDistance * ShieldWallWalkDistance;
            Agent target = agent.GetTargetAgent();
            if (target != null && target.IsActive() && position.DistanceSquared(target.Position.AsVec2) < limitSquared)
            {
                return true;
            }
            Agent closest = agent.Formation.QuerySystem.ClosestEnemyAgentReadOnly;
            return closest != null && closest.IsActive() && position.DistanceSquared(closest.Position.AsVec2) < limitSquared;
        }
    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    internal class OverrideHumanAIComponent
    {
        // AISimpleBehaviorKind., zero range / point blank (invisible), weight, range , weight , range, weight, infinity range (invisible)
        [HarmonyPostfix]
        [HarmonyPatch("SetBehaviorValueSet")]
        private static void SetBehaviorValueSet(HumanAIComponent __instance, BehaviorValueSet behaviorValueSet, Agent ___Agent)
        {
            if (Mission.Current.IsSiegeBattle || Mission.Current.IsSallyOutBattle)
            {
                if (___Agent != null && ___Agent.Equipment != null && ___Agent.IsRangedCached)
                {
                    // siege shooters draw a sidearm only for a man actually on them (melee within 3 m, gone by 6 m)
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
                    // AI only: player-held formations keep vanilla Follow (ChargeHorseback 0)
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
                    // player-held Move/Stop: mounted archers hold their slot; decided per agent too (IsRangedCavalryFormation is a 5 s cached ratio)
                    // HasMount on both sides: an unhorsed rider is foot
                    if (!___Agent.Formation.IsAIControlled && ___Agent.HasMount && (___Agent.Formation.QuerySystem.IsRangedCavalryFormation || ___Agent.IsRangedCached))
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 3f, 15f, 5f, 20f, 5f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 0f, 2f, 0f, 20f, 0f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 0.01f, 2f, 0.01f, 30f, 0.01f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 1f, 15f, 0.065f, 30f, 0.065f);
                        return;
                    }
                    // AI mounted skirmishers with ammo keep the bow; the sword only when an enemy is on top of them (~3.5 m)
                    if (___Agent.Formation.IsAIControlled && ___Agent.HasMount
                        && ((___Agent.IsRangedCached && ___Agent.Formation.QuerySystem.IsRangedCavalryFormation)
                            || (___Agent.HasAnyRangedWeaponCached && ___Agent.Formation.AI.ActiveBehavior is BehaviorMountedSkirmish)))
                    {
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 3f, 15f, 5f, 20f, 5f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 40f, 3f, 15f, 4f, 0.01f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 12f, 3f, 7.5f, 4f, 0.01f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 8f, 10f, 6f, 25f, 0.05f);
                        __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Ranged, 0.5f, 10f, 1f, 30f, 30f);
                        return;
                    }
                    if (___Agent.Formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        if (___Agent.Formation.IsAIControlled)
                        {
                            // riders out of ammo or unhorsed fight; AI horse archers shoot from the formation's orbit, solo archery only to evade within ~15 m
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.GoToPos, 3f, 15f, 5f, 20f, 5f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.Melee, 50f, 4f, 20f, 6f, 0.55f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 6f, 4f, 3f, 10f, 0.01f);
                            __instance.OverrideBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 8f, 10f, 6f, 25f, 0.05f);
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
                    // everything else keeps vanilla DefaultMove
                    return;
                }
            }
        }
    }

    // Vanilla refreshes behavior values only on an order or a unit join, not when control changes hands (and Stop->Stop skips OnApply):
    // refresh on every control change, mapping the order as MovementOrder.OnApply does.
    [HarmonyPatch(typeof(Formation))]
    [HarmonyPatch("SetControlledByAI")]
    internal class RefreshBehaviorValuesOnControlChange
    {
        private static readonly AccessTools.FieldRef<Formation, bool> EnforceNotSplittableByAI =
            AccessTools.FieldRefAccess<Formation, bool>("_enforceNotSplittableByAI");

        private static void Prefix(Formation __instance, out bool __state)
        {
            __state = __instance.IsAIControlled;
        }

        private static void Postfix(Formation __instance, bool __state)
        {
            if (__state == __instance.IsAIControlled)
            {
                return;
            }
            // USER RULE: a sergeant's formation handed to the AI (F6) is still commanded, never split/merged/drained:
            // native IsAIOwned then stays false, so native and RBM reshuffles count it but leave its men alone.
            if (__instance.IsAIControlled && Agent.Main != null && __instance.PlayerOwner == Agent.Main
                && __instance.Team != null && !__instance.Team.IsPlayerGeneral)
            {
                EnforceNotSplittableByAI(__instance) = true;
            }
            if (__instance.CountOfUnits == 0)
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