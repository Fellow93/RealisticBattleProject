using HarmonyLib;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Stops vanilla's village-gold mechanic from operating, so the field can hold the village's real
    /// purse instead.
    ///
    /// Vanilla village gold is not money a village has; it is a prop. Every village is handed a flat
    /// 1000 at world generation regardless of size, and every village's day ends with
    /// <c>if (Gold &gt; 1000) ChangeGold(1000 - Gold)</c> putting it back. It exists to stock the
    /// player's village stall and to decide which "no goods" line the shop shows, and it is deliberately
    /// inert: villages in vanilla neither earn nor spend, since a convoy's takings go straight past them
    /// to the owner clan.
    ///
    /// Under the ledger the village earns and spends for real, and that purse lives in this same field
    /// -- see <see cref="SettlementWealth"/>. So the daily clamp has to go, or the day a village is paid
    /// is the day its money is deleted.
    ///
    /// Nothing else in <c>Village.DailyTick</c> touches gold -- the rest moves hearth and militia -- so
    /// the whole of the day's change to gold IS the clamp, and restoring the opening balance afterwards
    /// removes exactly it, without a transpiler and without repeating vanilla's hearth arithmetic.
    /// </summary>
    [HarmonyPatch(typeof(Village), "DailyTick")]
    internal class VillageGoldStock
    {
        private static void Prefix(Village __instance, ref int __state)
        {
            __state = __instance.Gold;
        }

        // The one place in RBMCampaign that writes a settlement's gold without going through
        // SettlementWealth.Credit/Debit, and deliberately so: this is not a movement of money under any
        // source, it is the UNDOING of vanilla's own write. Putting it through the ledger would post a
        // phantom credit every day for money that never went anywhere.
        private static void Postfix(Village __instance, int __state)
        {
            if (__instance.Gold != __state)
            {
                __instance.ChangeGold(__state - __instance.Gold);
            }
        }
    }
}
