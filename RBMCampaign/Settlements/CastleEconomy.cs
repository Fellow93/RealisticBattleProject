using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// A castle's economy, held in a SINGLE pool -- its own wealth, the keep's strongbox. A castle grows
    /// no food and runs no market -- vanilla never trades at one, and RBM's food and liquidity systems
    /// are all <c>IsTown</c>-gated -- so it has no circulating market float the way a town does, and no
    /// citizen purse to hold one. It has one balance: the money it has collected from its lands and not
    /// yet spent or handed up.
    ///
    /// That balance fills from its prosperity, which is itself pinned to the countryside it holds (see
    /// <see cref="RBMProsperityEquilibrium.CastleTargetProsperity"/>): a settled, taxable population
    /// behind a wall, whose size is what the surrounding villages support. Out of it come the garrison
    /// (<see cref="GarrisonUpkeep"/>), the militia (<see cref="MilitiaUpkeep"/>), the clerks and the
    /// walls (<see cref="AdministrativeUpkeep"/>). What is left over the hoard line is remitted to the
    /// holding lord as the castle's surplus -- see <see cref="WealthTax"/>. The lord is paid no fixed
    /// head-tax; his income is simply that the castle ran a surplus.
    ///
    /// Everything here is castle-only and additive: towns and villages never reach this code, and a
    /// castle is dropped from <see cref="SettlementWealth.HasMarket"/> so nothing tries to keep it a
    /// second (citizen) purse.
    /// </summary>
    public static class CastleEconomy
    {
        /// <summary>
        /// Daily income into a castle's wealth, per point of prosperity. Drawn straight from its
        /// prosperity because a castle has no market to earn through; this is the tax on its lands.
        /// </summary>
        public const float IncomePerProsperityPerDay = 41f;

        /// <summary>
        /// The wealth level above which a castle counts as hoarding, per point of prosperity: the
        /// surplus above this is skimmed to the holding lord each day (see <see cref="WealthTax"/>),
        /// which is the pool's ONLY drain to the owner and the one thing that keeps it from piling up
        /// without bound. It is also the level the daily income settles the pool against.
        /// </summary>
        public const float HoardThresholdPerProsperity = 200f;

        /// <summary>What a castle's wealth opens the campaign holding, per point of prosperity.</summary>
        public const float SeedPerProsperity = 100f;

        /// <summary>The castle administration's daily wage, between a town's (300) and a village's (100).</summary>
        public const int AdminDailySalary = 200;

        /// <summary>
        /// Mints a castle's daily income into its wealth. Runs before the day's upkeep and the surplus
        /// skim so those act on the post-income balance -- the castle collects first, then pays and
        /// remits.
        /// </summary>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null
                || !settlement.IsCastle || settlement.Town == null)
            {
                return;
            }

            float prosperity = settlement.Town.Prosperity;
            if (prosperity <= 0f)
            {
                return;
            }

            // Craftsman Quarters: smiths, wrights and coopers quartered inside the walls turn the same
            // lands into more money than raw dues would fetch. ×1.1/1.2/1.3 at levels 1/2/3, on the whole
            // of the castle's income, this being the one place it is worked out.
            int income = (int)(prosperity * IncomePerProsperityPerDay * BuildingEffects.CraftsmanIncomeFactor(settlement.Town));
            if (income <= 0)
            {
                return;
            }

            SettlementWealth.Credit(settlement, income, SettlementWealth.Source.CastleIncome);

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("CASTLE", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "prosperity " + (int)prosperity + "  ·  +" + income + "d income"
                    + "  ·  castle wealth now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }
        }
    }
}
