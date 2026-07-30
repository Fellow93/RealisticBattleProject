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

            if (MobileParty.MainParty == null || MobileParty.MainParty.CurrentSettlement != null)
                return;

            if (Campaign.Current.CurrentMenuContext != null)
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

            // The target has to have a party of his own to hand us. Without one, ChangePlayerCharacterAction
            // falls into its heir-with-no-party branch and mints a fresh player party at a guessed position --
            // never what a "switch to this lord" is asking for.
            if (targetHero.PartyBelongedTo == null || targetHero.IsPrisoner)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    targetHero.Name + " has no field party to switch into.", Colors.Red));
                return;
            }

            Hero oldHero = Hero.MainHero;
            Clan oldClan = Clan.PlayerClan;
            Clan newClan = targetHero.Clan;

            // The two parties in play: the one we are leaving (the current main party) and the one we are taking
            // over (the target lord's). Captured before Apply moves MainParty off the first onto the second.
            MobileParty oldParty = MobileParty.MainParty;
            MobileParty newParty = targetHero.PartyBelongedTo;

            // THE FIX FOR THE PERMANENT STUTTER.
            //
            // ChangePlayerCharacterAction was written for heir SUCCESSION -- the old character is dead, the heir
            // usually had no party -- so it never considers that either party might be in an ARMY. Switching to a
            // LIVING lord routinely does: in wartime most lords are armied. If we let the swap run with the target
            // still attached to an AI army, the player party ends up INSIDE an AI-commanded army, and the army's
            // per-tick logic (cohesion decay, gather-around, disband checks, and the leader's move orders to its
            // members) runs every frame against a party that now answers only to the player -- a conflict that
            // never resolves. That is the sudden, extreme, and unending stutter. Pull both parties clear of any
            // army first: setting Army = null removes a member cleanly, or disbands the army if the party led it.
            DetachFromArmy(newParty);
            DetachFromArmy(oldParty);

            if (newClan != null && newClan != oldClan && PlayerDefaultFactionProp != null)
            {
                PlayerDefaultFactionProp.SetValue(Campaign.Current, newClan);
            }

            ChangePlayerCharacterAction.Apply(targetHero);

            // Repair the party we just left. Apply's succession branch handed it to the NEW hero
            // (LordPartyComponent.ChangePartyOwner(Hero.MainHero), which by that point is already the target) while
            // leaving its Leader as the old hero -- an ownerless-feeling, split-brain party owned by the player's
            // new clan but led by an old-clan hero. Reassigning the old hero as leader routes through
            // LordPartyComponent.OnChangePartyLeader, which sets both _leader AND Owner back to him, turning it
            // into a clean AI lord party for the lord we stopped playing. (ActualClan already tracks the old clan,
            // so MapFaction stays correct.)
            if (oldParty != null && oldParty != MobileParty.MainParty && oldParty.IsActive
                && oldParty.MemberRoster.TotalManCount > 0
                && oldHero != null && oldHero.IsAlive && !oldHero.IsPrisoner
                && oldParty.LordPartyComponent != null && oldParty.LeaderHero != oldHero)
            {
                oldParty.ChangePartyLeader(oldHero);
            }

            string msg = "Switched from " + oldHero.Name + " (" + (oldClan != null ? oldClan.Name.ToString() : "?") +
                         ") to " + targetHero.Name + " (" + (newClan != null ? newClan.Name.ToString() : "?") + ")";
            InformationManager.DisplayMessage(new InformationMessage(msg, Colors.Green));
        }

        /// <summary>
        /// Take a party out of whatever army it is in, if any. A player party must never sit inside an AI army:
        /// the army ticks its members every frame and would fight the player's control indefinitely. Nulling Army
        /// removes a member cleanly (OnRemovePartyInternal) or, if this party was the army's leader, disbands the
        /// army (DisbandArmyAction.ApplyByLeaderPartyRemoved) -- both acceptable outcomes for a debug switch.
        /// </summary>
        private static void DetachFromArmy(MobileParty party)
        {
            if (party != null && party.Army != null)
            {
                party.Army = null;
            }
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
