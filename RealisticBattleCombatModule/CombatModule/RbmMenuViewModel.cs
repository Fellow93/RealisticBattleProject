// CunningLords.Interaction.CunningLordsMenuViewModel
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Engine.Screens;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal class RbmMenuViewModel : ViewModel
{

	private string _doneText;

	private string _cancelText;

	private string _tab1Text;

	private string _tab2Text;

	private string _sliderText;

	private float _sliderValue;

	private string _sliderValueText;

	private bool _booleanValue;

	public TextViewModel  ActiveTroopOverhaulText{ get; }

	public SelectorVM<SelectorItemVM> ActiveTroopOverhaul { get; }

	[DataSourceProperty]
	public string CancelText => _cancelText;

	[DataSourceProperty]
	public string DoneText => _doneText;

	[DataSourceProperty]
	public bool BooleanValue
	{
		get
		{
			return _booleanValue;
		}
		set
		{
			_booleanValue = !BooleanValue;
		}
	}

	public RbmMenuViewModel()
	{
		_doneText = new TextObject("{=ATDone}Done").ToString();
		_cancelText = new TextObject("{=ATCancel}Cancel").ToString();
		_tab1Text = new TextObject("{=ATTab1Text}Main Interface").ToString();
		_tab2Text = new TextObject("{=ATTab2Text}Sub Interface").ToString();
		_sliderText = new TextObject("{=ATSlideText}Slider Example").ToString();
		_sliderValue = 10f;
		_sliderValueText = _sliderValue.ToString();
		_booleanValue = true;
		RefreshValues();
		//RbmConfigData data;
		List<string> troopOverhaulOnOff = new List<string> { "Active", "Inactive",};
		ActiveTroopOverhaulText = new TextViewModel(new TextObject("RBM Troop Overhaul"));
		ActiveTroopOverhaul = new SelectorVM<SelectorItemVM>(troopOverhaulOnOff, 0, null);
		ActiveTroopOverhaul.SelectedIndex = 0;
	}

	public override void RefreshValues()
	{
		base.RefreshValues();
	}

	private void ExecuteDone()
	{
		bool isTroopovehaulActive = true;
		if (ActiveTroopOverhaul.SelectedIndex == 0)
        {
			isTroopovehaulActive = true;
		}
        else
        {
			isTroopovehaulActive = false;
		}
		RbmConfigData orders = new RbmConfigData
		{
			isTroopOverhaulActive = isTroopovehaulActive
		};
		ScreenManager.PopScreen();
	}

	private void ExecuteCancel()
	{
		ScreenManager.PopScreen();
	}

}
