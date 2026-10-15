using HarmonyLib;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.TownManagement;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The selected project's effect blocks in the town management screen. Vanilla glues its level tag onto
    /// the first effect line ("L-II : +10% ..."), which in a left-aligned list (see
    /// <see cref="ProjectsGridPrefabPatch"/>) reads as one odd line among the others. Here each block gets the
    /// tag as a header line of its own, saying which is the building as it stands and which the next upgrade,
    /// with the effect lines under it. The texts are rebuilt the way vanilla builds them (empty at level 0 /
    /// past level 3), so only the header changes. Daily projects have no levels and are left as they are.
    /// </summary>
    [HarmonyPatch(typeof(SettlementProjectVM), nameof(SettlementProjectVM.RefreshValues))]
    internal static class ProjectEffectHeaders
    {
        private static void Postfix(SettlementProjectVM __instance)
        {
            Building building = __instance?.Building;
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || building == null || building.BuildingType.IsDailyProject)
            {
                return;
            }
            __instance.CurrentPositiveEffectText = Block(building, building.CurrentLevel,
                "{=RBM_bld_effects_now}Now (level {LEVEL}):");
            __instance.NextPositiveEffectText = Block(building, building.CurrentLevel + 1,
                "{=RBM_bld_effects_next}Next (level {LEVEL}):");
        }

        private static string Block(Building building, int level, string header)
        {
            if (level < 1 || level > 3)
            {
                return "";
            }
            string effects = building.BuildingType.GetExplanationAtLevel(level).ToString();
            TextObject title = new TextObject(header);
            title.SetTextVariable("LEVEL", level);
            return effects.Length == 0 ? title.ToString() : title + "\n" + effects;
        }
    }
}
