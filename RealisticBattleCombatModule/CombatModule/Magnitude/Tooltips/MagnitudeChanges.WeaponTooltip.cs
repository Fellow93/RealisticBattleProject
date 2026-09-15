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

                                int realDamageCompared = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).WeaponClass.ToString(),
                                    comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).SwingDamageType, skillBasedDamageCompared, i, 1f, out float penetratedDamageCompared, out float bluntForceCompared, swingDamageFactorCompared, null, false)), 0, 2000);

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

                                int realDamageCompared = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).WeaponClass.ToString(),
                                comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex).ThrustDamageType, skillBasedDamageCompared, i, 1f, out float penetratedDamageCompared, out float bluntForceCompared, thrustDamageFactorCompared, null, false)), 0, 2000);

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
            private static readonly MethodInfo methodAddFloatProperty = typeof(ItemMenuVM).GetMethod("AddFloatProperty", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(TextObject), typeof(float), typeof(float?), typeof(bool) }, null);
            private static readonly MethodInfo methodAddIntProperty = typeof(ItemMenuVM).GetMethod("AddIntProperty", BindingFlags.NonPublic | BindingFlags.Instance);
            private static readonly MethodInfo methodCreateProperty = typeof(ItemMenuVM).GetMethod("CreateProperty", BindingFlags.NonPublic | BindingFlags.Instance);

            private static void Postfix(ref ItemMenuVM __instance, in EquipmentElement targetWeapon, int targetWeaponUsageIndex, EquipmentElement comparedWeapon, int comparedWeaponUsageIndex)
            {

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
                            float ammoWeightIdealModifier = 1750f;

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
    }
}
