using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// The two purses a settlement holds, and the only supported way to move money in or out of either.
    ///
    /// Vanilla gives a settlement one integer, <c>SettlementComponent.Gold</c>, and it is not a purse at
    /// all: <c>DefaultSettlementEconomyModel.GetTownGoldChange</c> drags it toward
    /// <c>10000 + 12 x Prosperity</c> at a quarter of the gap per day and destroys anything above that,
    /// while the townsfolk who buy goods off the market roster conjure their money from nowhere
    /// (<c>ItemConsumptionBehavior.MakeConsumption</c> credits the town for a sale nobody paid for). It is
    /// a self-healing float standing in for an economy, not money anyone owns.
    ///
    /// This splits it in two:
    ///
    ///   CITIZEN WEALTH -- the money circulating in the settlement's market: what the merchants and
    ///   townsfolk between them can pay. Backed by vanilla's <c>Gold</c>, deliberately, so that every
    ///   vanilla consumer of settlement money keeps working untouched -- villager and caravan sales gate
    ///   on it, workshops read it, the player's trade screen shows it as the merchant's purse.
    ///   TOWNS AND CASTLES ONLY: a village has no market to circulate money in, so it has no such pot.
    ///
    ///   SETTLEMENT WEALTH -- the money the settlement holds as a body, as opposed to the money its
    ///   inhabitants hold. Vanilla has no equivalent, so for a town or castle this is a store of our
    ///   own. It is NOT <c>TradeTaxAccumulated</c>, which stays exactly what it was: a write-only
    ///   ledger that drains to the owner clan and is never spent locally.
    ///
    /// A VILLAGE HAS ONLY THE SECOND, and it lives in vanilla's own <c>Gold</c> field rather than in
    /// the store. Vanilla's village-gold mechanic is switched off to make room for it (see
    /// <see cref="VillageGoldStock"/>), which leaves the field free and means every reader vanilla
    /// already has -- the player's village stall, the shop-availability check, a sale made at the
    /// village -- reads the village's real purse with nothing to keep in step. One number, and no
    /// second village pot to model a circulation a village does not have.
    ///
    /// This file is the ledger and its accessors only. The flows built on top of it -- tariffs, wages,
    /// upkeep, trade, the countryside chain -- live in their own files and are mapped end to end in
    /// `docs/economy-money-flows.md`.
    /// </summary>
    /// <remarks>
    /// Settlement carries no spare serialized field, so settlement wealth cannot ride along on the
    /// settlement itself. Keyed by <see cref="TaleWorlds.Core.MBObjectBase.StringId"/>, stable across a
    /// save and unique per settlement, following the same pattern as the troop-trade tallies in
    /// <see cref="TroopMarketFeedback"/>.
    ///
    /// A town or castle holds BOTH purses; a village holds only the settlement one, in vanilla's own
    /// <c>Gold</c> field -- <see cref="HasCitizenPurse"/> is false for a village and
    /// <see cref="GetSettlementWealth"/> reads the field directly for one. Callers that must tell "has no
    /// market" from "market is broke" have to ask, since both answer zero.
    ///
    /// Go through this class rather than calling <c>ChangeGold</c> directly: the accounting moves around
    /// behind these six methods, and a stray <c>ChangeGold</c> is money created or destroyed outside the
    /// ledger.
    /// </remarks>
    public static class SettlementWealth
    {
        /// <summary>
        /// Starting treasury per point of prosperity, for a town or castle.
        ///
        /// Sized as roughly a month of the fief's own tax take so a new campaign opens with treasuries
        /// that look like they have been collecting for a while rather than at zero. Vanilla's town tax
        /// is about 0.35 per point of prosperity per day on the scale its models expect, which RBM
        /// reaches through <see cref="RBMProsperityEquilibrium.VanillaProsperityScale"/> -- so
        /// 0.35 x 20 x 30 days lands here.
        ///
        /// Per point of HOUSEHOLD-scale prosperity, which is what a town stores but not what a castle
        /// does -- always go through <see cref="RBMProsperityEquilibrium.HouseholdProsperity"/>.
        /// </summary>
        public const int TreasuryPerProsperity = 210;

        /// <summary>
        /// Starting treasury per point of hearth, for a village. Villages are poor as institutions --
        /// a headman's chest, not a town's -- but at two per hearth the chest held barely a thousand
        /// denars, too little to arm a single militiaman, so a village opened the campaign unable to
        /// raise the watch its hearths support. Twenty-five per hearth gives a fair-sized village a
        /// chest of some fifteen thousand -- enough for a handful of levies and the odd convoy escort,
        /// still a fraction of what a town treasury per head holds.
        /// </summary>
        public const int TreasuryPerHearth = 150;

        private static Dictionary<string, int> _settlementWealth = new Dictionary<string, int>();

        /// <summary>
        /// Drops the previous campaign's treasuries. Called from the behaviour CONSTRUCTOR, which is the
        /// only safe place: it runs on OnGameStart for a new game and a loaded save alike, and BEFORE the
        /// save is read, so a real save repopulates through <see cref="SyncData"/> while a new campaign
        /// starts clean instead of inheriting the last one's figures under the same settlement ids.
        /// </summary>
        public static void Reset()
        {
            _settlementWealth = new Dictionary<string, int>();
            _ledger.Clear();
            _citizenLedger.Clear();
        }

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_settlementWealth", ref _settlementWealth);
            if (_settlementWealth == null)
            {
                _settlementWealth = new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Seeds a treasury for every settlement that has none yet. Run once the session is up, this
        /// covers a fresh campaign, a save the mod was just added to, and any settlement a loaded save
        /// has never carried -- an entry already present keeps its stored value.
        /// </summary>
        public static void InitializeAll()
        {
            // The seed reads the countryside sums, and the cache holding them is stamped with the
            // campaign day, so it does not expire between two campaigns sitting on the same date: load a
            // save at day 20 having just played a different campaign to day 20 and the stamp matches.
            // The dead campaign's entries are keyed on Settlement objects that no longer exist, so every
            // lookup would miss and every fief would seed at ZERO -- and a seed is banked once and kept
            // for the life of the campaign, so there is no recovering from it afterwards.
            //
            // RBMSimulationCampaignBehavior already clears the cache for exactly this reason, and being
            // registered first (RBM.SubModule) its OnSessionLaunched runs before this one. This does not
            // duplicate that so much as refuse to depend on it: behaviour registration order is not a
            // contract, and a silent permanent zero is far too expensive to leave resting on one.
            RBMProsperityEquilibrium.InvalidateHearthCache();

            foreach (Settlement settlement in Settlement.All)
            {
                EnsureInitialized(settlement);
            }
        }

        /// <summary>Gives <paramref name="settlement"/> a starting purse if it lacks one.</summary>
        public static void EnsureInitialized(Settlement settlement)
        {
            // Villages keep their purse in vanilla's gold field, which always has a value, so there is
            // no "lacks one" state to detect and nothing to seed here. They are sized instead on the
            // new-game path -- see SeedVillagePurses.
            if (!Holds(settlement) || settlement.IsVillage)
            {
                return;
            }
            if (!_settlementWealth.ContainsKey(settlement.StringId))
            {
                _settlementWealth[settlement.StringId] = InitialSettlementWealth(settlement);
            }
        }

        /// <summary>
        /// Replaces vanilla's flat 1000 with a purse sized on the village, overwriting the seed
        /// <c>Village.OnInit</c> hands out.
        /// </summary>
        /// <remarks>
        /// New campaigns only. A village's purse is vanilla's own field, so on a loaded save there is
        /// no way to tell a starting 1000 from 1000 a village earned, and rewriting it would confiscate
        /// real money. Existing saves simply start their villages from whatever they were carrying and
        /// earn from there.
        /// </remarks>
        public static void SeedVillagePurses()
        {
            foreach (Settlement settlement in Settlement.All)
            {
                if (!settlement.IsVillage || settlement.Village == null)
                {
                    continue;
                }
                int gap = InitialSettlementWealth(settlement) - GetSettlementWealth(settlement);
                if (gap > 0)
                {
                    Credit(settlement, gap, Source.Seed);
                }
                else if (gap < 0)
                {
                    Debit(settlement, -gap, Source.Seed);
                }
            }
        }

        /// <summary>
        /// Replaces the flat 20,000 <c>Town.OnInit</c> deals out with a market float sized on the fief,
        /// for every town and castle.
        /// </summary>
        /// <remarks>
        /// This seed only started mattering when the liquidity controller was switched off. While
        /// <c>GetTownGoldChange</c> ran it closed a quarter of the gap to <c>10000 + 12 x prosperity</c>
        /// every day, so whatever a town opened with was gone inside a week and the seed was cosmetic.
        /// With the controller gone -- see <see cref="RBMMarketLiquidity"/> -- there is no longer
        /// anything pulling a market toward the size it ought to be, and the opening balance is simply
        /// the opening balance for good.
        ///
        /// Flat 20,000 against a target of 48,000-154,000 left every town on the map opening between two
        /// and seven times short, with no way back up but trade. That is the deadlock
        /// <c>TownTreasuryScale</c> was raised to 40 to break: food is priced on demand over supply, so a
        /// market too poor to stock itself keeps paying the scarcity price and stays too poor. The scale
        /// was fixed and the seed was not, so campaigns still opened inside the trap.
        ///
        /// New campaigns only, for the same reason villages are: citizen wealth IS vanilla's gold field,
        /// so on a loaded save a balance of 20,000 cannot be told from 20,000 a town traded its way to,
        /// and rewriting it would confiscate real money.
        /// </remarks>
        public static void SeedCitizenWealth()
        {
            foreach (Settlement settlement in Settlement.All)
            {
                if (!HasCitizenPurse(settlement) || settlement.Town == null)
                {
                    continue;
                }
                int gap = InitialCitizenWealth(settlement) - GetCitizenWealth(settlement);
                if (gap > 0)
                {
                    CreditCitizens(settlement, gap, Source.Seed);
                }
                else if (gap < 0)
                {
                    DebitCitizens(settlement, -gap, Source.Seed);
                }
            }
        }

        /// <summary>The settlement's own purse.</summary>
        public static int GetSettlementWealth(Settlement settlement)
        {
            if (!Holds(settlement))
            {
                return 0;
            }
            if (settlement.IsVillage)
            {
                return settlement.SettlementComponent.Gold;
            }
            EnsureInitialized(settlement);
            int value;
            return _settlementWealth.TryGetValue(settlement.StringId, out value) ? value : 0;
        }

        /// <summary>
        /// Every name money can enter or leave a settlement's purse under. One of these is required on
        /// every <see cref="Credit"/> and <see cref="Debit"/>, so the daily ledger can always say where
        /// a settlement's money came from and where it went.
        /// </summary>
        public static class Source
        {
            public const string Tariff = "tariff";
            public const string Homecoming = "homecoming";
            public const string GarrisonWage = "garrison-wage";
            public const string GarrisonFood = "garrison-food";

            /// <summary>
            /// A fief arming a garrison troop it auto-recruited: the equipment cost, drawn from the
            /// fief's own treasury rather than the owner's gold as vanilla charged it. In a town it
            /// reaches the armourers who kitted the man (citizen wealth); a castle, with no market,
            /// sources the gear from outside and the coin leaves the ledger. See
            /// <see cref="GarrisonRecruitCost"/>.
            /// </summary>
            public const string GarrisonRecruit = "garrison-recruit";
            public const string Militia = "militia";
            public const string Admin = "admin";
            public const string Construction = "construction";

            /// <summary>
            /// The fief buying food off a convoy its market was too broke to pay for, and only once the
            /// granary has run low -- see <c>VillagerDelivery.AdvanceForFood</c>.
            /// </summary>
            public const string Dearth = "dearth";
            public const string Seed = "seed";
            public const string Delivery = "delivery";

            /// <summary>
            /// An RBM supply caravan trading between two towns of one kingdom: the source market being
            /// paid for the surplus it loaded, or the destination market paying for the shortage it
            /// received. See <see cref="RBMCaravanArrival"/> and <see cref="RBMCaravanDispatch"/>.
            /// </summary>
            public const string Caravan = "caravan";

            /// <summary>
            /// A supply caravan on a wealthy→struggling route injecting capital into the destination
            /// market as a repayable investment, and the destination later repaying its rescuers out of
            /// its hoard levy. See <see cref="RBMCaravanInvestment"/>.
            /// </summary>
            public const string CaravanInvest = "caravan-invest";
            public const string CaravanRepay = "caravan-repay";
            public const string WealthTax = "wealth-tax";
            public const string Carousing = "carousing";
            public const string TroopGoods = "troop-goods";

            /// <summary>
            /// What a lord paid to muster a man, into the purse of the settlement that raised him -- a
            /// town its own treasury, a village its own purse (NOT the town it trades with: the village
            /// bought the kit off that town at muster and is reimbursed here, turning a profit on the men
            /// it raises). Raising soldiers is the fief's business as a body, so the fee is the
            /// settlement's rather than its shopkeepers'. Untariffed, the coin never having passed through
            /// the market. Vanilla destroyed every denar of it; the man's gear is a separate leg. See
            /// <see cref="RecruitSupply"/>.
            /// </summary>
            public const string Recruit = "recruit";
            public const string Boost = "boost";

            /// <summary>
            /// A stack's daily field upkeep, spent over the counter of whatever market supplies it: the
            /// straps, shafts, shoes and mail rings a soldier goes through keeping his kit serviceable.
            /// Charged to the men's own purses and their lord's gold in
            /// <see cref="SpoilsPool.ChargeClanMaintenance"/> and destroyed there until now; it is the
            /// single largest thing an army buys.
            /// </summary>
            public const string Maintenance = "maintenance";

            /// <summary>
            /// What a promotion cost, reaching the town whose armourers turned the man out in his new
            /// kit -- the men's own spoils and their lord's gold alike. Vanilla destroyed the gold and
            /// RBM's draw took the gear for nothing besides; see <see cref="UpgradeSupply"/>.
            /// </summary>
            public const string Upgrade = "upgrade";

            /// <summary>
            /// Goods bought and sold over the settlement's counter by anyone the ledger does not model
            /// by hand: caravans, passing lords, the player. Routed in from vanilla's own gold writes --
            /// see <see cref="RouteNativeWrite"/>.
            /// </summary>
            public const string Trade = "trade";

            /// <summary>
            /// Prisoners sold at the settlement, bought by the brokers and slavers who work its market.
            /// Vanilla paid the seller out of nothing; see <see cref="RansomFunding"/>.
            /// </summary>
            public const string Ransom = "ransom";

            /// <summary>Surgeons and bonesetters paid by soldiers mending in the settlement.</summary>
            public const string Surgery = "surgery";

            /// <summary>
            /// Vanilla's commission on a sale struck at the settlement's own stall, on its way to the
            /// owner through <c>TradeTaxAccumulated</c>. See <see cref="NativeTradeConservation"/>.
            /// </summary>
            public const string Commission = "commission";

            /// <summary>
            /// A workshop's running costs, reaching the townspeople who work it. Vanilla destroyed
            /// these -- see <see cref="WorkshopPurse"/>.
            /// </summary>
            public const string WorkshopWages = "workshop-wages";

            /// <summary>
            /// A castle's daily income drawn straight from its prosperity -- the taxable life behind the
            /// wall, apart from the town market model. See <see cref="CastleEconomy"/>.
            /// </summary>
            public const string CastleIncome = "castle-income";

            /// <summary>
            /// A town's mint striking coin from the silver ore standing in its market. The ore is
            /// consumed off the shelves and its value struck into fresh coin, most of it landing in the
            /// citizens' purse; the owner, the ruler and the fief's own strongbox each take a cut. See
            /// <see cref="Minting"/>.
            /// </summary>
            public const string Minting = "minting";

            /// <summary>
            /// A village spending its accumulated purse on finished goods at its market town, once the
            /// purse has grown past a headman's reserve. The village's savings leaving for the town
            /// market instead of hoarding forever; the goods bought leave the town's shelves, consumed
            /// by the countryside. See <see cref="VillageShopping"/>.
            /// </summary>
            public const string VillageDemand = "village-demand";

            /// <summary>
            /// A village paying its market town for the gear its new recruits take off the shelves. The
            /// gear leg of recruitment moves no money when a town arms its own sons, but a village draws
            /// its kit from a DIFFERENT settlement's market and now pays that town's merchants for it out
            /// of its own purse. See <see cref="RecruitSupply.DrawKitFromMarket"/>.
            /// </summary>
            public const string VillageArms = "village-arms";

            /// <summary>
            /// A castle paying its nearest friendly town for the gear its militia take off that town's
            /// shelves. A castle keeps no market of its own, so where a town arms its watch straight out of
            /// its citizens' wealth, a castle buys the kit off a town's stalls and pays that town's
            /// merchants for it out of its own wealth. See <see cref="MilitiaUpkeep"/>.
            /// </summary>
            public const string CastleArms = "castle-arms";

            /// <summary>
            /// A town's citizens fronting the cost of arming their own new volunteers -- the kit that leaves
            /// their shelves when a man first steps forward, before any lord has come for him. Recovered
            /// under <see cref="Recruit"/> when one is mustered; carried as a loss on the men who never are.
            /// See <see cref="RecruitSupply.DrawKitFromMarket"/>.
            /// </summary>
            public const string TownArms = "town-arms";

            /// <summary>
            /// A village purse stripped by a raid. The whole draw leaves the settlement; part is carried
            /// off as the raiders' spoils and the rest is burned, spoiled or hidden and gone from the
            /// economy. The drawn coin never re-enters a settlement purse -- the spoils leg lands in the
            /// raiding stacks' own purses, not another fief -- so this is a pure outflow on the ledger.
            /// See <see cref="SpoilsPool.OnRaidCompleted"/>.
            /// </summary>
            public const string Raid = "raid";

            /// <summary>
            /// A besieged fief bled day by day while the siege holds: the besiegers strip its hinterland
            /// and it spends down its reserves behind the wall. Drawn from a castle's treasury or a town's
            /// market (citizen) wealth -- the same pot each is sacked from at capture, a town's treasury
            /// being spared to pass to the new owner. Part of each day's draw is carried off as the
            /// besiegers' spoils, the rest destroyed. A pure outflow, like <see cref="Raid"/>. See
            /// <see cref="SpoilsPool.OnBesiegedFortificationDailyTick"/>.
            /// </summary>
            public const string Siege = "siege";

            /// <summary>
            /// A castle's remaining treasury when it falls by storm. What is not retained for the new
            /// owner is removed here -- half carried off as the besiegers' spoils, half destroyed. A pure
            /// outflow, like <see cref="Raid"/>. See <see cref="SpoilsPool.OnSettlementCaptured"/>.
            /// </summary>
            public const string Sack = "sack";
        }

        // Depth of a funnel write in progress. The ChangeGold guard uses it to tell OUR write to the
        // backing store from a native one that has to be routed -- without it the guard would catch the
        // funnel's own writes and recurse forever. A counter rather than a flag because Apply can be
        // reached from inside another purse's move (the boost pass-through credits and debits in one
        // call chain), and a flag would clear on the inner return.
        private static int _funnelDepth;

        /// <summary>Whether a purse write currently in progress came from the funnel itself.</summary>
        internal static bool IsInsideFunnel
        {
            get { return _funnelDepth > 0; }
        }

        /// <summary>
        /// Takes a gold move vanilla was about to make to a settlement and puts it through the funnel
        /// instead, so it lands on the ledger under a name like everything else.
        /// </summary>
        /// <remarks>
        /// Returns false when the settlement holds no purse this system models -- a hideout, say -- in
        /// which case the caller must let vanilla's own write proceed untouched. Routing money into a
        /// settlement the funnel refuses would silently destroy it.
        ///
        /// Which purse it lands in follows the same split as everywhere else: a village has one purse
        /// and vanilla's gold field IS it, so a sale at a village stall is settlement wealth; a town or
        /// castle keeps its market money apart from its treasury, and a trade over the counter is the
        /// market's.
        ///
        /// The town's market fee is taken here rather than at any one action, which is what lets it
        /// apply to ALL of them. A trade is a trade whether it arrived through <c>SellItemsAction</c>, a
        /// caravan being bought, a ship being repaired at a port, or something a later patch adds --
        /// they all end at this one write, so charging the fee here charges it once and charges it
        /// everywhere. See <see cref="TradeTariff"/>, which used to hook <c>SellItemsAction</c> by hand
        /// and no longer needs to.
        /// </remarks>
        internal static bool RouteNativeWrite(Settlement settlement, int amount, bool seeding)
        {
            if (settlement == null || amount == 0)
            {
                return false;
            }

            // World generation handing a town its opening market money is not a trade and must not be
            // taxed as one. Without this the 20,000 denars Town.OnInit deals out would post a phantom
            // trade on every town and pay a market fee on money that never changed hands.
            string source = seeding ? Source.Seed : Source.Trade;

            if (settlement.IsVillage)
            {
                if (!Holds(settlement))
                {
                    return false;
                }
                Apply(settlement, amount, source);
                return true;
            }

            if (!HasMarket(settlement) || settlement.SettlementComponent == null)
            {
                return false;
            }

            int applied = ApplyCitizens(settlement, amount, source);
            if (seeding)
            {
                return true;
            }
            // Charged on what actually moved, and in both directions -- a party buying from the town and
            // one selling to it both pay the fee. Levied after the money has landed, so the fee comes out
            // of a market that has already been paid rather than out of its standing float.
            //
            // Skipped while the player's shop screen is settling, because that one write is a whole
            // visit's worth of trades netted against each other rather than a trade. TradeTariff charges
            // it on the gross instead; see PlayerMarketSessionPatch.
            if (!TradeTariff.IsSessionDeferred)
            {
                TradeTariff.Levy(settlement, (applied < 0) ? -applied : applied);
            }
            return true;
        }

        // A day's movements per settlement, by source, signed -- one ledger per purse. Diagnostics only.
        private static readonly Dictionary<Settlement, Dictionary<string, int>> _ledger =
            new Dictionary<Settlement, Dictionary<string, int>>();
        private static readonly Dictionary<Settlement, Dictionary<string, int>> _citizenLedger =
            new Dictionary<Settlement, Dictionary<string, int>>();

        /// <summary>
        /// Puts money into a settlement's purse under a named source, and returns what actually went in.
        /// The ONE way money is added -- see <see cref="Apply"/>.
        /// </summary>
        public static int Credit(Settlement settlement, int amount, string source)
        {
            return (amount <= 0) ? 0 : Apply(settlement, amount, source);
        }

        /// <summary>
        /// Takes money out of a settlement's purse under a named source, and returns how much it could
        /// actually give -- less than asked for when the purse would go below zero. Callers that need to
        /// know whether the settlement could afford something should read the return value rather than
        /// checking the balance first: the clamp is the authority. The ONE way money is taken out.
        /// </summary>
        public static int Debit(Settlement settlement, int amount, string source)
        {
            return (amount <= 0) ? 0 : -Apply(settlement, -amount, source);
        }

        /// <summary>
        /// Writes down a settlement's day of purse movements and clears them. One line, every source
        /// that moved, and the balance left -- so a treasury that is bleeding can be read off directly
        /// instead of inferred from the outflow lines scattered through the log.
        /// </summary>
        public static void FlushDaily(Settlement settlement)
        {
            if (settlement == null)
            {
                return;
            }
            Flush(settlement, _ledger, "PURSE", GetSettlementWealth(settlement));
            Flush(settlement, _citizenLedger, "MARKET", GetCitizenWealth(settlement));
        }

        private static void Flush(Settlement settlement, Dictionary<Settlement, Dictionary<string, int>> ledger,
            string tag, int balance)
        {
            Dictionary<string, int> bySource;
            if (!ledger.TryGetValue(settlement, out bySource))
            {
                return;
            }
            ledger.Remove(settlement);

            if (!EconomyLog.IsEnabled || bySource.Count == 0)
            {
                return;
            }

            int inTotal = 0;
            int outTotal = 0;
            string breakdown = "";
            foreach (KeyValuePair<string, int> kv in bySource)
            {
                if (kv.Value >= 0)
                {
                    inTotal += kv.Value;
                }
                else
                {
                    outTotal += -kv.Value;
                }
                breakdown += "  " + kv.Key + " " + kv.Value + "d";
            }

            EconomyLog.Log(tag, settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                "in " + inTotal + "d  ·  out " + outTotal + "d  ·  net " + (inTotal - outTotal) + "d"
                + "  ·  balance now " + balance + "d  ·" + breakdown);
        }

        /// <summary>
        /// The single point at which a settlement's purse is ever written, whichever store backs it.
        /// Everything else goes through <see cref="Credit"/> or <see cref="Debit"/>, so the balance can
        /// only move under a named source and the day's ledger is complete by construction.
        /// </summary>
        private static int Apply(Settlement settlement, int amount, string source)
        {
            if (!Holds(settlement) || amount == 0)
            {
                return 0;
            }
            int current = GetSettlementWealth(settlement);
            int applied = current + amount < 0 ? -current : amount;
            if (applied == 0)
            {
                return 0;
            }

            if (settlement.IsVillage)
            {
                // A village's purse IS vanilla's gold field, so every reader vanilla already has --
                // the player's stall, the shop-availability line, a sale made at the village -- sees
                // the real number without a mirror to keep in step. See VillageGoldStock for the two
                // vanilla behaviours that had to be switched off to free the field for this.
                _funnelDepth++;
                try
                {
                    settlement.SettlementComponent.ChangeGold(applied);
                }
                finally
                {
                    _funnelDepth--;
                }
            }
            else
            {
                _settlementWealth[settlement.StringId] = current + applied;
            }

            if (EconomyLog.IsEnabled && source != null)
            {
                Dictionary<string, int> bySource;
                if (!_ledger.TryGetValue(settlement, out bySource))
                {
                    bySource = new Dictionary<string, int>();
                    _ledger[settlement] = bySource;
                }
                int running;
                bySource.TryGetValue(source, out running);
                bySource[source] = running + applied;
            }

            // Always-on feed to the Ledger's per-day treasury income/expense breakdown (gated on IsTown
            // inside), independent of the diagnostics log above. This is the non-village branch, so it is
            // the settlement TREASURY pot; a village's purse is handled by the citizen-wealth feed only when
            // it has a market, which a village does not, so a village posts to neither here.
            if (source != null && !settlement.IsVillage)
            {
                RBMTownLedger.AddSettlementFlow(settlement, source, applied);
            }

            return applied;
        }

        /// <summary>
        /// The money circulating in the settlement's market, held by its merchants and townsfolk. Zero
        /// for a village, which has no market pot at all -- see <see cref="HasMarket"/>.
        /// </summary>
        public static int GetCitizenWealth(Settlement settlement)
        {
            if (!HasMarket(settlement) || settlement.SettlementComponent == null)
            {
                return 0;
            }
            return settlement.SettlementComponent.Gold;
        }

        /// <summary>
        /// Whether this settlement has a market pot at all -- a town or a castle, not a village.
        /// </summary>
        /// <remarks>
        /// Needed because a zero from <see cref="GetCitizenWealth"/> or <see cref="DebitCitizens"/>
        /// cannot tell "this settlement has no market" from "its market is broke", and a caller that
        /// treats the first as the second will charge nobody and think it charged everybody.
        /// </remarks>
        public static bool HasCitizenPurse(Settlement settlement)
        {
            return HasMarket(settlement) && settlement.SettlementComponent != null;
        }

        /// <summary>
        /// Puts money into the settlement's market under a named source, and returns what actually went
        /// in. The ONE way citizen wealth is added -- see <see cref="ApplyCitizens"/>.
        /// </summary>
        public static int CreditCitizens(Settlement settlement, int amount, string source)
        {
            return (amount <= 0) ? 0 : ApplyCitizens(settlement, amount, source);
        }

        /// <summary>
        /// Takes money out of the settlement's market under a named source, and returns how much it
        /// could actually give. Vanilla's <c>ChangeGold</c> clamps at zero silently; this reports the
        /// clamp, so a caller can tell a full payment from a partial one. The ONE way it is taken out.
        /// </summary>
        public static int DebitCitizens(Settlement settlement, int amount, string source)
        {
            return (amount <= 0) ? 0 : -ApplyCitizens(settlement, -amount, source);
        }

        /// <summary>
        /// The single point at which citizen wealth is ever written. Everything goes through
        /// <see cref="CreditCitizens"/> or <see cref="DebitCitizens"/>, so the market's balance can only
        /// move under a named source and the day's ledger is complete by construction.
        /// </summary>
        private static int ApplyCitizens(Settlement settlement, int amount, string source)
        {
            if (!HasMarket(settlement) || settlement.SettlementComponent == null || amount == 0)
            {
                return 0;
            }
            int current = settlement.SettlementComponent.Gold;
            int applied = current + amount < 0 ? -current : amount;
            if (applied == 0)
            {
                return 0;
            }
            _funnelDepth++;
            try
            {
                settlement.SettlementComponent.ChangeGold(applied);
            }
            finally
            {
                _funnelDepth--;
            }

            if (EconomyLog.IsEnabled && source != null)
            {
                Dictionary<string, int> bySource;
                if (!_citizenLedger.TryGetValue(settlement, out bySource))
                {
                    bySource = new Dictionary<string, int>();
                    _citizenLedger[settlement] = bySource;
                }
                int running;
                bySource.TryGetValue(source, out running);
                bySource[source] = running + applied;
            }

            // Always-on feed to the Ledger's per-day citizen-wealth income/expense breakdown (gated on
            // IsTown inside), independent of the diagnostics log above.
            if (source != null)
            {
                RBMTownLedger.AddCitizenFlow(settlement, source, applied);
            }

            return applied;
        }

        /// <summary>Villages, castles and towns hold settlement wealth; hideouts and the rest do not.</summary>
        private static bool Holds(Settlement settlement)
        {
            return settlement != null && (settlement.IsVillage || settlement.IsTown || settlement.IsCastle);
        }

        /// <summary>
        /// Who has a market with money circulating in it, and so a citizen-wealth pot at all.
        ///
        /// Villages are excluded on purpose. A village has a stall, not a market: nobody in it buys and
        /// sells at a price, its produce leaves by convoy and what it needs comes back the same way.
        /// Giving it a second purse meant modelling a circulation that is not there, so the village
        /// keeps one purse -- its settlement wealth -- and vanilla's village <c>Gold</c> is left alone
        /// to go on being the flat float behind the player's stall.
        /// </summary>
        private static bool HasMarket(Settlement settlement)
        {
            // Towns only. A castle has no market circulating money and so no citizen purse: it holds a
            // SINGLE pool, its settlement wealth (see CastleEconomy), and everything that would credit a
            // castle's "citizens" -- garrison spending, trade or a ransom struck there -- has no second
            // pot to land in and falls back to vanilla, the castle's income being its lands, not its bar.
            return settlement != null && settlement.IsTown;
        }

        /// <summary>
        /// The prosperity figure a fief's opening purses are sized on: what its countryside supports,
        /// not what its prosperity field happens to hold at the moment it is seeded.
        ///
        /// For a TOWN this is <see cref="RBMProsperityEquilibrium.TargetProsperity"/>, computed from
        /// bound hearths and therefore always on the household scale whatever the save holds. Reading
        /// the stored value instead is only safe when RBM itself wrote it, which is true of a new
        /// campaign and false of the case <see cref="InitializeAll"/> explicitly supports: a save the
        /// mod was just added to still carries authored prosperity of 2800-5100, twenty times the
        /// household scale <see cref="TreasuryPerProsperity"/> is derived against, so such a town seeded
        /// a treasury of 840,000 against a new campaign's 52,000. Prosperity then converges down over
        /// the following days and the treasury does not -- it is a seed, banked once.
        ///
        /// For a CASTLE there is no countryside figure at all -- castles are outside the equilibrium
        /// model -- so the stored value is all there is, converted off the vanilla scale it is kept on.
        /// The 20x hazard does not arise there: a castle's prosperity is authored, never rewritten, so
        /// a fresh campaign and an adopted save read the same number.
        /// </summary>
        private static float SeedProsperity(Settlement settlement)
        {
            if (settlement == null || settlement.Town == null)
            {
                return 0f;
            }
            if (!settlement.IsTown)
            {
                return RBMProsperityEquilibrium.HouseholdProsperity(settlement.Town);
            }

            // Zero means the countryside is not known yet -- no bound village has been read, which on a
            // built map means the cache was consulted too early rather than that the town has no land.
            // Its own prosperity is a poorer figure than the target but a far better one than nothing,
            // and a zero here would be cached as the town's treasury for the life of the campaign.
            float target = RBMProsperityEquilibrium.TargetProsperity(settlement);
            return (target > 0f) ? target : RBMProsperityEquilibrium.HouseholdProsperity(settlement.Town);
        }

        private static int InitialSettlementWealth(Settlement settlement)
        {
            if (settlement.IsVillage && settlement.Village != null)
            {
                return (int)(settlement.Village.Hearth * TreasuryPerHearth);
            }
            // A castle sizes its single wealth pool on its RAW prosperity -- the same figure its daily
            // income is measured against (see CastleEconomy) -- not the household-scale conversion a
            // town uses for its treasury.
            if (settlement.IsCastle && settlement.Town != null)
            {
                return (int)(settlement.Town.Prosperity * CastleEconomy.SeedPerProsperity);
            }
            if (settlement.Town != null)
            {
                return (int)(SeedProsperity(settlement) * TreasuryPerProsperity);
            }
            return 0;
        }

        /// <summary>
        /// What a fief's market ought to open the campaign holding.
        ///
        /// Vanilla's own figure, which is the best yardstick there is for the money a settlement of this
        /// size needs circulating in it: <c>10000 + 12 x prosperity</c>, the target
        /// <c>DefaultSettlementEconomyModel.GetTownGoldChange</c> used to pull every town to. Taken on
        /// the treasury scale, the same one the drift line in <see cref="RBMMarketLiquidity"/> reports
        /// against, so a freshly seeded town opens at drift zero rather than deep in the red. Exactly
        /// zero because this runs on the new-game path alone, where a town's prosperity has just been
        /// set to the same countryside figure <see cref="SeedProsperity"/> reads.
        ///
        /// <c>TroopMarketFeedback.TreasuryBonus</c> is deliberately left out. It is a running response to
        /// what a garrison has been spending, and on day one no garrison has spent anything.
        /// </summary>
        private static int InitialCitizenWealth(Settlement settlement)
        {
            if (settlement.Town == null)
            {
                return 0;
            }
            // Towns only. A castle has no citizen purse (see HasMarket); its single wealth pool is
            // seeded as settlement wealth in InitialSettlementWealth, and this seeder skips it because
            // HasCitizenPurse is false for a castle.
            return (int)(10000f + 12f * SeedProsperity(settlement) * RBMProsperityEquilibrium.TownTreasuryScale);
        }
    }
}
