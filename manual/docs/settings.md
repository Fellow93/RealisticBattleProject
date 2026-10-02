# Settings Reference

RBM is configured from its own settings screen. Open it from Bannerlord's **main menu** with the
**RBM Configuration** entry, near the bottom of the menu list. The screen can only be reached from the
main menu, not from inside a running campaign or battle.

How the screen works:

- Settings are grouped into five collapsible sections: **RBM Combat**, **RBM AI**, **RBM Campaign**,
  **RBM Tournament** and **RBM Debug & Logging**. Every section starts collapsed; click its title to
  open it. The RBM Campaign section is split further into sub-groups, which start open, and RBM Combat
  holds a collapsed **Advanced Armor** sub-group.
- Hover over a setting's name to see a tooltip explaining it.
- Rows drawn indented on a darker band belong to the setting directly above them. In the tables below
  they are marked with **↳**. A few of them (the Stamina System and the Frontline sliders) are greyed out
  while their parent is switched off.
- **Done** saves your changes, **Cancel** throws them away, and **Reset to Default** puts the controls
  back to the shipped defaults (nothing is saved until you press Done).

Settings are saved to:

```
Documents\Mount and Blade II Bannerlord\Configs\RBM\config.xml
```

The file also holds a few advanced values that have no control on the screen. If you edit it by hand,
do so while the game is closed, because the game rewrites the file whenever it loads or saves settings.
When an RBM update changes the format of this file, your settings are reset to the defaults.

!!! note "When changes take effect"
    Changes take effect the next time you start or load a game. Some settings say otherwise in their
    description (for example, the inventory weight column needs a game restart, and the starting gold
    multiplier only affects a new campaign). Switching **RBM Combat** on or off also needs a full game
    restart: RBM's spear and weapon animation parameters are only read when Bannerlord starts, and the
    screen shows a "Restart Required" message when you change it.

---

## RBM Combat

The combat overhaul: damage, armor, weapon and missile physics, ranged reloading, and RBM's reworked
items, troops and siege engines.

!!! warning "Module Status switches the whole section"
    Disabling **Module Status (RBM Combat)** returns combat to the base game, and every other option in
    this section stops working. It also needs a game restart (see above).

| Setting | Default | What it does |
|---|---|---|
| Module Status (RBM Combat) | Enabled | Master switch for RBM's combat overhaul: damage, armor, weapon and missile physics, ranged reload, and RBM's reworked items, troops and siege engines. Disabled returns combat to the base game. |
| Armor Effectiveness | 1.00 | Armor counts as this many times its value. Higher means armored troops take less damage from every weapon; lower means armor protects less. Unarmored troops are unaffected. Also affects auto-resolve and troop power. The Advanced Armor settings below are applied on top of the armor value this one produces. Range 0.25–3, in steps of 0.05. |
| **Advanced Armor** (sub-group) | | Collapsed by default. Fine control over how armor turns a hit into damage. |
| ↳ Armor Multiplier | 2.00 | How strongly armor absorbs blunt trauma (the damage that gets through armor without penetrating it). That part of a hit is scaled by 100 / (100 + armor × this value). Higher makes armored troops tankier; lower makes them die faster. Penetrating damage is unchanged. Multiplies with Armor Effectiveness: Armor Effectiveness 1.5 with this at 2 makes armor count 3× in this curve. Also affects auto-resolve and troop power. Range 0.5–4. |
| ↳ Blunt Trauma Multiplier | 1.00 | Scales blunt trauma for every weapon type. Lower makes armored troops tankier; higher makes armor protect less. Penetrating damage is unchanged. It does not make armor count for more; it only changes how much a stopped hit still hurts. Also affects auto-resolve. Range 0–3. |
| ↳ Armor Penetration Threshold | 1.00 | Scales how much of a blow armor stops outright: the part of a hit above armor × weapon factor × this value penetrates, and the rest becomes blunt trauma. Higher makes armored troops tankier, since more hits fail to penetrate; lower lets more damage straight through. Shields use the same rule. Multiplies with Armor Effectiveness (1.5 there with 1.2 here makes armor stop 1.8× as much as at the defaults). Also affects auto-resolve. Range 0–3. |
| Troop Overhaul | Active (Recommended) | Replaces the troop trees of the main cultures, plus mercenaries, minor-faction troops, militia and tournament fighters, with RBM's versions (new equipment, skills and upgrade paths). **Inactive** keeps the base game's troops, still using RBM's item changes. |
| ↳ Passive Shoulder Shields | Disabled | Lets RBM troops keep the shoulder-strapped versions of their shields, which stay on the shoulder while they fight with another weapon. Disabled swaps them for the normal hand-held versions. Needs Troop Overhaul active. |
| Ranged reload speed | Semi-realistic | How fast bows and crossbows are reloaded, scaling with skill. **Vanilla**: the base game's speeds. **Realistic**: slow, above all for unskilled shooters. **Semi-realistic**: reloads much faster than Realistic at low skill. Both Realistic and Semi-realistic also slow the bow draw for everyone. The reload part applies to the player only, unless the option below is enabled. |
| ↳ Ranged reload applies to AI | Disabled | When enabled, AI archers and crossbowmen follow the Ranged reload speed setting too, instead of their fixed AI reload. Disabled keeps that setting player-only. |
| Better Arrow Visuals | Enabled | Arrows and bolts in flight are drawn with their real model instead of the base game's thin streak. True to size they are harder to follow. Visual only; hits are unchanged. |
| ↳ Flying arrow thickness (experimental) | 1.00 | Experimental. Makes the in-flight arrows and bolts of Better Arrow Visuals thicker so they are easier to follow. Only thickness is scaled, and only in flight; arrows in the quiver, on the string and stuck in a target stay true to size. Visual only. Does nothing when Better Arrow Visuals is disabled; at 1.00 it is off. Takes effect the next time a game is started or loaded. Range 1–10, in steps of 0.25. |
| Stuck javelins fall out after (s) | 5 | Javelins, throwing axes and throwing knives stuck in a shield or in a living body work loose after roughly this many seconds and drop to the ground, where they can be picked up again. Each one gets its own time, up to 40% shorter or longer. 0 (shown as Disabled) leaves them stuck, as in the base game. Range 0–60. |
| Arrows in shields fall out after (s) | 45 | Arrows and bolts stuck in a shield work loose after roughly this many seconds (each up to 40% shorter or longer) and drop to the ground, where they can be picked up again. A shield holds only 8 at once: one more and the oldest drops right away. Arrows in a body have no timer, but a body keeps only 4: one more and the oldest drops. 0 (shown as Disabled) leaves them stuck with no limit, as in the base game. Range 0–300, in steps of 5. |
| Sneak Attack Insta-Kill | Disabled | A blow that counts as a sneak attack (a melee weapon or throwing knife striking an unaware person, or one not yet fully alert from behind, without hitting a shield) deals a flat 200 damage, ignoring armor. Mostly matters in stealth missions, and you can never be the victim. Disabled applies the base game's sneak attack bonus before armor instead. |
| Armor Status GUI | Enabled | Shows six small icons in the lower right of the battle screen with the condition of your own armor (head, shoulders, body, hands, legs and horse harness), shading from green (better than standard) through grey to red as blows wear it down during the battle. |
| Realistic Arrow Arc | Disabled | Your bow and crossbow shots leave about 5 degrees above where you aim, so they fly a higher arc and the crosshair no longer marks where they land. Player only; slings and thrown weapons are unaffected. |
| Ranged aim arc (player, experimental) | Disabled | Experimental. While you draw a bow or crossbow or wind up a sling, shows the predicted flight of the missile as a dotted arc with a marker where it will land, using the real shot's launch speed and air drag. Also covers javelins, throwing axes, throwing knives and stones. In third person, when you aim up the camera lifts and tilts down so the landing point of a high shot stays on screen; your aim is unchanged, and the crosshair is hidden while the camera is moved. Player only. |
| Thrust weapon preference for AI (default at 0.05) | 0.05 | Dropdown from 0.01 to 1.00 in steps of 0.05. Scales the thrust damage figures weapons carry, which the AI weighs when choosing between thrusting and swinging. RBM undoes the scale when it works out piercing damage, so stabs hit about as hard at any setting; lower values make the AI favour swings. |

---

## RBM AI

RBM's battle AI: formation tactics and behaviors, siege AI, AI blocking and parrying, the posture and
stamina systems, the frontline system and AI kicks and bashes.

!!! warning "Module Status switches the whole section"
    Disabling **Module Status (RBM AI)** returns battle AI to the base game, and every other option in
    this section stops working, including Slow Motion in Combat.

| Setting | Default | What it does |
|---|---|---|
| Module Status (RBM AI) | Enabled | Master switch for RBM's battle AI. Disabled returns battle AI to the base game. |
| Slow Motion in Combat | Enabled | Briefly slows the battle to a quarter of its speed, for three quarters of a second, when you kill or knock out an enemy or break his posture. The posture-break slowdown needs the Posture System. |
| Frontline System | Enabled | Per-soldier jostling inside a charging infantry or archer line: every couple of seconds each man decides whether to press in, step back, close on a neighbour, slide around a flank or hold his rank. Disabled leaves melee lines on RBM's plain charge. Cavalry and archer free-charge rules and unit facing are unaffected either way. The six sliders below are greyed out while this is disabled. |
| ↳ Frontline Sidestep Weight | 1.00 | Multiplier on the left and right "slide sideways past the man in front" choices. Above 1 makes lines spread wide around a stalled front rank. Range 0–3. |
| ↳ Frontline Close Ranks Weight | 1.00 | Multiplier on the "close the gap to my nearest neighbour" choice. Above 1 makes lines huddle tighter and shield walls hold better; below 1 lets them spread. Range 0–3. |
| ↳ Frontline Back Step Weight | 1.00 | Multiplier on the "give ground and let a fresh man through" choice. Above 1 gives more rotation out of the front rank. Range 0–3. |
| ↳ Frontline Attack Weight | 1.00 | Multiplier on the "press forward at my target" choice. Above 1 makes lines more aggressive and thinner; below 1 makes them hang back. Range 0–3. |
| ↳ Frontline Decision Hold | 2.00 | Once a man picks a move he sticks with it for a random 0 to this many seconds before reconsidering. Higher is steadier and cheaper to run; lower makes the line twitchier. Range 0–10. |
| ↳ Frontline Min Formation Size | 25 | Formations with fewer men than this skip the frontline system and charge normally. Range 0–200. |
| Posture System | Enabled | Blocks, parries and hits wear down a posture meter that recovers over time. When it runs out the soldier can be staggered, drop his weapon or shield, be knocked off his horse, or take damage that crushes through his guard. Disabling it also switches off the Stamina System and the Posture GUI. |
| ↳ Stamina System (requires Posture) | Enabled | Attacking, blocking and being hit drain stamina, faster in heavy armor and slower with high Athletics. A tired soldier hits weaker, moves, swings and reloads slower, blocks worse and loses posture faster; above 85% stamina he slowly regains health. Greyed out, and switched off automatically, when the Posture System is disabled. |
| ↳ Player Posture Multiplier | 1x | Options **1x**, **1.5x**, **2x**. Multiplies your own character's maximum posture and posture recovery, and with the Stamina System on also your maximum stamina and its recovery. AI soldiers are unaffected. Needs the Posture System. |
| ↳ Posture GUI | Enabled | Shows your own posture bar in battle (and stamina bar, with the Stamina System on), and for a few seconds after you trade blows with an enemy, his name, health, posture and stamina. Needs the Posture System. |
| AI Kick and Bash | Enabled | AI soldiers kick, shield bash and weapon bash. Kicks and bashes deal real damage, cost posture and stamina, and can knock an enemy down. Applies to the player's kicks and bashes too. |
| Keep Battle (Last Stand) | Disabled | In sieges RBM keeps defenders from losing morale as their comrades fall, so a garrison holds the walls to the last man. With this enabled, once fewer than 50 defenders remain and they have less than half the attackers' strength, losses shake them twice as hard, so the survivors can break and fall back for a last stand in the keep. |
| Vanilla AI Block/Parry/Attack | Disabled | Leaves AI soldiers' blocking, parrying and melee attack decisions on the base game's values instead of RBM's skill-based ones. It also lets the Combat AI difficulty option weaken RBM's AI aim as strongly as it does in the base game. |

---

## RBM Campaign

The campaign overhaul: the troop spoils economy, wages and upkeep, settlement wealth, village and town
production, caravans, equipment-aware auto-resolve and troop power, and RBM's additions to the campaign
screens.

Several settings below refer to **spoils**: each troop stack in a party has its own purse, filled from
battle loot, plunder and its wages, and spent on upgrades, upkeep, food, carousing, healing and
luxuries. One point of spoils is worth one gold.

!!! warning "Module Status switches the whole section"
    Disabling **Module Status (RBM Campaign)** returns the campaign to the base game, and every other
    option in this section stops working. RBM's additions to the campaign screens only follow a change
    after a game restart.

### Module & Simulation

| Setting | Default | What it does |
|---|---|---|
| Module Status (RBM Campaign) | Enabled | Master switch for RBM's campaign overhaul. Disabled returns the campaign to the base game. |
| Workshop Production | 1.00 | Multiplier on every workshop's conversion speed (how fast it turns inputs into outputs), on top of RBM's prosperity-driven scale. Below 1 slows shops, above 1 speeds them. Range 0.01–2. |
| Village Production | 0.50 | Multiplier on every village's daily output, together with its warehouse size and its production tooltip. Below 1 throttles the countryside, above 1 floods it. Range 0.01–2. |
| Starting Gold Multiplier | 5.0x | Multiplies the gold character creation hands out at the start of a new campaign. Only applies to a new game; a loaded save keeps its own gold. Range 1–50, in steps of 0.5. |
| Deserter Raiders | Enabled | Gives deserter parties initiative: they actively hunt nearby villager convoys and caravans and raid weakly held villages when they out-match the target, instead of aimlessly patrolling their spawn point. Disabled leaves deserters on vanilla behavior. |
| Kingdom Supply Caravans | Enabled | Spawns caravans that carry a surplus good from one of a kingdom's towns to another town of the same kingdom that is short of it. Real goods and money move, and the caravan can be raided. Disabled leaves the map on vanilla caravans alone. |
| ↳ Caravan Investment | Enabled | On a route from a wealthy town to a struggling one, the caravan also injects capital into the struggling town so it can afford the goods, booked as a debt the town repays once it recovers. Needs Kingdom Supply Caravans. |
| AI Lords Buy Equipment | Enabled | AI lords spend gold in towns to upgrade their own battle gear to items of their culture. Disable it if another mod manages lord equipment. |
| Inventory Weight Column | Enabled | Adds a weight column to the item rows of the inventory and trade screens, showing what one item weighs. Takes effect after restarting the game. |
| Equipment Based Troop Power | Enabled | Party strength (what the encounter screen shows, and what AI lords weigh before attacking, fleeing or gathering armies) is worked out from each troop's gear, training and horse and his commander's perks, instead of his tier alone. Auto-resolve itself is not affected, so the shown strength no longer predicts it exactly. |
| Detailed Auto Resolve | Enabled | Auto-resolve works out every simulated blow from the troops' actual gear (armor on each body part, shields and real missiles) instead of their tier alone. Disabled returns auto-resolve to the base game, and switches off the two options below with it. |
| ↳ Auto Resolve Routing | Disabled | In auto-resolve, a side cut down to fewer than 50 men and losing clearly worse than its enemy may break and flee instead of fighting to the last man; the survivors escape. The more one-sided the fight, the likelier the break. Siege assaults are not affected. Needs Detailed Auto Resolve. |
| ↳ Auto Resolve Perks | Enabled | In auto-resolve, troops are split into formations under captains as in a real battle, and each captain's own combat perks apply to his formation; the commander's hit-point perks also reach his men. Replaces the base game's flat bonus for the number of captain perks. Needs Detailed Auto Resolve. |
| Spectate AI Battles | Disabled | When two AI sides meet in a field battle or siege assault with enough men on each side, offers to let you watch it from a free camera. What you watch is only a copy: the battle on the map resolves on its own. Needs the RTS Camera mod. |
| ↳ Spectate Minimum Troops Per Side | 100 | How many men both sides must field before a battle between two AI lords is offered for spectating. Range 10–1000. |

### Troop Upgrades

| Setting | Default | What it does |
|---|---|---|
| Troop Upgrade Cost | 1.00 | Multiplier on the gold-and-spoils cost to upgrade a troop. **0 turns the spoils system off and makes upgrades free.** Range 0–2. |
| Battle Spoils Loot | 1.00 | Multiplier on the worth of the kit your men salvage from a battlefield. Range 0–5. |
| Buy Mounts For Upgrades | Enabled | Upgrading into a mounted troop no longer needs a horse in the baggage train and consumes none; the horse and harness are paid for in gold or spoils instead. Disabled restores the vanilla horse requirement. |
| Recruits Kitted From Market | Enabled | A volunteer who appears in a town is kitted out from that town's market (one in a village, from the town it trades with). It takes what the market has and never blocks a recruitment. It also sets the recruit price: free in your own fief (or anywhere in your realm as its ruler); the man's gear plus five days' wage for a vassal in his own realm; a tenth more for outsiders and mercenaries; and only the wage part plus a tenth for a landless adventurer. The money goes to the settlement. Disabled restores the base game's recruit price. |
| Upgrade Near Town | Enabled | A party can only upgrade troops while it is inside a friendly or neutral settlement, or within Upgrade Supply Range of a friendly or neutral town, and the new kit is taken from that town's market. AI parties are only held back on upgrades they pay for in gold; bandits are exempt. Disabled lets parties upgrade anywhere. Needs Troop Upgrade Cost above 0. |
| ↳ Upgrade Supply Range | 30 | How near, in map units, a friendly or neutral town must be for a party to upgrade its troops. Needs Upgrade Near Town. Range 0–200. |

### Battle & Raid Spoils

Raid and siege plunder has no setting of its own: half of the wealth a raid or a siege drains becomes
spoils for the men, and the rest is destroyed.

| Setting | Default | What it does |
|---|---|---|
| Kit Pieces per Man | 3 | How many pieces of kit a single soldier can carry off a battlefield. Range 1–10. |
| Overlook Chance / Tier | 0.50 | Chance a man passes over a piece of kit for each tier it sits beneath his own, compounded: at 0.50 a veteran takes half of what is one tier below him and a quarter of what is two tiers below, leaving the rest for greener troops. Kit of his own tier or better is never overlooked. Range 0–1. |
| Fallen Spoils Captured | 0.75 | Share of a beaten enemy's killed and wounded men's spoils the victors carry off; the rest is lost. Range 0–1. |
| Leader's Cut | 0.05 | Base share of the spoils a party's men gather (from a battlefield, a raid or a sack) that their leader takes into his own purse as gold before the rest goes to the troop stacks. Multiplied by the leader's clan tier plus one, so a tier-0 or clanless leader takes this share once and a tier-6 clan seven times it. 0 leaves the men everything they take. Range 0–1. |

### Wages & Maintenance

Troop wages themselves are a fixed RBM pay table and have no setting here. To get the vanilla wages
back, disable the RBM Campaign module.

| Setting | Default | What it does |
|---|---|---|
| Self-Funded Maintenance Share | 1.00 | Share of daily maintenance a self-funded clan (one sworn to no kingdom, or a mercenary company under contract, whose pay is doubled while the contract holds) meets from its men's own spoils; any shortfall falls to the party leader's gold. Sworn vassals and rulers pay none from their purses. Range 0–1. |
| Daily Maintenance | 0.005 | Daily upkeep per soldier as a share of his whole kit's worth (gear, horse and harness). Paid first from the men's own spoils; any shortfall falls to the party leader's gold. 0 stops maintenance. Range 0–0.05. |
| Construction Budget Share | 0.010 | The share of a fief's treasury put into its construction reserve every day. Apart from the owner's own purse this is the only thing that funds building, so it sets how fast a fief builds on its own account. 0 stops it. Range 0–0.1, in steps of 0.005. |
| Building Cost Multiplier | 250 | What a building project costs, as a multiple of the base game's price. In RBM a point of construction costs a coin, so the base game's prices would amount to about a week's tax; this multiplier makes a project the long undertaking it should be. 1 leaves the base game's prices. Range 1–1000. |
| Spoils Reserve (Days of Keep) | 20 | How many days of keep (its wage and its field maintenance together) a troop stack holds in its purse before upkeep spends the surplus. Range 0–60. |
| Recruit Maintenance Days | 20 | Days of maintenance a recruit mustered from a village or town brings into his stack's purse, priced off the same daily upkeep. 0 gives new recruits nothing. Range 0–30. |

### Settlement Upkeep

| Setting | Default | What it does |
|---|---|---|
| Food Days Bought | 20 | Days of food a man buys when he passes through a settlement. 0 stops troops buying food. Range 0–60. |
| Spoils Spent on Food | 0.50 | Share of a day's wage a man is willing to spend on a day's rations. Raising it lets better-paid veterans eat better food while recruits still buy the cheapest; 0 leaves everyone eating whatever is cheapest. Range 0–10. |
| Spoils Spent on Fun | 0.25 | How many days' wages a man drinks and gambles away for each day he sits idle in a settlement. At 1 he spends everything the day paid him; above 1 he eats into his savings. Range 0–10. |
| Luxury Cooldown (Days) | 20 | Days a troop stack waits after buying a luxury before it splurges again. 0 lets it buy on every roll. Range 0–120. |
| Luxury Buy Chance | 0.02 | Chance each hour that a stack idling in a settlement with more spoils than its reserve cap buys a luxury from the settlement's market. 0 stops it. Range 0–1. |
| Heal Cost per Tier | 10 | Gold a wounded man's stack pays, per tier he holds, to have him mended faster while resting in a settlement. Paid from the stack's spoils and left in the settlement. 0 stops paid healing. Range 0–100. |
| Heal Rate per Hour | 0.05 | The most of a stack's wounded that paid healing can mend in one hour, so a deep purse buys a faster recovery, not an instant one. Range 0–1. |

---

## RBM Tournament

| Setting | Default | What it does |
|---|---|---|
| Module Status (RBM Tournament) | Enabled | Tiered tournaments. Your tier comes from your level and the quality of your armor: below tier 5 you fight troops of your own tier, from tier 5 the lords and heroes in town. Arena gear, the prize (which can roll a better quality) and the renown you win follow your tier. Disabled restores the base game's tournaments. |

---

## RBM Debug & Logging

Diagnostic tools, all disabled by default. They are not meant for normal play. Log files are written
into a `logs` folder next to the config file (`Documents\Mount and Blade II Bannerlord\Configs\RBM\logs`).

| Setting | Default | What it does |
|---|---|---|
| Developer Mode | Disabled | Developer extras: an in-battle stats overlay, an "RBM Developer Stats" block in weapon tooltips and the siege archer-point debug pass. Takes effect from the next battle. |
| Field Battle Logging | Disabled | Writes every blow of a fought battle to `logs/battles`, one file per battle: attacker, target, weapon, body part, armor, damage and health left, plus the standings every 15 seconds and a summary at the end. Arenas and town visits are left out. Only runs while RBM Combat is enabled. |
| AI Behavior Logging | Disabled | Writes, second by second, every team's chosen tactic and every formation's behavior and movement order to `logs/ai`. Needs RBM AI. Takes effect from the next battle. |
| Armor Penetration Messages | Disabled | Prints the blunt trauma and armor penetration damage of every blow you deal or take to the message log. |
| Detailed Auto Resolve Logging | Disabled | Writes every auto-resolved battle to `logs/simulation`: the sides and their parties, troop power, charge and volley figures, and the outcome including routed men. Needs RBM Campaign. |
| ↳ Auto Resolve Per-Hit Detail | Disabled | Adds every simulated blow to the auto-resolve log (thousands of lines for a big battle). Needs Detailed Auto Resolve and Detailed Auto Resolve Logging. |
| Troop Power Logging | Disabled | Once a day, writes every party's troop power breakdown to `logs/powerCalculation`: its commander's perks and each troop stack's power per man. Needs Equipment Based Troop Power. |
| Spoils Logging | Disabled | Writes the spoils economy to `logs/campaign`: purse changes, loot awards, troop upgrades and their supply-town draws, recruit gear, food and carousing. Needs RBM Campaign. |
| ↳ Verbose Logging | Disabled | Adds a line for every troop stack to the spoils log; disabled keeps only the party-level summaries. Needs Spoils Logging. |
| Economy Logging | Disabled | Writes the village-to-town goods and food chain to `logs/economy`: each village's daily production, every villager party sent out with its size, composition and cargo, each town's rations, and the end-of-day state of every settlement. Verbose; only useful for tuning the economy. |
| Caravan Logging | Disabled | Writes the supply-caravan system to `logs/caravans`: each caravan dispatched, its arrival and sale, capital injected and repaid, and any lost on the road. Needs Kingdom Supply Caravans. |
