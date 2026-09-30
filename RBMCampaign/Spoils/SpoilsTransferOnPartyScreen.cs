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

        // The tally as it stood when the screen last saved its state, which it does on opening the upgrade
        // popup. Cancelling the popup rolls back to that save, so moves made before it are still pending.
        private static Dictionary<CharacterObject, int> _savedNet;

        // The visit's two owner parties, noted off the first drag so the upgrade quote can read the purse
        // incoming men will bring without the screen in hand. A screen with no owner on the left (a
        // donation, a new clan party) leaves _left null, and nothing is quoted or moved for it.
        private static PartyBase _left;
        private static PartyBase _right;

        private static void Clear()
        {
            _stagedNet.Clear();
            _savedNet = null;
            _left = null;
            _right = null;
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
                _left = __instance.LeftOwnerParty;
                _right = __instance.RightOwnerParty;
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
        }

        /// <summary>
        /// Purses cross before the upgrade events rather than after them. Men brought in and promoted in
        /// the same visit were quoted against the purse they carry (see <see cref="GetIncomingSpoils"/>),
        /// so it has to be in place when their upgrade event draws its reservation and splits what is left
        /// between the old troop name and the new. Men sent out were held a share the stayers' upgrades
        /// were quoted clear of (see <see cref="GetOutgoingSpoils"/>), and it has to leave before those
        /// events split the purse among the men who remain. FireCampaignRelatedEvents runs inside
        /// DoneLogic once the commit has succeeded, the rosters having moved as the player dragged.
        /// </summary>
        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("FireCampaignRelatedEvents")]
        private class ApplyIncomingBeforeUpgradeEvents
        {
            private static void Prefix(PartyScreenLogic __instance)
            {
                PartyBase right = __instance.RightOwnerParty;
                PartyBase left = __instance.LeftOwnerParty;
                if (right == null || left == null)
                {
                    return;
                }
                // Every crossing is settled here, so nothing is left for DoneLogic's Postfix on this screen.
                // The leavers go with exactly the share the quote held back for them (GetOutgoingSpoils),
                // read while the reservations still stand, which is why it never reaches reserved coin.
                foreach (KeyValuePair<CharacterObject, int> entry in _stagedNet)
                {
                    if (entry.Value < 0)
                    {
                        MoveAndLog(left, right, entry.Key, -entry.Value);
                    }
                    else if (entry.Value > 0)
                    {
                        int carried = SpoilsPool.TransferSpoilsAmount(right, left, entry.Key, GetOutgoingSpoils(right, entry.Key));
                        if (carried > 0 && SpoilsLog.IsEnabled)
                        {
                            SpoilsLog.Log("XFER", right == PartyBase.MainParty ? right : left,
                                "transferred " + entry.Value + "x " + SpoilsLog.Describe(entry.Key)
                                + " from " + SpoilsLog.Describe(right) + " to " + SpoilsLog.Describe(left)
                                + "| carried " + carried + " spoils along");
                        }
                    }
                }
                _stagedNet.Clear();
                // Men sent back after others of their stack were promoted on the purse they brought take
                // their share home with them, leaving the reservation short. The coin it counted on is in
                // the purse they returned to, so draw the difference from there rather than let the
                // promotion go part-paid.
                PartyScreenStagedUpgrades.CoverShortfalls(right, left);
            }
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

        /// <summary>
        /// The purse share that men dragged into <paramref name="party"/> this visit will bring with them
        /// when Done commits, so the screen can quote their upgrades against it. The same sum
        /// <see cref="SpoilsPool.TransferSpoils"/> moves on commit: the rosters shift as the player drags,
        /// so the stack they left is already short of them here as it will be there.
        /// </summary>
        public static int GetIncomingSpoils(PartyBase party, CharacterObject character)
        {
            int net;
            if (_left == null || party == null || party != _right || !_stagedNet.TryGetValue(character, out net) || net >= 0)
            {
                return 0;
            }
            return SpoilsPool.GetCarriedSpoils(SpoilsPool.GetSpoils(_left, character), -net,
                SpoilsPool.GetStackSize(_left, character) - net);
        }

        /// <summary>
        /// Men of a troop dragged OUT of <paramref name="party"/> this visit and not yet committed. They
        /// are already off its roster, so a purse share measured against the stack has to count them back in.
        /// </summary>
        public static int GetOutgoingCount(PartyBase party, CharacterObject character)
        {
            int net;
            return (party != null && party == _right && _stagedNet.TryGetValue(character, out net) && net > 0) ? net : 0;
        }

        /// <summary>
        /// The purse share that men dragged OUT of <paramref name="party"/> this visit will take with them,
        /// so the screen stops quoting the upgrades of the men who stay against coin that is leaving. Their
        /// share is measured against the stack as it stood with them and every man since promoted still in
        /// it. Upgrades staged before the drag may already have reserved into it; the leavers then take
        /// only what is not spoken for, since a reservation cannot be re-priced once it is made.
        /// </summary>
        public static int GetOutgoingSpoils(PartyBase party, CharacterObject character)
        {
            int leaving = GetOutgoingCount(party, character);
            if (leaving == 0)
            {
                return 0;
            }
            int purse = SpoilsPool.GetSpoils(party, character);
            int stackBefore = SpoilsPool.GetStackSize(party, character)
                + PartyScreenStagedUpgrades.GetStagedCount(party, character) + leaving;
            int share = SpoilsPool.GetCarriedSpoils(purse, leaving, stackBefore);
            int unreserved = purse - PartyScreenStagedUpgrades.GetStagedSpoils(party, character);
            return unreserved <= 0 ? 0 : (share < unreserved ? share : unreserved);
        }

        // A visit that reverts its pending moves -- the reset button, or a cancel -- must forget the tally
        // too, or the next Done would move purses for troops that never crossed. Mirrors the reset points
        // the staged-upgrade tracker already clears on.
        // Backstop: a visit that somehow ended without reaching a clear must not leak into the next.
        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("Initialize")]
        private class ClearOnOpen
        {
            private static void Prefix()
            {
                Clear();
            }
        }

        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("Reset")]
        private class ClearOnReset
        {
            // Before vanilla: Reset re-quotes every troop row itself, and the quote reads this tally.
            private static void Prefix()
            {
                Clear();
            }
        }

        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("SavePartyScreenData")]
        private class SnapshotOnSave
        {
            private static void Postfix()
            {
                _savedNet = new Dictionary<CharacterObject, int>(_stagedNet);
            }
        }

        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("ResetToLastSavedPartyScreenData")]
        private class RestoreOnResetToLastSaved
        {
            // A Prefix for the same reason as ClearOnReset.
            private static void Prefix()
            {
                _stagedNet.Clear();
                if (_savedNet == null)
                {
                    return;
                }
                foreach (KeyValuePair<CharacterObject, int> entry in _savedNet)
                {
                    _stagedNet[entry.Key] = entry.Value;
                }
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
