using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace RBMConfig
{
    public static partial class RBMConfig
    {
        //RBMCampaign
        // An upgrade costs what the better kit is worth over the old, scaled by this, and it costs the
        // same number whether it is paid in gold or in spoils. One point of spoils is therefore one
        // gold piece, and everything a troop earns or spends can be quoted in either without conversion.
        // Zero makes upgrades free and disables the spoils system with them.
        public static float troopUpgradeCostMultiplier = 1f;

        public static float troopUpgradeSpoilsLootMultiplier = 1f;

        // SupplyTown gate: troops may only be upgraded while a friendly or neutral town is within
        // TroopUpgradeSupplyRadius map units of the party, and the upgrade buys its kit from that town.
        // False (0) restores upgrade-anywhere and the plain gold sink.
        public static bool troopUpgradeRequireSupplyTown = true;

        // RecruitSupply draw: a man mustered out of a settlement is outfitted from its market -- a town's
        // own, a village's trade-bound town's -- and value-appropriate stock leaves it. A soft sink: it
        // draws what the market has and never blocks a recruitment. False (0) recruits gear from nowhere.
        public static bool recruitDrawsFromSettlementStock = true;

        // How near, in campaign-map units, a friendly town must be to supply a party's upgrades. Roughly
        // a short march at the default; raise it to be lenient, lower it to force troops back to town.
        public static float troopUpgradeSupplyRadius = 30f;

        // Charge the mount in gold: a troop upgrading into a mounted tier no longer needs a horse item in
        // the baggage train (none is consumed); the horse and harness are priced into the upgrade cost
        // instead. False (0) restores the native horse-item requirement and mount-less upgrade pricing.
        public static bool troopUpgradeChargeMountValue = true;

        // Pieces of kit one man carries off a battlefield, however much of it he sees lying there.
        public static int troopLootPiecesPerMan = 3;

        // Chance a troop overlooks a piece of kit for each tier it sits beneath his own, compounded:
        // at 0.5 a veteran sees half of what is one tier under him and a quarter of what is two. He
        // never overlooks kit of his own tier or better. At 1 he sees nothing beneath him at all.
        public static float troopLootOverlookChancePerTier = 0.5f;

        // Troop wages are NOT configurable. They are a fixed historical pay table in
        // Wages/TierBasedWageModel.cs (foot 20/30/40/60/120/240, horse 30/40/60/120/240/480, tier 0 paid
        // as tier 1), and it applies whenever this module is on. The old TroopWageTierBase dial read as a
        // per-tier multiplier long after the wage stopped being computed that way; once the table landed
        // the number meant nothing but "not zero", so it was removed rather than left to mislead. To get
        // the vanilla wage back, turn the campaign module off.

        // The daily cost of keeping a soldier in the field, as a share of his whole kit's worth -- his
        // gear, his horse and its armour alike. A lancer in full harness costs more to maintain than a
        // spearman, in proportion to what he carries. Paid first out of the stack's own spoils; whatever
        // the purse cannot cover falls to the party leader, out of his gold. Zero stops maintenance.
        public static float troopMaintenanceFraction = 0.005f;

        // How much of each stack's daily maintenance the men's own spoils may cover, by the party's
        // standing in the field. A mercenary company in a kingdom's pay meets this share from its purses,
        // its employer the rest; the leftover, as ever, falls to the party leader's gold. A sworn
        // vassal's or ruler's men pay none from their purses (their liege bears it all, not configurable).
        public static float mercenaryMaintenancePurseFraction = 0.5f;

        // How much of each stack's daily maintenance the men's own spoils may cover for an independent
        // clan -- one sworn to no kingdom. At the default their men fund their upkeep in full from their
        // purses, whatever the purse cannot meet falling to the party leader's gold as any shortfall does.
        public static float independentMaintenancePurseFraction = 1.0f;

        // How long a stack's men stay fed on one visit to a settlement. They buy exactly the food they
        // will eat over that span at the game's own rate of one item per twenty men per day, so at 20
        // days each man carries off one item. Zero stops troops buying food.
        public static int troopSettlementFoodDays = 20;

        // Days of maintenance a recruit mustered from a village or town brings in his stack's purse, so a
        // fresh soldier arrives with his kit in order and a little put by rather than penniless. Priced
        // off the same daily upkeep the maintenance charge uses. Zero seeds nothing.
        public static int recruitMaintenanceDays = 20;

        // Share of a day's wage a man will lay out on a day's rations before he calls it extravagant.
        // Nothing else sets a soldier's taste, so raising this feeds veterans on meat and cheese while
        // recruits still buy grain. Zero leaves everyone eating whatever is cheapest.
        public static float troopFoodWageFraction = 0.5f;

        // A day's wage a stack drinks and gambles away for each day it sits in a settlement. At 1 the
        // men spend everything the day paid them; above that they eat into what they came in with, so
        // a long stay in town costs a stack the kit it was saving for.
        //
        // A quarter, down from one and a half. At 1.5 a soldier drank half again what he earned, every
        // day, forever -- which left nothing for his kit and made carousing 98% of all money entering a
        // town: 62,333 denars per town per day, almost exactly what the old gold controller was
        // destroying to hide it. A quarter is a man spending a fair share of his pay on drink and
        // keeping the rest.
        public static float troopSettlementFunWageFraction = 0.25f;

        // Share of a sacked village's plundered wealth its soldiers pocket as spoils, on top of the
        // goods the party carts off. Scaled against how much of the village the raid actually stripped.
        // Zero leaves raiding paying the party but not its men.
        public static float troopRaidSpoilsMultiplier = 0.25f;

        // The base share of the spoils a party's men gather -- off a battlefield, a raid or a sack -- that
        // their leader skims into his own purse as gold before the rest settles into the stacks: a
        // commander's cut. Multiplied by the leader's clan tier plus one, so a tier-0 or clanless leader
        // takes this share once over and a tier-6 dynasty seven times it. Drawn out of the same purses the
        // gather just filled, so it moves coin from the men's pool into their keeper's treasury rather than
        // minting any. 0 leaves the men all they take.
        public static float troopLeaderSpoilsCutFraction = 0.05f;

        // Days of keep a stack holds in its purse before its upkeep spends the surplus: this many days'
        // worth of its daily wage and its daily field maintenance together set the ceiling. Higher lets
        // a stack sit on a deeper reserve; zero holds it to nothing above what its upkeep spends at once.
        public static int troopSpoilsCapDays = 20;

        // Days a stack waits after buying a luxury before it will indulge again, so the splurge stays
        // an occasional treat rather than a daily habit. Kept per stack. Zero lets it buy on every roll.
        public static int troopLuxuryCooldownDays = 20;

        // The chance, each hour a stack idles in a settlement holding more spoils than its cap, that it
        // buys a luxury off the market. Small: over a full day's stay the odds add up. Zero stops it.
        public static float troopLuxurySpendChance = 0.02f;

        // Gold a wounded man's stack pays the local surgeons, per tier he holds, to mend him faster than
        // he would heal on the march while the stack rests in a settlement. A veteran costs more to patch
        // up than a recruit, and his richer purse can bear it. Drawn from the stack's own spoils and left
        // in the settlement the way carousing is. Zero stops troops paying to heal.
        public static int troopSpoilsHealGoldPerTier = 10;

        // The most of a stack's wounded that paid healing can mend in a single hour, so even a deep purse
        // buys a faster recovery rather than an instant one. A stay in town still takes a bad wounding a
        // while to clear; it just costs the stack its savings.
        public static float troopSpoilsHealFractionPerHour = 0.05f;

        // Share of a beaten enemy's fallen-and-wounded spoils the victors strip off the field; the rest
        // is trampled and lost. Split across the winning parties by their part in the battle, and within
        // a party across its stacks by weight -- men times tier -- so veterans take the larger cut. Zero
        // leaves the dead's purse on the field.
        public static float troopFallenSpoilsCaptureFraction = 0.75f;

        // The purse the player opens a new campaign with, replacing whatever his backstory choices added
        // up to. RBM prices most of the campaign well above vanilla -- kit, trade goods, upgrades paid out
        // of a spoils purse -- so the few hundred denars character creation hands out leaves none of the
        // opening decisions affordable. New games only; a loaded save keeps the gold it was saved with.
        public static int campaignStartingGold = 5000;

        // Replaces the vanilla worth and weight of the trade goods with historically derived figures --
        // a period price in denars times ten, and the real mass in kilograms of one lot. Value and weight
        // move together, so a cart of velvet is no longer worth what a cart of hardwood is. Off restores
        // the game's own numbers. Only the trade goods are touched; no other item changes.
        public static bool realisticTradeGoodPrices = true;

        // Adds a weight column to the inventory and trade item rows, showing the unit weight of one of
        // the item. With the goods repriced above, weight spans four orders of magnitude and decides
        // what a party can profitably carry, so it stops being a footnote. The column is carved out of
        // the item name field. Takes effect on the next game start: Gauntlet caches a parsed prefab.
        public static bool showInventoryItemWeight = true;

        // Writes every spoils pool change, loot award and upgrade to rbm_spoils.log next to this config.
        public static bool spoilsLoggingEnabled = true;

        // Whether that log carries the full per-stack detail or only the party-level summaries. On, it
        // reads as now: a line per stack. Off, individual-soldier lines are dropped and only what each
        // party did is kept. No effect unless logging above is on.
        public static bool spoilsVerboseLoggingEnabled = true;

        // Writes the village-to-town goods and food chain -- village production, villager dispatches,
        // town rations, and the daily state of every settlement -- to its own logs/economy folder.
        // Separate from the spoils log because it is about the countryside, not the troops' purses.
        public static bool economyLoggingEnabled = true;
    }
}
