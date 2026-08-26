using System.Collections.Generic;
using HarmonyLib;
using SandBox.View.Missions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;

namespace RBMCampaign
{
    /// <summary>
    /// The views, taught that the battle on screen is not the player's.
    ///
    /// The mission behaviours were the part we wrote, so they were the part we audited. The views are the part the
    /// engine adds behind us -- MissionState.OpenNew("Battle", ...) hands the name to ViewCreatorManager, which looks
    /// up [ViewMethod("Battle")] and appends thirty more MissionBehaviors we never listed, plus every [DefaultView]
    /// type in every loaded assembly. We keep the name on purpose: it is what supplies the spectate-target cycling and
    /// the scoreboard. So the views come with it, and two of them ask the same question the behaviours did -- "which
    /// side is the player on?" -- of a battle he is not in.
    ///
    /// Both patches are gated on RBMSpectatorMission.IsSpectating and are inert in every other mission. The gate is
    /// NOT "PlayerMapEvent is null": that is true of missions that are none of our business.
    /// </summary>
    [HarmonyPatch(typeof(MissionPreloadView), "OnPreMissionTick")]
    public static class RBMSpectatorPreloadViewPatch
    {
        /// <summary>
        /// Preload the men who are actually going to fight.
        ///
        /// MissionPreloadView walks MapEvent.PlayerMapEvent.InvolvedParties on the first pre-tick and hands every
        /// troop's character to PreloadHelper, so the meshes are resident before the battle starts instead of being
        /// faulted in mid-charge. In a spectated battle PlayerMapEvent is null and it faults on the foreach itself --
        /// this is the reported crash, at SandBox.View.Missions\MissionPreloadView.cs:24.
        ///
        /// Skipping it would fix the crash and cost us the whole point of the view: a spectated battle is watched from
        /// a free camera flying down the line, which is the worst case for mesh hitching, and it is precisely the
        /// battle we are trying to judge RBM's AI by. We have the real event in hand, so we do the same work against
        /// it: same loop, same helper instance, same _preloadDone latch, only the party source is different.
        ///
        /// Vanilla also preloads siege missiles when a SiegeDeploymentMissionController is present. The siege fork keeps
        /// that controller (the assault needs its towers and rams), so for a spectated siege the branch DOES apply and
        /// is reproduced below: same GetSiegeMissiles(), same helper. In a field battle the lookup returns null and it
        /// is skipped, exactly as vanilla skips it.
        /// </summary>
        // Harmony injects a field as a parameter named ___<field>. These fields are themselves named with a leading
        // underscore, so the parameters carry four: three for Harmony, one belonging to the field.
        public static bool Prefix(ref bool ____preloadDone, PreloadHelper ____helperInstance)
        {
            if (!RBMSpectatorMission.IsSpectating)
            {
                return true;
            }
            if (____preloadDone)
            {
                return false;
            }

            MapEvent watched = RBMSpectatorMission.WatchedMapEvent;
            if (watched == null || ____helperInstance == null)
            {
                // Nothing to preload against. Latch anyway so this does not run every frame for the whole battle.
                ____preloadDone = true;
                return false;
            }

            List<BasicCharacterObject> characters = new List<BasicCharacterObject>();
            foreach (PartyBase party in watched.InvolvedParties)
            {
                if (party == null || party.MemberRoster == null)
                {
                    continue;
                }
                foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster())
                {
                    if (element.Character == null)
                    {
                        continue;
                    }
                    for (int i = 0; i < element.Number; i++)
                    {
                        characters.Add(element.Character);
                    }
                }
            }

            ____helperInstance.PreloadCharacters(characters);

            // Siege: the assault keeps a SiegeDeploymentMissionController, and its GetSiegeMissiles() enumerates the
            // stones and bolts every ranged engine on the field will throw. Preloading them here spares the same
            // mid-charge hitching the character preload spares -- worse in a siege, where the first volleys land
            // immediately. Field battles have no such controller, so this is null and skipped.
            SiegeDeploymentMissionController siegeDeployment =
                Mission.Current.GetMissionBehavior<SiegeDeploymentMissionController>();
            if (siegeDeployment != null)
            {
                ____helperInstance.PreloadItems(siegeDeployment.GetSiegeMissiles());
            }

            ____preloadDone = true;
            return false;
        }
    }

    /// <summary>
    /// "Is the player attacking?" -- answered for a battle the player is not in.
    ///
    /// PlayerEncounter.PlayerIsAttacker is `Current.PlayerSide == BattleSideEnum.Attacker`, and Current is
    /// `Campaign.Current.PlayerEncounter`, which is null for the whole spectated mission. Unlike its neighbour
    /// PlayerEncounter.Battle, which null-checks Current and returns null, this one dereferences it bare.
    ///
    /// It matters because SPScoreboardVM.Initialize reads it twice, unguarded, to decide which column of the scoreboard
    /// is "yours" -- and Initialize is called straight out of MissionGauntletBattleScore.OnMissionScreenInitialize, so
    /// the scoreboard is a second, independent crash sitting behind the preload one. The scoreboard is one of the two
    /// things the "Battle" view list exists to give us; losing it is not an option.
    ///
    /// Patching the one-line getter rather than the scoreboard's Initialize is the narrower fix and the more honest
    /// one. Every caller in this mission is asking an unanswerable question and would fault on it; the closest true
    /// answer is the side we chose to watch, which is exactly what the scoreboard wants to highlight. Fixing it at the
    /// source covers the callers we have not enumerated as well as the one we have.
    /// </summary>
    [HarmonyPatch(typeof(PlayerEncounter), "PlayerIsAttacker", MethodType.Getter)]
    public static class RBMSpectatorPlayerIsAttackerPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!RBMSpectatorMission.IsSpectating)
            {
                return true;
            }
            __result = RBMSpectatorMission.WatchedSide == BattleSideEnum.Attacker;
            return false;
        }
    }

    /// <summary>
    /// One team per side, because there is no player to be allied to.
    ///
    /// When the watched side is an army -- more than one party, the leader party being just one of them --
    /// MissionCombatantsLogic splits it in two: a PlayerTeam for the leader, and a PlayerAllyTeam for everyone else,
    /// on the reasoning that in a normal battle the player commands his own men while allied lords fight beside him
    /// under their own AI. That reasoning has no purchase here. There is no player, so nobody is "under the player's
    /// command" -- Mission.GetAgentTeam then routes EVERY watched-side troop, the leader party included, to the ally
    /// team, and leaves Mission.PlayerTeam holding zero men. An empty team is given no deployment plan, and the spawn
    /// gate will not spawn a side until every team on it has one -- so the whole watched side never appears and the
    /// enemy wins on an empty field. It only bites armies; a single-party side has no second combatant to hive off,
    /// so its troops stay on PlayerTeam and it spawns fine. That is why the first battles watched -- lone parties --
    /// worked, and an army battle did not.
    ///
    /// The fix is to refuse the split. The enemy side is ALREADY a single team no matter how many lords stand on it
    /// (MissionCombatantsLogic only ever hives off an ally team on the PLAYER side), so collapsing the watched side to
    /// one team does not lopside the fight -- it makes the two sides symmetric, which is exactly what a battle nobody
    /// is commanding should be. SupportsAllyTeamOnPlayerSide is the single gate the split hangs on; deny it, and every
    /// watched-side party lands on the one PlayerTeam. Gated on IsSpectating, inert in every real battle, and it
    /// covers the siege fork for free since it splits the player side the same way.
    /// </summary>
    // v1.5.1 added a 5-arg naval static overload of SupportsAllyTeamOnPlayerSide, so the name-only
    // selector became ambiguous and failed to bind. Pin the 1-arg instance overload by its out param.
    [HarmonyPatch(typeof(MissionCombatantsLogic), "SupportsAllyTeamOnPlayerSide",
        new[] { typeof(IBattleCombatant) }, new[] { ArgumentType.Out })]
    public static class RBMSpectatorSuppressAllyTeamPatch
    {
        public static void Postfix(ref bool __result, ref IBattleCombatant allyCombatant)
        {
            if (!RBMSpectatorMission.IsSpectating)
            {
                return;
            }
            __result = false;
            allyCombatant = null;
        }
    }
}
