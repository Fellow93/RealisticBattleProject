using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static class RBMAiPatcher
    {
        public static Harmony harmony = null;
        public static bool patched = false;

        public static void DoPatching()
        {
            var harmony = new Harmony("com.rbmai");
            //if (!patched)
            //{
            harmony.UnpatchAll(harmony.Id);
            harmony.PatchAll();
            //    patched = true;
            //}
        }

        public static void FirstPatch(ref Harmony rbmaiHarmony)
        {
            harmony = rbmaiHarmony;
            harmony.UnpatchAll(harmony.Id);
            var original = AccessTools.Method(typeof(MissionCombatantsLogic), "EarlyStart");
            var postfix = AccessTools.Method(typeof(Tactics.EarlyStartPatch), nameof(Tactics.EarlyStartPatch.Postfix));
            harmony.Patch(original, null, new HarmonyMethod(postfix));

            //harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }
    }
}