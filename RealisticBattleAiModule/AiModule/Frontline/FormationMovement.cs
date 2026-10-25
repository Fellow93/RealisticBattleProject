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
        // Private setter via a compiled delegate (per agent per movement update: no boxing/arg-array allocation).
        private static readonly Action<HumanAIComponent, bool> SetShouldCatchUpWithFormation = CreateShouldCatchUpSetter();

        private static Action<HumanAIComponent, bool> CreateShouldCatchUpSetter()
        {
            // Null (patch skips the write) rather than a type-initializer exception that would take the class down.
            try
            {
                MethodInfo setter = AccessTools.PropertySetter(typeof(HumanAIComponent), "ShouldCatchUpWithFormation");
                return setter != null ? AccessTools.MethodDelegate<Action<HumanAIComponent, bool>>(setter) : null;
            }
            catch
            {
                return null;
            }
        }

        // Diagnostic counters (worker-thread Interlocked increments).
        internal static long CallCount;
        internal static long SetCount;

        [HarmonyPostfix]
        [HarmonyPatch("ParallelUpdateFormationMovement")]
        private static void PostfixParallelUpdateFormationMovement(ref HumanAIComponent __instance, ref Agent ___Agent)
        {
            // Worker thread, WRITES agent formation state. MissionLibrary mods also hook this path; two writers on the
            // same worker job race the native update (use-after-free AV), so stay off it when one is present.
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
            // Siege attackers keep native's slot rule (their wall-climb/gate-walk orders are Moves).
            bool isSiegeAttacker = Mission.Current != null && Mission.Current.IsSiegeBattle && ___Agent.Team != null && ___Agent.Team.IsAttacker;
            if (___Agent.Controller == AgentControllerType.AI && orderType == MovementOrder.MovementOrderEnum.Move && !isSiegeAttacker && ___Agent.Formation.ArrangementOrder != ArrangementOrder.ArrangementOrderColumn)
            {
                Vec2 currentGlobalPositionOfUnit = ___Agent.Formation.GetCurrentGlobalPositionOfUnit(___Agent, false);
                FormationIntegrityDataGroup formationIntegrityData = ___Agent.Formation.CachedFormationIntegrityData;

                // Native clears ShouldCatchUpWithFormation for EVERY man once the formation deviation exceeds ~11 m, so a
                // ragged line never paces. Only the per-agent test matters: near his slot paces, far sprints. Move only.
                float catchUpThreshold = formationIntegrityData.AverageMaxUnlimitedSpeedExcludeFarAgents * 3f;
                bool unitFarFromSlot = ___Agent.Position.AsVec2.Distance(currentGlobalPositionOfUnit) >= catchUpThreshold * 2f;

                // "Far from slot -> sprint" assumes the man is lagging, but under RBM's advance the ordered position sits
                // 10-50 m ahead of the formation (AdvanceBehavior), so men already at the front trip it and run ahead.
                // Judge lagging by the line, not the slot: behind the average position may sprint, level or ahead is paced.
                Vec2 formationDirection = ___Agent.Formation.CurrentDirection;
                bool unitBehindLine = (___Agent.Position.AsVec2 - ___Agent.Formation.CachedAveragePosition).DotProduct(formationDirection) < 0f;

                if ((!unitFarFromSlot || !unitBehindLine) && SetShouldCatchUpWithFormation != null)
                {
                    System.Threading.Interlocked.Increment(ref SetCount);
                    SetShouldCatchUpWithFormation(__instance, true);

                    // shouldKeepWithFormationInsteadOfMovingToAgent is hard-set true on purpose: a Move order marches the
                    // formation (native only holds slot for shielded men in mostly-missile formations).
                    // Via DeepWaterSteering: zeroes the position when it or the line to it lies in deep water; inert otherwise.
                    DeepWaterSteering.SetFormationIntegrityData(___Agent, currentGlobalPositionOfUnit, ___Agent.Formation.CurrentDirection, formationIntegrityData.AverageVelocityExcludeFarAgents, formationIntegrityData.AverageMaxUnlimitedSpeedExcludeFarAgents, formationIntegrityData.DeviationOfPositionsExcludeFarAgents, true);
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

        // Formation.IsUnitDetachedForDebug (internal) = _detachedUnits.Contains(unit); differs from Agent.IsDetachedFromFormation
        // (set/cleared at other points). Compiled delegate: no per-call allocation.
        private static readonly Func<Formation, Agent, bool> IsUnitDetachedForDebug = CreateIsUnitDetachedDelegate();

        private static Func<Formation, Agent, bool> CreateIsUnitDetachedDelegate()
        {
            // Null (prefix defers to native) rather than a type-initializer exception.
            try
            {
                MethodInfo method = AccessTools.Method(typeof(Formation), "IsUnitDetachedForDebug", new Type[] { typeof(Agent) });
                return method != null ? AccessTools.MethodDelegate<Func<Formation, Agent, bool>>(method) : null;
            }
            catch
            {
                return null;
            }
        }

        // NOT dead work: native's ChargeToTarget path never calls the managed Formation.GetOrderPositionOfUnit, so this
        // explicit call is the only thing running RBM's frontline prefix for ChargeWithTarget units.
        [HarmonyPrefix]
        [HarmonyPatch("GetFormationFrame")]
        private static bool PrefixGetFormationFrame(ref bool __result, ref Agent ___Agent, ref HumanAIComponent __instance, ref WorldPosition formationPosition, ref Vec2 formationDirection, ref float speedLimit, ref bool limitIsMultiplier)
        {
            // Also on the parallel worker path. Defer to native (return true) when a MissionLibrary mod is present.
            if (RBMAI.Tactics.IsFormationReshufflingUnsafe || IsUnitDetachedForDebug == null)
            {
                return true;
            }
            if (___Agent != null)
            {
                var formation = ___Agent.Formation;
                // Cheapest test first (the detached test is a list scan); all side-effect free.
                // IsOnLand: a swimmer is left to native. *ReadOnly class flags: worker thread (see FrontlinePositioning).
                if (formation != null && formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget
                    && !___Agent.IsMount && ___Agent.IsOnLand()
                    && (formation.QuerySystem.IsCavalryFormationReadOnly || formation.QuerySystem.IsInfantryFormationReadOnly || formation.QuerySystem.IsRangedFormationReadOnly)
                    && !IsUnitDetachedForDebug(formation, ___Agent))
                {
                    formationPosition = formation.GetOrderPositionOfUnit(___Agent);
                    Agent targetAgent = ___Agent.GetTargetAgent();
                    if (targetAgent != null)
                    {
                        formationDirection = targetAgent.Position.AsVec2 - ___Agent.Position.AsVec2;
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
            }

            return true;
        }

        // Breaks native's run-ahead latch under Move orders: with ShouldCatchUpWithFormation false, GetFormationFrame's Hold case
        // gives speedLimit = -1 and the flag only resets within ~22 m of the slot, so a man AHEAD of the line runs away.
        // Only rewrites the out value (no grid / integrity-data writes), so it needs no IsFormationReshufflingUnsafe guard.
        // A man behind the line keeps his unlimited speed so he can catch up.
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
                    return; // behind the line: let him catch up
                }
                float ownMax = agent.MountAgent != null ? agent.MountAgent.GetMaximumForwardUnlimitedSpeed() : agent.GetMaximumForwardUnlimitedSpeed();
                if (ownMax <= 0.01f)
                {
                    return;
                }
                // GetDesiredSpeedInFormation's non-charging formula, slot term included: slows a man ahead of his slot
                // (down to the 0.2 floor) until the line catches him; level or behind gets native's 1.1 x pace.
                Vec2 slot = formation.GetCurrentGlobalPositionOfUnit(agent, true);
                float aheadOfSlot = MathF.Clamp(-agent.AverageVelocity.AsVec2.DotProduct(slot - agent.Position.AsVec2), 0f, 100f);
                float slotScale = (agent.MountAgent != null) ? 4f : 2f;
                float slotFactor = 0.7f + 0.4f * ((ownMax - aheadOfSlot * slotScale) / (ownMax + aheadOfSlot * slotScale));
                speedLimit = MathF.Clamp(slotFactor * formation.CachedMovementSpeed / ownMax, 0.2f, 1f);
                limitIsMultiplier = true;
            }
            catch
            {
                // Worker thread: never let an exception escape.
            }
        }
    }
}
