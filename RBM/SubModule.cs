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
            // Must run here: the engine reads project.mbproj right after OnSubModuleLoad, and only once.
            CombatParameterGate.Apply();

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

            // "RBM Changelog" badge in the title screen's top-right corner (not a menu option); it adds its layers
            // to every initial screen as it is pushed.
            RBMConfig.RBMChangelog.Install();
        }

        protected override void OnApplicationTick(float dt)
        {
            // Opens/closes the title-screen changelog viewer; returns at once when no title screen is up.
            RBMConfig.RBMChangelog.Tick();
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
                // MapEvent.PlayerMapEvent dereferences Campaign.Current, which is null outside the campaign
                // (custom battle); guard it so this tick does not throw-and-swallow every frame.
                bool isHideout = Campaign.Current != null && MapEvent.PlayerMapEvent != null && MapEvent.PlayerMapEvent.IsHideoutBattle;
                if (ScreenManager.TopScreen != null && (Mission.Current.IsFieldBattle || Mission.Current.IsSiegeBattle || Mission.Current.IsNavalBattle || Mission.Current.SceneName.Contains("arena") || isHideout))
                {
                    MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
                    // MissionScreen.InputManager is `Mission.InputManager`: it throws while the screen has no
                    // Mission (setup/teardown frames where Mission.Current is already/still set).
                    if (missionScreen != null && missionScreen.Mission != null && missionScreen.InputManager != null && missionScreen.InputManager.IsControlDown())
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
            // RBM's native parameters load with the module regardless of the Combat toggle; revert them here.
            NativeParameterGate.Apply();
            // Crash guard for saves carrying uninitialized troops (see SaveRosterRepairBehavior); deliberately
            // outside the campaign toggle, since the corruption comes from other mods, not RBMCampaign.
            if (game.GameType is Campaign)
            {
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new SaveRosterRepairBehavior());
            }
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
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMClanFinanceLedgerCampaignBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMGarrisonRefillBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMRecruitBiasBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMSettlementDefenseBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMDeserterRaiderBehavior());
                ((CampaignGameStarter)gameStarterObject).AddBehavior(new RBMRecruitPoolCampaignBehavior());
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
            // With Combat off RBM changes no items or troops, so War Sails needs nothing from RBM_WS (its combat XML
            // and XSLT are skipped; only the flavour Nord patrols remain). Only nag when it matters.
            if (isWSActive && isRBMActive && !isRBMWSActive && RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                InformationManager.ShowInquiry(new InquiryData("RBM War Sails submodule is missing!", "RBM War Sails submod is required when using both RBM and the War Sails DLC. Please install and enable the RBM War Sails submod to avoid potential issues, like Nords having no weapons etc.", true, false, "OK", "OK", null, null), false, true);
            }
            // Where TaleWorlds register theirs, and for the same reason: the tooltip registry has to know what draws
            // an RBMPowerTooltipData before the first hover can ask for one.
            if (RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                RBMCampaign.RBMPowerTooltipVM.Register();
            }
            ApplyHarmonyPatches();
        }

        public override void OnGameEnd(Game game)
        {
            // Drop the previous campaign's troop/perk objects so nothing stale survives into the next game.
            RBMConfig.TroopPerks.Clear();
            // Flush and release every buffered campaign log file, so a finished session's logs are complete on
            // disk before the next campaign starts or loads (and before the player quits from the main menu).
            BufferedLogWriter.CloseAll();
            base.OnGameEnd(game);
        }

        protected override void OnSubModuleUnloaded()
        {
            // Normal exit: write out whatever the campaign logs still hold. BufferedLogWriter also hooks
            // ProcessExit as a fallback for an exit that skips this.
            BufferedLogWriter.CloseAll();
            base.OnSubModuleUnloaded();
        }

        public override void OnBeforeMissionBehaviorInitialize(Mission mission)
        {
            // Unconditional: turning the AI module off mid-session must still release the previous mission's
            // formations, or Formation._simulationFormationTemp outlives its scene.
            MissionStartReset.Reset();
            // Likewise unconditional: a mission that ended without OnEndMission must not leave the troop perk
            // recorder running (and holding its entries) into this one. TroopPerkLogLogic restarts it if it is on.
            TroopPerkLog.Reset();
            base.OnBeforeMissionBehaviorInitialize(mission);
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            mission.AddMissionBehavior(new UnitStatusMissionView());
            if (RBMConfig.RBMConfig.rbmAiEnabled && RBMConfig.RBMConfig.hitStopEnabled)
            {
                mission.AddMissionBehavior((MissionBehavior)(object)new RBMAI.HitStopLogic());
            }
            Game.Current.GameTextManager.LoadGameTexts();
            if (RBMConfig.RBMConfig.rbmAiEnabled && RBMConfig.RBMConfig.developerMode)
            {
                mission.AddMissionBehavior((MissionBehavior)(object)new BattleStatsLogic());
            }
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                // Prototype mordhau usage: hand moved up the blade + Blunt swing, before any agent is built (each agent
                // hands the usage frame to the engine when it equips). Per mission so a sword crafted mid-campaign gets
                // it too; idempotent. Also run at OnGameInitializationFinished for tooltips.
                MordhauGrip.Apply();
                if (RBMConfig.RBMConfig.armorStatusUIEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new PlayerArmorStatus());
                }
                // Stuck missiles falling out; the missile collision patch registers them with this logic. Idle (and
                // never registered with) when both of its settings are 0.
                mission.AddMissionBehavior((MissionBehavior)(object)new RangedRework.StuckMissileLogic());
                if (RBMConfig.RBMConfig.battleHitLoggingEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new BattleHitLogic());
                }
                // Which listed troop perks the game actually asks about (logs/troopperks). Only meaningful when troop
                // perks apply at all; without this logic the GetPerkValue postfixes record nothing.
                if (RBMConfig.RBMConfig.troopPerksEnabled && RBMConfig.RBMConfig.troopPerkLoggingEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new TroopPerkLogLogic());
                }
                // The view also turns on the aim camera (its own Harmony instance), which frames the view's prediction.
                if (RBMConfig.RBMConfig.rangedAimArcEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new RangedAimArcView());
                }
            }
            if (RBMConfig.RBMConfig.rbmAiEnabled)
            {
                if (RBMConfig.RBMConfig.aiBehaviorLogEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new RBMAI.AiBehaviorLogic());
                }
                mission.AddMissionBehavior((MissionBehavior)(object)new AgentPanicFix());
                mission.AddMissionBehavior((MissionBehavior)(object)new RBMAIPatchLogic());
                if (RBMConfig.RBMConfig.postureEnabled && RBMConfig.RBMConfig.postureGUIEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new StanceVisualLogic());
                }
                if (RBMConfig.RBMConfig.frontlineEnabled)
                {
                    // Inert until toggled in-mission with Ctrl+Shift+F.
                    mission.AddMissionBehavior((MissionBehavior)(object)new FrontlineDebugOverlay());
                }
                mission.AddMissionBehavior((MissionBehavior)(object)new SiegeArcherPoints());
                // Always present: the Harmony posture patches self-gate on postureEnabled at runtime,
                // so toggling posture on mid-mission would otherwise drain posture with no StanceLogic
                // to regenerate it. StanceLogic's own tick and handlers check postureEnabled.
                mission.AddMissionBehavior((MissionBehavior)(object)new StanceLogic());
                // Inert until a first-person posture tiredness; needs no posture check of its own.
                mission.AddMissionBehavior((MissionBehavior)(object)new PlayerExhaustionLogic());
            }
            else
            {
                if (mission.GetMissionBehavior<FrontlineDebugOverlay>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<FrontlineDebugOverlay>());
                }
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
                if (mission.GetMissionBehavior<PlayerExhaustionLogic>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<PlayerExhaustionLogic>());
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
            // Gated with the rest of RBMCampaign: the WorkshopTypes XML whose recipes name these
            // categories (RBMEconomy_workshops_artisans.xml) now carries RBM_CAMPAIGN_XML_TAG, so
            // MergeTwoXmlsPatch skips it when the module is off and nothing asks for the categories.
            if (RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                TradeGoodCategories.Register(game);
            }
        }

        /// <summary>
        /// Drops culture-less clans (left by broken XML) so campaign ticks never see them. Mirrors
        /// vanilla's own invalid-clan cleanup in Clan.OnLoad: DestroyClanAction, then
        /// CampaignObjectManager.RemoveClan (internal), which takes the clan out of both Clans and
        /// Factions -- removing it from Clans alone left it in Factions for any faction loop to hit.
        /// Already-eliminated clans are not destroyed again, which would re-fire OnClanDestroyed.
        /// </summary>
        public override void OnGameInitializationFinished(Game game)
        {
            // Troop perks (RBMConfig.TroopPerks): resolved here because this runs for a new campaign and a loaded
            // save alike, after every troop and perk object exists. Rebuilt per campaign and only read through
            // TroopPerks.IsActive (rbmCombatEnabled + troopPerksEnabled), so it is loaded whatever the toggles are and
            // a toggle flipped mid-game takes effect without a reload. Plain data; no patch or behaviour is added.
            if (game.GameType is Campaign)
            {
                RBMConfig.TroopPerks.Load();
            }
            else
            {
                RBMConfig.TroopPerks.Clear();
            }
            // Prototype mordhau usage (RBMCombat MordhauGrip): every item exists by now, in a custom battle, a new
            // campaign and a loaded save alike, so inventory tooltips already show the Blunt swing. Missions apply it again.
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                MordhauGrip.Apply();
            }
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
                    if (!clan.IsEliminated)
                    {
                        DestroyClanAction.Apply(clan);
                    }
                    AccessTools.Method(typeof(CampaignObjectManager), "RemoveClan")
                        .Invoke(Campaign.Current.CampaignObjectManager, new object[] { clan });
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