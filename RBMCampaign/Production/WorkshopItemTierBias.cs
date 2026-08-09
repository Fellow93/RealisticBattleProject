using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Fattens the tail on which concrete item a workshop mints for an output category, so a smithy
    /// occasionally turns out a good sword and an artisan bench a decent garment -- without losing the
    /// bias toward cheap, town-culture goods.
    ///
    /// A production recipe names an <c>ItemCategory</c> to output, not an item; the pick among every
    /// item in that category happens in <c>WorkshopsCampaignBehavior.GetRandomItemAux</c>. Vanilla
    /// weights each candidate by <c>1 / (max(100, Value) + 100)</c> -- the raw inverse of its value --
    /// so the curve is steep: a 3000g item is ~15x rarer per-item than a 100g one, and once you fold in
    /// how few top-tier items exist per category, the dear ones essentially never appear. Towns end up
    /// spitting out an unbroken stream of the cheapest junk in each category.
    /// </summary>
    /// <remarks>
    /// This is a faithful re-implementation of <c>GetRandomItemAux</c> (a full-replacement Prefix) that
    /// changes exactly one line: the per-item weight is raised to <see cref="TierWeightExponent"/>.
    /// Everything else is preserved verbatim so the two vanilla behaviours we want to keep survive
    /// untouched --
    /// <list type="bullet">
    /// <item>the culture filter (<c>IsItemPreferredForTown</c>): items keep their town-culture-or-neutral
    /// preference, and because the outer <c>GetRandomItem</c> calls this method first with the town then
    /// again with none, the "prefer local, fall back to anything" two-pass is intact;</item>
    /// <item>the cheap bias: the weight is still monotonically decreasing in Value, just less steeply;</item>
    /// <item>the quality-modifier roll (<c>GetRandomItemModifierProductionScoreBased</c>) is unchanged.</item>
    /// </list>
    ///
    /// Because the exponent is applied to the whole vanilla weight, the odds ratio between any two items
    /// becomes the vanilla ratio raised to the exponent: at 0.5 the 100g-vs-3000g gap of ~15x flattens to
    /// ~4x, and the 100g-vs-1000g gap of ~5.5x to ~2.4x -- cheap still wins, dear now shows up sometimes.
    /// Set the exponent to 1.0 to get vanilla back, toward 0 to approach a flat (value-blind) draw.
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
        /// The single knob for this feature.
        /// </summary>
        private const double TierWeightExponent = 0.5;

        // Local copy of the private WorkshopsCampaignBehavior.IsItemPreferredForTown, so the culture
        // preference is reproduced exactly rather than reached for by reflection on a hot path.
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
                        if ((townComponent == null || IsItemPreferredForTown(candidate, townComponent))
                            && candidate.ItemCategory == itemGroupBase)
                        {
                            float vanillaWeight = 1f / ((float)Math.Max(100, candidate.Value) + 100f);
                            float weight = (float)Math.Pow(vanillaWeight, TierWeightExponent);
                            list.Add((candidate, weight));
                        }
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
