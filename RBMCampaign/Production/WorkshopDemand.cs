using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// What a town's workshops get through in a day, category by category -- the industrial half of the
    /// appetite that <see cref="CitizenDemand"/> measures for households.
    ///
    /// It exists because the days-of-supply price and the storage cap both need a daily figure to divide
    /// by, and until now RBM had one only for the shopping basket. Everything a workshop eats -- iron,
    /// planks, wool, clay, hides, flax, livestock -- was therefore left on vanilla's pricing, where the
    /// scarcity term is a ratio of a prosperity-derived demand to a supply measured in GOLD. That reads
    /// badly for exactly the goods a workshop needs: RBM prices iron ore at one denar, so five hundred
    /// units on the shelf come to five hundred of "in store value" against a demand of a hundred and
    /// fifty, and a well-stocked forge town still prices its ore near the scarcity end. Worse, the
    /// signal was one-sided in the way that matters most -- vanilla's demand comes from
    /// <c>GetEstimatedDemandForCategory</c>, which is prosperity and nothing else, so a smithy standing
    /// idle for want of iron contributed no demand at all. The one consumer who actually wanted the good
    /// was invisible to its price.
    ///
    /// The figure here is the one vanilla itself uses to project a warehouse's drawdown
    /// (<c>IWorkshopWarehouseCampaignBehavior.GetInputDailyChange</c>): for every production of every
    /// workshop in the town, its effective conversion speed times the units that recipe takes. That is
    /// potential demand rather than realised -- a shop with no iron still counts its iron -- which is
    /// the same choice <see cref="CitizenDemand.DailyUnits"/> makes for luxuries a poor town cannot
    /// afford, and for the same reason: a price is meant to say what a town WANTS, and a shortage that
    /// silenced its own demand could never be priced out of.
    /// </summary>
    /// <remarks>
    /// Measured per CATEGORY, not per item, because that is how a recipe consumes: an input is declared
    /// as an <c>ItemCategory</c> and <c>DetermineItemRosterHasSufficientInputs</c> counts any member of
    /// it. Iron is the case that forces the point -- ore, crude iron, wrought iron, iron, steel, fine
    /// steel and thamaskene steel are all <c>DefaultItemCategories.Iron</c>, and a forge will take
    /// whichever of them is on the shelf. So the stock is counted across the whole category and every
    /// member is priced off the same days figure, which is the honest reading: to the shop that eats
    /// them they are one good.
    ///
    /// Where a category is BOTH a workshop input and a household staple -- grain for the brewery, planks
    /// for the artisans -- the two appetites are added, and the category figure supersedes the per-item
    /// one. There is no double count: the basket is a shopping list in units and this is a drawdown in
    /// units, and they are separate consumers of the same shelf.
    ///
    /// Rebuilt once a campaign day per town. Workshop types change only when a shop is bought or
    /// converted, and the citizen half moves with prosperity, which drifts by a fraction of a percent a
    /// day -- so a day-stamped table is far more precision than either input carries.
    /// </remarks>
    public static class WorkshopDemand
    {
        // Per town: the day it was built, and units-per-day for each category the town's shops take as
        // an input. Only input categories appear, so a hit IS the "RBM models this good" test.
        private static readonly Dictionary<string, KeyValuePair<int, Dictionary<string, float>>> _cache =
            new Dictionary<string, KeyValuePair<int, Dictionary<string, float>>>();

        internal static void ResetForNewSession()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Units of this category the town gets through in a day -- its workshops' draw plus whatever
        /// its households buy of the goods in it -- or 0 for a category no workshop here takes.
        /// </summary>
        public static float DailyUnits(Town town, ItemCategory category)
        {
            if (town == null || category == null || !town.IsTown)
            {
                return 0f;
            }

            float units;
            return TableFor(town).TryGetValue(category.StringId, out units) ? units : 0f;
        }

        /// <summary>Every category the town's workshops take as an input, for the log.</summary>
        public static IEnumerable<string> InputCategories(Town town)
        {
            if (town == null || !town.IsTown)
            {
                return new string[0];
            }
            return TableFor(town).Keys;
        }

        /// <summary>
        /// Units of a category held in the town's market, counted across every item in it.
        /// </summary>
        /// <remarks>
        /// The whole point of counting here rather than per item: a shelf holding five thamaskene
        /// ingots and no ore is a shelf with five units of iron on it, and vanilla -- which measures the
        /// same shelf in gold -- reads it as thirteen hundred and calls the forge well supplied.
        /// </remarks>
        public static int UnitsInStore(Town town, ItemCategory category)
        {
            if (town == null || category == null)
            {
                return 0;
            }

            ItemRoster roster = town.Owner.ItemRoster;
            int held = 0;
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                ItemObject item = roster.GetItemAtIndex(i);
                if (item != null && item.GetItemCategory() == category)
                {
                    held += roster.GetElementNumber(i);
                }
            }
            return held;
        }

        private static Dictionary<string, float> TableFor(Town town)
        {
            string key = town.Settlement.StringId;
            int today = (int)CampaignTime.Now.ToDays;

            KeyValuePair<int, Dictionary<string, float>> cached;
            if (_cache.TryGetValue(key, out cached) && cached.Key == today)
            {
                return cached.Value;
            }

            Dictionary<string, float> table = Build(town);
            _cache[key] = new KeyValuePair<int, Dictionary<string, float>>(today, table);
            return table;
        }

        private static Dictionary<string, float> Build(Town town)
        {
            Dictionary<string, float> table = new Dictionary<string, float>();
            Dictionary<string, ItemCategory> categories = new Dictionary<string, ItemCategory>();

            Workshop[] shops = town.Workshops;
            if (shops != null)
            {
                foreach (Workshop shop in shops)
                {
                    if (shop == null || shop.WorkshopType == null || shop.WorkshopType.Productions == null)
                    {
                        continue;
                    }

                    foreach (WorkshopType.Production production in shop.WorkshopType.Productions)
                    {
                        if (production.Inputs == null || production.Inputs.Count == 0)
                        {
                            continue;
                        }

                        // The same figure the warehouse projection uses: cycles a day times the units a
                        // cycle takes, with buildings, policies and perks already folded in.
                        float speed = Campaign.Current.Models.WorkshopModel
                            .GetEffectiveConversionSpeedOfProduction(shop, production.ConversionSpeed, false)
                            .ResultNumber;
                        if (speed <= 0f)
                        {
                            continue;
                        }

                        foreach (var input in production.Inputs)
                        {
                            ItemCategory category = input.Item1;
                            if (category == null)
                            {
                                continue;
                            }

                            float running;
                            table.TryGetValue(category.StringId, out running);
                            table[category.StringId] = running + speed * input.Item2;
                            categories[category.StringId] = category;
                        }
                    }
                }
            }

            if (table.Count > 0)
            {
                AddHouseholdShare(town, table, categories);
            }
            return table;
        }

        /// <summary>
        /// Folds the households' own appetite into an input category they also shop from, so a good the
        /// town eats AND forges is measured against both.
        /// </summary>
        /// <remarks>
        /// Walks the basket rather than the category, because the basket is the short list -- a couple
        /// of dozen goods against every item in the game -- and it is the only side that knows which
        /// items a household actually buys.
        /// </remarks>
        private static void AddHouseholdShare(Town town, Dictionary<string, float> table,
            Dictionary<string, ItemCategory> categories)
        {
            foreach (string id in CitizenDemand.ModelledGoods)
            {
                ItemObject item = Game.Current.ObjectManager.GetObject<ItemObject>(id);
                if (item == null)
                {
                    continue;
                }

                ItemCategory category = item.GetItemCategory();
                if (category == null)
                {
                    continue;
                }

                float running;
                ItemCategory known;
                if (!categories.TryGetValue(category.StringId, out known) || known != category
                    || !table.TryGetValue(category.StringId, out running))
                {
                    continue;
                }

                table[category.StringId] = running + CitizenDemand.DailyUnits(town, id);
            }
        }
    }
}
