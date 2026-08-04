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
        /// Share of the standing militia that drifts home each day -- the decline spine. At vanilla's
        /// 0.025 a settlement sheds one man per day per forty it holds, which is what balances the base
        /// muster and the hearth/prosperity intake into a steady-state count.
        /// </summary>
        public const float MilitiaRetirementRate = 0.025f;

        /// <summary>Militia a day per unit of a village's hearth -- vanilla's Hearth / 400.</summary>
        public const float MilitiaPerHearth = 1f / 400f;

        /// <summary>Militia a day per unit of a fortification's prosperity -- vanilla's Prosperity / 1000.</summary>
        public const float MilitiaPerProsperity = 1f / 1000f;

        // Vanilla's own line labels, reused verbatim so RBM's rebuilt breakdown reads exactly as the
        // player is used to. These are TaleWorlds localization keys resolved by the game, not RBM strings.
        private static readonly TextObject BaseText = new TextObject("{=militarybase}Base");
        private static readonly TextObject RetiredText = new TextObject("{=gHnfFi1s}Retired");
        private static readonly TextObject FromHearthsText = new TextObject("{=ecdZglky}From Hearths");
        private static readonly TextObject FromProsperityText = new TextObject("{=cTmiNAlI}From Prosperity");
        private static readonly TextObject LowLoyaltyText = new TextObject("{=SJ2qsRdF}Low Loyalty");
        private static readonly TextObject MilitiaFromMarketText = new TextObject("{=7ve3bQxg}Weapons From Market");
        private static readonly TextObject CultureText = GameTexts.FindText("str_culture");

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
        public const float MilitiaVillageGearShare = 0.1f;

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
        /// Returns <paramref name="amount"/> to the settlement's militia funding pot, the inverse of arming
        /// a man from it, routed by how that arming moved the money. Returns what was actually credited.
        ///
        ///   * TOWN -- its citizens armed their own watch out of market money that left for outside gear;
        ///     the refund re-enters that same citizen wealth (the symmetric inverse of the spend).
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
        /// Replaces vanilla's militia-change model outright: RBM authors the whole day's number, so the
        /// settlement's growth, decline and cap are all RBM's, not vanilla's shaved at the edges.
        /// </summary>
        /// <remarks>
        /// A <c>Prefix</c> that returns <see langword="false"/> and hands back its own
        /// <see cref="ExplainedNumber"/>, so vanilla's <c>CalculateMilitiaChangeInternal</c> never runs.
        /// The number is built in three layers:
        ///
        ///   * THE BASE CURVE (RBM-owned). The muster/retirement/hearth/prosperity spine, rebuilt from
        ///     RBM's own constants (<see cref="BaseMilitiaFortification"/>, <see cref="MilitiaRetirementRate"/>,
        ///     <see cref="MilitiaPerHearth"/>, <see cref="MilitiaPerProsperity"/>) seeded to vanilla's
        ///     values -- this is the growth/decline RBM now dials.
        ///
        ///   * THE KEPT MODIFIERS (vanilla flavour). Loyalty, market weapons, kingdom policies, the
        ///     Battanian feat, building effects, governor perks and settlement issues, reproduced from the
        ///     same public API vanilla uses so a governor's perk or a serfdom policy still reads on the
        ///     breakdown exactly as before (see <see cref="AddKeptModifiers"/>).
        ///
        ///   * RBM'S CEILING AND FLOOR. A SOFT CAP -- past a share of hearths or prosperity, positive
        ///     growth is cut to <see cref="MilitiaOverCapGrowthFactor"/>, a ceiling that yields to strong
        ///     loyalty or perks rather than one the count cannot pass; only positive growth is touched. And
        ///     an AFFORDABILITY FLOOR -- a settlement that cannot arm a new man raises none, and one that
        ///     cannot keep twenty days of its militia's maintenance in the pot sheds men whatever its cap
        ///     says, because a market spent dry SHOULD start losing its watch.
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
                __result = ComputeMilitiaChange(settlement, includeDescriptions);
                return false;
            }
        }

        /// <summary>
        /// Builds a settlement's whole daily militia change: RBM's base curve, then the kept vanilla
        /// modifiers, then RBM's soft cap and affordability floor.
        /// </summary>
        private static ExplainedNumber ComputeMilitiaChange(Settlement settlement, bool includeDescriptions)
        {
            ExplainedNumber result = new ExplainedNumber(0f, includeDescriptions);

            // A raided or otherwise abnormal village musters nothing, as in vanilla.
            if (settlement.IsVillage && settlement.Village.VillageState != Village.VillageStates.Normal)
            {
                return result;
            }

            float militia = settlement.Militia;

            // --- Base curve (RBM-owned): flat muster, retirement drag, manpower intake.
            if (settlement.IsFortification)
            {
                result.Add(BaseMilitiaFortification, BaseText);
            }
            else if (settlement.IsVillage)
            {
                result.Add(BaseMilitiaVillage, BaseText);
            }

            result.Add(-militia * MilitiaRetirementRate, RetiredText);

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

            // --- Kept modifiers (vanilla flavour, reproduced from public API).
            AddKeptModifiers(settlement, ref result);

            // --- RBM ceiling and floor.
            ApplySoftCap(settlement, ref result);

            // A settlement that cannot arm a new militiaman fields no new ones -- growth is held to zero,
            // though the men it already has are left standing (the maintenance shed below thins those, when
            // even their upkeep cannot be met).
            if (result.ResultNumber > 0f && !CanAffordSpawn(settlement))
            {
                result.Add(-result.ResultNumber, CannotArmText);
            }

            if (!CanKeepMilitia(settlement) && result.ResultNumber > -MilitiaShedPerDay)
            {
                result.Add(-MilitiaShedPerDay - result.ResultNumber, UnaffordableText);
            }

            return result;
        }

        /// <summary>
        /// Adds the vanilla militia modifiers RBM keeps -- market weapons, kingdom policies, the Battanian
        /// feat, building effects, governor perks and settlement issues -- from the same public API vanilla
        /// uses, so they read on the breakdown exactly as before. The base muster/retirement/intake spine is
        /// NOT here: that is RBM's, added in <see cref="ComputeMilitiaChange"/>.
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
                Kingdom townKingdom = settlement.OwnerClan.Kingdom;
                if (townKingdom != null)
                {
                    if (townKingdom.ActivePolicies.Contains(DefaultPolicies.Serfdom))
                    {
                        result.Add(-1f, DefaultPolicies.Serfdom.Name);
                    }
                    if (townKingdom.ActivePolicies.Contains(DefaultPolicies.Cantons))
                    {
                        result.Add(1f, DefaultPolicies.Cantons.Name);
                    }
                }
                if (settlement.OwnerClan.Culture.HasFeat(DefaultCulturalFeats.BattanianMilitiaFeat))
                {
                    result.Add(DefaultCulturalFeats.BattanianMilitiaFeat.EffectBonus, CultureText);
                }
            }

            if (settlement.IsCastle || settlement.IsTown)
            {
                settlement.Town.AddEffectOfBuildings(BuildingEffectEnum.Militia, ref result);
                if (settlement.IsCastle && settlement.Town.InRebelliousState)
                {
                    settlement.Town.AddEffectOfBuildings(BuildingEffectEnum.MilitiaReduction, ref result);
                }

                Kingdom kingdom = settlement.OwnerClan.Kingdom;
                if (kingdom != null && kingdom.ActivePolicies.Contains(DefaultPolicies.Citizenship))
                {
                    result.Add(1f, DefaultPolicies.Citizenship.Name);
                }

                if (settlement.Town.Governor != null)
                {
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.OneHanded.SwiftStrike, settlement.Town, ref result);
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.Polearm.KeepAtBay, settlement.Town, ref result);
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.Bow.MerryMen, settlement.Town, ref result);
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.Crossbow.LongShots, settlement.Town, ref result);
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.Throwing.SlingingCompetitions, settlement.Town, ref result);
                    if (settlement.IsUnderSiege)
                    {
                        PerkHelper.AddPerkBonusForTown(DefaultPerks.Roguery.ArmsDealer, settlement.Town, ref result);
                    }
                    PerkHelper.AddPerkBonusForTown(DefaultPerks.Steward.SevenVeterans, settlement.Town, ref result);
                }

                Campaign.Current.Models.IssueModel.GetIssueEffectsOfSettlement(
                    DefaultIssueEffects.SettlementMilitia, settlement, ref result);
            }
        }

        /// <summary>
        /// Cuts the day's growth to <see cref="MilitiaOverCapGrowthFactor"/> once the settlement is at or
        /// over its soft cap. Leaves a settlement under its cap, or one already losing men, untouched.
        /// </summary>
        private static void ApplySoftCap(Settlement settlement, ref ExplainedNumber result)
        {
            if (result.ResultNumber <= 0f)
            {
                return;
            }
            float cap = MilitiaCap(settlement);
            if (cap <= 0f || settlement.Militia < cap)
            {
                return;
            }
            result.Add(-(1f - MilitiaOverCapGrowthFactor) * result.ResultNumber, OverCapText);
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
        /// The mirror of <see cref="_pendingGrowth"/> for men lost to the affordability floor: a settlement
        /// shedding militia it can no longer pay for banks each shed man here, so his kit's cost can be
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
        /// Banks the day's militia change the moment it is applied, splitting it by sign. This is the only
        /// militia change that happens inside a fief's DailyTick -- an escort borrowing men (see
        /// <c>VillagerEscort</c>) moves them on other events, and combat losses happen in battle, so
        /// measuring the DailyTick delta catches the model's own muster and shedding and nothing else.
        ///
        ///   * GROWTH accrues to <see cref="_pendingGrowth"/>, to be armed and paid for later out of the
        ///     village suppression window (see <see cref="ChargePendingSpawn"/>).
        ///
        ///   * DECLINE accrues to <see cref="_pendingRefund"/> ONLY when the settlement cannot keep its
        ///     militia (<see cref="CanKeepMilitia"/> is false) -- the affordability floor thinning a watch
        ///     the pot can no longer pay for. Its kit cost is returned to the funding pot later (see
        ///     <see cref="RefundPendingDecline"/>). Natural retirement on a settlement that CAN still pay is
        ///     left alone: those men keep their kit, only a watch shed for want of money hands it back.
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
            else if (delta < 0f && !CanKeepMilitia(settlement))
            {
                float acc;
                _pendingRefund.TryGetValue(settlement, out acc);
                _pendingRefund[settlement] = acc - delta; // -delta is the positive count shed
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

        /// <summary>
        /// Returns the kit cost of every whole militiaman a settlement has shed to the affordability floor
        /// since it last paid, back into its funding pot. Called from the daily settlement pass beside
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

            int credited = CreditFundingPot(settlement, perMan * refundedMen);

            if (SpoilsLog.IsEnabled && refundedMen > 0)
            {
                SpoilsLog.Log("MILITIA", (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId)
                    + " refunded " + refundedMen + " shed militia (" + credited + "d to funding pot)");
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
