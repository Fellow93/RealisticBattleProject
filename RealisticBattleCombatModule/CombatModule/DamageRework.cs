using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using SandBox;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.GameComponents.Map;
using TaleWorlds.Core;
using TaleWorlds.DotNet;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace RealisticBattleCombatModule
{
    class DamageRework
    {
        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("ComputeBlowMagnitudeMissile")]
        class RealArrowDamage
        {
            static bool Prefix(ref AttackCollisionData acd, ItemObject weaponItem, bool isVictimAgentNull, float momentumRemaining, float missileTotalDamage, out float baseMagnitude, out float specialMagnitude, Vec2 victimVelocity)
            {

                //Vec3 gcn = acd.CollisionGlobalNormal;
                // Vec3 wbd = acd.MissileVelocity;

                //float angleModifier = Vec3.DotProduct(gcn, wbd);

                //Vec3 resultVec = gcn + wbd;
                //float angleModifier = 1f - Math.Abs((resultVec.x + resultVec.y + resultVec.z) / 3);

                float length;
                if (!isVictimAgentNull)
                {
                    length = (victimVelocity.ToVec3() - acd.MissileVelocity).Length;
                }
                else
                {
                    length = acd.MissileVelocity.Length;
                }
                //float expr_32 = length / acd.MissileStartingBaseSpeed;
                //float num = expr_32 * expr_32;

                if (weaponItem != null && weaponItem.PrimaryWeapon != null)
                {
                    switch (weaponItem.PrimaryWeapon.WeaponClass.ToString())
                    {
                        case "Boulder":
                        case "Stone":
                            {
                                missileTotalDamage *= 0.01f;
                                break;
                            }
                        case "ThrowingAxe":
                        case "ThrowingKnife":
                            {
                                length -= 5f;
                                if (length < 5.0f)
                                {
                                    length = 5f;
                                }
                                //length += -(7.0f);
                                //if (length < 5.0f)
                                //{
                                //    length = 5.0f;
                                //} 
                                break;
                            }
                        case "Javelin":
                            {
                                length -= 10f;
                                if (length < 5.0f)
                                {
                                    length = 5f;
                                }
                                //missileTotalDamage += 168.0f;
                                //missileTotalDamage *= 0.01f;
                                //missileTotalDamage = 1f;
                                break;
                            }
                        case "OneHandedPolearm":
                            {
                                length -= 10f;
                                if (length < 5.0f)
                                {
                                    length = 5f;
                                }
                                //missileTotalDamage -= 25f;
                                //missileTotalDamage = 1f;
                                break;
                            }
                        case "LowGripPolearm":
                            {
                                length -= 10f;
                                if (length < 5.0f)
                                {
                                    length = 5f;
                                }
                                //missileTotalDamage -= 25f;
                                //missileTotalDamage *= 0.01f;
                                //missileTotalDamage = 1f;
                                break;
                            }
                        case "Arrow":
                            {
                                missileTotalDamage -= 100f;
                                missileTotalDamage *= 0.01f;
                                break;
                            }
                        case "Bolt":
                            {
                                missileTotalDamage -= 100f;
                                missileTotalDamage *= 0.01f;
                                break;
                            }
                    }
                }

                float physicalDamage = ((length * length) * (weaponItem.Weight)) / 2;

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin") && physicalDamage > 300f)
                {
                    physicalDamage = 300f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") && physicalDamage > (weaponItem.Weight) * 300f)
                {
                    physicalDamage = (weaponItem.Weight) * 300f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow") && physicalDamage > (weaponItem.Weight) * 2250f)
                {
                    physicalDamage = (weaponItem.Weight) * 2250f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt") && physicalDamage > (weaponItem.Weight) * 2500f)
                {
                    physicalDamage = (weaponItem.Weight) * 2500f;
                }

                baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin"))
                {
                    baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage - 150f)*0.5f) * XmlConfig.dict["Global.ThrustModifier"];
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingAxe"))
                {
                    baseMagnitude = physicalDamage * momentumRemaining + (missileTotalDamage * 1f);
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingKnife"))
                {
                    baseMagnitude = physicalDamage * momentumRemaining + (missileTotalDamage * 0f);
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("LowGripPolearm"))
                {
                    baseMagnitude = (physicalDamage * momentumRemaining + missileTotalDamage) * XmlConfig.dict["Global.ThrustModifier"];
                }
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt"))
                {
                    baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;
                }
                specialMagnitude = baseMagnitude;

                return false;
            }
        }
    }

    [HarmonyPatch(typeof(CombatStatCalculator))]
    [HarmonyPatch("CalculateStrikeMagnitudeForPassiveUsage")]
    class ChangeLanceDamage
    {
        static bool Prefix(float weaponWeight, float extraLinearSpeed, ref float __result)
        {
            __result = CalculateStrikeMagnitudeForThrust(0f, weaponWeight, extraLinearSpeed, isThrown: false);
            return false;
        }

        private static float CalculateStrikeMagnitudeForThrust(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown)
        {
            const float ashBreakTreshold = 43f;

            float num = extraLinearSpeed;
            if (!isThrown && weaponWeight < 1.5f)
            {
                weaponWeight += 1.0f;
            }
            float CouchedSkillBonus = 0.5f * 2.5f * num * num * 0.10f * XmlConfig.dict["Global.ThrustModifier"];
            float LanceKE = 0.5f * weaponWeight * num * num * 0.10f * XmlConfig.dict["Global.ThrustModifier"];
            float num2 = CouchedSkillBonus + LanceKE;
            if (num2 > (23f * XmlConfig.dict["Global.ThrustModifier"]) && LanceKE < (23f * XmlConfig.dict["Global.ThrustModifier"]))
            {
                num2 = 23f * XmlConfig.dict["Global.ThrustModifier"];
            }

            if (LanceKE >= (23f * XmlConfig.dict["Global.ThrustModifier"]))
            {
                num2 = LanceKE;
            }

            if (num2 > (ashBreakTreshold * XmlConfig.dict["Global.ThrustModifier"]))
            {
                num2 = ashBreakTreshold * XmlConfig.dict["Global.ThrustModifier"];
            }
            return num2;

        }
    }

    [HarmonyPatch(typeof(CombatStatCalculator))]
    [HarmonyPatch("CalculateStrikeMagnitudeForThrust")]
    class CalculateStrikeMagnitudeForThrustPatch
    {
        static bool Prefix(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown, ref float __result)
        {
            float combinedSpeed = thrustWeaponSpeed + extraLinearSpeed;
            if (combinedSpeed > 0f)
            {
                if (!isThrown && weaponWeight < 1.5f)
                {
                    weaponWeight += 0.5f;
                }
                float kineticEnergy = 0.5f * weaponWeight * combinedSpeed * combinedSpeed;
                float handBonus = 0.5f * (weaponWeight + 1.5f) * combinedSpeed * combinedSpeed;
                float handLimit = 120f;
                if (handBonus > handLimit)
                {
                    handBonus = handLimit;
                }
                float thrust = handBonus;
                if (kineticEnergy > handLimit)
                {
                    thrust = kineticEnergy;
                }
                if (thrust > 200f)
                {
                    thrust = 200f; 
                }
                __result =  0.125f * thrust * XmlConfig.dict["Global.ThrustModifier"];
                return false;
            }
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("ComputeBlowDamage")]
    class OverrideDamageCalc
    {
        static bool Prefix(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, in MissionWeapon attackerWeapon, DamageTypes damageType, float magnitude, int speedBonus, bool cancelDamage, out int inflictedDamage, out int absorbedByArmor)
        {
            float armorAmountFloat = attackInformation.ArmorAmountFloat;
            WeaponComponentData shieldOnBack = attackInformation.ShieldOnBack;
            AgentFlag victimAgentFlag = attackInformation.VictimAgentFlag;
            float victimAgentAbsorbedDamageRatio = attackInformation.VictimAgentAbsorbedDamageRatio;
            float damageMultiplierOfBone = attackInformation.DamageMultiplierOfBone;
            float combatDifficultyMultiplier = attackInformation.CombatDifficultyMultiplier;
            _ = attackCollisionData.CollisionGlobalPosition;
            bool attackBlockedWithShield = attackCollisionData.AttackBlockedWithShield;
            bool collidedWithShieldOnBack = attackCollisionData.CollidedWithShieldOnBack;
            bool isFallDamage = attackCollisionData.IsFallDamage;
            BasicCharacterObject attackerAgentCharacter = attackInformation.AttackerAgentCharacter;
            BasicCharacterObject attackerCaptainCharacter = attackInformation.AttackerCaptainCharacter;
            BasicCharacterObject victimAgentCharacter = attackInformation.VictimAgentCharacter;
            BasicCharacterObject victimCaptainCharacter = attackInformation.VictimCaptainCharacter;

            float armorAmount = 0f;

            if (!isFallDamage)
            {
                float adjustedArmor = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateAdjustedArmorForBlow(armorAmountFloat, attackerAgentCharacter, attackerCaptainCharacter, victimAgentCharacter, victimCaptainCharacter, attackerWeapon.CurrentUsageItem);
                armorAmount = adjustedArmor;
            }
            //float num2 = (float)armorAmount;
            if (collidedWithShieldOnBack && shieldOnBack != null)
            {
                armorAmount += 20f;
            }

            string weaponType = "otherDamage";
            if (attackerWeapon.Item != null && attackerWeapon.Item.PrimaryWeapon != null)
            {
                weaponType = attackerWeapon.Item.PrimaryWeapon.WeaponClass.ToString();
            }

            float dmgMultiplier = 1f;

            if (!attackBlockedWithShield && !isFallDamage)
            {
                switch (attackCollisionData.VictimHitBodyPart)
                {
                    case BoneBodyPartType.Abdomen:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 0.7f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.Chest:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 0.9f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 0.9f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 0.9f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.ShoulderLeft:
                    case BoneBodyPartType.ShoulderRight:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 0.6f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 0.6f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 0.7f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.ArmLeft:
                    case BoneBodyPartType.ArmRight:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 0.5f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 0.6f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 0.7f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.Legs:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 0.5f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 0.6f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 0.7f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.Head:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 2f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 2f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 2f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.Neck:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 2f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 2f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 2f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    default: 
                        {
                            dmgMultiplier *= 1f;
                            break;
                        }
                }
                
                dmgMultiplier *= combatDifficultyMultiplier;
            }

            BasicCharacterObject player = null;
            bool isPlayerVictim = false;
            if (attackerAgentCharacter != null && attackInformation.IsAttackerPlayer)
            {
                player = attackerAgentCharacter;
                isPlayerVictim = false;
            }
            else if (victimAgentCharacter != null && attackInformation.IsVictimPlayer)
            {
                player = victimAgentCharacter;
                isPlayerVictim = true;
            }

            inflictedDamage = MBMath.ClampInt((int)MyComputeDamage(weaponType, damageType, magnitude, armorAmount, victimAgentAbsorbedDamageRatio, player, isPlayerVictim), 0, 2000);

            inflictedDamage = (int)(inflictedDamage * dmgMultiplier);

            //float dmgWithPerksSkills = MissionGameModels.Current.AgentApplyDamageModel.CalculateDamage(ref attackInformation, ref attackCollisionData, in attackerWeapon, inflictedDamage, out float bonusFromSkills);

            //InformationManager.DisplayMessage(new InformationMessage("dmgWithPerksSkills: " + dmgWithPerksSkills + " inflictedDamage: " + inflictedDamage +
            //    " HP: " + attackInformation.VictimAgentHealth));

            int absoluteDamage = MBMath.ClampInt((int)(MyComputeDamage(weaponType, damageType, magnitude, 0f, victimAgentAbsorbedDamageRatio) * dmgMultiplier), 0, 2000);
            absorbedByArmor = absoluteDamage - inflictedDamage;

            return false;
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("ComputeBlowDamageOnShield")]
        class OverrideDamageCalcShield
        {
            static bool Prefix(bool isAttackerAgentNull, bool isAttackerAgentActive, bool isAttackerAgentDoingPassiveAttack, bool canGiveDamageToAgentShield, bool isVictimAgentLeftStance, MissionWeapon victimShield, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, float blowMagnitude)
            {
                attackCollisionData.InflictedDamage = 0;
                if (victimShield.CurrentUsageItem.WeaponFlags.HasAnyFlag(WeaponFlags.CanBlockRanged) & canGiveDamageToAgentShield)
                {
                    DamageTypes damageType = (DamageTypes)attackCollisionData.DamageType;
                    int shieldArmorForCurrentUsage = victimShield.GetGetModifiedArmorForCurrentUsage();
                    float absorbedDamageRatio = 1f;

                    string weaponType = "otherDamage";
                    if (attackerWeapon != null)
                    {
                        weaponType = attackerWeapon.WeaponClass.ToString();
                    }

                    float num = MyComputeDamage(weaponType, damageType, blowMagnitude, (float)shieldArmorForCurrentUsage, absorbedDamageRatio);

                    if (attackCollisionData.IsMissile)
                    {
                        switch (weaponType)
                        {
                            case "Arrow":
                                {
                                    num *= 1.0f;
                                    break;
                                }
                            case "Bolt":
                                {
                                    num *= 1.0f;
                                    break;
                                }
                            case "Javelin":
                                {
                                    num *= 2.5f;
                                    break;
                                }
                            case "ThrowingAxe":
                                {
                                    num *= 2.0f;
                                    break;
                                }
                            case "OneHandedPolearm":
                                {
                                    num *= 2.5f;
                                    break;
                                }
                            case "LowGripPolearm":
                                {
                                    num *= 2.5f;
                                    break;
                                }
                            default:
                                {
                                    num *= 0.1f;
                                    break;
                                }
                        }
                    }
                    else if (!attackCollisionData.IsMissile)
                    {
                        switch (weaponType)
                        {
                            case "OneHandedAxe":
                            case "TwoHandedAxe":
                            case "OneHandedBastardAxe":
                            case "TwoHandedPolearm":
                                {
                                    num *= 2.0f;
                                    break;
                                }
                            default:
                                {
                                    if (attackCollisionData.DamageType == 0)
                                    {
                                        num *= 1.5f;
                                    }
                                    else
                                    {
                                        num *= 1f;
                                    }
                                    break;
                                }
                        }
                    }

                    if (attackerWeapon != null && attackerWeapon.WeaponFlags.HasAnyFlag(WeaponFlags.BonusAgainstShield))
                    {
                        num *= 5f;
                    }

                    if (num > 0f)
                    {
                        if (!isVictimAgentLeftStance)
                        {
                            num *= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldRightStanceBlockDamageMultiplier);
                        }
                        if (attackCollisionData.CorrectSideShieldBlock)
                        {
                            num *= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldCorrectSideBlockDamageMultiplier);
                        }

                        num = MissionGameModels.Current.AgentApplyDamageModel.CalculateShieldDamage(num);
                        attackCollisionData.InflictedDamage = (int)num;
                    }
                }

                return false;
            }
        }

        private static float MyComputeDamage(string weaponType, DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio, BasicCharacterObject player = null, bool isPlayerVictim = false)
        {

            float damage = 0f;
            float armorReduction = 100f / (100f + armorEffectiveness * XmlConfig.dict["Global.ArmorMultiplier"]);
            float mag_1h_thrust;
            float mag_2h_thrust;
            float mag_1h_sword_thrust;
            float mag_2h_sword_thrust;

            if (damageType == DamageTypes.Pierce)
            {
                mag_1h_thrust = magnitude * XmlConfig.dict["Global.OneHandedThrustBonus"];
                mag_2h_thrust = magnitude * XmlConfig.dict["Global.TwoHandedThrustBonus"];
                mag_1h_sword_thrust = magnitude * 1.15f * XmlConfig.dict["Global.OneHandedThrustBonus"];
                mag_2h_sword_thrust = magnitude * 2.0f * XmlConfig.dict["Global.TwoHandedThrustBonus"];
            }
            else if (damageType == DamageTypes.Cut)
            {
                mag_1h_thrust = magnitude;
                mag_2h_thrust = magnitude;
                mag_1h_sword_thrust = magnitude * 1.35f;
                mag_2h_sword_thrust = magnitude * 1.07f;
            }
            else
            {
                mag_1h_thrust = magnitude;
                mag_2h_thrust = magnitude;
                mag_1h_sword_thrust = magnitude;
                mag_2h_sword_thrust = magnitude;
            }

            switch (weaponType)
            {
                case "Dagger":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness,
                                XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "ThrowingKnife":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                                XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "OneHandedSword":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness,
                                XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedSword":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_2h_sword_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "OneHandedAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "OneHandedBastardAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_2h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_2h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_2h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "Mace":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedMace":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_2h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "Arrow":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "Bolt":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "Javelin":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "ThrowingAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                default:
                    {
                        //InformationManager.DisplayMessage(new InformationMessage("POZOR DEFAULT !!!!"));
                        damage = weaponTypeDamage(1f, 1f, magnitude, armorReduction, damageType, armorEffectiveness, 1f, 1f, player, isPlayerVictim);
                        break;
                    }
            }

            return damage * absorbedDamageRatio;
        }

        private static float weaponTypeDamage(float bluntFactorCut, float bluntFactorPierce, float magnitude, float armorReduction, DamageTypes damageType, float armorEffectiveness, float cutTreshold, float pierceTreshold, BasicCharacterObject player, bool isPlayerVictim)
        {
            float damage = 0f;
            switch (damageType)
            {
                case DamageTypes.Blunt:
                    {
                        float armorReductionBlunt = 100f / (100f + armorEffectiveness * XmlConfig.dict["Global.ArmorMultiplier"] * 1.5f);
                        damage += magnitude * armorReductionBlunt;

                        break;
                    }
                case DamageTypes.Cut:
                    {
                        float penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * cutTreshold);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * bluntFactorCut * bluntFraction;
                        float bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (XmlConfig.dict["Global.ArmorPenetrationMessage"] >= 1f)
                        {
                            if (player != null)
                            {
                                if (isPlayerVictim)
                                {
                                    //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                    InformationManager.DisplayMessage(new InformationMessage("You received " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage) + " armor penetration damage"));
                                    //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("You dealt " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage) + " armor penetration damage"));
                                }
                            }
                        }
                            break;
                        
                    }
                case DamageTypes.Pierce:
                    {
                        float penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * pierceTreshold);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * bluntFactorPierce * bluntFraction;
                        float bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (XmlConfig.dict["Global.ArmorPenetrationMessage"] >= 1f)
                        {
                            if (player != null)
                            {
                                if (isPlayerVictim)
                                {
                                    //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                    InformationManager.DisplayMessage(new InformationMessage("You received " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage) + " armor penetration damage"));
                                    //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("You dealt " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage) + " armor penetration damage"));
                                }
                            }
                        }
                        break;
                    }
                default:
                    {
                        damage = 0f;
                        break;
                    }
            }
            return damage;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateValue")]
    class OverrideCalculateValue
    {
        static bool Prefix(ref DefaultItemValueModel __instance, ItemObject item, ref int __result)
        {
            float num = 1f;
            if (item.ItemComponent != null)
            {
                num = __instance.GetEquipmentValueFromTier(item.Tierf);
            }
            float num2 = 1f;
            if (item.ItemComponent is ArmorComponent)
            {
                num2 = ((item.ItemType == ItemObject.ItemTypeEnum.BodyArmor) ? 120 : ((item.ItemType == ItemObject.ItemTypeEnum.HandArmor) ? 120 : ((item.ItemType == ItemObject.ItemTypeEnum.LegArmor) ? 120 : 100)));
            }
            else if (item.ItemComponent is WeaponComponent)
            {
                num2 = 200f;

            }
            else if (item.ItemComponent is HorseComponent)
            {
                num2 = 100f;
            }
            else if (item.ItemComponent is SaddleComponent)
            {
                num2 = 100f;
            }
            else if (item.ItemComponent is TradeItemComponent)
            {
                num2 = 100f;
            }
            __result = (int)(num2 * num * (1f + 0.2f * (item.Appearance - 1f)) + 100f * Math.Max(0f, item.Appearance - 1f));
            return false;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateTierMeleeWeapon")]
    class OverrideCalculateTierMeleeWeapon
    {
        private static float GetFactor(DamageTypes swingDamageType)
        {
            switch (swingDamageType)
            {
                default:
                    return 1f;
                case DamageTypes.Pierce:
                    return 1.15f;
                case DamageTypes.Blunt:
                    return 2.0f;
            }
        }

        static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
        {
            WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
            float val = ((float)weaponComponentData.ThrustDamage * XmlConfig.dict["Global.OneHandedThrustBonus"] - 50f) * 0.1f * GetFactor(weaponComponentData.ThrustDamageType) * ((float)weaponComponentData.ThrustSpeed * 0.01f);
            float num = ((float)weaponComponentData.SwingDamage - 50f) * 0.1f * GetFactor(weaponComponentData.SwingDamageType) * ((float)weaponComponentData.SwingSpeed * 0.01f);
            float maceTier = ((float)weaponComponentData.SwingDamage - 30f) * 0.2f * ((float)weaponComponentData.SwingSpeed * 0.01f);
            if (val < 0f)
            {
                val = 0f;
            }
            if (num < 0f)
            {
                num = 0f;
            }
            if (maceTier < 0f)
            {
                maceTier = 0f;
            }

            float num2 = 0f;
            switch (weaponComponentData.WeaponClass)
            {
                case WeaponClass.OneHandedSword:
                case WeaponClass.Dagger:
                    {
                        num2 = (val + num) * 0.5f - 1f;
                        break;
                    }
                case WeaponClass.TwoHandedSword:
                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    {
                        num2 = (val + num) * 0.4f - 1f;
                        break;
                    }
                case WeaponClass.TwoHandedAxe:
                case WeaponClass.TwoHandedMace:
                    {
                        num2 = num * 0.75f - 1f;
                        break;
                    }
                case WeaponClass.OneHandedAxe:
                case WeaponClass.Pick:
                    {
                        num2 = num * (float)weaponComponentData.WeaponLength * 0.014f - 1f;
                        break;
                    }
                case WeaponClass.Mace:
                    {
                        num2 = maceTier * (float)weaponComponentData.WeaponLength * 0.014f - 1f;
                        break;
                    }
                case WeaponClass.ThrowingKnife:
                case WeaponClass.ThrowingAxe:
                    {
                        num2 = (float)weaponComponentData.SwingDamage * 0.05f - 1f;
                        break;
                    }
                case WeaponClass.Javelin:
                    {
                        num2 = ((float)weaponComponentData.ThrustDamage * XmlConfig.dict["Global.OneHandedThrustBonus"] - 100f) * 0.34f - 1f;
                        break;
                    }
                case WeaponClass.OneHandedPolearm:
                    {
                        num2 = val - 0.1f;
                        break;
                    }
                default:
                    {
                        num2 = (val + num) * 0.5f - 1f;
                        break;
                    }
            }
            if (num2 < 0f)
            {
                num2 = 0f;
            }
            if (num2 > 5.5f)
            {
                num2 = 5.5f;
            }
            __result =  num2;
            return false;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateRangedWeaponTier")]
    class OverrideCalculateRangedWeaponTier
    {


        static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
        {
            WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
            //float num;
            float RangedTier;
            float DrawWeight = (float)weaponComponentData.MissileSpeed * 1f;
            switch (weaponComponent.Item?.ItemType ?? ItemObject.ItemTypeEnum.Invalid)
            {
                default:
                    RangedTier = (DrawWeight - 60f) * 0.049f;
                    break;
                case ItemObject.ItemTypeEnum.Crossbow:
                    RangedTier = (DrawWeight - 450f) * 0.008f;
                    break;
            }
            //num = RangedTier;
            __result = RangedTier;
            return false;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateAmmoTier")]
    class OverrideCalculateAmmoTier
    {


        static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
        {
            WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
            //float num;
            float ArrowTier;
            float ArrowWeight = (float)weaponComponentData.MissileSpeed * 1f;

            ArrowTier = (ArrowWeight - 40f) * 0.1f;
            //num = ArrowTier;
            __result = ArrowTier;
            return false;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateArmorTier")]
    class OverrideCalculateArmorTier
    {
        static bool Prefix(ref DefaultItemValueModel __instance, ArmorComponent armorComponent, ref float __result)
        {
            float ArmorTier = 0f;
            if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.LegArmor)
            {
                ArmorTier = (float)armorComponent.LegArmor * 0.10f - 1f;
            }
            else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
            {
                ArmorTier = (float)armorComponent.ArmArmor * 0.10f - 1f;
            }
            else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
            {
                ArmorTier = (float)armorComponent.HeadArmor * 0.06f - 1f;
            }
            else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
            {
                ArmorTier = ((float)armorComponent.BodyArmor + (float)armorComponent.ArmArmor) * 0.15f - 1f;
            }
            else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
            {
                ArmorTier = ((float)armorComponent.BodyArmor * 0.05f) + ((float)armorComponent.LegArmor * 0.035f) + ((float)armorComponent.ArmArmor * 0.025f) - 1f;
            }
            else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
            {
                ArmorTier = ((float)armorComponent.BodyArmor * 0.1f) - 1f;
            }
            if (ArmorTier < 0f)
            {
                ArmorTier = 0f;
            }
            __result =  ArmorTier;
            return false;
        }
    }

    [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
    [HarmonyPatch("UpdateHumanStats")]
    class SandboxAgentUpdateHumanStats
    {
        static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties)
        {
            agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder = 0.01f;
        }
    }

    [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
    [HarmonyPatch("UpdateHorseStats")]
    class ChangeHorseChargeBonus
    {
        static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties)
        {
            float weightOfHorseAndRaider = 0f;
            if (agent.RiderAgent != null)
            {
                MissionEquipment equipment = agent.RiderAgent.Equipment;
                weightOfHorseAndRaider += (float)agent.RiderAgent.Monster.Weight;
                weightOfHorseAndRaider += agent.RiderAgent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: true);
                weightOfHorseAndRaider += equipment.GetTotalWeightOfWeapons();
                weightOfHorseAndRaider += (float)agent.Monster.Weight;
                weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                weightOfHorseAndRaider += 100f;
            }
            else
            {
                weightOfHorseAndRaider += (float)agent.Monster.Weight;
                weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                weightOfHorseAndRaider += 100f;
            }
            agentDrivenProperties.MountChargeDamage = weightOfHorseAndRaider;
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("ComputeBlowMagnitudeFromHorseCharge")]
    class ChangeHorseDamageCalculation
    {
        static bool Prefix(ref AttackCollisionData acd, Vec2 attackerAgentMovementDirection, Vec2 attackerAgentVelocity, float agentMountChargeDamageProperty, Vec2 victimAgentVelocity, Vec3 victimAgentPosition, out float baseMagnitude, out float specialMagnitude)
        {
            Vec2 vec = attackerAgentMovementDirection * Vec2.DotProduct(victimAgentVelocity, attackerAgentMovementDirection);
            Vec2 vec2 = attackerAgentVelocity - vec;
            float num = ChargeDamageDotProduct(victimAgentPosition, attackerAgentMovementDirection, acd.CollisionGlobalPosition);
            float num2 = vec2.Length * num;
            baseMagnitude = (num2 * num2 * num * agentMountChargeDamageProperty) / 2500f;
            specialMagnitude = baseMagnitude;

            return false;
        }

        private static float ChargeDamageDotProduct(Vec3 victimPosition, Vec2 chargerMovementDirection, Vec3 collisionPoint)
        {
            return Vec2.DotProduct((victimPosition.AsVec2 - collisionPoint.AsVec2).Normalized(), chargerMovementDirection);
        }
    }

    [HarmonyPatch(typeof(CustomBattleAgentStatCalculateModel))]
    [HarmonyPatch("UpdateAgentStats")]
    class CustomBattleUpdateAgentStats
    {
        static void Postfix(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder = 0.01f;

            if (!agent.IsHuman)
            {
                float weightOfHorseAndRaider = 0f;

                if (agent.RiderAgent != null)
                {
                    MissionEquipment equipment = agent.RiderAgent.Equipment;
                    weightOfHorseAndRaider += (float)agent.RiderAgent.Monster.Weight;
                    weightOfHorseAndRaider += agent.RiderAgent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: true);
                    weightOfHorseAndRaider += equipment.GetTotalWeightOfWeapons();
                    weightOfHorseAndRaider += (float)agent.Monster.Weight;
                    weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                }
                else
                {
                    weightOfHorseAndRaider += (float)agent.Monster.Weight;
                    weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                }
                agentDrivenProperties.MountChargeDamage = weightOfHorseAndRaider;
            }
        }

    }

    //[HarmonyPatch(typeof(SandboxAgentApplyDamageModel))]
    //[HarmonyPatch("DecideCrushedThrough")]
    //class OverrideDecideCrushedThrough
    //{
    //    static bool Prefix(Agent attackerAgent, Agent defenderAgent, float totalAttackEnergy, Agent.UsageDirection attackDirection, StrikeType strikeType, WeaponComponentData defendItem, bool isPassiveUsage, ref bool __result)
    //    {
    //        EquipmentIndex wieldedItemIndex = attackerAgent.GetWieldedItemIndex(Agent.HandIndex.OffHand);
    //        if (wieldedItemIndex == EquipmentIndex.None)
    //        {
    //            wieldedItemIndex = attackerAgent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
    //        }
    //        WeaponComponentData weaponComponentData = (wieldedItemIndex != EquipmentIndex.None) ? attackerAgent.Equipment[wieldedItemIndex].CurrentUsageItem : null;
    //        float num = 47f;

    //        EquipmentIndex wieldedItemIndex4 = defenderAgent.GetWieldedItemIndex(Agent.HandIndex.OffHand);
    //        WeaponComponentData secondaryItem = (wieldedItemIndex4 != EquipmentIndex.None) ? defenderAgent.Equipment[wieldedItemIndex4].CurrentUsageItem : null;
    //        int meleeSkill = Utilities.GetMeleeSkill(defenderAgent, weaponComponentData, secondaryItem);
    //        float meleeLevel = Utilities.CalculateAILevel(defenderAgent, meleeSkill);

    //        if (defendItem != null && defendItem.IsShield)
    //        {
    //            num *= (defendItem.WeaponLength / 100f) * meleeLevel * 3f;
    //        }
    //        else
    //        {
    //            num *= (weaponComponentData.WeaponLength / 100f) * meleeLevel * 2f;
    //        }
    //        if (weaponComponentData != null && weaponComponentData.WeaponClass == WeaponClass.TwoHandedMace)
    //        {
    //            num *= 0.8f;
    //        }
    //        if (defendItem != null && defendItem.IsShield)
    //        {
    //            num *= 1.2f;
    //        }
    //        if (totalAttackEnergy > num && (isPassiveUsage || attackDirection == Agent.UsageDirection.AttackUp || attackDirection == Agent.UsageDirection.AttackLeft || attackDirection == Agent.UsageDirection.AttackRight))
    //        {
    //            __result = true;
    //            return false;
    //        }
    //        __result =  false;
    //        return false;
    //    }

    //}

    [HarmonyPatch(typeof(MBObjectManager))]
    [HarmonyPatch("MergeTwoXmls")]
    class MergeTwoXmlsPatch
    {
        static bool Prefix(XmlDocument xmlDocument1, XmlDocument xmlDocument2, ref XmlDocument __result)
        {
            XDocument originalXml = MBObjectManager.ToXDocument(xmlDocument1);
            XDocument mergedXml = MBObjectManager.ToXDocument(xmlDocument2);

            List<XElement> nodesToRemoveArray = new List<XElement>();

            if(XmlConfig.dict["Global.TroopOverhaulActive"] == 0 && xmlDocument2.BaseURI.Contains("unit_overhaul"))
            {
                __result = MBObjectManager.ToXmlDocument(originalXml);
                return false;
            }

            foreach(XElement origNode in originalXml.Root.Elements())
            {
                if(origNode.Name == "CraftedItem" && xmlDocument2.BaseURI.Contains("RealisticBattle"))
                {
                    foreach (XElement mergedNode in mergedXml.Root.Elements())
                    {
                        if (mergedNode.Name == "CraftedItem")
                        {
                            if (origNode.Attribute("id").Value.Equals(mergedNode.Attribute("id").Value)){
                                nodesToRemoveArray.Add(origNode);
                            }
                        }
                    }
                }

                if (origNode.Name == "Item" && xmlDocument2.BaseURI.Contains("RealisticBattle"))
                {
                    foreach (XElement mergedNode in mergedXml.Root.Elements())
                    {
                        if (mergedNode.Name == "Item")
                        {
                            if (origNode.Attribute("id").Value.Equals(mergedNode.Attribute("id").Value))
                            {
                                nodesToRemoveArray.Add(origNode);
                            }
                        }
                    }
                }

                if (origNode.Name == "NPCCharacter" && xmlDocument2.BaseURI.Contains("RealisticBattle"))
                {
                    foreach (XElement nodeEquip in origNode.Elements())
                    {
                        if (nodeEquip.Name == "Equipments")
                        {
                            foreach (XElement nodeEquipRoster in nodeEquip.Elements())
                            {
                                if (nodeEquipRoster.Name == "EquipmentRoster")
                                {
                                    foreach (XElement mergedNode in mergedXml.Root.Elements())
                                    {
                                        if (origNode.Attribute("id").Value.Equals(mergedNode.Attribute("id").Value))
                                        {
                                            foreach (XElement mergedNodeEquip in mergedNode.Elements())
                                            {
                                                if (mergedNodeEquip.Name == "Equipments")
                                                {
                                                    foreach (XElement mergedNodeRoster in mergedNodeEquip.Elements())
                                                    {
                                                        if (mergedNodeRoster.Name == "EquipmentRoster")
                                                        {
                                                            if (!nodesToRemoveArray.Contains(origNode))
                                                            {
                                                                nodesToRemoveArray.Add(origNode);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if(nodesToRemoveArray.Count > 0)
            {
                foreach (XElement node in nodesToRemoveArray)
                {
                    node.Remove();
                }
            }

            originalXml.Root.Add(mergedXml.Root.Elements());
            __result = MBObjectManager.ToXmlDocument(originalXml);
            return false;
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("CreateMeleeBlow")]
    class CreateMeleeBlowPatch
    {
        static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent,ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
        {
            string weaponType = "otherDamage";
            if (attackerWeapon.Item != null && attackerWeapon.Item.PrimaryWeapon != null)
            {
                weaponType = attackerWeapon.Item.PrimaryWeapon.WeaponClass.ToString();
            }

            if((attackerAgent.IsDoingPassiveAttack && collisionData.CollisionResult == CombatCollisionResult.StrikeAgent))
            {
                if (attackerAgent.Team != victimAgent.Team)
                {
                    __result.BlowFlag |= BlowFlags.KnockDown;
                    return;
                }
            }

            if ((collisionData.CollisionResult == CombatCollisionResult.StrikeAgent) && (collisionData.DamageType == (int)DamageTypes.Pierce))
            {
                switch (weaponType)
                {
                    case "TwoHandedPolearm":
                        if (attackerAgent.Team != victimAgent.Team)
                        {

                            //AttackCollisionData newdata = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(false, false, false, true, false, false, false, false, false, true, false, collisionData.CollisionResult, collisionData.AffectorWeaponSlotOrMissileIndex,
                            //    collisionData.StrikeType, collisionData.StrikeType, collisionData.CollisionBoneIndex, BoneBodyPartType.Chest, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex,
                            //    collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon, collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage,
                            //    0, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp, collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity,
                            //    collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);

                            //collisionData = newdata;

                            __result.BlowFlag |= BlowFlags.KnockBack;
                        }
                        break;
                }
            }

            if ((attackerAgent.IsDoingPassiveAttack && collisionData.CollisionResult == CombatCollisionResult.Blocked))
            {
                if (attackerAgent.Team != victimAgent.Team)
                {

                    //AttackCollisionData newdata = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(false, false, false, true, false, false, false, false, false, true, false, collisionData.CollisionResult, collisionData.AffectorWeaponSlotOrMissileIndex,
                    //    collisionData.StrikeType, collisionData.StrikeType, collisionData.CollisionBoneIndex, BoneBodyPartType.Chest, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex,
                    //    collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon, collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage,
                    //    0, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp, collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity,
                    //    collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);

                    //collisionData = newdata;

                    sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                    __result.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                    __result.StrikeType = (StrikeType)collisionData.StrikeType;
                    __result.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                    __result.NoIgnore = collisionData.IsAlternativeAttack;
                    __result.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                    __result.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                    __result.BlowFlag = BlowFlags.None;
                    __result.Position = collisionData.CollisionGlobalPosition;
                    __result.BoneIndex = collisionData.CollisionBoneIndex;
                    __result.Direction = blowDirection;
                    __result.SwingDirection = swingDirection;
                    //__result.InflictedDamage = 1;
                    __result.VictimBodyPart = collisionData.VictimHitBodyPart;
                    __result.BlowFlag |= BlowFlags.KnockBack;
                    victimAgent.RegisterBlow(__result);
                    foreach (MissionBehaviour missionBehaviour in __instance.MissionBehaviours)
                    {
                        missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, null, __result, ref collisionData, in attackerWeapon);
                    }
                    return;
                }
            }

            if ((collisionData.CollisionResult == CombatCollisionResult.Parried && !collisionData.AttackBlockedWithShield) || (collisionData.AttackBlockedWithShield && !collisionData.CorrectSideShieldBlock) )
            {
                switch (weaponType)
                {
                    case "TwoHandedAxe":
                    case "OneHandedBastardAxe":
                    case "TwoHandedPolearm":
                    case "TwoHandedMace":
                        {
                            if(attackerAgent.Team != victimAgent.Team)
                            {
                                sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                                __result.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                                __result.StrikeType = (StrikeType)collisionData.StrikeType;
                                __result.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                                __result.NoIgnore = collisionData.IsAlternativeAttack;
                                __result.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                                __result.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                                __result.BlowFlag = BlowFlags.None;
                                __result.Position = collisionData.CollisionGlobalPosition;
                                __result.BoneIndex = collisionData.CollisionBoneIndex;
                                __result.Direction = blowDirection;
                                __result.SwingDirection = swingDirection;
                                //__result.InflictedDamage = 1;
                                __result.VictimBodyPart = collisionData.VictimHitBodyPart;
                                __result.BlowFlag |= BlowFlags.NonTipThrust;
                                victimAgent.RegisterBlow(__result);
                                foreach (MissionBehaviour missionBehaviour in __instance.MissionBehaviours)
                                {
                                    missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, null, __result, ref collisionData, in attackerWeapon);
                                }
                            }
                            break;
                        }
                }
            }

        }
    }

    //[UsedImplicitly]
    //[MBCallback]
    //[HarmonyPatch(typeof(Mission))]
    //[HarmonyPatch("MeleeHitCallback")]
    //class MeleeHitCallbackPatch
    //{
    //    static bool Prefix(ref AttackCollisionData collisionData, Agent attacker, Agent victim, GameEntity realHitEntity, ref float inOutMomentumRemaining, ref MeleeCollisionReaction colReaction, CrushThroughState crushThroughState, Vec3 blowDir, Vec3 swingDir, ref HitParticleResultData hitParticleResultData, bool crushedThroughWithoutAgentCollision)
    //    {
    //        //EquipmentIndex shieldindex = victim.GetWieldedItemIndex(Agent.HandIndex.OffHand);
    //        //if(shieldindex != EquipmentIndex.None)
    //        //{
    //        //    AttackCollisionData newdata = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(true, false, false, true, false, false, false, false, false, collisionData.ThrustTipHit, false, CombatCollisionResult.Blocked, collisionData.AffectorWeaponSlotOrMissileIndex,
    //        //        collisionData.StrikeType, collisionData.StrikeType,18, BoneBodyPartType.ArmLeft, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex,
    //        //        collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon, 0.2f, 0.2f, collisionData.MissileTotalDamage,
    //        //        0, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp, collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity,
    //        //        collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);
    //        //    newdata.InflictedDamage = -2147483648;
    //        //    newdata.BaseMagnitude = -1;
    //        //    newdata.AbsorbedByArmor = -2147483648;
    //        //    newdata.MovementSpeedDamageModifier = -1;
    //        //    newdata.SelfInflictedDamage = -2147483648;
    //        //    collisionData = newdata;
    //        //}
    //        return true;
    //    }
    //}

    [HarmonyPatch(typeof(Agent))]
    [HarmonyPatch("HandleBlow")]
    class HandleBlowPatch
    {

        private static ArmorComponent.ArmorMaterialTypes GetProtectorArmorMaterialOfBone(Agent agent,sbyte boneIndex)
        {
            if(agent != null && agent.SpawnEquipment != null)
            {
                if (boneIndex >= 0)
                {
                    EquipmentIndex equipmentIndex = EquipmentIndex.None;
                    switch (agent.AgentVisuals.GetBoneTypeData(boneIndex).BodyPartType)
                    {
                        case BoneBodyPartType.Chest:
                        case BoneBodyPartType.Abdomen:
                        case BoneBodyPartType.ShoulderLeft:
                        case BoneBodyPartType.ShoulderRight:
                            equipmentIndex = EquipmentIndex.Body;
                            break;
                        case BoneBodyPartType.ArmLeft:
                        case BoneBodyPartType.ArmRight:
                            equipmentIndex = EquipmentIndex.Gloves;
                            break;
                        case BoneBodyPartType.Legs:
                            equipmentIndex = EquipmentIndex.Leg;
                            break;
                        case BoneBodyPartType.Head:
                        case BoneBodyPartType.Neck:
                            equipmentIndex = EquipmentIndex.NumAllWeaponSlots;
                            break;
                    }
                    if (equipmentIndex != EquipmentIndex.None && agent.SpawnEquipment[equipmentIndex].Item != null)
                    {
                        if(agent.SpawnEquipment[equipmentIndex].Item.ArmorComponent != null)
                        {
                            return agent.SpawnEquipment[equipmentIndex].Item.ArmorComponent.MaterialType;
                        }
                    }
                }
            }
	        return ArmorComponent.ArmorMaterialTypes.None;
        }
        static bool Prefix(ref Agent __instance, ref Blow b)
        {
            b.BaseMagnitude = Math.Min(b.BaseMagnitude, 1000f)/8f;
            Agent agent = (b.OwnerId != -1) ? __instance.Mission.FindAgentWithIndex(b.OwnerId) : __instance;
            if (!b.BlowFlag.HasAnyFlag(BlowFlags.NoSound))
            {
                bool isCriticalBlow = b.IsBlowCrit(__instance.Monster.HitPoints * 4);
                bool isLowBlow = b.IsBlowLow(__instance.Monster.HitPoints);
                bool isOwnerHumanoid = agent?.IsHuman ?? false;
                bool isNonTipThrust = b.BlowFlag.HasAnyFlag(BlowFlags.NonTipThrust);
                int hitSound = b.WeaponRecord.GetHitSound(isOwnerHumanoid, isCriticalBlow, isLowBlow, isNonTipThrust, b.AttackType, b.DamageType);
                float soundParameterForArmorType = 0.1f*(float)GetProtectorArmorMaterialOfBone(__instance, b.BoneIndex);
                SoundEventParameter parameter = new SoundEventParameter("Armor Type", soundParameterForArmorType);
                __instance.Mission.MakeSound(hitSound, b.Position, soundCanBePredicted: false, isReliable: true, b.OwnerId, __instance.Index, ref parameter);
                if (b.IsMissile && agent != null)
                {
                    int soundCodeMissionCombatPlayerhit = CombatSoundContainer.SoundCodeMissionCombatPlayerhit;
                    __instance.Mission.MakeSoundOnlyOnRelatedPeer(soundCodeMissionCombatPlayerhit, b.Position, agent.Index);
                }
                __instance.Mission.AddSoundAlarmFactorToAgents(b.OwnerId, b.Position, 15f);
            }
            b.DamagedPercentage = (float)b.InflictedDamage / __instance.HealthLimit;
        //__instance.UpdateLastAttackAndHitTimes(agent, b.IsMissile);
            MethodInfo method = typeof(Agent).GetMethod("UpdateLastAttackAndHitTimes", BindingFlags.NonPublic | BindingFlags.Instance);
            method.DeclaringType.GetMethod("UpdateLastAttackAndHitTimes");
            method.Invoke(__instance, new object[] { agent, b.IsMissile });

            bool isKnockBack = ((b.BlowFlag & BlowFlags.NonTipThrust) != 0) || ((b.BlowFlag & BlowFlags.KnockDown) != 0) || ((b.BlowFlag & BlowFlags.KnockBack) != 0);
            if(b.AttackType == AgentAttackType.Bash || b.AttackType == AgentAttackType.Kick)
            {
                if(b.InflictedDamage <= 0)
                {
                    b.InflictedDamage = 1;
                }
            }
            if (b.InflictedDamage == 0 && isKnockBack)
            {
                b.InflictedDamage = 1;
            }
            if(b.DamageCalculated == false && b.InflictedDamage == 1)
            {

            }
            else
            {
                float num = __instance.Health - (float)b.InflictedDamage;
                if (num < 0f)
                {
                    num = 0f;
                }
                if (!__instance.Invulnerable && !Mission.DisableDying)
                {
                    __instance.Health = num;
                }
            }
            int affectorWeaponSlotOrMissileIndex = b.WeaponRecord.AffectorWeaponSlotOrMissileIndex;
            float hitDistance = b.IsMissile ? (b.Position - b.WeaponRecord.StartingPosition).Length : 0f;
            //__instance.Mission.OnAgentHit(__instance, agent, affectorWeaponSlotOrMissileIndex, b.IsMissile, isBlocked: false, b.InflictedDamage, b.MovementSpeedDamageModifier, hitDistance, b.AttackType, b.VictimBodyPart);
            MethodInfo method3 = typeof(Mission).GetMethod("OnAgentHit", BindingFlags.NonPublic | BindingFlags.Instance);
            method3.DeclaringType.GetMethod("OnAgentHit");
            method3.Invoke(__instance.Mission, new object[] { __instance, agent, affectorWeaponSlotOrMissileIndex, b.IsMissile, false, b.InflictedDamage, b.MovementSpeedDamageModifier, hitDistance, b.AttackType, b.VictimBodyPart });
            if (__instance.Health < 1f)
            {
                __instance.Die(b);
            }
            //__instance.HandleBlowAux(ref b);
            MethodInfo method2 = typeof(Agent).GetMethod("HandleBlowAux", BindingFlags.NonPublic | BindingFlags.Instance);
            method2.DeclaringType.GetMethod("HandleBlowAux");
            method2.Invoke(__instance, new object[] { b });
            return false;
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("DecideAgentHitParticles")]
    class DecideAgentHitParticlesPatch
    {
        [EngineStruct("Hit_particle_result_data")]
        internal struct HitParticleResultData
        {
            public int StartHitParticleIndex;

            public int ContinueHitParticleIndex;

            public int EndHitParticleIndex;

            public void Reset()
            {
                StartHitParticleIndex = -1;
                ContinueHitParticleIndex = -1;
                EndHitParticleIndex = -1;
            }
        }
        static void Postfix(Blow blow, Agent victim, ref AttackCollisionData collisionData, ref HitParticleResultData hprd)
        {
            if (victim != null && (blow.InflictedDamage > 0 || victim.Health <= 0f))
            {
                if (!blow.WeaponRecord.HasWeapon() || blow.WeaponRecord.WeaponFlags.HasAnyFlag(WeaponFlags.NoBlood) || collisionData.IsAlternativeAttack || blow.InflictedDamage <= 20)
                {
                    hprd.StartHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                    hprd.ContinueHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                    hprd.EndHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                }
                else
                {
                    hprd.StartHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_enter");
                    hprd.ContinueHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_inside");
                    hprd.EndHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_exit");
                }
            }
        }
    }

    //[HarmonyPatch(typeof(ItemObject))]
    //[HarmonyPatch("CalculateEffectiveness")]
    //class CalculateEffectivenessPatch
    //{
    //    static bool Prefix(ref ItemObject __instance, ref float __result)
    //    {
    //        float result = 1f;
    //        ArmorComponent armorComponent = __instance.ArmorComponent;
    //        if (armorComponent != null)
    //        {
    //            result = ((__instance.Type != ItemTypeEnum.HorseHarness) ? (((float)armorComponent.HeadArmor * 34f + (float)armorComponent.BodyArmor * 42f + (float)armorComponent.LegArmor * 12f + (float)armorComponent.ArmArmor * 12f) * 0.03f) : ((float)armorComponent.BodyArmor * 1.67f));
    //        }
    //        if (__instance.WeaponComponent != null)
    //        {
    //            WeaponComponentData primaryWeapon = __instance.WeaponComponent.PrimaryWeapon;
    //            float num = 1f;
    //            switch (primaryWeapon.WeaponClass)
    //            {
    //                case WeaponClass.Dagger:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.OneHandedSword:
    //                    num = 0.55f;
    //                    break;
    //                case WeaponClass.TwoHandedSword:
    //                    num = 0.6f;
    //                    break;
    //                case WeaponClass.OneHandedAxe:
    //                    num = 0.5f;
    //                    break;
    //                case WeaponClass.TwoHandedAxe:
    //                    num = 0.55f;
    //                    break;
    //                case WeaponClass.Mace:
    //                    num = 0.5f;
    //                    break;
    //                case WeaponClass.Pick:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.TwoHandedMace:
    //                    num = 0.55f;
    //                    break;
    //                case WeaponClass.OneHandedPolearm:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.TwoHandedPolearm:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.LowGripPolearm:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.Arrow:
    //                    num = 3f;
    //                    break;
    //                case WeaponClass.Bolt:
    //                    num = 3f;
    //                    break;
    //                case WeaponClass.Cartridge:
    //                    num = 3f;
    //                    break;
    //                case WeaponClass.Bow:
    //                    num = 0.55f;
    //                    break;
    //                case WeaponClass.Crossbow:
    //                    num = 0.57f;
    //                    break;
    //                case WeaponClass.Stone:
    //                    num = 0.1f;
    //                    break;
    //                case WeaponClass.Boulder:
    //                    num = 0.1f;
    //                    break;
    //                case WeaponClass.ThrowingAxe:
    //                    num = 0.25f;
    //                    break;
    //                case WeaponClass.ThrowingKnife:
    //                    num = 0.2f;
    //                    break;
    //                case WeaponClass.Javelin:
    //                    num = 0.28f;
    //                    break;
    //                case WeaponClass.Pistol:
    //                    num = 1f;
    //                    break;
    //                case WeaponClass.Musket:
    //                    num = 1f;
    //                    break;
    //                case WeaponClass.SmallShield:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.LargeShield:
    //                    num = 0.5f;
    //                    break;
    //            }
    //            if (primaryWeapon.IsRangedWeapon)
    //            {
    //                result = ((!primaryWeapon.IsConsumable) ? (((float)(primaryWeapon.MissileSpeed * primaryWeapon.MissileDamage) * 1.75f + (float)(primaryWeapon.ThrustSpeed * primaryWeapon.Accuracy) * 0.3f) * 0.01f * (float)primaryWeapon.MaxDataValue * num) : (((float)(primaryWeapon.MissileDamage * primaryWeapon.MissileSpeed) * 1.775f + (float)(primaryWeapon.Accuracy * primaryWeapon.MaxDataValue) * 25f + (float)primaryWeapon.WeaponLength * 4f) * 0.006944f * (float)primaryWeapon.MaxDataValue * num));
    //            }
    //            else if (primaryWeapon.IsMeleeWeapon)
    //            {
    //                float val = (float)(primaryWeapon.ThrustSpeed * primaryWeapon.ThrustDamage) * 0.01f;
    //                float val2 = (float)(primaryWeapon.SwingSpeed * primaryWeapon.SwingDamage) * 0.01f;
    //                float num2 = Math.Max(val2, val);
    //                float num3 = Math.Min(val2, val);
    //                result = ((num2 + num3 * num3 / num2) * 120f + (float)primaryWeapon.Handling * 15f + (float)primaryWeapon.WeaponLength * 20f + __instance.Weight * 5f) * 0.01f * num;
    //            }
    //            else if (primaryWeapon.IsConsumable)
    //            {
    //                result = ((float)primaryWeapon.MissileDamage * 550f + (float)primaryWeapon.MissileSpeed * 15f + (float)primaryWeapon.MaxDataValue * 60f) * 0.01f * num;
    //            }
    //            else if (primaryWeapon.IsShield)
    //            {
    //                result = ((float)primaryWeapon.BodyArmor * 60f + (float)primaryWeapon.ThrustSpeed * 10f + (float)primaryWeapon.MaxDataValue * 40f + (float)primaryWeapon.WeaponLength * 20f) * 0.01f * num;
    //            }
    //        }
    //        HorseComponent horseComponent = __instance.HorseComponent;
    //        if (horseComponent != null)
    //        {
    //            result = ((float)(horseComponent.ChargeDamage * horseComponent.Speed + horseComponent.Maneuver * horseComponent.Speed) + (float)horseComponent.BodyLength * __instance.Weight * 0.025f) * (float)(horseComponent.HitPoints + horseComponent.HitPointBonus) * 0.0001f;
    //        }
    //        __result =  result;
    //        return false;
    //    }
    //}

    [HarmonyPatch(typeof(Mission))]
    class DecideAgentDismountedByBlowPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("DecideAgentDismountedByBlow")]
        static bool PrefixDecideAgentDismountedByBlow(Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, WeaponComponentData attackerWeapon, bool isInitialBlowShrugOff, ref Blow blow)
        {
            if (!blow.IsMissile)
            {
                if (victimAgent.HasMount && !isInitialBlowShrugOff)
                {
                    //bool flag = (float)blow.InflictedDamage / victimAgent.HealthLimit > 0.25f;
                    bool flag = blow.BaseMagnitude > 7f;
                    bool flag2 = MBMath.IsBetween((int)blow.VictimBodyPart, 0, 5);
                    if (!(victimAgent.Health - (float)collisionData.InflictedDamage >= 1f && flag && flag2))
                    {
                        return false;
                    }
                    if (attackerWeapon != null && attackerWeapon.ItemUsage != null && blow.StrikeType == StrikeType.Thrust && attackerWeapon.ItemUsage.Equals("polearm_couch"))//&& blow.WeaponRecord.WeaponFlags.HasAnyFlag(WeaponFlags.CanDismount))
                    {
                        blow.BlowFlag |= BlowFlags.CanDismount;
                        return false;
                    }
                    float num = 0f;
                    num += MissionGameModels.Current.AgentApplyDamageModel.CalculateDismountChanceBonus(attackerAgent, attackerWeapon);
                    if ((MBMath.IsBetween(num, 0f, 1f) ? MBRandom.RandomFloat : 0.1f) <= num)
                    {
                        blow.BlowFlag |= BlowFlags.CanDismount;
                    }
                }
                else
                {
                    _ = victimAgent.HasMount;
                }
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(DefaultPartyHealingModel))]
    class OverrideDefaultPartyHealingModel
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetSurvivalChance")]
        static bool PrefixGetAiWeight(ref float __result, PartyBase party, CharacterObject character, DamageTypes damageType, PartyBase enemyParty = null)
        {
            if (character.IsPlayerCharacter || (character.IsHero && !character.HeroObject.CanDie()))
            {
                __result = 1f;
                return false;
            }
            //if (damageType == DamageTypes.Blunt)
            //{
            //    __result = 0.25f;
            //    return false;
            //}
            ExplainedNumber stat = new ExplainedNumber(character.IsHero ? 50f : 1f);
            if (party != null && party.MobileParty != null)
            {
                MobileParty mobileParty = party.MobileParty;
                SkillHelper.AddSkillBonusForParty(DefaultSkills.Medicine, DefaultSkillEffects.SurgeonSurvivalBonus, mobileParty, ref stat);
                if (enemyParty?.MobileParty != null && enemyParty.MobileParty.HasPerk(DefaultPerks.Medicine.DoctorsOath))
                {
                    SkillHelper.AddSkillBonusForParty(DefaultSkills.Medicine, DefaultSkillEffects.SurgeonSurvivalBonus, enemyParty.MobileParty, ref stat);
                }
                stat.Add((float)character.Level * 0.02f);
                if (!character.IsHero && party.MapEvent != null && character.Tier < 3)
                {
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.Medicine.PhysicianOfPeople, party.MobileParty, isPrimaryBonus: false, ref stat);
                }
                ExplainedNumber stat2 = new ExplainedNumber(1f / stat.ResultNumber);
                if (character.IsHero)
                {
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.Medicine.CheatDeath, mobileParty, isPrimaryBonus: false, ref stat2);
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.Medicine.FortitudeTonic, mobileParty, isPrimaryBonus: true, ref stat2);
                }
                __result = 1f - MBMath.ClampFloat(stat2.ResultNumber, 0f, 1f);
                return false;
            }
            __result = 1f - 1f / stat.ResultNumber;
            return false;
        }
    }
}
