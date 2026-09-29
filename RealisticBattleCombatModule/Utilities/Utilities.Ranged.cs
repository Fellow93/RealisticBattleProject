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
        private static float SanitizeAmmoWeight(float ammoWeight)
        {
            return MissileBallistics.SanitizeAmmoWeight(ammoWeight);
        }

        // Body lives in RBMConfig.MissileBallistics so RBMAI's crossbow reach gate uses the same launch speed.
        public static int calculateMissileSpeed(float ammoWeight, string rangedWeaponType, int drawWeight)
        {
            return MissileBallistics.CalculateMissileSpeed(ammoWeight, rangedWeaponType, drawWeight);
        }

        public static int calculateThrowableSpeed(float ammoWeight, float effectiveSkill)
        {
            // Melee usages reach here with weight/0 = +Infinity, which already yields 0; treat a
            // weightless item (0 or 0/0 = NaN) the same instead of letting it become int.MinValue.
            if (!(ammoWeight > 0f) || float.IsInfinity(ammoWeight))
            {
                return 0;
            }
            int calculatedThrowingSpeed = (int)Math.Ceiling(Math.Sqrt((MBMath.ClampFloat(ammoWeight * 70f, 60f, 250f) + (effectiveSkill * 0.75f)) * 2f / ammoWeight));
            return calculatedThrowingSpeed;
        }

        public static int assignThrowableMissileSpeedForMenu(float ammoWeight, int correctiveMissileSpeed, float effectiveSkill)
        {
            //float ammoWeight = throwable.GetWeight() / throwable.Amount;
            int calculatedThrowingSpeed = calculateThrowableSpeed(ammoWeight, effectiveSkill);
            //PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            //property.DeclaringType.GetProperty("MissileSpeed");
            //throwable.CurrentUsageIndex = index;
            calculatedThrowingSpeed += correctiveMissileSpeed;
            return calculatedThrowingSpeed;
            //property.SetValue(throwable.CurrentUsageItem, calculatedThrowingSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            //throwable.CurrentUsageIndex = 0;
        }

        public static int assignThrowableMissileSpeed(float ammoWeight, int correctiveMissileSpeed, float effectiveSkill, float armorModifier, WeaponClass shieldType)
        {
            //float ammoWeight = throwable.GetWeight() / throwable.Amount;
            float shieldTypeModifier = 1f;
            float weightTraining = MBMath.ClampFloat(effectiveSkill * 0.001f, 0f, 0.2f); // until we have perk
            float equipmentWeightModifier = (float)Math.Sqrt(MBMath.ClampFloat(1f - (armorModifier * 0.005f) + weightTraining, 0.7f, 1f));
            switch (shieldType)
            {
                case WeaponClass.LargeShield:
                    {
                        shieldTypeModifier = 0.87f;
                        break;
                    }
                case WeaponClass.SmallShield:
                    {
                        shieldTypeModifier = 0.96f;
                        break;
                    }
            }
            int calculatedThrowingSpeed = (int)Math.Round(calculateThrowableSpeed(ammoWeight, effectiveSkill) * shieldTypeModifier * equipmentWeightModifier);
            //PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            //property.DeclaringType.GetProperty("MissileSpeed");
            //throwable.CurrentUsageIndex = index;
            calculatedThrowingSpeed += correctiveMissileSpeed;
            return calculatedThrowingSpeed;
            //property.SetValue(throwable.CurrentUsageItem, calculatedThrowingSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            //throwable.CurrentUsageIndex = 0;
        }

        public static int assignSlingMissileSpeed(float ammoWeight, int drawWeight, float effectiveSkill, float armorModifier, WeaponClass shieldType)
        {
            ammoWeight = SanitizeAmmoWeight(ammoWeight);
            // Shield penalty: a shield on the arm restricts the slinging motion.
            float shieldTypeModifier = 1f;
            switch (shieldType)
            {
                case WeaponClass.LargeShield:
                    shieldTypeModifier = 0.87f;
                    break;
                case WeaponClass.SmallShield:
                    shieldTypeModifier = 0.96f;
                    break;
            }

            // Armor on shoulders and arms reduces sling rotation speed, same as for throws.
            float weightTraining = MBMath.ClampFloat(effectiveSkill * 0.001f, 0f, 0.2f);
            float equipmentWeightModifier = (float)Math.Sqrt(MBMath.ClampFloat(1f - (armorModifier * 0.005f) + weightTraining, 0.7f, 1f));

            // From the design formula in calculateMissileSpeed:
            // weightModifier = 730 * (1 + skill/100)  → at 100 skill it doubles
            // slingLengthModifier = missile_speed * 0.01  (item MissileSpeed stat encodes cord length/quality)
            // KE = ammoWeight * weightModifier * slingLengthModifier, clamped to [60, 350] J
            // v = sqrt(2 * KE / ammoWeight)
            float weightModifier = 730f * (1f + (effectiveSkill / 100f));
            float slingLengthModifier = drawWeight * 0.01f;
            int calculatedSpeed = (int)Math.Ceiling(Math.Sqrt((MBMath.ClampFloat(ammoWeight * weightModifier * slingLengthModifier, 60f, 350f)) * 2f / ammoWeight));

            return (int)Math.Round(calculatedSpeed * shieldTypeModifier * equipmentWeightModifier);
        }

        public static int assignStoneMissileSpeed(MissionWeapon throwable)
        {
            //PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            //property.DeclaringType.GetProperty("MissileSpeed");
            //throwable.CurrentUsageIndex = index;
            //property.SetValue(throwable.CurrentUsageItem, 25, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            //throwable.CurrentUsageIndex = 0;
            return 25;
            // mal by tam byt ten isty vzorec ako calculateThrowableSpeed v respektive to loadnut tie data
        }
    }
}
