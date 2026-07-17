using System.Collections.Generic;
using HarmonyLib;
using SandBox.View.Missions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
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
        /// Vanilla also preloads siege missiles when a SiegeDeploymentMissionController is present. That branch is not
        /// reproduced because it cannot be reached: RBMSpectateCampaignBehavior.ShouldOffer only ever offers a field
        /// battle (IsFieldBattle, non-naval), and a field battle has no siege deployment controller.
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
}
