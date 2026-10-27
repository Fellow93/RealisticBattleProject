# Changelog

## v4.8.0 (changes since v4.7.0)

### Compatibility
- Updated for Bannerlord v1.5.6: fixed a crash on game start and perks (troop perks, Arrow Catcher, captain and party perks in battles and auto-resolve) after the game's perk changes. This build needs v1.5.6 or newer.

### Performance
- Better performance in large battles: fewer stutters at deployment, when reinforcements arrive and at first contact, and less work every frame in a big melee.
- Better frame rate in large battles: soldiers pick their melee target faster and the battle AI checks each formation's fighting state once per frame instead of several times, with no change to how the AI fights.

### Combat
- Horse charges now depend on the horse's weight. The charge counted every horse, camel and mule as 400 kg, so light, fast horses charged hardest; it now uses each mount's own weight, the same one that already slows heavy horses down. An average troop horse charges as hard as before: War Camels, Cossians and Destriers hit harder, light horses like the Hunter less, and pack animals much less. (Thanks to Unknown-Won.)
- Shields in custom battles no longer pull in missiles that would have missed; they only catch what actually hits them, as in the campaign.
- The enemy health/posture/stamina panel at the top of the screen now goes away a few seconds after you kill or knock out that enemy, name included. Before, the enemy's name stayed on screen, or the whole panel stayed up with an empty health bar for about ten seconds. (Thanks to JZ0907.)

### AI
- **Fewer crashes:** an error in RBM's battle AI no longer crashes the game; only that formation skips its decision for a moment. A game update that breaks part of RBM AI only switches that part off.
- Fixed missing-text errors in the battle messages for the newer tactics and formation orders.
- **Real formation shapes:** the AI now measures the distance between formations by their real shape (a wide line is a long rectangle, not a circle). Wide lines no longer count as close to an enemy that is still well in front of them or off to the side at an angle, an enemy coming round a line's end is noticed in time, and lines that cross each other count as touching. This drives when defenders charge, when reserves and archers react, when the reaction system sends help and when the armies count as engaged.

#### Battle plans
- **Plans fit the army:** every culture can now use every battle plan, rated by how well its army suits it. A culture's own signature plan still gets a bonus, the commander's traits and Tactics skill shape the choice, and each battle adds a little variation. Armies keep a plan for at least 30 seconds instead of flip-flopping.
- **Terrain-aware battle plans** (new setting, on by default): in field battles the AI reads the ground once the armies have deployed and uses it in its plans:
  - Defenders pick where to fight: a hill, crest, narrow pass, bridge or ford they can reach well before the enemy, and look again (within limits) when the enemy moves or pulls back.
  - Flank groups go round the enemy's open side, and flanks covered by water, cliffs or woods are not guarded.
  - Archers take nearby hills and viewpoints, cavalry charges along clear runs and avoids woods and rough ground, and foot formations loosen up in woods and narrow down in tight spots.
  - Plans are updated as the armies move.
- **New battle plans:** Hold the Choke (Sturgia), Reserve Plug (Empire) and Hedgehog (Battania, the schiltron).
- Removed the Hammer and Anvil cavalry tactic.
- **AI reacts to the battle** (new setting, on by default): AI armies help formations under attack, cut off cavalry riding round their line, hit exposed flanks, hunt archers that are hurting them, and switch plans when the battle turns. They ignore small bait groups and never move your formations; allied AI armies help yours too.
- **Small fights stay simple:** sides under about 60 men, bandits and sally-outs fought on castle maps skip the terrain plans. All-cavalry armies now attack, and small mounted groups no longer flee at the start.
- Attacking armies no longer flip between attacking and advancing, or reshuffle their formations mid-battle.
- The attacking AI now looks for a better way to reach your army — round a held bridge or choke, along a covered route, or toward an open flank — instead of always walking straight at you; it goes straight in once close.

#### Defenders
- Holding lines are much harder to bait: small groups, distant archers and passing riders no longer draw them out. They fight in place, charge only real infantry that reaches them, return to their post afterwards, and turn to face your main force. A held choke is given up when you go round it.
- Defending lines help friendly groups attacked next to them, and re-form once reinforcements arrive.
- Hedgehog: its archers stand in a ring with room inside, it reacts only to melee cavalry (not horse archers), and it forms up in place when cavalry catches it on the march.
- The hidden infantry reserve can't be lured out, and follows its line when there is no hidden spot.
- Defenders no longer march back and forth between hilltops before the battle.
- Defending cavalry is calmer: it rides out after your horsemen only when it is strong enough and stays close to its army; reserve cavalry defends itself when attacked.
- An outnumbered defending AI now looks harder for hills, chokes and covered flanks and will march a bit further and take a bit more risk to reach them; the more it is outnumbered, the more it prefers such ground.

#### Archers
- Archers stand and shoot once the lines meet, from one place with a clear view, instead of running back and forth. They stand their ground against cavalry and horse archers, take cover behind their infantry from enemy infantry, and shoot back at enemy archers.
- Archers choose line or loose order by the situation, stand about four ranks deep instead of in a very wide line, form a loose ring when surrounded, and are no longer put into shield walls or tight squares by RBM.
- Archers keep close to their main infantry, also when you lead it; attacking archers move with the army instead of waiting on hills.

#### Infantry
- **AI infantry charges only within 50 m:** it marches up in formation and breaks into the charge only when close (a shield wall, square or circle within about 10 m), so it arrives as a block instead of a tired crowd. Your own foot formations do the same when you order a charge from far away. Cavalry is unchanged.
- **AI infantry keeps its eyes on the real threat:** charging infantry fights the formation it charged instead of scattering after passing horsemen, and soldiers only turn to another enemy group that is right next to them. It weighs enemy cavalry against the enemy infantry by strength and distance: riders that clearly outweigh the infantry, or are charging into the block, get the charge; weaker riders that hit the block while the enemy line is still far away are fought where the block stands.
- Soldiers no longer walk out ahead of a moving formation, and formations no longer walk backwards while pausing.
- No infantry flank guards under 100 melee infantry; fixed small infantry forces splitting into tiny groups.
- Infantry holding a position turns to face cavalry riding round it, and defending infantry faces the enemy's main infantry.
- Shield walls move faster and no longer flip in and out of formation under cavalry; infantry circles stand closer together.
- Less weapon swapping for troops with a polearm and throwing weapons or a two-handed sidearm.
- Javelin skirmisher formations now skirmish, and skirmishers stay beside their own main infantry.
- Banner bearers face forward and keep up with their formation.
- AI formations no longer pick places on top of each other.

#### Melee
- Back ranks no longer shove through their own front ranks or bunch up, soldiers packed in a crowd no longer squeeze sideways, and frontline soldiers no longer push into rocks and walls.

#### Cavalry
- Cavalry ignores small groups and gives up chases that get nowhere; cavalry wedge fixes.
- Wedge cavalry no longer leaves its attack to chase enemy horsemen near your horse archers; fixed cavalry sometimes charging from far away right after changing formation; and formations no longer react to enemy cavalry that stays in front of the army unless it charges right into them, including archers standing in front; riders going round the flanks still get an answer.
- Fixed both cavalry groups of an army sometimes going to guard the same flank, standing on top of each other, when the other flank was safe.
- Wedge cavalry forms up in front of its infantry again instead of shifting between charge lanes off to the side.
- Cavalry picks where to regroup after a charge from where the enemy and friendly formations are at that moment, avoiding bad ground, instead of using points worked out at the start of the battle.
- Advancing AI formations keep turning to face you while they approach (they used to keep the facing they started with and walk sideways toward you), and square up to your line once close.
- Horse archers are bolder against weaker horse archers, more careful of strong cavalry, really ride round a formation that chases them, shoot as they circle (enemy on their bow side, within their bows' reach), keep a limited distance even from much stronger cavalry, switch to another enemy formation they can circle when enemy riders block the way round their target, wait in one steady spot on their own side only when nothing is open (instead of twitching between standing still and dashing across the enemy army), steer clear of rocks and the map edge, never wait pressed against the edge, and curve away toward open ground when they flee, instead of being chased along the edge into a corner.

#### Reinforcements
- Reinforcements join your existing formation instead of forming a stray new one.
- The rally after a reinforcement wave no longer pulls men out of melee, gathers on the bulk of the formation and ends sooner.
- AI infantry no longer keeps charging when only a few men are still fighting far ahead; the rest march up in formation.
- Defending lines no longer leave their position to help friends fighting far away, or right after being called back.
- Flank guards under arrow fire step back to their closer post and stay there, instead of shuffling back and forth every few seconds.

#### Deep water
- **Deep water blocks paths** (new setting, on by default): water about 1 m deep or more is walled off by an invisible wall that stops everyone (missiles pass). AI paths and formations go round it, and soldiers who end up in it are walked back out. Fords and bridges stay open, and it switches itself off for a battle if it would cut the armies apart.

#### Debug
- New Battle Terrain Logging and Deep Water Path Logging settings (Debug & Logging, off by default).
- The developer battle stats show each side's ranked battle plans and how it reads the ground. In Developer Mode, Ctrl+Shift+D shows the terrain features and AI decisions on the battlefield; Ctrl+Shift+W (with a deep-water option on) shows where the AI can walk.

### Campaign
- **Towns and castles raise and pay for their own patrols** (new Settlement Patrols setting, on by default). Every town and castle can keep one if its treasury (and a town's citizens' savings) can pay for it; your clan's gold is never touched. Its size (12–60 men) and quality grow with that money and with the Guard House, the Coastal Guard Edict and the Patrols perk. Its men come from the garrison first and only the rest are recruited; it tops up at home instead of being rebuilt for free, and its men return to the garrison when it is stood down.
- Patrols march faster (+0.5 map speed), and every patrol raises its settlement's security.
- New Patrol Budget Share and Patrol Logging settings. Switching patrols off mid-campaign is safe; removing RBM from a save with RBM patrols is not.

### Auto-resolve
- **Troop perks now count in auto-resolve:** hit points, ammo, damage, armor penetration, armor and damage taken work as in a fought battle (needs the Troop Perks setting and auto-resolve's perk setting). Lords' and companions' own combat perks count too.
- Extra hit points now protect against siege engines and very heavy blows in auto-resolve.

## v4.7.0 (changes since v4.6.0)

### Combat
- **Regular troops can have hero perks** (new Troop Perks setting): perks are given per troop type in `ModuleData/rbm_troop_perks.xml` and shown in the troop tooltip and encyclopedia. About 150 perks work for troops. No troops have perks yet: this release only adds the system. New Troop Perk Logging setting.
- Shields, slings and sling stones come in different qualities again.
- Shield and weapon bashes reach as far as a kick, and a man knocked down by a kick or bash falls where he stands instead of flying back.
- Stone sling bullets do blunt damage again, and thrown stones no longer hit charging riders twice as hard. Every weapon type has its own blunt threshold and blunt factor in the combat settings.
- Form Fitting Armor now lightens armor everywhere RBM uses its weight.
- Small percentage damage bonuses are no longer lost to rounding, and body hits are rounded once instead of twice.
- **Arrow Catcher works again:** missiles blocked with your raised shield cost less posture, stamina and shield damage.
- **Thrust and swing fixes:** light polearms no longer thrust harder than heavy ones, one-handed thrusts no longer count the weapon's weight twice (about 10–20% weaker), and swings landing early or late in the arc hit harder.
- Pilums' armor piercing now shows in the tooltip and counts in auto-resolve.
- Shields last longer: blocked melee blows no longer get the game's skill bonus, and arrows, bolts and sling stones no longer tear through them.
- Horse charges in the campaign no longer hit harder than in custom battles.
- **Less blunt trauma through armor:** blunt blows pass on less, mail passes more and plate less, kicks and bashes much less, and the Blunt Trauma Multiplier now defaults to 0.9.
- A blow the armor stops now sounds like it hit armor, and blocks no longer add a body-hit sound.
- The Armor Penetration Message setting also reports blunt hits.
- Low-grip polearms and picks from other mods deal sensible damage.
- Melee weapon skill has stronger diminishing returns (about 10% less damage at skill 200, 20% at 330).

### Posture
- **Posture rework:** perfect parries and chamber blocks are rewarded, landing a blow costs the attacker almost nothing, heavy weapons cost more to have blocked, and posture no longer regenerates mid-exchange. A break from a body hit no longer leaves you broken.
- Skill matters less to posture, and only closing speed adds to it.
- Missiles drain posture by the damage they do, and missiles caught on your raised shield cost a little posture and stamina.
- Kicks and bashes caught on a guard now rock the blocker.
- Fixed wrong shield posture costs on Turkish Windows and slower regeneration at low frame rates.
- New Large Vertical Bars setting for the posture and stamina bars.

### AI
- Skilled AI fighters parry more often with posture on.
- **AI horse archers no longer bunch up and ride into your other formations:** they ride loose, turn away from other enemy formations, fear enemy cavalry by its strength, and keep their bows until an enemy is right on them.
- **Formations shout** (new Formation shouts setting, on by default): soldiers echo orders, AI captains call theirs, main infantry keeps a marching call in its culture's rhythm, charges yell and roar, and cavalry blow horns. Sound only.
- New experimental Staggered order reaction setting: soldiers carry out orders after a short delay of their own.
- AI infantry forms shield walls again (new Shield wall share setting, default 60%).
- RBM's AI no longer pulls soldiers away from jobs the game gave them, such as crewing siege engines.
- **RBM Stance:** posture, stamina, AI kicks and bashes and the combat slow motion have their own config section and switch, and work with RBM AI off.

### Campaign
- **Villages no longer grow without end:** hearths approach a land capacity of twice the starting size, and village militia is capped at 10–20% of hearths.
- **Recruit spoils for everyone:** garrisons, militia, caravans, rebels and every starting army get the spoils a recruit brings, and men moved between parties and garrisons take their share.
- AI lords always appear at a town or castle, and a respawned lord takes his men from that garrison instead of getting a free army.
- **Clan parties recruit better troops:** companions' and non-leader lords' parties get more gold and can borrow from the clan leader to recruit.
- **AI armies form again:** AI clans pay their parties from a real daily budget, need less strength (700) to raise an army, can call more lords, carry more food and keep the pack animals they need.
- **Neutral mercenary companies** (new setting, on by default): two per main culture, made of tavern mercenaries, that army leaders (you included) can hire for gold for the length of an army. New Mercenary Companies Logging setting.
- **Clan finances tab** in the RBM Ledger: 30 days of income and expenses, by day and by source.
- Gear for new men and promotions is bought piece by piece at market prices.
- Unticking a garrison's auto recruitment now stops it growing.
- Hovering a troop on the party screen shows its tooltip.
- Clearer building effects in the town and castle management screen.
- Settlement wealth in map tooltips is rounded (hold Alt for the exact amount), and troops are listed by type and tier.
- The Switch Lord picker can observe a lord's party instead of switching to it.
- Doctor's Oath gives Medicine experience in fought battles again, deserters no longer count village militia twice, and carousing messages appear far less often.
- War Sails: supply caravans no longer get stuck at port towns, and captain perks count as they work at sea.
- Parties cross bridges at full speed.
- The campaign map runs faster and no longer stutters with RBM's logs on. New Garrison Refill, Party Budget and Stuck Party Logging settings.
- New debug console command `campaign.rbm_test_battle`: a field battle with every troop type, with saved presets.

### Auto-resolve
- **Detailed Auto Resolve has its own config section and works with RBM Campaign off.**
- Fixed siege assaults being thrown back at no cost when the attackers had no siege engines.
- With RBM Combat off, auto-resolve follows the game's own rules: archers deal proper damage again, and hit locations, skill, horse charges and party strength match the base game.
- Thrown stones fly at a fixed speed, and the battle panel shows the hardest hits again with logging off.

## v4.6.0 (changes since v4.5.4)

### Compatibility
- Updated for Bannerlord v1.5.4 and War Sails v1.3.4. RBM no longer runs on v1.4.8.

### Combat
- War Sails: cutting a boarding rope no longer stops the swing. Roguery speeds up crouching again.

### AI
- Formations under arrow fire raise their shields and react again.

### Campaign
- A governor's Generosity changes his garrison's food again; Accuracy Training adds port-village militia only while the governor is in town.
- Forced supplies, coin from battle sites and executed lords' kit now go through spoils and settlement wealth instead of appearing or vanishing.
- Blood money goes to the town where it is paid, and a clan's ransom is capped at what it can pay.

## v4.5.4 (changes since v4.5.3)

### Combat
- Ranged weapons from other mods fly at their own missile speed.
- Pommel and handle hits get no blade bonus, hit as hard as the swing behind them, strike shields as blunt blows and work on non-crafted weapons too.
- Non-crafted weapons do posture damage when blocked.
- Head and arm armor perks now protect the face and under the shoulder; swings count as face hits less often.
- **Posture fixes:** kicks and bashes cost posture and stamina once and drain a blocker as unarmed attacks, a heavy posture break really unhorses riders, and crush-through damage now comes from the blow that was blocked.
- In first person, posture tiredness staggers you backward.

### AI
- Kick and bash knockdowns are less certain; the AI kicks at broken posture and banner bearers no longer kick friends.
- **Flank cavalry** stays organized before the lines meet and covers its infantry.
- **Cavalry charges** ride through, pull out after 8 seconds of real melee, and pick targets by type, distance and strength.
- **Horse archers keep formation.**
- **Attackers keep advancing** instead of stalling after reinforcements; the rally lasts at most 45 seconds.
- Both split-infantry flanks charge, rear ranks follow the line, and soldiers step into gaps sensibly.
- Swimmers no longer freeze, and Aserai Vanguard Faris charge again.

### Campaign
- Clothing and armor workshops make twice as much, gambeson horse armor recipes work, wool, hides and cattle are cheaper, and supply caravans carry more.
- **Expected daily gold now matches what you are paid.**

### Translations
- Most remaining English-only text can be translated (thanks to OhMyMike57), with name fixes (thanks to DivineOblivion). A translation can ship its own changelog.

## v4.5.3 (changes since v4.5.2)

### Combat
- **New shields:** four Khuzait strapped shields and a Cataphract Targe.
- **Missiles and shields:** metal shields stop missiles, arrows stick or break in wooden ones, pila punch through shields, and stuck missiles fall out as items you can pick up (new sliders).
- **Armor sliders:** a new Armor Effectiveness slider plus advanced armor sliders.
- Kicks and bashes deal real blunt damage; spiked mace thrusts pierce less.
- Ship ballista stones, fire pots and stones deal proper damage again.
- Experimental flying arrow thickness slider.
- Auto-resolve follows the live armor rules; pavises have more armor.
- Fixes for spears on the back, worn armor after special missions, sheathed bows, livestock, the Kama dagger and the Oathsworn's sword.

### Ranged / AI shooting
- **AI shooting fixed:** arrows and bolts no longer fall short, shooters wait until in range, throws and sling shots go where aimed, and moving targets are no longer over-led.
- Javelins, throwing axes and knives use the game's own drag again.
- Experimental ranged aim arc for the player.

### AI
- **AI kick and bash** (new setting, on by default): AI foot soldiers kick, shield bash and weapon bash, with knockdowns by skill.
- **Bigger armies field more men** (square-root share, 25–75%), and every reinforcement wave refills fully.
- **Rally after reinforcements**, and advancing lines leave fewer men behind.
- **Better frame rate** in charges and with cavalry.
- Siege attackers on the walls fight, and troops leave abandoned ladders and towers.
- Troops only fetch spent ammo when nearly out and never near the enemy, and pick up a dropped melee weapon again.
- Fixes for dismounted cavalry, retreating riders, swimmers, two-handed siege troops, polearm swapping, lancers, horse archers, braced infantry and cavalry knocking back its own men.
- Posture tiredness plays a stagger animation.

### Campaign
- Starting gold is now a multiplier (default 5x).
- **Honest upgrade prices** on the party screen, and several spoils fixes.
- **Militia caps:** a soft cap of 40% and a hard cap of 75%, raised by buildings and policies.
- **Workshop gear follows the town's culture,** and towns no longer run out of leather.
- Starving a besieged fief works again, and AI armies no longer form and disband in a day or loop boarding ships (War Sails).
- Artisan quests ask for planks and tools; towns arm militia from their market.

### Tournament
- Fixes for round skipping, missing prizes and the participants' culture.

### Config
- Logging and developer mode are in one "RBM Debug & Logging" section, every option has a tooltip, and Reset to Default restores real defaults.
- **Changelog on the title screen.**

### Stability / fixes
- Fixed crashes on Linux/Proton, in custom battle deployment, with siege machines, missiles, modded tavern mercenaries and the smithy.

## v4.5.2 (changes since v4.5.1)

### Combat
- Disabling RBM Combat now fully disables it.
- Sneak attacks work again, and punches no longer stun the puncher.
- Smithing crash fixes and restored falx blades.
- Fixes for War Sails items, grapeshot, several weapons, troop equipment, stealth gear and weapon prices with Campaign off.

### Ranged
- The Vanilla reload setting covers draw speed too, and a new option applies it to the AI.

### Posture / AI
- Heavy posture breaks dismount riders again.
- **Signature two-handed weapons:** eastern lancers keep the lance in both hands, and falxmen and other two-handed troops never draw a shield.
- Less weapon swapping between polearm and sidearm.
- **Reinforcements** enter from the map border behind their own side.
- Fixes for player horse archers, player charges, siege shield walls, flanking archers, cavalry braces, siege archers, frontline holds and banner bearers.

### Campaign
- Food forecasts and food perks fixed, and a crash with the Escort Merchant Caravan quest.

### Tournament
- Eligibility checks and Self Promoter fixed.

### Stability / fixes
- Fixed several crashes in battle, at startup and on save load.

## v4.5.1 (changes since v4.5.0.2)

### Combat
- RBM's spear and thrust parameters now actually load.
- Two-handed mace skill and punch damage fixes.
- Shields spark and splinter instead of bleeding, and vanilla collision reactions are back (Skewer works again).

### Posture / AI
- Crush-through uses the live damage math.
- Charge routs flee in formation, and the rally brings back only them.
- Banner bearers no longer clump together.

### Campaign
- **Per-settlement recruit pool:** manpower is a finite, regenerating resource.
- **AI kingdoms form armies and take fiefs again.**
- New "AI Lords Buy Equipment" option.
- Market purchases take and pay for the exact stack.

### Auto-resolve
- Melee uses the shared combat skill table.

### Stability / fixes
- Fixed stuck NaN market prices (#22), a save-thread race and comma-decimal config parsing.

### UI
- Weapon comparison tooltip fixes.
