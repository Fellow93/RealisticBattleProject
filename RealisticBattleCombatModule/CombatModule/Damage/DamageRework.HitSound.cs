using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    internal partial class DamageRework
    {
        // Vanilla picks a hit's Low/Med/High sound (Agent.HandleBlow -> Blow.IsBlowLow/IsBlowCrit) from the damage
        // dealt. Low is the glance off armor, Med and High are flesh wounds. Under RBM a blow the armor stops still
        // deals blunt trauma, often more than vanilla's Low limit (10), so a mace or axe stopped by plate sounded
        // like it cut flesh. A blow the armor stopped (no point of penetration damage) now always plays Low, with the
        // clank of the armor it lands on (vanilla's "Armor Type" parameter); one that got through keeps vanilla's
        // choice. The calls are swapped in HandleBlow rather than patching the one-line Blow methods, which the JIT
        // may inline.
        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("HandleBlow")]
        private class HitSoundTierPatch
        {
            // OverrideDamageCalc records each hit's penetration, HandleBlow reads it later in the same hit. Both run
            // on the main thread; a record only counts for the same attacker and victim at the same mission time.
            private static int _recordedAttacker = -1;
            private static int _recordedVictim = -1;
            private static float _recordedTime = -1f;
            private static bool _recordedPenetrated;
            private static bool _armorStoppedBlow;

            internal static void RecordPenetration(Agent attacker, Agent victim, bool penetrated)
            {
                Mission mission = Mission.Current;
                if (mission == null || victim == null)
                {
                    return;
                }
                _recordedAttacker = attacker?.Index ?? -1;
                _recordedVictim = victim.Index;
                _recordedTime = mission.CurrentTime;
                _recordedPenetrated = penetrated;
            }

            private static void Prefix(Agent __instance, ref Blow b)
            {
                Mission mission = Mission.Current;
                _armorStoppedBlow = mission != null && _recordedVictim == __instance.Index && _recordedAttacker == b.OwnerId
                    && _recordedTime == mission.CurrentTime && !_recordedPenetrated;
                _recordedVictim = -1;
            }

            private static readonly MethodInfo IsBlowCritMethod = AccessTools.Method(typeof(Blow), nameof(Blow.IsBlowCrit));
            private static readonly MethodInfo IsBlowLowMethod = AccessTools.Method(typeof(Blow), nameof(Blow.IsBlowLow));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo crit = AccessTools.Method(typeof(HitSoundTierPatch), nameof(IsCritSound));
                MethodInfo low = AccessTools.Method(typeof(HitSoundTierPatch), nameof(IsLowSound));
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

            // GetHitSound checks the crit flag first, so a stopped blow must clear it too.
            private static bool IsCritSound(ref Blow b, int maxHitPoints)
            {
                return !_armorStoppedBlow && b.IsBlowCrit(maxHitPoints);
            }

            private static bool IsLowSound(ref Blow b, int maxHitPoints)
            {
                return _armorStoppedBlow || b.IsBlowLow(maxHitPoints);
            }
        }
    }
}
