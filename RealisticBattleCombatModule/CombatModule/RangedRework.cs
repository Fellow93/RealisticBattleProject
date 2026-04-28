using HarmonyLib;
using JetBrains.Annotations;
using NetworkMessages.FromServer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Mission;

namespace RBMCombat
{
    public class RangedRework
    {
        public static Dictionary<TextObject, int> originalItemSwingSpeed = new Dictionary<TextObject, int> { };
        public static Dictionary<TextObject, int> originalItemThrustSpeed = new Dictionary<TextObject, int> { };
        public static Dictionary<TextObject, int> originalItemHandling = new Dictionary<TextObject, int> { };
        public static Dictionary<string, RangedWeaponStats> rangedWeaponStats = new Dictionary<string, RangedWeaponStats>(new RangedWeaponStatsComparer());
        public static Dictionary<string, MissionWeapon> rangedWeaponMW = new Dictionary<string, MissionWeapon> { };

        private static readonly PropertyInfo MissileSpeedProperty = typeof(WeaponComponentData).GetProperty("MissileSpeed");
        private static readonly PropertyInfo SwingSpeedProperty = typeof(WeaponComponentData).GetProperty("SwingSpeed");
        private static readonly PropertyInfo ThrustSpeedProperty = typeof(WeaponComponentData).GetProperty("ThrustSpeed");
        private static readonly PropertyInfo HandlingProperty = typeof(WeaponComponentData).GetProperty("Handling");
        private static readonly PropertyInfo SiegeShootingDirectionProperty = typeof(RangedSiegeWeapon).GetProperty("ShootingDirection", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo SiegeShootingSpeedProperty = typeof(RangedSiegeWeapon).GetProperty("ShootingSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo SiegeProjectileProperty = typeof(RangedSiegeWeapon).GetProperty("Projectile", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo SiegeMissileStartPositionProperty = typeof(RangedSiegeWeapon).GetProperty("MissileStartingGlobalPositionForSimulation", BindingFlags.NonPublic | BindingFlags.Instance);

        private static bool IsBowOrCrossbow(WeaponClass weaponClass)
        {
            return weaponClass == WeaponClass.Bow || weaponClass == WeaponClass.Crossbow;
        }

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
                ManagedParameters.SetParameter(ManagedParametersEnum.BipedalRadius, 0.48f);
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

                            if (IsBowOrCrossbow((WeaponClass)weaponStatsData[i].WeaponClass))
                            {
                                // Compatibility build: keep projectile physics, but do not alter bow/crossbow fire rate or ready-state flags.
                                continue;
                            }

                            //float equipmentWeight = __instance.SpawnEquipment.GetTotalWeightOfArmor(true); //+ __instance.Equipment.GetTotalWeightOfWeapons();
                            float armorModifier = 0;
                            WeaponClass typeOfShieldEquipped = WeaponClass.Undefined;
                            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                            {
                                if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty && __instance.Equipment[equipmentIndex].IsShield())
                                {
                                    typeOfShieldEquipped = __instance.Equipment[equipmentIndex].CurrentUsageItem.WeaponClass;
                                }
                            }
                            armorModifier += MBMath.ClampFloat(ArmorRework.getShoulderArmor(__instance) - 20f, 0f, 100f);
                            armorModifier += MBMath.ClampFloat(ArmorRework.getArmArmor(__instance) - 20f, 0f, 100f);

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
                                            armorModifier,
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
                                            armorModifier,
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
                                            armorModifier,
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

                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {
                    if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                    {
                        MissionWeapon missionWeapon = __instance.Equipment[equipmentIndex];
                        WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

                        if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow) || (wsd[0].WeaponClass == (int)WeaponClass.Sling))
                        {
                            RangedWeaponStats rangedWeaponStatNew = new RangedWeaponStats(missionWeapon.CurrentUsageItem.MissileSpeed);
                            RangedWeaponStats rangedWeaponStatOld;
                            if (!rangedWeaponStats.TryGetValue(missionWeapon.GetModifiedItemName().ToString(), out rangedWeaponStatOld))
                            {
                                rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()] = rangedWeaponStatNew;
                            }
                            stringRangedWeapons.Add(missionWeapon);
                        }
                        if ((wsd[0].WeaponClass == (int)WeaponClass.Arrow) || (wsd[0].WeaponClass == (int)WeaponClass.Bolt) || (wsd[0].WeaponClass == (int)WeaponClass.SlingStone))
                        {
                            if (firstProjectile)
                            {
                                arrow = missionWeapon;
                                firstProjectile = false;
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

                        int msModifier = 0;
                        if (missionWeapon.ItemModifier != null)
                        {
                            msModifier = missionWeapon.ItemModifier.ModifyHitPoints(50) - 50;
                        }

                        WeaponStatsData[] mwWsd = missionWeapon.GetWeaponStatsData();
                        if (mwWsd != null && mwWsd.Length > 0 && mwWsd[0].WeaponClass == (int)WeaponClass.Sling)
                        {
                            // Slings use assignSlingMissileSpeed so skill and equipment weight
                            // (armor/shield) are factored in from the start.
                            WeaponData slingWd = missionWeapon.GetWeaponData(true);
                            SkillObject slingSkill = (slingWd.GetItemObject() == null) ? DefaultSkills.Athletics : slingWd.GetItemObject().RelevantSkill;
                            int slingEf = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, slingSkill);
                            float slingEffectiveSkillDR = Utilities.GetEffectiveSkillWithDR(slingEf);

                            float slingArmorModifier = 0;
                            WeaponClass slingShieldType = WeaponClass.Undefined;
                            for (EquipmentIndex ei = EquipmentIndex.WeaponItemBeginSlot; ei < EquipmentIndex.NumAllWeaponSlots; ei++)
                            {
                                if (__instance.Equipment != null && !__instance.Equipment[ei].IsEmpty && __instance.Equipment[ei].IsShield())
                                    slingShieldType = __instance.Equipment[ei].CurrentUsageItem.WeaponClass;
                            }
                            slingArmorModifier += MBMath.ClampFloat(ArmorRework.getShoulderArmor(__instance) - 20f, 0f, 100f);
                            slingArmorModifier += MBMath.ClampFloat(ArmorRework.getArmArmor(__instance) - 20f, 0f, 100f);

                            calculatedMissileSpeed = Utilities.assignSlingMissileSpeed(ammoWeight, missionWeapon.CurrentUsageItem.MissileSpeed + msModifier, slingEffectiveSkillDR, slingArmorModifier, slingShieldType);
                        }
                        else
                        {
                            calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon.CurrentUsageItem.ItemUsage, missionWeapon.CurrentUsageItem.MissileSpeed + msModifier);
                        }
                        rangedWeaponMW[missionWeapon.GetModifiedItemName().ToString()] = missionWeapon;

                        MissileSpeedProperty.SetValue(missionWeapon.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                    else if (!missionWeapon.Equals(MissionWeapon.Invalid))
                    {
                        rangedWeaponMW[missionWeapon.GetModifiedItemName().ToString()] = missionWeapon;
                        MissileSpeedProperty.SetValue(missionWeapon.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                }

                return true;
            }

            private static void Postfix(Agent __instance)
            {
                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {
                    if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                    {
                        MissionWeapon mw = __instance.Equipment[equipmentIndex];
                        WeaponStatsData[] wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData();

                        if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow) || (wsd[0].WeaponClass == (int)WeaponClass.Sling))
                        {
                            MissileSpeedProperty.SetValue(__instance.Equipment[equipmentIndex].CurrentUsageItem, rangedWeaponStats[mw.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
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
                            float relativeModifier = Vec3.DotProduct(shooterAgent.Velocity.NormalizedCopy(), targetAgent.Velocity.NormalizedCopy());
                            float shooterSpeed = shooterAgent.Velocity.Length;
                            if (shooterSpeed > 0)
                            {
                                float shooterRelativeSpeed = shooterSpeed * relativeModifier;
                                if (shooterRelativeSpeed <= 0)
                                {
                                    double rotRad = (0.0174533 * shooterRelativeSpeed) / 1.1f;
                                    float vecLength = velocity.Length;
                                    double currentRad = (double)Math.Acos(velocity.z / vecLength);
                                    float newZ = velocity.Length * ((float)Math.Cos(currentRad - rotRad));
                                    velocity.z = newZ;
                                }
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

                if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow) || (wsd[0].WeaponClass == (int)WeaponClass.Sling))
                {
                    float ammoWeight;
                    if (missionWeapon.AmmoWeapon.Item != null && missionWeapon.Item != null && !missionWeapon.AmmoWeapon.IsEmpty && missionWeapon.AmmoWeapon.Amount > 0)
                    {
                        float ammoWeightSum = missionWeapon.AmmoWeapon.GetWeight();
                        float ammoCount = missionWeapon.AmmoWeapon.Amount;
                        ammoWeight = ammoWeightSum / ammoCount;
                    }
                    else
                    {
                        ammoWeight = 0.07f;
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

                    int calculatedMissileSpeed;
                    if (wsd[0].WeaponClass == (int)WeaponClass.Sling)
                    {
                        // Slings factor in the shooter's skill and equipment weight on every shot.
                        WeaponData slingWd = missionWeapon.GetWeaponData(true);
                        SkillObject slingSkill = (slingWd.GetItemObject() == null) ? DefaultSkills.Athletics : slingWd.GetItemObject().RelevantSkill;
                        int slingEf = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(shooterAgent, slingSkill);
                        float slingEffectiveSkillDR = Utilities.GetEffectiveSkillWithDR(slingEf);

                        float slingArmorModifier = 0;
                        WeaponClass slingShieldType = WeaponClass.Undefined;
                        for (EquipmentIndex ei = EquipmentIndex.WeaponItemBeginSlot; ei < EquipmentIndex.NumAllWeaponSlots; ei++)
                        {
                            if (!shooterAgent.Equipment[ei].IsEmpty && shooterAgent.Equipment[ei].IsShield())
                                slingShieldType = shooterAgent.Equipment[ei].CurrentUsageItem.WeaponClass;
                        }
                        slingArmorModifier += MBMath.ClampFloat(ArmorRework.getShoulderArmor(shooterAgent) - 20f, 0f, 100f);
                        slingArmorModifier += MBMath.ClampFloat(ArmorRework.getArmArmor(shooterAgent) - 20f, 0f, 100f);

                        calculatedMissileSpeed = Utilities.assignSlingMissileSpeed(ammoWeight, rangedWeaponStats[min].getDrawWeight() + msModifier, slingEffectiveSkillDR, slingArmorModifier, slingShieldType);
                    }
                    else
                    {
                        calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon.CurrentUsageItem.ItemUsage, rangedWeaponStats[min].getDrawWeight() + msModifier);
                    }

                    Vec3 shooterAgentVelocity = new Vec3(shooterAgent.Velocity, -1);
                    Vec3 myVelocity = new Vec3(velocity, -1);

                    myVelocity.Normalize();

                    float shooterAgentSpeed = Vec3.DotProduct(shooterAgentVelocity, myVelocity);

                    Vec3 modifierVec = shooterAgentVelocity + myVelocity;

                    velocity.x = myVelocity.x * (calculatedMissileSpeed + shooterAgentSpeed);
                    velocity.y = myVelocity.y * (calculatedMissileSpeed + shooterAgentSpeed);
                    velocity.z = myVelocity.z * (calculatedMissileSpeed + shooterAgentSpeed);

                    MissileSpeedProperty.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
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
                if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow) || (wsd[0].WeaponClass == (int)WeaponClass.Sling))
                {
                    MissileSpeedProperty.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, rangedWeaponStats[missionWeapon.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
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
                    if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow) || (wsd[0].WeaponClass == (int)WeaponClass.Sling))
                    {
                        if (rangedWeaponStats.ContainsKey(mw.Value.GetModifiedItemName().ToString()))
                        {
                            MissileSpeedProperty.SetValue(mw.Value.CurrentUsageItem, rangedWeaponStats[mw.Value.GetModifiedItemName().ToString()].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
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
                EquipmentIndex wieldedItemIndex = __instance.GetPrimaryWieldedItemIndex();
                if (wieldedItemIndex != EquipmentIndex.None)
                {
                    WeaponStatsData[] wieldedStatsData = __instance.Equipment[wieldedItemIndex].GetWeaponStatsData();
                    if (wieldedStatsData == null || wieldedStatsData.Length == 0)
                    {
                        return;
                    }
                    WeaponStatsData weaponStatsData = wieldedStatsData[0];
                    WeaponData weaponData = __instance.Equipment[wieldedItemIndex].GetWeaponData(true);
                    if (weaponStatsData.WeaponClass == (int)WeaponClass.Bow)
                    {
                        // Bow/crossbow fire-rate and ready-state behavior are intentionally left vanilla.
                    }
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (__instance.Equipment[equipmentIndex].GetWeaponStatsData() != null && __instance.Equipment[equipmentIndex].GetWeaponStatsData().Length > 0)
                        {
                            WeaponData wd = __instance.Equipment[equipmentIndex].GetWeaponData(true);
                            WeaponStatsData wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData()[0];
                            if (IsBowOrCrossbow((WeaponClass)wsd.WeaponClass))
                            {
                                // Compatibility build: no bow/crossbow speed-shooting or ammo ready-state manipulation.
                                continue;
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
                    Vec3 MissileStartingGlobalPositionForSimulation = (Vec3)SiegeMissileStartPositionProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                    WeaponStatsData weaponStatsData = new MissionWeapon(___OriginalMissileItem, null, null).GetWeaponStatsDataForUsage(0);
                    __result = Mission.GetMissileVerticalAimCorrection(target - MissileStartingGlobalPositionForSimulation, 60f, ref weaponStatsData, ItemObject.GetAirFrictionConstant(___OriginalMissileItem.PrimaryWeapon.WeaponClass, ___OriginalMissileItem.PrimaryWeapon.WeaponFlags));
                    return false;
                }
                else
                {
                    return true;
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("ShootProjectileAux")]
            private static bool PrefixShootProjectileAux(ref RangedSiegeWeapon __instance, ref string[] ___SkeletonNames, ref ItemObject missileItem, ref Agent ___LastShooterAgent)
            {
                if (___SkeletonNames != null && ___SkeletonNames.Length > 0 && ___SkeletonNames[0].Contains("trebuchet"))
                {
                    for (int i = 0; i < 7; i++)
                    {
                        Mat3 mat = default(Mat3);

                        mat.f = (Vec3)SiegeShootingDirectionProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                        mat.u = Vec3.Up;
                        Mat3 mat2 = mat;
                        mat2.Orthonormalize();
                        float a = MBRandom.RandomFloat * ((float)Math.PI * 2f);
                        mat2.RotateAboutForward(a);
                        float f = 1.5f * MBRandom.RandomFloat;
                        mat2.RotateAboutSide(f.ToRadians());

                        Mat3 identity = Mat3.Identity;

                        ItemObject @object = Game.Current.ObjectManager.GetObject<ItemObject>("grapeshot_projectile");

                        float num = (float)SiegeShootingSpeedProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                        num *= MBRandom.RandomFloatRanged(0.95f, 1.05f);
                        identity.f = mat2.f;
                        identity.Orthonormalize();

                        Vec3 ProjectileEntityCurrentGlobalPosition = ((SynchedMissionObject)SiegeProjectileProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null)).GameEntity.GetGlobalFrame().origin;

                        Mission.Current.AddCustomMissile(___LastShooterAgent, new MissionWeapon(@object, null, ___LastShooterAgent.Origin?.Banner, 1), ProjectileEntityCurrentGlobalPosition, identity.f, identity, num, num, addRigidBody: false, __instance);
                    }
                    return false;
                }
                if (___SkeletonNames != null && ___SkeletonNames.Length > 0 && ___SkeletonNames[0].Contains("ballista"))
                {
                    Mat3 mat = default(Mat3);

                    mat.f = (Vec3)SiegeShootingDirectionProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

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

                    Vec3 ProjectileEntityCurrentGlobalPosition = ((SynchedMissionObject)SiegeProjectileProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null)).GameEntity.GetGlobalFrame().origin;

                    Mission.Current.AddCustomMissile(___LastShooterAgent, new MissionWeapon(missileItem, null, null, 1), ProjectileEntityCurrentGlobalPosition, identity.f, identity, 60f, 60f, addRigidBody: false, __instance);
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
                    // Compatibility build: bow/crossbow reload and draw speed are left to vanilla/game settings.
                }
            }
        }

        [HarmonyPatch(typeof(Mangonel))]
        internal class OverrideMangonel
        {
            [HarmonyPrefix]
            [HarmonyPatch("OnTick")]
            private static bool PrefixOnTick(ref Mangonel __instance, ref float ___CurrentReleaseAngle)
            {
                float baseSpeed = 25f;
                float speedIncrease = 1.5f;
                __instance.ProjectileSpeed = baseSpeed + (((___CurrentReleaseAngle * MathF.RadToDeg)) * speedIncrease);

                return true;
            }
        }

        [HarmonyPatch(typeof(Mission))]
        internal class HandleMissileCollisionReactionPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("HandleMissileCollisionReaction")]
            private static bool Prefix(ref Mission __instance, ref Dictionary<int, Missile> ____missilesDictionary, int missileIndex, ref MissileCollisionReaction collisionReaction, MatrixFrame attachLocalFrame, Agent attackerAgent, Agent attachedAgent, bool attachedToShield, sbyte attachedBoneIndex, MissionObject attachedMissionObject, Vec3 bounceBackVelocity, Vec3 bounceBackAngularVelocity, int forcedSpawnIndex, bool isAttachedFrameLocal)
            {
                Missile missile = ____missilesDictionary[missileIndex];
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
                                    wieldedItemIndex = attachedAgent.GetOffhandWieldedItemIndex();
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
            private static bool Prefix(ref Mission __instance, ref Dictionary<int, Missile> ____missilesDictionary, ref AttackCollisionData collisionData, Vec3 missileStartingPosition, Vec3 missilePosition, Vec3 missileAngularVelocity, Vec3 movementVelocity, MatrixFrame attachGlobalFrame, MatrixFrame affectedShieldGlobalFrame, int numDamagedAgents, Agent attacker, Agent victim, GameEntity hitEntity)
            {
                Missile missile;
                if (____missilesDictionary.TryGetValue(collisionData.AffectorWeaponSlotOrMissileIndex, out missile))
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
            private static void Postfix(ref Mission __instance, ref Dictionary<int, Missile> ____missilesDictionary, ref AttackCollisionData collisionData, Vec3 missileStartingPosition, Vec3 missilePosition, Vec3 missileAngularVelocity, Vec3 movementVelocity, MatrixFrame attachGlobalFrame, MatrixFrame affectedShieldGlobalFrame, int numDamagedAgents, Agent attacker, Agent victim, GameEntity hitEntity)
            {
                Missile missile;
                if (____missilesDictionary.TryGetValue(collisionData.AffectorWeaponSlotOrMissileIndex, out missile))
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
}