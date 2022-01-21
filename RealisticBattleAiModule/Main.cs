using TaleWorlds.MountAndBlade;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using System.Collections.Generic;
using System.Xml;
using TaleWorlds.Localization;
using TaleWorlds.Engine.Screens;
using System.IO;
using RealisticBattleAiModule.AiModule.Posture;
using SandBox;

namespace RealisticBattleAiModule
{

    public static class XmlConfig
    {
        public static Dictionary<string, float> dict = new Dictionary<string, float> { };
    }

    public static class RBMCMXmlConfig
    {
        public static Dictionary<string, float> dict = new Dictionary<string, float> { };
    }

    public static class AgentPostures
    {
        public static Dictionary<Agent, Posture> values = new Dictionary<Agent, Posture> { };
        public static PostureVisualLogic postureVisual = null;
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
            var original2 = AccessTools.Method(typeof(CampaignMissionComponent), "EarlyStart");
            var postfix2 = AccessTools.Method(typeof(Tactics.CampaignMissionComponentPatch), nameof(Tactics.CampaignMissionComponentPatch.Postfix));
            harmony.Patch(original2, null, new HarmonyMethod(postfix2));
            
            //harmony.Patch(original, postfix: new HarmonyMethod(postfix));

        }
    }

    class Main : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            XmlDocument xmlDocument = new XmlDocument();

            string defaultConfigFilePath = BasePath.Name + "Modules/RealisticBattleAiModule/config.xml";
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
                    XmlConfig.dict.Add(childNode.Name + "." + subNode.Name, float.Parse(subNode.InnerText));
                }
            }

            string rbmcmConfigFolderPath = Utilities.GetRBMCMConfigFolderPath();
            string rbmcmConfigFilePath = Utilities.GetRBMCMConfigFilePath();

            if (Directory.Exists(rbmcmConfigFolderPath))
            {
                if (File.Exists(rbmcmConfigFilePath))
                {
                    xmlDocument.Load(rbmcmConfigFilePath);

                    foreach (XmlNode childNode in xmlDocument.SelectSingleNode("/config").ChildNodes)
                    {
                        foreach (XmlNode subNode in childNode)
                        {
                            RBMCMXmlConfig.dict.Add(childNode.Name + "." + subNode.Name, float.Parse(subNode.InnerText));
                        }
                    }
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

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            mission.AddMissionBehavior((MissionBehavior)(object)new PostureVisualLogic());
            base.OnMissionBehaviorInitialize(mission);
        }
    }

}
