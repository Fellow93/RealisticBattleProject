using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle.SelectionItem;
using TaleWorlds.MountAndBlade.View.CustomBattle;

namespace RBM
{
    internal class CustomBattlePatches
    {
        private static readonly PropertyInfo SelectedMap = typeof(MapSelectionGroupVM).GetProperty("SelectedMap");

        //land custom battle is loadd first
        [HarmonyPatch(typeof(CustomBattleFactory))]
        [HarmonyPatch("StartCustomBattle")]
        private class StartCustomBattlePatch
        {
            private static bool Prefix(List<Type> ____providers)
            {
                if (____providers.Count > 1)
                {
                    (Activator.CreateInstance(____providers[1]) as ICustomBattleProvider).StartCustomBattle();
                    return false;
                }
                if (____providers.Count > 0)
                {
                    (Activator.CreateInstance(____providers[0]) as ICustomBattleProvider).StartCustomBattle();
                    return false;
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(MapSelectionGroupVM))]
        [HarmonyPatch("RefreshValues")]
        private class RefreshValuesPatch
        {
            private static void Postfix(MapSelectionGroupVM __instance)
            {
                //move jabal ashab to top of list
                int jabalAshabIndex = __instance.MapSelection.ItemList.FindIndex((MapItemVM x) => x.MapName.Contains("Jabal Ashab"));
                MapItemVM jabalAshabMap = __instance.MapSelection.ItemList[jabalAshabIndex];
                __instance.MapSelection.ItemList[jabalAshabIndex] = __instance.MapSelection.ItemList[0];
                __instance.MapSelection.ItemList[0] = jabalAshabMap;

                //pendraic prairie should be second in list
                int pendraicPrairieIndex = __instance.MapSelection.ItemList.FindIndex((MapItemVM x) => x.MapName.Contains("Pendraic Prairie"));
                MapItemVM pendraicPrairieMap = __instance.MapSelection.ItemList[pendraicPrairieIndex];
                __instance.MapSelection.ItemList[pendraicPrairieIndex] = __instance.MapSelection.ItemList[1];
                __instance.MapSelection.ItemList[1] = pendraicPrairieMap;

                __instance.MapSelection.SelectedIndex = 0;
                SelectedMap.SetValue(__instance, __instance.MapSelection.ItemList[0], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            }
        }

        // Restore saved settings after the VM finishes constructing
        [HarmonyPatch(typeof(CustomBattleVM), MethodType.Constructor, new Type[] { typeof(CustomBattleState) })]
        private class CustomBattleVMConstructorPatch
        {
            private static void Postfix(CustomBattleVM __instance)
            {
                CustomBattlePreset.ApplyToVM(__instance);
            }
        }

        // Save settings when the player starts a battle
        [HarmonyPatch(typeof(CustomBattleVM), "ExecuteStart")]
        private class ExecuteStartPatch
        {
            private static void Prefix(CustomBattleVM __instance)
            {
                CustomBattlePreset.SaveFromVM(__instance);
                CustomBattlePreset.SavePreset();
            }
        }

        // Save settings when the player goes back
        [HarmonyPatch(typeof(CustomBattleVM), "ExecuteBack")]
        private class ExecuteBackPatch
        {
            private static void Prefix(CustomBattleVM __instance)
            {
                CustomBattlePreset.SaveFromVM(__instance);
                CustomBattlePreset.SavePreset();
            }
        }
    }
}