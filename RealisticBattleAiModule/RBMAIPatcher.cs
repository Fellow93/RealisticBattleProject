using HarmonyLib;
using System;
using System.Reflection;

namespace RBMAI
{
    public static class RBMAiPatcher
    {
        public static Harmony harmony = null;
        public static bool patched = false;

        public static void DoPatching()
        {
            DoStanceOnlyPatching();
        }

        public static void DoStanceOnlyPatching()
        {
            var harmony = new Harmony("com.rbmai");
            harmony.UnpatchAll(harmony.Id);

            Type stanceLogicType = typeof(StanceLogic);
            int patchedCount = 0;
            foreach (Type nestedType in stanceLogicType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (nestedType.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                {
                    harmony.CreateClassProcessor(nestedType).Patch();
                    patchedCount++;
                }
            }

            RBMConfig.SelectiveDebug.Log("RBMAI", "Applied stance/posture-only patch set: " + patchedCount + " classes. Battlefield AI tactics remain vanilla.");
        }

        public static void FirstPatch(ref Harmony rbmaiHarmony)
        {
            harmony = rbmaiHarmony;
            harmony.UnpatchAll(harmony.Id);
            RBMConfig.SelectiveDebug.Log("RBMAI", "Skipped first-stage battlefield AI hooks for compatibility build.");
        }
    }
}