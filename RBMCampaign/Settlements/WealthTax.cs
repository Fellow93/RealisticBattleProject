using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// A daily levy on a town's accumulated citizen wealth, paid to the lord who holds it.
    ///
    /// The fief's other income to its owner is a tax on TRADE -- it moves when goods move. This is a tax
    /// on STOCK: a small daily bite of the money standing in the town's market, whether or not anyone
    /// traded that day. It is what makes a rich town worth holding for its own sake and not only for the
    /// caravans passing through it, and -- unlike every other flow in the ledger, which keeps money
    /// circulating inside the settlement -- it is a real drain OUT of the town to the lord, the one
    /// place citizen wealth leaves the market for good.
    /// </summary>
    public static class WealthTax
    {
        /// <summary>
        /// Fraction of citizen wealth taken each day. At roughly a tenth of a tenth of a percent it
        /// compounds to about a tenth of the town's standing wealth over a year -- a wealth tax, not a
        /// confiscation, and gentle enough day to day that a town rebuilds its float between levies.
        /// </summary>
        public const float DailyRate = 0.00027f;

        /// <summary>
        /// Takes the day's levy from a town's citizen wealth and hands it to the owner.
        /// </summary>
        /// <remarks>
        /// Towns only, matching the rest of the market model; a castle holds citizen wealth but sits
        /// outside this the same way it sits outside the food and administration systems. The lord is
        /// paid whatever the market could actually cover -- a town with an empty market owes nothing --
        /// so this can never push citizen wealth below zero.
        /// </remarks>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null || !settlement.IsTown)
            {
                return;
            }

            int wealth = SettlementWealth.GetCitizenWealth(settlement);
            int levy = (int)(wealth * DailyRate);
            if (levy <= 0)
            {
                return;
            }

            int taken = SettlementWealth.DebitCitizens(settlement, levy, SettlementWealth.Source.WealthTax);
            if (taken <= 0)
            {
                return;
            }

            Hero owner = (settlement.OwnerClan != null) ? settlement.OwnerClan.Leader : null;
            if (owner != null)
            {
                owner.ChangeHeroGold(taken);
            }

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("WEALTHTAX", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "levied " + taken + "d to owner"
                    + "  ·  citizen wealth now " + SettlementWealth.GetCitizenWealth(settlement) + "d");
            }
        }
    }
}
