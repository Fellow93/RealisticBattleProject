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
        /// Read as a difference either side of the call so the refund path -- lowering a boost pays the
        /// owner back -- keeps working untouched and is not credited a second time.
        ///
        /// The wage paid out is what the treasury could actually hand over, which after a credit of the
        /// same size is the whole of it. Reading the debit's return rather than assuming keeps the two
        /// halves equal even if the credit were ever clamped.
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
                if (spent <= 0)
                {
                    return;
                }

                // Owner -> treasury -> the men who do the work.
                SettlementWealth.Credit(town.Settlement, spent, SettlementWealth.Source.Boost);
                int wages = SettlementWealth.Debit(town.Settlement, spent, SettlementWealth.Source.Construction);
                SettlementWealth.CreditCitizens(town.Settlement, wages, SettlementWealth.Source.Construction);
            }
        }
    }
}
