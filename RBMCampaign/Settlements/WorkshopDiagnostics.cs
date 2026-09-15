using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Records why a town's workshops are not producing.
    ///
    /// Measured throughput is about five production cycles a town a day against a theoretical
    /// twenty-six -- the sum of every workshop's <c>conversion_speed</c>, which is literally cycles per
    /// day. So roughly four cycles in five are being refused, and the goods that never get made are
    /// precisely the ones the DEMAND line reports unmet: beer, wine, oil, pottery, velvet. Reading the
    /// code narrows it to four possible refusals but cannot say which actually fires, and guessing from
    /// the shape of a formula has been wrong repeatedly here.
    ///
    /// So each refusal is counted, by reason, per settlement per day, and written as SHOPBLOCK.
    ///
    /// A second, narrower line, SHOPIDLE, answers a question the first cannot: of the artisans' own
    /// recipes, which were DUE to run and made nothing all day purely because the raw good was not on
    /// the shelf. SHOPBLOCK counts refused cycles across every shop; SHOPIDLE names the outputs a town
    /// went entirely without -- no arrows made anywhere, no oil, no tier-2 blades -- and the input each
    /// was short of. See <see cref="RecipeIdlePatch"/>.
    /// </summary>
    /// <remarks>
    /// Two of the four are worth stating in advance, because they predict different fixes:
    ///
    /// <list type="bullet">
    /// <item><b>no-input</b> means the material was not on the shelf when the workshop ticked. If this
    /// dominates for grain, the cause is contention -- citizens eat cheapest-first and strip the grain
    /// before the brewery buys any -- and the fix is about ordering or reservation, not about
    /// workshops.</item>
    /// <item><b>margin</b> is vanilla's <c>inputCost + 200 / ConversionSpeed</c> floor. That term scales
    /// INVERSELY with speed, so the slowest recipes demand the largest returns: fourteen of the game's
    /// sixty-three productions need between 2,000 and 8,000 denars of output value per cycle and can
    /// essentially never run. If this dominates, the fix is the floor itself.</item>
    /// </list>
    ///
    /// The recipe is named on the margin and no-input counts, because knowing that pottery is blocked
    /// tells you something and knowing that "a workshop" is blocked does not.
    ///
    /// Diagnostics only -- nothing here changes a decision. It patches the same behaviour class as
    /// <see cref="WorkshopPurse"/>, which is already patched safely; note that
    /// <c>DefaultClanFinanceModel</c> is NOT touched, having proved hostile to early patching.
    /// </remarks>
    public static class WorkshopDiagnostics
    {
        // Per settlement: reason -> count, plus the run/blocked totals under reserved keys.
        private static readonly Dictionary<Settlement, Dictionary<string, int>> _blocks =
            new Dictionary<Settlement, Dictionary<string, int>>();

        private const string Ran = "!ran";

        // A day of the artisans' shop, per recipe: how many cycles it made, how many due-attempts it
        // lost for want of inputs, and the first input it was short of. A recipe that ends the day with
        // Produced == 0 && InputFail > 0 is one that WAS due and made nothing purely because the shelf
        // was bare -- which is the thing being counted. See RecipeIdlePatch.
        private class RecipeDay
        {
            public int Produced;
            public int InputFail;
            public string Missing;
        }

        private static readonly Dictionary<Settlement, Dictionary<string, RecipeDay>> _recipeDay =
            new Dictionary<Settlement, Dictionary<string, RecipeDay>>();

        // Per settlement: output good -> how many production cycles the headroom gate skipped because the
        // town was already at its storage ceiling for that good. Written as SHOPCAP. Distinct from the
        // SHOPBLOCK reasons because a gated cycle is refused by RBM on purpose, before vanilla's own gates
        // are ever consulted, so it never reaches the counters those record. See RBMWorkshopCycle.Decide.
        private static readonly Dictionary<Settlement, Dictionary<string, int>> _capped =
            new Dictionary<Settlement, Dictionary<string, int>>();

        // The artisans recipe currently ticking, set for the span of one notable-shop production
        // attempt and null otherwise. Only ever non-null inside a HIDDEN workshop's cycle, which is what
        // scopes the input-sufficiency reading below to the artisans and away from the visible shops.
        private static Workshop _ctxShop;
        private static string _ctxKey;
        private static bool _ctxInputOk;
        private static string _ctxMissing;

        /// <summary>Drops the previous session's tallies. Diagnostics only, so a session hook is enough.</summary>
        public static void Reset()
        {
            _blocks.Clear();
            _recipeDay.Clear();
            _capped.Clear();
            _ctxShop = null;
            _ctxKey = null;
        }

        /// <summary>
        /// A stable, readable key for a recipe: its output plus its inputs. The output alone is not
        /// unique -- cow, sheep and hog all make meat -- so the inputs are appended to tell them apart.
        /// </summary>
        private static string RecipeKey(WorkshopType.Production production)
        {
            string output = (production.Outputs.Count > 0 && production.Outputs[0].Item1 != null)
                ? production.Outputs[0].Item1.StringId
                : "?";
            StringBuilder sb = new StringBuilder(output);
            foreach (var input in production.Inputs)
            {
                sb.Append('|').Append(input.Item1 != null ? input.Item1.StringId : "?");
            }
            return sb.ToString();
        }

        private static RecipeDay GetRecipeRecord(Settlement settlement, string key)
        {
            Dictionary<string, RecipeDay> byRecipe;
            if (!_recipeDay.TryGetValue(settlement, out byRecipe))
            {
                byRecipe = new Dictionary<string, RecipeDay>();
                _recipeDay[settlement] = byRecipe;
            }
            RecipeDay record;
            if (!byRecipe.TryGetValue(key, out record))
            {
                record = new RecipeDay();
                byRecipe[key] = record;
            }
            return record;
        }

        private static void Count(Settlement settlement, string reason)
        {
            if (settlement == null)
            {
                return;
            }
            Dictionary<string, int> byReason;
            if (!_blocks.TryGetValue(settlement, out byReason))
            {
                byReason = new Dictionary<string, int>();
                _blocks[settlement] = byReason;
            }
            int running;
            byReason.TryGetValue(reason, out running);
            byReason[reason] = running + 1;
        }

        /// <summary>
        /// Records one production cycle the headroom gate skipped for a full store, by the output it would
        /// have made. Called from <see cref="RBMWorkshopCycle"/>; a no-op when logging is off so no
        /// tally accumulates unread.
        /// </summary>
        public static void CountCapped(Settlement settlement, string output)
        {
            if (settlement == null || !EconomyLog.IsEnabled)
            {
                return;
            }
            Dictionary<string, int> byOutput;
            if (!_capped.TryGetValue(settlement, out byOutput))
            {
                byOutput = new Dictionary<string, int>();
                _capped[settlement] = byOutput;
            }
            int running;
            byOutput.TryGetValue(output, out running);
            byOutput[output] = running + 1;
        }

        /// <summary>The first input category a production could not find enough of, for naming a block.</summary>
        private static string MissingInput(WorkshopType.Production production, ItemRoster roster)
        {
            foreach (var input in production.Inputs)
            {
                ItemCategory category = input.Item1;
                int wanted = input.Item2;
                for (int i = 0; i < roster.Count; i++)
                {
                    ItemObject item = roster.GetItemAtIndex(i);
                    if (item != null && item.ItemCategory == category)
                    {
                        wanted -= roster.GetElementNumber(i);
                    }
                }
                if (wanted > 0)
                {
                    return (category != null) ? category.StringId : "?";
                }
            }
            return "?";
        }

        /// <summary>
        /// Catches the material shortage. Returning false here refuses the cycle before any of the
        /// economic gates are consulted, so it is measured first and separately.
        /// </summary>
        /// <remarks>
        /// The town is taken from the method's own argument rather than from the roster, because this is
        /// also called against a WAREHOUSE roster for player-owned shops -- the town is the reliable one.
        /// </remarks>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "DetermineItemRosterHasSufficientInputs")]
        private static class InputBlockPatch
        {
            private static void Postfix(WorkshopType.Production production, ItemRoster itemRoster, Town town, bool __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !EconomyLog.IsEnabled)
                {
                    return;
                }

                // If an artisans recipe is mid-tick, this call is its input check: remember whether it
                // passed and, if not, what it was short of, so the per-recipe tracker can classify it.
                if (_ctxShop != null && !__result)
                {
                    _ctxInputOk = false;
                    _ctxMissing = MissingInput(production, itemRoster);
                }

                if (__result || town == null)
                {
                    return;
                }
                Count(town.Settlement, "no-input:" + MissingInput(production, itemRoster));
            }
        }

        /// <summary>
        /// Counts, per artisans recipe per day, the cycles it made against the due-attempts it lost for
        /// want of inputs -- so the day's tally can name the recipes that were ready to run and made
        /// nothing only because the shelf was bare.
        /// </summary>
        /// <remarks>
        /// The context is set only for a hidden workshop, which is the artisans and nothing else, and it
        /// is read by the input-check postfix above during the same call. A finalizer clears it as well
        /// as the postfix, so a throwing cycle cannot leave one recipe's label standing over the next.
        ///
        /// Being due is implicit and free: <c>RunTownWorkshop</c> only calls this method when a recipe's
        /// accumulated progress has reached a whole cycle, so a slow recipe that was not ready that day
        /// is simply never seen here and cannot be miscounted as blocked.
        /// </remarks>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "TickOneProductionCycleForNotableWorkshop")]
        private static class RecipeIdlePatch
        {
            private static void Prefix(WorkshopType.Production production, Workshop workshop)
            {
                _ctxShop = null;
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !EconomyLog.IsEnabled
                    || workshop == null || workshop.WorkshopType == null || !workshop.WorkshopType.IsHidden)
                {
                    return;
                }
                _ctxShop = workshop;
                _ctxKey = RecipeKey(production);
                _ctxInputOk = true;
                _ctxMissing = null;
            }

            private static void Postfix(Workshop workshop, bool __result)
            {
                if (_ctxShop == null || _ctxKey == null)
                {
                    return;
                }
                Settlement settlement = (workshop != null) ? workshop.Settlement : null;
                if (settlement != null)
                {
                    RecipeDay record = GetRecipeRecord(settlement, _ctxKey);
                    if (__result)
                    {
                        record.Produced++;
                    }
                    else if (!_ctxInputOk)
                    {
                        record.InputFail++;
                        if (record.Missing == null)
                        {
                            record.Missing = _ctxMissing;
                        }
                    }
                }
                _ctxShop = null;
                _ctxKey = null;
            }

            private static void Finalizer()
            {
                _ctxShop = null;
                _ctxKey = null;
            }
        }

        /// <summary>
        /// Catches the economic refusals, and says which one fired.
        /// </summary>
        /// <remarks>
        /// The reason is now OBSERVED, not recomputed. <see cref="RBMWorkshopCycle"/> owns the decision
        /// outright and publishes the verdict it just reached, so this reads it back instead of keeping a
        /// second copy of the rule that has to be edited in step with the first -- which is exactly the
        /// coupling the workshop rewrite set out to remove. A count under "unknown" now means only that
        /// no verdict was published, i.e. RBM's gate did not run.
        /// </remarks>
        // Both owner paths, because a player shop refused every day used to leave no line at all: the
        // one workshop the player is watching was the one the log could not see. The player method has
        // an extra warehouse argument, which the shared postfix does not need and Harmony matches by name.
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanNotableWorkshopProduceThisCycle")]
        private static class NotableEconomicBlockPatch
        {
            private static void Postfix(WorkshopType.Production production, Workshop workshop,
                int inputMaterialCost, int outputIncome, bool effectCapital, bool __result)
            {
                RecordEconomicBlock(production, workshop, inputMaterialCost, outputIncome, effectCapital, __result);
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanPlayerWorkshopProduceThisCycle")]
        private static class PlayerEconomicBlockPatch
        {
            private static void Postfix(WorkshopType.Production production, Workshop workshop,
                int inputMaterialCost, int outputIncome, bool effectCapital, bool __result)
            {
                RecordEconomicBlock(production, workshop, inputMaterialCost, outputIncome, effectCapital, __result);
            }
        }

        private static void RecordEconomicBlock(WorkshopType.Production production, Workshop workshop,
            int inputMaterialCost, int outputIncome, bool effectCapital, bool __result)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !EconomyLog.IsEnabled || workshop == null)
            {
                return;
            }
            Settlement settlement = workshop.Settlement;
            if (settlement == null)
            {
                return;
            }

            if (__result)
            {
                Count(settlement, Ran);
                return;
            }

            RBMWorkshopCycle.Verdict verdict;
            if (!RBMWorkshopCycle.TryGetLastVerdict(workshop, out verdict) || verdict.Allowed)
            {
                Count(settlement, "unknown");
                return;
            }

            switch (verdict.Why)
            {
                case RBMWorkshopCycle.Reason.Glutted:
                    // Already reported on its own SHOPCAP line, and deliberately kept off this one: a
                    // glut is RBM refusing a cycle on purpose, not the shop's economics refusing it.
                    return;
                case RBMWorkshopCycle.Reason.Margin:
                    // Named by what it would have made -- the question about this gate has always been
                    // which recipes it is biting on.
                    Count(settlement, "margin:" + RBMWorkshopCycle.PrimaryOutput(production));
                    return;
                case RBMWorkshopCycle.Reason.TownBroke:
                    Count(settlement, "town-broke");
                    return;
                case RBMWorkshopCycle.Reason.ShopBroke:
                    Count(settlement, "shop-broke");
                    return;
                default:
                    Count(settlement, "unknown");
                    return;
            }
        }

        /// <summary>
        /// Writes a settlement's day of refusals and clears it.
        /// </summary>
        /// <remarks>
        /// The ratio at the front is the number to read: cycles run against cycles attempted. Everything
        /// after it says where the rest went.
        /// </remarks>
        public static void FlushDaily(Settlement settlement)
        {
            // Independent of the block tally below: a town can gate cycles on a full store while refusing
            // none for the reasons SHOPBLOCK counts, so this must not sit behind that early return.
            FlushCapped(settlement);

            Dictionary<string, int> byReason;
            if (settlement == null || !_blocks.TryGetValue(settlement, out byReason))
            {
                return;
            }
            _blocks.Remove(settlement);

            if (!EconomyLog.IsEnabled || byReason.Count == 0)
            {
                return;
            }

            int ran = 0;
            byReason.TryGetValue(Ran, out ran);

            int blocked = 0;
            List<KeyValuePair<string, int>> reasons = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> pair in byReason)
            {
                if (pair.Key == Ran)
                {
                    continue;
                }
                blocked += pair.Value;
                reasons.Add(pair);
            }

            reasons.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                return b.Value.CompareTo(a.Value);
            });

            StringBuilder breakdown = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in reasons)
            {
                breakdown.Append("  ").Append(pair.Key).Append(" x").Append(pair.Value);
            }

            EconomyLog.Log("SHOPBLOCK", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                "ran " + ran + " of " + (ran + blocked) + " attempted cycles  ·" + breakdown);

            FlushIdleRecipes(settlement);
        }

        /// <summary>
        /// Writes the artisans recipes that were due and made nothing all day for want of inputs, and
        /// clears the settlement's per-recipe tally.
        /// </summary>
        /// <remarks>
        /// A recipe that made even one cycle is not here, however many later attempts it lost -- the
        /// question this answers is which of the artisans' outputs the town went entirely without, and
        /// which raw good it was short of when it tried. Its own line, SHOPIDLE, rather than folded into
        /// the cycle counts above, because it is about outputs the town never saw at all.
        /// </remarks>
        private static void FlushIdleRecipes(Settlement settlement)
        {
            Dictionary<string, RecipeDay> byRecipe;
            if (settlement == null || !_recipeDay.TryGetValue(settlement, out byRecipe))
            {
                return;
            }
            _recipeDay.Remove(settlement);

            if (!EconomyLog.IsEnabled || byRecipe.Count == 0)
            {
                return;
            }

            List<string> idle = new List<string>();
            foreach (KeyValuePair<string, RecipeDay> pair in byRecipe)
            {
                RecipeDay record = pair.Value;
                if (record.Produced == 0 && record.InputFail > 0)
                {
                    idle.Add(record.Missing != null ? pair.Key + "(" + record.Missing + ")" : pair.Key);
                }
            }

            if (idle.Count == 0)
            {
                return;
            }
            idle.Sort();

            StringBuilder list = new StringBuilder();
            foreach (string entry in idle)
            {
                list.Append("  ").Append(entry);
            }

            EconomyLog.Log("SHOPIDLE", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                idle.Count + " artisan recipes made nothing for want of inputs  ·" + list);
        }

        /// <summary>
        /// Writes the production cycles the headroom gate skipped for a full store, by output, and clears
        /// the settlement's tally.
        /// </summary>
        /// <remarks>
        /// This is the counterpart to SHOPBLOCK for a refusal that is deliberate rather than a symptom: a
        /// good on this line is one the town makes faster than it uses and has now stopped topping up,
        /// which is the gate working, not a shortage. Read against the PRICE line's days-of-supply for that
        /// good -- a SHOPCAP output should be one sitting at or above the storage ceiling.
        /// </remarks>
        private static void FlushCapped(Settlement settlement)
        {
            Dictionary<string, int> byOutput;
            if (settlement == null || !_capped.TryGetValue(settlement, out byOutput))
            {
                return;
            }
            _capped.Remove(settlement);

            if (!EconomyLog.IsEnabled || byOutput.Count == 0)
            {
                return;
            }

            int total = 0;
            List<KeyValuePair<string, int>> outputs = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> pair in byOutput)
            {
                total += pair.Value;
                outputs.Add(pair);
            }

            outputs.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                return b.Value.CompareTo(a.Value);
            });

            StringBuilder breakdown = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in outputs)
            {
                breakdown.Append("  ").Append(pair.Key).Append(" x").Append(pair.Value);
            }

            EconomyLog.Log("SHOPCAP", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                total + " cycles skipped -- town at its storage ceiling  ·" + breakdown);
        }
    }
}
