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
        public static int calculateMissileSpeed(float ammoWeight, string rangedWeaponType, int drawWeight)
        {
            int calculatedMissileSpeed = 10;
            switch (rangedWeaponType)
            {
                case "bow": // composite horn-sinew horsebow
                    {
                        float powerstroke = (25f * 0.0254f); // in inches then converted to metres
                        float materialEfficiency = 0.9f; // composite > wood > steel
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * materialEfficiency; // draw weight is in pounds and then multiplied to newton metres
                        float virtualArrow = drawWeight * 0.00015f;// virtual arrow - weight of limbs and string added to the weight of the arrow
                        ammoWeight += virtualArrow;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "long_bow":
                    {
                        float powerstroke = (25f * 0.0254f); // in inches then converted to metres
                        float materialEfficiency = 0.835f; // composite > wood > steel
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * materialEfficiency; // draw weight is in pounds and then multiplied to newton metres
                        float virtualArrow = drawWeight * 0.00018f;// virtual arrow - weight of limbs and string added to the weight of the arrow
                        ammoWeight += virtualArrow;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "crossbow": // composite horn-sinew crossbow
                case "crossbow_fast":
                    {
                        // Eastern / Chinese style crossbows
                        float powerstroke = (20f * 0.0254f); // in inches then converted to metres
                        float materialEfficiency = 0.88f; // composite bow + bit of drag
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * materialEfficiency; // draw weight is in pounds and then multiplied to newton metres
                        float virtualArrow = drawWeight * 0.00015f;// virtual arrow - weight of limbs and string added to the weight of the arrow
                        ammoWeight += virtualArrow;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;

                        //European crossbows
                        //float powerstroke = (8f * 0.0254f); // in inches then converted to metres
                        //float materialEfficiency = 0.85f; // composite > wood > steel
                        //double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * materialEfficiency; // draw weight is in pounds and then multiplied to newton metres
                        //float virtualArrow = drawWeight * 0.00014f;// virtual arrow - weight of limbs and string added to the weight of the arrow
                        //ammoWeight += virtualArrow;
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        //break;
                    }
                // case "Sling":
                // float weightModifier  = 730f * (1f + throwing skill / 100); takze pri 100 skille to bude * 2
                // float slingLengthModifier  = missile_speed (zo slingu, z tej equipnutej zbrane) * 0.01;
                // int calculatedThrowingSpeed = (int)Math.Ceiling(Math.Sqrt((MBMath.ClampFloat(ammoWeight * weightModifier * slingLengthModifier, 60f, 350f)) * 2f / ammoWeight));
                // return calculatedThrowingSpeed;
                // tie modifiery zo stitu a armoru ktore vplivaju na normalny throw sa mozu aplikovat aj tu
                case "osa_sling":
                    {
                        // 40 grams is added to the weight of projectiles, this results in 60 m/s at 80 grams with good sling, 70 m/s at 50 grams and some 80 ms at 30 grams
                        double potentialEnergy = 0.5f * (drawWeight * drawWeight) * 0.12f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + 0.04f)));
                        break;
                    }
                case "cla_musket":
                    {
                        // arquebus , ammo should weight 40g+, kinetic energy should be 1300-1750J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_flint_rifle":
                    {
                        // flintlock musket , ammo should weight 18g-25g, kinetic energy should be 2300-3000J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_pistol":
                    {
                        // early flintlock pistol , ammo should weight 14g, kinetic energy should be 700+J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_revolver":
                    {
                        // wild west revolver , ammo should weight 16.5g, kinetic energy should be 850+J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_cannon":
                    {
                        // early hand cannon , ammo should weight 50g, kinetic energy should be 300-500J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_bolt_rifle":
                    {
                        // early bolt action , ammo should weight 25g, kinetic energy should be 5000J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_bomb":
                    {
                        // Just a throw
                        double potentialEnergy = 150f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / ammoWeight));
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

        public static int calculateThrowableSpeed(float ammoWeight, float effectiveSkill)
        {
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
