# RBM Campaign — Spoils System Technical Reference

The [narrative README](README.md) explains *what* the spoils economy does and *why*. This document is the *how*: the exact formulas, constants, clamps, and code locations, for anyone tuning the system or reading the source.

All paths are relative to the `RBMCampaign/` project. A **point of spoils == 1 gold piece** — the two are the same currency in different pockets. The purse is a per-troop-**stack** value, keyed `party.Id + "#" + character.StringId`, persisted under the `SyncData` key `RBM_troopSpoilsGold`.

Master switch: `IsEnabled => troopUpgradeCostMultiplier > 0f` (`Spoils/SpoilsPool.cs`). At `0` every entry point below early-returns and the system is inert.

---

## Config fields and defaults (`RBMConfig/RBMConfig.cs`)

| Field | Default | Used by |
| --- | --- | --- |
| `troopUpgradeCostMultiplier` | `1f` | upgrade cost/credit, spoils cap, wage↔gold equivalence; **`0` disables the whole system** |
| `troopUpgradeSpoilsLootMultiplier` | `1f` | battlefield salvage share |
| `troopLootPiecesPerMan` | `3` | loot carry capacity |
| `troopLootOverlookChancePerTier` | `0.5f` | per-tier overlook probability |
| `troopWageTierBase` | `20` | daily wage = base × tier for non-heroes (`0` = vanilla wage) |
| `troopRaidSpoilsMultiplier` | `0.25f` | raid **and** siege plunder pot |
| `troopFallenSpoilsCaptureFraction` | `0.75f` | share of a beaten enemy's purse captured |
| `troopSettlementFoodDays` | `20` | days of rations bought at market (`0` disables food) |
| `troopFoodWageFraction` | `0.5f` | food price ceiling, as a share of daily wage |
| `troopSettlementFunWageFraction` | `1.5f` | carousing, as a multiple of daily wage |
| `settlementProsperityPerGoldSpent` | `0.02f` | prosperity/hearth moved per gold of worth, both ways — trade/carousing add, militia & production drain, villager produce returns home (`0` = layer off) |
| `troopSpoilsCapDays` | `20` | days of keep (wage + field maintenance) a stack holds before upkeep spends the surplus; the flush threshold |
| `troopLuxuryCooldownDays` | `20` | cooldown between over-cap luxury splurges |
| `troopLuxurySpendChance` | `0.02f` | per-check chance an over-cap stack buys a luxury |

Exempt from holding a purse entirely: villager parties (`IsExemptParty`, gated at `AddSpoils`).

---

## 1. Upgrade pricing by the kit

### Equipment value (`Spoils/SpoilsPool.Equipment.cs`)

A troop's kit is valued as the mean of its battle loadouts, so a troop that spawns in several kits is priced on the average:

```
GetSetValue(equipment) = Σ item.ItemValue   over all armor + weapon slots, skipping empties
GetEquipmentValue(char) = (sets.Count == 0) ? 0 : Σ GetSetValue(set) / sets.Count      // integer division
```

Sets come from `character.BattleEquipments` (fallback `FirstBattleEquipment ?? Equipment`). Cached per `CharacterObject` in `_equipmentValueCache`.

### Cost / credit of one man's upgrade (`Spoils/SpoilsPool.UpgradeMath.cs`)

```
delta = GetEquipmentValue(target) - GetEquipmentValue(character)

cost   (delta > 0):   Max(1, Round(delta   * troopUpgradeCostMultiplier))
credit (delta < 0):   Max(1, Round(-delta  * troopUpgradeCostMultiplier))   // cheaper kit → purse gains
```

Cost and credit are mutually exclusive (one needs the new kit dearer, the other cheaper). Minimum non-zero price is **1**.

### Spending across a batch (spoils drain per man, gold pays the remainder)

Spoils are spent one man at a time — the leading men upgrade free, the rest are billed to your treasury:

```
coveredMen  = availableSpoils / spoilsCost                       // float
unpaidMen   = Max(0, count - Min(coveredMen, count))
freeCount   = Min(availableSpoils / spoilsCost, stackSize)       // whole men
batchSpoils = Min(availableSpoils, spoilsCost * count)           // drawn from purse
availableSpoils = Max(0, GetSpoils - StagedSpoils)               // staged upgrades reserve their share
```

### Gold price + vanilla perks (`RBMCampaignPatches.cs`, `BuildUpgradeGoldCost`)

The gold charge is an `ExplainedNumber` seeded with the kit delta, then the native upgrade-discount perks apply on top:

```
base = (targetEquipmentCost - characterEquipmentCost) * goldFactor       // goldFactor = number of unpaid men
  + SoundReserves        (Steward)
  + RenownedArcher       (Bow, if IsRanged)
  + KhuzaitRecruitUpgradeFeat.AddFactor  (if IsMounted)
  + Contractors          (Steward, if mercenary / gangster / caravan guard)
  + AddFactor(troopUpgradeCostMultiplier - 1f, "Realistic Battle Mod")

GetFullUpgradeGoldCost      = Max(0, BuildUpgradeGoldCost(..., 1f).RoundedResultNumber)
GetBatchUpgradeGoldCost     = Max(0, BuildUpgradeGoldCost(..., unpaidMen).RoundedResultNumber)
```

A cheaper-kit "upgrade" floors at **0 gold**; its surplus is handed back as a spoils credit instead.

### Purse carried on graduation

When men upgrade, they carry their share of the leftover purse to the new troop:

```
GetCarriedSpoils(poolAfterSpend, count, stackSizeBefore) =
    count >= stackSizeBefore ? poolAfterSpend
                             : (long)poolAfterSpend * count / stackSizeBefore
```

Player commit runs through `OnPlayerUpgradedTroops`; AI through `SpoilsUpgradePatches`. Staged player upgrades reconcile vanilla's gold charge against RBM's via a correction term in `PartyScreenStagedUpgrades.cs`.

---

## 2. Battle spoils — who is stripped, and for how much

Fires on `MapEvent` end (`Spoils/SpoilsPool.BattleLoot.cs`, `SpoilsPool.Casualties.cs`). Skipped when `troopUpgradeSpoilsLootMultiplier <= 0` or there is no losing side.

### Salvage fraction per item

```
MinSalvageFraction = 0.25f
MaxSalvageFraction = 0.75f
RollSalvageFraction(item) = MBRandom.RandomFloatRanged(0.25f, 0.75f)   // uniform, mean 0.5, rolled per man per slot
spoilsByTier[tier] += (long)(item.ItemValue * RollSalvageFraction(item))
```

Nothing salvages whole: every piece yields a random quarter-to-three-quarters of its value.

### Who is stripped

Only troops in the `DiedInBattle` rosters — of **both** sides, since the victor holds the field and strips his own dead too. Wounded and routed men are never in `DiedInBattle`, so they keep their kit by construction.

### Contribution share

Each victor party's cut of the salvage scales with how much it actually fought:

```
totalContribution = Σ Max(0, victor.ContributionToBattle)
weight   = totalContribution > 0 ? Max(0, victor.ContributionToBattle) : 1
divisor  = totalContribution > 0 ? totalContribution : winner.Parties.Count
share    = (float)weight / divisor * troopUpgradeSpoilsLootMultiplier
```

Zero-contribution simulated battles fall back to an even split.

### Capturing the enemy's purse (`SpoilsPool.Casualties.cs`)

Beating an enemy also captures a slice of *their* stacks' purses:

```
fallenMen = killed + wounded
preBattle = GetStackSize + killed + routed
share     = (long)purse * Min(fallenMen, preBattle) / preBattle       // routers dilute but keep their share
toVictors = Round(pot * Clamp(troopFallenSpoilsCaptureFraction, 0, 1))  // default 0.75; remainder is lost
```

Captured pot is distributed to winners by contribution, then within a party by tier-weight `Number * Max(1, Tier)`.

### Your own casualties' purses

```
FallenPurseRecoveryFraction = 0.5f
partial stack loss:  lost = (long)purse * dead / (survivors + dead)
whole stack wiped:   recover Round(purse * 0.5), split among survivors by headcount (remainder to the largest stack)
```

---

## 3. Loot division on the field (`Spoils/SpoilsPool.BattleLoot.cs`)

Claimants are sorted by troop tier, highest first; the field is worked from the top item-tier down, so **veterans pick first**.

```
carryCapacity(men) = Max(0, troopLootPiecesPerMan) * men            // default 3 per man
tierGap(itemTier, char) = (char.Tier - 1) - itemTier                // item tiers 0-based, troop tiers 1-based
```

The chance a man stoops for gear beneath him compounds per tier of gap:

```
noticeFraction(gap) = gap <= 0 ? 1
                              : Pow(1 - Clamp(troopLootOverlookChancePerTier, 0, 1), gap)
```

At the default `0.5`: a man takes gear one tier down half the time, two tiers down a quarter — the "coin-flip per tier." At `1.0` he sees nothing beneath his own tier at all. The fractional piece is rolled for (`MBRandom.RandomFloat < frac`) rather than dropped, so a lone piece a veteran would notice a quarter of the time is not silently unlootable.

Within an equal-tier group, pieces split proportionally by headcount, capped by remaining carry room, with leftovers cascading to peers and then down to greener troops. Points credited per taken piece use `valuePerPiece = spoilsByTier[tier] / piecesByTier[tier]`.

---

## 4. Raid spoils (`Spoils/SpoilsPool.Plunder.cs`)

```
pot = village.Hearth * Clamp(raidEvent.RaidDamage, 0, 1) * troopRaidSpoilsMultiplier
if (pot < 1) skip
per raider party:  share = Round(pot * (contributionWeight / divisor))   // same weight/divisor scheme as battle
```

`RaidDamage` is the 0–1 fraction of hearth the raid stripped, so a raid broken off early pays proportionally less. Only fires when the attacker won. Each party's share is split evenly among its men (heroes excluded).

## 5. Storm / siege spoils (`Spoils/SpoilsPool.Plunder.cs`)

```
pot = town.Prosperity * troopRaidSpoilsMultiplier      // same multiplier, but scaled to prosperity, not hearth
if (pot < 1) skip
granted = GrantFlatSpoilsToParty(captor, Round(pot))
```

Fires **only** on `ChangeOwnerOfSettlementDetail.BySiege` — a fief handed over by barter, gift, or vote sacks nothing. The whole pot goes to the single credited captor party, not the army.

## 6. Wage deposit (`Spoils/SpoilsPool.Wages.cs`)

Daily, per non-hero stack, on every party:

```
wage    = wageModel.GetCharacterWage(character) * element.Number
granted = wage                                                  // the stack's whole wage
```

`GetCharacterWage` is itself overridden for non-heroes (`Wages/TierBasedWageModel.cs`): the daily wage is `troopWageTierBase × character.Tier`, replacing vanilla's wage table (`0` keeps vanilla). A stack's whole wage lands in its purse — the party's gold is untouched, so this only reinterprets where the pay went. Spoils are a **closed loop**: nothing is handed back to gold.

---

## 7. Food buying (`Upkeep/TroopUpkeep.Food.cs`)

`MenPerFoodPerDay = MobilePartyFoodConsumptionModel.NumberOfMenOnMapToEatOneFood` (vanilla 20 — one food feeds 20 men for a day).

```
wanted = ceil(element.Number * troopSettlementFoodDays / MenPerFoodPerDay)
priceCeiling = Round(perManDailyWage * troopFoodWageFraction * MenPerFoodPerDay)   // "half a day's wage per man"
```

Buying is dearest-first, in two passes: first only items at or under the ceiling (a recruit stops at grain), then anything at all rather than starve. Purchases draw real stock at real settlement prices from the purse, and credit the settlement. Partial supply feeds proportionally: `fedHours = Max(1, foodDays * 24 * bought / wanted)`.

Interaction with party food stores: a Harmony postfix on `CalculateDailyBaseFoodConsumptionf` shrinks the party's own consumption for provisioned men — `AddFactor(unfedFraction - 1)` on the (negative) base — so self-fed troops don't also eat from your stores. Heroes always count as unfed.

## 8. Drink / carousing (`Upkeep/TroopUpkeep.cs`)

Hourly, for visiting parties only (not garrison or militia), per non-hero stack with a purse:

```
dailyWage = wageModel.GetCharacterWage(character) * element.Number
spend     = Round(dailyWage / 24 * troopSettlementFunWageFraction)           // hourly base

surplus = purse - GetSpoilsCap
if surplus > 0:
    cap <= 0:  spend += surplus                                              // top-tier: drink the lot
    cap  > 0:  spend += Round(surplus / 24 * troopSettlementFunWageFraction * (purse / cap))
spend = Min(purse, spend)                                                    // never into debt
```

The surplus bite scales by how many times over its cap the purse stands, so a bloated garrison drinks faster. At the `1.5` default the base rate alone exceeds a day's wage per day idled. Spending credits the settlement (see below).

### Prosperity / hearth flows

One rate, `settlementProsperityPerGoldSpent`, moves a settlement's Prosperity (town/castle) or
Hearth (village) in **both** directions. `TroopUpkeep.CreditSettlement` (`internal`) is the add:

```
gain = goldWorth * settlementProsperityPerGoldSpent        // default 0.02 per gold
Town    → Prosperity += gain            (drains subtract, clamped at 0)
Village → Hearth     += gain
```

The inputs (all gate on `rate > 0`):

| Source | Hook | `goldWorth` | Dir |
|---|---|---|---|
| Food / drink / luxury | `TroopUpkeep`, visiting stacks | spend | + |
| Market purchase | `MarketTradeProsperity` postfix on `SellItemsAction.Apply` | `number × GetItemPrice` | + (settlement bought from) |
| Villager produce sale | `VillagerTradeHearth` postfix on `SellGoodsForTradeAction.ApplyByVillagerTrade` | Δ`PartyTradeGold` | + (home village) |
| Militia wages | `MilitiaUpkeep` on `DailyTickSettlementEvent` | wage × `settlement.Militia` | − |
| Production | `ProductionUpkeep` on `OnItemProducedEvent` | `item.Value × count` | − |

Money spent in a settlement stays there; goods made there cost it; goods sold return it — netting
where goods actually move. Full per-hook detail in `ARCHITECTURE.md → Settlement prosperity flows`.

---

## 9. The spoils cap — days of keep (`Spoils/SpoilsPool.Cap.cs`)

What a stack counts itself flush against: a configured number of days' worth of its own keep — its
daily wage and its daily field maintenance together. Priced the same for every tier (a veteran's
dearer wage and kit already deepen his days' keep), so there is no separate war chest and top-tier
troops with no upgrade to save for are held to the same rule.

```
dailyWage        = PartyWageModel.GetCharacterWage(char) * stackSize
dailyMaintenance = DailyMaintenanceCost(char, stackSize)                      // §7's per-stack upkeep

GetSpoilsCap = (dailyWage + dailyMaintenance) * troopSpoilsCapDays            // 0 days ⇒ cap 0
```

The cap is a behavioural threshold, not a hard limit: a purse can hold more than its cap (loot and wage both fill past it), but once over, upkeep draws the surplus down — carousing bites harder (§8) and only over-cap stacks splurge on luxuries (§10). Nothing over the cap is minted back to your gold; spoils are a **closed loop** spent only on upgrades, food and drink.

`GetPartyPayee` (owner if alive, else `LeaderHero`) lives in this file too — the party-leader spoils cut pays through it.

---

## 10. Luxury splurges (`Upkeep/TroopUpkeep.Luxury.cs`)

Only stacks already over their spoils cap indulge. Per check:

```
if MBRandom.RandomFloat >= troopLuxurySpendChance: skip     // default 0.02
buy one random affordable luxury (ItemCategory.LuxuryDemand > BaseDemand, trade good or equipment, not food)
cost = Max(1, GetElementUnitCost); drawn from purse, credits the settlement
cooldown until NowHours + troopLuxuryCooldownDays * 24       // default 20 days
```

---

## Persistence & keys

| Thing | Value |
| --- | --- |
| Purse save key | `RBM_troopSpoilsGold` |
| Fed-state save key | `RBM_troopFedUntilHours` |
| Luxury-cooldown save key | `RBM_troopLuxuryCooldown` |
| Stack key format | `party.Id + "#" + character.StringId` |
| Item-tier clamp | `Min(Max((int)item.Tier, 0), NumTiers - 1)` |

On party transfers, a leaving detachment carries its `GetCarriedSpoils` share (and its fed-state) to the destination party (`Spoils/SpoilsPool.Transfers.cs`).
