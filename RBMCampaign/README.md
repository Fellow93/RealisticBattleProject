# RBM Campaign Module

It gives every stack of troops in your party its own purse, fills it with the gear they strip off the battlefield and a share of their wages, and makes them spend it on their own upgrades, their own food, and their own drink.

And it makes that gear matter where you never see it: battles resolved on the map are now decided by what your men are actually wearing and carrying, not by the tier number beside their name.

## Upgrades are priced by the kit

Vanilla charges a flat, hand-authored gold number to upgrade a troop. RBM replaces that with the actual value of the equipment the man gains — his new armor, weapons, shield and horse, minus what he already carried, averaged over all the loadouts that troop type can spawn with. A recruit becoming a footman is cheap. A veteran stepping into heavy mail and a warhorse is not. (All the vanilla perks that discount upgrades — Sound Reserves, Renowned Archer, Contractors, the Khuzait cavalry feat — still apply on top.)

And troops can only upgrade where there's a town to outfit them. A stack upgrades only while a friendly or neutral town sits within reach on the map, and the new kit is bought from that town — march too far from friendly ground and your men make do with what they have until you bring them back within a short ride of a market. (Turn `TroopUpgradeRequireSupplyTown` off to upgrade anywhere, the way vanilla does.)

A troop stepping up to a mounted tier is priced its horse and harness in gold like any other kit, rather than needing a live horse pulled from your baggage train — none is consumed. (Turn `TroopUpgradeChargeMountValue` off to restore vanilla's horse-item requirement.)

## Wages scale with tier

Vanilla pays a soldier by his tier off a hand-authored table. RBM keeps the tier but turns it into one clean dial: a man's daily wage is a flat base value multiplied by his tier, so each rung of the tree costs proportionally more to keep in the field — a tier-3 man three times a tier-1's keep. (Set `TroopWageTierBase` to zero to keep vanilla's wage.)

## Spoils: where the money comes from

Each troop stack (say, your 40 Imperial Recruits) holds a stockpile of **spoils**, measured in gold. One point of spoils is one gold piece — it just lives in the soldiers' pockets rather than yours.

Two things fill it:

**Winning battles.** Hold the field and your men strip the dead — the enemy's dead *and* your own fallen. The wounded keep their kit (they're carried off wearing it) and routers fled with theirs. Nothing comes off a field intact: every piece salvages a random quarter to three-quarters of its worth, because armor is battered, blades are chipped, and a quiver is only worth the arrows still in it. The enemy's own purse is part of the prize too — most of what their fallen and wounded were carrying is captured with the field, the rest lost in the rout. Your share of it all scales with how much your party actually contributed to the battle.

How that loot gets divided is the interesting part. **Veterans pick first**, but each man can only carry so much (three pieces by default), and the further beneath a soldier a piece of gear lies, the likelier he is to walk straight past it — roughly a coin-flip per tier of difference. So the good kit goes to the elite stacks, and the recruits' spears and rags get left lying for the greener troops who'd actually consider them an upgrade. A veteran alone on a field will eventually stoop for a peasant's club, but he won't fill his arms with them while there's mail about.

**Raiding villages.** When your party sacks a village, its soldiers pocket coin and plate as they go, on top of the goods you cart off. What they take scales with how thoroughly the village was stripped — a raid you saw through to the end pays more than one you broke off — and with the village's wealth. A raid the militia or a relief force turned back pays nothing. The plunder is shared evenly among the men who did the raiding.

**Storming towns.** Take a town or castle by siege and the men who stormed it sack it, pocketing plunder scaled to the settlement's prosperity — a rich town is a far bigger prize than a village, enough to fund a whole wave of upgrades. Only a capture by storm sacks the place: a fief handed to you by barter, gift or a council vote leaves its wealth alone. The plunder goes to the party credited with the capture.

**Wages.** All of what you pay a stack each day (by default) goes straight back into its own purse. This doesn't cost you extra gold — it's a statement about where their pay was already going: mending what the march wore through, replacing what couldn't be mended.

## Where the money goes

**Upgrades.** When you upgrade a stack, its spoils are spent one man at a time. If the stockpile covers three men, those three upgrade free and the fourth pays full price out of your treasury. The party screen tooltip spells this out — a **Spoils cover: X** line and a **You pay: Y** line — and there's a spoils bar on each troop card showing how full the stockpile is.

**Maintenance.** Every day in the field a stack pays to keep its own kit serviceable — a small share of everything it carries, so a lancer in full harness costs more to maintain than a spearman. It comes out of the stack's own purse first; whatever the purse can't cover falls to you, drawn from your treasury. Fresh recruits arrive with a few days of upkeep already put by, rather than penniless. (Set `TroopMaintenanceFraction` to zero to stop it.)

**Food.** When your party stops in a town or village, soldiers buy their own rations off the local market — real items, taken from that settlement's actual stock, at that settlement's actual prices. While a stack is carrying its own food, it doesn't eat out of your party's food stores at all. Better-paid troops buy better food: a man will spend up to half a day's wage on a day's rations, so a recruit buys grain and a veteran buys meat and cheese. If a starving village has nothing on the stalls, your men leave hungry.

**Drink.** Every hour your party idles in a settlement, the men spend on taverns, dice and worse — at the default, more than they earn in a day. **A garrison parked in a town will drink its way out of ever affording better armor.** They never go into debt; an empty purse spends nothing.

**Luxuries.** A stack already sitting on more spoils than it could ever spend on its own kit will now and then indulge — buying a luxury off the settlement's market (jewellery, velvet, furs, wine, a fine garment) purely to have it. The coin leaves for the settlement the way carousing does, and the good is a personal keepsake rather than party loot: it can't be carted to the next town and sold back for gold. Only over-cap stacks splurge, only once in a while, and never twice in quick succession.

**Healing.** A stack resting in a settlement pays the local surgeons out of its own purse to mend its wounded faster than they'd knit on the march. A veteran costs more to patch up than a recruit, and only a little of the wounded is mended each hour, so a bad convalescence still means a stay in town — it just costs the stack the kit it was saving for. This runs on top of the game's own free daily healing, so an empty-pursed stack still recovers, only slower.

**A commander's cut.** Before the rest settles into the stacks, the party leader skims a share of everything his men gather — off a battlefield, a raid or a sack — straight into his own purse as gold. A greater lord takes a larger cut: the share is multiplied by his clan's standing, so a landless captain takes it once over and a great dynasty many times it. This is the one place spoils turn back into treasury gold. (Set `TroopLeaderSpoilsCutFraction` to zero to leave the men everything they take.)

Otherwise spoils are a **closed loop**: what a stack loots and earns is spent on its own upgrades, upkeep, food and drink, not handed back as gold. A stack keeps saving toward its next upgrade and a war chest that grows with the men's tier; once it holds more than that, the surplus goes to drink and the odd luxury rather than sitting idle.

## The settlement economy

Coin doesn't vanish when it changes hands in a town or village — it settles into the place. One rate runs the whole exchange, both directions: what a settlement takes in raises its Prosperity (a town or castle) or its Hearth (a village); what it lays out drains the same. Set that rate to zero and this entire layer switches off.

**Trade feeds the place.** Whatever your soldiers spend there on food and drink stays where they spent it, and so does ordinary trade — every purchase made at a market, yours or any other party's, a caravan's or a lord's, feeds the settlement it was bought from. A busy market grows the town that hosts it.

**Militia earn their keep.** The other way round, a settlement pays for the militia that defend it. Each day a fraction of their wages — scaled to each man's tier, like every other troop, but discounted because they're part-time defenders — is drawn straight out of the place they hold: a town or castle's Prosperity, a village's Hearth. A settlement that raises more militia than its economy can carry bleeds for it, and because militia swells with prosperity in the first place, the drain reins itself in.

**Upkeep enriches the nearest fort.** The coin your army spends maintaining its kit in the field has to be spent somewhere, so a share of every party's daily maintenance settles as Prosperity into the nearest fortified town or castle — never a village. An army campaigning near a friendly stronghold quietly feeds it.

**Making things costs something.** So does production. Every good a settlement turns out — a workshop's wares in a town, a village's crops and raw goods — is worked out of its own back: the item's worth is drawn off the town's Prosperity or the village's Hearth. A place only comes out ahead where its goods actually sell: production spends it down, trade builds it back up, and idle stock that no one carries off is just wealth spent and waiting on a buyer.

**Produce sold brings the coin home.** When a village's own people cart its produce to town and sell it, the takings feed the Hearth of the village it came from — not the town it was sold at. That closes the village's loop: it bleeds Hearth growing its goods and wins it back once they find a buyer, so a village on a busy trade road thrives and one whose produce rots on the road withers.

## Detailed auto resolve

When a battle resolves on the map rather than on the field — you send your troops in, or two AI parties meet somewhere you aren't — vanilla decides it almost entirely on one number: the troop's **tier**. Its whole strength is `(2 + tier) × (10 + tier) × 0.02`, and nothing else about the man enters into it. It does not know what he is wearing. It does not know what he is holding. A tier-1 recruit in mail behind a shield, levelling a spear, hits a tier-0 looter exactly **1.41×** as hard as the looter swinging a stick in rags hits back — and that thin margin is the entire difference between them.

That is why your recruits could lose a close fight to looters on the map and slaughter the same looters on the field. The two battles were not being decided by the same things.

RBM takes tier out of the reckoning and puts the soldier back in. A tier was only ever shorthand for *what kit does he carry and how well is he trained* — so we measure those directly, from his actual armor, his actual weapon and his actual skill, and drop the shorthand rather than charge for the same thing twice.

**Armor is real armor now.** Every blow is run through the same armor equation the live battle uses — and *which* equation depends on which combat module is running, so that auto-resolve stays faithful to the battle you would actually have fought. With RBM Combat on, armor is roughly two and a half times more protective than vanilla's, and a heavy harness can stop a cut outright, letting nothing through but the blunt shock of it. With RBM Combat off, vanilla's gentler curve is used instead. Either way the numbers come from the game's own item data, not from a table of guesses.

**What you hit with matters.** A mace against a mail hauberk is not a sword against a mail hauberk. Under RBM's armor rules a blunt weapon carries far more of a stopped blow through as trauma, and a spear-point beats armor a sword-edge cannot — so maces and spears come into their own against heavy troops, exactly as they do on the field. A bow is priced with the arrows it looses, not on its own, and it wounds in the arrow's kind.

**And a soldier is not one weapon.** He carries a spear *and* an axe, and he swings whichever happens to be in his hand — so a blow is priced as the average of everything on his belt, not the best of it. This matters more than it sounds: a mace and a sabre answer armor differently, so they are each run through the armor equation separately and averaged *afterwards*. Averaging them into one weapon first would produce a blow that is neither.

Nor is an archer one arrow. An Imperial Archer carries flight arrows in two of his three kits and needle bodkins in the third, and a bodkin punches through a hauberk that a broadhead barely scratches — so the arrow with the bigger number printed on it is very often the *worse* one against a man in mail. Every kind of shaft in his quiver is priced on its own terms, and the average taken after.

The one exception is a horse. When a rider bears down on him, a man with a spear reaches for the spear — every time, and so does every man beside him who has one. So against cavalry the choice narrows to his polearms, and if he has none he swings what he has and hopes. This is also what lets infantry finally **set a spear against a charge**: a braced polearm lands half again as hard on a horseman, which is the answer foot soldiers have had to cavalry for three thousand years and which auto-resolve has never once let them use.

**And the javelins come off his back first.** Half the infantry in Calradia carry a brace of throwing spears or a few throwing axes, and those are not melee weapons — he hurls them while the lines close and then draws steel. So they are kept off the belt entirely and spent in the volley, where they belong: a throw is a *missile*, priced on the energy his arm actually puts into it, going to the mass of the man and meeting a shield held up against something in flight.

The count is not a guess — it is the item's own stack size, two javelins or three axes — and it is the whole reason the throwing phase ends. A skirmisher is terrifying for twenty seconds and then he is a man with a knife. That is exactly how it should feel, and it is the first time auto-resolve has let him be either.

**Shields stop blows — and stop arrows better still.** A shield is not armor and does not blunt anything; it stops the blow dead or it doesn't. So a shield-bearer turns aside a share of everything thrown at him — two blows in five, for an ordinary shield — and a better shield turns aside proportionally more. A steel round shield and a wooden one look identical on paper (both are the same span) but differ five-fold in how much punishment they take, and that is what separates them here. Carrying the shield your fellows carry is simply what an infantryman does, and counts for nothing special; carrying **none** means eating the blows they would have blocked.

Against **arrows** the same shield does about a third better again. An arrow comes from one known direction and arrives on its own; a man gets the board up and it sticks there. A swordsman feints, comes round the edge, and waits for the shield to drop. This is the whole reason a line advances under fire from behind its shields — and it means a shieldless archer line is now a genuinely poor answer to a shielded one, exactly as it should be.

**Where a blow lands depends on who threw it.** Two footmen are eye to eye, so it is the chest and shoulders and arms that catch it, the head often enough, and the legs almost never. But a man on foot hacking upward at a horseman finds the rider's *legs and lower body* at his eye level — while the rider, cutting downward, finds the footman's *head and shoulders*. So a horse's barding is worth a great deal against infantry and nearly nothing against another lancer, because the infantry are the ones swinging where it is. Arrows ignore all of this and go to the mass of the man, whether he is horsed or not.

**And a battle has a shape now — it is not one long brawl.** It has three acts, and auto-resolve has only ever known about the third: the **volley**, while the lines are far apart and the bowmen have the field; the **skirmish**, on the ground between them; and then the lines meet, which is the part everyone already knew about and the least interesting of the three.

*The skirmish is where the javelins fly and the horse fight the horse.* A man does not hurl a spear at somebody a bowshot away — he carries it across the open ground and throws it when he is close enough, and then it is gone and he is a man with a knife. And each side's cavalry ride out at each other well before the foot are anywhere near, which is what cavalry have always done and what auto-resolve has never let them do: it held every horseman back until the infantry lines collided and then threw him into the press, where a horse is worth least.

*The archers get their volley — and in it, nobody else does anything at all.* A bowman is doing the only thing he is for, and the man walking toward him is a bowshot away with his shield up, landing no blow because there is nothing within reach to land it on. This is the whole of what auto-resolve never modelled: it threw archers into a melee at contact range and wondered why they were bad. How long the approach lasts is a question about the ground — six rounds across open country, **twelve up a siege ladder** with everyone on the wall shooting at you the whole way, and **two in a village**, where there is barely any ground to cross at all.

*And the opening two rounds belong to the defender.* He is standing on his ground with the whole field to shoot across; the attacker is still coming, too far out to answer, and eats it. That is what it means to advance on a prepared position — and it is why storming one is expensive.

*And then they run out.* A quiver empties per minute, not per swing, so arrows are counted in **rounds**: a man shoots for about fourteen of them and then he is a man with a knife, and his armor was never meant for that. It makes no difference how large the battle is — the skirmish is over before anyone runs dry, and the long battle is exactly where the arrows run out. The one exception is a man on a wall: he is not shooting from his quiver, he is shooting from the town's stores, and those were filled before the siege began.

*The cavalry charge once.* Weight and speed, spent in the first shock and gone by the fourth round — after which a lancer is a man on a horse, hemmed in, with no room to build the speed that made him terrible. And a footman hacking upward at a rider is mostly hacking at the **horse**. Horses die; when one does, its rider loses the barding that was answering those blows, and they start finding his head instead of his legs.

**Training counts.** A master with a blade lands what a recruit only swings — up to three times as much, on RBM's own skill curve. Under vanilla's rules, where proficiency mostly buys handling and speed, it counts for much less.

**What has not changed.** Everything vanilla already did well is left alone. The table that makes cavalry worth a quarter more in the open and archers worth half as much defending a wood still applies. So do the captain's perks, the leader's Tactics, morale and routing, and the surgeon's skill deciding whether a downed man is dead or merely wounded. This changes what a blow *does* — not how a battle is shaped around it.

**You can check it rather than trust it.** With logging on, every battle on the map is written to `logs/simulation/` — replayed twenty times with the model and twenty times without, from the same opening rosters, so you can read what the model actually did to that battle instead of taking anyone's word for it. A battle cannot be fought twice, so it is replayed instead; both replays are simplified in exactly the same way, which is what makes the difference between them honest.

It writes out the baselines it measured, every troop's kit item by item, and then **the battle itself, blow by blow** — round by round, every man who swung, what he was doing at the time (shooting, hurling a javelin, charging, setting a spear against a horse, or just walking into arrows with nothing to answer them), what armor he met, what the shield turned aside, what vanilla alone would have hit for, and what the model made of it. That last part is the only place a battle's *story* can be read: the averages tell you it went one way, but only the blows tell you the archers ran dry in round fifteen and were cut down with knives in their hands.

A handful of figures in all this are judgement rather than measurement, and they are gathered in one place — `AUTO_RESOLVE.md` — so they can be argued with rather than hunted for: how much of a blow a shield turns aside, how much a man achieves while walking into arrows, what a spear set against a horse is worth, how long a charge lasts, how many rounds are in a quiver, and how the blows distribute across a man's body. Everything else is read from the game's own equations and item data, or measured from its own troop roster.

**`AUTO_RESOLVE.md` documents the whole of it** — including the exact difference between the RBM Combat and vanilla paths, and which terms are deliberately left out of the baseline and why.

## What this changes about how you play

Battles now pay for your army's growth directly. A hard-won victory over well-equipped enemies funds a wave of upgrades that would otherwise have drained your treasury. Grinding looters funds nothing, because looters wear nothing worth taking. Sitting still costs you — an idle army eats and drinks its savings. Losing a stack loses its purse with it.

The AI plays by the same rules. Lords' parties accumulate spoils, get discounted upgrades from them, and their troop quality now tracks how well their wars have been going.

And the gear you buy your men now decides battles you never watch. Sending troops in is no longer a gamble on their tier — a well-found company beats a ragged one on the map for the same reasons it beats them on the field, so kitting out your men pays even when you never draw your sword. Equally, an army of high-tier troops you have let go to seed will no longer coast on the number beside their name.

## Tuning it

Everything sits in the in-game RBM config under the campaign section, or in the config XML under `/Config/RBMCampaign`:

| Setting | Default | What it does |
| --- | --- | --- |
| `TroopUpgradeCostMultiplier` | 1 | Scales the gold and spoils price of every upgrade. **0 turns the whole system off.** |
| `TroopUpgradeSpoilsLootMultiplier` | 1 | How much of a battlefield's salvage your men actually carry off. |
| `TroopUpgradeRequireSupplyTown` | 1 | Troops may upgrade only while a friendly or neutral town is within reach, buying the new kit there. **0 lets them upgrade anywhere.** |
| `TroopUpgradeSupplyRadius` | 30 | How near, in map units, that supplying town must be — roughly a short march. |
| `TroopUpgradeChargeMountValue` | 1 | Price a mounted upgrade's horse and harness into its gold cost instead of consuming a horse item from the baggage. **0 restores vanilla's horse-item requirement.** |
| `TroopLootPiecesPerMan` | 3 | Pieces of kit one soldier can carry away from a field. |
| `TroopLootOverlookChancePerTier` | 0.5 | Chance a soldier walks past a piece of gear, per tier it lies beneath him. |
| `TroopFallenSpoilsCaptureFraction` | 0.75 | Share of a beaten enemy's fallen-and-wounded purse the victors capture; the rest is lost in the rout. |
| `TroopWageTierBase` | 20 | A troop's daily wage — this base value times its tier, replacing vanilla's wage table. 0 keeps the vanilla wage. |
| `TroopMaintenanceFraction` | 0.005 | Daily field-upkeep cost as a share of a stack's whole kit worth, paid from its purse and overflowing onto your gold. 0 stops maintenance. |
| `RecruitMaintenanceDays` | 5 | Days of upkeep a freshly recruited stack arrives with already in its purse. 0 seeds nothing. |
| `TroopLeaderSpoilsCutFraction` | 0.05 | Base share of gathered spoils the party leader skims into his own gold, multiplied by his clan tier + 1. **The one place spoils become treasury gold.** 0 leaves the men everything. |
| `TroopRaidSpoilsMultiplier` | 0.25 | Plunder its soldiers pocket as spoils when they sack a settlement — a share of a raided village's wealth, or of a stormed town's prosperity. 0 turns plunder spoils off. |
| `TroopSettlementFoodDays` | 20 | Days of rations a stack buys for itself when it reaches a market. |
| `TroopFoodWageFraction` | 0.5 | Share of a day's wage a man will spend on a day's food before calling it extravagant. |
| `TroopSettlementFunWageFraction` | 1.5 | Drink and dice, as a multiple of the daily wage, per day idled in a settlement. |
| `TroopLuxuryCooldownDays` | 20 | Days a stack waits after an indulgence before it will buy another luxury. |
| `TroopLuxurySpendChance` | 0.02 | Chance per hour idled in a settlement that an over-cap stack buys a luxury off the market. 0 turns luxuries off. |
| `TroopSpoilsHealGoldPerTier` | 10 | Gold a wounded man's stack pays local surgeons, per tier, to mend faster while resting in a settlement. 0 turns paid healing off. |
| `TroopSpoilsHealFractionPerHour` | 0.05 | The most of a stack's wounded that paid healing can mend in a single hour. |
| `SettlementProsperityPerGoldSpent` | 0.02 | Prosperity (or Hearth) a gold moves at a settlement, both ways — trade and carousing there add it, its militia's daily wages and every good it produces drain it. 0 turns all of it off. |
| `MilitiaWageModifier` | 0.2 | Share of their tier-scaled gear wage a settlement's militia actually cost the place, since they're part-time. 0 makes militia free to garrison. |
| `MaintenanceProsperityFraction` | 0.5 | Share of a party's daily maintenance spend that settles as Prosperity into the nearest fortress town or castle. 0 stops it. |
| `TroopSpoilsCapDays` | 20 | Days of keep a stack holds in its purse — this many days of its daily wage and its daily field maintenance together — before it counts itself flush and spends the surplus on drink and luxuries. |
| `SpoilsLoggingEnabled` | 1 | Writes what the system is doing to the log, for debugging. |
| `SpoilsVerboseLoggingEnabled` | 1 | Whether that log carries per-stack detail or only party-level summaries. No effect unless logging is on. |
| `SimulationEquipmentEnabled` | 1 | **Detailed auto resolve.** Decides map battles by the men's actual armor, weapons, shields and skill instead of their tier. **0 restores vanilla's tier-only auto resolve.** |
| `SimulationEquipmentPowerWeight` | 1 | How far kit is allowed to bend a battle. 0 is vanilla; 1 is the model at face value; above 1 widens the gap between a well-found soldier and a ragged one. |
| `SimulationShieldBlockChance` | 0.4 | Share of blows an ordinary shield turns aside; a better shield turns aside proportionally more. 0 makes shields count for nothing. *A judgement figure — the game does not record how often a man gets his shield in the way.* |
| `SimulationLoggingEnabled` | 1 | Writes every map battle to `logs/simulation/`, replayed both with the model and without it, so you can see what it did. |
| `SimulationLogSamples` | 20 | How many times each battle is replayed per side of that comparison. One roll says nothing — a simulated battle is heavily random — so they are averaged. Higher is steadier and slower. |
| `SimulationLogHits` | 1 | Writes one replay of each battle out **blow by blow**: every man who swung, what he was doing, what it met and what it did. Bounded to the opening rounds. Needs the log above. |
