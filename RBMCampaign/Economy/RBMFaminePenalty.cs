using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Retunes the prosperity a fief loses while starving.
    ///
    /// <c>DefaultSettlementProsperityModel</c> charges <c>0.5</c> prosperity per unit of daily food
    /// deficit. That coefficient was set against vanilla's ration of
    /// <c>Prosperity / NumberOfProsperityToEatOneFood</c> at a divisor of 40, which makes a total
    /// famine cost <c>P/80</c> -- about 1.25% of prosperity per day.
    ///
    /// RBM lowers that divisor to 4 (see <see cref="RBMVillageProduction"/>), so a town eats ten
    /// times as much and its deficit is ten times larger for the same shortfall. The coefficient did
    /// not move with it, leaving a total famine at <c>P/8</c> -- 12.5% per day, which erases a city
    /// inside a fortnight. The penalty tracks a quantity whose scale changed underneath it.
    ///
    /// Dividing the coefficient by ten restores vanilla's severity in proportional terms. Note this
    /// is a retune, not a reshaping: the penalty is still a fixed multiple of the absolute deficit,
    /// so it would need revisiting again if the ration divisor moves. Making it proportional by
    /// construction -- a percentage of prosperity scaled by <c>unmet / required</c> -- would decouple
    /// the two permanently, at the cost of also having to carry the ration ratio out of
    /// <see cref="RBMTownFoodSupply"/>.
    /// </summary>
    public static class RBMFaminePenalty
    {
        /// <summary>Vanilla's prosperity loss per unit of daily food deficit.</summary>
        private const float VanillaCoefficient = 0.5f;

        /// <summary>RBM's, set so a total famine costs about what it does in vanilla as a share of prosperity.</summary>
        private const float RBMCoefficient = 0.05f;

        [HarmonyPatch(typeof(DefaultSettlementProsperityModel), "CalculateProsperityChange")]
        private static class FamineCoefficientPatch
        {
            private static void Postfix(Town fortification, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || fortification == null || !fortification.Settlement.IsFortification)
                {
                    return;
                }

                // Match vanilla's own gates exactly. Outside them it adds nothing, so there would be
                // nothing to correct -- and adding a correction anyway would invent a penalty where
                // the base model had none. Helping Hands is an AddFactor perk, so it cannot turn a
                // zero deficit into a non-zero term either.
                float foodChange = fortification.FoodChange;
                if (!fortification.Owner.IsStarving || foodChange >= 0f)
                {
                    return;
                }

                // Rebuild vanilla's deficit term, including the governor perk that mitigates it, then
                // apply only the DIFFERENCE between the two coefficients. Correcting by the delta
                // rather than cancelling and re-adding keeps the perk applied exactly once and leaves
                // a single tooltip line.
                ExplainedNumber deficit = new ExplainedNumber((int)foodChange);
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Medicine.HelpingHands, fortification, ref deficit);
                __result.Add(deficit.ResultNumber * (RBMCoefficient - VanillaCoefficient), FoodShortageText);
            }
        }

        private static readonly TextObject FoodShortageText = new TextObject("{=RBM_PROSPERITY_FAMINE}Food shortage (RBM)");
    }
}
