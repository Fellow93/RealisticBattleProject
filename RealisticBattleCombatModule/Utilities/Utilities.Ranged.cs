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

        // Body lives in RBMConfig.MissileBallistics so the aim arc and RBMAI's reach gate use the same throw speed.
        public static int calculateThrowableSpeed(float ammoWeight, float effectiveSkill)
        {
            return MissileBallistics.ThrowableSpeed(ammoWeight, effectiveSkill);
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

        // Body lives in RBMConfig.MissileBallistics, as calculateThrowableSpeed.
        public static int assignThrowableMissileSpeed(float ammoWeight, int correctiveMissileSpeed, float effectiveSkill, float armorModifier, WeaponClass shieldType)
        {
            return MissileBallistics.ThrowSpeed(ammoWeight, correctiveMissileSpeed, effectiveSkill, armorModifier, shieldType);
        }

        // Body lives in RBMConfig.MissileBallistics so RBMAI's reach gate uses the same sling speed.
        public static int assignSlingMissileSpeed(float ammoWeight, int drawWeight, float effectiveSkill, float armorModifier, WeaponClass shieldType)
        {
            return MissileBallistics.SlingSpeed(ammoWeight, drawWeight, effectiveSkill, armorModifier, shieldType);
        }

        public static int assignStoneMissileSpeed(MissionWeapon throwable)
        {
            //PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            //property.DeclaringType.GetProperty("MissileSpeed");
            //throwable.CurrentUsageIndex = index;
            //property.SetValue(throwable.CurrentUsageItem, 25, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            //throwable.CurrentUsageIndex = 0;
            return MissileBallistics.StoneThrowSpeed;
            // mal by tam byt ten isty vzorec ako calculateThrowableSpeed v respektive to loadnut tie data
        }
    }
}
