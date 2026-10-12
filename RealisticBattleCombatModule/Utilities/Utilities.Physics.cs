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
        // Thrust physics lives in RBMConfig/Shared/ThrustEnergy.cs (shared with the auto-resolve); these forward to it.
        public static float CalculateThrustSpeed(float weaponWeight, float inertia, float com)
        {
            return ThrustEnergy.CalculateThrustSpeed(weaponWeight, inertia, com);
        }

        /// <summary>The speed the body behind a thrust moves at: the weapon's thrust speed, but no faster than a median weapon of its class.</summary>
        public static float CalculateThrustBodySpeed(WeaponClass weaponClass, float thrustSpeed, float rawThrustSpeed)
        {
            return thrustSpeed * ThrustEnergy.GetBodySpeedFactor(weaponClass, rawThrustSpeed);
        }

        public static float CalculateThrustMagnitudeForOneHandedWeapon(float weaponWeight, float effectiveSkill, float thrustSpeed, float bodySpeed, float exraLinearSpeed, Agent.UsageDirection attackDirection)
        {
            bool isOverheadAttack = attackDirection == Agent.UsageDirection.AttackUp;
            return ThrustEnergy.OneHanded(weaponWeight, effectiveSkill, thrustSpeed, bodySpeed, exraLinearSpeed, isOverheadAttack) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
        }

        public static float CalculateThrustMagnitudeForTwoHandedWeapon(float weaponWeight, float effectiveSkill, float thrustSpeed, float bodySpeed, float exraLinearSpeed, Agent.UsageDirection attackDirection)
        {
            bool isOverheadAttack = attackDirection == Agent.UsageDirection.AttackUp;
            return ThrustEnergy.TwoHanded(weaponWeight, effectiveSkill, thrustSpeed, bodySpeed, exraLinearSpeed, isOverheadAttack) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
        }
    }
}
