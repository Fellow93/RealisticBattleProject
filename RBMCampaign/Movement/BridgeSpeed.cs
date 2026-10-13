using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// A bridge is a road, not a ford. Vanilla <see cref="DefaultPartySpeedCalculatingModel.CalculateFinalSpeed"/>
    /// lumps <see cref="TerrainType.Bridge"/> in with fording and shallow water and takes the same -30% off a
    /// party on it, so a long bridge was slower to cross than wading a ford beside it. This gives the
    /// penalty back on a bridge face. The NavalDLC speed model calls the default one as its base model, so
    /// this covers it too; parties at sea never got the penalty and are left alone.
    /// </summary>
    internal static class BridgeSpeed
    {
        // Vanilla's ford penalty, mirrored to cancel it exactly (ExplainedNumber factors add up).
        private const float FordPenalty = 0.3f;

        private static readonly TextObject BridgeText = new TextObject("{=RBM_BRIDGE_SPEED}Bridge");

        [HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel), nameof(DefaultPartySpeedCalculatingModel.CalculateFinalSpeed))]
        private static class CalculateFinalSpeedPatch
        {
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                if (!RC.rbmCampaignEnabled || mobileParty == null || mobileParty.IsCurrentlyAtSea)
                {
                    return;
                }
                if (Campaign.Current.MapSceneWrapper.GetFaceTerrainType(mobileParty.CurrentNavigationFace) == TerrainType.Bridge)
                {
                    __result.AddFactor(FordPenalty, BridgeText);
                }
            }
        }
    }
}
