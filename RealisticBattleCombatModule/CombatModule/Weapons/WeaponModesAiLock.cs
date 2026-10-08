using HarmonyLib;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    /// <summary>
    /// Keeps AI agents in the normal grip of a sword with the prototype modes (Half-Sword, Mordhau; RBMConfig.WeaponModes)
    /// until an AI that switches modes on purpose exists.
    ///
    /// Why: no managed code picks a weapon usage. The engine's AI chooses the usage of the weapon in hand itself (it
    /// re-picks right after spawn, and switches without an input event, see SignatureWeaponGrip), from the per-usage
    /// WeaponStatsData it is handed in Agent.WeaponEquipped. Given the sword's three usages it settled on the Mordhau
    /// (index 2). Its criterion is native and not visible; the Mordhau differs from the normal grip in that data by a
    /// Blunt swing, slower swing and handling, an axe usage set with no thrust, and a hand frame 0.94 m up the blade.
    ///
    /// Fix: for an AI agent only, the copy of the weapon's data sent to the engine has every mode usage replaced by the
    /// normal grip's, so the engine sees three identical grips and stays on the first (the same tie it kept with the
    /// replaced grips of SignatureWeaponGrip). The ItemObject and the managed MissionWeapon are untouched, so the player
    /// keeps all three modes and every other agent and weapon is unaffected. A weapon picked up in a mode (dropped by
    /// the player) is put back in the normal grip for an AI. Known gap: an agent whose controller changes mid-mission
    /// keeps the data it was equipped with until his next re-equip.
    ///
    /// The battle log (Field Battle Logging) records every usage change of such a sword: USAGE lines.
    /// </summary>
    public static class WeaponModesAiLock
    {
        /// <summary>The first usage of an item that is not a prototype mode, or -1 if the item has no mode usages.</summary>
        public static int GetNormalUsageIndex(ItemObject item)
        {
            if (item?.WeaponComponent == null)
            {
                return -1;
            }
            MBReadOnlyList<WeaponComponentData> usages = item.Weapons;
            int normal = -1;
            bool hasMode = false;
            for (int i = 0; i < usages.Count; i++)
            {
                if (RBMConfig.WeaponModes.HasOwnDamageType(usages[i]))
                {
                    hasMode = true;
                }
                else if (normal < 0)
                {
                    normal = i;
                }
            }
            return hasMode ? normal : -1;
        }

        private static bool IsAi(Agent agent)
        {
            return agent.Controller == AgentControllerType.AI && !agent.IsPlayerControlled;
        }

        /// <summary>
        /// Low priority: after RangedRework's WeaponEquipped prefix, which rewrites every usage's speeds, so the copies
        /// carry the normal grip's final speeds.
        /// </summary>
        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("WeaponEquipped")]
        private class LockModesForAiPatch
        {
            [HarmonyPriority(Priority.Low)]
            private static void Prefix(Agent __instance, EquipmentIndex equipmentSlot, ref WeaponData weaponData, WeaponStatsData[] weaponStatsData)
            {
                if (weaponStatsData == null || !IsAi(__instance))
                {
                    return;
                }
                MissionWeapon weapon = __instance.Equipment[equipmentSlot];
                if (weapon.IsEmpty)
                {
                    return;
                }
                int normal = GetNormalUsageIndex(weapon.Item);
                if (normal < 0 || normal >= weaponStatsData.Length)
                {
                    return;
                }
                MBReadOnlyList<WeaponComponentData> usages = weapon.Item.Weapons;
                for (int i = 0; i < weaponStatsData.Length && i < usages.Count; i++)
                {
                    if (RBMConfig.WeaponModes.HasOwnDamageType(usages[i]))
                    {
                        weaponStatsData[i] = weaponStatsData[normal];
                    }
                }
                if (RBMConfig.WeaponModes.HasOwnDamageType(weapon.CurrentUsageItem))
                {
                    __instance.Equipment.SetUsageIndexOfSlot(equipmentSlot, normal);
                    weaponData.CurrentUsageIndex = normal;
                }
            }
        }

        /// <summary>Battle log: who changed the usage of a sword with modes, from what to what, when, and AI or not.</summary>
        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("OnWeaponUsageIndexChange")]
        private class LogUsageChangePatch
        {
            private static void Prefix(Agent __instance, EquipmentIndex slotIndex, out int __state)
            {
                __state = -1;
                if (!BattleHitLog.IsEnabled || slotIndex < EquipmentIndex.WeaponItemBeginSlot || slotIndex >= EquipmentIndex.NumAllWeaponSlots)
                {
                    return;
                }
                MissionWeapon weapon = __instance.Equipment[slotIndex];
                if (!weapon.IsEmpty && GetNormalUsageIndex(weapon.Item) >= 0)
                {
                    __state = weapon.CurrentUsageIndex;
                }
            }

            private static void Postfix(Agent __instance, EquipmentIndex slotIndex, int usageIndex, int __state)
            {
                if (__state < 0 || __state == usageIndex)
                {
                    return;
                }
                MissionWeapon weapon = __instance.Equipment[slotIndex];
                WeaponComponentData usage = weapon.IsEmpty ? null : weapon.CurrentUsageItem;
                StringBuilder sb = new StringBuilder();
                sb.Append("    USAGE ").Append(__instance.Name)
                  .Append(IsAi(__instance) ? " (AI)" : (__instance.IsMainAgent ? " (player)" : " (not AI)"))
                  .Append(" ").Append(weapon.Item?.StringId)
                  .Append(" usage ").Append(__state).Append(" -> ").Append(usageIndex)
                  .Append(usage != null ? ":" + usage.WeaponDescriptionId : "")
                  .Append(" at ").Append(BattleHitLog.Clock(__instance.Mission != null ? __instance.Mission.CurrentTime : 0f));
                BattleHitLog.Write(sb.ToString());
            }
        }
    }
}
