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

        public TextViewModel FrontlineEnabledText { get; }
        public SelectorVM<SelectorItemVM> FrontlineEnabled { get; }

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

        // Greys out the frontline tuning sliders while the system itself is off. Same shape as
        // IsStaminaSelectable: a derived flag re-announced from the master selector's change handler.
        [DataSourceProperty]
        public bool FrontlineEnabledBool => FrontlineEnabled.SelectedIndex == 1;

        private void OnFrontlineEnabledChanged(SelectorVM<SelectorItemVM> selector)
        {
            OnPropertyChanged("FrontlineEnabledBool");
        }

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

        // ---- Frontline (RBMAI melee jostling system) -------------------------------------------------
        // Plain-text hints on purpose: the {=RBM_CON_xxx} ids are a contiguous block used by the campaign
        // options and LOC-eng.xml overrides any id it defines, so new rows stay unkeyed like the other
        // recently added toggles (Deserter Raiders, Caravan Logging).

        [DataSourceProperty]
        public string FrontlineEnabledt
        {
            get { return new TextObject("Frontline System").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel FrontlineEnabledHint { get; } = Hint("Per-soldier jostling inside a charging infantry or archer line: each man decides every couple of seconds whether to press in, step back, close on a neighbour, slide around a flank or hold his rank. Off leaves melee lines on RBM's plain charge. Cavalry/archer free-charge rules and unit facing are unaffected either way. Default on.");

        private float _frontlineMinFormationSize;

        [DataSourceProperty]
        public float FrontlineMinFormationSize
        {
            get { return _frontlineMinFormationSize; }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value), 0f, 200f);
                if (snapped != _frontlineMinFormationSize)
                {
                    _frontlineMinFormationSize = snapped;
                    OnPropertyChangedWithValue(snapped, "FrontlineMinFormationSize");
                    OnPropertyChanged("FrontlineMinFormationSizeValue");
                }
            }
        }

        [DataSourceProperty]
        public string FrontlineMinFormationSizeValue
        {
            get { return _frontlineMinFormationSize.ToString("0"); }
        }

        [DataSourceProperty]
        public string FrontlineMinFormationSizet
        {
            get { return new TextObject("Frontline Min Formation Size").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel FrontlineMinFormationSizeHint { get; } = Hint("Formations smaller than this (undetached men) skip the frontline system entirely and charge normally -- there is no rank to hold in a handful of men. Default 25.");

        private float _frontlineDecisionTimerMax;

        [DataSourceProperty]
        public float FrontlineDecisionTimerMax
        {
            get { return _frontlineDecisionTimerMax; }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 10f);
                if (snapped != _frontlineDecisionTimerMax)
                {
                    _frontlineDecisionTimerMax = snapped;
                    OnPropertyChangedWithValue(snapped, "FrontlineDecisionTimerMax");
                    OnPropertyChanged("FrontlineDecisionTimerMaxValue");
                }
            }
        }

        [DataSourceProperty]
        public string FrontlineDecisionTimerMaxValue
        {
            get { return _frontlineDecisionTimerMax.ToString("0.00"); }
        }

        [DataSourceProperty]
        public string FrontlineDecisionTimerMaxt
        {
            get { return new TextObject("Frontline Decision Hold").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel FrontlineDecisionTimerMaxHint { get; } = Hint("Once a man picks a move he sticks with it for a random 0 to this many seconds before reconsidering. Higher is steadier and cheaper; lower makes the line twitchier. Default 2.00.");

        private float _frontlineAttackWeight;

        [DataSourceProperty]
        public float FrontlineAttackWeight
        {
            get { return _frontlineAttackWeight; }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 3f);
                if (snapped != _frontlineAttackWeight)
                {
                    _frontlineAttackWeight = snapped;
                    OnPropertyChangedWithValue(snapped, "FrontlineAttackWeight");
                    OnPropertyChanged("FrontlineAttackWeightValue");
                }
            }
        }

        [DataSourceProperty]
        public string FrontlineAttackWeightValue
        {
            get { return _frontlineAttackWeight.ToString("0.00"); }
        }

        [DataSourceProperty]
        public string FrontlineAttackWeightt
        {
            get { return new TextObject("Frontline Attack Weight").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel FrontlineAttackWeightHint { get; } = Hint("Multiplier on the 'press forward at my target' score. Above 1 makes lines more aggressive and thinner; below 1 makes them hang back. Default 1.00.");

        private float _frontlineBackStepWeight;

        [DataSourceProperty]
        public float FrontlineBackStepWeight
        {
            get { return _frontlineBackStepWeight; }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 3f);
                if (snapped != _frontlineBackStepWeight)
                {
                    _frontlineBackStepWeight = snapped;
                    OnPropertyChangedWithValue(snapped, "FrontlineBackStepWeight");
                    OnPropertyChanged("FrontlineBackStepWeightValue");
                }
            }
        }

        [DataSourceProperty]
        public string FrontlineBackStepWeightValue
        {
            get { return _frontlineBackStepWeight.ToString("0.00"); }
        }

        [DataSourceProperty]
        public string FrontlineBackStepWeightt
        {
            get { return new TextObject("Frontline Back Step Weight").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel FrontlineBackStepWeightHint { get; } = Hint("Multiplier on the 'give ground and let a fresh man through' score. Above 1 gives more rotation out of the front rank. Default 1.00.");

        private float _frontlineFindAllyWeight;

        [DataSourceProperty]
        public float FrontlineFindAllyWeight
        {
            get { return _frontlineFindAllyWeight; }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 3f);
                if (snapped != _frontlineFindAllyWeight)
                {
                    _frontlineFindAllyWeight = snapped;
                    OnPropertyChangedWithValue(snapped, "FrontlineFindAllyWeight");
                    OnPropertyChanged("FrontlineFindAllyWeightValue");
                }
            }
        }

        [DataSourceProperty]
        public string FrontlineFindAllyWeightValue
        {
            get { return _frontlineFindAllyWeight.ToString("0.00"); }
        }

        [DataSourceProperty]
        public string FrontlineFindAllyWeightt
        {
            get { return new TextObject("Frontline Close Ranks Weight").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel FrontlineFindAllyWeightHint { get; } = Hint("Multiplier on the 'close the gap to my nearest neighbour' score. Above 1 makes lines huddle tighter and shield walls hold better; below 1 lets them spread. Default 1.00.");

        private float _frontlineFlankWeight;

        [DataSourceProperty]
        public float FrontlineFlankWeight
        {
            get { return _frontlineFlankWeight; }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 3f);
                if (snapped != _frontlineFlankWeight)
                {
                    _frontlineFlankWeight = snapped;
                    OnPropertyChangedWithValue(snapped, "FrontlineFlankWeight");
                    OnPropertyChanged("FrontlineFlankWeightValue");
                }
            }
        }

        [DataSourceProperty]
        public string FrontlineFlankWeightValue
        {
            get { return _frontlineFlankWeight.ToString("0.00"); }
        }

        [DataSourceProperty]
        public string FrontlineFlankWeightt
        {
            get { return new TextObject("Frontline Sidestep Weight").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel FrontlineFlankWeightHint { get; } = Hint("Multiplier on both the left and right 'slide sideways past the man in front' scores. Above 1 makes lines spread wide around a stalled front rank. Default 1.00.");

    }
}
