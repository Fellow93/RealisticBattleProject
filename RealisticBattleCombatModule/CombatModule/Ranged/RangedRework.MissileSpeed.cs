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
    public partial class RangedRework
    {
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
                            if (!rangedWeaponStats.TryGetValue(GetRangedWeaponKey(missionWeapon), out rangedWeaponStatOld))
                            {
                                rangedWeaponStats[GetRangedWeaponKey(missionWeapon)] = rangedWeaponStatNew;
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
                            // Do NOT call missionWeapon.GetWeaponData(true) here: with needBatchedVersionForMeshes
                            // it acquires two native PhysicsShape resources that vanilla always pairs with
                            // DeinitializeManagedPointers(), and this prefix runs inside the spawn equip batch.
                            // Only the ItemObject is needed, which the MissionWeapon exposes directly.
                            ItemObject slingItem = missionWeapon.Item;
                            SkillObject slingSkill = (slingItem == null) ? DefaultSkills.Athletics : slingItem.RelevantSkill;
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
                        rangedWeaponMW[GetRangedWeaponKey(missionWeapon)] = missionWeapon;

                        MissileSpeedProperty.SetValue(missionWeapon.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                    else if (!missionWeapon.Equals(MissionWeapon.Invalid))
                    {
                        rangedWeaponMW[GetRangedWeaponKey(missionWeapon)] = missionWeapon;
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
                            MissileSpeedProperty.SetValue(__instance.Equipment[equipmentIndex].CurrentUsageItem, rangedWeaponStats[GetRangedWeaponKey(mw)].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
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
                Vec3 engineLaunchVelocity = velocity;

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
                                    double currentRad = (double)Math.Acos(MBMath.ClampFloat(velocity.z / vecLength, -1f, 1f));
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
                            double currentRad = (double)Math.Acos(MBMath.ClampFloat(velocity.z / vecLength, -1f, 1f));
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
                    if (!rangedWeaponStats.TryGetValue(GetRangedWeaponKey(missionWeapon), out rws))
                    {
                        rangedWeaponMW[GetRangedWeaponKey(missionWeapon)] = missionWeapon;
                        rangedWeaponStats[GetRangedWeaponKey(missionWeapon)] = new RangedWeaponStats(missionWeapon.CurrentUsageItem.MissileSpeed);
                    }

                    string min = GetRangedWeaponKey(missionWeapon);

                    int msModifier = 0;
                    if (missionWeapon.ItemModifier != null)
                    {
                        msModifier = missionWeapon.ItemModifier.ModifyHitPoints(50) - 50;
                    }

                    int calculatedMissileSpeed;
                    if (wsd[0].WeaponClass == (int)WeaponClass.Sling)
                    {
                        // Slings factor in the shooter's skill and equipment weight on every shot.
                        // Same as the spawn prefix: never GetWeaponData(true) just for the item (leaks PhysicsShapes).
                        ItemObject slingItem = missionWeapon.Item;
                        SkillObject slingSkill = (slingItem == null) ? DefaultSkills.Athletics : slingItem.RelevantSkill;
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

                    // A modded launcher whose draw weight plus a negative modifier is <= 0 gives sqrt(negative) = NaN,
                    // cast to int.MinValue. That would go into the weapon stats AddMissile reads, so keep the engine's shot.
                    if (calculatedMissileSpeed <= 0)
                    {
                        return true;
                    }

                    Vec3 engineVelocity = velocity;
                    Vec3 shooterAgentVelocity = new Vec3(shooterAgent.Velocity, -1);
                    Vec3 myVelocity = new Vec3(velocity, -1);

                    myVelocity.Normalize();

                    float shooterAgentSpeed = Vec3.DotProduct(shooterAgentVelocity, myVelocity);

                    Vec3 modifierVec = shooterAgentVelocity + myVelocity;

                    velocity.x = myVelocity.x * (calculatedMissileSpeed + shooterAgentSpeed);
                    velocity.y = myVelocity.y * (calculatedMissileSpeed + shooterAgentSpeed);
                    velocity.z = myVelocity.z * (calculatedMissileSpeed + shooterAgentSpeed);

                    if (wsd[0].WeaponClass != (int)WeaponClass.Sling)
                    {
                        MissileAimTrace.BeginShot(shooterAgent, (WeaponClass)wsd[0].WeaponClass, position, engineVelocity, velocity, calculatedMissileSpeed, ammoWeight);
                    }

                    MissileSpeedProperty.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }

                //if (shooterAgent != null && !shooterAgent.IsAIControlled && !BannerlordConfig.DisplayTargetingReticule && (wsd[0].WeaponClass == (int)WeaponClass.Bow || wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                if (shooterAgent != null && !shooterAgent.IsAIControlled && RBMConfig.RBMConfig.rbmCombatEnabled && RBMConfig.RBMConfig.realisticArrowArc && (wsd[0].WeaponClass == (int)WeaponClass.Bow || wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                {

                    double rotRad = 0.083141f;
                    float vecLength = velocity.Length;
                    double currentRad = (double)Math.Acos(MBMath.ClampFloat(velocity.z / vecLength, -1f, 1f));
                    float newZ = velocity.Length * ((float)Math.Cos(currentRad - rotRad));
                    velocity.z = newZ;
                }

                // A NaN/Infinity or zero launch velocity is handed straight to native AddMissile, which
                // access-violates on it; fire the engine's own shot instead.
                if (!velocity.IsValid || velocity.LengthSquared < 1f)
                {
                    velocity = engineLaunchVelocity;
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
                    MissileSpeedProperty.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, rangedWeaponStats[GetRangedWeaponKey(missionWeapon)].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }
                MissileAimTrace.CommitShot(__instance);
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
                        if (rangedWeaponStats.ContainsKey(GetRangedWeaponKey(mw.Value)))
                        {
                            MissileSpeedProperty.SetValue(mw.Value.CurrentUsageItem, rangedWeaponStats[GetRangedWeaponKey(mw.Value)].getDrawWeight(), BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        }
                    }
                }
                // Every shared WeaponComponentData.MissileSpeed is back at its draw weight now, so the caches
                // can be dropped; the next mission re-captures them on spawn/shot.
                rangedWeaponMW.Clear();
                rangedWeaponStats.Clear();
                return true;
            }
        }
    }
}
