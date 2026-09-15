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

        // Diagnostic counters read by AiBehaviorLogic (worker-thread increments, Interlocked).
        internal static long CallCount;
        internal static long SetCount;

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
            System.Threading.Interlocked.Increment(ref CallCount);
            MovementOrder.MovementOrderEnum orderType = ___Agent.Formation.GetReadonlyMovementOrderReference().OrderEnum;
            if (___Agent.Controller == AgentControllerType.AI && orderType == MovementOrder.MovementOrderEnum.Move && ___Agent.Formation.ArrangementOrder != ArrangementOrder.ArrangementOrderColumn)
            {
                Vec2 currentGlobalPositionOfUnit = ___Agent.Formation.GetCurrentGlobalPositionOfUnit(___Agent, false);
                FormationIntegrityDataGroup formationIntegrityData = ___Agent.Formation.CachedFormationIntegrityData;

                // ShouldCatchUpWithFormation gates GetDesiredSpeedInFormation's cap (CachedMovementSpeed / own
                // top speed, floored at 0.2). Native clears it for EVERY man once the formation's deviation exceeds
                // ~3x average speed (~11 m), so a line that spawns ragged (16-22 m at 500 men) never paces at all:
                // everyone runs flat out, the fast men outrun the slow, and the deviation never drops back under
                // the gate. Logged 2026-09-09: enemy infantry sat at 15 m deviation with catchUp=0 on all 500 for
                // the whole approach. Only the per-agent test matters: a man near his slot paces, a man far from
                // it sprints to close. Charge orders are excluded above (Move only).
                float catchUpThreshold = formationIntegrityData.AverageMaxUnlimitedSpeedExcludeFarAgents * 3f;
                bool unitFarFromSlot = ___Agent.Position.AsVec2.Distance(currentGlobalPositionOfUnit) >= catchUpThreshold * 2f;

                // The "far from slot -> sprint" escape above (native's own rule, kept) assumes a man far from
                // his slot is LAGGING and needs to close. Under RBM's advance that assumption breaks: the
                // ordered position is placed 10-50 m + half a depth AHEAD of the formation's own centre
                // (AdvanceBehavior.PrefixCalculateCurrentOrder), so every slot sits far in front of the line
                // and the distance test trips for men who are already at the front. Their speed cap is then
                // dropped and whoever has the highest unlimited speed (light gear / high athletics) walks out
                // of the line toward the carrot -- the reported "one or two soldiers run ahead of the
                // formation". Judge lagging by the line, not by the slot: a man behind the formation's average
                // position may sprint, a man level with or ahead of it is paced regardless of slot distance.
                Vec2 formationDirection = ___Agent.Formation.CurrentDirection;
                bool unitBehindLine = (___Agent.Position.AsVec2 - ___Agent.Formation.CachedAveragePosition).DotProduct(formationDirection) < 0f;

                if ((!unitFarFromSlot || !unitBehindLine) && ShouldCatchUpWithFormationProperty != null)
                {
                    System.Threading.Interlocked.Increment(ref SetCount);
                    ShouldCatchUpWithFormationProperty.SetValue(__instance, true, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                    // shouldKeepWithFormationInsteadOfMovingToAgent is hard-set to true on purpose. Native
                    // computes it as (shield in the offhand) && (formation is >=50% ranged), so only a
                    // shielded man in a mostly-missile formation holds his slot and everyone else drifts
                    // toward whichever agent he is targeting. RBM wants every moving unit to hold slot --
                    // a Move order should march the formation, not smear it toward enemy individuals.
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

        // NOT dead work, even though the sibling postfix disables the frame again for ChargeToTarget.
        // Native's ChargeToTarget path goes through Agent.GetBaseFormationFrame and never calls the managed
        // Formation.GetOrderPositionOfUnit, so this explicit call is the ONLY thing that runs RBM's frontline
        // prefix (the mindset step + ClearTargetFrame side effect) for units under ChargeWithTarget.
        // Removing it made ChargeWithTarget infantry run straight in instead of advancing as a line.
        // The speed limit (isCharging: false -> CachedMovementSpeed) also survives the postfix.
        [HarmonyPrefix]
        [HarmonyPatch("GetFormationFrame")]
        private static bool PrefixGetFormationFrame(ref bool __result, ref Agent ___Agent, ref HumanAIComponent __instance, ref WorldPosition formationPosition, ref Vec2 formationDirection, ref float speedLimit, ref bool limitIsMultiplier)
        {
            // Also on the parallel formation-movement worker path -- see PostfixParallelUpdateFormationMovement.
            // Defer to native (return true = run original) when a MissionLibrary mod is present.
            if (RBMAI.Tactics.IsFormationReshufflingUnsafe || IsUnitDetachedForDebug == null)
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

        // Breaks native's run-ahead latch under Move orders. GetFormationFrame's Hold case hands out
        // speedLimit = -1 (no limit) whenever ShouldCatchUpWithFormation is false, and native only sets that
        // flag true again once the man is back within ~22 m of his slot. A man who trips it while AHEAD of
        // the line therefore runs at his own unlimited speed, pulls further ahead, and can never re-enter the
        // window -- the "one or two soldiers sprint to the front and don't wait" on every advance (logged
        // 2026-09-11: the captain and one ranker at limit -1 / catchUp 0 / 2-4 m/s while 498 men sat at 0.8).
        //
        // This postfix only rewrites the out value: no formation-grid or integrity-data writes, so it does
        // NOT need the IsFormationReshufflingUnsafe guard the sibling patches carry -- which is the point,
        // since with MissionLibrary loaded those patches never run and the latch was going unbroken.
        // A man behind the line keeps his unlimited speed so he can genuinely catch up.
        [HarmonyPostfix]
        [HarmonyPatch("GetFormationFrame")]
        private static void PostfixGetFormationFrame(ref Agent ___Agent, ref float speedLimit, ref bool limitIsMultiplier)
        {
            try
            {
                if (speedLimit >= 0f || !HumanAIComponent.FormationSpeedAdjustmentEnabled)
                {
                    return;
                }
                Agent agent = ___Agent;
                if (agent == null || agent.IsMount || !agent.IsActive() || agent.Controller != AgentControllerType.AI || agent.IsDetachedFromFormation)
                {
                    return;
                }
                Formation formation = agent.Formation;
                if (formation == null || formation.Arrangement is ColumnFormation)
                {
                    return;
                }
                if (formation.GetReadonlyMovementOrderReference().MovementState != MovementOrder.MovementStateEnum.Hold)
                {
                    return;
                }
                Vec2 direction = formation.CurrentDirection;
                if (!direction.IsValid || direction.LengthSquared < 0.01f)
                {
                    return;
                }
                float ahead = (agent.Position.AsVec2 - formation.CachedAveragePosition).DotProduct(direction);
                if (ahead < 0f)
                {
                    return; // behind the line: let him sprint to catch up, as native intends
                }
                float ownMax = agent.MountAgent != null ? agent.MountAgent.GetMaximumForwardUnlimitedSpeed() : agent.GetMaximumForwardUnlimitedSpeed();
                if (ownMax <= 0.01f)
                {
                    return;
                }
                // Same shape as GetDesiredSpeedInFormation's steady-state result: pace to the formation's
                // cached movement speed, floored at 0.2 of own top speed.
                speedLimit = MathF.Clamp(formation.CachedMovementSpeed / ownMax, 0.2f, 1f);
                limitIsMultiplier = true;
            }
            catch
            {
                // Worker-thread path: never let an exception escape.
            }
        }
    }
}
