using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
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
    }
}
