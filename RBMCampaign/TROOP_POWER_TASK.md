# Task: Equipment- & commander-based troop power for player/AI oversight

**Status: IMPLEMENTED — this document is kept as the original design rationale, not as a description
of the shipped code.** The system lives in `Power/StrategicTroopPower.cs`. For what it actually does
now — including the constants, which have moved since this was written — read
[STRATEGIC_POWER.md](STRATEGIC_POWER.md) and [POWER_COMPUTATION.md](POWER_COMPUTATION.md); those two
are maintained against the source, this one is not.

Where they disagree, they are right and this is history. Known divergences: `PowerScale` is 272, not
any figure below; barding was pulled out of the passive armour term and is now priced only inside the
mount share; the passive divisor tracks `ArmorConstant / armorMultiplier` under RBM Combat; and the
captain-perk half of the design was ultimately built for **auto-resolve** (`Simulation/SimulationPerks.cs`,
gated on `simulationPerkSystem`), which §2 below rules out of scope. That constraint no longer holds.

## Goal

Replace the tier-only number the player and AI read as "how strong is this troop" with one computed from
the soldier's actual skill, gear, mount and shield — plus his commander's perks.

**Auto-resolve is explicitly out of scope and must not change.** See §2: this is the constraint that shapes
the whole design.

---

## 1. The problem

The whole oversight surface is one chain in `TaleWorlds.CampaignSystem.dll`:

```
GetPowerOfParty(party, side, context)                      // sums troops, applies morale factor
  └─ GetTroopPower(troop, side, context, leaderModifier)   // = base * (1 + leaderModifier + contextModifier)
       └─ GetDefaultTroopPower(troop)                      // = (2+tier)*(10+tier)*0.02 * (hero?1.5)
```

`GetDefaultTroopPower` sees **only tier**. A heavily-armoured elite and a ragged levy of the same tier are
the same soldier to it. That one number drives party strength, the strength shown in encounters, every AI
attack/flee/army decision, and auto-resolve casualty weighting.

Nothing patches it today (`RBMCampaign/AUTO_RESOLVE.md:47` states this outright; RBMCombat's
`OverrideDefaultMilitaryPowerModel` is misnamed and inert — its nested classes actually patch
`CommonAIComponent.InitializeMorale` and `Agent.InitializeSpawnEquipment`). RBM's own tier/noble tweaks to
`CharacterObject.GetPower` never reach party strength, because `GetDefaultTroopPower` recomputes tier power
independently.

Commander perks are also near-absent: `GetPowerModifierOfHero` counts only perks where
`PrimaryRole == PartyRole.Captain`, but **98 of the 100 `PartyRole.Captain` lines in `DefaultPerks.cs`
declare Captain as their *Secondary* role — only 2 are Primary.** So the leader term is ≈0 in practice, and
it is arm-blind by construction.

## 2. ⚠️ Inject at `GetPowerOfParty`, NOT `GetDefaultTroopPower`

This is the single most important constraint in this document. Verified decompile of
`DefaultCombatSimulationModel`:

```csharp
public override ExplainedNumber SimulateHit(CharacterObject strikerTroop, CharacterObject struckTroop, ...)
{
    float troopPower  = Campaign.Current.Models.MilitaryPowerModel.GetTroopPower(strikerTroop, ..., strikerParty.MapEventSide.LeaderSimulationModifier);
    float troopPower2 = Campaign.Current.Models.MilitaryPowerModel.GetTroopPower(struckTroop, ..., struckParty.MapEventSide.LeaderSimulationModifier);
    int num = (int)((0.5f + 0.5f * MBRandom.RandomFloat) * (40f * MathF.Pow(troopPower / troopPower2, 0.7f) * strikerAdvantage));
    ...
}
```

`SimulateHit` → `GetTroopPower` → `GetDefaultTroopPower`. If you patch `GetDefaultTroopPower`, vanilla's blow
already contains the equipment-aware ratio, and then `SimulationEquipmentPower`'s postfix divides by
`tierTerm` (removing a tier term that is no longer there) and multiplies by `equipmentRatio` again —
**equipment counted twice, plus a spurious division**. The correction's calibration would skew silently and
non-linearly (equipment entering at `^0.7` and again at `^weight`).

`SimulationEquipmentPower.VanillaTierPower`'s comment ("Recomputed here rather than called so that a patch on
the model cannot make this divide out something other than what vanilla actually charged") guards the
*divisor* against being patched. It cannot guard the *dividend* from changing underneath it.

`SimulateHit` never calls `GetPowerOfParty`. Patching `GetPowerOfParty` reaches every oversight surface —
`PartyBase.GetCustomStrength`/`TotalStrength`, `MapEventSide.RecalculateStrengthOfSide` (→ `StrengthOfSide`,
`StrengthRatio`, renown, influence), `Army.GetCustomStrength`,
`MobileParty.GetTotalLandStrengthWithFollowers`, and all AI consumers — while leaving auto-resolve alone.

`MapEventSide` also calls `GetTroopPower` **directly** for `CasualtyStrength`; that stays tier-based, which is
correct — the sim prices casualties its own way.

This also means the blow's leader term comes from `MapEventSide.LeaderSimulationModifier`, not from
`GetPowerOfParty` — so the perk work in §5 cannot disturb `SimulateHit` either.

## 3. The patch

One Harmony prefix on
`DefaultMilitaryPowerModel.GetPowerOfParty(PartyBase, BattleSideEnum, MapEvent.PowerCalculationContext)`,
reimplementing vanilla's loop. **Do not call `GetTroopPower`** — it would reintroduce the tier base. Reproduce
its shape instead:

```
for each roster element (skip null Character):
    troopPower = equipmentPower(troop)
               × (1 + leaderMod_troop + contextMod)
    total += (Number − WoundedNumber) × troopPower        // healthy count only — preserve
total ×= moraleFactor                                     // preserve vanilla's map exactly
```

Preserve vanilla's details verbatim:

- `contextMod = model.GetContextModifier(troop, side, context)`, **skipped when `context == Estimated`**.
- morale (only when `party.IsMobile`): `Estimated` → `MBMath.Map(morale, 20f, 40f, 0.7f, 1f)`; otherwise
  `morale < 30f → 0.7f`; else `1f`.

Tier is **replaced entirely** — it plays no part in the number. **No hero ×1.5**: heroes are measured on gear
and skills like anyone else. (This matches the principle the sim already applies in `IsBaselineTroop`, which
excludes heroes from the baseline because "what makes him a lord … is the thing we are trying to MEASURE".)

## 4. `equipmentPower(troop)`

Standalone extractor, living in **RBMCampaign**. No dependency on `SimulationEquipmentPower.GetKit`, and no
cross-module reference to RBMCombat. Averaged over `troop.BattleEquipments`; cached per `CharacterObject`.

```
per equipment set:

  ── OFFENSE ──────────────────────────────────────────────────
  rbmCombat = RBMConfig.rbmCombatEnabled

  meleeWeapon(w) = rbmCombat
      ? classCeiling(w.WeaponClass) × (1 + 2·skillModifier) × penetration(w.DamageFactor)
      : f(w.Tierf)                                  // vanilla uses listed damage
  melee   = 0.7 × best(meleeWeapon) + 0.3 × mean(meleeWeapon)

  ranged  = g(launcher.Tierf, ammo.Tierf) × skillFactor(launcher.RelevantSkill)
                                          × penetration(ammo.DamageFactor)
  offense = isShooter ? (s × ranged + (1−s) × melee) : melee
  offense ×= 1 + k·ChargeDamage                     // mount

  ── PROTECTION ───────────────────────────────────────────────
  skillFrac = clamp(meleeSkill / SkillSaturationLevel, 0, 1)

  // ACTIVE — melee only; a chance to negate outright. Shield is BINARY here.
  active = hasShield ? min(0.45 + 0.30·skillFrac, 0.75)
                     : min(0.20 + 0.18·skillFrac, 0.75)
  activeFactor = 1 / (1 − active)                   // 1.25× .. 4.0×

  // PASSIVE — armour + shield as standing cover (this is what answers arrows)
  weightedArmor = Σ_zone armor[zone]·zoneWeight[zone]
                + barding
                + ShieldPassiveWeight · shield.Tierf
  passiveFactor = 1 + weightedArmor / ArmorConstant

  power_set = offense × activeFactor × passiveFactor

equipmentPower = mean(power_set over BattleEquipments)
```

### Why the shape is what it is

**Protection splits into active and passive because they act at different stages of one blow.** A blow must
first fail to be turned aside (a probability), *then* get through armour (a magnitude). Expected damage is
`(1 − P_active) × reduced(passive)`, so their survivability multipliers **compose** — that is the chain, not a
modelling preference. Both are dimensionless ("how many times longer he lives"), which is exactly why
`offense × protection` is coherent and an additive form is not.

**A shield enters BOTH, and that is not double-counting.** Active: you raise it against a blow you saw coming
(a skill roll). Passive: it is strapped to your arm and covers you regardless — including against arrows you
never saw, which is precisely the thing you cannot parry. This is also why active defence being melee-only
needs no melee/ranged blend: the shield's answer to missiles lives in the passive term.

### Melee class ceiling

`max × scale`, lifted from `SimulationWeaponModel.GetMeleeClamp` (cut/pierce rows):

| WeaponClass | max·scale | vs 1H twin |
|---|---|---|
| Dagger / OneHandedSword / ThrowingKnife | 15 × 4.6 = 69 | — |
| TwoHandedSword | 20 × 4.6 = 92 | 1.33× |
| OneHandedAxe / ThrowingAxe | 18 × 4.6 = 82.8 | — |
| TwoHandedAxe | 24 × 4.6 = 110.4 | 1.33× |
| Mace | 15 × 4.6 = 69 | — |
| TwoHandedMace | 22 × 4.6 = 101.2 | 1.47× |
| OneHandedPolearm | 24 × 4 = 96 | — |
| TwoHandedPolearm | 28 × 4 = 112 | 1.17× |

Blunt rows are all 26/20 = 1.30×; copy them separately from `GetMeleeClamp` if blunt is to be handled
properly. **This table is a deliberate duplication — keep it in step with `GetMeleeClamp` if that is retuned.**

Note the two-handed advantage is derived from RBM's own combat model, not invented. Do **not** try to get it
from `Tierf`: RBM's `CalculateTierMeleeWeapon` deliberately *suppresses* two-handers (`TwoHandedSword`,
`TwoHandedAxe`, `TwoHandedMace` are all divided by 1.3, while polearms are not), so a tier-derived offense
would rank a two-handed axe *below* a one-hander of equal raw damage. The per-class tier formulas are also
structurally different (`TwoHandedAxe = num/1.3` vs `OneHandedAxe = num × length × 0.014`), so the ÷1.3 and
the 1.33× do **not** cancel arithmetically — `Tierf × 1.33` would not give a 1.33× two-hander.

### Active defence constants

Lifted from `SimulationEquipmentPower` (the skill-based block/parry system, gated there on
`simulationDefenseSystem`):

```
ShieldDefenseBase       = 0.45   ShieldDefenseSkillCoeff = 0.30
WeaponDefenseFloor      = 0.20   WeaponDefenseSkillCoeff = 0.18
DefenseChanceCap        = 0.75
```

Resulting `activeFactor`: bare weapon 1.25× (unskilled) → 1.61× (saturated); shield 1.82× → 4.0× (capped).

Shield quality is deliberately **not** in the active roll — this matches what the sim actually rolls
(`ShieldDefenseChance(skill)` takes skill only). Shield quality counts in the passive term instead.

### Armour zones

Reuse `SimulationEquipmentPower.GetArmorZones(Equipment set, bool rbmCombat, out head, neck, torso, shoulder,
arm, leg)` — same assembly, and it already branches on `rbmCombat`. Make it `internal`. This avoids both a
cross-module reference to RBMCombat's `ArmorRework` and a fresh duplication.

## 5. Perks — two channels

Verified in decompiled `SandBox.dll` `SandboxAgentStatCalculateModel`, whose own debug dump labels the two
blocks **"Party Leader Perks"** vs **"Captain Perks"**:

| Channel | Source | Applies to |
|---|---|---|
| **Party leader** | `PartyBaseHelper.GetVisualPartyLeader(party)`, filtered on `PartyRole.PartyLeader` | **Every** troop of the party |
| **Captain** | `agent.Formation.Captain` → `PerkHelper.AddPerkBonusFromCaptain` | **Only that formation** |

`PartyRole`: `None, Ruler, ClanLeader, Governor, ArmyCommander, PartyLeader, PartyOwner, Surgeon, Engineer,
Scout, Quartermaster, PartyMember, Personal, Captain, FirstMate, Navigator`.
Perk-line counts in `DefaultPerks.cs`: **PartyLeader 130, Captain 100, ArmyCommander 8, Personal 219**.

Per party (memoised — see §6):

1. **`flags(troop)`** → `TaleWorlds.Core.TroopUsageFlags` (`[Flags] ushort`):
   `None=0, OnFoot=1, Mounted=2, Melee=4, Ranged=8, OneHandedUser=0x10, ShieldUser=0x20, TwoHandedUser=0x40,
   PolearmUser=0x80, BowUser=0x100, ThrownUser=0x200, CrossbowUser=0x400, Undefined=ushort.MaxValue`.
   Must be a **complete** description of the troop, because matching requires
   `troopFlags.HasAllFlags(perk.Mask)`.

2. **Captain assignment (greedy approximation).** There is no campaign-side assignment to reuse:
   `GetCaptainRatingForTroopUsages` is referenced only by `TaleWorlds.CampaignSystem.dll` and
   `SandBox.ViewModelCollection.dll` — it is **UI-only advice** for the Order of Battle screen. Formations do
   not exist on the map, so approximate: candidate heroes = roster elements with `Character.IsHero` (leader
   included); arms present = the distinct arms in the roster; score each `(hero, arm)` with
   `Campaign.Current.Models.BattleCaptainModel.GetCaptainRatingForTroopUsages(hero, representativeFlags(arm),
   out _)`; repeatedly take the highest-scoring free `(hero, arm)` pair until heroes or arms run out. Arms left
   uncaptained get no captain channel. (The game auto-assigns captains in battle anyway, so this predicts what
   will happen.)

3. **Per-troop modifier** = the two channels summed, each computed from perks that mask-match **that troop's
   own** flags — so a shieldless man in the infantry formation gets no `ShieldUser` perk:
   - *Leader channel*: perks with `PrimaryRole` or `SecondaryRole` ∈ {`PartyLeader`, `ArmyCommander`} that the
     **party leader** has → applies to every troop.
   - *Captain channel*: perks with `PrimaryRole` or `SecondaryRole` == `Captain` that **that arm's assigned
     captain** has.

4. **Weighting mirrors vanilla's `GetPowerModifierOfHero`:**
   `RequiredSkillValue / Campaign.Current.Models.CharacterDevelopmentModel.MaxSkillRequiredForEpicPerkBonus`
   → `≤0.3 → 0.01`, `≤0.6 → 0.02`, `≤0.9 → 0.03`, else `0.06`.

5. `leaderMod_troop = clamp((leaderChannel + captainChannel) × captainPowerScale, 0, captainPowerMax)`.
   This **replaces** vanilla's `LeaderHero.PowerModifier` (≈0 anyway; adding would double-count the 2
   primary-role perks).

Parties with no heroes (garrisons, caravans, villager parties) → modifier 0, early out.

Use `PerkHelper.GetCaptainPerksForTroopUsages(flags)` for matching — do **not** hand-roll it. It checks
`(PrimaryTroopUsageMask != Undefined && flags.HasAllFlags(PrimaryTroopUsageMask)) || (Secondary…)`, i.e. it is
mask-based and role-blind, so it correctly catches the 98 secondary-role perks vanilla's power modifier misses.
Filter by role yourself afterwards to split the channels.

Non-combat leader perks (Scouting/Roguery/Trade/Steward) are excluded **for free**, because that matcher skips
any perk whose mask is `Undefined`. Note `Undefined` ≠ `None`: mask `None` (0) matches **everyone**, since
`HasAllFlags(0)` is always true (e.g. `TwoHanded.WoodChopper`, "damage against shields by troops in your
formation").

## 6. Gotchas

- **`ItemObject.Tierf` is computed live on every access, not cached:**
  `Tierf => TierfOverride >= 1f ? TierfOverride - 1f : Game.Current.BasicModels.ItemValueModel.CalculateTier(this)`.
  It runs the full tier calculation per `get`. **Cache it.**
- **RBM replaces the tier model.** `RealisticBattleCombatModule/CombatModule/ItemValuesTiers.cs` prefixes
  `CalculateTierMeleeWeapon`, `CalculateRangedWeaponTier`, `CalculateAmmoTier`, `CalculateShieldTier`,
  `CalculateArmorTier`, `CalculateHorseTier` and `CalculateValue` — all full replacements. So `Tierf` is
  RBM-aware **for free** when `rbmCombatEnabled`. This is why ranged needs no vanilla branch.
- **`TierfOverride` bypasses the model.** If an item's XML declares a tier, `Tierf` returns that and RBM's
  patches never run for it. Worth checking whether RBMXML sets tiers explicitly.
- **RBM's melee tier depends on `RBMConfig.OneHandedThrustDamageBonus`** — so a `Tierf` cache must invalidate
  on config change, not only on gear change. Hero kits change too;
  `SimulationEquipmentPower.ForgetHeroKits` / `EvictHeroes<TValue>` show the reusable eviction pattern.
- **Do NOT use item price as a quality signal.** RBM's weapon price is
  `(500 + 100·GetEquipmentValueFromTier(Tierf)) × 0.7 × WeaponPriceModifier`, with ItemType multipliers
  (`Polearm ×0.3`, `Shield ×0.3`, `Thrown ×0.25`, `TwoHandedWeapon ×1.5`). It is a **monotone function of
  `Tierf`** — zero independent signal — and it would import economy knobs into combat power (a spear would read
  ~70% weaker purely because RBM makes spears cheap to buy; retuning shop prices would silently shift every
  unit's power). Use `DamageFactor` for per-item quality instead: `sqrt` of the item's damage factor, described
  in `SimulationWeaponModel` as *"divides the armour threshold: this is a weapon's quality"*.
- **Under RBM Combat, listed melee damage is essentially unused** — the clamp binds on class + training. That
  is why melee uses the class ceiling and not `Tierf`. Ranged is the opposite: a kinetic model off real stats
  (draw weight, powerstroke, ammo weight/speed) with **no class clamp**, so `Tierf` tracks it well.
- **Perf.** `GetPowerOfParty` is called constantly by the AI, for every party.
  `GetCaptainPerksForTroopUsages` / `GetCaptainRatingForTroopUsages` each iterate **all** `PerkObject.All` and
  allocate a `List`. Unmemoised this is catastrophic. Required caches: `equipmentPower` per `CharacterObject`;
  `TroopUsageFlags` per `CharacterObject`; `flags → PerkObject[]` split by channel; `(Hero, flags) → channel
  sum`; and the per-party captain assignment. Perks change only on level-up, so a daily-tick clear suffices;
  the assignment cache also needs a roster-change/time stamp (no roster version exists).
- **Compat.** These reach the base through the virtual `Campaign.Current.Models.MilitaryPowerModel` /
  `BattleCaptainModel`, so the patches propagate — but a mod replacing either with a non-`Default` subclass
  bypasses this. Null-guard `BattleCaptainModel`.
- **Possible existing bug, adjacent but not part of this task:** `SimulationWeaponModel.GetMeleeClamp` has
  `TwoHandedMace skillCoefficient = 1.125f`, where every other class scales ×1.5 from its 1H twin
  (`Mace = 0.075` → should be `0.1125`; sword `0.133 → 0.199`, axe `0.1 → 0.15`, polearm `0.1 → 0.1495`). As
  written the skill term overshoots the clamp, so 2H maces always pin to `max·(1+2·sm)` — skill-insensitive and
  permanently at their ceiling.

## 7. Config

Put the whole feature behind a toggle (default on), plus `captainPowerScale` (default `1.0`) and
`captainPowerMax` (default `0.4`). Follow `simulationEquipmentPowerWeight` in `RBMConfig/RBMConfig.cs` for the
pattern: the field, the `ReadOrCreate` load, and the `setInnerText` save, all under `/Config/RBMCampaign`.

Dials to tune in-game (none are derived; all need a pass):

- `ArmorConstant` — RBM's armour equation is `100/(100 + armor·armorMultiplier)`, so `≈100/armorMultiplier`
  keeps the passive term consistent with how blows actually resolve.
- `ShieldPassiveWeight` — **keep small, tune last.** Shields already swing `activeFactor` from 1.25× to 4.0×;
  they enter in three places overall. Overweight this and shielded infantry read as strictly dominant, and the
  AI will act on it.
- `s`, the assumed ranged share (~0.7); the 0.7/0.3 best/average split; `k` for `ChargeDamage`; and how
  `skillModifier` feeds `max·(1+2·sm)`.

## 8. Consequences to expect

- **No anchoring** — the number is on an entirely new scale. Renown and influence read `MapEventSide` strength
  absolutely and **will shift**; AI thresholds were tuned against the tier curve and are all on a new footing at
  once. Expect a tuning pass, and keep the toggle so it can be switched off wholesale.
- **Displayed strength no longer predicts auto-resolve.** Auto-resolve keeps vanilla's tier base plus the
  existing sim correction; this is a *different* gear model. Both are gear-aware, so they agree in direction but
  not in magnitude. This is the accepted price of not breaking the sim (§2).
- **Tier is gone**, so a tier-6 troop in rags reads weak and a well-equipped low-tier troop can outrank him.
  That is intended.

## 9. Verification

1. Two same-tier stacks, one well-armoured and one ragged → party strength should differ clearly (previously
   identical). Toggle the feature off → back to vanilla.
2. Two-handed axe troop vs one-handed axe troop, gear otherwise equal → ~1.33× offense.
3. Toggle `rbmCombatEnabled` → melee offense switches path (class ceiling ↔ listed damage).
4. **Leader channel:** a lord with `PartyLeader`-role combat perks raises the strength of *all* his troops; a
   perk-less lord does not.
5. **Captain channel:** add a companion with many Bow captain perks to a party with archers → archer strength
   rises while cavalry does not; remove him → it drops back. `captainPowerScale = 0` → vanilla's leader term.
6. **Auto-resolve must be unchanged.** Run identical battles before/after; outcomes should match. This is the
   regression that matters most — see §2.
7. **Perf:** no campaign-map stutter. Verify the caches actually hit (temporary counter/log) rather than
   assuming.

## Out of scope

- Auto-resolve / `SimulateHit` / `SimulationEquipmentPower` — must not change.
- `CharacterObject.GetPower` / `GetPowerImp` / `GetBattlePower` — RBMCombat's tier/noble formula stays as is.
- All economy models (recruit cost is `DefaultPartyWageModel.GetTroopRecruitmentCost`, a level-banded step
  function, and is not `GetPower`-driven; `GetPower`'s only real consumers are `DefaultValuationModel`
  barter-valuation and `DefaultPrisonerDonationModel` donation influence).
