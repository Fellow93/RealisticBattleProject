using HarmonyLib;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The stipend a settlement pays the townsmen and villagers who muster as its militia.
    ///
    /// Militia are the one armed body in the game nobody pays at all. They appear in no clan's
    /// expenses, no wage model bills anyone for them, and they cost their settlement nothing -- vanilla
    /// grows them out of prosperity and loyalty and leaves it there. That is defensible for a levy in
    /// the abstract, but under the ledger it left a real hole: RBM credits every party's daily wage
    /// into its troops' spoils purses, so militia were accruing a full soldier's pay out of thin air.
    ///
    /// So they are paid, and paid sparingly: a tenth of a soldier's wage, from the settlement's own
    /// treasury. That is what a militia is -- men who work for a living and are given something for the
    /// days they stand a watch instead. The other nine tenths were never real money and are no longer
    /// credited to anyone.
    /// </summary>
    public static class MilitiaUpkeep
    {
        /// <summary>
        /// Share of a full soldier's wage a militiaman draws. Far below the garrison's, deliberately: a
        /// garrison soldier does nothing else, a militiaman has a trade and is given something for the
        /// days he stands a watch instead of working it.
        ///
        /// A tenth rather than a fifth. At a fifth the militia bill was the largest single charge on a
        /// town's treasury -- 27,444 denars over seven days at Danustica, more than its garrison's
        /// wages and four times its tariff income -- for men who are not soldiers and were free in
        /// vanilla.
        /// </summary>
        public const float MilitiaWageShare = 0.1f;

        /// <summary>
        /// Days of a militiaman's stipend a settlement must have in hand to keep him under arms.
        ///
        /// This is what turns the stipend from a bill into a limit. A settlement does not muster the
        /// militia its prosperity would allow and then go broke paying them: it musters what it can
        /// keep paying, and the purse is what says how many that is. A month's pay per man is the
        /// margin -- enough that a single bad convoy does not disband the watch, little enough that a
        /// village cannot field a standing company on a harvest's takings.
        /// </summary>
        public const int MilitiaPayDaysHeld = 30;

        /// <summary>
        /// Men a settlement sheds per day while it is over what it can afford. Deliberately slow: men
        /// drift home when the pay stops, they are not dismissed on parade.
        /// </summary>
        public const float MilitiaShedPerDay = 1f;

        private static readonly TextObject UnaffordableText = new TextObject("{=RBM_militia_unpaid}Cannot be paid");

        /// <summary>
        /// What this settlement's militia actually costs it in stipend each day.
        /// </summary>
        /// <remarks>
        /// Read off the militia party's own wage bill rather than priced per head. An earlier version
        /// costed every militiaman as a tier-one recruit on the grounds that most of them are, and that
        /// pricing the real roster would make the cap chase its own tail. Both halves of that were
        /// wrong. Measured over seven days, Danustica spent 27,444 denars on militia -- about twice
        /// what a tier-one roster of that size comes to -- so the cap was licensing roughly double the
        /// militia the treasury could keep, and the treasury went bankrupt on the difference.
        ///
        /// The runaway it was guarding against does not exist either, because this bills the WHOLE
        /// roster rather than an average man: shedding always lowers the bill, so the test converges
        /// instead of chasing a rising average down to zero.
        /// </remarks>
        public static int DailyStipendBill(Settlement settlement)
        {
            MobileParty party = (settlement.MilitiaPartyComponent != null)
                ? settlement.MilitiaPartyComponent.MobileParty
                : null;
            if (party != null && party.IsActive)
            {
                return (int)(party.TotalWage * MilitiaWageShare);
            }

            // Militia counted but not yet mustered into a party: nothing to read a wage off, so fall
            // back to a recruit's rate for the headcount.
            return (int)(settlement.Militia * MilitiaWageShare * RBMConfig.RBMConfig.troopWageTierBase);
        }

        /// <summary>
        /// Whether the purse can keep the militia the settlement currently has under arms -- a month of
        /// their real stipend in hand.
        /// </summary>
        public static bool CanKeepMilitia(Settlement settlement)
        {
            int bill = DailyStipendBill(settlement);
            if (bill <= 0)
            {
                return true;
            }
            return SettlementWealth.GetSettlementWealth(settlement) >= bill * MilitiaPayDaysHeld;
        }

        /// <summary>
        /// Holds a settlement's militia to what it can pay for.
        /// </summary>
        /// <remarks>
        /// Vanilla grows militia out of prosperity, hearths and loyalty, and nothing anywhere asks
        /// whether the place can afford them -- which was harmless while militia were free and becomes
        /// the wrong way round the moment they are not. A settlement that cannot pay should field fewer
        /// men, not go bankrupt fielding the same number.
        ///
        /// Applied to every settlement rather than villages alone. A town's purse is large enough that
        /// the cap will not normally bind, so the rule costs nothing there -- but a town that has spent
        /// itself dry on garrison wages and food SHOULD start losing its militia too, and carving out
        /// an exception would only hide that.
        ///
        /// The result is pushed down to the shed rate rather than replaced, so vanilla's own lines stay
        /// on the breakdown and the player can see what the militia would have been.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultSettlementMilitiaModel), "CalculateMilitiaChange")]
        private static class AffordableMilitiaPatch
        {
            private static void Postfix(Settlement settlement, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
                {
                    return;
                }

                if (CanKeepMilitia(settlement))
                {
                    return;
                }

                if (__result.ResultNumber > -MilitiaShedPerDay)
                {
                    __result.Add(-MilitiaShedPerDay - __result.ResultNumber, UnaffordableText);
                }
            }
        }

        /// <summary>
        /// Pays a militia stack's stipend out of its settlement's treasury and reports what the purse
        /// could actually cover.
        /// </summary>
        /// <remarks>
        /// The caller credits exactly what this returns, so the payment and the deposit are the same
        /// number by construction and no stipend can be credited that nobody paid. A settlement with an
        /// empty treasury simply does not pay its militia that day -- unlike the garrison, there is no
        /// owner backstop, because a lord never agreed to pay these men in the first place.
        /// </remarks>
        public static int PayStipend(MobileParty militiaParty, int fullWage)
        {
            if (fullWage <= 0)
            {
                return 0;
            }

            Settlement settlement = (militiaParty.CurrentSettlement ?? militiaParty.HomeSettlement);
            if (settlement == null)
            {
                return 0;
            }

            int stipend = (int)(fullWage * MilitiaWageShare);
            if (stipend <= 0)
            {
                return 0;
            }

            return SettlementWealth.Debit(settlement, stipend, SettlementWealth.Source.Militia);
        }
    }
}
