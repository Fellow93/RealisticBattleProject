using System;
using System.Collections.Generic;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.CampaignSystem.TroopSuppliers;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;
using TaleWorlds.MountAndBlade.Missions.Handlers;
using TaleWorlds.MountAndBlade.Source.Missions;
using TaleWorlds.MountAndBlade.Source.Missions.Handlers.Logic;

namespace RBMCampaign
{
    /// <summary>
    /// A battle between two AI lords, fought in real time, with the player nowhere in it.
    ///
    /// This is SandBoxMissions.OpenBattleMission with the player cut out of it. Vanilla's version is written on the
    /// assumption that the player is a party to the fight: it reads the main party's map event, its army, its side,
    /// its heroes, and hands an agent to half a dozen behaviours that go on to dereference it. None of that is true
    /// here. The mission is opened FOR a map event the player has no share in, and there is no player agent at all --
    /// not a possessed trooper, not a ghost, nothing. RTSCamera's free camera is the only way to see it, which is why
    /// it is a hard requirement.
    ///
    /// The watched battle is NOT the campaign's battle. It is a copy, spun up beside the real one, and the real
    /// MapEvent goes on auto-resolving on its own clock and reaching its own verdict regardless of what happens on
    /// this field. Nothing done here is written back. That is deliberate: the point is to watch RBM's field AI fight
    /// the same muster its auto-resolve is being asked to score, and a tool that changed the thing it measures is
    /// worth nothing.
    ///
    /// Three things carry the whole design, and each is one NullReferenceException away from not working:
    ///
    ///   The mission is still called "Battle". The name is not decoration -- it is what binds the view list, and that
    ///   list is where the free spectate-target cycling and the scoreboard come from.
    ///
    ///   PlayerTeam is never null. It is the watched side. BattleEndLogic dereferences it without a guard in four
    ///   places, so "no player, so no player team" is not an option; instead the player team is the side we watch,
    ///   with no player on it.
    ///
    ///   And that team's formations must be AI-driven. A Team is born believing IsPlayerGeneral, and a team that
    ///   believes it has a general waits for his orders -- forever, since he does not exist. The one thing that tells
    ///   it otherwise is AssignPlayerRoleInTeamMissionController, which is why a controller named for the player is
    ///   in a mission with no player in it.
    /// </summary>
    public static class RBMSpectatorMission
    {
        private static MapEvent _watchedMapEvent;
        private static BattleSideEnum _watchedSide;

        private static Mission _cachedMission;
        private static bool _cachedIsSpectator;

        /// <summary>
        /// The battle being watched, or null when no spectated mission is running.
        /// </summary>
        public static MapEvent WatchedMapEvent
        {
            get { return _watchedMapEvent; }
        }

        /// <summary>
        /// Whose lines we are watching it from. Only meaningful while <see cref="IsSpectating"/> is true.
        /// </summary>
        public static BattleSideEnum WatchedSide
        {
            get { return _watchedSide; }
        }

        /// <summary>
        /// Whether the mission running right now is ours.
        ///
        /// This is the gate every view patch hangs on, so it has to be exact in both directions: a false positive
        /// rewrites a real battle the player is fighting, and a false negative puts the crash back. It is deliberately
        /// NOT "is PlayerMapEvent null" -- that is true of missions we have nothing to do with -- and deliberately not
        /// a bare static flag either, because a flag that leaks (an exception between the set and the mission, a
        /// mission that never opens) would follow the player into his own battles and quietly answer for them.
        ///
        /// Instead the answer is read off the live mission: it is ours only if the mission standing right now contains
        /// the marker behaviour that only we ever add. That is self-clearing by construction -- a stale _watchedMapEvent
        /// is harmless, because the next mission does not carry our behaviour and the gate shuts on its own. The
        /// per-mission cache is there because this is asked from a property getter that vanilla calls freely.
        ///
        /// The marker is <see cref="RBMSpectatorMarker"/>, carried by BOTH the field-battle and the siege spectator
        /// missions. It used to key on RBMSpectatorDeploymentFinisher, which the siege mission does not carry (the
        /// siege keeps a real deployment controller), so the gate was generalised onto a dedicated marker instead.
        /// </summary>
        public static bool IsSpectating
        {
            get
            {
                if (_watchedMapEvent == null)
                {
                    return false;
                }

                Mission mission = Mission.Current;
                if (mission == null)
                {
                    return false;
                }

                if (!ReferenceEquals(mission, _cachedMission))
                {
                    _cachedMission = mission;
                    _cachedIsSpectator = mission.GetMissionBehavior<RBMSpectatorMarker>() != null;
                }
                return _cachedIsSpectator;
            }
        }

        internal static void EndWatching()
        {
            _watchedMapEvent = null;
            _cachedMission = null;
            _cachedIsSpectator = false;
        }

        public static Mission OpenSpectatorBattleMission(MapEvent mapEvent, BattleSideEnum watchSide)
        {
            MissionInitializerRecord rec = BuildRecord(mapEvent);

            // Before OpenNew, not after: the mission ticks views while it is still loading -- MissionPreloadView
            // reaches for the map event on the first pre-tick -- and OpenNew does not return until well past that.
            // A watched event set on the next line would already be too late.
            _watchedMapEvent = mapEvent;
            _watchedSide = watchSide;
            _cachedMission = null;
            _cachedIsSpectator = false;

            return MissionState.OpenNew("Battle", rec, delegate
            {
                Hero attackerLeader = mapEvent.AttackerSide.LeaderParty.LeaderHero;
                Hero defenderLeader = mapEvent.DefenderSide.LeaderParty.LeaderHero;

                return new MissionBehavior[]
                {
                    // Vanilla builds this through a factory that sources the event from MapEvent.PlayerMapEvent and
                    // the side from PartyBase.MainParty.Side -- both null or meaningless here. Same two suppliers,
                    // same order (defender first, as the factory has it: the array is indexed by BattleSideEnum and
                    // Defender is 0), sourced from the battle we were handed instead.
                    new DefaultBattleMissionAgentSpawnLogic(
                        new IMissionTroopSupplier[]
                        {
                            new PartyGroupTroopSupplier(mapEvent, BattleSideEnum.Defender, null, null),
                            new PartyGroupTroopSupplier(mapEvent, BattleSideEnum.Attacker, null, null)
                        },
                        watchSide,
                        Mission.BattleSizeType.Battle),

                    new BattlePowerCalculationLogic(),
                    new BattleSpawnLogic("battle_set"),
                    new RBMSpectatorSpawnHandler(mapEvent),
                    new CampaignMissionComponent(),

                    // BattleAgentLogic is DROPPED. Two reasons, either sufficient. It reaches through
                    // MapEvent.PlayerMapEvent for the troop upgrade tracker on initialise, and that is null.
                    // And it is the thing that pays out experience and records casualties -- which is precisely what
                    // a non-canonical battle must not do. Nobody dies here in any sense the campaign can see.

                    new MountAgentLogic(),
                    new BannerBearerLogic(),
                    new MissionOptionsComponent(),
                    new BattleEndLogic(),
                    new BattleReinforcementsSpawnController(),

                    // The second argument is the load-bearing one: it becomes Mission.PlayerTeam. It cannot be
                    // PartyBase.MainParty (the player is not in this battle and MissionCombatantsLogic would put his
                    // team on a side he has no parties on), and it cannot be null (BattleEndLogic derefs PlayerTeam
                    // unguarded). So it is the leading party of whichever side we chose to watch: a real combatant,
                    // on a real side, with no player attached.
                    //
                    // isPlayerSergeant must stay false. True routes the ally-team decision through this combatant's
                    // General, which is a road we have no reason to walk.
                    new MissionCombatantsLogic(
                        mapEvent.InvolvedParties,
                        mapEvent.GetLeaderParty(watchSide),
                        mapEvent.GetLeaderParty(BattleSideEnum.Defender),
                        mapEvent.GetLeaderParty(BattleSideEnum.Attacker),
                        Mission.MissionTeamAITypeEnum.FieldBattle,
                        false),

                    new BattleObserverMissionLogic(),
                    new AgentHumanAILogic(),
                    new AgentVictoryLogic(),
                    new BattleSurgeonLogic(),
                    new MissionAgentPanicHandler(),
                    new BattleMissionAgentInteractionLogic(),
                    new AgentMoraleInteractionLogic(),

                    // Not player housekeeping -- the ignition. Its AfterStart calls PlayerTeam.SetPlayerRole(false,
                    // false), and that is the ONLY thing in the mission that tells the watched team it has no general
                    // to wait on. A Team is constructed with IsPlayerGeneral true, and AddTeamAI hands its formations
                    // to the AI only when the team is not the player's or the player is not its general. Drop this
                    // and the watched side stands in its start positions until the timer runs out.
                    new AssignPlayerRoleInTeamMissionController(false, false, false, null),

                    // Vanilla reads these two names off MapEvent.PlayerMapEvent, which would fault before the
                    // constructor was even reached. Same names, off the battle we were given.
                    new RBMSpectatorGeneralsAndCaptainsAssignmentLogic(
                        (attackerLeader != null) ? attackerLeader.Name : null,
                        (defenderLeader != null) ? defenderLeader.Name : null),

                    new EquipmentControllerLeaveLogic(),
                    new MissionHardBorderPlacer(),
                    new MissionBoundaryPlacer(),
                    new MissionBoundaryCrossingHandler(10f),

                    // HighlightsController and BattleHighlightsController are DROPPED: they exist to record the
                    // player's finest moments, and there is no player.
                    //
                    // BattleDeploymentMissionController and BattleDeploymentHandler are DROPPED and must stay
                    // dropped: the deployment controller dereferences Mission.InitialPlayerAgent unconditionally,
                    // in two places, and no argument we could pass would stop it. This is the one that costs us
                    // something -- with no deployment controller, nothing calls Mission.OnDeploymentFinished --
                    // and the finisher below is what pays it back.
                    new RBMSpectatorDeploymentFinisher(),

                    // The shared spectate marker. IsSpectating keys on this, and this alone, in both mission kinds.
                    new RBMSpectatorMarker()
                };
            }, true, true);

            // Vanilla's trailing SetPlayerCanTakeControlOfAnotherAgentWhenDead() is dropped with the rest: there is
            // no player agent to die, and no agent for him to take over when he does not.
        }

        /// <summary>
        /// The same idea, aimed at a wall: a siege assault between two AI lords, watched from a free camera.
        ///
        /// This is SandBoxMissions.OpenSiegeMissionWithDeployment (the assault path) with the player cut out, exactly
        /// as OpenSpectatorBattleMission is the field version of OpenBattleMission. The differences from the field fork
        /// are the ones a siege forces:
        ///
        ///   The scene is a NAMED town scene at a wall-damage level, not a random field patch. It, the wall hit-point
        ///   ratios and the two sides' prepared siege engines all come off the besieged settlement's SiegeEvent -- the
        ///   same sources PlayerSiege.StartSiegeMission reads, pointed at the watched event instead of the player's.
        ///
        ///   The deployment controller STAYS. Dropping it (as the field fork does) would leave the assault with no
        ///   towers, rams or ladders placed -- a degenerate engine-less siege. But DeploymentMissionController
        ///   dereferences Mission.InitialPlayerAgent unguarded, and there is no player agent. The fix is the RTSCamera
        ///   proxy: with CommandMode on before deployment, RTSCamera possesses a stand-in troop on team-deploy and
        ///   writes it into Mission._initialPlayerAgent (SwitchFreeCameraLogic.OnEarlyTeamDeployed), which fires from
        ///   OnSetupTeamsOfSide -> OnSideDeploymentOver -> Mission.OnTeamDeployed BEFORE SetupTeams derefs the field.
        ///   This is the one place the siege fork WANTS the proxy the field fork spends effort avoiding.
        ///
        ///   And with no player to press "Begin", BattleInitializationModel.SetBypassPlayerDeployment(true) makes
        ///   SetupTeams call FinishDeployment on its own. It is a persisted static, so it -- and CommandMode -- are put
        ///   back on mission end by RBMSpectatorSiegeBridge, or on the spot if OpenNew throws.
        ///
        /// Sally-out is out of scope here: assault only. The future hook is
        /// OpenSiegeMissionWithDeployment(isSallyOut:true) + SandBoxSallyOutMissionController, which needs a different
        /// spawn set ("sally_out_set"), the sally-out mission controller and priority-ambush troops -- not built.
        /// </summary>
        public static Mission OpenSpectatorSiegeMission(MapEvent mapEvent, BattleSideEnum watchSide)
        {
            Settlement settlement = mapEvent.MapEventSettlement;
            SiegeEvent siegeEvent = settlement.SiegeEvent;

            int wallLevel = settlement.Town.GetWallLevel();
            string scene = settlement.LocationComplex.GetLocationWithId("center").GetSceneName(wallLevel);
            float[] wallHitPointPercentages = settlement.SettlementWallSectionHitPointsRatioList.ToArray();

            // Defender = 0, Attacker = 1, exactly as PlayerSiege.StartSiegeMission orders them.
            List<MissionSiegeWeapon> siegeWeaponsOfAttackers =
                siegeEvent.GetPreparedAndActiveSiegeEngines(siegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker));
            List<MissionSiegeWeapon> siegeWeaponsOfDefenders =
                siegeEvent.GetPreparedAndActiveSiegeEngines(siegeEvent.GetSiegeEventSide(BattleSideEnum.Defender));
            bool hasAnySiegeTower = siegeWeaponsOfAttackers.Exists(
                delegate (MissionSiegeWeapon data) { return data.Type == DefaultSiegeEngineTypes.SiegeTower; });

            // isPlayerAttacker, in the deployment controller's terms, is "is the side we watch the attacker": it decides
            // which side the controller treats as PlayerSide and hangs its deployment boundaries and hide/unhide off.
            bool isPlayerAttacker = watchSide == BattleSideEnum.Attacker;

            string upgradeLevelTag = Campaign.Current.Models.LocationModel.GetUpgradeLevelTag(wallLevel) + " siege";
            MissionInitializerRecord rec = BuildSiegeRecord(mapEvent, scene, upgradeLevelTag);

            // Before OpenNew, for the same reason the field fork sets its watched event early: the mission ticks views
            // while still loading, and the preload view reaches for the watched event on the first pre-tick.
            _watchedMapEvent = mapEvent;
            _watchedSide = watchSide;
            _cachedMission = null;
            _cachedIsSpectator = false;

            // Two persisted statics, set before the mission is built and restored by RBMSpectatorSiegeBridge on end.
            //   BypassPlayerDeployment: no player to press Begin, so SetupTeams must auto-finish.
            //   CommandMode: on, so RTSCamera possesses the proxy that satisfies the InitialPlayerAgent derefs. The
            //   field fork keeps this false through deployment on purpose; the siege fork needs it true.
            bool previousCommandMode;
            RBMSpectatorCommandMode.TryRead(out previousCommandMode);
            BattleInitializationModel.SetBypassPlayerDeployment(true);
            RBMSpectatorCommandMode.TryWrite(true);

            try
            {
                return MissionState.OpenNew("SiegeMissionWithDeployment", rec, delegate
                {
                    Hero attackerLeader = mapEvent.AttackerSide.LeaderParty.LeaderHero;
                    Hero defenderLeader = mapEvent.DefenderSide.LeaderParty.LeaderHero;

                    return new MissionBehavior[]
                    {
                        new BattleSpawnLogic("battle_set"),
                        new MissionOptionsComponent(),
                        new CampaignMissionComponent(),

                        // BattleEndLogic. Vanilla arms EnableEnemyDefenderPullBack when the player is the defender,
                        // reading MobileParty.MainParty.MapEvent.PlayerSide -- MainParty has no siege event here. The
                        // pull-back is an optional lords-hall retreat convenience for a defending player; there is no
                        // player, so it is simply left off. BattleEndLogic itself is kept (it derefs PlayerTeam, which
                        // is the watched side and non-null).
                        new BattleEndLogic(),
                        new BattleReinforcementsSpawnController(),

                        // Second arg becomes Mission.PlayerTeam: the leading party of the watched side, a real
                        // combatant with no player attached. Siege team-AI type, isPlayerSergeant false. Defender(0),
                        // Attacker(1) leaders as vanilla.
                        new MissionCombatantsLogic(
                            mapEvent.InvolvedParties,
                            mapEvent.GetLeaderParty(watchSide),
                            mapEvent.GetLeaderParty(BattleSideEnum.Defender),
                            mapEvent.GetLeaderParty(BattleSideEnum.Attacker),
                            Mission.MissionTeamAITypeEnum.Siege,
                            false),

                        // Walls, breaches and pre-destruction, all from ctor args -- zero player deref.
                        new SiegeMissionPreparationHandler(false, false, wallHitPointPercentages, hasAnySiegeTower),

                        // CampaignSiegeStateHandler is DROPPED. Its ctor sets _mapEvent = PlayerEncounter.Battle (null),
                        // and its OnEndMission both derefs that and calls Settlement.SetNextSiegeState() -- a campaign
                        // mutation a spectator must never perform. Nothing else in-mission reads it. WorkshopMissionHandler
                        // is likewise omitted: it only appears when GetCurrentTown() is non-null (the player standing in
                        // a town), which is not this, and it is irrelevant to a watched assault.

                        // The spawn handler, told which event it is spawning. Base finds it via MapEvent.PlayerMapEvent
                        // (null); the subclass hands it the watched one. Siege spawn settings (no horses, single phase).
                        new RBMSpectatorSiegeSpawnHandler(mapEvent),

                        // Same factory as the field fork, with BattleSizeType.Siege. Defender supplier first, as the
                        // vanilla factory has it.
                        new DefaultBattleMissionAgentSpawnLogic(
                            new IMissionTroopSupplier[]
                            {
                                new PartyGroupTroopSupplier(mapEvent, BattleSideEnum.Defender, null, null),
                                new PartyGroupTroopSupplier(mapEvent, BattleSideEnum.Attacker, null, null)
                            },
                            watchSide,
                            Mission.BattleSizeType.Siege),

                        new BattlePowerCalculationLogic(),
                        new BattleObserverMissionLogic(),

                        // BattleAgentLogic is DROPPED, same as the field fork: it reaches through MapEvent.PlayerMapEvent
                        // for the upgrade tracker, and it pays out xp / records casualties -- which a non-canonical
                        // battle must not do.

                        new BattleSurgeonLogic(),
                        new MountAgentLogic(),
                        new BannerBearerLogic(),
                        new AgentHumanAILogic(),

                        // Defender-side ammo resupply, siege-only -- the men on the walls get their arrows back, as
                        // vanilla resupplies side 0 (Defender). Player-agnostic (keys off side).
                        new AmmoSupplyLogic(new List<BattleSideEnum> { BattleSideEnum.Defender }),

                        new AgentVictoryLogic(),

                        // The ignition: PlayerTeam.SetPlayerRole(false, false) hands the watched team's formations to
                        // the AI. Same call the field fork makes.
                        new AssignPlayerRoleInTeamMissionController(false, false, false, null),

                        // Generals and captains, minus the player-housekeeping OnDeploymentFinished. Reuses the field
                        // fork's subclass. Names off the watched event, not MapEvent.PlayerMapEvent (which NREs).
                        new RBMSpectatorGeneralsAndCaptainsAssignmentLogic(
                            (attackerLeader != null) ? attackerLeader.Name : null,
                            (defenderLeader != null) ? defenderLeader.Name : null),

                        new MissionAgentPanicHandler(),
                        new MissionBoundaryPlacer(),
                        new MissionBoundaryCrossingHandler(10f),
                        new AgentMoraleInteractionLogic(),

                        // HighlightsController / BattleHighlightsController DROPPED: they record the player's finest
                        // moments, and there is no player.

                        new EquipmentControllerLeaveLogic(),

                        // Siege engines and their placement. Defender engines first. KEPT -- without the deployment
                        // handler and controller the assault has no towers/rams/ladders placed.
                        new MissionSiegeEnginesLogic(siegeWeaponsOfDefenders, siegeWeaponsOfAttackers),
                        new SiegeDeploymentHandler(isPlayerAttacker),
                        new SiegeDeploymentMissionController(isPlayerAttacker),

                        // Restores CommandMode + BypassPlayerDeployment and calls EndWatching on mission end. There is
                        // no RBMSpectatorDeploymentFinisher here (the deployment controller does the finishing), so the
                        // bridge is what closes the watch.
                        new RBMSpectatorSiegeBridge(previousCommandMode),

                        // The shared spectate marker. IsSpectating keys on this in both mission kinds.
                        new RBMSpectatorMarker()
                    };
                }, true, true);

                // Vanilla's trailing SetPlayerCanTakeControlOfAnotherAgentWhenDead() is dropped: no player agent.
            }
            catch (Exception)
            {
                // OpenNew faulted before the bridge could take over the restore. Put the statics back here so a failed
                // spectate cannot bleed a bypassed deployment or a stuck command mode into the player's own next siege.
                RBMSpectatorCommandMode.TryWrite(previousCommandMode);
                BattleInitializationModel.SetBypassPlayerDeployment(false);
                EndWatching();
                throw;
            }
        }

        /// <summary>
        /// The scene record for a siege: a named town scene at a wall-damage level, not a map patch.
        ///
        /// This mirrors SandBoxMissions.CreateSandBoxMissionInitializerRecord, but sources position (weather/terrain)
        /// off the watched event rather than MobileParty.MainParty -- the player may be anywhere. The scene itself is
        /// named (the besieged town at its wall level), so position only colours atmosphere and terrain type.
        /// </summary>
        private static MissionInitializerRecord BuildSiegeRecord(MapEvent mapEvent, string scene, string sceneLevels)
        {
            CampaignVec2 position = mapEvent.Position;

            MissionInitializerRecord rec = new MissionInitializerRecord(scene);
            rec.DamageToFriendsMultiplier = Campaign.Current.Models.DifficultyModel.GetPlayerTroopsReceivedDamageMultiplier();
            rec.DamageFromPlayerToFriendsMultiplier = Campaign.Current.Models.DifficultyModel.GetPlayerTroopsReceivedDamageMultiplier();
            rec.PlayingInCampaignMode = true;
            rec.AtmosphereOnCampaign = Campaign.Current.Models.MapWeatherModel.GetAtmosphereModel(position);
            rec.TerrainType = (Campaign.Current.MapSceneWrapper != null)
                ? (int)Campaign.Current.MapSceneWrapper.GetFaceTerrainType(position.Face)
                : 0;
            rec.SceneLevels = sceneLevels;
            rec.DoNotUseLoadingScreen = false;
            rec.DecalAtlasGroup = 3; // Siege
            return rec;
        }

        /// <summary>
        /// Where the battle is, and what it looks like there.
        ///
        /// Vanilla builds this record around MobileParty.MainParty -- its position picks the map patch, its
        /// navigation face picks the terrain, its position picks the weather. Every one of those is the wrong place
        /// now: the player could be on the other side of Calradia. The map event knows where it is; ask it.
        /// </summary>
        private static MissionInitializerRecord BuildRecord(MapEvent mapEvent)
        {
            CampaignVec2 position = mapEvent.Position;
            MapPatchData mapPatch = Campaign.Current.MapSceneWrapper.GetMapPatchAtPosition(in position);
            string scene = Campaign.Current.Models.SceneModel.GetBattleSceneForMapPatch(mapPatch, false);

            MissionInitializerRecord rec = new MissionInitializerRecord(scene);
            rec.TerrainType = (int)Campaign.Current.MapSceneWrapper.GetFaceTerrainType(position.Face);
            rec.DamageToFriendsMultiplier = Campaign.Current.Models.DifficultyModel.GetPlayerTroopsReceivedDamageMultiplier();
            rec.DamageFromPlayerToFriendsMultiplier = Campaign.Current.Models.DifficultyModel.GetPlayerTroopsReceivedDamageMultiplier();
            rec.NeedsRandomTerrain = false;
            rec.PlayingInCampaignMode = true;
            rec.RandomTerrainSeed = MBRandom.RandomInt(10000);
            rec.AtmosphereOnCampaign = Campaign.Current.Models.MapWeatherModel.GetAtmosphereModel(position);
            rec.SceneHasMapPatch = true;
            rec.DecalAtlasGroup = 2;
            rec.PatchCoordinates = mapPatch.normalizedCoordinates;
            rec.PatchEncounterDir = (mapEvent.AttackerSide.LeaderParty.Position.ToVec2()
                                     - mapEvent.DefenderSide.LeaderParty.Position.ToVec2()).Normalized();
            return rec;
        }
    }
}
