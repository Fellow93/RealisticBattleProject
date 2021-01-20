using HarmonyLib;
using SandBox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using static TaleWorlds.Core.Crafting;

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
            float num2 = 0.5f * weaponWeight * num * num * 0.35f;
            if (num2 > (weaponWeight * 14.0f))
            {
                num2 = weaponWeight * 14.0f;
            }
            return num2;

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
                        float bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * bluntFactorCut * bluntFraction;
                        float bluntTraumaAfterArmor = bluntTrauma * armorReduction;
                        damage += bluntTraumaAfterArmor;

                        if(player != null)
                        {
                            if (isPlayerVictim)
                            {
                                //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                InformationManager.DisplayMessage(new InformationMessage("You received " + bluntTraumaAfterArmor + " blunt trauma, " + penetratedDamage + "armor penetration damage"));
                                //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                            }
                            else
                            {
                                InformationManager.DisplayMessage(new InformationMessage("You dealt " + bluntTraumaAfterArmor + " blunt trauma, " + penetratedDamage + "armor penetration damage"));
                            }
                        }
                        break;
                    }
                case DamageTypes.Pierce:
                    {
                        float penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * pierceTreshold);
                        float bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * bluntFactorPierce * bluntFraction;
                        float bluntTraumaAfterArmor = bluntTrauma * armorReduction;
                        damage += bluntTraumaAfterArmor;

                        if (player != null)
                        {
                            if (isPlayerVictim)
                            {
                                //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                InformationManager.DisplayMessage(new InformationMessage("You received " + bluntTraumaAfterArmor + " blunt trauma, " + penetratedDamage + "armor penetration damage"));
                                //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                            }
                            else
                            {
                                InformationManager.DisplayMessage(new InformationMessage("You dealt " + bluntTraumaAfterArmor + " blunt trauma, " + penetratedDamage + "armor penetration damage"));
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
                num2 = 10f;
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

    //[HarmonyPatch(typeof(Crafting))]
    //[HarmonyPatch("CreatePreCraftedWeapon")]
    //class GenerateItemPatch
    //{

    //    static List<string> names = new List<string>();
    //    static bool Prefix(ItemObject itemObject, WeaponDesignElement[] usedPieces, string templateId, TextObject weaponName, OverrideData overridenData, ItemModifierGroup itemModifierGroup)
    //    {
    //        if (itemObject != null)
    //        {
    //            bool contains = false;
    //            foreach(String name in names)
    //            {
    //                if (name.Equals(itemObject.StringId))
    //                {
    //                    contains = true;
    //                }
    //            }
    //            if (contains)
    //            {
    //                return false;
    //            }
    //            else
    //            {
    //                names.Add(itemObject.StringId);
    //                return true;
    //            }
    //        }
    //        return true;
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
}
