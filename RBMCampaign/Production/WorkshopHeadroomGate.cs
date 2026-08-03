using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Stops a town's workshops from producing an output the town already holds its full storage ceiling
    /// of.
    /// </summary>
    /// <remarks>
    /// Vanilla runs a workshop cycle and dumps its output straight onto the town roster, past the
    /// <see cref="TownStorage"/> ceiling -- which only ever gated goods arriving from OUTSIDE (a villager
    /// or caravan finds the shelf full and carries on). A low-demand output whose input is cheap -- pottery
    /// above all -- therefore piles to many times its cap, because nothing on the production side reads the
    /// glut: the produce-or-not decision values the output at the flat wholesale price, which does not fall
    /// as the shelf fills, so its margin gate never trips. This is the quantity stop that closes that hole.
    ///
    /// It is a <c>Prefix</c> returning false, placed on the per-cycle production tick. Returning false there
    /// skips the whole method BEFORE any input is consumed (vanilla consumes inputs and adds outputs in the
    /// same method, after an internal gate this runs ahead of), so a gated cycle wastes nothing -- the clay
    /// stays on the shelf. Because it refuses the cycle before vanilla's own input and economic gates are
    /// reached, none of the SHOPBLOCK / SHOPIDLE counters see it; it is recorded on its own SHOPCAP line
    /// instead. Priority.First makes it run ahead of <see cref="WorkshopDiagnostics.RecipeIdlePatch"/> for a
    /// deterministic order, though the outcome is the same either way (a skipped cycle leaves that patch's
    /// context untouched, so it records neither a made cycle nor an input failure).
    ///
    /// Both the notable (AI and the hidden artisans) and the player tick are patched, so a glutted good is
    /// not topped up whoever owns the shop. The glut is read against the TOWN's store in both cases -- the
    /// market the good ultimately lands in -- not the player's warehouse.
    ///
    /// A multi-output recipe is skipped only when EVERY output is full, so a wanted co-product is never
    /// starved for a glutted one (cow -> meat + hides still runs while hides has room, accepting a little
    /// meat overflow). The real glut cases -- pottery, beer, wine, oil -- are single-output, so they are
    /// stopped cleanly.
    /// </remarks>
    public static class WorkshopHeadroomGate
    {
        /// <summary>
        /// True when every one of a recipe's outputs is already at or over the town's storage ceiling.
        /// </summary>
        private static bool AllOutputsGlutted(WorkshopType.Production production, Town town)
        {
            if (town == null || production.Outputs == null || production.Outputs.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < production.Outputs.Count; i++)
            {
                ItemCategory category = production.Outputs[i].Item1;
                if (!TownStorage.OutputHasNoRoom(town, category))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The recipe's first output, for naming a skipped cycle on the SHOPCAP line.</summary>
        private static string PrimaryOutput(WorkshopType.Production production)
        {
            return (production.Outputs.Count > 0 && production.Outputs[0].Item1 != null)
                ? production.Outputs[0].Item1.StringId
                : "?";
        }

        /// <summary>
        /// The shared decision: returns false (skip the original cycle) when the recipe is fully glutted,
        /// true (run vanilla) otherwise.
        /// </summary>
        private static bool GateCycle(WorkshopType.Production production, Workshop workshop, ref bool __result)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !RBMConfig.RBMConfig.workshopHeadroomGateEnabled
                || workshop == null || workshop.Settlement == null)
            {
                return true;
            }

            Town town = workshop.Settlement.Town;
            if (town == null || !AllOutputsGlutted(production, town))
            {
                return true;
            }

            WorkshopDiagnostics.CountCapped(workshop.Settlement, PrimaryOutput(production));
            __result = false;
            return false;
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "TickOneProductionCycleForNotableWorkshop")]
        private static class NotableGate
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(WorkshopType.Production production, Workshop workshop, ref bool __result)
            {
                return GateCycle(production, workshop, ref __result);
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "TickOneProductionCycleForPlayerWorkshop")]
        private static class PlayerGate
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(WorkshopType.Production production, Workshop workshop, ref bool __result)
            {
                return GateCycle(production, workshop, ref __result);
            }
        }
    }
}
