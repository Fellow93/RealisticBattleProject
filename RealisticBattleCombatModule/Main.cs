using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;
using HarmonyLib;
using System.Collections.Generic;
using System.Xml;
using TaleWorlds.Localization;
using TaleWorlds.Engine.Screens;
using TaleWorlds.Core;

namespace RealisticBattleCombatModule
{
    public static class XmlConfig
    {
        public static Dictionary<string, float> dict = new Dictionary<string, float> { };
    }
    public static class MyPatcher
    {
        public static void DoPatching()
        {
            var harmony = new Harmony("com.pf.rbcm");
            harmony.PatchAll();
        }
    }

    class Main : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(BasePath.Name + "Modules/RealisticBattleCombatModule/config.xml");
            foreach (XmlNode childNode in xmlDocument.SelectSingleNode("/config").ChildNodes)
            {
                foreach (XmlNode subNode in childNode)
                {
                    XmlConfig.dict.Add(childNode.Name + "." + subNode.Name, float.Parse(subNode.InnerText));
                }
            }
            MyPatcher.DoPatching();

            //Module.CurrentModule.AddInitialStateOption(new InitialStateOption("RbmConfiguration", new TextObject("RBM Configuration"), 3, delegate
            //{
            //    ScreenManager.PushScreen(new RbmConfigScreen());
            //}, () => (false, new TextObject("Realistic Battle Configuration"))));
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            InformationManager.DisplayMessage(new InformationMessage("RBM Combat Module Active", Color.FromUint(4282569842u)));
        }
    }
}
