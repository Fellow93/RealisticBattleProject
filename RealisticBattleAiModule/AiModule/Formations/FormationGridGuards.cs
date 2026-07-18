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
    [HarmonyPatch(typeof(LineFormation))]
    internal class OverrideLineFormation
    {
        [HarmonyPrefix]
        [HarmonyPatch("SwitchUnitLocations")]
        private static bool PrefixSwitchUnitLocations(ref LineFormation __instance, IFormationUnit firstUnit, IFormationUnit secondUnit)
        {
            // Vanilla SwitchUnitLocations indexes _units2D[FormationFileIndex, FormationRankIndex] for BOTH units
            // with no bounds check of its own, so an unplaced unit (index -1) would drive a _units2D[-1, ...]
            // access. Guard ONLY that -- a negative index means the unit is not in the grid, so skipping is
            // correct and loses nothing.
            //
            // Do NOT also gate on Formation != null / IsActive(). Those are the states native performs this
            // maintenance FOR (a dying or just-detached unit), and suppressing it leaves the grid describing
            // units that are no longer there. See PrefixRemoveUnit below -- that is how this crashed.
            return firstUnit != null
                && firstUnit.FormationFileIndex >= 0 && firstUnit.FormationRankIndex >= 0
                && secondUnit != null
                && secondUnit.FormationFileIndex >= 0 && secondUnit.FormationRankIndex >= 0;
        }

        [HarmonyPrefix]
        [HarmonyPatch("RemoveUnit", new Type[] { typeof(IFormationUnit), typeof(bool), typeof(bool) })]
        private static bool PrefixRemoveUnit(IFormationUnit unit, bool fillInTheGap, bool isRemovingFromAnUnavailablePosition = false)
        {
            // Vanilla RemoveUnit indexes _units2D by these indices directly (it only WARNS on an out-of-range
            // slot, then dereferences anyway), so an unplaced unit (negative index) must not be handed to it.
            // A negative index also means the unit is not in the grid, so skipping removal loses nothing.
            //
            // CRITICAL: do NOT gate on ((Agent)unit).Formation != null. Skipping this prefix skips
            // `_units2D[i, j] = null`, so the unit is never unlinked from the arrangement grid -- and an agent's
            // Formation is already cleared by the time it is being removed, which is precisely when removal must
            // happen. That combination left dead agents in _units2D; once freed, the native parallel
            // formation-movement job (TWParallel.For -> AgentTickMT -> HumanAIComponent.ParallelUpdateFormationMovement)
            // walked the grid onto freed memory -> intermittent 0xC0000005 use-after-free on a worker thread,
            // pure-native stack, no managed frames. Only guard what native genuinely cannot handle: the indices.
            return unit != null
                && unit.FormationFileIndex >= 0 && unit.FormationRankIndex >= 0;
        }
    }
}
