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
    /// Both patches are pure rescalings -- the formulae are vanilla's, term for term, with the
    /// prosperity input converted. Nothing here changes the shape of the economy; it undoes a change
    /// of units that the prosperity rework made by accident.
    ///
    /// Why both, and not just the treasury: a town's gold is a float that circulates, not income. It
    /// buys villager cargo and recoups that gold selling to its own townspeople, and the two legs
    /// scale off different things. <c>SellGoodsForTradeAction</c> is demand-blind -- it buys
    /// <c>min(units, Gold / price)</c> of whatever arrives, at full price -- while the recouping leg
    /// is the civilian demand pool, <c>BaseDemand * Prosperity</c>. Enlarging the treasury alone would
    /// just buy a warhorse sooner and drain again; the outlet has to widen with the inlet.
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
        /// Rebases the target town treasury on the countryside, by way of the prosperity that now
        /// derives from it. Vanilla's <c>GetTownGoldChange</c> is not income at all: it is a
        /// proportional controller that closes a quarter of the gap to a target of
        /// <c>10000 + 12 * Prosperity</c> each day, and it is symmetric -- a town above target has
        /// gold destroyed. Only the target's prosperity term is rescaled.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementEconomyModel), "GetTownGoldChange")]
        private static class TownGoldChangePatch
        {
            private static bool Prefix(Town town, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null || !town.IsTown)
                {
                    return true;
                }

                float gap = 10000f + RBMProsperityEquilibrium.TreasuryProsperity(town) * 12f - town.Gold;
                __result = MathF.Round(0.25f * gap);
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
