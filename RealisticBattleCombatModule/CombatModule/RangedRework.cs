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
using TaleWorlds.Engine;

namespace RealisticBattleCombatModule
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
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionArrow, 0.0020f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionJavelin, 0.0025f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionAxe, 0.01f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionKnife, 0.01f);
                ManagedParameters.SetParameter(ManagedParametersEnum.MissileMinimumDamageToStick, 20);
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("WeaponEquipped")]
        class OverrideWeaponEquipped
        {
            static bool Prefix(ref Agent __instance, EquipmentIndex equipmentSlot, in WeaponData weaponData,ref WeaponStatsData[] weaponStatsData, in WeaponData ammoWeaponData,ref WeaponStatsData[] ammoWeaponStatsData, GameEntity weaponEntity, bool removeOldWeaponFromScene, bool isWieldedOnSpawn)
            {
                if(weaponStatsData != null)
                {
                    for (int i = 0; i < weaponStatsData.Length; i++)
                    {
                        SkillObject skill = (weaponData.GetItemObject() == null) ? DefaultSkills.Athletics : weaponData.GetItemObject().RelevantSkill;
                        if(skill != null) { 
                            int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance.Character, __instance.Origin, __instance.Formation, skill);

                            if ((weaponStatsData[i].WeaponClass == (int)WeaponClass.LowGripPolearm)
                                 || (weaponStatsData[i].WeaponClass == (int)WeaponClass.Mace) || (weaponStatsData[i].WeaponClass == (int)WeaponClass.OneHandedAxe) || (weaponStatsData[i].WeaponClass == (int)WeaponClass.OneHandedPolearm)
                                  || (weaponStatsData[i].WeaponClass == (int)WeaponClass.TwoHandedAxe) || (weaponStatsData[i].WeaponClass == (int)WeaponClass.TwoHandedMace) || (weaponStatsData[i].WeaponClass == (int)WeaponClass.TwoHandedPolearm))
                            {

                                float swingskillModifier = 1f + (effectiveSkill / 1000f);
                                float thrustskillModifier = 1f + (effectiveSkill / 700f);
                                float handlingskillModifier = 1f + (effectiveSkill / 700f);

                                weaponStatsData[i].SwingSpeed = MathF.Ceiling((weaponStatsData[i].SwingSpeed * 0.83f) * swingskillModifier);
                                weaponStatsData[i].ThrustSpeed = MathF.Ceiling((weaponStatsData[i].ThrustSpeed * 0.83f) * thrustskillModifier);
                                weaponStatsData[i].DefendSpeed = MathF.Ceiling((weaponStatsData[i].DefendSpeed * 0.83f) * handlingskillModifier);
                            }
                            else if ((weaponStatsData[i].WeaponClass == (int)WeaponClass.OneHandedSword) || (weaponStatsData[i].WeaponClass == (int)WeaponClass.Dagger) || (weaponStatsData[i].WeaponClass == (int)WeaponClass.TwoHandedSword))
                            {

                                float swingskillModifier = 1f + (effectiveSkill / 600f);
                                float thrustskillModifier = 1f + (effectiveSkill / 600f);
                                float handlingskillModifier = 1f + (effectiveSkill / 600f);

                                weaponStatsData[i].SwingSpeed = MathF.Ceiling((weaponStatsData[i].SwingSpeed * 0.83f) * swingskillModifier);
                                weaponStatsData[i].ThrustSpeed = MathF.Ceiling((weaponStatsData[i].ThrustSpeed * 0.83f) * thrustskillModifier);
                                weaponStatsData[i].DefendSpeed = MathF.Ceiling((weaponStatsData[i].DefendSpeed * 0.83f) * handlingskillModifier);
                            }
                            else if (weaponStatsData[i].WeaponClass == (int)WeaponClass.Bow)
                            {
                                if(XmlConfig.dict["Global.RealisticRangedReload"] == 1 || XmlConfig.dict["Global.RealisticRangedReload"] == 2)
                                {
                                    float DrawSpeedskillModifier = 1 + (effectiveSkill * 0.01f);
                                    weaponStatsData[i].ThrustSpeed = MathF.Ceiling((weaponStatsData[i].ThrustSpeed * 0.1f) * DrawSpeedskillModifier);
                                }
                            }
                            if ((weaponStatsData[i].WeaponClass == (int)WeaponClass.OneHandedPolearm) || (weaponStatsData[i].WeaponClass == (int)WeaponClass.LowGripPolearm))
                            {
                                weaponStatsData[i].MissileSpeed =  Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentSlot], 10, effectiveSkill);
                            }
                            if (weaponStatsData[i].WeaponClass == (int)WeaponClass.Javelin)
                            {
                                weaponStatsData[i].MissileSpeed = Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentSlot], 10, effectiveSkill);
                            }
                            if ((weaponStatsData[i].WeaponClass == (int)WeaponClass.ThrowingAxe) || (weaponStatsData[i].WeaponClass == (int)WeaponClass.ThrowingKnife) || (weaponStatsData[i].WeaponClass == (int)WeaponClass.Dagger))
                            {
                                weaponStatsData[i].MissileSpeed = Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentSlot], 0, effectiveSkill);
                            }
                            if (weaponStatsData[i].WeaponClass == (int)WeaponClass.Stone)
                            {
                                weaponStatsData[i].MissileSpeed = Utilities.assignStoneMissileSpeed(__instance.Equipment[equipmentSlot]);
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
                        MissionWeapon mw = __instance.Equipment[equipmentIndex];
                        WeaponStatsData[] wsd = mw.GetWeaponStatsData();

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
                        calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon, rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight());
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

                //for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                //{

                //    if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                //    {
                //        MissionWeapon mw = __instance.Equipment[equipmentIndex];
                //        WeaponStatsData[] wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData();
                //        WeaponComponentData weapon = __instance.Equipment[equipmentIndex].CurrentUsageItem;

                //        //if ((wsd[0].WeaponClass == (int)WeaponClass.OneHandedSword) || (wsd[0].WeaponClass == (int)WeaponClass.Dagger) || (wsd[0].WeaponClass == (int)WeaponClass.LowGripPolearm)
                //        //     || (wsd[0].WeaponClass == (int)WeaponClass.Mace) || (wsd[0].WeaponClass == (int)WeaponClass.OneHandedAxe) || (wsd[0].WeaponClass == (int)WeaponClass.OneHandedPolearm)
                //        //      || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedAxe) || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedMace) || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedPolearm)
                //        //       || (wsd[0].WeaponClass == (int)WeaponClass.TwoHandedSword))
                //        //{
                //        //    swingSpeedProperty.SetValue(weapon, originalItemSwingSpeed[mw.GetModifiedItemName()], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                //        //    thrustSpeedProperty.SetValue(weapon, originalItemThrustSpeed[mw.GetModifiedItemName()], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                //        //    handlingProperty.SetValue(weapon, originalItemHandling[mw.GetModifiedItemName()], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                //        //}

                //        if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                //        {
                //            propertyMissileSpeed.SetValue(weapon, rangedWeaponStats[mw.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                //        }
                //    }
                //}
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

                if (Mission.Current.IsFieldBattle && !shooterAgent.IsMainAgent && (wsd[0].WeaponClass == (int)WeaponClass.Javelin || wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe))
                {
                    //float shooterSpeed = shooterAgent.MovementVelocity.Normalize();
                    if (!shooterAgent.HasMount)
                    {
                        velocity.z = velocity.z - 1.4f;
                    }
                    else
                    {
                        velocity.z = velocity.z - 2f;

                    }
                }

                if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                {
                    float ammoWeight = missionWeapon.AmmoWeapon.GetWeight();

                    //PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                    //property2.DeclaringType.GetProperty("MissileSpeed");
                    //property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                    RangedWeaponStats rws;
                    if (!rangedWeaponStats.TryGetValue(missionWeapon.GetModifiedItemName().ToString(), out rws))
                    {
                        rangedWeaponMW[missionWeapon.GetModifiedItemName().ToString()] = missionWeapon;
                        rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()] = new RangedWeaponStats(missionWeapon.GetModifiedMissileSpeedForCurrentUsage());
                    }

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
                //    property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                //}
            }
        }

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
        //            if(__instance.Equipment[slotIndex].CurrentUsageItem.WeaponClass == WeaponClass.Bow || __instance.Equipment[slotIndex].CurrentUsageItem.WeaponClass == WeaponClass.Crossbow)
        //            {
        //                WeaponData weaponData = WeaponData.InvalidWeaponData;
        //                WeaponStatsData[] weaponStatsData = null;
        //                WeaponData ammoWeaponData = WeaponData.InvalidWeaponData;
        //                WeaponStatsData[] ammoWeaponStatsData = null;

        //                if (!__instance.Equipment[slotIndex].IsEmpty && !__instance.Equipment[ammoSlotIndex].IsEmpty)
        //                {
        //                    float ammoWeight = __instance.Equipment[ammoSlotIndex].GetWeight() / __instance.Equipment[ammoSlotIndex].Amount;
        //                    RangedWeaponStats rws;
        //                    if (!rangedWeaponStats.TryGetValue(__instance.Equipment[slotIndex].GetModifiedItemName().ToString(), out rws))
        //                    {
        //                        rangedWeaponMW[__instance.Equipment[slotIndex].GetModifiedItemName().ToString()] = __instance.Equipment[slotIndex];
        //                        rangedWeaponStats[__instance.Equipment[slotIndex].GetModifiedItemName().ToString()] = new RangedWeaponStats(__instance.Equipment[slotIndex].GetModifiedMissileSpeedForCurrentUsage());
        //                    }
        //                    int calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, __instance.Equipment[slotIndex], rangedWeaponStats[__instance.Equipment[slotIndex].GetModifiedItemName().ToString()].getDrawWeight());

        //                    if (calculatedMissileSpeed != __instance.Equipment[slotIndex].CurrentUsageItem.MissileSpeed)
        //                    {
        //                        PropertyInfo propertyMissileSpeed = typeof(WeaponComponentData).GetProperty("MissileSpeed");
        //                        propertyMissileSpeed.DeclaringType.GetProperty("MissileSpeed");
        //                        propertyMissileSpeed.SetValue(__instance.Equipment[slotIndex].CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

        //                        weaponData = __instance.Equipment[slotIndex].GetWeaponData(needBatchedVersionForMeshes: true);
        //                        weaponStatsData = __instance.Equipment[slotIndex].GetWeaponStatsData();

        //                        MethodInfo method = typeof(Agent).GetMethod("WeaponEquipped", BindingFlags.NonPublic | BindingFlags.Instance);
        //                        method.DeclaringType.GetMethod("WeaponEquipped");
        //                        method.Invoke(__instance, new object[] { slotIndex, weaponData, weaponStatsData, ammoWeaponData, ammoWeaponStatsData, null, true, true });

        //                       __instance.Equipment[slotIndex].ReloadAmmo(__instance.Equipment[ammoSlotIndex], 2);

        //                        __instance.TryToWieldWeaponInSlot(slotIndex, Agent.WeaponWieldActionType.WithAnimation, false);

        //                        //__instance.Equipment.SetReloadPhaseOfSlot(slotIndex, 2);
        //                    }
        //                }
        //                weaponData.DeinitializeManagedPointers();
        //                ammoWeaponData.DeinitializeManagedPointers();
        //            }
        //        }
        //        return true;
        //    }
        //}

    }

    [HarmonyPatch(typeof(RangedSiegeWeapon))]
    class OverrideRangedSiegeWeapon
    {
        [HarmonyPrefix]
        [HarmonyPatch("CalculateShootingRange")]
        static bool PrefixCalculateShootingRange(ref float __result, ref string[] ___skeletonNames, float heightDifference)
        {
            if (___skeletonNames != null && ___skeletonNames.Length > 0 && ___skeletonNames[0].Contains("ballista"))
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
        static bool PrefixGetTargetReleaseAngle(RangedSiegeWeapon __instance, ref float __result, Vec3 target, ref string[] ___skeletonNames, ItemObject ___OriginalMissileItem)
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

        [HarmonyPatch(typeof(AgentStatCalculateModel))]
        [HarmonyPatch("SetAiRelatedProperties")]
        class OverrideSetAiRelatedProperties
        {
            static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedItem, WeaponComponentData secondaryItem, AgentStatCalculateModel __instance)
            {
                if (XmlConfig.dict["Global.RealisticRangedReload"] == 1)
                {
                    SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                    if(skill != null)
                    {
                        int effectiveSkill = __instance.GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);

                        if (equippedItem != null)
                        {
                            switch (equippedItem.ItemUsage)
                            {
                                case "bow":
                                case "long_bow":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.19f * (1 + (0.01f * effectiveSkill));
                                        break;
                                    }
                                case "crossbow_fast":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.36f * (1 + (0.0025f * effectiveSkill));
                                        break;
                                    }
                                case "crossbow":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.18f * (1 + (0.0025f * effectiveSkill));
                                        break;
                                    }
                            }
                        }
                    }
                    
                }
                else if (XmlConfig.dict["Global.RealisticRangedReload"] == 2)
                {
                    SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                    if (skill != null)
                    {
                        int effectiveSkill = __instance.GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
                        if (equippedItem != null)
                        {
                            switch (equippedItem.ItemUsage)
                            {
                                case "bow":
                                case "long_bow":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.38f * (1 + (0.01f * effectiveSkill));
                                        break;
                                    }
                                case "crossbow_fast":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.72f * (1 + (0.0025f * effectiveSkill));
                                        break;
                                    }
                                case "crossbow":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.36f * (1 + (0.0025f * effectiveSkill));
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
}
