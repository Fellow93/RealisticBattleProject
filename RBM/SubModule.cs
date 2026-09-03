using HarmonyLib;
using RBM.AgentStatusBar;
using RBMAI;
using RBMCombat;
using RBMCampaign;
using RBMTournament;
using System;
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
        public static Harmony rbmcampaignHarmony = new Harmony("com.rbmcampaign");
        public static Harmony rbmHarmony = new Harmony("com.rbmmain");
    }

    public class SubModule : MBSubModuleBase
    {
        public static string ModuleId = "RBM";

        public static void ApplyHarmonyPatches()
        {
            RBMAiPatcher.patched = false;
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
                RBMCombatPatcher.DoPatching(ref HarmonyModules.rbmcombatHarmony);
            }
            else
            {
                HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
            }
            if (RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                RBMCampaignPatcher.DoPatching(ref HarmonyModules.rbmcampaignHarmony);
            }
            else
            {
                HarmonyModules.rbmcampaignHarmony.UnpatchAll(HarmonyModules.rbmcampaignHarmony.Id);
            }
        }

        public static void UnpatchAllRBM()
        {
            //RBMAiPatcher.patched = false;
            HarmonyModules.rbmHarmony.UnpatchAll(HarmonyModules.rbmHarmony.Id);
            HarmonyModules.rbmtHarmony.UnpatchAll(HarmonyModules.rbmtHarmony.Id);
            HarmonyModules.rbmaiHarmony.UnpatchAll(HarmonyModules.rbmaiHarmony.Id);
            HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
            HarmonyModules.rbmcampaignHarmony.UnpatchAll(HarmonyModules.rbmcampaignHarmony.Id);
        }

        protected override void OnSubModuleLoad()
        {
            RBMConfig.RBMConfig.LoadConfig();
            CustomBattlePreset.LoadPreset();

            // Gauntlet parses and caches the party screen prefab before OnGameStart runs, so this
            // one hook cannot wait for ApplyHarmonyPatches like the rest of RBMCampaign does.
            if (RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                SpoilsBarPrefabPatch.ApplyEarly(HarmonyModules.rbmcampaignHarmony);
                // Same story for the inventory screen: its prefabs are parsed and cached long before
                // OnGameStart, so the weight column has to be injected at module load or not at all.
                ItemWeightPrefabPatch.ApplyEarly(HarmonyModules.rbmcampaignHarmony);
                // The maintenance line under the party screen's selected-troop wage, injected the same
                // way and for the same reason as the spoils bar above.
                MaintenanceLabelPrefabPatch.ApplyEarly(HarmonyModules.rbmcampaignHarmony);
                // The per-party upgrade-budget slider + checkbox beside the clan Parties panel's wage cap,
                // injected the same way -- the clan screen's prefabs are likewise cached before OnGameStart.
                UpgradeLimitPrefabPatch.ApplyEarly(HarmonyModules.rbmcampaignHarmony);
                // Grows the map Escape-menu panel so the added RBM Ledger row does not overflow it; same
                // cached-before-OnGameStart reason as the injections above.
                RBMEscapeMenuPrefabPatch.ApplyEarly(HarmonyModules.rbmcampaignHarmony);
                // Lets the smithy refine rows shrink-wrap and centre their material cluster so the added silver
                // tile on the Thamaskene row does not overflow; same cached-before-OnGameStart reason.
                RefineRowLayoutPrefabPatch.ApplyEarly(HarmonyModules.rbmcampaignHarmony);
                // Shows all three rows of the town-management Projects grid (War Sails' shipyard is the 13th
                // tile and sat scrolled out of view); same cached-before-OnGameStart reason.
                ProjectsGridPrefabPatch.ApplyEarly(HarmonyModules.rbmcampaignHarmony);
                // Scales the project tile itself (DevelopmentItem.xml) to match the shrunken grid cells set
                // above, so two full rows of building icons always fit with slack.
                TownManagementGridPatch.ApplyEarly(HarmonyModules.rbmcampaignHarmony);
            }

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
                if (RBMConfig.RBMConfig.rbmCampaignEnabled && Campaign.Current != null)
                {
                    LordSwitcher.CheckHotkey();
                    RBMLedgerHotkey.CheckHotkey();
                }
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
            ApplyHarmonyPatches();
            base.RegisterSubModuleTypes();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            RBMConfig.RBMConfig.LoadConfig();
            ApplyHarmonyPatches();
            if (RBMConfig.RBMConfig.rbmCampaignEnabled && game.GameType is Campaign)
            {
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMSpoilsCampaignBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMTroopUpkeepCampaignBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMSimulationCampaignBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMSpectateCampaignBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMEconomyCampaignBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMSettlementWealthCampaignBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMCaravanBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMVillageLedgerCampaignBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMTownLedgerCampaignBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMGarrisonRefillBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMRecruitBiasBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMSettlementDefenseBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMDeserterRaiderBehavior());
                // Registered last so it wins GetGameModel and receives whatever workshop model was
                // already in place (vanilla's, or NavalDLC's) as its BaseModel to delegate to.
                ((CampaignGameStarter)gameStarterObject).AddModel(new RBMCampaign.RBMWorkshopModel());
            }
            base.OnGameStart(game, gameStarterObject);
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            var isWSActive = ModuleHelper.IsModuleActive("NavalDLC");
            var isRBMActive = ModuleHelper.IsModuleActive("RBM");
            var isRBMWSActive = ModuleHelper.IsModuleActive("RBM_WS");
            if (isWSActive && isRBMActive && !isRBMWSActive)
            {
                InformationManager.ShowInquiry(new InquiryData("RBM War Sails submodule is missing!", "RBM War Sails submod is required when using both RBM and the War Sails DLC. Please install and enable the RBM War Sails submod to avoid potential issues, like Nords having no weapons etc.", true, false, "OK", "OK", null, null), false, true);
            }
            // Where TaleWorlds register theirs, and for the same reason: the tooltip registry has to know what draws
            // an RBMPowerTooltipData before the first hover can ask for one.
            RBMCampaign.RBMPowerTooltipVM.Register();
            ApplyHarmonyPatches();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            mission.AddMissionBehavior(new UnitStatusMissionView());
            if (RBMConfig.RBMConfig.hitStopEnabled)
            {
                mission.AddMissionBehavior((MissionBehavior)(object)new RBMAI.HitStopLogic());
            }
            Game.Current.GameTextManager.LoadGameTexts();
            if (RBMConfig.RBMConfig.developerMode)
            {
                mission.AddMissionBehavior((MissionBehavior)(object)new BattleStatsLogic());
            }
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                if (RBMConfig.RBMConfig.armorStatusUIEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new PlayerArmorStatus());
                }
            }
            if (RBMConfig.RBMConfig.battleHitLoggingEnabled)
            {
                mission.AddMissionBehavior((MissionBehavior)(object)new BattleHitLogic());
            }
            if (RBMConfig.RBMConfig.rbmAiEnabled)
            {
                mission.AddMissionBehavior((MissionBehavior)(object)new AgentPanicFix());
                mission.AddMissionBehavior((MissionBehavior)(object)new RBMAIPatchLogic());
                if (RBMConfig.RBMConfig.postureEnabled && RBMConfig.RBMConfig.postureGUIEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new StanceVisualLogic());
                }
                mission.AddMissionBehavior((MissionBehavior)(object)new SiegeArcherPoints());
                if (RBMConfig.RBMConfig.postureEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new StanceLogic());
                }
            }
            else
            {
                if (mission.GetMissionBehavior<SiegeArcherPoints>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<SiegeArcherPoints>());
                }
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

        /// <summary>
        /// Runs from Game.InitializeDefaultGameObjects, after the default item categories are built
        /// and before DefaultItems, the Items XML and the WorkshopTypes XML -- the one point at which
        /// RBM's own categories can be added and still be seen by everything that reads them.
        /// </summary>
        public override void InitializeSubModuleGameObjects(Game game)
        {
            base.InitializeSubModuleGameObjects(game);
            TradeGoodCategories.Register(game);
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