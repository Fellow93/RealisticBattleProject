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
        //
        // Keyed by the Town object rather than its id string, and read through a per-table memo keyed by
        // the ItemCategory object: DailyUnits sits under every price, price factor and storage check, and
        // hashing two strings per call was a measurable share of the caravan AI's scoring pass. The memo
        // only ever records what the id-keyed table answered, so the result is the same by construction.
        private sealed class DemandTable
        {
            public int Day;
            public Dictionary<string, float> ById;
            public readonly Dictionary<ItemCategory, float> ByRef = new Dictionary<ItemCategory, float>();
        }

        private static readonly Dictionary<Town, DemandTable> _cache = new Dictionary<Town, DemandTable>();

        // Per town: unit counts of its market roster, stamped with the roster and the VersionNo they were
        // taken at (the roster bumps it on every change). See UnitsInStore / GarmentsInStore. Main-thread
        // only, like every other cache in this file -- the price and storage paths that read it run from
        // campaign ticks, AI and UI, none of which is the parallel party tick.
        private sealed class StockCounts
        {
            public ItemRoster Roster;
            public int Version;

            // The first category asked for at this version, counted by a plain walk. A second, different
            // category at the same version builds the whole table instead -- so a roster read once
            // between every write (a town's own shopping) costs no more than the old single walk did,
            // and one read many times over (caravan scoring every category of every town) walks once.
            public bool HasFirst;
            public ItemCategory FirstCategory;
            public int FirstUnits;

            public bool CategoriesBuilt;
            public readonly Dictionary<ItemCategory, Counter> ByCategory = new Dictionary<ItemCategory, Counter>();

            public bool GarmentsBuilt;
            public int Garments;
        }

        // Mutable box so one lookup both finds and bumps a category's running count during a rebuild.
        private sealed class Counter
        {
            public int Units;
        }

        private static readonly Dictionary<Town, StockCounts> _stock = new Dictionary<Town, StockCounts>();

        internal static void ResetForNewSession()
        {
            _cache.Clear();
            _stock.Clear();
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

            DemandTable table = TableFor(town);
            float units;
            if (table.ByRef.TryGetValue(category, out units))
            {
                return units;
            }
            if (!table.ById.TryGetValue(category.StringId, out units))
            {
                units = 0f;
            }
            table.ByRef[category] = units;
            return units;
        }

        /// <summary>Every category the town's workshops take as an input, for the log.</summary>
        public static IEnumerable<string> InputCategories(Town town)
        {
            if (town == null || !town.IsTown)
            {
                return new string[0];
            }
            return TableFor(town).ById.Keys;
        }

        /// <summary>
        /// Units of a category held in the town's market, counted across every item in it.
        /// </summary>
        /// <remarks>
        /// The whole point of counting here rather than per item: a shelf holding five thamaskene
        /// ingots and no ore is a shelf with five units of iron on it, and vanilla -- which measures the
        /// same shelf in gold -- reads it as thirteen hundred and calls the forge well supplied.
        ///
        /// Served from a count of the roster taken once per roster version (see <see cref="StockCounts"/>),
        /// so the many readers between two trades -- price, price factor, storage headroom, recipe input
        /// checks -- share one walk instead of each taking its own.
        /// </remarks>
        public static int UnitsInStore(Town town, ItemCategory category)
        {
            if (town == null || category == null || town.Owner == null)
            {
                return 0;
            }

            ItemRoster roster = town.Owner.ItemRoster;
            if (roster == null)
            {
                return 0;
            }

            StockCounts counts = CountsFor(town, roster);
            if (counts.CategoriesBuilt)
            {
                Counter counter;
                return counts.ByCategory.TryGetValue(category, out counter) ? counter.Units : 0;
            }

            if (!counts.HasFirst)
            {
                counts.FirstCategory = category;
                counts.FirstUnits = CountCategory(roster, category);
                counts.HasFirst = true;
                return counts.FirstUnits;
            }
            if (counts.FirstCategory == category)
            {
                return counts.FirstUnits;
            }

            BuildCategoryCounts(counts, roster);
            Counter built;
            return counts.ByCategory.TryGetValue(category, out built) ? built.Units : 0;
        }

        /// <summary>
        /// Units of civilian clothing -- every civilian item in a worn slot, see
        /// <see cref="TownStorage.IsGarment"/> -- held in the town's market, counted once per roster
        /// version.
        /// </summary>
        internal static int GarmentsInStore(Town town)
        {
            ItemRoster roster = (town != null && town.Owner != null) ? town.Owner.ItemRoster : null;
            if (roster == null)
            {
                return 0;
            }

            StockCounts counts = CountsFor(town, roster);
            if (!counts.GarmentsBuilt)
            {
                int held = 0;
                for (int i = roster.Count - 1; i >= 0; i--)
                {
                    ItemRosterElement element = roster.GetElementCopyAtIndex(i);
                    if (TownStorage.IsGarment(element.EquipmentElement.Item))
                    {
                        held += element.Amount;
                    }
                }
                counts.Garments = held;
                counts.GarmentsBuilt = true;
            }
            return counts.Garments;
        }

        /// <summary>
        /// The town's count entry, emptied if the roster has changed (or been replaced) since it was taken.
        /// </summary>
        private static StockCounts CountsFor(Town town, ItemRoster roster)
        {
            StockCounts counts;
            if (!_stock.TryGetValue(town, out counts))
            {
                counts = new StockCounts();
                _stock[town] = counts;
            }

            int version = roster.VersionNo;
            if (!ReferenceEquals(counts.Roster, roster) || counts.Version != version)
            {
                counts.Roster = roster;
                counts.Version = version;
                counts.HasFirst = false;
                counts.FirstCategory = null;
                counts.FirstUnits = 0;
                counts.CategoriesBuilt = false;
                counts.GarmentsBuilt = false;
                counts.Garments = 0;
            }
            return counts;
        }

        /// <summary>Units of one category in the roster, by a plain walk -- the original count.</summary>
        private static int CountCategory(ItemRoster roster, ItemCategory category)
        {
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

        /// <summary>
        /// Counts every category in the roster in one walk. Counters are zeroed rather than dropped, so a
        /// category that has left the shelf reads 0 -- what a walk for it would have found.
        /// </summary>
        private static void BuildCategoryCounts(StockCounts counts, ItemRoster roster)
        {
            foreach (Counter counter in counts.ByCategory.Values)
            {
                counter.Units = 0;
            }

            // Neighbouring stacks are often the same category (a good in several qualities), so the
            // last category's counter is reused without a lookup.
            ItemCategory lastCategory = null;
            Counter lastCounter = null;
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                ItemObject item = roster.GetItemAtIndex(i);
                ItemCategory category = (item != null) ? item.GetItemCategory() : null;
                if (category == null)
                {
                    continue;
                }

                if (category != lastCategory)
                {
                    if (!counts.ByCategory.TryGetValue(category, out lastCounter))
                    {
                        lastCounter = new Counter();
                        counts.ByCategory[category] = lastCounter;
                    }
                    lastCategory = category;
                }
                lastCounter.Units += roster.GetElementNumber(i);
            }

            counts.CategoriesBuilt = true;
        }

        private static DemandTable TableFor(Town town)
        {
            int today = (int)CampaignTime.Now.ToDays;

            DemandTable cached;
            if (_cache.TryGetValue(town, out cached) && cached.Day == today)
            {
                return cached;
            }

            DemandTable table = new DemandTable { Day = today, ById = Build(town) };
            _cache[town] = table;
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

                    // The effective speed splits the shop's labour over the recipes the town can supply
                    // today, so the fewer inputs on the shelf, the faster each recipe reads. Summed over
                    // EVERY recipe that overstated the appetite exactly when inputs were short (Danustica:
                    // ~1,500 leather a day reported against ~270 if every recipe were supplied), which
                    // pinned the price of every short input at its cap and made the storage ceiling
                    // meaningless. Rescaled to the labour split over every recipe, the figure is what the
                    // shop would draw fully supplied: still potential demand, but no longer one that grows
                    // as the shortage deepens.
                    float fullySupplied = RBMConfig.RBMConfig.rbmCampaignEnabled
                        ? ArtisanOutput.FullySuppliedFactor(shop)
                        : 1f;

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
                            .ResultNumber * fullySupplied;
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
