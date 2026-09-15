# Workshop rules — RBM ownership plan (2026-09-03)

Status: **Phases 1-4 BUILT 2026-09-03** (`Workshops/RBMWorkshopModel.cs`, `RBMWorkshopCycle.cs`,
`RBMWorkshopSettlement.cs`, `RBMWorkshopExpense.cs`; `WorkshopCardPayrollLine.cs` moved into
`Workshops/` and re-pointed). Phases 5-6 still design.

Phase 4 deviations: the expense ladder keeps vanilla's `Capital > CapitalLowLimit` rung ahead of the
capital payment rather than the plan's bare `Capital >= bill`, so an undercapitalised player shop is
still billed to its owner and the clan finance expense line (`DefaultClanFinanceModel.cs:199-207`)
keeps reporting it. The player leg mirrors vanilla's own `shop.Owner.Gold -= expense` (WCB:738)
rather than a `GiveGoldAction`, so no clan-income event fires for an expense. A partial payment is
attributed to the overhead first and only the remainder to the wage, so the SHOPWAGE per-batch figure
dipping below 75 is the signal that a shop ran out mid-payroll. Bankruptcy charges nothing, as in
vanilla. `WorkshopPurse` is now a pure ledger and keeps only the log state (`_cyclesLogged`,
`_wageDay`), fed by `RecordCycles`/`RecordWage` calls from the expense step.

Two earlier deviations from the plan below:
phase 5's diagnostics change was pulled forward into phase 2 — `WorkshopDiagnostics.RecordEconomicBlock`
and its two postfixes are kept, but they now READ `RBMWorkshopCycle.TryGetLastVerdict` instead of
recomputing a second copy of the rule (keeping the recomputation would have meant maintaining the new
proportional-margin rule twice, which is the coupling this plan exists to remove). And
`Production/WorkshopHeadroomGate.cs` was deleted outright rather than kept for its log helper:
`CountCapped` was always public on `WorkshopDiagnostics`, so nothing needed to stay behind. Companion to `BUILDINGS_CONSTRUCTION_PLAN.md` (same shape:
take the vanilla rule off its seam, own the whole decision, keep money conserved).

Goal: stop nudging vanilla's workshop economy with constant-transpilers and argument-rewriting
prefixes, and own the rules outright. Today six RBM files each bend one vanilla constant or one
vanilla argument, and the bends interact: `WorkshopPayoutCap` rewrites `outputIncome` by ref so
that `WorkshopProductionMargin`'s transpiled floor and `WorkshopDiagnostics`' *recomputed* copy
of the same floor agree — three files that must be edited together to change one number.

Vanilla file references below are all
`decompiled/TaleWorlds.CampaignSystem/TaleWorlds.CampaignSystem.CampaignBehaviors/WorkshopsCampaignBehavior.cs`
unless stated; abbreviated **WCB**.

---

## 0. Why the current stack fails (verified, for the record)

| Vanilla rule | Where | RBM prices break it |
|---|---|---|
| Payout `min(1000, itemPrice)` | WCB:851 | RBM velvet is ~10–25k; the shop is paid 1,000 for it |
| Input cost uncapped, full retail | WCB:866-868 | cotton 2,000–8,000 a draw, paid in full |
| Town-gold gate on **full retail** `outputIncome` | WCB:783 (notable), WCB:719 (player) | a 26,500-value cycle needs a town holding 26,500 cash, so it never fires |
| Margin floor `inputCost + 200/ConversionSpeed` | WCB:778, WCB:708 | slow recipes (velvet, wine, oil) demand 2,000–8,000 margin |
| `CapitalLowLimit` 5,000 / `InitialCapital` 10,000 | `DefaultWorkshopModel.cs:21,23` | one RBM input draw can exceed the whole float |
| Income estimate uses **sell** price (`isSelling:true`) | WCB:826 | but payment uses the **buy** price (WCB:847) — gate and payment disagree by the spread even before the cap |

That last row is the structural bug and the reason argument-rewriting can never fully work:
**the number the gate judges and the number the town pays are computed in two different methods
from two different price sides.** Any fix that only touches one of them leaves a shop that
either produces at a loss or refuses a profitable cycle.

---

## 1. Inventory — every workshop rule and its proposed owner

### 1a. Model constants (`DefaultWorkshopModel`)

| Rule | Vanilla | RBM today | New owner |
|---|---|---|---|
| `InitialCapital` = 10000 | `DefaultWorkshopModel.cs:23` | `Settlements/WorkshopCapital.cs:31-44` postfix → 60000 | `RBMWorkshopModel.InitialCapital` |
| `CapitalLowLimit` = 5000 | `:21` | `WorkshopCapital.cs:60-72` postfix → 30000 | `RBMWorkshopModel.CapitalLowLimit` |
| `DailyExpense` = 100 | `:25` | untouched (RBM adds a *separate* per-cycle payroll, `WorkshopPurse.cs:363`) | `RBMWorkshopModel.DailyExpense` |
| `GetEffectiveConversionSpeedOfProduction` | `:31-58` | `Production/ArtisanOutput.cs:227-245` postfix, `AddFactor(Scale-1)` | `RBMWorkshopModel` override calling `ArtisanOutput.Scale` |
| `GetCostForPlayer` (reads `InitialCapital/5`) | `:65-68` | reached indirectly by the capital postfix | `RBMWorkshopModel.GetCostForPlayer` |
| `GetCostForNotable` (reads `workshop.Capital`) | `:70-73` | indirectly | `RBMWorkshopModel.GetCostForNotable` |
| `WarehouseCapacity` 6000 | `:17` | read by `WorkshopHeadroomGate.cs:112-128` | keep vanilla (delegate) |
| `DaysForPlayerSaveWorkshopFromBankruptcy` 3 | `:19` | untouched | keep vanilla (delegate) |
| `MaximumWorkshopsPlayerCanHave`, `GetMaxWorkshopCountForClanTier`, `GetNotableOwnerForWorkshop`, `GetConvertProductionCost`, `CanPlayerSellWorkshop`, `GetTradeXpPerWarehouseProduction` | `:29,60,75,90,95,103` | untouched | delegate to `BaseModel` |

> There is **no** `GetPolicyEffectToProduction` on `WorkshopModel`
> (`decompiled/.../ComponentInterfaces/WorkshopModel.cs`) — the kingdom-policy effects
> (ForgivenessOfDebts −5%, StateMonopolies −10%) live *inside*
> `GetEffectiveConversionSpeedOfProduction` at `DefaultWorkshopModel.cs:37-44`, alongside the
> building effect, the governor trait, and two perks. Overriding that one method owns all of them.

### 1b. The production decision

| Rule | Vanilla | RBM today | New owner |
|---|---|---|---|
| Cycle scheduling (`progress += speed`, loop while ≥1) | WCB:1387-1420 | — | keep vanilla |
| `effectCapital` = "all inputs and outputs are trade goods" | WCB:1409 | overridden per-shop by three `ref bool` prefixes (`WorkshopPurse.cs:129-132, 173, 281-287`) | `RBMWorkshopCycle.SettlesInGold(workshop)` |
| Owner branch `Owner == Hero.MainHero` | WCB:1410 | — | keep the branch; both paths call one RBM decision |
| Input sufficiency | WCB:884-909 | postfix for logging only, `WorkshopDiagnostics.cs:202-226` | keep vanilla, read its result |
| Margin floor `+200/speed` | WCB:778, 708 | `Production/WorkshopProductionMargin.cs:45,65-81` transpiles 200f→20f in **both** | `RBMWorkshopCycle` |
| Shop solvency `Capital < inputCost` | WCB:787, 713 | — | `RBMWorkshopCycle` |
| Town-gold gate on retail | WCB:783, 719 | `Production/WorkshopPayoutCap.cs:71-87` clamps `outputIncome` by ref first | `RBMWorkshopCycle`, on the **actual** payment |
| Warehouse-at-limit escape | WCB:720-721 | — | `RBMWorkshopCycle` (player only) |
| Storage-glut skip | *(none)* | `Production/WorkshopHeadroomGate.cs:76-146` prefix `return false` | fold into `RBMWorkshopCycle` |

### 1c. Settlement (the money and goods legs)

| Rule | Vanilla | RBM today | New owner |
|---|---|---|---|
| Output payout `min(1000, buyPrice)`, shop +, town − | WCB:844-855 | `WorkshopPayoutCap.cs:94-110` transpiles 1000→10000 | `RBMWorkshopSettlement.PayForOutput` |
| Income estimate at **sell** price | WCB:826 | — | same valuation function as the payout |
| Input purchase at full retail, **prices 1 unit, removes N** | WCB:857-873 | tariff prefix `WorkshopPurse.cs:160-204` works around it for artisans | `RBMWorkshopSettlement.BuyInput`, priced ×N |
| Artisans settle in kind | *(none)* | `WorkshopPurse.cs:126-132, 163-189, 278-288` (three `effectCapital=false` prefixes) | one `SettlesInGold` predicate |
| Market fee on artisan draws | *(none)* | `WorkshopPurse.cs:197` `TradeTariff.Levy` | `RBMWorkshopSettlement.BuyInput` |
| Warehouse in/out | WCB:679-704 | untouched | keep vanilla |

### 1d. Expenses, payroll, bankruptcy

| Rule | Vanilla | RBM today | New owner |
|---|---|---|---|
| `HandleDailyExpense` dispatch on `IsHidden` / `Owner == MainHero` | WCB:1101-1114 | payroll postfix `WorkshopPurse.cs:523-541` | `RBMWorkshopExpense.Run` |
| Player expense: capital-first, else owner gold, else capital, else bankrupt | WCB:729-748 | ledger prefix/postfix `WorkshopPurse.cs:543-555` | `RBMWorkshopExpense` |
| Notable expense: capital, else bankrupt | WCB:794-805 | ledger prefix/postfix `WorkshopPurse.cs:557-569` | `RBMWorkshopExpense` |
| Expense money **destroyed** | WCB:734,738,741,799 | recovered as citizen credit, `WorkshopPurse.cs:307-343` | `RBMWorkshopExpense`, natively |
| Per-cycle payroll 75/cycle | *(none)* | `WorkshopPurse.cs:363, 475-509` | `RBMWorkshopExpense` |
| Bankruptcy handover | WCB:1093-1099 | — | keep vanilla, call it |
| Owner withdrawal `ProfitMade / 5` | `DefaultClanFinanceModel.cs:870-873, 909-936` (`ChangeGold(-num3)` at :919) | untouched (deliberately: cctor trap, `WorkshopPurse.cs:206-218`) | **unchanged** — still vanilla |
| Player clan expense line when capital low | `DefaultClanFinanceModel.cs:199-207` | reads the patched `CapitalLowLimit` | reads the model — free |

### 1e. Selection / diagnostics / UI (unchanged in this plan)

| File | What it does | Fate |
|---|---|---|
| `Production/WorkshopVillageBias.cs:188-224` | postfix `FindTotalInputDensityScore`; `:225+` logs `DecideBestWorkshopType` (WCB:1278) | keep as-is |
| `Production/WorkshopItemTierBias.cs:75+` | prefix `GetRandomItemAux` (WCB:1049) | keep as-is |
| `Production/TownStorage.cs:151,165` | `OutputHasNoRoom` / `Accept` | keep; called by the new decision |
| `Production/WorkshopDemand.cs`, `SteelRefining.cs` | no workshop-behavior patches (Steel patches `DefaultSmithingModel`/`CraftingCampaignBehavior` only) | untouched |
| `Economy/RBMMarketPrices.cs:695,737,849` | the price patches everything above reads | untouched |
| `RBMXML/RBMEconomy_workshops_artisans.xml` | recipes | untouched |
| `Settlements/WorkshopDiagnostics.cs` | recomputes reasons at `:324-370` | rewritten to read the RBM reason |
| `Settlements/WorkshopCardPayrollLine.cs:19-54` | clan-card payroll row | kept, re-pointed at RBM figures |

---

## 2. Target architecture

New folder `RBMCampaign/Workshops/`. Five files, one owner per concern.

### (a) `Workshops/RBMWorkshopModel.cs` — the constants

**Subclass `WorkshopModel`, not `DefaultWorkshopModel`, and delegate everything unowned to
`BaseModel`.** `GameModelsManager.GetGameModel<T>` walks the list backwards
(`decompiled/TaleWorlds.Core/TaleWorlds.Core/GameModelsManager.cs:16-24`), so the last model
added wins and `MBGameModel<T>.Initialize` hands it the previous one as `BaseModel`.
`NavalDLC` registers `NavalDLCWorkshopModel` the same way
(`decompiled/NavalDLC/NavalDLC/NavalDLCSubModule.cs:189`) and it decorates rather than replaces
— every member returns `base.BaseModel.X`, plus two naval policy factors in
`GetEffectiveConversionSpeedOfProduction`. Subclassing `DefaultWorkshopModel` would silently
drop those whenever RBM is registered after Naval.

Registration: `RBM/SubModule.cs:189-203`, in the existing
`rbmCampaignEnabled && game.GameType is Campaign` block, alongside the `AddBehavior` calls:
`((CampaignGameStarter)gameStarterObject).AddModel(new RBMWorkshopModel());`

Overrides:

| Member | Value |
|---|---|
| `InitialCapital` | `60000` (as `WorkshopCapital.cs:35` today) |
| `CapitalLowLimit` | `InitialCapital / 2` — expressed as a ratio so the two can never drift |
| `DailyExpense` | `250` — the standing overhead only; the per-batch wage stays in the expense step |
| `GetEffectiveConversionSpeedOfProduction` | `BaseModel`'s result, then `AddFactor(ArtisanOutput.Scale(workshop) - 1f, text)` — the body of `ArtisanOutput.cs:230-244` moved verbatim |
| `GetCostForPlayer` | `BaseModel`'s (it already reads the model's own `InitialCapital`, so 60000 flows through) |
| `GetCostForNotable` | `BaseModel`'s |
| everything else | `BaseModel` passthrough |

**`ArtisanOutput.Scale` (`ArtisanOutput.cs:65-93`) belongs here as a *caller*, not as a body.**
Keep the prosperity/bench math and its cache in `ArtisanOutput.cs` — it has its own session
reset (`:60-63`) and its own `ARTISAN` log line (`:182-226`). Only the Harmony postfix at
`:227-245` is deleted, replaced by a direct call from the model.

### (b) `Workshops/RBMWorkshopCycle.cs` — the decision

Skip-vanilla prefixes on **both** gate methods:

```
[HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanNotableWorkshopProduceThisCycle")]
[HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanPlayerWorkshopProduceThisCycle")]
Prefix(... ref bool __result) { __result = Decide(...).Allowed; return false; }
```

Both vanilla bodies (WCB:706-727, WCB:776-792) become dead code. `Decide` returns a struct:

```
struct Verdict { bool Allowed; Reason Why; int Payout; int InputCost; }
enum Reason { Ran, Glutted, Margin, ShopBroke, TownBroke, NoWarehouseRoom }
```

Order of tests (deliberately RBM's, not vanilla's):

1. **Glut** — `TownStorage.OutputHasNoRoom` for every output (`WorkshopHeadroomGate.cs:47-62`),
   with the player-warehouse exemption (`WorkshopHeadroomGate.cs:112-128`) → `Glutted`.
2. **Payout** — `Payout = RBMWorkshopSettlement.ValueOfOutputs(town, itemsToProduce)`, the same
   function the settlement step will actually pay. This is the whole point of the rewrite.
3. **Margin** — `Payout >= InputCost * (1 + MarginRate) + WagePerCycle` → else `Margin`.
4. **Shop solvency** — `workshop.Capital >= InputCost + WagePerCycle` → else `ShopBroke`.
5. **Town cash** — if the cycle settles in gold, `town.Gold >= Payout` → else `TownBroke`,
   *unless* the player warehouse absorbs the whole run (vanilla's escape at WCB:720-721).
6. Otherwise `Ran`.

Because step 5 tests the same `Payout` step 2 computed and the settlement step will move, the
gate and the ledger cannot disagree. `WorkshopPayoutCap`'s ref-rewriting prefixes exist purely
to fake this agreement and are deleted.

`SettlesInGold(workshop)` lives here and is the single home for what
`WorkshopPurse.cs:129-132`, `:173` and `:281-287` do with three separate `ref bool` prefixes:
`vanillaEffectCapital && !workshop.WorkshopType.IsHidden`.

`RBMWorkshopCycle` publishes the last verdict per workshop for the diagnostics and the
settlement step to read; the block reasons are then *observed*, never recomputed.

### (c) `Workshops/RBMWorkshopSettlement.cs` — the money and goods legs

Skip-vanilla prefixes on `ProduceAnOutputToTown` (WCB:844) and `ConsumeInputFromTownMarket`
(WCB:857). Both vanilla bodies are replaced; the item movement is reproduced (that is 3 lines
each) and the money is RBM's.

`ValueOfOutputs(town, items)` — **one** valuation, used by the gate and by the payment:
`Σ town.GetItemPrice(item, null, isSelling: true)` (the sell side, i.e. what the town would pay
a seller), times `WholesaleShare`, then clamped by `PayoutCeiling(town)`. This deletes vanilla's
sell/buy inconsistency between WCB:826 and WCB:847.

`PayForOutput`: `town.Owner.ItemRoster.AddToCounts(item, 1)`; if settling in gold,
`workshop.ChangeGold(+p); town.ChangeGold(-p)` — the conserved pair vanilla already writes
(WCB:852-853). No tariff on the output leg (`WorkshopPurse.cs:117-125` reasoning stands: the
fee was already taken on the materials).

`BuyInput`: price `town.GetItemPrice(item) * productionInputCount` — **fixing vanilla's
one-unit pricing at WCB:866, which the current tariff prefix already works around at
`WorkshopPurse.cs:151-155`**. If settling in gold: `workshop.ChangeGold(-c); town.ChangeGold(+c)`.
If not (artisans): no gold moves, and `TradeTariff.Levy(settlement, c)` charges the counter fee
exactly as `WorkshopPurse.cs:197` does today. Items come off the roster either way, plus
`OnItemConsumed` so village/demand bookkeeping still fires.

### (d) `Workshops/RBMWorkshopExpense.cs` — overhead, payroll, bankruptcy

Skip-vanilla prefix on `HandleDailyExpense` (WCB:1101) — one seam instead of three, replacing
`HandlePlayerWorkshopExpense` (WCB:729), `HandleNotableWorkshopExpense` (WCB:794) and RBM's
payroll postfix (`WorkshopPurse.cs:523-541`).

```
if (shop.WorkshopType.IsHidden) { ClearCycleCount(shop); return; }   // artisans pay nothing
wage    = cyclesToday(shop) * WagePerCycle;                          // WorkshopPurse.cs:363 = 75
overhead= Models.WorkshopModel.DailyExpense;                         // 250
bill    = wage + overhead;
if (shop.Capital >= bill)                    -> shop.ChangeGold(-bill);
else if (owner is player && owner.Gold >= bill) -> owner.Gold -= bill;   // vanilla WCB:733-740
else if (shop.Capital >= overhead)           -> shop.ChangeGold(-shop.Capital);  // pay what it has
else                                          -> ChangeWorkshopOwnerByBankruptcy(shop);  // WCB:1093
paid -> SettlementWealth.CreditCitizens(settlement, paid, Source.WorkshopWages);   // SettlementWealth.cs:357
```

Every leg pairs a debit with the citizen credit — nothing minted, nothing destroyed. That is
strictly better than today, where `SettleAfter` (`WorkshopPurse.cs:332-343`) infers the outlay
from a before/after diff across a vanilla call it does not control.

Bankruptcy is called through vanilla's own `ChangeWorkshopOwnerByBankruptcy` so
`ChangeOwnerOfWorkshopAction.ApplyByBankruptcy` and `DecideBestWorkshopType` are untouched.

### (e) Diagnostics — read, don't recompute

`WorkshopDiagnostics.RecordEconomicBlock` (`:324-370`) is deleted along with its two gate
postfixes (`:304-322`). `RBMWorkshopCycle.Decide` calls `WorkshopDiagnostics.CountVerdict(
settlement, verdict)` directly, mapping `Reason` → the existing SHOPBLOCK strings, so
`WorkshopProductionMargin.MarginPerSpeed` stops being read from a diagnostics file
(`WorkshopDiagnostics.cs:347`). `WorkshopHeadroomGate.CountCapped` (`:154-169`) is called from
`Decide`'s glut branch instead of from the deleted gate. `SHOPCAP` and `SHOPIDLE` are unchanged;
the input-block postfix at `:202-226` and the recipe-idle tracker at `:242-291` stay as they are
(they observe vanilla's `DetermineItemRosterHasSufficientInputs`, which RBM keeps).

### (f) UI

`WorkshopCardPayrollLine.cs:19-54` stays, re-pointed at `RBMWorkshopExpense.TryGetLastPayroll`
and `RBMWorkshopExpense.WagePerCycle`. `ClanFinanceWorkshopItemVM` needs no other change: its
"Daily Wage" row (`ClanFinanceWorkshopItemVM.cs:481`) reads `Workshop.Expense`, which reads
`Models.WorkshopModel.DailyExpense` (`Workshop.cs:38`), and its capital warning
(`:487-505`, test at `:493`) reads `CapitalLowLimit`. Both come from `RBMWorkshopModel` for free.

### (g) What stays thin, what dies

**Stays** (pure recording, no decision):
- `WorkshopPurse` `ChangeGold` ledger prefix (`:79-104`), `FlushDaily` SHOPS (`:580-696`),
  `FlushWages` SHOPWAGE (`:715-732`), `Describe`/`Per`. The `_context` markers move from
  Harmony prefixes/finalizers to plain calls inside the RBM steps, which is both simpler and
  exception-safe by construction.
- `WorkshopVillageBias`, `WorkshopItemTierBias`, `TownStorage`, `ArtisanOutput`'s math and log.
- The `DefaultClanFinanceModel` avoidance note at `WorkshopPurse.cs:206-218` — still policy.

**Deleted outright:**
- `Settlements/WorkshopCapital.cs` (both postfixes) → model.
- `Production/WorkshopProductionMargin.cs` (both transpilers) → `RBMWorkshopCycle`.
- `Production/WorkshopPayoutCap.cs` (transpiler + both ref-prefixes) → `RBMWorkshopSettlement`.
- `Production/WorkshopHeadroomGate.cs` (both prefixes) → `RBMWorkshopCycle`.
- `ArtisanOutput.ConversionSpeedPatch` (`:227-245`) → model override.
- `WorkshopPurse` `OutputContext` (`:114-139`), `CitizenLabourTariff` (`:160-204`),
  `CitizenLabourSettlesInKind` (`:278-288`), `CaptureBefore`/`SettleAfter`/`PayWages`
  (`:307-343`), the two expense patches (`:543-569`), the payroll postfix (`:523-541`), the two
  cycle counters (`:454-464`), `PayProductionWage` (`:475-509`) → the RBM steps.
- `WorkshopDiagnostics.RecordEconomicBlock` + its two postfixes (`:304-370`).

Net: **7 Harmony patches on `WorkshopsCampaignBehavior` replaced by 4 skip-prefixes**, two
transpilers gone, zero `ref`-argument rewriting.

---

## 3. Semantics to decide

**1. Payout cap — recommend: no fixed cap; a town-gold fraction instead.**
The 1,000 (WCB:851) and RBM's 10,000 (`WorkshopPayoutCap.cs:41`) both exist to stop one cycle
draining a town's cash. Any absolute number is wrong at some price scale — that is exactly how
we got here. Use `PayoutCeiling(town) = max(MinPayout, PayoutTownShare * town.Gold)` with
`PayoutTownShare = 0.10`, `MinPayout = 500`. A poor town simply pays less per item (the shop
still gets a fair share of what exists), a rich town pays full value, and the ceiling scales
itself with RBM prices forever. Because the gate (§2b step 5) tests the *ceilinged* figure,
`town-broke` becomes rare rather than universal.
*Rejected:* fixed 10,000 — breaks again the next time prices move. *Rejected:* no cap at all —
one velvet cycle would take a fifth of a small town's cash in a day.

**2. Retail or wholesale — recommend: the sell side, `WholesaleShare = 1.0` initially.**
`town.GetItemPrice(item, null, isSelling:true)` is already the lower, town-buying-from-a-seller
price, and it is what vanilla's own income estimate uses (WCB:826). Using it for the *payment*
too (vanilla pays the higher buy price, WCB:847) is itself the wholesale discount, worth the
market spread. Keep `WholesaleShare` as a named const at 1.0 so a further haircut is a
one-token change if playtesting shows shops printing money.

**3. Margin — recommend proportional, speed-independent:**
`Payout >= InputCost * 1.15 + WagePerCycle`. Vanilla's `200/ConversionSpeed` (WCB:708, 778) is
backwards — it demands the *most* margin from the *slowest* recipes, which is precisely the
velvet/wine/oil set RBM wants running (`WorkshopProductionMargin.cs:15-20`). A percentage over
input cost plus the cycle's own wage is the actual business test: does this batch cover its
materials and its labour with something left over?

**4. Player and notable identical — recommend yes, except the warehouse.**
The only genuine asymmetries in vanilla are the warehouse (WCB:584-678 vs WCB:750-774) and the
owner-gold fallback on expenses (WCB:733-740). Everything else — margin, solvency, town cash —
is duplicated code that has already drifted once (the two gates test the same three conditions
in *different order*: WCB:713/719 vs WCB:783/787). One `Decide` for both, with
`allOutputsWillBeSentToWarehouse` as an argument that is always false on the notable path.

**5. Player bankruptcy — recommend: keep vanilla's rule, unchanged.**
A solvent player is never bankrupted (WCB:733-740 tries owner gold before the bankruptcy
branch at WCB:745), and `DaysForPlayerSaveWorkshopFromBankruptcy = 3` gives the grace. RBM's
expense step reproduces the ladder exactly (§2d). The one change is that RBM charges
`wage + overhead` where vanilla charged `overhead` alone, so add the "pay what it has" rung
before bankruptcy — a busy shop should never be bankrupted *by its own good day*, which is the
rule `WorkshopPurse.cs:469-473` already reasoned its way to.

**6. Old saves at 10,000 capital — recommend: no migration, no minting.**
`CapitalLowLimit = InitialCapital/2 = 30000` means an old shop sits under the warning line and
its overhead is billed to its owner (for the player: `DefaultClanFinanceModel.cs:199-207`; for
a notable: it eats capital). That is the correct signal — the shop *is* undercapitalised at RBM
prices. It climbs out through production, or turns over: `ChangeOwnerOfWorkshopAction` resets
`Capital` to the model's `InitialCapital` (`Workshop.cs:130-131`), so shops reach 60,000 as they
change hands, exactly as `WorkshopCapital.cs:19-22` already documents. Topping shops up at load
would mint money into every town in the world, which the RBM money rule forbids.
*If* playtesting shows old-save player shops stuck, the fallback is a one-time transfer from the
owner's gold with a player prompt — never a mint.

---

## 4. Phasing

Each commit builds, ships, and is testable in game on its own. Money conservation is checked
per phase against the `SHOPS` net (`WorkshopPurse.cs:690-693`) and the settlement ledger.

**Phase 1 — the model.** Add `Workshops/RBMWorkshopModel.cs`; register at `RBM/SubModule.cs:196`.
Delete `Settlements/WorkshopCapital.cs` and `ArtisanOutput.ConversionSpeedPatch`
(`ArtisanOutput.cs:227-245`). Behaviour-neutral except `DailyExpense` 100→250 and
`CapitalLowLimit` becoming a ratio (same 30,000 value). Proves model registration, `BaseModel`
delegation and Naval coexistence before anything else depends on them. csproj: add 1, remove 1.

**Phase 2 — the decision.** Add `Workshops/RBMWorkshopCycle.cs` with the two skip-prefixes.
Delete `Production/WorkshopProductionMargin.cs`, `Production/WorkshopHeadroomGate.cs`, and
`WorkshopPayoutCap`'s two gate prefixes (`WorkshopPayoutCap.cs:71-87`) — keeping its transpiler
for now, so the payout the gate assumes and the payout paid still match. Keep
`WorkshopDiagnostics.RecordEconomicBlock` alive one more phase as an independent check: its
recomputed reasons should now *agree* with `Decide`'s. Any disagreement is a bug found for free.
No money moves differently in this phase — only which cycles run.

**Phase 3 — the settlement step.** Add `Workshops/RBMWorkshopSettlement.cs`; point
`RBMWorkshopCycle` at its `ValueOfOutputs`. Delete `Production/WorkshopPayoutCap.cs` entirely,
plus `WorkshopPurse.OutputContext` (`:114-139`) and `CitizenLabourTariff` (`:160-204`); the
`_context` labels move into the RBM step as plain assignments. This is the phase that changes
money: the town-gold-fraction ceiling replaces the 10,000 cap, inputs are priced ×N, and the
sell/buy inconsistency dies. Watch `SHOPS in/out/net` and town gold closely for 30 days.

**Phase 4 — expense and payroll.** Add `Workshops/RBMWorkshopExpense.cs` with the
`HandleDailyExpense` skip-prefix. Delete `WorkshopPurse`'s payroll block
(`:363, 415-509, 523-569` and the two counters at `:454-464`) and `PayWages`/`CaptureBefore`/
`SettleAfter` (`:307-343`). `WorkshopPurse` is left as what its name says: a ledger
(`:79-104, 580-732`). Re-point `WorkshopCardPayrollLine.cs:36,37`.

**Phase 5 — diagnostics off the reason enum.** Delete `WorkshopDiagnostics`'s two gate postfixes
and `RecordEconomicBlock` (`:304-370`); `Decide` reports its own verdict. Nothing else changes,
so any shift in the SHOPBLOCK mix between phase 4 and 5 is a diagnostics bug, isolated.

**Phase 6 — tuning pass.** `MarginRate`, `PayoutTownShare`, `WholesaleShare`, `DailyExpense`,
`WagePerCycle` all become named consts in one file; promote to config only if playtesting shows
a real dial is wanted (`RBMConfig.Campaign.cs` already carries `workshopProductionMultiplier`
at `:201` and `workshopHeadroomGateEnabled` at `:190` — the latter's gate moves into `Decide`
and the toggle must move with it).

Remember `RBMCampaign.csproj` lists every file with an explicit `<Compile Include>` — update it
in every phase that adds or deletes one.

---

## 5. Risks

**Harmony ordering.** Four skip-prefixes returning `false` will suppress any other mod's
postfix's view of a real vanilla run, and will silently lose another mod's prefix if it also
returns `false` first. `WorkshopHeadroomGate` already sets `[HarmonyPriority(Priority.First)]`
(`:97, 133`) — carry that onto `RBMWorkshopCycle`'s prefixes so RBM's glut/margin decision is
made before anyone else's. Accept that a workshop-economy mod loaded alongside RBM will
conflict; that is the explicit trade of owning the rules.

**`DefaultClanFinanceModel` cctor trap.** Its static fields call
`Game.Current.GameTextManager.FindText(...)`, and the type is `beforefieldinit`, so Harmony
preparing any of its methods before a game exists throws a `TypeInitializationException` that
.NET caches for the process. Documented at `WorkshopPurse.cs:206-218`; the working pattern is
`MercenaryContractPay.ApplyDeferred` (`:135-165`) — kept off `PatchAll`, applied by hand from
`RBMCampaignPatcher.DoPatching` (`RBMCampaignPatcher.cs:15-19`) after
`RuntimeHelpers.RunClassConstructor`, and **re-applied on every pass** because
`ApplyHarmonyPatches` does `UnpatchAll` first. **This plan patches none of it** — the workshop
income withdrawal (`DefaultClanFinanceModel.cs:909-936`) and the player expense line (`:199-207`)
stay vanilla, both reading `RBMWorkshopModel` through `Campaign.Current.Models`, which needs no
patch at all. That is a deliberate win of the model approach: a whole class of trap avoided.

**Save compatibility.** Add **no** `SaveableProperty` and no new `SaveableTypeDefiner` — vanilla
already saves `Workshop.Capital`, `Workshop.InitialCapital` and its `WorkshopData` progress
(WCB:42-58, `Workshop.cs:44,47`). The per-day cycle counts and last-payroll figures are
session-only diagnostics and must stay that way; if anything ever needs persisting, use
`SyncData` on `RBMSettlementWealthCampaignBehavior` with the constructor-reset discipline
(`ARCHITECTURE.md`, save/load section). A save made with RBM and loaded without it degrades
cleanly: capital numbers stay, vanilla's own rules resume.

**Naval DLC.** `NavalDLCWorkshopModel` decorates via `BaseModel` and adds two policy factors
inside `GetEffectiveConversionSpeedOfProduction`. RBM must decorate too (§2a), never subclass
`DefaultWorkshopModel`. Verify both orderings in game (RBM before Naval, Naval before RBM) —
the model chain should show the naval policy line *and* RBM's "Town crafts"/"Workshop share"
factor in the same tooltip. Also confirm `NavalDLCSubModule.cs:189` still runs when RBM is
active; the `RBM_WS` compatibility submodule check lives at `RBM/SubModule.cs:208-215`.

**`Owner == Hero.MainHero` branches.** Three of them: WCB:1410 (which tick method),
WCB:1103-1112 (which expense method), `DefaultClanFinanceModel.cs:920` (the player-earned-gold
event). RBM keeps all three branch points and only unifies the *body*, so the player-only
`OnPlayerEarnedGoldFromAsset` and `SkillLevelingManager.OnProductionProducedToWarehouse`
(WCB:588 region, WCB:640) keep firing on the player path only. Watch for a player who *sells*
a shop mid-day: `TransferPlayerWorkshopsIfNeeded` (WCB:1074) can move ownership between the
production run and `HandleDailyExpense`.

**Warehouse.** Untouched by design, but two RBM decisions read it: the glut exemption
(`WorkshopHeadroomGate.cs:112-128`, moving into `Decide`) and vanilla's town-cash escape at
WCB:720-721. `IWorkshopWarehouseCampaignBehavior` is the public seam
(`GetStockProductionInWarehouseRatio`, `GetWarehouseItemRosterWeight` at WCB:573-582) — keep
using it rather than reaching into `_warehouseRosterPerSettlement` (WCB:105).

**Money conservation.** Every leg must pair. Output: `workshop.ChangeGold(+p)` /
`town.ChangeGold(-p)`. Input: `workshop.ChangeGold(-c)` / `town.ChangeGold(+c)`. Artisan input:
no gold, tariff only (`TradeTariff.Levy`, `TradeTariff.cs:110`). Expense: `ChangeGold(-bill)` /
`SettlementWealth.CreditCitizens(..., Source.WorkshopWages)` (`SettlementWealth.cs:357,693`).
Bankruptcy hands the capital to a new owner through vanilla's action — no mint. The one place
money still legitimately leaves the ledger is the owner withdrawal at
`DefaultClanFinanceModel.cs:919`, which is pre-existing and out of scope.

---

## 6. Verification

### Log lines per phase (`logs/economy/`)

| Phase | Line | What proves it |
|---|---|---|
| 1 | `ARTISAN` (`ArtisanOutput.cs:182-226`) | `speed x` figures identical to pre-change — the model override reproduces the deleted postfix exactly. `SHOPS ... capital now` unchanged on day 1. |
| 1 | clan screen | "Daily Wage" reads 250; "Current Capital" warns below 30,000 |
| 2 | `SHOPBLOCK` (`WorkshopDiagnostics.cs:423`) | `ran N of M` ratio rises; `margin:velvet`/`margin:wine`/`margin:oil` counts collapse. The retained recomputed reasons must still *agree* with the new decision — a `unknown` count above zero means `Decide` and `RecordEconomicBlock` diverged. |
| 2 | `SHOPCAP` (`:522`) | unchanged in shape — the glut gate moved but did not change rule |
| 3 | `SHOPS` (`:690`) | `output` per cycle rises to the ceilinged value; `inputs` rises ×N on multi-unit recipes; `net` per town should be *near zero over a week* for artisan-heavy towns |
| 3 | `SHOPBLOCK` | `town-broke` falls to near zero — the gate now asks for what is actually paid |
| 3 | town gold | must fall by exactly the sum of `output` credits; no town gold created |
| 4 | `SHOPWAGE` (`:729`) | `shops Nd over M batches (75/batch)` — the per-batch figure must sit exactly on the rate unless a shop ran out mid-payroll |
| 4 | `SHOPS` breakdown | an `overhead` bucket of `250 × shops` and a `payroll` bucket matching SHOPWAGE |
| 5 | `SHOPBLOCK` | mix identical to phase 4's — any shift is a diagnostics bug |

### Manual in-game check list — a player velvet weavery

1. Buy a velvet weavery in a high-prosperity town. Clan screen: capital 60,000, purchase price
   includes `InitialCapital/5` = 12,000 (`DefaultWorkshopModel.cs:67`).
2. Day 1 `SHOPBLOCK` for that town: the weavery's cycles show as `!ran`, not `margin:velvet`.
3. Day 1 `SHOPS`: the weavery line shows `N batches`, an `inputs` debit ≈ cotton price × recipe
   count, an `output` credit at the sell-side value or the town-gold ceiling, and capital moving
   *up*, not down.
4. `SHOPWAGE`: 75/batch, and the clan card's "Production Wages" row matches it.
5. Over 10 days: `ProfitMade` positive on the clan screen, and the clan income line shows a
   workshop income of roughly `ProfitMade / 5` (`DefaultClanFinanceModel.cs:872`,
   `RevenueSmoothenFraction` = 5 at `:938`).
6. Force a poor town: buy a weavery in a low-gold fief. It should still run — at a smaller
   payout per item — rather than showing `town-broke` every day. That is the whole point of the
   town-gold fraction over a fixed cap.
7. Let the shop's capital fall below 30,000 (sell off the town's cotton). The capital row turns
   to its warning tooltip, and the clan expense line picks up the overhead
   (`DefaultClanFinanceModel.cs:199-207`). It must **not** go bankrupt while the player has gold.
8. Load a pre-RBM-model save: the shop still shows 10,000 capital, sits under the warning line,
   and climbs — no sudden jump to 60,000, no gold appearing anywhere.
9. With NavalDLC active: a workshop in a port town under `MaritimeWealEdict` shows both the
   naval +25% and RBM's town-crafts factor in the conversion-speed tooltip.
