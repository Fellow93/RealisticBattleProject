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
        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel))]
        [HarmonyPatch("GetGoldCostForUpgrade")]
        private class OverrideGetGoldCostForUpgrade
        {
            private static bool Prefix(PartyBase party, CharacterObject characterObject, CharacterObject upgradeTarget, ref ExplainedNumber __result)
            {
                int characterEquipmentCost = GearPool.GetEquipmentValue(characterObject);
                int upgradeTargetEquipmentCost = GearPool.GetEquipmentValue(upgradeTarget);

                bool isForHire = characterObject.Occupation == Occupation.Mercenary || characterObject.Occupation == Occupation.Gangster || characterObject.Occupation == Occupation.CaravanGuard;

                // Gold buys the gear the soldier is missing. Whatever share of the upgrade's gear
                // requirement he already carries, he does not have to pay for.
                float goldCoverage = GearPool.GetGoldCoverage(party, characterObject, upgradeTarget);
                float equipmentCostDelta = (upgradeTargetEquipmentCost - characterEquipmentCost) * (1f - goldCoverage);

                ExplainedNumber stat = new ExplainedNumber(equipmentCostDelta);
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

                __result = stat;
                return false;
            }
        }
    }
}