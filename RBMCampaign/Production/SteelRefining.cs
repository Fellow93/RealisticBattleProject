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
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

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
    /// This postfix rewrites the iron- and steel-tier formulas the game yields so the smithy matches the
    /// workshop ratios (1:1 iron:charcoal, no Iron1 byproduct — vanilla smithing charged 2 ingots per output
    /// on the Iron3/steel tiers and spat one back). The charcoal and iron-ore formulas, and every perk gate
    /// (SteelMaker / SteelMaker2 / SteelMaker3, which decide whether a steel formula is yielded at all), are
    /// left exactly as vanilla produced it — we key off the formula's Output, so if a formula is absent
    /// (perk not unlocked) there is nothing to rewrite.
    ///
    /// Workshop → smithy mapping:
    ///   Iron       (Iron2): 1 Iron1 + 1 Charcoal  -> 1 Iron2      (already vanilla; kept explicit)
    ///   Wrought    (Iron3): 1 Iron2 + 1 Charcoal  -> 1 Iron3      (vanilla was 2 Iron2 -> Iron3 + Iron1 byproduct)
    ///   Steel      (Iron4): 1 Iron2 + 1 Charcoal  -> 1 Iron4      (workshop: ironIngot2 + charcoal -> ironIngot4)
    ///   Fine steel (Iron5): 1 Iron3 + 1 Charcoal  -> 1 Iron5      (workshop: ironIngot3 + charcoal -> ironIngot5)
    ///   Thamaskene (Iron6): 5 Iron1 + 10 Charcoal -> 5 Iron6      (workshop: 5 ironIngot1 + 10 charcoal + 1 silver -> 5 ironIngot6)
    ///
    /// SILVER: the workshop's Thamaskene recipe also consumes silver, but <see cref="Crafting.RefiningFormula"/>
    /// only holds TWO inputs and <see cref="CraftingMaterials"/> has no silver slot, so silver cannot be a
    /// formula ingredient. Iron6 is therefore refined in the workshop's exact 5:10:1 batch (so the silver cost
    /// is a whole number), and the silver is charged out-of-band by the two patches below: one injects a silver
    /// row into the refine screen (which makes the availability check gate on it for free), the other deducts it
    /// in DoRefinement.
    /// </summary>
    [HarmonyPatch(typeof(DefaultSmithingModel), nameof(DefaultSmithingModel.GetRefiningFormulas))]
    internal static class SteelRefining
    {
        /// <summary>Silver ore consumed per Thamaskene batch, matching the workshop (1 silver per 5 ironIngot6).</summary>
        internal const int ThamaskeneSilverCost = 1;

        /// <summary>Trade-good item id for silver ore (SandBoxCore items/horses_and_others.xml).</summary>
        internal const string SilverItemId = "silver";

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
                    case CraftingMaterials.Iron2: // iron (kept 1:1 to match vanilla explicitly)
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron1, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron2);
                        break;
                    case CraftingMaterials.Iron3: // wrought iron (vanilla 2:1 + Iron1 byproduct stripped)
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron2, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron3);
                        break;
                    case CraftingMaterials.Iron4: // steel
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron2, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron4);
                        break;
                    case CraftingMaterials.Iron5: // fine steel
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron3, 1, CraftingMaterials.Charcoal, 1, CraftingMaterials.Iron5);
                        break;
                    case CraftingMaterials.Iron6: // thamaskene steel (workshop batch; silver charged out-of-band below)
                        yield return new Crafting.RefiningFormula(CraftingMaterials.Iron1, 5, CraftingMaterials.Charcoal, 10, CraftingMaterials.Iron6, 5);
                        break;
                    default:
                        yield return formula;
                        break;
                }
            }
        }

        internal static ItemObject GetSilverItem()
        {
            return MBObjectManager.Instance?.GetObject<ItemObject>(SilverItemId);
        }
    }

    /// <summary>
    /// Shows silver as an input on the Thamaskene (Iron6) refine action. <see cref="RefiningFormula"/> can't carry
    /// silver, so we append a <see cref="CraftingResourceItemVM"/> for it after the row is built. Because
    /// <see cref="RefinementActionItemVM"/>'s own availability check loops over InputMaterials and reads each row's
    /// real <see cref="CraftingResourceItemVM.ResourceItem"/>, giving that row the actual silver item makes the
    /// refine button auto-disable when the party is short on silver — no extra gating needed. The row renders from
    /// ResourceItemStringId / ResourceName like any other input, so no prefab change is required.
    /// </summary>
    [HarmonyPatch(typeof(RefinementActionItemVM), MethodType.Constructor, new Type[] { typeof(Crafting.RefiningFormula), typeof(Action<RefinementActionItemVM>) })]
    internal static class ThamaskeneSilverRow
    {
        private static void Postfix(RefinementActionItemVM __instance)
        {
            if (__instance.RefineFormula.Output != CraftingMaterials.Iron6)
            {
                return;
            }
            ItemObject silver = SteelRefining.GetSilverItem();
            if (silver == null)
            {
                return;
            }

            // Build the row off any enum value, then overwrite its display + real item with silver.
            CraftingResourceItemVM row = new CraftingResourceItemVM(CraftingMaterials.IronOre, SteelRefining.ThamaskeneSilverCost);
            AccessTools.Property(typeof(CraftingResourceItemVM), nameof(CraftingResourceItemVM.ResourceItem))?.SetValue(row, silver);
            string name = silver.Name?.ToString() ?? "Silver";
            row.ResourceName = name;
            row.ResourceItemStringId = silver.StringId;
            row.ResourceHint = new HintViewModel(new TextObject("{=!}" + name));

            __instance.InputMaterials.Add(row);
            // Re-run the availability check now that silver is part of the input list (the ctor ran it without silver).
            __instance.RefreshDynamicProperties();
        }
    }

    /// <summary>
    /// Charges the silver when a Thamaskene (Iron6) refine actually happens. The UI row above already prevents the
    /// player from starting the action without enough silver; the Min guard keeps this safe against any code path
    /// that reaches DoRefinement with a short inventory (never goes negative).
    /// </summary>
    [HarmonyPatch(typeof(CraftingCampaignBehavior), nameof(CraftingCampaignBehavior.DoRefinement))]
    internal static class ThamaskeneSilverCharge
    {
        private static void Postfix(Crafting.RefiningFormula refineFormula)
        {
            if (refineFormula.Output != CraftingMaterials.Iron6)
            {
                return;
            }
            ItemObject silver = SteelRefining.GetSilverItem();
            if (silver == null)
            {
                return;
            }
            ItemRoster roster = MobileParty.MainParty?.ItemRoster;
            if (roster == null)
            {
                return;
            }
            int take = Math.Min(roster.GetItemNumber(silver), SteelRefining.ThamaskeneSilverCost);
            if (take > 0)
            {
                roster.AddToCounts(silver, -take);
            }
        }
    }
}
