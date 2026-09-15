using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Closes the three places where <c>SellItemsAction</c> creates or destroys a settlement's money.
    ///
    /// Vanilla settles a stall trade through <c>GiveGoldAction</c>, and picks the overload by what the
    /// trading party IS -- a caravan is paid as a party, anyone else is paid as a hero. That second
    /// assumption does not hold: villager parties, bandit parties and garrisons have no
    /// <c>LeaderHero</c>, and <c>GiveGoldAction.ApplyInternal</c> silently skips a null participant
    /// rather than failing. So the settlement's side of the trade happens and the party's side does not.
    ///
    /// <list type="bullet">
    /// <item>A leaderless party BUYING from a settlement pays nothing, and the settlement is credited
    /// out of thin air.</item>
    /// <item>A leaderless party SELLING to a settlement is paid nothing, and the settlement's money is
    /// destroyed.</item>
    /// <item>The settlement's own commission is taken out of its purse in full, but only a
    /// security-scaled fraction of it reaches the owner through <c>TradeTaxAccumulated</c>. The
    /// remainder reaches nobody. At a VILLAGE none of it does, because the accumulate step is gated on
    /// <c>Town != null</c> -- so a village stall destroys the whole of every sale.</item>
    /// </list>
    ///
    /// All three are repaired by supplying what vanilla left out rather than by rewriting the action:
    /// the missing counterparty is the trading party's own purse, and the missing commission recipient
    /// is the owner it was always meant for.
    /// </summary>
    public static class NativeTradeConservation
    {
        // The party on the non-settlement side of the trade currently being settled, or null outside
        // one. This is the counterparty vanilla forgets when it has no LeaderHero to hand.
        private static MobileParty _trader;

        // Whether the settlement is the SELLER in the trade being settled -- the only case in which a
        // commission is charged at all. See TryTakeCommission, which without this cannot tell the
        // commission apart from a town paying for what it just bought.
        private static bool _settlementIsSeller;

        /// <summary>
        /// Remembers the trading party for the duration of one stall trade.
        /// </summary>
        /// <remarks>
        /// Resolved the same way vanilla resolves it -- whichever side is not the settlement -- so the
        /// two can never disagree about who is trading. Released from a FINALIZER so a throwing trade
        /// cannot leave a stale trader to be charged for the next unfunded hand-off in the game.
        /// </remarks>
        [HarmonyPatch(typeof(SellItemsAction), "ApplyInternal")]
        private static class MarkTradePatch
        {
            private static void Prefix(PartyBase sellerParty, PartyBase buyerParty,
                ItemRosterElement itemRosterElement, ref int number)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }

                _settlementIsSeller = sellerParty != null && sellerParty.IsSettlement;

                PartyBase other = _settlementIsSeller ? buyerParty : sellerParty;
                _trader = (other != null && other.IsMobile) ? other.MobileParty : null;

                // A town buying has only so much room for any one good. Clamping the count here rather
                // than after the fact means vanilla settles the smaller trade throughout -- goods, gold,
                // commission and demand all agree, and the seller keeps what the town would not take.
                // See TownStorage.
                if (buyerParty != null && buyerParty.IsSettlement && number > 0)
                {
                    number = TownStorage.Accept(buyerParty.Settlement, itemRosterElement.EquipmentElement.Item, number);
                }
            }

            private static void Finalizer()
            {
                _trader = null;
                _settlementIsSeller = false;
            }
        }

        /// <summary>
        /// Supplies the party vanilla left out of a stall trade, on whichever side it went missing.
        /// </summary>
        /// <remarks>
        /// Deliberately narrow. It fires only inside a stall trade, only when the participant on one
        /// side is entirely absent, and only against the party already identified as the trader -- so a
        /// hand-off with a real hero on both ends is untouched, and so is every unfunded hand-off
        /// elsewhere in the game.
        ///
        /// The amount is clamped to what the party can actually pay when it is buying, and vanilla goes
        /// on to credit the settlement with the same clamped figure. A poor party simply buys less.
        /// </remarks>
        [HarmonyPatch(typeof(GiveGoldAction), "ApplyInternal")]
        private static class SupplyTraderPatch
        {
            private static void Prefix(Hero giverHero, PartyBase giverParty, Hero recipientHero,
                PartyBase recipientParty, ref int goldAmount)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || _trader == null || goldAmount <= 0)
                {
                    return;
                }

                bool giverMissing = giverHero == null && giverParty == null;
                bool recipientMissing = recipientHero == null && recipientParty == null;

                if (giverMissing && !recipientMissing)
                {
                    // The party is buying and nobody was charged. Charge it, for as much as it holds.
                    int paid = MathF.Min(_trader.PartyTradeGold, goldAmount);
                    if (paid < 0)
                    {
                        paid = 0;
                    }
                    _trader.PartyTradeGold -= paid;
                    goldAmount = paid;
                }
                else if (recipientMissing && !giverMissing)
                {
                    // The party sold and nobody was paid. The settlement has already been debited by the
                    // giver branch above it, so this money exists -- it simply had nowhere to go.
                    _trader.PartyTradeGold += goldAmount;
                }
            }
        }

        /// <summary>
        /// Sends the settlement's commission to the owner it was always meant for, instead of deleting
        /// the part of it that vanilla's security scaling shaves off.
        /// </summary>
        /// <remarks>
        /// Called from <see cref="SettlementGoldFunnel"/> rather than patched separately, so there is
        /// exactly one prefix on <c>ChangeGold</c> and no question of which runs first.
        ///
        /// Identifying the write takes BOTH the trade being in progress and the settlement being the
        /// seller. A negative write alone is not enough, and the belief that it was cost the towns their
        /// money: when a settlement BUYS, <c>GiveGoldAction</c> pays for it out of
        /// <c>SettlementComponent.ChangeGold(-goldAmount)</c> -- negative, direct, and inside the same
        /// action. Both of vanilla's buy branches do it, <c>ApplyForSettlementToParty</c> through the
        /// giver arm and <c>ApplyForSettlementToCharacter</c> by passing a negated amount to the
        /// recipient arm. Every one of those was being swallowed here and charged to the citizens as
        /// commission, so a town's purchases never reached the ledger as trade, never paid the market
        /// fee, and read as a commission many times larger than any sale could justify. The seller flag
        /// is set by <see cref="MarkTradePatch"/> from the same test vanilla itself branches on, so the
        /// two cannot disagree.
        ///
        /// The two cases differ in which half was broken:
        /// <list type="bullet">
        /// <item>A TOWN accumulates the security-scaled share correctly and merely over-charges its
        /// market for it. So only the scaled share is taken, and vanilla's own accumulate line -- which
        /// runs immediately after and is left alone -- completes the transfer. The town keeps what the
        /// lord's officials failed to collect, which is what low security ought to mean.</item>
        /// <item>A VILLAGE accumulates nothing at all, so the full commission is taken and accumulated
        /// here instead. That leaves village stall income flowing to the owner exactly as the convoy
        /// income does, rather than evaporating.</item>
        /// </list>
        /// </remarks>
        internal static bool TryTakeCommission(Settlement settlement, int changeAmount)
        {
            if (_trader == null || !_settlementIsSeller || changeAmount >= 0 || settlement == null)
            {
                return false;
            }

            int commission = -changeAmount;
            Town town = settlement.Town;

            if (town != null)
            {
                int scaled = (int)Campaign.Current.Models.SettlementTaxModel
                    .GetTownCommissionChangeBasedOnSecurity(town, commission);
                if (scaled > 0)
                {
                    SettlementWealth.DebitCitizens(settlement, scaled, SettlementWealth.Source.Commission);
                }
                // Vanilla adds the same scaled figure to TradeTaxAccumulated on the next line, so the
                // transfer is complete without anything further here.
                return true;
            }

            Village village = settlement.Village;
            if (village == null)
            {
                return false;
            }

            int taken = SettlementWealth.Debit(settlement, commission, SettlementWealth.Source.Commission);
            village.TradeTaxAccumulated += taken;
            return true;
        }
    }
}
