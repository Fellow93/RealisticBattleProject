using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

// ReSharper disable UnusedType.Local
// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

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
                    exraLinearSpeed *= 1f;
                    float thrustWeaponSpeed = weapon.GetModifiedThrustSpeedForCurrentUsage() / Utilities.thrustSpeedTransfer * accelerationProgress;

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
                            case WeaponClass.TwoHandedMace: {
                                float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                                float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.TotalInertia, weapon.CurrentUsageItem.CenterOfMass);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.75f * thrustskillModifier * accelerationProgress;
                                break;
                            }
                            case WeaponClass.TwoHandedPolearm: {
                                float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                                float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.TotalInertia, weapon.CurrentUsageItem.CenterOfMass);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.65f * thrustskillModifier * accelerationProgress;
                                break;
                            }
                            case WeaponClass.TwoHandedAxe: {
                                float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                                float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                                float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.TotalInertia, weapon.CurrentUsageItem.CenterOfMass);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.9f * thrustskillModifier * accelerationProgress;
                                break;
                            }
                            case WeaponClass.OneHandedSword:
                            case WeaponClass.Dagger:
                            case WeaponClass.TwoHandedSword: {
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
                                    thrustMagnitude = Utilities.CalculateThrustMagnitudeForOneHandedWeapon(weapon.Item.Weight, effectiveSkillDR, thrustWeaponSpeed, exraLinearSpeed, attacker.AttackDirection);
                                    break;
                                case WeaponClass.TwoHandedPolearm:
                                case WeaponClass.TwoHandedSword:
                                    thrustMagnitude = Utilities.CalculateThrustMagnitudeForTwoHandedWeapon(weapon.Item.Weight, effectiveSkillDR, thrustWeaponSpeed, exraLinearSpeed, attacker.AttackDirection);
                                    break;
                                //default:
                                //    thrustMagnitude = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForThrust(attackerAgentCharacter, attackerCaptainCharacter, thrustWeaponSpeed, weapon.Item.Weight, weapon.Item, currentUsageItem, exraLinearSpeed, doesAttackerHaveMount);
                                //    break;
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
                float swingSpeed = (float)weapon.GetModifiedSwingSpeedForCurrentUsage() / Utilities.swingSpeedTransfer * accelerationProgress;

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
                    case WeaponClass.TwoHandedMace: {
                        float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                        swingSpeed = swingSpeed * 0.83f * swingskillModifier * accelerationProgress;
                        break;
                    }
                    case WeaponClass.TwoHandedPolearm: {
                        float swingskillModifier = 1f + (effectiveSkillDR / 1000f);

                        swingSpeed = swingSpeed * 0.83f * swingskillModifier * accelerationProgress;
                        break;
                    }
                    case WeaponClass.TwoHandedAxe: {
                        float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                        swingSpeed = swingSpeed * 0.75f * swingskillModifier * accelerationProgress;
                        break;
                    }
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.TwoHandedSword: {
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

            switch (weaponClass) {
            case WeaponClass.Boulder:
            case WeaponClass.Stone:
                missileTotalDamage *= 0.01f;
                break;
            case WeaponClass.ThrowingAxe:
            case WeaponClass.ThrowingKnife:
            case WeaponClass.Dagger:
                missileSpeed -= Utilities.throwableCorrectionSpeed;
                if (missileSpeed < 5.0f)
                    missileSpeed = 5f;
                break;
            case WeaponClass.Javelin:
                missileSpeed -= Utilities.throwableCorrectionSpeed;
                if (missileSpeed < 5.0f)
                    missileSpeed = 5f;
                break;
            case WeaponClass.OneHandedPolearm:
                missileSpeed -= Utilities.throwableCorrectionSpeed;
                if (missileSpeed < 5.0f)
                    missileSpeed = 5f;
                break;
            case WeaponClass.LowGripPolearm:
                missileSpeed -= Utilities.throwableCorrectionSpeed;
                if (missileSpeed < 5.0f)
                    missileSpeed = 5f;
                break;
            case WeaponClass.Arrow:
                missileTotalDamage -= 100f;
                missileTotalDamage *= 0.01f;
                break;
            case WeaponClass.Bolt:
                missileTotalDamage -= 100f;
                missileTotalDamage *= 0.01f;
                break;
            case WeaponClass.SlingStone:
                missileTotalDamage -= 100f;
                missileTotalDamage *= 0.01f;
                break;
            }

            float physicalDamage = ((missileSpeed * missileSpeed) * (weaponWeight)) / 2;
            float momentumDamage = (missileSpeed * weaponWeight);
            switch (weaponClass) {
            case WeaponClass.Boulder:
            case WeaponClass.Stone:
                physicalDamage = (missileSpeed * missileSpeed * (weaponWeight) * 0.5f);
                break;
            case WeaponClass.ThrowingAxe:
            case WeaponClass.ThrowingKnife:
            case WeaponClass.Dagger:
                missileSpeed -= 0f; //5f
                break;
            case WeaponClass.Javelin:
            case WeaponClass.OneHandedPolearm:
            case WeaponClass.LowGripPolearm:
                if (physicalDamage > (weaponWeight) * 300f)
                    physicalDamage = (weaponWeight) * 300f;
                break;
            case WeaponClass.Arrow:
                if (physicalDamage > (weaponWeight) * 2250f)
                    physicalDamage = (weaponWeight) * 2250f;
                break;
            case WeaponClass.Bolt:
                if (physicalDamage > (weaponWeight) * 2500f)
                    physicalDamage = (weaponWeight) * 2500f;
                break;
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
                weaponClass == WeaponClass.Bolt ||
                weaponClass == WeaponClass.SlingStone)
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
            private static bool Prefix(in AttackInformation attackInformation, in AttackCollisionData collisionData, float momentumRemaining, in Vec2 victimVelocity, out float baseMagnitude, out float specialMagnitude)
            {
                MissionWeapon weapon = attackInformation.AttackerWeapon;
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

        //======================================================================================

        [HarmonyPatch(typeof(ItemMenuVM))]
        static class SetWeaponTooltipPatch
        {
            static readonly MethodInfo RefAddFloat   = typeof(ItemMenuVM).GetMethod("AddFloatProperty", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(TextObject), typeof(float), typeof(float?), typeof(bool) }, null);
            static readonly MethodInfo RefAddInt     = typeof(ItemMenuVM).GetMethod("AddIntProperty",   BindingFlags.NonPublic | BindingFlags.Instance);
            static readonly MethodInfo RefCreateProp = typeof(ItemMenuVM).GetMethod("CreateProperty",   BindingFlags.NonPublic | BindingFlags.Instance);

            [HarmonyPostfix]
            [HarmonyPatch("SetWeaponComponentTooltip")]
            static void PostfixSetWeaponComponentTooltip(ItemMenuVM __instance, in EquipmentElement targetWeapon, int targetWeaponUsageIndex, EquipmentElement comparedWeapon, int comparedWeaponUsageIndex)
            {
                if (targetWeapon.IsEmpty)
                    return;
                WeaponComponentData targetWcd = targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex);
                if (targetWcd == null)
                    return;

                // Shield
                if (targetWcd.IsShield) {
                    int currentArmor  = targetWeapon.GetModifiedBodyArmor();
                    int comparedArmor = comparedWeapon.IsEmpty ? currentArmor : comparedWeapon.GetModifiedBodyArmor();
                    AddInt(new TextObject("{=RBM_COM_022}Shield Armor: "), currentArmor, comparedArmor);
                }
                if (currentSelectedChar == null)
                    return;
                // Ranged
                if (targetWcd.IsRangedWeapon)
                    ProcessRangedWeapon(targetWeapon, targetWeaponUsageIndex, comparedWeapon, comparedWeaponUsageIndex, targetWcd, AddInt, CreateHeader, CreateSubHeader, SetHint);
                // Melee
                if (targetWcd.IsMeleeWeapon)
                    ProcessMeleeWeapon(targetWeapon, targetWeaponUsageIndex, comparedWeapon, comparedWeaponUsageIndex, targetWcd, AddInt, AddFloat, CreateHeader, CreateSubHeader, SetHint, CreateSubHeaderComp, SetHintComp);

                return;

                void AddInt(TextObject label, int val1, int? val2) {
                    RefAddInt.Invoke(__instance, new object[] {label, val1, val2});
                }
                void AddFloat(TextObject label, float val1, float? val2, bool reversedCompare = false) {
                    RefAddFloat.Invoke(__instance, new object[] {label, val1, val2, reversedCompare});
                }
                void CreateHeader(string text) {
                    RefCreateProp.Invoke(__instance, new object[] {__instance.TargetItemProperties, text, "", 1, null});
                }
                void CreateSubHeader(string text) {
                    RefCreateProp.Invoke(__instance, new object[] {__instance.TargetItemProperties, "", text, 1, null});
                }
                void SetHint(string hintText) {
                    __instance.TargetItemProperties[__instance.TargetItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(hintText));
                }
                void CreateSubHeaderComp(string text) {
                    RefCreateProp.Invoke(__instance, new object[] {__instance.ComparedItemProperties, "", text, 1, null});
                }
                void SetHintComp(string hintText) {
                    __instance.ComparedItemProperties[__instance.ComparedItemProperties.Count - 1].PropertyHint = new HintViewModel(new TextObject(hintText));
                }
            }

            // --- RANGED LOGIC ---
            static void ProcessRangedWeapon(
                EquipmentElement targetWeapon,
                int usageIndex,
                EquipmentElement compWeapon,
                int compIdx,
                WeaponComponentData targetWcd,
                Action<TextObject, int, int?> addInt,
                Action<string> createHeader,
                Action<string> createSubHeader,
                Action<string> setHint)
            {
                SkillObject skill = targetWcd.RelevantSkill;
                int effectiveSkill = currentSelectedChar.GetSkillValue(skill);
                createHeader(new TextObject("{=RBM_COM_036}RBM Stats").ToString());

                switch (targetWcd.WeaponClass) 
                {
                case WeaponClass.Bow:
                case WeaponClass.Crossbow: {
                    WeaponClass ammoClass = GetBowParams(targetWeapon, usageIndex, targetWcd, out int drawWeight, out float ammoWeightIdeal, out float speedCalc);

                    float compAmmoWeightIdeal = 0, compSpeedCalc = 0;
                    int   compDrawWeight = 0;
                    if (!compWeapon.IsEmpty) {
                        WeaponComponentData compWcd = compWeapon.Item.GetWeaponWithUsageIndex(compIdx);
                        GetBowParams(compWeapon, compIdx, compWcd, out compDrawWeight, out compAmmoWeightIdeal, out compSpeedCalc);
                    }

                    addInt(new TextObject("{=RBM_COM_009}Ideal Ammo Weight Range/Damage, grams: "), MathF.Round(ammoWeightIdeal * 1000f), MathF.Round(compAmmoWeightIdeal * 1000f));
                    addInt(new TextObject("{=RBM_COM_010}Initial Missile Speed, m/s: "), (int)speedCalc, (int)compSpeedCalc);
                    addInt(new TextObject("{=RBM_COM_011}Draw weight with modifier: "), drawWeight, compDrawWeight);

                    // PIERCE
                    createSubHeader(new TextObject("{=RBM_COM_012}Missile Damage Pierce").ToString());
                    float pMag = CalculateMissileMagnitude(ammoClass, ammoWeightIdeal, speedCalc, targetWeapon.GetModifiedThrustDamageForUsage(usageIndex) + 100f, 1f, DamageTypes.Pierce);
                    setHint(GenerateDamageTable(val => {
                        float raw = Utilities.RBMComputeDamage(ammoClass.ToString(), DamageTypes.Pierce, pMag, val, 1f, out float pen, out float blunt);
                        return (raw, pen, blunt);
                    }));

                    // CUT
                    createSubHeader(new TextObject("{=RBM_COM_013}Missile Damage Cut").ToString());
                    float cMag = CalculateMissileMagnitude(ammoClass, ammoWeightIdeal, speedCalc, targetWeapon.GetModifiedThrustDamageForUsage(usageIndex) + 115f, 1f, DamageTypes.Cut);
                    setHint(GenerateDamageTable(val => {
                        float raw = Utilities.RBMComputeDamage(ammoClass.ToString(), DamageTypes.Cut, cMag, val, 1f, out float pen, out float blunt);
                        return (raw, pen, blunt);
                    }));
                    break;
                }

                case WeaponClass.Javelin:
                case WeaponClass.ThrowingAxe:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Dagger: {
                    int calcSpeed     = Utilities.assignThrowableMissileSpeedForMenu(targetWeapon.Weight, (int)Utilities.throwableCorrectionSpeed, effectiveSkill);
                    int compCalcSpeed = compWeapon.IsEmpty ? 0 : Utilities.assignThrowableMissileSpeedForMenu(compWeapon.Weight, (int)Utilities.throwableCorrectionSpeed, effectiveSkill);
                    addInt(new TextObject("{=RBM_COM_014}Relevant Skill: "), effectiveSkill, effectiveSkill);
                    addInt(new TextObject("{=RBM_COM_010}Initial Missile Speed, m/s: "), calcSpeed, compCalcSpeed);

                    bool  isAxe     = targetWcd.WeaponClass == WeaponClass.ThrowingAxe;
                    var   dType     = isAxe ? targetWcd.SwingDamageType : targetWcd.ThrustDamageType;
                    float dmgFactor = MathF.Sqrt(isAxe ? targetWcd.SwingDamageFactor : Utilities.getThrustDamageFactor(targetWcd, targetWeapon.ItemModifier)); 
                    float mag       = CalculateMissileMagnitude(targetWcd.WeaponClass, targetWeapon.Weight, calcSpeed, targetWeapon.GetModifiedThrustDamageForUsage(usageIndex), 1f, dType);

                    createSubHeader(new TextObject("{=RBM_COM_015}Missile Damage").ToString());
                    setHint(GenerateDamageTable(val => {
                        float raw = Utilities.RBMComputeDamage(targetWcd.WeaponClass.ToString(), dType, mag, val, 1f, out float pen, out float blunt, dmgFactor);
                        return (raw, pen, blunt);
                    }));
                    break;
                }
                }
            }

            static WeaponClass GetBowParams(
                EquipmentElement weapon,
                int idx,
                WeaponComponentData wcd,
                out int drawWeight,
                out float ammoWeightIdeal,
                out float speedCalc)
            {
                bool  isBow      = wcd.WeaponClass == WeaponClass.Bow;
                int   msModifier = weapon.ItemModifier?.HitPoints ?? 0;
                float idealMod   = isBow ? wcd.ItemUsage.Equals("bow") ? 1600f : 1400f : 4000f;

                drawWeight = weapon.GetModifiedMissileSpeedForUsage(idx) + msModifier;
                ammoWeightIdeal = drawWeight / idealMod;
                if (!isBow)
                    ammoWeightIdeal = MathF.Clamp(ammoWeightIdeal, 0f, 0.150f);

                speedCalc = Utilities.calculateMissileSpeed(ammoWeightIdeal, wcd.ItemUsage, drawWeight);
                return isBow ? WeaponClass.Arrow : WeaponClass.Bolt;
            }

            // --- MELEE LOGIC ---
            static void ProcessMeleeWeapon(
                EquipmentElement targetWeapon,
                int targetIdx,
                EquipmentElement compWeapon,
                int compIdx,
                WeaponComponentData targetWcd,
                Action<TextObject, int, int?> addInt,
                Action<TextObject, float, float?, bool> addFloat,
                Action<string> createHeader,
                Action<string> createSubHeader,
                Action<string> setHint,
                Action<string> createSubHeaderComp,
                Action<string> setHintComp)
            {
                SkillObject skill = targetWcd.RelevantSkill;
                int   effectiveSkill   = currentSelectedChar.GetSkillValue(skill);
                float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(effectiveSkill);
                float skillModifier    = Utilities.CalculateSkillModifier(effectiveSkill);

                // Calculate Speeds
                Utilities.CalculateVisualSpeeds(targetWeapon, targetIdx, effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int _);
                float swingSpeed  = swingSpeedReal / Utilities.swingSpeedTransfer;
                float thrustSpeed = thrustSpeedReal / Utilities.thrustSpeedTransfer;

                float swingSpeedComp  = 0f;
                float thrustSpeedComp = 0f;
                if (!compWeapon.IsEmpty) {
                    Utilities.CalculateVisualSpeeds(compWeapon, compIdx, effectiveSkillDR, out int sReal, out int tReal, out int _);
                    swingSpeedComp  = sReal / Utilities.swingSpeedTransfer;
                    thrustSpeedComp = tReal / Utilities.thrustSpeedTransfer;
                }

                // Base Properties
                createHeader(new TextObject("{=RBM_COM_036}RBM Stats").ToString());
                addInt(new TextObject("{=RBM_COM_014}Relevant Skill: "), effectiveSkill, effectiveSkill);
                addFloat(new TextObject("{=RBM_COM_020}Swing Speed, m/s: "), swingSpeed, swingSpeedComp, false);
                addFloat(new TextObject("{=RBM_COM_021}Thrust Speed, m/s: "), thrustSpeed, thrustSpeedComp, false);

                string swingTable      = "";
                string thrustTable     = "";
                string swingTableComp  = "";
                string thrustTableComp = "";

                // Swing Logic
                if (targetWeapon.GetModifiedSwingDamageForUsage(targetIdx) > 0)
                {
                    float sweetSpotMag = CalculateSweetSpotSwingMagnitude(targetWeapon, targetIdx, effectiveSkill, out float sweetSpot);
                    float skillDmg     = Utilities.GetSkillBasedDamage(sweetSpotMag, false, targetWcd.WeaponClass.ToString(), targetWcd.SwingDamageType, effectiveSkillDR, skillModifier, StrikeType.Swing, targetWeapon.Item.Weight);
                    float swingFactor  = MathF.Sqrt(Utilities.getSwingDamageFactor(targetWcd, targetWeapon.ItemModifier));

                    // Calculate Compared Stats (Default to -1 if missing)
                    float sweetSpotComp   = 0f;
                    float swingFactorComp = -1f;
                    if (!compWeapon.IsEmpty) {
                         CalculateSweetSpotSwingMagnitude(compWeapon, compIdx, effectiveSkill, out sweetSpotComp);
                         swingFactorComp = MathF.Sqrt(Utilities.getSwingDamageFactor(compWeapon.Item.GetWeaponWithUsageIndex(compIdx), compWeapon.ItemModifier));
                    }

                    addInt(new TextObject("{=RBM_COM_016}Swing Damage Factor:"),  MathF.Round(swingFactor * 100f), MathF.Round(swingFactorComp * 100f));
                    addInt(new TextObject("{=RBM_COM_018}Swing Sweet Spot, %: "), MathF.Floor(sweetSpot * 100f),   MathF.Floor(sweetSpotComp * 100f));

                    swingTable = GenerateDamageTable(val => {
                        float raw = Utilities.RBMComputeDamage(targetWcd.WeaponClass.ToString(), targetWcd.SwingDamageType, skillDmg, val, 1f, out float pen, out float blunt, swingFactor);
                        return (raw, pen, blunt);
                    });

                    if (!compWeapon.IsEmpty) {
                        var compWcd = compWeapon.Item.GetWeaponWithUsageIndex(compIdx);
                        float sweetSpotMagComp = CalculateSweetSpotSwingMagnitude(compWeapon, compIdx, effectiveSkill, out _);
                        float skillDmgComp = Utilities.GetSkillBasedDamage(sweetSpotMagComp, false, compWcd.WeaponClass.ToString(), compWcd.SwingDamageType, effectiveSkillDR, skillModifier, StrikeType.Swing, compWeapon.Item.Weight);

                        swingTableComp = GenerateDamageTable(val => {
                            float raw = Utilities.RBMComputeDamage(compWcd.WeaponClass.ToString(), compWcd.SwingDamageType, skillDmgComp, val, 1f, out float pen, out float blunt, swingFactorComp);
                            return (raw, pen, blunt);
                        });
                    }
                }

                // Thrust Logic
                if (targetWeapon.GetModifiedThrustDamageForUsage(targetIdx) > 0)
                {
                    float thrustMag    = CalculateThrustMagnitude(targetWeapon, targetIdx, effectiveSkill);
                    float skillDmg     = Utilities.GetSkillBasedDamage(thrustMag, false, targetWcd.WeaponClass.ToString(), targetWcd.ThrustDamageType, effectiveSkillDR, skillModifier, StrikeType.Thrust, targetWeapon.Item.Weight);
                    float thrustFactor = MathF.Sqrt(Utilities.getThrustDamageFactor(targetWcd, targetWeapon.ItemModifier));

                    float thrustFactorComp = -1f;
                    if (!compWeapon.IsEmpty)
                        thrustFactorComp = MathF.Sqrt(Utilities.getThrustDamageFactor(compWeapon.Item.GetWeaponWithUsageIndex(compIdx), compWeapon.ItemModifier));

                    addInt(new TextObject("{=RBM_COM_017}Thrust Damage Factor:"), MathF.Round(thrustFactor * 100f), MathF.Round(thrustFactorComp * 100f));

                    thrustTable = GenerateDamageTable(val => {
                        float raw = Utilities.RBMComputeDamage(targetWcd.WeaponClass.ToString(), targetWcd.ThrustDamageType, skillDmg, val, 1f, out float pen, out float blunt, thrustFactor);
                        return (raw, pen, blunt);
                    });

                    if (!compWeapon.IsEmpty && compWeapon.GetModifiedThrustDamageForUsage(compIdx) > 0f) {
                        var compWcd = compWeapon.Item.GetWeaponWithUsageIndex(compIdx);
                        float thrustMagComp = CalculateThrustMagnitude(compWeapon, compIdx, effectiveSkill);
                        float skillDmgComp = Utilities.GetSkillBasedDamage(thrustMagComp, false, compWcd.WeaponClass.ToString(), compWcd.ThrustDamageType, effectiveSkillDR, skillModifier, StrikeType.Thrust, compWeapon.Item.Weight);
                        
                        thrustTableComp = GenerateDamageTable(val => {
                            float raw = Utilities.RBMComputeDamage(compWcd.WeaponClass.ToString(), compWcd.ThrustDamageType, skillDmgComp, val, 1f, out float pen, out float blunt, thrustFactorComp);
                            return (raw, pen, blunt);
                        });
                    }
                }

                if (targetWeapon.GetModifiedSwingDamageForUsage(targetIdx) > 0) {
                    createSubHeader(new TextObject("{=QeToaiLt}Swing Damage") + " (" + new TextObject("{=RBM_COM_037}Hover") + ")");
                    setHint(swingTable);
                    if (!compWeapon.IsEmpty) {
                        createSubHeaderComp(new TextObject("{=QeToaiLt}Swing Damage") + " (" + new TextObject("{=RBM_COM_037}Hover") + ")");
                        setHintComp(swingTableComp);
                    }
                }
                if (targetWeapon.GetModifiedThrustDamageForUsage(targetIdx) > 0) {
                    createSubHeader(new TextObject("{=dO95yR9b}Thrust Damage") + " (" + new TextObject("{=RBM_COM_037}Hover") + ")");
                    setHint(thrustTable);
                    if (!compWeapon.IsEmpty) {
                        createSubHeaderComp(new TextObject("{=dO95yR9b}Thrust Damage") + " (" + new TextObject("{=RBM_COM_037}Hover") + ")");
                        setHintComp(thrustTableComp);
                    }
                }

                // Developer Mode
                if (RBMConfig.RBMConfig.developerMode && targetWeapon.Item.WeaponDesign?.UsedPieces != null) {
                    createHeader(new TextObject("{=RBM_COM_019}RBM Developer Stats").ToString());
                    foreach (var wde in targetWeapon.Item.WeaponDesign.UsedPieces) {
                        createSubHeader(wde.CraftingPiece.StringId + " " + wde.CraftingPiece.Name);
                        addFloat(new TextObject("{=YvwQL9aa}Weight: "), wde.CraftingPiece.Weight, wde.CraftingPiece.Weight, false);
                        addFloat(new TextObject("{=XUtiwiYP}Length: "), wde.CraftingPiece.Length, wde.CraftingPiece.Length, false);
                    }
                }
            }

            static float CalculateThrustMagnitude(EquipmentElement weapon, int weaponUsageIndex, int relevantSkill)
            {
                var weaponWcd = weapon.Item?.GetWeaponWithUsageIndex(weaponUsageIndex);
                if (weaponWcd == null)
                    return -1f;

                float progressEffect = 1f;
                float thrustWeaponSpeed = (float)weapon.GetModifiedThrustSpeedForUsage(weaponUsageIndex) / Utilities.thrustSpeedTransfer * progressEffect;
                int   ef               = relevantSkill;
                float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);
                float weaponWeight  = weapon.Item.Weight;
                float weaponInertia = weaponWcd.TotalInertia;
                float weaponCOM     = weaponWcd.CenterOfMass;

                switch (weaponWcd.WeaponClass)
                {
                case WeaponClass.LowGripPolearm:
                case WeaponClass.Mace:
                case WeaponClass.OneHandedAxe:
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.TwoHandedMace: {
                    float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                    thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                    thrustWeaponSpeed = thrustWeaponSpeed * 0.75f * thrustskillModifier * progressEffect;
                    break;
                }
                case WeaponClass.TwoHandedPolearm: {
                    float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                    thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                    thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * progressEffect;
                    break;
                }
                case WeaponClass.TwoHandedAxe: {
                    float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                    thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                    thrustWeaponSpeed = thrustWeaponSpeed * 0.9f * thrustskillModifier * progressEffect;
                    break;
                }
                case WeaponClass.OneHandedSword:
                case WeaponClass.Dagger:
                case WeaponClass.TwoHandedSword: {
                    float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                    thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                    thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * progressEffect;
                    break;
                }
                }

                float thrustMagnitude = -1f;
                switch (weaponWcd.WeaponClass)
                {
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.OneHandedSword:
                case WeaponClass.Dagger:
                case WeaponClass.Mace:
                    thrustMagnitude = Utilities.CalculateThrustMagnitudeForOneHandedWeapon(weaponWeight, effectiveSkillDR, thrustWeaponSpeed, 0f, Agent.UsageDirection.AttackDown);
                    break;
                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.TwoHandedSword:
                    thrustMagnitude = Utilities.CalculateThrustMagnitudeForTwoHandedWeapon(weaponWeight, effectiveSkillDR, thrustWeaponSpeed, 0f, Agent.UsageDirection.AttackDown);
                    break;
                //default:
                //    thrustMagnitude = SandboxStrikeMagnitudeModel.CalculateStrikeMagnitudeForThrust(currentSelectedChar, null, thrustWeaponSpeed, weaponWeight, weapon.Item, weaponWcd, 0f, false);
                //    break;
                }

                return thrustMagnitude;
            }

            static float CalculateSweetSpotSwingMagnitude(EquipmentElement weapon, int weaponUsageIndex, int relevantSkill, out float sweetSpot)
            {
                sweetSpot = -1f;
                var weaponWcd = weapon.Item?.GetWeaponWithUsageIndex(weaponUsageIndex);
                if (weaponWcd == null)
                    return sweetSpot;

                const float progressEffect = 1f;
                float swingSpeed = (float)weapon.GetModifiedSwingSpeedForUsage(weaponUsageIndex) / Utilities.swingSpeedTransfer * progressEffect;
                float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(relevantSkill);

                switch (weaponWcd.WeaponClass)
                {
                case WeaponClass.LowGripPolearm:
                case WeaponClass.Mace:
                case WeaponClass.OneHandedAxe:
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.TwoHandedMace: {
                    float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                    swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                    break;
                }
                case WeaponClass.TwoHandedPolearm: {
                    float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                    swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                    break;
                }
                case WeaponClass.TwoHandedAxe: {
                    float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                    swingSpeed = swingSpeed * 0.75f * swingskillModifier * progressEffect;
                    break;
                }
                case WeaponClass.OneHandedSword:
                case WeaponClass.Dagger:
                case WeaponClass.TwoHandedSword: {
                    float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                    swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                    break;
                }
                }
                float weaponWeight  = weapon.Item.Weight;
                float weaponInertia = weaponWcd.TotalInertia;
                float weaponCOM     = weaponWcd.CenterOfMass;

                float sweetSpotMagnitude = -1f;
                for (float currentSpot = 1f; currentSpot > 0.35f; currentSpot -= 0.01f)
                {
                    //float currentSpotMagnitude = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForSwing(currentSelectedChar, null, swingSpeed, currentSpot, weaponWeight,
                    //    weapon.Item, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex),
                    //    weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).GetRealWeaponLength(), weaponInertia, weaponCOM, 0f, false);
                    float currentSpotMagnitude = CombatStatCalculator.CalculateStrikeMagnitudeForSwing(
                        swingSpeed, currentSpot, weaponWeight, weaponWcd.GetRealWeaponLength(), weaponInertia, weaponCOM, 0f);
                    if (currentSpotMagnitude > sweetSpotMagnitude)
                    {
                        sweetSpotMagnitude = currentSpotMagnitude;
                        sweetSpot          = currentSpot;
                    }
                }

                return sweetSpotMagnitude;
            }

            static string GenerateDamageTable(Func<float, (float, float, float)> calculator)
            {
                var sb = new StringBuilder();
                sb.AppendLine(new TextObject("{=RBM_COM_028}A-Armor").ToString())
                  .AppendLine(new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString())
                  .AppendLine(new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString())
                  .AppendLine(new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString());

                string colA = new TextObject("{=RBM_COM_032}A").ToString();
                string colD = new TextObject("{=RBM_COM_033}D").ToString();
                string colP = new TextObject("{=RBM_COM_034}P").ToString();
                string colB = new TextObject("{=RBM_COM_035}B").ToString();

                for (int i = 0; i <= 100; i += 10) {
                    (float raw, float pen, float blunt) = calculator(i);
                    int realDamage = MBMath.ClampInt((int)raw, 0, 2000);
                    sb.AppendLine($"{colA}: {i,-5} {colD}: {realDamage,-5} {colP}: {(int)pen,-5} {colB}: {(int)blunt}");
                }

                return sb.ToString();
            }
        }

        //======================================================================================

        static void GetRBMArmorStatsStrings(
            Equipment equipment,
            out string headStr,
            out string bodyStr,
            out string armStr,
            out string legStr)
        {
            if (equipment == null) {
                headStr = "";
                bodyStr = "";
                armStr  = "";
                legStr  = "";
                return;
            }

            float headArmor     = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Head);
            float neckArmor     = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Neck);
            float shoulderArmor = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.ShoulderLeft);
            float armArmor      = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.ArmLeft);
            float chestArmor    = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Chest);
            float abdomenArmor  = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Abdomen);
            float legsArmor     = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBMHuman(equipment, BoneBodyPartType.Legs);

            headStr = $"{new TextObject("{=EUzxzL9s}Head Armor: ")}{headArmor}\n";
            if (!equipment[EquipmentIndex.Head].IsEmpty) {
                float faceArmor = equipment[EquipmentIndex.Head].GetModifiedBodyArmor();
                headStr += $"{new TextObject("{=RBM_COM_023}Face Armor")}: {faceArmor}\n";
            }
            headStr += $"{new TextObject("{=RBM_COM_024}Neck Armor")}: {neckArmor}";

            bodyStr = $"{new TextObject("{=RBM_COM_025}Shoulder Armor")}: {shoulderArmor}\n" +
                      $"{new TextObject("{=oiSW6MyB}Chest Armor")}: {chestArmor}\n" +
                      $"{new TextObject("{=RBM_COM_026}Abdomen Armor")}: {abdomenArmor}";

            armStr = $"{new TextObject("{=kx7q8ybD}Arm Armor")}: {armArmor}\n";
            if (!equipment[EquipmentIndex.Body].IsEmpty) {
                float underShoulderArmor = (equipment[EquipmentIndex.Body].GetModifiedArmArmor());
                if (!equipment[EquipmentIndex.Cape].IsEmpty) {
                    underShoulderArmor += equipment[EquipmentIndex.Cape].GetModifiedArmArmor();
                }
                armStr += $"{new TextObject("{=RBM_COM_027}Lower Shoulder Armor")}: {underShoulderArmor}";
            }

            legStr = $"{new TextObject("{=U8VHRdwF}Leg Armor: ")}{legsArmor}";
        }

        [HarmonyPatch(typeof(SPInventoryVM))]
        [HarmonyPatch("UpdateCharacterArmorValues")]
        class UpdateCharacterArmorValuesPatch
        {
            static void Postfix(ref SPInventoryVM __instance, CharacterObject ____currentCharacter)
            {
                if (____currentCharacter == null)
                    return;
                currentSelectedChar = ____currentCharacter;
                Equipment equipment = ____currentCharacter.Equipment;
                GetRBMArmorStatsStrings(
                    equipment,
                    out string headStr,
                    out string bodyStr,
                    out string armStr,
                    out string legStr);
                __instance.HeadArmorHint = new HintViewModel(new TextObject(headStr));
                __instance.BodyArmorHint = new HintViewModel(new TextObject(bodyStr));
                __instance.ArmArmorHint  = new HintViewModel(new TextObject(armStr));
                __instance.LegArmorHint  = new HintViewModel(new TextObject(legStr));
            }
        }

        [HarmonyPatch(typeof(SPInventoryVM))]
        [HarmonyPatch("RefreshValues")]
        class RefreshValuesPatch
        {
            static void Postfix(ref SPInventoryVM __instance, CharacterObject ____currentCharacter)
            {
                if (____currentCharacter == null)
                    return;
                Equipment equipment = ____currentCharacter.Equipment;
                GetRBMArmorStatsStrings(
                    equipment,
                    out string headStr,
                    out string bodyStr,
                    out string armStr,
                    out string legStr);
                __instance.HeadArmorHint = new HintViewModel(new TextObject(headStr));
                __instance.BodyArmorHint = new HintViewModel(new TextObject(bodyStr));
                __instance.ArmArmorHint  = new HintViewModel(new TextObject(armStr));
                __instance.LegArmorHint  = new HintViewModel(new TextObject(legStr));
            }
        }

        //[HarmonyPatch(typeof(EncyclopediaUnitPageVM))]
        //[HarmonyPatch("RefreshValues")]
        //private class EncyclopediaUnitPageVMRefreshValuesPatch
        //{
        //    private static void Postfix(ref EncyclopediaUnitPageVM __instance, CharacterObject ____character)
        //    {
        //        if (__instance.EquipmentSetSelector != null)
        //        {
        //            equipmentSetindex = __instance.EquipmentSetSelector.SelectedIndex;
        //        }
        //        currentSelectedChar = ____character;
        //    }
        //}

        [HarmonyPatch(typeof(EncyclopediaUnitPageVM))]
        [HarmonyPatch("OnEquipmentSetChange")]
        class EncyclopediaUnitPageVOnEquipmentSetChangePatch
        {
            static void Postfix(ref EncyclopediaUnitPageVM __instance, CharacterObject ____character)
            {
                if (__instance.EquipmentSetSelector != null)
                    equipmentSetindex = __instance.EquipmentSetSelector.SelectedIndex;
                currentSelectedChar = ____character;
            }
        }
    }
}