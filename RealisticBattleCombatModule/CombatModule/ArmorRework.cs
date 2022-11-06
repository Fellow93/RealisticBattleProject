using HarmonyLib;
using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
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
                                //__result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead);
                                __result = getHeadArmor(__instance);
                                break;
                            }
                        case BoneBodyPartType.Neck:
                            {
                                //__result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead) * 0.66f;
                                __result = getNeckArmor(__instance);
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
                EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.Head];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
                {
                    num += (float)equipmentElement.GetModifiedHeadArmor();
                }
                num *= 0.66f;
                return num;
            }

            //static public float getNeckArmor(Agent agent)
            //{
            //    float num = 0f;
            //    for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            //    {
            //        EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
            //        if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
            //        {
            //            num += (float)equipmentElement.GetModifiedBodyArmor();
            //        }
            //        if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
            //        {
            //            num += (float)equipmentElement.GetModifiedArmArmor();
            //        }
            //    }
            //    return num;
            //}

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
                    num += (float)equipmentElement.GetModifiedBodyArmor();
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
}
