# RBM Campaign — Power Computation Reference

How Realistic Battle Mod's campaign layer prices soldiers. This document covers
the two power systems in `RBMCampaign`, their formulas, the constants and config
dials that drive them, and how equipment, tier, perks and terrain each factor in.

---

## 0. The central idea: two systems, one philosophy

RBM replaces Bannerlord's **tier-based** power with **equipment-based** power. In
vanilla, a soldier's worth is a pure function of the number on his troop card
(`(2 + tier)(10 + tier) * 0.02`, a range of 0.40 → 2.56). RBM throws that away and
measures the man's **actual armour, actual weapon, and actual skill** instead.

There are **two separate power systems**, deliberately kept apart. They agree in
*direction* but not in *magnitude* (by design):

| System | What it answers | Patch target | File |
|---|---|---|---|
| **Strategic power** | "How strong is this party?" — the number shown to the player and used by the AI to decide whether to fight, flee, or besiege. | `DefaultMilitaryPowerModel.GetPowerOfParty` (prefix) | `Power/StrategicTroopPower.cs` |
| **Auto-resolve blow power** | "How much does *this simulated blow* actually do?" — used inside auto-resolve to grind down hit points and produce casualties. | `DefaultCombatSimulationModel.SimulateHit` (postfix) | `Simulation/SimulationEquipmentPower.cs` |

Both are equipment-aware, but through **different models with different constants**.
Neither one should be tuned by copying numbers from the other.

> There is no separate `RBMAI` C# project involved here — all of this logic lives
> under `RBMCampaign\`.

---

## 1. Strategic party power (displayed strength / AI decisions)

**File:** `RBMCampaign/Power/StrategicTroopPower.cs`

### 1.1 What is patched — and why not the obvious method

RBM prefixes **`GetPowerOfParty`**, not `GetDefaultTroopPower`. The long comment at
`StrategicTroopPower.cs:19-54` explains: `GetDefaultTroopPower` returns vanilla's
tier term, and that same term is *also* the divisor the auto-resolve model cancels
out. Patching it would charge for equipment twice. So RBM keeps vanilla's per-party
loop and swaps the **base per-man value** (dropping vanilla's field-terrain modifier on
the way, §1.2).

The gate (`StrategicTroopPower.cs:329-337`):

```
Enabled = rbmCampaignEnabled && strategicPowerEnabled && Campaign.Current != null
```

### 1.2 The party formula

`TryGetPowerOfParty` (`StrategicTroopPower.cs:557-655`) is vanilla's loop with the
tier base replaced. Per troop stack:

```csharp
int healthy = element.Number - element.WoundedNumber;      // wounded men do not fight

float power = PowerOf(troop);                                // equipment-aware, §1.3
if (power <= 0f)
    power = model.GetDefaultTroopPower(troop);               // unreadable troop → vanilla fallback

power *= HealthFactorOf(troop, party);                       // commander HP perks, §5.1

bool siege = context == MapEvent.PowerCalculationContext.Siege;
float contextMod = siege ? model.GetContextModifier(troop, side, context) : 0f;
float leaderMod  = (party.LeaderHero != null) ? party.LeaderHero.PowerModifier : 0f;

float perMan = power * (1f + leaderMod + contextMod);        // vanilla's (1 + leader + context) shape kept
total += healthy * perMan;

// ... after the loop ...
result = total * morale;                                     // morale applied last, §1.5
```

Notes:

- **`leaderMod`** is vanilla's own `LeaderHero.PowerModifier`. It is left exactly as
  vanilla computes it and is worth almost nothing (it counts only the two
  `PrimaryRole == Captain` perks). RBM does not try to fix it here — it preserves the
  `(1 + leader + context)` shape.
- **`contextMod`** is vanilla's terrain-vs-arm table, **kept for a siege only**. Field
  terrain is dropped in every context — it is a per-arm guess that double-counts what the
  model already prices in the man — while a wall is a real fact about the fight and stays
  in the strength the AI reads. (Contrast the auto-resolve blow, which lifts the context out
  everywhere, siege included — §6.)

### 1.3 `PowerOf` → `Measure` — the man's own worth

`PowerOf` (`StrategicTroopPower.cs:812-870`) is cached per `CharacterObject` (heroes
re-measured daily). `Measure` (`967-1034`) averages `PowerOfSet` over **all** of the
troop's battle equipment sets, then divides by a scale constant:

```
detail.Power = (sum over sets of PowerOfSet) / setCount / PowerScale       // PowerScale = 272f
```

**`PowerScale = 272`** is *measured, not chosen*. It maps the model's raw output (in the
low hundreds) back onto vanilla's 0.40 → 2.56 range, so that hardcoded AI constants
elsewhere in the game — the army-power floor of 1000, siege dampers, etc. — still behave.
Without it, an unreadable villager rescued by the `GetDefaultTroopPower` fallback
(0.4–2.56) would count hundreds of times less than his neighbours priced in the hundreds.

**Re-measure it after any offence, armour or passive retune** — never re-pick it:
`k = men-weighted Σ(men × pm_new) / Σ(men × pm_old)` over matched troops, then
`newScale = oldScale × k`, checking that melee↔ranged parity holds. It has moved
197 → 260 (passive divisor began tracking `100/armorMultiplier`) → 272 (mount became a
proportional term).

### 1.4 `PowerOfSet` — pricing one kit

`PowerOfSet` (`StrategicTroopPower.cs:1043-1130`) is the heart of the strategic model:

```
product = offense × activeFactor × passiveFactor
power   = product + product × MountFractionOf(set)
```

Three stages of one blow — first it must not be turned aside, then it must get
through the armour — so what each buys **multiplies** rather than adds.

The mount then adds a **share of the rider's own power**, sized by how survivable the animal
is (its hit points plus its barding, against a barded warhorse as the yardstick). Because the
share tracks the horse and not the base, lighter cavalry gain less than armoured: roughly
+18% for a bare mount, +30% for a knight's, +34% for a cataphract's. A flat additive term
inverted that ordering, which is why it is proportional. `MountFractionOf` (`1140-1150`)
is `(HitPoints + HitPointBonus + BardingToHealth·barding) / ReferenceMountSurvival ·
MountBonusAtReference`, 0 on foot.

**Offense** (`1064-1079`; `melee`/`ranged` themselves are built as in §4):
```
offense = melee
if shooter:                              # shooter = has ranged AND game fields him as ranged
    blended = RangedShare·ranged + (1 − RangedShare)·melee
    offense = max(offense, blended)      # a bow never makes a man WORSE than his sword
    offense *= RangedOffenseWeight
offense *= 1 + ChargeWeight·chargeDamage # cavalry charge
```
> "Shooter" is a fact about how the game *fields* the troop (`IsRangedTroop`), not
> about whether a bow is in his baggage — otherwise a mounted lord with a bow on his
> back would be priced as a full-time archer.

**Active factor** — blows turned aside outright (a thing he *does*, priced on skill;
a shield is nearly the whole worth of carrying one) (`1081-1087`):
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

**Barding is not in this term** — it is the horse's armour, priced once in the mount share
above. **The divisor tracks the combat module**: RBM's armour equation scales armour by
`armorEffectivenessMultiplier` and divides a blow by `100/(100 + armor·armorMultiplier)`, so
the passive term only agrees with a real blow when its divisor is
`100/(armorMultiplier · armorEffectivenessMultiplier)`. At the defaults (2 and 1) that doubles
armour's weight. With RBM Combat off, the flat `ArmorConstant` is used.

### 1.5 Morale factor

`MoraleOf` (`StrategicTroopPower.cs:793-804`), applied to the party total:

| Case | Factor |
|---|---|
| Non-mobile party | `1.0` |
| Estimated | `MBMath.Map(morale, 20, 40, 0.7, 1.0)` |
| Live | `morale < 30 ? 0.7 : 1.0` |

### 1.6 Strategic tuning constants

These live **in code** (`StrategicTroopPower.cs:92-325`), *not* in the config screen
(the full list, including the launcher-physics and defence-ladder constants, is in
`STRATEGIC_POWER.md` §6):

| Constant | Value | Meaning |
|---|---|---|
| `PowerScale` | `272f` | maps model output onto vanilla's power range. **Measured, not chosen.** |
| Zone weights H/N/T/Sh/A/L | `0.16 / 0.03 / 0.44 / 0.12 / 0.14 / 0.11` | hit-share per armour zone |
| `ArmorConstant` | `100f` | armour → passive-factor divisor, **÷ `armorMultiplier · armorEffectivenessMultiplier` under RBM Combat** |
| `ShieldPassiveWeight` | `4f` | shield's passive (arrow-stopping) worth |
| `ReferenceMountSurvival` | `440f` | the barded-warhorse yardstick the mount share is scaled off |
| `MountBonusAtReference` | `0.43f` | share of his own power a rider gains at that yardstick |
| `BardingToHealth` | `2f` | barding → horse survivability; the only place barding is priced |
| `BestWeaponWeight` | `0.7f` | weight of the best weapon among a kit |
| `RangedShare` | `0.7f` | fraction of battle an archer spends shooting |
| `RangedOffenseWeight` | `1.35f` | archer offense premium |
| `SkillOffenseSpread` | `1f` | at saturation a man hits this much harder than a recruit |
| `SkillSaturation` | `250f` | skill value at which the offense and defense curves saturate |
| `PenetrationWeight` | `0.35f` | weapon-quality → penetration |
| `ChargeWeight` | `0.004f` | cavalry charge-damage weight |
| `ActiveDefenseDamping` | `0.4f` | exponent damping the turn-aside factor |
| `RangedEnergyScale` | `0.7f` | launcher joules → offense units |
| `CrossbowReloadDivisor` | `2.5f` | a crossbow's energy paid back for its reload |
| `SlingEnergy` | `110f` | flat joules for slings |

---

## 2. Auto-resolve blow power (`SimulateHit` postfix)

**File:** `RBMCampaign/Simulation/SimulationEquipmentPower.cs`

Vanilla prices a simulated blow as:
```
damage = (0.5 + 0.5·rand) · 40 · (power_striker / power_struck)^0.7 · advantage
```
where `power` is again the pure tier term. RBM's postfix (`SimulationEquipmentPower.cs:44-49`)
**replaces the power ratio** with a real equipment-vs-armour computation.

The master gate (`1272-1278`):
```
SimulationEnabled = simulationEquipmentEnabled && simulationEquipmentPowerWeight > 0f
```
Every auxiliary system (arm targeting, morale, wound pools, perks) reads this one flag.

### 2.1 The correction — `Explain` / `GetCorrection`

`Explain` (`SimulationEquipmentPower.cs:1302-2266`) returns a `Breakdown` whose
`.Correction` multiplies vanilla's damage (`GetCorrection`, `1172`, is the same without the
breakdown). A blow the defender turned aside (`breakdown.Defended`) leaves with a
correction of 0 before any of this. The assembly (`2159-2266`):

```csharp
float baseline = GetBaselineDamage(strikerTroop, struckTroop);   // typical dmg, this arm-vs-arm matchup
breakdown.EquipmentRatio = actual / baseline;

// vanilla's tier term, recomputed by hand so no patch can move the divisor:
float tierTerm = pow(VanillaTierPower(striker) / VanillaTierPower(struck), 0.7f);

if (simulationAbsoluteDamage) {          // DEFAULT (true)
    // cancels vanilla's 40 base AND its tier core, substitutes real magnitude `actual`.
    // no clamp here — the per-blow cap is applied later against the struck man's HP.
    correction = (simulationAbsoluteScale · actual) / (VanillaBaseScale · tierTerm);   // VanillaBaseScale = 40
} else {                                 // RATIO mode
    correction = pow(EquipmentRatio / tierTerm, simulationEquipmentPowerWeight);
    correction = clamp(correction, 0.1f, 8f);
}

// landing spread — a blow rarely bites at full force:
//   thrown  → ThrownLandingExponent  (~0.2, lands hardest)
//   missile → RangedLandingExponent  (~0.5)
//   charge  → ChargeLandingExponent  (~0.35)
//   melee   → MeleeLandingExponent (1.5) / MeleeLandingExponentNoDefense (2.0)
float landing = spend ? pow(rand, exponent) : 1/(exponent + 1);   // mean of the draw on reference tables
correction *= landing;
```

- **ABSOLUTE mode (default):** damage is the blow's own real magnitude. The formula
  cancels vanilla's `40` base scale and its tier core, leaving `actual` in their place;
  everything else vanilla carries (side advantage, leader/captain modifiers, Tactics
  and Scouting perks, its own random spread) rides through the multiply untouched.
  `simulationAbsoluteScale` is the sole calibration dial — **tune it against a paired
  log**. There is no `[0.1, 8]` clamp; the upper end is bounded per blow instead
  (`simulationAbsoluteBlowCap`, §2.2).
- **RATIO mode:** `correction = (EquipmentRatio / tierTerm) ^ weight`, clamped to
  `[0.1, 8]`. `weight = 0` is exactly vanilla, `1` is the model at face value, `>1`
  widens the gap between a well-found soldier and a ragged one.

### 2.2 The postfix wrapper

`SimulateHit` postfix (`SimulationEquipmentPower.cs:879-988`):

0. Returns at once, leaving vanilla's blow untouched, when `SimulationEnabled` is false.
1. Calls `Explain(... spend: true ...)` to get the real blow.
2. **Terrain/leader neutralizing** (`916-923`): multiplies `Correction` by
   `GetVanillaPowerNeutralizingFactor` (§6).
3. **Commander Tactics reshaped** (`924-934`): multiplies `Correction` by
   `CommanderTacticsFactor` (§6) — vanilla's one-sided Tactics advantage out, a gentler
   two-sided one in.
4. **Absolute per-blow cap** (`940-962`, absolute mode only): caps `vanillaDamage · correction` at
   `simulationAbsoluteBlowCap · MaxHitPoints(struck, struckParty)` — his commanded,
   lethality-scaled pool (§5.1).
5. `__result = new ExplainedNumber(vanillaDamage · correction)`.
6. The blow is parked for the siege width (`SimulationSiege.NoteBlow`) and written to the
   hit log (`RecordHit`).
7. Riposte (parry counter) applied (`984-987`).

### 2.3 `VanillaTierPower` — the divisor being cancelled

`VanillaTierPower` (`SimulationEquipmentPower.cs:2490-2499`) recomputes vanilla's tier
formula **by hand**, deliberately, so that no Harmony patch elsewhere can move the
divisor RBM is trying to cancel:

```csharp
int tier   = troop.IsHero ? (troop.HeroObject.Level / 4 + 1) : troop.Tier;
float power = (2 + tier) * (10 + tier) * 0.02f;
if (troop.IsHero) power *= 1.5f;
```

---

## 3. Configuration toggles & multipliers

**Files:** `RBMConfig/Config/RBMConfig.Simulation.cs` (auto-resolve + strategic toggles),
`RBMConfig.Combat.cs` (armour/thrust), `RBMConfig.Debug.cs` (log toggles),
`RBMConfig.Core.cs` (module toggles and the XML loader). Defaults below are the loader's
fallbacks — what a fresh config gets — and match the field initializers. The full
auto-resolve list is in `AUTO_RESOLVE.md` §9.

| Field | Default | Affects |
|---|---|---|
| `rbmCampaignEnabled` | `true` | master campaign gate |
| `rbmCombatEnabled` | `true` | selects the whole offense/armour model + skill curve used by both power systems |
| `strategicPowerEnabled` | `true` | §1 on/off |
| `strategicPowerLoggingEnabled` | `false` | strategic power log (`logs/powerCalculation/`) |
| `simulationEquipmentEnabled` | `true` | §2 master gate |
| `simulationEquipmentPowerWeight` | `1f` | ratio-mode exponent (`0` = vanilla, `>1` widens gaps); also part of `SimulationEnabled` |
| `simulationAbsoluteDamage` | `true` | absolute vs ratio damage mode |
| `simulationAbsoluteScale` | `1f` | absolute-mode magnitude dial (the main calibration knob) |
| `simulationAbsoluteBlowCap` | `1.5f` | per-blow cap as a share of the struck man's HP |
| `simulationShieldBlockChance` | `0.4f` | typical shield block folded into the baseline |
| `simulationDefenseSystem` | `true` | block/parry/riposte ladder (also switches the melee landing exponent) |
| `simulationArmTargeting` | `true` | arm-aware striker/struck selection |
| `simulationRangedMissEnabled` | `true` | archer accuracy/miss rolls |
| `simulationPerkSystem` | `true` | captain + commander perk contributions — the commander HP perks also feed §1 (§5.1) |
| `simulationLoggingEnabled` / `simulationLogHits` | `false` / `false` | auto-resolve battle log / its per-hit trace |
| `armorMultiplier` | `2f` | RBM armour equation `100 / (100 + armor·mult)` |
| `armorEffectivenessMultiplier` | `1f` | scales every armour value before the threshold and the curve; also in §1's passive divisor |
| `armorThresholdModifier` | `1f` | per-type armour thresholds |
| `bluntTraumaMultiplier` | `1f` | scales every trauma term of RBM's armour equation |
| `ThrustMagnitudeModifier` | `0.05f` | thrust energy; its reciprocal is `OneHandedThrustDamageBonus` (= 20) |
| `OneHandedThrustDamageBonus` | `20f` | read by the RBM melee tier formula |

**Non-config constant worth knowing:** `LethalityHitPointScale = 1.25f`
(`SimulationTroopHitPoints.cs:84`) widens every trooper's HP pool so each blow is
proportionally less lethal.

### Cache invalidation

Changing a dial mid-session rebuilds the affected caches:

- `StrategicTroopPower.EnsureCacheFresh` (`391-412`) watches `rbmCombatEnabled`,
  `OneHandedThrustDamageBonus`, `armorMultiplier` and `armorEffectivenessMultiplier`.
- `SimulationEquipmentPower.EnsureBaselines` (`2676-2966`) watches `rbmCombatEnabled`,
  `simulationShieldBlockChance`, `armorMultiplier`, `armorThresholdModifier`,
  `bluntTraumaMultiplier`, `armorEffectivenessMultiplier`, `ThrustMagnitudeModifier`, and
  `simulationDefenseSystem` — moving any of these rebuilds all baselines and kit prices.
  (`simulationPerkSystem` needs no rebuild: it rides in the kit-cache key as the captain's
  perk signature.)

---

## 4. How equipment & tier factor in

**Tier is explicitly removed, not used.** This is the central design decision
(`SimulationEquipmentPower.cs:26-31` and `51-56`, `StrategicTroopPower.cs:19-54`). Both
models divide vanilla's tier term back out (or never read it) and substitute real kit
measurements. Tier survives only as `VanillaTierPower` — the divisor being cancelled — and
as the item tier of a shield in the strategic passive term.

What actually feeds **strategic** power instead (all in `StrategicTroopPower.cs`):

- **Melee weapons** — `MeleeWeaponScore` (`1228-1250`), combined per kit by `MeleeOffense`
  (`1156-1214`) as `0.7·best + 0.3·mean`. With RBM Combat **on**, the weapon's listed
  damage is discarded; the blow collapses onto a per-class ceiling (`ClassCeiling`,
  `1257-1290` — e.g. OneHandedSword cut `15·4.6`, TwoHandedAxe cut `24·4.6`, mirroring the
  clamps in `RBMConfig/Shared/SkillDamage.cs`) × `(1 + skillFrac)` × penetration. Weapon
  *quality* survives only as penetration (`Penetration`, `1426-1430`:
  `1 + 0.35·(√factor − 1)`). With RBM Combat **off**, it uses listed `max(Swing, Thrust)`
  × `(1 + skillFrac)`.
- **Ranged launchers** — priced on **real kinetic energy in joules**
  (`LauncherEnergyOf`, `1387-1423`), not tier:
  `0.5 · (drawWeight·4.448) · powerstroke · efficiency`, crossbows `/ 2.5` for reload, then
  × `RangedEnergyScale` (0.7) × `(1 + skillFrac)` in `RangedOffense` (`1301-1370`), which
  also requires matching ammo in the kit. RBM repurposes `MissileSpeed` as draw weight in
  pounds. Slings are priced flat at 110 J (their `MissileSpeed` is a length, not a draw
  weight).
- **Armour** — read zone-by-zone (`SimulationEquipmentPower.GetArmorZones`, `3501`) and
  weighted by hit-zone shares. Shield item tiers use `TierfOf` (`1498-1535`, clamped 0–6.5).
- **Barding / charge** — `BardingOf` (`1455-1463`, uses `ArmorComponent.BodyArmor`),
  `ChargeDamageOf` (`1445-1453`, uses `HorseComponent.ChargeDamage`).

In the **auto-resolve** model, item worth enters through **actual damage vs actual armour
per body zone**: each weapon is turned into a profile by `SimulationWeaponModel`
(`CollectMeleeProfiles` / `CollectShotProfiles` / `GetThrownProfile` in
`SimulationEquipmentPower.cs`), run through the live combat model's armour equation
(`SimulationWeaponModel.RbmDamage`, mirroring `RBMConfig/Shared/BlowDamage.cs`, or
`SimulationWeaponModel.VanillaDamage`, mirroring native's `ComputeRawDamage`), and — in ratio mode — normalized against a per-arm baseline
(`_baselineDamage`).

---

## 5. Captain & commander perks

Bannerlord has **two non-overlapping perk tracks**; RBM routes them differently.

### 5.1 Commander track (party-scoped) → hit points

**File:** `RBMCampaign/Simulation/SimulationTroopHitPoints.cs`,
`BuildCommandedHealth` (`274-334`). Transcribes
`SandboxAgentStatCalculateModel.GetEffectiveMaxHealth`. Perks that raise a trooper's
HP pool include:

- `TwoHanded.ThickHides`, `Polearm.HardyFrontline` (primary slot) — not at sea;
  `Crossbow.PickedShots` (ranged only)
- Foot only: `Athletics.WellBuilt` (not at sea), `Polearm.HardKnock`,
  `OneHanded.UnwaveringDefense` (infantry, not at sea). "Foot" is the battle's answer
  (`SimulationBattleState.IsMountedIn`), so a cavalryman on a wall collects them.
- Leader's `Medicine.MinisterOfHealth`, scaled by Medicine skill above the epic threshold (`319-331`)
- Mount HP: `CommandedMountHealth` (`359-384`) — `Medicine.Sledges`, `Riding.Veterinary`

All of it is gated on `SimulationPerks.Enabled` (`simulationPerkSystem && SimulationEnabled`);
off, the pool is just the troop's own `MaxHitPoints()`.

This HP number flows into **both** systems:

- **Strategic:** `HealthFactorOf` (`StrategicTroopPower.cs:1591-1599`) =
  `CommandedHealth / 100`, multiplied into per-man power.
- **Auto-resolve:** `MaxHitPoints` (`SimulationTroopHitPoints.cs:164-186`) =
  `CommandedHealth · 1.25` (lethality scale), used for casualty attrition and the
  per-blow cap. A hero is exempt: he keeps his own unscaled `MaxHitPoints()`.

### 5.2 Captain track (formation-scoped) → skill → into the kit

**File:** `RBMCampaign/Simulation/SimulationPerks.cs`, `SkillOf` (`197-261`).
Transcribes `GetEffectiveSkill`'s captain branch. A captain's teaching is folded into
the troop's **skill value**, which then flows through every real damage / miss /
defense equation. Perk table (`88-100`): `FlexibleFighter`, `DeadAim`, `HorseMaster`,
`StrongArms`, `RunningThrow`, `DonkeysSwiftness`, `WrappedHandles`, `StrongGrip`,
`CleanThrust`, `CounterWeight`. Melee perks reach **foot troops only** (faithfully
matching native's quirk).

Captains are baked into the kit-cache key via `SignatureOf` (`134-151`, a perk
bitmask), so the same troop template on both sides of a battle gets differently-priced
kits. Gate (`111-117`): `simulationPerkSystem && SimulationEnabled`.

> The **strategic** model applies only the commander (party-scoped HP) track —
> captains need formations that don't exist on the campaign map
> (`StrategicTroopPower.cs:56-74`). Vanilla's own `leaderMod = LeaderHero.PowerModifier`
> is kept intact but counts only 2 `Captain`-role perks.

---

## 6. Terrain / arm context modifiers

**File:** `SimulationEquipmentPower.cs`, `GetVanillaPowerNeutralizingFactor`
(`2534-2597`).

Vanilla's blow rides on `(1 + leaderModifier + contextModifier)` per side, where
`contextModifier` is the arm-vs-terrain-vs-side table (cavalry worth more in the open,
archers worth less defending a wood). RBM **lifts the context out of every blow, siege
included** — arm advantage is meant to come from the horse and lance already priced into
the equipment ratio, and a siege's own facts (no horses, the wall) are priced by the
model's siege handling rather than by vanilla's table. Only the `Estimated` context is
left alone, because vanilla charged it no context to begin with:

```csharp
float chargedContextStriker = estimated ? 0f : model.GetContextModifier(...);   // what vanilla charged
float keptContextStriker = 0f;                                                   // what the blow keeps

// leader term also lifted when RBM prices captain perks itself:
float keptLeaderStriker = SimulationPerks.Enabled ? 0f : chargedLeaderStriker;

float vanillaRatio = pow(chargedStriker / chargedStruck, 0.7f);
float neutralRatio = pow(keptStriker   / keptStruck,   0.7f);
return neutralRatio / vanillaRatio;      // folded into breakdown.Correction
```

(The strategic model is the opposite on sieges: it keeps the siege context for the AI's
strength read — §1.2.)

`LeaderModifierOf` (`2605-2608`) =
`party.MapEventSide.LeaderParty.LeaderHero.PowerModifier` (mirrors vanilla's cached
`LeaderSimulationModifier`).

### Commander Tactics

`CommanderTacticsFactor` (`2654-2667`) is folded into the correction right after the
neutralizing factor. Vanilla's side commander adds `+0.1%` per point of Tactics to every
blow his side lands (`VanillaTacticsAdvantagePerPoint = 0.001`), one-sided. RBM divides that
back out and puts a two-sided edge at half the rate in its place
(`CommanderTacticsPerPoint = 0.0005`):

```
factor = 1/(1 + Ts·0.001) · (1 + Ts·0.0005) / (1 + Tk·0.0005)     // Ts, Tk = striker's / struck's side commander Tactics
```

Two equal generals cancel; only the gap tells. Vanilla's siege storming penalty and the
PreBattleManeuvers perk gap ride the same advantage and are deliberately left in.

### Arm buckets

`GetBucket` (`2981-2984`) / `GetTroopType` (`3078-3086`) classify every troop into
Infantry(0) / Archer(1) / Cavalry(2) / HorseArcher(3). `ArmOf` (`2992-2995`) is the
single shared arm classifier used by both damage pricing and target selection.
`IsRangedTroop` (`3072-3075`) counts slingers as ranged. Heroes are bucketed by what
they *fight* as, never their own bucket. The baseline table
`_baselineDamage[striker][struck]` (built in `EnsureBaselines`, `2676-2966`) is the
per-arm-matchup pivot the equipment ratio divides against.

---

## 7. File map

| File | Role |
|---|---|
| `Power/StrategicTroopPower.cs` | §1 — displayed party power (`GetPowerOfParty` prefix); amphibious raid discount |
| `Power/StrategicPowerLog.cs` | strategic power logging (`logs/powerCalculation/`) |
| `Power/StrategicPowerTooltip*.cs` | strategic power UI tooltip (encounter strength bar) |
| `Power/SiegeDecisionGate.cs` | AI siege-start strength bar (not gated on `strategicPowerEnabled`) |
| `Simulation/SimulationEquipmentPower.cs` | §2 — auto-resolve blow power (`SimulateHit` postfix), baselines, arm buckets, terrain/leader neutralizing, commander Tactics |
| `Simulation/SimulationTroopHitPoints.cs` | §5.1 — commander-perk HP pool + lethality scale |
| `Simulation/SimulationPerks.cs` | §5.2 — captain-perk skill folding + kit signature |
| `Simulation/SimulationWeaponModel.cs` | weapon/missile physics both models mirror |
| `Simulation/SimulationBattleState.cs` | battle clock, ammo, horses-alive, charge/kiting terrain reads |
| `Simulation/SimulationCommandStructure.cs` | per-side captain chain of command |
| `Simulation/SimulationArmTargeting.cs` | arm-aware striker/struck selection |
| `Simulation/SimulationMorale.cs` | skips vanilla's morale multiplier on a blow |
| `Simulation/SimulationRout.cs` | in-sim routing |
| `Simulation/SimulationSiege.cs` / `SimulationSiegeEngines.cs` | wall-assault acts and widths / artillery |
| `RBMConfig/Shared/BlowDamage.cs`, `SkillDamage.cs` | RBM's armour equation and skill clamps, shared with live combat |
| `RBMConfig/Config/RBMConfig.*.cs` | all config dials (§3) |

Related design notes already in the repo: `RBMCampaign/AUTO_RESOLVE.md`,
`RBMCampaign/TROOP_POWER_TASK.md`, `RBMCampaign/ARCHITECTURE.md`.

---

*Line numbers reference the source as of this writing; treat them as anchors, not
guarantees — re-grep the method name if a line has drifted.*
