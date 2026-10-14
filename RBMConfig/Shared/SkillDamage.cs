using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMConfig
{
    /// <summary>Skill-scaled blow magnitudes shared by RBMCombat, RBMStance and the auto-resolve.</summary>
    public static class SkillDamage
    {
        public static float GetPunchMagnitude(ArmorMaterialTypes gauntletMaterial, float gauntletWeight)
        {
            float magnitude = 1f;
            magnitude *= GetPunchMaterialFactor(gauntletMaterial);
            magnitude += gauntletWeight;
            return magnitude;
        }

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

        // Share of the weapon skill in a kick or bash; the rest is Athletics.
        public const float KickBashWeaponSkillShare = 0.3f;

        // Applied after the skill scaling, so it still holds where the unarmed clamp caps both.
        public const float KickDamageFactor = 1.2f;

        // Same blend RBMStance's AiKickBash.KickSkill uses.
        public static int GetKickBashSkill(Agent agent)
        {
            AgentStatCalculateModel model = MissionGameModels.Current.AgentStatCalculateModel;
            int athletics = model.GetEffectiveSkill(agent, DefaultSkills.Athletics);
            WeaponComponentData weapon = agent.WieldedWeapon.CurrentUsageItem;
            if (weapon == null || weapon.RelevantSkill == null)
            {
                return athletics;
            }
            int weaponSkill = model.GetEffectiveSkill(agent, weapon.RelevantSkill);
            return MathF.Round(athletics * (1f - KickBashWeaponSkillShare) + weaponSkill * KickBashWeaponSkillShare);
        }

        public static float GetKickMagnitude(ArmorMaterialTypes bootMaterial, float bootWeight)
        {
            return GetPunchMaterialFactor(bootMaterial) + bootWeight * 0.25f;
        }

        public static float GetShieldBashMagnitude(float shieldWeight)
        {
            return 0.4f + shieldWeight * 0.1f;
        }

        public static float GetWeaponBashMagnitude(float weaponWeight)
        {
            return GetPunchMaterialFactor(ArmorMaterialTypes.None) + weaponWeight * 0.1f;
        }

        private const float HandleHitFloor = 4f;

        // Below these the input magnitude is kept; polearm thrusts are on the ThrustMagnitudeModifier scale.
        private const float MinScaledMagnitude = 1f;
        private const float MinScaledPolearmMagnitude = 0.15f;

        private const float AshBreakThreshold = 430f;
        private const float PoplarBreakThreshold = 260f;

        public static float GetSkillBasedDamage(float magnitude, bool isPassiveUsage, string weaponType, DamageTypes damageType, float effectiveSkill, float skillModifier, StrikeType strikeType, float weaponWeight, bool isHandleHit = false)
        {
            float skillBasedDamage;
            switch (WeaponTypeKeys.NormalizeWeaponType(weaponType))
            {
                case "Dagger":
                case "OneHandedSword":
                case "ThrowingKnife":
                    if (damageType == DamageTypes.Cut)
                    {
                        skillBasedDamage = SkillClamp(magnitude + effectiveSkill * 0.133f, 5f, 15f, skillModifier) * 4.6f;
                    }
                    else if (damageType == DamageTypes.Blunt)
                    {
                        skillBasedDamage = (BluntClamp(magnitude + effectiveSkill * 0.075f, isHandleHit, 15f, 20f, skillModifier) * 4f) * 0.4f;
                    }
                    else if (strikeType == StrikeType.Swing)
                    {
                        skillBasedDamage = (SkillClamp(magnitude + effectiveSkill * 0.133f, 5f, 15f, skillModifier) * 4f) * RBMConfig.ThrustMagnitudeModifier;
                    }
                    else
                    {
                        return magnitude;
                    }
                    return magnitude > MinScaledMagnitude || isHandleHit ? skillBasedDamage : magnitude;

                case "TwoHandedSword":
                    if (damageType == DamageTypes.Cut)
                    {
                        skillBasedDamage = SkillClamp(magnitude + effectiveSkill * 0.199f, 12f, 20f, skillModifier) * 4.6f;
                    }
                    else if (damageType == DamageTypes.Blunt)
                    {
                        skillBasedDamage = (BluntClamp(magnitude + effectiveSkill * 0.112f, isHandleHit, 20f, 26f, skillModifier) * 4f) * 0.4f;
                    }
                    else if (strikeType == StrikeType.Swing)
                    {
                        skillBasedDamage = (SkillClamp(magnitude + effectiveSkill * 0.199f, 12f, 20f, skillModifier) * 4f) * RBMConfig.ThrustMagnitudeModifier;
                    }
                    else
                    {
                        return magnitude;
                    }
                    return magnitude > MinScaledMagnitude || isHandleHit ? skillBasedDamage : magnitude;

                case "OneHandedAxe":
                case "ThrowingAxe":
                    skillBasedDamage = damageType == DamageTypes.Blunt
                        ? (BluntClamp(magnitude + effectiveSkill * 0.075f, isHandleHit, 15f, 20f, skillModifier) * 4f) * 0.3f
                        : SkillClamp(magnitude + effectiveSkill * 0.1f, 10f, 18f, skillModifier) * 4.6f;
                    return magnitude > MinScaledMagnitude || isHandleHit ? skillBasedDamage : magnitude;

                case "TwoHandedAxe":
                    skillBasedDamage = damageType == DamageTypes.Blunt
                        ? (BluntClamp(magnitude + effectiveSkill * 0.112f, isHandleHit, 20f, 26f, skillModifier) * 4f) * 0.3f
                        : SkillClamp(magnitude + effectiveSkill * 0.15f, 15f, 24f, skillModifier) * 4.6f;
                    return magnitude > MinScaledMagnitude || isHandleHit ? skillBasedDamage : magnitude;

                case "Mace":
                    if (damageType == DamageTypes.Pierce || !(magnitude > MinScaledMagnitude))
                    {
                        return magnitude;
                    }
                    return SkillClamp(magnitude + effectiveSkill * 0.075f, 10f, 15f, skillModifier) * 4.6f;

                case "TwoHandedMace":
                    if (damageType == DamageTypes.Pierce || !(magnitude > MinScaledMagnitude))
                    {
                        return magnitude;
                    }
                    return SkillClamp(magnitude + effectiveSkill * 0.1125f, 15f, 22f, skillModifier) * 4.6f;

                case "unarmedAttack":
                    return SkillClamp(magnitude * (effectiveSkill * 0.2f), 1f, 10f, skillModifier) * 2f;

                case "OneHandedPolearm":
                    if (isPassiveUsage)
                    {
                        return damageType == DamageTypes.Cut ? magnitude : GetCouchedMagnitude(magnitude, damageType, effectiveSkill, weaponWeight);
                    }
                    if (damageType == DamageTypes.Cut)
                    {
                        skillBasedDamage = SkillClamp(magnitude + effectiveSkill * 0.1f, 15f, 24f, skillModifier) * 4f;
                    }
                    else if (damageType == DamageTypes.Blunt)
                    {
                        skillBasedDamage = (BluntClamp(magnitude + effectiveSkill * 0.075f, isHandleHit, 15f, 20f, skillModifier) * 4f) * 0.3f;
                    }
                    else
                    {
                        return magnitude;
                    }
                    return magnitude > MinScaledPolearmMagnitude || isHandleHit ? skillBasedDamage : magnitude;

                case "TwoHandedPolearm":
                    if (isPassiveUsage)
                    {
                        return damageType == DamageTypes.Cut ? magnitude : GetCouchedMagnitude(magnitude, damageType, effectiveSkill, weaponWeight);
                    }
                    if (damageType == DamageTypes.Cut)
                    {
                        skillBasedDamage = SkillClamp(magnitude + effectiveSkill * 0.1495f, 18f, 28f, skillModifier) * 4f;
                    }
                    else if (damageType == DamageTypes.Blunt)
                    {
                        skillBasedDamage = (BluntClamp(magnitude + effectiveSkill * 0.0975f, isHandleHit, 20f, 26f, skillModifier) * 4f) * 0.3f;
                    }
                    else
                    {
                        return magnitude;
                    }
                    return magnitude > MinScaledPolearmMagnitude || isHandleHit ? skillBasedDamage : magnitude;

                default:
                    return magnitude;
            }
        }

        private static float SkillClamp(float value, float minBase, float maxBase, float skillModifier)
        {
            return MBMath.ClampFloat(value, minBase * (1 + skillModifier), maxBase * (1 + (2 * skillModifier)));
        }

        private static float BluntClamp(float value, bool isHandleHit, float minBase, float maxBase, float skillModifier)
        {
            return MBMath.ClampFloat(value, isHandleHit ? HandleHitFloor : minBase * (1 + skillModifier), maxBase * (1 + (2 * skillModifier)));
        }

        // Couched lance or braced polearm, Pierce or Blunt.
        private static float GetCouchedMagnitude(float magnitude, DamageTypes damageType, float effectiveSkill, float weaponWeight)
        {
            float couchedSkill = 0.5f + effectiveSkill * 0.02f;
            float skillCap = 150f + effectiveSkill * 1.5f;
            float braceBonus = weaponWeight < 2.1f ? 0.5f : 0f;

            float lanceBallistics = magnitude / weaponWeight;
            float couchedMagnitude = lanceBallistics * (weaponWeight + couchedSkill + braceBonus);

            if (damageType == DamageTypes.Blunt)
            {
                // One-handed bonus for both classes; the two bonuses are equal.
                float bluntLanceBallistics = lanceBallistics * RBMConfig.OneHandedThrustDamageBonus;
                float bluntCouchedMagnitude = lanceBallistics * (weaponWeight + couchedSkill + braceBonus) * RBMConfig.OneHandedThrustDamageBonus;
                float bluntBallisticMagnitude = bluntLanceBallistics * (weaponWeight + braceBonus);

                float result = bluntCouchedMagnitude;
                if (bluntCouchedMagnitude > skillCap && bluntBallisticMagnitude < skillCap)
                {
                    result = skillCap;
                }
                if (bluntBallisticMagnitude >= skillCap)
                {
                    result = bluntBallisticMagnitude;
                }
                if (result > PoplarBreakThreshold)
                {
                    result = PoplarBreakThreshold;
                }
                return result;
            }

            float thrustSkillCap = skillCap * RBMConfig.ThrustMagnitudeModifier;
            float ballisticMagnitude = lanceBallistics * (weaponWeight + braceBonus);
            float thrustResult = couchedMagnitude;
            if (couchedMagnitude > thrustSkillCap && ballisticMagnitude < thrustSkillCap)
            {
                thrustResult = thrustSkillCap;
            }
            if (ballisticMagnitude >= thrustSkillCap)
            {
                thrustResult = ballisticMagnitude;
            }
            float thrustBreakThreshold = AshBreakThreshold * RBMConfig.ThrustMagnitudeModifier;
            if (thrustResult > thrustBreakThreshold)
            {
                thrustResult = thrustBreakThreshold;
            }
            return thrustResult;
        }
    }
}
