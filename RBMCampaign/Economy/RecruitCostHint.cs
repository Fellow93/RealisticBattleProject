using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The recruit screen shows one gold number per volunteer and no sign of what makes it up. Under
    /// RBM's recruit pricing that number is a soldier's gear plus a five-day enlistment bounty (and, for
    /// an outsider, a levy on top), so the player is owed a breakdown. This appends an itemised
    /// "Recruitment cost" section -- Equipment, Enlistment, Foreign levy -- onto the character card the
    /// recruit tile already shows on hover, reading the exact same model call the tile's Cost is built
    /// from (see <see cref="RecruitSupply.MainPartyRecruitCost"/>), so the parts always sum to the price.
    ///
    /// Two patches, because the card builder (<c>TooltipRefresherCollection.RefreshCharacterTooltip</c>)
    /// is shared by every CharacterObject tooltip in the game and knows nothing of the recruit screen:
    /// the recruit tile's hover records which volunteer the cursor is over, and the card builder appends
    /// the cost only for that one character. Off when the recruit-supply feature is off, in which case
    /// the price is vanilla's and there is no wage/gear split to show.
    /// </summary>
    public static class RecruitCostHint
    {
        private const string CoinIcon = "<img src=\"General\\Icons\\Coin@2x\" extend=\"6\">";

        /// <summary>The volunteer whose tile the cursor is currently over, or null when over none.</summary>
        private static CharacterObject _hoveredVolunteer;

        /// <summary>
        /// Records the hovered volunteer before the tile shows its character card, so the shared card
        /// builder below can tell this card apart from every other CharacterObject tooltip. Only the
        /// branch that shows the card (a recruitable troop the player has the relation to raise) sets it;
        /// the not-enough-relation branch shows its own property list and wants no cost section.
        /// </summary>
        [HarmonyPatch(typeof(RecruitVolunteerTroopVM))]
        [HarmonyPatch("ExecuteBeginHint")]
        private class TrackHoveredVolunteer
        {
            private static void Prefix(RecruitVolunteerTroopVM __instance)
            {
                _hoveredVolunteer = (__instance != null && __instance.Character != null && __instance.PlayerHasEnoughRelation)
                    ? __instance.Character
                    : null;
            }
        }

        [HarmonyPatch(typeof(RecruitVolunteerTroopVM))]
        [HarmonyPatch("ExecuteEndHint")]
        private class ForgetHoveredVolunteer
        {
            private static void Postfix()
            {
                _hoveredVolunteer = null;
            }
        }

        /// <summary>
        /// Appends the recruit-cost breakdown onto the volunteer's character card. Matches on the exact
        /// character the tile recorded, so no other CharacterObject tooltip is touched. GetLines() carries
        /// the named parts (Equipment, Enlistment, Foreign levy) with their denar amounts; the total is
        /// the card's own RoundedResultNumber, i.e. the number printed on the tile.
        /// </summary>
        [HarmonyPatch(typeof(TooltipRefresherCollection))]
        [HarmonyPatch("RefreshCharacterTooltip")]
        private class AppendRecruitCostToCharacterTooltip
        {
            private static void Postfix(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] args)
            {
                CharacterObject volunteer = _hoveredVolunteer;
                if (volunteer == null || !RecruitSupply.IsEnabled || propertyBasedTooltipVM == null)
                {
                    return;
                }
                if (args == null || args.Length == 0 || !(args[0] is CharacterObject shown) || shown != volunteer)
                {
                    return;
                }

                ExplainedNumber cost = RecruitSupply.MainPartyRecruitCost(volunteer);
                string title = new TextObject("{=RBM_recruit_cost_title}Recruitment cost").ToString();

                propertyBasedTooltipVM.AddProperty("", "");
                if (cost.RoundedResultNumber <= 0)
                {
                    // Owner or ruler raising his own -- feudal service, owed not bought.
                    string free = new TextObject("{=RBM_recruit_cost_free}Free").ToString();
                    propertyBasedTooltipVM.AddProperty(title, free);
                    return;
                }

                propertyBasedTooltipVM.AddProperty(title, cost.RoundedResultNumber + CoinIcon);
                propertyBasedTooltipVM.AddProperty("", "", 0, TooltipProperty.TooltipPropertyFlags.RundownSeperator);
                foreach ((string name, float number) in cost.GetLines())
                {
                    int amount = MathF.Round(number);
                    if (string.IsNullOrEmpty(name) || amount == 0)
                    {
                        continue;
                    }
                    propertyBasedTooltipVM.AddProperty(name, amount + CoinIcon);
                }
            }
        }
    }
}
