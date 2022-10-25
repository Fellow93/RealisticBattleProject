using System;
using TaleWorlds.MountAndBlade;

namespace  RBMCombat
{
    public static class Utilities
    {
        public static int calculateMissileSpeed(float ammoWeight, MissionWeapon rangedWeapon, int drawWeight)
        {
            int calculatedMissileSpeed = 10;
            switch (rangedWeapon.CurrentUsageItem.ItemUsage)
            {
                case "bow":
                    {
                        float powerstroke = (25f * 0.0254f); //28f
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * 0.91f;
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.91f * ((ammoWeight * 3f) + 0.432f)));
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + (drawWeight * 0.00012f))));
                        ammoWeight += drawWeight * 0.00012f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "long_bow":
                    {
                        float powerstroke = (25f * 0.0254f); //30f
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * 0.89f;
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.89f * ((ammoWeight * 3.3f) + 0.33f) * (1f + (0.416f - (0.0026 * drawWeight)))));
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + (drawWeight * 0.00020f))));
                        ammoWeight += drawWeight * 0.00020f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "crossbow":
                case "crossbow_fast":
                    {
                        float powerstroke = (12f * 0.0254f); //4.5f
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * 0.7f;
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.45f));
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + (drawWeight * 0.0000588f))));
                        ammoWeight += drawWeight * 0.00013f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "osa_sling":
                    {
                        // 40 grams is added to the weight of projectiles, this results in 60 m/s at 80 grams with good sling, 70 m/s at 50 grams and some 80 ms at 30 grams
                        double potentialEnergy = 0.5f * (drawWeight * drawWeight) * 0.12f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + 0.04f)));
                        break;
                    }
                default:
                    {
                        calculatedMissileSpeed = 10;
                        break;
                    }
            }
            return calculatedMissileSpeed;
        }

        public static int calculateThrowableSpeed(float ammoWeight, int effectiveSkill)
        {
            int calculatedThrowingSpeed = (int)Math.Ceiling(Math.Sqrt((60f + effectiveSkill * 0.8f) * 2f / ammoWeight));
            if (calculatedThrowingSpeed > 25)
            {
                calculatedThrowingSpeed = 25;
            }
            return calculatedThrowingSpeed;
        }

        public static int assignThrowableMissileSpeed(MissionWeapon throwable, int correctiveMissileSpeed, int effectiveSkill)
        {
            float ammoWeight = throwable.GetWeight() / throwable.Amount;
            int calculatedThrowingSpeed = calculateThrowableSpeed(ammoWeight,effectiveSkill);
            //PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            //property.DeclaringType.GetProperty("MissileSpeed");
            //throwable.CurrentUsageIndex = index;
            calculatedThrowingSpeed += correctiveMissileSpeed;
            return calculatedThrowingSpeed;
            //property.SetValue(throwable.CurrentUsageItem, calculatedThrowingSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            //throwable.CurrentUsageIndex = 0;
        }

        public static int assignStoneMissileSpeed(MissionWeapon throwable)
        {
            //PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            //property.DeclaringType.GetProperty("MissileSpeed");
            //throwable.CurrentUsageIndex = index;
            //property.SetValue(throwable.CurrentUsageItem, 25, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            //throwable.CurrentUsageIndex = 0;
            return 25;
        }

        public static string GetConfigFilePath()
        {
            return System.IO.Path.Combine(GetConfigFolderPath(), "config.xml");
        }

        public static string GetConfigFolderPath()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
             "Mount and Blade II Bannerlord", "Configs", "RealisticBattleCombatModule");
        }
    }
}

