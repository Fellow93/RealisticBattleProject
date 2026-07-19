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
        public TextViewModel ThrustModifierText { get; }
        public SelectorVM<SelectorItemVM> ThrustModifier { get; }

        public TextViewModel SneakAttackInstaKillText { get; }
        public SelectorVM<SelectorItemVM> SneakAttackInstaKill { get; }

        public TextViewModel ArmorStatusUIEnabledText { get; }
        public SelectorVM<SelectorItemVM> ArmorStatusUIEnabled { get; }

        public TextViewModel RealisticArrowArcText { get; }
        public SelectorVM<SelectorItemVM> RealisticArrowArc { get; }

        public TextViewModel HitStopEnabledText { get; }
        public SelectorVM<SelectorItemVM> HitStopEnabled { get; }

        public TextViewModel PostureSystemEnabledText { get; }
        public SelectorVM<SelectorItemVM> PostureSystemEnabled { get; }

        public TextViewModel StaminaSystemEnabledText { get; }
        public SelectorVM<SelectorItemVM> StaminaSystemEnabled { get; }

        public TextViewModel PlayerPostureMultiplierText { get; }
        public SelectorVM<SelectorItemVM> PlayerPostureMultiplier { get; }

        public TextViewModel PostureGUIEnabledText { get; }
        public SelectorVM<SelectorItemVM> PostureGUIEnabled { get; }

        public TextViewModel VanillaCombatAiText { get; }
        public SelectorVM<SelectorItemVM> VanillaCombatAi { get; }

        public TextViewModel KeepBattleEnabledText { get; }
        public SelectorVM<SelectorItemVM> KeepBattleEnabled { get; }

        public TextViewModel ActiveTroopOverhaulText { get; }
        public SelectorVM<SelectorItemVM> ActiveTroopOverhaul { get; }

        public TextViewModel RangedReloadSpeedText { get; }
        public SelectorVM<SelectorItemVM> RangedReloadSpeed { get; }

        public TextViewModel PassiveShoulderShieldsText { get; }
        public SelectorVM<SelectorItemVM> PassiveShoulderShields { get; }

        public TextViewModel BetterArrowVisualsText { get; }
        public SelectorVM<SelectorItemVM> BetterArrowVisuals { get; }

        [DataSourceProperty]
        public string ThrustModifiert
        {
            get
            {
                return new TextObject("{=RBM_CON_021}Thrust weapon preference for AI (default at 0.05)").ToString();
            }
        }

        [DataSourceProperty]
        public string TroopOverhault
        {
            get
            {
                return new TextObject("{=RBM_CON_003}Troop Overhaul").ToString();
            }
        }

        [DataSourceProperty]
        public string Rangedspeedt
        {
            get
            {
                return new TextObject("{=RBM_CON_007}Ranged reload speed").ToString();
            }
        }

        [DataSourceProperty]
        public string PassiveShieldt
        {
            get
            {
                return new TextObject("{=RBM_CON_008}Passive Shoulder Shields").ToString();
            }
        }

        [DataSourceProperty]
        public string BetterArrowst
        {
            get
            {
                return new TextObject("{=RBM_CON_009}Better Arrow Visuals").ToString();
            }
        }

        [DataSourceProperty]
        public string ArmorGUIt
        {
            get
            {
                return new TextObject("{=RBM_CON_010}Armor Status GUI").ToString();
            }
        }

        [DataSourceProperty]
        public string SneakAttackt
        {
            get
            {
                return new TextObject("{=RBM_CON_023}Sneak Attack Insta-Kill").ToString();
            }
        }

        [DataSourceProperty]
        public string RealArrowt
        {
            get
            {
                return new TextObject("{=RBM_CON_011}Realistic Arrow Arc").ToString();
            }
        }

        [DataSourceProperty]
        public string HitStopt
        {
            get
            {
                return new TextObject("{=RBM_CON_022}Slow Motion in Combat").ToString();
            }
        }

        [DataSourceProperty]
        public string PostureSyst
        {
            get
            {
                return new TextObject("{=RBM_CON_012}Posture System").ToString();
            }
        }

        [DataSourceProperty]
        public string StaminaSyst
        {
            get
            {
                return new TextObject("{=RBM_CON_030}Stamina System (requires Posture)").ToString();
            }
        }

        [DataSourceProperty]
        public bool IsStaminaSelectable => PostureSystemEnabled.SelectedIndex == 1;

        private void OnPostureSystemChanged(SelectorVM<SelectorItemVM> selector)
        {
            if (StaminaSystemEnabled == null)
            {
                return;
            }
            if (selector.SelectedIndex == 0)
            {
                StaminaSystemEnabled.SelectedIndex = 0;
            }
            OnPropertyChanged("IsStaminaSelectable");
        }

        [DataSourceProperty]
        public string Playpost
        {
            get
            {
                return new TextObject("{=RBM_CON_013}Player Posture Multiplier").ToString();
            }
        }

        [DataSourceProperty]
        public string PostureGUIt
        {
            get
            {
                return new TextObject("{=RBM_CON_014}Posture GUI").ToString();
            }
        }

        [DataSourceProperty]
        public string Vanillat
        {
            get
            {
                return new TextObject("{=RBM_CON_015}Vanilla AI Block/Parry/Attack").ToString();
            }
        }

        [DataSourceProperty]
        public string KeepBattlet
        {
            get
            {
                return new TextObject("{=RBM_CON_031}Keep Battle (Last Stand)").ToString();
            }
        }

        public List<string> thrustModifierList = new List<string> { new TextObject("0.01").ToString(), new TextObject("0.05").ToString(), new TextObject("0.10").ToString(), new TextObject("0.15").ToString(),
                                                                new TextObject("0.20").ToString(), new TextObject("0.25").ToString(), new TextObject("0.30").ToString(), new TextObject("0.35").ToString(),
                                                                new TextObject("0.40").ToString(), new TextObject("0.45").ToString(), new TextObject("0.50").ToString(), new TextObject("0.55").ToString(),
                                                                new TextObject("0.60").ToString(), new TextObject("0.65").ToString(), new TextObject("0.70").ToString(), new TextObject("0.75").ToString(),
                                                                new TextObject("0.80").ToString(), new TextObject("0.85").ToString(), new TextObject("0.90").ToString(), new TextObject("0.95").ToString(),
                                                                new TextObject("1.00").ToString()};
    }
}
