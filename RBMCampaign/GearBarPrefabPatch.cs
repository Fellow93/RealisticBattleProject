using HarmonyLib;
using System;
using System.IO;
using System.Xml;
using TaleWorlds.GauntletUI.PrefabSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Inserts the gear bar into SandBox's PartyTroopTuple prefab as it loads, without shipping a
    /// copy of that file or overriding it by load order. WidgetPrefab.LoadFrom reads the prefab off
    /// disk by path, so patching the xml and redirecting the path leaves the engine's own loader
    /// untouched. If TaleWorlds ever renames the xp bar, the anchor is not found and the bar is
    /// simply skipped rather than the party screen breaking.
    /// </summary>
    public static class GearBarPrefabPatch
    {
        private const string TargetPrefabFileName = "PartyTroopTuple.xml";
        private const string XpBarId = "TroopXPBarWidget";
        private const string PartyScreenMovieName = "PartyScreen";

        private static string _patchedPrefabPath;
        private static bool _patchAttempted;

        /// <summary>
        /// Must run from OnSubModuleLoad, not with the rest of the module's patches: Gauntlet loads
        /// the party screen prefab before OnGameStart, and WidgetFactory caches the parsed prefab
        /// forever, so a hook installed later never sees the load at all.
        /// </summary>
        public static void ApplyEarly(Harmony harmony)
        {
            GearBarLog.Reset();
            try
            {
                harmony.CreateClassProcessor(typeof(SkipGeneratedPartyScreenPrefab)).Patch();
                harmony.CreateClassProcessor(typeof(RedirectPartyTroopTuple)).Patch();
                harmony.CreateClassProcessor(typeof(TraceCustomTypeResolution)).Patch();
                GearBarLog.Trace("installed the " + TargetPrefabFileName + " load hook");
            }
            catch (Exception exception)
            {
                GearBarLog.Trace("FAILED to install the load hook: " + exception);
            }
        }

        /// <summary>
        /// The party screen ships as a generated prefab: a C# class TaleWorlds compiled from the xml
        /// at their build time. GauntletMovie.Load prefers it and never reads PartyScreen.xml, so
        /// neither the xml on disk nor any hook on the xml loader can affect that screen. Returning
        /// no generated prefab for this one movie sends it down the xml path, which also makes its
        /// nested PartyTroopTuple load from xml. Costs one screen its codegen fast path.
        /// </summary>
        [HarmonyPatch(typeof(GeneratedPrefabContext))]
        [HarmonyPatch("InstantiatePrefab")]
        private class SkipGeneratedPartyScreenPrefab
        {
            private static bool Prefix(string prefabName, ref GeneratedPrefabInstantiationResult __result)
            {
                if (!GearPool.IsEnabled || prefabName != PartyScreenMovieName)
                {
                    return true;
                }
                GearBarLog.TraceOnce("skip-generated", "bypassed the generated " + PartyScreenMovieName + " prefab so the xml loads");
                __result = null;
                return false;
            }
        }

        /// <summary>
        /// Diagnostic only. GetCustomType runs for every prefab that is instantiated, whether or not
        /// it had to be read off disk, so it separates "the screen was never opened" from "the
        /// prefab reached the factory without passing through WidgetPrefab.LoadFrom".
        /// </summary>
        [HarmonyPatch(typeof(WidgetFactory))]
        [HarmonyPatch("GetCustomType")]
        private class TraceCustomTypeResolution
        {
            private static void Prefix(string typeName)
            {
                GearBarLog.TraceOnce("customtype-" + typeName, "GetCustomType: " + typeName);
            }
        }

        [HarmonyPatch(typeof(WidgetPrefab))]
        [HarmonyPatch("LoadFrom")]
        private class RedirectPartyTroopTuple
        {
            private static void Prefix(ref string path)
            {
                // Every prefab the game loads is recorded, so a missing PartyTroopTuple line proves
                // the hook is live but never sees that prefab, rather than being dead altogether.
                GearBarLog.Trace("LoadFrom: " + path);
                // The resource depot hands out forward slashes; match the trailing file name
                // directly so PartyTroopTupleLeft.xml, which has no xp bar, cannot be mistaken for
                // the target and burn the one-shot patch.
                if (!path.EndsWith("/" + TargetPrefabFileName, StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith("\\" + TargetPrefabFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (!GearPool.IsEnabled)
                {
                    GearBarLog.Trace("gear is disabled in config; not injecting the bar into " + TargetPrefabFileName);
                    return;
                }
                GearBarLog.Trace("intercepted load of " + path);
                // Idempotent, and the factory is certainly alive here even if it was not when the
                // module's patches were applied.
                RBMTroopGearBarWidget.RegisterWidgetType();
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

                XmlElement xpBar = document.SelectSingleNode("//*[@Id='" + XpBarId + "']") as XmlElement;
                if (xpBar == null || xpBar.ParentNode == null)
                {
                    GearBarLog.Trace(XpBarId + " not found in " + TargetPrefabFileName + "; skipping the gear bar.");
                    return null;
                }
                xpBar.ParentNode.InsertAfter(CreateGearBar(document), xpBar);

                string directory = Path.Combine(Path.GetTempPath(), "RBM", "Prefabs");
                Directory.CreateDirectory(directory);
                string patchedPath = Path.Combine(directory, TargetPrefabFileName);
                document.Save(patchedPath);
                _patchedPrefabPath = patchedPath;
                GearBarLog.Trace("injected gear bar, redirected prefab to " + patchedPath);
            }
            catch (Exception exception)
            {
                GearBarLog.Trace("failed to inject the gear bar: " + exception);
                _patchedPrefabPath = null;
            }
            return _patchedPrefabPath;
        }

        /// <summary>Mirrors the xp bar's geometry, offset left of it by its own width plus a gap.</summary>
        private static XmlElement CreateGearBar(XmlDocument document)
        {
            XmlElement bar = document.CreateElement(nameof(RBMTroopGearBarWidget));
            bar.SetAttribute("Id", "TroopGearBarWidget");
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
