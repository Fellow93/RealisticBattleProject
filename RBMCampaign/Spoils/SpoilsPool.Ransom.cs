using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Issues;
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
            StripPrisonersForSpoils(sellerParty, prisoners, "RANSOM", "ransomed prisoners",
                AnnounceRansomSpoilsToPlayer);
        }

        /// <summary>
        /// The same kit-strip gather as a ransom, for prisoners handed over to satisfy a delivery quest --
        /// a route that never touches <see cref="SellPrisonersAction"/>, so the ransom gear-strip would
        /// otherwise skip it. Keeps delivering a captive worth the same stripped kit as ransoming one; the
        /// quest's own gold reward is separate and untouched.
        /// </summary>
        public static void DeliverQuestPrisonersForSpoils(PartyBase sellerParty, TroopRoster prisoners)
        {
            StripPrisonersForSpoils(sellerParty, prisoners, "QUESTDELIVER", "delivered quest prisoners",
                AnnounceQuestDeliverSpoilsToPlayer);
        }

        /// <summary>
        /// Shared kit-strip gather behind ransom and quest delivery: prices the prisoners' kit, splits it
        /// to the party's stacks by tier weight, skims the leader's cut, and (for the main party) announces
        /// through <paramref name="announce"/>. <paramref name="logCategory"/> and <paramref name="logVerb"/>
        /// only colour the dev log; the money math is identical either way.
        /// </summary>
        private static void StripPrisonersForSpoils(PartyBase sellerParty, TroopRoster prisoners,
            string logCategory, string logVerb, Action<int, int> announce)
        {
            if (!IsEnabled || sellerParty == null || prisoners == null || IsExemptParty(sellerParty))
            {
                return;
            }

            int pot = SumRansomGearValue(prisoners);
            if (pot <= 0)
            {
                return;
            }

            int total = GrantSpoilsWeightedByTier(sellerParty, pot, logCategory, out int companionGold);
            int troopGranted = total - companionGold;
            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log(logCategory, sellerParty, SpoilsLog.Describe(sellerParty)
                    + " " + logVerb + "; their kit worth " + pot + " split to the stacks by tier weight ("
                    + troopGranted + ")");
            }
            int leaderCut = (troopGranted > 0)
                ? ApplyLeaderCut(sellerParty, troopGranted)
                : (companionGold > 0 ? 0 : ApplyLeaderCutSolo(sellerParty, pot));
            if (sellerParty == PartyBase.MainParty)
            {
                announce(troopGranted, leaderCut);
                AnnounceCompanionSpoilsToPlayer(companionGold);
            }
        }

        /// <summary>
        /// The stripped-kit pot a roster of prisoners yields: every captive's kit worth -- heroes and levy
        /// alike -- priced off <see cref="GetEquipmentValueWithMount"/> and summed over the stack. The gross
        /// the ransom gather grants and the ransom-menu tooltip previews, so the two always agree.
        /// </summary>
        public static int SumRansomGearValue(TroopRoster prisoners)
        {
            if (prisoners == null)
            {
                return 0;
            }
            long gross = 0L;
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
            }
            return (int)MathF.Min(gross, (float)int.MaxValue);
        }

        /// <summary>
        /// Tells the player the ransom's spoils half -- what his men kept off the prisoners' backs and, if
        /// there is one, the leader's cut of it -- so the spoils bar filling after a ransom is not silent
        /// next to vanilla's gold popup for the man-ransom itself.
        /// </summary>
        private static void AnnounceRansomSpoilsToPlayer(int granted, int leaderCut)
        {
            if (granted > 0)
            {
                TextObject message = new TextObject("{=RBM_SPOILS_025}Your men strip the ransomed prisoners' kit and split {AMOUNT} in spoils.");
                message.SetTextVariable("AMOUNT", granted);
                InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
            }
            // Even with no men to share the kit, a lone captor still keeps his commander's cut -- announce it.
            AnnounceLeaderCutToPlayer(leaderCut);
        }

        /// <summary>
        /// Tells the player what stripping the prisoners he handed to a delivery quest fetched -- the kit
        /// half RBM adds on top of the quest's own gold reward, so the spoils bar filling after a delivery
        /// is not a silent mystery.
        /// </summary>
        private static void AnnounceQuestDeliverSpoilsToPlayer(int granted, int leaderCut)
        {
            if (granted > 0)
            {
                TextObject message = new TextObject("{=RBM_SPOILS_029}Your men strip the delivered prisoners' kit and keep {AMOUNT} in spoils.");
                message.SetTextVariable("AMOUNT", granted);
                InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
            }
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

    /// <summary>
    /// Gives the prisoner-delivery quest ("Landowner Needs Manual Laborers") the same gear-strip spoils a
    /// normal ransom grants, so handing captives to the quest is not silently worth far less than selling
    /// them at a settlement would be.
    /// </summary>
    /// <remarks>
    /// The quest delivers prisoners through its OWN <c>PartyScreenMode.PrisonerManage</c> screen and never
    /// calls <see cref="SellPrisonersAction"/>, so <see cref="SellPrisonersGearSpoilsPatch"/> never fires
    /// for it -- the player got only the quest's gold and none of the kit worth. Vanilla still pays that
    /// gold (5x the ransom value per captive); this only adds back the stripped-kit half a normal ransom
    /// grants, restoring parity between delivering and ransoming.
    ///
    /// <c>OnDoneClicked</c> runs once per delivery session, and <c>leftPrisonRoster</c> is exactly the
    /// prisoners moved to the quest giver's side that session -- the same roster its own reward loop meters
    /// -- so stripping it grants the kit spoils once, for precisely the captives handed over. Priced and
    /// paid through the very same <see cref="RansomPrisonersForSpoils"/> a settlement ransom uses.
    /// </remarks>
    [HarmonyPatch(typeof(LandLordNeedsManualLaborersIssueBehavior.LandLordNeedsManualLaborersIssueQuest), "OnDoneClicked")]
    public static class ManualLaborersQuestGearSpoilsPatch
    {
        private static void Postfix(TroopRoster leftPrisonRoster)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            SpoilsPool.DeliverQuestPrisonersForSpoils(PartyBase.MainParty, leftPrisonRoster);
        }
    }
}
