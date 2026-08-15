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
is written `…GameComponents/…`. Researched 2026-08-15 against game v1.4.7.

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
purses, and the notables living in them are the one population that is entirely outside that circuit.

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
notables per culture across six cultures. `Modules/NavalDLC/ModuleData/naval_characters.xml` adds more
Headman templates.

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

`DefaultHeroCreationModel.GetTraitsForHero` (L277-303) rolls `Honor, Mercy, Generosity, Valor,
Calculating` for every notable occupation. These are not flavour: `Mercy <= 0`, `Generosity <= 0`, and
`Honor + Mercy < 0` are hard gates on five of the rural-notable issues (§6.2). **A generous, merciful
rural notable is issue-sterile for most of the roster** — it will sit there producing recruits and
never offering the player anything.

### 1.5 Occupation is immutable

No `SetNewOccupation` callsite anywhere promotes a village notable. The only calls are
`NavalStorylineData.cs:204`, `FamilyFeudIssueBehavior.cs:715` (→ Wanderer),
`RivalGangMovingInIssueBehavior.cs` (×3), and `CompanionRolesCampaignBehavior.cs:262` (→ Lord). Heirs
copy `relative.Occupation` (`HeroCreator.cs:226`). **A village line stays Headman/RuralNotable
forever** — it can never become a Merchant and thereby acquire an income.

### 1.6 Death, heirs, and respawn

Village notables have **no death protection**. `NotablesCampaignBehavior.CanHeroDie` (L85-99) only
vetoes death while one of the notable's caravans is in a map event, and village notables never own
caravans.

**Attrition death** — `CheckAndMakeNotableDisappear` (L286-307), daily. Requires: no
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

**Respawn** — `DailyTickSettlement` (L199-214) keeps a per-settlement 7-day counter, then calls
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
(`ChangeDeadNotable:366`). They do not flee raids, do not relocate, and do not switch allegiance when
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
(`NotablesCampaignBehavior.OnHeroCreated:47`) — dead centre of the band.

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
  gold is **destroyed**. Notables are paid nothing for the men they supply.
- **Village production, hearth, tax, trade, prosperity.** Village gold is `SettlementComponent.Gold`,
  hard-capped at `InitialVillageGold = 1000` (`Village.cs:29, 236`), and belongs to the settlement.
  `VillagerCampaignBehavior.cs:332-336` zeroes convoy trade gold into `Village.TradeTaxAccumulated`.
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
gifts and can never be topped up by liquidating assets.

> ⚠️ **Consequence for tooling.** Reading `notable.Gold` to gauge wealth or economic health returns
> ~10,000 everywhere in Calradia. It is a transducer, not a stock. `Hero.Power` is the accumulator.
> Equally: their 10,000 is dead capital, so **writing to it breaks nothing** — no vanilla consumer
> reads it except the converter.

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
   > 0 / > 100 / > 200 (`DefaultClanPoliticsModel.cs:76-88`).

---

## 4. Volunteers — the entire product

### 4.1 The slots

`Hero.VolunteerTypes` is `CharacterObject[6]` (`MaximumNumberOfVolunteers = 6`, `Hero.cs:43/47`),
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

Notable **Power plays no part in filling** either. Modifiers: the `Cantons` policy `AddFactor(0.2f)`,
and `Riding.CavalryTactics` if the slot's *existing* troop `IsMounted`.

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
> RBMCampaign off.

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

After any change the array is insertion-sorted descending by `Level + (IsMounted ? 0.5 : 0)` with
nulls slid to the tail (L255-287). This is why "relation gates index N" means **"you may buy the N
strongest"**.

### 4.4 Consumption

Three disjoint paths:

| Consumer | Site | Slot bound |
|---|---|---|
| **Player** | `RecruitmentVM.OnDone` (`…ViewModelCollection.GameMenu.Recruitment/RecruitmentVM.cs:884`) | `index <= max` |
| **AI parties** | `RecruitmentCampaignBehavior.RecruitVolunteersFromNotable` (L504) | `index < max` |
| **Garrison auto-recruit** | `GarrisonRecruitmentCampaignBehavior.TickAutoRecruitmentGarrisonChange` | `MaximumIndexGarrisonCanRecruitFromHero` |

Note the **off-by-one**: the player UI uses `<=` and the AI uses `<`, so an AI party effectively gets
one fewer slot than the player at identical relation.

AI recruiting runs from `HourlyTickParty` (L291) and again on `OnBeforeSettlementEntered` (L563), which
calls `CheckRecruiting` **7 times** for a normal party (1 for caravans; 1/2/3 for parties in the
player's army depending on `MainParty.PartySizeRatio` vs 0.6 / 0.9).

Garrison auto-recruit draws from the town's own notables **plus all bound villages'** notables, subject
to `boundVillage.VillageState == Normal` — so a looted village stops feeding its town's garrison.

Slots are set to `null` on purchase and refill only on the next daily tick — there is no immediate
refill anywhere.

### 4.5 Price

`…GameComponents/DefaultPartyWageModel.cs:214` `GetTroopRecruitmentCost(troop, buyerHero, withoutItemCost)`:

Base by `troop.Level`: `≤1 → 10`, `≤6 → 20`, `≤11 → 50`, `≤16 → 100`, `≤21 → 200`, `≤26 → 400`,
`≤31 → 600`, `≤36 → 1000`, `>36 → 1500`. Then `+150` if mounted and `Level < 26`, else `+500`. Then
`+ BaseNumber × 2` for Mercenary/Gangster/CaravanGuard occupations (never a notable volunteer). Then
buyer perk factors, then `LimitMin(1f)`.

**Relation does not change the price.** It only changes how many slots are visible.

### 4.6 Militia is entirely separate

`…GameComponents/DefaultSettlementMilitiaModel.cs` has **zero** references to `VolunteerTypes`. Village
militia is `BaseVillageMilitiaChange = 0.5f` plus `Village.Hearth / 400f` ("From Hearths", L112-115),
retirement `−Militia × 0.025`, policies, feats, and the **bound town's governor** perks (L54-56).

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

**Daily loyalty gain** — `CharacterRelationCampaignBehavior.cs:421-435`, the village branch:

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

**Decay** — `NotablesCampaignBehavior.UpdateNotableRelations` (L216-241), reached only on a 1 %/day
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
`ApplyPlayerRelation(-5)`, `AddPower(-10f)`, `Honor -50`.

### 5.3 What relation buys

**Recruit slot count** — `DefaultVolunteerModel.MaximumIndexHeroCanRecruitFromHero` (L13), summed then
`MathF.Min(6, …)`:

| Term | Value |
|---|---|
| Base | `min(6, max(0, 1 + difficultyBonus + oneOfTheFamilyBonus))` |
| **Relation** | `≥100 → 7`, `≥80 → 6`, `≥60 → 5`, `≥40 → 4`, `≥20 → 3`, `≥10 → 2`, `≥5 → 1`, `≥0 → 0`, `<0 → −1` |
| Same map faction as the notable's settlement | `+1` |
| Buyer is **not** the player | `+1` |
| At war with that faction | `−(1 + notPlayerBonus)` |
| `Charm.Firebrand` (seller `IsRuralNotable`) | `+SecondaryBonus` |
| `Leadership.CombatTips` (same culture) | `+SecondaryBonus` |
| `Engineering.EngineeringGuilds` (seller `IsArtisan`) | — town only |

`difficultyBonus` = `DefaultDifficultyModel.GetPlayerRecruitSlotBonus()`: VeryEasy **2**, Easy **1**,
Realistic **0**.

**Patronage** — `NotableSupportersCampaignBehavior.cs:39-48`. The `notable_support_request` line needs
`GetRelationWithPlayer() >= 50f` and, if already sponsored, `relationWithPlayer >=
notable.GetRelation(SupporterOf.Leader)` with that relation `!= MaxRelationLimit`. Cost
`GetInitialNotableSupporterCost = 20000 + 10000 × Clan.PlayerClan.SupporterNotables.Count`. Accepting
sets `SupporterOf = Clan.PlayerClan` and grants `+5` relation.

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
- **AI can solve an issue out from under the player** — `OnSettlementEntered` (L440+): when a non-player
  lord enters, 5 % (own fief) / 1 % (other) chance to `CompleteIssueWithAiLord`.
- Cooldown after any terminal state is **per issue type per hero**, 30 days
  (`DefaultIssueModel.IssueOwnerCoolDownInDays`).

### 6.2 The roster

Every issue gated on a village notable:

| Issue | Gate (condensed) | Freq |
|---|---|---|
| ExtortionByDeserters | `IsHeadman`, village, `Bound?.Town.Security <= 50` | Common |
| HeadmanNeedsGrain | `IsHeadman`, bound to a **town**, type ≠ WheatFarm, town grain `InStore < 30`, local price `> 0.9 × avg` | Common |
| HeadmanNeedsToDeliverAHerd | `IsHeadman \|\| IsRuralNotable`, bound **not** a castle, animal-production type, `Bound.Town.Security <= 60` | VeryCommon |
| HeadmanVillageNeedsDraughtAnimals | `IsHeadman`, prosperity Low/Mid, mine or Lumberjack | VeryCommon |
| VillageNeedsTools | `IsHeadman`, prosperity `< Mid`, no `IsAnimal` production, item count 0 | VeryCommon |
| NearbyBanditBase | `IsHeadman`, `Bound.Town.Security <= 50` | VeryCommon |
| LandlordNeedsAccessToVillageCommons | `IsRuralNotable`, WheatFarm, `Mercy <= 0 && Generosity <= 0`, `Security <= 70`, + a sibling village with a free Headman | Common |
| LandLordNeedsManualLaborers | `IsRuralNotable`, `Mercy <= 0`, mine type | VeryCommon |
| LandLordTheArtOfTheTrade | `IsRuralNotable`, `Bound.Town.GetItemPrice(PrimaryProduction) < PrimaryProduction.Value` | VeryCommon |
| LandlordTrainingForRetainers | `IsRuralNotable`, horse production | VeryCommon |
| VillageNeedsCraftingMaterials | `IsRuralNotable`, not at war with the player | Rare |
| MerchantNeedsHelpWithOutlaws | `IsMerchant \|\| IsRuralNotable`, nearby `IsInfested` hideout | VeryCommon |
| FamilyFeud | `IsRuralNotable`, bound to a town, + another village of that town with a free `IsRuralNotable` with `Mercy <= 0` | Rare |
| NotableWantsDaughterFound | `IsRuralNotable`, `Bound.BoundVillages.Count > 2`, `Age > 2 × HeroComesOfAge`, `Mercy <= 0 && Generosity <= 0` | Rare |
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
| HeadmanNeedsGrain | rel `+5`, `AddPower(10)`, `Prosperity +50`, `+1` with every other notable | rel `−3` others, `AddPower(−5)`, `Prosperity −10`; quest worst branch `AddPower(−10)` |
| HeadmanNeedsToDeliverAHerd | rel `+5`, `AddPower(5)`, **`Hearth +50`** | `AddPower(−5)`, target `Prosperity −10` |
| HeadmanVillageNeedsDraughtAnimals | rel `+5`/`+8`, `Hearth +30`/`+80`/`+50` | rel `−5`, `Hearth −30` |
| VillageNeedsTools | rel `+5`/`+7`, `AddPower(10)`, `Hearth +50` | rel `−5`, `AddPower(−10)`, `Hearth −30` |
| VillageNeedsCraftingMaterials | rel `+5`, `AddPower(10)`, `Hearth +60` (quest `+30`) | `AddPower(−10)`, `Hearth −40` |
| LandLordNeedsManualLaborers | rel `+5`, `AddPower(10)` | betray branch: headman rel `−5`, `AddPower(−10)` |
| LandlordNeedsAccessToVillageCommons | owner rel `+5`, `AddPower(10)`; **target village's notables `−3` and `AddPower(−10)`** | inverted |
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
`NearbyBanditBase:321`, …). **A raid auto-cancels them, and cancel pays no relation** (§6.4) — so a
raid silently voids the player's in-progress work with that village.

---

## 7. Raids, sieges, and ownership changes

### 7.1 Raid completed

`NotablePowerManagementBehavior.cs:38-44`:

```csharp
private void OnRaidCompleted(BattleSideEnum winnerSide, RaidEventComponent mapEvent)
{ foreach (Hero notable in mapEvent.MapEventSettlement.Notables) notable.AddPower(-5f); }
```

Flat **`−5` power to every notable of the raided village, regardless of which side won** — the
`winnerSide` argument is ignored. No gold loss. Relation is handled separately by `BeHostileAction`,
which no-ops while at war (§5.2).

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

The only reaction is `IssueManager.OnSettlementOwnerChanged` (L576-596): if the player is on either
side, notable issues in that settlement — and in its bound villages if the settlement
`IsFortification` — get `InitializeIssueOnSettlementOwnerChange()`. No power, gold, relation, or
`CurrentSettlement` change. **Notables do not switch allegiance and are not replaced.**

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
- **Village militia** — `Village.Hearth / 400f` plus a flat base and the bound town's governor perks.
  Not notables.
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

---

## 9. Player interaction surface

- **Conversation family** — `LordConversationsCampaignBehavior.UsesLordConversations` (L124-131)
  includes `IsHeadman` and `IsRuralNotable`, so village notables get the full `hero_main_options` menu.
- **First meeting** — `conversation_headman_introduction_on_condition` (L1698) sets `VILLAGE_NAME`;
  `conversation_rural_notable_introduction_on_condition` (L1710) sets no settlement variable. Both
  require `ConversationManager.CurrentConversationIsFirst`.
- **Issue offer** — `IssuesCampaignBehavior.AddDialogues` (L460+) plus
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
  `notable.Issue.IssueAsRumorInSettlement`; `GetBeggarStories` (L634-650) casts a `RuralNotable` with
  `Mercy < 0 && Generosity <= 0` as the villain. `CommonVillagersCampaignBehavior.cs:1051-1060` uses
  `GetRelation(leader)` with `IsHeadman` to pick villager lines.
- **Tutorial** — `StoryMode…/TutorialPhaseCampaignBehavior.cs:292-297` creates a scripted Headman and
  immediately grants `AddPower(200)` so it cannot vanish; L511 creates a RuralNotable.

---

## 10. What RBM currently does

RBM touches village notables **only through `VolunteerTypes`**. Nothing in the repo reads or writes
notable `Hero.Gold`, `Hero.Power`, or `SupporterOf`. All recruitment money RBM re-plumbs lands in
`SettlementWealth` pots — never in the purse of the notable who supplied the man.

| File | What it does | Gate |
|---|---|---|
| [`Economy/RecruitSupply.cs`](../RBMCampaign/Economy/RecruitSupply.cs) | Pre/postfix on `UpdateVolunteersOfNotablesInSettlement` counts volunteers as a **multiset** before/after (vanilla re-sorts the array, so slot indices are unusable) and draws each net-new troop's kit in real items off the supply market — village → `Village.TradeBound`. Separately **replaces the recruit price wholesale**: owner clan / realm ruler → free, vassal at home → gear + 5× wage, mercenary or lord abroad → +10 %. | `SpoilsPool.IsEnabled && recruitDrawsFromSettlementStock` |
| [`Settlements/SettlementDefenseMuster.cs`](../RBMCampaign/Settlements/SettlementDefenseMuster.cs) | On raid or siege assault, **empties every notable's whole `VolunteerTypes` array** into militia (village) or garrison (fortification). Permanently consumed. | `rbmCampaignEnabled` |
| [`Settlements/GarrisonRecruitCost.cs`](../RBMCampaign/Settlements/GarrisonRecruitCost.cs) | Prefix-skips vanilla's garrison auto-recruit, so it no longer eats a village volunteer per day; replaced by a wealth-driven growth curve. | `GarrisonRecruitCost.IsEnabled` |
| [`AI/RBMRecruitBiasBehavior.cs`](../RBMCampaign/AI/RBMRecruitBiasBehavior.cs) | Additive `GoToSettlement` score steering understrength AI lords toward free-recruit fiefs, which vanilla's scorer cannot see because it prices off volunteer wage. Reads the first 4 slots per notable. | `RecruitSupply.IsEnabled` |
| [`Settlements/MilitiaUpkeep.cs`](../RBMCampaign/Settlements/MilitiaUpkeep.cs) | `ArmOneMilitiaman` reuses `RecruitSupply.DrawKitFromMarket` at `MilitiaVillageGearShare` (≈ ¼ kit) for villages. | `RecruitSupply.IsEnabled` |

### 10.1 Known interactions worth watching

- **The `GetBasicVolunteer` override** (§4.3) is gated by `rbmCombatEnabled`, not by any campaign
  toggle. It removes the castle-village elite rule and adds a flat 15 % elite roll everywhere.
- **Slot accumulation.** Because `GarrisonRecruitCost` suppresses vanilla's garrison auto-recruit,
  village slots now drain only to players and AI parties. Higher average slot age → more in-place
  upgrade rolls → **offered troop tiers drift above vanilla over a long campaign.**
- **`RecruitSupply`'s multiset diff observes the overridden `GetBasicVolunteer` output**, so the kit
  values drawn from market move with that patch.

---

## 11. Design implications for RBM

Recorded as observations, not proposals.

1. **The purse is free real estate.** A village notable's 10,000 gold is written once and read by
   nothing but the converter. RBM could use `Hero.Gold` as a real per-notable balance without breaking
   a single vanilla consumer — but the converter would have to be neutralised first, or any balance
   above 10,500 silently bleeds into power at 500:1.
2. **Volunteer production is blind to everything RBM models.** Local prosperity, hearth, food, wealth,
   and the settlement's purse have no influence on recruit output; only kingdom-wide town count does.
   This is the sharpest mismatch between vanilla and RBM's locality-driven economy.
3. **Power is the only tier lever.** Any attempt to make recruit quality reflect a village's condition
   has to route through `notable.Power`, because `log2(Power / Tier) × 0.01` is the sole upgrade path.
4. **Castles are notable-free.** Anything castle-side must work through bound villages or the garrison
   path that RBM already suppresses.
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
| Power rank thresholds | 0 / 100 / 200 → 0.05 / 0.10 / 0.15 influence | ″ |
| Raid power penalty | −5, both sides | `NotablePowerManagementBehavior.OnRaidCompleted` |
| Occupation power drift | Headman +0.1, RuralNotable +0.1 | `DefaultNotablePowerModel` |
| Castle-bound village bonus | +0.1 power/day, +0..20 initial | ″ |
| `MaximumNumberOfVolunteers` | 6 | `Hero.cs:43` |
| `MaxVolunteerTier` | 4 | `DefaultVolunteerModel.cs:11` |
| Slot fill chance (large faction) | 0.525 / 0.368 / 0.257 / 0.180 / 0.126 / 0.088 | ″ |
| Faction size score saturation | 46 | ″ |
| Upgrade chance | `log2(Power / Tier) × 0.01` | `RecruitmentCampaignBehavior.cs:243` |
| Max concurrent village issues | 2 | `IssuesCampaignBehavior.cs:39-45` |
| Issue cooldown | 30 days, per type per hero | `DefaultIssueModel` |
| Notable respawn cadence | weekly check, 1 per success | `SettlementHelper.SpawnNotablesIfNeeded` |
| Dead notable unregister | 7 days | `NotablesCampaignBehavior.WeeklyTick` |
| Loyalty relation bonus | `Bound.Town.Loyalty ≥ 75` → +1 at 5 %/day | `CharacterRelationCampaignBehavior.cs:421` |
| Patronage cost | `20000 + 10000 × SupporterNotables.Count` | `DefaultNotablePowerModel.cs:152` |
| Notable templates | 18 Headman, 12 RuralNotable | `spspecialcharacters.xml` |
