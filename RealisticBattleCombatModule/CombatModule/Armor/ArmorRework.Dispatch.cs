using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    public partial class ArmorRework
    {
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
}
