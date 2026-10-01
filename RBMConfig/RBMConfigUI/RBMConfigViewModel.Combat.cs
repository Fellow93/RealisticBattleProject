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

        public TextViewModel AiKickBashEnabledText { get; }
        public SelectorVM<SelectorItemVM> AiKickBashEnabled { get; }

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

        public TextViewModel RangedReloadAffectsAiText { get; }
        public SelectorVM<SelectorItemVM> RangedReloadAffectsAi { get; }

        public TextViewModel RangedAimArcEnabledText { get; }
        public SelectorVM<SelectorItemVM> RangedAimArcEnabled { get; }

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

        // Plain-text label/hint (no {=RBM_CON_xxx} id) for the same reason as the Frontline rows below.
        private float _armorMultiplier;

        [DataSourceProperty]
        public float ArmorMultiplier
        {
            get { return _armorMultiplier; }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value * 20f) / 20f, 0.5f, 4f);
                if (snapped != _armorMultiplier)
                {
                    _armorMultiplier = snapped;
                    OnPropertyChangedWithValue(snapped, "ArmorMultiplier");
                    OnPropertyChanged("ArmorMultiplierValue");
                }
            }
        }

        [DataSourceProperty]
        public string ArmorMultiplierValue
        {
            get { return _armorMultiplier.ToString("0.00"); }
        }

        [DataSourceProperty]
        public string ArmorMultipliert
        {
            get { return new TextObject("Armor Multiplier").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel ArmorMultiplierHint { get; } = Hint("How strongly armor reduces damage: a blow is scaled by 100 / (100 + armor x this). Higher makes armor protect more and fights last longer; lower makes everyone die faster. Also feeds auto-resolve and troop power. Default 2.00.");

        // Plain-text label/hint for the same reason as the row above.
        private float _arrowThicknessScale;

        [DataSourceProperty]
        public float ArrowThicknessScale
        {
            get { return _arrowThicknessScale; }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value * 4f) / 4f, 1f, 5f);
                if (snapped != _arrowThicknessScale)
                {
                    _arrowThicknessScale = snapped;
                    OnPropertyChangedWithValue(snapped, "ArrowThicknessScale");
                    OnPropertyChanged("ArrowThicknessScaleValue");
                }
            }
        }

        [DataSourceProperty]
        public string ArrowThicknessScaleValue
        {
            get { return _arrowThicknessScale.ToString("0.00"); }
        }

        [DataSourceProperty]
        public string ArrowThicknessScalet
        {
            get { return new TextObject("Flying arrow thickness").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel ArrowThicknessScaleHint { get; } = Hint("Makes the realistic arrows and bolts of Better Arrow Visuals thicker while they fly, so they are easier to follow. Only the thickness is scaled, not the length, and only in flight: arrows in the quiver, on the string and stuck in a target stay true to size. Visual only, hits are unchanged. Does nothing when Better Arrow Visuals is disabled. Default 1.00 (true to size).");

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

        // Plain-text label/hint (no {=RBM_CON_xxx} id) for the same reason as the Frontline rows below.
        [DataSourceProperty]
        public string RangedReloadAffectsAit
        {
            get { return new TextObject("Ranged reload applies to AI").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel RangedReloadAffectsAiHint { get; } = Hint("When enabled, AI archers and crossbowmen follow the Ranged reload speed setting too (Vanilla / Realistic / Semi-realistic) instead of their fixed AI reload. Off keeps that setting player-only. Default off.");

        // Plain-text label/hint for the same reason as the row above.
        [DataSourceProperty]
        public string RangedAimArcEnabledt
        {
            get { return new TextObject("Ranged aim arc (player, experimental)").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel RangedAimArcEnabledHint { get; } = Hint("Experimental. While you draw a bow or crossbow or wind up a sling, shows the predicted flight of the missile as a dotted arc with a marker where it will land. Uses the same launch speed and air drag the real shot flies with. In third person, when you aim up the camera also lifts and tilts down so the landing point of a high shot stays on screen; where you aim is unchanged, and the crosshair is hidden while the camera is moved (the arc shows the aim). Player only; thrown weapons are not covered. Default off.");

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
        public string AiKickBasht
        {
            get
            {
                return new TextObject("{=RBM_CON_120}AI Kick and Bash").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel AiKickBashHint { get; } = Hint("{=RBM_CON_121}AI soldiers kick, shield bash and weapon bash. Kicks and bashes also deal real damage, cost posture and stamina, and can knock an enemy down. Applies to the player's kicks and bashes too.");

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
