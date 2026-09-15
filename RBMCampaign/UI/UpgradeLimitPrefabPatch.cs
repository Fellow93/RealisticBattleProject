using HarmonyLib;
using System;
using System.IO;
using System.Xml;
using TaleWorlds.GauntletUI.PrefabSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Inserts the per-party upgrade-budget control (a slider + an "unlimited" checkbox) into SandBox's
    /// ClanPartiesRightPanel prefab, directly beneath the native party wage control it mirrors, as the
    /// prefab loads -- the same disk-path redirect the spoils bar uses (see <see cref="SpoilsBarPrefabPatch"/>).
    /// No copy of the file is shipped and nothing is overridden by load order. If TaleWorlds ever renames the
    /// wage-cap anchor, it is not found and the control is simply skipped rather than the clan screen breaking.
    /// Switches off with the spoils economy (RBMConfig.rbmCampaignEnabled / troopUpgradeCostMultiplier = 0).
    /// </summary>
    public static class UpgradeLimitPrefabPatch
    {
        private const string TargetPrefabFileName = "ClanPartiesRightPanel.xml";
        // The native wage-cap block; the upgrade-budget block is inserted as its next sibling.
        private const string WageCapAnchorId = "PartiesWageCapParent";
        private const string ClanScreenMovieName = "ClanScreen";

        private static string _patchedPrefabPath;
        private static bool _patchAttempted;

        /// <summary>
        /// Must run from OnSubModuleLoad, not with the rest of the module's patches: Gauntlet parses and
        /// caches the clan-screen prefabs before OnGameStart, so a hook installed later never sees the load.
        /// </summary>
        public static void ApplyEarly(Harmony harmony)
        {
            try
            {
                harmony.CreateClassProcessor(typeof(SkipGeneratedClanScreenPrefab)).Patch();
                harmony.CreateClassProcessor(typeof(RedirectClanPartiesRightPanel)).Patch();
                SpoilsLog.Trace("installed the " + TargetPrefabFileName + " upgrade-budget load hook");
            }
            catch (Exception exception)
            {
                SpoilsLog.Trace("FAILED to install the upgrade-budget load hook: " + exception);
            }
        }

        /// <summary>
        /// The clan screen ships as a generated prefab TaleWorlds compiled from the xml at their build time;
        /// GauntletMovie.Load prefers it and never reads the xml, so nested named prefabs (including
        /// ClanPartiesRightPanel) come from codegen too. Returning no generated prefab for this one movie
        /// sends the whole tree down the xml path so the LoadFrom hook below can fire. Costs one screen its
        /// codegen fast path. Same technique as SpoilsBarPrefabPatch's skip for PartyScreen.
        /// </summary>
        [HarmonyPatch(typeof(GeneratedPrefabContext))]
        [HarmonyPatch("InstantiatePrefab")]
        private class SkipGeneratedClanScreenPrefab
        {
            private static bool Prefix(string prefabName, ref GeneratedPrefabInstantiationResult __result)
            {
                if (!SpoilsPool.IsEnabled || prefabName != ClanScreenMovieName)
                {
                    return true;
                }
                SpoilsLog.TraceOnce("skip-generated-clan", "bypassed the generated " + ClanScreenMovieName + " prefab so the xml loads");
                __result = null;
                return false;
            }
        }

        [HarmonyPatch(typeof(WidgetPrefab))]
        [HarmonyPatch("LoadFrom")]
        private class RedirectClanPartiesRightPanel
        {
            private static void Prefix(ref string path)
            {
                if (!path.EndsWith("/" + TargetPrefabFileName, StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith("\\" + TargetPrefabFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (!SpoilsPool.IsEnabled)
                {
                    SpoilsLog.Trace("spoils are disabled in config; not injecting the upgrade-budget control into " + TargetPrefabFileName);
                    return;
                }
                SpoilsLog.Trace("intercepted load of " + path + " for the upgrade-budget control");
                UpgradeLimitWidgets.RegisterWidgetTypes();
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

                XmlElement anchor = document.SelectSingleNode("//*[@Id='" + WageCapAnchorId + "']") as XmlElement;
                if (anchor == null || anchor.ParentNode == null)
                {
                    SpoilsLog.Trace(WageCapAnchorId + " not found in " + TargetPrefabFileName + "; skipping the upgrade-budget control.");
                    return null;
                }

                XmlDocumentFragment fragment = document.CreateDocumentFragment();
                fragment.InnerXml = BlockXml;
                anchor.ParentNode.InsertAfter(fragment, anchor);

                string directory = Path.Combine(Path.GetTempPath(), "RBM", "Prefabs");
                Directory.CreateDirectory(directory);
                string patchedPath = Path.Combine(directory, TargetPrefabFileName);
                document.Save(patchedPath);
                _patchedPrefabPath = patchedPath;
                SpoilsLog.Trace("injected upgrade-budget control, redirected prefab to " + patchedPath);
            }
            catch (Exception exception)
            {
                SpoilsLog.Trace("failed to inject the upgrade-budget control: " + exception);
                _patchedPrefabPath = null;
            }
            return _patchedPrefabPath;
        }

        /// <summary>
        /// The injected block, sitting at the same view-model context as the native wage cap
        /// (<c>ClanPartyItemVM</c>, via <c>CurrentSelectedParty</c>), so <c>@Party</c> and
        /// <c>@ShouldPartyHaveExpense</c> resolve. The slider/checkbox are RBM's own widgets, which
        /// self-source the cap from the bound party; the ImageWidget children clone the native wage
        /// slider's look (SPClan.Slider.* / SPOptions.Checkbox.* brushes). The two label ids and the
        /// Filler/Handle ids are the ones the widgets look up by name.
        ///
        /// Layout mirrors the native wage block exactly: an outer (horizontally-laid-out) ListPanel with
        /// the slider column on the left and the checkbox as its RIGHT sibling. Stacking the checkbox below
        /// the slider instead pushes it off the bottom of this already-tall right panel, where it clips.
        /// </summary>
        private const string BlockXml =
            "<Widget Id=\"RBMUpgradeCapParent\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"15\" IsVisible=\"@ShouldPartyHaveExpense\">" +
            "  <Children>" +
            "    <ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\">" +
            "      <Children>" +
            "        <ListPanel WidthSizePolicy=\"Fixed\" SuggestedWidth=\"338\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" StackLayout.LayoutMethod=\"VerticalTopToBottom\" MarginLeft=\"105\" MarginRight=\"50\">" +
            "          <Children>" +
            "            <Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"78\" VerticalAlignment=\"Center\">" +
            "              <Children>" +
            "                <RBMUpgradeLimitSliderWidget Party=\"@Party\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"42\" VerticalAlignment=\"Center\" Filler=\"RBMUpgFiller\" Handle=\"RBMUpgHandle\" MinValueInt=\"0\" MaxValueInt=\"100000\" DiscreteIncrementInterval=\"500\" IsDiscrete=\"true\" DoNotPassEventsToChildren=\"true\" DoNotUpdateHandleSize=\"true\">" +
            "                  <Children>" +
            "                    <ImageWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"362\" SuggestedHeight=\"38\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"SPClan.Slider.Canvas\" />" +
            "                    <ImageWidget Id=\"RBMUpgFiller\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"345\" SuggestedHeight=\"35\" VerticalAlignment=\"Center\" Brush=\"SPClan.Slider.Fill\" ClipContents=\"true\" UpdateChildrenStates=\"true\">" +
            "                      <Children>" +
            "                        <ImageWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"345\" SuggestedHeight=\"35\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Brush=\"SPClan.Slider.Fill\" />" +
            "                      </Children>" +
            "                    </ImageWidget>" +
            "                    <ImageWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"400\" SuggestedHeight=\"65\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"SPClan.Slider.Frame\" />" +
            "                    <ImageWidget Id=\"RBMUpgHandle\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"14\" SuggestedHeight=\"38\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Brush=\"SPClan.Slider.Handle\" DoNotAcceptEvents=\"true\" />" +
            "                    <TextWidget Id=\"RBMUpgradeCapTitle\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" PositionYOffset=\"-34\" Brush=\"Kingdom.TitleSmall.Text\" Brush.TextHorizontalAlignment=\"Center\" />" +
            "                    <TextWidget Id=\"RBMUpgradeCapValue\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" PositionYOffset=\"34\" Brush=\"Clan.Party.Wage.Text\" Brush.TextHorizontalAlignment=\"Center\" />" +
            "                  </Children>" +
            "                </RBMUpgradeLimitSliderWidget>" +
            "              </Children>" +
            "            </Widget>" +
            "          </Children>" +
            "        </ListPanel>" +
            "        <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"40\" SuggestedHeight=\"40\" VerticalAlignment=\"Center\" MarginBottom=\"20\">" +
            "          <Children>" +
            "            <RBMUpgradeLimitToggleWidget Party=\"@Party\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"SPOptions.Checkbox.Empty.Button\" ButtonType=\"Toggle\" ToggleIndicator=\"ToggleIndicator\" UpdateChildrenStates=\"true\">" +
            "              <Children>" +
            "                <ImageWidget Id=\"ToggleIndicator\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"SPOptions.Checkbox.Full.Button\" />" +
            "                <TextWidget Id=\"RBMUpgradeCapUnlimitedLabel\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"120\" HorizontalAlignment=\"Center\" PositionYOffset=\"50\" Brush=\"Clan.Party.Wage.Text\" Brush.TextHorizontalAlignment=\"Center\" />" +
            "              </Children>" +
            "            </RBMUpgradeLimitToggleWidget>" +
            "          </Children>" +
            "        </Widget>" +
            "      </Children>" +
            "    </ListPanel>" +
            "  </Children>" +
            "</Widget>";
    }
}
