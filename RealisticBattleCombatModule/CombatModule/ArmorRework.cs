using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    public class ArmorRework
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
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
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
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
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

        public static float GetBaseArmorEffectivenessForBodyPartRBM(Agent agent, BoneBodyPartType bodyPart)
        {
            float result;
            if (!agent.IsHuman)
            {
                switch (bodyPart)
                {
                    case BoneBodyPartType.None:
                        {
                            result = 10f;
                            break;
                        }
                    case BoneBodyPartType.Head:
                        {
                            result = getHorseHeadArmor(agent);
                            break;
                        }
                    case BoneBodyPartType.Neck:
                        {
                            result = getHorseArmArmor(agent);
                            break;
                        }
                    case BoneBodyPartType.Legs:
                    case BoneBodyPartType.ArmLeft:
                    case BoneBodyPartType.ArmRight:
                        {
                            result = (getHorseLegArmor(agent) * 2f + getHorseBodyArmor(agent)) / 3f;
                            break;
                        }
                    case BoneBodyPartType.Chest:
                        {
                            result = (getHorseLegArmor(agent) + getHorseBodyArmor(agent)) / 2f;
                            break;
                        }
                    case BoneBodyPartType.ShoulderLeft:
                    case BoneBodyPartType.ShoulderRight:
                        {
                            result = getHorseBodyArmor(agent);
                            break;
                        }
                    case BoneBodyPartType.Abdomen:
                        {
                            result = getHorseLegArmor(agent);
                            break;
                        }
                    default:
                        {
                            _ = 10;
                            result = 10f;
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
                            result = 0f;
                            break;
                        }
                    case BoneBodyPartType.Head:
                        {
                            result = getHeadArmor(agent);
                            break;
                        }
                    case BoneBodyPartType.Neck:
                        {
                            result = getNeckArmor(agent);
                            break;
                        }
                    case BoneBodyPartType.Legs:
                        {
                            result = getLegArmor(agent);
                            break;
                        }
                    case BoneBodyPartType.ArmLeft:
                    case BoneBodyPartType.ArmRight:
                        {
                            result = getArmArmor(agent);
                            break;
                        }
                    case BoneBodyPartType.Chest:
                        {
                            result = getChestArmor(agent);
                            break;
                        }
                    case BoneBodyPartType.ShoulderLeft:
                    case BoneBodyPartType.ShoulderRight:
                        {
                            result = getShoulderArmor(agent);
                            break;
                        }
                    case BoneBodyPartType.Abdomen:
                        {
                            result = getAbdomenArmor(agent);
                            break;
                        }
                    default:
                        {
                            _ = 3;
                            result = 3f;
                            break;
                        }
                }
            }
            return result;
        }

        public static ArmorMaterialTypes GetArmorMaterialForBodyPartRBM(Agent agent, BoneBodyPartType bodyPart)
        {
            ArmorMaterialTypes result = ArmorMaterialTypes.None;
            if (agent != null)
            {
                if (!agent.IsHuman)
                {
                    result = getHorseArmorMaterial(agent);
                }
                else
                {
                    switch (bodyPart)
                    {
                        case BoneBodyPartType.None:
                            {
                                result = ArmorMaterialTypes.None;
                                break;
                            }
                        case BoneBodyPartType.Head:
                            {
                                result = getHeadArmorMaterial(agent);
                                break;
                            }
                        case BoneBodyPartType.Neck:
                            {
                                result = getNeckArmorMaterial(agent);
                                break;
                            }
                        case BoneBodyPartType.Legs:
                            {
                                result = getLegArmorMaterial(agent);
                                break;
                            }
                        case BoneBodyPartType.ArmLeft:
                        case BoneBodyPartType.ArmRight:
                            {
                                result = getArmArmorMaterial(agent);
                                break;
                            }
                        case BoneBodyPartType.Chest:
                            {
                                result = getChestArmorMaterial(agent);
                                break;
                            }
                        case BoneBodyPartType.ShoulderLeft:
                        case BoneBodyPartType.ShoulderRight:
                            {
                                result = getShoulderArmorMaterial(agent);
                                break;
                            }
                        case BoneBodyPartType.Abdomen:
                            {
                                result = getAbdomenArmorMaterial(agent);
                                break;
                            }
                        default:
                            {
                                _ = ArmorMaterialTypes.None;
                                result = ArmorMaterialTypes.None;
                                break;
                            }
                    }
                }
            }
            return result;
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

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("GetBaseArmorEffectivenessForBodyPart")]
        public class ChangeBodyPartArmor
        {
            public static bool Prefix(Agent __instance, BoneBodyPartType bodyPart, ref float __result)
            {
                __result = GetBaseArmorEffectivenessForBodyPartRBM(__instance, bodyPart);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(ItemModifier))]
    [HarmonyPatch("ModifyArmor")]
    internal class ModifyArmorPatch
    {
        private static int ModifyFactor(int baseValue, float factor)
        {
            if (baseValue == 0)
            {
                return 0;
            }
            if (!MBMath.ApproximatelyEquals(factor, 0f))
            {
                baseValue = ((factor < 1f) ? MathF.Ceiling(factor * (float)baseValue) : MathF.Floor(factor * (float)baseValue));
            }
            return baseValue;
        }

        private static bool Prefix(ref int armorValue, ref int __result, ref ItemModifier __instance)
        {
            float calculatedModifier = 1f + (__instance.Armor / 100f);
            int result = ModifyFactor(armorValue, calculatedModifier);
            __result = MBMath.ClampInt(result, 1, result);
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemModifier))]
    [HarmonyPatch("ModifyDamage")]
    internal class ModifyModifyDamagePatch
    {
        private static int ModifyFactor(int baseValue, float factor)
        {
            if (baseValue == 0)
            {
                return 0;
            }
            if (!MBMath.ApproximatelyEquals(factor, 0f))
            {
                baseValue = ((factor < 1f) ? MathF.Ceiling(factor * (float)baseValue) : MathF.Floor(factor * (float)baseValue));
            }
            return baseValue;
        }

        private static bool Prefix(ref int baseDamage, ref int __result, ref ItemModifier __instance)
        {
            float calculatedModifier = 1f + (__instance.Damage / 100f);
            int result = ModifyFactor(baseDamage, calculatedModifier);
            __result = MBMath.ClampInt(result, 1, result);
            return false;
        }
    }
}