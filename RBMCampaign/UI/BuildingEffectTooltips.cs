using HarmonyLib;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Tells the player what a building is actually worth under RBM.
    ///
    /// Vanilla writes a building's effects out of its own effect table, one line per
    /// <c>BuildingEffectEnum</c>, and that table is still true as far as it goes -- a Barracks still cuts
    /// garrison wages, a Warehouse still helps the workshops. But most of what a building does in RBM is
    /// not in that table at all: the wall that decides a siege, the lodgings that decide how fast a
    /// garrison fills, the mason yard that decides whether anything gets built. A player reading the
    /// vanilla text would be choosing his projects on a fraction of the facts.
    ///
    /// So a plain "RBM:" block is appended to the effect text the town management screen builds, per
    /// building type, naming the RBM effects at all three levels at once. It hangs off
    /// <see cref="BuildingType.GetExplanationAtLevel"/> -- the single method every one of those surfaces
    /// reads (the project list's current and next-level lines both route through it) -- so one postfix
    /// covers them all, and a building RBM does not touch is left exactly as vanilla wrote it.
    /// </summary>
    internal static class BuildingEffectTooltips
    {
        [HarmonyPatch(typeof(BuildingType), "GetExplanationAtLevel")]
        private static class ExplanationPatch
        {
            private static void Postfix(BuildingType __instance, int level, ref TextObject __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || __instance == null || level < 1 || level > 3)
                {
                    return;
                }
                string extra = Describe(__instance);
                if (extra == null)
                {
                    return;
                }

                // Composed through text variables rather than by string concatenation, so nothing in the
                // vanilla text is re-parsed as markup on the way through.
                TextObject wrapper = new TextObject("{=!}{BASE}\n{RBM}");
                wrapper.SetTextVariable("BASE", __result);
                wrapper.SetTextVariable("RBM", new TextObject("{=!}" + extra));
                __result = wrapper;
            }
        }

        /// <summary>
        /// The RBM line for a building type, or null when RBM adds nothing to it. Written at all three
        /// levels together (the "+10/20/30%" idiom) because a player choosing what to build is choosing a
        /// whole ladder, not the next rung.
        /// </summary>
        private static string Describe(BuildingType type)
        {
            if (type == DefaultBuildingTypes.SettlementFortifications || type == DefaultBuildingTypes.CastleFortifications)
            {
                return "RBM: siege defence +10/20/30% · garrison & militia maintenance -0/5/10%";
            }
            if (type == DefaultBuildingTypes.SettlementBarracks || type == DefaultBuildingTypes.CastleBarracks)
            {
                return "RBM: cost of arming garrison & militia recruits -5/10/15% · garrison and militia intake +1/2/3 per day when the treasury can fund them";
            }
            if (type == DefaultBuildingTypes.SettlementTrainingFields || type == DefaultBuildingTypes.CastleTrainingFields)
            {
                return "RBM: garrison promotions -5/10/15% · garrison and militia gain +10/20/30 experience a day (replaces the 1/2/3 above)";
            }
            if (type == DefaultBuildingTypes.SettlementGuardHouse || type == DefaultBuildingTypes.CastleGuardHouse)
            {
                return "RBM: tariff on caravan and traveller trade +0.3/0.6/1.0 percentage points · convicts kept at work on the fief's building projects";
            }
            if (type == DefaultBuildingTypes.SettlementTaxOffice)
            {
                return "RBM: wealth tax and minting cuts +5/10/15%, to the fief and its lord alike";
            }
            if (type == DefaultBuildingTypes.SettlementMarketplace)
            {
                return "RBM: tariff on every trade in the settlement +10/20/30%";
            }
            if (type == DefaultBuildingTypes.SettlementWarehouse || type == DefaultBuildingTypes.CastleGranary)
            {
                return "RBM: the granary holds 20/30/40 days of the fief's own eating (10 days with no granary), replacing the fixed food limit";
            }
            if (type == DefaultBuildingTypes.SettlementMason || type == DefaultBuildingTypes.CastleMason)
            {
                return "RBM: construction efficiency +5/10/15% · labour ceiling +10/20/30% (replaces construction per day)";
            }
            if (type == DefaultBuildingTypes.SettlementWaterworks)
            {
                return "RBM: everything else the town has built is worth +10/20/30% more prosperity";
            }
            if (type == DefaultBuildingTypes.SettlementRoadsAndPaths || type == DefaultBuildingTypes.CastleRoadsAndPaths)
            {
                return "RBM: bound villages produce +5/10/15% more goods";
            }
            return null;
        }
    }
}
