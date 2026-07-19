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
        public static void SimulateThrustLayer(double distance, double usablePower, double maxUsableForce, double mass, out double finalSpeed, out double finalTime)
        {
            double num = 0.0;
            double num2 = 0.01;
            double num3 = 0.0;
            while (num < distance)
            {
                double num4 = usablePower / num2;
                if (num4 > maxUsableForce)
                {
                    num4 = maxUsableForce;
                }
                double num5 = 0.01 * num4 / mass;
                num2 += num5;
                num += num2 * 0.01;
                num3 += 0.01;
            }
            finalSpeed = num2;
            finalTime = num3;
        }

        public static float CalculateThrustSpeed(float _currentWeaponWeight, float inertia, float com)
        {
            float _currentWeaponInertiaAroundGrip = inertia + _currentWeaponWeight * com * com;
            double num = 1.8 + (double)_currentWeaponWeight + (double)_currentWeaponInertiaAroundGrip * 0.2;
            double num2 = 170.0;
            double num3 = 90.0;
            double num4 = 24.0;
            double num5 = 15.0;
            //if (_weaponDescription.WeaponFlags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.NotUsableWithOneHand) && !_weaponDescription.WeaponFlags.HasAnyFlag(WeaponFlags.WideGrip))
            //{
            //    num += 0.6;
            //    num5 *= 1.9;
            //    num4 *= 1.1;
            //    num3 *= 1.2;
            //    num2 *= 1.05;
            //}
            //else if (_weaponDescription.WeaponFlags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.NotUsableWithOneHand | WeaponFlags.WideGrip))
            //{
            //    num += 0.9;
            //    num5 *= 2.1;
            //    num4 *= 1.2;
            //    num3 *= 1.2;
            //    num2 *= 1.05;
            //}
            SimulateThrustLayer(0.6, 250.0, 48.0, 4.0 + num, out var finalSpeed, out var finalTime);
            SimulateThrustLayer(0.6, num2, num4, 2.0 + num, out var finalSpeed2, out var finalTime2);
            SimulateThrustLayer(0.6, num3, num5, 0.5 + num, out var finalSpeed3, out var finalTime3);
            double num6 = 0.33 * (finalTime + finalTime2 + finalTime3);
            return (float)(3.8500000000000005 / num6);
        }

        public const float oneHandedPolearmThrustStrength = 2.5f;
        public const float twoHandedPolearmThrustStrength = 5f;

        public static float CalculateThrustMagnitudeForOneHandedWeapon(float weaponWeight, float effectiveSkill, float thrustSpeed, float exraLinearSpeed, Agent.UsageDirection attackDirection)
        {
            float magnitude = 0f;

            bool isOverheadAttack = attackDirection == Agent.UsageDirection.AttackUp;

            thrustSpeed = (isOverheadAttack ? thrustSpeed * 1.33f : thrustSpeed);
            if (thrustSpeed > 9f)
            {
                thrustSpeed = 9f;
            }
            float combinedSpeed = thrustSpeed + exraLinearSpeed;
            float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill) * 2f;

            float spearKineticEnergy = 0.5f * weaponWeight * (combinedSpeed * combinedSpeed);

            float armStrength = isOverheadAttack ? oneHandedPolearmThrustStrength - 1f : oneHandedPolearmThrustStrength;

            float thrustStrength = weaponWeight + (armStrength * (1f + skillModifier));
            float thrustStrengthWithWeaponWeight = weaponWeight + (armStrength * (1f + skillModifier));

            float thrustEnergyCap = MathF.Clamp(0.5f * thrustStrength * (thrustSpeed * thrustSpeed) * 1.5f, 0f, 180f);
            float thrustEnergy = (0.5f * thrustStrength * (thrustSpeed * thrustSpeed)) + (0.5f * weaponWeight * (combinedSpeed * combinedSpeed));
            //float thrustEnergy = 0.5f * thrustStrengthWithWeaponWeight * (combinedSpeed * combinedSpeed);
            if (thrustEnergy > thrustEnergyCap)
            {
                thrustEnergy = thrustEnergyCap;
            }

            magnitude = thrustEnergy;

            if (spearKineticEnergy > magnitude)
            {
                magnitude = spearKineticEnergy;
            }

            if (magnitude > thrustEnergyCap)
            {
                magnitude = thrustEnergyCap;
            }

            return magnitude * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
        }

        public static float CalculateThrustMagnitudeForTwoHandedWeapon(float weaponWeight, float effectiveSkill, float thrustSpeed, float exraLinearSpeed, Agent.UsageDirection attackDirection)
        {
            float magnitude = 0f;

            bool isOverheadAttack = attackDirection == Agent.UsageDirection.AttackUp;
            thrustSpeed = (isOverheadAttack ? thrustSpeed + 1f : thrustSpeed);
            if (thrustSpeed > 6f)
            {
                thrustSpeed = 6f;
            }
            float combinedSpeed = thrustSpeed + exraLinearSpeed;
            float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill) * 2f;

            float spearKineticEnergy = 0.5f * weaponWeight * (combinedSpeed * combinedSpeed);

            float armStrength = isOverheadAttack ? twoHandedPolearmThrustStrength - 1f : twoHandedPolearmThrustStrength;

            float thrustStrength = armStrength * (1f + skillModifier);
            float thrustStrengthWithWeaponWeight = weaponWeight + (armStrength * (1f + skillModifier));

            float thrustEnergyCap = MathF.Clamp(0.5f * thrustStrength * (thrustSpeed * thrustSpeed) * 1.5f, 0f, 250f);

            float thrustEnergy = (0.5f * thrustStrength * (thrustSpeed * thrustSpeed)) + (0.5f * weaponWeight * (combinedSpeed * combinedSpeed));
            //float thrustEnergy = 0.5f * thrustStrengthWithWeaponWeight * (combinedSpeed * combinedSpeed);
            if (thrustEnergy > thrustEnergyCap)
            {
                thrustEnergy = thrustEnergyCap;
            }

            magnitude = thrustEnergy;

            if (spearKineticEnergy > magnitude)
            {
                magnitude = spearKineticEnergy;
            }

            if (magnitude > thrustEnergyCap)
            {
                magnitude = thrustEnergyCap;
            }

            return magnitude * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
        }
    }
}
