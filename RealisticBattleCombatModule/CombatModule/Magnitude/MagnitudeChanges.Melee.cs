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
        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("CalculateBaseMeleeBlowMagnitude")]
        public class CalculateBaseMeleeBlowMagnitudePatch
        {
            public static bool Prefix(ref float __result, in AttackInformation attackInformation, StrikeType strikeType, float progressEffect, float impactPointAsPercent, float exraLinearSpeed)
            {
                MissionWeapon weapon = attackInformation.AttackerWeapon;
                WeaponComponentData currentUsageItem = weapon.CurrentUsageItem;
                WeaponClass weaponClass = currentUsageItem.WeaponClass;

                if (strikeType == StrikeType.Thrust)
                {
                    if (progressEffect < 0.1f)
                    {
                        progressEffect = 0.1f;
                    }
                }
                else
                {
                    if (progressEffect < 0.5f)
                    {
                        progressEffect = 0.5f;
                    }
                }
                float accelerationProgress = MathF.Sqrt(progressEffect);
                if (strikeType == StrikeType.Thrust)
                {
                    float thrustWeaponSpeed = (float)weapon.GetModifiedThrustSpeedForCurrentUsage() / 11.7647057f * accelerationProgress;

                    if (weapon.Item != null && weapon.CurrentUsageItem != null)
                    {
                        Agent attacker = attackInformation.AttackerAgent;

                        if (attacker != null)
                        {
                            bool isNonTipHit = false;

                            if (weaponClass == WeaponClass.OneHandedSword ||
                                weaponClass == WeaponClass.Dagger ||
                                weaponClass == WeaponClass.TwoHandedSword)
                            {
                                //if full blade weapon change things elsewhere
                            }
                            else
                            {
                                if (attacker.AttackDirection == Agent.UsageDirection.AttackUp)
                                {
                                    if (impactPointAsPercent < 0.9f)
                                    {
                                        isNonTipHit = true;
                                    }
                                }
                                else if (attacker.AttackDirection == Agent.UsageDirection.AttackDown)
                                {
                                    if (impactPointAsPercent < 0.7f)
                                    {
                                        isNonTipHit = true;
                                    }
                                }
                            }

                            SkillObject skill = weapon.CurrentUsageItem.RelevantSkill;
                            int ef = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attacker, skill);
                            float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);
                            switch (weapon.CurrentUsageItem.WeaponClass)
                            {
                                case WeaponClass.LowGripPolearm:
                                case WeaponClass.Mace:
                                case WeaponClass.OneHandedAxe:
                                case WeaponClass.OneHandedPolearm:
                                case WeaponClass.TwoHandedMace:
                                    {
                                        float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.TotalInertia, weapon.CurrentUsageItem.CenterOfMass);
                                        thrustWeaponSpeed = thrustWeaponSpeed * 0.75f * thrustskillModifier * accelerationProgress;
                                        break;
                                    }
                                case WeaponClass.TwoHandedPolearm:
                                    {
                                        float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                                        float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.TotalInertia, weapon.CurrentUsageItem.CenterOfMass);
                                        thrustWeaponSpeed = thrustWeaponSpeed * 0.65f * thrustskillModifier * accelerationProgress;
                                        break;
                                    }
                                case WeaponClass.TwoHandedAxe:
                                    {
                                        float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                                        float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.TotalInertia, weapon.CurrentUsageItem.CenterOfMass);
                                        thrustWeaponSpeed = thrustWeaponSpeed * 0.9f * thrustskillModifier * accelerationProgress;
                                        break;
                                    }
                                case WeaponClass.OneHandedSword:
                                case WeaponClass.Dagger:
                                case WeaponClass.TwoHandedSword:
                                    {
                                        float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                                        float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                                        float handlingskillModifier = 1f + (effectiveSkillDR / 800f);

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.TotalInertia, weapon.CurrentUsageItem.CenterOfMass);
                                        thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * accelerationProgress;
                                        break;
                                    }
                            }

                            float thrustMagnitude = 0f;
                            switch (weapon.CurrentUsageItem.WeaponClass)
                            {
                                case WeaponClass.OneHandedPolearm:
                                case WeaponClass.OneHandedSword:
                                case WeaponClass.Dagger:
                                case WeaponClass.Mace:
                                case WeaponClass.LowGripPolearm:
                                    {
                                        thrustMagnitude = Utilities.CalculateThrustMagnitudeForOneHandedWeapon(weapon.Item.Weight, effectiveSkillDR, thrustWeaponSpeed, exraLinearSpeed, attacker.AttackDirection);
                                        break;
                                    }
                                case WeaponClass.TwoHandedPolearm:
                                case WeaponClass.TwoHandedSword:
                                case WeaponClass.TwoHandedMace:
                                    {
                                        thrustMagnitude = Utilities.CalculateThrustMagnitudeForTwoHandedWeapon(weapon.Item.Weight, effectiveSkillDR, thrustWeaponSpeed, exraLinearSpeed, attacker.AttackDirection);
                                        break;
                                    }
                                    //default:
                                    //    {
                                    //        thrustMagnitude = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForThrust(attackerAgentCharacter, attackerCaptainCharacter, thrustWeaponSpeed, weapon.Item.Weight, weapon.Item, currentUsageItem, exraLinearSpeed, doesAttackerHaveMount);
                                    //        break;
                                    //    }
                            }
                            //InformationManager.DisplayMessage(new InformationMessage("isNonTipHit : " + isNonTipHit, Color.FromUint(4289612505u)));

                            if (isNonTipHit)
                            {
                                thrustMagnitude *= 0.2f;
                            }
                            __result = thrustMagnitude;
                            return false;
                        }
                    }
                    return true;
                    //__result = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForThrust(attackerAgentCharacter, attackerCaptainCharacter, thrustWeaponSpeed, weapon.Item.Weight, weapon.Item, currentUsageItem, exraLinearSpeed, doesAttackerHaveMount);
                    //return false;
                }
                float swingSpeed = (float)weapon.GetModifiedSwingSpeedForCurrentUsage() / 4.5454545f * accelerationProgress;

                if (weapon.Item != null && weapon.CurrentUsageItem != null)
                {
                    Agent attacker = attackInformation.AttackerAgent;
                    SkillObject skill = weapon.CurrentUsageItem.RelevantSkill;
                    int ef = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attacker, skill);
                    float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);
                    switch (weapon.CurrentUsageItem.WeaponClass)
                    {
                        case WeaponClass.LowGripPolearm:
                        case WeaponClass.Mace:
                        case WeaponClass.OneHandedAxe:
                        case WeaponClass.OneHandedPolearm:
                        case WeaponClass.TwoHandedMace:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * accelerationProgress;
                                break;
                            }
                        case WeaponClass.TwoHandedPolearm:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 1000f);

                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * accelerationProgress;
                                break;
                            }
                        case WeaponClass.TwoHandedAxe:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                                swingSpeed = swingSpeed * 0.75f * swingskillModifier * accelerationProgress;
                                break;
                            }
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.Dagger:
                        case WeaponClass.TwoHandedSword:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * accelerationProgress;
                                break;
                            }
                    }
                }

                float num2 = MBMath.ClampFloat(0.4f / currentUsageItem.GetRealWeaponLength(), 0f, 1f);
                float num3 = MathF.Min(1f, impactPointAsPercent);
                float num4 = MathF.Min(1f, impactPointAsPercent + num2);
                //float originalValue = 0f;
                float newValue = 0f;
                int j = 0;
                //for (int i = 0; i < 5; i++)
                //{
                //    //float bladeLength = weapon.Item.WeaponDesign.UsedPieces[0].ScaledBladeLength;
                //    float impactPointAsPercent2 = num3 + (float)i / 4f * (num4 - num3);
                //    float num6 = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForSwing(attackerAgentCharacter, attackerCaptainCharacter, swingSpeed, impactPointAsPercent2, weapon.Item.Weight, currentUsageItem, currentUsageItem.GetRealWeaponLength(), currentUsageItem.TotalInertia, currentUsageItem.CenterOfMass, exraLinearSpeed, doesAttackerHaveMount);
                //    if (originalValue < num6)
                //    {
                //        originalValue = num6;
                //    }
                //}

                float impactPointAsPercent3 = num3 + (float)0 / 4f * (num4 - num3);
                //newValue = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForSwing(attackerAgentCharacter, attackerCaptainCharacter, swingSpeed, impactPointAsPercent3, weapon.Item.Weight, weapon.Item, currentUsageItem, currentUsageItem.GetRealWeaponLength(), currentUsageItem.TotalInertia, currentUsageItem.CenterOfMass, exraLinearSpeed, doesAttackerHaveMount);
                newValue = CombatStatCalculator.CalculateStrikeMagnitudeForSwing(swingSpeed, impactPointAsPercent3, weapon.Item.Weight,
                            currentUsageItem.GetRealWeaponLength(), currentUsageItem.TotalInertia, currentUsageItem.CenterOfMass, exraLinearSpeed);
                __result = newValue;
                return false;
            }
        }
    }
}
