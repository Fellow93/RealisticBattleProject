// CunningLords.Interaction.CunningLordsMenuViewModel
using RealisticBattleAiModule;
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

	public TextViewModel SiegeTowersEnabledText { get; }
	public SelectorVM<SelectorItemVM> SiegeTowersEnabled { get; }

	public TextViewModel PostureSystemEnabledText { get; }
	public SelectorVM<SelectorItemVM> PostureSystemEnabled { get; }
	
	public TextViewModel PostureGUIEnabledText { get; }
	public SelectorVM<SelectorItemVM> PostureGUIEnabled { get; }

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
		List<string> siegeTowersOptions = new List<string> { "Disabled", "Enabled"};
		SiegeTowersEnabledText = new TextViewModel(new TextObject("Siege Towers"));
		SiegeTowersEnabled = new SelectorVM<SelectorItemVM>(siegeTowersOptions, 0, null);

		List<string> postureOptions = new List<string> { "Disabled", "Enabled (Default)" };
		PostureSystemEnabledText = new TextViewModel(new TextObject("Posture System"));
		PostureSystemEnabled = new SelectorVM<SelectorItemVM>(postureOptions, 0, null);

		List<string> postureGUIOptions = new List<string> { "Disabled", "Enabled (Default)" };
		PostureGUIEnabledText = new TextViewModel(new TextObject("Posture GUI"));
		PostureGUIEnabled = new SelectorVM<SelectorItemVM>(postureGUIOptions, 0, null);

		if (XmlConfig.dict["Global.SiegeTowersEnabled"] == 0)
		{
			SiegeTowersEnabled.SelectedIndex = 0;
		}
		else if (XmlConfig.dict["Global.SiegeTowersEnabled"] == 1)
		{
			SiegeTowersEnabled.SelectedIndex = 1;
		}

		if (XmlConfig.dict["Global.PostureEnabled"] == 0)
		{
			PostureSystemEnabled.SelectedIndex = 0;
		}
		else if (XmlConfig.dict["Global.PostureEnabled"] == 1)
		{
			PostureSystemEnabled.SelectedIndex = 1;
		}

		if (XmlConfig.dict["Global.PostureGUIEnabled"] == 0)
		{
			PostureGUIEnabled.SelectedIndex = 0;
		}
		else if (XmlConfig.dict["Global.PostureGUIEnabled"] == 1)
		{
			PostureGUIEnabled.SelectedIndex = 1;
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
                if (subNode.Name.Equals("SiegeTowersEnabled"))
                {
					if (SiegeTowersEnabled.SelectedIndex == 0)
					{
						subNode.InnerText = "0";
					}
					if (SiegeTowersEnabled.SelectedIndex == 1)
					{
						subNode.InnerText = "1";
					}
				}

				if (subNode.Name.Equals("PostureGUIEnabled"))
				{
					if (PostureSystemEnabled.SelectedIndex == 0)
					{
						subNode.InnerText = "0";
					}
					if (PostureSystemEnabled.SelectedIndex == 1)
					{
						subNode.InnerText = "1";
					}
				}

				if (subNode.Name.Equals("PostureGUIEnabled"))
				{
					if (PostureGUIEnabled.SelectedIndex == 0)
					{
						subNode.InnerText = "0";
					}
					if (PostureGUIEnabled.SelectedIndex == 1)
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
