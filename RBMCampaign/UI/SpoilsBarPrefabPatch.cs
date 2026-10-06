using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.GauntletUI.PrefabSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Inserts the spoils bar into SandBox's PartyTroopTuple prefab as it loads, without shipping a
    /// copy of that file or overriding it by load order. WidgetPrefab.LoadFrom reads the prefab off
    /// disk by path, so patching the xml and redirecting the path leaves the engine's own loader
    /// untouched. If TaleWorlds ever renames the xp bar, the anchor is not found and the bar is
    /// simply skipped rather than the party screen breaking.
    ///
    /// The same rewrite also adds the troop hover tooltip (<see cref="RBMTroopHoverTooltipWidget"/>) to the
    /// right-hand rows (PartyTroopTuple.xml) and, through the same hook, the left-hand rows
    /// (PartyTroopTupleLeft.xml, which gets no spoils bar). Each addition has its own gate: the spoils bar
    /// <see cref="SpoilsPool.IsEnabled"/>, the hover <see cref="RBMTroopHoverTooltipWidget.IsFeatureEnabled"/>
    /// (campaign module + troop perks setting); the party screen is sent down the xml path when either is on.
    /// </summary>
    public static class SpoilsBarPrefabPatch
    {
        private const string TargetPrefabFileName = "PartyTroopTuple.xml";
        private const string LeftPrefabFileName = "PartyTroopTupleLeft.xml";
        private const string XpBarId = "TroopXPBarWidget";
        private const string RowRootId = "PartyTroopTuple";
        private const string PartyScreenMovieName = "PartyScreen";

        // One patched copy per prefab file, decided on its first load (Gauntlet caches the parsed prefab after it).
        private static readonly Dictionary<string, string> _patchedPrefabPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether the party screen needs its xml loaded at all: some row addition is switched on.</summary>
        private static bool AnyRowInjectionEnabled
        {
            get { return SpoilsPool.IsEnabled || RBMTroopHoverTooltipWidget.IsFeatureEnabled; }
        }

        /// <summary>
        /// Must run from OnSubModuleLoad, not with the rest of the module's patches: Gauntlet loads
        /// the party screen prefab before OnGameStart, and WidgetFactory caches the parsed prefab
        /// forever, so a hook installed later never sees the load at all.
        /// </summary>
        public static void ApplyEarly(Harmony harmony)
        {
            // Do not open the log here: this runs at module load, before any campaign. The early
            // traces below buffer in SpoilsLog until StartCampaignLog opens the campaign log and
            // flushes them in, so a session produces one file rather than a near-empty one plus it.
            try
            {
                harmony.CreateClassProcessor(typeof(SkipGeneratedPartyScreenPrefab)).Patch();
                harmony.CreateClassProcessor(typeof(RedirectPartyTroopTuple)).Patch();
                SpoilsLog.Trace("installed the " + TargetPrefabFileName + " load hook");
            }
            catch (Exception exception)
            {
                SpoilsLog.Trace("FAILED to install the load hook: " + exception);
            }
        }

        /// <summary>
        /// The party screen ships as a generated prefab: a C# class TaleWorlds compiled from the xml
        /// at their build time. GauntletMovie.Load prefers it and never reads PartyScreen.xml, so
        /// neither the xml on disk nor any hook on the xml loader can affect that screen. Returning
        /// no generated prefab for this one movie sends it down the xml path, which also makes its
        /// nested PartyTroopTuple load from xml. Costs one screen its codegen fast path. Needed whenever the
        /// spoils bar or the troop hover is on; MaintenanceLabelPrefabPatch relies on it too and gates itself.
        /// </summary>
        [HarmonyPatch(typeof(GeneratedPrefabContext))]
        [HarmonyPatch("InstantiatePrefab")]
        private class SkipGeneratedPartyScreenPrefab
        {
            private static bool Prefix(string prefabName, ref GeneratedPrefabInstantiationResult __result)
            {
                if (prefabName != PartyScreenMovieName || !AnyRowInjectionEnabled)
                {
                    return true;
                }
                SpoilsLog.TraceOnce("skip-generated", "bypassed the generated " + PartyScreenMovieName + " prefab so the xml loads");
                __result = null;
                return false;
            }
        }

        [HarmonyPatch(typeof(WidgetPrefab))]
        [HarmonyPatch("LoadFrom")]
        private class RedirectPartyTroopTuple
        {
            private static void Prefix(ref string path)
            {
                // The resource depot hands out forward slashes; match the trailing file name
                // directly so PartyTroopTupleLeft.xml, which has no xp bar, cannot be mistaken for
                // the right-hand target (and vice versa).
                string fileName;
                if (EndsWithFile(path, TargetPrefabFileName))
                {
                    fileName = TargetPrefabFileName;
                }
                else if (EndsWithFile(path, LeftPrefabFileName))
                {
                    fileName = LeftPrefabFileName;
                }
                else
                {
                    return;
                }
                // The spoils bar only ever goes on the right-hand (own party) rows.
                bool injectSpoilsBar = fileName == TargetPrefabFileName && SpoilsPool.IsEnabled;
                bool injectHover = RBMTroopHoverTooltipWidget.IsFeatureEnabled;
                if (!injectSpoilsBar && !injectHover)
                {
                    SpoilsLog.Trace("spoils bar and troop hover are disabled in config; not patching " + fileName);
                    return;
                }
                SpoilsLog.Trace("intercepted load of " + path);
                // Idempotent, and the factory is certainly alive here even if it was not when the
                // module's patches were applied.
                if (injectSpoilsBar)
                {
                    RBMTroopSpoilsBarWidget.RegisterWidgetType();
                }
                if (injectHover)
                {
                    RBMTroopHoverTooltipWidget.RegisterWidgetType();
                }
                string patched = GetPatchedPrefabPath(path, fileName, injectSpoilsBar, injectHover);
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

        private static string GetPatchedPrefabPath(string originalPath, string fileName, bool injectSpoilsBar, bool injectHover)
        {
            string cached;
            if (_patchedPrefabPaths.TryGetValue(fileName, out cached))
            {
                return cached;
            }
            _patchedPrefabPaths[fileName] = null;
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(originalPath);
                bool changed = false;

                if (injectSpoilsBar)
                {
                    XmlElement xpBar = document.SelectSingleNode("//*[@Id='" + XpBarId + "']") as XmlElement;
                    if (xpBar == null || xpBar.ParentNode == null)
                    {
                        SpoilsLog.Trace(XpBarId + " not found in " + fileName + "; skipping the spoils bar.");
                    }
                    else
                    {
                        xpBar.ParentNode.InsertAfter(CreateSpoilsBar(document), xpBar);
                        changed = true;
                        SpoilsLog.Trace("injected spoils bar into " + fileName);
                    }
                }

                if (injectHover)
                {
                    // A direct child of the row's root button: the hover widget listens to its parent's events.
                    XmlElement rowRoot = document.SelectSingleNode("//*[@Id='" + RowRootId + "']") as XmlElement;
                    XmlElement rowChildren = rowRoot?["Children"];
                    if (rowChildren == null)
                    {
                        SpoilsLog.Trace(RowRootId + " (or its Children) not found in " + fileName + "; skipping the troop hover.");
                    }
                    else
                    {
                        rowChildren.PrependChild(CreateTroopHover(document));
                        changed = true;
                        SpoilsLog.Trace("injected troop hover into " + fileName);
                    }
                }

                if (!changed)
                {
                    return null;
                }
                string directory = Path.Combine(Path.GetTempPath(), "RBM", "Prefabs");
                Directory.CreateDirectory(directory);
                string patchedPath = Path.Combine(directory, fileName);
                document.Save(patchedPath);
                _patchedPrefabPaths[fileName] = patchedPath;
                SpoilsLog.Trace("redirected " + fileName + " to " + patchedPath);
            }
            catch (Exception exception)
            {
                SpoilsLog.Trace("failed to patch " + fileName + ": " + exception);
                _patchedPrefabPaths[fileName] = null;
            }
            return _patchedPrefabPaths[fileName];
        }

        /// <summary>
        /// Zero-sized, disabled and event-transparent: it only listens to the row button's hover events, so
        /// it adds nothing to the row's layout, clicks or drag and drop.
        /// </summary>
        private static XmlElement CreateTroopHover(XmlDocument document)
        {
            XmlElement hover = document.CreateElement(nameof(RBMTroopHoverTooltipWidget));
            hover.SetAttribute("Id", "RBMTroopHoverTooltip");
            hover.SetAttribute("WidthSizePolicy", "Fixed");
            hover.SetAttribute("HeightSizePolicy", "Fixed");
            hover.SetAttribute("SuggestedWidth", "0");
            hover.SetAttribute("SuggestedHeight", "0");
            hover.SetAttribute("DoNotAcceptEvents", "true");
            hover.SetAttribute("DoNotPassEventsToChildren", "true");
            hover.SetAttribute("IsDisabled", "true");
            hover.SetAttribute("TroopId", "@TroopID");
            return hover;
        }

        /// <summary>Mirrors the xp bar's geometry, offset left of it by its own width plus a gap.</summary>
        private static XmlElement CreateSpoilsBar(XmlDocument document)
        {
            XmlElement bar = document.CreateElement(nameof(RBMTroopSpoilsBarWidget));
            bar.SetAttribute("Id", "TroopSpoilsBarWidget");
            bar.SetAttribute("DoNotPassEventsToChildren", "true");
            bar.SetAttribute("WidthSizePolicy", "Fixed");
            bar.SetAttribute("HeightSizePolicy", "Fixed");
            bar.SetAttribute("SuggestedWidth", "10");
            bar.SetAttribute("SuggestedHeight", "40");
            bar.SetAttribute("HorizontalAlignment", "Right");
            bar.SetAttribute("VerticalAlignment", "Bottom");
            bar.SetAttribute("MarginTop", "5");
            bar.SetAttribute("MarginBottom", "20");
            bar.SetAttribute("MarginRight", "14");
            bar.SetAttribute("Sprite", "BlankWhiteSquare_9");
            bar.SetAttribute("Color", "#00000066");
            bar.SetAttribute("FillWidget", "FillWidget");
            bar.SetAttribute("IsDirectionUpward", "true");
            bar.SetAttribute("IsTroopUpgradable", "@IsUpgradableTroop");
            bar.SetAttribute("IsPrisoner", "@IsPrisonerOfPlayer");
            bar.SetAttribute("TroopId", "@TroopID");

            XmlElement children = document.CreateElement("Children");
            XmlElement fill = document.CreateElement("Widget");
            fill.SetAttribute("Id", "FillWidget");
            fill.SetAttribute("WidthSizePolicy", "StretchToParent");
            fill.SetAttribute("HeightSizePolicy", "Fixed");
            fill.SetAttribute("Sprite", "BlankWhiteSquare_9");
            // Steel, against the xp bar's gold.
            fill.SetAttribute("Color", "#8CA3B8FF");
            children.AppendChild(fill);
            bar.AppendChild(children);
            return bar;
        }
    }
}
