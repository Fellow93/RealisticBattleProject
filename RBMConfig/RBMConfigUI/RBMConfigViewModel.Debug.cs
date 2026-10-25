using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMConfig
{
    // The "RBM Debug & Logging" section: developer mode plus every diagnostic log toggle, gathered from the
    // Combat / AI / Campaign sections they used to sit in. Declarations only, like the other category files; the
    // selectors are built, read back and reset in RBMConfigViewModel.Core.cs. The backing fields are in
    // Config/RBMConfig.Debug.cs.
    internal partial class RBMConfigViewModel
    {
        public TextViewModel DeveloperModeText { get; }
        public SelectorVM<SelectorItemVM> DeveloperMode { get; }

        // Field-battle blow-by-blow log, the counterpart to the auto-resolve trace, meant to be read against it.
        public TextViewModel BattleHitLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> BattleHitLoggingEnabled { get; }

        // Per-mission record of which troop perks the game actually asked about (logs/troopperks). Needs Troop Perks.
        public TextViewModel TroopPerkLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> TroopPerkLoggingEnabled { get; }

        // Per-second tactic / behavior / movement-order trace of every team and formation (logs/ai).
        public TextViewModel AiBehaviorLogEnabledText { get; }
        public SelectorVM<SelectorItemVM> AiBehaviorLogEnabled { get; }

        // Deep-water nav blocker log (logs/deepwater). Needs Deep water blocks AI paths in the AI section.
        public TextViewModel DeepWaterNavLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> DeepWaterNavLoggingEnabled { get; }

        // Battlefield terrain read log (logs/terrain). The read runs anyway with RBM AI on; this only writes it down.
        public TextViewModel BattleTerrainLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> BattleTerrainLoggingEnabled { get; }

        // Blunt trauma / penetration of the player's blows, printed to the message log.
        public TextViewModel ArmorPenetrationMessageText { get; }
        public SelectorVM<SelectorItemVM> ArmorPenetrationMessage { get; }

        public TextViewModel SimulationLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> SimulationLoggingEnabled { get; }

        // The blow-by-blow detail inside the auto-resolve trace. Off just trims the per-hit lines; the per-round
        // summary still writes while Detailed Auto Resolve Logging is on. Needs Detailed Auto Resolve Logging on.
        public TextViewModel SimulationLogHitsText { get; }
        public SelectorVM<SelectorItemVM> SimulationLogHits { get; }

        // The troop-power breakdown written to logs/powerCalculation. On/off only; needs Equipment Based Troop Power on.
        public TextViewModel StrategicPowerLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> StrategicPowerLoggingEnabled { get; }

        public TextViewModel SpoilsLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> SpoilsLoggingEnabled { get; }

        public TextViewModel SpoilsVerboseLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> SpoilsVerboseLoggingEnabled { get; }

        // Economy logging (village production, villager dispatches, town food): on/off.
        public TextViewModel EconomyLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> EconomyLoggingEnabled { get; }

        // Supply-caravan logging (logs/caravans): on/off.
        public TextViewModel CaravanLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> CaravanLoggingEnabled { get; }

        // Garrison-refill AI logging (logs/garrison): on/off. Used to write whenever RBM Campaign was on.
        public TextViewModel GarrisonRefillLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> GarrisonRefillLoggingEnabled { get; }

        // Mercenary companies logging (logs/mercenaries): on/off.
        public TextViewModel MercenaryLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> MercenaryLoggingEnabled { get; }

        // AI lords' wage budget and army check logging (logs/partybudget): on/off.
        public TextViewModel PartyBudgetLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> PartyBudgetLoggingEnabled { get; }

        // Stuck-party logging (logs/stuckparties): on/off.
        public TextViewModel StuckPartyLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> StuckPartyLoggingEnabled { get; }

        // Settlement patrol logging (logs/patrols): on/off.
        public TextViewModel PatrolLoggingEnabledText { get; }
        public SelectorVM<SelectorItemVM> PatrolLoggingEnabled { get; }

        // Section title. Own RBM_DBG_* id block, so the new strings cannot collide with the crowded RBM_CON_* ids.
        [DataSourceProperty]
        public string RBMDebugt
        {
            get { return new TextObject("{=RBM_DBG_001}RBM Debug & Logging").ToString(); }
        }

        [DataSourceProperty]
        public string DeveloperModet
        {
            get { return new TextObject("{=RBM_DBG_002}Developer Mode").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel DeveloperModeHint { get; } = Hint("{=RBM_DBG_003}Developer extras: an in-battle stats overlay, an \"RBM Developer Stats\" block in weapon tooltips and the siege archer-point debug pass. Takes effect from the next battle. Not meant for normal play. Default off.");

        [DataSourceProperty]
        public string BattleHitLoggingt
        {
            get { return new TextObject("{=RBM_CON_095}Field Battle Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel BattleHitLoggingEnabledHint { get; } = Hint("{=RBM_DBG_011}Writes every blow of a fought battle to logs/battles next to the config, one file per battle: attacker, target, weapon, body part, armor, damage and health left, plus the standings every 15 seconds and a summary at the end. Arenas and town visits are left out. Default off.");

        [DataSourceProperty]
        public string TroopPerkLoggingt
        {
            get { return new TextObject("{=RBM_DBG_012}Troop Perk Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel TroopPerkLoggingEnabledHint { get; } = Hint("{=RBM_DBG_013}For each battle or other mission, writes to logs/troopperks next to the config which perks from the troop perks file the game actually checked for the troops that fought: perks that took effect (how often, and which game code asked), perks checked where their effect does not apply, and perks never checked, which had no effect. For testing the troop perks file. Needs Troop Perks and RBM Combat on. Takes effect from the next battle. Default off.");

        [DataSourceProperty]
        public string AiBehaviorLogt
        {
            get { return new TextObject("{=RBM_DBG_004}AI Behavior Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel AiBehaviorLogEnabledHint { get; } = Hint("{=RBM_DBG_005}Writes, second by second, every team's chosen tactic and every formation's behavior and movement order to logs/ai next to the config. Needs RBM AI on. Takes effect from the next battle. Default off.");

        [DataSourceProperty]
        public string DeepWaterNavLoggingt
        {
            get { return new TextObject("{=RBM_DBG_DEEPWATER_LOG}Deep Water Path Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel DeepWaterNavLoggingEnabledHint { get; } = Hint("{=RBM_DBG_DEEPWATER_LOG_HINT}For each field battle, writes to logs/deepwater next to the config how much of the map was blocked as deep water and how deep it was, whether the two armies could still reach each other, the water depth where soldiers started and stopped swimming, and any soldier stuck in the water. For tuning Deep water blocks AI paths. Needs that setting and RBM AI on. Takes effect from the next battle. Default off.");

        [DataSourceProperty]
        public string BattleTerrainLoggingt
        {
            get { return new TextObject("{=RBM_DBG_TERRAIN_LOG}Battle Terrain Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel BattleTerrainLoggingEnabledHint { get; } = Hint("{=RBM_DBG_TERRAIN_LOG_HINT}For each field battle, writes to logs/terrain next to the config how the AI reads the battlefield once the armies are deployed: heights and slopes between the armies, frontages, open ground and obstacles, narrow gaps, flanking routes, hills, charge run-ups, fields of fire, weather and a label for the kind of ground. Needs RBM AI on. Takes effect from the next battle. Default off.");

        [DataSourceProperty]
        public string ArmorPenetrationMessaget
        {
            get { return new TextObject("{=RBM_DBG_006}Armor Penetration Messages").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel ArmorPenetrationMessageHint { get; } = Hint("{=RBM_DBG_007}Prints the blunt trauma and armor penetration damage of every blow you deal or take to the message log. Default off.");

        [DataSourceProperty]
        public string SimulationLoggingt
        {
            get { return new TextObject("{=RBM_CON_094}Detailed Auto Resolve Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel SimulationLoggingEnabledHint { get; } = Hint("{=RBM_DBG_010}Writes every auto-resolved battle to logs/simulation next to the config: the sides and their parties, troop power, charge and volley figures, and the outcome including routed men. Needs Detailed Auto Resolve on. Default off.");

        [DataSourceProperty]
        public string SimulationLogHitst
        {
            get { return new TextObject("{=RBM_CFG_SIM_HIT_DETAIL}Auto Resolve Per-Hit Detail").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel SimulationLogHitsHint { get; } = Hint("{=RBM_CFG_SIM_HIT_DETAIL_HINT}Adds every simulated blow to the auto-resolve log -- thousands of lines for a big battle. Needs Detailed Auto Resolve and Detailed Auto Resolve Logging on. Default off.");

        [DataSourceProperty]
        public string StrategicPowerLoggingt
        {
            get { return new TextObject("{=RBM_CFG_POWER_LOG}Troop Power Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel StrategicPowerLoggingEnabledHint { get; } = Hint("{=RBM_CFG_POWER_LOG_HINT}Once a day, writes every party's troop power breakdown to logs/powerCalculation next to the config: its commander's perks and each troop stack's power per man. Needs Equipment Based Troop Power and RBM Combat on. Default off.");

        [DataSourceProperty]
        public string SpoilsLoggingt
        {
            get { return new TextObject("{=RBM_CON_039}Spoils Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel SpoilsLoggingEnabledHint { get; } = Hint("{=RBM_DBG_009}Writes the spoils economy to logs/campaign next to the config: purse changes, loot awards, troop upgrades and their supply-town draws, recruit gear, food and carousing. Needs RBM Campaign on. Default off.");

        [DataSourceProperty]
        public string SpoilsVerboseLoggingt
        {
            get { return new TextObject("{=RBM_CON_049}Verbose Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel SpoilsVerboseLoggingEnabledHint { get; } = Hint("{=RBM_DBG_008}Adds a line for every troop stack to the spoils log; off keeps only the party-level summaries. Needs Spoils Logging on. Default off.");

        [DataSourceProperty]
        public string EconomyLoggingt
        {
            get { return new TextObject("{=RBM_CON_107}Economy Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel EconomyLoggingEnabledHint { get; } = Hint("{=RBM_CON_108}Writes the village-to-town goods and food chain to logs/economy next to the config: each village's daily production, every villager party sent out with its size, composition and cargo, each town's rations, and the end-of-day state of every settlement. Verbose, and only useful for tuning the economy. Default off.");

        [DataSourceProperty]
        public string CaravanLoggingEnabledt
        {
            get { return new TextObject("{=RBM_CFG_CARAVAN_LOG}Caravan Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel CaravanLoggingEnabledHint { get; } = Hint("{=RBM_CFG_CARAVAN_LOG_HINT}Writes the supply-caravan system to logs/caravans next to the config: each caravan dispatched, its arrival and sale, capital injected and repaid, and any lost on the road. Needs Kingdom Supply Caravans on. Default off.");

        [DataSourceProperty]
        public string GarrisonRefillLoggingEnabledt
        {
            get { return new TextObject("{=RBM_DBG_014}Garrison Refill Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel GarrisonRefillLoggingEnabledHint { get; } = Hint("{=RBM_DBG_015}Writes the garrison-refill AI to logs/garrison next to the config: depleted lords sent to their own surplus garrisons, the troops those garrisons hand over, and AI armies forming and dispersing. Needs RBM Campaign on. Default off.");

        [DataSourceProperty]
        public string MercenaryLoggingEnabledt
        {
            get { return new TextObject("{=RBM_DBG_MERC_LOG}Mercenary Companies Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel MercenaryLoggingEnabledHint { get; } = Hint("{=RBM_DBG_MERC_LOG_HINT}Writes the mercenary companies to logs/mercenaries next to the config: companies created, respawned and destroyed, where they roam and what they hire, every contract (hire, daily pay, end and the reason) and which companies an AI army passed over and why. Needs Mercenary Companies on. Default off.");

        [DataSourceProperty]
        public string PartyBudgetLoggingEnabledt
        {
            get { return new TextObject("{=RBM_DBG_BUDGET_LOG}Party Budget Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel PartyBudgetLoggingEnabledHint { get; } = Hint("{=RBM_DBG_BUDGET_LOG_HINT}Writes the AI lords' party budgets to logs/partybudget next to the config: once a day per clan, its gold on hand, steady income, daily wage budget and each war party's share of it, and once a day per clan leader, whether he could raise an army (his party's strength, food, influence, and the parties he could call). Needs RBM Campaign on. Default off.");

        [DataSourceProperty]
        public string StuckPartyLoggingEnabledt
        {
            get { return new TextObject("{=RBM_DBG_STUCK_LOG}Stuck Party Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel StuckPartyLoggingEnabledHint { get; } = Hint("{=RBM_DBG_STUCK_LOG_HINT}Writes to logs/stuckparties next to the config every map party (lords, caravans, villagers, fleets) that has a destination but has not moved for a day: where it is, where it is trying to go, whether it is at sea or heading for a port, its ships, and whether the game found a path. A second line when it moves again. For reporting parties stuck on coasts. Needs RBM Campaign on. Default off.");

        [DataSourceProperty]
        public string PatrolLoggingEnabledt
        {
            get { return new TextObject("{=RBM_DBG_PATROL_LOG}Patrol Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel PatrolLoggingEnabledHint { get; } = Hint("{=RBM_DBG_PATROL_LOG_HINT}Writes the settlement patrols to logs/patrols next to the config: each patrol raised (size, quality, kit cost) and refilled (men bought, cost), each patrol's daily wage and upkeep against what it was owed, patrols stood down (and why) or lost, and a settlement's patrol funds, budget and size on a day they changed. Needs Settlement Patrols and RBM Campaign on. Default off.");
    }
}
