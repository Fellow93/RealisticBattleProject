# RBM Campaign — Money: every pool, every flow

Where gold sits, how it moves, and what spoils have to do with any of it.

This is the *money* view of the campaign layer. Two companion documents cover the things money is
spent on: [`economy-production-food.md`](economy-production-food.md) is the goods chain (what
villages make, how it reaches a town, what a town eats), and
[`../RBMCampaign/SPOILS-TECHNICAL.md`](../RBMCampaign/SPOILS-TECHNICAL.md) is the spoils system's own
formulas. Where they and this document disagree about a number, they are the more specific and win.

Everything here lives in `RBMCampaign` and is gated behind `rbmCampaignEnabled`.

---

## 0. The one idea

Vanilla's campaign economy does not conserve money. It is a set of independent controllers, each
dragging its own number toward its own target: a town's gold is pulled toward `10000 + 12 × Prosperity`
and anything above that is deleted; townsfolk buying off the market roster conjure their payment from
nowhere; a lord's payroll is deducted from clan gold and credited to no one. None of these numbers is
money anybody owns. They are floats standing in for an economy.

RBM's direction of travel is to replace those controllers with a **circuit**: a fixed set of purses,
and every movement a debit of one and a credit of another. The circuit is **not yet closed** — §7
names every edge where money still enters or leaves the world — but the pools and the accounting are
in place, and that is what this document describes.

Two things follow from the circuit that are worth holding onto while reading:

- **A "pool" is a real balance somebody owns**, not a level being pulled toward a target. If money
  leaves one, it must arrive somewhere.
- **Spoils are a second currency at par with gold.** One point of spoils is one gold piece. They are
  not a discount coupon or an abstraction — they are a purse, held by soldiers rather than by you, and
  they buy things from the same markets your gold does. §3 is the whole of it.

---

## 1. The pools

Seven places hold money. Everything in §4–§6 is a movement between two of these.

| Pool | Lives in | Who owns it | Persisted by |
|---|---|---|---|
| **Hero gold** | `Hero.Gold` | every hero, player included; "clan gold" is the leader's | vanilla |
| **Party trade gold** | `MobileParty.PartyTradeGold` | caravans and villager convoys | vanilla |
| **Citizen wealth** | `SettlementComponent.Gold` of a **town or castle** | the townsfolk and merchants collectively | vanilla |
| **Settlement treasury** | RBM's own store, keyed by `StringId` | the settlement as a body | `RBM_settlementWealth` |
| **Village purse** | `SettlementComponent.Gold` of a **village** | the village as a body | vanilla |
| **Workshop capital** | `Workshop.Capital` | the shop's owner — **named shops only** | vanilla |
| **Spoils purse** | RBM's own store, per troop **stack** | the soldiers in that stack | `RBM_troopSpoilsGold` |

### 1.1 The two settlement purses

A town or castle holds **two** pots, and which vanilla field backs which is the thing to get right:

- **Citizen wealth is vanilla's `Gold` field**, deliberately. Every vanilla consumer of settlement
  money keeps working untouched — villager and caravan sales gate on it, workshops read it, the
  player's trade screen shows it as the merchant's purse.
- **The treasury is RBM's own store.** Vanilla has no equivalent. It is *not*
  `TradeTaxAccumulated`, which stays exactly what it always was.

**A village has only one purse**, and it is the *treasury*, living in vanilla's `Gold`. A village has
no market to circulate money in, so it has no citizen pot: `HasCitizenPurse` is false for one, and
`GetSettlementWealth` reads `SettlementComponent.Gold` directly. Vanilla's village-gold mechanic —
a flat 1000 dealt at worldgen and clamped back to 1000 every night — is suppressed at the source by
`VillageGoldStock` to free the field, or the day a village is paid would be the day its money is
deleted.

> **Rule for contributors:** go through `SettlementWealth`'s six methods (`Credit`, `Debit`,
> `CreditCitizens`, `DebitCitizens`, and the two getters), never `ChangeGold` directly. Every call
> takes a `Source` string and lands in the daily ledger. A stray `ChangeGold` is money created or
> destroyed outside the books — which is exactly the class of bug §7 exists to track.

### 1.2 What is *not* a pool

**`TradeTaxAccumulated` is a conduit, not a purse.** It is write-only: commissions and tolls
accumulate into it and the clan finance model drains it to the owner. Nothing is ever spent out of it
locally. Treat it as a pipe from a settlement to its lord, not as money the settlement has.

**Prosperity and hearth are not money.** They size things — the treasury seed, production, the tax
take — but they are population and wellbeing, not a balance.

---

## 2. The chain, in one pass

The countryside is the source and the town is the exchange. One full circuit:

```
village hearths ──produce──> goods on the village's shelf
        │
        │  a convoy loads them and walks to its bound town
        ▼
town citizen wealth ──pays the convoy──> convoy PartyTradeGold
        │                                        │
        │                                        │ the convoy walks home
        │                                        ▼
        │                            village purse (its takings, less
        │                            the owner's cut via TradeTaxAccumulated)
        │
        ├──townsfolk eat and buy──> (goods leave the shelf; money stays in town)
        ├──market fee / tariff────> town treasury
        ├──wealth tax────────────> owner hero gold
        └──ransoms, workshop wages, construction, admin pay ──> back to citizens
        ▲
        │
        └── soldiers spend here: rations, drink, surgery, keepsakes, kit
                    ▲
                    │ paid out of SPOILS, which were filled by
                    │ wages (clan gold), battlefield loot and plunder
```

Read that middle-left column as the town's money going out to the land and coming back over its own
counters. The soldier arrow at the bottom is the one that does not balance — see §3.3 and §7.

---

## 3. Spoils, and what they do to the economy

### 3.1 What a spoils purse is

Every stack of identical troops in every party — yours and every AI lord's — has a hidden purse
denominated in gold, keyed `party.Id + "#" + character.StringId`. "40 Imperial Recruits in party X" is
one purse; the same troop in party Y is a different one. It lives in a side dictionary because
`TroopRosterElement` is a struct with no spare field.

**One point of spoils is one gold piece.** There is no exchange rate and no conversion anywhere in the
code. The distinction is *whose pocket it is in*, not what it is worth.

### 3.2 What fills it

| Source | Where the money comes from | Conserving? |
|---|---|---|
| **Daily wage** | the stack's **whole** daily wage is deposited | ⚠️ see §3.3 |
| **Battlefield loot** | kit stripped off the fallen, at 25–75% of item value | new value, from a battle |
| **Raid plunder** | a share of a sacked village's wealth | new value, from a raid |
| **Siege plunder** | a share of a stormed town's prosperity | new value, from a sack |
| **Recruit seed** | `RecruitMaintenanceDays` of upkeep in a fresh recruit's purse | conjured |

### 3.3 The wage deposit is the economy's largest single flow — and its largest open edge

This is the most important thing in this document, so it gets stated plainly.

Vanilla deducts a party's wage bill from clan gold every day and **credits it to nobody**. That is
already a pure sink, and RBM does not change it — the clan still pays, and that gold is still
destroyed where it always was.

RBM then *separately* mints an equal sum of spoils into the men's purses. The code's own framing is
that this "only says where the pay went" — the party's gold is untouched by the deposit itself. But
the two halves do not cancel in the same place:

- Clan gold goes **down** by the wage, and nothing receives it.
- Spoils go **up** by the wage, from nothing.
- The men then spend those spoils in towns, and `TroopMarketFeedback` credits that spending to
  **citizen wealth** — real money, in a real pool, that can be taxed, tariffed and spent onward.

So in aggregate the world's money supply is roughly preserved, but the *route* is fictional: money
teleports from lords' treasuries into town markets without any transfer between them. Whether that is
a bug or the intended "soldiers spend their pay in town" depends on whether the lord's gold was meant
to be the source. As the ledger stands, it is the dominant faucet — `economy-production-food.md` §13
names it as such.

### 3.4 What drains it

| Sink | Goes to | Conserving? |
|---|---|---|
| **Upgrades** | the supply town's citizen wealth, market fee and all (`UpgradeSupply`) | ✅ |
| **Field maintenance** | the supplying town's citizen wealth; kit worth the day's wear leaves its shelves | ✅ |
| **Food** | the settlement's purse, at that settlement's real prices, for real items off its stock | ✅ |
| **Carousing** | the settlement's citizen wealth (`Source.Carousing`) | ✅ |
| **Paid healing** | the settlement's citizen wealth (`Source.Surgery`) | ✅ |
| **Luxuries** | the settlement's citizen wealth; the good is a keepsake, not resellable party loot | ✅ |
| **The leader's cut** | the party leader's **gold** | ✅ — see below |

**The leader's cut is the only spoils→gold exit.** Before a gather settles into the stacks, the party
leader skims a share (base × clan tier + 1) into his own purse as gold. It is conserving: the cut is
drawn back out of the very purses the gather just filled, so no coin is minted — it moves from the
men's pool to their keeper's treasury. Nothing else in the system ever hands spoils back as gold; in
particular, **surplus over the spoils cap does not return to you** — it is drunk and eaten where the
men stand, which credits that settlement.

### 3.5 Who pays the shortfall

Maintenance is met from the men's purses first, and what they cannot cover falls to the party leader's
gold — folded into the clan's daily gold change, so it appears in the Daily Gold Change message and the
finance breakdown, through the same channel wages run through.

How much the men are expected to find themselves depends on the clan's contract state:

| Contract | Share met from the men's purses | Dial |
|---|---|---|
| Independent (no kingdom) | all of it | `IndependentMaintenancePurseFraction` (1.0) |
| Mercenary (under contract) | half; the employer bears the rest | `MercenaryMaintenancePurseFraction` (0.5) |
| Sworn vassal or ruler | none — the liege bears the whole bill | not configurable |

---

## 4. Settlement income

Everything that puts money **into** a settlement's purses. The `Source` column is the string the
ledger records it under, which is what you grep the economy log for.

### 4.1 Into citizen wealth (towns and castles)

| Flow | From | Source | File |
|---|---|---|---|
| Counter trade — a party sells to the town | the party's own gold or trade purse | `Trade` | `NativeTradeConservation` |
| Soldier spending — rations, drink, surgery, keepsakes | spoils purses | `TroopGoods`, `Carousing`, `Surgery` | `TroopMarketFeedback` |
| Troop upgrades | spoils + the lord's gold | `Upgrade` | `UpgradeSupply` |
| Field maintenance | spoils + the lord's gold | `Maintenance` | `SpoilsPool.Maintenance` |
| Recruitment | the recruiter's gold | `Recruit` | `RecruitSupply` |
| Workshop wages — named shops only | the shop's capital | `WorkshopWages` | `WorkshopPurse` |
| Construction labour | the town treasury | `Construction` | `ConstructionLabour` |
| Administrative pay | the town treasury | `Admin` | `AdministrativeUpkeep` |
| Dearth advance | the town treasury | `Dearth` | `VillagerDelivery` |
| Worldgen seeding | nowhere — deliberate, once | `Seed` | `SettlementGoldFunnel` |

### 4.2 Into the treasury

| Flow | From | Source |
|---|---|---|
| Market fee on a trade | citizen wealth | `Tariff` |
| Market fee on the artisans' materials — their only money movement | citizen wealth | `Tariff` |
| Stall commission | citizen wealth | `Commission` |
| Construction repayment | citizen wealth | `Construction` |
| Wealth tax — the fief's own levy | citizen wealth | `WealthTax` |
| Worldgen seeding | nowhere — deliberate, once | `Seed` |

### 4.3 Into a village purse

| Flow | From | Source |
|---|---|---|
| A convoy comes home with its takings | the town's citizen wealth, via the convoy | `Homecoming` |
| Stall commission on a village sale | the trading party | `Commission` |

---

## 5. Settlement outgo

### 5.1 Out of citizen wealth

| Flow | To | Source |
|---|---|---|
| Buying a convoy's load | the convoy's `PartyTradeGold` | `Delivery` |
| Counter trade — the town buys from a party | the party | `Trade` |
| Market fee / tariff | the treasury | `Tariff` |
| Stall commission | the treasury, then the owner via `TradeTaxAccumulated` | `Commission` |
| Wealth tax | the **owner hero's gold** at 0.00027/day, and the treasury at 0.00014/day | `WealthTax` |
| Ransoms | the ransomed hero | `Ransom` |

### 5.2 Out of the treasury

| Flow | To | Source | Conserving? |
|---|---|---|---|
| Garrison wage share | **nobody** | `GarrisonWage` | ❌ §7 |
| Militia stipend | the militia's own purses | `Militia` | ✅ |
| Administrative pay | citizen wealth | `Admin` | ✅ |
| Construction labour | citizen wealth | `Construction` | ✅ |
| Garrison food | citizen wealth | `GarrisonFood` | ✅ |
| Dearth advance | citizen wealth | `Dearth` | ✅ |

---

## 6. Clan and party money

### 6.1 Hero and clan gold — in

| Flow | From |
|---|---|
| Fief tax income | the settlement's `TradeTaxAccumulated` — a real drain on it |
| Wealth tax | citizen wealth of a fief he owns — his own 0.00027/day levy only; the fief's 0.00014 is separate and stays home |
| Caravan payouts | the caravan's own `PartyTradeGold`, debited by exactly what he receives |
| The leader's cut | his men's spoils purses |
| Selling goods | the buyer's purse |
| Quest and issue rewards, tournaments, ransoms taken | **nowhere** — vanilla, see §7 |

### 6.2 Hero and clan gold — out

| Flow | To |
|---|---|
| Party wages | **nobody** — vanilla's largest sink, unchanged by RBM |
| Garrison wages (the owner's remaining share) | **nobody** |
| Maintenance shortfall | the supplying town's citizen wealth |
| Troop upgrades | the supply town's citizen wealth |
| Recruitment | the settlement's citizen wealth |
| Buying goods | the seller's purse |

### 6.3 Party trade gold

Caravans and villager convoys hold their own purse and are genuine intermediaries, not conduits:

- A **convoy** is paid by the town's citizen wealth for its load, carries the money home, and hands
  it to the village purse less the owner's cut. Its takings exist as a real balance the whole way.
- A **caravan** trades on its own capital. Its payout to the owner debits `PartyTradeGold` by exactly
  what the clan receives — the two are the same number by construction.

---

## 7. Where money is still created or destroyed

The honest list. Sorted by size.

### Still open

| Edge | Direction | Where | Note |
|---|---|---|---|
| **Party wages** | destroyed | vanilla | Deducted from clan gold daily, credited to nobody. The largest sink on the map. |
| **The wage→spoils deposit** | conjured | `SpoilsPool.Wages` | §3.3. The largest faucet, and the mirror of the line above — they roughly cancel in aggregate but not in place. |
| Quest and issue rewards | both | vanilla | ~60 files in `CampaignSystem` use a null-participant `GiveGoldAction`: issue payouts, crime fines, bribes, incidents. |
| Tournament prizes and betting | both | vanilla | |
| Garrison wage share | destroyed | `GarrisonUpkeep` | RBM moves a quarter of the bill from clan gold onto the fief's treasury, but the debit still credits nobody. It relocates the sink rather than closing it. |
| Recruit upkeep seed | conjured | `SpoilsPool` | `RecruitMaintenanceDays` of upkeep appears in a fresh recruit's purse. |
| Village admin salary | destroyed | `AdministrativeUpkeep` | Deliberate, and structural: a village has no citizen pot for the wage to land in, so it leaves the purse into the untracked household economy. Up to 100/day, capped at what the purse holds — a ceiling, not a rate. A town's equivalent is conserving, since its officials are townsfolk and the wage lands back in citizen wealth. |
| Perk-based tax mints | conjured | vanilla | `Tollgates`, `TravelingRumors`, Naval `Salvage` add straight to `TradeTaxAccumulated` with no counterparty. Small. |
| Worldgen seeding | conjured | `SettlementGoldFunnel` | Deliberate, once. `Town.OnInit` deals every town 20,000 through the same `ChangeGold` as everything else; the funnel books it as `Source.Seed` so it is not counted as a trade or charged a market fee. |

### Clamps that swallow a shortfall

Most callers credit exactly the figure the mover returned, which makes the pairing exact by
construction. Three do not, and conjure the difference when a purse runs dry mid-transaction:

1. the delivery sale pays the convoy before debiting the market, and discards the debit's return;
2. the commission path credits the owner's tax ledger the full figure regardless of what the market
   could actually pay;
3. the garrison wage share lowers the owner's bill by a **pre-read** balance rather than by what was
   actually withdrawn.

Separately, provisioned rations leave the roster unconditionally, so a fief broke on both purses feeds
its garrison free — destroying physical value rather than gold.

### Closed, and how

Worth knowing so they are not "fixed" twice:

- **Leaderless-party stall trades.** `GiveGoldAction.ApplyInternal` silently skips a null participant,
  so a villager, bandit or garrison party buying from a town paid nothing (town credited from thin
  air) and selling to one was paid nothing (town's money destroyed). `NativeTradeConservation` supplies
  the missing counterparty's own purse.
- **Village stall commission.** Vanilla's accumulate step is gated on `Town != null`, so a village sale
  destroyed the entire commission. Same file.
- **Unbooked writes to settlement gold.** Measured at Danustica over eleven days: the ledger accounted
  for +120,999 while the balance moved +87,749 — a hidden drain of ~2,500 a day, negative every single
  day. `SettlementGoldFunnel` now catches `ChangeGold` itself, so every path in or out lands in the
  funnel whether or not anyone wrote a wrapper for it.
- **Civilian purchases off the town's own market.** Vanilla's `ItemConsumptionBehavior.MakeConsumption`
  credits the town for every household purchase even though the townsfolk have no purse to pay from —
  vanilla's single largest manufactured-money source. Both legs are reimplemented and neither credits
  anything: the goods leg in `RBMTownFoodSupply.MakeConsumptionPatch`, the food leg in
  `BuyFoodFromMarket`. Under the two-purse ledger a townsman paying a merchant is a move *inside*
  citizen wealth, so the pot is unchanged and the goods are simply eaten. The market fee on those sales
  is still levied — deliberately, and it conserves: citizens are debited and the treasury credited.
  Garrison and administrative rations are the one leg where money genuinely crosses, and it now runs
  the right way (treasury → citizens) where it once credited the town, making a bigger garrison enrich
  the fief that fed it.
- **Recruitment gold**, which vanilla destroys in full on every path, now reaches the settlement.
- **Market-funded ransoms**, via `RansomFunding`.
- **Caravan payouts**, via `CaravanCapital`.
- **Upgrade cost.** Both the gold billed to the lord and the spoils drawn from the men now reach a
  town. A party with no hero to bill still hands over its spoils leg, and a party that can reach no
  friendly town pays a fence rather than burning the coin.

---

## 8. Watching it happen

| Log | Toggle | Contents |
|---|---|---|
| `logs/economy/` | `EconomyLoggingEnabled` | Village production, convoy dispatches and deliveries, town rations, and each settlement's daily wealth state — every ledger line with its `Source`. |
| `logs/campaign/` | `SpoilsLoggingEnabled` | Every spoils movement: loot distribution, wage deposits, upgrade pricing and supply, food, carousing, the leader's cut. `SpoilsVerboseLoggingEnabled` adds per-stack detail. |
| In-game tooltips | — | A settlement's hover panel shows both purses; the clan finance screen carries a maintenance line; the party wage tooltip shows the day's maintenance beside the wage (display only — it never touches the charge). |

To audit conservation for one settlement, take a day's economy log, sum the credits and debits by
`Source`, and compare against the balance delta. A gap is either one of §7's known edges or a new
`ChangeGold` that skipped the funnel.

---

## 9. The dials that move money

Grouped by what they actually change. Full descriptions in
[`../RBMCampaign/README.md`](../RBMCampaign/README.md#tuning-it).

| Dial | Moves |
|---|---|
| `TroopUpgradeCostMultiplier` | The size of the upgrade flow. **0 disables the entire spoils system**, and with it every flow in §3. |
| `TroopMaintenanceFraction` | The daily spoils→town flow, and the clan-gold shortfall behind it. |
| `Mercenary` / `IndependentMaintenancePurseFraction` | Who pays that bill — the men or their leader. |
| `TroopSettlementFunWageFraction` | The carousing flow. Historically the single largest money-into-town term; a quarter of a day's wage now, down from one and a half. |
| `TroopLeaderSpoilsCutFraction` | The one spoils→gold exit. |
| `TroopSpoilsCapDays` | How long a stack saves before its surplus goes to drink — i.e. how much of the spoils supply sits idle rather than reaching towns. |
| `RecruitMaintenanceDays` | The size of the recruit-seed faucet (§7). |
| `TroopRaidSpoilsMultiplier`, `TroopUpgradeSpoilsLootMultiplier`, `TroopFallenSpoilsCaptureFraction` | How much new value a battle or a sack injects. |
| `RealisticTradeGoodPrices` | What everything is worth, and so the magnitude of every trade flow. |

Troop **wages** have no dial. They are a fixed per-tier table
(`RBMCampaign/Wages/TierBasedWageModel.cs`) and apply whenever the module is on. Since the wage is
both the largest clan sink and the source of every spoils purse, that table is the scale factor on
most of §3 — change it and everything downstream moves with it.
