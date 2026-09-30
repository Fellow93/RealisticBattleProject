using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

namespace RBMCampaign
{
    /// <summary>
    /// Follows a stack's purse across the party screens that march real men out of the main party while
    /// showing a roster with no party behind it on the left: a donation to a garrison, and the raising of
    /// a clan party for a companion. <see cref="SpoilsTransferOnPartyScreen"/> reads both parties back off
    /// the screen and so passes these over -- the left owner is null, the men are handed to their new party
    /// by the screen's own handler instead. Each handler is hooked here, where the receiving party is known.
    /// </summary>
    public static class SpoilsTransferOnSpecialScreens
    {
        // ------------------------------------------------------------------ donation to a garrison

        // What the donation handler handed to the garrison, held until Done has finished. Done fires the
        // visit's upgrade events only after the handler returns, and a man promoted and donated in one
        // visit must have his purse carried onto his new troop name before it can march off with him --
        // the order the ordinary party screen already keeps.
        private static PartyBase _donatedTo;
        private static readonly List<KeyValuePair<CharacterObject, int>> _donated = new List<KeyValuePair<CharacterObject, int>>();

        private static void ClearDonation()
        {
            _donatedTo = null;
            _donated.Clear();
        }

        // The left roster is a dummy that opens empty, so what it holds when Done fires is exactly the men
        // donated. The right roster is the main party's own, reduced as the player dragged, and the handler
        // has just added the men to the garrison -- raising the garrison party first if the town had none,
        // which is why the party is read back only now.
        [HarmonyPatch(typeof(PartyScreenHelper))]
        [HarmonyPatch("DonateGarrisonDoneHandler")]
        private class TrackGarrisonDonation
        {
            private static void Postfix(TroopRoster leftMemberRoster, bool __result)
            {
                ClearDonation();
                if (!SpoilsPool.IsEnabled || !__result || leftMemberRoster == null)
                {
                    return;
                }
                PartyBase garrison = Hero.MainHero?.CurrentSettlement?.Town?.GarrisonParty?.Party;
                if (garrison == null)
                {
                    return;
                }
                for (int i = 0; i < leftMemberRoster.Count; i++)
                {
                    TroopRosterElement element = leftMemberRoster.GetElementCopyAtIndex(i);
                    if (element.Character != null && !element.Character.IsHero && element.Number > 0)
                    {
                        _donated.Add(new KeyValuePair<CharacterObject, int>(element.Character, element.Number));
                    }
                }
                _donatedTo = garrison;
            }
        }

        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("DoneLogic")]
        private class ApplyGarrisonDonation
        {
            // A Done that is refused before it reaches the handler must not replay an older donation.
            private static void Prefix()
            {
                ClearDonation();
            }

            private static void Postfix(bool __result)
            {
                if (__result && _donatedTo != null)
                {
                    foreach (KeyValuePair<CharacterObject, int> entry in _donated)
                    {
                        MoveAndLog(PartyBase.MainParty, _donatedTo, entry.Key, entry.Value);
                    }
                }
                ClearDonation();
            }
        }

        // ------------------------------------------------------------------ a clan party for a companion

        // The screen works on a clone of the main party's roster, so nothing real moves until it closes:
        // the closed handler raises the new party, adds the chosen men to it and takes them off the main
        // party in the same loop. Once it has returned both rosters are settled. Troop upgrades are
        // switched off on this screen, so there is no promotion to wait for.
        [HarmonyPatch(typeof(PartyScreenHelper))]
        [HarmonyPatch("OpenScreenAsCreateClanPartyForHeroPartyScreenClosed")]
        private class CarryToNewClanParty
        {
            private static void Postfix(TroopRoster leftMemberRoster, PartyBase rightOwnerParty, bool fromCancel)
            {
                CarryToNewParty(leftMemberRoster, rightOwnerParty, fromCancel);
            }
        }

        // The same screen opened from the conversation with a companion just freed from captivity, which
        // passes its own copy of the closed handler.
        [HarmonyPatch(typeof(CompanionRolesCampaignBehavior))]
        [HarmonyPatch("PartyScreenClosed")]
        private class CarryToRescuedCompanionParty
        {
            private static void Postfix(TroopRoster leftMemberRoster, PartyBase rightOwnerParty, bool fromCancel)
            {
                CarryToNewParty(leftMemberRoster, rightOwnerParty, fromCancel);
            }
        }

        private static void CarryToNewParty(TroopRoster leftMemberRoster, PartyBase from, bool fromCancel)
        {
            if (!SpoilsPool.IsEnabled || fromCancel || leftMemberRoster == null || from == null)
            {
                return;
            }
            // The handler keeps the new party to itself. The one hero on the left is the companion it was
            // raised for -- no other hero can be dragged across -- and by now he rides in it.
            PartyBase to = null;
            List<TroopRosterElement> moved = leftMemberRoster.GetTroopRoster();
            foreach (TroopRosterElement element in moved)
            {
                MobileParty party = element.Character?.HeroObject?.PartyBelongedTo;
                if (party != null && party.Party != from)
                {
                    to = party.Party;
                    break;
                }
            }
            if (to == null)
            {
                return;
            }
            foreach (TroopRosterElement element in moved)
            {
                if (element.Character != null && !element.Character.IsHero && element.Number > 0)
                {
                    MoveAndLog(from, to, element.Character, element.Number);
                }
            }
        }

        private static void MoveAndLog(PartyBase from, PartyBase to, CharacterObject character, int count)
        {
            int carried = SpoilsPool.TransferSpoils(from, to, character, count);
            if (carried > 0 && SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("XFER", from,
                    "transferred " + count + "x " + SpoilsLog.Describe(character)
                    + " from " + SpoilsLog.Describe(from) + " to " + SpoilsLog.Describe(to)
                    + "| carried " + carried + " spoils along");
            }
        }
    }
}
