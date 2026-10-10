# Troop perk audit: Riding and Athletics (2026-10-06)

Data: `riding-athletics.json`. Rule: a perk works for a regular troop only when a mission-time check goes through
`CharacterObject.GetPerkValue` on the troop's own character and RBM still runs that check.

| Perk | Verdict | What a troop gets |
|---|---|---|
| RidingFullSpeed | works | +20% horse charge damage (mounted, land) |
| RidingNimbleStead (Nimble Steed) | works | +10% horse maneuvering |
| RidingWellStraped | hero-only | nothing (main hero's horse only) |
| RidingVeterinary | works | +20% HP for the troop's horse (at spawn) |
| RidingNomadicTraditions | no-personal-effect | nothing |
| RidingDeeperSacks | no-personal-effect | nothing |
| RidingSagittarius | works | -15% mounted movement/unsteady accuracy penalty with a ranged weapon in hand |
| RidingSweepingWind | works | +5% horse top speed (RBM re-applies it; partly compressed by RBM's weight lerp on fast horses) |
| RidingReliefForce | no-personal-effect | nothing |
| RidingMountedWarrior | works | +5% mounted melee damage |
| RidingHorseArcher | works | +10% mounted ranged damage (any missile, not only bows) |
| RidingShepherd | no-personal-effect | nothing |
| RidingBreeder | no-personal-effect | nothing |
| RidingThunderousCharge | partial | +20% enemy morale loss on mounted melee kills; lost when the victim is a siege defender (RBMAI) |
| RidingAnnoyingBuzz | partial | same for mounted ranged kills |
| RidingMountedPatrols | no-personal-effect | nothing |
| RidingCavalryTactics | no-personal-effect | nothing |
| RidingDauntlessSteed | works | +50% stagger threshold while mounted (vanilla stagger only) |
| RidingToughSteed | works | +20% horse armor (barded horses only, since it multiplies) |
| RidingTheWayOfTheSaddle | conditional | only with effective Riding > 250 |
| AthleticsMorningExercise | conditional | +3% move speed, only with no shield in the off hand |
| AthleticsWellBuilt | works | +5 max HP |
| AthleticsFury | works | +10% handling on foot with a melee weapon, plus less posture lost on blocks (RBM posture) |
| AthleticsFormFittingArmor | works | armor 15% lighter for vanilla encumbrance (speed) and RBM stamina, posture, kick/bash knockdown and horse load |
| AthleticsImposingStature | campaign-only | nothing in battle (persuasion) |
| AthleticsStamina | campaign-only | nothing in battle (crafting stamina) |
| AthleticsSprint | conditional | +5% move speed with no shield and no ranged weapon wielded |
| AthleticsPowerful | works | +4% melee damage |
| AthleticsSurgingBlow | works | +30% of the speed bonus to melee blows on foot (RBM re-implements it) |
| AthleticsBraced | works | -40% horse-charge damage taken |
| AthleticsWalkItOff | no-personal-effect | nothing |
| AthleticsAGoodDaysRest | no-personal-effect | nothing |
| AthleticsDurable | hero-only | nothing (+1 Endurance is a hero attribute grant) |
| AthleticsEnergetic | no-personal-effect | nothing |
| AthleticsSteady | hero-only | nothing (+1 Control, hero attribute grant) |
| AthleticsStrong | hero-only | nothing (+1 Vigor, hero attribute grant) |
| AthleticsStrongLegs | conditional | -50% fall damage; double kick damage, but troops only kick with RBM's AI kick/bash option |
| AthleticsStrongArms | works | +5% throwing damage (slings included) |
| AthleticsSpartan | works | +50% stagger threshold on foot (vanilla stagger only) |
| AthleticsIgnorePain | works | +10% armor on foot (RBM carries it over as a ratio) |
| AthleticsMightyBlow | conditional | **harmful below 250 Athletics** (see note 1) |

Counts: works 18, conditional 5, partial 2, hero-only 4, campaign-only 2, no-personal-effect 10.

## Notes

1. **Mighty Blow is a trap for troops.** `DefaultCharacterStatsModel.MaxHitpoints` (`:41`) adds
   `GetSkillValue(Athletics) - 250` HP whenever the perk is present, with no `> 0` guard and no floor on the
   ExplainedNumber. Heroes can only unlock it at 275 Athletics, so it is always positive for them. A troop with
   Athletics 100 gets -150 HP and may spawn with zero or negative health. Do not grant it to troops below 250 Athletics.
   The editor (or `TroopPerks.Load`) should probably warn about it.
2. **Mighty Blow's stun half is backwards in vanilla.** The description says the enemy is stunned longer after they
   block. `CalculateDefendedBlowStunMultipliers` (`SandboxAgentApplyDamageModel.cs:551`) applies the +5% to
   `attackerStunPeriod`, which is the perk owner's own recoil. That makes it a small self-penalty for heroes and for troops.
3. **Morning Exercise / Sprint description mismatches.** Both raise `MaxSpeedMultiplier` (general movement), not the
   combat speed the descriptions mention. Morning Exercise sits in the "no shield in off hand" branch, which the
   description does not mention. Sprint checks the wielded weapon, not equipped/carried items.
4. **Horse Archer** applies to any missile while mounted (crossbows, throwing, slings), despite its BowUser usage flag.
   **Braced** has no on-foot check in code, so the rider's perk also covers its horse when the horse is charged.
5. **Thunderous Charge / Annoying Buzz** lose their effect on kills of siege defenders (bypass map #17, RBMAI skips the
   morale shock). Horse-charge kills never count, because the killing blow's weapon class is not melee/ranged.
6. **Form Fitting Armor** works in vanilla's encumbrance. RBM's own armor-weight terms used raw
   `GetTotalWeightOfArmor`: stamina cost (`MeleeBlowPatch.cs:248`), max posture (`Stance.cs:140`), kick/bash
   knockdown resistance (`AiKickBash.cs:675`), horse speed and charge weight (`HorseChanges.MountStats.cs:58`, `:109`).
   FIXED 2026-10-09: they now read the model's `GetEffectiveArmorEncumbrance`, so the perk counts in all of them.
7. **Strong Legs kicks.** The ×2 kick multiplier (one-argument check, no environment filter) applies to RBM's
   punch-model kick damage. Since posture loss follows damage, it also doubles kick posture loss and the
   posture-break knockdown chance.
8. **The Way Of The Saddle** uses effective Riding (troop skill + captain Nimble Steed +30), but regular troops
   practically never pass 250.
9. Stagger perks (Dauntless Steed, Spartan) only change vanilla's shrug-off threshold. RBM posture-break
   staggers and knockdowns ignore them.
