using HarmonyLib;
//using RBMAI;
using RBMCombat;
using RBMTournament;
using System;
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
        //public static Harmony rbmaiHarmony = new Harmony("com.rbmai");
        public static Harmony rbmtHarmony = new Harmony("com.rbmt");
        public static Harmony rbmcombatHarmony = new Harmony("com.rbmcombat");
        public static Harmony rbmHarmony = new Harmony("com.rbmmain");
    }

    public class SubModule : MBSubModuleBase
    {
        public static string ModuleId = "RBM";

        public static void ApplyHarmonyPatches()
        {
            UnpatchAllRBM();
            HarmonyModules.rbmHarmony.PatchAll();
            if (RBMConfig.RBMConfig.rbmTournamentEnabled)
            {
                RBMTournamentPatcher.DoPatching(ref HarmonyModules.rbmtHarmony);
            }
            else
            {
                HarmonyModules.rbmtHarmony.UnpatchAll(HarmonyModules.rbmtHarmony.Id);
            }
            //if (RBMConfig.RBMConfig.rbmAiEnabled)
            //{
            //    RBMAiPatcher.FirstPatch(ref HarmonyModules.rbmaiHarmony);
            //}
            //else
            //{
            //    HarmonyModules.rbmaiHarmony.UnpatchAll(HarmonyModules.rbmaiHarmony.Id);
            //}
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                RBMCombatPatcher.DoPatching(ref HarmonyModules.rbmcombatHarmony);
            }
            else
            {
                HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
            }
        }

        public static void UnpatchAllRBM()
        {
            //RBMAiPatcher.patched = false;
            HarmonyModules.rbmHarmony.UnpatchAll(HarmonyModules.rbmHarmony.Id);
            HarmonyModules.rbmtHarmony.UnpatchAll(HarmonyModules.rbmtHarmony.Id);
            //HarmonyModules.rbmaiHarmony.UnpatchAll(HarmonyModules.rbmaiHarmony.Id);
            HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
        }

        protected override void OnSubModuleLoad()
        {
            RBMConfig.RBMConfig.LoadConfig();

            TaleWorlds.MountAndBlade.Module.CurrentModule.AddInitialStateOption(new InitialStateOption("RbmConfiguration", new TextObject("{=RBM_CON_020}RBM Configuration"), 9999, delegate
            {
                ScreenManager.PushScreen(new RBMConfig.RBMConfigScreen());
            }, () => (false, new TextObject("{=RBM_CON_020}RBM Configuration"))));
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
                    if (missionScreen != null && missionScreen.InputManager != null && missionScreen.InputManager.IsControlDown())
                    {
                        if (missionScreen.InputManager.IsKeyPressed(InputKey.V))
                        {
                            Mission.Current.SetFastForwardingFromUI(!Mission.Current.IsFastForward);
                            InformationManager.DisplayMessage(new InformationMessage("Vroom = " + Mission.Current.IsFastForward, Color.FromUint(4282569842u)));
                        }
                        //if (missionScreen.InputManager.IsKeyPressed(InputKey.Numpad2))
                        //{
                        //    Frontline.normalCommand = !Frontline.normalCommand;
                        //    Frontline.aggressiveCommand = !Frontline.normalCommand;
                        //    Frontline.defensiveCommand = !Frontline.normalCommand;
                        //    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=8UBfIenN}Normal").ToString(), Color.FromUint(4282569842u)));
                        //}
                        //if (missionScreen.InputManager.IsKeyPressed(InputKey.Numpad1))
                        //{
                        //    Frontline.aggressiveCommand = !Frontline.aggressiveCommand;
                        //    Frontline.normalCommand = !Frontline.aggressiveCommand;
                        //    Frontline.defensiveCommand = !Frontline.aggressiveCommand;

                        //    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=4Hdcxk0a}Aggressive").ToString(), Color.FromUint(4282569842u)));
                        //}
                        //if (missionScreen.InputManager.IsKeyPressed(InputKey.Numpad3))
                        //{
                        //    Frontline.defensiveCommand = !Frontline.defensiveCommand;
                        //    Frontline.normalCommand = !Frontline.defensiveCommand;
                        //    Frontline.aggressiveCommand = !Frontline.defensiveCommand;
                        //    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=A3T5z4Mv}Defensive").ToString(), Color.FromUint(4282569842u)));
                        //}
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
            //if (RBMConfig.RBMConfig.developerMode)
            //{
            //    mission.AddMissionBehavior((MissionBehavior)(object)new BattleStatsLogic());
            //}
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                if (RBMConfig.RBMConfig.armorStatusUIEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new PlayerArmorStatus());
                }
            }
            //if (RBMConfig.RBMConfig.rbmAiEnabled)
            //{
            //    if (RBMConfig.RBMConfig.postureEnabled && RBMConfig.RBMConfig.postureGUIEnabled)
            //    {
            //        mission.AddMissionBehavior((MissionBehavior)(object)new PostureVisualLogic());
            //    }
            //    mission.AddMissionBehavior((MissionBehavior)(object)new SiegeArcherPoints());
            //    mission.AddMissionBehavior((MissionBehavior)(object)new PostureLogic());
            //}
            //else
            //{
            //    if (mission.GetMissionBehavior<SiegeArcherPoints>() != null)
            //    {
            //        mission.RemoveMissionBehavior(mission.GetMissionBehavior<SiegeArcherPoints>());
            //    }
            //    if (mission.GetMissionBehavior<PostureVisualLogic>() != null)
            //    {
            //        mission.RemoveMissionBehavior(mission.GetMissionBehavior<PostureVisualLogic>());
            //    }
            //    if (mission.GetMissionBehavior<PostureLogic>() != null)
            //    {
            //        mission.RemoveMissionBehavior(mission.GetMissionBehavior<PostureLogic>());
            //    }
            //}
            base.OnMissionBehaviorInitialize(mission);
        }
    }
}