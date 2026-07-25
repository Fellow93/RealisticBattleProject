using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Lifts the two caps vanilla puts on how much of one item category a caravan may buy in a single
    /// town visit.
    ///
    /// Both numbers are calibrated for vanilla's price list, where every trade good sits in a narrow
    /// band around a hundred denars. <see cref="TradeGoodValues"/> replaces that list with historical
    /// prices on a ten-times scale, and the band is no longer narrow: iron ore is worth 1 denar a lot
    /// and velvet 26,500. Against that spread a flat 1,500-denar budget and a flat 300-lot ceiling
    /// stop being caps on greed and start being arbitrary rules about WHICH goods a caravan is allowed
    /// to carry.
    ///
    /// The 300-lot ceiling binds at the cheap end. At a denar a lot, iron ore hits it after 300 denars
    /// of a budget that had thousands left in it, and the caravan walks away from a full warehouse. The
    /// 1,500-denar budget binds at the expensive end, and binds absolutely: <c>BuyCategory</c> sizes its
    /// purchase as <c>RoundRandomized(budget / itemPrice)</c>, so a budget below the price of a single
    /// lot rounds to zero almost every time. At 1,500 against 26,500 a caravan cannot buy velvet at all
    /// -- not "rarely", but structurally, on every visit to every town in Calradia. The same holds for
    /// cotton, wine and fish. The dearest goods in the game, the ones long-haul trade exists for, were
    /// invisible to the trade AI.
    ///
    /// So both are raised by the factor the price list was: ten. What limits a caravan afterwards is
    /// what should -- the weight its mules can carry, and the half of its purse <c>BuyCategory</c> will
    /// already not exceed.
    /// </summary>
    public static class CaravanTradeVolume
    {
        /// <summary>
        /// Ten times vanilla's 1,500, the same factor <see cref="TradeGoodValues"/> moved the price list by.
        /// </summary>
        private const int LandCategoryBudget = 15000;

        /// <summary>
        /// Ten times the 3,000 War Sails gives a convoy, keeping the DLC's own doubling of the land figure.
        /// </summary>
        private const int NavalCategoryBudget = 30000;

        /// <summary>
        /// Stands in for "no ceiling". A real number rather than <c>int.MaxValue</c> because
        /// <c>BuyCategory</c> compares a running total against it; nothing in the game can approach this,
        /// and the loop is bounded anyway by the gold budget, the town's stock and the caravan's capacity.
        /// </summary>
        private const int UnitCap = 1000000;

        /// <summary>
        /// The land model. With War Sails installed this still covers land caravans, since the DLC's
        /// decorator hands everything that is not a convoy straight to this method.
        /// </summary>
        [HarmonyPatch(typeof(DefaultCaravanModel), "GetMaxGoldToSpendOnOneItemCategory")]
        private static class MaxGoldPerCategoryPatch
        {
            private static void Postfix(ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }

                __result = LandCategoryBudget;
            }
        }

        /// <summary>
        /// The lot ceiling. One patch serves both models: War Sails does not resolve this one itself,
        /// it forwards to the base model's property.
        /// </summary>
        [HarmonyPatch(typeof(DefaultCaravanModel), "MaxNumberOfItemsToBuyFromSingleCategory", MethodType.Getter)]
        private static class MaxItemsPerCategoryPatch
        {
            private static void Postfix(ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }

                __result = UnitCap;
            }
        }

        /// <summary>
        /// Lets the trade AI see what a lot of a good is actually worth.
        ///
        /// <c>UpdateAverageValues</c> builds the per-category average value the buy decision is sized
        /// against, and folds each item in at <c>MathF.Min(500, item.Value)</c>. In vanilla that clamp
        /// catches almost nothing -- the dearest trade good in the game is under it. On the repriced
        /// list it catches the entire top of the market: velvet's 26,500 registers as 500, the same
        /// figure as a lot of felt, so <c>CalculateBuyValue</c> cannot produce a budget anywhere near
        /// the price of the thing it is deciding whether to buy, and the caravan concludes there is no
        /// trade to be done in the most valuable cargo in Calradia.
        ///
        /// The cache is rebuilt here, unclamped, for the categories the trade AI actually reads --
        /// trade goods and animals, the two <c>CalculateBuyValue</c> and
        /// <c>CalculateTownBuyScoreForCategory</c> are gated on. Everything else keeps vanilla's clamp:
        /// nothing reads those entries today, and leaving them as the game computed them means this
        /// patch cannot surprise a reader that appears later.
        ///
        /// <c>UpdateAverageValues</c> runs once, from the caravan behaviour's session launch, which is
        /// after <see cref="TradeGoodValues"/> has repriced every good at load -- so the values read
        /// here are the final ones.
        /// </summary>
        [HarmonyPatch(typeof(CaravansCampaignBehavior), "UpdateAverageValues")]
        private static class CategoryValuePerceptionPatch
        {
            private static readonly FieldInfo AverageValuesField =
                AccessTools.Field(typeof(CaravansCampaignBehavior), "_averageValuesCached");

            private static void Postfix(CaravansCampaignBehavior __instance)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || AverageValuesField == null)
                {
                    return;
                }

                Dictionary<ItemCategory, float> cache =
                    AverageValuesField.GetValue(__instance) as Dictionary<ItemCategory, float>;
                if (cache == null)
                {
                    return;
                }

                Dictionary<ItemCategory, (float sum, int count)> totals =
                    new Dictionary<ItemCategory, (float, int)>();

                foreach (ItemObject item in Items.All)
                {
                    if (!item.IsReady || item.ItemCategory == null)
                    {
                        continue;
                    }

                    float value = (item.ItemCategory.IsTradeGood || item.ItemCategory.IsAnimal)
                        ? item.Value
                        : MathF.Min(500, item.Value);

                    (float sum, int count) running;
                    totals.TryGetValue(item.ItemCategory, out running);
                    totals[item.ItemCategory] = (running.sum + value, running.count + 1);
                }

                // Same shape as the method this follows: a category with no items is worth 1, not 0,
                // which keeps it a harmless multiplicand rather than erasing the score it appears in.
                foreach (ItemCategory category in ItemCategories.All)
                {
                    (float sum, int count) totalsForCategory;
                    cache[category] = totals.TryGetValue(category, out totalsForCategory)
                        ? totalsForCategory.sum / totalsForCategory.count
                        : 1f;
                }
            }
        }

        /// <summary>
        /// War Sails does not extend the caravan model, it wraps it, and for a convoy it answers the
        /// budget question itself and returns 3,000 without ever reaching the base method the patch above
        /// sits on. Left alone, sea trade would keep vanilla's ceiling while land trade lost it.
        ///
        /// Reflected onto the DLC type by name so RBM keeps no build- or load-time dependency on an
        /// optional module: with War Sails absent the type does not resolve, <see cref="Prepare"/> returns
        /// false and the patch is never applied.
        /// </summary>
        [HarmonyPatch]
        private static class NavalMaxGoldPerCategoryPatch
        {
            private const string NavalModelTypeName = "NavalDLC.GameComponents.NavalDLCCaravanModel";

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(NavalModelTypeName + ":GetMaxGoldToSpendOnOneItemCategory");
            }

            private static void Postfix(MobileParty caravan, ItemCategory itemCategory, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || caravan == null)
                {
                    return;
                }

                // Mirrors the decorator's own branch. Anything not a convoy was answered by the base model,
                // where the land patch has already set the figure.
                if (caravan.HasNavalNavigationCapability)
                {
                    __result = NavalCategoryBudget;
                }
            }
        }
    }
}
