# Throwing perks on regular troops (RBM, v1.5.4) - audited 2026-10-06

| Perk | Verdict | What a troop gets |
|---|---|---|
| Quick Draw | works (medium) | +20% ReloadSpeed with Throwing-skill weapons (incl. slings). RBM's reload rewrite skips throwing usages. |
| Shield Breaker | conditional | +40% damage to shields (raised or on back) with thrown missiles. |
| Hunter | conditional | +40% thrown-missile damage to horses. Land only. |
| Flexible Fighter | conditional | +10% melee damage when fighting with a Thrown-type item (javelin/axe/knife melee mode). |
| Mounted Skirmisher | conditional | Mounted with a throwing weapon: -20% movement and unsteady accuracy penalty (RBMAI's later `*=` keep it). |
| Well Prepared | works | +1 on each throwing ammo stack at spawn (0 at sea after rounding, if alone). |
| Running Throw | hero-only | Nothing. Vanilla and RBM's re-implementation both gate on `IsHero`. |
| Knock Off | conditional | Thrown missiles can dismount riders, +25% dismount penetration. Missiles stay on the vanilla dismount path. |
| Skirmisher | conditional | -10% ranged damage taken while the main-hand usage is Throwing-skill. |
| Saddlebags | conditional | +2 on each throwing ammo stack when spawned mounted. |
| Focus | hero-only | Nothing: camera zoom only exists for the player's agent. |
| Last Hit | conditional | +50% thrown-missile damage to targets at or below half health. |
| Head Hunter | conditional | +50% thrown-missile headshot damage. |
| Slinging Competitions | conditional | Sling stones ignore head armor (armor 0, carried into RBM's face-hit armor). |
| Resourceful | works | +2 on each throwing ammo stack at spawn. |
| Splinters | conditional | Thrown axes do +300% (about x4) damage to shields. |
| Perfect Technique | partial (medium) | +25% launch speed for javelins/axes/knives/stones (RBMAI re-applies it); lost for slings. |
| Long Reach | conditional (medium) | Mounted: interaction reach 3 m instead of 1.5 m (AI item pickup reach x3 = 9 m). |
| Weak Spot | works | -30% effective target armor on Throwing-skill hits (thrown weapons and sling stones). |
| Impale | conditional | Javelins penetrate wooden shields (raised or on back). Metal shields still stop them (RBM by design). |
| Unstoppable Force | conditional (medium) | Epic: needs the troop's own Throwing above 200; +0.2% speed / +0.5% damage per point. Speed half lost for slings. |

Counts: works 4, conditional 14, partial 1, hero-only 2.

## Notes

- **Slings lose all missile-speed perks under RBM.** `RangedRework.OverrideOnAgentShootMissile` replaces sling launch
  velocity with `MissileBallistics.GetSlingSpeed`, which never reads `AgentDrivenProperties.MissileSpeedMultiplier`
  (`RangedRework.MissileSpeed.cs:191`, `RBMConfig/Shared/MissileBallistics.cs:329`). So Perfect Technique and the speed
  half of Unstoppable Force, which RBMAI carefully re-applies (`AgentStats.cs:36-59`), do nothing for slings. This
  applies to heroes and the player too. For javelins/axes/knives/stones the multiplier is assumed to be folded in
  natively (UNVERIFIED, bypass map #19).
- **Last Hit description vs code:** the text says damage against wounded enemies in general (TroopUsageFlags.Any). The
  code only applies it inside the throwing-consumable branch of `ApplyDamageAmplifications`
  (`SandboxAgentApplyDamageModel.cs:236-239`), so only thrown missiles benefit.
- **Splinters description vs code:** "Triple damage" is an `AddFactor(3f)`, i.e. +300% (x4), and it adds to other
  factors (with Shield Breaker it is +340%).
- **Long Reach description vs code:** "can pick up items while mounted" is implemented as a longer interaction
  distance when mounted (3 m vs 1.5 m, `SandboxAgentStatCalculateModel.cs:398`). It does not unlock anything.
- **Impale and RBM's pilum rule:** a javelin with Impale skips RBM's new pilum-through-shield partial wound
  (`RangedRework.ShieldPenetration.cs:110-116`, not yet in the bypass map) and goes down the engine's full
  CanPenetrateShield path instead. On metal shields both are cancelled. The `HeroObject.GetPerkValue` fallback in
  `PenetratesShieldOnBack` (`RangedRework.Collision.cs:303`) is redundant because the model call just before it already
  asks the same character.
- **At sea:** Hunter, Mounted Skirmisher, Knock Off and Saddlebags are LandOnly (no effect in naval battles). Most others
  are NavalReduced (half effect).
- Epic Unstoppable Force uses the troop's raw `GetSkillValue(Throwing)`. Few vanilla troops have more than 200 Throwing, so
  for most troops it does nothing even when granted.
