using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Lets a village spend its accumulated purse in town when a convoy sets out, instead of hoarding
    /// it forever.
    ///
    /// The ledger gives a village a real purse -- it keeps a fifth of every convoy's takings (see
    /// <see cref="VillageHousehold"/>) -- but nothing ever draws it back down, so a prosperous village's
    /// gold simply climbs without limit and never re-enters the economy. Here the countryside spends:
    /// once the purse has grown past a headman's reserve (<see cref="HoardPerHearth"/> denars per point
    /// of hearth), a dispatching village buys finished goods at its market town, moving its savings into
    /// the town market and pulling wares off the town's shelves.
    ///
    /// Runs as a postfix on the same dispatch method <see cref="VillagerEscort"/> hooks, so the spend is
    /// tied to a convoy actually setting out -- the villagers carrying money to market and coming home
    /// with goods, modelled in one step. Money moves only through <see cref="SettlementWealth"/> (the
    /// <see cref="SettlementWealth.Source.VillageDemand"/> source): the village purse pays, the town
    /// market is paid, and nothing is minted or burned. The goods bought leave the town's stock and are
    /// consumed by the countryside -- a village has no inventory to hold them, and the demand they leave
    /// behind is a town the villagers keep supplied with custom.
    /// </summary>
    internal static class VillageShopping
    {
        /// <summary>
        /// Denars per point of hearth the village lets its purse reach before it starts spending the
        /// surplus. A headman's reserve: below it the village is saving, above it the money does more
        /// good circulating in the market town than sitting in the village chest.
        /// </summary>
        private const int HoardPerHearth = 50;

        /// <summary>
        /// Share of the surplus above the reserve spent on one dispatch. Fractional so the purse draws
        /// down toward the reserve over several trips rather than emptying in one, and so a village that
        /// keeps earning keeps spending.
        /// </summary>
        private const float SpendFractionOfExcess = 0.5f;

        [HarmonyPatch(typeof(VillagerCampaignBehavior), "LoadAndSendVillagerParty")]
        private static class VillageShoppingPatch
        {
            private static void Postfix(Village village, MobileParty villagerParty)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || village == null || villagerParty == null)
                {
                    return;
                }

                BuyFromTown(village, villagerParty);
            }
        }

        private static void BuyFromTown(Village village, MobileParty villagerParty)
        {
            Settlement villageSettlement = village.Settlement;
            if (villageSettlement == null)
            {
                return;
            }

            // Only the surplus over the reserve is ever spent. The reserve is guaranteed to stay behind:
            // the whole day's budget is a fraction of the excess, so no single purchase can pull the
            // purse down to it, let alone through it.
            int purse = SettlementWealth.GetSettlementWealth(villageSettlement);
            int reserve = (int)(village.Hearth * HoardPerHearth);
            int excess = purse - reserve;
            if (excess <= 0)
            {
                return;
            }
            int budget = (int)(excess * SpendFractionOfExcess);
            if (budget <= 0)
            {
                return;
            }

            // Bought at the market the village trades with -- a town, with a real stock and a purse to
            // be paid. A village bound to a castle has no market to shop in and simply keeps its savings.
            Settlement town = village.TradeBound;
            if (town == null || town.Town == null || town.ItemRoster == null
                || !SettlementWealth.HasCitizenPurse(town))
            {
                return;
            }

            // Finished goods first. Sorting the town's non-food stock by unit value descending targets
            // the town-made wares a village actually buys -- cloth, tools, wine, oil -- and leaves the
            // cheap raw goods for last, so a village does not spend its savings buying back the grain and
            // wool it just sold.
            List<Lot> lots = CollectLots(town, villagerParty);
            if (lots.Count == 0)
            {
                return;
            }
            lots.Sort(delegate (Lot a, Lot b) { return b.Element.Item.Value.CompareTo(a.Element.Item.Value); });

            int spent = 0;
            int unitsBought = 0;
            foreach (Lot lot in lots)
            {
                if (budget < lot.Price)
                {
                    // Cannot afford even one of this lot; a cheaper lot later in the list might still fit.
                    continue;
                }

                int affordable = MathF.Min(lot.Amount, budget / lot.Price);
                if (affordable <= 0)
                {
                    continue;
                }
                int cost = affordable * lot.Price;

                // Money village -> town market. Debit clamps at the purse floor of zero; the reserve
                // above guarantees this never binds, but if it somehow did, undo and stop rather than
                // buy goods the village did not pay for.
                int paid = SettlementWealth.Debit(villageSettlement, cost, SettlementWealth.Source.VillageDemand);
                if (paid < cost)
                {
                    if (paid > 0)
                    {
                        SettlementWealth.Credit(villageSettlement, paid, SettlementWealth.Source.VillageDemand);
                    }
                    break;
                }

                SettlementWealth.CreditCitizens(town, cost, SettlementWealth.Source.VillageDemand);
                // The wares leave the shelf, consumed by the countryside -- a village holds no inventory.
                town.ItemRoster.AddToCounts(lot.Element, -affordable);

                budget -= cost;
                spent += cost;
                unitsBought += affordable;
                if (budget <= 0)
                {
                    break;
                }
            }

            if (spent <= 0)
            {
                return;
            }

            // The fief's cut on the trade struck at its market, the same fee a caravan or the player pays
            // and the sell leg levies by hand -- a villager party reaches the market without going through
            // SellItemsAction, so the tariff patch there never sees this. See VillagerDelivery / TradeTariff.
            TradeTariff.Levy(town, spent);

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("VILLAGEBUY",
                    villageSettlement.Name != null ? villageSettlement.Name.ToString() : villageSettlement.StringId,
                    "at " + (town.Name != null ? town.Name.ToString() : town.StringId)
                    + "  ·  bought " + unitsBought + " units for " + spent + "d"
                    + "  ·  purse " + purse + " → " + SettlementWealth.GetSettlementWealth(villageSettlement)
                    + "  (reserve " + reserve + ")");
            }
        }

        /// <summary>A stack of town stock priced for the village to buy, fixed before any removal.</summary>
        private struct Lot
        {
            public EquipmentElement Element;
            public int Amount;
            public int Price;
        }

        /// <summary>
        /// The town's non-food market stock, each lot priced at the town's ask. Copies are taken so
        /// later removals (by element, not index) cannot disturb this plan -- the same idiom the sell
        /// leg uses. Food is left alone: a village grows its own, and buying rations back is perverse.
        /// </summary>
        private static List<Lot> CollectLots(Settlement town, MobileParty villagerParty)
        {
            List<Lot> lots = new List<Lot>();
            ItemRoster roster = town.ItemRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                ItemRosterElement element = roster.GetElementCopyAtIndex(i);
                ItemObject item = element.EquipmentElement.Item;
                if (item == null)
                {
                    continue;
                }
                int amount = element.Amount;
                if (amount <= 0)
                {
                    continue;
                }
                if (item.ItemCategory != null
                    && item.ItemCategory.Properties == ItemCategory.Property.BonusToFoodStores)
                {
                    continue;
                }
                int price = town.Town.GetItemPrice(element.EquipmentElement, villagerParty, isSelling: false);
                if (price <= 0)
                {
                    continue;
                }
                lots.Add(new Lot { Element = element.EquipmentElement, Amount = amount, Price = price });
            }
            return lots;
        }
    }
}
