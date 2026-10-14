using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMConfig
{
    /// <summary>Armor, penetration and trauma math shared by RBMCombat, RBMStance and the auto-resolve.</summary>
    public static class BlowDamage
    {
        // Damage type ComputeBlowDamage last settled on; RBMStance's crush-through reads it back.
        [ThreadStatic]
        public static DamageTypes? LastComputedDamageType;

        public const float BluntTraumaBase = 0.6f;

        // Kicks, bashes and punches ("unarmedAttack").
        public const float UnarmedBluntTraumaFactor = 0.75f;

        // Only these read a config row; anything else gets the base rule, so stray saved rows stay unused.
        private static readonly HashSet<string> FactorRowWeaponTypes = new HashSet<string>
        {
            "Dagger", "ThrowingKnife", "OneHandedSword", "TwoHandedSword", "OneHandedAxe", "TwoHandedAxe",
            "OneHandedPolearm", "LowGripPolearm", "TwoHandedPolearm", "Mace", "Pick", "TwoHandedMace",
            "Arrow", "Bolt", "Javelin", "ThrowingAxe", "SlingStone", "Stone",
        };

        // Scales the trauma of every damage type, never the penetration threshold.
        public static float GetTraumaMaterialFactor(ArmorMaterialTypes armorMaterial)
        {
            switch (armorMaterial)
            {
                case ArmorMaterialTypes.Chainmail:
                    return 1.2f;
                case ArmorMaterialTypes.Plate:
                    return 0.8f;
                default:
                    return 1f;
            }
        }

        // Undoes ThrustMagnitudeModifier for the thrust-scaled classes; applied to Pierce only.
        public static float GetThrustBonus(string weaponType)
        {
            switch (WeaponTypeKeys.NormalizeWeaponType(weaponType))
            {
                case "Dagger":
                case "ThrowingKnife":
                case "OneHandedSword":
                case "OneHandedPolearm":
                case "Mace":
                case "Javelin":
                case "ThrowingAxe":
                    return RBMConfig.OneHandedThrustDamageBonus;
                case "TwoHandedSword":
                case "TwoHandedPolearm":
                case "TwoHandedMace":
                    return RBMConfig.TwoHandedThrustDamageBonus;
                default:
                    return 1f;
            }
        }

        public static float RBMComputeDamage(string weaponType, DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio, out float penetratedDamage, out float traumaAfterArmor, float weaponDamageFactor = 1f, ArmorMaterialTypes armorMaterial = ArmorMaterialTypes.None)
        {
            armorEffectiveness *= RBMConfig.armorEffectivenessMultiplier;

            if (armorMaterial != ArmorMaterialTypes.None && armorMaterial != ArmorMaterialTypes.Plate
                && damageType == DamageTypes.Pierce && (weaponType == "Arrow" || weaponType == "Bolt"))
            {
                armorEffectiveness *= 0.5f;
            }

            float traumaScale = GetTraumaMaterialFactor(armorMaterial);
            if (weaponType == "unarmedAttack")
            {
                traumaScale *= UnarmedBluntTraumaFactor;
            }

            float armorReduction = 100f / (100f + armorEffectiveness * RBMConfig.armorMultiplier);

            if (damageType == DamageTypes.Pierce)
            {
                magnitude *= GetThrustBonus(weaponType);
            }

            // null = the base rule (WeaponTypeDamage's defaults).
            RBMCombatConfigWeaponType weaponTypeFactors = FactorRowWeaponTypes.Contains(weaponType)
                ? RBMConfig.getWeaponTypeFactors(WeaponTypeKeys.NormalizeWeaponType(weaponType))
                : null;
            float damage = WeaponTypeDamage(weaponTypeFactors, magnitude, armorReduction, damageType, armorEffectiveness, weaponDamageFactor, traumaScale, out penetratedDamage, out traumaAfterArmor);
            return damage * absorbedDamageRatio;
        }

        private static float WeaponTypeDamage(RBMCombatConfigWeaponType weaponTypeFactors, float magnitude, float armorReduction, DamageTypes damageType, float armorEffectiveness, float weaponDamageFactor, float traumaScale, out float penetratedDamage, out float traumaAfterArmor)
        {
            float extraArmorThresholdFactorCut = 1f;
            float extraArmorThresholdFactorPierce = 1f;
            float extraArmorThresholdFactorBlunt = 5f;
            float extraBluntFactorCut = 1f;
            float extraBluntFactorPierce = 1f;
            float extraBluntFactorBlunt = 1f;
            if (weaponTypeFactors != null)
            {
                extraArmorThresholdFactorCut = weaponTypeFactors.ExtraArmorThresholdFactorCut;
                extraArmorThresholdFactorPierce = weaponTypeFactors.ExtraArmorThresholdFactorPierce;
                extraArmorThresholdFactorBlunt = weaponTypeFactors.ExtraArmorThresholdFactorBlunt;
                extraBluntFactorCut = weaponTypeFactors.ExtraBluntFactorCut;
                extraBluntFactorPierce = weaponTypeFactors.ExtraBluntFactorPierce;
                extraBluntFactorBlunt = weaponTypeFactors.ExtraBluntFactorBlunt;
            }

            float thresholdFactor;
            float traumaCarry;
            switch (damageType)
            {
                case DamageTypes.Blunt:
                    thresholdFactor = extraArmorThresholdFactorBlunt;
                    traumaCarry = BluntTraumaBase * RBMConfig.maceBluntModifier * extraBluntFactorBlunt;
                    break;
                case DamageTypes.Cut:
                    thresholdFactor = extraArmorThresholdFactorCut;
                    traumaCarry = extraBluntFactorCut + RBMConfig.bluntTraumaBonus;
                    break;
                case DamageTypes.Pierce:
                    thresholdFactor = extraArmorThresholdFactorPierce;
                    traumaCarry = extraBluntFactorPierce + RBMConfig.bluntTraumaBonus;
                    break;
                default:
                    penetratedDamage = 0f;
                    traumaAfterArmor = 0f;
                    return 0f;
            }

            float armorThresholdModifier = RBMConfig.armorThresholdModifier / weaponDamageFactor;
            penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * thresholdFactor * armorThresholdModifier);
            float bluntFraction = 0f;
            if (magnitude > 0f)
            {
                bluntFraction = (magnitude - penetratedDamage) / magnitude;
            }

            float trauma = magnitude * traumaCarry * traumaScale * bluntFraction * RBMConfig.bluntTraumaMultiplier;
            traumaAfterArmor = Math.Max(0f, trauma * armorReduction);

            // 0f + keeps a -0 blow at +0.
            return 0f + penetratedDamage + traumaAfterArmor;
        }
    }
}
