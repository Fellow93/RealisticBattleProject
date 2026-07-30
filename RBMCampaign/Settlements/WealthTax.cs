using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// A daily levy on a town's accumulated citizen wealth, assessed twice: once for the lord who holds
    /// the fief, and again, at its own lower rate, for the fief itself.
    ///
    /// The fief's other income to its owner is a tax on TRADE -- it moves when goods move. This is a tax
    /// on STOCK: a small daily bite of the money standing in the town's market, whether or not anyone
    /// traded that day. It is what makes a rich town worth holding for its own sake and not only for the
    /// caravans passing through it, and -- unlike every other flow in the ledger, which keeps money
    /// circulating inside the settlement -- the lord's share of it is a real drain OUT of the town, the
    /// one place citizen wealth leaves the market for good. The town's own share stays put, moving from
    /// the market's pocket into the fief's strongbox to pay the garrison, the militia and the clerks.
    /// </summary>
    public static class WealthTax
    {
        /// <summary>
        /// Fraction of citizen wealth taken each day for the owner. At roughly a tenth of a tenth of a
        /// percent it compounds to about a tenth of the town's standing wealth over a year -- a wealth
        /// tax, not a confiscation, and gentle enough day to day that a town rebuilds its float between
        /// levies.
        /// </summary>
        public const float DailyRate = 0.00027f;

        /// <summary>
        /// Fraction of citizen wealth the fief takes for its own strongbox each day, off the same balance
        /// the owner is assessed on. A little over half the lord's rate: the town keeps a smaller bite
        /// than the man it answers to.
        /// </summary>
        public const float SettlementDailyRate = 0.00014f;

        /// <summary>
        /// The line above which a market counts as hoarding: <see cref="HoardThresholdPerProsperity"/>
        /// denars of citizen wealth per point of the town's prosperity. A healthy market floats well
        /// below this, so day to day the gentle rates above are what apply; only a town that has piled
        /// up money far past what its size can justify crosses it.
        /// </summary>
        public const float HoardThresholdPerProsperity = 1000f;

        /// <summary>
        /// Fraction of citizen wealth taken each day for the owner once the market is hoarding -- a flat
        /// tenth, in place of <see cref="DailyRate"/>. A punitive bracket, not a levy: it exists to pull a
        /// runaway market back down rather than to fund the lord, and it bites hard enough to do so in days
        /// rather than years.
        /// </summary>
        public const float HoardOwnerRate = 0.10f;

        /// <summary>
        /// Fraction of citizen wealth the fief takes for its own strongbox each day once the market is
        /// hoarding, in place of <see cref="SettlementDailyRate"/> -- a flat tenth, matching the owner's,
        /// so a glutted market is drained equally into the lord's purse and the town's own.
        /// </summary>
        public const float HoardSettlementRate = 0.10f;

        /// <summary>
        /// Takes the day's two levies from a town's citizen wealth -- the owner's and the fief's own --
        /// and hands each to its collector.
        /// </summary>
        /// <remarks>
        /// Towns only, matching the rest of the market model; a castle holds citizen wealth but sits
        /// outside this the same way it sits outside the food and administration systems. Both levies are
        /// assessed against the same morning balance, but each is paid only to the extent the market can
        /// actually cover it -- a town with an empty market owes nothing -- so this can never push citizen
        /// wealth below zero. The lord is served first, as he is in every other reckoning; on a market too
        /// thin to pay both, the strongbox is what goes short.
        /// </remarks>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null || !settlement.IsTown)
            {
                return;
            }

            int wealth = SettlementWealth.GetCitizenWealth(settlement);

            // A market carrying more than 1000d per point of prosperity has hoarded far past its healthy
            // float, and the day's levies switch to a flat tenth apiece -- assessed on the whole standing
            // balance, not just the excess -- to haul it back down in days. Below the line the gentle
            // year-scale rates apply as before.
            float prosperity = (settlement.Town != null) ? settlement.Town.Prosperity : 0f;
            bool hoarding = wealth > prosperity * HoardThresholdPerProsperity;
            float ownerRate = hoarding ? HoardOwnerRate : DailyRate;
            float settlementRate = hoarding ? HoardSettlementRate : SettlementDailyRate;

            int ownerLevy = (int)(wealth * ownerRate);
            int settlementLevy = (int)(wealth * settlementRate);
            if (ownerLevy <= 0 && settlementLevy <= 0)
            {
                return;
            }

            int takenForOwner = (ownerLevy > 0)
                ? SettlementWealth.DebitCitizens(settlement, ownerLevy, SettlementWealth.Source.WealthTax)
                : 0;
            if (takenForOwner > 0)
            {
                Hero owner = (settlement.OwnerClan != null) ? settlement.OwnerClan.Leader : null;
                if (owner != null)
                {
                    owner.ChangeHeroGold(takenForOwner);
                }
            }

            int takenForSettlement = (settlementLevy > 0)
                ? SettlementWealth.DebitCitizens(settlement, settlementLevy, SettlementWealth.Source.WealthTax)
                : 0;
            if (takenForSettlement > 0)
            {
                SettlementWealth.Credit(settlement, takenForSettlement, SettlementWealth.Source.WealthTax);
            }

            if (EconomyLog.IsEnabled && (takenForOwner > 0 || takenForSettlement > 0))
            {
                EconomyLog.Log("WEALTHTAX", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    (hoarding ? "HOARDING -- " : "") + "levied " + takenForOwner + "d to owner, " + takenForSettlement + "d to treasury"
                    + "  ·  citizen wealth now " + SettlementWealth.GetCitizenWealth(settlement) + "d"
                    + "  ·  settlement wealth now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }
        }
    }
}
