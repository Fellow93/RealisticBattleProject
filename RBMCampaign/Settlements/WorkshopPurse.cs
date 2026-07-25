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

        // Three separate things used to share one "wages" label, which made the SHOPS line unreadable
        // the moment the artisans and the named shops started paying by different rules. They are the
        // standing overhead vanilla charges whether or not a shop worked, the per-batch payroll a named
        // shop pays its hands, and the whole of what the artisans' day added.
        private const string Overhead = "overhead";
        private const string Payroll = "payroll";
        private const string Craft = "craftwage";

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
            _inputSpendToday.Clear();
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
            private static void Prefix(EquipmentElement outputItem, Workshop workshop, ref bool effectCapital,
                out int[] __state)
            {
                _context = Output;
                __state = null;
                if (!IsCitizenLabour(workshop))
                {
                    return;
                }

                effectCapital = true;
                Town town = workshop.Settlement != null ? workshop.Settlement.Town : null;
                if (town != null)
                {
                    __state = new int[] { town.GetItemPrice(outputItem) };
                }
            }

            /// <summary>Pays the part of the price vanilla's per-cycle ceiling leaves on the table.</summary>
            /// <remarks>
            /// Vanilla settles an output at <c>min(1000, price)</c> on both sides, which is a wash for a
            /// brewery selling beer and a swindle for a bench turning out mail: the town takes the armour
            /// onto its shelf at full value and pays a thousand for it. Under the old gold-free artisans
            /// that cost nothing, since neither side moved. Now that the shop's takings are what its hands
            /// are paid, the ceiling would be a standing cut in the wages of exactly the highest-skilled
            /// work in the town, so the remainder is moved after the fact.
            /// </remarks>
            private static void Postfix(Workshop workshop, int[] __state)
            {
                if (__state == null || !Campaign.Current.GameStarted)
                {
                    return;
                }
                int rest = __state[0] - 1000;
                if (rest <= 0)
                {
                    return;
                }
                Town town = workshop.Settlement != null ? workshop.Settlement.Town : null;
                if (town == null)
                {
                    return;
                }
                workshop.ChangeGold(rest);
                town.ChangeGold(-rest);
            }

            private static void Finalizer()
            {
                _context = null;
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "ConsumeInputFromTownMarket")]
        private static class InputContext
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

                effectCapital = true;

                // The price of the item vanilla is about to take, read before it is taken -- a recipe that
                // clears the last of a good off the shelf leaves nothing to price afterwards. Same walk
                // and same first match as ConsumeInputFromTownMarket's own FindIndex.
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
                        __state = new int[] { town.GetItemPrice(item), productionInputCount };
                        break;
                    }
                }
            }

            /// <summary>Pays for the rest of the units the recipe just took off the shelf.</summary>
            /// <remarks>
            /// Vanilla charges ONE item's price and removes <c>productionInputCount</c> of them, which no
            /// vanilla recipe notices because vanilla recipes take one of everything. RBM's take up to
            /// twenty ingots at a time, so left alone the bench would buy an armour's worth of steel for
            /// the price of a single ingot -- goods off the citizens' shelf for a nineteenth of their
            /// worth, and every denar of the difference showing up as wages. The shortfall is charged
            /// here, at the same price vanilla used for the first unit.
            /// </remarks>
            private static void Postfix(Workshop workshop, Town town, int[] __state)
            {
                if (__state == null || !Campaign.Current.GameStarted || town == null)
                {
                    return;
                }
                int owed = __state[0] * (__state[1] - 1);
                if (owed > 0)
                {
                    workshop.ChangeGold(-owed);
                    town.ChangeGold(owed);
                }

                RecordInputSpend(workshop, __state[0] * __state[1]);
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
        /// The artisans: the town's own craftsmen, who buy their materials off the shelf, sell their work
        /// back to it, and are paid for the difference.
        /// </summary>
        /// <remarks>
        /// Every town has a hidden <c>artisans</c> shop in slot 0, and it is not a business in the sense
        /// the other twelve types are. It is the townspeople themselves: the butcher jointing the cow, the
        /// smith at his tier-1 blades.
        ///
        /// That reading once argued for moving no money at all -- both sides of the trade are the same
        /// pocket, so why shuffle denars between them. But a purse that neither takes nor pays is a trade
        /// that never reaches the man who did the work: the town's largest body of craftsmen earned
        /// nothing, and the goods appeared on the shelf as if by weather. So the circuit is closed
        /// instead. Citizens pay the shop for what it makes, the shop pays the citizens for making it,
        /// and its capital is only the buffer between the two -- see <see cref="PayCitizenLabourWage"/>.
        ///
        /// Three things follow, and all three are deliberate. Both trade legs pay the market fee, because
        /// goods crossing the counter are what the fee is for, whoever is buying; the wage itself is not
        /// taxed, here or for any other shop. The per-cycle price ceiling is lifted, or the best-paid work
        /// in town would be the worst paid. And citizen wealth becomes a gate on production: a town whose
        /// people have no money cannot buy its own craftsmen's output, and the bench idles until the wages
        /// land.
        /// </remarks>
        private static bool IsCitizenLabour(Workshop workshop)
        {
            return RBMConfig.RBMConfig.rbmCampaignEnabled
                && workshop != null
                && workshop.WorkshopType != null
                && workshop.WorkshopType.IsHidden;
        }

        // effectCapital is vanilla's own switch for "this recipe settles in gold", and it is a plain
        // parameter on every method that reads it, so setting it by ref is exact. Vanilla clears it for
        // any recipe whose goods are not all trade goods -- which is most of the artisans' bench, every
        // blade and every mail shirt -- and that work should be paid for like the rest of it.

        /// <summary>
        /// Holds the artisans to the same solvency test as any other shop, now that they trade for real.
        /// </summary>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanNotableWorkshopProduceThisCycle")]
        private static class CitizenLabourSettlesInGold
        {
            private static void Prefix(Workshop workshop, ref bool effectCapital)
            {
                if (IsCitizenLabour(workshop))
                {
                    effectCapital = true;
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
        /// How many days of materials the artisans keep in hand before paying the rest out.
        /// </summary>
        /// <remarks>
        /// The float is what lets the bench open tomorrow: a cycle is refused outright when the shop's
        /// capital is short of one cycle's materials, so a shop that paid itself to nothing would stop
        /// the whole town's craft for a day. Three days is enough to ride out a day of dear iron without
        /// being so much that a town's wages sit in a till instead of in its pockets.
        /// </remarks>
        private const int DaysOfMaterialsHeld = 3;

        /// <summary>
        /// What a named workshop pays its hands for one production cycle.
        /// </summary>
        /// <remarks>
        /// Vanilla's wage bill is <c>DailyExpense</c>, a flat hundred denars a day that a shop pays
        /// whether it ran fifty cycles or none: an overhead, not a wage. So the one number in the game
        /// that was supposed to represent workshop labour was blind to whether any labour happened.
        ///
        /// This is per CYCLE, so it scales with the work actually done -- one batch a day costs a
        /// hundred and fifty, ten batches fifteen hundred. And it is roughly twice what the artisans'
        /// bench yields for a batch, which is the point: a brewer, a vintner or a silversmith is a
        /// tradesman at a craft he was apprenticed to, and the general bench is not.
        ///
        /// Left alongside vanilla's flat expense rather than replacing it, because the two now mean
        /// different things -- the hundred is the shop's standing overhead, this is its payroll.
        /// </remarks>
        private const int WorkshopWagePerCycle = 150;

        // Cycles each shop actually completed today, counted off the two methods that run one. Consumed
        // by whichever payroll pays that shop, so an entry never outlives the day that made it.
        private static readonly Dictionary<Workshop, int> _cyclesToday = new Dictionary<Workshop, int>();

        /// <summary>A settlement's day of wages, split by the two rules that pay them.</summary>
        private struct WageDay
        {
            public int CraftCycles;
            public int CraftPaid;
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

        private static void RecordWage(Settlement settlement, int cycles, int paid, bool isCraft)
        {
            if (!EconomyLog.IsEnabled || settlement == null)
            {
                return;
            }
            WageDay day;
            _wageDay.TryGetValue(settlement, out day);
            if (isCraft)
            {
                day.CraftCycles += cycles;
                day.CraftPaid += paid;
            }
            else
            {
                day.ShopCycles += cycles;
                day.ShopPaid += paid;
            }
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
            RecordWage(settlement, cycles, wage, false);
        }

        // What each town's artisans spent on materials today, so the float can be sized against the work
        // actually being done rather than a figure picked in advance. Read and cleared by the daily wage.
        private static readonly Dictionary<Settlement, int> _inputSpendToday = new Dictionary<Settlement, int>();

        private static void RecordInputSpend(Workshop workshop, int spend)
        {
            if (spend <= 0 || workshop == null || workshop.Settlement == null)
            {
                return;
            }
            int running;
            _inputSpendToday.TryGetValue(workshop.Settlement, out running);
            _inputSpendToday[workshop.Settlement] = running + spend;
        }

        /// <summary>
        /// Pays the day's takings out to the townspeople, keeping back a working float.
        /// </summary>
        /// <remarks>
        /// The wage is not a rate but a remainder, and that is the point: production is the only thing
        /// that moves this purse, so everything in it above the float IS the value the day's work added.
        /// Paying that out needs no wage rate to be invented and no share to be guessed at, and it scales
        /// with the bench itself -- a big town runs more cycles, earns more, and pays more.
        ///
        /// A bad day, where materials cost more than the work fetched, simply pays nothing and eats into
        /// the float rather than charging anyone for the loss.
        ///
        /// Runs from <c>HandleDailyExpense</c>, which vanilla calls immediately after a shop's production
        /// for the day and then skips for hidden types -- so the ordering is already right and the hook is
        /// otherwise unused for the artisans.
        /// </remarks>
        private static void PayCitizenLabourWage(Workshop shop)
        {
            if (!IsCitizenLabour(shop))
            {
                return;
            }
            Settlement settlement = shop.Settlement;
            if (settlement == null || !SettlementWealth.HasCitizenPurse(settlement))
            {
                return;
            }

            int spentOnMaterials;
            _inputSpendToday.TryGetValue(settlement, out spentOnMaterials);
            _inputSpendToday.Remove(settlement);

            int held = spentOnMaterials * DaysOfMaterialsHeld;
            int opening = Campaign.Current.Models.WorkshopModel.InitialCapital;
            if (held < opening)
            {
                held = opening;
            }

            // Taken whether or not there is anything to pay, so a day the bench worked for nothing still
            // clears its own tally instead of carrying the count into tomorrow.
            int cycles = TakeCycles(shop);

            int wage = shop.Capital - held;
            if (wage <= 0)
            {
                return;
            }

            _context = Craft;
            shop.ChangeGold(-wage);
            _context = null;

            // Untaxed, for the same reason the other shops' wages are: the fee belongs on the two trades
            // that bracket this one, which the artisans now pay like anyone else.
            SettlementWealth.CreditCitizens(settlement, wage, SettlementWealth.Source.WorkshopWages);
            RecordWage(settlement, cycles, wage, true);
        }

        /// <summary>
        /// Both payrolls, at the point vanilla has finished a shop's day.
        /// </summary>
        /// <remarks>
        /// <c>DailyTickTown</c> runs a shop's production and then calls this, so the cycles are counted
        /// by the time the wage is worked out. The two branches are the two kinds of shop: the artisans
        /// are self-employed and take the whole of what the day added, a named workshop pays a wage per
        /// batch and its owner keeps the rest.
        /// </remarks>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "HandleDailyExpense")]
        private static class CitizenLabourWagePatch
        {
            private static void Postfix(Workshop shop)
            {
                if (shop != null && shop.WorkshopType != null && shop.WorkshopType.IsHidden)
                {
                    PayCitizenLabourWage(shop);
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
                        if (pair.Key == Payroll || pair.Key == Craft)
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

            if (!EconomyLog.IsEnabled || (day.CraftCycles == 0 && day.ShopCycles == 0))
            {
                return;
            }

            EconomyLog.Log("SHOPWAGE", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                "artisans " + day.CraftPaid + "d over " + day.CraftCycles + " batches"
                + " (" + Per(day.CraftPaid, day.CraftCycles) + "/batch)"
                + "  ·  shops " + day.ShopPaid + "d over " + day.ShopCycles + " batches"
                + " (" + Per(day.ShopPaid, day.ShopCycles) + "/batch)"
                + "  ·  total " + (day.CraftPaid + day.ShopPaid) + "d to citizens");
        }

        private static string Per(int paid, int cycles)
        {
            return (cycles > 0) ? (paid / cycles).ToString() : "-";
        }
    }
}
