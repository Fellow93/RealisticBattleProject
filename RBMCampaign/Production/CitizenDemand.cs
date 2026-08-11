using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// What a town's households actually buy each day, good by good.
    ///
    /// Vanilla has no shopping list. Every item category is handed a gold budget out of prosperity
    /// (<c>GetDailyDemandForCategory</c>) and <c>MakeConsumption</c> then spends it against whatever
    /// the market happens to be holding -- so a town's diet is decided by its suppliers rather than by
    /// its appetite, and a town with nothing but fish on the shelf eats fish forever and calls it fed.
    /// RBM's food rework made that worse in one specific way: rations are bought CHEAPEST FIRST, which
    /// is right for a garrison's grocer but means the townspeople live on grain alone and touch nothing
    /// else until the grain is gone.
    ///
    /// Here the household has a basket. It is stated per unit of Prosperity per day -- one prosperity
    /// standing in for one household, the same reading <c>NumberOfProsperityToEatOneFood</c> already
    /// uses -- and it has three parts:
    ///
    /// <list type="bullet">
    /// <item><b>Food</b> is a MIX rather than a quantity. The day's ration is still whatever the food
    /// model says (so perks, buildings and the existing calibration are untouched); this only decides
    /// what it is made of.</item>
    /// <item><b>Staples</b> are absolute quantities: fuel, salt, crockery, timber. Charcoal alone is
    /// nearly three times the food throughput, which is what a pre-modern town's fuel bill looks
    /// like.</item>
    /// <item><b>Luxuries</b> appear only once the townspeople have savings, and in three widening
    /// tiers, keyed on citizen wealth measured in days of the town's income.</item>
    /// </list>
    ///
    /// None of this moves money across the settlement's boundary. A townsman buying bread off a
    /// merchant is a transfer inside citizen wealth, so the pot is unchanged and only the goods leave
    /// -- exactly as <see cref="RBMTownFoodSupply"/> already treats the civilian ration. The town's
    /// market fee is the one part that is not internal, and every line here pays it.
    ///
    /// A deliberate non-feature: nothing is conjured to meet this demand. Several goods on the list
    /// have no producer anywhere in the chain -- civilian garments most of all, which no village makes
    /// and no workshop turns out -- so that demand goes unfilled and the money simply goes unspent.
    /// That is the honest result and it is worth being able to see; the DEMAND log line reports every
    /// shortfall by name.
    /// </summary>
    public static class CitizenDemand
    {
        /// <summary>
        /// A household's daily income, per unit of Prosperity. Not spent as such -- nothing debits it,
        /// because citizens' earnings and citizens' spending are the same pot under the ledger. It
        /// exists to size the luxury thresholds below, which ask how many days of the town's income
        /// its people have put by.
        /// </summary>
        public const float IncomePerProsperity = 127.4f;

        /// <summary>Days of town income in savings before households buy anything beyond necessities.</summary>
        public const float SmallLuxuryDays = 5f;
        /// <summary>Days of town income in savings before households buy clothes, trinkets and soft furnishings.</summary>
        /// <remarks>
        /// At 15 days this tier was near-vestigial: measured over a 23-day campaign only 3 of 57 towns
        /// ever reached it, because 15 days of savings needs gold of roughly 2.5x a town's vanilla
        /// wealth target and the economy's mean drift is mildly NEGATIVE -- most towns sit below target,
        /// not 2.5x above it. Lowered to 9 (~1.5x target) so a strong-but-not-booming town reaches it,
        /// giving jewelry, felt and furs an actual buyer rather than a threshold nothing crosses. The
        /// small tier's 0.83x-target anchor is untouched, so the baseline "a solvent town buys a few
        /// small luxuries" reading still holds.
        /// </remarks>
        public const float MediumLuxuryDays = 9f;
        /// <summary>Days of town income in savings before households buy what only the rich buy.</summary>
        /// <remarks>
        /// Lowered from 30 (~5x target, one town in 57) to 18 (~3x target) in step with the medium tier
        /// above, keeping the same 2:1 spacing between them so velvet and the doubled jewelry/tusk rates
        /// remain the preserve of genuinely rich hubs without being a threshold that never fires.
        /// </remarks>
        public const float LargeLuxuryDays = 18f;

        /// <summary>
        /// What the day's ration is made of, as shares summing to 1. Grain is half of it and beer
        /// another sixth -- the two together are the medieval diet, and beer is food rather than drink
        /// at this volume.
        /// </summary>
        /// <remarks>
        /// Two of these -- <c>oil</c> and <c>wine</c> -- are NOT food-store goods
        /// (<c>ItemCategory.Property.BonusToFoodStores</c> is false for both, and their <c>IsFood</c>
        /// flag is false besides). They are eaten all the same, and counting them as ration filled is
        /// deliberate: the alternative is a town that buys its oil and then still reads as 3% starving.
        /// The consequence is only that they do not show in the granary, which is correct -- a barrel
        /// of wine is not a siege reserve.
        /// </remarks>
        private static readonly Line[] FoodMix =
        {
            new Line("grain", 0.518f),
            new Line("beer", 0.176f),
            new Line("meat", 0.0975f),
            new Line("cheese", 0.053f),
            new Line("butter", 0.05f),
            new Line("fish", 0.0575f),
            new Line("wine", 0.02f),
            new Line("date_fruit", 0.018f),
            new Line("oil", 0.01f),
        };

        /// <summary>
        /// Necessities that are not food, in units per Prosperity per day.
        /// </summary>
        /// <remarks>
        /// Charcoal at 0.6 is the largest physical flow in the whole economy -- larger than the town's
        /// food -- and that is not an error: cooking and heating a household through a year burns far
        /// more fuel by weight than it eats. Only lumberjack villages make any, so this will read as a
        /// chronic shortfall until charcoal supply grows to meet it. Salt is the same story on a
        /// smaller scale.
        ///
        /// Whale oil is a lamp: a household's lighting bill, cheap and burned steadily. It is a Naval
        /// DLC good, so with the DLC absent the id never resolves and the line is silently inert -- no
        /// purchase, no shortfall (<see cref="BuyLine"/> returns before recording). With the DLC it is
        /// coastal supply feeding universal demand, so inland towns read it as an honest shortfall until
        /// a caravan carries a barrel their way -- the same shape as charcoal.
        ///
        /// Clothing is handled apart from this table because it is not a trade good: it is whatever
        /// civilian garments happen to be on the shelf, across all five worn slots. See
        /// <see cref="BuyGarments"/>.
        /// </remarks>
        private static readonly Line[] Staples =
        {
            new Line("charcoal", 0.6f),
            new Line("salt", 0.24f),
            new Line("whale_oil", 0.05f),
            new Line("clay", 0.004f),
            new Line("planks", 0.004f),
            new Line("tools", 0.0027f),
            new Line("hides", 0.003f),
        };

        /// <summary>Garments bought per Prosperity per day as a necessity -- replacing what wears out.</summary>
        private const float StapleGarments = 0.1f;

        /// <summary>The first thing savings buy: a better table.</summary>
        private static readonly Line[] SmallLuxuries =
        {
            new Line("date_fruit", 0.01f),
            new Line("wine", 0.01f),
            new Line("oil", 0.01f),
            new Line("olives", 0.01f),
            new Line("clay", 0.001f),
            new Line("tools", 0.0007f),
            new Line("meat", 0.001f),
        };

        /// <summary>The second: dressing well, and a little display.</summary>
        private static readonly Line[] MediumLuxuries =
        {
            new Line("jewelry", 0.0043f),
            new Line("felt", 0.002f),
            new Line("fur", 0.0015f),
            new Line("walrus_tusk", 0.003f),
            new Line("date_fruit", 0.01f),
            new Line("oil", 0.01f),
            new Line("wine", 0.01f),
            new Line("pottery", 0.001f),
            new Line("planks", 0.002f),
            new Line("tools", 0.0007f),
            new Line("meat", 0.002f),
        };

        /// <summary>Garments bought per Prosperity per day on top of the staple replacement, once comfortable.</summary>
        private const float MediumLuxuryGarments = 0.05f;

        /// <summary>
        /// The third: what only the rich buy.
        /// </summary>
        /// <remarks>
        /// Jewelry appears here as well as in the tier below, and the tiers are cumulative, so a town
        /// at this level buys twice the medium tier's jewelry. That is the reading the source figures
        /// support -- the large tier restates jewelry at the same rate rather than raising it, which
        /// only means anything if it stacks.
        ///
        /// Walrus tusk is ivory: a dense, high-value display good priced alongside jewelry. It too
        /// restates its medium-tier rate here so the richest towns buy twice as much. Like whale oil it
        /// is a Naval DLC good -- absent the DLC the id never resolves and the line is inert.
        /// </remarks>
        private static readonly Line[] LargeLuxuries =
        {
            new Line("jewelry", 0.01f),
            new Line("walrus_tusk", 0.003f),
            new Line("velvet", 0.001f),
            new Line("planks", 0.003f),
            new Line("pottery", 0.002f),
            new Line("fur", 0.0015f),
            new Line("meat", 0.005f),
            new Line("date_fruit", 0.01f),
            new Line("oil", 0.01f),
            new Line("wine", 0.01f),
        };

        /// <summary>A demanded good and how much of it a household wants, per day.</summary>
        private struct Line
        {
            public readonly string ItemId;
            public readonly float Rate;

            public Line(string itemId, float rate)
            {
                ItemId = itemId;
                Rate = rate;
            }
        }

        // Every good named anywhere in the tables above, for the double-buy guard below. Built once.
        private static HashSet<string> _basketIds;
        private static string[] _modelledGoods;

        /// <summary>
        /// Every trade good the basket models the consumption of, once each, in table order: the day's
        /// ration first, then the staples, then the luxury tiers.
        /// </summary>
        /// <remarks>
        /// This set is the boundary of what RBM claims to understand about a town's appetite, and three
        /// separate systems are drawn along it -- <see cref="TownStorage"/> caps these and nothing else,
        /// <see cref="RBMMarketPrices"/> prices these and nothing else, and the DEMAND line reports
        /// these. Keeping it one list rather than three keeps that boundary honest.
        ///
        /// Clothing is absent by necessity: it is hundreds of distinct garments rather than a trade
        /// good, so it has no id to name here and is handled apart in each of those places.
        /// </remarks>
        public static string[] ModelledGoods
        {
            get
            {
                if (_modelledGoods == null)
                {
                    List<string> ordered = new List<string>();
                    Line[][] tables = { FoodMix, Staples, SmallLuxuries, MediumLuxuries, LargeLuxuries };
                    foreach (Line[] table in tables)
                    {
                        foreach (Line line in table)
                        {
                            if (!ordered.Contains(line.ItemId))
                            {
                                ordered.Add(line.ItemId);
                            }
                        }
                    }
                    _modelledGoods = ordered.ToArray();
                }
                return _modelledGoods;
            }
        }

        /// <summary>
        /// Whether the households buy this item by the basket, and so must not also be bought out of
        /// vanilla's per-category gold budget.
        /// </summary>
        /// <remarks>
        /// Tested per ITEM rather than per category on purpose. Clothing has no category of its own --
        /// it spreads across garment, light_armor, medium_armor and the rest, categories that also hold
        /// war gear the basket does not touch -- so a category-level guard would either double-buy the
        /// tunics or stop the market consuming armour entirely. Trade goods map one to one with their
        /// category, so for them the two tests are the same thing.
        ///
        /// Everything NOT on the basket stays on vanilla's budget: iron, clay, tools, hides, weapons,
        /// horses. Those have their own sinks -- workshops consume the raw materials, parties buy the
        /// gear -- and inventing a household appetite for them would be worse than leaving them alone.
        /// </remarks>
        public static bool CoversItem(ItemObject item)
        {
            if (item == null)
            {
                return false;
            }
            if (item.IsCivilian && IsWornSlot(item.ItemType))
            {
                return true;
            }

            if (_basketIds == null)
            {
                _basketIds = new HashSet<string>();
                Line[][] tables = { FoodMix, Staples, SmallLuxuries, MediumLuxuries, LargeLuxuries };
                foreach (Line[] table in tables)
                {
                    foreach (Line line in table)
                    {
                        _basketIds.Add(line.ItemId);
                    }
                }
            }
            return _basketIds.Contains(item.StringId);
        }

        /// <summary>
        /// Units of one good the town's households get through in a day, at full appetite.
        /// </summary>
        /// <remarks>
        /// Used to size a town's storage for that good -- see <see cref="TownStorage"/>. Deliberately
        /// counts EVERY tier, including luxuries a poor town cannot currently afford: a warehouse is
        /// built for the trade it might carry, not the trade it happens to be carrying this week, and a
        /// cap that shrank as a town got poorer would strangle its recovery.
        ///
        /// Returns 0 for anything the basket does not model -- iron, clay, tools, war gear. Those are
        /// bought by workshops and passing parties rather than by households, so RBM has no figure for
        /// how much a town gets through and no business capping what it cannot measure.
        /// </remarks>
        public static float DailyUnits(Town town, string itemId)
        {
            if (town == null || string.IsNullOrEmpty(itemId) || town.Prosperity <= 0f)
            {
                return 0f;
            }

            float prosperity = town.Prosperity;
            float units = 0f;

            // The food mix is a share of the day's RATION -- a headcount figure, the same one
            // FeedPopulation works from -- while everything else is a rate per unit of prosperity.
            float ration = prosperity / Campaign.Current.Models.SettlementFoodModel.NumberOfProsperityToEatOneFood;
            foreach (Line line in FoodMix)
            {
                if (line.ItemId == itemId)
                {
                    units += ration * line.Rate;
                }
            }

            Line[][] byProsperity = { Staples, SmallLuxuries, MediumLuxuries, LargeLuxuries };
            foreach (Line[] table in byProsperity)
            {
                foreach (Line line in table)
                {
                    if (line.ItemId == itemId)
                    {
                        units += prosperity * line.Rate;
                    }
                }
            }

            return units;
        }

        /// <summary>Garments a town gets through in a day: staple replacement plus the comfortable tier.</summary>
        public static float DailyGarments(Town town)
        {
            return (town != null && town.Prosperity > 0f)
                ? town.Prosperity * (StapleGarments + MediumLuxuryGarments)
                : 0f;
        }

        /// <summary>
        /// How rich a household is: what it has put by, measured in days of its own daily income.
        /// </summary>
        /// <remarks>
        /// Deliberately expressed per household rather than town-wide, because that is the comparison
        /// the tiers actually mean -- a man buys velvet when HE has thirty days' earnings behind him,
        /// not when his city does. The two forms happen to be the same number, since dividing both
        /// sides by Prosperity cancels, but only this one says why the threshold is where it is.
        ///
        /// A consequence worth stating: the tiers are therefore blind to town size. A small prosperous
        /// town reaches the large tier on far less absolute wealth than a big poor one, which is right
        /// -- it is a statement about how comfortable the people are, not about how big the place is.
        /// </remarks>
        public static float SavingsInDaysOfIncome(Town town)
        {
            float prosperity = town.Prosperity;
            if (prosperity <= 0f || IncomePerProsperity <= 0f)
            {
                return 0f;
            }

            float savingsPerHousehold = SettlementWealth.GetCitizenWealth(town.Settlement) / prosperity;
            return savingsPerHousehold / IncomePerProsperity;
        }

        // What went unbought today, per town, good by good -- built during the day's purchases and
        // emitted by ReportAndClear at the end of the same tick.
        private static readonly Dictionary<string, int> _shortfall = new Dictionary<string, int>();
        private static int _spentToday;

        // Per-tier demand satisfaction, 0..1, from the town's last basket: the fraction of each tier's
        // wanted units the market could actually fill. Read by the prosperity equilibrium as the growth
        // modifier -- a fed town grows toward its countryside figure only as fast as it supplies its
        // people's base wants, with met medium and luxury demand adding to that. Base defaults to 1 (an
        // unticked town is not throttled); the luxury tiers default to 0 (a tier with no demand grants no
        // bonus, rather than a free full one). Garments are deliberately excluded -- nothing produces them,
        // so counting their permanent shortfall would peg every town's growth low. Food ration is excluded
        // too: it is the prosperity model's separate hard GATE on growth, not part of this goods modifier.
        private static readonly Dictionary<Town, float> _baseSatisfaction = new Dictionary<Town, float>();
        private static readonly Dictionary<Town, float> _mediumSatisfaction = new Dictionary<Town, float>();
        private static readonly Dictionary<Town, float> _luxurySatisfaction = new Dictionary<Town, float>();

        // Last tick's per-tier wanted/filled UNITS per town, kept so the Ledger's Towns tab can show
        // demand vs consumed vs missing in whole units (the satisfaction dicts above keep only the ratio).
        // Not persisted -- refreshed every daily consumption tick, same as the satisfaction dicts.
        private static readonly Dictionary<Town, int> _baseWantedByTown = new Dictionary<Town, int>();
        private static readonly Dictionary<Town, int> _baseFilledByTown = new Dictionary<Town, int>();
        private static readonly Dictionary<Town, int> _mediumWantedByTown = new Dictionary<Town, int>();
        private static readonly Dictionary<Town, int> _mediumFilledByTown = new Dictionary<Town, int>();
        private static readonly Dictionary<Town, int> _luxuryWantedByTown = new Dictionary<Town, int>();
        private static readonly Dictionary<Town, int> _luxuryFilledByTown = new Dictionary<Town, int>();

        // The current town's per-tier tally, summed across its basket purchases this tick and folded into
        // the dicts above by ReportAndClear. Static because one town's buys run start to finish before the
        // next town's -- the same single-town-at-a-time assumption _shortfall and _spentToday already make.
        private static int _baseWanted, _baseFilled;
        private static int _mediumWanted, _mediumFilled;
        private static int _luxuryWanted, _luxuryFilled;

        /// <summary>
        /// Buys the households' ration as a basket rather than cheapest-first, and reports the units it
        /// could not fill by preference.
        /// </summary>
        /// <remarks>
        /// The caller fills that remainder from whatever food is left, cheapest first. That fallback is
        /// what keeps this change purely compositional: the number of rations a town gets in a day is
        /// exactly what it was before, so nothing about starvation, prosperity or loyalty moves. Only
        /// the contents of the basket changed. Without it, a town with no brewery would go permanently
        /// 17.6% hungry over a preference, which is not what a hungry household does -- it eats bread.
        /// </remarks>
        public static int BuyRation(Town town, int units, Dictionary<ItemCategory, int> saleLog)
        {
            if (units <= 0)
            {
                return 0;
            }

            int filled = 0;
            int spend = 0;
            foreach (Line line in FoodMix)
            {
                int wanted = MBRandom.RoundRandomized(units * line.Rate);
                filled += BuyLine(town, line.ItemId, wanted, saleLog, ref spend);
            }

            Levy(town, spend);
            return (units > filled) ? units - filled : 0;
        }

        /// <summary>
        /// Buys everything the households want that is not their ration: the staples always, and the
        /// luxury tiers their savings have reached.
        /// </summary>
        /// <remarks>
        /// Quantities are per Prosperity, so this is a shopping list rather than vanilla's gold budget.
        /// The distinction matters for the same reason it did for food: a budget denominated in gold
        /// means a town facing a fuel shortage buys LESS fuel as the price climbs, when what a shortage
        /// should mean is that the same fuel costs more.
        /// </remarks>
        public static void BuyStaplesAndLuxuries(Town town, Dictionary<ItemCategory, int> saleLog)
        {
            float prosperity = town.Prosperity;
            if (prosperity <= 0f)
            {
                return;
            }

            int spend = 0;
            // Staples and small luxuries are the base backbone; the medium and large luxury tiers are the
            // higher demand that lifts growth further. Each tier's wanted-vs-filled is tallied so the
            // prosperity model can read how well the town supplies it (see the per-tier dicts above).
            BuyTable(town, Staples, prosperity, saleLog, ref spend, ref _baseWanted, ref _baseFilled);
            int garments = MBRandom.RoundRandomized(prosperity * StapleGarments);

            float savings = SavingsInDaysOfIncome(town);
            if (savings >= SmallLuxuryDays)
            {
                BuyTable(town, SmallLuxuries, prosperity, saleLog, ref spend, ref _baseWanted, ref _baseFilled);
            }
            if (savings >= MediumLuxuryDays)
            {
                BuyTable(town, MediumLuxuries, prosperity, saleLog, ref spend, ref _mediumWanted, ref _mediumFilled);
                garments += MBRandom.RoundRandomized(prosperity * MediumLuxuryGarments);
            }
            if (savings >= LargeLuxuryDays)
            {
                BuyTable(town, LargeLuxuries, prosperity, saleLog, ref spend, ref _luxuryWanted, ref _luxuryFilled);
            }

            BuyGarments(town, garments, saleLog, ref spend);
            Levy(town, spend);
        }

        private static void BuyTable(Town town, Line[] table, float prosperity, Dictionary<ItemCategory, int> saleLog, ref int spend, ref int wantedAccum, ref int filledAccum)
        {
            foreach (Line line in table)
            {
                int wanted = MBRandom.RoundRandomized(prosperity * line.Rate);
                int filled = BuyLine(town, line.ItemId, wanted, saleLog, ref spend);
                wantedAccum += wanted;
                filledAccum += filled;
            }
        }

        /// <summary>
        /// Takes up to <paramref name="wanted"/> units of one good off the market at the market price
        /// and returns how many it got, recording the rest as a shortfall.
        /// </summary>
        /// <remarks>
        /// No money changes hands here beyond the tally the caller levies a fee on. Both sides of the
        /// counter are inside citizen wealth, so the sale nets to zero across the settlement and the
        /// goods simply leave. The demand registration is a price signal rather than a payment, and is
        /// the same call every other buying channel makes -- see
        /// <see cref="RBMTownFoodSupply.RegisterPurchaseDemand"/>.
        /// </remarks>
        private static int BuyLine(Town town, string itemId, int wanted, Dictionary<ItemCategory, int> saleLog, ref int spend)
        {
            if (wanted <= 0)
            {
                return 0;
            }

            ItemObject item = Game.Current.ObjectManager.GetObject<ItemObject>(itemId);
            if (item == null)
            {
                return 0;
            }

            ItemRoster itemRoster = town.Owner.ItemRoster;
            int available = itemRoster.GetItemNumber(item);
            int taken = (available < wanted) ? available : wanted;
            if (taken < wanted)
            {
                Record(itemId, wanted - taken);
            }
            if (taken <= 0)
            {
                return 0;
            }

            int cost = taken * town.MarketData.GetPrice(item);
            itemRoster.AddToCounts(item, -taken);
            spend += cost;

            RBMTownFoodSupply.RegisterPurchaseDemand(town.MarketData, item.ItemCategory, cost);
            saleLog.TryGetValue(item.ItemCategory, out int logged);
            saleLog[item.ItemCategory] = logged + taken;
            return taken;
        }

        /// <summary>
        /// Buys civilian clothing off the market -- whatever is on the shelf, cheapest first, across
        /// the five worn slots.
        /// </summary>
        /// <remarks>
        /// Clothing cannot go through <see cref="BuyLine"/> because there is no clothing trade good:
        /// it is hundreds of distinct armour items that happen to carry the Civilian flag. Note that
        /// the flag alone is not a garment test -- every trade good carries it too -- so the slot has
        /// to be checked as well.
        ///
        /// Cheapest first because a household replacing a worn tunic buys a tunic, not the best coat in
        /// the market. Left unsorted this would have towns quietly consuming the merchants' finest
        /// stock at forty pieces a day.
        ///
        /// Nothing in the game produces civilian garments -- no village, no workshop -- so this will
        /// usually buy far less than it wants. The shortfall is reported rather than hidden.
        /// </remarks>
        private static void BuyGarments(Town town, int wanted, Dictionary<ItemCategory, int> saleLog, ref int spend)
        {
            if (wanted <= 0)
            {
                return;
            }

            ItemRoster itemRoster = town.Owner.ItemRoster;
            List<GarmentLot> lots = new List<GarmentLot>();
            for (int i = itemRoster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement element = itemRoster.GetElementCopyAtIndex(i);
                ItemObject item = element.EquipmentElement.Item;
                if (item == null || element.Amount <= 0 || !item.IsCivilian || !IsWornSlot(item.ItemType))
                {
                    continue;
                }

                lots.Add(new GarmentLot
                {
                    Element = element.EquipmentElement,
                    Category = item.ItemCategory,
                    Amount = element.Amount,
                    Price = town.MarketData.GetPrice(item),
                    RosterOrder = lots.Count
                });
            }

            lots.Sort(delegate (GarmentLot a, GarmentLot b)
            {
                return (a.Price != b.Price) ? a.Price.CompareTo(b.Price) : a.RosterOrder.CompareTo(b.RosterOrder);
            });

            int remaining = wanted;
            foreach (GarmentLot lot in lots)
            {
                if (remaining <= 0)
                {
                    break;
                }

                int taken = (lot.Amount >= remaining) ? remaining : lot.Amount;
                remaining -= taken;

                int cost = taken * lot.Price;
                itemRoster.AddToCounts(lot.Element, -taken);
                spend += cost;

                RBMTownFoodSupply.RegisterPurchaseDemand(town.MarketData, lot.Category, cost);
                saleLog.TryGetValue(lot.Category, out int logged);
                saleLog[lot.Category] = logged + taken;
            }

            if (remaining > 0)
            {
                Record("clothing", remaining);
            }
        }

        private static bool IsWornSlot(ItemObject.ItemTypeEnum type)
        {
            return type == ItemObject.ItemTypeEnum.HeadArmor
                || type == ItemObject.ItemTypeEnum.BodyArmor
                || type == ItemObject.ItemTypeEnum.LegArmor
                || type == ItemObject.ItemTypeEnum.HandArmor
                || type == ItemObject.ItemTypeEnum.Cape;
        }

        /// <summary>A stack of market clothing priced for this purchase, held apart from the roster so
        /// buying out of price order cannot disturb the iteration.</summary>
        private struct GarmentLot
        {
            public EquipmentElement Element;
            public ItemCategory Category;
            public int Amount;
            public int Price;
            public int RosterOrder;
        }

        /// <summary>
        /// The town's fee on its own people's shopping. The purchase itself is internal to citizen
        /// wealth, but the fee is not -- it moves a sliver into the treasury, like any trade struck in
        /// the market. See <see cref="TradeTariff"/>.
        /// </summary>
        private static void Levy(Town town, int spend)
        {
            _spentToday += spend;
            if (spend > 0)
            {
                TradeTariff.Levy(town.Settlement, spend);
            }
        }

        private static void Record(string itemId, int units)
        {
            if (!EconomyLog.IsEnabled || units <= 0)
            {
                return;
            }
            _shortfall.TryGetValue(itemId, out int had);
            _shortfall[itemId] = had + units;
        }

        /// <summary>
        /// Writes the day's basket for one town and resets the tally for the next.
        /// </summary>
        /// <remarks>
        /// The shortfall list is the point of this line. Demand that cannot be filled is the only
        /// visible evidence of a good nobody produces, and several goods on the basket have no producer
        /// at all -- so a healthy economy is one where this list shrinks, and it is the readout for
        /// whether village and workshop output is anywhere near what towns actually want.
        /// </remarks>
        public static void ReportAndClear(Town town)
        {
            // Fold the day's per-tier tally into the persisted satisfaction figures the prosperity model
            // reads. Not log-gated -- this is live economy state, not diagnostics. A tier with no demand
            // (savings below its threshold, or prosperity zero) leaves base at 1 (no throttle) but the
            // luxury tiers at 0 (no bonus earned).
            _baseSatisfaction[town] = (_baseWanted > 0) ? MathF.Clamp((float)_baseFilled / _baseWanted, 0f, 1f) : 1f;
            _mediumSatisfaction[town] = (_mediumWanted > 0) ? MathF.Clamp((float)_mediumFilled / _mediumWanted, 0f, 1f) : 0f;
            _luxurySatisfaction[town] = (_luxuryWanted > 0) ? MathF.Clamp((float)_luxuryFilled / _luxuryWanted, 0f, 1f) : 0f;
            // Keep the raw unit tallies too, for the ledger's demand/consumed/missing readout.
            _baseWantedByTown[town] = _baseWanted;
            _baseFilledByTown[town] = _baseFilled;
            _mediumWantedByTown[town] = _mediumWanted;
            _mediumFilledByTown[town] = _mediumFilled;
            _luxuryWantedByTown[town] = _luxuryWanted;
            _luxuryFilledByTown[town] = _luxuryFilled;
            _baseWanted = _baseFilled = 0;
            _mediumWanted = _mediumFilled = 0;
            _luxuryWanted = _luxuryFilled = 0;

            if (EconomyLog.IsEnabled)
            {
                float savings = SavingsInDaysOfIncome(town);
                string tier = (savings >= LargeLuxuryDays) ? "large"
                    : (savings >= MediumLuxuryDays) ? "medium"
                    : (savings >= SmallLuxuryDays) ? "small" : "none";

                StringBuilder missing = new StringBuilder();
                foreach (KeyValuePair<string, int> pair in _shortfall)
                {
                    if (missing.Length > 0)
                    {
                        missing.Append(", ");
                    }
                    missing.Append(pair.Key).Append(" ").Append(pair.Value);
                }

                EconomyLog.Log("DEMAND", town.Settlement != null ? town.Settlement.Name.ToString() : town.StringId,
                    "spent " + _spentToday + "d"
                    + "  ·  prosperity " + EconomyLog.Fmt(town.Prosperity)
                    + "  ·  savings " + EconomyLog.Fmt(savings) + " days of income → luxuries " + tier
                    + (missing.Length > 0 ? ("  ·  UNMET: " + missing) : ""));
            }

            _shortfall.Clear();
            _spentToday = 0;
        }

        /// <summary>
        /// Fraction of the town's BASE goods demand -- staples and small luxuries -- the market filled on
        /// its last tick, 0..1. The backbone of the prosperity growth modifier: a town grows toward its
        /// countryside figure only as fast as it supplies its people's everyday wants. Defaults to 1 for a
        /// town not yet ticked, so a fresh or seeded town is not throttled. Food ration is NOT part of this
        /// -- it is the prosperity model's separate hard gate on growth.
        /// </summary>
        public static float BaseDemandSatisfaction(Town town)
        {
            return (town != null && _baseSatisfaction.TryGetValue(town, out float satisfaction)) ? satisfaction : 1f;
        }

        /// <summary>
        /// Fraction of the town's MEDIUM luxury demand the market filled on its last tick, 0..1, or 0 when
        /// the town was not wealthy enough to demand the tier at all. A bonus on top of base growth, so a
        /// town whose citizens can actually buy the middling luxuries they want grows faster.
        /// </summary>
        public static float MediumDemandSatisfaction(Town town)
        {
            return (town != null && _mediumSatisfaction.TryGetValue(town, out float satisfaction)) ? satisfaction : 0f;
        }

        /// <summary>
        /// Fraction of the town's LARGE (top-tier) luxury demand the market filled on its last tick, 0..1,
        /// or 0 when the town was not wealthy enough to demand the tier. The richest growth bonus, earned
        /// only by a genuinely prosperous town whose market can supply what the rich buy.
        /// </summary>
        public static float LuxuryDemandSatisfaction(Town town)
        {
            return (town != null && _luxurySatisfaction.TryGetValue(town, out float satisfaction)) ? satisfaction : 0f;
        }

        // Last tick's per-tier wanted / filled UNITS, for the Towns-tab demand readout. "Wanted" is the
        // whole ration the tier asked for; "Filled" is what the market could supply; the difference is the
        // shortfall. Tiers group as RBM's consumption model does: base = staples + small luxuries,
        // medium = medium luxuries, luxury = large luxuries. Zero for a town not yet ticked.
        public static int BaseWanted(Town town) => Get(_baseWantedByTown, town);
        public static int BaseFilled(Town town) => Get(_baseFilledByTown, town);
        public static int MediumWanted(Town town) => Get(_mediumWantedByTown, town);
        public static int MediumFilled(Town town) => Get(_mediumFilledByTown, town);
        public static int LuxuryWanted(Town town) => Get(_luxuryWantedByTown, town);
        public static int LuxuryFilled(Town town) => Get(_luxuryFilledByTown, town);

        private static int Get(Dictionary<Town, int> dict, Town town)
        {
            return (town != null && dict.TryGetValue(town, out int v)) ? v : 0;
        }

        /// <summary>Drops the previous campaign's per-town demand-satisfaction state and tick tallies.</summary>
        public static void ResetForNewSession()
        {
            _baseSatisfaction.Clear();
            _mediumSatisfaction.Clear();
            _luxurySatisfaction.Clear();
            _baseWantedByTown.Clear();
            _baseFilledByTown.Clear();
            _mediumWantedByTown.Clear();
            _mediumFilledByTown.Clear();
            _luxuryWantedByTown.Clear();
            _luxuryFilledByTown.Clear();
            _baseWanted = _baseFilled = 0;
            _mediumWanted = _mediumFilled = 0;
            _luxuryWanted = _luxuryFilled = 0;
            _shortfall.Clear();
            _spentToday = 0;
        }
    }
}
