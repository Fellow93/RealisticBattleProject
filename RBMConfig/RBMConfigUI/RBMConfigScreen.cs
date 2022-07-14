using TaleWorlds.Engine.GauntletUI;
namespace RBMConfig
{
	public class RBMConfigScreen : TaleWorlds.ScreenSystem.ScreenBase
	{
		private GauntletLayer _gauntletLayer;

		private RBMConfigViewModel _viewModel;

		protected override void OnInitialize()
		{
			base.OnInitialize();
			_viewModel = new RBMConfigViewModel();
			_gauntletLayer = new GauntletLayer(1);
			_gauntletLayer.LoadMovie("RBMConfig", _viewModel);
			_gauntletLayer.InputRestrictions.SetInputRestrictions();
			AddLayer(_gauntletLayer);
		}
	}
}