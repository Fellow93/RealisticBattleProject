using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Making a thing costs the place that makes it. Every good a settlement produces -- a workshop's
    /// wares in a town, a village's crops and raw goods -- is worked out of the settlement's own back:
    /// its worth is drawn off the town's Prosperity or the village's Hearth, at the same
    /// settlementProsperityPerGoldSpent rate that selling it back at market pours in. Production spends
    /// the place down, trade builds it back up, and the two only balance where goods actually move.
    /// </summary>
    public static class ProductionUpkeep
    {
        // Fires for every produced batch: village goods and food, and both town workshop paths.
        // Initial game-setup stocking does not raise it, so a fresh campaign is not drained on day one.
        public static void OnItemProduced(ItemObject item, Settlement settlement, int count)
        {
            float rate = RBMConfig.RBMConfig.settlementProsperityPerGoldSpent;
            if (rate <= 0f || item == null || settlement == null || count <= 0)
            {
                return;
            }
            if (settlement.Town == null && settlement.Village == null)
            {
                return;
            }

            float drain = (float)item.Value * count * rate;
            if (drain <= 0f)
            {
                return;
            }

            if (settlement.Town != null)
            {
                settlement.Town.Prosperity = MathF.Max(0f, settlement.Town.Prosperity - drain);
            }
            else
            {
                settlement.Village.Hearth = MathF.Max(0f, settlement.Village.Hearth - drain);
            }

            if (SpoilsLog.IsEnabled)
            {
                // Production fires many times a day per settlement -- every food unit raises it -- so the
                // line is throttled to the first of the day, just enough to confirm the drain is running.
                int day = (int)(CampaignTime.Now.ToHours / 24);
                SpoilsLog.LogOnce("make-" + settlement.StringId + "-" + day, "MAKE",
                    settlement.Name + ": producing goods, draining "
                    + (settlement.Town != null ? "prosperity" : "hearth") + " at " + rate.ToString("0.00")
                    + " per gold of worth (first item today: " + item.Name + " x" + count + ")");
            }
        }
    }
}
