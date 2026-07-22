using HarmonyLib;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Closes the last back doors into a settlement's money, so that every denar entering or leaving one
    /// does so under a name on the ledger.
    ///
    /// <see cref="SettlementWealth"/> was built as a funnel: two credit methods, two debit methods, one
    /// private writer behind each, and a source string required on every call. That holds for everything
    /// RBM does by hand. It did NOT hold for vanilla, which writes
    /// <c>SettlementComponent.ChangeGold</c> directly from its own actions -- and
    /// <c>SettlementComponent.Gold</c> is not a mirror of citizen wealth, it IS citizen wealth. So a
    /// caravan selling into a town, a lord buying grain, or the player trading at a village all moved the
    /// pot without the ledger ever seeing it.
    ///
    /// Measured at Danustica over eleven days, the ledger accounted for +120,999 denars while the balance
    /// actually moved +87,749 -- a gap of 33,250, some 2,500 a day, negative on every single day without
    /// exception. That was caravans selling more into the town than it sold out, and it was quietly
    /// cancelling about half the town's surplus. A hidden drain is no better than a hidden faucet: it
    /// makes the surplus unreadable, and the whole point of the two-purse ledger is that a town's money
    /// can be accounted for.
    ///
    /// Rather than wrapping each native action one at a time -- <c>SellItemsAction</c>,
    /// <c>GiveGoldAction</c>, and whatever a future patch adds -- this catches the write itself. Every
    /// path into a settlement's gold, known or not, now lands in the funnel.
    /// </summary>
    public static class SettlementGoldFunnel
    {
        // Depth of a region in which vanilla's gold writes are to be DISCARDED rather than routed --
        // raised only where a native write is a mechanic being switched off rather than money moving.
        // See VillageGoldStock, its sole user.
        private static int _suppressDepth;

        /// <summary>
        /// Swallows vanilla's writes to settlement gold for the duration of a call, so a native mechanic
        /// can be switched off at the source instead of being undone after the fact.
        /// </summary>
        /// <remarks>
        /// Paired with <see cref="EndSuppress"/> from a Harmony FINALIZER rather than a postfix, so the
        /// depth cannot leak if the patched method throws -- a leaked suppression would silently swallow
        /// every settlement's gold from then on, which is about the worst failure this system could have.
        /// </remarks>
        internal static void BeginSuppress()
        {
            _suppressDepth++;
        }

        internal static void EndSuppress()
        {
            if (_suppressDepth > 0)
            {
                _suppressDepth--;
            }
        }

        // Depth of a region in which gold arriving at a settlement is world SEEDING rather than trade --
        // it still lands, it simply is not a transaction and pays no market fee.
        private static int _seedDepth;

        /// <summary>
        /// Marks a town's opening market money as what it is, so it is not booked and taxed as a trade.
        /// </summary>
        /// <remarks>
        /// <c>Town.OnInit</c> deals every town 20,000 denars at world generation through the same
        /// <c>ChangeGold</c> everything else uses. Once that write began being routed, the seeding
        /// silently became a taxable transaction: a phantom trade line on every town and 200 denars of
        /// market fee on money nobody had handed over. The gold is real and belongs in the market -- only
        /// its description was wrong.
        /// </remarks>
        [HarmonyPatch(typeof(Town), "OnInit")]
        private static class SeedTownGoldPatch
        {
            private static void Prefix()
            {
                _seedDepth++;
            }

            private static void Finalizer()
            {
                if (_seedDepth > 0)
                {
                    _seedDepth--;
                }
            }
        }

        /// <summary>
        /// Intercepts vanilla's direct write to a settlement's gold and re-issues it through the funnel.
        /// </summary>
        /// <remarks>
        /// The funnel's own writes must pass through untouched or this would recurse forever, which is
        /// what <see cref="SettlementWealth.IsInsideFunnel"/> is for -- the funnel raises a depth counter
        /// around its call to the backing store, and a raised counter means "this write is already
        /// accounted for".
        ///
        /// Falling through to vanilla when routing declines is not a formality. A hideout has no purse
        /// this system models, and <c>RouteNativeWrite</c> refuses it; skipping the original in that case
        /// would destroy the money outright rather than merely leaving it off the ledger.
        ///
        /// One behavioural difference is deliberate and worth naming: vanilla's <c>ChangeGold</c> clamps
        /// a negative result to zero silently, and so does the funnel -- but the funnel clamps to what
        /// the purse actually held and records the clamped figure, so an overdraft now shows up as a
        /// short payment on the ledger instead of vanishing. The resulting balance is identical either
        /// way; only the record improves.
        /// </remarks>
        [HarmonyPatch(typeof(SettlementComponent), "ChangeGold")]
        private static class ChangeGoldPatch
        {
            private static bool Prefix(SettlementComponent __instance, int changeAmount)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled
                    || changeAmount == 0
                    || SettlementWealth.IsInsideFunnel)
                {
                    return true;
                }

                if (_suppressDepth > 0)
                {
                    // A vanilla mechanic being held off, not a payment: drop the write entirely rather
                    // than post it under a source it does not have.
                    return false;
                }

                Settlement settlement = (__instance.Owner != null) ? __instance.Owner.Settlement : null;
                if (settlement == null)
                {
                    return true;
                }

                // The stall commission needs its own handling -- part of it is meant for the owner and
                // vanilla deletes the rest -- so it is offered here first. Routed through this one
                // prefix rather than a second patch on the same method, so the order is explicit.
                if (NativeTradeConservation.TryTakeCommission(settlement, changeAmount))
                {
                    return false;
                }

                // True means the funnel took it and has already written the backing store; false means
                // it declined, and vanilla must be allowed to do what it was going to do.
                return !SettlementWealth.RouteNativeWrite(settlement, changeAmount, _seedDepth > 0);
            }
        }
    }
}
