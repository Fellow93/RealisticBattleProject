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
        /// <summary>USER RULE (2026-10-04): an attacker's advance phase must actually advance. Ordinary (non-rally)
        /// Regroup holds the formation on its own centre, and on a ragged line it out-weighs Advance and only lets go
        /// once the line is almost tidy, so it could hold indefinitely (worst after a reinforcement wave). For a
        /// field-battle attacker in its advance phase it may stay active at most this long...</summary>
        private const float AttackerRegroupMaxSeconds = 10f;

        /// <summary>...and may not take over again until the forward behavior has run at least this long.</summary>
        private const float AttackerAdvanceMinSeconds = 20f;

        /// <summary>FormationAI.ActiveBehavior's setter stamps PreserveExpireTime = now + this on activation (its only writer).</summary>
        private const float NativePreserveSeconds = 10f;

        // Advance phase = a forward behavior is weighted. The split tactics' deliberate opening waits weight only
        // Charge + Regroup, and the attack phase (FixCharge) zeroes Regroup, so neither is touched.
        private static bool IsAttackerAdvancing(Formation formation)
        {
            Mission mission = Mission.Current;
            if (mission == null || !mission.IsFieldBattle || formation.Team == null || !formation.Team.IsAttacker)
            {
                return false;
            }
            BehaviorAdvance advance = formation.AI.GetBehavior<BehaviorAdvance>();
            BehaviorCautiousAdvance cautious = formation.AI.GetBehavior<BehaviorCautiousAdvance>();
            return (advance != null && advance.WeightFactor > 0f) || (cautious != null && cautious.WeightFactor > 0f);
        }

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
                // Rally: a chunk of the formation is far from the main body (reinforcement wave + surviving
                // veterans). Native's deviation-based weight excludes exactly those men; force Regroup to win.
                if (RallyLogic.NeedsRally(__instance.Formation))
                {
                    __result = RallyLogic.RallyWeight;
                    return false;
                }
                if (IsAttackerAdvancing(__instance.Formation))
                {
                    BehaviorComponent active = __instance.Formation.AI.ActiveBehavior;
                    float activeFor = Mission.Current.CurrentTime - (active.PreserveExpireTime - NativePreserveSeconds);
                    bool regroupActive = active == __instance;
                    if (regroupActive ? activeFor >= AttackerRegroupMaxSeconds : activeFor < AttackerAdvanceMinSeconds)
                    {
                        __result = 0f;
                        return false;
                    }
                }
                float coherence = __instance.Formation.AI.ActiveBehavior?.BehaviorCoherence ?? __instance.BehaviorCoherence;
                __result = MBMath.Lerp(0.1f, 1.2f, MBMath.ClampFloat(coherence * (querySystem.Formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents + 1f) / (querySystem.IdealAverageDisplacement + 1f), 0f, 3f) / 3f);
                // For a while after a reinforcement wave lands, lean toward closing up before pressing on.
                if (RallyLogic.RecentlyReinforced(__instance.Formation))
                {
                    __result *= RallyLogic.ReinforcedWeightScale;
                }
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorRegroup __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            // Runs for player-led formations too (see OverrideBehaviorCharge) -- don't force Line onto them.
            if (__instance.Formation != null && !__instance.Formation.IsAIControlled)
            {
                return true;
            }
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                // Only while Regroup is the active behaviour: FindBestBehavior also runs this on a mere candidate,
                // and the flip to Line and back re-applied behaviour values to every agent (see BehaviorChargePatch).
                // Once active, the TickOccasionally postfix below sets Line every tick anyway.
                if (__instance.Formation.AI != null && __instance.Formation.AI.ActiveBehavior == __instance)
                {
                    __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
                }
                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
                if (significantEnemy != null)
                {
                    WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                    if (RallyLogic.IsRallying(__instance.Formation))
                    {
                        // Hold at the rally anchor -- the wave's spawn point after reinforcements, else the MAIN
                        // BODY -- not the average (which the far men drag toward the enemy).
                        Vec2 mainBody = RallyLogic.MainBodyCenter(__instance.Formation);
                        if (mainBody.IsValid)
                        {
                            medianPosition.SetVec2(mainBody);
                        }
                    }
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
            if (RallyLogic.NeedsRally(__instance.Formation))
            {
                // Give each far man the nearest cell so he runs straight at the body, not a flank diagonal.
                RallyLogic.PullFarMenToNearestCells(__instance.Formation);
            }
        }
    }
}
