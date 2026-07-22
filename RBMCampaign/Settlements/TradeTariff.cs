using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// A market fee on every trade struck in a town, kept by the town itself.
    ///
    /// This is the treasury's own income -- the money a settlement raises AS a settlement, to spend on
    /// the things it now has to pay for: its garrison's wages, its administration, its building work. It
    /// is not vanilla's commission, which is a much larger cut that goes to the OWNER through
    /// <c>TradeTaxAccumulated</c> and is untouched here. This is a small levy that stays home.
    ///
    /// A penny in the pound on the value of the goods changing hands, taken whichever way the trade runs
    /// -- a party buying from the town or selling to it both pay the fee. Like vanilla's commission it
    /// comes out of the market's own money rather than off the trader, so it moves a sliver of citizen
    /// wealth into the treasury and nothing leaves the town: the market funds the institution that keeps
    /// the market.
    /// </summary>
    public static class TradeTariff
    {
        /// <summary>Fraction of a trade's value the town takes as a market fee.</summary>
        public const float TariffRate = 0.01f;

        // A day's tariff take per settlement, and the value it was drawn from, for the log. The levy
        // fires many times a day -- every trade, every consumption category -- so the log is a single
        // daily line per settlement rather than one per call, the same way COUNTER aggregates.
        private static readonly Dictionary<Settlement, int[]> _day = new Dictionary<Settlement, int[]>();

        /// <summary>Drops the previous session's tallies. Pure diagnostics, so a session hook is enough.</summary>
        public static void Reset()
        {
            _day.Clear();
        }

        /// <summary>
        /// Writes down a settlement's day of tariff income and clears it. Called from the daily
        /// settlement tick.
        /// </summary>
        public static void FlushDaily(Settlement settlement)
        {
            int[] tally;
            if (settlement == null || !_day.TryGetValue(settlement, out tally))
            {
                return;
            }
            _day.Remove(settlement);

            if (EconomyLog.IsEnabled && tally[0] > 0)
            {
                EconomyLog.Log("TARIFF", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "took " + tally[0] + "d on " + tally[1] + "d of trade"
                    + "  ·  treasury now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }
        }

        /// <summary>
        /// Takes the market fee on a trade worth <paramref name="tradeValue"/> and puts it in the town
        /// treasury. Out of the market's own money, so the town's total is unchanged and only the split
        /// between citizen wealth and treasury moves. Shared by the market-trade patch below and the
        /// villager delivery path, which reaches the market without going through
        /// <see cref="SellItemsAction"/>.
        /// </summary>
        public static void Levy(Settlement settlement, int tradeValue)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null || settlement.Town == null || tradeValue <= 0)
            {
                return;
            }
            int tariff = (int)(tradeValue * TariffRate);
            if (tariff <= 0)
            {
                return;
            }
            int taken = SettlementWealth.DebitCitizens(settlement, tariff, SettlementWealth.Source.Tariff);
            if (taken > 0)
            {
                SettlementWealth.Credit(settlement, taken, SettlementWealth.Source.Tariff);

                if (EconomyLog.IsEnabled)
                {
                    int[] tally;
                    if (!_day.TryGetValue(settlement, out tally))
                    {
                        tally = new int[2];
                        _day[settlement] = tally;
                    }
                    tally[0] += taken;
                    tally[1] += tradeValue;
                }
            }
        }

        // There is deliberately no patch here any more.
        //
        // The fee used to be hooked onto SellItemsAction, measuring the trade as number x price in a
        // prefix. That caught the ordinary market trade and nothing else -- a caravan being bought, a
        // ship repaired at a port, a tournament forfeit and a prisoner ransom all reached a town's money
        // without paying a penny of it. Adding a second hook per action would have double-charged the
        // one already covered, since SellItemsAction settles through GiveGoldAction internally.
        //
        // So the levy moved to SettlementWealth.RouteNativeWrite, the single point every native gold
        // write now passes through. That charges each trade exactly once, charges all of them, and needs
        // no maintenance when a new path appears. It is also more accurate than the old prefix, which
        // admitted to being a hair off: vanilla re-prices each unit as the roster shifts within a large
        // transaction, and the funnel sees the gold that actually moved rather than an estimate made
        // before it did.
    }
}
