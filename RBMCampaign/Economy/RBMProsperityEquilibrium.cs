using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
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
    /// pulled back; see <see cref="ConvergenceRate"/>, which is what decides that authority.
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
        /// A fief's prosperity as the vanilla economic models expect to receive it. Use this ONLY for
        /// gold and market demand; anything counting mouths wants the raw value.
        /// </summary>
        public static float EconomicProsperity(Town town)
        {
            return town.Prosperity * VanillaProsperityScale;
        }

        /// <summary>A fief's prosperity as the town-treasury controller expects it.</summary>
        public static float TreasuryProsperity(Town town)
        {
            return town.Prosperity * TownTreasuryScale;
        }

        /// <summary>
        /// Fraction of the gap to the equilibrium closed per day, and with it the authority this term
        /// carries against everything else pushing on prosperity.
        ///
        /// The rate is not really a speed setting, it is a weight. Prosperity rests where all terms
        /// cancel, so any other term contributing a steady <c>x</c> per day parks the fief
        /// <c>x / ConvergenceRate</c> away from its target. At the original 0.02 that was a
        /// fifty-fold lever: vanilla's housing ladder alone (+6/day) would have held every town 300
        /// above target, which is why it is cancelled below, and once markets began to fill, Surplus
        /// Food at ~+11/day was displacing well-fed towns by ~550 -- several times the target itself,
        /// leaving the countryside figure a rounding correction rather than an attractor.
        ///
        /// 0.1 makes the lever tenfold instead, so that same +11 displaces by ~110: enough that a
        /// thriving town still outgrows its countryside noticeably, not so much that the countryside
        /// stops meaning anything. The time constant comes down to about ten days, which is brisker
        /// than the seasonal drift originally intended -- the deliberate trade for giving this term
        /// enough weight to argue with its neighbours.
        /// </summary>
        private const float ConvergenceRate = 0.1f;

        /// <summary>
        /// Prosperity the countryside TRADING with this town can support, or 0 for anything that is
        /// not a town.
        ///
        /// Trade-bound rather than administratively bound, so the countryside counted here is the
        /// same one the food chain actually uses: a villager party walks to <c>Village.TradeBound</c>
        /// and sells there. A castle's villages trade at a nearby town, so their hearths feed that
        /// town's market and belong in its equilibrium, not the castle's.
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
            return hearths * ProsperityPerBoundHearth;
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
                // Towns only. A castle keeps vanilla prosperity untouched -- including vanilla's
                // housing ladder, which was town-gated to begin with and so needs no cancelling.
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || fortification == null || !fortification.IsTown)
                {
                    return;
                }

                float gap = TargetProsperity(fortification.Settlement) - fortification.Prosperity;

                // One combined line: vanilla's ladder cancelled, the hearth pull applied. Folding
                // them together keeps the town screen to a single readable entry rather than a
                // correction and a counter-correction.
                __result.Add(gap * ConvergenceRate - VanillaHousingCosts(fortification), CountrysideText);
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

        private static readonly TextObject CountrysideText = new TextObject("{=RBM_PROSPERITY_HEARTH}Countryside support");
    }
}
