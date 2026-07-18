using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.Formation;
using static TaleWorlds.MountAndBlade.MovementOrder;

namespace RBMAI
{
    [HarmonyPatch(typeof(HumanAIComponent))]
    internal class OverrideParallelFormationMovement
    {
        private static readonly PropertyInfo ShouldCatchUpWithFormationProperty =
            typeof(HumanAIComponent).GetProperty("ShouldCatchUpWithFormation");

        [HarmonyPostfix]
        [HarmonyPatch("ParallelUpdateFormationMovement")]
        private static void PostfixParallelUpdateFormationMovement(ref HumanAIComponent __instance, ref Agent ___Agent)
        {
            // This runs on a native worker thread during the parallel formation-movement job and WRITES agent
            // formation state (SetValue / SetFormationIntegrityData / SetFormationFrameDisabled). MissionLibrary mods
            // (RTSCamera/CommandSystem/BattleMiniMap) also hook HumanAIComponent's parallel movement path; two mods
            // mutating formation state on the same worker job races the native update -> use-after-free AVE. When one
            // is present, stay off this path entirely and let native own it.
            if (RBMAI.Tactics.IsFormationReshufflingUnsafe)
            {
                return;
            }
            if (___Agent.IsActive() == false || ___Agent.Formation == null)
            {
                return;
            }
            MovementOrder.MovementOrderEnum orderType = ___Agent.Formation.GetReadonlyMovementOrderReference().OrderEnum;
            if (___Agent.Controller == AgentControllerType.AI && orderType == MovementOrder.MovementOrderEnum.Move && ___Agent.Formation.ArrangementOrder != ArrangementOrder.ArrangementOrderColumn)
            {
                Vec2 currentGlobalPositionOfUnit = ___Agent.Formation.GetCurrentGlobalPositionOfUnit(___Agent, false);
                FormationIntegrityDataGroup formationIntegrityData = ___Agent.Formation.CachedFormationIntegrityData;

                // ShouldCatchUpWithFormation gates GetDesiredSpeedInFormation's cap, which floors at 0.2 of an
                // agent's own top speed. Native clears it in two cases so stragglers can close: the formation has
                // scattered, or this agent is far enough from its slot to need a sprint. Asserting it regardless
                // re-caps exactly those agents every tick -- worst on flanking formations, which travel furthest
                // and spend the longest wheeling, so they sit in both states most of the traverse.
                float catchUpThreshold = formationIntegrityData.AverageMaxUnlimitedSpeedExcludeFarAgents * 3f;
                bool formationScattered = formationIntegrityData.DeviationOfPositionsExcludeFarAgents > catchUpThreshold;
                bool unitFarFromSlot = ___Agent.Position.AsVec2.Distance(currentGlobalPositionOfUnit) >= catchUpThreshold * 2f;

                if (!formationScattered && !unitFarFromSlot)
                {
                    ShouldCatchUpWithFormationProperty.SetValue(__instance, true, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                    ___Agent.SetFormationIntegrityData(currentGlobalPositionOfUnit, ___Agent.Formation.CurrentDirection, formationIntegrityData.AverageVelocityExcludeFarAgents, formationIntegrityData.AverageMaxUnlimitedSpeedExcludeFarAgents, formationIntegrityData.DeviationOfPositionsExcludeFarAgents, true);
                }
            }
            if (orderType == MovementOrder.MovementOrderEnum.Charge || orderType == MovementOrder.MovementOrderEnum.ChargeToTarget)
            {
                ___Agent.SetFormationFrameDisabled();
            }
        }
    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    internal class OverrideFormationMovementComponent
    {
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
            // Also on the parallel formation-movement worker path -- see PostfixParallelUpdateFormationMovement.
            // Defer to native (return true = run original) when a MissionLibrary mod is present.
            if (RBMAI.Tactics.IsFormationReshufflingUnsafe)
            {
                return true;
            }
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
