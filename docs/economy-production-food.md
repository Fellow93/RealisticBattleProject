# RBM Economy — Settlements, Production, Food and Wealth

How the settlement economy works: what villages make, how it reaches a town, what a town does with
it, what everything costs, and — in detail — where the money comes from and where it goes.

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

The circuit is not yet closed — §12 names every edge where money is still conjured or destroyed.
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
| Who holds it | **towns and castles** (`HasMarket`) | **villages, towns and castles** (`Holds`) |
| Where it lives | **is** `SettlementComponent.Gold` — the vanilla field itself, not a mirror | towns/castles: a new `Dictionary<string,int>` by `StringId`. **Villages: also `SettlementComponent.Gold`** |
| Seed | vanilla's `Town.OnInit` → 20,000 | `Prosperity × TreasuryPerProsperity` (**210**) for town/castle, `Hearth × TreasuryPerHearth` (**2**) for a village |
| Persistence | vanilla's own save data | `SyncData` key `RBM_settlementWealth` |

Citizen wealth deliberately *is* vanilla's `Gold` field so that every native reader keeps working
unchanged — the villager sale gate, caravan trades, workshop income, the player's trade screen. A
village holds only the one purse, and it is that same field, so a village needs no mirror at all.

Both movers clamp at zero and **return what actually moved**. The clamp is the authority: callers
credit the returned figure, never the requested one, so a shortfall cannot conjure the difference.

Seeds are `Prosperity × 210` because 210 = `0.35 × VanillaProsperityScale(20) × 30 days` — roughly a
month of the fief's turnover.

`SettlementWealth.Reset()` runs from `RBMSettlementWealthCampaignBehavior`'s **constructor**, the only
hook that runs before the save is read. An absent `SyncData` key leaves the dictionary untouched, so a
null guard would never catch a cross-campaign leak.

### 2.2 The funnel

`Settlements/SettlementGoldFunnel.cs` prefixes `SettlementComponent.ChangeGold`, so *every* native
write to a settlement's gold is routed rather than intercepted case by case. In order:

1. Module off, zero amount, or already inside the funnel → let vanilla run.
2. Suppressed (raised only around `Village.DailyTick`, §3.4) → **drop the write**.
3. `NativeTradeConservation.TryTakeCommission` handled it → skip vanilla.
4. `SettlementWealth.RouteNativeWrite` → village to settlement wealth, town/castle to citizen wealth,
   then levy the tariff. If it declines (a hideout, no component) vanilla runs, so money is never
   destroyed by falling between the two.

### 2.3 Citizen wealth — sources

| Source | Amount | Conserved? |
|---|---|---|
| **Counter trade** — anyone selling into the town: lords, caravans, the player's trade screen, ransom payments. Routed through the funnel | vanilla's own figure | **Conserved** — comes out of a hero's gold or a party's trade gold |
| **Worldgen seed** — `Town.OnInit` | 20,000/town, once | *Conjured*, deliberately |
| **Administrative wages** — the fief pays its staff, who spend locally | `TownDailySalary` = **300**/day | **Conserved** — treasury → market |
| **Construction labour** — the player boosting a build project pays wages | what the owner actually paid | **Conserved** — owner → treasury → market |
| **Dearth advance** — the fief buys food its market cannot afford (§4.3) | `units × price`, food only | **Conserved** — treasury → market |
| **Garrison rations** — the fief provisions its soldiers (§6.3) | `units × price` | **Conserved** — treasury, then owner's gold, → market |
| **Workshop wages** | measured capital + owner gold spent | **Conserved** — closes a vanilla destroy |
| **Soldier spending** — troop goods, carousing, surgery (§10) | see §10 | ⚠️ **Conjured.** Paid from `SpoilsPool`, a parallel currency minted from wages without deducting the payer's gold. The dominant faucet in the economy |

Explicitly *no longer* a source: the townsfolk's own purchases. A townsman paying a merchant is
internal to citizen wealth, so vanilla's `town.ChangeGold(+price)` on every consumption sale is gone.
Only the market fee moves.

### 2.4 Citizen wealth — sinks

| Sink | Amount | Conserved? |
|---|---|---|
| **Counter trade** — the town buying from a caravan, lord or the player | vanilla's figure | **Conserved** |
| **Villager delivery** — paying a convoy for its cargo (§4.2) | `units × price` | **Conserved** → the convoy's trade gold |
| **Trade tariff** — a market fee on every transaction | `TariffRate` = **1%** of trade value, towns only | **Conserved** → treasury |
| **Wealth tax** — the owner's cut of the market | `DailyRate` = **0.00027** of citizen wealth/day, towns only | **Conserved**, but *leaves the settlement* — the one permanent drain to the owner clan |
| **Commission** — vanilla's trade tax, rerouted so it is paid rather than conjured | per `SettlementTaxModel` | **Conserved** → owner via the tax ledger |
| **Ransom** — the market funds prisoner ransoms | prisoner price, clamped | **Conserved** |

### 2.5 Settlement wealth — sources

| Source | Amount | Conserved? |
|---|---|---|
| **Seed** | `Prosperity × 210`; villages `Hearth × 2` | *Conjured*, worldgen only |
| **Trade tariff** | 1% of every town transaction | **Conserved** from citizen wealth |
| **Village homecoming** (§4.4) | `VillageShare` = **20%** of the trade tax the convoy's sale generated | **Conserved** — taken off the owner's accrued tax |
| **Village stall trade** | whatever a party buys at the village | **Conserved** |
| **Construction refunds** | the owner's own money | **Conserved** |
| **Soldier spending at a village** | see §10 | ⚠️ *Conjured* (spoils origin) |

### 2.6 Settlement wealth — sinks

| Sink | Amount | Conserved? |
|---|---|---|
| **Garrison wages** — the fief pays a share of its own garrison's bill | `TownGarrisonWageShare` = **25%** of the party wage, clamped to the purse; the owner's bill drops by the same | leaves as spoils wage credit, returns via §10 |
| **Garrison rations** (§6.3) | the food bill for soldiers + staff | **Conserved** → citizen wealth |
| **Militia stipend** (§2.7) | `MilitiaWageShare` = **10%** of the stack's wage | **Conserved** into the spoils ledger |
| **Administrative wages** | town **300**/day → citizens. **Village 100/day → destroyed** | mixed, deliberately |
| **Dearth advance** (§4.3) | food the market could not afford | **Conserved** → citizen wealth |
| **Village commission** | the full stall commission, pushed into the owner's tax ledger | **Conserved** (vanilla would have destroyed it) |

The village admin wage is destroyed on purpose: there is no citizen pot in a village to hand it to, so
it leaves for the untracked household economy.

### 2.7 Militia are paid, and capped by the purse

`Settlements/MilitiaUpkeep.cs`. Militia used to accrue a full soldier's wage from nothing. Now the
fief pays a stipend and can only keep what it can pay for:

```
stipend           = stackWage × MilitiaWageShare              MilitiaWageShare  = 0.1
AffordableMilitia = settlementWealth / (0.1 × tierWageBase × MilitiaPayDaysHeld)     MilitiaPayDaysHeld = 30
```

A postfix on `DefaultSettlementMilitiaModel.CalculateMilitiaChange` forces the day's change down to
`-MilitiaShedPerDay` (**1**/day) whenever `Militia > AffordableMilitia`, adding it as a named line
(`{=RBM_militia_unpaid}Cannot be paid`) so vanilla's own breakdown stays visible. Applies to every
settlement kind. Only what the treasury actually handed over is banked as spoils.

### 2.8 What is still outside the ledger

- **`TradeTaxAccumulated` → owner clan.** A write-only ledger RBM only adds to and, for the village
  share, subtracts from.
- **Workshops.** `Workshop.Capital` is a third purse. RBM redirects the expense leg into citizen
  wealth and otherwise only records it.
- **Clan and hero gold.** Party wages, tax income, tournament prizes — all outside.
- **`SpoilsPool`.** A parallel currency minted from wages and spent into citizen wealth. The largest
  unconserved edge remaining (§10, §12).

---

## 3. Village production

Every village makes a fixed subsistence base set, plus its `VillageType`'s speciality on top. Output
is linear in raw `Hearth`:

```
daily units of good k = RoundRandomized( rate_k × Hearth )
```

### 3.1 The base set

Produced by every village, whatever its type:

| Good | Rate | | Good | Rate |
|---|---:|---|---|---:|
| grain | 0.05 | | wool | 0.02 |
| cheese | 0.038 | | meat | 0.011 |
| butter | 0.03 | | hides | 0.01 |
| hog | 0.022 | | sheep | 0.006 |
| cow | 0.003 | | | |

**Sum: 0.190 per Hearth per day.**

Specialities are additive — a cattle farm makes base cheese *plus* cattle cheese. Rates run from
`silk_plant` at 0.008 to `clay_mine` at 7.5; the farms sit at 0.019–0.118, the mines and lumberjacks
one to two orders above. Horse ranches are generated from the culture's mounts rather than tabled:

```
HorseNormalRate = 0.007      {culture}_horse
HorseWarRate    = 0.0020     t2_{culture}_horse
HorseNobleRate  = 0.0005     t3_{culture}_horse
HorsePackBucket = 0.003      split evenly across the ranch's pack animals
```

so the pack-animal total per ranch is constant however many pack items the culture has. Items that
fail to resolve (missing DLC) or are flagged not-merchandise are dropped when the table is built, and
the resolved map is cached per `VillageType`.

Vanilla's separate food-production track is disabled outright — food is part of the base set now.

### 3.2 Warehouse capacity

```
capacity = ceil( max(1, totalRatePerHearth × Hearth) × CapacityDays )      CapacityDays = 5
```

Sized off the **total** rate, because every reader compares it against the sum of the whole roster,
never against one good:

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
term. Mines and lumber camps sit far above `BusyRate` and peg to the largest party.

⚠️ `QuietRate` was pinned to the base set's own sum so a speciality-less village landed exactly on the
quiet end. The base set has since grown to 0.190, past it, so nothing is fully quiet: the floor is
`busyness ≈ 0.06`, `divisor ≈ 38.8`. Re-pin it when the rates are next calibrated.

### 3.4 When a village produces nothing

One gate stops production: `VillageState != Normal` — raided or looted.

`TradeBound == null` is deliberately **not** a gate. Vanilla's null check guards only the
worldgen-seeding branch, protecting the settlement goods are written *into*; the normal branch fills
the village's own store with no such test. A village with no reachable non-hostile town — a castle
village in wartime, for the whole war — keeps producing into its own store.

Vanilla's daily `Gold > 1000 → clamp to 1000` on a village is suppressed (a snapshot/restore around
`Village.DailyTick`, since nothing else there touches gold). A village purse is real money now and
must be allowed to accumulate.

---

## 4. The convoy

### 4.1 Dispatch

```
MaxConvoysPerVillage = 2                       (vanilla: 1, enforced by a single component slot)
hourly raise chance  = 0.15                    (vanilla)
dispatch threshold   = 0.5 × warehouseCapacity
hearth cost          = max(0, Hearth - (manCount + 1)/2)
```

The threshold is applied by an **IL transpiler** that rewrites the result of every
`GetWarehouseCapacity()` call inside `ThinkAboutSendingItemToTown`. If the anchor ever stops matching
it no-ops silently and vanilla's full-warehouse gate stands.

A second convoy needs three pieces of native behaviour to give way, since the village holds only one
`VillagerPartyComponent` slot: the hourly tick destroys any villager party the slot does not name (so
the slot is repointed pre-emptively), `OnFinalize` is replaced to hand the slot to a survivor rather
than clear it, and the zero-member sweep is extended to every registered convoy. The register rebuilds
itself on load from `OnInitialize`, so nothing is saved.

### 4.2 Escort

Guards scale with what the convoy carries, and the richer the load the more of them are veterans:

```
GoldPerEscort  = 400        MaxEscort = 12
desired        = min( floor(cargoValue / 400), 12 )
missing        = desired - existingEscort                (a target, not a per-trip addition)
missing        = min( missing, floor(Hearth) )

EliteStartValue = 2000      EliteFullValue = 8000
eliteShare      = clamp( (cargoValue - 2000) / 6000, 0, 1 )
eliteCount      = round( missing × eliteShare )

MeleePerRanged  = 2
rangedCount     = floor(eliteCount / 3) + floor((missing - eliteCount) / 3)     remainder melee

hearth cost     = max(0, Hearth - (missing + 1)/2)       (vanilla's per-villager rate)
```

Militia already aboard count toward `desired`, so repeat trips top the guard back up rather than
stacking. The elite share applies only to guards added now. The melee/ranged split runs once per tier,
so total ranged can fall one short of `floor(missing/3)`. If the culture lacks a troop class the other
takes the whole allocation.

### 4.3 Delivery

The town buys the cargo **food before non-food; within food, cheapest per unit first; otherwise in
vanilla's reverse-roster order**. Per lot:

```
wanted     = TownStorage.Accept(settlement, item, lotAmount)          (§5.1)
affordable = min( wanted, floor(citizenWealth / price) )
if affordable == 0 and the lot is food:
    affordable = min( AdvanceForFood(lot), wanted )
```

`price` is `town.GetItemPrice(..., isSelling: true)` — the wholesale leg, so scarcity does not inflate
what the town pays its own suppliers (§5.2).

Ordering is what decides whether a town is fed. In roster order, velvet at ~26,500/unit and warhorses
at ~10,000 drain the purse before food is reached; and among food, fish at ~1,140 buys the same unit
of stock as grain at 60.

**`AdvanceForFood`** is the fief buying grain out of public funds because its market has run out of
money. Food only, by design — a town too poor to buy wool goes without wool.

```
affordable = min( lotAmount, floor(settlementWealth / price) )
moved      = Debit(treasury, affordable × price)
Credit(citizens, moved)
return moved / price                                   units the market can now afford
```

The gold moves treasury → citizens rather than paying the villagers directly, so the purchase itself
stays an ordinary one and the market ends up holding the money it needed. Without a second purse the
first empty market would be permanent, since citizen wealth is both the money and the gate on what may
be bought — and §8 has switched off the controller that used to break that loop. Logged as `DEARTH`.

Storage room is checked **before** money, so a full store cannot be talked into a sale by an advance.

Villager parties never pass through `SellItemsAction`, so the market fee is levied by hand on the
total.

### 4.4 Homecoming

The village keeps a share of the trade tax vanilla would have handed entirely to the owner:

```
tax  = the tax vanilla actually charged on this convoy's takings
kept = floor( tax × VillageShare )                    VillageShare = 0.2
```

`kept` comes off `TradeTaxAccumulated` and goes into the village purse. Vanilla's village commission
rate is 1.0 — the owner takes everything — so this is a real 20% cut to every lord's village income,
the player's included. Taking the share off what was *charged* rather than off the gross keeps it
correct if a policy or perk ever moves the rate.

The convoy buys nothing in town; it carries only its takings home. Logged as `HOMECOME`.

---

## 5. The town's shelf

### 5.1 Storage: goods are not fungible

`Production/TownStorage.cs`. A town holding 1,900 fish and 60 grain read as a full granary under one
undifferentiated cap — no shortage to the prosperity model, none to the siege logic, and a brewery
that could not buy a sack of grain. Each good now gets its own ceiling:

```
Capacity(town, item) = max(1, ceil( CitizenDemand.DailyUnits(town, item) × StorageDays ))
StorageDays = 60
Headroom             = max(0, Capacity - held)
Accept(offered)      = min(offered, Headroom)
```

Two months: long enough to ride out a season, short enough that a market is not an infinite sink.
Nothing is destroyed when a cap binds — the goods stay with whoever brought them, to be carried to a
town with room.

Clothing has no trade-good id, so garments share one wardrobe ceiling across every worn slot;
otherwise each distinct tunic would get its own two months' supply and the cap would never bind.
Goods RBM does not model household consumption of — iron, clay, tools, war gear, horses — are
**uncapped**, because they are bought by workshops and passing parties and a guessed cap would
throttle them.

The clamp is applied at the two inbound doors: native `SellItemsAction`, and villager delivery.

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
days   = unitsHeld / CitizenDemand.DailyUnits(town, item)
factor = clamp( (AbundantDays / max(days, FloorDays)) ^ ScarcityExponent, MinFactor, MaxFactor )

AbundantDays = 60      ScarcityExponent = 0.6      MinFactor = 1
FloorDays    = 0.1     MaxFactor        = 8        WholesaleFactor = 1
```

| Days of stock | 60 | 30 | 15 | 10 | 5 | ≤4.7 |
|---|---|---|---|---|---|---|
| Price | 1.0× | 1.5× | 2.3× | 2.9× | 4.1× | 8.0× |

An empty shelf is zero days, i.e. maximum price by construction — the signal exists before the first
sale rather than after it. `AbundantDays` is deliberately equal to `StorageDays`, so a town with a
full store pays exactly the base value from §11's historical table, which is a *floor* price rather
than an average.

`MaxFactor = 8` is a deliberate ceiling rather than an open curve. At an effective 17.7× cap, every
good with no stock priced at the ceiling; because vanilla sums *uncapped* item prices into the figure
it tests against the town's purse, town-broke refusals rose from 58 to 878 and became the single
largest blocker of production, workshop cycles fell 79% → 64%, taverns bought nothing at all, and
treasuries poured 3,867 denars/day into dearth advances propping up markets that had been solvent.
Velvet has no producer anywhere in the chain, so an unbounded markup on it is a permanent tax, not a
signal.

`WholesaleFactor = 1` keeps scarcity out of what the town pays a supplier. Carrying it through meant a
short town paid up to 8× and that money left for the village: 49 of 133 settlements fell under 10,000
denars, the lowest holding 300 — and at 300, vanilla's own solvency test refuses nearly every workshop
cycle.

This is a ratio postfix, not a replacement: vanilla's scarcity contribution is divided out and RBM's
multiplied in, leaving item value, trade penalties, war markups, Trade skill and perks, caravan and
village modifiers untouched. Goods outside the basket are priced entirely by vanilla.

### 5.3 Two ways stock is withheld

Deliberately disjoint, covering the two different buyers:

- **Food reserve** (`Production/TownFoodReserve.cs`) — `ReserveFraction` = **0.5** of a town's food is
  held back from *outside* buyers: AI parties, armies, caravans. The player is exempt (they trade
  through a different code path). The floor is fixed once per campaign day, the first time an outsider
  tries to buy: a moving floor is not a reserve, since half of a shrinking number is never empty and a
  run of buyers would nibble the stock toward zero. Deliveries arriving later that day are freely
  buyable; tomorrow the floor re-anchors. Townsfolk, garrison and militia still eat from the whole
  stock — that is not a market sale.
- **Hidden stock** (`Economy/HiddenMarketStock.cs`) — `HiddenFraction` = **0.5** of every stack is
  hidden from the *player's trade screen only*, so a fief never shows everything it holds at once. The
  screen is handed a shadow roster; drags apply to it live, its in-screen Reset reverts to the halved
  view, and on a confirmed trade the delta is applied to the real roster. Cancel touches nothing.
  Integer truncation means a stack of one shows whole. The campaign side — prices, rations, caravans —
  always reads the full roster.

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
| 0.518 | 0.176 | 0.124 | 0.053 | 0.05 | 0.031 | 0.02 | 0.018 | 0.01 |

Grain half and beer a sixth is the medieval diet; at that volume beer is food, not drink. Wine and oil
are not food-store goods but count as ration filled — the alternative is a town that buys its oil and
still reads 3% starving. They correctly do not show in the granary: a barrel of wine is not a siege
reserve.

**Staples** — units per Prosperity per day: charcoal **0.6**, salt **0.24**, pottery **0.005**,
planks **0.003**. Charcoal is the largest physical flow in the economy, larger than the town's food,
and that is not an error — heating and cooking burn more by weight than a household eats. Only
lumberjack villages make any, so it reads as a chronic shortfall.

**Luxuries** are gated on savings, measured per household:

```
savings = ( citizenWealth / Prosperity ) / IncomePerProsperity        IncomePerProsperity = 127.4
```

| Tier | Threshold | Goods (units/Prosperity/day) |
|---|---|---|
| Small | 5 days of income | date_fruit 0.01, wine 0.01, oil 0.01, olives 0.01, pottery 0.002 |
| Medium | 15 days | jewelry 0.0033, felt 0.002, fur 0.0015, **+0.05 garments** |
| Large | 30 days | jewelry 0.0033, velvet 0.001 |

Tiers are cumulative, so a town at the large tier buys 0.0066 jewelry. Garments run at
`StapleGarments` **0.1**/Prosperity/day as a necessity, plus `MediumLuxuryGarments` **0.05** at the
medium tier; they are bought cheapest-first, because a household replacing a worn tunic buys a tunic —
unsorted, towns quietly consumed the merchants' finest stock at forty pieces a day.

Savings are expressed per household on purpose. A man buys velvet when *he* has thirty days' earnings
behind him, not when his city does. The tiers are therefore blind to town size: a small prosperous
town reaches the large tier on far less absolute wealth than a big poor one, which is correct, because
it is a statement about comfort rather than size. `IncomePerProsperity` is never debited — earnings and
spending are the same pot under §2 — it exists only to size the thresholds.

**Nothing is conjured to meet demand.** Several basket goods have no producer anywhere in the chain,
civilian garments above all, so that demand simply goes unfilled and the money goes unspent. The
`DEMAND` log names every shortfall.

`ModelledGoods` — the union of these tables — is the boundary of what RBM claims to understand about a
town's appetite, and three systems are drawn along it: `TownStorage` caps these and nothing else,
`RBMMarketPrices` prices these and nothing else, `DEMAND` reports these. Everything else stays on
vanilla's gold budget, which still runs afterwards for items the basket does not cover.

### 6.2 Food stocks

A town's `FoodStocks` is the food physically in its market roster:

```
FoodStocks = min( foodUnitsInMarket, FoodStocksUpperLimit × TownFoodStockScale )
TownFoodStockScale = 10
```

The limit is scaled to keep the granary a town *shows* the same size as the one it can *fill* under
`StorageDays = 60`; it multiplies the whole of the vanilla limit, so granary buildings keep
proportional worth. The clamp is required because `DefaultSettlementProsperityModel` pays
`((FoodStocks + FoodChange) − FoodStocksUpperLimit) × 0.1`, and an unclamped roster would hand the
town a large standing prosperity bonus for hoarding.

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
soldiers   = men / NumberOfMenOnGarrisonToEatOneFood
```

The divisor of 4 means a town eats **10× more food per point of prosperity** than vanilla — the single
change most of this document follows from. Militia are charged for; vanilla never fed them.

Perk order mirrors vanilla term for term: under siege `Steward.Gourmet` on soldiers and
`Medicine.TriageTent` on rations; always `Steward.MasterOfWarcraft` on households; then the
`FoodConsumption` building effect on the total; then `RoundRandomized`. A flat
`AdministrativeUpkeep.TownDailyFood` = **3** is added after, as a floor, so a town with no prosperity
and no garrison still feeds its staff.

The day's units are then **split by who eats**, on the pre-building shares so the total ration is
unchanged by splitting it:

```
soldierUnits     = round( units × soldiers / (households + soldiers) )      clamped ≤ units
civilianUnits    = units - soldierUnits
provisionedUnits = soldierUnits + 3
```

- **Civilians pay nobody.** Townsman and merchant are both inside citizen wealth. Rations are shaped
  by the §6.1 food mix first; whatever the mix could not fill falls back to a cheapest-first buy, so
  the *number* of rations a town gets is exactly what it was and starvation, prosperity and loyalty do
  not move. Without that fallback a town with no brewery would run permanently 17.6% hungry over a
  preference. Only the market fee moves money.
- **Soldiers and staff are provisioned by the fief.** `PayForGarrisonFood` takes the cost from
  settlement wealth first, charges any remainder to the owner clan's leader (clamped to their gold),
  and credits whatever actually moved to citizen wealth.

Rations are bought cheapest-first within each leg. A ration is a ration, so buying 1,140-denar fish
while 60-denar grain sits on the shelf buys the town nothing and costs it the difference.

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

The ration divisor makes any deficit ten times larger than vanilla's, and vanilla's famine coefficient
was tuned against the old one:

```
vanilla coefficient  0.5  prosperity lost per unit of daily deficit   →  ≈ P/80 = 1.25 %/day
RBM coefficient      0.05                                            →  same proportional severity
```

At vanilla's coefficient the RBM deficit would cost ≈ P/8 = 12.5%/day and empty a city in a fortnight.
Applied as a delta correction of `deficit × (0.05 − 0.5)`, inside vanilla's own gates, so the
`HelpingHands` perk is counted exactly once and the tooltip shows a single line. Towns **and** castles.

### 6.7 Demand feedback

Every purchase — rations, the household basket, the residual vanilla budget, soldier spending — feeds
its gold value back as market demand through one shared call:

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

Town prosperity is pulled toward a share of the hearths of the villages bound to it, rather than by
vanilla's housing-cost ladder.

```
ProsperityPerBoundHearth = 0.1
target = 0.1 × Σ Hearth over trade-bound villages
gap    = target - Prosperity

ConvergenceRate = 0.1                       → time constant ≈ 10 days
delta  = gap × 0.1 - vanillaHousingCosts
```

The rate is a weight, not a speed. Prosperity rests where every term cancels, so any other term
contributing a steady `x`/day parks the fief `x / ConvergenceRate` away from its target — a tenfold
lever at 0.1, so Surplus Food alone (~+11/day) displaces a well-fed town by ~110. A slower rate makes
the countryside a rounding correction rather than an attractor; the ten-day time constant is the price
of giving this term enough weight to argue with its neighbours.

Vanilla's ladder is transcribed and subtracted in the same term so the two collapse into one readable
tooltip line. That ladder is a flat step function:

| Prosperity | Term |
|---|---|
| < 250 / 500 / 750 / 1000 / 1250 / 1500 | +6 / +5 / +4 / +3 / +2 / +1 |
| 1500 – 6000 | 0 |
| > 6000 / 9000 / 12000 / 15000 / 18000 / 21000 | −1 / −2 / −3 / −4 / −5 / −6 |

New games seed every town at `target` directly. Loaded saves converge on their own — ~10 days to close
two thirds of the gap, ~50 to close it. Castles keep vanilla prosperity entirely.

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

**Towns only**, on every leg. Castles are outside §7, so they carry vanilla-scale prosperity and must
keep vanilla's models too — scaling a castle would target a ~490k treasury on prosperity 1000 and
price its goods around 6× a town's, which is a buy-in-town/sell-in-castle gold printer.

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
for telling whether §2's circuit is actually closing.

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
the 1000 nudge and the 3000 threshold at the same *relative* size.

The three gates are one decision: §8.3 divides by the same scale §8.2 multiplies by, so gating one
without the other would collapse castle prices by 20× instead of inflating them 6×. Bringing castles
into §7 means lifting all three together.

---

## 9. Fief finance

The fief's treasury is funded and drained by a small set of named flows, all tabled in §2:

| In | Rate |
|---|---|
| Trade tariff on every market transaction | 1% |
| Village homecoming share | 20% of the convoy's trade tax |

| Out | Rate |
|---|---|
| Garrison wage share | 25% of the garrison party's wage |
| Garrison + staff rations | at market price |
| Militia stipend | 10% of the stack's wage |
| Administrative salary | 300/day town, 100/day village |
| Dearth advances | as needed, food only |

The garrison wage share is deducted from the owner's bill by the same figure it takes from the fief,
so the whole chain — purse drain, clan top-up, and the per-fief garrison line in the clan finance
screen — follows from one number. It is only withdrawn on the applying pass, but read on both, so the
projection matches the charge.

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
and castles keep the flat value, since a castle prices on the vanilla scale (§8). The price is
snapshotted once per party, so a party stripping the shelves pays yesterday's price for today's
shortage.

**Carousing** is bounded twice: `MaxSurplusFunFractionPerHour` = **0.02** of the purse per hour, and a
per-man ceiling of `MaxFunPerManPerDayPerTier` = **25** × (Tier + 1) gold per day, applied as a
per-stack hourly clamp. Half of it (`CarousingGoodsShare` = **0.5**) leaves the shelf as physical
tavern fare — beer 38%, wine 18%, meat 18%, cheese 11%, fish 8%, grapes 7% — for which no additional
money moves.

**Who spends.** Food buying is visitors-only. Carousing and luxuries are not: garrisons and militia
now spend in their own settlement, because their coin is the fief's own treasury money coming back
(§9's 25% wage share and 10% stipend), which makes it a loop rather than an invention.

**The tally.** A decaying per-town record of what soldiers have spent, `TallyDecayPerDay` = **0.9**
(half-life ~1 week), capped at `MaxGarrisonTradeShare` = **0.5** of the countryside term and scaled by
**0.25**. Persisted under `SyncData` key `RBM_townTroopTrade`, reset in the owning behavior's
constructor. It now feeds only the §8.1 yardstick — with the controller off it moves no money.

⚠️ **This is the economy's largest faucet.** Spoils are minted from wages without deducting the payer's
gold, so every denar of troop goods, carousing and surgery is new money entering citizen wealth.
Soldier spending brings a town roughly **nine times** what deliveries and the wealth tax take out of
it, and with the top-up controller off nothing absorbs it.

Caravans are not exempt: their guards read as soldiers to every gate here, so a caravan provisions and
carouses like a war party and pays the town for it. Villagers are exempt.

---

## 11. Trade good values

Trade goods are valued and weighted off historical figures: a period price in denars ×10, and the real
mass in kilograms of one trade lot. Value and weight move together, so a cart of velvet is not worth
what a cart of hardwood is. These are *floor* prices — §5.2 marks up from here.

Applied at both good-creation sites (XML goods and the code-built grain/meat/iron chain), before item
category averages, initial town stock seeding, and trade AI read them. Gated on
`realisticTradeGoodPrices` (default on) — the one toggle here independent of `rbmCampaignEnabled`.
Items outside the table — tools, stolen goods, trash, all non-Goods — are untouched.

| Good | Value | Weight (kg) | | Good | Value | Weight (kg) |
|---|---:|---:|---|---|---:|---:|
| grain | 60 | 30 | | wool | 160 | 2 |
| meat | 200 | 30 | | silver | 85 | 0.85 |
| fish | 1140 | 20 | | jewelry | 420 | 0.025 |
| cheese | 166 | 15 | | salt | 300 | 10 |
| butter | 230 | 8.4 | | spice | 125 | 10 |
| grape | 275 | 89 | | cotton | 1925 | 1 |
| date fruit | 333 | 20 | | flax | 340 | 10 |
| olives | 45 | 46 | | clay | 20 | 10 |
| beer | 220 | 110 | | pottery | 100 | 10 |
| wine | 1330 | 85 | | linen | 1700 | 7.6 |
| oil | 270 | 6.23 | | leather | 176 | 0.8 |
| hides | 88 | 0.8 | | velvet | 26500 | 0.5 |
| planks | 10 | 20 | | fur | 833 | 0.75 |
| hardwood | 11 | 200 | | felt | 640 | 1 |
| charcoal | 3 | 4 | | iron ore | 1 | 4 |

Iron ingot ladder: crude 4 / wrought 11 / iron 22 / steel 40 / fine steel 69 / thamaskene 120,
weights 2, 1, 1, 1, 1, 1.

Weight spans four orders of magnitude (0.025 → 200 kg) and decides what a party can profitably carry,
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
| `DELIVER` | what the town bought off a convoy, and what went unsold |
| `DEARTH` | the fief advancing for food its market could not afford |
| `FOOD` | a town's rations: eaten, delivered, unmet, stock against limit |
| `DEMAND` | what the household basket wanted and could not get, by name |
| `STORE` | goods turned away for want of storage room |
| `PRICE` | days of supply and the resulting multiplier, per modelled good |
| `GARRISON` | the garrison wage share the fief paid |
| `HOMECOME` | the village's cut of its convoy's trade tax |
| `PURSE` / `MARKET` | daily movement in each of the two purses |
| `SHOPS` | workshop capital movement |
| `COUNTER` | who the money over the counter came from — player, lord, caravan, villager, garrison, bandit |
| `LIQUID` | how far real trade has carried the market from vanilla's target (§8.1) |
| `DAILY` / `PROSPER` | end-of-day state of every settlement, and the prosperity terms |

`STORE` and `DEMAND` read against each other: a good refused daily while another goes unmet means the
villages are producing the wrong thing, and no town-side adjustment will fix it. `PRICE` is the
instrument for detecting drift between `AbundantDays` and `StorageDays`, which must stay equal.

**Config** — three toggles, all default on: `realisticTradeGoodPrices`, `showInventoryItemWeight`,
`economyLoggingEnabled`. The latter two are additionally gated on `rbmCampaignEnabled`.

---

## 13. Known gaps

**The circuit is not closed.** Money still enters and leaves the ledger at these edges:

| Edge | Direction | Note |
|---|---|---|
| **Soldier spending** | conjured | §10. By far the largest. Spoils are minted from wages; nothing absorbs them now the controller is off |
| Worldgen seeding | conjured | deliberate, once |
| No-buyer sales | conjured | a settlement selling with no counterparty is credited, and tariffed, for a sale nobody paid for |
| Village admin salary | destroyed | deliberate — 100/day/village into the untracked household economy |
| Hideout gold | conjured | vanilla, inert — hideout gold is never spent |

**Clamps that swallow a shortfall.** Most callers credit the mover's returned figure. Three do not, and
conjure the difference when a purse runs dry mid-transaction: the delivery sale pays the convoy before
debiting the market and discards the debit's return; the commission path credits the owner's tax ledger
the full figure regardless of what the market could pay; and the garrison wage share lowers the owner's
bill by a pre-read balance rather than what was actually withdrawn. Separately, provisioned rations
remove food from the roster unconditionally, so a fief broke on both purses feeds its garrison free —
destroying physical value rather than gold.

**Ordering hazard.** Reading settlement wealth lazily seeds it from live prosperity. A read between the
constructor's reset and the save being loaded would seed a value that then persists, shadowing the
saved figure.

**Calibration.**

- Every rate and coefficient above is uncalibrated in-game.
- `QuietRate` (§3.3) no longer matches the base set's own sum, so no village sits at the quiet end of
  the party-size band, and warehouses are ~13% larger than intended.
- `AbundantDays`, `StorageDays` and `TownFoodStockScale` are three expressions of one decision and must
  be moved together.
- Charcoal at 0.6 units/Prosperity/day is the largest physical flow in the economy and only lumberjack
  villages make any. Either the rate or the production side is wrong.
- Several basket goods — civilian garments above all — have no producer anywhere in the chain.

**Castles are vanilla throughout** — outside the food rework (§6), the prosperity equilibrium (§7) and
the market scaling (§8). A castle neither feeds from its countryside nor participates in the reworked
price system. Bringing them into §7, then lifting the §8 gates together, is the real fix.
