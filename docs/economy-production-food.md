# RBM Economy — Settlement Production & Food

How the village-production → convoy → town-market → rations chain works, and the prosperity and
price rescaling that keeps it consistent. All of it lives in `RBMCampaign` and is gated behind
`rbmCampaignEnabled`; with the module off, every leg falls through to vanilla.

Not calibrated in-game — treat the numbers as a starting point.

---

## 0. The ration divisor

```
daily food consumption = Prosperity / NumberOfProsperityToEatOneFood
vanilla divisor = 40      RBM divisor = 4
```

A town eats **10× more food per point of prosperity** than vanilla. Most of the numbers below follow
from that, from food being a physical good, or from the prosperity scale the two together imply.

---

## 1. Village production

Every village produces a base subsistence set of 8 goods plus a speciality set drawn from its village
type, each at its own per-Hearth daily rate. Horse ranches generate their table from the culture's
mounts (`HorseNormalRate 0.007`, `HorseWarRate 0.002`, `HorseNobleRate 0.0005`,
`HorsePackBucket 0.003`); goods that are not merchandise are filtered out.

```
daily output of a good = ratePerHearth × Hearth        rolled with RoundRandomized
```

The same figure backs `CalculateDailyProductionAmount`, so tooltips, trade AI and tax seeding agree
with the tick.

### 1.1 Warehouse capacity

Vanilla's sizing formula, with RBM's own good set as the rate:

```
capacity = ceil( max(1, totalRatePerHearth × Hearth) × CapacityDays )     CapacityDays = 5
```

It is sized off the **total** rate because every reader compares it against the sum of the whole item
roster, not against any one good:

| Reader | Gate |
|---|---|
| `VillageGoodProductionCampaignBehavior.TickProductions` | `rosterSum < capacity × 1.5` (production halt) |
| `VillagerCampaignBehavior.ThinkAboutSendingItemToTown` | `rosterSum < capacity` (dispatch, §3.2) |
| `FishingPartyCampaignBehavior` (War Sails) | `rosterSum < capacity` |

The game tracks one shared store, not a per-good allowance, so there is nowhere to express a per-good
cap. The village tooltip reflects that: per-good `x /day`, then a single `Warehouse  stored /
capacity` line.

### 1.2 Villager party size

Vanilla's `12 + Hearth/[20..40]` band, with the interpolation driven by the village's total per-Hearth
throughput rather than by a sum over `VillageType.Productions`:

```
QuietRate = 0.17     (the subsistence base set alone sums to 0.168/Hearth/day)
BusyRate  = 0.50

busyness  = clamp( (totalRatePerHearth - 0.17) / (0.50 - 0.17), 0, 1 )
divisor   = lerp(40, 20, busyness)
partySize = MinimumNumberOfVillagersAtVillagerParty + floor(Hearth / divisor)
```

Rate enters **per-Hearth**, not as an absolute daily figure, because `Hearth` already appears in the
size term — the absolute would count population twice. Mines and lumber camps sit far above
`BusyRate` and peg to the largest party.

### 1.3 Stored units

The store is measured the way the dispatch and production-halt gates measure it: the sum of roster
amounts, not weight and not distinct item types.

### 1.4 When a village produces nothing

One gate stops production outright: `VillageState != Normal` (raided or looted), which is what vanilla
expresses by having the model return 0.

`TradeBound == null` is deliberately **not** such a gate. Vanilla's null check guards only the
`initialProductionForTowns` branch — it protects the settlement seeded goods are written *into*, not
production itself — and the normal branch fills the village's own store with no such test. A village
with no reachable non-hostile town, which for a castle village in wartime is the whole war, keeps
producing into its own store.

Vanilla's separate `TickFoodProduction` is disabled: food is part of the base set now.

---

## 2. Town food is physical goods

A town's `FoodStocks` is the food actually sitting in its market roster, not an abstract running total.

```
FoodStocks = min( foodUnitsInMarket, FoodStocksUpperLimit × TownFoodStockScale )     TownFoodStockScale = 5
```

The limit is scaled because vanilla sized it against vanilla eating — a 300-unit cap is six days of
RBM rations, which leaves most towns pinned at the cap with nowhere to put a surplus. The clamp
itself is required, because `DefaultSettlementProsperityModel` pays

```
prosperity bonus = ((FoodStocks + FoodChange) - FoodStocksUpperLimit) × 0.1
```

so an unclamped 2,000-grain roster would hand the town **+170 prosperity/day**.

`FoodChange` is measured rather than modelled — the difference between this tick's food count and the
last one's, less anything the town could not buy:

```
measuredChange = closingFoodUnits - previousTickFoodUnits - unmetRations
```

split for the tooltip into `change + unmet` ("Market food") and `-unmet` ("Unmet rations").

`FoodUnitsInMarket` is memoised against `ItemRoster.VersionNo`, since `FoodStocks` is read constantly
and is otherwise a full roster scan.

### 2.1 Consumption

```
households = Prosperity / NumberOfProsperityToEatOneFood        (divisor 4, see §0)
men        = garrisonMembers + militia
soldiers   = men / NumberOfMenOnGarrisonToEatOneFood
```

Militia eat, which they do not in vanilla. Perk order mirrors vanilla: under siege `Steward.Gourmet`
on soldiers and `Medicine.TriageTent` on rations; always `Steward.MasterOfWarcraft` on households;
then the `FoodConsumption` building effect on the total; then `RoundRandomized`.

Beyond the numbers:

- The town **pays market price** for its rations. Vanilla's siege-only path confiscated food for free.
- Items with `BonusToFoodStores` are skipped by generic consumption, so food is only eaten as food.
- Rations are bought **cheapest first**, the same ordering the delivery leg (§3.2) sells in. A ration
  is a ration, so buying the 1,140-denar fish while 60-denar grain sits on the shelf buys the town
  nothing and costs it the difference — and §5.1 sizes the treasury on the assumption that a day's
  rations cost about what a day's rations should. Because the purchase order no longer matches the
  roster order, the buy is planned into a list and executed against `EquipmentElement`, not index.

### 2.2 The non-market food sources

Everything vanilla added straight onto the food total arrives as goods on the shelf instead, paid out
each day before rations are eaten:

| Source | Delivered as |
|---|---|
| `FoodProduction` building effect | grain, at the effect's own amount |
| `HuntingRights` policy | 2 meat/day (`HuntingRightsGame = 2`) |
| `Roguery.DirtyFighting`, under siege only | 2 units of a random good from a 9-item smuggled-food list |

### 2.3 Demand feedback

Every purchase — the town's own rations and the civilian consumption pass alike — feeds its gold
value back as market demand:

```
DemandFromPurchaseFactor = 1.0
added = purchaseValue × DemandFromPurchaseFactor / VanillaProsperityScale        (= /20, see §5)
```

The division is a units conversion, not a dial. Demand lives on the ×20 pool scale of §5.2 while the
purchase is in real denars, so the raw value would arrive twenty times too large.

The 0.15 factors cancel rather than compound. `AddDemand` scales its input by 0.15 and
`UpdateSupplyAndDemand` decays the pool 15%/day, so a sustained addition `F` against an equilibrium
`E` solves

```
D = 0.85 D + 0.15 E + 0.15 F      →      D = E + F
```

i.e. the addition lands at face value, once converted.

### 2.4 Starvation

Stocks can no longer sit floored at a modelled value, so `IsStarving` is re-derived directly: if any
rations went unmet on the day, `RemainingFoodPercentage = -100`.

The starvation *clock* gets the same treatment, and it is the half that bites. The loyalty penalty
fires on `DaysStarving > 14`, and `DaysStarving` is measured from `PartyBase._lastEatingTime`, which
`Town.DailyTick` stamps to now whenever `FoodStocks > 0`. Under a market-backed stock that would let
a partial famine reset its own clock every day for as long as a single grain sat unsold, and never
reach fourteen. RBM captures the stamp in a prefix and restores it in the postfix on any day rations
went unmet, so the clock runs from the last day the town actually fed everyone.

Castles are excluded from all of §2 and fall through to vanilla.

---

## 3. Village → town delivery

### 3.1 Purchase ordering

A town buys a villager party's cargo **food before non-food; within food, cheapest first; otherwise
in reverse roster order**. Per lot:

```
price      = town.GetItemPrice(element, villagerParty, isSelling: true)
affordable = min( lotAmount, floor(townGold / price) )
```

Ordering is what decides whether a town is fed. Buying in roster order lets wool at ~800/unit and
warhorses at ~10,000 drain the purse before food is reached; and among food, fish at ~1,140 buys the
same +1 stock as grain at 60. Whole-map food demand is roughly 2,400 units/day, which as grain is
~150,000 denars and as an unsorted basket is several times that.

Vanilla's pack-animal reserve is preserved: the cheapest pack animal in cargo is held back at
`0.5 × TotalManCount`.

### 3.2 Convoys

```
MaxConvoysPerVillage = 2                       (vanilla: 1, enforced by a single component slot)
hourly raise chance  = 0.15                    (vanilla)
dispatch threshold   = 0.5 × warehouseCapacity (vanilla gate, RBM fraction)
hearth cost          = max(0, Hearth - (manCount + 1)/2)
```

Three pieces of native behaviour have to give way for a second convoy to survive, since the village
holds only one `VillagerPartyComponent` slot:

- `HourlyTickParty` destroys any villager party the slot does not name, so the slot is repointed
  pre-emptively each hour.
- `OnFinalize` is fully replaced: it clears the slot only when the slot named the departing party,
  and otherwise hands it to a survivor.
- `DestroyVillagerPartyIfMemberCountIsZero` gets a postfix sweeping all registered convoys, not just
  the one in the slot.

The dispatch threshold is applied by an IL transpiler that rewrites the result of every
`GetWarehouseCapacity()` call inside `ThinkAboutSendingItemToTown`. If the anchor ever stops matching
it no-ops silently and the vanilla full-warehouse gate stands.

### 3.3 Escorts

Convoys travel with militia scaled to what they carry:

```
GoldPerEscort  = 400        MaxEscort = 12
desired        = min( floor(cargoValue / 400), 12 )
missing        = desired - existingEscort                 (a target, not a per-trip addition)
missing        = min( missing, floor(Hearth) )

EliteStartValue = 2000      EliteFullValue = 8000
eliteShare      = clamp( (cargoValue - 2000) / 6000, 0, 1 )
eliteCount      = round( missing × eliteShare )

MeleePerRanged = 2
rangedCount    = floor(eliteCount / 3) + floor((missing - eliteCount) / 3)    remainder melee

hearth cost     = max(0, Hearth - (missing + 1)/2)        (vanilla's per-villager rate)
```

The melee/ranged split runs once per tier, so total ranged can fall one short of `floor(missing/3)`.
If the culture lacks one troop class the other takes the whole allocation.

---

## 4. Prosperity as a countryside equilibrium

Town prosperity is pulled toward a share of the hearths of the villages bound to it, rather than by
vanilla's housing-cost ladder.

```
ProsperityPerBoundHearth = 0.1
target      = 0.1 × Σ Hearth over trade-bound villages
gap         = target - Prosperity

ConvergenceRate = 0.1                       → time constant ≈ 10 days
delta       = gap × 0.1 - vanillaHousingCosts
```

The rate is a weight, not a speed. Prosperity rests where every term cancels, so any other term
contributing a steady `x`/day parks the fief `x / ConvergenceRate` away from its target — a tenfold
lever at 0.1, so Surplus Food alone (~+11/day) displaces a well-fed town by ~110. A slower rate would
make the countryside a rounding correction rather than an attractor; the ten-day time constant is the
price of giving this term enough weight to argue with its neighbours.

Vanilla's ladder is transcribed and subtracted in the same term, so the two collapse into one readable
tooltip line. That ladder is a flat step function of prosperity:

| Prosperity | Term |
|---|---|
| < 250 / 500 / 750 / 1000 / 1250 / 1500 | +6 / +5 / +4 / +3 / +2 / +1 |
| 1500 – 6000 | 0 |
| > 6000 / 9000 / 12000 / 15000 / 18000 / 21000 | −1 / −2 / −3 / −4 / −5 / −6 |

New games seed every town at `target` directly. Loaded saves are left to converge on their own —
~10 days to close two thirds of the gap, ~50 to close it. Castles keep vanilla prosperity entirely.

Trade-bound hearths are recomputed once per campaign day by walking every village's `TradeBound`
rather than reading `Town.TradeBoundVillages`, which is cached data emptied on load and only
repopulated for castle villages.

---

## 5. Scaling the vanilla economic models

Prosperity sits on a *household* scale, roughly 1/20 of the number vanilla's economy models expect.
Feeding it in raw would collapse market liquidity, so two scale factors are applied:

```
VanillaProsperityScale = 20      (demand)
TownTreasuryScale      = 40      (town gold only)
```

**Towns only** — all three patches in this section. `TargetProsperity` returns 0 for a castle and the
§4 equilibrium skips them, so a castle carries vanilla-scale prosperity and must keep vanilla's
models too: scaling it would target a ~490k treasury on prosperity 1000 and price goods around 6× a
town's, which is a buy-in-town/sell-in-castle gold printer.

The three gates are one decision, not three. §5.3 divides by the same scale §5.2 multiplies by, so
gating the pool leg without the price leg would collapse castle prices by 20× instead of inflating
them 6×. Bringing castles into §4 means lifting all three together.

### 5.1 Town gold

```
countryside = 12 × (40 × Prosperity)
target      = 10000 + countryside + garrisonTrade
change      = round( 0.25 × (target - townGold) )
```

Vanilla's formula term-for-term — a proportional controller closing a quarter of the gap per day,
symmetric, so gold above target is destroyed. The ×12 is vanilla's own coefficient; `TownTreasuryScale`
replaces the raw prosperity it was fed.

`garrisonTrade` is the tally below. It has to enter the *target* rather than arrive as income, because
the controller is what makes plain income meaningless: coin handed to a town already at its target is
destroyed within a few days. Capped at `0.5 × countryside`, so a town stays a place with a
countryside rather than becoming a barracks.

The treasury dial is 40 rather than 20 because a town short of its daily food bill cannot buy its way
back out: an empty market prices food near 269 d/unit against 154 stocked, so falling behind is
self-reinforcing. The larger dial clears that tipping point with margin, and the controller
self-limits because surplus gold is destroyed.

### 5.2 Daily demand

```
p             = 20 × Prosperity
baseline      = max(0, p + extraProsperity)
luxury        = max(0, p - 3000)

demand = (BaseDemand < 1e-8) ? baseline × 0.01
                             : BaseDemand × baseline + LuxuryDemand × luxury
```

`extraProsperity` (the 1000 nudge) and the 3000 luxury threshold are deliberately **not** scaled.

### 5.3 The price path is un-scaled again

`ItemData.Demand` does double duty: it is a gold pool *and* the numerator of

```
priceFactor = ( demand / (0.1 × supply + 0.04 × inStoreValue + 2) ) ^ 0.6
```

which is compared against unscaled physical counts. Feeding the ×20 pool into it would raise every
price by

```
20 ^ 0.6 ≈ 6×
```

so the estimate path is divided back down by 20. The two paths are separable because each has exactly
one caller. Deriving by division rather than rewriting against raw prosperity keeps the 1000 nudge and
the 3000 luxury threshold at the same *relative* size.

---

## 6. Famine

The ration divisor of §0 makes any deficit ten times larger than vanilla's, and vanilla's famine
coefficient was tuned against the old one:

```
vanilla coefficient  0.5   prosperity lost per unit of daily deficit   →  ≈ P/80 = 1.25 %/day
RBM coefficient      0.05                                             →  same proportional severity
```

At vanilla's coefficient the RBM deficit would cost ≈ P/8 = 12.5 %/day and empty a city in a
fortnight. Applied as a delta correction of `deficit × (0.05 - 0.5) = deficit × -0.45`, inside
vanilla's own gates, so the `HelpingHands` perk is counted exactly once and the tooltip shows a single
line. Applies to towns **and** castles.

---

## 7. Trade good values

Trade goods are valued and weighted off historical figures: a period price in denars ×10, and the real
mass in kilograms of one trade lot. Value and weight move together, so a cart of velvet is not worth
what a cart of hardwood is.

Applied at both good-creation sites (XML goods and the code-built grain/meat/iron chain), before item
category averages, initial town stock seeding, and trade AI read them. Gated on
`realisticTradeGoodPrices` (default on) — the one toggle here that is independent of
`rbmCampaignEnabled`. Items outside the table — tools, stolen goods, trash, all non-Goods — are
untouched.

| Good | Value | Weight (kg) | | Good | Value | Weight (kg) |
|---|---:|---:|---|---|---:|---:|
| grain | 60 | 30 | | wool | 800 | 10 |
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
| hardwood | 11 | 200 | | felt | 3200 | 10 |
| charcoal | 3 | 4 | | iron ore | 1 | 4 |

Iron ingot ladder: crude 4 / wrought 11 / iron 22 / steel 40 / fine steel 69 / thamaskene 120,
weights 2, 1, 1, 1, 1, 1.

Weight spans four orders of magnitude (0.025 → 200 kg) and decides what a party can profitably carry,
which is why the inventory screen carries a weight column.

---

## 8. Soldiers as customers

Troop spoils spending (`RBMCampaign/Upkeep/`) buys off the very roster §2 counts as `FoodStocks`, so
an army now physically eats a town toward famine. Three legs connect it to the rest of this document.

**Price.** A stack pays `town.MarketData.GetPrice`, not the flat §7 item value it used to. Scarcity is
therefore visible to troops: a famine-priced town puts its last grain above a recruit's wage ceiling,
so the army goes hungry rather than finishing the stocks off — self-limiting exactly when the town can
least afford the custom. Villages and **castles** keep the flat value; a castle prices on the vanilla
scale (§5), so market-pricing its bread would charge ~6× and starve every garrison holding one.

The price is snapshotted once per party, so a party stripping the shelves pays yesterday's price for
today's shortage. The next customer sees the new one.

**Carousing** is bounded by a hard per-man ceiling of `200 × (Tier + 1)` gold per day, applied to the
wage and over-cap-surplus components together. The pre-existing surplus drain is a *rate* — a quarter
of the surplus per hour — and a rate on a large enough purse is still a fortune: one stack was measured
shedding 110,289 in a single hour, which dominated every other flow into the tally below. That was
tolerable while spoils were a closed sink; it is not once the coin reaches a town. The ceiling sits at
roughly three times the wage-driven rate for the tier, so an over-cap stack still visibly outspends a
saving one.

**Demand.** Every purchase goes through the same `RegisterPurchaseDemand` the town's rations and the
civilian pass use (§2.3), so the `/VanillaProsperityScale` units conversion has one owner. Rough
magnitude: a 100-man party's ~500 g/day of food is `F = 25` against a grain baseline of order 10³ —
a couple of percent. A 1,000-man army parked in town is order 25%. Carousing has no item and so no
category; it takes the gold leg only.

**Treasury.** A decaying per-town tally of what soldiers have spent:

```
on purchase   tally += gold
daily         tally  = round(tally × 0.9)        (half-life ~1 week, window ~10 days)
target term   min( tally × 0.25, 0.5 × countryside )
```

The term reads as *the town's treasury target rises by a quarter of the trade it has taken from
soldiers over the last ten days*, and falls back off within a fortnight of the army leaving.

The quarter is measured, not chosen: tallies run 20k–40k on an ordinary town against a cap of
`240 × Prosperity` that lands at 36k–53k for the prosperities §4 actually produces. At a factor of 1.0
every town with any traffic sat pinned near its ceiling, which makes the term a flat treasury bonus
instead of something a busy town can stand out by. Persisted
under `SyncData` key `RBM_townTroopTrade`; reset in `RBMEconomyCampaignBehavior`'s **constructor**,
which is the only hook that runs before the save is read (see the store-reset ordering note — an
absent key leaves the dictionary untouched, so a null guard never catches a cross-campaign leak, and
settlement `StringId`s are identical between campaigns).

Towns only, on all three legs, matching §5.

Caravans are **not** exempt: their guards read as soldiers to every gate here — troops, drawing a
wage, visiting settlements — so a caravan provisions and carouses like a war party, and pays the town
for it. Villagers are exempt (`SpoilsPool.IsExemptParty`), caravans deliberately are not.

---

## 9. Supporting systems

**Economy log** — `logs/economy/` next to the config, one file per session, capped by
`LogRetention.PruneOldest`. Six categories: `PRODUCE`, `DISPATCH`, `DELIVER`, `FOOD`, `DAILY`,
`PROSPER`. The header echoes `prosperityPerBoundHearth`, `vanillaProsperityScale` and
`townTreasuryScale`; day dividers carry campaign year, day and season (read from
`CampaignTime.GetSeasonOfYear`). The file opens lazily, so the toggle can be flipped mid-session.
Everything on the logging path is short-circuited when it is off — production runs for every village
on the map every day.

**Inventory weight column** — a 52 px `Wt.` column carved out of the item name field, not added to the
row width, displayed as `0.##`. Injected at module load, because Gauntlet caches parsed prefabs before
the campaign starts: the patched prefab XML is written to `%TEMP%/RBM/Prefabs` and the loader
redirected to it, and the generated `Inventory` prefab is bypassed so the XML tree loads at the cost
of that screen's codegen fast path. Requires a restart to take effect.

**Config** — three toggles, all default on: `realisticTradeGoodPrices`, `showInventoryItemWeight`,
`economyLoggingEnabled`. The latter two are additionally gated on `rbmCampaignEnabled`.

---

## 10. Known gaps

- Every rate and coefficient above is **uncalibrated in-game**.
- Castles are vanilla throughout — outside the food rework (§2), the prosperity equilibrium (§4) and
  the market scaling (§5). A castle neither feeds from its countryside nor participates in the
  reworked price system. Bringing them into §4, and then lifting the §5 gates, is the real fix.
- The demand-feedback factor (§2.3) and the treasury dial (§5.1) both push town gold upward and were
  tuned separately; they should be re-paired.
