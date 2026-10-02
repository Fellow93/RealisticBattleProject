# Combat & Armor

RBM replaces the base game's damage formula with one built around how armor really works. Armor stops a blow outright up to a threshold. Anything beyond that cuts or stabs through, and the part the armor stopped still hurts you as **blunt trauma**. Everything on this page needs the **RBM Combat** switch on (see the [Settings Reference](../settings.md)).

## How a blow does damage

Every hit is worked out in three steps:

1. **Strength of the blow.** This comes from the weapon's physics (weight, balance, length, how fast it moves) and from your skill (see [Skill](#skill-and-weapon-speed)).
2. **Penetration.** The armor on the body part you hit stops the blow up to a threshold: the armor value times a factor that depends on the weapon type and on whether the blow cuts, stabs or crushes. Whatever is above the threshold goes straight through as **penetrating damage**.
3. **Blunt trauma.** The part the armor stopped is not wasted. A share of it, set by the weapon type, comes through as blunt trauma, and is then softened again by the armor (the more armor, the less trauma gets through).

So a strong blow against light armor mostly penetrates. A weak blow against heavy armor mostly turns into a little blunt trauma. Heavy armor makes you hard to kill, but not invulnerable.

### Weapon types

The threshold factor tells you how much armor resists each weapon. A **lower** number means the weapon gets through armor more easily. The blunt share is how much of the stopped part still reaches you as trauma.

| Weapon type | Cut: armor resistance | Cut: blunt share | Thrust: armor resistance | Thrust: blunt share |
|---|---|---|---|---|
| Dagger | 5 | 25% | 3 | 35% |
| One-handed / two-handed sword | 5 | 25% | 3.5 | 35% |
| One-handed axe | 5 | 30% | 2.5 | 25% |
| Two-handed axe | 5 | 30% | 2.5 | 30% |
| One-handed / two-handed polearm | 5 | 30% | 3 | 35% |
| Mace / two-handed mace | 4 | 10% | 4 | 25% |
| Arrow, bolt | 2.6 | 15% | 2 | 15% |
| Javelin | 3 | 5% | 3 | 20% |
| Throwing axe | 4 | 30% | 2.5 | 20% |
| Throwing knife | 5 | 15% | 3 | 15% |
| Sling stone | 10 | 50% | 6 | 60% |

**Blunt** blows (maces, hammers, the flat of a weapon) work differently. Armor stops them up to five times its value, and 70% of the stopped part comes through as trauma. That is why blunt weapons stay useful against heavily armored targets.

Other rules that change the threshold:

- A weapon with a higher damage factor (shown in its tooltip) lowers the threshold, so better weapons punch through more armor.
- **Arrows and bolts against mail, leather or cloth** face only half the armor. Plate gets its full value.
- **Javelins with a bonus against shields** (pila) have a much lower threshold.

The overall armor curve can be tuned with the **Armor Effectiveness**, **Armor Multiplier**, **Blunt Trauma Multiplier** and **Armor Penetration Threshold** sliders. To see the split for every blow you deal or take, turn on **Armor Penetration Messages**. Both are in the [Settings Reference](../settings.md).

## Skill and weapon speed

- **Swings** draw most of their power from your weapon skill, kept between a minimum and a maximum for each weapon class. A skilled fighter hits much harder with the same weapon. Skill has diminishing returns at high levels.
- **Thrusts** come mostly from the weapon's weight and speed. Couched and braced lances depend on the horse's speed, the lance's weight and your Polearm skill, and are capped where a lance would break.
- Your weapon's **swing speed, thrust speed and handling** are recalculated from its physical properties and your skill, so the same weapon moves faster in skilled hands.
- **Thrust Modifier** is a balance setting for how the AI chooses between thrusting and swinging. RBM undoes it when it works out thrust damage, so stabs hit about as hard at any setting.

## Where the blow lands

| Body part | Cut | Pierce | Blunt |
|---|---|---|---|
| Head, neck | 150% | 150% | 150% |
| Chest | 90% | 90% | 90% |
| Abdomen | 100% | 100% | 100% |
| Shoulders | 60% | 60% | 70% |
| Arms, legs | 60% | 50% | 70% |

Special hits:

- **Face hit.** A head hit from the front that lands on the face uses the helmet's *face armor*. Closed plate helmets with a mail face count as mail there. Without a helmet the face has almost no protection. You see a "Face hit!" message.
- **Under-shoulder hit.** A hit into the armpit, below the shoulder plate, uses the body armor's arm protection (plus the cape's). Plate armor counts as mail there, except a few brigandines, coats of plates and scale armors that stay plate below the shoulder. You see an "Under shoulder hit!" message.
- **Edge or handle.** A swing that lands with the haft, hilt or pommel instead of the blade does blunt damage, and you see a "Handle hit" or "Pommel hit" message. A sword thrust that doesn't land with the tip cuts instead of stabbing. A spear or other polearm thrust that hits with the shaft instead of the head does only a fifth of its damage.
- **A man on the ground.** A victim who is falling or knocked back gets only 75% of his armor.

## Armor coverage

Each body part takes its armor from specific pieces:

| Body part | Protected by |
|---|---|
| Head | Helmet (face hits use the helmet's face armor) |
| Neck | Body armor's arm value |
| Shoulders | Cape or pauldrons plus the body armor's arm value |
| Chest, abdomen | Body armor |
| Arms | Gloves |
| Legs | Half of the boots' value plus half of the body armor's leg value |

In the inventory, hover over your armor values to see this breakdown: head, face and neck armor; shoulder, chest and abdomen armor; arm and lower-shoulder armor; and leg armor.

**Item modifiers** (such as Thick or Rusty) change armor and weapon damage by a **percentage** instead of a flat amount, so they matter as much on light gear as on heavy gear.

### Armor wears down during battle

A weapon hit that does damage has a chance to knock the armor piece it hit down one quality step. The chance starts at 5% and grows with how much damage the armor absorbed. It also depends on weapon and material: sword and dagger cuts wear cloth and leather fastest, and blunt blows wear plate far faster than anything else. Kicks, bashes and punches never wear armor. The wear only lasts for that battle.

### Armor status display

With **Armor Status GUI** on (default), six small icons in the lower right of the battle screen show the condition of your head, shoulder, body, hand, leg and horse armor:

| Color | Condition |
|---|---|
| Green | Better than standard quality |
| Grey | Standard quality |
| Light orange, orange, dark orange | Worn: 90%+, 80%+, 70%+ of standard |
| Red | Below 70%, or no armor in that slot |

## Shields

Shields take damage by weapon type:

- **Axes and two-handed polearms** chop shields apart faster.
- **Thrusts** barely damage a shield.
- **Blunt blows** do a bit less.
- Weapons with a **bonus against shields** do double.
- **Javelins and throwing axes** hit shields very hard. Arrows and bolts do far less.

A blow that hits a shield slung on someone's back is treated as blocked by it. For missiles and shields, see [Ranged Combat](ranged.md).

## Blows and reactions

- A **couched lance or braced spear** that hits an enemy knocks him down. If he blocks it, he is knocked back.
- An **axe, two-handed polearm or two-handed mace** blocked by a weapon, or by a shield held on the wrong side, still jolts the defender when its blade lands.
- Weapons **stick** in or **bounce** off a body depending on the armor material and the damage done. A killing cut to the neck, arms or legs slices through.
- **Friendly hits** never knock back or knock down your own side.
- **Punching** someone hurts your own hand a little, softened by your gloves. It can never kill you.
- **Sneak attacks** apply the base game's bonus to the blow before armor, so a knife in the back still gets through RBM's armor. With **Sneak Attack Insta-Kill** on, a sneak attack deals a flat 200 damage instead.

## Kicks, bashes and punches

With **AI Kick and Bash** on (default), kicks, shield bashes and weapon bashes, yours included, do real blunt damage instead of the base game's token amount. Damage is based on Athletics with a smaller share of your weapon skill, plus what you strike with: boots for a kick, the shield's weight for a shield bash, the weapon's weight for a pommel or haft shove. A kick lands harder than a punch thrown with the same skill. A bash blocked by a shield, or a kick into a raised shield, does no damage. Kicks and bashes also cost posture and stamina and can knock a man down. See [Posture & Stamina](posture.md).

Punches work the same way, based on Athletics and your gloves (heavier, harder gloves hit harder).

## Stamina and armor

With the [stamina system](posture.md) on, a tired fighter hits up to 15% weaker. A fresh one wears his armor at its best: above 70% stamina, armor counts for up to 30% more (at full stamina).

## Horses

- **Speed** depends on your Riding skill compared with the horse's difficulty (up to 10% faster or slower), the total weight of horse, rider and gear, and the conditions. Rain and night (outdoors) each cost 10%. A horse without a harness loses 10% of its speed stat.
- **Charge damage** comes from momentum: speed times the combined weight of horse, rider, armor and weapons. A charging horse also takes some damage itself, reduced by its chest armor.
- **Rearing.** A moving horse never rears. A standing or slow horse rears only when hit from the front, by a polearm thrust or by a missile that does enough damage.
- **Dismounting.** A strong couched or braced lance hit to a rider's head, neck, chest or shoulders can unseat him. Higher Riding skill resists it.

## Weapon and item tooltips

Item tooltips get an **RBM Stats** section, calculated for the selected character:

- **Melee weapons:** relevant skill, swing and thrust damage factor, swing and thrust speed in m/s, the swing's sweet spot, and **Swing Damage** / **Thrust Damage** tables. Hover over a table to see damage, penetrating damage and blunt trauma against armor values from 0 to 100.
- **Bows and crossbows:** draw weight, ideal arrow weight, launch speed, and damage tables. See [Ranged Combat](ranged.md).
- **Throwing weapons:** relevant skill, launch speed, and a damage table.
- **Shields:** shield armor.

## Other changes

- **Destructible objects** (gates, siege engines) take reduced damage from cuts and even less from thrusts and missiles. Fire does five times the damage to flammable objects.
- **Slow Motion in Combat** (default on, needs RBM AI) briefly slows the battle when you kill or knock out an enemy or break his posture. See [Posture & Stamina](posture.md).
