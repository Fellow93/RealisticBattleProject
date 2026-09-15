using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using TaleWorlds.GauntletUI.PrefabSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Adds a weight column to the inventory / trade item rows by editing SandBox's own prefabs as
    /// they load, the same way SpoilsBarPrefabPatch adds the spoils bar to the party screen: the xml
    /// is read off disk by path, so patching a copy and redirecting the path leaves the engine's
    /// loader untouched and ships no forked copy of a TaleWorlds file. If an anchor is not found the
    /// column is skipped rather than the inventory screen breaking.
    /// </summary>
    public static class ItemWeightPrefabPatch
    {
        private const string TupleFileName = "InventoryItemTuple.xml";
        private const string ListFileName = "InventoryList.xml";
        private const string InventoryMovieName = "Inventory";

        // Width of the new column, and how far its right edge sits from the right edge of a row.
        // The row already packs name / count / value edge to edge, so the column is carved out of
        // the name field: NameText.Width loses exactly this width and the column takes its place,
        // immediately left of the count.
        private const int ColumnWidth = 52;

        private static readonly Dictionary<string, string> _patchedPaths = new Dictionary<string, string>();

        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled && RBMConfig.RBMConfig.showInventoryItemWeight; }
        }

        /// <summary>
        /// Must run from OnSubModuleLoad. Gauntlet parses a prefab once and caches it forever, so a
        /// hook installed at OnGameStart never sees the load.
        /// </summary>
        public static void ApplyEarly(Harmony harmony)
        {
            try
            {
                harmony.CreateClassProcessor(typeof(SkipGeneratedInventoryPrefab)).Patch();
                harmony.CreateClassProcessor(typeof(RedirectInventoryPrefabs)).Patch();
                SpoilsLog.Trace("installed the inventory weight column load hook");
            }
            catch (Exception exception)
            {
                SpoilsLog.Trace("FAILED to install the inventory weight column hook: " + exception);
            }
        }

        /// <summary>
        /// The inventory screen ships as a generated prefab -- a C# class TaleWorlds compiled from
        /// the xml at their build time -- and GauntletMovie.Load prefers it over the xml. Only the
        /// top-level movie is ever looked up there (GauntletMovie.Load is the sole caller of
        /// GeneratedPrefabContext.InstantiatePrefab), so refusing that one lookup sends the whole
        /// tree, InventoryList and InventoryItemTuple included, down the xml path. Costs this one
        /// screen its codegen fast path.
        /// </summary>
        [HarmonyPatch(typeof(GeneratedPrefabContext))]
        [HarmonyPatch("InstantiatePrefab")]
        private class SkipGeneratedInventoryPrefab
        {
            private static bool Prefix(string prefabName, ref GeneratedPrefabInstantiationResult __result)
            {
                if (!IsEnabled || prefabName != InventoryMovieName)
                {
                    return true;
                }
                SpoilsLog.TraceOnce("skip-generated-inventory", "bypassed the generated " + InventoryMovieName + " prefab so the xml loads");
                __result = null;
                return false;
            }
        }

        [HarmonyPatch(typeof(WidgetPrefab))]
        [HarmonyPatch("LoadFrom")]
        private class RedirectInventoryPrefabs
        {
            private static void Prefix(ref string path)
            {
                string fileName = MatchTarget(path);
                if (fileName == null || !IsEnabled)
                {
                    return;
                }
                // Idempotent, and the factory is certainly alive by the time a prefab is loading.
                RBMItemWeightTextWidget.RegisterWidgetType();
                string patched = GetPatchedPrefabPath(fileName, path);
                if (patched != null)
                {
                    path = patched;
                }
            }
        }

        private static string MatchTarget(string path)
        {
            if (EndsWithFile(path, TupleFileName))
            {
                return TupleFileName;
            }
            if (EndsWithFile(path, ListFileName))
            {
                return ListFileName;
            }
            return null;
        }

        private static bool EndsWithFile(string path, string fileName)
        {
            return path.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("\\" + fileName, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPatchedPrefabPath(string fileName, string originalPath)
        {
            string cached;
            if (_patchedPaths.TryGetValue(fileName, out cached))
            {
                return cached;
            }
            _patchedPaths[fileName] = null;
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(originalPath);

                bool patched = (fileName == TupleFileName) ? PatchTuple(document) : PatchListHeader(document);
                if (!patched)
                {
                    return null;
                }

                string directory = Path.Combine(Path.GetTempPath(), "RBM", "Prefabs");
                Directory.CreateDirectory(directory);
                string patchedPath = Path.Combine(directory, fileName);
                document.Save(patchedPath);
                _patchedPaths[fileName] = patchedPath;
                SpoilsLog.Trace("injected the weight column, redirected " + fileName + " to " + patchedPath);
            }
            catch (Exception exception)
            {
                SpoilsLog.Trace("failed to inject the weight column into " + fileName + ": " + exception);
                _patchedPaths[fileName] = null;
            }
            return _patchedPaths[fileName];
        }

        /// <summary>
        /// Carves the column out of the name field and drops a weight cell into it, left of the
        /// count. The two margins mirror the native CountText ones: the column's right edge sits
        /// exactly where the count column's left edge is.
        /// </summary>
        private static bool PatchTuple(XmlDocument document)
        {
            XmlElement constants = document.SelectSingleNode("/Prefab/Constants") as XmlElement;
            XmlElement nameWidth = document.SelectSingleNode("/Prefab/Constants/Constant[@Name='NameText.Width']") as XmlElement;
            XmlElement countMargin = document.SelectSingleNode("/Prefab/Constants/Constant[@Name='CountText.Margin']") as XmlElement;
            XmlElement countWidth = document.SelectSingleNode("/Prefab/Constants/Constant[@Name='CountText.Width']") as XmlElement;
            XmlElement countParent = document.SelectSingleNode("//*[@Id='CountTextParent']") as XmlElement;
            if (constants == null || nameWidth == null || countMargin == null || countWidth == null
                || countParent == null || countParent.ParentNode == null)
            {
                SpoilsLog.Trace("the inventory tuple prefab does not look as expected; skipping the weight column.");
                return false;
            }

            // The name field pays for the column, on both the player and the other side.
            if (!Shrink(nameWidth, "OnTrue") || !Shrink(nameWidth, "OnFalse"))
            {
                return false;
            }

            // Right edge of the new column = left edge of the count column.
            int marginTrue, marginFalse;
            if (!TryOffset(countMargin, countWidth, "OnTrue", out marginTrue)
                || !TryOffset(countMargin, countWidth, "OnFalse", out marginFalse))
            {
                return false;
            }

            constants.AppendChild(MakeConstant(document, "RBMWeightText.Width", ColumnWidth.ToString(CultureInfo.InvariantCulture)));
            XmlElement marginConstant = document.CreateElement("Constant");
            marginConstant.SetAttribute("Name", "RBMWeightText.Margin");
            marginConstant.SetAttribute("BooleanCheck", "*IsPlayerItem");
            marginConstant.SetAttribute("OnTrue", marginTrue.ToString(CultureInfo.InvariantCulture));
            marginConstant.SetAttribute("OnFalse", marginFalse.ToString(CultureInfo.InvariantCulture));
            constants.AppendChild(marginConstant);

            XmlElement parent = document.CreateElement("Widget");
            parent.SetAttribute("Id", "RBMWeightTextParent");
            parent.SetAttribute("WidthSizePolicy", "Fixed");
            parent.SetAttribute("HeightSizePolicy", "StretchToParent");
            parent.SetAttribute("SuggestedWidth", "!RBMWeightText.Width");
            parent.SetAttribute("HorizontalAlignment", "Right");
            parent.SetAttribute("PositionYOffset", "!TextYFix");
            parent.SetAttribute("MarginRight", "!RBMWeightText.Margin");
            parent.SetAttribute("DoNotAcceptEvents", "true");

            XmlElement children = document.CreateElement("Children");
            XmlElement text = document.CreateElement(nameof(RBMItemWeightTextWidget));
            text.SetAttribute("Id", "RBMWeightText");
            text.SetAttribute("WidthSizePolicy", "StretchToParent");
            text.SetAttribute("HeightSizePolicy", "StretchToParent");
            text.SetAttribute("DoNotAcceptEvents", "true");
            text.SetAttribute("Brush", "InventoryDefaultFontBrush");
            text.SetAttribute("Brush.FontSize", "16");
            text.SetAttribute("Brush.FontColor", "#B5A98FFF");
            text.SetAttribute("Brush.TextHorizontalAlignment", "Center");
            text.SetAttribute("ItemId", "@StringId");
            // The count and value cells hide themselves for an untradeable row; the weight follows.
            text.SetAttribute("IsVisible", "@IsTransferable");
            children.AppendChild(text);
            parent.AppendChild(children);

            countParent.ParentNode.InsertBefore(parent, countParent);
            return true;
        }

        /// <summary>
        /// Labels the column in the list header. The header is a ListPanel whose four sort buttons
        /// tile it left to right (StackLayout.LayoutMethod is absent, and LayoutMethod is an
        /// auto-property whose default is HorizontalLeftToRight -- neither side of the screen
        /// overrides it), so the label is laid out by the panel rather than floated over it: the
        /// name button gives up exactly the width the name field gave up in the row, and the label
        /// takes its place directly after it. Everything downstream -- the count and value buttons --
        /// keeps the position it had, which is precisely what happened to the row's count and value
        /// cells, so the two stay in step without a single hand-derived pixel offset.
        /// </summary>
        private static bool PatchListHeader(XmlDocument document)
        {
            XmlElement headers = document.SelectSingleNode("//*[@Id='Headers']") as XmlElement;
            XmlElement constants = document.SelectSingleNode("/Prefab/Constants") as XmlElement;
            // A prefab node's children hang off an intermediate <Children> element.
            XmlElement headerChildren = (headers == null) ? null : headers["Children"];
            XmlElement nameSort = FindSortButton(headerChildren, "ExecuteSortByName");
            if (headers == null || constants == null || headerChildren == null || nameSort == null)
            {
                SpoilsLog.Trace("the inventory list prefab does not look as expected; skipping the weight header.");
                return false;
            }

            // Negative Additive is supported: ConstantDefinition.GetValue converts it with
            // Convert.ToInt32 and adds it, so a shrink is expressed the same way the file's own
            // SidePanel.Width expresses a grow.
            XmlElement nameWidth = document.CreateElement("Constant");
            nameWidth.SetAttribute("Name", "RBMNameSort.Width");
            nameWidth.SetAttribute("Value", "!Inventory.SidePanel.NameSort.Width");
            nameWidth.SetAttribute("Additive", (-ColumnWidth).ToString(CultureInfo.InvariantCulture));
            constants.AppendChild(nameWidth);
            constants.AppendChild(MakeConstant(document, "RBMWeightHeader.Width", ColumnWidth.ToString(CultureInfo.InvariantCulture)));

            nameSort.SetAttribute("SuggestedWidth", "!RBMNameSort.Width");

            XmlElement label = document.CreateElement(nameof(RBMItemWeightTextWidget));
            label.SetAttribute("Id", "RBMWeightHeaderText");
            label.SetAttribute("IsHeader", "true");
            label.SetAttribute("DoNotAcceptEvents", "true");
            label.SetAttribute("DoNotAcceptNavigation", "true");
            label.SetAttribute("WidthSizePolicy", "Fixed");
            label.SetAttribute("HeightSizePolicy", "Fixed");
            label.SetAttribute("SuggestedWidth", "!RBMWeightHeader.Width");
            label.SetAttribute("SuggestedHeight", "!Inventory.SidePanel.NameSort.Height");
            label.SetAttribute("VerticalAlignment", "Center");
            label.SetAttribute("PositionYOffset", "!Inventory.SidePanel.SortTextYOffset");
            label.SetAttribute("Brush", "InventoryDefaultFontBrush");
            label.SetAttribute("Brush.TextHorizontalAlignment", "Center");
            label.SetAttribute("Brush.TextVerticalAlignment", "Center");

            headerChildren.InsertAfter(label, nameSort);
            return true;
        }

        /// <summary>
        /// The sort buttons carry no Id, so they are told apart by the command they fire. Matched by
        /// hand rather than by XPath because the attribute name contains a dot.
        /// </summary>
        private static XmlElement FindSortButton(XmlElement headerChildren, string command)
        {
            if (headerChildren == null)
            {
                return null;
            }
            foreach (XmlNode child in headerChildren.ChildNodes)
            {
                XmlElement element = child as XmlElement;
                if (element != null && element.GetAttribute("Command.Click") == command)
                {
                    return element;
                }
            }
            return null;
        }

        private static XmlElement MakeConstant(XmlDocument document, string name, string value)
        {
            XmlElement constant = document.CreateElement("Constant");
            constant.SetAttribute("Name", name);
            constant.SetAttribute("Value", value);
            return constant;
        }

        private static bool Shrink(XmlElement constant, string attribute)
        {
            int value;
            if (!int.TryParse(constant.GetAttribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                SpoilsLog.Trace("could not read " + constant.GetAttribute("Name") + "/" + attribute + "; skipping the weight column.");
                return false;
            }
            constant.SetAttribute(attribute, (value - ColumnWidth).ToString(CultureInfo.InvariantCulture));
            return true;
        }

        private static bool TryOffset(XmlElement marginConstant, XmlElement widthConstant, string attribute, out int result)
        {
            result = 0;
            int margin, width;
            if (!int.TryParse(marginConstant.GetAttribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out margin)
                || !int.TryParse(widthConstant.GetAttribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out width))
            {
                SpoilsLog.Trace("could not read the count column geometry; skipping the weight column.");
                return false;
            }
            result = margin + width;
            return true;
        }
    }
}
