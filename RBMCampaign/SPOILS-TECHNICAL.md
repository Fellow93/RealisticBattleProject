# RBM Campaign — Spoils System Technical Reference

The [narrative README](README.md) explains *what* the spoils economy does and *why*. This document is the *how*: the exact formulas, constants, clamps, and code locations, for anyone tuning the system or reading the source.

For how spoils sit inside the wider campaign economy — the other gold pools, what each flow debits and credits, and which edges still conjure or destroy money — see [`docs/economy-money-flows.md`](../docs/economy-money-flows.md).

All paths are relative to the `RBMCampaign/` project. A **point of spoils == 1 gold piece** — the two are the same currency in different pockets. The purse is a per-troop-**stack** value, keyed `party.Id + "#" + character.StringId`, persisted under the `SyncData` key `RBM_troopSpoilsGold`.

Master switch: `IsEnabled => troopUpgradeCostMultiplier > 0f` (`Spoils/SpoilsPool.cs`). At `0` every entry point below early-returns and the system is inert.

---

## Config fields and defaults (`RBMConfig/Config/RBMConfig.Campaign.cs`)

Only the fields this document's formulas read. The rest of the campaign set — the supply-town gate, the recruit stock draw, the stewardship XP rates — is tabulated in [README.md](README.md#tuning-it).

| Field | Default | Used by |
| --- | --- | --- |
| `troopUpgradeCostMultiplier` | `1f` | upgrade cost/credit; **`0` disables the whole system** |
| `troopUpgradeChargeMountValue` | `true` | whether the horse and harness count in an upgrade's kit value (§1) |
| `troopUpgradeSpoilsLootMultiplier` | `1f` | battlefield salvage share |
| `troopLootPiecesPerMan` | `3` | loot carry capacity |
| `troopLootOverlookChancePerTier` | `0.5f` | per-tier overlook probability |
| `troopFallenSpoilsCaptureFraction` | `0.75f` | share of a beaten enemy's purse captured |
| `troopSettlementFoodDays` | `20` | days of rations bought at market (`0` disables food) |
| `troopFoodWageFraction` | `0.5f` | food price ceiling, as a share of daily wage |
| `troopSettlementFunWageFraction` | `0.25f` | carousing, as a multiple of daily wage |
| `troopSpoilsCapDays` | `20` | days of keep (wage + field maintenance) a stack holds before upkeep spends the surplus; the flush threshold |
| `troopLuxuryCooldownDays` | `20` | cooldown between over-cap luxury splurges |
| `troopLuxurySpendChance` | `0.02f` | per-check chance an over-cap stack buys a luxury |
| `troopMaintenanceFraction` | `0.005f` | daily field maintenance, as a share of mounted kit worth (§11); also the cap's maintenance term |
| `independentMaintenancePurseFraction` | `1f` | share of maintenance an independent or mercenary clan's purses may meet (§11) |
| `recruitMaintenanceDays` | `20` | days of maintenance a recruit's purse is seeded with (§11) |
| `troopLeaderSpoilsCutFraction` | `0.05f` | base leader's cut (§12) |
| `troopSpoilsHealGoldPerTier` | `10` | surgeon's fee per man per tier (§13) |
| `troopSpoilsHealFractionPerHour` | `0.05f` | most of a stack's wounded mended per hour (§13) |

The raid and siege spoils shares are **code constants**, not config: `RaidSpoilsShare = 0.5f`, `SiegeDrainSpoilsShare = 0.5f`, `SiegeDailyDrainRate = 0.05f` (`Spoils/SpoilsPool.Plunder.cs`) and the per-aftermath tiers in `Spoils/SpoilsPool.MarketSack.cs`.

Exempt from holding a purse entirely: villager parties (`IsExemptParty`, gated at `AddSpoils`). Bandit parties are not exempt — they loot and plunder like anyone — but they bank no wage, are charged no maintenance and are seeded nothing on recruitment.

---

## 1. Upgrade pricing by the kit

### Equipment value (`Spoils/SpoilsPool.Equipment.cs`)

A troop's kit is valued as the mean of its battle loadouts, so a troop that spawns in several kits is priced on the average:

```
GetSetValue(equipment)  = Σ item.ItemValue   over all armor + weapon slots, skipping empties
GetEquipmentValue(char) = (sets.Count == 0) ? 0 : Σ GetSetValue(set) / sets.Count      // integer division
GetEquipmentValueWithMount(char)  = the same, with the Horse and HorseHarness slots included
GetUpgradeEquipmentValue(char)    = troopUpgradeChargeMountValue ? GetEquipmentValueWithMount : GetEquipmentValue
```

Sets come from `character.BattleEquipments` (fallback `FirstBattleEquipment ?? Equipment`). Cached per `CharacterObject` in `_equipmentValueCache` / `_mountedEquipmentValueCache`. Upgrades price off `GetUpgradeEquipmentValue`; maintenance, the recruit seed, battle salvage and the prisoner strips price off the mounted value.

### Gold price + vanilla perks (`Upgrades/RBMCampaignPatches.cs`, `BuildUpgradeGoldCost`)

The gold charge is an `ExplainedNumber` seeded with the kit delta, already scaled by the multiplier, then the native upgrade-discount perks apply on top as factors:

```
base = (GetUpgradeEquipmentValue(target) - GetUpgradeEquipmentValue(character))
       * goldFactor * troopUpgradeCostMultiplier                         // goldFactor = number of unpaid men
  + SoundReserves        (Steward)
  + RenownedArcher       (Bow, if IsRanged)
  + KhuzaitRecruitUpgradeFeat.AddFactor  (if IsMounted)
  + Contractors          (Steward, if mercenary / gangster / caravan guard)

GetFullUpgradeGoldCost      = Max(0, BuildUpgradeGoldCost(..., 1f).RoundedResultNumber)
GetBatchUpgradeGoldCost     = Max(0, BuildUpgradeGoldCost(..., unpaidMen).RoundedResultNumber)
```

The multiplier scales the base rather than joining the perk factors, so it does not sum with them. The perks apply only when the party is a `MobileParty`.

### Cost / credit of one man's upgrade (`Spoils/SpoilsPool.UpgradeMath.cs`)

```
spoilsCost (per man) = GetFullUpgradeGoldCost(party, character, target)     // perks and all
credit     (per man) = Max(1, Round(surplus * troopUpgradeCostMultiplier))
                       where surplus = GetUpgradeEquipmentValue(character) - GetUpgradeEquipmentValue(target) > 0
```

A point of spoils is a gold piece, so a man the purse covers is charged exactly what the gold path would charge him — the perks discount both pockets alike. Cost and credit are mutually exclusive (one needs the new kit dearer, the other cheaper). A cheaper-kit "upgrade" floors at **0 gold**; its surplus is paid into the upgraded stack's purse instead.

### Spending across a batch (spoils drain per man, gold pays the remainder)

Spoils are spent one man at a time — the leading men upgrade free, the rest are billed to your treasury:

```
availableSpoils = Max(0, GetSpoils + incoming - outgoing - staged)  // party-screen transfers and staged upgrades this visit
coveredMen  = availableSpoils / spoilsCost                       // float
unpaidMen   = Max(0, count - Min(coveredMen, count))
freeCount   = Min(availableSpoils / spoilsCost, stackSize)       // whole men
batchSpoils = Min(availableSpoils, spoilsCost * count)           // drawn from purse
```

### Where the payment goes (`Upgrades/UpgradeSupply.cs`)

Only the **gold** leg reaches a town: `SupplyUpgradeFromTown` credits it to the supply town's citizen wealth through `TroopMarketFeedback.RegisterPurchase` (market fee included), or — gate off, or no town resolved — to the nearest friendly town, then the nearest town of any faction (`FindFenceTown`). The spoils leg is drawn from the purse and credits no one: men the purse covered re-armed from their own loot. With `troopUpgradeRequireSupplyTown` on, stock of the improved slots' classes and tiers leaves that town's market for the **gold-bought men only** (`goldBuyers = Round(goldPaid / spoilsCost)`); kit drawn worth more than the coin is levied the market fee on the difference.

The gate: a party stationed in a friendly/neutral settlement is always supplied (a town by itself, a castle or village by the nearest friendly city); one in the field needs a friendly town within `troopUpgradeSupplyRadius`. Bandits are never gated. The player's upgrade arrows are refused outright without a supply town; the AI still promotes the men its purse covers and closes only the gold leg.

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

Fires on `MapEvent` end (`Spoils/SpoilsPool.BattleLoot.cs`, `SpoilsPool.Casualties.cs`). The winners' casualty settlement and the capture of the enemy's purse run first, whenever there is a winner; the field salvage after it is skipped when `troopUpgradeSpoilsLootMultiplier <= 0` or there is no losing side.

### Salvage fraction per item

```
MinSalvageFraction = 0.25f
MaxSalvageFraction = 0.75f
RollSalvageFraction(item) = MBRandom.RandomFloatRanged(0.25f, 0.75f)   // uniform, mean 0.5, rolled per man per slot
spoilsByTier[tier] += (long)(item.ItemValue * RollSalvageFraction(item))
```

Nothing salvages whole: every piece yields a random quarter-to-three-quarters of its value. Each dead man is stripped of one of his battle sets drawn at random, **horse and harness included** (`EnumerateEquipmentSlots(..., includeMount: true)`).

### Who is stripped

Only non-hero troops in the `DiedInBattle` rosters — of **both** sides, since the victor holds the field and strips his own dead too. Wounded and routed men are never in `DiedInBattle`, so they keep their kit by construction.

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

Captured pot is distributed to winners by contribution (rounding remainder to the top contributor), then within a party by tier-weight `Number * Max(1, Tier)` (`GrantSpoilsWeightedByTier`, remainder to the heaviest-weighted stack). The losing side's purses are otherwise left alone.

### The winners' own casualties

Only the winning side's purses are settled, in two passes so roster order cannot change the outcome:

```
FallenPurseRecoveryFraction = 0.5f
partial stack loss:  lost = (long)purse * dead / (survivors + dead)
whole stack wiped:   recover Round(purse * 0.5), split among the party's surviving stacks by tier weight
                     (GrantSpoilsWeightedByTier); the rest is lost
```

### Companions

A companion hero in the party (any hero other than the payee, `IsCompanionStack`) claims loot and lump shares like a troop stack, but holds no purse: his share is paid straight to the party payee's gold (`GiveGoldAction` from null, recorded as `EventGoldKind.CompanionSpoils`) and no leader's cut is skimmed from it.

---

## 3. Loot division on the field (`Spoils/SpoilsPool.BattleLoot.cs`)

Claimants are sorted by troop tier, highest first, and within a tier the culture's noble line (the tree rooted at `Culture.EliteBasicTroop`) ahead of the levy; the field is worked from the top item-tier down, so **veterans pick first**.

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
RaidSpoilsShare = 0.5f                                                  // code constant, not config
drained = SettlementWealth.Debit(village, Round(villageWealth * Clamp(raidEvent.RaidDamage, 0, 1)), Source.Raid)
pot     = Round(drained * RaidSpoilsShare)                              // the rest is destroyed
if (pot < 1) skip
per raider party:  share = Round(pot * (contributionWeight / divisor))   // same weight/divisor scheme as battle
```

`RaidDamage` is the 0–1 share of the village the raid stripped, so a raid broken off early pays proportionally less; the ledger's clamp means a broke village pays nothing however hard it was hit. Only fires when the attacker won (`RaidCompletedEvent`). Each party's share is split among its stacks by tier weight (`GrantSpoilsWeightedByTier`, as captured spoils), then the leader's cut (§12) is skimmed.

The goods a raid carts off are cut separately, at the model: `RaidGoodsDestruction` postfixes `DefaultRaidModel.GetRaidLootMultiplier` to keep `Clamp(0.5 + 0.001 × leader's Roguery + 0.2 if a Nord leader, 0, 1)` of vanilla's haul (the army leader's skill and culture for a party in an army).

## 5. Siege and sack spoils (`Spoils/SpoilsPool.Plunder.cs`, `SpoilsPool.MarketSack.cs`)

**While the siege holds** (`OnBesiegedFortificationDailyTick`, daily per settlement): a besieged castle bleeds its treasury, a besieged town its citizen wealth (its treasury is spared):

```
SiegeDailyDrainRate = 0.05f, SiegeDrainSpoilsShare = 0.5f
drained = Debit(pot, Round(potBalance * 0.05))
spoils  = Round(drained * 0.5)        // to every besieging party, by headcount then tier weight; the rest destroyed
```

The same tick snapshots the besieging parties, so the sack can pay the whole siege after the camp is gone. Silent to the player.

**At capture**, the sack runs off vanilla's aftermath choice (`OnSiegeAftermathAppliedEvent`) — the player's menu, or `DetermineAISiegeAftermath` for an AI:

| Aftermath | Prosperity lost | Wealth stolen | Wealth → spoils | Market goods taken | Goods → spoils |
| --- | --- | --- | --- | --- | --- |
| Show Mercy | 10% | 10% | 0.5 | — | — |
| Pillage | 20% | 50% | 0.5 | 40% | 0.5 |
| Devastate | 50% | 90% | 0.1 | 90% | 0.1 |

```
wealthPot = Round(stolen * wealthSpoils)        // stolen from a town's citizen wealth, or a castle's treasury
goodsPot  = Round(Σ taken * UnitPrice * goodsSpoils)   // towns only, off town.Owner.ItemRoster; food at half the fraction
pot       = wealthPot + goodsPot                // the non-spoils remainder of both is destroyed
```

The pot is split across every besieging party by vanilla's contribution map (headcount fallback), then by tier weight, each with its leader's cut. A won sally-out (same event, previous owner's own faction) sacks nothing, and a fief changing hands by barter, gift or vote raises no aftermath and sacks nothing. `SiegeAftermathPatches` replaces vanilla's army-size prosperity penalty with the tier's flat fraction and zeroes the gold vanilla minted for the victors. A capture whose aftermath never arrives is sacked at the Pillage tier by the hourly sweep (`OnHourlyTickSackSweep`).

## 6. Wage deposit (`Spoils/SpoilsPool.Wages.cs`)

Daily, per non-hero stack, on every party but bandit parties:

```
wage    = wageModel.GetCharacterWage(character) * element.Number
granted = wage                                                  // the stack's whole wage
granted = wage * 2   if the payee's clan is a mercenary company under contract   // MercenaryContractPay
```

A garrison banks its wage like any troop (the fief pays it — see `Settlements/GarrisonUpkeep.cs`). A militia stack is routed to `MilitiaUpkeep.PayMilitiaUpkeep` instead: the settlement's funding pot pays a reduced wage into the purse — a quarter of a soldier's wage in a town (`MilitiaWageFactorTown = 0.25`), a tenth in a castle (`0.10`), nothing in a village (`0.0`) — and the militia's kit maintenance is then met from that purse first.

`GetCharacterWage` is itself overridden for non-heroes (`Wages/TierBasedWageModel.cs`), replacing vanilla's 1/2/3/5/8/12/17/23 table with a per-tier rate read off a table of its own — the medieval daily rates in pence at ten gold to the penny:

```
tier      1    2    3    4    5     6
foot     20   30   40   60  120   240
cavalry  30   40   60  120  240   480
```

Tier 0 rabble are paid as tier 1 (nobody serves for nothing) and anything above tier 6 clamps to the top rung. Heroes never reach this path and keep vanilla pay.

**Not configurable.** The table applies whenever RBMCampaign's patches are on. The former `troopWageTierBase` dial was removed: it read as a per-tier multiplier long after the wage stopped being a formula, and the only thing it still decided was whether the table applied at all — which is the module toggle's job.

A stack's whole wage lands in its purse — the party's gold is untouched, so this only reinterprets where the pay went. Spoils are otherwise a **closed loop**: the one place a purse becomes gold again is the leader's cut (§12), which draws the share back out of the same purses it was just deposited into, so no coin is minted by it. (A companion's share of a gather never enters a purse at all; it is paid as gold at once — §2.)

---

## 7. Food buying (`Upkeep/TroopUpkeep.Food.cs`)

`MenPerFoodPerDay = MobilePartyFoodConsumptionModel.NumberOfMenOnMapToEatOneFood` (vanilla 20 — one food feeds 20 men for a day).

```
wanted = ceil(element.Number * troopSettlementFoodDays / MenPerFoodPerDay)
priceCeiling = Round(perManDailyWage * troopFoodWageFraction * MenPerFoodPerDay)   // "half a day's wage per man"
```

Buying is dearest-first, in two passes: first only items at or under the ceiling (a recruit stops at grain), then anything at all rather than starve. Purchases draw real stock at real settlement prices (`TroopMarketFeedback.UnitPrice`) from the purse. Partial supply feeds proportionally: `fedHours = Max(1, foodDays * 24 * bought / wanted)`. Visiting parties only, on `SettlementEntered` and hourly while they stay: garrisons and militia are fed by their settlement and buy no rations.

Interaction with party food stores: a Harmony postfix on `CalculateDailyBaseFoodConsumptionf` shrinks the party's own consumption for provisioned men — it adds `members * (1 - unfedFraction) / MenPerFoodPerDay` back onto the (negative) base rather than a factor, so the vanilla food perks still scale what is left — and self-fed troops don't also eat from your stores. Prisoners keep eating from the stores. Heroes always count as unfed.

## 8. Drink / carousing (`Upkeep/TroopUpkeep.cs`)

Hourly, for **every** party sitting in a settlement — garrisons and militia included, since their purse holds the wage their own settlement paid them — per non-hero stack with a purse:

```
dailyWage = wageModel.GetCharacterWage(character) * element.Number
spend     = Round(dailyWage / 24 * troopSettlementFunWageFraction)           // hourly base

surplus = purse - GetSpoilsCap
if surplus > 0:
    cap <= 0:  spend += surplus                                              // nothing to save for: drink the lot
    cap  > 0:  spend += Min(Round(surplus / 24 * troopSettlementFunWageFraction * (purse / cap)),
                            Round(surplus * 0.02))                           // MaxSurplusFunFractionPerHour
spend = Min(spend, Max(1, 25 * (Tier + 1) * Number / 24))                     // MaxFunPerManPerDayPerTier
spend = Min(purse, spend)                                                    // never into debt
```

The surplus bite scales by how many times over its cap the purse stands, but never takes more than 2% of the surplus an hour, and the per-man tier ceiling bounds wage and surplus together. At the `0.25` default the base rate alone is a quarter of a day's wage per day idled. The coin is credited to the settlement's purse (a town's citizen wealth, market fee taken; a castle's or village's treasury) through `TroopMarketFeedback.RegisterServiceSpend`; in a town half of it also buys tavern fare (beer, wine, meat, cheese, fish…) off the market.

---

## 9. The spoils cap — days of keep (`Spoils/SpoilsPool.Cap.cs`)

What a stack counts itself flush against: a configured number of days' worth of its own keep — its
daily wage and its daily field maintenance together. Priced the same for every tier (a veteran's
dearer wage and kit already deepen his days' keep), so there is no separate war chest and top-tier
troops with no upgrade to save for are held to the same rule.

```
dailyWage        = PartyWageModel.GetCharacterWage(char) * stackSize
dailyMaintenance = DailyMaintenanceCost(char, stackSize)                      // §11's per-stack upkeep

GetSpoilsCap = (dailyWage + dailyMaintenance) * troopSpoilsCapDays            // 0 days ⇒ cap 0
```

The cap is a behavioural threshold, not a hard limit: a purse can hold more than its cap (loot and wage both fill past it), but once over, upkeep draws the surplus down — carousing bites harder (§8) and only over-cap stacks splurge on luxuries (§10). Nothing over the cap is minted back to your gold: the surplus is drunk and eaten where the men stand, which credits that settlement's purse rather than yours. A purse reaches gold at one point only, the leader's cut (§12), and never by way of the cap.

`GetPartyPayee` (owner if alive, else `LeaderHero`) lives in this file too — the party-leader spoils cut pays through it.

---

## 10. Luxury splurges (`Upkeep/TroopUpkeep.Luxury.cs`)

Only stacks already over their spoils cap and off cooldown indulge. Per hourly check, in any party sitting in a settlement:

```
if MBRandom.RandomFloat >= troopLuxurySpendChance: skip     // default 0.02
buy one random affordable luxury (ItemCategory.LuxuryDemand > BaseDemand, trade good or equipment, not food)
cost = TroopMarketFeedback.UnitPrice(...); one unit leaves the market, cost drawn from purse
cooldown until NowHours + troopLuxuryCooldownDays * 24       // default 20 days; set only if a purchase landed
```

The good goes to no inventory — it is a keepsake, not party loot.

---

## 11. Field maintenance and the recruit seed (`Spoils/SpoilsPool.Maintenance.cs`)

```
DailyMaintenanceCost(char, n) = Round(troopMaintenanceFraction * GetEquipmentValueWithMount(char) * n)
```

Charged once per clan per day off the clan finance model's apply pass (`MaintenanceFinanceLine`), for every active party in `clan.WarPartyComponents` (caravans, bandit parties, villagers excluded). Per stack:

```
purseFraction = (clan == null || clan.Kingdom == null || clan.IsUnderMercenaryService)
                ? independentMaintenancePurseFraction      // independent or mercenary: default 1
                : 0                                        // sworn vassal or ruler: the liege pays all
fromSpoils = Min(purse, Round(cost * purseFraction))
shortfall  = Σ cost - Σ fromSpoils                          // folded into the clan's daily gold change
```

The spoils leg is simply drained. The shortfall — the leader's gold — is credited to the nearest town not at war with the party (`UpgradeSupply.FindNearestFriendlyTown`, the town it stands in if any; never a castle or village) through `TroopMarketFeedback.RegisterPurchase`, market fee included. **Nothing comes off that town's shelves** — maintenance buys labour, not stock. Garrisons are not war parties: their kit maintenance is billed to the fief (`GarrisonUpkeep`), and militia's inside `MilitiaUpkeep`.

A stack recruited from a village or town arrives with `RecruitSeedValue = DailyMaintenanceCost(char, n) * recruitMaintenanceDays` already in its purse (`OnTroopRecruited` for the AI, `OnUnitRecruited` for the player's recruit screen; prisoners pressed into service bring nothing).

## 12. The leader's cut (`Spoils/SpoilsPool.LeaderCut.cs`)

```
fraction = troopLeaderSpoilsCutFraction * (payee.Clan.Tier + 1)     // clanless = tier 0
         * 1.5                       if the payee's clan is a mercenary under contract
         * (1 + Roguery * 0.003)     the payee's Roguery skill
fraction = Clamp(fraction, 0, 1)
cut      = Round(gathered * fraction), drawn back out of the party's purses pro rata and paid as gold to GetPartyPayee
```

Applied after every gather — battle loot, raid, siege drain, sack, ransom and prisoner strip — on what actually reached the stacks. A party with no non-hero stack still takes its cut of the whole pot (`ApplyLeaderCutSolo`), minted to the leader since there is no purse to draw it from.

## 13. Paid healing (`Upkeep/TroopUpkeep.Healing.cs`)

Hourly, for every party in a settlement (garrisons and militia included), per non-hero stack with wounded and a purse:

```
costPerMan = Max(1, troopSpoilsHealGoldPerTier * Tier)
heal       = Min(wounded, Max(1, Round(wounded * troopSpoilsHealFractionPerHour)), purse / costPerMan)
```

Paid to the settlement through `TroopMarketFeedback.RegisterSurgery`. Skipped while the party (or, for a garrison or militia, its fief) is starving. Runs on top of vanilla's free healing.

## 14. Ransom and left-behind prisoners (`Spoils/SpoilsPool.Ransom.cs`, `SpoilsPool.PrisonerStrip.cs`)

```
ransom / quest delivery:   pot = Σ GetEquipmentValueWithMount(prisoner) * count          // full kit worth, heroes included
left on the loot screen:   pot = Round(Σ GetEquipmentValueWithMount(prisoner) * count * 0.5)   // heroes skipped
```

Split among the captor party's stacks by tier weight, then the leader's cut. Ransom hooks: `SellPrisonersAction.ApplyInternal` (real sales only), `EndCaptivityAction.ApplyByRansom` (a lord ransomed by courier offer or barter; skipped inside a sale and for the player), and the manual-labourers delivery quest's `OnDoneClicked`. Left-behind prisoners: a prefix on `PlayerEncounter.OnPlayerLootMembersAndPrisonerEnd`. The ransom's gold for the man himself is vanilla's, funded by the buying town (`Settlements/RansomFunding.cs`).

---

## Persistence & keys

| Thing | Value |
| --- | --- |
| Purse save key | `RBM_troopSpoilsGold` |
| Fed-state save key | `RBM_troopFedUntilHours` |
| Luxury-cooldown save key | `RBM_troopLuxuryCooldown` |
| Upgrade-budget save keys | `RBM_partyUpgradeCapGold`, `RBM_partyUpgradeCapEnabled` |
| Stack key format | `party.Id + "#" + character.StringId` |
| Item-tier clamp | `Min(Max((int)item.Tier, 0), NumTiers - 1)` |

On party transfers, a leaving detachment carries its `GetCarriedSpoils` share (and its fed-state) to the destination party (`Spoils/SpoilsPool.Transfers.cs`).
