using HarmonyLib;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// When a village's own people cart its produce to a town and sell it, the coin comes home. The sale
    /// feeds the Hearth of the village the goods were grown in -- not the town they were sold at -- at the
    /// same settlementProsperityPerGoldSpent rate. Villagers sell through SellGoodsForTradeAction, a path
    /// apart from the market trade <see cref="MarketTradeProsperity"/> covers, so it takes its own credit.
    /// </summary>
    [HarmonyPatch(typeof(SellGoodsForTradeAction), "ApplyByVillagerTrade")]
    internal static class VillagerTradeHearth
    {
        // The proceeds of the sale pile into the party's trade gold, so its rise across the call is
        // exactly what the town paid for the produce.
        private static void Prefix(MobileParty villagerParty, out int __state)
        {
            __state = (villagerParty != null) ? villagerParty.PartyTradeGold : 0;
        }

        private static void Postfix(Settlement settlement, MobileParty villagerParty, int __state)
        {
            float rate = RBMConfig.RBMConfig.settlementProsperityPerGoldSpent;
            if (rate <= 0f || villagerParty == null)
            {
                return;
            }
            int proceeds = villagerParty.PartyTradeGold - __state;
            if (proceeds <= 0)
            {
                return;
            }
            Village home = villagerParty.HomeSettlement?.Village;
            if (home == null)
            {
                return;
            }

            float gain = proceeds * rate;
            home.Hearth += gain;

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("HAUL", villagerParty.HomeSettlement.Name + ": villagers sold produce at "
                    + (settlement != null ? settlement.Name.ToString() : "market")
                    + " for " + proceeds + " gold -> +" + gain.ToString("0.00") + " hearth");
            }
        }
    }
}
