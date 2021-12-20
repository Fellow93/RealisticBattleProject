// CunningLords.Interaction.CunningLordsMenuViewModel
using RealisticBattleCombatModule;
using System.Collections.Generic;
using System.Xml;
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

	public TextViewModel RangedReloadSpeedText { get; }
	public SelectorVM<SelectorItemVM> RangedReloadSpeed { get; }

	public TextViewModel PassiveShoulderShieldsText { get; }
	public SelectorVM<SelectorItemVM> PassiveShoulderShields { get; }

	public TextViewModel BetterArrowVisualsText { get; }
	public SelectorVM<SelectorItemVM> BetterArrowVisuals { get; }


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
		List<string> troopOverhaulOnOff = new List<string> { "Inactive", "Active (Recommended)",};
		ActiveTroopOverhaulText = new TextViewModel(new TextObject("Troop Overhaul"));
		ActiveTroopOverhaul = new SelectorVM<SelectorItemVM>(troopOverhaulOnOff, 0, null);

		List<string> rangedReloadSpeed = new List<string> { "Vanilla" , "Realisitc" , "Semi-realistic (Default)" };
		RangedReloadSpeedText = new TextViewModel(new TextObject("Ranged reload speed"));
		RangedReloadSpeed = new SelectorVM<SelectorItemVM>(rangedReloadSpeed, 0, null);

		List<string> passiveShoulderShields = new List<string> { "Disabled", "Enabled (Default)" };
		PassiveShoulderShieldsText = new TextViewModel(new TextObject("Passive Shoulder Shields"));
		PassiveShoulderShields = new SelectorVM<SelectorItemVM>(passiveShoulderShields, 0, null);

		List<string> betterArrowVisuals = new List<string> { "Disabled", "Enabled (Default)" };
		BetterArrowVisualsText = new TextViewModel(new TextObject("Better Arrow Visuals"));
		BetterArrowVisuals = new SelectorVM<SelectorItemVM>(betterArrowVisuals, 0, null);

		if (XmlConfig.dict["Global.TroopOverhaulActive"] == 0)
		{
			ActiveTroopOverhaul.SelectedIndex = 0;
		}
		else if (XmlConfig.dict["Global.TroopOverhaulActive"] == 1)
		{
			ActiveTroopOverhaul.SelectedIndex = 1;
		}

		if (XmlConfig.dict["Global.RealisticRangedReload"] == 0)
		{
			RangedReloadSpeed.SelectedIndex = 0;
		}
		else if (XmlConfig.dict["Global.RealisticRangedReload"] == 1)
		{
			RangedReloadSpeed.SelectedIndex = 1;
		}
		else if (XmlConfig.dict["Global.RealisticRangedReload"] == 2)
		{
			RangedReloadSpeed.SelectedIndex = 2;
		}

		if (XmlConfig.dict["Global.PassiveShoulderShields"] == 0)
		{
			PassiveShoulderShields.SelectedIndex = 0;
		}
		else if (XmlConfig.dict["Global.PassiveShoulderShields"] == 1)
		{
			PassiveShoulderShields.SelectedIndex = 1;
		}

		if (XmlConfig.dict["Global.BetterArrowVisuals"] == 0)
		{
			BetterArrowVisuals.SelectedIndex = 0;
		}
		else if (XmlConfig.dict["Global.BetterArrowVisuals"] == 1)
		{
			BetterArrowVisuals.SelectedIndex = 1;
		}
	}

	public override void RefreshValues()
	{
		base.RefreshValues();
	}

	private void ExecuteDone()
	{

		XmlDocument xmlDocument = new XmlDocument();
		xmlDocument.Load(Utilities.GetConfigFilePath());

		foreach (XmlNode childNode in xmlDocument.SelectSingleNode("/config").ChildNodes)
		{
			foreach (XmlNode subNode in childNode)
			{
                if (subNode.Name.Equals("TroopOverhaulActive"))
                {
					if (ActiveTroopOverhaul.SelectedIndex == 0)
					{
						subNode.InnerText = "0";
					}
					if (ActiveTroopOverhaul.SelectedIndex == 1)
					{
						subNode.InnerText = "1";
					}
				}

				if (subNode.Name.Equals("RealisticRangedReload"))
				{
					if (RangedReloadSpeed.SelectedIndex == 0)
					{
						subNode.InnerText = "0";
					}
					else if(RangedReloadSpeed.SelectedIndex == 1)
					{
						subNode.InnerText = "1";
					}
					else if (RangedReloadSpeed.SelectedIndex == 2)
					{
						subNode.InnerText = "2";
					}
				}

				if (subNode.Name.Equals("PassiveShoulderShields"))
				{
					if (PassiveShoulderShields.SelectedIndex == 0)
					{
						subNode.InnerText = "0";
					}
					if (PassiveShoulderShields.SelectedIndex == 1)
					{
						subNode.InnerText = "1";
					}
				}

				if (subNode.Name.Equals("BetterArrowVisuals"))
				{
					if (BetterArrowVisuals.SelectedIndex == 0)
					{
						subNode.InnerText = "0";
					}
					if (BetterArrowVisuals.SelectedIndex == 1)
					{
						subNode.InnerText = "1";
					}
				}
			}
		}

		xmlDocument.Save(Utilities.GetConfigFilePath());

		XmlConfig.dict.Clear();

		foreach (XmlNode childNode in xmlDocument.SelectSingleNode("/config").ChildNodes)
		{
			foreach (XmlNode subNode in childNode)
			{
				XmlConfig.dict.Add(childNode.Name + "." + subNode.Name, float.Parse(subNode.InnerText));
			}
		}

		ScreenManager.PopScreen();
	}

	private void ExecuteCancel()
	{
		ScreenManager.PopScreen();
	}

}
