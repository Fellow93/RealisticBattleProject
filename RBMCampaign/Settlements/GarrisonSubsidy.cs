using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Answers one question for a fief that cannot pay its garrison today: who covers the shortfall.
    ///
    /// Under the ledger a garrison is the settlement's own charge, paid out of the settlement's treasury
    /// (<see cref="GarrisonUpkeep"/>). That is right for a fief in good order and wrong for a frontier
    /// castle a lord WANTS held: its treasury runs dry, its maintenance goes undone, its promotions stop
    /// and <see cref="GarrisonRecruitCost"/> starts standing men down -- all while the owner sits on a war
    /// chest. A lord in that position would simply pay, and so would a town that would rather fund the
    /// wall than be sacked.
    ///
    /// So a shortfall is met in a fixed order:
    ///
    ///   FIEF TREASURY -- always first, and always the caller's own business: by the time anything here is
    ///   asked, the treasury has already given what it had.
    ///
    ///   OWNER -- the clan that holds the fief, out of its leader's gold, but only where the player has
    ///   said so. The opt-in is the fief's OWN garrison wage limit: "unlimited" means "I will pay for this
    ///   garrison whatever it costs", which is precisely what the control has always meant, and is what
    ///   RBM already forces on every AI fief (<see cref="GarrisonWageLimit"/>). A FINITE limit is left
    ///   alone rather than treated as a subsidy ceiling: vanilla does not treat it as a spending cap
    ///   either (the owner is still billed the wage residual; the limit drives desertion and the ideal
    ///   garrison size), so reading it as one here would quietly change a control the player already
    ///   understands. Upgrades have their own opt-in instead -- the per-party daily upgrade budget in
    ///   <see cref="PartyUpgradeBudget"/>.
    ///
    ///   TOWN CITIZENS -- last, and only in a town, and only out of what the market holds ABOVE
    ///   <see cref="CitizenReserveFloor"/>. A wealthy burgher class will fund the garrison standing
    ///   between it and a sack; a market that is itself short is left alone, because draining it stops
    ///   the town trading its way back up. Castles have no such pot at all.
    ///
    /// Money is conserved throughout: every denar this hands over is actually taken from a purse, and the
    /// caller is told exactly how much came from where so it can pay it over to whoever did the work. The
    /// owner's MAINTENANCE share is the one leg taken late rather than on the spot: it is booked to his
    /// clan and charged on the clan's next finance apply pass, so the recurring bill shows in the Daily
    /// Gold Change and the finance breakdown can project it (see <see cref="GarrisonSubsidyFinanceLine"/>).
    /// </summary>
    public static class GarrisonSubsidy
    {
        /// <summary>
        /// Gold an AI clan keeps back before it will subsidise a fief -- it pays out of surplus, never down
        /// to its last denar, or it would strand its field armies to hold a castle.
        ///
        /// Vanilla draws its garrison poverty line at 8000 (<c>DefaultClanFinanceModel.AddPartyExpense</c>:
        /// below that an AI clan pays a garrison nothing at all). RBM runs wages an order of magnitude
        /// higher (see <c>LordPartyWageLimit</c>'s wage-limit scale of 10), so the same line in RBM money
        /// is 80,000. UNCALIBRATED: derived, not measured.
        ///
        /// The player's clan has no floor -- a player who set a fief to unlimited wages meant it, and may
        /// spend himself to zero holding the place.
        /// </summary>
        public const int AiOwnerGoldFloor = 80000;

        /// <summary>
        /// Citizen wealth a town keeps circulating before any of it is spent on the garrison. Below this
        /// the market is itself short, and taking its money is how a town gets stuck paying scarcity
        /// prices it cannot trade its way out of (see <see cref="SettlementWealth"/>); only genuine
        /// surplus above the floor is a burgher class rich enough to fund its own walls. UNCALIBRATED.
        /// </summary>
        public const int CitizenReserveFloor = 50000;

        /// <summary>What a subsidy is being asked for -- each leg has its own opt-in control.</summary>
        public enum Purpose
        {
            /// <summary>The garrison's daily wage. Owner leg gated on the fief's unlimited wage limit.</summary>
            Wage,
            /// <summary>The garrison's daily kit maintenance. Same gate as the wage: one bill, one opt-in.</summary>
            Maintenance,
            /// <summary>A garrison promotion. Gated on the party's daily upgrade budget instead.</summary>
            Upgrade
        }

        // The owner's share of garrison MAINTENANCE, booked on each fief's daily tick and charged on the
        // owning clan's next finance apply pass (GarrisonSubsidyFinanceLine), the way the wealth tax is
        // paid. Running the recurring bill through CalculateClanGoldChange is what puts it in the Daily
        // Gold Change and lets the breakdown project the next charge exactly; taking it straight from the
        // leader's gold mid-day did neither. SERIALIZED: the market was paid on the tick, so a save taken
        // before the apply must still bill the owner on load. Promotions are NOT booked here -- they are
        // one-off, gated on the gold in hand, and are charged on the spot in Cover.
        private static readonly SettlementAccrualPool _pendingMaintenance = new SettlementAccrualPool();

        /// <summary>
        /// True when this fief's owner has been told to carry the garrison's running costs -- the fief's
        /// garrison wage limit is at the wage model's maximum, i.e. "unlimited". That is the player's
        /// opt-in; RBM sets it for every AI fief already.
        /// </summary>
        public static bool OwnerCoversUpkeep(Settlement fief)
        {
            if (fief == null || Campaign.Current == null || Campaign.Current.Models == null)
            {
                return false;
            }
            return fief.GarrisonWagePaymentLimit == Campaign.Current.Models.PartyWageModel.MaxWagePaymentLimit;
        }

        /// <summary>
        /// The hero whose gold a subsidy would come out of -- the owning clan's leader -- or null where
        /// there is nobody to bill: an unowned fief, or a rebel or bandit clan with no leader at all.
        /// </summary>
        public static Hero PayerOf(Settlement fief)
        {
            Clan owner = (fief != null) ? fief.OwnerClan : null;
            if (owner == null || owner.IsEliminated)
            {
                return null;
            }
            Hero leader = owner.Leader;
            return (leader != null && leader.IsAlive) ? leader : null;
        }

        /// <summary>
        /// Gold the owner could put into this fief today for this purpose, before the citizens are asked.
        /// Zero when the purpose's opt-in is off, when there is nobody to bill, or when an AI clan is at
        /// or below its <see cref="AiOwnerGoldFloor"/>.
        /// </summary>
        /// <param name="ownerCap">
        /// A caller-supplied ceiling on the owner leg -- the upgrade budget's remaining gold, for an
        /// upgrade. <see cref="int.MaxValue"/> means "no ceiling".
        /// </param>
        public static int OwnerCapacity(Settlement fief, Purpose purpose, int ownerCap)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || ownerCap <= 0)
            {
                return 0;
            }
            if (purpose != Purpose.Upgrade && !OwnerCoversUpkeep(fief))
            {
                return 0;
            }
            Hero payer = PayerOf(fief);
            if (payer == null)
            {
                return 0;
            }
            // The player meant it and may spend to zero; an AI clan pays only out of surplus. Maintenance
            // already booked but not yet charged is as good as spent, so it comes off the purse first --
            // otherwise a clan's fiefs could each promise the same gold before the apply pass takes it.
            int floor = (fief.OwnerClan == Clan.PlayerClan) ? 0 : AiOwnerGoldFloor;
            int spendable = payer.Gold - floor - _pendingMaintenance.Pending(fief.OwnerClan);
            if (spendable <= 0)
            {
                return 0;
            }
            return (ownerCap == int.MaxValue) ? spendable : MathF.Min(spendable, ownerCap);
        }

        /// <summary>
        /// Gold the town's market could put into the garrison today: whatever its citizens hold above
        /// <see cref="CitizenReserveFloor"/>. Always zero for a castle, which has no market pot.
        /// </summary>
        public static int CitizenCapacity(Settlement fief)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || fief == null || !fief.IsTown)
            {
                return 0;
            }
            int excess = SettlementWealth.GetCitizenWealth(fief) - CitizenReserveFloor;
            return (excess > 0) ? excess : 0;
        }

        /// <summary>
        /// How much of <paramref name="shortfall"/> the owner covers: all of it, up to his
        /// <see cref="OwnerCapacity"/>. The one split <see cref="Cover"/> charges with and the finance
        /// projection (<see cref="GarrisonUpkeep.ProjectOwnerMaintenance"/>) reads, so the two agree.
        /// </summary>
        public static int OwnerShare(Settlement fief, int shortfall, Purpose purpose, int ownerCap)
        {
            return (shortfall <= 0) ? 0 : MathF.Min(shortfall, OwnerCapacity(fief, purpose, ownerCap));
        }

        /// <summary>
        /// Everything outside the fief's own treasury that could cover a bill today -- the owner leg plus
        /// the citizen leg. The pure-compute twin of <see cref="Cover"/>, for gates and projections that
        /// must not move a coin.
        /// </summary>
        public static int Capacity(Settlement fief, Purpose purpose, int ownerCap)
        {
            long total = (long)OwnerCapacity(fief, purpose, ownerCap) + CitizenCapacity(fief);
            return (total > int.MaxValue) ? int.MaxValue : (int)total;
        }

        /// <summary>
        /// Whether the fief's garrison bill would be met from outside the treasury today -- the test the
        /// shed gate uses to hold a garrison instead of standing men down when somebody is willing to pay
        /// for it. Read against the garrison's own running bill, so it is the upkeep opt-in that answers.
        /// </summary>
        public static bool CanCoverDaily(Settlement fief, int amount)
        {
            return amount > 0 && Capacity(fief, Purpose.Maintenance, int.MaxValue) >= amount;
        }

        /// <summary>
        /// The gold a garrison's promotions could draw from outside its fief's treasury -- the owner
        /// (within his party upgrade budget) plus the town's citizen surplus. Sized for the affordability
        /// gate in <see cref="SpoilsUpgradePatches"/>, which turns it into a number of men.
        /// </summary>
        public static int UpgradeCapacity(Settlement fief, PartyBase party, int budgetRemaining)
        {
            int cap = budgetRemaining;
            if (party != null && cap == int.MaxValue && !PartyUpgradeBudget.IsUnlimited(party))
            {
                cap = PartyUpgradeBudget.GetRemainingDailyBudget(party);
            }
            return Capacity(fief, Purpose.Upgrade, cap);
        }

        /// <summary>
        /// Actually covers <paramref name="shortfall"/> denars of a garrison bill the fief's treasury could
        /// not meet, owner first and the town's citizens second, and reports the split so the caller can
        /// hand the money on to whoever did the work. Returns the total covered, which may be less than
        /// the shortfall -- what is left simply goes unpaid, exactly as before.
        /// </summary>
        /// <remarks>
        /// The owner's gold is destroyed rather than transferred: it is not moved into the fief's treasury,
        /// because the treasury is not the thing being paid -- the men and the market are, and the caller
        /// pays them the sum reported here. One transfer, not two, so nothing is minted in between.
        ///
        /// HOW the owner pays depends on the purpose. A promotion is taken from his gold on the spot
        /// (<c>GiveGoldAction</c> with a null recipient), because whether it happens at all hangs on the
        /// gold he has now. Maintenance is a recurring daily bill, so it is booked to his clan instead and
        /// charged on its next finance apply pass (<see cref="ConsumePendingMaintenance"/>): the same coin,
        /// leaving his purse a few hours later, through the Daily Gold Change.
        /// </remarks>
        public static int Cover(Settlement fief, int shortfall, Purpose purpose, int ownerCap,
            out int ownerPaid, out int citizensPaid)
        {
            ownerPaid = 0;
            citizensPaid = 0;
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || fief == null || shortfall <= 0)
            {
                return 0;
            }

            int fromOwner = OwnerShare(fief, shortfall, purpose, ownerCap);
            if (fromOwner > 0)
            {
                Hero payer = PayerOf(fief);
                if (payer != null)
                {
                    if (purpose == Purpose.Maintenance)
                    {
                        _pendingMaintenance.Accrue(fief, fief.OwnerClan, fromOwner);
                    }
                    else
                    {
                        int payerGoldBefore = payer.Gold;
                        GiveGoldAction.ApplyBetweenCharacters(payer, null, fromOwner, true);
                        // A promotion paid in gold, the same event a clan party's is; ignored unless the
                        // payer is in the player's clan.
                        ClanEventGoldLedger.Record(payer, EventGoldKind.UpgradeGold, payerGoldBefore - payer.Gold);
                    }
                    ownerPaid = fromOwner;
                }
            }

            int rest = shortfall - ownerPaid;
            if (rest > 0)
            {
                int fromCitizens = MathF.Min(rest, CitizenCapacity(fief));
                if (fromCitizens > 0)
                {
                    citizensPaid = SettlementWealth.DebitCitizens(fief, fromCitizens, SettlementWealth.Source.GarrisonSubsidy);
                }
            }

            return ownerPaid + citizensPaid;
        }

        /// <summary>
        /// Covers a shortfall out of the town's citizen surplus ALONE. Used by the wage leg, where the
        /// owner is billed by vanilla's own finance pass rather than here, so charging him again would
        /// take the wage twice.
        /// </summary>
        public static int CoverFromCitizens(Settlement fief, int shortfall)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || fief == null || shortfall <= 0)
            {
                return 0;
            }
            int fromCitizens = MathF.Min(shortfall, CitizenCapacity(fief));
            return (fromCitizens <= 0) ? 0
                : SettlementWealth.DebitCitizens(fief, fromCitizens, SettlementWealth.Source.GarrisonSubsidy);
        }

        /// <summary>Who is standing behind this fief's garrison, for the garrison-change tooltip.</summary>
        public static string SubsidiserName(Settlement fief)
        {
            if (OwnerCapacity(fief, Purpose.Maintenance, int.MaxValue) > 0 && fief.OwnerClan != null)
            {
                return fief.OwnerClan.Name != null ? fief.OwnerClan.Name.ToString() : string.Empty;
            }
            return null;
        }

        // ---- Owner maintenance, charged through the finance model ---------------------------------------

        /// <summary>
        /// Marks a fief's maintenance day as assessed, whatever the owner's share came to (usually nothing),
        /// so the finance projection stops counting the fief until its owner's next apply pass. Called at
        /// the top of every <see cref="GarrisonUpkeep.ChargeMaintenance"/>; the share itself, when there is
        /// one, is booked by <see cref="Cover"/>.
        /// </summary>
        public static void MarkMaintenanceAssessed(Settlement fief)
        {
            _pendingMaintenance.Accrue(fief, null, 0);
        }

        /// <summary>
        /// Hands over the maintenance subsidy the clan's fiefs booked since its last apply pass, and empties
        /// the pool. Called once per clan per day from the apply pass, which charges the returned sum to the
        /// leader as a negative line; every point of it is maintenance a market has already been paid for.
        /// </summary>
        internal static int ConsumePendingMaintenance(Clan clan)
        {
            return _pendingMaintenance.Consume(clan);
        }

        /// <summary>
        /// What the clan's next apply pass will charge it in garrison maintenance subsidies: what its fiefs
        /// have already booked, plus the projected owner share of every fief that has not ticked since
        /// (<see cref="GarrisonUpkeep.ProjectOwnerMaintenance"/>). The figure the finance breakdown shows.
        /// </summary>
        internal static int ProjectNextOwnerMaintenance(Clan clan)
        {
            return _pendingMaintenance.ProjectNext(clan, GarrisonUpkeep.ProjectOwnerMaintenance);
        }

        /// <summary>Persists the booked-but-uncharged maintenance with the settlement-wealth store.</summary>
        internal static void SyncData(IDataStore dataStore)
        {
            _pendingMaintenance.SyncData(dataStore, "RBM_pendingGarrisonMaintSubsidy", "RBM_garrisonMaintBookedFiefs");
        }

        /// <summary>Drops the previous campaign's pool, before this one's save is read.</summary>
        internal static void ResetForNewSession()
        {
            _pendingMaintenance.Reset();
        }
    }
}
