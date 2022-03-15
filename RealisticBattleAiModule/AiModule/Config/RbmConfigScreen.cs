using TaleWorlds.Engine.GauntletUI;

internal class RbmConfigScreen : TaleWorlds.ScreenSystem.ScreenBase
{
	private GauntletLayer _gauntletLayer;

	private RbmMenuViewModel _viewModel;

	protected override void OnInitialize()
	{
		base.OnInitialize();
		_viewModel = new RbmMenuViewModel();
		_gauntletLayer = new GauntletLayer(1);
		_gauntletLayer.LoadMovie("RBMAIMConfig", _viewModel);
		_gauntletLayer.InputRestrictions.SetInputRestrictions();
		AddLayer(_gauntletLayer);
	}
}