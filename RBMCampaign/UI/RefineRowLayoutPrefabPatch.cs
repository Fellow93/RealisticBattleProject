using HarmonyLib;
using System;
using System.IO;
using System.Xml;
using TaleWorlds.GauntletUI.PrefabSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Makes the refine-screen rows size their input/output cluster to content and centre it, so the extra
    /// silver input tile that <see cref="ThamaskeneSilverRow"/> adds to the Thamaskene row no longer overflows
    /// the row's left edge.
    ///
    /// The native RefinementCategory prefab lays each row out as two StretchToParent panels (inputs | outputs)
    /// that split the fixed row width down the middle. That budgets room for two input tiles; a third pushes the
    /// leftmost tile off the panel. Switching the input panel, the output panel and their wrapper to CoverChildren
    /// lets the whole [inputs][arrow][output] cluster shrink-wrap and centre inside the fixed-width row, which fits
    /// two OR three tiles without clipping.
    ///
    /// Done the same non-invasive way as the other RBM prefab injections (see <see cref="SpoilsBarPrefabPatch"/>):
    /// hook WidgetPrefab.LoadFrom, edit the xml in memory, save a copy to temp and redirect the path — no shipped
    /// override, and if TaleWorlds restructures the prefab the anchors are simply not found and the row is left
    /// untouched. The crafting screen ships as a generated prefab, so (as with the party screen) the generated
    /// "Crafting" movie is bypassed to send it down the xml path where the sub-prefab load is interceptable;
    /// bypassing a movie that has no generated prefab is a no-op, so this is safe either way.
    /// </summary>
    public static class RefineRowLayoutPrefabPatch
    {
        private const string TargetPrefabFileName = "RefinementCategory.xml";
        private const string CraftingMovieName = "Crafting";

        private static string _patchedPrefabPath;
        private static bool _patchAttempted;

        /// <summary>
        /// Installed from OnSubModuleLoad, like the other prefab injections: Gauntlet caches parsed prefabs, so a
        /// hook added later can miss the load entirely.
        /// </summary>
        public static void ApplyEarly(Harmony harmony)
        {
            try
            {
                harmony.CreateClassProcessor(typeof(SkipGeneratedCraftingPrefab)).Patch();
                harmony.CreateClassProcessor(typeof(RedirectRefinementCategory)).Patch();
            }
            catch
            {
                // A UI polish; never let a prefab-hook failure take the module down.
            }
        }

        [HarmonyPatch(typeof(GeneratedPrefabContext))]
        [HarmonyPatch("InstantiatePrefab")]
        private class SkipGeneratedCraftingPrefab
        {
            private static bool Prefix(string prefabName, ref GeneratedPrefabInstantiationResult __result)
            {
                if (prefabName != CraftingMovieName)
                {
                    return true;
                }
                // No generated prefab -> load "Crafting" (and its RefinementCategory sub-prefab) from xml.
                __result = null;
                return false;
            }
        }

        [HarmonyPatch(typeof(WidgetPrefab))]
        [HarmonyPatch("LoadFrom")]
        private class RedirectRefinementCategory
        {
            private static void Prefix(ref string path)
            {
                if (!path.EndsWith("/" + TargetPrefabFileName, StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith("\\" + TargetPrefabFileName, StringComparison.OrdinalIgnoreCase))
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

                XmlElement input = document.SelectSingleNode("//ListPanel[@DataSource=\"{InputMaterials}\"]") as XmlElement;
                XmlElement output = document.SelectSingleNode("//ListPanel[@DataSource=\"{OutputMaterials}\"]") as XmlElement;
                if (input == null || output == null)
                {
                    return null;
                }

                input.SetAttribute("WidthSizePolicy", "CoverChildren");
                output.SetAttribute("WidthSizePolicy", "CoverChildren");
                // Wrapper = input panel's grandparent (input -> <Children> -> wrapper ListPanel). Centre it so the
                // shrink-wrapped cluster sits in the middle of the fixed-width row instead of pinning to one side.
                XmlElement wrapper = input.ParentNode?.ParentNode as XmlElement;
                if (wrapper != null)
                {
                    wrapper.SetAttribute("WidthSizePolicy", "CoverChildren");
                    wrapper.SetAttribute("HorizontalAlignment", "Center");
                }

                string directory = Path.Combine(Path.GetTempPath(), "RBM", "Prefabs");
                Directory.CreateDirectory(directory);
                string patchedPath = Path.Combine(directory, TargetPrefabFileName);
                document.Save(patchedPath);
                _patchedPrefabPath = patchedPath;
            }
            catch
            {
                _patchedPrefabPath = null;
            }
            return _patchedPrefabPath;
        }
    }
}
