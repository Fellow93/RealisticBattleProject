using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace RBMConfig
{
    // The "RBM Changelog" badge in the top-right corner of the title screen and the viewer it opens, as two layers
    // on the native initial screen (any MBInitialScreenBase), so nothing shows in a campaign or a mission.
    //
    // Hooked through ScreenManager.OnPushScreen / OnPopScreen, not a Harmony patch: those fire exactly once per
    // initial-screen instance (first launch and every return to the main menu create a new one) and are untouched
    // by RBM's unpatch/repatch cycles. The layers belong to that screen, so they hide while another screen (e.g.
    // RBM Configuration) sits on top and are finalized with it. Opening and closing are deferred to Tick (from
    // RBM's OnApplicationTick) so a layer is never added or released inside its own click handler.
    //
    // "What's new": the version is the first "## " header of CHANGELOG.md. While it differs from
    // RBMConfig.lastSeenChangelogVersion the badge shows its "new" dot and the viewer opens once by itself;
    // opening the viewer either way stores the version.
    public static class RBMChangelog
    {
        private const string BadgeMovie = "RBMChangelogBadge";
        private const string ViewerMovie = "RBMChangelog";

        // The native menu layer is 1 and its brightness/exposure prompts 2. The badge shares the menu's order
        // (they never overlap) so a first-run brightness prompt still covers it; the modal viewer goes above both.
        private const int BadgeLayerOrder = 1;
        private const int ViewerLayerOrder = 10;

        private static bool _installed;

        private static ScreenBase _titleScreen;
        private static GauntletLayer _badgeLayer;
        private static RBMChangelogBadgeVM _badgeVM;
        private static GauntletLayer _viewerLayer;
        private static GauntletMovieIdentifier _viewerMovie;
        private static RBMChangelogVM _viewerVM;

        private static List<ChangelogEntry> _entries = new List<ChangelogEntry>();
        private static string _loadMessage;

        private static bool _openRequested;
        private static bool _closeRequested;
        private static bool _autoOpenPending;

        public static void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;
            ScreenManager.OnPushScreen += OnPushScreen;
            ScreenManager.OnPopScreen += OnPopScreen;
        }

        public static void Tick()
        {
            if (_titleScreen == null)
            {
                return;
            }
            try
            {
                if (_titleScreen.IsFinalized)
                {
                    Detach();
                    return;
                }
                if (_viewerLayer != null)
                {
                    if (_closeRequested || _viewerLayer.Input.IsHotKeyReleased("Exit") || _viewerLayer.Input.IsKeyReleased(InputKey.Escape))
                    {
                        CloseViewer();
                    }
                    return;
                }
                if (_openRequested)
                {
                    OpenViewer();
                    return;
                }
                // Wait until the title screen is on top and no native prompt (first-run brightness/exposure
                // calibration, a module warning inquiry) is up, so the viewer never hides one of them.
                if (_autoOpenPending && ScreenManager.TopScreen == _titleScreen && _titleScreen.IsActive && !IsNativePromptOpen())
                {
                    OpenViewer();
                }
            }
            catch (Exception e)
            {
                TaleWorlds.Library.Debug.Print("[RBM] Changelog viewer failed: " + e);
                Detach();
            }
        }

        private static void OnPushScreen(ScreenBase screen)
        {
            if (!(screen is MBInitialScreenBase) || screen == _titleScreen)
            {
                return;
            }
            try
            {
                Attach(screen);
            }
            catch (Exception e)
            {
                TaleWorlds.Library.Debug.Print("[RBM] Could not add the changelog badge: " + e);
                Detach();
            }
        }

        private static void OnPopScreen(ScreenBase screen)
        {
            if (screen != null && screen == _titleScreen)
            {
                Detach();
            }
        }

        private static void Attach(ScreenBase screen)
        {
            Detach();
            _titleScreen = screen;
            LoadEntries();

            string newest = NewestVersion();
            bool unseen = newest != null && !string.Equals(newest, RBMConfig.lastSeenChangelogVersion, StringComparison.Ordinal);

            _badgeVM = new RBMChangelogBadgeVM(() => _openRequested = true) { HasUnseen = unseen };
            _badgeLayer = new GauntletLayer("RBMChangelogBadge", BadgeLayerOrder);
            _badgeLayer.LoadMovie(BadgeMovie, _badgeVM);
            // Mouse only, like the native menu layer: the badge never takes keyboard focus from the menu.
            _badgeLayer.InputRestrictions.SetInputRestrictions(isMouseVisible: true, InputUsageMask.Mouse);
            screen.AddLayer(_badgeLayer);

            _autoOpenPending = unseen;
        }

        // Removes both layers from a screen still alive (no-op for a finalized one, which already finalized
        // its layers) and forgets everything.
        private static void Detach()
        {
            ScreenBase screen = _titleScreen;
            bool live = screen != null && !screen.IsFinalized;
            if (_viewerLayer != null)
            {
                ScreenManager.TryLoseFocus(_viewerLayer);
                if (live && screen.HasLayer(_viewerLayer))
                {
                    _viewerLayer.ReleaseMovie(_viewerMovie);
                    screen.RemoveLayer(_viewerLayer);
                }
            }
            if (_badgeLayer != null && live && screen.HasLayer(_badgeLayer))
            {
                screen.RemoveLayer(_badgeLayer);
            }
            _viewerVM?.OnFinalize();
            _badgeVM?.OnFinalize();
            _titleScreen = null;
            _badgeLayer = null;
            _badgeVM = null;
            _viewerLayer = null;
            _viewerMovie = null;
            _viewerVM = null;
            _openRequested = false;
            _closeRequested = false;
            _autoOpenPending = false;
        }

        private static void OpenViewer()
        {
            _openRequested = false;
            _autoOpenPending = false;
            if (_viewerLayer != null || _titleScreen == null || _titleScreen.IsFinalized)
            {
                return;
            }
            _viewerVM = new RBMChangelogVM(_entries, _loadMessage, () => _closeRequested = true);
            _viewerLayer = new GauntletLayer("RBMChangelog", ViewerLayerOrder);
            _viewerMovie = _viewerLayer.LoadMovie(ViewerMovie, _viewerVM);
            _viewerLayer.InputRestrictions.SetInputRestrictions();
            _viewerLayer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
            _viewerLayer.IsFocusLayer = true;
            _titleScreen.AddLayer(_viewerLayer);
            ScreenManager.TrySetFocus(_viewerLayer);
            MarkNewestSeen();
        }

        private static void CloseViewer()
        {
            _closeRequested = false;
            if (_viewerLayer == null)
            {
                return;
            }
            GauntletLayer layer = _viewerLayer;
            _viewerLayer = null;
            layer.InputRestrictions.ResetInputRestrictions();
            layer.IsFocusLayer = false;
            ScreenManager.TryLoseFocus(layer);
            if (_titleScreen != null && !_titleScreen.IsFinalized && _titleScreen.HasLayer(layer))
            {
                layer.ReleaseMovie(_viewerMovie);
                _titleScreen.RemoveLayer(layer);
            }
            _viewerMovie = null;
            _viewerVM?.OnFinalize();
            _viewerVM = null;
        }

        private static bool IsNativePromptOpen()
        {
            return InformationManager.IsAnyInquiryActive()
                || _titleScreen.FindLayer<GauntletLayer>("MainMenuBrightness") != null
                || _titleScreen.FindLayer<GauntletLayer>("MainMenuExposure") != null;
        }

        private static string NewestVersion()
        {
            return _entries.Count > 0 ? _entries[0].Version : null;
        }

        private static void MarkNewestSeen()
        {
            if (_badgeVM != null)
            {
                _badgeVM.HasUnseen = false;
            }
            string newest = NewestVersion();
            if (newest != null && !string.Equals(newest, RBMConfig.lastSeenChangelogVersion, StringComparison.Ordinal))
            {
                RBMConfig.lastSeenChangelogVersion = newest;
                RBMConfig.saveXmlConfig();
            }
        }

        // Read on every title-screen push: the file is small, and a missing or broken one only costs a message.
        private static void LoadEntries()
        {
            _entries = new List<ChangelogEntry>();
            _loadMessage = null;
            string path = null;
            try
            {
                path = ChangelogParser.GetFilePath();
                if (!File.Exists(path))
                {
                    TaleWorlds.Library.Debug.Print("[RBM] Changelog not found at " + path);
                    _loadMessage = new TextObject("{=RBM_CHG_003}The changelog file was not found. It should be CHANGELOG.md in the RBM module folder.").ToString();
                    return;
                }
                _entries = ChangelogParser.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (_entries.Count == 0)
                {
                    _loadMessage = new TextObject("{=RBM_CHG_004}The changelog has no version entries.").ToString();
                }
            }
            catch (Exception e)
            {
                TaleWorlds.Library.Debug.Print("[RBM] Could not read changelog " + path + ": " + e.Message);
                _entries = new List<ChangelogEntry>();
                _loadMessage = new TextObject("{=RBM_CHG_005}The changelog could not be read.").ToString();
            }
        }
    }
}
