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

Eight places hold money. Everything in §4–§6 is a movement between two of these.

| Pool | Lives in | Who owns it | Persisted by |
|---|---|---|---|
| **Hero gold** | `Hero.Gold` | every hero, player included; "clan gold" is the leader's | vanilla |
| **Party trade gold** | `MobileParty.PartyTradeGold` | caravans and villager convoys | vanilla |
| **Citizen wealth** | `SettlementComponent.Gold` of a **town** | the townsfolk and merchants collectively | vanilla |
| **Settlement treasury** | RBM's own store, keyed by `StringId` (towns and castles) | the settlement as a body | `RBM_settlementWealth` |
| **Village purse** | `SettlementComponent.Gold` of a **village** | the village as a body | vanilla |
| **Construction reserve** | `Town.BoostBuildingProcess` | the fief's building fund | vanilla |
| **Workshop capital** | `Workshop.Capital` | the shop's owner — **named shops only** | vanilla |
| **Spoils purse** | RBM's own store, per troop **stack** | the soldiers in that stack | `RBM_troopSpoilsGold` |

### 1.1 The settlement purses

A **town** holds **two** pots, and which vanilla field backs which is the thing to get right:

- **Citizen wealth is vanilla's `Gold` field**, deliberately. Every vanilla consumer of settlement
  money keeps working untouched — villager and caravan sales gate on it, workshops read it, the
  player's trade screen shows it as the merchant's purse.
- **The treasury is RBM's own store.** Vanilla has no equivalent. It is *not*
  `TradeTaxAccumulated`, which stays exactly what it always was.

**A castle has only one purse — its treasury** (`CastleEconomy`). It runs no market, so
`HasCitizenPurse` is false for it, and everything that would credit a castle's "citizens" — its
garrison's carousing and purchases above all — lands in the treasury instead. A castle's vanilla
`Gold` field is left to vanilla and sits outside the ledger: the funnel declines to route it.

**A village has only one purse**, and it is the *treasury*, living in vanilla's `Gold`. A village has
no market to circulate money in, so it has no citizen pot: `HasCitizenPurse` is false for one, and
`GetSettlementWealth` reads `SettlementComponent.Gold` directly. Vanilla's village-gold mechanic —
a flat 1000 dealt at worldgen and clamped back to 1000 every night — is suppressed at the source by
`VillageGoldStock` to free the field, or the day a village is paid would be the day its money is
deleted.

**Opening balances (new campaigns only).** A town's treasury is seeded at 210 per point of
(countryside-target) prosperity, a castle's at 100 per point of its own prosperity, a village's purse
at 150 per hearth, and a town's citizen wealth at vanilla's own yardstick, `10000 + 12 × prosperity ×
40` (`RBMProsperityEquilibrium.TownTreasuryScale`). A loaded save that was never created with RBM
Campaign on cannot be seeded after the fact and warns on load (`RBM_campaignSeeded`).

> **Rule for contributors:** go through `SettlementWealth`'s six methods (`Credit`, `Debit`,
> `CreditCitizens`, `DebitCitizens`, and the two getters), never `ChangeGold` directly. Every call
> takes a `Source` string and lands in the daily ledger. A stray `ChangeGold` on a town or village is
> caught by `SettlementGoldFunnel` and booked as a `Trade` — charged a market fee, and with the
> counterparty still yours to supply. Without one it is money created or destroyed outside the books,
> which is exactly the class of bug §7 exists to track.

### 1.2 What is *not* a pool

**`TradeTaxAccumulated` is a conduit, not a purse.** It is write-only: commissions and tolls
accumulate into it and the clan finance model drains it to the owner. Nothing is ever spent out of it
locally. Treat it as a pipe from a settlement to its lord, not as money the settlement has. Under RBM
a **town's** commission rate is zero (`TradeTariff` zeroes `GetTownTaxRatio`), so what still flows
through it is a village's convoy and stall commission and the perk mints listed in §7.

**The owner's pending wealth-tax income** (`RBM_pendingWealthTaxIncome`) is money in flight, not a
pool: the levy leaves the market on the settlement's daily tick and is handed to the lord on his
clan's next finance pass (`SettlementIncomeFinanceLine`). It is saved so a save taken in between
loses nothing.

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
        │                            village purse (half its takings; the
        │                            other half to the owner via TradeTaxAccumulated)
        │                                        │
        │◄──village shopping, recruit & militia kit──┘
        │
        ├──townsfolk eat and buy──> (goods leave the shelf; money stays in town)
        ├──market fee / tariff────> town treasury
        ├──wealth tax────────────> owner hero gold, and the town treasury
        ├──ransoms───────────────> the lord selling the prisoners
        └◄─workshop wages, construction labour, admin pay, the mint ── back to citizens
        ▲
        │
        └── soldiers spend here: rations, drink, surgery, keepsakes, upgrades, mending
                    ▲
                    │ paid out of SPOILS (filled by wages, battlefield loot and plunder)
                    │ and out of the lord's gold where the purses fall short
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
| **Daily wage** | the stack's **whole** daily wage is deposited — **twice** it for a mercenary company under contract (the second wage is charged to the company and reimbursed by its employer, `MercenaryContractPay`) | ⚠️ see §3.3 |
| **Garrison wage** | a garrison banks its wage the same way; the fief's treasury pays that wage (§5.2) | ⚠️ as §3.3 |
| **Militia wage** | a quarter of a soldier's wage in a town, a tenth in a castle, **nothing** in a village — drawn from the settlement's pot into the purse (`MilitiaUpkeep`) | ✅ only what the pot paid is banked |
| **Battlefield loot** | kit stripped off the fallen, at 25–75% of item value, split by contribution | new value, from a battle |
| **Fallen enemy purses** | `TroopFallenSpoilsCaptureFraction` (0.75) of the beaten side's killed-and-wounded share of their stacks' purses | ✅ a transfer; the rest is lost |
| **Raid plunder** | the raided village's purse, drained in proportion to the looting; **half** of what is drained becomes spoils, the rest is destroyed | ✅ a transfer, with destruction |
| **Siege drain** | 5% a day of a besieged castle's treasury or a town's citizen wealth; half to the besiegers, half destroyed | ✅ a transfer, with destruction |
| **Sack** | on the aftermath choice: Show Mercy takes 10% of the pot and keeps half; Pillage 50% and keeps half (plus 40% of the market's goods); Devastate 90% and keeps a tenth (plus 90% of the goods). A town is sacked from citizen wealth, its treasury passing to the new owner; a castle from its treasury | ✅ a transfer, with destruction |
| **Ransom kit** | the arms and armour stripped off prisoners sold or ransomed, at their kit value | new value |
| **Abandoned prisoners** | half the kit value of captives left on the post-battle loot screen | new value |
| **Recruit seed** | `RecruitMaintenanceDays` of upkeep in a fresh recruit's purse | conjured |

A companion in the party collects his share of any of these straight into the payee's **gold**
rather than a purse, with no leader's cut taken from it.

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

A garrison has the same shape with a different payer: its wage is debited from the fief's treasury
(owner and burghers as backstops, §5.2), credited to nobody, and minted afresh into the garrison's
purses — which its men then spend in the very settlement that paid them.

So in aggregate the world's money supply is roughly preserved, but the *route* is fictional: money
teleports from lords' treasuries into town markets without any transfer between them. Whether that is
a bug or the intended "soldiers spend their pay in town" depends on whether the lord's gold was meant
to be the source. As the ledger stands, it is the dominant faucet — `economy-production-food.md` §13
names it as such.

### 3.4 What drains it

Everything a stack spends in a settlement lands in that settlement's own purse
(`TroopMarketFeedback.CreditLocalPurse`): citizen wealth in a town, the single purse of a castle or a
village. A town also takes its market fee on it on the way in.

| Sink | Goes to | Conserving? |
|---|---|---|
| **Upgrades** | the supply town's citizen wealth, market fee and all (`UpgradeSupply`); value-matched kit leaves its shelves | ✅ |
| **Field maintenance** | **nowhere** — the men's share is simply drained. Only the leader's gold shortfall reaches a town (§3.5); nothing leaves any shelf, maintenance being labour | ❌ destroyed |
| **Militia maintenance** | the town that mends the watch's kit — the purse pays first, the settlement's pot the rest | ✅ |
| **Food** | the local purse, for real items off its stock — at the town's market price in a town, at flat item value elsewhere | ✅ |
| **Carousing** | the local purse (`Source.Carousing`); in a town half of it buys tavern fare (beer, wine, meat, cheese, fish, grapes) off the shelves | ✅ |
| **Paid healing** | the local purse (`Source.Surgery`) | ✅ |
| **Luxuries** | the local purse; the good is a keepsake, not resellable party loot | ✅ |
| **Death** | a fallen man's share of his stack's purse falls with him; a wiped stack's comrades recover half of it, and the victors strip the beaten side's (§3.2) | ❌ partly destroyed |
| **The leader's cut** | the party leader's **gold** | ✅ — see below |

**The leader's cut is the only spoils→gold exit.** Before a gather — battlefield loot, plunder, a
sack, a ransom's kit — settles into the stacks, the party's payee skims a share into his own purse as
gold: `TroopLeaderSpoilsCutFraction` × (clan tier + 1), ×1.5 while his clan holds a mercenary
contract, and ×(1 + 0.003 × Roguery). It is conserving: the cut is drawn back out of the very purses
the gather just filled, so no coin is minted — it moves from the men's pool to their keeper's
treasury. (A party of nothing but heroes, with no purse to draw from, has its leader's cut minted
straight to him off the whole pot.) Nothing else ever hands *purse* spoils back as gold; in
particular, **surplus over the spoils cap does not return to you** — it is drunk and eaten where the
men stand, which credits that settlement. The cap is `TroopSpoilsCapDays` (20) × the stack's daily
wage plus daily maintenance.

### 3.5 Who pays the shortfall

Maintenance — `TroopMaintenanceFraction` (0.005) of each man's whole kit value, horse and harness
included, per day — is charged once per clan per day over the clan's war parties (caravans pay their
own way). It is met from the men's purses first, and what they cannot cover falls to the party
leader's gold — folded into the clan's daily gold change, so it appears in the Daily Gold Change
message and the finance breakdown, through the same channel wages run through. That gold leg, and only
it, is paid over to a town as `Maintenance`: the one the party stands in, or else the nearest town of
a faction it is not at war with (`SpoilsPool.Maintenance`).

How much the men are expected to find themselves depends on the clan's contract state:

| Contract | Share met from the men's purses | Dial |
|---|---|---|
| Independent (no kingdom) | all of it | `IndependentMaintenancePurseFraction` (1.0) |
| Mercenary (under contract) | all of it — the men are paid double, so their spoils meet it first; only a genuine shortfall falls to the leader | `IndependentMaintenancePurseFraction` (1.0) |
| Sworn vassal or ruler | none — the liege bears the whole bill | not configurable |

---

## 4. Settlement income

Everything that puts money **into** a settlement's purses. The `Source` column is the string the
ledger records it under, which is what you grep the economy log for.

### 4.1 Into citizen wealth (towns only)

| Flow | From | Source | File |
|---|---|---|---|
| Counter trade — a party sells to the town, or a workshop is paid for its output | the party's own gold or trade purse; the shop's capital | `Trade` | `SettlementGoldFunnel`, `NativeTradeConservation` |
| A ship bought at the port | the buyer (charged by vanilla) | `Trade` | `ShipTradeFunnel` |
| Soldier spending — rations, drink, surgery, keepsakes | spoils purses | `TroopGoods`, `Carousing`, `Surgery` | `TroopMarketFeedback` |
| Troop upgrades | spoils + the lord's gold | `Upgrade` | `UpgradeSupply` |
| Field maintenance | the lord's gold shortfall only | `Maintenance` | `SpoilsPool.Maintenance` |
| Garrison maintenance | the fief's treasury, then the owner (unlimited wage limit only), then the town's own burghers | `Maintenance` | `GarrisonUpkeep` |
| Militia maintenance | the militiamen's purses, then their settlement's pot | `Militia` | `MilitiaUpkeep` |
| Recruitment — the recruit price | the recruiter's gold | `Recruit` | `RecruitSupply` |
| A village or castle buying kit off the town | the village purse / castle treasury | `VillageArms`, `CastleArms` | `RecruitSupply`, `MilitiaUpkeep` |
| Village shopping — a village spending its surplus on finished goods | the village purse | `VillageDemand` | `VillageShopping` |
| Workshop salary and overhead — named shops only | the shop's capital (overhead: the player's gold, for an under-capitalised player shop) | `WorkshopWages` | `RBMWorkshopExpense` |
| Garrison recruit kit | the town treasury | `GarrisonRecruit` | `GarrisonRecruitCost` |
| Construction labour — half of each wage-paid point | the construction reserve (funded daily from the treasury) | `Construction` | `Construction` |
| Building materials and tools | the construction reserve | `BuildMaterials`, `ConstructionTools` | `ConstructionMaterials` |
| Administrative pay | the town treasury | `Admin` | `AdministrativeUpkeep` |
| Dearth advance | the town treasury | `Dearth` | `VillagerDelivery` |
| The mint — the coin struck from silver ore, less the cuts | new money, from ore consumed off the market | `Minting` | `Minting` |
| A notable's surplus over his 10,000 band | the notable's gold | `NotableWealth` | `NotableWealth` |
| Supply-caravan sale at the source town; caravan investment and repayment | another town's citizen wealth | `Caravan`, `CaravanInvest`, `CaravanRepay` | `RBMCaravanArrival`, `RBMCaravanInvestment` |
| Militia kit refund — a watch shed for want of pay — only with the recruit-supply draw off; with it on, a town's disbanded men put their kit back on its shelves and no coin moves | the militia's own kit, returned to the pot that armed it | `Militia` | `MilitiaUpkeep` |
| Worldgen and new-campaign seeding | nowhere — deliberate, once | `Seed` | `SettlementGoldFunnel`, `SettlementWealth` |

### 4.2 Into the treasury (towns and castles)

| Flow | From | Source |
|---|---|---|
| Market fee on a trade — 1%, plus the Guard House's +0.3/0.6/1.0 points on trade by outsiders, all × the Marketplace's 1.1/1.2/1.3 | citizen wealth | `Tariff` |
| Market fee on the artisans' materials — their only money movement | citizen wealth | `Tariff` |
| Wealth tax — the fief's own levy | citizen wealth | `WealthTax` |
| The mint's 1% cut | new money, from ore | `Minting` |
| Prison labour — 30 a day per prisoner in the cells | nowhere | `PrisonLabour` |
| A castle's income — 41 a day per point of prosperity (× the Craftsman Quarters' 1.1/1.2/1.3) | nowhere | `CastleIncome` |
| A castle's soldiers spending in it | spoils purses | `TroopGoods`, `Carousing`, `Surgery` |
| A castle's building day — half of every coin its reserve spends comes back | the construction reserve | `Construction` |
| Worldgen seeding | nowhere — deliberate, once | `Seed` |

### 4.3 Into a village purse

| Flow | From | Source |
|---|---|---|
| A convoy comes home with its takings — the village keeps **half** of the commission vanilla would hand the owner | the town's citizen wealth, via the convoy | `Homecoming` |
| The recruit price of men raised there | the recruiter's gold | `Recruit` |
| Soldiers spending in the village | spoils purses | `TroopGoods`, `Carousing`, `Surgery` |
| A stall sale at the village — but the whole of it leaves again at once as commission to the owner (vanilla's village rate is 1.0) | the trading party | `Trade`, then `Commission` out |
| Militia kit refund | pulled back out of the town that sold the kit | `Militia` |
| New-campaign seed — 150 per hearth | nowhere — deliberate, once | `Seed` |

---

## 5. Settlement outgo

### 5.1 Out of citizen wealth

| Flow | To | Source |
|---|---|---|
| Buying a convoy's load | the convoy's `PartyTradeGold` | `Delivery` |
| Counter trade — the town buys from a party, or buys a workshop's materials back from it | the party; the shop's capital | `Trade` |
| A ship sold to the port | the seller, as far as the market can cover it | `Trade` |
| Market fee / tariff | the treasury | `Tariff` |
| Wealth tax | the **owner hero's gold** at 0.00027/day and the treasury at 0.00014/day of the whole balance; once the market holds more than 1000 per point of prosperity, a flat 10% a day of the **excess** to each instead (up to half of that hoard levy first repays any supply-caravan investment the town owes). × the Tax Office's 1.05/1.10/1.15 | `WealthTax` |
| Ransoms — prisoners sold at the town | the lord selling them, as far as the market can pay | `Ransom` |
| The town's militia — wage and maintenance shortfall (each new man's kit is taken straight off the town's shelves with no coin moving; only with the recruit-supply draw off is it a citizen-wealth debit to nobody) | the militiamen's purses; the mending town | `Militia` |
| Garrison subsidy — the wage, maintenance or promotion the treasury and owner could not cover, out of what the market holds above 50,000 | the garrison's bill | `GarrisonSubsidy` |
| Arming the town's own volunteers when they first step forward — recovered when a lord pays to muster them | nobody (the kit leaves the shelves) | `TownArms` |
| Siege drain and sack | the besiegers' spoils, half or less; the rest destroyed | `Siege`, `Sack` |
| A notable's refill below his band | the notable's gold | `NotableWealth` |
| Supply caravans — buying a caravan's cargo; investing in a struggling town; repaying | the source town's citizen wealth | `Caravan`, `CaravanInvest`, `CaravanRepay` |

### 5.2 Out of the treasury

| Flow | To | Source | Conserving? |
|---|---|---|---|
| Garrison wage — the fief pays the **whole** bill first; the owner covers what the treasury cannot, then the town's burghers | **nobody**; the garrison's purses are minted the equal sum (§3.3) | `GarrisonWage` | ❌ §7 |
| Garrison maintenance — a quarter of a field troop's, less the Castellan's Office's 10/20/30% on mounted men and the Fortifications' 0/5/10% | the mending town's citizen wealth | `Maintenance` | ✅ |
| Garrison recruit kit — the garrison growing off the fief's wealth | citizen wealth in a town; **nobody** in a castle | `GarrisonRecruit` | town ✅ / castle ❌ |
| A town's militia, once citizen wealth runs dry; a castle's militia wage (a tenth), maintenance and kit | the militiamen's purses; the mending town; the town that sells the kit | `Militia`, `CastleArms` | ✅ |
| Administrative pay — 300/day in a town, 200 in a castle | citizen wealth (town); **nobody** (castle) | `Admin` | town ✅ / castle ❌ |
| Walls upkeep — 200 × wall level a day for a town, 150 × for a castle | **nobody** | `Maintenance` | ❌ |
| Construction budget — `ConstructionBudgetShare` (1%) of the treasury a day | the construction reserve | `Construction` | ✅ |
| Dearth advance | citizen wealth | `Dearth` | ✅ |
| Siege drain and sack — a castle only; a town's treasury passes intact to the new owner | the besiegers' spoils, half or less; the rest destroyed | `Siege`, `Sack` | ❌ partly |
| A castle's surplus — 10% a day of its treasury above 200 per point of prosperity | the owner, on his next finance pass | `WealthTax` | ✅ |

### 5.3 Out of the construction reserve

Spent on the project at the head of the queue (or, with nothing queued, a quarter-day on the least-built
building). Materials and replacement tools are bought off the fief's own market — a castle's market
town — at its prices, the coin to that market's citizens (market fee and all). Every other point of
work costs a coin, of which **half** reaches the townsmen as `Construction` and half is simply spent.
Prisoners lift the day's ceiling and part of their work is free. The owner can top the reserve up by
hand; vanilla's own boost moves his gold into it and nowhere else.

### 5.4 Out of a village purse

| Flow | To | Source | Conserving? |
|---|---|---|---|
| Recruit kit, and a tenth of a kit per new militiaman | the trade-bound town's citizen wealth | `VillageArms` | ✅ |
| Village shopping — half the excess over 50 per hearth, each time a convoy sets out | the trade-bound town's citizen wealth | `VillageDemand` | ✅ |
| Militia maintenance shortfall (militia wage is **zero**) | the mending town | `Militia` | ✅ |
| Administrative pay — 100/day | **nobody** | `Admin` | ❌ §7 |
| Raid | the raiders' spoils, half; the rest destroyed | `Raid` | ❌ partly |

---

## 6. Clan and party money

### 6.1 Hero and clan gold — in

| Flow | From |
|---|---|
| Fief tax on prosperity — vanilla's `CalculateTownTax`, about 0.35 a day per point of a fief's prosperity | **nowhere** — vanilla, see §7 |
| Tariff income | the settlement's `TradeTaxAccumulated` — a real drain on it; at a town now only perk tolls, since the town commission is zero |
| Village income | the village's `TradeTaxAccumulated` — the half of each convoy's takings the village does not keep, and its stall sales |
| Wealth tax | citizen wealth of a town he owns (his own 0.00027/day levy, or 10% of a hoard's excess — the fief's share is separate and stays home), and a castle's surplus; paid on the clan's next finance pass |
| The mint | a town's silver: the holding lord takes 10% of the coin struck, the realm's ruler 20% |
| Caravan payouts | the caravan's own `PartyTradeGold`, debited by exactly what he receives |
| The leader's cut | his men's spoils purses |
| Companions' share of spoils | a battle, raid or sack — paid straight to the payee as gold |
| Mercenary contract pay — a stipend of 300 + 2 per point of influence and renown, plus twice the company's base troop wages | the employing ruler's gold, as far as he can pay |
| Prisoners sold at a town | that town's citizen wealth (`RansomFunding`) |
| Prisoners sold at a castle | that castle's own wealth, no market fee (`RansomFunding`) |
| Selling goods | the buyer's purse |
| Quest and issue rewards, tournaments, other ransoms | **nowhere** — vanilla, see §7 |

### 6.2 Hero and clan gold — out

| Flow | To |
|---|---|
| Party wages | **nobody** — vanilla's largest sink, unchanged by RBM |
| A mercenary company's second wage | **nobody** — the men bank it as the doubled spoils deposit, and the employer reimburses it |
| Garrison wages — only what the fief's treasury could not cover | **nobody** |
| Garrison subsidy — maintenance and promotions, only for a fief set to an unlimited garrison wage limit (an AI clan keeps 80,000 back) | the mending / arming town's citizen wealth |
| Maintenance shortfall | the supplying town's citizen wealth |
| Troop upgrades | the supply town's citizen wealth |
| Recruitment — free for the fief's own clan and the realm's ruler; gear + 5 days' wage for a vassal at home; +10% for anyone else; bounty + 10% only for a realmless adventurer | the recruiting settlement: a town's citizen wealth, a village's purse |
| Mercenary contract pay | the mercenary clan's leader |
| A player workshop's overhead while its capital is under 30,000 | citizen wealth |
| Topping up a fief's construction reserve | the reserve |
| Buying goods | the seller's purse |

### 6.3 Party trade gold

Caravans and villager convoys hold their own purse and are genuine intermediaries, not conduits:

- A **convoy** is paid by the town's citizen wealth for its load (the town paying the market fee on
  it), carries the money home, and at its own gate vanilla turns the takings into the owner's
  `TradeTaxAccumulated` — of which RBM hands **half** back to the village purse (`VillageHousehold`).
  Its takings exist as a real balance the whole way.
- A **caravan** trades on its own capital — seeded, and priced to form, at ten times vanilla's
  (`CaravanCapital`). Its payout to the owner debits `PartyTradeGold` by exactly what the clan
  receives — the two are the same number by construction.
- An **RBM supply caravan** (`Caravans/`) carries no purse of its own: the destination market pays
  for the cargo and the source market is credited the proceeds, town to town.

---

## 7. Where money is still created or destroyed

The honest list. Sorted by size.

### Still open

| Edge | Direction | Where | Note |
|---|---|---|---|
| **Party wages** | destroyed | vanilla | Deducted from clan gold daily, credited to nobody. The largest sink on the map. |
| **The wage→spoils deposit** | conjured | `SpoilsPool.Wages` | §3.3. The largest faucet, and the mirror of the line above — they roughly cancel in aggregate but not in place. |
| **Garrison wage** | destroyed | `GarrisonUpkeep` | The fief's treasury now pays the whole bill first (owner, then burghers above 50,000, as backstops), but the debit still credits nobody; the garrison's purses are minted the same sum under the line above. It relocates the sink rather than closing it. |
| **Field maintenance, spoils leg** | destroyed | `SpoilsPool.Maintenance` | What the men meet from their own purses is drained and credited nowhere; only the leader's gold shortfall reaches a town. |
| Castle income | conjured | `CastleEconomy` | 41 a day per point of a castle's prosperity, into its treasury — the tax on its lands, from no payer. Most of it leaves again as wage, upkeep and the surplus skim to the owner. |
| Fief tax on prosperity | conjured | vanilla | `DefaultSettlementTaxModel.CalculateTownTax` still pays an owner about 0.35 a day per point of each fief's prosperity, untouched by RBM. Modest for a town on RBM's household-scale prosperity. |
| Mint | conjured | `Minting` | A town strikes silver ore standing in its market above a 10-unit reserve — 10% of the stock up to 100, 30% of anything over — at 85 a unit, deliberately about twice silver's trade value of 43. The ore is consumed, and the coin struck is new money worth more than the ore it came from. Owner 10%, ruler 20%, treasury 1% (× the Tax Office), the rest to citizens. |
| Prison labour | conjured | `PrisonLabour` | 30 a day per prisoner in a town's or castle's cells, into its treasury. |
| Walls upkeep, castle admin | destroyed | `AdministrativeUpkeep` | Walls: 200 (town) or 150 (castle) × wall level a day, paid to tradesmen the ledger does not track. A castle's 200/day administration has no market to land in either. |
| Village admin salary | destroyed | `AdministrativeUpkeep` | Deliberate, and structural: a village has no citizen pot for the wage to land in, so it leaves the purse into the untracked household economy. Up to 100/day, capped at what the purse holds — a ceiling, not a rate. A town's equivalent is conserving, since its officials are townsfolk and the wage lands back in citizen wealth. |
| Construction | destroyed | `Construction` | Half of every wage-paid point of building work is "simply spent" — rope, scaffolding, spoilage. |
| Raids, sieges and sacks | destroyed | `SpoilsPool.Plunder`, `.MarketSack` | Of the coin drawn, half (a tenth on Devastate) becomes the attackers' spoils; the rest is gone. |
| Militia arming and its refund | both | `MilitiaUpkeep` | A town arms each new watchman straight off its own shelves and a disbanded one puts his kit back — goods only, no coin (with the recruit-supply draw off it falls back to a citizen-wealth debit and coin refund). A castle with no friendly town in reach arms out of its treasury; a man shed for want of pay there has his kit's worth credited back to that pot from nowhere. A village's kit is bought from, and refunded out of, its trade town, so it conserves. |
| Garrison recruit kit in a castle | destroyed | `GarrisonRecruitCost` | The kit is sourced outside the walls; in a town the coin reaches the citizens. |
| Town volunteers' kit | destroyed | `RecruitSupply` | A town's citizens are debited the value of the kit its new volunteers take off the shelves; recovered under `Recruit` only for the men a lord actually pays to muster (owners and rulers muster free). |
| Battlefield loot, ransom kit, abandoned prisoners | conjured | `SpoilsPool` | New value from a battle; only the share companions take, and a lone hero's solo leader cut, enter as gold directly. |
| Recruit upkeep seed | conjured | `SpoilsPool` | `RecruitMaintenanceDays` of upkeep appears in a fresh recruit's purse. |
| Quest and issue rewards | both | vanilla | ~60 files in `CampaignSystem` use a null-participant `GiveGoldAction`: issue payouts, crime fines, bribes, incidents. |
| Tournament prizes and betting | both | vanilla | |
| Alley income | conjured | vanilla | `CalculateHeroIncomeFromAssets` adds a flat 30/day per owned alley to a gang leader's purse with no counterparty debit. It was net zero until the notable converter stopped destroying his surplus (below), and is now a real faucet: 30/day/alley, 60–120 a town. Closing it means charging the townspeople for the racket, which is a gameplay decision, not plumbing. |
| Perk-based tax mints | conjured | vanilla | `Tollgates`, `TravelingRumors`, Naval `Salvage` add straight to `TradeTaxAccumulated` with no counterparty. Small. |
| Player's starting gold | conjured | `RBMEconomyCampaignBehavior` | Deliberate, once: `CampaignStartingGoldMultiplier` (5×) on what character creation handed out. |
| Worldgen seeding | conjured | `SettlementGoldFunnel`, `SettlementWealth` | Deliberate, once. `Town.OnInit` deals every town 20,000 through the same `ChangeGold` as everything else; the funnel books it as `Source.Seed` so it is not counted as a trade or charged a market fee. A new campaign then re-seeds citizen wealth, treasuries and village purses to the figures in §1.1. |

### Clamps that swallow a shortfall

Most callers credit exactly the figure the mover returned, which makes the pairing exact by
construction. These do not, and conjure the difference when a purse runs dry mid-transaction:

1. the delivery sale pays the convoy before debiting the market, and discards the debit's return;
2. the garrison wage share lowers the owner's bill by a **pre-read** balance rather than by what was
   actually withdrawn;
3. a ship sold to a port drains the port's market for what it can cover and lets vanilla mint the
   rest of the seller's price (`ShipTradeFunnel`).

(The town-commission path that used to be on this list no longer moves money: the town rate is zero.)

Separately, the garrison's, the administration's and the prisoners' rations are taken off a town's
market **free** — no money changes hands — so the goods leave the economy unpaid for. That is
deliberate (feeding the defenders is the settlement's duty), but it destroys physical value rather
than gold.

### Closed, and how

Worth knowing so they are not "fixed" twice:

- **The notable purse.** Measured as the second-largest sink on the map after party wages, and unlisted
  here until 2026-08-15. Every notable's balance is pinned to a band around 10,000 by a nightly
  converter that turns the surplus into standing at 500 gold a point — and the upward leg is
  `GiveGoldAction.ApplyBetweenCharacters(notable, null, …)`, which credits nobody. Everything feeding
  that purse came out of citizen wealth: a named workshop's day is a net withdrawal from the market
  (in vanilla the shop was credited `min(1000, price)` an output and billed for one input, and every
  trade-good recipe turns one input into two dearer outputs; RBM now pays the sell-side price capped
  at 10% of the town's gold and bills the whole input draw — `Workshops/RBMWorkshopSettlement`), and a
  caravan buys and sells against `Town.Gold` on both legs. The owner then draws a fifth of the
  accumulation. Order of magnitude at four gold-settling
  shops and two caravans a town: **12,000–22,000 a day per town, destroyed within 24 hours** — against
  a 20,000 worldgen seed. Note the withdrawals themselves are honest transfers; the destruction is
  entirely at the converter, so that is the only place `NotableWealth` patches. The surplus now credits
  the market and the refill leg is paid out of it, clamped to what the market can find. Two things it
  deliberately does not touch: `DefaultClanFinanceModel`, which carries the cctor trap and holds only
  conserving legs anyway, and alley income, which is listed above.
- **Leaderless-party stall trades.** `GiveGoldAction.ApplyInternal` silently skips a null participant,
  so a villager, bandit or garrison party buying from a town paid nothing (town credited from thin
  air) and selling to one was paid nothing (town's money destroyed). `NativeTradeConservation` supplies
  the missing counterparty's own purse.
- **Village stall commission.** Vanilla's accumulate step is gated on `Town != null`, so a village sale
  destroyed the entire commission. Same file: it now reaches the owner through the village's
  `TradeTaxAccumulated`.
- **The town commission.** Vanilla took 0.7 of every town stall sale off the market for the owner —
  in a conserved purse, the largest single line on the ledger by a factor of three, and enough to
  drain every town to under a thousand. `TradeTariff` zeroes the town rate; the 1% market fee to the
  town's own treasury is the only cut a town takes on a trade.
- **The town gold controller.** Vanilla's `GetTownGoldChange` dragged every town's gold a quarter of
  the way to `10000 + 12 × Prosperity` each day, conjuring below it and destroying above it — some
  65,000 a day destroyed at Danustica. `RBMMarketLiquidity` returns zero for towns; citizen wealth is
  now a real stock (the old target survives only as the `LIQUID` drift line in the log).
- **Ship trades at a port**, which vanilla settled against nobody, now move the port's market
  (`ShipTradeFunnel`; the selling leg is clamped as above).
- **Garrison, militia and soldiers' spending in castles and villages**, which used to be burned
  wherever the settlement was not a town, now lands in that settlement's single purse.
- **Workshop running costs**, which vanilla destroyed, are paid to the townspeople as salary and
  overhead (`RBMWorkshopExpense`).
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
  Garrison and administrative rations no longer credit the town either: they are taken off the
  market free (see the clamps above), so a bigger garrison no longer enriches the fief that fed it.
- **Recruitment gold**, which vanilla destroys in full on every path, now reaches the settlement the
  men were raised from — a town's citizens, a village's purse.
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
| The Ledger screen | — (Ctrl+Shift+K, or the escape menu) | Per town and village, every day's treasury and citizen-wealth income and expense by `Source`, fed from the same two writers as the log whether or not logging is on. |
| In-game tooltips | — | A settlement's hover panel shows its purses; the clan finance screen carries maintenance, wealth-tax income, garrison-subsidy and mercenary-contract lines, plus a 14-day average of event gold (leader's cut, companions' spoils, mint cuts, gold-paid promotions); the party wage tooltip shows the day's maintenance beside the wage (display only — it never touches the charge). |

To audit conservation for one settlement, take a day's economy log, sum the credits and debits by
`Source`, and compare against the balance delta. A gap is either one of §7's known edges or a new
`ChangeGold` that skipped the funnel.

---

## 9. The dials that move money

Grouped by what they actually change. Full descriptions in
[`../RBMCampaign/README.md`](../RBMCampaign/README.md#tuning-it).

| Dial | Moves |
|---|---|
| `TroopUpgradeCostMultiplier` (1) | The size of the upgrade flow. **0 disables the entire spoils system**, and with it every flow in §3. |
| `TroopMaintenanceFraction` (0.005) | The daily maintenance drain on the purses, and the clan-gold shortfall that reaches a town (also the base of garrison and militia maintenance). |
| `IndependentMaintenancePurseFraction` (1.0) | Who pays that bill for an independent or mercenary clan — the men or their leader. A sworn vassal's men always pay none. |
| `TroopSettlementFunWageFraction` (0.25) | The carousing flow. Historically the single largest money-into-town term; a quarter of a day's wage now, down from one and a half. |
| `TroopLeaderSpoilsCutFraction` (0.05) | The one spoils→gold exit. |
| `TroopSpoilsCapDays` (20) | How long a stack saves before its surplus goes to drink — i.e. how much of the spoils supply sits idle rather than reaching towns. |
| `RecruitMaintenanceDays` (20) | The size of the recruit-seed faucet (§7). |
| `TroopUpgradeSpoilsLootMultiplier` (1), `TroopFallenSpoilsCaptureFraction` (0.75) | How much a battle injects or moves. The raid, siege and sack shares are constants in code (§3.2). |
| `RecruitDrawsFromSettlementStock`, `TroopUpgradeRequireSupplyTown` | Whether recruits and upgrades draw kit off a market's shelves. The upgrade **payment** reaches a town whether or not the supply-town gate is on. |
| `ConstructionBudgetShare` (0.01), `BuildingCostMultiplier` (250) | How fast a treasury drains into building work, and how much work a project costs. |
| `KingdomCaravansEnabled`, `CaravanInvestmentEnabled` | The town-to-town supply-caravan flows and their repayable investments. |
| `CampaignStartingGoldMultiplier` (5) | The player's one-off opening faucet. |

Troop **wages** have no dial. They are a fixed per-tier table
(`RBMCampaign/Wages/TierBasedWageModel.cs`) and apply whenever the module is on. Since the wage is
both the largest clan sink and the source of every spoils purse, that table is the scale factor on
most of §3 — change it and everything downstream moves with it.
