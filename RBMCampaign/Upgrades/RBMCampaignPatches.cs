using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    public static class RBMCampaignPatches
    {
        /// <summary>
        /// The upgrade price before any spoils discount: the equipment value the troop gains, run
        /// through the same perks and multiplier vanilla would apply. Everything that has to price a
        /// batch of upgrades rather than a single man starts here.
        /// </summary>
        public static ExplainedNumber BuildUpgradeGoldCost(PartyBase party, CharacterObject characterObject, CharacterObject upgradeTarget, float goldFactor)
        {
            int characterEquipmentCost = SpoilsPool.GetEquipmentValue(characterObject);
            int upgradeTargetEquipmentCost = SpoilsPool.GetEquipmentValue(upgradeTarget);

            bool isForHire = characterObject.Occupation == Occupation.Mercenary || characterObject.Occupation == Occupation.Gangster || characterObject.Occupation == Occupation.CaravanGuard;

            ExplainedNumber stat = new ExplainedNumber((upgradeTargetEquipmentCost - characterEquipmentCost) * goldFactor);
            // A stack-keyed purse can be priced for any party (the cap values every
            // stack), and a settlement-owned PartyBase has no MobileParty to read perks off. The perks
            // simply do not apply then; the base multiplier below still does.
            MobileParty mobileParty = party?.MobileParty;
            if (mobileParty != null)
            {
                if (mobileParty.HasPerk(DefaultPerks.Steward.SoundReserves))
                {
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.Steward.SoundReserves, mobileParty, isPrimaryBonus: true, ref stat);
                }
                if (characterObject.IsRanged && mobileParty.HasPerk(DefaultPerks.Bow.RenownedArcher, checkSecondaryRole: true))
                {
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.Bow.RenownedArcher, mobileParty, isPrimaryBonus: false, ref stat);
                }
                if (characterObject.IsMounted && PartyBaseHelper.HasFeat(party, DefaultCulturalFeats.KhuzaitRecruitUpgradeFeat))
                {
                    stat.AddFactor(DefaultCulturalFeats.KhuzaitRecruitUpgradeFeat.EffectBonus, GameTexts.FindText("str_culture"));
                }
                if (isForHire && mobileParty.HasPerk(DefaultPerks.Steward.Contractors))
                {
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.Steward.Contractors, mobileParty, isPrimaryBonus: true, ref stat);
                }
            }

            // ExplainedNumber resolves to base * (1 + sum of factors), so a 0.1x
            // multiplier has to be expressed as a -0.9 factor.
            stat.AddFactor(RBMConfig.RBMConfig.troopUpgradeCostMultiplier - 1f, new TextObject("{=RBM_CON_033}Realistic Battle Mod"));
            return stat;
        }

        /// <summary>
        /// What one man's upgrade costs when the stack holds no usable spoils at all. Floored at zero:
        /// a troop that upgrades into cheaper kit costs nothing in gold, and the surplus it strips off
        /// the retired gear is paid into its spoils purse instead (see GetSpoilsCreditForUpgrade).
        /// </summary>
        public static int GetFullUpgradeGoldCost(PartyBase party, CharacterObject characterObject, CharacterObject upgradeTarget)
        {
            return MathF.Max(0, BuildUpgradeGoldCost(party, characterObject, upgradeTarget, 1f).RoundedResultNumber);
        }

        /// <summary>
        /// The gold a batch of <paramref name="count"/> upgrades actually costs. Spoils are consumed one
        /// man at a time, so the men the stockpile covers go free and the rest pay full price. Vanilla
        /// only ever asks the model for a single per-man number and multiplies it, which is why the
        /// party screen and the AI both have to correct the total themselves.
        /// </summary>
        public static int GetBatchUpgradeGoldCost(PartyBase party, CharacterObject characterObject, CharacterObject upgradeTarget, int count)
        {
            float unpaidMen = SpoilsPool.GetUnpaidMen(party, characterObject, upgradeTarget, count);
            return MathF.Max(0, BuildUpgradeGoldCost(party, characterObject, upgradeTarget, unpaidMen).RoundedResultNumber);
        }

        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel))]
        [HarmonyPatch("GetGoldCostForUpgrade")]
        private class OverrideGetGoldCostForUpgrade
        {
            private static bool Prefix(PartyBase party, CharacterObject characterObject, CharacterObject upgradeTarget, ref ExplainedNumber __result)
            {
                // The price of the next man to be upgraded, not the stack's average: if the stockpile
                // covers him he is free, and the man after him may not be.
                float goldFactor = SpoilsPool.GetUnpaidMen(party, characterObject, upgradeTarget, 1);
                ExplainedNumber stat = BuildUpgradeGoldCost(party, characterObject, upgradeTarget, goldFactor);
                // A cheaper-kit upgrade prices out negative; the surplus is salvaged into the stack's
                // spoils on commit instead of handing the player gold, so the quoted cost floors at zero.
                if (stat.RoundedResultNumber < 0)
                {
                    stat = new ExplainedNumber(0f);
                }

                // The party screen recomputes this on every refresh, so once per troop pair is plenty.
                SpoilsLog.LogOnce("goldcost-" + characterObject.StringId + "-" + upgradeTarget.StringId, "GOLD", party,
                    SpoilsLog.Describe(characterObject) + " -> " + SpoilsLog.Describe(upgradeTarget)
                    + " | equip " + SpoilsPool.GetEquipmentValue(characterObject) + " -> " + SpoilsPool.GetEquipmentValue(upgradeTarget)
                    + ", spoils cost " + SpoilsPool.GetSpoilsCostForUpgrade(party, characterObject, upgradeTarget)
                    + ", stockpile " + SpoilsPool.GetAvailableSpoils(party, characterObject)
                    + ", next man pays " + goldFactor.ToString("0.00") + " of full"
                    + ", gold " + stat.RoundedResultNumber + " in " + SpoilsLog.Describe(party));

                __result = stat;
                return false;
            }
        }

        /// <summary>
        /// The party screen's upgrade tooltip quotes a single "Cost" line, which under spoils is a
        /// discounted price with no sign of where the discount came from. Break it into the three
        /// numbers the player actually wants: what the upgrade is worth, what the salvaged spoils
        /// pay for, and what is left for his purse.
        /// </summary>
        [HarmonyPatch(typeof(CampaignUIHelper))]
        [HarmonyPatch("GetUpgradeHint")]
        private class ExplainSpoilsDiscountInUpgradeHint
        {
            private const string CoinIcon = "<img src=\"General\\Icons\\Coin@2x\" extend=\"6\">";

            /// <summary>
            /// Vanilla prints upgradeCoinCost as its "Cost" line, so handing it the undiscounted price
            /// puts the full worth of the upgrade at the top where it belongs. The only other thing it
            /// does with either argument is test gold + partyGoldChangeAmount against the cost, so
            /// crediting the change amount with the covered gold leaves that verdict untouched.
            /// </summary>
            /// <param name="__state">The gold the stockpile covers, handed to the Postfix.</param>
            private static void Prefix(int index, ref int upgradeCoinCost, CharacterObject character, ref int partyGoldChangeAmount, bool areUpgradesDisabled, out int __state)
            {
                __state = 0;
                if (areUpgradesDisabled || !SpoilsPool.IsEnabled || character == null
                    || index < 0 || index >= character.UpgradeTargets.Length)
                {
                    return;
                }

                int fullCost = GetFullUpgradeGoldCost(PartyBase.MainParty, character, character.UpgradeTargets[index]);
                int coveredBySpoils = fullCost - upgradeCoinCost;
                if (coveredBySpoils <= 0)
                {
                    return;
                }

                __state = coveredBySpoils;
                upgradeCoinCost = fullCost;
                partyGoldChangeAmount += coveredBySpoils;
            }

            /// <summary>
            /// upgradeCoinCost arrives as the Prefix left it: the full price. When the upgrade instead
            /// wears cheaper kit, there is no discount to break out — the price is already zero — so the
            /// salvaged surplus is named on its own line, read straight from the original arguments.
            /// </summary>
            private static void Postfix(ref string __result, int index, CharacterObject character, bool areUpgradesDisabled, int upgradeCoinCost, int __state)
            {
                if (__result == null)
                {
                    return;
                }
                if (__state > 0)
                {
                    __result += "\n" + new TextObject("{=RBM_SPOILS_006}Spoils cover: {AMOUNT}")
                        .SetTextVariable("AMOUNT", __state).ToString() + CoinIcon;
                    __result += "\n" + new TextObject("{=RBM_SPOILS_007}You pay: {AMOUNT}")
                        .SetTextVariable("AMOUNT", upgradeCoinCost - __state).ToString() + CoinIcon;
                    return;
                }
                if (areUpgradesDisabled || !SpoilsPool.IsEnabled || character == null
                    || index < 0 || index >= character.UpgradeTargets.Length)
                {
                    return;
                }
                int salvaged = SpoilsPool.GetSpoilsCreditForUpgrade(character, character.UpgradeTargets[index]);
                if (salvaged > 0)
                {
                    __result += "\n" + new TextObject("{=RBM_SPOILS_005}Salvaged into spoils: {AMOUNT}")
                        .SetTextVariable("AMOUNT", salvaged).ToString() + CoinIcon;
                }
            }
        }

        /// <summary>
        /// SupplyTown gate: when the main party has no friendly town within reach, its upgrade arrows are
        /// refused (see PartyScreenStagedUpgrades.GateUpgradeOnSupplyTown), so the tooltip says why rather
        /// than leaving the arrow looking merely unresponsive. Delete this class to remove the note.
        /// </summary>
        [HarmonyPatch(typeof(CampaignUIHelper))]
        [HarmonyPatch("GetUpgradeHint")]
        private class NoteSupplyTownInUpgradeHint
        {
            private static void Postfix(ref string __result)
            {
                if (__result == null || UpgradeSupply.CanUpgradeNear(MobileParty.MainParty))
                {
                    return;
                }
                __result += "\n" + new TextObject("{=RBM_SPOILS_015}No friendly town nearby to supply this upgrade.").ToString();
            }
        }

        /// <summary>
        /// A shown hint is a snapshot: BasicTooltipViewModel.ExecuteBeginHint reads the text once on
        /// hover-begin and hands it to the tooltip layer, and nothing polls it after that. Under spoils
        /// every upgrade shifts the "Spoils cover / You pay" split, so a tooltip the player is still
        /// hovering keeps stale numbers until the cursor leaves the arrow and returns.
        ///
        /// The party screen's upgrade arrows never raise a focus event on mouse hover (only gamepad
        /// navigation sets PartyVM.CurrentFocusedUpgrade), so the only reliable way to know which arrow
        /// the cursor is over is to watch which tooltip the game last showed. These two patches record
        /// the tooltip that is currently on screen and forget it when it closes.
        /// </summary>
        private static BasicTooltipViewModel _shownHint;

        [HarmonyPatch(typeof(BasicTooltipViewModel))]
        [HarmonyPatch("ExecuteBeginHint")]
        private class TrackShownHintBegin
        {
            private static void Postfix(BasicTooltipViewModel __instance)
            {
                _shownHint = __instance;
            }
        }

        [HarmonyPatch(typeof(BasicTooltipViewModel))]
        [HarmonyPatch("ExecuteEndHint")]
        private class TrackShownHintEnd
        {
            private static void Postfix(BasicTooltipViewModel __instance)
            {
                if (_shownHint == __instance)
                {
                    _shownHint = null;
                }
            }
        }

        /// <summary>
        /// An upgrade re-runs InitializeUpgrades, which calls Refresh on every arrow of the stack and
        /// rebuilds each one's hint with the post-upgrade numbers. If the arrow being refreshed is the
        /// one whose tooltip is currently on screen, replay the player's own workaround — hide the stale
        /// tooltip and show it again — so the live numbers update without moving the cursor. Matching on
        /// the pre-refresh Hint (captured before Refresh swaps in the new one) is what identifies the
        /// hovered arrow; the last-man-upgraded path never refreshes that arrow (its VM is dropped) and
        /// the game hides its tooltip itself, so it is left alone.
        /// </summary>
        [HarmonyPatch(typeof(UpgradeTargetVM))]
        [HarmonyPatch("Refresh")]
        private class RefreshShownUpgradeHint
        {
            private static void Prefix(UpgradeTargetVM __instance, out bool __state)
            {
                __state = SpoilsPool.IsEnabled && _shownHint != null && __instance.Hint == _shownHint;
            }

            private static void Postfix(UpgradeTargetVM __instance, bool __state)
            {
                if (!__state)
                {
                    return;
                }
                MBInformationManager.HideInformations();
                __instance.Hint?.ExecuteBeginHint();
            }
        }
    }
}
