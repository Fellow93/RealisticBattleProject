# TwoHanded perks on regular troops (RBM, v1.5.4, audited 2026-10-06)

Every TwoHanded perk has a Personal primary half (Way of the Great Axe: both halves). "Two-handed weapon" below
means a TwoHanded-skill melee weapon: two-handed sword, axe or mace. Two-handed polearms use the Polearm skill
and never trigger these perks.

| Perk | Verdict | What a troop gets |
|---|---|---|
| StrongGrip | works | +10% handling with two-handed weapons. RBM posture also cuts the posture lost on blocks and parries. |
| WoodChopper | works | +30% damage to shields on blocked two-handed hits. |
| OnTheEdge | works | +3% swing animation speed with two-handed weapons. No damage gain: swing magnitude uses item speed in both vanilla and RBM. |
| HeadBasher | works | +10% damage with two-handed axes and maces. |
| ShowOfStrength | conditional | Swings ignore 30% of knockdown resistance, but only with CanKnockDown weapons. In practice that means heavy two-handed maces (mace_head_24/34/37/38). |
| BaptisedInBlood | hero-only | Nothing. Only read from the hero combat-hit event, through `HeroObject`. |
| BeastSlayer | works | +50% damage to mounts with two-handed weapons. Land only. |
| ShieldBreaker | works | +40% damage to shields on blocked two-handed hits. Stacks with WoodChopper. |
| Berserker | works | +20% two-handed damage while below 50% health. |
| Confidence | works | +15% two-handed damage while above 90% health. |
| ProjectileDeflection | hero-only | Nothing. The deflect flag is only set inside `if (agent.IsHero)`. |
| Terror | conditional | Two-handed kills and knock-outs cause +20% morale loss to enemies. Lost when killing siege defenders under RBMAI. |
| Hope | conditional | Two-handed kills and knock-outs give allies +30% morale gain. Lost when killing siege defenders under RBMAI. |
| RecklessCharge | conditional | On foot, +20% to the forward-speed term of two-handed swings and thrusts. Only applies while moving into the blow. RBM re-implements this in `ApplyMeleeSpeedPerks`. |
| ThickHides | works | +5 max HP in battle, on land and at sea. |
| BladeMaster | works | +10% damage with two-handed weapons. |
| Vandal | works | -25% effective victim armor on all of the troop's attacks, with any weapon. |
| WayOfTheGreatAxe | conditional | Applies only above 250 raw TwoHanded skill. No shipped troop gets there (vanilla max 220, RBM unit overhaul max 200). |

Totals: works 11, conditional 5, hero-only 2.

## Notes

- **BaptisedInBlood is missing from `TroopPerks.HeroOnlyPerkIds`** (`RBMConfig/Shared/TroopPerks.cs:38`). Its only
  personal read is `attacker.HeroObject.GetPerkValue` in `BattleCampaignBehavior.OnHeroCombatHit`.
  `MapEventParty.OnTroopScoreHit` raises that event only for heroes. So the loader accepts it for a troop
  without warning, and it does nothing. Consider adding `TwoHandedBaptisedInBlood` to that list.
  (Done after the audit: the list is now `TroopPerks.NoTroopEffectPerkIds` and includes it.)
- **Vandal's scope is wider than its tree suggests.** `CalculateAdjustedArmorForBlow` applies it before any weapon
  check, so it boosts bows, crossbows, throwing, 1H and unarmed attacks too (TroopUsageFlags.Any). RBM keeps it
  through `armorPenetrationFactor`, which the rebuilt face and under-shoulder armor also use.
- **WayOfTheGreatAxe is dead weight on every shipped troop.** The epic threshold is
  `MaxSkillRequiredForEpicPerkBonus` = 250, checked against raw `GetSkillValue`.
- **ShowOfStrength's reach depends on item flags.** In vanilla, only four crafting pieces carry `CanKnockDown`
  (`mace_head_24/34/37/38`), all of them TwoHandedMace heads (`mace_head_24` is in the 1H Mace description too, but
  a 1H mace is OneHanded skill). RBM's overrides of those pieces have no `<Flags>` node.
  `MBObjectManager.MergeElements` only drops child nodes when `_replaceWhileMerging` is set, so the vanilla flag
  should survive. This is not verified in game.
- **Hope/Terror code matches the descriptions,** even though the variable names suggest otherwise. In
  `CalculateMaxMoraleChangeDueToAgentIncapacitatedExplained`, `bonuses` is returned as the affector-side gain and
  `bonuses2` as the affected-side loss. Hope therefore boosts friendly gain and Terror boosts enemy loss.
  RBMAI's siege prefix (`SiegePatches.cs:427-471`) throws away the whole morale shock when a defender goes down.
- **OnTheEdge / WayOfTheGreatAxe speed only changes the animation.** `CalculateBaseMeleeBlowMagnitude`, both
  vanilla and RBM's rewrite, takes swing speed from the item (`GetModifiedSwingSpeedForCurrentUsage`), not from
  `SwingSpeedMultiplier`.
- ThickHides only shows inside a mission: the troop's `MaxHitPoints()` is evaluated in the `Agent.Character`
  setter while `Mission.Current` is set. The party screen still shows 100.
- StrongGrip's RBM posture factor is cached per Stance. It is not recomputed if the troop-perk map changes
  mid-mission.
