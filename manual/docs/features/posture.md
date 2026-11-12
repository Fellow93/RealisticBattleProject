# Posture & Stamina

Every soldier on the field, you included, has two hidden meters on top of health:

- **Posture** is how well he keeps his guard and footing. Blocks, parries and hits wear it down, and it recovers over time. When it runs out his guard breaks: he can be staggered, drop his weapon or shield, be knocked off his horse, or take damage that crushes through his block.
- **Stamina** is how fresh he is. Attacking, blocking, shooting and being hit drain it. A tired soldier hits weaker, moves and reloads slower, and loses posture faster.

## Settings

These settings are in the **RBM Stance** section, with its own on/off switch. They work with RBM AI on or off.

| Setting | Default | Notes |
|---|---|---|
| **Posture System** | On | Off also turns off the Stamina System and Posture GUI. |
| **Stamina System** | On | Needs the Posture System. |
| **Posture GUI** | On | Shows the bars described in [What you see](#what-you-see). |
| **Large Vertical Bars** | Off | Shows your posture and stamina as two large vertical bars at the right edge of the screen instead of the small bars. Needs Posture GUI. |
| **Player Posture Multiplier** | 1x | 1x, 1.5x or 2x your own maximum posture and its recovery (and stamina, with the Stamina System on). AI soldiers are unaffected. |
| **AI Kick and Bash** | On | Kicks and bashes cost posture and stamina and can knock a man down. |

See the [Settings Reference](../settings.md) for the full list.

## Posture

### How much you have

Maximum posture is built from your skills:

- your skill with the weapon in your hand counts the most;
- **Athletics** on foot, or **Riding** on horseback, adds a smaller part;
- carrying more armor adds a little: each kilogram of armor over 5 kg adds one point of posture.

Switching weapons recalculates your maximum, but you keep the same share of it you had before. Posture recovers on its own over time, also faster with higher skills. It stops recovering for about a second each time you lose posture in a fight, so it doesn't refill in the middle of an exchange. If you haven't attacked, been attacked, shot or been shot for 10 seconds, it recovers **three times as fast**.

### What drains it

Every melee exchange costs posture on **both** sides: the defender pays for stopping the blow, and the attacker pays for having it stopped.

| Outcome | Defender | Attacker |
|---|---|---|
| **Weapon block** | Loses posture. On a break he is staggered, damage crushes through, and he may drop his weapon. | Loses posture. On a break he is staggered and may drop his weapon. |
| **Weapon parry** (perfect block) | Loses a quarter of what a block costs, and gets a little back if he holds. On a break he is staggered and half the crush-through damage gets through, but he keeps his weapon and his saddle. | Loses more than against a block. Same break effects as a block. |
| **Shield block** (correct side) | Loses posture. On a break he is staggered and may drop his shield. | Loses posture. On a break he is staggered. |
| **Shield block** (wrong side) | Loses posture. On a break he is staggered and may drop his shield. | Loses posture. On a break he has to catch his breath. |
| **Shield parry** | Loses half of what a block costs, and gets a little back if he holds. On a break he is staggered, but keeps his shield. | Loses more. On a break he is staggered and may drop his weapon. |
| **Clean hit** | Loses posture equal to the damage he took. No stagger on its own. | Loses very little (a fifth of a blocked blow's cost). On a break he has to catch his breath. |
| **Chamber block** | Both lose posture: the chambering man little, the attacker a lot. Whoever breaks is staggered. | |

How much a blow drains depends on:

- **The weapons on both sides.** Every weapon class has its own posture profile. A two-handed sword drains far more from a blocker than a one-handed sword, and shields, large ones above all, are good at absorbing blows. In every class, a parry costs the defender less than a block and sends more back to the attacker. The heavier the weapon, the more it costs its wielder to have it blocked.
- **Skill against skill.** The attacker's weapon skill plus Athletics (Riding on horseback) is weighed against the defender's. Holding a shield counts as extra skill for the defender. Skill matters less than the weapons.
- **Closing speed.** The faster the two men move toward each other, the harder the impact. Charges hit posture hard.
- **Where the weapon connects.** The closer the point of contact is to the weapon's center of mass, the more the blow drains.
- **Perks.** The base game's perks for weapon handling and shield protection reduce the posture you lose when blocking.
- **Stamina.** An exhausted man loses up to 25% more posture, both attacking and defending.

### When posture breaks

- **Stagger.** The man reels back and can't act for a moment.
- **Crush-through.** When a weapon block or parry breaks, part of the blow gets through as damage. You get a message like "Posture break: Posture depleted, 12 damage crushed through". The damage depends on the attacking weapon and the defender's overall armor, and is smaller if the block only just broke.
- **Disarm.** If the breaking blow overshoots by at least half the man's maximum posture, he may drop his weapon (only if he has another melee weapon to fall back on) or his shield.
- **Dismount.** A rider is knocked out of the saddle when the blow that breaks his posture alone dealt at least a third of his maximum. Chip damage that happens to empty the bar is not enough.
- **Recovery.** After a break the man gets 75% of his maximum posture back at once, also when a clean hit emptied it. Only an attacker who breaks on his own clean hit gets just a third back.

### Missiles and posture

Being hit by an arrow, bolt, throwing knife, javelin or throwing axe costs posture equal to the damage it does, so hits that armor stops cost little and hits to the head cost more. A missile caught on your raised shield costs only a little posture and stamina: at most 2 posture for an arrow, bolt or sling stone and 6 for a thrown weapon, less with the Arrow Catcher perk. A shield on your back costs nothing.

If a missile empties your posture you are staggered, and your posture refills to 75%. Shooting and throwing cost posture too. Bows cost the most per shot, crossbows very little, and thrown weapons are in between. Skill reduces the cost.

## Stamina

### How much you have

Maximum stamina and its recovery come from **Athletics**. A helmet with heavy face protection (30 face armor or more) **halves** your stamina recovery. Stamina recovers faster the emptier it is. Below 70%, it recovers three times as fast once you have been out of combat for 10 seconds.

### What drains it

- Attacking, blocking and parrying. Two-handed weapons cost a little more, and skill reduces the cost slightly.
- Being hit, more the harder the hit.
- **Heavy armor.** Every action costs more stamina the heavier your armor. Athletics offsets part of that weight.
- Shooting bows and throwing weapons. Higher skill (above the weapon's difficulty for bows and crossbows) makes it cheaper. Crossbows pay when you finish spanning them, not when you shoot.
- Being hit by missiles.
- Throwing a kick or bash, whether it lands or not.

### What tiredness does

Effects grow gradually as stamina falls. At **zero stamina**:

| Effect | At zero stamina |
|---|---|
| Damage of your blows | 85% |
| Movement speed | 85% |
| Swing and thrust speed | 85% |
| Weapon handling and shield block speed | 50% |
| Reload speed | 80% |
| Ranged accuracy | Worse spread while moving and unsteady; longer time to steady your aim |
| Posture lost when attacking or defending | Up to 125% |

The good side of being fresh:

- Above 70% stamina your armor counts for more, up to 30% more at full stamina. Below that it counts at its normal value.
- Above 85% stamina you slowly regain health: 0.9 HP every 10 seconds.

AI soldiers also attack and react less sharply when tired.

## Kicks and bashes

With **AI Kick and Bash** on, AI soldiers kick, shield bash and weapon bash, and your own kicks and bashes follow the same rules:

- Each kick or bash costs posture and stamina as it starts, hit or miss. Higher skill makes it cheaper. Kicks and bashes use mostly Athletics with a smaller share of your weapon skill.
- A raised shield stops a bash but not a kick.
- One that lands deals damage and drains the victim's posture and stamina like any blow (see [Combat & Armor](combat.md)). One caught on a weapon or shield block rocks the blocker: it drains his posture like a heavy blow.
- It **knocks an enemy on foot down** for certain if he is still reeling from a posture break or posture tiredness, or has his posture emptied by it. Otherwise it's a roll against relative skill. The chance is higher from behind, against a man kicked or bashed moments before, against a tired man, and for a kick rather than a bash, and lower the heavier his armor. He falls where he stands instead of flying back. A man already down is not knocked down again.

## What you see

With **Posture GUI** on:

- **Your bars.** Your posture bar, plus your stamina bar with the Stamina System on, are shown during battle, as small bars or, with **Large Vertical Bars** on, as two large bars at the right edge of the screen.
- **Your opponent.** For a few seconds after you trade blows with someone, his name, health, posture and stamina are shown at the top of the screen. The panel goes away a few seconds after you kill or knock him out.
- **Messages.** Posture breaks are reported in the message log, including any crush-through damage.

With **Slow Motion in Combat** on (default), breaking an enemy's posture briefly slows the battle, as killing or knocking out an enemy does.

## Other details

- A horse killed while running at speed in a field battle staggers the foot soldiers in its path.
- In tournaments, everyone's posture is reset between matches.
- Missile and melee hits from your own side don't touch posture.
- With RBM AI on as well, skilled AI fighters parry more often.
