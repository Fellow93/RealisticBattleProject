using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    // Native siege-machine detachment weighting dereferences
    // standingPoint.MovingAgent.Formation.Team.Side with no null check. An agent
    // still walking to a standing point after losing its formation (usually a
    // mod reassigning agents mid-battle) throws an NRE out of Team.Tick and
    // crashes the mission. Treat the machine as unusable for that tick instead,
    // exactly the value vanilla returns when no standing point is available.
    [HarmonyPatch]
    internal static class SiegeMachineDetachmentGuard
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UsableMachine), "GetDetachmentWeightAux");
            yield return AccessTools.Method(typeof(SiegeLadder), "GetDetachmentWeightAux");
            yield return AccessTools.Method(typeof(StonePile), "GetDetachmentWeightAux");
            yield return AccessTools.Method(typeof(RangedSiegeWeapon), "GetDetachmentWeightAuxForExternalAmmoWeapons");
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
