using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// What a settlement lays out to keep a patrol on the road, and where the money comes from.
    ///
    /// Vanilla fields patrols for free: a town raises one the moment it builds a Guard House, a coastal town
    /// once its kingdom passes the Coastal Guard Edict, and in both cases the company is conjured whole from a
    /// culture party template -- no purse is touched to arm it, pay it or keep it, and RBM's own wage pass was
    /// quietly banking a patrol's daily wage into its troops' spoils out of nothing at all.
    ///
    /// Under this rework a settlement instead FUNDS its patrol out of its wealth, the way a lord funds his war
    /// party. A share of the settlement's funding pot (<see cref="RBMConfig.RBMConfig.patrolBudgetFraction"/>,
    /// raised by the Guard House level and the Coastal Guard Edict) is the daily budget; the patrol is sized to
    /// what that budget can sustain, and while the pot can keep it the men draw a full wage and their kit is
    /// mended, both billed to the settlement exactly as <see cref="MilitiaUpkeep"/> bills a levy -- the wage into
    /// the men's purses, the maintenance out of them and the settlement's pot, landing in the town that mends the
    /// gear. When the pot can no longer hold a patrol's keep the company is stood down (the native removal path
    /// fires once <see cref="CanFundPatrol"/> turns false). Towns and castles field them; villages do not.
    ///
    /// No denar is invented: every wage banked is drawn from a pot first, and the arming cost is a sink like a
    /// militia's kit. Coin left in a disbanded patrol's purse is lost rather than refunded, a small bounded burn
    /// matching how a militia's purse behaves when its watch is shed.
    /// </summary>
    public static class PatrolUpkeep
    {
        /// <summary>Smallest patrol worth fielding. A settlement whose budget cannot sustain this many keeps none.</summary>
        public const int PatrolMinSize = 6;

        /// <summary>The most men a patrol is sized to, however deep the settlement's purse.</summary>
        public const int PatrolMaxSize = 40;

        /// <summary>
        /// Days of a minimal patrol's keep a settlement's funding pot must hold to raise or keep one. The
        /// buffer that stops a patrol flickering in and out as its own upkeep nibbles the pot: a fief musters
        /// what it can keep paying, not what it can afford for a day.
        /// </summary>
        public const int PatrolSustainDays = 15;

        /// <summary>Times a patrol's whole kit value the pot must hold before it may arm one -- the spawn reserve.</summary>
        public const int PatrolSpawnReserveMult = 5;

        /// <summary>
        /// How much a Guard House raises a town's patrol budget, per level. At the default a level-3 Guard
        /// House trebles the budget the fraction alone would set, so the building that used to gate patrols now
        /// makes them bigger and better instead. Castles have no Guard House and take no bonus.
        /// </summary>
        public const float PatrolGuardHouseBonus = 0.5f;

        /// <summary>
        /// How much the Coastal Guard Edict raises a coastal town's naval patrol budget. The edict no longer
        /// switches sea patrols on -- wealth does that -- it just funds a stronger squadron where a kingdom has
        /// resolved to keep its waters. Read by the naval reflection patches (see PatrolUpkeep.Naval.cs).
        /// </summary>
        public const float PatrolNavalEdictMultiplier = 1.5f;

        /// <summary>
        /// Share of a land patrol's foot that is remounted into cavalry after it musters, so a patrol is the
        /// fast, bandit-catching company the countryside needs rather than a column of spearmen a raider party
        /// simply outruns. Naval patrols are left alone -- ships, not horses.
        /// </summary>
        public const float PatrolCavalryFraction = 0.5f;

        /// <summary>The share of a settlement's funding pot set aside each day for its patrol -- the player dial.</summary>
        public static float BudgetFraction => RBMConfig.RBMConfig.patrolBudgetFraction;

        /// <summary>Master gate: the whole rework is off unless the campaign module and the patrol toggle are on.</summary>
        public static bool IsEnabled =>
            RBMConfig.RBMConfig.rbmCampaignEnabled && RBMConfig.RBMConfig.settlementPatrolsEnabled;

        // Cavalry troop chosen per (culture, tier) for the remount, cached so a spawn does not re-sweep the
        // whole character list. Session state; troops never change within a run, so it is never invalidated.
        private static readonly Dictionary<string, CharacterObject> _cavalryByCultureTier =
            new Dictionary<string, CharacterObject>();

        /// <summary>Drops the per-session caches before a new campaign's objects take their place.</summary>
        public static void ResetForNewSession()
        {
            _cavalryByCultureTier.Clear();
        }

        // ------------------------------------------------------------------ eligibility

        /// <summary>
        /// Whether a settlement can field a patrol under the rework: a town or castle with a real owner, not in
        /// revolt, whose funding pot can sustain at least a minimal patrol. This replaces vanilla's Guard-House
        /// gate (land) and Coastal-Guard-Edict gate (naval): both become budget bonuses, and wealth is the gate.
        /// The native behaviour removes an existing patrol the moment this turns false, so a fief whose pot has
        /// been drained -- sacked, at war, bankrupt -- stands its patrol down of itself.
        /// </summary>
        public static bool CanFundPatrol(Settlement settlement)
        {
            if (!IsEnabled || settlement == null)
            {
                return false;
            }
            if (!(settlement.IsTown || settlement.IsCastle))
            {
                return false;
            }
            if (settlement.OwnerClan == null || settlement.OwnerClan.IsRebelClan)
            {
                return false;
            }
            return CanSustainPatrol(settlement);
        }

        /// <summary>
        /// Whether a settlement can field a SEA patrol under the rework: a coastal town with a real owner, not
        /// in revolt, whose funding pot can sustain a minimal crew. This replaces the naval model's Coastal
        /// Guard Edict gate -- wealth is the gate now, and the edict becomes a budget bonus (see
        /// <see cref="HasCoastalEdict"/> / <see cref="DailyBudget"/>). Read by the naval reflection patches.
        /// </summary>
        public static bool CanFundNavalPatrol(Settlement settlement)
        {
            if (!IsEnabled || settlement == null)
            {
                return false;
            }
            if (!settlement.IsTown || !settlement.HasPort)
            {
                return false;
            }
            if (settlement.OwnerClan == null || settlement.OwnerClan.IsRebelClan)
            {
                return false;
            }
            return CanSustainPatrol(settlement);
        }

        /// <summary>
        /// Whether the settlement's kingdom holds the Coastal Guard Edict, detected by the policy's own string
        /// id so no reference to the NavalDLC assembly is needed. Used only to raise the naval patrol budget --
        /// the edict no longer switches sea patrols on.
        /// </summary>
        public static bool HasCoastalEdict(Settlement settlement)
        {
            Kingdom kingdom = (settlement != null && settlement.OwnerClan != null) ? settlement.OwnerClan.Kingdom : null;
            if (kingdom == null)
            {
                return false;
            }
            foreach (PolicyObject policy in kingdom.ActivePolicies)
            {
                if (policy != null && policy.StringId == "policy_coastal_guard_edict")
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the funding pot holds enough to keep a minimal patrol on the road for
        /// <see cref="PatrolSustainDays"/> days. A stable floor -- it reads a minimal patrol's cost, not the
        /// current one's -- so paying the patrol its keep each day does not itself trip the gate that would
        /// disband it.
        /// </summary>
        public static bool CanSustainPatrol(Settlement settlement)
        {
            int perMan = PerManDaily(settlement);
            if (perMan <= 0)
            {
                return false;
            }
            long floorDailyCost = (long)PatrolMinSize * perMan;
            return MaintenancePot(settlement) >= floorDailyCost * PatrolSustainDays;
        }

        /// <summary>
        /// Whether the pot can arm a new patrol with a reserve to spare -- <see cref="PatrolSpawnReserveMult"/>
        /// times the kit value of the patrol it would field. Keeps a fief from arming a company it cannot then
        /// keep, and read against the same pot the upkeep is drawn from.
        /// </summary>
        public static bool CanAffordSpawn(Settlement settlement, bool naval = false)
        {
            bool fundable = naval ? CanFundNavalPatrol(settlement) : CanFundPatrol(settlement);
            if (!fundable)
            {
                return false;
            }
            CharacterObject rep = RepresentativeTroop(settlement);
            if (rep == null)
            {
                return false;
            }
            int kit = SpoilsPool.GetEquipmentValue(rep);
            long spawnCost = (long)PatrolSizeLimit(settlement, naval) * kit;
            return MaintenancePot(settlement) >= spawnCost * PatrolSpawnReserveMult;
        }

        // ------------------------------------------------------------------ budget & size

        /// <summary>
        /// The daily budget a settlement sets aside for its patrol: a share of its funding pot, raised by the
        /// Guard House level (land) and left for the naval path to raise by the Coastal Guard Edict.
        /// </summary>
        public static float DailyBudget(Settlement settlement, bool naval = false)
        {
            if (settlement == null)
            {
                return 0f;
            }
            // A land patrol's budget is raised by the Guard House; a sea patrol's by the Coastal Guard Edict.
            float bonus = naval
                ? (HasCoastalEdict(settlement) ? PatrolNavalEdictMultiplier : 1f)
                : GuardHouseBonus(settlement);
            return MaintenancePot(settlement) * BudgetFraction * bonus;
        }

        /// <summary>The patrol the budget can sustain, in men, clamped to the min/max band. The size the size-limit patch returns.</summary>
        public static int PatrolSizeLimit(Settlement settlement, bool naval = false)
        {
            int perMan = PerManDaily(settlement);
            if (perMan <= 0)
            {
                return PatrolMinSize;
            }
            int size = (int)(DailyBudget(settlement, naval) / perMan);
            return (int)MathF.Clamp(size, PatrolMinSize, PatrolMaxSize);
        }

        /// <summary>
        /// The Guard House budget multiplier for a settlement: 1 for a castle or a town without one, rising by
        /// <see cref="PatrolGuardHouseBonus"/> per Guard House level. The building that used to gate a town's
        /// patrol now funds a bigger one.
        /// </summary>
        public static float GuardHouseBonus(Settlement settlement)
        {
            int level = GuardHouseLevel(settlement);
            return 1f + PatrolGuardHouseBonus * level;
        }

        private static int GuardHouseLevel(Settlement settlement)
        {
            if (settlement == null || settlement.Town == null)
            {
                return 0;
            }
            foreach (Building building in settlement.Town.Buildings)
            {
                if (building.BuildingType == DefaultBuildingTypes.SettlementGuardHouse)
                {
                    return building.CurrentLevel;
                }
            }
            return 0;
        }

        /// <summary>The troop a patrol's size and spawn cost are priced off -- the culture's basic soldier, or its melee levy as a fallback.</summary>
        private static CharacterObject RepresentativeTroop(Settlement settlement)
        {
            CultureObject culture = settlement != null ? settlement.Culture : null;
            if (culture == null)
            {
                return null;
            }
            return culture.BasicTroop ?? culture.MeleeMilitiaTroop;
        }

        /// <summary>What one representative patrolman costs the settlement a day -- his wage plus his kit's maintenance.</summary>
        private static int PerManDaily(Settlement settlement)
        {
            CharacterObject rep = RepresentativeTroop(settlement);
            if (rep == null)
            {
                return 0;
            }
            int wage = Campaign.Current.Models.PartyWageModel.GetCharacterWage(rep);
            int maint = SpoilsPool.GetDailyMaintenanceCost(rep, 1);
            return wage + maint;
        }

        // ------------------------------------------------------------------ funding pot (mirrors MilitiaUpkeep)

        /// <summary>
        /// The pot that funds a settlement's patrol: a town's citizens' market money BACKED by its treasury, a
        /// castle's single settlement wealth. The same pot, in the same order, that <see cref="DebitFundingPot"/>
        /// spends, so what the affordability floor judges is exactly what can be drawn.
        /// </summary>
        private static int MaintenancePot(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0;
            }
            if (settlement.IsTown)
            {
                return SettlementWealth.GetCitizenWealth(settlement)
                    + SettlementWealth.GetSettlementWealth(settlement);
            }
            return SettlementWealth.GetSettlementWealth(settlement);
        }

        /// <summary>Draws from the patrol funding pot -- a town's citizens then treasury, a castle's wealth -- and returns what it gave.</summary>
        private static int DebitFundingPot(Settlement settlement, int amount)
        {
            if (amount <= 0 || settlement == null)
            {
                return 0;
            }
            if (settlement.IsTown)
            {
                int paid = SettlementWealth.DebitCitizens(settlement, amount, SettlementWealth.Source.Patrol);
                int remaining = amount - paid;
                if (remaining > 0)
                {
                    paid += SettlementWealth.Debit(settlement, remaining, SettlementWealth.Source.Patrol);
                }
                return paid;
            }
            return SettlementWealth.Debit(settlement, amount, SettlementWealth.Source.Patrol);
        }

        /// <summary>The town that mends a patrol's kit: the town itself, or the nearest friendly town to a castle's patrol.</summary>
        private static Settlement MaintenanceMarket(Settlement settlement, MobileParty patrolParty)
        {
            if (settlement == null)
            {
                return null;
            }
            if (settlement.IsTown)
            {
                return RecruitSupply.GetSupplyMarket(settlement);
            }
            Town town = UpgradeSupply.FindNearestFriendlyTown(patrolParty);
            return town != null ? town.Settlement : null;
        }

        // ------------------------------------------------------------------ daily upkeep

        /// <summary>
        /// Pays a patrol stack's daily upkeep the way a field troop's is met -- a full wage into the men's
        /// purses, then kit-value maintenance out of them and the settlement's pot -- with the settlement
        /// standing in for the party leader as the payer. Called once per patrol stack from the wage-into-spoils
        /// pass (see <see cref="SpoilsPool"/>) in place of that pass's free deposit, so the wage a patrol banks
        /// is one the settlement actually paid rather than one minted from nothing.
        /// </summary>
        public static void PayPatrolUpkeep(MobileParty patrolParty, CharacterObject character, int number, int fullWage)
        {
            if (!IsEnabled || patrolParty == null || character == null || number <= 0)
            {
                return;
            }
            Settlement settlement = patrolParty.HomeSettlement;
            if (settlement == null)
            {
                return;
            }
            PartyBase party = patrolParty.Party;
            if (party == null)
            {
                return;
            }

            // Wage leg: settlement pot -> the man's spoils purse. Only what the pot gives is banked, so the
            // deposit -- which the wage pass used to make from nothing -- is now backed by a real draw.
            if (fullWage > 0)
            {
                int wagePaid = DebitFundingPot(settlement, fullWage);
                if (wagePaid > 0)
                {
                    SpoilsPool.AddSpoils(party, character, wagePaid);
                }
            }

            // Maintenance leg: the man's purse first, then the pot, all of it paid to the mending town.
            int maintenance = SpoilsPool.GetDailyMaintenanceCost(character, number);
            if (maintenance <= 0)
            {
                return;
            }
            int fromPurse = System.Math.Min(SpoilsPool.GetSpoils(party, character), maintenance);
            if (fromPurse > 0)
            {
                SpoilsPool.AddSpoils(party, character, -fromPurse);
            }
            int shortfall = maintenance - fromPurse;
            int fromPot = shortfall > 0 ? DebitFundingPot(settlement, shortfall) : 0;
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

        // ------------------------------------------------------------------ spawn charge & cavalry bias

        /// <summary>
        /// Charges a freshly-mustered patrol's kit to the settlement that raised it and remounts a share of its
        /// foot into cavalry. Called from the spawn postfix once the native party exists. The kit charge is a
        /// sink -- the same soft sink a militia's arming is -- drawn from the funding pot.
        /// </summary>
        public static void OnPatrolSpawned(Settlement settlement, MobileParty patrolParty)
        {
            if (!IsEnabled || settlement == null || patrolParty == null)
            {
                return;
            }
            PartyBase party = patrolParty.Party;
            if (party == null || party.MemberRoster == null)
            {
                return;
            }

            // Remount first, so the kit charged is the kit the patrol actually rides out with.
            if (!patrolParty.PatrolPartyComponent.IsNaval)
            {
                BiasTowardCavalry(patrolParty, PatrolCavalryFraction);
            }

            int kit = 0;
            TroopRoster roster = party.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character == null || element.Character.IsHero)
                {
                    continue;
                }
                kit += SpoilsPool.GetEquipmentValue(element.Character) * element.Number;
            }
            if (kit > 0)
            {
                DebitFundingPot(settlement, kit);
            }

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("PATROL", (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId)
                    + " raised a patrol of " + party.NumberOfAllMembers + " for " + kit + "d");
            }
        }

        /// <summary>
        /// Re-applies the cavalry remount to a land patrol whose roster was just rebuilt from its template --
        /// the native replenishment clears and refills the roster from the plain template, so without this a
        /// patrol reverts to foot the first time it tops up at home. Naval crews are left alone.
        /// </summary>
        public static void ReapplyCavalryBias(MobileParty patrolParty)
        {
            if (!IsEnabled || patrolParty == null || patrolParty.PatrolPartyComponent == null
                || patrolParty.PatrolPartyComponent.IsNaval)
            {
                return;
            }
            BiasTowardCavalry(patrolParty, PatrolCavalryFraction);
        }

        /// <summary>
        /// Remounts a share of a patrol's foot into cavalry of its own culture, preserving the head count. Each
        /// foot stack gives up a fraction of its men, replaced by the culture's cavalry troop nearest that
        /// stack's tier -- so a patrol is fast enough to run bandits down. A no-op where the culture fields no
        /// cavalry the sweep can find.
        /// </summary>
        private static void BiasTowardCavalry(MobileParty patrolParty, float fraction)
        {
            if (fraction <= 0f || patrolParty == null)
            {
                return;
            }
            TroopRoster roster = patrolParty.MemberRoster;
            if (roster == null)
            {
                return;
            }
            CultureObject culture = patrolParty.HomeSettlement != null ? patrolParty.HomeSettlement.Culture : null;
            if (culture == null)
            {
                return;
            }

            // Snapshot the foot stacks first: mutating the roster while walking it is unsafe.
            List<TroopRosterElement> footStacks = new List<TroopRosterElement>();
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character != null && !element.Character.IsHero
                    && !element.Character.IsMounted && element.Number > 0)
                {
                    footStacks.Add(element);
                }
            }

            foreach (TroopRosterElement foot in footStacks)
            {
                int convert = (int)(foot.Number * fraction);
                if (convert <= 0)
                {
                    continue;
                }
                CharacterObject cavalry = FindCultureCavalry(culture, foot.Character.Tier);
                if (cavalry == null || cavalry == foot.Character)
                {
                    continue;
                }
                roster.AddToCounts(foot.Character, -convert);
                roster.AddToCounts(cavalry, convert);
            }
        }

        /// <summary>
        /// The cavalry troop a culture remounts its patrol foot into: a mounted, non-hero soldier of that
        /// culture nearest the given tier (preferring one at or below it). Cached per culture and tier. Null
        /// where the culture has no such troop.
        /// </summary>
        private static CharacterObject FindCultureCavalry(CultureObject culture, int tier)
        {
            string key = culture.StringId + "#" + tier;
            if (_cavalryByCultureTier.TryGetValue(key, out CharacterObject cached))
            {
                return cached;
            }

            CharacterObject best = null;
            int bestScore = int.MaxValue;
            foreach (CharacterObject candidate in CharacterObject.All)
            {
                if (candidate == null || candidate.IsHero || candidate.Culture != culture
                    || !candidate.IsMounted || candidate.Occupation != Occupation.Soldier)
                {
                    continue;
                }
                // Prefer the nearest tier; on a tie, the one at or below the foot's tier (a lighter mount for
                // a lighter man), and failing that whichever came first.
                int diff = candidate.Tier - tier;
                int score = (diff >= 0) ? diff * 2 : (-diff * 2 - 1);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            _cavalryByCultureTier[key] = best;
            return best;
        }
    }
}
