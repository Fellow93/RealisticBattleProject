# Village notables — the complete reference

Headmen and rural notables: what they are, what they hold, what they produce, and the surprisingly
short list of things they actually affect.

This is a **vanilla reference** with RBM's current interactions marked inline. Its sibling
[`town-notables.md`](town-notables.md) covers merchants, artisans and gang leaders — the two documents
are deliberately parallel, and the contrasts between them are the interesting part. Companions:
[`economy-money-flows.md`](economy-money-flows.md) is the money circuit,
[`economy-production-food.md`](economy-production-food.md) is the goods chain. Where they and this
document disagree about a number, they are the more specific and win.

All decompiled paths are relative to `decompiled/`; the default assembly is
`TaleWorlds.CampaignSystem`, so `TaleWorlds.CampaignSystem/TaleWorlds.CampaignSystem.GameComponents/…`
is written `…GameComponents/…`. Researched 2026-08-15 against game v1.4.7; re-verified 2026-10-02
against the v1.4.8 decompile and the current RBM code.

---

## 0. The one idea

A village notable is **a recruit dispenser with a reputation score**, and nothing else.

That sounds dismissive, so here is the precise version. A village notable has four pieces of state:
gold, power, relation, and six volunteer slots. Of those:

- **Gold is inert.** It is exactly 10,000 at birth and exactly 10,000 at death. Every asset class that
  could change it is structurally closed to villagers.
- **Power is real but its only output is troop tier.** It decides how fast volunteer slots upgrade,
  whether the notable survives, and — if the player has paid to become their patron — a trickle of
  influence.
- **Relation buys slot access**, and nothing that touches the village.
- **The volunteer slots are the entire product.**

And critically: **a village notable's presence, power, and relation have zero mechanical effect on
their village's hearth, prosperity, militia, production, tax, or food.** §8 is the grep evidence. The
one channel from a notable to any settlement number is the issue-effect pipeline (§6), and that is a
*debuff* applied while an issue is open, not a contribution.

This matters for RBM because the campaign layer treats settlements as economic actors with real
purses, and the notables living in them are almost entirely outside that circuit. RBM's touches —
the converter's surplus routed into the village purse, a hearth-sized manpower pool gating new
volunteers, ownership-based slot access and pricing — are marked inline and collected in §10.

---

## 1. Who they are

### 1.1 Quotas

`…GameComponents/DefaultNotableSpawnModel.cs` → `GetTargetNotableCountForSettlement` is the single
source of notable quotas:

| Settlement | Occupations | Total |
|---|---|---|
| **Town** | 2 Merchant, 2 GangLeader, 1 Artisan | 5 |
| **Village** | 1 Headman, 2 RuralNotable | 3 |
| **Castle** | — | **0** |

Castles have no notables of their own. `IsTown` and `IsCastle` are mutually exclusive
(`Settlement.cs:370/382`), and the model's `else if (settlement.IsVillage)` branch never fires for a
castle. A castle is served by the notables of its bound villages, which is also why those villages get
special treatment elsewhere (§4.2, §3).

`Occupation.Preacher` is **vestigial** — the occupation, `IsPreacher`, and `PreacherNotableTypeTag`
all exist, but no spawn path in the game ever creates one.

### 1.2 Creation and the template roll

`HeroCreator.CreateNotable(occupation, settlement)` (`HeroCreator.cs:172-187`):

```csharp
CharacterObject t = Campaign.Current.Models.HeroCreationModel.GetRandomTemplateByOccupation(occupation, settlement);
(birthDay, deathDay) = HeroCreationModel.GetBirthAndDeathDay(t, createAlive: true, -1);
Hero hero = CreateHero(t, useCharacterAsTemplate: true, birthDay, deathDay);
args.SetGenerateFirstAndFullName(true); args.SetBornSettlement(settlement);
```

Called from `NotablesCampaignBehavior.SpawnNotablesAtGameStart` (L412 RuralNotable, L417 Headman) and
from `SettlementHelper.cs:615` for weekly top-ups.

Template selection — `…GameComponents/DefaultHeroCreationModel.cs:249-275`:

```csharp
List<CharacterObject> list = settlement2.Culture.NotableTemplates.Where(x => x.Occupation == occupation).ToList();
foreach (var item in list) { int w = item.GetTraitLevel(DefaultTraits.Frequency) * 10; num += (w > 0) ? w : 100; }
int num3 = settlement2.RandomIntWithSeed((uint)settlement2.Notables.Count, 1, num);
```

Two things worth knowing:

- Weight is `Frequency × 10`, **defaulting to 100 when `Frequency <= 0`**. So a template declaring
  `Frequency` 1–9 becomes *rarer* than an undeclared one, not commoner. No vanilla notable template
  declares `Frequency` at all (verified: zero `id="Frequency"` matches in `spspecialcharacters.xml`),
  so selection is **uniform within each (culture, occupation) bucket**.
- The roll is **deterministic per settlement**, seeded by `settlement.Notables.Count`. The Nth notable
  a given village ever spawns always draws the same template.

### 1.3 The XML

Notable templates live in `Modules/SandBox/ModuleData/spspecialcharacters.xml` — *not*
`SandBoxCore/spnpccharactertemplates.xml`, which declares no notable occupations. They load into
`CultureObject.NotableTemplates` (`CultureObject.cs:206, 516`).

Counts: **18 `occupation="Headman"` and 12 `occupation="RuralNotable"`** — three headmen and two rural
notables per culture across six cultures. That is vanilla only: `Modules/NavalDLC/ModuleData/naval_characters.xml`
adds a seventh culture, Nord, with 3 Headman templates (`spc_nord_headman_1/2/3`) and 2 RuralNotable
templates — so with the NavalDLC loaded it is **seven** cultures.

```xml
<NPCCharacter id="spc_empire_headman_1" name="{=!}empire rebellious headman" voice="earnest"
  is_template="true" default_group="Infantry" is_hero="false" culture="Culture.empire"
  skill_template="SkillSet.spc_empire_headman_1" occupation="Headman">
  <face><face_key_template value="BodyProperty.fighter_empire" /></face>
  <Traits><Trait id="Valor" value="1" /><Trait id="Calculating" value="-1" /></Traits>
  ...
</NPCCharacter>
```

The three headman archetypes per culture are consistently **rebellious** (`Valor +1`,
`Calculating −1`), **conservative** (`Valor −1`, `Generosity +1`), and **devious** (`Calculating +1`,
`Honor −1`). Templates are `is_hero="false"`, which matters in §5.4.

### 1.4 Traits are load-bearing

`DefaultHeroCreationModel.GetTraitsForHero` (L277-305; the notable block is L296-303) rolls `Honor,
Mercy, Generosity, Valor, Calculating` for every notable occupation. These are not flavour: `Mercy <= 0`,
`Generosity <= 0`, and `Honor + Mercy < 0` are hard gates on the issue giver for several of the
rural-notable issues (§6.2). **A generous, merciful rural notable is excluded from three of the ten
rural-capable issues outright** (LandlordNeedsAccessToVillageCommons, LandLordNeedsManualLaborers,
NotableWantsDaughterFound), **plus RuralNotableInnAndOut unless `Honor < −Mercy`**. FamilyFeud's
`Mercy <= 0` gate applies to the *other* village's notable, not the giver
(`SandBox/SandBox.Issues/FamilyFeudIssueBehavior.cs`, `ConditionsHold`).

### 1.5 Occupation is immutable

No `SetNewOccupation` callsite anywhere promotes a village notable. The only calls are
`NavalStorylineData.cs:204`, `FamilyFeudIssueBehavior.cs:715` (→ Wanderer),
`RivalGangMovingInIssueBehavior.cs` (×3), `CompanionRolesCampaignBehavior.cs:262` (→ Lord), and
`HeroCreator.cs:322` — reached through `SetOccupation` for offspring (`HeroCreator.cs:267`, taking the
mother's or father's occupation), not for notables. Heirs get their occupation through the template
roll: `HeroCreator.cs:226` calls `GetRandomTemplateByOccupation(relative.Occupation, …)`, so the heir is
built from a template of the relative's occupation rather than copying it directly. **A village line stays Headman/RuralNotable
forever** — it can never become a Merchant and thereby acquire an income.

### 1.6 Death, heirs, and respawn

Village notables have **no death protection**. `NotablesCampaignBehavior.CanHeroDie` (L85-99) only
vetoes death while one of the notable's caravans is in a map event, and village notables never own
caravans.

**Attrition death** — `CheckAndMakeNotableDisappear` (L295-307), daily. Requires: no
workshop/caravan/alley (always true for villagers), `CanDie(Lost)`, `CanHaveCampaignIssues()` — i.e.
**no active issue** — and `Power < NotableDisappearPowerLimit` (100). Probability:

```
GetNotableDisappearProbability = (100 − Power) / 100 × 0.02f     // max 2 %/day at Power 0
```

On trigger: `KillCharacterAction.ApplyByRemove`, then the issue (if any) completes via AI lord.

Note the interaction: **holding an open issue makes a village notable immortal**, because
`CanHaveCampaignIssues()` is false while one is active. Town notables get the same immunity from
owning assets; village notables can only get it from issues.

**Heirs** — `OnHeroKilled` (L338-362): if `victim.Power >= 100`, `HeroCreator.CreateRelativeNotableHero`
spawns a replacement that inherits every relation with `|value| >= 20` (or any non-zero relation with
co-residents) and re-parents the issue. **Below 100 power the seat is simply vacated.** Dead notables
are unregistered after 7 days (`RemoveNotableCharacterAfterDays`).

**Respawn** — `DailyTickSettlement` (L201-216) keeps a per-settlement 7-day counter, then calls
`SettlementHelper.SpawnNotablesIfNeeded` (`Helpers/SettlementHelper.cs:559`), which gates on a deficit
ratio and, on success, spawns **exactly one** notable:

```csharp
num = ((settlement.Notables.Count > 0) ? ((float)(num2 - settlement.Notables.Count) / (float)num2) : 1f);
num *= MathF.Pow(num, 0.36f);
if (!(randomFloat <= num)) return;
```

A fully-emptied village refills at roughly one notable per week at best.

### 1.7 They never move

There is **no code path that changes a notable's `CurrentSettlement`**. The only
`EnterSettlementAction.ApplyForCharacterOnly` calls for notables are at creation
(`NotablesCampaignBehavior.cs:46`, `SettlementHelper.cs:615`) and at heir replacement
(`ChangeDeadNotable:367`). They do not flee raids, do not relocate, and do not switch allegiance when
the fief changes hands.

---

## 2. Gold — a flat 10,000, forever

### 2.1 The converter

`NotablePowerManagementBehavior.BalanceGoldAndPowerOfNotable`, daily, for every notable:

```csharp
private const int GoldLimitForNotablesToStartGainingPower = 10000;
private const int GoldLimitForNotablesToStartLosingPower  = 5000;
private const int GoldNeededToGainOnePower                = 500;

if (notable.Gold > 10500) {
    int num = (notable.Gold - 10000) / 500;
    GiveGoldAction.ApplyBetweenCharacters(notable, null, num * 500, disableNotification: true);
    notable.AddPower(num);
} else if (notable.Gold < 4500 && notable.Power > 0f) {
    int num2 = (5000 - notable.Gold) / 500;
    GiveGoldAction.ApplyBetweenCharacters(null, notable, num2 * 500, disableNotification: true);
    notable.AddPower(-num2);
}
```

**500 gold ⇄ 1 power**, dead band `[4500, 10500]`. Every notable is born with exactly 10,000
(`NotablesCampaignBehavior.OnHeroCreated:47`) — dead centre of the band. Note the upward leg pays the
surplus to recipient `null`: vanilla **destroys** it.

> ⚠️ **RBM replaces the converter.**
> [`Settlements/NotableWealth.cs`](../RBMCampaign/Settlements/NotableWealth.cs) is a replacing prefix on
> `BalanceGoldAndPowerOfNotable` with vanilla's arithmetic, band and integer truncation unchanged —
> only the counterparty moves. The surplus is **credited to the notable's settlement** instead of being
> destroyed (a town's citizen wealth; a village, which has no citizen pot, its own purse via
> `SettlementWealth.Credit`), and the `< 4500` refill leg is paid out of that same pocket rather than
> minted (in whole 500-gold lots; it buys back only what the pocket can fund). A notable with no
> `CurrentSettlement` skips the day. Gated by `rbmCampaignEnabled`. The power gained per gold is
> identical to vanilla.

### 2.2 Why a villager's gold never moves

Daily income is `DefaultClanFinanceModel.CalculateHeroIncomeFromAssets`, applied by
`ClanVariablesCampaignBehavior.DailyTickHero` under an `if (num > 0)` guard. It has exactly three
terms, and **all three are structurally closed to village notables**:

| Asset | Gate | Why villagers are excluded |
|---|---|---|
| **Caravan** | `DefaultCaravanModel.CanHeroCreateCaravan` opens `if (hero.IsMerchant && …)` | `IsMerchant` is `Occupation == Merchant`; village Merchant quota is 0 |
| **Workshop** | `DefaultWorkshopModel.GetNotableOwnerForWorkshop` iterates `workshop.Settlement.Notables` | `Workshop` objects exist only on `Town` (`InitializeWorkshops` loops `Town.AllTowns`); the candidate pool is the *town's* notable list |
| **Alley** | `AlleyCampaignBehavior` — `foreach (Town allTown in Town.AllTowns)` / `if (settlement.IsTown)` / `if (!notable.IsGangLeader) continue` | double-gated on town **and** gang leader |

The alley case has a subtlety worth recording. **Villages really do contain `Alley` objects** —
`Settlement.Alleys` is populated from the `<CommonAreas>` XML node (`Settlement.cs:1014-1035`), and 274
of the settlements carrying that node in `settlements.xml` are villages (e.g. `castle_village_EN1_1`
has Pasture / Thicket / Bog). But nothing can ever assign them an owner, so they sit at
`AreaState.Empty` for the entire campaign and the income line —
`DefaultClanFinanceModel.cs:899-905`, `if (alley.Owner == hero) goldChange.Add(30f, alley.Name)` —
never fires.

The `SpawnCaravan` branch `settlement.IsVillage ? settlement.Village.TradeBound : …`
(`CaravansCampaignBehavior.cs:507`) is unreachable defensive code for the same reason.

### 2.3 The decisive negative

Across the entire decompiled tree there is **exactly one `GiveGoldAction` callsite where a notable is
the giver**: the converter above. The only other purse debit is `notable.Gold -= …` inside
`ManageCaravanExpensesOfNotable`, which is a `for` over `OwnedCaravans` — an empty list, so the body
never executes.

Things that do **not** pay a village notable:

- **Recruitment.** `RecruitmentCampaignBehavior.ApplyInternal` (L619/625/630) sends the price to
  `GiveGoldAction.ApplyBetweenCharacters(side1Party.LeaderHero, null, …)` — recipient `null`, so the
  gold is **destroyed**. Notables are paid nothing for the men they supply. (Under RBM the price is
  credited to the settlement the man was raised in — a village's own purse — never to the notable;
  §10.)
- **Village production, hearth, tax, trade, prosperity.** Village gold is `SettlementComponent.Gold`,
  hard-capped at `InitialVillageGold = 1000` (`Village.cs:29, 236`), and belongs to the settlement.
  `VillagerCampaignBehavior.cs:332-336` zeroes the returning convoy's trade gold and adds only the
  village tax on it (`CalculateVillageTaxFromIncome`) to `Village.TradeTaxAccumulated`. (Under
  RBM, [`Settlements/VillageGoldStock.cs`](../RBMCampaign/Settlements/VillageGoldStock.cs) suppresses
  the daily clamp and that field becomes the village's real purse — still the settlement's, not a
  notable's.)
- **Quest rewards.** Every issue reward is `GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero,
  RewardGold)` — minted, never drawn from the giver's purse.
- **Inheritance.** `KillCharacterAction.cs:98` transfers a dead hero's gold to `victim.Clan.Leader`,
  inside `if (victim.Clan != null)`. Notables have `Clan == null`. A dead village notable's gold
  simply evaporates; the heir gets a fresh 10,000.

### 2.4 The one-way ratchet

Village notables *can* receive gold, but only from the player, via three paths:

- `NotableSupportersCampaignBehavior.cs:125` — the supporter fee (§5.3).
- `LandLordTheArtOfTheTradeIssueBehavior.cs:680/688/696/751` — genuinely village-gated
  (`issueGiver.IsRuralNotable && issueGiver.CurrentSettlement?.Village != null`).
- `GoldBarterable.cs:68` — plain barter gifting.

Each pushes them above 10,500, and the converter bleeds it back down at 500 gold → 1 power per day
until they settle into `[10000, 10500)`. **They can never fall below 10,000, so the `Gold < 4500`
power→gold branch is unreachable for a village notable.** Their power is a one-way sink from player
gifts and can never be topped up by liquidating assets. Under RBM the converted gold is no longer
destroyed: a player's gift to a village notable ends up in the **village's purse** (§2.1).

> ⚠️ **Consequence for tooling.** Reading `notable.Gold` to gauge wealth or economic health returns
> ~10,000 everywhere in Calradia. It is a transducer, not a stock. `Hero.Power` is the accumulator.
> Equally: their 10,000 is dead capital, so **writing to it breaks nothing** — no vanilla consumer
> reads it except the converter (and, under RBM, its `NotableWealth` replacement).

---

## 3. Power — the only real accumulator

`…GameComponents/DefaultNotablePowerModel.cs`. `Hero.Power` is a plain float; `AddPower` does **not
clamp**, so power can go negative.

### 3.1 Initial value

`GetInitialPower` (L140) — roll `r = MBRandom.RandomFloat`:

| Roll | Power |
|---|---|
| `r < 0.2` | `RandomInt(50, 100)` |
| `0.2 ≤ r < 0.8` | `RandomInt(100, 200)` |
| `r ≥ 0.8` | `RandomInt(200, 400)` |

plus `+ (int)(RandomFloat * 20f)` if the home settlement is a **castle-bound village**.

### 3.2 Daily change

`CalculateDailyPowerChangeForHero` (L45), summed:

| Term | Value |
|---|---|
| Soft cap (when `Power > 100`) | `−(Power − 100) / 500` |
| Per owned alley | `+0.1` — never applies to villagers |
| Active issue | `DefaultIssueEffects.IssueOwnerPower`, typically **−0.1** (see §6.3) |
| Occupation flat | Headman **+0.1**, RuralNotable **+0.1** |
| Castle-bound village | `+0.1` |
| `SupporterOf == CurrentSettlement.OwnerClan` | `+0.2` |

Equilibrium is where the soft cap cancels the rest, i.e. `P* ≈ 100 + 500 × (sum of flat terms)`:

| Situation | Flat sum | Equilibrium |
|---|---|---|
| Plain village notable | +0.1 | **≈ 150** |
| Castle-bound village | +0.2 | **≈ 200** |
| Castle-bound **and** supporting the owner clan | +0.4 | **≈ 300** |
| Plain, with a permanently open issue | 0.0 | **≈ 100** (on the disappearance threshold) |

`DefaultNotablePowerModel._militiaEffect` (L35) is declared but **never used** — power has no militia
effect in current vanilla.

### 3.3 Other power sources

- **Raid** — `NotablePowerManagementBehavior.OnRaidCompleted`: flat **`−5` to every notable of the
  raided village**, and note the `winnerSide` argument is *ignored* (§7.1).
- **Issues** — hand-written `AddPower` deltas in each issue's consequence methods, typically ±5/±10,
  occasionally ±15/±30 (§6.4).
- **Coercion** — `QuestHelper.ApplyGenericMinorMajorCoercionConsequences`: `AddPower(-10f)`.
- **Tutorial** — `TutorialPhaseCampaignBehavior.cs:297` grants the scripted headman
  `NotableDisappearPowerLimit * 2` (= 200) so they cannot vanish.
- **Siege aftermath does NOT apply** — see §7.3.

### 3.4 What power buys a village notable

Only four things, and only the first is routine:

1. **Volunteer tier upgrades** — the `log2(Power / Tier) × 0.01` daily roll (§4.3). This is the
   entire practical output of the power system.
2. **Survival** — the `Power < 100` disappearance roll (§1.6).
3. **An heir** — `Power >= 100` at death.
4. **Clan influence**, if the player has bought patronage — 0.05 / 0.10 / 0.15 per day at power
   ≤ 100 / > 100 / > 200 (the Regular rank is the default, so even power ≤ 0 pays 0.05;
   `DefaultNotablePowerModel.GetPowerRank`, summed in `DefaultClanPoliticsModel.cs:77-84`).

---

## 4. Volunteers — the entire product

### 4.1 The slots

`Hero.VolunteerTypes` is `CharacterObject[6]` (`MaximumNumberOfVolunteers = 6`, `Hero.cs:44/47`),
`[SaveableField(130)]`, allocated in the ctor and **set to `null` wholesale in `Hero.OnDeath`**
(L1960) — so always null-check the array itself, not just its elements.

`CanHaveRecruits` → `DefaultVolunteerModel.CanHaveRecruits` (L120):

```csharp
Occupation occupation = hero.Occupation;
if (occupation == Occupation.Mercenary || (uint)(occupation - 17) <= 5u) return true;
```

Indices 17–22 are Artisan, Merchant, Preacher, Headman, GangLeader, RuralNotable. Both village
occupations qualify.

### 4.2 Filling

`RecruitmentCampaignBehavior.UpdateVolunteersOfNotablesInSettlement` (L215), on
`DailyTickSettlementEvent`. **Nothing is produced while the town is `InRebelliousState`** (for a
village, its bound town).

Per notable, per slot `i`, roll `GetDailyVolunteerProductionProbability`
(`DefaultVolunteerModel.cs:87`):

```csharp
float num = 0.7f;
int num2 = 0;
foreach (Town fief in hero.CurrentSettlement.MapFaction.Fiefs)
    num2 += (fief.IsTown ? (((fief.Prosperity < 3000f) ? 1 : ((fief.Prosperity < 6000f) ? 2 : 3)) + fief.Villages.Count)
                         : fief.Villages.Count);
float num3 = ((num2 < 46) ? ((float)num2/46f * ((float)num2/46f)) : 1f);
num += ((hero.CurrentSettlement != null && num3 < 1f) ? ((1f - num3) * 0.2f) : 0f);
float baseNumber = 0.75f * MathF.Clamp(MathF.Pow(num, index + 1), 0f, 1f);
```

Per-slot daily chance for a large faction (`num = 0.7`):

| Slot | 0 | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|---|
| Chance | 0.525 | 0.368 | 0.257 | 0.180 | 0.126 | 0.088 |

> ⚠️ **The village's own prosperity and hearth play no part whatsoever.** The only settlement term is
> a **faction-wide** size score — every town in the owning kingdom scores 1/2/3 by prosperity
> (`<3000` / `<6000` / `≥6000`) plus its village count, saturating at 46. A small faction gets up to
> `+0.2` on the base. A prosperous, high-hearth village produces recruits at exactly the same rate as
> a burned-out one in the same kingdom.

Notable **Power plays no part in filling** either. Modifiers: the `Cantons` policy `AddFactor(0.2f)` —
but it is keyed on `hero.Clan?.Kingdom`, and notables are clanless (`DefaultHeroCreationModel.GetClan`
returns null without a mother), so it never fires for them — and `Riding.CavalryTactics` (perk of the
village's `TradeBound` town) if the slot's *existing* troop `IsMounted`.

> ⚠️ **RBM gates filling on manpower.**
> [`Recruitment/RecruitPool.cs`](../RBMCampaign/Recruitment/RecruitPool.cs) gives every village a
> manpower pool sized off its **hearth**: ceiling `Hearth × 0.2`, refill `Hearth × 0.03` a day (+10 %
> per level of the bound fief's Roads and Paths), persisted as `RBM_settlementRecruitPool`, starting
> full. A postfix on `UpdateVolunteersOfNotablesInSettlement` (run first, before `RecruitSupply`'s kit
> draw) counts each notable's occupied slots before/after the roll: every new fill costs one man from
> the pool, and fills the pool cannot pay for are cleared again, weakest first. In-place **upgrades
> cost nothing** (the occupied count does not change), so an empty pool freezes the slot count but not
> the tier climb. Purchases and the defence muster neither charge nor refund the pool; a raid shrinks
> it only through the hearth it destroys (the pool is clamped to its current ceiling). Gated by
> `rbmCampaignEnabled`. A 400-hearth village holds up to 80 men and regains 12 a day against 18 slots
> across its three notables, so the pool binds mainly in small or depopulated villages — but it is the
> one place hearth now reaches recruit output.

### 4.3 Which troop, and the upgrade ladder

`DefaultVolunteerModel.GetBasicVolunteer` (L111):

```csharp
if (sellerHero.IsRuralNotable && sellerHero.CurrentSettlement.Village.Bound.IsCastle)
    return sellerHero.Culture.EliteBasicTroop;
return sellerHero.Culture.BasicTroop;
```

- `EliteBasicTroop` **only** for a `RuralNotable` (not a Headman) in a village bound to a **castle**.
  This is the mechanical reason castle villages matter.
- It reads the **notable's** `Hero.Culture`, not the settlement's. Normally identical, but a
  culture-mismatched notable produces their own culture's troops.
- There is **no random elite roll**, and **slot index does not map to tier**. Every slot seeds with the
  same basic troop.

> ⚠️ **RBM overrides this.**
> [`CampaignChanges.TroopPower.cs:56`](../RealisticBattleCombatModule/CombatModule/Campaign/CampaignChanges.TroopPower.cs)
> installs a *replacing prefix* (`DefaultVolunteerModelPatch`) giving a flat **15 % elite / 85 % basic**
> roll for every notable in the world. This discards the castle rule entirely — castle-bound villages
> lose their guaranteed elite recruits, and every town notable gains a 15 % elite chance they should
> not have. It is gated by `rbmCombatEnabled`, **not** by any campaign toggle, so it applies even with
> RBMCampaign off. `UpdateVolunteersOfNotablesInSettlement` calls `GetBasicVolunteer` **once per
> notable per day** (L228), so the roll is per notable-day: every slot that notable fills that day gets
> the same troop.

**In-place upgrade is the only source of high-tier recruits.** Same tick, same probability gate first,
then `RecruitmentCampaignBehavior.cs:241-249`:

```csharp
else if (characterObject.UpgradeTargets.Length != 0 && characterObject.Tier < Models.VolunteerModel.MaxVolunteerTier)
{
    float num = MathF.Log(notable.Power / (float)characterObject.Tier, 2f) * 0.01f;
    if (MBRandom.RandomFloat < num)
        notable.VolunteerTypes[i] = characterObject.UpgradeTargets[MBRandom.RandomInt(characterObject.UpgradeTargets.Length)];
}
```

`MaxVolunteerTier = 4`. The branch (infantry vs archer) is a **uniform** pick from `UpgradeTargets`,
not weighted. A notable at Power 200 upgrades a Tier-1 troop at `log2(200) × 0.01 ≈ 7.6 %` per
successful slot roll. Power ≤ Tier gives a non-positive chance.

After any change the array is insertion-sorted **ascending** — weakest first — by
`Level + (IsMounted ? 0.5 : 0)` (L255-287). Empty slots are skipped over, not compacted: the troops
are reordered among themselves while the nulls stay roughly where they were. This is why "relation
gates index N" means **"you may buy only the weakest few"** — the strongest recruits sit at the high
indices that need the most relation.

### 4.4 Consumption

Three disjoint paths:

| Consumer | Site | Slot bound |
|---|---|---|
| **Player** | gate `RecruitVolunteerVM.cs:173` → `HeroHelper.HeroCanRecruitFromHero` (`HeroHelper.cs:411`); purchase `RecruitmentVM.OnDone` (`…ViewModelCollection.GameMenu.Recruitment/RecruitmentVM.cs:882`, slot nulled L893) | `index <= max` |
| **AI parties** | `RecruitmentCampaignBehavior.RecruitVolunteersFromNotable` (L504) | `index < max` |
| **Garrison auto-recruit** | `GarrisonRecruitmentCampaignBehavior.TickAutoRecruitmentGarrisonChange` | `MaximumIndexGarrisonCanRecruitFromHero` |

Note the **off-by-one**: the player UI uses `<=` and the AI uses `<`, so an AI party effectively gets
one fewer slot than the player at identical relation. The AI loop also starts at a random index and
stops at the first index `>= max`, and buys only if `PartyTradeGold` exceeds the recruit price and the
wage budget covers the man.

AI recruiting runs from `HourlyTickParty` (L291) and again on `OnBeforeSettlementEntered` (L563), which
calls `CheckRecruiting` **7 times** for a normal party (1 for caravans; 1/2/3 for parties in the
player's army depending on `MainParty.PartySizeRatio` vs 0.6 / 0.9).

Garrison auto-recruit draws from the town's own notables **plus all bound villages'** notables, subject
to `boundVillage.VillageState == Normal` (L145) — so a looted village stops feeding its town's
garrison. **Under RBM this consumer is switched off entirely** (`GarrisonRecruitCost`, §10): garrisons
grow from the fief's treasury and its own manpower pool, and village slots drain only to parties.

Slots are set to `null` on purchase and refill only on the next daily tick — there is no immediate
refill anywhere.

### 4.5 Price

`…GameComponents/DefaultPartyWageModel.cs:214` `GetTroopRecruitmentCost(troop, buyerHero, withoutItemCost)`:

Base by `troop.Level`: `≤1 → 10`, `≤6 → 20`, `≤11 → 50`, `≤16 → 100`, `≤21 → 200`, `≤26 → 400`,
`≤31 → 600`, `≤36 → 1000`, `>36 → 1500`. Then `+150` if mounted and `Level < 26`, else `+500`. Then
`+ BaseNumber × 2` for Mercenary/Gangster/CaravanGuard occupations (never a notable volunteer). Then
buyer perk factors, then `LimitMin(1f)`.

**Relation does not change the price.** It only changes how many slots are visible.

> ⚠️ **RBM replaces the price wholesale** (`RecruitSupply.RecruitPrice`, a postfix on this method,
> skipped for `withoutItemCost` quotes). By the recruiter's standing at the village or town he stands
> in: **owner clan or the realm's ruler → free**; a vassal of the settlement's own kingdom → the
> troop's full gear value (mount included) + 5 days of its wage; a mercenary or a lord of another
> realm → that × 1.1; a clanless/kingdomless adventurer → the 5-day wage × 1.1, no gear. Vanilla's
> buyer perks and feats are re-applied and the price floors at 1. Mercenary/gangster/caravan-guard
> troops (and whatever the town tavern is selling) are never free. Relation still plays no part.
> Gated by `RecruitSupply.IsEnabled`.

### 4.6 Militia is entirely separate

`…GameComponents/DefaultSettlementMilitiaModel.cs` has **zero** references to `VolunteerTypes`. Village
militia is `BaseVillageMilitiaChange = 0.5f` plus `Village.Hearth / 400f` ("From Hearths", L112-115),
retirement `−Militia × 0.025` (L110), policies, feats, and the governor perks of the village's
**`TradeBound` town** (L54-56 — the bound town, or for a castle village the town it trades with).

> ⚠️ **RBM rebuilds the militia curve** ([`Settlements/MilitiaUpkeep.cs`](../RBMCampaign/Settlements/MilitiaUpkeep.cs),
> a patch on `CalculateMilitiaChange`), still with no reference to volunteer slots. For a village:
> the same 0.5 + Hearth/400 intake but **no retirement**; a soft cap of 40 % of hearth (raised by
> kingdom policies, never above 70 %) and a hard cap of 75 % of hearth, with growth tapering by
> `(1 − fill)²` between them and 5 %/day of any excess over the hard cap disbanding; an extra
> Hearth/150 a day while below half the soft cap. Each new militiaman is armed with **10 %** of a
> full kit bought off the `TradeBound` town (village purse → town citizens), and the village must
> hold 3× that cost to arm one. Village militia draw **no wage** (factor 0) and a tenth of a field
> troop's maintenance.

The raid "force volunteers" action also ignores notable slots:
`VillageHostileActionCampaignBehavior` grants `ceil(Village.Hearth / 30)` of
`Settlement.Culture.BasicTroop` (`+Notables.Count` with `Roguery.InBestLight` — a head count, not a
slot draw), knocks 80 % off `SettlementHitPoints`, and halves the granted count off `Hearth`.

---

## 5. Relation

### 5.1 Initial

`NotablesCampaignBehavior.SetInitialRelationsBetweenNotablesAndLords` (L122-178), at world-gen. Against
every same-faction clan leader and every co-resident notable: the sum of four uniform `[-1,1]` draws,
× 30, clamped to ±100, then sign-forced by `HeroHelper.NPCPersonalityClashWithNPC` (trait clash →
negative, affinity → positive, 0 → keep sign).

### 5.2 How it changes

**Daily loyalty gain** — `CharacterRelationCampaignBehavior.cs:423-431`, the village branch:

```csharp
if (!item2.IsVillage || !(item2.Village.Bound.Town.Loyalty >= settlementLoyaltyModel.ThresholdForNotableRelationBonus)) continue;
foreach (Hero notable4 in item2.Notables)
  if ((notable4.IsHeadman || notable4.IsRuralNotable) && MBRandom.RandomFloat < 0.05f)
    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(item2.OwnerClan.Leader, notable4, settlementLoyaltyModel.DailyNotableRelationBonus, ...)
```

`ThresholdForNotableRelationBonus = 75f`, `DailyNotableRelationBonus = 1`, roll 5 %/day. So **owning
the village and keeping the bound town's loyalty ≥ 75 yields about +1 relation every 20 days per
village notable.**

There is **no loyalty-based penalty branch for villages**, and — unlike towns, where low security
gives artisans/merchants `DailyNotablePowerPenalty = −1` and gang leaders `+1` — **village notables
receive no daily power change from settlement stats at all.**

**Decay** — `NotablesCampaignBehavior.UpdateNotableRelations` (L218-244), reached only on a 1 %/day
roll per notable (≈ once per 100 days), and it **skips `Clan.PlayerClan` entirely**. For each AI clan
leader, with probability `|relation| / 1000`, apply a 20-point step toward zero.

> **Player relation with a village notable never decays.** Only AI-clan relations mean-revert.

**Hostile actions** — `Actions/BeHostileAction.cs:30-45`. Against a **village settlement while not at
war**: owner clan leader `−4 × value` and **every notable `−4 × value`**, where `value` is 1 (minor
coercion), 2 (major), or 6 (encounter). **At war it returns early — raiding an enemy village costs you
nothing with its notables.** Attacking a villager party (L52-70): at war, each home notable `−1 ×
value`; at peace, owner `−1 × value` and each notable **`−5 × value`**.

**Raid defence** — `CharacterRelationCampaignBehavior.cs:175-177`: when a raid map event is won by the
defender, one random notable of the settlement gains `+5` with each contributing party leader.

**Coercion** — `QuestHelper.ApplyGenericMinorMajorCoercionConsequences` (L104-116): forcing supplies or
volunteers from a village whose notable is your quest giver → `CompleteQuestWithFail`,
`ApplyPlayerRelation(-5)`, `AddPower(-10f)`, `Honor -50`. Callers first check
`QuestHelper.CheckMinorMajorCoercion` (L90-100): the player forcing supplies or volunteers from a
village where the quest sits either with the village's `OwnerClan` or with one of its notables.

### 5.3 What relation buys

**Recruit slot count** — `DefaultVolunteerModel.MaximumIndexHeroCanRecruitFromHero` (L13), summed then
`MathF.Min(6, …)`:

| Term | Value |
|---|---|
| Base | `min(6, max(0, 1 + difficultyBonus + oneOfTheFamilyBonus))` |
| **Relation** | `≥100 → 7`, `≥80 → 6`, `≥60 → 5`, `≥40 → 4`, `≥20 → 3`, `≥10 → 2`, `≥5 → 1`, `≥0 → 0`, `<0 → −1` |
| Same map faction as the notable's settlement | `+1` |
| Buyer is **not** the player | `+1` |
| At war with that faction | `−(1 + notPlayerBonus)` — waived (0) for a minor-faction hero recruiting in a village |
| `Charm.Firebrand` (seller `IsRuralNotable`) | `+SecondaryBonus` |
| `Leadership.CombatTips` (same culture) | `+SecondaryBonus` |
| `Trade.ArtisanCommunity` (seller `IsMerchant`), `Charm.FlexibleEthics` (seller `IsUrbanNotable`), `Engineering.EngineeringGuilds` (seller `IsArtisan`) | — town only |

`difficultyBonus` = `DefaultDifficultyModel.GetPlayerRecruitSlotBonus()`: VeryEasy **2**, Easy **1**,
Realistic **0**.

> ⚠️ **RBM re-roots slot access in land, not relation**
> ([`Economy/RecruitCapacity.cs`](../RBMCampaign/Economy/RecruitCapacity.cs), a prefix on
> `MaximumIndexHeroCanRecruitFromHero`, gated by `rbmCampaignEnabled`). Any member of the clan that
> **owns the village gets the full 6**, whatever the headman thinks of him. The **realm's ruler**
> recruiting from a vassal's fief gets `min(6, max(0, 1 + ladder))`, where `ladder` is the relation
> ladder above applied to his relation with the **owner clan's leader**, not with the notable — with
> no faction, non-player, perk or difficulty terms. Everyone else keeps vanilla's notable-relation
> calculation. Garrison auto-recruit (`MaximumIndexGarrisonCanRecruitFromHero`) is a separate method
> and is untouched.

**Patronage** — `NotableSupportersCampaignBehavior.cs:39-48` registers the lines; the conditions are in
`notable_support_request_on_clickable_condition` (L60-88). The `notable_support_request` line needs
`GetRelationWithPlayer() >= 50f` (L81) and, if already sponsored, `relationWithPlayer >=
notable.GetRelation(SupporterOf.Leader)` with that relation `!= MaxRelationLimit` (L64-79). Cost
`GetInitialNotableSupporterCost = 20000 + 10000 × Clan.PlayerClan.SupporterNotables.Count`. Accepting
sets `SupporterOf = Clan.PlayerClan` and grants `+5` relation (accept consequence, L121-126).

**Player progression** — `DefaultPlayerProgressionModel.cs:11` includes `SupporterNotables.Count ×
0.001f`, which feeds `IssueDifficultyMultiplier` and hence most issue reward formulas.

### 5.4 `SupporterOf` — always null at birth

`NotablesCampaignBehavior.OnHeroCreated` runs for villagers too, but
`HeroHelper.GetRandomClanForNotable` (`Helpers/HeroHelper.cs:427-460`) sets its 50 % chance **only**
for `IsPreacher` (sects) and `IsGangLeader` (mafias). For everything else the probability stays `0f`
and `if (MBRandom.RandomFloat >= num) return null;` fires unconditionally. The `Template.HeroObject.Clan`
branch never triggers either, because the XML templates are `is_hero="false"` with the `<Hero/>` line
commented out.

**A Headman/RuralNotable is therefore always created with `SupporterOf == null`.**

They can acquire one later — `UpdateNotableSupport` (L246-278), daily and 50× at world-gen:

```csharp
// unsupported: for each non-bandit clan != PlayerClan with relation > 50,
//   chance = (relation - 50) / 2000f  to become supporter
// supported: drop it if relation < 0, or with chance (50 - relation) / 500f
```

The acquisition loop **explicitly excludes `Clan.PlayerClan`** — the player can only become a patron by
paying through dialogue. Patronage yields 0.05/0.10/0.15 daily influence by power rank, but **zero
loyalty**, because `GetSettlementLoyaltyChangeDueToNotableRelations` reads only
`town.Settlement.Notables` (§8).

---

## 6. Issues and quests — the only channel to settlement numbers

### 6.1 Cadence

`…CampaignBehaviors/IssuesCampaignBehavior.cs`. Constants (L39-45):
`MinNotableIssueCountForVillages = 1`, **`MaxNotableIssueCountForVillages = 2`** (towns: 1 / 3). So a
village caps at **two concurrent notable issues** across its three notables.

- `OnSettlementDailyTick` (L66-90) counts issues on `settlement.HeroesWithoutParty`; below the min it
  always tries, between min and max it rolls `GetIssueGenerationChance`.
- `CalculateIssueScoreForNotable` (L317) returns **0 if any notable in the same settlement already has
  an issue of that exact type**. Otherwise weighted by `GetFrequencyScore`: `VeryCommon = 6`,
  `Common = 3`, `Rare = 1`, modulated by `_additionalFrequencyScore` (0.2 normally, −0.4 during
  world-gen).
- World-gen seeds `ceil(0.7 × Village.All.Count)` village issues (towns: 0.8).
- **AI can solve an issue out from under the player** — `OnSettlementEntered` (L384-401): when a
  non-player lord not in an army enters, 5 % (own fief) / 1 % (other) chance to pick a random issue in
  the settlement and `CompleteIssueWithAiLord` — only if `CanBeCompletedByAI()` and it is still
  `IsOngoingWithoutQuest`, so an issue the player has taken as a quest is safe.
- Cooldown after any terminal state is **per issue type per hero**, 30 days
  (`DefaultIssueModel.IssueOwnerCoolDownInDays`).

### 6.2 The roster

Every issue gated on a village notable:

| Issue | Gate (condensed) | Freq |
|---|---|---|
| ExtortionByDeserters | `IsHeadman`, village, `Bound?.Town.Security <= 50` | Common |
| HeadmanNeedsGrain | `IsHeadman`, bound to a **town**, type ≠ WheatFarm, town grain `InStore < 30`, local price `> 0.9 × avg` | Common |
| HeadmanNeedsToDeliverAHerd | `IsHeadman \|\| IsRuralNotable`, bound **not** a castle, animal-production type, `Bound.Town.Security <= 60`, `Bound.Notables.Count > 0` | VeryCommon |
| HeadmanVillageNeedsDraughtAnimals | `IsHeadman`, prosperity Low/Mid, mine or Lumberjack | VeryCommon |
| VillageNeedsTools | `IsHeadman`, prosperity `< Mid`, no `IsAnimal` production, item count 0 | VeryCommon |
| NearbyBanditBase | `IsHeadman`, `Bound.Town.Security <= 50`, an infested hideout within `NearbyHideoutMaxRange` (half the average distance between the two closest towns) | VeryCommon |
| LandlordNeedsAccessToVillageCommons | `IsRuralNotable`, WheatFarm, `Mercy <= 0 && Generosity <= 0`, `Security <= 70`, + a sibling village with a free Headman | Common |
| LandLordNeedsManualLaborers | `IsRuralNotable`, `Mercy <= 0`, mine type | VeryCommon |
| LandLordTheArtOfTheTrade | `IsRuralNotable`, `Bound.Town.GetItemPrice(PrimaryProduction) < PrimaryProduction.Value` | VeryCommon |
| LandlordTrainingForRetainers | `IsRuralNotable`, horse production | VeryCommon |
| VillageNeedsCraftingMaterials | `IsRuralNotable`, not at war with the player | Rare |
| MerchantNeedsHelpWithOutlaws | `IsMerchant \|\| IsRuralNotable`, nearby `IsInfested` hideout | VeryCommon |
| FamilyFeud | `IsRuralNotable`, bound to a town, + another village of that town with a free `IsRuralNotable` with `Mercy <= 0` | Rare |
| NotableWantsDaughterFound | `IsRuralNotable`, `CanHaveCampaignIssues()`, `Bound.BoundVillages.Count > 2`, `Age > 2 × HeroComesOfAge`, a female companion template of the culture and a male GangLeader template, `Mercy <= 0 && Generosity <= 0` | Rare |
| RuralNotableInnAndOut | `IsRuralNotable \|\| IsHeadman`, bound is a town, `Mercy + Honor < 0`, culture `BoardGame != None` | Common |

`LesserNobleRevoltIssueBehavior` is **not** a village-notable issue (gated `IsLord`); it merely applies
`ChangeRelationWithRuralNotables(-2)` on one branch.

Note how many gates read `Mercy <= 0`, `Generosity <= 0`, or `Honor + Mercy < 0` — the trait roll in
§1.4 decides whether a rural notable is issue-capable at all.

### 6.3 Passive drag while an issue is open

`GetIssueEffectAmountInternal` per issue:

| Effect | Issues applying it |
|---|---|
| `VillageHearth −0.1 … −0.3` | HeadmanVillageNeedsDraughtAnimals, VillageNeedsTools, VillageNeedsCraftingMaterials (−0.2); LandLordNeedsManualLaborers (−0.3); LandLordTheArtOfTheTrade (−0.1) |
| `IssueOwnerPower −0.1` | the five above, plus LandlordNeedsAccessToVillageCommons, MerchantNeedsHelpWithOutlaws, FamilyFeud, NotableWantsDaughterFound, RuralNotableInnAndOut |
| `SettlementProsperity` | ExtortionByDeserters −1, HeadmanNeedsGrain −0.2, HeadmanNeedsToDeliverAHerd −0.2, NearbyBanditBase −0.2, MerchantNeedsHelpWithOutlaws −0.2, RuralNotableInnAndOut −0.1 |
| `SettlementSecurity −1` | ExtortionByDeserters, LandlordNeedsAccessToVillageCommons, LandlordTrainingForRetainers, NearbyBanditBase, MerchantNeedsHelpWithOutlaws, FamilyFeud |
| `SettlementLoyalty −0.5` | ExtortionByDeserters, HeadmanNeedsGrain |

**Crucial routing detail** — `…GameComponents/DefaultIssueModel.cs:28-58`, `GetIssueEffectsOfSettlement`
walks `settlement.OwnerClan.AliveLords` and `settlement.HeroesWithoutParty`, then:

```csharp
if (!settlement.IsTown && !settlement.IsCastle) return;
// … only then iterate settlement.BoundVillages[*].Settlement.Notables
```

So a **village notable's issue debuffs its bound town's** prosperity/security/loyalty (labelled
`RelatedSettlementIssuesText`) *as well as* its own village's hearth. `IssueOwnerPower` is consumed by
`DefaultNotablePowerModel.cs:114` inside the daily power change.

### 6.4 Resolution deltas

`IssueBase` itself applies **no** relation or power — it only dispatches `OnIssueUpdated`. The single
consumer is `IssuesCampaignBehavior.OnIssueUpdated` (L403-434), which applies relation **only** for
`IssueFail`, `IssueFinishedWithSuccess`, `IssueFinishedWithBetrayal`, `IssueTimedOut`,
`SentTroopsFinishedQuest`, `SentTroopsFailedQuest`, and **only when `issueSolver != null`**.

> **`IssueCancel` and `IssueFinishedByAILord` apply no relation change at all** — only the 30-day
> cooldown. The `Trade.DistributedGoods` / `Trade.LocalConnection` perk multipliers apply to artisans
> and merchants only, so **village notables get no perk-boosted relation.**

Power deltas are hand-written inside each issue. Selected values:

| Issue | Success | Failure |
|---|---|---|
| ExtortionByDeserters | rel `+8`, `AddPower(15)`, town `Security +10`, `Prosperity +100` | rel `−10`, `AddPower(−10)`, `Security −10`, `Prosperity −50` |
| HeadmanNeedsGrain | rel `+5`, `AddPower(10)`, `Prosperity +50`, `+1` with every other notable | alt-solution failure (L270-278): `AddPower(−5)`, `Prosperity −10`, rel `−3` with **all** the village's notables, owner included; quest timeout (L729-741): owner rel `−5`, others `−3`, `AddPower(−5)`, `Prosperity −10`; criminal-rating branch `AddPower(−10)` |
| HeadmanNeedsToDeliverAHerd | rel `+5`, `AddPower(5)`, **`Hearth +50`**; quest success also target town `Prosperity +50` (L532) | `AddPower(−5)`, target `Prosperity −10`, rel `−5` on timeout or `−10` otherwise (L660-662) |
| HeadmanVillageNeedsDraughtAnimals | rel `+5`/`+8`, `Hearth +30`/`+80`/`+50`; `AddPower(10)` on the alt solution (L285) and on quest success (L627) | timeout: `AddPower(−10)`, rel `−5`, `Hearth −30` (L633-635) |
| VillageNeedsTools | alt solution: rel `+5`, `AddPower(10)`, `Hearth +50` (L297-299); quest: `AddPower(10)`, rel `+7` and `Hearth +40` with an exchange item (and `+2` with the other notables), else rel `+5` and `Hearth +20` (L589-614) | timeout: rel `−5`, `AddPower(−10)`, `Hearth −30` (L575-577); `OnFailed`: only `AddPower(−10)`, rel `−5` (L580-584) |
| VillageNeedsCraftingMaterials | rel `+5`, `AddPower(10)`, `Hearth +60` (quest `+30`) | `AddPower(−10)`, `Hearth −40` |
| LandLordNeedsManualLaborers | giver rel `+5`, `AddPower(10)`; on the no-profit-share success where a counter-offer was made, the headman takes rel `−5` and `AddPower(−10)` (L673-674); with profit share and a counter-offer, headman `AddPower(5)` | accepting the counter-offer (`QuestFailPlayerAcceptedCounterOffer`, L681-695): giver `AddPower(−10)`, rel `−3`; headman `AddPower(+10)`, rel `+5`. Timeout (L707-711): giver `AddPower(−10)`, rel `−5` |
| LandlordNeedsAccessToVillageCommons | owner rel `+5`, `AddPower(10)`; **only the target village's `IsHeadman` notables** `−3` and `AddPower(−10)` (L247-254) | owner rel `−5`, `AddPower(−10)`; target headmen rel `+3`, `AddPower(+10)` (L257-273) |
| LandlordTrainingForRetainers | rel `+5`, `AddPower(10)` | rel `−5`, `AddPower(−10)` |
| NearbyBanditBase | `AddPower(+5)`, `Prosperity +10` | `AddPower(−5)`, `Prosperity −10` |
| NotableWantsDaughterFound | rel `+10`, `AddPower(10)`, `Security +10` | rel `−10`, `Prosperity −5`, `Security −5` |
| FamilyFeud | rel `+10`, `_targetNotable −5`, `Security +10` | betray: rel `−10`, target `+5`, `Honor −50` |
| MerchantNeedsHelpWithOutlaws | rel `+3`, `Security +5`, `Prosperity +5` | rel `−5`, `Prosperity −10` — **no power change** |
| RuralNotableInnAndOut | rel `+5`, bound town **`Loyalty +5`** | rel `−5`, `Loyalty −5` — **no power change** |

`LandlordNeedsAccessToVillageCommons` is the only village issue that deliberately **transfers power
between two villages**.

Reward gold is minted to the player, never drawn from the giver — except `LandLordTheArtOfTheTrade`,
whose quest loop is a genuine `GiveGoldAction.ApplyBetweenCharacters(MainHero, QuestGiver, …)`
(one of the three player→villager gold paths in §2.4).

### 6.5 Raids cancel issues

Nearly every village issue carries `!CurrentSettlement.IsRaided && !IsUnderRaid` in its stay-alive
check (`ExtortionByDeserters:269`, `HeadmanVillageNeedsDraughtAnimals:236`,
`LandLordNeedsManualLaborers:181`, `LandLordTheArtOfTheTrade:226`, `LandlordTrainingForRetainers:177`,
`NearbyBanditBase:321`, …). That stay-alive auto-cancel only applies to issues **not yet turned into
quests** — it is gated on `IsOngoingWithoutQuest` (`IssueBase.cs:896`, `IssueManager.cs:262/516`). **A
raid auto-cancels them, and cancel pays no relation** (§6.4).

A quest already in progress is cancelled by its own `VillageBeingRaided` handler instead.
ExtortionByDeserters is the exception (`Issues/ExtortionByDesertersIssueBehavior.cs:901-925`): if the
raider is the deserter party or the player, the quest **fails** — rel `−10`/`−5`, `AddPower(−10)`, town
`Security −10`, `Prosperity −50`. Only a third party's raid gives it a zero-delta cancel.

---

## 7. Raids, sieges, and ownership changes

### 7.1 Raid completed

`NotablePowerManagementBehavior.cs:39-45`:

```csharp
private void OnRaidCompleted(BattleSideEnum winnerSide, RaidEventComponent mapEvent)
{ foreach (Hero notable in mapEvent.MapEventSettlement.Notables) notable.AddPower(-5f); }
```

Flat **`−5` power to every notable of the raided village, regardless of which side won** — the
`winnerSide` argument is ignored. No gold loss. Relation is handled separately by `BeHostileAction`,
which no-ops while at war (§5.2).

Under RBM a raid also costs the village its **standing volunteers**: `RBMSettlementDefenseBehavior`
(§10) empties every notable's slots into the village militia the moment the raid — or a forced levy
of volunteers or supplies — map event starts.

### 7.2 Village states

`ChangeVillageStateAction` only flips `village.VillageState` and dispatches `OnVillageStateChanged`; it
does not touch notables. Downstream notable-relevant effects:

- `CalculateHearthChangeInternal` — `Looted → −1f` hearth/day, and the normal-state growth term is
  skipped.
- `GarrisonRecruitmentCampaignBehavior.cs:143-146` — `if (boundVillage.VillageState != Normal) continue;`,
  so a looted village's notables stop feeding the bound town's auto-garrison.

There is **no village-destruction mechanic** in this build; villages only cycle
`Normal / BeingRaided / Looted / ForcedForSupplies / ForcedForVolunteers`.

### 7.3 Siege aftermath does not reach villages

`SiegeAftermathCampaignBehavior.cs:140-148`:

```csharp
if (settlement.IsTown)
  foreach (Hero notable in settlement.Notables)
    notable.AddPower(notable.Power * GetSiegeAftermathNotablePowerModifierForAftermath(aftermathType));
```

Guarded by `settlement.IsTown` and iterating only the fortification's own notables. **Village notables
are completely untouched by siege aftermath**, including Pillage/Devastate on their bound town.

### 7.4 Ownership changes

The main reaction is `IssueManager.OnSettlementOwnerChanged` (L576-596): if the player is on either
side, notable issues in that settlement — and in its bound villages if the settlement
`IsFortification` — get `InitializeIssueOnSettlementOwnerChange()` (`IssueBase.cs:1005`), which removes
the issue's lord-solution dialogue lines if `IsThereLordSolution`. Separately, every `IssueBase`
subscribes to `OnSettlementOwnerChanged` itself (`IssueBase.cs:503`, handler L1013-1019): on any owner
change of its `IssueSettlement` it calls `ConversationManager.RemoveRelatedLines(this)`. No power, gold,
relation, or `CurrentSettlement` change. **Notables do not switch allegiance and are not replaced.**

---

## 8. What village notables do *not* affect

This is the section to read before assuming a notable hook exists. Grepping `Notable` across every
settlement-output model:

```
…GameComponents/DefaultSettlementMilitiaModel.cs           -> 0 hits
…GameComponents/DefaultSettlementProsperityModel.cs        -> 0 hits
…GameComponents/DefaultSettlementFoodModel.cs              -> 0 hits
…GameComponents/DefaultSettlementTaxModel.cs               -> 0 hits
…GameComponents/DefaultVillageProductionCalculatorModel.cs -> 0 hits
…GameComponents/DefaultVillageTradeModel.cs                -> 0 hits  (also 0 for "Power"/"Hearth")
```

Specifically:

- **Hearth** — `CalculateHearthChangeInternal` (L41-70) is entirely `VillageState`, hearth band
  (`<300 → 4f`, `<600 → 1.2f`, else `0.2f`), `GrazingRights −0.25`, three perks, the bound town's
  `VillageHeartsPerDay` buildings, `EmpireVillageHearthFeat`, and the `VillageHearth` issue effect.
  **Notable power appears nowhere.**
- **Village militia** — `Village.Hearth / 400f` plus a flat base and the trade-bound town's governor
  perks. Not notables.
- **Village production** — `DefaultVillageProductionCalculatorModel.cs:31,82` reads
  `village.GetHearthLevel() + 1` only.
- **Town loyalty** — `DefaultSettlementLoyaltyModel.GetSettlementLoyaltyChangeDueToNotableRelations`
  (L169-190) iterates **`town.Settlement.Notables`** only (`SupporterOf == OwnerClan → +0.5`, supporter
  at war → `−0.5`). Village notables are not in that list, so **a Headman supporting your clan gives
  the bound town zero loyalty.**
- **Town security** — `DefaultSettlementSecurityModel.CalculateSecurityChange` (L83-95) sums hideouts,
  raided villages, siege, prosperity, garrison and issue effects. Its notable-flavoured constants
  (`ThresholdForNotableRelationBonus`, `DailyNotablePowerBonus`, …) are **not used inside the security
  calculation at all** — their only consumer is `CharacterRelationCampaignBehavior.DailyTick`, whose
  power branch is town-only.

**The only channel from a village notable to any settlement number is the issue-effect pipeline
(§6.3)** — and every one of those effects is negative.

This still holds under RBM. RBM replaces or rebuilds several of these models (village production in
`Production/RBMVillageProduction.cs`, militia in `Settlements/MilitiaUpkeep.cs`, which keeps the issue
effects), and none of its replacements reads a notable's presence, power or relation. The arrows RBM
adds run the other way — from the settlement **to** the notables: hearth sizes the manpower pool that
gates their slot fills (§4.2), and the village purse receives what their converted gold and their
recruits' prices bring in (§2.1, §10).

---

## 9. Player interaction surface

- **Conversation family** — `LordConversationsCampaignBehavior.UsesLordConversations` (L123-130)
  includes `IsHeadman` and `IsRuralNotable`, so village notables get the full `hero_main_options` menu.
- **First meeting** — `conversation_headman_introduction_on_condition` (L1698) sets `VILLAGE_NAME`;
  `conversation_rural_notable_introduction_on_condition` (L1709) sets no settlement variable. Both
  require `ConversationManager.CurrentConversationIsFirst`.
- **Issue offer** — `IssuesCampaignBehavior.AddDialogues` (L459+) plus
  `LordConversationsCampaignBehavior.cs:794` `"hero_give_issue"` → `"issue_offer"` (priority 110),
  branching to lord solution / quest / send-troops.
- **Patronage** — `"notable_support_request"` / `"notable_support_end"` (§5.3).
- **Recruiting is a menu, not dialogue** — `PlayerTownVisitCampaignBehavior.cs:154`
  `AddGameMenuOption("village", "recruit_volunteers", "{=E31IJyqs}Recruit troops", …)`.
- **Barter: none.** Grepping `Notable|IsHeadman|IsRuralNotable` across
  `…CampaignBehaviors.BarterBehaviors/` and `…Barterables/` returns **zero hits**. The only gold path is
  the generic `GoldBarterable`.
- **Alley equivalent: none.** Alleys are town-and-gang-leader only (§2.2).
- **Scene NPCs** — `NotableHelperCharacterCampaignBehavior.cs:60,65` spawns one
  `culture.RuralNotableNotary` (`sp_rural_notable_notary`) per `IsRuralNotable || IsHeadman` in the
  village scene; placement via `SandBox.Missions.AgentBehaviors/NotableSpawnPointHandler.cs`.
- **Rumours** — `CommonVillagersCampaignBehavior.GetPossibleIssueRumors` (L611-626) surfaces
  `notable.Issue.IssueAsRumorInSettlement`; `GetBeggarStories` (L629-680; villain check L639) casts a
  `RuralNotable` with `Mercy < 0 && Generosity <= 0` as the villain — it iterates the current
  settlement's `BoundVillages`, so it is a **town** beggar's story about a village notable. `CommonVillagersCampaignBehavior.cs:1051-1060` uses
  `GetRelation(leader)` with `IsHeadman` to pick villager lines.
- **Tutorial** — `StoryMode…/TutorialPhaseCampaignBehavior.cs:292-297` creates a scripted Headman and
  immediately grants `AddPower(200)` so it cannot vanish; L511 creates a RuralNotable.

---

## 10. What RBM currently does

RBM touches village notables mainly through `VolunteerTypes`, plus one write to `Hero.Gold`/`Hero.Power`
(the converter replacement). Nothing in the repo reads or writes `SupporterOf`, and nothing changes a
village notable's daily power drift (`ArtisanStanding` is artisan-only). All recruitment money RBM
re-plumbs lands in settlement purses — for a village, the village's own purse — never in the purse of
the notable who supplied the man.

| File | What it does | Gate |
|---|---|---|
| [`Settlements/NotableWealth.cs`](../RBMCampaign/Settlements/NotableWealth.cs) | Replacing prefix on `BalanceGoldAndPowerOfNotable`: same 500:1 band and arithmetic, but the surplus is credited to the notable's settlement (a village: its purse) instead of destroyed, and the refill leg is paid from there instead of minted (§2.1). | `rbmCampaignEnabled` |
| [`Recruitment/RecruitPool.cs`](../RBMCampaign/Recruitment/RecruitPool.cs) (+ `RBMRecruitPoolCampaignBehavior`) | Hearth-sized manpower pool (max `Hearth × 0.2`, `+Hearth × 0.03`/day, +10 %/level of the bound fief's Roads and Paths). Postfix (priority First) on `UpdateVolunteersOfNotablesInSettlement` charges one man per new fill and clears fills it cannot pay for; upgrades are free (§4.2). Shown on the settlement tooltip. | `rbmCampaignEnabled` |
| [`Economy/RecruitCapacity.cs`](../RBMCampaign/Economy/RecruitCapacity.cs) | Prefix on `MaximumIndexHeroCanRecruitFromHero`: owner clan → all 6 slots; realm ruler → ladder of his relation with the owner clan's leader; everyone else vanilla (§5.3). | `rbmCampaignEnabled` |
| [`Economy/RecruitSupply.cs`](../RBMCampaign/Economy/RecruitSupply.cs) | **Gear leg:** pre/postfix on `UpdateVolunteersOfNotablesInSettlement` counts volunteers as a **multiset** before/after (vanilla re-sorts the array, so slot indices are unusable) and draws each net-new troop's full kit, mount included, in real items off `Village.TradeBound`'s market. The village **pays** that town's citizens the full kit value out of its own purse (budget capped at what the purse holds); a broke village's recruits go out in whatever they had. **Price leg:** replaces the recruit price wholesale (§4.5) and credits what the recruiter paid to the **village purse** (`TroopMarketFeedback.RegisterRecruitPay`), so the village recovers its outlay when a lord comes for the man. Prisoner recruitment pays nothing. | `SpoilsPool.IsEnabled && recruitDrawsFromSettlementStock` (default on) |
| [`Settlements/SettlementDefenseMuster.cs`](../RBMCampaign/Settlements/SettlementDefenseMuster.cs) (`RBMSettlementDefenseBehavior`) | On `MapEventStarted` for a raid, a siege assault, or a forced levy of volunteers or supplies, **empties every living notable's whole `VolunteerTypes` array** into the militia (village) or garrison (fortification; militia if it has no garrison party). Permanently consumed; the pool is not refunded. Own-settlement notables only. A muster that lifts the militia above its hard cap drains back 5 %/day. | `rbmCampaignEnabled` |
| [`Settlements/GarrisonRecruitCost.cs`](../RBMCampaign/Settlements/GarrisonRecruitCost.cs) | Prefix-skips vanilla's `TickAutoRecruitmentGarrisonChange` (and `TickGarrisonChangeForTown`), so the garrison no longer takes a village volunteer per day; garrisons instead grow from the fief's treasury and its **own** manpower pool, never from village slots. | `GarrisonRecruitCost.IsEnabled` (`SpoilsPool.IsEnabled && rbmCampaignEnabled`) |
| [`AI/RBMRecruitBiasBehavior.cs`](../RBMCampaign/AI/RBMRecruitBiasBehavior.cs) | Additive `GoToSettlement` score steering understrength AI lords (below 90 % of their affordable size, within 6 days' travel, target offering at least 4 volunteers) toward free-recruit fiefs, which vanilla's scorer cannot see because it prices off volunteer wage. Reads the first 4 slots per notable (vanilla's same-faction count — it does not model `RecruitCapacity`'s 6 for owners). | `RecruitSupply.IsEnabled` |
| [`Settlements/MilitiaUpkeep.cs`](../RBMCampaign/Settlements/MilitiaUpkeep.cs) | `ArmOneMilitiaman` reuses `RecruitSupply.DrawKitFromMarket` at `MilitiaVillageGearShare` = **0.1** of a kit (no mount) for villages: village purse → trade-bound town's citizens. Militia never draws on notable slots or the manpower pool. | the draw runs only under `RecruitSupply.IsEnabled` |

### 10.1 Known interactions worth watching

- **The `GetBasicVolunteer` override** (§4.3) is gated by `rbmCombatEnabled`, not by any campaign
  toggle. It removes the castle-village elite rule and adds a flat 15 % elite roll everywhere.
- **Slot accumulation.** Because `GarrisonRecruitCost` suppresses vanilla's garrison auto-recruit,
  village slots now drain only to players, AI parties and the defence muster. Higher average slot age
  → more in-place upgrade rolls → **offered troop tiers drift above vanilla over a long campaign.**
  The manpower pool does not counter this: it charges fills, not upgrades.
- **Owners empty their own villages.** With `RecruitCapacity` giving the owner clan all 6 slots and
  `RecruitSupply` pricing them at zero, an owning lord takes the strongest, most-upgraded slots that
  vanilla would have locked behind relation.
- **`RecruitSupply`'s multiset diff observes the overridden `GetBasicVolunteer` output**, so the kit
  values drawn from market move with that patch. It runs after `RecruitPool`'s postfix, so refused
  fills are never armed.
- **The defence muster arms men the village already paid for.** Their kit was bought at fill time;
  moving them into the militia does not refund or re-charge anything, and the muster can carry the
  militia over its hard cap, after which the excess drains at 5 %/day with kit refunds.

---

## 11. Design implications for RBM

Recorded as observations, not proposals.

1. **The purse is free real estate.** A village notable's 10,000 gold is written once and read by
   nothing but the converter. RBM could use `Hero.Gold` as a real per-notable balance without breaking
   a single vanilla consumer — but the converter (now `NotableWealth`, which still runs the 500:1
   exchange, only with the village purse as counterparty) would have to be neutralised first, or any
   balance above 10,500 bleeds into power and the village purse at 500:1.
2. **Volunteer production is nearly blind to what RBM models.** Fill *chance* still reads only the
   kingdom-wide size score; the one local term is RBM's hearth-sized manpower pool, which caps how
   many fills a village can afford but not how fast they roll. Prosperity, food, wealth and the
   village purse still play no part. The purse does decide how well a new recruit is armed (the gear
   draw is capped at what the village can pay).
3. **Power is the only tier lever.** Any attempt to make recruit quality reflect a village's condition
   has to route through `notable.Power`, because `log2(Power / Tier) × 0.01` is the sole upgrade path.
4. **Castles are notable-free.** Anything castle-side must work through bound villages or the
   castle's own manpower pool, which under RBM feeds only its wealth-grown garrison.
5. **Villages already hold `Alley` objects** that nothing owns — an existing, save-safe per-village
   container if a village-side equivalent of the alley economy were ever wanted.
6. **Issue effects are the only vanilla precedent** for a notable moving a settlement number, and they
   are exclusively debuffs applied while an issue is open.

---

## Appendix — quick constant reference

| Constant | Value | Source |
|---|---|---|
| Village notable quota | 1 Headman + 2 RuralNotable | `DefaultNotableSpawnModel` |
| Castle notable quota | 0 | ″ |
| Creation gold grant | 10,000 | `NotablesCampaignBehavior.OnHeroCreated` |
| Gold⇄power rate | 500 : 1 | `NotablePowerManagementBehavior` |
| Converter dead band | `[4500, 10500]` | ″ |
| `NotableDisappearPowerLimit` | 100 | `DefaultNotablePowerModel` |
| Disappearance chance | `(100 − Power)/100 × 0.02`/day | ″ |
| Power rank thresholds | ≤ 100 / > 100 / > 200 → 0.05 / 0.10 / 0.15 influence | ″ |
| Raid power penalty | −5, both sides | `NotablePowerManagementBehavior.OnRaidCompleted` |
| Occupation power drift | Headman +0.1, RuralNotable +0.1 | `DefaultNotablePowerModel` |
| Castle-bound village bonus | +0.1 power/day, +0..20 initial | ″ |
| `MaximumNumberOfVolunteers` | 6 | `Hero.cs:44` |
| `MaxVolunteerTier` | 4 | `DefaultVolunteerModel.cs:11` |
| Slot fill chance (large faction) | 0.525 / 0.368 / 0.257 / 0.180 / 0.126 / 0.088 | ″ |
| Faction size score saturation | 46 | ″ |
| Upgrade chance | `log2(Power / Tier) × 0.01` | `RecruitmentCampaignBehavior.cs:243` |
| Max concurrent village issues | 2 | `IssuesCampaignBehavior.cs:39-45` |
| Issue cooldown | 30 days, per type per hero | `DefaultIssueModel` |
| Notable respawn cadence | weekly check, 1 per success | `SettlementHelper.SpawnNotablesIfNeeded` |
| Dead notable unregister | 7 days | `NotablesCampaignBehavior.WeeklyTick` |
| Loyalty relation bonus | `Bound.Town.Loyalty ≥ 75` → +1 at 5 %/day | `CharacterRelationCampaignBehavior.cs:423` |
| Patronage cost | `20000 + 10000 × SupporterNotables.Count` | `DefaultNotablePowerModel.cs:152` |
| Notable templates | 18 Headman, 12 RuralNotable | `spspecialcharacters.xml` |
| **RBM** manpower pool (village) | max `Hearth × 0.2`, refill `Hearth × 0.03`/day, 1 man per new fill | `RecruitPool` |
| **RBM** slot access | owner clan 6; ruler by relation with owner leader; else vanilla | `RecruitCapacity` |
| **RBM** recruit price | owner/ruler free; vassal at home gear + 5 days' wage; outsider × 1.1 | `RecruitSupply.RecruitPrice` |
| **RBM** elite volunteer roll | 15 % `EliteBasicTroop`, every notable (`rbmCombatEnabled`) | `CampaignChanges.DefaultVolunteerModelPatch` |
