using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

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
        internal const float DispatchThresholdFraction = 0.5f;

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

    /// <summary>
    /// Raises the share of a village warehouse a single convoy loads when it sets out. Vanilla
    /// <c>VillagerCampaignBehavior.MoveItemsToVillagerParty</c> walks the store four times taking
    /// <c>0.2</c> of each remaining stack per pass -- <c>1 - 0.8^4 ≈ 59%</c> of the warehouse per trip.
    /// With one convoy per village instead of two (see <see cref="VillagerConvoys.MaxConvoysPerVillage"/>)
    /// that leaves too much behind: the store backs up while the lone convoy is away and production
    /// halts against the warehouse cap. This transpiler rewrites the single per-pass fraction to
    /// <see cref="PerPassLoadFraction"/>, so a departing convoy nearly empties the store.
    ///
    /// The share is still capped by the party's weight budget in the same method
    /// (<c>InventoryCapacity - TotalWeightCarried</c>, itself doubled for villager parties by
    /// <see cref="VillagerConvoys.VillagerCarryCapacityPatch"/>): the fraction sets how much the convoy
    /// TRIES to take, capacity sets how much it CAN. Compounds over the four passes as
    /// <c>1 - (1-f)^4</c> -- 0.5 ≈ 94%, 0.4 ≈ 87%, 0.34 ≈ 81%.
    ///
    /// Surgical by design: <c>0.2f</c> is the method's only float literal, so the anchor is
    /// unambiguous. If it ever stops matching, the transpiler no-ops and vanilla's 59% remains.
    /// Applied only under rbmCampaignEnabled (PatchAll runs only then).
    /// </summary>
    [HarmonyPatch(typeof(VillagerCampaignBehavior), "MoveItemsToVillagerParty")]
    internal static class VillagerLoadSharePatch
    {
        internal const float PerPassLoadFraction = 0.5f;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value
                    && System.Math.Abs(value - 0.2f) < 1e-6f)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_R4, PerPassLoadFraction);
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }

    /// <summary>
    /// Writes down every villager party that sets out: who is walking, what they are carrying, and what
    /// it is worth. The cargo is the whole point of the party and nothing in game ever shows it, so a
    /// village that never seems to enrich its town can only be diagnosed from here.
    ///
    /// Called from <see cref="VillagerEscort"/>'s postfix rather than from a postfix of its own, so the
    /// escort is already aboard when the party is described -- two postfixes on the same method have no
    /// defined order between them, and a dispatch line missing its guards would be misleading.
    /// </summary>
    internal static class VillagerDispatchLog
    {
        public static void LogDispatch(Village village, MobileParty villagerParty, string escortNote)
        {
            if (!EconomyLog.IsEnabled || village == null || villagerParty == null)
            {
                return;
            }

            string name = village.Settlement != null ? village.Settlement.Name.ToString() : village.StringId;
            Settlement destination = village.TradeBound;

            EconomyLog.Log("DISPATCH", name,
                "to " + (destination != null ? destination.Name.ToString() : "-")
                + "  ·  " + villagerParty.MemberRoster.TotalManCount + " men"
                + "  hearth left " + EconomyLog.Fmt(village.Hearth)
                + "  ·  village store " + RBMVillageProduction.StoredUnits(village)
                + "/" + village.GetWarehouseCapacity() + " after loading");

            EconomyLog.Log("DISPATCH", name, "    party    " + DescribeRoster(villagerParty.MemberRoster));
            EconomyLog.Log("DISPATCH", name, "    cargo    " + DescribeCargo(villagerParty.Party.ItemRoster));
            if (!string.IsNullOrEmpty(escortNote))
            {
                EconomyLog.Log("DISPATCH", name, "    escort   " + escortNote);
            }
        }

        /// <summary>Troop composition, largest stack first, with each stack's tier.</summary>
        private static string DescribeRoster(TroopRoster roster)
        {
            List<KeyValuePair<CharacterObject, int>> stacks = new List<KeyValuePair<CharacterObject, int>>();
            for (int i = 0; i < roster.Count; i++)
            {
                CharacterObject character = roster.GetCharacterAtIndex(i);
                if (character != null)
                {
                    stacks.Add(new KeyValuePair<CharacterObject, int>(character, roster.GetElementNumber(i)));
                }
            }
            stacks.Sort((a, b) => b.Value.CompareTo(a.Value));

            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<CharacterObject, int> stack in stacks)
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(stack.Value).Append("x ")
                  .Append(stack.Key.Name != null ? stack.Key.Name.ToString() : stack.Key.StringId)
                  .Append(" (T").Append(stack.Key.Tier).Append(")");
            }
            return (sb.Length > 0) ? sb.ToString() : "empty";
        }

        /// <summary>Goods aboard, most valuable first, with the load's total worth and weight.</summary>
        private static string DescribeCargo(ItemRoster roster)
        {
            List<KeyValuePair<ItemObject, int>> goods = new List<KeyValuePair<ItemObject, int>>();
            int units = 0;
            float weight = 0f;
            for (int i = 0; i < roster.Count; i++)
            {
                ItemRosterElement element = roster.GetElementCopyAtIndex(i);
                ItemObject item = element.EquipmentElement.Item;
                if (item != null && element.Amount > 0)
                {
                    goods.Add(new KeyValuePair<ItemObject, int>(item, element.Amount));
                    units += element.Amount;
                    weight += item.Weight * element.Amount;
                }
            }
            goods.Sort((a, b) => (b.Key.Value * b.Value).CompareTo(a.Key.Value * a.Value));

            // Summed by hand -- ItemRoster carries TotalValue but no weight equivalent. Worth having
            // rather than dropping from the summary: RBM's goods span four orders of magnitude of
            // mass, so what a convoy can shift is now as much a constraint as what it is worth.
            StringBuilder sb = new StringBuilder();
            sb.Append(units).Append(" units, worth ").Append(roster.TotalValue).Append("d")
              .Append(", weighing ").Append(EconomyLog.Fmt(weight)).Append("kg");
            foreach (KeyValuePair<ItemObject, int> good in goods)
            {
                sb.Append("  ·  ").Append(good.Key.StringId).Append(" ").Append(good.Value)
                  .Append(" @").Append(good.Key.Value).Append("d");
            }
            return sb.ToString();
        }
    }
}
