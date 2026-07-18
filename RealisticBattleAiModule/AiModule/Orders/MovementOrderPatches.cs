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
    [HarmonyPatch(typeof(MovementOrder))]
    internal class OverrideMovementOrder
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetSubstituteOrder")]
        private static bool PrefixGetSubstituteOrder(MovementOrder __instance, ref MovementOrder __result, Formation formation)
        {
            if (formation != null && (formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && __instance.OrderType == OrderType.ChargeWithTarget)
            {
                if (formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    __result = MovementOrder.MovementOrderChargeToTarget(formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                }
                else
                {
                    __result = MovementOrder.MovementOrderCharge;
                }
                return false;
            }

            return true;
        }

        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

        [HarmonyPostfix]
        [HarmonyPatch("GetPositionAux")]
        private static void GetPositionAuxPostfix(ref MovementOrder __instance, ref WorldPosition __result, ref Formation f, ref WorldPosition.WorldPositionEnforcedCache worldPositionEnforcedCache)
        {
            if (__instance.OrderEnum == MovementOrder.MovementOrderEnum.FallBack)
            {
                FormationQuerySystem querySystem = f.QuerySystem;
                FormationQuerySystem closestSignificantlyLargeEnemyFormation = querySystem.ClosestSignificantlyLargeEnemyFormation;
                Vec2 directionAux;
                if (closestSignificantlyLargeEnemyFormation == null)
                {
                    directionAux = Vec2.One;
                }
                else
                {
                    directionAux = (RBMAI.Utilities.GetFormationCenter(closestSignificantlyLargeEnemyFormation.Formation) - RBMAI.Utilities.GetFormationCenter(querySystem.Formation)).Normalized();
                }

                WorldPosition medianPosition = f.QuerySystem.Formation.CachedMedianPosition;
                medianPosition.SetVec2(RBMAI.Utilities.GetFormationCenter(f.QuerySystem.Formation) - directionAux * 0.35f);
                __result = medianPosition;

                return;
            }
            if (__instance.OrderEnum == MovementOrder.MovementOrderEnum.Advance)
            {
                Formation enemyFormation = RBMAI.Utilities.FindSignificantEnemy(f, true, true, false, false, false, true);
                FormationQuerySystem querySystem = f.QuerySystem;
                FormationQuerySystem enemyQuerySystem;
                if (enemyFormation != null)
                {
                    enemyQuerySystem = enemyFormation.QuerySystem;
                }
                else
                {
                    enemyQuerySystem = querySystem.ClosestSignificantlyLargeEnemyFormation;
                }
                if (enemyQuerySystem == null)
                {
                    __result = f.CreateNewOrderWorldPosition(worldPositionEnforcedCache);
                    return;
                }
                // This runs on a worker thread (CreateNewOrderWorldPositionMT). Give each WorldPosition exactly
                // one SetVec2 off a freshly-copied median: a second SetVec2 can trigger native Z validation.
                Vec2 enemyCenter = RBMAI.Utilities.GetFormationCenter(enemyQuerySystem.Formation);
                WorldPosition oldPosition = enemyQuerySystem.Formation.CachedMedianPosition;
                oldPosition.SetVec2(enemyCenter);
                WorldPosition newPosition = enemyQuerySystem.Formation.CachedMedianPosition;
                Vec2 newPositionVec2 = enemyCenter;
                if (querySystem.IsRangedFormation || querySystem.IsRangedCavalryFormation)
                {
                    float effectiveMissileRange = querySystem.MissileRangeAdjusted / 2.25f;
                    if (!(enemyCenter.DistanceSquared(RBMAI.Utilities.GetFormationCenter(querySystem.Formation)) > effectiveMissileRange * effectiveMissileRange))
                    {
                        Vec2 directionAux2 = (enemyCenter - RBMAI.Utilities.GetFormationCenter(querySystem.Formation)).Normalized();

                        newPositionVec2 = enemyCenter - directionAux2 * effectiveMissileRange;
                    }
                    newPosition.SetVec2(newPositionVec2);

                    if (oldPosition.AsVec2.Distance(newPosition.AsVec2) > 7f)
                    {
                        positionsStorage[f] = newPosition;
                        __result = newPosition;
                    }
                    else
                    {
                        WorldPosition tempPos = WorldPosition.Invalid;
                        if (positionsStorage.TryGetValue(f, out tempPos))
                        {
                            __result = tempPos;
                            return;
                        }
                        __result = oldPosition;
                    }
                    return;
                }
                else
                {
                    Vec2 vec = (enemyCenter - RBMAI.Utilities.GetFormationCenter(f.QuerySystem.Formation)).Normalized();
                    float distance = enemyCenter.Distance(RBMAI.Utilities.GetFormationCenter(f.QuerySystem.Formation));
                    float num = 5f;
                    if (enemyQuerySystem.FormationPower < f.QuerySystem.FormationPower * 0.2f)
                    {
                        num = 0.1f;
                    }
                    newPosition.SetVec2(enemyCenter - vec * num);

                    if (distance > 7f)
                    {
                        positionsStorage[f] = newPosition;
                        __result = newPosition;
                    }
                    else
                    {
                        WorldPosition tempPos = WorldPosition.Invalid;
                        if (positionsStorage.TryGetValue(f, out tempPos))
                        {
                            __result = tempPos;
                            return;
                        }
                        __result = oldPosition;
                    }
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Formation))]
    internal class OverrideSetMovementOrder
    {
        [HarmonyPrefix]
        [HarmonyPatch("SetMovementOrder")]
        private static bool PrefixSetOrder(Formation __instance, ref MovementOrder input)
        {
            try
            {
                if (__instance == null ||
                    __instance.IsDeployment ||
                    __instance.QuerySystem == null ||
                    Mission.Current == null ||
                    input == null
                    )
                {
                    return true;
                }
                if (Mission.Current.IsFieldBattle && input.OrderType == OrderType.Charge)
                {
                    if (__instance.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                    {
                        input = MovementOrder.MovementOrderChargeToTarget(__instance.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Agent))]
    internal class OverrideAgent
    {
        [HarmonyPrefix]
        [HarmonyPatch("SetFiringOrder")]
        private static bool PrefixSetFiringOrder(ref Agent __instance, ref int order)
        {
            if (
                __instance == null ||
                !__instance.IsActive() ||
                __instance.Formation == null ||
                __instance.Formation.IsSpawning ||
                __instance.Formation.AI.ActiveBehavior == null ||
                __instance.Formation.QuerySystem == null ||
                __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null ||
                __instance.Formation.GetReadonlyMovementOrderReference().OrderType != OrderType.ChargeWithTarget)
            {
                return true;
            }
            Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

            if (__instance.Formation.QuerySystem.IsInfantryFormation && !RBMAI.Utilities.FormationFightingInMelee(__instance.Formation, 0.5f))
            {
                Formation enemyCav = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);

                if (enemyCav != null && !enemyCav.QuerySystem.IsCavalryFormation)
                {
                    enemyCav = null;
                }

                float cavDist = 0f;
                float signDist = 1f;
                if (enemyCav != null && significantEnemy != null)
                {
                    Vec2 cavDirection = RBMAI.Utilities.GetFormationCenter(enemyCav) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                    cavDist = cavDirection.Normalize();

                    Vec2 signDirection = RBMAI.Utilities.GetFormationCenter(significantEnemy) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                    signDist = signDirection.Normalize();
                }

                if ((enemyCav != null) && (cavDist <= signDist) && (enemyCav.CountOfUnits > __instance.Formation.CountOfUnits / 10) && (signDist > 35f))
                {
                    if (enemyCav.TargetFormation == __instance.Formation && (enemyCav.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || enemyCav.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge))
                    {
                        if (RBMAI.Utilities.CheckIfCanBrace(__instance))
                        {
                            order = 1;
                        }
                        else
                        {
                            order = 0;
                        }
                    }
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Agent))]
    internal class OverrideUpdateFormationOrders
    {
        [HarmonyPrefix]
        [HarmonyPatch("UpdateFormationOrders")]
        private static bool PrefixUpdateFormationOrders(ref Agent __instance)
        {
            if (__instance.Formation != null && __instance.IsAIControlled && __instance.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
            {
                if (__instance.Formation.ArrangementOrder.OrderEnum == ArrangementOrderEnum.Square ||
                    __instance.Formation.ArrangementOrder.OrderEnum == ArrangementOrderEnum.Circle ||
                    __instance.Formation.ArrangementOrder.OrderEnum == ArrangementOrderEnum.ShieldWall)
                {
                    __instance.EnforceShieldUsage(ArrangementOrder.GetShieldDirectionOfUnit(__instance.Formation, __instance, __instance.Formation.ArrangementOrder.OrderEnum));
                }
                else
                {
                    if (!__instance.WieldedOffhandWeapon.IsEmpty)
                    {
                        bool hasnotusableonehand = __instance.Equipment.HasAnyWeaponWithFlags(WeaponFlags.NotUsableWithOneHand);
                        bool hasranged = __instance.IsRangedCached;
                        float distance = __instance.GetTargetAgent() != null ? __instance.Position.Distance(__instance.GetTargetAgent().Position) : 100f;
                        if (!hasnotusableonehand && !hasranged && __instance.GetTargetAgent() != null && distance < 7f)
                        {
                            __instance.EnforceShieldUsage(Agent.UsageDirection.DefendDown);
                        }
                        else
                        {
                            __instance.EnforceShieldUsage(Agent.UsageDirection.None);
                        }
                    }
                }
                return false;
            }
            return true;
        }
    }
}
