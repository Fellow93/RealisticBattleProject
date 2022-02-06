using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;
using HarmonyLib;
using System.Collections.Generic;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Engine.Screens;
using TaleWorlds.Localization;
using System.IO;

namespace RealisticBattleCombatModule
{
    public static class RBMCMConfig
    {
        public static Dictionary<string, float> dict = new Dictionary<string, float> { };
        public static float ThrustMagnitudeModifier = 0.014f;
        public static float OneHandedThrustDamageBonus = 71.42f;
        public static float TwoHandedThrustDamageBonus = 71.42f;
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

            string defaultConfigFilePath = BasePath.Name + "Modules/RealisticBattleCombatModule/config.xml";
            string configFolderPath = Utilities.GetConfigFolderPath();
            string configFilePath = Utilities.GetConfigFilePath();

            if (!Directory.Exists(configFolderPath))
            {
                Directory.CreateDirectory(configFolderPath);
            }

            if (File.Exists(configFilePath))
            {
                xmlDocument.Load(configFilePath);
            }
            else
            {
                File.Copy(defaultConfigFilePath, configFilePath);
                xmlDocument.Load(configFilePath);
            }

            foreach (XmlNode childNode in xmlDocument.SelectSingleNode("/config").ChildNodes)
            {
                foreach (XmlNode subNode in childNode)
                {
                    RBMCMConfig.dict.Add(childNode.Name + "." + subNode.Name, float.Parse(subNode.InnerText));
                }
            }
            MyPatcher.DoPatching();

            Module.CurrentModule.AddInitialStateOption(new InitialStateOption("RbmConfiguration", new TextObject("RBM Combat Module Settings"), 3, delegate
            {
                ScreenManager.PushScreen(new RbmConfigScreen());
            }, () => (false, new TextObject("RBM Combat Module Settings"))));
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            InformationManager.DisplayMessage(new InformationMessage("RBM Combat Module Active, please check RBM combat module settings, if everything is set up to your liking.", Color.FromUint(4282569842u)));
        }
    }
}
