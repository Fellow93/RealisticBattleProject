# RBMCampaign — Architecture / Developer Notes

Technical companion to `README.md` (which is the player-facing description). This document
is for a developer working on the module: what it hooks, how the pieces fit, and where state
lives.

## Big picture

RBMCampaign is a **6th C# project** (not in the "5 projects" list in `CLAUDE.md`). It has its
own Harmony instance `com.rbmcampaign` and is gated by the config toggle `rbmCampaignEnabled`
(default on).

**Spoils = a per-troop-stack purse, denominated in gold.**

Every stack of identical troops in every party (yours and the AI's) has its own hidden purse.
A "point" of spoils is literally one gold piece. The purse is keyed by
`party.Id + "#" + character.StringId` (`SpoilsPool.Key`) — so "Aserai Veteran Infantry in
party X" is one purse, separate from the same troop in party Y. It lives in a side dictionary
(`SpoilsPool._spoils`) because Bannerlord's `TroopRosterElement` struct has no spare field to
hang it on.

The purse **fills** from battlefield loot and wages, and **drains** on upgrades, food, and
carousing.

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
- Spend is credited to the settlement (Prosperity for towns, Hearth for villages) via
  `settlementProsperityPerGoldSpent`.
- Patches `DefaultMobilePartyFoodConsumptionModel.CalculateDailyBaseFoodConsumptionf`: men
  carrying their own rations **stop eating party food stores**. Heroes always eat from stores.

### 3. Carousing (`TroopUpkeep.SpendOnFun`)

Each hour idling in a settlement, each stack spends `troopSettlementFunWageFraction` of its
daily wage on drink/dice (>1 day's wage at default). Also credits settlement prosperity. Purse
never goes negative.

**Garrisons and militia are excluded** from food/carousing (they never leave; would be an
infinite prosperity faucet). Only visiting field parties spend in settlements. (Militia are not
free, though — they drain their settlement daily; see *Settlement prosperity flows* below.)

### 4. The spoils cap (`SpoilsPool.GetSpoilsCap`, `Spoils/SpoilsPool.Cap.cs`)

Not a sink of its own — a ceiling the sinks above read. Each stack's cap
`GetSpoilsCap` = `(dearest upgrade cost + war chest) × stackSize`, where the per-man war chest is
`troopSpoilsWarChestGoldPerTier × character.Tier` (a veteran keeps a deeper purse than a recruit). A
top-tier troop has no upgrade target, so its upgrade headroom is replaced by its own equipment value
(`GetEquipmentValue × troopUpgradeCostMultiplier`) — an elite holds a purse worthy of its kit rather
than collapsing to the war chest alone.

The cap governs *behaviour*, not storage: a purse may sit over its cap (loot and wage both fill past
it), but once it does, upkeep starts drawing the surplus down — carousing bites harder (§3) and only
over-cap stacks splurge on luxuries. Nothing over the cap is handed back to your gold: spoils are a
**closed loop**, spent only on upgrades, food and drink. `GetPartyPayee` (owner if alive, else
`LeaderHero`) also lives here — the party-leader spoils cut pays through it.

## Settlement prosperity flows

Separate from the troop purse: four hooks move a settlement's **Prosperity** (towns/castles) or
**Hearth** (villages) at the shared `settlementProsperityPerGoldSpent` rate (1 gold of worth →
`rate` points). All gate on `rate > 0`, so `0` disables the whole layer; drains clamp so neither
stat goes negative. `TroopUpkeep.CreditSettlement` (now `internal`) is the shared add helper; the
food/drink/luxury spending above already feeds it.

- **Market trade credit** (`Upkeep/MarketTradeProsperity.cs`) — postfix on `SellItemsAction.Apply`.
  When any party *buys from* a settlement (the settlement is the `receiverParty`, giving up goods
  for coin) it gains `number × GetItemPrice(pre-trade) × rate`. Covers player, caravans, lords;
  selling *to* a settlement is left alone. Log `TRADE`, throttled once/buyer/settlement/day.
- **Militia upkeep** (`Upkeep/MilitiaUpkeep.cs`) — `DailyTickSettlementEvent`. Drains the militia's
  daily wage × rate. Wage read off the real `MilitiaPartyComponent.MobileParty` roster
  when one exists (elites included, via `TroopWage`), else the culture's rank-and-file militia
  average × `settlement.Militia`. Log `MILITIA` (the daily tick is throttle enough).
- **Production drain** (`Upkeep/ProductionUpkeep.cs`) — `OnItemProducedEvent`. Every produced item
  (workshop wares + village goods/food; *not* initial game stocking) drains `item.Value × count ×
  rate`. Fires often — each food unit raises it — so `MAKE` is throttled once/settlement/day.
- **Villager produce credit** (`Upkeep/VillagerTradeHearth.cs`) — postfix on
  `SellGoodsForTradeAction.ApplyByVillagerTrade` (villagers sell through this, **not**
  `SellItemsAction`). Credits the **home village's** Hearth by the sale proceeds × rate, measured as
  the rise in `villagerParty.PartyTradeGold` across the call. Log `HAUL`. Closes the loop the
  production drain opens on the village side.

Wiring: the two postfixes via `PatchAll`; the two events in
`RBMTroopUpkeepCampaignBehavior.RegisterEvents`. The rate is reused deliberately — one knob, both
directions (production and militia spend a settlement down, trade and produce-sales build it back up,
netting where goods actually move).

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

The save key was deliberately renamed when a spoils point's meaning changed (was "equipment
value," now "1 gold"), so **old saves drop stale pools** rather than misreading them. Purses are
pruned when a stack disappears (upgraded away / killed) or a party is destroyed — spoils die
with the stack, like its XP.

## Config knobs

All under `/Config/RBMCampaign` in the config XML, wired into the in-game settings UI.

| Setting | Default | Effect |
|---|---|---|
| `TroopUpgradeCostMultiplier` | 1 | Scales upgrade gold *and* spoils cost. **0 disables the whole system** (`SpoilsPool.IsEnabled`). |
| `TroopUpgradeSpoilsLootMultiplier` | 1 | How much battlefield loot yields. |
| `TroopLootPiecesPerMan` | 3 | Pieces of kit one man can carry off a field. |
| `TroopLootOverlookChancePerTier` | 0.5 | Chance a troop overlooks kit one tier below him (compounds per tier). |
| `TroopWageTierBase` | 50 | Daily wage = base × tier for non-heroes, replacing vanilla's wage table. 0 keeps vanilla. |
| `TroopRaidSpoilsMultiplier` | 0.25 | Plunder soldiers pocket sacking a settlement — of a village's `Hearth × RaidDamage`, or a stormed town's `Prosperity`. 0 disables plunder spoils. |
| `TroopSpoilsWarChestGoldPerTier` | 25 | Per-man war chest in `GetSpoilsCap`, multiplied by `character.Tier` — the flush threshold above which upkeep spends surplus on drink/luxuries. Slider 0–1000. |
| `TroopSettlementFoodDays` | 20 | Days of food a stack buys per trip. |
| `TroopFoodWageFraction` | 0.5 | Food price ceiling a man will pay, relative to his wage. |
| `TroopSettlementFunWageFraction` | 1.5 | Carousing spend per day idled, as a multiple of daily wage. |
| `SettlementProsperityPerGoldSpent` | 0.02 | Prosperity/Hearth moved per gold of worth, both ways — trade & carousing add, militia wages & production drain, villager produce returns to its home village. Shared by every *Settlement prosperity flow*; **0 disables that whole layer**. |
| `RBMCampaignEnabled` | 1 | Master on/off for the whole module. |
| `SpoilsLoggingEnabled` | 1 | Toggles the diagnostic log file. |

## Diagnostics

`SpoilsLog` writes a detailed trace (loot distribution, wage deposits, upgrade pricing, food
buying, carousing, save/load counts) to `rbm_spoils_<yyyy-MM-dd_HH-mm-ss>.log` in the config
folder — one timestamped file per launch so runs don't overwrite each other. When
`developerMode` is on, lines also print to the in-game message log.

## File map

| File | Role |
|---|---|
| `SpoilsPool.cs` | The heart: purse storage, equipment valuation, loot distribution, wage deposits, upgrade cost math. Contains `RBMSpoilsCampaignBehavior`. |
| `SpoilsUpgradePatches.cs` | AI upgrade reimplementation + player party-screen staging (`PartyScreenStagedUpgrades`). |
| `RBMCampaign.cs` | Upgrade gold-cost model patches (`GetGoldCostForUpgrade`) and the tooltip breakdown (`GetUpgradeHint`). |
| `TroopUpkeep.cs` | Settlement food buying, carousing, food-consumption patch. Contains `RBMTroopUpkeepCampaignBehavior`. |
| `SpoilsBarWidget.cs` / `SpoilsBarPrefabPatch.cs` | The party-screen purse bar and its prefab injection. |
| `SpoilsLog.cs` | Diagnostic logging. |
| `RBMCampaignPatcher.cs` | Entry point (`DoPatching`). |

## Lifecycle wiring (in `RBM/SubModule.cs`)

- `ApplyHarmonyPatches()` → `RBMCampaignPatcher.DoPatching(ref rbmcampaignHarmony)` (PatchAll +
  widget registration), or `UnpatchAll` when disabled.
- `OnSubModuleLoad()` → `SpoilsBarPrefabPatch.ApplyEarly(...)` — **must** run here, not in
  `ApplyHarmonyPatches`, because Gauntlet parses and caches the party-screen prefab before
  `OnGameStart`. `ApplyEarly` also calls `SpoilsLog.Reset()`.
- `OnGameStart()` (Campaign only) → adds `RBMSpoilsCampaignBehavior` +
  `RBMTroopUpkeepCampaignBehavior`.

### Campaign event listeners

`RBMSpoilsCampaignBehavior` (`SpoilsPool`):
- `MapEventEnded` → loot distribution
- `RaidCompletedEvent` → village-raid plunder
- `OnSettlementOwnerChangedEvent` → town/castle sack plunder (siege captures only)
- `DailyTickPartyEvent` → wage deposits
- `MobilePartyDestroyed` → prune purses
- `PlayerUpgradedTroopsEvent` → charge staged spoils

`RBMTroopUpkeepCampaignBehavior` (`TroopUpkeep`):
- `SettlementEntered` → buy food
- `HourlyTickPartyEvent` → buy food + carouse
- `MobilePartyDestroyed` → prune food state
