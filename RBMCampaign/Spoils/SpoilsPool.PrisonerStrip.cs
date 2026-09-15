using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The prisoners the player leaves on the post-battle loot screen do not simply vanish -- his men
    /// strip the kit off their backs before turning them loose. Vanilla drops the untaken prisoners into
    /// a throwaway roster and clears it, so a fully armoured captive the party has no room to keep was
    /// worth nothing; here half his kit's worth is recovered as spoils and split among the party the same
    /// way a fallen enemy's stripped gear is.
    /// </summary>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// The share of a left-behind captive's kit his captors carry off. He is stripped and turned
        /// loose rather than kept, so his arms and armour stay behind; half their worth, echoing the
        /// mean salvage a fallen man's kit yields on the field (<see cref="MinSalvageFraction"/>..
        /// <see cref="MaxSalvageFraction"/>), reaches the men.
        /// </summary>
        private const float LeftoverPrisonerStripFraction = 0.5f;

        /// <summary>
        /// Strips the kit off the prisoners the player declined to take and hands half its worth to the
        /// main party's stacks by tier weight, with the leader taking his usual cut. Priced off
        /// <see cref="GetEquipmentValueWithMount"/>, the same averaged battle-set value wages and ransoms
        /// meter against, so a knight left on the field is worth his mail and his horse and a levy his
        /// spear. Heroes never reach here -- they are pulled out of the loot pool and resolved by
        /// conversation before the screen opens -- but are skipped defensively.
        /// </summary>
        /// <returns>The spoils that actually reached the stacks, for the announcement to note.</returns>
        public static int StripLeftoverPrisoners(TroopRoster leftovers)
        {
            if (!IsEnabled || leftovers == null)
            {
                return 0;
            }
            PartyBase party = PartyBase.MainParty;
            if (party == null || party.MemberRoster == null)
            {
                return 0;
            }

            long gross = 0L;
            int men = 0;
            for (int i = 0; i < leftovers.Count; i++)
            {
                TroopRosterElement element = leftovers.GetElementCopyAtIndex(i);
                CharacterObject character = element.Character;
                if (character == null || character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                int per = GetEquipmentValueWithMount(character);
                if (per > 0)
                {
                    gross += (long)per * element.Number;
                }
                men += element.Number;
            }
            if (gross <= 0L)
            {
                return 0;
            }

            int stripped = (int)MathF.Min(gross * LeftoverPrisonerStripFraction, (float)int.MaxValue);
            if (stripped <= 0)
            {
                return 0;
            }

            int total = GrantSpoilsWeightedByTier(party, stripped, "STRIP", out int companionGold);
            int troopGranted = total - companionGold;
            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("STRIP", party, "left " + men + " prisoners on the field; their kit worth " + gross
                    + " stripped for " + stripped + " (half), " + troopGranted + " split to the stacks by tier weight");
            }
            int leaderCut = (troopGranted > 0)
                ? ApplyLeaderCut(party, troopGranted)
                : (companionGold > 0 ? 0 : ApplyLeaderCutSolo(party, stripped));
            AnnounceStrippedPrisonersToPlayer(troopGranted, leaderCut, men);
            AnnounceCompanionSpoilsToPlayer(companionGold);
            return troopGranted;
        }

        /// <summary>
        /// Tells the player what stripping the prisoners he left behind fetched, so the spoils bar filling
        /// after a battle he took no captives from is not a silent mystery. Nothing recovered says nothing.
        /// </summary>
        private static void AnnounceStrippedPrisonersToPlayer(int granted, int leaderCut, int men)
        {
            if (men <= 0)
            {
                return;
            }
            if (granted > 0)
            {
                TextObject message = new TextObject("{=RBM_SPOILS_024}Your men strip the {COUNT} prisoners you left behind and recover {AMOUNT} in spoils.");
                message.SetTextVariable("COUNT", men);
                message.SetTextVariable("AMOUNT", granted);
                InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
            }
            // Even with no men to share the kit, a lone captor still keeps his commander's cut -- announce it.
            AnnounceLeaderCutToPlayer(leaderCut);
        }
    }

    /// <summary>
    /// Catches the prisoners the player leaves on the post-battle loot screen the instant before vanilla
    /// clears them off the field. <see cref="PlayerEncounter.OnPlayerLootMembersAndPrisonerEnd"/> is the
    /// callback the loot party-screen closes into; its <c>leftPrisonRoster</c> argument is the loot pool's
    /// unclaimed side -- exactly the captives the player did not take -- which the method then discards.
    /// The prefix reads them while they still exist and strips their kit for spoils.
    /// </summary>
    [HarmonyPatch(typeof(PlayerEncounter), "OnPlayerLootMembersAndPrisonerEnd")]
    public static class PlayerEncounterLootPrisonerStripPatch
    {
        private static void Prefix(TroopRoster leftPrisonRoster)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            SpoilsPool.StripLeftoverPrisoners(leftPrisonRoster);
        }
    }
}
