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
        public TextViewModel SpoilsLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> SpoilsLoggingEnabled { get; }

        // Historical trade good worth and weight: on/off toggle for the repriced goods table.
        public TextViewModel RealisticTradeGoodPricesText { get; }
        public SelectorVM<SelectorItemVM> RealisticTradeGoodPrices { get; }

        // Weight column in the inventory / trade item rows: on/off.
        public TextViewModel ShowInventoryItemWeightText { get; }
        public SelectorVM<SelectorItemVM> ShowInventoryItemWeight { get; }

        public TextViewModel SpoilsVerboseLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> SpoilsVerboseLoggingEnabled { get; }

        // Economy logging (village production, villager dispatches, town food): on/off.
        public TextViewModel EconomyLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> EconomyLoggingEnabled { get; }

        // Intra-kingdom supply caravans: master on/off.
        public TextViewModel KingdomCaravansEnabledText { get; }
        public SelectorVM<SelectorItemVM> KingdomCaravansEnabled { get; }

        // The repayable wealth injection bundled onto wealthy→struggling caravan routes: on/off.
        public TextViewModel CaravanInvestmentEnabledText { get; }
        public SelectorVM<SelectorItemVM> CaravanInvestmentEnabled { get; }

        // Supply-caravan logging (logs/caravans): on/off.
        public TextViewModel CaravanLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> CaravanLoggingEnabled { get; }

        // SupplyTown gate: on/off toggle for gating upgrades on a nearby friendly town.
        public TextViewModel TroopUpgradeRequireSupplyTownText { get; }
        public SelectorVM<SelectorItemVM> TroopUpgradeRequireSupplyTown { get; }

        // RecruitSupply draw: on/off toggle for outfitting recruits out of the settlement's market stock.
        public TextViewModel RecruitDrawsFromSettlementStockText { get; }
        public SelectorVM<SelectorItemVM> RecruitDrawsFromSettlementStock { get; }

        // Charge mount in gold: on/off toggle for dropping the horse-item requirement and pricing the mount.
        public TextViewModel TroopUpgradeChargeMountValueText { get; }
        public SelectorVM<SelectorItemVM> TroopUpgradeChargeMountValue { get; }

        private float _campaignStartingGold;

        [DataSourceProperty]
        public float CampaignStartingGold
        {
            get
            {
                return _campaignStartingGold;
            }
            set
            {
                // Slider reports continuous values; snap to whole 500-denar steps.
                float snapped = MathF.Round(value / 500f) * 500f;
                snapped = MathF.Clamp(snapped, 1000f, 100000f);
                if (snapped != _campaignStartingGold)
                {
                    _campaignStartingGold = snapped;
                    OnPropertyChangedWithValue(snapped, "CampaignStartingGold");
                    OnPropertyChanged("CampaignStartingGoldValue");
                }
            }
        }

        [DataSourceProperty]
        public string CampaignStartingGoldValue
        {
            get
            {
                return _campaignStartingGold.ToString("0");
            }
        }

        [DataSourceProperty]
        public string CampaignStartingGoldt
        {
            get
            {
                return new TextObject("{=RBM_CON_109}Starting Gold").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel CampaignStartingGoldHint { get; } = Hint("{=RBM_CON_110}The purse a new campaign opens with, replacing whatever character creation handed out. Only applies to a new game; a loaded save keeps its own gold. Default 5000.");

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

        // Troop wages are a fixed table (RBMCampaign/Wages/TierBasedWageModel.cs) and have no dial.
        // The former TroopWageTierBase slider was removed along with its config field.

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

        private float _mercenaryMaintenancePurseFraction;

        /// <summary>
        /// How much of a mercenary company's daily maintenance its own purses may cover; the employer meets
        /// the rest. Snapped to hundredths so the 0.5 default and its neighbours are reachable on the slider.
        /// </summary>
        [DataSourceProperty]
        public float MercenaryMaintenancePurseFraction
        {
            get
            {
                return _mercenaryMaintenancePurseFraction;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 1f);
                if (snapped != _mercenaryMaintenancePurseFraction)
                {
                    _mercenaryMaintenancePurseFraction = snapped;
                    OnPropertyChangedWithValue(snapped, "MercenaryMaintenancePurseFraction");
                    OnPropertyChanged("MercenaryMaintenancePurseFractionValue");
                }
            }
        }

        [DataSourceProperty]
        public string MercenaryMaintenancePurseFractionValue
        {
            get
            {
                return _mercenaryMaintenancePurseFraction.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string MercenaryMaintenancePurseFractiont
        {
            get
            {
                return new TextObject("{=RBM_CON_089}Mercenary Maintenance Share").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel MercenaryMaintenancePurseFractionHint { get; } = Hint("{=RBM_CON_090}Share of daily maintenance a mercenary company under a kingdom's pay meets from its own spoils; its employer covers the rest, any shortfall falling to the party leader's gold. Default 0.50.");

        private float _independentMaintenancePurseFraction;

        /// <summary>
        /// How much of an independent clan's daily maintenance its own purses may cover -- one sworn to no
        /// kingdom. Snapped to hundredths so the 1.0 default and its neighbours are reachable on the slider.
        /// </summary>
        [DataSourceProperty]
        public float IndependentMaintenancePurseFraction
        {
            get
            {
                return _independentMaintenancePurseFraction;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value, 2), 0f, 1f);
                if (snapped != _independentMaintenancePurseFraction)
                {
                    _independentMaintenancePurseFraction = snapped;
                    OnPropertyChangedWithValue(snapped, "IndependentMaintenancePurseFraction");
                    OnPropertyChanged("IndependentMaintenancePurseFractionValue");
                }
            }
        }

        [DataSourceProperty]
        public string IndependentMaintenancePurseFractionValue
        {
            get
            {
                return _independentMaintenancePurseFraction.ToString("0.00");
            }
        }

        [DataSourceProperty]
        public string IndependentMaintenancePurseFractiont
        {
            get
            {
                return new TextObject("{=RBM_CON_091}Independent Maintenance Share").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel IndependentMaintenancePurseFractionHint { get; } = Hint("{=RBM_CON_092}Share of daily maintenance an independent clan -- one sworn to no kingdom -- meets from its own spoils; any shortfall falls to the party leader's gold. Sworn vassals and rulers pay none from their purses. Default 1.00.");

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

        private float _recruitMaintenanceDays;

        /// <summary>
        /// Whole days of maintenance a recruit brings in his purse, so the slider is discrete. Zero is
        /// meaningful -- it seeds a fresh recruit nothing -- so the floor is 0. Float because that is what
        /// SliderWidget binds; rounded to an int on save.
        /// </summary>
        [DataSourceProperty]
        public float RecruitMaintenanceDays
        {
            get
            {
                return _recruitMaintenanceDays;
            }
            set
            {
                float snapped = MathF.Clamp(MathF.Round(value), 0f, 30f);
                if (snapped != _recruitMaintenanceDays)
                {
                    _recruitMaintenanceDays = snapped;
                    OnPropertyChangedWithValue(snapped, "RecruitMaintenanceDays");
                    OnPropertyChanged("RecruitMaintenanceDaysValue");
                }
            }
        }

        [DataSourceProperty]
        public string RecruitMaintenanceDaysValue
        {
            get
            {
                return _recruitMaintenanceDays.ToString("0");
            }
        }

        [DataSourceProperty]
        public string RecruitMaintenanceDayst
        {
            get
            {
                return new TextObject("{=RBM_CON_083}Recruit Maintenance Days").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel RecruitMaintenanceDaysHint { get; } = Hint("{=RBM_CON_084}Days of maintenance a recruit mustered from a village or town brings in his stack's purse, priced off the same daily upkeep. 0 seeds nothing. Default 20.");

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
        public BasicTooltipViewModel TroopSettlementFunWageFractionHint { get; } = Hint("{=RBM_CON_057}A day's wage a man drinks away for each day he sits idle in a settlement. Default 0.25.");


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
        public string EconomyLoggingt
        {
            get
            {
                return new TextObject("{=RBM_CON_107}Economy Logging").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel EconomyLoggingEnabledHint { get; } = Hint("{=RBM_CON_108}Writes the village-to-town goods and food chain to logs/economy next to the config: each village's daily production, every villager party sent out with its size, composition and cargo, each town's rations, and the end-of-day state of every settlement. Verbose, and only useful for tuning the economy.");

        // Caravan toggle labels and hints. Plain literal TextObjects (no {=KEY}) to sidestep the
        // LOC-eng.xml key-collision issue, like the category headers below.
        [DataSourceProperty]
        public string KingdomCaravansEnabledt
        {
            get { return new TextObject("Kingdom Supply Caravans").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel KingdomCaravansEnabledHint { get; } = Hint("Spawns caravans that carry a surplus good from one of a kingdom's towns to another town of the same kingdom that is short of it -- real goods and money move, and the caravan can be raided. Off leaves the map on vanilla caravans alone. Default on.");

        [DataSourceProperty]
        public string CaravanInvestmentEnabledt
        {
            get { return new TextObject("Caravan Investment").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel CaravanInvestmentEnabledHint { get; } = Hint("On a wealthy→struggling route, a caravan also injects capital into the struggling town so it can afford the goods, booked as a debt the town repays out of its hoard tax once it recovers. Needs Kingdom Supply Caravans on. Default on.");

        [DataSourceProperty]
        public string CaravanLoggingEnabledt
        {
            get { return new TextObject("Caravan Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel CaravanLoggingEnabledHint { get; } = Hint("Writes the supply-caravan system to logs/caravans next to the config: each caravan dispatched, its arrival and sale, capital injected and repaid, and any lost on the road. Needs Kingdom Supply Caravans on. Default on.");

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

        [DataSourceProperty]
        public BasicTooltipViewModel RecruitDrawsFromSettlementStockHint { get; } = Hint("{=RBM_CON_109}A man recruited in a town is outfitted from that town's market; one recruited in a village is outfitted from the town it trades with. Gear of the right kind and tier leaves that market. It draws what the market has and never blocks a recruitment. Default on.");

        [DataSourceProperty]
        public string RecruitDrawsFromSettlementStockt
        {
            get
            {
                return new TextObject("{=RBM_CON_110}Recruits Kitted From Market").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopUpgradeChargeMountValueHint { get; } = Hint("{=RBM_CON_088}Upgrading into a mounted troop no longer needs a horse in the baggage train and consumes none; the horse and harness are paid for in gold/spoils instead. Off restores the vanilla horse-item requirement. Default on.");

        [DataSourceProperty]
        public string TroopUpgradeChargeMountValuet
        {
            get
            {
                return new TextObject("{=RBM_CON_087}Buy Mounts For Upgrades").ToString();
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

        private float _troopSpoilsCapDays;

        [DataSourceProperty]
        public float TroopSpoilsCapDays
        {
            get
            {
                return _troopSpoilsCapDays;
            }
            set
            {
                float snapped = MathF.Clamp((float)System.Math.Round(value), 0f, 60f);
                if (snapped != _troopSpoilsCapDays)
                {
                    _troopSpoilsCapDays = snapped;
                    OnPropertyChangedWithValue(snapped, "TroopSpoilsCapDays");
                    OnPropertyChanged("TroopSpoilsCapDaysValue");
                }
            }
        }

        [DataSourceProperty]
        public string TroopSpoilsCapDaysValue
        {
            get
            {
                return ((int)_troopSpoilsCapDays).ToString();
            }
        }

        [DataSourceProperty]
        public string TroopSpoilsCapDayst
        {
            get
            {
                return new TextObject("{=RBM_CON_046}Spoils Reserve (Days of Keep)").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopSpoilsCapDaysHint { get; } = Hint("{=RBM_CON_061}Days of keep a stack holds in its purse before upkeep spends the surplus: this many days of its wage and its field maintenance together. Default 20.");

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
        public string RealisticTradeGoodPricest
        {
            get
            {
                return new TextObject("{=RBM_CON_103}Historical Trade Goods").ToString();
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel RealisticTradeGoodPricesHint { get; } = Hint("{=RBM_CON_104}Reprices the trade goods off historical figures -- a period price in denars and the real weight of one lot -- so worth and bulk move together and a cart of velvet is no longer a cart of hardwood. Off restores the game's own numbers. Default on.");

        [DataSourceProperty]
        public string ShowInventoryItemWeightt
        {
            get
            {
                return new TextObject("{=RBM_CON_105}Inventory Weight Column").ToString();
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

        // Campaign config category headers. Plain literal TextObjects (no {=KEY}) to sidestep
        // the LOC-eng.xml key-collision issue; these are collapsible sub-section titles.
        [DataSourceProperty]
        public string CampaignCatModulet
        {
            get
            {
                return new TextObject("Module & Simulation").ToString();
            }
        }

        [DataSourceProperty]
        public string CampaignCatLoggingt
        {
            get
            {
                return new TextObject("Logging").ToString();
            }
        }

        [DataSourceProperty]
        public string CampaignCatUpgradest
        {
            get
            {
                return new TextObject("Troop Upgrades").ToString();
            }
        }

        [DataSourceProperty]
        public string CampaignCatSpoilst
        {
            get
            {
                return new TextObject("Battle & Raid Spoils").ToString();
            }
        }

        [DataSourceProperty]
        public string CampaignCatWagest
        {
            get
            {
                return new TextObject("Wages & Maintenance").ToString();
            }
        }

        [DataSourceProperty]
        public string CampaignCatUpkeept
        {
            get
            {
                return new TextObject("Settlement Upkeep").ToString();
            }
        }

    }
}
