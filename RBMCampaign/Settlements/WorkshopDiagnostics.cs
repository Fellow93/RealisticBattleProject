using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Records why a town's workshops are not producing.
    ///
    /// Measured throughput is about five production cycles a town a day against a theoretical
    /// twenty-six -- the sum of every workshop's <c>conversion_speed</c>, which is literally cycles per
    /// day. So roughly four cycles in five are being refused, and the goods that never get made are
    /// precisely the ones the DEMAND line reports unmet: beer, wine, oil, pottery, velvet. Reading the
    /// code narrows it to four possible refusals but cannot say which actually fires, and guessing from
    /// the shape of a formula has been wrong repeatedly here.
    ///
    /// So each refusal is counted, by reason, per settlement per day, and written as SHOPBLOCK.
    /// </summary>
    /// <remarks>
    /// Two of the four are worth stating in advance, because they predict different fixes:
    ///
    /// <list type="bullet">
    /// <item><b>no-input</b> means the material was not on the shelf when the workshop ticked. If this
    /// dominates for grain, the cause is contention -- citizens eat cheapest-first and strip the grain
    /// before the brewery buys any -- and the fix is about ordering or reservation, not about
    /// workshops.</item>
    /// <item><b>margin</b> is vanilla's <c>inputCost + 200 / ConversionSpeed</c> floor. That term scales
    /// INVERSELY with speed, so the slowest recipes demand the largest returns: fourteen of the game's
    /// sixty-three productions need between 2,000 and 8,000 denars of output value per cycle and can
    /// essentially never run. If this dominates, the fix is the floor itself.</item>
    /// </list>
    ///
    /// The recipe is named on the margin and no-input counts, because knowing that pottery is blocked
    /// tells you something and knowing that "a workshop" is blocked does not.
    ///
    /// Diagnostics only -- nothing here changes a decision. It patches the same behaviour class as
    /// <see cref="WorkshopPurse"/>, which is already patched safely; note that
    /// <c>DefaultClanFinanceModel</c> is NOT touched, having proved hostile to early patching.
    /// </remarks>
    public static class WorkshopDiagnostics
    {
        // Per settlement: reason -> count, plus the run/blocked totals under reserved keys.
        private static readonly Dictionary<Settlement, Dictionary<string, int>> _blocks =
            new Dictionary<Settlement, Dictionary<string, int>>();

        private const string Ran = "!ran";

        /// <summary>Drops the previous session's tallies. Diagnostics only, so a session hook is enough.</summary>
        public static void Reset()
        {
            _blocks.Clear();
        }

        private static void Count(Settlement settlement, string reason)
        {
            if (settlement == null)
            {
                return;
            }
            Dictionary<string, int> byReason;
            if (!_blocks.TryGetValue(settlement, out byReason))
            {
                byReason = new Dictionary<string, int>();
                _blocks[settlement] = byReason;
            }
            int running;
            byReason.TryGetValue(reason, out running);
            byReason[reason] = running + 1;
        }

        /// <summary>The first input category a production could not find enough of, for naming a block.</summary>
        private static string MissingInput(WorkshopType.Production production, ItemRoster roster)
        {
            foreach (var input in production.Inputs)
            {
                ItemCategory category = input.Item1;
                int wanted = input.Item2;
                for (int i = 0; i < roster.Count; i++)
                {
                    ItemObject item = roster.GetItemAtIndex(i);
                    if (item != null && item.ItemCategory == category)
                    {
                        wanted -= roster.GetElementNumber(i);
                    }
                }
                if (wanted > 0)
                {
                    return (category != null) ? category.StringId : "?";
                }
            }
            return "?";
        }

        /// <summary>
        /// Catches the material shortage. Returning false here refuses the cycle before any of the
        /// economic gates are consulted, so it is measured first and separately.
        /// </summary>
        /// <remarks>
        /// The town is taken from the method's own argument rather than from the roster, because this is
        /// also called against a WAREHOUSE roster for player-owned shops -- the town is the reliable one.
        /// </remarks>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "DetermineItemRosterHasSufficientInputs")]
        private static class InputBlockPatch
        {
            private static void Postfix(WorkshopType.Production production, ItemRoster itemRoster, Town town, bool __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !EconomyLog.IsEnabled || __result || town == null)
                {
                    return;
                }
                Count(town.Settlement, "no-input:" + MissingInput(production, itemRoster));
            }
        }

        /// <summary>
        /// Catches the economic refusals, and says which of the three fired.
        /// </summary>
        /// <remarks>
        /// The conditions are recomputed rather than observed, because vanilla returns a single bool and
        /// the three are what the answer turns on. They are evaluated in the same order the original
        /// tests them, so the reason recorded is the one that actually stopped it.
        /// </remarks>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanNotableWorkshopProduceThisCycle")]
        private static class EconomicBlockPatch
        {
            private static void Postfix(WorkshopType.Production production, Workshop workshop,
                int inputMaterialCost, int outputIncome, bool effectCapital, bool __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !EconomyLog.IsEnabled || workshop == null)
                {
                    return;
                }
                Settlement settlement = workshop.Settlement;
                if (settlement == null)
                {
                    return;
                }

                if (__result)
                {
                    Count(settlement, Ran);
                    return;
                }

                float floor = workshop.WorkshopType.IsHidden
                    ? inputMaterialCost
                    : (inputMaterialCost + 200f / production.ConversionSpeed);

                if (outputIncome <= floor)
                {
                    // Named by what it would have made, and by the margin it fell short of -- the whole
                    // question about this gate is whether the 200/speed term is the thing biting.
                    string made = (production.Outputs.Count > 0 && production.Outputs[0].Item1 != null)
                        ? production.Outputs[0].Item1.StringId
                        : "?";
                    Count(settlement, "margin:" + made);
                }
                else if (settlement.Town != null && settlement.Town.Gold < outputIncome && effectCapital)
                {
                    Count(settlement, "town-broke");
                }
                else if (workshop.Capital < inputMaterialCost)
                {
                    Count(settlement, "shop-broke");
                }
                else
                {
                    Count(settlement, "unknown");
                }
            }
        }

        /// <summary>
        /// Writes a settlement's day of refusals and clears it.
        /// </summary>
        /// <remarks>
        /// The ratio at the front is the number to read: cycles run against cycles attempted. Everything
        /// after it says where the rest went.
        /// </remarks>
        public static void FlushDaily(Settlement settlement)
        {
            Dictionary<string, int> byReason;
            if (settlement == null || !_blocks.TryGetValue(settlement, out byReason))
            {
                return;
            }
            _blocks.Remove(settlement);

            if (!EconomyLog.IsEnabled || byReason.Count == 0)
            {
                return;
            }

            int ran = 0;
            byReason.TryGetValue(Ran, out ran);

            int blocked = 0;
            List<KeyValuePair<string, int>> reasons = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> pair in byReason)
            {
                if (pair.Key == Ran)
                {
                    continue;
                }
                blocked += pair.Value;
                reasons.Add(pair);
            }

            reasons.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                return b.Value.CompareTo(a.Value);
            });

            StringBuilder breakdown = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in reasons)
            {
                breakdown.Append("  ").Append(pair.Key).Append(" x").Append(pair.Value);
            }

            EconomyLog.Log("SHOPBLOCK", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                "ran " + ran + " of " + (ran + blocked) + " attempted cycles  ·" + breakdown);
        }
    }
}
