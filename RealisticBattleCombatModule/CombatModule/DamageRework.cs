using HarmonyLib;
using SandBox;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
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
            static bool Prefix(ref AttackCollisionData acd, ItemObject weaponItem, bool isVictimAgentNull, float momentumRemaining, float missileTotalDamage, out float baseMagnitude, out float specialMagnitude, Vec3 victimVel)
            {

                //Vec3 gcn = acd.CollisionGlobalNormal;
                // Vec3 wbd = acd.MissileVelocity;

                //float angleModifier = Vec3.DotProduct(gcn, wbd);

                //Vec3 resultVec = gcn + wbd;
                //float angleModifier = 1f - Math.Abs((resultVec.x + resultVec.y + resultVec.z) / 3);

                float length;
                if (!isVictimAgentNull)
                {
                    length = (victimVel - acd.MissileVelocity).Length;
                }
                else
                {
                    length = acd.MissileVelocity.Length;
                }
                //float expr_32 = length / acd.MissileStartingBaseSpeed;
                //float num = expr_32 * expr_32;

                if (weaponItem != null && weaponItem.PrimaryWeapon != null)
                {
                    if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Boulder") ||
                        weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Stone"))
                    {
                        missileTotalDamage *= 0.01f;
                    }
                    if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingAxe") ||
                        weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingKnife"))
                    {
                        //length += -(7.0f);
                        //if (length < 5.0f)
                        //{
                        //    length = 5.0f;
                        //} 
                        missileTotalDamage *= 0.01f;
                    }
                    if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin"))
                    {
                        length -= 10f;
                        if (length < 5.0f)
                        {
                            length = 5f;
                        }
                        //missileTotalDamage += 168.0f;
                        missileTotalDamage *= 0.0020f;
                    }
                    if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm"))
                    {
                        length -= 5f;
                        if (length < 5.0f)
                        {
                            length = 5f;
                        }
                        missileTotalDamage *= 0.0045f;
                    }
                    if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("LowGripPolearm"))
                    {
                        length -= 5f;
                        if (length < 5.0f)
                        {
                            length = 5f;
                        }
                        missileTotalDamage *= 0.0045f;
                    }
                    else
                    {
                        if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow"))
                        {
                            missileTotalDamage -= 10f;
                            missileTotalDamage *= 0.01f;
                        }
                        if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt"))
                        {
                            missileTotalDamage -= 10f;
                            missileTotalDamage *= 0.01f;
                        }
                    }
                }

                float physicalDamage = ((length * length) * (weaponItem.Weight)) / 2;

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin") && physicalDamage > (weaponItem.Weight) * 200f)
                {
                    physicalDamage = (weaponItem.Weight) * 200f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") && physicalDamage > (weaponItem.Weight) * 150f)
                {
                    physicalDamage = (weaponItem.Weight) * 150f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow") && physicalDamage > (weaponItem.Weight) * 2500f)
                {
                    physicalDamage = (weaponItem.Weight) * 2500f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt") && physicalDamage > (weaponItem.Weight) * 2500f)
                {
                    physicalDamage = (weaponItem.Weight) * 2500f;
                }

                baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;
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
            float num = extraLinearSpeed;
            if (!isThrown)
            {
                weaponWeight += 0f;
            }
            float num2 = 0.5f * weaponWeight * num * num * 0.20f;
            if (num2 > (weaponWeight * 10.0f))
            {
                num2 = weaponWeight * 10.0f;
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
                if (!isThrown)
                {
                    weaponWeight += 2.5f;
                }
                float kineticEnergy = 0.5f * weaponWeight * combinedSpeed * combinedSpeed;
                float handLimit = 0.5f * weaponWeight * thrustWeaponSpeed * thrustWeaponSpeed * 1.25f;
                if (kineticEnergy > handLimit)
                {
                    kineticEnergy = handLimit;
                }
                __result =  0.125f * kineticEnergy;
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
                if (damageMultiplierOfBone == 2f)
                {
                    dmgMultiplier *= 1.5f;
                }
                else
                {
                    dmgMultiplier *= damageMultiplierOfBone;
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
                                    num *= 0.5f;
                                    break;
                                }
                            case "Bolt":
                                {
                                    num *= 0.5f;
                                    break;
                                }
                            case "Javelin":
                                {
                                    num *= 1.5f;
                                    break;
                                }
                            case "ThrowingAxe":
                                {
                                    num *= 1.0f;
                                    break;
                                }
                            case "OneHandedPolearm":
                                {
                                    num *= 100.0f;
                                    break;
                                }
                            case "LowGripPolearm":
                                {
                                    num *= 100.0f;
                                    break;
                                }
                            default:
                                {
                                    num *= 0.1f;
                                    break;
                                }
                        }
                    }
                    else if (attackCollisionData.DamageType == 1)
                    {
                        num *= 0.5f;
                    }
                    else if (attackCollisionData.DamageType == 2)
                    {
                        num *= 0.5f;
                    }
                    if (attackerWeapon != null && attackerWeapon.WeaponFlags.HasAnyFlag(WeaponFlags.BonusAgainstShield))
                    {
                        num *= 5f;
                    }

                    if (num > 0f)
                    {
                        if (!isVictimAgentLeftStance)
                        {
                            num *= ManagedParameters.Instance.GetManagedParameter(ManagedParametersEnum.ShieldRightStanceBlockDamageMultiplier);
                        }
                        if (attackCollisionData.CorrectSideShieldBlock)
                        {
                            num *= ManagedParameters.Instance.GetManagedParameter(ManagedParametersEnum.ShieldCorrectSideBlockDamageMultiplier);
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
            float mag_1hpol;
            float mag_2hpol;

            if (damageType == DamageTypes.Pierce)
            {
                mag_1hpol = magnitude + XmlConfig.dict["Global.OneHandedPolearmBonus"];
                mag_2hpol = magnitude + XmlConfig.dict["Global.TwoHandedPolearmBonus"];
            }
            else if(damageType == DamageTypes.Cut)
            {
                mag_1hpol = magnitude + XmlConfig.dict["Global.OneHandedPolearmBonus"];
                mag_2hpol = magnitude + XmlConfig.dict["Global.TwoHandedPolearmBonus"];
            }
            else
            {
                mag_1hpol = magnitude;
                mag_2hpol = magnitude;
            }

            switch (weaponType)
            {
                case "Dagger":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
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
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                                XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedSword":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "OneHandedAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1hpol, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_2hpol, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "Mace":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedMace":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
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
                        magnitude += 35.0f;
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "ThrowingAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
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

                        if(player != null)
                        {
                            if (isPlayerVictim)
                            {
                                //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                InformationManager.DisplayMessage(new InformationMessage("You received " + (int)(bluntTraumaAfterArmor) +
                                    " blunt trauma, " + (int)(penetratedDamage) + "armor penetration damage"));
                                //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                            }
                            else
                            {
                                InformationManager.DisplayMessage(new InformationMessage("You dealt " + (int)(bluntTraumaAfterArmor) +
                                    " blunt trauma, " + (int)(penetratedDamage) + "armor penetration damage"));
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

                        if (player != null)
                        {
                            if (isPlayerVictim)
                            {
                                //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                InformationManager.DisplayMessage(new InformationMessage("You received " + (int)(bluntTraumaAfterArmor) +
                                    " blunt trauma, " + (int)(penetratedDamage) + "armor penetration damage"));
                                //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                            }
                            else
                            {
                                InformationManager.DisplayMessage(new InformationMessage("You dealt " + (int)(bluntTraumaAfterArmor) +
                                    " blunt trauma, " + (int)(penetratedDamage) + "armor penetration damage"));
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
                num2 = 100f;
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
            float val = ((float)weaponComponentData.ThrustDamage - 50f) * 0.1f * GetFactor(weaponComponentData.ThrustDamageType) * ((float)weaponComponentData.ThrustSpeed * 0.01f);
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
                        num2 = ((float)weaponComponentData.ThrustDamage - 100f) * 0.34f - 1f;
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
            __result =  num2;
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
                ArmorTier = (float)armorComponent.HeadArmor * 0.086f - 1f;
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
        static bool Prefix(ref AttackCollisionData acd, Vec3 attackerAgentMovementDirection, Vec3 attackerAgentVelocity, float agentMountChargeDamageProperty, Vec3 victimAgentVelocity, Vec3 victimAgentPosition, out float baseMagnitude, out float specialMagnitude)
        {
            Vec3 v = victimAgentVelocity.ProjectOnUnitVector(attackerAgentMovementDirection);
            Vec3 vec = attackerAgentVelocity - v;
            float num = ChargeDamageDotProduct(victimAgentPosition, attackerAgentMovementDirection, acd.CollisionGlobalPosition);
            float num2 = vec.Length * num;
            baseMagnitude = (num2 * num2 * num * agentMountChargeDamageProperty) / 2500f;
            specialMagnitude = baseMagnitude;

            return false;
        }

        private static float ChargeDamageDotProduct(Vec3 victimPosition, Vec3 chargerMovementDirection, Vec3 collisionPoint)
        {
            Vec2 va = victimPosition.AsVec2 - collisionPoint.AsVec2;
            va.Normalize();
            Vec2 asVec = chargerMovementDirection.AsVec2;
            return Vec2.DotProduct(va, asVec);
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
            XDocument xDocument = MBObjectManager.ToXDocument(xmlDocument1);
            XDocument xDocument2 = MBObjectManager.ToXDocument(xmlDocument2);

            List<XElement> toRemove = new List<XElement>();

            foreach(XElement node in xDocument.Root.Elements())
            {
                if(node.Name == "CraftedItem")
                {
                    foreach (XElement node2 in xDocument2.Root.Elements())
                    {
                        if (node2.Name == "CraftedItem")
                        {
                            if (node.Attribute("id").Value.Equals(node2.Attribute("id").Value)){
                                toRemove.Add(node);
                            }
                        }
                    }
                }
            }
            foreach (XElement node in toRemove)
            {
                node.Remove();
            }

            xDocument.Root.Add(xDocument2.Root.Elements());
            __result = MBObjectManager.ToXmlDocument(xDocument);
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

            if ((collisionData.CollisionResult == CombatCollisionResult.Parried && !collisionData.AttackBlockedWithShield) || (collisionData.AttackBlockedWithShield && !collisionData.CorrectSideShieldBlock))
            {
                switch (weaponType)
                {
                    case "TwoHandedAxe":
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

    //[HarmonyPatch(typeof(Mission))]
    //[HarmonyPatch("MeleeHitCallback")]
    //class MeleeHitCallbackPatch
    //{
    //    static void Postfix(ref AttackCollisionData collisionData, Agent attacker, Agent victim, GameEntity realHitEntity, ref float inOutMomentumRemaining, ref MeleeCollisionReaction colReaction, CrushThroughState crushThroughState, Vec3 blowDir, Vec3 swingDir, ref HitParticleResultData hitParticleResultData, bool crushedThroughWithoutAgentCollision)
    //    {
    //    }
    //}

    [HarmonyPatch(typeof(Agent))]
    [HarmonyPatch("HandleBlow")]
    class MeleeHitCallbackPatch
    {

        private static ArmorComponent.ArmorMaterialTypes GetProtectorArmorMaterialOfBone(Agent agent,sbyte boneIndex)
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
		        case BoneBodyPartType.BipedalArmLeft:
		        case BoneBodyPartType.BipedalArmRight:
		        case BoneBodyPartType.QuadrupedalArmLeft:
		        case BoneBodyPartType.QuadrupedalArmRight:
			        equipmentIndex = EquipmentIndex.Gloves;
			        break;
		        case BoneBodyPartType.BipedalLegs:
		        case BoneBodyPartType.QuadrupedalLegs:
			        equipmentIndex = EquipmentIndex.Leg;
			        break;
		        case BoneBodyPartType.Head:
		        case BoneBodyPartType.Neck:
			        equipmentIndex = EquipmentIndex.NumAllWeaponSlots;
			        break;
		        }
		        if (equipmentIndex != EquipmentIndex.None && agent.SpawnEquipment[equipmentIndex].Item != null)
		        {
			        return agent.SpawnEquipment[equipmentIndex].Item.ArmorComponent.MaterialType;
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

            bool isKnockBack = (b.BlowFlag & BlowFlags.NonTipThrust) != 0;
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
}
