using TaleWorlds.MountAndBlade;
using HarmonyLib;
using SandBox.Missions.MissionLogics;

namespace RBMAI
{
    public static class RBMAiPatcher
    {
        public static Harmony harmony = null;
        public static bool patched = false;
        public static void DoPatching()
        {
            //var harmony = new Harmony("com.pf.rbai");
            if (!patched)
            {
                harmony.PatchAll();
                patched = true;
            }
        }

        public static void FirstPatch(ref Harmony rbmaiHarmony)
        {
            harmony = rbmaiHarmony;
            var original = AccessTools.Method(typeof(MissionCombatantsLogic), "EarlyStart");
            var postfix = AccessTools.Method(typeof(Tactics.EarlyStartPatch), nameof(Tactics.EarlyStartPatch.Postfix));
            harmony.Patch(original, null, new HarmonyMethod(postfix));
            var original2 = AccessTools.Method(typeof(CampaignMissionComponent), "EarlyStart");
            var postfix2 = AccessTools.Method(typeof(Tactics.CampaignMissionComponentPatch), nameof(Tactics.CampaignMissionComponentPatch.Postfix));
            harmony.Patch(original2, null, new HarmonyMethod(postfix2));
            
            //harmony.Patch(original, postfix: new HarmonyMethod(postfix));

        }
    }

}
