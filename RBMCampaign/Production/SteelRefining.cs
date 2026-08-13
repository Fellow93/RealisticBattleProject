using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// SMITHY STEEL REFINING = THE WORKSHOP STEEL RECIPE.
    ///
    /// The two ways to make steel in RBM use different data sources:
    ///   * Workshops read the ingredient/output ratios from XML
    ///     (<c>RBMXML/RBMEconomy_workshops_artisans.xml</c>, the "Steel"/"Fine steel"/"Thamaskene steel"
    ///     <c>&lt;Production&gt;</c> blocks).
    ///   * The player's smithy reads them from code — <see cref="DefaultSmithingModel.GetRefiningFormulas"/>,
    ///     which yields one <see cref="Crafting.RefiningFormula"/> per refine action.
    ///
    /// This postfix rewrites only the three steel-tier formulas the game yields so the smithy matches the
    /// workshop ratios (1:1 iron:charcoal, no Iron1 byproduct — vanilla smithing charged 2 ingots per output
    /// and spat one back). Every other formula (charcoal, iron ore, iron1, iron2) and every perk gate
    /// (SteelMaker / SteelMaker2 / SteelMaker3, which decide whether a steel formula is yielded at all) is
    /// left exactly as vanilla produced it — we key off the formula's Output, so if a steel formula is absent
    /// (perk not unlocked) there is nothing to rewrite.
    ///
    /// Workshop → smithy mapping (ratios, small counts):
    ///   Steel      (Iron4): 1 Iron2 + 1 Charcoal -> 1 Iron4      (workshop: ironIngot2 + charcoal -> ironIngot4)
    ///   Fine steel (Iron5): 1 Iron3 + 1 Charcoal -> 1 Iron5      (workshop: ironIngot3 + charcoal -> ironIngot5)
    ///   Thamaskene (Iron6): 1 Iron1 + 2 Charcoal -> 1 Iron6      (workshop: ironIngot1 + 2*charcoal + silver -> ironIngot6)
    ///
    /// NOTE: the workshop's Thamaskene recipe also consumes silver, but <see cref="Crafting.RefiningFormula"/>
    /// only holds TWO inputs (Input1/Input2), so silver cannot be represented in a refine formula and is dropped
    /// here. The 1 iron : 2 charcoal ratio is preserved.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSmithingModel), nameof(DefaultSmithingModel.GetRefiningFormulas))]
    internal static class SteelRefining
    {
        private static void Postfix(ref IEnumerable<Crafting.RefiningFormula> __result)
        {
            __result = Rewrite(__result);
        }

        private static IEnumerable<Crafting.RefiningFormula> Rewrite(IEnumerable<Crafting.RefiningFormula> original)
        {
            foreach (Crafting.RefiningFormula formula in original)
            {
                switch (formula.Output)
                {
                    case CraftingMaterials.Iron4: // steel
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron2, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron4);
                        break;
                    case CraftingMaterials.Iron5: // fine steel
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron3, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron5);
                        break;
                    case CraftingMaterials.Iron6: // thamaskene steel (silver input cannot be encoded, dropped)
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron1, 1, CraftingMaterials.Charcoal, 2, CraftingMaterials.Iron6);
                        break;
                    default:
                        yield return formula;
                        break;
                }
            }
        }
    }
}
