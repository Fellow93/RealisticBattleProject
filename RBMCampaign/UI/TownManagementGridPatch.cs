using HarmonyLib;
using System;
using System.Globalization;
using System.IO;
using System.Xml;
using TaleWorlds.GauntletUI.PrefabSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Scales the town-management project tile (SandBox TownManagement/DevelopmentItem.xml) down ~18% so the
    /// Projects grid always shows two full rows of building icons with slack.
    ///
    /// The grid side of the change (DefaultCellWidth/Height on NavigatableGridWidget Id="AvailableProjects" and
    /// the DevelopmentItem SuggestedWidth/Height in its ItemTemplate) lives in <see cref="ProjectsGridPrefabPatch"/>,
    /// because that class already redirects TownManagement.xml and both would otherwise write the same temp file.
    /// This class owns only DevelopmentItem.xml, whose sizes are hard numbers in the prefab and therefore do not
    /// follow the shrunken cell on their own: the caption offset, the progress strip, the hammer and the level
    /// plate all have to be scaled by hand or they float off the smaller icon.
    ///
    /// DevelopmentItem.xml is used by exactly one call site (the TownManagement projects grid), verified by
    /// grepping every module's Prefabs folder, so scaling the file itself is safe and needs no extra Parameter.
    ///
    /// Same non-invasive mechanism as the other RBM prefab injections: bypass the generated prefab so the XML is
    /// really parsed, load it, mutate in memory, save to %TEMP%\RBM\Prefabs and redirect WidgetPrefab.LoadFrom.
    /// Every mutation is anchored on a node lookup and skipped when the anchor is gone, so a TaleWorlds prefab
    /// restructure degrades to vanilla layout instead of throwing.
    /// </summary>
    public static class TownManagementGridPatch
    {
        private const string TargetPrefabFileName = "DevelopmentItem.xml";
        private const string MovieName = "DevelopmentItem";

        /// <summary>Tile shrinks 110 -> 90 in the grid; every coupled offset scales by the same factor.</summary>
        private const float Scale = 90f / 110f;

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
                harmony.CreateClassProcessor(typeof(SkipGeneratedDevelopmentItemPrefab)).Patch();
                harmony.CreateClassProcessor(typeof(RedirectDevelopmentItem)).Patch();
            }
            catch
            {
                // A UI polish; never let a prefab-hook failure take the module down.
            }
        }

        [HarmonyPatch(typeof(GeneratedPrefabContext))]
        [HarmonyPatch("InstantiatePrefab")]
        private class SkipGeneratedDevelopmentItemPrefab
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
        private class RedirectDevelopmentItem
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

                XmlElement root = document.SelectSingleNode("//DevelopmentItemButtonWidget") as XmlElement;
                if (root == null)
                {
                    return null;
                }

                // Root tile box (StretchToParent in practice, but keep the fallback size consistent).
                ScaleAttributes(root, "SuggestedWidth", "SuggestedHeight");

                // Caption sits below the icon at a hard MarginTop -- the single most size-coupled value here.
                ScaleAttributes(document.SelectSingleNode("//DevelopmentNameTextWidget[@Id='DevelopmentNameTextWidget']") as XmlElement,
                    "SuggestedWidth", "MarginTop");

                // Progress strip across the top of the icon.
                ScaleAttributes(document.SelectSingleNode("//Widget[@Id='ProgressClipWidget']/Children/Widget[@Sprite='SPGeneral\\TownManagement\\progress_bar']") as XmlElement,
                    "SuggestedWidth", "SuggestedHeight", "PositionYOffset");

                // Hammer cluster (queue index + in-progress animation), including its two icon children.
                XmlElement hammer = document.SelectSingleNode("//DevelopmentQueueVisualIconWidget[@Id='HammerIconWidget']") as XmlElement;
                ScaleAttributes(hammer, "SuggestedWidth", "SuggestedHeight", "PositionYOffset");
                ScaleAttributes(document.SelectSingleNode("//Widget[@Id='QueueIconWidget']") as XmlElement, "SuggestedWidth", "SuggestedHeight");
                ScaleAttributes(document.SelectSingleNode("//BrushWidget[@Id='InProgressIconWidget']") as XmlElement, "SuggestedWidth", "SuggestedHeight");

                // Level plate and the numeral on it.
                ScaleAttributes(document.SelectSingleNode("//Widget[@Id='DevelopmentLevelVisualBackgroundWidget']") as XmlElement,
                    "SuggestedWidth", "SuggestedHeight", "PositionYOffset");
                ScaleAttributes(document.SelectSingleNode("//ImageWidget[@Id='DevelopmentLevelVisualWidget']") as XmlElement,
                    "SuggestedWidth", "SuggestedHeight");

                // Hover-overlay action buttons, so they stay inside the smaller tile.
                ScaleAttributes(document.SelectSingleNode("//ButtonWidget[@Id='AddToQueueButtonWidget']") as XmlElement,
                    "SuggestedWidth", "SuggestedHeight", "MarginLeft");
                ScaleAttributes(document.SelectSingleNode("//Widget[@Id='SetAsActiveButtonParentWidget']") as XmlElement,
                    "SuggestedWidth", "SuggestedHeight", "MarginRight");

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

        /// <summary>Multiplies the named integer attributes by <see cref="Scale"/>, ignoring absent ones.</summary>
        private static void ScaleAttributes(XmlElement element, params string[] attributeNames)
        {
            if (element == null)
            {
                return;
            }
            foreach (string name in attributeNames)
            {
                string raw = element.GetAttribute(name);
                int value;
                if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    continue;
                }
                int scaled = (int)Math.Round(value * Scale);
                // Keep sub-pixel offsets from collapsing to nothing (e.g. the -2 progress-bar nudge).
                if (scaled == 0 && value != 0)
                {
                    scaled = Math.Sign(value);
                }
                element.SetAttribute(name, scaled.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
