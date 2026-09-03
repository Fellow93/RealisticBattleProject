# Buildings & construction rework — plan (2026-09-03)

Status: PLAN ONLY, nothing implemented. Companion to `GARRISON_MILITIA_WALL_ECONOMY_PLAN.md`
(its deferred task 6c "wall upgrade gold cost" is subsumed by this plan).

## 0. What exists today (verified)

Vanilla (`decompiled/TaleWorlds.CampaignSystem/...`):
- `DefaultBuildingConstructionModel.CalculateDailyConstructionPower`: base = `Prosperity * 0.01`
  (no flat base), + boost `50 * min(1, reserve/500)` (castle 20/250), + governor skill/perk terms,
  + `0.25 * market goods with BonusToProduction` ("Construction from Market"), + `ConstructionPerDay`
  building effect (Mason 3/6/9), then loyalty factor (+20% at 100, −50% at 25, halted ≤25).
- `BuildingsCampaignBehavior.TickCurrentBuildingForTown`: `progress += town.Construction`, then
  drains the FULL `GetBoostCost` (500) from `Town.BoostBuildingProcess` every day. Skipped when the
  current project is a daily project — construction power is simply discarded.
- `Town.BoostBuildingProcess` is a plain denar reserve, vanilla-saved. Only writer is the player via
  `BuildingHelper.BoostBuildingProcessWithGold` (`TownManagementReserveControlVM`, cap 10,000).
- Costs: `BuildingType.GetProductionCost(level)`; town buildings 1,200–12,000, castles 280–2,800.
  `Building.GetConstructionCost()` applies the CastleCharters 0.8 policy. Progress carries over on level-up.
- AI project choice is pure random (`DefaultBuildingScoreCalculationModel`), 10%/day queue pick.
- Prisoners: `Settlement.Party.PrisonRoster`; only effect today is the governor ForcedLabor perk.
- Wall level → `DefaultCombatSimulationModel.GetSettlementAdvantage` (round-tick scaling only).

RBM:
- The ONLY money↔construction contact point is `Settlements/ConstructionLabour.cs`
  (`BoostBuildingProcessWithGold` prefix/postfix): player boost gold → treasury → citizens immediately.
  There is no daily construction charge; "construction upkeep" in ARCHITECTURE.md is a misnomer.
- Building reads: `RBMProsperityEquilibrium.InfrastructureMultiplier` (Σ levels × 0.02, cap 2.0),
  `RBMTownFoodSupply` (FoodProduction/FoodConsumption effects), `MilitiaUpkeep` (Militia effect),
  `SimulationSiege.MeasureWall` (wall level), `AdministrativeUpkeep` wall upkeep.
- Food cap: `RBMTownFoodSupply` postfix on `Town.FoodStocksUpperLimit` = vanilla (300 + FoodStock
  effects) × `TownFoodStockScale`, towns only. Not days-based.
- Daily settlement pass: `RBMSettlementWealthCampaignBehavior.OnDailyTickSettlement` — CastleEconomy,
  Minting, AdministrativeUpkeep, GarrisonUpkeep, MilitiaUpkeep, GarrisonRecruitCost, WealthTax, flushes.
- **Patrols: NOT on the branch.** `PatrolUpkeep.cs/.Patches.cs/.Naval.cs` + config/prefab wiring exist
  only in `stash@{0}` ("!!GitHub_Desktop<campaignModule>", 2026-08-27, ~1,000 lines, build-verified,
  never played). Must be popped, rebased on the current branch and committed before Guard House work.

## 1. Construction engine (phase 1)

New file `Settlements/Construction.cs` (+ `.Patches.cs`, `.Materials.cs`), gated on
`rbmCampaignEnabled`. Units: **1 construction point = 1 coin** (vanilla boost is 10:1).

### 1.1 Cost of buildings
- Postfix `BuildingType.GetProductionCost(int level)` → `__result *= buildingCostMultiplier`
  (config, default **250**). Flows through `Building.GetConstructionCost`, `CheckIfBuildingIsComplete`
  and every tooltip. Old saves keep their progress (smaller fraction, no reset).
- Resulting scale: town Fortifications L2→L3 = 3,000,000; Barracks L1 = 450,000; castle Barracks L1 = 105,000.

### 1.2 Daily cap (potential)
```
cap = prosperity * 36                        // 90 × prosperity × 0.4
    + prisoners * 60                          // Settlement.Party.PrisonRoster.TotalManCount
    + guardHouseTier * 0.6 * prosperity       // passive prisoners (§2.4)
cap *= 1 + 0.1 * masonTier                    // §2.7
cap *= loyalty factor (vanilla curve, kept)   // halted ≤25 loyalty
```
Free-labour discount: `free = prisoners * 30 + guardHouseTier * 0.3 * prosperity` points/day cost nothing.

### 1.3 Budget (the reserve)
- Reuse `Town.BoostBuildingProcess` as the construction budget (already saved, already in the UI).
- Each day, in the RBM settlement pass **after** garrison/militia/admin bills and WealthTax:
  `deposit = 1% of settlement wealth` → `SettlementWealth.Debit(Source.Construction)` →
  `BoostBuildingProcess += deposit`. (Simple 1% as the spec allows; threshold variant noted in §4.)
- Player deposits keep the vanilla UI; `ConstructionLabour` is rewritten so player gold only enters the
  reserve (no instant citizen credit). Raise `TownManagementReserveControlVM.MaxReserveAmount`
  (postfix) from 10,000 to e.g. `min(gold, 10 × cap)`.

### 1.4 Daily tick (replaces vanilla)
- Prefix `BuildingsCampaignBehavior.TickCurrentBuildingForTown` → return false (vanilla behavior keeps
  only wall heal + AI queue/daily-project picks). Our tick runs from the daily pass:
```
target = queue head, else random lowest-level non-daily building with cap *= 0.25
points  = min(cap, affordable(reserve))
1. materials: up to 50% of points from market clay (300/pc) and planks (50/pc), only stock above 20
   pieces; pay market price treasury→citizens (BuyLine idiom, CitizenDemand.cs:512), levy tariff.
2. free labour: next `free` points cost 0.
3. cash: remaining points cost 1 coin each from the reserve → 0.5 to citizens (salaries), 0.5 sink.
   (Material-covered points also pay the 0.5 salary.)  ← ASSUMPTION, see §4.
4. mason efficiency: points *= 1 + 0.05 * masonTier (applied to what money bought, cap unchanged).
5. tools: toolDebt += points / 50,000; while toolDebt ≥ 1 and market has tools, consume one at
   market price (treasury→citizens). toolDebt ≥ 1 with no tools in the market → today's points are
   halved (debt persists until a tool is bought).
6. building.BuildingProgress += points; BuildingHelper.CheckIfBuildingIsComplete.
```
- Under siege: skip (vanilla rule).
- Bonuses that were "passive" (governor perks, Battanian feat, market BonusToProduction goods,
  Confidence/SelfMadeMan) become **factors on funded points**, never free points.
- `DailyTickSettlement`'s daily-project skip no longer matters: the fallback (25% of cap, random
  lowest-tier building) runs whenever the queue is empty and the reserve is positive.

### 1.5 UI / model
- Postfix `DefaultBuildingConstructionModel.CalculateDailyConstructionPower` (+ `WithoutBoost`) to
  return the projected funded points with `ExplainedNumber` lines: Cap, Reserve, Prisoners, Guard House,
  Mason, Materials, Loyalty. `BuildingHelper.GetDaysToComplete` and the project list read this.
- Postfix `GetBoostCost`/`GetBoostAmount` so vanilla's 500-per-day drain never runs (our tick owns the reserve).
- Ledger: `SettlementWealth.Source` gains `Construction` (exists), `BuildMaterials`, `ConstructionTools`;
  ledger lines in the RBM Ledger screen come free via `Debit/CreditCitizens`.
- Log: `EconomyLog` tag `"BUILD"` (≤8 chars): target, cap, funded, materials, tools, reserve left.
- Persist: `RBM_constructionToolDebt` (`Dictionary<string,float>`) in the behaviour's `SyncData`.

### 1.6 Config
- `buildingCostMultiplier` (250), `constructionBudgetShare` (0.01). Wire through
  `RBMConfig.Campaign.cs` / `.Core.cs` load+save / `RBMConfigViewModel.Campaign.cs` / `.Core.cs` /
  `RBMXML/GUI/Prefabs/RBMConfig.xml`. Everything else is `Construction` consts.

## 2. Building effects (phase 2) — one seam per building

Vanilla effects stay unless a row says "replace".

| Building | New RBM effect | Seam |
|---|---|---|
| **Fortifications** L1/2/3 | auto-resolve defender +10/20/30% | `SimulationSiege.MeasureWall` → `1 + 0.1 × level` (currently a downward-only 0.25 step from L3; replace). |
| | garrison + militia maintenance −0/5/10% | `GarrisonUpkeep.MaintenanceBill`, `MilitiaUpkeep.DailyMaintenanceBill` |
| **Barracks** | garrison + militia spawn price −5/10/15% | `GarrisonRecruitCost.SpawnCost`/`ArmOneGarrisonTroop`, `MilitiaUpkeep.ArmOneMilitiaman` |
| | growth +1/2/3 per day when funded | `GarrisonRecruitCost.Compute` (`GarrisonSpawnDailyMax` term), `MilitiaUpkeep.CalculateMilitiaChange` intake |
| **Training Field** | garrison + militia upgrade −5/10/15% | `SpoilsUpgradePatches` garrison branch (+ militia branch if any) |
| | XP ×10 (10/20/30 per day), militia too | `GarrisonDrill` postfix on `GetEffectiveDailyExperience`: add `10 × ExperiencePerDay` for garrison AND `MilitiaPartyComponent` parties |
| **Guard House** T0/1/2/3 | T0 spawns a light patrol; each tier adds one patrol | needs multi-patrol (vanilla single `settlement.PatrolParty` slot) — see §3 |
| | tariff +0.3/0.6/1.0% on caravan + player trades | `TradeTariff.Levy` gains a channel arg; only `RouteNativeWrite` + `DoneLogic` channels get the bonus |
| | patrol maintenance −25/50/75% | `PatrolUpkeep.PayPatrolUpkeep` (stash) |
| | passive prisoners | §1.2 cap/discount terms |
| **Siege Workshop** | unchanged | — |
| **Tax Office** | +5/10/15% on wealth tax (owner + fief), optionally minting cuts | `WealthTax.OnDailyTick` rates × `(1 + TaxPerDay effect)`; same factor in `Minting` |
| **Marketplace** | tariff ×1.1/1.2/1.3 on ALL channels (incl. citizen consumption) | `TradeTariff.Levy` rate factor from `TariffIncome` effect |
| **Warehouse / Granary** | food cap = days × daily consumption; tier 0/1/2/3 = 10/20/30/40 days | replace `RBMTownFoodSupply` `FoodStocksUpperLimit` postfix: `days × GetFoodConsumption(town).Total`; extend to castles (Granary) |
| **Mason** | replace ConstructionPerDay: efficiency ×1.05/1.1/1.15, cap ×1.1/1.2/1.3 | §1.2/§1.4 |
| **Waterworks** | infrastructure bonus ×1.1/1.2/1.3 | `RBMProsperityEquilibrium.InfrastructureMultiplier`: `1 + score×0.02×(1+0.1×tier)` |
| **Courthouse** | unchanged | — |
| **Roads** | verify VillageProduction factor reaches RBM production (it does NOT today) | add `village.Bound.Town.AddEffectOfBuildings(VillageProduction)` in `RBMVillageProduction`; VillageHeartsPerDay still flows via vanilla hearth model |

Castle equivalents map 1:1 (CastleFortifications/Barracks/TrainingFields/GuardHouse/Mason/Granary).

## 3. Guard House & patrols (phase 3)

1. Pop `stash@{0}`, resolve against current `campaignModule`, build, commit ("Campaign: wealth-funded
   patrols (phase 1)"). Nothing else in this section is possible until then.
2. Multi-patrol: vanilla keeps one patrol per settlement (`Settlement.PatrolParty`,
   `PatrolPartiesCampaignBehavior.CanSettlementSpawnNewPartyCurrently`). Options: (a) track extra
   patrols in an RBM-owned list and spawn them ourselves via the same `SpawnPatrolParty` path, keeping
   vanilla's slot as patrol #1; (b) transpile the single-slot checks. Recommend (a) — mirrors the
   villager "second convoy" lift in `VillagerDispatch`.
3. Tier 0 = one weak patrol whenever `CanFundPatrol`; tier N adds N more (moderate/strong by budget).
4. Maintenance discount and tariff bonus per table in §2.

## 4. Decisions (confirmed 2026-09-03) and remaining assumptions

- **Salary vs sink split — DECIDED**: every funded point pays 0.5 to citizens; the other 0.5 of a
  cash-paid point is a sink; material-paid points pay the item price instead of that 0.5.
- **Budget rule — DECIDED**: plain 1% of settlement wealth per day, no threshold.
- **Tools unavailable — DECIDED**: when `toolDebt ≥ 1` and the market has no tools, that day's output
  is halved (the debt stays until a tool is bought). §1.4 step 5 updated accordingly.
- **Scale check**: 5,000-prosperity town → cap 180k/day, but 1% of a ~1M treasury is ~10k/day, so
  construction is budget-bound; a 1.5M project takes ~150 days unless the player funds it. Castles
  (treasury ≈ 210×prosperity, low prosperity) will build very slowly. Acceptable? If not, lower
  `buildingCostMultiplier` or raise the budget share.
- **Vanilla effects kept**: Barracks `GarrisonWageReduction` (−5/10/15% wage) and `GarrisonCapacity`,
  Waterworks `FoodConsumption`, Marketplace `CaravanAccessibility` stay in place unless told otherwise.
- **Player-clan queue**: vanilla only clears the queue on owner change; the AI random pick is unchanged.

## 5. Order of work

Decision 2026-09-03: **patrols deferred** — §3 and the Guard House patrol rows in §2 are out of scope
for now. The stash stays parked; Guard House ships only its tariff bonus and the passive-prisoner
cap/discount terms in this pass.

1. Phase 1 engine, log-verified in-game over ~30 days on one town + one castle.
2. Phase 2 effects, one building per commit, each on its named seam (Guard House: tariff + passive
   prisoners only).
3. Later: pop the patrol stash, then §3.
