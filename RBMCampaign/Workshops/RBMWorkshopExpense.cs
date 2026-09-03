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
    /// A workshop's daily bill: its standing overhead, the wage for the batches it actually ran, and the
    /// bankruptcy that follows when it can pay neither.
    ///
    /// Vanilla splits this across three methods (<c>HandleDailyExpense</c> dispatching to
    /// <c>HandlePlayerWorkshopExpense</c> or <c>HandleNotableWorkshopExpense</c>) and charges only a flat
    /// <c>DailyExpense</c> -- an overhead that is blind to whether any work was done. It then destroys the
    /// money: the one number in the game meant to represent workshop labour was paid to nobody.
    ///
    /// RBM owns the whole step at one seam. The bill is overhead plus a per-batch payroll, and every denar
    /// of it is credited to the townspeople who did the work, which makes workshops a standing channel
    /// from owners and capital into the market rather than a hole in it. A brewery's costs are its
    /// brewers, and its brewers drink in the same town.
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
        /// What a named workshop pays its hands for one production cycle.
        /// </summary>
        /// <remarks>
        /// Vanilla's wage bill is <c>DailyExpense</c>, a flat sum a shop pays whether it ran fifty cycles
        /// or none. This is per BATCH, so it scales with the work actually done -- one batch a day costs
        /// seventy-five, ten batches seven hundred and fifty.
        ///
        /// A named shop pays a wage and the artisans do not, and the asymmetry is the whole distinction
        /// between them: a brewery has an owner, and the hands who work it are not him. The artisans have
        /// no owner to be separate from -- see <c>RBMWorkshopCycle.SettlesInGold</c>.
        /// </remarks>
        public const int WagePerCycle = 75;

        // Cycles each shop actually completed today, counted off the two methods that run one. Consumed
        // by the expense step, so an entry never outlives the day that made it.
        private static readonly Dictionary<Workshop, int> _cyclesToday = new Dictionary<Workshop, int>();

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
        /// The whole daily bill for one shop.
        /// </summary>
        /// <remarks>
        /// The ladder is vanilla's (WCB:729-748), with the bill widened from overhead alone to overhead
        /// plus payroll and one rung added before bankruptcy:
        ///
        /// <list type="number">
        /// <item>capital, while the shop is above <c>CapitalLowLimit</c>;</item>
        /// <item>the player owner's own gold -- vanilla's signal that an undercapitalised shop is billed
        /// to its owner, which is also what the clan finance expense line reports;</item>
        /// <item>capital again, if it covers the bill;</item>
        /// <item>whatever capital there is, if that at least covers the standing overhead. A busy shop
        /// must never be bankrupted BY ITS OWN GOOD DAY -- the payroll is a consequence of work done, and
        /// work done is the last thing that should close a business;</item>
        /// <item>bankruptcy, charging nothing: vanilla hands the shop to a new owner instead, and the
        /// capital goes with it.</item>
        /// </list>
        ///
        /// A partial payment is attributed to the overhead FIRST and only the remainder to the wage. The
        /// overhead is a standing obligation (rent, tools, the licence) that falls due whether or not
        /// anyone worked, so it has the prior claim on a thin till; the payroll is what gets shorted, and
        /// the SHOPWAGE line's per-batch figure dipping below the declared rate is exactly the signal that
        /// a shop ran out mid-payroll.
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
                return;
            }

            int cycles = TakeCycles(shop);
            int wage = cycles * WagePerCycle;
            int overhead = (Campaign.Current != null) ? Campaign.Current.Models.WorkshopModel.DailyExpense : 0;
            int bill = wage + overhead;

            Settlement settlement = shop.Settlement;
            int lowLimit = (Campaign.Current != null)
                ? Campaign.Current.Models.WorkshopModel.CapitalLowLimit
                : 0;

            int fromCapital = 0;
            int fromOwner = 0;

            if (bill <= 0)
            {
                _lastPayroll[shop] = new Payroll { Cycles = cycles, Paid = 0 };
                return;
            }

            if (shop.Capital > lowLimit && shop.Capital >= bill)
            {
                fromCapital = bill;
            }
            else if (shop.Owner != null && shop.Owner == Hero.MainHero && shop.Owner.Gold >= bill)
            {
                fromOwner = bill;
            }
            else if (shop.Capital >= bill)
            {
                fromCapital = bill;
            }
            else if (shop.Capital >= overhead)
            {
                fromCapital = shop.Capital;
            }
            else
            {
                _lastPayroll[shop] = new Payroll { Cycles = cycles, Paid = 0 };
                Bankrupt(behavior, shop);
                return;
            }

            int paid = fromCapital + fromOwner;

            // Overhead has the prior claim; the wage takes what is left.
            int overheadPaid = (paid < overhead) ? paid : overhead;
            int wagePaid = paid - overheadPaid;

            if (fromCapital > 0)
            {
                // Split into two debits so the SHOPS breakdown keeps its overhead and payroll buckets.
                int capitalOverhead = (fromCapital < overheadPaid) ? fromCapital : overheadPaid;
                if (capitalOverhead > 0)
                {
                    WorkshopPurse.SetContext(WorkshopPurse.Overhead);
                    shop.ChangeGold(-capitalOverhead);
                    WorkshopPurse.ClearContext();
                }
                int capitalWage = fromCapital - capitalOverhead;
                if (capitalWage > 0)
                {
                    WorkshopPurse.SetContext(WorkshopPurse.Payroll);
                    shop.ChangeGold(-capitalWage);
                    WorkshopPurse.ClearContext();
                }
            }
            if (fromOwner > 0 && shop.Owner != null)
            {
                // Mirrors vanilla's own write (WCB:738): the owner's pocket, not a GiveGoldAction, so no
                // clan-income event fires for what is an expense.
                shop.Owner.Gold -= fromOwner;
            }

            _lastPayroll[shop] = new Payroll { Cycles = cycles, Paid = wagePaid };

            if (paid > 0 && settlement != null && SettlementWealth.HasCitizenPurse(settlement))
            {
                // Untaxed, unlike the trades on either side of it. The market fee is charged on goods
                // changing hands over a counter, and a wage is not that -- it is a man being paid, and the
                // town takes its penny later when he spends it.
                SettlementWealth.CreditCitizens(settlement, paid, SettlementWealth.Source.WorkshopWages);
            }
            WorkshopPurse.RecordWage(settlement, cycles, wagePaid);
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
