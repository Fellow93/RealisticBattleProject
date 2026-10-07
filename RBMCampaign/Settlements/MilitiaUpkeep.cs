using System.Collections.Generic;
using System.Linq;
using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

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
        public const float MilitiaWageFactorVillage = 0.0f;

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

        // ------------------------------------------------------------------ militia caps
        //
        // Every settlement's watch is bounded by two caps sized on one BASE -- the manpower behind it (see
        // MilitiaCapBase): a town's prosperity, a castle's countryside (the AVERAGE hearth of its bound
        // villages), a village's own hearths. The same shares then apply to all three kinds of place:
        //
        //   * SOFT cap = MilitiaSoftCapShare of the base, plus the fief's bonuses in percentage points
        //     (buildings, the running daily project, kingdom policies), never above MilitiaSoftCapMaxShare.
        //     Below it the day's growth runs untouched.
        //   * HARD cap = MilitiaHardCapShare of the base, for everyone, moved by nothing. Between the two
        //     the day's growth tapers by (1 - fill)^2, fill = how far the count has climbed from soft to
        //     hard; growth may never carry the count past the hard cap; and a watch standing ABOVE it (a
        //     siege spawn, an escort returning, a cap that fell under it) disbands MilitiaOverflowDrainRate
        //     of the overflow a day, each man's kit refunded like an unpaid one's.

        /// <summary>The soft cap's share of the base before any bonus -- 40% of the manpower behind it.</summary>
        public const float MilitiaSoftCapShare = 0.40f;

        /// <summary>
        /// The most the bonuses may lift the soft cap's share to -- 70%, kept under the hard cap's 75% so the
        /// taper between the two never collapses.
        /// </summary>
        public const float MilitiaSoftCapMaxShare = 0.70f;

        /// <summary>
        /// The HARD cap's share of the base -- 75%, the same for every settlement and moved by no bonus. No
        /// day's muster may carry the count past it, and a watch standing over it disbands the excess.
        /// </summary>
        public const float MilitiaHardCapShare = 0.75f;

        /// <summary>
        /// Barracks (town or castle): soft-cap share added at levels 1/2/3 (+2/+3/+5 percentage points). The
        /// lodgings that let a fief take in more men a day also let it keep more of them under arms. Keeps its
        /// own +1/2/3 a day intake on top (see <see cref="BuildingEffects.BarracksGrowth"/>).
        /// </summary>
        public const float MilitiaSoftCapBarracksL1 = 0.02f;
        public const float MilitiaSoftCapBarracksL2 = 0.03f;
        public const float MilitiaSoftCapBarracksL3 = 0.05f;

        /// <summary>
        /// Castle Guard House: soft-cap share added at levels 1/2/3 (+2/+3/+5 percentage points), in place of
        /// the vanilla +1/2/3 militia a day it no longer raises (see <see cref="AddMilitiaEffectOfBuildings"/>).
        /// Castles only -- the town Guard House has no militia effect, in vanilla or here.
        /// </summary>
        public const float MilitiaSoftCapGuardHouseL1 = 0.02f;
        public const float MilitiaSoftCapGuardHouseL2 = 0.03f;
        public const float MilitiaSoftCapGuardHouseL3 = 0.05f;

        /// <summary>Training Fields (town or castle): soft-cap share added per level, +1/+2/+3 percentage points.</summary>
        public const float MilitiaSoftCapTrainingFieldsPerLevel = 0.01f;

        /// <summary>
        /// Train Militia (town) / Raise Troops (castle): soft-cap share added ONLY while it is the fief's
        /// running daily project (<see cref="BuildingEffects.IsDailyProjectActive"/>), +3 percentage points.
        /// </summary>
        public const float MilitiaSoftCapDailyProject = 0.03f;

        /// <summary>
        /// Kingdom policies of the settlement's owner clan (towns, castles AND villages): soft-cap share in
        /// percentage points. They used to add or take a flat man a day; they now move only the ceiling, and
        /// War Sails' Bolster the Fyrd (looked up by <see cref="BolsterTheFyrdPolicyId"/>, so RBM needs no
        /// NavalDLC reference) replaces its +25% growth factor with the same.
        /// </summary>
        public const float MilitiaSoftCapCitizenship = 0.03f;
        public const float MilitiaSoftCapCantons = 0.03f;
        public const float MilitiaSoftCapBolsterTheFyrd = 0.03f;
        public const float MilitiaSoftCapSerfdom = -0.03f;

        /// <summary>The string id War Sails registers its Bolster the Fyrd policy under (<c>NavalPolicies</c>).</summary>
        public const string BolsterTheFyrdPolicyId = "policy_bolster_the_fyrd";

        /// <summary>
        /// Share of the count standing over the hard cap that disbands each day. Men drift home rather than
        /// being dismissed on parade, so a large overflow sheds over weeks, not overnight.
        /// </summary>
        public const float MilitiaOverflowDrainRate = 0.05f;

        /// <summary>The fewest men an over-hard-cap watch disbands in a day (never more than the overflow itself).</summary>
        public const float MilitiaOverflowDrainMin = 1f;

        // ------------------------------------------------------------------ base growth curve (RBM-owned)
        //
        // RBM now authors the whole militia change rather than shaving vanilla's: these are the base-curve
        // knobs, seeded to vanilla's own values so nothing moves until they are turned. They replace the
        // Base / Retired / From-Hearths / From-Prosperity lines vanilla used to add; the loyalty, market,
        // policy, feat, building, perk and issue MODIFIERS are kept as vanilla computes them (see
        // AddKeptModifiers), so only the growth/decline spine is RBM's to tune, not the flavour on top.

        /// <summary>Flat daily muster a fortification (town or castle) raises before anything else -- vanilla's 2.</summary>
        public const float BaseMilitiaFortification = 2f;

        /// <summary>Flat daily muster a village raises before anything else -- vanilla's 0.5.</summary>
        public const float BaseMilitiaVillage = 0.5f;

        /// <summary>
        /// Vanilla's retirement rate, kept ONLY to size the new-campaign seed (<see cref="EquilibriumMilitia"/>).
        /// Militia no longer retires: the standing watch is bounded by the soft cap and by what the
        /// settlement's pot can arm and pay, not by a daily drift home. Seeding still opens each place at
        /// the count vanilla's curve would have settled on (intake / 0.025), so campaign starts are
        /// unchanged rather than every town opening at its cap.
        /// </summary>
        public const float MilitiaSeedRetirementRate = 0.025f;

        /// <summary>Militia a day per unit of a village's hearth -- vanilla's Hearth / 400.</summary>
        public const float MilitiaPerHearth = 1f / 400f;

        /// <summary>Militia a day per unit of a fortification's prosperity -- vanilla's Prosperity / 1000.</summary>
        public const float MilitiaPerProsperity = 1f / 1000f;

        // ------------------------------------------------------------------ understrength catch-up muster
        //
        // A settlement whose watch has been gutted -- stormed, routed, or run down below half of what it
        // can hold -- musters far faster than the steady trickle would rebuild it: a place under real
        // threat arms its own in a hurry. Below half the (effective) soft cap, a large extra intake is
        // added on top of the base curve, sized on a fortification's prosperity or a village's hearths. It
        // is ordinary positive growth: the caps still taper and stop it (though at half the soft cap it
        // is far under), and the affordability floor still gates it -- a settlement whose pot
        // cannot arm a new man raises none of these either, so money is still needed to spawn.

        /// <summary>
        /// The share of the soft cap below which the understrength catch-up muster fires. A watch at or
        /// above half its cap rebuilds on the base curve alone; one below it musters the extra intake.
        /// </summary>
        public const float MilitiaUnderstrengthThreshold = 0.5f;

        /// <summary>Extra daily muster per unit of a city's prosperity while understrength -- Prosperity / 50.</summary>
        public const float MilitiaCatchUpPerProsperityCity = 1f / 50f;

        /// <summary>Extra daily muster per unit of a castle's prosperity while understrength -- Prosperity / 100.</summary>
        public const float MilitiaCatchUpPerProsperityCastle = 1f / 100f;

        /// <summary>Extra daily muster per unit of a village's hearth while understrength -- Hearth / 150.</summary>
        public const float MilitiaCatchUpPerHearthVillage = 1f / 150f;

        // ------------------------------------------------------------------ prosperous-city muster
        //
        // A city whose households have savings to spare arms more of its own: for every luxury tier the
        // citizens' savings have reached (small / medium / large, the same thresholds CitizenDemand shops
        // on) the city musters an extra Prosperity / 50 a day. Ordinary positive growth like the rest --
        // the caps taper and stop it and the pot must still arm every man it raises.

        /// <summary>Extra daily muster per unit of a city's prosperity, per luxury tier its citizens' savings have reached.</summary>
        public const float MilitiaPerProsperityPerLuxuryTier = 1f / 50f;

        // Vanilla's own line labels, reused verbatim so RBM's rebuilt breakdown reads exactly as the
        // player is used to. These are TaleWorlds localization keys resolved by the game, not RBM strings.
        private static readonly TextObject BaseText = new TextObject("{=militarybase}Base");
        private static readonly TextObject FromHearthsText = new TextObject("{=ecdZglky}From Hearths");
        private static readonly TextObject FromProsperityText = new TextObject("{=cTmiNAlI}From Prosperity");
        private static readonly TextObject LowLoyaltyText = new TextObject("{=SJ2qsRdF}Low Loyalty");
        private static readonly TextObject MilitiaFromMarketText = new TextObject("{=7ve3bQxg}Weapons From Market");
        private static readonly TextObject CultureText = GameTexts.FindText("str_culture");

        // A new campaign opens every settlement on the steady state VANILLA's curve would have reached
        // (intake / 0.025, see EquilibriumMilitia). RBM's live curve has no retirement, so its own
        // steady state is the soft cap -- but seeding at the cap would open large towns with thousands of
        // militia; the vanilla-equivalent seed (~Prosperity/25 for a town, ~Hearth/10 for a village) keeps
        // campaign starts familiar and lets the watch grow from there, budget permitting. This replaces
        // both vanilla's cap-blind MilitiaChange*45 seed and RBM's earlier "quarter of the soft cap" seed.

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
        public const float MilitiaVillageGearShare = 0.1f;

        private static readonly TextObject UnderstrengthText = new TextObject("{=RBM_militia_understrength}Mustering the levy");
        private static readonly TextObject ProsperousCityText = new TextObject("{=RBM_militia_prosperous}Prosperous citizens");
        private static readonly TextObject UnaffordableText = new TextObject("{=RBM_militia_unpaid}Cannot be paid");
        private static readonly TextObject OverCapText = new TextObject("{=RBM_militia_overcap}Over muster");
        private static readonly TextObject HardCapText = new TextObject("{=RBM_militia_hardcap}Full muster");
        private static readonly TextObject CannotArmText = new TextObject("{=RBM_militia_unarmed}Cannot be armed");
        private static readonly TextObject OverflowText = new TextObject("{=RBM_militia_overflow}Disbanding excess");

        /// <summary>The extra intake a fief's Barracks lodgings allow.</summary>
        private static readonly TextObject BarracksText = new TextObject("{=RBM_militia_barracks}Barracks");

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
        internal static float MilitiaMaintFactor(Settlement settlement)
        {
            return (settlement != null && settlement.IsVillage)
                ? MilitiaMaintFactorVillage
                : MilitiaMaintFactorTownCastle;
        }

        /// <summary>
        /// The pot that funds a settlement's militia, and against which its affordability is judged. Each
        /// kind of place pays from the purse the spec names: a village and a castle from their single
        /// settlement wealth, a town from its citizens' market money -- BACKED by its own treasury.
        /// </summary>
        /// <remarks>
        /// A town's treasury backstops the citizen pot: when the market is bare -- most sharply just after
        /// a storm, where the sack strips citizen wealth but spares the treasury -- the fief funds militia
        /// from the treasury rather than fielding none while it visibly holds gold. <see cref="DebitFundingPot"/>
        /// spends the two pots in the same order this reads them, so the affordability floor and the actual
        /// draw stay in step and no militiaman is armed for free.
        /// </remarks>
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
            if (settlement.IsTown)
            {
                // Citizens pay first; the treasury backstops the remainder, in the same order
                // MaintenancePot reads the two pots -- so what the floor judged affordable is exactly
                // what can be drawn.
                int paid = SettlementWealth.DebitCitizens(settlement, amount, SettlementWealth.Source.Militia);
                int remaining = amount - paid;
                if (remaining > 0)
                {
                    paid += SettlementWealth.Debit(settlement, remaining, SettlementWealth.Source.Militia);
                }
                return paid;
            }
            return SettlementWealth.Debit(settlement, amount, SettlementWealth.Source.Militia);
        }

        /// <summary>
        /// Returns <paramref name="amount"/> to the settlement's militia funding pot, the inverse of arming
        /// a man from it, routed by how that arming moved the money. Returns what was actually credited.
        ///
        ///   * TOWN -- only with the recruit-supply draw off, when its citizens armed their watch out of
        ///     market money; the refund re-enters that same citizen wealth. With the draw on a town arms
        ///     from its own shelves and gets gear back instead (see RefundPendingDecline).
        ///
        ///   * CASTLE -- the kit was sourced from beyond its walls and the coin left its wealth; the refund
        ///     re-enters that wealth.
        ///
        ///   * VILLAGE -- it did not buy from the outside world but from its trade-bound town, paying that
        ///     town's citizens. So the refund is a STRICT reversal, not a mint: the money is pulled back out
        ///     of that town's citizen wealth and returned to the village, and the village recovers only what
        ///     the town can actually give -- no denar is invented.
        /// </summary>
        private static int CreditFundingPot(Settlement settlement, int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }
            if (settlement.IsTown)
            {
                return SettlementWealth.CreditCitizens(settlement, amount, SettlementWealth.Source.Militia);
            }
            if (settlement.IsVillage)
            {
                Settlement market = RecruitSupply.GetSupplyMarket(settlement);
                if (market == null || market == settlement)
                {
                    return 0;
                }
                int fromTown = SettlementWealth.DebitCitizens(market, amount, SettlementWealth.Source.Militia);
                return fromTown > 0
                    ? SettlementWealth.Credit(settlement, fromTown, SettlementWealth.Source.Militia)
                    : 0;
            }
            return SettlementWealth.Credit(settlement, amount, SettlementWealth.Source.Militia);
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
            // Fortifications: the same armoury and covered walkways that spare the garrison spare the watch.
            // −0/5/10% at levels 1/2/3.
            wageFactor *= BuildingEffects.MaintenanceFactor(settlement.Town);
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
        /// The manpower both militia caps are sized on: a town's prosperity; a village's hearths; a castle's
        /// countryside -- the AVERAGE hearth of its bound villages, the same notion the castle's resting
        /// prosperity is built on (<see cref="RBMProsperityEquilibrium.CastleTargetProsperity"/>, divided back
        /// by its <see cref="RBMProsperityEquilibrium.CastleProsperityHearthFactor"/>). A castle with no bound
        /// villages falls back to its own prosperity over that same factor -- the average hearth its
        /// prosperity implies -- so a lone keep is sized on the scale its villages would have given it. Zero
        /// (no cap) for anything else.
        /// </summary>
        public static float MilitiaCapBase(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0f;
            }
            if (settlement.IsVillage)
            {
                return settlement.Village != null ? settlement.Village.Hearth : 0f;
            }
            if (settlement.IsCastle)
            {
                float scaled = RBMProsperityEquilibrium.CastleTargetProsperity(settlement);
                if (scaled <= 0f)
                {
                    scaled = Prosperity(settlement);
                }
                return scaled / RBMProsperityEquilibrium.CastleProsperityHearthFactor;
            }
            if (settlement.IsTown)
            {
                return Prosperity(settlement);
            }
            return 0f;
        }

        /// <summary>
        /// The share of <see cref="MilitiaCapBase"/> this settlement's SOFT cap sits at:
        /// <see cref="MilitiaSoftCapShare"/> plus its building and policy bonuses, clamped to
        /// [0, <see cref="MilitiaSoftCapMaxShare"/>].
        /// </summary>
        public static float MilitiaSoftCapShareFor(Settlement settlement)
        {
            float share = MilitiaSoftCapShare + SoftCapBuildingBonus(settlement) + SoftCapPolicyBonus(settlement);
            return MBMath.ClampFloat(share, 0f, MilitiaSoftCapMaxShare);
        }

        /// <summary>
        /// The soft cap on this settlement's militia -- the count past which growth starts to taper toward
        /// the hard cap. <see cref="MilitiaCapBase"/> times <see cref="MilitiaSoftCapShareFor"/>; zero (no
        /// cap) for anything that is not a town, castle or village.
        /// </summary>
        public static float MilitiaCap(Settlement settlement)
        {
            return MilitiaCapBase(settlement) * MilitiaSoftCapShareFor(settlement);
        }

        /// <summary>
        /// The hard cap on this settlement's militia: <see cref="MilitiaHardCapShare"/> of
        /// <see cref="MilitiaCapBase"/>, the same share everywhere. Growth never carries the count past it,
        /// and a count above it disbands the excess (<see cref="OverflowDrain"/>).
        /// </summary>
        public static float MilitiaHardCap(Settlement settlement)
        {
            return MilitiaCapBase(settlement) * MilitiaHardCapShare;
        }

        /// <summary>
        /// The soft-cap bonus a fief's buildings and running daily project give, as a share of the base:
        /// Barracks, the castle Guard House, Training Fields, and Train Militia / Raise Troops while running.
        /// Towns and castles only -- a village builds nothing of its own.
        /// </summary>
        private static float SoftCapBuildingBonus(Settlement settlement)
        {
            if (settlement == null || !(settlement.IsTown || settlement.IsCastle) || settlement.Town == null)
            {
                return 0f;
            }
            Town town = settlement.Town;
            float bonus = LevelBonus(BuildingEffects.Barracks(town),
                MilitiaSoftCapBarracksL1, MilitiaSoftCapBarracksL2, MilitiaSoftCapBarracksL3);
            if (settlement.IsCastle)
            {
                bonus += LevelBonus(BuildingEffects.CastleGuardHouseTier(town),
                    MilitiaSoftCapGuardHouseL1, MilitiaSoftCapGuardHouseL2, MilitiaSoftCapGuardHouseL3);
            }
            bonus += MilitiaSoftCapTrainingFieldsPerLevel * BuildingEffects.TrainingFields(town);
            if (BuildingEffects.IsDailyProjectActive(town, DefaultBuildingTypes.SettlementDailyTrainMilitia)
                || BuildingEffects.IsDailyProjectActive(town, DefaultBuildingTypes.CastleDailyRaiseTroops))
            {
                bonus += MilitiaSoftCapDailyProject;
            }
            return bonus;
        }

        /// <summary>The value for a building's level 1/2/3, 0 below level 1.</summary>
        private static float LevelBonus(int level, float l1, float l2, float l3)
        {
            switch (level)
            {
                case 1: return l1;
                case 2: return l2;
                case 3: return l3;
                default: return 0f;
            }
        }

        /// <summary>
        /// The soft-cap bonus the owner clan's kingdom policies give, as a share of the base: Citizenship,
        /// Cantons and War Sails' Bolster the Fyrd raise it, Serfdom lowers it. Towns, castles and villages
        /// alike (a village answers to its owner clan's kingdom); nothing for a settlement outside a kingdom.
        /// </summary>
        private static float SoftCapPolicyBonus(Settlement settlement)
        {
            Kingdom kingdom = (settlement != null && settlement.OwnerClan != null) ? settlement.OwnerClan.Kingdom : null;
            if (kingdom == null || kingdom.ActivePolicies == null)
            {
                return 0f;
            }
            float bonus = 0f;
            foreach (PolicyObject policy in kingdom.ActivePolicies)
            {
                if (policy == null)
                {
                    continue;
                }
                if (policy == DefaultPolicies.Citizenship)
                {
                    bonus += MilitiaSoftCapCitizenship;
                }
                else if (policy == DefaultPolicies.Cantons)
                {
                    bonus += MilitiaSoftCapCantons;
                }
                else if (policy == DefaultPolicies.Serfdom)
                {
                    bonus += MilitiaSoftCapSerfdom;
                }
                else if (policy.StringId == BolsterTheFyrdPolicyId)
                {
                    bonus += MilitiaSoftCapBolsterTheFyrd;
                }
            }
            return bonus;
        }

        /// <summary>
        /// Men an over-hard-cap watch disbands in a day, judged on a count of <paramref name="militia"/>:
        /// <see cref="MilitiaOverflowDrainRate"/> of the overflow, at least <see cref="MilitiaOverflowDrainMin"/>,
        /// never more than the overflow itself. Zero at or under the hard cap, or with no cap to measure.
        /// </summary>
        public static float OverflowDrain(Settlement settlement, float militia)
        {
            float hardCap = MilitiaHardCap(settlement);
            if (hardCap <= 0f || militia <= hardCap)
            {
                return 0f;
            }
            float overflow = militia - hardCap;
            float drain = MathF.Max(MilitiaOverflowDrainMin, overflow * MilitiaOverflowDrainRate);
            return MathF.Min(drain, overflow);
        }

        /// <summary>A raided or otherwise abnormal village musters nothing, and loses no one to the day's model, as in vanilla.</summary>
        private static bool IsMusterHalted(Settlement settlement)
        {
            return settlement.IsVillage && settlement.Village != null
                && settlement.Village.VillageState != Village.VillageStates.Normal;
        }

        /// <summary>
        /// The new-campaign seed count: where vanilla's curve would have settled, with the base muster and
        /// manpower intake balanced against vanilla's retirement drag (<see cref="MilitiaSeedRetirementRate"/>)
        /// -- a town on ~Prosperity/25, a village on ~Hearth/10. RBM's live curve has no retirement, so
        /// this is a starting point the watch grows up from, not a level it holds; it is clamped to the
        /// soft cap for safety. The kept vanilla modifiers (loyalty, policies, perks, market weapons) are
        /// not folded in -- at campaign start they are near-zero.
        /// </summary>
        public static float EquilibriumMilitia(Settlement settlement)
        {
            // MilitiaSeedRetirementRate is a positive const, so the intake/rate division is always safe.
            float intake;
            if (settlement.IsVillage)
            {
                float hearth = settlement.Village != null ? settlement.Village.Hearth : 0f;
                intake = BaseMilitiaVillage + hearth * MilitiaPerHearth;
            }
            else if (settlement.IsFortification)
            {
                intake = BaseMilitiaFortification + Prosperity(settlement) * MilitiaPerProsperity;
            }
            else
            {
                return 0f;
            }
            float eq = intake / MilitiaSeedRetirementRate;
            float cap = MilitiaCap(settlement);
            return (cap > 0f && eq > cap) ? cap : eq;
        }

        /// <summary>
        /// Replaces vanilla's militia-change model outright: RBM authors the whole day's number, so the
        /// settlement's growth, decline and cap are all RBM's, not vanilla's shaved at the edges.
        /// </summary>
        /// <remarks>
        /// A <c>Prefix</c> that returns <see langword="false"/> and hands back its own
        /// <see cref="ExplainedNumber"/>, so vanilla's <c>CalculateMilitiaChangeInternal</c> never runs.
        /// The number is built in three layers:
        ///
        ///   * THE BASE CURVE (RBM-owned). The muster/retirement/hearth/prosperity spine, rebuilt from
        ///     RBM's own constants (<see cref="BaseMilitiaFortification"/>, <see cref="MilitiaSeedRetirementRate"/>,
        ///     <see cref="MilitiaPerHearth"/>, <see cref="MilitiaPerProsperity"/>) seeded to vanilla's
        ///     values -- this is the growth/decline RBM now dials.
        ///
        ///   * THE KEPT MODIFIERS (vanilla flavour). Loyalty, market weapons, the Battanian feat, building
        ///     effects, governor perks and settlement issues, reproduced from the same public API vanilla
        ///     uses so a governor's perk still reads on the breakdown exactly as before (see
        ///     <see cref="AddKeptModifiers"/>). The militia policies now move the soft cap instead.
        ///
        ///   * RBM'S CEILING AND FLOOR (<see cref="ApplyCeilingAndFloor"/>). Between the SOFT cap and the
        ///     HARD cap positive growth tapers by (1 - fill)^2, and it may never carry the count past the
        ///     hard cap; a watch already over the hard cap disbands its excess. And an AFFORDABILITY FLOOR --
        ///     a settlement that cannot arm a new man raises none, and one that cannot keep twenty days of its
        ///     militia's maintenance in the pot sheds men whatever its cap says, because a market spent dry
        ///     SHOULD start losing its watch.
        ///
        /// While War Sails is loaded its model wraps this one and adds its own terms on top; see
        /// <see cref="NavalMilitiaChangePatch"/>, which asks this prefix for the growth stage alone
        /// (<see cref="_growthOnlyDepth"/>) so the ceiling and floor still land last.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultSettlementMilitiaModel), "CalculateMilitiaChange")]
        private static class MilitiaChangePatch
        {
            private static bool Prefix(Settlement settlement, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
                {
                    return true;
                }
                __result = (_growthOnlyDepth > 0)
                    ? ComputeMilitiaGrowth(settlement, includeDescriptions)
                    : ComputeMilitiaChange(settlement, includeDescriptions);
                return false;
            }
        }

        /// <summary>
        /// Set while <see cref="NavalMilitiaChangePatch"/> is asking the model chain beneath War Sails' model
        /// for the day's GROWTH alone: <see cref="MilitiaChangePatch"/> then returns
        /// <see cref="ComputeMilitiaGrowth"/> without the ceiling and floor, which the naval patch applies
        /// itself once War Sails' own term is in. A depth counter rather than a flag so a re-entrant call
        /// unwinds cleanly; thread-static because a militia query may in principle come off another thread.
        /// </summary>
        [System.ThreadStatic]
        private static int _growthOnlyDepth;

        /// <summary>
        /// Builds a settlement's whole daily militia change: the growth stage (RBM's base curve and the kept
        /// vanilla modifiers), then RBM's ceiling and floor.
        /// </summary>
        private static ExplainedNumber ComputeMilitiaChange(Settlement settlement, bool includeDescriptions)
        {
            ExplainedNumber result = ComputeMilitiaGrowth(settlement, includeDescriptions);
            if (!IsMusterHalted(settlement))
            {
                ApplyCeilingAndFloor(settlement, ref result);
            }
            return result;
        }

        /// <summary>
        /// The GROWTH stage of the day's militia change: RBM's base curve, the understrength and
        /// prosperous-city musters, the Barracks intake and the kept vanilla modifiers -- everything that
        /// adds or takes men before the caps and the purse have their say (<see cref="ApplyCeilingAndFloor"/>).
        /// Empty for a raided village.
        /// </summary>
        private static ExplainedNumber ComputeMilitiaGrowth(Settlement settlement, bool includeDescriptions)
        {
            ExplainedNumber result = new ExplainedNumber(0f, includeDescriptions);

            // A raided or otherwise abnormal village musters nothing, as in vanilla.
            if (IsMusterHalted(settlement))
            {
                return result;
            }

            float militia = settlement.Militia;

            // --- Base curve (RBM-owned): flat muster and manpower intake. No retirement: the watch only
            // shrinks through losses, the affordability shed or the over-hard-cap drain, and only grows while
            // the caps and the pot allow. Ride both caps in the base line's label -- where muster starts to
            // taper and where it stops -- so the tooltip always shows what the watch is growing toward, the
            // way the garrison names its cap.
            TextObject baseLine = BaseText;
            if (includeDescriptions)
            {
                float softCap = MilitiaCap(settlement);
                if (softCap > 0f)
                {
                    baseLine = new TextObject("{=RBM_militia_base_caps}Base (soft cap {SOFT}, hard cap {HARD})");
                    baseLine.SetTextVariable("SOFT", (int)softCap);
                    baseLine.SetTextVariable("HARD", (int)MilitiaHardCap(settlement));
                }
            }
            if (settlement.IsFortification)
            {
                result.Add(BaseMilitiaFortification, baseLine);
            }
            else if (settlement.IsVillage)
            {
                result.Add(BaseMilitiaVillage, baseLine);
            }

            if (settlement.IsVillage)
            {
                result.Add(settlement.Village.Hearth * MilitiaPerHearth, FromHearthsText);
            }
            else if (settlement.IsFortification)
            {
                float fromProsperity = settlement.Town.Prosperity * MilitiaPerProsperity;
                result.Add(fromProsperity, FromProsperityText);

                // Rebellious low loyalty boosts the watch, scaled off the prosperity intake, as in vanilla.
                if (settlement.Town.InRebelliousState)
                {
                    SettlementLoyaltyModel loyaltyModel = Campaign.Current.Models.SettlementLoyaltyModel;
                    float boostPct = MBMath.Map(settlement.Town.Loyalty, 0f,
                        loyaltyModel.RebelliousStateStartLoyaltyThreshold, loyaltyModel.MilitiaBoostPercentage, 0f);
                    result.Add(MathF.Abs(fromProsperity * (boostPct * 0.01f)), LowLoyaltyText);
                }
            }

            // --- Understrength catch-up: a gutted watch (below half its soft cap) musters far faster.
            AddUnderstrengthMuster(settlement, militia, ref result);

            // --- Prosperous city: Prosperity / 50 more a day per luxury tier the citizens' savings have reached.
            AddProsperousCityMuster(settlement, ref result);

            // --- Barracks: the lodgings that let a fief take in more garrison recruits also let it drill
            // more of its own townsmen. +1/2/3 a day at levels 1/2/3. Held to zero by the affordability
            // floor below when the pot cannot arm them, so it is a ceiling on intake and not free men.
            int barracks = BuildingEffects.BarracksGrowth(settlement.Town);
            if (barracks > 0)
            {
                result.Add(barracks, BarracksText);
            }

            // --- Kept modifiers (vanilla flavour, reproduced from public API).
            AddKeptModifiers(settlement, ref result);

            return result;
        }

        /// <summary>
        /// RBM's CEILING AND FLOOR, applied last to the day's change -- after the growth stage, and after any
        /// term a wrapping model (War Sails, see <see cref="NavalMilitiaChangePatch"/>) added on top of it:
        ///
        ///   * TAPER -- between the soft and the hard cap positive growth is multiplied by (1 - fill)^2,
        ///     fill = (militia - soft) / (hard - soft), shown as one "Over muster" line.
        ///   * HARD CLAMP -- growth may not carry the count past the hard cap.
        ///   * ARMING -- a settlement that cannot arm a new man raises none (<see cref="CanAffordSpawn"/>).
        ///   * OVERFLOW DRAIN -- a count over the hard cap disbands <see cref="OverflowDrain"/> men a day.
        ///   * UNPAID SHED -- a settlement that cannot keep its militia sheds <see cref="MilitiaShedPerDay"/>.
        ///
        /// The two declines do not stack: the day's change is the more negative of the two (the drain is
        /// always at least one man, so the shed only bites under the hard cap). Both are refunded their kit
        /// (see <see cref="RecordMilitiaChange"/>).
        /// </summary>
        private static void ApplyCeilingAndFloor(Settlement settlement, ref ExplainedNumber result)
        {
            float militia = settlement.Militia;
            ApplySoftCapTaper(settlement, militia, ref result);
            ApplyHardCap(settlement, militia, ref result);

            // A settlement that cannot arm a new militiaman fields no new ones -- growth is held to zero,
            // though the men it already has are left standing (the maintenance shed below thins those, when
            // even their upkeep cannot be met).
            if (result.ResultNumber > 0f && !CanAffordSpawn(settlement))
            {
                result.Add(-result.ResultNumber, CannotArmText);
            }

            float drain = OverflowDrain(settlement, militia);
            if (drain > 0f && result.ResultNumber > -drain)
            {
                result.Add(-drain - result.ResultNumber, OverflowText);
            }

            if (!CanKeepMilitia(settlement) && result.ResultNumber > -MilitiaShedPerDay)
            {
                result.Add(-MilitiaShedPerDay - result.ResultNumber, UnaffordableText);
            }
        }

        /// <summary>
        /// Adds the vanilla militia modifiers RBM keeps -- market weapons, the Battanian feat, building
        /// effects, governor perks and settlement issues -- from the same public API vanilla uses, so they
        /// read on the breakdown exactly as before. The base muster/retirement/intake spine is NOT here: that
        /// is RBM's, added in <see cref="ComputeMilitiaGrowth"/>. Nor are the Serfdom, Cantons and Citizenship
        /// policies' flat men a day: those now move the soft cap instead (<see cref="SoftCapPolicyBonus"/>).
        /// </summary>
        private static void AddKeptModifiers(Settlement settlement, ref ExplainedNumber result)
        {
            if (settlement.IsTown)
            {
                int soldToMilitia = settlement.Town.SoldItems.Sum(
                    (Town.SellLog x) => (x.Category.Properties == ItemCategory.Property.BonusToMilitia) ? x.Number : 0);
                if (soldToMilitia > 0)
                {
                    result.Add(0.2f * soldToMilitia, MilitiaFromMarketText);
                }
                if (settlement.OwnerClan.Culture.HasFeat(DefaultCulturalFeats.BattanianMilitiaFeat))
                {
                    result.Add(DefaultCulturalFeats.BattanianMilitiaFeat.EffectBonus, CultureText);
                }
            }

            if (settlement.IsCastle || settlement.IsTown)
            {
                AddMilitiaEffectOfBuildings(settlement.Town, ref result);
                if (settlement.IsCastle && settlement.Town.InRebelliousState)
                {
                    settlement.Town.AddEffectOfBuildings(BuildingEffectEnum.MilitiaReduction, ref result);
                }

                if (settlement.Town.Governor != null)
                {
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.OneHanded.SwiftStrike, settlement.Town, isPrimaryBonus: false, ref result);
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.Polearm.KeepAtBay, settlement.Town, isPrimaryBonus: false, ref result);
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.Bow.MerryMen, settlement.Town, isPrimaryBonus: false, ref result);
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.Crossbow.LongShots, settlement.Town, isPrimaryBonus: false, ref result);
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.Throwing.SlingingCompetitions, settlement.Town, isPrimaryBonus: false, ref result);
                    if (settlement.IsUnderSiege)
                    {
                        PerkHelper.AddPerkBonusForTown(DefaultPerks.Roguery.ArmsDealer, settlement.Town, isPrimaryBonus: false, ref result);
                    }
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.Steward.SevenVeterans, settlement.Town, isPrimaryBonus: false, ref result);
                }

                Campaign.Current.Models.IssueModel.GetIssueEffectsOfSettlement(
                    DefaultIssueEffects.SettlementMilitia, settlement, ref result);
            }
        }

        /// <summary>
        /// Vanilla's <c>Militia</c> building effect, minus the castle Guard House's share of it.
        ///
        /// A castle Guard House adds +1/2/3 militia a day in vanilla, which under RBM would be a second,
        /// free intake channel sitting beside the Barracks -- the building RBM makes responsible for how
        /// many men a fief can take in and settle in a day (see <see cref="BuildingEffects.BarracksGrowth"/>).
        /// Two buildings paying the same currency makes neither choice mean anything, so the Guard House
        /// keeps its gaol and its tariff and stops raising the watch -- it lifts the militia SOFT CAP
        /// instead (<see cref="MilitiaSoftCapGuardHouseL1"/>), how many men the keep can hold under arms
        /// rather than how fast they come. Every other building's Militia
        /// contribution -- the town Guard House has none -- passes through untouched.
        /// </summary>
        private static void AddMilitiaEffectOfBuildings(Town town, ref ExplainedNumber result)
        {
            bool skipGuardHouse = RBMConfig.RBMConfig.rbmCampaignEnabled;
            foreach (Building building in town.Buildings)
            {
                if (skipGuardHouse && building.BuildingType == DefaultBuildingTypes.CastleGuardHouse)
                {
                    continue;
                }
                building.AddEffectOfBuilding(BuildingEffectEnum.Militia, ref result);
            }
        }

        /// <summary>
        /// Adds the understrength catch-up muster: while a settlement's watch sits below
        /// <see cref="MilitiaUnderstrengthThreshold"/> of its effective soft cap (bonuses included), a large
        /// extra intake is raised on top of the base curve, sized on a city's or castle's prosperity or a
        /// village's hearths. Nothing is added at or above the threshold, or where there is no cap to
        /// measure against. This is ordinary positive growth, so the caps still taper and stop it and the
        /// affordability floor (<see cref="CanAffordSpawn"/>) still gates it -- money is still needed to
        /// arm the men it musters.
        /// </summary>
        private static void AddUnderstrengthMuster(Settlement settlement, float militia, ref ExplainedNumber result)
        {
            float cap = MilitiaCap(settlement);
            if (cap <= 0f || militia >= cap * MilitiaUnderstrengthThreshold)
            {
                return;
            }

            float bonus;
            if (settlement.IsVillage)
            {
                float hearth = settlement.Village != null ? settlement.Village.Hearth : 0f;
                bonus = hearth * MilitiaCatchUpPerHearthVillage;
            }
            else if (settlement.IsCastle)
            {
                bonus = Prosperity(settlement) * MilitiaCatchUpPerProsperityCastle;
            }
            else if (settlement.IsTown)
            {
                bonus = Prosperity(settlement) * MilitiaCatchUpPerProsperityCity;
            }
            else
            {
                return;
            }

            if (bonus > 0f)
            {
                result.Add(bonus, UnderstrengthText);
            }
        }

        /// <summary>
        /// Adds the prosperous-city muster: <see cref="MilitiaPerProsperityPerLuxuryTier"/> of the city's
        /// prosperity per luxury tier (small / medium / large) its citizens' savings have reached, read off
        /// the same <see cref="CitizenDemand.SavingsInDaysOfIncome"/> thresholds the households shop on.
        /// Towns only -- castles and villages have no citizen ledger to measure. Ordinary positive growth:
        /// the caps taper and stop it and <see cref="CanAffordSpawn"/> still gates it.
        /// </summary>
        private static void AddProsperousCityMuster(Settlement settlement, ref ExplainedNumber result)
        {
            if (!settlement.IsTown || settlement.Town == null)
            {
                return;
            }
            float savings = CitizenDemand.SavingsInDaysOfIncome(settlement.Town);
            int tiers = (savings >= CitizenDemand.SmallLuxuryDays ? 1 : 0)
                + (savings >= CitizenDemand.MediumLuxuryDays ? 1 : 0)
                + (savings >= CitizenDemand.LargeLuxuryDays ? 1 : 0);
            if (tiers <= 0)
            {
                return;
            }
            float bonus = Prosperity(settlement) * MilitiaPerProsperityPerLuxuryTier * tiers;
            if (bonus > 0f)
            {
                result.Add(bonus, ProsperousCityText);
            }
        }

        /// <summary>
        /// Tapers the day's growth between the soft and the hard cap: fill = clamp01((militia - soft) /
        /// (hard - soft)), and the growth is multiplied by (1 - fill)^2 -- full rate at the soft cap, a
        /// quarter halfway, nothing at the hard cap. One "Over muster" line carries the cut. Leaves a
        /// settlement at or under its soft cap, or one already losing men, untouched.
        /// </summary>
        private static void ApplySoftCapTaper(Settlement settlement, float militia, ref ExplainedNumber result)
        {
            if (result.ResultNumber <= 0f)
            {
                return;
            }
            float softCap = MilitiaCap(settlement);
            float hardCap = MilitiaHardCap(settlement);
            if (softCap <= 0f || militia <= softCap || hardCap <= softCap)
            {
                return;
            }
            float fill = MBMath.ClampFloat((militia - softCap) / (hardCap - softCap), 0f, 1f);
            float keep = (1f - fill) * (1f - fill);
            result.Add(-(1f - keep) * result.ResultNumber, OverCapText);
        }

        /// <summary>
        /// Clamps the day's growth so the count never crosses the hard cap (<see cref="MilitiaHardCap"/>).
        /// Growth that would land under it passes untouched; growth that would overshoot is cut to exactly
        /// what fills it; a settlement already at or over it raises no one (and disbands its excess, see
        /// <see cref="ApplyCeilingAndFloor"/>). Decline is never touched here, and neither is a settlement
        /// with no cap to measure against.
        /// </summary>
        private static void ApplyHardCap(Settlement settlement, float militia, ref ExplainedNumber result)
        {
            if (result.ResultNumber <= 0f)
            {
                return;
            }
            float hardCap = MilitiaHardCap(settlement);
            if (hardCap <= 0f)
            {
                return;
            }
            float room = hardCap - militia;
            if (room < 0f)
            {
                room = 0f;
            }
            if (result.ResultNumber > room)
            {
                result.Add(room - result.ResultNumber, HardCapText);
            }
        }

        // ------------------------------------------------------------------ War Sails (NavalDLC)

        /// <summary>
        /// War Sails replaces the militia model with a decorator (<c>NavalDLCSettlementMilitiaModel</c>) that
        /// calls the base model -- RBM's prefixed result, already capped and floored -- and only then adds the
        /// Boatswain's Accuracy Training governor perk (+2 a day, coastal towns and villages bound to a port
        /// town) and a +25% factor for the Bolster the Fyrd policy. Both escaped every RBM cap and floor: the
        /// perk could push a town over its hard cap or lift an unpayable watch's shed, and the factor
        /// multiplied the shed and the drain along with the growth.
        ///
        /// So this prefix takes the decorator's place and rebuilds it in RBM's order: the model chain beneath
        /// it (normally RBM's default-model prefix) is asked for the GROWTH stage alone via
        /// <see cref="_growthOnlyDepth"/>, the perk is added exactly as War Sails adds it, and the ceiling and
        /// floor (<see cref="ApplyCeilingAndFloor"/>) land last. Bolster the Fyrd's factor is dropped -- the
        /// policy lifts the soft cap instead (<see cref="MilitiaSoftCapBolsterTheFyrd"/>).
        ///
        /// Reflected onto the DLC type by name, and the perk looked up by its string id, so RBM keeps no
        /// build- or load-time dependency on an optional module: with War Sails absent the type does not
        /// resolve, <see cref="Prepare"/> returns false and the patch is never applied. The target type has no
        /// static initialiser, so unlike the default model it need not be held back until a game is live.
        /// </summary>
        /// <remarks>
        /// The trade of a skip-prefix: a later War Sails build adding a third term to this method would be
        /// skipped until mirrored here. The alternative -- a postfix -- cannot take a factor back out of an
        /// <see cref="ExplainedNumber"/> nor put the caps after the perk, so it could not do the job.
        /// </remarks>
        [HarmonyPatch]
        private static class NavalMilitiaChangePatch
        {
            private const string NavalModelTypeName = "NavalDLC.GameComponents.NavalDLCSettlementMilitiaModel";

            /// <summary>War Sails' Boatswain perk "Accuracy Training", by the string id <c>NavalPerks</c> registers it under.</summary>
            private const string AccuracyTrainingPerkId = "Accuracytraining";

            private static System.Func<MBGameModel<SettlementMilitiaModel>, SettlementMilitiaModel> _baseModelGetter;

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static System.Reflection.MethodBase TargetMethod()
            {
                return AccessTools.Method(NavalModelTypeName + ":CalculateMilitiaChange");
            }

            private static bool Prefix(object __instance, Settlement settlement, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
                {
                    return true;
                }

                // The growth stage from the chain beneath War Sails, so any other mod's model in between keeps
                // its say; straight from RBM if the base model cannot be read.
                SettlementMilitiaModel baseModel = BaseModelOf(__instance as SettlementMilitiaModel);
                ExplainedNumber result;
                _growthOnlyDepth++;
                try
                {
                    result = (baseModel != null)
                        ? baseModel.CalculateMilitiaChange(settlement, includeDescriptions)
                        : ComputeMilitiaGrowth(settlement, includeDescriptions);
                }
                finally
                {
                    _growthOnlyDepth--;
                }

                // A raided village musters nothing -- not even the perk's men -- and the model takes no one.
                if (!IsMusterHalted(settlement))
                {
                    AddAccuracyTraining(settlement, ref result);
                    ApplyCeilingAndFloor(settlement, ref result);
                }
                __result = result;
                return false;
            }

            /// <summary>The decorator's protected <c>BaseModel</c>, through a delegate built once.</summary>
            private static SettlementMilitiaModel BaseModelOf(SettlementMilitiaModel model)
            {
                if (model == null)
                {
                    return null;
                }
                if (_baseModelGetter == null)
                {
                    System.Reflection.MethodInfo getter = AccessTools.PropertyGetter(
                        typeof(MBGameModel<SettlementMilitiaModel>), "BaseModel");
                    if (getter == null)
                    {
                        return null;
                    }
                    _baseModelGetter = AccessTools.MethodDelegate<System.Func<MBGameModel<SettlementMilitiaModel>, SettlementMilitiaModel>>(getter);
                }
                return _baseModelGetter(model);
            }

            /// <summary>
            /// War Sails' Accuracy Training governor perk, reproduced from <c>NavalDLCSettlementMilitiaModel</c>:
            /// a coastal town, or a village bound to one, whose resident governor has it gains the perk's
            /// secondary bonus through the standard town-perk helper.
            /// </summary>
            private static void AddAccuracyTraining(Settlement settlement, ref ExplainedNumber result)
            {
                PerkObject perk = MBObjectManager.Instance != null
                    ? MBObjectManager.Instance.GetObject<PerkObject>(AccuracyTrainingPerkId)
                    : null;
                if (perk == null)
                {
                    return;
                }
                if (settlement.IsTown && settlement.HasPort)
                {
                    PerkHelper.AddPerkBonusForTown(perk, settlement.Town, isPrimaryBonus: false, ref result);
                }
                else if (settlement.IsVillage && settlement.Village != null && settlement.Village.Bound != null)
                {
                    // v1.5: War Sails now routes the village bonus through the town-perk helper too, which
                    // also requires the governor to be in residence.
                    Town town = settlement.Village.Bound.Town;
                    if (town != null && town.Settlement.HasPort)
                    {
                        PerkHelper.AddPerkBonusForTown(perk, town, isPrimaryBonus: false, ref result);
                    }
                }
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

        /// <summary>
        /// The mirror of <see cref="_pendingGrowth"/> for men disbanded -- by the affordability floor or the
        /// over-hard-cap drain: a settlement shedding militia it can no longer pay for, or standing over its
        /// hard cap, banks each disbanded man here, so his kit's cost can be
        /// returned to the funding pot later (see <see cref="RefundPendingDecline"/>) -- out of the same
        /// village suppression window the arming charge is, and in whole men so a coin is refunded only once
        /// a whole man has actually drifted home. Combat losses never touch this: they happen in battle, not
        /// in a fief's DailyTick, so they are not measured here at all.
        /// </summary>
        private static readonly Dictionary<Settlement, float> _pendingRefund = new Dictionary<Settlement, float>();

        /// <summary>Drops the accumulators before a new campaign's settlements take their place.</summary>
        public static void ResetForNewSession()
        {
            _pendingGrowth.Clear();
            _pendingRefund.Clear();
        }

        /// <summary>
        /// Opens a new campaign with every settlement holding the steady state of RBM's base growth curve
        /// (<see cref="EquilibriumMilitia"/>), in place of vanilla's cap-blind <c>MilitiaChange * 45</c>.
        /// So each place starts on the strength it will actually keep and neither swells nor bleeds from
        /// turn one. New-game path only -- it overwrites the live militia count, which a loaded save must
        /// keep -- and after hearths and prosperity are built, so the equilibrium reads real numbers.
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
                float eq = EquilibriumMilitia(settlement);
                if (eq <= 0f)
                {
                    continue;
                }
                settlement.Militia = eq;
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
            // Barracks discount included, so the gate that decides whether a man can be raised is priced
            // exactly as the charge that raises him (see ArmOneMilitiaman).
            float full = MilitiaKitValue(troop) * BuildingEffects.SpawnCostFactor(settlement != null ? settlement.Town : null);
            return (settlement != null && settlement.IsVillage)
                ? (int)(full * MilitiaVillageGearShare)
                : (int)full;
        }

        /// <summary>
        /// One militiaman's kit worth, before the barracks discount and the village share. With the market
        /// draw on, men are armed off a market and his kit is worth what the draw values it at -- and so is
        /// the abstract charge of a castle with no town in reach, so every path agrees with the gate.
        /// With the draw off, every path is the abstract charge at the averaged kit value.
        /// </summary>
        private static int MilitiaKitValue(CharacterObject troop)
        {
            return RecruitSupply.IsEnabled
                ? RecruitSupply.DrawnKitValue(troop, false)
                : SpoilsPool.GetEquipmentValue(troop);
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
        /// Banks the day's militia change the moment it is applied, splitting it by sign. This is the only
        /// militia change that happens inside a fief's DailyTick -- an escort borrowing men (see
        /// <c>VillagerEscort</c>) moves them on other events, and combat losses happen in battle, so
        /// measuring the DailyTick delta catches the model's own muster and shedding and nothing else.
        ///
        ///   * GROWTH accrues to <see cref="_pendingGrowth"/>, to be armed and paid for later out of the
        ///     village suppression window (see <see cref="ChargePendingSpawn"/>).
        ///
        ///   * DECLINE accrues to <see cref="_pendingRefund"/> when the watch was disbanded rather than
        ///     simply lost: in full when the settlement cannot keep its militia (<see cref="CanKeepMilitia"/>
        ///     is false) -- the affordability floor thinning a watch the pot can no longer pay for -- and
        ///     otherwise up to the over-hard-cap drain (<see cref="OverflowDrain"/>, judged on the count the
        ///     day began with), so only the men sent home for standing over the cap are counted, not a
        ///     negative modifier's. Their kit cost is returned to the funding pot later (see
        ///     <see cref="RefundPendingDecline"/>). Any other decline on a settlement that CAN still pay is
        ///     left alone: those men keep their kit.
        /// </summary>
        private static void RecordMilitiaChange(Settlement settlement, float preMilitia)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
            {
                return;
            }
            float delta = settlement.Militia - preMilitia;
            if (delta > 0f)
            {
                float acc;
                _pendingGrowth.TryGetValue(settlement, out acc);
                _pendingGrowth[settlement] = acc + delta;
            }
            else if (delta < 0f)
            {
                float lost = -delta; // the positive count shed
                float refunded = CanKeepMilitia(settlement)
                    ? MathF.Min(lost, OverflowDrain(settlement, preMilitia))
                    : lost;
                if (refunded > 0f)
                {
                    float acc;
                    _pendingRefund.TryGetValue(settlement, out acc);
                    _pendingRefund[settlement] = acc + refunded;
                }
            }
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
                    RecordMilitiaChange(__instance.Settlement, __state);
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
                    RecordMilitiaChange(__instance.Settlement, __state);
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

            // Resolved once for the batch rather than once per man: it is a distance sweep over every town
            // on the map, and arming a man neither moves the castle nor changes who its faction is at war
            // with, so every man of the batch got the same answer anyway. Asked under exactly the condition
            // ArmOneMilitiaman asked it per man -- a castle, with the draw feature on.
            Settlement castleMarket = (settlement.IsCastle && RecruitSupply.IsEnabled)
                ? ResolveCastleSupplyMarket(settlement)
                : null;
            // One priced view of the arming market shared by every man of the batch, so its stall is priced
            // once rather than once per man. See UpgradeSupply.KitStock.
            UpgradeSupply.KitStock kitStock = new UpgradeSupply.KitStock();

            int armed = 0;
            while (acc >= 1f)
            {
                ArmOneMilitiaman(settlement, troop, castleMarket, kitStock);
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

        /// <summary>
        /// Returns the kit cost of every whole militiaman a settlement has shed to the affordability floor
        /// or the over-hard-cap drain since it last paid, back into its funding pot. Called from the daily settlement pass beside
        /// <see cref="ChargePendingSpawn"/> -- out here rather than in the DailyTick that shed them, so a
        /// village purse write is not caught inside <c>VillageGoldStock</c>'s suppression, exactly as the
        /// arming charge is deferred.
        /// </summary>
        /// <remarks>
        /// The refund per man is <see cref="SpawnCostPerMan"/>, which already carries the village's
        /// <see cref="MilitiaVillageGearShare"/> fraction, so a village recovers only the fraction of a kit
        /// it paid to arm one -- the same coin that left its wealth, returning to it. Only whole men are
        /// refunded; the sub-man remainder waits in the accumulator, the mirror of the arming path. Combat
        /// losses are never in this accumulator (they are not measured in DailyTick), so a militia butchered
        /// on the walls hands nothing back -- only a watch quietly disbanded for want of pay does.
        /// </remarks>
        public static void RefundPendingDecline(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
            {
                return;
            }
            float acc;
            if (!_pendingRefund.TryGetValue(settlement, out acc) || acc < 1f)
            {
                return;
            }
            int perMan = SpawnCostPerMan(settlement);
            if (perMan <= 0)
            {
                _pendingRefund[settlement] = 0f;
                return;
            }

            int refundedMen = 0;
            while (acc >= 1f)
            {
                acc -= 1f;
                refundedMen++;
            }
            _pendingRefund[settlement] = acc;

            // A town armed these men free off its own shelves (see ArmOneMilitiaman), so they hand their
            // gear back to the market, not coin to anyone.
            bool returnsGear = settlement.IsTown && RecruitSupply.IsEnabled;
            int credited = returnsGear
                ? RecruitSupply.ReturnKitToMarket(settlement, SpawnTroop(settlement), refundedMen,
                    BuildingEffects.SpawnCostFactor(settlement.Town))
                : CreditFundingPot(settlement, perMan * refundedMen);

            if (SpoilsLog.IsEnabled && refundedMen > 0)
            {
                SpoilsLog.Log("MILITIA", (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId)
                    + " refunded " + refundedMen + " shed militia ("
                    + (returnsGear ? credited + "d of gear back to market)" : credited + "d to funding pot)"));
            }
        }

        /// <summary>Arms one militiaman out of the settlement's funding pot, routed by kind of place.</summary>
        /// <param name="castleMarket">
        /// For a castle with the draw feature on, the town it buys from (<see cref="ResolveCastleSupplyMarket"/>),
        /// resolved once by the caller for the whole batch; null otherwise. Unused for towns and villages.
        /// </param>
        /// <param name="kitStock">The priced market view shared across the batch (see <see cref="RecruitSupply.DrawKitFromMarket"/>).</param>
        private static void ArmOneMilitiaman(Settlement settlement, CharacterObject troop, Settlement castleMarket,
            UpgradeSupply.KitStock kitStock)
        {
            if (settlement.IsVillage)
            {
                // The village buys the kit off the town it trades with and pays that town's merchants --
                // the recruit gear leg, which already does exactly this (debit village, credit town) --
                // but only MilitiaVillageGearShare of a full kit's worth, the cheap arming of a levy.
                RecruitSupply.DrawKitFromMarket(RecruitSupply.GetSupplyMarket(settlement), settlement, troop, 1,
                    MilitiaVillageGearShare, kitStock: kitStock);
                return;
            }
            // A castle keeps no market of its own, so it buys a full kit off its nearest friendly town,
            // paying that town's merchants the full kit value out of its own wealth whatever they had in
            // stock -- the same remote-buyer draw a village uses, and real, typed stock leaves that town's
            // shelves for it. Only when the draw feature is on and a supply town can be reached; otherwise
            // it falls through to the abstract debit below, so a castle's watch is never armed for free.
            // Barracks: kit already stored inside the walls, so the watch is armed a little more cheaply --
            // −5/10/15% at levels 1/2/3, the same discount the garrison's recruits get.
            float armingShare = BuildingEffects.SpawnCostFactor(settlement.Town);
            if (settlement.IsCastle)
            {
                // castleMarket is the caller's once-per-batch ResolveCastleSupplyMarket (null with the draw
                // feature off), the same town this used to look up for every man.
                if (castleMarket != null)
                {
                    RecruitSupply.DrawKitFromMarket(castleMarket, settlement, troop, 1, armingShare,
                        kitStock: kitStock);
                    return;
                }
            }
            // A town's citizens arm their own watch straight off their own market's shelves. The gear
            // leaving is the whole cost: they are both buyer and seller, so no coin moves.
            if (settlement.IsTown && RecruitSupply.IsEnabled)
            {
                RecruitSupply.DrawKitFromMarket(settlement, settlement, troop, 1, armingShare, chargeOwnTown: false,
                    kitStock: kitStock);
                return;
            }
            int cost = (int)(MilitiaKitValue(troop) * armingShare);
            if (cost <= 0)
            {
                return;
            }
            if (settlement.IsTown)
            {
                // The draw feature is off, so there is no market draw to arm from: the townsmen pay the
                // kit's worth out of the market's money instead.
                SettlementWealth.DebitCitizens(settlement, cost, SettlementWealth.Source.Militia);
            }
            else
            {
                // A castle with no town left to supply it (or with the draw feature off): the kit is sourced
                // off-screen and the coin simply leaves its wealth, unmatched by any town's gain.
                SettlementWealth.Debit(settlement, cost, SettlementWealth.Source.Militia);
            }
        }

        /// <summary>
        /// The town a castle buys its militia kit from: the nearest town its faction is not at war with.
        /// Null when the castle can reach no such town -- a faction reduced to that castle alone -- in which
        /// case the caller arms the man off-screen and debits the castle's own wealth instead.
        /// </summary>
        private static Settlement ResolveCastleSupplyMarket(Settlement castle)
        {
            if (castle == null)
            {
                return null;
            }
            IFaction faction = castle.MapFaction;
            Town town = SettlementHelper.FindNearestTownToSettlement(castle, MobileParty.NavigationType.Default,
                s => s.MapFaction != null && faction != null
                    && (s.MapFaction == faction || !faction.IsAtWarWith(s.MapFaction)));
            return (town != null) ? town.Settlement : null;
        }
    }
}
