using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMConfig
{
    /// <summary>
    /// Armor/penetration damage math shared by RBMCombat (the real blow) and RBMAI (the
    /// posture crush-through HP estimate). Lives here because RBMConfig is the one project
    /// both reference; RBMAI does not reference RBMCombat. RBMCombat's behaviour is the
    /// source of truth for everything in this file.
    /// </summary>
    public static class BlowDamage
    {
        public static float RBMComputeDamage(string weaponType, DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio, out float penetratedDamage, out float bluntTraumaAfterArmor, float weaponDamageFactor = 1f, BasicCharacterObject player = null, bool isPlayerVictim = false, ArmorMaterialTypes armorMaterial = ArmorMaterialTypes.None)
        {
            if (armorMaterial != ArmorMaterialTypes.None)
            {
                if (armorMaterial != ArmorMaterialTypes.Plate && damageType == DamageTypes.Pierce && (weaponType.Contains("Arrow") || weaponType.Contains("Bolt")))
                {
                    armorEffectiveness *= 0.5f;
                }
            }

            float damage = 0f;
            float armorReduction = 100f / (100f + armorEffectiveness * RBMConfig.armorMultiplier);
            float mag_1h_thrust;
            float mag_2h_thrust;
            float mag_1h_sword_thrust;
            float mag_2h_sword_thrust;

            if (damageType == DamageTypes.Pierce)
            {
                mag_1h_thrust = magnitude * RBMConfig.OneHandedThrustDamageBonus;
                mag_2h_thrust = magnitude * 1f * RBMConfig.TwoHandedThrustDamageBonus;
                mag_1h_sword_thrust = magnitude * 1.0f * RBMConfig.OneHandedThrustDamageBonus;
                mag_2h_sword_thrust = magnitude * 1f * RBMConfig.TwoHandedThrustDamageBonus;
            }
            else if (damageType == DamageTypes.Cut)
            {
                mag_1h_thrust = magnitude;
                mag_2h_thrust = magnitude;
                mag_1h_sword_thrust = magnitude * 1.0f;
                mag_2h_sword_thrust = magnitude * 1.00f;
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
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "ThrowingKnife":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedSword":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedSword":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "Mace":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedMace":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "Arrow":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "Bolt":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "Javelin":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "ThrowingAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "SlingStone":
                    {
                        damage = WeaponTypeDamage(RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                default:
                    {
                        //InformationManager.DisplayMessage(new InformationMessage("POZOR DEFAULT !!!!"));
                        RBMCombatConfigWeaponType defaultwct = new RBMCombatConfigWeaponType("default", 1f, 1f, 1f, 1f, 1f, 1f);
                        damage = WeaponTypeDamage(defaultwct, magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
            }
            return damage * absorbedDamageRatio;
        }

        private static float WeaponTypeDamage(RBMCombatConfigWeaponType weaponTypeFactors, float magnitude, float armorReduction, DamageTypes damageType, float armorEffectiveness, BasicCharacterObject player, bool isPlayerVictim, float weaponDamageFactor, out float penetratedDamage, out float bluntTraumaAfterArmor)
        {
            float damage = 0f;
            float armorThresholdModifier = RBMConfig.armorThresholdModifier / weaponDamageFactor;

            float extraArmorThresholdFactorCut = 1f;
            float extraArmorThresholdFactorPierce = 1f;
            float extraBluntFactorCut = 1f;
            float extraBluntFactorPierce = 1f;
            if (weaponTypeFactors != null)
            {
                extraArmorThresholdFactorCut = weaponTypeFactors.ExtraArmorThresholdFactorCut;
                extraArmorThresholdFactorPierce = weaponTypeFactors.ExtraArmorThresholdFactorPierce;
                extraBluntFactorCut = weaponTypeFactors.ExtraBluntFactorCut;
                extraBluntFactorPierce = weaponTypeFactors.ExtraBluntFactorPierce;
            }

            switch (damageType)
            {
                case DamageTypes.Blunt:
                    {
                        //float armorReductionBlunt = 100f / ((100f + armorEffectiveness) * RBMConfig.RBMConfig.dict["Global.ArmorMultiplier"]);
                        //damage += magnitude * armorReductionBlunt * RBMConfig.RBMConfig.dict["Global.MaceBluntModifier"];

                        penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * 5f * armorThresholdModifier);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (0.7f * RBMConfig.maceBluntModifier) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        break;
                    }
                case DamageTypes.Cut:
                    {
                        penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * extraArmorThresholdFactorCut * armorThresholdModifier);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (extraBluntFactorCut + RBMConfig.bluntTraumaBonus) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (RBMConfig.armorPenetrationMessage)
                        {
                            MBTextManager.SetTextVariable("DMG1", (int)(bluntTraumaAfterArmor));
                            MBTextManager.SetTextVariable("DMG2", (int)(penetratedDamage));
                            if (player != null)
                            {
                                if (isPlayerVictim)
                                {
                                    //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_021}You received {DMG1} blunt trauma, {DMG2} armor penetration damage").ToString()));
                                    //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_022}You dealt {DMG1} blunt trauma, {DMG2} armor penetration damage").ToString()));
                                }
                            }
                        }
                        break;
                    }
                case DamageTypes.Pierce:
                    {
                        penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * extraArmorThresholdFactorPierce * armorThresholdModifier);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (extraBluntFactorPierce + RBMConfig.bluntTraumaBonus) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (RBMConfig.armorPenetrationMessage)
                        {
                            MBTextManager.SetTextVariable("DMG1", (int)(bluntTraumaAfterArmor));
                            MBTextManager.SetTextVariable("DMG2", (int)(penetratedDamage));
                            if (player != null)
                            {
                                if (isPlayerVictim)
                                {
                                    //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_021}You received {DMG1} blunt trauma, {DMG2} armor penetration damage").ToString()));
                                    //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_022}You dealt {DMG1} blunt trauma, {DMG2} armor penetration damage").ToString()));
                                }
                            }
                        }
                        break;
                    }
                default:
                    {
                        penetratedDamage = 0f;
                        bluntTraumaAfterArmor = 0f;
                        damage = 0f;
                        break;
                    }
            }
            return damage;
        }
    }
}
