using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Blood money paid to a ransom broker to end a feud (v1.5.0) lands in the broker's town instead of
    /// vanishing.
    /// </summary>
    /// <remarks>
    /// <c>ExecutionCampaignBehavior.conversation_ransom_broker_end_feud_on_consequence</c> settles the
    /// feud with <c>GiveGoldAction.ApplyBetweenCharacters(MainHero, null, bloodMoney)</c> -- a null
    /// recipient, so up to tens of thousands of denars leave the player and reach nobody. The broker
    /// brokered it and the wronged clan was paid, so the coin belongs in the market the deal was struck
    /// in: while the consequence runs (prefix + finalizer marker, the <see cref="RansomFunding"/>
    /// pattern), the player-to-nobody hand-off is mirrored into the current settlement's citizen wealth
    /// under the ransom source, and noted in <see cref="ClanEventGoldLedger"/> so the finance tooltip can
    /// explain the hole. Vanilla still takes the gold from the player; nothing is minted or doubled.
    /// </remarks>
    public static class BloodMoney
    {
        private static bool _settling;

        [HarmonyPatch(typeof(ExecutionCampaignBehavior), "conversation_ransom_broker_end_feud_on_consequence")]
        private static class MarkSettlementPatch
        {
            private static void Prefix()
            {
                _settling = RBMConfig.RBMConfig.rbmCampaignEnabled;
            }

            private static void Finalizer()
            {
                _settling = false;
            }
        }

        [HarmonyPatch(typeof(GiveGoldAction), "ApplyInternal")]
        private static class LandBloodMoneyPatch
        {
            private static void Prefix(Hero giverHero, PartyBase giverParty, Hero recipientHero, PartyBase recipientParty, int goldAmount)
            {
                if (!_settling
                    || giverHero != Hero.MainHero
                    || recipientHero != null || recipientParty != null
                    || goldAmount <= 0)
                {
                    return;
                }
                Settlement market = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
                if (market != null && SettlementWealth.HasCitizenPurse(market))
                {
                    SettlementWealth.CreditCitizens(market, goldAmount, SettlementWealth.Source.Ransom);
                }
                ClanEventGoldLedger.Record(Hero.MainHero, EventGoldKind.BloodMoney, goldAmount);
            }
        }
    }
}
