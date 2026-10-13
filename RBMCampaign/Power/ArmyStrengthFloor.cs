using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;

namespace RBMCampaign
{
    /// <summary>
    /// THE COMBINED STRENGTH AN AI LORD'S ARMY MUST REACH BEFORE HE MAY RAISE IT.
    ///
    /// Vanilla's <see cref="DefaultArmyManagementCalculationModel.CanLordCreateArmy"/> ends with
    /// <code>
    ///     float num5 = leader's GetCustomStrength(Attacker, Siege) + each chosen member's;
    ///     if (num5 &lt; 1000f) { possibleArmyMembers.Clear(); return false; }
    /// </code>
    /// The 1000 was written against vanilla's tier power (~1 a man for a lord's levy). Under RBM's equipment power
    /// (<see cref="StrategicTroopPower"/>, divided back onto vanilla's scale by its measured <c>PowerScale</c>) a lord
    /// party prices at ~0.9-1.2 a man, so a muster of four or five ordinary lords fell just short, and in the first
    /// party-budget test this floor was the commonest reason an otherwise ready leader raised no army (71 of 293
    /// checks). <see cref="ArmyStrengthFloor"/> lowers it to 700, about the same number of men at RBM's scale.
    ///
    /// Only the floor moves here: the leader's 60% size ratio, food, influence, mercenary, sea and war gates and the
    /// member pass stay vanilla's (the member call range and the kingdom quota are widened by
    /// <see cref="ArmyCallReachPatch"/>). PowerScale is deliberately left alone (it is measured,
    /// and renown, influence and barter values read it too).
    ///
    /// A transpiler on the one literal, because the sum is a local the method never exposes. That literal is the only
    /// <c>ldc.r4 1000</c> in this method (the type's other one is in <c>CalculatePartyInfluenceCost</c>); if the pattern
    /// is not found exactly once the method is left unpatched and a PATCH line goes to the party budget log.
    /// Gated on <c>rbmCampaignEnabled</c> at run time (the literal becomes a call), so switching the module off
    /// restores 1000 without re-patching. Nothing else in vanilla or RBM re-checks this floor: AiMilitaryBehavior only
    /// reads <c>CanLordCreateArmy</c>'s result, and <c>Diagnostics/ArmyCheckLog</c> reads <see cref="CurrentFloor"/>.
    /// </summary>
    [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), nameof(DefaultArmyManagementCalculationModel.CanLordCreateArmy))]
    internal static class ArmyStrengthFloorPatch
    {
        /// <summary>RBM's army strength floor (vanilla 1000).</summary>
        internal const float ArmyStrengthFloor = 700f;

        private const float VanillaFloor = 1000f;

        /// <summary>True once the transpiler found and replaced the literal.</summary>
        internal static bool Applied { get; private set; }

        /// <summary>The floor <c>CanLordCreateArmy</c> is applying right now.</summary>
        internal static float CurrentFloor()
        {
            return Applied && RBMConfig.RBMConfig.rbmCampaignEnabled ? ArmyStrengthFloor : VanillaFloor;
        }

        // Called in place of the literal; returns the floor for the comparison.
        private static float Floor()
        {
            return RBMConfig.RBMConfig.rbmCampaignEnabled ? ArmyStrengthFloor : VanillaFloor;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            int index = -1;
            int matches = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float f && f == VanillaFloor)
                {
                    matches++;
                    index = i;
                }
            }

            if (matches != 1)
            {
                Applied = false;
                PartyBudgetLog.Log("PATCH", "", "CanLordCreateArmy: expected one 1000 literal, found " + matches
                    + "; army strength floor left at vanilla's 1000");
                return codes;
            }

            // Rewrite in place so any label on the literal stays on it; same stack shape (pushes one float).
            codes[index].opcode = OpCodes.Call;
            codes[index].operand = AccessTools.Method(typeof(ArmyStrengthFloorPatch), nameof(Floor));
            Applied = true;
            return codes;
        }
    }
}
