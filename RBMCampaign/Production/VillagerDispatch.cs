using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Lets a village dispatch its villager trade party once stored goods reach a FRACTION of
    /// warehouse capacity, instead of requiring a full warehouse. Vanilla
    /// <c>VillagerCampaignBehavior.ThinkAboutSendingItemToTown</c> gates on
    /// <c>stored &lt; GetWarehouseCapacity()</c>; this transpiler scales the capacity value used by
    /// that single gate down to <see cref="DispatchThresholdFraction"/>, so caravans set out earlier
    /// and goods reach market sooner.
    ///
    /// Surgical by design: the <c>warehouseCapacity</c> local is used nowhere else in the method, and
    /// the same 15%/hour outer chance and the party create/top-up/send logic are unchanged. The
    /// production-halt gate (which reads GetWarehouseCapacity at full value elsewhere) is untouched.
    /// If the IL anchor ever stops matching, the transpiler no-ops and the vanilla full-warehouse
    /// threshold simply remains. Applied only under rbmCampaignEnabled (PatchAll runs only then).
    /// </summary>
    [HarmonyPatch(typeof(VillagerCampaignBehavior), "ThinkAboutSendingItemToTown")]
    internal static class VillagerDispatchThresholdPatch
    {
        private const float DispatchThresholdFraction = 0.5f;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getWarehouseCapacity = AccessTools.Method(typeof(Village), "GetWarehouseCapacity");
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (getWarehouseCapacity != null && instruction.Calls(getWarehouseCapacity))
                {
                    // Rewrite the returned int capacity to (int)(capacity * fraction) before it is
                    // stored into the local the dispatch gate compares against.
                    yield return new CodeInstruction(OpCodes.Conv_R4);
                    yield return new CodeInstruction(OpCodes.Ldc_R4, DispatchThresholdFraction);
                    yield return new CodeInstruction(OpCodes.Mul);
                    yield return new CodeInstruction(OpCodes.Conv_I4);
                }
            }
        }
    }
}
