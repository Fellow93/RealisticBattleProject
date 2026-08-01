# Garrison / Militia / Wall Economy — Design Plan

Rework of soldier maintenance, salaries and fortification costs across the three
settlement types (village / castle / city), reconciling the design spec against the
current RBMCampaign implementation.

**Status:** Tasks 1, 2, 3, 4 (4a+4b) and 5 (5a) and 6a implemented and building. 4c, 5b, 6b dropped;
6c deferred. Nothing left outstanding.
**Decisions locked** (this session): garrison wages = *fief-first, owner backstop* (share 1.0);
city garrison food = *free*; wall upgrades (6c) = *deferred*; owner-only garrison recruitment
(4c) = *dropped* — garrison size is shaped by Task 3's affordability/cost gates instead, and
vanilla allied-lord reinforcement is kept.

**Task 3 decisions** (from the research pass, as later narrowed by the user): (3c) auto-recruit cost
redirected owner→fief (suppress `AutoRecruitmentExpenses`, charge fief the equipment value); gear =
FULL TWO-LEG for militia (draw kit off settlement/bound-city `ItemRoster` + pay the pot), money-only
for garrison (spec: paid by castle/city WEALTH). Charge site (3a): gate read-only in the
`CalculateMilitiaChange` postfix + charge the realized whole-man day-over-day delta in RBM's
daily-settlement handler (never move money in the model).

**(3d) garrison-upgrade fief billing — IMPLEMENTED.** Garrison parties are pulled out of the spoils
economy (`SpoilsPool.Wages` skips `IsGarrison`, so they accrue no wage-spoils), and a garrison
promotion is billed straight to the fief's treasury: `SpoilsUpgradePatches.GetPossibleUpgradeTargets`
gains a garrison branch that clamps the batch to what the treasury holds over its 30×-wage reserve
(and 10× a man's cost), and `ApplyEffects` debits the fief (`Source.Upgrade`) in place of the absent
owner's gold — which `SupplyUpgradeFromTown` then credits to the town's citizens, conserving. The
GENERAL/player upgrade path is untouched; only the ownerless-garrison branches are new.

**(3f / 5b) horse-presence gate — DROPPED ENTIRELY (user).** No mount gating on spawn, recruit, or
upgrade. `HasHorseInStock` and the garrison mounted-recruit check were removed. Task 3 = the militia +
garrison SPAWN equipment costs, their affordability gates, and the garrison-upgrade fief billing.

Confirmed hooks: militia apply `Town.cs:638`/`Village.cs:235` (once/day via DailyTickSettlement);
garrison spawn `GarrisonRecruitmentCampaignBehavior.TickAutoRecruitmentGarrisonChange` (adds free,
accrues `OwnerClan.AutoRecruitmentExpenses` at :91, prefix-skippable); garrison upgrades run through
`SpoilsUpgradePatches.OverrideUpgradeReadyTroops` with NO `IsGarrison` skip and garrison troops
accrue full wage-spoils (no `IsGarrison` guard in `SpoilsPool.Wages`); horse gate seams
`GreyUpgradesWithoutSupplyTown` / `GateUpgradeOnSupplyTown` / `OverrideUpgradeReadyTroops.Prefix` /
`RecruitSupply` volunteer postfix; no horse-in-stock helper exists (new simple `ItemTypeEnum.Horse`
roster scan).

---

## 0. Shared vocabulary and infrastructure

### 0.1 The pots (already exist — see `Settlements/SettlementWealth.cs`)

| Name in spec | RBM backing | Which settlements |
|---|---|---|
| **Citizen wealth** (market money) | vanilla `SettlementComponent.Gold` | **towns only** |
| **City/Castle wealth** (treasury) | RBM `_settlementWealth` dict (Pot B) | towns & castles |
| **Village wealth** | vanilla `Gold` field of the village | villages |

Village and castle have **no** citizen pot. A village that needs market goods sources
them from its **bound town** (`Village.TradeBound`) — already the pattern in
`Economy/RecruitSupply.cs` (debit village `Gold`, credit bound-town citizen wealth,
draw kit from bound-town `ItemRoster`). Reuse that pattern everywhere the spec says
"bought from citizen wealth / inventory in the bound city".

### 0.2 Settlement tier

`settlement.Town.GetWallLevel()` returns 0–3 (0 = fortifications building unresolved).
Clamp to 1–3 for cost tables. No new state needed.

### 0.3 Cost routing matrix (the whole spec, condensed)

| Purpose | Village | Castle | City (town) |
|---|---|---|---|
| Militia **salary** | free | free | 25% of wage: debit city treasury → credit citizens |
| Militia **maintenance** | village wealth ← bound-city citizen wealth | castle wealth | citizen wealth |
| Militia **spawn / upgrade gear** | village wealth ← bound-city citizen wealth/stock | castle wealth | citizen wealth / trade stock |
| Militia **soft cap** | 40% hearths (city-bound) / 50% hearths (castle-bound) | 50% prosperity | 70% prosperity |
| Garrison **wages** | — | castle wealth, owner backstop | city wealth, owner backstop |
| Garrison **food** | — | free from castle stocks | free from city stocks/market |
| Garrison **spawn / upgrade gear** | — | castle wealth | city wealth |
| Garrison **recruiter** | — | owner only | owner only |

### 0.4 Affordability helper (generalise the existing `MilitiaUpkeep.CanKeepMilitia`)

```
CanAfford(pot, dailyMaintenance, unitCost, maintDays, unitMult)
    = pot >= maintDays * dailyMaintenance   // keep N days of upkeep in reserve
   && pot >= unitMult  * unitCost           // and cover this spawn/upgrade M times over
```

| | maintDays | unitMult |
|---|---|---|
| Militia | 20 | 5 |
| Garrison | 30 | 10 |

Wage payment for **existing** troops is *not* gated by the reserve (you must pay who you
already have; shortfall backstops to the owner). The reserve only gates **new**
spawn/recruit/upgrade.

---

## Task 1 — Militia salary & maintenance pot routing

**Files:** `Settlements/MilitiaUpkeep.cs`, `Spoils/SpoilsPool.Wages.cs`.

**Current:** one flat 10%-of-wage stipend (`MilitiaWageShare = 0.1`) debited from the
settlement's own Pot B for every settlement type; `CanKeepMilitia` keeps 30 days of that
stipend in reserve and otherwise sheds 1 militia/day.

**Change — split salary from maintenance and route by type:**

- **Maintenance leg** (reframe the existing 10% stipend as *equipment upkeep*, not salary):
  - Village → debit village `Gold`, **credit bound-town citizen wealth** (goods bought there).
  - Castle → debit castle wealth (Pot B). *(No change in pot, only in framing.)*
  - City → debit **citizen wealth** (Pot A), **not** the treasury.
- **Salary leg** (new, city only): 25% of militia wage, debit **city treasury (Pot B)**,
  **credit citizens (Pot A)** — a treasury→citizen transfer (the city paying its part-time
  militia). Village/castle militia draw no salary (feudal levy).
- Rework `CanKeepMilitia`: reserve = **20× the maintenance bill** (was 30× the stipend).
  Keep the -1/day shed when unaffordable.

**Config:** `MilitiaMaintenanceWageShare` (default 0.10, was `MilitiaWageShare`),
`MilitiaCitySalaryShare` (default 0.25), `MilitiaReserveDays` (default 20).

**Risk:** city militia now pull from citizen wealth (market Gold), which is a busier pot —
verify it doesn't starve the market. The salary transfer nets zero denars overall (treasury
→ citizens), only redistributes.

---

## Task 2 — Militia soft caps by hearth / prosperity %

**File:** `Settlements/MilitiaUpkeep.cs` (extend the existing
`CalculateMilitiaChange` postfix — do **not** rewrite the vanilla growth model).

**Current:** no percentage cap. Vanilla's implicit 2.5%/day retirement equilibrium plus
RBM's wealth shed are the only limits.

**Change — cap base by type, throttle over-cap growth to 10%:**

```
cap = village-bound-to-city   -> 0.40 * Village.Hearth
      village-bound-to-castle -> 0.50 * Village.Hearth   (Village.TradeBound.IsCastle)
      castle                  -> 0.50 * Settlement.Town.Prosperity
      city                    -> 0.70 * Settlement.Town.Prosperity

if currentMilitia >= cap and change > 0:  change *= 0.10   // soft cap
```

Compose with Task 1's affordability shed (take the lower of the two) so an over-cap **and**
broke settlement still sheds rather than creeps up at 10%.

**Config:** `MilitiaCapVillageCity` 0.40, `MilitiaCapVillageCastle` 0.50,
`MilitiaCapCastle` 0.50, `MilitiaCapCity` 0.70, `MilitiaOverCapGrowthFactor` 0.10.

---

## Task 3 — Militia + garrison spawn / upgrade equipment cost (+ affordability gates)

The biggest new mechanic. Vanilla spawns militia and auto-recruits garrison **for free**
(from the model / owner-clan auto-recruit gold that RBM's ledger doesn't see).

**3a — Militia spawn cost.** In the same `CalculateMilitiaChange` postfix chain, once the
capped + maintenance-limited growth `Δ` is known:
- price the new men at the culture militia troop equipment value (`SpoilsPool`'s equipment
  valuation) × `Δ`;
- clamp `Δ` down to what the pot can fund while keeping the **5×-spawn-cost** reserve;
- debit the maintenance pot for the men actually added (village → bound-town citizen wealth,
  castle → castle wealth, city → citizen wealth).

Militia "upgrade" is abstract in vanilla (the model re-tiers `Settlement.Militia`; there is
no per-man upgrade event). **Approximation:** value the spawn at the tier mix the model
produces, so higher-tier militia inherently cost more — no separate upgrade hook. *(Flagged
as an approximation to confirm.)*

**3b — Garrison spawn/upgrade cost.** Postfix
`GarrisonRecruitmentCampaignBehavior.TickAutoRecruitmentGarrisonChange`: when the garrison
grows, charge the added troops' equipment value to fief wealth, gated on the **10×** reserve;
if unaffordable, suppress the growth. Garrison **upgrades** run through the AI upgrade path
that RBM already reimplements (`Upgrades/SpoilsUpgradePatches.cs`). **Decision needed inside
this task:** either (i) let garrison upgrades keep flowing through the existing spoils/supply
path, or (ii) exclude garrison parties from the spoils path and bill fief wealth instead
(closer to spec). Recommend (ii) for consistency; note it changes the spoils economy's scope.

**Config:** `MilitiaSpawnReserveMult` 5, `GarrisonSpawnReserveMult` 10 (reuse the 0.4
constants). Master gate stays `SpoilsPool.IsEnabled`.

**Risks:** militia growth is fractional/daily, so "spawn cost" is charged on the day's net
increase, not per-man events — acceptable but means a settlement that oscillates around its
cap pays small daily amounts. Garrison auto-recruit suppression will visibly slow AI garrison
build-up in poor fiefs — intended, but watch for empty frontier castles.

---

## Task 4 — Garrison wages, food, and owner-only recruitment

**Files:** `Settlements/GarrisonUpkeep.cs`, `Production/RBMTownFoodSupply.cs`,
`Production/GarrisonFoodFinanceLine.cs`, `decompiled` garrison behaviors (for the gate).

**4a — Wages: fief-first, owner backstop.** In the `CalculatePartyWage` postfix, replace the
fixed 25% share with: `pay = min(fullWage, fiefWealth)`; debit fief; `__result -= pay` so the
owner clan sees only the shortfall. Drop `TownGarrisonWageShare = 0.25` (or expose it as a
`GarrisonFiefWageShare` defaulting to **1.0**). Applies to castle and city garrisons alike.

**4b — City garrison food: free.** In `RBMTownFoodSupply`, keep the physical stock draw for
garrison/admin "provisioned" units (so food supply/demand still balances) but **remove the
`PayForGarrisonFood` gold leg** — treat garrison rations like civilian rations (no gold).
Remove / zero `GarrisonFoodFinanceLine` (the owner-cost display) since there is no longer an
owner food cost. Castle garrison food is already free — unchanged.

**4c — Owner-only garrison recruitment — DROPPED.** Investigation (see the garrison-fill map)
found auto-recruitment is already owner-only (owner toggle, `OwnerClan.AutoRecruitmentExpenses`,
1/day); the only non-owner path is a same-faction lord leaving his own troops on entering a fief
(`GarrisonTroopsCampaignBehavior.cs:249`, gated only by faction and even rewarded with kingdom
influence). A hard owner-only gate there would thin AI garrisons map-wide. Decision: do NOT add
the lock — let Task 3's affordability/cost gates shape garrison size, keeping vanilla allied-lord
reinforcement.

**Config:** `GarrisonFiefWageShare` 1.0.

**Risk:** 4a shifts real garrison cost off owner clans onto fief treasuries — a meaningful
economy swing. Poor fiefs will let garrisons shrink (wages unpaid → vanilla trims). Validate
against `docs/economy-money-flows.md` and update it.

---

## Task 5 — Recruit hire rules + horse-presence requirement

**Files:** `Economy/RecruitSupply.cs`, `Upgrades/MountValueUpgrade.cs`,
`Upgrades/UpgradeSupply.cs`.

**5a — Owner-free / outsider +10%.** Determine recruiter identity vs. the settlement:
- **Free** (no gold, feudal levy) if `recruiter.Clan == settlement.OwnerClan` **or** the
  recruiter is the ruler of the settlement's kingdom.
- **Outsider:** full price **+10%**, the surcharge credited to the settlement's wealth
  (village → village wealth; city → citizen wealth).

Implement in the recruitment-cost postfix (`OverrideGetTroopRecruitmentCost`) and the money
leg (`PayRecruitPrice`). Replaces the current uniform 5-day enlistment premium (or keep the
premium only for outsiders — confirm).

**5b — Mounted units require horses present — FOLDED INTO TASK 3.** The requirement getter
(`UpgradeRequiresItemFromCategory`) is a pure `CharacterObject` property with no settlement
context, so the real gate must live in the upgrade-eligibility path (`UpgradeSupply.CanUpgradeNear`
/ the AI `UpgradeReadyTroops` reimpl) and the volunteer-generation path — the same machinery
Task 3 reworks for stock-sourced spawn/upgrade. Doing it standalone would duplicate then collide
with Task 3, so it is implemented there. Design when built: gate mounted spawn/upgrade on a horse
being drawable from the supply settlement / bound-city `ItemRoster`; if none, disallow the mounted
step rather than granting it free; keep pricing the horse in when one *is* available.

**Config:** `OutsiderRecruitSurcharge` 0.10 (const, in `RecruitSupply`); horse-gate toggle to be
added with Task 3.

**5a status:** implemented. Owner/ruler-free via `AddFactor(-1)`; outsider +10% via
`AddFactor(0.10)` on top of the enlistment premium. Money still lands in the supply settlement's
pot through the existing `RegisterRecruitPay` redirect — re-plumbing to village-own / citizen
wealth per the exact spec wording is a noted follow-up, not done.

**Risk:** 5b re-introduces a hard gate that vanilla players expect to be soft — make it a
toggle. Horse stock in towns is thin, so cavalry recruitment/upgrade will slow noticeably.

---

## Task 6 — Wall economy

**File:** `Settlements/AdministrativeUpkeep.cs` (+ `CastleEconomy.cs` constants), plus a new
repair hook for 6b.

**6a — Wall upkeep, per-tier, both settlement types.**

| Tier | Castle/day | City/day |
|---|---|---|
| 1 | 150 | 200 |
| 2 | 300 | 400 |
| 3 | 450 | 600 |

Formula: `base × tier` (castle base 150, city base 200). Replace
`WallUpkeepPerLevel = 75 × wallLevel` (castle-only today) and **add the town branch** in
`AdministrativeUpkeep.OnDailyTick` (towns currently pay only the admin wage). Both from
Pot B. **Config:** `CastleWallUpkeepPerTier` 150, `CityWallUpkeepPerTier` 200.

**6b — Wall repair cost per damaged section.** `tier × 100 000` (t1 100K / t2 200K /
t3 300K), from settlement wealth; if unaffordable, repair stalls. **Requires research:**
find the native wall-section damage/repair hook (post-siege wall hitpoint regen — likely in
`SiegeEventCampaignBehavior` / the wall-section hitpoint model) and decide whether to levy
per section as it repairs or as a lump when a section returns to full. Add a
hook-discovery step before implementing. **Config:** `WallRepairCostPerTierPerSection` 100000.

**6c — Wall upgrade gold cost — DEFERRED** (per decision). Vanilla builds fortifications with
construction points; layering a 12M–40M owner-paid gold gate is a separate later task once
6a/6b are validated. Left unspecified here on purpose.

**Optional / future (spec musings, not in scope):** routing a fraction of wall costs back to
citizen wealth; consuming pottery as "bricks" for a fraction of construction.

---

## Suggested build order & dependencies

1. **Task 1** (militia salary/maintenance routing) — self-contained, low risk. Establishes
   the pot-routing + affordability helper the rest reuse.
2. **Task 2** (militia caps) — small, independent postfix extension.
3. **Task 6a** (wall upkeep per-tier) — trivial, independent.
4. **Task 4a/4b** (garrison wages fief-first + free city food) — independent of militia work.
5. **Task 5** (recruit owner/outsider + horse gate) — independent.
6. **Task 3** (militia + garrison spawn/upgrade cost) — heaviest; depends on Task 1's helper
   and touches Task 4's garrison path. Do after 1 & 4.
7. **Task 6b** (wall repair) — needs a hook-discovery research step first.
8. **Task 4c** (owner-only garrison recruitment) — optional, ship last.
9. **Task 6c** (wall upgrade cost) — deferred.

## Cross-cutting checklist per task

- Every new debit/credit must be traced in `docs/economy-money-flows.md` (update it).
- New config fields: add to `RBMConfig/Config/RBMConfig.Campaign.cs` **and** the settings-UI
  `[DataSourceProperty]` in `RBMConfigUI/RBMConfigViewModel.Campaign.cs` (completeness is
  compiler-enforced — see the RBMConfig reorganization note).
- Master gate stays `RBMConfig.rbmCampaignEnabled` + `SpoilsPool.IsEnabled` where money moves.
- Update `RBMCampaign/ARCHITECTURE.md` and per-folder docs as behaviors change.
- No test suite — plan manual in-game verification per task (militia counts, garrison
  wages on the clan finance screen, wall upkeep on the settlement tooltip).
