using HarmonyLib;
using JetBrains.Annotations;
using NetworkMessages.FromServer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;
using static TaleWorlds.MountAndBlade.Mission;

namespace RBMCombat
{
    //[Serializable]
    //public class WeaponComponentDataAdditionalData
    //{
    //    public int drawWeight;

    //    public WeaponComponentDataAdditionalData()
    //    {
    //        drawWeight = 0;
    //    }
    //}

    //public static class WeaponComponentDataExtension
    //{
    //    private static readonly ConditionalWeakTable<WeaponComponentData, WeaponComponentDataAdditionalData> data =
    //        new ConditionalWeakTable<WeaponComponentData, WeaponComponentDataAdditionalData>();

    //    public static WeaponComponentDataAdditionalData GetAdditionalData(this WeaponComponentData weapon)
    //    {
    //        return data.GetOrCreateValue(weapon);
    //    }

    //    public static void AddData(this WeaponComponentData weapon, WeaponComponentDataAdditionalData value)
    //    {
    //        try
    //        {
    //            data.Add(weapon, value);
    //        }
    //        catch (Exception) { }
    //    }
    //}

    public class RangedRework
    {
        public static Dictionary<TextObject, int> originalItemSwingSpeed = new Dictionary<TextObject, int> { };
        public static Dictionary<TextObject, int> originalItemThrustSpeed = new Dictionary<TextObject, int> { };
        public static Dictionary<TextObject, int> originalItemHandling = new Dictionary<TextObject, int> { };
        public static Dictionary<string, RangedWeaponStats> rangedWeaponStats = new Dictionary<string, RangedWeaponStats>(new RangedWeaponStatsComparer());
        public static Dictionary<string, MissionWeapon> rangedWeaponMW = new Dictionary<string, MissionWeapon> { };

        //[HarmonyPatch(typeof(WeaponComponentData))]
        //[HarmonyPatch("Deserialize")]
        //internal class WeaponComponentDataDeserializePatch
        //{
        //    private static void Postfix(WeaponComponentData __instance, ItemObject item, XmlNode node)
        //    {
        //        if (node.Attributes["draw_weight"] != null)
        //        {
        //            WeaponComponentDataAdditionalData data = new WeaponComponentDataAdditionalData();
        //            data.drawWeight = int.Parse(node.Attributes["draw_weight"].Value);
        //            __instance.AddData(data);
        //        }
        //    }
        //}

        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("FinishMissionLoading")]
        public class MissionLoadChangeParameters
        {
            private static void Postfix()
            {
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionArrow, 0.0015f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionJavelin, 0.00215f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionAxe, 0.01f);
                ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionKnife, 0.01f);
                ManagedParameters.SetParameter(ManagedParametersEnum.MissileMinimumDamageToStick, 12.5f);

                ManagedParameters.SetParameter(ManagedParametersEnum.MakesRearAttackDamageThreshold, 13f);
                ManagedParameters.SetParameter(ManagedParametersEnum.NonTipThrustHitDamageMultiplier, 1f);
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("WeaponEquipped")]
        private class OverrideWeaponEquipped
        {
            private static bool Prefix(ref Agent __instance, EquipmentIndex equipmentSlot, in WeaponData weaponData, ref WeaponStatsData[] weaponStatsData, in WeaponData ammoWeaponData, ref WeaponStatsData[] ammoWeaponStatsData, GameEntity weaponEntity, bool removeOldWeaponFromScene, bool isWieldedOnSpawn)
            {
                if (weaponStatsData != null)
                {
                    for (int i = 0; i < weaponStatsData.Length; i++)
                    {
                        SkillObject skill = (weaponData.GetItemObject() == null) ? DefaultSkills.Athletics : weaponData.GetItemObject().RelevantSkill;
                        if (skill != null)
                        {
                            int ef = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, skill);
                            float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);

                            MissionWeapon missionWeapon = __instance.Equipment[equipmentSlot];
                            EquipmentElement ee = new EquipmentElement(missionWeapon.Item);
                            Utilities.CalculateVisualSpeeds(ee, i, effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal);

                            if (swingSpeedReal >= 0 && thrustSpeedReal >= 0 && handlingReal >= 0)
                            {
                                weaponStatsData[i].SwingSpeed = swingSpeedReal;
                                weaponStatsData[i].ThrustSpeed = thrustSpeedReal;
                                weaponStatsData[i].DefendSpeed = handlingReal;
                            }

                            if ((WeaponClass)weaponStatsData[i].WeaponClass == WeaponClass.Bow)
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
                            float equipmentWeight = __instance.SpawnEquipment.GetTotalWeightOfArmor(true); //+ __instance.Equipment.GetTotalWeightOfWeapons();
                            WeaponClass typeOfShieldEquipped = WeaponClass.Undefined;
                            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                            {
                                if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty && __instance.Equipment[equipmentIndex].IsShield())
                                {
                                    typeOfShieldEquipped = __instance.Equipment[equipmentIndex].CurrentUsageItem.WeaponClass;

                                }
                            }
                            switch (weaponStatsData[i].WeaponClass)
                            {
                                case (int)WeaponClass.OneHandedPolearm:
                                case (int)WeaponClass.LowGripPolearm:
                                    {
                                        float ammoWeight = __instance.Equipment[equipmentSlot].GetWeight() / __instance.Equipment[equipmentSlot].Amount;
                                        weaponStatsData[i].MissileSpeed = Utilities.assignThrowableMissileSpeed(
                                            ammoWeight,
                                            (int)Utilities.throwableCorrectionSpeed,
                                            effectiveSkillDR,
                                            equipmentWeight,
                                            typeOfShieldEquipped
                                            );
                                        break;
                                    }
                                case (int)WeaponClass.Javelin:
                                    {
                                        float ammoWeight = __instance.Equipment[equipmentSlot].GetWeight() / __instance.Equipment[equipmentSlot].Amount;
                                        weaponStatsData[i].MissileSpeed = Utilities.assignThrowableMissileSpeed(
                                            ammoWeight,
                                            (int)Utilities.throwableCorrectionSpeed,
                                            effectiveSkillDR,
                                            equipmentWeight,
                                            typeOfShieldEquipped
                                            );
                                        break;
                                    }
                                case (int)WeaponClass.ThrowingAxe:
                                case (int)WeaponClass.ThrowingKnife:
                                case (int)WeaponClass.Dagger:
                                    {
                                        //weaponStatsData[i].MissileSpeed = Utilities.assignThrowableMissileSpeed(
                                        //__instance.Equipment[equipmentSlot].GetWeight() / __instance.Equipment[equipmentSlot].Amount,
                                        float ammoWeight = __instance.Equipment[equipmentSlot].GetWeight() / __instance.Equipment[equipmentSlot].Amount;
                                        weaponStatsData[i].MissileSpeed = Utilities.assignThrowableMissileSpeed(
                                            ammoWeight,
                                            (int)Utilities.throwableCorrectionSpeed,
                                            effectiveSkillDR,
                                            equipmentWeight,
                                            typeOfShieldEquipped
                                            );
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
        private class OverrideEquipItemsFromSpawnEquipment
        {
            private static bool Prefix(ref Agent __instance)
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

                        if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                        {
                            RangedWeaponStats rangedWeaponStatNew = new RangedWeaponStats(missionWeapon.CurrentUsageItem.MissileSpeed);
                            RangedWeaponStats rangedWeaponStatOld;
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
                            //msModifier = missionWeapon.ItemModifier.ModifyHitPoints(50) - 50;
                            msModifier = missionWeapon.ItemModifier.HitPoints;
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

            private static void Postfix(Agent __instance)
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

                        if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                        {
                            propertyMissileSpeed.SetValue(__instance.Equipment[equipmentIndex].CurrentUsageItem, rangedWeaponStats[mw.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("OnAgentShootMissile")]
        [UsedImplicitly]
        [MBCallback]
        private class OverrideOnAgentShootMissile
        {
            private static bool Prefix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, ref Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
            {
                MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

                if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle && !shooterAgent.IsMainAgent && (wsd[0].WeaponClass == (int)WeaponClass.Javelin || wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe))
                {
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
                    float ammoWeight;
                    if (missionWeapon.AmmoWeapon.IsEmpty)
                    {
                        ammoWeight = 0.07f;
                    }
                    else
                    {
                        float ammoWeightSum = missionWeapon.AmmoWeapon.GetWeight();
                        float ammoCount = missionWeapon.AmmoWeapon.Amount;
                        ammoWeight = ammoWeightSum / ammoCount;
                    }

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

                    double rotRad = 0.083141f;
                    float vecLength = velocity.Length;
                    double currentRad = (double)Math.Acos(velocity.z / vecLength);
                    float newZ = velocity.Length * ((float)Math.Cos(currentRad - rotRad));
                    velocity.z = newZ;
                }

                return true;
            }

            private static void Postfix(ref Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
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

        [UsedImplicitly]
        [MBCallback]
        [HarmonyPatch(typeof(Mission))]
        private class OverrideEndMission
        {
            [HarmonyPrefix]
            [HarmonyPatch("EndMission")]
            private static bool PrefixOnEndMissionResult(ref Mission __instance)
            {
                foreach (KeyValuePair<string, MissionWeapon> mw in rangedWeaponMW)
                {
                    WeaponStatsData[] wsd = mw.Value.GetWeaponStatsData();
                    if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                    {
                        PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                        property2.DeclaringType.GetProperty("MissileSpeed");
                        if (rangedWeaponStats.ContainsKey(mw.Value.GetModifiedItemName().ToString()))
                        {
                            property2.SetValue(mw.Value.CurrentUsageItem, rangedWeaponStats[mw.Value.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        }
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("OnWieldedItemIndexChange")]
        private class OnWieldedItemIndexChangePatch
        {
            private static void Postfix(ref Agent __instance, bool isOffHand, bool isWieldedInstantly, bool isWieldedOnSpawn)
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
                                        int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, skill);

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
                                    if (mwa.Amount > 0)
                                    {
                                        __instance.Equipment.GetAmmoCountAndIndexOfType(mw.Item.Type, out var ammouCount, out var eIndex);
                                        if (eIndex != EquipmentIndex.None)
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
    internal class OverrideRangedSiegeWeapon
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetTargetReleaseAngle")]
        private static bool PrefixGetTargetReleaseAngle(RangedSiegeWeapon __instance, ref float __result, Vec3 target, ref string[] ___SkeletonNames, ItemObject ___OriginalMissileItem)
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
        private static bool PrefixShootProjectileAux(ref RangedSiegeWeapon __instance, ref string[] ___SkeletonNames, ref ItemObject missileItem, ref Agent ____lastShooterAgent, ref ItemObject ___LoadedMissileItem)
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
                identity.f = mat2.f;
                identity.Orthonormalize();

                PropertyInfo property2 = typeof(RangedSiegeWeapon).GetProperty("Projectile", BindingFlags.NonPublic | BindingFlags.Instance);
                property2.DeclaringType.GetProperty("Projectile");
                Vec3 ProjectileEntityCurrentGlobalPosition = ((SynchedMissionObject)property2.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null)).GameEntity.GetGlobalFrame().origin;

                Mission.Current.AddCustomMissile(____lastShooterAgent, new MissionWeapon(missileItem, null, null, 1), ProjectileEntityCurrentGlobalPosition, identity.f, identity, 60f, 60f, addRigidBody: false, __instance);
                return false;
            }
            else
            {
                return true;
            }
        }

        [HarmonyPatch(typeof(AgentStatCalculateModel))]
        [HarmonyPatch("SetAiRelatedProperties")]
        private class OverrideSetAiRelatedProperties
        {
            private static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedItem, WeaponComponentData secondaryItem, AgentStatCalculateModel __instance)
            {
                if (agent.IsPlayerControlled)
                {
                    if (RBMConfig.RBMConfig.realisticRangedReload.Equals("1"))
                    {
                        SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                        if (skill != null)
                        {
                            int ef = __instance.GetEffectiveSkill(agent, skill);
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
                            int ef = __instance.GetEffectiveSkill(agent, skill);
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
                        int ef = __instance.GetEffectiveSkill(agent, skill);
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

    [HarmonyPatch(typeof(Mangonel))]
    internal class OverrideMangonel
    {
        [HarmonyPrefix]
        [HarmonyPatch("OnTick")]
        private static bool PrefixOnTick(ref Mangonel __instance, ref float ___currentReleaseAngle)
        {
            float baseSpeed = 20f;
            float speedIncrease = 1.125f;
            __instance.ProjectileSpeed = baseSpeed + (((___currentReleaseAngle * MathF.RadToDeg)) * speedIncrease);

            return true;
        }
    }

    [HarmonyPatch(typeof(Mission))]
    internal class HandleMissileCollisionReactionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("HandleMissileCollisionReaction")]
        private static bool Prefix(ref Mission __instance, ref Dictionary<int, Missile> ____missiles, int missileIndex, ref MissileCollisionReaction collisionReaction, MatrixFrame attachLocalFrame, Agent attackerAgent, Agent attachedAgent, bool attachedToShield, sbyte attachedBoneIndex, MissionObject attachedMissionObject, Vec3 bounceBackVelocity, Vec3 bounceBackAngularVelocity, int forcedSpawnIndex, bool isAttachedFrameLocal)
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
                GameNetwork.BeginBroadcastModuleEvent(); ;
                GameNetwork.WriteMessage(new HandleMissileCollisionReaction(missileIndex, collisionReaction, attachLocalFrame, isAttachedFrameLocal, attackerAgent.Index, attachedAgent?.Index ?? (-1), attachedToShield, attachedBoneIndex, attachedMissionObject?.Id ?? MissionObjectId.Invalid, bounceBackVelocity, bounceBackAngularVelocity, missionObjectId.Id));
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
    internal class MeleeHitCallbackPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("MeleeHitCallback")]
        private static bool Prefix(ref Mission __instance, ref AttackCollisionData collisionData, Agent attacker, Agent victim, GameEntity realHitEntity, ref float inOutMomentumRemaining, ref MeleeCollisionReaction colReaction, CrushThroughState crushThroughState, Vec3 blowDir, Vec3 swingDir, ref HitParticleResultData hitParticleResultData, bool crushedThroughWithoutAgentCollision)
        {
            if (collisionData.CollidedWithShieldOnBack)
            {
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
    }

    [UsedImplicitly]
    [MBCallback]
    [HarmonyPatch(typeof(Mission))]
    internal class MissileHitCallbackPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("MissileHitCallback")]
        private static bool Prefix(ref Mission __instance, ref Dictionary<int, Missile> ____missiles, ref AttackCollisionData collisionData, Vec3 missileStartingPosition, Vec3 missilePosition, Vec3 missileAngularVelocity, Vec3 movementVelocity, MatrixFrame attachGlobalFrame, MatrixFrame affectedShieldGlobalFrame, int numDamagedAgents, Agent attacker, Agent victim, GameEntity hitEntity)
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

                    if (attacker.Character != null)
                    {
                        TaleWorlds.CampaignSystem.CharacterObject characterObject = attacker.Character as TaleWorlds.CampaignSystem.CharacterObject;
                        if (characterObject != null)
                        {
                            if (characterObject.HeroObject != null)
                            {
                                if (characterObject.HeroObject.GetPerkValue(DefaultPerks.Throwing.Impale))
                                {
                                    return true;
                                }
                            }
                        }
                    }

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
        private static void Postfix(ref Mission __instance, ref Dictionary<int, Missile> ____missiles, ref AttackCollisionData collisionData, Vec3 missileStartingPosition, Vec3 missilePosition, Vec3 missileAngularVelocity, Vec3 movementVelocity, MatrixFrame attachGlobalFrame, MatrixFrame affectedShieldGlobalFrame, int numDamagedAgents, Agent attacker, Agent victim, GameEntity hitEntity)
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
                            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.ExtraWeaponSlot; equipmentIndex++)
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