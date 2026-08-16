using System.Collections.Generic;
using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
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
            ("hog", 0.002f),
            ("meat", 0.001f),
            ("hides", 0.01f),
            ("cow", 0.001f),
            ("cheese", 0.038f),
            ("butter", 0.03f),
            ("grain", 0.05f),
            ("wool", 0.02f),
            ("sheep", 0.002f),
            //basic village "industrial" production so cities can function
            ("charcoal", 0.1f),
            ("planks", 0.01f),
            ("clay", 0.001f),
            ("ironIngot1", 0.015f),
            ("flax", 0.017f),
            // Every village occasionally raises a pack animal (PackAnimal). Set to 10% of a horse
            // ranch's per-item mule rate (HorsePackBucket/2), so it reads as subsistence, not a
            // production speciality. Does NOT affect the map primary-production icon (that draws
            // only from SpecByType).
            ("mule", HorsePackBucket / 2f * 0.05f),
        };

        // Horse ranches produce culture-appropriate breeds, one item per tier at the rates below:
        // a "normal" riding horse (ItemCategory Horse, item `{culture}_horse`), a "warhorse"
        // (WarHorse, `t2_{culture}_horse`) and a "noble" horse (NobleHorse, `t3_{culture}_horse`).
        // The breed follows the ranch TYPE, not the current owner, exactly as vanilla assigns it.
        // Every ranch also produces a fixed pack-animal bucket (PackAnimal) split evenly across its
        // pack items so the per-ranch pack total equals HorsePackBucket regardless of item count.
        // Deliberately NOT used: `saddle_horse` (a PackAnimal redundant with mule/sumpter) and the
        // shared `war_horse` item (flagged is_merchandise="false", so it is not a tradeable good).
        private const float HorseNormalRate = 0.015f;
        private const float HorseWarRate = 0.0020f;
        private const float HorseNobleRate = 0.0005f;
        private const float HorsePackBucket = 0.01f; // total pack animals per Hearth per day, split across pack items

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
                { "wheat_farm", new (string, float)[] { ("grain", 0.2f) } },
                { "cattle_farm", new (string, float)[] { ("cheese", 0.035f), ("butter", 0.028f), ("cow", 0.008f) } },
                { "sheep_farm", new (string, float)[] { ("sheep", 0.0031f), ("cheese", 0.02f), ("butter", 0.015f), ("wool", 0.08f) } },
                { "swine_farm", new (string, float)[] { ("hog", 0.019f) } },
                { "lumberjack", new (string, float)[] { ("charcoal", 1.027f*1.8f), ("planks", 1.027f*0.2f) } },
                { "clay_mine", new (string, float)[] { ("clay", 1.3f), ("grain", 0.07f) } },
                { "salt_mine", new (string, float)[] { ("salt", 0.7f) } },
                { "iron_mine", new (string, float)[] { ("ironIngot1", 2f * 0.75f), ("charcoal", 1.027f * 0.5f) } },
                { "silver_mine", new (string, float)[] { ("silver", 0.85f * 0.75f), ("ironIngot1", 2f * 0.25f) } },
                { "fisherman", new (string, float)[] { ("fish", 0.2f), ("salt", 0.07f) } },
                { "vineyard", new (string, float)[] { ("grape", 0.038f) } },
                { "flax_plant", new (string, float)[] { ("flax", 0.170f) } },
                { "date_farm", new (string, float)[] { ("date_fruit", 0.547f) } },
                { "olive_trees", new (string, float)[] { ("olives", 0.089f) } },
                { "silk_plant", new (string, float)[] { ("cotton", 0.008f) } },
                { "trapper", new (string, float)[] { ("fur", 0.055f), ("meat", 0.055f) } },
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
                { "walrus_hunter", new (string, float)[] { ("walrus_tusk", 0.008f), ("salt", 0.07f), ("meat", 0.055f) } }, // Naval DLC
                { "whaler", new (string, float)[] { ("whale_oil", 0.29f), ("salt", 0.07f), ("meat", 0.055f) } },           // Naval DLC
            };

        // Culture "flavour": a light trickle of a signature good produced by EVERY village of a
        // given culture, on top of its base+speciality set, at roughly a tenth of the corresponding
        // specialist village's rate. Keyed by the village's OWN culture (Settlement.Culture, a fixed
        // XML field that never tracks the current owner), so a captured Battanian village keeps making
        // its charcoal no matter who holds it. Items that don't resolve (e.g. walrus_tusk without the
        // Naval DLC) are skipped, exactly like the speciality table.
        //
        // The Empire is a single culture but three regions with distinct signatures, so its key is
        // refined to empire_north/south/west by FlavourKey(); every other culture keys straight off
        // its StringId. Cultures with no entry here (bandits, minor factions) simply get no flavour.
        private static readonly Dictionary<string, (string id, float rate)[]> FlavourByCulture =
            new Dictionary<string, (string, float)[]>
            {
                { "aserai",       new (string, float)[] { ("date_fruit", 0.0547f), ("salt", 0.03f) } },
                { "empire_south", new (string, float)[] { ("olives", 0.0089f) } },
                { "empire_west",  new (string, float)[] { ("olives", 0.00445f), ("grape", 0.0019f) } },
                { "empire_north", new (string, float)[] { ("fur", 0.0055f) } },
                { "battania",     new (string, float)[] { ("charcoal", 1.027f*0.15f), ("planks", 1.027f*0.05f) } },
                { "vlandia",      new (string, float)[] { ("grape", 0.0038f) } },
                { "khuzait",      ScaleRates(HorseRanch("khuzait_horse"), 0.1f) },
                { "sturgia",      new (string, float)[] { ("fur", 0.0055f) } },
                { "nord",         new (string, float)[] { ("walrus_tusk", 0.0008f) } }, // Naval DLC culture
            };

        private static (string, float)[] ScaleRates((string id, float rate)[] rates, float factor)
        {
            var scaled = new (string, float)[rates.Length];
            for (int i = 0; i < rates.Length; i++)
            {
                scaled[i] = (rates[i].id, rates[i].rate * factor);
            }
            return scaled;
        }

        /// <summary>
        /// The <see cref="FlavourByCulture"/> key for a village, or null for no flavour. Reads the
        /// village's fixed <see cref="CultureObject.StringId"/> (never the owner's). Empire is one
        /// culture spanning three regions, so it is split by the village's bound fortification
        /// StringId -- <c>town_E?*</c>/<c>castle_E?*</c>, where the letter after <c>_E</c> is
        /// N/S/W. That binding is set in the settlement data and does not change with conquest, so
        /// the region is as stable as the culture is. Unknown empire fortifications get no flavour
        /// (better than a wrong one).
        /// </summary>
        private static string FlavourKey(Village village)
        {
            string culture = village?.Settlement?.Culture?.StringId;
            if (string.IsNullOrEmpty(culture))
            {
                return null;
            }

            if (culture != "empire")
            {
                return culture;
            }

            string bound = village.Bound?.StringId;
            if (bound == null)
            {
                return null;
            }
            if (bound.Contains("_EN")) return "empire_north";
            if (bound.Contains("_ES")) return "empire_south";
            if (bound.Contains("_EW")) return "empire_west";
            return null;
        }

        // Village types whose "primary production" icon should be pinned to a specific good rather
        // than the automatic rate*Value pick. Desert ranches are chiefly known for their camels even
        // though horses out-weight them on rate*Value, so they show a camel on the map. Keyed by
        // VillageType.StringId -> item id (the item still has to resolve and be merchandise).
        private static readonly Dictionary<string, string> PrimaryOverride =
            new Dictionary<string, string>
            {
                { "desert_horse_ranch", "camel" },
            };

        private static readonly Dictionary<ItemObject, float> Empty = new Dictionary<ItemObject, float>();

        // Resolved rate tables are cached per VillageType. VillageType objects (and the ItemObjects
        // they resolve to) are rebuilt for each campaign, so the cache is dropped whenever the
        // current Campaign changes to avoid handing out stale item references.
        private static readonly Dictionary<VillageType, Dictionary<ItemObject, float>> _cache =
            new Dictionary<VillageType, Dictionary<ItemObject, float>>();
        // Resolved speciality good shown as each type's "primary production" (map icon, tooltips).
        // Same per-campaign lifetime as _cache -- dropped together when the campaign changes.
        private static readonly Dictionary<VillageType, ItemObject> _primaryCache =
            new Dictionary<VillageType, ItemObject>();
        // Per-village-flavour resolved tables (base+spec+flavour), keyed by
        // "villageTypeStringId|flavourKey". Same per-campaign lifetime as _cache.
        private static readonly Dictionary<string, Dictionary<ItemObject, float>> _flavourCache =
            new Dictionary<string, Dictionary<ItemObject, float>>();
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
                _primaryCache.Clear();
                _flavourCache.Clear();
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

        /// <summary>
        /// The per-Hearth production rate table for a specific village: its type's base+speciality
        /// set (<see cref="GetRates(VillageType)"/>) with the village's culture flavour layered on
        /// top (overlapping goods add). Falls back to the plain type table when the culture has no
        /// flavour entry. Cached per (type, flavour key) for the campaign's lifetime.
        /// </summary>
        public static Dictionary<ItemObject, float> GetRates(Village village)
        {
            if (village == null || village.VillageType == null)
            {
                return Empty;
            }

            // Resolve the base+speciality table first -- this also runs the per-campaign cache
            // invalidation (which clears _flavourCache), so it must precede any _flavourCache read.
            Dictionary<ItemObject, float> baseSpec = GetRates(village.VillageType);

            string flavourKey = FlavourKey(village);
            (string id, float rate)[] flavour;
            if (flavourKey == null || !FlavourByCulture.TryGetValue(flavourKey, out flavour))
            {
                return baseSpec;
            }

            string key = village.VillageType.StringId + "|" + flavourKey;
            Dictionary<ItemObject, float> cached;
            if (_flavourCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            Dictionary<ItemObject, float> result = new Dictionary<ItemObject, float>(baseSpec);
            foreach (var s in flavour)
            {
                ItemObject item = Game.Current.ObjectManager.GetObject<ItemObject>(s.id);
                if (item != null && !item.NotMerchandise)
                {
                    float current;
                    result[item] = result.TryGetValue(item, out current) ? current + s.rate : s.rate;
                }
            }

            _flavourCache[key] = result;
            return result;
        }

        /// <summary>
        /// Sum of all per-Hearth rates for a specific village, including its culture flavour.
        /// The village-aware counterpart of <see cref="GetTotalRate(VillageType)"/>.
        /// </summary>
        public static float GetTotalRate(Village village)
        {
            float sum = 0f;
            foreach (var kv in GetRates(village))
            {
                sum += kv.Value;
            }
            return sum;
        }

        /// <summary>
        /// The single good shown as a village's "primary production" -- its type speciality, chosen
        /// as the speciality good with the greatest <c>rate * Value</c>. Deliberately drawn from
        /// <see cref="SpecByType"/> alone, NOT the shared subsistence base set (which would show
        /// iron/charcoal on every village) and NOT vanilla's value-weighted pick over its now-stale
        /// <c>VillageType.Productions</c> list. So an iron mine reads as iron, a palm orchard as
        /// dates, a forester as charcoal. Returns null for a type with no resolved speciality, which
        /// leaves vanilla's own getter standing.
        /// </summary>
        public static ItemObject GetPrimaryProduction(VillageType villageType)
        {
            if (villageType == null || Game.Current == null)
            {
                return null;
            }

            // Reuse GetRates' per-campaign invalidation so a new campaign drops stale item refs.
            GetRates(villageType);

            ItemObject cached;
            if (_primaryCache.TryGetValue(villageType, out cached))
            {
                return cached;
            }

            // A pinned icon wins outright, provided its item resolves and is sellable.
            string overrideId;
            if (PrimaryOverride.TryGetValue(villageType.StringId, out overrideId))
            {
                ItemObject pinned = Game.Current.ObjectManager.GetObject<ItemObject>(overrideId);
                if (pinned != null && !pinned.NotMerchandise)
                {
                    _primaryCache[villageType] = pinned;
                    return pinned;
                }
            }

            ItemObject best = null;
            float bestWeight = -1f;
            (string id, float rate)[] spec;
            if (SpecByType.TryGetValue(villageType.StringId, out spec))
            {
                foreach (var s in spec)
                {
                    ItemObject item = Game.Current.ObjectManager.GetObject<ItemObject>(s.id);
                    if (item == null || item.NotMerchandise)
                    {
                        continue;
                    }

                    float weight = s.rate * item.Value;
                    if (weight > bestWeight)
                    {
                        bestWeight = weight;
                        best = item;
                    }
                }
            }

            _primaryCache[villageType] = best;
            return best;
        }

        /// <summary>
        /// The village type's speciality goods as resolved, sellable items ordered by the same
        /// <c>rate * Value</c> weight <see cref="GetPrimaryProduction"/> ranks by (a pinned
        /// <see cref="PrimaryOverride"/> good first). Used by the map/ledger icon resolver to fall back
        /// from a primary good the nameplate brush ships no sprite for (charcoal) to the best companion
        /// speciality good that it does -- so a lumberjack reads as planks, a salt-and-charcoal village
        /// as salt. Drawn from <see cref="SpecByType"/> only, matching <see cref="GetPrimaryProduction"/>.
        /// </summary>
        public static List<ItemObject> GetSpecialityItemsByWeight(VillageType villageType)
        {
            var ordered = new List<ItemObject>();
            if (villageType == null || Game.Current == null)
            {
                return ordered;
            }

            // Reuse GetRates' per-campaign invalidation so a new campaign drops stale item refs.
            GetRates(villageType);

            string overrideId;
            if (PrimaryOverride.TryGetValue(villageType.StringId, out overrideId))
            {
                ItemObject pinned = Game.Current.ObjectManager.GetObject<ItemObject>(overrideId);
                if (pinned != null && !pinned.NotMerchandise)
                {
                    ordered.Add(pinned);
                }
            }

            (string id, float rate)[] spec;
            if (SpecByType.TryGetValue(villageType.StringId, out spec))
            {
                var ranked = new List<(ItemObject item, float weight)>();
                foreach (var s in spec)
                {
                    ItemObject item = Game.Current.ObjectManager.GetObject<ItemObject>(s.id);
                    if (item == null || item.NotMerchandise || ordered.Contains(item))
                    {
                        continue;
                    }
                    ranked.Add((item, s.rate * item.Value));
                }
                ranked.Sort((a, b) => b.weight.CompareTo(a.weight));
                foreach (var r in ranked)
                {
                    ordered.Add(r.item);
                }
            }

            return ordered;
        }

        /// <summary>
        /// Points the game's "primary production" at the village type's RBM speciality good, so the
        /// map nameplate icon -- and every other reader of <c>PrimaryProduction</c> (hover tooltip,
        /// town-management list, trade issues) -- shows what the village actually produces under the
        /// reworked table instead of vanilla's value-weighted pick over its stale Productions list.
        /// Falls through to vanilla for any type without a resolved speciality.
        /// </summary>
        [HarmonyPatch(typeof(VillageType), "PrimaryProduction", MethodType.Getter)]
        private static class PrimaryProductionPatch
        {
            private static bool Prefix(VillageType __instance, ref ItemObject __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return true;
                }

                ItemObject primary = GetPrimaryProduction(__instance);
                if (primary == null)
                {
                    return true;
                }

                __result = primary;
                return false;
            }
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

                foreach (var kv in GetRates(village))
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

                __result = GetGoodCapacity(GetTotalRate(__instance), __instance.Hearth);
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
                    if (GetRates(village).TryGetValue(item, out rate))
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

                float busyness = MathF.Clamp((GetTotalRate(village) - QuietRate) / (BusyRate - QuietRate), 0f, 1f);
                float divisor = MathF.Lerp(40f, 20f, busyness);
                __result = __instance.MinimumNumberOfVillagersAtVillagerParty + (int)(village.Hearth / divisor);
                return false;
            }
        }

        /// <summary>RBM's prosperity-to-food divisor: a settlement eats one food per this many points of
        /// prosperity per day. Lowered from vanilla's <see cref="VanillaProsperityToEatOneFood"/> so a
        /// town eats ~10x as much, making food an actual constraint on growth. The divisor is a
        /// context-free property applied to every settlement; castles are exempted back to vanilla in
        /// <see cref="CastleConsumptionExemptionPatch"/>, since they never received RBM's compensating
        /// production rework (that is towns-only) and would otherwise starve perpetually.</summary>
        public const int RBMProsperityToEatOneFood = 4;

        /// <summary>Vanilla's prosperity-to-food divisor, kept for the castle consumption exemption.</summary>
        private const int VanillaProsperityToEatOneFood = 40;

        /// <summary>
        /// Lowers the divisor that converts a settlement's prosperity into daily food consumption
        /// (<c>consumption = Prosperity / NumberOfProsperityToEatOneFood</c>) from vanilla's 40 to
        /// <see cref="RBMProsperityToEatOneFood"/>, so a given prosperity eats roughly 10x more food.
        /// Paired with the reworked production above to make food supply an actual constraint on
        /// settlement growth.
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

                __result = RBMProsperityToEatOneFood;
                return false;
            }
        }

        /// <summary>
        /// Exempts CASTLES from the ~10x food consumption <see cref="ProsperityToEatOneFoodPatch"/>
        /// imposes. That divisor is a context-free property -- it cannot tell a castle from a town, and
        /// three town-side callers depend on it reading <see cref="RBMProsperityToEatOneFood"/> -- so the
        /// exemption is applied here, in the food-change model, where the settlement is known.
        ///
        /// A castle is a big self-sustaining village: it grows its own food and was never given RBM's
        /// town production rework, so charging it town-scale consumption starved it perpetually. This
        /// postfix adds back the difference between the RBM and the vanilla prosperity-consumption for a
        /// castle, restoring vanilla's food balance there while leaving towns on the heavier diet. The
        /// garrison term is unpatched and needs no correction; only the prosperity term is rebuilt,
        /// including the Master of Warcraft governor perk exactly as vanilla applies it (an AddFactor),
        /// so the delta cancels the perk out and stays faithful whether or not the castle has it.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementFoodModel), "CalculateTownFoodStocksChange")]
        private static class CastleConsumptionExemptionPatch
        {
            private static void Postfix(Town town, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null || !town.IsCastle)
                {
                    return;
                }

                // The model SUBTRACTS the prosperity-consumption term, so the heavier RBM divisor made
                // the food change more negative by (charged@RBM - charged@vanilla). Rebuild both at the
                // two divisors identically -- same Master of Warcraft factor on each -- and add the
                // difference back, putting the castle on vanilla's divisor.
                ExplainedNumber chargedRBM = new ExplainedNumber(town.Prosperity / (float)RBMProsperityToEatOneFood);
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Steward.MasterOfWarcraft, town, ref chargedRBM);
                ExplainedNumber chargedVanilla = new ExplainedNumber(town.Prosperity / (float)VanillaProsperityToEatOneFood);
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Steward.MasterOfWarcraft, town, ref chargedVanilla);

                __result.Add(chargedRBM.ResultNumber - chargedVanilla.ResultNumber, CastleSelfSufficiencyText);
            }
        }

        private static readonly TextObject CastleSelfSufficiencyText = new TextObject("{=RBM_CASTLE_SELF_SUFFICIENCY}Castle self-sufficiency");

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
                List<KeyValuePair<ItemObject, float>> ordered = new List<KeyValuePair<ItemObject, float>>(GetRates(village));
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
