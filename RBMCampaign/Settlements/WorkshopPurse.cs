using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

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

        // Two separate things used to share one "wages" label, which made the SHOPS line unreadable: the
        // standing overhead vanilla charges whether or not a shop worked, and the per-batch payroll a
        // named shop pays its hands. (A third, "craftwage", was the artisans' -- they no longer move gold
        // at all, so there is nothing left to label. See IsCitizenLabour.)
        private const string Overhead = "overhead";
        private const string Payroll = "payroll";

        private const string Owner = "owner";
        private const string Other = "other";

        // What the shop was doing when it moved money, or null outside a known operation. Every write is
        // reached from one of the patched methods below; anything else lands under "other", which is
        // itself worth seeing.
        private static string _context;

        // A day of movements per SHOP, by source. Kept per shop rather than per settlement so a town
        // whose figures look wrong can be read down to the bench that made them wrong; the settlement
        // totals are added back up at flush. Diagnostics only.
        private static readonly Dictionary<Workshop, Dictionary<string, int>> _ledger =
            new Dictionary<Workshop, Dictionary<string, int>>();

        /// <summary>Drops the previous session's tallies. Pure diagnostics, so a session hook is enough.</summary>
        public static void Reset()
        {
            _ledger.Clear();
            _cyclesToday.Clear();
            _cyclesLogged.Clear();
            _wageDay.Clear();
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
                if (__instance.Settlement == null)
                {
                    return;
                }

                Dictionary<string, int> bySource;
                if (!_ledger.TryGetValue(__instance, out bySource))
                {
                    bySource = new Dictionary<string, int>();
                    _ledger[__instance] = bySource;
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

        // Both markers now carry the citizen-labour work as well, because it happens in the same window:
        // the artisans' recipes are forced to settle in gold, and what they pay for their materials and
        // take for their work passes the market counter like any other trade, fee and all.

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "ProduceAnOutputToTown")]
        private static class OutputContext
        {
            /// <remarks>
            /// Nothing is charged for the finished good and nothing is levied on it. A man setting his own
            /// work on his own stall has not crossed a counter, and the fee already came off the materials
            /// that made it -- taking it twice would tax the same goods on the way in and on the way out.
            ///
            /// This also disposes of vanilla's <c>min(1000, price)</c> per-cycle ceiling, which used to
            /// need correcting after the fact: it only ever mattered because the number was being paid to
            /// somebody. Now that nobody is paid, an armour and a barrel of beer settle alike, at nothing.
            /// </remarks>
            private static void Prefix(Workshop workshop, ref bool effectCapital)
            {
                _context = Output;
                if (IsCitizenLabour(workshop))
                {
                    effectCapital = false;
                }
            }

            private static void Finalizer()
            {
                _context = null;
            }
        }

        /// <summary>
        /// The artisans draw their materials off the shelf without paying for them, and pay the market fee.
        /// </summary>
        /// <remarks>
        /// The goods are the townspeople's already, so no price changes hands -- but the stall is the
        /// town's, and the town takes its penny for the counter whoever is standing at it. That is the one
        /// leg of the artisans' day where anything still moves, and it moves the way every other trade in
        /// the ledger does: out of citizen wealth and into the treasury, at
        /// <see cref="TradeTariff.TariffRate"/>.
        ///
        /// Levied on the whole draw, not on one unit. Vanilla prices ONE item and removes
        /// <c>productionInputCount</c> of them -- harmless in vanilla, where every recipe takes one of
        /// everything, but RBM's recipes take up to twenty ingots at a time. The fee is on what left the
        /// shelf.
        ///
        /// The price is read in the prefix because a recipe that clears the last of a good leaves nothing
        /// to price afterwards; the walk matches <c>ConsumeInputFromTownMarket</c>'s own <c>FindIndex</c>,
        /// so it prices the same item vanilla is about to take.
        /// </remarks>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "ConsumeInputFromTownMarket")]
        private static class CitizenLabourTariff
        {
            private static void Prefix(ItemCategory productionInput, int productionInputCount, Town town,
                Workshop workshop, ref bool effectCapital, out int[] __state)
            {
                _context = Inputs;
                __state = null;
                if (!IsCitizenLabour(workshop))
                {
                    return;
                }

                effectCapital = false;

                if (town == null || town.Owner == null || productionInput == null)
                {
                    return;
                }
                ItemRoster roster = town.Owner.ItemRoster;
                for (int i = 0; i < roster.Count; i++)
                {
                    ItemObject item = roster.GetItemAtIndex(i);
                    if (item != null && item.ItemCategory == productionInput)
                    {
                        __state = new int[] { town.GetItemPrice(item) * productionInputCount };
                        break;
                    }
                }
            }

            private static void Postfix(Town town, int[] __state)
            {
                if (__state == null || !Campaign.Current.GameStarted || town == null)
                {
                    return;
                }
                TradeTariff.Levy(town.Settlement, __state[0]);
            }

            private static void Finalizer()
            {
                _context = null;
            }
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
        /// The artisans: the town's own craftsmen, working their own materials on their own account. They
        /// move no gold at all -- only the market fee on what they take off the shelf.
        /// </summary>
        /// <remarks>
        /// Every town has a hidden <c>artisans</c> shop in slot 0, and it is not a business in the sense
        /// the other twelve types are. It is the townspeople themselves: the butcher jointing the cow, the
        /// smith at his tier-1 blades.
        ///
        /// This went the long way round. The bench was first made to trade for real -- citizens paying the
        /// shop for its output, the shop paying a wage back for the labour -- on the reasoning that a purse
        /// which neither takes nor pays is a trade that never reaches the man who did the work. Measured
        /// over fourteen logged days, that circuit turned out to be almost entirely self-cancelling: the
        /// wage credit is the output debit coming home, and the whole apparatus resolved to the market fee
        /// plus whatever the working float happened to be doing that day. It bought a great deal of
        /// machinery for a residue.
        ///
        /// It also did real harm, because the float is a claim on the town's money. In the poorest towns
        /// it was measured holding MORE than the townspeople had between them -- and since citizen wealth
        /// gated production, the float was holding the very money that would have bought the goods it was
        /// waiting to sell. Those towns locked, and they were exactly the ones with the worst output.
        ///
        /// So the circuit is gone. A man working his own stock does not buy from himself, pay himself, or
        /// keep a float against his own wages, and the ledger should not pretend otherwise. Materials come
        /// off the shelf and finished goods go back onto it; what the day added is a better shelf, not a
        /// bigger pile of denars. The town's gold rises when an OUTSIDER buys the work -- a caravan, a
        /// passing lord, the player -- which is the only point at which value actually leaves the town, and
        /// which the trade routing already handles.
        ///
        /// One thing still moves, and it is the exception that proves the rule: the market fee on the
        /// materials drawn. The stall a man takes his iron from is the town's, not his, and the town takes
        /// its penny for the counter whoever is standing at it -- see <see cref="CitizenLabourTariff"/>.
        ///
        /// The gate on production is deliberately not replaced. It was never the binding constraint: the
        /// SHOPBLOCK lines put the refusals overwhelmingly on missing inputs and on vanilla's margin floor,
        /// with shop-broke a rarity.
        /// </remarks>
        private static bool IsCitizenLabour(Workshop workshop)
        {
            return RBMConfig.RBMConfig.rbmCampaignEnabled
                && workshop != null
                && workshop.WorkshopType != null
                && workshop.WorkshopType.IsHidden;
        }

        // effectCapital is vanilla's own switch for "this recipe settles in gold", and it is a plain
        // parameter on every method that reads it, so clearing it by ref is exact -- and it is the whole
        // of the change. Vanilla's own methods still move the ITEMS either way: ConsumeInputFromTownMarket
        // takes them off the roster and ProduceAnOutputToTown adds them, and only the ChangeGold pair is
        // behind the flag. So the bench goes on working and only the denars stop.
        //
        // Cleared rather than merely left alone, because vanilla SETS it for any recipe whose goods are
        // all trade goods -- grain to beer, grape to wine -- and those are the artisans' commonest work.
        // Leaving it would settle half the bench in gold and half of it not.

        /// <summary>
        /// Excuses the artisans the solvency test, there being nothing for them to be short of.
        /// </summary>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanNotableWorkshopProduceThisCycle")]
        private static class CitizenLabourSettlesInKind
        {
            private static void Prefix(Workshop workshop, ref bool effectCapital)
            {
                if (IsCitizenLabour(workshop))
                {
                    effectCapital = false;
                }
            }
        }

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
        ///
        /// Untaxed, unlike the trades on either side of it. The market fee is charged on goods changing
        /// hands over a counter, and a wage is not that -- it is a man being paid, and the town takes its
        /// penny later when he spends it.
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
        }

        /// <summary>Photographs both purses an expense can come out of, so the real outlay is known.</summary>
        private static void CaptureBefore(Workshop shop, out int[] state)
        {
            _context = Overhead;
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

        /// <summary>
        /// What a named workshop pays its hands for one production cycle.
        /// </summary>
        /// <remarks>
        /// Vanilla's wage bill is <c>DailyExpense</c>, a flat hundred denars a day that a shop pays
        /// whether it ran fifty cycles or none: an overhead, not a wage. So the one number in the game
        /// that was supposed to represent workshop labour was blind to whether any labour happened.
        ///
        /// This is per CYCLE, so it scales with the work actually done -- one batch a day costs
        /// seventy-five, ten batches seven hundred and fifty.
        ///
        /// A named shop pays a wage and the artisans do not, and the asymmetry is the whole distinction
        /// between them: a brewery has an owner, and the hands who work it are not him. The artisans have
        /// no owner to be separate from -- see <see cref="IsCitizenLabour"/>.
        ///
        /// Left alongside vanilla's flat expense rather than replacing it, because the two now mean
        /// different things -- the hundred is the shop's standing overhead, this is its payroll.
        /// </remarks>
        private const int WorkshopWagePerCycle = 75;

        // Cycles each shop actually completed today, counted off the two methods that run one. Consumed
        // by whichever payroll pays that shop, so an entry never outlives the day that made it.
        private static readonly Dictionary<Workshop, int> _cyclesToday = new Dictionary<Workshop, int>();

        /// <summary>A settlement's day of workshop payroll.</summary>
        private struct WageDay
        {
            public int ShopCycles;
            public int ShopPaid;
        }

        private static readonly Dictionary<Settlement, WageDay> _wageDay = new Dictionary<Settlement, WageDay>();

        // The same counts again, kept only until the day is written. TakeCycles consumes the working
        // tally as soon as a payroll is worked out, which is well before the log runs.
        private static readonly Dictionary<Workshop, int> _cyclesLogged = new Dictionary<Workshop, int>();

        private static int TakeCycles(Workshop shop)
        {
            int cycles;
            if (!_cyclesToday.TryGetValue(shop, out cycles))
            {
                return 0;
            }
            _cyclesToday.Remove(shop);
            if (EconomyLog.IsEnabled)
            {
                _cyclesLogged[shop] = cycles;
            }
            return cycles;
        }

        private static void RecordWage(Settlement settlement, int cycles, int paid)
        {
            if (!EconomyLog.IsEnabled || settlement == null)
            {
                return;
            }
            WageDay day;
            _wageDay.TryGetValue(settlement, out day);
            day.ShopCycles += cycles;
            day.ShopPaid += paid;
            _wageDay[settlement] = day;
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
        /// Pays a named workshop's hands for the day's batches.
        /// </summary>
        /// <remarks>
        /// Clamped to the capital actually in the till. A shop that cannot make its payroll pays what it
        /// has and no more, rather than going to the owner's pocket or to bankruptcy -- vanilla's flat
        /// expense already owns both of those paths, and a wage that could bankrupt a shop on a good
        /// production day would be a strange way to reward it.
        /// </remarks>
        private static void PayProductionWage(Workshop shop)
        {
            if (shop == null || shop.WorkshopType == null || shop.WorkshopType.IsHidden)
            {
                return;
            }

            int cycles = TakeCycles(shop);
            Settlement settlement = shop.Settlement;
            if (cycles <= 0 || settlement == null || !SettlementWealth.HasCitizenPurse(settlement))
            {
                return;
            }

            int wage = cycles * WorkshopWagePerCycle;
            if (wage > shop.Capital)
            {
                wage = shop.Capital;
            }
            if (wage <= 0)
            {
                return;
            }

            _context = Payroll;
            shop.ChangeGold(-wage);
            _context = null;

            SettlementWealth.CreditCitizens(settlement, wage, SettlementWealth.Source.WorkshopWages);
            RecordWage(settlement, cycles, wage);
        }

        /// <summary>
        /// The payroll, at the point vanilla has finished a shop's day.
        /// </summary>
        /// <remarks>
        /// <c>DailyTickTown</c> runs a shop's production and then calls this, so the cycles are counted by
        /// the time the wage is worked out.
        ///
        /// The artisans reach this hook too, and are handled by taking their tally and paying nothing.
        /// That is not a no-op: <c>TakeCycles</c> is what clears the day's count and hands it to the log,
        /// and a bench whose count is never taken carries it into tomorrow and reports the whole campaign
        /// as one enormous day.
        /// </remarks>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "HandleDailyExpense")]
        private static class WorkshopPayrollPatch
        {
            private static void Postfix(Workshop shop)
            {
                if (shop == null || shop.WorkshopType == null)
                {
                    return;
                }
                if (shop.WorkshopType.IsHidden)
                {
                    TakeCycles(shop);
                }
                else
                {
                    PayProductionWage(shop);
                }
            }
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
            Town town = (settlement != null) ? settlement.Town : null;
            if (town == null || town.Workshops == null)
            {
                return;
            }

            string name = settlement.Name != null ? settlement.Name.ToString() : settlement.StringId;

            // Every shop is walked whether or not it traded, because the ledger only ever holds the ones
            // that moved money and a shop that moved none is exactly the interesting case. Both day
            // tallies are cleared here for the same reason: a shop that idles all day still has to have
            // yesterday's count taken off it.
            int inTotal = 0;
            int outTotal = 0;
            int capital = 0;
            Dictionary<string, int> townBySource = new Dictionary<string, int>();
            StringBuilder shopLines = new StringBuilder();

            foreach (Workshop shop in town.Workshops)
            {
                if (shop == null)
                {
                    continue;
                }
                capital += shop.Capital;

                Dictionary<string, int> bySource;
                if (!_ledger.TryGetValue(shop, out bySource))
                {
                    bySource = null;
                }
                else
                {
                    _ledger.Remove(shop);
                }

                int cycles;
                if (!_cyclesLogged.TryGetValue(shop, out cycles))
                {
                    cycles = 0;
                }
                else
                {
                    _cyclesLogged.Remove(shop);
                }

                if (!EconomyLog.IsEnabled)
                {
                    continue;
                }

                int shopIn = 0;
                int shopOut = 0;
                int wage = 0;
                StringBuilder shopBreakdown = new StringBuilder();
                if (bySource != null)
                {
                    foreach (KeyValuePair<string, int> pair in bySource)
                    {
                        if (pair.Value >= 0)
                        {
                            shopIn += pair.Value;
                        }
                        else
                        {
                            shopOut -= pair.Value;
                        }
                        if (pair.Key == Payroll)
                        {
                            wage -= pair.Value;
                        }
                        shopBreakdown.Append("  ").Append(pair.Key).Append(" ").Append(pair.Value).Append("d");

                        int running;
                        townBySource.TryGetValue(pair.Key, out running);
                        townBySource[pair.Key] = running + pair.Value;
                    }
                }
                inTotal += shopIn;
                outTotal += shopOut;

                if (bySource == null && cycles == 0)
                {
                    shopLines.Append(System.Environment.NewLine)
                        .Append("    ").Append(Describe(shop)).Append("  idle  ·  capital ")
                        .Append(shop.Capital).Append("d");
                    continue;
                }

                shopLines.Append(System.Environment.NewLine)
                    .Append("    ").Append(Describe(shop))
                    .Append("  ").Append(cycles).Append(" batches")
                    .Append("  ·  wage ").Append(wage).Append("d (").Append(Per(wage, cycles)).Append("/batch)")
                    .Append("  ·  capital ").Append(shop.Capital).Append("d  ·").Append(shopBreakdown);
            }

            if (!EconomyLog.IsEnabled || townBySource.Count == 0)
            {
                _wageDay.Remove(settlement);
                return;
            }

            StringBuilder breakdown = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in townBySource)
            {
                breakdown.Append("  ").Append(pair.Key).Append(" ").Append(pair.Value).Append("d");
            }

            EconomyLog.Log("SHOPS", name,
                "in " + inTotal + "d  ·  out " + outTotal + "d  ·  net " + (inTotal - outTotal) + "d"
                + "  ·  capital now " + capital + "d  ·" + breakdown
                + shopLines);

            FlushWages(settlement);
        }

        /// <summary>Names a shop by its type, padded so the per-shop lines read as a column.</summary>
        private static string Describe(Workshop shop)
        {
            string id = (shop.WorkshopType != null) ? shop.WorkshopType.StringId : "empty";
            return (id.Length < 18) ? id.PadRight(18) : id;
        }

        /// <summary>
        /// Writes what the town's craftsmen earned, and what each batch of work paid.
        /// </summary>
        /// <remarks>
        /// Its own line because the two payrolls answer to different rules and the SHOPS breakdown can
        /// only show what they cost, not whether either is behaving. The per-batch figures are the ones
        /// to read: a named shop should sit exactly on its declared rate unless a shop somewhere ran out
        /// of capital mid-payroll, and the artisans' figure is a residual that will move with prices,
        /// with which of their recipes ran, and with how many.
        /// </remarks>
        private static void FlushWages(Settlement settlement)
        {
            WageDay day;
            if (!_wageDay.TryGetValue(settlement, out day))
            {
                return;
            }
            _wageDay.Remove(settlement);

            if (!EconomyLog.IsEnabled || day.ShopCycles == 0)
            {
                return;
            }

            EconomyLog.Log("SHOPWAGE", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                "shops " + day.ShopPaid + "d over " + day.ShopCycles + " batches"
                + " (" + Per(day.ShopPaid, day.ShopCycles) + "/batch) to citizens");
        }

        private static string Per(int paid, int cycles)
        {
            return (cycles > 0) ? (paid / cycles).ToString() : "-";
        }
    }
}
