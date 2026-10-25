# Battle Replay

`battle-replay.html` is a single self-contained page (no build step, no library) that replays one RBM field battle from its logs, checks the battle for the AI problems we keep finding by hand, and runs a small what-if sandbox. Open it in a browser, or publish it as an artifact. It opens on an embedded sample battle.

## Which logs to load

Both are in `Documents\Mount and Blade II Bannerlord\Configs\RBM\logs\`. Turn them on in the RBM settings (Debug & Logging):

| File | Toggle | Gives the page |
|---|---|---|
| `terrain\terrain_<stamp>.log` | Battle terrain log (`battleTerrainLoggingEnabled`) | the map (FEATURE lines), plans (SPOTS, CAVALRY, HAMMER, CHOKEHOLD, BATTLEPOS, RESERVE, FLANKPLAN), TACTIC / REPICK, BEHAVIOR events |
| `ai\ai_<stamp>.log` | AI behavior log | formation tracks (FORM, about 1/s), CHANGE lines, TEAM / SELECT |

Use the pair with the same timestamp (the AI log's can be a second off). Drop both on the Load tab, pick them with the file inputs, or paste one at a time. Either file alone also works: terrain only gives the map, plans and events; AI only gives tracks and the track-based checks. Files are read in the browser and go nowhere.

## Tabs

- **Events**: TACTIC, BEHAVIOR (terrain log), CHANGE (AI log), PLAN (re-plans, FORMSHAPE, FLANKUSE...) and SIM lines; filter by team, formation and kind; follows the replay time. Click a line to jump there.
- **Checks**: findings over the whole battle with severity, click to jump; also drawn on the timeline and as dashed rings on the map. "Copy findings as text" for a bug report.
- **Sim**: the sandbox (below).
- **Load**: files, drop zone, paste, back to the sample.
- **Settings**: trail length and every check threshold (kept in this browser only).

## Checks

| Check | Default rule |
|---|---|
| Order point flipping | Move order point jumps > 15 m and comes back within 10 m of where it was, 3 times in 30 s |
| Moving backward | AI formation under Advance/Embolon/FlankSweep/Hammer, Move order, centre moves > 6 m away from the enemy main infantry in 8 s, for 4 s or more |
| Army stall | attacker main infantry closes < 10 m on the defender's main infantry in 60 s while not engaged (no attacker within 40 m of an enemy) |
| Too far from main infantry | FormationCohesion limits as edge gaps to the nearer of the anchor's footprint and its destination: defender ranged 40 / melee 30, attacker 70 / 50, reserve plug 80 m; 10 s or more; formations holding a spot or in cover, charging, mounted or within 25 m of an enemy are skipped |
| Overlapping a friend | > 30 % of a formation's footprint (5 x 3 samples, 2 m margin) inside a friend's, for 5 s, only while it stands at its own order point (passing through on the march is not a chosen place) |
| Charge started far away | Charge/ChargeToTarget of 3 s or more starting > 150 m (mounted 400 m) from the nearest enemy |
| Charge that never comes back | charging > 60 s in total with no enemy within 100 m |
| Spot / lane churn | > 3 take/release (also cover take/release) in 60 s for one formation |
| Behavior flip-flop | > 4 CHANGE BEHAVIOR lines in 30 s |
| Trigger by an insignificant enemy | a BEHAVIOR `why` with `fN:Class men=A power=P vs men=B power=Q` where A < max(10, ceil(0.2 B)) and P < 0.25 Q (ThreatSignificance) |
| Hold broken by cavalry | Hedgehog / HoldChoke engage, charge or contact whose `why` names a mounted formation |
| Tactic churn | > 2 tactic changes in 60 s |

## Sandbox

Approximate. It starts from the current replay frame and steps every 0.5 s at fixed speeds (foot 3.5 m/s, mounted 8 m/s). Rules, with the C# constant names, are in `SIM_RULES` in the page and editable in the Sim tab:

- Front-centre rule: a Move point is the formation's front centre, its ranks behind it.
- Defender main infantry holds the latest published battle position (or the choke hold when the tactic is Hold the Choke). Early mode (HoldPosition): a significant enemy within 60 m of the hold engages it, latched. Late mode (HoldLateCharge, choke / hedgehog): enemy foot within 15 m = Contact, fights in place; charge only for significant foot (archers wait while significant enemy cavalry is within 150 m); cavalry never starts Contact; leash recall beyond 100 m when not in melee; recall after 15 s once no enemy foot of 5+ men has been within 60 m for 10 s; lineGone charge.
- Defender archers: archer place from the plan, FormationCohesion pull (40 m), FriendlyOverlap shift back (0.3); ArcherCover takes cover behind the nearest friendly infantry with >= max(20, 0.5 x archers) men when a significant melee formation is within 35 m, or within 70 m (foot) / 120 m (cavalry) and closing >= 1 m/s; not when friendly infantry already screens; at most one change per 6 s; released when the threat dies or stays > 120 m and not closing for 8 s.
- Reserve plug: holds the plug point, commits on a significant enemy within 50 m, 45 s after the line's contact, or when the line is gone.
- Attacker infantry advances straight at the defender's main infantry and charges at 40 m; with the split-archers wait on, it holds its front while its archers lag > 60 m (30 s at most, re-armed 90 s after the wait began).
- Attacker archers keep 110 m from the nearest enemy foot (cohesion 70 m) and fall back behind their infantry from a significant melee formation within 40 m; attacker cavalry charges the nearest enemy archers.
- Significance uses men only (power = men).

As a player: select a formation, Take control, then Move (click where its front should stand), Charge (click an enemy) or Halt; or spawn a detachment of any size and kind and walk it at a square, a choke line or the archers. Every decision shows in the Events feed with its rule and reason.

Not modelled: terrain under the sim (formations walk through water and woods), shapes (square/circle), shield wall, native behaviors, real casualty rates (made-up melee and missile losses), spots/lanes/hammer use, horse archers.

## Known gaps

- The AI log has no facing: it is inferred from the order point, the movement or the nearest enemy.
- The logs have no terrain outlines: areas are ellipses from position, size along/across the battle axis and area; water also gets a band to its nearest point.
- The embedded sample is trimmed (half-metre positions, no speed/deviation/weights columns, unused terrain fields dropped). Loaded logs keep every field.
- Checks are approximations of the C# rules from logged data, not the rules themselves. The stall check also fires on a deliberate wait (an archer wait, a ranged harassment).
