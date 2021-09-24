using TaleWorlds.MountAndBlade;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using System.Collections.Generic;
using System.Xml;

namespace RealisticBattleAiModule
{

    public static class XmlConfig
    {
        public static Dictionary<string, float> dict = new Dictionary<string, float> { };
    }
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
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(BasePath.Name + "Modules/RealisticBattleAiModule/config.xml");
            foreach (XmlNode childNode in xmlDocument.SelectSingleNode("/config").ChildNodes)
            {
                foreach (XmlNode subNode in childNode)
                {
                    XmlConfig.dict.Add(childNode.Name + "." + subNode.Name, float.Parse(subNode.InnerText));
                }
            }

            MyPatcher.DoPatching();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            InformationManager.DisplayMessage(new InformationMessage("RBM AI Module Active", Color.FromUint(4282569842u)));
        }
    }


}
