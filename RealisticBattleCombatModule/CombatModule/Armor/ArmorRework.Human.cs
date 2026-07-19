using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    public partial class ArmorRework
    {
        public static float getHeadArmor(Agent agent)
        {
            float num = 0f;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.Head];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
            {
                num += (float)equipmentElement.GetModifiedHeadArmor();
            }
            return num;
        }

        public static ArmorMaterialTypes getHeadArmorMaterial(Agent agent)
        {
            ArmorMaterialTypes material = ArmorMaterialTypes.None;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.Head];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
            {
                if (equipmentElement.Item.ArmorComponent != null)
                {
                    return equipmentElement.Item.ArmorComponent.MaterialType;
                }
            }
            return material;
        }

        public static float getNeckArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];

                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
                {
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
            }
            return num;
        }

        public static ArmorMaterialTypes getNeckArmorMaterial(Agent agent)
        {
            ArmorMaterialTypes material = ArmorMaterialTypes.None;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.Body];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
            {
                if (equipmentElement.Item.ArmorComponent != null)
                {
                    return equipmentElement.Item.ArmorComponent.MaterialType;
                }
            }
            return material;
        }

        public static float getShoulderArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];

                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
                {
                    num += (float)equipmentElement.GetModifiedBodyArmor();
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
            }
            return num;
        }

        public static ArmorMaterialTypes getShoulderArmorMaterial(Agent agent)
        {
            ArmorMaterialTypes material = ArmorMaterialTypes.None;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];

                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
                {
                    if (equipmentElement.Item.ArmorComponent != null)
                    {
                        return equipmentElement.Item.ArmorComponent.MaterialType;
                    }
                }
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    if (equipmentElement.Item.ArmorComponent != null)
                    {
                        if (equipmentElement.Item.ArmorComponent.MaterialType == ArmorMaterialTypes.Plate)
                        {
                            if (equipmentElement.GetModifiedItemName().Contains("mail") || equipmentElement.GetModifiedItemName().Contains("Mail"))
                            {
                                return ArmorMaterialTypes.Chainmail;
                            }
                        }
                        return equipmentElement.Item.ArmorComponent.MaterialType;
                    }
                }
            }
            return material;
        }

        public static float getAbdomenArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.GetModifiedBodyArmor();
                }
            }
            return num;
        }

        public static ArmorMaterialTypes getAbdomenArmorMaterial(Agent agent)
        {
            ArmorMaterialTypes material = ArmorMaterialTypes.None;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    if (equipmentElement.Item.ArmorComponent != null)
                    {
                        return equipmentElement.Item.ArmorComponent.MaterialType;
                    }
                }
            }
            return material;
        }

        public static float getChestArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.GetModifiedBodyArmor();
                }
            }
            return num;
        }
        public static ArmorMaterialTypes getChestArmorMaterial(Agent agent)
        {
            ArmorMaterialTypes material = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    if (equipmentElement.Item.ArmorComponent != null)
                    {
                        return equipmentElement.Item.ArmorComponent.MaterialType;
                    }
                }
            }
            return material;
        }

        public static float getArmArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                {
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
            }
            return num;
        }

        public static ArmorMaterialTypes getArmArmorMaterial(Agent agent)
        {
            ArmorMaterialTypes material = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                {
                    if (equipmentElement.Item.ArmorComponent != null)
                    {
                        return equipmentElement.Item.ArmorComponent.MaterialType;
                    }
                }
            }
            return material;
        }

        public static float getGauntletWeight(Agent agent)
        {
            float weight = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                {
                    if (equipmentElement.Item.ArmorComponent != null)
                    {
                        return equipmentElement.Item.Weight / 2f;
                    }
                }
            }
            return weight;
        }

        public static float getLegArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.LegArmor)
                {
                    num += ((float)equipmentElement.GetModifiedLegArmor()) * 0.5f;
                }
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += ((float)equipmentElement.GetModifiedLegArmor()) * 0.5f;
                }
            }
            return num;
        }

        public static ArmorMaterialTypes getLegArmorMaterial(Agent agent)
        {
            ArmorMaterialTypes material = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.LegArmor)
                {
                    if (equipmentElement.Item.ArmorComponent != null)
                    {
                        return equipmentElement.Item.ArmorComponent.MaterialType;
                    }
                }
            }
            return material;
        }

        public static float getHeadArmor(Equipment equipment)
        {
            float num = 0f;
            EquipmentElement equipmentElement = equipment[EquipmentIndex.Head];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
            {
                num += (float)equipmentElement.GetModifiedHeadArmor();
            }
            return num;
        }

        public static float getNeckArmor(Equipment equipment)
        {
            float num = 0f;
            EquipmentElement equipmentElement = equipment[EquipmentIndex.Body];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
            {
                num += (float)equipmentElement.GetModifiedArmArmor();
            }
            return num;
        }

        public static float getShoulderArmor(Equipment equipment)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = equipment[equipmentIndex];

                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
                {
                    num += (float)equipmentElement.GetModifiedBodyArmor();
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
            }
            return num;
        }

        public static float getAbdomenArmor(Equipment equipment)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = equipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.GetModifiedBodyArmor();
                }
            }
            return num;
        }

        public static float getChestArmor(Equipment equipment)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = equipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.GetModifiedBodyArmor();
                }
            }
            return num;
        }

        public static float getArmArmor(Equipment equipment)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = equipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                {
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
            }
            return num;
        }

        public static float getLegArmor(Equipment equipment)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = equipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.LegArmor)
                {
                    num += ((float)equipmentElement.GetModifiedLegArmor()) * 0.5f;
                }
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += ((float)equipmentElement.GetModifiedLegArmor()) * 0.5f;
                }
            }
            return num;
        }

        public static float GetBaseArmorEffectivenessForBodyPartRBMHuman(Equipment equipment, BoneBodyPartType bodyPart)
        {
            float result;
            switch (bodyPart)
            {
                case BoneBodyPartType.None:
                    {
                        result = 0f;
                        break;
                    }
                case BoneBodyPartType.Head:
                    {
                        result = getHeadArmor(equipment);
                        break;
                    }
                case BoneBodyPartType.Neck:
                    {
                        result = getNeckArmor(equipment);
                        break;
                    }
                case BoneBodyPartType.Legs:
                    {
                        result = getLegArmor(equipment);
                        break;
                    }
                case BoneBodyPartType.ArmLeft:
                case BoneBodyPartType.ArmRight:
                    {
                        result = getArmArmor(equipment);
                        break;
                    }
                case BoneBodyPartType.Chest:
                    {
                        result = getChestArmor(equipment);
                        break;
                    }
                case BoneBodyPartType.ShoulderLeft:
                case BoneBodyPartType.ShoulderRight:
                    {
                        result = getShoulderArmor(equipment);
                        break;
                    }
                case BoneBodyPartType.Abdomen:
                    {
                        result = getAbdomenArmor(equipment);
                        break;
                    }
                default:
                    {
                        _ = 3;
                        result = 3f;
                        break;
                    }
            }
            return result;
        }
    }
}
