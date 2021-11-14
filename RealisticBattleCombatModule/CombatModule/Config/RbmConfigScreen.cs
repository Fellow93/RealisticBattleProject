using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Engine.Screens;

internal class RbmConfigScreen : ScreenBase
{
	private GauntletLayer _gauntletLayer;

	private RbmMenuViewModel _viewModel;

	protected override void OnInitialize()
	{
		base.OnInitialize();
		_viewModel = new RbmMenuViewModel();
		_gauntletLayer = new GauntletLayer(1);
		_gauntletLayer.LoadMovie("RBMCMConfig", _viewModel);
		_gauntletLayer.InputRestrictions.SetInputRestrictions();
		AddLayer(_gauntletLayer);
	}
}