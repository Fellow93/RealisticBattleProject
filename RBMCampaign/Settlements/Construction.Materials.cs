using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// What a building site takes off the market: clay and hardwood for the work itself, and the tools
    /// that wear out doing it.
    ///
    /// A fief that has stone, brick-clay and timber standing in its yards builds cheaply -- a cartload of
    /// clay is a wall's worth of work, bought once at what the market asks -- while one that has none
    /// pays for every hour in wages. This is the one thing that makes a well-supplied town, fed by
    /// villages that actually produce, build faster than a rich but bare one.
    ///
    /// Buying follows the same idiom as every other RBM consumer of a market (see
    /// <c>CitizenDemand.BuyLine</c>): the goods leave the roster at the market's own price, the purchase
    /// is registered as demand so the price responds, and the market fee is levied on what changed hands.
    /// The money comes from the construction reserve rather than the treasury -- the reserve was already
    /// drawn from the treasury when it was funded -- and lands in the townsmen's purses.
    ///
    /// A floor of <see cref="StockFloor"/> pieces is left on the shelves whatever happens: a building
    /// site is not allowed to strip the market of every last log and leave the smiths and wrights with
    /// nothing, and without it a project would sit permanently at the scarcity price.
    ///
    /// A CASTLE has no market and no citizen purse of its own, so its building is transacted at the
    /// nearest friendly town (<see cref="Construction.LabourMarket"/>): the money always lands in that
    /// town's purse and the fee is levied there. The GOODS still come off the castle's own stores first
    /// where it has any to spare -- a keep with timber in its yard uses its timber -- and off the town's
    /// shelves only when it has not; the demand is registered against whichever market they left, so the
    /// price responds where the stock actually moved.
    /// </summary>
    public static class ConstructionMaterials
    {
        /// <summary>Pieces of a good the market always keeps back from the building site.</summary>
        public const int StockFloor = 20;

        /// <summary>Points of work one load of clay -- brick, mortar, daub -- is worth.</summary>
        public const int ClayPoints = 300;

        /// <summary>Points of work one log of hardwood is worth once sawn into planks and beams.</summary>
        public const int HardwoodPoints = 50;

        public const string ClayId = "clay";
        public const string HardwoodId = "hardwood";
        public const string ToolsId = "tools";

        /// <summary>One line of a day's material buying, worked out before any of it is carried out.</summary>
        public struct Purchase
        {
            public ItemObject Item;
            public int Count;
            public int Cost;
            /// <summary>Whose shelves the goods actually left -- the builder's own, or its market town's.</summary>
            public Town From;
        }

        private static ItemObject GetItem(string id)
        {
            return Game.Current == null ? null : Game.Current.ObjectManager.GetObject<ItemObject>(id);
        }

        /// <summary>
        /// How much of the day's work the market could cover, and what it would cost. Reads the shelves
        /// and the prices; changes nothing.
        /// </summary>
        /// <param name="supplier">The town the fief transacts in -- itself, or a castle's market town.</param>
        /// <param name="maxPoints">The most work materials are allowed to cover today.</param>
        /// <param name="budget">What the construction reserve holds.</param>
        internal static List<Purchase> Plan(Town town, Town supplier, float maxPoints, int budget, out float points, out int spend)
        {
            points = 0f;
            spend = 0;
            if (town == null || town.Owner == null || supplier == null || maxPoints < 1f || budget <= 0)
            {
                return null;
            }

            List<Purchase> purchases = null;
            // Clay first: it is worth six times a log per piece, so a site short of money gets more built
            // out of the denser material.
            AddLine(town, supplier, ClayId, ClayPoints, maxPoints, ref points, ref budget, ref spend, ref purchases);
            AddLine(town, supplier, HardwoodId, HardwoodPoints, maxPoints, ref points, ref budget, ref spend, ref purchases);
            return purchases;
        }

        private static void AddLine(Town town, Town supplier, string itemId, int pointsPerPiece, float maxPoints,
            ref float points, ref int budget, ref int spend, ref List<Purchase> purchases)
        {
            float room = maxPoints - points;
            if (room < pointsPerPiece || budget <= 0)
            {
                return;
            }

            ItemObject item = GetItem(itemId);
            if (item == null)
            {
                return;
            }

            // Own stores first -- a keep with timber in its yard uses its timber -- and the market town's
            // shelves only when it has none to spare.
            Town from = town;
            int available = Available(town, item);
            if (available <= 0 && supplier != town)
            {
                from = supplier;
                available = Available(supplier, item);
            }
            if (available <= 0)
            {
                return;
            }

            int price = from.MarketData.GetPrice(item);
            if (price <= 0)
            {
                return;
            }

            int wanted = (int)(room / pointsPerPiece);
            int affordable = budget / price;
            int count = wanted;
            if (available < count)
            {
                count = available;
            }
            if (affordable < count)
            {
                count = affordable;
            }
            if (count <= 0)
            {
                return;
            }

            int cost = count * price;
            points += count * pointsPerPiece;
            budget -= cost;
            spend += cost;

            if (purchases == null)
            {
                purchases = new List<Purchase>();
            }
            purchases.Add(new Purchase { Item = item, Count = count, Cost = cost, From = from });
        }

        /// <summary>What a town has on its shelves above the floor the building site may not touch.</summary>
        private static int Available(Town town, ItemObject item)
        {
            if (town == null || town.Owner == null)
            {
                return 0;
            }
            return town.Owner.ItemRoster.GetItemNumber(item) - StockFloor;
        }

        /// <summary>
        /// Carries out a planned day of buying: the goods leave the shelves and their price leaves the
        /// reserve for the merchants who sold them.
        /// </summary>
        internal static void Execute(Settlement market, List<Purchase> purchases)
        {
            if (purchases == null)
            {
                return;
            }
            foreach (Purchase purchase in purchases)
            {
                Take(market, purchase.From, purchase.Item, purchase.Count, purchase.Cost, SettlementWealth.Source.BuildMaterials);
            }
        }

        /// <summary>
        /// Buys one load of tools off the market if there is one to spare, and reports whether it could.
        /// </summary>
        internal static bool BuyOneTool(Town town, Settlement market)
        {
            if (town == null || town.Owner == null || market == null || market.Town == null)
            {
                return false;
            }
            ItemObject item = GetItem(ToolsId);
            if (item == null)
            {
                return false;
            }
            // Own stores first, then the market town's, exactly as with clay and timber.
            Town from = (Available(town, item) > 0) ? town : market.Town;
            if (Available(from, item) <= 0)
            {
                return false;
            }
            int price = from.MarketData.GetPrice(item);
            if (price > town.BoostBuildingProcess)
            {
                // The reserve cannot afford a replacement set; the debt stands and the site works short.
                return false;
            }
            Take(market, from, item, 1, price, SettlementWealth.Source.ConstructionTools);
            town.BoostBuildingProcess -= price;
            if (town.BoostBuildingProcess < 0)
            {
                town.BoostBuildingProcess = 0;
            }
            return true;
        }

        /// <summary>
        /// The one place goods move from a market onto a building site: off the roster, price to the
        /// merchants, demand registered, fee levied.
        /// </summary>
        private static void Take(Settlement market, Town from, ItemObject item, int count, int cost, string source)
        {
            if (count <= 0 || from == null || from.Owner == null)
            {
                return;
            }
            from.Owner.ItemRoster.AddToCounts(item, -count);
            if (cost <= 0 || market == null)
            {
                return;
            }
            // Paid and taxed where the fief transacts -- its own market, or a castle's market town -- while
            // the demand is registered where the goods actually left the shelf.
            SettlementWealth.CreditCitizens(market, cost, source);
            RBMTownFoodSupply.RegisterPurchaseDemand(from.MarketData, item.ItemCategory, cost);
            TradeTariff.Levy(market, cost);
        }
    }
}
