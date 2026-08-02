using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// A ransomed prisoner is handed back to the market that buys him, but his kit is not: the captors
    /// strip his arms, armour and mount and keep their worth. That worth does not reach the leader as gold
    /// -- it flows through the spoils system exactly the way a fallen enemy's stripped gear does, split
    /// among the men who took him by tier weight, with the leader skimming his usual cut off the top.
    /// </summary>
    /// <remarks>
    /// The MAN himself is still paid for in gold, by the town buying the captive back out of its citizen
    /// wealth (see <see cref="RansomFunding"/>) -- that is the "gold for leader" half of a ransom. This
    /// file adds the "spoils to party" half: the gear, priced off
    /// <see cref="SpoilsPool.GetEquipmentValueWithMount"/> -- the same averaged battle-set value battle
    /// loot and the leftover-prisoner strip meter against -- and granted through the very same
    /// <see cref="SpoilsPool.GrantSpoilsWeightedByTier"/> / <see cref="SpoilsPool.ApplyLeaderCut"/> pair
    /// as every other gather, so the leader's cut and the men's share are figured identically.
    ///
    /// Every captive is stripped, a named lord included: a ransomed hero's arms and armour join the pot at
    /// their full worth alongside the levy's. He is a SOURCE of spoils, not a recipient of them -- the pot
    /// is still split only among the party's own non-hero stacks (that is all
    /// <see cref="SpoilsPool.GrantSpoilsWeightedByTier"/> pays out to), so a lord's fine kit ends up on the
    /// backs of the men who took him. His gold ransom is unchanged, exactly as vanilla paid it.
    /// </remarks>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// Strips the kit off every prisoner a party is ransoming -- heroes and levy alike -- and hands
        /// its full worth to that party's own stacks as spoils, split by tier weight, with the leader
        /// taking his cut as gold.
        /// </summary>
        public static void RansomPrisonersForSpoils(PartyBase sellerParty, TroopRoster prisoners)
        {
            if (!IsEnabled || sellerParty == null || prisoners == null || IsExemptParty(sellerParty))
            {
                return;
            }

            long gross = 0L;
            int men = 0;
            for (int i = 0; i < prisoners.Count; i++)
            {
                TroopRosterElement element = prisoners.GetElementCopyAtIndex(i);
                CharacterObject character = element.Character;
                if (character == null || element.Number <= 0)
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

            int pot = (int)MathF.Min(gross, (float)int.MaxValue);
            if (pot <= 0)
            {
                return;
            }

            int granted = GrantSpoilsWeightedByTier(sellerParty, pot, "RANSOM");
            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("RANSOM", sellerParty, SpoilsLog.Describe(sellerParty) + " ransomed " + men
                    + " prisoners; their kit worth " + pot + " split to the stacks by tier weight (" + granted + ")");
            }
            int leaderCut = ApplyLeaderCut(sellerParty, granted);
            if (sellerParty == PartyBase.MainParty)
            {
                AnnounceRansomSpoilsToPlayer(granted, leaderCut);
            }
        }

        /// <summary>
        /// Tells the player the ransom's spoils half -- what his men kept off the prisoners' backs and, if
        /// there is one, the leader's cut of it -- so the spoils bar filling after a ransom is not silent
        /// next to vanilla's gold popup for the man-ransom itself.
        /// </summary>
        private static void AnnounceRansomSpoilsToPlayer(int granted, int leaderCut)
        {
            if (granted <= 0)
            {
                return;
            }
            TextObject message = new TextObject("{=RBM_SPOILS_025}Your men strip the ransomed prisoners' kit and split {AMOUNT} in spoils.");
            message.SetTextVariable("AMOUNT", granted);
            InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
            AnnounceLeaderCutToPlayer(leaderCut);
        }
    }

    /// <summary>
    /// Fires the gear-into-spoils gather the instant a prisoner sale is applied for real. Sits alongside
    /// <see cref="RansomFunding"/>, which patches the same method to fund the man-ransom's gold from the
    /// buying town; this half turns the kit those prisoners were carrying into the party's spoils.
    /// </summary>
    /// <remarks>
    /// Gated on <paramref name="applyConsequences"/>: only the real sale paths
    /// (<c>ApplyForAllPrisoners</c> / <c>ApplyForSelectedPrisoners</c>) pay a ransom and part with the
    /// captives, so only they strip them. The party-screen preview (<c>ApplyByPartyScreen</c>, which passes
    /// false) removes nothing and pays nothing, so it strips nothing. <paramref name="prisoners"/> is the
    /// clone vanilla iterates, untouched by the roster edits inside, so the full sold list is still here.
    /// </remarks>
    [HarmonyPatch(typeof(SellPrisonersAction), "ApplyInternal")]
    public static class SellPrisonersGearSpoilsPatch
    {
        private static void Postfix(PartyBase sellerParty, TroopRoster prisoners, bool applyConsequences)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !applyConsequences)
            {
                return;
            }
            SpoilsPool.RansomPrisonersForSpoils(sellerParty, prisoners);
        }
    }
}
