using HarmonyLib;
using System;
using System.Globalization;
using System.IO;
using System.Xml;
using TaleWorlds.GauntletUI.PrefabSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Grows the campaign-map Escape menu's background panel so the extra "RBM Ledger" row (added by
    /// RBMLedgerEscapeMenuPatch) does not spill out of the frame. The panel Widget Id="EscapeMenu" in
    /// Native's EscapeMenu.xml is Fixed-height, sized to the escape_panel sprite, while the buttons
    /// list inside is CoverChildren -- so one more item overflows the fixed frame. This bumps the
    /// panel's SuggestedHeight by one row's worth via an added Constant, editing a copy of the prefab
    /// and redirecting the loader (the same technique as SpoilsBarPrefabPatch / ItemWeightPrefabPatch),
    /// so no forked TaleWorlds file ships and a layout change degrades to "not resized" rather than a
    /// crash.
    ///
    /// EscapeMenu.xml is shared with the in-mission escape menu; that menu keeps its (unchanged) items
    /// and simply gets a slightly taller panel, which is cosmetically harmless.
    /// </summary>
    public static class RBMEscapeMenuPrefabPatch
    {
        private const string TargetPrefabFileName = "EscapeMenu.xml";
        private const string EscapeMenuMovieName = "EscapeMenu";
        private const string PanelId = "EscapeMenu";
        private const string HeightConstantName = "RBM.EscapeMenu.Background.Height";

        // One button row plus a little breathing room (button 84px scaled + 30 margin ≈ one row).
        private const int ExtraHeight = 90;

        private static string _patchedPrefabPath;
        private static bool _patchAttempted;

        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled; }
        }

        /// <summary>
        /// Must run from OnSubModuleLoad. Gauntlet parses a prefab once and caches it forever, and the
        /// escape menu can be opened before OnGameStart, so a hook installed later never sees the load.
        /// </summary>
        public static void ApplyEarly(Harmony harmony)
        {
            try
            {
                harmony.CreateClassProcessor(typeof(SkipGeneratedEscapeMenuPrefab)).Patch();
                harmony.CreateClassProcessor(typeof(RedirectEscapeMenu)).Patch();
                SpoilsLog.Trace("installed the " + TargetPrefabFileName + " load hook");
            }
            catch (Exception exception)
            {
                SpoilsLog.Trace("FAILED to install the EscapeMenu load hook: " + exception);
            }
        }

        /// <summary>
        /// The escape menu ships as a generated prefab, which GauntletMovie.Load prefers over the xml.
        /// Refusing that one lookup sends it down the xml path so the redirect below can act. Costs the
        /// escape menu its codegen fast path; it is parsed once and cached thereafter.
        /// </summary>
        [HarmonyPatch(typeof(GeneratedPrefabContext))]
        [HarmonyPatch("InstantiatePrefab")]
        private class SkipGeneratedEscapeMenuPrefab
        {
            private static bool Prefix(string prefabName, ref GeneratedPrefabInstantiationResult __result)
            {
                if (!IsEnabled || prefabName != EscapeMenuMovieName)
                {
                    return true;
                }
                __result = null;
                return false;
            }
        }

        [HarmonyPatch(typeof(WidgetPrefab))]
        [HarmonyPatch("LoadFrom")]
        private class RedirectEscapeMenu
        {
            private static void Prefix(ref string path)
            {
                if (!EndsWithFile(path, TargetPrefabFileName) || !IsEnabled)
                {
                    return;
                }
                string patched = GetPatchedPrefabPath(path);
                if (patched != null)
                {
                    path = patched;
                }
            }
        }

        private static bool EndsWithFile(string path, string fileName)
        {
            return path.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("\\" + fileName, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPatchedPrefabPath(string originalPath)
        {
            if (_patchAttempted)
            {
                return _patchedPrefabPath;
            }
            _patchAttempted = true;
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(originalPath);

                XmlElement constants = document.SelectSingleNode("/Prefab/Constants") as XmlElement;
                XmlElement panel = document.SelectSingleNode("//*[@Id='" + PanelId + "']") as XmlElement;
                if (constants == null || panel == null)
                {
                    SpoilsLog.Trace("the EscapeMenu prefab does not look as expected; not resizing it.");
                    return null;
                }

                // New height = the sprite-derived base height plus one row. Additive on a Value that
                // references another Constant is supported (ConstantDefinition.GetValue adds it), the
                // same way the inventory weight column shrinks a width constant.
                XmlElement heightConstant = document.CreateElement("Constant");
                heightConstant.SetAttribute("Name", HeightConstantName);
                heightConstant.SetAttribute("Value", "!EscapeMenu.Background.Height");
                heightConstant.SetAttribute("Additive", ExtraHeight.ToString(CultureInfo.InvariantCulture));
                constants.AppendChild(heightConstant);

                panel.SetAttribute("SuggestedHeight", "!" + HeightConstantName);

                string directory = Path.Combine(Path.GetTempPath(), "RBM", "Prefabs");
                Directory.CreateDirectory(directory);
                string patchedPath = Path.Combine(directory, TargetPrefabFileName);
                document.Save(patchedPath);
                _patchedPrefabPath = patchedPath;
                SpoilsLog.Trace("grew the EscapeMenu panel by " + ExtraHeight + "px, redirected prefab to " + patchedPath);
            }
            catch (Exception exception)
            {
                SpoilsLog.Trace("failed to resize the EscapeMenu panel: " + exception);
                _patchedPrefabPath = null;
            }
            return _patchedPrefabPath;
        }
    }
}
