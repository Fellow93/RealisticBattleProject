using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// A settlement's patrol, raised, kept and paid out of the settlement's own funds.
    ///
    /// Vanilla fields one land patrol per TOWN with a Guard House, conjured whole from a culture template,
    /// refilled for free whenever it comes home, and paid by nobody -- under RBM its men even banked a full
    /// wage into their spoils each day out of nothing. Here every town AND castle with a lawful owner may keep
    /// one, if its money can carry it, and the money is the settlement's:
    ///
    ///   * FUNDING POT -- the settlement's treasury first; a town's is backed by its citizens' money above
    ///     <see cref="GarrisonSubsidy.CitizenReserveFloor"/> (the same citizen leg the garrison subsidy uses).
    ///     A castle has only its treasury. The owner clan never pays.
    ///
    ///   * BOOST -- <c>B = 1 + 0.5 × Guard House level (towns) + 0.25 Coastal Guard Edict (port towns) + 0.25
    ///     Patrols perk (the governor's, or the owner clan leader's when there is no governor)</c>. The Guard
    ///     House no longer gates a patrol; it makes it bigger and better.
    ///
    ///   * SIZE -- the daily budget (pot × <see cref="RBMConfig.RBMConfig.patrolBudgetFraction"/> × B) over
    ///     what one man costs a day (the template's average man: wage + kit maintenance), clamped to
    ///     [<see cref="MinSize"/>, min(<see cref="MaxSizeCap"/>, 20 × B)]. Land patrols only; a naval patrol
    ///     keeps War Sails' own size.
    ///
    ///   * QUALITY -- template level <c>1 + floor((B − 1) / 0.5)</c>, 1..4: RBM's
    ///     <c>patrol_party_&lt;culture&gt;_template_level_N</c>, scaled to the size.
    ///
    ///   * MANNING -- the template decides the patrol's shape, the home garrison its men: each template slot
    ///     is filled first with the garrison's healthy men of that troop, then with garrison men of the same
    ///     tier and kind (mounted / ranged / foot, who keep their own troop), then of the same kind one tier
    ///     lower (<see cref="GarrisonLowerTierSteps"/>), never taking the garrison below
    ///     <see cref="GarrisonKeepFloor"/>; they bring their share of their stack's spoils purse. Only what the
    ///     garrison cannot spare is bought, as the template's own troop: at the garrison's per-man price and
    ///     out of its recruit pool, but judged against and paid from the FUNDING POT (<see cref="PayRecruitKit"/>),
    ///     the same money the size is worked from.
    ///
    ///   * GATES -- a fief keeps its patrol while the pot holds <see cref="KeepDays"/> days of a minimal
    ///     patrol's keep, and raises a new one only when the whole roster can be manned that way and the pot
    ///     holds <see cref="SpawnKeepFloorMult"/> times that keep reserve plus <see cref="SpawnKitReserveMult"/>
    ///     (a castle <see cref="CastleSpawnKitReserveMult"/>) times the kit of the men it would have to BUY --
    ///     so a patrol is never raised short, nor one the fief would stand down again the next day.
    ///
    ///   * COSTS -- the bought men's kit is paid for when the patrol is raised, and again for every man bought
    ///     when it comes home short (<see cref="Replenish"/>, which keeps the veterans and their purses instead
    ///     of rebuilding the roster); garrison men cost nothing to move. Its wage and maintenance are billed
    ///     daily (<see cref="PayPatrolUpkeep"/>), naval patrols included, so no patrol wage is minted any more.
    ///
    ///   * STANDING DOWN -- the men of a patrol stood down because its fief can no longer keep it, or because
    ///     the fief changed hands peacefully, go back into the garrison with their experience and purses; a fief
    ///     taken by siege or rebellion loses them, as in vanilla. A patrol at home with more men than its size
    ///     sends the surplus back too, and one that cannot be manned to <see cref="MinSize"/> is stood down.
    ///
    ///   * SPEED -- a land patrol marches <see cref="SpeedBonus"/> faster on the map, so it can run looters down.
    ///
    ///   * SECURITY -- a land patrol raises its fief's security even without a Guard House (vanilla's bonus at
    ///     level 0, <see cref="NoGuardHouseSecurityBonus"/>).
    ///
    /// Vanilla's <c>PatrolPartiesCampaignBehavior</c> and <c>PatrolPartyComponent</c> stay in charge of the
    /// party itself -- its AI, dialogs, save type and UI; the patches in PatrolUpkeep.Patches.cs only steer
    /// them. Everything is gated on <see cref="IsEnabled"/>; off is vanilla's behaviour exactly.
    ///
    /// THREADS: a patrol's size limit is read by the map's parallel movement tick (the speed model reads
    /// <c>PartySizeLimit</c>), so the plain size query only reads a cache (<see cref="CachedPartyLimit"/>) that
    /// the main thread fills (<see cref="RefreshLimit(MobileParty)"/>); everything else here runs on the main thread.
    /// </summary>
    public static class PatrolUpkeep
    {
        /// <summary>
        /// The smallest patrol worth fielding. A fief whose funds cannot keep this many keeps none. 12, not 6: in game a
        /// castle's 9-man patrol was beaten by looters the day after it was raised, losing garrison men for nothing.
        /// </summary>
        public const int MinSize = 12;

        /// <summary>
        /// Days the funding pot is averaged over (<see cref="AveragePot"/>) for the patrol's size and its keep and
        /// raise gates. A town's pot includes its citizens' money, which swings by hundreds of thousands in a few days
        /// (in game one fell from 198k to 3k and was back at 175k the next day, standing its patrol down on the dip).
        /// </summary>
        public const float PotAverageDays = 7f;

        /// <summary>The most men any patrol is sized to, however deep the fief's purse and however large its boost.</summary>
        public const int MaxSizeCap = 60;

        /// <summary>The size ceiling per point of boost: 20 men at B = 1, 30 with a Guard House, 50 with a level-3 one.</summary>
        public const int MaxSizePerBoost = 20;

        /// <summary>
        /// Days of a minimal patrol's keep the pot must hold for the fief to keep a patrol at all. The buffer that
        /// stops a patrol flickering in and out as its own pay nibbles the pot: it reads a minimal patrol's cost,
        /// not the current one's, so paying the patrol does not itself trip the gate that stands it down.
        /// </summary>
        public const int KeepDays = 15;

        /// <summary>Times the bought men's kit value a town's pot must hold, on top of the keep reserves, to raise a patrol.</summary>
        public const int SpawnKitReserveMult = 3;

        /// <summary>
        /// As <see cref="SpawnKitReserveMult"/>, for a castle: its pot is its treasury alone, with no citizens' money
        /// behind it, so three times the kit kept nearly every castle saving for good.
        /// </summary>
        public const int CastleSpawnKitReserveMult = 1;

        /// <summary>
        /// Times the keep reserve (<see cref="KeepFloor"/>) the pot must hold to raise a patrol. Above 1, so a fief
        /// whose garrison supplies every man (no kit to buy) does not raise a patrol on the very pot that would
        /// stand it down again the next day.
        /// </summary>
        public const int SpawnKeepFloorMult = 2;

        /// <summary>
        /// Daily security a land patrol gives a fief without a Guard House: vanilla's patrol bonus
        /// (<c>0.5 × Guard House level + 0.5</c>, <c>DefaultSettlementSecurityModel</c>) at level 0. A Guard House
        /// keeps vanilla's own figure.
        /// </summary>
        public const float NoGuardHouseSecurityBonus = 0.5f;

        /// <summary>Boost per Guard House level (towns): the building that used to gate a patrol now funds a bigger, better one.</summary>
        public const float GuardHouseBoostPerLevel = 0.5f;

        /// <summary>Boost for a port town whose kingdom holds War Sails' Coastal Guard Edict.</summary>
        public const float CoastalEdictBoost = 0.25f;

        /// <summary>Boost when the governor (or, with none, the owner clan's leader) has the Scouting perk Patrols.</summary>
        public const float PatrolsPerkBoost = 0.25f;

        /// <summary>Boost per template level: B 1 → level 1, 1.5 → 2, 2 → 3, 2.5 and up → 4.</summary>
        public const float TemplateLevelStep = 0.5f;

        /// <summary>The best of RBM's patrol templates (<c>patrol_party_&lt;culture&gt;_template_level_4</c>).</summary>
        public const int MaxTemplateLevel = 4;

        /// <summary>
        /// Days a fief without a Guard House drills its first patrol. Vanilla reads the Guard House level here
        /// (10 days at level 1, 2 fewer per level above) and throws on a fief without one; with one, its own
        /// figure stands.
        /// </summary>
        public const float NoGuardHouseSpawnDays = 10f;

        /// <summary>The string id War Sails registers its Coastal Guard Edict under (<c>NavalPolicies</c>), so RBM needs no NavalDLC reference.</summary>
        public const string CoastalGuardEdictPolicyId = "policy_coastal_guard_edict";

        /// <summary>
        /// Map speed a land patrol gains on its base speed, so a band of footmen can catch looters. Added flat to
        /// the base, so the terrain, night and weather factors scale it like the rest.
        /// </summary>
        public const float SpeedBonus = 0.5f;

        /// <summary>
        /// Healthy men a patrol always leaves in its fief's garrison. A fixed count, not a share of the garrison's
        /// size limit: RBM lifts that limit by 10000 (<see cref="GarrisonPartySize"/>), so any share of it kept
        /// every man at home.
        /// </summary>
        public const int GarrisonKeepFloorMen = 50;

        /// <summary>
        /// Tiers below a slot's troop a garrison man of the same kind may be and still be drawn for it, once the
        /// garrison has none of the slot's own troop or tier to spare: one, nearest tier first.
        /// </summary>
        public const int GarrisonLowerTierSteps = 1;

        /// <summary>
        /// The strength vanilla's patrol AI wants before it leaves home (<c>PartySizeRatio</c> ≥ 0.9 in
        /// <c>PatrolPartiesCampaignBehavior.AiHourlyTick</c>). A refill that leaves the patrol below it stalls the
        /// refill (<see cref="IsRefillStalled"/>) so the patrol goes back out short instead of waiting at home.
        /// </summary>
        public const float RefillStallRatio = 0.9f;

        /// <summary>Days a stalled refill lasts at most before the patrol is drawn home to try again (it also ends on reaching home).</summary>
        public const float RefillStallMaxDays = 7f;

        private static readonly TextObject SpeedText = new TextObject("{=RBM_patrol_speed}Patrol remounts");
        private static readonly TextObject StalledText = new TextObject("{=RBM_patrol_stalled}No men to refill it: patrolling short");
        private static readonly TextObject GuardHouseText = new TextObject("{=RBM_patrol_guardhouse}Guard House");
        private static readonly TextObject CoastalEdictText = new TextObject("{=RBM_patrol_edict}Coastal Guard Edict");
        private static readonly TextObject PatrolsPerkText = new TextObject("{=RBM_patrol_perk}Patrols perk");
        private static readonly TextObject MinSizeText = new TextObject("{=RBM_patrol_min}Smallest patrol worth fielding");

        /// <summary>
        /// Master gate: the campaign module and the patrol toggle must both be on, and so must the spoils system
        /// (<see cref="SpoilsPool.IsEnabled"/>): a patrol's daily pay rides its wage pass, which does not run with
        /// the upgrade cost multiplier at 0, so without it the patrols fall back to vanilla's rather than go unpaid.
        /// </summary>
        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled && RBMConfig.RBMConfig.settlementPatrolsEnabled && SpoilsPool.IsEnabled; }
        }

        /// <summary>The share of the funding pot set aside each day for the patrol, before the boost -- the player's dial.</summary>
        public static float BudgetFraction
        {
            get { return RBMConfig.RBMConfig.patrolBudgetFraction; }
        }

        // ------------------------------------------------------------------ session state

        // Each fief's patrol state as last seen by the daily pass (OnDailyTick): only a change is logged. Session
        // state, rebuilt within a day of a load.
        private static readonly Dictionary<Settlement, PatrolState> _lastState = new Dictionary<Settlement, PatrolState>();

        // The day's pay per patrol, summed over its stacks by PayPatrolUpkeep and written as one PAY line by
        // FlushPayLog once the wage pass has done the whole party. Only filled while the patrol log is on.
        private static readonly Dictionary<MobileParty, PayTally> _payTally = new Dictionary<MobileParty, PayTally>();

        // The day a patrol's refill last logged coming up short with nobody added. Vanilla rolls the refill
        // hourly while the patrol waits at home, so a dry recruit pool would otherwise write a line an hour.
        private static readonly Dictionary<MobileParty, int> _shortRefillLogDay = new Dictionary<MobileParty, int>();

        // Patrols whose last refill left them under RefillStallRatio: when, and how many men they had then. While a
        // patrol is in here its size limit is held at THAT head count (ExplainPartySize), so vanilla's AI sends it
        // back out rather than keeping it home until the garrison or the recruit pool recovers -- and men it then
        // loses in the field show as a shortfall again and draw it home. Cleared on reaching home or resting there
        // (the next refill roll decides again) or after RefillStallMaxDays. Session state: after a load the next
        // refill re-marks it.
        private static readonly Dictionary<MobileParty, StallInfo> _refillStalled = new Dictionary<MobileParty, StallInfo>();

        // Patrols whose last refill at home left them under MinSize with nobody to be had. Stood down by the next
        // daily pass (OnDailyTick) if they are still at home and still that short -- not from the refill itself,
        // which runs inside vanilla's hourly tick of that very party.
        private static readonly HashSet<MobileParty> _pendingStandDown = new HashSet<MobileParty>();

        // Each fief's funding pot averaged over PotAverageDays (an exponential average, one step a day from
        // OnDailyTick, main thread only), keyed by settlement StringId and saved (RBM_patrolAveragePot), so a load
        // does not restart the average from whatever the pot holds that day. A fief with no entry reads its live pot.
        private static Dictionary<string, int> _averagePot = new Dictionary<string, int>();

        // Each land patrol's size limit as the size query returns it (ExplainPartySize's result). Read from the map's
        // parallel movement tick (DefaultPartySpeedCalculatingModel reads PartySizeLimit on a roster version
        // change), where computing it would write the spoils system's lazy kit-value caches from several threads at
        // once; so it is only ever WRITTEN on the main thread (RefreshLimit) and only read elsewhere. Concurrent so
        // a read never sees the dictionary mid-resize.
        private static readonly ConcurrentDictionary<MobileParty, int> _partyLimit = new ConcurrentDictionary<MobileParty, int>();

        // The campaign's main thread, recorded where the session starts. -1 until then: no thread counts as main,
        // so nothing computes a size limit before the session is up.
        private static volatile int _mainThreadId = -1;

        // How the fief is changing hands while vanilla's owner-change listener stands its patrol down; null outside
        // it. Set and cleared by PatrolUpkeepPatches.OwnerChangedPatch, read by OnStandingDown.
        private static ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail? _ownerChangeDetail;

        private struct StallInfo
        {
            public CampaignTime Since;
            public int Men;
        }

        private struct PatrolState
        {
            public bool Eligible;
            public bool HasPatrol;
            public bool CanKeep;
            public bool CanRaise;
            public bool ShortOfMen;
            public int Size;
            public int Level;
        }

        /// <summary>
        /// How a raise or a refill was manned: men drawn from the garrison, men bought and what their kit cost
        /// (the planned price when only planning), and the men it could find neither way, by what ran out. Also
        /// the garrison as the fill found it -- its healthy men and how many stood above
        /// <see cref="GarrisonKeepFloor"/> -- so the log tells a floor that left nobody spare from spare men
        /// who matched no slot.
        /// </summary>
        private class FillResult
        {
            public int FromGarrison;
            public int Bought;
            public int Paid;
            public int PoolShort;
            public int FundsShort;
            public int GarrisonHealthy;
            public int GarrisonSpare;

            public int Added
            {
                get { return FromGarrison + Bought; }
            }

            public int Short
            {
                get { return PoolShort + FundsShort; }
            }

            public string Describe()
            {
                string text = "from garrison " + FromGarrison + " (garrison " + GarrisonHealthy + " healthy, "
                    + GarrisonSpare + " spare), bought " + Bought + " for " + Paid + "d";
                if (PoolShort > 0)
                {
                    text += ", pool short " + PoolShort;
                }
                if (FundsShort > 0)
                {
                    text += ", funds short " + FundsShort;
                }
                return text;
            }
        }

        private class PayTally
        {
            public int WageOwed;
            public int WagePaid;
            public int MaintOwed;
            public int MaintFromPurse;
            public int MaintFromPot;
        }

        /// <summary>
        /// Drops the previous campaign's per-fief state. Called from the settlement-wealth behaviour's constructor,
        /// on the main thread, which it records.
        /// </summary>
        public static void ResetForNewSession()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _lastState.Clear();
            _payTally.Clear();
            _shortRefillLogDay.Clear();
            _refillStalled.Clear();
            _pendingStandDown.Clear();
            _averagePot.Clear();
            _partyLimit.Clear();
            _ownerChangeDetail = null;
        }

        /// <summary>
        /// Fills the size-limit cache for every land patrol already on the map, before the first map tick reads it
        /// from the movement threads. Called from the settlement-wealth behaviour's OnSessionLaunched, after the
        /// treasuries are initialised.
        /// </summary>
        public static void OnSessionLaunched()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            if (!IsEnabled)
            {
                return;
            }
            foreach (Settlement settlement in Settlement.All)
            {
                if ((settlement.IsTown || settlement.IsCastle) && settlement.PatrolParty != null
                    && settlement.PatrolParty.MobileParty != null)
                {
                    RefreshLimit(settlement.PatrolParty.MobileParty);
                }
            }
        }

        private static bool IsMainThread
        {
            get { return Thread.CurrentThread.ManagedThreadId == _mainThreadId; }
        }

        // ------------------------------------------------------------------ eligibility & gates

        /// <summary>
        /// Whether the rework applies to this settlement at all: a town or castle with an owner clan that is not
        /// a rebel clan. Anything else -- a village, a hideout -- never keeps a patrol.
        /// </summary>
        public static bool IsEligible(Settlement settlement)
        {
            return settlement != null
                && (settlement.IsTown || settlement.IsCastle)
                && settlement.Town != null
                && settlement.OwnerClan != null
                && !settlement.OwnerClan.IsRebelClan;
        }

        /// <summary>
        /// The keep-gate: an eligible fief with a template to draw on whose pot holds <see cref="KeepDays"/> days of
        /// a minimal patrol's keep. Vanilla stands an existing patrol down the day this turns false.
        /// </summary>
        public static bool CanKeepPatrol(Settlement settlement)
        {
            if (!IsEnabled || !IsEligible(settlement))
            {
                return false;
            }
            if (TemplateFor(settlement) == null)
            {
                return false;
            }
            return HoldsKeepReserve(AveragePot(settlement), KeepFloor(settlement));
        }

        internal static bool HoldsKeepReserve(long averagePot, int keepFloor)
        {
            return keepFloor > 0 && averagePot >= keepFloor;
        }

        /// <summary>
        /// The spawn-gate: the keep-gate, plus the whole roster at the fief's size must be mannable -- garrison
        /// men and men the fief can buy under the garrison's rules (<see cref="Fill"/>, only planned here) -- and
        /// the pot must hold <see cref="SpawnKeepFloorMult"/> keep reserves plus <see cref="SpawnKitReserveMult"/>
        /// (a castle <see cref="CastleSpawnKitReserveMult"/>) times the kit of the men it would have to buy.
        /// Garrison men cost nothing, so a fief with men to spare raises cheaply; it never raises a patrol short,
        /// nor one it would stand down again at once.
        /// </summary>
        public static bool CanRaisePatrol(Settlement settlement)
        {
            return CanRaisePatrol(settlement, out bool _);
        }

        /// <summary>
        /// As <see cref="CanRaisePatrol(Settlement)"/>; <paramref name="shortOfMen"/> is true when the plan ran out of
        /// men (garrison spare and recruit pool both spent) rather than money.
        /// </summary>
        public static bool CanRaisePatrol(Settlement settlement, out bool shortOfMen)
        {
            shortOfMen = false;
            if (!CanKeepPatrol(settlement))
            {
                return false;
            }
            int size = PatrolSize(settlement);
            FillResult plan = Fill(settlement, null, TargetRoster(TemplateFor(settlement), size), size, false);
            shortOfMen = plan.PoolShort > 0;
            return plan.Short == 0
                && HoldsRaiseReserve(AveragePot(settlement), KeepFloor(settlement), plan.Paid, settlement.IsCastle);
        }

        /// <summary>
        /// The raise reserve: <see cref="SpawnKeepFloorMult"/> keep reserves plus <see cref="SpawnKitReserveMult"/>
        /// (a castle <see cref="CastleSpawnKitReserveMult"/>) times the kit of the men it has to buy.
        /// </summary>
        internal static bool HoldsRaiseReserve(long averagePot, int keepFloor, long kitPaid, bool isCastle)
        {
            int kitMult = isCastle ? CastleSpawnKitReserveMult : SpawnKitReserveMult;
            return averagePot >= (long)keepFloor * SpawnKeepFloorMult + kitPaid * kitMult;
        }

        /// <summary>The healthy (unwounded, non-hero) men in the fief's garrison; 0 without one.</summary>
        private static int GarrisonHealthyMen(Settlement settlement)
        {
            MobileParty garrison = settlement != null && settlement.Town != null ? settlement.Town.GarrisonParty : null;
            if (garrison == null || garrison.MemberRoster == null)
            {
                return 0;
            }
            TroopRoster roster = garrison.MemberRoster;
            int healthy = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character != null && !element.Character.IsHero)
                {
                    healthy += MathF.Max(0, element.Number - element.WoundedNumber);
                }
            }
            return healthy;
        }

        /// <summary>The healthy men a patrol always leaves in its fief's garrison: <see cref="GarrisonKeepFloorMen"/>.</summary>
        public static int GarrisonKeepFloor(Settlement settlement)
        {
            return GarrisonKeepFloorMen;
        }

        /// <summary>A minimal patrol's keep for <see cref="KeepDays"/> days -- the reserve the pot must hold to keep one.</summary>
        public static int KeepFloor(Settlement settlement)
        {
            return KeepFloor(PerManDaily(settlement));
        }

        internal static int KeepFloor(int perManDaily)
        {
            return MinSize * perManDaily * KeepDays;
        }

        /// <summary>Healthy garrison men a patrol may take: those above <see cref="GarrisonKeepFloorMen"/>.</summary>
        internal static int GarrisonSpare(int healthy)
        {
            return MathF.Max(0, healthy - GarrisonKeepFloorMen);
        }

        // ------------------------------------------------------------------ boost

        /// <summary>A town's Guard House level, 0 for a castle (the castle Guard House is a gaol, not a patrol house) or a town without one.</summary>
        public static int GuardHouseLevel(Settlement settlement)
        {
            if (settlement == null || !settlement.IsTown)
            {
                return 0;
            }
            return BuildingEffects.Tier(settlement.Town, DefaultBuildingTypes.SettlementGuardHouse, null);
        }

        /// <summary>Whether this is a port town whose owner's kingdom holds the Coastal Guard Edict, read by its string id.</summary>
        public static bool HasCoastalEdict(Settlement settlement)
        {
            if (settlement == null || !settlement.IsTown || !settlement.HasPort)
            {
                return false;
            }
            Kingdom kingdom = settlement.OwnerClan != null ? settlement.OwnerClan.Kingdom : null;
            if (kingdom == null || kingdom.ActivePolicies == null)
            {
                return false;
            }
            foreach (PolicyObject policy in kingdom.ActivePolicies)
            {
                if (policy != null && policy.StringId == CoastalGuardEdictPolicyId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether whoever runs the fief's patrols has the Scouting perk Patrols: its governor, or the owner clan's
        /// leader when no governor sits there.
        /// </summary>
        public static bool HasPatrolsPerk(Settlement settlement)
        {
            if (settlement == null || settlement.Town == null)
            {
                return false;
            }
            Hero hero = settlement.Town.Governor;
            if (hero == null && settlement.OwnerClan != null)
            {
                hero = settlement.OwnerClan.Leader;
            }
            return hero != null && hero.IsAlive && hero.GetPerkValue(DefaultPerks.Scouting.Patrols);
        }

        /// <summary>The fief's patrol boost B -- see the class summary.</summary>
        public static float Boost(Settlement settlement)
        {
            return Boost(GuardHouseLevel(settlement), HasCoastalEdict(settlement), HasPatrolsPerk(settlement));
        }

        internal static float Boost(int guardHouseLevel, bool coastalEdict, bool patrolsPerk)
        {
            float boost = 1f + GuardHouseBoostPerLevel * guardHouseLevel;
            if (coastalEdict)
            {
                boost += CoastalEdictBoost;
            }
            if (patrolsPerk)
            {
                boost += PatrolsPerkBoost;
            }
            return boost;
        }

        /// <summary>The template level the boost buys: <c>1 + floor((B − 1) / 0.5)</c>, clamped to 1..<see cref="MaxTemplateLevel"/>.</summary>
        public static int TemplateLevel(Settlement settlement)
        {
            return TemplateLevel(Boost(settlement));
        }

        internal static int TemplateLevel(float boost)
        {
            // The epsilon keeps 1.5 / 0.5 from landing a hair under 1 after float addition.
            int level = 1 + (int)((boost - 1f) / TemplateLevelStep + 0.0001f);
            return MathF.Max(1, MathF.Min(MaxTemplateLevel, level));
        }

        // ------------------------------------------------------------------ funding pot

        /// <summary>
        /// The money that funds the patrol: the treasury, plus a town's citizens' money above the garrison
        /// subsidy's reserve floor (<see cref="GarrisonSubsidy.CitizenCapacity"/>). The same pot, in the same
        /// order, that <see cref="DebitPot"/> spends, so the gates judge exactly what can be drawn.
        /// </summary>
        public static int FundingPot(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0;
            }
            long pot = (long)SettlementWealth.GetSettlementWealth(settlement) + GarrisonSubsidy.CitizenCapacity(settlement);
            return pot > int.MaxValue ? int.MaxValue : (int)pot;
        }

        /// <summary>
        /// The funding pot averaged over <see cref="PotAverageDays"/> days -- what the patrol's size and its keep and
        /// raise gates read, so a few days' swing in a town's citizen money neither stands a patrol down nor trims it.
        /// Spending (wages, kit) still checks and draws the live <see cref="FundingPot"/>. A fief the daily pass has
        /// not reached yet this session reads its live pot.
        /// </summary>
        public static int AveragePot(Settlement settlement)
        {
            if (settlement != null && _averagePot.TryGetValue(settlement.StringId, out int average))
            {
                return average;
            }
            return FundingPot(settlement);
        }

        /// <summary>Steps a fief's <see cref="AveragePot"/> one day toward its live pot. Daily pass, main thread.</summary>
        private static void StepAveragePot(Settlement settlement)
        {
            int pot = FundingPot(settlement);
            if (_averagePot.TryGetValue(settlement.StringId, out int average))
            {
                _averagePot[settlement.StringId] = StepAveragePot(average, pot);
            }
            else
            {
                _averagePot[settlement.StringId] = pot;
            }
        }

        internal static int StepAveragePot(int average, int livePot)
        {
            double next = average + (livePot - (double)average) / PotAverageDays;
            return (int)System.Math.Round(next);
        }

        /// <summary>
        /// Saves and loads the averaged pots with the settlement-wealth behaviour's data. A save from before this key
        /// has none, so each fief starts its average from its live pot on its first day, as a new campaign does.
        /// </summary>
        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_patrolAveragePot", ref _averagePot);
            if (_averagePot == null)
            {
                _averagePot = new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Draws <paramref name="amount"/> from the funding pot -- the treasury first, then a town's citizen
        /// surplus -- and returns what it could give, split by purse.
        /// </summary>
        private static int DebitPot(Settlement settlement, int amount, string source, out int fromTreasury)
        {
            fromTreasury = 0;
            if (settlement == null || amount <= 0)
            {
                return 0;
            }
            fromTreasury = SettlementWealth.Debit(settlement, amount, source);
            int rest = amount - fromTreasury;
            int fromCitizens = 0;
            if (rest > 0)
            {
                int surplus = MathF.Min(rest, GarrisonSubsidy.CitizenCapacity(settlement));
                if (surplus > 0)
                {
                    fromCitizens = SettlementWealth.DebitCitizens(settlement, surplus, source);
                }
            }
            return fromTreasury + fromCitizens;
        }

        /// <summary>
        /// Pays one bought patrolman's kit out of the funding pot (<see cref="DebitPot"/>: treasury first, then a
        /// town's citizen surplus), booked <c>PatrolRecruit</c>. In a town the treasury's share reaches the armourers
        /// who kitted him (the citizens), as a garrison recruit's does; the citizens' own share, and a castle's coin,
        /// buy the gear from outside and leave the ledger. Returns what was paid. The patrol's counterpart of
        /// <see cref="GarrisonRecruitCost.PayRecruitKit"/>, which pays from the treasury alone.
        /// </summary>
        private static int PayRecruitKit(Settlement settlement, int cost)
        {
            int paid = DebitPot(settlement, cost, SettlementWealth.Source.PatrolRecruit, out int fromTreasury);
            if (fromTreasury > 0 && settlement.IsTown)
            {
                SettlementWealth.CreditCitizens(settlement, fromTreasury, SettlementWealth.Source.PatrolRecruit);
            }
            return paid;
        }

        /// <summary>The town that mends the patrol's kit: the town itself, or the nearest friendly town to a castle's patrol.</summary>
        private static Settlement MaintenanceMarket(Settlement settlement, MobileParty patrolParty)
        {
            if (settlement.IsTown)
            {
                return RecruitSupply.GetSupplyMarket(settlement);
            }
            Town town = UpgradeSupply.FindNearestFriendlyTown(patrolParty);
            return town != null ? town.Settlement : null;
        }

        // ------------------------------------------------------------------ size

        /// <summary>The troop a patrol is priced off when it has no template: the owner culture's basic soldier, else the settlement culture's.</summary>
        public static CharacterObject RepresentativeTroop(Settlement settlement)
        {
            if (settlement == null)
            {
                return null;
            }
            CultureObject owner = settlement.OwnerClan != null ? settlement.OwnerClan.Culture : null;
            CharacterObject troop = owner != null ? owner.BasicTroop : null;
            if (troop == null && settlement.Culture != null)
            {
                troop = settlement.Culture.BasicTroop;
            }
            if (troop == null && owner != null)
            {
                troop = owner.MeleeMilitiaTroop;
            }
            return troop;
        }

        /// <summary>
        /// What one patrolman costs the fief a day, wage plus kit maintenance: the average man of the template the
        /// fief draws from (weighted by each stack's max value), so a Guard House's better patrol is priced as the
        /// dearer company it is. Falls back to the <see cref="RepresentativeTroop"/> when there is no template.
        /// </summary>
        public static int PerManDaily(Settlement settlement)
        {
            if (Campaign.Current == null)
            {
                return 0;
            }
            PartyWageModel wageModel = Campaign.Current.Models.PartyWageModel;
            PartyTemplateObject template = TemplateFor(settlement);
            if (template != null && template.Stacks != null)
            {
                long cost = 0;
                int men = 0;
                foreach (PartyTemplateStack stack in template.Stacks)
                {
                    int weight = MathF.Max(stack.MinValue, stack.MaxValue);
                    if (stack.Character == null || stack.Character.IsHero || weight <= 0)
                    {
                        continue;
                    }
                    cost += (long)(wageModel.GetCharacterWage(stack.Character)
                        + SpoilsPool.GetDailyMaintenanceCost(stack.Character, 1)) * weight;
                    men += weight;
                }
                if (men > 0)
                {
                    return MathF.Max(1, (int)((cost + men / 2) / men));
                }
            }
            CharacterObject troop = RepresentativeTroop(settlement);
            if (troop == null)
            {
                return 0;
            }
            return wageModel.GetCharacterWage(troop) + SpoilsPool.GetDailyMaintenanceCost(troop, 1);
        }

        /// <summary>The fief's daily patrol budget: pot × <see cref="BudgetFraction"/> × B.</summary>
        public static int DailyBudget(Settlement settlement)
        {
            return (int)(AveragePot(settlement) * BudgetFraction * Boost(settlement));
        }

        /// <summary>The size ceiling: min(<see cref="MaxSizeCap"/>, 20 × B), never under <see cref="MinSize"/>.</summary>
        public static int MaxSize(Settlement settlement)
        {
            return MaxSize(Boost(settlement));
        }

        internal static int MaxSize(float boost)
        {
            int max = MathF.Min(MaxSizeCap, MathF.Round(MaxSizePerBoost * boost));
            return MathF.Max(MinSize, max);
        }

        /// <summary>
        /// The size <see cref="ExplainSize"/> would give with <paramref name="pot"/> in the funding pot instead of what
        /// is there now: floor(pot × <see cref="BudgetFraction"/> × B / per-man cost), clamped to
        /// [<see cref="MinSize"/>, <see cref="MaxSize"/>]. <see cref="Fill"/> reads it so a man is never bought whose
        /// price would shrink the patrol below the head count he joins -- the money spent on kit is money the size
        /// was worked from, and buying past that point only had the surplus trimmed back the same day.
        /// </summary>
        internal static int SizeAtPot(long pot, float budgetFraction, float boost, int perMan)
        {
            if (perMan <= 0)
            {
                return MinSize;
            }
            int men = (int)(pot * budgetFraction * boost / perMan);
            return MathF.Max(MinSize, MathF.Min(MaxSize(boost), men));
        }

        /// <summary>
        /// Whether <see cref="Fill"/> may buy one more man at <paramref name="cost"/>: the pot holds
        /// <see cref="GarrisonRecruitCost.GarrisonSpawnReserveMult"/> times his price, stays at the keep reserve after
        /// it, and the size left once his kit is paid (<paramref name="sizeAfterPrice"/>) still covers the
        /// <paramref name="headCount"/> he makes -- otherwise the next trim sends him (or another man) home.
        /// </summary>
        internal static bool CanAffordMan(long pot, int cost, int keepFloor, int sizeAfterPrice, int headCount)
        {
            return cost > 0
                && pot >= (long)cost * GarrisonRecruitCost.GarrisonSpawnReserveMult
                && pot - cost >= keepFloor
                && sizeAfterPrice >= headCount;
        }

        /// <summary>The patrol the fief's funds can keep, in men. Read off <see cref="ExplainSize"/> so the number and its tooltip never differ.</summary>
        public static int PatrolSize(Settlement settlement)
        {
            return (int)ExplainSize(settlement, false).ResultNumber;
        }

        /// <summary>
        /// The patrol's size limit with its breakdown: what the settlement's funds pay for at B = 1, then the men the
        /// Guard House, the Coastal Guard Edict and the Patrols perk each add, then the clamp to the
        /// [<see cref="MinSize"/>, <see cref="MaxSize"/>] band as its own line. The lines are whole men that sum
        /// to floor(budget / per-man cost) before the clamp -- the same figure <see cref="PatrolSize"/> returns.
        /// </summary>
        public static ExplainedNumber ExplainSize(Settlement settlement, bool includeDescriptions)
        {
            int perMan = PerManDaily(settlement);
            int pot = AveragePot(settlement);
            TextObject fundsText = null;
            if (includeDescriptions)
            {
                fundsText = new TextObject("{=RBM_patrol_funds}Settlement funds ({POT} on hand over the week, {COST} a man a day)");
                fundsText.SetTextVariable("POT", pot);
                fundsText.SetTextVariable("COST", perMan);
            }
            if (perMan <= 0)
            {
                return new ExplainedNumber(MinSize, includeDescriptions, MinSizeText);
            }

            // Men the plain budget (B = 1) pays for, and what each boost adds on top. Each line takes the whole men
            // its share brings the running total to, so the parts add up to the floor of the whole.
            float baseMen = pot * BudgetFraction / perMan;
            int prev = (int)baseMen;
            ExplainedNumber result = new ExplainedNumber(prev, includeDescriptions, fundsText);
            float running = baseMen;
            AddBoostLine(ref result, ref running, ref prev, baseMen * GuardHouseBoostPerLevel * GuardHouseLevel(settlement), GuardHouseText);
            AddBoostLine(ref result, ref running, ref prev, HasCoastalEdict(settlement) ? baseMen * CoastalEdictBoost : 0f, CoastalEdictText);
            AddBoostLine(ref result, ref running, ref prev, HasPatrolsPerk(settlement) ? baseMen * PatrolsPerkBoost : 0f, PatrolsPerkText);

            int max = MaxSize(settlement);
            if (prev < MinSize)
            {
                result.Add(MinSize - prev, MinSizeText);
            }
            else if (prev > max)
            {
                TextObject capText = new TextObject("{=RBM_patrol_cap}Most the fief can command (cap {MAX})");
                capText.SetTextVariable("MAX", max);
                result.Add(max - prev, capText);
            }
            return result;
        }

        private static void AddBoostLine(ref ExplainedNumber result, ref float running, ref int prev, float men, TextObject label)
        {
            if (men <= 0f)
            {
                return;
            }
            running += men;
            int now = (int)running;
            if (now > prev)
            {
                result.Add(now - prev, label);
                prev = now;
            }
        }

        // ------------------------------------------------------------------ template & roster

        /// <summary>The land patrol template the fief's model hands out (RBM's level pick, through the model chain).</summary>
        public static PartyTemplateObject TemplateFor(Settlement settlement)
        {
            if (settlement == null || Campaign.Current == null)
            {
                return null;
            }
            return Campaign.Current.Models.SettlementPatrolModel.GetPartyTemplateForPatrolParty(settlement, false);
        }

        /// <summary>
        /// The template for the fief's level: <c>patrol_party_&lt;owner culture&gt;_template_level_N</c>, falling back
        /// down the levels, then to <paramref name="fallback"/> (vanilla's own pick), then the owner culture's and
        /// the settlement culture's weak patrol. Null only when none of those exists.
        /// </summary>
        public static PartyTemplateObject PickTemplate(Settlement settlement, PartyTemplateObject fallback)
        {
            CultureObject culture = settlement.OwnerClan != null ? settlement.OwnerClan.Culture : null;
            if (culture != null && MBObjectManager.Instance != null)
            {
                for (int level = TemplateLevel(settlement); level >= 1; level--)
                {
                    PartyTemplateObject template = MBObjectManager.Instance.GetObject<PartyTemplateObject>(
                        "patrol_party_" + culture.StringId + "_template_level_" + level);
                    if (IsUsableLandTemplate(template))
                    {
                        return template;
                    }
                }
            }
            if (IsUsableLandTemplate(fallback))
            {
                return fallback;
            }
            if (culture != null && IsUsableLandTemplate(culture.SettlementPatrolPartyTemplateWeak))
            {
                return culture.SettlementPatrolPartyTemplateWeak;
            }
            if (settlement.Culture != null && IsUsableLandTemplate(settlement.Culture.SettlementPatrolPartyTemplateWeak))
            {
                return settlement.Culture.SettlementPatrolPartyTemplateWeak;
            }
            return null;
        }

        /// <summary>A template with men in it and no ships -- one with ship hulls would raise a naval patrol from the land path.</summary>
        private static bool IsUsableLandTemplate(PartyTemplateObject template)
        {
            return template != null && template.Stacks != null && template.Stacks.Count > 0
                && (template.ShipHulls == null || template.ShipHulls.Count == 0);
        }

        /// <summary>
        /// The template scaled to <paramref name="size"/> men: each stack's share in proportion to its max value,
        /// the rounding left over handed out by largest remainder, so the counts sum to exactly the size.
        /// </summary>
        public static List<KeyValuePair<CharacterObject, int>> TargetRoster(PartyTemplateObject template, int size)
        {
            List<KeyValuePair<CharacterObject, int>> result = new List<KeyValuePair<CharacterObject, int>>();
            if (template == null || template.Stacks == null || size <= 0)
            {
                return result;
            }
            List<CharacterObject> characters = new List<CharacterObject>();
            List<int> weights = new List<int>();
            int totalWeight = 0;
            foreach (PartyTemplateStack stack in template.Stacks)
            {
                int weight = MathF.Max(stack.MinValue, stack.MaxValue);
                if (stack.Character == null || stack.Character.IsHero || weight <= 0)
                {
                    continue;
                }
                characters.Add(stack.Character);
                weights.Add(weight);
                totalWeight += weight;
            }
            if (totalWeight <= 0)
            {
                return result;
            }

            int[] counts = new int[characters.Count];
            float[] remainders = new float[characters.Count];
            int assigned = 0;
            for (int i = 0; i < characters.Count; i++)
            {
                float exact = (float)size * weights[i] / totalWeight;
                counts[i] = (int)exact;
                remainders[i] = exact - counts[i];
                assigned += counts[i];
            }
            // At most one leftover man per stack, so each stack is topped up at most once.
            while (assigned < size)
            {
                int best = -1;
                for (int i = 0; i < remainders.Length; i++)
                {
                    if (best < 0 || remainders[i] > remainders[best])
                    {
                        best = i;
                    }
                }
                counts[best]++;
                remainders[best] = -1f;
                assigned++;
            }

            // A template may list one troop twice; merge, keeping the template's order.
            Dictionary<CharacterObject, int> merged = new Dictionary<CharacterObject, int>();
            List<CharacterObject> order = new List<CharacterObject>();
            for (int i = 0; i < characters.Count; i++)
            {
                if (counts[i] <= 0)
                {
                    continue;
                }
                if (merged.ContainsKey(characters[i]))
                {
                    merged[characters[i]] += counts[i];
                }
                else
                {
                    merged[characters[i]] = counts[i];
                    order.Add(characters[i]);
                }
            }
            foreach (CharacterObject character in order)
            {
                result.Add(new KeyValuePair<CharacterObject, int>(character, merged[character]));
            }
            return result;
        }

        // ------------------------------------------------------------------ raising & refilling

        /// <summary>
        /// Mans a freshly raised patrol and charges the fief for the men it had to buy. Vanilla conjures it whole
        /// from the template; the party is brand new, so those men are simply stood down and the scaled template
        /// roster is manned for real (<see cref="Fill"/>): garrison men first, the rest bought under the
        /// garrison's rules and arriving with the recruit seed in their purses. The spawn-gate planned the same
        /// fill, so the patrol comes out whole; should it come out empty anyway, it is stood down again.
        /// </summary>
        public static void OnPatrolSpawned(Settlement settlement, MobileParty patrolParty)
        {
            if (!IsEnabled || settlement == null || settlement.Town == null || patrolParty == null || patrolParty.Party == null
                || patrolParty.MemberRoster == null || patrolParty.PatrolPartyComponent == null
                || patrolParty.PatrolPartyComponent.IsNaval)
            {
                return;
            }
            PartyBase party = patrolParty.Party;
            TroopRoster roster = patrolParty.MemberRoster;
            int size = PatrolSize(settlement);

            // Snapshot first: changing the roster while walking it is unsafe.
            List<TroopRosterElement> conjured = new List<TroopRosterElement>();
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character != null && !element.Character.IsHero && element.Number > 0)
                {
                    conjured.Add(element);
                }
            }
            foreach (TroopRosterElement element in conjured)
            {
                roster.AddToCounts(element.Character, -element.Number, false, -element.WoundedNumber);
                SpoilsPool.ClearSpoilsIfStackGone(party, element.Character);
            }

            FillResult fill = Fill(settlement, patrolParty, TargetRoster(TemplateFor(settlement), size), size, true);
            if (roster.TotalManCount <= 0)
            {
                if (PatrolLog.IsEnabled)
                {
                    PatrolLog.Log("SPAWN", settlement, "could not man a patrol, stood down  ·  " + fill.Describe());
                }
                if (patrolParty.IsActive)
                {
                    DestroyPartyAction.Apply(null, patrolParty);
                }
                return;
            }
            patrolParty.PatrolPartyComponent.SortRoster();
            RefreshLimit(patrolParty);

            if (PatrolLog.IsEnabled)
            {
                PatrolLog.Log("SPAWN", settlement, "raised " + roster.TotalManCount + " men (size " + size
                    + ", L" + TemplateLevel(settlement) + ", B " + Boost(settlement).ToString("0.00")
                    + ")  ·  " + fill.Describe() + "  ·  pot now " + FundingPot(settlement) + "d");
            }
        }

        /// <summary>
        /// Mans the template slots in <paramref name="needs"/> (troop, men wanted), at most <paramref name="room"/>
        /// men in all, and returns how. With <paramref name="apply"/> false nothing changes -- it is the
        /// spawn-gate's plan, tracking the pot and recruit pool itself (conservatively: every bought man takes his
        /// full price off the plan's pot, though in a town the treasury's share comes back as the citizens' surplus,
        /// and the pool's per-man cost is held at today's, though it eases as men leave the garrison) -- so the gate
        /// and the raise walk the same rules in the same order and the raise never does worse than the plan. Each slot:
        ///   1. healthy garrison men of the slot's own troop;
        ///   2. healthy garrison men of the same tier and kind (<see cref="SameKind"/>), largest stacks first, who
        ///      keep their own troop id;
        ///   3. healthy garrison men of the same kind up to <see cref="GarrisonLowerTierSteps"/> tiers lower, nearest
        ///      tier first, then largest stacks;
        ///   4. the rest bought as the slot's troop, man by man: the garrison's price
        ///      (<see cref="GarrisonRecruitCost.SpawnCostFor"/>) and recruit pool, but the FUNDING POT must hold
        ///      <see cref="GarrisonRecruitCost.GarrisonSpawnReserveMult"/> times the price and stay at the keep
        ///      reserve after it -- the money the size was worked from, not the treasury alone. Paid from the pot
        ///      (<see cref="PayRecruitKit"/>), booked as <c>PatrolRecruit</c>. Buying stops for good at the first man
        ///      the money or the pool refuses.
        /// The garrison is never taken below <see cref="GarrisonKeepFloor"/> healthy men, and its men carry their
        /// share of their stack's purse and experience with them.
        /// </summary>
        private static FillResult Fill(Settlement settlement, MobileParty patrolParty, List<KeyValuePair<CharacterObject, int>> needs, int room, bool apply)
        {
            FillResult result = new FillResult();
            if (settlement == null || settlement.Town == null || needs == null)
            {
                return result;
            }

            // The garrison's healthy men by troop, largest stacks first, and how many it can spare above its floor.
            MobileParty garrison = settlement.Town.GarrisonParty;
            Dictionary<CharacterObject, int> garrisonHealthy = new Dictionary<CharacterObject, int>();
            List<CharacterObject> garrisonOrder = new List<CharacterObject>();
            int spare = 0;
            if (garrison != null && garrison.Party != null && garrison.MemberRoster != null)
            {
                TroopRoster garrisonRoster = garrison.MemberRoster;
                int healthy = 0;
                for (int i = 0; i < garrisonRoster.Count; i++)
                {
                    TroopRosterElement element = garrisonRoster.GetElementCopyAtIndex(i);
                    int men = element.Number - element.WoundedNumber;
                    if (element.Character == null || element.Character.IsHero || men <= 0)
                    {
                        continue;
                    }
                    garrisonHealthy[element.Character] = men;
                    garrisonOrder.Add(element.Character);
                    healthy += men;
                }
                garrisonOrder.Sort((a, b) => garrisonHealthy[b].CompareTo(garrisonHealthy[a]));
                spare = GarrisonSpare(healthy);
                result.GarrisonHealthy = healthy;
                result.GarrisonSpare = spare;
            }

            // The buying state the plan tracks for itself; applying reads the live figures instead.
            int planPot = FundingPot(settlement);
            int planPool = RecruitPool.GarrisonAvailable(settlement);
            int keepFloor = KeepFloor(settlement);
            int perMan = PerManDaily(settlement);
            float boost = Boost(settlement);
            // The men already in the patrol before this fill (none for a fresh raise, whose template men are
            // cleared first), so the size each purchase leaves can be checked against the head count it makes.
            int startMen = patrolParty != null && patrolParty.MemberRoster != null ? patrolParty.MemberRoster.TotalManCount : 0;
            // The averaged pot the size is worked from, less what this fill has spent on kit so far.
            long sizePot = AveragePot(settlement);
            bool poolDry = false;
            bool fundsOut = false;

            foreach (KeyValuePair<CharacterObject, int> need in needs)
            {
                if (room <= 0)
                {
                    break;
                }
                CharacterObject troop = need.Key;
                int wanted = MathF.Min(need.Value, room);
                if (troop == null || troop.IsHero || wanted <= 0)
                {
                    continue;
                }
                int missing = wanted;

                // 1. The slot's own troop out of the garrison.
                int taken = TakeFromGarrison(garrison, patrolParty, troop, missing, garrisonHealthy, ref spare, apply);
                // 2. Garrison men of the same tier and kind.
                foreach (CharacterObject other in garrisonOrder)
                {
                    if (missing - taken <= 0 || spare <= 0)
                    {
                        break;
                    }
                    if (other == troop || other.Tier != troop.Tier || !SameKind(other, troop))
                    {
                        continue;
                    }
                    taken += TakeFromGarrison(garrison, patrolParty, other, missing - taken, garrisonHealthy, ref spare, apply);
                }
                // 3. Garrison men of the same kind a tier (or up to GarrisonLowerTierSteps) lower, nearest first.
                for (int step = 1; step <= GarrisonLowerTierSteps; step++)
                {
                    foreach (CharacterObject other in garrisonOrder)
                    {
                        if (missing - taken <= 0 || spare <= 0)
                        {
                            break;
                        }
                        if (other == troop || other.Tier != troop.Tier - step || !SameKind(other, troop))
                        {
                            continue;
                        }
                        taken += TakeFromGarrison(garrison, patrolParty, other, missing - taken, garrisonHealthy, ref spare, apply);
                    }
                }
                result.FromGarrison += taken;
                missing -= taken;

                // 4. Bought, as the slot's troop, out of the funding pot.
                if (missing > 0 && !poolDry && !fundsOut)
                {
                    int cost = GarrisonRecruitCost.SpawnCostFor(settlement, troop);
                    int bought = 0;
                    while (bought < missing)
                    {
                        int pot = apply ? FundingPot(settlement) : planPot;
                        // The pot must afford him, and still pay for a patrol of the size he makes once his kit is
                        // paid for -- otherwise the fief would trim him (or another man) straight back.
                        int headCount = startMen + result.FromGarrison + result.Bought + bought + 1;
                        if (!CanAffordMan(pot, cost, keepFloor, SizeAtPot(sizePot - cost, BudgetFraction, boost, perMan), headCount))
                        {
                            fundsOut = true;
                            break;
                        }
                        if ((apply ? RecruitPool.GarrisonAvailable(settlement) : planPool) < 1)
                        {
                            poolDry = true;
                            break;
                        }
                        if (apply)
                        {
                            RecruitPool.ConsumeGarrison(settlement);
                            result.Paid += PayRecruitKit(settlement, cost);
                        }
                        else
                        {
                            planPot -= cost;
                            planPool--;
                            result.Paid += cost;
                        }
                        sizePot -= cost;
                        bought++;
                    }
                    if (bought > 0 && apply)
                    {
                        patrolParty.MemberRoster.AddToCounts(troop, bought);
                        SpoilsPool.SeedNewMen(patrolParty.Party, troop, bought);
                    }
                    result.Bought += bought;
                    missing -= bought;
                }

                if (missing > 0)
                {
                    if (poolDry)
                    {
                        result.PoolShort += missing;
                    }
                    else
                    {
                        result.FundsShort += missing;
                    }
                }
                room -= wanted - missing;
            }
            return result;
        }

        /// <summary>
        /// Takes up to <paramref name="wanted"/> healthy men of <paramref name="troop"/> from the garrison, within
        /// its <paramref name="spare"/>, and (when applying) moves them into the patrol with their share of the
        /// stack's experience and spoils purse. Returns how many were taken.
        /// </summary>
        private static int TakeFromGarrison(MobileParty garrison, MobileParty patrolParty, CharacterObject troop, int wanted,
            Dictionary<CharacterObject, int> garrisonHealthy, ref int spare, bool apply)
        {
            if (!garrisonHealthy.TryGetValue(troop, out int healthy))
            {
                return 0;
            }
            int take = MathF.Min(wanted, MathF.Min(healthy, spare));
            if (take <= 0)
            {
                return 0;
            }
            garrisonHealthy[troop] = healthy - take;
            spare -= take;
            if (apply)
            {
                MoveMen(garrison, patrolParty, troop, take, 0);
            }
            return take;
        }

        /// <summary>
        /// Moves <paramref name="count"/> men of <paramref name="troop"/>, <paramref name="wounded"/> of them wounded,
        /// from one party to another with their share of the stack's experience and spoils purse (and fed state).
        /// Clamped to what the stack holds. Returns how many moved.
        /// </summary>
        private static int MoveMen(MobileParty from, MobileParty to, CharacterObject troop, int count, int wounded)
        {
            TroopRoster roster = from.MemberRoster;
            int index = roster.FindIndexOfTroop(troop);
            if (index < 0 || count <= 0)
            {
                return 0;
            }
            TroopRosterElement element = roster.GetElementCopyAtIndex(index);
            count = MathF.Min(count, element.Number);
            wounded = MathF.Max(0, MathF.Min(wounded, MathF.Min(count, element.WoundedNumber)));
            if (count <= 0)
            {
                return 0;
            }
            int xp = element.Number > 0 ? (int)((long)element.Xp * count / element.Number) : 0;
            roster.AddToCounts(troop, -count, false, -wounded, -xp);
            to.MemberRoster.AddToCounts(troop, count, false, wounded, xp);
            // Once the roster has moved them: the purse share is measured against what is left plus who left.
            SpoilsPool.TransferSpoils(from.Party, to.Party, troop, count);
            return count;
        }

        /// <summary>
        /// Moves <paramref name="count"/> men of a patrol's stack back into the garrison, the wounded first (the
        /// garrison rests them, the patrol keeps its able men). Returns how many moved.
        /// </summary>
        private static int ReturnMen(MobileParty patrolParty, MobileParty garrison, CharacterObject troop, int count)
        {
            int index = patrolParty.MemberRoster.FindIndexOfTroop(troop);
            if (index < 0)
            {
                return 0;
            }
            int wounded = patrolParty.MemberRoster.GetElementCopyAtIndex(index).WoundedNumber;
            return MoveMen(patrolParty, garrison, troop, count, wounded);
        }

        /// <summary>The fief's garrison party, created the way vanilla does (as a garrison recruit is armed) when it has none.</summary>
        private static MobileParty EnsureGarrison(Settlement settlement)
        {
            if (settlement == null || settlement.Town == null)
            {
                return null;
            }
            if (settlement.Town.GarrisonParty == null)
            {
                settlement.AddGarrisonParty();
            }
            MobileParty garrison = settlement.Town.GarrisonParty;
            return garrison != null && garrison.Party != null && garrison.MemberRoster != null ? garrison : null;
        }

        /// <summary>
        /// Whether a patrol's men may be moved into its fief's garrison now: neither the patrol nor the fief is in a
        /// battle or a siege, where the rosters belong to the fight.
        /// </summary>
        private static bool CanTouchRosters(Settlement settlement, MobileParty patrolParty)
        {
            if (settlement == null || settlement.Town == null || patrolParty == null || !patrolParty.IsActive
                || patrolParty.MemberRoster == null || patrolParty.MapEvent != null || patrolParty.SiegeEvent != null)
            {
                return false;
            }
            if (settlement.IsUnderSiege || (settlement.Party != null && settlement.Party.MapEvent != null))
            {
                return false;
            }
            MobileParty garrison = settlement.Town.GarrisonParty;
            return garrison == null || garrison.MapEvent == null;
        }

        /// <summary>
        /// Sends every man of the patrol, healthy and wounded, back into its fief's garrison with his experience and
        /// purse share, creating the garrison if the fief has none. The caller has checked <see cref="CanTouchRosters"/>.
        /// Returns how many moved; the party itself is left (empty) for its caller to destroy.
        /// </summary>
        private static int ReturnAllToGarrison(Settlement settlement, MobileParty patrolParty)
        {
            MobileParty garrison = EnsureGarrison(settlement);
            if (garrison == null)
            {
                return 0;
            }
            List<TroopRosterElement> stacks = new List<TroopRosterElement>();
            TroopRoster roster = patrolParty.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character != null && !element.Character.IsHero && element.Number > 0)
                {
                    stacks.Add(element);
                }
            }
            int moved = 0;
            foreach (TroopRosterElement element in stacks)
            {
                moved += MoveMen(patrolParty, garrison, element.Character, element.Number, element.WoundedNumber);
            }
            return moved;
        }

        /// <summary>Same kind of soldier: both mounted or both on foot, and both ranged or both not.</summary>
        private static bool SameKind(CharacterObject a, CharacterObject b)
        {
            return a.IsMounted == b.IsMounted && a.IsRanged == b.IsRanged;
        }

        /// <summary>Tiers above a slot's troop a promoted man may be and still count as filling it.</summary>
        private const int PromotedSlotTiers = 2;

        /// <summary>
        /// The men the patrol is missing from its template roster, slot by slot, and (<paramref name="unmatched"/>)
        /// the men it has that fill no slot. The men are matched to the slots in passes, each pass over every slot
        /// before the next, each man filling at most one slot:
        ///   0. the slot's own troop;
        ///   1. the same tier and kind (<see cref="SameKind"/>) -- a garrison substitute, or a man of another stack;
        ///   2. the same kind, up to <see cref="PromotedSlotTiers"/> tiers higher -- a man promoted in service;
        ///   3. the same kind, up to <see cref="GarrisonLowerTierSteps"/> tiers lower -- a garrison man drawn for a
        ///      slot a tier above his own (<see cref="Fill"/>), so he keeps that slot rather than the first same-kind
        ///      gap pass 5 would hand him;
        ///   4. any troop the slot's troop upgrades into, at any depth (<see cref="UpgradeLine"/>) -- a promoted man
        ///      who changed kind, say a footman turned horseman;
        ///   5. any man of the same kind at any tier -- one from an older template level.
        /// So veterans, lower-tier garrison substitutes and men of another template level keep their slots instead
        /// of reading as gaps to buy anew.
        /// </summary>
        private static List<KeyValuePair<CharacterObject, int>> MissingFromTarget(TroopRoster roster, List<KeyValuePair<CharacterObject, int>> target,
            out Dictionary<CharacterObject, int> unmatched)
        {
            Dictionary<CharacterObject, int> left = new Dictionary<CharacterObject, int>();
            List<CharacterObject> order = new List<CharacterObject>();
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character == null || element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                left[element.Character] = element.Number;
                order.Add(element.Character);
            }

            int[] missing = new int[target.Count];
            for (int s = 0; s < target.Count; s++)
            {
                CharacterObject troop = target[s].Key;
                int have = left.TryGetValue(troop, out int count) ? MathF.Min(count, target[s].Value) : 0;
                if (have > 0)
                {
                    left[troop] = count - have;
                }
                missing[s] = target[s].Value - have;
            }
            HashSet<CharacterObject>[] lines = new HashSet<CharacterObject>[target.Count];
            for (int pass = 1; pass <= 5; pass++)
            {
                for (int s = 0; s < target.Count; s++)
                {
                    CharacterObject troop = target[s].Key;
                    foreach (CharacterObject other in order)
                    {
                        if (missing[s] <= 0)
                        {
                            break;
                        }
                        if (other == troop || left[other] <= 0)
                        {
                            continue;
                        }
                        bool fits;
                        switch (pass)
                        {
                            case 1:
                                fits = other.Tier == troop.Tier && SameKind(other, troop);
                                break;
                            case 2:
                                fits = SameKind(other, troop) && other.Tier > troop.Tier && other.Tier <= troop.Tier + PromotedSlotTiers;
                                break;
                            case 3:
                                fits = SameKind(other, troop) && other.Tier < troop.Tier && other.Tier >= troop.Tier - GarrisonLowerTierSteps;
                                break;
                            case 4:
                                if (lines[s] == null)
                                {
                                    lines[s] = UpgradeLine(troop);
                                }
                                fits = lines[s].Contains(other);
                                break;
                            default:
                                fits = SameKind(other, troop);
                                break;
                        }
                        if (!fits)
                        {
                            continue;
                        }
                        int used = MathF.Min(left[other], missing[s]);
                        left[other] -= used;
                        missing[s] -= used;
                    }
                }
            }

            List<KeyValuePair<CharacterObject, int>> result = new List<KeyValuePair<CharacterObject, int>>();
            for (int s = 0; s < target.Count; s++)
            {
                if (missing[s] > 0)
                {
                    result.Add(new KeyValuePair<CharacterObject, int>(target[s].Key, missing[s]));
                }
            }
            unmatched = new Dictionary<CharacterObject, int>();
            foreach (CharacterObject character in order)
            {
                if (left[character] > 0)
                {
                    unmatched[character] = left[character];
                }
            }
            return result;
        }

        /// <summary>Every troop <paramref name="troop"/> upgrades into, at any depth (itself excluded).</summary>
        private static HashSet<CharacterObject> UpgradeLine(CharacterObject troop)
        {
            HashSet<CharacterObject> line = new HashSet<CharacterObject>();
            Stack<CharacterObject> open = new Stack<CharacterObject>();
            open.Push(troop);
            while (open.Count > 0)
            {
                CharacterObject current = open.Pop();
                if (current == null || current.UpgradeTargets == null)
                {
                    continue;
                }
                foreach (CharacterObject next in current.UpgradeTargets)
                {
                    // The set doubles as the visited mark, so an XML upgrade cycle cannot loop here.
                    if (next != null && next != troop && line.Add(next))
                    {
                        open.Push(next);
                    }
                }
            }
            return line;
        }

        /// <summary>
        /// Replaces vanilla's free top-up of a land patrol resting at home. Vanilla clears the roster and rebuilds
        /// it from the template, wiping out veterans and their purses for nothing; here the men already serving
        /// stay, and only the men missing from the template roster at the current size (<see cref="MissingFromTarget"/>)
        /// are manned, as when the patrol was raised (<see cref="Fill"/>): from the garrison first, the rest bought
        /// man by man out of the funding pot, stopping at the size limit or the moment the next man would take
        /// the pot below the keep reserve -- a refill never costs the fief the patrol itself. A fief that can no
        /// longer keep its patrol refills nothing (the day's keep-gate stands it down). A patrol left under
        /// <see cref="MinSize"/> with nobody to be had is marked to be stood down by the next daily pass. Called
        /// from vanilla's own hourly roll, so the timing is vanilla's.
        /// </summary>
        public static void Replenish(MobileParty patrolParty)
        {
            Settlement settlement = patrolParty != null ? patrolParty.HomeSettlement : null;
            if (settlement == null || settlement.Town == null || patrolParty.Party == null || patrolParty.MemberRoster == null)
            {
                return;
            }
            if (!CanKeepPatrol(settlement))
            {
                return;
            }
            TroopRoster roster = patrolParty.MemberRoster;
            int size = PatrolSize(settlement);
            int room = size - roster.TotalManCount;
            if (room <= 0)
            {
                _pendingStandDown.Remove(patrolParty);
                SetRefillStalled(patrolParty, false);
                return;
            }
            List<KeyValuePair<CharacterObject, int>> missing = MissingFromTarget(roster, TargetRoster(TemplateFor(settlement), size), out _);
            FillResult fill = Fill(settlement, patrolParty, missing, room, true);
            if (fill.Added > 0)
            {
                patrolParty.PatrolPartyComponent.SortRoster();
            }
            // Too few to be worth fielding and nobody to make it up: stood down by the next daily pass.
            if (roster.TotalManCount < MinSize && fill.Short > 0)
            {
                _pendingStandDown.Add(patrolParty);
            }
            else
            {
                _pendingStandDown.Remove(patrolParty);
            }
            // Still too short for vanilla's AI to send it out: stall the refill so it patrols with what it has.
            SetRefillStalled(patrolParty, roster.TotalManCount < size * RefillStallRatio);
            RefreshLimit(patrolParty);

            if (!PatrolLog.IsEnabled || (fill.Added == 0 && fill.Short == 0))
            {
                return;
            }
            if (fill.Added == 0)
            {
                // Nobody to be had: once a day is enough to say so while the patrol waits at home.
                int day = (int)CampaignTime.Now.ToDays;
                if (_shortRefillLogDay.TryGetValue(patrolParty, out int lastDay) && lastDay == day)
                {
                    return;
                }
                _shortRefillLogDay[patrolParty] = day;
            }
            PatrolLog.Log("REFILL", settlement, "+" + fill.Added + " men  ·  " + fill.Describe() + "  ·  now "
                + roster.TotalManCount + "/" + size + (IsRefillStalled(patrolParty) ? "  ·  going out short" : "")
                + "  ·  pot " + FundingPot(settlement) + "d");
        }

        // ------------------------------------------------------------------ stalled refill

        /// <summary>Whether the patrol's last refill left it too short to leave home and it is patrolling short instead.</summary>
        public static bool IsRefillStalled(MobileParty patrolParty)
        {
            return patrolParty != null && _refillStalled.ContainsKey(patrolParty);
        }

        /// <summary>
        /// Marks or clears a stalled refill, holding the limit at the men the patrol has now, and refreshes its
        /// cached size limit (<see cref="RefreshLimit(MobileParty)"/>).
        /// </summary>
        private static void SetRefillStalled(MobileParty patrolParty, bool stalled)
        {
            if (patrolParty == null)
            {
                return;
            }
            bool was = _refillStalled.TryGetValue(patrolParty, out StallInfo old);
            if (stalled)
            {
                int men = MathF.Max(1, patrolParty.MemberRoster != null ? patrolParty.MemberRoster.TotalManCount : 1);
                if (was && old.Men == men)
                {
                    return;
                }
                _refillStalled[patrolParty] = new StallInfo { Since = was ? old.Since : CampaignTime.Now, Men = men };
            }
            else
            {
                if (!was)
                {
                    return;
                }
                _refillStalled.Remove(patrolParty);
            }
            RefreshLimit(patrolParty);
        }

        /// <summary>
        /// A patrol reaching home ends a stalled refill, so its full size limit is back and vanilla's hourly refill
        /// roll (which needs the patrol under strength) can try again; if it still finds nobody, it stalls again
        /// and the patrol goes back out. A patrol bigger than its fief now pays for sends the surplus back into
        /// the garrison (<see cref="TrimSurplus"/>).
        /// </summary>
        public static void OnPatrolEnteredHome(MobileParty patrolParty)
        {
            SetRefillStalled(patrolParty, false);
            if (patrolParty != null && patrolParty.HomeSettlement != null && patrolParty.PatrolPartyComponent != null
                && !patrolParty.PatrolPartyComponent.IsNaval)
            {
                TrimSurplus(patrolParty.HomeSettlement, patrolParty, PatrolSize(patrolParty.HomeSettlement));
            }
        }

        /// <summary>
        /// A land patrol's size limit: the fief's <see cref="ExplainSize"/>, held down to the head count it had when
        /// its refill stalled -- so its strength reads full and vanilla's AI patrols with it instead of keeping it
        /// home, while men it loses after that show as a shortfall and draw it home. The breakdown for the tooltip;
        /// the plain size query reads <see cref="CachedPartyLimit"/>.
        /// </summary>
        public static ExplainedNumber ExplainPartySize(MobileParty patrolParty, bool includeDescriptions)
        {
            ExplainedNumber result = ExplainSize(patrolParty.HomeSettlement, includeDescriptions);
            if (_refillStalled.TryGetValue(patrolParty, out StallInfo stall))
            {
                int limit = (int)result.ResultNumber;
                if (stall.Men < limit)
                {
                    result.Add(stall.Men - limit, StalledText);
                }
            }
            return result;
        }

        /// <summary>
        /// Whether the size-limit patch manages this party: a land patrol of a town or castle while the rework is on.
        /// </summary>
        public static bool IsManagedPatrol(MobileParty mobileParty)
        {
            if (!IsEnabled || mobileParty == null || mobileParty.PatrolPartyComponent == null || mobileParty.PatrolPartyComponent.IsNaval)
            {
                return false;
            }
            Settlement home = mobileParty.HomeSettlement;
            return home != null && home.Town != null && (home.IsTown || home.IsCastle);
        }

        /// <summary>
        /// Recomputes a land patrol's size limit (as <see cref="ExplainPartySize"/> gives it) into the cache the size
        /// query reads, and bumps its roster version when the figure moved, so the engine's own per-version cache
        /// (<c>PartyBase.PartySizeLimit</c>) picks it up. Main thread only.
        /// </summary>
        public static int RefreshLimit(MobileParty patrolParty)
        {
            return RefreshLimit(patrolParty, -1, true);
        }

        /// <param name="size">The fief's <see cref="PatrolSize"/> when the caller has it, else -1.</param>
        /// <param name="bumpVersion">False from inside the engine's own size read, which is already refreshing its cache.</param>
        private static int RefreshLimit(MobileParty patrolParty, int size, bool bumpVersion)
        {
            if (!IsManagedPatrol(patrolParty) || !IsMainThread)
            {
                return 0;
            }
            int limit = size >= 0 ? size : PatrolSize(patrolParty.HomeSettlement);
            if (_refillStalled.TryGetValue(patrolParty, out StallInfo stall) && stall.Men < limit)
            {
                limit = stall.Men;
            }
            bool changed = !_partyLimit.TryGetValue(patrolParty, out int old) || old != limit;
            _partyLimit[patrolParty] = limit;
            if (changed && bumpVersion && patrolParty.MemberRoster != null)
            {
                patrolParty.MemberRoster.UpdateVersion();
            }
            return limit;
        }

        /// <summary>
        /// The size limit the plain size query returns, safe from any thread: the cached figure; on a miss the main
        /// thread computes and caches it, while a worker thread (the parallel movement tick) writes nothing and
        /// reads the head count, so the patrol neither looks over its limit (a speed penalty) nor short (the AI's
        /// pull home) until the main thread fills the cache -- at the latest on the next daily pass.
        /// </summary>
        public static int CachedPartyLimit(MobileParty patrolParty)
        {
            if (_partyLimit.TryGetValue(patrolParty, out int limit))
            {
                return limit;
            }
            if (IsMainThread)
            {
                return MathF.Max(1, RefreshLimit(patrolParty, -1, false));
            }
            int men = patrolParty.MemberRoster != null ? patrolParty.MemberRoster.TotalManCount : 0;
            return MathF.Max(MinSize, men);
        }

        // ------------------------------------------------------------------ trimming & standing down

        /// <summary>
        /// Sends a patrol resting at home that is bigger than its fief's current <paramref name="size"/> back down to
        /// it: the surplus goes into the garrison with its experience and purses, first the men who fill no template
        /// slot (lowest tier first), then the lowest tiers. A stalled patrol is never over its held limit, so only the
        /// fief's size counts. Nothing while the patrol or the fief is in a battle or a siege.
        /// </summary>
        private static void TrimSurplus(Settlement settlement, MobileParty patrolParty, int size)
        {
            if (!IsEnabled || settlement == null || patrolParty == null || patrolParty.CurrentSettlement != settlement
                || size <= 0 || !CanTouchRosters(settlement, patrolParty))
            {
                return;
            }
            TroopRoster roster = patrolParty.MemberRoster;
            int surplus = roster.TotalManCount - size;
            if (surplus <= 0)
            {
                return;
            }
            MobileParty garrison = EnsureGarrison(settlement);
            if (garrison == null)
            {
                return;
            }
            int before = roster.TotalManCount;

            // 1. Men who fill no slot of the template at the current size, lowest tier first.
            MissingFromTarget(roster, TargetRoster(TemplateFor(settlement), size), out Dictionary<CharacterObject, int> unmatched);
            List<CharacterObject> spare = new List<CharacterObject>(unmatched.Keys);
            spare.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            foreach (CharacterObject troop in spare)
            {
                if (surplus <= 0)
                {
                    break;
                }
                surplus -= ReturnMen(patrolParty, garrison, troop, MathF.Min(unmatched[troop], surplus));
            }
            // 2. Then the lowest tier left.
            while (surplus > 0)
            {
                CharacterObject lowest = null;
                for (int i = 0; i < roster.Count; i++)
                {
                    TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                    if (element.Character != null && !element.Character.IsHero && element.Number > 0
                        && (lowest == null || element.Character.Tier < lowest.Tier))
                    {
                        lowest = element.Character;
                    }
                }
                int moved = lowest != null ? ReturnMen(patrolParty, garrison, lowest, surplus) : 0;
                if (moved <= 0)
                {
                    break;
                }
                surplus -= moved;
            }

            int sent = before - roster.TotalManCount;
            if (sent <= 0)
            {
                return;
            }
            patrolParty.PatrolPartyComponent.SortRoster();
            RefreshLimit(patrolParty);
            if (PatrolLog.IsEnabled)
            {
                PatrolLog.Log("TRIM", settlement, sent + " men sent back to the garrison (over size " + size + ")  ·  now "
                    + roster.TotalManCount + "/" + size);
            }
        }

        /// <summary>
        /// Stands down a patrol that cannot be manned to <see cref="MinSize"/>: its men go back into the garrison and
        /// the party is destroyed with vanilla's own action, so vanilla's spawn queue picks the fief up again on its
        /// next daily look. The caller has checked <see cref="CanTouchRosters"/>.
        /// </summary>
        private static void StandDownShort(Settlement settlement, MobileParty patrolParty)
        {
            int men = patrolParty.MemberRoster.TotalManCount;
            int returned = ReturnAllToGarrison(settlement, patrolParty);
            if (PatrolLog.IsEnabled)
            {
                PatrolLog.Log("DISBAND", settlement, men + " men stood down  ·  " + returned
                    + " men returned to the garrison  ·  under " + MinSize + " men and nobody to refill it");
            }
            if (patrolParty.IsActive)
            {
                DestroyPartyAction.Apply(null, patrolParty);
            }
        }

        // ------------------------------------------------------------------ speed

        /// <summary>
        /// Adds <see cref="SpeedBonus"/> to a land patrol's base map speed, as its own line on the speed tooltip.
        /// Naval patrols, and anything at sea, keep their speed.
        /// </summary>
        public static void AddSpeedBonus(MobileParty mobileParty, ref ExplainedNumber speed)
        {
            if (!IsEnabled || mobileParty == null || !mobileParty.IsPatrolParty || mobileParty.IsCurrentlyAtSea
                || mobileParty.PatrolPartyComponent == null || mobileParty.PatrolPartyComponent.IsNaval)
            {
                return;
            }
            speed.Add(SpeedBonus, SpeedText);
        }

        // ------------------------------------------------------------------ daily pay

        /// <summary>The spoils wage pass's hook for patrols: each stack billed by <see cref="PayPatrolUpkeep"/>.</summary>
        internal static readonly SpoilsPool.IWageBiller WageBiller = new PatrolWageBiller();

        private sealed class PatrolWageBiller : SpoilsPool.IWageBiller
        {
            public bool Bills(MobileParty party)
            {
                return party.IsPatrolParty && IsEnabled;
            }

            public void BillStack(MobileParty party, CharacterObject character, int number, int wage)
            {
                PayPatrolUpkeep(party, character, number, wage);
            }

            public void FinishParty(MobileParty party)
            {
                FlushPayLog(party);
            }
        }

        /// <summary>
        /// Pays a patrol stack's daily upkeep the way a field troop's is met -- a full wage into the men's purses,
        /// then kit-value maintenance out of them and the fief's pot -- with the home settlement standing in for a
        /// leader as the payer. Called once per patrol stack (land or naval) from the wage-into-spoils pass
        /// (<see cref="SpoilsPool"/>) instead of its free deposit, so the wage a patrol banks is one its fief paid.
        /// </summary>
        /// <remarks>
        /// Modelled on <see cref="MilitiaUpkeep.PayMilitiaUpkeep"/>, at a field troop's full rates: a patrol is a
        /// marching company, not a watch on the wall. Only what the pot gives is banked; maintenance is met from
        /// the purse first, the pot for the rest, and the whole of it paid to the town that mends the gear.
        /// </remarks>
        public static void PayPatrolUpkeep(MobileParty patrolParty, CharacterObject character, int number, int fullWage)
        {
            if (!IsEnabled || patrolParty == null || character == null || number <= 0)
            {
                return;
            }
            Settlement settlement = patrolParty.HomeSettlement;
            PartyBase party = patrolParty.Party;
            if (settlement == null || party == null)
            {
                return;
            }

            // Wage leg: the pot -> the men's spoils purse.
            int wagePaid = 0;
            if (fullWage > 0)
            {
                wagePaid = DebitPot(settlement, fullWage, SettlementWealth.Source.Patrol, out int _);
                if (wagePaid > 0)
                {
                    SpoilsPool.AddSpoils(party, character, wagePaid);
                }
            }

            // Maintenance leg: the purse first, then the pot, all of it paid to the mending town.
            int maintenance = SpoilsPool.GetDailyMaintenanceCost(character, number);
            int fromPurse = 0;
            int fromPot = 0;
            if (maintenance > 0)
            {
                fromPurse = MathF.Min(SpoilsPool.GetSpoils(party, character), maintenance);
                if (fromPurse > 0)
                {
                    SpoilsPool.AddSpoils(party, character, -fromPurse);
                }
                int shortfall = maintenance - fromPurse;
                fromPot = shortfall > 0 ? DebitPot(settlement, shortfall, SettlementWealth.Source.Patrol, out int _) : 0;
                int toMarket = fromPurse + fromPot;
                if (toMarket > 0)
                {
                    Settlement market = MaintenanceMarket(settlement, patrolParty);
                    if (market != null)
                    {
                        TroopMarketFeedback.RegisterPurchase(market, null, toMarket, SettlementWealth.Source.Patrol);
                    }
                }
            }

            if (PatrolLog.IsEnabled)
            {
                if (!_payTally.TryGetValue(patrolParty, out PayTally tally))
                {
                    tally = new PayTally();
                    _payTally[patrolParty] = tally;
                }
                tally.WageOwed += MathF.Max(0, fullWage);
                tally.WagePaid += wagePaid;
                tally.MaintOwed += maintenance;
                tally.MaintFromPurse += fromPurse;
                tally.MaintFromPot += fromPot;
            }
        }

        /// <summary>Writes the patrol's one PAY line for the day, once the wage pass has paid all its stacks.</summary>
        public static void FlushPayLog(MobileParty patrolParty)
        {
            if (patrolParty == null || !_payTally.TryGetValue(patrolParty, out PayTally tally))
            {
                return;
            }
            _payTally.Remove(patrolParty);
            if (!PatrolLog.IsEnabled)
            {
                return;
            }
            bool naval = patrolParty.PatrolPartyComponent != null && patrolParty.PatrolPartyComponent.IsNaval;
            PatrolLog.Log("PAY", patrolParty.HomeSettlement, (naval ? "naval " : "") + patrolParty.MemberRoster.TotalManCount
                + " men  ·  wage " + tally.WagePaid + "/" + tally.WageOwed
                + "d  ·  maintenance " + (tally.MaintFromPurse + tally.MaintFromPot) + "/" + tally.MaintOwed
                + "d (purse " + tally.MaintFromPurse + ", pot " + tally.MaintFromPot + ")"
                + "  ·  pot " + FundingPot(patrolParty.HomeSettlement) + "d");
        }

        /// <summary>
        /// What the fief's patrols cost it a day at full pay -- each stack's wage plus its kit maintenance, the land
        /// patrol and a port's naval patrol alike. Added to the garrison-growth reserve
        /// (<see cref="GarrisonRecruitCost.FullDailyBill"/>) so a fief does not recruit away its patrol's money.
        /// </summary>
        public static int DailyBill(Settlement settlement)
        {
            if (!IsEnabled || settlement == null || Campaign.Current == null)
            {
                return 0;
            }
            int bill = 0;
            if (settlement.PatrolParty != null)
            {
                bill += PartyBill(settlement.PatrolParty.MobileParty);
            }
            if (settlement.HasPort)
            {
                foreach (MobileParty patrol in MobileParty.AllPatrolParties)
                {
                    if (patrol != null && patrol.HomeSettlement == settlement && patrol.PatrolPartyComponent != null
                        && patrol.PatrolPartyComponent.IsNaval)
                    {
                        bill += PartyBill(patrol);
                    }
                }
            }
            return bill;
        }

        private static int PartyBill(MobileParty patrolParty)
        {
            if (patrolParty == null || !patrolParty.IsActive || patrolParty.MemberRoster == null)
            {
                return 0;
            }
            int bill = 0;
            TroopRoster roster = patrolParty.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character == null || element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                bill += Campaign.Current.Models.PartyWageModel.GetCharacterWage(element.Character) * element.Number
                    + SpoilsPool.GetDailyMaintenanceCost(element.Character, element.Number);
            }
            return bill;
        }

        // ------------------------------------------------------------------ daily pass & disband

        /// <summary>
        /// The fief's daily look at its patrol, from the settlement-wealth daily pass: stands down a patrol its last
        /// refill left under <see cref="MinSize"/> with nobody to be had, ends a stalled refill once the patrol rests
        /// at home or the stall has run <see cref="RefillStallMaxDays"/>, sends home a resting patrol's surplus
        /// (<see cref="TrimSurplus"/>), refreshes the cached size limit (the funds may have moved it; the engine only
        /// re-reads the limit when the roster changes, and the AI's go-home and refill rolls read it), and logs a
        /// STATE line on a day the patrol's state changed -- whether it has one, can keep one, can raise one, is
        /// short of men, or its template level. Not on a size change alone: the size follows the pot every day.
        /// </summary>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!IsEnabled || settlement == null || !(settlement.IsTown || settlement.IsCastle))
            {
                return;
            }
            // The day's step of the averaged pot every size and gate below reads.
            StepAveragePot(settlement);
            PatrolState state = new PatrolState();
            state.Eligible = IsEligible(settlement);
            if (state.Eligible)
            {
                state.Size = PatrolSize(settlement);
                state.Level = TemplateLevel(settlement);
            }

            MobileParty patrol = settlement.PatrolParty != null ? settlement.PatrolParty.MobileParty : null;
            if (patrol != null && patrol.IsActive)
            {
                bool atHome = patrol.CurrentSettlement == settlement;
                if (_pendingStandDown.Remove(patrol) && atHome && patrol.MemberRoster.TotalManCount < MinSize
                    && CanTouchRosters(settlement, patrol))
                {
                    StandDownShort(settlement, patrol);
                }
                else
                {
                    // A stalled refill ends once the patrol rests at home (vanilla's hourly refill roll then tries
                    // again; finding nobody, it stalls again and the patrol leaves) or after RefillStallMaxDays (its
                    // shortfall shows again and draws it home).
                    if (_refillStalled.TryGetValue(patrol, out StallInfo stall)
                        && (atHome || stall.Since.ElapsedDaysUntilNow >= RefillStallMaxDays))
                    {
                        SetRefillStalled(patrol, false);
                    }
                    if (atHome && state.Eligible)
                    {
                        TrimSurplus(settlement, patrol, state.Size);
                    }
                    if (state.Eligible)
                    {
                        RefreshLimit(patrol, state.Size, true);
                    }
                }
            }

            state.HasPatrol = settlement.PatrolParty != null;
            if (state.Eligible)
            {
                state.CanKeep = CanKeepPatrol(settlement);
                state.CanRaise = state.CanKeep && !state.HasPatrol && CanRaisePatrol(settlement, out state.ShortOfMen);
            }
            bool seen = _lastState.TryGetValue(settlement, out PatrolState last);
            _lastState[settlement] = state;

            if (!PatrolLog.IsEnabled || !state.Eligible)
            {
                return;
            }
            // The size is left out on purpose: it moves with the pot daily, and RefreshLimit above already carries it.
            if (seen && last.HasPatrol == state.HasPatrol && last.CanKeep == state.CanKeep && last.CanRaise == state.CanRaise
                && last.ShortOfMen == state.ShortOfMen && last.Level == state.Level)
            {
                return;
            }
            int garrisonHealthy = GarrisonHealthyMen(settlement);
            int garrisonSpare = GarrisonSpare(garrisonHealthy);
            string patrolText = state.HasPatrol
                ? ("patrol " + settlement.PatrolParty.MobileParty.MemberRoster.TotalManCount + "/" + state.Size)
                : (state.CanRaise ? "no patrol, can raise"
                    : (!state.CanKeep ? "no patrol, cannot fund"
                        : (state.ShortOfMen ? "no patrol, too few men to raise one" : "no patrol, saving to raise")));
            PatrolLog.Log("STATE", settlement, patrolText + "  ·  pot " + FundingPot(settlement)
                + "d, week avg " + AveragePot(settlement) + "d (keep " + KeepFloor(settlement) + ")  ·  budget " + DailyBudget(settlement)
                + "d/day at " + PerManDaily(settlement) + "d a man  ·  B " + Boost(settlement).ToString("0.00")
                + ", L" + state.Level + ", max " + MaxSize(settlement)
                + "  ·  garrison " + garrisonHealthy + " healthy, " + garrisonSpare + " spare");
        }

        /// <summary>Marks that vanilla's owner-change listener is running for a fief changing hands this way.</summary>
        public static void BeginOwnerChange(ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            _ownerChangeDetail = detail;
        }

        /// <summary>Clears the mark set by <see cref="BeginOwnerChange"/>.</summary>
        public static void EndOwnerChange()
        {
            _ownerChangeDetail = null;
        }

        /// <summary>
        /// Whether a change of owner takes the fief by force -- a siege or a rebellion -- in which case its patrol's
        /// men are lost with it, as in vanilla, rather than handed to the captor's garrison. Every other change (a
        /// gift, a barter, a king's grant or decision, a clan leaving its kingdom or dying out) keeps the garrison,
        /// and the patrol's men join it.
        /// </summary>
        private static bool IsHostileOwnerChange(ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            return detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege
                || detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByRebellion;
        }

        /// <summary>
        /// Just before vanilla destroys a fief's land patrol -- its keep-gate failed, or the fief changed hands --
        /// sends the patrol's men, healthy and wounded, back into the garrison with their experience and purses,
        /// unless the fief was taken by force (<see cref="IsHostileOwnerChange"/>) or the patrol or the fief is in a
        /// battle or a siege. Vanilla then destroys the emptied party. Logs a DISBAND line with the reason.
        /// </summary>
        public static void OnStandingDown(Settlement settlement)
        {
            if (settlement == null || settlement.PatrolParty == null || settlement.PatrolParty.MobileParty == null)
            {
                return;
            }
            MobileParty patrol = settlement.PatrolParty.MobileParty;
            int men = patrol.MemberRoster != null ? patrol.MemberRoster.TotalManCount : 0;
            bool hostile = _ownerChangeDetail.HasValue && IsHostileOwnerChange(_ownerChangeDetail.Value);
            bool canReturn = !hostile && CanTouchRosters(settlement, patrol);
            int returned = canReturn ? ReturnAllToGarrison(settlement, patrol) : 0;
            _pendingStandDown.Remove(patrol);

            if (!PatrolLog.IsEnabled)
            {
                return;
            }
            string reason;
            if (_ownerChangeDetail.HasValue)
            {
                reason = "fief changed hands (" + _ownerChangeDetail.Value + ")";
            }
            else if (!IsEligible(settlement))
            {
                reason = "fief no longer eligible (no owner or a rebel owner)";
            }
            else if (!CanKeepPatrol(settlement))
            {
                reason = "funds below the keep reserve (week avg pot " + AveragePot(settlement) + "d < " + KeepFloor(settlement)
                    + "d, live pot " + FundingPot(settlement) + "d)";
            }
            else
            {
                reason = "stood down by the game";
            }
            string fate = canReturn ? returned + " men returned to the garrison"
                : (hostile ? "men lost with the fief" : "men lost (patrol or fief in a battle or siege)");
            PatrolLog.Log("DISBAND", settlement, men + " men stood down  ·  " + fate + "  ·  " + reason);
        }

        /// <summary>
        /// Gives a land patrol's security bonus to a fief without a Guard House -- every castle (whose Guard House is
        /// the castle building, which vanilla's patrol bonus does not read) and any town that never built one:
        /// vanilla's formula at level 0, under vanilla's own label. A Guard House town already has vanilla's.
        /// </summary>
        public static void AddSecurityBonus(Town town, ref ExplainedNumber result)
        {
            if (!IsEnabled || town == null || town.Settlement == null || town.Settlement.PatrolParty == null
                || GuardHouseLevel(town.Settlement) > 0)
            {
                return;
            }
            result.Add(NoGuardHouseSecurityBonus, TaleWorlds.Core.GameTexts.FindText("str_patrol_parties"));
        }

        /// <summary>Logs a patrol destroyed in the field, and drops its session state.</summary>
        public static void OnPatrolDestroyed(MobileParty patrolParty, PartyBase destroyerParty)
        {
            if (patrolParty == null)
            {
                return;
            }
            _payTally.Remove(patrolParty);
            _shortRefillLogDay.Remove(patrolParty);
            _refillStalled.Remove(patrolParty);
            _pendingStandDown.Remove(patrolParty);
            _partyLimit.TryRemove(patrolParty, out int _);
            if (!PatrolLog.IsEnabled || destroyerParty == null)
            {
                return;
            }
            bool naval = patrolParty.PatrolPartyComponent != null && patrolParty.PatrolPartyComponent.IsNaval;
            PatrolLog.Log("LOST", patrolParty.HomeSettlement, (naval ? "naval patrol" : "patrol") + " destroyed by "
                + (destroyerParty.Name != null ? destroyerParty.Name.ToString() : "?"));
        }
    }
}
