using TaleWorlds.MountAndBlade;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using System.Collections.Generic;
using System.Xml;
using TaleWorlds.Localization;
using TaleWorlds.Engine.Screens;

namespace RealisticBattleAiModule
{

    public static class XmlConfig
    {
        public static Dictionary<string, float> dict = new Dictionary<string, float> { };
    }
    public static class MyPatcher
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

        public static void FirstPatch()
        {
            harmony = new Harmony("com.pf.rbai");
            var original = AccessTools.Method(typeof(MissionCombatantsLogic), "EarlyStart");
            var postfix = AccessTools.Method(typeof(Tactics.TeamAiFieldBattle), nameof(Tactics.TeamAiFieldBattle.Postfix));
            harmony.Patch(original, null, new HarmonyMethod(postfix));

            //harmony.Patch(original, postfix: new HarmonyMethod(postfix));

        }
    }

    class Main : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(BasePath.Name + "Modules/RealisticBattleAiModule/config.xml");
            foreach (XmlNode childNode in xmlDocument.SelectSingleNode("/config").ChildNodes)
            {
                foreach (XmlNode subNode in childNode)
                {
                    XmlConfig.dict.Add(childNode.Name + "." + subNode.Name, float.Parse(subNode.InnerText));
                }
            }

            Module.CurrentModule.AddInitialStateOption(new InitialStateOption("RbmConfiguration", new TextObject("RBM AI Module Settings"), 4, delegate
            {
                ScreenManager.PushScreen(new RbmConfigScreen());
            }, () => (false, new TextObject("RBM AI Module Settings"))));

            MyPatcher.FirstPatch();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            InformationManager.DisplayMessage(new InformationMessage("RBM AI Module Active", Color.FromUint(4282569842u)));
        }
    }


}
