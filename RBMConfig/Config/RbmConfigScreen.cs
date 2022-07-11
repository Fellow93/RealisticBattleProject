using TaleWorlds.Engine.GauntletUI;
namespace RBMConfig
{
	public class RbmConfigScreen : TaleWorlds.ScreenSystem.ScreenBase
	{
		private GauntletLayer _gauntletLayer;

		private RbmMenuViewModel _viewModel;

		protected override void OnInitialize()
		{
			base.OnInitialize();
			_viewModel = new RbmMenuViewModel();
			_gauntletLayer = new GauntletLayer(1);
			_gauntletLayer.LoadMovie("RBMConfig", _viewModel);
			_gauntletLayer.InputRestrictions.SetInputRestrictions();
			AddLayer(_gauntletLayer);
		}
	}
}