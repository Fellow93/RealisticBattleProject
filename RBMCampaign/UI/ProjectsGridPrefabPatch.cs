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
    /// The same file also halves the selected project's icon so its effect text gets the room
    /// (<see cref="ShrinkSelectedProjectIcon"/>); the two edits are independent and each skips itself if its
    /// anchor is gone.
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

        // Selected-project column: icon and the gaps around it; the freed height goes to the effect text below.
        private const int SelectedProjectIconSize = 72;        // was 193
        private const int SelectedProjectIconMarginTop = 4;    // was 18
        private const int SelectedProjectNameMarginTop = 6;    // was 15
        private const int SelectedProjectCostMarginTop = 2;    // production line under the icon, was 10

        // The whole Manage popup is this much wider, all of it given to the selected-project column.
        // The popup's 1500px width comes from its background sprite (StdAssets\standart_popup, a plain
        // stretched image, no nine-slice), so the art stretches ~9% sideways.
        private const int PopupWidth = 1640;          // was !Encyclopedia.Frame.Width = 1500
        private const int SelectedProjectExtraWidth = 140;

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

                bool changed = ReshapeProjectsGrid(document);
                changed |= ShrinkSelectedProjectIcon(document);
                changed |= LeftAlignSelectedProjectText(document);
                changed |= WidenSelectedProjectColumn(document);
                if (!changed)
                {
                    return null;
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

        /// <summary>The 7-column, shorter projects grid described on the class. False when the anchor is gone.</summary>
        private static bool ReshapeProjectsGrid(XmlDocument document)
        {
            // Grid -> <Children> -> ScrollingRect -> <Children> -> ScrollablePanel -> <Children> -> clip wrapper.
            XmlElement grid = document.SelectSingleNode("//NavigatableGridWidget[@Id='AvailableProjects']") as XmlElement;
            XmlElement wrapper = grid?.ParentNode?.ParentNode?.ParentNode?.ParentNode?.ParentNode?.ParentNode as XmlElement;
            if (wrapper == null || wrapper.GetAttribute("HeightSizePolicy") != "Fixed")
            {
                return false;
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
            return true;
        }

        /// <summary>
        /// The selected-project column (name, icon, effect text, cost) gives a 193px icon most of its height and
        /// leaves the effect text a 236px auto-scrolling window, so a building's effects scroll past slowly. The
        /// icon is cut to well under half and the gaps above and below it are tightened, and every pixel that
        /// frees goes to the text window, so the column keeps its total height. False when the anchors are gone (the column is then left vanilla).
        /// </summary>
        private static bool ShrinkSelectedProjectIcon(XmlDocument document)
        {
            XmlElement panel = document.SelectSingleNode("//Widget[@Id='CurrentProjectPanel']") as XmlElement;
            XmlElement text = panel?.ParentNode?.SelectSingleNode("CircularAutoScrollablePanelWidget") as XmlElement;
            int iconSize, iconMargin, textHeight;
            if (text == null
                || !int.TryParse(panel.GetAttribute("SuggestedHeight"), out iconSize)
                || !int.TryParse(panel.GetAttribute("MarginTop"), out iconMargin)
                || !int.TryParse(text.GetAttribute("SuggestedHeight"), out textHeight)
                || iconSize <= SelectedProjectIconSize)
            {
                return false;
            }

            string size = SelectedProjectIconSize.ToString();
            panel.SetAttribute("SuggestedWidth", size);
            panel.SetAttribute("SuggestedHeight", size);
            panel.SetAttribute("MarginTop", SelectedProjectIconMarginTop.ToString());
            foreach (XmlNode child in panel.SelectNodes("Children/*"))
            {
                XmlElement element = child as XmlElement;
                if (element != null && element.HasAttribute("SuggestedWidth"))
                {
                    element.SetAttribute("SuggestedWidth", size);
                    element.SetAttribute("SuggestedHeight", size);
                }
            }

            int freed = (iconSize - SelectedProjectIconSize) + (iconMargin - SelectedProjectIconMarginTop);
            freed += CutMarginTop(panel.ParentNode.SelectSingleNode("TextWidget[@Text='@Name']") as XmlElement, SelectedProjectNameMarginTop);
            freed += CutMarginTop(panel.ParentNode.SelectSingleNode("TextWidget[@Text='@ProductionText']") as XmlElement, SelectedProjectCostMarginTop);
            text.SetAttribute("SuggestedHeight", (textHeight + freed).ToString());
            return true;
        }

        /// <summary>
        /// Gauntlet centres text by default, which turns the selected project's effect lists into a ragged
        /// column that is hard to scan. The description and both effect blocks (now / next level, headed by
        /// <see cref="ProjectEffectHeaders"/>) are left-aligned and spaced apart; the cost line stays centred.
        /// False when none of the three text widgets is found.
        /// </summary>
        private static bool LeftAlignSelectedProjectText(XmlDocument document)
        {
            XmlElement description = document.SelectSingleNode("//TextWidget[@Text='@Explanation']") as XmlElement;
            XmlElement current = document.SelectSingleNode("//AutoHideRichTextWidget[@Text='@CurrentPositiveEffectText']") as XmlElement;
            XmlElement next = document.SelectSingleNode("//AutoHideRichTextWidget[@Text='@NextPositiveEffectText']") as XmlElement;
            bool changed = false;
            foreach (XmlElement element in new[] { description, current, next })
            {
                if (element != null)
                {
                    element.SetAttribute("Brush.TextHorizontalAlignment", "Left");
                    changed = true;
                }
            }
            if (current != null)
            {
                current.SetAttribute("MarginTop", "10");
            }
            if (next != null)
            {
                next.SetAttribute("MarginTop", "10");
            }
            return changed;
        }

        /// <summary>
        /// Widens the Manage popup by <see cref="SelectedProjectExtraWidth"/> and hands every pixel to the
        /// selected-project column and its text, so the description and effect lines wrap less. The projects
        /// grid, governor and reserve panels keep their fixed widths (the stretch areas between them absorb
        /// nothing, because the column takes exactly what the popup gained). False when an anchor is gone.
        /// </summary>
        private static bool WidenSelectedProjectColumn(XmlDocument document)
        {
            XmlElement popup = document.SelectSingleNode("//BrushWidget[@Id='TownManagementPopup']") as XmlElement;
            XmlElement column = document.SelectSingleNode("//ListPanel[@DataSource='{CurrentSelectedProject}']") as XmlElement;
            int columnWidth;
            if (popup == null || column == null || !int.TryParse(column.GetAttribute("SuggestedWidth"), out columnWidth))
            {
                return false;
            }
            popup.SetAttribute("SuggestedWidth", PopupWidth.ToString());
            column.SetAttribute("SuggestedWidth", (columnWidth + SelectedProjectExtraWidth).ToString());

            // The description, the two effect blocks: fixed 350px text boxes inside the column.
            foreach (XmlNode node in column.SelectNodes(".//*[@SuggestedWidth='350']"))
            {
                ((XmlElement)node).SetAttribute("SuggestedWidth", (350 + SelectedProjectExtraWidth).ToString());
            }
            return true;
        }

        /// <summary>Lowers an element's MarginTop to <paramref name="margin"/> and returns the pixels freed (0 if absent or already smaller).</summary>
        private static int CutMarginTop(XmlElement element, int margin)
        {
            int old;
            if (element == null || !int.TryParse(element.GetAttribute("MarginTop"), out old) || old <= margin)
            {
                return 0;
            }
            element.SetAttribute("MarginTop", margin.ToString());
            return old - margin;
        }
    }
}
