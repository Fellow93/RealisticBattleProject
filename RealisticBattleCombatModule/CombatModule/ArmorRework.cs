using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RealisticBattleCombatModule
{
    class ArmorRework
    {
        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("GetBaseArmorEffectivenessForBodyPart")]
        class ChangeBodyPartArmor
        {
            static bool Prefix(Agent __instance, BoneBodyPartType bodyPart, ref float __result)
            {

                if (!__instance.IsHuman)
                {
                    switch (bodyPart)
                    {
                        case BoneBodyPartType.None:
                            {
                                __result = 10f;
                                break;
                            }
                        case BoneBodyPartType.Head:
                            {
                                __result = getHorseHeadArmor(__instance);
                                break;
                            }
                        case BoneBodyPartType.Neck:
                            {
                                __result = getHorseArmArmor(__instance);
                                break;
                            }
                        case BoneBodyPartType.Legs:
                        case BoneBodyPartType.ArmLeft:
                        case BoneBodyPartType.ArmRight:
                            {
                                __result = (getHorseLegArmor(__instance) * 2f + getHorseBodyArmor(__instance)) / 3f;
                                break;
                            }
                        case BoneBodyPartType.Chest:
                            {
                                __result = (getHorseLegArmor(__instance) + getHorseBodyArmor(__instance)) / 2f;
                                break;
                            }
                        case BoneBodyPartType.ShoulderLeft:
                        case BoneBodyPartType.ShoulderRight:
                            {
                                __result = getHorseBodyArmor(__instance);
                                break;
                            }
                        case BoneBodyPartType.Abdomen:
                            {
                                __result = getHorseLegArmor(__instance);
                                break;
                            }
                        default:
                            {
                                _ = 10;
                                __result = 10f;
                                break;
                            }
                    }
                }

                else
                {
                    switch (bodyPart)
                    {
                        case BoneBodyPartType.None:
                            {
                                __result = 0f;
                                break;
                            }
                        case BoneBodyPartType.Head:
                            {
                                __result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead);
                                break;
                            }
                        case BoneBodyPartType.Neck:
                            {
                                __result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead) * 0.66f;
                                break;
                            }
                        case BoneBodyPartType.Legs:
                            {
                                __result = getLegArmor(__instance);
                                break;
                            }
                        case BoneBodyPartType.ArmLeft:
                        case BoneBodyPartType.ArmRight:
                            {
                                __result = getArmArmor(__instance);
                                break;
                            }
                        case BoneBodyPartType.Chest:
                            {
                                __result = getMyChestArmor(__instance);
                                break;
                            }
                        case BoneBodyPartType.ShoulderLeft:
                        case BoneBodyPartType.ShoulderRight:
                            {
                                __result = getShoulderArmor(__instance);
                                break;
                            }
                        case BoneBodyPartType.Abdomen:
                            {
                                __result = getAbdomenArmor(__instance);
                                break;
                            }
                        default:
                            {
                                _ = 3;
                                __result = 3f;
                                break;
                            }
                    }
                }

                return false;
            }

            static public float getNeckArmor(Agent agent)
            {
                float num = 0f;
                for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
                {
                    EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                    if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
                    {
                        num += (float)equipmentElement.Item.ArmorComponent.BodyArmor;
                    }
                    if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                    {
                        num += (float)equipmentElement.Item.ArmorComponent.ArmArmor;
                    }
                }
                return num;
            }

            static public float getHorseHeadArmor(Agent agent)
            {
                float num = 0f;
                EquipmentElement equipmentElement = agent.SpawnEquipment[11];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
                {
                    num += (float)equipmentElement.Item.ArmorComponent.HeadArmor;
                    num += 10f;
                }
                return num;
            }

            static public float getHorseLegArmor(Agent agent)
            {
                float num = 0f;
                EquipmentElement equipmentElement = agent.SpawnEquipment[11];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
                {
                    num += (float)equipmentElement.Item.ArmorComponent.LegArmor;
                    num += 10f;
                }
                return num;
            }

            static public float getHorseArmArmor(Agent agent)
            {
                float num = 0f;
                EquipmentElement equipmentElement = agent.SpawnEquipment[11];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
                {
                    num += (float)equipmentElement.Item.ArmorComponent.ArmArmor;
                    num += 10f;
                }
                return num;
            }

            static public float getHorseBodyArmor(Agent agent)
            {
                float num = 0f;
                 EquipmentElement equipmentElement = agent.SpawnEquipment[11];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
                {
                    num += (float)equipmentElement.Item.ArmorComponent.BodyArmor;
                    num += 10f;
                }
                return num;
            }

            static public float getShoulderArmor(Agent agent)
            {
                float num = 0f;
                for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
                {
                    EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                    if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
                    {
                        num += (float)equipmentElement.Item.ArmorComponent.BodyArmor;
                        num += (float)equipmentElement.Item.ArmorComponent.ArmArmor;
                    }
                    if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                    {
                        num += (float)equipmentElement.Item.ArmorComponent.ArmArmor;
                    }
                }
                return num;
            }

            static public float getAbdomenArmor(Agent agent)
            {
                float num = 0f;
                for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
                {
                    EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                    if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                    {
                        num += (float)equipmentElement.Item.ArmorComponent.BodyArmor;
                    }
                }
                return num;
            }

            static public float getMyChestArmor(Agent agent)
            {
                float num = 0f;
                for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
                {
                    EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                    if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                    {
                        num += (float)equipmentElement.Item.ArmorComponent.BodyArmor;
                    }
                }
                return num;
            }

            static public float getArmArmor(Agent agent)
            {
                for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
                {
                    EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                    if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                    {
                        return (float)equipmentElement.Item.ArmorComponent.ArmArmor;
                    }
                }
                return 0f;
            }

            static public float getLegArmor(Agent agent)
            {
                float num = 0f;
                for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
                {
                    EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                    if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.LegArmor)
                    {
                        num += ((float)equipmentElement.Item.ArmorComponent.LegArmor) * 0.5f;
                    }
                    if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                    {
                        num += ((float)equipmentElement.Item.ArmorComponent.LegArmor) * 0.5f;
                    }
                }
                return num;
            }
        }
    }
}
