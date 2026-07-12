// CunningLords.Interaction.CunningLordsMenuViewModel
using System;
using System.Collections.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMConfig
{
    internal class RBMConfigViewModel : ViewModel
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

        public SelectorVM<SelectorItemVM> RBMCombatEnabled { get; }

        public SelectorVM<SelectorItemVM> RBMAIEnabled { get; }

        public SelectorVM<SelectorItemVM> RBMTournamentEnabled { get; }

        public SelectorVM<SelectorItemVM> RBMCampaignEnabled { get; }

        public TextViewModel SpoilsLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> SpoilsLoggingEnabled { get; }

        public TextViewModel SpoilsVerboseLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> SpoilsVerboseLoggingEnabled { get; }

        // SupplyTown gate: on/off toggle for gating upgrades on a nearby friendly town.
        public TextViewModel TroopUpgradeRequireSupplyTownText { get; }
        public SelectorVM<SelectorItemVM> TroopUpgradeRequireSupplyTown { get; }

        private float _troopUpgradeCostMultiplier;

        [DataSourceProperty]
        public float TroopUpgradeCostMultiplier
        {
            get
            {
                return _troopUpgradeCostMultiplier;
            }
            set
            {
                // Slider reports continuous values; snap to 0.01 steps. Zero is meaningful -- it makes
                // upgrades free and turns the spoils system off with them -- so the floor is 0, not 0.01.
                float snapped = (float)System.Math.Round(value, 2);
                snapped = MathF.Clamp(snapped, 0f, 2f);
                if (snapped != _troopUpgradeCostMultiplier)
                {
                    _troopUpgradeCostMultiplier = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopUpgradeCostMultiplier");
                    OnPropertyChanged("TroopUpgradeCostMultiplierValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopUpgradeCostMultiplierValue
        {
            get
            {
                return _troopUpgradeCostMultiplier.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopUpgradeCostt
        {
            get
            {
                return new TextObject("{=RBM_CON_032}Troop Upgrade Cost").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopUpgradeCostHint { get; } = Hint("{=RBM_CON_050}Multiplier on the gold-and-spoils cost to upgrade a troop. 0 turns the spoils system off and makes upgrades free. Default 1.00.");

        private float _troopUpgradeSpoilsLootMultiplier;

        [DataSourceProperty]
        public float TroopUpgradeSpoilsLootMultiplier
        {
            get
            {
                return _troopUpgradeSpoilsLootMultiplier;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 5f);
                if (snapped != _troopUpgradeSpoilsLootMultiplier)
                {
                    _troopUpgradeSpoilsLootMultiplier = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopUpgradeSpoilsLootMultiplier");
                    OnPropertyChanged("TroopUpgradeSpoilsLootMultiplierValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopUpgradeSpoilsLootMultiplierValue
        {
            get
            {
                return _troopUpgradeSpoilsLootMultiplier.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopUpgradeSpoilsLoott
        {
            get
            {
                return new TextObject("{=RBM_CON_035}Battle Spoils Loot").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopUpgradeSpoilsLootHint { get; } = Hint("{=RBM_CON_051}Multiplier on the worth of the kit your men salvage from a battlefield. Default 1.00.");

        private float _troopLootPiecesPerMan;

        /// <summary>
        /// Whole pieces of kit, so the slider is discrete. Float because that is what SliderWidget
        /// binds; the value is rounded on the way in and cast back to an int on the way out.
        /// </summary>
        [DataSourceProperty]
        public float TroopLootPiecesPerMan
        {
            get
            {
                return _troopLootPiecesPerMan;
            }
            set
            {
                float snapped = MathF.Clamp(MathF.Round(value), 1f, 10f);
                if (snapped != _troopLootPiecesPerMan)
                {
                    _troopLootPiecesPerMan = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopLootPiecesPerMan");
                    OnPropertyChanged("TroopLootPiecesPerManValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopLootPiecesPerManValue
        {
            get
            {
                return _troopLootPiecesPerMan.ToString("0");
            }
        }

        [DataSourceProperty]
        public string TroopLootPiecesPerMant
        {
            get
            {
                return new TextObject("{=RBM_CON_037}Kit Pieces per Man").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopLootPiecesPerManHint { get; } = Hint("{=RBM_CON_052}How many pieces of kit a single soldier can carry off a field. Default 3.");

        private float _troopLootOverlookChancePerTier;

        [DataSourceProperty]
        public float TroopLootOverlookChancePerTier
        {
            get
            {
                return _troopLootOverlookChancePerTier;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 1f);
                if (snapped != _troopLootOverlookChancePerTier)
                {
                    _troopLootOverlookChancePerTier = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopLootOverlookChancePerTier");
                    OnPropertyChanged("TroopLootOverlookChancePerTierValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopLootOverlookChancePerTierValue
        {
            get
            {
                return _troopLootOverlookChancePerTier.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopLootOverlookChancePerTiert
        {
            get
            {
                return new TextObject("{=RBM_CON_040}Overlook Chance / Tier").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopLootOverlookChancePerTierHint { get; } = Hint("{=RBM_CON_053}Chance a man steps over a piece of kit one tier beneath him, leaving it for greener troops. Default 0.50.");

        private float _troopWageSpoilsFraction;

        [DataSourceProperty]
        public float TroopWageSpoilsFraction
        {
            get
            {
                return _troopWageSpoilsFraction;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 1f);
                if (snapped != _troopWageSpoilsFraction)
                {
                    _troopWageSpoilsFraction = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopWageSpoilsFraction");
                    OnPropertyChanged("TroopWageSpoilsFractionValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopWageSpoilsFractionValue
        {
            get
            {
                return _troopWageSpoilsFraction.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopWageSpoilsFractiont
        {
            get
            {
                return new TextObject("{=RBM_CON_036}Wage Kept as Spoils").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopWageSpoilsFractionHint { get; } = Hint("{=RBM_CON_054}Share of a man's daily wage that returns to you as spoils. Default 1.00.");

        private float _troopWageTierBase;

        /// <summary>
        /// A flat gold base multiplied by the troop's tier. Whole numbers, so the slider is discrete;
        /// float only because SliderWidget binds float. Rounded to an int on save.
        /// </summary>
        [DataSourceProperty]
        public float TroopWageTierBase
        {
            get
            {
                return _troopWageTierBase;
            }
            set
            {
                float snapped = MathF.Clamp(MathF.Round(value), 0f, 300f);
                if (snapped != _troopWageTierBase)
                {
                    _troopWageTierBase = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopWageTierBase");
                    OnPropertyChanged("TroopWageTierBaseValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopWageTierBaseValue
        {
            get
            {
                return _troopWageTierBase.ToString("0");
            }
        }

        [DataSourceProperty]
        public string TroopWageTierBaset
        {
            get
            {
                return new TextObject("{=RBM_CON_067}Wage Base per Tier").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopWageTierBaseHint { get; } = Hint("{=RBM_CON_068}Daily wage is this base value multiplied by the troop's tier, replacing the vanilla wage. Zero keeps vanilla wages. Default 50.");

        private float _troopMaintenanceFraction;

        /// <summary>
        /// A small share of a troop's whole kit worth (gear, horse and harness) spent per day on upkeep.
        /// Snapped to thousandths so the 0.005 default and its neighbours are reachable on the slider.
        /// </summary>
        [DataSourceProperty]
        public float TroopMaintenanceFraction
        {
            get
            {
                return _troopMaintenanceFraction;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 3), 0f, 0.05f);
                if (snapped != _troopMaintenanceFraction)
                {
                    _troopMaintenanceFraction = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopMaintenanceFraction");
                    OnPropertyChanged("TroopMaintenanceFractionValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopMaintenanceFractionValue
        {
            get
            {
                return _troopMaintenanceFraction.ToString("0.000");
            }
        }

        [DataSourceProperty]
        public string TroopMaintenanceFractiont
        {
            get
            {
                return new TextObject("{=RBM_CON_081}Daily Maintenance").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopMaintenanceFractionHint { get; } = Hint("{=RBM_CON_082}Daily upkeep per soldier as a share of his whole kit's worth -- gear, horse and harness. Paid first from the men's own spoils; any shortfall falls to the party leader's gold. Zero stops maintenance. Default 0.005.");

        private float _troopSettlementFoodDays;

        /// <summary>
        /// Whole days, so the slider is discrete. Zero is meaningful -- it stops troops buying food --
        /// so the floor is 0. Float because that is what SliderWidget binds; rounded to an int on save.
        /// </summary>
        [DataSourceProperty]
        public float TroopSettlementFoodDays
        {
            get
            {
                return _troopSettlementFoodDays;
            }
            set
            {
                float snapped = MathF.Clamp(MathF.Round(value), 0f, 60f);
                if (snapped != _troopSettlementFoodDays)
                {
                    _troopSettlementFoodDays = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopSettlementFoodDays");
                    OnPropertyChanged("TroopSettlementFoodDaysValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopSettlementFoodDaysValue
        {
            get
            {
                return _troopSettlementFoodDays.ToString("0");
            }
        }

        [DataSourceProperty]
        public string TroopSettlementFoodDayst
        {
            get
            {
                return new TextObject("{=RBM_CON_041}Food Days Bought").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopSettlementFoodDaysHint { get; } = Hint("{=RBM_CON_055}Days of food a man buys when he passes through a settlement. 0 stops him buying any. Default 20.");

        private float _troopFoodWageFraction;

        [DataSourceProperty]
        public float TroopFoodWageFraction
        {
            get
            {
                return _troopFoodWageFraction;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 10f);
                if (snapped != _troopFoodWageFraction)
                {
                    _troopFoodWageFraction = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopFoodWageFraction");
                    OnPropertyChanged("TroopFoodWageFractionValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopFoodWageFractionValue
        {
            get
            {
                return _troopFoodWageFraction.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopFoodWageFractiont
        {
            get
            {
                return new TextObject("{=RBM_CON_042}Spoils Spent on Food").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopFoodWageFractionHint { get; } = Hint("{=RBM_CON_056}Share of a day's wage a man spends to feed himself for a day. Default 0.50.");

        private float _troopSettlementFunWageFraction;

        [DataSourceProperty]
        public float TroopSettlementFunWageFraction
        {
            get
            {
                return _troopSettlementFunWageFraction;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 10f);
                if (snapped != _troopSettlementFunWageFraction)
                {
                    _troopSettlementFunWageFraction = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopSettlementFunWageFraction");
                    OnPropertyChanged("TroopSettlementFunWageFractionValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopSettlementFunWageFractionValue
        {
            get
            {
                return _troopSettlementFunWageFraction.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopSettlementFunWageFractiont
        {
            get
            {
                return new TextObject("{=RBM_CON_043}Spoils Spent on Fun").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopSettlementFunWageFractionHint { get; } = Hint("{=RBM_CON_057}A day's wage a man drinks away for each day he sits idle in a settlement. Default 1.50.");

        private float _settlementProsperityPerGoldSpent;

        [DataSourceProperty]
        public float SettlementProsperityPerGoldSpent
        {
            get
            {
                return _settlementProsperityPerGoldSpent;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 1f);
                if (snapped != _settlementProsperityPerGoldSpent)
                {
                    _settlementProsperityPerGoldSpent = snapped;
                    OnPropertyChangedWithValue(snapped, "SettlementProsperityPerGoldSpent");
                    OnPropertyChanged("SettlementProsperityPerGoldSpentValue");
                }
            }
        }

        [DataSourceProperty]
        public string SettlementProsperityPerGoldSpentValue
        {
            get
            {
                return _settlementProsperityPerGoldSpent.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string SettlementProsperityPerGoldSpentt
        {
            get
            {
                return new TextObject("{=RBM_CON_038}Prosperity per Gold").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel SettlementProsperityPerGoldSpentHint { get; } = Hint("{=RBM_CON_058}How much Prosperity, or a village's Hearth, a gold moves at a settlement, both ways: trade and carousing there add it, its militia's wages and every good it produces drain it. Default 0.02.");

        private float _militiaWageModifier;

        [DataSourceProperty]
        public float MilitiaWageModifier
        {
            get
            {
                return _militiaWageModifier;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 1f);
                if (snapped != _militiaWageModifier)
                {
                    _militiaWageModifier = snapped;
                    OnPropertyChangedWithValue(snapped, "MilitiaWageModifier");
                    OnPropertyChanged("MilitiaWageModifierValue");
                }
            }
        }

        [DataSourceProperty]
        public string MilitiaWageModifierValue
        {
            get
            {
                return _militiaWageModifier.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string MilitiaWageModifiert
        {
            get
            {
                return new TextObject("{=RBM_CON_077}Militia Wage Modifier").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel MilitiaWageModifierHint { get; } = Hint("{=RBM_CON_078}Share of their gear-based wage a settlement's militia cost the place that keeps them, drawn from Prosperity or Hearth. Zero makes militia free to garrison. Default 0.20.");

        private float _troopRaidSpoilsMultiplier;

        [DataSourceProperty]
        public float TroopRaidSpoilsMultiplier
        {
            get
            {
                return _troopRaidSpoilsMultiplier;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 10f);
                if (snapped != _troopRaidSpoilsMultiplier)
                {
                    _troopRaidSpoilsMultiplier = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopRaidSpoilsMultiplier");
                    OnPropertyChanged("TroopRaidSpoilsMultiplierValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopRaidSpoilsMultiplierValue
        {
            get
            {
                return _troopRaidSpoilsMultiplier.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopRaidSpoilsMultipliert
        {
            get
            {
                return new TextObject("{=RBM_CON_044}Raid Plunder Pocketed").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopRaidSpoilsMultiplierHint { get; } = Hint("{=RBM_CON_059}Share of a raid's plunder its soldiers keep for themselves as spoils. Default 0.25.");

        [DataSourceProperty]
        public string SpoilsLoggingt
        {
            get
            {
                return new TextObject("{=RBM_CON_039}Spoils Logging").ToString();
            }
        }

        [DataSourceProperty]
        public string SpoilsVerboseLoggingt
        {
            get
            {
                return new TextObject("{=RBM_CON_049}Verbose Logging").ToString();
            }
        }

        // SupplyTown gate: radius slider (whole map units) + the toggle's row label.
        private float _troopUpgradeSupplyRadius;

        [DataSourceProperty]
        public float TroopUpgradeSupplyRadius
        {
            get
            {
                return _troopUpgradeSupplyRadius;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value), 0f, 200f);
                if (snapped != _troopUpgradeSupplyRadius)
                {
                    _troopUpgradeSupplyRadius = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopUpgradeSupplyRadius");
                    OnPropertyChanged("TroopUpgradeSupplyRadiusValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopUpgradeSupplyRadiusValue
        {
            get
            {
                return _troopUpgradeSupplyRadius.ToString("0");
            }
        }

        [DataSourceProperty]
        public string TroopUpgradeSupplyRadiust
        {
            get
            {
                return new TextObject("{=RBM_CON_071}Upgrade Supply Range").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopUpgradeSupplyRadiusHint { get; } = Hint("{=RBM_CON_072}How near, in map units, a friendly or neutral town must be for a party to upgrade its troops. Needs 'Upgrade Near Town' on. Default 30.");

        [DataSourceProperty]
        public string TroopUpgradeRequireSupplyTownt
        {
            get
            {
                return new TextObject("{=RBM_CON_070}Upgrade Near Town").ToString();
            }
        }

        private float _troopLeaderSpoilsCutFraction;

        [DataSourceProperty]
        public float TroopLeaderSpoilsCutFraction
        {
            get
            {
                return _troopLeaderSpoilsCutFraction;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 1f);
                if (snapped != _troopLeaderSpoilsCutFraction)
                {
                    _troopLeaderSpoilsCutFraction = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopLeaderSpoilsCutFraction");
                    OnPropertyChanged("TroopLeaderSpoilsCutFractionValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopLeaderSpoilsCutFractionValue
        {
            get
            {
                return _troopLeaderSpoilsCutFraction.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopLeaderSpoilsCutFractiont
        {
            get
            {
                return new TextObject("{=RBM_CON_079}Leader's Cut").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopLeaderSpoilsCutFractionHint { get; } = Hint("{=RBM_CON_080}Base share of the spoils a party's men gather -- off a battlefield, a raid or a sack -- that their leader skims into his own purse as gold before the rest settles into the stacks. Multiplied by the leader's clan tier plus one, so a tier-0 or clanless leader takes this share once and a tier-6 house seven times it. Zero leaves the men all they take. Default 0.05.");

        private float _troopSpoilsWarChestGoldPerTier;

        [DataSourceProperty]
        public float TroopSpoilsWarChestGoldPerTier
        {
            get
            {
                return _troopSpoilsWarChestGoldPerTier;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value), 0f, 1000f);
                if (snapped != _troopSpoilsWarChestGoldPerTier)
                {
                    _troopSpoilsWarChestGoldPerTier = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopSpoilsWarChestGoldPerTier");
                    OnPropertyChanged("TroopSpoilsWarChestGoldPerTierValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopSpoilsWarChestGoldPerTierValue
        {
            get
            {
                return ((int)_troopSpoilsWarChestGoldPerTier).ToString();
            }
        }

        [DataSourceProperty]
        public string TroopSpoilsWarChestGoldPerTiert
        {
            get
            {
                return new TextObject("{=RBM_CON_046}War Chest per Tier").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopSpoilsWarChestGoldPerTierHint { get; } = Hint("{=RBM_CON_061}Gold a man holds back from the surplus he hands up, scaled by his tier. Default 25.");

        private float _troopLuxuryCooldownDays;

        /// <summary>Whole days, so the slider is discrete. Zero lets a stack indulge on every roll.</summary>
        [DataSourceProperty]
        public float TroopLuxuryCooldownDays
        {
            get
            {
                return _troopLuxuryCooldownDays;
            }
            set
            {
                float snapped = MathF.Clamp(MathF.Round(value), 0f, 120f);
                if (snapped != _troopLuxuryCooldownDays)
                {
                    _troopLuxuryCooldownDays = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopLuxuryCooldownDays");
                    OnPropertyChanged("TroopLuxuryCooldownDaysValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopLuxuryCooldownDaysValue
        {
            get
            {
                return _troopLuxuryCooldownDays.ToString("0");
            }
        }

        [DataSourceProperty]
        public string TroopLuxuryCooldownDayst
        {
            get
            {
                return new TextObject("{=RBM_CON_047}Luxury Cooldown (Days)").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopLuxuryCooldownDaysHint { get; } = Hint("{=RBM_CON_063}Days a stack waits after buying a luxury before it splurges again. Default 20.");

        private float _troopLuxurySpendChance;

        [DataSourceProperty]
        public float TroopLuxurySpendChance
        {
            get
            {
                return _troopLuxurySpendChance;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 1f);
                if (snapped != _troopLuxurySpendChance)
                {
                    _troopLuxurySpendChance = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopLuxurySpendChance");
                    OnPropertyChanged("TroopLuxurySpendChanceValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopLuxurySpendChanceValue
        {
            get
            {
                return _troopLuxurySpendChance.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopLuxurySpendChancet
        {
            get
            {
                return new TextObject("{=RBM_CON_048}Luxury Buy Chance").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopLuxurySpendChanceHint { get; } = Hint("{=RBM_CON_064}Chance each idle hour that an over-cap stack buys a luxury from the settlement. Default 0.02.");

        private float _troopFallenSpoilsCaptureFraction;

        [DataSourceProperty]
        public float TroopFallenSpoilsCaptureFraction
        {
            get
            {
                return _troopFallenSpoilsCaptureFraction;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 1f);
                if (snapped != _troopFallenSpoilsCaptureFraction)
                {
                    _troopFallenSpoilsCaptureFraction = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopFallenSpoilsCaptureFraction");
                    OnPropertyChanged("TroopFallenSpoilsCaptureFractionValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopFallenSpoilsCaptureFractionValue
        {
            get
            {
                return _troopFallenSpoilsCaptureFraction.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopFallenSpoilsCaptureFractiont
        {
            get
            {
                return new TextObject("{=RBM_CON_065}Fallen Spoils Captured").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopFallenSpoilsCaptureFractionHint { get; } = Hint("{=RBM_CON_066}Share of a beaten enemy's killed and wounded spoils the victors carry off; the rest is lost. Default 0.75.");

        private float _troopSpoilsHealGoldPerTier;

        [DataSourceProperty]
        public float TroopSpoilsHealGoldPerTier
        {
            get
            {
                return _troopSpoilsHealGoldPerTier;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value), 0f, 100f);
                if (snapped != _troopSpoilsHealGoldPerTier)
                {
                    _troopSpoilsHealGoldPerTier = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopSpoilsHealGoldPerTier");
                    OnPropertyChanged("TroopSpoilsHealGoldPerTierValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopSpoilsHealGoldPerTierValue
        {
            get
            {
                return ((int)_troopSpoilsHealGoldPerTier).ToString();
            }
        }

        [DataSourceProperty]
        public string TroopSpoilsHealGoldPerTiert
        {
            get
            {
                return new TextObject("{=RBM_CON_073}Heal Cost per Tier").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopSpoilsHealGoldPerTierHint { get; } = Hint("{=RBM_CON_074}Gold a wounded man's stack pays to mend him faster while resting in a settlement, scaled by his tier. Zero stops paid healing. Default 10.");

        private float _troopSpoilsHealFractionPerHour;

        [DataSourceProperty]
        public float TroopSpoilsHealFractionPerHour
        {
            get
            {
                return _troopSpoilsHealFractionPerHour;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 1f);
                if (snapped != _troopSpoilsHealFractionPerHour)
                {
                    _troopSpoilsHealFractionPerHour = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopSpoilsHealFractionPerHour");
                    OnPropertyChanged("TroopSpoilsHealFractionPerHourValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopSpoilsHealFractionPerHourValue
        {
            get
            {
                return _troopSpoilsHealFractionPerHour.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string TroopSpoilsHealFractionPerHourt
        {
            get
            {
                return new TextObject("{=RBM_CON_075}Heal Rate per Hour").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopSpoilsHealFractionPerHourHint { get; } = Hint("{=RBM_CON_076}Most of a stack's wounded that paid healing mends in one hour, so a deep purse buys a faster recovery, not an instant one. Default 0.05.");

        [DataSourceProperty]
        public string ThrustModifiert
        {
            get
            {
                return new TextObject("{=RBM_CON_021}Thrust weapon preference for AI (default at 0.05)").ToString();
            }
        }

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
        public string RBMAIt
        {
            get
            {
                return new TextObject("{=RBM_CON_018}RBM AI").ToString();
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
                return new TextObject("RBM Campaign").ToString();
            }
        }

        public List<string> thrustModifierList = new List<string> { new TextObject("0.01").ToString(), new TextObject("0.05").ToString(), new TextObject("0.10").ToString(), new TextObject("0.15").ToString(),
                                                                new TextObject("0.20").ToString(), new TextObject("0.25").ToString(), new TextObject("0.30").ToString(), new TextObject("0.35").ToString(),
                                                                new TextObject("0.40").ToString(), new TextObject("0.45").ToString(), new TextObject("0.50").ToString(), new TextObject("0.55").ToString(),
                                                                new TextObject("0.60").ToString(), new TextObject("0.65").ToString(), new TextObject("0.70").ToString(), new TextObject("0.75").ToString(),
                                                                new TextObject("0.80").ToString(), new TextObject("0.85").ToString(), new TextObject("0.90").ToString(), new TextObject("0.95").ToString(),
                                                                new TextObject("1.00").ToString()};

        public RBMConfigViewModel()
        {
            RefreshValues();
            //RbmConfigData data;
            List<string> troopOverhaulOnOff = new List<string> { new TextObject("{=RBM_CON_001}Inactive").ToString(), new TextObject("{=RBM_CON_002}Active (Recommended)").ToString(), };
            ActiveTroopOverhaulText = new TextViewModel(new TextObject("Troop Overhaul"));
            ActiveTroopOverhaul = new SelectorVM<SelectorItemVM>(troopOverhaulOnOff, 0, null);

            List<string> rangedReloadSpeed = new List<string> { new TextObject("{=RBM_CON_004}Vanilla").ToString(), new TextObject("{=RBM_CON_005}Realistic").ToString(), new TextObject("{=RBM_CON_006}Semi-realistic").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            RangedReloadSpeedText = new TextViewModel(new TextObject("Ranged reload speed"));
            RangedReloadSpeed = new SelectorVM<SelectorItemVM>(rangedReloadSpeed, 0, null);

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

            int i = 0;
            foreach (var item in thrustModifierList)
            {
                if(float.Parse(item) == RBMConfig.ThrustMagnitudeModifier)
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
            RBMCombatEnabled = new SelectorVM<SelectorItemVM>(rbmCombatEnabledOptions, 0, null);

            List<string> rbmAiEnabledOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            RBMAIEnabled = new SelectorVM<SelectorItemVM>(rbmAiEnabledOptions, 0, null);

            List<string> rbmTournamentEnabledOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            RBMTournamentEnabled = new SelectorVM<SelectorItemVM>(rbmTournamentEnabledOptions, 0, null);

            List<string> rbmCampaignEnabledOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            RBMCampaignEnabled = new SelectorVM<SelectorItemVM>(rbmCampaignEnabledOptions, 0, null);

            List<string> spoilsLoggingOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            SpoilsLoggingEnabledText = new TextViewModel(new TextObject("{=RBM_CON_039}Spoils Logging"));
            SpoilsLoggingEnabled = new SelectorVM<SelectorItemVM>(spoilsLoggingOptions, 0, null);

            List<string> spoilsVerboseLoggingOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            SpoilsVerboseLoggingEnabledText = new TextViewModel(new TextObject("{=RBM_CON_049}Verbose Logging"));
            SpoilsVerboseLoggingEnabled = new SelectorVM<SelectorItemVM>(spoilsVerboseLoggingOptions, 0, null);

            // SupplyTown gate: Enabled is the default, so its option carries the "(Default)" tag.
            List<string> troopUpgradeRequireSupplyTownOptions = new List<string> { new TextObject("{=1JlzQIXE}Disabled").ToString(), new TextObject("{=tsPjK1Ke}Enabled").ToString() + " (" + new TextObject("{=fMSYE6Ii}Default").ToString() + ")" };
            TroopUpgradeRequireSupplyTownText = new TextViewModel(new TextObject("{=RBM_CON_070}Upgrade Near Town"));
            TroopUpgradeRequireSupplyTown = new SelectorVM<SelectorItemVM>(troopUpgradeRequireSupplyTownOptions, 0, null);

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

            _troopUpgradeCostMultiplier = MathF.Clamp(RBMConfig.troopUpgradeCostMultiplier, 0f, 2f);
            _troopUpgradeSpoilsLootMultiplier = MathF.Clamp(RBMConfig.troopUpgradeSpoilsLootMultiplier, 0f, 5f);
            _troopUpgradeSupplyRadius = MathF.Clamp(RBMConfig.troopUpgradeSupplyRadius, 0f, 200f);
            TroopUpgradeRequireSupplyTown.SelectedIndex = RBMConfig.troopUpgradeRequireSupplyTown ? 1 : 0;
            _troopLootPiecesPerMan = MathF.Clamp(RBMConfig.troopLootPiecesPerMan, 1f, 10f);
            _troopLootOverlookChancePerTier = MathF.Clamp(RBMConfig.troopLootOverlookChancePerTier, 0f, 1f);
            _troopWageSpoilsFraction = MathF.Clamp(RBMConfig.troopWageSpoilsFraction, 0f, 1f);
            _troopWageTierBase = MathF.Clamp((float)RBMConfig.troopWageTierBase, 0f, 300f);
            _troopMaintenanceFraction = MathF.Clamp(RBMConfig.troopMaintenanceFraction, 0f, 0.05f);
            _troopSettlementFoodDays = MathF.Clamp(RBMConfig.troopSettlementFoodDays, 0f, 60f);
            _troopFoodWageFraction = MathF.Clamp(RBMConfig.troopFoodWageFraction, 0f, 10f);
            _troopSettlementFunWageFraction = MathF.Clamp(RBMConfig.troopSettlementFunWageFraction, 0f, 10f);
            _settlementProsperityPerGoldSpent = MathF.Clamp(RBMConfig.settlementProsperityPerGoldSpent, 0f, 1f);
            _militiaWageModifier = MathF.Clamp(RBMConfig.militiaWageModifier, 0f, 1f);
            _troopRaidSpoilsMultiplier = MathF.Clamp(RBMConfig.troopRaidSpoilsMultiplier, 0f, 10f);
            SpoilsLoggingEnabled.SelectedIndex = RBMConfig.spoilsLoggingEnabled ? 1 : 0;
            SpoilsVerboseLoggingEnabled.SelectedIndex = RBMConfig.spoilsVerboseLoggingEnabled ? 1 : 0;
            _troopLeaderSpoilsCutFraction = MathF.Clamp(RBMConfig.troopLeaderSpoilsCutFraction, 0f, 1f);
            _troopSpoilsWarChestGoldPerTier = MathF.Clamp(RBMConfig.troopSpoilsWarChestGoldPerTier, 0f, 1000f);
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

            var newThrustModifier = float.Parse(thrustModifierList[ThrustModifier.SelectedIndex]);
            RBMConfig.ThrustMagnitudeModifier = newThrustModifier;
            RBMConfig.OneHandedThrustDamageBonus = 1f / newThrustModifier;
            RBMConfig.TwoHandedThrustDamageBonus = 1f / newThrustModifier;

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

            RBMConfig.troopUpgradeCostMultiplier = _troopUpgradeCostMultiplier;
            RBMConfig.troopUpgradeSpoilsLootMultiplier = _troopUpgradeSpoilsLootMultiplier;
            RBMConfig.troopUpgradeSupplyRadius = _troopUpgradeSupplyRadius;
            RBMConfig.troopUpgradeRequireSupplyTown = TroopUpgradeRequireSupplyTown.SelectedIndex == 1;
            RBMConfig.troopLootPiecesPerMan = MathF.Round(_troopLootPiecesPerMan);
            RBMConfig.troopLootOverlookChancePerTier = _troopLootOverlookChancePerTier;
            RBMConfig.troopWageSpoilsFraction = _troopWageSpoilsFraction;
            RBMConfig.troopWageTierBase = (int)MathF.Round(_troopWageTierBase);
            RBMConfig.troopMaintenanceFraction = _troopMaintenanceFraction;
            RBMConfig.troopSettlementFoodDays = (int)MathF.Round(_troopSettlementFoodDays);
            RBMConfig.troopFoodWageFraction = _troopFoodWageFraction;
            RBMConfig.troopSettlementFunWageFraction = _troopSettlementFunWageFraction;
            RBMConfig.settlementProsperityPerGoldSpent = _settlementProsperityPerGoldSpent;
            RBMConfig.militiaWageModifier = _militiaWageModifier;
            RBMConfig.troopRaidSpoilsMultiplier = _troopRaidSpoilsMultiplier;
            RBMConfig.spoilsLoggingEnabled = SpoilsLoggingEnabled.SelectedIndex == 1;
            RBMConfig.spoilsVerboseLoggingEnabled = SpoilsVerboseLoggingEnabled.SelectedIndex == 1;
            RBMConfig.troopLeaderSpoilsCutFraction = _troopLeaderSpoilsCutFraction;
            RBMConfig.troopSpoilsWarChestGoldPerTier = (int)MathF.Round(_troopSpoilsWarChestGoldPerTier);
            RBMConfig.troopLuxuryCooldownDays = (int)MathF.Round(_troopLuxuryCooldownDays);
            RBMConfig.troopLuxurySpendChance = _troopLuxurySpendChance;
            RBMConfig.troopFallenSpoilsCaptureFraction = _troopFallenSpoilsCaptureFraction;
            RBMConfig.troopSpoilsHealGoldPerTier = (int)MathF.Round(_troopSpoilsHealGoldPerTier);
            RBMConfig.troopSpoilsHealFractionPerHour = _troopSpoilsHealFractionPerHour;

            RBMConfig.saveXmlConfig();
            //RBMConfig.parseXmlConfig();
            TaleWorlds.ScreenSystem.ScreenManager.PopScreen();
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
            RealisticArrowArc.SelectedIndex = 0;
            ArmorStatusUIEnabled.SelectedIndex = 1;
            SneakAttackInstaKill.SelectedIndex = 0;
            BetterArrowVisuals.SelectedIndex = 1;
            PassiveShoulderShields.SelectedIndex = 0;
            RangedReloadSpeed.SelectedIndex = 2;
            ActiveTroopOverhaul.SelectedIndex = 1;
            RBMCombatEnabled.SelectedIndex = 1;

            // AI
            VanillaCombatAi.SelectedIndex = 0;
            KeepBattleEnabled.SelectedIndex = 0;
            PostureGUIEnabled.SelectedIndex = 1;
            PlayerPostureMultiplier.SelectedIndex = 0;
            PostureSystemEnabled.SelectedIndex = 1;
            StaminaSystemEnabled.SelectedIndex = 1;
            HitStopEnabled.SelectedIndex = 1;
            RBMAIEnabled.SelectedIndex = 1;

            // Modules
            RBMTournamentEnabled.SelectedIndex = 1;
            RBMCampaignEnabled.SelectedIndex = 1;

            // Campaign / spoils
            TroopUpgradeCostMultiplier = 1f;
            TroopUpgradeSpoilsLootMultiplier = 1f;
            TroopUpgradeSupplyRadius = 30f;
            TroopUpgradeRequireSupplyTown.SelectedIndex = 1;
            TroopLootPiecesPerMan = 3f;
            TroopLootOverlookChancePerTier = 0.5f;
            TroopWageSpoilsFraction = 1.0f;
            TroopWageTierBase = 50f;
            TroopSettlementFoodDays = 20f;
            TroopFoodWageFraction = 0.5f;
            TroopSettlementFunWageFraction = 1.5f;
            SettlementProsperityPerGoldSpent = 0.02f;
            MilitiaWageModifier = 0.2f;
            TroopRaidSpoilsMultiplier = 0.25f;
            TroopLeaderSpoilsCutFraction = 0.05f;
            TroopSpoilsWarChestGoldPerTier = 25f;
            TroopLuxuryCooldownDays = 20f;
            TroopLuxurySpendChance = 0.02f;
            TroopFallenSpoilsCaptureFraction = 0.75f;
            TroopSpoilsHealGoldPerTier = 10f;
            TroopSpoilsHealFractionPerHour = 0.05f;
            SpoilsLoggingEnabled.SelectedIndex = 1;
            SpoilsVerboseLoggingEnabled.SelectedIndex = 1;
        }

        private void ExecuteCancel()
        {
            TaleWorlds.ScreenSystem.ScreenManager.PopScreen();
        }
    }
}