using HarmonyLib;
using Helpers;
using psai.Editor;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Core;

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
                PartyWageModel partyWageModel = Campaign.Current.Models.PartyWageModel;
                //int roundedResultNumber = partyWageModel.GetTroopRecruitmentCost(upgradeTarget, null, withoutItemCost: true).RoundedResultNumber;
                //int roundedResultNumber2 = partyWageModel.GetTroopRecruitmentCost(characterObject, null, withoutItemCost: true).RoundedResultNumber;

                int characterEquipmentCost = 0;
                for (EquipmentIndex i = EquipmentIndex.ArmorItemBeginSlot; i < EquipmentIndex.ArmorItemEndSlot; i++)
                {
                    if (!characterObject.Equipment[i].IsEmpty)
                    {
                        characterEquipmentCost += characterObject.Equipment[i].ItemValue;
                    }
                }
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                {
                    if (!characterObject.Equipment[i].IsEmpty)
                    {
                        characterEquipmentCost += characterObject.Equipment[i].ItemValue;
                    }
                }

                int upgradeTargetEquipmentCost = 0;
                for (EquipmentIndex j = EquipmentIndex.ArmorItemBeginSlot; j < EquipmentIndex.ArmorItemEndSlot; j++)
                {
                    if (!upgradeTarget.Equipment[j].IsEmpty)
                    {
                        upgradeTargetEquipmentCost += upgradeTarget.Equipment[j].ItemValue;
                    }
                }
                for (EquipmentIndex j = EquipmentIndex.WeaponItemBeginSlot; j < EquipmentIndex.NumAllWeaponSlots; j++)
                {
                    if (!upgradeTarget.Equipment[j].IsEmpty)
                    {
                        upgradeTargetEquipmentCost += upgradeTarget.Equipment[j].ItemValue;
                    }
                }

                bool isForHire = characterObject.Occupation == Occupation.Mercenary || characterObject.Occupation == Occupation.Gangster || characterObject.Occupation == Occupation.CaravanGuard;

                ExplainedNumber stat = new ExplainedNumber((float)(upgradeTargetEquipmentCost - characterEquipmentCost));
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

                stat = new ExplainedNumber((float)((upgradeTargetEquipmentCost - characterEquipmentCost) * RBMConfig.RBMConfig.troopUpgradeCostMultiplier));

                __result = stat;
                return false;
            }
        }
    }
}