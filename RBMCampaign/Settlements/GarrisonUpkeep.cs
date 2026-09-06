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
            // The budget vanilla was handed before the prefix widened it, and the fief the prefix resolved.
            // The daily finance pass is single-threaded and prefix and postfix bracket one call, so a pair
            // of scratch fields is safe and saves resolving the settlement twice.
            private static int _originalBudget;
            private static Settlement _fief;

            /// <summary>
            /// Widens the budget vanilla measures the garrison against, so the men are not docked morale
            /// for a wage their fief is in fact paying.
            /// </summary>
            /// <remarks>
            /// Vanilla's budget is the OWNER's gold, and <c>ApplyMoraleEffect</c> runs against it before
            /// our postfix ever sees the number -- so a fief whose treasury covers its garrison in full
            /// still took a morale hit whenever its lord happened to be broke. Raising the budget by what
            /// the fief and its citizens will actually put in makes vanilla's own <c>min(wage, budget)</c>
            /// and its morale penalty read the real coverage. The original budget is kept for the postfix,
            /// which still needs to know what the OWNER alone could pay.
            /// </remarks>
            private static void Prefix(MobileParty mobileParty, ref int budget)
            {
                _originalBudget = budget;
                _fief = null;
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || mobileParty == null || !mobileParty.IsGarrison)
                {
                    return;
                }
                Settlement settlement = mobileParty.CurrentSettlement ?? mobileParty.HomeSettlement;
                if (settlement == null)
                {
                    return;
                }
                _fief = settlement;

                long widened = (long)budget
                    + SettlementWealth.GetSettlementWealth(settlement)
                    + GarrisonSubsidy.CitizenCapacity(settlement);
                budget = (widened > int.MaxValue) ? int.MaxValue : (int)widened;
            }

            private static void Postfix(MobileParty mobileParty, bool applyWithdrawals, ref int __result)
            {
                Settlement settlement = _fief;
                _fief = null;
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || mobileParty == null
                    || !mobileParty.IsGarrison || settlement == null)
                {
                    return;
                }

                // Read off the party, not off __result: the prefix widened the budget, but vanilla may
                // still have trimmed the return, and the split below has to be against the whole bill.
                int wage = mobileParty.TotalWage;
                if (wage <= 0)
                {
                    return;
                }

                int share = (int)(wage * GarrisonFiefWageShare);
                // Capped at what the fief actually holds, and read the same way on both passes so the
                // projected figure on the clan finance screen matches the charge that follows it.
                int available = SettlementWealth.GetSettlementWealth(settlement);
                int fiefPaid = (share < available ? share : available);
                if (fiefPaid < 0)
                {
                    fiefPaid = 0;
                }

                int residual = wage - fiefPaid;
                // The owner pays what his own gold could always have paid -- unlimited wage limit or not,
                // this is the bill vanilla has always sent him, and it is the one figure that must stay on
                // his books so the "{SETTLEMENT} Garrison" line keeps meaning what it says.
                int ownerBudget = _originalBudget > 0 ? _originalBudget : 0;
                int ownerPart = residual < ownerBudget ? residual : ownerBudget;
                int citizensPart = 0;

                if (applyWithdrawals)
                {
                    if (fiefPaid > 0)
                    {
                        SettlementWealth.Debit(settlement, fiefPaid, SettlementWealth.Source.GarrisonWage);
                    }
                    // Whatever neither the treasury nor the owner could reach falls to the town's own
                    // burghers, out of their surplus alone -- the last leg of the subsidy order.
                    citizensPart = GarrisonSubsidy.CoverFromCitizens(settlement, residual - ownerPart);

                    if (EconomyLog.IsEnabled)
                    {
                        EconomyLog.Log("GARRISON", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                            "wage " + wage + "d  ·  fief paid " + fiefPaid + "d, owner " + ownerPart + "d"
                            + (citizensPart > 0 ? ", citizens " + citizensPart + "d" : "")
                            + (residual - ownerPart - citizensPart > 0 ? ", unpaid " + (residual - ownerPart - citizensPart) + "d" : "")
                            + "  ·  treasury now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
                    }
                }

                __result = ownerPart;
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
            // Castellan's Office: stables, fodder and a farrier on the payroll are the castellan's own
            // business, and they tell only on the HORSE. −10/20/30% off the mounted men's day of mending
            // at levels 1/2/3; the foot pay the same as ever.
            float mountedFactor = BuildingEffects.CastellanMountedMaintFactor(settlement.Town);
            int bill = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                float cost = SpoilsPool.GetDailyMaintenanceCost(element.Character, element.Number) * GarrisonMaintFactor;
                if (element.Character.IsMounted)
                {
                    cost *= mountedFactor;
                }
                bill += (int)cost;
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
        /// friendly one). The men's spoils purse is not drawn on here (that purse is the wage they bank and
        /// spend on drink, luxuries and promotions); what the treasury cannot cover is offered instead to
        /// the owner and then the town's burghers through <see cref="GarrisonSubsidy"/>, and what none of
        /// the three can reach simply leaves that day's mending undone. Money conserved throughout -- the
        /// three payers between them give exactly what the market receives.
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

            // What the treasury could not reach is offered to the owner (only where he has taken the
            // fief's garrison on -- an unlimited wage limit) and then to the town's burghers. Anything
            // still short is simply that day's mending left undone, as before.
            int ownerPaid;
            int citizensPaid;
            GarrisonSubsidy.Cover(settlement, bill - paid, GarrisonSubsidy.Purpose.Maintenance,
                int.MaxValue, out ownerPaid, out citizensPaid);
            int total = paid + ownerPaid + citizensPaid;
            if (total <= 0)
            {
                return;
            }

            Settlement market = settlement.IsTown ? settlement
                : (UpgradeSupply.FindNearestFriendlyTown(garrison)?.Settlement);
            if (market != null)
            {
                // Money conserved: exactly what the three payers gave between them reaches the market.
                TroopMarketFeedback.RegisterPurchase(market, null, total, SettlementWealth.Source.Maintenance);
            }

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("GARRISON", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "maintenance " + bill + "d  ·  fief paid " + paid + "d"
                    + (ownerPaid > 0 ? ", owner " + ownerPaid + "d" : "")
                    + (citizensPaid > 0 ? ", citizens " + citizensPaid + "d" : "")
                    + (market != null ? " to " + market.Name : " — no town in reach")
                    + "  ·  treasury now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }
        }
    }
}
