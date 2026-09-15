using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using System.Collections.Generic;

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
    /// caller is told exactly how much came from where so it can pay it over to whoever did the work.
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

        // clanId -> gold the clan's leader handed over in garrison subsidies today. NOT persisted and not
        // authoritative for anything: it exists so the player's clan finance breakdown can show the drain
        // he is paying (see GarrisonSubsidyFinanceLine). Rolled the moment the campaign day changes.
        private static readonly Dictionary<string, int> _paidToday = new Dictionary<string, int>();
        private static int _tallyDay = int.MinValue;

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
            // The player meant it and may spend to zero; an AI clan pays only out of surplus.
            int floor = (fief.OwnerClan == Clan.PlayerClan) ? 0 : AiOwnerGoldFloor;
            int spendable = payer.Gold - floor;
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
        /// The owner's gold is destroyed rather than transferred (<c>GiveGoldAction</c> with a null
        /// recipient): it is not moved into the fief's treasury, because the treasury is not the thing
        /// being paid -- the men and the market are, and the caller pays them the sum reported here. One
        /// transfer, not two, so nothing is minted in between.
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

            int fromOwner = MathF.Min(shortfall, OwnerCapacity(fief, purpose, ownerCap));
            if (fromOwner > 0)
            {
                Hero payer = PayerOf(fief);
                if (payer != null)
                {
                    GiveGoldAction.ApplyBetweenCharacters(payer, null, fromOwner, true);
                    ownerPaid = fromOwner;
                    RecordOwnerSubsidy(fief.OwnerClan, fromOwner);
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

        // ---- Daily owner tally (display only) ---------------------------------------------------------

        /// <summary>Notes gold an owner handed over today, so the player's finance breakdown can show it.</summary>
        private static void RecordOwnerSubsidy(Clan clan, int gold)
        {
            if (clan == null || gold <= 0)
            {
                return;
            }
            RollDayIfNeeded();
            int paid;
            _paidToday.TryGetValue(clan.StringId, out paid);
            _paidToday[clan.StringId] = paid + gold;
        }

        /// <summary>Gold this clan has paid in garrison subsidies today -- maintenance and promotions.</summary>
        public static int PaidTodayBy(Clan clan)
        {
            if (clan == null)
            {
                return 0;
            }
            RollDayIfNeeded();
            int paid;
            return _paidToday.TryGetValue(clan.StringId, out paid) ? paid : 0;
        }

        // Clears the tally the first time it is touched after the campaign day rolls over -- the same
        // day-keyed pattern PartyUpgradeBudget uses, so no event hook is needed and a mid-day reload is
        // harmless (the figure is cosmetic).
        private static void RollDayIfNeeded()
        {
            if (Campaign.Current == null)
            {
                return;
            }
            int today = (int)CampaignTime.Now.ToDays;
            if (today != _tallyDay)
            {
                _paidToday.Clear();
                _tallyDay = today;
            }
        }
    }
}
