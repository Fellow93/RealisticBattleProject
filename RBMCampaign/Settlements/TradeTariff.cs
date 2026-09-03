using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// A market fee on every trade struck in a town, kept by the town itself -- and, since it replaces
    /// it, the removal of vanilla's commission.
    ///
    /// This is the treasury's own income -- the money a settlement raises AS a settlement, to spend on
    /// the things it now has to pay for: its garrison's wages, its administration, its building work.
    /// This is a small levy that stays home, and it is now the ONLY cut a town takes on a trade.
    ///
    /// A penny in the pound on the value of the goods changing hands, taken whichever way the trade runs
    /// -- a party buying from the town or selling to it both pay the fee. It comes out of the market's
    /// own money rather than off the trader, so it moves a sliver of citizen wealth into the treasury and
    /// nothing leaves the town: the market funds the institution that keeps the market. That last part is
    /// what vanilla's commission did not do, and why it could not stay.
    /// </summary>
    public static class TradeTariff
    {
        /// <summary>Fraction of a trade's value the town takes as a market fee.</summary>
        public const float TariffRate = 0.01f;

        /// <summary>
        /// Stops vanilla taking its commission on a town's counter sales.
        /// </summary>
        /// <remarks>
        /// <c>SettlementCommissionRateTown</c> is 0.7: seven denars in ten of every sale a town makes are
        /// taken off its market and handed to the owner. Vanilla can afford that because a town's gold is
        /// a float it re-floats -- nothing depends on the market still having money tomorrow. Here it is a
        /// conserved purse, so the same rate reads as a market that pays full price for what it buys and
        /// keeps three tenths of what it sells. Twenty-four logged days: every town in Calradia went from
        /// its seed to under a thousand denars, deliveries fell to a couple of units a convoy, and the
        /// food chain stopped. The commission was the largest single line on the ledger by a factor of
        /// three.
        ///
        /// Zeroing the RATIO rather than intercepting the write puts both halves out at once and leaves
        /// nothing to reconcile: vanilla computes a commission of zero, so the charge on the market
        /// becomes <c>ChangeGold(0)</c> and the credit to the owner becomes <c>+= 0</c>. Taking only the
        /// charge away would have left the owner being paid out of money that no longer left anywhere,
        /// which is the one thing this ledger exists to prevent.
        ///
        /// The owner is not left with nothing: what a town raises it now largely spends on itself -- its
        /// garrison, its militia, its administration -- which vanilla charged to the owner's purse. The
        /// tariff below is the town's cut, and the owner's town income is its tax on prosperity.
        ///
        /// Scoped to the town rate alone. <c>GetTownTaxRatio</c> has exactly one caller in the game,
        /// <c>SellItemsAction</c>, and one reader of the constant behind it, so nothing else moves. The
        /// VILLAGE rate is deliberately untouched: it is how a convoy's takings reach the owner, and
        /// <see cref="VillageHousehold"/> measures the village's own share off it.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultSettlementTaxModel), "GetTownTaxRatio")]
        private static class NoTownCommissionPatch
        {
            private static bool Prefix(ref float __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return true;
                }
                __result = 0f;
                return false;
            }
        }

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
            Levy(settlement, tradeValue, guardedTrade: false);
        }

        /// <summary>
        /// The same fee, told whether this is a GUARDED trade -- a caravan or a traveller coming through the
        /// gate, weighed and tallied by the watch, as opposed to a townsman buying bread from his neighbour
        /// or a fief buying its own bricks. Only guarded trade pays the Guard House's surcharge: a gate that
        /// searches every wagon collects on wagons, and no amount of guarding makes the baker pay more.
        /// </summary>
        public static void Levy(Settlement settlement, int tradeValue, bool guardedTrade)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null || settlement.Town == null || tradeValue <= 0)
            {
                return;
            }
            float rate = TariffRate;
            if (guardedTrade)
            {
                // Guard House: +0.3/0.6/1.0 percentage points at tiers 1/2/3.
                rate += BuildingEffects.GuardHouseTariffBonus(settlement.Town);
            }
            // Marketplace: a proper market square, weights, scales and a clerk taking the toll -- vanilla's
            // own TariffIncome effect, x1.1/1.2/1.3, on every channel including the townsmen's own trade.
            rate *= BuildingEffects.TariffFactor(settlement.Town);
            int tariff = (int)(tradeValue * rate);
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

        // There is deliberately no patch on any trading ACTION here.
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
        //
        // The player is the one trader that measurement gets wrong, and the patch below is why.

        // Depth of a player shop session in progress. While raised, the funnel routes the session's gold
        // as trade but does NOT levy on it -- the fee is taken whole, off the gross, in the postfix.
        private static int _sessionDepth;

        /// <summary>Whether a player shop session is settling, and the funnel should not levy.</summary>
        internal static bool IsSessionDeferred
        {
            get { return _sessionDepth > 0; }
        }

        /// <summary>
        /// Charges the player's visit to a market the fee on every trade struck in it, rather than on
        /// whatever was left over at the end of it.
        /// </summary>
        /// <remarks>
        /// The player does not trade through <c>SellItemsAction</c>. The shop screen accumulates the
        /// whole visit and settles it in ONE write -- <c>InventoryScreenHelper.SetGold</c> calls
        /// <c>ChangeGold(gold - Gold)</c> with the session's net -- so the funnel, which measures a trade
        /// by the gold that moved, saw a single trade worth the difference. Sell 5,000 denars of loot and
        /// buy 5,000 denars of gear on the same visit and the net is zero: two trades happened, the town
        /// took nothing, and the funnel's prefix never even ran, since it bails on a zero write. Every
        /// other trader in the game pays on each stack he moves, because vanilla settles their trades one
        /// at a time. The player alone was billed on the remainder.
        ///
        /// So the fee is taken off the visit's GROSS instead -- what was bought plus what was sold, from
        /// the screen's own transaction history, which already cancels anything the player dragged over
        /// and dragged back. The funnel's levy is deferred for the duration of the call so the net write
        /// is not charged a second time; the money still routes through it and is still booked as trade.
        ///
        /// The fee still comes out of the market's own money rather than off the player's price, exactly
        /// as it does for everyone else. This makes his visit MEASURED the same way; it does not tax him
        /// differently.
        ///
        /// The postfix mirrors vanilla's own branch for the market write -- a settlement counter, a
        /// listener, and a visit that is actually a trade -- and takes <c>__result</c> into account, so
        /// the two visits vanilla abandons (previewing an item, and not affording the bill) settle
        /// nothing here either. The depth comes down in a FINALIZER so a throwing screen cannot leave the
        /// tariff switched off for the rest of the session.
        /// </remarks>
        [HarmonyPatch(typeof(InventoryLogic), "DoneLogic")]
        private static class PlayerMarketSessionPatch
        {
            private static void Prefix()
            {
                _sessionDepth++;
            }

            private static void Postfix(InventoryLogic __instance, bool __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !__result || __instance == null
                    || !__instance.IsTrading || __instance.InventoryListener == null)
                {
                    return;
                }

                Settlement settlement = Settlement.CurrentSettlement;
                if (settlement == null || settlement.SettlementComponent == null)
                {
                    return;
                }

                // The player is a traveller trading at the gate: guarded trade.
                Levy(settlement, GrossTraded(__instance), guardedTrade: true);
            }

            private static void Finalizer()
            {
                if (_sessionDepth > 0)
                {
                    _sessionDepth--;
                }
            }
        }

        /// <summary>
        /// What changed hands over the counter this visit, both ways, at the prices the screen quoted.
        /// </summary>
        private static int GrossTraded(InventoryLogic logic)
        {
            return SumPrices(logic.GetBoughtItems()) + SumPrices(logic.GetSoldItems());
        }

        private static int SumPrices(List<(ItemRosterElement, int)> entries)
        {
            int total = 0;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    int price = entries[i].Item2;
                    total += (price < 0) ? -price : price;
                }
            }
            return total;
        }
    }
}
