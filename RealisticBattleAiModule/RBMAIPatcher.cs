using HarmonyLib;
using System;
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
            harmony.UnpatchAll(harmony.Id);
            // PatchAll equivalent, one class at a time: PatchAll stops at the first failing class (renamed target or ___field) and
            // throws out of EarlyStart, leaving every later class unpatched. Now only that class is skipped and logged (AiTickGuard).
            int failed = 0;
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(RBMAiPatcher).Assembly))
            {
                if (!type.HasHarmonyAttribute())
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    failed++;
                    AiTickGuard.Report("patch " + type.FullName, e, null);
                }
            }
            if (failed > 0)
            {
                AiTickGuard.Flush();
            }
        }

        public static void FirstPatch(ref Harmony rbmaiHarmony)
        {
            harmony = rbmaiHarmony;
            harmony.UnpatchAll(harmony.Id);
            var original = AccessTools.Method(typeof(MissionCombatantsLogic), "EarlyStart");
            var postfix = AccessTools.Method(typeof(Tactics.EarlyStartPatch), nameof(Tactics.EarlyStartPatch.Postfix));
            harmony.Patch(original, null, new HarmonyMethod(postfix));
        }
    }
}