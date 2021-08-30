using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RealisticBattleCombatModule
{
    public static class Utilities
    {
        public static int calculateMissileSpeed(float ammoWeight, MissionWeapon rangedWeapon, int drawWeight)
        {
            int calculatedMissileSpeed = 10;
            if (rangedWeapon.CurrentUsageItem.ItemUsage.Equals("bow"))
            {
                float powerstroke = (25f * 0.0254f); //28f
                double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * 0.91f;
                //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.91f * ((ammoWeight * 3f) + 0.432f)));
                //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + (drawWeight * 0.00012f))));
                ammoWeight += drawWeight * 0.00012f;
                calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
            }
            else if (rangedWeapon.CurrentUsageItem.ItemUsage.Equals("long_bow"))
            {
                float powerstroke = (25f * 0.0254f); //30f
                double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * 0.89f;
                //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.89f * ((ammoWeight * 3.3f) + 0.33f) * (1f + (0.416f - (0.0026 * drawWeight)))));
                //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + (drawWeight * 0.00020f))));
                ammoWeight += drawWeight * 0.00020f;
                calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
            }
            else if (rangedWeapon.CurrentUsageItem.ItemUsage.Equals("crossbow") || rangedWeapon.CurrentUsageItem.ItemUsage.Equals("crossbow_fast"))
            {
                float powerstroke = (6f * 0.0254f); //4.5f
                double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * 0.91f;
                //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.45f));
                //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + (drawWeight * 0.0000588f))));
                ammoWeight += drawWeight * 0.0000588f;
                calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
            }
            return calculatedMissileSpeed;
        }

        public static int calculateThrowableSpeed(float ammoWeight)
        {
            int calculatedThrowingSpeed = (int)Math.Ceiling(Math.Sqrt(200f * 2f / ammoWeight));
            if (calculatedThrowingSpeed > 22)
            {
                calculatedThrowingSpeed = 22;
            }
            return calculatedThrowingSpeed;
        }

        public static void assignThrowableMissileSpeed(MissionWeapon throwable, int index, int correctiveMissileSpeed)
        {
            float ammoWeight = throwable.GetWeight() / throwable.Amount;
            int calculatedThrowingSpeed = calculateThrowableSpeed(ammoWeight);
            PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            property.DeclaringType.GetProperty("MissileSpeed");
            throwable.CurrentUsageIndex = index;
            calculatedThrowingSpeed += correctiveMissileSpeed;
            property.SetValue(throwable.CurrentUsageItem, calculatedThrowingSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            throwable.CurrentUsageIndex = 0;
        }

        public static void assignStoneMissileSpeed(MissionWeapon throwable, int index)
        {
            PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            property.DeclaringType.GetProperty("MissileSpeed");
            throwable.CurrentUsageIndex = index;
            property.SetValue(throwable.CurrentUsageItem, 25, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            throwable.CurrentUsageIndex = 0;
        }
    }
}

