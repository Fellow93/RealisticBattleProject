# RBM Economy — Settlement Production & Food

Status: in progress on `campaignModule`, uncommitted. Compiles; **not** calibrated in-game.

This document covers the village-production → convoy → town-market → rations chain and the
prosperity/price rescaling that had to follow it. All of it lives in `RBMCampaign` and is gated
behind `rbmCampaignEnabled`.

---

## 0. The change that forces everything else

The per-Hearth production rework lowered the ration divisor:

```
daily food consumption = Prosperity / NumberOfProsperityToEatOneFood
vanilla divisor = 40      RBM divisor = 4
```

A town now eats **10× more food per point of prosperity**. Every other number below is a
consequence of that, of the fact that food became a physical good, or of the prosperity rescale
needed to keep the two consistent.

---

## 1. Village production

### 1.1 Warehouse capacity

Vanilla's sizing formula is kept term for term; only the rate fed into it changes, from a sum over
`VillageType.Productions` to RBM's own good set:

```
capacity = ceil( max(1, totalRatePerHearth × Hearth) × CapacityDays )     CapacityDays = 5
```

It must be sized off the **total** rate, not a per-good average. Every reader of this number compares
it against the sum of the whole item roster:

| Reader | Gate |
|---|---|
| `VillageGoodProductionCampaignBehavior.TickProductions` | `rosterSum < capacity × 1.5` (production halt) |
| `VillagerCampaignBehavior.ThinkAboutSendingItemToTown` | `rosterSum < capacity` (dispatch, §3.3) |
| `FishingPartyCampaignBehavior` (War Sails) | `rosterSum < capacity` |

An averaged capacity was briefly tried, on the reasoning that a village with one dominant staple
should not also get room to hoard five days of every minor good. It is the wrong unit for all three
gates: across the 8–10 goods a village makes it works out to roughly *one* day of actual output, so
production halted before the first day was out and stayed halted for the whole of the convoy's
multi-day round trip — the exact throttle this patch exists to prevent — while also shrinking the
dispatch threshold and near-permanently suppressing fishing parties.

The per-good hoarding concern is real but has no expression here: the game tracks one shared store,
not a per-good allowance. The village tooltip reflects that — per-good `x /day`, then a single
`Warehouse  stored / capacity` line.

### 1.2 Villager party size

Vanilla sums the daily output of only the goods on `VillageType.Productions`, which under RBM is a
stale subset (every village also makes the base set, and specialities were re-tabled). The shape and
the `12 + Hearth/[20..40]` band are kept, but the interpolation is driven by total per-Hearth
throughput:

```
QuietRate = 0.17     (subsistence base set only, ~0.168/Hearth/day)
BusyRate  = 0.50

busyness  = clamp( (totalRatePerHearth - 0.17) / (0.50 - 0.17), 0, 1 )
divisor   = lerp(40, 20, busyness)
partySize = MinimumNumberOfVillagersAtVillagerParty + floor(Hearth / divisor)
```

Rate is used **per-Hearth**, not as an absolute daily figure, because `Hearth` already appears in the
size term — using the absolute would count population twice. Mines and lumber camps sit far above
`BusyRate` and simply peg to the largest party.

### 1.3 Stored units

The store is measured the way the dispatch and production-halt gates measure it: the sum of roster
amounts, not weight and not distinct item types.

### 1.4 When a village produces nothing

Only one gate stops production outright: `VillageState != Normal` (raided or looted), which is what
vanilla expresses by having the model return 0.

In particular **not** `TradeBound == null`. Vanilla's null check guards only the
`initialProductionForTowns` branch — it protects the settlement seeded goods are written *into*, and
the normal branch fills the village's own store with no such test. Hoisting it to the top of the
patch, as RBM briefly did, silently stopped a village producing whenever it had no reachable
non-hostile town, which for a castle village in wartime is the whole war. With vanilla's
`TickFoodProduction` disabled (§1, food is part of the base set now) that left castle food with
nothing upstream of it at all — the delivery chain alone, carrying goods that were never made.

---

## 2. Town food is physical goods

A town's `FoodStocks` is no longer an abstract running total; it is the food actually sitting in the
market roster.

```
FoodStocks = min( foodUnitsInMarket, FoodStocksUpperLimit )
```

The clamp is required: `DefaultSettlementProsperityModel` pays

```
prosperity bonus = ((FoodStocks + FoodChange) - FoodStocksUpperLimit) × 0.1
```

so an unclamped 2,000-grain roster would hand the town **+170 prosperity/day**.

`FoodChange` becomes a *measured* quantity rather than a modelled one:

```
measuredChange = closingFoodUnits - openingFoodUnits - unmetRations
```

split for the tooltip into `change + unmet` ("Market food") and `-unmet` ("Unmet rations").

### 2.1 Consumption

```
households = Prosperity / NumberOfProsperityToEatOneFood        (divisor 4, see §0)
men        = garrisonMembers + militia
soldiers   = men / NumberOfMenOnGarrisonToEatOneFood
```

Militia are charged for the first time — vanilla never fed them. Perk order mirrors vanilla:
under siege `Steward.Gourmet` on soldiers and `Medicine.TriageTent` on rations; always
`Steward.MasterOfWarcraft` on households; then the `FoodConsumption` building effect on the total;
then `RoundRandomized`.

Two behavioural changes beyond the numbers:

- The town **pays market price** for its rations. Vanilla's siege-only path confiscated food for free.
- Items with `BonusToFoodStores` are skipped by generic consumption, so food is only eaten as food.
- Rations are bought **cheapest first**, the same ordering the delivery leg (§3.2) sells in. A ration
  is a ration, so buying the 1,140-denar fish while 60-denar grain sits on the shelf buys the town
  nothing and costs it the difference — and §5.1 sizes the treasury on the assumption that a day's
  rations cost about what a day's rations should. Sorting means the purchase order no longer matches
  the roster order, so the buy is planned into a list and executed against `EquipmentElement` rather
  than index.

### 2.2 Demand feedback

Every ration purchase feeds its gold value back as market demand:

```
DemandFromPurchaseFactor = 1.0
```

That factor is not the whole story — `AddDemand` scales by 0.15 internally and `UpdateSupplyAndDemand`
decays only 15%/day, so a sustained addition of `x` settles at

```
baseline + x / 0.15  ≈  baseline + 6.7x
```

### 2.3 Starvation

Because stocks can no longer sit floored at a modelled value, `IsStarving` is re-derived directly:
if any rations went unmet on the day, `RemainingFoodPercentage = -100`.

The starvation *clock* needs the same treatment, and it is the half that actually bites. The loyalty
penalty fires on `DaysStarving > 14`, and `DaysStarving` is measured from `PartyBase._lastEatingTime`,
which `Town.DailyTick` stamps to now whenever `FoodStocks > 0`. Under a market-backed stock a partial
famine therefore reset its own clock every day for as long as a single grain sat unsold, and could
never reach fourteen. RBM captures the stamp in a prefix and restores it in the postfix on any day
rations went unmet, so the clock runs from the last day the town actually fed everyone.

Castles are excluded from all of §2 and fall through to vanilla.

---

## 3. Village → town delivery

### 3.1 What was measured

Over one campaign day, before the change:

| Measurement | Value |
|---|---|
| Gold spent by towns on villager cargo | 590,000 d |
| Food units actually landed | 1,100 |
| Effective cost per food unit | 538 d |
| Grain's actual value | 60 d |
| Food units refused for lack of town gold | 6,300 |
| Whole-map food demand | 2,400 units/day ≈ 150,000 d as grain |

Culprits: towns bought in roster order, so wool at ~800/unit and warhorses at ~10,000 drained the
purse before food was reached; and among food, fish at ~1,140 bought the same +1 stock as grain at 60.

### 3.2 The new ordering

Lots are sorted **food before non-food; within food, ascending price; otherwise original roster
order**. Per lot:

```
affordable = min( lotAmount, floor(townGold / unitPrice) )
```

Vanilla's pack-animal reserve is preserved unchanged: the cheapest pack animal in cargo is held back
at `0.5 × TotalManCount`.

### 3.3 Convoys

```
MaxConvoysPerVillage = 2                       (vanilla: 1, enforced by a single component slot)
hourly raise chance  = 0.15                    (vanilla)
dispatch threshold   = 0.5 × warehouseCapacity (vanilla gate, RBM fraction)
hearth cost          = max(0, Hearth - (manCount + 1)/2)
```

The second convoy required working around the single native villager-party slot; the slot is
repointed pre-emptively each hour so vanilla's own cleanup does not destroy the extra party.

### 3.4 Escorts

Convoys now travel with militia scaled to what they are carrying:

```
GoldPerEscort  = 400        MaxEscort = 12
desired        = min( floor(cargoValue / 400), 12 )
missing        = desired - existingEscort                 (a target, not a per-trip addition)
missing        = min( missing, floor(Hearth) )

MeleePerRanged = 2
rangedCount    = floor(missing / 3)                       remainder melee

EliteStartValue = 2000      EliteFullValue = 8000
eliteShare      = clamp( (cargoValue - 2000) / 6000, 0, 1 )
eliteCount      = round( missing × eliteShare )

hearth cost     = max(0, Hearth - (missing + 1)/2)        (vanilla's per-villager rate)
```

If the culture lacks one troop class the other takes the whole allocation.

---

## 4. Prosperity as a countryside equilibrium

Town prosperity stops being pulled by vanilla's housing-cost ladder and is instead pulled toward a
share of the hearths of the villages bound to it.

```
ProsperityPerBoundHearth = 0.1
target      = 0.1 × Σ Hearth over trade-bound villages
gap         = target - Prosperity

ConvergenceRate = 0.1                       → time constant ≈ 10 days
delta       = gap × 0.1 - vanillaHousingCosts
```

The rate is a weight, not a speed. Prosperity rests where every term cancels, so any other term
contributing a steady `x`/day parks the fief `x / ConvergenceRate` away from its target. At the
original 0.02 that was a fifty-fold lever — Surplus Food alone (~+11/day) displaced a well-fed town
by ~550, several times the target itself, leaving the countryside a rounding correction rather than
an attractor. 0.1 makes it tenfold, so the same +11 displaces by ~110. The ten-day time constant is
brisker than the seasonal drift originally intended; that is the trade for giving this term enough
weight to argue with its neighbours.

Vanilla's ladder is transcribed and subtracted so the two collapse into one readable tooltip line.
For reference, that ladder is a flat step function of prosperity:

| Prosperity | Term |
|---|---|
| < 250 / 500 / 750 / 1000 / 1250 / 1500 | +6 / +5 / +4 / +3 / +2 / +1 |
| 1500 – 6000 | 0 |
| > 6000 / 9000 / 12000 / 15000 / 18000 / 21000 | −1 / −2 / −3 / −4 / −5 / −6 |

New games seed every town at `target` directly. Loaded saves converge over ~50 days. Castles keep
vanilla prosperity entirely.

Trade-bound hearths are recomputed once per campaign day by walking every village's `TradeBound`
rather than reading `Town.TradeBoundVillages`, which is cached data emptied on load and only
repopulated for castle villages.

---

## 5. Rescaling the vanilla economic models

Prosperity is now on a *household* scale, roughly 1/20 of the number vanilla's economy models expect.
Feeding it in raw would collapse market liquidity, so two separate scale factors are applied:

```
VanillaProsperityScale = 20      (demand)
TownTreasuryScale      = 40      (town gold only)
```

**Towns only** — all three patches in this section. The rescaling exists to compensate for the §4
re-seed, and that re-seed is towns-only: `TargetProsperity` returns 0 for a castle and the
equilibrium postfix skips them, so a castle still carries vanilla-scale prosperity. Rescaling it a
second time was a gold printer — a prosperity-1000 castle targeted a ~490k treasury and priced goods
around 6× a town's, so anything bought in a town sold into a castle for a multiple against a
half-million-denar purse. Castles now fall through to vanilla on every leg, which is at least
self-consistent: vanilla numbers on a vanilla prosperity.

The three gates are one decision, not three. §5.3 divides by the same scale §5.2 multiplies by, so
gating the pool leg without the price leg would collapse castle prices by 20× instead of inflating
them 6×. If castles are ever brought into the countryside model, all three come off together.

### 5.1 Town gold

```
target  = 10000 + 12 × (40 × Prosperity)
change  = round( 0.25 × (target - townGold) )
```

The ×12 is vanilla's own coefficient on the prosperity term and is left alone; `TownTreasuryScale`
(40) is the only thing RBM substitutes, replacing the raw prosperity vanilla fed it.

Vanilla's formula term-for-term — a proportional controller closing a quarter of the gap per day,
symmetric, so gold above target is destroyed. Only the prosperity term is rescaled.

Why 40 and not 20: logs showed towns spending 509k/day against the 645k needed, ~27% short, while an
empty market priced food at 269 d/unit against 154 stocked — a bistable trap where a town that falls
behind can never buy its way out. Doubling the treasury dial clears the tipping point with margin,
and the controller self-limits because surplus gold is destroyed.

### 5.2 Daily demand

```
p             = 20 × Prosperity
baseline      = max(0, p + extraProsperity)
luxury        = max(0, p - 3000)

demand = (BaseDemand < 1e-8) ? baseline × 0.01
                             : BaseDemand × baseline + LuxuryDemand × luxury
```

`extraProsperity` (the 1000 nudge) and the 3000 luxury threshold are deliberately **not** scaled.

### 5.3 The price path must be un-scaled again

`ItemData.Demand` does double duty: it is a gold pool *and* the numerator of

```
priceFactor = ( demand / (0.1 × supply + 0.04 × inStoreValue + 2) ) ^ 0.6
```

which is compared against unscaled physical counts. Feeding the ×20 pool into it raised every price
by

```
20 ^ 0.6 ≈ 6×          (measured: grain at 330 d against a value of 60)
```

The estimate path is therefore divided back down by 20. The two paths are separable because each has
exactly one caller. Deriving by division rather than rewriting against raw prosperity keeps the 1000
nudge and the 3000 luxury threshold at the same *relative* size.

---

## 6. Famine

The 40 → 4 ration divisor makes any deficit ten times larger, and vanilla's famine coefficient was
tuned against the old one:

```
vanilla:  0.5  prosperity lost per unit of daily deficit   →  ≈ P/80  = 1.25 %/day
RBM raw:  same coefficient on a 10× deficit                →  ≈ P/8   = 12.5 %/day   (city dead in ~2 weeks)
RBM new:  0.05 coefficient                                 →  proportional severity restored
```

Applied as a delta correction of `deficit × (0.05 - 0.5) = deficit × -0.45` so the `HelpingHands`
perk is still counted exactly once and the tooltip shows a single line. Applies to towns **and**
castles.

---

## 7. Trade good repricing

All trade goods are re-valued and re-weighted off historical figures: a period price in denars ×10,
and the real mass in kilograms of one trade lot. Value and weight now move together, so a cart of
velvet is not worth what a cart of hardwood is.

Applied at both good-creation sites (XML goods and the code-built grain/meat/iron chain), before item
category averages, initial town stock seeding, and trade AI read them. Gated on
`realisticTradeGoodPrices` (default on); items outside the table — tools, stolen goods, trash, all
non-Goods — are untouched.

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

Weight now spans four orders of magnitude (0.025 → 200 kg) and decides what a party can profitably
carry, which is why the inventory screen gained a weight column.

---

## 8. Supporting changes

- **Economy log** — its own `logs/economy/` folder, one file per session, six categories:
  `PRODUCE`, `DISPATCH`, `DELIVER`, `FOOD`, `DAILY`, `PROSPER`. Day dividers carry campaign year, day
  and season (read from `CampaignTime.GetSeasonOfYear`, not derived from the day number —
  `GetDayOfYear` is 0-based, and deriving it got the first day of every season wrong). Everything on the logging path is
  short-circuited when the toggle is off — production runs for every village on the map every day.
- **Inventory weight column** — a 52 px `Wt.` column carved out of the item name field (not added to
  the row width), injected at module load because Gauntlet caches parsed prefabs before the campaign
  starts. Displayed as `0.##`. Requires a restart to take effect.
- **Config** — three new toggles, all default on: `realisticTradeGoodPrices`,
  `showInventoryItemWeight`, `economyLoggingEnabled`.
- **No new save data.** Nothing here adds a `SyncData` key; all state is rebuilt from the campaign
  objects each session, and the per-day caches are cleared on session start.

---

## 9. Open items

- Every rate and coefficient above is **uncalibrated in-game**.
- Castles are now excluded from the food rework (§2), the prosperity equilibrium (§4) *and* the
  market rescaling (§5) — they are vanilla throughout. That closed the buy-in-town/sell-in-castle
  exploit, but it is a stopgap rather than a model: a castle's economy is simply untouched, so it
  neither feeds from its countryside nor participates in the reworked price system. Bringing castles
  into §4 (and then lifting the §5 gates) is the real fix.
- The demand-feedback factor (§2.2) and the treasury dial (§5.1) both push town gold upward and were
  tuned separately; they should be re-paired.
