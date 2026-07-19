using System;
using System.Collections.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMConfig
{
    internal partial class RBMConfigViewModel
    {
        // Equipment-aware auto-resolve: on/off only. Its strength (SimulationEquipmentPowerWeight) and the
        // replay sample count stay in the config file -- they are tuning knobs, not settings to fiddle with here.
        public TextViewModel SimulationEquipmentEnabledText { get; }
        public SelectorVM<SelectorItemVM> SimulationEquipmentEnabled { get; }

        // A beaten side breaks and runs in auto-resolve instead of fighting to the last man. On/off only.
        public TextViewModel SimulationRoutEnabledText { get; }
        public SelectorVM<SelectorItemVM> SimulationRoutEnabled { get; }

        // Party strength priced on a troop's kit and training instead of his tier, plus his commander's perks.
        // On/off only; its scales stay in the config file as tuning knobs. Auto-resolve is not affected.
        public TextViewModel StrategicPowerEnabledText { get; }
        public SelectorVM<SelectorItemVM> StrategicPowerEnabled { get; }

        // Real captain perks in auto-resolve (in place of vanilla's flat count of the side commander's), plus the
        // commander's hit-point perks restored to his men. On/off only.
        public TextViewModel SimulationPerkSystemText { get; }
        public SelectorVM<SelectorItemVM> SimulationPerkSystem { get; }

        public TextViewModel SimulationLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> SimulationLoggingEnabled { get; }

        // Field-battle blow-by-blow log, the counterpart to the auto-resolve trace, meant to be read against it.
        public TextViewModel BattleHitLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> BattleHitLoggingEnabled { get; }

        // Watching an AI battle from a free camera: the live counterpart to both logs above, and the only way to see
        // the field AI fight the same muster auto-resolve is scoring. Needs RTSCamera.
        public TextViewModel SpectateBattlesEnabledText { get; }
        public SelectorVM<SelectorItemVM> SpectateBattlesEnabled { get; }

        [DataSourceProperty]
        public string SimulationEquipmentt
        {
            get
            {
                return new TextObject("{=RBM_CON_093}Detailed Auto Resolve").ToString();
            }
        }

        [DataSourceProperty]
        public string SimulationRoutt
        {
            get
            {
                return new TextObject("{=RBM_CON_096}Auto Resolve Routing").ToString();
            }
        }

        [DataSourceProperty]
        public string StrategicPowert
        {
            get
            {
                return new TextObject("{=RBM_CON_098}Equipment Based Troop Power").ToString();
            }
        }

        [DataSourceProperty]
        public string SimulationPerkt
        {
            get
            {
                return new TextObject("{=RBM_CON_097}Auto Resolve Perks").ToString();
            }
        }

        [DataSourceProperty]
        public string SimulationLoggingt
        {
            get
            {
                return new TextObject("{=RBM_CON_094}Detailed Auto Resolve Logging").ToString();
            }
        }

        [DataSourceProperty]
        public string SpectateBattlest
        {
            get
            {
                return new TextObject("{=RBM_CON_099}Spectate AI Battles").ToString();
            }
        }

        private float _spectateMinTroopsPerSide;

        [DataSourceProperty]
        public float SpectateMinTroopsPerSide
        {
            get
            {
                return _spectateMinTroopsPerSide;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value), 10f, 1000f);
                if (snapped != _spectateMinTroopsPerSide)
                {
                    _spectateMinTroopsPerSide = snapped;
                    OnPropertyChangedWithValue(snapped, "SpectateMinTroopsPerSide");
                    OnPropertyChanged("SpectateMinTroopsPerSideValue");
                }
            }
        }

        [DataSourceProperty]
        public string SpectateMinTroopsPerSideValue
        {
            get
            {
                return ((int)_spectateMinTroopsPerSide).ToString();
            }
        }

        [DataSourceProperty]
        public string SpectateMinTroopsPerSidet
        {
            get
            {
                return new TextObject("{=RBM_CON_101}Spectate Minimum Troops Per Side").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel SpectateMinTroopsPerSideHint { get; } = Hint("{=RBM_CON_102}How many men both sides must field before a battle between two AI lords is worth being asked about. Two patrols brushing past each other say nothing about how a line holds. Default 100.");

        [DataSourceProperty]
        public string BattleHitLoggingt
        {
            get
            {
                return new TextObject("{=RBM_CON_095}Field Battle Logging").ToString();
            }
        }
    }
}
