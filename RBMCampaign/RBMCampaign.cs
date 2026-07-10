using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    public static class RBMCampaignPatches
    {
        /// <summary>
        /// The upgrade price before any gear discount: the equipment value the troop gains, run
        /// through the same perks and multiplier vanilla would apply. Everything that has to price a
        /// batch of upgrades rather than a single man starts here.
        /// </summary>
        public static ExplainedNumber BuildUpgradeGoldCost(PartyBase party, CharacterObject characterObject, CharacterObject upgradeTarget, float goldFactor)
        {
            int characterEquipmentCost = GearPool.GetEquipmentValue(characterObject);
            int upgradeTargetEquipmentCost = GearPool.GetEquipmentValue(upgradeTarget);

            bool isForHire = characterObject.Occupation == Occupation.Mercenary || characterObject.Occupation == Occupation.Gangster || characterObject.Occupation == Occupation.CaravanGuard;

            ExplainedNumber stat = new ExplainedNumber((upgradeTargetEquipmentCost - characterEquipmentCost) * goldFactor);
            if (party.MobileParty.HasPerk(DefaultPerks.Steward.SoundReserves))
            {
                PerkHelper.AddPerkBonusForParty(DefaultPerks.Steward.SoundReserves, party.MobileParty, isPrimaryBonus: true, ref stat);
            }
            if (characterObject.IsRanged && party.MobileParty.HasPerk(DefaultPerks.Bow.RenownedArcher, checkSecondaryRole: true))
            {
                PerkHelper.AddPerkBonusForParty(DefaultPerks.Bow.RenownedArcher, party.MobileParty, isPrimaryBonus: false, ref stat);
            }
            if (characterObject.IsMounted && PartyBaseHelper.HasFeat(party, DefaultCulturalFeats.KhuzaitRecruitUpgradeFeat))
            {
                stat.AddFactor(DefaultCulturalFeats.KhuzaitRecruitUpgradeFeat.EffectBonus, GameTexts.FindText("str_culture"));
            }
            if (isForHire && party.MobileParty.HasPerk(DefaultPerks.Steward.Contractors))
            {
                PerkHelper.AddPerkBonusForParty(DefaultPerks.Steward.Contractors, party.MobileParty, isPrimaryBonus: true, ref stat);
            }

            // ExplainedNumber resolves to base * (1 + sum of factors), so a 0.1x
            // multiplier has to be expressed as a -0.9 factor.
            stat.AddFactor(RBMConfig.RBMConfig.troopUpgradeCostMultiplier - 1f, new TextObject("{=RBM_CON_033}Realistic Battle Mod"));
            return stat;
        }

        /// <summary>What one man's upgrade costs when the stack holds no usable gear at all.</summary>
        public static int GetFullUpgradeGoldCost(PartyBase party, CharacterObject characterObject, CharacterObject upgradeTarget)
        {
            return BuildUpgradeGoldCost(party, characterObject, upgradeTarget, 1f).RoundedResultNumber;
        }

        /// <summary>
        /// The gold a batch of <paramref name="count"/> upgrades actually costs. Gear is consumed one
        /// man at a time, so the men the stockpile covers go free and the rest pay full price. Vanilla
        /// only ever asks the model for a single per-man number and multiplies it, which is why the
        /// party screen and the AI both have to correct the total themselves.
        /// </summary>
        public static int GetBatchUpgradeGoldCost(PartyBase party, CharacterObject characterObject, CharacterObject upgradeTarget, int count)
        {
            float unpaidMen = GearPool.GetUnpaidMen(party, characterObject, upgradeTarget, count);
            return BuildUpgradeGoldCost(party, characterObject, upgradeTarget, unpaidMen).RoundedResultNumber;
        }

        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel))]
        [HarmonyPatch("GetGoldCostForUpgrade")]
        private class OverrideGetGoldCostForUpgrade
        {
            private static bool Prefix(PartyBase party, CharacterObject characterObject, CharacterObject upgradeTarget, ref ExplainedNumber __result)
            {
                // The price of the next man to be upgraded, not the stack's average: if the stockpile
                // covers him he is free, and the man after him may not be.
                float goldFactor = GearPool.GetUnpaidMen(party, characterObject, upgradeTarget, 1);
                ExplainedNumber stat = BuildUpgradeGoldCost(party, characterObject, upgradeTarget, goldFactor);

                // The party screen recomputes this on every refresh, so once per troop pair is plenty.
                GearLog.LogOnce("goldcost-" + characterObject.StringId + "-" + upgradeTarget.StringId, "GOLD",
                    GearLog.Describe(characterObject) + " -> " + GearLog.Describe(upgradeTarget)
                    + " | equip " + GearPool.GetEquipmentValue(characterObject) + " -> " + GearPool.GetEquipmentValue(upgradeTarget)
                    + ", gear cost " + GearPool.GetGearCostForUpgrade(characterObject, upgradeTarget)
                    + ", stockpile " + GearPool.GetAvailableGear(party, characterObject)
                    + ", next man pays " + goldFactor.ToString("0.00") + " of full"
                    + ", gold " + stat.RoundedResultNumber + " in " + GearLog.Describe(party));

                __result = stat;
                return false;
            }
        }

        /// <summary>
        /// The party screen's upgrade tooltip quotes a single "Cost" line, which under gear is the
        /// discounted price with no sign of where the discount came from. Break it into the three
        /// numbers the player actually wants: what the upgrade is worth, what the salvaged gear
        /// pays for, and what is left for his purse.
        /// </summary>
        [HarmonyPatch(typeof(CampaignUIHelper))]
        [HarmonyPatch("GetUpgradeHint")]
        private class ExplainGearDiscountInUpgradeHint
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
                if (areUpgradesDisabled || !GearPool.IsEnabled || character == null
                    || index < 0 || index >= character.UpgradeTargets.Length)
                {
                    return;
                }

                int fullCost = GetFullUpgradeGoldCost(PartyBase.MainParty, character, character.UpgradeTargets[index]);
                int coveredByGear = fullCost - upgradeCoinCost;
                if (coveredByGear <= 0)
                {
                    return;
                }

                __state = coveredByGear;
                upgradeCoinCost = fullCost;
                partyGoldChangeAmount += coveredByGear;
            }

            /// <summary>upgradeCoinCost arrives as the Prefix left it: the full price.</summary>
            private static void Postfix(ref string __result, int upgradeCoinCost, int __state)
            {
                if (__state <= 0 || __result == null)
                {
                    return;
                }
                __result += "\n" + new TextObject("{=RBM_GEAR_006}Salvaged gear covers: {AMOUNT}")
                    .SetTextVariable("AMOUNT", __state).ToString() + CoinIcon;
                __result += "\n" + new TextObject("{=RBM_GEAR_007}You pay: {AMOUNT}")
                    .SetTextVariable("AMOUNT", upgradeCoinCost - __state).ToString() + CoinIcon;
            }
        }
    }
}
