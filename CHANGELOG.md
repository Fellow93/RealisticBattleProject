# Changelog

## v4.5.3 (changes since v4.5.2)

### Combat
- **New shields:** four Khuzait kalkan-style strapped shields and a Cataphract Targe, worn strapped to the arm by Khuzait and Imperial cavalry. They swap back to their hand-held versions when the passive shoulder shields option is off.
- Ship ballista stones and boulders damage hulls and crew again (4.5.2 left them at almost no damage). Grapeshot stays capped at about vanilla's hull damage per pellet, so one volley can no longer sink a light ship.
- Hand-thrown fire pots and stones kill again: the fire pot is back to full weight and damage, with a large splash. Burning splashes (hand-thrown, siege-engine and War Sails pots) deal full damage in the inner 30% of the radius and fall off to zero at the edge.
- Spiked mace thrusts no longer pierce armor better than swords (mace pierce threshold 2 → 4). Existing configs still on the old default are updated once.
- Kicks, shield bashes and weapon bashes deal real blunt damage instead of 1–2 points, using the same model as punches: it scales with skill, boot, shield or weapon weight and the body part hit, and armor reduces it. A bash blocked by a shield and a kick into a raised shield do no damage. Kicks and bashes also connect more easily (larger hit radius). The damage follows the new "AI Kick and Bash" setting.
- Spears carried on the back no longer hang low and sink into the ground.
- Armor worn down in battle no longer stays damaged on the saved gear after the War Sails storyline battle, stealth missions or closing the inventory mid-mission.
- Sheathing a bow with an arrow nocked no longer leaves the string drawn, and a dropped bow no longer carries a phantom arrow.
- Village livestock no longer get knock-downs or 0-damage hit reactions.
- The tier-5 Kama dagger blade is no longer unlocked at campaign start.
- The Battanian Oathsworn now carries a sword.

### Ranged / AI shooting
- AI arrows and bolts no longer fall short at long range. RBM's arrow drag didn't match the engine's, so the AI aimed with one drag value while missiles flew with another.
- AI archers, crossbowmen and slingers hold fire until the target is actually within reach, instead of opening fire at 200–300 m and landing tens of metres short.
- AI slingers aim at the target instead of over it.
- AI shooters no longer over-lead moving targets, which makes them noticeably better against walking infantry and charging cavalry.
- Skilled AI crossbowmen are at least as accurate as vanilla ones and settle their aim as fast.
- AI shooters use the correct launch speed after re-equipping or picking up a bow, crossbow or sling.
- **Ranged aim arc (experimental, off by default):** a new RBM Combat setting for the player. While you draw a bow or crossbow or wind up a sling, a dotted arc shows the missile's predicted flight, with a marker where it will land (red on an enemy, green on a friendly) and a ring on the ground showing how far your current aiming error can scatter the shot. In third person, aiming upward also lifts the camera and tilts it down so the landing point of a high shot stays on screen. Your aim is unchanged; the crosshair is hidden while the camera is moved. Thrown weapons are not covered.

### AI
- Sieges: attackers on the walls fight instead of idling until the gate opens. Attackers no longer stand shuffling outside a breached wall, and troops leave ladders and towers the AI has given up on.
- Cavalry ordered to dismount now dismounts and holds its formation, including after the formation is delegated to the AI.
- Retreating riders are no longer held back by RBM's speed limit.
- Archers no longer walk into the enemy to pick up spent arrows. Ammo lying within 25 m of an enemy (45 m for horsemen) is ignored.
- Troops that lose their melee weapon pick up a dropped one again.
- Polearm troops no longer switch weapons back and forth while an enemy hovers at the edge of range.
- Swimmers in field battles (e.g. on river maps) are no longer treated as being on land, so they are no longer ordered to cheer or to pick up weapons while swimming.
- **Rally after reinforcements:** when part of an AI infantry formation is stranded far ahead of or behind the rest (typically first-wave survivors standing in front of a fresh wave), the formation stops and gathers before advancing again. After a reinforcement wave it gathers up to 200 m in front of where the wave entered, but never within 200 m of the enemy. Stragglers run straight to the nearest free place in the line at full speed. Works for both sides and every field tactic until the lines meet.
- Advancing AI lines no longer leave a few men lagging far behind. When the line widens, each man takes the nearest place in it instead of one on the far flank.
- Advancing AI lines that are already in formation move at their slower men's full speed instead of being held back further. The formation's speed limit now applies only while the line is ragged.
- Siege attackers whose only melee weapon is two-handed (e.g. the Imperial Flame line) no longer sheathe it to raise their shield and fight bare-handed.
- **AI kick and bash (new RBM AI setting, on by default):** AI foot soldiers now kick, shield bash and weapon bash, which the vanilla AI never does. They try it at close range against an enemy who is blocking, holding a weapon ready, showing his back or staggered; a clearly more skilled fighter also tries it at other times. How often depends on the two fighters' relative skill (mostly Athletics, partly weapon skill). A raised shield stops a bash but not a kick, so the AI kicks a man behind a raised shield and otherwise picks either. With the posture system on, throwing one costs posture and stamina, hit or miss.
- **Kick and bash knockdowns (same setting, player and AI):** a kick or bash always knocks down an enemy who is already staggered (posture break, posture tiredness or another kick or bash a moment earlier) or whose posture it empties. Otherwise it has a chance based on relative skill against an enemy holding a weapon ready or hit from behind; it is higher for a kick than a bash, from behind and against a tired enemy, and lower the heavier his armor.

### Campaign
- Starting gold is now a multiplier on the gold character creation gives you (default 5x, adjustable 1x–50x) instead of a flat 5,000-denar purse, so backstory choices matter again. The setting is renamed "Starting Gold Multiplier" and resets to 5x on existing configs. New campaigns only.
- Starving a besieged fief works again: paid healing no longer mends starving garrisons, militia or parties faster than starvation wounds them.
- Artisan quests no longer ask for hardwood or iron ore, which RBM villages don't produce. They ask for planks and tools instead.
- A plank now refines into 10 hardwood.
- **Troop upgrade prices on the party screen are honest now.** The price on an upgrade arrow is for the next man after spoils; upgrading a whole stack at once used to charge full price for the men the spoils didn't reach, several times what the arrow suggested. The arrow now only offers as many men as your gold covers, and its tooltip adds an "All N: you pay X" line with the real total.
- Upgrading a batch the spoils fully cover no longer fails silently when you are low on gold, which also showed a gold gain and paid it out.
- Cancelling the upgrade popup no longer forgets the spoils already promised to upgrades made before opening it.
- Splitting one stack between two upgrade branches in one visit no longer spends the same spoils twice.
- Men taken from a garrison or companion party and upgraded in the same visit pay with the spoils they bring. Men sent the other way take their share with them, and it is no longer spent on the upgrades of the men who stay.
- Troops donated to a garrison or handed to a new clan party keep their spoils.
- The troop upgrade cost multiplier now multiplies the price instead of stacking with perk discounts, so a low setting plus perks no longer makes upgrades free.
- Leaderless parties that belong to someone (patrols) only promote the men their own spoils cover instead of billing the owner, and AI upgrades no longer pay the supplying town more gold than the payer had.
- Prisoners recruited while in a town or village no longer arrive with a free spoils allowance.
- War Sails: AI armies that have to sail to a siege no longer board their ships, disembark and board again in an endless loop. A party's strength no longer drops while it is at sea; the landing limit for ship raids now only affects the decision to raid a village through its port.
- Clans without a culture are removed the way vanilla does it, and clans that are already eliminated are no longer destroyed again on every load.

### Tournament
- Skipping a round simulates the right fighters again, instead of reusing the previous match and scoring nobody.
- Tournaments no longer crash on load when an overhaul mod removes vanilla's elite prize items. Unready or junk prizes are replaced.
- Tournament participants use the host town's culture, and NPC tournament tiers compare against average armor tier, as the player's do.

### Config
- All logging toggles and developer mode are now in one collapsible "RBM Debug & Logging" section. This adds rows for the AI behavior log and armor penetration messages, which had no UI before.
- A newly created config is read right away, so settings missing from the default file are saved from the first session.
- With Battle Hit Logging on, every AI bow, crossbow and sling shot is traced in the hit log (aim, predicted range, where it landed).

### Stability / fixes
- Siege machines no longer crash the mission when a soldier walking to one has lost his formation, and releasing a siege engine no longer crashes on soldiers without an AI component.
- Arrows no longer crash the game when the shooter has already left the battle, whether they hit a shield worn on the back or armor (e.g. a helmet on a headshot).
- A ranged hit that both breaks posture and kills no longer forces a stagger animation on the dying soldier.
- Hardening against a reported crash from missile collisions in very large battles. Missiles that stick into a shield the soldier no longer holds are removed instead of left floating.
- Hiring modded tavern mercenaries in your own fief no longer crashes the hire menu.
- The smithy no longer crashes on saved crafting orders whose weapon uses a part its template no longer has (e.g. the gang leader dagger quest).

## v4.5.2 (changes since v4.5.1)

### Combat
- Disabling RBM Combat now fully disables it. RBM's native and spear combat parameters used to stay active with Combat off: AI archers kept engaging at RBM's range, the slower walk speed stayed, and so on. Native parameters switch back immediately. Spear animation parameters need a restart, and the config screen shows a notice when one is needed.
- With Combat off, RBM_WS no longer adds the Nord crafting pieces a second time, which had made every Nord spear throwable. The "RBM_WS missing" warning now only shows when Combat is on.
- RBM_WS weapon descriptions now match NavalDLC's again. Nord atgeirs regain their one-handed usage, and two-handed swords crafted from Nord pieces regain the one-handed grip.
- The sneak-attack bonus now applies before armor, so stealth kills work again (the stealth tutorial and War Sails stealth quests).
- Landing a punch no longer stuns the puncher. The recoil damage is still taken, but it can never kill.
- Smithing: fixed the crash with long two-handed mace handles, and restored the Battanian falx blades. Crafting pieces that a future game patch or DLC adds are now attached to a weapon type automatically, so the smithy no longer crashes on them.
- Loading a save made with Combat on, with Combat now off, no longer crashes on smithed weapons.
- The Cloth Banner no longer removes the couch ability from lances.
- The heavy spiked club head no longer floats above its handle.
- The Light Menavlion's grip is back on the shaft, and the weapon is now shorter than the Heavy Menavlion.
- Repeating crossbows keep their reticle while bolts are still loaded.
- Troop equipment fixes: the Fian Champion's bows, the Skolder tier 2/3 helmets, two broken cape entries, and the missing reins on the light harness.
- Passive shoulder shields: every `_shoulder` shield (e.g. the Falxman's targe) is swapped back when the option is off.
- War Sails grapeshot no longer deals several times vanilla damage to ship hulls, where a single volley could sink a light ship. It stays deadly to crew.
- Armwraps and the Leather Coat regain their vanilla stealth values, and the Leather Coat counts as stealth gear again.
- Fifteen arrow visual entries with broken Empire/Vlandia culture references now resolve.
- With Campaign off, junk weapons no longer sell for 400+ denars. The legacy weapon price floor is lower: tier-0 weapons are worth about 105, while tier 3 and above barely change.
- Hardening against reported crashes when a missile breaks a shield.

### Ranged
- The "Vanilla" reload setting now also gives the player vanilla bow draw speed, not only vanilla reload speed.
- New option: "Ranged reload applies to AI" (off by default). When on, the AI follows the reload setting for both reload and draw speed.
- Fixed arrows being returned twice, or quivers overfilling, when a bow is sheathed with an arrow nocked.
- Ammo with zero weight or count no longer produces a broken missile speed.
- With Combat off and AI on, the player's bow and crossbow aiming is vanilla again. AI shooters keep RBM's tuning.

### Posture / AI
- Heavy posture breaks dismount riders again. A single hit that deals at least 33% of a rider's max posture can unseat him, including through a shield block, and a mounted attacker whose blocked attack breaks his own posture can be dismounted too.
- **Signature two-handed weapons.** Eastern heavy lancers keep the lance in both hands with the shield slung on the back, and draw mace and shield only once an enemy is within 2.5 m. Falxmen, shock troops, two-handed axemen and atgeir infantry never draw a shield, and throw only at 6–25 m.
- Eastern heavy lancers no longer swirl the lance between grips after a couch, or flip between lance and mace while hovering near an enemy.
- Troops carrying a polearm and a sidearm prefer the polearm until an enemy has stayed within 2.5 m for 2 s (so a passing enemy or a cavalry charge doesn't trigger it), and keep the sidearm until no enemy has been within that range for 4 s (2 s for cavalry, so they have the lance back for the next charge). They no longer switch weapons back and forth while enemies hover at the edge. Troops with only a polearm are left alone, and throwing weapons (javelins, throwing axes and knives, throwable spears) count as neither polearm nor sidearm.
- **Reinforcements** enter from the map border behind their own side, on dry ground, instead of spawning in one mid-field blob. Waves on both sides are paired, and each side's reinforcements are capped at half the battle size.
- Player horse archers stay in formation under Move/Stop and in column under Follow. Those who lose their horse fight in melee instead of holding their bow.
- A player's charge at a selected enemy formation now goes for that formation, instead of the nearest large enemy formation.
- In sieges, player formations and shieldwall, square or circle formations keep their slots when an enemy gets close, so a gate shieldwall no longer dissolves into chases.
- Archers flanking with no infantry to support hold a stand-off on their own side of the enemy, instead of marching through the enemy line.
- AI order calculations no longer overwrite the formation of a player leading as a captain (e.g. shieldwall replaced by charge + line every tick).
- AI infantry only braces against enemy cavalry that is actually charging within 150 m. Parked cavalry no longer freezes the attacker at its spawn.
- Infantry under the split-archers tactic waits at most 30 s for its archers.
- Siege archers switch to melee at 6 m instead of 15 m, so defenders no longer drop their bows after the first shot.
- Frontline holds are released properly, so troops (e.g. archers with an enemy close by) no longer stay rooted in place. Retreating troops are never held.
- Archers holding their sidearm are no longer moved to Infantry and back, which re-formed both formations each time.
- Charge-routed troops are not rallied back out of a Retreat order.
- Mounted banner bearers no longer ride back and forth at battle start.
- The formation banner marker keeps up with cavalry.

### Campaign
- Days-of-food forecasts account for troop rations running out. 31 food no longer shows as lasting thousands of days, and AI army and siege decisions use the corrected figure.
- Saves carrying troop stacks broken by another mod are repaired on load instead of crashing in the daily training tick.
- Fixed the Escort Merchant Caravan quest crashing on accept with the troop overhaul.
- Well-provisioned parties with food perks (e.g. Spartan, Warrior's Diet) no longer eat almost nothing. The perks now scale what the party actually eats.

### Tournament
- Top-tier tournaments apply vanilla's eligibility checks again, so children, wounded and non-combatant heroes no longer join.
- The Self Promoter perk's renown bonus is granted for tournament wins again.

### Stability / fixes
- Fixed several crashes to desktop: fast horse deaths near riderless horses, charge blows that killed their victim, forced stagger animations on swimmers, climbers and siege-engine users, health bars reading deleted agents (War Sails), and native writes from worker threads.
- Swimmers in naval battles are left to native formation handling, and the signature-weapon grip stands down while swimming or climbing.
- Fixed a crash on save load caused by the party food tooltip patch being applied before game texts were loaded.
- Per-battle AI data is cleared at the start of every mission, so old battles are no longer kept in memory. This also fixes a crash when reloading.
- A redirected, protected or unwritable Documents folder, or a corrupt config.xml, no longer crashes the game at startup. RBM uses the defaults and logs the problem.
- Code cleanup: removed unused variables, fields and dead code so the mod builds without compiler warnings. No gameplay change.

## v4.5.1 (changes since v4.5.0.2)

### Combat
- RBM's managed core parameters (spear/thrust tuning) now actually load. They were never applied before. They apply only with RBM Combat enabled.
  - Swings that hit the arm deal 0.5x damage (vanilla 0.15x); thrusts that hit the arm deal 0.034x.
  - Fuller thrust and late-swing speed curves; ballista bolts have 5x drag.
- Fixed the two-handed mace skill coefficient (1.125 → 0.1125). Every swing was hitting the damage cap from around skill 25.
- Punch damage: bare fists 0.4 → 0.3, cloth/leather gloves 0.3 → 0.4. A glove no longer loses to a bare fist.
- Fixed unarmed blows while holding an item applying the skill formula twice.
- Shield impacts: blocks and parries no longer spawn body-hit dust or blood. Metal shields throw sparks on every block, and wooden shields splinter on heavy blocks.
- Restored vanilla collision reactions that RBM's armor thresholds skipped: kick and bash follow-through, hilt hits, shrugged-off blows and body punches. The couched lance keeps RBM's stick/bounce but regains the vanilla pass-through roll, so the Skewer perk works again.
- Weapon sticking and hit particles now use the blow's damage type, so a swing that doesn't land on the blade counts as blunt.

### Posture / AI
- Posture crush-through now uses the same damage math as live combat. The AI's copy dated from 2023 and had drifted from it. Crush-through damage is scaled x0.85 to compensate for the change.
- Fixed unarmed crush-through dividing by zero (NaN damage).
- Cavalry-charge knockdown routs flee in formation again. Since 4.5.0 these troops left their formation, came back formationless, and then charged on their own, ignoring orders. This was most visible in large battles. Charge routs also no longer set off the flee-contagion morale wave.
- The rally now brings back only charge-routed troops. Ordered retreats and normal morale routs are left alone.
- Fixed banner bearers added by Raise Your Banner clumping together, and bearers "attacking air". Bearers now always stand next to a neighbouring soldier in the formation grid.

### Campaign
- **New: per-settlement recruit pool.** Manpower is now a finite, regenerating resource.
  - Its size comes from prosperity (towns/castles) or hearth (villages): it grows +0.03 per point a day, up to a maximum of 0.2 per point.
  - Each new volunteer costs 1 point. Each new garrison soldier costs 1 + (garrison / soft size)², where the soft size is 150 for towns and 100 for castles.
  - Garrisons stop growing when the pool falls to a reserve of 0.07 per point, which is kept for volunteers.
  - Militia and troop transfers are unaffected.
  - Building bonuses: Train Militia / Housing daily project +25% growth, Raise Troops daily project +50%, Castellan's Office max +10/20/30%, Barracks soft size +20/40/60, a fief's Roads and Paths +10/20/30% growth for its villages.
  - The settlement tooltip and the garrison tooltip show the pool, the per-soldier garrison cost and the building bonuses.
  - Building descriptions that promised garrison capacity or automatic recruitment (which do nothing under RBM) have been rewritten.
- **AI kingdoms form armies and take fiefs again.** Their siege targets always scored 0, so armies never gathered.
  - Fresh sieges now need 2.5x strength (it was 3x).
  - AI lords top up their parties from their own clan's surplus garrisons, so they meet the party-size requirement for joining armies. Lords who already qualify aren't pulled home.
- New option: "AI Lords Buy Equipment". Turn it off if another mod manages lord gear. It is on by default. Pack trains are unaffected.
- Goods are now removed and paid for by the exact stack (including item modifiers) everywhere: citizen demand, troop food and drink, refining, minting, construction, caravans and workshops. Previously a sale could be paid for goods that never left the stack, or take from the wrong stack. A caravan that is short of cargo is now trimmed instead of creating goods from nothing.
- Recruit, upgrade and luxury market purchases pay what the stack they actually take is worth.

### Auto-resolve
- Melee profiles use the shared combat skill table, so future retunes apply to it automatically. Blunt thrusts (whips, pitchforks, training weapons) are no longer pinned at the damage cap.

### Stability / fixes
- Fixed markets getting permanently stuck with NaN prices (#22). Existing saves are repaired on the next daily tick. Town food budgets and rounding are also guarded against NaN.
- Fixed a race between the save thread and the town food cache, and a crash for towns with no owner.
- Config: decimal settings now parse and save correctly on comma-decimal locales. Previously 0.05 was read as 5 (thrust modifier, weapon-type factors, horse price).
- A config version mismatch that resets settings is now logged.

### UI
- Weapon comparison tooltip: bow, crossbow and throwable rows show the compared weapon's own values, rows stay aligned with the target column, and missing values are left blank instead of showing -100 or 0.
