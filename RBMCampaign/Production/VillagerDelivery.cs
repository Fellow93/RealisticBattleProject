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
    /// So the sale is reimplemented with an ordering rule and nothing else changed: food first,
    /// cheapest per unit first, then everything else in vanilla's original order. Cheapest-first
    /// matters as much as food-first, because a unit of food is a unit of food to the stock but not
    /// to the purse -- fish runs about 1,140 denars against grain's 60 for the same +1.
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

                SellCargo(settlement, villagerParty);
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

        private static void SellCargo(Settlement settlement, MobileParty villagerParty)
        {
            Town town = settlement.Town;
            ItemRoster roster = villagerParty.ItemRoster;

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

            // Food first, then cheapest per unit within it. Non-food keeps vanilla's reverse-roster
            // order, so nothing about the rest of the trade changes.
            lots.Sort(delegate (Lot a, Lot b)
            {
                if (a.IsFood != b.IsFood)
                {
                    return a.IsFood ? -1 : 1;
                }
                if (a.IsFood && a.Price != b.Price)
                {
                    return a.Price.CompareTo(b.Price);
                }
                return a.RosterOrder.CompareTo(b.RosterOrder);
            });

            int sold = 0;
            foreach (Lot lot in lots)
            {
                int affordable = MathF.Min(lot.Amount, town.Gold / lot.Price);
                if (affordable <= 0)
                {
                    // The market cannot pay. Before the cargo is turned away, the fief buys it out of
                    // the treasury -- see AdvanceForFood.
                    affordable = AdvanceForFood(settlement, lot);
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
        /// The fief buying grain from public funds because its market has run out of money, and the
        /// one thing standing between a conserved economy and a town that starves forever.
        ///
        /// The trap is that <c>town.Gold</c> is both the money the market holds and the gate on what it
        /// may buy. A town that spends its last denar cannot buy food; not buying food does not earn it
        /// anything; so nothing it can do will ever refill the purse. Vanilla never meets this because
        /// the daily controller tops the purse back up out of nothing. Once that goes, the settlement
        /// needs a second purse it can fall back on, or the first empty market is permanent.
        ///
        /// The advance is deliberately food-only. A town too poor to buy wool should go without wool --
        /// that is a market working. It is only the food gate that is a one-way door, because hunger
        /// takes prosperity with it and prosperity is what the town's income rests on.
        ///
        /// The gold moves treasury to citizens rather than paying the villagers directly, so the
        /// purchase itself stays the ordinary one below and the market ends up holding the money it
        /// needed to make it.
        /// </summary>
        private static int AdvanceForFood(Settlement settlement, Lot lot)
        {
            if (!lot.IsFood || lot.Price <= 0)
            {
                return 0;
            }

            int treasury = SettlementWealth.GetSettlementWealth(settlement);
            int affordable = MathF.Min(lot.Amount, treasury / lot.Price);
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
                    "market broke  ·  treasury advanced " + moved + "d for "
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
