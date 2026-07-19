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
        public static void CalculateVisualSpeeds(MissionWeapon weapon, WeaponData weaponData, WeaponClass weaponClass, float effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal)
        {
            swingSpeedReal = -1;
            thrustSpeedReal = -1;
            handlingReal = -1;
            if (!weapon.IsEmpty && weapon.Item != null)
            {
                int swingSpeed = weapon.GetModifiedSwingSpeedForCurrentUsage();
                int handling = weapon.GetModifiedHandlingForCurrentUsage();

                switch (weaponClass)
                {
                    case WeaponClass.LowGripPolearm:
                    case WeaponClass.Mace:
                    case WeaponClass.OneHandedAxe:
                    case WeaponClass.OneHandedPolearm:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 900f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 600f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Item.Weight, weaponData.TotalInertia, weaponData.CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.1f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                    case WeaponClass.TwoHandedMace:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 900f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 600f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Item.Weight, weaponData.TotalInertia, weaponData.CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.05f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 650f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 900f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 600f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.75f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Item.Weight, weaponData.TotalInertia, weaponData.CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 0.9f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.TwoHandedSword:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 650f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 700f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 600f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.9f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Item.Weight, weaponData.TotalInertia, weaponData.CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.15f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.9f) * handlingskillModifier);
                            break;
                        }
                }
            }
        }

        public static void CalculateVisualSpeeds(EquipmentElement weapon, int weaponUsageIndex, float effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal)
        {
            swingSpeedReal = -1;
            thrustSpeedReal = -1;
            handlingReal = -1;
            if (!weapon.IsEmpty && weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
            {
                int swingSpeed = weapon.GetModifiedSwingSpeedForUsage(weaponUsageIndex);
                int handling = weapon.GetModifiedHandlingForUsage(weaponUsageIndex);

                switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                {
                    case WeaponClass.LowGripPolearm:
                    case WeaponClass.Mace:
                    case WeaponClass.OneHandedAxe:
                    case WeaponClass.OneHandedPolearm:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 900f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 600f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.1f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                    case WeaponClass.TwoHandedMace:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 500f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 450f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.05f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 5f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 450f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 900f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 400f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.75f) * swingskillModifier);
                            thrustSpeedReal = MathF.Ceiling((weapon.GetModifiedThrustSpeedForUsage(weaponUsageIndex) * 0.9f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.TwoHandedSword:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 650f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 700f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 600f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.15f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.9f) * handlingskillModifier);
                            break;
                        }
                }
            }
        }
    }
}
