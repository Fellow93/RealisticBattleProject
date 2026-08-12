using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Prices a good by how long the town's stock of it would last, instead of by how much money is
    /// sitting in it.
    ///
    /// Vanilla's scarcity term is
    /// <c>(demand / (0.1*supply + 0.04*inStoreValue + 2))^0.6</c>, and both <c>supply</c> and
    /// <c>inStoreValue</c> are GOLD VALUES. That measures the wrong thing twice over. A market holding a
    /// hundred units of a three-hundred-denar good reads as better supplied than one holding a thousand
    /// units of a twenty-denar good, though the second town can eat for a month and the first cannot eat
    /// at all. And because the denominator is a value, a good getting dearer makes itself look more
    /// abundant, which damps the very signal it is supposed to carry.
    ///
    /// What a market actually feels is days. Thirty days of grain is comfort, three days is a crisis,
    /// and that is true whatever a sack costs. RBM now knows exactly how much of each good a town gets
    /// through in a day -- see <see cref="CitizenDemand.DailyUnits"/> -- so the honest measure is
    /// available for the first time:
    ///
    /// <code>
    /// cap      = MarkupCap(good)                              // 2x basic, 4x medium, 8x luxury
    /// exponent = ln(cap) / ln(AbundantDays / CeilingDays)     // derived, so the curve fits the cap
    /// factor   = clamp( (AbundantDays / days) ^ exponent , 1 , cap )
    /// </code>
    ///
    /// The ceiling is the one thing that is NOT uniform across goods, and it does not merely clip a
    /// shared curve -- it RESHAPES it. The exponent is derived from the cap so that every tier reads
    /// 1.0x at a full store and reaches its own ceiling at the same near-empty point, which makes the
    /// cap a statement about a good's price ELASTICITY rather than its base value: a staple people must
    /// buy and every region grows ramps gently and cannot spike far however bare the shelf (basic, 2x);
    /// a semi-processed or regional good answers scarcity harder (medium, 4x); a status luxury commands
    /// whatever the shortage will bear (luxury, 8x). Clipping instead of reshaping would have pinned a
    /// basic good at 2x from a week's stock down to empty -- a flat tax, not a scarcity signal. See
    /// <see cref="MarkupCap(ItemObject)"/> and <see cref="ScarcityFactor(float, float)"/>.
    ///
    /// The lower clamp is 1 rather than a discount, because an item's <c>Value</c> is already the price
    /// of that good where it is plentiful -- <see cref="TradeGoodValues"/> sets it from a table of
    /// historical denar prices. It is a floor, not an average, so everything a market does to it is a
    /// markup and a full store simply pays the floor.
    ///
    /// This also closes the one-sided signal by construction. Under the old formula a good nobody could
    /// buy registered no demand at all, because demand was fed only by COMPLETED purchases -- so beer
    /// that was never in stock never told anyone it was wanted, and the brewery that would have made it
    /// never saw a price worth making it for. Here an empty shelf IS zero days, which is the maximum
    /// price by definition. Absence speaks.
    /// </summary>
    /// <remarks>
    /// Applied as a REPLACEMENT of vanilla's price, not a ratio against it. The postfix throws away
    /// what the price model returned and rebuilds the number from RBM's own terms: the good's base
    /// value, this scarcity factor, and nothing else vanilla decided -- its supply/demand term, its war
    /// markup, and its village/caravan spreads are all gone. The ONE vanilla contribution kept is the
    /// player's own trade spread, their Trade skill and the goods' trade perks, recovered by asking
    /// vanilla's <c>GetTradePenalty</c> with no merchant (which is what the war markup and every
    /// location spread key off, so a null merchant strips them and leaves only the party's own margin).
    ///
    /// Goods RBM does not model the consumption of are left entirely on vanilla: tools, war gear,
    /// horses, and anything else with no measured sink. There is no daily figure to divide by, so there
    /// are no days to speak of, and guessing one would misprice it on no evidence. The same rule governs
    /// <see cref="TownStorage"/>, deliberately -- one boundary, drawn in one place, between what RBM
    /// models and what it leaves alone.
    ///
    /// That boundary has two sides to it. <see cref="CitizenDemand"/> measures what the households get
    /// through and <see cref="WorkshopDemand"/> what the workshops do, so a raw material with a forge or
    /// a loom to eat it -- iron, planks, wool, clay, hides, flax -- is now inside the model on the
    /// industrial side even though no household ever buys a sack of it. Those are priced by their whole
    /// CATEGORY rather than per item, matching how a recipe consumes them.
    /// </remarks>
    public static class RBMMarketPrices
    {
        /// <summary>
        /// Days of stock at which a good sells for exactly its base value -- which is the CHEAPEST it
        /// ever sells for, not its average.
        ///
        /// An item's <c>Value</c> is not a market price and must not be treated as one. RBM sets it
        /// from a table of historical denar prices (see <see cref="TradeGoodValues"/>: grain 60, beer
        /// 220, meat 200, wine 1330), and those are floor prices -- what a good is worth where it is
        /// made, in a year when there is plenty of it. Everything a market does to that figure is a
        /// markup, so the scarcity term is clamped at 1 below and never discounts.
        ///
        /// Fifteen days is a comfortably stocked market: a town holding that much or more (up to a full
        /// <see cref="TownStorage.StorageDays"/> granary) pays the floor and nobody profits carrying
        /// grain to it. Set below the warehouse cap on purpose, so prices bottom out at an ordinary
        /// stock rather than only at a brim-full store no supply chain reliably reaches.
        /// </summary>
        public const float AbundantDays = 15f;

        /// <summary>
        /// Floor on the days figure, purely to keep the division finite. It is NOT the ceiling --
        /// <see cref="MaxFactor"/> is, and this is deliberately far below where that clamp bites so the
        /// two cannot argue.
        ///
        /// They did argue, and it cost a run. At 0.5 this capped the ratio at 120, so the real ceiling
        /// was <c>120^0.6 = 17.68x</c> while <c>MaxFactor</c> sat unreachable at 20 -- the number named
        /// for the job was doing none of it, and the effective ceiling was an accident of a constant
        /// meant only to guard an edge case.
        /// </summary>
        public const float FloorDays = 0.1f;

        /// <summary>
        /// Floor on the daily consumption the days-of-supply price divides by -- a MINIMUM turnover the
        /// pricing model assumes for any good a town consumes at all, so a single unit can never satisfy
        /// a market.
        ///
        /// Days of supply is an honest measure only where the daily draw is a real number of units. For a
        /// good a town gets through by the trickle -- a high-value luxury like velvet, whose citizen
        /// appetite in a modest or impoverished town can fall to a few hundredths of a unit a day -- one
        /// unit reads as YEARS of stock, and the price collapses from the famine ceiling to the floor the
        /// instant the shelf holds a single item. That let a party sell one velvet into a bare town at the
        /// 8x ceiling (~200k on a 26,500 base) and buy the very same unit straight back at 1x, an unbounded
        /// loop: the town still wanted velvet exactly as much, but the metric had declared it sated.
        ///
        /// Flooring the divisor closes it at the root without any price memory. At 2 units a day a good
        /// prices as bare (its tier ceiling) until the town holds more than one unit and does not reach the
        /// floor until it holds a genuine stock (<see cref="AbundantDays"/> x 2 = 30 units), so selling in
        /// still pays the shortage while buying back costs the same shortage price -- the round trip only
        /// ever loses the trade spread. It bites only where the metric was already lying: a good with
        /// real turnover (every staple, and any luxury in a town prosperous enough to want it in quantity)
        /// draws far more than two units a day and is untouched. Consumption itself is NOT floored -- only
        /// the price divisor -- so <see cref="TownStorage"/> and <see cref="CitizenDemand"/> still count the
        /// true trickle; this is a statement about what a price may read, not about what a town eats.
        /// </summary>
        public const float MinPricingDaily = 2f;

        /// <summary>
        /// Days of stock at which a good reaches its tier ceiling -- the near-empty point every curve is
        /// anchored to. The scarcity exponent is DERIVED from this and the good's cap
        /// (<c>ln(cap) / ln(AbundantDays / CeilingDays)</c>) so that, whatever the cap, the curve reads
        /// 1.0x at <see cref="AbundantDays"/> and exactly the cap here.
        ///
        /// Kept at 0.5 because that is where the old single 8x ceiling was reached, so the luxury tier
        /// (cap 8) keeps precisely the curve the mod shipped -- exponent 0.61, and the same reference
        /// points it was calibrated against:
        ///
        /// <list type="bullet">
        /// <item>15 days or more (comfortably stocked) -- 1.0x, the historical floor price</item>
        /// <item>10 days -- 1.3x (luxury) · 1.1x (basic)</item>
        /// <item>5 days -- 1.9x (luxury) · 1.3x (basic)</item>
        /// <item>2 days -- 3.4x (luxury) · 1.5x (basic)</item>
        /// <item>1 day -- 5.2x (luxury) · 3.0x (medium) · 1.7x (basic)</item>
        /// <item>0.5 days or less -- the cap: 8x luxury, 4x medium, 2x basic</item>
        /// </list>
        ///
        /// That shape is the point of the whole change. An ordinarily stocked town pays the floor and a
        /// week's stock only a little more, which leaves an honest margin for a merchant without
        /// inflating everything; a town down to a day or two pays a multiple set by what the good is;
        /// a bare shelf runs to the ceiling, which is what grain actually did in a bad year. Vanilla
        /// could not express any of it, because its 10x ceiling was measured from a floor of 0.1x -- so
        /// the "expensive" price and the "cheap" price were both fictions either side of a value that
        /// meant nothing in particular.
        /// </summary>
        public const float CeilingDays = 0.5f;

        /// <summary>
        /// The floor, and it is exactly 1: a market never sells below the base value, because that
        /// value already IS the price of the good where it is plentiful. Anything less would be selling
        /// grain for under what it costs to grow.
        /// </summary>
        public const float MinFactor = 1f;

        /// <summary>
        /// The luxury-tier ceiling and the default for any untiered good: dearest a famine can make a
        /// good whose demand a shortage can inflate without limit, against a floor that is a real
        /// historical price. Reached at <see cref="CeilingDays"/> and held there however empty the shelf
        /// gets. This is what the old single ceiling was, kept for the goods that earn it.
        /// </summary>
        /// <remarks>
        /// Lowered from an effective 17.68x, which was measured and did real damage. Every good with no
        /// stock priced at the ceiling, and since <c>GetItemsToProduce</c> sums UNCAPPED item prices
        /// into the figure vanilla tests against a town's whole market purse, workshops began refusing
        /// to produce for want of a buyer: <c>town-broke</c> went from 58 refusals to 878 and became the
        /// single largest blocker of production, cycles fell from 79% to 64%, taverns bought nothing at
        /// all, and treasuries poured 3,867 denars a day into dearth advances propping up markets that
        /// had been solvent.
        ///
        /// The deeper reason it had to come down: an unbounded markup only means something where supply
        /// can ANSWER it. Velvet has no producer anywhere in the chain, so pinning it at the ceiling
        /// forever was not a scarcity signal but a permanent tax on a good that will never arrive --
        /// and the gates downstream read that number as real. That is also why the ceiling is now a
        /// TIER (<see cref="MarkupCap(ItemObject)"/>): the goods most likely to sit unanswered at the
        /// top -- staples every region grows -- are the ones held to 2x, while the 8x is reserved for
        /// the luxuries whose price a shortage really does carry that far.
        /// </remarks>
        public const float MaxFactor = 8f;

        /// <summary>The medium tier: semi-processed and regional goods a shortage stretches to 4x.</summary>
        public const float MediumCap = 4f;

        /// <summary>The basic tier: staples every region grows, held to 2x however bare the shelf.</summary>
        public const float BasicCap = 2f;

        /// <summary>
        /// The per-good scarcity ceiling, by trade tier. This is the ONE term of the price that is not
        /// uniform across goods; everything else in <see cref="ScarcityFactor(float, float)"/> is shared
        /// and the exponent is derived from the value this returns.
        /// </summary>
        /// <remarks>
        /// The tier is a good's price ELASTICITY, not its base value -- so a cheap, volatile good and a
        /// dear, steady one sit in different tiers regardless of price: spice (base 13) is a luxury at
        /// 8x, grape (base 275) a staple at 2x. Keyed by <see cref="ItemObject.StringId"/> so it spans
        /// both value tables uniformly (<see cref="TradeGoodValues"/> and the naval
        /// <see cref="NavalTradeGoodValues"/>); a good named in neither keeps the old 8x default, so a
        /// good is never silently given a TIGHTER cap than it had before this split without being
        /// listed here.
        /// </remarks>
        private static readonly Dictionary<string, float> MarkupCaps = new Dictionary<string, float>
        {
            // Basic (2x) -- staples every region produces; a shortage cannot spike them far.
            { "flax", BasicCap }, { "clay", BasicCap }, { "cheese", BasicCap }, { "cow", BasicCap },
            { "butter", BasicCap }, { "olives", BasicCap }, { "grape", BasicCap }, { "wool", BasicCap },
            { "grain", BasicCap }, { "hides", BasicCap }, { "meat", BasicCap }, 
            { "silver", BasicCap }, { "iron", BasicCap }, { "hardwood", BasicCap }, 
            { "ironIngot1", BasicCap }, { "ironIngot2", BasicCap }, { "ironIngot3", BasicCap },
            { "hog", BasicCap }, { "sheep", BasicCap },

            // Medium (4x) -- semi-processed or regional goods that answer scarcity harder.
            { "leather", MediumCap }, { "fish", MediumCap }, { "salt", MediumCap }, { "cotton", MediumCap },
            { "beer", MediumCap }, { "date_fruit", MediumCap }, { "tools", MediumCap }, { "wine", MediumCap },
            { "oil", MediumCap }, { "ironIngot4", MediumCap }, { "ironIngot5", MediumCap }, { "planks", MediumCap },
            { "whale_oil", MediumCap }, { "pottery", MediumCap }, { "charcoal", MediumCap }, { "linen", MediumCap },
            { "felt", MediumCap },

            // Luxury (8x) -- status goods a shortage carries as far as the shelf is bare.
            { "jewelry", MaxFactor }, { "spice", MaxFactor }, { "velvet", MaxFactor }, { "fur", MaxFactor },
            { "ironIngot6", MaxFactor }, { "walrus_tusk", MaxFactor },
        };

        /// <summary>The scarcity ceiling for this good's tier; the default 8x for an untiered good.</summary>
        public static float MarkupCap(ItemObject item)
        {
            return MarkupCap((item != null) ? item.StringId : null);
        }

        /// <summary>
        /// The tier ceiling by id. The <see cref="ItemCategory"/> overload of the price path uses this
        /// too -- a workshop input is priced per item but read per category, and a raw material's
        /// category id matches its good id (wool, iron, clay), so the same table serves both.
        /// </summary>
        public static float MarkupCap(string id)
        {
            float cap;
            if (id != null && MarkupCaps.TryGetValue(id, out cap))
            {
                return cap;
            }
            return MaxFactor;
        }

        /// <summary>
        /// What the town pays a supplier, as a share of base value: the wholesale price, and it does
        /// NOT move with scarcity.
        ///
        /// Thirty per cent over the floor. Base value is what a good is worth where it is made, so a
        /// supplier who has carted it somewhere else is owed the carting -- a flat, dependable margin
        /// that makes growing food for a town worth doing in an ordinary year, which a bare floor price
        /// does not. It stays flat because the point is that the countryside is paid for its work rather
        /// than for the town's misfortune.
        /// </summary>
        /// <remarks>
        /// A market has two sides and they are not the same trade. What a convoy is paid at the gate is
        /// the wholesale price of its cargo; what a townsman pays over a stall is that plus whatever the
        /// shortage will bear. Vanilla priced both off one figure, and carrying the scarcity markup into
        /// both was measured doing real harm: a town short of a good paid up to eight times for it, that
        /// money left for the village, and a town short of EVERYTHING was drained on everything it
        /// bought. Forty-nine settlements of a hundred and thirty-three fell under ten thousand denars,
        /// the lowest holding three hundred -- and at three hundred, vanilla's own
        /// <c>town.Gold &lt; outputIncome</c> test refuses nearly every workshop cycle, which is what
        /// made <c>town-broke</c> the largest single blocker of production and why halving the ceiling
        /// did nothing to fix it.
        ///
        /// So the village convoy is paid the floor and the scarcity rent stays inside the town,
        /// recirculated between its own people instead of exported to the countryside. That is the ordinary
        /// shape of the trade anyway: a merchant buys at wholesale and sells at the famine price, and the
        /// margin is his.
        ///
        /// This floor is for the daily bulk flow only -- the village convoys, keyed on
        /// <see cref="MobileParty.IsVillager"/> in the price patch. A caravan, a lord, or the player
        /// selling into the town is paid the real days-of-supply price instead, so a shortage advertises
        /// its own reward and the market can pull supply toward it without waiting on the administrative
        /// caravans. That reopens a self-relieving channel the flat floor had closed, while the convoy
        /// stays flat so the drain that motivated this in the first place cannot come back through the one
        /// flow big enough to cause it. An opportunistic caravan is occasional and self-limiting -- a broke
        /// town buys fewer units at the dearer price -- so it cannot drain a town the way a daily convoy
        /// would.
        ///
        /// A side effect worth naming, because it is the fix for the blocked production rather than a
        /// coincidence: <c>GetItemsToProduce</c> values a workshop's output with <c>isSelling: true</c> and
        /// a null trading party, so that figure stays wholesale and no longer overshoots the market purse
        /// it is tested against.
        ///
        /// Note this is the factor, not the receipt. Vanilla discounts a seller by the merchant's
        /// spread -- <c>basePriceFactor / (1 + tradePenalty)</c>, about 6% for a villager convoy -- so a
        /// supplier actually takes home around 1.22x base rather than 1.30x. That spread is left alone
        /// deliberately: it is how a party's Trade skill earns it a better price, and flattening it here
        /// would quietly delete the mechanic.
        /// </remarks>
        public const float WholesaleFactor = 1.3f;

        // Days-of-supply memo, keyed by town and item and stamped with the roster version, which the
        // roster bumps on every change. GetPrice is called constantly -- every tooltip, every AI trade
        // evaluation, every item in an open inventory -- and the uncached form walks the roster.
        private static readonly Dictionary<string, KeyValuePair<int, float>> _daysCache =
            new Dictionary<string, KeyValuePair<int, float>>();

        // The category reading feeds the AI price signal (GetPriceFactor), which is called for every
        // caravan scoring every reachable town's every cargo category -- so it is memoised the same way
        // the per-item days are, against the roster version, and carries the tier cap alongside the days
        // because both come out of the same branch and recomputing the cap would mean redoing it.
        private struct CatReading { public int Version; public float Days; public float Cap; }
        private static readonly Dictionary<string, CatReading> _catDaysCache =
            new Dictionary<string, CatReading>();

        // ItemCategory id -> the one modelled citizen good that stands for it, built once from
        // CitizenDemand.ModelledGoods. Only the non-workshop branch needs it; a raw material is read across
        // its whole category by WorkshopDemand instead. First good wins, which is immaterial since a food
        // category maps to a single good.
        private static Dictionary<string, ItemObject> _catToItem;

        internal static void ResetForNewSession()
        {
            _daysCache.Clear();
            _catDaysCache.Clear();
            _catToItem = null;
        }

        /// <summary>
        /// How many days the town's stock of this good would last its own people, or a negative number
        /// for a good whose consumption RBM does not model.
        /// </summary>
        /// <remarks>
        /// Memoised against the roster version rather than recomputed. Prosperity also feeds the daily
        /// figure and does NOT bump that version, so the cached value can lag a prosperity change until
        /// the next trade -- immaterial, since prosperity moves by a fraction of a percent a day and any
        /// trade at all refreshes it.
        /// </remarks>
        public static float DaysOfSupply(Town town, ItemObject item)
        {
            if (town == null || item == null || !town.IsTown)
            {
                return -1f;
            }

            // What the player has already sold into (or bought out of) the town this trade visit. The
            // trade screen mutates a shadow roster, not the real one (see HiddenMarketStock), so without
            // this the price a shopper is quoted would not move however much they dump until they closed
            // the screen -- a bare shelf and a flooded one would read the same mid-sale. Folding the
            // uncommitted delta into the stock makes each successive unit priced against the supply the
            // last one just shifted, matching vanilla's within-sale slide. Zero outside a trade session,
            // and the clock is paused while one is open, so no tick-driven caller ever sees it.
            int delta = HiddenMarketStock.SessionDelta(town.Owner.ItemRoster, item);
            if (delta != 0)
            {
                return DaysCore(town, item, delta);
            }

            ItemRoster roster = town.Owner.ItemRoster;
            string key = town.Settlement.StringId + "#" + item.StringId;
            int version = roster.VersionNo;

            KeyValuePair<int, float> cached;
            if (_daysCache.TryGetValue(key, out cached) && cached.Key == version)
            {
                return cached.Value;
            }

            float days = DaysCore(town, item, 0);
            _daysCache[key] = new KeyValuePair<int, float>(version, days);
            return days;
        }

        /// <summary>
        /// Days of stock the town's own people would get through, computed from an effective unit count of
        /// the real stock plus <paramref name="stockDelta"/> (uncommitted trade-screen movement). A
        /// negative return marks a good whose consumption RBM does not model.
        /// </summary>
        private static float DaysCore(Town town, ItemObject item, int stockDelta)
        {
            // A workshop input is measured across its whole category, because that is how a recipe
            // consumes it -- see WorkshopDemand. Where the households buy the same good the two
            // appetites are already added together, so this supersedes the per-item reading rather
            // than competing with it.
            ItemCategory category = item.GetItemCategory();
            float industrial = WorkshopDemand.DailyUnits(town, category);
            if (industrial > 0f)
            {
                float units = WorkshopDemand.UnitsInStore(town, category) + stockDelta;
                return ((units > 0f) ? units : 0f) / MathF.Max(industrial, MinPricingDaily);
            }

            float daily = CitizenDemand.DailyUnits(town, item.StringId);
            if (daily > 0f)
            {
                float units = town.Owner.ItemRoster.GetItemNumber(item) + stockDelta;
                return ((units > 0f) ? units : 0f) / MathF.Max(daily, MinPricingDaily);
            }

            return -1f;
        }

        /// <summary>
        /// How many days the town's stock of a whole category would last, and the tier ceiling that goes
        /// with it, or a negative number for a category RBM does not model. This is the category-level
        /// reading the AI price signal is built on -- see <see cref="SupplySignalPatch"/>.
        /// </summary>
        /// <remarks>
        /// It mirrors the branch in <see cref="DaysOfSupply"/> exactly, so the signal and the retail price
        /// never disagree about whether a good is scarce: a workshop input is measured across its category
        /// by <see cref="WorkshopDemand"/> and capped by its category id; a citizen good is measured on the
        /// one modelled item that stands for the category and capped by that item. A category matching
        /// neither returns -1, and the patch leaves vanilla's factor untouched -- the same boundary the
        /// retail path draws around tools, war gear and horses.
        /// </remarks>
        public static float DaysOfSupplyForCategory(Town town, ItemCategory category, out float cap)
        {
            cap = MaxFactor;
            if (town == null || category == null || !town.IsTown)
            {
                return -1f;
            }

            ItemRoster roster = town.Owner.ItemRoster;
            string key = town.Settlement.StringId + "#" + category.StringId;
            int version = roster.VersionNo;

            CatReading cached;
            if (_catDaysCache.TryGetValue(key, out cached) && cached.Version == version)
            {
                cap = cached.Cap;
                return cached.Days;
            }

            float days;
            float industrial = WorkshopDemand.DailyUnits(town, category);
            if (industrial > 0f)
            {
                cap = MarkupCap(category.StringId);
                days = WorkshopDemand.UnitsInStore(town, category) / MathF.Max(industrial, MinPricingDaily);
            }
            else
            {
                ItemObject item = RepresentativeItem(category);
                if (item != null)
                {
                    float daily = CitizenDemand.DailyUnits(town, item.StringId);
                    if (daily > 0f)
                    {
                        cap = MarkupCap(item);
                        days = roster.GetItemNumber(item) / MathF.Max(daily, MinPricingDaily);
                    }
                    else
                    {
                        days = -1f;
                    }
                }
                else
                {
                    days = -1f;
                }
            }

            _catDaysCache[key] = new CatReading { Version = version, Days = days, Cap = cap };
            return days;
        }

        /// <summary>
        /// The one modelled citizen good that stands for a category, or null if the category has none.
        /// </summary>
        private static ItemObject RepresentativeItem(ItemCategory category)
        {
            if (_catToItem == null)
            {
                _catToItem = new Dictionary<string, ItemObject>();
                foreach (string id in CitizenDemand.ModelledGoods)
                {
                    ItemObject good = Game.Current.ObjectManager.GetObject<ItemObject>(id);
                    if (good == null)
                    {
                        continue;
                    }
                    ItemCategory cat = good.GetItemCategory();
                    if (cat != null && !_catToItem.ContainsKey(cat.StringId))
                    {
                        _catToItem[cat.StringId] = good;
                    }
                }
            }

            ItemObject item;
            return _catToItem.TryGetValue(category.StringId, out item) ? item : null;
        }

        /// <summary>
        /// The scarcity multiplier for a stock that would last <paramref name="days"/>, on a curve
        /// scaled to the good's ceiling <paramref name="maxFactor"/>.
        /// </summary>
        /// <remarks>
        /// The exponent is derived from the cap rather than shared, so the ceiling reshapes the whole
        /// curve instead of clipping a common one: <c>exponent = ln(cap) / ln(AbundantDays/CeilingDays)</c>
        /// makes the factor read 1.0x at <see cref="AbundantDays"/> and reach exactly the cap at
        /// <see cref="CeilingDays"/>, whatever the cap. The luxury cap of 8 recovers the original 0.61
        /// exponent, so that tier is unchanged; lower caps ramp more gently rather than flat-lining early.
        /// </remarks>
        public static float ScarcityFactor(float days, float maxFactor)
        {
            float effective = (days > FloorDays) ? days : FloorDays;
            float exponent = (float)(Math.Log(maxFactor) / Math.Log(AbundantDays / CeilingDays));
            float factor = (float)Math.Pow(AbundantDays / effective, exponent);
            return MathF.Clamp(factor, MinFactor, maxFactor);
        }

        /// <summary>
        /// The floor of the AI price SIGNAL, below the retail floor of 1.0 so that an over-stocked town
        /// reads cheaper than a merely-comfortable one. Vanilla's own factor bottoms out at 0.1 for a
        /// glutted good; matching it keeps every consumer that reads a sub-1.0 factor as "abundant" -- the
        /// caravan buy score, the settlement gold budget, and the workshop-placement input-abundance term
        /// (<c>Max(0, 1 - priceFactor)</c>) -- working as it did on vanilla's number.
        /// </summary>
        public const float SignalFloor = 0.1f;

        /// <summary>
        /// The same days-of-supply curve as <see cref="ScarcityFactor"/>, but floored at
        /// <see cref="SignalFloor"/> rather than 1.0.
        /// </summary>
        /// <remarks>
        /// This is what drives the AI's read of a town -- caravan routing, the trade budget, workshop
        /// placement -- not what anyone is charged. The retail price must never fall below the good's floor
        /// value, so <see cref="ScarcityFactor"/> clamps up to 1.0; the signal has no such rule and is free
        /// to say a good is cheap where it is abundant, which is exactly what the AI needs to prefer a
        /// glutted town to buy from and a bare one to sell to. Both read 1.0x at <see cref="AbundantDays"/>,
        /// so the two never contradict each other about where the shortage is.
        /// </remarks>
        public static float SignalFactor(float days, float maxFactor)
        {
            float effective = (days > FloorDays) ? days : FloorDays;
            float exponent = (float)(Math.Log(maxFactor) / Math.Log(AbundantDays / CeilingDays));
            float factor = (float)Math.Pow(AbundantDays / effective, exponent);
            return MathF.Clamp(factor, SignalFloor, maxFactor);
        }

        /// <summary>
        /// Writes what every modelled good is worth in this town today, and why.
        /// </summary>
        /// <remarks>
        /// Per good: units held, the days that lasts, the scarcity multiplier, vanilla's own
        /// supply/demand for the category, and the price -- because the whole claim of this file is that
        /// the markup follows from the first two, and a log that showed only the price would prove
        /// nothing. If a good sits at the ceiling the units will say whether that is a real shortage or a
        /// good the town never stocks at all.
        ///
        /// The supply/demand pair is vanilla's, carried alongside so the two pricings can be read
        /// against each other. RBM's markup divides days into a full-store figure; vanilla's divides a
        /// prosperity-fed demand by a gold-valued supply. Where the two disagree -- RBM calling a shelf
        /// bare while vanilla's supply reads high, or the reverse -- that gap is exactly what the rewrite
        /// exists to correct, and this is the only line it is visible on.
        ///
        /// This is also the only place the two calibrations can be seen agreeing or not: any town
        /// holding at least <see cref="AbundantDays"/> of a good should read 1.00x, and if nothing ever
        /// does -- if even well-stocked markets carry a markup -- then <see cref="AbundantDays"/> is set
        /// higher than a working supply chain can keep the shelves.
        /// </remarks>
        public static void LogDaily(Settlement settlement)
        {
            if (!EconomyLog.IsEnabled || !RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            Town town = (settlement != null) ? settlement.Town : null;
            if (town == null || !town.IsTown)
            {
                return;
            }

            System.Text.StringBuilder line = new System.Text.StringBuilder();
            foreach (string id in CitizenDemand.ModelledGoods)
            {
                ItemObject item = Game.Current.ObjectManager.GetObject<ItemObject>(id);
                if (item == null)
                {
                    continue;
                }

                float days = DaysOfSupply(town, item);
                if (days < 0f)
                {
                    continue;
                }

                // Vanilla's own supply/demand for the good's category -- the EMAs that feed
                // GetBasePriceFactor, and so the divisor this file's ratio patch divides out. Shown so a
                // days-based markup can be read against the value-based one it replaced: if RBM says a
                // shelf is bare (high days markup) while vanilla's demand/supply says the opposite, that
                // gap is the whole reason the rewrite exists.
                ItemData data = town.MarketData.GetCategoryData(item.GetItemCategory());

                line.Append("  ").Append(id)
                    .Append(" ").Append(town.Owner.ItemRoster.GetItemNumber(item)).Append("u")
                    .Append(" ").Append(EconomyLog.Fmt(days)).Append("d")
                    // Retail markup over the AI signal: the first is floored at 1.0 (a sale never dips
                    // below the good's floor), the second at 0.1, so an abundant town reads below 1.0 here
                    // and nowhere else -- that sub-1.0 number is what the caravan AI reads to buy here.
                    .Append(" ").Append(EconomyLog.Fmt(ScarcityFactor(days, MarkupCap(item))))
                    .Append("/").Append(EconomyLog.Fmt(SignalFactor(days, MarkupCap(item)))).Append("x")
                    .Append(" ").Append(EconomyLog.Fmt(data.Supply))
                    .Append("/").Append(EconomyLog.Fmt(data.Demand)).Append("sd")
                    // Retail over wholesale: what a townsman pays, and what the convoy that brought it
                    // was paid. The gap between the two is the whole of this system, so both are shown.
                    .Append(" ").Append(town.MarketData.GetPrice(item))
                    .Append("/").Append(town.MarketData.GetPrice(item, null, true));
            }

            if (line.Length > 0)
            {
                EconomyLog.Log("PRICE", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "units · days · markup/signal · supply/demand · retail/wholesale  ·" + line);
            }

            LogInputs(settlement, town);
        }

        /// <summary>
        /// The same reading for the raw materials the town's own workshops eat, by category.
        /// </summary>
        /// <remarks>
        /// Kept apart from the PRICE line because it is a different measurement, not more of the same
        /// one: these are counted across a whole category and divided by an industrial draw, so a shared
        /// line would put two incompatible readings of "units" side by side under one heading.
        ///
        /// This is where a stalled workshop economy shows itself. A category sitting at 0 days every day
        /// is a forge with nothing to forge, and the multiplier beside it says whether the price has any
        /// room left to call supply in -- if it is pinned at the ceiling and the units still read zero,
        /// the answer is somewhere in production or carriage, not in pricing.
        /// </remarks>
        private static void LogInputs(Settlement settlement, Town town)
        {
            System.Text.StringBuilder line = new System.Text.StringBuilder();
            foreach (string id in WorkshopDemand.InputCategories(town))
            {
                ItemCategory category = Game.Current.ObjectManager.GetObject<ItemCategory>(id);
                if (category == null)
                {
                    continue;
                }

                float daily = WorkshopDemand.DailyUnits(town, category);
                if (daily <= 0f)
                {
                    continue;
                }

                int units = WorkshopDemand.UnitsInStore(town, category);
                float days = units / daily;
                ItemData data = town.MarketData.GetCategoryData(category);
                line.Append("  ").Append(id)
                    .Append(" ").Append(units).Append("u")
                    .Append(" ").Append(EconomyLog.Fmt(daily)).Append("/d")
                    .Append(" ").Append(EconomyLog.Fmt(days)).Append("d")
                    // Retail markup over the AI signal -- see the PRICE line. The signal is the number the
                    // caravan/workshop AI actually navigates by for this category.
                    .Append(" ").Append(EconomyLog.Fmt(ScarcityFactor(days, MarkupCap(id))))
                    .Append("/").Append(EconomyLog.Fmt(SignalFactor(days, MarkupCap(id)))).Append("x")
                    .Append(" ").Append(EconomyLog.Fmt(data.Supply))
                    .Append("/").Append(EconomyLog.Fmt(data.Demand)).Append("sd");
            }

            if (line.Length > 0)
            {
                EconomyLog.Log("INPUT", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "units · draw · days · markup/signal · supply/demand  ·" + line);
            }
        }

        /// <summary>
        /// Swaps vanilla's value-based scarcity term for a days-of-supply one, leaving the rest of the
        /// price alone.
        /// </summary>
        /// <remarks>
        /// The town comes in by field injection -- <c>TownMarketData._town</c> is private, and the price
        /// model itself is handed no settlement at all, which is why this sits here rather than on
        /// <c>GetBasePriceFactor</c>. Note the FOUR underscores: Harmony's injection prefix is three,
        /// and the field's own name begins with one.
        ///
        /// Only the four-argument overload is patched. The <c>ItemObject</c> overload forwards to it, so
        /// patching both would apply the adjustment twice.
        /// </remarks>
        [HarmonyPatch(typeof(TownMarketData), "GetPrice",
            new Type[] { typeof(EquipmentElement), typeof(MobileParty), typeof(bool), typeof(PartyBase) })]
        private static class ScarcityPricePatch
        {
            private static void Postfix(TownMarketData __instance, Town ____town,
                EquipmentElement itemRosterElement, MobileParty tradingParty, bool isSelling, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || ____town == null)
                {
                    return;
                }

                ItemObject item = itemRosterElement.Item;
                if (item == null)
                {
                    return;
                }

                float days = DaysOfSupply(____town, item);
                if (days < 0f)
                {
                    // Not a good RBM models. Vanilla prices it.
                    return;
                }

                // Ours, and only ours: the good's base value is the historical floor price
                // (TradeGoodValues), scarcity is the markup, and the base value is the floor. isSelling is
                // from the PARTY's side -- true means the party is selling and the town is buying.
                //
                // Who is selling decides whether the sale carries scarcity. A village convoy delivering
                // its harvest is paid the flat wholesale floor: that is the daily bulk flow, and paying it
                // the famine price would drain a short town on its own countryside's food -- the very harm
                // WholesaleFactor exists to prevent. A caravan, a lord, or the player selling into the town
                // is instead paid the real days-of-supply price, the same curve a buyer pays, so a shortage
                // rewards whoever hauls the good in and the market can pull supply toward it on its own
                // rather than waiting on the administrative caravans. A null party is not a trade at all but
                // an internal valuation -- GetItemsToProduce values a workshop's output isSelling:true -- and
                // keeps the flat wholesale figure it was tuned against; see WholesaleFactor.
                bool wholesale = isSelling && (tradingParty == null || tradingParty.IsVillager);
                float factor = wholesale ? WholesaleFactor : ScarcityFactor(days, MarkupCap(item));

                // The one thing kept from vanilla: the party's own trade spread -- their Trade skill and
                // the goods' trade perks. Passing a null merchant is what strips everything else, because
                // the war markup and every village/caravan spread key off the merchant party; with none
                // supplied GetTradePenalty returns just the base margin scaled by the party's skill and
                // the perks the goods carry. Vanilla applies that spread as (1 + penalty) to a buyer and
                // 1 / (1 + penalty) to a seller.
                ItemData data = __instance.GetCategoryData(item.GetItemCategory());
                float penalty = Campaign.Current.Models.TradeItemPriceFactorModel.GetTradePenalty(
                    item, tradingParty, null, isSelling, data.InStoreValue, data.Supply, data.Demand);
                float spread = isSelling ? (1f / (1f + penalty)) : (1f + penalty);

                float priced = itemRosterElement.ItemValue * factor * spread;
                int rounded = isSelling ? MathF.Floor(priced) : MathF.Ceiling(priced);
                __result = (rounded > 1) ? rounded : 1;
            }
        }

        /// <summary>
        /// Replaces vanilla's category price FACTOR with RBM's days-of-supply signal, so the AI navigates
        /// by the same scarcity the retail price is built on instead of by vanilla's parallel demand EMA.
        /// </summary>
        /// <remarks>
        /// This is the seam that collapses the two price signals into one. Vanilla's caravan routing, its
        /// trade budget, its village trade AI and its workshop-placement AI all read
        /// <c>GetPriceFactor(category)</c> -- a different method from the per-item <c>GetPrice</c> the
        /// retail patch above rewrites -- and it was still returning a factor built from the stale
        /// <c>Supply</c>/<c>Demand</c> EMA that RBM's own pricing ignores. Overriding it here, for exactly
        /// the categories RBM models, makes every one of those consumers see the real shortage: a caravan
        /// now scores a bare town as a good place to sell and a glutted one as a good place to buy.
        ///
        /// The per-town factor and the network average the routing compares it against are BOTH built from
        /// this method (the average, in CaravansCampaignBehavior, sums GetItemCategoryPriceIndex which is
        /// GetPriceFactor), so patching this one place keeps them on a single scale by construction. The
        /// signal uses <see cref="SignalFactor"/>, not the retail <see cref="ScarcityFactor"/>, so an
        /// abundant town can read below 1.0 the way vanilla's factor did. Four underscores on the town
        /// field for the same reason the retail patch needs them -- see <see cref="ScarcityPricePatch"/>.
        /// </remarks>
        [HarmonyPatch(typeof(TownMarketData), "GetPriceFactor", new Type[] { typeof(ItemCategory) })]
        private static class SupplySignalPatch
        {
            private static void Postfix(TownMarketData __instance, Town ____town,
                ItemCategory itemCategory, ref float __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !RBMConfig.RBMConfig.rbmDaysOfSupplyAiSignal
                    || ____town == null)
                {
                    return;
                }

                float cap;
                float days = DaysOfSupplyForCategory(____town, itemCategory, out cap);
                if (days < 0f)
                {
                    // Not a category RBM models. Vanilla's factor stands.
                    return;
                }

                __result = SignalFactor(days, cap);
            }
        }
    }
}
