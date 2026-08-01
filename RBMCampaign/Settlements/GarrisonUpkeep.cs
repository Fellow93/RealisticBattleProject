using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Makes a fief pay its own garrison's wages out of its treasury, instead of the owner carrying the
    /// whole bill from anywhere in the world.
    ///
    /// A garrison is the settlement's, not the field army's: it never marches, it exists to hold this
    /// one place, and the place it holds is the thing that benefits. Vanilla charges every denar of it
    /// to the owner clan, which is why a fief is pure profit to its holder and why garrison payroll is
    /// one of the economy's dead ends -- clan gold deducted and credited to nobody.
    ///
    /// Under the ledger the wage becomes a real local charge: the fief pays its garrison first, out of
    /// its own wealth, and only what the treasury cannot cover falls back to the owner. A well-run fief
    /// pays for its own defence; a poor one leans on its lord, who keeps a frontier castle garrisoned
    /// at a loss because he wants it held. Either way the money now lands somewhere -- the men, and the
    /// market they stand in -- instead of evaporating off the owner's books.
    /// </summary>
    public static class GarrisonUpkeep
    {
        /// <summary>
        /// Share of a garrison's wage bill the fief pays before the owner. One: a garrison is the
        /// settlement's own charge, and the owner is only the backstop for what its treasury cannot
        /// cover. Left as a knob rather than inlined so the split can be softened later without hunting
        /// the arithmetic down.
        /// </summary>
        public const float GarrisonFiefWageShare = 1.0f;

        /// <summary>
        /// Moves the fief's share of a garrison's wage off the owner's books and onto the treasury's.
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
        /// Fief-first with an owner backstop: the treasury pays as much of its share as it holds, and
        /// whatever is left over stays on the owner's books. An empty treasury shifts the whole burden
        /// back to the lord rather than leaving the garrison unpaid, which is also what makes this safe
        /// before the treasury has any income -- with a balance of zero it simply does nothing.
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

                int share = (int)(__result * GarrisonFiefWageShare);
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
