using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;

namespace RBMCampaign
{
    /// <summary>
    /// HOW MUCH STRONGER AN AI LORD MUST BE BEFORE HE STARTS A SIEGE.
    ///
    /// Vanilla's <see cref="DefaultTargetScoreCalculatingModel.GetTargetScoreForFaction"/> gates a fresh siege on a
    /// simple strength test. It builds an effective defensive strength (num15 = garrison+militia+parties, scaled by
    /// wall level, plus an estimate of any relief force and the defender kingdom's reserves) and refuses to score the
    /// target — returns 0, so the AI won't commit — unless the attacker clears a multiple of it:
    /// <code>
    ///     float num16 = (missionType == Besieger) ? 2f : 0.75f;   // the fresh-siege factor
    ///     if (mobileParty.SiegeEvent != null &amp;&amp; besieging THIS settlement) num16 = 1.5f;   // already sat down
    ///     if (ourStrength &lt; num15 * num16) return 0f;
    /// </code>
    /// So vanilla asks for ~2x the effective defensive strength to START a siege (only 1.5x to CONTINUE one already
    /// under way — that case is left alone here, we only harden the decision to begin).
    ///
    /// This transpiler multiplies that fresh-siege 2f by <see cref="SiegeStrengthGateMultiplier"/>, raising the bar to
    /// 3x. An AI lord now demands ~50% more relative strength before he'll lay siege. Note RBM's StrategicTroopPower
    /// already reprices BOTH sides from equipment, so ourStrength / num15 is a like-for-like ratio and this gate moves
    /// it honestly — a well-armed garrison genuinely reads as harder to take.
    ///
    /// Implemented as an IL edit of one literal rather than a pre/postfix because the gate depends on num15, a local
    /// the model computes from a dozen inputs and never exposes; reconstructing it outside the method would be a second
    /// copy of vanilla's math to keep in sync. The 2f we target is disambiguated from the method's other 2f literals
    /// by its ternary partner, the 0.75f raider/defender factor, which is unique to this expression.
    /// </summary>
    [HarmonyPatch(typeof(DefaultTargetScoreCalculatingModel), nameof(DefaultTargetScoreCalculatingModel.GetTargetScoreForFaction))]
    internal static class SiegeDecisionGate
    {
        /// <summary>Vanilla fresh-siege factor is 2f; this multiplies it. 1.5 => the AI needs 3x defensive strength.</summary>
        private const float SiegeStrengthGateMultiplier = 1.5f;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            // Locate the unique 0.75f (the besieger ternary's false branch), then its 2f partner a few IL
            // instructions away in either order, and scale only that 2f.
            int gateIndex = -1;
            for (int i = 0; i < codes.Count && gateIndex < 0; i++)
            {
                if (codes[i].opcode != OpCodes.Ldc_R4 || !(codes[i].operand is float f) || f != 0.75f)
                {
                    continue;
                }
                for (int j = i - 3; j <= i + 3; j++)
                {
                    if (j < 0 || j >= codes.Count || j == i)
                    {
                        continue;
                    }
                    if (codes[j].opcode == OpCodes.Ldc_R4 && codes[j].operand is float g && g == 2f)
                    {
                        gateIndex = j;
                        break;
                    }
                }
            }

            if (gateIndex >= 0)
            {
                codes[gateIndex].operand = 2f * SiegeStrengthGateMultiplier;
            }

            return codes;
        }
    }
}
