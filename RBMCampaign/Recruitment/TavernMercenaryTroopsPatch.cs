using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;

namespace RBMCampaign
{
    /// <summary>
    /// Removes caravan guards from the tavern mercenary pool.
    ///
    /// Vanilla <see cref="TaleWorlds.CampaignSystem.CampaignBehaviors.RecruitmentCampaignBehavior"/>
    /// re-rolls each town's single mercenary stack daily: with probability
    /// <c>RegularMercenariesSpawnChance</c> (0.7) it draws from <c>Culture.BasicMercenaryTroops</c>,
    /// otherwise it stocks the culture's <c>CaravanGuard</c>. Forcing the chance to 1.0 makes the
    /// caravan-guard branch unreachable, so towns always offer a real mercenary line instead —
    /// caravan guards never appear in the backstreet for the player, AI caravans, or lords.
    /// </summary>
    [HarmonyPatch(typeof(DefaultTavernMercenaryTroopsModel), "RegularMercenariesSpawnChance", MethodType.Getter)]
    public static class OverrideRegularMercenariesSpawnChance
    {
        public static void Postfix(ref float __result)
        {
            __result = 1f;
        }
    }
}
