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
    [HarmonyPatch(typeof(BehaviorDefend))]
    internal class OverrideBehaviorDefend
    {
        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(ref BehaviorDefend __instance, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                WorldPosition medianPositionNew = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);

                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

                if (significantEnemy != null)
                {
                    Vec2 enemyDirection = RBMAI.Utilities.GetFormationCenter(significantEnemy) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                    float distance = enemyDirection.Normalize();
                    if (distance < (200f))
                    {
                        WorldPosition newPosition = WorldPosition.Invalid;
                        positionsStorage.TryGetValue(__instance.Formation, out newPosition);
                        ____currentOrder = MovementOrder.MovementOrderMove(newPosition);
                        ___IsCurrentOrderChanged = true;
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                    }
                    else
                    {
                        if (__instance.DefensePosition.IsValid)
                        {
                            WorldPosition newPosition = __instance.DefensePosition;
                            newPosition.SetVec2(newPosition.AsVec2 + __instance.Formation.Direction * 10f);
                            ____currentOrder = MovementOrder.MovementOrderMove(newPosition);
                            positionsStorage[__instance.Formation] = newPosition;

                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                        }
                        else
                        {
                            WorldPosition newPosition = medianPositionNew;
                            newPosition.SetVec2(newPosition.AsVec2 + __instance.Formation.Direction * 10f);
                            positionsStorage[__instance.Formation] = newPosition;
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorHoldHighGround))]
    internal class OverrideBehaviorHoldHighGround
    {
        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(ref BehaviorHoldHighGround __instance, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                WorldPosition medianPositionNew = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);

                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

                if (significantEnemy != null)
                {
                    Vec2 enemyDirection = RBMAI.Utilities.GetFormationCenter(significantEnemy) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                    float distance = enemyDirection.Normalize();

                    if (distance < (200f))
                    {
                        WorldPosition newPosition = WorldPosition.Invalid;
                        positionsStorage.TryGetValue(__instance.Formation, out newPosition);
                        Vec2 posVec2 = newPosition.AsVec2;
                        Vec2 closestBoundary = Mission.Current.GetClosestBoundaryPosition(posVec2);
                        float distFromBoundary = closestBoundary.Distance(posVec2);
                        if (distFromBoundary <= 70f)
                        {
                            Vec2 awayFromBoundary = (posVec2 - closestBoundary).Normalized();
                            newPosition.SetVec2(posVec2 + awayFromBoundary * (100f - distFromBoundary));
                            positionsStorage[__instance.Formation] = newPosition;
                        }
                        ____currentOrder = MovementOrder.MovementOrderMove(newPosition);
                        ___IsCurrentOrderChanged = true;
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                    }
                    else
                    {
                        WorldPosition newPosition = medianPositionNew;
                        newPosition.SetVec2(newPosition.AsVec2 + __instance.Formation.Direction * 10f);
                        Vec2 posVec2 = newPosition.AsVec2;
                        Vec2 closestBoundary = Mission.Current.GetClosestBoundaryPosition(posVec2);
                        float distFromBoundary = closestBoundary.Distance(posVec2);
                        if (distFromBoundary <= 70f)
                        {
                            Vec2 awayFromBoundary = (posVec2 - closestBoundary).Normalized();
                            newPosition.SetVec2(posVec2 + awayFromBoundary * (100f - distFromBoundary));
                            ____currentOrder = MovementOrder.MovementOrderMove(newPosition);
                            ___IsCurrentOrderChanged = true;
                            positionsStorage[__instance.Formation] = newPosition;
                        }
                        else if (distFromBoundary > 100f)
                        {
                            positionsStorage[__instance.Formation] = newPosition;
                        }
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorRegroup))]
    internal class OverrideBehaviorRegroup
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        private static bool PrefixGetAiWeight(ref BehaviorRegroup __instance, ref float __result)
        {
            if (__instance.Formation.AI != null &&
                __instance.Formation.AI.ActiveBehavior != null &&
                (__instance.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorHoldHighGround) || __instance.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorDefend)))
            {
                __result = 0f;
                return false;
            }
            if (__instance.Formation != null)
            {
                FormationQuerySystem querySystem = __instance.Formation.QuerySystem;
                if (__instance.Formation.AI.ActiveBehavior == null || querySystem.IsRangedFormation)
                {
                    __result = 0f;
                    return false;
                }
                __result = MBMath.Lerp(0.1f, 1.2f, MBMath.ClampFloat(__instance.BehaviorCoherence * (querySystem.Formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents + 1f) / (querySystem.IdealAverageDisplacement + 1f), 0f, 3f) / 3f);
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorRegroup __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
                if (significantEnemy != null)
                {
                    WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);

                    Vec2 direction = (RBMAI.Utilities.GetFormationCenter(significantEnemy) - RBMAI.Utilities.GetFormationCenter(__instance.Formation)).Normalized();
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);

                    return false;
                }
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("TickOccasionally")]
        private static void PrefixTickOccasionally(ref BehaviorRegroup __instance)
        {
            __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
        }
    }
}
