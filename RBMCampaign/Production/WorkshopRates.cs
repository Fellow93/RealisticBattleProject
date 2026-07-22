using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Raises the output of the workshops whose goods RBM's households actually depend on.
    ///
    /// Vanilla's conversion speeds were set against vanilla's appetite. RBM multiplied a town's
    /// consumption roughly tenfold when it took <c>NumberOfProsperityToEatOneFood</c> from 40 to 4, then
    /// added a household basket and tavern fare on top, and never repriced the only thing that
    /// manufactures the goods those demands are denominated in. Beer is the clearest case: it is 17.6%
    /// of every ration and 38% of what a soldier drinks, and one brewery at 3.5 cycles a day makes seven
    /// casks -- against citizen demand alone of about nine.
    ///
    /// This is a rate correction, not a subsidy: nothing is conjured, the brewery still buys its grain
    /// off the same shelf and still has to clear vanilla's profitability gate. It simply works harder.
    /// </summary>
    /// <remarks>
    /// Applied per workshop TYPE rather than per production, because the model is handed the workshop
    /// and its speed but not which of its recipes is being ticked
    /// (<c>GetEffectiveConversionSpeedOfProduction(Workshop, float, bool)</c>). For a single-purpose
    /// shop like a brewery the two are the same thing.
    ///
    /// A limit worth knowing before this is tuned any further: a town has about six workshops drawn from
    /// thirteen types, so a good many towns have no brewery at all, and no conversion speed will give
    /// those towns beer. If beer is still short after this, the question is how workshop types are
    /// allotted, not how fast they run.
    /// </remarks>
    public static class WorkshopRates
    {
        /// <summary>
        /// Multiplier on a workshop type's conversion speed, by type id. Anything absent runs at
        /// vanilla's rate.
        /// </summary>
        /// <remarks>
        /// Half again for the brewery: 3.5 cycles a day becomes 5.25, and at two casks a cycle that is
        /// about ten a day against the nine the households want -- enough to feed the town with a little
        /// over for the taverns, rather than enough to flood it. Deliberately modest, because beer's
        /// price is now set by days of supply and a glut would collapse it to the floor and put the
        /// brewery back out of business.
        /// </remarks>
        private static readonly Dictionary<string, float> SpeedMultipliers = new Dictionary<string, float>
        {
            { "brewery", 1.5f },
        };

        private static readonly TextObject RateText = new TextObject("{=RBM_WORKSHOP_RATE}Local demand");

        [HarmonyPatch(typeof(DefaultWorkshopModel), "GetEffectiveConversionSpeedOfProduction")]
        private static class ConversionSpeedPatch
        {
            private static void Postfix(Workshop workshop, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || workshop == null || workshop.WorkshopType == null)
                {
                    return;
                }

                float multiplier;
                if (!SpeedMultipliers.TryGetValue(workshop.WorkshopType.StringId, out multiplier) || multiplier == 1f)
                {
                    return;
                }

                // As a FACTOR rather than a replacement, so buildings, policies and the Sweatshops perk
                // keep their proportional effect instead of being overwritten.
                __result.AddFactor(multiplier - 1f, RateText);
            }
        }
    }
}
