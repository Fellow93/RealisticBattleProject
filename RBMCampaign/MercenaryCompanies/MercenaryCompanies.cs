using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// The neutral mercenary companies: a fixed roster of two companies per main culture, each its OWN clan
    /// (so a contract, which is a clan-level kingdom membership, covers exactly one party) with one leader
    /// and one war party. They are created at runtime the first time a campaign ticks with the feature on, so
    /// existing saves get them too, and are found again by their StringId prefix -- never by a stored
    /// reference -- so creation is idempotent across save/load.
    ///
    /// Persistence: a runtime clan, hero and party live in <c>CampaignObjectManager</c>'s saved lists exactly
    /// like vanilla's own runtime clans (rebel clans, companion clans), so nothing extra is stored for them.
    /// The flags set here by reflection (<c>IsMinorFaction</c>, <c>IsClanTypeMercenary</c>, the party template
    /// and minor-faction templates) are all <c>[SaveableProperty]</c>/<c>[SaveableField]</c> on <see cref="Clan"/>.
    ///
    /// Slot ids: <c>rbm_merc_co_&lt;culture&gt;_&lt;n&gt;_g&lt;generation&gt;</c>. A slot whose company was destroyed
    /// (its only lord killed) is refilled by a new generation after <see cref="RespawnDays"/>; a company whose
    /// party was lost but whose leader lives gets a new party for him after the same delay (once he is free).
    /// Vanilla's minor-faction hero/party spawning is blocked for these clans (MercenaryCompanyPatches), so
    /// this is the only thing that ever spawns them.
    /// </summary>
    public static class MercenaryCompanies
    {
        public const string IdPrefix = "rbm_merc_co_";

        // The main cultures; nord exists only with War Sails and is skipped when absent.
        private static readonly string[] CultureIds = { "empire", "aserai", "battania", "khuzait", "sturgia", "vlandia", "nord" };
        private const int CompaniesPerCulture = 2;

        // Clan tier the company starts at (renown for it is granted on creation). Tier drives the lord
        // party's size limit, so this is what makes a company roughly a mid-tier minor faction's size.
        private const int CompanyTier = 3;

        // Gold the founding leader starts with: enough to pay his men for a while and buy tavern mercenaries.
        private const int StartingGold = 40000;

        // Share of the party size limit a newly spawned company party is filled to (template + culture troops).
        private const float InitialFillShare = 0.6f;

        // Share of the party size limit a company recruits back up to from tavern mercenaries.
        internal const float RecruitTargetShare = 0.8f;

        // Gold a leader always keeps back when buying recruits (his men's wages come out of the same purse).
        internal const int RecruitGoldFloor = 5000;

        // Days a company waits before a lost party (or a destroyed company) is replaced.
        internal const int RespawnDays = 7;

        // Persisted respawn clocks: "party:<clanId>" = day the company was first seen without a party,
        // "slot:<slotKey>" = day the slot was first seen with no living company.
        private static Dictionary<string, int> _timers = new Dictionary<string, int>();

        // Transient, for the log only: the last WAIT reason logged per company, so a wait is logged once per change.
        private static readonly Dictionary<string, string> _waitNotes = new Dictionary<string, string>();

        private static readonly MethodInfo ApplyRecruitMercenaryMethod =
            AccessTools.Method(typeof(RecruitmentCampaignBehavior), "ApplyRecruitMercenary");

        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled && RBMConfig.RBMConfig.mercenaryCompaniesEnabled; }
        }

        /// <summary>A clan of the roster, and the feature is on. Every RBM patch on vanilla gates on this.</summary>
        public static bool IsCompany(Clan clan)
        {
            return clan != null && IsEnabled && clan.StringId != null
                && clan.StringId.StartsWith(IdPrefix, StringComparison.Ordinal);
        }

        public static bool IsCompany(IFaction faction)
        {
            return IsCompany(faction as Clan);
        }

        public static bool IsCompanyParty(MobileParty party)
        {
            return party != null && party.IsLordParty && IsCompany(party.ActualClan);
        }

        /// <summary>The company's one war party, if it is on the map.</summary>
        public static MobileParty ActiveParty(Clan clan)
        {
            if (clan == null)
            {
                return null;
            }
            foreach (WarPartyComponent component in clan.WarPartyComponents)
            {
                MobileParty party = component?.MobileParty;
                if (party != null && party.IsActive)
                {
                    return party;
                }
            }
            return null;
        }

        /// <summary>Every living company. A snapshot, so callers may change kingdoms while iterating.</summary>
        public static List<Clan> LiveCompanies()
        {
            List<Clan> list = new List<Clan>();
            foreach (Clan clan in Clan.All)
            {
                if (!clan.IsEliminated && IsCompany(clan))
                {
                    list.Add(clan);
                }
            }
            return list;
        }

        // ---- Pricing (the contract's numbers, all off the party's actual daily wage) -------------------

        /// <summary>Hire price = this many days of the company's wage bill, paid once to its leader.</summary>
        public const float HirePriceWageDays = 10f;
        /// <summary>Daily pay = this multiple of the company's wage bill, paid every day of the contract.</summary>
        public const float DailyPayWageMultiplier = 1.5f;
        public const int MinimumHirePrice = 1000;

        /// <summary>Priced on the FULL wage bill, never the idle half pay (<see cref="FullWage"/>).</summary>
        public static int HirePrice(MobileParty party)
        {
            return party == null ? 0 : Math.Max(MinimumHirePrice, MathF.Round(HirePriceWageDays * FullWage(party)));
        }

        /// <summary>Priced on the FULL wage bill, never the idle half pay (<see cref="FullWage"/>).</summary>
        public static int DailyPay(MobileParty party)
        {
            return party == null ? 0 : Math.Max(1, MathF.Round(DailyPayWageMultiplier * FullWage(party)));
        }

        // ---- Half pay when idle ----------------------------------------------------------------------
        // A company out of contract pays its men half wages. The cut is made once, on the party's total wage
        // (MercenaryCompanyPatches.IdleHalfPay, a postfix on DefaultPartyWageModel.GetTotalWage), so everything
        // that reads the party's wage follows it: the leader's daily wage expense (DefaultClanFinanceModel reads
        // MobileParty.TotalWage), the party wage tooltip (TotalWageExplained, with its own line), desertion and
        // wage budgets. The spoils deposit is per stack off GetCharacterWage, so it applies WageFactor itself
        // (SpoilsPool.DepositWageSpoils): the men bank exactly what they are paid. Per-man wages
        // (GetCharacterWage) are NOT cut, so troop tooltips, upkeep price ceilings and spoils caps are unchanged.

        /// <summary>Share of wages a company pays: half while idle, all of it under contract; 1 for any other party.</summary>
        public const float IdleWageShare = 0.5f;

        public static float WageFactor(MobileParty party)
        {
            if (!IsCompanyParty(party) || MercenaryContracts.IsHired(party.ActualClan))
            {
                return 1f;
            }
            return IdleWageShare;
        }

        // Set while FullWage reads the party's wage, so the idle cut stays out of contract prices.
        [ThreadStatic]
        internal static bool ReadingFullWage;

        /// <summary>The party's whole daily wage bill, as if under contract: what contracts are priced on.</summary>
        public static int FullWage(MobileParty party)
        {
            if (party == null)
            {
                return 0;
            }
            bool previous = ReadingFullWage;
            ReadingFullWage = true;
            try
            {
                return party.TotalWage;
            }
            finally
            {
                ReadingFullWage = previous;
            }
        }

        // ---- Persistence ---------------------------------------------------------------------------

        internal static void Reset()
        {
            _timers = new Dictionary<string, int>();
            _waitNotes.Clear();
            _disbandDeferLogged.Clear();
        }

        // ---- Disband cleanup (feature switched off mid-save) ---------------------------------------------
        // With mercenaryCompaniesEnabled off and RBM Campaign on, every company is taken apart instead of being
        // left to vanilla's minor-faction AI: out of its kingdom first (never mid-battle/siege), then the whole
        // clan goes through DestroyClanAction.Apply -- vanilla's own clan teardown, which destroys its parties
        // (MobileParty.RemoveParty releases hero prisoners and drops the rest), removes its heroes with
        // KillCharacterAction.ApplyByRemove (no notification; a captive leader is ended in captivity first) and
        // takes it out of every war. Its "clan destroyed" pop-up and log entry and the "made peace" notices of the
        // kingdom exit are suppressed while Disbanding is set (MercenaryCompanyPatches). Runs from the behavior on
        // session launch and hourly until no company is left; retried hourly for a company still in a fight.

        /// <summary>A roster clan by its id, whatever the feature toggle says (the disband cleanup and its patches use this).</summary>
        public static bool HasCompanyId(Clan clan)
        {
            return clan?.StringId != null && clan.StringId.StartsWith(IdPrefix, StringComparison.Ordinal);
        }

        /// <summary>True only while the cleanup moves or destroys a company, so its notices can be suppressed.</summary>
        internal static bool Disbanding { get; private set; }

        /// <summary>Whether the cleanup should run: RBM Campaign on, the feature off.</summary>
        public static bool CleanupActive
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled && !RBMConfig.RBMConfig.mercenaryCompaniesEnabled; }
        }

        private static readonly HashSet<string> _disbandDeferLogged = new HashSet<string>();

        /// <summary>Disbands every living company; returns true when none is left.</summary>
        internal static bool DisbandAll()
        {
            if (!CleanupActive || Campaign.Current == null)
            {
                return true;
            }
            bool allGone = true;
            foreach (Clan clan in Clan.All.ToList())
            {
                if (clan.IsEliminated || !HasCompanyId(clan))
                {
                    continue;
                }
                if (!DisbandOne(clan))
                {
                    allGone = false;
                }
            }
            if (allGone && MercenaryContracts.Count > 0)
            {
                MercenaryContracts.Reset();
                MercenaryLog.LogCleanup("DISBAND", "", "all companies gone; contract store cleared");
            }
            return allGone;
        }

        private static string SlotOf(Clan clan)
        {
            string id = clan.StringId.Substring(IdPrefix.Length);
            int gen = id.LastIndexOf("_g", StringComparison.Ordinal);
            return gen > 0 ? id.Substring(0, gen) : id;
        }

        private static bool InFight(MobileParty party)
        {
            return party != null && (party.MapEvent != null || party.SiegeEvent != null || party.BesiegerCamp != null);
        }

        private static bool DisbandOne(Clan clan)
        {
            MobileParty party = ActiveParty(clan);
            Hero leader = clan.Leader;
            MobileParty captor = leader?.PartyBelongedToAsPrisoner?.MobileParty;
            string wait = InFight(party) ? "its party is in a battle or siege"
                : InFight(captor) ? "its captive leader's captor is in a battle or siege"
                : null;
            if (wait != null)
            {
                if (_disbandDeferLogged.Add(clan.StringId))
                {
                    MercenaryLog.LogCleanup("DISBAND", MercenaryLog.Who(clan), "deferred: " + wait + " (retried hourly)");
                }
                return false;
            }
            _disbandDeferLogged.Remove(clan.StringId);
            string what = (party != null ? party.MemberRoster.TotalManCount + " men, " + party.PrisonRoster.TotalManCount + " prisoners" : "no party")
                + ", leader " + (leader != null ? leader.Name + (leader.IsPrisoner ? " (prisoner)" : "") : "none")
                + ", gold " + clan.Gold + "d";
            Disbanding = true;
            try
            {
                if (clan.Kingdom != null)
                {
                    Kingdom kingdom = clan.Kingdom;
                    MercenaryContracts.Forget(clan);
                    ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(clan, showNotification: false);
                    MercenaryLog.LogCleanup("DISBAND", MercenaryLog.Who(clan), "left " + kingdom.Name + " (feature switched off)");
                }
                DestroyClanAction.Apply(clan);
            }
            finally
            {
                Disbanding = false;
            }
            MercenaryContracts.Forget(clan);
            _timers.Remove("party:" + clan.StringId);
            _waitNotes.Remove(clan.StringId);
            // Due at once: switched back on, this slot gets a new company on the next roster check.
            _timers["slot:" + SlotOf(clan)] = Today - RespawnDays;
            MercenaryLog.LogCleanup("DISBAND", MercenaryLog.Who(clan), "disbanded and destroyed (" + what + ")");
            return true;
        }

        internal static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_mercCompanyTimers", ref _timers);
            if (_timers == null)
            {
                _timers = new Dictionary<string, int>();
            }
        }

        private static int Today
        {
            get { return (int)CampaignTime.Now.ToDays; }
        }

        // ---- Roster -----------------------------------------------------------------------------

        /// <summary>
        /// Creates every slot's company that is missing: at once for a slot that never had one (a new game,
        /// or an older save meeting the feature for the first time), and <see cref="RespawnDays"/> after the
        /// last generation was destroyed otherwise. Idempotent.
        /// </summary>
        internal static void EnsureRoster()
        {
            if (!IsEnabled || Campaign.Current == null)
            {
                return;
            }
            List<Clan> all = Clan.All.ToList();
            foreach (string cultureId in CultureIds)
            {
                CultureObject culture = MBObjectManager.Instance.GetObject<CultureObject>(cultureId);
                if (culture == null)
                {
                    continue;
                }
                for (int n = 0; n < CompaniesPerCulture; n++)
                {
                    string slot = cultureId + "_" + n;
                    string prefix = IdPrefix + slot + "_g";
                    Clan live = null;
                    int generations = 0;
                    foreach (Clan clan in all)
                    {
                        if (clan.StringId != null && clan.StringId.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            generations++;
                            if (!clan.IsEliminated)
                            {
                                live = clan;
                            }
                        }
                    }
                    string key = "slot:" + slot;
                    if (live != null)
                    {
                        _timers.Remove(key);
                        continue;
                    }
                    if (generations > 0)
                    {
                        int since;
                        if (!_timers.TryGetValue(key, out since))
                        {
                            _timers[key] = Today;
                            MercenaryLog.Log("GONE", slot, "no living company in slot " + slot + " (" + generations
                                + " generation(s) so far); a new one in " + RespawnDays + " days");
                            continue;
                        }
                        if (Today - since < RespawnDays)
                        {
                            continue;
                        }
                    }
                    Settlement home = PickHomeTown(culture, n);
                    if (home == null)
                    {
                        continue;
                    }
                    _timers.Remove(key);
                    CreateCompany(culture, n, prefix + generations, home);
                }
            }
        }

        /// <summary>
        /// Gives a company whose party was lost a new one, <see cref="RespawnDays"/> after the loss, once its
        /// leader is free and active again (a captured leader is waited out: vanilla's escape and ransom
        /// release him, and only then does he get a band).
        /// </summary>
        internal static void CheckRespawns()
        {
            if (!IsEnabled)
            {
                return;
            }
            foreach (Clan clan in LiveCompanies())
            {
                string key = "party:" + clan.StringId;
                Hero leader = clan.Leader;
                if (leader == null || !leader.IsAlive || ActiveParty(clan) != null)
                {
                    _timers.Remove(key);
                    _waitNotes.Remove(clan.StringId);
                    continue;
                }
                int since;
                if (!_timers.TryGetValue(key, out since))
                {
                    _timers[key] = Today;
                    since = Today;
                }
                string wait = Today - since < RespawnDays ? "party lost on day " + since + ", respawn from day " + (since + RespawnDays)
                    : leader.IsPrisoner ? "respawn due, leader " + leader.Name + " is a prisoner"
                        + (leader.PartyBelongedToAsPrisoner != null ? " of " + leader.PartyBelongedToAsPrisoner.Name : "")
                    : !leader.IsActive ? "respawn due, leader " + leader.Name + " is " + leader.HeroState
                    : leader.PartyBelongedTo != null ? "respawn due, leader " + leader.Name + " is in " + leader.PartyBelongedTo.Name
                    : null;
                Settlement town = wait == null ? SpawnTown(clan) : null;
                if (wait == null && town == null)
                {
                    wait = "respawn due, no safe town to appear at";
                }
                if (wait != null)
                {
                    string previous;
                    if (!_waitNotes.TryGetValue(clan.StringId, out previous) || previous != wait)
                    {
                        _waitNotes[clan.StringId] = wait;
                        MercenaryLog.Log("WAIT", clan, wait + ", leader gold " + leader.Gold + "d");
                    }
                    continue;
                }
                _timers.Remove(key);
                _waitNotes.Remove(clan.StringId);
                SpawnParty(leader, town, "respawn after " + (Today - since) + " days");
            }
        }

        private static Settlement PickHomeTown(CultureObject culture, int index)
        {
            List<Settlement> towns = Settlement.All
                .Where(s => s.IsTown && s.Culture == culture)
                .OrderBy(s => s.StringId, StringComparer.Ordinal)
                .ToList();
            if (towns.Count == 0)
            {
                return null;
            }
            // Spread the culture's companies across its towns rather than stacking them in one.
            return towns[(index * towns.Count / CompaniesPerCulture) % towns.Count];
        }

        /// <summary>The company's home town, or failing that the nearest town of its culture it can safely appear at.</summary>
        private static Settlement SpawnTown(Clan clan)
        {
            Settlement home = clan.InitialHomeSettlement;
            if (IsSafeTown(home, clan))
            {
                return home;
            }
            Settlement best = null;
            float bestDistance = float.MaxValue;
            foreach (Settlement settlement in Settlement.All)
            {
                if (!IsSafeTown(settlement, clan))
                {
                    continue;
                }
                float distance = home != null ? home.Position.Distance(settlement.Position) : 0f;
                if (settlement.Culture != clan.Culture)
                {
                    distance += 100000f;
                }
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = settlement;
                }
            }
            return best;
        }

        private static bool IsSafeTown(Settlement settlement, Clan clan)
        {
            return settlement != null && settlement.IsTown && !settlement.IsUnderSiege
                && settlement.MapFaction != null && !FactionManager.IsAtWarAgainstFaction(clan.MapFaction, settlement.MapFaction);
        }

        private static void CreateCompany(CultureObject culture, int index, string clanId, Settlement home)
        {
            CharacterObject template = culture.LordTemplates?.FirstOrDefault(x => x.Occupation == Occupation.Lord)
                ?? culture.LordTemplates?.FirstOrDefault();
            if (template == null)
            {
                MercenaryLog.Log("CREATE", clanId, "FAILED: culture " + culture.StringId + " has no lord template");
                return;
            }

            Clan clan = Clan.CreateClan(clanId);
            TextObject name = CompanyName(culture, index);
            clan.ChangeClanName(name, name);
            clan.Culture = culture;
            Banner banner = Banner.CreateRandomClanBanner();
            clan.Banner = banner;
            clan.Color = banner.GetPrimaryColor();
            clan.Color2 = banner.GetFirstIconColor();
            // Remembered so the banner gets its own colours back each time a contract ends and the clan
            // leaves the kingdom whose colours it wore.
            clan.UpdateBannerColor(banner.GetPrimaryColor(), banner.GetFirstIconColor());
            // Minor faction: keeps FactionDiscontinuation from disbanding a landless clan and makes vanilla treat
            // it as a hire-able band. Never IsOutlaw (outlaws are at constant war with same-culture kingdoms).
            AccessTools.Property(typeof(Clan), nameof(Clan.IsMinorFaction)).SetValue(clan, true);
            AccessTools.Property(typeof(Clan), nameof(Clan.IsClanTypeMercenary)).SetValue(clan, true);
            // No party template in the game is all mercenaries, so the culture's is kept only as a non-null value
            // for the code that reads one (naval capability, size models); a company party is never filled from
            // it (SkipTemplateForCompanies), and vanilla's minor-faction spawning that would use it is blocked.
            AccessTools.Field(typeof(Clan), "_defaultPartyTemplate").SetValue(clan, culture.DefaultPartyTemplate);
            // Hero templates (vanilla's minor-faction hero spawn, blocked for companies), not troops.
            AccessTools.Field(typeof(Clan), "_minorFactionCharacterTemplates").SetValue(clan, new MBList<CharacterObject> { template });
            ApplyMercenaryIdentity(clan);
            clan.IsNoble = false;
            clan.SetInitialHomeSettlement(home);
            clan.CalculateMidSettlement();

            Hero hero = HeroCreator.CreateSpecialHero(template, home, clan, null, MBRandom.RandomInt(25, 45));
            hero.ChangeState(Hero.CharacterStates.Active);
            hero.IsMinorFactionHero = true;
            // Vanilla never calls him to an army for influence; only a paid contract puts him in one.
            hero.CanJoinArmy = false;
            clan.SetLeader(hero);
            clan.AddRenown(Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(CompanyTier), false);
            GiveGoldAction.ApplyBetweenCharacters(null, hero, StartingGold, disableNotification: true);
            CampaignEventDispatcher.Instance.OnClanCreated(clan, isCompanion: false);

            MercenaryLog.Log("CREATE", clan, "id " + clan.StringId + ", culture " + culture.StringId + ", home " + home.Name
                + ", tier " + clan.Tier + ", leader " + hero.Name + " (" + hero.StringId + "), gold " + hero.Gold + "d");
            SpawnParty(hero, home, "new company");
        }

        // Set only while SpawnParty creates a company party: the lord party template's men are skipped, so the
        // party starts with its leader alone and is filled from the mercenary pool (no template is all mercenary).
        private static bool _spawning;

        /// <summary>
        /// Spawns the leader's party at the gate of <paramref name="town"/> with no template men, then fills it
        /// with mercenary troops (<see cref="MercenaryPool"/>) to <see cref="InitialFillShare"/> of its size
        /// limit. The fill is given the usual recruit spoils seed.
        /// </summary>
        private static MobileParty SpawnParty(Hero hero, Settlement town, string why)
        {
            MobileParty party;
            _spawning = true;
            try
            {
                party = MobilePartyHelper.SpawnLordParty(hero, town);
            }
            finally
            {
                _spawning = false;
            }
            if (party == null)
            {
                return null;
            }
            Dictionary<CharacterObject, int> before = SpoilsPool.IsEnabled ? SpoilsPool.SnapshotStacks(party.Party) : null;
            int toAdd = (int)(party.Party.PartySizeLimit * InitialFillShare) - party.MemberRoster.TotalManCount;
            string poolNote;
            List<(CharacterObject, float)> pool = MercenaryPool(hero.Clan.Culture, out poolNote);
            if (pool.Count > 0)
            {
                for (int i = 0; i < toAdd; i++)
                {
                    party.MemberRoster.AddToCounts(MBRandom.ChooseWeighted(pool), 1);
                }
            }
            if (before != null)
            {
                SpoilsPool.SeedGrowthSince(party.Party, before);
            }
            MercenaryLog.Log("SPAWN", party, "at " + town.Name + " (" + why + "): " + party.MemberRoster.TotalManCount
                + " men of " + party.Party.PartySizeLimit + " from " + poolNote + ", wage " + party.TotalWage
                + "d/day, hire price " + HirePrice(party) + "d, leader " + hero.Name + " (" + hero.Occupation + ", "
                + hero.CharacterObject.OriginalCharacter?.StringId + "), gold " + hero.Gold + "d");
            return party;
        }

        /// <summary>No template men for a company party being spawned (same seam RBM's LordRespawn uses).</summary>
        [HarmonyPatch(typeof(MobileParty), "InitializeMobilePartyWithPartyTemplate")]
        private static class SkipTemplateForCompanies
        {
            private static bool Prefix(MobileParty __instance, CampaignVec2 position)
            {
                if (!_spawning)
                {
                    return true;
                }
                __instance.InitializeMobilePartyAtPosition(position);
                return false;
            }
        }

        /// <summary>A troop a company may hold: a non-hero of the Mercenary occupation.</summary>
        public static bool IsMercenaryTroop(CharacterObject troop)
        {
            return troop != null && !troop.IsHero && troop.Occupation == Occupation.Mercenary;
        }

        // Fewer distinct troops than this in a culture's own mercenary trees and the pool widens to every one.
        private const int MinimumCulturePool = 3;

        /// <summary>
        /// The troops a company is made of, weighted toward the lower tiers: the mercenary trees the culture's
        /// taverns hire from (<c>CultureObject.BasicMercenaryTroops</c> and their upgrades -- vanilla's
        /// <c>UpdateCurrentMercenaryTroopAndCount</c> pool), Mercenary occupation only; every non-hero Mercenary
        /// troop in the game when the culture has fewer than <see cref="MinimumCulturePool"/>.
        /// </summary>
        public static List<(CharacterObject, float)> MercenaryPool(CultureObject culture, out string note)
        {
            List<(CharacterObject, float)> pool = new List<(CharacterObject, float)>();
            if (culture?.BasicMercenaryTroops != null)
            {
                foreach (CharacterObject root in culture.BasicMercenaryTroops)
                {
                    AddMercenaryTree(pool, root);
                }
            }
            note = (culture != null ? culture.StringId : "?") + " tavern mercenaries";
            if (pool.Count < MinimumCulturePool)
            {
                pool.Clear();
                foreach (CharacterObject troop in CharacterObject.All)
                {
                    if (IsMercenaryTroop(troop) && !troop.IsTemplate && pool.All(p => p.Item1 != troop))
                    {
                        pool.Add((troop, TierWeight(troop)));
                    }
                }
                note = "all mercenary troops";
            }
            note += " (" + pool.Count + " troops)";
            return pool;
        }

        private static float TierWeight(CharacterObject troop)
        {
            return Math.Max(1f, 7f - troop.Tier);
        }

        private static void AddMercenaryTree(List<(CharacterObject, float)> pool, CharacterObject root)
        {
            if (root == null)
            {
                return;
            }
            HashSet<CharacterObject> seen = new HashSet<CharacterObject>();
            Queue<CharacterObject> queue = new Queue<CharacterObject>();
            queue.Enqueue(root);
            seen.Add(root);
            while (queue.Count > 0)
            {
                CharacterObject troop = queue.Dequeue();
                if (!IsMercenaryTroop(troop))
                {
                    // A non-mercenary branch is neither used nor followed (company upgrades into it are blocked too).
                    continue;
                }
                if (pool.All(p => p.Item1 != troop))
                {
                    pool.Add((troop, TierWeight(troop)));
                }
                if (troop.UpgradeTargets == null)
                {
                    continue;
                }
                foreach (CharacterObject next in troop.UpgradeTargets)
                {
                    if (next != null && seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        /// <summary>The lowest-tier troop of the culture's mercenary pool: the clan's BasicTroop (map recruits).</summary>
        private static CharacterObject BasicMercenary(CultureObject culture)
        {
            string note;
            CharacterObject best = null;
            foreach ((CharacterObject troop, float _) in MercenaryPool(culture, out note))
            {
                if (best == null || troop.Tier < best.Tier || (troop.Tier == best.Tier && string.CompareOrdinal(troop.StringId, best.StringId) < 0))
                {
                    best = troop;
                }
            }
            return best;
        }

        /// <summary>
        /// Keeps a company clan's troop identity mercenary: its BasicTroop -- what vanilla's minor-faction map
        /// recruiting (<c>RecruitmentCampaignBehavior.HourlyTickParty</c>) buys -- is the culture's lowest
        /// mercenary troop. Applied on creation and re-applied to every living company each day (so a company made
        /// before this rule gets it too).
        /// </summary>
        internal static void ApplyMercenaryIdentity(Clan clan)
        {
            CharacterObject basic = BasicMercenary(clan.Culture);
            if (basic != null && clan.BasicTroop != basic)
            {
                clan.BasicTroop = basic;
            }
        }

        /// <summary>
        /// Removes from a company party every troop that is not a mercenary -- the backstop for any way in this
        /// module does not close at the source (rescued prisoners, quests, other mods). One PURGE line per
        /// removal; a clean party costs one roster scan.
        /// </summary>
        internal static void PurgeNonMercenaries(MobileParty party)
        {
            TroopRoster roster = party?.MemberRoster;
            if (roster == null)
            {
                return;
            }
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character == null || element.Character.IsHero || IsMercenaryTroop(element.Character) || element.Number <= 0)
                {
                    continue;
                }
                roster.AddToCounts(element.Character, -element.Number, insertAtFront: false, -element.WoundedNumber);
                MercenaryLog.Log("PURGE", party, "dismissed " + element.Number + " x " + element.Character.Name + " ("
                    + element.Character.StringId + ", " + element.Character.Occupation + "): not a mercenary troop");
            }
        }

        private static TextObject CompanyName(CultureObject culture, int index)
        {
            switch (culture.StringId + "_" + index)
            {
                case "empire_0": return new TextObject("{=RBM_merc_name_empire_0}The Gilded Spears");
                case "empire_1": return new TextObject("{=RBM_merc_name_empire_1}The Free Legion");
                case "aserai_0": return new TextObject("{=RBM_merc_name_aserai_0}The Desert Lances");
                case "aserai_1": return new TextObject("{=RBM_merc_name_aserai_1}The Sons of the Dunes");
                case "battania_0": return new TextObject("{=RBM_merc_name_battania_0}The Wolves of the Glen");
                case "battania_1": return new TextObject("{=RBM_merc_name_battania_1}The Oakshield Band");
                case "khuzait_0": return new TextObject("{=RBM_merc_name_khuzait_0}The Grey Riders");
                case "khuzait_1": return new TextObject("{=RBM_merc_name_khuzait_1}The Steppe Arrows");
                case "sturgia_0": return new TextObject("{=RBM_merc_name_sturgia_0}The Bear Company");
                case "sturgia_1": return new TextObject("{=RBM_merc_name_sturgia_1}The Northern Axes");
                case "vlandia_0": return new TextObject("{=RBM_merc_name_vlandia_0}The Free Lances");
                case "vlandia_1": return new TextObject("{=RBM_merc_name_vlandia_1}The Black Company");
                case "nord_0": return new TextObject("{=RBM_merc_name_nord_0}The Sea Wolves");
                case "nord_1": return new TextObject("{=RBM_merc_name_nord_1}The Raven Band");
            }
            TextObject fallback = new TextObject("{=RBM_merc_name_fallback}{CULTURE} Free Company");
            fallback.SetTextVariable("CULTURE", culture.Name);
            return fallback;
        }

        // ---- Recruiting -------------------------------------------------------------------------------

        // Days of the party's wage bill (as paid now, new men included) the leader keeps after a tavern hire.
        internal const int RecruitWageReserveDays = 5;

        /// <summary>
        /// How many men at <paramref name="price"/> each the leader can hire while keeping the larger of
        /// <see cref="RecruitGoldFloor"/> and <see cref="RecruitWageReserveDays"/> days of the wage bill he pays
        /// (half while idle), the new men's wages included: n ≤ (gold − floor) / price and
        /// n ≤ (gold − D·wage) / (price + D·newManWage).
        /// </summary>
        internal static int AffordableRecruits(MobileParty party, CharacterObject troop, int price)
        {
            Hero leader = party?.LeaderHero;
            if (leader == null || troop == null || price <= 0)
            {
                return 0;
            }
            float factor = WageFactor(party);
            float paidWage = party.TotalWage;
            float newManWage = Campaign.Current.Models.PartyWageModel.GetCharacterWage(troop) * factor;
            int byFloor = (leader.Gold - RecruitGoldFloor) / price;
            int byWages = (int)((leader.Gold - RecruitWageReserveDays * paidWage) / (price + RecruitWageReserveDays * newManWage));
            return Math.Max(0, Math.Min(byFloor, byWages));
        }

        /// <summary>
        /// Buys the town's tavern mercenaries, up to <see cref="RecruitTargetShare"/> of the party size limit and
        /// what the leader can pay while keeping <see cref="RecruitGoldFloor"/>. Goes through vanilla's own
        /// tavern-hire path (RecruitmentCampaignBehavior.ApplyRecruitMercenary), so the price is the campaign
        /// recruit-cost model's and the recruit event fires: RBM's recruit payment to the town and the spoils
        /// seed happen exactly as for any AI lord hiring there, with nothing charged twice.
        /// </summary>
        internal static void RecruitTavernMercenaries(MobileParty party, Settlement settlement)
        {
            Hero leader = party?.LeaderHero;
            if (leader == null || settlement?.Town == null || ApplyRecruitMercenaryMethod == null)
            {
                return;
            }
            RecruitmentCampaignBehavior recruitment = Campaign.Current.GetCampaignBehavior<RecruitmentCampaignBehavior>();
            RecruitmentCampaignBehavior.TownMercenaryData data = recruitment?.GetMercenaryData(settlement.Town);
            if (data == null || data.TroopType == null || data.Number <= 0)
            {
                return;
            }
            if (!IsMercenaryTroop(data.TroopType))
            {
                MercenaryLog.Log("TAVERN", party, settlement.Name + ": tavern offers " + data.TroopType.Name + " ("
                    + data.TroopType.StringId + ", " + data.TroopType.Occupation + "), not a mercenary troop; hired none");
                return;
            }
            int want = (int)(party.Party.PartySizeLimit * RecruitTargetShare) - party.Party.NumberOfAllMembers;
            if (want <= 0)
            {
                return;
            }
            int price = Math.Max(1, Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(data.TroopType, leader).RoundedResultNumber);
            int affordable = AffordableRecruits(party, data.TroopType, price);
            int count = Math.Min(want, Math.Min(data.Number, affordable));
            if (count <= 0)
            {
                return;
            }
            CharacterObject troop = data.TroopType;
            ApplyRecruitMercenaryMethod.Invoke(recruitment, new object[] { party, settlement, troop, count });
            MercenaryLog.Log("TAVERN", party, settlement.Name + ": " + count + " x " + troop.Name + " at " + price + "d = "
                + (count * price) + "d (" + data.Number + " left in tavern), now " + party.Party.NumberOfAllMembers + " of "
                + party.Party.PartySizeLimit + " men, gold left " + leader.Gold + "d");
        }
    }
}
