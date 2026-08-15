# Town notables — the complete reference

Merchants, artisans and gang leaders: the assets they hold, the power those assets buy, and the parts
of a town they actually move.

This is a **vanilla reference** with RBM's current interactions marked inline. Its sibling
[`village-notables.md`](village-notables.md) covers Headmen and rural notables — the two documents are
deliberately parallel, and the contrasts between them are the interesting part. Companions:
[`economy-money-flows.md`](economy-money-flows.md) is the money circuit,
[`economy-production-food.md`](economy-production-food.md) is the goods chain.

All decompiled paths are relative to `decompiled/`; the default assembly is
`TaleWorlds.CampaignSystem`, so `TaleWorlds.CampaignSystem/TaleWorlds.CampaignSystem.GameComponents/…`
is written `…GameComponents/…`. Researched 2026-08-15 against game v1.4.7.

---

## 0. The one idea

A town notable is **an asset holder whose assets buy power, and whose power buys troop tier and
survival**.

That is the whole loop, and it is worth stating why it looks like an economy without being one. A town
notable really does own things that really do generate revenue — a caravan trading across Calradia, a
workshop converting flax into linen, an alley running a protection racket. Money genuinely flows.

But it flows *through* them, not *to* them. Every denar of asset income lands in a purse that is
already sitting at the top of a 1,000-wide dead band, and the daily converter immediately burns the
excess into `Hero.Power` at 500:1. The purse is a **transducer**. What accumulates is power, and power
buys exactly four things: volunteer tier, survival, an heir, and — if the player has paid for
patronage — a trickle of influence.

The contrast with villages is the useful frame:

| | Village notable | Town notable |
|---|---|---|
| Assets available | **none** | workshop, caravan, alley |
| Gold trajectory | flat 10,000 forever | sawtooth in `[10000, 10500)` |
| Converter fires | never (absent player gifts) | daily to weekly |
| Coupling to settlement stats | **none** (§10 of that doc) | loyalty via `SupporterOf`, security both ways, siege aftermath |
| Can be despawned | yes, at `Power < 100` | **no**, if holding any asset |

So town notables are the richer system in every sense — but the richness is in *power dynamics and
gameplay hooks*, not in a circulating money supply. §3 is the part to internalise.

---

## 1. Who they are

### 1.1 Quotas

`…GameComponents/DefaultNotableSpawnModel.cs` → `GetTargetNotableCountForSettlement`:

| Settlement | Occupations | Total |
|---|---|---|
| **Town** | 2 Merchant, 2 GangLeader, 1 Artisan | 5 |
| Village | 1 Headman, 2 RuralNotable | 3 |
| Castle | — | 0 |

`Occupation.Preacher` is **vestigial** — `IsPreacher`, `PreacherNotableTypeTag`, preacher notary
spawns, and even a `SupporterOf` branch for sect clans all exist, but no spawn path ever creates one.
Every preacher-specific code path in this document is therefore dead in practice.

### 1.2 Creation, templates, and traits

Identical machinery to villages — `HeroCreator.CreateNotable` → `GetRandomTemplateByOccupation`, weight
`Frequency × 10` defaulting to 100, deterministic per settlement via
`settlement.RandomIntWithSeed((uint)settlement.Notables.Count, …)`. Templates live in
`Modules/SandBox/ModuleData/spspecialcharacters.xml` and load into `CultureObject.NotableTemplates`.
No vanilla notable template declares `Frequency`, so selection is uniform within each
(culture, occupation) bucket.

One occupation-specific twist: **gang leaders get a trait floor**.
`…GameComponents/DefaultHeroCreationModel.cs:319` suppresses positive `Mercy` and `Honor` rolls for
`IsGangLeader` at creation, so a kindly gang leader cannot exist. Traits matter downstream —
`Mercy <= 0` and `Mercy + Honor < 0` gate several issues (§7.1), and the alley flavour dialogue keys
off `DefaultTraits.Thug` / `DefaultTraits.Smuggler` (§8.7).

Naming is per-occupation (`NameGenerator.cs:85-103`), and merchants who own a workshop get a distinct
epithet form (`:538`). `HeroHelper.GetCharacterTypeName` maps occupation →
`str_charactertype_artisan` / `_gangleader` / `_merchant`.

### 1.3 Occupation is immutable

No `SetNewOccupation` callsite promotes or converts a notable in normal play; heirs copy
`relative.Occupation` (`HeroCreator.cs:226`). An artisan can never become a merchant and thereby gain
caravan access.

### 1.4 Death, heirs, respawn — and asset immortality

**The key difference from villages.** `NotablesCampaignBehavior.CheckAndMakeNotableDisappear` (L295)
requires:

```csharp
OwnedWorkshops.IsEmpty() && OwnedCaravans.IsEmpty() && OwnedAlleys.IsEmpty()
    && CanDie(Lost) && CanHaveCampaignIssues() && Power < NotableDisappearPowerLimit /* 100 */
```

> **Any asset at all makes a town notable permanently un-despawnable**, no matter how far their power
> sinks. Since game start assigns 4 workshops and 2 alleys per town, and merchants auto-spawn caravans
> at 75 %/day, most town notables are immortal-by-attrition within days of world-gen.

`CanHeroDie` (L85-99) additionally vetoes death outright while one of their caravans is in a map
event — a protection village notables can never have.

Heirs work as elsewhere: `Power >= 100` at death spawns a relative inheriting relations with
`|value| >= 20`, the open issue, and **the caravans** (`TransferCaravanOwnership:296-300`, with
`PartyTradeGold` preserved). Below 100 the caravans are destroyed outright and the seat is vacated,
refilling on the weekly `SettlementHelper.SpawnNotablesIfNeeded` roll at one notable per success.

### 1.5 They never move

No code path changes a notable's `CurrentSettlement`. They do not flee sieges, do not relocate, and do
not switch allegiance when the town changes hands (§9.3).

---

## 2. The three assets

### 2.1 Workshops — open to everyone

There is **no occupation filter** in the ownership chain.
`…GameComponents/DefaultWorkshopModel.cs:70-83`:

```csharp
foreach (Hero notable in workshop.Settlement.Notables)
    if (notable.IsAlive && notable != workshop.Owner)
    {
        int count = notable.OwnedWorkshops.Count;
        float item = Math.Max(notable.Power, 0f) / MathF.Pow(10f, count);
        list.Add((notable, item));
    }
return MBRandom.ChooseWeighted(list);
```

`IsAlive && != current owner`, weighted `max(Power,0) / 10^OwnedWorkshops.Count`. That's it. **A gang
leader owning a tannery is normal, not an edge case.**

Every assignment path funnels through it:

| Path | Site | Occupation filter |
|---|---|---|
| Game start, slot 0 (hidden `artisans`) | `WorkshopsCampaignBehavior.BuildArtisanWorkshop:1362` | `Notables.FirstOrDefault(x => x.IsArtisan)` **with `?? Notables.FirstOrDefault()` fallback** |
| Game start, slots 1–3 | `BuildWorkshopsAtGameStart:1348` | none |
| Owner death | `OnHeroKilled:292` → `ApplyByDeath` | none |
| War / capture | `TransferPlayerWorkshopsIfNeeded:1074` → `ApplyByWar` | none (player-owned only) |
| Bankruptcy | `ChangeWorkshopOwnerByBankruptcy:1093` | none |
| Player sale | `WorkshopsCharactersCampaignBehavior.cs:410` | none |
| Save repair | `RemoveDeadOwnersFromWorkshops:190` | none |

That `??` fallback in `BuildArtisanWorkshop` is the **only** occupation reference in the entire system,
and it degrades to "any notable." Because the weight is `Power / 10^count`, the Artisan — already
holding slot 0 — is **10× penalised** for slots 1–3, leaving the 2 Merchants and 2 GangLeaders
competing on raw `Power` for the three named shops.

**Money model.** Constants (`DefaultWorkshopModel.cs:17-27`): `InitialCapital = 10000`,
`CapitalLowLimit = 5000`, `DailyExpense = 100`, `DefaultWorkshopCountInSettlement = 4`,
`WarehouseCapacity = 6000`, `GetCostForNotable = (EquipmentCost + Prosperity/2 + Capital) / 2`,
`GetCostForPlayer = EquipmentCost + Prosperity×4 + InitialCapital/5`.

`Workshop.ProfitMade => MathF.Max(Capital - InitialCapital, 0)`, where `InitialCapital` is set once at
`InitializeWorkshop` and **never rewritten** by an ownership change. `ChangeGold(g) => Capital += g` is
the only writer. Production credits `min(1000, itemPrice)` and debits the town; inputs debit capital
and credit the town — both only when `effectCapital` is true, which requires **all** inputs and outputs
of that production to be `IsTradeGood`. The margin floor
(`CanNotableWorkshopProduceThisCycle:776-792`) refuses a cycle if
`outputIncome <= inputMaterialCost + 200f/ConversionSpeed`, or `town.Gold < outputIncome`, or
`workshop.Capital < inputMaterialCost`.

**The owner's gold is never touched.** `HandleDailyExpense:1101-1114` routes purely on identity:

```csharp
if (shop.Owner != Hero.MainHero) HandleNotableWorkshopExpense(shop);
else                             HandlePlayerWorkshopExpense(shop);
```

and the notable branch (L794-805) is, in full, `if (shop.Capital >= expense) shop.ChangeGold(-expense);
else ChangeWorkshopOwnerByBankruptcy(shop);`. **There is no `shop.Owner.Gold` term** — only the player
twin has the owner-gold fallback. The daily owner withdrawal (`CalculateHeroIncomeFromWorkshops:908`)
takes `max(0, ProfitMade)/5` off capital, and the hero side is a separate
`GiveGoldAction(null, hero, num)` **mint** that happens to equal it.

**Bankruptcy is a free bailout.** At `Capital < 100`, `ApplyByBankruptcy` hands the shop to another
notable and **resets capital to 10,000 out of thin air**, with `ChangeOwnerOfWorkshopAction` moving
zero gold on a notable→notable transfer.

> ⚠️ **RBM findings.** In [`WorkshopPurse.cs`](../RBMCampaign/Settlements/WorkshopPurse.cs), the
> `NotableExpensePatch`'s `fromOwner = state[1] - shop.Owner.Gold` term is **identically zero on every
> call**, because vanilla's notable branch has no owner-gold leg — the capture is dead weight and the
> doc comment describing it is wrong for the notable twin. Separately, `IsCitizenLabour` forces
> `effectCapital = false`, freezing the hidden artisans shop's capital at 10,000 → `ProfitMade == 0` →
> **RBM Artisans earn exactly nothing** unless they win a named shop (§3.3).

### 2.2 Caravans — merchants only

`…GameComponents/DefaultCaravanModel.cs:34`:

```csharp
if (hero.IsMerchant && hero.PartyBelongedTo == null
    && hero.OwnedCaravans.Count(x => !x.MobileParty.Ai.IsDisabled) == 0
    && hero.IsActive && !hero.IsTemplate)
    return hero.CanLeadParty();
```

One non-disabled AI caravan per merchant, maximum. Spawn paths: `CaravansCampaignBehavior.cs:348` at
world-gen, and `DailyTickHero:580` at **75 %/day** for any merchant currently without one.

- Seed: `GetInitialTradeGold` = **10,000**, **17,500** elite, `+5000` if the owner is the player.
  It is **minted** — the merchant pays nothing.
- Cost to the merchant: `GetPowerChangeAfterCaravanCreation` = **−30 power** if `Power >= 50`, else 0.
- Elite chance: `Power × 0.0045 − 0.5` when `Power >= 112`, else 0.
- Growth: `PartyTradeGold` moves only through real `SellItemsAction`/`BuyItemsAction` trades.
  `BuyCategory` caps a single-category purchase at `min(0.5 × PartyTradeGold, 1.5 × avg, 1500)`.
- Income: `(PartyTradeGold − 10000) / 5` per day, deducted from the caravan, plus Trade XP via
  `SkillLevelingManager.OnTradeProfitMade`.

**The only real gold drain in the notable system** — `NotablesCampaignBehavior.ManageCaravanExpensesOfNotable:309-331`:

```csharp
int totalWage = caravan.MobileParty.TotalWage;
if (PartyTradeGold >= totalWage) PartyTradeGold -= totalWage;
else { int num2 = MathF.Min(totalWage, notable.Gold); notable.Gold -= num2; }
if (PartyTradeGold < 5000) {                       // CaravanGoldLowLimit
    int num3 = MathF.Min(5000 - PartyTradeGold, notable.Gold);
    PartyTradeGold += num3; notable.Gold -= num3;
}
```

Note the else-branch is all-or-nothing: if the caravan cannot cover its own wage, the caravan
contributes **zero** and the merchant pays `min(wage, gold)` — wages get silently underpaid when the
merchant is broke.

> ⚠️ **The elite-caravan discrepancy.** Creation uses `isElite: true` → 17,500, but the payout
> threshold (`CalculateOwnerIncomeFromCaravan:866`) and the gate
> (`CalculateHeroIncomeFromAssets:879`) both hardcode `eliteCaravan: false` → 10,000. An elite caravan
> therefore pays its owner `(17500−10000)/5 = 1500/day` on day one purely from its own seed, bleeding
> down to the non-elite float. **RBM's ×10 `PriceScale`
> ([`CaravanCapital.cs:40-52`](../RBMCampaign/Economy/CaravanCapital.cs)) amplifies this to
> 15,000/day**, and `ClanCaravanPayoutFloatPatch` reproduces the same `eliteCaravan: false` for the
> clan path. Note `CaravanGoldLowLimit = 5000` is **not** RBM-scaled.

**Destruction:** `PartyTradeGold` transfers to the winner as plunder (`MapEvent.cs:1867`). The owner
loses the asset but **no `Hero.Gold`**, and takes no power penalty.

RBM's own supply caravans ([`RBMCaravanDispatch.cs:337`](../RBMCampaign/Economy/RBMCaravanDispatch.cs))
are owned by the settlement owner clan's leader, never by notables, so
`ManageCaravanExpensesOfNotable` never sees them.

### 2.3 Alleys — gang leaders only

Covered in depth in §8. Economically: a flat, hardcoded **+30/day per alley** in
`DefaultClanFinanceModel.cs:899-905`, iterating `hero.CurrentSettlement.Alleys` and matching
`alley.Owner == hero`.

> ⚠️ `DefaultAlleyModel.GetDailyIncomeOfAlley = (int)(Prosperity / 50f)` is used **only** by
> `AddPlayerClanIncomeFromOwnedAlleys`. **AI gang leaders never see the prosperity-scaled number** —
> theirs is 30/day regardless of town size.

Alleys cost nothing to acquire or hold. `AlleyCampaignBehavior` contains **zero** `GiveGoldAction`
calls touching an owner, and thug rosters are conjured at battle time rather than paid for.

---

## 3. Gold — a transducer, not a stock

### 3.1 The converter

`NotablePowerManagementBehavior.BalanceGoldAndPowerOfNotable`, daily:

```csharp
private const int GoldLimitForNotablesToStartGainingPower = 10000;
private const int GoldLimitForNotablesToStartLosingPower  = 5000;
private const int GoldNeededToGainOnePower                = 500;

if (notable.Gold > 10500) {
    int num = (notable.Gold - 10000) / 500;
    GiveGoldAction.ApplyBetweenCharacters(notable, null, num * 500, disableNotification: true);
    notable.AddPower(num);
} else if (notable.Gold < 4500 && notable.Power > 0f) { /* mint gold, burn power */ }
```

**500 gold ⇄ 1 power**, dead band `[4500, 10500]`, every notable born at exactly 10,000.

> ⚠️ **In vanilla the upward leg destroys the money** — recipient `null`, so `ApplyInternal` passes
> over without crediting anybody — and the downward leg mints it. Since everything feeding the purse
> came out of citizen wealth (§2.1, §2.2), the converter was the second-largest sink on the map:
> roughly **12,000–22,000 a day per town**, against a 20,000 worldgen seed. Under RBM,
> [`NotableWealth.cs`](../RBMCampaign/Settlements/NotableWealth.cs) now credits the surplus to the
> market and pays the refill leg out of it. Note the workshop and caravan *withdrawals* were always
> honest transfers — the destruction was entirely here, one step later.

### 3.2 Why the purse still pins

All three income terms are non-negative (`max(0, ProfitMade)`, `max(0, PartyTradeGold − initial)`,
`+30`), and `ClanVariablesCampaignBehavior.DailyTickHero` applies the total under `if (num > 0)`. So
gold only ever ratchets **up** until it crosses 10,500, at which point the converter burns
`(Gold−10000)/500` lots of 500 and lands it back in `[10000, 10500)`.

With daily income `I`, the converter fires roughly `I/500` times per day.

> ⚠️ **`Hero.Gold` for a town notable is a bounded sawtooth in `[10000, 10500)` for its entire
> campaign life, essentially regardless of how rich its assets are.** Reading `notable.Gold` to gauge
> wealth or economic health returns ~10,000 in every town in Calradia. `Hero.Power` is the
> accumulator.

### 3.3 Per-occupation trajectory

| Occupation | Typical assets | Daily income `I` | Converter cadence | Notes |
|---|---|---|---|---|
| **Merchant** | 1 caravan (75 %/day respawn) + ~0.6 named workshops | highest — hundreds/day typical; 1,500/day (RBM 15,000) transiently on an elite caravan | daily, often several lots | the only occupation that can dip toward the 4,500 floor, via the caravan top-up leg after a looting |
| **Artisan** | hidden slot-0 shop + ~0.1 named workshops | vanilla: modest. **RBM: exactly 0** | vanilla: every few days. RBM: **never** | see below |
| **GangLeader** | 1–2 alleys + ~0.6 named workshops | 30–60 from alleys, plus workshop | ~every 8–17 days on alleys alone | never dips — no top-up leg exists for alleys |

The hidden `artisans` shop is exempt from `HandleDailyExpense` entirely (`if (!shop.WorkshopType.IsHidden)`),
so it never pays the 100/day. In vanilla it accrues capital from its all-trade-good recipes
(grape→wine, olives→oil, iron→tools, cow→meat+hides), while the armour and garment recipes have
`isTradeGood: false` outputs and settle in kind.

> ⚠️ **Under RBM the Artisan is the poorest notable in the game.** `WorkshopPurse.IsCitizenLabour`
> freezes the artisans shop, so an Artisan without a named workshop has `I = 0` and their gold never
> moves after creation. Their power therefore has nothing opposing the `−0.1/day` occupation term.
> Vanilla's restoring force applies only above 100, so this decayed **without bound** until
> [`ArtisanStanding.cs`](../RBMCampaign/Settlements/ArtisanStanding.cs) cancelled the penalty at and
> below the Regular rank — an RBM artisan now drifts to exactly 100 and holds. See §13.3.

### 3.4 Player gold that reaches a notable

Three paths, and only one is routine:

| Path | Site | Who receives |
|---|---|---|
| **Buying a workshop** | `ChangeOwnerOfWorkshopAction.ApplyInternal` → `GiveGoldAction.ApplyBetweenCharacters(newOwner, owner, cost)` | **the notable** — real gold |
| Buying a caravan | `CaravanConversationsCampaignBehavior` → `GiveGoldAction.ApplyForCharacterToSettlement(MainHero, Settlement.CurrentSettlement, cost)` | **the settlement** — the notable gets nothing, no power, no relation |
| Patronage / barter | `NotableSupportersCampaignBehavior.cs:125`, `GoldBarterable.cs:68` | the notable |

**Selling a workshop back** is `GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, cost)` — the
player's gold is **minted** and the buying notable is **not debited**.

Every denar that does reach a notable is above the 10,500 ceiling within one payment and gets bled
into power at 500:1. There is no way to make a town notable *hold* money.

---

## 4. Power — the real accumulator

`…GameComponents/DefaultNotablePowerModel.cs`. `AddPower` does **not clamp**; power can go negative.

**Initial** (`GetInitialPower:140`): 20 % roll `RandomInt(50,100)`, 60 % roll `RandomInt(100,200)`,
20 % roll `RandomInt(200,400)`. (The castle-bound-village bonus does not apply to towns.)

**Daily** (`CalculateDailyPowerChangeForHero:45`), summed:

| Term | Value |
|---|---|
| Soft cap (when `Power > 100`) | `−(Power − 100) / 500` |
| Per owned alley | `+0.1` |
| Active issue | `IssueOwnerPower`, −0.1 to −1.0 (§7.2) |
| Occupation flat | Merchant **+0.2**, Artisan **−0.1**, GangLeader **−0.4** |
| `SupporterOf == CurrentSettlement.OwnerClan` | `+0.2` |

Equilibrium ≈ `100 + I + 500 × (flat terms)`. A bare merchant settles near **200**; a gang leader with
1–2 alleys nets `−0.4 + 0.1…0.2` plus roughly `+0.06…0.12` from converted alley income and so **runs
net-negative essentially always**; an RBM artisan with no named shop decays to the 100 floor and stays
there.

> Four alleys would break even on the gang leader's −0.4 drift, but **that is unreachable** — a town
> has exactly 3 alleys and `TickAlleyOwnerships` pins each leader at 1–2 (§8.2).

**Other sources**: siege aftermath (§9.1), the security branch (§6.3), issue resolutions (§7.3),
`CaravansCampaignBehavior:510` −30 on caravan creation, `CraftingCampaignBehavior:1174`
`AddPower(craftedItem.Tier + 1)` on completing a smithing order, `AlleyCampaignBehavior:96`
`−Power × 0.2` on losing an alley fight, and `QuestHelper:111` −10 on quest cancel.

`DefaultNotablePowerModel._militiaEffect` (L35) is declared but **never used**.

**What power buys**: volunteer tier upgrades (§5.3), survival (moot for asset holders, §1.4), an heir,
clan influence if the player has bought patronage (0.05/0.10/0.15 per day at power >0/>100/>200),
**alley thug roster strength** (§8.4), **elite-caravan chance** (`Power × 0.0045 − 0.5`),
**workshop-buyer weight** (`Power / 10^count`), `DefaultPartySizeLimitModel:154` `+10 × (1|2|3)` if the
notable ever leads a party, and issue gating (`SnareTheWealthy` needs a target merchant at
`Power >= 150`).

---

## 5. Volunteers

Mechanically identical to villages; only the differences are given here. See
[`village-notables.md` §4](village-notables.md) for the full pipeline.

### 5.1 Fill

`RecruitmentCampaignBehavior.UpdateVolunteersOfNotablesInSettlement`, daily. **Nothing is produced
while the town is `InRebelliousState`** (L217) — the one real notable consequence of a rebellion
(§9.2).

Per-slot chance `0.75 × pow(0.7 + smallFactionBonus, i+1)` → 0.525 / 0.368 / 0.257 / 0.180 / 0.126 /
0.088 for a large faction. **The town's own prosperity affects this only through the faction-wide size
score** (every faction town scores 1/2/3 by prosperity `<3000`/`<6000`/`≥6000` plus its village count,
saturating at 46) — a rich town in a big kingdom produces no faster than a poor one beside it.

### 5.2 Which troop

`DefaultVolunteerModel.GetBasicVolunteer` returns `Culture.EliteBasicTroop` **only** for a
`RuralNotable` in a castle-bound village. **Every town notable always produces `Culture.BasicTroop`** —
merchants, artisans and gang leaders alike, with no elite path whatsoever.

> ⚠️ **RBM overrides this.**
> [`CampaignChanges.TroopPower.cs:56`](../RealisticBattleCombatModule/CombatModule/Campaign/CampaignChanges.TroopPower.cs)
> replaces the method with a flat **15 % elite / 85 % basic** roll for every notable in the world, so
> under RBM **town notables gain an elite chance they should not have** (while castle villages lose
> their guaranteed one). Gated by `rbmCombatEnabled`, not by any campaign toggle.

### 5.3 Upgrade and slot access

`MathF.Log(notable.Power / (float)troop.Tier, 2f) * 0.01f` daily per slot, capped at
`MaxVolunteerTier = 4`. Since merchants sit near power 200 and gang leaders drift below 100, **merchant
slots upgrade markedly faster than gang-leader slots** — the one place occupation visibly changes
recruit quality.

Occupation-specific slot perks on top of the relation table
(`DefaultVolunteerModel.cs:26-45`): `Trade.ArtisanCommunity` (seller `IsMerchant`),
`Charm.FlexibleEthics` (seller `IsUrbanNotable`), `Engineering.EngineeringGuilds` (seller `IsArtisan`,
party's `EffectiveEngineer`). `MaximumIndexCanPartyRecruitFromHeroInternal:60` gives a garrison bonus
for `IsGangLeader` sellers in a clan-owned settlement, gated on the governor's `Roguery.OneOfTheFamily`.

> `Charm.FlexibleEthics` is the **only** gameplay use of `IsUrbanNotable` (= Merchant ∪ Artisan ∪
> GangLeader) in the entire tree, besides the property's own definition at `Hero.cs:348`.

### 5.4 Garrison auto-recruit

`GarrisonRecruitmentCampaignBehavior` draws from the town's own notables **plus all bound villages'**,
subject to `boundVillage.VillageState == Normal`, using
`MaximumIndexGarrisonCanRecruitFromHero` (**no relation term** — effectively 1 plus
`OneOfTheFamily`), sorted cheapest-wage-first, limited by wage budget, party size, and
`GetMaximumDailyAutoRecruitmentCount`. Requires `GarrisonAutoRecruitmentIsEnabled && FoodChange > 0`
and no map event or siege. RBM suppresses this entirely (§12).

---

## 6. Relation, patronage, and the settlement coupling

### 6.1 Initial and decay

`SetInitialRelationsBetweenNotablesAndLords` (L122-178): against every same-faction clan leader and
every co-resident notable, the sum of four uniform `[-1,1]` draws × 30, clamped ±100, sign-forced by
`HeroHelper.NPCPersonalityClashWithNPC`.

Decay — `UpdateNotableRelations` (L216-241), reached on a 1 %/day roll per notable, applies a 20-point
step toward zero with probability `|relation|/1000`, and **skips `Clan.PlayerClan` entirely**. Player
relation with a notable never decays.

### 6.2 `SupporterOf`

`HeroHelper.GetRandomClanForNotable` (`Helpers/HeroHelper.cs:427-466`) assigns an initial patron
**only** to preachers (50 %, `IsSect` clans) and **gang leaders** (50 %, `IsMafia` clans), weighted by
`GetProbabilityForClan` over towns/hideouts and excluding clans already supported by another notable in
the same settlement. `IsMafia`/`IsSect` are XML flags on `Clan` (`Clan.cs:885-886`).

**Merchants and artisans start with `SupporterOf == null`.** All notables can acquire one later via
`UpdateNotableSupport` (L246-278, daily): unsupported → for each non-player clan with `relation > 50`,
chance `(relation − 50)/2000`; supported → drop if `relation < 0` or with chance `(50 − relation)/500`.
The acquisition loop **excludes `Clan.PlayerClan`** — the player can only buy in through dialogue, at
`20000 + 10000 × SupporterNotables.Count`, which also grants `+5` relation.

### 6.3 Security → relation and power

`CharacterRelationCampaignBehavior.DailyTick`, the town branch (L385-420):

```csharp
if (Town.Security >= ThresholdForNotableRelationBonus)          // 75
    foreach notable: if ((IsArtisan || IsMerchant) && rand < 0.05f)
        ApplyRelationChangeBetweenHeroes(OwnerClan.Leader, notable, +1);
else if (Town.Security < ThresholdForNotableRelationPenalty)    // 50
    foreach notable: if ((IsArtisan || IsMerchant) && rand < 0.05f) {
        notable.AddPower(-1); ApplyRelationChangeBetweenHeroes(OwnerClan.Leader, notable, -1); }
    foreach notable: if (IsGangLeader && rand < 0.05f)
        notable.AddPower(+1);
```

Precise reading:

- Thresholds **75** and **50**; the **50–75 band does nothing at all**.
- The high-security branch grants **relation only** — no power gain for artisans or merchants.
- The gang-leader `+1` power fires **only** in the low-security branch. There is no gang-leader
  penalty at high security and no gang-leader relation change either way.
- **Preachers are excluded from both branches.**
- Roll is `ChanceForRelationChange = 0.05f`/day.

> A lawless town is *good* for its gang leaders and *bad* for its merchants and artisans — the only
> place in the notable system where occupations have opposed interests.

### 6.4 Notables → town loyalty

`…GameComponents/DefaultSettlementLoyaltyModel.GetSettlementLoyaltyChangeDueToNotableRelations:169-190`:

```csharp
foreach (Hero notable in town.Settlement.Notables)
    if (notable.SupporterOf != null) {
        if (notable.SupporterOf == town.Settlement.OwnerClan)                 num += 0.5f;
        else if (town.MapFaction.IsAtWarWith(notable.SupporterOf.MapFaction)) num += -0.5f;
    }
```

> It keys off **`SupporterOf`, not relation** — a notable who adores you but supports nobody
> contributes zero, and one supporting a neutral third clan also contributes zero. This is the only
> path by which any notable moves a settlement stat outside the issue pipeline, and it is **town-only**:
> the loop reads `town.Settlement.Notables`, so village notables are excluded (which is why a Headman
> supporting your clan gives their bound town nothing).

Because loyalty drives rebellion thresholds (§9.2), this is also the only indirect notable→rebellion
coupling in the game.

### 6.5 Other relation touch-points

- `CharacterRelationCampaignBehavior:123` — `Charm.Oratory` grants `SecondaryBonus` relation with a
  random notable of the winner's faction after a lord battle.
- `:195` — escorting a villager/caravan gives `+2 × contribution` with the home settlement's notables.
- `Town.cs:620-636` — governor perks `Roguery.WhiteLies` (`+1` with a random notable) and
  `Roguery.Scarface` (`+1` with a random **gang leader**).
- `InventoryLogic.cs:571` — `Trade.TrickleDown` grants `floor(PrimaryBonus)` relation with **every
  `IsMerchant` notable** of the settlement after ≥10,000 denars of trade-good purchases.
- `IssuesCampaignBehavior:411-428` — issue relation rewards are **multiplied** by
  `Trade.DistributedGoods.PrimaryBonus` if the owner `IsArtisan`, and by
  `Trade.LocalConnection.PrimaryBonus` if `IsMerchant` (positive values only). Village notables have no
  equivalent perk.

---

## 7. Issues and quests

### 7.1 The roster

| Issue | Gate (condensed) | Freq |
|---|---|---|
| **Merchant** | | |
| EscortMerchantCaravan | `IsMerchant`, town, `!HasPort`, `Security <= 50`, `OwnedCaravans.Count < 2` | VeryCommon |
| MerchantArmyOfPoachers | `IsMerchant`, `Mercy + Honor < 0`, `Security <= 60`, un-raided bound village, nearby hideout | Common |
| MerchantNeedsHelpWithOutlaws | `IsMerchant \|\| IsRuralNotable`, an `IsInfested` non-busy hideout nearby | VeryCommon |
| **Artisan** | | |
| ArtisanOverpricedGoods | `IsArtisan`, town, + an antagonist `IsMerchant` with `Mercy <= 0`, + items with `GetItemCategoryPriceIndex > 2f` | Common |
| ArtisanCantSellProductsAtAFairPrice | `IsArtisan`, + a co-located `IsMerchant`, + a target town within `AvgDistanceBetweenClosestTwoTowns × 2.25f` | Common |
| **Merchant or Artisan** | | |
| CaravanAmbush | `IsNotable`, `!OwnedCaravans.IsEmpty()`, `(IsArtisan \|\| IsMerchant)`, `!HasPort` | Common |
| **GangLeader** | | |
| GangLeaderNeedsRecruits | `IsGangLeader` — **no town check, no security gate** | VeryCommon |
| GangLeaderNeedsSpecialWeapons | `IsGangLeader`, town, crafting behavior present | VeryCommon |
| GangLeaderNeedsWeapons | `IsGangLeader`, town, **`Loyalty < 60`** (not security) | Common |
| GangLeaderNeedsToOffloadStolenGoods | `IsGangLeader`, `Security < 70`, another notable `IsMerchant`, suitable hideout | Common |
| BettingFraud | `IsGangLeader`, town, `Security < 45` | Rare |
| CapturedByBountyHunters | `IsGangLeader`, infested hideout within range, `"looter"` character exists | Common |
| RivalGangMovingIn | `IsGangLeader`, town, `Security <= 60`, + a rival `IsGangLeader` in the same town | Common |
| SnareTheWealthy | `IsGangLeader`, town, `!HasPort`, `Security <= 50`, + target merchant with `Power >= 150` and `Mercy + Honor < 0`, no cooldown on three related issues | Rare |

**Negatives:** nothing in the tree gates an issue on `IsUrbanNotable`. `ProdigalSon` and
`NotableWantsDaughterFound` merely *consume* a gang leader as a target — their givers are a lord and a
rural notable. `SmugglersIssue` is lord-given.

Cadence: towns cap at `MaxNotableIssueCountForTowns = 3` concurrent notable issues (villages: 2),
`CalculateIssueScoreForNotable` returns 0 if any notable in the same settlement already holds that
exact type, frequency weights are `VeryCommon 6 / Common 3 / Rare 1`, world-gen seeds
`ceil(0.8 × town count)`, and the post-resolution cooldown is 30 days per type per hero.

### 7.2 Passive effects while open

| Issue | Prosperity | Security | Loyalty | OwnerPower |
|---|---|---|---|---|
| ArtisanCantSellProducts | −0.2 | — | — | −0.2 |
| ArtisanOverpricedGoods | −0.4 | — | — | — |
| CaravanAmbush | −0.3 | −1.0 | — | −0.2 |
| EscortMerchantCaravan | −0.4 | — | — | −0.2 |
| MerchantArmyOfPoachers | **+0.2** | −1.0 | −0.2 | −0.2 |
| MerchantNeedsHelpWithOutlaws | −0.2 | −1.0 | — | −0.1 |
| BettingFraud | — | — | — | −0.2 |
| CapturedByBountyHunters | — | **+1.0** | — | −0.2 |
| GangLeaderNeedsRecruits | — | — | — | −0.1 |
| GangLeaderNeedsSpecialWeapons | — | **+0.5** | — | −0.1 |
| GangLeaderNeedsWeapons | — | **+1.0** | — | −0.2 |
| GangLeaderNeedsToOffloadStolenGoods | — | **+1.0** | — | **−1.0** |
| RivalGangMovingIn | — | −0.5 | — | −0.2 |
| SnareTheWealthy | — | −0.5 | −0.1 | — |

> **Note the sign inversion.** While a gang leader has an open issue, town **security rises** — the
> gang is distracted — and `MerchantArmyOfPoachers` *raises* prosperity because the poached goods reach
> market. Village issues are uniformly debuffs; town issues are not.

`GangLeaderNeedsToOffloadStolenGoods` carries the heaviest owner-power drag in the game at **−1.0/day**,
enough on its own to overwhelm a gang leader's entire alley income.

### 7.3 Resolution highlights

Relation is applied centrally in `IssuesCampaignBehavior:411-428`, **only** for `IssueFail`,
`IssueFinishedWithSuccess`, `IssueFinishedWithBetrayal`, `IssueTimedOut`, `SentTroopsFinishedQuest`,
`SentTroopsFailedQuest`, and **only when `issueSolver != null`**. `IssueCancel` and
`IssueFinishedByAILord` pay nothing but the 30-day cooldown.

Power deltas are hand-written per issue. The notable ones:

| Issue | Success | Failure |
|---|---|---|
| **MerchantArmyOfPoachers** | `AddPower(+30)`, rel `+5`, `Prosperity += 50` | `AddPower(−50)`, rel `−5` |
| SnareTheWealthy | giver `AddPower(+30)`, rel `+5`, target merchant rel `−10` | giver `−10`/`−15`, merchant `+5`/`−20` |
| ArtisanCantSellProducts | giver `+10`…`+15`, rel `+5`…`+10`; **every other town notable `AddPower(−10 × difficulty)` and player-rel `−10`** | giver `−10`, others `+3` |
| ArtisanOverpricedGoods | giver `+10`, rel `+5`, antagonist `−10` and rel `−10`, `Prosperity += 30` | `−10`; timeout **`−20`** |
| GangLeaderNeedsSpecialWeapons | `AddPower(+15)` | `−10` |
| RivalGangMovingIn | giver `+10`, rival `−10` and rel `−5` | giver `−10`, `Security −10` |
| BettingFraud | counter-offer accepted: player `+2500` gold, giver `+10`, **`Security −20`** | expose: player `+4500` gold, giver `−15`, `Security +15` |
| EscortMerchantCaravan | `+10`, rel `+5`, `Prosperity += 10` | `−5`…`−10` |
| CapturedByBountyHunters | `+10`, rel `+5` | `−10`, rel `−5`, `Security +5` |

`MerchantArmyOfPoachers` has by far the widest swing in the game: **+30 on success, −50 on failure**.

Several gang-leader issues raise the player's **crime rating** on success —
`ChangeCrimeRatingAction.Apply` at `+5` (artisan quests), `+10`, `+30` or `+60`
(`GangLeaderNeedsWeapons` branches). `RivalGangMovingIn` punishes attacking the questgiver's own alley
mid-quest with `Honor −150`, giver `AddPower(−10)`, player-rel `−8`, `Security −10`.

---

## 8. The alley system

### 8.1 Types

`…Settlements/Alley.cs` — `Alley : SettlementArea`, states
`{ Empty, OccupiedByGangLeader, OccupiedByPlayer }`, one saved field `_owner`, and `SetOwner`
maintaining `Hero.OwnedAlleys` and firing `OnAlleyOwnerChanged`.

> **There is no `AlleyPartyComponent`.** Alleys never create a `MobileParty`; alley troops exist only
> as a `TroopRoster` in the behavior and as `LocationCharacter`s in the town-centre mission.

Every settlement carries exactly **3** `<Area>` entries under `<CommonAreas>` in `settlements.xml`
(326/326 blocks verified) — **including villages**, whose alleys can never be assigned an owner because
every `SetOwner` path is `IsTown` + `IsGangLeader` gated.

### 8.2 Assignment and drift

Game start (`SandBox/SandBox.CampaignBehaviors/AlleyCampaignBehavior.cs:344-355`): per town, pick
`num = MBRandom.RandomInt(0, Alleys.Count)` and assign alleys `num` and `num+1` (mod count) to gang
leaders — exactly **2 occupied alleys per town** (`DesiredOccupiedAlleyPerTownFrequency = 2`), leaving
one free.

Daily (`TickAlleyOwnerships:365`, towns only), per gang leader with `count = OwnedAlleys.Count`:

```
acquire chance = 0.02f − count × 0.005f   // takes the first Empty alley in the town
abandon chance =         count × 0.005f   // releases a random owned alley
```

At `count = 1` that is 1.5 %/day up against 0.5 %/day down, so **equilibrium sits at 1–2 alleys**.
Abandon is skipped if that notable is currently attacking a player alley in the settlement. The same
tick also runs `if (!notable.IsHealthFull()) notable.Heal(10)` — **gang leaders regenerate 10 HP/day
here regardless of alley count**, which is the only routine healing any notable receives.

Constants (L193-207): `RelationLossWithSettlementOwnerAfterOccupyingAnAlley = −2`,
`RelationLossWithOtherNotablesUponOccupyingAnAlley = −1`,
`RelationLossWithOldOwnerUponClearingAlley = −5`,
`RelationGainWithOtherNotablesUponClearingAlley = 1`,
`SpawningNewAlleyFightDailyPercentage = 0.015f`, `ConvertTroopsToThugsDailyPercentage = 0.01f`,
`GainOrLoseAlleyDailyBasePercentage = 0.02f`.

### 8.3 What an alley actually does

| For | Effect |
|---|---|
| **AI gang leader** | **power only** — `+0.1/day` (`DefaultNotablePowerModel:97`), `+30/day` gold that converts to power, and despawn immunity (`NotablesCampaignBehavior:297`) |
| **The town** | **nothing** — no security, prosperity, loyalty or militia effect anywhere |
| **The player** | `Prosperity/50` daily clan income per alley (`AddPlayerClanIncomeFromOwnedAlleys:530`), `+0.5` crime rating per faction town containing a player alley (`DefaultCrimeModel:62`), and full settlement visibility (`DefaultInformationRestrictionModel:38`) |

> ⚠️ **Alleys are a notable-power and player-economy system, not a town system.** Grepping every use of
> `GetDailyCrimeRatingOfAlley`, `GetDailyIncomeOfAlley` and `OwnedAlleys` shows no touch of
> `Town.Security`, `Prosperity`, `Loyalty` or `Militia`. Clearing every alley in a town changes nothing
> about the town.

### 8.4 Thug rosters

`…GameComponents/DefaultAlleyModel.cs:92-151`, characters `gangster_1/_2/_3`, branching on
`owner.Power` and a coin flip on `owner.RandomValue > 0.5f`:

| Power | roll > 0.5 | roll ≤ 0.5 |
|---|---|---|
| ≤ 100 | 3 thug | 2 thug, 1 master |
| ≤ 200 | 2 / 1 / 2 (thug / expert / master) | 1 / 2 / 2 |
| ≤ 300 | 3 / 2 / 2 | 1 / 3 / 3 |
| > 300 | 3 / 3 / 3 | 1 / 4 / 4 |

`GetTroopsOfAlleyForBattleMission` (L81-90) **doubles every count**, so an alley fight is against 2× the
visible roster.

### 8.5 Fights

**AI gang leaders never fight each other.** The only alley fight is against the player:
`CheckSpawningNewAlleyFight:301` rolls `< 0.015f` per player-owned alley per day, requires at least one
`OccupiedByGangLeader` alley in the same town, then `StartNewAlleyAttack` picks one at random, calls
`SetHasMet()`, sets `AttackResponseDueDate = DaysFromNow(GetAlleyAttackResponseTimeInDays)` and applies
`ApplyPlayerRelation(attacker, −5)`.

Response time = `Min(12, 8 + Σ(min(Tier,4) × Number) / 8)` days (`DefaultAlleyModel:256`). Let it
expire and `DestroyAlley()` hands your alley to the attacker and makes your assigned clan member a
fugitive (`MakeHeroFugitiveAction`).

- **Win** (`AlleyFightWon:94`): `UnderAttackBy.Owner.AddPower(−Power × 0.2f)` and
  `UnderAttackBy.SetOwner(null)` — you destroy the attacker's alley and strip **20 % of their current
  power**. This is the only multiplicative power hit outside siege aftermath. Player gains
  `GetXpGainAfterSuccessfulAlleyDefenseForMainHero() = 6000f` Roguery XP.
- **Lose**: `Hero.MainHero.HitPoints = 1`. The player cannot die — `CanHeroDie` returns false while
  `_playerIsInAlleyFightMission`.

### 8.6 Player ownership

Requirements (`SandBox/SandBox.Missions.MissionLogics/MissionAlleyHandler.cs:147`): one alley per
settlement maximum; a clan member with **Roguery ≥ 30 and Mercy ≤ 0**
(`MinimumRoguerySkillNeededForLeadingAnAlley = 30`, `MaximumMercyTraitNeededForLeadingAnAlley = 0`) who
is not a governor, party leader, fugitive or prisoner; and `MinimumTroopCountInPlayerOwnedAlley = 5`
regulars (max 10).

- **Occupying** (`OnAlleyOccupiedByPlayer:391`): old owner player-rel **−5**; if the town isn't yours,
  the settlement owner **−2** and every **non-gang-leader** notable **−1**.
  `GetInitialXpGainForMainHero() = 1500f` Roguery.
- **Clearing** (`OnAlleyClearedByPlayer:414`): old owner **−5**, every non-gang-leader notable **+1**,
  `SetOwner(null)`.
- **Daily while owned**: main hero `+40` Roguery XP, assigned member `+200`;
  `CheckConvertTroopsToBandits` converts each non-`Occupation.Gangster` troop at **1 %/day** into the
  tier-matched gangster.
- **Weekly recruits** via `RandomFloatWeekly`: `≥0.5` nothing, `>0.3` 1 thug + 1 basic bandit, `>0.15`
  2/1/1, `>0.05` 3/2/1, else 2 thug + 3 basic + 3 upgraded, from the culture-mapped bandit clan
  (khuzait→steppe, vlandia/empire→mountain, aserai→desert, battania→forest, sturgia/nord→sea raiders).
- **On the owner's death**: `DestroyAlleyAfterDaysWhenLeaderIsDeath = 4` days grace. Any
  **non-player-clan** hero's alleys are `SetOwner(null)` immediately (`OnHeroKilled:1052-1059`).

**Gold flows:** the player-clan daily income is the only one. No purchase price, no upkeep, no payment
to notables ever.

### 8.7 Dialogue and menus

`AddDialogs:762-804` has two families — player-owned (inspect troops, ask for volunteers, change
leader, abandon) and AI-owned (`alley_options` → *"I don't take orders from the likes of you"* →
`start_alley_fight_on_consequence`, logging a `PlayerAttackAlleyLogEntry`).
`gang_leader_bodyguard_on_condition` gates a priority-200 "You best talk to the boss" line on the
culture's `GangleaderBodyguard`.

Flavour (`alley_activity_on_condition:959`) picks the racket by owner traits: `DefaultTraits.Thug > 0`
→ protection, `DefaultTraits.Smuggler > 0` → smuggling, `Owner.Gold > 100` → loan-sharking.
`enter_alley_rude_on_occasion` is rude if `!HasMet` or `GetRelationWithPlayer() < −5`.

`AddGameMenus:537` adds "Go to alley" to the `town` menu at priority 4, plus `manage_alley`,
`manage_alley_abandon_are_you_sure`, `alley_fight_won` and `alley_fight_lost`; background mesh is
`<culture>_alley`.

---

## 9. Sieges, rebellions, ownership

### 9.1 Siege aftermath — the one big power event

`SiegeAftermathCampaignBehavior.OnSiegeAftermathApplied:122`, inside
`if (aftermathType != SiegeAftermath.ShowMercy)` and then `if (settlement.IsTown)`:

```csharp
foreach (Hero notable in settlement.Notables)
    notable.AddPower(notable.Power * GetSiegeAftermathNotablePowerModifierForAftermath(aftermathType));
```

| Aftermath | Modifier |
|---|---|
| `Devastate` | **−0.5** (−50 % of current power) |
| `Pillage` | **−0.25** |
| `ShowMercy` | 0, and unreachable — the branch is skipped |

Multiplicative on current power, so a proportional haircut rather than a flat subtraction — it hurts
the strongest notables most in absolute terms. Displayed as
`" • Notable Powers: {NOTABLE_POWER_LOST_AMOUNT}%"` (L577) only when `Notables.Count > 0`.

**No other notable effect of siege or capture exists**: no `KillCharacterAction` on notables, no
relation change with notables (the only relation leg is attacker-leader ↔ previous owner, −30 Devastate
/ −15 Pillage), no gold to or from notables. Town-level companions: loyalty −30/−15/0, prosperity
−1.5×/−1×/−0.5× a log-scaled base, building level-downs, party morale +20/+10.

> Village notables are exempt from all of this (`if (settlement.IsTown)`), including when their own
> bound town is devastated.

### 9.2 Rebellions

`…CampaignBehaviors/RebellionsCampaignBehavior.cs` contains **no reference to `Notables`, `IsNotable`,
`Power`, or `SupporterOf`.** Rebel clan heroes come from `settlement.Culture.RebelliousHeroTemplates`
via `HeroCreator.CreateSpecialHero`, not from the notable pool. **Notables neither cause, join, nor
lead rebellions.**

Trigger chain is purely loyalty-driven: `DailyTickSettlement:104` (towns only) → 25 % daily gate →
`CheckRebellionEvent:187` requires `Town.Loyalty <= RebellionStartLoyaltyThreshold (15)` **and**
`Militia >= (garrison + same-faction lord parties) × 1.4f`. `InRebelliousState` is set at
`Loyalty <= 25`.

The **only** notable→rebellion coupling is indirect, via `GetSettlementLoyaltyChangeDueToNotableRelations`
(§6.4) moving loyalty ±0.5/notable/day toward or away from those thresholds. Notable power and player
relation feed nothing.

**What `InRebelliousState` does to notables: nothing directly** — but
`RecruitmentCampaignBehavior.cs:217` blocks volunteer *production* for the town and its villages while
it holds, which is the one consequence that reaches the notable system. Its other consumers are
tournaments, workshops, militia models, patrol parties and menu text. Notable spawning, power, income
and existing slots are untouched.

Rebellion consequences for reference: half the garrison becomes settlement prisoners, garrison refilled
with `Militia × (0.6 ± 0.1)` ranged militia plus 50 basic + 25 upgraded, `Militia = 0` then `100f`,
`Loyalty = 100f`, food restocked; rebel leader gets 50,000 gold and `RandomInt(200,300)` renown.

### 9.3 Ownership changes

`IssueManager.OnSettlementOwnerChanged:576-596` re-initialises notable issues in the settlement (and its
bound villages if it `IsFortification`) when the player is on either side. No power, gold, relation, or
`CurrentSettlement` change. **Notables do not switch allegiance and are not replaced.** Player-owned
workshops transfer via `ApplyByWar` (capital reset to 10,000, cost 0).

---

## 10. What town notables do not affect

Shorter than the village list, because town notables genuinely do couple to loyalty and security — but
still worth stating precisely:

- **Prosperity, food, tax, militia, production** — `DefaultSettlementProsperityModel`,
  `DefaultSettlementFoodModel`, `DefaultSettlementTaxModel`, `DefaultSettlementMilitiaModel` contain
  **zero** `Notable` references. A powerful merchant does not make the town richer.
- **Security** — notables *receive* security effects (§6.3) but contribute none.
  `DefaultSettlementSecurityModel.CalculateSecurityChange` sums hideouts, raided villages, siege,
  prosperity, garrison and issue effects; the notable-flavoured constants it exposes are consumed only
  by `CharacterRelationCampaignBehavior`.
- **Alleys → the town** — nothing at all (§8.3).
- **Rebellions** — no direct participation (§9.2).
- **Loyalty is the sole exception**, and only through `SupporterOf`, not relation or power (§6.4).

So the causal arrows run **town → notable** almost everywhere, with the single `SupporterOf` → loyalty
arrow running back the other way.

---

## 11. Interaction surface

- **Conversation family** — `LordConversationsCampaignBehavior.cs:125` gates the notable menu on
  `IsLord || IsWanderer || IsMerchant || IsPreacher || IsHeadman || IsArtisan || IsGangLeader ||
  IsRuralNotable`. First-meeting variants: merchant (L1538), **four gang-leader variants** keyed on
  `Calculating == 1`, `GetPersona() == PersonaIronic`, `Mercy < 0`, and a fallback (L1649-1679), artisan
  (L1689).
- **Personality gates** — `ConversationTagHelper.EducatedClass`: `IsMerchant → true`,
  `IsGangLeader → false` (overriding everything else). `ImpoliteTag.cs:18`:
  `(IsLord || IsMerchant || IsGangLeader) && Clan.PlayerClan.Renown < 100 && relation < 1`.
  `CharacterHelper.GetNonconversationPose` returns `"aggressive"` for gang leaders.
- **Buying a caravan** — `CaravanConversationsCampaignBehavior.cs:51`, gated on
  **`IsMerchant || IsArtisan`** (not merchant-only), blocked while disguised, needs a clan companion who
  `CanLeadParty()`. Cost 15,000 / 22,500 elite. **The gold goes to the settlement, not the notable**
  (`ApplyForCharacterToSettlement`), and the notable gains no power or relation. Port towns swap
  "caravan" for "trade convoy".
- **Buying a workshop** — `WorkshopsCharactersCampaignBehavior.cs:93-98`, gated on
  `IsNotable && CurrentSettlement == Settlement.CurrentSettlement && OwnedWorkshops.Count(!IsHidden) == 1`
  — **no occupation filter**. Clickable needs peace with the town's faction, enough gold for
  `GetCostForPlayer`, and clan-tier workshop headroom. **The notable receives the gold.**
- **Selling a workshop** — via the shop worker, not the notable; the buyer is chosen by
  `GetNotableOwnerForWorkshop` and the player's gold is minted.
- **Patronage** — `notable_support_request` / `notable_support_end`, any `IsNotable`, no occupation
  gate (§6.2).
- **Recruiting** is a menu, not dialogue.
- **Barter: none.** Grepping `Notable|IsMerchant|IsArtisan|IsGangLeader` across
  `…CampaignBehaviors.BarterBehaviors/` and `…Barterables/` returns zero hits; only the generic
  `GoldBarterable` applies.
- **Entourage** — `NotableHelperCharacterCampaignBehavior` spawns `GangleaderBodyguard` at **2× the
  gang-leader count** (4 per standard town) plus one each of `PreacherNotary`, `ArtisanNotary`,
  `MerchantNotary`, `RuralNotableNotary`. Cosmetic conversation props.
- **Scene placement** — `SandBox.Missions.AgentBehaviors/NotableSpawnPointHandler.cs` activates
  per-occupation prop sets; **a notable who owns a workshop is placed inside it** via
  `WorkshopAreaMarker`. Spawn tags `sp_notable_artisan` / `_merchant` / `_gangleader` / `_preacher`.
- **Rumours** — `CommonVillagersCampaignBehavior.cs:966-991` has four gang-leader rumour variants keyed
  on trait combinations, plus merchant and artisan/headman variants.

---

## 12. What RBM currently does

RBM touches notables **only through `VolunteerTypes`** and through workshop/caravan *pricing*. Nothing
in the repo reads or writes notable `Hero.Power` or `SupporterOf`, and the only `Hero.Gold` contact is a
**measurement** that turns out to be identically zero.

| File | What it does | Gate |
|---|---|---|
| [`Settlements/WorkshopPurse.cs`](../RBMCampaign/Settlements/WorkshopPurse.cs) | `CaptureBefore`/`SettleAfter` around `HandleNotableWorkshopExpense` measure the outlay and credit citizen wealth. `IsCitizenLabour` forces `effectCapital = false` on every path. ⚠️ The `fromOwner` term is **dead** (§2.1), and the freeze **zeroes Artisan income** (§3.3). | `rbmCampaignEnabled` |
| [`Economy/CaravanCapital.cs`](../RBMCampaign/Economy/CaravanCapital.cs) | `PriceScale = 10` on `GetInitialTradeGold` and `GetCaravanFormingCost`; `FormingCostPatch` scales what the player pays. ⚠️ Amplifies the elite-caravan discrepancy to ~15,000/day (§2.2); `CaravanGoldLowLimit` is left unscaled. | `rbmCampaignEnabled` |
| [`Economy/RecruitSupply.cs`](../RBMCampaign/Economy/RecruitSupply.cs) | Multiset diff around the daily volunteer tick draws each new troop's kit off the market — a town arming its own sons debits its **citizen purse** (`Source.TownArms`). Replaces recruit price wholesale (owner clan/ruler free, vassal gear + 5× wage, foreigner +10 %). | `SpoilsPool.IsEnabled && recruitDrawsFromSettlementStock` |
| [`Settlements/GarrisonRecruitCost.cs`](../RBMCampaign/Settlements/GarrisonRecruitCost.cs) | Prefix-skips vanilla's garrison auto-recruit so it no longer consumes a notable's volunteer per day; replaced with a wealth-driven growth curve. | `GarrisonRecruitCost.IsEnabled` |
| [`Settlements/SettlementDefenseMuster.cs`](../RBMCampaign/Settlements/SettlementDefenseMuster.cs) | On siege assault, empties every notable's `VolunteerTypes` into the garrison. | `rbmCampaignEnabled` |
| [`Recruitment/TavernMercenaryTroopsPatch.cs`](../RBMCampaign/Recruitment/TavernMercenaryTroopsPatch.cs) | Postfixes `RegularMercenariesSpawnChance` → `1f`, making the caravan-guard branch of the tavern re-roll unreachable. | `rbmCampaignEnabled` |
| [`Settlements/NotableWealth.cs`](../RBMCampaign/Settlements/NotableWealth.cs) | Replacing prefix on `NotablePowerManagementBehavior.BalanceGoldAndPowerOfNotable`. Same arithmetic and the same standing per denar, but the surplus is credited to citizen wealth under `Source.NotableWealth` instead of destroyed, and the refill leg is debited from it instead of minted — clamped to what the market can find, with any part-point remainder returned. | `rbmCampaignEnabled` |
| [`Settlements/ArtisanStanding.cs`](../RBMCampaign/Settlements/ArtisanStanding.cs) | Postfix on `DefaultNotablePowerModel.CalculateDailyPowerChangeForHero` cancelling the artisan's `−0.1/day` occupation penalty at and below `RegularNotableMaxPowerLevel`, so the decay left behind by the bench freeze self-limits at 100 instead of running unbounded. | `rbmCampaignEnabled` |
| [`AI/RBMRecruitBiasBehavior.cs`](../RBMCampaign/AI/RBMRecruitBiasBehavior.cs) | Additive `GoToSettlement` score toward free-recruit fiefs. | `RecruitSupply.IsEnabled` |

### 12.1 Interactions worth watching

- **The `GetBasicVolunteer` override** (§5.2) runs under `rbmCombatEnabled`, giving town notables a
  15 % elite chance vanilla never grants them.
- **Slot accumulation.** With garrison auto-recruit suppressed, town slots drain only to players and AI
  parties → higher average slot age → more `log2(Power/Tier)` upgrade rolls → **offered tiers drift
  above vanilla over a long campaign**, and more so for merchants than gang leaders.
- **The artisan freeze** makes one of the five town notables permanently powerless, which feeds back
  into the workshop-buyer weight (`Power / 10^count`) and makes artisans progressively *less* likely to
  win named shops over time.

---

## 13. Design implications for RBM

Recorded as observations, not proposals.

1. **The purse cannot hold value.** Any attempt to give town notables a working balance has to
   neutralise the 500:1 converter first; otherwise anything above 10,500 bleeds into power at
   500/day and anything below 4,500 is refunded from power.
2. **Power is the lever that matters** — it drives recruit tier, alley strength, elite-caravan chance
   and workshop-buyer weight simultaneously. One number, four consequences; tuning it is not local.
3. **The artisan's unbounded power decay — fixed 2026-08-15.** The bench freeze itself is a
   deliberate, measured decision — see the remark on `IsCitizenLabour`, which records that the full
   trade circuit was built, logged over fourteen days, found self-cancelling, and actively harmful
   (the working float held more than the townspeople had, locking the poorest towns). That was never
   in question. The problem was downstream: with `I = 0`, an Artisan holding no named shop has no
   converter income, and `CalculateDailyPowerChangeForInfluentialNotables` applies the `−(P−100)/500`
   restoring force **only when `Power > 100`**. Below that the flat `−0.1/day` occupation term ran
   unopposed and unbounded — roughly −36 power per campaign year, eventually negative. Two knock-on
   effects, both self-reinforcing: `log2(Power/Tier)` goes non-positive so that notable's volunteer
   slots **never upgrade**, and `max(Power, 0) / 10^count` reaches zero so the Artisan can **never win
   a named workshop**, the one thing that would have restored their income.
   [`ArtisanStanding.cs`](../RBMCampaign/Settlements/ArtisanStanding.cs) now cancels the occupation
   penalty at and below the Regular rank, so an artisan settles at exactly 100 and holds. Note vanilla
   has the same structural hole — an artisan whose hidden shop yields under ~50 gold/day also decays
   past 100 with no restoring force — RBM merely guaranteed it by zeroing the income.
4. **Alleys are inert from the town's perspective** and already carry a per-town, save-persisted,
   three-slot container with an owner field — the closest thing vanilla has to a free extension point.
5. **`SupporterOf` is the only notable→settlement arrow**, worth 0.5 loyalty/day each, and it is
   town-only. If RBM wants notables to matter to a settlement's condition, this is the existing
   precedent to widen rather than invent against.
6. **Security already splits the occupations** (§6.3) — merchants and artisans lose power in a lawless
   town while gang leaders gain it. Any RBM security rework inherits that asymmetry.

---

## Appendix — quick constant reference

| Constant | Value | Source |
|---|---|---|
| Town notable quota | 2 Merchant + 2 GangLeader + 1 Artisan | `DefaultNotableSpawnModel` |
| Creation gold grant | 10,000 | `NotablesCampaignBehavior.OnHeroCreated` |
| Gold⇄power rate / dead band | 500 : 1 / `[4500, 10500]` | `NotablePowerManagementBehavior` |
| Occupation power drift | Merchant +0.2, Artisan −0.1, GangLeader −0.4 | `DefaultNotablePowerModel` |
| Per-alley power | +0.1/day | ″ |
| `NotableDisappearPowerLimit` | 100 (moot for asset holders) | ″ |
| Power ranks | 0 / 100 / 200 → 0.05 / 0.10 / 0.15 influence | ″ |
| Workshops per town | 4 (slot 0 hidden `artisans`) | `DefaultWorkshopModel` |
| Workshop initial capital / expense / bankruptcy | 10,000 / 100 per day / `Capital < 100` | ″ |
| Workshop buyer weight | `max(Power,0) / 10^OwnedWorkshops` | ″ |
| Caravan seed | 10,000 / 17,500 elite (RBM ×10) | `DefaultCaravanModel` |
| Caravan payout gate | hardcoded `eliteCaravan:false` → 10,000 | `DefaultClanFinanceModel:879` |
| `CaravanGoldLowLimit` | 5,000 (not RBM-scaled) | `NotablesCampaignBehavior:17` |
| Caravan creation power cost | −30 if `Power >= 50` | `DefaultCaravanModel` |
| Elite caravan chance | `Power × 0.0045 − 0.5` above 112 | ″ |
| Alleys per town | 3, of which 2 occupied at game start | `AlleyCampaignBehavior:344` |
| Alley acquire / abandon | `0.02 − count×0.005` / `count×0.005` | `TickAlleyOwnerships:365` |
| AI alley income | flat **30**/day | `DefaultClanFinanceModel:899` |
| Player alley income | `Prosperity / 50`/day | `DefaultAlleyModel` |
| Alley fight spawn | 1.5 %/day per player alley | `AlleyCampaignBehavior:301` |
| Alley fight loss (attacker) | `−Power × 0.2` | `AlleyFightWon:94` |
| Siege aftermath power | Devastate **−50 %**, Pillage **−25 %** | `SiegeAftermathCampaignBehavior:703` |
| Security thresholds | bonus ≥ 75, penalty < 50, 5 %/day roll | `DefaultSettlementSecurityModel` |
| Loyalty per supporting notable | ±0.5/day | `DefaultSettlementLoyaltyModel:169` |
| Rebellion thresholds | `InRebelliousState ≤ 25`, rebellion ≤ 15 | ″ / `RebellionsCampaignBehavior` |
| Max concurrent town issues | 3 | `IssuesCampaignBehavior:39-45` |
| Issue cooldown | 30 days, per type per hero | `DefaultIssueModel` |
| Patronage cost | `20000 + 10000 × SupporterNotables.Count` | `DefaultNotablePowerModel:152` |
| Alley leader requirements | Roguery ≥ 30, Mercy ≤ 0, 5–10 troops | `MissionAlleyHandler:147` |
