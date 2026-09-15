using HarmonyLib;
using System;
using System.IO;
using System.Xml;
using TaleWorlds.GauntletUI.PrefabSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Inserts a per-man maintenance line into SandBox's PartyScreen prefab, directly under the selected
    /// troop's wage, as the prefab loads -- the same disk-path redirect the spoils bar uses for the troop
    /// tuple (see <see cref="SpoilsBarPrefabPatch"/>). That patch's SkipGeneratedPartyScreenPrefab already
    /// forces PartyScreen down the xml path, so this LoadFrom hook fires for it. If TaleWorlds ever moves
    /// the wage widget, the anchor is not found and the line is simply skipped rather than the screen
    /// breaking. Switch off with RBMConfig.rbmCampaignEnabled / the spoils economy being off.
    /// </summary>
    public static class MaintenanceLabelPrefabPatch
    {
        private const string TargetPrefabFileName = "PartyScreen.xml";
        private const string WageBinding = "@CurrentCharacterWageLbl";
        private const string CharacterInfoId = "CharacterInfo";

        private static string _patchedPrefabPath;
        private static bool _patchAttempted;

        /// <summary>
        /// Must run from OnSubModuleLoad, not with the rest of the module's patches: Gauntlet loads and
        /// caches the party screen prefab before OnGameStart, so a hook installed later never sees it.
        /// </summary>
        public static void ApplyEarly(Harmony harmony)
        {
            try
            {
                harmony.CreateClassProcessor(typeof(RedirectPartyScreen)).Patch();
                SpoilsLog.Trace("installed the " + TargetPrefabFileName + " maintenance-line load hook");
            }
            catch (Exception exception)
            {
                SpoilsLog.Trace("FAILED to install the maintenance-line load hook: " + exception);
            }
        }

        [HarmonyPatch(typeof(WidgetPrefab))]
        [HarmonyPatch("LoadFrom")]
        private class RedirectPartyScreen
        {
            private static void Prefix(ref string path)
            {
                // Match the trailing file name only; the resource depot hands out forward slashes.
                if (!path.EndsWith("/" + TargetPrefabFileName, StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith("\\" + TargetPrefabFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (!SpoilsPool.IsEnabled)
                {
                    return;
                }
                SpoilsLog.Trace("intercepted load of " + path + " for the maintenance line");
                RBMTroopMaintenanceTextWidget.RegisterWidgetType();
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

                // The wage text widget, and the horizontal row (Id="List") it sits in. The maintenance
                // line is inserted after that row, as the next child of the vertical CharacterInfo panel,
                // so it lands on its own line beneath the tier/wage row.
                XmlElement wage = document.SelectSingleNode("//*[@Text='" + WageBinding + "']") as XmlElement;
                if (wage == null)
                {
                    SpoilsLog.Trace("wage widget not found in " + TargetPrefabFileName + "; skipping the maintenance line.");
                    return null;
                }
                XmlElement row = FindAncestorListRow(wage);
                if (row == null || row.ParentNode == null)
                {
                    SpoilsLog.Trace("wage row not found in " + TargetPrefabFileName + "; skipping the maintenance line.");
                    return null;
                }
                XmlNode charInfoChildren = row.ParentNode;
                charInfoChildren.InsertAfter(CreateMaintenanceLine(document), row);

                // The CharacterInfo panel is a fixed height sized for name + one row; give it room for the
                // extra line so it is not drawn on top of what sits below the panel.
                XmlElement charInfo = document.SelectSingleNode("//*[@Id='" + CharacterInfoId + "']") as XmlElement;
                if (charInfo != null && charInfo.HasAttribute("SuggestedHeight"))
                {
                    charInfo.SetAttribute("SuggestedHeight", "100");
                }

                string directory = Path.Combine(Path.GetTempPath(), "RBM", "Prefabs");
                Directory.CreateDirectory(directory);
                string patchedPath = Path.Combine(directory, TargetPrefabFileName);
                document.Save(patchedPath);
                _patchedPrefabPath = patchedPath;
                SpoilsLog.Trace("injected maintenance line, redirected prefab to " + patchedPath);
            }
            catch (Exception exception)
            {
                SpoilsLog.Trace("failed to inject the maintenance line: " + exception);
                _patchedPrefabPath = null;
            }
            return _patchedPrefabPath;
        }

        /// <summary>
        /// The horizontal row (a ListPanel Id="List") the wage widget lives in, found by walking up from
        /// the wage. Anchored on the row rather than a fixed depth so a moved-around wage still resolves.
        /// </summary>
        private static XmlElement FindAncestorListRow(XmlElement wage)
        {
            XmlNode node = wage.ParentNode;
            while (node != null)
            {
                if (node is XmlElement element && element.Name == "ListPanel"
                    && element.GetAttribute("Id") == "List")
                {
                    return element;
                }
                node = node.ParentNode;
            }
            return null;
        }

        /// <summary>
        /// The maintenance line: a full-width centred row, shown for exactly the troops the wage is (via
        /// IsCurrentCharacterWageEnabled on the panel's own view model), holding the custom label widget
        /// bound to the selected troop.
        /// </summary>
        private static XmlElement CreateMaintenanceLine(XmlDocument document)
        {
            XmlElement container = document.CreateElement("Widget");
            container.SetAttribute("WidthSizePolicy", "StretchToParent");
            container.SetAttribute("HeightSizePolicy", "Fixed");
            container.SetAttribute("SuggestedHeight", "26");
            container.SetAttribute("HorizontalAlignment", "Center");
            container.SetAttribute("MarginTop", "2");
            container.SetAttribute("IsVisible", "@IsCurrentCharacterWageEnabled");

            XmlElement children = document.CreateElement("Children");
            XmlElement label = document.CreateElement(nameof(RBMTroopMaintenanceTextWidget));
            label.SetAttribute("DataSource", "{CurrentCharacter}");
            label.SetAttribute("WidthSizePolicy", "CoverChildren");
            label.SetAttribute("HeightSizePolicy", "StretchToParent");
            label.SetAttribute("HorizontalAlignment", "Center");
            label.SetAttribute("VerticalAlignment", "Center");
            label.SetAttribute("Brush", "Party.Text.TroopInfo");
            label.SetAttribute("Brush.TextHorizontalAlignment", "Center");
            label.SetAttribute("TroopId", "@TroopID");
            children.AppendChild(label);
            container.AppendChild(children);
            return container;
        }
    }
}
