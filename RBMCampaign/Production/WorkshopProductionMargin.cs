using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace RBMCampaign
{
    /// <summary>
    /// Makes named workshops far more willing to run a production cycle.
    ///
    /// Vanilla gates every cycle on a profitability floor:
    ///   floor = IsHidden ? inputCost : inputCost + 200 / ConversionSpeed
    ///   produce only if outputIncome &gt; floor
    /// The <c>200 / ConversionSpeed</c> term scales INVERSELY with a recipe's speed, so the slowest
    /// recipes -- beer, wine, oil, pottery, velvet, the very goods the DEMAND line reports unmet --
    /// demand the largest per-cycle returns (2,000-8,000 denars) and essentially never run. Measured
    /// throughput was about five cycles a town a day against a theoretical twenty-six; roughly four in
    /// five cycles refused. See <see cref="WorkshopDiagnostics"/>'s SHOPBLOCK "margin" line.
    ///
    /// This lowers that constant from 200 to <see cref="MarginPerSpeed"/>, shrinking the required margin
    /// proportionally: a recipe that needed 4,000 denars of output value now needs 400. The hidden
    /// "artisans" shop already uses the bare <c>inputCost</c> floor and is left untouched; the change is
    /// on the named-shop branch, which is exactly where the punitive term lives.
    ///
    /// Overproduction is not a risk: <see cref="WorkshopHeadroomGate"/> already skips cycles once a town
    /// is at its storage ceiling for the good being made, so a more willing shop still stops topping up a
    /// full shelf.
    /// </summary>
    /// <remarks>
    /// Done as a single-constant transpiler rather than a decision rewrite so it composes with the
    /// prefixes already on these methods (<see cref="WorkshopPurse"/> sets <c>effectCapital</c> for hidden
    /// shops on the notable path) and leaves every other gate -- town gold, shop capital, warehouse limit
    /// -- exactly as vanilla wrote it. The <c>200f</c> literal is the only <c>ldc.r4 200</c> in either
    /// method, so a change to it is caught by <c>tools/bannerlord-types.lock.txt</c>. Same technique as
    /// the siege-decision strength gate.
    ///
    /// Patched only when <c>rbmCampaignEnabled</c>: RBMCampaign's <c>PatchAll</c> runs solely under that
    /// toggle, so the transpiler is never applied with the module off.
    /// </remarks>
    public static class WorkshopProductionMargin
    {
        // Was 200. The per-cycle margin a named workshop must clear ON TOP of its input cost, before the
        // division by the recipe's conversion speed. Lower = more willing. Set to 0f to let a named shop
        // produce on any profit at all, exactly like the artisans.
        private const float MarginPerSpeed = 20f;

        private const float VanillaMarginPerSpeed = 200f;

        // Rewrites the one margin constant in place -- mutating the existing instruction keeps any labels
        // and exception blocks attached to it -- and passes every other instruction through unchanged.
        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4
                    && instruction.operand is float
                    && (float)instruction.operand == VanillaMarginPerSpeed)
                {
                    instruction.operand = MarginPerSpeed;
                }
                yield return instruction;
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanNotableWorkshopProduceThisCycle")]
        private static class NotableMarginPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return Rewrite(instructions);
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanPlayerWorkshopProduceThisCycle")]
        private static class PlayerMarginPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return Rewrite(instructions);
            }
        }
    }
}
