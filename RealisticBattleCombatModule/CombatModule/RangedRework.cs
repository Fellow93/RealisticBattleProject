using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using HarmonyLib;
using System.Reflection;
using JetBrains.Annotations;
using TaleWorlds.Library;
using System.Collections;
using System.Collections.Generic;
using TaleWorlds.Localization;
using System;
using TaleWorlds.Engine;
using static TaleWorlds.MountAndBlade.Agent;
using static TaleWorlds.MountAndBlade.Mission;
using static TaleWorlds.Core.ItemObject;
using NetworkMessages.FromServer;

namespace RBMCombat
{
    public class RangedRework
    {
        public static Dictionary<TextObject, int> originalItemSwingSpeed = new Dictionary<TextObject, int> { };
        public static Dictionary<TextObject, int> originalItemThrustSpeed = new Dictionary<TextObject, int> { };
        public static Dictionary<TextObject, int> originalItemHandling = new Dictionary<TextObject, int> { };
        public static Dictionary<string, RangedWeaponStats> rangedWeaponStats = new Dictionary<string, RangedWeaponStats>(new RangedWeaponStatsComparer());
        public static Dictionary<string, MissionWeapon> rangedWeaponMW = new Dictionary<string, MissionWeapon> { };

        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("FinishMissionLoading")]
        public class MissionLoadChangeParameters
        {
            static void Postfix()
            {
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionArrow, 0.0015f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionJavelin, 0.00215f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionAxe, 0.01f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionKnife, 0.01f);
                ManagedParameters.SetParameter(ManagedParametersEnum.MissileMinimumDamageToStick, 12.5f);

                ManagedParameters.SetParameter(ManagedParametersEnum.MakesRearAttackDamageThreshold, 13f);
                ManagedParameters.SetParameter(ManagedParametersEnum.NonTipThrustHitDamageMultiplier, 1f);
                //ManagedParameters.SetParameter(ManagedParametersEnum.SwingHitWithArmDamageMultiplier, 1.0f);
                //ManagedParameters.SetParameter(ManagedParametersEnum.ThrustHitWithArmDamageMultiplier, 0.02f);
                //ManagedParameters.SetParameter(ManagedParametersEnum.NonTipThrustHitDamageMultiplier, 1.0f);

                //ManagedParameters.SetParameter(ManagedParametersEnum.OverSwingCombatSpeedGraphSecondMaximumPoint, 0.7f);

                //ManagedParameters.SetParameter(ManagedParametersEnum.ThrustCombatSpeedGraphZeroProgressValue, 0.3f);
                //ManagedParameters.SetParameter(ManagedParametersEnum.ThrustCombatSpeedGraphFirstMaximumPoint, 0.05f);
                //ManagedParameters.SetParameter(ManagedParametersEnum.ThrustCombatSpeedGraphSecondMaximumPoint, 0.95f);

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

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("WeaponEquipped")]
        class OverrideWeaponEquipped
        {
            static bool Prefix(ref Agent __instance, EquipmentIndex equipmentSlot, in WeaponData weaponData, ref WeaponStatsData[] weaponStatsData, in WeaponData ammoWeaponData,ref WeaponStatsData[] ammoWeaponStatsData, GameEntity weaponEntity, bool removeOldWeaponFromScene, bool isWieldedOnSpawn)
            {
                if(weaponStatsData != null)
                {
                    for (int i = 0; i < weaponStatsData.Length; i++)
                    {
                        SkillObject skill = (weaponData.GetItemObject() == null) ? DefaultSkills.Athletics : weaponData.GetItemObject().RelevantSkill;
                        if(skill != null) {
                            int ef = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance.Character, __instance.Origin, __instance.Formation, skill);
                            float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);

                            MissionWeapon missionWeapon = __instance.Equipment[equipmentSlot];
                            EquipmentElement ee = new EquipmentElement(missionWeapon.Item);
                            Utilities.CalculateVisualSpeeds(ee, i, effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal);

                            if(swingSpeedReal >= 0 && thrustSpeedReal >= 0 && handlingReal >= 0)
                            {
                                weaponStatsData[i].SwingSpeed = swingSpeedReal;
                                weaponStatsData[i].ThrustSpeed = thrustSpeedReal;
                                weaponStatsData[i].DefendSpeed = handlingReal;
                            }

                            if((WeaponClass)weaponStatsData[i].WeaponClass == WeaponClass.Bow)
                            {
                                int thrustSpeed = missionWeapon.GetModifiedThrustSpeedForCurrentUsage();
                                if (RBMConfig.RBMConfig.realisticRangedReload.Equals("1") || RBMConfig.RBMConfig.realisticRangedReload.Equals("2"))
                                {
                                    float DrawSpeedskillModifier = 1 + (ef * 0.01f);
                                    weaponStatsData[i].ThrustSpeed = MathF.Ceiling((thrustSpeed * 0.1f) * DrawSpeedskillModifier);
                                }
                                if (RBMConfig.RBMConfig.realisticRangedReload.Equals("0"))
                                {
                                    weaponStatsData[i].ThrustSpeed = MathF.Ceiling(thrustSpeed * 0.45f);
                                }

                                MissionWeapon mw = __instance.Equipment[equipmentSlot];
                                RangedWeaponStats rws;
                                if (rangedWeaponStats.TryGetValue(mw.GetModifiedItemName().ToString(), out rws))
                                {
                                    if ((ef) < rws.getDrawWeight() + 9f) // 70 more skill needed to unlock speed shooting
                                    {
                                        __instance.Equipment[equipmentSlot].GetWeaponComponentDataForUsage(0).WeaponFlags |= WeaponFlags.UnloadWhenSheathed;
                                        weaponStatsData[i].WeaponFlags = (ulong)__instance.Equipment[equipmentSlot].GetWeaponComponentDataForUsage(0).WeaponFlags;
                                    }
                                    else
                                    {
                                        __instance.Equipment[equipmentSlot].GetWeaponComponentDataForUsage(0).WeaponFlags &= ~WeaponFlags.UnloadWhenSheathed;
                                        weaponStatsData[i].WeaponFlags = (ulong)__instance.Equipment[equipmentSlot].GetWeaponComponentDataForUsage(0).WeaponFlags;
                                    }
                                }
                            }

                            switch (weaponStatsData[i].WeaponClass)
                            {
                                case (int)WeaponClass.OneHandedPolearm:
                                case (int)WeaponClass.LowGripPolearm:
                                    {
                                        weaponStatsData[i].MissileSpeed = Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentSlot].GetWeight() / __instance.Equipment[equipmentSlot].Amount, (int)Utilities.throwableCorrectionSpeed, effectiveSkillDR);
                                        break;
                                    }
                                case (int)WeaponClass.Javelin:
                                    {
                                        weaponStatsData[i].MissileSpeed = Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentSlot].GetWeight() / __instance.Equipment[equipmentSlot].Amount, (int)Utilities.throwableCorrectionSpeed, effectiveSkillDR);
                                        break;
                                    }
                                case (int)WeaponClass.ThrowingAxe:
                                case (int)WeaponClass.ThrowingKnife:
                                case (int)WeaponClass.Dagger:
                                    {
                                        weaponStatsData[i].MissileSpeed = Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentSlot].GetWeight() / __instance.Equipment[equipmentSlot].Amount, 0, effectiveSkillDR);
                                        break;
                                    }
                                case (int)WeaponClass.Stone:
                                    {
                                        weaponStatsData[i].MissileSpeed = Utilities.assignStoneMissileSpeed(__instance.Equipment[equipmentSlot]);
                                        break;
                                    }
                            }
                        }
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("EquipItemsFromSpawnEquipment")]
        class OverrideEquipItemsFromSpawnEquipment
        {
            static bool Prefix(ref Agent __instance)
            {
                ArrayList stringRangedWeapons = new ArrayList();
                MissionWeapon arrow = MissionWeapon.Invalid;
                bool firstProjectile = true;

                PropertyInfo swingSpeedProperty = typeof(WeaponComponentData).GetProperty("SwingSpeed");
                swingSpeedProperty.DeclaringType.GetProperty("SwingSpeed");

                PropertyInfo thrustSpeedProperty = typeof(WeaponComponentData).GetProperty("ThrustSpeed");
                thrustSpeedProperty.DeclaringType.GetProperty("ThrustSpeed");

                PropertyInfo handlingProperty = typeof(WeaponComponentData).GetProperty("Handling");
                handlingProperty.DeclaringType.GetProperty("Handling");

                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {

                    if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                    {
                        MissionWeapon missionWeapon = __instance.Equipment[equipmentIndex];
                        WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

                        //SkillObject skill = (mw.CurrentUsageItem == null) ? DefaultSkills.Athletics : mw.CurrentUsageItem.RelevantSkill;
                        //if (skill != null)
                        //{
                        //    int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance.Character, __instance.Origin, __instance.Formation, skill);

                        //if ((wsd[0].WeaponClass == (int)WeaponClass.SmallShield) || (wsd[0].WeaponClass == (int)WeaponClass.LargeShield))
                        //{
                        //    __instance.AttachWeaponToWeapon(equipmentIndex, __instance.Equipment[equipmentIndex], __instance.GetWeaponEntityFromEquipmentSlot(equipmentIndex), ref wsd[0].WeaponFrame);
                        //    __instance.AttachWeaponToBone(__instance.Equipment[equipmentIndex], __instance.AgentVisuals.GetEntity(), 5, ref wsd[0].WeaponFrame);
                        //}
                        //if ((wsd[0].WeaponClass == (int)WeaponClass.OneHandedSword) || (wsd[0].WeaponClass == (int)WeaponClass.Dagger) || (wsd[0].WeaponClass == (int)WeaponClass.LowGripPolearm)
                        //     || (wsd[0].WeaponClass == (int)WeaponClass.Mace) || (wsd[0].WeaponClass == (int)WeaponClass.OneHandedAxe) || (wsd[0].WeaponClass == (int)WeaponClass.OneHandedPolearm)
                        //      || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedAxe) || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedMace) || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedPolearm)
                        //       || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedSword))
                        //{
                        //    float skillModifier = 1f + effectiveSkill / 1000f;

                        //    WeaponComponentData weapon = mw.CurrentUsageItem;

                        //    int swingSpeed = (int)swingSpeedProperty.GetValue(weapon, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                        //    originalItemSwingSpeed[mw.GetModifiedItemName()] = swingSpeed;
                        //    swingSpeedProperty.SetValue(weapon, MathF.Ceiling((swingSpeed * 0.83f) * skillModifier), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                        //    int thrustSpeed = (int)thrustSpeedProperty.GetValue(weapon, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                        //    originalItemThrustSpeed[mw.GetModifiedItemName()] = thrustSpeed;
                        //    thrustSpeedProperty.SetValue(weapon, MathF.Ceiling((thrustSpeed * 0.83f) * skillModifier), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                        //    int handling = (int)handlingProperty.GetValue(weapon, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                        //    originalItemHandling[mw.GetModifiedItemName()] = handling;
                        //    handlingProperty.SetValue(weapon, MathF.Ceiling((handling * 0.83f) * skillModifier), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        //}
                        if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                        {
                            RangedWeaponStats rangedWeaponStatNew = new RangedWeaponStats(missionWeapon.CurrentUsageItem.MissileSpeed);
                            RangedWeaponStats rangedWeaponStatOld;
                            //if (missionWeapon.ItemModifier != null)
                            //{
                            //    PropertyInfo propertyItemModifier = typeof(MissionWeapon).GetProperty("ItemModifier");
                            //    propertyItemModifier.DeclaringType.GetProperty("ItemModifier");
                            //    ItemModifier itemModifier = null;
                            //    propertyItemModifier.SetValue(missionWeapon, itemModifier, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                            //}
                            if (!rangedWeaponStats.TryGetValue(missionWeapon.GetModifiedItemName().ToString(), out rangedWeaponStatOld))
                            {
                                rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()] = rangedWeaponStatNew;
                            }
                            stringRangedWeapons.Add(missionWeapon);
                        }
                        if ((wsd[0].WeaponClass == (int)WeaponClass.Arrow) || (wsd[0].WeaponClass == (int)WeaponClass.Bolt))
                        {
                            if (firstProjectile)
                            {
                                arrow = missionWeapon;
                                firstProjectile = false;
                            }
                        }
                        //if ((wsd[0].WeaponClass == (int)WeaponClass.OneHandedPolearm) || (wsd[0].WeaponClass == (int)WeaponClass.LowGripPolearm))
                        //{
                        //    for (int i = 0; i < wsd.Length; i++)
                        //    {
                        //        if (wsd[i].MissileSpeed != 0)
                        //        {
                        //            Utilities.assignThrowableMissileSpeed(mw, i, 10, effectiveSkill);
                        //        }
                        //    }
                        //}
                        //if (wsd[0].WeaponClass == (int)WeaponClass.Javelin)
                        //{
                        //    for (int i = 0; i < wsd.Length; i++)
                        //    {
                        //        if (wsd[i].MissileSpeed != 0)
                        //        {
                        //            Utilities.assignThrowableMissileSpeed(mw, i, 10, effectiveSkill);
                        //        }
                        //    }
                        //}
                        //if ((wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe) || (wsd[0].WeaponClass == (int)WeaponClass.ThrowingKnife))
                        //{
                        //    for (int i = 0; i < wsd.Length; i++)
                        //    {
                        //        if (wsd[i].MissileSpeed != 0)
                        //        {
                        //            Utilities.assignThrowableMissileSpeed(mw, i, 5, effectiveSkill);
                        //        }
                        //    }
                        //}
                        //if (wsd[0].WeaponClass == (int)WeaponClass.Stone)
                        //{
                        //    for (int i = 0; i < wsd.Length; i++)
                        //    {
                        //        if (wsd[i].MissileSpeed != 0)
                        //        {
                        //            Utilities.assignStoneMissileSpeed(mw, i);
                        //        }
                        //    }
                        //}
                        //}
                    }
                }

                PropertyInfo propertyMissileSpeed = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                propertyMissileSpeed.DeclaringType.GetProperty("MissileSpeed");

                foreach (MissionWeapon missionWeapon in stringRangedWeapons)
                {
                    int calculatedMissileSpeed = 50;
                    if (!missionWeapon.Equals(MissionWeapon.Invalid) && !arrow.Equals(MissionWeapon.Invalid))
                    {
                        float ammoWeight = arrow.GetWeight() / arrow.Amount;

                        int msModifier = 0;
                        if (missionWeapon.ItemModifier != null)
                        {
                            msModifier = missionWeapon.ItemModifier.ModifyHitPoints(50) - 50;
                        }

                        calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon.CurrentUsageItem.ItemUsage, missionWeapon.CurrentUsageItem.MissileSpeed + msModifier); //rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight());
                        rangedWeaponMW[missionWeapon.GetModifiedItemName().ToString()] = missionWeapon;
                        
                        propertyMissileSpeed.SetValue(missionWeapon.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                    else if (!missionWeapon.Equals(MissionWeapon.Invalid))
                    {
                        rangedWeaponMW[missionWeapon.GetModifiedItemName().ToString()] = missionWeapon;
                        propertyMissileSpeed.SetValue(missionWeapon.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                }

                return true;
            }
            static void Postfix(Agent __instance)
            {
                PropertyInfo propertyMissileSpeed = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                propertyMissileSpeed.DeclaringType.GetProperty("MissileSpeed");

                PropertyInfo swingSpeedProperty = typeof(WeaponComponentData).GetProperty("SwingSpeed");
                swingSpeedProperty.DeclaringType.GetProperty("SwingSpeed");

                PropertyInfo thrustSpeedProperty = typeof(WeaponComponentData).GetProperty("ThrustSpeed");
                thrustSpeedProperty.DeclaringType.GetProperty("ThrustSpeed");

                PropertyInfo handlingProperty = typeof(WeaponComponentData).GetProperty("Handling");
                handlingProperty.DeclaringType.GetProperty("Handling");

                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {

                    if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                    {
                        MissionWeapon mw = __instance.Equipment[equipmentIndex];
                        WeaponStatsData[] wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData();
                        WeaponComponentData weapon = __instance.Equipment[equipmentIndex].CurrentUsageItem;

                        //if ((wsd[0].WeaponClass == (int)WeaponClass.OneHandedSword) || (wsd[0].WeaponClass == (int)WeaponClass.Dagger) || (wsd[0].WeaponClass == (int)WeaponClass.LowGripPolearm)
                        //     || (wsd[0].WeaponClass == (int)WeaponClass.Mace) || (wsd[0].WeaponClass == (int)WeaponClass.OneHandedAxe) || (wsd[0].WeaponClass == (int)WeaponClass.OneHandedPolearm)
                        //      || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedAxe) || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedMace) || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedPolearm)
                        //       || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedSword))
                        //{
                        //    swingSpeedProperty.SetValue(weapon, originalItemSwingSpeed[mw.GetModifiedItemName()], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                        //    thrustSpeedProperty.SetValue(weapon, originalItemThrustSpeed[mw.GetModifiedItemName()], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                        //    handlingProperty.SetValue(weapon, originalItemHandling[mw.GetModifiedItemName()], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        //}

                        if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                        {

                            propertyMissileSpeed.SetValue(__instance.Equipment[equipmentIndex].CurrentUsageItem, rangedWeaponStats[mw.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        }
                    }
                }
                //_oldMissileSpeeds.Clear();
                //_oldSwordSpeeds.Clear();
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("OnAgentShootMissile")]
        [UsedImplicitly]
        [MBCallback]
        class OverrideOnAgentShootMissile
        {

            //private static int _oldMissileSpeed;
            static bool Prefix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, ref Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
            {
                MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

                if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle && !shooterAgent.IsMainAgent && (wsd[0].WeaponClass == (int)WeaponClass.Javelin || wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe))
                {
                    //float shooterSpeed = shooterAgent.MovementVelocity.Normalize();
                    Agent targetAgent = shooterAgent.GetTargetAgent();
                    if (targetAgent != null)
                    {
                        if (wsd[0].WeaponClass == (int)WeaponClass.Javelin)
                        {
                            float relativeSpeed = (targetAgent.Velocity - shooterAgent.Velocity).Length;
                            float relativeModifier = Vec3.DotProduct(shooterAgent.Velocity.NormalizedCopy(), targetAgent.Velocity.NormalizedCopy());
                            float shooterSpeed = shooterAgent.Velocity.Length;
                            if (shooterSpeed > 0)
                            {
                                float shooterRelativeSpeed = shooterSpeed * relativeModifier;
                                double rotRad;
                                rotRad = +(0.0174533 * shooterRelativeSpeed) * 3f;
                                if (wsd[0].WeaponClass == (int)WeaponClass.Javelin)
                                {
                                    rotRad = +(0.0174533 * shooterRelativeSpeed) / 1.1f;
                                }
                                if (shooterRelativeSpeed > 0)
                                {
                                    rotRad = 0;
                                }
                                float vecLength = velocity.Length;
                                double currentRad = (double)Math.Acos(velocity.z / vecLength);
                                float newZ = velocity.Length * ((float)Math.Cos(currentRad - rotRad));
                                velocity.z = newZ;
                            }
                        }
                        if (wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe)
                        {
                            double rotRad;
                            rotRad = 0.0174533 * -5f;
                            float vecLength = velocity.Length;
                            double currentRad = (double)Math.Acos(velocity.z / vecLength);
                            float newZ = velocity.Length * ((float)Math.Cos(currentRad - rotRad));
                            velocity.z = newZ;
                        }
                    }
                    else
                    {
                        if (!shooterAgent.HasMount)
                        {
                            velocity.z = velocity.z - 1.4f;
                        }
                        else
                        {
                            velocity.z = velocity.z - 2f;
                        }
                    }
                    
                }

                if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                {
                    float ammoWeightSum = missionWeapon.AmmoWeapon.GetWeight();
                    float ammoCount = missionWeapon.AmmoWeapon.Amount;
                    float ammoWeight = ammoWeightSum / ammoCount;
                    //PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                    //property2.DeclaringType.GetProperty("MissileSpeed");
                    //property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                    RangedWeaponStats rws;
                    if (!rangedWeaponStats.TryGetValue(missionWeapon.GetModifiedItemName().ToString(), out rws))
                    {
                        rangedWeaponMW[missionWeapon.GetModifiedItemName().ToString()] = missionWeapon;
                        rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()] = new RangedWeaponStats(missionWeapon.CurrentUsageItem.MissileSpeed);
                    }

                    string min = missionWeapon.GetModifiedItemName().ToString();

                    int msModifier = 0;
                    if (missionWeapon.ItemModifier != null)
                    {
                        msModifier = missionWeapon.ItemModifier.ModifyHitPoints(50) - 50;
                    }
                    int calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon.CurrentUsageItem.ItemUsage, rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight() + msModifier);// rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight());

                    Vec3 shooterAgentVelocity = new Vec3(shooterAgent.Velocity, -1);
                    Vec3 myVelocity = new Vec3(velocity, -1);

                    myVelocity.Normalize();

                    float shooterAgentSpeed = Vec3.DotProduct(shooterAgentVelocity, myVelocity);

                    Vec3 modifierVec = shooterAgentVelocity + myVelocity;

                    velocity.x = myVelocity.x * (calculatedMissileSpeed + shooterAgentSpeed);
                    velocity.y = myVelocity.y * (calculatedMissileSpeed + shooterAgentSpeed);
                    velocity.z = myVelocity.z * (calculatedMissileSpeed + shooterAgentSpeed);

                    PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                    property2.DeclaringType.GetProperty("MissileSpeed");
                    property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                }

                //if (shooterAgent != null && !shooterAgent.IsAIControlled && !BannerlordConfig.DisplayTargetingReticule && (wsd[0].WeaponClass == (int)WeaponClass.Bow || wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                if (shooterAgent != null && !shooterAgent.IsAIControlled && RBMConfig.RBMConfig.rbmCombatEnabled && RBMConfig.RBMConfig.realisticArrowArc && (wsd[0].WeaponClass == (int)WeaponClass.Bow || wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                {
                    //radians = degrees * (pi / 180)
                    //degrees = radians * (180 / pi)

                    double rotRad = 0.083141f;
                    float vecLength = velocity.Length;
                    double currentRad = (double)Math.Acos(velocity.z / vecLength);
                    float newZ = velocity.Length * ((float)Math.Cos(currentRad - rotRad));
                    velocity.z = newZ;
                }

                return true;
            }

            static void Postfix(ref Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
            {
                MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

                if (shooterAgent != null && Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.Siege && !shooterAgent.IsMainAgent && (wsd[0].WeaponClass == (int)WeaponClass.Javelin || wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe) && shooterAgent.Team?.Side == BattleSideEnum.Defender)
                {
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (shooterAgent.Equipment[equipmentIndex].IsAnyConsumable() && shooterAgent.Equipment[equipmentIndex].Amount <= 2)
                        {
                            shooterAgent.SetWeaponAmountInSlot(equipmentIndex, shooterAgent.Equipment[equipmentIndex].ModifiedMaxAmount, enforcePrimaryItem: true);
                        }
                    }
                }
                if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                {
                    PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                    property2.DeclaringType.GetProperty("MissileSpeed");
                    property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }
            }
        }

        //[HarmonyPatch(typeof(SandboxAgentApplyDamageModel))]
        //class CalculateEffectiveMissileSpeedPatch
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch("CalculateEffectiveMissileSpeed")]
        //    static bool PrefixCalculateEffectiveMissileSpeed(Agent attackerAgent, WeaponComponentData missileWeapon, ref Vec3 missileStartDirection, float missileStartSpeed,ref float __result)
        //    {
        //        if(attackerAgent != null && !attackerAgent.IsPlayerControlled)
        //        {
        //            __result = missileStartSpeed;
        //            return false;
        //        }
        //        return true;
        //    }
        //}

        [UsedImplicitly]
        [MBCallback]
        [HarmonyPatch(typeof(Mission))]
        class OverrideEndMission
        {
            [HarmonyPrefix]
            [HarmonyPatch("EndMission")]
            static bool PrefixOnEndMissionResult(ref Mission __instance)
            {
                foreach (KeyValuePair<string, MissionWeapon> mw in rangedWeaponMW) {
                    WeaponStatsData[] wsd = mw.Value.GetWeaponStatsData();
                    if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                    {
                        PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                        property2.DeclaringType.GetProperty("MissileSpeed");
                        property2.SetValue(mw.Value.CurrentUsageItem, rangedWeaponStats[mw.Value.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                }
                return true;
            }
        }

        //[HarmonyPatch(typeof(MissionEquipment))]
        //class OverrideCheckLoadedAmmos
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch("CheckLoadedAmmos")]
        //    static bool PrefixOnTick(ref MissionEquipment __instance, ref MissionWeapon[] ____weaponSlots)
        //    {
        //        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
        //        {
        //            if (!__instance[equipmentIndex].IsEmpty && __instance[equipmentIndex].Item.PrimaryWeapon.WeaponClass == WeaponClass.Crossbow)
        //            {
        //                __instance.GetAmmoCountAndIndexOfType(__instance[equipmentIndex].Item.Type, out var _, out var eIndex);
        //                if (eIndex != EquipmentIndex.None)
        //                {
        //                    MissionWeapon ammoWeapon = ____weaponSlots[(int)eIndex].Consume(Math.Min(__instance[equipmentIndex].MaxAmmo, ____weaponSlots[(int)eIndex].Amount));
        //                    ____weaponSlots[(int)equipmentIndex].ReloadAmmo(ammoWeapon, 2);
        //                }
        //            }
        //        }
        //        return false;
        //    }
        //}

        //[MBCallback]
        //[HarmonyPatch(typeof(Agent))]
        //class OverrideOnWeaponAmmoReload
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch("OnWeaponAmmoReload")]
        //    static bool PrefixOnWeaponAmmoReload(ref Agent __instance, EquipmentIndex slotIndex, EquipmentIndex ammoSlotIndex, short totalAmmo)
        //    {
        //        if (__instance.Equipment[slotIndex].CurrentUsageItem.IsRangedWeapon)
        //        {
        //            if (__instance.Equipment[slotIndex].CurrentUsageItem.WeaponClass == WeaponClass.Bow || __instance.Equipment[slotIndex].CurrentUsageItem.WeaponClass == WeaponClass.Crossbow)
        //            {
        //                bool testik = true;
        //            }
        //        }
        //        return true;
        //    }
        //}

        //[HarmonyPatch(typeof(MissionWeapon))]
        //[HarmonyPatch("ReloadAmmo")]
        //class OnWieldedItemIndexChangePatch
        //{

        //}

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("OnWieldedItemIndexChange")]
        class OnWieldedItemIndexChangePatch
        {
            static void Postfix(ref Agent __instance, bool isOffHand, bool isWieldedInstantly, bool isWieldedOnSpawn)
            {
                EquipmentIndex wieldedItemIndex = __instance.GetWieldedItemIndex(HandIndex.MainHand);
                if (wieldedItemIndex != EquipmentIndex.None)
                {
                    bool isBowWielded = false;
                    WeaponStatsData weaponStatsData = __instance.Equipment[wieldedItemIndex].GetWeaponStatsData()[0];
                    WeaponData weaponData = __instance.Equipment[wieldedItemIndex].GetWeaponData(true);
                    if (weaponStatsData.WeaponClass == (int)WeaponClass.Bow)
                    {
                        isBowWielded = true;
                    }
                    //EquipmentIndex firstAmmo = EquipmentIndex.None;
                    //for (EquipmentIndex ammoIndex = EquipmentIndex.WeaponItemBeginSlot; ammoIndex < EquipmentIndex.NumAllWeaponSlots; ammoIndex++)
                    //{
                    //    if (__instance.Equipment[ammoIndex].GetWeaponStatsData() != null && __instance.Equipment[ammoIndex].GetWeaponStatsData().Length > 0)
                    //    {
                    //        WeaponStatsData wsd = __instance.Equipment[ammoIndex].GetWeaponStatsData()[0];
                    //        if (wsd.WeaponClass == (int)WeaponClass.Arrow)
                    //        {
                    //            firstAmmo = ammoIndex;
                    //            continue;
                    //        }
                    //    }
                    //}
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (__instance.Equipment[equipmentIndex].GetWeaponStatsData() != null && __instance.Equipment[equipmentIndex].GetWeaponStatsData().Length > 0)
                        {
                            WeaponData wd = __instance.Equipment[equipmentIndex].GetWeaponData(true);
                            WeaponStatsData wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData()[0];
                            if (wsd.WeaponClass == (int)WeaponClass.Bow)
                            {
                                MissionWeapon mw = __instance.Equipment[equipmentIndex];
                                if (isBowWielded)
                                {
                                    SkillObject skill = (wd.GetItemObject() == null) ? DefaultSkills.Athletics : weaponData.GetItemObject().RelevantSkill;
                                    if (skill != null)
                                    {
                                        int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance.Character, __instance.Origin, __instance.Formation, skill);

                                        RangedWeaponStats rws;
                                        if (rangedWeaponStats.TryGetValue(mw.GetModifiedItemName().ToString(), out rws))
                                        {
                                            if ((effectiveSkill) < rws.getDrawWeight() + 9f) // 70 more skill needed to unlock speed shooting
                                            {
                                                __instance.Equipment[equipmentIndex].GetWeaponComponentDataForUsage(0).WeaponFlags |= WeaponFlags.UnloadWhenSheathed;
                                                wsd.WeaponFlags = (ulong)__instance.Equipment[equipmentIndex].GetWeaponComponentDataForUsage(0).WeaponFlags;
                                            }
                                            else
                                            {
                                                __instance.Equipment[equipmentIndex].GetWeaponComponentDataForUsage(0).WeaponFlags &= ~WeaponFlags.UnloadWhenSheathed;
                                                wsd.WeaponFlags = (ulong)__instance.Equipment[equipmentIndex].GetWeaponComponentDataForUsage(0).WeaponFlags;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    __instance.Equipment[equipmentIndex].GetWeaponComponentDataForUsage(0).WeaponFlags |= WeaponFlags.UnloadWhenSheathed;
                                    __instance.Equipment[equipmentIndex].GetWeaponStatsData()[0].WeaponFlags = (ulong)__instance.Equipment[equipmentIndex].GetWeaponComponentDataForUsage(0).WeaponFlags;
                                    
                                    MissionWeapon mwa = mw.AmmoWeapon;
                                    int ammoInHandCount = mwa.Amount;
                                    if(mwa.Amount > 0)
                                    {
                                        __instance.Equipment.GetAmmoCountAndIndexOfType(mw.Item.Type, out var ammouCount, out var eIndex);
                                        if(eIndex != EquipmentIndex.None)
                                        {
                                            __instance.SetReloadAmmoInSlot(equipmentIndex, eIndex, Convert.ToInt16(-ammoInHandCount));
                                            __instance.SetWeaponReloadPhaseAsClient(equipmentIndex, 0);
                                            if (__instance.Equipment[eIndex].Amount == __instance.Equipment[eIndex].ModifiedMaxAmount)
                                            {
                                                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                                                {
                                                    if (!__instance.Equipment[i].IsEmpty && !__instance.Equipment[eIndex].IsEmpty && 
                                                        __instance.Equipment[i].Item != null && __instance.Equipment[eIndex].Item != null &&
                                                        __instance.Equipment[i].Item.PrimaryWeapon != null && __instance.Equipment[eIndex].Item.PrimaryWeapon != null)
                                                    {
                                                        if (i != eIndex)
                                                        {
                                                            if (__instance.Equipment[i].IsSameType(__instance.Equipment[eIndex]))
                                                            {
                                                                __instance.SetWeaponAmountInSlot(i, Convert.ToInt16(__instance.Equipment[i].Amount + ammoInHandCount), enforcePrimaryItem: true);
                                                                break;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                __instance.SetWeaponAmountInSlot(eIndex, Convert.ToInt16(__instance.Equipment[eIndex].Amount + ammoInHandCount), enforcePrimaryItem: true);
                                            }
                                        }
                                    }
                                    
                                }
                            }
                        }
                    }
                }
            }
        }

    }

    [HarmonyPatch(typeof(RangedSiegeWeapon))]
    class OverrideRangedSiegeWeapon
    {
        //[HarmonyPrefix]
        //[HarmonyPatch("CalculateShootingRange")]
        //static bool PrefixCalculateShootingRange(ref float __result, ref string[] ___skeletonNames, float heightDifference)
        //{
        //    if (___skeletonNames != null && ___skeletonNames.Length > 0 && ___skeletonNames[0].Contains("ballista"))
        //    {
        //        __result = Mission.GetMissileRange(60f, heightDifference);
        //        return false;
        //    }
        //    else
        //    {
        //        return true;
        //    }
        //}

        [HarmonyPrefix]
        [HarmonyPatch("GetTargetReleaseAngle")]
        static bool PrefixGetTargetReleaseAngle(RangedSiegeWeapon __instance, ref float __result, Vec3 target, ref string[] ___SkeletonNames, ItemObject ___OriginalMissileItem)
        {
            if (___SkeletonNames != null && ___SkeletonNames.Length > 0 && ___SkeletonNames[0].Contains("ballista"))
            {

                PropertyInfo property = typeof(RangedSiegeWeapon).GetProperty("MissleStartingPositionForSimulation", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("MissleStartingPositionForSimulation");
                Vec3 MissleStartingPositionForSimulation = (Vec3)property.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                WeaponStatsData weaponStatsData = new MissionWeapon(___OriginalMissileItem, null, null).GetWeaponStatsDataForUsage(0);
                __result = Mission.GetMissileVerticalAimCorrection(target - MissleStartingPositionForSimulation, 60f, ref weaponStatsData, ItemObject.GetAirFrictionConstant(___OriginalMissileItem.PrimaryWeapon.WeaponClass, ___OriginalMissileItem.PrimaryWeapon.WeaponFlags));
                return false;
            }
            else
            {
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("ShootProjectileAux")]
        static bool PrefixShootProjectileAux(ref RangedSiegeWeapon __instance, ref string[] ___SkeletonNames,ref ItemObject missileItem,ref Agent ____lastShooterAgent, ref ItemObject ___LoadedMissileItem)
        {
            if (___SkeletonNames != null && ___SkeletonNames.Length > 0 && ___SkeletonNames[0].Contains("trebuchet"))
            {
                for (int i = 0; i < 7; i++)
                {
                    Mat3 mat = default(Mat3);

                PropertyInfo property = typeof(RangedSiegeWeapon).GetProperty("ShootingDirection", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("ShootingDirection");
                mat.f = (Vec3)property.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                mat.u = Vec3.Up;
                Mat3 mat2 = mat;
                mat2.Orthonormalize();
                float a = MBRandom.RandomFloat * ((float)Math.PI * 2f);
                mat2.RotateAboutForward(a);
                float f = 1.5f * MBRandom.RandomFloat;
                mat2.RotateAboutSide(f.ToRadians());

                Mat3 identity = Mat3.Identity;
                //identity.f = GetBallisticErrorAppliedDirection(1f);
                //identity.f = mat2.f;
                //identity.Orthonormalize();

                ItemObject @object = Game.Current.ObjectManager.GetObject<ItemObject>("grapeshot_projectile");

                PropertyInfo property3 = typeof(RangedSiegeWeapon).GetProperty("ShootingSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
                property3.DeclaringType.GetProperty("ShootingSpeed");
                float num = (float)property3.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                num *= MBRandom.RandomFloatRanged(0.95f, 1.05f);
                identity.f = mat2.f;
                identity.Orthonormalize();

                PropertyInfo property2 = typeof(RangedSiegeWeapon).GetProperty("Projectile", BindingFlags.NonPublic | BindingFlags.Instance);
                property2.DeclaringType.GetProperty("Projectile");
                Vec3 ProjectileEntityCurrentGlobalPosition = ((SynchedMissionObject)property2.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null)).GameEntity.GetGlobalFrame().origin;

                Mission.Current.AddCustomMissile(____lastShooterAgent, new MissionWeapon(@object, null, ____lastShooterAgent.Origin?.Banner, 1), ProjectileEntityCurrentGlobalPosition, identity.f, identity, ___LoadedMissileItem.PrimaryWeapon.MissileSpeed, num, addRigidBody: false, __instance);
                }
                return false;
            }
            if (___SkeletonNames != null && ___SkeletonNames.Length > 0 && ___SkeletonNames[0].Contains("ballista"))
            {
                Mat3 mat = default(Mat3);

                PropertyInfo property = typeof(RangedSiegeWeapon).GetProperty("ShootingDirection", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("ShootingDirection");
                mat.f = (Vec3)property.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                mat.u = Vec3.Up;
                Mat3 mat2 = mat;
                mat2.Orthonormalize();
                float a = MBRandom.RandomFloat * ((float)MathF.PI * 2f);
                mat2.RotateAboutForward(a);
                float f = 1f * MBRandom.RandomFloat;
                mat2.RotateAboutSide(f.ToRadians());

                Mat3 identity = Mat3.Identity;
                //identity.f = GetBallisticErrorAppliedDirection(1f);
                identity.f = mat2.f;
                identity.Orthonormalize();

                PropertyInfo property2 = typeof(RangedSiegeWeapon).GetProperty("Projectile", BindingFlags.NonPublic | BindingFlags.Instance);
                property2.DeclaringType.GetProperty("Projectile");
                Vec3 ProjectileEntityCurrentGlobalPosition = ((SynchedMissionObject)property2.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null)).GameEntity.GetGlobalFrame().origin;

                Mission.Current.AddCustomMissile(____lastShooterAgent, new MissionWeapon(missileItem, null, ____lastShooterAgent.Origin?.Banner, 1), ProjectileEntityCurrentGlobalPosition, identity.f, identity, 60f, 60f, addRigidBody: false, __instance);
                return false;
            }
            else
            {
                return true;
            }
        }

        [HarmonyPatch(typeof(AgentStatCalculateModel))]
        [HarmonyPatch("SetAiRelatedProperties")]
        class OverrideSetAiRelatedProperties
        {
            static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedItem, WeaponComponentData secondaryItem, AgentStatCalculateModel __instance)
            {
                if (agent.IsPlayerControlled)
                {
                    if (RBMConfig.RBMConfig.realisticRangedReload.Equals("1"))
                    {
                        SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                        if (skill != null)
                        {
                            int ef = __instance.GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
                            float effectiveSkill = Utilities.GetEffectiveSkillWithDR(ef);
                            if (equippedItem != null)
                            {
                                switch (equippedItem.ItemUsage)
                                {
                                    case "bow":
                                    case "long_bow":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.25f * (1.5f + (0.012f * effectiveSkill));
                                            break;
                                        }
                                    case "crossbow_fast":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.4f * (1f + (0.0045f * effectiveSkill));
                                            break;
                                        }
                                    case "crossbow":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.2f * (1f + (0.0045f * effectiveSkill));
                                            break;
                                        }
                                }
                            }
                        }

                    }
                    else if (RBMConfig.RBMConfig.realisticRangedReload.Equals("2"))
                    {
                        SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                        if (skill != null)
                        {
                            int ef = __instance.GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
                            float effectiveSkill = Utilities.GetEffectiveSkillWithDR(ef);
                            if (equippedItem != null)
                            {
                                switch (equippedItem.ItemUsage)
                                {
                                    case "bow":
                                    case "long_bow":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.38f * (1.5f + (0.0075f * effectiveSkill));
                                            break;
                                        }
                                    case "crossbow_fast":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.72f * (1 + (0.0035f * effectiveSkill));
                                            break;
                                        }
                                    case "crossbow":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.36f * (1 + (0.0035f * effectiveSkill));
                                            break;
                                        }
                                }
                            }
                        }
                    }
                }
                else
                {
                    SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                    if (skill != null)
                    {
                        int ef = __instance.GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
                        float effectiveSkill = Utilities.GetEffectiveSkillWithDR(ef);

                        if (equippedItem != null)
                        {
                            switch (equippedItem.ItemUsage)
                            {
                                case "bow":
                                case "long_bow":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.25f * (1.5f + (0.012f * effectiveSkill));
                                        break;
                                    }
                                case "crossbow_fast":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.4f * (1f + (0.0045f * effectiveSkill));
                                        break;
                                    }
                                case "crossbow":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.2f * (1f + (0.0045f * effectiveSkill));
                                        break;
                                    }
                            }
                        }
                    }
                }
                //0.12 for heavy crossbows, 0.19f for light crossbows, composite bows and longbows.
            }
        }
    }

    //[HarmonyPatch(typeof(MissionEquipment))]
    //class OverrideCheckLoadedAmmos
    //{
    //    [HarmonyPrefix]
    //    [HarmonyPatch("CheckLoadedAmmos")]
    //    static bool PrefixOnTick(ref MissionEquipment __instance,ref MissionWeapon[] ____weaponSlots)
    //    {
    //        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
    //        {
    //            if (!__instance[equipmentIndex].IsEmpty && __instance[equipmentIndex].Item.PrimaryWeapon.WeaponClass == WeaponClass.Crossbow)
    //            {
    //                __instance.GetAmmoCountAndIndexOfType(__instance[equipmentIndex].Item.Type, out var _, out var eIndex);
    //                if (eIndex != EquipmentIndex.None)
    //                {
    //                    MissionWeapon ammoWeapon = ____weaponSlots[(int)eIndex].Consume(Math.Min(__instance[equipmentIndex].MaxAmmo, ____weaponSlots[(int)eIndex].Amount));
    //                    ____weaponSlots[(int)equipmentIndex].ReloadAmmo(ammoWeapon, 2);
    //                }
    //            }
    //        }
    //        return false;
    //    }

    //}

    [HarmonyPatch(typeof(Mangonel))]
    class OverrideMangonel
    {
        [HarmonyPrefix]
        [HarmonyPatch("OnTick")]
        static bool PrefixOnTick(ref Mangonel __instance, ref float ___currentReleaseAngle)
        {
            float baseSpeed = 20f;
            float speedIncrease = 1.125f;
            __instance.ProjectileSpeed = baseSpeed + (((___currentReleaseAngle * MathF.RadToDeg)) * speedIncrease);

            return true;

        }
    }

    [HarmonyPatch(typeof(Mission))]
    class HandleMissileCollisionReactionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("HandleMissileCollisionReaction")]
        static bool Prefix(ref Mission __instance, ref Dictionary<int, Missile> ____missiles, int missileIndex, ref MissileCollisionReaction collisionReaction, MatrixFrame attachLocalFrame, Agent attackerAgent, Agent attachedAgent, bool attachedToShield, sbyte attachedBoneIndex, MissionObject attachedMissionObject, Vec3 bounceBackVelocity, Vec3 bounceBackAngularVelocity, int forcedSpawnIndex)
        {
            Missile missile = ____missiles[missileIndex];
            MissionObjectId missionObjectId = new MissionObjectId(-1, createdAtRuntime: true);
            switch (collisionReaction)
            {
                case MissileCollisionReaction.BecomeInvisible:
                    missile.Entity.Remove(81);
                    break;
                case MissileCollisionReaction.Stick:
                    missile.Entity.SetVisibilityExcludeParents(visible: true);
                    if (attachedAgent != null)
                    {
                        __instance.PrepareMissileWeaponForDrop(missileIndex);
                        if (attachedToShield)
                        {
                            EquipmentIndex wieldedItemIndex;

                            if (attachedAgent.WieldedOffhandWeapon.IsEmpty)
                            {
                                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                                {
                                    if (attachedAgent.Equipment != null && !attachedAgent.Equipment[equipmentIndex].IsEmpty)
                                    {
                                        if (attachedAgent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                                        {
                                            attachedAgent.AttachWeaponToWeapon(equipmentIndex, missile.Weapon, missile.Entity, ref attachLocalFrame);
                                            break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                wieldedItemIndex = attachedAgent.GetWieldedItemIndex(Agent.HandIndex.OffHand);
                                attachedAgent.AttachWeaponToWeapon(wieldedItemIndex, missile.Weapon, missile.Entity, ref attachLocalFrame);
                            }
                        }
                        else
                        {
                            attachedAgent.AttachWeaponToBone(missile.Weapon, missile.Entity, attachedBoneIndex, ref attachLocalFrame);
                        }
                    }
                    else
                    {
                        Vec3 velocity = Vec3.Zero;
                        missionObjectId = __instance.SpawnWeaponAsDropFromMissile(missileIndex, attachedMissionObject, in attachLocalFrame, WeaponSpawnFlags.AsMissile | WeaponSpawnFlags.WithStaticPhysics, in velocity, in velocity, forcedSpawnIndex);
                    }
                    break;
                case MissileCollisionReaction.BounceBack:
                    missile.Entity.SetVisibilityExcludeParents(visible: true);
                    missionObjectId = __instance.SpawnWeaponAsDropFromMissile(missileIndex, null, in attachLocalFrame, WeaponSpawnFlags.AsMissile | WeaponSpawnFlags.WithPhysics, in bounceBackVelocity, in bounceBackAngularVelocity, forcedSpawnIndex);
                    break;
            }
            bool flag = collisionReaction != MissileCollisionReaction.PassThrough;
            if (GameNetwork.IsServerOrRecorder)
            {
                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(new HandleMissileCollisionReaction(missileIndex, collisionReaction, attachLocalFrame, attackerAgent, attachedAgent, attachedToShield, attachedBoneIndex, attachedMissionObject, bounceBackVelocity, bounceBackAngularVelocity, missionObjectId.Id));
                GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.AddToMissionRecord);
            }
            else if (GameNetwork.IsClientOrReplay && flag)
            {
                __instance.RemoveMissileAsClient(missileIndex);
            }
            foreach (MissionBehavior missionBehavior in __instance.MissionBehaviors)
            {
                missionBehavior.OnMissileCollisionReaction(collisionReaction, attackerAgent, attachedAgent, attachedBoneIndex);
            }
            return false;
        }
    }

    [UsedImplicitly]
    [MBCallback]
    [HarmonyPatch(typeof(Mission))]
    class MeleeHitCallbackPatch
    {

        [HarmonyPrefix]
        [HarmonyPatch("MeleeHitCallback")]
        static bool Prefix(ref Mission __instance, ref AttackCollisionData collisionData, Agent attacker, Agent victim, GameEntity realHitEntity, ref float inOutMomentumRemaining, ref MeleeCollisionReaction colReaction, CrushThroughState crushThroughState, Vec3 blowDir, Vec3 swingDir, ref HitParticleResultData hitParticleResultData, bool crushedThroughWithoutAgentCollision)
        {
            if (collisionData.CollidedWithShieldOnBack)
            {
                //FieldInfo _attackBlockedWithShield = typeof(AttackCollisionData).GetField("_attackBlockedWithShield", BindingFlags.NonPublic | BindingFlags.Instance);
                //_attackBlockedWithShield.DeclaringType.GetField("_attackBlockedWithShield");
                //_attackBlockedWithShield.SetValue(collisionData, true);
                AttackCollisionData acd = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(true, collisionData.CorrectSideShieldBlock, collisionData.IsAlternativeAttack, collisionData.IsColliderAgent, collisionData.CollidedWithShieldOnBack,
                    collisionData.IsMissile, collisionData.MissileBlockedWithWeapon, collisionData.MissileHasPhysics, collisionData.EntityExists, collisionData.ThrustTipHit, collisionData.MissileGoneUnderWater, collisionData.MissileGoneOutOfBorder,
                    CombatCollisionResult.Blocked, collisionData.AffectorWeaponSlotOrMissileIndex, collisionData.StrikeType, collisionData.DamageType, collisionData.CollisionBoneIndex,
                    collisionData.VictimHitBodyPart, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex, collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon,
                    collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage, collisionData.MissileStartingBaseSpeed, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp,
                    collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity, collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);
                acd.BaseMagnitude = collisionData.BaseMagnitude;
                acd.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                acd.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                acd.InflictedDamage = collisionData.InflictedDamage;
                acd.AbsorbedByArmor = collisionData.AbsorbedByArmor;
                collisionData = acd;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("MeleeHitCallback")]
        static void Postfix(ref Mission __instance, ref AttackCollisionData collisionData, Agent attacker, Agent victim, GameEntity realHitEntity, ref float inOutMomentumRemaining, ref MeleeCollisionReaction colReaction, CrushThroughState crushThroughState, Vec3 blowDir, Vec3 swingDir, ref HitParticleResultData hitParticleResultData, bool crushedThroughWithoutAgentCollision)
        {
            //if (collisionData.AttackBlockedWithShield && collisionData.CollidedWithShieldOnBack)
            //{
            //    if (victim != null && collisionData.CollidedWithShieldOnBack && collisionData.IsMissile)
            //    {
            //        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            //        {
            //            if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
            //            {
            //                if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
            //                {
            //                    int num = MathF.Max(0, victim.Equipment[equipmentIndex].HitPoints - collisionData.InflictedDamage);
            //                    victim.ChangeWeaponHitPoints(equipmentIndex, (short)num);
            //                    if (num == 0)
            //                    {
            //                        victim.RemoveEquippedWeapon(equipmentIndex);
            //                    }
            //                    break;
            //                }
            //            }
            //        }
            //    }
            return;
            //}
        }
    }

    [UsedImplicitly]
    [MBCallback]
    [HarmonyPatch(typeof(Mission))]
    class MissileHitCallbackPatch
    {

        [HarmonyPrefix]
        [HarmonyPatch("MissileHitCallback")]
        static bool Prefix(ref Mission __instance, ref Dictionary<int, Missile> ____missiles, ref AttackCollisionData collisionData, Vec3 missileStartingPosition, Vec3 missilePosition, Vec3 missileAngularVelocity, Vec3 movementVelocity, MatrixFrame attachGlobalFrame, MatrixFrame affectedShieldGlobalFrame, int numDamagedAgents, Agent attacker, Agent victim, GameEntity hitEntity)
        {
            Missile missile;
            if (____missiles.TryGetValue(collisionData.AffectorWeaponSlotOrMissileIndex, out missile))
            {
                if (collisionData.CollidedWithShieldOnBack)
                {
                    if (missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.MultiplePenetration) || missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.CanPenetrateShield) || 
                        missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.AffectsArea) || missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.AffectsAreaBig))
                    {
                        return true;
                    }
                        //FieldInfo _attackBlockedWithShield = typeof(AttackCollisionData).GetField("_attackBlockedWithShield", BindingFlags.NonPublic | BindingFlags.Instance);
                        //_attackBlockedWithShield.DeclaringType.GetField("_attackBlockedWithShield");
                        //_attackBlockedWithShield.SetValue(collisionData, true);
                        AttackCollisionData acd = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(true, collisionData.CorrectSideShieldBlock, collisionData.IsAlternativeAttack, collisionData.IsColliderAgent, collisionData.CollidedWithShieldOnBack,
                        collisionData.IsMissile, collisionData.MissileBlockedWithWeapon, collisionData.MissileHasPhysics, collisionData.EntityExists, collisionData.ThrustTipHit, collisionData.MissileGoneUnderWater, collisionData.MissileGoneOutOfBorder,
                        CombatCollisionResult.Blocked, collisionData.AffectorWeaponSlotOrMissileIndex, collisionData.StrikeType, collisionData.DamageType, collisionData.CollisionBoneIndex,
                        collisionData.VictimHitBodyPart, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex, collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon,
                        collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage, collisionData.MissileStartingBaseSpeed, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp,
                        collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity, collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);
                    acd.BaseMagnitude = collisionData.BaseMagnitude;
                    acd.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                    acd.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                    acd.InflictedDamage = collisionData.InflictedDamage;
                    acd.AbsorbedByArmor = collisionData.AbsorbedByArmor;
                    collisionData = acd;
                }
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("MissileHitCallback")]
        static void Postfix(ref Mission __instance, ref Dictionary<int, Missile> ____missiles, ref AttackCollisionData collisionData, Vec3 missileStartingPosition, Vec3 missilePosition, Vec3 missileAngularVelocity, Vec3 movementVelocity, MatrixFrame attachGlobalFrame, MatrixFrame affectedShieldGlobalFrame, int numDamagedAgents, Agent attacker, Agent victim, GameEntity hitEntity)
        {
            Missile missile;
            if (____missiles.TryGetValue(collisionData.AffectorWeaponSlotOrMissileIndex, out missile))
            {
                if (missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.MultiplePenetration) || missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.CanPenetrateShield) ||
                        missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.AffectsArea) || missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.AffectsAreaBig))
                {
                    if (collisionData.CollidedWithShieldOnBack)
                    {
                        if (victim != null && collisionData.IsMissile)
                        {
                            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                            {
                                if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                                {
                                    if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                                    {
                                        int num = MathF.Max(0, victim.Equipment[equipmentIndex].HitPoints - collisionData.InflictedDamage);
                                        victim.ChangeWeaponHitPoints(equipmentIndex, (short)num);
                                        if (num == 0)
                                        {
                                            victim.RemoveEquippedWeapon(equipmentIndex);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    return;
                }
            }
            if (collisionData.AttackBlockedWithShield && collisionData.CollidedWithShieldOnBack)
            {
                if (victim != null && collisionData.CollidedWithShieldOnBack && collisionData.IsMissile)
                {
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                        {
                            if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                            {
                                int num = MathF.Max(0, victim.Equipment[equipmentIndex].HitPoints - collisionData.InflictedDamage);
                                victim.ChangeWeaponHitPoints(equipmentIndex, (short)num);
                                if (num == 0)
                                {
                                    victim.RemoveEquippedWeapon(equipmentIndex);
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
