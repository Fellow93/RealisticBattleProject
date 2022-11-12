using HarmonyLib;
using RBMAI;
using RBMCombat;
using RBMTournament;
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
        public static Harmony rbmHarmony = new Harmony("com.rbmmain");
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
            UnpatchAllRBM();
            HarmonyModules.rbmHarmony.PatchAll();
            //string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (RBMConfig.RBMConfig.rbmTournamentEnabled)
            {
                //string xmlFileName = Path.Combine(assemblyFolder, "RBMTournament.dll");
                //HarmonyModules.rbmtHarmony.PatchAll(Assembly.LoadFrom(xmlFileName));
                RBMTournamentPatcher.DoPatching(ref HarmonyModules.rbmtHarmony);
            }
            else
            {
                HarmonyModules.rbmtHarmony.UnpatchAll(HarmonyModules.rbmtHarmony.Id);
            }
            if (RBMConfig.RBMConfig.rbmAiEnabled)
            {
                RBMAiPatcher.FirstPatch(ref HarmonyModules.rbmaiHarmony);
            }
            else
            {
                HarmonyModules.rbmaiHarmony.UnpatchAll(HarmonyModules.rbmaiHarmony.Id);
            }
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                //string xmlFileName = Path.Combine(assemblyFolder, "RBMCombat.dll");
                //HarmonyModules.rbmcombatHarmony.PatchAll(Assembly.LoadFrom(xmlFileName));
                RBMCombatPatcher.DoPatching(ref HarmonyModules.rbmcombatHarmony);
            }
            else
            {
                HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
            }
        }

        public static void UnpatchAllRBM()
        {
            RBMAiPatcher.patched = false;
            HarmonyModules.rbmHarmony.UnpatchAll(HarmonyModules.rbmHarmony.Id);
            HarmonyModules.rbmtHarmony.UnpatchAll(HarmonyModules.rbmtHarmony.Id);
            HarmonyModules.rbmaiHarmony.UnpatchAll(HarmonyModules.rbmaiHarmony.Id);
            HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
        }

        protected override void OnSubModuleLoad()
        {
            RBMConfig.RBMConfig.LoadConfig();
            //ApplyHarmonyPatches();

            TaleWorlds.MountAndBlade.Module.CurrentModule.AddInitialStateOption(new InitialStateOption("RbmConfiguration", new TextObject("RBM Configuration"), 9999, delegate
            {
               ScreenManager.PushScreen(new RBMConfig.RBMConfigScreen());
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
            RBMConfig.RBMConfig.LoadConfig();
            ApplyHarmonyPatches();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            Game.Current.GameTextManager.LoadGameTexts();
            if (RBMConfig.RBMConfig.developerMode)
            {
                mission.AddMissionBehavior((MissionBehavior)(object)new BattleStatsLogic());
                if (RBMConfig.RBMConfig.rbmCombatEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new PlayerArmorStatus());
                }
            }
            if (RBMConfig.RBMConfig.rbmAiEnabled)
            {
                if (RBMConfig.RBMConfig.postureEnabled && RBMConfig.RBMConfig.postureGUIEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new PostureVisualLogic());
                }
                mission.AddMissionBehavior((MissionBehavior)(object)new SiegeArcherPoints());
                mission.AddMissionBehavior((MissionBehavior)(object)new PostureLogic());
            }
            else
            {
                if(mission.GetMissionBehavior<SiegeArcherPoints>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<SiegeArcherPoints>());
                }
                if (mission.GetMissionBehavior<PostureVisualLogic>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<PostureVisualLogic>());
                }
                if (mission.GetMissionBehavior<PostureLogic>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<PostureLogic>());
                }
            }
            base.OnMissionBehaviorInitialize(mission);
        }
    }
}