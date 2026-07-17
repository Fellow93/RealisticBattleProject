using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.TroopSuppliers;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
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
        /// the finisher that only we ever add. That is self-clearing by construction -- a stale _watchedMapEvent is
        /// harmless, because the next mission does not carry our behaviour and the gate shuts on its own. The
        /// per-mission cache is there because this is asked from a property getter that vanilla calls freely.
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
                    _cachedIsSpectator = mission.GetMissionBehavior<RBMSpectatorDeploymentFinisher>() != null;
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
                    new RBMSpectatorDeploymentFinisher()
                };
            }, true, true);

            // Vanilla's trailing SetPlayerCanTakeControlOfAnotherAgentWhenDead() is dropped with the rest: there is
            // no player agent to die, and no agent for him to take over when he does not.
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
