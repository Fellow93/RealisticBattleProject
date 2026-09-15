using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace RBMCampaign
{
    /// <summary>
    /// What a workshop pays the townspeople who work it: a share of every sale as the salary, the standing
    /// daily overhead, and the bankruptcy that follows when the overhead cannot be paid.
    ///
    /// Vanilla splits the daily step across three methods (<c>HandleDailyExpense</c> dispatching to
    /// <c>HandlePlayerWorkshopExpense</c> or <c>HandleNotableWorkshopExpense</c>) and charges only a flat
    /// <c>DailyExpense</c> -- an overhead that is blind to whether any work was done. It then destroys the
    /// money: the one number in the game meant to represent workshop labour was paid to nobody.
    ///
    /// RBM owns the daily step at one seam and adds the salary at the sale itself. Every denar of both is
    /// credited to the townspeople who did the work, which makes workshops a standing channel from owners
    /// and capital into the market rather than a hole in it. A brewery's costs are its brewers, and its
    /// brewers drink in the same town.
    /// </summary>
    /// <remarks>
    /// This replaces what <c>WorkshopPurse</c> used to do with a payroll postfix and two before/after
    /// capture patches that inferred the outlay from a diff across a vanilla call it did not control.
    /// Here every debit is written by RBM and pairs with its citizen credit by construction -- nothing
    /// minted, nothing destroyed.
    ///
    /// The prefix is inert when <c>rbmCampaignEnabled</c> is off (it returns true and vanilla runs),
    /// which matters because <c>ApplyHarmonyPatches</c> can leave it applied across a toggle.
    /// </remarks>
    public static class RBMWorkshopExpense
    {
        /// <summary>
        /// The salary is a share of every sale: whenever a named workshop's capital is paid for a finished
        /// good, this fraction of the payment goes to the townspeople who made it instead of into the till.
        /// </summary>
        /// <remarks>
        /// A named shop pays its hands and the artisans do not, and the asymmetry is the whole distinction
        /// between them: a brewery has an owner, and the hands who work it are not him. The artisans have
        /// no owner to be separate from -- see <c>RBMWorkshopCycle.SettlesInGold</c>.
        ///
        /// The share is <c>55% - 5% per 48,000 of equipment_cost</c>: a pottery at 48,000 pays 50%, a
        /// brewery at 144,000 pays 40%, a smithy at 240,000 pays 30%. A dearer shop is a bigger stake for
        /// its owner, so more of each sale is his. Floored so no shop type ever pays nothing.
        /// </remarks>
        public const float SalaryShareBase = 0.55f;
        public const float SalaryShareStepReduction = 0.05f;
        public const float SalaryEquipmentStep = 48000f;
        public const float SalaryShareFloor = 0.10f;

        /// <summary>
        /// Below this capital a shop keeps the whole of every sale. It sits between the low-capital mark
        /// and the founding capital: a struggling shop rebuilds its till before it pays anyone.
        /// </summary>
        public const int SalaryCapitalThreshold = 40000;

        /// <summary>The fraction of a sale that goes to the hands, by workshop type.</summary>
        public static float SalaryShare(Workshop shop)
        {
            float equipment = (shop != null && shop.WorkshopType != null) ? shop.WorkshopType.EquipmentCost : 0f;
            if (equipment < 0f)
            {
                equipment = 0f;
            }
            float share = SalaryShareBase - SalaryShareStepReduction * equipment / SalaryEquipmentStep;
            return (share < SalaryShareFloor) ? SalaryShareFloor : share;
        }

        /// <summary>
        /// What the hands take out of one payment to the shop. Zero for the artisans and for a shop whose
        /// capital, before the payment, is not above <see cref="SalaryCapitalThreshold"/>.
        /// </summary>
        public static int SalaryFromPayout(Workshop shop, int payout)
        {
            if (payout <= 0 || shop == null || shop.WorkshopType == null || shop.WorkshopType.IsHidden)
            {
                return 0;
            }
            if (shop.Capital <= SalaryCapitalThreshold)
            {
                return 0;
            }
            int salary = (int)(payout * SalaryShare(shop));
            return (salary > payout) ? payout : salary;
        }

        /// <summary>
        /// Takes the hands' share out of a payment the shop has just received and credits it to the town's
        /// citizens. Called by the output leg right after the payout lands in capital, so the SHOPS
        /// breakdown still sees the whole sale come in and the payroll go out.
        /// </summary>
        public static void PaySalary(Workshop shop, int payout)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            // Capital is read AFTER the payout has landed, so the threshold is tested against the till
            // that will actually be paying.
            int salary = SalaryFromPayout(shop, payout);
            if (salary <= 0)
            {
                return;
            }
            WorkshopPurse.SetContext(WorkshopPurse.Payroll);
            shop.ChangeGold(-salary);
            WorkshopPurse.ClearContext();

            Settlement settlement = shop.Settlement;
            if (settlement != null && SettlementWealth.HasCitizenPurse(settlement))
            {
                // Untaxed, unlike the trades on either side of it. The market fee is charged on goods
                // changing hands over a counter, and a wage is not that -- it is a man being paid, and the
                // town takes its penny later when he spends it.
                SettlementWealth.CreditCitizens(settlement, salary, SettlementWealth.Source.WorkshopWages);
            }
            int running;
            _salaryToday.TryGetValue(shop, out running);
            _salaryToday[shop] = running + salary;
        }

        // Cycles each shop actually completed today, counted off the two methods that run one. Consumed
        // by the expense step, so an entry never outlives the day that made it.
        private static readonly Dictionary<Workshop, int> _cyclesToday = new Dictionary<Workshop, int>();

        // Salary each shop has paid out of today's sales, likewise consumed by the expense step.
        private static readonly Dictionary<Workshop, int> _salaryToday = new Dictionary<Workshop, int>();

        /// <summary>What a named shop paid its hands on its most recent production day.</summary>
        private struct Payroll
        {
            public int Cycles;
            public int Paid;
        }

        // Kept whether or not the log is on, because the clan-screen workshop card reads it (see
        // WorkshopCardPayrollLine). Overwritten each day the shop is billed, so it always describes the
        // last day of work rather than accumulating.
        private static readonly Dictionary<Workshop, Payroll> _lastPayroll = new Dictionary<Workshop, Payroll>();

        // Vanilla's private bankruptcy handover, called rather than reimplemented so
        // ChangeOwnerOfWorkshopAction.ApplyByBankruptcy and DecideBestWorkshopType stay untouched.
        private static MethodInfo _bankruptcy;

        /// <summary>Drops the previous session's tallies. Session-only figures, so a session hook is enough.</summary>
        public static void Reset()
        {
            _cyclesToday.Clear();
            _salaryToday.Clear();
            _lastPayroll.Clear();
        }

        /// <summary>
        /// Reads what a shop paid its hands the last day it worked. False if it has not been billed since
        /// the session began.
        /// </summary>
        public static bool TryGetLastPayroll(Workshop shop, out int cycles, out int paid)
        {
            Payroll last;
            if (shop != null && _lastPayroll.TryGetValue(shop, out last))
            {
                cycles = last.Cycles;
                paid = last.Paid;
                return true;
            }
            cycles = 0;
            paid = 0;
            return false;
        }

        /// <summary>
        /// Takes a shop's day of batches off the counter and hands the figure to the log.
        /// </summary>
        /// <remarks>
        /// Taking the tally is never a no-op, even for a shop that pays nothing: a bench whose count is
        /// never taken carries it into tomorrow and reports the whole campaign as one enormous day.
        /// </remarks>
        private static int TakeCycles(Workshop shop)
        {
            int cycles;
            if (shop == null || !_cyclesToday.TryGetValue(shop, out cycles))
            {
                return 0;
            }
            _cyclesToday.Remove(shop);
            WorkshopPurse.RecordCycles(shop, cycles);
            return cycles;
        }

        private static int TakeSalary(Workshop shop)
        {
            int salary;
            if (shop == null || !_salaryToday.TryGetValue(shop, out salary))
            {
                return 0;
            }
            _salaryToday.Remove(shop);
            return salary;
        }

        private static void CountCycle(Workshop workshop, bool produced)
        {
            if (!produced || workshop == null || !RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            int running;
            _cyclesToday.TryGetValue(workshop, out running);
            _cyclesToday[workshop] = running + 1;
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "TickOneProductionCycleForNotableWorkshop")]
        private static class NotableCycleCounter
        {
            private static void Postfix(Workshop workshop, bool __result) { CountCycle(workshop, __result); }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "TickOneProductionCycleForPlayerWorkshop")]
        private static class PlayerCycleCounter
        {
            private static void Postfix(Workshop workshop, bool __result) { CountCycle(workshop, __result); }
        }

        /// <summary>
        /// The daily overhead for one shop, and the bankruptcy that follows when it cannot be paid.
        /// </summary>
        /// <remarks>
        /// The salary is no longer part of this bill: it came off each sale as it was made
        /// (<see cref="PaySalary"/>), so a shop can never be bankrupted by its own good day. What is
        /// billed here is the standing overhead alone, on vanilla's ladder (WCB:729-748):
        ///
        /// <list type="number">
        /// <item>capital, while the shop is above <c>CapitalLowLimit</c>;</item>
        /// <item>the player owner's own gold -- vanilla's signal that an undercapitalised shop is billed
        /// to its owner, which is also what the clan finance expense line reports;</item>
        /// <item>capital again, if it covers the bill;</item>
        /// <item>bankruptcy, charging nothing: vanilla hands the shop to a new owner instead, and the
        /// capital goes with it.</item>
        /// </list>
        ///
        /// The overhead is credited to the townspeople like the salary, so nothing is destroyed. The day's
        /// salary tally is taken here too, so the card and the SHOPWAGE line report a whole day of sales.
        ///
        /// The artisans pay nothing at all -- vanilla exempts hidden workshops from the whole method, and
        /// under RBM they do not move gold in either direction (<c>RBMWorkshopCycle.SettlesInGold</c>).
        /// </remarks>
        private static void Run(WorkshopsCampaignBehavior behavior, Workshop shop)
        {
            if (shop == null || shop.WorkshopType == null)
            {
                return;
            }
            if (shop.WorkshopType.IsHidden)
            {
                TakeCycles(shop);
                TakeSalary(shop);
                return;
            }

            int cycles = TakeCycles(shop);
            int salary = TakeSalary(shop);
            _lastPayroll[shop] = new Payroll { Cycles = cycles, Paid = salary };

            Settlement settlement = shop.Settlement;
            WorkshopPurse.RecordWage(settlement, cycles, salary);

            int overhead = (Campaign.Current != null) ? Campaign.Current.Models.WorkshopModel.DailyExpense : 0;
            if (overhead <= 0)
            {
                return;
            }
            int lowLimit = (Campaign.Current != null)
                ? Campaign.Current.Models.WorkshopModel.CapitalLowLimit
                : 0;

            if (shop.Capital > lowLimit && shop.Capital >= overhead)
            {
                WorkshopPurse.SetContext(WorkshopPurse.Overhead);
                shop.ChangeGold(-overhead);
                WorkshopPurse.ClearContext();
            }
            else if (shop.Owner != null && shop.Owner == Hero.MainHero && shop.Owner.Gold >= overhead)
            {
                // Mirrors vanilla's own write (WCB:738): the owner's pocket, not a GiveGoldAction, so no
                // clan-income event fires for what is an expense.
                shop.Owner.Gold -= overhead;
            }
            else if (shop.Capital >= overhead)
            {
                WorkshopPurse.SetContext(WorkshopPurse.Overhead);
                shop.ChangeGold(-overhead);
                WorkshopPurse.ClearContext();
            }
            else
            {
                Bankrupt(behavior, shop);
                return;
            }

            if (settlement != null && SettlementWealth.HasCitizenPurse(settlement))
            {
                // Rent, tools and the licence are bought from townspeople too.
                SettlementWealth.CreditCitizens(settlement, overhead, SettlementWealth.Source.WorkshopWages);
            }
        }

        private static void Bankrupt(WorkshopsCampaignBehavior behavior, Workshop shop)
        {
            if (behavior == null)
            {
                return;
            }
            if (_bankruptcy == null)
            {
                _bankruptcy = AccessTools.Method(typeof(WorkshopsCampaignBehavior), "ChangeWorkshopOwnerByBankruptcy");
            }
            if (_bankruptcy != null)
            {
                _bankruptcy.Invoke(behavior, new object[] { shop });
            }
        }

        /// <summary>
        /// One seam in place of vanilla's three. <c>DailyTickTown</c> runs a shop's production and then
        /// calls this, so the day's batches are counted by the time the wage is worked out.
        /// </summary>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "HandleDailyExpense")]
        private static class DailyExpensePatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(WorkshopsCampaignBehavior __instance, Workshop shop)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return true;
                }
                Run(__instance, shop);
                return false;
            }
        }
    }
}
