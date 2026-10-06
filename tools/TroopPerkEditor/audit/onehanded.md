# OneHanded perks on regular troops (audited 2026-10-06, v1.5.4)

| Perk | Verdict | What a troop gets |
|---|---|---|
| Wrapped Handles | works | +20% handling with a 1H melee weapon; with RBM posture on, ~17% less posture lost on blocks/parries |
| Basher | partial | Shield-bash stun +50% (shield wielded). The +50% bash damage is dead code in vanilla too |
| To Be Blunt | works | +5% damage with 1H axes and maces |
| Swift Strike | works | +2% swing speed with 1H melee weapons |
| Cavalry | works | +5% 1H melee damage while mounted (land only) |
| Shield Bearer | works | Wielded shield no longer adds 1.5x its weight to weapon encumbrance (small speed/acceleration gain) |
| Trainer | works | +2 HP |
| Duelist | partial | +20% 1H melee damage with an empty off hand; tournament-renown half is hero-only |
| Shieldwall | works | -20% shield damage on wrong-side blocks; RBM posture: -20% posture cost on those blocks |
| Arrow Catcher | rbm-bypassed | Nothing: RBM fixes the shield missile catch size at 0.01 for everyone |
| Military Tradition | no-personal-effect | Party leader / governor only |
| Corps-a-corps | no-personal-effect | Party leader / governor only |
| Stand United | no-personal-effect | Party leader / governor only |
| Lead by Example | no-personal-effect | Party leader only |
| Steel Core Shields | works | -10% shield damage; RBM posture: -10% posture cost on shield blocks |
| Fleet of Foot | works | +4% combat movement speed |
| Deadly Purpose | works | +5% 1H melee damage |
| Unwavering Defense | works | +5 HP |
| Prestige | works | +50% damage to shields with 1H melee weapons |
| Chink in the Armor | works | Ignores 10% of armor with 1H weapons and shield bashes (not 2H or polearms) |
| Way of the Sword | conditional | Needs One Handed above 250; no vanilla troop has more than 240, so normally nothing |

Verdicts: works 13, partial 2, conditional 1, rbm-bypassed 1, no-personal-effect 4.

"Halved at sea": every personal half here is `NavalReduced` (bonus x0.5 in naval battles) except Cavalry (`LandOnly`: nothing at sea) and the two HP perks (`BattleEnvironment.Any`, always full).

## Notes

- **Basher's damage bonus never fires, in vanilla either.** `SandboxAgentApplyDamageModel.ApplyDamageAmplifications:137` checks
  `currentUsageItem.IsShield` inside `if (currentUsageItem.IsMeleeWeapon)`. During a bash the attacker weapon is the shield
  (see `CalculateAlternativeAttackDamage`, which branches on Small/LargeShield), and shields never carry the `MeleeWeapon`
  flag (vanilla `shields.xml` and `RBMCombat_shields.xml`; flags come only from XML). Only the stun-duration half
  (`ShieldBashStunDurationMultiplier`) works.
- **Chink in the Armor does not match its description.** The description says "melee attacks", but the code only checks
  `weaponComponent.RelevantSkill == OneHanded`, which covers one-handed weapons plus shields (WeaponComponentData maps
  Small/LargeShield to OneHanded). So it also helps shield bashes, and it does nothing for two-handed weapons or polearms.
- **Arrow Catcher** works in vanilla but RBM's `DamageRework.SandboxAgentUpdateHumanStats` postfix (HitReaction.cs:26)
  overwrites `AttributeShieldMissileCollisionBodySizeAdder` after the stat build (bypass map #10). This is by design.
- **The HP perks (Trainer, Unwavering Defense)** reach troops through `CharacterObject.MaxHitPoints()`. `Agent.Character`'s
  setter reads it into `BaseHealthLimit` at spawn, while `Mission.Current` is set, and `InitializeAgentStats`'
  non-hero branch builds HealthLimit from it.
- **RBM adds a posture effect** for Wrapped Handles, Steel Core Shields and Shieldwall (`MeleeBlowPatch.Math.cs:376/406/412`).
  It reads the troop's own CharacterObject, so troop perks feed it. It needs rbmAiEnabled + postureEnabled. The factors are
  cached per Stance.
- **Way of the Sword** uses the raw `GetSkillValue` against 250. The highest One Handed among vanilla and RBM troop definitions
  is 240 (270/280/300 appear only on lords and hero skill sets).
- War Sails `NavalStrikeMagnitudeModel.CalculateAdjustedArmorForBlow` calls the Sandbox model, then rebuilds the armor
  reduction from `baseArmor`, so Chink in the Armor is not doubled. The base model's bow/crossbow
  `ArmorPenetrationMultiplier` step is lost in the process (a vanilla quirk outside this group).
- Every captain half (Wrapped Handles, Basher, Cavalry, Shield Bearer, Shieldwall, Arrow Catcher, Steel Core Shields,
  Fleet of Foot, Deadly Purpose) needs a hero captain, so it never comes from a troop's own perk.
