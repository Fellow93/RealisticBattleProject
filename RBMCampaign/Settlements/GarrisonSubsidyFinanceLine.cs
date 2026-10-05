using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Charges an owner the garrison maintenance he has taken on, through the clan finance model, and shows
    /// the player what it will cost him.
    ///
    /// A garrison's WAGE residual already reaches the clan finance screen on vanilla's own
    /// "{SETTLEMENT} Garrison" line, because it flows through <c>CalculatePartyWage</c>. Its kit
    /// MAINTENANCE does not: <see cref="GarrisonUpkeep.ChargeMaintenance"/> runs on each fief's daily tick,
    /// and what the treasury cannot cover <see cref="GarrisonSubsidy"/> books to the owning clan. This hands
    /// that booking over on the clan's apply pass -- the once-a-day call whose result becomes the leader's
    /// gold -- as a negative line, so the projection, the payment and the "Daily Gold Change" message all
    /// carry the same bill, exactly as <see cref="SettlementIncomeFinanceLine"/> pays the wealth tax.
    ///
    /// Garrison PROMOTIONS paid by the owner are deliberately not here: they are one-off purchases, gated on
    /// the gold in hand, and are taken from his purse when they happen. Vanilla projects no one-off spending
    /// either.
    /// </summary>
    /// <remarks>
    /// The apply leg runs for EVERY clan -- every clan's fiefs can book a subsidy -- and drains the pool once
    /// so the charge happens once. The display leg is player-only and moves no coin: it shows what the next
    /// apply pass will charge (<see cref="GarrisonSubsidy.ProjectNextOwnerMaintenance"/>), the bookings
    /// already made plus the projected share of every fief that has not ticked since the last apply.
    /// </remarks>
    public static class GarrisonSubsidyFinanceLine
    {
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange")]
        private class ChargeGarrisonSubsidies
        {
            private static void Postfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
            {
                if (clan == null || !RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }

                if (applyWithdrawals)
                {
                    // The authoritative once-a-day pass: everything the clan's fiefs booked since it was
                    // last charged, as an expense the finance model takes from the leader's gold.
                    int owed = GarrisonSubsidy.ConsumePendingMaintenance(clan);
                    if (owed > 0)
                    {
                        __result.Add(-owed, Label());
                    }
                    return;
                }

                if (clan != Clan.PlayerClan)
                {
                    return;
                }
                int projected = GarrisonSubsidy.ProjectNextOwnerMaintenance(clan);
                if (projected > 0)
                {
                    __result.Add(-projected, Label());
                }
            }
        }

        /// <summary>The breakdown label, shared with the Finances tab's expense total (<see cref="ClanFinanceTabLines"/>).</summary>
        internal static TextObject Label()
        {
            return new TextObject("{=rbm_garr_maint_subsidy_line}Garrison maintenance subsidies");
        }
    }
}
