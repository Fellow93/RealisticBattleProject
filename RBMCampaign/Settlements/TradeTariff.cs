using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

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

        /// <summary>
        /// Levies the fee on a completed town trade.
        /// </summary>
        /// <remarks>
        /// The value is measured in the prefix, before the goods move, as <c>number x price</c> at the
        /// pre-trade price. Vanilla re-prices each unit as the roster shifts within a big transaction,
        /// so this is a hair off the exact figure -- immaterial for a one-percent levy, and far simpler
        /// than reconstructing vanilla's per-unit loop from a postfix after the roster has already
        /// changed. The fee is applied in the postfix so it lands after vanilla has settled the trade
        /// and cannot perturb the price the trade itself used.
        /// </remarks>
        [HarmonyPatch(typeof(SellItemsAction), "ApplyInternal")]
        private static class TradeTariffPatch
        {
            private static void Prefix(PartyBase sellerParty, PartyBase buyerParty, ItemRosterElement itemRosterElement, int number, out int __state)
            {
                __state = 0;
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || number <= 0)
                {
                    return;
                }

                Town town = TownOf(sellerParty, buyerParty);
                if (town == null)
                {
                    return;
                }

                // Whoever is not the town is the party doing the trading, exactly as vanilla resolves it.
                MobileParty tradingParty = buyerParty != null ? buyerParty.MobileParty : null;
                bool isSelling = false;
                if (tradingParty == null)
                {
                    tradingParty = sellerParty != null ? sellerParty.MobileParty : null;
                    isSelling = true;
                }
                if (tradingParty == null)
                {
                    return;
                }

                int unitPrice = town.GetItemPrice(itemRosterElement.EquipmentElement, tradingParty, isSelling);
                __state = number * unitPrice;
            }

            private static void Postfix(PartyBase sellerParty, PartyBase buyerParty, int __state)
            {
                if (__state <= 0)
                {
                    return;
                }
                Town town = TownOf(sellerParty, buyerParty);
                if (town == null)
                {
                    return;
                }

                Levy(town.Settlement, __state);
            }
        }

        /// <summary>The town on the settlement side of a trade, or null when neither side is a town.</summary>
        private static Town TownOf(PartyBase sellerParty, PartyBase buyerParty)
        {
            if (sellerParty != null && sellerParty.IsSettlement && sellerParty.Settlement != null && sellerParty.Settlement.IsTown)
            {
                return sellerParty.Settlement.Town;
            }
            if (buyerParty != null && buyerParty.IsSettlement && buyerParty.Settlement != null && buyerParty.Settlement.IsTown)
            {
                return buyerParty.Settlement.Town;
            }
            return null;
        }
    }
}
