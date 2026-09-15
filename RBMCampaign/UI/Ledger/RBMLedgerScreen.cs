using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace RBMCampaign
{
    // Full-screen ledger/statistics screen, modelled on RBMConfig.RBMConfigScreen. Pushed onto
    // the ScreenManager stack from the campaign map via RBMLedgerHotkey (Ctrl+Shift+K); popped
    // by its close button or Escape.
    public class RBMLedgerScreen : ScreenBase
    {
        private GauntletLayer _gauntletLayer;
        private RBMLedgerViewModel _viewModel;

        // Campaign clock state to restore on close. Pushing a ScreenBase does NOT change the active
        // game state, so MapState keeps ticking the world (and NavalDLC keeps running its parallel
        // ship-visual job) behind this screen. If the clock is left running/fast-forwarding, naval
        // parties churn — created/destroyed mid-tick — which trips a lifetime race in NavalDLC.View's
        // parallel tick (AccessViolation in NavalMobilePartyVisual.UpdateEntityPosition). Freezing
        // time while the ledger is open removes that churn, matching how vanilla full-screen overlays
        // behave. Restored exactly (mode + lock) on finalize.
        private CampaignTimeControlMode _prevTimeMode;
        private bool _prevTimeLock;
        private bool _frozeTime;

        protected override void OnInitialize()
        {
            base.OnInitialize();

            if (Campaign.Current != null)
            {
                _prevTimeMode = Campaign.Current.TimeControlMode;
                _prevTimeLock = Campaign.Current.TimeControlModeLock;
                Campaign.Current.SetTimeControlModeLock(false);
                Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                Campaign.Current.SetTimeControlModeLock(true);
                _frozeTime = true;
            }

            _viewModel = new RBMLedgerViewModel();
            _gauntletLayer = new GauntletLayer("GauntletLayer", -1);
            _gauntletLayer.LoadMovie("RBMLedger", _viewModel);
            _gauntletLayer.InputRestrictions.SetInputRestrictions();
            AddLayer(_gauntletLayer);
            ScreenManager.TrySetFocus(_gauntletLayer);
        }

        protected override void OnFrameTick(float dt)
        {
            base.OnFrameTick(dt);
            if (_gauntletLayer != null && _gauntletLayer.Input.IsKeyReleased(InputKey.Escape))
            {
                ScreenManager.PopScreen();
            }
        }

        protected override void OnFinalize()
        {
            if (_frozeTime && Campaign.Current != null)
            {
                Campaign.Current.SetTimeControlModeLock(false);
                Campaign.Current.TimeControlMode = _prevTimeMode;
                Campaign.Current.SetTimeControlModeLock(_prevTimeLock);
            }
            _frozeTime = false;

            if (_gauntletLayer != null)
            {
                RemoveLayer(_gauntletLayer);
                _gauntletLayer = null;
            }
            if (_viewModel != null)
            {
                _viewModel.OnFinalize();
                _viewModel = null;
            }
            base.OnFinalize();
        }
    }
}
