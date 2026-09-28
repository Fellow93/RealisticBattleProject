using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using TaleWorlds.ModuleManager;

namespace RBM
{
    /// <summary>
    /// Gates RBM's native-parameter overrides (RBM_native_parameters_tweaks.xml) behind the RBM Combat toggle.
    ///
    /// That file is registered in ModuleData/project.mbproj and loaded by the engine itself, so the managed
    /// XML gate in XmlLoadingPatches never sees it: with Combat disabled, RBM's slower walk speed, 10s guard
    /// reset, wider ranged_target_radius_multiplier etc. all stayed active. The engine exposes no way to skip
    /// the file, so instead we push every parameter RBM touches back to the value it would have without RBM,
    /// through the engine's override_native_parameter binding (MBAPI.IMBDebugExtensions, internal).
    ///
    /// Both value sets are rebuilt from the active modules' own project.mbproj files in load order, so a mod
    /// loaded after RBM that sets the same parameter keeps its value, and nothing is hardcoded.
    ///
    /// Only native_parameters files are handled. RBM_spear_combat_parameters.xml (animation_combat_parameters)
    /// has no runtime override API and is gated by CombatParameterGate instead.
    /// </summary>
    internal static class NativeParameterGate
    {
        // True while the engine holds our without-RBM values, so re-enabling Combat in the same session
        // (config screen at the main menu, then a new game start) knows to restore RBM's.
        private static bool _revertedToBaseline;

        public static void Apply()
        {
            try
            {
                bool combatEnabled = RBMConfig.RBMConfig.rbmCombatEnabled;
                if (combatEnabled && !_revertedToBaseline)
                {
                    // The engine already loaded RBM's values at startup; nothing to do. The disabled case is
                    // re-applied on every game start rather than once, in case the engine reloads its parameters.
                    return;
                }

                Dictionary<string, float> rbmValues = new Dictionary<string, float>();
                Dictionary<string, float> withRbm = new Dictionary<string, float>();
                Dictionary<string, float> withoutRbm = new Dictionary<string, float>();
                foreach (ModuleInfo module in ModuleHelper.GetActiveModules())
                {
                    bool isRbm = module.Id == SubModule.ModuleId;
                    foreach (KeyValuePair<string, float> param in ReadModuleNativeParameters(module))
                    {
                        withRbm[param.Key] = param.Value;
                        if (isRbm)
                        {
                            rbmValues[param.Key] = param.Value;
                        }
                        else
                        {
                            withoutRbm[param.Key] = param.Value;
                        }
                    }
                }

                MethodInfo overrideMethod = GetOverrideMethod(out object debugExtensions);
                if (overrideMethod == null)
                {
                    TaleWorlds.Library.Debug.Print("[RBM] Native parameters: override_native_parameter binding not found, RBM's native parameters stay active");
                    return;
                }

                Dictionary<string, float> target = combatEnabled ? withRbm : withoutRbm;
                int applied = 0;
                foreach (string id in rbmValues.Keys)
                {
                    if (!withoutRbm.TryGetValue(id, out float baseline))
                    {
                        TaleWorlds.Library.Debug.Print($"[RBM] Native parameters: no non-RBM value for '{id}', left at RBM's {rbmValues[id].ToString(CultureInfo.InvariantCulture)}");
                        continue;
                    }
                    if (baseline == withRbm[id])
                    {
                        // RBM's entry matches the baseline (or a later module overrides it anyway); no-op.
                        continue;
                    }
                    float value = target[id];
                    overrideMethod.Invoke(debugExtensions, new object[] { id, value });
                    TaleWorlds.Library.Debug.Print($"[RBM] Native parameters: {id} = {value.ToString(CultureInfo.InvariantCulture)}");
                    applied++;
                }

                _revertedToBaseline = !combatEnabled;
                TaleWorlds.Library.Debug.Print(combatEnabled
                    ? $"[RBM] Native parameters: RBM Combat re-enabled, restored {applied} RBM value(s)"
                    : $"[RBM] Native parameters: RBM Combat disabled, reverted {applied} parameter(s) to non-RBM values");
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[RBM] Native parameters: error applying overrides: {ex}");
            }
        }

        private static MethodInfo GetOverrideMethod(out object debugExtensions)
        {
            debugExtensions = null;
            Type mbApi = AccessTools.TypeByName("TaleWorlds.MountAndBlade.MBAPI");
            Type extensionsType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.IMBDebugExtensions");
            if (mbApi == null || extensionsType == null)
            {
                return null;
            }
            debugExtensions = AccessTools.Field(mbApi, "IMBDebugExtensions")?.GetValue(null);
            if (debugExtensions == null)
            {
                return null;
            }
            return AccessTools.Method(extensionsType, "OverrideNativeParameter", new[] { typeof(string), typeof(float) });
        }

        // Every native_parameter the module's project.mbproj registers, later files overriding earlier ones.
        private static Dictionary<string, float> ReadModuleNativeParameters(ModuleInfo module)
        {
            Dictionary<string, float> result = new Dictionary<string, float>();
            string mbprojPath = Path.Combine(module.FolderPath, "ModuleData", "project.mbproj");
            if (!File.Exists(mbprojPath))
            {
                return result;
            }

            XmlDocument mbproj = new XmlDocument();
            mbproj.Load(mbprojPath);
            XmlNodeList files = mbproj.SelectNodes("/base/file[@type='native_parameters']");
            if (files == null)
            {
                return result;
            }
            foreach (XmlNode file in files)
            {
                string name = file.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                string paramsPath = Path.Combine(module.FolderPath, name);
                if (!File.Exists(paramsPath))
                {
                    continue;
                }

                XmlDocument paramsDoc = new XmlDocument();
                paramsDoc.Load(paramsPath);
                XmlNodeList parameters = paramsDoc.SelectNodes("//native_parameter");
                if (parameters == null)
                {
                    continue;
                }
                foreach (XmlNode parameter in parameters)
                {
                    string id = parameter.Attributes?["id"]?.Value;
                    string value = parameter.Attributes?["value"]?.Value;
                    if (!string.IsNullOrEmpty(id)
                        && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    {
                        result[id] = parsed;
                    }
                }
            }
            return result;
        }
    }
}
