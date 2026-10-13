using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;

namespace RBMCampaign
{
    /// <summary>
    /// Sets each AI clan's war parties' daily wage limit (<c>MobileParty.PaymentLimit</c>) from a real budget in
    /// RBM wage money, in place of vanilla's gold bands.
    ///
    /// Vanilla (<c>ClanVariablesCampaignBehavior.MakeClanFinancialEvaluation</c>, once a day per non-player clan)
    /// reads only the clan LEADER's gold through three fixed bands (up to 30k: 200 + 400(g/30k)^2; 30-90k:
    /// 600-1200; above: <c>MaxWagePaymentLimit</c>, x1.5 for the leader's own party). The limit is what every
    /// "how many men can this lord afford" question reads -- <c>PartyBaseHelper.FindPartySizeNormalLimit</c>
    /// (<c>(PaymentLimit - leader wage) / Campaign.AverageWage + 1</c>), recruiting's
    /// <c>GetAvailableWageBudget</c> / <c>IsWageLimitExceeded</c>, prisoner recruiting, upgrades, the garrison
    /// hand-over and the wage-limit desertion -- and those bands were built for vanilla's wages, a tenth of RBM's.
    /// A x10 patch put them back on scale, but <see cref="ClanPartyPurse"/> keeps much of an AI clan's money in
    /// its member parties' purses and lends the leader down to its reserve, so leaders sat at ~20k, the steep
    /// bottom of the band, and their parties -- the only ones that can raise armies -- stayed too small to.
    ///
    /// The budget, per clan per day:
    /// <list type="number">
    /// <item><b>On hand</b> = the clan leader's gold + every other lord-party leader's gold in the clan (a lord
    /// party's trade gold IS its leader's gold; each hero counted once) - <see cref="WorkingReserve"/>.</item>
    /// <item><b>Steady income</b> = the clan's applied daily gold change from the day before, with the field
    /// parties' own flows taken out (the clan leader's party wage, the member parties' purse top-ups and the
    /// clan's skim of their purses). What is left is fiefs, workshops, caravans, tariffs, RBM's wealth tax, the
    /// kingdom's support, mercenary pay ... minus garrisons, administration, troop maintenance, tribute and the
    /// rest -- everything but the field wages this budget is for. See <see cref="IncomeCapture"/>.</item>
    /// <item><b>Daily wage budget</b> = max(steady + on hand / <see cref="RunwayDays"/>, on hand /
    /// <see cref="SavingsFloorDays"/>), never below 0. The second term is the savings floor: a clan whose fiefs lose
    /// money still spends a thirtieth of its savings a day on its field parties instead of fielding nothing.</item>
    /// <item><b>Split</b> across the clan's war parties by <c>PartySizeLimit</c>, the clan leader's party weighted
    /// x<see cref="LeaderPartyWeight"/> as in vanilla. Each share, floored (<see cref="FloorShareOfSizeLimit"/>),
    /// is that party's <c>PaymentLimit</c> in RBM wage money -- no scale factor.</item>
    /// </list>
    ///
    /// <c>MaxWagePaymentLimit</c> (10,000) is a sentinel, not a ceiling: <c>HasLimitedWage()</c> is false at exactly
    /// that value, and vanilla's "unlimited" is not unlimited either -- recruiting still stops at
    /// <c>PaymentLimit - TotalWage</c>, i.e. ~170 men at RBM's average wage. So a budgeted party is never given the
    /// sentinel: a share landing on it exactly is nudged one above, and a share above it is written as it is, a
    /// real limit. Mercenary companies keep the sentinel (no wage-limit desertion; see the company branch).
    ///
    /// Patched as a prefix that replaces vanilla's evaluation for every non-player clan (vanilla never calls it
    /// for the player's clan), so a clan with no income record yet keeps yesterday's limits instead of falling
    /// back to vanilla's bands. Gated on <c>rbmCampaignEnabled</c>; off, vanilla runs unchanged.
    /// </summary>
    internal static class PartyWageBudget
    {
        /// <summary>Days of field wages the clan's money on hand is spread over (the user's call).</summary>
        internal const int RunwayDays = 15;

        /// <summary>
        /// The savings floor: whatever its income, a clan spends at least on hand / this many days a day on its field
        /// parties. Without it a clan whose garrisons and fiefs run at a loss (negative steady income) got a budget of
        /// 0 however much gold it sat on -- in the first test, 28 of 350 clans, one with 122k on hand.
        /// </summary>
        internal const int SavingsFloorDays = 30;

        /// <summary>
        /// The clan's working float, left out of its money on hand: <see cref="ClanPartyPurse.LenderReserve"/>, the
        /// gold the clan leader never lends to his members' recruiting. One per clan, not one per party: the member
        /// purses are refilled to <c>ClanPartyPurse</c>'s 20k floor every day out of the leader's gold, so a
        /// reserve per purse would keep a whole extra 20k idle for every party the clan fields, and a clan that
        /// split its men into more parties would field fewer of them.
        /// </summary>
        internal const int WorkingReserve = ClanPartyPurse.LenderReserve;

        /// <summary>Vanilla's x1.5 for the clan leader's own party, kept as a weight in the split.</summary>
        internal const float LeaderPartyWeight = 1.5f;

        /// <summary>
        /// The least a party's limit may be, as a share of its size limit paid at <c>Campaign.AverageWage</c>: enough
        /// to keep a retinue (a fifth of the party) on a broke clan rather than letting the wage-limit desertion
        /// strip it to nothing. Vanilla's own bottom band (200 at no gold) holds ~30-35 men at vanilla's wages, about
        /// a fifth to a quarter of a lord's size limit, so this keeps that. The floor is a promise, not money: a clan
        /// that cannot pay it pays what it has and the men lose morale, vanilla's rule for unpaid wages.
        /// </summary>
        internal const float FloorShareOfSizeLimit = 0.2f;

        /// <summary>
        /// One clan's last applied finance pass, with the field-party flows apart. A class so it can sit in a
        /// <see cref="ConditionalWeakTable{TKey,TValue}"/> keyed by the clan: nothing outlives its campaign.
        /// </summary>
        internal sealed class IncomeRecord
        {
            public int Day;
            public float Applied;
            public float LeaderPartyWage;
            public float MemberTopUps;
            public float MemberSkims;

            /// <summary>The applied change with the field flows (leader wage and top-ups negative, skims positive) taken out.</summary>
            public float Steady
            {
                // The model returns the leader's wage and the top-ups as negative amounts, so subtracting them adds
                // the costs back: the steady income is what the clan earns before its field parties cost it anything.
                get { return Applied - LeaderPartyWage - MemberTopUps - MemberSkims; }
            }
        }

        private static readonly ConditionalWeakTable<Clan, IncomeRecord> _income = new ConditionalWeakTable<Clan, IncomeRecord>();

        private static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled; }
        }

        [HarmonyPatch(typeof(ClanVariablesCampaignBehavior), "MakeClanFinancialEvaluation")]
        private static class EvaluationPatch
        {
            private static bool Prefix(Clan clan)
            {
                if (!IsEnabled || clan == null || clan == Clan.PlayerClan)
                {
                    return true;
                }
                try
                {
                    Evaluate(clan);
                }
                catch (Exception)
                {
                    // A throw here would stop the clan's daily tick; leave its limits as they were instead.
                }
                return false;
            }
        }

        private static void Evaluate(Clan clan)
        {
            int unlimited = Campaign.Current.Models.PartyWageModel.MaxWagePaymentLimit;

            // A mercenary company has no wage cap: vanilla deserts up to 20 men a day from a party whose wage
            // bill exceeds its limit, which bled companies white. MaxWagePaymentLimit is vanilla's "unlimited"
            // (HasLimitedWage is false at exactly that value). Unpaid wages still cost morale when broke.
            if (MercenaryCompanies.IsCompany(clan))
            {
                bool changed = false;
                foreach (WarPartyComponent component in clan.WarPartyComponents)
                {
                    MobileParty party = component?.MobileParty;
                    if (party != null && party.PaymentLimit != unlimited)
                    {
                        party.SetWagePaymentLimit(unlimited);
                        changed = true;
                    }
                }
                if (changed && PartyBudgetLog.IsEnabled)
                {
                    PartyBudgetLog.Log("COMPANY", clan, "wage limit set to vanilla's unlimited (" + unlimited + ")");
                }
                return;
            }

            Hero leader = clan.Leader;
            if (leader == null)
            {
                return;
            }

            List<MobileParty> parties = new List<MobileParty>();
            foreach (WarPartyComponent component in clan.WarPartyComponents)
            {
                MobileParty party = component?.MobileParty;
                if (party != null)
                {
                    parties.Add(party);
                }
            }
            if (parties.Count == 0)
            {
                return;
            }

            // Money on hand: the leader's gold and every other lord-party leader's purse, each hero once.
            long leaderGold = Math.Max(0, leader.Gold);
            long purses = 0;
            HashSet<Hero> counted = new HashSet<Hero> { leader };
            foreach (MobileParty party in parties)
            {
                Hero partyLeader = party.LeaderHero;
                if (partyLeader != null && partyLeader.Clan == clan && counted.Add(partyLeader))
                {
                    purses += Math.Max(0, partyLeader.Gold);
                }
            }
            long onHand = Math.Max(0L, leaderGold + purses - WorkingReserve);

            if (!_income.TryGetValue(clan, out IncomeRecord record))
            {
                if (PartyBudgetLog.IsEnabled)
                {
                    PartyBudgetLog.Log("KEEP", clan, "no income record yet; limits kept  ·  leader gold " + leaderGold
                        + "d, other purses " + purses + "d, " + parties.Count + " war parties");
                }
                return;
            }

            double steady = record.Steady;
            double runwayBudget = steady + (double)onHand / RunwayDays;
            double savingsFloor = (double)onHand / SavingsFloorDays;
            bool savingsFloorWon = savingsFloor > runwayBudget;
            double budget = Math.Max(0.0, Math.Max(runwayBudget, savingsFloor));
            float averageWage = Math.Max(1f, Campaign.Current.AverageWage);

            double totalWeight = 0.0;
            foreach (MobileParty party in parties)
            {
                totalWeight += Weight(party, leader);
            }

            StringBuilder shares = PartyBudgetLog.IsEnabled ? new StringBuilder() : null;
            long totalLimits = 0;
            foreach (MobileParty party in parties)
            {
                double weight = Weight(party, leader);
                double share = totalWeight > 0.0 ? budget * weight / totalWeight : 0.0;
                int sizeLimit = Math.Max(1, party.Party.PartySizeLimit);
                double floor = FloorShareOfSizeLimit * sizeLimit * averageWage;
                long limit = (long)Math.Round(Math.Max(share, floor));
                limit = Math.Min(limit, int.MaxValue);
                if (limit == unlimited)
                {
                    // Never the sentinel: at exactly MaxWagePaymentLimit the party would read as unlimited.
                    limit++;
                }
                int oldLimit = party.PaymentLimit;
                if (oldLimit != (int)limit)
                {
                    party.SetWagePaymentLimit((int)limit);
                }
                totalLimits += limit;

                if (shares != null)
                {
                    int affordable = Math.Min(sizeLimit, (int)(PartyBaseHelperAffordableMen(party, (int)limit, averageWage)));
                    shares.Append(Environment.NewLine).Append(Pad("SHARE"))
                        .Append(PartyBudgetLog.Who(party)).Append(party.LeaderHero == leader ? " (clan leader)" : "")
                        .Append("  ·  weight ").Append(weight.ToString("0"))
                        .Append("  ·  share ").Append(share.ToString("0"))
                        .Append(share < floor ? " (floor " + floor.ToString("0") + ")" : "")
                        .Append("  ·  limit ").Append(limit).Append(" (was ").Append(oldLimit == unlimited ? "unlimited" : oldLimit.ToString()).Append(")")
                        .Append("  ·  wage ").Append(party.TotalWage)
                        .Append("  ·  men ").Append(party.MemberRoster.TotalManCount).Append("/").Append(sizeLimit)
                        .Append("  ·  affordable ").Append(affordable)
                        .Append("  ·  purse ").Append(party.LeaderHero != null ? party.LeaderHero.Gold.ToString() : "-").Append("d")
                        .Append(party.Army != null ? "  ·  in army" : "");
                }
            }

            if (shares != null)
            {
                PartyBudgetLog.Log("BUDGET", clan,
                    "leader gold " + leaderGold + "d + other leaders' purses " + purses + "d - reserve " + WorkingReserve
                    + "d = on hand " + onHand + "d"
                    + "  ·  steady " + steady.ToString("0") + "d/day (day " + record.Day + " applied " + record.Applied.ToString("0")
                    + " + leader party wage " + (-record.LeaderPartyWage).ToString("0")
                    + " + member top-ups " + (-record.MemberTopUps).ToString("0")
                    + " - member skims " + record.MemberSkims.ToString("0") + ")"
                    + "  ·  budget " + budget.ToString("0") + "d/day ("
                    + (savingsFloorWon
                        ? "SAVINGS FLOOR on hand/" + SavingsFloorDays + "; steady + on hand/" + RunwayDays + " was " + runwayBudget.ToString("0")
                        : "steady + on hand/" + RunwayDays + "; savings floor on hand/" + SavingsFloorDays + " was " + savingsFloor.ToString("0"))
                    + ")"
                    + "  ·  limits set " + totalLimits + "d over " + parties.Count + " parties"
                    + "  ·  avg wage " + averageWage.ToString("0.0")
                    + (clan.IsMinorFaction ? "  ·  minor faction" : "")
                    + shares.ToString());
            }
        }

        private static double Weight(MobileParty party, Hero clanLeader)
        {
            double weight = Math.Max(1, party.Party.PartySizeLimit);
            return party.LeaderHero == clanLeader ? weight * LeaderPartyWeight : weight;
        }

        // FindPartySizeNormalLimit's men, for the log (it subtracts a non-leader party leader's own wage first).
        private static float PartyBaseHelperAffordableMen(MobileParty party, int limit, float averageWage)
        {
            int leaderWage = 0;
            Hero partyLeader = party.LeaderHero;
            Clan ownerClan = party.Party.Owner?.Clan;
            if (partyLeader != null && ownerClan != null && partyLeader != ownerClan.Leader)
            {
                leaderWage = Math.Min(partyLeader.CharacterObject.TroopWage, party.TotalWage);
            }
            return Math.Max(1f, (limit - leaderWage) / averageWage + 1f);
        }

        private static string Pad(string tag)
        {
            // Continuation lines line up under the message column of the BUDGET line.
            return new string(' ', 10) + tag.PadRight(12);
        }

        /// <summary>
        /// Records each AI clan's steady income off its own daily apply pass -- the one
        /// <c>CalculateClanGoldChange(clan, applyWithdrawals: true)</c> that <c>DailyTickClan</c> runs right after the
        /// evaluation above, so today's budget reads yesterday's day. The apply pass is the only one that carries
        /// RBM's own lines for an AI clan (wealth tax, castle surplus, troop maintenance, garrison subsidies,
        /// mercenary pay all show on a display pass for the player's clan only), and recording it costs no extra
        /// model call. While it runs, the three field-party flows are summed from their own methods:
        /// <c>AddExpenseFromLeaderParty</c> (the clan leader's party wage), <c>AddPartyExpense</c> for a war party
        /// (the clan's top-up of a member's purse after its wage) and <c>AddIncomeFromParty</c> for a war party (the
        /// clan's skim of it). Those are the clan's spending on and from its field parties, which the budget is
        /// for; a member party's wage itself is paid from its purse, which is in the money on hand.
        ///
        /// What stays in: everything else, including RBM's troop maintenance for the field parties (a real cost of
        /// keeping them, so the wage budget is what is left after it), and garrison wages. Approximation: War Sails'
        /// Coastal Guard Edict line (a share of port garrisons' top-ups) is added by its wrapping model after this
        /// postfix and is missed -- a garrison cost, small, and only under that policy.
        /// </summary>
        private static class IncomeCapture
        {
            // Main thread only (DailyTickClan). The clan whose apply pass is running, and its running flows.
            private static Clan _clan;
            private static float _leaderWage;
            private static float _topUps;
            private static float _skims;

            private static bool Capturing(Clan clan, bool applyWithdrawals)
            {
                return applyWithdrawals && clan != null && _clan == clan;
            }

            [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange")]
            private static class ApplyPassPatch
            {
                [HarmonyPriority(Priority.First)]
                private static void Prefix(Clan clan, bool applyWithdrawals)
                {
                    if (!applyWithdrawals || !IsEnabled || clan == null || clan == Clan.PlayerClan || clan.IsBanditFaction)
                    {
                        return;
                    }
                    _clan = clan;
                    _leaderWage = 0f;
                    _topUps = 0f;
                    _skims = 0f;
                }

                // Last, so every other postfix's line (maintenance, wealth tax, subsidies, mercenary pay) is in.
                [HarmonyPriority(Priority.Last)]
                private static void Postfix(Clan clan, bool applyWithdrawals, ExplainedNumber __result)
                {
                    if (!Capturing(clan, applyWithdrawals))
                    {
                        return;
                    }
                    IncomeRecord record = _income.GetValue(clan, _ => new IncomeRecord());
                    record.Day = (int)CampaignTime.Now.ToDays;
                    record.Applied = __result.ResultNumber;
                    record.LeaderPartyWage = _leaderWage;
                    record.MemberTopUps = _topUps;
                    record.MemberSkims = _skims;
                }

                private static void Finalizer(Clan clan, bool applyWithdrawals)
                {
                    if (applyWithdrawals && _clan == clan)
                    {
                        _clan = null;
                    }
                }
            }

            [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddExpenseFromLeaderParty")]
            private static class LeaderPartyWagePatch
            {
                private static void Postfix(Clan clan, bool applyWithdrawals, int __result)
                {
                    if (Capturing(clan, applyWithdrawals))
                    {
                        _leaderWage += __result;
                    }
                }
            }

            [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddPartyExpense")]
            private static class MemberTopUpPatch
            {
                private static void Postfix(MobileParty party, Clan clan, bool applyWithdrawals, int __result)
                {
                    if (Capturing(clan, applyWithdrawals) && party?.WarPartyComponent != null)
                    {
                        _topUps += __result;
                    }
                }
            }

            [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddIncomeFromParty")]
            private static class MemberSkimPatch
            {
                private static void Postfix(MobileParty party, Clan clan, bool applyWithdrawals, int __result)
                {
                    if (Capturing(clan, applyWithdrawals) && party?.WarPartyComponent != null)
                    {
                        _skims += __result;
                    }
                }
            }
        }
    }
}
