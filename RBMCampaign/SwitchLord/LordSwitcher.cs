using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;

namespace RBMCampaign
{
    public static class LordSwitcher
    {
        private static readonly PropertyInfo PlayerDefaultFactionProp =
            typeof(Campaign).GetProperty("PlayerDefaultFaction",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static void CheckHotkey()
        {
            if (Campaign.Current == null)
                return;

            bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool shift = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);

            if (ctrl && shift && Input.IsKeyPressed(InputKey.L))
            {
                ShowLordPicker();
            }
        }

        public static void ShowLordPicker()
        {
            if (Campaign.Current == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("Not in a campaign.", Colors.Red));
                return;
            }

            var lordsByKingdom = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h != Hero.MainHero && h.PartyBelongedTo != null)
                .GroupBy(h => h.Clan != null ? h.Clan.Kingdom : null)
                .OrderBy(g => g.Key == null ? "zzz" : g.Key.Name.ToString());

            List<InquiryElement> elements = new List<InquiryElement>();

            foreach (var group in lordsByKingdom)
            {
                string kingdomName = group.Key != null ? group.Key.Name.ToString() : "No Kingdom";
                int count = group.Count();

                elements.Add(new InquiryElement(
                    null,
                    "--- " + kingdomName + " (" + count + ") ---",
                    null,
                    false,
                    kingdomName + ": " + count + " lords with active parties"));

                foreach (Hero h in group.OrderBy(h => h.Name.ToString()))
                {
                    elements.Add(new InquiryElement(
                        h,
                        h.Name + " (" + (h.Clan != null ? h.Clan.Name.ToString() : "?") + ")",
                        null,
                        true,
                        BuildHint(h)));
                }
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Switch Lord",
                "All lords with active parties, grouped by kingdom:",
                elements,
                true,
                1,
                1,
                "Switch",
                "Cancel",
                selected => OnLordSelected(selected),
                null,
                "",
                true), true);
        }

        private static string BuildHint(Hero h)
        {
            string clan = h.Clan != null ? h.Clan.Name.ToString() : "None";
            string kingdom = h.Clan != null && h.Clan.Kingdom != null ? h.Clan.Kingdom.Name.ToString() : "None";
            string party = h.PartyBelongedTo != null
                ? h.PartyBelongedTo.Name + " (" + h.PartyBelongedTo.MemberRoster.TotalManCount + " troops)"
                : "No party";
            return "Age: " + (int)h.Age + " | Clan: " + clan + " | Kingdom: " + kingdom + " | " + party;
        }

        private static void OnLordSelected(List<InquiryElement> selected)
        {
            if (selected == null || selected.Count == 0)
                return;

            Hero targetHero = selected[0].Identifier as Hero;
            if (targetHero == null)
                return;

            SwitchToLord(targetHero);
        }

        public static void SwitchToLord(Hero targetHero)
        {
            if (Campaign.Current == null || targetHero == null)
                return;

            Hero oldHero = Hero.MainHero;
            Clan oldClan = Clan.PlayerClan;
            Clan newClan = targetHero.Clan;

            if (newClan != null && newClan != oldClan && PlayerDefaultFactionProp != null)
            {
                PlayerDefaultFactionProp.SetValue(Campaign.Current, newClan);
            }

            ChangePlayerCharacterAction.Apply(targetHero);

            string msg = "Switched from " + oldHero.Name + " (" + (oldClan != null ? oldClan.Name.ToString() : "?") +
                         ") to " + targetHero.Name + " (" + (newClan != null ? newClan.Name.ToString() : "?") + ")";
            InformationManager.DisplayMessage(new InformationMessage(msg, Colors.Green));
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("switch_lord", "campaign")]
        public static string ConsoleSwitchLord(List<string> args)
        {
            if (Campaign.Current == null)
                return "Not in a campaign.";

            if (args == null || args.Count == 0)
                return "Usage: campaign.switch_lord <name>\nSearches for alive lords matching the name.";

            string searchText = string.Join(" ", args);

            List<Hero> matches = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h != Hero.MainHero &&
                       h.Name != null &&
                       h.Name.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(h => h.Name.ToString())
                .ToList();

            if (matches.Count == 0)
                return "No lords found matching '" + searchText + "'.";

            if (matches.Count > 1)
            {
                string list = string.Join("\n",
                    matches.Take(25).Select(h =>
                        "  " + h.Name + " (" + (h.Clan != null ? h.Clan.Name.ToString() : "no clan") + ")"));
                if (matches.Count > 25)
                    list += "\n  ... and " + (matches.Count - 25) + " more";
                return "Multiple matches for '" + searchText + "':\n" + list + "\nRefine your search.";
            }

            SwitchToLord(matches[0]);
            return "Switched to " + matches[0].Name + " (" +
                   (matches[0].Clan != null ? matches[0].Clan.Name.ToString() : "no clan") + ").";
        }
    }
}
