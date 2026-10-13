using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;

namespace RBMCampaign
{
    /// <summary>
    /// HOW MANY LORDS AN AI ARMY LEADER CALLS: a larger kingdom quota.
    ///
    /// Vanilla's <see cref="DefaultArmyManagementCalculationModel.CanLordCreateArmy"/> member pass takes every lord
    /// party of the kingdom that is free, not the ruler's, has more than 15 days of food, is over 60% of its size
    /// limit and is closer than <c>MaximumDistanceToCallToArmy</c> (8x the average distance between neighbouring
    /// towns), ranks them by strength per influence cost and keeps at most
    /// <code>
    ///     ceil(kingdom war parties * 0.7 - parties already in the kingdom's armies)
    /// </code>
    /// Influence never limits the number: the leader only needs more than 100 to start, and each member's cost is
    /// charged on joining (<c>Army.OnAddPartyInternal</c>) with no affordability check.
    ///
    /// Under RBM's siege gate (<see cref="SiegeDecisionGate"/>, about 2.5x the defenders) the armies those rules
    /// raise -- 3 to 6 members, 700 to 1300 strength in the 2026-10-13 test -- were too small to score any siege
    /// target, so leaders that could raise an army mostly did not. This patch lets armies fill a larger share of the
    /// kingdom: the quota becomes <see cref="KingdomQuota"/>. The call range, the 60% size ratio, the food rule, the
    /// member ranking and the influence costs stay vanilla's (user decision 2026-10-13: only mercenary companies get
    /// a longer reach, see <c>MercenaryContracts.HireRangeMultiplier</c>).
    ///
    /// A transpiler like <see cref="ArmyStrengthFloorPatch"/>: the one <c>ldc.r4 0.7</c> becomes a call returning
    /// the quota, replaced only if found exactly once; otherwise it stays vanilla and a PATCH line goes to the party
    /// budget log. Gated on <c>rbmCampaignEnabled</c> at run time. <c>Diagnostics/ArmyCheckLog</c> reads
    /// <see cref="CurrentQuota"/>.
    /// </summary>
    [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), nameof(DefaultArmyManagementCalculationModel.CanLordCreateArmy))]
    internal static class ArmyCallReachPatch
    {
        /// <summary>Share of the kingdom's war parties its armies may hold together (vanilla 0.7).</summary>
        internal const float KingdomQuota = 0.8f;

        private const float VanillaQuota = 0.7f;

        /// <summary>True once the quota literal was found and replaced.</summary>
        internal static bool QuotaApplied { get; private set; }

        /// <summary>The quota <c>CanLordCreateArmy</c> is applying right now.</summary>
        internal static float CurrentQuota()
        {
            return QuotaApplied && RBMConfig.RBMConfig.rbmCampaignEnabled ? KingdomQuota : VanillaQuota;
        }

        // Called in place of the 0.7 literal.
        private static float Quota()
        {
            return RBMConfig.RBMConfig.rbmCampaignEnabled ? KingdomQuota : VanillaQuota;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            int quotaIndex = -1;
            int quotaMatches = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float f && f == VanillaQuota)
                {
                    quotaMatches++;
                    quotaIndex = i;
                }
            }

            // Rewrite in place so any label on the instruction stays on it; the literal pushes one float, so does the call.
            QuotaApplied = quotaMatches == 1;
            if (QuotaApplied)
            {
                codes[quotaIndex].opcode = OpCodes.Call;
                codes[quotaIndex].operand = AccessTools.Method(typeof(ArmyCallReachPatch), nameof(Quota));
            }
            else
            {
                PartyBudgetLog.Log("PATCH", "", "CanLordCreateArmy: expected one 0.7 literal, found " + quotaMatches
                    + "; army quota left at vanilla's 70% of the kingdom's war parties");
            }

            return codes;
        }
    }
}
