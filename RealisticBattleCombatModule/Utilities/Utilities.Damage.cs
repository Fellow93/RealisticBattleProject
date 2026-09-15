using RBMConfig;
using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    public static partial class Utilities
    {
        public static float GetSkillBasedDamage(float magnitude, bool isPassiveUsage, string weaponType, DamageTypes damageType, float effectiveSkill, float skillModifier, StrikeType strikeType, float weaponWeight)
        {
            float skillBasedDamage = 0f;
            const float ashBreakTreshold = 430f;
            const float poplarBreakTreshold = 260f;
            float BraceBonus = 0f;
            float BraceModifier = 1f; // because lances have 3 times more damage
            switch (weaponType)
            {
                case "Dagger":
                case "OneHandedSword":
                case "ThrowingKnife":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            float value = magnitude + (effectiveSkill * 0.133f);
                            float min = 5f * (1 + skillModifier);
                            float max = 15f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4.6f);
                            //skillBasedDamage = magnitude + 40f + (effectiveSkill * 0.53f);
                        }
                        else if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude + 0.50f * (40f + (effectiveSkill * 0.53f));
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.075f), 15f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * 0.4f;
                        }
                        else
                        {
                            if (strikeType == (int)StrikeType.Swing)
                            {
                                skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.133f), 5f * (1 + skillModifier), 15f * (1 + (2 * skillModifier))) * 4f) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                            }
                            else
                            {
                                //float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();
                                //float totalSpeed = (float)Math.Sqrt((magnitude * 2) / 8f);
                                //totalSpeed += 3f;
                                //skillBasedDamage = 0.5f * 8f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f));
                                //if (skillBasedDamage > 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                //{
                                //    skillBasedDamage = 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                                skillBasedDamage = magnitude;
                            }
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "TwoHandedSword":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            float value = magnitude + (effectiveSkill * 0.199f);
                            float min = 12f * (1 + skillModifier);
                            float max = 20f * (1 + (2 * skillModifier));
                            skillBasedDamage = MBMath.ClampFloat(value, min, max) * 4.6f;
                        }
                        else if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude * 1.3f + 0.5f * ((40f + (effectiveSkill * 0.53f)) * 1.3f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.112f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.4f;
                        }
                        else
                        {
                            if (strikeType == (int)StrikeType.Swing)
                            {
                                skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.199f), 12f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                            }
                            else
                            {
                                //float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();
                                //float totalSpeed = (float)Math.Sqrt((magnitude * 2) / 8f);
                                //skillBasedDamage = 0.5f * 15f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //if (skillBasedDamage > 240f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                //{
                                //    skillBasedDamage = 240 * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                                skillBasedDamage = magnitude;
                            }
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "OneHandedAxe":
                case "ThrowingAxe":
                    {
                        float value = magnitude + (effectiveSkill * 0.1f);
                        float min = 10f * (1 + skillModifier);
                        float max = 18f * (1 + (2 * skillModifier));
                        skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4.6f);
                        if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude + 0.5f * (60f + (effectiveSkill * 0.4f));
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.075f), 15f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "OneHandedBastardAxe":
                    {
                        skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.13f), 12f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4.6f);
                        if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude * 1.15f + 0.5f * ((60f + (effectiveSkill * 0.4f)) * 1.15f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.09375f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        float value = magnitude + (effectiveSkill * 0.15f);
                        float min = 15f * (1 + skillModifier);
                        float max = 24f * (1 + (2 * skillModifier));
                        skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4.6f);
                        if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude * 1.3f + 0.5f * ((60f + (effectiveSkill * 0.4f)) * 1.30f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.112f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "Mace":
                    {
                        if (damageType == DamageTypes.Pierce)
                        {
                            //float totalSpeed = (float)Math.Sqrt((magnitude * 2) / 8f);
                            //totalSpeed += 3f;
                            //skillBasedDamage = 0.5f * 8f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f));

                            //if (skillBasedDamage > 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                            //{
                            //    skillBasedDamage = 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                            //}
                            skillBasedDamage = magnitude;
                        }
                        else
                        {
                            float value = magnitude + (effectiveSkill * 0.075f);
                            float min = 10f * (1 + skillModifier);
                            float max = 15f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4.6f);
                            //skillBasedDamage = value;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "unarmedAttack":
                    {
                        float value = magnitude * (effectiveSkill * 0.2f);
                        float min = 1f * (1 + skillModifier);
                        float max = 10f * (1 + (2 * skillModifier));
                        skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 2f);
                        magnitude = skillBasedDamage;
                        break;
                    }
                case "TwoHandedMace":
                    {
                        if (damageType == DamageTypes.Pierce)
                        {
                            //skillBasedDamage = (magnitude * 0.2f + 40f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.4f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;
                            skillBasedDamage = magnitude;
                        }
                        else
                        {
                            float value = magnitude + (effectiveSkill * 1.125f);
                            float min = 15f * (1 + skillModifier);
                            float max = 22f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4.6f);
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.1f), 15f * (1 + skillModifier), 24f * (1 + (2 * skillModifier))) * 4f);
                        }
                        else if (damageType == DamageTypes.Blunt && !isPassiveUsage)
                        {
                            //skillBasedDamage = magnitude + 30f + (effectiveSkill * 0.26f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.075f), 15f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        else
                        {
                            if (isPassiveUsage)
                            {
                                float couchedSkill = 0.5f + effectiveSkill * 0.02f;
                                float skillCap = (150f + effectiveSkill * 1.5f);

                                if (weaponWeight < 2.1f)
                                {
                                    BraceBonus += 0.5f;
                                    BraceModifier *= 1f;
                                }
                                float lanceBalistics = (magnitude * BraceModifier) / weaponWeight;
                                float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                float BluntLanceBalistics = ((magnitude * BraceModifier) / weaponWeight) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                float BluntCouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                magnitude = CouchedMagnitude;

                                if (damageType == DamageTypes.Blunt)
                                {
                                    magnitude = BluntCouchedMagnitude;
                                    if (BluntCouchedMagnitude > skillCap && (BluntLanceBalistics * (weaponWeight + BraceBonus)) < skillCap) //skill based damage
                                    {
                                        magnitude = skillCap;
                                    }

                                    if ((BluntLanceBalistics * (weaponWeight + BraceBonus)) >= skillCap) //ballistics
                                    {
                                        magnitude = (BluntLanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > poplarBreakTreshold) // damage cap - lance break threshold
                                    {
                                        magnitude = poplarBreakTreshold;
                                    }
                                    magnitude *= 1f;
                                }
                                else
                                {
                                    if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                    {
                                        magnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }

                                    if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                    {
                                        magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                    {
                                        magnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }
                                }
                            }
                            else
                            {
                                float totalSpeed = (float)Math.Sqrt((magnitude * 2f) / 8f);
                                //totalSpeed += 3f;
                                skillBasedDamage = magnitude;

                                //skillBasedDamage = magnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.26f * RBMConfig.RBMConfig.ThrustMagnitudeModifier);
                                //if (skillBasedDamage > 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)

                                //{
                                //    skillBasedDamage = 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                            }
                        }
                        if (magnitude > 0.15f && !isPassiveUsage)
                        {
                            magnitude = skillBasedDamage;
                        }
                        //else if(magnitude > 0f && magnitude <= 0.15f)
                        //{
                        //    InformationManager.DisplayMessage(new InformationMessage("DEBUG WARNING: strike bagnitude below treshlod"));
                        //}
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            float value = magnitude + (effectiveSkill * 0.1495f);
                            float min = 18f * (1 + skillModifier);
                            float max = 28f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4f);
                        }
                        else if (damageType == DamageTypes.Blunt && !isPassiveUsage)
                        {
                            //skillBasedDamage = magnitude + (30f + (effectiveSkill * 0.26f) * 1.3f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.0975f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        else
                        {
                            if (isPassiveUsage)
                            {
                                float couchedSkill = 0.5f + effectiveSkill * 0.02f;
                                float skillCap = (150f + effectiveSkill * 1.5f);

                                if (weaponWeight < 2.1f)
                                {
                                    BraceBonus += 0.5f;
                                    BraceModifier *= 1f;
                                }
                                float lanceBalistics = (magnitude * BraceModifier) / weaponWeight;
                                float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                float BluntLanceBalistics = ((magnitude * BraceModifier) / weaponWeight) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                float BluntCouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                magnitude = CouchedMagnitude;

                                if (damageType == DamageTypes.Blunt)
                                {
                                    magnitude = BluntCouchedMagnitude;
                                    if (BluntCouchedMagnitude > skillCap && (BluntLanceBalistics * (weaponWeight + BraceBonus)) < skillCap) //skill based damage
                                    {
                                        magnitude = skillCap;
                                    }

                                    if ((BluntLanceBalistics * (weaponWeight + BraceBonus)) >= skillCap) //ballistics
                                    {
                                        magnitude = (BluntLanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > poplarBreakTreshold) // damage cap - lance break threshold
                                    {
                                        magnitude = poplarBreakTreshold;
                                    }
                                    magnitude *= 1f;
                                }
                                else
                                {
                                    if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                    {
                                        magnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }

                                    if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                    {
                                        magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                    {
                                        magnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }
                                }
                            }
                            else
                            {
                                //float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();
                                //float totalSpeed = (float)Math.Sqrt((magnitude * 2f) / 8f);
                                //skillBasedDamage = 0.5f * 15f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f));
                                ////skillBasedDamage = (magnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.26f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;

                                //if (skillBasedDamage > 240f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                //{
                                //    skillBasedDamage = 240 * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                                skillBasedDamage = magnitude;
                            }
                        }
                        if (magnitude > 0.15f && !isPassiveUsage)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
            }
            return magnitude;
        }

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
            float armorReduction = 100f / (100f + armorEffectiveness * RBMConfig.RBMConfig.armorMultiplier);
            float mag_1h_thrust;
            float mag_2h_thrust;
            float mag_1h_sword_thrust;
            float mag_2h_sword_thrust;

            if (damageType == DamageTypes.Pierce)
            {
                mag_1h_thrust = magnitude * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                mag_2h_thrust = magnitude * 1f * RBMConfig.RBMConfig.TwoHandedThrustDamageBonus;
                mag_1h_sword_thrust = magnitude * 1.0f * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                mag_2h_sword_thrust = magnitude * 1f * RBMConfig.RBMConfig.TwoHandedThrustDamageBonus;
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
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "ThrowingKnife":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedSword":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedSword":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedBastardAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "Mace":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
                        break;
                    }
                case "TwoHandedMace":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "Arrow":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
                        break;
                    }
                case "Bolt":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
                        break;
                    }
                case "Javelin":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "ThrowingAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "SlingStone":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
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

        private static float WeaponTypeDamage(RBMCombatConfigWeaponType weaponTypeFactors, float magnitude, float armorReduction, DamageTypes damageType, float armorEffectiveness, BasicCharacterObject player, bool isPlayerVictim, float weaponDamageFactor, out float penetratedDamage, out float bluntTraumaAfterArmor, float partialPenetrationThreshold = 2f)
        {
            float damage = 0f;
            float armorThresholdModifier = RBMConfig.RBMConfig.armorThresholdModifier / weaponDamageFactor;

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

                        float bluntTrauma = magnitude * (0.7f * RBMConfig.RBMConfig.maceBluntModifier) * bluntFraction;
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

                        float bluntTrauma = magnitude * (extraBluntFactorCut + RBMConfig.RBMConfig.bluntTraumaBonus) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (RBMConfig.RBMConfig.armorPenetrationMessage)
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
                        float partialPenetration = Math.Max(0f, magnitude - armorEffectiveness * partialPenetrationThreshold * armorThresholdModifier);
                        if (partialPenetration > 15f)
                        {
                            partialPenetration = 15f;
                        }
                        penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * extraArmorThresholdFactorPierce * armorThresholdModifier) - partialPenetration;
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - (penetratedDamage + partialPenetration)) / magnitude;
                        }
                        penetratedDamage += partialPenetration;
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (extraBluntFactorPierce + RBMConfig.RBMConfig.bluntTraumaBonus) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (RBMConfig.RBMConfig.armorPenetrationMessage)
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
