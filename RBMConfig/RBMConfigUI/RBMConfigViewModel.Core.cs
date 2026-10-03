using System;
using System.Collections.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMConfig
{
    internal partial class RBMConfigViewModel : ViewModel
    {
        // Each module's master toggle, and the caption above it. The row's left-hand label is the
        // shared "Module Status", so the caption is what says WHICH module is being switched --
        // without it all four tabs showed an unlabelled dropdown. Same texts as the RBMCombatt /
        // RBMAIt / RBMTournament / RBMCampaignt section titles, so a name is written in one place.
        public SelectorVM<SelectorItemVM> RBMCombatEnabled { get; }

        public TextViewModel RBMCombatEnabledText { get; }

        public SelectorVM<SelectorItemVM> RBMAIEnabled { get; }

        public TextViewModel RBMAIEnabledText { get; }

        public SelectorVM<SelectorItemVM> RBMTournamentEnabled { get; }

        public TextViewModel RBMTournamentEnabledText { get; }

        public SelectorVM<SelectorItemVM> RBMCampaignEnabled { get; }

        public TextViewModel RBMCampaignEnabledText { get; }

        // Hover tooltips on the four "Module Status" rows. The Campaign one is plain text, matching its unkeyed caption.
        [DataSourceProperty]
        public BasicTooltipViewModel RBMCombatEnabledHint { get; } = Hint("{=RBM_CON_130}Master switch for RBM's combat overhaul: damage, armor, weapon and missile physics, ranged reload, and RBM's reworked items, troops and siege engines. Off returns combat to the base game and every option in this section stops working. RBM's spear and weapon animation parameters only follow a change after a game restart. Default on.");

        [DataSourceProperty]
        public BasicTooltipViewModel RBMAIEnabledHint { get; } = Hint("{=RBM_CON_131}Master switch for RBM's battle AI: formation tactics and behaviors, siege AI, AI blocking and parrying, the posture and stamina systems, the frontline system and AI kicks and bashes. Off returns battle AI to the base game and every option in this section stops working. Default on.");

        [DataSourceProperty]
        public BasicTooltipViewModel RBMTournamentEnabledHint { get; } = Hint("{=RBM_CON_132}Tiered tournaments. Your tier comes from your level and the quality of your armor: below tier 5 you fight troops of your own tier, from tier 5 the lords and heroes in town. Arena gear, the prize (which can roll a better quality) and the renown you win follow your tier. Off restores the base game's tournaments. Default on.");

        [DataSourceProperty]
        public BasicTooltipViewModel RBMCampaignEnabledHint { get; } = Hint("{=RBM_CFG_CAMPAIGN_HINT}Master switch for RBM's campaign overhaul: the troop spoils economy, wages and upkeep, settlement wealth, village and town production, caravans, equipment-aware auto-resolve and troop power, and RBM's additions to the campaign screens. Off returns the campaign to the base game and every option in this section stops working. The screen additions only follow a change after a game restart. Default on.");

        [DataSourceProperty]
        public string CancelText
        {
            get
            {
                return new TextObject("{=3CpNUnVl}Cancel").ToString();
            }
        }

        [DataSourceProperty]
        public string DoneText
        {
            get
            {
                return new TextObject("{=WiNRdfsm}Done").ToString();
            }
        }

        [DataSourceProperty]
        public string ResetToDefaultText
        {
            get
            {
                return new TextObject("{=RBM_CON_062}Reset to Default").ToString();
            }
        }

        [DataSourceProperty]
        public string RBMCombatt
        {
            get
            {
                return new TextObject("{=RBM_CON_016}RBM Combat").ToString();
            }
        }

        [DataSourceProperty]
        public string RBMConft
        {
            get
            {
                return new TextObject("{=RBM_CON_020}RBM Configuration").ToString();
            }
        }

        [DataSourceProperty]
        public string ModuleStatust
        {
            get
            {
                return new TextObject("{=RBM_CON_017}Module Status").ToString();
            }
        }

        [DataSourceProperty]
        public string RBMAIt
        {
            get
            {
                return new TextObject("{=RBM_CON_018}RBM AI").ToString();
            }
        }

        [DataSourceProperty]
        public string RBMTournament
        {
            get
            {
                return new TextObject("{=RBM_CON_019}RBM Tournament").ToString();
            }
        }

        [DataSourceProperty]
        public string RBMCampaignt
        {
            get
            {
                return new TextObject("{=RBM_CFG_CAT_CAMPAIGN}RBM Campaign").ToString();
            }
        }

        public RBMConfigViewModel()
        {
            RefreshValues();
            List<string> troopOverhaulOnOff = new List<string> { new TextObject("{=RBM_CON_001}Inactive").ToString(), new TextObject("{=RBM_CON_002}Active (Recommended)").ToString(), };
            ActiveTroopOverhaulText = new TextViewModel(new TextObject("Troop Overhaul"));
            ActiveTroopOverhaul = new SelectorVM<SelectorItemVM>(troopOverhaulOnOff, 0, null);

            List<string> rangedReloadSpeed = new List<string> { new TextObject("{=RBM_CON_004}Vanilla").ToString(), new TextObject("{=RBM_CON_005}Realistic").ToString(), new TextObject("{=RBM_CON_006}Semi-realistic").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            RangedReloadSpeedText = new TextViewModel(new TextObject("Ranged reload speed"));
            RangedReloadSpeed = new SelectorVM<SelectorItemVM>(rangedReloadSpeed, 0, null);

            List<string> rangedReloadAffectsAi = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", new TextObject("{=tsPjK1Ke}Enabled").ToString(), };
            RangedReloadAffectsAiText = new TextViewModel(new TextObject("Ranged reload applies to AI"));
            RangedReloadAffectsAi = new SelectorVM<SelectorItemVM>(rangedReloadAffectsAi, 0, null);

            List<string> rangedAimArcEnabled = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", new TextObject("{=tsPjK1Ke}Enabled").ToString(), };
            RangedAimArcEnabledText = new TextViewModel(new TextObject("Ranged aim arc (player, experimental)"));
            RangedAimArcEnabled = new SelectorVM<SelectorItemVM>(rangedAimArcEnabled, 0, null);

            List<string> passiveShoulderShields = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", new TextObject("{=tsPjK1Ke}Enabled").ToString() };
            PassiveShoulderShieldsText = new TextViewModel(new TextObject("Passive Shoulder Shields"));
            PassiveShoulderShields = new SelectorVM<SelectorItemVM>(passiveShoulderShields, 0, null);

            List<string> betterArrowVisuals = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            BetterArrowVisualsText = new TextViewModel(new TextObject("Better Arrow Visuals"));
            BetterArrowVisuals = new SelectorVM<SelectorItemVM>(betterArrowVisuals, 0, null);

            List<string> sneakAttackInstaKill = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", new TextObject("{=tsPjK1Ke}Enabled").ToString() };
            SneakAttackInstaKillText = new TextViewModel(new TextObject("Sneak Attack Insta-Kill"));
            SneakAttackInstaKill = new SelectorVM<SelectorItemVM>(sneakAttackInstaKill, 0, null);

            List<string> armorStatusUIEnabled = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", };
            ArmorStatusUIEnabledText = new TextViewModel(new TextObject("Armor Status GUI"));
            ArmorStatusUIEnabled = new SelectorVM<SelectorItemVM>(armorStatusUIEnabled, 0, null);

            List<string> realisticArrowArc = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", new TextObject("{=tsPjK1Ke}Enabled").ToString(), };
            RealisticArrowArcText = new TextViewModel(new TextObject("Realistic Arrow Arc"));
            RealisticArrowArc = new SelectorVM<SelectorItemVM>(realisticArrowArc, 0, null);

            ThrustModifierText = new TextViewModel(new TextObject("Thrust Modifier"));
            ThrustModifier = new SelectorVM<SelectorItemVM>(thrustModifierList, 0, null);

            _armorEffectiveness = MathF.Clamp(RBMConfig.armorEffectivenessMultiplier, 0.25f, 3f);
            _armorMultiplier = MathF.Clamp(RBMConfig.armorMultiplier, 0.5f, 4f);
            _bluntTraumaMultiplier = MathF.Clamp(RBMConfig.bluntTraumaMultiplier, 0f, 3f);
            _armorThresholdModifier = MathF.Clamp(RBMConfig.armorThresholdModifier, 0f, 3f);
            _arrowThicknessScale = MathF.Clamp(RBMConfig.arrowThicknessScale, 1f, 10f);
            _stuckThrownFallOutSeconds = MathF.Clamp(RBMConfig.stuckThrownFallOutSeconds, 0f, 60f);
            _stuckArrowFallOutSeconds = MathF.Clamp(RBMConfig.stuckArrowFallOutSeconds, 0f, 300f);

            int i = 0;
            foreach (var item in thrustModifierList)
            {
                if(RBMConfig.ParseFloat(item) == RBMConfig.ThrustMagnitudeModifier)
                {
                    ThrustModifier.SelectedIndex = i;
                    break;
                }
                i++;
            }

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

            RangedReloadAffectsAi.SelectedIndex = RBMConfig.rangedReloadAffectsAi ? 1 : 0;

            RangedAimArcEnabled.SelectedIndex = RBMConfig.rangedAimArcEnabled ? 1 : 0;

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

            SneakAttackInstaKill.SelectedIndex = RBMConfig.sneakAttackInstaKill ? 1 : 0;

            if (RBMConfig.armorStatusUIEnabled)
            {
                ArmorStatusUIEnabled.SelectedIndex = 1;
            }
            else
            {
                ArmorStatusUIEnabled.SelectedIndex = 0;
            }

            if (RBMConfig.realisticArrowArc)
            {
                RealisticArrowArc.SelectedIndex = 1;
            }
            else
            {
                RealisticArrowArc.SelectedIndex = 0;
            }

            List<string> hitStopOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            HitStopEnabledText = new TextViewModel(new TextObject("Slow Motion in Combat"));
            HitStopEnabled = new SelectorVM<SelectorItemVM>(hitStopOptions, 0, null);

            List<string> postureOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            PostureSystemEnabledText = new TextViewModel(new TextObject("Posture System"));
            PostureSystemEnabled = new SelectorVM<SelectorItemVM>(postureOptions, 0, OnPostureSystemChanged);

            List<string> staminaOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            StaminaSystemEnabledText = new TextViewModel(new TextObject("Stamina System"));
            StaminaSystemEnabled = new SelectorVM<SelectorItemVM>(staminaOptions, 0, null);

            List<string> aiKickBashOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            AiKickBashEnabledText = new TextViewModel(new TextObject("AI Kick and Bash"));
            AiKickBashEnabled = new SelectorVM<SelectorItemVM>(aiKickBashOptions, 0, null);

            List<string> playerPostureMultiplierOptions = new List<string> { "1x (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", "1.5x", "2x" };
            PlayerPostureMultiplierText = new TextViewModel(new TextObject("Player Posture Multiplier"));
            PlayerPostureMultiplier = new SelectorVM<SelectorItemVM>(playerPostureMultiplierOptions, 0, null);

            List<string> postureGUIOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            PostureGUIEnabledText = new TextViewModel(new TextObject("Posture GUI"));
            PostureGUIEnabled = new SelectorVM<SelectorItemVM>(postureGUIOptions, 0, null);

            List<string> vanillaCombatAiOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", new TextObject("{=tsPjK1Ke}Enabled").ToString() };
            VanillaCombatAiText = new TextViewModel(new TextObject("Vanilla AI Block/Parry/Attack"));
            VanillaCombatAi = new SelectorVM<SelectorItemVM>(vanillaCombatAiOptions, 0, null);

            List<string> keepBattleOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", new TextObject("{=tsPjK1Ke}Enabled").ToString() };
            KeepBattleEnabledText = new TextViewModel(new TextObject("Keep Battle (Last Stand)"));
            KeepBattleEnabled = new SelectorVM<SelectorItemVM>(keepBattleOptions, 0, null);

            List<string> frontlineOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            FrontlineEnabledText = new TextViewModel(new TextObject("Frontline System"));
            FrontlineEnabled = new SelectorVM<SelectorItemVM>(frontlineOptions, 0, OnFrontlineEnabledChanged);
            FrontlineEnabled.SelectedIndex = RBMConfig.frontlineEnabled ? 1 : 0;
            _frontlineMinFormationSize = MathF.Clamp(RBMConfig.frontlineMinFormationSize, 0f, 200f);
            _frontlineDecisionTimerMax = MathF.Clamp(RBMConfig.frontlineDecisionTimerMax, 0f, 10f);
            _frontlineAttackWeight = MathF.Clamp(RBMConfig.frontlineAttackWeight, 0f, 3f);
            _frontlineBackStepWeight = MathF.Clamp(RBMConfig.frontlineBackStepWeight, 0f, 3f);
            _frontlineFindAllyWeight = MathF.Clamp(RBMConfig.frontlineFindAllyWeight, 0f, 3f);
            _frontlineFlankWeight = MathF.Clamp(RBMConfig.frontlineFlankWeight, 0f, 3f);

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

            HitStopEnabled.SelectedIndex = RBMConfig.hitStopEnabled ? 1 : 0;

            if (RBMConfig.postureEnabled)
            {
                PostureSystemEnabled.SelectedIndex = 1;
            }
            else
            {
                PostureSystemEnabled.SelectedIndex = 0;
            }

            StaminaSystemEnabled.SelectedIndex = RBMConfig.staminaEnabled ? 1 : 0;

            AiKickBashEnabled.SelectedIndex = RBMConfig.aiKickBashEnabled ? 1 : 0;

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

            if (RBMConfig.keepBattleEnabled)
            {
                KeepBattleEnabled.SelectedIndex = 1;
            }
            else
            {
                KeepBattleEnabled.SelectedIndex = 0;
            }

            List<string> rbmCombatEnabledOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            RBMCombatEnabledText = new TextViewModel(new TextObject("{=RBM_CON_016}RBM Combat"));
            RBMCombatEnabled = new SelectorVM<SelectorItemVM>(rbmCombatEnabledOptions, 0, null);

            List<string> rbmAiEnabledOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            RBMAIEnabledText = new TextViewModel(new TextObject("{=RBM_CON_018}RBM AI"));
            RBMAIEnabled = new SelectorVM<SelectorItemVM>(rbmAiEnabledOptions, 0, null);

            List<string> rbmTournamentEnabledOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            RBMTournamentEnabledText = new TextViewModel(new TextObject("{=RBM_CON_019}RBM Tournament"));
            RBMTournamentEnabled = new SelectorVM<SelectorItemVM>(rbmTournamentEnabledOptions, 0, null);

            // No LOC key, matching the RBMCampaignt section title this name is shared with.
            List<string> rbmCampaignEnabledOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            RBMCampaignEnabledText = new TextViewModel(new TextObject("RBM Campaign"));
            RBMCampaignEnabled = new SelectorVM<SelectorItemVM>(rbmCampaignEnabledOptions, 0, null);

            // Inventory weight column: on by default, so Enabled carries the "(Default)" tag.
            List<string> showInventoryItemWeightOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            ShowInventoryItemWeightText = new TextViewModel(new TextObject("{=RBM_CON_105}Inventory Weight Column"));
            ShowInventoryItemWeight = new SelectorVM<SelectorItemVM>(showInventoryItemWeightOptions, 0, null);

            // AI lord equipment purchasing: on by default, so Enabled carries the "(Default)" tag.
            List<string> lordEquipmentUpgradeOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            LordEquipmentUpgradeEnabledText = new TextViewModel(new TextObject("AI Lords Buy Equipment"));
            LordEquipmentUpgradeEnabled = new SelectorVM<SelectorItemVM>(lordEquipmentUpgradeOptions, 0, null);

            // Intra-kingdom supply caravans and their two sub-toggles: all on by default, so Enabled
            // carries the "(Default)" tag.
            List<string> kingdomCaravansOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            KingdomCaravansEnabledText = new TextViewModel(new TextObject("Kingdom Supply Caravans"));
            KingdomCaravansEnabled = new SelectorVM<SelectorItemVM>(kingdomCaravansOptions, 0, null);

            List<string> caravanInvestmentOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            CaravanInvestmentEnabledText = new TextViewModel(new TextObject("Caravan Investment"));
            CaravanInvestmentEnabled = new SelectorVM<SelectorItemVM>(caravanInvestmentOptions, 0, null);

            // Deserter raider AI: on by default, so Enabled carries the "(Default)" tag.
            List<string> deserterRaidersOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            DeserterRaidersEnabledText = new TextViewModel(new TextObject("Deserter Raiders"));
            DeserterRaidersEnabled = new SelectorVM<SelectorItemVM>(deserterRaidersOptions, 0, null);

            // Equipment simulation: Enabled is the default, so its option carries the "(Default)" tag.
            List<string> simulationEquipmentOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            SimulationEquipmentEnabledText = new TextViewModel(new TextObject("{=RBM_CON_093}Detailed Auto Resolve"));
            SimulationEquipmentEnabled = new SelectorVM<SelectorItemVM>(simulationEquipmentOptions, 0, null);

            // Auto resolve routing: off by default, so Disabled carries the "(Default)" tag.
            List<string> simulationRoutOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", new TextObject("{=tsPjK1Ke}Enabled").ToString() };
            SimulationRoutEnabledText = new TextViewModel(new TextObject("{=RBM_CON_096}Auto Resolve Routing"));
            SimulationRoutEnabled = new SelectorVM<SelectorItemVM>(simulationRoutOptions, 0, null);

            // Equipment based troop power: on by default, so Enabled carries the "(Default)" tag.
            List<string> strategicPowerOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            StrategicPowerEnabledText = new TextViewModel(new TextObject("{=RBM_CON_098}Equipment Based Troop Power"));
            StrategicPowerEnabled = new SelectorVM<SelectorItemVM>(strategicPowerOptions, 0, null);

            // Auto resolve perks: on by default, so its option carries the "(Default)" tag.
            List<string> simulationPerkOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            SimulationPerkSystemText = new TextViewModel(new TextObject("{=RBM_CON_097}Auto Resolve Perks"));
            SimulationPerkSystem = new SelectorVM<SelectorItemVM>(simulationPerkOptions, 0, null);

            // Watching an AI battle: off by default -- it is an instrument, not a way to play -- so Disabled carries
            // the "(Default)" tag.
            List<string> spectateBattlesOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", new TextObject("{=tsPjK1Ke}Enabled").ToString() };
            SpectateBattlesEnabledText = new TextViewModel(new TextObject("{=RBM_CON_099}Spectate AI Battles"));
            SpectateBattlesEnabled = new SelectorVM<SelectorItemVM>(spectateBattlesOptions, 0, null);

            // SupplyTown gate: Enabled is the default, so its option carries the "(Default)" tag.
            List<string> troopUpgradeRequireSupplyTownOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            TroopUpgradeRequireSupplyTownText = new TextViewModel(new TextObject("{=RBM_CON_070}Upgrade Near Town"));
            TroopUpgradeRequireSupplyTown = new SelectorVM<SelectorItemVM>(troopUpgradeRequireSupplyTownOptions, 0, null);

            // RecruitSupply draw: Enabled is the default, so its option carries the "(Default)" tag.
            List<string> recruitDrawsFromSettlementStockOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            RecruitDrawsFromSettlementStockText = new TextViewModel(new TextObject("{=RBM_CON_128}Recruits Kitted From Market"));
            RecruitDrawsFromSettlementStock = new SelectorVM<SelectorItemVM>(recruitDrawsFromSettlementStockOptions, 0, null);

            // Charge mount in gold: Enabled is the default, so its option carries the "(Default)" tag.
            List<string> troopUpgradeChargeMountValueOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            TroopUpgradeChargeMountValueText = new TextViewModel(new TextObject("{=RBM_CON_087}Buy Mounts For Upgrades"));
            TroopUpgradeChargeMountValue = new SelectorVM<SelectorItemVM>(troopUpgradeChargeMountValueOptions, 0, null);

            // RBM Debug & Logging (declared in RBMConfigViewModel.Debug.cs). Every one of these is off by default,
            // so Disabled carries the "(Default)" tag throughout.
            List<string> debugToggleOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")", new TextObject("{=tsPjK1Ke}Enabled").ToString() };
            DeveloperModeText = new TextViewModel(new TextObject("{=RBM_DBG_002}Developer Mode"));
            DeveloperMode = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.developerMode ? 1 : 0, null);
            BattleHitLoggingEnabledText = new TextViewModel(new TextObject("{=RBM_CON_095}Field Battle Logging"));
            BattleHitLoggingEnabled = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.battleHitLoggingEnabled ? 1 : 0, null);
            AiBehaviorLogEnabledText = new TextViewModel(new TextObject("{=RBM_DBG_004}AI Behavior Logging"));
            AiBehaviorLogEnabled = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.aiBehaviorLogEnabled ? 1 : 0, null);
            ArmorPenetrationMessageText = new TextViewModel(new TextObject("{=RBM_DBG_006}Armor Penetration Messages"));
            ArmorPenetrationMessage = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.armorPenetrationMessage ? 1 : 0, null);
            SimulationLoggingEnabledText = new TextViewModel(new TextObject("{=RBM_CON_094}Detailed Auto Resolve Logging"));
            SimulationLoggingEnabled = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.simulationLoggingEnabled ? 1 : 0, null);
            SimulationLogHitsText = new TextViewModel(new TextObject("Auto Resolve Per-Hit Detail"));
            SimulationLogHits = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.simulationLogHits ? 1 : 0, null);
            StrategicPowerLoggingEnabledText = new TextViewModel(new TextObject("Troop Power Logging"));
            StrategicPowerLoggingEnabled = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.strategicPowerLoggingEnabled ? 1 : 0, null);
            SpoilsLoggingEnabledText = new TextViewModel(new TextObject("{=RBM_CON_039}Spoils Logging"));
            SpoilsLoggingEnabled = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.spoilsLoggingEnabled ? 1 : 0, null);
            SpoilsVerboseLoggingEnabledText = new TextViewModel(new TextObject("{=RBM_CON_049}Verbose Logging"));
            SpoilsVerboseLoggingEnabled = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.spoilsVerboseLoggingEnabled ? 1 : 0, null);
            EconomyLoggingEnabledText = new TextViewModel(new TextObject("{=RBM_CON_107}Economy Logging"));
            EconomyLoggingEnabled = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.economyLoggingEnabled ? 1 : 0, null);
            CaravanLoggingEnabledText = new TextViewModel(new TextObject("Caravan Logging"));
            CaravanLoggingEnabled = new SelectorVM<SelectorItemVM>(debugToggleOptions, RBMConfig.caravanLoggingEnabled ? 1 : 0, null);

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

            if (RBMConfig.rbmTournamentEnabled)
            {
                RBMTournamentEnabled.SelectedIndex = 1;
            }
            else
            {
                RBMTournamentEnabled.SelectedIndex = 0;
            }

            if (RBMConfig.rbmCampaignEnabled)
            {
                RBMCampaignEnabled.SelectedIndex = 1;
            }
            else
            {
                RBMCampaignEnabled.SelectedIndex = 0;
            }

            _campaignStartingGoldMultiplier = MathF.Clamp(RBMConfig.campaignStartingGoldMultiplier, 1f, 50f);
            _troopUpgradeCostMultiplier = MathF.Clamp(RBMConfig.troopUpgradeCostMultiplier, 0f, 2f);
            _villageProductionMultiplier = MathF.Clamp(RBMConfig.villageProductionMultiplier, 0.01f, 2f);
            _workshopProductionMultiplier = MathF.Clamp(RBMConfig.workshopProductionMultiplier, 0.01f, 2f);
            _troopUpgradeSpoilsLootMultiplier = MathF.Clamp(RBMConfig.troopUpgradeSpoilsLootMultiplier, 0f, 5f);
            _troopUpgradeSupplyRadius = MathF.Clamp(RBMConfig.troopUpgradeSupplyRadius, 0f, 200f);
            TroopUpgradeRequireSupplyTown.SelectedIndex = RBMConfig.troopUpgradeRequireSupplyTown ? 1 : 0;
            RecruitDrawsFromSettlementStock.SelectedIndex = RBMConfig.recruitDrawsFromSettlementStock ? 1 : 0;
            TroopUpgradeChargeMountValue.SelectedIndex = RBMConfig.troopUpgradeChargeMountValue ? 1 : 0;
            _troopLootPiecesPerMan = MathF.Clamp(RBMConfig.troopLootPiecesPerMan, 1f, 10f);
            _troopLootOverlookChancePerTier = MathF.Clamp(RBMConfig.troopLootOverlookChancePerTier, 0f, 1f);
            _troopMaintenanceFraction = MathF.Clamp(RBMConfig.troopMaintenanceFraction, 0f, 0.05f);
            _independentMaintenancePurseFraction = MathF.Clamp(RBMConfig.independentMaintenancePurseFraction, 0f, 1f);
            _troopSettlementFoodDays = MathF.Clamp(RBMConfig.troopSettlementFoodDays, 0f, 60f);
            _recruitMaintenanceDays = MathF.Clamp(RBMConfig.recruitMaintenanceDays, 0f, 30f);
            _troopFoodWageFraction = MathF.Clamp(RBMConfig.troopFoodWageFraction, 0f, 10f);
            _troopSettlementFunWageFraction = MathF.Clamp(RBMConfig.troopSettlementFunWageFraction, 0f, 10f);
            ShowInventoryItemWeight.SelectedIndex = RBMConfig.showInventoryItemWeight ? 1 : 0;
            LordEquipmentUpgradeEnabled.SelectedIndex = RBMConfig.lordEquipmentUpgradeEnabled ? 1 : 0;
            KingdomCaravansEnabled.SelectedIndex = RBMConfig.kingdomCaravansEnabled ? 1 : 0;
            CaravanInvestmentEnabled.SelectedIndex = RBMConfig.caravanInvestmentEnabled ? 1 : 0;
            DeserterRaidersEnabled.SelectedIndex = RBMConfig.deserterRaidersEnabled ? 1 : 0;
            SimulationEquipmentEnabled.SelectedIndex = RBMConfig.simulationEquipmentEnabled ? 1 : 0;
            SimulationRoutEnabled.SelectedIndex = RBMConfig.simulationRoutEnabled ? 1 : 0;
            StrategicPowerEnabled.SelectedIndex = RBMConfig.strategicPowerEnabled ? 1 : 0;
            SimulationPerkSystem.SelectedIndex = RBMConfig.simulationPerkSystem ? 1 : 0;
            SpectateBattlesEnabled.SelectedIndex = RBMConfig.spectateBattlesEnabled ? 1 : 0;
            _spectateMinTroopsPerSide = MathF.Clamp(RBMConfig.spectateMinTroopsPerSide, 10f, 1000f);
            _troopLeaderSpoilsCutFraction = MathF.Clamp(RBMConfig.troopLeaderSpoilsCutFraction, 0f, 1f);
            _troopSpoilsCapDays = MathF.Clamp(RBMConfig.troopSpoilsCapDays, 0f, 60f);
            _buildingCostMultiplier = MathF.Clamp(RBMConfig.buildingCostMultiplier, 1f, 1000f);
            _constructionBudgetShare = MathF.Clamp(RBMConfig.constructionBudgetShare, 0f, 0.1f);
            _troopLuxuryCooldownDays = MathF.Clamp(RBMConfig.troopLuxuryCooldownDays, 0f, 120f);
            _troopLuxurySpendChance = MathF.Clamp(RBMConfig.troopLuxurySpendChance, 0f, 1f);
            _troopFallenSpoilsCaptureFraction = MathF.Clamp(RBMConfig.troopFallenSpoilsCaptureFraction, 0f, 1f);
            _troopSpoilsHealGoldPerTier = MathF.Clamp(RBMConfig.troopSpoilsHealGoldPerTier, 0f, 100f);
            _troopSpoilsHealFractionPerHour = MathF.Clamp(RBMConfig.troopSpoilsHealFractionPerHour, 0f, 1f);
        }

        /// <summary>
        /// A hover tooltip carrying the full explanation a shortened option label leaves out. The
        /// localized string is resolved lazily, each time the tooltip is shown, so a language switch
        /// while the screen is open is honoured. Paired in the prefab with a HintWidget overlaying the
        /// label, which relays its parent's hover to this view model.
        /// </summary>
        private static BasicTooltipViewModel Hint(string localizedText)
        {
            return new BasicTooltipViewModel((Func<string>)(() => new TextObject(localizedText).ToString()));
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

            RBMConfig.rangedReloadAffectsAi = RangedReloadAffectsAi.SelectedIndex == 1;

            RBMConfig.rangedAimArcEnabled = RangedAimArcEnabled.SelectedIndex == 1;

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
            if (BetterArrowVisuals.SelectedIndex == 1)
            {
                RBMConfig.betterArrowVisuals = true;
            }

            RBMConfig.sneakAttackInstaKill = SneakAttackInstaKill.SelectedIndex == 1;

            if (ArmorStatusUIEnabled.SelectedIndex == 0)
            {
                RBMConfig.armorStatusUIEnabled = false;
            }
            if (ArmorStatusUIEnabled.SelectedIndex == 1)
            {
                RBMConfig.armorStatusUIEnabled = true;
            }

            if (RealisticArrowArc.SelectedIndex == 0)
            {
                RBMConfig.realisticArrowArc = false;
            }
            if (RealisticArrowArc.SelectedIndex == 1)
            {
                RBMConfig.realisticArrowArc = true;
            }

            var newThrustModifier = RBMConfig.ParseFloat(thrustModifierList[ThrustModifier.SelectedIndex]);
            RBMConfig.ThrustMagnitudeModifier = newThrustModifier;
            RBMConfig.OneHandedThrustDamageBonus = 1f / newThrustModifier;
            RBMConfig.TwoHandedThrustDamageBonus = 1f / newThrustModifier;

            RBMConfig.armorEffectivenessMultiplier = _armorEffectiveness;
            RBMConfig.armorMultiplier = _armorMultiplier;
            RBMConfig.bluntTraumaMultiplier = _bluntTraumaMultiplier;
            RBMConfig.armorThresholdModifier = _armorThresholdModifier;
            RBMConfig.arrowThicknessScale = _arrowThicknessScale;
            RBMConfig.stuckThrownFallOutSeconds = _stuckThrownFallOutSeconds;
            RBMConfig.stuckArrowFallOutSeconds = _stuckArrowFallOutSeconds;

            RBMConfig.hitStopEnabled = HitStopEnabled.SelectedIndex == 1;

            if (PostureSystemEnabled.SelectedIndex == 0)
            {
                RBMConfig.postureEnabled = false;
            }
            if (PostureSystemEnabled.SelectedIndex == 1)
            {
                RBMConfig.postureEnabled = true;
            }

            RBMConfig.staminaEnabled = StaminaSystemEnabled.SelectedIndex == 1;

            RBMConfig.aiKickBashEnabled = AiKickBashEnabled.SelectedIndex == 1;

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
            if (VanillaCombatAi.SelectedIndex == 1)
            {
                RBMConfig.vanillaCombatAi = true;
            }

            if (KeepBattleEnabled.SelectedIndex == 0)
            {
                RBMConfig.keepBattleEnabled = false;
            }
            if (KeepBattleEnabled.SelectedIndex == 1)
            {
                RBMConfig.keepBattleEnabled = true;
            }

            RBMConfig.frontlineEnabled = FrontlineEnabled.SelectedIndex == 1;
            RBMConfig.frontlineMinFormationSize = (int)_frontlineMinFormationSize;
            RBMConfig.frontlineDecisionTimerMax = _frontlineDecisionTimerMax;
            RBMConfig.frontlineAttackWeight = _frontlineAttackWeight;
            RBMConfig.frontlineBackStepWeight = _frontlineBackStepWeight;
            RBMConfig.frontlineFindAllyWeight = _frontlineFindAllyWeight;
            RBMConfig.frontlineFlankWeight = _frontlineFlankWeight;

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

            if (RBMCampaignEnabled.SelectedIndex == 0)
            {
                RBMConfig.rbmCampaignEnabled = false;
            }
            if (RBMCampaignEnabled.SelectedIndex == 1)
            {
                RBMConfig.rbmCampaignEnabled = true;
            }

            RBMConfig.campaignStartingGoldMultiplier = _campaignStartingGoldMultiplier;
            RBMConfig.troopUpgradeCostMultiplier = _troopUpgradeCostMultiplier;
            RBMConfig.villageProductionMultiplier = _villageProductionMultiplier;
            RBMConfig.workshopProductionMultiplier = _workshopProductionMultiplier;
            RBMConfig.troopUpgradeSpoilsLootMultiplier = _troopUpgradeSpoilsLootMultiplier;
            RBMConfig.troopUpgradeSupplyRadius = _troopUpgradeSupplyRadius;
            RBMConfig.troopUpgradeRequireSupplyTown = TroopUpgradeRequireSupplyTown.SelectedIndex == 1;
            RBMConfig.recruitDrawsFromSettlementStock = RecruitDrawsFromSettlementStock.SelectedIndex == 1;
            RBMConfig.troopUpgradeChargeMountValue = TroopUpgradeChargeMountValue.SelectedIndex == 1;
            RBMConfig.troopLootPiecesPerMan = MathF.Round(_troopLootPiecesPerMan);
            RBMConfig.troopLootOverlookChancePerTier = _troopLootOverlookChancePerTier;
            RBMConfig.troopMaintenanceFraction = _troopMaintenanceFraction;
            RBMConfig.independentMaintenancePurseFraction = _independentMaintenancePurseFraction;
            RBMConfig.troopSettlementFoodDays = (int)MathF.Round(_troopSettlementFoodDays);
            RBMConfig.recruitMaintenanceDays = (int)MathF.Round(_recruitMaintenanceDays);
            RBMConfig.troopFoodWageFraction = _troopFoodWageFraction;
            RBMConfig.troopSettlementFunWageFraction = _troopSettlementFunWageFraction;
            RBMConfig.showInventoryItemWeight = ShowInventoryItemWeight.SelectedIndex == 1;
            RBMConfig.lordEquipmentUpgradeEnabled = LordEquipmentUpgradeEnabled.SelectedIndex == 1;
            RBMConfig.kingdomCaravansEnabled = KingdomCaravansEnabled.SelectedIndex == 1;
            RBMConfig.caravanInvestmentEnabled = CaravanInvestmentEnabled.SelectedIndex == 1;
            RBMConfig.deserterRaidersEnabled = DeserterRaidersEnabled.SelectedIndex == 1;
            RBMConfig.simulationEquipmentEnabled = SimulationEquipmentEnabled.SelectedIndex == 1;
            RBMConfig.simulationRoutEnabled = SimulationRoutEnabled.SelectedIndex == 1;
            RBMConfig.strategicPowerEnabled = StrategicPowerEnabled.SelectedIndex == 1;
            RBMConfig.simulationPerkSystem = SimulationPerkSystem.SelectedIndex == 1;
            RBMConfig.spectateBattlesEnabled = SpectateBattlesEnabled.SelectedIndex == 1;
            RBMConfig.spectateMinTroopsPerSide = (int)MathF.Round(_spectateMinTroopsPerSide);
            RBMConfig.troopLeaderSpoilsCutFraction = _troopLeaderSpoilsCutFraction;
            RBMConfig.troopSpoilsCapDays = (int)MathF.Round(_troopSpoilsCapDays);
            RBMConfig.buildingCostMultiplier = (int)MathF.Round(_buildingCostMultiplier);
            RBMConfig.constructionBudgetShare = _constructionBudgetShare;
            RBMConfig.troopLuxuryCooldownDays = (int)MathF.Round(_troopLuxuryCooldownDays);
            RBMConfig.troopLuxurySpendChance = _troopLuxurySpendChance;
            RBMConfig.troopFallenSpoilsCaptureFraction = _troopFallenSpoilsCaptureFraction;
            RBMConfig.troopSpoilsHealGoldPerTier = (int)MathF.Round(_troopSpoilsHealGoldPerTier);
            RBMConfig.troopSpoilsHealFractionPerHour = _troopSpoilsHealFractionPerHour;

            // Debug & Logging
            RBMConfig.developerMode = DeveloperMode.SelectedIndex == 1;
            RBMConfig.battleHitLoggingEnabled = BattleHitLoggingEnabled.SelectedIndex == 1;
            RBMConfig.aiBehaviorLogEnabled = AiBehaviorLogEnabled.SelectedIndex == 1;
            RBMConfig.armorPenetrationMessage = ArmorPenetrationMessage.SelectedIndex == 1;
            RBMConfig.simulationLoggingEnabled = SimulationLoggingEnabled.SelectedIndex == 1;
            RBMConfig.simulationLogHits = SimulationLogHits.SelectedIndex == 1;
            RBMConfig.strategicPowerLoggingEnabled = StrategicPowerLoggingEnabled.SelectedIndex == 1;
            RBMConfig.spoilsLoggingEnabled = SpoilsLoggingEnabled.SelectedIndex == 1;
            RBMConfig.spoilsVerboseLoggingEnabled = SpoilsVerboseLoggingEnabled.SelectedIndex == 1;
            RBMConfig.economyLoggingEnabled = EconomyLoggingEnabled.SelectedIndex == 1;
            RBMConfig.caravanLoggingEnabled = CaravanLoggingEnabled.SelectedIndex == 1;

            RBMConfig.saveXmlConfig();
            TaleWorlds.ScreenSystem.ScreenManager.PopScreen();

            // RBM's spear combat parameters are read by the engine once per launch, so they alone lag the
            // Combat toggle until a restart; everything else follows it at the next game start.
            if (RBMConfig.rbmCombatEnabledAtLaunch.HasValue && RBMConfig.rbmCombatEnabledAtLaunch.Value != RBMConfig.rbmCombatEnabled)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    new TextObject("{=RBM_RESTART_001}Restart Required").ToString(),
                    new TextObject("{=RBM_RESTART_002}RBM Combat was changed. Everything else takes effect the next time you start or load a game, but RBM's spear and weapon animation parameters are only read when Bannerlord starts. Restart the game for them to match the new setting.").ToString(),
                    true, false,
                    new TextObject("{=RBM_RESTART_003}OK").ToString(), null,
                    null, null));
            }
        }

        /// <summary>
        /// Restores every control to the mod's shipped default, matching the field defaults in
        /// <see cref="RBMConfig"/>. Only the on-screen controls are touched; nothing is persisted until
        /// the player presses Done, so a reset can still be abandoned with Cancel.
        /// </summary>
        private void ExecuteResetToDefault()
        {
            // Combat
            ThrustModifier.SelectedIndex = thrustModifierList.IndexOf(new TextObject("0.05").ToString());
            ArmorEffectiveness = 1f;
            ArmorMultiplier = 2f;
            BluntTraumaMultiplier = 1f;
            ArmorThresholdModifier = 1f;
            RealisticArrowArc.SelectedIndex = 0;
            ArmorStatusUIEnabled.SelectedIndex = 1;
            SneakAttackInstaKill.SelectedIndex = 0;
            BetterArrowVisuals.SelectedIndex = 1;
            ArrowThicknessScale = 1f;
            StuckThrownFallOutSeconds = 5f;
            StuckArrowFallOutSeconds = 45f;
            PassiveShoulderShields.SelectedIndex = 0;
            RangedReloadSpeed.SelectedIndex = 2;
            RangedReloadAffectsAi.SelectedIndex = 0;
            RangedAimArcEnabled.SelectedIndex = 0;
            ActiveTroopOverhaul.SelectedIndex = 1;
            RBMCombatEnabled.SelectedIndex = 1;

            // AI
            VanillaCombatAi.SelectedIndex = 0;
            KeepBattleEnabled.SelectedIndex = 0;
            PostureGUIEnabled.SelectedIndex = 1;
            PlayerPostureMultiplier.SelectedIndex = 0;
            PostureSystemEnabled.SelectedIndex = 1;
            StaminaSystemEnabled.SelectedIndex = 1;
            AiKickBashEnabled.SelectedIndex = 1;
            HitStopEnabled.SelectedIndex = 1;
            RBMAIEnabled.SelectedIndex = 1;
            FrontlineEnabled.SelectedIndex = 1;
            FrontlineMinFormationSize = 25f;
            FrontlineDecisionTimerMax = 2f;
            FrontlineAttackWeight = 1f;
            FrontlineBackStepWeight = 1f;
            FrontlineFindAllyWeight = 1f;
            FrontlineFlankWeight = 1f;

            // Modules
            RBMTournamentEnabled.SelectedIndex = 1;
            RBMCampaignEnabled.SelectedIndex = 1;

            // Campaign / spoils
            CampaignStartingGoldMultiplier = 5f;
            TroopUpgradeCostMultiplier = 1f;
            VillageProductionMultiplier = 0.5f;
            WorkshopProductionMultiplier = 1f;
            TroopUpgradeSpoilsLootMultiplier = 1f;
            TroopUpgradeSupplyRadius = 30f;
            TroopUpgradeRequireSupplyTown.SelectedIndex = 1;
            RecruitDrawsFromSettlementStock.SelectedIndex = 1;
            TroopUpgradeChargeMountValue.SelectedIndex = 1;
            TroopLootPiecesPerMan = 3f;
            TroopLootOverlookChancePerTier = 0.5f;
            TroopMaintenanceFraction = 0.005f;
            IndependentMaintenancePurseFraction = 1f;
            TroopSettlementFoodDays = 20f;
            RecruitMaintenanceDays = 20f;
            TroopFoodWageFraction = 0.5f;
            TroopSettlementFunWageFraction = 0.25f;
            TroopLeaderSpoilsCutFraction = 0.05f;
            TroopSpoilsCapDays = 20f;
            BuildingCostMultiplier = 250f;
            ConstructionBudgetShare = 0.01f;
            TroopLuxuryCooldownDays = 20f;
            TroopLuxurySpendChance = 0.02f;
            TroopFallenSpoilsCaptureFraction = 0.75f;
            TroopSpoilsHealGoldPerTier = 10f;
            TroopSpoilsHealFractionPerHour = 0.05f;
            ShowInventoryItemWeight.SelectedIndex = 1;
            LordEquipmentUpgradeEnabled.SelectedIndex = 1;
            KingdomCaravansEnabled.SelectedIndex = 1;
            CaravanInvestmentEnabled.SelectedIndex = 1;
            DeserterRaidersEnabled.SelectedIndex = 1;
            SimulationEquipmentEnabled.SelectedIndex = 1;
            SimulationRoutEnabled.SelectedIndex = 0;
            StrategicPowerEnabled.SelectedIndex = 1;
            SimulationPerkSystem.SelectedIndex = 1;
            SpectateBattlesEnabled.SelectedIndex = 0;
            SpectateMinTroopsPerSide = 100f;

            // Debug & Logging: all off
            DeveloperMode.SelectedIndex = 0;
            BattleHitLoggingEnabled.SelectedIndex = 0;
            AiBehaviorLogEnabled.SelectedIndex = 0;
            ArmorPenetrationMessage.SelectedIndex = 0;
            SimulationLoggingEnabled.SelectedIndex = 0;
            SimulationLogHits.SelectedIndex = 0;
            StrategicPowerLoggingEnabled.SelectedIndex = 0;
            SpoilsLoggingEnabled.SelectedIndex = 0;
            SpoilsVerboseLoggingEnabled.SelectedIndex = 0;
            EconomyLoggingEnabled.SelectedIndex = 0;
            CaravanLoggingEnabled.SelectedIndex = 0;
        }

        private void ExecuteCancel()
        {
            TaleWorlds.ScreenSystem.ScreenManager.PopScreen();
        }
    }
}
