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
                    __result = MovementOrder.MovementOrderChargeToTarget(ChargeTargetFor(formation));
                }
                else
                {
                    __result = MovementOrder.MovementOrderCharge;
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Native's closest significantly large enemy formation; when that one is mounted, an AI foot formation charges the nearest
        /// significant enemy FOOT formation unless riders clearly outweigh it (ChargeTargetChoice). Cached formation data only (AI thread).
        /// </summary>
        internal static Formation ChargeTargetFor(Formation formation)
        {
            Formation closest = formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation?.Formation;
            FormationQuerySystem own = formation.QuerySystem;
            if (closest == null || !formation.IsAIControlled || own.IsCavalryFormation || own.IsRangedCavalryFormation || formation.Team == null)
            {
                return closest;
            }
            if (!ChargeTargetChoice.IsMounted(closest))
            {
                return ChargeTargetChoice.Prefer(formation, closest, null);
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            Formation best = null;
            float bestDistance = float.MaxValue;
            foreach (Team team in Mission.Current.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits <= 0 || enemy.QuerySystem.IsCavalryFormation || enemy.QuerySystem.IsRangedCavalryFormation
                        || !ThreatSignificance.IsSignificant(formation, enemy))
                    {
                        continue;
                    }
                    float distance = centre.DistanceSquared(RBMAI.Utilities.GetFormationCenter(enemy));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = enemy;
                    }
                }
            }
            return ChargeTargetChoice.Prefer(formation, best, null) ?? closest;
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
                // Worker thread (CreateNewOrderWorldPositionMT): exactly one SetVec2 per WorldPosition, off a fresh copy (a second triggers native Z validation).
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
        // Priority.First: OrderReactionHold's stamp prefix must see the order really applied (ChargeGuard may turn a charge into a Move).
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
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
                MovementOrder original = input;
                if (Mission.Current.IsFieldBattle && input.OrderType == OrderType.Charge)
                {
                    if (__instance.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                    {
                        input = MovementOrder.MovementOrderChargeToTarget(OverrideMovementOrder.ChargeTargetFor(__instance));
                    }
                }
                // DecisionTrace (observation only): what the caller asked before ChargeGuard.
                bool trace = DecisionTrace.Active;
                MovementOrder.MovementOrderEnum askedKind = input.OrderEnum;
                Formation askedTarget = trace ? input.TargetFormation : null;
                if (trace)
                {
                    DecisionTrace.Sweep(__instance, null, false);
                }
                // User rule: an AI foot formation charges only within 50 m of its target (ChargeGuard).
                ChargeGuard.Apply(__instance, ref input, original);
                if (trace)
                {
                    DecisionTrace.Sweep(__instance, "chargeGuard", false);
                    DecisionTrace.OnMovementOrder(__instance, original.OrderEnum, askedKind, askedTarget, ref input);
                }
                return true;
            }
            catch (Exception e)
            {
                // The order goes through as given; counted and logged (AiTickGuard).
                AiTickGuard.Report("SetMovementOrder prefix (ChargeGuard)", e, null);
                return true;
            }
        }

        // A player's "charge that formation" order is SetMovementOrder(Charge) then SetTargetFormation(pick). The prefix above already
        // turned the Charge into a charge at the NEAREST large enemy formation, so once the pick arrives re-issue the charge at it.
        // Player-controlled formations only.
        [HarmonyPostfix]
        [HarmonyPatch("SetTargetFormation")]
        private static void PostfixSetTargetFormation(Formation __instance, Formation targetFormation)
        {
            try
            {
                if (targetFormation == null || __instance == null || __instance.IsAIControlled || Mission.Current == null || !Mission.Current.IsFieldBattle)
                {
                    return;
                }
                MovementOrder current = __instance.GetReadonlyMovementOrderReference();
                // A far charge is walked as ChargeGuard's Move: its aim is the guard's target.
                bool advancing = ChargeGuard.IsAdvancingToCharge(__instance);
                Formation aimedAt = advancing ? ChargeGuard.AdvanceTarget(__instance) : current.TargetFormation;
                if ((!advancing && current.OrderType != OrderType.ChargeWithTarget) || aimedAt == targetFormation || targetFormation.CountOfUnits <= 0)
                {
                    return;
                }
                // SetMovementOrder ends by clearing the target, so set the pick again (the nested call returns above).
                __instance.SetMovementOrder(MovementOrder.MovementOrderChargeToTarget(targetFormation));
                __instance.SetTargetFormation(targetFormation);
            }
            catch (Exception)
            {
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

                if ((enemyCav != null) && (cavDist <= signDist) && AiModule.RbmBehaviors.OverrideBehaviorCharge.BraceWorthy(__instance.Formation, enemyCav) && (signDist > 35f))
                {
                    if (AiModule.RbmBehaviors.OverrideBehaviorCharge.ChargeTargetOf(enemyCav) == __instance.Formation)
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
            // Retreating agents fall through to vanilla, which enforces no shield usage on them.
            if (__instance.Formation != null && __instance.IsAIControlled && !__instance.IsRetreating() && __instance.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
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
