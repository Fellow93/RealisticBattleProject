using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle.SelectionItem;
using TaleWorlds.MountAndBlade.View.CustomBattle;
using TaleWorlds.ScreenSystem;

namespace RBM
{
    internal class CustomBattlePatches
    {
        private static readonly PropertyInfo SelectedMap = typeof(MapSelectionGroupVM).GetProperty("SelectedMap");
        private static CustomBattleVM _battleVM;

        private static GauntletLayer _hintLayer;
        private static CustomBattleHintVM _hintVM;

        private static void AddHintOverlay()
        {
            if (_hintLayer != null) return;
            var screen = ScreenManager.TopScreen;
            // Only attach to the custom battle setup screen, not to the mission screen during battle
            if (screen == null || !screen.GetType().Name.Contains("CustomBattle")) return;
            _hintVM = new CustomBattleHintVM();
            _hintLayer = new GauntletLayer("CustomBattleHintLayer", 100);
            _hintLayer.LoadMovie("CustomBattleHint", _hintVM);
            screen.AddLayer(_hintLayer);
        }

        private static void RemoveHintOverlay()
        {
            if (_hintLayer == null) return;
            ScreenManager.TopScreen?.RemoveLayer(_hintLayer);
            _hintVM?.OnFinalize();
            _hintLayer = null;
            _hintVM = null;
        }

        internal static void TickInput()
        {
            if (_battleVM == null) return;

            // Shortcuts and hint overlay are only active on the custom battle setup screen, not during a mission
            if (Mission.Current != null) return;

            // Add (or re-add after returning from battle) overlay whenever VM is live but layer is gone
            if (_hintLayer == null)
                AddHintOverlay();

            bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            if (!ctrl) return;
            if (Input.IsKeyPressed(InputKey.S))
                SavePresetToFile();
            else if (Input.IsKeyPressed(InputKey.L))
                LoadPresetFromFile();
        }

        private static string ShowSaveDialog()
        {
            string result = null;
            var thread = new Thread(() =>
            {
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Title = "Save Battle Preset";
                    dlg.Filter = "XML Preset|*.xml";
                    dlg.DefaultExt = "xml";
                    try { dlg.InitialDirectory = RBMConfig.Utilities.GetConfigFolderPath(); } catch { }
                    if (dlg.ShowDialog() == DialogResult.OK)
                        result = dlg.FileName;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }

        private static string ShowOpenDialog()
        {
            string result = null;
            var thread = new Thread(() =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "Load Battle Preset";
                    dlg.Filter = "XML Preset|*.xml";
                    try { dlg.InitialDirectory = RBMConfig.Utilities.GetConfigFolderPath(); } catch { }
                    if (dlg.ShowDialog() == DialogResult.OK)
                        result = dlg.FileName;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }

        private static void SavePresetToFile()
        {
            try
            {
                string path = ShowSaveDialog();
                if (string.IsNullOrEmpty(path)) return;
                string name = Path.GetFileNameWithoutExtension(path);
                var preset = CustomBattlePreset.CaptureFromVM(_battleVM, name);
                CustomBattlePreset.SavePresetToFile(preset, path);
                InformationManager.DisplayMessage(new InformationMessage($"Preset saved: '{Path.GetFileName(path)}'."));
            }
            catch (Exception) { }
        }

        private static void LoadPresetFromFile()
        {
            try
            {
                string path = ShowOpenDialog();
                if (string.IsNullOrEmpty(path)) return;
                var preset = CustomBattlePreset.LoadPresetFromFile(path);
                if (preset != null)
                {
                    CustomBattlePreset.ApplyNamedToVM(preset, _battleVM);
                    InformationManager.DisplayMessage(new InformationMessage($"Preset loaded: '{Path.GetFileName(path)}'."));
                }
            }
            catch (Exception) { }
        }

        // Custom battle provider ordering
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
                int jabalAshabIndex = __instance.MapSelection.ItemList.FindIndex((MapItemVM x) => x.MapName.Contains("Jabal Ashab"));
                MapItemVM jabalAshabMap = __instance.MapSelection.ItemList[jabalAshabIndex];
                __instance.MapSelection.ItemList[jabalAshabIndex] = __instance.MapSelection.ItemList[0];
                __instance.MapSelection.ItemList[0] = jabalAshabMap;

                int pendraicPrairieIndex = __instance.MapSelection.ItemList.FindIndex((MapItemVM x) => x.MapName.Contains("Pendraic Prairie"));
                MapItemVM pendraicPrairieMap = __instance.MapSelection.ItemList[pendraicPrairieIndex];
                __instance.MapSelection.ItemList[pendraicPrairieIndex] = __instance.MapSelection.ItemList[1];
                __instance.MapSelection.ItemList[1] = pendraicPrairieMap;

                __instance.MapSelection.SelectedIndex = 0;
                SelectedMap.SetValue(__instance, __instance.MapSelection.ItemList[0], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            }
        }

        // Track VM reference and restore last session settings on construction
        [HarmonyPatch(typeof(CustomBattleVM), MethodType.Constructor, new Type[] { typeof(CustomBattleState) })]
        private class CustomBattleVMConstructorPatch
        {
            private static void Postfix(CustomBattleVM __instance)
            {
                _battleVM = __instance;
                CustomBattlePreset.ApplyToVM(__instance);
            }
        }

        // Save settings when starting a battle (keep _battleVM so shortcuts work on return)
        [HarmonyPatch(typeof(CustomBattleVM), "ExecuteStart")]
        private class ExecuteStartPatch
        {
            private static void Prefix(CustomBattleVM __instance)
            {
                RemoveHintOverlay();
                CustomBattlePreset.SaveFromVM(__instance);
                CustomBattlePreset.SavePreset();
            }
        }

        // Save settings and release VM reference when going back
        [HarmonyPatch(typeof(CustomBattleVM), "ExecuteBack")]
        private class ExecuteBackPatch
        {
            private static void Prefix(CustomBattleVM __instance)
            {
                RemoveHintOverlay();
                CustomBattlePreset.SaveFromVM(__instance);
                CustomBattlePreset.SavePreset();
                _battleVM = null;
            }
        }
    }
}
