using System;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMConfig
{
    /// <summary>
    /// Melee thrust physics shared by RBMCombat (the live blow and the weapon tooltip) and RBMCampaign
    /// (the auto-resolve). Returns raw thrust energy: RBMCombat scales it by ThrustMagnitudeModifier,
    /// the auto-resolve works in the unscaled energy. The CraftingEditor's thrustMag1H/2H mirror this file.
    ///
    ///     energy = ½ × bodyStrength × bodySpeed² + ½ × weaponWeight × (thrustSpeed + extraLinearSpeed)²
    ///     cap    = min(½ × bodyStrength × bodySpeed² × 1.5, 180 one-handed / 250 two-handed)
    ///
    /// The body behind a thrust moves at body speed, whatever it holds: a lighter shaft does not carry the
    /// wielder's arm and body faster. So the body term runs at the weapon's thrust speed only up to the
    /// speed of a median weapon of its class (<see cref="GetBodySpeedFactor"/>); only the shaft's own term
    /// keeps the light weapon's faster point. Before this, a 0.6 kg pitchfork out-thrust every spear, pike
    /// and menavlion against heavy armor, where its whole (unpenetrating) energy became blunt trauma.
    /// </summary>
    public static class ThrustEnergy
    {
        public const float OneHandedArmStrength = 2.5f;
        public const float TwoHandedArmStrength = 5f;

        public const float OneHandedSpeedCap = 9f;
        public const float TwoHandedSpeedCap = 6f;

        public const float OneHandedEnergyCap = 180f;
        public const float TwoHandedEnergyCap = 250f;

        /// <summary>
        /// RBM's thrust speed of a weapon (m/s, before the class, skill and progress factors): the time the arm
        /// takes to push the weapon's mass, plus the inertia around the grip, through three layers of the thrust.
        /// </summary>
        public static float CalculateThrustSpeed(float weaponWeight, float inertia, float centerOfMass)
        {
            float inertiaAroundGrip = inertia + weaponWeight * centerOfMass * centerOfMass;
            double mass = 1.8 + (double)weaponWeight + (double)inertiaAroundGrip * 0.2;
            double time1 = SimulateThrustLayer(0.6, 250.0, 48.0, 4.0 + mass);
            double time2 = SimulateThrustLayer(0.6, 170.0, 24.0, 2.0 + mass);
            double time3 = SimulateThrustLayer(0.6, 90.0, 15.0, 0.5 + mass);
            double meanTime = 0.33 * (time1 + time2 + time3);
            return (float)(3.8500000000000005 / meanTime);
        }

        private static double SimulateThrustLayer(double distance, double usablePower, double maxUsableForce, double mass)
        {
            double travelled = 0.0;
            double speed = 0.01;
            double time = 0.0;
            while (travelled < distance)
            {
                double force = usablePower / speed;
                if (force > maxUsableForce)
                {
                    force = maxUsableForce;
                }
                speed += 0.01 * force / mass;
                travelled += speed * 0.01;
                time += 0.01;
            }
            return time;
        }

        /// <summary>
        /// The median <see cref="CalculateThrustSpeed"/> of the thrusting weapons of each class in RBM's item
        /// files (2026-10-12, from tools/CraftingEditor). 0 = no reference: the body runs at the weapon's speed.
        /// </summary>
        public static float GetReferenceThrustSpeed(WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case WeaponClass.Dagger:
                    return 8.58f;
                case WeaponClass.OneHandedSword:
                    return 8.05f;
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    return 7.73f;
                case WeaponClass.Mace:
                    return 8.64f;
                case WeaponClass.TwoHandedPolearm:
                    return 7.20f;
                case WeaponClass.TwoHandedSword:
                    return 7.94f;
                case WeaponClass.TwoHandedMace:
                    return 7.58f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Body speed over the weapon's thrust speed: 1 for a weapon no faster than its class median, below 1
        /// for a faster one. The class, skill and progress factors are the same for both speeds, so they cancel
        /// and the ratio of the raw <see cref="CalculateThrustSpeed"/> values is enough.
        /// </summary>
        public static float GetBodySpeedFactor(WeaponClass weaponClass, float rawThrustSpeed)
        {
            float reference = GetReferenceThrustSpeed(weaponClass);
            if (reference <= 0f || rawThrustSpeed <= reference)
            {
                return 1f;
            }
            return reference / rawThrustSpeed;
        }

        public static float OneHanded(float weaponWeight, float effectiveSkill, float thrustSpeed, float bodySpeed, float extraLinearSpeed, bool isOverheadAttack)
        {
            if (isOverheadAttack)
            {
                thrustSpeed *= 1.33f;
                bodySpeed *= 1.33f;
            }
            thrustSpeed = Math.Min(thrustSpeed, OneHandedSpeedCap);
            bodySpeed = Math.Min(bodySpeed, thrustSpeed);
            float armStrength = isOverheadAttack ? OneHandedArmStrength - 1f : OneHandedArmStrength;
            return Energy(weaponWeight, effectiveSkill, armStrength, thrustSpeed, bodySpeed, extraLinearSpeed, OneHandedEnergyCap);
        }

        public static float TwoHanded(float weaponWeight, float effectiveSkill, float thrustSpeed, float bodySpeed, float extraLinearSpeed, bool isOverheadAttack)
        {
            if (isOverheadAttack)
            {
                thrustSpeed += 1f;
                bodySpeed += 1f;
            }
            thrustSpeed = Math.Min(thrustSpeed, TwoHandedSpeedCap);
            bodySpeed = Math.Min(bodySpeed, thrustSpeed);
            float armStrength = isOverheadAttack ? TwoHandedArmStrength - 1f : TwoHandedArmStrength;
            return Energy(weaponWeight, effectiveSkill, armStrength, thrustSpeed, bodySpeed, extraLinearSpeed, TwoHandedEnergyCap);
        }

        // The weapon's weight is only in the shaft term. The one-handed thrust used to add it to the body's
        // strength as well, counting it twice (and raising the cap with it).
        private static float Energy(float weaponWeight, float effectiveSkill, float armStrength, float thrustSpeed, float bodySpeed, float extraLinearSpeed, float energyCap)
        {
            float skillModifier = MBMath.ClampFloat(effectiveSkill / 250f, 0f, 1f) * 2f;
            float bodyStrength = armStrength * (1f + skillModifier);
            float bodyEnergy = 0.5f * bodyStrength * (bodySpeed * bodySpeed);
            float combinedSpeed = thrustSpeed + extraLinearSpeed;
            float shaftEnergy = 0.5f * weaponWeight * (combinedSpeed * combinedSpeed);
            float cap = MBMath.ClampFloat(bodyEnergy * 1.5f, 0f, energyCap);
            return Math.Min(bodyEnergy + shaftEnergy, cap);
        }
    }
}
