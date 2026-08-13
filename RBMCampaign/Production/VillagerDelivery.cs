using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Owns the delivery leg of the village-to-town chain: a villager party arriving at its
    /// trade-bound town, selling what it carried, and the line in the economy log that records it.
    ///
    /// Vanilla's <c>SellGoodsForTradeAction</c> buys <c>min(units, town.Gold / price)</c> of each
    /// stack while walking the villager's roster BACKWARDS, with no reference to demand. The order is
    /// therefore an accident of roster layout, and the purse is spent on whatever the tail of the
    /// cargo happens to hold. Measured over one campaign day, towns spent 590,000 denars and landed
    /// 1,100 units of food -- 538 denars per unit, against grain at 60 -- while 6,300 units of food a
    /// day were refused for lack of gold. The whole map wants 2,400 units a day, which as grain costs
    /// 150,000: the gold was never short, it was buying wool at 800 a unit and warhorses at 10,000.
    ///
    /// So the sale is reimplemented with two rules and nothing else touched. First, an ordering:
    /// food first, then everything cheapest-per-unit first, so a purse that empties mid-sale has
    /// spent on grain before fish and staples before finery rather than on whatever the tail of the
    /// cargo happened to hold. Cheapest-first matters as much as food-first, because a unit of food is
    /// a unit of food to the stock but not to the purse -- fish runs about 1,140 denars against
    /// grain's 60 for the same +1.
    ///
    /// Second, a reserve. Food may spend the market down to its last denar (and, when the granary has
    /// run low, past it out of the treasury); everything else stops once the purse falls to a fraction
    /// of what a town this size should hold. So a poor town takes cheap staples and leaves costly
    /// imports on the cart, and a broke one buys nothing but food -- rather than spending its last
    /// gold on wool it cannot resell and bleeding it into the countryside for good. This is the leg
    /// that lets a low-traffic market hold what little it earns instead of drifting to zero every
    /// convoy. See <see cref="NonFoodReserveShare"/>.
    ///
    /// Two vanilla details preserved deliberately:
    /// <list type="bullet">
    /// <item>The party keeps back half a man's worth of the cheapest pack animal per member, so it
    /// can carry next season's load.</item>
    /// <item>Only TOWNS buy -- <c>VillagerCampaignBehavior.OnSettlementEntered</c> calls the sale
    /// under <c>settlement.IsTown</c>, so villagers bound to a castle sell nothing at all.</item>
    /// </list>
    /// </summary>
    public static class VillagerDelivery
    {
        /// <summary>State carried from prefix to postfix -- the before picture of both sides of the trade.</summary>
        private class Snapshot
        {
            public int TownGold;
            public int TownFood;
            public int PartyUnits;
            public int PartyFood;
            // Reserve accounting, filled by SellCargo: the floor the market kept in hand, and the
            // non-food units it therefore left on the cart that it could otherwise have paid for.
            public int ReserveFloor;
            public int HeldNonFood;
        }

        [HarmonyPatch(typeof(SellGoodsForTradeAction), "ApplyByVillagerTrade")]
        private static class VillagerTradePatch
        {
            private static bool Prefix(Settlement settlement, MobileParty villagerParty, out Snapshot __state)
            {
                __state = null;
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null || settlement.Town == null || villagerParty == null)
                {
                    return true;
                }

                if (EconomyLog.IsEnabled)
                {
                    CountRoster(villagerParty.ItemRoster, out int units, out int food);
                    __state = new Snapshot
                    {
                        TownGold = settlement.Town.Gold,
                        TownFood = RBMTownFoodSupply.FoodUnitsInMarket(settlement.Town),
                        PartyUnits = units,
                        PartyFood = food
                    };
                }

                SellCargo(settlement, villagerParty, __state);
                return false;
            }

            private static void Postfix(Settlement settlement, MobileParty villagerParty, Snapshot __state)
            {
                if (__state == null)
                {
                    return;
                }

                CountRoster(villagerParty.ItemRoster, out int unitsAfter, out int foodAfter);
                int goldAfter = settlement.Town.Gold;

                string village = (villagerParty.HomeSettlement != null && villagerParty.HomeSettlement.Name != null)
                    ? villagerParty.HomeSettlement.Name.ToString()
                    : villagerParty.Name?.ToString() ?? "?";

                // Food the town could not buy is the diagnostic: cargo the villagers would gladly have
                // sold, left behind because the purse ran out mid-transaction.
                string verdict = "";
                if (foodAfter > 0)
                {
                    verdict = (goldAfter <= 0) ? "  ·  BROKE, " + foodAfter + " food unsold"
                                               : "  ·  " + foodAfter + " food unsold";
                }

                // Why a solvent market still bought little: the reserve held its purse above the floor
                // and left non-food on the cart. Only shown when it actually bit, so a full sale stays clean.
                if (__state.HeldNonFood > 0)
                {
                    verdict += "  ·  reserve " + __state.ReserveFloor + "d held " + __state.HeldNonFood + " non-food";
                }

                EconomyLog.Log("DELIVER", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "from " + EconomyLog.Clip(village, 18)
                    + "  ·  sold " + (__state.PartyUnits - unitsAfter) + " of " + __state.PartyUnits + " units for " + (__state.TownGold - goldAfter) + "d"
                    + "  ·  food +" + (__state.PartyFood - foodAfter) + " (market " + __state.TownFood + " → " + RBMTownFoodSupply.FoodUnitsInMarket(settlement.Town) + ")"
                    + "  ·  town gold " + __state.TownGold + " → " + goldAfter
                    + verdict);
            }
        }

        /// <summary>
        /// A stack of cargo priced for this sale, kept apart from the roster so removals cannot
        /// disturb the iteration. Vanilla walks the roster backwards precisely so that removing an
        /// element never shifts an index it has yet to visit; selling in a different order than the
        /// roster is stored in gives up that guarantee, so the plan is fixed before the first sale
        /// and executed against EquipmentElement rather than index.
        /// </summary>
        private struct Lot
        {
            public EquipmentElement Element;
            public int Amount;
            public int Price;
            public bool IsFood;
            public int RosterOrder;
        }

        private static void SellCargo(Settlement settlement, MobileParty villagerParty, Snapshot state)
        {
            Town town = settlement.Town;
            ItemRoster roster = villagerParty.ItemRoster;

            // The cash the market will not spend on anything but food. Sized off the same
            // prosperity-implied worth the LIQUID line measures drift against -- the best yardstick
            // there is for what a town this size should hold -- so it scales with the town and moves
            // with it. See NonFoodReserveShare.
            float countryside = RBMProsperityEquilibrium.TreasuryProsperity(town) * 12f;
            float target = 10000f + countryside + TroopMarketFeedback.TreasuryBonus(town, countryside);
            int reserveFloor = MathF.Max(0, MathF.Round(NonFoodReserveShare * target));
            if (state != null)
            {
                state.ReserveFloor = reserveFloor;
            }

            // Vanilla reserves the cheapest pack animal in the cargo, half a head per party member.
            ItemObject reservedPackAnimal = null;
            int cheapestPackAnimal = 10000;
            for (int i = 0; i < roster.Count; i++)
            {
                ItemObject item = roster.GetElementCopyAtIndex(i).EquipmentElement.Item;
                if (item != null && item.ItemCategory == DefaultItemCategories.PackAnimal && item.Value < cheapestPackAnimal)
                {
                    cheapestPackAnimal = item.Value;
                    reservedPackAnimal = item;
                }
            }
            int packAnimalsKept = (int)(0.5f * villagerParty.MemberRoster.TotalManCount);

            List<Lot> lots = new List<Lot>();
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement element = roster.GetElementCopyAtIndex(i);
                ItemObject item = element.EquipmentElement.Item;
                if (item == null)
                {
                    continue;
                }

                int amount = roster.GetElementNumber(i);
                if (item == reservedPackAnimal)
                {
                    amount -= packAnimalsKept;
                }
                if (amount <= 0)
                {
                    continue;
                }

                lots.Add(new Lot
                {
                    Element = element.EquipmentElement,
                    Amount = amount,
                    Price = town.GetItemPrice(element.EquipmentElement, villagerParty, isSelling: true),
                    IsFood = item.ItemCategory.Properties == ItemCategory.Property.BonusToFoodStores,
                    RosterOrder = lots.Count
                });
            }

            // Food first; then cheapest per unit first, food and non-food alike. When the purse runs
            // out mid-sale it has spent on the cheapest cargo -- grain before fish, staples before
            // finery -- so the costly goods are the ones left unbought. Roster order only breaks ties.
            lots.Sort(delegate (Lot a, Lot b)
            {
                if (a.IsFood != b.IsFood)
                {
                    return a.IsFood ? -1 : 1;
                }
                if (a.Price != b.Price)
                {
                    return a.Price.CompareTo(b.Price);
                }
                return a.RosterOrder.CompareTo(b.RosterOrder);
            });

            int sold = 0;
            foreach (Lot lot in lots)
            {
                // What the town has ROOM for, before what it can pay for. A store already full of this
                // good buys none of it however deep the purse is -- see TownStorage. The cargo stays on
                // the cart.
                int wanted = TownStorage.Accept(settlement, lot.Element.Item, lot.Amount);
                if (wanted <= 0)
                {
                    continue;
                }

                // Food spends the market down to its last denar; everything else must leave the reserve
                // untouched. A town at or under the reserve buys no imports at all -- which is what puts
                // a broke market on food alone and lets it hold the little it has rather than bleed its
                // last gold into the countryside for goods it cannot resell.
                int spendable = lot.IsFood ? town.Gold : MathF.Max(0, town.Gold - reserveFloor);
                int affordable = MathF.Min(wanted, spendable / lot.Price);

                // Diagnostic: non-food this lot would have bought had the reserve not stood in the way.
                // Room is already out of the picture -- a store-full lot never reaches here (wanted <= 0)
                // -- so what is left is purely the reserve's doing, and its sum explains a small sale.
                if (state != null && !lot.IsFood && lot.Price > 0)
                {
                    int withoutReserve = MathF.Min(wanted, town.Gold / lot.Price);
                    if (withoutReserve > affordable)
                    {
                        state.HeldNonFood += withoutReserve - affordable;
                    }
                }

                if (affordable <= 0 && lot.IsFood)
                {
                    // The market cannot pay. In the ordinary case that is the end of it -- the fief does
                    // not buy the townspeople their groceries, and the cargo goes home on the cart. It
                    // is only when the granary itself has run low that the town steps in and buys the
                    // food on its own account, which is a different thing from subsidising a shopper.
                    // See AdvanceForFood.
                    //
                    // Sized off the whole lot, so it has to respect the store's room as well -- an
                    // advance is the fief finding the money, not the granary finding the space.
                    affordable = MathF.Min(AdvanceForFood(settlement, lot), wanted);
                }
                if (affordable <= 0)
                {
                    continue;
                }

                villagerParty.PartyTradeGold += affordable * lot.Price;
                SettlementWealth.DebitCitizens(settlement, affordable * lot.Price, SettlementWealth.Source.Delivery);
                settlement.ItemRoster.AddToCounts(lot.Element, affordable);
                roster.AddToCounts(lot.Element, -affordable);
                sold += affordable * lot.Price;
                // Ledger: gold value villagers delivered into this town today (incl. treasury-advanced food),
                // plus the per-good breakdown behind that gold for the Delivered column's hover.
                RBMTownLedger.AddVillagerBrought(settlement, affordable * lot.Price);
                RBMTownLedger.AddVillagerGood(settlement, lot.Element.Item, affordable, affordable * lot.Price);
            }

            // The market fee on the sale, the same one a caravan or the player pays -- a villager party
            // reaches the market without going through SellItemsAction, so the tariff patch there never
            // sees this and it has to be charged by hand. See TradeTariff.
            TradeTariff.Levy(settlement, sold);

            // The convoy carries only its takings home now: nothing is bought here. What the village
            // keeps and what its owner is owed is settled when it reaches its own gate -- see the
            // homecoming in VillageHousehold.
        }

        /// <summary>
        /// Share of a town's granary below which the fief will buy food out of its own treasury because
        /// the market has run out of money to buy it with.
        ///
        /// A quarter, so that the town is provisioning a granary rather than doing the townspeople's
        /// shopping. Above the mark a broke market simply turns the cargo away: the food it failed to
        /// buy would have been eaten by citizens who could not pay for it, and a treasury that bought it
        /// for them is the lord's purse feeding the town -- exactly the manufactured money the ledger
        /// exists to remove. Below it the same purchase is a fief keeping a stocked granary, which is
        /// its own business and has always been paid for out of public funds.
        ///
        /// It also bounds the exposure. The old ungated advance fired on any empty purse whatever the
        /// granary held, so treasuries poured money into markets that were merely illiquid -- 3,867
        /// denars a day at the old price ceiling, propping up towns that had food (see
        /// <c>RBMMarketPrices.MaxFactor</c>).
        /// </summary>
        private const float DearthStockShare = 0.25f;

        /// <summary>
        /// Fraction of a town's prosperity-implied worth that its market keeps in hand rather than spend
        /// on anything but food. Below this line a convoy sells the town only food; above it the town
        /// spends the surplus over the line on its cheapest cargo first, so a little slack buys staples
        /// and a lot buys finery too.
        ///
        /// The worth is the figure the LIQUID drift line measures against -- <c>10000 + countryside +
        /// treasury bonus</c> -- so the reserve is a tenth of what vanilla would have pinned the town to,
        /// and scales with the town rather than being a flat denar count that would be a fortune to a
        /// hamlet and pocket change to a capital.
        ///
        /// A tenth is deliberately a floor and not a target. It is not enough to starve a healthy town's
        /// workshops of imported inputs -- a town holding several times its reserve buys the whole cargo
        /// as before -- but it is enough that a market which has stopped being paid stops handing its
        /// last gold to the countryside for wool and pottery it has no one to resell to. That trade was
        /// the largest single drain on the poorest towns and the reason a low-traffic fief sat at zero
        /// however much its villages delivered; the reserve is what lets such a town accumulate back
        /// toward this line instead of bleeding to nothing every convoy. Raising it lifts where broke
        /// towns settle and makes them fussier buyers; lowering it lets them spend closer to empty.
        /// </summary>
        private const float NonFoodReserveShare = 0.1f;

        /// <summary>
        /// The fief buying grain from public funds because its granary has run low and its market has
        /// run out of money, and the one thing standing between a conserved economy and a town that
        /// starves forever.
        ///
        /// The trap is that <c>town.Gold</c> is both the money the market holds and the gate on what it
        /// may buy. A town that spends its last denar cannot buy food; not buying food does not earn it
        /// anything; so nothing it can do will ever refill the purse. Vanilla never meets this because
        /// the daily controller tops the purse back up out of nothing. Once that goes, the settlement
        /// needs a second purse it can fall back on, or the first empty market is permanent.
        ///
        /// Two limits keep it from becoming the old blanket subsidy:
        /// <list type="bullet">
        /// <item>Food only. A town too poor to buy wool should go without wool -- that is a market
        /// working. It is only the food gate that is a one-way door, because hunger takes prosperity
        /// with it and prosperity is what the town's income rests on.</item>
        /// <item>Only up to <see cref="DearthStockShare"/> of the granary, and only as far as that mark.
        /// The fief fills the shortfall and stops; everything above it is the market's own trade, paid
        /// for with the market's own money or not at all. Re-read per lot off the live roster, so a
        /// large convoy stops being advanced for the moment the granary crosses the line.</item>
        /// </list>
        ///
        /// The gold moves treasury to citizens rather than paying the villagers directly, so the
        /// purchase itself stays the ordinary one above and the market ends up holding the money it
        /// needed to make it.
        /// </summary>
        private static int AdvanceForFood(Settlement settlement, Lot lot)
        {
            Town town = settlement.Town;
            if (!lot.IsFood || lot.Price <= 0 || town == null)
            {
                return 0;
            }

            // How far the granary is under the low-water mark. Measured off the market roster rather
            // than town.FoodStocks so the reading is the raw stock, not the clamped figure the UI shows.
            int lowWaterMark = MathF.Round(town.FoodStocksUpperLimit() * DearthStockShare);
            int shortfall = lowWaterMark - RBMTownFoodSupply.FoodUnitsInMarket(town);
            if (shortfall <= 0)
            {
                return 0;
            }

            int treasury = SettlementWealth.GetSettlementWealth(settlement);
            int affordable = MathF.Min(MathF.Min(lot.Amount, shortfall), treasury / lot.Price);
            if (affordable <= 0)
            {
                return 0;
            }

            int advance = affordable * lot.Price;
            int moved = SettlementWealth.Debit(settlement, advance, SettlementWealth.Source.Dearth);
            if (moved <= 0)
            {
                return 0;
            }
            SettlementWealth.CreditCitizens(settlement, moved, SettlementWealth.Source.Dearth);

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("DEARTH", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "market broke at " + RBMTownFoodSupply.FoodUnitsInMarket(town) + "/" + lowWaterMark
                    + " food  ·  treasury advanced " + moved + "d for "
                    + (moved / lot.Price) + " units of " + lot.Element.Item.Name
                    + "  ·  treasury now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }

            return moved / lot.Price;
        }

        /// <summary>Total units in a roster, and how many of them are food goods.</summary>
        private static void CountRoster(ItemRoster roster, out int units, out int food)
        {
            units = 0;
            food = 0;
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement element = roster.GetElementCopyAtIndex(i);
                ItemObject item = element.EquipmentElement.Item;
                if (item == null)
                {
                    continue;
                }

                units += element.Amount;
                if (item.ItemCategory.Properties == ItemCategory.Property.BonusToFoodStores)
                {
                    food += element.Amount;
                }
            }
        }
    }
}
