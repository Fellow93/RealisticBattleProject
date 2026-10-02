# Ranged Combat

RBM treats bows, crossbows, slings and thrown weapons as physical launchers. A missile's speed comes from the launcher and the missile's weight, and its damage comes from the energy it carries when it hits. It then meets armor under the same penetration and blunt trauma rules as melee (see [Combat & Armor](combat.md)). Everything on this page needs the **RBM Combat** switch on unless stated otherwise (see the [Settings Reference](../settings.md)).

## Launch speed

| Weapon | What sets the launch speed |
|---|---|
| **Bows** | The bow's draw weight and the weight of the arrow. Composite bows turn draw weight into speed a little more efficiently than longbows. |
| **Crossbows** | The crossbow's draw weight and the weight of the bolt. |
| **Slings** | The sling, the stone's weight and your skill. Shoulder and arm armor above a light level slows the throw, and so does a shield on your arm: a large shield costs 13%, a small one 4%. |
| **Javelins, throwing axes, throwing knives** | The weapon's weight and your skill, with the same armor and shield penalties as slings. |
| **Thrown stones** | A flat speed. |

Some general rules:

- **Heavier arrows and bolts** leave the string slower but carry more of the bow's energy.
- **Item modifiers** on a bow or crossbow change its draw weight.
- **Your own movement adds** to the launch speed when you shoot a bow, crossbow or sling on the move.
- With **RBM AI** on, the base game's weather penalties are removed: rain and snow no longer slow bows and crossbows, and fog no longer shortens missile range.

## Missile damage

A missile hits with the kinetic energy it has at impact. That uses its speed relative to the target, so shooting at a man running toward you hits harder. For arrows and bolts, the arrowhead then scales that energy. The result meets armor like any blow:

- Arrows and bolts face only **half** of mail, leather or cloth armor. Plate counts in full.
- An arrow or bolt that does no penetrating damage at all doesn't stick in the target. Most of the time (70%) it **shatters** on the armor, otherwise it **glances off**.

Bow and crossbow tooltips show their **draw weight**, the **ideal ammo weight** in grams and the resulting **initial missile speed**, plus *Missile Damage Pierce* and *Missile Damage Cut* tables. Throwing weapons show your skill, their launch speed and a *Missile Damage* table. Hover over a table to see damage, penetrating damage and blunt trauma against armor from 0 to 100.

## Drawing and reloading

**Ranged reload speed** sets how fast bows and crossbows are reloaded:

| Option | Effect |
|---|---|
| Vanilla | The base game's speeds |
| Realistic | Slow, above all for unskilled shooters. Also slows the bow draw for everyone. |
| Semi-realistic (default) | Much faster at low skill than Realistic. Also slows the bow draw. |

The reload part of this setting only affects you, unless **Ranged reload applies to AI** is on. Otherwise AI archers and crossbowmen keep their own fixed reload. Reload perks (such as Rapid Fire and Wind Winder) still apply on top. With the [stamina system](posture.md) on, a tired shooter reloads more slowly and aims less steadily.

## Aiming

**Realistic Arrow Arc** (default off): your bow and crossbow shots leave about 5 degrees above where you aim, so they fly a higher arc and the crosshair no longer marks where they land. This affects only you, and only bows and crossbows.

**Ranged aim arc** (experimental, default off). While you draw a bow or crossbow, wind up a sling, or ready a javelin, throwing axe, throwing knife or stone, a dotted line shows the predicted flight with a marker where the missile will land:

- The prediction uses the same launch speed and air drag as the real shot (and the Realistic Arrow Arc tilt, if that is on).
- The marker turns **red** when the arc would hit an enemy and **green** when it would hit a friend.
- A ring of dots on the ground around the landing point shows your current aiming spread. It shrinks as your aim settles.
- In third person, aiming up also lifts the camera and tilts it down so the landing point of a high shot stays on screen. Where you aim is unchanged, and the crosshair is hidden while the camera is moved. The camera frames where the shot would come down on the ground, so in a siege, sweeping your aim across walls, ladders and towers no longer makes it jump.

!!! note
    The aim arc and aim camera are player-only aids and are still experimental.

## Missiles and shields

- **Wooden shields.** Most arrows and bolts stick. About 15% shatter and about 5% glance off.
- **Metal shields.** Nothing goes through and nothing sticks: missiles bounce off. Arrows and bolts shatter 80% of the time. The shield still takes the hit's damage.
- **Shields on the back** block missiles like a raised shield and take the damage. Missiles that can pierce shields (or a thrower with the Impale perk) go through a wooden shield on the back into the man carrying it.
- **Pila punching through.** A pilum (a javelin with a bonus against shields) that hits a raised wooden shield sticks in it, and its iron shank can come out the back and wound the man behind. Thicker shields stop more: a shield with 18 armor or more stops it completely. Metal shields stop it every time.
- **Shield damage.** Javelins and throwing axes wreck shields far faster than arrows and bolts. A shield on the back that is shot to pieces is destroyed.

## Stuck missiles fall out

Missiles stuck in men and shields don't stay there forever. They work loose and drop to the ground, where they can be picked up again.

| Missile | Behavior | Setting |
|---|---|---|
| Javelins, throwing axes, throwing knives in a shield or a living body | Fall out after about the set time | **Stuck javelins fall out after (s)**, default 5 |
| Arrows and bolts in a shield | Fall out after about the set time. A shield holds at most 8: one more and the oldest drops at once. | **Arrows in shields fall out after (s)**, default 45 |
| Arrows and bolts in a body (man or horse) | No timer. A body holds at most 4: one more and the oldest drops. | Same setting as above |

Each missile gets its own time, up to 40% shorter or longer, so a volley doesn't fall out all at once. Setting either option to 0 leaves that kind stuck for good (and the arrow limits off), as in the base game. Missiles in a dropped shield or a corpse are left alone.

## Arrow visuals

**Better Arrow Visuals** (default on) draws arrows and bolts in flight with their real model instead of the base game's thin streak. At true size they are harder to follow, so the experimental **Flying arrow thickness** slider can make them thicker while they fly. Only the thickness changes, not the length, and arrows go back to true size the moment they land. Both are visual only. Hits are unchanged.

## Siege engines and fire

- **Ballistas** fire their bolt at 60 m/s with a small random spread.
- **Trebuchets** fire a spread of seven grapeshot projectiles instead of a single one.
- **Mangonels** throw faster the higher their release angle.
- **Fire pots** (hand-thrown, siege and naval) only do full damage near the center of the splash. Damage then falls off steeply to nothing at the edge, so one pot no longer burns a whole file of men.
- In sieges, AI defenders with javelins or throwing axes are resupplied when they are down to their last two.

## Posture and stamina

With the [Posture & Stamina](posture.md) systems on, shooting and throwing cost posture and stamina. Bows cost the most posture per shot, crossbows very little, and thrown weapons are in between. Skill reduces both costs. Crossbows pay their stamina cost when you finish spanning them, not when you shoot. Being hit by missiles also drains posture, and a big enough hit can stagger you. Details are on the posture page.
