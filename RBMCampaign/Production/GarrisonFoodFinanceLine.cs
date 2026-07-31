using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Shows the garrison-food shortfall a lord pays out of his own gold on the clan finance breakdown --
    /// the part a fief's treasury cannot cover when provisioning its garrison, billed to the owner by
    /// <see cref="RBMTownFoodSupply"/>'s PayForGarrisonFood. It is the food-side twin of the garrison
    /// WAGE the owner already sees (<see cref="GarrisonUpkeep"/> shrinks his CalculatePartyWage line);
    /// without it the food drain was the one recurring player-gold expense with no line to explain it.
    /// </summary>
    /// <remarks>
    /// DISPLAY ONLY, and unlike the wealth-tax income this deliberately cannot move to the finance apply
    /// pass. The money is handed to the town's food-sellers (citizen wealth) the instant it is charged,
    /// and <c>Hero.Gold</c> floors at zero, so deferring the owner's leg past that credit would let a
    /// broke lord's charge clamp while the sellers keep the full sum -- money minted from the gap. So the
    /// charge stays immediate and exact in PayForGarrisonFood, and this only surfaces it: a stable
    /// last-day figure, player clan only, added as an expense. No coin moves here, so it cannot double-bill.
    /// </remarks>
    public static class GarrisonFoodFinanceLine
    {
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange")]
        private class ShowGarrisonFoodThroughFinance
        {
            private static void Postfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
            {
                if (applyWithdrawals || clan == null || clan != Clan.PlayerClan
                    || !RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }
                int cost = RBMTownFoodSupply.GetClanDailyGarrisonFoodOwnerCost(clan);
                if (cost > 0)
                {
                    // An expense: negative lowers the daily change, the sign costs are counted with.
                    __result.Add(-cost, new TextObject("{=RBM_garrison_food}Garrison provisioning"));
                }
            }
        }
    }
}
