# Campaign

The RBM Campaign module reworks how money, troops and goods move around Calradia. In short:

- **Your soldiers have their own money.** Every troop stack carries a purse of *spoils*, filled from loot,
  plunder and their wages, and spent on their own upgrades, upkeep, food, drink and healing.
- **Wages follow tier.** A fixed table replaces the base game's wages, and a veteran horseman costs many times
  what a recruit does.
- **Upgrades cost what the new gear costs**, and need a town nearby to buy it from.
- **Settlements have real treasuries.** Towns, castles and villages earn, store and spend gold. Garrisons,
  militia, construction and administration are paid out of it, and your income as a fief owner comes from taxes
  on it.
- **Goods are produced and moved.** Villages produce goods and send convoys to their town. Towns eat real food
  out of the market, and prices follow how many days of stock a town holds.
- **Auto-resolve looks at equipment.** Battles resolved on the map are decided by what the men wear and carry,
  not only by their tier.

Everything on this page needs the **RBM Campaign** toggle. Turn it off and the campaign plays like the base
game. Most rules can be tuned or switched off separately; see [Settings](../settings.md).

## Spoils: every troop stack has a purse

Each stack of troops in a party (for example your 40 Imperial Recruits) has its own purse of **spoils**.
One point of spoils is one gold piece, but it belongs to the soldiers, not to you. AI lords' parties use the
same system, so their troop quality follows how well their wars go.

You can see the purse in three places:

- **Party screen.** A steel-blue bar next to each troop's experience bar fills toward the price of upgrading
  the next man. When it is full, the whole stack is covered. Its tooltip shows the stockpile (total and per man),
  the reserve the stack aims for, and what an upgrade costs in spoils.
- **Map tooltip.** Hovering a party on the map shows a "Spoils" line with its total purse.
- **Messages.** After a battle, raid or sack you are told how much your men recovered.

### How the purse fills

| Source | What happens |
|---|---|
| **Battle loot** | When your side wins, your men strip the **dead** of both sides. The wounded and the routed keep their gear. Each piece salvages a random 25 to 75% of its value. The haul is split between the winning parties by their contribution to the battle. |
| **Enemy purses** | The beaten side's spoils, in the share carried by its killed and wounded men, fall to the victors (75% by default; the rest is lost). |
| **Wages** | Each stack banks its **whole daily wage** into its own purse. This costs you nothing extra: it is where the wage you already pay goes. |
| **Raids** | Raiding a village drains its wealth. Half of what is drained becomes spoils for the raiders, the other half is destroyed. A raid that is beaten off pays nothing. |
| **Sieges** | Each day a siege lasts, the besieged castle loses 5% of its treasury, or the town 5% of its citizens' wealth. Half of that becomes spoils for the besiegers. |
| **Sacking** | Taking a town or castle by storm sacks it according to the aftermath you choose (table below). Every besieging party gets a share. |
| **Prisoners left behind** | Prisoners you leave on the post-battle screen are stripped for half the value of their gear. |
| **Ransom** | Selling prisoners strips their full gear value into your stacks' purses, on top of the ransom gold. The ransom menu tooltip and the ransom screen show this in advance. |

**Who picks first.** Inside a party, the highest-tier troops pick first, and each man carries at most three
pieces of gear (setting: *Kit Pieces per Man*). Men tend to walk past gear below their own tier: by default
each tier of difference halves the chance they take it (setting: *Overlook Chance / Tier*). Good gear goes to
the veterans, and cheap gear is left to the recruits who still value it. Pieces nobody can carry are lost.

**Your own dead.** A man who dies takes his share of the stack's purse with him. If a whole stack is wiped
out in a battle you win, half its purse is shared among the survivors.

**Companions** in your party take a share like any troop, but theirs is paid to you as gold.

**Sacking a settlement:**

| Aftermath | Prosperity lost | Wealth taken | Share of it that becomes spoils | Market goods taken | Share of goods value that becomes spoils |
|---|---|---|---|---|---|
| Devastate | 50% | 90% | 10% | 90% | 10% |
| Pillage | 20% | 50% | 50% | 40% | 50% |
| Show mercy | 10% | 10% | 50% | none | none |

Food is taken at half the rate of other goods, and only towns lose market goods. A town loses its citizens'
wealth but keeps its treasury for the new owner; a castle loses its treasury. The base game's flat aftermath
gold is replaced by this.

Raids also carry off less: the goods you take from a raided village are halved, with a little back for the
raid leader's Roguery skill (and for a Nord leader).

### The leader's cut

Each time spoils are gathered (battle loot, raids, siege drain, sacks, stripped prisoners, ransoms), the party
leader takes a share as **gold**. This is the only way spoils become money in your treasury. The share is:

- *Leader's Cut* (5% by default) multiplied by your **clan tier + 1**,
- multiplied by 1.5 while you serve under a mercenary contract,
- raised slightly by your Roguery skill.

For parties led by your companions, the cut goes to you. Wages are never cut.

### How the purse drains

**Upgrades.** Spoils pay for upgrades first, one man at a time. If the purse covers two and a half men,
two upgrade for free, the third is half paid, and the rest come out of your gold. The men who upgrade take
their share of the remaining purse with them.

**Maintenance.** Every day in the field, each stack pays to keep its gear in order: a small share of the
value of everything it carries (0.5% per day by default, horses included), so heavy cavalry cost much more to
keep than spearmen. The purse pays first; any shortfall comes from your gold. How much the purse may cover
depends on whose war it is:

- **Independent clans and mercenaries under contract:** the men pay their own maintenance in full (setting:
  *Self-Funded Maintenance Share*).
- **Sworn vassals and rulers:** the purse pays nothing, and the leader pays it all.

The gold you pay goes to the town you are in, or the nearest one not at war with you. Your finance screen shows
"Troop maintenance" and "Maintenance paid from troop spoils" lines, and each troop shows its maintenance per
man under its wage. Freshly recruited stacks arrive with 20 days of maintenance already in their purse
(setting: *Recruit Maintenance Days*).

**Food.** When your party stops in a settlement, stacks whose rations have run out buy their own food off the
local market with their spoils: enough for 20 days by default (setting: *Food Days Bought*). Better-paid troops
buy dearer food. If the market has nothing affordable, they buy whatever there is, and if it is empty they leave
hungry. While a stack has its own rations it does not eat from your party's food. The food tooltip shows this
as "Provisioned from their own purse", and the days-of-food estimate takes it into account.

**Carousing.** Every hour a party spends in a settlement, its men spend on drink and dice: by default a
quarter of their daily wage per day (setting: *Spoils Spent on Fun*). This applies to garrisons and militia too.
Stacks above their reserve spend harder. The money goes to the settlement. A purse never goes below zero.

**Luxuries.** A stack that holds more than its reserve occasionally buys a luxury item (jewelry, velvet, furs
and the like) off the market for itself, with a cooldown between purchases. The item is theirs, not party loot.

**Healing.** A stack resting in a settlement pays local surgeons to heal its wounded faster: 10 gold per tier
per man by default, up to 5% of the stack's wounded per hour. The base game's free healing still applies, so
a poor stack still recovers, only slower. No paid healing while the party or fief is starving.

### The reserve (cap)

A stack aims to hold 20 days of its wage plus maintenance (setting: *Spoils Reserve (Days of Keep)*). This is
not a hard limit, but anything above it is spent faster on drink and luxuries. It never comes back to you as
gold.

### Moving troops

When you move troops on the party screen (to a garrison, another party, or back), each group carries its share
of the purse. Donating troops to a garrison and creating a companion's party do the same. Men who leave in
other ways (dismissal, for example) lose their purse.

## Wages

Troop wages follow a fixed table by tier. There is no setting for it; it applies whenever RBM Campaign is on.

| Tier | 1 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|---|
| Foot | 20 | 30 | 40 | 60 | 120 | 240 |
| Mounted | 30 | 40 | 60 | 120 | 240 | 480 |

Tier 0 troops are paid as tier 1. Mounted means cavalry and horse archer troop types. Heroes keep the base game's
pay. Because all of this wage goes into the stack's purse, a well-paid stack also saves faster toward its
upgrades.

AI lords' wage limits are raised to match, so their parties stay about as large as in the base game.

## Troop upgrades

**Price.** Upgrading a man costs the difference in value between his new gear and his old gear, averaged over
all the equipment sets the troop can spawn with, horse and harness included. Promoting a recruit to a footman
is cheap; promoting a veteran into heavy armor and a warhorse is not. The base game's discount perks (Sound
Reserves, Renowned Archer, Contractors, the Khuzait cavalry bonus) still apply. A mounted upgrade does not need
a horse from your inventory: the horse is part of the price (setting: *Buy Mounts For Upgrades*). If the new
gear is worth less than the old, the upgrade is free and the difference goes into the purse.

**Supply town.** New gear has to come from somewhere. By default (setting: *Upgrade Near Town*), you can only
upgrade while inside a friendly or neutral settlement, or within 30 map units of a friendly or neutral town
(setting: *Upgrade Supply Range*). Otherwise the upgrade arrows are greyed out with the message "No friendly
town nearby to supply this upgrade." Gear paid for with gold is taken from that town's market where it has
something suitable, but an empty market never blocks an upgrade. The gold you pay goes to the town.

**Party screen.** The upgrade tooltip shows "Spoils cover", "You pay", the total for the whole stack, and
whether a mount is included. The arrows only offer as many upgrades as you can pay for.

**AI parties** follow the same rules, but only the gold-paid part needs a nearby town. You can set a daily
**upgrade budget** for each of your clan's parties in the Clan > Parties panel, next to the wage limit.

Use the *Troop Upgrade Cost* setting to scale all upgrade prices. At **0** the whole spoils system is turned
off.

## Settlements and fiefs

### Two purses

A town has two separate pools of money:

- **Citizen wealth** is the town's market money: what merchants pay you and what the trade screen shows.
- **Treasury** is the town as an institution. It pays the garrison, militia, administration, walls and
  construction.

Castles and villages have a single pool. Hovering a settlement on the map shows its purses.

### Your income as an owner

The base game's cut of town trade is gone. Instead:

- **Market fee.** 1% of every trade in a town moves from the citizens to the town treasury. A Guard House
  raises the fee on caravans, lords and the player; a Marketplace raises it too.
- **Wealth tax.** Each day a small share of the town's citizen wealth goes to you and to the treasury. If the
  citizens hoard more than 1000 times the town's prosperity, 10% of the excess is taken each day by you and
  by the treasury. A Tax Office raises both.
- **Castle surplus.** A castle earns income into its pool each day. You receive 10% of whatever it holds
  above 200 times its prosperity.
- **Minting.** Towns strike silver ore from their market into coin; the ruler, the owner and the treasury
  each take a cut.
- **Prison labour.** Each prisoner in a town or castle earns its treasury 30 gold a day and helps with
  construction.
- **Villages** keep half of the tax from their convoy's sales; the rest goes to the owner.

These appear in your finance screen ("Settlement wealth tax", and so on). In the Fiefs tab, the Tariffs row is
replaced by "Wealth tax" or "Castle surplus".

When a lord sells prisoners in a town, the ransom is paid out of that town's citizen wealth. At a castle it
is paid out of the castle's own wealth, so a poor castle pays less. Any other gold the game pays to a castle
also goes into its wealth.

### Garrisons

- The fief treasury pays its garrison's wages. You only pay what the treasury cannot, and only if the fief's
  garrison wage limit is unlimited. After that, a rich town's citizens chip in.
- Garrisons also cost a reduced gear maintenance, paid by the treasury.
- There is no flat garrison size cap any more: money and food decide how large a garrison grows.
- Garrisons recruit on their own only while the treasury holds at least 15 days of the fief's whole bill, a few
  men per day at most (more with Barracks), and each recruit's gear is paid by the treasury. If the treasury
  drops below 7 days of the garrison's own bill and nobody subsidises it, the garrison loses 2 men a day.
- Garrisons no longer take the notables' volunteers, so those stay available for you.
- Garrison morale is fixed, so garrisons do not desert.
- Garrisons drill: they gain experience each day, more when the treasury is well stocked, plus a flat bonus from
  Training Fields. Garrison promotions are paid by the treasury.

### Militia

- Militia have a **soft cap** of 40% of the settlement's base (prosperity for towns, hearths for villages,
  and for castles the average hearths of their villages). Barracks, a castle's Guard House, Training Fields,
  the Train Militia and Raise Troops projects (while they run) and the Citizenship, Cantons and Bolster the
  Fyrd (War Sails) policies raise it, up to 70%. Serfdom lowers it. The castle Guard House and these four
  policies no longer add or remove militia each day; they only move the soft cap.
- Above the soft cap, growth slows more and more the closer the militia get to the **hard cap** of 75%, and
  it can never pass it. Militia standing above the hard cap (after a siege, for example) disband at 5% of the
  excess a day, and their kit is refunded. The militia tooltip shows both caps.
- A town arms new militia straight from its own market's shelves, and disbanded militiamen put their kit
  back on them (with *Recruits Kitted From Market* on; otherwise the kit is paid out of citizen wealth).
- Militia cost money: a town's militia is paid a quarter of a soldier's wage, a castle's a tenth, a village's
  nothing, plus gear maintenance. If the settlement cannot hold 20 days of the militia's pay, they shrink by one
  man a day.
- When a settlement is besieged or raided, the notables' waiting volunteers join the defence.

### Other costs and starvation

- Administration costs each settlement a daily salary, and walls cost upkeep per wall level.
- A fief with under 7 days of food stops growing. Under 3 days it loses prosperity. A starving fief loses
  prosperity faster, and its garrison and militia start to sicken and die.

### Construction

- Building projects cost real money: project costs are multiplied (setting: *Building Cost Multiplier*), and
  one construction point costs one denar.
- Each day the treasury moves 1% of itself into the construction reserve (setting: *Construction Budget
  Share*). You can also top the reserve up yourself.
- How much work can be done per day depends on prosperity, prisoners, a Guard House and Mason level. Prisoners
  work for free.
- Construction buys clay and planks from the market and wears out tools.
- A fief with no project queued slowly improves its least-built building.

## Villages, towns and food

**Village production.** Villages produce goods every day in proportion to their hearths: a common set (grain,
charcoal, crude iron, hides, wool, planks, flax and more), their own specialities, and a few goods typical of
their culture. Horse-breeding villages also raise horses. Output is scaled by the *Village Production* setting.

**Convoys.** Each village sends one convoy to its town once its warehouse is half full. Convoys carry far more
than in the base game and are escorted by village militia (up to 12 guards, depending on cargo value). They
sell to the town at a fixed wholesale price, food first. A wealthy village spends part of its savings on goods
in its market town.

**Town food.** A town's food is the food actually in its market. Townsfolk eat from it daily in a mix of
grain, beer, meat, cheese, butter, fish, wine, dates and oil. A town's granary holds 30 to 60 days of its own
consumption, depending on its Granary level. When stock falls very low, the treasury buys food. A town short of
food stops growing; a starving one declines.

**Town prosperity** is on a different scale from the base game: it follows the size of the villages that feed
the town, so towns typically sit in the hundreds, not thousands. Towns grow only while fed, faster when their
citizens can afford luxuries, and buildings raise how high they can grow.

**Citizen demand.** Townsfolk also buy staples such as charcoal and salt, and once they have savings, better
and luxury goods.

## Markets and prices

- **Trade goods are repriced** to historical values with realistic weights. Goods range from cheap bulk like
  grain to very expensive luxuries like velvet. The *Inventory Weight Column* setting adds a weight column to
  the inventory and trade screens, to help judge what is worth carrying (takes effect after a restart).
- **Prices follow days of stock.** A town sells a good at its base value while it holds at least 15 days of its
  own consumption. Below that, the price climbs: up to 2 times for basic goods, 4 times for medium goods and
  8 times for luxuries when the town is nearly out. Prices never drop below base value.
- Village convoys get a flat wholesale price; caravans, lords and you get the full market price when
  selling to a town. A village's trade screen uses its town's prices.
- War gear, horses and other goods without a modelled demand keep the base game's prices.
- Castles keep the base game's pricing.
- **Hidden stock.** On your trade screen you only see half of each stack a market holds.
- **Food reserve.** Half of a town's market food cannot be bought by AI parties, armies and caravans. You are
  exempt.
- **Storage caps.** A town stops accepting a good from traders once it holds 60 days of it. Your own sales are
  not capped.
- **Trade skill.** Very large single trades give less Trade experience, and workshop production gives at most
  20 Trade experience a day.
- New campaigns start with more gold (setting: *Starting Gold Multiplier*, 5 times by default).

## Caravans

- Forming a caravan costs about ten times the base game's price, and it carries ten times the seed capital.
  Your caravan pays you a tenth of its purse above its seed capital each day.
- **Kingdom supply caravans** (setting: *Kingdom Supply Caravans*) carry goods from towns with a surplus to
  towns running short, inside a kingdom and to kingdoms with a trade agreement. Besides the goods townsfolk
  buy, they carry the workshop materials leather, linen, wool, flax and iron ingots. They can be raided. They
  cost their owner nothing and pay nothing.
- **Caravan investment** (setting: *Caravan Investment*) lets rich towns lend to struggling ones along these
  routes.

## Workshops

- A workshop needs 60,000 gold of capital to found, and costs 250 a day in upkeep.
- Workers are paid a share of each sale, smaller for workshops with costly equipment (roughly half for a
  pottery, less for a smithy). Your clan screen shows this as "Production Wages". No wages are paid while the
  workshop's capital is low.
- A workshop only produces when the batch is profitable and the town can pay for it. It skips a cycle if the
  town already holds all it can store of every output.
- Workshops that make war gear (weapons, shields, armor and ammunition) mostly make the gear of the town
  culture's militia, troop trees and mercenaries, and never make another culture's gear. They stop making an
  item once the market holds 6 of it, so a few items no longer flood every market.
- Towns also have small hidden artisans producing alongside the workshops.
- **Leather.** The artisans tan hides into leather before any of their other recipes that use hides or
  leather, and tanneries spend more of their work on tanning, so towns no longer run out of leather (and with
  it armor).
- The player's smithy gets a steel refining chain using crude iron, charcoal and silver.

## Recruitment

- **Manpower.** Each settlement has a pool of men that grows daily with its prosperity (towns, castles) or
  hearths (villages). Every new volunteer, and every garrison recruit, uses it up. Barracks, Castellan's Office
  and some projects raise it.
- **Recruits from your own fiefs are free.** If you are a ruler, recruits anywhere in your realm are free.
  Elsewhere, a recruit costs his gear plus a few days' wage, a little more for mercenaries and foreign lords.
  Recruits are equipped from the town's market (setting: *Recruits Kitted From Market*).
- **More slots in your own fiefs.** Members of the clan that owns a settlement may take all of its notables'
  volunteers.
- **Taverns** always offer real mercenaries, never caravan guards.

## Mercenary contracts

The base game's influence-based mercenary pay is replaced. The ruler you serve pays a daily stipend that grows
with your influence and renown, and reimburses your doubled field-troop wages, as far as the ruler can afford.
Your finance screen shows the lines "Mercenary stipend", "Mercenary wage pay" and "Mercenary troop wages".

## How AI lords behave

- **Recruiting.** Understrength lords head for their own fiefs, where recruiting is free, and refill from
  their own garrisons when those hold surplus troops.
- **Equipment** (setting: *AI Lords Buy Equipment*). In towns, AI lords upgrade their own gear with better items
  of their culture.
- **Pack animals.** AI lords buy pack animals and spare riding horses to travel faster.
- **Sieges.** AI lords need more strength than in the base game before they start a siege.
- **Respawn.** Released or respawning AI lords appear at one of their own clan's fiefs.
- **Deserters** (setting: *Deserter Raiders*). Deserter bands hunt villager convoys and caravans and raid weak
  villages, instead of only wandering.

## Detailed auto-resolve

With **Detailed Auto Resolve** on (the default), battles resolved on the map are decided by the men's actual
equipment and skill instead of their tier:

- **Armor and weapons matter.** Every blow is run through the armor rules of the combat module you use (RBM
  Combat or the base game's). Maces and spears do better against heavy armor, as they do on the field.
- **Every soldier has health.** Wounds add up over the battle instead of each blow being a single kill roll, so
  results are less random and the better-equipped army wins more reliably.
- **Battles have phases.** First a volley in which only archers shoot, then a skirmish with javelins and
  cavalry against cavalry, then melee. The defender gets the first shots. Archers can miss and run out of
  arrows, except defenders on walls.
- **Defense.** Men block, parry and counter-attack based on their skill and shield. Spears braced against
  cavalry hit harder. Cavalry charge again and again on open ground, less in forests and villages, and not in
  sieges.
- **Captains and perks** (setting: *Auto Resolve Perks*). Each formation gets a captain (your Order of Battle is
  respected), and their real perks apply.
- **Sieges.** Assaults go through an approach under fire and then fighting at the breaches, ladders and towers.
  Siege engines fire during the battle.
- **You fight too.** When you send your troops, your character takes part and can be wounded, as AI lords
  always could.
- **Captured wounded.** When an AI party is beaten, its wounded troops are taken prisoner by the winners.
- **Morale.** Morale no longer changes how hard blows land, and the base game's morale rout is turned off.
  With **Auto Resolve Routing** on (off by default), a side under 50 men that is clearly losing may break and
  flee instead of fighting to the last man. Siege assaults never rout.

Each simulated round takes less campaign time than in the base game. While you watch your own battle being
resolved, a panel shows the current phase, each side's remaining infantry, ranged and cavalry, and a battle
chronicle.

**Troop power.** With **Equipment Based Troop Power** on, the strength shown on the encounter screen (and used
by the AI to decide whether to fight or flee) is also based on equipment. Hover the power bar to see each troop
type's strength.

Turning Detailed Auto Resolve off restores the base game's auto-resolve completely.

## Watching AI battles

With **Spectate AI Battles** on (off by default) and the RTSCamera mod installed, the game offers to let you
watch battles between two AI sides that each have at least 100 men (setting: *Spectate Minimum Troops Per
Side*). You watch from a free camera, with no character on the field. What you watch is a copy: the real map
battle is still auto-resolved, and nothing from the watched battle is kept.

## RBM Ledger

Press **Ctrl+Shift+K** on the campaign map, or choose **RBM Ledger** in the Escape menu, to open the ledger.
It tracks the economy over the last 30 days:

- **Villages** tab: every village grouped by its trade town, with production, wealth, hearths and militia, and
  events such as raids and convoys sent and delivered.
- **Towns** tab: every town grouped by kingdom, with prosperity, citizen wealth, treasury and market food;
  where income and expenses come from; food eaten; demand and stock for each good; workshop output; and events
  such as sieges and changes of owner.

## Other interface changes

- **Building tooltips** describe what each building does under RBM.
- The **Town Management** project grid shows more buildings per row.
- The **refining** screen has room for recipes with three inputs.
- Settlement nameplates on the map briefly show when parties carouse there, and when your party buys food or
  luxuries.

## Log files

Several settings in the RBM Debug & Logging section write log files to
`Documents\Mount and Blade II Bannerlord\Configs\RBM\logs\` (the same folder as RBM's `config.xml`):
`simulation\` for auto-resolved battles, `campaign\` for spoils, `economy\` for production and food, and
`caravans\` and `powerCalculation\` for those systems. All are off by default and only needed for checking or
reporting problems.
