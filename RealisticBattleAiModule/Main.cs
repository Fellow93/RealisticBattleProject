using TaleWorlds.MountAndBlade;
using HarmonyLib;

namespace RealisticBattleAiModule
{
    public static class MyPatcher
    {
        public static void DoPatching()
        {
            var harmony = new Harmony("com.pf.rbai");
            harmony.PatchAll();
        }
    }

    class Main : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            MyPatcher.DoPatching();
        }
    }
}
