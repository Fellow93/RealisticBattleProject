using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting;
using TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting.Refinement;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.TwoDimension;

namespace RBMCampaign
{
    /// <summary>
    /// SMITHY REFINING = THE WORKSHOP STEEL CHAIN.
    ///
    /// The two ways to make steel in RBM use different data sources:
    ///   * Workshops read the ingredient/output ratios from XML
    ///     (<c>RBMXML/RBMEconomy_workshops_artisans.xml</c>, the smithy workshop's iron/steel
    ///     <c>&lt;Production&gt;</c> blocks).
    ///   * The player's smithy reads them from code — <see cref="DefaultSmithingModel.GetRefiningFormulas"/>,
    ///     which yields one <see cref="Crafting.RefiningFormula"/> per refine action.
    ///
    /// This postfix replaces the iron- and steel-tier formulas the game yields with RBM's recipe set (below).
    /// The charcoal and iron-ore formulas, and every perk gate (SteelMaker / SteelMaker2 / SteelMaker3, which
    /// decide whether a steel formula is yielded at all), are left exactly as vanilla produced them — we key off
    /// the formula's Output, so if a tier's formula is absent (perk not unlocked) that whole case is skipped and
    /// its recipes never appear. A single tier may emit SEVERAL recipes (the refine list is one row per formula,
    /// with no dedup by output), which is how the steel tiers below offer multiple routes.
    ///
    /// Recipes (names: Crude=Iron1, Wrought=Iron2, Iron=Iron3, Steel=Iron4, Fine Steel=Iron5, Thamaskene=Iron6):
    ///   Hardwood   (Wood) : 1 Planks -> 1 Hardwood                (planks charged out-of-band; always available)
    ///   Wrought    (Iron2): 1 Crude + 1 Charcoal  -> 1 Wrought
    ///   Iron       (Iron3): 1 Crude + 1 Charcoal  -> 1 Iron
    ///   Steel      (Iron4): 1 Crude + 2 Charcoal  -> 1 Steel  |  1 Iron + 1 Charcoal  |  1 Wrought + 1 Charcoal
    ///   Fine steel (Iron5): 1 Steel + 1 Charcoal  -> 1 FineSteel  |  1 Iron + 1 Charcoal  |  1 Crude + 2 Charcoal
    ///   Thamaskene (Iron6): 1 Crude + 2 Charcoal + silver  ->  1 Thamaskene  |  1 FineSteel + 1 Charcoal + silver
    ///
    /// OUT-OF-BAND INGREDIENTS: some recipes want an ingredient that isn't a <see cref="CraftingMaterials"/> value
    /// (silver ore for Thamaskene; planks for hardwood). A <see cref="Crafting.RefiningFormula"/> can only carry
    /// materials from that enum, so those ingredients can't live in the formula. They are handled by
    /// <see cref="OutOfBandIngredients"/> plus the two patches below: <see cref="RefineExtraIngredientRow"/> injects
    /// the missing ingredient as an input tile on the matching refine action (which makes the built-in availability
    /// check gate the button on it for free), and <see cref="RefineExtraIngredientCharge"/> deducts it in
    /// DoRefinement. The plank->hardwood recipe therefore carries NO in-formula input at all — the plank is entirely
    /// out-of-band, so the formula just mints hardwood and the plank tile/charge supply the cost.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSmithingModel), nameof(DefaultSmithingModel.GetRefiningFormulas))]
    internal static class SteelRefining
    {
        private const string MaterialBrushName = "Crafting.Material.Brush";

        /// <summary>
        /// Ingredients a recipe consumes that aren't <see cref="CraftingMaterials"/> and so can't sit in the formula:
        /// keyed by the recipe's Output, each names the trade-good item to charge, how many, the brush icon state to
        /// render, and the sprite that state uses (all in the always-resident ui_group1 category).
        /// </summary>
        internal static readonly OutOfBandIngredient[] OutOfBandIngredients =
        {
            new OutOfBandIngredient(CraftingMaterials.Iron6, "silver", 1, "Silver", "General\\Icons\\Production\\silver"),
            new OutOfBandIngredient(CraftingMaterials.Wood, "planks", 1, "Planks", "General\\Icons\\Production\\hardwood"),
        };

        private static void Postfix(ref IEnumerable<Crafting.RefiningFormula> __result)
        {
            __result = Rewrite(__result);
        }

        private static IEnumerable<Crafting.RefiningFormula> Rewrite(IEnumerable<Crafting.RefiningFormula> original)
        {
            // Planks -> hardwood. Zero in-formula inputs: the plank is charged out-of-band (see OutOfBandIngredients),
            // so the formula only mints the hardwood and the injected plank tile supplies + gates the cost.
            yield return new Crafting.RefiningFormula(CraftingMaterials.Wood, 0, CraftingMaterials.Wood, 0, CraftingMaterials.Wood, 1);

            foreach (Crafting.RefiningFormula formula in original)
            {
                switch (formula.Output)
                {
                    case CraftingMaterials.Iron2: // wrought iron: crude iron + charcoal
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron1, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron2);
                        break;
                    case CraftingMaterials.Iron3: // iron: crude iron + charcoal
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron1, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron3);
                        break;
                    case CraftingMaterials.Iron4: // steel: three routes
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron1, 1, CraftingMaterials.Charcoal, 2, CraftingMaterials.Iron4);
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron3, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron4);
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron2, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron4);
                        break;
                    case CraftingMaterials.Iron5: // fine steel: three routes
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron4, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron5);
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron3, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron5);
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron1, 1, CraftingMaterials.Charcoal, 2, CraftingMaterials.Iron5);
                        break;
                    case CraftingMaterials.Iron6: // thamaskene: two routes; each also costs silver, charged out-of-band
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron1, 1, CraftingMaterials.Charcoal, 2, CraftingMaterials.Iron6);
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron5, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron6);
                        break;
                    default:
                        yield return formula;
                        break;
                }
            }
        }

        internal static OutOfBandIngredient GetIngredientFor(CraftingMaterials output)
        {
            foreach (OutOfBandIngredient ingredient in OutOfBandIngredients)
            {
                if (ingredient.Output == output)
                {
                    return ingredient;
                }
            }
            return null;
        }

        internal static ItemObject GetItem(string itemId)
        {
            return MBObjectManager.Instance?.GetObject<ItemObject>(itemId);
        }

        /// <summary>
        /// Adds the ingredient's icon state (and its "Big" variant) to the crafting material brush, pointing at its
        /// sprite, so <see cref="CraftingMaterialVisualBrushWidget"/> can render it. Idempotent and self-healing: it
        /// re-checks each call, so a brush hot-reload that wipes the added state is repaired the next time a refine
        /// screen builds. Done in code rather than as a shipped brush file so RBM keeps its no-GUI-asset footprint;
        /// the sprites live in the always-resident ui_group1 category, so they resolve without loading a category.
        /// </summary>
        internal static void EnsureMaterialStyle(OutOfBandIngredient ingredient)
        {
            try
            {
                Brush brush = UIResourceManager.BrushFactory?.GetBrush(MaterialBrushName);
                if (brush == null || brush.GetStyle(ingredient.MaterialState) != null)
                {
                    return;
                }
                Sprite sprite = UIResourceManager.SpriteData?.GetSprite(ingredient.SpriteName);
                if (sprite == null)
                {
                    return;
                }
                AddMaterialState(brush, ingredient.MaterialState, sprite);
                AddMaterialState(brush, ingredient.MaterialState + "Big", sprite);
            }
            catch
            {
                // Cosmetic only; never let an icon tweak disturb the crafting screen.
            }
        }

        private static void AddMaterialState(Brush brush, string stateName, Sprite sprite)
        {
            if (brush.GetStyle(stateName) != null)
            {
                return;
            }
            Style style = new Style(brush.Layers)
            {
                Name = stateName,
                DefaultStyle = brush.DefaultStyle
            };
            StyleLayer layer = style.GetLayer("Default");
            if (layer != null)
            {
                layer.Sprite = sprite;
            }
            brush.AddStyle(style);
        }
    }

    /// <summary>A recipe ingredient that can't live in a <see cref="Crafting.RefiningFormula"/>; see <see cref="SteelRefining"/>.</summary>
    internal sealed class OutOfBandIngredient
    {
        internal readonly CraftingMaterials Output;
        internal readonly string ItemId;
        internal readonly int Count;
        internal readonly string MaterialState;
        internal readonly string SpriteName;

        internal OutOfBandIngredient(CraftingMaterials output, string itemId, int count, string materialState, string spriteName)
        {
            Output = output;
            ItemId = itemId;
            Count = count;
            MaterialState = materialState;
            SpriteName = spriteName;
        }
    }

    /// <summary>
    /// Shows an out-of-band ingredient (silver on Thamaskene, planks on hardwood) as an input tile on the matching
    /// refine action. <see cref="Crafting.RefiningFormula"/> can't carry it, so we append a
    /// <see cref="CraftingResourceItemVM"/> after the row is built. Because <see cref="RefinementActionItemVM"/>'s own
    /// availability check loops over InputMaterials and reads each row's real
    /// <see cref="CraftingResourceItemVM.ResourceItem"/>, giving that row the actual item makes the refine button
    /// auto-disable when the party is short — no extra gating needed. The row renders from ResourceItemStringId /
    /// ResourceName / ResourceMaterialTypeAsStr like any other input, so no prefab change is required.
    /// </summary>
    [HarmonyPatch(typeof(RefinementActionItemVM), MethodType.Constructor, new Type[] { typeof(Crafting.RefiningFormula), typeof(Action<RefinementActionItemVM>) })]
    internal static class RefineExtraIngredientRow
    {
        private static void Postfix(RefinementActionItemVM __instance)
        {
            OutOfBandIngredient ingredient = SteelRefining.GetIngredientFor(__instance.RefineFormula.Output);
            if (ingredient == null)
            {
                return;
            }
            ItemObject item = SteelRefining.GetItem(ingredient.ItemId);
            if (item == null)
            {
                return;
            }

            // Make sure the material brush has the ingredient's icon state before the tile below asks for it.
            SteelRefining.EnsureMaterialStyle(ingredient);

            // Build the row off any enum value, then overwrite its display + real item. The icon comes from the
            // CraftingMaterialVisualBrushWidget, which keys off ResourceMaterialTypeAsStr -> so point it at the
            // brush state we just ensured, not the IronOre it was constructed with.
            CraftingResourceItemVM row = new CraftingResourceItemVM(CraftingMaterials.IronOre, ingredient.Count);
            AccessTools.Property(typeof(CraftingResourceItemVM), nameof(CraftingResourceItemVM.ResourceItem))?.SetValue(row, item);
            string name = item.Name?.ToString() ?? ingredient.MaterialState;
            row.ResourceName = name;
            row.ResourceItemStringId = item.StringId;
            row.ResourceMaterialTypeAsStr = ingredient.MaterialState;
            row.ResourceHint = new HintViewModel(new TextObject("{=!}" + name));

            __instance.InputMaterials.Add(row);
            // Re-run the availability check now that the ingredient is part of the input list (the ctor ran it without).
            __instance.RefreshDynamicProperties();
        }
    }

    /// <summary>
    /// Charges the out-of-band ingredient when the matching refine actually happens. The UI row above already
    /// prevents the player from starting the action while short; the Min guard keeps this safe against any code path
    /// that reaches DoRefinement with a short inventory (never goes negative).
    /// </summary>
    [HarmonyPatch(typeof(CraftingCampaignBehavior), nameof(CraftingCampaignBehavior.DoRefinement))]
    internal static class RefineExtraIngredientCharge
    {
        private static void Postfix(Crafting.RefiningFormula refineFormula)
        {
            OutOfBandIngredient ingredient = SteelRefining.GetIngredientFor(refineFormula.Output);
            if (ingredient == null)
            {
                return;
            }
            ItemObject item = SteelRefining.GetItem(ingredient.ItemId);
            if (item == null)
            {
                return;
            }
            ItemRoster roster = MobileParty.MainParty?.ItemRoster;
            if (roster == null)
            {
                return;
            }
            int take = Math.Min(roster.GetItemNumber(item), ingredient.Count);
            if (take > 0)
            {
                roster.AddToCounts(item, -take);
            }
        }
    }
}
