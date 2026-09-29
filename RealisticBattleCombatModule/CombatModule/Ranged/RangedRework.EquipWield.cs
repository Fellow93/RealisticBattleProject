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
                // AirFrictionArrow is NOT overridden: it has a compiled-in twin in native_core_parameters (see the
                // comment in Native managed_core_parameters.xml) that RBM cannot change. Setting 0.0015 here made the
                // AI aim with one drag while arrows and bolts flew with the other, so AI shots fell short, worse with
                // distance (~3 m at 60 m, ~18 m at 150 m). Tune reach through missile speed instead.
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
                                    // "Vanilla" means the bow's own draw speed for the player. The flat 0.45 cut
                                    // stays AI-only unless "Ranged reload applies to AI" is on, in which case AI
                                    // follows the setting too and gets the bow's own draw speed like the player.
                                    weaponStatsData[i].ThrustSpeed = (__instance.IsPlayerControlled || RBMConfig.RBMConfig.rangedReloadAffectsAi) ? thrustSpeed : MathF.Ceiling(thrustSpeed * 0.45f);
                                }

                                MissionWeapon mw = __instance.Equipment[equipmentSlot];
                                RangedWeaponStats rws;
                                if (rangedWeaponStats.TryGetValue(GetRangedWeaponKey(mw), out rws))
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
                    // No GetWeaponData(true) here: with needBatchedVersionForMeshes it acquires two native
                    // PhysicsShape resources per call that vanilla always releases with
                    // DeinitializeManagedPointers(). This postfix runs on the spawn wield for every agent
                    // and only ever needed the ItemObject, which MissionWeapon.Item exposes directly.
                    if (weaponStatsData.WeaponClass == (int)WeaponClass.Bow)
                    {
                        isBowWielded = true;
                    }
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (__instance.Equipment[equipmentIndex].GetWeaponStatsData() != null && __instance.Equipment[equipmentIndex].GetWeaponStatsData().Length > 0)
                        {
                            ItemObject slotItem = __instance.Equipment[equipmentIndex].Item;
                            WeaponStatsData wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData()[0];
                            if (wsd.WeaponClass == (int)WeaponClass.Bow)
                            {
                                MissionWeapon mw = __instance.Equipment[equipmentIndex];
                                if (isBowWielded)
                                {
                                    SkillObject skill = (slotItem == null) ? DefaultSkills.Athletics : slotItem.RelevantSkill;
                                    if (skill != null)
                                    {
                                        int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, skill);

                                        RangedWeaponStats rws;
                                        if (rangedWeaponStats.TryGetValue(GetRangedWeaponKey(mw), out rws))
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

                                    if (mw.AmmoWeapon.Amount > 0)
                                    {
                                        QueueBowUnload(__instance, equipmentIndex);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // A sheathed bow keeps its nocked arrow when the skill check cleared UnloadWhenSheathed, so it is
        // returned to the quiver by hand. That is deferred out of OnWieldedItemIndexChange: it is a native
        // callback fired mid weapon switch (en masse when deployment ends), and changing ammo from inside
        // it re-enters the engine. Vanilla defers equipment changes from callbacks the same way (Mission._tickActions).
        private static readonly List<(Agent agent, EquipmentIndex bowSlot)> PendingBowUnloads = new List<(Agent, EquipmentIndex)>();
        private static readonly object PendingBowUnloadsLock = new object();

        private static void QueueBowUnload(Agent agent, EquipmentIndex bowSlot)
        {
            lock (PendingBowUnloadsLock)
            {
                // One entry per bow: a duplicate would return the arrow twice if the managed mirror lags.
                if (!PendingBowUnloads.Contains((agent, bowSlot)))
                {
                    PendingBowUnloads.Add((agent, bowSlot));
                }
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("OnTick")]
        private class ProcessPendingBowUnloadsPatch
        {
            private static void Prefix(Mission __instance)
            {
                List<(Agent agent, EquipmentIndex bowSlot)> pending;
                lock (PendingBowUnloadsLock)
                {
                    if (PendingBowUnloads.Count == 0)
                    {
                        return;
                    }
                    pending = new List<(Agent, EquipmentIndex)>(PendingBowUnloads);
                    PendingBowUnloads.Clear();
                }
                foreach ((Agent agent, EquipmentIndex bowSlot) in pending)
                {
                    // Entries left over from a previous mission are dropped here.
                    if (agent.Mission == __instance && agent.IsActive())
                    {
                        UnloadSheathedBow(agent, bowSlot);
                    }
                }
            }
        }

        private static void UnloadSheathedBow(Agent agent, EquipmentIndex bowSlot)
        {
            MissionWeapon bow = agent.Equipment[bowSlot];
            // The bow may have been dropped, swapped or drawn again since the switch was queued.
            if (bow.IsEmpty || bow.Item == null || bow.CurrentUsageItem.WeaponClass != WeaponClass.Bow || agent.GetPrimaryWieldedItemIndex() == bowSlot)
            {
                return;
            }
            MissionWeapon nockedAmmo = bow.AmmoWeapon;
            int toReturn = nockedAmmo.Amount;
            if (toReturn <= 0 || nockedAmmo.Item == null || nockedAmmo.Item.PrimaryWeapon == null)
            {
                return;
            }

            // Prefer the quiver the arrow came from, then any quiver of the same ammo class; never past its max.
            List<(EquipmentIndex slot, short newAmount)> returns = new List<(EquipmentIndex, short)>();
            for (int pass = 0; pass < 2 && toReturn > 0; pass++)
            {
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots && toReturn > 0; i++)
                {
                    MissionWeapon quiver = agent.Equipment[i];
                    if (i == bowSlot || quiver.IsEmpty || quiver.Item == null || quiver.Item.PrimaryWeapon == null ||
                        !quiver.IsSameType(nockedAmmo) || (pass == 0) != (quiver.Item == nockedAmmo.Item))
                    {
                        continue;
                    }
                    int room = quiver.ModifiedMaxAmount - quiver.Amount;
                    if (room <= 0)
                    {
                        continue;
                    }
                    int returned = Math.Min(room, toReturn);
                    returns.Add((i, (short)(quiver.Amount + returned)));
                    toReturn -= returned;
                }
            }
            // No quiver with room: leave the arrow nocked rather than destroy it.
            if (returns.Count == 0)
            {
                return;
            }

            // Empty the bow the way a client applies a shot (loaded ammo consumed to 0, no quiver slot), so
            // the engine has no quiver to hand the arrow back to; the quiver amounts are the only return.
            agent.SetWeaponAmmoAsClient(bowSlot, EquipmentIndex.None, 0);
            agent.SetWeaponReloadPhaseAsClient(bowSlot, 0);
            foreach ((EquipmentIndex slot, short newAmount) in returns)
            {
                agent.SetWeaponAmountInSlot(slot, newAmount, enforcePrimaryItem: true);
            }
        }
    }
}
