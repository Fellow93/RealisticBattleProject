using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    public static partial class MagnitudeChanges
    {
        public static float CalculateSweetSpotSwingMagnitude(EquipmentElement weapon, int weaponUsageIndex, int relevantSkill, out float sweetSpot)
        {
            float progressEffect = 1f;
            float sweetSpotMagnitude = -1f;
            sweetSpot = -1f;

            if (weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
            {
                float swingSpeed = (float)weapon.GetModifiedSwingSpeedForUsage(weaponUsageIndex) / 4.5454545f * progressEffect;

                int ef = relevantSkill;
                float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);
                switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                {
                    case WeaponClass.LowGripPolearm:
                    case WeaponClass.Mace:
                    case WeaponClass.OneHandedAxe:
                    case WeaponClass.OneHandedPolearm:
                    case WeaponClass.TwoHandedMace:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                            swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                            swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                            swingSpeed = swingSpeed * 0.75f * swingskillModifier * progressEffect;
                            break;
                        }
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.TwoHandedSword:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                            swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                            break;
                        }
                }
                float weaponWeight = weapon.Item.Weight;
                float weaponInertia = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia;
                float weaponCOM = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass;
                for (float currentSpot = 1f; currentSpot > 0.35f; currentSpot -= 0.01f)
                {
                    //float currentSpotMagnitude = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForSwing(currentSelectedChar, null, swingSpeed, currentSpot, weaponWeight,
                    //    weapon.Item, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex),
                    //    weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).GetRealWeaponLength(), weaponInertia, weaponCOM, 0f, false);
                    float currentSpotMagnitude = CombatStatCalculator.CalculateStrikeMagnitudeForSwing(swingSpeed, currentSpot, weaponWeight,
                            weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).GetRealWeaponLength(), weaponInertia, weaponCOM, 0f);
                    if (currentSpotMagnitude > sweetSpotMagnitude)
                    {
                        sweetSpotMagnitude = currentSpotMagnitude;
                        sweetSpot = currentSpot;
                    }
                }
            }
            return sweetSpotMagnitude;
        }

        public static float CalculateThrustMagnitude(EquipmentElement weapon, int weaponUsageIndex, int relevantSkill)
        {
            float progressEffect = 1f;
            float thrustMagnitude = -1f;

            if (weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
            {
                float thrustWeaponSpeed = (float)weapon.GetModifiedThrustSpeedForUsage(weaponUsageIndex) / 11.7647057f * progressEffect;

                int ef = relevantSkill;
                float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);

                float weaponWeight = weapon.Item.Weight;
                float weaponInertia = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia;
                float weaponCOM = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass;

                switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                {
                    case WeaponClass.LowGripPolearm:
                    case WeaponClass.Mace:
                    case WeaponClass.OneHandedAxe:
                    case WeaponClass.OneHandedPolearm:
                    case WeaponClass.TwoHandedMace:
                        {
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);

                            thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                            thrustWeaponSpeed = thrustWeaponSpeed * 0.75f * thrustskillModifier * progressEffect;
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                        {
                            float thrustskillModifier = 1f + (effectiveSkillDR / 800f);

                            thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                            thrustWeaponSpeed = thrustWeaponSpeed * 0.65f * thrustskillModifier * progressEffect;
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                        {
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);

                            thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                            thrustWeaponSpeed = thrustWeaponSpeed * 0.9f * thrustskillModifier * progressEffect;
                            break;
                        }
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.TwoHandedSword:
                        {
                            float thrustskillModifier = 1f + (effectiveSkillDR / 800f);

                            thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                            thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * progressEffect;
                            break;
                        }
                }

                switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                {
                    case WeaponClass.OneHandedPolearm:
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.Mace:
                    case WeaponClass.LowGripPolearm:
                        {
                            thrustMagnitude = Utilities.CalculateThrustMagnitudeForOneHandedWeapon(weaponWeight, effectiveSkillDR, thrustWeaponSpeed, 0f, Agent.UsageDirection.AttackDown);
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                    case WeaponClass.TwoHandedSword:
                    case WeaponClass.TwoHandedMace:
                        {
                            thrustMagnitude = Utilities.CalculateThrustMagnitudeForTwoHandedWeapon(weaponWeight, effectiveSkillDR, thrustWeaponSpeed, 0f, Agent.UsageDirection.AttackDown);
                            break;
                        }
                        //default:
                        //    {
                        //        thrustMagnitude = SandboxStrikeMagnitudeModel.CalculateStrikeMagnitudeForThrust(currentSelectedChar, null, thrustWeaponSpeed, weaponWeight, weapon.Item, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex), 0f, false);
                        //        break;
                        //    }
                }
            }
            return thrustMagnitude;
        }
    }
}
