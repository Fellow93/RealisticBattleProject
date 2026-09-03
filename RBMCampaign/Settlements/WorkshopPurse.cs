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
    /// This file is a LEDGER and nothing else: it records what the RBM workshop steps move and writes the
    /// day's SHOPS and SHOPWAGE lines. The rules themselves live in <see cref="RBMWorkshopModel"/>,
    /// <see cref="RBMWorkshopCycle"/>, <see cref="RBMWorkshopSettlement"/> and
    /// <see cref="RBMWorkshopExpense"/>; the daily bill and the payroll that used to be worked out here
    /// belong to the last of those.
    /// </remarks>
    public static class WorkshopPurse
    {
        internal const string Output = "output";
        internal const string Inputs = "inputs";

        // Two separate things used to share one "wages" label, which made the SHOPS line unreadable: the
        // standing overhead vanilla charges whether or not a shop worked, and the per-batch payroll a
        // named shop pays its hands. (A third, "craftwage", was the artisans' -- they no longer move gold
        // at all, so there is nothing left to label. See RBMWorkshopCycle.SettlesInGold.)
        internal const string Overhead = "overhead";
        internal const string Payroll = "payroll";

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

        // Context markers. Each names what the shop is doing for the span of one RBM step. They were once
        // Harmony prefix/finalizer pairs around vanilla's methods; now that RBM owns those steps
        // (RBMWorkshopSettlement) they are plain calls made and released inside the step itself, which is
        // both simpler and exception-safe by construction -- there is no vanilla body between the set and
        // the clear that could throw past it.

        /// <summary>Names what a shop is doing, for the span of one RBM settlement step.</summary>
        internal static void SetContext(string context)
        {
            _context = context;
        }

        /// <summary>Releases the label, so a later move is not mislabelled as part of this step.</summary>
        internal static void ClearContext()
        {
            _context = null;
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

        /// <summary>A settlement's day of workshop payroll.</summary>
        private struct WageDay
        {
            public int ShopCycles;
            public int ShopPaid;
        }

        private static readonly Dictionary<Settlement, WageDay> _wageDay = new Dictionary<Settlement, WageDay>();

        // A shop's batch count for the day, handed over by RBMWorkshopExpense as it consumes its own
        // working tally, and kept only until the day is written.
        private static readonly Dictionary<Workshop, int> _cyclesLogged = new Dictionary<Workshop, int>();

        /// <summary>Notes a shop's day of batches for the SHOPS line.</summary>
        internal static void RecordCycles(Workshop shop, int cycles)
        {
            if (!EconomyLog.IsEnabled || shop == null)
            {
                return;
            }
            _cyclesLogged[shop] = cycles;
        }

        /// <summary>Adds a shop's payroll to its settlement's day, for the SHOPWAGE line.</summary>
        internal static void RecordWage(Settlement settlement, int cycles, int paid)
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
