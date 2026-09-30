using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Reserves the spoils an open party screen has promised but not yet charged, and corrects the gold
    /// it staged. Vanilla asks the model for one per-man price and multiplies it by the batch size,
    /// which overcharges: spoils are consumed a man at a time, so the men the stockpile reaches go free
    /// and only the rest pay.
    /// </summary>
    public static class PartyScreenStagedUpgrades
    {
        // Total spoils reserved against a source troop, across all the targets it is being upgraded to this
        // visit. Drives GetAvailableSpoils, which must see every reservation the source has made so the
        // screen cannot spend the same spoils twice while it is open.
        private static readonly Dictionary<CharacterObject, int> _stagedSpoils = new Dictionary<CharacterObject, int>();

        // Spoils reserved for one (source -> target) pair, keyed source@target. A source can branch to more
        // than one target in a single visit (recruits to both infantry and archers), and each branch raises
        // its own PlayerUpgradedTroops event on commit, so the reservation has to be drawn down per target
        // rather than all at once, or the second branch would carry its purse share off the wrong pool.
        private static readonly Dictionary<string, int> _stagedByTarget = new Dictionary<string, int>();

        // Gold staged for one (source -> target) pair, keyed the same way. Kept because the commit has to
        // hand the supply town what the player was ACTUALLY charged, and by then it cannot be recomputed:
        // GetBatchUpgradeGoldCost prices against the stockpile as it stands, and the stockpile has moved.
        private static readonly Dictionary<string, int> _stagedGold = new Dictionary<string, int>();

        // Men of a source troop staged to upgrade this visit, summed over its targets. The commit fires
        // after every staged roster move is already applied, so the source stack has shrunk by all of them;
        // adding the still-pending count back is what recovers the size it stood at before a branch's men
        // left, which is the denominator the carried purse share is measured against.
        private static readonly Dictionary<CharacterObject, int> _stagedCount = new Dictionary<CharacterObject, int>();

        // Set once TrackStagedUpgrade has reserved a batch, and spent by the ValidateCommand vanilla's
        // UpgradeTroop opens with. That second check runs against the purse the reservation just drew
        // down, so TightenUpgradeAffordability would price the same batch a second time, as if nothing
        // covered it: a batch the spoils pay for outright was refused for gold it never needed, with its
        // reservation and gold correction already staged and nothing to take them back.
        private static bool _batchReserved;

        private static string TargetKey(CharacterObject from, CharacterObject to)
        {
            return from.StringId + "@" + to.StringId;
        }

        /// <summary>Spoils promised to upgrades the player has queued but not yet confirmed.</summary>
        public static int GetStagedSpoils(PartyBase party, CharacterObject character)
        {
            int staged;
            return (party == PartyBase.MainParty && _stagedSpoils.TryGetValue(character, out staged)) ? staged : 0;
        }

        /// <summary>
        /// Hands one target's reservation over on commit, so it is spent exactly once, and reports the size
        /// the source stack stood at before this branch's men left, for the carried-purse share. Draws the
        /// reserved spoils down both from the per-target pool and the source total.
        /// </summary>
        /// <param name="goldPaid">
        /// The gold the screen charged for this branch, so the commit can pay the supply town what the
        /// player actually handed over. Zero when the branch was never staged through this class.
        /// </param>
        public static int ConsumeStagedUpgrade(PartyBase party, CharacterObject from, CharacterObject to, int count,
            out int stackSizeBefore, out int goldPaid)
        {
            stackSizeBefore = SpoilsPool.GetStackSize(party, from) + count;
            goldPaid = 0;
            if (party != PartyBase.MainParty)
            {
                return 0;
            }
            if (_stagedGold.TryGetValue(TargetKey(from, to), out goldPaid))
            {
                _stagedGold.Remove(TargetKey(from, to));
            }

            // Recover the pre-commit stack size: the roster already lost every staged man of this source,
            // so add the still-pending count (which includes this branch) back. Fall back to this branch's
            // own count if the tally is missing, which reproduces the old single-branch reconstruction.
            int pending;
            _stagedCount.TryGetValue(from, out pending);
            if (pending < count)
            {
                pending = count;
            }
            // Men of this troop dragged to the other party are not counted back in: their share of the
            // purse is already set aside (SpoilsTransferOnPartyScreen.GetOutgoingSpoils), so what is
            // split here is split among the men who stayed.
            stackSizeBefore = SpoilsPool.GetStackSize(party, from) + pending;
            int remaining = pending - count;
            if (remaining > 0)
            {
                _stagedCount[from] = remaining;
            }
            else
            {
                _stagedCount.Remove(from);
            }

            int spend;
            if (!_stagedByTarget.TryGetValue(TargetKey(from, to), out spend))
            {
                return 0;
            }
            _stagedByTarget.Remove(TargetKey(from, to));
            // Keep the source total in step, so a later branch of the same source still reads a truthful
            // available-spoils figure while the screen is mid-commit.
            int total;
            if (_stagedSpoils.TryGetValue(from, out total))
            {
                total -= spend;
                if (total > 0)
                {
                    _stagedSpoils[from] = total;
                }
                else
                {
                    _stagedSpoils.Remove(from);
                }
            }
            return spend;
        }

        /// <summary>Men of this source troop staged to upgrade this visit and not yet committed.</summary>
        public static int GetStagedCount(PartyBase party, CharacterObject character)
        {
            int staged;
            return (party == PartyBase.MainParty && _stagedCount.TryGetValue(character, out staged)) ? staged : 0;
        }

        /// <summary>Whether any branch of this source troop still waits for its commit event.</summary>
        public static bool HasPendingUpgrade(PartyBase party, CharacterObject character)
        {
            return party == PartyBase.MainParty && _stagedCount.ContainsKey(character);
        }

        /// <summary>
        /// Tops a stack's purse back up to what the screen reserved against it, out of the same troop's
        /// purse in the other party. The reservation can only outrun the purse when it counted on spoils
        /// that men dragged in were bringing and those men were then sent back.
        /// </summary>
        public static void CoverShortfalls(PartyBase party, PartyBase other)
        {
            if (party != PartyBase.MainParty || other == null)
            {
                return;
            }
            foreach (KeyValuePair<CharacterObject, int> reserved in _stagedSpoils)
            {
                int shortfall = reserved.Value - SpoilsPool.GetSpoils(party, reserved.Key);
                int drawn = System.Math.Min(shortfall, SpoilsPool.GetSpoils(other, reserved.Key));
                if (drawn > 0)
                {
                    SpoilsPool.AddSpoils(other, reserved.Key, -drawn);
                    SpoilsPool.AddSpoils(party, reserved.Key, drawn);
                    SpoilsLog.Log("XFER", party, "drew " + drawn + " spoils back from " + SpoilsLog.Describe(other)
                        + " to meet upgrades of " + SpoilsLog.Describe(reserved.Key) + " quoted against them");
                }
            }
        }

        // If a clear is ever missed the next screen open resets it (ClearOnOpen), and until then upgrades
        // are quoted slightly high rather than the spoils pool being corrupted.
        private static void Clear()
        {
            _stagedSpoils.Clear();
            _stagedByTarget.Clear();
            _stagedGold.Clear();
            _stagedCount.Clear();
            _savedSpoils = null;
            _savedByTarget = null;
            _savedGold = null;
            _savedCount = null;
        }

        // The four tallies as they stood when the screen last saved its state, which it does on opening
        // the upgrade popup. Cancelling the popup rolls the screen back to that save, not to the start of
        // the visit, so the upgrades staged on the main screen beforehand are still pending and their
        // reservations have to come back with them.
        private static Dictionary<CharacterObject, int> _savedSpoils;
        private static Dictionary<string, int> _savedByTarget;
        private static Dictionary<string, int> _savedGold;
        private static Dictionary<CharacterObject, int> _savedCount;

        private static void Restore<TKey>(Dictionary<TKey, int> live, Dictionary<TKey, int> saved)
        {
            live.Clear();
            if (saved == null)
            {
                return;
            }
            foreach (KeyValuePair<TKey, int> entry in saved)
            {
                live[entry.Key] = entry.Value;
            }
        }

        /// <summary>
        /// Runs before vanilla rather than after it. UpgradeTroop ends by invoking UpdateDelegate,
        /// which is what drives PartyCharacterVM.InitializeUpgrades and so recomputes the quoted
        /// price and its tooltip. Reserving from a Postfix would leave that recomputation reading a
        /// stockpile the upgrade had already claimed, and the screen would quote the man who just
        /// left the roster.
        /// </summary>
        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("UpgradeTroop")]
        private class TrackStagedUpgrade
        {
            private static readonly MethodInfo SetPartyGoldChangeAmount =
                AccessTools.Method(typeof(PartyScreenLogic), "SetPartyGoldChangeAmount");

            private static void Prefix(PartyScreenLogic __instance, PartyScreenLogic.PartyCommand command)
            {
                // Vanilla bails on an invalid command without touching gold or roster, so the
                // reservation must not happen either. ValidateCommand is pure, so asking twice is free.
                if (!SpoilsPool.IsEnabled || !__instance.ValidateCommand(command))
                {
                    return;
                }
                PartyBase party = PartyBase.MainParty;
                CharacterObject character = command.Character;
                CharacterObject upgradeTarget = character.UpgradeTargets[command.UpgradeTarget];
                int count = command.TotalNumber;

                // Priced against the stockpile as it stands, before this batch draws on it.
                int spend = SpoilsPool.GetBatchSpoilsSpend(party, character, upgradeTarget, count);
                int actualGold = RBMCampaignPatches.GetBatchUpgradeGoldCost(party, character, upgradeTarget, count);

                int staged;
                _stagedSpoils.TryGetValue(character, out staged);
                _stagedSpoils[character] = staged + spend;

                // Reserve per target and tally the men, so the commit can draw each branch's reservation
                // down on its own event and recover the source's pre-commit size (see ConsumeStagedUpgrade).
                string targetKey = TargetKey(character, upgradeTarget);
                int stagedForTarget;
                _stagedByTarget.TryGetValue(targetKey, out stagedForTarget);
                _stagedByTarget[targetKey] = stagedForTarget + spend;
                int stagedGoldForTarget;
                _stagedGold.TryGetValue(targetKey, out stagedGoldForTarget);
                _stagedGold[targetKey] = stagedGoldForTarget + actualGold;
                int stagedMen;
                _stagedCount.TryGetValue(character, out stagedMen);
                _stagedCount[character] = stagedMen + count;

                // Vanilla is about to subtract perManPrice * count, and it will quote that per-man
                // price against the stockpile the reservation above just depleted. Mirror the read it
                // is going to make, then pre-credit the difference so its subtraction lands on
                // actualGold. Reading before the reservation would mirror a price vanilla never uses.
                int chargedByVanilla = character.GetUpgradeGoldCost(party, command.UpgradeTarget) * count;
                int correction = chargedByVanilla - actualGold;
                if (correction != 0 && SetPartyGoldChangeAmount != null)
                {
                    SetPartyGoldChangeAmount.Invoke(__instance, new object[] { __instance.CurrentData.PartyGoldChangeAmount + correction });
                }

                SpoilsLog.Log("UPGRADE", PartyBase.MainParty, "party screen staged " + count + "x " + SpoilsLog.Describe(character)
                    + " -> " + SpoilsLog.Describe(upgradeTarget)
                    + "| spoils reserved " + spend + " (total " + _stagedSpoils[character] + ")"
                    + ", gold " + actualGold + " (vanilla will charge " + chargedByVanilla + ")");

                _batchReserved = true;
            }

            // Vanilla's own ValidateCommand normally spends the flag; this only catches a throw before it.
            private static void Finalizer()
            {
                _batchReserved = false;
            }
        }

        // SupplyTown gate (player side): refuse the staged upgrade command when no friendly town is in
        // reach of the main party. Runs before TrackStagedUpgrade (Priority.First) so no spoils are
        // reserved for an upgrade that will not happen. Delete this class to remove the player-side gate;
        // the tooltip note in RBMCampaignPatches.NoteSupplyTownInUpgradeHint tells the player why.
        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("UpgradeTroop")]
        private class GateUpgradeOnSupplyTown
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                return UpgradeSupply.CanUpgradeNear(MobileParty.MainParty);
            }
        }

        // Vanilla judges an upgrade affordable by the next man's per-man price times the batch size. Under
        // spoils that per-man price is the discounted price of the next man -- often zero, when the
        // stockpile covers the leading men -- so a batch whose trailing, unpaid men do cost gold is passed
        // as free and its arrow never greys out. The commit still refuses it, but the preview lies. Only
        // ever tighten the verdict: when the true batch gold cost cannot be met, mark the command invalid.
        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("ValidateCommand")]
        private class TightenUpgradeAffordability
        {
            private static void Postfix(PartyScreenLogic __instance, PartyScreenLogic.PartyCommand command, ref bool __result)
            {
                if (!__result || !SpoilsPool.IsEnabled || command.Code != PartyScreenLogic.PartyCommandCode.UpgradeTroop)
                {
                    return;
                }
                // Already judged against the undrawn purse by TrackStagedUpgrade, before it reserved.
                if (_batchReserved)
                {
                    _batchReserved = false;
                    return;
                }
                CharacterObject character = command.Character;
                if (character == null || command.UpgradeTarget < 0 || command.UpgradeTarget >= character.UpgradeTargets.Length)
                {
                    return;
                }
                CharacterObject target = character.UpgradeTargets[command.UpgradeTarget];
                int trueCost = RBMCampaignPatches.GetBatchUpgradeGoldCost(PartyBase.MainParty, character, target, command.TotalNumber);
                if (trueCost <= 0)
                {
                    return;
                }
                // Read gold off the same leader vanilla charges for the command's side, plus the change the
                // screen has already staged, so this compares against exactly the pool the commit checks.
                CharacterObject leader = (command.RosterSide == PartyScreenLogic.PartyRosterSide.Left)
                    ? __instance.LeftPartyLeader
                    : __instance.RightPartyLeader;
                int gold = (leader != null && leader.HeroObject != null) ? leader.HeroObject.Gold : 0;
                if (gold + __instance.CurrentData.PartyGoldChangeAmount < trueCost)
                {
                    __result = false;
                }
            }
        }

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
            // Before vanilla, not after: Reset raises AfterReset itself, which rebuilds every troop row
            // and re-quotes its upgrades, and those quotes must already read the purse as unreserved.
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
                _savedSpoils = new Dictionary<CharacterObject, int>(_stagedSpoils);
                _savedByTarget = new Dictionary<string, int>(_stagedByTarget);
                _savedGold = new Dictionary<string, int>(_stagedGold);
                _savedCount = new Dictionary<CharacterObject, int>(_stagedCount);
            }
        }

        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("ResetToLastSavedPartyScreenData")]
        private class RestoreOnResetToLastSaved
        {
            // A Prefix for the same reason as ClearOnReset: the rows are re-quoted inside the original.
            private static void Prefix()
            {
                Restore(_stagedSpoils, _savedSpoils);
                Restore(_stagedByTarget, _savedByTarget);
                Restore(_stagedGold, _savedGold);
                Restore(_stagedCount, _savedCount);
            }
        }

        // Runs after DoneLogic has fired PlayerUpgradedTroopsEvent, so the spoils are already charged.
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
