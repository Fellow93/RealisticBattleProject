using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Folds the extended (Alt) settlement tooltip's troop list -- one row per troop type -- into one row
    /// per troop type and tier, e.g. "Infantry, tier 3   45+3w". A big garrison of thirty-odd troop types
    /// ran the tooltip off the bottom of the screen; grouped, it fits.
    /// </summary>
    /// <remarks>
    /// Runs from the Settlement tooltip wrapper in <see cref="SettlementWealthTooltip"/>, after vanilla's or
    /// War Sails' refresher has built the list. Both build each section (troops, then prisoners) through
    /// AddPartyTroopProperties: a spacer, the section title, the per-formation totals, and -- extended only --
    /// a spacer, a "str_troop_types" header, a separator, one row per hero, one row per troop type, up to the
    /// next spacer (TextHeight -1). We drop the troop-type rows and add the grouped ones after the heroes.
    /// The rosters are rebuilt here with the refreshers' own party filters, so the totals match the
    /// formation rows above them. Vanilla's rows refresh their counts while the tooltip stays open; ours are
    /// a snapshot taken when it opens (or when Alt is pressed, which rebuilds it).
    /// </remarks>
    public static class SettlementTroopTooltip
    {
        public static void GroupTroopTypes(PropertyBasedTooltipVM propertyBasedTooltipVM, Settlement settlement)
        {
            if (!propertyBasedTooltipVM.IsExtended || settlement == null || settlement.IsHideout || settlement.Party == null)
            {
                return;
            }
            MBBindingList<TooltipProperty> list = propertyBasedTooltipVM.TooltipPropertyList;

            var troops = new TroopGroups();
            var prisoners = new TroopGroups();
            foreach (MobileParty party in settlement.Parties)
            {
                if (party.IsMainParty || FactionManager.IsAtWarAgainstFaction(party.MapFaction, settlement.MapFaction))
                {
                    continue;
                }
                if (!(party.Aggressiveness < 0.01f) || party.IsGarrison || party.IsMilitia)
                {
                    troops.Add(party.MemberRoster);
                }
                prisoners.Add(party.PrisonRoster);
            }
            prisoners.Add(settlement.Party.PrisonRoster);

            GroupSection(list, GameTexts.FindText("str_map_tooltip_troops").ToString(), troops);
            GroupSection(list, GameTexts.FindText("str_map_tooltip_prisoners").ToString(), prisoners);
        }

        private static void GroupSection(MBBindingList<TooltipProperty> list, string title, TroopGroups groups)
        {
            if (groups.Counts.Count == 0)
            {
                return;
            }
            int titleIndex = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].DefinitionLabel == title)
                {
                    titleIndex = i;
                    break;
                }
            }
            if (titleIndex < 0)
            {
                return;
            }

            // The per-type list is the part of the section after its first spacer, and only when that spacer
            // opens the "Troop types" header; any other spacer means the section ended without one (the
            // settlement is not inspected) and the next section begins.
            string typesHeader = GameTexts.FindText("str_troop_types").ToString();
            int start = -1;
            for (int i = titleIndex + 1; i < list.Count; i++)
            {
                if (list[i].TextHeight != -1)
                {
                    continue;
                }
                if (i + 1 < list.Count && list[i + 1].DefinitionLabel == typesHeader)
                {
                    start = i + 3; // spacer, header, separator
                }
                break;
            }
            if (start < 0 || start > list.Count)
            {
                return;
            }
            int end = start;
            while (end < list.Count && list[end].TextHeight != -1)
            {
                end++;
            }

            for (int i = end - 1; i >= start; i--)
            {
                if (groups.TroopNames.Contains(list[i].DefinitionLabel))
                {
                    list.RemoveAt(i);
                    end--;
                }
            }

            foreach (KeyValuePair<int, int[]> group in groups.Counts)
            {
                FormationClass formationClass = (FormationClass)(group.Key / 100);
                TextObject label = new TextObject("{=RBM_troop_group}{TYPE}, tier {TIER}");
                label.SetTextVariable("TYPE", GameTexts.FindText("str_troop_type_name", formationClass.GetName()));
                label.SetTextVariable("TIER", group.Key % 100);
                string value = PartyBaseHelper.GetPartySizeText(group.Value[0], group.Value[1], true).ToString();
                list.Insert(end, new TooltipProperty(label.ToString(), value, 0));
                end++;
            }
        }

        /// <summary>
        /// Healthy and wounded men of a section's non-hero troops by formation class and tier (key
        /// class x 100 + tier, so the sorted order is vanilla's formation order, then tier upward), and the
        /// troop names whose rows the grouped ones replace.
        /// </summary>
        private sealed class TroopGroups
        {
            public readonly SortedDictionary<int, int[]> Counts = new SortedDictionary<int, int[]>();
            public readonly HashSet<string> TroopNames = new HashSet<string>();

            public void Add(TroopRoster roster)
            {
                if (roster == null)
                {
                    return;
                }
                for (int i = 0; i < roster.Count; i++)
                {
                    TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                    if (element.Character == null || element.Character.IsHero || element.Number <= 0)
                    {
                        continue;
                    }
                    TroopNames.Add(element.Character.Name.ToString());
                    int key = (int)element.Character.DefaultFormationClass * 100 + element.Character.Tier;
                    if (!Counts.TryGetValue(key, out int[] count))
                    {
                        count = new int[2];
                        Counts.Add(key, count);
                    }
                    count[0] += element.Number - element.WoundedNumber;
                    count[1] += element.WoundedNumber;
                }
            }
        }
    }
}
