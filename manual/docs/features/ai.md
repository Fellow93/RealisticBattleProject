# Battle AI

RBM replaces much of the base game's battle AI, from army tactics down to how a single soldier blocks, steps
and picks his weapon. Armies use tactics that suit their culture. Archers skirmish and pull back behind their
infantry. Cavalry charge through, reform and charge again. Infantry lines advance at the pace of their slowest
men and jostle for position once they meet the enemy. Soldiers fight according to their skill.

Everything on this page needs the **RBM AI** toggle (on by default). Turning it off returns the battle AI to
the base game. A few parts also need **RBM Combat**, as noted below. The posture and stamina system is part of
RBM AI too, and has its own page: [Posture & Stamina](posture.md). Settings are listed in
[Settings](../settings.md).

## Tactics by culture

In field battles, RBM gives each side a set of tactics based on its leader's culture:

| Side | Culture | Special tactic |
|---|---|---|
| Attacker | Empire | **Embolon**: heavy cavalry forms a wedge in front of the infantry |
| Attacker | Aserai | **Split Skirmishers**: javelin troops form a separate screen |
| Attacker | Sturgia | **Split Infantry**: the infantry splits into a center and two flanks |
| Attacker | Battania | **Split Archers**: archers deploy on both flanks |
| Attacker | Khuzait | **Ranged Harassment** (from the base game) |
| Defender | Battania | Split Archers (defensive version) |
| Defender | Sturgia | Split Infantry (defensive version) |

Every side can also use a full-scale attack, and defenders can use a defensive line and defensive engagement.
A culture's special tactic is only picked when the army suits it: Embolon needs a large share of heavy cavalry,
Split Archers needs a good share of archers, Split Infantry needs an army that is mostly infantry, and Split
Skirmishers needs a strong infantry force with enough javelin men. Several base-game tactics, such as the
all-out charge and the defensive ring, are not used in field battles.

**Split Infantry** puts about a sixth of the infantry on each flank, filled first with men carrying two-handed
axes and polearms. **Split Skirmishers** puts up to a fifth of the infantry, the lower-tier javelin troops, in a
separate formation that skirmishes ahead of the main line. In **Split Archers**, the infantry waits for its
archers if they fall too far behind.

## How a battle unfolds

Most tactics have two phases.

**Advance.** The main infantry moves forward in good order. Archers stand just behind it and shoot over it,
cavalry guard the flanks, and javelin troops skirmish ahead.

**Battle joined.** The tactic switches to attack once the infantry lines are close (around 70 to 95 m depending
on the tactic), once a good part of the main infantry is already fighting in melee, or once the enemy has only
cavalry left. From then on the infantry charges, archers skirmish on their own, and cavalry charge.

## Infantry

**Marching in step.** An advancing AI formation moves at the pace of its slower men, so the line arrives
together instead of strung out. Only men who have fallen behind their place may run to catch up; men level
with or ahead of the line keep the formation's pace. A tidy line is allowed to move faster than a ragged one.
These pacing rules also apply to your own formations when you give them a Move order.

**Line shape.** While advancing, the AI keeps its infantry line wider than it is deep. Within 150 m of the
enemy, an AI infantry formation of 30 or more men picks its own arrangement: a **shield wall** if most men carry
a large shield, **loose** order if most carry two-handed weapons, otherwise a **line**.

**Bracing against cavalry.** When enemy cavalry charges an AI infantry formation that is not yet fighting, the
infantry stops, forms a shield wall and turns to face the horsemen.

**Javelin volleys.** When an AI infantry formation is busy throwing javelins at a nearby enemy, it holds its
ground (or steps back a little) to finish its volley before closing in.

**Shields up.** AI soldiers charging an enemy with a one-handed weapon and a shield raise their shield as they
close. In a shield wall, the front rank holds shields low toward the enemy and the men at the ends of the line
cover the flanks; in a square or circle, the outer rank holds its shields toward the enemy. Men carrying a bow,
crossbow or two-handed weapon are left out of this unless the formation is ordered to hold fire.

## The frontline

The **Frontline System** (on by default, setting *Frontline System*) changes what happens when two infantry lines
meet. Instead of every man pressing straight forward into a crowd, each soldier in a charging infantry or
archer formation looks at the allies and enemies directly around him and chooses to:

- **attack** the enemy in front of him,
- **step back** a little,
- **close ranks** with the nearest ally,
- **sidestep** left or right,
- **rest** to recover, if the posture system is on, his posture is low and no enemy is in front of him.

He keeps each decision for a short random time, so the line shifts and rotates naturally: tired and battered
men drop back, fresh men step forward, and gaps close. Shield-bearers prefer to close ranks, men with two-handed
weapons attack and sidestep more, banner bearers hang back, and wounded or exhausted men are less eager to
attack. This also applies to your own formations when they charge.

Formations of 25 men or fewer are left out (setting: *Frontline Min Formation Size*). Further settings adjust
how long each decision is held and how much each choice is favored.

**Leaving the line.** Some rules apply whether the frontline system is on or off:

- Infantry under a charge order turn to face their own opponent once he is within 20 m.
- AI archers draw a sidearm when an enemy is within about 2.5 m. While skirmishing, an archer who has had nothing
  to shoot at for a while near the enemy charges in.
- AI cavalry riders break ranks when several enemies are close, and a rider who loses his horse leaves the
  formation.
- In sieges, an AI soldier (not in a shield wall, square or circle) leaves his place to fight an enemy within
  a couple of meters.

## Archers

**Skirmishing.** Once the battle is joined, AI archers keep to an effective shooting distance. They close in
until they are in range, shoot, and pull back when the enemy gets too close, provided they have infantry to hide
behind. Each formation approaches from a slightly different angle. If the enemy has only cavalry left, the
archers stand and shoot. If an army is almost all archers, they simply charge.

**Before contact,** archers stand just behind their own infantry and shoot over it.

**Holding fire out of reach** (needs RBM Combat). AI bowmen, crossbowmen, slingers and javelin throwers do not
shoot at targets their missiles cannot physically reach.

**Opening range.** AI archers judge their range correctly and open fire at real bow range.

**Picking up ammunition.** AI soldiers no longer run out to collect arrows, bolts, stones or throwing weapons
when an enemy is near the item (within 25 m, or 45 m for an enemy rider), while they still have at least 15% of
their ammunition, or while they are drawing, shooting or reloading.

**Out of arrows.** In field battles, AI formations are re-sorted by what each man carries: an archer with 5 or
fewer arrows left joins the infantry, and a rider with a bow and ammunition joins the horse archers.

## Cavalry

**Repeated charges.** AI cavalry charges the enemy's infantry first, then its archers. Riders ride through the
enemy, carry on past, then reform and charge again. If they get stuck in melee for several seconds, they pull
out. They reform quickly, and cut the reform short if they come under fire or an enemy approaches. They are more
eager to charge downhill, and less eager to charge other cavalry.

**Flank guards.** Before contact, cavalry guards the flanks of the main infantry, out to the side and somewhat
forward. It charges any enemy that comes close, and returns to its post when it strays too far.

**Embolon** (Empire). Heavy cavalry forms a wedge in front of the advancing infantry before the battle is
joined.

**Horse archers** ride an oval loop around their target formation, shooting as they go. If they get close to
the map edge, they break off and reposition. Horse archers are much less likely to charge into melee.

**Mounted javelin troops** skirmish: they ride in, throw, ride back and repeat. In some tactics they are shared
evenly between the two flank formations.

**Charging into infantry.** When a horse slams into an enemy infantryman in a field battle, he may panic and flee
for a few seconds, more likely when he is knocked down or hit from behind. He rallies once nobody has hit him for
10 seconds. A charge that neither knocks the man down nor knocks him back does no damage and only pushes him
aside. Riding into your own men does no damage either. Riders surrounded by infantry keep pushing forward.

## Javelin skirmishers

Javelin troops on foot and on horse (in **Split Skirmishers**, for example) run a cycle: wait beside their own
infantry, dash forward to throwing range, throw for a few seconds, fall back, and repeat.

## Defending

A defending formation holds a position and freezes there, facing the enemy, once the enemy is within 200 m.
Defensive positions are kept away from the map edge.

## Reinforcements and rallying

**Spawn position** (field battles). Reinforcements arrive behind where their own army is now, instead of at the
spot fixed at deployment. RBM looks for a point on the map border behind the army, steps it inward, prefers a
spot well away from enemy formations, and keeps the men out of water. When the base game's own spawn point is
used, reinforcements appear 50 m in from the border instead of right on it.

**Field share.** Your battle size setting is split between the sides by the square root of each side's men
left, so the bigger army has more men on the field, but less than its full numbers advantage:

| Army sizes | Men on the field |
|---|---|
| 1:1 | 50:50 |
| 2:1 | 59:41 |
| 3:1 | 63:37 |
| 5:1 | 69:31 |
| 9:1 or more | 75:25 (the limit) |

A small army always gets at least as many places as the smaller of its own size and the battle size minus
its size. So an army that fits in half the battle size brings everyone (at battle size 1000, 400 men against
5000 start 400 vs 600), and the guarantee fades for slightly larger armies instead of cutting off.

The share is recalculated from the men each side has left (on the field and in reserve) at every reinforcement
wave, so it shifts as the armies take losses. A side that can't fill its share leaves the room to the other side.

**Synced waves.** When one side's reinforcement wave arrives, the other side's wave is brought in too. Each
wave brings its side back up to its field share, as long as it has men left in reserve.

**Rallying.** AI infantry whose men are scattered far ahead of or behind the formation stops to regroup before
pressing on, unless the enemy is close. After a reinforcement wave, the formation gathers at a point a little in
front of where the wave arrived, so the newcomers join up before advancing.

**Ending the battle.** When you win a field battle in the campaign, the defeated side's remaining troops are
counted as wounded, so the battle does not continue into a second round.

## How individual soldiers fight

**Skill decides.** An AI soldier's blocking, parrying, feinting and attack decisions depend on his melee skill.
His aim depends on his ranged skill: a skilled archer shoots more accurately, steadies his aim faster and shoots
more often. Accuracy suffers when shooting on the move. The **Combat AI** difficulty option in the game settings
scales the AI's skill. The setting *Vanilla AI Block/Parry/Attack* keeps the base game's values for melee
decisions instead.

**Shields against missiles.** Melee troops raise their shields against incoming missiles. Archers and other
ranged troops do not.

**Stamina.** With the posture and stamina systems on, a tired soldier moves, swings, reloads and reacts more
slowly, and aims worse. See [Posture & Stamina](posture.md).

**Weapon speed.** The speed bonus from weapon skill stops growing at 150 skill. Fighting on horseback does not
slow weapons down.

**Weather.** Rain and other weather no longer reduce bow and crossbow power or missile range.

**Spear or sidearm.** AI troops who carry both a polearm and a sidearm keep the polearm until an enemy stays
within about 2.5 m for a couple of seconds, then switch to the sidearm. They go back to the polearm once there
has been no enemy close for a few seconds. They no longer switch back and forth constantly.

**Two-handed specialists.** Some troops always fight with their two-handed weapon: Imperial cataphracts and
heavy horsemen, Khuzait lancers, druzhinniks and Aserai faris keep the shield slung on their back and only
draw shield and sidearm at close quarters. Battanian falxmen, Sturgian shock troops and berserkers and several
Nord troops never use a shield, and only throw their throwing weapons at targets between 6 and 25 m away. This
only affects the AI; you can still use every grip of the same weapons.

**Re-arming.** An AI soldier who has lost his melee weapon looks for one on the ground within 15 m.

**Banner bearers** stop swinging at enemies who are out of reach.

## Kicks and bashes

With **AI Kick and Bash** on (the default), AI soldiers kick, shield bash and weapon bash. The base game's AI never
kicks.

**When the AI tries.** Both fighters must be on foot, and the enemy close (about 1.4 m) and in front. The AI
looks for an opening: an enemy who is blocking, holding his weapon ready, turning his back, or staggered.
Without an opening, only a more skilled fighter will try. Attempts are more likely from behind and against a
staggered enemy, and there is a pause of several seconds between attempts. Against an enemy holding a shield
toward him, the AI kicks rather than bashes. Any AI soldier can kick or bash, whatever weapon he holds, but
never while bracing a polearm.

**Skill.** Kicks and bashes use a mix of Athletics and the skill of the weapon in hand. The more skilled fighter
tries more often and knocks down more often.

**Cost.** Every kick or bash, the player's included, costs the attacker posture and stamina (if those systems
are on), hit or miss. Higher skill makes it slightly cheaper.

**Damage** (needs RBM Combat). Kicks and bashes deal real blunt damage based on the weight of the boot, shield or
weapon, and heavier boots kick harder. A bash blocked by a shield, or a kick into a raised shield, does no real
damage. Kicks and bashes do not wear down armor. Because the victim also loses posture and stamina in
proportion to the damage, a kick or bash can break his posture.

**Knockdown.** A kick or bash that lands knocks the victim down for certain if he is already staggered, or if it
breaks his posture. Otherwise there is a chance to knock him down that is higher for a kick, from behind, and
against a tired man, and lower against a man in heavy armor. These rules also apply to your own kicks and
bashes.

## Sieges

- **Attackers** on the walls spread out and fire at will. Instead of walking along the wall to the gate, they
  charge a nearby enemy formation, or one that is shooting them up.
- **Shields.** Attackers with a shield and a one-handed weapon keep their shield raised while not attacking.
- **Archers on the walls** spread out along a wider front and keep shooting at men below the wall. They only draw
  a sidearm when an enemy is within about 3 m.
- **Archer positions.** For many castles and towns, RBM ships its own set of defender archer positions on the
  walls, replacing the base game's.
- **Defender morale.** Defenders do not lose morale when their comrades fall. With the setting *Keep Battle (Last
  Stand)* on, a heavily outnumbered garrison that is down to its last men loses heart quickly, so the survivors
  can fall back to the keep.
- **Siege weapons.** Defenders keep manning their siege engines, and no side stops using its ranged siege weapons.

## Your own orders

- **Charge.** A plain Charge order sends a formation at the nearest sizeable enemy formation instead of at
  whoever is closest. If the formation's target disappears, it charges the next nearest one.
- **Advance** stops infantry a few meters short of the enemy. Archers ordered to advance stop at a sensible
  shooting distance instead of at the edge of their maximum range.
- **Mounted archers** under your Move or Stop order hold their places instead of breaking out at nearby enemies.
- RBM's automatic arrangements and its charge, regroup and advance adjustments do not override formations you
  control.
- There is no separate General's bodyguard formation; the general fights in the army's normal formations.

## On screen

- **Formation markers** use icons for infantry, archers, cavalry, horse archers and javelin cavalry, and follow
  the formation smoothly instead of jumping from soldier to soldier.
- **Slow motion** (setting *Slow Motion in Combat*). When you kill or knock out an enemy, or break his posture,
  the game slows to a quarter speed for three quarters of a second. Needs RBM AI on.
- **Fast forward.** Press **Ctrl+V** in battle to toggle fast-forward.
- **Frontline overlay.** With the frontline system on, **Ctrl+Shift+F** toggles a debug overlay that shows the
  soldiers' frontline decisions.
- **Battle stats.** With *Developer Mode* on, a text overlay in the bottom-left corner shows damage by troop type,
  kick and bash attempts, melee hits, and a counter for each kind of block and parry (chamber, weapon block or
  parry, shield block or parry, wrong-side shield). It is meant for testing, not normal play.

## AI log

With *AI Behavior Logging* on, RBM writes each battle's tactics, formation behaviors and orders, second by
second, to `Documents\Mount and Blade II Bannerlord\Configs\RBM\logs\ai\`. Only the newest 10 logs are kept.
