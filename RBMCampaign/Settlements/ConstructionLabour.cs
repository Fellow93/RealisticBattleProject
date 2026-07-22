using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Sends the money a player spends hurrying a building project to the town that does the work,
    /// instead of nowhere.
    ///
    /// Vanilla construction is not funded in gold at all: a town accrues build points out of its
    /// prosperity and workshops and spends them on a building, and no purse is ever touched. That
    /// stands. A building is paid for in the town's own labour and materials rather than out of the
    /// treasury, so nothing here charges a fief for building and nothing gates a project on what the
    /// treasury holds.
    ///
    /// The one place gold does enter construction is the owner boosting a project by hand, and vanilla
    /// destroys it: <c>BoostBuildingProcessWithGold</c> hands the difference to a null hero. The owner
    /// is buying labour, and the labourers are townspeople, so the money should reach them -- by the
    /// route it would really take. He does not pay the masons himself; he pays the settlement, and the
    /// settlement pays its workmen. So the gold goes into the treasury and straight back out as wages.
    ///
    /// That leaves the treasury exactly where it started -- this is money passing through, not income
    /// it keeps and not a cost it bears -- but it puts both halves on the ledger, so a boosted project
    /// reads as what it is: the lord funding the work, and the town disbursing it.
    /// </summary>
    public static class ConstructionLabour
    {
        /// <summary>
        /// Takes the owner's boost money into the treasury and pays it out again as wages.
        /// </summary>
        /// <remarks>
        /// Read as a difference either side of the call, and applied in BOTH directions -- which the
        /// first version did not do, and that was an exploitable faucet rather than a cosmetic gap.
        /// Vanilla refunds a lowered boost to the player out of nothing (<c>BoostBuildingProcessWithGold</c>
        /// pays a null giver), so an earlier postfix that only acted when the boost ROSE meant: raise
        /// it, the player pays X and X becomes wages in the market; lower it again, the player is handed
        /// X back from nowhere and the town keeps its X. Repeatable at will, for as much as you liked.
        ///
        /// So a refund now runs the whole chain backwards -- the wages come back out of the market,
        /// through the treasury, and out to the owner -- and the pair is a closed loop whichever way it
        /// is walked.
        ///
        /// Amounts are read from what each step could actually move rather than assumed, so a market
        /// that cannot return the full wage bill refunds only what it has instead of minting the
        /// difference.
        /// </remarks>
        [HarmonyPatch(typeof(BuildingHelper), "BoostBuildingProcessWithGold")]
        private static class BoostPaysLabourPatch
        {
            private static void Prefix(Town town, out int __state)
            {
                __state = (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null) ? -1 : town.BoostBuildingProcess;
            }

            private static void Postfix(int gold, Town town, int __state)
            {
                if (__state < 0)
                {
                    return;
                }
                int spent = gold - __state;
                if (spent == 0)
                {
                    return;
                }

                if (spent > 0)
                {
                    // Owner -> treasury -> the men who do the work.
                    SettlementWealth.Credit(town.Settlement, spent, SettlementWealth.Source.Boost);
                    int wages = SettlementWealth.Debit(town.Settlement, spent, SettlementWealth.Source.Construction);
                    SettlementWealth.CreditCitizens(town.Settlement, wages, SettlementWealth.Source.Construction);
                    return;
                }

                // The boost was lowered and vanilla has just refunded the owner out of nothing. Walk the
                // chain backwards so the money he gets back is money the town gives up.
                int returned = -spent;
                int fromMarket = SettlementWealth.DebitCitizens(town.Settlement, returned, SettlementWealth.Source.Construction);
                int toTreasury = SettlementWealth.Credit(town.Settlement, fromMarket, SettlementWealth.Source.Construction);
                SettlementWealth.Debit(town.Settlement, toTreasury, SettlementWealth.Source.Boost);
            }
        }
    }
}
