using HarmonyLib;

namespace RBMCombat
{
    public static class RBMCombatPatcher
    {
        public static void DoPatching(ref Harmony rbmcombatHarmony)
        {
                rbmcombatHarmony.PatchAll();
        }
    }
}
