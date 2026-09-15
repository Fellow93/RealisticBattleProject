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

                            if ((WeaponClass)weaponStatsData[i].WeaponClass == WeaponClass.Bow)
                            {
                                int thrustSpeed = missionWeapon.GetModifiedThrustSpeedForCurrentUsage();
                                if (RBMConfig.RBMConfig.realisticRangedReload.Equals("1") || RBMConfig.RBMConfig.realisticRangedReload.Equals("2"))
                                {
                                    float DrawSpeedskillModifier = 1 + (ef * 0.01f);
                                    weaponStatsData[i].ThrustSpeed = MathF.Ceiling((thrustSpeed * 0.2f) * DrawSpeedskillModifier);
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
        [HarmonyPatch("OnWieldedItemIndexChange")]
        private class OnWieldedItemIndexChangePatch
        {
            private static void Postfix(ref Agent __instance, bool isOffHand, bool isWieldedInstantly, bool isWieldedOnSpawn)
            {
                EquipmentIndex wieldedItemIndex = __instance.GetPrimaryWieldedItemIndex();
                if (wieldedItemIndex != EquipmentIndex.None)
                {
                    bool isBowWielded = false;
                    WeaponStatsData[] wieldedStatsData = __instance.Equipment[wieldedItemIndex].GetWeaponStatsData();
                    if (wieldedStatsData == null || wieldedStatsData.Length == 0)
                    {
                        return;
                    }
                    WeaponStatsData weaponStatsData = wieldedStatsData[0];
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
                                    SkillObject skill = (wd.GetItemObject() == null) ? DefaultSkills.Athletics : wd.GetItemObject().RelevantSkill;
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
}
