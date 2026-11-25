using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RBM
{
    /// <summary>
    /// Rescues saves holding an entry of 32,768..65,535 bytes. The save system writes each entry's length as
    /// a signed 16-bit number, so such an entry is written whole but its length reads back negative and the
    /// load throws. The same two bytes read as unsigned are the true length, so the loader's
    /// <c>ReadBytes(length)</c> gets its argument masked to 16 bits; lengths that fit are unchanged.
    /// RBM's ledgers wrote such entries up to v4.8.2 (now cut up by <c>SaveStringChunks</c>).
    ///
    /// Patched at module load under its own Harmony id: a save is read before OnGameStart, and
    /// <c>com.rbmmain</c> is unpatched and re-patched on every game start.
    /// </summary>
    public static class SaveEntryLengthRepair
    {
        private static bool _applied;

        public static void ApplyEarly()
        {
            if (_applied)
            {
                return;
            }
            _applied = true;
            try
            {
                MethodInfo loadFrom = AccessTools.Method(AccessTools.TypeByName("TaleWorlds.SaveSystem.ArchiveDeserializer"), "LoadFrom");
                new Harmony("com.rbmsaverepair").Patch(loadFrom,
                    transpiler: new HarmonyMethod(typeof(SaveEntryLengthRepair), nameof(Transpiler)));
            }
            catch (Exception e)
            {
                // Without the patch saves load exactly as in the unmodded game, so this must never block startup.
                TaleWorlds.Library.Debug.Print("[RBM] SaveEntryLengthRepair not applied: " + e);
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo readBytes = AccessTools.Method(typeof(TaleWorlds.Library.BinaryReader), "ReadBytes", new[] { typeof(int) });
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(readBytes))
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4, 0xFFFF);
                    yield return new CodeInstruction(OpCodes.And);
                }
                yield return instruction;
            }
        }
    }
}
