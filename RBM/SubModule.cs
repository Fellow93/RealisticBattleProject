using HarmonyLib;
using RBM.AgentStatusBar;
using RBMAI;
using RBMCombat;
using RBMTournament;
using System;
using System.IO;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
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

        private static string ResolveInstalledModuleId()
        {
            try
            {
                string assemblyFile = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string binClient = Path.GetDirectoryName(assemblyFile);
                string bin = Directory.GetParent(binClient)?.FullName;
                string moduleRoot = Directory.GetParent(bin)?.FullName;
                if (!string.IsNullOrEmpty(moduleRoot))
                {
                    string folderName = Path.GetFileName(moduleRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrEmpty(folderName))
                    {
                        return folderName;
                    }
                }
            }
            catch
            {
            }
            return "RBM";
        }

        public static void ApplyHarmonyPatches()
        {
            RBMAiPatcher.patched = false;
            UnpatchAllRBM();
            HarmonyModules.rbmHarmony.PatchAll();
            RBMConfig.SelectiveDebug.Log("PATCH", "Applying RBM Compat CN patches.");
            if (RBMConfig.RBMConfig.rbmTournamentEnabled)
            {
                RBMTournamentPatcher.DoPatching(ref HarmonyModules.rbmtHarmony);
                RBMConfig.SelectiveDebug.Log("PATCH", "Tournament reward patches enabled.");
            }
            else
            {
                HarmonyModules.rbmtHarmony.UnpatchAll(HarmonyModules.rbmtHarmony.Id);
                RBMConfig.SelectiveDebug.Log("PATCH", "Tournament reward patches disabled.");
            }
            if (RBMConfig.RBMConfig.rbmAiEnabled)
            {
                RBMAiPatcher.DoStanceOnlyPatching();
                RBMConfig.SelectiveDebug.Log("PATCH", "Posture/stamina patches enabled; battlefield AI tactics disabled.");
            }
            else
            {
                HarmonyModules.rbmaiHarmony.UnpatchAll(HarmonyModules.rbmaiHarmony.Id);
                RBMConfig.SelectiveDebug.Log("PATCH", "Posture/stamina patches disabled.");
            }
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                RBMCombatPatcher.DoPatching(ref HarmonyModules.rbmcombatHarmony);
                RBMConfig.SelectiveDebug.Log("PATCH", "Combat patches enabled; bow/crossbow fire-rate changes disabled by compat guards.");
            }
            else
            {
                HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
                RBMConfig.SelectiveDebug.Log("PATCH", "Combat patches disabled.");
            }
        }

        public static void UnpatchAllRBM()
        {
            //RBMAiPatcher.patched = false;
            HarmonyModules.rbmHarmony.UnpatchAll(HarmonyModules.rbmHarmony.Id);
            HarmonyModules.rbmtHarmony.UnpatchAll(HarmonyModules.rbmtHarmony.Id);
            HarmonyModules.rbmaiHarmony.UnpatchAll(HarmonyModules.rbmaiHarmony.Id);
            HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
        }

        protected override void OnSubModuleLoad()
        {
            ModuleId = ResolveInstalledModuleId();
            RBMConfig.RBMConfig.LoadConfig();
            RBMConfig.SelectiveDebug.Log("LIFECYCLE", "SubModule loaded from " + ModuleId + ".");
            CustomBattlePreset.LoadPreset();

            Module.CurrentModule.AddInitialStateOption(new InitialStateOption("RbmConfiguration", new TextObject("{=RBM_CON_020}RBM Configuration"), 9999, delegate
            {
                ScreenManager.PushScreen(new RBMConfig.RBMConfigScreen());
            }, () => (false, new TextObject("{=RBM_CON_020}RBM Configuration"))));
        }

        protected override void OnApplicationTick(float dt)
        {
            CustomBattlePatches.TickInput();
            if (Mission.Current == null)
            {
                return;
            }
            try
            {
                if (ScreenManager.TopScreen != null && (Mission.Current.IsFieldBattle || Mission.Current.IsSiegeBattle || Mission.Current.IsNavalBattle || Mission.Current.SceneName.Contains("arena") || (MapEvent.PlayerMapEvent != null && MapEvent.PlayerMapEvent.IsHideoutBattle)))
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

        protected override void RegisterSubModuleTypes()
        {
            RBMConfig.RBMConfig.LoadConfig();
            RBMConfig.SelectiveDebug.Log("LIFECYCLE", "RegisterSubModuleTypes.");
            ApplyHarmonyPatches();
            base.RegisterSubModuleTypes();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            RBMConfig.RBMConfig.LoadConfig();
            RBMConfig.SelectiveDebug.Log("LIFECYCLE", "OnGameStart.");
            ApplyHarmonyPatches();
            base.OnGameStart(game, gameStarterObject);
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            var isWSActive = ModuleHelper.IsModuleActive("NavalDLC");
            var isRBMActive = ModuleHelper.IsModuleActive("RBM") || ModuleHelper.IsModuleActive("RBM_CompatCN");
            var isRBMWSActive = ModuleHelper.IsModuleActive("RBM_WS") || ModuleHelper.IsModuleActive("RBM_WS_CompatCN");
            if (isWSActive && isRBMActive && !isRBMWSActive)
            {
                InformationManager.ShowInquiry(new InquiryData("RBM War Sails submodule is missing!", "RBM War Sails submod is required when using both RBM and the War Sails DLC. Please install and enable the RBM War Sails submod to avoid potential issues, like Nords having no weapons etc.", true, false, "OK", "OK", null, null), false, true);
            }
            ApplyHarmonyPatches();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            if (mission.GetMissionBehavior<UnitStatusMissionView>() == null)
            {
                mission.AddMissionBehavior(new UnitStatusMissionView());
            }
            if (RBMConfig.RBMConfig.hitStopEnabled)
            {
                if (mission.GetMissionBehavior<RBMAI.HitStopLogic>() == null)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new RBMAI.HitStopLogic());
                }
            }
            Game.Current.GameTextManager.LoadGameTexts();
            RBMConfig.SelectiveDebug.Log("MISSION", "Developer logging is " + (RBMConfig.RBMConfig.developerMode ? "enabled" : "disabled") + ".");
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                if (RBMConfig.RBMConfig.armorStatusUIEnabled && mission.GetMissionBehavior<PlayerArmorStatus>() == null)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new PlayerArmorStatus());
                }
            }
            if (RBMConfig.RBMConfig.rbmAiEnabled)
            {
                if (RBMConfig.RBMConfig.postureEnabled && RBMConfig.RBMConfig.postureGUIEnabled)
                {
                    if (mission.GetMissionBehavior<StanceVisualLogic>() == null)
                    {
                        mission.AddMissionBehavior((MissionBehavior)(object)new StanceVisualLogic());
                    }
                }
                if (RBMConfig.RBMConfig.postureEnabled)
                {
                    if (mission.GetMissionBehavior<StanceLogic>() == null)
                    {
                        mission.AddMissionBehavior((MissionBehavior)(object)new StanceLogic());
                    }
                }
                RBMConfig.SelectiveDebug.Log("MISSION", "Posture/stamina mission behaviors initialized for scene " + mission.SceneName + ".");
            }
            else
            {
                if (mission.GetMissionBehavior<StanceVisualLogic>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<StanceVisualLogic>());
                }
                if (mission.GetMissionBehavior<StanceLogic>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<StanceLogic>());
                }
            }
            base.OnMissionBehaviorInitialize(mission);
        }

        public override void OnGameInitializationFinished(Game game)
        {
            if (Campaign.Current != null && Campaign.Current.Clans != null)
            {
                MBList<Clan> clansToRemove = new MBList<Clan>();
                foreach (var clan in Campaign.Current.Clans)
                {
                    if (clan.Culture == null)
                    {
                        clansToRemove.Add(clan);
                    }
                }
                foreach (var clan in clansToRemove)
                {
                    DestroyClanAction.Apply(clan);
                    Campaign.Current.Clans.Remove(clan);
                }
            }
        }
    }

    public class RBMAIPatchLogic : MissionLogic
    {
        public override void EarlyStart()
        {
            RBMAiPatcher.DoPatching();
        }
    }
}