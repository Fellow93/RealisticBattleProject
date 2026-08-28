# Detailed Auto Resolve

How RBM decides a battle fought on the campaign map, and how that differs from vanilla.

Everything here lives in `RBMCampaign/Simulation/`. It is gated by `SimulationEquipmentEnabled`; set that to `0` and Bannerlord's own auto-resolve comes back exactly as it was.

---

## 1. What vanilla does

A map battle never becomes a mission. `MapEvent.Update()` drives a loop:

```
SimulateBattleRoundInternal()
  └─ CombatSimulationModel.GetSimulationTicksForBattleRound(mapEvent)   ← once per round
  └─ SimulateBattleRound(defenderTicks, attackerTicks)
       └─ TickBattleSimulation(side)                                    ← once per tick
            └─ SimulateSingleTroopHit(side)
                 ├─ side.SelectRandomSimulationTroop()   ← picks ONE soldier
                 ├─ CombatSimulationModel.SimulateHit(strikerTroop, struckTroop, ...)
                 └─ ApplySimulationDamageToSelectedTroop(damage)
```

A **tick is one blow**: one named soldier striking one named soldier. The number of blows in a round is roughly `pow(menOnThatSide, 0.6)` — about 7 a round for 30 men, about 45 for 600. A blow that rolls above the victim's hit points puts him down; the surgeon decides afterwards whether he is dead or merely wounded.

And the blow itself, in `DefaultCombatSimulationModel.SimulateHit`, is this:

```csharp
damage = (0.5 + 0.5*rand) * 40 * pow(strikerPower / struckPower, 0.7) * advantage
damage *= morale factor
```

where

```csharp
GetTroopPower(troop, side, context, leaderModifier)
    = GetDefaultTroopPower(troop) * (1 + leaderModifier + contextModifier)

GetDefaultTroopPower(troop):
    t = troop.IsHero ? (Level/4 + 1) : troop.Tier
    p = (2 + t) * (10 + t) * 0.02
    if (IsHero) p *= 1.5
```

**That is the whole of vanilla's model of a soldier: his tier.** Not his armour, not his weapon, not his shield, not his training. A tier-3 Imperial Archer and a tier-3 Imperial Infantryman are, to this equation, the same man. The only place vanilla compares the four arms at all is `contextModifier` — a table in `DefaultMilitaryPowerModel` keyed on `(troop type | terrain | side)` that makes cavalry worth about a quarter more on open ground and archers worth less in a wood.

Note carefully: RBM Combat *does* patch `CharacterObject.GetPowerImp` with a different curve, but auto-resolve does not go through it. It goes through `MilitaryPowerModel.GetDefaultTroopPower`, which nothing patches. The formula above is what a simulated blow is really priced on.

---

## 2. What RBM does instead

A Harmony postfix on `SimulateHit` multiplies vanilla's number by a **correction**. There are two ways to compute it, and `SimulationAbsoluteDamage` picks between them.

**ABSOLUTE — the default.** The blow is worth its own real magnitude, and is not a ratio to anything:

```
correction = ( scale · actual ) / ( 40 · tierTerm )

actual    = damage THIS soldier's kit does to THAT soldier's armour
tierTerm  = pow( VanillaTierPower(striker) / VanillaTierPower(struck), 0.7 )
scale     = SimulationAbsoluteScale
40        = vanilla's own base scale, cancelled
```

Dividing by `40 · tierTerm` cancels vanilla's base scale *and* its tier-power core and puts `actual` in their place. What is left of vanilla's number — side advantage, the leader/captain modifier, every Tactics and Scouting perk, and its own random spread — rides through the multiply untouched, which is how "keep all of vanilla's factors" and "absolute damage" hold at once.

There is no `0.1 … 8` clamp on this path: there is no ratio to clamp, and an absolute mismatch is *meant* to be lopsided. The upper end is bounded per blow instead, against the struck man's own hit points (`SimulationAbsoluteBlowCap`, default 1.5× his pool) — taken in the postfix, where the man being struck is actually known. `scale` is the sole calibration dial of this path: it sets how a blow's real magnitude maps onto the pool the casualty stage wears down. Vanilla's fixed 40 set that scale for free; absolute mode owns it, and it is tuned against a paired log.

**RATIO — the fallback** (`SimulationAbsoluteDamage = 0`), the model's original shape:

```
                    ⎛  actual / baseline  ⎞ weight
correction = clamp  ⎜  ─────────────────  ⎟          , 0.1 … 8
                    ⎝      tierTerm       ⎠

baseline  = damage a TYPICAL soldier of the striker's arm does to a TYPICAL
            soldier of the victim's arm      (measured, not guessed — see §8)
weight    = SimulationEquipmentPowerWeight
```

Note that `SimulationEquipmentPowerWeight` is the exponent **only on this path**. In absolute mode it is nothing but the on/off gate: `SimulationEnabled` is `simulationEquipmentEnabled && simulationEquipmentPowerWeight > 0`, so a weight of zero switches the whole overhaul off exactly as the toggle does. The baselines still matter on both paths regardless — they are what §8 and the matchup tables are about — but only the ratio path *divides* by one.

**Tier is replaced, not adjusted**, either way. This is deliberate: a tier was only ever shorthand for *what kit does he carry and how well is he trained*, and both of those are now measured directly. Leaving vanilla's tier term in would charge for the same thing twice — and it was the reason a recruit in mail could not out-fight a looter in rags by more than the 1.41× his tier number allowed.

What survives untouched is the `leaderModifier` half of `(1 + leaderModifier + contextModifier)`. So **the captain's perks and the leader's Tactics still apply exactly as before.** Morale and routing are a different matter now, and are their own sections — see §6b and §6c.

**And the whole of it hangs on one switch.** `SimulationEnabled` is not merely "price blows by kit": every part of this document is gated on it. With it off, vanilla's morale multiplies blows again (§6b), the flat hundred-point lottery decides every man (§6a), selection goes back to arm-blind random (§5a), the rout never fires (§6c), and the player is spared from his own auto-resolve (§7). Off means off — what you get is the battle Bannerlord would have fought without RBM installed.

The `contextModifier` — the `(troop type | terrain | side)` table — is a different matter, and it does **not** simply cancel: the striker and the struck are different arms on different sides, so their context terms differ and both ride into vanilla's ratio. On a **field** battle that terrain-vs-arm bonus is now deliberately **lifted back out** — the postfix recomputes the ratio with the context zeroed on both sides and folds the difference into the blow's correction. An arm's edge is meant to come from its horse and its lance, both already priced in the equipment ratio, not from the ground it stands on. A **siege** keeps its full vanilla context (the wall is its own fact), and the leader modifier, being no kind of terrain, is kept everywhere. See `GetTerrainNeutralizingFactor`.

---

## 3. RBM Combat ON vs OFF — the central difference

`rbmCombatEnabled` picks **which combat model the auto-resolve is faithful to**, so the map battle stays consistent with the battle you would actually have fought had you pressed the button. It changes two things: how a blow's force is computed, and how armour answers it.

**And it changes the items themselves.** `XmlLoadingPatches` gates the XML merge: every file tagged `RBM_COMBAT_XML_TAG` — the weapons, armours, shields, horses and ranged data — is **not merged at all** when RBM Combat is off, and the game keeps native item stats. This matters, because RBM's numbers are tuned for RBM's equations: a Short Simple Raider Spear carries `thrust_damage="6"` precisely *because* `RBMComputeDamage` multiplies a Pierce magnitude back by twenty. Read that 6 through native's equations and the spear is worthless. It never happens, because with RBM Combat off the spear is a native spear again.

The consequence for this model is that the two paths are genuinely two models — different equations **and** different item data — and both must be complete. Neither can borrow from the other.

### 3a. Where a blow's force comes from

| | **RBM Combat ON** | **RBM Combat OFF (vanilla)** |
|---|---|---|
| Source of magnitude | **Physics.** Weapon mass, inertia, centre of mass, length, and the wielder's skill. | **The number printed on the item.** `SwingDamage` / `ThrustDamage`. |
| Listed item damage | **Never used for melee.** Not once. | Is the entire answer. |
| What weapon quality buys | **Penetration.** `sqrt(damageFactor)` divides the armour threshold. A fine blade does not hit *harder*; it finds the gap. | Force. A better weapon has a bigger number. |
| Skill | Enormous. A master lands what a recruit only swings — up to ~3× on RBM's own clamp. Nothing is added; it is already inside RBM's damage clamp. | Small but **not nothing**: `× (1 + 0.3·min(skill/250, 1))`. A master does 1.3× a recruit with the same sword. See the note below. |
| Swing | `StrikeMagnitudeForSwing` — vanilla's two-body impulse physics, on RBM's class-specific angular speed (`0.83/1000` for maces and polearms, `0.75/800` two-handed axe, `0.83/800` blades). | — |
| Thrust (Pierce) | **Raw kinetic energy, no clamp at all.** `CalculateThrustSpeed` / `SimulateThrustLayer`, three acceleration layers, then energy capped at 180 (one-handed) or 250 (two-handed). | — |
| Thrust (Cut/Blunt) | Physics, then through the same skill clamp a swing uses. | — |
| Missiles | `energy = 0.5·m·v²`, where `v` comes from the bow's **draw weight**, its powerstroke and its material efficiency. A longbow (0.835) wastes more of the draw than a composite horsebow (0.90). The arrow's own damage enters as a head factor. | The **arrowhead**: the shaft's listed damage, scaled linearly by the bow's missile speed against a reference of 100 and clamped to 0.5…2. Native reaches damage through missile speed, and this is the honest shorthand for it. |
| Slings | Their own law — the arm whirls the stone, so the man's **skill reaches the speed**, which is true of nothing a bowman does. | Listed damage. |
| Thrown | `0.5·m·v²` from `calculateThrowableSpeed`; a javelin's energy is capped at `weight × 300`, a throwing axe's is not. | Listed damage. |

> **Both paths must exist, or the baseline cannot save you.** For a while the ranged branch ran RBM's bow physics *unconditionally* — so with RBM Combat off, every archer in Calradia was priced on draw weight and powerstroke while every infantryman beside him was priced on the number printed on his sword. Those are not the same units, and a baseline cannot rescue a striker who is measured in different units from the man he is measured against.

> **Why skill counts under vanilla rules at all.** It is tempting to leave it out — native only reaches damage through handling and swing speed, so any share we give it is an estimate. But leaving it out is not the conservative choice, it is a *wrong* one, and for a while it was the code: the constant sat there unused and a master swordsman and a raw recruit holding the same sword came out identical. The reason it matters is §2 — **this model divides vanilla's tier term away, and tier was the only thing carrying a soldier's training.** Take tier out and put nothing back, and a veteran's entire superiority collapses to his gear.

> **A trap, documented here because it has bitten three times.** `ThrustMagnitudeModifier` (0.05) and `OneHandedThrustDamageBonus` (20) are *exact reciprocals* — `OneHandedThrustDamageBonus = 1f / ThrustMagnitudeModifier`. They are two halves of one scaling: RBM multiplies a Pierce magnitude by 0.05 when computing it and `RBMComputeDamage` multiplies it back by 20 when spending it. **They cancel.** This model works in that cancelled scale, so a Pierce thrust is raw energy with *neither* factor applied. Applying the 0.05 alone — which is what reading `CalculateMissileMagnitude` in isolation invites — divides every spear in Calradia by twenty.

### 3b. How armour answers

**RBM Combat ON** (`Utilities.RBMComputeDamage` → `WeaponTypeDamage`):

```
armorReduction = 100 / (100 + armor · armorMultiplier)          ← multiplicative, but only on TRAUMA
penetrated     = max(0, magnitude − armor · threshold · thresholdModifier / weaponDamageFactor)
trauma         = magnitude · bluntCarry · stopped · armorReduction
damage         = penetrated + trauma
```

Two mechanisms, not one:

- a **subtractive penetration threshold** — armour that is thick enough simply stops the blow, and no amount of a sword's edge gets through it;
- a **multiplicative blunt carry** — what the armour stopped still arrives as shock, and a mace carries far more of it through than a blade.

The threshold is per-weapon-type (RBM's own `getWeaponTypeFactors`: roughly 5 for cut, 3 for pierce), and it is **divided by the weapon's damage factor** — which is the whole of what weapon quality means here. A Pierce arrow or bolt **halves** the armour it meets on anything that is not plate; a Cut broadhead does not. Plate is the only armour RBM does not halve, and it answers an arrow properly.

Consequences that fall straight out of this and are worth stating plainly:

- **Maces and spears come into their own against heavy troops.** A blunt weapon carries more of a stopped blow through as trauma; a spear-point beats armour a sword-edge cannot.
- **A bodkin and a broadhead are not the same arrow.** One halves a hauberk, the other does not. Against an unarmoured looter they are much the same; against a Huscarl they are not remotely.
- **Armour is roughly twice as protective as in vanilla** (`armorMultiplier`, 2.0), and a heavy harness can stop a cut outright, letting nothing through but the shock of it.

**RBM Combat OFF** (`DefaultStrikeMagnitudeModel.ComputeRawDamage`):

```
damage = magnitude · (50 / (50 + armor))          ← gentler, and applied to the whole blow
       + threshold and blunt-factor terms by damage type
```

Vanilla's curve is softer, has no penetration threshold worth the name, and does not distinguish a bodkin from a broadhead. Both paths read the numbers from the game's own item data — neither is a table of guesses — but they are genuinely different models, and the auto-resolve follows whichever one you are running.

---

## 4. A soldier is not one weapon

A man carries a spear *and* an axe and swings whichever is in his hand. So the kit holds his **whole melee arsenal** — every weapon across every battle set, each with the share of blows it accounts for — and the blow is the average over it.

This matters more than it sounds. A mace and a sabre answer armour by different rules, so each weapon is run through the armour equation **separately** and the results averaged **afterwards**. Averaging them into one weapon first produces a blow that is neither.

The same rule applies to the quiver. An Imperial Archer carries Greased Flight Arrows (125, Cut) in two of his three kits and Needle Bodkins (100, Pierce) in the third. Picking the arrow with the biggest number and pricing every shaft he looses as that arrow made him nine times worse against armour than he really is.

**The exception is a horse.** When a rider bears down on him, a man with a spear reaches for the spear — every time, and so does every man beside him who has one. So against cavalry the melee pool narrows to his **polearms**, and if he has none he swings what he has and hopes.

Things that are deliberately *not* in the pool:

- **Launchers and ammunition.** Whatever the number on a sling says, a man in the line is not hitting anybody with it.
- **Shields.** Not how he fights.
- **Thrown weapons.** He hurls those while the lines close and then draws steel — see §6.

---

## 5. Where a blow lands, and what stops it

### Hit zones

**Six zones, not vanilla's four.** RBM's own body-part worth is reckoned over six bones, and this model keeps all six rather than folding shoulders into `Arm` and chest+abdomen into `Body` — a shoulder is not an arm and does not answer like one. Each row is a distribution and sums to 1. The game itself has no such table — a real blow's bone is decided by collision geometry per swing — so these are honest estimates of that geometry, not figures lifted from anywhere.

| | Head | Neck | Torso | Shoulder | Arm | Leg |
|---|---|---|---|---|---|---|
| Foot vs foot | 0.15 | 0.05 | 0.40 | 0.20 | 0.15 | 0.05 |
| Foot vs mounted *(no polearm)* | 0.03 | 0.02 | 0.30 | 0.10 | 0.08 | **0.47** |
| Mounted vs foot | **0.22** | 0.08 | 0.32 | 0.18 | 0.15 | 0.05 |
| Mounted vs mounted | 0.15 | 0.05 | 0.35 | 0.20 | 0.15 | 0.10 |
| Missile vs foot | 0.12 | 0.03 | 0.50 | 0.15 | 0.10 | 0.10 |
| Missile vs mounted | 0.08 | 0.02 | 0.40 | 0.12 | 0.08 | **0.30** |

Two footmen are eye to eye, so it is the chest and shoulders and arms that catch it and the legs almost never — a man does not stoop to hack at ankles. But a man on foot hacking upward at a horseman finds the rider's **legs and lower body** at his eye level, while the rider cutting downward finds the footman's **head and shoulders**.

**A spear is the exception, and it is why `Foot vs mounted` is not the whole of foot-against-horse.** The legs are where a footman's reach *ends*, not where he wants to strike: a man with a sword cannot get past them. A polearm gives him back the height the horse took — he sets it at the rider's chest and face and does not stoop to the animal's shins. So a spearman at a horseman rolls **`Foot vs foot`**, the same spread two footmen trade, and only a man *without* a spear is reduced to the legs. This must agree with the weapon pool, which narrows to his polearms in the same breath (§4): it would be nonsense to price the blow as the spear and then aim it as though he were swinging a hatchet. The consequence worth stating: barding is worth a great deal against infantry **who have no polearm**, and much less against the ones who do — which is most of them.

An arrow does not roll any of the foot/mounted matchups, but it is **not** target-blind either: a shaft loosed at a rider meets a far larger, lower target, so a great share of them find the horse at the leg where its barding answers, and fewer reach the man's head above. A single missile table could not tell those apart.

### Horse or man — never both

A blow at a mounted troop is a blow at **two** things, and it finds only one of them.

| The blow | Finds the horse |
|---|---|
| A footman's melee stroke | **0.45** |
| A horseman's melee stroke | 0.15 |
| A missile | 0.22 |

The horse is the bigger target and the lower one, so a footman hacking upward often takes it; a horseman is aiming at the man he means to unseat and rarely wastes a stroke on the mount; an arrow is loosed at the mass of the rider and only now and then takes the animal instead.

A blow that finds the horse wears the **horse alone** — its own pool, met through its own barding — and never touches the rider, his armour, his defence or his wound pool. A blow that finds the rider meets **his** armour, not the barding, because the barding is the horse's and the horse was not hit. Horses die, and when one does its rider is a man on foot in cavalry harness: no barding, no height, no charge, and no longer a horseman for the purpose of anything else in this document — a man whose horse is dead has left the cavalry skirmish, whatever else he is doing.

These three shares are dials for the whole cavalry balance and are meant to be tuned against a paired log: foot infantry should ground a squadron over the course of a fight, not in a round.

### And a blow is worth what it is worth *where it lands*

Where a blow falls decides two separate things, and only one of them is armour. RBM's own `DamageRework.GetBodyPartDamageMultiplier`:

| | Pierce | Cut | Blunt |
|---|---|---|---|
| Head / Neck | **1.5** | **1.5** | **1.5** |
| Abdomen | 1.0 | 1.0 | 1.0 |
| Chest | 0.9 | 0.9 | 0.9 |
| Shoulders | 0.6 | 0.6 | 0.7 |
| Arms | 0.5 | 0.6 | 0.7 |
| Legs | 0.5 | 0.6 | 0.7 |

**A head hit is worth three times a leg hit.** RBM's table is over six bones and this model keeps all six of them, so every row above maps across untouched.

So **a real blow rolls a body part** from the distribution for its matchup, meets the armour standing over *that* part, and is paid what a blow to that part is worth. It does not meet an average of a man. The reference tables and the baselines take the expectation over all six zones instead — each zone's own armour, each zone's own multiplier, **averaged after** — because they are asking about a matchup, not a moment.

That ordering is the same rule as everywhere else here: a mace and a sabre must meet armour separately, and so must a head and a shin. Averaging the armour first and applying one multiplier to the result yields a blow that landed nowhere, on a man who is the mean of himself.

This is also what makes the hit-zone tables above *matter*. Before it, they moved which armour a blow met and nothing else — so a rider cutting down at heads and a footman hacking up at shins were, once the armour washed out, throwing the same blow.

The horse's own barding and bulk answer at the leg and body — but they are kept **apart** from the rider's armour, because a horse can be killed and a dead one answers nothing.

### Defence: block, parry, riposte

Gated behind `SimulationDefenseSystem` (on by default). A blow is not simply thrown at a target — it is *answered*, and how well depends on the defender's own training.

**A melee blow is met by one defence roll**, and on a success by a block-vs-parry split:

| | Base | + skill, at saturation | |
|---|---|---|---|
| Behind an intact shield | 0.45 | +0.30 | high and easy |
| With only a weapon (or a shattered shield) | 0.20 | +0.18 | a floor, ~2× harder across the range |
| | | | capped at **0.75** — no defence makes a man untouchable |

Both climb with the **defender's own melee skill**. A successful defence is a **parry** with probability `parryShare` — base 0.20 at equal skill, tilted by the defender's skill *advantage* (his skill minus his attacker's), capped at 0.6 — and otherwise a plain **block**. A shield block dumps the whole blow onto the shield; a weapon block merely deflects it; a **parry negates the blow and lands a riposte on the attacker**, against the attacker's own wound pool.

Note the two *distinct, non-overlapping* uses of skill, because it is easy to read them as one: the defender's **absolute** skill raises the defence *chance*; the skill **gap** splits a defence into block-or-parry. Out-fighting a man is what turns your defences into counters.

This is what makes landed melee lethality depend on **training** rather than on kit alone — which is the thing that pulls the sim's ranged-to-melee kill balance back toward a real field battle, and it is why §2's removal of the tier term does not simply collapse a veteran's superiority into his gear.

**The exceptions are as pointed as the rule.**

| | |
|---|---|
| **A charge is unblockable** | `defenseChance = 0`. There is no getting a board in front of a lance at the gallop. |
| **An archer ridden down** | ×0.25, and **no parry at all**. A bow is no parrying weapon and there is no countering a charge with a knife. This is the classic death of unsupported archers. |
| **A mounted man** | ×0.85, shield and all. He sits high and busy, managing a horse with one hand, and a shield slung for the saddle does not come across as fast as one carried on the arm. A bit less, not a collapse. |
| **An archer under fire** | ×0.5 on his shield block against incoming **shots**. A man watching his own shot and his target gets the board up late. |

**A ranged blow is answered by the shield alone** — quality-based, skill-blind — and now to **full negation** onto the shield rather than a fractional skim. `SimulationShieldBlockChance` is read *here and nowhere else* once the defence system is on:

```
block = SimulationShieldBlockChance · sqrt(shieldQuality / typicalShieldQuality)     capped at 0.65
```

The square root matters. Taken flat, a Pavise — which is a wall of wood and scores accordingly — sat on the cap while a Norse round shield turned 21%, so the shieldwall infantry that shields exist *for* came off worse than a crossbowman hiding behind a board. A better shield should stop more blows than a poorer one; it should not stop three and a half times as many. Most of what stops a blow is the man, and men do not differ fourfold.

Shield quality is RBM's own reckoning (`ItemValuesTiers.CalculateShieldTier`): durability, the armour of its face, and how much of him it covers. A steel round shield and a wooden adarga are the same span and the same 60 length and differ **five-fold** in hit points, and that is what separates them.

**Against arrows the same shield does about a third better again** (×1.35). An arrow comes from one known direction and arrives on its own; a man gets the board up and it sticks there. A swordsman feints, comes round the edge, and waits for the shield to drop. This is the whole reason a line advances under fire from behind its shields.

Shields **degrade**. What a shield stops, it eats, and a wooden board that has taken thirty mace-blows is kindling. The item's own hit points set the spread against a reference shield, so a steel shield really does outlast an adarga. A shield at zero integrity drops its bearer to the bare-weapon defence chance for the rest of the fight. Note that the capacity a shield eats was raised more than twentyfold (`ShieldCapacityPerMan`, 25 → 600) when the defence system came in, and for a plain reason: a block now dumps the *whole* blow onto the board rather than a skimmed fraction of it, so at the old capacity every shield in Calradia splintered in the first exchange.

**With `SimulationDefenseSystem = 0`** the whole of the above stands down and the old **fractional skim** returns: the same `block` formula, applied as a share taken off every blow, melee and missile alike, with no skill, no parry and no riposte. It is kept whole because the melee landing spread is calibrated differently for it — see the note under `MeleeLandingExponentNoDefense`.

### And a shot can miss

Gated behind `SimulationRangedMissEnabled` (on by default). Auto-resolve has never let a shot miss: every arrow it loosed connected with somebody, and the only thing that could stop one was a shield in the way — so an archer's shafts all arrived, and the arm was worth what a bowman would be if he never missed in his life.

A shot now rolls to hit **before it is a blow at all**, which is the important part: a missed shaft meets no armour, wears no shield and kills no horse. It sits *above* the zones and the shield, not inside them.

```
missChance = SimulationRangedMissChance          (0.35 — an UNTRAINED man with a bow)
           · (1 − 0.6 · skillFraction)           accuracy is the most trained thing about an archer
           · launcherFactor                      bolt 0.7 · arrow 1.0 · stone 1.3
           · volleyFactor                        1.25 in the volley, 1.0 once closing
           · mountedShooterFactor                1.25 — loosing from a moving horse
           · mountedTargetFactor                 1.4  — shooting at one
                                                 capped at 0.8
```

Skill bites harder here than anywhere else in the model, and deliberately: accuracy is what an archer's training *is*. At saturation a man misses 40% as often as an untrained one — a Fian's shafts find men, a levy's find dirt — but never to zero, because nobody hits every shot. The launcher factor is keyed on the *shaft's* class, which is how the shot profile names itself: a bolt means a crossbow, the one ranged weapon in Calradia a conscript can point and loose; a stone means a sling, the least accurate thing on the field by a distance.

**Fired missiles only.** A thrown javelin is a committed, short-range throw at a man the thrower can see, and is left alone.

> **Calibration, and read this before tuning anything else.** This roll removes shots that `RangedLandingExponent`'s magnitude spread was implicitly standing in for. That exponent was calibrated against a paired log with **no miss roll upstream**, so it was carrying the misses itself, in magnitude space. With a discrete miss now taking them out first, the arm is being charged for the same failure twice — exactly the double-count the melee side had. Re-measure ranged against a paired log and expect to **lower** `RangedLandingExponent` to compensate. Until that is done, these two dials are known to overlap.

---

## 5a. Who swings, and at whom

Gated behind `SimulationArmTargeting` (on by default).

Everything in §5 asks what a blow *does*. This asks a prior question the model had no answer to at all: **whose blow is it, and who is on the end of it?** Vanilla picks both **uniformly at random from the whole side** — `SelectRandomSimulationTroop`, twice, arm-blind. A melee footman was as likely to "hit" an enemy archer three ranks back as the man in front of him, and an archer was chosen to act only as often as archers happen to be common.

It is a **weighted preference, never a hard filter**: a drawn candidate of an unfavoured arm is usually passed over and redrawn, which biases the pick without ever forbidding an arm that is present. No blow is lost to it — a passed-over man is redrawn, not dropped — and when the preferred arm is absent it degrades cleanly to random.

**Who acts, by phase:**

| Phase | |
|---|---|
| **Volley** | The bows — but *weighted by how many bows the side brought*, not handed to them whole. Only `share^0.6` of the volley's shots become archer fire. |
| **Skirmish** | The horse (1.0), the shooters (1.0), and foot skirmishers who still have javelins (1.0). Everyone else on foot is merely walking (0.15). |
| **Contact** | The mounted arms are chosen **1.4×** as often as their headcount alone would give them — a horseman rides in, kills, backs out and comes again, engaging many where the foot engage one. A **foot archer** drops to **0.35**: the enemy is on him and he is drawing a sword or dying, not loosing freely. (A horse archer keeps his mounted weight — he rides clear and shoots; he is not overrun the way the foot are.) |

The volley's `share^0.6` is not free, and it is the subtle one. Random selection gave archers `share` of the shots and the old `VolleyFocus` boost multiplied them by `share^-0.4`, so their output went as `share^0.6`. Reproducing that same count-dependence **in the pick** is what lets the two coexist: **when arm targeting is on, `VolleyFocus` stands down**, because the bows are now handed their turns directly and boosting them as well would pay for the same thing twice.

**Whom the blow reaches, by the striker's arm:**

| Striker | Preferences |
|---|---|
| Melee foot | The man in front (1.0), the horse in among the line (0.6), the enemy's shooters behind their own line (0.2) |
| Ranged (foot bows and horse archers alike) | The massed foot (1.0) over the mounted, who are fewer, faster and further off (0.35) |
| Cavalry, in the skirmish | Horse meets horse out in front of the foot (1.0 vs 0.3) |
| Cavalry, at contact | They break off and ride down the shooters (0.9) before grinding at the foot line (0.75) |

Two of these numbers carry scars worth recording. `CavalryMobilityMultiplier` was **eased from 1.75 to 1.4**: at 1.75, stacked on the charge buffs, the horse landed so many blows it ran the field on charges where a ranged army should have won. `ContactArcherStrikerWeight` was **eased from 0.25 to 0.35**: the sim had let an attacker's bows shoot half a forest battle's blows where a real forest melee lets them shoot a fifth, and 0.25 over-corrected it.

**This feature must stand down with the master switch, and here is why it is not optional.** Arm targeting routes volley shots onto foot soldiers *trusting the equipment model's volley rule to nullify them*. With the equipment model off, that rule is not there — and those shots would land full vanilla damage before the lines ever met.

---

## 6. The battle has a clock

The tick allocation is called once at the top of every round, and it is the **only** place the simulation ever says a round has turned. A blow cannot say it — a blow does not know how many came before it. So the battle's clock is read from there, and everything spent *over* a battle rather than in an instant hangs off it.

### The field is only as wide as the real one

Before a round is diced into its acts, there is a prior question the acts assume an answer to: **how many men are in the fight at all.** Vanilla, and RBM after it, hands each side `pow(itsOwnMen, 0.6)` blows a round off its **whole headcount** — every soldier a side owns is an eligible striker every round, so a lord who marches up with four times the enemy swings roughly `pow(4)^0.6 ≈ 2.3` times as often and grinds the smaller force to nothing at almost no cost to himself. That is not how the field battle you would have *fought* plays, and it is the reason send-the-troops always looked so much cheaper than going yourself.

**The field only holds so many.** The engine never puts every man in the line at once: it spawns each side up to a share of the battle-size cap (`BannerlordConfig.GetRealBattleSize()`, the 200–1000 slider) and feeds the rest in as reinforcements as their own fall. RBM sets the split the player actually spawns into (RBMAI's `SpawningPatches`), and it is a simple one:

| Both sides' strength | Who stands in the line |
|---|---|
| **Fit under the cap** (`nDef + nAtt ≤ cap`) | everyone — the whole of both sides |
| **Over the cap**, smaller side ≤ half | the **smaller side in full**, the larger fills the remainder of the cap |
| **Over the cap**, both sides > half | **half the cap each**; the rest is reserve |

So 2000 vs 500 at a cap of 1000 is fought **500 against 500**, with 1500 men waiting behind the larger side's line; 700 vs 250 is fought whole (it fits); 1000 vs 450 is 550 against 450.

**The simulation now fights it the same way.** Each round it reads the two sides' live strength, works out how many of each the cap lets stand in the line — the *engaged* count — and hands out blows off **that** rather than off the whole headcount, using vanilla's own formula (`min(enemy·2, pow(engaged, 0.6))`) so nothing else about the round moves. For 2000 vs 500 the blow ratio collapses from ~2.3 : 1 to ~**1 : 1**: the two lines are the same width, so they trade nearly evenly. The larger side still **wins** — it feeds its reserve forward as the front falls and the smaller side has no reserve to feed — but the smaller side, dying at the pace of the front rather than of the crowd, lands far more blows before it breaks, and the winner **pays in casualties for every rank it has to bring up**. A bloodless four-to-one is now a bloody one, which is the whole point.

**The engaged counts are re-read every round off live strength**, so the front is not frozen at the muster. As the loser thins below half the cap, the winner's share of the line grows to fill what the cap leaves — envelopment, and it falls straight out of the same rule with nothing added for it.

**The round is repriced to match.** Fewer blows a round means the battle takes more rounds to resolve, and charging each of those extra rounds the full field price (§6, the clock) would stretch the fight out across the campaign map — the same "billed half an hour for a slice of a battle" error the clock was rebuilt to kill. So a narrowed field round costs proportionally fewer campaign minutes, scaled by how much of the round the thinner front actually carries. The battle is bloodier; it is not longer.

**Field battles only.** A wall assault has its own, finer frontage — the openings the siege equipment bought (§6, *Width*) — and is left to it; a broken side being ridden down has had its blows zeroed already and is left alone; and a battle small enough that both sides fit under the cap is vanilla's, untouched. It is a model constant (`FieldFrontageEnabled`), dialled against the log rather than set by the player, exactly as the tick multiplier is.

### A battle has three acts

Auto-resolve has only ever known about the third.

| | | |
|---|---|---|
| **1. The volley** | the lines are far apart | The bowmen have the field, **and nobody else does anything at all.** In the first round only the **defender** may loose — the attacker is still too far out to answer. |
| **2. The skirmish** (3 rounds) | the ground between them | The javelins come off their backs and are hurled. And the **horse meet the horse**: each side's cavalry ride out at each other in the open, long before the foot are anywhere near. Everyone else is still walking, and pays the closing penalty. |
| **3. The lines meet** | the brawl | Everything auto-resolve has always imagined a battle to be — and the least interesting of the three. |

**In the volley, a man who is not shooting lands no blow.** Not a weak one — *none*. The lines are a bowshot apart: no sword reaches that far, and a man walking toward an enemy he cannot touch is not fighting badly, he is not fighting. The volley is the archers' round and nobody else's, which is the entire reason it is worth having archers.

**And the archers get their turns back.** This is subtler, and it is not a damage question at all — it is a question about whose *turn* it is. Vanilla hands a side `pow(men, 0.6)` blows in a round and then picks the man who throws each one **uniformly from the whole side**. So an archer is chosen only as often as archers are common. Once nobody but an archer does anything, four blows in five of a typical army's round are spent on men standing still — and the archers are not shooting *slowly*, they are being **skipped**. Their own infantry are eating their turns.

There are two ways to give them back, and **which one runs depends on `SimulationArmTargeting`** (§5a). With arm targeting **on** — the default — the bows are simply handed their turns in the *pick* itself, weighted by `share^0.6`, and everything below stands down. What follows is the older path, `VolleyFocus`, which is what runs when arm targeting is off: it cannot change whose turn it is, so it compensates in **damage** instead.

On that path an archer's volley shot is multiplied by `share^-0.4`, giving him back the tick allocation he would have had if the volley were a battle between the archers alone (`pow(share·men, 0.6)` rather than `share·pow(men, 0.6)`).

| archers on the side | multiplier |
|---|---|
| all of them | ×1.00 |
| half | ×1.32 |
| a fifth | ×1.90 |
| a twentieth | ×3.31 |

Note what this is **not**: it is not `1/share`. That is the obvious fix and it is badly wrong — it would hand the side's entire volley to whatever archers it happens to own, so one bowman in a hundred would loose as many shafts as a hundred bowmen, and *how many archers you brought would stop mattering*. That is the one thing a volley must depend on. More archers still means more shooting here — just sublinearly, exactly as vanilla scales everything else. The `0.6` exponent in arm targeting's own volley pick is this same reasoning, moved from damage into selection: **the two must never both run**, or the archers are paid for the same skipped turns twice.

This was a closing *penalty* before — a hundredth of a blow, but a blow — and across four thousand of them it added up to a real body count landed by men who were, at the time, several hundred yards away with their shields up. Nothing is spent by such a man either: he splinters no shield and kills no horse, because he never reached one.

In the **skirmish** the closing penalty does apply, and should: the ground between the lines is not a bowshot any more, so a blow is at least conceivable — but he is still walking, past a cavalry battle he can do nothing about and under javelins he cannot answer.

Two consequences worth stating, because both were wrong before and both matter:

- **A javelin is thrown in the skirmish, not the volley.** A man does not hurl a spear at somebody a bowshot away. He carries it across the open ground and throws it when he is close enough, and then it is gone and he is a man with a knife.
- **A horseman has two moments of contact.** He meets the enemy cavalry in the skirmish; he cannot reach their infantry until the lines close. He may charge at either — but he is *engaged* only when he has actually found somebody, and until then he is riding across empty ground.

### The volley

While the lines are closing, a bowman is doing the only thing he is for, and the man walking toward him is doing nothing at all but walking. This is the whole of what auto-resolve never modelled: it threw archers into a melee brawl at contact range and wondered why they were bad.

How long the approach lasts is a question about the ground:

| Context | Volley rounds |
|---|---|
| **Siege**, not an assault | **12** |
| Plain, steppe, desert, dune, snow, river, forest | 6 |
| Naval raid, sea, open sea, river crossing | 4 |
| **Village** | **2** |

Storming a wall is the longest approach there is, and everyone on it is shooting at you the whole way with nowhere to go but forward. A village is the opposite: there is no ground to cross at all — the fighting starts in among the houses, at arm's length, the moment anyone arrives. Ships closing on one another is a short thing, and then it is boarding and butchery.

During the volley, a man who is **not** shooting or throwing pays a **closing penalty** — he is walking, into arrows, and achieving very nearly nothing.

### The opening rounds belong to the defender

For the first **two** rounds, only the defender may loose. He is standing on his ground with his enemy in the open and the whole field to shoot across; the attacker is still coming, too far out to answer, and eats it. That is what it means to advance on a prepared position, and it is why storming one is expensive.

The attacker is not merely out-shot in those rounds — he is doing **nothing at all**, because a man in the volley who is not shooting lands no blow (see above). Two rounds of free fire is the price of crossing open ground at somebody who is already there.

Javelins are unaffected: nobody throws during the volley at all, so there is nothing to delay.

### The defender's high ground

A side that stands and waits picks the ground it waits on — a ridge, a slope, the lip of a ford — and its archers shoot **downhill**: a little more range, a plunging angle that finds the gaps a level shot glances off, and a target that is climbing at them rather than shooting back on even terms. The attacker, coming up, shoots **uphill** for the reverse of all of it. So in a field battle the defender's **fired** shots are worth **×1.10** and the attacker's **×0.90**.

This is the field cousin of the siege wall's magnitude bonus (×1.25 / ×0.85, below), and deliberately a **milder** one: a wall is a wall every time, but a defender does not *always* hold the height, so the flat field figure is kept small. It touches fired missiles only — a javelin at skirmish range is a level, short throw the slope barely moves — and never a wall assault, which prices the same idea harder and by phase. Both figures are model constants (`FieldDefenderShotMagnitude` / `FieldAttackerShotMagnitude`); set them to 1 to switch the bias off.

### A stormed wall has two acts, not three

**None of the three acts above happen in a siege assault.** A wall assault (`MapEvent.IsSiegeAssault`) replaces the volley/skirmish/contact clock outright with two acts of its own, in `Simulation/SimulationSiege.cs`. Battles merely *fought at* a besieged settlement — sally-outs, siege ambushes, relief actions — are ordinary field battles and keep the three acts.

**And a wall assault does not necessarily stay one.** Native keeps the battle's kind in a *mutable* field: the instant a defending party that is not inside the settlement joins — a relief army arriving to lift the siege — `MapEvent.AddParty` rewrites the type from `Siege` to `SiegeOutside`, and `SimulationContext` is derived from that same field, so the ground changes with it. Every siege fact is latched at round 1, which is right for a fact about a wall and wrong once the battle has stopped being about one.

So the model **re-checks every round and stands down** when the battle leaves the wall: the terrain is re-read as what it now is, the horses and the charge come back, the wall's bonuses stop, the artillery falls silent, and the frontage stops mattering. It is **one-way** — native flips the type *back* to `Siege` once the relief force is gone, which would otherwise re-arm a storm whose approach was crossed an hour earlier, with fresh widths read off equipment the assault had already spent.

This was not hypothetical. At Tamnuh Castle on 1084-030 the garrison went from 223 men to 673 at round 13, and the storm rules ran on to round 116 — attackers dismounted, unable to charge, confined to their ladders' frontage and eating the wall's bonuses, against an army standing in the open field beside them. The log now prints `wall: LEFT THE WALL at round N`.

| | |
|---|---|
| **1. The approach** (12 rounds) | The killing ground. Nobody is in reach of anybody: the men on the parapet shoot down, the besiegers shoot back up, and no sword touches anything. |
| **2. The assault** | The ladders go up and the fighting is hand to hand at whatever openings the siege equipment bought. |

**On the approach the defender has every advantage there is.** He looses **five shots for every one** the attacker gets — he is standing still behind stone with the town's arrow stores at his elbow, while the besieger is walking uphill carrying a ladder. His shots are worth **×1.25** and the attacker's **×0.85**, and the attacker misses half again as often on top of the wall skew that already applies to any siege.

**And a besieger can only reach the men shooting at him.** The defending infantry are behind the parapet; an arrow aimed at one of them hits masonry. This is a *hard* rule, not a preference — a garrison with no archers on the wall simply cannot be hurt while the ground is being crossed, which is correct, because there is nobody up there to shoot at. The defender is under no such restriction: from a wall he can see the whole army.

**In the assault the defender's edge narrows.** Two shots to one rather than five, and **no** magnitude bonus — his advantage there is the frontage and the rate of fire, not the weight of the arrow. The attacker keeps his penalties (he is still fighting from a ladder) but can now reach anybody.

**And how good the wall is scales all of it.** Fortifications are built, and native tracks the level on the settlement (`Town.GetWallLevel()`, 1–3, off `SettlementFortifications` for a town and `CastleFortifications` for a castle). A higher wall means a higher parapet, better merlons to shoot from and hide behind, and a longer, worse climb.

What scales is the **advantage**, not the raw number: every dial is a departure from parity, and the wall scales that departure. **Level 3 is the reference and nothing scales above it** — a fully fortified city plays exactly as this model did before wall level was read at all, and every lesser wall is a lesser version of the same edge, 25% of the advantage per level down. So nothing can invert into a handicap.

| | palisade (1) | middling (2) | great walls (3) |
|---|---|---|---|
| Defender shots per attacker shot, approach | 3 : 1 | 4 : 1 | **5 : 1** |
| …assault | 1.5 : 1 | 1.75 : 1 | **2 : 1** |
| Defender magnitude, approach | ×1.13 | ×1.19 | **×1.25** |
| Attacker magnitude | ×0.93 | ×0.89 | **×0.85** |
| Attacker miss, approach | ×1.25 | ×1.38 | **×1.50** |

A settlement whose wall level cannot be read falls back to the **reference**, not to a poor wall — the conservative failure, since a bad reading can then never quietly hand a siege to the besieger by treating a great city as a palisade. (`GetWallLevel()` genuinely returns 0, not 1, when it cannot find the building.)

**It does not touch the width**, deliberately. Width is a fact about the *openings* — how wide the breach is, how many men fit through a gatehouse, how many can stand at the top of one ladder — and a hole in a great wall is the same size as a hole in a poor one. A better wall buys a worse approach to it, not a narrower gap once it is down.

### Width — the frontage at the openings

Three lanes, which is native's own `MaximumAttackerMeleeSiegeEngineCount = 3`. What stands in each decides how many men can fight there, and the two sides get **different** numbers:

| Lane content | Attacker | Defender |
|---|---|---|
| Breach in the wall | 4 | 4 |
| Siege tower | 4 | 4 |
| Battering ram (middle lane only) | 8 | 8 |
| Siege ladder | 1 | **5** |
| Empty | 0 | 0 |

The ladder row is the shape of the whole idea: one man can be at the top of it at a time, and five can be waiting for him when he arrives.

**Lanes are identified by what stands in them, never by slot index.** Native's three melee slots look like three positions on the wall and are nothing of the kind: `DefaultSiegeStrategyActionModel` deploys into `FindIndex(engine == null)` — the first free slot — so the index is just the order the besieger finished building things in. Reading slot 1 as "the gate" put a real ram (slot 0, Tamnuh Castle 1084-029) outside the model entirely: it was standing, the defenders were shooting at it, and it bought its side nothing.

So the lanes are assembled from the equipment: **the gate** is wherever the ram is, and **the two wall lanes** are the settlement's two wall sections (`WallSectionCount` is hardcoded to 2 for every fortification), each either a hole or whatever climbing engine is assigned to it. Engines count only if `IsActive` and `Hitpoints > 0`.

**A breached section is a hole and nothing else** — men walk through a gap, they do not queue for a ladder beside it. **An empty lane is worth nothing**: no fallback, no floor. And with only two stretches of wall, a besieger who built three towers has one with nowhere to go — that surplus is *logged*, never silently dropped.

The widths are frozen at the moment the approach ends, so a ram broken on the way in contributes nothing.

**Width is a hard ceiling on melee actions, not a share of them.** At most `attackWidth` attacker melee blows and `defendWidth` defender melee blows in a round, whatever the size of the armies — bounded also by each side's own natural melee output, so a side with few melee troops does not suddenly field a full frontage of them. That is what a breach *is*: a gap only so many men can fight in at once, with a thousand more behind them who do not widen it by standing there.

It was a ratio first, and eight logged sieges showed why that could not work. Native's besiegers build rams and towers and nothing else, and both are symmetric (8/8, 4/4) — so every siege opened at 12:12 or 16:16, and since both widths step together a ratio of equals is 1:1 for ever. The frontage never once touched an outcome. The ladder's 1/5, the whole reason widths differ at all, was never built in any of them.

A consequence worth stating: the round's total blow count is **no longer preserved**. A storm through a single gap contains less fighting than a field battle between the same armies, so a siege resolves over more rounds — and each still bills the campaign clock at `simulationRoundMinutes`. If sieges start taking implausible campaign *time*, the round clock is the thing to reprice, not the ceiling.

**Ranged fire is untouched by width** — men shoot over the fight from the whole length of the wall, and no gap in the masonry limits that.

**Width moves.** Every man the attackers put down at an opening widens it by one for *both* sides — the press gives ground, the fight spills along the wall; every man the defenders put down narrows it by one for both. Melee kills only: an archer picking a man off the ground below does not close a breach. The floor is what the equipment bought at the start, and there is no ceiling — an assault that is going well goes better, which is what a collapse looks like from outside.

**If nothing survives, there is no assault.** Every ladder burned, every tower broken, the ram destroyed and the wall still whole: the men who crossed the killing ground have arrived at a sheer face with empty hands. The besiegers are **repulsed** on the spot, through native's own `Route()` so the survivors leave as fugitives, carrying whatever the crossing cost them.

**How the round is divided.** The game hands each side one number — its tick count — and a siege has two ratios to honour at once. So the allocation solves for all four counts directly (the two sides' shots and the two sides' melee blows), from the rate of fire, the width, and the archers the two sides actually brought; the tick counts carry half the answer and the striker selection carries the other half. **The round's total is unchanged**: this redistributes a round, it does not inflate one, so no siege lethality moves for a reason that is not the wall.

Every number here is a hardcoded constant, uncalibrated as of this writing.

### The artillery

Engines are not troops, and everything above is built around one soldier striking another — so they get their own volley, fired once at the top of each round, outside the blow-by-blow entirely (`Simulation/SimulationSiegeEngines.cs`). They are read from `SiegeEnginesContainer.DeployedRangedSiegeEngines`, four slots a side, counted only while built and unbroken.

Engines are bucketed by id the way native itself buckets them for map projectiles. **By id, not by `DefaultSiegeEngineTypes`** — `FireTrebuchet` there is a native bug that returns the plain trebuchet's object, so a real fire trebuchet matches nothing.

| Kind | Native ids |
|---|---|
| Ballista | `ballista`, `fire_ballista` |
| Stone catapult | `catapult`, `onager`, `bricole` |
| Pot catapult | `fire_catapult`, `fire_onager` |
| Trebuchet | `trebuchet`, `fire_trebuchet` — the besieger's alone |

**Hit chances and engine damage come from the data**, not from constants: `hit_chance`, `anti_personnel_hit_chance` and `damage` off `SiegeEngineType`, which RBM already overrides in `RBMXML/RBMCombat_siege_engines.xml`. Retuning that XML retunes this with no code change. Only the rates of fire and what a hit means for the men are hardcoded here.

**Catapults halve their rate in the assault**, on both sides and whatever they are shooting at: on the approach a crew loose at a fixed point they have been ranging on for days, but once the ladders are up the fight is moving and their own men are in it, so every shot has to be spotted and re-laid. Ballistas keep their rate (a bolt-thrower is spanned in the time a mangonel's arm is winched once); trebuchets already fire at that pace.

**And the ammunition runs out** — 15 shots per catapult or trebuchet, 30 per ballista, per battle. Every stone has to be cut, hauled and stacked beside the machine before the assault, and what is stacked there is what it has; a bolt is a smaller thing a crew carries by the armful, hence the larger sheaf. A miss spends one too. This is the archers' quiver rule applied to artillery, and it fixes the same failure: an engine that never runs dry decides a long battle by itself simply because the battle was long. The pile is restocked between assaults, so storming twice does not mean doing it the second time with half a catapult.

Ballistas were exempt at first, on the reasoning that nobody runs a bolt-thrower dry in an afternoon. The logs said otherwise: capping the catapults and not the ballistas simply handed the siege to the one engine still working. Across eight sieges the defending ballistas fired 340 of 470 shots, and in one of them 350 shots killed 241 men — near a quarter of the storming army — purely because they never stopped.

**On the wall.** A ballista looses one or two bolts a round at men only, and a bolt that finds someone puts a terrible wound in him — 50–100% of that man's own pool, so it is lethal to a whole man at the top of its band, survivable in good armour, and cumulative. It used to kill outright on every hit; that threw away everything the rest of the model says about armour for the engine that fires most often, and in one siege it meant 241 dead from 350 bolts. A defending catapult decides once, at the start, whether it is working on the besieger's equipment or on his men, and holds to it — until the ladders go up, when every engine on the wall turns to the men climbing it. Firing at men, a stone kills two or three at a stroke; a pot kills one and burns ten more for 20–60% of their pools apiece. There is no accuracy roll on the approach — a mangonel does not miss a column crossing open ground it has been ranging on for days — but there is one in the assault, because now it is dropping rocks near a fight its own men are in.

**Below it.** Every besieger's shot is 30% likelier to go wide. His ballistas work exactly as the wall's do. His heavy engines shoot at the defender's engines the whole battle through, assault included, and what they kill among the garrison they kill incidentally, rolled *separately* from the hit — a stone that misses a mangonel still lands somewhere. A stone catapult takes a man a quarter of the time; a pot kills the one it lands on and burns three to six more; a trebuchet fires every second round, hits timber hardest of anything on the field, and drops a rock big enough to take two or three men a third of the time.

**A broken engine stays broken.** It is removed from the campaign's own siege event, by slot — not through native's `BreakSiegeEngine`, which takes a *type* and would break the wrong one of a matched pair. So a ram lost to a mangonel has to be rebuilt before the next assault, and — because the assault widths are read from the survivors at the end of the approach — **a garrison that breaks the ram in time has genuinely narrowed the storm that follows.** That is the loop that makes the artillery matter.

Casualties go through the game's own `ApplySimulationDamageToSelectedTroop`, so they book against the right party, run RBM's wound pools, and let the surgeon decide dead or merely carried off, exactly like a casualty from a sword.

### Ammunition — counted in rounds, not blows

**A quiver does not empty per blow. It empties per minute.** A man looses arrows at a rate and keeps loosing until the quiver is out or the enemy is on him.

This is worth being precise about, because getting it wrong inverts the behaviour. Blows per man per round go as `N^-0.4`, so counting shots in *blows* meant twenty archers in a roadside skirmish burned their quivers dry before the fight was decided, while eight hundred archers in the great set-piece battle of the war shot from a full quiver from the first exchange to the last. Exactly the wrong way round: the skirmish is over in a minute and nobody empties anything; the long battle is precisely where the arrows run out.

So arrows are spent against the **round counter**: a man shoots for `AmmoRounds` (30) and then he is a man with a knife, and how many friends he brought has nothing to do with it. When the quiver is dry he draws from his melee arsenal — and his armour was never meant for that. (Raised from 14 alongside the skill-based defence system: once melee blows could be blocked, parried and countered, a battle took materially longer to decide, and a 14-round quiver had every archer in Calradia dry before the lines properly met.)

**Siege defenders never run dry.** A man on a wall is not shooting from his quiver; he is shooting from the town's arrow stores, stacked behind the parapet for exactly this. A besieger carries what he can climb a ladder with.

### Javelins

Half the infantry in Calradia carry a brace of throwing spears or a few throwing axes, and those are not melee weapons — he hurls them while the lines close and then draws steel. Auto-resolve has never once let him: they were either ignored entirely or, worse, treated as the weapon he swung for the whole battle, an axe thrown on an infinite loop.

A throw is a **missile** in every respect that follows: it goes to the mass of the man, it meets the *missile* shield block, and it does not touch the horse — a javelin goes where it was thrown, not into the animal's flank.

He hurls one per round, so **the bundle on his back is the number of rounds he can throw for.** Two javelins, two rounds. The approach across open ground runs four rounds — so he does terrible damage in the opening two, runs out, and spends the rest of the walk paying the closing penalty with nothing in his hand and the enemy line still coming. That is exactly what being a skirmisher is. There is no store to fall back on and no siege exception: nobody stockpiles javelins behind a parapet.

### The charge, and the horse under him

A charge is weight and speed, and a horseman has it **some of the time**. A lance at the gallop is a different thing from the same man hemmed in and hacking downward from a standing horse — and over a long fight he is both, by turns: he rides in, kills, is boxed in, backs out, finds room and comes again. Which of his blows carries the horse behind it is a matter of where he happens to be at that moment.

So the charge is a **coin, not a countdown**. A share of his blows land at the gallop and are paid the horse's charge in full; the rest are a man swinging a sword from a saddle. It does not decay, and it does not run out — a squadron with room to work is dangerous for as long as the battle lasts, which is what a squadron with room to work is.

**How large that share is depends on the ground.** A charge wants room to hit hard, which any open field gives it:

| Ground | Base | × boost | Effective |
|---|---|---|---|
| Open field — plain, steppe, desert | 0.5 | 0.9 | **0.45** |
| Trees and water — wood, river | 0.4 | 0.9 | 0.36 |
| A village street | 0.15 | 0.9 | 0.135 |
| A wall, a deck, a besieged gate | **0** | — | **0** |

Note this is *not* `KitingRoom`, though the two ask a related question and are read off the same terrain. Kiting asks whether a horse can run *away* and keep running; the charge asks only whether it has room to build speed into a crowd. A **village street** is where they part company: it gives a horse archer nothing at all — nobody kites between houses — while still leaving a lancer room for the odd charge. The naval and siege zeroes are not scaled by the boost: a wall and a deck have no charge to scale, and no horse on them either.

The `ChargeChanceBoost` was pulled from 1.2 to 0.9 when the charge became **unblockable** and started hitting for the mount's real weight: with those two changes the horse was charging too often and running whole battles single-handed.

**And a charge needs a crowd to break.** The ground's own figure is thinned by how many of the enemy are still **on their feet** — a charge into a thin screen of survivors is not the same act as a charge into a standing line. So as a side's foot are killed, the charges into it come less freely, and a small fight prints a small number and should. The battle log writes the opening figure for each side, with the foot count it was computed from.

It fires only once he has met somebody: while the lines are still closing a horseman has nobody to ride down, and a charge delivered into empty ground is not a charge. He finds the enemy *cavalry* in the skirmish, well before the foot are in reach, and his blows there roll the same coin.

**A charge costs the horse something, too.** Riding a horse into a standing man at speed hurts the animal — and riding it onto set spears hurts it a great deal more (`ChargeSpearRebound`). That toll is paid out of the horse's own pool, which is one of the ways a squadron grinds itself down over a long fight rather than charging fresh for ever.

For where a footman's blow actually goes — the horse or the man on it — see §5's *Horse or man*, which is now a roll of its own rather than an aside here.

**And a siege — or a ship — has no horses in it at all.** This is a stronger thing than kiting room going to nothing, and kept separate from it. A horse hemmed into a village street is still a horse — it cannot charge, but it is there, catching blows at the leg and dying before its rider does. A horse on a wall or a deck does not exist: the game brings none to a storm, and none aboard a boarding action, and a cavalry troop in either is a lance and a suit of barding with no animal under it. So there a lancer is dismounted outright — no charge, no barding counted at the leg, no horse to be killed first, and no riding out to meet the enemy cavalry in front. A wall, a deck and a village street all read zero kiting room, but they are not the same zero: the village keeps its horses and the other two have none.

### Braced steel

A spear set against a horse is the answer infantry have had to cavalry for three thousand years, and auto-resolve has never once let them use it. A braced polearm lands **half again as hard** on a horseman.

This one is a deliberate thumb on the scale and is *not* built into the baseline — see §8.

### The horse archer, and why nobody catches him

A horse archer is **not a cavalryman with a bow**, and auto-resolve has always modelled him as one: a mounted man who rides into the line, gets hemmed in, and is hacked down by the infantry around him like anybody else. That is the one thing a horse archer never does. His whole art is the *refusal of contact* — he shoots, and when the foot come at him he turns his horse and goes, and shoots again from somewhere they cannot follow. An infantry line does not kill horse archers. It chases them until it is exhausted, and they kill it.

He was always his own arm of service here (`HorseArcherType` — mounted *and* ranged, bucketed and baselined apart from the lancers), and he has always shot in the volley alongside the foot archers, and gone on shooting in every act of the battle for as long as his quiver holds out. What he could not do was **decline the melee**.

So a foot melee blow at a mounted archer who still has arrows lands at **a tenth of its worth**. Not because the spearman is bad — his spear is as good as it ever was, and if he braced it he still gets his full half-again — but because there is nobody standing in front of him to put it into. The tenth that gets through is the man caught turning, the horse gone lame, the pocket of ground with no way out.

**Three things end it, and only three:**

| | |
|---|---|
| **The quiver runs dry** | The clock the model already keeps (§ *Ammunition*). Out of arrows, he has no reason to keep his distance and no way to profit by it — he is a lightly armoured man on a tired horse, and now the infantry get their turn. This is the *design*: the way to beat horse archers is to outlast them. |
| **Cavalry** | A rider catches a rider. The exemption asks `!striker.IsMounted`, so it never applies to a horseman at all: lances land in full, at full charge. Which is exactly why every steppe army in history feared the other side's cavalry and very little else. |
| **The ground** | It is scaled by the battle's **kiting room**, read off the same terrain the volley length is read off. Open country — plain, steppe, desert, dune, snow — is the horse's at `0.9`: not quite absolute, because even on the steppe there are hollows and broken ground and horses that stumble. A forest or a river crossing cuts it to `0.4`: the lanes are short, the horse cannot run, and a man on foot with an axe gets his chance. A village street, a ship's deck, a breached wall: **zero**. Nobody kites up a siege ladder. |

Arrows find him regardless — a shaft does not care how fast his horse is — because only *melee from foot* requires him to be somewhere he can be reached.

Like the brace, this is a deliberate thumb on the scale and is *not* in the baseline (§8), so it survives the division rather than cancelling in it. In the blow-by-blow trace it prints as **`KITED`**, which is worth its own word: a column of "melee" blows each dealing a tenth of nothing looks like a broken model, and is in fact infantry doing the one thing infantry cannot do.

---

## 6a. Every soldier has hit points

Vanilla gives a line trooper **no health at all**. His life is one coin-flip per blow:

```csharp
else if (MBRandom.RandomInt(_selectedSimulationTroop.MaxHitPoints()) < damage)
```

Eight damage against a hundred hit points is not eight points off a bar — it is an **eight per cent chance the man is simply gone**, and a ninety-two per cent chance the blow never happened. Nothing accumulates. A veteran in plate who has been hacked at for twenty rounds is as fresh as the moment he arrived, and a recruit's lucky swing can kill a champion outright. Only a **hero** got a real pool (`AddHeroDamage`) — four lines higher up in the same method.

The game does know who each man is: `MapEventSide` keeps a `UniqueTroopDescriptor` for the soldier it has selected. So a pool is possible, and RBM keeps one, per battle, for every man.

The roll is not replaced — it is **bent**. `RandomInt(maxHitPoints)` returns `0 … maxHitPoints-1`, so rewriting `damage` in a prefix makes the outcome certain in either direction:

```
still standing  ->  damage = 0             ->  RandomInt(max) < 0    is never true, and he lives
worn through    ->  damage = maxHitPoints  ->  RandomInt(max) < max  is always true, and he falls
```

Everything downstream then runs exactly as it always did — the surgeon's survival roll deciding dead-or-wounded, the `BattleObserver`, the casualty books, the player's kill event. None of it is reimplemented, so none of it can drift. The XP survives too: `MapEvent` awards it from its *own* copy of the damage, and a `ref` prefix only rewrites the callee's.

**What it changes in play.** What collapses is the **variance**. Men die in the order they are worn down; no recruit fluke-kills a champion, and twenty grazes finally add up to a corpse instead of twenty separate near-misses. Battles get less swingy and the better army wins more reliably.

And every part of the equipment model bites harder, which is the real prize: armour that halves a blow now genuinely **doubles a man's life**, instead of halving a lottery ticket.

**The mean is *not* untouched, and that is on purpose.** A trooper's pool is widened by `LethalityHitPointScale` (1.25) beyond his native hundred, so the expected blows to kill him — `maxHP / damage` — rise by the same factor and each blow is proportionally **less** lethal. A single simulated blow was landing far harder than a real one, because the sim compresses a whole battle into a fraction of its blows and each therefore carries more; widening the pool walks that back toward what a man on the field actually endures. It is the honest knob for it: it sits downstream of the whole armour-and-kit model and distorts none of it — it only says how much a man can take before the last blow tells. It must stay ≥ 1, because the pool trick above relies on the pool never dropping below the hundred vanilla rolls against.

**A hero is exempt from all of it.** He keeps his own pool, unscaled: the lethality figure is a trooper knob, and a hero already had a real pool of his own to accumulate against. The scale is also *not* applied to his `MaxHitPoints()` for a second reason worth recording — that method also feeds the absolute per-blow cap (§2), so scaling him there would cap his blows against a pool he does not have, and a lord would die faster than the cap dial claimed.

**The trace prints what a man has left, and not what he started with.** It used to print both (`hp 52/100`), which was worth it while every trooper's pool was a flat hundred. It is not any more: the pool is the native hundred widened by the lethality scale and lifted again by his commander's hit-point perks, so the denominator now moves per troop, per party and per lord — and a column that changes its own meaning down the page is worse than no column. The pools are reported once, in full, in the perks block at the head of each battle (§6c).

**The commander's perks are folded in; the captain's are not there to fold.** A trooper's pool is his own hundred plus whatever his *party leader* has learned about keeping men alive — `ThickHides`, `HardyFrontline`, `WellBuilt`, `HardKnock`, `UnwaveringDefense`, `PickedShots`, and a doctor-lord's `MinisterOfHealth`, worth up to +28 and more for a well-led line. This is `SandboxAgentStatCalculateModel.GetEffectiveMaxHealth` transcribed, so a battle you press the button on agrees with a battle you fight by hand.

This block was once removed on the principle that a soldier's staying power should be his own frame, "not a bonus his captain carries." Both halves of that turned out to be wrong. **None of these is a captain perk** — every one is `PartyRole.PartyLeader`, and their own descriptions say *"to troops in your party"* where a captain perk always says *"in your formation"*; there is no hit-point perk anywhere in Bannerlord with a Captain slot. And the analogy to lifting tier and terrain does not hold: those were *proxies* the equipment model could measure directly and better, where a perk is a real effect with a real number that nothing else in the model says. Gated on `simulationPerkSystem`, with the captain system (§6c).

---

## 6b. Morale no longer prices the blow

Vanilla's `SimulateHit` ends by multiplying the blow through `CalculateSimulationMoraleEffects` — a side's standing morale makes its every blow land harder or softer. While the equipment model is pricing blows, that multiplier is **skipped entirely** (a prefix returning `false`; the method is `void`, so there is nothing to substitute and the damage simply passes through unmodified).

The reason is that it double-counts a thing this model now measures directly. What a blow *does* is decided by the kit that threw it, the armour it met, the training behind it and the pool it wears down. A side's campaign morale is not a fact about a spear, and letting it scale the spear taxes or subsidises every blow in the battle for a reason already accounted for elsewhere.

Be precise about the scope, because it is narrow: only morale's effect on a **blow's damage** is removed. Whether a side *breaks* is a different question entirely, and vanilla's answer to it stands untouched unless §6c is switched on.

With the equipment model off, the prefix returns `true` and vanilla's morale runs exactly as it always did — the battle is meant to be vanilla's own, morale and all.

---

## 6c. The rout

Gated behind `SimulationRoutEnabled`, and **off by default**.

Vanilla's auto-resolve routs a side only when its side morale reaches nearly zero — and that figure is the *standing campaign* `MobileParty.Morale`, which **never moves during the simulated fight**. So a side that is being annihilated is exactly as steady in round forty as it was in round one, and every auto-resolved battle in the game grinds on to the last man. That is not how battles end. Battles end when somebody runs.

**A side breaks when it is being butchered** — when it has lost a much larger *share* of the men it marched in with than the enemy has:

| | |
|---|---|
| `RoutLossGapThreshold` | **0.2** — it must have lost this much more of itself, in proportion, than its enemy |
| `RoutMinBeatenLoss` | **0.25** — and it must have bled a real quarter of itself away first |
| `RoutBaseChancePerRound` | **0.03** — a small base chance, re-rolled every round |
| `RoutSeverityScale` | **0.35** — plus a share growing with how far past the gap the butchery has gone (severity 0 at the threshold, 1 at a wipe) |
| `RoutMaxChancePerRound` | **0.45** — the ceiling |

Re-rolled each round, so a hopeless stand compounds toward a near-certain break while a merely bad one may yet hold. The figures are kept deliberately low: routs are meant to be the **exception**. A beaten side more often fights on and takes its losses than breaks and runs.

**Casualty share and not headcount, and this is the whole design.** The first version measured the live headcount ratio, and it broke in both directions:

- A battle that merely **starts** lopsided — 100 men against 25 looters — put the small side past the threshold in round *one*, with no casualties taken at all. Small parties evaporated before they could be wiped or captured.
- **Quality was invisible.** 80 knights against 300 recruits are below any live-headcount threshold from the opening and were routed *while winning*, handing the recruit horde the field. That is the wrong-winner failure class this model exists to avoid.

Casualty share reads the fight the right way round: the side bleeding out faster, **whatever its raw numbers**, is the one that breaks. The muster each side marched in with is caught in a prefix on the first round, before a man of it has fallen, and dropped when the event ends. A battle already under way when the patch loads takes its current strength as the baseline, which reads zero losses and simply means nobody routs until real casualties accumulate.

Two details that keep it honest: a side that *gained* men after the muster (reinforcements attaching mid-battle) would read a negative loss, so the fractions are clamped at zero; and a dead-even bleed breaks nobody.

**The break runs through vanilla's own `Route()`**, not through a reimplementation — which is what makes the fugitives *survive*, and the pursuit, the prisoners and the rewards all behave as the game intends. Ending the battle means setting `MapEvent.BattleState`, whose setter is internal and is reached by reflection; that is deliberate, because it is the same act vanilla's own rout performs and it is what fires `OnBattleWon` and finalises the event.

**Sieges are left to vanilla.** A storm is not a field a man can run off.

---

## 7. Heroes and the player

Party leaders and the player sit in the member roster as ordinary `CharacterObject`s, so they are mustered, priced from their real gear, struck, and wounded like anyone else.

**And the player is no longer spared his own auto-resolve.** Vanilla is built to protect him: a field battle he leads himself spawns him as a man on the ground, but a battle he "sends troops" to is mustered with `includePlayer = false`, and the muster's own gate — `CanTroopJoinBattle` — drops the main character on that flag alone. His soldiers fight and fall; he is simply not there. The AI lords beside him have no such shield: every one of them is mustered, swings, and can be wounded or killed in the same simulation.

That asymmetry is what this undoes. Once he is allowed into the muster he is an **ordinary hero** in it, the same as any lord — `SelectRandomSimulationTroop` can pick him to strike (and his hero-tier kit strikes hard) or to be struck, and `ApplySimulationDamageToSelectedTroop` rolls his survival exactly as it already rolls every other hero's. Sending his troops now means sending himself with them, risk and weight alike.

It only ever flips the `includePlayer = false` case, and only for the player character: a real field battle passes `true` and is untouched, no other troop's verdict is ever changed, and a player already wounded, routed or killed is left out — vanilla was right about him. With the overhaul off, vanilla's spared-player auto-resolve stands exactly as it was.

A lord is bucketed as **what he fights as** — cavalry, infantry, archer — and not into a bucket of his own. This is not a detail. The correction is a *ratio against the bucket's baseline*, so a bucket cancels precisely the differences **between** buckets: give heroes their own and a typical lord striking a typical infantryman divides to 1.0 by construction, and every scrap of his plate, his warhorse and his forty years of swordsmanship vanishes into his own baseline. What makes him a lord is the thing we are trying to *measure*, so it must not be hidden inside his own denominator.

A lord's kit is re-measured at the opening of each battle — his gear changes across a campaign but not in the middle of a fight.

---

## 8. The baselines

Everything above produces `actual`. It only means something divided by a **baseline**: what a typical man of the striker's arm does to a typical man of the victim's arm.

These are **measured from the game's own roster**, never guessed, and the log prints the whole table at the top of every file:

```
           vs      inf     arc     cav      HA     (troops)
  inf         11.65   15.56    8.97   13.52         185
  arc          9.12   16.87    4.22   10.15         123
  cav         14.61   19.09   10.23   15.79          58
  HA           12.5   21.46    5.61   14.31          27
```

Measuring rather than deriving is deliberate. A formula baseline with a different *slope* against tier than the model's own would silently hand one end of the tier range a bonus and tax the other, for no reason but the curve — and the whole correction is a ratio against this, so a baseline that is quietly wrong makes every blow quietly wrong without making any single blow *look* wrong. Measuring also means the model adapts to whatever items a mod loads.

**The population is line troops only.** `CharacterObject.All` is not a muster roll — it is every character the game has ever heard of, and it is full of people who never see a battle: villagers, townsfolk, tavern keepers, blacksmiths, musicians. They carry pitchforks and kitchen knives and were dragging down the very average that decides whether a real soldier is any good. Heroes are excluded too: Calradia fields a few hundred lords, nearly all mounted in the finest harness in the game, and leaving them in made the typical "cavalryman" a nobleman in plate. **A lord is measured against the line; he is not part of it.**

### What is in the baseline and what is not

This is the sharpest edge in the whole design, so it is stated explicitly.

Any term applied to **both** `actual` and `baseline` **cancels in the ratio and does nothing.** A term applied to only one is a deliberate thumb on the scale.

| Term | In `actual` | In `baseline` | Why |
|---|---|---|---|
| Armour, by zone, incl. the horse | yes | **yes** | The typical rider must be sitting on the same horse as the rider being struck. |
| Shield block (and the missile bonus) | yes | **yes** | Carrying the shield your fellows carry is simply what an infantryman does, and counts for nothing special. Carrying **none** means eating the blows they would have blocked. |
| Defence roll: block / parry / riposte | yes | **yes** | Same reasoning as the shield. Defending yourself is what a soldier does; the baseline man does it too, and what is being measured is the man who does it *better*. |
| Weapon pool, and the polearm preference | yes | **yes** | Which weapon a man draws is a fact about his kit. Otherwise a spearman would be measured against a baseline of men who never reached for theirs, and *every* infantry troop in Calradia would read as unusually good against horse. |
| **Brace bonus** | yes | **no** | A thumb. Auto-resolve has never let infantry set a spear, and it should. It must survive the division rather than cancel in it. |
| **Charge** | yes | **no** | Same. |
| **Volley / closing penalty** | yes | **no** | Same. |
| **Javelins** | yes | **no** | Same. |
| **Ranged miss roll** | yes | **no** | Same — and note it sits *above* the blow entirely: a missed shot is not a smaller blow, it is no blow at all. |
| **Horse-or-man roll** | yes | **no** | The reference tables ask what a matchup does to the **man**, and the horse is not part of that question — so they take the man every time. Only a live blow rolls it. |

Note that on the **absolute** path (§2, the default) there is no division by a baseline at all, so this table's "cancels in the ratio" logic does not bind there — the baselines remain what the matchup tables and §8's measurements are about, and the ratio path is where the cancelling matters.

---

## 9. Configuration

In the XML, under `/Config/RBMCampaign`:

| Key | Default | Effect |
|---|---|---|
| `SimulationEquipmentEnabled` | 1 | **Detailed auto resolve — the master switch.** `0` restores vanilla's auto-resolve *entirely*: tier-priced blows, vanilla morale (§6b), the hit-point lottery (§6a), arm-blind selection (§5a), no rout (§6c), and the spared player (§7). |
| `SimulationEquipmentPowerWeight` | 1 | The exponent on the correction **in ratio mode only** (§2). `1` = the model at face value; above 1 widens the gap between a well-found soldier and a ragged one. `0` is **the master switch off**, not merely a neutral weight — `SimulationEnabled` reads it. |
| `SimulationAbsoluteDamage` | 1 | Price a blow at its own real magnitude rather than as a ratio against its arm's baseline (§2). `0` restores the ratio-against-baseline path and its `0.1 … 8` clamp. |
| `SimulationAbsoluteScale` | 1 | The sole calibration dial of absolute mode: how a blow's real magnitude maps onto the hit-point pool. Raise to make blows bite harder. **Tune vs a paired log.** |
| `SimulationAbsoluteBlowCap` | 1.5 | The per-blow ceiling, as a multiple of the struck man's pool — what replaces the ratio clamp. `0` disables it. Absolute mode only. |
| `SimulationDefenseSystem` | 1 | Block / parry / riposte (§5). `0` restores the old fractional shield-skim — and with it `MeleeLandingExponentNoDefense`, since the two are calibrated together. |
| `SimulationShieldBlockChance` | 0.4 | What a *typical* shield-bearer turns aside **against missiles**; better shields scale up from here, poorer ones down. With the defence system on, melee does not read this at all. |
| `SimulationArmTargeting` | 1 | Phase- and arm-weighted selection of striker and struck (§5a). `0` restores vanilla's uniform random pick and the `VolleyFocus` path. |
| `SimulationRangedMissEnabled` | 1 | Let a fired shot miss before it is a blow (§5). `0` restores the shot that always arrives. |
| `SimulationRangedMissChance` | 0.35 | What an **untrained** man with a bow misses; every other accuracy term works on this. `0` disables the roll. **Interacts with `RangedLandingExponent` — see the calibration note in §5.** |
| `SimulationRoutEnabled` | **0** | Let a butchered side break and run (§6c). **Off by default**: vanilla's fight-to-annihilation is what the game does without RBM. |
| `SimulationPerkSystem` | 1 | Synthesise the formations auto-resolve lacks, appoint a captain over each by the game's own assignment rule (honouring the player's Order of Battle), and ask each for his real perks — replacing vanilla's flat *count* of the side commander's captain perks, which is then lifted back out so nothing is counted twice. Also restores the commander's hit-point perks (ThickHides, HardyFrontline, WellBuilt, HardKnock, UnwaveringDefense, PickedShots, MinisterOfHealth) to his men. `0` restores vanilla's count and drops the hit-point perks. |
| `SimulationSiegeDefenderEnabled` | 1 | Price the wall as better **dice** rather than the flat power bonus RBM has to neutralise: the besieged man turns aside more, and an exchange of shot is skewed by height. Off outside a siege. |
| `SimulationSiegeDefenderDefenseBonus` | 1.3 | Multiplier on the besieged man's shield-block, weapon-block and parry chances while a besieger strikes him (capped, so the wall is an edge and not invulnerability). `1` is no edge. |
| `SimulationSiegeRangedMissSkew` | 1.4 | Height advantage in an exchange of shot, symmetric about 1: the besieger firing up misses ×skew more, the defender firing down ×(2 − skew) less. `1` is no skew. |
| `SimulationRoundMinutes` | 10 | **The clock, and nothing else.** What one simulated round costs in campaign minutes; a siege assault keeps vanilla's own ratio and costs twice it. Changes no blow, casualty or phase. Vanilla bills a flat 30 for a round that was a whole chunk of the brawl, while an RBM round is a thin phase slice — so a fight needing more rounds (blows that miss, are blocked, or kill a horse) billed half an hour for each and locked two warbands together for a day. `0` restores vanilla's flat 30/60. |
| `SpectateBattlesEnabled` | **0** | Offer to open an AI-vs-AI battle as a real-time fight with no player agent on the field (§10). The map battle auto-resolves beside it and reaches its own verdict; the watched fight is a copy, written back nowhere. A measuring instrument. Needs RTSCamera. |
| `SpectateMinTroopsPerSide` | 100 | How big both sides must be before that offer is made. |
| `SimulationLoggingEnabled` | 1 | The battle log (§10): rosters, kit, matchup table, result. |
| `SimulationLogHits` | 1 | The blow-by-blow trace (§10). Needs the log above. A large battle runs to several thousand lines. |

And one knob that belongs to the field rather than the map, under `/Config/RBMCombat/Global`:

| Key | Default | What it does |
|---|---|---|
| `BattleHitLoggingEnabled` | 0 | Writes every blow of the battles you fight **yourself** to `logs/battles/`, in the same columns, so the model can be checked against a real fight (§10). |

Three toggles appear in the in-game config screen — *Detailed Auto Resolve*, *Auto Resolve Routing* and *Detailed Auto Resolve Logging*. The numeric knobs and the remaining feature gates are XML-only, deliberately.

The baselines and kits are rebuilt if `rbmCombatEnabled`, `SimulationShieldBlockChance`, `SimulationDefenseSystem`, `armorMultiplier`, `armorThresholdModifier` or `ThrustMagnitudeModifier` moves — every setting that is baked into them. `actual` is computed live on every blow, so a setting that changed under a stale baseline would skew every correction in the game while nothing anywhere looked broken.

**And the caches are cleared at the start of every session.** The per-battle and per-troop caches are static, keyed by `MapEvent`/`CharacterObject` identity, and are reclaimed only by the `MapEventEnded` of the battle that filled them. A save loaded while an event was live tears that campaign down *without ever ending its events*, so those entries — and any hero instances they hold — would sit orphaned for the life of the process, and the loaded battle would resume against a stale round clock. Every simulation cache therefore resets on `OnSessionLaunched`, which fires on a new game and on every load alike.

---

## 10. Checking it rather than trusting it

With logging on, every auto-resolved battle is written to `<configFolder>/logs/simulation/` **as it was actually fought** — not replayed, not averaged, not simulated a second time:

```
day 1085-016  ·  FieldBattle  ·  PlainBattle  ·  PLAYER
  attacker : 12 parties  (852 men)
  defender : 15 parties  (934 men)
  advantage: attacker 1.1, defender 1

  RESULT  winner attacker  ·  casualties  attacker 721, defender 933
```

Then three things, and they answer three different questions.

**The kit, as the model reads it** — item by item, with the raw numbers off each item. RBM builds its melee weapons from crafting pieces at runtime, so no XML on disk can be trusted to say what a weapon finally is; only this can. It also prints how many weapons are on the belt, how many kinds of arrow are in the quiver, and whether one of them is a spear.

**The matchup table** — every striker against every struck, worked through: armour met, shield block, actual, baseline, equipment ratio, tier term, correction. Note what this is and is not. It asks what a blow **would** do, *outside any battle* — so nobody in it is ever in a volley, nobody is ever out of arrows, and no shield is ever splintered. It is a reference table, not a record of the fight.

**The battle, blow by blow** — the fight itself, round by round, every man who swung, recorded as it landed:

```
  the battle, blow by blow -- as the game actually fought it (4192 blows):
    striker              -> struck                what        weapon        armor  blk%   vanilla  x corr  =  dealt   hp   result

    ── round 1  ·  VOLLEY -- the bowmen have the field, the foot are walking into it  ·  22 v 20
    A Aserai Archer      -> Imperial Recruit      shoot       Arrow         26.71 33.58     31.4    1.53     48.0   hp 52
    A Harami             -> Aserai Recruit        throw       Javelin       22.35 34.46     40.1    2.87    115.1   hp  0   DOWN

    ── round 5  ·  THE LINES HAVE MET  ·  19 v 14
    D Aserai Recruit     -> Nomad Bandit          braced      OneHandedPol  35.35     0     22.4    5.16    115.6   hp  0   DOWN
```

This is the only place the model's *story* can be read. The matchup table says what a blow would do in the abstract; it cannot tell you that the archers ran dry in round fifteen and spent the rest of the fight being cut down with knives in their hands, or that the lancers' charge was spent by round four and they were never dangerous again. `what` is what the man was actually doing — `shoot`, `throw`, `melee`, `CHARGE`, `braced`, or `closing` (in the volley with nothing to answer arrows with).

**Every blow here is a blow the game really struck**, taken from inside `SimulateHit` as it happened, and whether it put its man down is the game's own verdict. That is not pedantry, and it is why the log no longer replays anything. It used to: each battle was fought twenty more times with the model and twenty without, and the two averages set side by side. But a replay is a reimplementation of vanilla's loop, and a reimplementation **drifts** from the thing it reimplements. This one did — it gave heroes a line trooper's single roll where the game accumulates their damage, and quietly killed every lord in the log. It was answering, very confidently, a question about a battle nobody had fought.

The model has been designed wrong several times on reasoning that looked perfectly sound from the inside, and the log caught every one — but only ever where the log was recording the real thing. What is left here is exactly that, and nothing else.

### And the battle you fight yourself, in the same columns

Everything above is a claim about what a battle **is**: that the archers own the approach and are helpless once it is crossed, that a javelin is worth more than the man who throws it, that a charge is spent once, that armour and not tier decides who walks away. None of it can be argued into being true.

So `BattleHitLoggingEnabled` (in `/Config/RBMCombat/Global`, off by default) writes the battles you fight **on the field** to `<configFolder>/logs/battles/` — one file per battle, every blow as it landed, in the same columns:

```
    striker            -> struck                what     weapon           part    armor      raw   absorb    dealt   hp

    ── 0:22  ·  THE APPROACH -- no line has reached the other yet  ·  241 v 198
    A Battanian Fian     -> Imperial Legionary   shoot    Arrow            head    38.4     92.1     51.7     40.4   hp 60
    D Imperial Legionary -> Battanian Skirmisher throw    Javelin          body    24.1    121.6     37.2     84.4   hp 16   DOWN

    ── 1:15  ·  THE LINES HAVE MET (at 1:04)  ·  198 v 171
    A Battanian Wildling -> Imperial Legionary   melee    TwoHandedAxe     body    41.0     88.3     44.9     43.4   hp 12
```

Two differences, both forced by the thing being logged rather than chosen. A real battle has **seconds, not rounds**, so the headers count time. And it does not need a volley *model* — it simply notices the first melee blow anyone lands and says so, which is the real event the simulation's volley is an abstraction of. Everything logged before that line was landed across open ground by men who had not reached each other yet, and the tallies at the foot of the file say how much of the battle that was.

That is the check. What share of the killing did the bowmen really do; what was a javelin really worth; did the charge really decide anything — asked of the field, and answerable against the map.

---

## 11. Judgement, not measurement

Everything else is read from the game's own equations and item data, or measured from its own troop roster. These are the numbers that are somebody's opinion, gathered in one place so they can be argued with:

| | | |
|---|---|---|
| `SimulationShieldBlockChance` | 0.4 | What a typical shield turns aside — against missiles. |
| Max shield block | 0.65 | No shield makes a man safe. |
| Missile shield bonus | 1.35 | A shield is better against an arrow than a swordsman. |
| Shield defence base / skill | 0.45 / +0.30 | The melee defence chance behind an intact shield, before and across skill. |
| Weapon defence floor / skill | 0.20 / +0.18 | The same with only a weapon — about half a shield's across the range. |
| Defence chance cap | 0.75 | No defence makes a man untouchable. |
| Parry share, base / gap / cap | 0.20 / 0.5 / 0.6 | How many defences become counters, and how far out-skilling a man tilts it. |
| Cavalry vs archer defence | 0.25 | What is left of a bowman's block with a lance coming at him. No parry at all. |
| Mounted defence | 0.85 | A rider defends a little worse than the same man standing. |
| Archer vs ranged block | 0.5 | A man watching his own shot gets the board up late. |
| `SimulationRangedMissChance` | 0.35 | What an untrained man with a bow misses. |
| Ranged miss skill reduction | 0.6 | How much of his misses a fully trained bowman removes. |
| Ranged miss factors | 0.7 / 1.0 / 1.3 | Crossbow, bow, sling. |
| Volley / mounted shooter / mounted target | 1.25 / 1.25 / 1.4 | The long shot, the moving platform, the moving target. |
| Max miss chance | 0.8 | No dial pairing makes an arm that cannot hit anything. |
| Closing penalty | 0.08 | What a man with a sword achieves while walking into arrows. |
| Brace bonus | 1.6 | A spear set against a horse. |
| Anti-cavalry closing bonus | 0.5 | The struck horse's own momentum, fed back into the spear that met it. |
| Charge chance by ground | 0.5 / 0.4 / 0.15 | Open field, wood, village street — before the 0.9 boost. A wall and a deck are zero. |
| Charge strength | 0.02 | Per point of the mount's charge stat. A charge is unblockable, so this is the dial to pull if the horse comes out too strong. |
| Charge self-damage / spear rebound / armour rebound | 4 / 2.5 / 0.01 | What a charge costs the horse that makes it, and what running onto set spears or into plate costs it. |
| Horse hit chance | 0.45 / 0.15 / 0.22 | Foot melee, mounted melee, missile — whether a blow at a rider finds the animal. |
| Contact javelin throw chance | 0.25 | A skirmisher who reaches the melee still carrying javelins hurls one rather than drawing steel. |
| Horse archer evasion | 0.1 | What a foot melee blow is worth against a mounted archer who still has arrows. |
| Ammo rounds | 30 | A quiver, in rounds of steady loosing. |
| Shield capacity per man | 600 | Simulated damage an ordinary shield eats before it is kindling. Raised from 25 when a block started eating the whole blow. |
| Horse capacity | 260 | What a horse takes before it falls. **Needs re-tuning**: the horse-or-man roll cut a footman's wear on it from 100% of blows to 45%. |
| Lethality hit point scale | 1.25 | How far a trooper's pool is widened past his native hundred (§6a). |
| Landing exponents: melee / melee-no-defence / ranged / thrown / charge | 1.5 / 2 / 0.5 / 0.2 / 0.35 | How much of its full magnitude a landed blow of each kind is worth. **See the note below.** |
| Rout: gap / min loss / base / severity / cap | 0.2 / 0.25 / 0.03 / 0.35 / 0.45 | When a butchered side breaks, and how often (§6c). |
| Missile momentum remaining | 0.7 | An arrow has been slowing the whole way across the field. |
| Thrown momentum remaining | 0.85 | A javelin has not been slowing nearly as long. |
| Correction clamp | 0.1 … 8 | Ratio mode only. A real mismatch is meant to be lopsided. Not unbounded. |
| `SimulationAbsoluteBlowCap` | 1.5 | Absolute mode's replacement for it, against the struck man's own pool. |
| Hit-zone tables | §5 | Where blows land. |
| Arm-targeting keep-weights | §5a | Who swings, and at whom. |
| Volley rounds by terrain | §6 | How long the approach lasts. |
| Vanilla skill share | 0.3 | How much of a soldier's damage his training accounts for **under vanilla rules only** (saturating at 250 skill). The only number here that is an estimate rather than a reading. |
| Vanilla missile speed reference | 100 | The speed an ordinary bow throws at, under vanilla rules only. |

> **Two calibration debts are outstanding and are recorded here rather than buried.** `RangedLandingExponent` was calibrated with no miss roll upstream and now **double-counts** with `SimulationRangedMissChance` (§5); it wants re-measuring downward against a paired log. And `HorseCapacity` was set when every footman's blow wore the horse — the horse-or-man roll now sends 55% of them to the rider instead, so a squadron is grounded more slowly than the figure was tuned for.

---

## 12. Known limits

- **Reinforcements that join mid-battle** are not counted. The muster and the snapshot are taken at the top of the first round — the first moment the battle can be seen whole, and before a blow has landed. A party arriving at round five is not in the rosters the arrows, shields and horses were all measured against.
- **A stack, not a soldier.** The simulation hands us troop *types*, never individual men — a blow is struck by "an Imperial Archer", not by a man with eleven arrows left. So arrows, shields and horses are tracked per stack and scaled by headcount. That is an abstraction, and it is the honest limit of what the game gives us to work with.
- **The trace is the whole battle.** A large fight is several thousand lines. That is deliberate — the arrows running dry, the charge decaying, the shieldwall splintering all happen *late*, and a truncated trace hid exactly the half of the model only the trace could show. The log folder keeps its last ten files.

- **There is no A/B any more.** The log cannot tell you what this battle *would* have been without the model, because that battle does not exist and the only way to produce it was to reimplement vanilla's loop and run it — which is precisely how the log came to be lying. To compare, set `SimulationEquipmentEnabled` to `0` and fight the campaign; both logs are records of real battles.

- **Two dials are known to be uncalibrated, and are listed in §11 rather than left to be discovered.** `RangedLandingExponent` double-counts with the ranged miss roll, which was added above it after the exponent had been tuned to carry those misses itself; and `HorseCapacity` was tuned when every footman's blow wore the horse, where the horse-or-man roll now sends 55% of them to the rider. Both want a paired log.

- **The field frontage and the high-ground bias are new, and their figures are starting values.** The frontage (§6, *The field is only as wide as the real one*) is the higher-stakes of the two: it is what makes a lopsided win bloody, and how bloody is exactly what wants checking against the log — a run of big, one-sided auto-resolves, confirming the winner's casualties have climbed to a believable level without the fight dragging on the campaign clock. The defender's ×1.10 / ×0.90 high-ground magnitudes are milder and lower-risk, but likewise unmeasured. Both are model constants, off with `FieldFrontageEnabled = false` or by setting the magnitudes to 1.

- **A riposte deepens a wound; it never lands the kill in the instant.** The counter is applied to the attacker's pool from inside the blow he threw, and is deliberately allowed to accumulate *past* the pool without downing him — the ordinary worn-through path finishes him on his next blow instead. Realising the kill reentrantly would drive the casualty books, the observer and the downed-marker in the middle of another blow's bookkeeping, which is the class of drift this whole file was rebuilt to avoid. A riposte is also never itself blocked or parried (there is no recursion), and **a hero is never wounded by one at all** — he carries his own pool rather than the trooper dictionary, so his counter is printed in the log and left un-applied rather than reimplement the hero-wounding path from inside a blow.
