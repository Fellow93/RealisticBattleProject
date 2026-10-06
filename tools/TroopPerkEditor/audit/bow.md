# Bow perks on regular troops (RBM, v1.5.4) — audited 2026-10-06

| Perk | Verdict | What a troop gets |
|---|---|---|
| Bow Control | works | -30% accuracy penalty while moving, bow wielded |
| Dead Aim | conditional | +30% damage on bow headshots |
| Bodkin | works | target armor counted 10% lower for the troop's arrows |
| Ranger's Swiftness | works | wielded bow no longer adds its extra length/weight encumbrance (faster movement) |
| Rapid Fire | works | +25% bow reload speed (RBM re-applies it on its own reload curve) |
| Quick Adjustments | partial | -50% rotation accuracy penalty on foot; **lost on horseback** (RBMAI fixed value) |
| Merry Men | no-personal-effect | party size / governor |
| Mounted Archery | conditional | -30% movement and unsteady accuracy penalties, mounted with a bow, land only |
| Trainer | no-personal-effect | party XP |
| Strong Bows | works | +8% arrow damage |
| Discipline | works | +50% time holding a draw before aim shakes (bow only) |
| Hunter Clan | conditional | +30% arrow damage vs horses, land only |
| Skirmish Phase Master | works | -10% damage taken from any projectile, also for the troop's horse |
| Eagle Eye | hero-only | camera zoom, read only for the player's agent |
| Bulls Eye | no-personal-effect | party / garrison XP |
| Renowned Archer | no-personal-effect | party morale / recruit cost |
| Horse Master | hero-only | all-bows-mounted flag set only in the `agent.IsHero` branch |
| Deep Quivers | works | +3 arrows per arrow stack at spawn |
| Quick Draw | works | +25% bow draw speed |
| Nocking Point | works | -50% movement penalty while reloading, any ranged weapon |
| Deadshot | conditional | per Bow skill point above 200: +0.2% reload, +0.5% bow damage |

Counts: works 10, conditional 4, partial 1, hero-only 2, no-personal-effect 4.

## Notes

- **Quick Adjustments is lost for horse archers.** `RealisticBattleAiModule/AiModule/Agents/AgentStats.cs:207` assigns
  `WeaponRotationalAccuracyPenaltyInRadians = 0.015f` for any mounted bow user after the perk has been applied
  (bypass map #9b). Only when RBM AI is on. The other mounted-bow stat edits there use `*=`, so Bow Control and
  Mounted Archery keep their effect.
- **Eagle Eye** does read the troop's own CharacterObject (`GetMaxCameraZoom`), but the only caller is
  `Mission.cs:2299` for `MainAgent`. A troop never gets anything from it, so it is in effect hero-only.
- **Deep Quivers secondary leaks to leaderless parties.** `InitializeMissionEquipment` (`SandboxAgentStatCalculateModel.cs:96`)
  checks the party-leader half through `PartyBaseHelper.GetVisualPartyLeader(party).GetPerkValue(...)`. In a party with
  no leader hero (bandits, garrisons, militia, villagers) that is the first troop in the member roster. If that troop is
  granted Deep Quivers, every other bow user in the party gets +1 arrow per stack. This is a side effect of the
  troop-perk grant, not a bug. Crossbow.Fletcher has the same pattern (and vanilla checks it with `isPrimaryEffect: true`
  for the secondary bonus, unlike Deep Quivers).
- **Description/code mismatches:** Nocking Point is flagged and described as a bow perk, but its personal call sits outside
  the bow branch, so it applies with crossbows and throwing weapons too. Discipline is flagged for all ranged troops, but
  it only applies with bows. Ranger's Swiftness ("equipped bows do not slow you down") only removes the extra encumbrance
  of the **wielded** bow; the bow's plain weight still counts like any carried weapon. Skirmish Phase Master is a bow perk
  but protects against every projectile, and also protects the rider's horse (`ApplyDamageReductions` uses the rider's
  character when a mount is hit).
- **Deadshot** uses the troop's raw `GetSkillValue(Bow)` against `MinSkillRequiredForEpicPerkBonus` = 200. Few regular
  troops have Bow above 200, so for most troops it does nothing.
- Reload perks (Rapid Fire, Deadshot primary) survive RBM's `ReloadSpeed` overwrite through
  `RangedRework.Reload.cs:37-78` `GetReloadPerkFactor`. All damage and armor perks (Dead Aim, Bodkin, Strong Bows,
  Hunter Clan, Deadshot secondary, Skirmish Phase Master) go through unpatched vanilla model methods that RBM still calls.
