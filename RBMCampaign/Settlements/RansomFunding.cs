using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Makes somebody pay for the prisoners a lord sells.
    ///
    /// <c>GiveGoldAction.ApplyInternal</c> only debits when it is given a giver -- a hero, a party, or a
    /// settlement -- and does nothing at all when it is given none. <c>SellPrisonersAction</c> passes
    /// none: a lord selling captives at a town is paid by an abstract ransom broker who has no purse,
    /// and the denars are created on the spot. It is the same shape of faucet as the gold controller cut
    /// this morning, only smaller and quieter, and unlike the tournament's betting -- which mints a
    /// forfeit but destroys the stake first, so the pair conserves -- nothing balances it.
    ///
    /// It also recurs. Every war produces prisoners and every AI lord sells them, so this ran constantly
    /// in the background of a ledger that claimed to account for every denar.
    ///
    /// The missing giver is not really missing: the broker works the market where the sale happens, and
    /// his money is that market's money. So the settlement's citizens pay, and if they cannot pay in
    /// full the seller gets what the town could raise. That closes the circuit without inventing a new
    /// participant.
    /// </summary>
    /// <remarks>
    /// A consequence worth being clear about, because it cuts both ways depending on who is selling:
    ///
    /// <list type="bullet">
    /// <item>A LORD selling at a town is now a real drain on that town -- money leaves the market and
    /// rides away in his purse. Towns are running a surplus, so this is an outflow they need, and war
    /// prisoners are frequent enough for it to matter.</item>
    /// <item>A SETTLEMENT selling its own prisoners nets to nothing, because the pot it is credited into
    /// is the same pot its brokers paid from. That is the honest answer under this model: the money
    /// never left the town. Treating a garrison's prisoner sale as an export paid for from outside would
    /// be defensible too, but it would be a new faucet, which is the thing being removed.</item>
    /// </list>
    ///
    /// Where there is no market to charge -- a village, a hideout -- vanilla is left alone rather than
    /// having the payment silently zeroed. Failing to fund a sale must not mean cancelling it.
    /// </remarks>
    public static class RansomFunding
    {
        // The market a prisoner sale is currently being struck in, or null outside one. Set for the
        // duration of SellPrisonersAction so the gold hand-off below knows whose money it is spending.
        private static Settlement _market;

        /// <summary>
        /// Marks the settlement a prisoner sale is happening in, for the duration of the sale.
        /// </summary>
        /// <remarks>
        /// The settlement is resolved exactly as vanilla resolves it a few lines later -- the seller's
        /// own settlement if it has one, otherwise the buyer's -- so the two can never disagree about
        /// where the sale took place.
        ///
        /// Released from a FINALIZER rather than a postfix so a throwing sale cannot leave the marker
        /// standing. A stale marker would charge some unrelated settlement for the next unfunded gold
        /// hand-off anywhere in the game.
        /// </remarks>
        [HarmonyPatch(typeof(SellPrisonersAction), "ApplyInternal")]
        private static class MarkSalePatch
        {
            private static void Prefix(PartyBase sellerParty, PartyBase buyerParty)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }
                Settlement seller = (sellerParty != null) ? sellerParty.Settlement : null;
                _market = seller ?? ((buyerParty != null) ? buyerParty.Settlement : null);
            }

            private static void Finalizer()
            {
                _market = null;
            }
        }

        /// <summary>
        /// Supplies the giver vanilla left out, charging the market where the sale is being struck.
        /// </summary>
        /// <remarks>
        /// Only fires on the exact case that mints: both giver slots null AND a sale in progress. A
        /// hand-off with any giver at all already moves real money and is left completely alone, as is
        /// every unfunded hand-off elsewhere in the game -- this is not a general rule about
        /// <c>GiveGoldAction</c>, only about prisoners.
        ///
        /// <paramref name="goldAmount"/> is lowered to what the market could actually raise, which is
        /// what makes this conserved rather than merely accounted: vanilla goes on to credit the
        /// recipient with this figure, so seller and buyer move by the same number by construction. A
        /// poor town simply pays less for its prisoners.
        /// </remarks>
        [HarmonyPatch(typeof(GiveGoldAction), "ApplyInternal")]
        private static class FundRansomPatch
        {
            private static void Prefix(Hero giverHero, PartyBase giverParty, ref int goldAmount)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled
                    || _market == null
                    || giverHero != null
                    || giverParty != null
                    || goldAmount <= 0)
                {
                    return;
                }

                // No market to charge -- a village stall, a hideout. Leave vanilla exactly as it was
                // rather than zeroing the payment: failing to FUND a sale must not mean CANCELLING it.
                if (!SettlementWealth.HasCitizenPurse(_market))
                {
                    return;
                }

                goldAmount = SettlementWealth.DebitCitizens(_market, goldAmount, SettlementWealth.Source.Ransom);

                // A ransom struck in the market is a trade like any other and pays the town's fee. Levied
                // here rather than at the funnel because this leg never reaches it: the charge is made
                // straight against citizen wealth, so RouteNativeWrite -- which levies on everything that
                // does pass through it -- never sees this one.
                TradeTariff.Levy(_market, goldAmount);
            }
        }

        /// <summary>
        /// Stops the courier's ransom offer from minting the payer's money. Vanilla's
        /// <c>RansomOfferCampaignBehavior.AcceptRansomOffer</c> tops an AI payer up to the price plus a
        /// 1,000-denar reserve before paying -- coin from nowhere -- whenever the clan cannot afford it.
        /// </summary>
        /// <remarks>
        /// The ransom is clamped to what the payer holds above that same reserve, so vanilla's top-up
        /// condition (<c>Gold &lt; price + 1000</c>) is false and never fires: a poor clan simply pays less,
        /// the way a poor town pays less for the prisoners it buys (<see cref="FundRansomPatch"/>). The
        /// player as payer is left alone -- the offer already refuses him when he cannot afford it. A payer
        /// holding under the reserve pays nothing and vanilla still tops him up to the reserve; that is the
        /// one residual mint, capped at 1,000 and rare.
        /// </remarks>
        [HarmonyPatch(typeof(RansomOfferCampaignBehavior), "AcceptRansomOffer")]
        private static class CapRansomOfferPatch
        {
            private const int VanillaReserve = 1000;

            private static void Prefix(Hero ____currentRansomPayer, ref int ransomPrice)
            {
                Hero payer = ____currentRansomPayer;
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || payer == null || payer == Hero.MainHero || ransomPrice <= 0)
                {
                    return;
                }
                int affordable = payer.Gold - VanillaReserve;
                if (affordable < 0)
                {
                    affordable = 0;
                }
                if (ransomPrice > affordable)
                {
                    ransomPrice = affordable;
                }
            }
        }
    }
}
