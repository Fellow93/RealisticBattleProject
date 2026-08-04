using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Ties a fief's prosperity to the countryside that feeds it, as a moving equilibrium rather than
    /// a one-off starting value.
    ///
    /// A walled settlement grows no food of its own; its population is whatever surplus the villages
    /// bound to it can support. So its resting prosperity is a share of their combined Hearth, and it
    /// drifts toward that figure -- upward while the countryside can carry more mouths than the town
    /// currently has, downward once it cannot. Because RBM's village production grows Hearth over
    /// time, a fief's ceiling rises with the prosperity of its own villages instead of being fixed.
    ///
    /// This REPLACES vanilla's equilibrium rather than layering on top of it. Vanilla drives
    /// prosperity with a housing-cost ladder in <c>DefaultSettlementProsperityModel</c> -- a flat
    /// +6/day below 250 prosperity, tapering to -6/day above 21000 -- which is an attractor toward
    /// roughly 1500-6000 with no reference to the land. Left in place it would simply pull every town
    /// back out of the band set here, so the postfix cancels it before applying the hearth pull.
    ///
    /// This is one force among several, not a ceiling. Prosperity settles where every term cancels,
    /// so a fief with a standing bonus -- a full granary paying Surplus Food, a good governor, strong
    /// loyalty -- rests above its countryside figure, and a famine or unrest holds it below. What the
    /// countryside sets is the level a fief returns to once those pressures pass, and how hard it is
    /// pulled back; see <see cref="ProsperityGrowthRate"/> and <see cref="DeclineUrgency"/>, which are
    /// what decide that authority.
    /// </summary>
    public static class RBMProsperityEquilibrium
    {
        /// <summary>
        /// Share of a fief's bound hearths that its prosperity rests at. Also the seed used at world
        /// generation -- see <see cref="RBMEconomyCampaignBehavior"/>, which starts every fief on
        /// its equilibrium so the map does not open with a map-wide correction already in progress.
        /// </summary>
        public const float ProsperityPerBoundHearth = 0.1f;

        /// <summary>
        /// Converts RBM's household-scale prosperity into the far larger figure the vanilla ECONOMIC
        /// models were calibrated against.
        ///
        /// Prosperity is two different quantities wearing one name. To the food system it is a count
        /// of households, and the countryside equilibrium above puts that at a tenth of bound hearths
        /// -- a town of 80. To <c>DefaultSettlementEconomyModel</c> it is instead the scalar that sets
        /// how much gold a market moves: both the target town treasury and every category's daily
        /// demand pool are literal multiples of it, tuned against vanilla towns sitting at 2000-6000.
        ///
        /// Re-seeding prosperity onto the household scale therefore silently cut market liquidity by
        /// the same factor, which is what bankrupted every town on the map: they kept buying villager
        /// cargo at full price out of a treasury a twentieth the size, with a civilian demand pool a
        /// twentieth as able to buy it back.
        ///
        /// Rather than retune the economy's constants one by one, the two places prosperity enters it
        /// are scaled back to the band vanilla expects. Vanilla prosperity ran at roughly twice a
        /// fief's bound hearths against RBM's tenth, so twenty restores the original scale -- and
        /// with it vanilla's prices, since price is driven by the same demand figure.
        /// </summary>
        public const float VanillaProsperityScale = 20f;

        /// <summary>
        /// The same conversion, for the town TREASURY alone. Separate from
        /// <see cref="VanillaProsperityScale"/> because the two stopped being the same concern once
        /// the price path was split out of demand: the treasury dial no longer moves prices, so it
        /// can be tuned purely on whether towns can afford to feed themselves.
        ///
        /// Set above the demand scale to break a deadlock the logs caught. Food pricing is a ratio of
        /// demand to supply, so an empty market prices food at the scarcity end -- measured at 269
        /// denars a unit against 154 in a well-stocked one. That is bistable: a town too poor to fill
        /// its market keeps paying the empty-market price and stays too poor. Towns were spending
        /// 509k a day against the 645k needed to clear a full ration at starving prices, about 27%
        /// short, so doubling the treasury clears the tipping point with margin rather than by a
        /// hair -- and once markets fill, prices fall toward the stocked end and the requirement
        /// drops well below what this provides. The controller self-limits from there: gold above
        /// target is destroyed, so towns cannot hoard it.
        /// </summary>
        public const float TownTreasuryScale = 40f;

        /// <summary>
        /// A castle's resting prosperity as a multiple of the AVERAGE hearth of the villages bound to
        /// it -- its own administratively-bound countryside, not the trade-bound set a town equilibrium
        /// counts. A castle is a big village's worth of settled life clustered behind a wall, so it
        /// rests half again above the size of a typical one of its own villages, and rises and falls
        /// with them as they grow or are raided.
        ///
        /// Deliberately the AVERAGE and not the sum: a castle with three thriving villages is not three
        /// times the place a castle with one is, it is a place of the same kind whose land happens to be
        /// divided differently. Averaging keeps the resting figure a statement about the quality of the
        /// countryside rather than a headcount of it, so a castle's prosperity does not balloon simply
        /// for having been drawn more villages on the map.
        ///
        /// This is the castle counterpart to the town's <see cref="ProsperityPerBoundHearth"/>, and it
        /// lands a castle at roughly 400-900 -- the same band the world authors castles at, so nothing
        /// downstream that reads a castle's raw prosperity sees its scale shift.
        /// </summary>
        public const float CastleProsperityHearthFactor = 1.5f;

        /// <summary>
        /// Fraction of the gap to its resting figure a castle closes per day, in both directions -- the
        /// castle counterpart to the town's <see cref="ProsperityGrowthRate"/> and
        /// <see cref="ProsperityDeclineRate"/>. A castle keeps a single flat rate rather than the town's
        /// food-modulated asymmetry: it grows no food of its own to meter the pull with, so it simply
        /// drifts toward its countryside over roughly three weeks -- deliberately, but not so slowly that
        /// a raided or recovering village takes a season to register on the keep it feeds.
        /// </summary>
        private const float CastleConvergenceRate = 0.05f;

        /// <summary>
        /// A fief's prosperity as the vanilla economic models expect to receive it. Use this ONLY for
        /// gold and market demand; anything counting mouths wants the raw value.
        /// </summary>
        public static float EconomicProsperity(Town town)
        {
            return town.Prosperity * VanillaProsperityScale;
        }

        /// <summary>
        /// A fortification's prosperity on the HOUSEHOLD scale, whichever scale it is actually stored
        /// on. Use this anywhere a figure has to mean the same thing for a town and for a castle.
        ///
        /// Prosperity is not stored on one scale across the map. <see cref="TargetProsperity"/> rewrites
        /// every TOWN onto the household scale at world generation, but castles are excluded from the
        /// countryside model entirely and keep the far larger figure the world was authored with --
        /// 420-960 against a re-seeded town's 250-300. Both are correct for what reads them: castles run
        /// vanilla's economy untouched (all three patches in <see cref="RBMMarketLiquidity"/> are
        /// <c>IsTown</c>-gated) and vanilla's economy wants the vanilla scale.
        ///
        /// It stops being correct the moment one of RBM's OWN constants, derived against the household
        /// scale, is multiplied by a castle's prosperity. That is a twentyfold error, and it was seeding
        /// castle treasuries at roughly three times a town's.
        /// </summary>
        public static float HouseholdProsperity(Town town)
        {
            if (town == null)
            {
                return 0f;
            }
            return town.IsTown ? town.Prosperity : (town.Prosperity / VanillaProsperityScale);
        }

        /// <summary>A fief's prosperity as the town-treasury controller expects it.</summary>
        public static float TreasuryProsperity(Town town)
        {
            return HouseholdProsperity(town) * TownTreasuryScale;
        }

        /// <summary>
        /// Fraction of the gap a town's prosperity closes per day while GROWING toward a countryside
        /// that can carry more than it currently holds. Deliberately slow -- a time constant of a month
        /// and a half rather than the ten days the single old rate gave -- so a town settles toward its
        /// figure over a season rather than snapping to it. Growth is additionally gated on the town
        /// actually being fed (see <see cref="ProsperityChangeRate"/>): a hungry town does not grow no
        /// matter how much land stands behind it.
        /// </summary>
        private const float ProsperityGrowthRate = 0.02f;

        /// <summary>
        /// The fraction a town's prosperity closes per day while DECLINING toward a countryside that
        /// can no longer carry it -- but only at full <see cref="DeclineUrgency"/>, i.e. an empty
        /// granary with its people going hungry. A well-fed town in surplus barely moves down at all
        /// (see <see cref="DeclineFloor"/>); this is the ceiling that famine builds up to, not the
        /// speed a comfortable town falls at.
        ///
        /// A decline is a settlement shedding mouths it can no longer feed, so its pace is set by how
        /// badly it is failing to feed them, not by the raw distance to target. That is what keeps a
        /// prosperous town from being dragged down simply for standing above its countryside figure:
        /// while the food holds, it stays.
        /// </summary>
        private const float ProsperityDeclineRate = 0.08f;

        /// <summary>
        /// The floor on <see cref="DeclineUrgency"/> -- the share of <see cref="ProsperityDeclineRate"/>
        /// that still applies to a town with a full granary whose every need is met. Small but not zero:
        /// a town resting above its countryside figure should drift down almost imperceptibly rather than
        /// being pinned there forever, so a lasting surplus still slowly gives way once its cause passes.
        /// At 0.05 a well-fed town over target has a decline time constant of roughly two hundred days --
        /// effectively frozen against week-to-week pressures, but not permanent.
        /// </summary>
        private const float DeclineFloor = 0.05f;

        /// <summary>
        /// The extra growth a town earns for fully meeting its citizens' MEDIUM luxury demand, as a
        /// fraction added on top of base-demand-driven growth. A town whose middling luxuries are actually
        /// on the shelf grows a quarter faster than one supplying only the necessities.
        /// See <see cref="CitizenDemand.MediumDemandSatisfaction"/>.
        /// </summary>
        private const float MediumDemandGrowthBonus = 0.25f;

        /// <summary>
        /// The extra growth a town earns for fully meeting its citizens' LARGE (top-tier) luxury demand,
        /// added on top of base and medium growth. The richest bonus, so a genuinely prosperous town whose
        /// market can supply what the rich buy pulls toward its ceiling half again as fast.
        /// See <see cref="CitizenDemand.LuxuryDemandSatisfaction"/>.
        /// </summary>
        private const float LuxuryDemandGrowthBonus = 0.5f;

        /// <summary>
        /// How much each point of a town's <see cref="InfrastructureScore"/> lifts its resting
        /// prosperity, as a fraction of the countryside figure. A tier-3 building is worth three
        /// points, so at +2%/point a single fully-built structure adds ~6% and a town summing ~17
        /// points across its buildings rests about a third above what the bare countryside can carry.
        ///
        /// The countryside sets how many mouths the land can feed; infrastructure sets how densely a
        /// town can house and employ them on that same food. A granary, aqueduct or paved market lets
        /// the same hearths support a larger settled population, which is the sense in which a
        /// well-built city outgrows a shantytown fed by identical villages.
        /// </summary>
        public const float InfrastructureBonusPerTierPoint = 0.02f;

        /// <summary>
        /// Ceiling on <see cref="InfrastructureMultiplier"/>, so the best-developed town rests at most
        /// this multiple of its countryside figure. Caps the compounding at a doubling (+100%) rather
        /// than letting a modded building set or an unusually dense slot count run the target away from
        /// the food the land actually provides.
        /// </summary>
        public const float MaxInfrastructureMultiplier = 2f;

        /// <summary>
        /// A town's development as the summed level of everything it has built: a tier-3 structure is
        /// worth three points, a tier-1 one point, an unbuilt slot nothing. Rotating daily projects
        /// (festivals, the marketplace boost) are skipped -- they are a standing choice of focus, not a
        /// permanent improvement, and would otherwise add a phantom point that comes and goes with the
        /// governor's orders.
        /// </summary>
        public static int InfrastructureScore(Town town)
        {
            if (town == null || town.Buildings == null)
            {
                return 0;
            }

            int score = 0;
            foreach (Building building in town.Buildings)
            {
                if (building.BuildingType != null && building.BuildingType.IsDailyProject)
                {
                    continue;
                }
                score += building.CurrentLevel;
            }
            return score;
        }

        /// <summary>
        /// The factor a town's <see cref="InfrastructureScore"/> applies to its countryside prosperity
        /// figure: 1 for a town that has built nothing, rising by
        /// <see cref="InfrastructureBonusPerTierPoint"/> per point and clamped at
        /// <see cref="MaxInfrastructureMultiplier"/>.
        /// </summary>
        public static float InfrastructureMultiplier(Town town)
        {
            float multiplier = 1f + InfrastructureScore(town) * InfrastructureBonusPerTierPoint;
            return (multiplier > MaxInfrastructureMultiplier) ? MaxInfrastructureMultiplier : multiplier;
        }

        /// <summary>
        /// Prosperity the countryside TRADING with this town can support, scaled by how well the town
        /// itself is built; 0 for anything that is not a town.
        ///
        /// Trade-bound rather than administratively bound, so the countryside counted here is the
        /// same one the food chain actually uses: a villager party walks to <c>Village.TradeBound</c>
        /// and sells there. A castle's villages trade at a nearby town, so their hearths feed that
        /// town's market and belong in its equilibrium, not the castle's.
        ///
        /// The bare hearth figure is the food the land provides; <see cref="InfrastructureMultiplier"/>
        /// then raises the ceiling for how much settled population that food can house, so a
        /// well-developed town rests higher than a neglected one fed by the same villages.
        ///
        /// Castles are excluded entirely for now -- they keep vanilla prosperity. Nothing about them
        /// fits this model yet: they buy nothing (only towns trade with villagers), and the villages
        /// they own support a town rather than themselves.
        /// </summary>
        public static float TargetProsperity(Settlement settlement)
        {
            if (settlement == null || settlement.Town == null || !settlement.IsTown)
            {
                return 0f;
            }

            EnsureHearthCache();
            _tradeBoundHearths.TryGetValue(settlement, out float hearths);
            return hearths * ProsperityPerBoundHearth * InfrastructureMultiplier(settlement.Town);
        }

        /// <summary>
        /// The prosperity a CASTLE rests at: the average hearth of its own bound villages times
        /// <see cref="CastleProsperityHearthFactor"/>, or 0 for anything that is not a castle or that
        /// has no bound villages to draw on.
        ///
        /// Administratively bound (<c>Settlement.BoundVillages</c>), not trade-bound: these are the
        /// villages the castle actually holds, the ones whose fortunes are its own, even though they
        /// sell their cargo at a nearby town. A zero return means "leave it alone" -- a castle with no
        /// villages, or one queried before its bounds are assigned -- and callers keep the castle's
        /// current prosperity rather than driving it to nothing.
        /// </summary>
        public static float CastleTargetProsperity(Settlement settlement)
        {
            if (settlement == null || !settlement.IsCastle || settlement.BoundVillages == null)
            {
                return 0f;
            }

            float sum = 0f;
            int count = 0;
            foreach (Village village in settlement.BoundVillages)
            {
                sum += village.Hearth;
                count++;
            }
            return (count > 0) ? (sum / count) * CastleProsperityHearthFactor : 0f;
        }

        // Hearths trading into each town, rebuilt once a campaign day. Hearth moves daily, so a
        // longer-lived cache would drift; a shorter-lived one would rescan every village for every
        // settlement that asks.
        private static int _hearthCacheDay = -1;
        private static readonly Dictionary<Settlement, float> _tradeBoundHearths = new Dictionary<Settlement, float>();

        internal static void ResetForNewSession()
        {
            InvalidateHearthCache();
        }

        /// <summary>
        /// Forces the next read to rebuild the hearth cache.
        ///
        /// Needed because the cache is keyed on the campaign day, and day zero is not a quiet moment:
        /// the equilibrium postfix fires on any <c>CalculateProsperityChange</c>, several of which
        /// happen during world generation, and one of those can build the cache BEFORE
        /// <c>VillageTradeBoundCampaignBehavior</c> has given castle villages a <c>TradeBound</c>.
        /// Those hearths are silently dropped, and being stamped with the current day the cache is
        /// then reused by anything else asking that day -- including the seed, which is the one
        /// caller that must not get it wrong. Observed as towns seeding at a third of their target
        /// and then spending fifty days climbing to it.
        /// </summary>
        internal static void InvalidateHearthCache()
        {
            _hearthCacheDay = -1;
            _tradeBoundHearths.Clear();
        }

        /// <summary>
        /// Sums hearths per trade-bound town by walking <see cref="Village.All"/> and reading each
        /// village's own <c>TradeBound</c>.
        ///
        /// Deliberately NOT <c>Town.TradeBoundVillages</c>, which is unreliable on a loaded save: it
        /// is <c>[CachedData]</c>, is emptied by <c>Town.OnLoad</c>, and the only thing that re-adds a
        /// town's OWN villages is <c>Village.Deserialize</c> -- which skips that branch for saved
        /// campaigns. <c>VillageTradeBoundCampaignBehavior.UpdateTradeBounds</c> then walks castle
        /// villages only, so after a load the list holds castle villages and none of the town's own.
        /// The <c>Village.TradeBound</c> getter is correct either way: it returns the bound settlement
        /// directly when that is a town, and the assigned one otherwise.
        /// </summary>
        private static void EnsureHearthCache()
        {
            int day = (int)CampaignTime.Now.ToDays;
            if (day == _hearthCacheDay)
            {
                return;
            }

            _hearthCacheDay = day;
            _tradeBoundHearths.Clear();
            foreach (Village village in Village.All)
            {
                Settlement tradeBound = village.TradeBound;
                if (tradeBound == null)
                {
                    continue;
                }

                _tradeBoundHearths.TryGetValue(tradeBound, out float hearths);
                _tradeBoundHearths[tradeBound] = hearths + village.Hearth;
            }
        }

        [HarmonyPatch(typeof(DefaultSettlementProsperityModel), "CalculateProsperityChange")]
        private static class ProsperityEquilibriumPatch
        {
            private static void Postfix(Town fortification, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || fortification == null)
                {
                    return;
                }

                // A castle drifts toward the average hearth of its own bound villages. Vanilla's
                // housing ladder was town-gated to begin with, so a castle needs no cancellation --
                // only the pull. A zero target means the bounds are not readable yet; leave it be.
                if (fortification.IsCastle)
                {
                    float castleTarget = CastleTargetProsperity(fortification.Settlement);
                    if (castleTarget > 0f)
                    {
                        // Ride the hearth equilibrium, and cancel vanilla's food-shortage and surplus-food
                        // pulls (a castle has no market, so no goods term to cancel) so it mostly drifts to
                        // its figure rather than swinging on its garrison's larder. Perks, policies, loyalty
                        // and building effects are left in place.
                        float drift = (castleTarget - fortification.Prosperity) * CastleConvergenceRate;
                        __result.Add(drift - VanillaFoodAndMarketProsperity(fortification), CountrysideText);
                    }
                    return;
                }

                if (!fortification.IsTown)
                {
                    return;
                }

                float gap = TargetProsperity(fortification.Settlement) - fortification.Prosperity;

                float change;
                if (gap >= 0f)
                {
                    // Below what the countryside can carry: grow toward it -- but food is a hard gate
                    // (a hungry town does not grow at all), and the pace is then set by how well the
                    // town supplies its people's wants. Base demand is the backbone; met medium and
                    // luxury demand add to it, so a town whose citizens can actually buy what they want
                    // climbs faster than one that only just feeds itself.
                    float foodGate = RBMTownFoodSupply.RationSatisfaction(fortification);
                    float demandModifier = CitizenDemand.BaseDemandSatisfaction(fortification)
                        * (1f + MediumDemandGrowthBonus * CitizenDemand.MediumDemandSatisfaction(fortification)
                              + LuxuryDemandGrowthBonus * CitizenDemand.LuxuryDemandSatisfaction(fortification));
                    change = gap * ProsperityGrowthRate * foodGate * demandModifier;
                }
                else
                {
                    // Above what the countryside can carry: give way gently even while fed, so a surplus
                    // does not stand forever once its cause has passed.
                    change = gap * ProsperityDeclineRate * DeclineFloor;
                }

                // Starvation drags prosperity down on top of the drift, and unlike the countryside pull it
                // fires even below target -- a town that cannot feed itself sheds people wherever it sits.
                // The fall starts slow and steepens with the town's accumulated hunger, so a passing
                // shortage barely registers while a lasting famine collapses it. See HungerPressure.
                change -= ProsperityDeclineRate * RBMTownFoodSupply.HungerPressure(fortification) * fortification.Prosperity;

                // One combined line: vanilla's food/market/housing pulls cancelled, RBM's food-and-demand
                // drift applied. Folding them together keeps the town screen to a single readable entry
                // rather than a correction and a counter-correction.
                __result.Add(change - VanillaHousingCosts(fortification) - VanillaFoodAndMarketProsperity(fortification), CountrysideText);
            }
        }

        /// <summary>
        /// Reproduces the housing-cost term of <c>CalculateProsperityChangeInternal</c> so it can be
        /// subtracted back out. It is a pure function of prosperity and is gated to towns, so castles
        /// -- which never received it -- need no cancellation.
        ///
        /// Kept as a literal transcription of the vanilla brackets rather than a formula: if a game
        /// update retunes them, a mismatch here shows up as prosperity that will not settle, and a
        /// transcription is far easier to diff against the decompiled source than an approximation.
        /// </summary>
        private static float VanillaHousingCosts(Town fortification)
        {
            if (!fortification.IsTown)
            {
                return 0f;
            }

            float prosperity = fortification.Prosperity;
            if (prosperity < 250f) return 6f;
            if (prosperity < 500f) return 5f;
            if (prosperity < 750f) return 4f;
            if (prosperity < 1000f) return 3f;
            if (prosperity < 1250f) return 2f;
            if (prosperity < 1500f) return 1f;
            if (prosperity > 21000f) return -6f;
            if (prosperity > 18000f) return -5f;
            if (prosperity > 15000f) return -4f;
            if (prosperity > 12000f) return -3f;
            if (prosperity > 9000f) return -2f;
            if (prosperity > 6000f) return -1f;
            return 0f;
        }

        /// <summary>
        /// Reproduces the vanilla prosperity terms RBM's own model OWNS -- the starvation food-shortage
        /// penalty, the surplus-food bonus and the goods-from-market bonus -- so they can be subtracted
        /// back out and RBM's drift is the sole authority on food- and trade-driven fief prosperity. A
        /// literal transcription of the vanilla lines (same getters, same perk call, same order) rather
        /// than a formula, for the same reason <see cref="VanillaHousingCosts"/> is: a game update that
        /// retunes them then shows up as a mismatch easy to diff against the decompiled source.
        ///
        /// Applies to both towns and castles: vanilla gives food-shortage and surplus-food to either, so
        /// this cancels them for either. Goods-from-market is vanilla's town-only term (a castle has no
        /// market and never receives it), so the cancellation adds it only for a town.
        ///
        /// Same small imprecision as the housing cancellation: vanilla folds these into the running total
        /// BEFORE the multiplicative Apprenticeship perk factor, so subtracting them afterwards does not
        /// unwind their tiny contribution to that factor. Negligible, and accepted for housing already.
        /// </summary>
        private static float VanillaFoodAndMarketProsperity(Town fortification)
        {
            float total = 0f;
            float foodChange = fortification.FoodChange;

            // Food shortage: vanilla docks half the negative food change when the OWNING CLAN is starving,
            // town or castle alike, softened by the Helping Hands perk. Reproduce the perk-modified figure.
            if (fortification.Owner != null && fortification.Owner.IsStarving)
            {
                ExplainedNumber bonuses = new ExplainedNumber((foodChange < 0f) ? ((int)foodChange) : 0);
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Medicine.HelpingHands, fortification, ref bonuses);
                total += bonuses.ResultNumber * 0.5f;
            }

            // Surplus food: vanilla pays 0.1 per unit of food projected over the granary cap, town or castle.
            int cap = fortification.FoodStocksUpperLimit();
            int surplus = (int)(fortification.FoodStocks + foodChange) - cap;
            if (surplus > 0)
            {
                total += surplus * 0.1f;
            }

            // Goods from market: vanilla pays 0.1 per prosperity-good sold -- but only for a TOWN. A castle
            // has no market and vanilla never gives it this term, so neither does the cancellation.
            if (fortification.IsTown)
            {
                int prosperityGoods = 0;
                foreach (Town.SellLog log in fortification.SoldItems)
                {
                    if (log.Category != null && log.Category.Properties == ItemCategory.Property.BonusToProsperity)
                    {
                        prosperityGoods += log.Number;
                    }
                }
                if (prosperityGoods > 0)
                {
                    total += prosperityGoods * 0.1f;
                }
            }

            return total;
        }

        private static readonly TextObject CountrysideText = new TextObject("{=RBM_PROSPERITY_HEARTH}Countryside support");
    }
}
