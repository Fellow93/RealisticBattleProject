# Troop perk audit: non-combat skills + War Sails

Group `noncombat-naval`: Smithing (`Crafting`), Scouting, Tactics, Roguery, Charm, Leadership, Trade, Steward,
Medicine, Engineering (DefaultPerks) and all NavalPerks (Mariner, Boatswain, Shipmaster). Audited 2026-10-06
against v1.5.4 `decompiled/` and the RBM working tree (`patreon-1.5.x`). Full data in `noncombat-naval.json`.

Verdicts (272 perks): works 4, conditional 22, partial 3, hero-only 6, campaign-only 62, no-personal-effect 175.

## Perks that do something for a troop

| Perk | Verdict | What a troop gets |
|---|---|---|
| MedicineDoctorsOath | works | +5 max HP (secondary half). |
| MedicineFortitudeTonic | works | +5 max HP (secondary half). |
| MedicinePreventiveMedicine | partial | +5 max HP works; post-battle HP recovery half is hero-only. |
| EngineeringScaffolds | works | +30% shield HP (secondary half); also cuts RBM posture loss on shield blocks. Half at sea. |
| EngineeringTorsionEngines | works | +3 flat damage on crossbow bolt body hits (secondary half). +1.5 at sea. |
| MedicineSelfMedication | partial | +2% MaxSpeedMultiplier, only without a shield in the off hand; healing half hero-only. |
| RogueryFleetFooted | partial | +10% speed only while holding nothing in either hand; escape half hero-only. |
| RogueryCarver | conditional | +10% damage with civilian-flagged weapons. |
| RogueryDashAndSlash | conditional | On foot, +50% to the extra linear speed of swings/thrusts (RBM re-implements it). |
| RogueryDirtyFighting | conditional | +50% kick stun; AI troops kick only with RBM `aiKickBashEnabled`. |
| LeadershipMakeADifference | conditional | Doubles the morale its side gains when the troop kills; not for siege defenders under RBMAI. |
| CraftingSharpenedEdge / SharpenedTip | conditional | Player-crafted weapons only; troop gear never is, so in practice nothing. |
| PiratesProwess | conditional | At sea, +25% melee handling. |
| BruteForce | conditional | At sea, +50% kick/bash damage; AI troops need RBM `aiKickBashEnabled`. |
| AxeOfTheNorthwind / SunnyDisposition | conditional | At sea, +20% melee damage with axes / swords. |
| WarriorsMight / TheCorsairsEdge | conditional | At sea, +20% melee / +10% one-handed melee damage. |
| TheSkysFury | conditional | At sea, +15% bow/crossbow/thrown damage. |
| BoardingMaster / HomeTurfAdvantage | conditional | At sea, +15% melee on an enemy ship / +20% on own ship. |
| ShatteringBlow / ShatteringVolley | conditional | At sea, -50% victim armor for melee / missiles (kept by RBM's damage rewrite). |
| CrewOfSpears | conditional | At sea, javelins pierce shields, throwing axes pass on after breaking one; RBM stops javelins on metal shields. |
| MightyBlows | conditional | At sea, 2H swings keep more momentum; under RBM only matters after a sliced-through kill. |
| RollingThunder | conditional | At sea, -30% ranged roll penalties, non-mariner troops only. |
| WindRider | conditional | At sea, deck speed penalty halved, non-mariner troops only. |
| TheHelmsmansShield | conditional | At sea, -50% missile damage while steering the ship. |

## Personal role, but nothing for a troop

| Perk(s) | Verdict | Why |
|---|---|---|
| VigorousSmith, StrongSmith, EnduringSmith, WeaponMasterSmith | hero-only | One-off +1 attribute/focus written to the hero's HeroDeveloper when learned. |
| MedicineMinisterOfHealth | hero-only | The battle check reads the party leader hero's perk, never the troop's. |
| ShipwrightsInsight | hero-only | `attackerAgent.IsHero` gate (already in `HeroOnlyPerkIds`). |
| 13 smithing perks (refining, research, stamina, quality) | campaign-only | Smithing screen, smith hero. |
| All personal Charm and Trade perks, TacticsBesieged, Roguery TwoFaced/DeepPockets/Manhunter/Scarface/WhiteLies/SmugglerConnections/RogueExtraordinaire, Leadership Presence/FamousCommander/WePledgeOurSwords, StewardSweatshops, Medicine WalkItOff/BestMedicine/GoodLodging/CheatDeath, Arr, Resilience | campaign-only | Campaign-map, barter, persuasion, relation, post-battle healing; all read a Hero. |
| Everything else (175) | no-personal-effect | Only party-leader / captain / scout / quartermaster / surgeon / engineer / governor / clan-leader / first-mate / navigator roles. |

## Notes

- **Mariner troops get nothing from RollingThunder or WindRider.** `NavalAgentStatCalculateModel.UpdateNavalHumanStats`
  (`:274`) gives `IsMariner` troops the full `NavalBattleCombatPenaltyNegation` (factor -1), which already takes every
  sea penalty to 0 (`LimitMin(0)`). Give these two perks only to land troops that fight at sea.
- **MinisterOfHealth is mislabelled.** Its primary role is Personal, but the only mission check
  (`SandboxAgentStatCalculateModel.GetEffectiveMaxHealth:561`) reads the party leader hero. Listing it for a troop does
  nothing. A candidate for `TroopPerks.HeroOnlyPerkIds`, along with the four smith attribute perks and the post-battle
  healing perks (Resilience, WalkItOff). (Done after the audit: all are in `TroopPerks.NoTroopEffectPerkIds`.)
- **HP perks work, with no RBM patch involved.** Agent spawn sets `BaseHealthLimit = MaxHitPoints()`
  (`Agent.cs:1438`) inside the mission, so the troop-perk postfix answers `DefaultCharacterStatsModel.MaxHitpoints`.
  `GetEffectiveMaxHealth` then builds on `BaseHealthLimit`. Auto-resolve HP does not see troop perks (no mission).
- **Kick perks depend on RBM AI.** The native AI never kicks, so DirtyFighting and BruteForce do nothing for AI troops
  unless `rbmAiEnabled` and `aiKickBashEnabled` are on. The DirtyFighting stun is the engine-read
  `KickStunDurationMultiplier`, which the managed code cannot confirm (medium confidence).
- **Siege morale (bypass #17):** RBMAI skips the morale shock when a siege defender goes down, so
  LeadershipMakeADifference does nothing for kills of siege defenders.
- **Descriptions that don't match the code:**
  - SelfMedication and FleetFooted say "combat movement speed" but write `MaxSpeedMultiplier`, not
    `CombatMaxSpeedMultiplier`. SelfMedication also skips agents with a shield in the off hand.
  - DashAndSlash says "damage bonus from speed", but it scales the extra linear speed fed into the magnitude.
  - TorsionEngines' "+3 damage to equipped crossbows" is a flat +3 added after armor on bolt body hits.
- **Vanilla oddities seen in passing:**
  - The Naval `CalculateRemainingMomentum` replaces the base result for every two-handed swing at sea, not only
    for MightyBlows holders.
  - `NavalAgentApplyDamageModel.ApplyDamageAmplifications:113` has a leftover no-op `agent.Name == "Itsul Ironeye"`.
