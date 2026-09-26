using TaleWorlds.Core;
using TaleWorlds.Library;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMConfig
{
    /// <summary>
    /// Melee damage math shared by RBMCombat (the real blow) and RBMAI (the posture
    /// crush-through HP estimate). Lives here because RBMConfig is the one project both
    /// reference; RBMAI does not reference RBMCombat. RBMCombat's behaviour is the source
    /// of truth for everything in this file.
    /// </summary>
    public static class SkillDamage
    {
        /// <summary>
        /// Base magnitude of an unarmed punch before the skill scaling in
        /// <see cref="GetSkillBasedDamage"/>: 1 x a factor for the attacker's arm-armor
        /// material, plus half the gauntlet's weight.
        /// </summary>
        public static float GetPunchMagnitude(ArmorMaterialTypes gauntletMaterial, float gauntletWeight)
        {
            float magnitude = 1f;
            magnitude *= GetPunchMaterialFactor(gauntletMaterial);
            magnitude += gauntletWeight;
            return magnitude;
        }

        /// <summary>
        /// Magnitude factor of a punch by the attacker's arm-armor material. Materials not
        /// listed leave the magnitude unchanged.
        /// </summary>
        public static float GetPunchMaterialFactor(ArmorMaterialTypes gauntletMaterial)
        {
            switch (gauntletMaterial)
            {
                case ArmorMaterialTypes.None:
                    {
                        return 0.3f;
                    }
                case ArmorMaterialTypes.Cloth:
                    {
                        return 0.4f;
                    }
                case ArmorMaterialTypes.Leather:
                    {
                        return 0.4f;
                    }
                case ArmorMaterialTypes.Chainmail:
                    {
                        return 0.75f;
                    }
                case ArmorMaterialTypes.Plate:
                    {
                        return 1f;
                    }
            }
            return 1f;
        }

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
                                skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.133f), 5f * (1 + skillModifier), 15f * (1 + (2 * skillModifier))) * 4f) * RBMConfig.ThrustMagnitudeModifier;
                            }
                            else
                            {
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
                                skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.199f), 12f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * RBMConfig.ThrustMagnitudeModifier;
                            }
                            else
                            {
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
                            skillBasedDamage = magnitude;
                        }
                        else
                        {
                            float value = magnitude + (effectiveSkill * 0.075f);
                            float min = 10f * (1 + skillModifier);
                            float max = 15f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4.6f);
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
                            skillBasedDamage = magnitude;
                        }
                        else
                        {
                            float value = magnitude + (effectiveSkill * 0.1125f);
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
                                float BluntLanceBalistics = ((magnitude * BraceModifier) / weaponWeight) * RBMConfig.OneHandedThrustDamageBonus;
                                float BluntCouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus) * RBMConfig.OneHandedThrustDamageBonus;
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
                                    if (CouchedMagnitude > (skillCap * RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                    {
                                        magnitude = skillCap * RBMConfig.ThrustMagnitudeModifier;
                                    }

                                    if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                    {
                                        magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > (ashBreakTreshold * RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                    {
                                        magnitude = ashBreakTreshold * RBMConfig.ThrustMagnitudeModifier;
                                    }
                                }
                            }
                            else
                            {
                                skillBasedDamage = magnitude;
                            }
                        }
                        if (magnitude > 0.15f && !isPassiveUsage)
                        {
                            magnitude = skillBasedDamage;
                        }
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
                                float BluntLanceBalistics = ((magnitude * BraceModifier) / weaponWeight) * RBMConfig.OneHandedThrustDamageBonus;
                                float BluntCouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus) * RBMConfig.OneHandedThrustDamageBonus;
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
                                    if (CouchedMagnitude > (skillCap * RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                    {
                                        magnitude = skillCap * RBMConfig.ThrustMagnitudeModifier;
                                    }

                                    if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                    {
                                        magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > (ashBreakTreshold * RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                    {
                                        magnitude = ashBreakTreshold * RBMConfig.ThrustMagnitudeModifier;
                                    }
                                }
                            }
                            else
                            {
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
    }
}
