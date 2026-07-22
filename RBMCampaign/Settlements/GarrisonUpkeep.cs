using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Makes a fief pay part of its own garrison's wages out of its treasury, instead of the owner
    /// carrying the whole bill from anywhere in the world.
    ///
    /// A garrison is the settlement's, not the field army's: it never marches, it exists to hold this
    /// one place, and the place it holds is the thing that benefits. Vanilla charges every denar of it
    /// to the owner clan, which is why a fief is pure profit to its holder and why garrison payroll is
    /// one of the economy's dead ends -- clan gold deducted and credited to nobody.
    ///
    /// Under the ledger the same wage becomes a real local flow: the treasury pays a quarter, that
    /// quarter reaches the men, and the men spend it in the market they are standing in. The lord still
    /// pays the rest, so a garrison is no cheaper overall -- it is simply no longer free to the town it
    /// defends, and the money now lands somewhere instead of evaporating.
    /// </summary>
    public static class GarrisonUpkeep
    {
        /// <summary>Share of a garrison's wage bill the settlement carries; the owner pays the rest.</summary>
        public const float TownGarrisonWageShare = 0.25f;

        /// <summary>
        /// Moves the settlement's share of a garrison's wage off the owner's books and onto the fief's.
        /// </summary>
        /// <remarks>
        /// Patched HERE, at the wage calculation, rather than at <c>AddPartyExpense</c>, because the
        /// clan's real charge is not the wage: the wage is subtracted from the garrison's purse, and
        /// what the clan is actually billed is the top-up that brings that purse back to its 5000-denar
        /// threshold. The two are equal only in steady state. Reducing the wage lets the whole chain --
        /// purse drain, top-up, the clan finance breakdown and its per-fief "{SETTLEMENT} Garrison"
        /// line -- follow consistently from one number, rather than having to be corrected in three
        /// places that would drift apart.
        ///
        /// <c>ApplyMoraleEffect</c> has already run by the time this returns, against the full wage, so
        /// the men's morale still reflects being paid in full. Only the question of who paid changes.
        ///
        /// A fief that cannot afford its share pays what it has and the owner covers the remainder, so
        /// an empty treasury shifts the burden back to the lord rather than leaving the garrison
        /// unpaid. That also makes this safe before the treasury has any income: with a balance of zero
        /// it simply does nothing.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculatePartyWage")]
        private static class GarrisonWageSharePatch
        {
            private static void Postfix(MobileParty mobileParty, bool applyWithdrawals, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || mobileParty == null
                    || !mobileParty.IsGarrison || __result <= 0)
                {
                    return;
                }

                Settlement settlement = mobileParty.CurrentSettlement ?? mobileParty.HomeSettlement;
                if (settlement == null)
                {
                    return;
                }

                int share = (int)(__result * TownGarrisonWageShare);
                if (share <= 0)
                {
                    return;
                }

                // Capped at what the fief actually holds, and read the same way on both passes so the
                // projected figure on the clan finance screen matches the charge that follows it.
                int available = SettlementWealth.GetSettlementWealth(settlement);
                int paid = share < available ? share : available;
                if (paid <= 0)
                {
                    return;
                }

                if (applyWithdrawals)
                {
                    SettlementWealth.Debit(settlement, paid, SettlementWealth.Source.GarrisonWage);
                    if (EconomyLog.IsEnabled)
                    {
                        EconomyLog.Log("GARRISON", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                            "wage " + __result + "d  ·  fief paid " + paid + "d, owner " + (__result - paid) + "d"
                            + "  ·  treasury now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
                    }
                }

                __result -= paid;
            }
        }
    }
}
