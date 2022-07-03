using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace RBMTournament
{
    public class SubModule : MBSubModuleBase
    {
        public static class HarmonyPatchAll
        {
            public static void DoPatching()
            {
                var harmony = new Harmony("com.pf.rbmt");
                harmony.PatchAll();
            }
        }
        protected override void OnSubModuleLoad()
        {
            HarmonyPatchAll.DoPatching();
        }
    }
}