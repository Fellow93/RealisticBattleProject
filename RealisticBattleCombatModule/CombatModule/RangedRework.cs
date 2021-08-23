using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using HarmonyLib;
using System.Reflection;
using JetBrains.Annotations;
using TaleWorlds.Library;
using System.Collections;
using System.Collections.Generic;
using TaleWorlds.Localization;
using RealisticBattleCombatModule.CombatModule;
using System;

namespace RealisticBattleCombatModule
{
    public class RangedRework
    {

        public static Dictionary<TextObject, int> originalItemSwingSpeed = new Dictionary<TextObject, int> { };
        public static Dictionary<TextObject, int> originalItemThrustSpeed = new Dictionary<TextObject, int> { };
        public static Dictionary<TextObject, int> originalItemHandling = new Dictionary<TextObject, int> { };
        public static Dictionary<string, RangedWeaponStats> rangedWeaponStats = new Dictionary<string, RangedWeaponStats>(new RangedWeaponStatsComparer());

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

            //private static ArrayList _oldMissileSpeeds = new ArrayList();

            static bool Prefix(ref Agent __instance)
            {
                ArrayList stringRangedWeapons = new ArrayList();
                MissionWeapon arrow = MissionWeapon.Invalid;
                bool firstProjectile = true;

                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {
                    if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                    {
                        MissionWeapon mw = __instance.Equipment[equipmentIndex];
                        WeaponStatsData[] wsd = mw.GetWeaponStatsData();
                        if ((wsd[0].WeaponClass == (int)WeaponClass.SmallShield) || (wsd[0].WeaponClass == (int)WeaponClass.LargeShield))
                        {
                            //__instance.AttachWeaponToWeapon(equipmentIndex, __instance.Equipment[equipmentIndex], __instance.GetWeaponEntityFromEquipmentSlot(equipmentIndex), ref wsd[0].WeaponFrame);
                            //__instance.AttachWeaponToBone(__instance.Equipment[equipmentIndex], __instance.AgentVisuals.GetEntity(), 5, ref wsd[0].WeaponFrame);
                        }
                        if ((wsd[0].WeaponClass == (int)WeaponClass.OneHandedSword))
                        {
                            float skillModifier = 1f + __instance.Character.GetSkillValue(DefaultSkills.OneHanded) / 1000f;

                            WeaponComponentData weapon = mw.CurrentUsageItem;

                            PropertyInfo swingSpeedProperty = typeof(WeaponComponentData).GetProperty("SwingSpeed");
                            swingSpeedProperty.DeclaringType.GetProperty("SwingSpeed");
                            int swingSpeed = (int)swingSpeedProperty.GetValue(weapon, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                            originalItemSwingSpeed[mw.GetModifiedItemName()] = swingSpeed;
                            swingSpeedProperty.SetValue(weapon, MathF.Ceiling(swingSpeed * skillModifier), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                            PropertyInfo thrustSpeedProperty = typeof(WeaponComponentData).GetProperty("ThrustSpeed");
                            thrustSpeedProperty.DeclaringType.GetProperty("ThrustSpeed");
                            int thrustSpeed = (int)thrustSpeedProperty.GetValue(weapon, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                            originalItemThrustSpeed[mw.GetModifiedItemName()] = thrustSpeed;
                            thrustSpeedProperty.SetValue(weapon, MathF.Ceiling(thrustSpeed * skillModifier), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                            PropertyInfo handlingProperty = typeof(WeaponComponentData).GetProperty("Handling");
                            handlingProperty.DeclaringType.GetProperty("Handling");
                            int handling = (int)handlingProperty.GetValue(weapon, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                            originalItemHandling[mw.GetModifiedItemName()] = handling;
                            handlingProperty.SetValue(weapon, MathF.Ceiling(handling * skillModifier), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        }
                        if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                        {
                            RangedWeaponStats rangedWeaponStatNew = new RangedWeaponStats(mw.GetModifiedMissileSpeedForCurrentUsage());
                            RangedWeaponStats rangedWeaponStatOld;
                            if (!rangedWeaponStats.TryGetValue(mw.GetModifiedItemName().ToString(), out rangedWeaponStatOld))
                            {
                                rangedWeaponStats[mw.GetModifiedItemName().ToString()] = rangedWeaponStatNew;
                            }
                            stringRangedWeapons.Add(mw);
                        }
                        if ((wsd[0].WeaponClass == (int)WeaponClass.Arrow) || (wsd[0].WeaponClass == (int)WeaponClass.Bolt))
                        {
                            if (firstProjectile)
                            {
                                arrow = mw;
                                firstProjectile = false;
                            }
                        }
                        if ((wsd[0].WeaponClass == (int)WeaponClass.OneHandedPolearm) || (wsd[0].WeaponClass == (int)WeaponClass.LowGripPolearm))
                        {
                            for (int i = 0; i < wsd.Length; i++)
                            {
                                if (wsd[i].MissileSpeed != 0)
                                {
                                    Utilities.assignThrowableMissileSpeed(mw, i, 10);
                                }
                            }
                        }
                        if (wsd[0].WeaponClass == (int)WeaponClass.Javelin)
                        {
                            for (int i = 0; i < wsd.Length; i++)
                            {
                                if (wsd[i].MissileSpeed != 0)
                                {
                                    Utilities.assignThrowableMissileSpeed(mw, i, 10);
                                }
                            }
                        }
                        if ((wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe) || (wsd[0].WeaponClass == (int)WeaponClass.ThrowingKnife))
                        {
                            for (int i = 0; i < wsd.Length; i++)
                            {
                                if (wsd[i].MissileSpeed != 0)
                                {
                                    Utilities.assignThrowableMissileSpeed(mw, i, 5);
                                }
                            }
                        }
                        if (wsd[0].WeaponClass == (int)WeaponClass.Stone)
                        {
                            for (int i = 0; i < wsd.Length; i++)
                            {
                                if (wsd[i].MissileSpeed != 0)
                                {
                                    Utilities.assignStoneMissileSpeed(mw, i);
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
                        float ammoWeight = arrow.GetWeight() / arrow.Amount;
                        calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon, rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight());

                        PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                        property.DeclaringType.GetProperty("MissileSpeed");
                        property.SetValue(missionWeapon.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                    else if (!missionWeapon.Equals(MissionWeapon.Invalid))
                    {
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
                int j = 0;
                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {

                    if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                    {
                        MissionWeapon mw = __instance.Equipment[equipmentIndex];
                        WeaponStatsData[] wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData();

                        if ((wsd[0].WeaponClass == (int)WeaponClass.OneHandedSword))
                        {
                            WeaponComponentData weapon = __instance.Equipment[equipmentIndex].CurrentUsageItem;

                            PropertyInfo swingSpeedProperty = typeof(WeaponComponentData).GetProperty("SwingSpeed");
                            swingSpeedProperty.DeclaringType.GetProperty("SwingSpeed");
                            swingSpeedProperty.SetValue(weapon, originalItemSwingSpeed[mw.GetModifiedItemName()], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                            
                            PropertyInfo thrustSpeedProperty = typeof(WeaponComponentData).GetProperty("ThrustSpeed");
                            thrustSpeedProperty.DeclaringType.GetProperty("ThrustSpeed");
                            thrustSpeedProperty.SetValue(weapon, originalItemThrustSpeed[mw.GetModifiedItemName()], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                            
                            PropertyInfo handlingProperty = typeof(WeaponComponentData).GetProperty("Handling");
                            handlingProperty.DeclaringType.GetProperty("Handling");
                            handlingProperty.SetValue(weapon, originalItemHandling[mw.GetModifiedItemName()], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                        }

                        //if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                        //{
                        //    MissionWeapon missionWeapon = __instance.Equipment[equipmentIndex];

                        //    PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                        //    property.DeclaringType.GetProperty("MissileSpeed");
                        //    if (missionWeapon.ItemModifier != null)
                        //    {
                        //        FieldInfo field = typeof(ItemModifier).GetField("_missileSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
                        //        field.DeclaringType.GetField("_missileSpeed");
                        //        int missileSpeedModifier = (int)field.GetValue(missionWeapon.ItemModifier);

                        //        property.SetValue(missionWeapon.CurrentUsageItem, rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight() - missileSpeedModifier, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                        //    }
                        //    else
                        //    {
                        //        property.SetValue(missionWeapon.CurrentUsageItem, rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        //    }
                        //    i++;
                        //}
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

                if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                {
                    float ammoWeight = missionWeapon.AmmoWeapon.GetWeight();

                    //PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                    //property2.DeclaringType.GetProperty("MissileSpeed");
                    //property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                    int calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon, rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight());

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
                //MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                //WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();
                //if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                //{
                //    PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                //    property2.DeclaringType.GetProperty("MissileSpeed");
                //    property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, _oldMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                //}
            }
        }
    }

    [HarmonyPatch(typeof(RangedSiegeWeapon))]
    class OverrideRangedSiegeWeapon
    {
        [HarmonyPrefix]
        [HarmonyPatch("CalculateShootingRange")]
        static bool PrefixCalculateShootingRange(ref float __result, ref string[] ___skeletonNames, float heightDifference)
        {
            if(___skeletonNames != null && ___skeletonNames.Length > 0 && ___skeletonNames[0].Contains("ballista"))
            {
                __result = Mission.GetMissileRange(60f, heightDifference);
                return false;
            }
            else
            {
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetTargetReleaseAngle")]
        static bool PrefixGetTargetReleaseAngle(RangedSiegeWeapon __instance, ref float __result, Vec3 target,  ref string[] ___skeletonNames, ItemObject ___OriginalMissileItem)
        {
            if (___skeletonNames != null && ___skeletonNames.Length > 0 && ___skeletonNames[0].Contains("ballista"))
            {

                PropertyInfo property = typeof(RangedSiegeWeapon).GetProperty("MissleStartingPositionForSimulation", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("MissleStartingPositionForSimulation");
                Vec3 MissleStartingPositionForSimulation = (Vec3)property.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                WeaponStatsData weaponStatsData = new MissionWeapon(___OriginalMissileItem, null, null).GetWeaponStatsDataForUsage(0);
                __result = Mission.GetMissileVerticalAimCorrection(target - MissleStartingPositionForSimulation, 60f, ref weaponStatsData, ItemObject.GetAirFrictionConstant(___OriginalMissileItem.PrimaryWeapon.WeaponClass));
                return false;
            }
            else
            {
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("ShootProjectileAux")]
        static bool PrefixShootProjectileAux(RangedSiegeWeapon __instance, ref string[] ___skeletonNames, ItemObject missileItem, Agent ____lastShooterAgent)
        {


            if (___skeletonNames != null && ___skeletonNames.Length > 0 && ___skeletonNames[0].Contains("ballista"))
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
    }

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
}
