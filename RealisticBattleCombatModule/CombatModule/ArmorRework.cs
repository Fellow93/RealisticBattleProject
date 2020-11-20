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
                    __result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorTorso);
                    return false;
                }
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
                            __result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead) * 0.8f;
                            break;
                        }
                    case BoneBodyPartType.BipedalLegs:
                    case BoneBodyPartType.QuadrupedalLegs:
                        {
                            __result = getLegArmor(__instance);
                            break;
                        }
                    case BoneBodyPartType.BipedalArmLeft:
                    case BoneBodyPartType.BipedalArmRight:
                    case BoneBodyPartType.QuadrupedalArmLeft:
                    case BoneBodyPartType.QuadrupedalArmRight:
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
