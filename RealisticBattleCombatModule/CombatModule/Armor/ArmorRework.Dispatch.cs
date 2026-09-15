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
            return ApplyDrivenArmorBonus(agent, bodyPart, result);
        }

        /// <summary>
        /// Vanilla applies armor perks (Athletics.IgnorePain, Engineering.Metallurgy, Riding.DauntlessSteed,
        /// Riding.ToughSteed) by rewriting the ArmorHead/Torso/Arms/Legs driven properties on top of the
        /// plain equipment sums. RBM computes per-body-part armor from equipment directly, so carry the
        /// perk effect over as the ratio (or, for a bare body part, the flat delta) between the driven
        /// property and the equipment sum it was built from.
        /// </summary>
        private static float ApplyDrivenArmorBonus(Agent agent, BoneBodyPartType bodyPart, float rbmArmor)
        {
            AgentDrivenProperties props = agent.AgentDrivenProperties;
            Equipment equipment = agent.SpawnEquipment;
            if (props == null || equipment == null)
            {
                return rbmArmor;
            }

            float driven;
            float vanillaBase;
            if (!agent.IsHuman)
            {
                driven = props.ArmorTorso;
                vanillaBase = 0f;
                for (int i = 1; i < 12; i++)
                {
                    if (equipment[i].Item != null)
                    {
                        vanillaBase += equipment[i].GetModifiedMountBodyArmor();
                    }
                }
            }
            else
            {
                switch (bodyPart)
                {
                    case BoneBodyPartType.Head:
                    case BoneBodyPartType.Neck:
                        driven = props.ArmorHead;
                        vanillaBase = equipment.GetHeadArmorSum();
                        break;
                    case BoneBodyPartType.Legs:
                        driven = props.ArmorLegs;
                        vanillaBase = equipment.GetLegArmorSum();
                        break;
                    case BoneBodyPartType.ArmLeft:
                    case BoneBodyPartType.ArmRight:
                        driven = props.ArmorArms;
                        vanillaBase = equipment.GetArmArmorSum();
                        break;
                    case BoneBodyPartType.Chest:
                    case BoneBodyPartType.Abdomen:
                    case BoneBodyPartType.ShoulderLeft:
                    case BoneBodyPartType.ShoulderRight:
                        driven = props.ArmorTorso;
                        vanillaBase = equipment.GetHumanBodyArmorSum();
                        break;
                    default:
                        return rbmArmor;
                }
            }

            if (driven <= 0f)
            {
                return rbmArmor;
            }
            if (vanillaBase > 0f)
            {
                return rbmArmor * (driven / vanillaBase);
            }
            // Nothing worn on this part: vanilla's result is the flat perk bonus itself.
            return rbmArmor + driven;
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
