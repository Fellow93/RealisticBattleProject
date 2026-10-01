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

        // The troop-power, auto-resolve and field-battle logging toggles are in RBMConfigViewModel.Debug.cs.

        // Real captain perks in auto-resolve (in place of vanilla's flat count of the side commander's), plus the
        // commander's hit-point perks restored to his men. On/off only.
        public TextViewModel SimulationPerkSystemText { get; }
        public SelectorVM<SelectorItemVM> SimulationPerkSystem { get; }

        // Watching an AI battle from a free camera: the live counterpart to the auto-resolve and field-battle logs,
        // and the only way to see the field AI fight the same muster auto-resolve is scoring. Needs RTSCamera.
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
        public BasicTooltipViewModel SimulationEquipmentEnabledHint { get; } = Hint("{=RBM_CON_136}Auto-resolve works out every simulated blow from the troops' actual gear -- armor on each body part, shields and real missiles -- instead of their tier alone. Off returns auto-resolve to the base game, and Auto Resolve Routing and Auto Resolve Perks with it. Default on.");

        [DataSourceProperty]
        public string SimulationRoutt
        {
            get
            {
                return new TextObject("{=RBM_CON_096}Auto Resolve Routing").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel SimulationRoutEnabledHint { get; } = Hint("{=RBM_CON_137}In auto-resolve, a side cut down to fewer than 50 men and losing clearly worse than its enemy may break and flee instead of fighting to the last man; the survivors escape. The more one-sided the fight, the likelier the break. Siege assaults are not affected. Needs Detailed Auto Resolve on. Default off.");

        [DataSourceProperty]
        public string StrategicPowert
        {
            get
            {
                return new TextObject("{=RBM_CON_098}Equipment Based Troop Power").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel StrategicPowerEnabledHint { get; } = Hint("{=RBM_CON_138}Party strength -- what the encounter screen shows and what AI lords weigh before attacking, fleeing or gathering armies -- is worked out from each troop's gear, training and horse and his commander's perks instead of his tier alone. Auto-resolve itself is not affected, so the shown strength no longer predicts it exactly. Default on.");

        [DataSourceProperty]
        public string SimulationPerkt
        {
            get
            {
                return new TextObject("{=RBM_CON_097}Auto Resolve Perks").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel SimulationPerkSystemHint { get; } = Hint("{=RBM_CON_139}In auto-resolve, troops are split into formations under captains as in a real battle, and each captain's own combat perks apply to his formation; the commander's hit-point perks also reach his men. Replaces the base game's flat bonus for the number of captain perks. Needs Detailed Auto Resolve on. Default on.");

        [DataSourceProperty]
        public string SpectateBattlest
        {
            get
            {
                return new TextObject("{=RBM_CON_099}Spectate AI Battles").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel SpectateBattlesEnabledHint { get; } = Hint("{=RBM_CON_135}When two AI sides meet in a field battle or siege assault with enough men on each side, offers to let you watch it from a free camera. What you watch is only a copy: the battle on the map resolves on its own. Needs the RTS Camera mod. Default off.");

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
    }
}
