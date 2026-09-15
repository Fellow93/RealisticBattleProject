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
`StrategicTroopPower.cs:16-51` explains: `GetDefaultTroopPower` returns vanilla's
tier term, and that same term is *also* the divisor the auto-resolve model cancels
out. Patching it would charge for equipment twice. So RBM keeps vanilla's whole
per-party loop and swaps only the **base per-man value**.

The gate (`StrategicTroopPower.cs:292-300`):

```
Enabled = rbmCampaignEnabled && strategicPowerEnabled && Campaign.Current != null
```

### 1.2 The party formula

`TryGetPowerOfParty` (`StrategicTroopPower.cs:457-546`) is vanilla's loop with the
tier base replaced. Per troop stack:

```csharp
int healthy = element.Number - element.WoundedNumber;      // wounded men do not fight

float power = PowerOf(troop);                                // equipment-aware, §1.3
if (power <= 0f)
    power = model.GetDefaultTroopPower(troop);               // unreadable troop → vanilla fallback

power *= HealthFactorOf(troop, party);                       // commander HP perks, §5.1

float contextMod = estimated ? 0f : model.GetContextModifier(troop, side, context);
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
- **`contextMod`** is vanilla's terrain-vs-arm table, and is dropped for `estimated`
  prices (where the terrain is unknown).

### 1.3 `PowerOf` → `Measure` — the man's own worth

`PowerOf` (`StrategicTroopPower.cs:571-629`) is cached per `CharacterObject` (heroes
re-measured daily). `Measure` (`721-784`) averages `PowerOfSet` over **all** of the
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

`PowerOfSet` (`StrategicTroopPower.cs:793-867`) is the heart of the strategic model:

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
inverted that ordering, which is why it is proportional.

**Offense** (`814-829`):
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
a shield is nearly the whole worth of carrying one) (`831-837`):
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

**Barding is not in this term** — it is the horse's armour, priced once in the mount share
above. **The divisor tracks the combat module**: RBM's armour equation divides a blow by
`100/(100 + armor·armorMultiplier)`, so the passive term only agrees with a real blow when its
divisor is `100/armorMultiplier`. At the default multiplier of 2 that doubles armour's weight.
With RBM Combat off, the flat `ArmorConstant` is used.

### 1.5 Morale factor

`MoraleOf` (`StrategicTroopPower.cs:552-563`), applied to the party total:

| Case | Factor |
|---|---|
| Non-mobile party | `1.0` |
| Estimated | `MBMath.Map(morale, 20, 40, 0.7, 1.0)` |
| Live | `morale < 30 ? 0.7 : 1.0` |

### 1.6 Strategic tuning constants

These live **in code** (`StrategicTroopPower.cs:96-288`), *not* in the config screen:

| Constant | Value | Meaning |
|---|---|---|
| `PowerScale` | `272f` | maps model output onto vanilla's power range. **Measured, not chosen.** |
| Zone weights H/N/T/Sh/A/L | `0.16 / 0.03 / 0.44 / 0.12 / 0.14 / 0.11` | hit-share per armour zone |
| `ArmorConstant` | `100f` | armour → passive-factor divisor, **÷ `armorMultiplier` under RBM Combat** |
| `ShieldPassiveWeight` | `4f` | shield's passive (arrow-stopping) worth |
| `ReferenceMountSurvival` | `440f` | the barded-warhorse yardstick the mount share is scaled off |
| `MountBonusAtReference` | `0.43f` | share of his own power a rider gains at that yardstick |
| `BardingToHealth` | `2f` | barding → horse survivability; the only place barding is priced |
| `BestWeaponWeight` | `0.7f` | weight of the best weapon among a kit |
| `RangedShare` | `0.7f` | fraction of battle an archer spends shooting |
| `RangedOffenseWeight` | `1.35f` | archer offense premium |
| `SkillSaturation` | `250f` | skill value at which defense curve saturates |
| `PenetrationWeight` | `0.35f` | weapon-quality → penetration |
| `ChargeWeight` | `0.004f` | cavalry charge-damage weight |
| `ActiveDefenseDamping` | `0.4f` | exponent damping the turn-aside factor |
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

The master gate (`1189-1196`):
```
SimulationEnabled = simulationEquipmentEnabled && simulationEquipmentPowerWeight > 0f
```
Every auxiliary system (arm targeting, morale, wound pools, perks) reads this one flag.

### 2.1 The correction — `Explain` / `GetCorrection`

`Explain` (`SimulationEquipmentPower.cs:1219-1982`) returns a `Breakdown` whose
`.Correction` multiplies vanilla's damage. The assembly (`1875-1980`):

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

`SimulateHit` postfix (`SimulationEquipmentPower.cs:831-919`):

1. Calls `Explain(... spend: true ...)` to get the real blow.
2. **Terrain/leader neutralizing** (`867-874`): multiplies `Correction` by
   `GetVanillaPowerNeutralizingFactor` (§6).
3. **Absolute per-blow cap** (`886-901`): caps `vanillaDamage · correction` at
   `simulationAbsoluteBlowCap · struckMaxHitPoints`.
4. `__result = new ExplainedNumber(vanillaDamage · correction)`.
5. Riposte (parry counter) applied (`915-918`).

### 2.3 `VanillaTierPower` — the divisor being cancelled

`VanillaTierPower` (`SimulationEquipmentPower.cs:2202-2211`) recomputes vanilla's tier
formula **by hand**, deliberately, so that no Harmony patch elsewhere can move the
divisor RBM is trying to cancel:

```csharp
int tier   = troop.IsHero ? (troop.HeroObject.Level / 4 + 1) : troop.Tier;
float power = (2 + tier) * (10 + tier) * 0.02f;
if (troop.IsHero) power *= 1.5f;
```

---

## 3. Configuration toggles & multipliers

**File:** `RBMConfig/RBMConfig.cs`

| Field | Default | Affects |
|---|---|---|
| `rbmCampaignEnabled` | `true` | master campaign gate |
| `rbmCombatEnabled` | `true` | selects the whole offense/armour model + skill curve used by both power systems |
| `strategicPowerEnabled` | `true` | §1 on/off |
| `strategicPowerLoggingEnabled` | `true` | strategic power log |
| `simulationEquipmentEnabled` | `true` | §2 master gate |
| `simulationEquipmentPowerWeight` | `1f` | ratio-mode exponent (`0` = vanilla, `>1` widens gaps); also part of `SimulationEnabled` |
| `simulationAbsoluteDamage` | `true` | absolute vs ratio damage mode |
| `simulationAbsoluteScale` | `1f` | absolute-mode magnitude dial (the main calibration knob) |
| `simulationAbsoluteBlowCap` | `1.5f` | per-blow cap as a share of the struck man's HP |
| `simulationShieldBlockChance` | `0.4f` | typical shield block folded into the baseline |
| `simulationDefenseSystem` | `true` | block/parry/riposte ladder (also switches the melee landing exponent) |
| `simulationArmTargeting` | `true` | arm-aware striker/struck selection |
| `simulationRangedMissEnabled` | `true` | archer accuracy/miss rolls |
| `simulationPerkSystem` | `true` | captain + commander perk contributions |
| `simulationLogHits` / `simulationLogHits` | `true` | per-hit auto-resolve log |
| `armorMultiplier` | `2f` | RBM armour equation `100 / (100 + armor·mult)` |
| `armorThresholdModifier` | `1f` | per-type armour thresholds |
| `ThrustMagnitudeModifier` | `0.05f` | thrust energy; its reciprocal is `OneHandedThrustDamageBonus` (= 20) |
| `OneHandedThrustDamageBonus` | `20f` | read by the RBM melee tier formula |

**Non-config constant worth knowing:** `LethalityHitPointScale = 1.25f`
(`SimulationTroopHitPoints.cs:84`) widens every trooper's HP pool so each blow is
proportionally less lethal.

### Cache invalidation

Changing a dial mid-session rebuilds the affected caches:

- `StrategicTroopPower.EnsureCacheFresh` (`348-364`) watches `rbmCombatEnabled` and
  `OneHandedThrustDamageBonus`.
- `SimulationEquipmentPower.EnsureBaselines` (`2325-2367`) watches `rbmCombatEnabled`,
  `simulationShieldBlockChance`, `armorMultiplier`, `armorThresholdModifier`,
  `ThrustMagnitudeModifier`, and `simulationDefenseSystem` — moving any of these
  rebuilds all baselines and kit prices.

---

## 4. How equipment & tier factor in

**Tier is explicitly removed, not used.** This is the central design decision
(`SimulationEquipmentPower.cs:28-56`, `StrategicTroopPower.cs:16-47`). Both models
divide vanilla's tier term back out and substitute real kit measurements. Tier
survives only as `VanillaTierPower` — the divisor being cancelled.

What actually feeds power instead:

- **Melee weapons** — `MeleeWeaponScore` (`SimulationEquipmentPower.cs:945-967`). With
  RBM Combat **on**, the weapon's listed damage is discarded; the blow collapses onto a
  per-class ceiling (`ClassCeiling`, `974-1007` — e.g. OneHandedSword cut `15·4.6`,
  TwoHandedAxe cut `24·4.6`) × a skill factor × penetration. Weapon *quality* survives
  only as penetration (`Penetration`, `1143-1147`: `1 + 0.35·(√factor − 1)`). With RBM
  Combat **off**, it uses listed `max(Swing, Thrust)`.
- **Ranged launchers** — priced on **real kinetic energy in joules**
  (`LauncherEnergyOf`, `1104-1140`), not tier:
  `0.5 · (drawWeight·4.448) · powerstroke · efficiency`, crossbows `/ 2.5` for reload.
  RBM repurposes `MissileSpeed` as draw weight in pounds. Slings are priced flat at
  110 J (their "tier" is a length, not a draw weight).
- **Armour** — read zone-by-zone (`GetArmorZones`, line 3133) and weighted by hit-zone
  shares. Shield item tiers still use `TierOf` (`1215-1252`, clamped 0–6.5).
- **Barding / charge** — `BardingOf` (`1172-1180`, uses `ArmorComponent.BodyArmor`),
  `ChargeDamageOf` (`1162-1170`, uses `HorseComponent.ChargeDamage`).

In the auto-resolve model, item worth enters through **actual damage vs actual armour
per body zone**, run through the live combat model's armour equation, then normalized
against a per-arm baseline (`_baselineDamage`).

---

## 5. Captain & commander perks

Bannerlord has **two non-overlapping perk tracks**; RBM routes them differently.

### 5.1 Commander track (party-scoped) → hit points

**File:** `RBMCampaign/Simulation/SimulationTroopHitPoints.cs`,
`BuildCommandedHealth` (`231-291`). Transcribes
`SandboxAgentStatCalculateModel.GetEffectiveMaxHealth`. Perks that raise a trooper's
HP pool include:

- `TwoHanded.ThickHides`, `Polearm.HardyFrontline` (primary slot), `Crossbow.PickedShots` (ranged only)
- Foot only: `Athletics.WellBuilt`, `Polearm.HardKnock`, `OneHanded.UnwaveringDefense`
- Leader's `Medicine.MinisterOfHealth`, scaled by Medicine skill above the epic threshold (`279-288`)
- Mount HP: `CommandedMountHealth` (`316+`) — `Medicine.Sledges`, `Riding.Veterinary`

This HP number flows into **both** systems:

- **Strategic:** `HealthFactorOf` (`StrategicTroopPower.cs:1308-1316`) =
  `CommandedHealth / 100`, multiplied into per-man power.
- **Auto-resolve:** `MaxHitPoints` (`SimulationTroopHitPoints.cs:164-186`) =
  `CommandedHealth · 1.25` (lethality scale), used for casualty attrition and the
  per-blow cap.

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
> (`StrategicTroopPower.cs:53-71`). Vanilla's own `leaderMod = LeaderHero.PowerModifier`
> is kept intact but counts only 2 `Captain`-role perks.

---

## 6. Terrain / arm context modifiers

**File:** `SimulationEquipmentPower.cs`, `GetVanillaPowerNeutralizingFactor`
(`2245-2305`).

Vanilla's blow rides on `(1 + leaderModifier + contextModifier)` per side, where
`contextModifier` is the arm-vs-terrain-vs-side table (cavalry worth more in the open,
archers worth less defending a wood). RBM **lifts this out on a field battle** — arm
advantage is meant to come from the horse and lance already priced into the equipment
ratio — but **keeps it on a siege**:

```csharp
bool keepContext = estimated || strikerContext == PowerCalculationContext.Siege;
float keptContextStriker = keepContext ? chargedContextStriker : 0f;

// leader term also lifted when RBM prices captain perks itself:
float keptLeaderStriker = SimulationPerks.Enabled ? 0f : chargedLeaderStriker;

float vanillaRatio = pow(chargedStriker / chargedStruck, 0.7f);
float neutralRatio = pow(keptStriker   / keptStruck,   0.7f);
return neutralRatio / vanillaRatio;      // folded into breakdown.Correction
```

`LeaderModifierOf` (`2313-2316`) =
`party.MapEventSide.LeaderParty.LeaderHero.PowerModifier` (mirrors vanilla's cached
`LeaderSimulationModifier`).

### Arm buckets

`GetBucket` / `GetTroopType` (`2620-2725`) classify every troop into
Infantry(0) / Archer(1) / Cavalry(2) / HorseArcher(3). `ArmOf` (`2631-2634`) is the
single shared arm classifier used by both damage pricing and target selection.
`IsRangedTroop` (`2711-2714`) counts slingers as ranged. Heroes are bucketed by what
they *fight* as, never their own bucket. The baseline table
`_baselineDamage[striker][struck]` (built in `EnsureBaselines`, `2325-2605`) is the
per-arm-matchup pivot the equipment ratio divides against.

---

## 7. File map

| File | Role |
|---|---|
| `Power/StrategicTroopPower.cs` | §1 — displayed party power (`GetPowerOfParty` prefix) |
| `Power/StrategicPowerLog.cs` | strategic power logging |
| `Power/StrategicPowerTooltip*.cs` | strategic power UI tooltip |
| `Simulation/SimulationEquipmentPower.cs` | §2 — auto-resolve blow power (`SimulateHit` postfix), baselines, arm buckets, terrain neutralizing |
| `Simulation/SimulationTroopHitPoints.cs` | §5.1 — commander-perk HP pool + lethality scale |
| `Simulation/SimulationPerks.cs` | §5.2 — captain-perk skill folding + kit signature |
| `Simulation/SimulationWeaponModel.cs` | weapon/missile physics both models mirror |
| `Simulation/SimulationBattleState.cs` | battle clock, ammo, horses-alive, charge/kiting terrain reads |
| `Simulation/SimulationCommandStructure.cs` | per-side captain chain of command |
| `Simulation/SimulationArmTargeting.cs` | arm-aware striker/struck selection |
| `Simulation/SimulationMorale.cs` / `SimulationRout.cs` | in-sim morale & routing |
| `RBMConfig/RBMConfig.cs` | all config dials (§3) |

Related design notes already in the repo: `RBMCampaign/AUTO_RESOLVE.md`,
`RBMCampaign/TROOP_POWER_TASK.md`, `RBMCampaign/ARCHITECTURE.md`.

---

*Line numbers reference the source as of this writing; treat them as anchors, not
guarantees — re-grep the method name if a line has drifted.*
