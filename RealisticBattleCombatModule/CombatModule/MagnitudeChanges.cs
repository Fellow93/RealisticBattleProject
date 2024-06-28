﻿using HarmonyLib;
using SandBox.GameComponents;
using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.CompressionInfo;

namespace RBMCombat
{
    public static class MagnitudeChanges
    {
        public static CharacterObject currentSelectedChar = null;
        public static int equipmentSetindex = 0;

        public static int tipHits = 0;
        public static int nonTipHits = 0;

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("CalculateBaseMeleeBlowMagnitude")]
        public class CalculateBaseMeleeBlowMagnitudePatch
        {
            public static bool Prefix(ref float __result, in AttackInformation attackInformation, in MissionWeapon weapon, StrikeType strikeType, float progressEffect, float impactPointAsPercent, float exraLinearSpeed)
            {
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
                    exraLinearSpeed *= 1f;
                    float thrustWeaponSpeed = (float)weapon.GetModifiedThrustSpeedForCurrentUsage() / 11.7647057f * accelerationProgress;

                    if (weapon.Item != null && weapon.CurrentUsageItem != null)
                    {
                        Agent attacker = attackInformation.AttackerAgent;

                        if (attacker != null)
                        {
                            bool isNonTipHit = false;
                            
                            if(weaponClass == WeaponClass.OneHandedSword || 
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

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.Inertia, weapon.CurrentUsageItem.CenterOfMass);
                                        thrustWeaponSpeed = thrustWeaponSpeed * 0.75f * thrustskillModifier * accelerationProgress;
                                        break;
                                    }
                                case WeaponClass.TwoHandedPolearm:
                                    {
                                        float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                                        float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.Inertia, weapon.CurrentUsageItem.CenterOfMass);
                                        thrustWeaponSpeed = thrustWeaponSpeed * 0.65f * thrustskillModifier * accelerationProgress;
                                        break;
                                    }
                                case WeaponClass.TwoHandedAxe:
                                    {
                                        float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                                        float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.Inertia, weapon.CurrentUsageItem.CenterOfMass);
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

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.Inertia, weapon.CurrentUsageItem.CenterOfMass);
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
                exraLinearSpeed *= 1f;
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
                //    float num6 = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForSwing(attackerAgentCharacter, attackerCaptainCharacter, swingSpeed, impactPointAsPercent2, weapon.Item.Weight, currentUsageItem, currentUsageItem.GetRealWeaponLength(), currentUsageItem.Inertia, currentUsageItem.CenterOfMass, exraLinearSpeed, doesAttackerHaveMount);
                //    if (originalValue < num6)
                //    {
                //        originalValue = num6;
                //    }
                //}

                float impactPointAsPercent3 = num3 + (float)0 / 4f * (num4 - num3);
                //newValue = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForSwing(attackerAgentCharacter, attackerCaptainCharacter, swingSpeed, impactPointAsPercent3, weapon.Item.Weight, weapon.Item, currentUsageItem, currentUsageItem.GetRealWeaponLength(), currentUsageItem.Inertia, currentUsageItem.CenterOfMass, exraLinearSpeed, doesAttackerHaveMount);
                newValue = CombatStatCalculator.CalculateStrikeMagnitudeForSwing(swingSpeed, impactPointAsPercent3, weapon.Item.Weight,
                            currentUsageItem.GetRealWeaponLength(), currentUsageItem.Inertia, currentUsageItem.CenterOfMass, exraLinearSpeed);
                __result = newValue;
                return false;
            }
        }

        //[HarmonyPatch(typeof(CombatStatCalculator))]
        //[HarmonyPatch("CalculateStrikeMagnitudeForSwing")]
        //public class CalculateStrikeMagnitudeForSwingPatch
        //{
        //    public static bool Prefix(ref float __result, float swingSpeed, float impactPointAsPercent, float weaponWeight, float weaponLength, float weaponInertia, float weaponCoM, float extraLinearSpeed)
        //    {
        //        float distanceFromCoM = weaponLength * impactPointAsPercent - weaponCoM;
        //        float num2 = swingSpeed * (0.5f + weaponCoM) + extraLinearSpeed;
        //        float kineticEnergy = 0.5f * weaponWeight * num2 * num2;
        //        float inertiaEnergy = 0.5f * weaponInertia * swingSpeed * swingSpeed;
        //        float num5 = kineticEnergy + inertiaEnergy;
        //        float num6 = (num2 + swingSpeed * distanceFromCoM) / (1f / weaponWeight + distanceFromCoM * distanceFromCoM / weaponInertia);
        //        float num7 = num2 - num6 / weaponWeight;
        //        float num8 = swingSpeed - num6 * distanceFromCoM / weaponInertia;
        //        float num9 = 0.5f * weaponWeight * num7 * num7;
        //        float num10 = 0.5f * weaponInertia * num8 * num8;
        //        float num11 = num9 + num10;
        //        float num12 = num5 - num11 + 0.5f;
        //        __result =  0.067f * num12;
        //        //InformationManager.DisplayMessage(new InformationMessage("energy " + num12, Color.FromUint(4289612505u)));
        //        return false;
        //    }
        //}

        public static float CalculateMissileMagnitude(WeaponClass weaponClass, float weaponWeight, float missileSpeed, float missileTotalDamage, float momentumRemaining, DamageTypes damageType)
        {
            float baseMagnitude = 0f;
            switch (weaponClass)
            {
                case WeaponClass.Boulder:
                case WeaponClass.Stone:
                    {
                        missileTotalDamage *= 0.01f;
                        break;
                    }
                case WeaponClass.ThrowingAxe:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Dagger:
                    {
                        missileSpeed -= Utilities.throwableCorrectionSpeed;
                        if (missileSpeed < 5.0f)
                        {
                            missileSpeed = 5f;
                        }
                        break;
                    }
                case WeaponClass.Javelin:
                    {
                        missileSpeed -= Utilities.throwableCorrectionSpeed;
                        if (missileSpeed < 5.0f)
                        {
                            missileSpeed = 5f;
                        }
                        break;
                    }
                case WeaponClass.OneHandedPolearm:
                    {
                        missileSpeed -= Utilities.throwableCorrectionSpeed;
                        if (missileSpeed < 5.0f)
                        {
                            missileSpeed = 5f;
                        }
                        break;
                    }
                case WeaponClass.LowGripPolearm:
                    {
                        missileSpeed -= Utilities.throwableCorrectionSpeed;
                        if (missileSpeed < 5.0f)
                        {
                            missileSpeed = 5f;
                        }
                        break;
                    }
                case WeaponClass.Arrow:
                    {
                        missileTotalDamage -= 100f;
                        missileTotalDamage *= 0.01f;
                        break;
                    }
                case WeaponClass.Bolt:
                    {
                        missileTotalDamage -= 100f;
                        missileTotalDamage *= 0.01f;
                        break;
                    }
            }

            float physicalDamage = ((missileSpeed * missileSpeed) * (weaponWeight)) / 2;
            float momentumDamage = (missileSpeed * weaponWeight);
            switch (weaponClass)
            {
                case WeaponClass.Boulder:
                case WeaponClass.Stone:
                    {
                        physicalDamage = (missileSpeed * missileSpeed * (weaponWeight) * 0.5f);
                        break;
                    }
                case WeaponClass.ThrowingAxe:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Dagger:
                    {
                        missileSpeed -= 0f; //5f
                        break;
                    }
                case WeaponClass.Javelin:
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    {
                        if (physicalDamage > (weaponWeight) * 300f)
                        {
                            physicalDamage = (weaponWeight) * 300f;
                        }
                        break;
                    }
                case WeaponClass.Arrow:
                    {
                        if (physicalDamage > (weaponWeight) * 2250f)
                        {
                            physicalDamage = (weaponWeight) * 2250f;
                        }
                        break;
                    }
                case WeaponClass.Bolt:
                    {
                        if (physicalDamage > (weaponWeight) * 2500f)
                        {
                            physicalDamage = (weaponWeight) * 2500f;
                        }
                        break;
                    }
            }

            baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;

            if (weaponClass == WeaponClass.Javelin)
            {
                missileTotalDamage = 0f;
                //baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                if (damageType == DamageTypes.Pierce)
                {
                    baseMagnitude = (physicalDamage * momentumRemaining) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                }
                else if (damageType == DamageTypes.Cut)
                {
                    baseMagnitude = (physicalDamage * momentumRemaining);
                }
                else
                {
                    baseMagnitude = (physicalDamage * momentumRemaining) * 0.5f;
                }
            }

            if (weaponClass == WeaponClass.ThrowingAxe)
            {
                baseMagnitude = physicalDamage * momentumRemaining;
            }
            if (weaponClass == WeaponClass.ThrowingKnife ||
                weaponClass == WeaponClass.Dagger)
            {
                baseMagnitude = (physicalDamage * momentumRemaining) * RBMConfig.RBMConfig.ThrustMagnitudeModifier * 0.6f;
            }

            if (weaponClass == WeaponClass.OneHandedPolearm ||
                weaponClass == WeaponClass.LowGripPolearm)
            {
                baseMagnitude = (physicalDamage * momentumRemaining) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
            }
            if (weaponClass == WeaponClass.Arrow ||
                weaponClass == WeaponClass.Bolt)
            {
                if (damageType == DamageTypes.Cut || damageType == DamageTypes.Pierce)
                {
                    baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;
                }
                else
                {
                    baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining; // momentum makes more sense for blunt attacks, maybe 500 damage is needed for sling projectiles
                }
            }
            return baseMagnitude;
        }

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("ComputeBlowMagnitudeMissile")]
        private class ComputeBlowMagnitudeMissilePacth
        {
            private static bool Prefix(in AttackInformation attackInformation, in AttackCollisionData collisionData, in MissionWeapon weapon, float momentumRemaining, in Vec2 victimVelocity, out float baseMagnitude, out float specialMagnitude)
            {
                Vec3 missileVelocity = collisionData.MissileVelocity;

                float missileTotalDamage = collisionData.MissileTotalDamage;

                WeaponComponentData currentUsageItem = weapon.CurrentUsageItem;
                ItemObject weaponItem;
                if (weapon.AmmoWeapon.Item != null)
                {
                    weaponItem = weapon.AmmoWeapon.Item;
                }
                else
                {
                    weaponItem = weapon.Item;
                }

                float length;
                if (!attackInformation.IsVictimAgentNull)
                {
                    length = (victimVelocity.ToVec3() - missileVelocity).Length;
                }
                else
                {
                    length = missileVelocity.Length;
                }
                baseMagnitude = CalculateMissileMagnitude(weapon.CurrentUsageItem.WeaponClass, weaponItem.Weight, length, missileTotalDamage, momentumRemaining, (DamageTypes)collisionData.DamageType);
                specialMagnitude = baseMagnitude;

                return false;
            }
        }

        [HarmonyPatch(typeof(CombatStatCalculator))]
        [HarmonyPatch("CalculateStrikeMagnitudeForPassiveUsage")]
        private class ChangeLanceDamage
        {
            private static bool Prefix(float weaponWeight, float extraLinearSpeed, ref float __result)
            {
                __result = CalculateStrikeMagnitudeForThrust(0f, weaponWeight, extraLinearSpeed, isThrown: false);
                return false;
            }

            private static float CalculateStrikeMagnitudeForThrust(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown)
            {
                float num = extraLinearSpeed * 1f; // because cav in the game is roughly 50% faster than it should be
                float num2 = 0.5f * weaponWeight * num * num * RBMConfig.RBMConfig.ThrustMagnitudeModifier; // lances need to have 3 times more damage to be preferred over maces
                return num2;
            }
        }

        [HarmonyPatch(typeof(CombatStatCalculator))]
        [HarmonyPatch("CalculateStrikeMagnitudeForThrust")]
        private class CalculateStrikeMagnitudeForThrustPatch
        {
            private static bool Prefix(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown, ref float __result)
            {
                float combinedSpeed = MBMath.ClampFloat(thrustWeaponSpeed, 4f, 6f) + extraLinearSpeed;
                if (combinedSpeed > 0f)
                {
                    float kineticEnergy = 0.5f * weaponWeight * combinedSpeed * combinedSpeed;
                    float mixedEnergy = 0.5f * (weaponWeight + 1.5f) * combinedSpeed * combinedSpeed;
                    float baselineEnergy = 0.5f * 8f * combinedSpeed * combinedSpeed;
                    //float basedamage = 0.5f * (weaponWeight + 4.5f) * combinedSpeed * combinedSpeed;

                    //float basedamage = 120f;
                    //if (mixedEnergy > 120f)
                    //{
                    //    basedamage = mixedEnergy;
                    //}

                    //float handBonus = 0.5f * (weaponWeight + 1.5f) * combinedSpeed * combinedSpeed;
                    //float handLimit = 120f;
                    //if (handBonus > handLimit)
                    //{
                    //    handBonus = handLimit;
                    //}
                    //float thrust = handBonus;
                    //if (kineticEnergy > handLimit)
                    //{
                    //    thrust = kineticEnergy;
                    //}
                    //else if (basedamage > 180f)
                    //{
                    //    basedamage = 180f;
                    //}
                    //float thrust = basedamage;
                    //if (kineticEnergy > basedamage)
                    //{
                    //    thrust = kineticEnergy;
                    //}

                    //if (thrust > 200f)
                    //{
                    //    thrust = 200f;
                    //}
                    float thrust = baselineEnergy;
                    __result = thrust * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                    return false;
                }
                __result = 0f;
                return false;
            }
        }

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
                float weaponInertia = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).Inertia;
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
                float weaponInertia = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).Inertia;
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
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);

                            thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                            thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * progressEffect;
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
                        {
                            thrustMagnitude = Utilities.CalculateThrustMagnitudeForOneHandedWeapon(weaponWeight, effectiveSkillDR, thrustWeaponSpeed, 0f, Agent.UsageDirection.AttackDown);
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                    case WeaponClass.TwoHandedSword:
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

        public static void GetRBMMeleeWeaponStats(in EquipmentElement targetWeapon, int targetWeaponUsageIndex, EquipmentElement comparedWeapon, int comparedWeaponUsageIndex,
            out int relevantSkill, out float swingSpeed, out float swingSpeedCompred, out float thrustSpeed, out float thrustSpeedCompred, out float sweetSpotOut, out float sweetSpotComparedOut,
            out string swingCombinedStringOut, out string swingCombinedStringComparedOut, out string thrustCombinedStringOut, out string thrustCombinedStringComparedOut,
            out float swingDamageFactor, out float swingDamageFactorCompared, out float thrustDamageFactor, out float thrustDamageFactorCompared)
        {
            relevantSkill = 0;
            swingSpeed = 0f;
            swingSpeedCompred = 0f;
            thrustSpeed = 0f;
            thrustSpeedCompred = 0f;
            swingDamageFactor = 0f;
            swingDamageFactorCompared = 0f;
            thrustDamageFactor = 0f;
            thrustDamageFactorCompared = 0f;
            sweetSpotOut = 0f;
            sweetSpotComparedOut = 0f;
            swingCombinedStringOut = "";
            swingCombinedStringComparedOut = "";
            thrustCombinedStringOut = "";
            thrustCombinedStringComparedOut = "";
            if (!targetWeapon.IsEmpty && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex) != null && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).IsMeleeWeapon)
            {
                if (currentSelectedChar != null)
                {
                    SkillObject skill = targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).RelevantSkill;
                    int effectiveSkill = currentSelectedChar.GetSkillValue(skill);
                    float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(effectiveSkill);
                    float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill);

                    Utilities.CalculateVisualSpeeds(targetWeapon, targetWeaponUsageIndex, effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal);
                    Utilities.CalculateVisualSpeeds(comparedWeapon, comparedWeaponUsageIndex, effectiveSkillDR, out int swingSpeedRealCompred, out int thrustSpeedRealCompared, out int handlingRealCompared);

                    float swingSpeedRealF = swingSpeedReal / Utilities.swingSpeedTransfer;
                    float thrustSpeedRealF = thrustSpeedReal / Utilities.thrustSpeedTransfer;
                    float swingSpeedRealComparedF = swingSpeedRealCompred / Utilities.swingSpeedTransfer;
                    float thrustSpeedRealComparedF = thrustSpeedRealCompared / Utilities.thrustSpeedTransfer;

                    relevantSkill = effectiveSkill;

                    swingSpeed = swingSpeedRealF;
                    swingSpeedCompred = swingSpeedRealComparedF;
                    thrustSpeed = thrustSpeedRealF;
                    thrustSpeedCompred = thrustSpeedRealComparedF;

                    if (targetWeapon.GetModifiedSwingDamageForUsage(targetWeaponUsageIndex) > 0f)
                    {
                        float sweetSpotMagnitude = CalculateSweetSpotSwingMagnitude(targetWeapon, targetWeaponUsageIndex, effectiveSkill, out float sweetSpot);
                        float sweetSpotMagnitudeCompared = CalculateSweetSpotSwingMagnitude(comparedWeapon, comparedWeaponUsageIndex, effectiveSkill, out float sweetSpotCompared);

                        float skillBasedDamage = Utilities.GetSkillBasedDamage(sweetSpotMagnitude, false, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                            targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, effectiveSkillDR, skillModifier, StrikeType.Swing, targetWeapon.Item.Weight);

                        float skillBasedDamageCompared = sweetSpotMagnitudeCompared > 0f ? Utilities.GetSkillBasedDamage(sweetSpotMagnitudeCompared, false, comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).WeaponClass.ToString(),
                            comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).SwingDamageType, effectiveSkillDR, skillModifier, StrikeType.Swing, comparedWeapon.Item.Weight) : -1f;

                        swingDamageFactor = (float)Math.Sqrt(Utilities.getSwingDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));
                        swingDamageFactorCompared = sweetSpotMagnitudeCompared > 0f ? (float)Math.Sqrt(Utilities.getSwingDamageFactor(comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex), comparedWeapon.ItemModifier)) : -1f;

                        bool shouldBreakNextTime = false;

                        sweetSpotOut = sweetSpot;
                        sweetSpotComparedOut = sweetSpotCompared;

                        string combinedDamageString = new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
                        string combinedDamageComparedString = new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
                        for (float i = 0; i <= 100; i += 10)
                        {
                            if (shouldBreakNextTime)
                            {
                                //break;
                            }
                            if (sweetSpotMagnitudeCompared > 0f)
                            {
                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                    targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, skillBasedDamage, i, 1f, out float penetratedDamage, out float bluntForce, swingDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);

                                int realDamageCompared = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).WeaponClass.ToString(),
                                    comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).SwingDamageType, skillBasedDamageCompared, i, 1f, out float penetratedDamageCompared, out float bluntForceCompared, swingDamageFactorCompared, null, false)), 0, 2000);
                                realDamageCompared = MathF.Floor(realDamageCompared * 1f);

                                if (penetratedDamage == 0f && penetratedDamageCompared == 0f)
                                {
                                    shouldBreakNextTime = true;
                                }
                                combinedDamageString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamage) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamage)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForce) + "\n";
                                combinedDamageComparedString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamageCompared) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamageCompared)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForceCompared) + "\n";
                            }
                            else
                            {
                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(), targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, skillBasedDamage, i, 1f, out float penetratedDamage, out float bluntForce, swingDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);

                                if (penetratedDamage == 0f)
                                {
                                    shouldBreakNextTime = true;
                                }
                                combinedDamageString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamage) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamage)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForce) + "\n";
                            }
                        }
                        swingCombinedStringOut = combinedDamageString;
                        if (!comparedWeapon.IsEmpty)
                        {
                            swingCombinedStringComparedOut = combinedDamageComparedString;
                        }
                    }

                    if (targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex) > 0f)
                    {
                        float thrustMagnitude = CalculateThrustMagnitude(targetWeapon, targetWeaponUsageIndex, effectiveSkill);
                        float thrustMagnitudeCompared = CalculateThrustMagnitude(comparedWeapon, comparedWeaponUsageIndex, effectiveSkill);

                        float skillBasedDamage = Utilities.GetSkillBasedDamage(thrustMagnitude, false, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                            targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, effectiveSkillDR, skillModifier, StrikeType.Thrust, targetWeapon.Item.Weight);

                        float skillBasedDamageCompared = thrustMagnitudeCompared > 0f ? Utilities.GetSkillBasedDamage(thrustMagnitudeCompared, false, comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).WeaponClass.ToString(),
                            comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).ThrustDamageType, effectiveSkillDR, skillModifier, StrikeType.Thrust, comparedWeapon.Item.Weight) : -1f;

                        thrustDamageFactor = (float)Math.Sqrt(Utilities.getThrustDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));
                        thrustDamageFactorCompared = thrustMagnitudeCompared > 0f ? (float)Math.Sqrt(Utilities.getThrustDamageFactor(comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex), comparedWeapon.ItemModifier)) : -1f;

                        bool shouldBreakNextTime = false;

                        string combinedDamageString = new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
                        string combinedDamageComparedString = new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
                        for (float i = 0; i <= 100; i += 10)
                        {
                            if (shouldBreakNextTime)
                            {
                                //break;
                            }
                            if (thrustMagnitudeCompared > 0f)
                            {
                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, skillBasedDamage, i, 1f, out float penetratedDamage, out float bluntForce, thrustDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);

                                int realDamageCompared = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).WeaponClass.ToString(),
                                comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).ThrustDamageType, skillBasedDamageCompared, i, 1f, out float penetratedDamageCompared, out float bluntForceCompared, thrustDamageFactorCompared, null, false)), 0, 2000);
                                realDamageCompared = MathF.Floor(realDamageCompared * 1f);

                                if (penetratedDamage == 0f && penetratedDamageCompared == 0f)
                                {
                                    shouldBreakNextTime = true;
                                }

                                //methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("Thrust Damage " + i + " Armor: "), realDamage, realDamageCompared });
                                combinedDamageString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamage) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamage)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForce) + "\n";
                                combinedDamageComparedString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamageCompared) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamageCompared)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForceCompared) + "\n";
                            }
                            else
                            {
                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, skillBasedDamage, i, 1f, out float penetratedDamage, out float bluntForce, thrustDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);

                                if (penetratedDamage == 0f)
                                {
                                    shouldBreakNextTime = true;
                                }
                                combinedDamageString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamage) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamage)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForce) + "\n";
                                //methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("Thrust Damage " + i + " Armor: "), realDamage, realDamage });
                            }
                        }
                        thrustCombinedStringOut = combinedDamageString;
                        if (!comparedWeapon.IsEmpty)
                        {
                            thrustCombinedStringComparedOut = combinedDamageComparedString;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ItemMenuVM))]
        [HarmonyPatch("SetWeaponComponentTooltip")]
        private class SetWeaponComponentTooltipPatch
        {
            private static void Postfix(ref ItemMenuVM __instance, in EquipmentElement targetWeapon, int targetWeaponUsageIndex, EquipmentElement comparedWeapon, int comparedWeaponUsageIndex, bool isInit)
            {
                MethodInfo methodAddFloatProperty = typeof(ItemMenuVM).GetMethod("AddFloatProperty", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(TextObject), typeof(float), typeof(float?), typeof(bool) }, null);
                methodAddFloatProperty.DeclaringType.GetMethod("AddFloatProperty", new[] { typeof(TextObject), typeof(float), typeof(float?), typeof(bool) });

                MethodInfo methodAddIntProperty = typeof(ItemMenuVM).GetMethod("AddIntProperty", BindingFlags.NonPublic | BindingFlags.Instance);
                methodAddIntProperty.DeclaringType.GetMethod("AddIntProperty");

                MethodInfo methodCreateProperty = typeof(ItemMenuVM).GetMethod("CreateProperty", BindingFlags.NonPublic | BindingFlags.Instance);
                methodCreateProperty.DeclaringType.GetMethod("CreateProperty");

                if (!targetWeapon.IsEmpty && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex) != null && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).IsShield)
                {
                    if (comparedWeapon.IsEmpty)
                    {
                        methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_022}Shield Armor: "), targetWeapon.GetModifiedBodyArmor(), targetWeapon.GetModifiedBodyArmor() });
                    }
                    else
                    {
                        methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_022}Shield Armor: "), targetWeapon.GetModifiedBodyArmor(), comparedWeapon.GetModifiedBodyArmor() });
                    }
                }
                if (!targetWeapon.IsEmpty && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex) != null && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).IsRangedWeapon)
                {
                    if (currentSelectedChar != null)
                    {
                        SkillObject skill = targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).RelevantSkill;
                        int effectiveSkill = currentSelectedChar.GetSkillValue(skill);
                        float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(effectiveSkill);
                        float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill);
                        if (targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass == WeaponClass.Bow)
                        {
                            int msModifier = 0;
                            if (targetWeapon.ItemModifier != null)
                            {
                                msModifier = targetWeapon.ItemModifier.HitPoints;
                            }
                            int drawWeight = targetWeapon.GetModifiedMissileSpeedForUsage(targetWeaponUsageIndex) + msModifier;
                            float ammoWeightIdealModifier;
                            if (targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ItemUsage.Equals("bow"))
                            {
                                ammoWeightIdealModifier = 1600f;
                            }
                            else
                            {
                                ammoWeightIdealModifier = 1400f;
                            }

                            float ammoWeightIdeal = drawWeight / ammoWeightIdealModifier;

                            int calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeightIdeal, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ItemUsage, drawWeight);

                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, new TextObject("{=RBM_COM_036}RBM Stats").ToString(), "", 1, null });

                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_009}Ideal Ammo Weight Range/Damage, grams: "), MathF.Round(ammoWeightIdeal * 1000f), MathF.Round(ammoWeightIdeal * 1000f) });
                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_010}Initial Missile Speed, m/s: "), calculatedMissileSpeed, calculatedMissileSpeed });
                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_011}Draw weight with modifier: "), drawWeight, drawWeight });

                            //pierceArrows
                            bool shouldBreakNextTime = false;
                            float missileMagnitude = CalculateMissileMagnitude(WeaponClass.Arrow, ammoWeightIdeal, calculatedMissileSpeed, targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex) + 100f, 1f, DamageTypes.Pierce);
                            string combinedDamageString = new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, "", new TextObject("{=RBM_COM_012}Missile Damage Pierce").ToString(), 1, null });
                            for (float i = 0; i <= 100; i += 10)
                            {
                                if (shouldBreakNextTime)
                                {
                                    //break;
                                }
                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(WeaponClass.Arrow.ToString(),
                                DamageTypes.Pierce, missileMagnitude, i, 1f, out float penetratedDamage, out float bluntForce, 1f, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);

                                if (penetratedDamage == 0f)
                                {
                                    shouldBreakNextTime = true;
                                }
                                combinedDamageString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamage) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamage)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForce) + "\n";
                                //methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("Thrust Damage " + i + " Armor: "), realDamage, realDamage });
                            }
                            __instance.TargetItemProperties[__instance.TargetItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(combinedDamageString));

                            //cut arrows
                            shouldBreakNextTime = false;
                            missileMagnitude = CalculateMissileMagnitude(WeaponClass.Arrow, ammoWeightIdeal, calculatedMissileSpeed, targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex) + 115f, 1f, DamageTypes.Cut);
                            combinedDamageString = new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, "", new TextObject("{=RBM_COM_013}Missile Damage Cut").ToString(), 1, null });
                            for (float i = 0; i <= 100; i += 10)
                            {
                                if (shouldBreakNextTime)
                                {
                                    //break;
                                }
                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(WeaponClass.Arrow.ToString(),
                                DamageTypes.Cut, missileMagnitude, i, 1f, out float penetratedDamage, out float bluntForce, 1f, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);

                                if (penetratedDamage == 0f)
                                {
                                    shouldBreakNextTime = true;
                                }
                                combinedDamageString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamage) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamage)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForce) + "\n";
                                //methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("Thrust Damage " + i + " Armor: "), realDamage, realDamage });
                            }
                            __instance.TargetItemProperties[__instance.TargetItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(combinedDamageString));
                        }
                        if (targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass == WeaponClass.Crossbow)
                        {
                            int msModifier = 0;
                            if (targetWeapon.ItemModifier != null)
                            {
                                msModifier = targetWeapon.ItemModifier.HitPoints;
                            }
                            int drawWeight = targetWeapon.GetModifiedMissileSpeedForUsage(targetWeaponUsageIndex) + msModifier;
                            float ammoWeightIdealModifier = 4000f;

                            float ammoWeightIdeal = MathF.Clamp(drawWeight / ammoWeightIdealModifier, 0f, 0.150f);

                            int calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeightIdeal, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ItemUsage, drawWeight);

                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, new TextObject("{=RBM_COM_036}RBM Stats").ToString(), "", 1, null });

                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_009}Ideal Ammo Weight Range/Damage, grams: "), MathF.Round(ammoWeightIdeal * 1000f), MathF.Round(ammoWeightIdeal * 1000f) });
                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_010}Initial Missile Speed, m/s: "), calculatedMissileSpeed, calculatedMissileSpeed });
                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_011}Draw weight with modifier: "), drawWeight, drawWeight });

                            //pierce bolts
                            bool shouldBreakNextTime = false;
                            float missileMagnitude = CalculateMissileMagnitude(WeaponClass.Bolt, ammoWeightIdeal, calculatedMissileSpeed, targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex) + 100f, 1f, DamageTypes.Pierce);
                            string combinedDamageString = new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, "", new TextObject("{=RBM_COM_012}Missile Damage Pierce").ToString(), 1, null });
                            for (float i = 0; i <= 100; i += 10)
                            {
                                if (shouldBreakNextTime)
                                {
                                    //break;
                                }
                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(WeaponClass.Bolt.ToString(),
                                DamageTypes.Pierce, missileMagnitude, i, 1f, out float penetratedDamage, out float bluntForce, 1f, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);

                                if (penetratedDamage == 0f)
                                {
                                    shouldBreakNextTime = true;
                                }
                                combinedDamageString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamage) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamage)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForce) + "\n";
                                //methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("Thrust Damage " + i + " Armor: "), realDamage, realDamage });
                            }
                            __instance.TargetItemProperties[__instance.TargetItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(combinedDamageString));

                            //cut bolts
                            shouldBreakNextTime = false;
                            missileMagnitude = CalculateMissileMagnitude(WeaponClass.Bolt, ammoWeightIdeal, calculatedMissileSpeed, targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex) + 115f, 1f, DamageTypes.Cut);
                            combinedDamageString = new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, "", new TextObject("{=RBM_COM_013}Missile Damage Cut").ToString(), 1, null });
                            for (float i = 0; i <= 100; i += 10)
                            {
                                if (shouldBreakNextTime)
                                {
                                    //break;
                                }
                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(WeaponClass.Bolt.ToString(),
                                DamageTypes.Cut, missileMagnitude, i, 1f, out float penetratedDamage, out float bluntForce, 1f, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);

                                if (penetratedDamage == 0f)
                                {
                                    shouldBreakNextTime = true;
                                }
                                combinedDamageString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamage) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamage)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForce) + "\n";
                                //methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("Thrust Damage " + i + " Armor: "), realDamage, realDamage });
                            }
                            __instance.TargetItemProperties[__instance.TargetItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(combinedDamageString));
                        }
                        if (targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass == WeaponClass.Javelin)
                        {
                            int calculatedMissileSpeed = Utilities.assignThrowableMissileSpeedForMenu(targetWeapon.Weight, (int)Utilities.throwableCorrectionSpeed, effectiveSkill);

                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, new TextObject("{=RBM_COM_036}RBM Stats").ToString(), "", 1, null });
                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_014}Relevant Skill: "), effectiveSkill, effectiveSkill });
                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_010}Initial Missile Speed, m/s: "), calculatedMissileSpeed, calculatedMissileSpeed });

                            //pierceArrows
                            bool shouldBreakNextTime = false;
                            float missileMagnitude = CalculateMissileMagnitude(WeaponClass.Javelin, targetWeapon.Weight, calculatedMissileSpeed, targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex), 1f, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType);
                            string combinedDamageString = new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, "", new TextObject("{=RBM_COM_015}Missile Damage").ToString(), 1, null });
                            float weaponDamageFactor = (float)Math.Sqrt(Utilities.getThrustDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));
                            for (float i = 0; i <= 100; i += 10)
                            {
                                if (shouldBreakNextTime)
                                {
                                    //break;
                                }
                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(WeaponClass.Javelin.ToString(),
                                DamageTypes.Pierce, missileMagnitude, i, 1f, out float penetratedDamage, out float bluntForce, weaponDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);

                                if (penetratedDamage == 0f)
                                {
                                    shouldBreakNextTime = true;
                                }
                                combinedDamageString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamage) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamage)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForce) + "\n";
                                //methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("Thrust Damage " + i + " Armor: "), realDamage, realDamage });
                            }
                            __instance.TargetItemProperties[__instance.TargetItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(combinedDamageString));
                        }
                        if (targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass == WeaponClass.ThrowingAxe ||
                            targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass == WeaponClass.ThrowingKnife ||
                            targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass == WeaponClass.Dagger)
                        {
                            //int calculatedMissileSpeed = Utilities.assignThrowableMissileSpeedForMenu(targetWeapon.Weight, 0, effectiveSkill);
                            int calculatedMissileSpeed = Utilities.assignThrowableMissileSpeedForMenu(targetWeapon.Weight, (int)Utilities.throwableCorrectionSpeed, effectiveSkill);

                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, new TextObject("{=RBM_COM_036}RBM Stats").ToString(), "", 1, null });
                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_014}Relevant Skill: "), effectiveSkill, effectiveSkill });
                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_010}Initial Missile Speed, m/s: "), calculatedMissileSpeed, calculatedMissileSpeed });

                            //pierceArrows
                            bool shouldBreakNextTime = false;
                            float missileMagnitude = CalculateMissileMagnitude(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass, targetWeapon.Weight, calculatedMissileSpeed, targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex), 1f, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType);
                            if (targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass == WeaponClass.ThrowingAxe)
                            {
                                missileMagnitude = CalculateMissileMagnitude(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass, targetWeapon.Weight, calculatedMissileSpeed, targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex), 1f, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType);
                            }
                            string combinedDamageString = new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, "", new TextObject("{=RBM_COM_015}Missile Damage").ToString(), 1, null });
                            float weaponDamageFactor = (float)Math.Sqrt(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageFactor);
                            if (targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass == WeaponClass.ThrowingAxe)
                            {
                                weaponDamageFactor = (float)Math.Sqrt(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageFactor);
                            }
                            for (float i = 0; i <= 100; i += 10)
                            {
                                if (shouldBreakNextTime)
                                {
                                    //break;
                                }
                                int realDamage;
                                float penetratedDamage;
                                float bluntForce;
                                if (targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass == WeaponClass.ThrowingAxe)
                                {
                                    realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                    targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, missileMagnitude, i, 1f, out penetratedDamage, out bluntForce, weaponDamageFactor, null, false)), 0, 2000);
                                }
                                else
                                {
                                    realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                               targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, missileMagnitude, i, 1f, out penetratedDamage, out bluntForce, weaponDamageFactor, null, false)), 0, 2000);
                                }
                                realDamage = MathF.Floor(realDamage * 1f);

                                if (penetratedDamage == 0f)
                                {
                                    shouldBreakNextTime = true;
                                }
                                combinedDamageString += new TextObject("{=RBM_COM_032}A").ToString() + ": " + String.Format("{0,-5}", i) + " " + new TextObject("{=RBM_COM_033}D").ToString() + ": " + String.Format("{0,-5}", realDamage) + " " + new TextObject("{=RBM_COM_034}P").ToString() + ": " + String.Format("{0,-5}", MathF.Floor(penetratedDamage)) + " " + new TextObject("{=RBM_COM_035}B").ToString() + ": " + MathF.Floor(bluntForce) + "\n";
                                //methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("Thrust Damage " + i + " Armor: "), realDamage, realDamage });
                            }
                            __instance.TargetItemProperties[__instance.TargetItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(combinedDamageString));
                        }
                    }
                }
                if (!targetWeapon.IsEmpty && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex) != null && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).IsMeleeWeapon)
                {
                    GetRBMMeleeWeaponStats(targetWeapon, targetWeaponUsageIndex, comparedWeapon, comparedWeaponUsageIndex, out int relevantSkill, out float swingSpeed, out float swingSpeedCompred, out float thrustSpeed, out float thrustSpeedCompred, out float sweetSpotOut, out float sweetSpotComparedOut,
                    out string swingCombinedStringOut, out string swingCombinedStringComparedOut, out string thrustCombinedStringOut, out string thrustCombinedStringComparedOut,
                    out float swingDamageFactor, out float swingDamageFactorCompared, out float thrustDamageFactor, out float thrustDamageFactorCompared);

                    if (currentSelectedChar != null)
                    {
                        methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, new TextObject("{=RBM_COM_036}RBM Stats").ToString(), "", 1, null });

                        methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_014}Relevant Skill: "), relevantSkill, relevantSkill });

                        methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_016}Swing Damage Factor:"), MathF.Round(swingDamageFactor * 100f), MathF.Round(swingDamageFactorCompared * 100f) });
                        methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_017}Thrust Damage Factor:"), MathF.Round(thrustDamageFactor * 100f), MathF.Round(thrustDamageFactorCompared * 100f) });

                        methodAddFloatProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_020}Swing Speed, m/s: "), swingSpeed, swingSpeedCompred, false });
                        methodAddFloatProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_021}Thrust Speed, m/s: "), thrustSpeed, thrustSpeedCompred, false });

                        if (targetWeapon.GetModifiedSwingDamageForUsage(targetWeaponUsageIndex) > 0f)
                        {
                            methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("{=RBM_COM_018}Swing Sweet Spot, %: "), MathF.Floor(sweetSpotOut * 100f), MathF.Floor(sweetSpotComparedOut * 100f) });

                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, "", new TextObject("{=QeToaiLt}Swing Damage").ToString() + " (" + new TextObject("{=RBM_COM_037}Hover").ToString() + ")", 1, null });

                            __instance.TargetItemProperties[__instance.TargetItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(swingCombinedStringOut));
                            if (!comparedWeapon.IsEmpty)
                            {
                                methodCreateProperty.Invoke(__instance, new object[] { __instance.ComparedItemProperties, "", new TextObject("{=QeToaiLt}Swing Damage").ToString() + " (" + new TextObject("{=RBM_COM_037}Hover").ToString() + ")", 1, null });
                                __instance.ComparedItemProperties[__instance.ComparedItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(swingCombinedStringComparedOut));
                            }
                        }

                        if (targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex) > 0f)
                        {
                            methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, "", new TextObject("{=dO95yR9b}Thrust Damage").ToString() + " (" + new TextObject("{=RBM_COM_037}Hover").ToString() + ")", 1, null });

                            __instance.TargetItemProperties[__instance.TargetItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(thrustCombinedStringOut));
                            if (!comparedWeapon.IsEmpty)
                            {
                                methodCreateProperty.Invoke(__instance, new object[] { __instance.ComparedItemProperties, "", new TextObject("{=dO95yR9b}Thrust Damage").ToString() + " (" + new TextObject("{=RBM_COM_037}Hover").ToString() + ")", 1, null });
                                __instance.ComparedItemProperties[__instance.ComparedItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(thrustCombinedStringComparedOut));
                            }
                        }

                        if (RBMConfig.RBMConfig.developerMode)
                        {
                            if (targetWeapon.Item.WeaponDesign != null && targetWeapon.Item.WeaponDesign.UsedPieces != null && targetWeapon.Item.WeaponDesign.UsedPieces.Count() > 0)
                            {
                                methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, new TextObject("{=RBM_COM_019}RBM Developer Stats").ToString(), "", 1, null });

                                foreach (WeaponDesignElement wde in targetWeapon.Item.WeaponDesign.UsedPieces)
                                {
                                    methodCreateProperty.Invoke(__instance, new object[] { __instance.TargetItemProperties, "", wde.CraftingPiece.StringId + " " + wde.CraftingPiece.Name, 1, null });
                                    //methodAddIntProperty.Invoke(__instance, new object[] { new TextObject("Scale Percentage:"), wde.ScalePercentage, wde.ScalePercentage });
                                    methodAddFloatProperty.Invoke(__instance, new object[] { new TextObject("{=YvwQL9aa}Weight: "), wde.CraftingPiece.Weight, wde.CraftingPiece.Weight, false });
                                    methodAddFloatProperty.Invoke(__instance, new object[] { new TextObject("{=XUtiwiYP}Length: "), wde.CraftingPiece.Length, wde.CraftingPiece.Length, false });
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void getRBMArmorStatsStrings(Equipment equipment,
            out string combinedHeadString,
            out string combinedBodyString,
            out string combinedArmString,
            out string combinedLegString)
        {
            combinedHeadString = "";
            combinedBodyString = "";
            combinedArmString = "";
            combinedLegString = "";
            if (equipment != null)
            {
                if (equipment != null)
                {
                    float headArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Head);
                    float neckArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Neck);
                    float shoulderArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.ShoulderLeft);
                    float armArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.ArmLeft);
                    float chestArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Chest);
                    float abdomenArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Abdomen);
                    float legsArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Legs);

                    combinedHeadString += String.Format("{0,-0}", new TextObject("{=EUzxzL9s}Head Armor: ").ToString()) + headArmor + "\n";
                    if (!equipment[EquipmentIndex.Head].IsEmpty)
                    {
                        float faceArmor = equipment[EquipmentIndex.Head].GetModifiedBodyArmor();

                        combinedHeadString += String.Format("{0,-0}", new TextObject("{=RBM_COM_023}Face Armor").ToString()) + ": " + faceArmor + "\n";
                    }
                    combinedHeadString += String.Format("{0,-0}", new TextObject("{=RBM_COM_024}Neck Armor").ToString()) + ": " + neckArmor;

                    combinedBodyString += String.Format("{0,-0}", new TextObject("{=RBM_COM_025}Shoulder Armor").ToString()) + ": " + shoulderArmor + "\n";
                    combinedBodyString += String.Format("{0,-0}", new TextObject("{=oiSW6MyB}Chest Armor").ToString()) + ": " + chestArmor + "\n";
                    combinedBodyString += String.Format("{0,-0}", new TextObject("{=RBM_COM_026}Abdomen Armor").ToString()) + ": " + abdomenArmor;

                    combinedArmString += String.Format("{0,-0}", new TextObject("{=kx7q8ybD}Arm Armor").ToString() + ": ") + armArmor + "\n";
                    if (!equipment[EquipmentIndex.Body].IsEmpty)
                    {
                        float underShoulderArmor = (equipment[EquipmentIndex.Body].GetModifiedArmArmor());
                        if (!equipment[EquipmentIndex.Cape].IsEmpty)
                        {
                            underShoulderArmor += equipment[EquipmentIndex.Cape].GetModifiedArmArmor();
                        }
                        combinedArmString += String.Format("{0,-0}", new TextObject("{=RBM_COM_027}Lower Shoulder Armor").ToString() + ": ") + underShoulderArmor;
                    }

                    combinedLegString += String.Format("{0,-0}", new TextObject("{=U8VHRdwF}Leg Armor: ").ToString()) + legsArmor;
                }
            }
        }

        [HarmonyPatch(typeof(SPInventoryVM))]
        [HarmonyPatch("UpdateCharacterArmorValues")]
        private class UpdateCharacterArmorValuesPatch
        {
            private static void Postfix(ref SPInventoryVM __instance, CharacterObject ____currentCharacter)
            {
                if (____currentCharacter != null)
                {
                    currentSelectedChar = ____currentCharacter;
                    Equipment equipment = ____currentCharacter.Equipment;
                    getRBMArmorStatsStrings(equipment,
                       out string combinedHeadString,
                       out string combinedBodyString,
                       out string combinedArmString,
                       out string combinedLegString);

                    __instance.HeadArmorHint = new HintViewModel(new TextObject(combinedHeadString));
                    __instance.BodyArmorHint = new HintViewModel(new TextObject(combinedBodyString));
                    __instance.ArmArmorHint = new HintViewModel(new TextObject(combinedArmString));
                    __instance.LegArmorHint = new HintViewModel(new TextObject(combinedLegString));
                }
            }
        }

        [HarmonyPatch(typeof(SPInventoryVM))]
        [HarmonyPatch("RefreshValues")]
        private class RefreshValuesPatch
        {
            private static void Postfix(ref SPInventoryVM __instance, CharacterObject ____currentCharacter)
            {
                if (____currentCharacter != null)
                {
                    Equipment equipment = ____currentCharacter.Equipment;
                    getRBMArmorStatsStrings(equipment,
                       out string combinedHeadString,
                       out string combinedBodyString,
                       out string combinedArmString,
                       out string combinedLegString);
                    __instance.HeadArmorHint = new HintViewModel(new TextObject(combinedHeadString));
                    __instance.BodyArmorHint = new HintViewModel(new TextObject(combinedBodyString));
                    __instance.ArmArmorHint = new HintViewModel(new TextObject(combinedArmString));
                    __instance.LegArmorHint = new HintViewModel(new TextObject(combinedLegString));
                }
            }
        }

        [HarmonyPatch(typeof(EncyclopediaUnitPageVM))]
        [HarmonyPatch("RefreshValues")]
        private class EncyclopediaUnitPageVMRefreshValuesPatch
        {
            private static void Postfix(ref EncyclopediaUnitPageVM __instance, CharacterObject ____character)
            {
                if (__instance.EquipmentSetSelector != null)
                {
                    equipmentSetindex = __instance.EquipmentSetSelector.SelectedIndex;
                }
                currentSelectedChar = ____character;
            }
        }

        [HarmonyPatch(typeof(EncyclopediaUnitPageVM))]
        [HarmonyPatch("OnEquipmentSetChange")]
        private class EncyclopediaUnitPageVOnEquipmentSetChangePatch
        {
            private static void Postfix(ref EncyclopediaUnitPageVM __instance)
            {
                if (__instance.EquipmentSetSelector != null)
                {
                    equipmentSetindex = __instance.EquipmentSetSelector.SelectedIndex;
                }
            }
        }

        //[HarmonyPatch(typeof(PropertyBasedTooltipVMExtensions))]
        //[HarmonyPatch("UpdateTooltip", new[] { typeof(PropertyBasedTooltipVM), typeof(EquipmentElement?) })]
        //private class UpdateTooltipPatch
        //{
        //    private static void Postfix(ref PropertyBasedTooltipVM propertyBasedTooltipVM, EquipmentElement? equipmentElement)
        //    {
        //        if (equipmentElement != null && equipmentElement.HasValue)
        //        {
        //            EquipmentElement eq = equipmentElement.Value;
        //            if (propertyBasedTooltipVM != null && currentSelectedChar != null && eq.Item != null)
        //            {
        //                ItemObject item = eq.Item;
        //                if (item.WeaponComponent != null && item.Weapons.Count > 0)
        //                {
        //                    int usageindex = 0;
        //                    if (eq.Item.Weapons.Count > 1 && propertyBasedTooltipVM.IsExtended)
        //                    {
        //                        usageindex = 1;
        //                    }
        //                    string hintText = "";
        //                    if (item.GetWeaponWithUsageIndex(usageindex).IsMeleeWeapon)
        //                    {
        //                        GetRBMMeleeWeaponStats(eq, usageindex, EquipmentElement.Invalid, -1, out int relevantSkill, out float swingSpeed, out float swingSpeedCompred, out float thrustSpeed, out float thrustSpeedCompred, out float sweetSpotOut, out float sweetSpotComparedOut,
        //                    out string swingCombinedStringOut, out string swingCombinedStringComparedOut, out string thrustCombinedStringOut, out string thrustCombinedStringComparedOut,
        //                    out float swingDamageFactor, out float swingDamageFactorCompared, out float thrustDamageFactor, out float thrustDamageFactorCompared);

        //                        hintText += "RBM Stats\n";
        //                        hintText += "Relevant Skill: " + relevantSkill + "\n";
        //                        hintText += "Swing Damage Factor: " + MathF.Round(swingDamageFactor * 100f) + "\n";
        //                        hintText += "Thrust Damage Factor: " + MathF.Round(thrustDamageFactor * 100f) + "\n";
        //                        hintText += "Swing Speed, m/s: " + swingSpeed + "\n";
        //                        hintText += "Thrust Speed, m/s: " + thrustSpeed + "\n";

        //                        if (eq.GetModifiedSwingDamageForUsage(usageindex) > 0f)
        //                        {
        //                            hintText += "Swing Sweet Spot, %:" + MathF.Floor(sweetSpotOut * 100f) + "\n";

        //                            hintText += "Swing Damage:\n";
        //                            hintText += swingCombinedStringOut + "\n";
        //                        }

        //                        if (eq.GetModifiedThrustDamageForUsage(usageindex) > 0f)
        //                        {
        //                            hintText += "Thrust Damage:\n";
        //                            hintText += thrustCombinedStringOut + "\n";
        //                        }
        //                        propertyBasedTooltipVM.AddProperty("", hintText);
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}

        //[HarmonyPatch(typeof(PropertyBasedTooltipVMExtensions))]
        //[HarmonyPatch("UpdateTooltip", new[] { typeof(PropertyBasedTooltipVM), typeof(CharacterObject) })]
        //private class UpdateTooltipCharacterObjectPatch
        //{
        //    private static void Postfix(ref PropertyBasedTooltipVM propertyBasedTooltipVM, CharacterObject character)
        //    {
        //        if (character != null)
        //        {
        //            Equipment equipment;
        //            if (character.BattleEquipments.ToArray().Length > equipmentSetindex)
        //            {
        //                equipment = character.BattleEquipments.ElementAt(equipmentSetindex);
        //            }
        //            else
        //            {
        //                equipment = character.Equipment;
        //            }
        //            getRBMArmorStatsStrings(equipment,
        //               out string combinedHeadString,
        //               out string combinedBodyString,
        //               out string combinedArmString,
        //               out string combinedLegString);
        //            propertyBasedTooltipVM.AddProperty("", combinedHeadString);
        //            propertyBasedTooltipVM.AddProperty("", combinedBodyString);
        //            propertyBasedTooltipVM.AddProperty("", combinedArmString);
        //            propertyBasedTooltipVM.AddProperty("", combinedLegString);
        //        }
        //    }
        //}
    }
}