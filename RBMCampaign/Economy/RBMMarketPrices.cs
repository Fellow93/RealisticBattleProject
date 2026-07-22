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
    /// factor = clamp( (AbundantDays / days) ^ ScarcityExponent , 1 , MaxFactor )
    /// </code>
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
    /// Applied as a RATIO against vanilla's own factor rather than as a replacement price. The postfix
    /// divides out what vanilla's scarcity term contributed and multiplies in this one, which leaves
    /// every other part of the price -- item value, trade penalties, war markups, the player's Trade
    /// skill and its perks, caravan and village modifiers -- exactly as it was. Only the scarcity
    /// component moves.
    ///
    /// Goods RBM does not model the consumption of are left entirely on vanilla: iron, clay, tools, war
    /// gear, horses. There is no daily figure to divide by, so there are no days to speak of, and
    /// guessing one would misprice the whole workshop economy on no evidence. The same rule governs
    /// <see cref="TownStorage"/>, deliberately -- one boundary, drawn in one place, between what RBM
    /// models and what it leaves alone.
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
        /// Sixty days is a full store (<see cref="TownStorage.StorageDays"/>), so a town that has
        /// filled its granary pays the floor and nobody profits carrying grain to it.
        /// </summary>
        public const float AbundantDays = 60f;

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
        /// How sharply price answers scarcity. Chosen on what the curve does, against a base value that
        /// is the floor:
        ///
        /// <list type="bullet">
        /// <item>60 days (full store) -- 1.0x, the historical floor price</item>
        /// <item>30 days -- 1.5x</item>
        /// <item>15 days -- 2.3x</item>
        /// <item>10 days -- 2.9x</item>
        /// <item>5 days -- 4.1x</item>
        /// <item>4.7 days or less -- 8.0x, the ceiling</item>
        /// </list>
        ///
        /// That shape is the point of the whole change. An ordinarily stocked town pays about half as
        /// much again as the floor, which leaves an honest margin for a merchant without inflating
        /// everything; a town down to a week pays four times; a famine runs to ten or more, which is
        /// what grain actually did in a bad year. Vanilla could not express any of it, because its 10x
        /// ceiling was measured from a floor of 0.1x -- so the "expensive" price and the "cheap" price
        /// were both fictions either side of a value that meant nothing in particular.
        /// </summary>
        public const float ScarcityExponent = 0.6f;

        /// <summary>
        /// The floor, and it is exactly 1: a market never sells below the base value, because that
        /// value already IS the price of the good where it is plentiful. Anything less would be selling
        /// grain for under what it costs to grow.
        /// </summary>
        public const float MinFactor = 1f;

        /// <summary>
        /// The ceiling: dearest a famine can make a good, against a floor that is a real historical
        /// price. Reached at about 4.7 days of stock and held there however empty the shelf gets.
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
        /// and the gates downstream read that number as real.
        /// </remarks>
        public const float MaxFactor = 8f;

        /// <summary>
        /// What the town pays a supplier, as a share of base value: the wholesale price, and it does
        /// NOT move with scarcity.
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
        /// So the supplier is paid the floor and the scarcity rent stays inside the town, recirculated
        /// between its own people instead of exported to the countryside. That is the ordinary shape of
        /// the trade anyway: a merchant buys at wholesale and sells at the famine price, and the margin
        /// is his.
        ///
        /// A side effect worth naming, because it is the fix for the blocked production rather than a
        /// coincidence: <c>GetItemsToProduce</c> values a workshop's output with <c>isSelling: true</c>,
        /// so that figure is now wholesale and no longer overshoots the market purse it is tested
        /// against.
        /// </remarks>
        public const float WholesaleFactor = 1f;

        // Days-of-supply memo, keyed by town and item and stamped with the roster version, which the
        // roster bumps on every change. GetPrice is called constantly -- every tooltip, every AI trade
        // evaluation, every item in an open inventory -- and the uncached form walks the roster.
        private static readonly Dictionary<string, KeyValuePair<int, float>> _daysCache =
            new Dictionary<string, KeyValuePair<int, float>>();

        internal static void ResetForNewSession()
        {
            _daysCache.Clear();
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

            ItemRoster roster = town.Owner.ItemRoster;
            string key = town.Settlement.StringId + "#" + item.StringId;
            int version = roster.VersionNo;

            KeyValuePair<int, float> cached;
            if (_daysCache.TryGetValue(key, out cached) && cached.Key == version)
            {
                return cached.Value;
            }

            float daily = CitizenDemand.DailyUnits(town, item.StringId);
            float days = (daily > 0f) ? (roster.GetItemNumber(item) / daily) : -1f;
            _daysCache[key] = new KeyValuePair<int, float>(version, days);
            return days;
        }

        /// <summary>The scarcity multiplier for a stock that would last <paramref name="days"/>.</summary>
        public static float ScarcityFactor(float days)
        {
            float effective = (days > FloorDays) ? days : FloorDays;
            float factor = (float)Math.Pow(AbundantDays / effective, ScarcityExponent);
            return MathF.Clamp(factor, MinFactor, MaxFactor);
        }

        /// <summary>
        /// Writes what every modelled good is worth in this town today, and why.
        /// </summary>
        /// <remarks>
        /// Four figures per good -- units held, days that lasts, the scarcity multiplier, the price --
        /// because the whole claim of this file is that the last three follow from the first, and a log
        /// that showed only the price would prove nothing. If a good sits at the 20x cap the units will
        /// say whether that is a real shortage or a good the town never stocks at all.
        ///
        /// This is also the only place the two calibrations can be seen agreeing or not: a town at a
        /// full store should read 1.00x, and if nothing ever does, then
        /// <see cref="AbundantDays"/> and <see cref="TownStorage.StorageDays"/> have drifted apart.
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

                line.Append("  ").Append(id)
                    .Append(" ").Append(town.Owner.ItemRoster.GetItemNumber(item)).Append("u")
                    .Append(" ").Append(EconomyLog.Fmt(days)).Append("d")
                    .Append(" ").Append(EconomyLog.Fmt(ScarcityFactor(days))).Append("x")
                    // Retail over wholesale: what a townsman pays, and what the convoy that brought it
                    // was paid. The gap between the two is the whole of this system, so both are shown.
                    .Append(" ").Append(town.MarketData.GetPrice(item))
                    .Append("/").Append(town.MarketData.GetPrice(item, null, true));
            }

            if (line.Length > 0)
            {
                EconomyLog.Log("PRICE", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "units · days · markup · retail/wholesale  ·" + line);
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
                EquipmentElement itemRosterElement, bool isSelling, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || __result <= 0 || ____town == null)
                {
                    return;
                }

                ItemObject item = itemRosterElement.Item;
                float days = DaysOfSupply(____town, item);
                if (days < 0f)
                {
                    // Not a good RBM models. Vanilla prices it.
                    return;
                }

                ItemCategory category = item.GetItemCategory();
                ItemData data = __instance.GetCategoryData(category);
                float vanilla = Campaign.Current.Models.TradeItemPriceFactorModel.GetBasePriceFactor(
                    category, data.InStoreValue, data.Supply, data.Demand, isSelling, 0);
                if (vanilla <= 0.001f)
                {
                    return;
                }

                // isSelling is from the PARTY's side: true means the party is selling and the town is
                // buying, which is the wholesale leg and carries no scarcity markup. See WholesaleFactor.
                float factor = isSelling ? WholesaleFactor : ScarcityFactor(days);

                // Divide out what vanilla's scarcity term contributed, multiply in ours. Everything else
                // the price model did -- penalties, perks, war markups -- rides through untouched.
                int priced = MathF.Round(__result * (factor / vanilla));
                __result = (priced > 1) ? priced : 1;
            }
        }
    }
}
