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
        private static readonly Dictionary<CharacterObject, int> _stagedSpoils = new Dictionary<CharacterObject, int>();

        /// <summary>Spoils promised to upgrades the player has queued but not yet confirmed.</summary>
        public static int GetStagedSpoils(PartyBase party, CharacterObject character)
        {
            int staged;
            return (party == PartyBase.MainParty && _stagedSpoils.TryGetValue(character, out staged)) ? staged : 0;
        }

        /// <summary>Hands the reservation over on commit, so it is spent exactly once.</summary>
        public static int ConsumeStagedSpoils(PartyBase party, CharacterObject character)
        {
            if (party != PartyBase.MainParty)
            {
                return 0;
            }
            int staged;
            if (!_stagedSpoils.TryGetValue(character, out staged))
            {
                return 0;
            }
            _stagedSpoils.Remove(character);
            return staged;
        }

        // If a clear is ever missed the next screen open resets it, and until then upgrades are
        // quoted slightly high rather than the spoils pool being corrupted.
        private static void Clear()
        {
            _stagedSpoils.Clear();
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
