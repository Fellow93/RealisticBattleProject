using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// What a settlement lays out to keep its militia under arms, and where the money comes from.
    ///
    /// Militia are the one armed body vanilla pays nothing: no wage model bills anyone for them and they
    /// cost their settlement nothing. Under the ledger that left a hole -- RBM banks every party's wage
    /// into its troops' purses, so militia were accruing a soldier's pay out of thin air.
    ///
    /// So the levy is billed the way a field troop is -- a WAGE plus kit-value MAINTENANCE -- only with
    /// the settlement standing in for the party leader as the payer, and both legs cut for a force that
    /// stands a watch rather than marching a campaign (see the wage- and maint-factor tables):
    ///
    ///   * WAGE -- pay for the days a man bears arms instead of working his trade. A town's part-time
    ///     watch draws a quarter of a soldier's wage; a village's or castle's levy, owing service under
    ///     feudalism, a tenth. Drawn from the settlement's funding pot into the man's own spoils purse,
    ///     the way a field troop banks the wage its leader paid.
    ///
    ///   * MAINTENANCE -- the kit he wears out standing his watch, priced off its worth exactly as a
    ///     marching troop's is and scaled down for standing still. Met first from the purse the wage just
    ///     filled, the rest from the settlement's pot, and paid over to the town that mends the gear.
    ///
    /// Unlike the garrison there is no owner backstop: a settlement with an empty pot fields an unpaid,
    /// unmended watch, which the affordability floor thins over time. No denar is invented -- only what a
    /// pot could give is ever banked or paid, and every coin that leaves lands somewhere real.
    /// </summary>
    public static class MilitiaUpkeep
    {
        // A militiaman is billed the way a field troop is -- a wage plus kit-value maintenance (see
        // SpoilsPool.GetDailyMaintenanceCost) -- but at reduced rates, because a militia is not a marching
        // company: it stands a watch, not a campaign. Both legs are scaled per settlement type, set out in
        // the two tables below and read through MilitiaWageFactor / MilitiaMaintFactor. The settlement is
        // the payer throughout -- the wage is drawn from its funding pot into the man's spoils purse, and
        // the maintenance is drawn from that purse first and the pot for the rest, landing in the town that
        // mends his kit. See PayMilitiaUpkeep.

        /// <summary>
        /// Share of a full soldier's wage a militiaman draws as pay, by settlement type. A town's watch is
        /// a paid part-time body -- a quarter-wage -- while a village's or castle's levy, owing service
        /// under feudalism, draws only a tenth. Banked into the man's spoils purse the way a field troop's
        /// whole wage is, so his own purse then meets his maintenance before the settlement's pot does.
        /// </summary>
        public const float MilitiaWageFactorTown = 0.25f;
        public const float MilitiaWageFactorCastle = 0.10f;
        public const float MilitiaWageFactorVillage = 0.10f;

        /// <summary>
        /// Share of a field troop's kit-value maintenance a militiaman actually costs to keep, by
        /// settlement type. A quarter for a town or castle watch, whose gear is in near-constant use; a
        /// tenth for a village levy, whose arms sit in a chest but for a raid or a convoy. Priced off kit
        /// value like a marching troop's (see <see cref="SpoilsPool.GetDailyMaintenanceCost"/>), only
        /// scaled down for standing still.
        /// </summary>
        public const float MilitiaMaintFactorTownCastle = 0.25f;
        public const float MilitiaMaintFactorVillage = 0.10f;

        /// <summary>
        /// Days of a militiaman's maintenance a settlement must have in hand to keep him under arms.
        ///
        /// This is what turns the bill into a limit. A settlement does not muster the militia its
        /// prosperity would allow and then go broke keeping them: it musters what it can keep paying,
        /// and the purse is what says how many that is. Twenty days' maintenance per man is the margin
        /// -- enough that a single bad convoy does not disband the watch, little enough that a village
        /// cannot field a standing company on a harvest's takings.
        /// </summary>
        public const int MilitiaPayDaysHeld = 20;

        /// <summary>
        /// Men a settlement sheds per day while it is over what it can afford. Deliberately slow: men
        /// drift home when the pay stops, they are not dismissed on parade.
        /// </summary>
        public const float MilitiaShedPerDay = 1f;

        /// <summary>
        /// The soft cap on a settlement's militia, as a share of the manpower behind it -- a village's
        /// hearths, a fortification's prosperity. Not a wall the count cannot pass: past it, growth
        /// slows to <see cref="MilitiaOverCapGrowthFactor"/> of its rate rather than stopping, so a
        /// settlement can still creep higher on strong loyalty or a governor's perks, only slowly.
        ///
        /// A village that answers to a city keeps a smaller watch than one that answers to a castle:
        /// the castle is a garrison's seat with no standing troops of its own to spare for the fields,
        /// so its villages carry more of their own defence.
        /// </summary>
        public const float MilitiaCapVillageCity = 0.40f;
        public const float MilitiaCapVillageCastle = 0.50f;

        /// <summary>Militia soft cap for a castle, as a share of its prosperity.</summary>
        public const float MilitiaCapCastle = 0.50f;

        /// <summary>
        /// Militia soft cap for a city, as a share of its prosperity -- the highest of the three: a
        /// town is a crowd, and a crowd under threat arms a great many of its own.
        /// </summary>
        public const float MilitiaCapCity = 0.70f;

        /// <summary>What fraction of a day's growth a settlement keeps once it is over its soft cap.</summary>
        public const float MilitiaOverCapGrowthFactor = 0.10f;

        /// <summary>
        /// The share of its soft cap a settlement opens a new campaign holding in militia. A quarter:
        /// enough that a fresh map has watches on its walls and villages that can turn out a few men,
        /// but well short of the cap, so growth still has room to run and the affordability floor thins
        /// any place that cannot keep even this many. Applied on the new-game path only, replacing
        /// vanilla's cap-blind <c>MilitiaChange * 45</c> seed.
        /// </summary>
        public const float MilitiaSeedCapFraction = 0.25f;

        /// <summary>Times a militiaman's kit cost the funding pot must hold before it may arm a new one.</summary>
        public const int MilitiaSpawnReserveMult = 5;

        /// <summary>
        /// The same reserve for a VILLAGE, held lower than a fortification's. A village purse is small and
        /// spiky -- it swells on a convoy's return and empties again -- so demanding as many days of a
        /// kit's cost in hand as a town would gate its levy out of ever mustering. Three rather than five,
        /// alongside the quarter-price kit (<see cref="MilitiaVillageGearShare"/>), so a village that has
        /// turned a season's trade can actually arm the watch its hearths support.
        /// </summary>
        public const int MilitiaVillageSpawnReserveMult = 3;

        /// <summary>
        /// Share of a militiaman's full war-kit value a VILLAGE actually pays to arm one. A village
        /// levy is not outfitted like a soldier: the men bring their own tools and cheap arms and are
        /// given only what the muster cannot do without, so the village buys a quarter of a real kit off
        /// the town it trades with rather than a whole one. Priced at full value everywhere else -- a
        /// town or castle arms its watch properly.
        ///
        /// Applied to both the affordability gate (so the reserve it must hold is a quarter as steep) and
        /// the charge itself, keeping the two in step: at full value the ~18k Empire kit put the 5x
        /// spawn reserve (~89k) out of every village purse's reach, so no village ever fielded a growing
        /// militia at all.
        /// </summary>
        public const float MilitiaVillageGearShare = 0.25f;

        private static readonly TextObject UnaffordableText = new TextObject("{=RBM_militia_unpaid}Cannot be paid");
        private static readonly TextObject OverCapText = new TextObject("{=RBM_militia_overcap}Over muster");
        private static readonly TextObject CannotArmText = new TextObject("{=RBM_militia_unarmed}Cannot be armed");

        /// <summary>The pay factor for a militiaman of this settlement -- see the wage-factor table.</summary>
        private static float MilitiaWageFactor(Settlement settlement)
        {
            if (settlement != null && settlement.IsTown)
            {
                return MilitiaWageFactorTown;
            }
            if (settlement != null && settlement.IsCastle)
            {
                return MilitiaWageFactorCastle;
            }
            return MilitiaWageFactorVillage;
        }

        /// <summary>The maintenance factor for a militiaman of this settlement -- see the maint-factor table.</summary>
        private static float MilitiaMaintFactor(Settlement settlement)
        {
            return (settlement != null && settlement.IsVillage)
                ? MilitiaMaintFactorVillage
                : MilitiaMaintFactorTownCastle;
        }

        /// <summary>
        /// The pot that funds a settlement's militia, and against which its affordability is judged. Each
        /// kind of place pays from the purse the spec names: a village and a castle from their single
        /// settlement wealth, a town from its citizens' market money.
        /// </summary>
        private static int MaintenancePot(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0;
            }
            if (settlement.IsTown)
            {
                return SettlementWealth.GetCitizenWealth(settlement);
            }
            return SettlementWealth.GetSettlementWealth(settlement);
        }

        /// <summary>
        /// Draws <paramref name="amount"/> from the settlement's militia funding pot -- a town's citizens,
        /// a village's or castle's settlement wealth -- and returns what it could actually give. The one
        /// place the pot is spent, so wage and maintenance-shortfall both leave by the same door.
        /// </summary>
        private static int DebitFundingPot(Settlement settlement, int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }
            return settlement.IsTown
                ? SettlementWealth.DebitCitizens(settlement, amount, SettlementWealth.Source.Militia)
                : SettlementWealth.Debit(settlement, amount, SettlementWealth.Source.Militia);
        }

        /// <summary>
        /// The town that mends a settlement's militia kit: the town itself, a village's trade-bound town,
        /// or the nearest friendly town to a castle. Where the day's maintenance coin lands, as a field
        /// troop's does at the town it rests by.
        /// </summary>
        private static Settlement MilitiaMaintenanceMarket(Settlement settlement, MobileParty militiaParty)
        {
            if (settlement.IsTown || settlement.IsVillage)
            {
                return RecruitSupply.GetSupplyMarket(settlement);
            }
            Town town = UpgradeSupply.FindNearestFriendlyTown(militiaParty);
            return town != null ? town.Settlement : null;
        }

        /// <summary>
        /// What this settlement's militia costs it each day, for the affordability floor to judge it
        /// against. The WAGE leg alone: the pay the settlement lays out from its pot every day
        /// (<see cref="MilitiaWageFactor"/> of a full wage). The maintenance leg is not added, because a
        /// militiaman meets his own maintenance out of the purse that same wage just filled -- only when
        /// his kit costs more to keep than he is paid does the settlement top it up, and that overflow is
        /// small beside the wage. Billing the wage keeps the floor a stable, convergent test: shedding a
        /// man always lowers it.
        /// </summary>
        /// <remarks>
        /// Read off the militia party's own wage bill rather than priced per head where a party exists;
        /// where the militia is only a settlement count with no mustered party, fall back to a tier-one
        /// wage for the headcount.
        /// </remarks>
        public static int DailyMaintenanceBill(Settlement settlement)
        {
            float wageFactor = MilitiaWageFactor(settlement);
            MobileParty party = (settlement.MilitiaPartyComponent != null)
                ? settlement.MilitiaPartyComponent.MobileParty
                : null;
            if (party != null && party.IsActive)
            {
                return (int)(party.TotalWage * wageFactor);
            }

            // Militia counted but not yet mustered into a party: nothing to read a wage off, so fall
            // back to a recruit's rate for the headcount.
            return (int)(settlement.Militia * wageFactor * TierBasedWageModel.WageForTier(1, false));
        }

        /// <summary>
        /// Whether the funding pot can keep the militia the settlement currently has under arms -- twenty
        /// days of their maintenance in hand.
        /// </summary>
        public static bool CanKeepMilitia(Settlement settlement)
        {
            int bill = DailyMaintenanceBill(settlement);
            if (bill <= 0)
            {
                return true;
            }
            return MaintenancePot(settlement) >= bill * MilitiaPayDaysHeld;
        }

        /// <summary>
        /// The prosperity a fortification's militia cap is sized on. Zero for anything without a Town
        /// component, which a village's cap never reads.
        /// </summary>
        private static float Prosperity(Settlement settlement)
        {
            return settlement.Town != null ? settlement.Town.Prosperity : 0f;
        }

        /// <summary>
        /// The soft cap on this settlement's militia -- the count past which growth slows to a trickle.
        /// A village is sized on its hearths and on whether it answers to a city or a castle; a
        /// fortification on its prosperity. Zero (no cap) for anything else.
        /// </summary>
        public static float MilitiaCap(Settlement settlement)
        {
            if (settlement.IsVillage)
            {
                float hearth = settlement.Village != null ? settlement.Village.Hearth : 0f;
                Settlement bound = settlement.Village != null ? settlement.Village.TradeBound : null;
                float share = (bound != null && bound.IsCastle) ? MilitiaCapVillageCastle : MilitiaCapVillageCity;
                return hearth * share;
            }
            if (settlement.IsCastle)
            {
                return Prosperity(settlement) * MilitiaCapCastle;
            }
            if (settlement.IsTown)
            {
                return Prosperity(settlement) * MilitiaCapCity;
            }
            return 0f;
        }

        /// <summary>
        /// Holds a settlement's militia to what its manpower supports and what it can pay for.
        /// </summary>
        /// <remarks>
        /// Vanilla grows militia out of prosperity, hearths and loyalty, and nothing anywhere asks
        /// whether the place can support or afford them -- harmless while militia were free and both
        /// uncapped, wrong the moment they are neither. Two rules stack here, both pushing the day's
        /// change down rather than replacing it, so vanilla's own lines stay on the breakdown and the
        /// player can read what the militia would have been:
        ///
        ///   * A SOFT CAP on manpower. Past a share of the settlement's hearths or prosperity, the day's
        ///     growth is cut to a tenth -- a ceiling that yields to strong loyalty or a governor's
        ///     perks rather than one the count cannot pass. Only positive growth is touched; a
        ///     settlement already shedding is left to shed.
        ///
        ///   * AN AFFORDABILITY FLOOR. A settlement that cannot keep twenty days of its militia's
        ///     maintenance in the funding pot sheds men, whatever its cap says -- and this wins, because
        ///     a town too poor to arm its watch loses it even below the manpower ceiling. Applied to
        ///     every settlement, towns included: a market spent dry SHOULD start losing its militia, and
        ///     an exception would only hide that.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultSettlementMilitiaModel), "CalculateMilitiaChange")]
        private static class AffordableMilitiaPatch
        {
            private static void Postfix(Settlement settlement, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
                {
                    return;
                }

                ApplySoftCap(settlement, ref __result);

                // A settlement that cannot arm a new militiaman fields no new ones -- growth is held to
                // zero, though the men it already has are left standing (the maintenance shed below is
                // what thins those, when even their upkeep cannot be met).
                if (__result.ResultNumber > 0f && !CanAffordSpawn(settlement))
                {
                    __result.Add(-__result.ResultNumber, CannotArmText);
                }

                if (!CanKeepMilitia(settlement) && __result.ResultNumber > -MilitiaShedPerDay)
                {
                    __result.Add(-MilitiaShedPerDay - __result.ResultNumber, UnaffordableText);
                }
            }

            /// <summary>
            /// Cuts the day's growth to <see cref="MilitiaOverCapGrowthFactor"/> once the settlement is
            /// at or over its soft cap. Leaves a settlement under its cap, or one already losing men,
            /// untouched.
            /// </summary>
            private static void ApplySoftCap(Settlement settlement, ref ExplainedNumber __result)
            {
                if (__result.ResultNumber <= 0f)
                {
                    return;
                }
                float cap = MilitiaCap(settlement);
                if (cap <= 0f || settlement.Militia < cap)
                {
                    return;
                }
                __result.Add(-(1f - MilitiaOverCapGrowthFactor) * __result.ResultNumber, OverCapText);
            }
        }

        /// <summary>
        /// Pays a militia stack's daily upkeep the way a field troop's is met -- a wage, then kit-value
        /// maintenance -- with the settlement standing in for the party leader as the payer. Called once
        /// per militia stack from the wage-into-spoils pass (see <see cref="SpoilsPool"/>), with the stack's
        /// full-strength soldier wage; nothing is returned, because unlike a field troop the settlement
        /// funds the purse here rather than the caller banking it afterward.
        /// </summary>
        /// <remarks>
        /// Two legs, each conserving, no denar invented:
        ///
        ///   * WAGE. The man draws <see cref="MilitiaWageFactor"/> of a full soldier's wage. The settlement
        ///     pays it out of its funding pot (<see cref="DebitFundingPot"/> -- a town's citizens, a village's
        ///     or castle's wealth) and it is banked into his spoils purse, exactly as a field troop banks the
        ///     wage its leader paid. Only what the pot could give is banked, so the deposit never exceeds the
        ///     payment.
        ///
        ///   * MAINTENANCE. Kit-value maintenance like a marching troop's (<see cref="SpoilsPool.GetDailyMaintenanceCost"/>),
        ///     scaled by <see cref="MilitiaMaintFactor"/>. Drawn from the man's purse first -- the wage just
        ///     filled it -- and the shortfall from the settlement's pot, and the whole of what was met is paid
        ///     over to the town that mends his gear (<see cref="MilitiaMaintenanceMarket"/>), the market fee
        ///     riding along. What neither purse nor pot can cover simply goes unmended that day.
        ///
        /// No owner backstop: a settlement with an empty pot fields an unpaid, unmended watch, which the
        /// affordability floor thins over time. The purse is the same one the man's upgrades and carousing
        /// draw on, so his militia pay behaves like any soldier's from here on.
        /// </remarks>
        public static void PayMilitiaUpkeep(MobileParty militiaParty, CharacterObject character, int number, int fullWage)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || militiaParty == null || character == null
                || number <= 0 || fullWage <= 0)
            {
                return;
            }
            Settlement settlement = (militiaParty.CurrentSettlement ?? militiaParty.HomeSettlement);
            if (settlement == null)
            {
                return;
            }
            PartyBase party = militiaParty.Party;
            if (party == null)
            {
                return;
            }

            // Wage leg: settlement pot -> the man's spoils purse.
            int wageAmount = (int)(fullWage * MilitiaWageFactor(settlement));
            int wagePaid = DebitFundingPot(settlement, wageAmount);
            if (wagePaid > 0)
            {
                SpoilsPool.AddSpoils(party, character, wagePaid);
            }

            // Maintenance leg: purse first, then the pot, all of it paid to the mending town.
            int maintenance = (int)(SpoilsPool.GetDailyMaintenanceCost(character, number) * MilitiaMaintFactor(settlement));
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
                Settlement market = MilitiaMaintenanceMarket(settlement, militiaParty);
                if (market != null)
                {
                    TroopMarketFeedback.RegisterPurchase(market, null, toMarket, SettlementWealth.Source.Militia);
                }
            }
        }

        // ------------------------------------------------------------------ spawn cost (the kit a new man is given)

        /// <summary>
        /// A militiaman's growth banked here as it happens but not yet paid for -- the fractional day's
        /// growth accrues until it makes a whole man, and only then is one armed. Session state, keyed by
        /// settlement, cleared per campaign; a fraction of a man lost on reload is beneath notice.
        /// </summary>
        private static readonly Dictionary<Settlement, float> _pendingGrowth = new Dictionary<Settlement, float>();

        /// <summary>Drops the accumulators before a new campaign's settlements take their place.</summary>
        public static void ResetForNewSession()
        {
            _pendingGrowth.Clear();
        }

        /// <summary>
        /// Opens a new campaign with every settlement holding a quarter of its militia soft cap
        /// (<see cref="MilitiaSeedCapFraction"/>), in place of vanilla's cap-blind <c>MilitiaChange * 45</c>.
        /// New-game path only -- it overwrites the live militia count, which a loaded save must keep --
        /// and after hearths and prosperity are built, so <see cref="MilitiaCap"/> reads a real ceiling.
        /// </summary>
        public static void SeedInitialMilitia()
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null || !(settlement.IsTown || settlement.IsCastle || settlement.IsVillage))
                {
                    continue;
                }
                float cap = MilitiaCap(settlement);
                if (cap <= 0f)
                {
                    continue;
                }
                settlement.Militia = cap * MilitiaSeedCapFraction;
            }
        }

        /// <summary>
        /// The militiaman a settlement's spawn cost is priced and armed as -- its culture's plain melee
        /// militia, the commonest of the muster. A representative rather than the exact roster mix: the
        /// design treats militia "upgrade" as abstract, so a settlement fielding dearer militia pays more
        /// only through carrying more of them, not through a per-man promotion charge.
        /// </summary>
        private static CharacterObject SpawnTroop(Settlement settlement)
        {
            return (settlement != null && settlement.Culture != null) ? settlement.Culture.MeleeMilitiaTroop : null;
        }

        /// <summary>
        /// What arming one militiaman costs -- his kit's worth, mount-less like a recruit's, and cut to
        /// <see cref="MilitiaVillageGearShare"/> for a village, whose levy is armed on the cheap. Drives
        /// both the affordability gate and the arming charge, so the two never disagree.
        /// </summary>
        private static int SpawnCostPerMan(Settlement settlement)
        {
            CharacterObject troop = SpawnTroop(settlement);
            if (troop == null)
            {
                return 0;
            }
            int full = SpoilsPool.GetEquipmentValue(troop);
            return (settlement != null && settlement.IsVillage)
                ? (int)(full * MilitiaVillageGearShare)
                : full;
        }

        /// <summary>
        /// Whether the funding pot can arm a new militiaman with a reserve to spare -- so many times his
        /// kit in hand, five for a fortification and three for a village (its purse being smaller and
        /// spikier). Read against the same pot the maintenance is drawn from, so the place that pays to
        /// keep him is the place that must be able to afford to raise him.
        /// </summary>
        public static bool CanAffordSpawn(Settlement settlement)
        {
            int per = SpawnCostPerMan(settlement);
            if (per <= 0)
            {
                return true;
            }
            int mult = (settlement != null && settlement.IsVillage)
                ? MilitiaVillageSpawnReserveMult
                : MilitiaSpawnReserveMult;
            return MaintenancePot(settlement) >= per * mult;
        }

        /// <summary>
        /// Banks the day's militia GROWTH the moment it is applied, so it can be armed later out of the
        /// village suppression window (see <see cref="ChargePendingSpawn"/>). Growth is the only militia
        /// change that happens inside a fief's DailyTick -- an escort borrowing men (see
        /// <c>VillagerEscort</c>) moves them on other events, so measuring the DailyTick delta catches
        /// muster and nothing else.
        /// </summary>
        private static void RecordGrowth(Settlement settlement, float preMilitia)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
            {
                return;
            }
            float delta = settlement.Militia - preMilitia;
            if (delta <= 0f)
            {
                return;
            }
            float acc;
            _pendingGrowth.TryGetValue(settlement, out acc);
            _pendingGrowth[settlement] = acc + delta;
        }

        [HarmonyPatch(typeof(Town), "DailyTick")]
        private static class TownMilitiaGrowthRecord
        {
            private static void Prefix(Town __instance, out float __state)
            {
                __state = (__instance != null && __instance.Settlement != null) ? __instance.Settlement.Militia : 0f;
            }

            private static void Postfix(Town __instance, float __state)
            {
                if (__instance != null)
                {
                    RecordGrowth(__instance.Settlement, __state);
                }
            }
        }

        [HarmonyPatch(typeof(Village), "DailyTick")]
        private static class VillageMilitiaGrowthRecord
        {
            private static void Prefix(Village __instance, out float __state)
            {
                __state = (__instance != null && __instance.Settlement != null) ? __instance.Settlement.Militia : 0f;
            }

            private static void Postfix(Village __instance, float __state)
            {
                if (__instance != null)
                {
                    RecordGrowth(__instance.Settlement, __state);
                }
            }
        }

        /// <summary>
        /// Arms every whole militiaman a settlement has grown since it last paid, out of the pots the
        /// spec names. Called from the daily settlement pass rather than the DailyTick that recorded the
        /// growth, so a village's purse write is not caught inside <c>VillageGoldStock</c>'s suppression.
        /// </summary>
        /// <remarks>
        /// Each kind of place arms its men the way it does its maintenance: a village off the town it
        /// trades with, paying that town for the gear (the recruit gear leg, reused whole); a town from
        /// its own citizens' market money; a castle from its wealth, sourcing the kit from outside its
        /// walls. Only whole men are armed -- the sub-man remainder waits in the accumulator for the next
        /// day's growth to complete it.
        /// </remarks>
        public static void ChargePendingSpawn(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
            {
                return;
            }
            float acc;
            if (!_pendingGrowth.TryGetValue(settlement, out acc) || acc < 1f)
            {
                return;
            }
            CharacterObject troop = SpawnTroop(settlement);
            if (troop == null)
            {
                _pendingGrowth[settlement] = 0f;
                return;
            }

            int armed = 0;
            while (acc >= 1f)
            {
                ArmOneMilitiaman(settlement, troop);
                acc -= 1f;
                armed++;
            }
            _pendingGrowth[settlement] = acc;

            if (SpoilsLog.IsEnabled && armed > 0)
            {
                SpoilsLog.Log("MILITIA", (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId)
                    + " armed " + armed + " new militia at " + SpawnCostPerMan(settlement) + "d each");
            }
        }

        /// <summary>Arms one militiaman out of the settlement's funding pot, routed by kind of place.</summary>
        private static void ArmOneMilitiaman(Settlement settlement, CharacterObject troop)
        {
            if (settlement.IsVillage)
            {
                // The village buys the kit off the town it trades with and pays that town's merchants --
                // the recruit gear leg, which already does exactly this (debit village, credit town) --
                // but only a quarter of a full kit's worth, the cheap arming of a levy.
                RecruitSupply.DrawKitFromMarket(RecruitSupply.GetSupplyMarket(settlement), settlement, troop, 1,
                    MilitiaVillageGearShare);
                return;
            }
            int cost = SpoilsPool.GetEquipmentValue(troop);
            if (cost <= 0)
            {
                return;
            }
            if (settlement.IsTown)
            {
                // The townsmen arm their own watch out of the market's money.
                SettlementWealth.DebitCitizens(settlement, cost, SettlementWealth.Source.Militia);
            }
            else
            {
                // A castle has no market; the kit is sourced from outside and the coin leaves its wealth.
                SettlementWealth.Debit(settlement, cost, SettlementWealth.Source.Militia);
            }
        }
    }
}
