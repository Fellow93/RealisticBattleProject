# Changelog

## v4.5.1 (changes since v4.5.0.2)

### Combat
- RBM's managed core parameters (spear/thrust tuning) now actually load. They were never applied before. They apply only with RBM Combat enabled.
  - Swings that hit the arm deal 0.5x damage (vanilla 0.15x); thrusts that hit the arm deal 0.034x.
  - Fuller thrust and late-swing speed curves; ballista bolts have 5x drag.
- Fixed the two-handed mace skill coefficient (1.125 → 0.1125). Every swing was hitting the damage cap from around skill 25.
- Punch damage: bare fists 0.4 → 0.3, cloth/leather gloves 0.3 → 0.4. A glove no longer loses to a bare fist.
- Fixed unarmed blows while holding an item applying the skill formula twice.
- Shield impacts: blocks and parries no longer spawn body-hit dust or blood. Metal shields throw sparks on every block, and wooden shields splinter on heavy blocks.
- Restored vanilla collision reactions that RBM's armor thresholds skipped: kick and bash follow-through, hilt hits, shrugged-off blows and body punches. The couched lance keeps RBM's stick/bounce but regains the vanilla pass-through roll, so the Skewer perk works again.
- Weapon sticking and hit particles now use the blow's damage type, so a swing that doesn't land on the blade counts as blunt.

### Posture / AI
- Posture crush-through now uses the same damage math as live combat. The AI's copy dated from 2023 and had drifted from it. Crush-through damage is scaled x0.85 to compensate for the change.
- Fixed unarmed crush-through dividing by zero (NaN damage).
- Cavalry-charge knockdown routs flee in formation again. Since 4.5.0 these troops left their formation, came back formationless, and then charged on their own, ignoring orders. This was most visible in large battles. Charge routs also no longer set off the flee-contagion morale wave.
- The rally now brings back only charge-routed troops. Ordered retreats and normal morale routs are left alone.
- Fixed banner bearers added by Raise Your Banner clumping together, and bearers "attacking air". Bearers now always stand next to a neighbouring soldier in the formation grid.

### Campaign
- **New: per-settlement recruit pool.** Manpower is now a finite, regenerating resource.
  - Its size comes from prosperity (towns/castles) or hearth (villages): it grows +0.03 per point a day, up to a maximum of 0.2 per point.
  - Each new volunteer costs 1 point. Each new garrison soldier costs 1 + (garrison / soft size)², where the soft size is 150 for towns and 100 for castles.
  - Garrisons stop growing when the pool falls to a reserve of 0.07 per point, which is kept for volunteers.
  - Militia and troop transfers are unaffected.
  - Building bonuses: Train Militia / Housing daily project +25% growth, Raise Troops daily project +50%, Castellan's Office max +10/20/30%, Barracks soft size +20/40/60, a fief's Roads and Paths +10/20/30% growth for its villages.
  - The settlement tooltip and the garrison tooltip show the pool, the per-soldier garrison cost and the building bonuses.
  - Building descriptions that promised garrison capacity or automatic recruitment (which do nothing under RBM) have been rewritten.
- **AI kingdoms form armies and take fiefs again.** Their siege targets always scored 0, so armies never gathered.
  - Fresh sieges now need 2.5x strength (it was 3x).
  - AI lords top up their parties from their own clan's surplus garrisons, so they meet the party-size requirement for joining armies. Lords who already qualify aren't pulled home.
- New option: "AI Lords Buy Equipment". Turn it off if another mod manages lord gear. It is on by default. Pack trains are unaffected.
- Goods are now removed and paid for by the exact stack (including item modifiers) everywhere: citizen demand, troop food and drink, refining, minting, construction, caravans and workshops. Previously a sale could be paid for goods that never left the stack, or take from the wrong stack. A caravan that is short of cargo is now trimmed instead of creating goods from nothing.
- Recruit, upgrade and luxury market purchases pay what the stack they actually take is worth.

### Auto-resolve
- Melee profiles use the shared combat skill table, so future retunes apply to it automatically. Blunt thrusts (whips, pitchforks, training weapons) are no longer pinned at the damage cap.

### Stability / fixes
- Fixed markets getting permanently stuck with NaN prices (#22). Existing saves are repaired on the next daily tick. Town food budgets and rounding are also guarded against NaN.
- Fixed a race between the save thread and the town food cache, and a crash for towns with no owner.
- Config: decimal settings now parse and save correctly on comma-decimal locales. Previously 0.05 was read as 5 (thrust modifier, weapon-type factors, horse price).
- A config version mismatch that resets settings is now logged.

### UI
- Weapon comparison tooltip: bow, crossbow and throwable rows show the compared weapon's own values, rows stay aligned with the target column, and missing values are left blank instead of showing -100 or 0.
