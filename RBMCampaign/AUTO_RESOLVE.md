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

A Harmony postfix on `SimulateHit` multiplies vanilla's number by a **correction**:

```
                    ⎛  actual / baseline  ⎞ weight
correction = clamp  ⎜  ─────────────────  ⎟          , 0.1 … 8
                    ⎝      tierTerm       ⎠

actual    = damage THIS soldier's kit does to THAT soldier's armour
baseline  = damage a TYPICAL soldier of the striker's arm does to a TYPICAL
            soldier of the victim's arm      (measured, not guessed — see §8)
tierTerm  = pow( VanillaTierPower(striker) / VanillaTierPower(struck), 0.7 )
weight    = SimulationEquipmentPowerWeight
```

**Tier is replaced, not adjusted.** Dividing by `tierTerm` cancels vanilla's tier base out of the blow entirely, and the equipment ratio is put in its place. This is deliberate: a tier was only ever shorthand for *what kit does he carry and how well is he trained*, and both of those are now measured directly. Leaving vanilla's tier term in would charge for the same thing twice — and it was the reason a recruit in mail could not out-fight a looter in rags by more than the 1.41× his tier number allowed.

What survives untouched is `(1 + leaderModifier + contextModifier)`, which sits on both sides of vanilla's ratio and cancels. So **the terrain table, the arm-vs-arm table, the captain's perks, the leader's Tactics, morale and routing all still apply exactly as before.** This changes what a blow *does*, not how a battle is shaped around it.

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
- **Armour is roughly two and a half times more protective than in vanilla,** and a heavy harness can stop a cut outright, letting nothing through but the shock of it.

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

Bannerlord keeps armour in four zones, so those are the four we can weight. Each row is a distribution and sums to 1.

| | Head | Body | Arm | Leg |
|---|---|---|---|---|
| Foot vs foot | 0.20 | 0.55 | 0.20 | 0.05 |
| Foot vs mounted | 0.05 | 0.40 | 0.10 | **0.45** |
| Mounted vs foot | **0.30** | 0.50 | 0.15 | 0.05 |
| Mounted vs mounted | 0.20 | 0.50 | 0.20 | 0.10 |
| Missile (any target) | 0.15 | 0.60 | 0.15 | 0.10 |

Two footmen are eye to eye, so it is the chest and shoulders and arms that catch it and the legs almost never — a man does not stoop to hack at ankles. But a man on foot hacking upward at a horseman finds the rider's **legs and lower body** at his eye level, while the rider cutting downward finds the footman's **head and shoulders**. This is why barding is worth a great deal against infantry and nearly nothing against another lancer: the infantry are the ones swinging where it is. An arrow ignores all of this and goes to the mass of the man.

The horse's own barding and bulk answer at the leg and body — but they are kept **apart** from the rider's armour, because a horse can be killed and a dead one answers nothing.

### Shields

A shield is not armour and does not blunt anything. It stops the blow dead or it doesn't. So a shield-bearer turns aside a *share* of everything thrown at him:

```
block = SimulationShieldBlockChance · sqrt(shieldQuality / typicalShieldQuality)     capped at 0.65
```

The square root matters. Taken flat, a Pavise — which is a wall of wood and scores accordingly — sat on the cap while a Norse round shield turned 21%, so the shieldwall infantry that shields exist *for* came off worse than a crossbowman hiding behind a board. A better shield should stop more blows than a poorer one; it should not stop three and a half times as many. Most of what stops a blow is the man, and men do not differ fourfold.

Shield quality is RBM's own reckoning (`ItemValuesTiers.CalculateShieldTier`): durability, the armour of its face, and how much of him it covers. A steel round shield and a wooden adarga are the same span and the same 60 length and differ **five-fold** in hit points, and that is what separates them.

**Against arrows the same shield does about a third better again** (×1.35). An arrow comes from one known direction and arrives on its own; a man gets the board up and it sticks there. A swordsman feints, comes round the edge, and waits for the shield to drop. This is the whole reason a line advances under fire from behind its shields.

Shields **degrade**. What a shield stops, it eats, and a wooden board that has taken thirty mace-blows is kindling. The item's own hit points set the spread against a reference shield, so a steel shield really does outlast an adarga.

---

## 6. The battle has a clock

The tick allocation is called once at the top of every round, and it is the **only** place the simulation ever says a round has turned. A blow cannot say it — a blow does not know how many came before it. So the battle's clock is read from there, and everything spent *over* a battle rather than in an instant hangs off it.

### The volley

While the lines are closing, a bowman is doing the only thing he is for, and the man walking toward him is doing nothing at all but walking. This is the whole of what auto-resolve never modelled: it threw archers into a melee brawl at contact range and wondered why they were bad.

How long the approach lasts is a question about the ground:

| Context | Volley rounds |
|---|---|
| Siege assault | 5 |
| Plain / steppe / desert / dune / snow | 4 |
| Siege (not an assault) | 4 |
| River, sea, village, naval raid | 2 |
| **Forest** | **1** |

Across an open plain a man walks a long way under arrows; in a wood he is on you before the second shaft is nocked. Storming a wall is the longest walk of all, and everyone on it is shooting at you the whole way.

During the volley, a man who is **not** shooting or throwing pays a **closing penalty** — he is walking, into arrows, and achieving very nearly nothing.

### Ammunition — counted in rounds, not blows

**A quiver does not empty per blow. It empties per minute.** A man looses arrows at a rate and keeps loosing until the quiver is out or the enemy is on him.

This is worth being precise about, because getting it wrong inverts the behaviour. Blows per man per round go as `N^-0.4`, so counting shots in *blows* meant twenty archers in a roadside skirmish burned their quivers dry before the fight was decided, while eight hundred archers in the great set-piece battle of the war shot from a full quiver from the first exchange to the last. Exactly the wrong way round: the skirmish is over in a minute and nobody empties anything; the long battle is precisely where the arrows run out.

So arrows are spent against the **round counter**: a man shoots for `AmmoRounds` (14) and then he is a man with a knife, and how many friends he brought has nothing to do with it. When the quiver is dry he draws from his melee arsenal — and his armour was never meant for that.

**Siege defenders never run dry.** A man on a wall is not shooting from his quiver; he is shooting from the town's arrow stores, stacked behind the parapet for exactly this. A besieger carries what he can climb a ladder with.

### Javelins

Half the infantry in Calradia carry a brace of throwing spears or a few throwing axes, and those are not melee weapons — he hurls them while the lines close and then draws steel. Auto-resolve has never once let him: they were either ignored entirely or, worse, treated as the weapon he swung for the whole battle, an axe thrown on an infinite loop.

A throw is a **missile** in every respect that follows: it goes to the mass of the man, it meets the *missile* shield block, and it does not touch the horse — a javelin goes where it was thrown, not into the animal's flank.

He hurls one per round, so **the bundle on his back is the number of rounds he can throw for.** Two javelins, two rounds. The approach across open ground runs four rounds — so he does terrible damage in the opening two, runs out, and spends the rest of the walk paying the closing penalty with nothing in his hand and the enemy line still coming. That is exactly what being a skirmisher is. There is no store to fall back on and no siege exception: nobody stockpiles javelins behind a parapet.

### The charge, and the horse under him

A charge is weight and speed, and it is **spent once**. A lancer at the gallop is a different thing from the same man five minutes later, hemmed in and hacking downward from a standing horse. So the horse's `ChargeDamage` is paid in full in the first round and decays to nothing by the fourth.

And a footman hacking upward at a rider is mostly hacking at the **horse**. Horses die. When one does, its rider is a man on foot in cavalry harness: he loses the barding that was answering those blows, and the blows start finding his head instead of his legs.

### Braced steel

A spear set against a horse is the answer infantry have had to cavalry for three thousand years, and auto-resolve has never once let them use it. A braced polearm lands **half again as hard** on a horseman.

This one is a deliberate thumb on the scale and is *not* built into the baseline — see §8.

---

## 7. Heroes and the player

Party leaders and the player sit in the member roster as ordinary `CharacterObject`s, so they are mustered, priced from their real gear, struck, and wounded like anyone else.

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
| Weapon pool, and the polearm preference | yes | **yes** | Which weapon a man draws is a fact about his kit. Otherwise a spearman would be measured against a baseline of men who never reached for theirs, and *every* infantry troop in Calradia would read as unusually good against horse. |
| **Brace bonus** | yes | **no** | A thumb. Auto-resolve has never let infantry set a spear, and it should. It must survive the division rather than cancel in it. |
| **Charge** | yes | **no** | Same. |
| **Volley / closing penalty** | yes | **no** | Same. |
| **Javelins** | yes | **no** | Same. |

---

## 9. Configuration

In the XML, under `/Config/RBMCampaign`:

| Key | Default | Effect |
|---|---|---|
| `SimulationEquipmentEnabled` | 1 | **Detailed auto resolve.** `0` restores vanilla's tier-only model entirely. |
| `SimulationEquipmentPowerWeight` | 1 | The exponent on the whole correction. `0` = vanilla. `1` = the model at face value. Above 1 widens the gap between a well-found soldier and a ragged one. |
| `SimulationShieldBlockChance` | 0.4 | What a *typical* shield-bearer turns aside. Better shields scale up from here, poorer ones down. |
| `SimulationLoggingEnabled` | 1 | The A/B log (§10). |
| `SimulationLogSamples` | 20 | Replays per battle, per side of the comparison. |

Only the two **enabled/disabled** toggles appear in the in-game config screen — *Detailed Auto Resolve* and *Detailed Auto Resolve Logging*. The numeric knobs are XML-only, deliberately.

The baselines and kits are rebuilt if `rbmCombatEnabled`, `SimulationShieldBlockChance`, `armorMultiplier`, `armorThresholdModifier` or `ThrustMagnitudeModifier` moves — every setting that is baked into them. `actual` is computed live on every blow, so a setting that changed under a stale baseline would skew every correction in the game while nothing anywhere looked broken.

---

## 10. Checking it rather than trusting it

With logging on, every auto-resolved battle is written to `<configFolder>/logs/simulation/`, replayed **twenty times with the model and twenty times without**, from the same opening rosters:

```
day 1085-016  ·  FieldBattle  ·  PlainBattle  ·  PLAYER
  attacker : 12 parties  (852 men)
  defender : 15 parties  (934 men)

  ACTUAL  winner attacker  ·  casualties  attacker 721, defender 933

  replayed 20x each:
                     atk win%   atk losses   def losses
    BASE (vanilla)         40%        830.1        891.2
    RBM  (model on)        60%        782.7        893.4
    delta                 +20%        -47.4         +2.2
```

followed by every troop's kit as the model reads it — item by item, with the raw numbers off each item, because RBM builds its melee weapons from crafting pieces at runtime and no XML on disk can be trusted to say what a weapon finally is — and then **every blow of the battle, worked through**: armour met, shield block, actual, baseline, equipment ratio, tier term, correction.

A battle cannot be fought twice — the real one mutates the rosters it resolves — so it is *replayed* instead. The replay does not reproduce perks, morale drift, or the wounded-versus-killed split. **That is fine, and it is the point: both replays are wrong in exactly the same way, so what differs between them is the model and nothing else.**

The model has been designed wrong several times on reasoning that looked perfectly sound from the inside, and the log caught every one. It is made to account for itself rather than be argued with.

---

## 11. Judgement, not measurement

Everything else is read from the game's own equations and item data, or measured from its own troop roster. These are the numbers that are somebody's opinion, gathered in one place so they can be argued with:

| | | |
|---|---|---|
| `SimulationShieldBlockChance` | 0.4 | What a typical shield turns aside. |
| Max shield block | 0.65 | No shield makes a man safe. |
| Missile shield bonus | 1.35 | A shield is better against an arrow than a swordsman. |
| Closing penalty | 0.08 | What a man with a sword achieves while walking into arrows. |
| Brace bonus | 1.6 | A spear set against a horse. |
| Charge rounds | 3 | How long a charge is worth anything. |
| Ammo rounds | 14 | A quiver, in rounds of steady loosing. |
| Shield capacity per man | 25 | Simulated damage an ordinary shield eats before it is kindling. |
| Horse capacity | 260 | What a horse takes before it falls. |
| Missile momentum remaining | 0.7 | An arrow has been slowing the whole way across the field. |
| Thrown momentum remaining | 0.85 | A javelin has not been slowing nearly as long. |
| Correction clamp | 0.1 … 8 | A real mismatch is meant to be lopsided. Not unbounded. |
| Hit-zone tables | §5 | Where blows land. |
| Volley rounds by terrain | §6 | How long the approach lasts. |
| Vanilla skill share | 0.3 | How much of a soldier's damage his training accounts for **under vanilla rules only** (saturating at 250 skill). The only number here that is an estimate rather than a reading. |
| Vanilla missile speed reference | 100 | The speed an ordinary bow throws at, under vanilla rules only. |

---

## 12. Known limits

- **Reinforcements that join mid-battle** are not counted. The muster and the snapshot are taken at the top of the first round — the first moment the battle can be seen whole, and before a blow has landed. A party arriving at round five breaks the "opening rosters" premise the whole replay rests on, so there is no honest snapshot to take.
- **A stack, not a soldier.** The simulation hands us troop *types*, never individual men — a blow is struck by "an Imperial Archer", not by a man with eleven arrows left. So arrows, shields and horses are tracked per stack and scaled by headcount. That is an abstraction, and it is the honest limit of what the game gives us to work with.
- **The closing penalty is partly swallowed by the clamp floor.** `0.08` applied before a floor of `0.1` means the effective penalty is `0.1` for most infantry.
