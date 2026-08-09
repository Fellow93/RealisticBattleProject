using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Routes the price of a ship traded at a port through that port's market money, the same way every
    /// other trade struck in a town lands there -- both a party buying a hull from the port and one
    /// selling one to it.
    ///
    /// Vanilla's <c>ChangeShipOwnerAction.ApplyByTrade</c> settles a port trade against NOBODY. A buyer is
    /// charged and the price is paid to <c>null</c>, destroyed; a seller is paid and the price comes from
    /// <c>null</c>, minted. Either way the port -- the counterparty that actually sold or bought the ship
    /// -- is never a denar richer or poorer for it, so a shipwright town's citizen wealth never reflects
    /// the hulls it turns out or takes in. Unlike a stall trade this never touches
    /// <c>SettlementComponent.ChangeGold</c>, so the funnel behind <see cref="SettlementGoldFunnel"/>
    /// never sees it and cannot re-home the money on its own.
    ///
    /// This supplies the missing settlement side. The party's leg stays vanilla's to settle; on top of it
    /// the port's market is moved the same figure through <see cref="SettlementWealth.RouteNativeWrite"/>,
    /// so the price runs buyer -> market or market -> seller rather than to or from nowhere -- on the
    /// ledger as a trade, and paying the market fee like any other (see <see cref="TradeTariff"/>).
    /// </summary>
    /// <remarks>
    /// The value is priced in the PREFIX, before the trade, with <c>ship.Owner</c> still the seller -- the
    /// same model, arguments and moment vanilla prices it at, so the two never disagree. The sell value in
    /// particular reads <c>ship.Owner</c> internally (for its repair deduction), so it cannot be recovered
    /// in the postfix, where ownership has already moved. The money is moved in the POSTFIX, once the
    /// trade has gone through, so a throwing action moves nothing; the stash is cleared from a finalizer
    /// either way.
    ///
    /// BUYING is fully conserved -- the buyer was charged by vanilla and the port is credited the same
    /// figure. SELLING drains the port's market for what it can cover and lets the rest stand as vanilla's
    /// minted payment: a market too poor to buy the ship outright still buys it, topping up from nothing
    /// rather than short-changing the seller. Both directions go through <c>RouteNativeWrite</c>, which
    /// credits or drains a town's market (charging the fee on what actually moved), routes a village's
    /// single purse, and declines a castle -- so a non-town port simply keeps vanilla's behaviour. In play
    /// a port is always a coastal town, so the village and castle cases are theoretical.
    ///
    /// Without the Naval DLC the active cost model returns zero and the guard below bails.
    /// </remarks>
    public static class ShipTradeFunnel
    {
        // Stashed from the prefix (which prices the ship against the pre-trade state) to the postfix
        // (which moves the money once the trade has gone through), and cleared by the finalizer. A single
        // trade settles at a time and ApplyByTrade does not re-enter itself, so a flat set of statics is
        // safe -- the same pattern NativeTradeConservation uses for its trader hand-off.
        private static Settlement _port;
        private static bool _portIsSeller;  // true: port sold a ship (a buyer pays it); false: port bought one
        private static int _value;

        [HarmonyPatch(typeof(ChangeShipOwnerAction), "ApplyByTrade")]
        private static class RouteShipTradePatch
        {
            private static void Prefix(PartyBase newOwner, Ship ship)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || ship == null)
                {
                    return;
                }

                PartyBase seller = ship.Owner;

                if (seller != null && seller.IsSettlement)
                {
                    // A port SELLING a ship: someone buys from it and the price should reach its market.
                    _port = seller.Settlement;
                    _portIsSeller = true;
                }
                else if (newOwner != null && newOwner.IsSettlement && seller != null && seller.IsMobile)
                {
                    // A port BUYING a ship: someone sells to it and its market should pay.
                    _port = newOwner.Settlement;
                    _portIsSeller = false;
                }
                else
                {
                    return;
                }

                if (_port == null)
                {
                    return;
                }

                _value = (int)Campaign.Current.Models.ShipCostModel.GetShipTradeValue(ship, seller, newOwner);
            }

            private static void Postfix()
            {
                if (_port == null || _value <= 0)
                {
                    return;
                }

                // Buy: buyer -> market (credit), conserved against vanilla's charge on the buyer. Sell:
                // market -> seller (debit), the market paying what it holds and the shortfall left as
                // vanilla's mint to the seller. Either way booked as a trade and charged the market fee.
                SettlementWealth.RouteNativeWrite(_port, _portIsSeller ? _value : -_value, false);
            }

            private static void Finalizer()
            {
                _port = null;
                _portIsSeller = false;
                _value = 0;
            }
        }
    }
}
