using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Restores market liquidity after the countryside re-seed, by feeding the two economic models
    /// that read prosperity a value on the scale they were built for
    /// (<see cref="RBMProsperityEquilibrium.EconomicProsperity"/>).
    ///
    /// The two demand patches are pure rescalings -- the formulae are vanilla's, term for term, with
    /// the prosperity input converted. Nothing there changes the shape of the economy; it undoes a
    /// change of units that the prosperity rework made by accident.
    ///
    /// The third patch used to do the same for a town's treasury and no longer does anything at all:
    /// the gold controller it rescaled has since been switched off outright, so a town's money is now
    /// whatever its trade has left it. See <c>TownGoldChangePatch</c> for why. What that leaves is the
    /// two demand legs, and they are still needed for the reason they always were: the leg that
    /// recoups what a town paid its villagers is the civilian demand pool,
    /// <c>BaseDemand * Prosperity</c>, and it has to be on the same scale as the buying leg --
    /// <c>SellGoodsForTradeAction</c> is demand-blind, taking <c>min(units, Gold / price)</c> of
    /// whatever arrives at full price. Now that the treasury is unregulated, the outlet's width is the
    /// only thing keeping the inlet honest.
    ///
    /// TOWNS ONLY, all three patches, because the re-seed they compensate for is towns-only:
    /// <see cref="RBMProsperityEquilibrium.TargetProsperity"/> returns 0 for a castle and its
    /// equilibrium postfix skips them, so a castle still carries vanilla-scale prosperity. Rescaling
    /// that a second time was a gold printer -- a prosperity-1000 castle targeted a ~490k treasury and
    /// priced goods around six times a town's, so anything bought in a town sold into a castle for a
    /// multiple against a half-million-denar purse. Castles fall through to vanilla on every leg,
    /// which is coherent: vanilla numbers on a vanilla prosperity. If castles are ever brought into
    /// the countryside model, all three gates come off together -- the price leg divides by the same
    /// scale the pool leg multiplies by, so gating them apart would break castle pricing instead.
    /// </summary>
    public static class RBMMarketLiquidity
    {
        /// <summary>
        /// Stops a town's money from being regulated, and lets it simply be what its trade has left it.
        ///
        /// Vanilla's <c>GetTownGoldChange</c> is not income: it is a proportional controller that
        /// closes a quarter of the gap to a target of <c>10000 + 12 * Prosperity</c> every day, and it
        /// is symmetric -- a town below target has gold CONJURED and a town above it has gold
        /// DESTROYED. Nobody pays and nobody receives either way. It runs at the end of the daily tick,
        /// after everything real has happened, and simply sets the pot back to what prosperity says it
        /// ought to be.
        ///
        /// That was the last unconserved edge in the economy, and it was larger than any real flow in
        /// it. Measured over seven days at Danustica the ledger of actual credits and debits netted
        /// +397,964 denars while the balance FELL 60,134 -- some 65,000 a day destroyed, almost exactly
        /// cancelling what the garrison spent carousing. No amount of tuning elsewhere could show
        /// through that, in either direction: a town could not accumulate what it earned, and could not
        /// go broke on what it owed.
        ///
        /// So it returns nothing. Citizen wealth is now a real stock, like the village purse and the
        /// fief treasury before it -- it grows when the town sells and shrinks when the town buys, and
        /// there is no floor under it and no ceiling over it.
        /// </summary>
        /// <remarks>
        /// A zero return rather than an unpatched method, deliberately. Removing the patch does not
        /// remove the controller -- it hands it back to vanilla, which is the outcome this is meant to
        /// prevent. The prefix must stay and must keep returning false.
        ///
        /// The other two patches below are untouched and must stay: they are unit rescalings of the
        /// demand pool and of price, unrelated to this, and both are still load-bearing.
        ///
        /// What this exposes, stated plainly because it is the thing to watch: soldier spending brings
        /// a town roughly nine times what deliveries and the wealth tax take out of it. The controller
        /// was hiding that. With it gone the imbalance is no longer absorbed -- garrison towns will
        /// accumulate, and the drift line below is how to see by how much.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultSettlementEconomyModel), "GetTownGoldChange")]
        private static class TownGoldChangePatch
        {
            private static bool Prefix(Town town, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null || !town.IsTown)
                {
                    return true;
                }

                __result = 0;

                // The controller is gone but its target is still the best yardstick there is for what a
                // town of this size "should" hold, so it is computed and reported without being acted
                // on. The line now reads as DRIFT: how far real trade has carried the town from the
                // figure vanilla would have pinned it to, and in which direction. A town drifting up
                // without limit is the garrison-spending imbalance made visible; one drifting to zero
                // is a market that has stopped being paid.
                if (EconomyLog.IsEnabled)
                {
                    float countryside = RBMProsperityEquilibrium.TreasuryProsperity(town) * 12f;
                    float target = 10000f + countryside + TroopMarketFeedback.TreasuryBonus(town, countryside);
                    float drift = town.Gold - target;

                    EconomyLog.Log("LIQUID", town.Settlement.Name != null ? town.Settlement.Name.ToString() : town.Settlement.StringId,
                        "drift " + (drift >= 0f ? "+" : "") + MathF.Round(drift) + "d"
                        + "  ·  gold " + town.Gold + " vs vanilla target " + MathF.Round(target)
                        + "  ·  countryside " + MathF.Round(countryside) + "d"
                        + "  ·  controller OFF");
                }

                return false;
            }
        }

        /// <summary>
        /// Rescales the daily demand POOL -- the gold a town's civilians bring to each item category,
        /// which <c>UpdateDemandShift</c> hands to <c>MakeConsumption</c> to spend. This is the leg
        /// that recoups what the town paid its villagers, so it belongs on the gold scale.
        ///
        /// This is only half of what <c>ItemData.Demand</c> is used for; the price half is split off
        /// in <see cref="EstimatedDemandPatch"/> below.
        ///
        /// <paramref name="extraProsperity"/> is deliberately NOT scaled: it is vanilla's own nudge,
        /// already expressed in vanilla prosperity units. The same goes for the 3000 luxury threshold,
        /// which now compares against a figure back on its intended scale.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementEconomyModel), "GetDailyDemandForCategory")]
        private static class DailyDemandPatch
        {
            private static bool Prefix(Town town, ItemCategory category, int extraProsperity, ref float __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null || !town.IsTown || category == null)
                {
                    return true;
                }

                float prosperity = RBMProsperityEquilibrium.EconomicProsperity(town);
                float baseline = MathF.Max(0f, prosperity + extraProsperity);
                float luxury = MathF.Max(0f, prosperity - 3000f);

                // Vanilla's fallback for a category with no base demand at all: a flat fraction of
                // prosperity, so such goods still trade rather than sitting at zero demand forever.
                __result = (category.BaseDemand < 1E-08f)
                    ? baseline * 0.01f
                    : category.BaseDemand * baseline + category.LuxuryDemand * luxury;

                return false;
            }
        }

        /// <summary>
        /// Keeps the PRICE half of demand on the household scale, undoing the gold rescale for this
        /// path alone.
        ///
        /// <c>ItemData.Demand</c> does two unrelated jobs. As a spending pool it is gold, and must
        /// track the gold scale. As the numerator of
        /// <c>(demand / (0.1*supply + 0.04*inStoreValue + 2))^0.6</c> it is compared against supply
        /// and in-store value -- physical unit counts that nothing rescaled. Scaling one input of a
        /// ratio and not the other is a units error, and it showed: every price rose by 20^0.6, or
        /// about six times, so towns bought a sixth as much with the very liquidity they had just
        /// been given. Measured at 330 denars for grain valued at 60.
        ///
        /// The two paths are separable because each has exactly one caller --
        /// <c>UpdateDemandShift</c> takes the pool, <c>UpdateSupplyAndDemand</c> takes this one --
        /// so the price side can be divided back down without touching the budget.
        ///
        /// Deriving it by dividing the scaled figure, rather than rewriting the formula against raw
        /// prosperity, is deliberate: it keeps vanilla's 1000 nudge and 3000 luxury threshold at the
        /// same RELATIVE size they have in the original. Written out by hand the threshold would have
        /// to be divided too, or a household-scale town would never reach it and luxury goods would
        /// lose their price support entirely.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementEconomyModel), "GetEstimatedDemandForCategory")]
        private static class EstimatedDemandPatch
        {
            private static bool Prefix(Town town, ItemCategory category, ref float __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null || !town.IsTown || category == null)
                {
                    return true;
                }

                // Re-enters the pool patch above, which is a different method -- no recursion.
                float pool = Campaign.Current.Models.SettlementEconomyModel.GetDailyDemandForCategory(town, category, 1000);
                __result = pool / RBMProsperityEquilibrium.VanillaProsperityScale;
                return false;
            }
        }
    }
}
