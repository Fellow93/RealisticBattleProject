# Crossbow perks on regular troops (audited 2026-10-06, v1.5.4)

| Perk | Verdict | What a troop gets |
|---|---|---|
| Piercer | conditional | Bolts treat armor below 20 (RBM per-part value) as 0; armor of 20+ is not reduced at all |
| Marksmen | works | +25% ranged ready (aim) speed with a wielded crossbow |
| Unhorser | conditional | +40% bolt damage against horses |
| Wind Winder | works | +25% crossbow reload speed (RBM re-applies it after its reload overwrite) |
| Donkey's Swiftness | works | -30% accuracy loss while moving with a crossbow |
| Sheriff | conditional | +50% damage on crossbow headshots |
| Peasant Leader | no-personal-effect | none (party leader / governor) |
| Renowned Marksmen | no-personal-effect | none (party leader / governor) |
| Fletcher | works | +4 bolts per bolt stack at spawn |
| Puncture | works | Bolts treat armor as 10% lower |
| Loose and Move | works | A wielded crossbow adds no wielded-weapon encumbrance (no speed loss); slung crossbows still count |
| Deft Hands | conditional | +50% stagger threshold while reloading a crossbow |
| Counter Fire | conditional | -10% missile damage taken while a crossbow is in the main hand |
| Mounted Crossbowman | hero-only | none: the agent flag is set only in the `agent.IsHero` branch |
| Steady | partial | Mounted: movement-accuracy half works; turning-accuracy half overwritten by RBM AI |
| Long Shots | no-check-found | none for AI: zoom only affects the player's own camera (`Mission.MainAgent`) |
| Hammer Bolts | conditional | Bolts can dismount riders, +0.5 dismount penetration |
| Pavise | conditional | Only a 75% chance to ignore missiles that go through a non-metal shield on the back (RBM already blocks every other back-shield hit for everyone) |
| Terror | no-personal-effect | none (party leader / captain) |
| Picked Shots (`CrossbowBoltenGuard`) | no-personal-effect | none (party leader) |
| Mighty Pull | conditional | +0.2% reload and +0.5% damage per point of the troop's own Crossbow skill above 200 (both halves Personal) |

## Notes

- **Fletcher leaks to leaderless parties (vanilla behaviour).** The party-leader half in
  `SandboxAgentStatCalculateModel.InitializeMissionEquipment:104` uses `PartyBaseHelper.GetVisualPartyLeader`. When a party has
  no hero leader (bandits, caravans, villagers, garrisons/militia), that returns the **first troop in the roster**. If that troop
  has Fletcher listed, every other bolt user in the party gets +2 bolts (troops of the leader's own type are excluded by the
  `== characterObject` check and get their personal +4 instead). The vanilla check also asks with `isPrimaryEffect: true`
  for the secondary half (harmless, both halves are LandOnly).
- **Steady loses half to RBM AI.** `AgentStats.cs:225` sets `WeaponRotationalAccuracyPenaltyInRadians = 0.010f` for every mounted
  crossbow user, so the perk's rotational half (bypass #9b) is lost. The movement half survives (RBM only multiplies it).
- **Pavise is almost redundant under RBM.** `RangedRework.Collision.cs:243-269` turns non-penetrating back-shield missile hits
  into blocks for everyone. The perk only matters for missiles that pass through the shield on the back, and never for metal
  shields, which stop those too.
- **Description vs code.** Piercer: "ignore armors below 20" means any armor under 20 counts as 0. Armor of 20 or more gets no
  reduction (a cliff, not a flat -20). Counter Fire: "while equipped" really means wielded in the main hand, and thrown weapons
  count as projectiles. Loose and Move: only the *wielded* crossbow's encumbrance is skipped. Long Shots: zoom is camera-only.
- **Epic perk gate.** Mighty Pull uses the troop's raw `GetSkillValue(Crossbow)` against 200 (`MinSkillRequiredForEpicPerkBonus`).
  Only a few native skill sets go over 200 (values 210/240/260 exist in SandBoxCore).
- **War Sails strike model.** `NavalStrikeMagnitudeModel.CalculateAdjustedArmorForBlow` calls the base model and then
  recomputes `num` from `baseArmor`. That throws away the base result, including the
  `ArmorPenetrationMultiplierCrossbow/Bow` scaling. Piercer and Puncture still apply once, not twice. Vanilla quirk, not RBM's.
