# RBMCampaign — Architecture / Developer Notes

Technical companion to `README.md` (which is the player-facing description). This document
is for a developer working on the module: what it hooks, how the pieces fit, and where state
lives.

## Big picture

RBMCampaign is the 6th C# project. It has its own Harmony instance `com.rbmcampaign` and is
gated by the config toggle `rbmCampaignEnabled` (default on).

It has grown well past the spoils system this document was first written for. The module now
also carries the **settlement wealth ledger** (`Settlements/`), the **village-to-town goods and
food chain** (`Production/`), the **market and caravan economy** (`Economy/`), the
**equipment-aware auto-resolve** (`Simulation/`, `Power/`) and the **spectator battle**
(`Spectate/`). Those have their own documents — see the file map at the foot of this one.
Everything below is about the spoils purse specifically.

> **Money.** `docs/economy-money-flows.md` maps every gold pool in the campaign layer, every flow
> between them, how spoils feed the settlement economy, and which edges still conjure or destroy
> money. Read it before adding anything that moves a denar.

**Spoils = a per-troop-stack purse, denominated in gold.**

Every stack of identical troops in every party (yours and the AI's) has its own hidden purse.
A "point" of spoils is literally one gold piece. The purse is keyed by
`party.Id + "#" + character.StringId` (`SpoilsPool.Key`) — so "Aserai Veteran Infantry in
party X" is one purse, separate from the same troop in party Y. It lives in a side dictionary
(`SpoilsPool._spoils`) because Bannerlord's `TroopRosterElement` struct has no spare field to
hang it on.

The purse **fills** from battlefield loot, raid and siege plunder, and the daily wage, and
**drains** on upgrades, field maintenance, food, carousing, paid healing, the odd luxury, and
the leader's cut.

## How spoils are EARNED

### 1. Battlefield loot (`SpoilsPool.OnMapEventEnded`)

- Only the **dead** are looted — not the wounded (carried off wearing their kit) and not the
  routed (fled with theirs). The winners hold the field, so they recover **both sides'**
  fallen, including their own dead.
- Each dead man is stripped of one of his battle-equipment sets, chosen at random. Every
  armor/weapon slot yields a **random 25–75% of the item's value** (mean 50%; consumables like
  arrows/javelins reflect what's left unspent).
- Loot is **bucketed by item tier**, then handed out to the victorious parties in proportion to
  each party's `ContributionToBattle` (even split if all contributions are zero, e.g. simulated
  battles).
- Within a party, loot follows a **pecking order**: veterans (higher troop tier) pick first.
  The further *beneath* a troop's tier a piece is, the likelier he is to **overlook** it
  (`troopLootOverlookChancePerTier`, compounding per tier of gap). What veterans overlook or
  can't carry cascades to greener troops.
- Each man carries at most `troopLootPiecesPerMan × men in stack` pieces.
- **Player feedback**: post-battle message ("Your men strip the fallen and recover N in
  spoils" / "…find nothing they can use").

### 2. Village raids (`SpoilsPool.OnRaidCompleted`)

Hooks `CampaignEvents.RaidCompletedEvent(BattleSideEnum winnerSide, RaidEventComponent raidEvent)`,
which fires once when a raid concludes. Only on `winnerSide == Attacker`. The pot is
`village.Hearth × RaidDamage × troopRaidSpoilsMultiplier` (`RaidEventComponent.RaidDamage` is the
0–1 share of the village actually stripped). It's split among `raidEvent.AttackerSide.Parties` by
`ContributionToBattle` (even split if all zero — mirrors `OnMapEventEnded`), then spread evenly by
head count within each party via `GrantFlatSpoilsToParty` (plunder is shared, not fought over piece
by piece like battlefield kit). The player gets a "Your men plunder {SETTLEMENT}…" message.

### 3. Town/castle sacking (`SpoilsPool.OnSettlementCaptured`)

Hooks `CampaignEvents.OnSettlementOwnerChangedEvent`, gated on
`detail == ChangeOwnerOfSettlementDetail.BySiege` (so barter/gift/vote transfers grant nothing).
Pot = `town.Prosperity × troopRaidSpoilsMultiplier` (prosperity, not hearth — towns are measured in
it; the same knob tunes both). Granted to the **single capturing party** — `capturerHero` (fallback
`newOwner`) → `PartyBelongedTo.Party` — via `GrantFlatSpoilsToParty`, mirroring how the game credits
a capture to one hero rather than splitting across a besieging army. Player gets a "Your men sack
{SETTLEMENT}…" message. (Army-wide splitting is a possible future refinement.)

### 4. Wages (`SpoilsPool.OnDailyTickParty`)

Each day, every non-hero stack's full wage is deposited into its purse.
**The party's actual gold is untouched** — this only reinterprets where some of the wage
notionally went (kit maintenance). Applies to every party in the world.

## How spoils are SPENT

### 1. Troop upgrades (main sink)

- The equipment-value delta between a troop and its upgrade target *is* the gold cost. Spoils
  pay it, consumed **one man at a time** — if the purse covers 2.5 men, the first 2 upgrade
  free, the 3rd pays half, the rest pay full.
- Base price runs through the vanilla perks/feats (Steward SoundReserves, Bow RenownedArcher,
  Khuzait feat, Steward Contractors) then `troopUpgradeCostMultiplier`.
- Patches `DefaultPartyTroopUpgradeModel.GetGoldCostForUpgrade` to quote the *next* man's price.

### 2. Food in settlements (`TroopUpkeep`)

- On settlement enter and each hour it stays, each unprovisioned stack buys `troopSettlementFoodDays`
  days of food off the market — **real items, real stock, real prices**.
- Buys the best fare it can afford first; per-item ceiling scales with wage
  (`troopFoodWageFraction`). Recruits buy grain, veterans buy meat/cheese. Falls back to
  anything rather than starve; limited by market stock and purse.
- Patches `DefaultMobilePartyFoodConsumptionModel.CalculateDailyBaseFoodConsumptionf`: men
  carrying their own rations **stop eating party food stores**. Heroes always eat from stores.

### 3. Carousing (`TroopUpkeep.SpendOnFun`)

Each hour idling in a settlement, each stack spends `troopSettlementFunWageFraction` of its
daily wage on drink/dice — a quarter of a day's wage at the default, plus a surplus term that
bites harder the further over its cap the purse stands. Purse never goes negative.

**Garrisons and militia are excluded** from food/carousing (they never leave; would be an
infinite spending faucet). Only visiting field parties spend in settlements.

### 4. The spoils cap (`SpoilsPool.GetSpoilsCap`, `Spoils/SpoilsPool.Cap.cs`)

Not a sink of its own — a ceiling the sinks above read. Each stack's cap
`GetSpoilsCap` = `(dailyWage + dailyMaintenance) × troopSpoilsCapDays`, i.e. a configured number of
days' worth of the stack's own keep — its daily wage (`PartyWageModel.GetCharacterWage × stackSize`)
and its daily field maintenance (`DailyMaintenanceCost`, the same per-stack upkeep §7 charges). Priced
the same for every tier: a veteran's dearer wage and kit already make his days' keep the deeper purse,
so there is no separate war chest and a top-tier troop with no upgrade to save for is held to the same
rule. `troopSpoilsCapDays` is 0–60 (default 20); 0 collapses the cap to nothing.

The cap governs *behaviour*, not storage: a purse may sit over its cap (loot and wage both fill past
it), but once it does, upkeep starts drawing the surplus down — carousing bites harder (§3) and only
over-cap stacks splurge on luxuries. Nothing over the cap is handed back to your gold — the surplus
is drunk and eaten where the men stand, which credits that settlement's purse rather than yours.

Spoils reach gold at exactly one point, the leader's cut (`Spoils/SpoilsPool.LeaderCut.cs`), and it
is conserving: the share is drawn back out of the same purses the gather just filled, so no coin is
minted. `GetPartyPayee` (owner if alive, else `LeaderHero`) also lives here — the cut pays through it.

## Who it applies to

- **Player** — party screen (upgrades) and settlement visits.
- **AI** — `SpoilsUpgradePatches` reimplements `PartyUpgraderCampaignBehavior.UpgradeReadyTroops`
  so AI lords draw down spoils on upgrade, affordability checked against the discounted price.
  (Vanilla's helpers are private and pass a private struct — can't be patched directly, hence
  the full reimplementation.)
- Fully symmetric: AI parties loot, earn wage-spoils, and spend on upgrades like the player.

## Player-visible UI

1. **Spoils bar** on the party screen — `RBMTroopSpoilsBarWidget` (a `FillBarVerticalWidget`)
   injected into the native party-screen prefab.
2. **Upgrade tooltip breakdown** — patches `CampaignUIHelper.GetUpgradeHint` → full worth,
   "Spoils cover: X", "You pay: Y".
3. **Party-screen staging** — `PartyScreenStagedUpgrades` reserves spoils for queued-but-unconfirmed
   upgrades and fixes vanilla's gold math (vanilla multiplies one per-man price by batch size,
   overcharging when spoils make leading men free). Cleared on screen reset/close.
4. **Post-battle loot message**.

## Save/load

Serialized via `SyncData`:

- `RBM_troopSpoilsGold` — the purses (`SpoilsPool`).
- `RBM_troopFedUntilHours` — when each stack next needs food (`TroopUpkeep`).
- `RBM_troopLuxuryCooldown` — when each stack may indulge again (`TroopUpkeep`).
- `RBM_townTroopTrade` — what troops have spent in each town (`TroopMarketFeedback`).
- `RBM_settlementWealth` — the settlement treasury pot (`SettlementWealth`).

⚠️ A persisted store must be reset in its behavior's **constructor**, not from `OnSessionLaunched`:
`LoadBehaviorData` runs before `RegisterEvents` on load, and an absent key leaves the field
untouched, so a null-guard never catches a cross-campaign leak.

The save key was deliberately renamed when a spoils point's meaning changed (was "equipment
value," now "1 gold"), so **old saves drop stale pools** rather than misreading them. Purses are
pruned when a stack disappears (upgraded away / killed) or a party is destroyed — spoils die
with the stack, like its XP.

## Config knobs

All under `/Config/RBMCampaign` in the config XML, wired into the in-game settings UI. **Only the
spoils knobs this document discusses are listed here** — the maintenance, healing, luxury,
leader-cut, supply-town, trade-good and simulation settings are tabulated in
[README.md](README.md#tuning-it), and the store itself is `RBMConfig/Config/RBMConfig.Campaign.cs`
plus `RBMConfig.Simulation.cs`.

| Setting | Default | Effect |
|---|---|---|
| `TroopUpgradeCostMultiplier` | 1 | Scales upgrade gold *and* spoils cost. **0 disables the whole system** (`SpoilsPool.IsEnabled`). |
| `TroopUpgradeSpoilsLootMultiplier` | 1 | How much battlefield loot yields. |
| `TroopLootPiecesPerMan` | 3 | Pieces of kit one man can carry off a field. |
| `TroopLootOverlookChancePerTier` | 0.5 | Chance a troop overlooks kit one tier below him (compounds per tier). |
| `TroopRaidSpoilsMultiplier` | 0.25 | Plunder soldiers pocket sacking a settlement — of a village's `Hearth × RaidDamage`, or a stormed town's `Prosperity`. 0 disables plunder spoils. |
| `TroopSpoilsCapDays` | 20 | Days of keep (daily wage + daily field maintenance) a stack holds in `GetSpoilsCap` — the flush threshold above which upkeep spends surplus on drink/luxuries. Slider 0–60, discrete. |
| `TroopSettlementFoodDays` | 20 | Days of food a stack buys per trip. |
| `TroopFoodWageFraction` | 0.5 | Food price ceiling a man will pay, relative to his wage. |
| `TroopSettlementFunWageFraction` | 0.25 | Carousing spend per day idled, as a multiple of daily wage. |
| `RBMCampaignEnabled` | 1 | Master on/off for the whole module. |
| `SpoilsLoggingEnabled` | 1 | Toggles the diagnostic log file. |

## Diagnostics

`SpoilsLog` writes a detailed trace (loot distribution, wage deposits, upgrade pricing, food
buying, carousing, save/load counts) to `<configFolder>/logs/campaign/rbm_spoils_<yyyy-MM-dd_HH-mm-ss>.log`
— one timestamped file per launch so runs don't overwrite each other, with `LogRetention.PruneOldest`
capping how many are kept. When `developerMode` is on, lines also print to the in-game message log.

`EconomyLog` (`logs/economy/`) and `SimulationLog` (`logs/simulation/`) are the other two sinks,
each with its own config toggle.

## File map

Everything is foldered; the namespace stays flat `RBMCampaign`. The csproj lists every file with an
explicit `<Compile Include>` — **update it when adding or moving one**.

### The spoils purse

| File | Role |
|---|---|
| `Spoils/SpoilsPool.cs` | Purse storage, keying, `IsEnabled`. A `partial static class` split across the files below. |
| `Spoils/SpoilsPool.Equipment.cs` | Equipment valuation and its cache. |
| `Spoils/SpoilsPool.BattleLoot.cs` / `.Casualties.cs` | Loot distribution off a field, and who is strippable. |
| `Spoils/SpoilsPool.Plunder.cs` | Raid and siege plunder pots. |
| `Spoils/SpoilsPool.Wages.cs` | The daily wage deposit. |
| `Spoils/SpoilsPool.Maintenance.cs` | Daily field upkeep and its market hand-off. |
| `Spoils/SpoilsPool.UpgradeMath.cs` | Upgrade pricing, the player-side commit path. |
| `Spoils/SpoilsPool.Cap.cs` | The days-of-keep ceiling and `GetPartyPayee`. |
| `Spoils/SpoilsPool.LeaderCut.cs` | The commander's cut — the one spoils→gold exit. |
| `Spoils/SpoilsPool.Transfers.cs` | Carrying a purse across a party transfer. |
| `Spoils/RBMSpoilsCampaignBehavior.cs` | Event subscriptions and `SyncData`. |
| `Spoils/MaintenanceFinanceLine.cs` / `MaintenancePartyWageLine.cs` | Clan-finance and party-wage tooltip lines (display only). |
| `Spoils/SpoilsTransferOnPartyScreen.cs` | Purse follows men moved on the party screen. |

### Spending and upgrades

| File | Role |
|---|---|
| `Upgrades/SpoilsUpgradePatches.cs` | AI upgrade reimplementation (`UpgradeReadyTroops`). |
| `Upgrades/PartyScreenStagedUpgrades.cs` | Player-side staging and gold-math fix. |
| `Upgrades/RBMCampaignPatches.cs` | `GetGoldCostForUpgrade` + the `GetUpgradeHint` tooltip breakdown. |
| `Upgrades/UpgradeSupply.cs` | The supply-town gate, the market draw, and the payment leg. |
| `Upgrades/MountValueUpgrade.cs` | Pricing the horse instead of consuming one. |
| `Upkeep/TroopUpkeep.cs` (+ `.Food.cs` / `.Healing.cs` / `.Luxury.cs`) | Settlement food, carousing, paid healing, luxuries. |
| `Upkeep/TroopMarketFeedback.cs` | Where troop spending lands in a settlement's purse. |
| `Upkeep/RBMTroopUpkeepCampaignBehavior.cs` | Event subscriptions and `SyncData`. |
| `Wages/TierBasedWageModel.cs` | The per-tier wage table. |

### Everything else in the module

| Folder | Role |
|---|---|
| `Settlements/` | The two-pot settlement wealth ledger (`SettlementWealth`), its funnel over vanilla's writes, tariffs, ransoms, garrison/militia/administrative upkeep, wealth-driven garrison growth (`GarrisonRecruitCost`) and drill XP (`GarrisonDrill`), workshop purses, and the **construction engine** (`Construction.cs` / `.Materials.cs` / `.Patches.cs`). |

#### Construction (`Settlements/Construction*.cs`)

Building work is money. One construction point = one denar, so every project's vanilla price is
multiplied by `buildingCostMultiplier` (config, default 250) at `BuildingType.GetProductionCost`, and
the fief has to fund the work before it happens. (Before this there was no daily construction charge at
all — the "construction upkeep" this table used to list was a misnomer for a single patch on the
player's boost, now deleted.)

- **Budget** — vanilla's own `Town.BoostBuildingProcess` reserve, refilled daily with
  `constructionBudgetShare` (default 1%) of settlement wealth via `SettlementWealth.Debit(Source.Construction)`,
  and toppable up by the owner through the unmodified vanilla reserve UI (its 10,000 ceiling is raised
  to `min(player gold, 10 x daily capacity)`).
- **Ceiling** — `prosperity x 36 + prisoners x 60 + guardHouseTier x 0.6 x prosperity`, times the Mason
  capacity factor (`1 + 0.1 x tier`), times vanilla's loyalty curve. `prisoners x 30 +
  guardHouseTier x 0.3 x prosperity` of it is free labour that costs nothing, and the Mason's efficiency
  factor (`1 + 0.05 x tier`) multiplies what the money bought.
- **Spending order** — free labour, then clay/hardwood off the settlement's own market (up to half the
  day's work, never touching the last 20 pieces on the shelves), then wages at a coin a point of which
  half reaches the townsmen. Tools wear out at one load per 50,000 points and are bought the same way;
  a load owed with no tools on the market halves the day's output. Nothing calls `ChangeGold`.
- **Seams** — a prefix on `BuildingsCampaignBehavior.TickCurrentBuildingForTown` takes the tick off
  vanilla (ours runs from `RBMSettlementWealthCampaignBehavior.OnDailyTickSettlement`), and postfixes on
  `DefaultBuildingConstructionModel.CalculateDailyConstructionPower`/`WithoutBoost` report the funded
  figure to the UI and the days-to-complete estimate.
- **Labour market** — `Construction.LabourMarket` resolves once per tick (cached a day) where the work is
  transacted: the fief itself if it has a citizen purse, else — for a castle, which has none — the nearest
  town it is not at war with, as `MilitiaUpkeep` arms a castle's watch. Wages, material and tool money
  land in that town's citizen purse and the tariff is levied there; goods still come off the castle's own
  stores first and off the town's shelves only when it has none. A castle that can reach no such town
  buys no materials and its wage coin leaves the ledger.
- Towns and castles alike; skipped under siege. Logged as `BUILD` in `EconomyLog`; tool debt persists as
  `RBM_constructionToolDebt`.

#### Building effects (`Settlements/BuildingEffects.cs`)

`BuildingEffects.Tier(town, townType, castleType)` reads `Building.CurrentLevel` for whichever of a
matched town/castle `DefaultBuildingTypes` pair the fief actually owns (Fortifications, Barracks,
Training Fields, Guard House, Mason, Roads map 1:1; Warehouse pairs with the castle Granary; Marketplace,
Tax Office and Waterworks are towns only). Everything below is gated on `rbmCampaignEnabled`, and every
vanilla effect stays in place unless the row says "replaces".

| Building | RBM effect | Seam |
|---|---|---|
| Fortifications | siege defence advantage x1.1/1.2/1.3 (**replaces** the old downward step from L3) | `SimulationSiege.MeasureWall` = `1 + 0.1 x level` |
| | garrison + militia maintenance −0/5/10% | `GarrisonUpkeep.MaintenanceBill`, `MilitiaUpkeep.DailyMaintenanceBill` |
| Barracks | arming a garrison or militia recruit −5/10/15% | `GarrisonRecruitCost.SpawnCost`, `MilitiaUpkeep.SpawnCostPerMan` + `ArmOneMilitiaman` |
| | intake ceiling +1/2/3 a day | `GarrisonRecruitCost.Compute` (added to `GarrisonSpawnDailyMax`, own tooltip line), `MilitiaUpkeep.ComputeMilitiaChange` |
| Training Fields | garrison promotions −5/10/15% | `SpoilsUpgradePatches.DiscountGarrisonUpgrade` (both the affordability test and the billed sum) |
| | +10/20/30 XP a day, garrison AND militia party (10x vanilla's `ExperiencePerDay`) | `GarrisonDrill` postfix on `GetEffectiveDailyExperience`, filter widened to `IsMilitia` |
| Guard House | tariff +0.3/0.6/1.0 percentage points on GUARDED trade only (caravans, lords, the player) | `TradeTariff.Levy(.., guardedTrade: true)` from `SettlementWealth.RouteNativeWrite` and `InventoryLogic.DoneLogic` |
| | passive convict labour | the Guard House terms in the construction ceiling/free-labour above |
| Tax Office | wealth tax and minting cuts x1.05/1.1/1.15, owner and fief legs alike | `WealthTax.OnDailyTick`, `Minting` |
| Marketplace | tariff x1.1/1.2/1.3 on ALL channels | `TradeTariff.Levy` rate factor |
| Warehouse / Granary | granary = 10/20/30/40 days of the fief's own consumption (**replaces** the flat `TownFoodStockScale` x10), castles included | `RBMTownFoodSupply.FoodStocksUpperLimitPatch`, sized off `GetFoodConsumption(town).Total` with a 300 floor |
| Mason | construction efficiency +5/10/15%, labour ceiling +10/20/30% (**replaces** `ConstructionPerDay`) | `Construction.MasonTier` |
| Waterworks | every other point of infrastructure worth +10/20/30% | `RBMProsperityEquilibrium.InfrastructureMultiplier` = `1 + score x 0.02 x (1 + 0.1 x tier)`, clamp unchanged |
| Roads and Paths | bound-village production +5/10/15% | `RBMVillageProduction.RoadsFactor`, applied to the tick and to `CalculateDailyProductionAmount` alike |

Four more rows are CASTLE-ONLY, three of them building types a town has no equivalent of (accessors
`CastellanTier` / `CraftsmanTier` / `FarmlandsTier`, `null` town type):

| Building | RBM effect | Seam |
|---|---|---|
| Castellan's Office | 10/20/30% of garrison recruits enlist as `Culture.EliteBasicTroop` | `GarrisonRecruitCost.PickRecruit`, rolled per man and priced through `SpawnCostFor`; `Compute`/`SpawnCost` keep the common soldier so the wealth rate stays deterministic |
| | mounted garrison maintenance −10/20/30% | `GarrisonUpkeep.MaintenanceBill`, `character.IsMounted` elements only |
| Craftsman Quarters | castle income x1.1/1.2/1.3 | `CastleEconomy.OnDailyTick` |
| Farmlands | castle food production +10/20/30% (**replaces** the flat 6/12/18) | `RBMTownFoodSupply.TownFoodStocksChangePatch.Postfix`, castles only |
| Guard House (castle) | **removes** vanilla's `Militia` +1/2/3 — the Barracks owns intake | `MilitiaUpkeep.AddMilitiaEffectOfBuildings` |

#### Prison labour (`Settlements/PrisonLabour.cs`)

Every man in a fief's `PrisonRoster` eats 0.05 food a day and earns it 30 denars a day, towns and castles
alike. Income runs from the daily settlement pass as a third income step (`Source.PrisonLabour`, `EconomyLog`
tag `PRISON`); the food is charged in `RBMTownFoodSupply.FeedPopulation` for a town (provisioned from stock,
nobody billed) and as an explained line on the castle food postfix. `FoodConsumptionBreakdown.Prisoners`
carries it into the granary cap and the ledger tooltip. The construction side of the same prisoners
(`Construction`: +60 ceiling, 30 free points each) is separate and unchanged.

`UI/BuildingEffectTooltips.cs` postfixes `BuildingType.GetExplanationAtLevel` to append a plain "RBM:"
line per building type, so the town management project list names these effects beside vanilla's.

The town management Projects grid is reshaped by two cooperating `WidgetPrefab.LoadFrom` injections, both
installed from `OnSubModuleLoad` under `rbmCampaignEnabled`. `UI/ProjectsGridPrefabPatch.cs` owns
`TownManagement.xml`: it shrinks the grid (`DefaultCellWidth` 160 -> 135, `DefaultCellHeight` 140 -> 115, the
`DevelopmentItem` template 110 -> 90) and widens it to `ColumnCount` 6 -> 7, so War Sails' 13th building (the
shipyard, which vanilla stranded on a hidden third row) fits the second row; 7 x 135 = 945 still clears the
950px `ScrollingRect`. The clipped viewport then *shrinks* 290 -> 250 (10px grid margin + 2 x 115 + slack),
which is what keeps the Daily Defaults row below it inside the Manage dialog. The
`NavigationScopeTargeter ScopeID="AvailableProjectsScope"` `AlternateMovementStepSize` tracks the column
count (6 -> 7); the sibling `DailyDefaultsScope` / `DailyDefaults` grid / `DailyDefaultItem.xml` are a
separate, untouched set.
`UI/TownManagementGridPatch.cs` owns `DevelopmentItem.xml` and scales that prefab's hard-coded, size-coupled
values by the same 90/110 factor (caption `MarginTop`, progress strip, hammer cluster, level plate, overlay
buttons). They are split by file because each redirects to `%TEMP%\RBM\Prefabs\<name>.xml` and would collide
otherwise. `DevelopmentItem.xml` has exactly one call site (this grid), so scaling the file is safe.
| `Production/` | Village production, villager convoys and deliveries, town food supply and storage, citizen and workshop demand. |
| `Workshops/` | RBM's ownership of the workshop rules. `RBMWorkshopModel` — a `WorkshopModel` decorator registered in `OnGameStart`; it owns `InitialCapital` (60,000), `CapitalLowLimit` (half of it) and `DailyExpense` (250, the standing overhead only), and applies `ArtisanOutput.Scale` inside `GetEffectiveConversionSpeedOfProduction`. Everything else delegates to `BaseModel`, so NavalDLC's own workshop-model decoration survives whichever order the two are registered in. `RBMWorkshopCycle` — the produce-or-not decision, as skip-prefixes on both `Can*WorkshopProduceThisCycle` gates: storage glut (folded in from the deleted `WorkshopHeadroomGate`), then a proportional margin (`inputCost x 1.15 + wage`, replacing vanilla's speed-inverted floor), shop solvency and town cash, all judged on the payout `RBMWorkshopSettlement` will actually pay. Also the single `SettlesInGold` predicate (the artisans settle in kind). `RBMWorkshopSettlement` — the money and goods legs, as skip-prefixes on `ProduceAnOutputToTown` and `ConsumeInputFromTownMarket`: one sell-side valuation ceilinged at 10% of town gold (min 500) serves gate and payment alike, and inputs are priced on the whole draw rather than vanilla's one unit. `RBMWorkshopExpense` — the daily bill, as one skip-prefix on `HandleDailyExpense` replacing vanilla's three methods: overhead plus a per-batch payroll (75/batch), paid down vanilla's ladder (capital while above `CapitalLowLimit`, else a player owner's gold, else capital, else what capital there is, else vanilla's own `ChangeWorkshopOwnerByBankruptcy`), with every denar credited to citizen wealth as `Source.WorkshopWages`. It also counts the day's batches off the two `TickOneProductionCycleFor*Workshop` methods. `WorkshopCardPayrollLine` — the clan-card "Production Wages" row, reading its last payroll. See `WORKSHOP_RULES_PLAN.md` for the phases that follow. |
| `Economy/` | Market prices and liquidity, caravan capital and trade volume, recruit supply, trade-good values, prosperity equilibrium. |
| `Simulation/` | The equipment-aware auto-resolve: weapon model, hit points, arm targeting, perks, morale, rout, player participation, and the two-phase wall assault (`SimulationSiege.cs`). |
| `Power/` | `StrategicTroopPower` and its tooltip — the campaign-side power figure. |
| `Spectate/` | Watching an AI-vs-AI battle as a no-agent spectator. |
| `UI/` | The party-screen spoils bar, the inventory weight column, and their prefab injections. |
| `Diagnostics/` | `SpoilsLog`, `EconomyLog`, `SimulationLog`, `LogRetention`. |
| `RBMCampaignPatcher.cs` | Entry point (`DoPatching`), at the project root. |

## Lifecycle wiring (in `RBM/SubModule.cs`)

- `ApplyHarmonyPatches()` → `RBMCampaignPatcher.DoPatching(ref rbmcampaignHarmony)` (PatchAll +
  widget registration), or `UnpatchAll` when disabled.
- `OnSubModuleLoad()` → `SpoilsBarPrefabPatch.ApplyEarly(...)` — **must** run here, not in
  `ApplyHarmonyPatches`, because Gauntlet parses and caches the party-screen prefab before
  `OnGameStart`. `ApplyEarly` also calls `SpoilsLog.Reset()`.
- `OnGameStart()` (Campaign only) → adds six behaviors: `RBMSpoilsCampaignBehavior`,
  `RBMTroopUpkeepCampaignBehavior`, `RBMSimulationCampaignBehavior`, `RBMSpectateCampaignBehavior`,
  `RBMEconomyCampaignBehavior`, `RBMSettlementWealthCampaignBehavior`.

### Campaign event listeners

`RBMSpoilsCampaignBehavior` (`SpoilsPool`):
- `OnSessionLaunchedEvent` → session setup
- `MapEventEnded` → loot distribution
- `RaidCompletedEvent` → village-raid plunder
- `OnSettlementOwnerChangedEvent` → town/castle sack plunder (siege captures only)
- `DailyTickPartyEvent` → wage deposits
- `MobilePartyDestroyed` → prune purses
- `PlayerUpgradedTroopsEvent` → charge staged spoils
- `OnTroopRecruitedEvent` / `OnUnitRecruitedEvent` → seed a recruit's upkeep. The two are
  **disjoint by source**, not duplicates: the player's recruit screen fires the second (with no
  settlement argument) and the AI path fires the first.

`RBMTroopUpkeepCampaignBehavior` (`TroopUpkeep`):
- `SettlementEntered` → buy food
- `HourlyTickPartyEvent` → buy food + carouse
- `MobilePartyDestroyed` → prune food state
