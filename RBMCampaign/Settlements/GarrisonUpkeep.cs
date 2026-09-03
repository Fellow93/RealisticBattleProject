using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
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
        /// Share of a field troop's kit-value maintenance a garrison soldier costs to keep -- the second
        /// leg of a garrison's upkeep, added so a garrison is billed the way a marching company is: a wage
        /// (above) plus maintenance. A quarter, because a garrison stands its post rather than marching a
        /// campaign, so its gear wears slowly. Priced off kit value like any troop's (see
        /// <see cref="SpoilsPool.GetDailyMaintenanceCost"/>) and paid, like the wage, out of the fief's own
        /// treasury -- the coin landing in the town that mends the gear.
        /// </summary>
        public const float GarrisonMaintFactor = 0.25f;

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

        /// <summary>The garrison's daily wage bill -- what its men are paid, read off its own party like the militia's.</summary>
        public static int WageBill(Settlement settlement)
        {
            MobileParty garrison = (settlement != null && settlement.Town != null) ? settlement.Town.GarrisonParty : null;
            return (garrison != null && garrison.IsActive) ? garrison.TotalWage : 0;
        }

        /// <summary>
        /// The garrison's daily maintenance bill -- the kit-value maintenance a field troop pays scaled to
        /// <see cref="GarrisonMaintFactor"/>, summed over the roster. The pure-compute half of
        /// <see cref="ChargeMaintenance"/>, shared with the reserve gates that size garrison recruiting.
        /// </summary>
        public static int MaintenanceBill(Settlement settlement)
        {
            MobileParty garrison = (settlement != null && settlement.Town != null) ? settlement.Town.GarrisonParty : null;
            if (garrison == null || !garrison.IsActive || garrison.MemberRoster == null)
            {
                return 0;
            }
            TroopRoster roster = garrison.MemberRoster;
            int bill = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                bill += (int)(SpoilsPool.GetDailyMaintenanceCost(element.Character, element.Number) * GarrisonMaintFactor);
            }
            // Fortifications: a proper armoury, covered walkways and a smithy inside the walls mean less of the
            // garrison's kit is rusting in the open. −0/5/10% off the day's mending at levels 1/2/3.
            return (int)(bill * BuildingEffects.MaintenanceFactor(settlement.Town));
        }

        /// <summary>The garrison's full daily cost -- wage plus maintenance -- for the reserve gates that size recruiting.</summary>
        public static int EstimateDailyBill(Settlement settlement)
        {
            return WageBill(settlement) + MaintenanceBill(settlement);
        }

        /// <summary>
        /// Charges a settlement its garrison's daily maintenance -- the second leg of a garrison's upkeep,
        /// the kit-value maintenance a field troop pays scaled to <see cref="GarrisonMaintFactor"/> for a
        /// force that stands its post. Called from the daily settlement pass.
        /// </summary>
        /// <remarks>
        /// Priced off the same kit-value formula a marching troop's maintenance is (<see cref="SpoilsPool.GetDailyMaintenanceCost"/>),
        /// summed over the garrison roster and drawn from the fief's treasury -- the pot its wage comes from
        /// -- with the coin paid over to the town that does the mending (a town itself, else the nearest
        /// friendly one). No owner backstop and no purse: a garrison keeps none, so a treasury too empty to
        /// pay simply leaves that day's mending undone, and only what the treasury could give reaches the
        /// market. Money conserved throughout -- the fief pays exactly what the market receives.
        /// </remarks>
        public static void ChargeMaintenance(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null || settlement.Town == null)
            {
                return;
            }
            MobileParty garrison = settlement.Town.GarrisonParty;
            if (garrison == null || !garrison.IsActive || garrison.MemberRoster == null)
            {
                return;
            }

            int bill = MaintenanceBill(settlement);
            if (bill <= 0)
            {
                return;
            }

            int paid = SettlementWealth.Debit(settlement, bill, SettlementWealth.Source.Maintenance);
            if (paid <= 0)
            {
                return;
            }

            Settlement market = settlement.IsTown ? settlement
                : (UpgradeSupply.FindNearestFriendlyTown(garrison)?.Settlement);
            if (market != null)
            {
                TroopMarketFeedback.RegisterPurchase(market, null, paid, SettlementWealth.Source.Maintenance);
            }

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("GARRISON", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "maintenance " + bill + "d  ·  fief paid " + paid + "d"
                    + (market != null ? " to " + market.Name : " — no town in reach")
                    + "  ·  treasury now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }
        }
    }
}
