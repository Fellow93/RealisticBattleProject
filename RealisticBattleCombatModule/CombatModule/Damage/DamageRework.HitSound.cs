using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    internal partial class DamageRework
    {
        // Vanilla picks a hit's Low/Med/High impact sound from InflictedDamage alone (Agent.HandleBlow ->
        // Blow.IsBlowLow/IsBlowCrit). Under RBM armor most blows on mail or plate get through for a few points, so
        // every one played the Low variant: a heavy axe on a steel helmet sounded like a tap. The tier is chosen from
        // the blow's impact instead (what got through + what the armor absorbed, i.e. the damage it would do
        // unarmored); the armor's own clank still comes from the "Armor Type" sound parameter vanilla sets.
        // The calls are swapped in HandleBlow rather than patching the one-line Blow methods, which the JIT may inline.
        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("HandleBlow")]
        private class HitSoundTierPatch
        {
            // Shares of the victim's base hit points (Monster.HitPoints, 100 for a human). Vanilla's Low is the same
            // 0.1; its High needed more than twice a man's hit points and practically never played.
            private const float LowImpactShare = 0.1f;
            private const float HighImpactShare = 0.6f;

            private static readonly MethodInfo IsBlowCritMethod = AccessTools.Method(typeof(Blow), nameof(Blow.IsBlowCrit));
            private static readonly MethodInfo IsBlowLowMethod = AccessTools.Method(typeof(Blow), nameof(Blow.IsBlowLow));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo crit = AccessTools.Method(typeof(HitSoundTierPatch), nameof(IsHighImpact));
                MethodInfo low = AccessTools.Method(typeof(HitSoundTierPatch), nameof(IsLowImpact));
                foreach (CodeInstruction instruction in instructions)
                {
                    // Same stack shape: (ref Blow, int) -> bool.
                    if (instruction.Calls(IsBlowCritMethod))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = crit;
                    }
                    else if (instruction.Calls(IsBlowLowMethod))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = low;
                    }
                    yield return instruction;
                }
            }

            private static float Impact(ref Blow b)
            {
                return b.InflictedDamage + b.AbsorbedByArmor;
            }

            // Vanilla passes Monster.HitPoints * 4 here.
            private static bool IsHighImpact(ref Blow b, int hitPointsTimesFour)
            {
                return Impact(ref b) > hitPointsTimesFour * 0.25f * HighImpactShare;
            }

            private static bool IsLowImpact(ref Blow b, int hitPoints)
            {
                return Impact(ref b) <= hitPoints * LowImpactShare;
            }
        }
    }
}
