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
    /// Nothing in this phase moves money between them. This file is the ledger and its accessors only, so
    /// that the flows can be built one at a time on top of a store that already persists correctly.
    /// </summary>
    /// <remarks>
    /// Settlement carries no spare serialized field, so settlement wealth cannot ride along on the
    /// settlement itself. Keyed by <see cref="TaleWorlds.Core.MBObjectBase.StringId"/>, stable across a
    /// save and unique per settlement, following the same pattern as the troop-trade tallies in
    /// <see cref="TroopMarketFeedback"/>.
    ///
    /// Both purses exist for every village, castle and town. Go through this class rather than calling
    /// <c>ChangeGold</c> directly: the later phases move the accounting around behind these six methods,
    /// and a stray <c>ChangeGold</c> is money created or destroyed outside the ledger.
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
        /// </summary>
        public const int TreasuryPerProsperity = 210;

        /// <summary>
        /// Starting treasury per point of hearth, for a village. Villages are poor as institutions --
        /// a headman's chest, not a town's -- so this is a fraction of what a fief holds per head.
        /// </summary>
        public const int TreasuryPerHearth = 2;

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
            public const string Militia = "militia";
            public const string Admin = "admin";
            public const string Construction = "construction";
            public const string Dearth = "dearth";
            public const string Seed = "seed";
            public const string Delivery = "delivery";
            public const string WealthTax = "wealth-tax";
            public const string Carousing = "carousing";
            public const string TroopGoods = "troop-goods";
            public const string Boost = "boost";

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
            TradeTariff.Levy(settlement, (applied < 0) ? -applied : applied);
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
            return settlement != null && (settlement.IsTown || settlement.IsCastle);
        }

        private static int InitialSettlementWealth(Settlement settlement)
        {
            if (settlement.IsVillage && settlement.Village != null)
            {
                return (int)(settlement.Village.Hearth * TreasuryPerHearth);
            }
            if (settlement.Town != null)
            {
                return (int)(settlement.Town.Prosperity * TreasuryPerProsperity);
            }
            return 0;
        }
    }
}
