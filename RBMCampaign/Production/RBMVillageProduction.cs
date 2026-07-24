using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Replaces vanilla village trade-good production. Every village makes a fixed subsistence
    /// "base" set of eight goods, and each specialized village type layers its speciality on top
    /// (additive: a cattle farm produces base cheese PLUS cattle cheese). Output is linear in the
    /// village's raw <see cref="Village.Hearth"/> -- <c>units/day = rate * Hearth</c> -- instead of
    /// vanilla's coarse three-tier <c>(GetHearthLevel()+1) * 0.5</c> step. The daily whole-item roll
    /// still goes through <see cref="MBRandom.RoundRandomized"/>, so a fractional rate is a daily
    /// probability of one extra unit exactly as in vanilla.
    ///
    /// The rate table below is the single source of truth. Both the production tick and the warehouse
    /// capacity are recomputed from it (capacity has to scale too, or the vanilla "roster over
    /// capacity*1.5 halts production" gate would throttle the higher output). The separate vanilla
    /// food track is disabled because the food goods (grain/cheese/butter/meat) are now part of the
    /// base set.
    /// </summary>
    public static class RBMVillageProduction
    {
        // Subsistence floor produced by EVERY village, per point of Hearth per day.
        private static readonly (string id, float rate)[] BaseProduction = new (string, float)[]
        {
            ("hog", 0.022f),
            ("meat", 0.011f),
            ("hides", 0.01f),
            ("cow", 0.003f),
            ("cheese", 0.038f),
            ("butter", 0.03f),
            ("grain", 0.05f),
            ("wool", 0.02f),
            ("sheep", 0.006f),
        };

        // Horse ranches produce culture-appropriate breeds, one item per tier at the rates below:
        // a "normal" riding horse (ItemCategory Horse, item `{culture}_horse`), a "warhorse"
        // (WarHorse, `t2_{culture}_horse`) and a "noble" horse (NobleHorse, `t3_{culture}_horse`).
        // The breed follows the ranch TYPE, not the current owner, exactly as vanilla assigns it.
        // Every ranch also produces a fixed pack-animal bucket (PackAnimal) split evenly across its
        // pack items so the per-ranch pack total equals HorsePackBucket regardless of item count.
        // Deliberately NOT used: `saddle_horse` (a PackAnimal redundant with mule/sumpter) and the
        // shared `war_horse` item (flagged is_merchandise="false", so it is not a tradeable good).
        private const float HorseNormalRate = 0.007f;
        private const float HorseWarRate = 0.0020f;
        private const float HorseNobleRate = 0.0005f;
        private const float HorsePackBucket = 0.003f; // total pack animals per Hearth per day, split across pack items

        private static (string, float)[] HorseRanch(string cultureHorse)
        {
            float pack = HorsePackBucket / 2f; // mule + sumpter_horse
            return new (string, float)[]
            {
                (cultureHorse, HorseNormalRate),
                ("t2_" + cultureHorse, HorseWarRate),
                ("t3_" + cultureHorse, HorseNobleRate),
                ("mule", pack),
                ("sumpter_horse", pack),
            };
        }

        // Speciality goods added on top of the base set, keyed by VillageType.StringId. Rates are
        // per point of Hearth per day. Items that resolve to null (e.g. Naval DLC goods when the DLC
        // is absent) are skipped when the per-village table is built.
        private static readonly Dictionary<string, (string id, float rate)[]> SpecByType =
            new Dictionary<string, (string, float)[]>
            {
                { "wheat_farm", new (string, float)[] { ("grain", 0.118f) } },
                { "cattle_farm", new (string, float)[] { ("cheese", 0.035f), ("butter", 0.028f), ("cow", 0.008f) } },
                { "sheep_farm", new (string, float)[] { ("sheep", 0.0031f), ("cheese", 0.02f), ("butter", 0.015f), ("wool", 0.08f) } },
                { "swine_farm", new (string, float)[] { ("hog", 0.019f) } },
                { "lumberjack", new (string, float)[] { ("charcoal", 1.027f), ("planks", 1.027f) } },
                { "clay_mine", new (string, float)[] { ("clay", 7.5f) } },
                { "salt_mine", new (string, float)[] { ("salt", 0.178f) } },
                { "iron_mine", new (string, float)[] { ("ironIngot1", 2f) } },
                { "silver_mine", new (string, float)[] { ("silver", 0.85f) } },
                { "fisherman", new (string, float)[] { ("fish", 0.055f) } },
                { "vineyard", new (string, float)[] { ("grape", 0.038f) } },
                { "flax_plant", new (string, float)[] { ("flax", 0.170f) } },
                { "date_farm", new (string, float)[] { ("date_fruit", 0.547f) } },
                { "olive_trees", new (string, float)[] { ("olives", 0.089f) } },
                { "silk_plant", new (string, float)[] { ("cotton", 0.008f) } },
                { "trapper", new (string, float)[] { ("fur", 0.055f) } },
                { "europe_horse_ranch", HorseRanch("empire_horse") },
                { "steppe_horse_ranch", HorseRanch("khuzait_horse") },
                { "battanian_horse_ranch", HorseRanch("battania_horse") },
                { "sturgian_horse_ranch", HorseRanch("sturgia_horse") },
                { "vlandian_horse_ranch", HorseRanch("vlandia_horse") },
                // Aserai desert ranch: culture horses (pack bucket split across three pack items:
                // mule + sumpter + pack camel), plus camels as separate bonus mounts
                // (riding camel = Horse, war camel = WarHorse).
                { "desert_horse_ranch", new (string, float)[]
                    {
                        ("aserai_horse", HorseNormalRate),
                        ("t2_aserai_horse", HorseWarRate),
                        ("t3_aserai_horse", HorseNobleRate),
                        ("mule", HorsePackBucket / 3f),
                        ("sumpter_horse", HorsePackBucket / 3f),
                        ("pack_camel", HorsePackBucket / 3f),
                        ("camel", 0.0017f),
                        ("war_camel", 0.0005f),
                    }
                },
                { "walrus_hunter", new (string, float)[] { ("walrus_tusk", 0.008f) } }, // Naval DLC
                { "whaler", new (string, float)[] { ("whale_oil", 0.29f) } },           // Naval DLC
            };

        private static readonly Dictionary<ItemObject, float> Empty = new Dictionary<ItemObject, float>();

        // Resolved rate tables are cached per VillageType. VillageType objects (and the ItemObjects
        // they resolve to) are rebuilt for each campaign, so the cache is dropped whenever the
        // current Campaign changes to avoid handing out stale item references.
        private static readonly Dictionary<VillageType, Dictionary<ItemObject, float>> _cache =
            new Dictionary<VillageType, Dictionary<ItemObject, float>>();
        private static Campaign _cachedCampaign;

        /// <summary>
        /// The effective per-Hearth production rate for a village type: base set summed with the
        /// type's speciality (overlapping goods add). Result maps a resolved <see cref="ItemObject"/>
        /// to its daily rate-per-Hearth. Cached per type for the lifetime of the current campaign.
        /// </summary>
        public static Dictionary<ItemObject, float> GetRates(VillageType villageType)
        {
            if (_cachedCampaign != Campaign.Current)
            {
                _cache.Clear();
                _cachedCampaign = Campaign.Current;
            }

            if (villageType == null)
            {
                return Empty;
            }

            Dictionary<ItemObject, float> cached;
            if (_cache.TryGetValue(villageType, out cached))
            {
                return cached;
            }

            Dictionary<string, float> byId = new Dictionary<string, float>();
            foreach (var b in BaseProduction)
            {
                Accumulate(byId, b.id, b.rate);
            }

            (string id, float rate)[] spec;
            if (SpecByType.TryGetValue(villageType.StringId, out spec))
            {
                foreach (var s in spec)
                {
                    Accumulate(byId, s.id, s.rate);
                }
            }

            Dictionary<ItemObject, float> result = new Dictionary<ItemObject, float>();
            foreach (var kv in byId)
            {
                ItemObject item = Game.Current.ObjectManager.GetObject<ItemObject>(kv.Key);
                // Skip goods that don't exist (e.g. Naval DLC items when the DLC is absent) and
                // anything flagged is_merchandise="false" -- those can't be sold at market, so
                // producing them would clog village/town storage with unsellable stock.
                if (item != null && !item.NotMerchandise)
                {
                    result[item] = kv.Value;
                }
            }

            _cache[villageType] = result;
            return result;
        }

        /// <summary>
        /// Units of goods sitting in a village's store, counted the way the dispatch and production-halt
        /// gates count them -- the sum of roster amounts, not weight or distinct item types.
        /// </summary>
        public static int StoredUnits(Village village)
        {
            ItemRoster roster = village.Owner.ItemRoster;
            int units = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                units += roster[i].Amount;
            }
            return units;
        }

        private static void Accumulate(Dictionary<string, float> byId, string id, float rate)
        {
            float current;
            byId[id] = byId.TryGetValue(id, out current) ? current + rate : rate;
        }

        /// <summary>
        /// Sum of all per-Hearth rates for a village type -- the daily unit throughput at one point
        /// of Hearth across the village's whole good set. Multiply by <see cref="Village.Hearth"/>
        /// for the village's actual daily output.
        /// </summary>
        public static float GetTotalRate(VillageType villageType)
        {
            float sum = 0f;
            foreach (var kv in GetRates(villageType))
            {
                sum += kv.Value;
            }
            return sum;
        }

        // Days of production the warehouse holds. Vanilla's warehouse sizing.
        private const float CapacityDays = 5f;

        /// <summary>
        /// Warehouse size for a given per-Hearth daily rate: five days of that output. Vanilla's own
        /// formula, term for term -- only the rate fed into it changes.
        /// </summary>
        public static int GetGoodCapacity(float ratePerHearth, float hearth)
        {
            return MathF.Ceiling(MathF.Max(1f, ratePerHearth * hearth) * CapacityDays);
        }

        /// <summary>
        /// Replaces the good-production tick. Iterates the full base+speciality set (not just the
        /// village type's vanilla Productions list) and rolls each good with the per-Hearth rate.
        /// </summary>
        [HarmonyPatch(typeof(VillageGoodProductionCampaignBehavior), "TickGoodProduction")]
        private static class TickGoodProductionPatch
        {
            private static bool Prefix(Village village, bool initialProductionForTowns)
            {
                // Hand the tick back to vanilla when the module is off. Returning false here instead
                // would skip the original method as well, so toggling the module off mid-session would
                // stop village production outright rather than restore the game's own.
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return true;
                }

                if (village == null)
                {
                    return false;
                }

                // Raided/looted villages produce nothing; vanilla achieves this via the model
                // returning 0.
                //
                // Deliberately NOT gated on TradeBound. Vanilla's null check sits inside the
                // initialProductionForTowns branch alone -- it guards the settlement the seeded goods
                // are written INTO, not production itself, and the normal branch fills the village's
                // own store with no such test. Hoisting it to the top made a village stop producing
                // whenever it had no reachable non-hostile town, which for a castle village in
                // wartime is the whole war; with vanilla's TickFoodProduction disabled, that left the
                // castle's food chain with nothing upstream of it at all.
                if (village.VillageState != Village.VillageStates.Normal)
                {
                    return false;
                }

                // Seeding has nowhere to write without a bound town, and unlike the normal branch it
                // has no local store to fall back on. Vanilla's own guard, at vanilla's position.
                if (initialProductionForTowns && village.TradeBound == null)
                {
                    return false;
                }

                // The day's output, good by good, for the economy log. Built only when that log is on;
                // production runs for every village on the map every day and this must cost nothing off.
                bool logging = EconomyLog.IsEnabled;
                System.Text.StringBuilder produced = logging ? new System.Text.StringBuilder() : null;
                int totalUnits = 0;

                foreach (var kv in GetRates(village.VillageType))
                {
                    int num = MBRandom.RoundRandomized(kv.Value * village.Hearth);
                    if (num <= 0)
                    {
                        continue;
                    }

                    if (!initialProductionForTowns)
                    {
                        village.Owner.ItemRoster.AddToCounts(kv.Key, num);
                        CampaignEventDispatcher.Instance.OnItemProduced(kv.Key, village.Owner.Settlement, num);
                    }
                    else
                    {
                        village.TradeBound.ItemRoster.AddToCounts(kv.Key, num);
                    }

                    if (logging)
                    {
                        totalUnits += num;
                        if (produced.Length > 0)
                        {
                            produced.Append(", ");
                        }
                        produced.Append(kv.Key.StringId).Append(" ").Append(num);
                    }
                }

                if (logging)
                {
                    LogProduction(village, initialProductionForTowns, totalUnits, produced.ToString());
                }

                return false;
            }

            /// <summary>
            /// One line per village per day: where the goods went, what the village is, how large its
            /// population and warehouse are, and then the goods themselves.
            /// </summary>
            private static void LogProduction(Village village, bool initialProductionForTowns, int totalUnits, string goods)
            {
                string name = village.Settlement != null ? village.Settlement.Name.ToString() : village.StringId;
                string destination = initialProductionForTowns
                    ? ("seeded into " + (village.TradeBound != null ? village.TradeBound.Name.ToString() : "bound town"))
                    : "into village store";

                EconomyLog.Log("PRODUCE", name,
                    "type " + (village.VillageType != null ? village.VillageType.StringId : "-")
                    + "  hearth " + EconomyLog.Fmt(village.Hearth)
                    + "  stored " + StoredUnits(village)
                    + "/" + village.GetWarehouseCapacity()
                    + "  ·  " + totalUnits + " units " + destination
                    + (string.IsNullOrEmpty(goods) ? "" : ("  ·  " + goods)));
            }
        }

        /// <summary>
        /// Disables the separate vanilla food track. Its goods (grain, cheese, butter) are now part
        /// of the base production set, so leaving it on would double-count food.
        /// </summary>
        [HarmonyPatch(typeof(VillageGoodProductionCampaignBehavior), "TickFoodProduction")]
        private static class TickFoodProductionPatch
        {
            private static bool Prefix()
            {
                return !RBMConfig.RBMConfig.rbmCampaignEnabled;
            }
        }

        /// <summary>
        /// Rescales warehouse capacity from the new rate table. Vanilla derives it from the model
        /// over VillageType.Productions; since our real output is larger and drawn from a different
        /// good set, capacity has to be recomputed or the production-halt gate throttles everything.
        ///
        /// Sized off the TOTAL rate across the good set, not a per-good average, because every reader
        /// of this number compares it against the sum of the whole item roster: the production-halt
        /// gate in <c>TickProductions</c> (<c>rosterSum &lt; capacity * 1.5</c>), the villager dispatch
        /// gate, and War Sails' fishing-party gate. An average-sized capacity would be roughly one
        /// day of a village's actual output across 8-10 goods, so production would halt before the
        /// first day was out and stay halted for the whole of the convoy's multi-day round trip --
        /// exactly the throttle this patch exists to prevent.
        /// </summary>
        [HarmonyPatch(typeof(Village), "GetWarehouseCapacity")]
        private static class WarehouseCapacityPatch
        {
            private static bool Prefix(Village __instance, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return true;
                }

                __result = GetGoodCapacity(GetTotalRate(__instance.VillageType), __instance.Hearth);
                return false;
            }
        }

        /// <summary>
        /// Aligns the production-calculator model with the new rates so tooltips, trade AI and the
        /// initial-tax seeding all agree with what the tick actually produces. Returns the per-Hearth
        /// amount for goods in the village's set, 0 otherwise.
        /// </summary>
        [HarmonyPatch(typeof(DefaultVillageProductionCalculatorModel), "CalculateDailyProductionAmount")]
        private static class ProductionAmountPatch
        {
            private static bool Prefix(Village village, ItemObject item, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return true;
                }

                float amount = 0f;
                if (village != null && item != null && village.VillageState == Village.VillageStates.Normal)
                {
                    float rate;
                    if (GetRates(village.VillageType).TryGetValue(item, out rate))
                    {
                        amount = rate * village.Hearth;
                    }
                }

                __result = new ExplainedNumber(amount, false, null);
                return false;
            }
        }

        // Per-Hearth total production rates that bracket the villager-party sizing curve. A village
        // producing only the subsistence base set sits at ~0.168/Hearth/day (QuietRate); anything at
        // or above BusyRate is treated as maximally busy. Most specialities (mines, lumberjacks)
        // sit far above BusyRate and simply peg to the largest party.
        private const float QuietRate = 0.17f;
        private const float BusyRate = 0.5f;

        /// <summary>
        /// Sizes villager parties off the RBM production set instead of vanilla's
        /// <c>VillageType.Productions</c> list. Vanilla sums the daily amount of only the goods on
        /// that list -- which under RBM is a stale subset, since every village also makes the base
        /// set and specialities were re-tabled -- then interpolates a Hearth divisor from 40 (quiet)
        /// down to 20 (busy) and returns <c>Minimum + Hearth / divisor</c>.
        ///
        /// This keeps vanilla's shape and its 12 + Hearth/[20..40] band, but drives the interpolation
        /// from the village's TOTAL per-Hearth throughput across its whole good set. Rate is used
        /// per-Hearth rather than as an absolute daily figure because Hearth already scales the size
        /// term; using the absolute would count population twice.
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartySizeLimitModel), "GetIdealVillagerPartySize")]
        private static class VillagerPartySizePatch
        {
            private static bool Prefix(DefaultPartySizeLimitModel __instance, Village village, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || village == null)
                {
                    return true;
                }

                float busyness = MathF.Clamp((GetTotalRate(village.VillageType) - QuietRate) / (BusyRate - QuietRate), 0f, 1f);
                float divisor = MathF.Lerp(40f, 20f, busyness);
                __result = __instance.MinimumNumberOfVillagersAtVillagerParty + (int)(village.Hearth / divisor);
                return false;
            }
        }

        /// <summary>
        /// Lowers the divisor that converts a settlement's prosperity into daily food consumption
        /// (<c>consumption = Prosperity / NumberOfProsperityToEatOneFood</c>) from vanilla's 40 to 4,
        /// so a given prosperity eats roughly 10x more food. Paired with the reworked production above
        /// to make food supply an actual constraint on settlement growth.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementFoodModel), "NumberOfProsperityToEatOneFood", MethodType.Getter)]
        private static class ProsperityToEatOneFoodPatch
        {
            private static bool Prefix(ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return true;
                }

                __result = 4;
                return false;
            }
        }

        /// <summary>
        /// Replaces the VILLAGE branch of the settlement production tooltip (the trade/inventory
        /// screen) so it lists the reworked good set with each good's expected per-day amount
        /// (rate * Hearth), highest first. Towns/castles and disabled mode fall through to vanilla,
        /// so only the village production listing changes.
        /// </summary>
        [HarmonyPatch(typeof(CampaignUIHelper), "GetSettlementProductionTooltip")]
        private static class ProductionTooltipPatch
        {
            private static bool Prefix(Settlement settlement, ref List<TooltipProperty> __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null || !settlement.IsVillage)
                {
                    return true;
                }

                Village village = settlement.Village;
                List<TooltipProperty> list = new List<TooltipProperty>();
                list.Add(new TooltipProperty("", GameTexts.FindText("str_production").ToString(), 0, false, TooltipProperty.TooltipPropertyFlags.Title));
                list.Add(new TooltipProperty(GameTexts.FindText("str_production_in_village").ToString(), " ", 0));
                list.Add(new TooltipProperty("", string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.RundownSeperator));

                // Order by daily quantity, most-produced first, so the staple leads the list.
                List<KeyValuePair<ItemObject, float>> ordered = new List<KeyValuePair<ItemObject, float>>(GetRates(village.VillageType));
                ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

                // Per-good daily output, then the one warehouse figure the game actually tracks --
                // a single store shared by every good, not a per-good allowance.
                float hearth = village.Hearth;
                foreach (var kv in ordered)
                {
                    float perDay = kv.Value * hearth;
                    list.Add(new TooltipProperty(kv.Key.Name.ToString(), perDay.ToString("0.##") + " /day", 0));
                }

                list.Add(new TooltipProperty("", string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.RundownSeperator));
                list.Add(new TooltipProperty(WarehouseText.ToString(),
                    StoredUnits(village) + " / " + village.GetWarehouseCapacity(), 0));

                __result = list;
                return false;
            }
        }

        private static readonly TextObject WarehouseText = new TextObject("{=RBM_VILLAGE_WAREHOUSE}Warehouse");
    }
}
