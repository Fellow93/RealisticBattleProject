# RBMCampaign — Architecture / Developer Notes

Technical companion to `README.md` (which is the player-facing description). This document
is for a developer working on the module: what it hooks, how the pieces fit, and where state
lives.

## Big picture

RBMCampaign is the 6th C# project. It has its own Harmony instance `com.rbmcampaign` and is
gated by the config toggle `rbmCampaignEnabled` (default on).

It has grown well past the spoils system this document was first written for. The module now
also carries the **settlement wealth ledger** (`Settlements/`), the **village-to-town goods and
food chain** (`Production/`), **workshop rules** (`Workshops/`), the **market and caravan economy**
(`Economy/`, `Caravans/`), the **settlement manpower pool** (`Recruitment/`), **AI lord behaviors**
(`AI/`), the **equipment-aware auto-resolve** (`Simulation/`, `Power/`), the **spectator battle**
(`Spectate/`) and the **RBM Ledger** screen (`UI/Ledger/`). Those have their own documents — see
the file map at the foot of this one. Everything below is about the spoils purse specifically.

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

The purse **fills** from battlefield loot, the beaten enemy's captured purses, raid and siege
plunder, stripped prisoners (ransomed or left behind), the daily wage and a recruit's seed, and
**drains** on upgrades, field maintenance, food, carousing, paid healing, the odd luxury, the
leader's cut, and the share each fallen man takes to the grave.

Villager parties are exempt (`IsExemptParty`, gated at `AddSpoils`). Companion heroes claim shares
like a stack but hold no purse: their share is paid straight to the party payee's gold
(`IsCompanionStack`, `EventGoldKind.CompanionSpoils`), with no leader's cut.

## How spoils are EARNED

### 1. Battlefield loot (`SpoilsPool.OnMapEventEnded`)

- Only the **dead** are looted — not the wounded (carried off wearing their kit) and not the
  routed (fled with theirs). The winners hold the field, so they recover **both sides'**
  fallen, including their own dead.
- Each dead man is stripped of one of his battle-equipment sets, chosen at random. Every
  armor/weapon slot, horse and harness included, yields a **random 25–75% of the item's value**
  (mean 50%; consumables like arrows/javelins reflect what's left unspent).
- Loot is **bucketed by item tier**, then handed out to the victorious parties in proportion to
  each party's `ContributionToBattle` (even split if all contributions are zero, e.g. simulated
  battles).
- Within a party, loot follows a **pecking order**: veterans (higher troop tier) pick first, and
  within a tier the culture's noble line ahead of the levy.
  The further *beneath* a troop's tier a piece is, the likelier he is to **overlook** it
  (`troopLootOverlookChancePerTier`, compounding per tier of gap). What veterans overlook or
  can't carry cascades to greener troops.
- A stack carries at most `troopLootPiecesPerMan × men in stack` pieces.
- **Casualties first** (`SpoilsPool.Casualties.cs`): the winners' dead take their per-man share
  of their stack's purse with them; a wholly wiped stack's purse is half recovered and split among
  the surviving stacks by tier weight. The beaten side's killed and wounded drop their share of
  their purses, of which `troopFallenSpoilsCaptureFraction` (0.75) goes to the winners by
  contribution, then tier weight.
- **Player feedback**: post-battle message ("Your men strip the fallen and recover N in
  spoils" / "…find nothing they can use"), plus companion-gold and leader's-cut lines.

### 2. Village raids (`SpoilsPool.OnRaidCompleted`)

Hooks `CampaignEvents.RaidCompletedEvent(BattleSideEnum winnerSide, RaidEventComponent raidEvent)`,
which fires once when a raid concludes. Only on `winnerSide == Attacker`. The village's purse
(`SettlementWealth`, a village's vanilla `Gold` field) is debited `× RaidDamage`
(`RaidEventComponent.RaidDamage` is the 0–1 share of the village actually stripped), and the
hard-coded `RaidSpoilsShare = 0.5` of what was drained becomes the pot; the rest is destroyed. The
pot is split among `raidEvent.AttackerSide.Parties` by `ContributionToBattle` (even split if all
zero — mirrors `OnMapEventEnded`), then by tier weight within each party via
`GrantSpoilsWeightedByTier` (plunder is a lump, not fought over piece by piece like battlefield
kit), then the leader's cut. The player gets a "Your men plunder {SETTLEMENT}…" message.

The goods side of a raid is cut at the model: `RaidGoodsDestruction` postfixes
`DefaultRaidModel.GetRaidLootMultiplier` so a raid keeps `0.5 + 0.001 × Roguery (+0.2 Nord)`,
clamped to 1, of vanilla's haul.

**Forcing supplies** (`Spoils/VillageCoercion.cs`) is wired the same way. Both the no-resist and the
fought (force-supplies `MapEvent`, not a raid) roads end in
`VillageHostileActionCampaignBehavior.village_force_supplies_ended_successfully_on_consequence`, which
pays the player from a null giver. While it runs, that hand-off is debited from the village purse
(`Source.Raid`, clamped) and the whole draw goes through `SpoilsPool.OnVillageCoerced` (tier-weight
split + leader's cut, no destroyed share); with the spoils economy off the clamped amount still reaches
the player. The goods go through `InventoryScreenHelper.OpenScreenAsLoot`, scaled by
`RaidGoodsDestruction.TakenFraction`.

**Battle sites** (`Spoils/BattleSiteSpoils.cs`): the coin a site search finds is taken off
`BattleWreckageCampaignBehavior._lootedGoldAmount` just before vanilla's gold branch pays it and granted
through `SpoilsPool.OnBattleSiteGold` (same split). The site's goods target
(`GetTradeGoodTargetValueAndRogueryXp`) is scaled by the same taken fraction, since sites only form on
battles the player was not in and RBM's loot pass already stripped those dead. Recovered troops DO get
the recruit maintenance seed, unlike prisoners recruited in a town or village: `BattleSiteTroopUpkeep`
prefixes `PartyScreenHelper.OpenScreenAsReceiveTroops` while `BattleSiteSpoils.InResults` is set,
clones the offered roster and wraps the screen's closing delegate, so on Done each stack is seeded
(`SpoilsPool.SeedRecruitMaintenance`) for the men actually taken (offered minus what was left on the
left side). Cancel seeds nothing.

### 3. Siege drain and town/castle sacking (`SpoilsPool.OnBesiegedFortificationDailyTick`, `SpoilsPool.OnSiegeAftermathApplied`)

While a siege holds, a daily settlement tick drains 5% (`SiegeDailyDrainRate`) of a besieged
castle's treasury or a besieged town's citizen wealth, of which `SiegeDrainSpoilsShare = 0.5` goes to
the besieging parties (by headcount, then tier weight) and the rest is destroyed. The same tick
snapshots the besiegers so the sack can pay them after the camp is torn down. Silent to the player.

The sack itself is driven by the **vanilla aftermath choice** — the player's Devastate / Pillage / Show Mercy menu, or
`DetermineAISiegeAftermath`'s weighted pick for an AI — off
`CampaignEvents.OnSiegeAftermathAppliedEvent`. Towns and castles, player and AI alike. Per tier:

| Tier | Prosperity | Wealth stolen | Spoils share | Market goods | Spoils share |
|---|---|---|---|---|---|
| Show Mercy | −10% | 10% | 0.5 | — | — |
| Pillage | −20% | 50% | 0.5 | 40% | 0.5 |
| Devastate | −50% | 90% | 0.1 | 90% | 0.1 |

Wealth is drawn from a town's **citizen wealth** or a castle's **treasury** (a town's treasury passes
intact to the new owner). Market goods (towns only) come off `town.Owner.ItemRoster` at the market's
own asking price, food at half the fraction. Everything paid out was first taken from the settlement;
the non-spoils remainder is destroyed, never banked. The pot is split across **every besieging party**
by vanilla's contribution map (headcount fallback), then by tier weight within each party, each with
its leader's cut. A won sally-out arrives on the same event and sacks nothing (the previous owner's
faction check). The sack and the siege drain share the `SpoilsPool.SackActive` gate.

The prosperity fraction and the zeroing of vanilla's minted army gold live in
`SiegeAftermathPatches`. **Ordering note:** `MapEvent.FinalizeEventAux` dispatches `OnMapEventEnded`
(→ aftermath, for AI and player-army-member captures) *before* `OnBeforeMapEventFinalize` (→
`SiegeCompleted` → owner change); a player-led siege reverses that, since its aftermath waits on his
menu. `OnSettlementCaptured` therefore only parks/consumes a besieger snapshot, with an hourly sweep
as the backstop for any capture path that raises no aftermath at all (sacked at Pillage).

### 4. Prisoners (`SpoilsPool.Ransom.cs`, `SpoilsPool.PrisonerStrip.cs`)

- **Ransom**: every prisoner sold — heroes included — yields his full mounted kit worth
  (`GetEquipmentValueWithMount`) as spoils to the selling party, by tier weight, then the leader's cut.
  Hooks: `SellPrisonersAction.ApplyInternal` (real sales only), `EndCaptivityAction.ApplyByRansom` (a
  lord ransomed by courier offer or barter, outside a sale), and the manual-labourers delivery quest's
  `OnDoneClicked`. The gold for the man himself is vanilla's, but `Settlements/RansomFunding.cs`
  debits it from the buying town's citizen wealth instead of minting it (left alone where there is no
  citizen purse). `RansomMenuTooltip` / `RansomScreenSpoilsLabel` show the spoils half. A courier
  ransom offer the player accepts is capped at what the AI payer holds above vanilla's 1,000 reserve
  (`RansomFunding.CapRansomOfferPatch`), so vanilla's top-up never mints the payer's gold.
- **Executed**: an executed captive lord (`Executed` / `ExecutionAfterMapEvent`, incl. v1.5 blood-feud
  dungeon executions) is stripped off `CampaignEvents.BeforeHeroKilledEvent`, while
  `PartyBelongedToAsPrisoner` still names the captor (`SpoilsPool.OnBeforeHeroKilled`). A mobile captor
  splits the kit as ransom spoils; a settlement captor credits it to its treasury (`Source.Execution`).
- **Left behind**: prisoners the player declines on the post-battle loot screen are stripped for half
  their kit worth (`LeftoverPrisonerStripFraction`), via a prefix on
  `PlayerEncounter.OnPlayerLootMembersAndPrisonerEnd`.

### 5. Wages and recruit seed (`SpoilsPool.OnDailyTickParty`, `SpoilsPool.Maintenance.cs`)

Each day, every non-hero stack's full wage is deposited into its purse — twice it for a mercenary
company under contract (the second wage is the one `MercenaryContractPay` charges and the crown
reimburses). **The party's actual gold is untouched** — this only reinterprets where some of the
wage notionally went (kit maintenance). Applies to every party in the world but bandit parties.
Garrisons bank their wage like any troop (the fief pays it, `GarrisonUpkeep`). Militia stacks go to
`MilitiaUpkeep.PayMilitiaUpkeep` instead: the settlement's funding pot pays a reduced wage into the
purse — town 0.25, castle 0.10, village 0 of a soldier's wage (`MilitiaWageFactor*`) — and their kit
maintenance is met from that purse first.

A stack recruited from a village or town is seeded `recruitMaintenanceDays` (20) days of maintenance
(`OnTroopRecruited` for the AI, `OnUnitRecruited` for the player; the two events are disjoint).

## How spoils are SPENT

### 1. Troop upgrades (main sink)

- The equipment-value delta between a troop and its upgrade target *is* the gold cost (horse and
  harness included while `troopUpgradeChargeMountValue` is on — `MountValueUpgrade` then also drops
  vanilla's horse-item requirement). Spoils pay it, consumed **one man at a time** — if the purse
  covers 2.5 men, the first 2 upgrade free, the 3rd pays half, the rest pay full.
- The delta is scaled by `troopUpgradeCostMultiplier`, then run through the vanilla perks/feats
  (Steward SoundReserves, Bow RenownedArcher, Khuzait feat, Steward Contractors). A man's spoils
  price is that same full gold price, so the perks discount both pockets. A cheaper-kit upgrade
  costs 0 gold and credits the surplus to the upgraded stack's purse.
- Patches `DefaultPartyTroopUpgradeModel.GetGoldCostForUpgrade` to quote the *next* man's price.
- **Supply town** (`UpgradeSupply`): only the **gold** leg reaches a town — the supply town's citizen
  wealth via `TroopMarketFeedback.RegisterPurchase`, market fee included, falling back to the nearest
  friendly town, then any town. The spoils leg credits no one. With `troopUpgradeRequireSupplyTown` on,
  stock matching the improved slots leaves that town's market for the gold-bought men only, and a party
  needs a friendly town within `troopUpgradeSupplyRadius` (or to be resting in any friendly settlement)
  to upgrade: the player's arrows are refused and greyed, the AI promotes only purse-covered men.
- **AI extras**: `PartyUpgradeBudget` — an optional per-party daily cap on upgrade *gold*, set from
  the clan Parties panel (`RBM_partyUpgradeCapGold` / `RBM_partyUpgradeCapEnabled`); garrison
  promotions are billed to the fief treasury (then `GarrisonSubsidy`), less the Training Fields
  discount; `UpgradeFormationWeights` turns vanilla's fixed AI branch pick at an upgrade fork into a
  weighted draw by culture, garrison role and the lord's traits.

### 2. Food in settlements (`TroopUpkeep`)

- On settlement enter and each hour it stays, each unprovisioned stack of a visiting party buys
  `troopSettlementFoodDays` days of food off the market — **real items, real stock, real prices**.
  Garrisons and militia buy none (`IsVisitor`): their settlement feeds them.
- Buys the best fare it can afford first; per-item ceiling scales with wage
  (`troopFoodWageFraction`). Recruits buy grain, veterans buy meat/cheese. Falls back to
  anything rather than starve; limited by market stock and purse.
- Patches `DefaultMobilePartyFoodConsumptionModel.CalculateDailyBaseFoodConsumptionf`: men
  carrying their own rations **stop eating party food stores**. Heroes always eat from stores.
- `TroopUpkeep.FoodForecast.cs` postfixes `MobileParty.GetNumDaysForFoodToLast` (AI army/siege
  decisions, map-bar warning) and the party/army food tooltips so "days until food runs out" walks
  forward over each stack's ration expiry instead of dividing by today's near-zero consumption. It
  re-asks `FoodChange` with the fed-check clock moved forward, so it cannot drift from the patch above.

### 3. Carousing (`TroopUpkeep.SpendOnFun`)

Each hour idling in a settlement, each stack spends `troopSettlementFunWageFraction` of its
daily wage on drink/dice — a quarter of a day's wage at the default, plus a surplus term that
bites harder the further over its cap the purse stands (at most 2% of the surplus an hour), all
under a per-man ceiling of `25 × (tier + 1)` a day. Purse never goes negative. The coin is credited to
the settlement's purse (`TroopMarketFeedback.RegisterServiceSpend`); in a town half of it also buys
tavern fare off the market.

**Garrisons and militia carouse too** (`SpendsLocally` admits every party), and buy luxuries and pay
for healing: their purse holds the wage their own settlement paid them, so their spending is that
coin coming back over the counter. Only food is reserved to visitors.

### 4. Field maintenance (`Spoils/SpoilsPool.Maintenance.cs`)

`troopMaintenanceFraction` (0.005) of a stack's mounted kit worth a day, charged once per clan per
day off the clan finance model's apply pass (`MaintenanceFinanceLine`) for every active party in
`clan.WarPartyComponents`. The purse meets `independentMaintenancePurseFraction` (1) of it for a clan
sworn to no kingdom or a mercenary under contract, and none of it for a sworn vassal or ruler; the
shortfall is folded into the clan's daily gold change. That gold leg is credited to the nearest town
not at war with the party (the one it stands in, if any; never a castle or village) through
`TroopMarketFeedback.RegisterPurchase`, market fee included. The spoils leg is simply drained.
**Nothing comes off the town's shelves** — maintenance is labour, not stock. Garrisons are billed
their kit maintenance by `GarrisonUpkeep.ChargeMaintenance` (paid by the fief, not the purse) and
militia inside `MilitiaUpkeep`.

### 5. Healing and luxuries (`TroopUpkeep.Healing.cs`, `TroopUpkeep.Luxury.cs`)

Hourly in a settlement: a stack with wounded pays `troopSpoilsHealGoldPerTier × tier` a man to mend
up to `troopSpoilsHealFractionPerHour` of them (not while starving), paid to the settlement; an
over-cap stack off cooldown rolls `troopLuxurySpendChance` to buy one luxury off the market, a
keepsake that goes to no inventory. The leader earns Steward XP for what his stacks spend on food and
luxuries (`TroopUpkeep.Stewardship.cs`, which also grants daily XP for food stores and spare mounts).

### 6. The spoils cap (`SpoilsPool.GetSpoilsCap`, `Spoils/SpoilsPool.Cap.cs`)

Not a sink of its own — a ceiling the sinks above read. Each stack's cap
`GetSpoilsCap` = `(dailyWage + dailyMaintenance) × troopSpoilsCapDays`, i.e. a configured number of
days' worth of the stack's own keep — its daily wage (`PartyWageModel.GetCharacterWage × stackSize`)
and its daily field maintenance (`DailyMaintenanceCost`, the same per-stack upkeep §4 charges). Priced
the same for every tier: a veteran's dearer wage and kit already make his days' keep the deeper purse,
so there is no separate war chest and a top-tier troop with no upgrade to save for is held to the same
rule. `troopSpoilsCapDays` is 0–60 (default 20); 0 collapses the cap to nothing.

The cap governs *behaviour*, not storage: a purse may sit over its cap (loot and wage both fill past
it), but once it does, upkeep starts drawing the surplus down — carousing bites harder (§3) and only
over-cap stacks splurge on luxuries. Nothing over the cap is handed back to your gold — the surplus
is drunk and eaten where the men stand, which credits that settlement's purse rather than yours.

A purse reaches gold at exactly one point, the leader's cut (`Spoils/SpoilsPool.LeaderCut.cs`), and it
is conserving: the share is drawn back out of the same purses the gather just filled, so no coin is
minted. The fraction is `troopLeaderSpoilsCutFraction × (clan tier + 1)`, ×1.5 for a mercenary under
contract, × `(1 + 0.003 × Roguery)`, clamped to 1. A party with no non-hero stack takes its cut of the
whole pot minted instead (`ApplyLeaderCutSolo`). (Companions' shares never enter a purse; see above.)
`GetPartyPayee` (owner if alive, else `LeaderHero`) lives in `SpoilsPool.Cap.cs` — the cut pays
through it.

## Who it applies to

- **Player** — party screen (upgrades) and settlement visits.
- **AI** — `SpoilsUpgradePatches` reimplements `PartyUpgraderCampaignBehavior.UpgradeReadyTroops`
  so AI lords draw down spoils on upgrade, affordability checked against the discounted price.
  (Vanilla's helpers are private and pass a private struct — can't be patched directly, hence
  the full reimplementation.)
- Fully symmetric: AI parties loot, earn wage-spoils, and spend on upgrades like the player.
- **Garrisons and militia** hold purses too: wage in (paid by their settlement), carousing, luxuries
  and healing out, promotions from the purse first.

## Player-visible UI

1. **Spoils bar** on the party screen — `RBMTroopSpoilsBarWidget` (a `FillBarVerticalWidget`)
   injected into the native party-screen prefab.
2. **Upgrade tooltip breakdown** — patches `CampaignUIHelper.GetUpgradeHint` → full worth,
   "Includes mount: M", "Spoils cover: X", "You pay: Y", "All N: you pay Z", or "Salvaged into
   spoils" for a cheaper-kit upgrade; a no-supply-town note, with the arrows greyed and each batch
   clamped to what the gold covers (`PartyCharacterVM.InitializeUpgrades` postfixes).
3. **Party-screen staging** — `PartyScreenStagedUpgrades` reserves spoils for queued-but-unconfirmed
   upgrades and fixes vanilla's gold math (vanilla multiplies one per-man price by batch size,
   overcharging when spoils make leading men free). Cleared on screen reset/close.
   `SpoilsTransferOnPartyScreen` / `SpoilsTransferOnSpecialScreens` move purses with transferred men.
4. **Maintenance lines** — per-man maintenance under the selected troop's wage on the party screen
   (`MaintenanceLabelPrefabPatch`), in the troop tooltip (`MaintenanceTroopTooltipLine`), and in the
   clan-finance and party-wage breakdowns (`MaintenanceFinanceLine`, `MaintenancePartyWageLine`).
5. **Upgrade-budget control** — slider + "unlimited" checkbox beside the clan Parties panel's wage
   limit (`UpgradeLimitPrefabPatch`, `UpgradeLimitWidgets`).
6. **Map party tooltip** — the party's total purse (`SpoilsPartyTooltip`).
7. **Ransom** — the spoils half of a ransom on the tavern option's tooltip and the ransom screen's
   label (`RansomMenuTooltip`, `RansomScreenSpoilsLabel`).
8. **Messages** — post-battle loot, plunder, sack, ransom, stripped-prisoner, companion-gold and
   leader's-cut lines; nameplate bubbles over a settlement for carousing, food and luxury buys
   (`RBMMapNotifications`).

## Save/load

Serialized via `SyncData`:

- `RBM_troopSpoilsGold` — the purses (`SpoilsPool`).
- `RBM_troopFedUntilHours` — when each stack next needs food (`TroopUpkeep`).
- `RBM_troopLuxuryCooldown` — when each stack may indulge again (`TroopUpkeep`).
- `RBM_townTroopTrade` — what troops have spent in each town (`TroopMarketFeedback`).
- `RBM_partyUpgradeCapGold`, `RBM_partyUpgradeCapEnabled` — per-party daily upgrade-gold caps (`PartyUpgradeBudget`).
- `RBM_clanEventGoldByDay`, `RBM_clanEventGoldElsewhereByDay`, `RBM_clanEventGoldFirstDay` — the 30-day event-gold record and the part of it paid to clan members other than the player (`ClanEventGoldLedger`; read by the RBM Ledger's Clan finances tab).
- `RBM_clanFinanceHist`, `RBM_clanFinanceDay` — the Clan finances record: one CSV series per field, one column per closed day, and the open day's running totals with its day, opening balance and partial flag (`RBMClanFinanceLedger`).
- `RBM_settlementWealth` — the town/castle treasury pot (`SettlementWealth`; citizen wealth rides on vanilla's `Gold`, as does a village's single purse).
- `RBM_settlementRecruitPool` — each settlement's manpower pool (`RecruitPool`); a settlement with no entry starts full.
- `RBM_constructionToolDebt`, `RBM_pendingWealthTaxIncome`, `RBM_campaignSeeded` — construction, wealth tax and the one-time seeding flag.
- `RBM_wealthTaxBookedFiefs`, `RBM_pendingGarrisonMaintSubsidy`, `RBM_garrisonMaintBookedFiefs` — which fiefs have booked today's wealth tax, and the owner's garrison-maintenance subsidy booked but not yet charged on the clan apply pass (`SettlementAccrualPool`).
- `RBM_caravan*` (`RBMCaravanRegister`, `RBMCaravanInvestment`) and `RBM_town*Hist` / `RBM_village*Hist` (the Ledger's histories).

⚠️ A persisted store must be reset in its behavior's **constructor**, not from `OnSessionLaunched`:
`LoadBehaviorData` runs before `RegisterEvents` on load, and an absent key leaves the field
untouched, so a null-guard never catches a cross-campaign leak.

The save key was deliberately renamed when a spoils point's meaning changed (was "equipment
value," now "1 gold"), so **old saves drop stale pools** rather than misreading them. Purses are
pruned when a stack disappears (upgraded away / killed) or a party is destroyed — spoils die
with the stack, like its XP.

## Config knobs

All under `/Config/RBMCampaign` in the config XML, most wired into the in-game settings UI. **Only
the spoils knobs this document discusses are listed here** — the maintenance, healing, luxury,
leader-cut, supply-town, stewardship and simulation settings are tabulated in
[README.md](README.md#tuning-it), and the store itself is `RBMConfig/Config/RBMConfig.Campaign.cs`
plus `RBMConfig.Simulation.cs`, with the log toggles in `RBMConfig.Debug.cs`. The raid and siege
spoils shares are code constants in `Spoils/SpoilsPool.Plunder.cs` / `.MarketSack.cs`, not settings.

| Setting | Default | Effect |
|---|---|---|
| `TroopUpgradeCostMultiplier` | 1 | Scales upgrade gold *and* spoils cost. **0 disables the whole system** (`SpoilsPool.IsEnabled`). |
| `TroopUpgradeSpoilsLootMultiplier` | 1 | How much battlefield loot yields. |
| `TroopLootPiecesPerMan` | 3 | Pieces of kit one man can carry off a field. |
| `TroopLootOverlookChancePerTier` | 0.5 | Chance a troop overlooks kit one tier below him (compounds per tier). |
| `TroopFallenSpoilsCaptureFraction` | 0.75 | Share of a beaten enemy's fallen-and-wounded purse the winners capture. |
| `TroopSpoilsCapDays` | 20 | Days of keep (daily wage + daily field maintenance) a stack holds in `GetSpoilsCap` — the flush threshold above which upkeep spends surplus on drink/luxuries. Slider 0–60, discrete. |
| `TroopSettlementFoodDays` | 20 | Days of food a stack buys per trip. |
| `TroopFoodWageFraction` | 0.5 | Food price ceiling a man will pay, relative to his wage. |
| `TroopSettlementFunWageFraction` | 0.25 | Carousing spend per day idled, as a multiple of daily wage. |
| `Enabled` (`rbmCampaignEnabled`) | 1 | Master on/off for the whole module. |
| `SpoilsLoggingEnabled` | 0 | Toggles the diagnostic log file. |
| `SpoilsVerboseLoggingEnabled` | 0 | Per-stack detail in that log, rather than party summaries only. |

## Diagnostics

`SpoilsLog` writes a detailed trace (loot distribution, wage deposits, upgrade pricing, food
buying, carousing, save/load counts) to `<configFolder>/logs/campaign/rbm_spoils_<yyyy-MM-dd_HH-mm-ss>.log`
— one timestamped file per play session (`SpoilsLog.StartCampaignLog` rolls it on session launch,
with the config dumped at the top) so runs don't overwrite each other, with `LogRetention.PruneOldest`
capping how many are kept (it skips a file a live log still holds open). Lines go to the file only —
not to the engine's debug output (that copy was dropped with the buffering below) and never to the
in-game message log.

The other sinks: `EconomyLog` (`logs/economy/`), `SimulationLog` (`logs/simulation/`), `CaravanLog`
(`logs/caravans/`), `Power/StrategicPowerLog` (`logs/powerCalculation/`) and `GarrisonRefillLog`
(`logs/garrison/`, `GarrisonRefillLoggingEnabled`), each with its own toggle.

Every sink writes through `BufferedLogWriter`: one open 64 KB-buffered handle per file (shared
`ReadWrite | Delete`, so it can be tailed), not a `File.AppendAllText` per line — the per-line open was
the main map stutter with logging on. Buffers are flushed on `HourlyTickEvent` and `OnBeforeSaveEvent`
(registered in `RBMSimulationCampaignBehavior`), closed on `RBM.SubModule.OnGameEnd` /
`OnSubModuleUnloaded` (plus a `ProcessExit` fallback), and a log rolling to a new session file closes its
old writer. A hard crash loses up to one in-game hour of lines.

## File map

Everything is foldered; the namespace stays flat `RBMCampaign`. The csproj lists every file with an
explicit `<Compile Include>` — **update it when adding or moving one**.

### The spoils purse

| File | Role |
|---|---|
| `Spoils/SpoilsPool.cs` | Purse storage, keying, `IsEnabled`. A `partial static class` split across the files below. |
| `Spoils/SpoilsPool.Equipment.cs` | Equipment valuation (with and without the mount) and its caches; the slot diffs the supply-town and recruit draws buy against. |
| `Spoils/SpoilsPool.BattleLoot.cs` | Loot distribution off a field: salvage, the pecking order, companion gold. |
| `Spoils/SpoilsPool.Casualties.cs` | The fallen's purse shares: the winners' own losses, wiped-stack recovery, the enemy purse captured; `GrantSpoilsWeightedByTier`. |
| `Spoils/SpoilsPool.Plunder.cs` | The raid pot, the daily siege drain and besieger snapshot, the wealth leg of the sack, the capture/aftermath handshake, and the multi-party split. |
| `Spoils/RaidGoodsDestruction.cs` | Scales a raid's goods haul by the taken fraction (base 0.5, Roguery and Nord lift it). |
| `Spoils/VillageCoercion.cs` | Forcing supplies: the coin is drawn from the village purse into spoils, the goods scaled by the taken fraction. |
| `Spoils/BattleSiteSpoils.cs` | Battle-site coin into spoils instead of player gold; recovered troops seeded with a few days' maintenance for the men taken; the site's goods target scaled by the taken fraction. |
| `Spoils/SpoilsPool.Ransom.cs` | Ransomed (and quest-delivered) prisoners' kit into spoils, with its three hooks; an executed captive's kit to his captors' spoils or the dungeon's treasury. |
| `Spoils/SpoilsPool.PrisonerStrip.cs` | Prisoners left on the loot screen stripped for half their kit. |
| `Spoils/RansomMenuTooltip.cs` / `RansomScreenSpoilsLabel.cs` | The spoils half of a ransom on the tavern option and the ransom screen (display only). |
| `Spoils/SpoilsPool.MarketSack.cs` | The sack of a stormed fief, tiered by the vanilla aftermath choice (Devastate / Pillage / Show Mercy): wealth and prosperity fractions, the market-goods sack, and the orphaned-capture sweep. |
| `Spoils/SiegeAftermathPatches.cs` | Replaces vanilla's army-size prosperity penalty with the flat tier fraction, and zeroes the gold it minted for the victors. |
| `Spoils/SpoilsPool.Wages.cs` | The daily wage deposit (mercenary double wage, militia hand-off) and the main party's orphan sweep. |
| `Spoils/SpoilsPool.Maintenance.cs` | Daily field upkeep and its market hand-off; the recruit seed and its two recruit events. |
| `Spoils/SpoilsPool.UpgradeMath.cs` | Upgrade pricing, salvage credit, the purse carried on graduation, the player-side commit path. |
| `Spoils/SpoilsPool.Cap.cs` | The days-of-keep ceiling, `GetPartyPayee` and `IsCompanionStack`. |
| `Spoils/SpoilsPool.LeaderCut.cs` | The commander's cut — the one spoils→gold exit. |
| `Spoils/SpoilsPool.Transfers.cs` | Carrying a purse across a party transfer. |
| `Spoils/RBMSpoilsCampaignBehavior.cs` | Event subscriptions and `SyncData`. |
| `Spoils/MaintenanceFinanceLine.cs` | Charges the clan's daily maintenance off the finance model's apply pass, and writes its breakdown lines. |
| `Spoils/MaintenancePartyWageLine.cs` / `MaintenanceTroopTooltipLine.cs` | Maintenance in the party-wage tooltip and, per man, in the troop tooltip (display only). |
| `Spoils/SpoilsTransferOnPartyScreen.cs` | Purse follows men moved on the party screen. |
| `Spoils/SpoilsTransferOnSpecialScreens.cs` | Purse follows men on the two screens with no left owner party: garrison donation and creating a companion's clan party. |
| `Finance/ClanEventGoldLedger.cs` | 30-day record of the gold paid to the player's clan per event (leader's cut, companions' share, mint cuts, gold-paid promotions incl. owner-paid garrison promotions, blood money, ship scrap, a ruling player's mercenary pay), pruned to the window, with the share that went to a clan member other than the player kept apart (`GetDayElsewhere`). Not on any finance breakdown (that gold is never on the apply pass); read only by the Clan finances record (`UI/Ledger/RBMClanFinanceLedger.cs`). |
| `Finance/BloodMoney.cs` | Blood money paid to a ransom broker to end a feud is credited to that town's citizen wealth (vanilla paid it to nobody), and recorded in the event-gold ledger. |
| `Finance/ShipScrapGold.cs` | Records the scrap gold a disbanded clan party's leftover ships pay the player in the event-gold ledger. |
| `Finance/SettlementAccrualPool.cs` | Per-clan pool of money fief ticks book for the clan's next finance apply pass, plus which fiefs have booked their day, so the display can project exactly what the next apply settles. Used by the wealth tax and the garrison maintenance subsidy. |
| `Finance/FiefProfitLines.cs` | Per-fief rows: the Fiefs tab's dead Tariffs row becomes the fief's wealth tax / castle surplus, Garrison Wages becomes the owner's residual after the treasury pays, both with hints; town management gets an owner-income row. Display only. |
| `Finance/ClanFinanceTabLines.cs` | Deferred postfixes on `CalculateClanIncome` / `CalculateClanExpenses` so the Clan screen's Finances tab totals carry every RBM line the denar tooltip does. |

### Spending and upgrades

| File | Role |
|---|---|
| `Upgrades/SpoilsUpgradePatches.cs` | AI upgrade reimplementation (`UpgradeReadyTroops`). |
| `Upgrades/PartyScreenStagedUpgrades.cs` | Player-side staging and gold-math fix. |
| `Upgrades/RBMCampaignPatches.cs` | `GetGoldCostForUpgrade` + the `GetUpgradeHint` tooltip breakdown. |
| `Upgrades/UpgradeSupply.cs` | The supply-town gate, the market draw, and the payment leg. |
| `Upgrades/MountValueUpgrade.cs` | Pricing the horse instead of consuming one. |
| `Upgrades/PartyUpgradeBudget.cs` | The per-party daily cap on upgrade gold, and its save. |
| `Upgrades/UpgradeFormationWeights.cs` | Weighted AI choice of branch at an upgrade fork (culture, garrison, traits). |
| `Upkeep/TroopUpkeep.cs` (+ `.Food.cs` / `.FoodForecast.cs` / `.Healing.cs` / `.Luxury.cs` / `.Stewardship.cs`) | Settlement food, carousing, paid healing, luxuries; days-of-food forecast; the leader's Steward XP. |
| `Upkeep/TroopMarketFeedback.cs` | Where troop spending lands in a settlement's purse. |
| `Upkeep/RBMTroopUpkeepCampaignBehavior.cs` | Event subscriptions and `SyncData`. |
| `Wages/TierBasedWageModel.cs` | The per-tier wage table. |

### Everything else in the module

| Folder | Role |
|---|---|
| `Settlements/` | The two-pot settlement wealth ledger (`SettlementWealth`, `SettlementWealthTooltip`; the same tooltip wrapper also groups the Alt troop list by type and tier, `SettlementTroopTooltip`), its funnel over vanilla's writes (`SettlementGoldFunnel`, `NativeTradeConservation`, `ShipTradeFunnel`, `VillageGoldStock`), tariffs (`TradeTariff`), wealth tax and owner income (`WealthTax`, `SettlementIncomeFinanceLine`), minting, castle income (`CastleEconomy`), ransom funding (`RansomFunding`), garrison/militia/administrative upkeep (`GarrisonUpkeep`, `MilitiaUpkeep`, `AdministrativeUpkeep`), the owner/citizen garrison backstop (`GarrisonSubsidy` + its player-facing `GarrisonSubsidyFinanceLine`), wealth-driven garrison growth (`GarrisonRecruitCost`), drill XP (`GarrisonDrill`), garrison morale/size/wage-limit patches, AI lords' wage limit (`LordPartyWageLimit`), mercenary contract pay, fief starvation, the defence muster (`SettlementDefenseMuster`), notable and artisan purses, workshop purses and diagnostics, prison labour, building effects, and the **construction engine** (`Construction.cs` / `.Materials.cs` / `.Patches.cs`). Details in the sub-sections below. |
| `Recruitment/` | `RecruitPool` — each settlement's finite **manpower pool** (towns/castles off Prosperity, villages off Hearth): +0.03/point a day, ceiling 0.2/point, persisted as `RBM_settlementRecruitPool` by `RBMRecruitPoolCampaignBehavior` (daily refill + one `MANPOWER` economy-log summary line). Each volunteer slot the vanilla daily roll fills draws one man (`VolunteerSpawnPatch`, a before/after occupied-slot diff on `RecruitmentCampaignBehavior.UpdateVolunteersOfNotablesInSettlement`, clearing over-budget fills; `Priority.First` so `RecruitSupply`'s kit draw sees only admitted men). A garrison recruit (`GarrisonRecruitCost.GrowGarrison`, clamped in `Compute`, never below a 0.07/point reserve kept for volunteers) costs `1 + (garrison / softSize)²` men, `softSize` 150 for a town, 100 for a castle, plus Barracks +20/40/60. Promotions, transfers, prisoners, the defence muster and militia are not charged. Building multipliers: growth (`GetGrowthBonus`) +25% while Train Militia or Housing runs, +50% for Raise Troops, a village +10/20/30% from its bound fief's Roads; ceiling (`GetMaxBonus`) +10/20/30% from the Castellan's Office. Also `TavernMercenaryTroopsPatch` (forces the tavern's regular-mercenary spawn chance to 1, so towns never stock caravan guards). |
| `Production/` | Village production (`RBMVillageProduction`, `VillageHousehold`, `VillageShopping`, `VillageProductionIconPatch`), villager convoys, dispatch, escorts and deliveries, town food supply, storage and reserve (`RBMTownFoodSupply`, `TownStorage`, `TownFoodReserve`), citizen and workshop demand, the artisan bench (`ArtisanOutput`), workshop output choice (`WorkshopTroopOrders`, `WorkshopItemTierBias`, `WorkshopVillageBias`), smithy steel refining (`SteelRefining`), and quest trade goods. |
| `Workshops/` | RBM's ownership of the workshop rules. `RBMWorkshopModel` — a `WorkshopModel` decorator registered in `OnGameStart`; it owns `InitialCapital` (60,000), `CapitalLowLimit` (half of it) and `DailyExpense` (250, the standing overhead only), and applies `ArtisanOutput.Scale` inside `GetEffectiveConversionSpeedOfProduction`. Everything else delegates to `BaseModel`, so NavalDLC's own workshop-model decoration survives whichever order the two are registered in. `RBMWorkshopCycle` — the produce-or-not decision, as skip-prefixes on both `Can*WorkshopProduceThisCycle` gates: storage glut (folded in from the deleted `WorkshopHeadroomGate`), then a proportional margin (`payout − salary ≥ inputCost × 1.15`, replacing vanilla's speed-inverted floor; the artisans need only `payout > inputCost`), shop solvency and town cash, all judged on the payout `RBMWorkshopSettlement` will actually pay. Also the single `SettlesInGold` predicate (the artisans settle in kind). `RBMWorkshopSettlement` — the money and goods legs, as skip-prefixes on `ProduceAnOutputToTown` and `ConsumeInputFromTownMarket`: one sell-side valuation ceilinged at 10% of town gold (min 500) serves gate and payment alike, and inputs are priced on the whole draw rather than vanilla's one unit. `RBMWorkshopExpense` — the **salary**, a share of every sale paid to citizen wealth as `Source.WorkshopWages` right after the payout lands (`PaySalary`: 55% − 5% per 48,000 of `EquipmentCost`, floor 10%; nothing while capital is at or below 40,000, nothing for the artisans), and the daily overhead as one skip-prefix on `HandleDailyExpense` replacing vanilla's three methods, paid down vanilla's ladder (capital while above `CapitalLowLimit`, else a player owner's gold, else capital if it covers the overhead, else vanilla's own `ChangeWorkshopOwnerByBankruptcy`) and credited to citizens the same way. It still counts the day's batches off the two `TickOneProductionCycleFor*Workshop` methods, for the log and the card. `WorkshopCardPayrollLine` — the clan-card "Production Wages" row. See `WORKSHOP_RULES_PLAN.md` for the phases that follow. |
| `Economy/` | Market prices and liquidity, the hidden stock and exact-slot roster reads (`HiddenMarketStock`, `RosterStock`), party trade flow, caravan capital and trade volume, clan member party purses (`ClanPartyPurse`: for lord parties not led by the clan leader, every clan, the daily top-up floor 5,000 → 20,000 via a getter postfix scoped to `DefaultClanFinanceModel.AddPartyExpense`, the clan's skim threshold 10,000 → 40,000 in `AddIncomeFromParty`, and a clan-leader recruit loan of up to 20,000 a day held only for the length of `RecruitmentCampaignBehavior.CheckRecruiting`, the unspent part handed back in a finalizer; the leader keeps 20,000), recruit supply, capacity and price hint, trade-good values and categories (base and War Sails), the Trade XP soft-cap, prosperity equilibrium, and world-generation seeding (`RBMEconomyCampaignBehavior`). |
| `Caravans/` | Intra-kingdom supply caravans: `RBMCaravanDispatch` (matches a surplus town to a short one), `RBMCaravanArrival` (keeps vanilla trade logic off them, sells on arrival), `RBMCaravanInvestment` (a repayable citizen-wealth injection from a rich town to a struggling one), `RBMCaravanRegister` (the persisted register and manifests), `RBMCaravanBehavior` (weekly dispatch, cleanup, save). |
| `AI/` | AI lord behaviors: `LordEquipmentUpgrade` (buy culture-matched gear in town), `LordPackTrain` (pack animals and spare mounts), `RBMGarrisonRefillBehavior` (a depleted lord refills from a clan fortification's surplus garrison), `RBMRecruitBiasBehavior` (recruiting is free in his own fiefs), `RBMDeserterRaiderBehavior` (deserters hunt convoys and raid), `RBMLordSpawnSettlementBehavior` (a respawning lord appears at his clan's fortification). |
| `Simulation/` | The equipment-aware auto-resolve: weapon model, hit points, arm targeting, perks and command structure, morale, rout, wounded capture, player participation, the battle state and snapshot, the two-phase wall assault (`SimulationSiege.cs`) and siege artillery (`SimulationSiegeEngines.cs`). See `AUTO_RESOLVE.md`. |
| `Power/` | `StrategicTroopPower` and its tooltip — the campaign-side power figure; `SiegeDecisionGate` (the strength an AI lord needs before he besieges) and `StrategicPowerLog`. |
| `Spectate/` | Watching an AI-vs-AI battle as a no-agent spectator. |
| `UI/` | The party-screen spoils bar, maintenance label and map party tooltip, the clan-screen upgrade-budget control, the inventory weight column, settlement nameplate bubbles (`RBMMapNotifications`), the smithy refine-row layout, building-effect tooltips and the Projects grid (below), each with its prefab injection; `UI/Ledger/` (the RBM Ledger screen, Ctrl+Shift+K or the Escape menu, with its 30-day town/village histories and the Clan finances tab, below) and `UI/SimulationPanel/` (a live auto-resolve panel on the map battle-simulation view). |
| `SwitchLord/` | `LordSwitcher` — debug tool to take over another lord's party. |
| `Diagnostics/` | `SpoilsLog`, `EconomyLog`, `SimulationLog`, `CaravanLog`, `GarrisonRefillLog`, `LogRetention`, `BufferedLogWriter` (the shared buffered file handle every log writes through). |
| `RBMCampaignPatcher.cs` | Entry point (`DoPatching`), at the project root. |

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
  guardHouseTier x 0.3 x prosperity` of it is free labour that costs nothing. The day's points —
  free, material and cash alike — are multiplied by the Mason's efficiency factor (`1 + 0.05 x tier`)
  and by `PerkFactor` (governor skill and perks, the Battanian feat, market production goods; clamped
  1..2).
- **Spending order** — free labour, then clay/planks off the settlement's own market (up to half the
  day's work, never touching the last 20 pieces on the shelves; the men working them draw a further 0.5
  coin a point, all to townsmen), then wages at a coin a point of which half reaches the townsmen. Tools
  wear out at one load per 50,000 points and are bought the same way; a load owed that cannot be bought
  (none on the market, or the reserve cannot afford it) halves the day's output. With nothing queued, a
  quarter of the day's capacity (`IdleProjectShare`) goes to the least-built building. Nothing calls
  `ChangeGold`.
- **Seams** — a prefix on `BuildingsCampaignBehavior.TickCurrentBuildingForTown` takes the tick off
  vanilla (ours runs from `RBMSettlementWealthCampaignBehavior.OnDailyTickSettlement`), and postfixes on
  `DefaultBuildingConstructionModel.CalculateDailyConstructionPower`/`WithoutBoost` report the funded
  figure to the UI and the days-to-complete estimate. `GetBoostAmount` returns 0 and `GetBoostCost`
  `int.MaxValue` (vanilla's boost is gone), `TownManagementReserveControlVM.UpdateReserveText` is
  rewritten, and the reserve ceiling is raised through a `set_MaxReserveAmount` prefix.
- **Labour market** — `Construction.LabourMarket` resolves once per tick (cached a day) where the work is
  transacted: the fief itself if it has a citizen purse, else — for a castle, which has none — the nearest
  town it is not at war with, as `MilitiaUpkeep` arms a castle's watch. Wages, material and tool money
  land in that town's citizen purse and the tariff is levied there; goods still come off the castle's own
  stores first and off the town's shelves only when it has none. A castle that can reach no such town
  buys no materials and its wage coin leaves the ledger. Either way a castle gets half of each day's
  spend back into its own wealth.
- Towns and castles alike; the work is skipped under siege, though the daily budget deposit still runs. Logged as `BUILD` in `EconomyLog`; tool debt persists as
  `RBM_constructionToolDebt`.

#### Militia caps (`Settlements/MilitiaUpkeep.cs`)

A skip-prefix on `DefaultSettlementMilitiaModel.CalculateMilitiaChange` authors the whole day's change in
two stages: **growth** (`ComputeMilitiaGrowth` — base curve, understrength and prosperous-city musters,
Barracks +1/2/3, the kept vanilla modifiers) then **ceiling & floor** (`ApplyCeilingAndFloor`). Both caps
are shares of one base (`MilitiaCapBase`): town = prosperity, village = hearth, castle = average hearth of
its bound villages (`RBMProsperityEquilibrium.CastleTargetProsperity / CastleProsperityHearthFactor`; a
castle with none uses its prosperity / 1.5).

- **Soft cap** = base × clamp(40% + bonuses, 0, 70%). Bonuses in percentage points: Barracks +2/3/5,
  castle Guard House +2/3/5, Training Fields +1/2/3, Train Militia / Raise Troops +3 while running (no
  building bonus for villages); owner kingdom's policies (all three kinds) Citizenship +3, Cantons +3,
  War Sails' Bolster the Fyrd +3 (by string id), Serfdom −3. The three vanilla policies no longer add men a day.
- **Hard cap** = base × 75% for everyone. Between the caps positive growth × (1 − fill)², fill =
  (militia − soft) / (hard − soft) ("Over muster"); growth never crosses the hard cap.
- **Over the hard cap** the watch disbands 5% of the overflow a day (min 1, max the overflow); with the
  unpaid shed (1/day) the day takes the more negative of the two. Both are refunded their kit
  (`RecordMilitiaChange` → `RefundPendingDecline`); combat losses are not.
- Unchanged: understrength catch-up below 0.5 × the effective soft cap, prosperous-city muster,
  `CanAffordSpawn`, `CanKeepMilitia`, governor perks, the new-campaign seed (clamped to the soft cap).
- **War Sails**: `NavalDLCSettlementMilitiaModel` wraps the default model and added Accuracy Training
  (+2/day) and Bolster the Fyrd (×1.25) after RBM's caps. `NavalMilitiaChangePatch` (resolved by type
  name, `Prepare` false without the DLC) skips it: it asks the chain beneath for the growth stage only
  (`_growthOnlyDepth`), re-adds the perk (looked up by id `Accuracytraining`), drops the factor, then
  applies the ceiling & floor.

#### Building effects (`Settlements/BuildingEffects.cs`)

`BuildingEffects.Tier(town, townType, castleType)` reads `Building.CurrentLevel` for whichever of a
matched town/castle `DefaultBuildingTypes` pair the fief actually owns (Fortifications, Barracks,
Training Fields, Guard House, Mason, Roads map 1:1; Warehouse pairs with the castle Granary; Marketplace,
Tax Office and Waterworks are towns only). Everything below is gated on `rbmCampaignEnabled`, and every
vanilla effect stays in place unless the row says "replaces".

| Building | RBM effect | Seam |
|---|---|---|
| Fortifications | siege defence advantage x1.1/1.2/1.3 (**replaces** the old downward step from L3) | `SimulationSiege.MeasureWall` = `1 + 0.1 x level` |
| | garrison maintenance −0/5/10%; for militia only the keep/affordability bill is cut, not what it is charged | `GarrisonUpkeep.MaintenanceBill`, `MilitiaUpkeep.DailyMaintenanceBill` (read by `CanKeepMilitia` / `GarrisonRecruitCost.FullDailyBill`) |
| Barracks | arming a garrison or militia recruit −5/10/15% | `GarrisonRecruitCost.SpawnCost`, `MilitiaUpkeep.SpawnCostPerMan` + `ArmOneMilitiaman` |
| | intake ceiling +1/2/3 a day | `GarrisonRecruitCost.Compute` (added to `GarrisonSpawnDailyMax`, own tooltip line), `MilitiaUpkeep.ComputeMilitiaGrowth` |
| | militia soft cap +2/3/5 percentage points | `MilitiaUpkeep.SoftCapBuildingBonus` |
| | garrison manpower soft size +20/40/60 | `RecruitPool` garrison draw |
| Training Fields | garrison promotions −5/10/15% | `SpoilsUpgradePatches.DiscountGarrisonUpgrade` (both the affordability test and the billed sum) |
| | +10/20/30 XP a day for the garrison (10x vanilla's `ExperiencePerDay`), a third of that for the militia party | `GarrisonDrill` postfix on `GetEffectiveDailyExperience`, filter widened to `IsMilitia` (`MilitiaDrillShare = 1/3`) |
| | militia soft cap +1/2/3 percentage points | `MilitiaUpkeep.SoftCapBuildingBonus` |
| Train Militia / Raise Troops (daily) | militia soft cap +3 percentage points while it is the running daily project | `MilitiaUpkeep.SoftCapBuildingBonus` via `BuildingEffects.IsDailyProjectActive` |
| | manpower pool growth +25% (Train Militia, also Housing) / +50% (Raise Troops) while running | `RecruitPool.GetGrowthBonus` |
| Guard House | tariff +0.3/0.6/1.0 percentage points on GUARDED trade only (caravans, lords, the player) | `TradeTariff.Levy(.., guardedTrade: true)` from `SettlementWealth.RouteNativeWrite` and `InventoryLogic.DoneLogic` |
| | passive convict labour | the Guard House terms in the construction ceiling/free-labour above |
| Tax Office | wealth tax and minting cuts x1.05/1.1/1.15, owner and fief legs alike | `WealthTax.OnDailyTick`, `Minting` |
| Marketplace | tariff x1.1/1.2/1.3 on ALL channels | `TradeTariff.Levy` rate factor |
| Warehouse / Granary | granary = 30/40/50/60 days of the fief's own consumption (**replaces** the flat `TownFoodStockScale` x10), castles included; for towns it is also the market's food intake ceiling (`TownStorage.Headroom`) | `RBMTownFoodSupply.FoodStocksUpperLimitPatch`, sized off `GetFoodConsumption(town).Total` with a 300 floor |
| Mason | construction efficiency +5/10/15%, labour ceiling +10/20/30% (**replaces** `ConstructionPerDay`) | `Construction.MasonTier` |
| Waterworks | every other point of infrastructure worth +10/20/30% | `RBMProsperityEquilibrium.InfrastructureMultiplier` = `1 + score x 0.02 x (1 + 0.1 x tier)`, clamp unchanged |
| Roads and Paths | bound-village production +5/10/15% | `RBMVillageProduction.RoadsFactor`, applied to the tick and to `CalculateDailyProductionAmount` alike |
| | bound-village manpower pool growth +10/20/30% | `RecruitPool.GetGrowthBonus` |

Four more rows are CASTLE-ONLY, three of them building types a town has no equivalent of (accessors
`CastellanTier` / `CraftsmanTier` / `FarmlandsTier`, `null` town type):

| Building | RBM effect | Seam |
|---|---|---|
| Castellan's Office | 10/20/30% of garrison recruits enlist as `Culture.EliteBasicTroop` | `GarrisonRecruitCost.PickRecruit`, rolled per man and priced through `SpawnCostFor`; `Compute`/`SpawnCost` keep the common soldier so the wealth rate stays deterministic |
| | mounted garrison maintenance −10/20/30% | `GarrisonUpkeep.MaintenanceBill`, `character.IsMounted` elements only |
| | manpower pool ceiling +10/20/30% | `RecruitPool.GetMaxBonus` |
| Craftsman Quarters | castle income x1.1/1.2/1.3 | `CastleEconomy.OnDailyTick` |
| Farmlands | castle food production +10/20/30% (**replaces** the flat 6/12/18) | `RBMTownFoodSupply.TownFoodStocksChangePatch.Postfix`, castles only |
| Guard House (castle) | **replaces** vanilla's `Militia` +1/2/3 a day with militia soft cap +2/3/5 percentage points — the Barracks owns intake | `MilitiaUpkeep.AddMilitiaEffectOfBuildings` (strip), `MilitiaUpkeep.SoftCapBuildingBonus` (cap) |

#### Prison labour (`Settlements/PrisonLabour.cs`)

Every man in a fief's `PrisonRoster` eats 0.05 food a day and earns it 30 denars a day, towns and castles
alike. Income runs from the daily settlement pass as a third income step (`Source.PrisonLabour`, `EconomyLog`
tag `PRISON`); the food is charged in `RBMTownFoodSupply.FeedPopulation` for a town (provisioned from stock,
nobody billed) and as an explained line on the castle food postfix. `FoodConsumptionBreakdown.Prisoners`
carries it into the granary cap and the ledger tooltip. The construction side of the same prisoners
(`Construction`: +60 ceiling, 30 free points each) is separate and unchanged.

`UI/BuildingEffectTooltips.cs` postfixes `BuildingType.GetExplanationAtLevel` to append a plain "RBM:"
line per building type, so the town management project list names these effects beside vanilla's, and
strips vanilla's `GarrisonCapacity` / `GarrisonAutoRecruitment` lines; its `ApplyDescriptions()` rewrites
several buildings' description text on each session launch.

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

#### Clan finances ledger (`UI/Ledger/RBMClanFinanceLedger.cs`)

A 30-day record of what actually happened to the player's gold, shown on the RBM Ledger's Clan finances tab.
A record, never a projection: nothing here touches the denar tooltip, the Finances totals or Expected Gold.

| File | Role |
|---|---|
| `UI/Ledger/RBMClanFinanceLedger.cs` | The store, the day boundary and the five hooks below; `GetDays()` for the view model. |
| `UI/Ledger/RBMClanFinanceLedgerCampaignBehavior.cs` | Ctor reset, starts tracking on session launch, closes a finished day every hour, `SyncData`. |
| `UI/Ledger/RBMLedgerClanFinanceVM.cs` | The tab: headline, Income/Expenses/Net/Gold bar chart, day table, by-source table with Net row, traded goods grouped by the Towns tab's equipment categories. |

What is captured, and where:

- **Apply pass** — `ClanVariablesCampaignBehavior.DailyTickClan` (prefix/postfix/finalizer, player clan only) brackets
  the one call that pays the day, reading the leader's gold before and after. Inside it, a prefix on
  `DefaultClanFinanceModel.CalculateClanGoldChange` turns `includeDescriptions` on for that call alone, and a
  `Priority.Last` postfix keeps a copy of the result; the lines are read after the tick (the copy shares the
  explainer, so War Sails' wrapping model's line is in it too). The model is never called a second time, and
  descriptions change no number (`ExplainedNumber` sums the same with or without its explainer; the model only
  picks labelled vs unlabelled `Add` of the same value). Gold the pass moved beyond its lines (an empty purse,
  rounding, a leaderless party's wage taken straight from the leader) is the `#adj` row.
- **Event gold** — read from `ClanEventGoldLedger` per day, never copied: only the part that touched the
  player's purse (`GetDay − GetDayElsewhere`). The elsewhere part (a companion's party paying its own leader's
  cut or promotions) is only noted, since it reaches the player later through the model's party income.
- **Trades** — `InventoryLogic.DoneLogic` (main party, `IsTrading`, `__result`): per item from the screen's
  transaction history, plus a `#tradeadj` row for the difference to the purse's real movement (a merchant short
  of gold, an accepted trader offer). `SellItemsAction.ApplyInternal` with the main party on either side is a
  zero-alloc safety net (vanilla's trading behaviours all skip the main party).
- **Other** — the day's real change in gold minus everything above, banked when the day closes.

Day boundary: everything is filed under `(int)CampaignTime.Now.ToDays`, the key `ClanEventGoldLedger` uses. The
closing balance is read by `RollIfNeeded`, called first thing from a `Priority.First` prefix on
`GiveGoldAction.ApplyInternal` whenever the player's hero or main party is a side, from each hook above before it
records, hourly, and when the ledger opens. Every tracked flow therefore falls on the same side of midnight as
the gold it moved; an untracked change between midnight and the first look lands in the previous day's Other.
Days with no look are closed flat. The first tracked day (new game, or a save older than the ledger) starts at
the moment tracking does, with the event gold already recorded that day held back as a baseline (`evbase`);
an heir taking over the player's purse closes the old one and starts a new partial day.

Storage: `RBM_clanFinanceHist` holds one CSV series per field (`day`, `start`, `end`, `other`, `lines`, `trade`,
`evbase`, `flags`) appended with `RBMTownLedger.AppendInt/AppendStr`, so the same amortised trim; `RBM_clanFinanceDay`
holds the open day's running totals (`L|row`, `T|item|bu/bg/su/sg`, `B|kind`) and, while saving, its day, opening
balance and partial flag. Finance lines are keyed by their display text (the explainer exposes no id), escaped
(`%xx`) so it is safe in the columns; events by `EventGoldKind` name; goods by item string id. A language switch
therefore starts new rows for the finance lines.

## Lifecycle wiring (in `RBM/SubModule.cs`)

- `ApplyHarmonyPatches()` → `RBMCampaignPatcher.DoPatching(ref rbmcampaignHarmony)` (PatchAll +
  widget registration), or `UnpatchAll` when disabled.
- `OnSubModuleLoad()` (under `rbmCampaignEnabled`) → the `ApplyEarly(...)` prefab injections:
  `SpoilsBarPrefabPatch`, `ItemWeightPrefabPatch`, `MaintenanceLabelPrefabPatch`,
  `UpgradeLimitPrefabPatch`, `RBMEscapeMenuPrefabPatch`, `RefineRowLayoutPrefabPatch`,
  `ProjectsGridPrefabPatch`, `TownManagementGridPatch`. These **must** run here, not in
  `ApplyHarmonyPatches`, because Gauntlet parses and caches those prefabs before `OnGameStart`. The
  spoils log is not opened here: early traces buffer until `SpoilsLog.StartCampaignLog` opens the file
  on session launch.
- `OnApplicationTick` (on the map) → `LordSwitcher.CheckHotkey()` and `RBMLedgerHotkey.CheckHotkey()`.
- `OnGameStart()` (Campaign only, under `rbmCampaignEnabled`) → adds fifteen behaviors:
  `RBMSpoilsCampaignBehavior`, `RBMTroopUpkeepCampaignBehavior`, `RBMSimulationCampaignBehavior`,
  `RBMSpectateCampaignBehavior`, `RBMEconomyCampaignBehavior`, `RBMSettlementWealthCampaignBehavior`,
  `RBMCaravanBehavior`, `RBMVillageLedgerCampaignBehavior`, `RBMTownLedgerCampaignBehavior`,
  `RBMClanFinanceLedgerCampaignBehavior`, `RBMGarrisonRefillBehavior`, `RBMRecruitBiasBehavior`, `RBMSettlementDefenseBehavior`,
  `RBMDeserterRaiderBehavior`, `RBMRecruitPoolCampaignBehavior` — and, registered last,
  `AddModel(new RBMWorkshopModel())`. (`SaveRosterRepairBehavior`, added for every campaign, is RBM's,
  not RBMCampaign's.)

### Campaign event listeners

`RBMSpoilsCampaignBehavior` (`SpoilsPool`):
- `OnSessionLaunchedEvent` → session setup (open the spoils log, prune exempt parties' purses)
- `MapEventEnded` → casualties, captured purses, loot distribution
- `RaidCompletedEvent` → village-raid plunder
- `DailyTickSettlementEvent` → besieger snapshot and the daily siege drain
- `OnSiegeAftermathAppliedEvent` → town/castle sack, tiered by the aftermath choice
- `OnSettlementOwnerChangedEvent` → besieger snapshot handshake for that sack (siege captures only)
- `HourlyTickEvent` → the sack sweep (orphaned captures, stale handshake marks)
- `DailyTickPartyEvent` → wage deposits
- `MobilePartyDestroyed` → prune purses, upgrade caps (`PartyUpgradeBudget`) and the payee cache (`UpgradeSupply`)
- `PlayerUpgradedTroopsEvent` → charge staged spoils
- `OnTroopRecruitedEvent` / `OnUnitRecruitedEvent` → seed a recruit's upkeep. The two are
  **disjoint by source**, not duplicates: the player's recruit screen fires the second (with no
  settlement argument) and the AI path fires the first.

`RBMTroopUpkeepCampaignBehavior` (`TroopUpkeep`):
- `SettlementEntered` → buy food
- `HourlyTickPartyEvent` → paid healing, buy food, carouse, luxuries, provisioning Steward XP
- `DailyTickPartyEvent` → food-reserve and spare-mount Steward XP; `FiefStarvation` (a starving field
  party loses wounded)
- `MobilePartyDestroyed` → prune food state

Maintenance has no listener: it is charged from the `DefaultClanFinanceModel.CalculateClanGoldChange`
patch in `MaintenanceFinanceLine`.
