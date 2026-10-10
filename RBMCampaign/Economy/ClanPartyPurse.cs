using System;
using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace RBMCampaign
{
    /// <summary>
    /// Lets a clan party led by someone other than the clan leader -- a companion's, a family member's,
    /// an AI clan's younger lords' -- hold and spend enough gold to buy the recruits RBM prices.
    ///
    /// A lord party's trade gold IS its leader's own gold (<c>MobileParty.PartyTradeGold</c>), and for a
    /// party not led by the clan leader that purse is managed by the clan's daily finances: topped up
    /// from clan gold to <c>PartyGoldLowerThreshold</c> (5,000) in <c>AddPartyExpense</c>, and skimmed
    /// back a tenth a day above a hard-coded 10,000 in <c>AddIncomeFromParty</c>. The AI recruits only
    /// while the purse exceeds the recruit's price (<c>RecruitmentCampaignBehavior.RecruitVolunteersFromNotable</c>),
    /// and <see cref="RecruitSupply"/> prices a recruit raised outside the buyer clan's own fiefs at his
    /// kit plus five days' wage -- a tier 3 man 5,000-13,000, a tier 4 one 16,000 and up. So such a
    /// party could never afford anything above tier 1 or 2, and a mercenary clan, which has no fiefs of
    /// its own, never anything better at all.
    ///
    /// Three parts, all keyed on one predicate (<see cref="IsClanMemberParty"/>), for every clan,
    /// player and AI alike:
    /// - the daily top-up floor is raised to <see cref="LordPartyGoldFloor"/>;
    /// - the clan's skim only starts above <see cref="LordPartyGoldSkimThreshold"/>;
    /// - while the party looks over a settlement's recruits, it may borrow from the clan leader's gold,
    ///   up to <see cref="DailyRecruitLoanCap"/> a day, and hands back whatever it did not spend.
    /// </summary>
    public static class ClanPartyPurse
    {
        /// <summary>The purse the clan tops these parties up to each day (vanilla 5,000).</summary>
        private const int LordPartyGoldFloor = 20000;

        /// <summary>The purse above which the clan skims a tenth of the excess each day (vanilla 10,000).</summary>
        private const int LordPartyGoldSkimThreshold = 40000;

        /// <summary>The most one party may spend of borrowed clan gold on recruits in one day.</summary>
        private const int DailyRecruitLoanCap = 20000;

        /// <summary>The gold the clan leader always keeps back from recruit loans.</summary>
        private const int LenderReserve = 20000;

        // Vanilla's figures, for the log's comparison only.
        private const int VanillaPartyGoldFloor = 5000;
        private const int VanillaSkimThreshold = 10000;

        /// <summary>
        /// A REFILL/SKIM line in the spoils log. The player's clan is logged always; AI clans, whose
        /// parties all refill daily, only with verbose logging on.
        /// </summary>
        private static void LogPurse(Clan clan, MobileParty party, string category, string message)
        {
            string line = SpoilsLog.Describe(party.Party) + " (" + clan.Name + "): " + message;
            if (clan == Clan.PlayerClan)
            {
                SpoilsLog.Log(category, party.Party, line);
            }
            else
            {
                SpoilsLog.LogVerbose(category, party.Party, line);
            }
        }

        /// <summary>
        /// A clan member's lord party that the clan leader does not lead in person -- the parties whose
        /// purse the clan's finances manage. The clan leader's own purse is the clan's gold and needs none
        /// of this.
        /// </summary>
        private static bool IsClanMemberParty(MobileParty party)
        {
            return party != null && party.IsLordParty && party.LeaderHero != null
                && party.LeaderHero.Clan?.Leader != null
                && party.LeaderHero != party.LeaderHero.Clan.Leader;
        }

        // Set for the length of one AddPartyExpense call on a clan member's party, so the getter patch
        // below raises the floor there and nowhere else. Thread-static so a finance projection on another
        // thread (none today) could never see it.
        [ThreadStatic]
        private static bool _inClanMemberPartyExpense;

        /// <summary>
        /// Marks the daily top-up of a clan member's party, for <see cref="PartyGoldFloorPatch"/>.
        ///
        /// The floor is raised through the getter rather than by rewriting the method so vanilla's own
        /// arithmetic stays as it is: the poverty clamp for a struggling AI clan, the cap of the top-up by
        /// what the clan can pay, and the same figure on the display pass (Expected Gold, the clan
        /// finance tooltip) as on the apply pass. <c>PartyGoldLowerThreshold</c> has other readers --
        /// the gold a new party is funded with from the clan screen and the companion dialog, a new
        /// garrison's float, War Sails' copy of this method, which only ever runs for garrisons -- and
        /// all of them keep vanilla's 5,000.
        /// </summary>
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddPartyExpense")]
        private static class PartyExpenseScopePatch
        {
            private static void Prefix(MobileParty party)
            {
                _inClanMemberPartyExpense = RBMConfig.RBMConfig.rbmCampaignEnabled && IsClanMemberParty(party);
            }

            // Logs the day's top-up when the raised floor made it bigger than vanilla's. For a lord party
            // with a leader the method returns minus the top-up, and vanilla's would have been the same
            // top-up taken only to 5,000: both start from the purse after wages and share the clan cap.
            private static void Postfix(MobileParty party, Clan clan, bool applyWithdrawals, int __result)
            {
                if (!_inClanMemberPartyExpense || !applyWithdrawals || !SpoilsLog.IsEnabled)
                {
                    return;
                }
                int topUp = -__result;
                int afterWages = party.PartyTradeGold - topUp;
                int vanillaTopUp = Math.Max(0, Math.Min(VanillaPartyGoldFloor - afterWages, topUp));
                if (topUp <= vanillaTopUp)
                {
                    return;
                }
                LogPurse(clan, party, "REFILL", "purse " + afterWages + "d after wages, clan topped up +" + topUp
                    + "d to " + party.PartyTradeGold + "d (vanilla +" + vanillaTopUp + "d)");
            }

            private static void Finalizer()
            {
                _inClanMemberPartyExpense = false;
            }
        }

        /// <summary>
        /// The raised floor itself. <c>AddPartyExpense</c> reads the property through a virtual call on
        /// the model, twice (the comparison and the shortfall), so both see the same raised figure.
        /// </summary>
        [HarmonyPatch(typeof(DefaultClanFinanceModel), nameof(DefaultClanFinanceModel.PartyGoldLowerThreshold), MethodType.Getter)]
        private static class PartyGoldFloorPatch
        {
            private static void Postfix(ref int __result)
            {
                if (_inClanMemberPartyExpense)
                {
                    __result = Math.Max(__result, LordPartyGoldFloor);
                }
            }
        }

        /// <summary>
        /// The clan's daily skim of a clan member's purse, moved up to <see cref="LordPartyGoldSkimThreshold"/>
        /// so it no longer takes back the floor above and the money a party is saving for its recruits.
        ///
        /// Vanilla's body with the constant swapped, for these parties only; the caravan-only branches
        /// (Great Investor, the player's asset income event) cannot apply to a lord party and are left
        /// out. Caravans are <see cref="CaravanCapital"/>'s, garrisons and the clan leader's party fall
        /// through to the original.
        /// </summary>
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddIncomeFromParty")]
        private static class PartySkimPatch
        {
            /// <summary>Vanilla's share of the excess, unchanged.</summary>
            private const int SkimDivisor = 10;

            private static bool Prefix(MobileParty party, Clan clan, bool applyWithdrawals, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || clan == null || !IsClanMemberParty(party))
                {
                    return true;
                }

                __result = 0;

                // Vanilla's own guard, kept as it is there.
                if (!party.IsActive || party.LeaderHero == clan.Leader)
                {
                    return false;
                }

                int purse = party.PartyTradeGold;
                if (purse <= LordPartyGoldSkimThreshold)
                {
                    return false;
                }

                int skim = (purse - LordPartyGoldSkimThreshold) / SkimDivisor;
                __result = skim;

                if (applyWithdrawals)
                {
                    party.PartyTradeGold -= skim;
                    if (skim > 0)
                    {
                        SkillLevelingManager.OnTradeProfitMade(party.LeaderHero, skim);
                    }
                }
                return false;
            }

            // Logs the skim whenever vanilla would have taken more, i.e. whenever the purse was above
            // vanilla's 10,000 (the purse read here is after this skim, so it is added back).
            private static void Postfix(MobileParty party, Clan clan, bool applyWithdrawals, int __result)
            {
                if (!applyWithdrawals || !SpoilsLog.IsEnabled
                    || !RBMConfig.RBMConfig.rbmCampaignEnabled || clan == null || !IsClanMemberParty(party)
                    || !party.IsActive || party.LeaderHero == clan.Leader)
                {
                    return;
                }
                int purse = party.PartyTradeGold + __result;
                int vanillaSkim = (purse - VanillaSkimThreshold) / SkimDivisor;
                if (vanillaSkim <= __result)
                {
                    return;
                }
                LogPurse(clan, party, "SKIM", "purse " + purse + "d, clan skimmed " + __result
                    + "d (vanilla " + vanillaSkim + "d)");
            }
        }

        // Borrowed clan gold each party has spent on recruits today, and the campaign day it is for.
        // Not saved: a reload only forgets today's tally. Main thread only (CheckRecruiting runs from
        // OnBeforeSettlementEntered and HourlyTickParty).
        private static readonly Dictionary<MobileParty, int> _drawnToday = new Dictionary<MobileParty, int>();
        private static int _drawnDay = -1;

        private sealed class RecruitLoan
        {
            public Hero Lender;
            public Hero Borrower;
            public int Amount;
        }

        /// <summary>
        /// A clan member's party looking over a settlement's recruits borrows from the clan leader's gold
        /// for the length of the look, and hands back whatever it did not spend.
        ///
        /// The loan sits in the leader's own purse (which is the party's trade gold) only while vanilla's
        /// recruit pass runs, so every gate in it -- the purse above the recruit's price, the tavern's
        /// hire count, <c>RecruitVolunteersFromNotable</c>'s extra rolls on <c>sqrt(purse / 10000)</c> --
        /// sees the larger purse. That last one makes a funded party recruit more eagerly as well as
        /// dearer men, as a clan leader with a big purse already does in vanilla; intended. The charge
        /// itself is vanilla's, to the leader's gold; <see cref="RecruitSupply.PayRecruitPrice"/> only
        /// redirects the price already paid to the settlement and never touches the payer's gold.
        ///
        /// What is handed back is the smaller of the loan and what is left in the purse, so the party
        /// spends its own gold first and the loan last. Only the part kept counts toward the day's cap.
        /// The lender keeps <see cref="LenderReserve"/>, and an AI clan leader at least the line under
        /// which vanilla's same pass stops his clan members recruiting at all, so the loan can never turn
        /// that gate off.
        /// </summary>
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "CheckRecruiting")]
        private static class RecruitLoanPatch
        {
            private static void Prefix(MobileParty mobileParty, out RecruitLoan __state)
            {
                __state = null;
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !IsClanMemberParty(mobileParty))
                {
                    return;
                }
                // A full party recruits nothing (vanilla's own size gate); no point moving gold for it.
                if (mobileParty.Party.NumberOfAllMembers >= mobileParty.Party.PartySizeLimit)
                {
                    return;
                }

                Hero borrower = mobileParty.LeaderHero;
                Hero lender = borrower.Clan.Leader;
                if (!lender.IsAlive || !borrower.IsAlive)
                {
                    return;
                }

                // Vanilla lets a non-leader recruit only while the clan's gold is strictly above this line
                // (0 for the player's clan), so the reserve sits one above it.
                int reserve = Math.Max(LenderReserve,
                    (int)HeroHelper.StartRecruitingMoneyLimitForClanLeader(borrower) + 1);

                int day = (int)CampaignTime.Now.ToDays;
                if (day != _drawnDay)
                {
                    _drawnToday.Clear();
                    _drawnDay = day;
                }
                _drawnToday.TryGetValue(mobileParty, out int drawn);

                int loan = Math.Min(lender.Gold - reserve, DailyRecruitLoanCap - drawn);
                if (loan <= 0)
                {
                    return;
                }

                lender.ChangeHeroGold(-loan);
                borrower.ChangeHeroGold(loan);
                __state = new RecruitLoan { Lender = lender, Borrower = borrower, Amount = loan };
            }

            // Runs whether or not the recruit pass threw, so a loan is never left in the purse; returns
            // nothing, so an exception goes on exactly as it was.
            private static void Finalizer(MobileParty mobileParty, RecruitLoan __state)
            {
                if (__state == null)
                {
                    return;
                }

                int repay = Math.Min(__state.Amount, Math.Max(0, __state.Borrower.Gold));
                if (repay > 0)
                {
                    __state.Borrower.ChangeHeroGold(-repay);
                    __state.Lender.ChangeHeroGold(repay);
                }

                int spent = __state.Amount - repay;
                if (spent <= 0)
                {
                    return;
                }

                _drawnToday.TryGetValue(mobileParty, out int drawn);
                _drawnToday[mobileParty] = drawn + spent;

                if (SpoilsLog.IsEnabled)
                {
                    SpoilsLog.Log("LOAN", mobileParty.Party, SpoilsLog.Describe(mobileParty.Party)
                        + " spent " + spent + "d borrowed from " + __state.Lender.Name
                        + " on recruits (" + (drawn + spent) + "/" + DailyRecruitLoanCap + " today)");
                }
            }
        }
    }
}
