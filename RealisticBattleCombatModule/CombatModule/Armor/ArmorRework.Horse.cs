using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    public partial class ArmorRework
    {
        public static ArmorMaterialTypes getHorseArmorMaterial(Agent agent)
        {
            ArmorMaterialTypes material = ArmorMaterialTypes.None;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.HorseHarness];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
            {
                if (equipmentElement.Item.ArmorComponent != null)
                {
                    return equipmentElement.Item.ArmorComponent.MaterialType;
                }
            }
            return material;
        }

        public static float getHorseHeadArmor(Agent agent)
        {
            float num = 0f;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.HorseHarness];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
            {
                num += (float)equipmentElement.GetModifiedHeadArmor();
                num += 10f;
            }
            return num;
        }

        public static float getHorseLegArmor(Agent agent)
        {
            float num = 0f;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.HorseHarness];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
            {
                num += (float)equipmentElement.GetModifiedLegArmor();
                num += 10f;
            }
            return num;
        }

        public static float getHorseArmArmor(Agent agent)
        {
            float num = 0f;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.HorseHarness];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
            {
                num += (float)equipmentElement.GetModifiedArmArmor();
                num += 10f;
            }
            return num;
        }

        public static float getHorseBodyArmor(Agent agent)
        {
            float num = 0f;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.HorseHarness];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness && equipmentElement.Item.ArmorComponent != null)
            {
                num += equipmentElement.Item.ArmorComponent.BodyArmor;
                if (num > 0 && equipmentElement.ItemModifier != null)
                {
                    num = equipmentElement.ItemModifier.ModifyArmor((int)num);
                }
                num += 10f;
            }
            return num;
        }

        public static float getHorseHeadArmor(Equipment equipment)
        {
            float num = 0f;
            EquipmentElement equipmentElement = equipment[EquipmentIndex.HorseHarness];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
            {
                num += (float)equipmentElement.GetModifiedHeadArmor();
                num += 10f;
            }
            return num;
        }

        public static float getHorseLegArmor(Equipment equipment)
        {
            float num = 0f;
            EquipmentElement equipmentElement = equipment[EquipmentIndex.HorseHarness];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
            {
                num += (float)equipmentElement.GetModifiedLegArmor();
                num += 10f;
            }
            return num;
        }

        public static float getHorseArmArmor(Equipment equipment)
        {
            float num = 0f;
            EquipmentElement equipmentElement = equipment[EquipmentIndex.HorseHarness];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
            {
                num += (float)equipmentElement.GetModifiedArmArmor();
                num += 10f;
            }
            return num;
        }

        public static float getHorseBodyArmor(Equipment equipment)
        {
            float num = 0f;
            EquipmentElement equipmentElement = equipment[EquipmentIndex.HorseHarness];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness && equipmentElement.Item.ArmorComponent != null)
            {
                num += equipmentElement.Item.ArmorComponent.BodyArmor;
                if (num > 0 && equipmentElement.ItemModifier != null)
                {
                    num = equipmentElement.ItemModifier.ModifyArmor((int)num);
                }
                //num += (float)equipmentElement.GetModifiedBodyArmor();
                num += 10f;
            }
            return num;
        }
    }
}
