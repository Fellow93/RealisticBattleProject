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

        [DataSourceProperty]
        public BasicTooltipViewModel ThrustModifierHint { get; } = Hint("{=RBM_CON_147}Scales the thrust damage figures weapons carry, which the AI weighs when it chooses between thrusting and swinging. RBM undoes the scale when it works out piercing damage, so stabs hit about as hard at any setting; lower values make the AI favour swings. Default 0.05.");

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
                float snapped = MathF.Clamp((float)System.Math.Round(value * 4f) / 4f, 1f, 10f);
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
            get { return new TextObject("Flying arrow thickness (experimental)").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel ArrowThicknessScaleHint { get; } = Hint("Experimental. Makes the realistic arrows and bolts of Better Arrow Visuals thicker while they fly, so they are easier to follow. Only the thickness is scaled, not the length, and only in flight: arrows in the quiver, on the string and stuck in a target stay true to size. Visual only, hits are unchanged. Does nothing when Better Arrow Visuals is disabled; at 1.00 it is switched off entirely. A change takes effect the next time a game is started or loaded. Default 1.00 (off, true to size).");

        // Stuck missiles falling out (RBMCombat Ranged/RangedRework.StuckMissiles.cs). Whole seconds; 0 = never.
        private float _stuckThrownFallOutSeconds;

        [DataSourceProperty]
        public float StuckThrownFallOutSeconds
        {
            get { return _stuckThrownFallOutSeconds; }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value), 0f, 60f);
                if (snapped != _stuckThrownFallOutSeconds)
                {
                    _stuckThrownFallOutSeconds = snapped;
                    OnPropertyChangedWithValue(snapped, "StuckThrownFallOutSeconds");
                    OnPropertyChanged("StuckThrownFallOutSecondsValue");
                }
            }
        }

        [DataSourceProperty]
        public string StuckThrownFallOutSecondsValue
        {
            get { return _stuckThrownFallOutSeconds <= 0f ? new TextObject("{=1JlzQIXE}Disabled").ToString() : _stuckThrownFallOutSeconds.ToString("0"); }
        }

        [DataSourceProperty]
        public string StuckThrownFallOutSecondst
        {
            get { return new TextObject("{=RBM_CON_124}Stuck javelins fall out after (s)").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel StuckThrownFallOutSecondsHint { get; } = Hint("{=RBM_CON_125}Javelins, throwing axes and throwing knives stuck in a shield or in a living body work loose after roughly this many seconds and drop to the ground, where they can be picked up again. Each one gets its own time, up to 40% shorter or longer. 0 leaves them stuck, as in the base game. Default 5.");

        private float _stuckArrowFallOutSeconds;

        [DataSourceProperty]
        public float StuckArrowFallOutSeconds
        {
            get { return _stuckArrowFallOutSeconds; }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value / 5f) * 5f, 0f, 300f);
                if (snapped != _stuckArrowFallOutSeconds)
                {
                    _stuckArrowFallOutSeconds = snapped;
                    OnPropertyChangedWithValue(snapped, "StuckArrowFallOutSeconds");
                    OnPropertyChanged("StuckArrowFallOutSecondsValue");
                }
            }
        }

        [DataSourceProperty]
        public string StuckArrowFallOutSecondsValue
        {
            get { return _stuckArrowFallOutSeconds <= 0f ? new TextObject("{=1JlzQIXE}Disabled").ToString() : _stuckArrowFallOutSeconds.ToString("0"); }
        }

        [DataSourceProperty]
        public string StuckArrowFallOutSecondst
        {
            get { return new TextObject("{=RBM_CON_126}Arrows in shields fall out after (s)").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel StuckArrowFallOutSecondsHint { get; } = Hint("{=RBM_CON_127}Arrows and bolts stuck in a shield work loose after roughly this many seconds and drop to the ground, where they can be picked up again. Each one gets its own time, up to 40% shorter or longer. A shield also holds only 8 at once: one more and the oldest drops right away. Arrows stuck in a body have no timer, but a body keeps only 4: one more and the oldest drops. 0 leaves them stuck with no limit, as in the base game. Default 45.");

        [DataSourceProperty]
        public string TroopOverhault
        {
            get
            {
                return new TextObject("{=RBM_CON_003}Troop Overhaul").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel ActiveTroopOverhaulHint { get; } = Hint("{=RBM_CON_154}Replaces the troop trees of the main cultures, plus mercenaries, minor-faction troops, militia and tournament fighters, with RBM's versions: new equipment, skills and upgrade paths. Inactive keeps the base game's troops, still using RBM's item changes. Default active.");

        [DataSourceProperty]
        public string Rangedspeedt
        {
            get
            {
                return new TextObject("{=RBM_CON_007}Ranged reload speed").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel RangedReloadSpeedHint { get; } = Hint("{=RBM_CON_153}How fast bows and crossbows are reloaded, scaling with skill. Realistic is slow, above all for unskilled shooters; Semi-realistic reloads much faster at low skill; both also slow the bow draw for everyone. Vanilla keeps the base game's speeds. The reload part applies to the player only unless 'Ranged reload applies to AI' is on. Default Semi-realistic.");

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
        public BasicTooltipViewModel RangedAimArcEnabledHint { get; } = Hint("Experimental. While you draw a bow or crossbow or wind up a sling, shows the predicted flight of the missile as a dotted arc with a marker where it will land. Uses the same launch speed and air drag the real shot flies with. In third person, when you aim up the camera also lifts and tilts down so the landing point of a high shot stays on screen; where you aim is unchanged, and the crosshair is hidden while the camera is moved (the arc shows the aim). Also covers javelins, throwing axes, throwing knives and stones. Player only. Default off.");

        [DataSourceProperty]
        public string PassiveShieldt
        {
            get
            {
                return new TextObject("{=RBM_CON_008}Passive Shoulder Shields").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel PassiveShoulderShieldsHint { get; } = Hint("{=RBM_CON_152}Lets RBM troops keep the shoulder-strapped versions of their shields, which stay on the shoulder while they fight with another weapon. Off swaps them for the normal hand-held versions. Needs Troop Overhaul on. Default off.");

        [DataSourceProperty]
        public string BetterArrowst
        {
            get
            {
                return new TextObject("{=RBM_CON_009}Better Arrow Visuals").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel BetterArrowVisualsHint { get; } = Hint("{=RBM_CON_151}Arrows and bolts in flight are drawn with their real model instead of the base game's thin streak. True to size they are harder to follow; 'Flying arrow thickness' can thicken them. Visual only, hits are unchanged. Default on.");

        [DataSourceProperty]
        public string ArmorGUIt
        {
            get
            {
                return new TextObject("{=RBM_CON_010}Armor Status GUI").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel ArmorStatusUIEnabledHint { get; } = Hint("{=RBM_CON_149}Shows six small icons in the lower right of the battle screen with the condition of your own armor -- head, shoulders, body, hands, legs and horse harness -- shading from green (better than standard) through grey to red as blows wear it down during the battle. Default on.");

        [DataSourceProperty]
        public string SneakAttackt
        {
            get
            {
                return new TextObject("{=RBM_CON_023}Sneak Attack Insta-Kill").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel SneakAttackInstaKillHint { get; } = Hint("{=RBM_CON_150}A blow that counts as a sneak attack -- a melee weapon or throwing knife striking an unaware person, or one not yet fully alert from behind, without hitting a shield -- deals a flat 200 damage, ignoring armor. It mostly matters in stealth missions, and you can never be the victim. Off applies the base game's sneak attack bonus before armor instead. Default off.");

        [DataSourceProperty]
        public string RealArrowt
        {
            get
            {
                return new TextObject("{=RBM_CON_011}Realistic Arrow Arc").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel RealisticArrowArcHint { get; } = Hint("{=RBM_CON_148}Your bow and crossbow shots leave about 5 degrees above where you aim, so they fly a higher arc and the crosshair no longer marks where they land. Player only; slings and thrown weapons are unaffected. Default off.");

        [DataSourceProperty]
        public string HitStopt
        {
            get
            {
                return new TextObject("{=RBM_CON_022}Slow Motion in Combat").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel HitStopEnabledHint { get; } = Hint("{=RBM_CON_146}Briefly slows the battle to a quarter of its speed, for three quarters of a second, when you kill or knock out an enemy or break his posture. Works without RBM AI, though the posture-break slowdown needs the Posture System. Default on.");

        [DataSourceProperty]
        public string PostureSyst
        {
            get
            {
                return new TextObject("{=RBM_CON_012}Posture System").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel PostureSystemEnabledHint { get; } = Hint("{=RBM_CON_145}Blocks, parries and hits wear down a posture meter that recovers over time. When it runs out the soldier can be staggered, drop his weapon or shield, be knocked off his horse, or take damage that crushes through his guard. Off also switches off the Stamina System and the Posture GUI. Needs RBM AI on. Default on.");

        [DataSourceProperty]
        public string StaminaSyst
        {
            get
            {
                return new TextObject("{=RBM_CON_030}Stamina System (requires Posture)").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel StaminaSystemEnabledHint { get; } = Hint("{=RBM_CON_144}Attacking, blocking and being hit drain stamina, faster in heavy armor and slower with high Athletics. A tired soldier hits weaker, moves, swings and reloads slower, blocks worse and loses posture faster; above 85% stamina he slowly regains health. Needs RBM AI and the Posture System on. Default on.");

        [DataSourceProperty]
        public string AiKickBasht
        {
            get
            {
                return new TextObject("{=RBM_CON_120}AI Kick and Bash").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel AiKickBashHint { get; } = Hint("{=RBM_CON_121}AI soldiers kick, shield bash and weapon bash. Kicks and bashes also deal real damage, cost posture and stamina, and can knock an enemy down. Applies to the player's kicks and bashes too. Default on.");

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
        public BasicTooltipViewModel PlayerPostureMultiplierHint { get; } = Hint("{=RBM_CON_143}Multiplies your own character's maximum posture and posture recovery, and with the Stamina System on also your maximum stamina and its recovery. AI soldiers are unaffected. Needs RBM AI and the Posture System on. Default 1x.");

        [DataSourceProperty]
        public string PostureGUIt
        {
            get
            {
                return new TextObject("{=RBM_CON_014}Posture GUI").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel PostureGUIEnabledHint { get; } = Hint("{=RBM_CON_142}Shows your own posture bar in battle (and stamina bar, with the Stamina System on), and for a few seconds after you trade blows with an enemy, his name, health, posture and stamina. Needs RBM AI and the Posture System on. Default on.");

        [DataSourceProperty]
        public string Vanillat
        {
            get
            {
                return new TextObject("{=RBM_CON_015}Vanilla AI Block/Parry/Attack").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel VanillaCombatAiHint { get; } = Hint("{=RBM_CON_140}Leaves AI soldiers' blocking, parrying and melee attack decisions on the base game's values instead of RBM's skill-based ones. It also lets the Combat AI difficulty option weaken RBM's AI aim as strongly as it does in the base game. Needs RBM AI on. Default off.");

        [DataSourceProperty]
        public string KeepBattlet
        {
            get
            {
                return new TextObject("{=RBM_CON_031}Keep Battle (Last Stand)").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel KeepBattleEnabledHint { get; } = Hint("{=RBM_CON_141}In sieges RBM keeps the defenders from losing morale as their comrades fall, so a garrison holds the walls to the last man. With this on, once fewer than 50 defenders remain and they have less than half the attackers' strength, losses shake them twice as hard, so the survivors can break and fall back for a last stand in the keep. Needs RBM AI on. Default off.");

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
