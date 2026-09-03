using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace RBMCampaign
{
    /// <summary>
    /// Reprices what a town pays a named workshop for a finished good, and judges a cycle on that.
    ///
    /// Vanilla pays a shop <c>min(1000, price)</c> per output but charges it the full price of every
    /// input, and it gates the cycle on the town holding the output's FULL retail value in cash. At
    /// vanilla prices the cap only bit on armour. At RBM prices it bites on every luxury good: a velvet
    /// weavery buys cotton at 2,000-8,000, is paid 1,000 for the velvet, and can only run at all in a
    /// town holding 26,500 or more. So it never ran, and when it did it lost money.
    ///
    /// Two changes, one constant:
    /// <list type="bullet">
    /// <item>The per-output payout cap rises from 1,000 to <see cref="PayoutCap"/>, the same tenfold
    /// step as RBM's repricing, so a luxury good is worth making again. It stays a cap: an armour worth
    /// 60,000 still does not pull 60,000 out of the market in one day.</item>
    /// <item>The income the production gates are judged on is clamped to the same cap per output, so
    /// the profitability floor sees what the shop will actually be paid, and the town-gold gate asks for
    /// what the town will actually pay. Vanilla judged both on the uncapped value, which is how a
    /// loss-making cycle passed the margin test and a viable one failed the cash test.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The artisans are left alone on both counts: their cycles settle in kind (see
    /// <c>WorkshopPurse.IsCitizenLabour</c>), so no payout exists to cap and no cash gate applies.
    ///
    /// The gate prefix rewrites the <c>outputIncome</c> argument by ref, which every later test in the
    /// method reads, and <see cref="WorkshopDiagnostics"/>'s block postfixes see the clamped value too,
    /// so their recomputed reasons agree with the decision. Patched under <c>rbmCampaignEnabled</c>
    /// only, like everything under RBMCampaign's <c>PatchAll</c>.
    /// </remarks>
    public static class WorkshopPayoutCap
    {
        // Was 1,000. The most a town pays a named shop for one finished item.
        public const int PayoutCap = 10000;

        private const int VanillaPayoutCap = 1000;

        /// <summary>What the town will actually pay for a cycle's outputs, given the cap.</summary>
        public static int CappedIncome(WorkshopType.Production production, int outputIncome)
        {
            if (production.Outputs == null)
            {
                return outputIncome;
            }
            int outputs = 0;
            for (int i = 0; i < production.Outputs.Count; i++)
            {
                outputs += production.Outputs[i].Item2;
            }
            long ceiling = (long)outputs * PayoutCap;
            return (outputIncome > ceiling) ? (int)ceiling : outputIncome;
        }

        private static void ClampIncome(WorkshopType.Production production, Workshop workshop, ref int outputIncome)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled
                || workshop == null || workshop.WorkshopType == null || workshop.WorkshopType.IsHidden)
            {
                return;
            }
            outputIncome = CappedIncome(production, outputIncome);
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanNotableWorkshopProduceThisCycle")]
        private static class NotableGatePatch
        {
            private static void Prefix(WorkshopType.Production production, Workshop workshop, ref int outputIncome)
            {
                ClampIncome(production, workshop, ref outputIncome);
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanPlayerWorkshopProduceThisCycle")]
        private static class PlayerGatePatch
        {
            private static void Prefix(WorkshopType.Production production, Workshop workshop, ref int outputIncome)
            {
                ClampIncome(production, workshop, ref outputIncome);
            }
        }

        /// <summary>
        /// Raises the <c>MathF.Min(1000, itemPrice)</c> payout cap in place. The literal is the only
        /// <c>ldc.i4 1000</c> in the method; the instruction is mutated rather than replaced so any
        /// labels on it survive. Same technique as <see cref="WorkshopProductionMargin"/>.
        /// </summary>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "ProduceAnOutputToTown")]
        private static class PayoutPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.opcode == OpCodes.Ldc_I4
                        && instruction.operand is int
                        && (int)instruction.operand == VanillaPayoutCap)
                    {
                        instruction.operand = PayoutCap;
                    }
                    yield return instruction;
                }
            }
        }
    }
}
