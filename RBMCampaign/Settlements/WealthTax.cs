using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A daily levy on a town's accumulated citizen wealth, assessed twice: once for the lord who holds
    /// the fief, and again, at its own lower rate, for the fief itself.
    ///
    /// The fief's other income to its owner is a tax on TRADE -- it moves when goods move. This is a tax
    /// on STOCK: a small daily bite of the money standing in the town's market, whether or not anyone
    /// traded that day. It is what makes a rich town worth holding for its own sake and not only for the
    /// caravans passing through it, and -- unlike every other flow in the ledger, which keeps money
    /// circulating inside the settlement -- the lord's share of it is a real drain OUT of the town, the
    /// one place citizen wealth leaves the market for good. The town's own share stays put, moving from
    /// the market's pocket into the fief's strongbox to pay the garrison, the militia and the clerks.
    /// </summary>
    public static class WealthTax
    {
        /// <summary>
        /// Fraction of citizen wealth taken each day for the owner. At roughly a tenth of a tenth of a
        /// percent it compounds to about a tenth of the town's standing wealth over a year -- a wealth
        /// tax, not a confiscation, and gentle enough day to day that a town rebuilds its float between
        /// levies.
        /// </summary>
        public const float DailyRate = 0.00027f;

        /// <summary>
        /// Fraction of citizen wealth the fief takes for its own strongbox each day, off the same balance
        /// the owner is assessed on. A little over half the lord's rate: the town keeps a smaller bite
        /// than the man it answers to.
        /// </summary>
        public const float SettlementDailyRate = 0.00014f;

        /// <summary>
        /// The line above which a market counts as hoarding: <see cref="HoardThresholdPerProsperity"/>
        /// denars of citizen wealth per point of the town's prosperity. A healthy market floats well
        /// below this, so day to day the gentle rates above are what apply; only a town that has piled
        /// up money far past what its size can justify crosses it.
        /// </summary>
        public const float HoardThresholdPerProsperity = 1000f;

        /// <summary>
        /// Fraction of the hoarded SURPLUS -- citizen wealth above the threshold, not the whole balance --
        /// taken each day for the owner while the market is over the line, in place of
        /// <see cref="DailyRate"/>. A punitive bracket, not a levy: it exists to pull the surplus back down
        /// rather than to fund the lord, and it bites hard enough to do so in days rather than years while
        /// leaving the town its proper float alone.
        /// </summary>
        public const float HoardOwnerRate = 0.10f;

        /// <summary>
        /// Fraction of the hoarded SURPLUS the fief takes for its own strongbox each day while the market
        /// is over the line, in place of <see cref="SettlementDailyRate"/> -- a flat tenth, matching the
        /// owner's, so the surplus is drained equally into the lord's purse and the town's own.
        /// </summary>
        public const float HoardSettlementRate = 0.10f;

        /// <summary>
        /// The owner's wealth-tax / castle-surplus income owed but not yet handed over, per owning clan,
        /// and which fiefs have booked their day into it. The levy is debited from the market on the
        /// settlement's daily tick -- where it belongs, off the morning balance -- but the lord is paid on
        /// his clan's next finance apply pass instead of straight to his gold, so the income runs through
        /// the Daily Gold Change like every other clan revenue (see <see cref="SettlementIncomeFinanceLine"/>).
        /// This pool is the money in flight between the two: SERIALIZED, so a save taken in that window
        /// credits the lord on load rather than dropping the coin the market already gave up. Conserved by
        /// construction -- every accrual is a debit that already happened, every consume becomes a gold
        /// credit on the finance pass. See <see cref="SettlementAccrualPool"/> for the timing model the
        /// finance display projects with.
        /// </summary>
        private static readonly SettlementAccrualPool _ownerIncome = new SettlementAccrualPool();

        /// <summary>
        /// Display only, and separate from the pending pool: the owner's share each settlement actually
        /// remitted on its most recent daily tick, for the per-fief "paid to you today" rows
        /// (<see cref="FiefProfitLines"/>). Never read to move money, and not what the clan finance
        /// breakdown shows -- that projects the next payment instead (<see cref="ProjectNextOwnerPayment"/>).
        /// </summary>
        private static readonly Dictionary<Settlement, int> _lastOwnerIncome = new Dictionary<Settlement, int>();

        /// <summary>
        /// Books what a settlement's tick took for its owner -- zero included -- to the owning clan's
        /// pending pool, to be paid on that clan's next finance apply pass, and records it as the fief's
        /// latest day for the per-fief display. A null clan drops the coin, matching the old null-owner
        /// path: a levy with no lord to collect it was never paid before this either.
        /// </summary>
        private static void BookOwnerIncome(Settlement settlement, int amount)
        {
            if (settlement == null)
            {
                return;
            }
            _lastOwnerIncome[settlement] = amount;
            _ownerIncome.Accrue(settlement, settlement.OwnerClan, amount);
        }

        /// <summary>
        /// Hands a clan the wealth-tax income accrued to it since it was last paid, and empties the pool.
        /// Called once per clan per day from the finance apply pass, which turns the returned sum into the
        /// leader's gold; a point returned here is a point that has already left some market of the clan's.
        /// </summary>
        internal static int ConsumePendingOwnerIncome(Clan clan)
        {
            return _ownerIncome.Consume(clan);
        }

        /// <summary>
        /// What the clan's next finance apply pass will pay it in wealth tax and castle surplus: the levies
        /// its fiefs have already taken since it was last paid (the pending pool), plus, for each fief that
        /// has not ticked since, the owner's share of its next levy projected off its balance as it stands
        /// (<see cref="ProjectOwnerLevy"/>). The figure the finance breakdown shows; moves nothing.
        /// </summary>
        internal static int ProjectNextOwnerPayment(Clan clan)
        {
            return _ownerIncome.ProjectNext(clan, ProjectOwnerLevy);
        }

        /// <summary>
        /// What one settlement remitted to its owner on its most recent daily tick -- the wealth tax for a
        /// town, the surplus skim for a castle. Display only (the per-fief line on the Clan screen's Fiefs
        /// tab and the town management screen, see <see cref="FiefProfitLines"/>); zero before the fief
        /// has ticked this session.
        /// </summary>
        internal static int GetSettlementDailyOwnerIncome(Settlement settlement)
        {
            int amount;
            return (settlement != null && _lastOwnerIncome.TryGetValue(settlement, out amount)) ? amount : 0;
        }

        /// <summary>
        /// Persists the in-flight pending pool -- and which fiefs have booked into it -- with the rest of
        /// the settlement-wealth store, so income the market has already given up but the lord has not yet
        /// been paid is not lost across a save, and is not projected a second time after a load. The
        /// per-fief display record (<see cref="_lastOwnerIncome"/>) is not saved -- it is a cosmetic figure
        /// that repopulates on the next tick.
        /// </summary>
        internal static void SyncData(IDataStore dataStore)
        {
            _ownerIncome.SyncData(dataStore, "RBM_pendingWealthTaxIncome", "RBM_wealthTaxBookedFiefs");
        }

        /// <summary>
        /// Drops the previous campaign's state before this one's save is read, the same reason and place
        /// <see cref="SettlementWealth.Reset"/> runs. The pending pool is re-read from the save by
        /// <see cref="SyncData"/>; the display record belongs to the departing campaign's settlements.
        /// </summary>
        internal static void ResetForNewSession()
        {
            _ownerIncome.Reset();
            _lastOwnerIncome.Clear();
        }

        /// <summary>
        /// One fief's levies for the day, assessed off its balance as it stands. See <see cref="Assess"/>.
        /// </summary>
        private struct Assessment
        {
            /// <summary>Whether the balance stands above the hoard line, switching to the hoard rates.</summary>
            public bool Hoarding;
            /// <summary>The owner's levy, before any caravan-debt repayment is carved out of it.</summary>
            public int OwnerLevy;
            /// <summary>The fief's own levy (a town's strongbox share; always zero for a castle).</summary>
            public int SettlementLevy;
            /// <summary>The most of a hoard levy offered to caravan-debt repayment; zero when none is.</summary>
            public int RepayCap;
        }

        /// <summary>
        /// The ONE levy formula: what a fief owes today off the balance it holds right now. The daily tick
        /// charges exactly this and the finance display projects with it (<see cref="ProjectOwnerLevy"/>),
        /// so the two cannot drift apart.
        /// </summary>
        private static Assessment Assess(Settlement settlement)
        {
            Assessment assessment = default(Assessment);
            if (settlement == null)
            {
                return assessment;
            }
            float prosperity = (settlement.Town != null) ? settlement.Town.Prosperity : 0f;

            // A castle: a flat tenth of its pool above the hoard line, to the owner alone (see
            // OnDailyTickCastle for why).
            if (settlement.IsCastle)
            {
                int castleWealth = SettlementWealth.GetSettlementWealth(settlement);
                int castleThreshold = (int)(prosperity * CastleEconomy.HoardThresholdPerProsperity);
                if (castleWealth > castleThreshold)
                {
                    assessment.Hoarding = true;
                    assessment.OwnerLevy = (int)((castleWealth - castleThreshold) * HoardOwnerRate);
                }
                return assessment;
            }
            if (!settlement.IsTown)
            {
                return assessment;
            }

            int wealth = SettlementWealth.GetCitizenWealth(settlement);

            // A market carrying more than 1000d per point of prosperity has hoarded past its healthy
            // float, and the day's levies switch to a flat tenth apiece -- but only on the EXCESS above
            // that line, not the whole balance -- to haul the surplus back down in days while leaving the
            // town its proper float untouched. Below the line the gentle year-scale rates apply as before,
            // assessed on the whole standing balance.
            int threshold = (int)(prosperity * HoardThresholdPerProsperity);
            bool hoarding = wealth > threshold;
            int taxable = hoarding ? (wealth - threshold) : wealth;
            float ownerRate = hoarding ? HoardOwnerRate : DailyRate;
            float settlementRate = hoarding ? HoardSettlementRate : SettlementDailyRate;

            // Tax Office: clerks, rolls and assessors -- vanilla's own TaxPerDay effect, +5/10/15%, on both
            // legs. It does not change who is taxed or when; it changes how much of what is there is found.
            float taxOffice = BuildingEffects.TaxFactor(settlement.Town);
            ownerRate *= taxOffice;
            settlementRate *= taxOffice;

            assessment.Hoarding = hoarding;
            assessment.OwnerLevy = (int)(taxable * ownerRate);
            assessment.SettlementLevy = (int)(taxable * settlementRate);
            if (hoarding && RBMCaravanInvestment.IsEnabled)
            {
                assessment.RepayCap = (int)((assessment.OwnerLevy + assessment.SettlementLevy)
                    * RBMCaravanInvestment.RepayShareOfHoardTax);
            }
            return assessment;
        }

        /// <summary>
        /// Splits what is left of a hoard levy once <paramref name="repaid"/> of it has gone to the town's
        /// caravan creditors, evenly between the owner and the strongbox. No-op when nothing was repaid.
        /// </summary>
        private static void SplitAfterRepay(ref int ownerLevy, ref int settlementLevy, int repaid)
        {
            if (repaid <= 0)
            {
                return;
            }
            int remainder = ownerLevy + settlementLevy - repaid;
            ownerLevy = remainder / 2;
            settlementLevy = remainder - ownerLevy;
        }

        /// <summary>
        /// The owner's share of this fief's next levy, projected off its balance as it stands with the
        /// same <see cref="Assess"/> the tick charges with, the caravan-debt carve-out and the market's
        /// own clamp included. Display only -- moves nothing. The balance will have moved by the time the
        /// fief actually ticks (a day's trade, a castle's income and upkeep), so this is the levy as of now.
        /// </summary>
        internal static int ProjectOwnerLevy(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
            {
                return 0;
            }
            Assessment assessment = Assess(settlement);
            if (settlement.IsCastle)
            {
                return MathF.Max(0, MathF.Min(assessment.OwnerLevy, SettlementWealth.GetSettlementWealth(settlement)));
            }
            if (!settlement.IsTown || (assessment.OwnerLevy <= 0 && assessment.SettlementLevy <= 0))
            {
                return 0;
            }
            int ownerLevy = assessment.OwnerLevy;
            int settlementLevy = assessment.SettlementLevy;
            int repaid = RBMCaravanInvestment.ProjectRepayFromHoardTax(settlement, assessment.RepayCap);
            SplitAfterRepay(ref ownerLevy, ref settlementLevy, repaid);
            int available = SettlementWealth.GetCitizenWealth(settlement) - repaid;
            return MathF.Max(0, MathF.Min(ownerLevy, available));
        }

        /// <summary>
        /// Takes the day's two levies from a town's citizen wealth -- the owner's and the fief's own --
        /// and hands each to its collector.
        /// </summary>
        /// <remarks>
        /// Towns only, matching the rest of the market model; a castle holds citizen wealth but sits
        /// outside this the same way it sits outside the food and administration systems. Both levies are
        /// assessed against the same morning balance, but each is paid only to the extent the market can
        /// actually cover it -- a town with an empty market owes nothing -- so this can never push citizen
        /// wealth below zero. The lord is served first, as he is in every other reckoning; on a market too
        /// thin to pay both, the strongbox is what goes short.
        /// </remarks>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
            {
                return;
            }
            // A castle is taxed only on a hoard, never on its healthy float -- its income is minted
            // fresh each day, so a gentle stock levy on top would just be a second tax to the lord.
            if (settlement.IsCastle)
            {
                OnDailyTickCastle(settlement);
                return;
            }
            if (!settlement.IsTown)
            {
                return;
            }

            // The rates, the hoard line and the Tax Office live in Assess, shared with the finance projection.
            Assessment assessment = Assess(settlement);
            bool hoarding = assessment.Hoarding;
            int ownerLevy = assessment.OwnerLevy;
            int settlementLevy = assessment.SettlementLevy;
            if (ownerLevy <= 0 && settlementLevy <= 0)
            {
                BookOwnerIncome(settlement, 0);
                return;
            }

            // A hoarding town is a recovered one: before the owner and fief take the hoard levy, up to
            // half of it repays any supply-caravan investment this town still owes its rescuers. The town
            // has crossed the same line that marks it as hoarding, so it is paying back out of money it
            // plainly did not need; the other half of the levy still reaches the lord and treasury. See
            // RBMCaravanInvestment.
            if (assessment.RepayCap > 0)
            {
                int repaid = RBMCaravanInvestment.RepayFromHoardTax(settlement, assessment.RepayCap);
                SplitAfterRepay(ref ownerLevy, ref settlementLevy, repaid);
            }

            int takenForOwner = (ownerLevy > 0)
                ? SettlementWealth.DebitCitizens(settlement, ownerLevy, SettlementWealth.Source.WealthTax)
                : 0;
            // Debited from the market now; booked to the owner's clan and handed over on its next finance
            // apply pass, so the income shows in the Daily Gold Change like every other clan revenue.
            BookOwnerIncome(settlement, takenForOwner);

            int takenForSettlement = (settlementLevy > 0)
                ? SettlementWealth.DebitCitizens(settlement, settlementLevy, SettlementWealth.Source.WealthTax)
                : 0;
            if (takenForSettlement > 0)
            {
                SettlementWealth.Credit(settlement, takenForSettlement, SettlementWealth.Source.WealthTax);
            }

            if (EconomyLog.IsEnabled && (takenForOwner > 0 || takenForSettlement > 0))
            {
                EconomyLog.Log("WEALTHTAX", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    (hoarding ? "HOARDING -- " : "") + "levied " + takenForOwner + "d to owner, " + takenForSettlement + "d to treasury"
                    + "  ·  citizen wealth now " + SettlementWealth.GetCitizenWealth(settlement) + "d"
                    + "  ·  settlement wealth now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }
        }

        /// <summary>
        /// The castle levy: the surplus skim, and a castle's only drain to its lord. A castle collects
        /// its income into a single wealth pool and pays its own upkeep from it (see
        /// <see cref="CastleEconomy"/>); whatever stands above the hoard line
        /// (<see cref="CastleEconomy.HoardThresholdPerProsperity"/> per point of prosperity) is what the
        /// castle did not need, and a flat tenth of that is remitted to the holding lord each day. Below
        /// the line nothing is taken. There is no fixed head-tax and no citizen share -- the lord's
        /// income from a castle is simply that it ran a surplus, and this is the one thing that keeps
        /// the pool from piling up without bound.
        /// </summary>
        private static void OnDailyTickCastle(Settlement settlement)
        {
            // The hoard line and the tenth live in Assess, shared with the finance projection.
            int levy = Assess(settlement).OwnerLevy;
            if (levy <= 0)
            {
                BookOwnerIncome(settlement, 0);
                return;
            }

            int taken = SettlementWealth.Debit(settlement, levy, SettlementWealth.Source.WealthTax);
            if (taken <= 0)
            {
                BookOwnerIncome(settlement, 0);
                return;
            }

            // As the town levy: booked to the owner's clan and paid on its next finance apply pass.
            BookOwnerIncome(settlement, taken);

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("WEALTHTAX", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "CASTLE SURPLUS -- remitted " + taken + "d to owner"
                    + "  ·  castle wealth now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }
        }
    }
}
