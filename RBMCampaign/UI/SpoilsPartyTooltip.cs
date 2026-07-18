using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Adds a spoils line to the party hover tooltip on the campaign map, showing the party's total purse.
    /// Shown for any party the map already lets the player inspect, villagers aside (they hold no purse).
    /// </summary>
    [HarmonyPatch(typeof(TooltipRefresherCollection), "RefreshMobilePartyTooltip")]
    public static class SpoilsPartyTooltip
    {
        // A leading coin glyph, sized to sit on the text baseline, so a spoils value reads like the gold
        // figures the rest of the UI shows. This is passed as a raw ValueLabel string, not through a
        // TextObject, so it carries no {=key} marker -- the tooltip renderer would print such a marker
        // literally rather than strip it.
        private const string CoinIcon = "<img src=\"General\\Icons\\Coin@2x\" extend=\"6\"> ";

        public static void Postfix(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] args)
        {
            if (!SpoilsPool.IsEnabled)
            {
                return;
            }
            MobileParty mobileParty = args[0] as MobileParty;
            if (mobileParty == null || mobileParty.IsInfoHidden)
            {
                return;
            }
            PartyBase party = mobileParty.Party;
            if (SpoilsPool.IsExemptParty(party))
            {
                return;
            }

            int total = SpoilsPool.GetPartyTotalSpoils(party);
            if (total <= 0)
            {
                return;
            }

            List<TooltipProperty> block = new List<TooltipProperty>
            {
                new TooltipProperty(string.Empty, string.Empty, -1),
                new TooltipProperty(new TextObject("{=RBM_spoils_tt}Spoils").ToString(), CoinIcon + total, 0)
            };

            InsertBeforeTroops(propertyBasedTooltipVM, block);
        }

        /// <summary>
        /// Splices the spoils block in just ahead of the troops section, high in the tooltip. The extended
        /// (Alt) view lists every troop type and, on a large party, runs long enough that anything appended
        /// at the end is clipped off-screen; sitting above the troop roster keeps the purse in view. The
        /// troops header carries its own leading blank line, so the block lands before that blank. Falls
        /// back to the end if the section can't be located.
        /// </summary>
        private static void InsertBeforeTroops(PropertyBasedTooltipVM vm, List<TooltipProperty> block)
        {
            MBBindingList<TooltipProperty> list = vm.TooltipPropertyList;
            string troopsTitle = GameTexts.FindText("str_map_tooltip_troops").ToString();
            int insertAt = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].DefinitionLabel == troopsTitle)
                {
                    insertAt = i;
                    // Step back over the blank separator the troops header sits under.
                    if (insertAt > 0 && string.IsNullOrEmpty(list[insertAt - 1].DefinitionLabel)
                        && string.IsNullOrEmpty(list[insertAt - 1].ValueLabel))
                    {
                        insertAt--;
                    }
                    break;
                }
            }
            for (int i = 0; i < block.Count; i++)
            {
                list.Insert(insertAt + i, block[i]);
            }
        }
    }
}
