namespace RBMConfig
{
    // Debug & Logging: every diagnostic switch, gathered here to match the config screen's "RBM Debug & Logging"
    // section (RBMConfigViewModel.Debug.cs). Only the C# declarations moved: each value keeps its old XML node
    // (under RBMCampaign / RBMAI / RBMCombat/Global) so existing config files load unchanged. Load and save are in
    // RBMConfig.Core.cs with everything else.
    public static partial class RBMConfig
    {
        // Developer extras: the in-battle stats overlay, the "RBM Developer Stats" block in weapon tooltips and the
        // siege archer-point debug pass. Read when a mission starts, so a change applies from the next battle.
        // Stored as /Config/DeveloperMode ("1"/"0"); off by default.
        public static bool developerMode = false;

        // Writes, second by second, every team's chosen tactic and every formation's active behavior and
        // movement order to logs/ai -- the only way to see why the enemy team's infantry and the player's
        // delegated team, running the same code, do not do the same thing.
        public static bool aiBehaviorLogEnabled = false;

        // Prints the blunt trauma and armor penetration of every blow the player deals or takes to the message log.
        public static bool armorPenetrationMessage = false;

        // Writes every spoils pool change, loot award and upgrade to rbm_spoils.log next to this config.
        public static bool spoilsLoggingEnabled = false;

        // Whether that log carries the full per-stack detail or only the party-level summaries. On, it
        // reads as now: a line per stack. Off, individual-soldier lines are dropped and only what each
        // party did is kept. No effect unless logging above is on.
        public static bool spoilsVerboseLoggingEnabled = false;

        // Writes the village-to-town goods and food chain -- village production, villager dispatches,
        // town rations, and the daily state of every settlement -- to its own logs/economy folder.
        // Separate from the spoils log because it is about the countryside, not the troops' purses.
        public static bool economyLoggingEnabled = false;

        // Writes the supply-caravan system to its own logs/caravans folder -- each caravan dispatched,
        // its arrival and sale, and any lost on the road. No effect unless kingdomCaravansEnabled is on.
        public static bool caravanLoggingEnabled = false;

        // Writes the garrison-refill AI to its own logs/garrison folder -- depleted lords steered to their own
        // surplus garrisons, the troops those garrisons release, and AI armies forming and dispersing. No effect
        // unless rbmCampaignEnabled is on. Used to write whenever the campaign module was on.
        public static bool garrisonRefillLoggingEnabled = false;

        // Writes every party out as it was priced -- the perks that reached it, then each stack with what one man of
        // it is worth and what he is made of -- to logs/powerCalculation. None of the model's constants are derived,
        // so this is how they get tuned. One block per party per in-game day; see StrategicPowerLog for why.
        public static bool strategicPowerLoggingEnabled = false;

        // Writes every auto-resolved battle to its own log under logs/simulation, as it was actually fought: who
        // stood on each side, what they carried, how it ended. Costs nothing while off -- no battle is snapshotted
        // and no blow is recorded.
        public static bool simulationLoggingEnabled = false;

        // And the battle itself, BLOW BY BLOW: every man who swung, what he was doing at the time (shooting,
        // hurling a javelin, charging, setting a spear, or just walking into arrows while the lines closed), what
        // armour he met, what his shield turned aside, what vanilla alone would have hit for, and what the model
        // made of it. The matchup table says what a blow would do in the abstract; only this can tell you the
        // archers ran dry in round fifteen. A large battle runs to several thousand lines. Needs the log above.
        public static bool simulationLogHits = false;

        // Writes every blow of a REAL battle -- the one fought on the field -- to logs/battles, in the same columns
        // the auto-resolve trace uses, so what the simulation CLAIMS a battle is can be held against one that
        // actually happened: who was shooting, who had reached anybody yet, what armour a blow met, what it did.
        // Off by default. A real battle lands thousands of blows and each is a line.
        public static bool battleHitLoggingEnabled = false;

        // Writes, per mission, which perks from rbm_troop_perks.xml the game actually asked about for the listed
        // troops that fought -- granted (how often, and from which caller), asked where the effect does not apply,
        // or never asked (no effect at all) -- to logs/troopperks (RBMCombat CombatModule/TroopPerks/TroopPerkLog*).
        // Kept under /Config/RBMCombat/Global with the hit log. Only meaningful with troopPerksEnabled and
        // rbmCombatEnabled; the mission logic is not even added otherwise. Off by default.
        public static bool troopPerkLoggingEnabled = false;
    }
}
