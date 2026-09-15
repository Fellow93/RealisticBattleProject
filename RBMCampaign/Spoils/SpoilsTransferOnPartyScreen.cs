using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Follows a stack's purse when the player marches its men across the party screen -- into a garrison,
    /// off to a companion's party, or back again. The screen moves the real rosters as the player drags,
    /// but only commits on Done and rolls everything back on cancel, so a purse cannot move with each drag
    /// or a cancelled visit would strand it. Instead the net of what crossed is tallied as it goes, and the
    /// purses are moved once, when Done commits. The mirror across parties of what an upgrade does across
    /// troop names on the same screen.
    /// </summary>
    public static class SpoilsTransferOnPartyScreen
    {
        // Net men of each troop moved from the right party to the left this visit; a negative is a move
        // the other way. The two owner parties do not change within a visit, so only the character and
        // the running count are tracked here -- the parties are read back off the screen when Done fires.
        private static readonly Dictionary<CharacterObject, int> _stagedNet = new Dictionary<CharacterObject, int>();

        private static void Clear()
        {
            _stagedNet.Clear();
        }

        // Each drag moves troops between the screen's two rosters. Tally the net crossing so Done can move
        // the matching share of the purse. Runs before vanilla, so the command still describes a move the
        // roster can make.
        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("TransferTroop")]
        private class TrackTransfer
        {
            private static void Prefix(PartyScreenLogic __instance, PartyScreenLogic.PartyCommand command)
            {
                if (!SpoilsPool.IsEnabled || command.Type != PartyScreenLogic.TroopType.Member)
                {
                    return;
                }
                CharacterObject character = command.Character;
                if (character == null || character.IsHero || !__instance.ValidateCommand(command))
                {
                    return;
                }
                // RosterSide is the side the men are leaving. A right-side departure is a right-to-left move.
                int signed = (command.RosterSide == PartyScreenLogic.PartyRosterSide.Right)
                    ? command.TotalNumber
                    : -command.TotalNumber;
                int net;
                _stagedNet.TryGetValue(character, out net);
                net += signed;
                if (net == 0)
                {
                    _stagedNet.Remove(character);
                }
                else
                {
                    _stagedNet[character] = net;
                }
            }
        }

        // By the time Done has returned true the roster moves are committed to the real parties, so each
        // source stack now holds only the men who stayed and the share the leavers carry is measured back
        // to the size the stack had before they left.
        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("DoneLogic")]
        private class ApplyOnDone
        {
            private static void Postfix(PartyScreenLogic __instance, bool __result)
            {
                if (!__result)
                {
                    return;
                }
                PartyBase right = __instance.RightOwnerParty;
                PartyBase left = __instance.LeftOwnerParty;
                if (_stagedNet.Count > 0 && right != null && left != null)
                {
                    foreach (KeyValuePair<CharacterObject, int> entry in _stagedNet)
                    {
                        if (entry.Value > 0)
                        {
                            MoveAndLog(right, left, entry.Key, entry.Value);
                        }
                        else if (entry.Value < 0)
                        {
                            MoveAndLog(left, right, entry.Key, -entry.Value);
                        }
                    }
                }
                Clear();
            }

            private static void MoveAndLog(PartyBase from, PartyBase to, CharacterObject character, int count)
            {
                int carried = SpoilsPool.TransferSpoils(from, to, character, count);
                if (carried > 0 && SpoilsLog.IsEnabled)
                {
                    SpoilsLog.Log("XFER", from == PartyBase.MainParty ? from : to,
                        "transferred " + count + "x " + SpoilsLog.Describe(character)
                        + " from " + SpoilsLog.Describe(from) + " to " + SpoilsLog.Describe(to)
                        + "| carried " + carried + " spoils along");
                }
            }
        }

        // A visit that reverts its pending moves -- the reset button, or a cancel -- must forget the tally
        // too, or the next Done would move purses for troops that never crossed. Mirrors the reset points
        // the staged-upgrade tracker already clears on.
        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("Reset")]
        private class ClearOnReset
        {
            private static void Postfix()
            {
                Clear();
            }
        }

        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("ResetToLastSavedPartyScreenData")]
        private class ClearOnResetToLastSaved
        {
            private static void Postfix()
            {
                Clear();
            }
        }

        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("OnPartyScreenClosed")]
        private class ClearOnClose
        {
            private static void Postfix()
            {
                Clear();
            }
        }
    }
}
