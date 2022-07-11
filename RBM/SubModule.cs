using HarmonyLib;
using RBMAI;
using System;
using System.IO;
using System.Reflection;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace RBM
{
    public static class HarmonyModules
    {
        public static Harmony rbmaiHarmony = new Harmony("com.rbmai");
        public static Harmony rbmtHarmony = new Harmony("com.rbmt");
        public static Harmony rbmcombatHarmony = new Harmony("com.rbmcombat");
    }

    public class SubModule : MBSubModuleBase
    {
        public static string ModuleId = "RBM";

        //public static void RBMAiFirstPatch(Harmony rbmaiHarmony)
        //{
        //    RBMAiPatcher.patched = false;
        //    var original = AccessTools.Method(typeof(MissionCombatantsLogic), "EarlyStart");
        //    var postfix = AccessTools.Method(typeof(Tactics.TeamAiFieldBattle), nameof(Tactics.TeamAiFieldBattle.Postfix));
        //    rbmaiHarmony.Patch(original, null, new HarmonyMethod(postfix));
        //    var original2 = AccessTools.Method(typeof(CampaignMissionComponent), "EarlyStart");
        //    var postfix2 = AccessTools.Method(typeof(Tactics.CampaignMissionComponentPatch), nameof(Tactics.CampaignMissionComponentPatch.Postfix));
        //    rbmaiHarmony.Patch(original2, null, new HarmonyMethod(postfix2));

        //    //harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        //}

        public static void ApplyHarmonyPatches()
        {
            var harmony = new Harmony("com.rbmmain");
            harmony.PatchAll();
            if (RBMConfig.RBMConfig.rbmTournamentEnabled)
            {
                HarmonyModules.rbmtHarmony.PatchAll(Assembly.LoadFrom("../../Modules/RBM/bin/Win64_Shipping_Client/RBMTournament.dll"));
            }
            else
            {
                HarmonyModules.rbmtHarmony.UnpatchAll(HarmonyModules.rbmtHarmony.Id);
            }
            if (RBMConfig.RBMConfig.rbmAiEnabled)
            {
                RBMAiPatcher.FirstPatch(HarmonyModules.rbmaiHarmony);
                //rbmaiHarmony.PatchAll(Assembly.LoadFrom("../../Modules/RBM/bin/Win64_Shipping_Client/RBMAI.dll"));
            }
            else
            {
                HarmonyModules.rbmaiHarmony.UnpatchAll(HarmonyModules.rbmaiHarmony.Id);
            }
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                HarmonyModules.rbmcombatHarmony.PatchAll(Assembly.LoadFrom("../../Modules/RBM/bin/Win64_Shipping_Client/RBMCombat.dll"));
            }
            else
            {
                HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
            }
        }

        public static void LoadConfig()
        {
            string defaultConfigFilePath = BasePath.Name + "Modules/RBM/DefaultConfigDONOTEDIT.xml";
            string configFolderPath = RBMConfig.ConfigUtilities.GetConfigFolderPath();
            string configFilePath = RBMConfig.ConfigUtilities.GetConfigFilePath();

            if (!Directory.Exists(configFolderPath))
            {
                Directory.CreateDirectory(configFolderPath);
            }

            if (File.Exists(configFilePath))
            {
                RBMConfig.RBMConfig.xmlConfig.Load(configFilePath);
            }
            else
            {
                File.Copy(defaultConfigFilePath, configFilePath);
                RBMConfig.RBMConfig.xmlConfig.Load(configFilePath);
            }

            RBMConfig.RBMConfig.parseXmlConfig();
        }

        protected override void OnSubModuleLoad()
        {
            LoadConfig();
            ApplyHarmonyPatches();

            TaleWorlds.MountAndBlade.Module.CurrentModule.AddInitialStateOption(new InitialStateOption("RbmConfiguration", new TextObject("RBM Configuration"), 3, delegate
            {
                TaleWorlds.ScreenSystem.ScreenManager.PushScreen(new RBMConfig.RbmConfigScreen());
            }, () => (false, new TextObject("RBM Configuration"))));
        }

        protected override void OnApplicationTick(float dt)
        {
            if (Mission.Current == null)
            {
                return;
            }
            try
            {
                if (ScreenManager.TopScreen != null && (Mission.Current.IsFieldBattle || Mission.Current.IsSiegeBattle || Mission.Current.SceneName.Contains("arena") || (MapEvent.PlayerMapEvent != null && MapEvent.PlayerMapEvent.IsHideoutBattle)))
                {
                    MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
                    if (missionScreen != null && missionScreen.InputManager != null && missionScreen.InputManager.IsControlDown() && missionScreen.InputManager.IsKeyPressed(InputKey.V))
                    {
                        Mission.Current.SetFastForwardingFromUI(!Mission.Current.IsFastForward);
                        InformationManager.DisplayMessage(new InformationMessage("Vroom = " + Mission.Current.IsFastForward, Color.FromUint(4282569842u)));
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            LoadConfig();
            ApplyHarmonyPatches();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            Game.Current.GameTextManager.LoadGameTexts();
            if (RBMConfig.RBMConfig.rbmAiEnabled)
            {
                if (RBMConfig.RBMConfig.postureEnabled && RBMConfig.RBMConfig.postureGUIEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new PostureVisualLogic());
                }
                mission.AddMissionBehavior((MissionBehavior)(object)new SiegeArcherPoints());
                mission.AddMissionBehavior((MissionBehavior)(object)new PostureLogic());
            }
            base.OnMissionBehaviorInitialize(mission);
        }
    }
}