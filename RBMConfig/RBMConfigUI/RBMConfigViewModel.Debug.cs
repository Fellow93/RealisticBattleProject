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

        // Per-second tactic / behavior / movement-order trace of every team and formation (logs/ai).
        public TextViewModel AiBehaviorLogEnabledText { get; }
        public SelectorVM<SelectorItemVM> AiBehaviorLogEnabled { get; }

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
        public string AiBehaviorLogt
        {
            get { return new TextObject("{=RBM_DBG_004}AI Behavior Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel AiBehaviorLogEnabledHint { get; } = Hint("{=RBM_DBG_005}Writes, second by second, every team's chosen tactic and every formation's behavior and movement order to logs/ai next to the config. Needs RBM AI on. Takes effect from the next battle. Default off.");

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
        public string SimulationLogHitst
        {
            get { return new TextObject("Auto Resolve Per-Hit Detail").ToString(); }
        }

        [DataSourceProperty]
        public string StrategicPowerLoggingt
        {
            get { return new TextObject("Troop Power Logging").ToString(); }
        }

        [DataSourceProperty]
        public string SpoilsLoggingt
        {
            get { return new TextObject("{=RBM_CON_039}Spoils Logging").ToString(); }
        }

        [DataSourceProperty]
        public string SpoilsVerboseLoggingt
        {
            get { return new TextObject("{=RBM_CON_049}Verbose Logging").ToString(); }
        }

        [DataSourceProperty]
        public string EconomyLoggingt
        {
            get { return new TextObject("{=RBM_CON_107}Economy Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel EconomyLoggingEnabledHint { get; } = Hint("{=RBM_CON_108}Writes the village-to-town goods and food chain to logs/economy next to the config: each village's daily production, every villager party sent out with its size, composition and cargo, each town's rations, and the end-of-day state of every settlement. Verbose, and only useful for tuning the economy.");

        // Plain literal TextObjects (no {=KEY}), as they were in the Campaign section.
        [DataSourceProperty]
        public string CaravanLoggingEnabledt
        {
            get { return new TextObject("Caravan Logging").ToString(); }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel CaravanLoggingEnabledHint { get; } = Hint("Writes the supply-caravan system to logs/caravans next to the config: each caravan dispatched, its arrival and sale, capital injected and repaid, and any lost on the road. Needs Kingdom Supply Caravans on. Default off.");
    }
}
