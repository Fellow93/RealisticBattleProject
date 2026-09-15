using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    [HarmonyPatch(typeof(GeneralsAndCaptainsAssignmentLogic))]
    [HarmonyPatch("CanTeamHaveGeneralsFormation")]
    internal class CanTeamHaveGeneralsFormationPatch
    {
        private static bool Prefix(ref GeneralsAndCaptainsAssignmentLogic __instance, ref Team team, bool __result)
        {
            __result = false;
            return false;
        }
    }
}
