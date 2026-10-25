using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    // Native detachment weighting dereferences standingPoint.MovingAgent.Formation.Team.Side unchecked; an agent that lost its
    // formation (mod reassigning agents mid-battle) crashes the mission with an NRE. Treat the machine as unusable that tick.
    [HarmonyPatch]
    internal static class SiegeMachineDetachmentGuard
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // A target a game update removed is skipped (a null target makes Harmony throw for the whole class).
            MethodBase[] targets =
            {
                AccessTools.Method(typeof(UsableMachine), "GetDetachmentWeightAux"),
                AccessTools.Method(typeof(SiegeLadder), "GetDetachmentWeightAux"),
                AccessTools.Method(typeof(StonePile), "GetDetachmentWeightAux"),
                AccessTools.Method(typeof(RangedSiegeWeapon), "GetDetachmentWeightAuxForExternalAmmoWeapons")
            };
            foreach (MethodBase target in targets)
            {
                if (target != null)
                {
                    yield return target;
                }
            }
        }

        private static Exception Finalizer(Exception __exception, ref float __result)
        {
            if (__exception is NullReferenceException)
            {
                __result = float.MinValue;
                return null;
            }
            return __exception;
        }
    }
}
