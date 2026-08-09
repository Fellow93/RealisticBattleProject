using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Reshapes which concrete item a workshop mints for an output category so higher tiers -- and the
    /// occasional imported piece -- surface sometimes, without losing the lean toward cheap, local goods.
    ///
    /// A production recipe names an <c>ItemCategory</c> to output, not an item; the pick among every
    /// item in that category happens in <c>WorkshopsCampaignBehavior.GetRandomItemAux</c>. Vanilla does
    /// two things we soften here:
    /// <list type="number">
    /// <item>it weights each candidate by <c>1 / (max(100, Value) + 100)</c> -- the raw inverse of its
    /// value -- so the curve is steep: a 3000g item is ~15x rarer per-item than a 100g one, and once you
    /// fold in how few top-tier items exist per category, the dear ones essentially never appear;</item>
    /// <item>it applies culture as a HARD filter: items of a foreign specific culture are dropped
    /// outright, and a second unfiltered pass only runs when the local list came back empty -- so as long
    /// as the town has any local/neutral item in a category, a foreign one is never made there.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// This is a faithful re-implementation of <c>GetRandomItemAux</c> (a full-replacement Prefix) that
    /// turns both of those from walls into biases while keeping their direction:
    /// <list type="bullet">
    /// <item>the cheap bias: the value weight is raised to <see cref="TierWeightExponent"/> (0.5), so the
    /// weight still decreases monotonically in Value, just less steeply -- the 100g-vs-3000g odds gap
    /// flattens from ~15x to ~4x, and 100g-vs-1000g from ~5.5x to ~2.4x. Cheap still wins most rolls;</item>
    /// <item>the culture bias: instead of excluding foreign-culture items, they are kept at
    /// <see cref="ForeignCultureFactor"/> (0.1x) of their weight -- local/neutral/untagged items keep full
    /// weight, so the town's own culture still dominates, but an imported piece shows up now and then.
    /// This also removes vanilla's "empty local list -> fall back to fully unfiltered" cliff: the single
    /// weighted draw already spans both sets, so the outer <c>GetRandomItem</c>'s second pass, which only
    /// fires on an empty result, is now effectively dead;</item>
    /// <item>the quality-modifier roll (<c>GetRandomItemModifierProductionScoreBased</c>) is unchanged.</item>
    /// </list>
    ///
    /// The picker receives only a category and a town, never the workshop, so this necessarily covers
    /// every workshop type alike (named shops and the hidden artisan bench). Gated on
    /// <c>rbmCampaignEnabled</c>; disabled, the Prefix bows out and vanilla runs.
    /// </remarks>
    public static class WorkshopItemTierBias
    {
        /// <summary>
        /// Exponent applied to vanilla's inverse-value weight. 1.0 == vanilla (steep, cheapest dominate);
        /// 0.0 == flat (value ignored). At 0.5 the odds ratio between a cheap and a dear item is the
        /// square root of vanilla's, so higher tiers surface occasionally while cheap goods still lead.
        /// </summary>
        private const double TierWeightExponent = 0.5;

        /// <summary>
        /// Weight multiplier for an item whose specific culture is neither the town's nor neutral. 1.0 ==
        /// no culture bias; 0.0 == vanilla's hard exclusion. At 0.1 a foreign piece is ten times rarer
        /// than an equivalent local one, so the home culture stays firmly dominant but imports appear.
        /// </summary>
        private const float ForeignCultureFactor = 0.1f;

        // Local copy of the private WorkshopsCampaignBehavior.IsItemPreferredForTown, so the culture
        // test is reproduced exactly rather than reached for by reflection on a hot path. Now read as a
        // weight condition (full vs ForeignCultureFactor) rather than an include/exclude gate.
        private static bool IsItemPreferredForTown(ItemObject item, Town townComponent)
        {
            if (item.Culture != null && item.Culture.StringId != "neutral_culture")
            {
                return item.Culture == townComponent.Culture;
            }
            return true;
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "GetRandomItemAux")]
        private static class ItemTierWeightPatch
        {
            private static bool Prefix(
                ItemCategory itemGroupBase,
                Town townComponent,
                Dictionary<ItemCategory, List<ItemObject>> ____itemsInCategory,
                ref EquipmentElement __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return true; // hand back to vanilla
                }

                ItemObject itemObject = null;
                ItemModifier itemModifier = null;
                List<(ItemObject, float)> list = new List<(ItemObject, float)>();

                List<ItemObject> value;
                if (____itemsInCategory.TryGetValue(itemGroupBase, out value))
                {
                    foreach (ItemObject candidate in value)
                    {
                        if (candidate.ItemCategory != itemGroupBase)
                        {
                            continue;
                        }

                        float vanillaWeight = 1f / ((float)Math.Max(100, candidate.Value) + 100f);
                        float weight = (float)Math.Pow(vanillaWeight, TierWeightExponent);

                        // Culture as a bias, not a wall: a foreign-culture item is kept but heavily
                        // discounted. townComponent is null only on the outer method's fallback pass,
                        // where there is no town culture to weigh against -- leave those at full weight.
                        if (townComponent != null && !IsItemPreferredForTown(candidate, townComponent))
                        {
                            weight *= ForeignCultureFactor;
                        }

                        list.Add((candidate, weight));
                    }

                    itemObject = MBRandom.ChooseWeighted(list);
                    ItemModifierGroup itemModifierGroup = itemObject?.ItemComponent?.ItemModifierGroup;
                    if (itemModifierGroup != null)
                    {
                        itemModifier = itemModifierGroup.GetRandomItemModifierProductionScoreBased();
                    }
                }

                __result = new EquipmentElement(itemObject, itemModifier);
                return false;
            }
        }
    }
}
