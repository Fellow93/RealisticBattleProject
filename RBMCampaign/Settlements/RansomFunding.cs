using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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

        // The gear-value slice of the ransom being struck, in gold, summed over every prisoner in the
        // sale. RBMLedger's RansomGearValue folds each captive's kit into what he ransoms for; that slice
        // is paid for by the town RECEIVING the prisoner and his gear, so it is minted to the seller
        // rather than drawn from citizen wealth like the man himself. Held here so the gold hand-off below
        // can tell the two apart and charge citizens only for the base ransom.
        private static int _pendingGearValue;

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
            private static void Prefix(PartyBase sellerParty, PartyBase buyerParty, TroopRoster prisoners)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }
                Settlement seller = (sellerParty != null) ? sellerParty.Settlement : null;
                _market = seller ?? ((buyerParty != null) ? buyerParty.Settlement : null);
                _pendingGearValue = SumGearValue(prisoners);
            }

            private static void Finalizer()
            {
                _market = null;
                _pendingGearValue = 0;
            }

            /// <summary>
            /// The gear-value slice of a whole prisoner sale: each captive's kit worth (the same figure
            /// <see cref="RansomGearValue"/> folds into his ransom) times how many of him are being sold.
            /// </summary>
            /// <remarks>
            /// Mirrors exactly what vanilla's own loop adds to the payout -- every prisoner but the main
            /// hero contributes -- so this sum is precisely the gear portion of the gold that will change
            /// hands, and the base ransom is whatever is left when it is subtracted off.
            /// </remarks>
            private static int SumGearValue(TroopRoster prisoners)
            {
                if (prisoners == null)
                {
                    return 0;
                }
                int total = 0;
                foreach (TroopRosterElement element in prisoners.GetTroopRoster())
                {
                    CharacterObject character = element.Character;
                    if (character == null || character == Hero.MainHero.CharacterObject)
                    {
                        continue;
                    }
                    total += element.Number * SpoilsPool.GetEquipmentValueWithMount(character);
                }
                return total;
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

                // Split the ransom into the man and his kit. The town pays for the man out of its citizen
                // wealth -- the ransom broker's money is the market's money -- but the GEAR it takes off
                // the prisoner is a real good it now owns, so its worth is minted to the seller rather than
                // drained from citizens: the town gets the prisoner and his gear, the party gets the gear's
                // value, and nobody's purse is emptied to pay for goods that arrived with the prisoner.
                int gearMint = _pendingGearValue;
                if (gearMint > goldAmount)
                {
                    gearMint = goldAmount;
                }
                int baseAmount = goldAmount - gearMint;

                int chargedBase = 0;
                if (baseAmount > 0)
                {
                    chargedBase = SettlementWealth.DebitCitizens(_market, baseAmount, SettlementWealth.Source.Ransom);

                    // The man-ransom is a trade struck in the market and pays the town's fee. Levied here
                    // rather than at the funnel because this leg never reaches it: the charge is made
                    // straight against citizen wealth, so RouteNativeWrite -- which levies on everything
                    // that does pass through it -- never sees this one. The minted gear slice is left out:
                    // no citizen paid it, so there is nothing of theirs to take a fee from.
                    TradeTariff.Levy(_market, chargedBase);
                }

                // Seller is paid what the citizens could raise for the man plus the full value of the kit.
                // A poor town still buys the prisoner off you for his gear even when it cannot afford him.
                goldAmount = chargedBase + gearMint;

                // Spent -- so a second unfunded hand-off in the same sale (there is none today) cannot
                // mint the gear a second time before the finalizer clears it.
                _pendingGearValue = 0;
            }
        }
    }
}
