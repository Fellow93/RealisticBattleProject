using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Lifts the flat garrison party-size cap so a garrison's ceiling is set by town wealth and food
    /// rather than an artificial troop count.
    ///
    /// Vanilla sizes a garrison by <c>min(a, b2, b)</c> where <c>a</c> is this party-size limit (base 200
    /// +200 for a town, <c>DefaultPartySizeLimitModel.CalculateGarrisonPartySizeLimit</c>), <c>b2</c> is the
    /// town's food capacity and <c>b</c> is the wealth/prosperity/economy-driven ideal strength
    /// (<c>GarrisonTroopsCampaignBehavior.CalculateSettlementGarrisonPartySizeLimitWithFoodAndWage</c>). For
    /// any prosperous town the wealth ideal <c>b</c> exceeds ~400, so the flat cap <c>a</c> is the binding
    /// minimum -- a rich town is held to the same garrison as a poor one, and anything pushed above it is
    /// shed as party-size desertion (<c>DefaultPartyDesertionModel</c>). RBM already lets the fief treasury
    /// carry the garrison wage bill (see <see cref="GarrisonUpkeep"/> and <see cref="GarrisonWageLimit"/>),
    /// so the intended governor is wealth and food, not this count.
    ///
    /// Adding a large flat bonus pushes <c>a</c> clear of any realistic <c>b</c>/<c>b2</c>, so
    /// <c>min(a, b2, b) = min(b2, b)</c> -- garrison size follows town wealth and food. The two auto-recruit
    /// paths keep their own independent wage/food ceilings, so this does not let a garrison out-recruit what
    /// the town can pay for or feed; it only removes the flat count as the ceiling. The reinforcement
    /// take/leave math reads the passing lord's party-size limit, not the garrison's, so it is untouched.
    /// </summary>
    [HarmonyPatch(typeof(DefaultPartySizeLimitModel), nameof(DefaultPartySizeLimitModel.CalculateGarrisonPartySizeLimit))]
    internal static class GarrisonPartySize
    {
        // Large enough to clear any wealth/food-driven ideal strength, so this flat cap never binds.
        private const float UncapBonus = 10000f;

        private static readonly TextObject BonusText = new TextObject("{=RBM_garr_size}Town wealth (RBM)");

        private static void Postfix(Settlement settlement, ref ExplainedNumber __result)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }

            __result.Add(UncapBonus, BonusText);
        }
    }
}
