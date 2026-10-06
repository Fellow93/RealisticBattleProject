# Troop perk audit: summary

What each hero perk does when RBM gives it to a **regular troop** (`RBMXML/rbm_troop_perks.xml`, loaded by
`RBMConfig/Shared/TroopPerks.cs`). Audited 2026-10-06 against the v1.5.4 sources in `decompiled/` (with War Sails)
and the RBM tree on branch `patreon-1.5.x`.

The rule: a troop perk works only when a mission-time check asks `CharacterObject.GetPerkValue` for the troop's
own character (RBM's postfix in `RealisticBattleCombatModule/CombatModule/TroopPerks/TroopPerkEffects.cs` answers
it) **and** RBM still runs that check. Captain, party-leader and governor halves never reach a troop. A check
that asks a `Hero`, `agent.IsHero`, or only the player's agent never sees a troop perk.

Files:

- `<group>.json`: the data, one entry per perk: verdict, confidence, summary, conditions, call sites, RBM
  notes. `Build-TroopPerkData.ps1` merges it into the editor.
- `<group>.md`: the group's table and notes.
- `_rbm-bypass-map.md`: every RBM patch that replaces a vanilla method with perk checks in it, and what is lost.

Verdict meanings are in `../README.md` ("The audit").

## Verdicts per group

| Group | works | conditional | partial | hero-only | campaign-only | no-check-found | rbm-bypassed | no-personal-effect | Total |
|---|---|---|---|---|---|---|---|---|---|
| One Handed (`onehanded`) | 13 | 1 | 2 | 0 | 0 | 0 | 1 | 4 | 21 |
| Two Handed (`twohanded`) | 11 | 5 | 0 | 2 | 0 | 0 | 0 | 0 | 18 |
| Polearm (`polearm`) | 3 | 14 | 0 | 0 | 0 | 0 | 0 | 4 | 21 |
| Bow (`bow`) | 10 | 4 | 1 | 2 | 0 | 0 | 0 | 4 | 21 |
| Crossbow (`crossbow`) | 6 | 8 | 1 | 1 | 0 | 1 | 0 | 4 | 21 |
| Throwing (`throwing`) | 4 | 14 | 1 | 2 | 0 | 0 | 0 | 0 | 21 |
| Riding and Athletics (`riding-athletics`) | 18 | 5 | 2 | 4 | 2 | 0 | 0 | 10 | 41 |
| Non-combat skills + War Sails (`noncombat-naval`) | 4 | 22 | 3 | 6 | 62 | 0 | 0 | 175 | 272 |
| **Total** | **69** | **73** | **10** | **17** | **64** | **1** | **1** | **201** | **436** |

No perk is `unclear`. 152 perks do something for a troop (works + conditional + partial); 83 have a personal
half that still does nothing; 201 have no personal half at all.

The groups did not grade "conditional" the same way: Polearm marks most situational damage bonuses
`conditional` (e.g. Pikeman, +2% on foot), where One Handed marks the same kind of bonus `works` (e.g. Cavalry,
+5% mounted). Read the "When" line, not only the badge.

## Perks that do nothing for a troop

These 83 are `TroopPerks.NoTroopEffectPerkIds`. The loader still loads them (they show in the troop tooltip) and
logs each one to `rgl_log`. The editor marks them red.

**Hero only (17).** The personal half is checked for heroes, or only for the player's agent.

| Perk | Why |
|---|---|
| BowHorseMaster, CrossbowMountedCrossbowman, TwoHandedProjectileDeflection | Agent flag set only in the `agent.IsHero` branch of `SandboxAgentStatCalculateModel.InitializeAgentStats`. |
| TwoHandedBaptisedInBlood | Read via `HeroObject` in `BattleCampaignBehavior.OnHeroCombatHit`, which is raised for heroes only. |
| ThrowingRunningThrow | Vanilla `CalculateStrikeMagnitudeForMissile` and RBM's `MagnitudeChanges.ApplyRunningThrowPerk` both need a hero. |
| BowEagleEye, ThrowingFocus | Camera zoom, read only for `Mission.MainAgent`. |
| RidingWellStraped | Only the main hero's own horse death/lame roll (`Hero.MainHero`). |
| AthleticsDurable, AthleticsSteady, AthleticsStrong | +1 attribute granted to the hero when learned; no battle check. |
| VigorousSmith, StrongSmith, EnduringSmith, WeaponMasterSmith | +1 attribute / focus granted to the hero when learned. |
| MedicineMinisterOfHealth | `GetEffectiveMaxHealth` reads the party leader hero's perk and skill, never the troop's. |
| ShipwrightsInsight (War Sails) | `NavalMissionSiegeEngineCalculationModel.CalculateDamage` requires `attackerAgent.IsHero`. |

**No check found (1).** CrossbowLongShots: the crossbow zoom is only read for the player's own camera.
It is the same mechanism as BowEagleEye and ThrowingFocus, which the other auditors called hero-only; the
outcome is the same.

**Removed by RBM, for everyone (1).** OneHandedArrowCatcher: RBMCombat sets
`AttributeShieldMissileCollisionBodySizeAdder = 0.01f` after the stat build
(`RealisticBattleCombatModule/CombatModule/Damage/DamageRework.HitReaction.cs:26`), so heroes lose it too.

**Campaign only (64).** The personal half acts on the campaign map or on a hero's own actions; nothing in battle.

| Skill | Perks |
|---|---|
| Smithing (13) | IronYield, CharcoalYield, SteelMaker, SteelMaker2, SteelMaker3, CuriousSmelter, CuriousSmith, PracticalRefiner, PracticalSmelter, PracticalSmith, ExperiencedSmith, MasterSmith, LegendarySmith |
| Athletics (2) | AthleticsImposingStature (persuasion), AthleticsStamina (crafting stamina, not RBM's combat stamina) |
| Tactics (1) | TacticsBesieged |
| Roguery (7) | RogueryTwoFaced, RogueryDeepPockets, RogueryManhunter, RogueryScarface, RogueryWhiteLies, RoguerySmugglerConnections, RogueryRogueExtraordinaire |
| Charm (18) | CharmVirile, CharmSelfPromoter, CharmOratory, CharmWarlord, CharmForgivableGrievances, CharmMeaningfulFavors, CharmInBloom, CharmYoungAndRespectful, CharmFlexibleEthics, CharmEffortForThePeople, CharmSlickNegotiator, CharmGoodNatured, CharmTribute, CharmMoralLeader, CharmNaturalLeader, CharmParade, CharmCamaraderie, CharmImmortalCharm |
| Leadership (3) | LeadershipPresence, LeadershipFamousCommander, LeadershipWePledgeOurSwords |
| Trade (13) | TradeAppraiser, TradeWholeSeller, TradeCaravanMaster, TradeMarketDealer, TradeTravelingRumors, TradeLocalConnection, TradeDistributedGoods, TradeTollgates, TradeSwordForBarter, TradeSelfMadeMan, TradeSilverTongue, TradeManOfMeans, TradeEverythingHasAPrice |
| Steward (1) | StewardSweatshops |
| Medicine (4) | MedicineWalkItOff, MedicineBestMedicine, MedicineGoodLodging, MedicineCheatDeath |
| War Sails (2) | Arr, Resilience |

**No personal effect (201)** perks only have captain / party leader / governor / quartermaster / scout / surgeon /
engineer / clan leader / first mate / navigator halves. The loader detects those from the `PerkObject` and logs
them; they are not in the hard-coded set.

Several perks that do work are useless in practice for shipped troops because they need a skill no regular troop
has: OneHandedWayOfTheSword, TwoHandedWayOfTheGreatAxe, PolearmWayOfTheSpear, RidingTheWayOfTheSaddle (above 250),
and Bow Deadshot, Crossbow MightyPull, Throwing UnstoppableForce (above 200). CraftingSharpenedEdge/SharpenedTip
need player-crafted weapons.

## Harmful

**AthleticsMightyBlow.** `DefaultCharacterStatsModel.MaxHitpoints`
(`decompiled/TaleWorlds.CampaignSystem/TaleWorlds.CampaignSystem.GameComponents/DefaultCharacterStatsModel.cs:41-45`)
adds `GetSkillValue(Athletics) - MaxSkillRequiredForEpicPerkBonus` to max HP whenever the perk is present, with no
`> 0` guard and no floor. `MaxSkillRequiredForEpicPerkBonus` is 250
(`DefaultCharacterDevelopmentModel.cs:71`; War Sails' model forwards it). Heroes can only take the perk at 275
Athletics, so for them it is always a gain. A troop with Athletics 100 gets -150 HP (vlandian_sharpshooter, 130:
-120) and may spawn with little or no health.

**Resolved (user decision, 2026-10-06):** `TroopPerks.Load` only gives Mighty Blow to a troop with Athletics above
250 and skips it otherwise (logged), so a troop's HP bonus from it is always above 0. The editor marks it
"skipped" for such a troop. Its stun half is still backwards (see below).

## RBM/vanilla issues found (for the user to decide, not fixed)

"Troops only" means the issue only shows up because RBM grants hero perks to troops; "everyone" means heroes and
the player are affected too.

RBM:

- RBMAI sets a fixed `WeaponRotationalAccuracyPenaltyInRadians` for every mounted shooter
  (`RealisticBattleAiModule/AiModule/Agents/AgentStats.cs:207` bow 0.015, `:225` crossbow 0.010, `:243` other 0.010),
  after the perks are applied: Bow QuickAdjustments and the turning half of Crossbow Steady are lost on horseback.
  Everyone (player too), only with RBM AI on.
- RBMCombat forces the shield missile-catch size to 0.01 (`DamageRework.HitReaction.cs:26`): OneHanded ArrowCatcher
  (personal and captain) and the captain half of OneHanded ShieldWall do nothing. Everyone; by design.
- RBMAI skips the whole morale shock when a siege defender is killed, wounded or flees
  (`RealisticBattleAiModule/AiModule/Siege/SiegePatches.cs:427-471`, `AgentMoraleInteractionLogicPatch`): TwoHanded
  Terror/Hope, Riding ThunderousCharge/AnnoyingBuzz, Leadership MakeADifference, Crossbow Terror, Leadership
  HeroicLeader, Polearm StandardBearer, Tactics Tight/LooseFormations, Medicine HealthAdvise, Steward PriceOfLoyalty
  and morale resistance do nothing for those casualties. Everyone.
- On a metal shield (wielded or on the back) RBM strips `CanPenetrateShield` after `DecideMissileWeaponFlags`
  (`Ranged/RangedRework.Collision.cs:185-215`): Throwing Impale and War Sails CrewOfSpears never pierce metal shields.
  Everyone; by design.
- RBM turns missile hits on a shield on the back into shield blocks (`RangedRework.Collision.cs:233-278`): Crossbow
  Pavise only matters for missiles that pass a non-metal shield on the back, so it is nearly moot. Everyone.
- Sling launch speed comes from `MissileBallistics.GetSlingSpeed`, which never reads
  `AgentDrivenProperties.MissileSpeedMultiplier` (`Ranged/RangedRework.MissileSpeed.cs:191`,
  `RBMConfig/Shared/MissileBallistics.cs:329`): Throwing PerfectTechnique and the speed half of Throwing
  UnstoppableForce, which RBMAI carefully re-applies (`AgentStats.cs:36-59`, `:289`), do nothing for slings. Everyone
  (heroes too). Whether javelins/axes/knives keep the multiplier natively is UNVERIFIED (bypass map #19).
- A javelin with Throwing Impale skips RBM's pilum-through-shield partial wound
  (`Ranged/RangedRework.ShieldPenetration.cs:110-116`) and takes the engine's full `CanPenetrateShield` path instead.
  Everyone with Impale.
- RBM's couch/brace shortcuts decide the outcome before the perk checks: strong couched/braced thrusts always
  dismount (`Horse/HorseChanges.MountedCombat.cs:62-87`) and couched/braced hits always knock down or back
  (`Damage/DamageRework.Blows.cs:33-61`), so Polearm Braced, HardKnock and KeepAtBay only matter for active thrusts and
  weak passive hits. Polearm UnstoppableForce (x4 against shields) is compressed by `CalculateCouchedLanceMagnitude`
  (`Damage/DamageRework.Core.cs:51-84`) to about x1.9 at full gallop (estimated, not tested). Everyone.
- RBM's own armor-weight terms use raw `GetTotalWeightOfArmor`, so Athletics FormFittingArmor only lightens vanilla
  encumbrance, not RBM stamina cost (`RealisticBattleAiModule/AiModule/Stance/MeleeBlowPatch.cs:248`), max posture
  (`Stance/Stance.cs:140`) or horse speed/charge weight (`Horse/HorseChanges.MountStats.cs:58`, `:109`). Everyone.
- RBM posture-break staggers and knockdowns ignore the stagger perks Riding DauntlessSteed and Athletics Spartan
  (they only raise vanilla's shrug-off threshold). Everyone.
- RBMAI's posture block-perk factors (WrappedHandles, StrongGrip, CounterWeight, Fury, SteelCoreShields, ShieldWall,
  Engineering Scaffolds) are cached per Stance (`Stance/MeleeBlowPatch.Math.cs:291-417`) and not recomputed when the
  troop-perk map changes mid-mission. Troops only; minor.
- RBM's battle-XP replacement applies Leadership InspiringLeader at sea, which vanilla skipped
  (`Campaign/CampaignChanges.Xp.cs:115-118`); its survival replacement drops vanilla's "blunt cannot kill" rule and
  the DoctorsOath surgery XP (`Campaign/CampaignChanges.Survival.cs:26-86`). Everyone; not troop perks.
- RBMAI drops vanilla's wet-weather -20% bow/crossbow missile speed when it reassigns `MissileSpeedMultiplier`
  (`AgentStats.cs:289`). Everyone; intentional per the bypass map.
- Harmless: `[HarmonyPriority(Priority.High)]` sits on `GetReloadPerkFactor`, not on `Postfix`
  (`Ranged/RangedRework.Reload.cs:29`); the `HeroObject.GetPerkValue(Impale)` fallback in `PenetratesShieldOnBack`
  (`RangedRework.Collision.cs:303`) is redundant after the model call just before it.

Vanilla:

- Athletics MightyBlow's HP half has no floor (above): a troop below 250 Athletics would lose HP. Troops only (heroes
  need 275 to take it). RESOLVED: the loader skips it for a troop with Athletics 250 or less.
- Athletics MightyBlow's stun half is backwards: `CalculateDefendedBlowStunMultipliers`
  (`decompiled/SandBox/SandBox.GameComponents/SandboxAgentApplyDamageModel.cs:551`) applies the +5% to
  `attackerStunPeriod`, the perk owner's own recoil, not the blocker's. Everyone.
- OneHanded Basher's +50% bash damage never applies (`SandboxAgentApplyDamageModel.cs:137`): it sits inside
  `if (currentUsageItem.IsMeleeWeapon)`, which shields never pass, and after the `RelevantSkill == OneHanded` branch
  that a shield (relevant skill One Handed) would take first. Only the stun half works. Everyone.
- Party-leader halves read `PartyBaseHelper.GetVisualPartyLeader`, which for a party without a hero leader (bandits,
  caravans, villagers, garrisons, militia) is the first troop in the roster
  (`decompiled/TaleWorlds.CampaignSystem/Helpers/PartyBaseHelper.cs:340-356`). If that troop is granted Bow
  DeepQuivers or Crossbow Fletcher, every other bow/crossbow user in the party gets the leader bonus
  (`SandboxAgentStatCalculateModel.cs:96`, `:104`). Troops only. Fletcher's party half is also asked with
  `isPrimaryEffect: true` (harmless).
- War Sails `NavalStrikeMagnitudeModel.CalculateAdjustedArmorForBlow`
  (`decompiled/NavalDLC/NavalDLC.GameComponents/NavalStrikeMagnitudeModel.cs:54-57`) recomputes the result from
  `baseArmor` and so drops the base model's bow/crossbow `ArmorPenetrationMultiplier` scaling at sea. Armor perks
  still apply once. Everyone at sea.
- War Sails: Mariner troops already get the full `NavalBattleCombatPenaltyNegation`
  (`NavalAgentStatCalculateModel.cs:274-283`), so RollingThunder and WindRider do nothing for them. Troops only.
- War Sails: the naval `CalculateRemainingMomentum` replaces the base result for every two-handed swing at sea, not
  only for MightyBlows holders. Everyone at sea.
- Swing-speed perks (TwoHanded OnTheEdge, WayOfTheGreatAxe speed) only speed up the animation: blow magnitude takes
  swing speed from the item in vanilla and in RBM's rewrite. Everyone.
- Description vs code (everyone): OneHanded ChinkInTheArmor covers one-handed weapons and shield bashes, not
  two-handed weapons or polearms; TwoHanded Vandal lowers armor for every weapon the owner uses; Polearm
  UnstoppableForce is x4 (not "triple") and works on foot too; Polearm SureFooted has no on-foot check; Polearm
  HardKnock also works on sweet-spot swings; Athletics Braced also covers the rider's horse; Riding HorseArcher
  applies to any missile; Bow NockingPoint applies to every ranged weapon and Discipline only to bows; Bow
  SkirmishPhaseMaster protects against all projectiles (and the horse); Throwing LastHit only boosts thrown
  missiles; Throwing Splinters is x4 (+300%); Throwing LongReach is just a longer mounted pickup reach; Crossbow
  Piercer zeroes armor below 20 and does nothing above (a cliff, not a flat -20); Athletics MorningExercise, Sprint,
  Medicine SelfMedication and Roguery FleetFooted raise general movement speed, not combat speed, with extra
  wielding conditions the text does not mention.
- TwoHanded ShowOfStrength only works with `CanKnockDown` weapons (crafting pieces `mace_head_24/34/37/38`); RBM's
  overrides of those pieces have no `<Flags>` node, so the vanilla flag should survive the XML merge. UNVERIFIED in
  game.
- `NavalAgentApplyDamageModel.ApplyDamageAmplifications:113` has a leftover no-op `agent.Name == "Itsul Ironeye"`.
