# RBM Economy — Settlements, Production, Food and Wealth

How the settlement economy works: what villages make, how it reaches a town, what a town does with
it, what everything costs, and — in detail — where the money comes from and where it goes.

> For the **money** view specifically — every pool that holds gold, every flow between them, where
> spoils fit, and the full conjured/destroyed list — see
> [`economy-money-flows.md`](economy-money-flows.md). This document is the goods chain.

All of it lives in `RBMCampaign` and is gated behind `rbmCampaignEnabled`. Almost every patch
re-checks that flag at runtime, so toggling the module off mid-session falls back to vanilla rather
than freezing the economy.

Not calibrated in-game. Treat every number as a starting point.

---

## 1. The shape of it

Vanilla's settlement economy is a set of independent controllers, each pulling its own number toward
its own target. Prosperity is pulled by a housing-cost ladder, a town's gold is pulled toward a
multiple of prosperity, food is a running total that consumption decrements and production increments.
Nothing is conserved: money and goods are conjured and destroyed at each controller's discretion.

RBM replaces the controllers with a circuit. Villages make goods out of hearths. Convoys carry them
to a town. The town's households buy them with money that came from somewhere. What the town cannot
buy, its treasury advances for. What the town eats leaves the shelf physically. Prosperity follows
the countryside that feeds it rather than a ladder.

The circuit is not yet closed — §13 names every edge where money is still conjured or destroyed.
The largest by far is soldier spending (§10).

Two ideas carry most of the design:

- **Money is a *purse*, not a level.** Every settlement holds an amount, and moving money means
  debiting one purse and crediting another. §2 is the ledger.
- **Goods are *units*, not gold values.** Storage caps, prices, and the food stock are all measured
  in days of a town's own consumption. §5 and §6.

---

## 2. Where wealth comes from

### 2.1 The two purses

`Settlements/SettlementWealth.cs` splits a settlement's money in two.

| | **Citizen wealth** | **Settlement wealth** |
|---|---|---|
| What it is | the money circulating in the settlement's market — merchants and townsfolk together | the settlement's treasury, the fief as an institution |
| Who holds it | **towns only** (`HasMarket`) | **villages, towns and castles** (`Holds`) |
| Where it lives | **is** `SettlementComponent.Gold` — the vanilla field itself, not a mirror | towns/castles: a new `Dictionary<string,int>` by `StringId`. **Villages: `SettlementComponent.Gold`** |
| Seed (new campaigns) | `10000 + 12 × Prosperity × TownTreasuryScale(40)` — vanilla's old gold target, so a town opens at zero drift (§8.1). Replaces the 20,000 `Town.OnInit` deals, which is booked as `Source.Seed` | town: `TargetProsperity × TreasuryPerProsperity` (**210**); castle: `Prosperity × CastleEconomy.SeedPerProsperity` (**100**); village: `Hearth × TreasuryPerHearth` (**150**), overwriting vanilla's flat 1,000 |
| Persistence | vanilla's own save data | `SyncData` key `RBM_settlementWealth` |

Citizen wealth deliberately *is* vanilla's `Gold` field so that every native reader keeps working
unchanged — the villager sale gate, caravan trades, workshop income, the player's trade screen. A
village holds only the one purse, and it is that same field, so a village needs no mirror at all.

**A castle holds one pool**, its settlement wealth (`Settlements/CastleEconomy.cs`). It has no
market and no citizen purse; anything that would credit a castle's "citizens" — its garrison's
carousing above all — lands in that one pool instead. It earns
`Prosperity × IncomePerProsperityPerDay` (**41**/day, ×1.1/1.2/1.3 with Craftsman Quarters), pays
its own garrison, militia, staff and walls, and remits a tenth a day of anything above
`Prosperity × 200` to its lord (§2.6). There is no other castle tax to the owner.

Both movers clamp at zero and **return what actually moved**. The clamp is the authority: callers
credit the returned figure, never the requested one, so a shortfall cannot conjure the difference.

Town seeds are `Prosperity × 210` because 210 = `0.35 × VanillaProsperityScale(20) × 30 days` —
roughly a month of the fief's turnover. They read the town's countryside target rather than its
stored prosperity, so a save the mod was added to does not seed at twenty times scale. The citizen and
village seeds run on the new-game path only: both live in vanilla's own `Gold` field, so on a loaded
save a seed cannot be told from money earned. A save not created with the campaign module on gets a
one-time warning that its economy was never seeded (`RBM_campaignSeeded`).

`SettlementWealth.Reset()` runs from `RBMSettlementWealthCampaignBehavior`'s **constructor**, the only
hook that runs before the save is read. An absent `SyncData` key leaves the dictionary untouched, so a
null guard would never catch a cross-campaign leak.

### 2.2 The funnel

`Settlements/SettlementGoldFunnel.cs` prefixes `SettlementComponent.ChangeGold`, so *every* native
write to a settlement's gold is routed rather than intercepted case by case. In order:

1. Module off, zero amount, or already inside the funnel → let vanilla run.
2. Suppressed (raised only around `Village.DailyTick`, §3.4) → **drop the write**.
3. `NativeTradeConservation.TryTakeCommission` handled it → skip vanilla.
4. `SettlementWealth.RouteNativeWrite` → village and castle to settlement wealth (a castle pays no
   tariff, and its worldgen gold is left to vanilla), town to citizen wealth, then levy the tariff as
   guarded trade (§2.4). If it declines (a hideout, no component) vanilla runs, so money is never
   destroyed by falling between the two.

The player's shop visit settles as one netted write, so the funnel's levy is deferred for it and
`TradeTariff.PlayerMarketSessionPatch` charges the fee on the visit's gross (bought + sold) instead.

### 2.3 Citizen wealth — sources

| Source | Amount | Conserved? |
|---|---|---|
| **Counter trade** — anyone selling into the town: lords, caravans, the player's trade screen, ransom payments. Routed through the funnel | vanilla's own figure | **Conserved** — comes out of a hero's gold or a party's trade gold |
| **Worldgen seed** | `10000 + 12 × 40 × Prosperity`, new campaigns only (§2.1) | *Conjured*, deliberately |
| **Administrative wages** — the fief pays its staff, who spend locally | `TownDailySalary` = **300**/day | **Conserved** — treasury → market |
| **Construction** — the build reserve pays townsmen and buys clay/planks/tools off the market | half of the wage coin, plus all materials | **Conserved** — treasury (or owner's boost) → reserve → market; the other half of the wage is consumed |
| **Dearth advance** — the fief buys food its market cannot afford (§4.3) | `units × price`, food only | **Conserved** — treasury → market |
| **Militia & garrison maintenance** — kit mended in the town that supplies it | see §2.7, §9 | **Conserved** — men's purses / fief → market |
| **Workshop wages** (§5.4) — named shops only | overhead **250**/day + the salary share of every sale | **Conserved** — shop capital (or owner) → market; closes a vanilla destroy |
| **Minting** — silver ore struck into coin (`Settlements/Minting.cs`) | 85/ore; 10% of stock a day (30% above 100), 10 ore kept | ⚠️ *Converts goods to money* — ore leaves the shelf; ruler 20%, owner 10%, treasury 1%, the rest to citizens |
| **Recruit fee** — what a lord pays to muster a townsman | the recruit price | **Conserved** — lord → citizens (vanilla destroyed it) |
| **Notable wealth** — a notable's surplus over his power band | vanilla's band | **Conserved** — notable → citizens (vanilla destroyed it) |
| **Village shopping** — a village spends its purse above `50 × Hearth`, half the excess per dispatch | finished goods off the town's shelf | **Conserved** — village → town citizens |
| **Soldier spending** — troop goods, carousing, surgery (§10) | see §10 | ⚠️ **Conjured.** Paid from `SpoilsPool`, a parallel currency minted from wages without deducting the payer's gold. The dominant faucet in the economy |

Explicitly *no longer* a source: the townsfolk's own purchases. A townsman paying a merchant is
internal to citizen wealth, so vanilla's `town.ChangeGold(+price)` on every consumption sale is gone.
Only the market fee moves.

### 2.4 Citizen wealth — sinks

| Sink | Amount | Conserved? |
|---|---|---|
| **Counter trade** — the town buying from a caravan, lord or the player | vanilla's figure | **Conserved** |
| **Villager delivery** — paying a convoy for its cargo (§4.3) | `units × price` | **Conserved** → the convoy's trade gold |
| **Trade tariff** — a market fee on every transaction | `TariffRate` = **1%** of trade value, towns only; outsider ("guarded") trade adds the Guard House's +0.3/0.6/1.0 points; the whole rate ×1.1/1.2/1.3 with a Marketplace | **Conserved** → treasury |
| **Wealth tax** — the owner's cut of the market | `DailyRate` = **0.00027** of citizen wealth/day; above the hoard line (`1000 × Prosperity`) **10%** a day of the excess instead. ×1.05/1.10/1.15 with a Tax Office. Towns only | **Conserved**, but *leaves the settlement* — booked to the owner clan and paid on its next finance pass (`RBM_pendingWealthTaxIncome`) |
| **Wealth tax** — the fief's own cut, levied the same day | `SettlementDailyRate` = **0.00014**/day; 10% of the excess while hoarding | **Conserved** → treasury |
| **Caravan repayment** — a hoarding town repays supply-caravan investment it received | up to half of the hoard levy | **Conserved** → its rescuers |
| **Militia** — a town's watch is paid from its citizens first, and armed straight off the town's own shelves (§2.7) | wage + maintenance shortfall; new men's kit | wage and maintenance **conserved** → the men's purses / the mending market. The kit is goods, not coin: it leaves the shelf and no money moves (with the recruit-supply draw off, a citizen-wealth debit stands in for it) |
| **Garrison subsidy** — the last payer of a garrison bill the fief and owner could not cover | only out of citizen wealth above **50,000** | **Conserved** |
| **Ransom** — the market funds prisoner ransoms | prisoner price, clamped; pays the tariff | **Conserved** |

Vanilla's town commission (`SettlementCommissionRateTown` 0.7 — seven tenths of every sale handed to
the owner) is zeroed outright (`TradeTariff.NoTownCommissionPatch`): on a conserved purse it emptied
every market on the map. The 1% tariff is now the only cut a town takes on a trade.

### 2.5 Settlement wealth — sources

| Source | Amount | Conserved? |
|---|---|---|
| **Seed** | town `TargetProsperity × 210`; castle `Prosperity × 100`; village `Hearth × 150` | *Conjured*, new campaigns only |
| **Trade tariff** | 1%+ of every town transaction (§2.4) | **Conserved** from citizen wealth |
| **Wealth tax** | `SettlementDailyRate` = 0.00014 of citizen wealth a day, or 10% of the hoard excess | **Conserved** from citizen wealth |
| **Castle income** | `41 × Prosperity`/day (Craftsman Quarters ×1.1/1.2/1.3), castles only | *Conjured* — the tax on its lands |
| **Prison labour** (`Settlements/PrisonLabour.cs`) | **30**/prisoner/day, towns and castles | *Conjured* |
| **Minting** | 1% of the coin struck, towns | *Converted* from silver ore |
| **Village homecoming** (§4.4) | `VillageShare` = **50%** of the trade tax the convoy's sale generated | **Conserved** — taken off the owner's accrued tax |
| **Village stall trade** | whatever a party buys at the village | **Conserved** |
| **Recruit fee at a village** | the recruit price | **Conserved** (vanilla destroyed it) |
| **Soldier spending at a village or castle** | see §10 | ⚠️ *Conjured* (spoils origin) |

### 2.6 Settlement wealth — sinks

| Sink | Amount | Conserved? |
|---|---|---|
| **Garrison wages** — the fief pays its own garrison's bill | `GarrisonFiefWageShare` = **100%** of the party wage, clamped to the purse; the owner pays the rest (vanilla's budget), then citizens above 50,000 | leaves as spoils wage credit, returns via §10 |
| **Garrison maintenance** | `0.25 ×` a field troop's kit-value maintenance (Castellan's Office cuts the mounted share; Fortifications −5/10%) | **Conserved** → the town that mends the gear |
| **Militia** (§2.7) | wage share + maintenance shortfall + kit for new men | **Conserved** → spoils purses / supplying market |
| **Administrative wages** | town **300**/day → citizens; castle **200**/day and village **100**/day → destroyed | mixed, deliberately |
| **Walls** | `200 ×` wall level/day for a town, `150 ×` for a castle | destroyed (outside tradesmen) |
| **Dearth advance** (§4.3) | food the market could not afford | **Conserved** → citizen wealth |
| **Construction reserve** | `constructionBudgetShare` = **1%** of the treasury a day | **Conserved** → the build reserve, then §2.3 |
| **Castle surplus** | 10%/day of castle wealth above `200 × Prosperity` | **Conserved**, *leaves the settlement* — to the owner clan on its finance pass |
| **Village commission** | the full stall commission, pushed into the owner's tax ledger | **Conserved** (vanilla would have destroyed it) |
| **Village shopping** | half the purse above `50 × Hearth`, per dispatch | **Conserved** → town citizens |
| **Raid / siege** | a raid drains the village purse by the raid's damage share; a besieged castle's treasury (a town's *citizen* wealth) loses 5%/day | half to the raiders' spoils (hard-coded), half destroyed |

The village and castle admin wages are destroyed on purpose: there is no citizen pot to hand them to,
so they leave for the untracked household economy.

### 2.7 Militia are paid, capped, and armed out of a purse

`Settlements/MilitiaUpkeep.cs`. Militia used to accrue a full soldier's wage from nothing. Now the
settlement pays them the way a party leader pays a field troop — a wage plus kit-value maintenance —
out of a **funding pot**: a town's citizen wealth backed by its treasury, a castle's or village's
settlement wealth. There is no owner backstop.

| | Town | Castle | Village |
|---|---:|---:|---:|
| Wage, share of a soldier's wage → the men's spoils purse | **25%** | **10%** | **0** |
| Maintenance, share of a field troop's (men's purse first, pot for the rest) → the mending town | 25% | 25% | 10% |
| Pot must hold before arming a new man | 5× kit | 5× kit | 3× kit, at **10%** of kit value |
| Arming a new man (Barracks −5/10/15% for town and castle) | kit value out of citizen wealth | kit bought off the nearest friendly town | 10% of a kit bought off its trade town |

```
CanKeepMilitia = pot ≥ dailyWageBill × MilitiaPayDaysHeld        MilitiaPayDaysHeld = 20
```

The wage bill is the militia party's `TotalWage × wage share` (Fortifications −5/10%), so a village —
paid nothing — never fails this test. A settlement that cannot keep its men sheds
`MilitiaShedPerDay` (**1**/day, `{=RBM_militia_unpaid}Cannot be paid`); one whose pot cannot arm a new
man raises none that day (`Cannot be armed`). A town arms its new men off its own market's shelves with no
coin moving, and its disbanded men put the cheap end of their kit back on those shelves; a village or castle
buys the kit from a town, and its disbanded men return the kit's value to the pot.

**Caps.** RBM authors the whole daily change (base 2/day for a fortification, 0.5 for a village, plus
`Prosperity/1000` or `Hearth/400`, plus a fast catch-up muster below half the soft cap; no retirement)
against two caps on a manpower base — a town's prosperity, a castle's average bound-village hearth, a
village's own hearth:

- **Soft cap** = **40%** of the base, plus building, daily-project and kingdom-policy bonuses, never
  above 70%.
- **Hard cap** = **75%** of the base, for everyone. Between the two, growth tapers by `(1 − fill)²`;
  growth never crosses the hard cap; a watch standing above it disbands 5% of the overflow a day.

New campaigns open every settlement on the steady state of vanilla's old curve (`intake / 0.025`).

### 2.8 What is still outside the ledger

- **`TradeTaxAccumulated` → owner clan.** A write-only ledger RBM only adds to and, for the village
  share, subtracts from.
- **Workshops.** `Workshop.Capital` is a third purse, and a **named shop's** only. Its trades with the
  town are paired moves, and its overhead and salary land in citizen wealth. The artisans hold no
  capital worth the name and move no gold at all — see §5.4.
- **Clan and hero gold.** Party wages, tax income, tournament prizes — all outside. The owner's
  wealth-tax share and a castle's surplus skim cross into it on the clan's finance pass.
- **A castle's `Gold` field.** Not its purse, and only worldgen's opening gold still lands in it; every
  later native gold write at a castle is routed into the castle's settlement wealth.
- **`SpoilsPool`.** A parallel currency minted from wages and spent into citizen wealth. The largest
  unconserved edge remaining (§10, §13).

---

## 3. Village production

Every village makes a fixed subsistence base set, plus its `VillageType`'s speciality and a light
culture "flavour" on top (`Production/RBMVillageProduction.cs`). Output is linear in raw `Hearth`:

```
daily units of good k = RoundRandomized( rate_k × Hearth × villageProductionMultiplier × roads )
villageProductionMultiplier = 0.5 (config, 0.01–2)      roads = 1 + 0.05 × Roads level of the bound fief
```

### 3.1 The base set

Produced by every village, whatever its type:

| Good | Rate | | Good | Rate |
|---|---:|---|---|---:|
| charcoal | 0.1 | | wool | 0.01 |
| grain | 0.05 | | planks | 0.01 |
| crude iron (`ironIngot1`) | 0.025 | | flax | 0.0085 |
| cheese | 0.019 | | hog | 0.002 |
| butter | 0.015 | | sheep | 0.002 |
| hides | 0.01 | | meat / cow | 0.001 each |
| mule | 0.00025 | | | |

**Sum: ≈0.254 per Hearth per day** (before the multiplier).

Specialities are additive — a cattle farm makes base cheese *plus* cattle cheese. Totals run from
`silk_plant` at 0.008 (cotton) to `lumberjack` at ~2.05 (charcoal + planks); the farms sit at
~0.01–0.29, the mines and lumberjacks an order above. **Flavour** is a trickle of a culture's
signature good on every village of that culture (the village's own culture, never the owner's; the
Empire split north/south/west by its bound fief), at about a tenth of the specialist rate — Battanian
charcoal, Aserai dates and salt, a tenth of a Khuzait horse ranch. Horse ranches are generated from the
culture's mounts rather than tabled:

```
HorseNormalRate = 0.015      {culture}_horse
HorseWarRate    = 0.0020     t2_{culture}_horse
HorseNobleRate  = 0.0005     t3_{culture}_horse
HorsePackBucket = 0.01       split evenly across the ranch's pack animals (desert ranch: three ways, plus camels)
```

so the pack-animal total per ranch is constant however many pack items the culture has. Items that
fail to resolve (missing DLC) or are flagged not-merchandise are dropped when the table is built, and
the resolved map is cached per `VillageType` (and per type + flavour). A village's map icon and
"primary production" read its speciality, not the base set.

Vanilla's separate food-production track is disabled outright — food is part of the base set now.

### 3.2 Warehouse capacity

```
capacity = ceil( max(1, totalRatePerHearth × villageProductionMultiplier × Hearth) × CapacityDays )      CapacityDays = 5
```

Sized off the **total** rate (flavour included, Roads not), because every reader compares it against
the sum of the whole roster, never against one good:

| Reader | Gate |
|---|---|
| production halt | `rosterSum < capacity × 1.5` |
| convoy dispatch (§4.1) | `rosterSum < capacity × 0.5` |
| fishing parties (War Sails) | `rosterSum < capacity` |

The game tracks one shared store, not a per-good allowance, so there is nowhere to express a per-good
cap. The village tooltip says the same: per-good `x /day`, then one `Warehouse  stored / capacity`
line.

### 3.3 Villager party size

Vanilla's `12 + Hearth/[20..40]` band, interpolated on the village's total throughput instead of a
sum over `VillageType.Productions`:

```
QuietRate = 0.17      BusyRate = 0.50
busyness  = clamp( (totalRatePerHearth - 0.17) / (0.50 - 0.17), 0, 1 )
divisor   = lerp(40, 20, busyness)
partySize = MinimumNumberOfVillagersAtVillagerParty + floor(Hearth / divisor)
```

Rate enters per-Hearth, not as an absolute daily figure, because `Hearth` already appears in the size
term, and without `villageProductionMultiplier`. Mines and lumber camps sit far above `BusyRate` and
peg to the largest party.

⚠️ `QuietRate` was pinned to the base set's own sum so a speciality-less village landed exactly on the
quiet end. The base set has since grown to ≈0.254, well past it, so nothing is fully quiet: the floor
is `busyness ≈ 0.25`, `divisor ≈ 34.9`. Re-pin it when the rates are next calibrated.

### 3.4 When a village produces nothing

One gate stops production: `VillageState != Normal` — raided or looted.

`TradeBound == null` is deliberately **not** a gate. Vanilla's null check guards only the
worldgen-seeding branch, protecting the settlement goods are written *into*; the normal branch fills
the village's own store with no such test. A village with no reachable non-hostile town — a castle
village in wartime, for the whole war — keeps producing into its own store.

Vanilla's daily `Gold > 1000 → clamp to 1000` on a village is suppressed at the source
(`Settlements/VillageGoldStock.cs` raises the funnel's suppression around `Village.DailyTick`, since
nothing else there touches gold). A village purse is real money now and must be allowed to accumulate.

A village eats too: its staff take `VillageDailyFood` = **1** unit a day, cheapest food first, out of
the village's own store. Raiders carry off only about half the goods vanilla would hand them (more
for Nords and a Roguery-schooled leader); the rest is lost with the village
(`Spoils/RaidGoodsDestruction.cs`). A won raid also drains the purse (§2.6).

---

## 4. The convoy

### 4.1 Dispatch

```
MaxConvoysPerVillage    = 1                    (as vanilla; the multi-convoy register is kept but idle)
VillagerCarryMultiplier = 8                    inventory capacity of a villager party
hourly dispatch chance  = 0.15                 (vanilla)
dispatch threshold      = 0.5 × warehouseCapacity
hearth cost             = max(0, Hearth - (manCount + 1)/2)       (vanilla)
```

The threshold is applied by an **IL transpiler** that rewrites the result of every
`GetWarehouseCapacity()` call inside `ThinkAboutSendingItemToTown`. If the anchor ever stops matching
it no-ops silently and vanilla's full-warehouse gate stands.

One convoy hauls what two used to: the carry multiplier sizes the weight budget, and loading
(`VillagerLoadSharePatch`) takes the **whole** store, scaling every good down by the same factor when it
will not fit — rather than vanilla's four passes of 20% filled in roster order, which left late-roster
goods behind wholesale. Horses ride on their own legs and never count against the budget.

`Production/VillagerConvoys.cs` still carries the register that let a village keep a second convoy on
the road (the native slot is repointed and `OnFinalize` hands it to a survivor); at a limit of 1 it is a
vanilla passthrough. It rebuilds itself on load from `OnInitialize`, so nothing is saved.

### 4.2 Escort

Guards scale with what the convoy carries, and the richer the load the more of them are veterans:

```
GoldPerEscort  = 400        MaxEscort = 12
desired        = min( floor(cargoValue / 400), 12 )
missing        = desired - existingEscort                (a target, not a per-trip addition)
missing        = min( missing, floor(village.Militia) )  (borrowed from the standing watch)

EliteStartValue = 2000      EliteFullValue = 8000
eliteShare      = clamp( (cargoValue - 2000) / 6000, 0, 1 )
eliteCount      = round( missing × eliteShare )

MeleePerRanged  = 2
rangedCount     = floor(eliteCount / 3) + floor((missing - eliteCount) / 3)     remainder melee
```

The guard is a **loan from the village militia**, not new men: dispatch debits `Settlement.Militia`,
and every guard still standing when the convoy walks back through its own gate is credited back
(`ESCORT`). Hearth pays nothing; a village with no militia sends its goods unescorted, and one whose
escort is ridden down is that many defenders short until it musters replacements. Militia already
aboard count toward `desired`, so repeat trips top the guard back up rather than stacking. The elite
share applies only to guards added now. The melee/ranged split runs once per tier, so total ranged can
fall one short of `floor(missing/3)`. If the culture lacks a troop class the other takes the whole
allocation.

### 4.3 Delivery

`Production/VillagerDelivery.cs`. Only towns buy — villagers bound to a castle sell nothing (vanilla).
The town buys the cargo **food before non-food; then cheapest per unit first, food and non-food alike;
roster order only breaks ties**. The convoy keeps back half a head of its cheapest pack animal per man
(vanilla). Per lot:

```
wanted     = TownStorage.Accept(settlement, item, lotAmount)          (§5.1)
spendable  = food ? citizenWealth : max(0, citizenWealth - reserveFloor)
affordable = min( wanted, floor(spendable / price) )
if affordable == 0 and the lot is food:
    affordable = min( AdvanceForFood(lot), wanted )

reserveFloor = NonFoodReserveShare(0.1) × (10000 + 12 × 40 × Prosperity + troopTradeBonus)    (§8.1's yardstick)
```

`price` is `town.GetItemPrice(..., isSelling: true)` — for a villager party the flat wholesale price,
`1.3 ×` base value less the trade spread, so scarcity does not inflate what the town pays its own
suppliers (§5.2).

Ordering is what decides whether a town is fed. In roster order, velvet at ~26,500/unit and warhorses
drain the purse before food is reached; and among food, meat at 200 or butter at 230 buys the same unit
of stock as grain at 60. The **non-food reserve** keeps a poor market from spending its last gold on
wool and pottery it cannot resell: below a tenth of its yardstick it buys food alone (`DELIVER` reports
what the reserve held back).

**`AdvanceForFood`** is the fief buying grain out of public funds because its market has run out of
money **and its granary has run low**. Food only, by design — a town too poor to buy wool goes without
wool.

```
lowWaterMark = min( 3 days × dailyFoodConsumption, 0.25 × FoodStocksUpperLimit )     DearthDays = 3, DearthStockShare = 0.25
shortfall    = lowWaterMark - foodUnitsInMarket                 (≤ 0 → no advance)
affordable   = min( lotAmount, shortfall, floor(settlementWealth / price) )
moved        = Debit(treasury, affordable × price)
Credit(citizens, moved)
return moved / price                                            units the market can now afford
```

Above the mark a broke market simply turns the cargo away: a treasury buying the townspeople their
groceries would be the lord's purse feeding the town. The gold moves treasury → citizens rather than
paying the villagers directly, so the purchase itself stays an ordinary one and the market ends up
holding the money it needed. Without a second purse the first empty market would be permanent, since
citizen wealth is both the money and the gate on what may be bought — and §8 has switched off the
controller that used to break that loop. Logged as `DEARTH`.

Storage room is checked **before** money, so a full store cannot be talked into a sale by an advance.

Villager parties never pass through `SellItemsAction`, so the market fee is levied by hand on the
total.

### 4.4 Homecoming

The village keeps a share of the trade tax vanilla would have handed entirely to the owner:

```
tax  = the tax vanilla actually charged on this convoy's takings
kept = floor( tax × VillageShare )                    VillageShare = 0.5
```

`Production/VillageHousehold.cs`. `kept` comes off `TradeTaxAccumulated` and goes into the village
purse. Vanilla's village commission rate is 1.0 — the owner takes everything — so this is a real 50%
cut to every lord's village income, the player's included. Taking the share off what was *charged*
rather than off the gross keeps it correct if a policy or perk ever moves the rate.

The convoy itself buys nothing in town; it carries only its takings home. Logged as `HOMECOME`. The
village spends its savings separately: when a convoy sets out with the purse above `50 × Hearth`, the
village buys finished goods at its market town with half the excess
(`Production/VillageShopping.cs`, `VILLAGEBUY`) — the goods leave the town's shelf, the coin lands in its
citizen wealth.

---

## 5. The town's shelf

### 5.1 Storage: goods are not fungible

`Production/TownStorage.cs`. A town holding 1,900 fish and 60 grain read as a full granary under one
undifferentiated cap — no shortage to the prosperity model, none to the siege logic, and a brewery
that could not buy a sack of grain. Each good now gets its own ceiling:

```
daily                = WorkshopDemand.DailyUnits(town, category)       a workshop input, by CATEGORY
                       else CitizenDemand.DailyUnits(town, item)        a basket good (§6.1)
Capacity(town, item) = max(1, ceil( daily × StorageDays ))              StorageDays = 60
Headroom             = max(0, Capacity - held)
                       for food, also ≤ GranaryRoom = FoodStocksUpperLimit − foodUnitsInMarket   (§6.2)
Accept(offered)      = min(offered, Headroom)
```

Food goods share one granary on top of their per-good shelves: the Warehouse-tier cap from §6.2
bounds the *total* food a town will take in. Kingdom caravan dispatch draws every food lot for a
town against that one budget (less food already in flight), so it does not order a granary's worth
of grain, meat and fish each. A food-producing workshop (brewery, press) also stops when the granary
is full, or it would stuff a market that is refusing every villager's grain at the gate.

Two months: long enough to ride out a season, short enough that a market is not an infinite sink.
Nothing is destroyed when a cap binds — the goods stay with whoever brought them, to be carried to a
town with room.

Clothing has no trade-good id, so garments share one wardrobe ceiling across every worn slot;
otherwise each distinct tunic would get its own two months' supply and the cap would never bind.
A raw material no household buys is capped all the same on its workshops' daily draw
(`Production/WorkshopDemand.cs`), counted across its whole category because a recipe takes any member
of it: a forge town holds two months of iron, a town with no smithy holds none. What still has no
measured sink — war gear, horses — is **uncapped**, because a guessed cap would throttle it. (Tools,
clay and hides are on the household basket now, so they are capped like any basket good.)
War gear is instead held to 6 of any one item by the workshops that make it (§5.4).

The clamp is applied at the three inbound doors: native `SellItemsAction`, villager delivery, and
kingdom caravan arrival. Player sales run through `InventoryLogic` and are not clamped. Workshop
output is gated on the production side instead: with `workshopHeadroomGateEnabled` (default on) a
cycle whose every output is already at its ceiling is skipped before any input is consumed (`SHOPCAP`).

### 5.2 Price: days of supply, not gold

`Economy/RBMMarketPrices.cs`. Vanilla's scarcity term is
`(demand / (0.1×supply + 0.04×inStoreValue + 2))^0.6`, where both denominators are *gold values*. Two
consequences: 100 units of a 300-denar good reads as better supplied than 1,000 units of a 20-denar
good, though only the second town can eat for a month; and a good getting dearer makes itself look
more abundant, damping the very signal it carries. A third: vanilla's demand is fed only by
*completed* purchases, so beer nobody could ever buy registered no demand, and no brewery ever saw a
price worth producing for.

RBM swaps that one term for days of the town's own consumption:

```
days     = unitsHeld / max(dailyDraw, MinPricingDaily)     dailyDraw: workshop draw by category, else the basket (§6.1)
cap      = MarkupCap(good)                                  2× basic, 4× medium, 8× luxury (default 8×)
exponent = ln(cap) / ln(AbundantDays / CeilingDays)
factor   = clamp( (AbundantDays / max(days, FloorDays)) ^ exponent, MinFactor, cap )

AbundantDays = 15      CeilingDays = 0.5      FloorDays = 0.1      MinFactor = 1
MinPricingDaily = 2    MaxFactor (luxury) = 8      WholesaleFactor = 1.3
```

| Days of stock | ≥15 | 10 | 5 | 2 | 1 | ≤0.5 |
|---|---|---|---|---|---|---|
| Luxury (8×) — jewelry, spice, velvet, fur, thamaskene | 1.0× | 1.3× | 2.0× | 3.4× | 5.2× | 8.0× |
| Medium (4×) — fish, salt, beer, wine, oil, planks, charcoal, pottery, tools | 1.0× | 1.2× | 1.6× | 2.3× | 3.0× | 4.0× |
| Basic (2×) — grain, meat, cheese, butter, wool, hides, clay, iron, livestock | 1.0× | 1.1× | 1.3× | 1.5× | 1.7× | 2.0× |

The tier is a good's price *elasticity*, not its base value: the exponent is derived from the cap, so
every tier reads 1.0× at a comfortably stocked fifteen days and reaches its own ceiling at half a day.
A staple every region grows cannot spike far however bare the shelf; a status luxury commands whatever
the shortage will bear. An empty shelf is zero days, i.e. the tier's maximum by construction — the
signal exists before the first sale rather than after it. Fifteen days rather than the sixty-day
store: a town holding an ordinary stock pays the base value from §11's historical table, which is a
*floor* price rather than an average.

`MinPricingDaily = 2` floors the *divisor* only. For a good a town draws by the trickle — velvet in a
modest town — one unit read as years of stock, so a party could sell one bolt at the 8× ceiling and buy
it straight back at 1×. Pricing as if the town drew at least two a day closes that loop; storage and
consumption still count the true trickle.

The ceilings are deliberate rather than an open curve. At an effective 17.7× cap, every good with no
stock priced at the ceiling; because vanilla sums *uncapped* item prices into the figure it tests
against the town's purse, town-broke refusals rose from 58 to 878 and became the single largest
blocker of production, workshop cycles fell 79% → 64%, taverns bought nothing at all, and treasuries
poured 3,867 denars/day into dearth advances propping up markets that had been solvent.

**Two sides of the market.** A *village convoy* selling to the town — and a workshop's own output
valuation, which has no trading party — is paid the flat `WholesaleFactor = 1.3 ×` base value: the
countryside is paid for its carting, not for the town's misfortune. Carrying scarcity into that leg
meant a short town paid up to 8× and that money left for the village: 49 of 133 settlements fell
under 10,000 denars, the lowest holding 300 — and at 300, vanilla's own solvency test refuses nearly
every workshop cycle. A *caravan, lord or the player* selling into the town is paid the real
days-of-supply price instead, so a shortage advertises its own reward; such sales are occasional and
self-limiting where a daily convoy is not.

This is a **replacement**, not a ratio: the price is rebuilt as `base value × factor × spread`, where
the spread is the party's own trade margin (Trade skill and perks, from vanilla's `GetTradePenalty` with
no merchant) and every other vanilla term — supply/demand, war markups, caravan and village spreads —
is dropped. A village's trade screen quotes the same price, read off its bound town. Goods outside the
model (war gear, horses, anything with no local sink) keep vanilla's price, except that a buyer never
pays below base value.

**The AI reads the same signal.** With `rbmDaysOfSupplyAiSignal` (default on),
`TownMarketData.GetPriceFactor` — what caravan routing, the trade budget and workshop placement read —
returns the same curve floored at 0.1 instead of 1.0, so a glutted town reads cheap and a bare one
dear.

### 5.3 Two ways stock is withheld

Deliberately disjoint, covering the two different buyers:

- **Food reserve** (`Production/TownFoodReserve.cs`) — `ReserveFraction` = **0.5** of a town's food is
  held back from *outside* buyers: AI parties, armies, caravans. The player is exempt (they trade
  through a different code path). The floor is fixed once per campaign day, the first time an outsider
  tries to buy: a moving floor is not a reserve, since half of a shrinking number is never empty and a
  run of buyers would nibble the stock toward zero. Deliveries arriving later that day are freely
  buyable; tomorrow the floor re-anchors. Townsfolk, garrison and militia still eat from the whole
  stock — that is not a market sale.
- **Hidden stock** (`Economy/HiddenMarketStock.cs`) — `HiddenFraction` = **0.5** of every stack in a
  town's or village's market is hidden from the *player's trade screen only*, so a fief never shows
  everything it holds at once. The screen is handed a shadow roster; drags apply to it live, its
  in-screen Reset reverts to the halved view, and on a confirmed trade the delta is applied to the real
  roster. Cancel touches nothing. Integer truncation means a stack of one shows whole. The campaign side
  — prices, rations, caravans — always reads the full roster, but the price quoted mid-visit folds in
  what the player has already moved, so it slides with each unit as vanilla's does.

### 5.4 The workshops turn the shelf over

Every town holds four workshop slots (vanilla's `DefaultWorkshopCountInSettlement`): the hidden
`artisans` shop in slot 0 and three **named** businesses — brewery, wine press, weavery and the rest.
They are the step between §3's villages and §6's households: grain becomes beer, clay becomes pottery,
iron becomes mail, and nearly every good the `DEMAND` line reports as unmet went unmet because a bench
did not run. RBM owns the workshop rules outright (`Workshops/`): the constants as a decorating game
model (`RBMWorkshopModel`), the run-or-not gate (`RBMWorkshopCycle`), the money and goods of a cycle
(`RBMWorkshopSettlement`) and the daily bill (`RBMWorkshopExpense`).

The two kinds are economically different things, and RBM treats them differently.

**Named shops have an owner.** Their capital is his, the hands who work them are not him, and a wage
between the two is a real transfer. A shop is founded with **60,000** capital (vanilla 10,000); below
30,000 (`CapitalLowLimit`) its overhead is billed to a player owner's gold.

| Leg | Amount | Where it goes |
|---|---|---|
| **Overhead** — `DailyExpense` | flat **250**/day (vanilla 100), run or idle | citizen wealth, as `WorkshopWages`. Vanilla destroyed it |
| **Salary** — a share of every sale | `55% − 5% per 48,000 of equipment cost`, floor 10% (pottery 50%, brewery 40%, smithy 30%); nothing while capital is at or under **40,000** | citizen wealth, untaxed |

The town pays a shop for each finished item at the sell-side price, capped at **10%** of the town's
gold (floor 500) — one valuation shared by the gate and the payment, so a cycle is judged on what the
town will actually hand over. Inputs are paid for at the market price for the whole draw, not one unit.
The gate runs, in order: storage glut (§5.1); a **15%** margin over materials on what the shop keeps
after the salary; the shop's capital covering its inputs; the town's gold covering the payout. The
salary comes off each sale, so a good production day can never bankrupt a shop; the overhead follows
vanilla's ladder (capital, then a player owner's gold, then bankruptcy).

**Speed** scales with the town (`Production/ArtisanOutput.cs`): each named shop works at
`Prosperity × 0.2 / activeRecipes`, and the artisans at
`Prosperity × (1 − 0.2 × namedShops) / activeRecipes`, both × `workshopProductionMultiplier` (default 1).
"Active" is a recipe whose inputs the market can currently cover.

**War gear.** A weapon, shield or armour recipe outputs a tier bucket that holds only a handful of a
culture's items, so the same two or three flooded every market. `Production/WorkshopTroopOrders.cs`
draws **80%** of equipment units (50% of garments) from the **town culture's troop kits** — militia,
troop trees and mercenaries weighted 50/40/10, each item by how many of those troops carry it — and the
rest from the open market, and skips any item the market already holds **6** of. When nothing in the
category is under the cap, the unit is not made and the cycle's income shrinks with it (`SHOPGEAR`).

**The artisans have no owner.** They are the townspeople themselves — the butcher jointing the cow,
the smith at his tier-1 blades — and they move **no gold at all**:

- Materials come off the shelf **unpaid for**. The goods are the townspeople's already.
- Finished goods go back on the shelf **unpaid for**, and untaxed.
- **No wage.** A man working his own stock does not pay himself.
- The one exception: the **market fee** on the materials drawn, `TariffRate` = **1%**, citizen wealth
  → town treasury. The stall is the town's even when the goods are not. Levied on the whole draw, not
  the single unit vanilla prices — RBM recipes take up to twenty ingots at a time.

Their *capacity* still scales with the town (the speed formula above): prosperity, less the fifth each
named shop takes, divided by the number of recipes the town can currently supply — a labour pool
rather than a bonus. Adding a recipe never adds capacity, it re-slices it. The artisans' recipes are
RBM's (`RBMXML/RBMEconomy_workshops_artisans.xml`) and include the household goods — garments,
pottery, charcoal, beer, even velvet from cotton.

This went the long way round, and the history is worth keeping because the failure was invisible
without the log. The bench was first made to trade for real — citizens paying it for output, it
paying a wage back — reasoning that a purse which neither takes nor pays is a trade that never
reaches the man who did the work. Fourteen logged days said that circuit was almost entirely
self-cancelling: the wage credit is the output debit coming home, and the whole apparatus resolved to
the market fee plus whatever the shop's working float was doing that day.

The float was the real damage. Sized against the bench (three days of materials) rather than against
the town, it is an absolute number of denars in a range of citizen wealth spanning 11,000 to 3.8
million. Measured: **Balgard's artisans held 123% of everything its townspeople had between them**,
Hvalvik's 109%, against a median of 7%. And because citizen wealth then *gated* production — the
people had to afford the output before the bench could run — the float was holding the very money
that would have bought the goods it was waiting to sell. Those towns locked, and they were exactly
the ones with the worst measured output (`corr(float share, wage per prosperity point) = −0.46`).

So the circuit is gone. The gate went with it and is deliberately not replaced: the artisans skip the
shop-solvency and town-cash tests and keep only vanilla's bare "output worth more than inputs" margin.

Implementation is one predicate, `RBMWorkshopCycle.SettlesInGold`: vanilla's own `effectCapital`
("this recipe settles in gold") **and** not the hidden shop. Vanilla moves the *items* either way; only
the gold pair sits behind it, so the bench works and only the denars stop. It must be forced rather
than left alone, because vanilla *sets* `effectCapital` for all-trade-good recipes, which is the
artisans' commonest work.

⚠️ The measurements above all predate the change. The first log run under gold-free artisans is the
first real reading on any of it.

---

## 6. What a town buys and eats

### 6.1 The household basket

`Production/CitizenDemand.cs`. Vanilla has no shopping list: each item category gets a gold budget
from prosperity and the consumption pass spends it against whatever is on the shelf, so a town's diet
is decided by its suppliers rather than its appetite — a town holding only fish eats fish forever and
calls itself fed. A gold budget also means a town facing a fuel shortage buys *less* fuel as the price
climbs, when a shortage should mean the same fuel costs more.

Households now buy **quantities**, per unit of Prosperity per day.

**Food mix** — shares of the day's ration, summing to 1.000:

| grain | beer | meat | cheese | butter | fish | wine | date fruit | oil |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0.518 | 0.176 | 0.0775 | 0.063 | 0.06 | 0.0575 | 0.02 | 0.018 | 0.01 |

Grain half and beer a sixth is the medieval diet; at that volume beer is food, not drink. Wine and oil
are not food-store goods but count as ration filled — the alternative is a town that buys its oil and
still reads 3% starving. They correctly do not show in the granary: a barrel of wine is not a siege
reserve.

**Staples** — units per Prosperity per day: charcoal **0.6**, salt **0.24**, whale oil **0.05**
(War Sails; inert without it), clay **0.01**, cheese **0.01**, butter **0.01**, tools **0.005**,
planks **0.004**, hides **0.003**. Charcoal is the largest physical flow in the economy, larger than
the town's food, and that is not an error — heating and cooking burn more by weight than a household
eats. Every village now makes a little (§3.1), lumberjacks and iron mines a lot, and the artisans have
charcoal recipes; it still reads as a chronic shortfall.

**Luxuries** are gated on savings, measured per household:

```
savings = ( citizenWealth / Prosperity ) / IncomePerProsperity        IncomePerProsperity = 127.4
```

| Tier | Threshold | Goods (units/Prosperity/day) |
|---|---|---|
| Small | 5 days of income | beer 0.1; date_fruit, wine, oil, olives, cheese, butter 0.01 each; clay 0.004; meat 0.003; jewelry 0.002; tools 0.0007 |
| Medium | 9 days | date_fruit, oil, wine 0.01 each; jewelry 0.0053; clay 0.004; pottery, walrus_tusk 0.003; felt, planks, meat 0.002; fur 0.0015; tools 0.0007; velvet 0.0005; **+0.05 garments** |
| Large | 18 days | jewelry 0.01; date_fruit, oil, wine 0.01 each; pottery 0.006; meat 0.005; planks, walrus_tusk 0.003; fur 0.0015; velvet 0.001 |

Tiers are cumulative, so a town at the large tier buys ≈0.017 jewelry. Medium and large were lowered
from 15 and 30 days, which almost no town ever reached. Garments run at `StapleGarments` **0.1**/
Prosperity/day as a necessity, plus `MediumLuxuryGarments` **0.05** at the medium tier; they are bought
cheapest-first, because a household replacing a worn tunic buys a tunic — unsorted, towns quietly
consumed the merchants' finest stock at forty pieces a day.

Savings are expressed per household on purpose. A man buys velvet when *he* has eighteen days'
earnings behind him, not when his city does. The tiers are therefore blind to town size: a small prosperous
town reaches the large tier on far less absolute wealth than a big poor one, which is correct, because
it is a statement about comfort rather than size. `IncomePerProsperity` is never debited — earnings and
spending are the same pot under §2 — it exists only to size the thresholds.

**Nothing is conjured to meet demand.** A basket good the chain does not supply — because no village
or workshop near the town makes it, or it never reaches the shelf — simply goes unfilled and the money
goes unspent. The `DEMAND` log names every shortfall. Meeting demand also feeds prosperity: a town
grows faster the more of its medium and large luxury demand is actually met (§7).

`ModelledGoods` — the union of these tables — is the boundary of what RBM claims to understand about a
town's appetite, and three systems are drawn along it: `TownStorage` caps these, `RBMMarketPrices`
prices these, `DEMAND` reports these. The one extension is the industrial side — raw materials a
town's own workshops draw (`WorkshopDemand`), capped and priced by category. Everything else stays on
vanilla's gold budget, which still runs afterwards for items the basket does not cover.

### 6.2 Food stocks

A town's `FoodStocks` is the food physically in its market roster:

```
FoodStocks           = min( foodUnitsInMarket, FoodStocksUpperLimit )
FoodStocksUpperLimit = max( 300, FoodStockDays × dailyFoodConsumption )
FoodStockDays        = 30 + 10 × WarehouseOrGranaryTier          // 30/40/50/60
dailyFoodConsumption = citizens + garrison + militia + prisoners  // GetFoodConsumption(town).Total
```

The granary is measured in *days of the fief's own eating* rather than in units, and the Warehouse
(or a castle's Granary) is the only building that moves it. Vanilla's flat +100/300/500 (castle
Granary +100/200/300) is discarded. Towns and castles use the same ladder; castles keep vanilla's
stored running total and are clamped to the new limit by vanilla's own daily tick.

**Castles stay on vanilla's modelled food**, corrected in three places: the ×10 appetite of the
prosperity divisor (§6.3) is added back as `Castle self-sufficiency`, so a castle eats at vanilla's
divisor of 40; its Farmlands' flat 6/12/18 becomes +10/20/30% of its own countryside production; and
its prisoners eat (§6.3).

For towns the limit is also the intake ceiling (§5.1): once the market holds this much food, villager
convoys, kingdom caravans and native trade are refused, and food workshops idle. A full Warehouse
lands exactly on `StorageDays = 60`, so a tier-3 town holds what its per-good shelves always could
and every lower tier is tighter. The clamp on the *reported* stock stays because the market can still
exceed the granary — the player sells past it, and modelled production (Farmlands, hunting) is
deliberately not gated — and the reported figure is what the siege AI and `FiefStarvation` count.
The vanilla prosperity bonus for stock over the limit is unreachable; `RBMProsperityEquilibrium`
replaces that model outright.

`FoodChange` is measured, not modelled:

```
measuredChange = closingFoodUnits - previousTickFoodUnits - unmetRations
```

split for the tooltip into `change + unmet` ("Market food") and `-unmet` ("Unmet rations"). Subtracting
unmet is what makes a famine read as a deficit: an empty market leaves the roster unmoved, which is
otherwise indistinguishable from perfect balance.

The unit count is memoised against `ItemRoster.VersionNo`, since `FoodStocks` is read constantly.

### 6.3 Rations, and who pays for them

```
households = Prosperity / NumberOfProsperityToEatOneFood          divisor 4, not vanilla's 40
men        = garrisonMembers + militia
soldiers   = men / NumberOfMenOnGarrisonToEatOneFood              vanilla's 20, unchanged
prisoners  = prisonersInCells × 0.05                              PrisonLabour.FoodPerPrisonerPerDay
```

The divisor of 4 (`RBMVillageProduction.ProsperityToEatOneFoodPatch`) means a town eats **10× more
food per point of prosperity** than vanilla — the single change most of this document follows from.
Militia are charged for; vanilla never fed them. Prisoners eat gruel and work it off (§2.5).

Perk order mirrors vanilla term for term: under siege `Steward.Gourmet` on soldiers and
`Medicine.TriageTent` on rations; always `Steward.MasterOfWarcraft` on households; then the
`FoodConsumption` building effect on the total; then `RoundRandomized`. A flat
`AdministrativeUpkeep.TownDailyFood` = **3** and the prisoners' ration are added after, so a town with
no prosperity and no garrison still feeds its staff.

The day's units are then **split by who eats**, on the pre-building shares so the total ration is
unchanged by splitting it:

```
soldierUnits     = RoundRandomized( units × soldiers / (households + soldiers) )      clamped ≤ units
civilianUnits    = units - soldierUnits
provisionedUnits = soldierUnits + 3 + prisonerUnits
```

- **Civilians pay nobody.** Townsman and merchant are both inside citizen wealth. Rations are shaped
  by the §6.1 food mix first; whatever the mix could not fill falls back to a cheapest-first buy, so
  the *number* of rations a town gets is exactly what it was and starvation, prosperity and loyalty do
  not move. Without that fallback a town with no brewery would run permanently 17.6% hungry over a
  preference. Only the market fee moves money.
- **Soldiers, staff and prisoners are provisioned free.** Feeding its defenders is the duty of holding
  the place: the food leaves the market (and registers demand), but no money moves and nobody is
  credited. A bigger garrison costs the town stock, not gold.

Outside the civilians' food mix, rations are taken cheapest-first — the civilian fallback and the
provisioned leg alike. A ration is a ration, so eating 200-denar meat while 60-denar grain sits on the
shelf buys the town nothing and costs it the difference.

### 6.4 The non-market food sources

Everything vanilla added straight onto the food total arrives as goods on the shelf instead, paid out
each day before rations are eaten:

| Source | Delivered as |
|---|---|
| `FoodProduction` building effect, not under siege | grain, at the effect's amount (0 for vanilla town content — castle Farmlands only) |
| `HuntingRights` policy | 2 meat/day |
| `Roguery.DirtyFighting`, under siege only | 2 units of a random good from a 9-item smuggled-food list |

### 6.5 Starvation

Two halves, both keyed on *unmet rations* rather than an empty market — a town with 10 grain and 700
mouths reads starving a day before its market empties.

- **The flag.** `RemainingFoodPercentage = -100`, which re-raises `IsStarving`; a market-backed stock
  never goes negative, so vanilla can no longer raise it itself.
- **The clock**, which is the half that bites. The loyalty penalty fires on `DaysStarving > 14`, and
  that is measured from a timestamp `Town.DailyTick` re-stamps whenever `FoodStocks > 0`. Under a
  market-backed stock a partial famine would reset its own clock every day for as long as a single
  grain sat unsold, and never reach fourteen. RBM captures the stamp in a prefix and restores it in
  the postfix on any day rations went unmet, so the clock runs from the last day the town actually fed
  everyone.

### 6.6 Famine

`Settlements/FiefStarvation.cs`. A fief's food is read as **days of supply** — `FoodStocks` over
`GetFoodConsumption(town).Total` — and drives three tiers, towns **and** castles:

| Tier | When | Effect |
|---|---|---|
| Rationing | under **7** days | prosperity stops growing, the garrison stops recruiting |
| Critical | under **3** days | prosperity also falls **1%**/day |
| Starving | rations went unmet today (a castle: granary empty) | prosperity falls **3%**/day; a tenth of the healthy garrison and militia are wounded and a tenth of the wounded die, each day |

The last replaces vanilla's starving-garrison wounding rather than stacking on it. Field parties out of
food keep vanilla's wounding and add the deaths (a tenth of the wounded a day). The prosperity loss is
the `Hunger` line of the prosperity model (§7); vanilla's famine term no longer exists there, since
that model is replaced outright. Logged as `STARVE`.

### 6.7 Demand feedback

Every purchase — rations (provisioned ones included), the household basket, the residual vanilla
budget, soldier spending — feeds its gold value back as market demand through one shared call:

```
DemandFromPurchaseFactor = 1.0
added = purchaseValue × DemandFromPurchaseFactor / VanillaProsperityScale        (= /20, see §8)
```

The division is a units conversion, not a dial: demand lives on the ×20 pool scale while the purchase
is in real denars. The 0.15 factors cancel rather than compound — `AddDemand` scales its input by 0.15
and the pool decays 15%/day, so a sustained addition `F` against equilibrium `E` solves
`D = 0.85D + 0.15E + 0.15F → D = E + F`. The addition lands at face value, once converted.

---

## 7. Prosperity follows the countryside

`Economy/RBMProsperityEquilibrium.cs`. Town prosperity is pulled toward a share of the hearths of the
villages that trade with it, rather than by vanilla's housing-cost ladder. **The vanilla prosperity
model no longer runs at all**: a prefix supplies the whole result, so vanilla's perk, building,
loyalty, policy and food terms — tuned for prosperity in the thousands — are gone, and only two
forces remain, the countryside pull and hunger.

```
ProsperityPerBoundHearth = 0.1
target = 0.1 × Σ Hearth over trade-bound villages × InfrastructureMultiplier
InfrastructureMultiplier = min(2, 1 + 0.02 × Σ building levels × (1 + 0.1 × Waterworks level))     daily projects excluded
gap    = target - Prosperity

below target:  delta = gap × 0.02 × foodGate × demand          ProsperityGrowthRate = 0.02 (≈ 50-day time constant)
    foodGate = 0 while rationing (§6.6), else today's ration satisfaction (0–1)
    demand   = baseSatisfaction × (1 + 0.25 × mediumLuxurySatisfaction + 0.5 × largeLuxurySatisfaction)
above target:  delta = gap × 0.08 × 0.05                        ≈ 250-day drift down while fed
always:        − Prosperity × hungerRate                        1%/day critical, 3%/day starving (§6.6)
```

A town grows only while it is fed, and faster the more of its people's wants its market meets; a town
standing above its countryside barely moves down while the food holds, and sheds people fast only once
it starves. Infrastructure raises the ceiling — a well-built town houses more people on the same
hearths — up to double.

New games seed every town at `target` directly. Loaded saves converge on their own. Vanilla's one-off
prosperity writes outside the daily model (governor perks, quest and issue rewards, incidents) are
divided by `DiscreteWriteScale` = **40** before they land, so a +100 on vanilla's scale is a nudge, not
an event; RBM's own seeding, the daily tick and the siege-aftermath penalty are exempt.

**Castles** follow their own countryside: `1.5 ×` the *average* hearth of their administratively bound
villages, closing 5% of the gap a day (growth blocked while rationing, and the same hunger drain). That
lands them at roughly 400–900 — the band the world authors castles at, still vanilla's scale — so new
games seed castles at that figure too.

Trade-bound hearths are recomputed once per campaign day by walking every village's `TradeBound`
rather than reading the town's cached list, which is emptied on load and only repopulated for castle
villages.

---

## 8. Scaling the vanilla economic models

Prosperity now sits on a *household* scale, roughly 1/20 of what vanilla's economy models expect. Two
factors reconcile them:

```
VanillaProsperityScale = 20      (demand)
TownTreasuryScale      = 40      (the treasury yardstick)
```

**Towns only**, on every leg (`Economy/RBMMarketLiquidity.cs`). A castle's countryside equilibrium
(§7) keeps it on vanilla-scale prosperity, so it must keep vanilla's models too — scaling a castle
would target a ~490k treasury on prosperity 1000 and price its goods around 6× a town's, which is a
buy-in-town/sell-in-castle gold printer.

### 8.1 The top-up controller is switched off

Vanilla pulls a town's gold toward `10000 + 12 × prosperity`, a quarter of the gap per day, symmetric —
conjuring money when the town is poor and destroying it when rich. That is the last controller in the
chain, and it is now **dead**: the model returns 0.

What remains is a yardstick. The patch still computes the target vanilla would have used and logs the
drift:

```
countryside = Prosperity × 40 × 12
target      = 10000 + countryside + troopTradeBonus                (§10)
drift       = citizenWealth - target
```

`LIQUID` therefore measures how far real trade has carried a town's market from the figure vanilla
would have pinned it to — the hole a conserved economy has to fill by other means, and the instrument
for telling whether §2's circuit is actually closing. The same yardstick seeds a new town's citizen
wealth (§2.1) and sizes the villager-delivery non-food reserve (§4.3).

### 8.2 Demand

```
p        = 20 × Prosperity
baseline = max(0, p + extraProsperity)
luxury   = max(0, p - 3000)
demand   = (BaseDemand < 1e-8) ? baseline × 0.01
                               : BaseDemand × baseline + LuxuryDemand × luxury
```

`extraProsperity` (the 1000 nudge) and the 3000 luxury threshold are deliberately not scaled.

### 8.3 And un-scaled again for prices

`ItemData.Demand` does double duty: a gold pool *and* the numerator of vanilla's price factor, which is
compared against unscaled physical counts. Feeding the ×20 pool in would raise every price by
`20^0.6 ≈ 6×`, so the estimate path is divided back down by 20. The two paths are separable because
each has exactly one caller. Deriving by division rather than rewriting against raw prosperity keeps
the 1000 nudge and the 3000 threshold at the same *relative* size. (For the goods §5.2 models, the
retail price no longer reads vanilla's factor at all; this path still prices everything else and feeds
the AI factor of unmodelled categories.)

The three gates are one decision: §8.3 divides by the same scale §8.2 multiplies by, so gating one
without the other would collapse castle prices by 20× instead of inflating them 6×. Putting castles on
the household scale means lifting all three together.

A poisoned (NaN) supply/demand EMA is reset before vanilla's daily blend, since one bad day would
otherwise stick in the save for good.

---

## 9. Fief finance

The fief's treasury is funded and drained by a small set of named flows, all tabled in §2:

| In | Rate |
|---|---|
| Trade tariff on every market transaction (towns) | 1%, + Guard House on outsider trade, × Marketplace |
| Wealth tax, the fief's own share (towns) | 0.014% of citizen wealth/day; 10% of the excess above `1000 × Prosperity` |
| Castle income | `41 × Prosperity`/day |
| Prison labour | 30/prisoner/day |
| Minting (towns) | 1% of the coin struck |
| Village homecoming share (villages) | 50% of the convoy's trade tax |

| Out | Rate |
|---|---|
| Garrison wage | 100% of the garrison party's wage, owner pays what the purse cannot |
| Garrison maintenance | 0.25 × field kit maintenance |
| Militia | town 25% / castle 10% / village 0 of a wage, plus maintenance shortfall and new men's kit (§2.7); a town draws its citizens first |
| Administrative salary | 300/day town, 200/day castle, 100/day village |
| Walls | 200 (town) / 150 (castle) × wall level per day |
| Construction reserve | 1% of the treasury/day |
| Dearth advances | as needed, food only, below the granary's low-water mark |
| Castle surplus to the lord | 10%/day of wealth above `200 × Prosperity` |

The day runs in that order — income first (castle income, minting, prison labour), then upkeep
(admin, walls, garrison maintenance, militia arming), then garrison growth out of what is left
(`Settlements/GarrisonRecruitCost.cs`), the wealth tax, and construction last.

The garrison wage is taken in `DefaultClanFinanceModel.CalculatePartyWage`, so the whole chain — purse
drain, clan top-up, and the per-fief garrison line in the clan finance screen — follows from one
number. The fief pays first; the owner's budget covers what it cannot; a town's citizens cover any
remainder out of their wealth above 50,000 (`GarrisonSubsidy`). The budget vanilla judges morale
against is widened by what the fief and citizens can pay, so the men are not docked morale for a
broke lord. It is only withdrawn on the applying pass, but read on both, so the projection matches the
charge.

The map tooltip shows both purses. It is not a Harmony patch: the game captures the settlement tooltip
refresher as a delegate once at load, so a later patch never routes through it and an earlier one
crashes the map. The tooltip is re-registered with a wrapper that chains the existing refresher.

---

## 10. Soldiers as customers

Troop spoils spending buys off the very roster §6.2 counts as `FoodStocks`, so an army physically eats
a town toward famine.

**Price.** A stack pays the market price, not a flat item value, so scarcity is visible to troops: a
famine-priced town puts its last grain above a recruit's ceiling and the army goes hungry rather than
finishing the stocks off — self-limiting exactly when the town can least afford the custom. Villages
and castles keep the flat value, since a castle prices on the vanilla scale (§8). The food stalls are
priced once per party per buying pass, so a party stripping the shelves pays the opening price for the
shortage it causes. A stack buys `troopSettlementFoodDays` (**20**) days of food at a time and buys no
more until it has eaten it.

**Where the coin lands** (`Upkeep/TroopMarketFeedback.cs`): a town's citizen wealth, paying the tariff;
a castle's or village's single purse. Only a town also takes the demand and tally legs.

**Carousing** spends `troopSettlementFunWageFraction` (**0.25**) of a day's wage per day in a
settlement, plus a bite of any purse above its cap. That bite is bounded twice:
`MaxSurplusFunFractionPerHour` = **0.02** of the surplus per hour, and a per-man ceiling of
`MaxFunPerManPerDayPerTier` = **25** × (Tier + 1) gold per day, applied as a per-stack hourly clamp on
the whole spend. In a town, half of it (`CarousingGoodsShare` = **0.5**) leaves the shelf as physical
tavern fare — beer 38%, wine 18%, meat 18%, cheese 11%, fish 8%, grapes 7% — for which no additional
money moves.

**Who spends.** Food buying is visitors-only — a garrison and militia are fed by the town (§6.3).
Carousing, luxuries and paid healing are not: garrisons and militia spend in their own settlement,
because their coin is the fief's own money coming back (§9's garrison wage and the militia wage),
which makes it a loop rather than an invention.

**The tally.** A decaying per-town record of what soldiers have spent, `TallyDecayPerDay` = **0.9**
(half-life ~1 week), capped at `MaxGarrisonTradeShare` = **0.5** of the countryside term and scaled by
**0.25**. Persisted under `SyncData` key `RBM_townTroopTrade`, reset in the owning behavior's
constructor. It feeds only the §8.1 yardstick and the delivery reserve sized off it (§4.3) — with the
controller off it moves no money directly.

⚠️ **This is the economy's largest faucet.** Spoils are minted from wages without deducting the payer's
gold, so every denar of troop goods, carousing and surgery is new money entering citizen wealth.
Soldier spending brings a town roughly **nine times** what deliveries and the wealth tax take out of
it, and with the top-up controller off nothing absorbs it.

Caravans are not exempt: their guards read as soldiers to every gate here, so a caravan provisions and
carouses like a war party and pays the town for it. Villagers are exempt (`SpoilsPool.IsExemptParty`):
they hold no purse at all.

---

## 11. Trade good values

Trade goods are valued and weighted off historical figures: a period price in denars ×10, and the real
mass in kilograms of one trade lot. Value and weight move together, so a cart of velvet is not worth
what a cart of hardwood is. These are *floor* prices — §5.2 marks up from here.

Applied at both good-creation sites (XML goods and the code-built grain/meat/iron chain), before item
category averages, initial town stock seeding, and trade AI read them. Unconditional whenever
`rbmCampaignEnabled` is on — there is no separate toggle.
Items outside the table — stolen goods, trash, all non-Goods — are untouched. (Tools joined the table:
left at vanilla's 250 among repriced metal, they were a margin out of nothing.)

`Economy/TradeGoodValues.cs` (and `NavalTradeGoodValues.cs` for the War Sails goods):

| Good | Value | Weight (kg) | | Good | Value | Weight (kg) |
|---|---:|---:|---|---|---:|---:|
| grain | 60 | 30 | | wool | 160 | 2 |
| meat | 200 | 30 | | silver | 43 | 0.85 |
| fish | 125 | 20 | | jewelry | 420 | 0.025 |
| cheese | 166 | 15 | | salt | 30 | 1 |
| butter | 230 | 8.4 | | spice | 13 | 1 |
| grape | 275 | 89 | | cotton | 1925 | 1 |
| date fruit | 333 | 20 | | flax | 34 | 1 |
| olives | 45 | 46 | | clay | 130 | 10 |
| beer | 180 | 72 | | pottery | 900 | 10 |
| wine | 1330 | 85 | | linen | 170 | 0.76 |
| oil | 270 | 6.23 | | leather | 176 | 0.8 |
| hides | 88 | 0.8 | | velvet | 26500 | 0.5 |
| planks | 10 | 20 | | fur | 833 | 0.75 |
| hardwood | 10 | 1 | | felt | 250 | 1 |
| charcoal | 3 | 4 | | iron ore | 1 | 4 |
| tools | 48 | 1 | | hog | 130 | 60 |
| sheep | 220 | 60 | | cow | 480 | 175 |
| walrus tusk | 360 | 5 | | whale oil | 36 | 12.6 |

`clay` stands for ordinary pottery and `pottery` for fine majolica. Iron ingot ladder: crude 4 /
wrought 11 / iron 22 / steel 40 / fine steel 69 / thamaskene 180, weights 2, 1, 1, 1, 1, 1.

Weight spans four orders of magnitude (0.025 → 175 kg) and decides what a party can profitably carry,
which is why the inventory screen carries a weight column: a 52 px `Wt.` column carved out of the item
name field, injected at module load because Gauntlet caches parsed prefabs before the campaign starts.
Requires a restart to take effect.

---

## 12. Diagnostics

The economy log writes to `logs/economy/` next to the config, one file per session, capped by
retention. It opens lazily, so the toggle can be flipped mid-session, and every logging path is
short-circuited when it is off — production runs for every village on the map every day.

| Category | What it answers |
|---|---|
| `PRODUCE` | what each village made, good by good |
| `DISPATCH` | every convoy that set out: escort, roster, cargo manifest with values and weight |
| `ESCORT` | militia returned to the village when its convoy came home |
| `DELIVER` | what the town bought off a convoy, what went unsold, and what the non-food reserve held back |
| `HOMECOME` | the village's cut of its convoy's trade tax |
| `VILLAGEBUY` | a village spending its purse surplus at its market town |
| `DEARTH` | the fief advancing for food its market could not afford (§4.3) |
| `FOOD` | a town's rations: eaten, delivered, unmet, stock against limit |
| `STARVE` | a starving fief's garrison and militia wounded and dead (§6.6) |
| `DEMAND` | what the household basket wanted and could not get, by name |
| `TAVERN` | the drink budget against what the shelf could supply |
| `STORE` | goods turned away for want of storage room |
| `PRICE` | days of supply and the resulting multiplier, per modelled good |
| `INPUT` | the workshop raw materials, by category: units, daily draw, days of cover, markup/signal, supply against demand |
| `SHOPS` | workshop capital movement, per town and then per shop |
| `SHOPWAGE` | the named shops' salary: denars over batches. Artisans do not appear — they pay none (§5.4) |
| `SHOPBLOCK` | cycles refused, counted **by reason** — `no-input:<good>`, `margin:<output>`, `shop-broke`, `town-broke`. The first question to ask of a town that makes nothing |
| `SHOPCAP` | cycles skipped on purpose because every output was at its storage ceiling (§5.1) |
| `SHOPIDLE` | artisan recipes that were due to run and made nothing all day, named with the input that stopped them |
| `SHOPSCALE` | the artisans' labour pool: prosperity, recipes active of total, speed multiplier, cycles due against declared (§5.4) |
| `SHOPGEAR` / `GEARBOOK` | war gear made per source (militia, troops, mercenaries, open market), units skipped at the 6-per-item cap; and each culture's troop-kit pools |
| `TARIFF` | the market fee taken, against the trade value it was taken on |
| `WEALTHTAX` | what the owner and the fief levied off citizen wealth (or a castle's surplus skim) |
| `CASTLE` / `MINTING` / `PRISON` | a castle's daily income; coin struck from silver; prison labour and rations |
| `ADMIN` / `WALLS` | the fief's daily salary bill and wall upkeep, paid or short |
| `GARRISON` | the garrison's wage and maintenance, and who paid each |
| `BUILD` | the construction reserve and what it bought |
| `PURSE` / `MARKET` | daily movement in each of the two purses |
| `COUNTER` | who the money over the counter came from — player, lord, caravan, villager, garrison, bandit |
| `LIQUID` | how far real trade has carried the market from vanilla's target (§8.1) |
| `DAILY` / `PROSPER` | end-of-day state of every settlement, and the prosperity terms |

`STORE` and `DEMAND` read against each other: a good refused daily while another goes unmet means the
villages are producing the wrong thing, and no town-side adjustment will fix it. `PRICE` is where the
two calibrations meet: any town holding at least `AbundantDays` of a good should read 1.00×, and if
nothing ever does, `AbundantDays` is set higher than a working supply chain can keep the shelves.

The `SHOP*` lines answer different questions and are easy to confuse. `SHOPSCALE` is how much the
bench *could* have run, `SHOPCAP` what was skipped for a full store, `SHOPBLOCK` how much was refused
and why, `SHOPIDLE` which recipes never got a turn at all, and `SHOPS` the money. A town producing
nothing reads them in that order.

**Config** (`RBMConfig.Campaign.cs` / `RBMConfig.Debug.cs`, all gated on `rbmCampaignEnabled`; the
XML parse default is what a fresh config gets):

| Setting | Default | |
|---|---|---|
| `villageProductionMultiplier` | **0.5** | village output and warehouse size (0.01–2) |
| `workshopProductionMultiplier` | 1 | workshop speed on top of the prosperity scale (0.01–2) |
| `workshopHeadroomGateEnabled` | on | skip a cycle whose outputs are all at the storage ceiling |
| `rbmDaysOfSupplyAiSignal` | on | AI reads the days-of-supply price factor (§5.2) |
| `constructionBudgetShare` / `buildingCostMultiplier` | 0.01 / 250 | treasury share into the build reserve; project cost multiple |
| `kingdomCaravansEnabled` / `caravanInvestmentEnabled` | on / on | intra-kingdom supply caravans, and their capital injection |
| `showInventoryItemWeight` | on | the `Wt.` column (restart to apply) |
| `economyLoggingEnabled` | **off** | the economy log |

The trade-good repricing, the price model and the wage table have no toggle of their own — they
apply whenever the campaign module is enabled.

---

## 13. Known gaps

**The circuit is not closed.** Money still enters and leaves the ledger at these edges:

| Edge | Direction | Note |
|---|---|---|
| **Soldier spending** | conjured | §10. By far the largest. The stack's whole wage is minted as spoils and spent into citizen wealth. Vanilla separately destroys that same wage out of clan gold, so the two roughly cancel in aggregate — but not in place, and no transfer links them |
| Worldgen seeding | conjured | deliberate, once, new campaigns only (§2.1); booked as `Source.Seed` so it is not taxed as a trade |
| Castle income, prison labour | conjured | deliberate — a castle's tax on its lands, a prisoner's work |
| Minting | goods → money | silver ore off the shelf becomes coin; the ruler's and owner's cuts leave for hero gold. `CoinsPerOre` = 85 is deliberately about twice silver's trade value (43), so the mint adds new money on top of the ore's worth |
| Village and castle admin salary, walls, construction consumables | destroyed | deliberate — paid to people and trades the ledger does not track. Capped at what the purse holds |
| Town militia kit | goods only | a town arms a new watchman straight off its own shelves with no coin moving, and a disbanded one puts his kit back; only with the recruit-supply draw off is it a bare citizen-wealth debit (village and castle kit is bought from a town and conserved) |
| Raid and siege plunder | destroyed / to spoils | half of what is drained becomes the raiders' spoils, half is destroyed |
| ~~Castle gold writes~~ | **closed** | `RouteNativeWrite` books a castle's native gold writes into its settlement wealth (no tariff), and `RansomFunding` charges prisoners sold at a castle to that same wealth |
| ~~No-buyer sales~~ | **closed** | the town is no longer credited for a sale nobody paid for — see below |
| ~~Hideout gold~~ | **not real** | `SettlementComponent.Gold` defaults to 0 and only `ChangeGold` writes it; nothing in `CampaignSystem` ever calls it for a hideout. There is no faucet here to close — and RBM's funnel passes hideouts through untouched |

**Why no-buyer sales are closed.** Vanilla's `ItemConsumptionBehavior.MakeConsumption` credits the town
for every civilian purchase, though the townsfolk buying have no purse — the single largest source of
manufactured money in the economy. Both of its legs are now reimplemented and neither credits anything:
the goods leg in `RBMTownFoodSupply.MakeConsumptionPatch` and the food leg in `BuyFoodFromMarket`. Under
the two-purse ledger the buyer and the seller are *both* inside citizen wealth, so a townsman handing a
merchant a denar moves nothing across the settlement's boundary — the pot is unchanged and the goods are
simply eaten.

The **tariff** on those sales is still charged, and that is deliberate rather than a leftover: it debits
citizens and credits the treasury, so it conserves. The fee is on the trade, not on the trader.
Garrison, staff and prisoner rations move no money at all: they are taken from stock (§6.3), where
vanilla *credited* the town for feeding its garrison and so made a bigger garrison make a town richer.

**Clamps that swallow a shortfall.** Most callers credit the mover's returned figure. The delivery sale
does not: it pays the convoy before debiting the market and discards the debit's return, so it would
conjure the difference if the market ran dry mid-sale. (The town commission path that once did the same
is moot now the town commission is zero.)

**Ordering hazard.** Reading settlement wealth lazily seeds it from live prosperity. A read between the
constructor's reset and the save being loaded would seed a value that then persists, shadowing the
saved figure.

**Calibration.**

- Every rate and coefficient above is uncalibrated in-game.
- `QuietRate` (§3.3) no longer matches the base set's own sum (≈0.254), so no village sits at the
  quiet end of the party-size band.
- `StorageDays` and the tier-3 `FoodStockDays` (§6.2) are two expressions of one decision and must be
  moved together. `AbundantDays` (15) is now deliberately below them. The 30/40/50/60 ladder and the
  3-day villager dearth mark are uncalibrated.
- Charcoal at 0.6 units/Prosperity/day is the largest physical flow in the economy, against a base
  village rate of 0.1/Hearth (halved by the default multiplier). Either the rate or the production side
  is wrong.

**Castles are half in.** A castle has its own single-pool economy (§2.1), its own countryside
equilibrium (§7), the granary ladder and the famine tiers (§6.2, §6.6) — but its food is still
vanilla's modelled figure rather than a market, it prices on vanilla's scale, and it sits outside the
market scaling (§8). Putting castle prosperity on the household scale, then lifting the §8 gates
together, is the real fix.
