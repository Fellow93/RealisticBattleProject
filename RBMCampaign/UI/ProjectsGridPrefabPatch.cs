using HarmonyLib;
using System;
using System.IO;
using System.Xml;
using TaleWorlds.GauntletUI.PrefabSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Reshapes the town-management "Projects" grid so every building is visible without scrolling AND the
    /// Daily Defaults row below it still fits inside the Manage dialog.
    ///
    /// The native prefab is a 6-column grid of 160x140 cells clipped to 290px, i.e. exactly two rows — the 12
    /// vanilla town buildings. War Sails adds a 13th (the shipyard), which lands alone on a third row below the
    /// clip rect behind an auto-hidden scrollbar, so it looks like it is missing from the screen. Rather than
    /// growing the viewport to three rows (which pushed the Daily Defaults row off the bottom of the dialog),
    /// RBM shrinks the tiles ~18% and widens the grid to 7 columns: 7 x 135 = 945 still fits the 950px
    /// ScrollingRect, so 13 (and up to 14) buildings fit two rows, and the viewport can drop to 250px —
    /// 40px SHORTER than vanilla, giving the rest of the dialog more room than it had before.
    ///
    /// Done the same non-invasive way as the other RBM prefab injections (see <see cref="SpoilsBarPrefabPatch"/>):
    /// hook WidgetPrefab.LoadFrom, edit the xml in memory, save a copy to temp and redirect the path. If TaleWorlds
    /// restructures the prefab the anchor is not found and the screen is left untouched. The generated
    /// "TownManagement" movie is bypassed so the screen loads from xml where this hook can fire; bypassing a movie
    /// with no generated prefab is a no-op.
    /// </summary>
    public static class ProjectsGridPrefabPatch
    {
        private const string TargetPrefabFileName = "TownManagement.xml";
        private const string MovieName = "TownManagement";

        // 10px MarginTop on the grid + two 115px rows = 240, plus 10px slack. Was 290 in vanilla.
        private const string ViewportHeight = "250";

        // Building tiles shrunk ~18% so two full rows fit with room to spare.
        // Sized in lockstep with TownManagementGridPatch, which scales DevelopmentItem.xml by the same factor.
        private const string CellWidth = "135";   // was 160; 7 x 135 = 945 <= the 950px ScrollingRect
        private const string CellHeight = "115";  // was 140
        private const string ItemSize = "90";     // was 110
        private const string ColumnCount = "7";   // was 6; 13-14 buildings now fit two rows

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
                harmony.CreateClassProcessor(typeof(SkipGeneratedTownManagementPrefab)).Patch();
                harmony.CreateClassProcessor(typeof(RedirectTownManagement)).Patch();
            }
            catch
            {
                // A UI polish; never let a prefab-hook failure take the module down.
            }
        }

        [HarmonyPatch(typeof(GeneratedPrefabContext))]
        [HarmonyPatch("InstantiatePrefab")]
        private class SkipGeneratedTownManagementPrefab
        {
            private static bool Prefix(string prefabName, ref GeneratedPrefabInstantiationResult __result)
            {
                if (prefabName != MovieName)
                {
                    return true;
                }
                __result = null;
                return false;
            }
        }

        [HarmonyPatch(typeof(WidgetPrefab))]
        [HarmonyPatch("LoadFrom")]
        private class RedirectTownManagement
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

                // Grid -> <Children> -> ScrollingRect -> <Children> -> ScrollablePanel -> <Children> -> clip wrapper.
                XmlElement grid = document.SelectSingleNode("//NavigatableGridWidget[@Id='AvailableProjects']") as XmlElement;
                XmlElement wrapper = grid?.ParentNode?.ParentNode?.ParentNode?.ParentNode?.ParentNode?.ParentNode as XmlElement;
                if (wrapper == null || wrapper.GetAttribute("HeightSizePolicy") != "Fixed")
                {
                    return null;
                }

                wrapper.SetAttribute("SuggestedHeight", ViewportHeight);

                // Shrink the grid cells and the tile inside them, and widen to 7 columns. Every DevelopmentItem
                // visual is StretchToParent / derived from Size, so the art follows without any hard-coded clamp.
                grid.SetAttribute("DefaultCellWidth", CellWidth);
                grid.SetAttribute("DefaultCellHeight", CellHeight);
                grid.SetAttribute("ColumnCount", ColumnCount);

                // Gamepad navigation steps a whole row at a time, so it has to track the column count. Anchored on
                // the projects scope specifically; the Daily Defaults list has its own targeter and is untouched.
                XmlElement targeter = document.SelectSingleNode("//NavigationScopeTargeter[@ScopeID='AvailableProjectsScope']") as XmlElement;
                if (targeter != null && targeter.HasAttribute("AlternateMovementStepSize"))
                {
                    targeter.SetAttribute("AlternateMovementStepSize", ColumnCount);
                }

                XmlElement item = grid.SelectSingleNode(".//DevelopmentItem[@Id='DevelopmentItem']") as XmlElement;
                if (item != null)
                {
                    item.SetAttribute("SuggestedWidth", ItemSize);
                    item.SetAttribute("SuggestedHeight", ItemSize);
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
