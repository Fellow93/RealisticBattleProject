using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The deep-water grid as the formation code sees it: <see cref="DeepWaterNavBlocker"/> publishes its grid here
    /// while its walls and slot rule are live (null = nothing to do), and clears it on revert and at mission end;
    /// <see cref="MissionStartReset"/> clears it too (and the forced-exit index set). Read on WORKER threads (the batch
    /// postfix below, Frontline's pin gate and forced-exit guard): grid / set reads only, no engine call, no
    /// allocation. The grid itself is immutable.
    /// </summary>
    internal static class DeepWaterSlots
    {
        internal static volatile DeepWaterGrid LiveGrid;

        /// <summary>Formation slots the batch postfix turned unavailable (Interlocked).</summary>
        internal static long PatchedUnavailable;

        /// <summary>Batch calls the postfix saw while the grid was live (Interlocked): proves the patch runs at all.</summary>
        internal static long BatchCalls;

        /// <summary>True when a live grid has this point in a blocked cell (deep + slot margin).</summary>
        internal static bool IsBlocked(Vec2 position)
        {
            DeepWaterGrid grid = LiveGrid;
            return grid != null && grid.IsBlockedAt(position.x, position.y);
        }

        // Agent indices under the blocker's forced exit (a scripted move out of deep water). Written on the main thread by
        // DeepWaterNavBlocker, read on the Frontline worker thread (so Frontline never pins these men): a concurrent set
        // keyed by the int index (no Agent reference held), plus a count for a lock-free fast path.
        private static readonly ConcurrentDictionary<int, byte> ForcedExitAgents = new ConcurrentDictionary<int, byte>();
        private static int _forcedExitCount;

        /// <summary>True while that agent is being driven out of deep water (any thread).</summary>
        internal static bool IsForcedExit(int agentIndex)
        {
            return Volatile.Read(ref _forcedExitCount) > 0 && ForcedExitAgents.ContainsKey(agentIndex);
        }

        internal static void AddForcedExit(int agentIndex)
        {
            if (ForcedExitAgents.TryAdd(agentIndex, 0))
            {
                Interlocked.Increment(ref _forcedExitCount);
            }
        }

        internal static void RemoveForcedExit(int agentIndex)
        {
            byte unused;
            if (ForcedExitAgents.TryRemove(agentIndex, out unused))
            {
                Interlocked.Decrement(ref _forcedExitCount);
            }
        }

        internal static void ClearForcedExits()
        {
            ForcedExitAgents.Clear();
            Interlocked.Exchange(ref _forcedExitCount, 0);
        }

        internal static void Clear()
        {
            LiveGrid = null;
            Interlocked.Exchange(ref PatchedUnavailable, 0L);
            Interlocked.Exchange(ref BatchCalls, 0L);
            Interlocked.Exchange(ref DeepWaterSteering.Calls, 0L);
            Interlocked.Exchange(ref DeepWaterSteering.Nulled, 0L);
            ClearForcedExits();
        }
    }

    /// <summary>
    /// "Keep with the formation" steering, filtered for deep water (user-approved 2026-10-20). Native
    /// HumanAIComponent.ParallelUpdateFormationMovement passes every man within ~22 m (2 × 3 × the formation's speed) of
    /// his place in the MOVING block -- Formation.GetCurrentGlobalPositionOfUnit = CurrentPosition + local offset, never
    /// slot-filtered -- to Agent.SetFormationIntegrityData, and the engine steers him straight there, no path search.
    /// RBM's own postfix on the same method (Frontline/FormationMovement.cs, OverrideParallelFormationMovement) calls it
    /// again for every AI man under a Move order. A block sweeping across a swamp walked its men straight at the wall
    /// (16:09 log: 547 crossings, all order=Move, slot=dry, 2-3 m/s).
    ///
    /// <see cref="SetFormationIntegrityData"/> stands in for Agent.SetFormationIntegrityData at all three call sites:
    /// when the target lies in a blocked cell, or the straight line from the man to it crosses a deep cell, the
    /// position becomes Vec2.Zero -- native's own "not catching up" value -- so he follows his formation frame (his
    /// slot, which the batch fix keeps out of the water, reached by path search) instead.
    ///
    /// Why not a prefix on Agent.SetFormationIntegrityData: it is a one-line wrapper (~29 bytes of IL) around the
    /// native call, the same shape as Mission.BatchFormationUnitPositions, which the JIT inlined so that a patch on it
    /// never ran. A transpiler on the (large, never inlined) caller rewrites the call site itself, and RBM's own call
    /// site calls this helper directly.
    ///
    /// WORKER THREAD: grid reads only (LiveGrid is immutable), Agent.Position (a pointer read, AgentHelper), two
    /// Interlocked counters; no engine call, no allocation.
    /// </summary>
    internal static class DeepWaterSteering
    {
        /// <summary>Calls seen while the grid was live, and positions zeroed (Interlocked). Proves the patch runs.</summary>
        internal static long Calls;
        internal static long Nulled;

        /// <summary>Native call sites the transpiler rewrote (expected 2), -1 before it ran.</summary>
        internal static int TranspiledCallSites = -1;

        private const int MaxLineSamples = 200;

        internal static void SetFormationIntegrityData(Agent agent, Vec2 position, Vec2 currentFormationDirection, Vec2 averageVelocityOfCloseAgents,
            float averageMaxUnlimitedSpeedOfCloseAgents, float deviationOfPositions, bool shouldKeepWithFormationInsteadOfMovingToAgent)
        {
            DeepWaterGrid grid = DeepWaterSlots.LiveGrid;
            if (grid != null && agent != null && (position.x != 0f || position.y != 0f))
            {
                Interlocked.Increment(ref Calls);
                try
                {
                    if (CrossesDeepWater(grid, agent.Position.AsVec2, position))
                    {
                        position = Vec2.Zero;
                        Interlocked.Increment(ref Nulled);
                    }
                }
                catch
                {
                    // Worker thread: keep native's value.
                }
            }
            agent.SetFormationIntegrityData(position, currentFormationDirection, averageVelocityOfCloseAgents,
                averageMaxUnlimitedSpeedOfCloseAgents, deviationOfPositions, shouldKeepWithFormationInsteadOfMovingToAgent);
        }

        /// <summary>True when the target is in a blocked cell or the straight line to it passes a deep cell.
        /// Also Frontline's step gate (FrontlinePositioning, IsStepBlocked): grid reads only, any thread.</summary>
        internal static bool CrossesDeepWater(DeepWaterGrid grid, Vec2 from, Vec2 to)
        {
            if (grid.IsBlockedAt(to.x, to.y))
            {
                return true;
            }
            float dx = to.x - from.x;
            float dy = to.y - from.y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            float spacing = grid.Step * 0.5f;
            int samples = Math.Min(MaxLineSamples, (int)(length / spacing));
            for (int k = 1; k <= samples; k++)
            {
                float t = k * spacing / length;
                if (grid.IsDeepAt(from.x + dx * t, from.y + dy * t))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Rewrites both native calls of Agent.SetFormationIntegrityData inside HumanAIComponent.ParallelUpdateFormationMovement
    /// to <see cref="DeepWaterSteering.SetFormationIntegrityData"/> (same arguments, the agent as the first one, so the
    /// stack is unchanged: callvirt instance -> call static). Inert while no grid is live. Leaves the method untouched
    /// if the call is not found (a game update); DeepWaterSteering.TranspiledCallSites says how many were rewritten.
    /// Coexists with RBM's postfix on the same method.
    /// </summary>
    [HarmonyPatch(typeof(HumanAIComponent), "ParallelUpdateFormationMovement")]
    internal static class DeepWaterSteeringTranspiler
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo target = AccessTools.Method(typeof(Agent), "SetFormationIntegrityData");
            MethodInfo replacement = AccessTools.Method(typeof(DeepWaterSteering), "SetFormationIntegrityData");
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int replaced = 0;
            if (target != null && replacement != null)
            {
                foreach (CodeInstruction code in codes)
                {
                    if ((code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Call) && code.operand is MethodInfo called && called == target)
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = replacement;
                        replaced++;
                    }
                }
            }
            DeepWaterSteering.TranspiledCallSites = replaced;
            return codes;
        }
    }

    /// <summary>
    /// LineFormation (and every arrangement derived from it: Circular, CircularSchiltron, Square, RectilinearSchiltron,
    /// Skein, TransposedLine, Wedge) gets its slots from LineFormation.BatchUnitPositionAvailabilities ->
    /// Formation.IFormation.BatchUnitPositions -> Mission.BatchFormationUnitPositions -> native
    /// batch_formation_unit_positions, which fills the availability and global-position tables NATIVELY, without
    /// ever calling Mission.IsFormationUnitPositionAvailable_AdditionalCondition (that only guards
    /// IsFormationUnitPositionAvailableMT / IsOrderPositionAvailable). This postfix marks every slot whose global
    /// position lies in a blocked cell unavailable.
    ///
    /// Target: Formation's explicit IFormation.BatchUnitPositions, NOT Mission.BatchFormationUnitPositions. The latter
    /// is an internal one-line wrapper the JIT inlines into its caller, so a Harmony patch on it never ran
    /// (battle_terrain_034, 2026-10-20: batchSlotsPatchedUnavailable=0 while slots were still placed in deep water).
    /// LineFormation calls this one through the IFormation interface, which cannot be inlined. It returns false
    /// (tables untouched) when the order position has no navmesh; the postfix then does nothing.
    ///
    /// Availability values (LineFormation): 0 = not computed yet (OnFormationFrameChanged clears the table to 0 and
    /// InvalidateCacheOfUnitAux resets one entry to 0, so the native batch fills them), 1 = unavailable (also used by
    /// MakeRestrainedPositionsUnavailable for the reserved middle-front slot, and for the whole table when the batch
    /// cannot run), 2 = available (IsUnitPositionAvailable is "== 2"). So 2 -> 1 is "unavailable", exactly what native
    /// writes for a slot over an obstacle; LineFormation then shifts those men back
    /// (ShiftUnitsBackwardsForNewUnavailableUnitPositions), fills the gaps and re-adds unpositioned units, as it does
    /// around rocks. Only entries at 2 are touched, so a restrained or not-yet-computed slot is never changed.
    ///
    /// Threading: BatchUnitPositionAvailabilities runs wherever the formation frame changes, which can be a worker.
    /// The postfix reads only DeepWaterSlots.LiveGrid and the two managed tables; WorldPosition.AsVec2 is a plain
    /// field read. Exceptions are swallowed so nothing escapes on a worker thread.
    ///
    /// Ordering: no other RBM patch targets Mission.BatchFormationUnitPositions or Formation.IFormation.BatchUnitPositions
    /// (FormationGridGuards patches LineFormation.SwitchUnitLocations / RemoveUnit, Frontline the Formation's
    /// GetOrderPositionOfUnit prefix, which only reads the slots this leaves behind), so default priority is enough.
    /// The siege-only native subscriber of the additional condition (SiegeDeploymentHandler) is unaffected: LiveGrid is
    /// only set in land field battles.
    /// </summary>
    [HarmonyPatch(typeof(Formation), "TaleWorlds.MountAndBlade.IFormation.BatchUnitPositions")]
    internal static class DeepWaterBatchSlotPatch
    {
        private const int Available = 2;
        private const int Unavailable = 1;

        [HarmonyPostfix]
        private static void Postfix(bool __result, MBList2D<int> availabilityTable, MBList2D<WorldPosition> globalPositionTable, int fileCount, int rankCount)
        {
            DeepWaterGrid grid = DeepWaterSlots.LiveGrid;
            if (!__result || grid == null || availabilityTable == null || globalPositionTable == null)
            {
                return;
            }
            Interlocked.Increment(ref DeepWaterSlots.BatchCalls);
            try
            {
                int files = Math.Min(fileCount, Math.Min(availabilityTable.Count1, globalPositionTable.Count1));
                int ranks = Math.Min(rankCount, Math.Min(availabilityTable.Count2, globalPositionTable.Count2));
                int patched = 0;
                for (int f = 0; f < files; f++)
                {
                    for (int r = 0; r < ranks; r++)
                    {
                        if (availabilityTable[f, r] != Available)
                        {
                            continue;
                        }
                        Vec2 p = globalPositionTable[f, r].AsVec2;
                        if (grid.IsBlockedAt(p.x, p.y))
                        {
                            availabilityTable[f, r] = Unavailable;
                            patched++;
                        }
                    }
                }
                if (patched > 0)
                {
                    Interlocked.Add(ref DeepWaterSlots.PatchedUnavailable, patched);
                }
            }
            catch
            {
                // Worker thread: leave native's result as it is.
            }
        }
    }
}
