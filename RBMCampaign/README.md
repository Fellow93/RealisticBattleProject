# RBM Campaign Module

It gives every stack of troops in your party its own purse, fills it with the gear they strip off the battlefield and a share of their wages, and makes them spend it on their own upgrades, their own food, and their own drink.

## Upgrades are priced by the kit

Vanilla charges a flat, hand-authored gold number to upgrade a troop. RBM replaces that with the actual value of the equipment the man gains — his new armor, weapons, shield and horse, minus what he already carried, averaged over all the loadouts that troop type can spawn with. A recruit becoming a footman is cheap. A veteran stepping into heavy mail and a warhorse is not. (All the vanilla perks that discount upgrades — Sound Reserves, Renowned Archer, Contractors, the Khuzait cavalry feat — still apply on top.)

## Spoils: where the money comes from

Each troop stack (say, your 40 Imperial Recruits) holds a stockpile of **spoils**, measured in gold. One point of spoils is one gold piece — it just lives in the soldiers' pockets rather than yours.

Two things fill it:

**Winning battles.** Hold the field and your men strip the dead — the enemy's dead *and* your own fallen. The wounded keep their kit (they're carried off wearing it) and routers fled with theirs. Nothing comes off a field intact: every piece salvages a random quarter to three-quarters of its worth, because armor is battered, blades are chipped, and a quiver is only worth the arrows still in it. Your share of the field scales with how much your party actually contributed to the battle.

How that loot gets divided is the interesting part. **Veterans pick first**, but each man can only carry so much (three pieces by default), and the further beneath a soldier a piece of gear lies, the likelier he is to walk straight past it — roughly a coin-flip per tier of difference. So the good kit goes to the elite stacks, and the recruits' spears and rags get left lying for the greener troops who'd actually consider them an upgrade. A veteran alone on a field will eventually stoop for a peasant's club, but he won't fill his arms with them while there's mail about.

**Raiding villages.** When your party sacks a village, its soldiers pocket coin and plate as they go, on top of the goods you cart off. What they take scales with how thoroughly the village was stripped — a raid you saw through to the end pays more than one you broke off — and with the village's wealth. A raid the militia or a relief force turned back pays nothing. The plunder is shared evenly among the men who did the raiding.

**Storming towns.** Take a town or castle by siege and the men who stormed it sack it, pocketing plunder scaled to the settlement's prosperity — a rich town is a far bigger prize than a village, enough to fund a whole wave of upgrades. Only a capture by storm sacks the place: a fief handed to you by barter, gift or a council vote leaves its wealth alone. The plunder goes to the party credited with the capture.

**Wages.** Half of what you pay a stack each day (by default) goes straight back into its own purse. This doesn't cost you extra gold — it's a statement about where their pay was already going: mending what the march wore through, replacing what couldn't be mended.

## Where the money goes

**Upgrades.** When you upgrade a stack, its spoils are spent one man at a time. If the stockpile covers three men, those three upgrade free and the fourth pays full price out of your treasury. The party screen tooltip spells this out — a **Spoils cover: X** line and a **You pay: Y** line — and there's a spoils bar on each troop card showing how full the stockpile is.

**Food.** When your party stops in a town or village, soldiers buy their own rations off the local market — real items, taken from that settlement's actual stock, at that settlement's actual prices. While a stack is carrying its own food, it doesn't eat out of your party's food stores at all. Better-paid troops buy better food: a man will spend up to half a day's wage on a day's rations, so a recruit buys grain and a veteran buys meat and cheese. If a starving village has nothing on the stalls, your men leave hungry.

**Drink.** Every hour your party idles in a settlement, the men spend on taverns, dice and worse — at the default, more than they earn in a day. **A garrison parked in a town will drink its way out of ever affording better armor.** They never go into debt; an empty purse spends nothing.

**Surplus handed up as gold.** A stack keeps only what it can still put to use — enough to finish its own upgrades, plus a war chest that grows with the men's tier (a veteran keeps a deeper purse than a recruit) — and hands a share of the rest up to you as gold each day. A top-tier stack has nothing left to buy, so nearly everything it loots and earns comes straight back to your treasury: an elite army you've stopped upgrading becomes a passive income instead of a pile of stranded loot. How much of the surplus is swept up is a dial — at the default it all comes up at once, lower and it drains down to the cap over several days, at zero spoils stay a closed loop spent only on troops, food and drink.

## The settlement economy

Coin doesn't vanish when it changes hands in a town or village — it settles into the place. One rate runs the whole exchange, both directions: what a settlement takes in raises its Prosperity (a town or castle) or its Hearth (a village); what it lays out drains the same. Set that rate to zero and this entire layer switches off.

**Trade feeds the place.** Whatever your soldiers spend there on food and drink stays where they spent it, and so does ordinary trade — every purchase made at a market, yours or any other party's, a caravan's or a lord's, feeds the settlement it was bought from. A busy market grows the town that hosts it.

**Militia earn their keep.** The other way round, a settlement pays for the militia that defend it. Each day their wages — priced off their kit, like every other troop — are drawn straight out of the place they hold: a town or castle's Prosperity, a village's Hearth. A settlement that raises more militia than its economy can carry bleeds for it, and because militia swells with prosperity in the first place, the drain reins itself in.

**Making things costs something.** So does production. Every good a settlement turns out — a workshop's wares in a town, a village's crops and raw goods — is worked out of its own back: the item's worth is drawn off the town's Prosperity or the village's Hearth. A place only comes out ahead where its goods actually sell: production spends it down, trade builds it back up, and idle stock that no one carries off is just wealth spent and waiting on a buyer.

**Produce sold brings the coin home.** When a village's own people cart its produce to town and sell it, the takings feed the Hearth of the village it came from — not the town it was sold at. That closes the village's loop: it bleeds Hearth growing its goods and wins it back once they find a buyer, so a village on a busy trade road thrives and one whose produce rots on the road withers.

## What this changes about how you play

Battles now pay for your army's growth directly. A hard-won victory over well-equipped enemies funds a wave of upgrades that would otherwise have drained your treasury. Grinding looters funds nothing, because looters wear nothing worth taking. Sitting still costs you — an idle army eats and drinks its savings. Losing a stack loses its purse with it.

The AI plays by the same rules. Lords' parties accumulate spoils, get discounted upgrades from them, and their troop quality now tracks how well their wars have been going.

## Tuning it

Everything sits in the in-game RBM config under the campaign section, or in the config XML under `/Config/RBMCampaign`:

| Setting | Default | What it does |
| --- | --- | --- |
| `TroopUpgradeCostMultiplier` | 1 | Scales the gold and spoils price of every upgrade. **0 turns the whole system off.** |
| `TroopUpgradeSpoilsLootMultiplier` | 1 | How much of a battlefield's salvage your men actually carry off. |
| `TroopLootPiecesPerMan` | 3 | Pieces of kit one soldier can carry away from a field. |
| `TroopLootOverlookChancePerTier` | 0.5 | Chance a soldier walks past a piece of gear, per tier it lies beneath him. |
| `TroopWageSpoilsFraction` | 0.5 | Share of a stack's daily wage that returns to its own purse. |
| `TroopRaidSpoilsMultiplier` | 0.25 | Plunder its soldiers pocket as spoils when they sack a settlement — a share of a raided village's wealth, or of a stormed town's prosperity. 0 turns plunder spoils off. |
| `TroopSettlementFoodDays` | 20 | Days of rations a stack buys for itself when it reaches a market. |
| `TroopFoodWageFraction` | 0.5 | Share of a day's wage a man will spend on a day's food before calling it extravagant. |
| `TroopSettlementFunWageFraction` | 1.5 | Drink and dice, as a multiple of the daily wage, per day idled in a settlement. |
| `SettlementProsperityPerGoldSpent` | 0.02 | Prosperity (or Hearth) a gold moves at a settlement, both ways — trade and carousing there add it, its militia's daily wages and every good it produces drain it. 0 turns all of it off. |
| `TroopSpoilsGoldSpillFraction` | 0.02 | Most of one man's share of a stack's surplus spoils — what it holds over what its upgrades could use — that can hand up to you in a day, priced as this share of his battle kit the way a wage is, so a better-armed man hands up more. A daily cap, so a deep surplus drains slowly; 0 keeps spoils a closed loop. |
| `TroopSpoilsWarChestGoldPerTier` | 25 | Gold a man keeps back from the spill, per tier he holds (a tier 6 keeps 6× this). On top of what his stack needs for its own upgrades. |
| `SpoilsLoggingEnabled` | 1 | Writes what the system is doing to the log, for debugging. |
