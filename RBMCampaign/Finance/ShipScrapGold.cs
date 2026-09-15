using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;

namespace RBMCampaign
{
    /// <summary>
    /// The scrap value of a defeated clan party's ships (v1.5.0 clan party recovery) is noted in the
    /// clan event-gold ledger so the finance tooltip can show where the windfall came from.
    /// </summary>
    /// <remarks>
    /// <c>EmptyClanPartiesCampaignBehavior.DistributePartyShipsAndRecoverGold</c> hands any ships no
    /// other clan party can take to the player as gold with a null giver. The ships were genuinely lost,
    /// so the coin stays minted; it is only recorded. Marker prefix + finalizer around the method, and
    /// the null-giver-to-player hand-off inside it is what gets recorded.
    /// </remarks>
    public static class ShipScrapGold
    {
        private static bool _recovering;

        [HarmonyPatch(typeof(EmptyClanPartiesCampaignBehavior), "DistributePartyShipsAndRecoverGold")]
        private static class MarkRecoveryPatch
        {
            private static void Prefix()
            {
                _recovering = RBMConfig.RBMConfig.rbmCampaignEnabled;
            }

            private static void Finalizer()
            {
                _recovering = false;
            }
        }

        [HarmonyPatch(typeof(GiveGoldAction), "ApplyInternal")]
        private static class RecordScrapPatch
        {
            private static void Prefix(Hero giverHero, PartyBase giverParty, Hero recipientHero, int goldAmount)
            {
                if (!_recovering || giverHero != null || giverParty != null || recipientHero != Hero.MainHero || goldAmount <= 0)
                {
                    return;
                }
                ClanEventGoldLedger.Record(Hero.MainHero, EventGoldKind.ShipScrap, goldAmount);
            }
        }
    }
}
