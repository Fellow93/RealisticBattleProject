# Polearm perks on regular troops (RBM, v1.5.4) — audited 2026-10-06

| Perk (StringId) | Verdict | What a troop actually gets |
|---|---|---|
| PolearmPikeman | conditional | +2% melee polearm damage while on foot |
| PolearmCavalry | conditional | +2% melee polearm damage while mounted |
| PolearmBraced | conditional | +0.25 dismount penetration on polearm hits that can dismount; moot for RBM's forced couch/brace dismounts |
| PolearmKeepAtBay | conditional | +0.3 knockback penetration on active thrusts with wide-grip polearms vs footmen |
| PolearmSwiftSwing | works | +5% swing speed with a polearm |
| PolearmCleanThrust | conditional | +10% polearm thrust damage |
| PolearmFootwork | works | +2% combat movement speed with a polearm |
| PolearmHardKnock | conditional | +0.25 knockdown penetration with CanKnockDown polearms (thrust, or sweet-spot swing); also feeds the dismount fallback |
| PolearmSteadKiller | conditional | +70% melee polearm damage to horses |
| PolearmLancer | conditional | +20% speed bonus on mounted polearm strikes (RBM re-implements swing/thrust; couch is vanilla) |
| PolearmSkewer | conditional | Couched-lance kills pass through 30% instead of 5% (RBM re-rolls with the perk) |
| PolearmGuards | conditional | +50% melee polearm damage on head hits |
| PolearmStandardBearer | no-personal-effect | — (captain / governor) |
| PolearmPhalanx | no-personal-effect | — (party leader / captain) |
| PolearmHardyFrontline | no-personal-effect | — (party leader) |
| PolearmDrills | no-personal-effect | — (governor / party leader) |
| PolearmSureFooted | conditional | -40% damage taken from horse charges |
| PolearmUnstoppableForce | conditional | x4 couched/braced magnitude against shields, compressed by RBM's lance caps (x1.9 at full gallop) |
| PolearmCounterweight | works | +15% handling with swingable polearms, plus ~13% less posture lost when blocking (RBM posture) |
| PolearmSharpenTheTip | conditional | +5% polearm thrust damage |
| PolearmWayOfTheSpear | conditional | Only if the troop's own Polearm skill is above 250: +0.2% speed and +0.5% damage per point (260-skill troops: +2% / +5%) |

Counts: works 3, conditional 14, no-personal-effect 4. No polearm perk is hero-only or removed by RBM.

## Notes

- **StringId typo:** Steed Killer is `PolearmSteadKiller` in `DefaultPerks.cs:1619` (property `Polearm.SteedKiller`). The XML must use the misspelled id.
- **Unstoppable Force:** the description says "triple", but the code uses `AddFactor 3` = **x4**. It is not gated on the attacker being mounted: a braced spear on foot hitting a rider's shield also gets it. Under RBM, the shield damage goes through `CalculateCouchedLanceMagnitude` (`DamageRework.Core.cs:51-84`). That clamp snaps the couched value to 230, or to the ballistic value, and caps it at 430. So the gain is x4 only for slow hits and about x1.87 for fast charges. This is estimated from the code and not tested in game.
- **Hard Knock:** the description says thrusts, but `CanWeaponKnockDown` also lets a polearm with CanKnockDown knock down on a sweet-spot swing, and the perk applies to that swing too. It also raises the knockdown fallback inside `DecideAgentDismountedByBlow`, so it helps unhorse riders as well.
- **Keep at Bay / Hard Knock / Braced vs RBM passive attacks:** RBM's `CreateMeleeBlow` postfix (`DamageRework.Blows.cs:33-61`) always knocks down on a couched or braced hit that strikes an agent, and always knocks back on one that is blocked. Strong couch/brace thrusts to head or chest always dismount (`HorseChanges.MountedCombat.cs:74-87`). These perks only matter for active thrusts and for the weaker passive hits that fall through to vanilla.
- **Sure Footed:** the code does not check that the victim is on foot, even though the troop-usage flag is OnFoot. A rider hit by a charge also gets the reduction.
- **Way of the Spear:** it reads the raw `GetSkillValue`, not the effective skill, so captain skill perks do not lift a troop over 250. Only a couple of vanilla characters have Polearm 260.
- **Damage perks** (Pikeman, Cavalry, CleanThrust, SharpenTheTip, SteedKiller, Guards, WayOfTheSpear secondary) also scale the damage a hit does to a shield when it is blocked, because `CalculateDamage` runs on the shield damage as well.
