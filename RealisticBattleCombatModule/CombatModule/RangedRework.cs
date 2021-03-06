﻿using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using HarmonyLib;
using System.Reflection;
using JetBrains.Annotations;
using TaleWorlds.Library;
using System.Collections;

namespace RealisticBattleCombatModule
{
    public class RangedRework
    {
        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("FinishMissionLoading")]
        public class MissionLoadChangeParameters
        {
            static void Postfix()
            {
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionArrow, 0.0020f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionJavelin, 0.0025f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionAxe, 0.01f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionKnife, 0.01f);
                ManagedParameters.SetParameter(ManagedParametersEnum.MissileMinimumDamageToStick, 20);
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("EquipItemsFromSpawnEquipment")]
        class OverrideEquipItemsFromSpawnEquipment
        {

            private static ArrayList _oldMissileSpeeds = new ArrayList();
            static bool Prefix(Agent __instance)
            {

                ArrayList stringRangedWeapons = new ArrayList();
                MissionWeapon arrow = MissionWeapon.Invalid;
                bool firstProjectile = true;

                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {
                    if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                    {
                        WeaponStatsData[] wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData();
                        if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                        {
                            stringRangedWeapons.Add(__instance.Equipment[equipmentIndex]);
                        }
                        if ((wsd[0].WeaponClass == (int)WeaponClass.Arrow) || (wsd[0].WeaponClass == (int)WeaponClass.Bolt))
                        {
                            if (firstProjectile)
                            {
                                arrow = __instance.Equipment[equipmentIndex];
                                firstProjectile = false;
                            }
                        }
                        if ((wsd[0].WeaponClass == (int)WeaponClass.OneHandedPolearm) || (wsd[0].WeaponClass == (int)WeaponClass.LowGripPolearm))
                        {
                            for (int i = 0; i < wsd.Length; i++)
                            {
                                if (wsd[i].MissileSpeed != 0)
                                {
                                    Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentIndex], i, 5);
                                }
                            }
                        }
                        if (wsd[0].WeaponClass == (int)WeaponClass.Javelin)
                        {
                            for (int i = 0; i < wsd.Length; i++)
                            {
                                if (wsd[i].MissileSpeed != 0)
                                {
                                    Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentIndex], i, 10);
                                }
                            }
                        }
                        if ((wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe) || (wsd[0].WeaponClass == (int)WeaponClass.ThrowingKnife))
                        {
                            for (int i = 0; i < wsd.Length; i++)
                            {
                                if (wsd[i].MissileSpeed != 0)
                                {
                                    Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentIndex], i, 0);
                                }
                            }
                        }
                        if (wsd[0].WeaponClass == (int)WeaponClass.Stone)
                        {
                            for (int i = 0; i < wsd.Length; i++)
                            {
                                if (wsd[i].MissileSpeed != 0)
                                {
                                    Utilities.assignStoneMissileSpeed(__instance.Equipment[equipmentIndex], i);
                                }
                            }
                        }
                    }
                }
                foreach (MissionWeapon missionWeapon in stringRangedWeapons)
                {
                    int calculatedMissileSpeed = 50;
                    if (!missionWeapon.Equals(MissionWeapon.Invalid) && !arrow.Equals(MissionWeapon.Invalid))
                    {
                        if (missionWeapon.ItemModifier != null)
                        {
                            FieldInfo field = typeof(ItemModifier).GetField("_missileSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
                            field.DeclaringType.GetField("_missileSpeed");
                            int missileSpeedModifier = (int)field.GetValue(missionWeapon.ItemModifier);

                            _oldMissileSpeeds.Add(missionWeapon.GetModifiedMissileSpeedForUsage(0) - missileSpeedModifier);

                        }
                        else
                        {
                            _oldMissileSpeeds.Add(missionWeapon.GetModifiedMissileSpeedForUsage(0));

                        }
                        float ammoWeight = arrow.GetWeight() / arrow.Amount;
                        calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon, missionWeapon.GetModifiedMissileSpeedForUsage(0));

                        PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                        property.DeclaringType.GetProperty("MissileSpeed");
                        property.SetValue(missionWeapon.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                    else if (!missionWeapon.Equals(MissionWeapon.Invalid))
                    {
                        if (missionWeapon.ItemModifier != null)
                        {
                            FieldInfo field = typeof(ItemModifier).GetField("_missileSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
                            field.DeclaringType.GetField("_missileSpeed");
                            int missileSpeedModifier = (int)field.GetValue(missionWeapon.ItemModifier);

                            _oldMissileSpeeds.Add(missionWeapon.GetModifiedMissileSpeedForUsage(0) - missileSpeedModifier);

                        }
                        else
                        {
                            _oldMissileSpeeds.Add(missionWeapon.GetModifiedMissileSpeedForUsage(0));

                        }
                        PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                        property.DeclaringType.GetProperty("MissileSpeed");
                        property.SetValue(missionWeapon.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                }

                return true;
            }
            static void Postfix(Agent __instance)
            {
                int i = 0;
                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {

                    if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                    {
                        WeaponStatsData[] wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData();
                        if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                        {
                            MissionWeapon missionWeapon = __instance.Equipment[equipmentIndex];

                            PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                            property.DeclaringType.GetProperty("MissileSpeed");
                            property.SetValue(missionWeapon.CurrentUsageItem, _oldMissileSpeeds.ToArray()[i], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                            i++;
                        }
                    }
                }
                _oldMissileSpeeds.Clear();
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("OnAgentShootMissile")]
        [UsedImplicitly]
        [MBCallback]
        class OverrideOnAgentShootMissile
        {

            private static int _oldMissileSpeed;
            static bool Prefix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, ref Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
            {
                MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

                if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                {
                    if (missionWeapon.ItemModifier != null)
                    {
                        FieldInfo field = typeof(ItemModifier).GetField("_missileSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
                        field.DeclaringType.GetField("_missileSpeed");
                        int missileSpeedModifier = (int)field.GetValue(missionWeapon.ItemModifier);

                        _oldMissileSpeed = missionWeapon.GetModifiedMissileSpeedForUsage(0) - missileSpeedModifier;
                    }
                    else
                    {
                        _oldMissileSpeed = missionWeapon.GetModifiedMissileSpeedForUsage(0);
                    }
                    float ammoWeight = missionWeapon.AmmoWeapon.GetWeight();

                    int calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon, missionWeapon.GetModifiedMissileSpeedForUsage(0));

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
                return true;
            }

            static void Postfix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
            {
                MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();
                if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                {
                    PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                    property2.DeclaringType.GetProperty("MissileSpeed");
                    property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, _oldMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }
            }
        }
    }
}
