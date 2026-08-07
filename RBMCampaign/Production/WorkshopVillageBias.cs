using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace RBMCampaign
{
    /// <summary>
    /// Biases which workshop a town rolls at game start toward the trades its bound villages feed.
    ///
    /// Vanilla already leans this way -- <c>DecideBestWorkshopType</c> weights each type by how much
    /// of its input the surrounding villages produce -- but only through the raw ItemCategory a
    /// village happens to output, so the link is soft and misses whenever the village's good and the
    /// workshop's input sit in different categories (a swine farm makes "hog", a tannery eats
    /// "hides", and the two never meet). This layer states the intended pairings outright: a town with
    /// a wheat farm should tend to grow a brewery, one with an iron mine a smithy, and so on.
    /// </summary>
    /// <remarks>
    /// Applied as a Postfix on the private per-type scorer <c>FindTotalInputDensityScore</c> rather
    /// than on the pick itself, so every vanilla feature that feeds the roll is kept intact -- the
    /// village-input density, the market-price term on re-rolls, the duplicate suppression, the
    /// hidden-type exclusion and the uniform fallback all still run; we only add to the score the
    /// weighted random then draws from. Because the pick is a weighted random over these scores,
    /// raising a type's score is exactly how you raise its chance.
    ///
    /// The bonus is ADDED, not multiplied, so it fires on the strength of the village's mere presence
    /// and does not depend on vanilla having found any matching input density -- which is what lets it
    /// cover the category-mismatch pairs (swine->tannery, and any other where the village good and the
    /// workshop input live in different ItemCategories).
    ///
    /// It enters in the same space vanilla's score lives in. Vanilla returns
    /// <c>pow(density * Frequency / (1 + 6*sameType)^3, 0.6)</c>; treating the table value as an
    /// added frequency against a unit density gives a contribution of
    /// <c>pow(bonus, 0.6) * (1 / (1 + 6*sameType)^1.8)</c> -- i.e. the same 0.6 power and the same
    /// duplicate suppression, so a town with one wheat farm strongly grows its first brewery but is no
    /// likelier than vanilla to stack a second. <see cref="BonusWeight"/> is the one absolute-scale
    /// knob: how decisively the table beats vanilla's own density signal.
    ///
    /// Covers game start and every re-roll (owner bankruptcy, war-driven transfer) alike, since all of
    /// them route the choice through this scorer. Gated on <c>rbmCampaignEnabled</c>; towns with no
    /// bound village in the table are left exactly as vanilla scored them.
    /// </remarks>
    public static class WorkshopVillageBias
    {
        /// <summary>
        /// How much a table pairing weighs against vanilla's own input-density score. Vanilla's
        /// game-start scores run from roughly 0.06 (a type the villages feed nothing) to ~6 (a type
        /// they feed strongly); at 4f a +6..+10 pairing lands well above that ceiling and reliably
        /// wins the roll without erasing the vanilla signal that breaks ties between unlisted types.
        /// This is the single value to turn if the bias reads too weak or too heavy-handed in play.
        /// </summary>
        private const float BonusWeight = 4f;

        /// <summary>
        /// village_type StringId -> the workshop types its presence should favour, and by how much
        /// (read as an added "frequency", the same unit vanilla's WorkshopType.Frequency uses). Keys
        /// are the lowercase ids from DefaultVillageTypes; workshop ids are the exact, case-sensitive
        /// ids from spworkshops.xml (note "wood_WorkshopType").
        /// </summary>
        private static readonly Dictionary<string, (string workshop, int freq)[]> Table
            = new Dictionary<string, (string, int)[]>
        {
            ["wheat_farm"]   = new[] { ("brewery", 6) },
            ["cattle_farm"]  = new[] { ("tannery", 3) },
            ["sheep_farm"]   = new[] { ("tannery", 2), ("wool_weavery", 6) },
            ["swine_farm"]   = new[] { ("tannery", 3) },
            ["lumberjack"]   = new[] { ("wood_WorkshopType", 10) },
            ["clay_mine"]    = new[] { ("pottery_shop", 6) },
            ["iron_mine"]    = new[] { ("smithy", 10) },
            ["silver_mine"]  = new[] { ("silversmithy", 10) },
            ["vineyard"]     = new[] { ("wine_press", 8) },
            ["flax_plant"]   = new[] { ("linen_weavery", 6) },
            ["olive_trees"]  = new[] { ("olive_press", 8) },
            ["silk_plant"]   = new[] { ("velvet_weavery", 10) },
        };

        // settlement -> (workshop StringId -> summed table bonus from its bound villages). A village's
        // type and trade binding are fixed for the campaign, so this is built once per settlement and
        // held for the session; cleared on session change (references dead settlements otherwise).
        private static readonly Dictionary<Settlement, Dictionary<string, int>> _bonusCache
            = new Dictionary<Settlement, Dictionary<string, int>>();

        // World-gen workshop picks are made before the economy log rolls over to the session's own
        // file (OnSessionLaunched), so their lines are held here and flushed into that file once it
        // is open -- keeping the start rolls in the same log as the session they belong to. Each is
        // (settlement name, fully-formed message); re-rolls during play are not buffered, they log live.
        private static readonly List<KeyValuePair<string, string>> _pendingStartRolls
            = new List<KeyValuePair<string, string>>();

        internal static void ResetForNewSession()
        {
            _bonusCache.Clear();
        }

        /// <summary>
        /// Emits the buffered world-gen workshop rolls, in the order they were made, into the now-open
        /// session log, then empties the buffer. Called from OnSessionLaunched after the log has rolled
        /// over; a no-op for loaded saves (no start rolls were made) and with logging off.
        /// </summary>
        internal static void FlushPendingStartRolls()
        {
            for (int i = 0; i < _pendingStartRolls.Count; i++)
            {
                EconomyLog.Log("WORKSHOP", _pendingStartRolls[i].Key, _pendingStartRolls[i].Value);
            }
            _pendingStartRolls.Clear();
        }

        private static Dictionary<string, int> BonusMapFor(Settlement settlement)
        {
            Dictionary<string, int> map;
            if (_bonusCache.TryGetValue(settlement, out map))
            {
                return map;
            }

            map = new Dictionary<string, int>();
            // TradeBound, not Bound: castle-bound villages trade with a separate town, and that is the
            // town whose workshops their goods should sway -- the same set vanilla's scorer sums over.
            foreach (Village village in Village.All)
            {
                if (village == null || village.VillageType == null || village.TradeBound != settlement)
                {
                    continue;
                }

                string villageType = village.VillageType.StringId;
                (string workshop, int freq)[] entries;
                if (villageType == null || !Table.TryGetValue(villageType, out entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    int current;
                    map.TryGetValue(entry.workshop, out current);
                    map[entry.workshop] = current + entry.freq;
                }
            }

            _bonusCache[settlement] = map;
            return map;
        }

        private static int CountSameType(Settlement settlement, WorkshopType type)
        {
            Workshop[] shops = settlement.Town != null ? settlement.Town.Workshops : null;
            if (shops == null)
            {
                return 0;
            }
            int count = 0;
            for (int i = 0; i < shops.Length; i++)
            {
                if (shops[i] != null && shops[i].WorkshopType == type)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// The town's whole slate of village bias, "smithy+10, tannery+5", strongest first -- the
        /// summed table bonuses in play when the roll was made, so a pick can be read against them.
        /// </summary>
        private static string DescribeBias(Dictionary<string, int> map)
        {
            List<KeyValuePair<string, int>> entries = new List<KeyValuePair<string, int>>(map);
            entries.Sort((a, b) => b.Value.CompareTo(a.Value));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(entries[i].Key).Append('+').Append(entries[i].Value);
            }
            return sb.ToString();
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "FindTotalInputDensityScore")]
        private static class InputDensityScorePatch
        {
            private static void Postfix(Settlement settlement, WorkshopType workshopType, ref float __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled
                    || settlement == null
                    || settlement.Town == null
                    || workshopType == null)
                {
                    return;
                }

                Dictionary<string, int> map = BonusMapFor(settlement);
                if (map.Count == 0)
                {
                    return;
                }

                int bonus;
                if (!map.TryGetValue(workshopType.StringId, out bonus) || bonus <= 0)
                {
                    return;
                }

                int sameType = CountSameType(settlement, workshopType);
                float suppression = 1f / (float)Math.Pow(1f + sameType * 6f, 1.8);
                __result += (float)Math.Pow(bonus, 0.6) * BonusWeight * suppression;
            }
        }

        /// <summary>
        /// Writes down each town's actual pick against the bias that shaped it -- the one place the
        /// feature is observable, since the choice is a weighted random the score patch only nudges.
        /// Logs only rolls where the town had a table-relevant village (an empty slate is just
        /// vanilla's own choice, with no bias to inspect), and only with the economy log switched on.
        /// </summary>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "DecideBestWorkshopType")]
        private static class ChoiceLogPatch
        {
            private static void Postfix(Settlement currentSettlement, bool atGameStart, WorkshopType __result)
            {
                if (!EconomyLog.IsEnabled
                    || currentSettlement == null
                    || currentSettlement.Town == null
                    || __result == null)
                {
                    return;
                }

                Dictionary<string, int> map = BonusMapFor(currentSettlement);
                if (map.Count == 0)
                {
                    return;
                }

                int chosenBonus;
                map.TryGetValue(__result.StringId, out chosenBonus);

                string name = currentSettlement.Name != null
                    ? currentSettlement.Name.ToString()
                    : currentSettlement.StringId;

                string message = "rolled " + __result.StringId
                    + (chosenBonus > 0 ? " [favoured +" + chosenBonus + "]" : " [unfavoured]")
                    + "  ·  " + (atGameStart ? "start" : "re-roll")
                    + "  ·  village bias: " + DescribeBias(map);

                if (atGameStart)
                {
                    // Held until the session log is open; see FlushPendingStartRolls.
                    _pendingStartRolls.Add(new KeyValuePair<string, string>(name, message));
                }
                else
                {
                    EconomyLog.Log("WORKSHOP", name, message);
                }
            }
        }
    }
}
