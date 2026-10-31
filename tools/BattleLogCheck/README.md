# Battle log checker

`Check-BattleLogs.ps1` reads one battle's **AI behavior log** (`logs/ai/ai_<stamp>.log`) and **terrain log**
(`logs/terrain/terrain_<stamp>.log`), runs ten checks for the usual signs of dumb battle AI, and prints
PASS / WARN / FAIL for each, with evidence lines (time, team, formation, numbers). This replaces reading
thousands of log lines by hand after every test battle.

Turn the logs on in the RBM settings (Debug & Logging): the AI behavior log and `battleTerrainLoggingEnabled`.
Without a terrain log, checks 4, 5, 8 and 10a have little or nothing to read. The other checks still run.

## Run it

From the repo root (Windows PowerShell 5.1, no install needed):

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BattleLogCheck\Check-BattleLogs.ps1
```

This checks the newest battle. Options:

| Option | Meaning |
|---|---|
| `-Stamp 2026-10-22_14-54-29` | check this battle (the part after `ai_` in the file name) |
| `-Last 5` | check the 5 newest battles, one report each |
| `-Out C:\temp\check.txt` | write the report to a file (UTF-8) instead of printing it |
| `-LogsDir <dir>` | the folder that holds `ai\` and `terrain\` (default `<My Documents>\Mount and Blade II Bannerlord\Configs\RBM\logs`, using the real Documents folder even when it has been moved, e.g. `D:\Documents`) |
| `-MaxEvidence 20` | evidence lines shown per check (default 8; FAIL lines come first, then WARN) |
| `-Threshold @{ Name = value }` | override any threshold below |
| `-Verbose` | phase timings (a 2 MB AI log takes about 2 s) |

A hashtable cannot be passed through `-File`, so use `-Command` to override thresholds:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& '.\tools\BattleLogCheck\Check-BattleLogs.ps1' -Last 3 -Threshold @{ StallMinShrink = 0.05; CohesionDefender = 80 }"
```

Exit code: 0 = all PASS/INFO, 1 = some WARN, 2 = some FAIL (the worst over all battles checked).

An `ai_` log and a `terrain_` log count as the same battle when their stamps are the same or at most
`PairToleranceSec` (5 s) apart. The two files can be stamped a second apart.

## What the report shows

**Header:** scene, map type hint, battle start (deployment end = the terrain log's `# gathered at t=` line;
without a terrain log, the first `Stop -> Move` order change), mission end, how long the fight lasted, and for
each team its side, culture, starting men, its tactics in order (`Name@t`), how much of the time its main
infantry was AI-controlled, and the terrain log's TACTICSUMMARY.

**Main infantry** = the team's largest formation whose `primaryClass` (QuerySystem.MainClass) is Infantry,
chosen again at every 1 s snapshot. The enemy main is the largest Infantry formation of the other side.
Formations with `aiControlled=0` (the player's own commands) are never judged.

**Checks:**

1. **Attacker progress.** For each AI attacker team, the distance between its main infantry and the enemy main
   infantry, from the battle start (+`StartGrace`) until contact (attacker main `distToEnemy <= ContactDist`).
   Every `StallWindow` window must shrink that gap by at least `StallMinShrink`. A window that does not, starting at
   a gap of `StallMinGap` or more, is a stall. Overlapping windows merge into one episode, reported with the gap
   before and after, the attacker's own advance toward the enemy, and its behaviors, orders and tactic. **FAIL** when
   the attacker's own advance is under `OwnAdvanceShare` of the gap (it stood still or walked back). **WARN** when it
   advanced but the gap held anyway (the defender withdrew, e.g. to its battle position).
2. **Backward drift.** For each AI foot formation (Infantry/Ranged class, 10 or more men) under a `Move` order, two
   kinds of drift, each judged step by step (1 s), at least `DriftMinSpeed` m/s, lasting `DriftMinSeconds` or more
   and covering `DriftMinMetres` or more (one missed step is tolerated):
   - *away from the enemy* (attackers only): its distance to the enemy main infantry grows. An infantry formation
     doing this is a **FAIL**. A ranged formation is a **WARN**, and only when it is not walking toward its own
     order point (archers walking to a spot behind the line are not reported).
   - *away from its own order point* (both sides, **WARN**): it moves against the direction to its order point,
     which is at least `DriftMinOrderDist` away.
3. **Flicker.**
   - *Order point reversals* (AI foot formations under `Move`): a jump of more than `FlickerJump` m between two
     snapshots, in the opposite direction (more than 120 degrees) to the previous jump. Jumps at a behavior switch
     are skipped. `FlickerWarnReversals` or more inside `FlickerWindow` is a **WARN**, more than `FlickerReversals`
     is a **FAIL**. Mounted formations are skipped, because their order point circles by design.
   - *Flips*: CHANGE BEHAVIOR, CHANGE ORDER, CHANGE HA_ORBIT and arrangement (`arr=`) changes per formation. More
     than `FlipsWarn` inside `FlipWindow` is a **WARN**, more than `FlipsFail` is a **FAIL**.
4. **Charges.** Lists every late-charge or engage transition in the terrain log's BEHAVIOR lines
   (`...->Charge`, `Hold->Engage` with `lineCharged`/`squareCharged`/HoldPosition reasons,
   ReservePlug `Holding->Committed`). It also lists native charges from a holding behavior in the AI log
   (`Hold*/Defend*/... -> BehaviorCharge`). Each line gets the formation's `distToEnemy` at that moment.
   - **FAIL**: an `outshot`, `advantage` or `behind` charge started with the enemy more than `ChargeFarDist` m away.
   - **WARN**: any other engage from that far.
   - **WARN**: an engage that enemy missile troops set off (`fN:Ranged` / `fN:HorseArcher` in an
     enemyNear / infantry / selfThreat / battleJoined reason). That is bait.
   - *Recall loops*: charge, recall (`Engage->Hold`), then charge again, with the two charges less than
     `RecallLoopGap` s apart. More than `RecallLoopMax` loops is a **FAIL**, fewer is a **WARN**.
   - *Bait loops*: `ChargeLoopCount` or more local `behind` / `infantry` charges (`->Charge` lines) of one formation at
     the same enemy formation (the first `fN:Class` in the reason) within `ChargeLoopWindow` s: **WARN**.
   - *Charge starts* (USER RULE 2026-10-22, 40 m since 2026-10-30: the real charge only within 40 m of its target; `Orders/ChargeGuard.cs`
     walks it there as a Move): every AI log `CHANGE ORDER` line of an AI-controlled foot formation (not
     Cavalry / HorseArcher) that switches to `Charge` / `ChargeToTarget` is checked against the last FORM sample at
     or before it. A `distToEnemy` above `ChargeStartDist` + `ChargeStartSlack` m (40 + 30: distToEnemy is centre
     to centre) is a **WARN**. One INFO line counts the starts, the far ones and the terrain log's ChargeGuard
     `charge advance` / `charge go` / `charge drop` lines (it turns into the first WARN line when there are far starts).
5. **Plan churn.** Counts FLANKPLAN replan/stale, SPOTS and CAVALRY replans, BATTLEPOS repicks (and how many moved
   the hold), REPICK lines, FLANKUSE followPlan re-applies per team, and how each side's flank `attackSide` changed
   over time. It also gives per-kind totals of formation-level take/release events: spot, cover, lane, reform
   (CavalryCharge `reform take` / `reform change`: more than 6 per formation in 60 s = WARN), cohesion pull/unpull, shift/unshift, HoldPosition/HoldChoke Holding<->Moving, hedgehog shape, and shield
   wall/line. **WARN** when:
   - one formation has more than `ChurnMaxPerWindow` events of one kind in `ChurnWindow` (more than 1 pair per 20 s);
   - a side's attackSide changes more than `AttackSideFlipsMax` times;
   - a team's tactic is re-applied for a newer flank plan more than `FollowPlanMax` times.

   The re-plans themselves are only counted, because they run on a timer by design.
6. **Cohesion.** At each snapshot it measures the rectangle-to-rectangle edge gap between every AI foot formation
   and its team's main infantry = the largest Infantry formation, AI- or player-led (since 2026-10-22: AI archers
   must keep to a player-led line too, and the game's anchor includes it; the WARN then names the main as
   `player-led fN`). The rectangles use FORM `w`/`d` and face toward the enemy main (the real facing,
   FORM `dir=` in newer logs, is not used here), so this is an approximation. The limits are `CohesionDefender` m, `CohesionAttacker` m,
   and `CohesionReserve` m for `RBMBehaviorReservePlug`. Staying beyond the limit for `CohesionSeconds` or more is a
   **WARN**. Not judged:
   - while either formation is within `MeleeDist` of an enemy;
   - while either is under a Charge or Retreat order;
   - the hedgehog's archers;
   - a formation on a reaction task (its FORM behavior is `RBMBehaviorReact`, or a REACT assign..end span covers
     the sample). Check 10 judges those.
7. **Idle army.** "Static" means the main infantry moved less than `IdleMoveMetres` over the last `IdleSpan` s.
   **FAIL** (stand-off): both main infantries static, more than `IdleDist` m apart, for more than `IdleSeconds`.
   **WARN**: the AI attacker's main infantry alone static that long.
8. **Terrain read.** **FAIL** when the terrain tactic factors were never published. **WARN** when:
   - the TERRAIN line was skipped, or there is no FEATURES line;
   - `sameGround=0`;
   - water crosses the axis and the armies share ground, but there are no bridge cells (TERRAIN_WATER
     `bridgeCells`) and no ford on the path (the grid may not see the crossing).

   Otherwise it prints sameGround, FEATURES, choke/water/ford/bridge-cell counts, CHOKEHOLD and BATTLEPOS plan
   results, and the HEDGEHOG verdicts so you can look them over. An INFO line shows how outnumbered the defender
   was (`outnumbered=r k=K`, with the battle position's weights and limits), the position's winner kind and the
   choke read's r/k.
9. **Errors.** In the terrain log, any `error=` / `failed=` / `rayFailed=` field is a **WARN**. Any `# FAILED`
   line or exception text, in either log, is a **FAIL**.
10. **Reactions.** Does the AI answer what happens in the battle (ReactionMonitor, `aiReactionsEnabled`)?
    - **a) REACT lines** (terrain log, written by the monitor). It prints the count per kind and line type and the
      task end reasons, then flags:
      - an R1 (own formation attacked) or R2 (enemy cavalry round a flank / at the archers) event that lasted at
        least `ReactEventMin` s with no `assign`, `covered` or `line` (expected / latched) within `ReactWindowR1` /
        `ReactWindowR2` s: **WARN** "nobody responded", with the event's `none` reasons; more than `ReactMissFail`
        of them is a **FAIL**. An event whose `none ... noResponder` rejects are all structural (`victim`, `player`,
        `fewMen`, `general`, `mainInfantry`, `horseArchers`, `ranged`, `class`, `notCavalry`, `noAI`, `noBehavior`:
        nobody could have answered, e.g. an army of one line and its archers) is only counted in an INFO line;
      - an `assign` with no `end` although the mission went on `ReactTaskMax` s after it: **WARN**;
      - one responder assigned more than `ReactPingPong` times in `ReactPingWindow` s: **WARN** (ping-pong);
      - leash + chaseTimeout over `ReactLeashShare` of all task ends (at least `ReactLeashMinEnds`): **WARN**.
      R3 hunts (mode `Hunt`, on enemy archers) count with the other tasks in all of these; a `hunts (R3)` line adds
      how many were assigned and how they ended (screened / chaseTimeout / haRunning ...). R6 `request` / `timeout` /
      `cap` lines are only counted. A `deter` line (INFO) lists R7 shadow detections (enemy riders parked round our
      flank / rear), `Deter` tasks (R7, and R3 on riding horse archers; `Hunt->Deter` phase lines are hunts turned
      into one), their strikes / releases (back to the screen, no chase) and end reasons; an `event closed with a live
      task` line counts `clear ... task=handoff=` (the task moved to another event on the same attacker),
      `task=screenHeld` (an Intercept / Deter kept its screen after a feint) and `phase ... side` stage side re-picks.
      The main infantry's refusals come as `mainInfantry(<why>)` and still count as structural.
    - **c) REPICK balance** (terrain log, `REPICK ... reason=balance switched|kept|blocked(...)`, the R6 re-judge of a
      team's tactic): every evaluation is listed; one team switching more than `BalancePingPong` times in
      `BalancePingWindow` s: **WARN** (ping-pong).
    - **b) FORM lines** (AI log; works with the monitor off and on old logs): an AI formation within `MeleeDist` of an
      enemy that lost at least `HelpLossShare` of its men in `HelpLossWindow` s, while a free non-ranged AI formation
      of its team (10+ men, `distToEnemy` above `HelpFreeDist`, within `HelpReach` m; not the main infantry, the
      general / bodyguard, nor a hedgehog / choke / reserve plug) stood by and none closed
      `HelpCloseMetres` m on it (or took a React task): **WARN**. Not judged as victims, as in the monitor: the main
      infantry and a hedgehog / choke line, which fight in place by rule.
11. **Facing.** Needs the FORM lines' `dir=` field (Formation.Direction; older logs: INFO, not judged). For each
    team's AI main infantry while it holds (behavior HoldPosition / HoldChoke / Defend / HoldHighGround, not under a
    Charge / Retreat order, `distToEnemy` above `MeleeDist`): the enemy's concentration = the enemy formations within
    `FacingRange` m with at least max(10, 0.2 x the line's men), weighted by men x class (infantry 1, cavalry 0.7,
    ranged 0.4, horse archers 0.2). Facing more than `FacingOffMax` deg off it (the concentration more than
    `FacingMinDist` m away) for more than `FacingSeconds` s: **WARN** (the line did not turn to the enemy).
12. **Support fire.** The terrain log's BEHAVIOR `support take|keep|move|rotate|shift|lost|none` lines (SupportFire:
    AI foot ranged once the frontlines connect). **INFO** per formation: the count of each kind and the seconds in
    support mode (from the first take/keep/move/rotate/shift to `support lost`, or the mission end). In support mode a
    formation's order point (FORM `order=`) moving more than `FlickerJump` m between two samples more than
    `SupportMovesMax` times per `SupportWindow` s: **WARN** (the archers should stand and shoot, not wander). No
    support lines at all (older build, terrain log off, no connected frontline): INFO.
13. **Decision trace.** The AI log's `DECISION` / `FLIP` lines (DecisionTrace, 2026-10-23: who changed each
    formation's movement order, facing, arrangement, width, behavior or tactic; format in the AI log header).
    **INFO** per formation: the number of DECISION and FLIP lines and its four most frequent `field:writer` pairs
    (e.g. `move:formAI:RBMBehaviorArcherSkirmish x12`, `move:formAI:BehaviorCharge>chargeGuard x3`). Every `FLIP`
    line (Move point reversing a > 20 m jump more than 3 times, or order kind / arrangement / behavior changing more
    than 2 times, in 30 s; thresholds in `DecisionTrace.cs`, not here): **WARN**, with the writers that fought and the
    sequence of values. Start a "why did it do that" with this check: the writers named are the rules to read. No
    DECISION / FLIP lines (older build): INFO.
14. **Gap shadow.** Every terrain-log field `key=Xm(old Ym)` (FormationGeometry's footprint gap next to the measure the
    site used before): **INFO** per site (`BEHAVIOR <behavior> <what>` / `REACT <kind> <what>` / line kind) and key, with
    the count and the mean / min / max of new - old in m (negative = now reads closer), to judge each threshold before
    retuning it.
15. **Approach.** The terrain log's `APPROACH` lines (TerrainApproach, the attacker main infantry's route): **INFO** per plan (chosen kind, gain, why) and the drop reasons; **WARN** when a route is dropped for `noGain` within 30 s of being taken, dropped `stalled`, or the main stands still (< 3 m in 10 s, FORM samples) while following it.
16. **Charge flicker.** The terrain log's BEHAVIOR `ChargeGuard` lines: **WARN** when one formation's `charge advance` / `charge go` lines switch more than 3 times within 30 s (a formation that keeps releasing and losing its charge, e.g. stragglers in melee far ahead of the main body).
17. **Army composition.** The terrain log's COMPOSITION / COMPEVENT lines and the k-driven BEHAVIOR lines (ArmyComposition): last shares / tokens / k per team, a COMPEVENT timeline (INFO); flicker, k-driven lines with every k at 0, a long wearDown hold, a token archer stand: **WARN** (details in the script's check text).
18. **Cavalry cohesion.** Every AI melee cavalry formation (FORM class `Cavalry`, `aiControlled=1`): the time it spends under a
    `Charge` / `ChargeToTarget` order with FORM `dev` above `CavScatterFactor` (4) x `idealDisp`. One continuous stretch
    (sample gaps up to `CavScatterGapSec` 3 s) longer than `CavScatterWarnSec` (30 s) is a **WARN**, longer than
    `CavScatterFailSec` (90 s) a **FAIL**, with from-to and the mean dev / idealDisp. **INFO** per formation from the
    terrain log's BEHAVIOR `CavalryCharge` reform lines: `reform take` / `change` / `none` counts, releases by why
    (`newCharge:<reason>`, `none`, `behaviorEnded` ...) and the reject reasons of the `none` lines. `ReformNoneWarn` (5)
    or more `reform none` with no `reform take`: **WARN** (it never found a place to regroup).

The **SUMMARY** line counts the checks per status and repeats the first six FAIL/WARN evidence lines.

## Thresholds

All thresholds are at the top of the script in the `$Cfg` hashtable, with a comment on each. Edit them there, or
pass `-Threshold` for a single run.

| Name | Default | Used by |
|---|---|---|
| ContactDist | 40 m | 1: the approach ends when the attacker main is this close |
| StallWindow / StallMinShrink / StallMinGap | 60 s / 0.10 / 60 m | 1 |
| OwnAdvanceShare | 0.10 | 1: FAIL vs WARN |
| StartGrace | 5 s | 1, 2, 3, 6: ignore the deployment-end burst |
| DriftMinSeconds / DriftMinMetres / DriftMinSpeed / DriftMinOrderDist | 10 s / 10 m / 0.3 m/s / 5 m | 2 |
| FlickerJump / FlickerWindow / FlickerWarnReversals / FlickerReversals | 10 m / 20 s / 3 / 4 | 3 |
| FlipWindow / FlipsWarn / FlipsFail | 60 s / 6 / 12 | 3 |
| ChargeFarDist | 150 m | 4 |
| RecallLoopGap / RecallLoopMax | 30 s / 2 | 4 |
| ChargeLoopCount / ChargeLoopWindow | 3 / 120 s | 4 |
| ChurnWindow / ChurnMaxPerWindow | 60 s / 6 | 5 |
| AttackSideFlipsMax / FollowPlanMax | 2 / 3 | 5 |
| CohesionDefender / CohesionAttacker / CohesionReserve / CohesionSeconds / MeleeDist | 60 / 90 / 100 m / 20 s / 30 m | 6 |
| IdleDist / IdleSeconds / IdleSpan / IdleMoveMetres | 200 m / 120 s / 10 s / 5 m | 7 |
| ReactWindowR1 / ReactWindowR2 / ReactEventMin / ReactMissFail | 8 s / 5 s / 10 s / 3 | 10a |
| ReactPingPong / ReactPingWindow / ReactLeashShare / ReactLeashMinEnds / ReactTaskMax | 4 / 60 s / 0.5 / 4 / 95 s | 10a |
| HelpLossWindow / HelpLossShare / HelpFreeDist / HelpReach / HelpCloseMetres | 15 s / 0.10 / 60 m / 250 m / 20 m | 10b |
| BalancePingPong / BalancePingWindow | 2 / 120 s | 10c |
| FacingOffMax / FacingSeconds / FacingRange / FacingMinDist | 60 deg / 30 s / 400 m / 40 m | 11 |
| SupportWindow / SupportMovesMax | 60 s / 6 | 12 (with FlickerJump) |
| (none) | | 13: the FLIP thresholds are DecisionTrace's own, in the game |
| ChargeFlipWindow / ChargeFlipMax | 30 s / 3 | 16 |
| CavScatterFactor / CavScatterWarnSec / CavScatterFailSec / CavScatterGapSec | 4 x / 30 s / 90 s / 3 s | 18 |
| ReformNoneWarn | 5 | 18 |
| PairToleranceSec | 5 s | pairing ai_/terrain_ files |
| MinBattleSeconds | 20 s | header note for very short battles |

How to tune:
- **A check flags something you judge fine:** loosen its threshold. For example, raise `FlickerWarnReversals`, or
  lower `StallMinShrink` for a slow, careful attacker.
- **A known bad battle passes:** tighten the threshold until it is caught, then re-run on a few good battles with
  `-Last 5` to make sure they stay clean.

## Limitations

- Snapshots are 1 s apart, so flicker faster than that (several changes inside one second) shows only in the
  CHANGE-line counts, not in order-point reversals.
- Cohesion uses rectangles facing the enemy main, because the real facing is not logged. A formation turned
  sideways can read a few metres off.
- "Main infantry" is the largest Infantry-class formation at each snapshot. If two formations are about the same
  size, the main can switch between them, which can add a little noise to the gap in check 1.
- Charges are judged by the formation's `distToEnemy`, which is the distance to the nearest enemy formation of any
  kind, not necessarily the one that set off the charge.
- Formation names map between the logs by index: terrain `formation=N` is FormationClass N (0 Infantry, 1 Ranged,
  2 Cavalry, 3 HorseArcher, 4 Skirmisher, 5 HeavyInfantry, 6 LightCavalry, 7 HeavyCavalry). The AI log uses the
  same names.
- New log line kinds or renamed fields are ignored silently. When the log format changes, update the parsing at
  the top of `Invoke-Check`.
