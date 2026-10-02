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
vanilla's party loop and swaps the **base per-man value** (and drops vanilla's field-terrain
modifier, §2).

**Gate** (`StrategicTroopPower.cs:329-337`):

```
Enabled = rbmCampaignEnabled && strategicPowerEnabled && Campaign.Current != null
```

---

## 2. The party formula

`TryGetPowerOfParty` (`StrategicTroopPower.cs:557-655`), per troop stack:

```csharp
int healthy = element.Number - element.WoundedNumber;      // wounded men do not fight

float power = PowerOf(troop);                               // equipment-aware, §3
if (power <= 0f)
    power = model.GetDefaultTroopPower(troop);              // unreadable troop → vanilla fallback

power *= HealthFactorOf(troop, party);                     // commander-perk HP bonus, §5

bool siege = context == MapEvent.PowerCalculationContext.Siege;
float contextMod = siege ? model.GetContextModifier(troop, side, context) : 0f;
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
- **`contextMod`** — vanilla's terrain-vs-arm table, **kept for a siege only**. Field
  terrain is dropped in every context: it is a per-arm guess (archers weak in a wood,
  infantry strong there) that double-counts what this model already prices in the man —
  it once halved a noble archer in a forest down to a Looter's level. A siege is a real
  fact about the fight, so its context stays in the strength the AI reads. (The
  auto-resolve blow lifts the context out everywhere, siege included — a different
  number; see `POWER_COMPUTATION.md` §6.)

The stack loop also feeds the encounter-screen tooltip: while `TryExplainParty` is on the
stack it captures each priced stack (troop, healthy count, total with morale applied), so
the tooltip's rows are the pricing's own and add up to the bar (§7).

---

## 3. Pricing one man — `PowerOf` → `Measure` → `PowerOfSet`

`PowerOf` (`812-870`) is cached per `CharacterObject` (heroes re-measured daily, and
hero entries nobody has asked about for a day are swept out).
`Measure` (`967-1034`) averages `PowerOfSet` over **all** of the troop's battle
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
the game. Its own doc comment names what should trigger a re-measure: a change to the
offense model (`rbmCombatEnabled`, `OneHandedThrustDamageBonus`), to `armorMultiplier` or
`armorEffectivenessMultiplier`, or a re-cut of the tuning constants (§6).

The raw model output lands in the low hundreds; dividing by it maps the result back onto
vanilla's `0.40 → 2.56` power range, so hardcoded AI thresholds elsewhere in the game
(the 1000 army-power floor, siege dampers) still behave. It also keeps the
`GetDefaultTroopPower` fallback (which returns a vanilla 0.4–2.56 value) in the same
units as everyone else.

### PowerOfSet — three multiplying stages of one blow

`PowerOfSet` (`1043-1130`):

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
that ordering, which is why it is written this way. `MountFractionOf` (`1140-1150`):

```
survival      = horse HitPoints + HitPointBonus + BardingToHealth·barding
mountFraction = (survival / ReferenceMountSurvival) · MountBonusAtReference     // 0 on foot
```

**Offense** (`1064-1079`):
```
offense = melee
if shooter:                              # shooter = has ranged AND the game FIELDS him as ranged
    blended = RangedShare·ranged + (1 − RangedShare)·melee
    offense = max(offense, blended)      # a bow never makes a man WORSE than his sword
    offense *= RangedOffenseWeight
offense *= 1 + ChargeWeight·chargeDamage # cavalry charge bump
```

Where `melee` and `ranged` come from:

```
# MeleeOffense (1156-1214): shields and launchers/ammo/thrown are skipped
melee  = BestWeaponWeight·best + (1 − BestWeaponWeight)·mean          over his melee weapons
score  = rbmCombat ? ClassCeiling(class) · (1 + SkillOffenseSpread·skillFrac) · Penetration(damageFactor)
                   : max(SwingDamage, ThrustDamage) · (1 + SkillOffenseSpread·skillFrac)
Penetration = 1 + PenetrationWeight·(sqrt(damageFactor) − 1)

# RangedOffense (1301-1370): his highest-energy launcher, and only if he carries matching ammo
ranged = RangedEnergyScale · LauncherEnergyOf(launcher) · (1 + SkillOffenseSpread·skillFrac)
LauncherEnergyOf = 0.5 · (drawWeight·4.448) · powerstroke · efficiency     # joules, not tier
                   (÷ CrossbowReloadDivisor for a crossbow; a sling is a flat SlingEnergy)
```

`skillFrac` is the weapon's own skill over `SkillSaturation`, clamped to 0…1. Under RBM
Combat the listed damage is ignored: the blow is the class ceiling (`ClassCeiling`,
`1257-1290`, mirroring the `max · 4.6` / `· 4` clamps of `RBMConfig/Shared/SkillDamage.cs`
— keep the two in step), so a great axe out-ranks a hand axe even though RBM's item tier
divides it by 1.3. `MissileSpeed` is RBM's **draw weight in pounds**; the powerstroke is
25″ for a bow and 20″ for a crossbow, the efficiency 0.90 (bow) / 0.835 (a `long_bow`
usage) / 0.88 (crossbow). The arrow itself is not priced — only its presence is checked.

**Active factor** — blows turned aside outright; a skill-priced thing he *does*, and a
shield is nearly its whole worth (`1081-1087`):
```
skillFrac    = clamp(MeleeSkill / SkillSaturation, 0, 1)
active       = hasShield ? clamp(ShieldDefenseBase + ShieldDefenseSkillCoeff·skillFrac, 0, cap)
                         : clamp(WeaponDefenseFloor + WeaponDefenseSkillCoeff·skillFrac, 0, cap)
activeFactor = (1 / (1 − active)) ^ ActiveDefenseDamping
```

**Passive factor** — what is left of a blow he did *not* turn aside; also where a
shield stops an arrow (`1089-1107`):
```
weighted = head·0.16 + neck·0.03 + torso·0.44 + shoulder·0.12 + arm·0.14 + leg·0.11
weighted += ShieldPassiveWeight·shieldTier
armorConstant = rbmCombat ? ArmorConstant / (armorMultiplier · armorEffectivenessMultiplier)
                          : ArmorConstant
passiveFactor = 1 + weighted / armorConstant
```

The zone armour is read by `SimulationEquipmentPower.GetArmorZones`, the same reader the
auto-resolve model uses; `shieldTier` is the best shield's item tier (`TierfOf`, clamped to
0…6.5).

**Barding is not here.** It is the horse's armour, and it is priced once, in the mount term
above. **And the divisor tracks the combat module**: RBM's own armour equation scales every
armour value by `armorEffectivenessMultiplier` (the *Armor Effectiveness* setting) and then
divides a blow by `100/(100 + armor·armorMultiplier)`, so the passive term only agrees with a
real blow when its divisor is `100/(armorMultiplier · armorEffectivenessMultiplier)`. At the
defaults (2 and 1) that doubles armour's weight — exactly the price of protection RBM charges
on the field. With RBM Combat off, the flat `ArmorConstant` is used.

### Two subtleties baked in

- **"Shooter" is about how the game fields the troop** (`IsRangedTroop`), not whether a
  bow is in his baggage — otherwise a mounted lord carrying a bow would be mispriced as
  a full-time archer.
- **A bow never lowers a man's score** below his sword: the ranged blend can only lift
  him (`offense = max(offense, blended)`).

---

## 4. Morale factor

`MoraleOf` (`793-804`), applied to the party total after the loop:

| Case | Factor |
|---|---|
| Non-mobile party | `1.0` |
| Estimated | `MBMath.Map(morale, 20, 40, 0.7, 1.0)` |
| Live | `morale < 30 ? 0.7 : 1.0` |

---

## 5. Commander perks → staying power

`HealthFactorOf` (`1591-1599`) = `CommandedHealth / 100`, multiplied into each man's
power. `CommandedHealth` comes from `SimulationTroopHitPoints.BuildCommandedHealth`
(`274-334`), which transcribes `GetEffectiveMaxHealth` — the man's own `MaxHitPoints()`
plus the party leader's HP-raising perks (`ThickHides`, `HardyFrontline`, `WellBuilt`,
`HardKnock`, `UnwaveringDefense`, `PickedShots`, `MinisterOfHealth`).

**The perks are gated by the auto-resolve perk switch**, not by anything strategic:
`BuildCommandedHealth` adds them only while `SimulationPerks.Enabled`
(`simulationPerkSystem && simulationEquipmentEnabled && simulationEquipmentPowerWeight > 0`).
With that off the factor is just the man's own `MaxHitPoints() / 100` — 1.0 for a line
trooper, more for a hero with a bigger pool.

The strategic model applies **only** this party-scoped commander track. Captain perks
are ignored here — they need battle formations, which do not exist on the campaign map
(`StrategicTroopPower.cs:56-74`).

---

## 6. Tuning constants (in code, not the config screen)

These are hardcoded in `StrategicTroopPower.cs` (lines `92-325`):

| Constant | Value | Meaning |
|---|---|---|
| `PowerScale` | `272f` | maps model output onto vanilla's power range. **Measured, not chosen** — re-derive it after any retune. |
| Zone weights H/N/T/Sh/A/L | `0.16 / 0.03 / 0.44 / 0.12 / 0.14 / 0.11` | hit-share per armour zone |
| `ArmorConstant` | `100f` | armour → passive-factor divisor, **divided by `armorMultiplier · armorEffectivenessMultiplier` when RBM Combat is on** |
| `ShieldPassiveWeight` | `4f` | shield's passive (arrow-stopping) worth, per point of shield tier |
| `ReferenceMountSurvival` | `440f` | the barded-warhorse yardstick the mount share is scaled off |
| `MountBonusAtReference` | `0.43f` | share of his own power a rider gains at that yardstick |
| `BardingToHealth` | `2f` | how barding converts into the horse's survivability — the only place barding is priced |
| `BestWeaponWeight` | `0.7f` | weight of the best weapon in a kit |
| `RangedShare` | `0.7f` | fraction of battle an archer spends shooting |
| `RangedOffenseWeight` | `1.35f` | archer offense premium |
| `SkillOffenseSpread` | `1f` | at saturation a man hits this much harder than a raw recruit (`× (1 + spread·skillFrac)`) |
| `SkillSaturation` | `250f` | skill value at which the offense and defense curves saturate |
| `PenetrationWeight` | `0.35f` | weapon quality (damage factor) → penetration, RBM Combat only |
| `ChargeWeight` | `0.004f` | cavalry charge-damage weight |
| `RangedEnergyScale` | `0.7f` | launcher joules → offense units |
| `CrossbowReloadDivisor` | `2.5f` | what a crossbow's power costs it in rate of fire |
| Bow / crossbow powerstroke | `25″ / 20″` (× 0.0254) | launcher physics, lifted from `SimulationWeaponModel` |
| Efficiency bow / longbow / crossbow | `0.90 / 0.835 / 0.88` | likewise |
| `SlingEnergy` | `110f` | flat joules for a sling (its `MissileSpeed` is a length, not a draw weight) |
| `MaxItemTier` | `6.5f` | clamp on every item tier read here |
| Shield defence base / skill | `0.45 / +0.30` | active ladder, lifted verbatim from the auto-resolve model |
| Weapon defence floor / skill | `0.20 / +0.18` | likewise |
| `DefenseChanceCap` | `0.75f` | likewise |
| `ActiveDefenseDamping` | `0.4f` | exponent damping the turn-aside factor |
| `BaselineHitPoints` | `100f` | the hundred `HealthFactorOf` divides by |
| `MinLandingFactor` | `0.1f` | floor on the amphibious-raid discount (§7) |

### What config actually changes strategic power

- `rbmCampaignEnabled` and `strategicPowerEnabled` (*Equipment Based Troop Power* on the
  config screen) — on/off.
- `rbmCombatEnabled`, `OneHandedThrustDamageBonus`, `armorMultiplier` and
  `armorEffectivenessMultiplier` — move the offense or armour model and trigger a cache
  rebuild (`EnsureCacheFresh`, `391-412`).
- `simulationPerkSystem`, `simulationEquipmentEnabled` and `simulationEquipmentPowerWeight`
  — only through §5: together they decide whether the commander's hit-point perks reach the
  men. Read live, no cache involved.

Everything else — the remaining auto-resolve dials (`simulationAbsoluteScale`, the miss,
defence and rout settings, etc.) — affects **casualties**, not this displayed number.

---

## 7. Around the number

The rest of `Power/`:

- **Amphibious raid discount** (`AmphibiousRaidStrengthPatch`, `StrategicTroopPower.cs:682-706`)
  — a prefix on `DefaultTargetScoreCalculatingModel.GetTargetScoreForFaction` that, for a
  raid the party would make through the village's port, scales its `ourStrength` by
  `LandingFactor`: the share of its healthy men its shallow-draft hulls' main decks can put
  ashore (mirroring War Sails' own landing cap), floored at `MinLandingFactor`. It lives on
  the raid score rather than in `GetPowerOfParty` so that strength itself stays
  position-blind (keying it on being at sea made armies flip the siege gate every time they
  embarked).
- **The strength-bar tooltip** (`StrategicPowerTooltip.cs`, `StrategicPowerTooltipVM.cs`) —
  the encounter overlay's hover, rewritten as two columns of per-troop rows taken from the
  real pricing via `TryExplainParty`. Vanilla's hint stands whenever this model did not
  price the sides.
- **The log** (`StrategicPowerLog.cs`) — `strategicPowerLoggingEnabled` (XML
  `StrategicPowerLogging`, default `0`; *Troop Power Logging* on the config screen) writes
  party pricings to `logs/powerCalculation/`.
- **The siege gate** (`SiegeDecisionGate.cs`) — not gated on `strategicPowerEnabled`: a
  transpiler on `GetTargetScoreForFaction` that raises vanilla's fresh-siege strength
  factor from 2× to 2.5× (`SiegeStrengthGateMultiplier = 1.25`) and holds a formed army's
  leader to 1.5× instead, so an army keeps the target it was gathered for.

---

*Line numbers are anchors as of this writing, not guarantees — re-grep the method name
if one has drifted.*
