# RBM battle terrain log format

The terrain log (`logs/terrain/terrain_*.log`, toggle `battleTerrainLoggingEnabled`, Debug & Logging) is written by
`RealisticBattleAiModule/AiModule/Terrain/BattleTerrainLogic.cs`, one file per land field battle. The data is
`BattleTerrain.Current`, computed in every field battle with RBM AI on; the log only prints it. The TERRAIN /
TERRAIN_* / FEATURE lines are formatted by `BattleTerrainLogFormat.cs`. `tools/BattleLogCheck/Check-BattleLogs.ps1`
reads it. Tab separated, ASCII only (bearings `44deg`).

The log opens with a short header: `# RBM battle terrain log ... scene=<scene>  t=<time>`, when it was opened and
when RBMAI.dll was built, and a pointer to this file. Runtime `#` lines: `# gathered at t=..: mainMs=.. (rayMs=..)`,
`# terrain tactic factors published at t=..: applied=..`, `# FAILED [key] Type: message`, `# mission ended at t=..`.

## Timing

The whole-map grid is built in AfterStart (mission load). On the deployment-end tick the MAIN thread gathers what
touches the engine (men, the obstacle rays, tactical regions; logged as `# gathered at t=..: mainMs=..`), then a
BACKGROUND task does the grid work; the snapshot is published and these lines written on the main thread a few frames
later, so every TERRAIN* line's t= is the PUBLISH time (the men's positions are from gatherT).

## Terrain read

```
TERRAIN t  TERRAIN  src  scene  men  frontage  map  field  ms  times   (BattleTerrainRead, field battles, once
       after deployment; the tactic choice reads it through TerrainTacticFactors). Frame: attacker centre looking
       at the defender centre; left/right are the attacker's.
          src      = deployment | noDeployment (gathered 3 s into a battle without a deployment phase);
                     'skipped (why)' instead of the fields when there is no map grid or a side has no men
          men      = men atk/def, dist (m), dz = defender minus attacker mean height
          frontage = each army's width across the axis (5th-95th percentile of its men)
          map      = the whole playable map (boundary bounding box): size, bounds source, step, cells
          field    = the strip between the armies (+50 m each end, 300-600 m wide): width, then out = share
                     outside the boundary, clutter = share of land with no navmesh (rocks, trees, buildings,
                     cliffs), deep = deep water share, slope = mean degrees of walkable ground, >15/>25 =
                     share steeper
          ms       = buildMs (whole-map grid, mission load), of which clearMs = the clearance map (exact
                     Euclidean distance transform, 2 passes) and edgeMs = the edge distance (the same
                     transform, distance to the map edge alone: places within 60 m of it are not used,
                     BattleTerrainRead.EdgeMargin); mainMs = the main-thread gather (the one-frame cost), of
                     which rayMs = the obstacle ray casts; bgMs = the background part (all groups); wallMs =
                     gather start to publish
          times    = gatherT / publishT = mission times of the gather and the publish; frames = ticks polled
                     until the background part finished

TERRAIN_* t  KIND  name=value ...  ms   -- written right after TERRAIN, same frame. Each ends with its own ms
       (background); error=Type:message if the group failed or its main-thread input did (rays:/tactical:
       prefix). A/D = the army centres snapped to the nearest walkable cell (<= 40 m; none = dependent fields
       '-'). Paths are A* over the grid's walkable cells (8 neighbours). Wet = walkable under shallow water.
       '-' = no value.
   TERRAIN_SPACE
          forceAtk/Def = men/foot f/mounted m/ranged r (launcher with ammo); speedAtk/Def = mean max
                     speed foot/mounted (m/s); snapAtk/Def = how far the centre was moved (m)
          widths   = walkable width across the axis at 21 lines A(0)..D(1), fraction:total width (m,
                     rounded); each line's left/right halves and their ends are in the snapshot
                     (SpaceInfo.WidthProfile)
          flankAtkL/R, flankDefL/R = reach/len m/len per straight x/time s: shortest route from the
                     mover to 60 m behind the other army, kept 50 m off its line and out of the other
                     half within 120 m of it along the axis; L/R = the attacker's left/right for both;
                     time at the mounted pace (foot if no riders); '-' = no walkable ground behind it.
                     Cells within 30 m (half EdgeMargin) of the map edge are closed to the route (not
                     its goal): a way round only along the boundary, or behind an army with its back
                     to it, is unreachable (0/-/-/-); TerrainFlankPlan reads these routes
          sameGround = A and D in one walkable 4-connected area (0 = cut off, e.g. a river with no crossing)
   TERRAIN_OPEN   Clearance = metres from a walkable cell's centre to the nearest non-walkable cell's centre
          (rocks, trees, buildings, cliffs, deep water, the map edge): the radius of open ground around it;
          wet cells are walkable.
          clearAtkLine/DefLine = median clearance of the cells under its line (frontage across the axis
                     through its centre; blocked = 0)
          corridorWidth = the widest corridor between the armies' areas (every walkable cell within
                     max(30 m, half its frontage) of each centre): of all walkable 8-connected routes, the
                     one whose smallest clearance is largest (exact, union-find in descending clearance);
                     2 x that bottleneck clearance; corridorFront = corridorWidth / wider frontage. '-' =
                     no route (separate grounds)
          exitAtk/Def = width m/x own frontage: the widest way out of the army's area to its exit line
                     (its stretch along the axis: 20% of dist, at least 60 m, >= 20 m past its area):
                     bottleneck width, that / its OWN frontage (< 1 = it must squeeze out)
          midCorridorWidth/Front = the widest route across the middle of the field: from anywhere behind
                     A's stretch line to anywhere beyond D's (bands across the whole map), same units as
                     corridor*; '-' when the armies are closer than two stretches
   TERRAIN_MOUNTED
          runupAtk = attacker riders onto the defender line (runupDef: the reverse): from its centre and
                     +-1/3 frontage back toward the charger, walkable ground < 15 deg (cap 250 m),
                     crossing steep/blocked bumps <= 8 m (water and the map edge end it at once):
                     max/min(end of the min: blocked|water|steep|edge|max) bumps <on the max>/<on the min>
   TERRAIN_SIGHT
          fireDef  = share of walkable field points (<= 300, between the lines, <= 200 m of D, 1 m up) seen
                     over the terrain from >= 1 of 5 eye points (1.7 m) on the defender line; fireAtk = the
                     same from A. Grid heights only (no trees, rocks or houses)
          rayFailed = obstacle ray casts that threw (only when > 0). The obstacle rays (main thread): up to
                     160 horizontal rays at 1.2 m through field-strip cells with no navmesh that border
                     walkable ground, from that walkable neighbour's centre through the cell's centre and
                     one cell beyond; each records what it hit first: a scene entity, a body with no entity
                     that is neither flora nor terrain by the masks, flora (re-cast with BodyOwnerFlora
                     excluded misses that hit), terrain (re-cast with BodyOwnerTerrain excluded too misses
                     it), nothing. The feature map's woods / rough step reads them.
   TERRAIN_WATER  where the grid's deep water and bridges came from, and the grid against the deep-water blocker's
          src = blocker (DeepWaterNavBlocker's grid, probed in its EarlyStart BEFORE it switched the deep faces off:
                     deep = its cell under the cell centre, bridge = any of its bridge cells in the cell's square with a
                     navmeshed deck there) | probe (no blocker grid: this grid's own ProbeCell at each centre). The
                     probe alone, run in AfterStart after the face pass, missed every bridge (2026-10-22 log: blocker
                     bridgeExcluded=72, bridge cells 0); step / blockerStep = the two grids' steps (m)
          deepM2 / blockerDeepM2 / probeDeepM2 = deep water m2 of this grid / the blocker's (after its clean-up) /
                     this grid's own probe; bridgeM2 / blockerBridgeM2 / probeBridgeM2 the same for bridge decks. The
                     grid's m2 should be near the blocker's (point samples of the same rule); probeBridge 0 with
                     blockerBridge > 0 = the post-pass probe still misses decks (expected, the reason for src=blocker)
          deepCells / bridgeCells / blockerBridgeCells / probeBridgeCells = the same as cell counts; deepDisagree
                     = cells where the probe's deep and the blocker cell under the centre differ (shorelines); deckMissing
                     = bridge squares with no navmeshed deck (left deep over deep water); deckFacesOff = bridge cells whose
                     deck face the blocker switched OFF (> 0 = the AI cannot path over that bridge: a blocker problem);
                     deckShallow = N/M: wet cells walkable only on a deck above them / of them joined to a deck over deep
                     water (a bridge's shallow ends, made bridge cells; the rest stay plain walkable: a rock in a ford)
```

## Feature map

```
FEATURES t  FEATURES  hill crest choke open flank woods rough water ford  total  paths  expanded  stepMs
       droppedNearEdge  ms   -- the FEATURE MAP (BattleTerrain.Features: named places built on the background task from
       the grid and the groups above, the API for tactic/behavior code), right after the TERRAIN_* lines.
       Counts per kind (caps hill 6, crest 4, choke 6, open 4, flank 4 = 2 per army, woods 6, rough 6,
       water 4, ford 4), total; paths/expanded = A* searches/cells of this step alone (the 4 most
       important hills' arrival times and the choke step's way-round searches); stepMs = ms per step
       (order hill, crest, open, flank, cover, water, ford, choke: chokes last); ms = the whole step (background);
       error=step:Type:message for a step that threw (that kind is empty, the others are kept).
       All thresholds are first guesses (BattleTerrainRead.Features.cs). Places within 60 m of the map
       edge (EdgeMargin) are not used: hills (top), crests, chokes, woods/rough (centre cell), fords
       are dropped and open fields lose those cells before labelling (a field touching the edge keeps
       its inner part); water and flanks are kept. droppedNearEdge = kind:count (open = fields with
       nothing listable left). woods = N (map:a density:b both:c [rays:d]) = the listed woods by source.
       THE WOODS MASK: in these scenes trees are entity-less static bodies NOT flagged as flora (the rays come
       back as entity-less hits, not flora) and their trunks punch only SMALL navmesh holes, so woods are found
       from: (1) map Forest TacticalRegions (authoritative): every in-bounds land cell (walkable or no navmesh,
       not deep water) within the region's radius; (2) small-obstacle density: no-navmesh components <= 64 m2
       (whole cells) are small obstacles (trunks, bushes); a land cell is woods when the small-obstacle cells are
       >= 0.05 (WoodsDensity, a first guess) of the land cells in the ~24 m square round it (everywhere, regions
       or not); (3) rays as confirmation: a large no-navmesh cluster (>= 1500 m2) with > 50% of its cells in
       (1)+(2) joins the woods unless its rays hit mostly terrain, as does one hit mostly as flora; a
       density-only area (no Forest cell) whose rays hit scene entities more than half the time (>= 3 hits:
       houses, props) is vetoed (left out, Rough objects src=density when >= 1500 m2). The mask is bad ground
       for cavalry (CAVALRY), no archer spot stands in it and >= 12 m of it in a row blocks a spot's sight
       line (SPOTS)
FEATURE t  FEATURE  id kind pos area size dAtk dDef between side imp  <kind fields>   (one per
       feature, kind order, most important first within a kind)
          id = kind + rank (hill1 = the most important hill), stable for the battle; pos = its
                     representative point x,y (world, rounded); area m2 ('-' for crest/choke/flank);
                     size = length x width along/across the battle axis (m, cell centres + one step)
          dAtk/dDef = from that army's centre: distance m@bearing deg (0 = its own forward toward the
                     enemy, + = its OWN left; the defender's left is the attacker's right)
          between = 1 when pos lies between the centres along A->D and within the field strip's
                     half width; side = left|right (more than max(50 m, half the wider frontage) off
                     the axis, the attacker's left/right), else behind (behind A) | ahead (beyond D) |
                     centre; imp = the rank key within its kind
          area kinds (open, woods, rough, water, ford) end with axisDist = m from their nearest cell
                     to the segment A->D, near = that cell x,y; pos = their cell nearest the centroid
          hill   = a local maximum of prominence (height - mean walkable height of a 120 m box) >= 4 m,
                     none within 100 m of a more prominent one; imp = prom / (1 + axis distance / 200 m);
                     pos = the top cell; area/size = the hilltop (connected walkable cells within 3 m of
                     the top). h = height, crest = walkable width across the axis through the top;
                     tAtk/tDef = s to get there at that army's walk pace (grid A* for the 4 most
                     important, else the straight line); first = atk|def|both|none (who gets there first)
          crest  = a local maximum of the height profile along A->D (every grid step, between the
                     centres) at least 2 m above the lowest ground on each side before higher ground or
                     150 m; imp = rise. at = fraction A(0)..D(1), rise = the smaller of the two, h; size -x
                     width = walkable width across the axis through it
          choke  = a narrow passage ANYWHERE on the map (BattleTerrainRead.Chokes.cs; size = length x width): the map-wide
                     passage scan (src scan) = saddles of the clearance map (walkable cells in descending clearance joined
                     by union-find; a cell whose taken neighbours form 2+ separate groups: merge = two areas meet, loop =
                     one area round an obstacle) with 2 x clearance <= limit + one step (limit = min(80 m, 1.5 x the
                     wider frontage)); each side opens into a room (clearance >= max(1.6 r, r + 8 m); a merge's areas
                     >= 5000 m2; a loop's sides do not meet within 3 cells and reach that within 600 cells); outside
                     EdgeMargin; bridge saddles (<= 2 cells from a bridge cell) checked first, then merges, narrowest
                     first, within 10 ms / 160 searches / 16 kept; one per passage (none within max(30 m, r1 + r2 + 2
                     steps) of a kept one); the passage CUT (cells within r + 1.5 steps closed) grid A* (<= 6000 cells)
                     finds no way round shorter than max(100 m, 4 x the gap across the cut) (a second try with a 2x cut
                     <= 60 m for an obstacle inside the passage; then width = all its gaps). Plus the old route
                     bottlenecks (corridor / mid-field / exit / width-profile minima < 1.5 x the frontage that must pass
                     them), merged into a scan choke within 40 m, else listed and measured the same way. pos = the
                     narrowest cell (the middle of the narrow stretch when as narrow: a bridge's middle); ratio = width /
                     wider frontage, at = fraction A->D, src = scan+corridor+mid+exitAtk+exitDef+profile, exitOf = whose
                     exit it is; type = bridge (grid bridge cells on the stretch or within r + one step of it: never
                     ford; pos moved onto the deck, boundedBy read there) | ford (>= half the stretch wet, deep water
                     beside) | neck (deep water both sides) | pass (no navmesh both sides, or the edge) | mixed (water
                     one side, no navmesh the other) | unknown; boundedBy = what the obstacle cells within r + 3 steps
                     are on each side (water | blocked | mixed | edge | unknown); width = narrowest walkable line (width
                     walker every 10 deg, <= 2 x clearance + one step), length = the narrow stretch (clearance ascent
                     from the saddle to max(1.5 r, r + 6 m) each side; '-' route chokes); line = the narrowest line's
                     ends; mouths = A..B (where it opened up; route: just past the cut); sides = atk:A|B|- def:A|B|-
                     same|opposite|unknown (a walk flood from both mouths with the cut closed; only the 8 most
                     important); separates = 1 when the two sides meet nowhere else (the only way across); onRoute =
                     the A->D grid path runs through it; wet = wet share of the stretch, water / ford = the listed
                     water / ford it touches. imp = wider frontage / width x 1/(1 + axis distance / 200 m), x 3 on
                     route, x 3 with the armies on its two sides
          open   = a region of clearance >= 50 m (the CORE of an open field: every cell >= 50 m from
                     any obstacle) and >= 60 m from the map edge, >= 10000 m2, largest first; pos = its most open
                     cell, maxClear = that clearance; armies = whose centre is in it or within 50 m
          flank  = each army's own left/right line end: army = atk|def, own = left|right; end =
                     blocked|water|edge|max and endDist = where the width walker from its centre stopped
                     (pos); spare = endDist - half its frontage; enemyRoute = the OTHER army's flank route
                     round it on that side (TERRAIN_SPACE format reach/len m/x straight/time s; '-' none)
          woods  = 8-connected components of the woods mask >= 2000 m2, imp = area; cover=woods,
                     src = map | density | both (each >= 25% of its cells inside a Forest region / passing the
                     density) | rays (a flora cluster alone)
          rough  = 8-connected no-navmesh clusters >= 1500 m2 not merged into the woods, by the obstacle
                     rays aimed through their cells (only field-strip cells get rays): cover = terrain
                     (terrain hits >= objects) | objects (entities + entity-less bodies: rocks,
                     buildings) | unknown (no ray reached it or none hit); cover=objects src=density =
                     a density area vetoed by its entity hits; cover=difficult src=map = a map
                     DifficultTerrain region (its land cells within radius m); cover=steep = walkable
                     ground steeper than 25 deg >= 1500 m2, slope = their mean degrees. imp = area / (1 +
                     axisDist / 200 m)
          water  = 8-connected deep water >= 1000 m2: touchesField = a cell between the armies, crosses
                     = the straight line A->D runs through it, crossAt = first crossing (fraction);
                     imp = area / (1 + axisDist / 200 m), x 10 when it crosses
          ford   = 8-connected wet walkable cells on the grid path A->D, or touching deep water and
                     >= 100 m2 (a wet bank strip counts too): onPath, atDeep, water = the listed water it
                     touches; pos = its middle path cell when on the path; imp x 10 on the path
```

## Terrain tactics

Terrain tactics (TerrainTacticFactors). A terrain factor t per tactic and side: t = (raw = product of named terms) ^
TerrainWeight 2, clamped 0.35..2.5 (shown as t=F(rawR)), computed and published once right after the TERRAIN lines in
every field battle. With terrainTacticsEnabled (applied=1) it is IN the AI's choice: the final weight =
GetTacticWeight through RBM's patches = composition fit x TacticSelection's culture/commander/jitter multiplier x t,
and every team is marked for one terrain re-pick at its next decision (<= 5 s; `# terrain tactic factors published
...: terrain re-pick ... due for N team(s)`; it runs whatever RBM's 30 s TacticDwell holds). With the toggle off (applied=0) the
lines are a prediction only: weight x t. Terrain is never multiplied twice: applied, the evaluated weight already holds
t and the no-terrain score is w / t. The weights are re-read on each line, the factors fixed. Native's x1.5 bonus for
the current tactic and the dwell hold are not modelled, so agree=0 can mean the AI is holding its tactic, not that it
would pick another. Teams with team AI only; team = Side#index.

```
REPICK t  REPICK  team  old -> best  w=old/best  ratio  switched   -- the one terrain re-pick (applied=1):
       at each team's first decision after the publish, the best weight WITHOUT native's x1.5 bonus for
       the current tactic replaces the current tactic when ratio >= 1.1 (TerrainRepickMargin); then the
       30 s dwell holds it and native's usual rules apply. switched=0 = it kept its tactic, re-applied
       only when flankReapply=1: the flank plan below changes one of the side choices it made before
       the ground was read (native IsTacticReapplyNeeded; the split infantry tactics also re-split).
       reason=terrain for it; reason=balance switched|kept|blocked(<line fighting>) <shift> = a balance
       re-pick (Reactions R6 below): the same evaluation at the team's next decision, margin 1.1
       (BalanceRepickMargin), blocked = a defensive tactic whose line fights (choke / hedgehog in contact
       or charging, a battle-position line engaged, the main infantry >= 10% in melee) and no huge shift;
       reason=chokeBypassed switched|kept <why> = the choke hold was bypassed (CHOKEHOLD bypassed): the
       same evaluation at the next decision, margin 1.1, never blocked (the choke weighs 0.01 from then)
```

## Flank plan

Flank plan (TerrainFlankPlan; read by the tactics only with terrainTacticsEnabled, applied=1). Sides are each army's
OWN left/right facing the enemy (as BehaviorProtectFlank's; the defender's own left is the attacker's right).

```
FLANKPLAN t  FLANKPLAN  side  applied  ownLeft  ownRight  attackSide  reason  in   (once per side at the
       publish) ownLeft/ownRight = covered|open(end why): covered = its line's end there is water /
       blocked / steep with spare <= 1 x own frontage (edge: <= 0.5 x) AND the enemy has no route round
       it (noRoute) or one >= 1.5 x its route round the other side (longRoute xR); open whys: far =
       more room than that, routeOpen = enemy route round it not long, max/none = the walk hit no
       obstacle. attackSide = own side to turn the enemy's flank on: the enemy flank that is not covered
       and that we have a reachable route round, shorter route time (reason onlyLeft/onlyRight/shorter;
       none = tie within 10% or noOpenRoute: the tactic keeps its own choice). in = frontage m, then per
       line end [end dist spare m, enemyRoute (round us) / ourRoute (round them): -=not measured,
       0=unreachable, Ns]; v=N(note) = plan version (1 = publish; + 1 per replan / stale / threatened)
FLANKPLAN t  FLANKPLAN  replan  side  (FLANKPLAN fields)  reason  ms   (main thread) a side's plan is
       stale once the ENEMY's main formation (largest infantry) moved > 100 m since its plan: both sides
       are planned again from the armies' centres now (the read's line-end walk and flank routes on a
       background task, the read's frontages and paces), <= 1 per 20 s, >= 20 s after the publish, <= 10
       per battle; also on a TIMER: 30 s after the last plan when the enemy moved > 20 m or turned > 10 deg;
       reason = n=K/10 gap=armies' centres apart m, side(enemy [timer] moved M m turned D deg);
       threatened sides stay latched, a dropped attack side comes back. failed=why when it could not run
FLANKPLAN t  FLANKPLAN  stale  side  attackSide=none(was ...)  why  v   the side's attackSide dropped
       until the next re-plan: turned D deg (the enemy's main formation turned > 30 deg: the plan measures line
       ends across the axis only), or moved M m + cap 10 / close gap=N m (< 150 m) / noCentre (no
       re-plan possible). The tactics keep the side their formations already have
FLANKWATCH t  FLANKWATCH  side  ownLeft|ownRight  threatened  enemy  was  v   (main thread, once per
       side) a covered own side stops being trusted: a SIGNIFICANT enemy cavalry / horse archer formation
       (relative to our main infantry) beyond its half width on that side, <= 250 m from that line end,
       closing on it or riding toward our rear at >= 1.5 m/s (own 1 s position samples) on 2 checks in a
       row. enemy = fN:Class men power vs ours, lat = metres out from our centre, dist = to the line end,
       closing m/s. The side counts as open from then on (a guard is kept / the cavalry is not moved off it)
FLANKUSE t  FLANKUSE  team  tactic  decision  reapply   (AI thread, queued; once per tactic instance
       and decision): guardLeft/Right=skipped(covered ...) = no infantry guard there at the split, its
       share stays in the main line; =joinsMain(...) = guard formation kept (men not movable), main
       line's behaviors; cavalry Left->Right(covered ...|attackSide ...) [lone] = a cavalry flank guard
       moved (lone = the only cavalry formation, sent to attackSide); skirmishSide=left|right(plan ...)
       = the forward skirmishers' side (random before). reapply=1 = the re-pick re-applied this tactic
       (REPICK flankReapply=1) before this use. Archer flank groups are never moved. followPlan
       vA->vB(note) re-applied = a newer plan version changed one of the current tactic's side choices
       (compared with the version it made them with; a dropped attackSide alone never re-applies)
```

SIGNIFICANT enemy (ThreatSignificance, below: the spots' release, the late charge's infantry / behind / advantage /
broken / cavalryGone foot and recall foot gap, HoldPosition's enemyNear, ReservePlug's selfThreat / flankTurn /
contact): an enemy formation with >= max(10, 20% of the reacting formation's men) men OR >= 25% of its FormationPower;
smaller (bait) groups never set these off. Logged as fN:Class men=X power=P vs men=Y power=Q (ours after vs).

## Archer spots

Archer spots (TerrainSpots; scored on the background task by BattleTerrainRead.Spots.cs, used by the archer behaviors
only with terrainTacticsEnabled, applied=1).

```
SPOTS t  SPOTS  side  applied  lineFire  cand  rej  rays  paths  ms  spots  usable  [dropped]   (once
       per side at the publish) candidates: the feature map's hills/crests and a 20 m lattice, each
       point moved to its highest roomy cell within 8 m, <= 250 m from the side's centre and <= 60 m
       past its own line; room = walkable, not wet, slope <= 15 deg, clearance >= 12 m, not in the woods
       mask. fire = share of
       the enemy's approach (12 m lattice of the field strip between the lines, 20-180 m in front,
       <= 48 sight lines, 1.7 m eye to 1 m above ground) seen over the terrain AND not through >= 8 m
       of no-navmesh cells in a row (rocks/buildings have no grid height: an approximation) nor >= 12 m
       of woods-mask cells in a row.
       lineFire = the same from the side's own centre. score = fire + 0.5 x min(gain,12)/12 - 0.2 x
       distLine/250; kept when >= lineFire + 0.1 (clearView also fire >= 0.6), 40 m apart, best first,
       reach = own/enemy grid A* walk s (own <= 0.6 x enemy). rej = room/woods/edge (within 60 m of the map
       edge, EdgeMargin)/region/targets/score/sep/reach
       counts; rays/paths = sight lines / A* searches. Per spot: id[kind source pos gain(m above the
       side's mean height) fire/targets side(own left/right/middle) distLine along across clear slope
       reach score why(height|view|both)]; usable/dropped = after the main thread's boundary and
       navmesh check; v=N(note) = plan version (1 = publish, + 1 per replan)
SPOTS t  SPOTS  replan  side  reason  wallMs  pruned  (SPOTS fields)   (main thread) both sides' spots
       found again from where the armies stand NOW (same rules, round each side's current centre, fire
       toward the enemy's current centre, reach from both current centres; the read's frontages, paces,
       hills/crests and woods mask) on a background task, published as the next version. Due 30 s after
       the last plan when an army's centre moved > 40 m or a main formation (largest infantry) turned
       > 15 deg, or 20 s after it when a centre moved > 100 m; <= 10 a battle. reason = timer|moved, atk /
       def moved m, turned deg, gap = centres apart m, after = s since the last plan, n=K/10, across
       atk/def = line (lanes spread along the main formation's line) | axis. wallMs = gather to publish;
       pruned = claims dropped on old spots nobody holds / of formations no longer on the behavior.
       failed=why kept vN = that side could not be planned, its version stays. The DEFENDER's spots are
       re-planned only when its OWN army moved > 40 m / its main formation turned > 15 deg since its own
       last plan: "(own moved Xm turned Ydeg)", else "kept vN (own moved ...)" (its version stays)
SPOTS t  SPOTS  replanStop  melee gap Nm<120m | melee atk|def main S in melee | cap 10   no more
       re-plans this battle of the archer spots AND the cavalry ground (one job, TerrainArmyReplan, one
       line for both; the lines are meeting: centres < 120 m apart or a main infantry >= 10% in melee;
       or the cap). The spots' and the cavalry's replan lines of one job carry the same reason / wallMs;
       a failed job writes SPOTS replan failed= (and CAVALRY replan failed= while the cavalry ground has
       a plan)
Gaps (gap=, foot=, allyGap=, ... in BEHAVIOR / REACT lines) are FormationGeometry's footprint edge gaps (0 = overlapping),
       written Xm, "none" without one.
BEHAVIOR t  BEHAVIOR  team  formation  ArcherFlank|ArcherSkirmish  cover take|change|step|engaged|disengaged|
       release|none|stay  spot=cover  why   ArcherCover (DEFENDER archers take cover behind / beside friendly
       infantry from a closing significant enemy melee FOOT formation: <= 35 m or closing within 70 m;
       gaps by the archers' size in Line; a cover
       point <= 45 m away and >= 15 m farther from the threat, else noGain; fallback one 40 m step).
       stay = not taken, the archers stand at a post: post (HoldPosition / HoldChoke archer place reached)
       | behind fN gap=Gm (<= 25 m behind its rear) | beside fN gap=Gm (<= 25 m beyond its end), once per
       threat, and only while the post IS covered: cover infantry within 60 m, not charging, not walking
       away > 1 m/s, the threat in front of it; otherwise take / none end with "post uncovered(fN charging
       | fN leaving X m/s | fN threat behind it | no line within 60m)". why = "<cover> <behind|behindLeft|
       behindRight|beside|fallback> threat=<f> gap=<m> closing=<m/s> d=<walk m>"; change adds the reason
       (threatGone / threatCalm / nearer / coverGone / the failed check); release = threatGone dead |
       threatGone gap=..m calm=..s | coverGone .. | behaviorEnded | charge | notDefender | notRanged |
       noTerrain, + held=..s; none = the threat + refusals cover/kind:far|edge|ground|toward|path|noGain|
       overlap(f)|enemy(f), fallback=i:reason. HoldPosition / HoldChoke write the same lines with point=cover
       for their Archers role (release also assigned | noPlan | noPlace). A charging line is no
       cover / screen; post uncovered(fN archers Xm ahead of it) = the archers stand before its front;
       fallback: arrived and the threat still close / coming in = another step (<= 3 per take), else
       release stepDone gap closing steps. STAND vs MOUNTED (USER RULE 2026-10-22, replaces the cavalry
       threat above and the attacker archers' cover from cavalry): enemy cavalry / horse archers never
       start a cover or a fallback - threats are enemy FOOT only, defender archers only; instead
       'stand vsMounted  spot=cover  fN:Class men=.. gap=Gm closing=C m/s melee=0|1' once per rider
       formation that would have been a cavalry threat (<= 60 m, or charging >= 6 m/s within 90 m),
       both sides. SupportFire ignores riders in its safety gaps / out-of-range re-pick; ArcherSkirmish
       never pulls back from a mounted target; a held spot is released by enemyClose cavalry / horse
       archers only with the archers >= 10% in melee
BEHAVIOR t  BEHAVIOR  team  formation  behavior  take|refuse|release  spot  why   (AI thread, queued)
       take = the formation moves to the spot (ArcherFlank: own spot on its flank side <= 120 m from its
       place beside the infantry; ArcherSkirmish: while shooting, <= 120 m from its hold position,
       fire >= 0.6, enemy within 0.9 x its effective range of it); both need no enemy formation within
       80 m and the infantry within 200 m. refuse = no spot fits, per spot id:why (otherSide, far,
       lowFire, claimed, enemyNear, allyFar, outOfRange), once per formation and set of reasons (digits
       ignored), at most one per formation per 10 s. release = left
       it (enemyClose fN ... d=M m: a SIGNIFICANT enemy < 50 m, allyAway > 200 m, outOfRange > 1.1 x range, inactive, behaviorEnded,
       pullingBack/approaching, charge, noAlly, noPlan, empty), held = seconds on it; no new spot for
       20 s after a release. ATTACKER: release why=armyAdvancing <reason> (no retake wait, no take while
       it holds + 15 s: latched Ns) = its main infantry advances: order=Charge|ChargeWithTarget, an AI
       main's BehaviorAdvance|BehaviorCautiousAdvance|BehaviorCharge, or a PLAYER-led main's
       playerLine order=Advance | playerLine speed=S m/s (>= 1 m/s toward the enemy in 2 own 1 s samples)
       | playerLine orderAhead=Mm (its order position > 30 m ahead of its front).
       One formation per spot per team. After a replan: rebase = the claim moved to
       the new version's spot nearest its old one (<= 40 m, the take rules; was = old id, shift m);
       keepOld why=replan vN noSpot<=40m = none fits: the formation KEEPS its old spot while every keep
       rule passes (release why=<keep rule> (old spot, vN), 20 s retake wait).
       FRIENDLY OVERLAP (FriendlyOverlap; friends = the side's other formations where they stand AND where
       they are heading, cached order position = front-centre, ranks behind it): refuse spot:
       friendOverlap(fN:Class,S) = more than 0.3 of the archers' footprint at the spot (front-centre,
       facing the enemy; 5 x 3 samples) would overlap friend fN (also no rebase onto it); release
       why=friendOverlap fN:Class share=S = more than 0.5 overlaps now (20 s retake wait)
       COHESION (FormationCohesion: AI FOOT formations stay near the team's MAIN infantry = its largest
       melee infantry, AI- OR PLAYER-led (a new one takes over only at 1.2 x the men; none while it is
       under Charge / Retreat), measured as the edge gap between the footprints - front-centre rule - to the
       nearer of where it stands and where it is heading; limits: defender ranged 40 m, defender melee
       30 m, attacker ranged 70 m, attacker melee 50 m, the defender's reserve plug 80 m to its line;
       never mounted / player / charging / retreating / in melee formations): refuse spot:
       cohesion(fN:Class:G>Lm) = the archers at the spot would be G m from main fN (also no rebase onto
       it); release why=cohesion fN:Class gap=Gm>Lm = beyond the limit + 15 m now (20 s retake wait).
BEHAVIOR t  BEHAVIOR  team  formation  ArcherFlank|ArcherSkirmish|ProtectFlank  cohesion  cohesion|guard
       why   (AI thread, queued) the formation's own target (ArcherFlank: its place beside the infantry;
       ArcherSkirmish: its hold / approach / pull-back point, not a spot or a cover point; ProtectFlank:
       an infantry flank guard's place, point=guard) moved along the line toward the main infantry:
       why = pull Nm fN:Class gap=Gm>Lm->Am [overlap] (moved N m, gap now A m = 3 m inside the limit, on
       walkable ground >= 10 m off the edge; overlap = only a point overlapping a friend's destination
       was walkable) | unpull fN:Class gap=Gm<=Lm (or the reason: player|mounted|noAnchor|noTerrain) |
       noPull fN:Class gap=Gm>Lm ground|noLine. Cached: decided again when the own target moves > 5 m, its
       facing ~10 deg, or the main infantry's order position / centre > 10 m (width / depth > 5 m); a
       pull already in force is logged again only when it moved > 10 m. HoldPosition / HoldChoke
       (archer) and ReservePlug (plug) write the same cohesion lines; ArcherCover's fallback step is
       refused as fallback=i:cohesion(fN:Class:G>Lm) when it would leave the limit (and go farther)
```

## Cavalry ground

Cavalry ground (TerrainCavalry; scored on the background task by BattleTerrainRead.Cavalry.cs, used by the cavalry
behaviors only with terrainTacticsEnabled, applied=1). Each side's points are for ITS riders against the OTHER army.

```
CAVALRY t  CAVALRY  side  applied  badField  badMap  lanesTried  reformCand  laneEdge  ms  lanes  reforms
       usable  [dropped]   (once per side at the publish). bad ground (mask) = >= 50% of the cells
       within 8 m not walkable / wet / steeper than 25 deg / clearance < 6 m, or in the woods mask;
       badField/badMap = its share
       of the field strip / map. lanes: the run-up walker (walkable, < 15 deg, bumps <= 8 m, <= 250 m)
       from 5 points across the enemy line (centre, +-1/4, +-1/2 frontage) back toward the side, >= 60 m;
       score = len/250 + 0.3 x clamp(drop/10,-1,1) - 0.1 x bumps - 0.5 x wet share - 0.5 x bad share;
       best 3, aims >= 20 m apart. Per lane id[start aim off(fraction of the enemy frontage) len end bumps
       drop(start - aim height, + = downhill) wet bad(m) entries(run-up points every 50 m down to 100 m
       from the aim) score]. reforms: 24 m lattice, room = walkable, not bad, not wet, slope <= 15 deg,
       clearance >= 40 m; beyond/beside = 100-280 m from the enemy centre, not > 40 m in front of its line
       (beyond = >= 40 m behind it); home = >= 30 m behind the own line, <= 200 m from its centre; score =
       min(clear,80)/80 - 0.3 x distance term; best 3 + 2, 80 m apart. No orbit centre (2026-10-23).
       usable/dropped = after the boundary/navmesh check.
       Map edge (60 m, EdgeMargin): reformCand .../edge = reform cells rejected there;
       laneEdge=trimmed:N/dropped:M = lane starts pulled back along the lane from
       within it / of those left < 60 m (also in short); run-up points there are left out; v=N(note) =
       plan version (1 = publish, + 1 per replan)
CAVALRY t  CAVALRY  replan  side  reason  wallMs  pruned  (CAVALRY fields)   (main thread) both sides'
       lanes / reform points found again from where the armies stand NOW (same rules; the lane
       aims spread along each enemy main formation's current line when it faces within 60 deg of the
       axis, else across the axis; the read's frontages, paces and bad-ground mask) on a background task,
       published as the next version. The SAME job as SPOTS replan (one frame, one timer, cap and stop:
       the SPOTS replanStop line), run only while the cavalry ground has a plan for a side. A re-plan
       with no earlier plan publishes v=2 (as the spots)
BEHAVIOR t  BEHAVIOR  team  formation  behavior  what  point  why   (AI thread, queued) what =
       takeLane/releaseLane (CavalryCharge rides to a lane's run-up point, then charges: why=arrived,
       timeout 15 s, melee, enemyClose < 60 m, targetMoved, stale ..., inactive, newCharge, behaviorEnded),
       takeReform/releaseReform (its reform point instead of the raw one), target (the terrain changed
       the charge target: pick=..(note) instead of ..(note)), badGroundTarget (a candidate on bad ground,
       score x3), refuseLane/refuseReform (per point id:why). target/refuse
       lines once per formation and text; held = seconds on it. One formation per lane / reform point
       per team. USE-TIME CHECKS (the points are read at deployment): stale aim Gm>Lm = the lane's
       aim is G m from the target's CURRENT front (centre + facing x depth/2), more than L = 40 m + half
       its width; stale angle Ddeg>30deg = the lane's direction is D deg off the bearing from its run-up
       point to the target's centre; checked at the take (refuse ...:stale(...)) and every tick
       of the approach (release why=stale ...), then the normal charge.
       occupied fN:Class men power vs ours d=M m = a SIGNIFICANT enemy formation (relative to the riders)
       within 60 m of a reform point (refused at the take; released while riding to it, then a new reform
       point or the raw one). After a CAVALRY replan:
       rebaseLane = a lane approach moved to the new lane onto the same target whose run-up point is
       nearest its old one (<= 60 m, the take rules; was = old id, shift m), releaseLane why=replan
       noLane(vN) = none, it charges from where it is;
       releaseReform why=replan vN = let go and chosen again at once from the new
       version; why=replan inactive = dropped at the publish (the formation left that behavior).
       FRIENDLY OVERLAP (as SPOTS): refuseLane/refuseReform ...:friendOverlap(fN:Class,S)
       = more than 0.3 of the riders' footprint at the point (front-centre, facing the target)
       overlaps friend fN (a lane rebase skips such run-up points too); releaseLane/releaseReform
       why=friendOverlap fN:Class share=S = more than 0.5 while approaching / riding.
FORMSHAPE t  FORMSHAPE  team  formation  behavior  what  why  values   (AI thread, queued; TerrainShape)
       advancing foot (Advance/CautiousAdvance): what = loose | unloose (woods/rough/steep share of its
       ground + 60 m ahead >= 0.35 / < 0.2; a shield wall is never touched) | narrow | renarrow | widen |
       release (min walkable width 0-60 m ahead < 1.1 x its normal width -> 0.9 x that width, >= 12 men;
       back at >= 1.3 x); values = share woods minPass at normal narrow width depth men arr
BEHAVIOR t  BEHAVIOR  team  formation  CavalryCharge  chaseTimeout  why = fN Ns dist A->B excluded Ns (xK)
       [footOnly 60s]: the target's exclusion doubles per repeat (20/40/80 s); a second timeout within 60 s
       picks foot targets only for 60 s (gap audit C6). Never while the target is <= 80 m (edge gap) or our
       dev > 3 x idealDisp (the window restarts).
BEHAVIOR t  BEHAVIOR  team  formation  CavalryCharge  charge advance|charge go  why (FORMED APPROACH, USER RULE 2026-10-30):
       an AI cavalry charge (Charging, off a lane) whose target is beyond 35 m edge gap (FootprintMode.Current) rides
       a Move in Line to the target's near edge (led by its sampled velocity, <= 2 s, never back toward us), facing it,
       at full pace. advance why = "fN -> fM:Class gap=Gm[ keepLost]" (keepLost = a go whose gap stayed > 110 m for 2 s
       with no melee / contact). go why = "fM:Class gap=Gm | melee gap=Gm (own riders >= 0.2 in melee) | clash closing=C gap=Gm (mounted target, closing m/s x 4 s >= gap)
       | noPoint after Ns" = the ChargeToTarget starts; the through / contact timers start only here. Logged only when
       a go ends an advance (a charge already within 35 m goes without lines).
BEHAVIOR t  BEHAVIOR  team  formation  CavalryCharge  reform take|change|release|none  why = kind=through|left|
       right|back gap=Gm(enemy fN:Class) ride=Rm overlap=S terrain=slopeD|bad,slopeD|- cost=C [was=kind(why)]: the reform
       point picked from the live situation (CavalryReform; all shapes compact = FootprintMode.Current, 2026-10-30):
       through 80/110/140/170/200 m beyond the target's far edge, left/right 130/180 m off the charge line, back 40 m
       behind our position anchor (main infantry, else the foot anchor, e.g. archers); rejected with a significant
       enemy <= 40 m edge gap (enemy), friend overlap > 0.3 (kept to 0.5), within 30 m of the map edge / not
       walkable (ground), water (water), slope > 35 deg (steep), ride > min(150 m, 19 s x 0.75 x top speed) at the
       take, back points > 250 m (ride); bad ground / woods only costs (terrain=bad,...; USER RULE 2026-10-30);
       re-checked every 2 s, changed
       when unsafe or beaten by 25% (<= 1 / 4 s), a change only to the same kind within 60 m (kind / shift);
       none = the rejects per kind, the reform is skipped (charge again); release why =
       newCharge(enemyClose|timeout|ready|underFire|targetGone|- dev=D/I [held=Ns])|undetermined|
       behaviorEnded|none(...)|noTarget (dev / idealDisp of the formation, held = seconds in Reforming)
BEHAVIOR t  BEHAVIOR  team  formation  ArcherSkirmish  counter take|change|end  counter  fN:Class d=M
       hits=R: while our archers are hit (UnderRangedAttackRatio >= 0.05, held 10 s) the nearest significant
       enemy foot ranged formation within 1.3 x the shooting range is their target (gap audit R7); charge  -
       enemyClose noInfantry (no melee-infantry main: charges when the enemy is within 0.4 x range, R3).
BEHAVIOR t  BEHAVIOR  team  formation  ArcherSkirmish  initiative|initiative end  -  why (INITIATIVE, no
       melee-infantry main, 2026-10-29): initiative = 'dry ammo=A<0.20' (ArmyComposition dry state: under 20%
       of its archer men with ammunition; was the 0.5 noAmmo charge) | 'idle shooting=R for Ns d=D/Rm enemy=fN supportExhausted' (a significant enemy
       within the shooting reach, MakingRangedAttackRatio < 0.05 for 20 s after SupportFire's last pick or
       ladder ended in none) | 'ineffectiveFire lost=L/M in Ns d=D/Rm enemy=fN' (attacker only, no melee-infantry main,
       not weaker than the enemy by weighted strength: shooting a significant enemy foot formation in reach that lost under 5 %
       of its men over 60 s) + target=fN team=take|held <composition shares> (the same text as its COMPEVENT role
       what=initiative, one call writes both): the archers ChargeToTarget the
       nearest significant enemy (ChargeGuard walks it in) for the rest of the behavior's life and the team's
       mounted ProtectFlank guards (AI-controlled only) charge with them; initiative end  -  mainInfantry fN =
       the team has a melee-infantry main again. Only the ACTIVE behavior of an AI formation latches it, never
       a token archer force or an ArmyScale NoPlans side (dry archers then charge on their own: COMPEVENT
       what=initiative suppressed); kCavPlace  -  'behindMain on kCav=K>=0.50 behind fN' | 'behindMain off
       kCav=K<=0.35 | mainLost' (the kCav place choice; ArcherFlank writes the same with point=flank). The enemyClose charge is never made at a mounted target.
BEHAVIOR t  BEHAVIOR  team  formation  AttackSplitInfantry|FullScaleAttack  refreshOnly  formations
       battleJoined|interval Ns: a formation-count change after the battle is joined or within 30 s of the
       last split only refreshes the tactic's references (a stray formation's men join the one of their
       kind), no re-deal of the army (gap audit X3).
BEHAVIOR t  BEHAVIOR  team  formation  ChargeGuard  charge advance|charge go|charge drop  spot=target
       why (CHARGE START <= 40 m, USER RULE 2026-10-22, 50 m until 2026-10-30, Orders/ChargeGuard.cs): an AI foot formation's
       Charge / ChargeToTarget with its target (edge gap along the line between the centres) beyond 40 m,
       under 10% in melee and no significant enemy within 20 m is walked as a Move in formation to 15 m
       short of the target's edge: advance why = "fN -> fM:Class gap=Gm by=<behavior>[ keepLost]"
       (keepLost = a released charge whose gap stayed > 75 m for 3 s). go why = "gap=Gm | foot fK gap=Gm
       (a significant enemy foot formation within 40 m) | attacked fK gap=Gm (any within 20 m) | melee=S
       after Ns" = the real charge starts. drop why = "order=X after Ns": the behavior gave another order.
       melee go (2026-10-23): "melee=main S all=A far=N": S = share in melee of the MAIN BODY only (men within
       40 m + depth of the men's median along the facing; N men left out), A = the whole formation's share.
BEHAVIOR t  BEHAVIOR  team  formation  ArcherSkirmish|ArcherFlank|HoldPosition|HoldChoke  support
       take|keep|move|rotate|shift|lost|none  support  why (SUPPORT FIRE, SupportFire.cs, 2026-10-22): once the
       frontlines connect (own main infantry >= 10% in melee with a significant enemy within 60 m, or under
       Charge with one within 30 m; held 8 s) AI foot ranged take ONE place and fire: take = stand (where they
       are: not ahead of the main, engaged enemy within 0.9 x reach, safe, within cohesion, shooting or a clear
       grid sight line) | behind (front 20 m behind the main's rear) | left|right | besideLeft|besideRight |
       spot sN | behindRelaxed | standNoPlace (nothing passed: the behind point on good safe ground, else stand
       unless ahead / unsafe); 'd=Nm enemy=fN range=A/Bm main=fN point=x,y' + here=<why the stand failed> +
       the connection (melee|charge fN gap=Nm). keep = 'KIND shooting=R' once shooting at the place, or a
       re-pick that stays. move = re-pick (outOfRange > 1.1 x reach for 5 s | mainMoved > 40 m | ahead |
       friendOverlap | ground; <= 1 / 15 s except the last three) or 'noVision(shooting=R Ns) search ...'.
       No vision = arrived, 10 s grace over, ammo >= 30%, enemy in reach, MakingRangedAttackRatio < 0.05 for
       8 s -> rotate (face the enemy concentration when > 25 deg off) -> shift (side / beside candidate with a
       clear sight line) -> move ... search (10 m lattice round the behind point, sight + higher ground) ->
       none 'search none ...' (they stay; ladder restarts after 30 s). lost = notConnected|notRanged|postOk|
       behaviorEnded|charge|noPlace ... (the behavior's own order again). HoldPosition / HoldChoke Archers keep
       their place while it serves (postOk). Map label 'ArcherSkirmish(support behind)', 'HoldPosition(archers
       support behind)'. While supporting the engaged main is the cohesion anchor under Charge too and counts
       as ArcherCover's cover (archers behind it relative to the threat); cover fallback steps bend toward the
       support place and the archers walk back to it afterwards. The main is the behavior's position anchor
       (ArmyComposition.AnchorFor: the main infantry, else the foot anchor); beside an ARCHER anchor the
       connection reads 'archerAnchor fN d=D/Rm' (the nearest significant enemy within 0.9 x reach of the
       anchor to take, 1.1 x to keep), places laid out round the anchor as for a main. ALONE (the formation
       is the anchor, or there is none, 2026-10-29): main = the archers themselves; the connection reads
       'alone fN d=D/Rm'; behind / sides / search are laid out round the archers, no cohesion test, no
       mainMoved / outOfRange / ahead re-pick.
```

## Hold the choke

Hold the choke (TerrainChokeHold, the defender's narrow pass for RBMTacticHoldChoke / RBMBehaviorHoldChoke; scored on
the background task by BattleTerrainRead.ChokeHold.cs, used only with terrainTacticsEnabled).

```
CHOKEHOLD t  CHOKEHOLD  Defender  applied  infantry/front  choke  hold  reach  archer  reserve  score  plan
       why  paths  ms   (once at the publish). infantry = foot men - ranged, front = them 4 deep at 1.1 m
       (>= 20 m). A FEATURE choke qualifies when: not both armies on one side of it (sameSide: a bridge off to the
       side stays a feature, never held); at in [-0.1, 1.1] and within max(30 m, width/2 + 10) of the A->D grid path
       or between the armies <= 80 m off the axis or a CROSSING (sides=opposite: the attacker comes through it) (notBetween /
       offPath); its narrowest line (walkable width every 10 deg) exists; not a crossing: its gap runs along the axis,
       the attacker >= 40 m in front of the line, the defender <= 30 m (sideways / attackerThrough / defenderBeyond;
       a crossing faces its attacker-side mouth instead); line <= 1.3 x front (wide) and >= 20 m (tooNarrow, except
       a crossing); mode gap = a hold point 20 m behind the line (then 15 25 10 30 5 35), walkable, dry, not near the
       edge, centred across the gap, span <= 1.6 x front (noHoldGround); else, a crossing, mode mouth = the defender's
       mouth of it + 10 m outward (then 15 5 20 30 0 40), walkable, dry, off the edge, the line across the exit facing
       into the passage, width = min(front x 0.95, max(20, gap + 24 m)), span = the gap, flanksCovered only when it is
       the only way across (separates) and both walls closed (noMouth / noMouthGround); score - 0.3 for a crossing that
       does not separate the armies; hold adds mode=gap|mouth crossing=0|1; <= 300 m from the defender's centre (far); the defender walks there
       >= 20 s before the attacker reaches the line, grid A* (noRoute / late). why = per choke
       id:ok(score) or the first failed test; score = min(front/line, 2) - 0.5 x dDef/300, best kept.
       choke = id, line width (feature width), line / front, line / attacker frontage, the point; hold
       = x,y, back, span (left/right end), width = span x 0.95 (the infantry's custom width),
       flanksCovered (both ends closed and span <= 1.3 x front: the cavalry waits in reserve), facing
       (toward the attacker through the gap), dDef; reach = A* seconds; archer = the defender's archer
       spot <= 120 m from the hold, not in front of it (id) or the highest cell <= 20 m round the
       point 35 m behind it (behind), gain = m above the hold; reserve = 60 m behind; dropped = no
       navmesh / outside at the publish; plan = published | none(why); the tactic weighs 0.01 without it
       OUTNUMBERED (2026-10-23): infantry= also carries outnumbered=r k=K(src) maxDist reach cavReach: r =
       attacker / defender strength (src scalePower|scaleMen = ArmyScale's latch incl. reinforcements, field* =
       the men on the field), k = clamp((r - 1) / 1.5, 0, 1); far = 300 -> 450 m and late = 20 -> 12 s by k; a
       margin below 20 s also needs the defender there before the attacker's riders when >= 35% of it is
       mounted (cavReach=1); the tactic's choke term 1.3 -> 1.6 by k (PREDICT in= outnumbered= k=)
CHOKEHOLD t  CHOKEHOLD  bypassed  team  why   (AI thread, queued; once) the hold is dropped for the battle:
       the enemy's concentration (power x class weight of its significant formations within 400 m of the
       main infantry) or its nearest significant melee foot (<= 200 m of the hold) behind the hold line
       (along < -10 m) or, with the flanks open, beside it (<= 300 m, > 60 deg off the facing) for 8 s,
       the line out of melee and not charging: behind|beside|footBehind|footBeside along dist off ...
       for Ns melee flanksCovered; then REPICK reason=chokeBypassed and BATTLEPOS repick chokeBypassed.
       Gap fix 2026-10-22: enemy FOOT only (riders never bypass); footBehind|footBeside also needs the group
       to carry >= 40% of the foot concentration's weight (share=S)
BEHAVIOR t  BEHAVIOR  team  formation  HoldChoke  what  point  why   (AI thread, queued) what =
       Wait->Moving (toPoint d) / Moving->Holding (arrived after Ns, width=W/gap G m: the line's
       custom width, 3 ranks at least, vs the hold width) / Holding->Moving (offPoint) / ->Wait
       (noPlan / noPlace); point = hold (infantry) | archer | reserve (cavalry); shieldWall / line =
       the infantry's arrangement at the point (shield-wall eligible share, under-fire ratio; never
       while in contact, never for a ranged formation); LATE CHARGE (the line, HoldLateCharge):
       Holding->Contact (melee=share of the line hit / hitting in 6 s >= 0.1 | enemyNear gap <= 15 m,
       enemy FOOT only - cavalry never starts contact; power = sqrt((ours+1)/(theirs within 30 m+1)),
       cavalry counted): it fights in place, same Move order and width / Contact->Holding (contactLost:
       no melee, no enemy foot within 30 m for 10 s) / ->Charge (latched):
       infantry gap fN (SIGNIFICANT enemy FOOT within 15 m: it waits for cavalry, charges infantry;
       a smaller group only starts contact) | allyFighting fN melee=S vs fM ... allyGap enemyGap (a
       friendly formation <= 60 m of the line >= 10% in melee with enemy foot SIGNIFICANT to the line
       within 30 m of it; local, recalled like infantry) |
       lineGone (< 25% of its most men or < 20) | pushedBack (> 30 m behind the hold point, once held)
       | behind fN ... by Pm gap=Gm (a SIGNIFICANT enemy formation >= 10 men, > 20 m past the line's rear
       plane, once held; enemy foot RANGED only within 30 m edge gap; the formation a local behind /
       infantry charge was recalled from not again for 60 s unless within 15 m)
       | outshot (no contact, at its point: enemy ranged power (ranged + HA FormationPower) >= 2.5 x
       ours and >= 30 missile men, hits landing (>= 5% of the line wounded by missiles in 10 s, shield
       blocks never count; a 15 s gap restarts), >= 60 s and the line lost >= 8% (>= 10 men) since it
       began, no enemy melee foot or cavalry/HA within 150 m; rangedPower/missiles/hit/lost/fighters)
       | advantage (in contact with SIGNIFICANT enemy foot; the power ratio still counts every enemy
       within 30 m: power >= 1.6 for 8 s) | broken (in contact: the SIGNIFICANT enemy FOOT formations met
       lost >= 40% of their most men, a Retreat
       order = all); men = the line's. Hold->Engage (formation = the main infantry, - without) = the
       tactic's engage: lineCharged <reason>; without a plan / line the old rule: battleJoined (75 m
       edge gap / 35% melee / no significant enemy or infantry) | melee (20%); Engage->Hold recall
       <reason>: an infantry / allyFighting / advantage / broken / behind / pushedBack charge goes back to the hold
       (only while the hold is within 150 m) after >= 15 s once no SIGNIFICANT enemy foot formation
       (>= 5 men) has been within 60 m for 10 s, or at once as recall leash <reason> charge: N m from
       its start (> 40 m from where the charge started, < 10% of the line in melee, no significant
       enemy foot within 30 m); the quiet 25 s after a recall still lets allyFighting through; other
       engages are latched until the tactic is applied again. reface / unface (point = hold): the line
       turned in place toward the enemy's concentration (HoldReface: holding, out of melee, > 35 deg off
       for 5 s, <= 1 turn / 10 s, only within 60 deg of the gap's facing): facing a->b turn plan+ dist
       melee conc=x,y n main w; the archers and the cavalry reserve face the same way, and their places
       turn with the line: turnPlace / unturnPlace (point = archer | reserve) as HoldPosition's (the place
       turned round the line's pivot, or back; offGround = not walkable / near the edge).
       shift / unshift / noShift (point = archer |
       reserve) = the place moved ONCE along the facing clear of a friend heading there:
       shift back|forward Mm friendOverlap fN:Class share=S->S2 (S > 0.3 of its footprint over where
       fN is heading: its cached order position as front-centre, ranks behind; cleared by 5 m, <= 80 m,
       walkable, >= 10 m off the edge, less overlap) | unshift share=S (<= 0.3) | noShift friendOverlap
       ... tooFar Mm|ground|noTerrain|noGain S2|noFootprint|cohesion fN:Class gap=Gm>Lm; decided again
       only when the place, the facing or fN's order position / depth moves > 5 m, or a shifted place
       overlaps > 0.5; a shift beyond the cohesion limit (and farther than the plan's place) is cut:
       shift ... cohesionCut A->Bm. cohesion (point = archer) = the (shifted) archer place pulled within
       40 m of the main infantry, as the SPOTS COHESION lines (the cavalry reserve is exempt)
       Gap fixes 2026-10-22: Engage->Hold recall outshot|farFight engage: calm foot=.. melee=.. ranged=..
       (an out-shot engage once no significant enemy foot <= 60 m and no shooters <= 150 m for 10 s; a
       local charge > 150 m from the hold the same) | Engage->Engage farFight was <reason> (a local charge
       still fighting > 150 m from the hold becomes the full engage); a player-controlled line takes the old
       rule ("(no plan/AI line)"); reserve release cavalry|horseArchers|melee=.. / reserve resume (point =
       reserve: the cavalry reserve weighs 0 - the tactic's ProtectFlank takes it - while in melee, while
       significant enemy melee cavalry is <= 30 m or closing <= 60 m, or significant enemy horse archers <=
       80 m hit the reserve ITSELF (UnderRangedAttackRatio >= 0.15; calmer cavalry 2026-10-22, was 200 m and
       any friend near them at 0.05), and 15 s after); the re-face
       reads enemy FOOT only, and a new plan version facing < 35 deg off the old one keeps the turn
```

## Battle position

Battle position (TerrainBattlePosition, where the DEFENDER fights: the defend tactics' main infantry / archers and
native DefensiveEngagement / DefensiveLine hold it through RBMBehaviorHoldPosition; scored on the background task by
BattleTerrainRead.BattlePosition.cs, used only with terrainTacticsEnabled).

```
BATTLEPOS t  BATTLEPOS  Defender  applied  in  armies  winner  terms  total  current  gain  archer  reserve  alt
       cand  plan  expanded  ms   (once at the publish). in = infantry (foot - ranged), its frontage (4 deep at
       1.1 m, >= 20), the attacker's frontage, ranged / enemy mounted shares, walking paces. Candidates: the
       current line (always valid), every hill top, crest and a point 30 m behind each crest (reverseSlope),
       the choke hold's hold point, a 25 m lattice <= 400 m from the defender (<= 900 points). Rejected (rej)
       when not walkable (ground) / wet / within 60 m of the edge / > 20 deg (steep) / > 400 m (far) / walkable
       width across the facing < 0.8 x infantry frontage (room) / no defender route (noRoute) / the defender
       not there >= 25 s before the attacker (late): two walk floods (Dijkstra over the grid) from the snapped
       centres / walking pace, expanded = their cells. Facing = toward the attacker's centre (choke: the gap).
       winner = kind(source) at, facing, span (walkable width, ends), line = min(span x 0.95, front x 1.2)
       (the infantry's custom width), flanks = own left/right cover (water/blocked within one line width
       of the end, or a run of >= 3 cells of woods/steep > 25 deg/cavalry bad ground there; the edge never
       covers; 2026-10-30: the same test must also pass 30 m ahead of and behind the line, else
       open(shallow-<kind>) - a patch riders ride round),
       reach (s) and walk (m). terms (raw): h = clamp(height over the ground 50-200 m in front / 15 m, -1,
       1) x 0.30; front = narrower than the attacker's frontage (0..1) - 2 x short of own frontage (not in a
       choke) x 0.15; flanks = 0.5 per covered end x 0.15; fire = share of 18 approach points (30-180 m in
       front) seen over terrain and through no cover x 0.12 x archers (0.25 + 0.75 x ranged share / 0.2);
       approach = 0.6 x slow share (no navmesh, deep, wet, > 15 deg, woods 10-150 m in front) / 0.4 + 0.4
       x open share x fire, x 0.12; cav = 0.5 x bad-ground share / 0.4 - 0.5 x run-up term (mean of 3,
       40 -> 150 m) x 0.10 x (0.25 + 0.75 x enemy mounted share / 0.3); minus dead (terrain-hidden share)
       x 0.08, march (walk / 400 m) x 0.10, caught ((60 s - margin) / 35 s) x 0.10. total = the sum; current
       = the current line's total (no bonus); the best moves only with gain >= 0.15 over it (a re-pick: over
       the previous hold), else the line stays (winner=current). archer = the best defender archer spot <=
       120 m, <= 5 m in front of the line (id) or the highest dry cell <= 12 m round the point 35 m behind
       (behind), gain = m above the hold; reserve = 60 m behind (a choke winner keeps the choke plan's);
       dropped = no navmesh / outside. alt = up to 3 runners-up kind@x,y:total, 40 m apart. plan = published
       vN | none(why); fallback = the winner's hold failed the navmesh, a runner-up holds
       outnumbered=r k=K(src) w=h f fl cav ap m c maxDist reach minGain cavReach (OUTNUMBERED, 2026-10-23):
       r = attacker / defender strength (scale* = ArmyScale's latch incl. reinforcements, field* = men on the
       field, now* = a re-pick's men on the field + those still to spawn), k = clamp((r - 1) / 1.5, 0, 1).
       Weights: height x (1 + 0.5k), front x (1 + k), flanks x (1 + k), cav x (1 + 0.5k), approach
       x (1 + 0.5k), march x (1 - 0.4k), caught x (1 - 0.4k); far 400 -> 550 m (lattice step widened to stay
       <= 900 points), late 25 -> 15 s (a margin below 25 s also needs the defender there before the
       attacker's riders when >= 35% of it is mounted: cavReach=1), minGain 0.15 -> 0.10. k = 0: unchanged
BATTLEPOS t  BATTLEPOS  repick  Defender  reason  ...   (main thread) a re-score on a background task with
       the armies' centres now, when the attacker is > 300 m from the hold point and its bearing from it
       swung > 45 deg since the last evaluation, or on a TIMER (30 s since the last plan / evaluation and the
       attacker moved > 40 m or swung > 15 deg), at most every 20 s; PULL-BACK: the armies' gap >= 60 m
       above its smallest since the last evaluation, not closing (vs a 10-20 s old sample), attacker > 200 m;
       REQUEST: the choke bypass, at once, the choke left out. Reason = swing N deg | timer moved M m swing
       N deg | pullback gap +N m (min A now B, C 10-20s ago) | chokeBypassed, atk M m, moves K/budget (old
       version); the same fields, previous = the old hold re-scored (the reference); published=0(kept: hold
       Xm facing Ydeg: same place, nothing published) | 1(move K/budget hold Xm facing Ydeg def Ds+20s vs
       0.7x atk As=Ms: the defender walks there and sets up within 0.7 x the attacker's time) | 1(facing
       Ydeg, no march) | 1|0(<capped K/budget | near atk Xm (< 200) | melee <side:fN> | late ...> facing ...:
       a move refused, only the facing turns). Budget 2 moves, +1 per 100 m pull-back or bypass, max 5;
       gap fix 2026-10-22: a refused move publishes the old hold re-placed for the new facing (..., re-placed
       archer=Am reserve=Rm plug=TIER line=Lm; places kept: hold <why> when its navmesh check fails); a
       bypassed choke hold is never kept (bypassed choke dropped for the current line Dm | 1(bypassed choke
       dropped: hold ...)) and is no reference for the re-pick;
       failed=... if the task threw
BEHAVIOR t  BEHAVIOR  team  formation  HoldPosition  what  point  why   (AI thread, queued) what =
       Wait->Moving (toPoint d kind) / Moving->Holding (arrived, width=W/line L m) / Holding->Moving
       (offPoint, or replan vA->vB after a re-pick) / ->Wait (noPlan / noPlace); point = hold | archer |
       reserve; shieldWall / line = the line's arrangement; Hold->Engage (the line, latched until the tactic
       assigns it again): battleJoined | melee (20%) | enemyNear fN gap (a SIGNIFICANT enemy <= 60 m of the hold point) |
       allyFighting fN melee=S vs fM ... allyGap enemyGap (a friendly formation - reserve plug, archers,
       flank guard - <= 150 m of the line >= 10% in melee with enemy foot SIGNIFICANT to the line) |
       outshot (HoldChoke's strict rule, at its point only); the tactic then runs its own engage;
       shift / unshift / noShift (point = archer | reserve) and cohesion (point = archer) as HoldChoke's;
       reface / unface (point = hold) as HoldChoke's but at any angle; a new plan version brings back the
       plan's facing (unface plan vN); the archers face the line's facing. Gap fixes 2026-10-22: the engage
       is released - Engage->Hold recall <word> engage after Ns: calm foot=.. melee=.. [ranged=..] for 10s,
       back to the hold Dm vN quiet 25s (>= 15 s engaged, the hold <= 150 m, no significant enemy foot <= 60
       m, out of melee; outshot: no shooters <= 150 m) and for 25 s only melee / allyFighting engage it;
       turnPlace / unturnPlace (point = archer | reserve) = the place turned round the line's pivot with a
       re-faced line (or back; offGround = the turned place is not walkable); reserve release / resume
       as HoldChoke's; reserve ready / reserve wait (point = reserve, 2026-10-30) why=line <state> | line
       engaged | line not holding the position | noLine: the cavalry Reserve weighs 0 (the tactic's flank
       guard) until its tactic's Line holds at its point, and again when it stops (3 s debounce both ways);
       a player-controlled formation is never assigned.
       No melee-infantry main: the archer anchor is never a HoldPosition Line (see ArcherStand below); other foot
       ranged formations take the Archers role. The line engage belongs to the tactic: a line handed to another
       formation (main swap) stays engaged until a calm recall (Engage->Hold recall); a Line that is no longer
       the current one weighs 0.
     ArcherStand (2026-10-29, user rule: defending archers without much infantry stand and shoot) - the
       defender's archer anchor without an infantry main (ArmyComposition line holder; small / bandit battles
       and terrainTacticsEnabled off too), in every tactic phase; ArcherFlank / ArcherSkirmish / HoldPosition and
       the cover / support glue step aside for it:
       archerStand take  why=men=N ammo=A <composition shares>
       archerStand spot  spot=x,y  why=position <plan kind> vN d=Dm (the battle position's hold) |
                         stay (where they stand, facing the enemy's concentration; a battle position published
                         later is taken once while the nearest significant enemy is > 150 m away)
       archerStand contact  why=Holding->Contact|Contact->Holding <late-charge why> gap=Gm (fights in place,
                         the Move order kept)
       archerStand charge dry | charge broken  why=ammo=A<0.20 | broken lost=... target=fN gap=Gm (ChargeToTarget the
                         nearest significant enemy, ChargeGuard walks it in; latched)
       archerStand release  why=notHolder|player|role phase=P (no longer the line holder / player control)
       ESCORT (2026-10-30, both sides): a team without an infantry main sends its AI melee-infantry and dry
       formations (not the anchor itself) 5 m in front of its target's front (side by side), facing its way; the
       target = the stand holder (kind stand), else the position anchor (kind anchor, the attacker's archers). In
       melee, or with the target gone (kind lastPoint), it holds its last point and fights there; it charges
       when the target charges (the stand's charge, the target under a charge order, or the archers'
       initiative), or (USER RULE 2026-10-30) when a significant enemy FOOT formation comes within
       ChargeGuard.ChargeStartDistance 40 m edge gap of it ("foot": charges that one, re-targets foot only,
       recalled once no enemy foot is within ChargeKeepDistance 75 m; the archers keep standing):
       archerStand escort take  why=men=N target=stand|anchor|lastPoint:fN:Class (also on a target change)
       archerStand escort charge  why=target=<kind>:fN:Class <stand state>|charging|initiative|foot target=fN gap=Gm (latched)
       archerStand escort recall  why=noFoot gap=Gm (a foot charge: no enemy foot within 75 m any more, back in front of the archers)
       archerStand escort release  why=notHolder|player|role phase=P target=fN:Class (an infantry main again / player control)
       MOUNTED ESCORT (2026-10-30, both sides, with or without an infantry main): an AI cavalry / horse-archer
       formation whose category is an own token force rides beside the position anchor's flank (front level with
       its centre, just outside its width; first right, then left, then further out), facing its way;
       MountedSkirmish / HorseArcherSkirmish weigh 0 for it. No anchor = released (old behaviour):
       archerStand escort take  why=men=N target=main(mounted):fN:Class
       archerStand escort charge  why=target=main(mounted):fN:Class mainCharging|mainMelee|melee|enemyNear gap=Gm target=fN gap=Gm
                                  (the main under a real Charge order, the main / itself >= 0.1 in melee, an enemy <= 40 m)
       archerStand escort recall  why=target=main(mounted):fN:Class (none of those any more and the enemy > 80 m: back beside the main)
       archerStand escort release  as above (no longer token / no anchor / player control)
       map label 'ArcherStand(holding position|moving position 40m|contact holding stay|charge fN:Class|escort fN:Class|escort contact fN:Class|escort lastPoint)'.
       COMPEVENT anchor reason=keptInContact: the held foot anchor kept although another is 1.2x stronger,
       because it is in melee / its stand fights / a significant enemy is within 75 m. A dry formation is never
       the anchor (nor the line holder). The defend
       tactics' battle joined is the stand's latched joined (an enemy within 75 m or contact), so Defend /
       Engage no longer flip-flop.
RESERVE t  RESERVE  [repick]  Defender  applied  position  plug  tier  hidden  dLine  reach  drop  score  cells  plan
       (at the publish and after each re-pick) the battle position's INFANTRY reserve point for
       RBMTacticReservePlug: cells 50-130 m behind the line, <= 20 m beyond either end, walkable, dry, <= 15 deg,
       clearance >= 15 m, not near the edge, the farther line end <= 25 s away at the defender's pace (reach);
       hidden = no sight line (terrain, no-navmesh runs, woods; 1.7 m eye, 1 m target) from the attacker's
       centre or from 300/200/120 m in front of the hold (middle and +-1/3 of its frontage). score = 1 - 0.5 x
       distance term (50 -> 130 m) - 0.5 x height above the hold / 5 m. tier = the fallback chain step that
       found it: hidden (the above) > seen (same scan, best cell failing only the sight test) > relaxed
       (30-150 m back, 40 m beyond the ends, <= 20 deg, clearance >= 8 m, 20 m off the edge, reach <= 45 s;
       hidden preferred) > beside (relaxed rules, 5-80 m back and up to 100 m beyond either end, >= 20 m
       behind within the line's span) > nearest (walkable dry cell nearest 70 m straight behind, within 60 m,
       10 m off the edge, not in front of the hold); hidden = it passed the sight test; plug=- why=noCell =
       no tier found one (the reserve follows its line). dLine = m to the hold, drop = hold minus its
       height; cells = the standard scan's tested, hidden, eye points, rej per reason (ground wet edge steep
       room reach seen), tiers= name:tested/passed/hidden per scan (nearest:cells); position ... facing=x,y
       (re-placed) = a facing-only re-pick placed the old hold's plug again; plan = vN usable | dropped=why
       (no navmesh / outside at the build)
BEHAVIOR t  BEHAVIOR  team  formation  ReservePlug  what  point=plug|follow  why   (AI thread, queued) the
       reserve: Wait->Moving / Moving->Holding / replan / ->Holding(noFollow: no line to follow, holds where
       it stands); follow (why = noPlan | noPoint noCell|dropped | lineAway Dm from the hold vN: the line's
       front > 60 m from the hold before contact) = walks 40 m behind its line's rear (shorter / to a side on
       bad ground), facing the line's facing; plug (line at the hold, back within 40 m) = to the plug point;
       contact (the line's first melee (10%) / lineMelee | enemyNear fN ... gap: a SIGNIFICANT enemy, to the
       LINE, within 15 m of it: the timeout clock starts); Holding->Committed
       why = selfThreat fN gap (a SIGNIFICANT enemy, to the reserve, <= 50 m of it) | flankTurn fN left/rightEnd gap
       (a SIGNIFICANT enemy <= 40 m of a line end, level with or behind its front) | power ratio (the line >= 15% in melee, sqrt((line power + 1) /
       (power of enemies within 30 m + 1)) < 0.8) | casualties lost (> 25% of its most men, after contact) |
       timeout (45 s after contact) | lineGone; target = the trigger's formation, else the largest enemy
       formation within 30 m of the line (ChargeToTarget); Committed->Done (targetGone | timeout 90 s): normal
       Charge; shift / unshift / noShift = the plug point moved clear of a friend, as HoldChoke's;
       cohesion = the (shifted) plug point pulled within 80 m (edge gap) of its line, as the SPOTS COHESION
       lines. Gap fixes 2026-10-22: follow lineTurned Ndeg off the plan's facing vN (the line faces > 45 deg
       off the plan's before contact, back within 30 deg); targets foot first; Committed->Done targetAway fN
       mounted|retreating gap=Gm for Ns (the target > 60 m from the line for 10 s); resplit also after the
       line engaged (the waiting reserve keeps its men)
```

## Hedgehog

Hedgehog (TerrainHedgehog, RBMTacticHedgehog / RBMBehaviorHedgehog: the defender's infantry square or circle against
strong enemy cavalry; read on the main thread at the publish, used only with terrainTacticsEnabled).

```
HEDGEHOG t  HEDGEHOG  side  applied  men  polearmShield  enemyCav  cavPower  lanesOntoUs  clearLine  verdict
       (once per side at the publish) men / melee infantry / mounted; polearmShield = melee infantry with a
       polearm or a shield in any slot (>= 0.6); enemyCav = its mounted men (cavalry + horse archers) / men,
       share (>= 0.35); cavPower = mounted CharacterPowerCached, ratio enemy / ours (strong arm >= 1.5 OR the
       share; ours weak needs >= 1.25); lanesOntoUs = the enemy's charge lanes onto our line (CAVALRY):
       count, longest, mean; clearLine = our line's median clearance; verdict = fit | fewInfantry (< 40 or
       < 30%) littleEnemyCav ownCavStrong fewPolearms. The tactic weighs 0.01 unless fit (defender only)
BEHAVIOR t  BEHAVIOR  team  formation  Hedgehog  what  why   (AI thread, queued) what = Wait->Moving /
       Moving->Holding (to the battle position, or inPlace) / replan / line (marching) / square / circle
       (quadrants = enemy formations of >= 5 men within 150 m in N of the 4 quadrants round it: circle at
       >= 3, square again at <= 1; <= 1 change per 10 s; line (rangedFormation) when the infantry is
       mostly missile men) / archers Wait->Holding (toCentre: inside the infantry, in a Line block,
       never a square) / LATE CHARGE (the infantry, HoldLateCharge, as the choke's: Holding->Contact,
       Contact->Holding contactLost, ->Charge latched): infantry gap fN (SIGNIFICANT enemy FOOT within 15 m:
       the square waits for cavalry, charges infantry; enemy ARCHERS there are not charged while significant
       enemy melee cavalry is within 150 m) | allyFighting (as the choke's, but never while significant
       enemy melee cavalry is within 150 m) | lineGone | pushedBack | behind fN (SIGNIFICANT
       enemy FOOT only, > 20 m past its rear) | outshot (as the choke's, HoldLateCharge.OutshotReason)
       | advantage (SIGNIFICANT enemy foot in contact, power >= 1.6 for 8 s) | broken (40% of the
       SIGNIFICANT foot met lost) | cavalryGone (enemy mounted men < 30% of those at the publish +
       SIGNIFICANT enemy foot within 30 m); then
       Hold->Engage squareCharged <reason> (noInfantry without one); Engage->Hold recall <reason>: a
       charge for infantry / allyFighting / advantage / broken / behind / pushedBack re-forms the hedgehog while the enemy
       has >= 30% of its mounted men: after >= 15 s of charging once no SIGNIFICANT enemy foot formation
       (>= 5 men) has been within 60 m for 10 s (nearest = the nearest foot's gap); at once as recall
       leash <reason> charge: N m from its start (> 100 m from where the charge started, < 10% in melee,
       no significant enemy foot within 30 m); behind: foot ranged only within 30 m, loop guard 60 s;
       at once as recall cavalry fN ... gap=M m (a SIGNIFICANT enemy melee cavalry formation within 100 m,
       < 10% in melee, no significant enemy foot within 30 m; always inPlace); reform at position N m =
       back to the battle position within 80 m, else inPlace; lineGone / outshot / cavalryGone charges stay
       Gap fixes 2026-10-22: the cavalry recall re-forms at the position within 80 m too (the march halts for
       the riders); inPlace end replan vA->vB | calm cavalry=..m foot=..m = an in-place re-form goes back to
       the position (never with significant foot <= 60 m); behind counts foot <= 60 m or closing >= 1 m/s only;
       archers follow / archers hold = during a local charge with significant enemy melee cavalry <= 150 m
       the archers keep 3 m behind the charging infantry; Engage->Hold recall outshot engage: calm ... (as the
       choke's); squareCharged playerLine = a player-controlled infantry, the old battle-joined rule (recall
       playerLine once AI-controlled again); the cavalry is ProtectFlank, the reserve only with both ends covered
```

## Attacker approach

Attacker approach (TerrainApproach, scored on the background task by BattleTerrainRead.Approach.cs; used only with
terrainTacticsEnabled and not under ArmyScale NoPlans, applied=1): how the attacker's main infantry walks to the
defender's line. A route to a DEPLOY point, then a straight last leg onto the line.

```
APPROACH t  APPROACH  Attacker  applied  v  chosen  gain  minGain  why  from  line  choke  pace  archerScale  shooters
       cand=...  waypoints  [dropped]  ms   (once at the publish) line = what it was scored against: position:<kind>
       (the battle position's hold) | army (the defender's centre / main formation), at = its front-centre, facing
       (toward the attacker), halfWidth, covered=L?R? = the defender's OWN left / right line end covered; choke = the
       held choke (the choke hold plan, only with its hold within 150 m of the scored line / the defender's main;
       never once bypassed). Candidates:
       straight (shortest A* to 150 m in front of the line: the baseline, what the AI did before) | open (A* with
       the open-route clearance cost + bad ground) | left / right (deploy 60 m beyond the defender's OWN left / right
       line end, 75 m in front; only when that end is not covered) | bypass (round a held choke the straight way
       crosses, cells within its radius + 30 m closed). A* cells within 70 m of the line block are closed. Terms in
       seconds (lower wins): walk = length / pace; fire = 1 x s within 120 m and sight of a defender shooter (battle
       position archers, 2 best archer spots, the line; a re-plan: its foot ranged formations) x archerScale (0.25..1 by
       its ranged share, full at 20%); climb = 2 s per m climbed over the last 150 m; bad = 0.75 x s on woods / wet /
       > 15 deg / cavalry bad ground; choke = 150 when a held choke is crossed; narrow = 40 x (1 - bottleneck /
       attacker frontage); flank = -15 at an open line end. A detour is CHOSEN only with gain >= minGain (0.25; a
       re-plan: 0.10 for the kind followed now), len <= 1.5 x straight's, time (walk + bad) <= 1.6 x straight's;
       else straight (why=noDetour; nothing changes). Rejected: covered | noDeploy | deployNearEdge | noPath |
       capped | tooLong | tooSlow | gain(g<need) | beaten | notCrossed | noHeldChoke | noWayRound. waypoints ~50 m
       apart (deploy last), each with a navmesh WorldPosition (the last or 2 failing = straight, why=noNavmesh)
APPROACH t  APPROACH  replan  Attacker  reason  wallMs  ...   (main thread) the same fields for a re-plan from where
       the armies stand now: reason = defender (its main moved > 60 m / turned > 20 deg, >= 20 s after the last plan)
       | timer (30 s after it, either main moved > 30 m), n = count (<= 8), keep = kind followed now; replanStop close
       (mains within 150 m) | melee | cap
APPROACH t  APPROACH  use  team  formation  what  why   (AI thread, queued) the attacker's main infantry under
       BehaviorAdvance: take / adopt vA->vB (kind, wp i/n, off = metres off the route, <= 120) | waypoint i/n
       reached|passed (front within 25 m of it, or past it along its leg) | refuse | drop <reason> (battle) =
       straight for the rest of the battle: enemyNear fN gap (a significant enemy within 150 m edge gap) | melee
       (>= 10%) | player | stalled (moved < 5 m in 15 s) | timeout (300 s) | arrived; (version) = until a new
       plan: blocked fN (a significant enemy formation, not horse archers, within 60 m of the remaining route) |
       offRoute | noGain vN (a re-plan found no detour still worth it) | notMain
```

## Reactions

Reactions (ReactionMonitor + RBMBehaviorReact, aiReactionsEnabled; every AI team in a field battle, once a second, AI
thread; terrain read or not). The kinds: R1 own formation attacked (significant enemy, not horse archers, in melee <=
30 m with the victim >= 15% in melee, or closing: foot <= 60 m at >= 1 m/s, cavalry <= 150 m at >= 3 m/s; never the
main infantry's frontal clash, never a holding choke line / hedgehog); R2 enemy cavalry (horse archers within 60 m)
round our main line (beyond its half width + 20 m, or past its front by 10 m) or at own archers in front with no
infantry screen, closing on own foot ranged / a formation behind the main front / a main line end at >= 3 m/s with ETA
<= 30 s; R4 an enemy formation (significant to our main infantry) >= 25% in melee with an own formation >= 20% in melee
within 30 m, for 6 checks: free cavalry to its open flank / rear. R3 enemy archers (foot ranged, or horse archers) whose
missiles land on an own formation they are significant to (UnderRangedAttackRatio >= 0.05, within 200 m: trigger
hits=) or that stood within 200 m of our main infantry for 20 s with our side's ranged power under 0.6 x theirs
(unanswered), 3 checks: free melee cavalry rides to their flank / rear 30 m out (frontal when no side is open but the
straight way is) and charges, or infantry within 80 m charges at once; never against horse archers on foot, cavalry
against horse archers only under 3 m/s; refused near significant enemy melee cavalry (150 m of responder / archers),
with significant melee foot within 60 m of the archers (screened) or 40 m of the way / across their main's front; one
per team, 20 s team / 60 s target cooldown; priority R2 > R1 > R3 > R4. R6 balance shift: our side's power / theirs
moved x1.3 either way (huge x2) or enemy mounted / enemy ranged fell to 30%, own ranged / own mounted to 50% (a class
that was >= 12% of its side) since the current tactic was chosen or last re-judged (>= 20 s), for 5 checks: the next
decision re-picks (REPICK reason=balance ...); <= 1 per 45 s, <= 4 switches / 8 requests per battle; a blocked one keeps
the shift (judged again later). R7 shadow: significant enemy riders (melee cavalry or horse archers) parked round our
flank / rear (R2's round) under 2 m/s within 150 m of a FOOT victim (own foot ranged / foot behind the line; never a
line end or a mounted formation), 12 checks: a Deter screen (mode=Deter: between victim and riders, strikes only within
40 m, back to the screen beyond 60 m out of melee, no chase); R3 on horse archers riding >= 3 m/s is a Deter too, only
while they hit the victim (UnderRangedAttackRatio >= 0.1, within 120 m), else refused notThreat(hits=.. gap=..m) (a
hunt whose horse archers ride turns into one, or ends haRunning ... notThreat). CALMER CAVALRY (USER RULE 2026-10-22):
cavalry answers a mounted attacker only with >= 0.8 x its power, no joint power with the victim (refused
weakVsMounted(p=ratio), both sides); the DEFENDER's cavalry: leash 150 m from the main (was 250), a 30 s team cooldown
between tasks on mounted attackers (refused mountedCooldown(Ns)), and the pair waits 60 s after a 'timeout' end.
Victims = every team of our side (the player's formations too; responders own team only); a victim over 100 men judges
attackers as if it had 100; R1 counts horse archers in melee; the main infantry answers only its own archers within 60
m (attacker within 80 m, no other significant enemy foot within 80 m; home leash 80 m, x1.5 in Fight;
why=mainInfantry(<reason>)). USER RULE 2026-10-23: own horse archers are R1 / R2 victims only once >= 15% in melee; the
Embolon wedge is never a responder (refused wedge); enemy cavalry in FRONT of our army (ahead of the main's front,
within its half width + 30 m, its ride not crossing our front past a line end within 20 s, not riding outward >= 2
m/s) gets no R1 / R7, and no R2 on archers in front, unless it charges in (gap <= 30 m, closing >= 4 m/s) or the victim
is in melee: R1|R2 skip why=frontalCavalry lateral gap closing (once per riders and kind per 10 s). R2 round = NOT
frontal: beyond the main's half width + 30 m (was 20), 10 m behind its front, or riding round as above.

```
REACT t  REACT  team  kind  what  id=N  victim=fN:Class[(leftEnd|rightEnd)]  attacker=<fN:Class men power vs the
       own formation it is judged against>  responder=fM:Class  mode=Help|Hit|Intercept|Exploit|Hunt|Deter  tta=Ns  score=S
       why=...   (AI thread, queued) what =
       detect (confirmed: 2 checks in a row, R4 6; why = trigger gap closing eta) | assign (a task: tta = time to
       the goal, score = tta x class factor / power factor, lower wins; why = stage side / headOn / flankGuard / frontal / direct,
       from = the behavior it left, men) | line (the line-answers handshake: expected line(fN, allyFighting) = a
       holding line whose allyFighting rule will answer; latched = it went; late = not latched within 3 s, a
       formation is sent) | covered (another own strike on the attacker: a task, a CavalryCharge on it)
       | none (no free responder, once per event: fN:Class=reason ..., or cap) | phase (Approach->Strike /
       Strike->Fight with the trigger; a MOUNTED Strike's formed approach, FormedApproach, USER RULE 2026-10-30:
       'strike approach gap=Gm[ keepLost]' = it rides a Move in Line to the target's near edge (React's edge gap
       beyond 35 m; keepLost = a go whose gap stayed > 110 m for 2 s out of melee), 'strike go <why> after Ns' =
       the ChargeToTarget starts, why as BEHAVIOR CavalryCharge charge go: gap=Gm | melee gap=Gm | clash closing=C gap=Gm | gap=0m | noPoint; a strike already within 35 m goes without lines) | end (reason: targetGone targetRetreat insignificant relieved disengaged
       timeout chaseTimeout leash threatened reshuffled lostControl overridden replaced armyRetreat noMen
       sideClosed (no stage side left after the target turned; a re-pick is a phase line 'side old->new');
       relieved ends set no responder cooldown; line 'released' = a line's latch dropped, the handshake runs again; Hunt:
       screened (melee foot within 30 m of the archers) notRanged haRunning; phase, after) | clear (event
       closed: relieved quiet=Ns / attackerGone / attackerRetreat / attackerNotRanged / victimGone; tasks,
       answer, task=handoff=Rk:N (its live task moved to that event on the same attacker, a covered line there)
       / task=screenHeld (an Intercept / Deter keeps its screen: riders within 300 m)) | error (failed=: the check
       threw, count=N skipped; 5 in a row = stopped, that team's reactions stop). R6 lines: request (why =
       the shift, own/enemy power with men and inf/rng/cav/ha, the reference, the tactic and how long it
       held) | timeout (no decision took it in 15 s) | cap (limits reached, once)
```

## Display, scale and the tactic watch

```
OUTLINES t  OUTLINES  dots  traced  capped  kinds  maskCells  edges  spacing  max  ms   (once, DEVELOPER MODE
       only: display data for the Ctrl+Shift+D map view, BattleTerrainRead.Outlines.cs, computed only then): each
       open field / woods / rough / water / hilltop region flood-filled from its feature with the feature step's
       mask (open fields without their cells near the map edge; woods = the woods mask; a difficult rough = its
       disc), its cell-edge boundary thinned to one dot every spacing m; chokes = a line across the gap, spots = a
       circle of their room. dots = kept (<= max: choke lines and spot circles first, then areas biggest first;
       capped = outlines cut short or left out), kinds = dots per kind, maskCells = region cells filled, edges =
       boundary edges before thinning

SCALE t  SCALE  side  men field src power/enemy bandit siegeScene mode plans reactions latchedAt   (ARMY
       SCALE, 2026-10-23; once per side at the publish, latched for the battle): men = the whole side
       (src=spawn: the spawn logic's total, reinforcements included; src=field: men on the field),
       field = on the field at the latch, power = sum of the troops' GetPower; mode = tiny (< 30 men and
       weaker than the enemy) | small (< 60 men) | bandit (bandit leader party) | siegeScene (field-battle
       AI on a siege scene) | normal; plans=off: no battle position, choke hold, reserve plug,
       hedgehog, archer spots, cavalry lanes or flank plan for that side, no terrain factor,
       no terrain re-pick; reactions = all | R1R2 (plans off) | off (tiny)
COMPOSITION t  COMPOSITION  team  shares  men  enemyMen  dismounted  strength  token  mainRule  main  anchor  line  k  dry  scale  trigger
       (k = kFoot kCav kWeak kMount kHA footRangedEngage)
       (ArmyComposition; per team with men, after the publish: every 10 s, sooner when any share or k
       moved >= 0.05 since the team's last line, at once on a token / main / anchor / line / infantry-leads
       change; written from the AI thread's last published plan only (the snapshot built at most once
       a second, which holds every decision: verdict, main, anchor, line holder, k, bands), no line
       before the team's first snapshot; trigger = first | roles | change | timer). CATEGORIES BY TROOP TYPE
       (the troop's DefaultFormationClass via the formation's logical class counts; ammo never changes
       a man's category; skirmishers count as archers, light cavalry as horse archers; a formation's
       category = its majority, mounted when at least half its men are): melee infantry, archers,
       melee cavalry, horse archers. DISMOUNTED riders count by what they are now: a formation's
       mounted men = its cached physical mount share (QuerySystem cav + horse-archer unit ratios,
       ReadOnly, normalised over the four ratios) x its men, never above the troop-type count; the
       difference moves melee cavalry -> infantry and horse archers -> archers in proportion
       (ammo still never moves anyone); dismounted = own/enemy such riders (already inside the
       inf:/arc: men, also in a formation's category). shares = MEN x class weight (inf 1, arc 1, cav 1.5, ha 1.3)
       smoothed by an EMA (tau 10 s; reseeded unsmoothed when a side's men grow > 20 % over their
       lowest since the last seed - reinforcements, refills - and once at the terrain publish):
       own inf / rng (archers) / cav / ha / mnt = cav + ha, haOfMnt = horse archers' share of our
       mounted, enemy eFoot / eRng / eCav / eHa, cavRatio = own non-token mounted / enemy non-token
       melee cavalry (capped 10); men = raw own men inf:arc:cav:ha, enemyMen the same; strength =
       raw own/enemy weighted men, start = the enemy's at the first snapshot plus every later rise
       (arrivals are not losses; wear-down's enemyLoss); token = token categories "own:inf,ha
       enemy:-" (TOKEN FORCE: weighted share < 0.10 AND < 60 men; stops at >= 0.15 or >= 80 men;
       a token category never drives a role or ramp: no main, no anchor, no archer stand, no
       initiative, never "our cavalry" for FollowCavalry, nothing for wear-down to hold, kWeak 0
       with all our mounted token, and an enemy token category counts as absent in kFoot / kCav /
       kHA, the army frame's enemy infantry and the HA push's "only mounted left"); mainRule =
       the ONE main-body verdict "leads|noLead share=S" (the team's smoothed infantry share of its
       weighted men; leads needs >= 0.25 to start, keeps it down to 0.15, never with own infantry a
       token force); main = FormationCohesion.TeamMain (only while infantry leads), anchor = main,
       else the plan's foot anchor (the strongest AI foot formation of a non-token category with
       >= MinMainMen men and >= 0.15 of the team's foot men, held with x1.2; reason = main | noMain |
       none(noFoot|token inf|token arc|token inf arc|small)); line = the defender's archer stand holder
       (the foot anchor when it is archers, no main; small / bandit sides too; reason = archerAnchor |
       main | attacker | noAnchor | anchorNotArchers); dry = each own archer / horse-archer
       formation's ammo share (men with ammunition / its shooters), "(dry)" under 0.2; scale = ArmyScale
       reason, "(limited)" = NoPlans: every k 0, no FollowCavalry / wear-down / initiative (the archer stand stays)
       (a tiny side also keeps the old battle-joined rule);
       k = the composition ramps on the men shares (0 for a normal enemy: >= ~40 % melee infantry
       and <= ~30 % mounted by men, which read eFoot >= 0.35 and eCav <= 0.39 with the weights):
          kFoot = Ramp(1 - eFoot, 0.7, 0.9) (enemy without melee foot): HoldPosition counts enemy
                  foot archers for battleJoined / enemyNear while footRangedEngage=1 (on at kFoot
                  0.5, off at 0.3; per team), HoldLateCharge outshot window 60 -> 20 s and loss
                  8 % / 10 men -> 0, behind's ranged gap 30 -> 60 m, R3 infantry push at horse
                  archers on a friend (footRangedEngage; on itself with kFoot > 0, the main only
                  with footRangedEngage); army frame forward toward any-class enemy only with kFoot
                  > 0 or no TeamMain
          kCav  = Ramp(eCav, 0.4, 0.6) (enemy with melee cavalry): cohesion limits x (1 -> 0.6),
                  ArcherFlank's flank place (left/right sides) and ArcherSkirmish's approach /
                  pull-back place are EITHER behind the main (shared CavalryBehindPlace; on at
                  kCav 0.5, off at 0.35; pulled up to 5 m toward the enemy to keep it in reach;
                  ArcherFlank: reach = half its max missile range + half its depth toward the
                  team's median target; the last point kept 6 s when the main is lost) OR the
                  normal place (BEHAVIOR ArcherSkirmish|ArcherFlank kCavPlace "behindMain on|off
                  ..." + COMPEVENT gate what=kCavPlace), archer spots need score -
                  0.5 x kCav >= the line's fire + 0.1 (refusal "cavScore(...)")
          kWeak = Ramp(1 - cavRatio, 0.2, 0.6) x kCav (our cavalry outmatched; 0 with our mounted
                  all token / absent): ProtectFlank cavalry sortie ranges / time and the React
                  cavalry main leash x (1 - 0.6 x kWeak), CavalryCharge weight x max(1 - kWeak,
                  0.1) unless enemy melee cavalry is within 30 m of our foot in melee (never the
                  Embolon wedge); native BehaviorCharge of that formation reads the same gate
                  (CavalryCommit: 0 while wear-down holds it) in every tactic
          own-army ramps (0 for a normal army, mnt <= 0.39 at 30 % mounted men; token mounted
          categories count as 0):
          kMount = Ramp(mnt, 0.45, 0.75) (our army cavalry-heavy): AI melee foot gets FollowCavalry
                  (weight kMount x 3; the defender only while its hold has engaged / its battle is
                  joined, not latched: on >= 10 s, off after 5 s), defender
                  ProtectFlank cavalry sortie ranges / time x Lerp(1, 1.8, kMount) (composes with
                  kWeak's shrink) with a 250 m home leash from the anchor, and its sorties may take
                  EXPOSED enemy foot (foot archers x1.5, beside / behind the enemy main x1.3,
                  stragglers insignificant to the guard x1.2; distance / weight, harassers x1;
                  unscreened, inside the sortie leash) - DecisionTrace note "sortie scale= exposed="
          kHA    = Ramp(haOfMnt, 0.3, 0.7) (our mounted power mostly horse archers): melee cavalry
                  CavalryCharge waits (wear down, below; needs kMount > 0 too)
       BEHAVIOR FollowCavalry  follow take|charge|end  -  why: take = activated / released back to
          following ("release gap=Nm" past 75 m), Move point 60 m short of the target's near edge
          (never stepping back), facing it; charge = ChargeToTarget (through ChargeGuard) at edge gap
          <= 50 m, or <= 60 m with our cavalry in melee within 30 m of the target ("cavalryEngaged");
          end = melee (own >= 0.1: the tactic's charge takes over; stays off until < 0.05 for 5 s) | noTarget | player | notJoined |
          hedgehog | choke | reservePlug | armyRetreat | replaced; target = the enemy
          formation our strongest non-token mounted formation fights (CavalryCharge target, ChargeToTarget
          target, else its closest enemy while charging / in melee; a MOUNTED target only while our
          cavalry is in melee with it within 30 m, and then charged only at <= 50 m); cav=, kMount= kHA=; map label
          FollowCavalry(follow|charge fN:Class)
       BEHAVIOR CavalryCharge  wearDown hold|open  why (once per change, active only; the same text as its COMPEVENT
          gate what=wearDown hold|open, one call writes both): k = kHA x kMount > 0 holds
          the team's melee cavalry (not horse archers, not the Embolon wedge, not player-led) at a
          guard post (beside the anchor's flank, 10 m out, facing the enemy; no foot anchor: 40 m
          behind our strongest horse archers), weight 0.01 (any tactic behavior wins), until -
          latched open per team - enemyLoss=L>=Lerp(0, 0.2, k) (raw weighted men vs the start, arrivals added),
          footEngaged fN on fM (a significant enemy >= 0.1 in melee within 30 m of our foot),
          haAmmo=A (< 0.3 riders with ammunition in our horse-archer formations) | noHorseArchers,
          time=Ns>=Lerp(0, 90, k)s since first contact (any of ours >= 0.1 in melee or a significant
          enemy within 150 m edge gap), kHA=0 | kMount=0 (fell after a hold);
          per formation selfMelee (it is in melee itself), [(never held)] when it opened before any hold;
          hold text "k= kHA= kMount= loss=/need haAmmo= joined=s/need" (joined = since contact); the
          native BehaviorCharge reads the same gate (CavalryCommit)
       Behavior lines whose decision a k changed carry it: BEHAVIOR HoldPosition Hold->Engage
       "rangedEngage kFoot=..", outshot "kFoot= window= need=", behind "kFoot= rangedGap=",
       ArcherFlank / ArcherSkirmish kCavPlace, spots take "score= kCav= cav=-", cohesion texts
       " kCav=", CavalryCharge kWeak home|engaged|off, React end "leash main=.. limit= kWeak=",
       REACT assign note "haPush kFoot=..", refusal infVsHorseArchers(noHits|kFoot=..);
       BEHAVIOR <Charge|Advance> haPush take|leash|follow|end (foot, only mounted enemies left, no
       significant melee cavalry within 150 m: Move toward the nearest significant horse archers,
       leashed 150 m from the position anchor, or from the push start for the anchor itself - one
       start per formation, kept until 15 s without a push or enemy foot is back; a token enemy
       foot group counts as no foot)
COMPEVENT t  COMPEVENT  team  kind  formation=fN:Class|-  what=W  text   (ArmyComposition.Note, queued on
       any thread, drained after the COMPOSITION lines; per team / kind / what / formation the same text is
       written once per 30 s and a different text within 0.5 s of the last is counted - the next
       line ends " suppressed=N"; also a DecisionTrace note "Composition <what>" when the AI log
       traces; a call that names a behavior writes that BEHAVIOR line too, undeduped, instead of the
       trace note). The plan's own changes (state token / infantryLeads / footRangedEngage, role main /
       anchor / line) come from ONE diff of each new snapshot against the previous one. kind = role
       (who does what) | state (the army's state) | gate (a gate on a behavior) | tactic (a tactic-level
       choice); what:
       state  token         "own|enemy inf|arc|cav|ha on|off share=S men=N (thresholds)"
              infantryLeads "on share=S>=0.25" | "off share=S<0.15|tokenInfantry" (the one main-body verdict)
              footRangedEngage "on|off kFoot=.."
              reseed        "first|publish|ownGrew|enemyGrew own=lowest->now enemy=lowest->now"
              startEnemy    "raised +R -> S (enemy arrivals)"
              dry           "enter|exit ammo=A<|>=0.20 shooters=N men=M" (an archer / HA formation)
              scale         "suppressed side= mode= men=: ..." (once per side, ArmyScale NoPlans)
       role   main          "gained|lost fA->fB leads|noLead share=.. men=N minMen=M" (TeamMain change)
              anchor        "fA->fB reason=noMain|none(..) men= power=" (the foot anchor without a main;
                            nothing while the anchor is the main)
              line          "archerStand fA->fB reason=archerAnchor|main|attacker|noAnchor|
                            anchorNotArchers men= ammo=" (the archer stand holder; a former holder's
                            stand releases)
              initiative    "<why> target= team=take|held <shares>" (+ BEHAVIOR ArcherSkirmish initiative)
              initiative end "mainInfantry fN" (+ BEHAVIOR ArcherSkirmish initiative end)
              initiative suppressed "tokenArchers|scale|player <dry ...> (own charge)" (dry archers still
                            charge, without the team's initiative)
       gate   wearDown hold|open  the BEHAVIOR CavalryCharge text (per formation, active only)
              kWeak         "home weight xF kWeak=.. kFoot= kCav= kWeak=" | "engaged fN ..." | "off ..."
                            (+ BEHAVIOR CavalryCharge kWeak)
              followCavalry skip "mountedTarget fN cav=fM notEngaged"
              haPush origin "set|reset after Ns idle at x,y" | "cleared enemyFootBack"
              kCavPlace     "ArcherSkirmish|ArcherFlank behindMain on kCav=K>=0.50 behind fN" | "... behindMain
                            off kCav=K<=0.35 | mainLost" (the same switch as the BEHAVIOR line)
              reactLeash    "fromAnchor fN leash ..." (RBMBehaviorReact leashed from the position anchor
                            without a melee main; REACT end reads "leash anchor=")
       tactic reapply       "<Tactic> main=fN|- line=fA->fB infShare=S" (the plan's main appeared / vanished or
                            its archer line holder changed under the current tactic: native
                            IsTacticReapplyNeeded set, <= 1 per 5 s per tactic)
              noMainArchers "<Tactic> Advance: ArcherSkirmish for ..." (no melee-infantry main)
              mountedFloor  "FullScaleAttack k=K mnt=S src=composition|native floor=W" (the all-mounted
                            weight floor 0.5 x sqrt(remaining power) x Ramp(mnt, 0.6, 0.9); mnt = the TEAM's
                            weighted-men mounted share, token mounted categories and dismounted riders out,
                            native unweighted cavalry + horse-archer ratio before the first snapshot; written
                            when k moves >= 0.1 or crosses 0)
PREDICT t  PREDICT  team  side  terrainAt  applied  scale  in  options  pick  basePick  real  agree   (once per
       team, right after the TERRAIN lines; terrainAt = the publish time; applied = 1 when the factor
       is in the AI's weights: terrainTacticsEnabled on, the side's factors published and its scale
       has plans; scale = the SCALE mode, 'unread' before the latch)
          in       = the raw snapshot inputs: corr = mid-field corridor / wider frontage ((all) =
                     the whole corridor); exit = own exit / frontage; flanksLR = own flank routes
                     reachable (1/0/-); runupAhead = own run-up straight ahead onto the enemy
                     line's centre m (TERRAIN_MOUNTED's centre walk: the wedge forms in front of the
                     main infantry); clutter/steep15/deep = field strip shares; fire = own fire
                     share; hdiff = defender - attacker mean height m; deadDef = 1 - defender fire;
                     sameGround
          options  = applied=1: Name:w=<final weight>/t=<applied factor>(rawR)[term=value,...]/
                     noTerrain=<w / t>; applied=0: Name:base=<weight>/t=<factor>(rawR)[...]/pred=
                     <base x t>. Terms: room exit flanks (splits), runup clutter steep (wedge), fire
                     (shooting), adv (height), choke (defender holding only), dead, water, position
                     (defender holding: 1 -> 1.3 by the BATTLEPOS gain 0 -> 0.5; its FullScaleAttack
                     1 / (1 + 0.5 x (that - 1))); n/a =
                     input not measured (term 1). Applied t is the one TacticSelection multiplied in
                     (1 for an option RBM did not register; raw/terms are still the side's)
          pick     = highest score with terrain (applied=1: highest w, what the AI ranks; applied=0:
                     highest pred); basePick = highest score without it (noTerrain / base); real =
                     active tactic; agree = 1 when pick is real
TACTIC t  TACTIC  team  old -> new  pick  basePick  agree  applied  options   (the first sighting at
       the publish, old = '-'; then the tick a team's real tactic changes). The weights are re-read
       then; pick/basePick/agree/applied as PREDICT against the new tactic; options compact:
       applied=1 Name:w/t/noTerrain, applied=0 Name:base/t/pred
TACTICSUMMARY t  TACTICSUMMARY  team  changes  agreeShare  agreeS  watchedS  lastPick  real   (mission
       end): changes = real tactic changes after the first sighting; agreeS / watchedS = mission
       seconds since the publish in which the real tactic was the pick of the last evaluation,
       agreeShare = their ratio; lastPick = that pick, real = the tactic at the end

# FAILED [key] Type: message  -- a step that threw, written once per key (terrainBackground = the
       background task faulted: nothing is published)
```
