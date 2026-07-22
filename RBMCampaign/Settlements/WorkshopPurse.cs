using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace RBMCampaign
{
    /// <summary>
    /// Brings a town's workshops into the ledger, and pays their hands.
    ///
    /// A workshop keeps its own purse. That is right and is left alone: a brewery's working capital is
    /// not the same money as the market's float, and folding the two together would make the town's
    /// balance move every time a shop bought a sack of grain. But <c>Workshop</c> is a
    /// <c>SettlementArea</c> rather than a <c>SettlementComponent</c>, so it slipped past the
    /// <see cref="SettlementGoldFunnel"/> entirely -- which left the town's largest internal economy
    /// half-visible. Every town-workshop trade showed only the town's side.
    ///
    /// That matters more than a gap in a log. Workshops are what turn grain into beer, clay into
    /// pottery, olives into oil and grape into wine -- which is to say they are the source of nearly
    /// every good the DEMAND line reports as unmet. A town that cannot supply its own basket is failing
    /// somewhere inside this purse, and until now there was no way to see where.
    ///
    /// So the moves are recorded as a third ledger, reported per settlement per day as SHOPS. The money
    /// is NOT redirected: a workshop's purse stays its own.
    /// </summary>
    /// <remarks>
    /// One genuine leak was found and closed while doing it. <c>HandlePlayerWorkshopExpense</c> and its
    /// notable-owned twin take the shop's running costs out of its capital -- or out of the owner's own
    /// gold when the capital is too low -- and hand them to nobody at all. Those costs are wages for the
    /// people who work the shop, and those people are townspeople. So the expense is now credited to
    /// citizen wealth, which makes the workshops a standing channel from owners and capital into the
    /// market rather than a hole in it.
    /// </remarks>
    public static class WorkshopPurse
    {
        private const string Output = "output";
        private const string Inputs = "inputs";
        private const string Wages = "wages";
        private const string Owner = "owner";
        private const string Other = "other";

        // What the shop was doing when it moved money, or null outside a known operation. Every write is
        // reached from one of the patched methods below; anything else lands under "other", which is
        // itself worth seeing.
        private static string _context;

        // A day of workshop movements per settlement, by source. Diagnostics only.
        private static readonly Dictionary<Settlement, Dictionary<string, int>> _ledger =
            new Dictionary<Settlement, Dictionary<string, int>>();

        /// <summary>Drops the previous session's tallies. Pure diagnostics, so a session hook is enough.</summary>
        public static void Reset()
        {
            _ledger.Clear();
            _context = null;
        }

        /// <summary>
        /// Records every movement of a workshop's purse against the settlement it stands in.
        /// </summary>
        [HarmonyPatch(typeof(Workshop), "ChangeGold")]
        private static class RecordPatch
        {
            private static void Prefix(Workshop __instance, int goldChange)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !EconomyLog.IsEnabled || goldChange == 0)
                {
                    return;
                }
                Settlement settlement = __instance.Settlement;
                if (settlement == null)
                {
                    return;
                }

                Dictionary<string, int> bySource;
                if (!_ledger.TryGetValue(settlement, out bySource))
                {
                    bySource = new Dictionary<string, int>();
                    _ledger[settlement] = bySource;
                }
                string source = _context ?? Other;
                int running;
                bySource.TryGetValue(source, out running);
                bySource[source] = running + goldChange;
            }
        }

        // Context markers. Each names what the shop is doing for the duration of one vanilla method, and
        // each releases from a FINALIZER so a throwing production cycle cannot leave the label standing
        // and mislabel every workshop move after it.

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "ProduceAnOutputToTown")]
        private static class OutputContext
        {
            private static void Prefix() { _context = Output; }
            private static void Finalizer() { _context = null; }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "ConsumeInputFromTownMarket")]
        private static class InputContext
        {
            private static void Prefix() { _context = Inputs; }
            private static void Finalizer() { _context = null; }
        }

        // There is deliberately NO marker for DefaultClanFinanceModel.CalculateHeroIncomeFromWorkshops,
        // which is where an owner withdraws his shops' profit. Adding one crashed new-game creation: the
        // type initializer for DefaultClanFinanceModel reads
        // Game.Current.GameTextManager.FindText(...) in a static field, so it throws a
        // NullReferenceException if it is forced to run before Game.Current exists -- and .NET caches a
        // failed type initialization permanently, so every later touch of the class rethrows. It
        // surfaced as a TypeInitializationException while building the map screen, long after the real
        // damage was done.
        //
        // The class is patched elsewhere in RBM (GarrisonUpkeep, MaintenanceFinanceLine) without
        // trouble, so what exactly differed here is not established -- and this was only ever a log
        // LABEL. Owner withdrawals now fall through to "other" on the SHOPS line, which costs nothing
        // worth a crash. See the settlement-tooltip note for the same cctor trap in another class.

        /// <summary>
        /// Pays a workshop's running costs to the townspeople who do the work.
        /// </summary>
        /// <remarks>
        /// Vanilla takes the expense from the shop's capital, or from the owner's own gold when the
        /// capital has fallen too low, and destroys it either way. Both are read here as a difference
        /// across the call rather than from <c>Expense</c>, because which branch ran -- and whether it
        /// ran at all, since a shop too poor for either goes bankrupt instead -- decides how much
        /// actually moved.
        ///
        /// Crediting the market with it is the conserved reading and a sensible one: a brewery's costs
        /// are its brewers, and its brewers drink in the same town.
        /// </remarks>
        private static void PayWages(Workshop shop, int spent)
        {
            if (spent <= 0 || shop == null)
            {
                return;
            }
            Settlement settlement = shop.Settlement;
            if (settlement == null || !SettlementWealth.HasCitizenPurse(settlement))
            {
                return;
            }
            SettlementWealth.CreditCitizens(settlement, spent, SettlementWealth.Source.WorkshopWages);
            TradeTariff.Levy(settlement, spent);
        }

        /// <summary>Photographs both purses an expense can come out of, so the real outlay is known.</summary>
        private static void CaptureBefore(Workshop shop, out int[] state)
        {
            _context = Wages;
            // A workshop's purse is called Capital -- ChangeGold writes it -- and the owner's own gold is
            // the fallback the expense comes out of when the capital is too low, so both are captured.
            state = (shop != null && RBMConfig.RBMConfig.rbmCampaignEnabled)
                ? new int[] { shop.Capital, (shop.Owner != null) ? shop.Owner.Gold : 0 }
                : null;
        }

        private static void SettleAfter(Workshop shop, int[] state)
        {
            _context = null;
            if (state == null || shop == null)
            {
                return;
            }
            int fromCapital = state[0] - shop.Capital;
            int fromOwner = state[1] - ((shop.Owner != null) ? shop.Owner.Gold : 0);
            int spent = ((fromCapital > 0) ? fromCapital : 0) + ((fromOwner > 0) ? fromOwner : 0);
            PayWages(shop, spent);
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "HandlePlayerWorkshopExpense")]
        private static class PlayerExpensePatch
        {
            private static void Prefix(Workshop shop, out int[] __state)
            {
                CaptureBefore(shop, out __state);
            }

            private static void Postfix(Workshop shop, int[] __state)
            {
                SettleAfter(shop, __state);
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "HandleNotableWorkshopExpense")]
        private static class NotableExpensePatch
        {
            private static void Prefix(Workshop shop, out int[] __state)
            {
                CaptureBefore(shop, out __state);
            }

            private static void Postfix(Workshop shop, int[] __state)
            {
                SettleAfter(shop, __state);
            }
        }

        /// <summary>
        /// Writes a settlement's day of workshop trade and clears it.
        /// </summary>
        /// <remarks>
        /// The number to watch is <c>output</c> against <c>inputs</c>. A town whose shops buy little and
        /// produce little is a town that will show beer, oil and pottery on the DEMAND line's unmet list
        /// however much grain its villages send -- the shortage is here, between the market and the
        /// shops, not out in the countryside.
        /// </remarks>
        public static void FlushDaily(Settlement settlement)
        {
            Dictionary<string, int> bySource;
            if (settlement == null || !_ledger.TryGetValue(settlement, out bySource))
            {
                return;
            }
            _ledger.Remove(settlement);

            if (!EconomyLog.IsEnabled || bySource.Count == 0)
            {
                return;
            }

            int inTotal = 0;
            int outTotal = 0;
            StringBuilder breakdown = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in bySource)
            {
                if (pair.Value >= 0)
                {
                    inTotal += pair.Value;
                }
                else
                {
                    outTotal -= pair.Value;
                }
                breakdown.Append("  ").Append(pair.Key).Append(" ").Append(pair.Value).Append("d");
            }

            int capital = 0;
            Town town = settlement.Town;
            if (town != null && town.Workshops != null)
            {
                foreach (Workshop shop in town.Workshops)
                {
                    if (shop != null)
                    {
                        capital += shop.Capital;
                    }
                }
            }

            EconomyLog.Log("SHOPS", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                "in " + inTotal + "d  ·  out " + outTotal + "d  ·  net " + (inTotal - outTotal) + "d"
                + "  ·  capital now " + capital + "d  ·" + breakdown);
        }
    }
}
