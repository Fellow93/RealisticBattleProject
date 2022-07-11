// CunningLords.Interaction.CunningLordsMenuViewModel
using System.Collections.Generic;
using System.Xml;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMConfig
{
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

		public TextViewModel PlayerPostureMultiplierText { get; }
		public SelectorVM<SelectorItemVM> PlayerPostureMultiplier { get; }

		public TextViewModel PostureGUIEnabledText { get; }
		public SelectorVM<SelectorItemVM> PostureGUIEnabled { get; }

		public TextViewModel VanillaCombatAiText { get; }
		public SelectorVM<SelectorItemVM> VanillaCombatAi { get; }

		public TextViewModel ActiveTroopOverhaulText { get; }
		public SelectorVM<SelectorItemVM> ActiveTroopOverhaul { get; }

		public TextViewModel RangedReloadSpeedText { get; }
		public SelectorVM<SelectorItemVM> RangedReloadSpeed { get; }

		public TextViewModel PassiveShoulderShieldsText { get; }
		public SelectorVM<SelectorItemVM> PassiveShoulderShields { get; }

		public TextViewModel BetterArrowVisualsText { get; }
		public SelectorVM<SelectorItemVM> BetterArrowVisuals { get; }

		public SelectorVM<SelectorItemVM> RBMCombatEnabled { get; }

		public SelectorVM<SelectorItemVM> RBMAIEnabled { get; }

		public SelectorVM<SelectorItemVM> RBMTournamentEnabled { get; }


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
			List<string> troopOverhaulOnOff = new List<string> { "Inactive", "Active (Recommended)", };
			ActiveTroopOverhaulText = new TextViewModel(new TextObject("Troop Overhaul"));
			ActiveTroopOverhaul = new SelectorVM<SelectorItemVM>(troopOverhaulOnOff, 0, null);

			List<string> rangedReloadSpeed = new List<string> { "Vanilla", "Realistic", "Semi-realistic (Default)" };
			RangedReloadSpeedText = new TextViewModel(new TextObject("Ranged reload speed"));
			RangedReloadSpeed = new SelectorVM<SelectorItemVM>(rangedReloadSpeed, 0, null);

			List<string> passiveShoulderShields = new List<string> { "Disabled (Default)", "Enabled" };
			PassiveShoulderShieldsText = new TextViewModel(new TextObject("Passive Shoulder Shields"));
			PassiveShoulderShields = new SelectorVM<SelectorItemVM>(passiveShoulderShields, 0, null);

			List<string> betterArrowVisuals = new List<string> { "Disabled", "Enabled (Default)" };
			BetterArrowVisualsText = new TextViewModel(new TextObject("Better Arrow Visuals"));
			BetterArrowVisuals = new SelectorVM<SelectorItemVM>(betterArrowVisuals, 0, null);

			if (RBMConfig.troopOverhaulActive)
			{
				ActiveTroopOverhaul.SelectedIndex = 1;
			}
			else
			{
				ActiveTroopOverhaul.SelectedIndex = 0;
			}

			if (RBMConfig.realisticRangedReload.Equals("0"))
			{
				RangedReloadSpeed.SelectedIndex = 0;
			}
			else if (RBMConfig.realisticRangedReload.Equals("1"))
			{
				RangedReloadSpeed.SelectedIndex = 1;
			}
			else if (RBMConfig.realisticRangedReload.Equals("2"))
			{
				RangedReloadSpeed.SelectedIndex = 2;
			}

			if (RBMConfig.passiveShoulderShields)
			{
				PassiveShoulderShields.SelectedIndex = 1;
			}
			else
			{
				PassiveShoulderShields.SelectedIndex = 0;
			}

			if (RBMConfig.betterArrowVisuals)
			{
				BetterArrowVisuals.SelectedIndex = 1;
			}
			else
			{
				BetterArrowVisuals.SelectedIndex = 0;
			}

			List<string> siegeTowersOptions = new List<string> { "Disabled", "Enabled" };
			SiegeTowersEnabledText = new TextViewModel(new TextObject("Siege Towers"));
			SiegeTowersEnabled = new SelectorVM<SelectorItemVM>(siegeTowersOptions, 0, null);

			List<string> postureOptions = new List<string> { "Disabled", "Enabled (Default)" };
			PostureSystemEnabledText = new TextViewModel(new TextObject("Posture System"));
			PostureSystemEnabled = new SelectorVM<SelectorItemVM>(postureOptions, 0, null);

			List<string> playerPostureMultiplierOptions = new List<string> { "1x (Default)", "1.5x", "2x" };
			PlayerPostureMultiplierText = new TextViewModel(new TextObject("Player Posture Multiplier"));
			PlayerPostureMultiplier = new SelectorVM<SelectorItemVM>(playerPostureMultiplierOptions, 0, null);

			List<string> postureGUIOptions = new List<string> { "Disabled", "Enabled (Default)" };
			PostureGUIEnabledText = new TextViewModel(new TextObject("Posture GUI"));
			PostureGUIEnabled = new SelectorVM<SelectorItemVM>(postureGUIOptions, 0, null);

			List<string> vanillaCombatAiOptions = new List<string> { "Disabled (Default)", "Enabled" };
			VanillaCombatAiText = new TextViewModel(new TextObject("Vanilla AI Block/Parry/Attack"));
			VanillaCombatAi = new SelectorVM<SelectorItemVM>(vanillaCombatAiOptions, 0, null);

            if (RBMConfig.siegeTowersEnabled)
            {
                SiegeTowersEnabled.SelectedIndex = 1;
            }
            else
            {
                SiegeTowersEnabled.SelectedIndex = 0;
            }

			if (RBMConfig.playerPostureMultiplier == 1f)
			{
				PlayerPostureMultiplier.SelectedIndex = 0;
			}
			else if (RBMConfig.playerPostureMultiplier == 1.5f)
			{
				PlayerPostureMultiplier.SelectedIndex = 1;
			}
			else if (RBMConfig.playerPostureMultiplier == 2f)
            {
				PlayerPostureMultiplier.SelectedIndex = 2;
			}

			if (RBMConfig.postureEnabled)
			{
				PostureSystemEnabled.SelectedIndex = 1;
			}
			else
			{
				PostureSystemEnabled.SelectedIndex = 0;
			}

			if (RBMConfig.postureEnabled)
			{
				PostureSystemEnabled.SelectedIndex = 1;
			}
			else
			{
				PostureSystemEnabled.SelectedIndex = 0;
			}

			if (RBMConfig.postureGUIEnabled)
			{
				PostureGUIEnabled.SelectedIndex = 1;
			}
			else
			{
				PostureGUIEnabled.SelectedIndex = 0;
			}

			if (RBMConfig.vanillaCombatAi)
			{
				VanillaCombatAi.SelectedIndex = 1;
			}
			else
			{
				VanillaCombatAi.SelectedIndex = 0;
			}

			List<string> rbmCombatEnabledOptions = new List<string> { "Disabled", "Enabled (Default)" };
			RBMCombatEnabled = new SelectorVM<SelectorItemVM>(rbmCombatEnabledOptions, 0, null);

			List<string> rbmAiEnabledOptions = new List<string> { "Disabled", "Enabled (Default)" };
			RBMAIEnabled = new SelectorVM<SelectorItemVM>(rbmAiEnabledOptions, 0, null);

			List<string> rbmTournamentEnabledOptions = new List<string> { "Disabled", "Enabled (Default)" };
			RBMTournamentEnabled = new SelectorVM<SelectorItemVM>(rbmTournamentEnabledOptions, 0, null);

			if (RBMConfig.rbmCombatEnabled)
			{
				RBMCombatEnabled.SelectedIndex = 1;
			}
			else
			{
				RBMCombatEnabled.SelectedIndex = 0;
			}

			if (RBMConfig.rbmAiEnabled)
			{
				RBMAIEnabled.SelectedIndex = 1;
			}
			else
			{
				RBMAIEnabled.SelectedIndex = 0;
			}

			if (RBMConfig.rbmCombatEnabled)
			{
				RBMTournamentEnabled.SelectedIndex = 1;
			}
			else
			{
				RBMTournamentEnabled.SelectedIndex = 0;
			}

		}

		public override void RefreshValues()
		{
			base.RefreshValues();
		}

		private void ExecuteDone()
		{

			if (ActiveTroopOverhaul.SelectedIndex == 0)
			{
				RBMConfig.troopOverhaulActive = false;
			}
			if (ActiveTroopOverhaul.SelectedIndex == 1)
			{
				RBMConfig.troopOverhaulActive = true;
			}

			if (ActiveTroopOverhaul.SelectedIndex == 0)
			{
				RBMConfig.troopOverhaulActive = false;
			}
			if (ActiveTroopOverhaul.SelectedIndex == 1)
			{
				RBMConfig.troopOverhaulActive = true;
			}

			if (RangedReloadSpeed.SelectedIndex == 0)
			{
				RBMConfig.realisticRangedReload = "0";
			}
			else if (RangedReloadSpeed.SelectedIndex == 1)
			{
				RBMConfig.realisticRangedReload = "1";
			}
			else if (RangedReloadSpeed.SelectedIndex == 2)
			{
				RBMConfig.realisticRangedReload = "2";
			}

			if (PassiveShoulderShields.SelectedIndex == 0)
			{
				RBMConfig.passiveShoulderShields = false;
			}
			if (PassiveShoulderShields.SelectedIndex == 1)
			{
				RBMConfig.passiveShoulderShields = true;
			}

			if (BetterArrowVisuals.SelectedIndex == 0)
			{
				RBMConfig.betterArrowVisuals = false;
			}
			if (PassiveShoulderShields.SelectedIndex == 1)
			{
				RBMConfig.betterArrowVisuals = true;
			}

            if (SiegeTowersEnabled.SelectedIndex == 0)
            {
				RBMConfig.siegeTowersEnabled = false;
			}
			if (SiegeTowersEnabled.SelectedIndex == 1)
            {
				RBMConfig.siegeTowersEnabled = true;
			}

			if (PostureSystemEnabled.SelectedIndex == 0)
			{
				RBMConfig.postureEnabled = false;
			}
			if (PostureSystemEnabled.SelectedIndex == 1)
			{
				RBMConfig.postureEnabled = true;
			}

			if (PlayerPostureMultiplier.SelectedIndex == 0)
			{
				RBMConfig.playerPostureMultiplier = 1f;
			}
			else if (PlayerPostureMultiplier.SelectedIndex == 1)
			{
				RBMConfig.playerPostureMultiplier = 1.5f;
			}
			else if (PlayerPostureMultiplier.SelectedIndex == 2)
			{
				RBMConfig.playerPostureMultiplier = 2f;
			}

			if (PostureGUIEnabled.SelectedIndex == 0)
			{
				RBMConfig.postureGUIEnabled = false;
			}
			if (PostureGUIEnabled.SelectedIndex == 1)
			{
				RBMConfig.postureGUIEnabled = true;
			}

			if (VanillaCombatAi.SelectedIndex == 0)
			{
				RBMConfig.vanillaCombatAi = false;
			}
			if (PostureGUIEnabled.SelectedIndex == 1)
			{
				RBMConfig.vanillaCombatAi = true;
			}

			if (RBMCombatEnabled.SelectedIndex == 0)
			{
				RBMConfig.rbmCombatEnabled = false;
			}
			if (RBMCombatEnabled.SelectedIndex == 1)
			{
				RBMConfig.rbmCombatEnabled = true;
			}

			if (RBMAIEnabled.SelectedIndex == 0)
			{
				RBMConfig.rbmAiEnabled = false;
			}
			if (RBMAIEnabled.SelectedIndex == 1)
			{
				RBMConfig.rbmAiEnabled = true;
			}

			if (RBMTournamentEnabled.SelectedIndex == 0)
			{
				RBMConfig.rbmTournamentEnabled = false;
			}
			if (RBMTournamentEnabled.SelectedIndex == 1)
			{
				RBMConfig.rbmTournamentEnabled = true;
			}

			RBMConfig.saveXmlConfig();
			RBMConfig.parseXmlConfig();
			TaleWorlds.ScreenSystem.ScreenManager.PopScreen();
		}

		private void ExecuteCancel()
		{
			TaleWorlds.ScreenSystem.ScreenManager.PopScreen();
		}

	}
}
