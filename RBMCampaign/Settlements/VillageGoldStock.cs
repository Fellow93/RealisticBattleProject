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
    /// </summary>
    /// <remarks>
    /// This used to let the clamp happen and then undo it, restoring the opening balance in a postfix.
    /// That worked while nothing watched the gold field. It stopped working the moment
    /// <see cref="SettlementGoldFunnel"/> began routing every native write into the ledger: the clamp
    /// and its undo would each have posted as a trade, netting to nothing but printing a phantom pair on
    /// every village every day.
    ///
    /// So the write is suppressed at the source instead, which is what was wanted all along -- vanilla's
    /// clamp simply does not happen, rather than happening and being reversed. Nothing else in
    /// <c>Village.DailyTick</c> touches gold (the rest moves hearth and militia), so suppressing for the
    /// whole call is exact.
    ///
    /// The release is a FINALIZER, not a postfix, so it runs even if the tick throws. A suppression left
    /// raised would swallow every settlement's gold writes for the rest of the session.
    /// </remarks>
    [HarmonyPatch(typeof(Village), "DailyTick")]
    internal class VillageGoldStock
    {
        private static void Prefix()
        {
            SettlementGoldFunnel.BeginSuppress();
        }

        private static void Finalizer()
        {
            SettlementGoldFunnel.EndSuppress();
        }
    }
}
