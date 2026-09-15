using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    public static partial class MagnitudeChanges
    {
        public static void getRBMArmorStatsStrings(Equipment equipment,
            out string combinedHeadString,
            out string combinedBodyString,
            out string combinedArmString,
            out string combinedLegString)
        {
            combinedHeadString = "";
            combinedBodyString = "";
            combinedArmString = "";
            combinedLegString = "";
            if (equipment != null)
            {
                float headArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Head);
                float neckArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Neck);
                float shoulderArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.ShoulderLeft);
                float armArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.ArmLeft);
                float chestArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Chest);
                float abdomenArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Abdomen);
                float legsArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Legs);

                combinedHeadString += String.Format("{0,-0}", new TextObject("{=EUzxzL9s}Head Armor: ").ToString()) + headArmor + "\n";
                if (!equipment[EquipmentIndex.Head].IsEmpty)
                {
                    float faceArmor = equipment[EquipmentIndex.Head].GetModifiedBodyArmor();

                    combinedHeadString += String.Format("{0,-0}", new TextObject("{=RBM_COM_023}Face Armor").ToString()) + ": " + faceArmor + "\n";
                }
                combinedHeadString += String.Format("{0,-0}", new TextObject("{=RBM_COM_024}Neck Armor").ToString()) + ": " + neckArmor;

                combinedBodyString += String.Format("{0,-0}", new TextObject("{=RBM_COM_025}Shoulder Armor").ToString()) + ": " + shoulderArmor + "\n";
                combinedBodyString += String.Format("{0,-0}", new TextObject("{=oiSW6MyB}Chest Armor").ToString()) + ": " + chestArmor + "\n";
                combinedBodyString += String.Format("{0,-0}", new TextObject("{=RBM_COM_026}Abdomen Armor").ToString()) + ": " + abdomenArmor;

                combinedArmString += String.Format("{0,-0}", new TextObject("{=kx7q8ybD}Arm Armor").ToString() + ": ") + armArmor + "\n";
                if (!equipment[EquipmentIndex.Body].IsEmpty)
                {
                    float underShoulderArmor = (equipment[EquipmentIndex.Body].GetModifiedArmArmor());
                    if (!equipment[EquipmentIndex.Cape].IsEmpty)
                    {
                        underShoulderArmor += equipment[EquipmentIndex.Cape].GetModifiedArmArmor();
                    }
                    combinedArmString += String.Format("{0,-0}", new TextObject("{=RBM_COM_027}Lower Shoulder Armor").ToString() + ": ") + underShoulderArmor;
                }

                combinedLegString += String.Format("{0,-0}", new TextObject("{=U8VHRdwF}Leg Armor: ").ToString()) + legsArmor;
            }
        }

        [HarmonyPatch(typeof(SPInventoryVM))]
        [HarmonyPatch("UpdateCharacterArmorValues")]
        private class UpdateCharacterArmorValuesPatch
        {
            private static void Postfix(ref SPInventoryVM __instance, CharacterObject ____currentCharacter)
            {
                if (____currentCharacter != null)
                {
                    currentSelectedChar = ____currentCharacter;
                    Equipment equipment = ____currentCharacter.Equipment;
                    getRBMArmorStatsStrings(equipment,
                       out string combinedHeadString,
                       out string combinedBodyString,
                       out string combinedArmString,
                       out string combinedLegString);

                    __instance.HeadArmorHint = new HintViewModel(new TextObject(combinedHeadString));
                    __instance.BodyArmorHint = new HintViewModel(new TextObject(combinedBodyString));
                    __instance.ArmArmorHint = new HintViewModel(new TextObject(combinedArmString));
                    __instance.LegArmorHint = new HintViewModel(new TextObject(combinedLegString));
                }
            }
        }

        [HarmonyPatch(typeof(SPInventoryVM))]
        [HarmonyPatch("RefreshValues")]
        private class RefreshValuesPatch
        {
            private static void Postfix(ref SPInventoryVM __instance, CharacterObject ____currentCharacter)
            {
                if (____currentCharacter != null)
                {
                    Equipment equipment = ____currentCharacter.Equipment;
                    getRBMArmorStatsStrings(equipment,
                       out string combinedHeadString,
                       out string combinedBodyString,
                       out string combinedArmString,
                       out string combinedLegString);
                    __instance.HeadArmorHint = new HintViewModel(new TextObject(combinedHeadString));
                    __instance.BodyArmorHint = new HintViewModel(new TextObject(combinedBodyString));
                    __instance.ArmArmorHint = new HintViewModel(new TextObject(combinedArmString));
                    __instance.LegArmorHint = new HintViewModel(new TextObject(combinedLegString));
                }
            }
        }

        [HarmonyPatch(typeof(EncyclopediaUnitPageVM))]
        [HarmonyPatch("OnEquipmentSetChange")]
        private class EncyclopediaUnitPageVOnEquipmentSetChangePatch
        {
            private static void Postfix(ref EncyclopediaUnitPageVM __instance, CharacterObject ____character)
            {
                if (__instance.EquipmentSetSelector != null)
                {
                    equipmentSetindex = __instance.EquipmentSetSelector.SelectedIndex;
                }
                currentSelectedChar = ____character;
            }
        }
    }
}
