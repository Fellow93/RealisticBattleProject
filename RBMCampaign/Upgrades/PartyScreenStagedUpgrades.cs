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

        // Men of a source troop staged to upgrade this visit, summed over its targets. The commit fires
        // after every staged roster move is already applied, so the source stack has shrunk by all of them;
        // adding the still-pending count back is what recovers the size it stood at before a branch's men
        // left, which is the denominator the carried purse share is measured against.
        private static readonly Dictionary<CharacterObject, int> _stagedCount = new Dictionary<CharacterObject, int>();

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
        public static int ConsumeStagedUpgrade(PartyBase party, CharacterObject from, CharacterObject to, int count, out int stackSizeBefore)
        {
            stackSizeBefore = SpoilsPool.GetStackSize(party, from) + count;
            if (party != PartyBase.MainParty)
            {
                return 0;
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

        // If a clear is ever missed the next screen open resets it, and until then upgrades are
        // quoted slightly high rather than the spoils pool being corrupted.
        private static void Clear()
        {
            _stagedSpoils.Clear();
            _stagedByTarget.Clear();
            _stagedCount.Clear();
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
