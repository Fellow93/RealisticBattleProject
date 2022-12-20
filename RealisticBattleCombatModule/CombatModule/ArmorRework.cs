using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    public class ArmorRework
    {
        static public float getHeadArmor(Agent agent)
        {
            float num = 0f;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.Head];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
            {
                num += (float)equipmentElement.GetModifiedHeadArmor();
            }
            return num;
        }

        static public float getNeckArmor(Agent agent)
        {
            float num = 0f;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.Body];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
            {
                num += (float)equipmentElement.GetModifiedArmArmor();
            }
            return num;
        }

        static public float getHorseHeadArmor(Agent agent)
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

        static public float getHorseLegArmor(Agent agent)
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

        static public float getHorseArmArmor(Agent agent)
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

        static public float getHorseBodyArmor(Agent agent)
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
                //num += (float)equipmentElement.GetModifiedBodyArmor();
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

        static public float getAbdomenArmor(Agent agent)
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

        static public float getMyChestArmor(Agent agent)
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

        static public float getArmArmor(Agent agent)
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

        static public float getLegArmor(Agent agent)
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

        static public float getHeadArmor(Equipment equipment)
        {
            float num = 0f;
            EquipmentElement equipmentElement = equipment[EquipmentIndex.Head];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
            {
                num += (float)equipmentElement.GetModifiedHeadArmor();
            }
            return num;
        }

        static public float getNeckArmor(Equipment equipment)
        {
            float num = 0f;
            EquipmentElement equipmentElement = equipment[EquipmentIndex.Body];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
            {
                num += (float)equipmentElement.GetModifiedArmArmor();
            }
            return num;
        }

        static public float getHorseHeadArmor(Equipment equipment)
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

        static public float getHorseLegArmor(Equipment equipment)
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

        static public float getHorseArmArmor(Equipment equipment)
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

        static public float getHorseBodyArmor(Equipment equipment)
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

        static public float getShoulderArmor(Equipment equipment)
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

        static public float getAbdomenArmor(Equipment equipment)
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

        static public float getMyChestArmor(Equipment equipment)
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

        static public float getArmArmor(Equipment equipment)
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

        static public float getLegArmor(Equipment equipment)
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
                            //__result = agent.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead);
                            result = getHeadArmor(agent);
                            break;
                        }
                    case BoneBodyPartType.Neck:
                        {
                            //__result = agent.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead) * 0.66f;
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
                            result = getMyChestArmor(agent);
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
                        result = getMyChestArmor(equipment);
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
    class ModifyArmorPatch
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

        static bool Prefix(ref int armorValue, ref int __result ,ref int ____armor)
        {
            float calculatedModifier = 1f + (____armor / 100f);
            int result = ModifyFactor(armorValue, calculatedModifier);
            __result = MBMath.ClampInt(result, 1, result);
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemModifier))]
    [HarmonyPatch("ModifyDamage")]
    class ModifyModifyDamagePatch
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

        static bool Prefix(ref int baseDamage, ref int __result, ref int ____damage)
        {
            float calculatedModifier = 1f + (____damage / 100f);
            int result = ModifyFactor(baseDamage, calculatedModifier);
            __result = MBMath.ClampInt(result, 1, result);
            return false;
        }
    }
}
