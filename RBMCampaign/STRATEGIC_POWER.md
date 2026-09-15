# RBM Campaign — Strategic Party Power

The **displayed strength** of a party: the single number the game shows the player
and the AI uses to decide whether to fight, flee, besiege, or run. It lives in
[`Power/StrategicTroopPower.cs`](Power/StrategicTroopPower.cs) and works by
Harmony-prefixing `DefaultMilitaryPowerModel.GetPowerOfParty`.

> This is **not** the number that produces casualties. That is a separate system
> (auto-resolve blow power, `Simulation/SimulationEquipmentPower.cs`) with its own
> constants. The two agree in *direction* but not *magnitude*, by design — never
> tune one from the other's numbers.

---

## 1. Why it patches `GetPowerOfParty`, not the per-troop method

The obvious target would be `GetDefaultTroopPower` — vanilla's per-troop tier value.
RBM deliberately avoids it: that same tier term is the divisor the **auto-resolve**
model cancels out, so patching it would price equipment twice. Instead RBM keeps
vanilla's whole party loop intact and swaps only the **base per-man value**.

**Gate** (`StrategicTroopPower.cs:292-300`):

```
Enabled = rbmCampaignEnabled && strategicPowerEnabled && Campaign.Current != null
```

---

## 2. The party formula

`TryGetPowerOfParty` (`StrategicTroopPower.cs:457-546`), per troop stack:

```csharp
int healthy = element.Number - element.WoundedNumber;      // wounded men do not fight

float power = PowerOf(troop);                               // equipment-aware, §3
if (power <= 0f)
    power = model.GetDefaultTroopPower(troop);              // unreadable troop → vanilla fallback

power *= HealthFactorOf(troop, party);                     // commander-perk HP bonus, §5

float contextMod = estimated ? 0f : model.GetContextModifier(troop, side, context);
float leaderMod  = (party.LeaderHero != null) ? party.LeaderHero.PowerModifier : 0f;

float perMan = power * (1f + leaderMod + contextMod);
total += healthy * perMan;

// ... after the loop ...
result = total * morale;                                   // morale applied once, at the end (§4)
```

- **`leaderMod`** — vanilla's own `LeaderHero.PowerModifier`, left exactly as vanilla
  computes it. It is worth almost nothing (it counts only the two `PrimaryRole ==
  Captain` perks), but RBM preserves the `(1 + leader + context)` shape rather than
  fixing it here.
- **`contextMod`** — vanilla's terrain-vs-arm table; dropped for `estimated` prices,
  where the terrain is unknown.

---

## 3. Pricing one man — `PowerOf` → `Measure` → `PowerOfSet`

`PowerOf` (`571-629`) is cached per `CharacterObject` (heroes re-measured daily).
`Measure` (`721-784`) averages `PowerOfSet` over **all** of the troop's battle
equipment sets, then divides by a scale constant:

```
detail.Power = (sum over sets of PowerOfSet) / setCount / PowerScale     // PowerScale = 272f
```

### PowerScale = 272 is measured, not chosen

It is **re-measured, never re-picked**, after any offence, armour or passive retune:
`k = men-weighted Σ(men × pm_new) / Σ(men × pm_old)` over matched troops, and
`newScale = oldScale × k`. It has moved 197 → 260 (when the passive divisor began
tracking `100/armorMultiplier`, doubling armour's weight) → 272 (when the mount became a
proportional term). A stale value here silently rescales every AI fight/flee decision in
the game.

The raw model output lands in the low hundreds; dividing by it maps the result back onto
vanilla's `0.40 → 2.56` power range, so hardcoded AI thresholds elsewhere in the game
(the 1000 army-power floor, siege dampers) still behave. It also keeps the
`GetDefaultTroopPower` fallback (which returns a vanilla 0.4–2.56 value) in the same
units as everyone else.

### PowerOfSet — three multiplying stages of one blow

`PowerOfSet` (`793-867`):

```
product = offense × activeFactor × passiveFactor
power   = product + product × MountFractionOf(set)
```

First the blow must not be turned aside, then it must get through the armour — so
what each stage buys **multiplies** rather than adds.

The mount is a **proportional** term, not a flat addition: a horse is worth a share of the
rider it carries, scaled by how survivable the animal is (its own hit points plus its
barding, against a barded warhorse as the yardstick). Because the share tracks the horse
rather than the base, lighter cavalry gain less than armoured — a bare mount is worth about
+18% to its rider, a knight's +30%, a cataphract's +34%. Making it flat-additive inverted
that ordering, which is why it is written this way.

**Offense** (`814-829`):
```
offense = melee
if shooter:                              # shooter = has ranged AND the game FIELDS him as ranged
    blended = RangedShare·ranged + (1 − RangedShare)·melee
    offense = max(offense, blended)      # a bow never makes a man WORSE than his sword
    offense *= RangedOffenseWeight
offense *= 1 + ChargeWeight·chargeDamage # cavalry charge bump
```

**Active factor** — blows turned aside outright; a skill-priced thing he *does*, and a
shield is nearly its whole worth (`831-837`):
```
skillFrac    = clamp(MeleeSkill / SkillSaturation, 0, 1)
active       = hasShield ? clamp(ShieldDefenseBase + ShieldDefenseSkillCoeff·skillFrac, 0, cap)
                         : clamp(WeaponDefenseFloor + WeaponDefenseSkillCoeff·skillFrac, 0, cap)
activeFactor = (1 / (1 − active)) ^ ActiveDefenseDamping
```

**Passive factor** — what is left of a blow he did *not* turn aside; also where a
shield stops an arrow (`839-848`):
```
weighted = head·0.16 + neck·0.03 + torso·0.44 + shoulder·0.12 + arm·0.14 + leg·0.11
weighted += ShieldPassiveWeight·shieldTier
armorConstant = rbmCombat ? ArmorConstant / armorMultiplier : ArmorConstant
passiveFactor = 1 + weighted / armorConstant
```

**Barding is not here.** It is the horse's armour, and it is priced once, in the mount term
above. **And the divisor tracks the combat module**: RBM's own armour equation divides a blow
by `100/(100 + armor·armorMultiplier)`, so the passive term only agrees with a real blow when
its divisor is `100/armorMultiplier`. At the default multiplier of 2 that doubles armour's
weight — exactly the price of protection RBM charges on the field. With RBM Combat off, the
flat `ArmorConstant` is used.

### Two subtleties baked in

- **"Shooter" is about how the game fields the troop** (`IsRangedTroop`), not whether a
  bow is in his baggage — otherwise a mounted lord carrying a bow would be mispriced as
  a full-time archer.
- **A bow never lowers a man's score** below his sword: the ranged blend can only lift
  him (`offense = max(offense, blended)`).

---

## 4. Morale factor

`MoraleOf` (`552-563`), applied to the party total after the loop:

| Case | Factor |
|---|---|
| Non-mobile party | `1.0` |
| Estimated | `MBMath.Map(morale, 20, 40, 0.7, 1.0)` |
| Live | `morale < 30 ? 0.7 : 1.0` |

---

## 5. Commander perks → staying power

`HealthFactorOf` (`1308-1316`) = `CommandedHealth / 100`, multiplied into each man's
power. `CommandedHealth` comes from `SimulationTroopHitPoints.BuildCommandedHealth`
(`231-291`), which transcribes `GetEffectiveMaxHealth` — the party leader's HP-raising
perks (`ThickHides`, `HardyFrontline`, `WellBuilt`, `MinisterOfHealth`, …).

The strategic model applies **only** this party-scoped commander track. Captain perks
are ignored here — they need battle formations, which do not exist on the campaign map
(`StrategicTroopPower.cs:53-71`).

---

## 6. Tuning constants (in code, not the config screen)

These are hardcoded in `StrategicTroopPower.cs` (lines `96-288`):

| Constant | Value | Meaning |
|---|---|---|
| `PowerScale` | `272f` | maps model output onto vanilla's power range. **Measured, not chosen** — re-derive it after any retune. |
| Zone weights H/N/T/Sh/A/L | `0.16 / 0.03 / 0.44 / 0.12 / 0.14 / 0.11` | hit-share per armour zone |
| `ArmorConstant` | `100f` | armour → passive-factor divisor, **divided by `armorMultiplier` when RBM Combat is on** |
| `ShieldPassiveWeight` | `4f` | shield's passive (arrow-stopping) worth |
| `ReferenceMountSurvival` | `440f` | the barded-warhorse yardstick the mount share is scaled off |
| `MountBonusAtReference` | `0.43f` | share of his own power a rider gains at that yardstick |
| `BardingToHealth` | `2f` | how barding converts into the horse's survivability — the only place barding is priced |
| `BestWeaponWeight` | `0.7f` | weight of the best weapon in a kit |
| `RangedShare` | `0.7f` | fraction of battle an archer spends shooting |
| `RangedOffenseWeight` | `1.35f` | archer offense premium |
| `SkillSaturation` | `250f` | skill value at which the defense curve saturates |
| `ChargeWeight` | `0.004f` | cavalry charge-damage weight |
| `ActiveDefenseDamping` | `0.4f` | exponent damping the turn-aside factor |

### What config actually changes strategic power

- `strategicPowerEnabled` — on/off.
- `rbmCombatEnabled` and `OneHandedThrustDamageBonus` — switch the underlying offense
  model and trigger a cache rebuild (`EnsureCacheFresh`, `348-364`).

Everything else — the auto-resolve dials (`simulationAbsoluteScale`,
`simulationEquipmentPowerWeight`, etc.) — affects **casualties**, not this displayed
number.

---

*Line numbers are anchors as of this writing, not guarantees — re-grep the method name
if one has drifted.*
