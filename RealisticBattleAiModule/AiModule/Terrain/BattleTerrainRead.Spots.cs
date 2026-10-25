using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Archer spots per side (BattleTerrain.Spots). BACKGROUND THREAD: grid, frame and feature-map hills / crests only, no
    /// engine call.
    ///
    /// Candidates near the side's own line: hill tops and crest points, and a coarse lattice with each point moved to the
    /// highest roomy cell nearby (kind hill when SpotHighGain above the side's mean height, else clearView, which needs only a
    /// view). Room: walkable, dry, not steep, clearance >= SpotMinClear; candidates in the woods mask or near the map edge
    /// are rejected.
    ///
    /// Fire share = share of the enemy's approach points in front of the spot that it sees. A sight line is blocked by terrain,
    /// by SpotCoverRun m of no-navmesh cells in a row (the grid has no tree / rock heights: rocks, buildings count as blocking
    /// whatever their height) and by WoodsSightDepth m of woods-mask cells. The side's own line gets the same measure (LineFire).
    ///
    /// Score = fire + height gain term - distance penalty, so a flat spot with an open view beats a small hill whose view is
    /// blocked. A spot must beat LineFire by SpotMinImprove (a clearView spot must also see SpotClearViewMinFire); best first,
    /// SpotSeparation apart; the side must reach it first (grid A* within SpotReachShare x the enemy's time). At most
    /// SpotPathRuns searches per side. First guesses.
    /// </summary>
    internal static partial class BattleTerrainRead
    {
        internal const int SpotsPerSide = 4;
        internal const float SpotMaxFromLine = 250f; // m from its side's centre...
        internal const float SpotMaxAhead = 60f; // ...and at most this far past its own line toward the enemy
        internal const float SpotMinClear = 12f; // m of clearance at its cell
        private const float SpotMaxSlope = 15f; // deg
        private const float SpotLatticeStep = 20f; // m
        private const float SpotLocalTopRadius = 8f; // m square; each lattice point moves to the highest cell with room
        private const float SpotHighGain = 3f; // m above the side's mean height = hill, else clearView
        internal const float SpotFireRange = 180f; // m; approach points between SpotFireNear and this
        private const float SpotFireNear = 20f;
        private const int SpotFireTargets = 48; // sight lines per spot at most (stride over the approach points)
        private const int SpotMinTargets = 8; // fewer approach points in range = rejected
        private const float SpotTargetStep = 12f; // m, the approach lattice
        private const float SpotCoverRun = 8f; // m of no-navmesh cells in a row that block a sight line (woods, rocks, buildings)
        /// <summary>...or this many m of woods-mask cells in a row (trees punch only small navmesh holes, so the no-navmesh run alone sees through a forest).</summary>
        internal const float WoodsSightDepth = 12f;
        private const float SpotGainWeight = 0.5f; // score weight of the gain term...
        private const float SpotGainCap = 12f; // ...and the gain (m) it saturates at
        private const float SpotDistancePenalty = 0.2f; // score minus this x distance / SpotMaxFromLine
        private const float SpotMinImprove = 0.1f; // score must beat the line's own fire share by this
        private const float SpotClearViewMinFire = 0.6f; // a clearView spot must see this share of the approach
        private const float SpotReachShare = 0.6f; // the side must walk there in at most this share of the enemy's time
        private const float SpotSeparation = 40f; // m between kept spots
        private const int SpotPathRuns = 24; // A* searches per side (two per spot checked)
        /// <summary>|across| within this (m) of the centre line = Middle, else own Left / Right.</summary>
        private const float SpotSideCentre = 20f;

        private struct SpotCandidate
        {
            public int Cell;
            public BattleTerrain.SpotKind Kind;
            public string Source;
            public float Gain;
            public float Fire;
            public int Targets;
            public float Distance;
            public float Score;
        }

        private static void ReadSpots(Frame f)
        {
            FindBothSpots(f, f.Terrain.Spots, false);
        }

        /// <summary>FindSpots for each side into info; a side that throws is named in info.Error and keeps its empty default (the read) or is set null (nullOnFailure: a re-plan).</summary>
        private static void FindBothSpots(Frame f, BattleTerrain.SpotsInfo info, bool nullOnFailure)
        {
            List<string> errors = new List<string>();
            try
            {
                info.Attacker = FindSpots(f, f.Atk, "A");
            }
            catch (Exception e)
            {
                if (nullOnFailure)
                {
                    info.Attacker = null;
                }
                errors.Add("attacker:" + e.GetType().Name + ":" + Sanitize(e.Message));
            }
            try
            {
                info.Defender = FindSpots(f, f.Def, "D");
            }
            catch (Exception e)
            {
                if (nullOnFailure)
                {
                    info.Defender = null;
                }
                errors.Add("defender:" + e.GetType().Name + ":" + Sanitize(e.Message));
            }
            info.Error = (errors.Count > 0) ? string.Join(";", errors) : null;
        }

        /// <summary>TerrainSpots' live re-plan. BACKGROUND THREAD: FindSpots for both sides on a ReplanFrame (armies' centres now, the read's frontages / paces) over the published grid, features and woods mask. Returns a new SpotsInfo (the published one is never touched); a side that threw is left null. No engine call.</summary>
        internal static BattleTerrain.SpotsInfo ReplanSpots(BattleTerrain terrain, Grid grid, Vec2 attacker, Vec2 defender, Vec2 attackerAcross, Vec2 defenderAcross)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            BattleTerrain.SpotsInfo info = new BattleTerrain.SpotsInfo();
            Frame f = ReplanFrame(terrain, grid, attacker, defender, attackerAcross, defenderAcross);
            if (f == null)
            {
                info.Attacker = null;
                info.Defender = null;
                info.Error = "sameCentre";
                info.Ms = (float)(DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
                return info;
            }
            FindBothSpots(f, info, true);
            info.Ms = (float)(DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
            return info;
        }

        private static BattleTerrain.SideSpots FindSpots(Frame f, Side own, string prefix)
        {
            Grid grid = f.Grid;
            Side enemy = own.Enemy;
            BattleTerrain.SideSpots result = new BattleTerrain.SideSpots();
            Vec2 toward = own.Toward;
            Vec2 ownLeft = new Vec2(-toward.y, toward.x);
            float lineHeight = own.Centre.z;
            int rays = 0;

            List<Vec3> approach = ApproachPoints(f, own);
            bool[] woods = f.WoodsMask;
            float lineGround = grid.SampleHeight(own.Origin);
            int lineTargets;
            float lineFire = IsPlausible(lineGround)
                ? FireShare(grid, woods, new Vec3(own.Origin, lineGround + EyeHeight), toward, approach, ref rays, out lineTargets)
                : float.NaN;
            result.LineFire = lineFire;
            float lineScore = float.IsNaN(lineFire) ? 0f : lineFire;

            List<SpotCandidate> scored = new List<SpotCandidate>();
            HashSet<int> seen = new HashSet<int>();
            int candidates = 0, rejectRoom = 0, rejectWoods = 0, rejectEdge = 0, rejectRegion = 0, rejectTargets = 0, rejectScore = 0;

            Action<int, BattleTerrain.SpotKind, string> consider = (cell, kind, source) =>
            {
                candidates++;
                if (cell < 0 || !seen.Add(cell))
                {
                    return;
                }
                if (!HasSpotRoom(grid, cell))
                {
                    rejectRoom++;
                    return;
                }
                if (woods != null && woods[cell])
                {
                    rejectWoods++;
                    return;
                }
                if (grid.NearEdge(cell))
                {
                    rejectEdge++;
                    return;
                }
                Vec2 p = grid.CellCentre(cell);
                float distance = p.Distance(own.C2);
                if (distance > SpotMaxFromLine || Vec2.DotProduct(p - own.C2, toward) > SpotMaxAhead)
                {
                    rejectRegion++;
                    return;
                }
                float h = grid.Heights[cell];
                float gain = h - lineHeight;
                if (kind == BattleTerrain.SpotKind.ClearView && gain >= SpotHighGain)
                {
                    kind = BattleTerrain.SpotKind.Hill;
                }
                int targets;
                float fire = FireShare(grid, woods, new Vec3(p, h + EyeHeight), toward, approach, ref rays, out targets);
                if (float.IsNaN(fire))
                {
                    rejectTargets++;
                    return;
                }
                float score = fire + SpotGainWeight * Math.Max(0f, Math.Min(gain, SpotGainCap)) / SpotGainCap
                    - SpotDistancePenalty * distance / SpotMaxFromLine;
                if (score < lineScore + SpotMinImprove || (kind == BattleTerrain.SpotKind.ClearView && fire < SpotClearViewMinFire))
                {
                    rejectScore++;
                    return;
                }
                scored.Add(new SpotCandidate { Cell = cell, Kind = kind, Source = source, Gain = gain, Fire = fire, Targets = targets, Distance = distance, Score = score });
            };

            foreach (BattleTerrain.HillFeature hill in f.Terrain.Hills)
            {
                consider(grid.IndexOf(hill.Position), BattleTerrain.SpotKind.Hill, hill.Id);
            }
            foreach (BattleTerrain.CrestFeature crest in f.Terrain.Crests)
            {
                consider(grid.IndexOf(crest.Position), BattleTerrain.SpotKind.Crest, crest.Id);
            }

            int stride = Math.Max(1, (int)Math.Round(SpotLatticeStep / grid.Step));
            int topRadius = Math.Max(0, (int)Math.Round(SpotLocalTopRadius / grid.Step));
            int reach = (int)Math.Ceiling(SpotMaxFromLine / grid.Step);
            int ci = (int)Math.Floor((own.C2.x - grid.MinX) / grid.Step);
            int cj = (int)Math.Floor((own.C2.y - grid.MinY) / grid.Step);
            for (int j = Math.Max(0, cj - reach); j <= Math.Min(grid.Ny - 1, cj + reach); j++)
            {
                if ((j - cj) % stride != 0)
                {
                    continue;
                }
                for (int i = Math.Max(0, ci - reach); i <= Math.Min(grid.Nx - 1, ci + reach); i++)
                {
                    if ((i - ci) % stride != 0)
                    {
                        continue;
                    }
                    Vec2 p = grid.CellCentre(j * grid.Nx + i);
                    if (p.Distance(own.C2) > SpotMaxFromLine + SpotLatticeStep || Vec2.DotProduct(p - own.C2, toward) > SpotMaxAhead + SpotLatticeStep)
                    {
                        continue;
                    }
                    int top = LocalTop(grid, i, j, topRadius);
                    if (top >= 0)
                    {
                        consider(top, BattleTerrain.SpotKind.ClearView, "grid");
                    }
                }
            }

            scored.Sort((x, y) => y.Score.CompareTo(x.Score));
            List<BattleTerrain.ArcherSpot> kept = new List<BattleTerrain.ArcherSpot>();
            int rejectSeparation = 0, rejectReach = 0, pathRuns = 0;
            foreach (SpotCandidate c in scored)
            {
                if (kept.Count >= SpotsPerSide || pathRuns + 2 > SpotPathRuns)
                {
                    break;
                }
                Vec2 p = grid.CellCentre(c.Cell);
                bool near = false;
                foreach (BattleTerrain.ArcherSpot k in kept)
                {
                    if (k.Position.Distance(p) < SpotSeparation)
                    {
                        near = true;
                        break;
                    }
                }
                if (near)
                {
                    rejectSeparation++;
                    continue;
                }
                float ours = WalkSeconds(f, own, c.Cell);
                float theirs = WalkSeconds(f, enemy, c.Cell);
                pathRuns += 2;
                if (float.IsNaN(ours) || (!float.IsNaN(theirs) && ours > SpotReachShare * theirs))
                {
                    rejectReach++;
                    continue;
                }
                float across = Vec2.DotProduct(p - own.C2, ownLeft);
                float gainTerm = SpotGainWeight * Math.Max(0f, Math.Min(c.Gain, SpotGainCap)) / SpotGainCap;
                float viewTerm = c.Fire - lineScore;
                kept.Add(new BattleTerrain.ArcherSpot
                {
                    Id = prefix + (kept.Count + 1).ToString(CultureInfo.InvariantCulture),
                    Kind = c.Kind,
                    Source = c.Source,
                    Position = p,
                    Height = grid.Heights[c.Cell],
                    Gain = c.Gain,
                    Fire = c.Fire,
                    FireTargets = c.Targets,
                    Side = (across > SpotSideCentre) ? FormationAI.BehaviorSide.Left
                        : (across < -SpotSideCentre) ? FormationAI.BehaviorSide.Right
                        : FormationAI.BehaviorSide.Middle,
                    DistLine = c.Distance,
                    Along = Vec2.DotProduct(p - own.C2, toward),
                    Across = across,
                    Clearance = grid.Clearance[c.Cell],
                    Slope = grid.Slopes[c.Cell],
                    OurSeconds = ours,
                    EnemySeconds = theirs,
                    Score = c.Score,
                    Reason = (gainTerm > 2f * viewTerm) ? "height" : (viewTerm > 2f * gainTerm) ? "view" : "both"
                });
            }

            result.Spots = Array.AsReadOnly(kept.ToArray());
            result.Candidates = candidates;
            result.RejectRoom = rejectRoom;
            result.RejectWoods = rejectWoods;
            result.RejectEdge = rejectEdge;
            result.RejectRegion = rejectRegion;
            result.RejectTargets = rejectTargets;
            result.RejectScore = rejectScore;
            result.RejectSeparation = rejectSeparation;
            result.RejectReach = rejectReach;
            result.Rays = rays;
            result.PathRuns = pathRuns;
            return result;
        }

        /// <summary>Walkable, not wet (nor its 4 neighbours), slope known and at most SpotMaxSlope, clearance at least SpotMinClear.</summary>
        private static bool HasSpotRoom(Grid grid, int k)
        {
            if (!grid.IsWalkable(k) || grid.Wet[k] || !IsPlausible(grid.Heights[k]))
            {
                return false;
            }
            float slope = grid.Slopes[k];
            if (float.IsNaN(slope) || slope > SpotMaxSlope || grid.Clearance[k] < SpotMinClear)
            {
                return false;
            }
            int i = k % grid.Nx;
            int j = k / grid.Nx;
            return !(i > 0 && grid.Wet[k - 1]) && !(i < grid.Nx - 1 && grid.Wet[k + 1])
                && !(j > 0 && grid.Wet[k - grid.Nx]) && !(j < grid.Ny - 1 && grid.Wet[k + grid.Nx]);
        }

        /// <summary>The highest cell with room in the (2r + 1)-cell square around (i, j); -1 with none.</summary>
        private static int LocalTop(Grid grid, int i, int j, int r)
        {
            int best = -1;
            float bestHeight = float.NegativeInfinity;
            for (int y = Math.Max(0, j - r); y <= Math.Min(grid.Ny - 1, j + r); y++)
            {
                for (int x = Math.Max(0, i - r); x <= Math.Min(grid.Nx - 1, i + r); x++)
                {
                    int k = y * grid.Nx + x;
                    if (grid.Heights[k] > bestHeight && HasSpotRoom(grid, k))
                    {
                        best = k;
                        bestHeight = grid.Heights[k];
                    }
                }
            }
            return best;
        }

        /// <summary>The enemy's approach as seen from one side: walkable cells every SpotTargetStep m between its own line and the enemy's inside the field strip, TargetHeight above ground.</summary>
        private static List<Vec3> ApproachPoints(Frame f, Side own)
        {
            Grid grid = f.Grid;
            List<Vec3> points = new List<Vec3>();
            int stride = Math.Max(1, (int)Math.Round(SpotTargetStep / grid.Step));
            Vec2 ownLeft = new Vec2(-own.Toward.y, own.Toward.x);
            for (int j = 0; j < grid.Ny; j += stride)
            {
                for (int i = 0; i < grid.Nx; i += stride)
                {
                    int k = j * grid.Nx + i;
                    if (grid.Cells[k] != Cell.Open || !IsPlausible(grid.Heights[k]))
                    {
                        continue;
                    }
                    Vec2 c = grid.CellCentre(k);
                    Vec2 rel = c - own.C2;
                    float along = Vec2.DotProduct(rel, own.Toward);
                    if (along < 0f || along > f.Distance || Math.Abs(Vec2.DotProduct(rel, ownLeft)) > f.ZoneWidth * 0.5f)
                    {
                        continue;
                    }
                    points.Add(new Vec3(c, grid.Heights[k] + TargetHeight));
                }
            }
            return points;
        }

        /// <summary>Share of approach points between SpotFireNear and SpotFireRange in front of the eye that it sees, at most SpotFireTargets on a stride; NaN with fewer than SpotMinTargets in range.</summary>
        private static float FireShare(Grid grid, bool[] woods, Vec3 eye, Vec2 toward, List<Vec3> approach, ref int rays, out int targets)
        {
            targets = 0;
            Vec2 from = eye.AsVec2;
            float near2 = SpotFireNear * SpotFireNear;
            float far2 = SpotFireRange * SpotFireRange;
            int inRange = 0;
            foreach (Vec3 t in approach)
            {
                Vec2 rel = t.AsVec2 - from;
                float d2 = rel.LengthSquared;
                if (d2 >= near2 && d2 <= far2 && Vec2.DotProduct(rel, toward) > 0f)
                {
                    inRange++;
                }
            }
            if (inRange < SpotMinTargets)
            {
                return float.NaN;
            }
            int stride = Math.Max(1, (inRange + SpotFireTargets - 1) / SpotFireTargets);
            int index = 0;
            int seen = 0;
            foreach (Vec3 t in approach)
            {
                Vec2 rel = t.AsVec2 - from;
                float d2 = rel.LengthSquared;
                if (d2 < near2 || d2 > far2 || Vec2.DotProduct(rel, toward) <= 0f)
                {
                    continue;
                }
                if (index++ % stride != 0)
                {
                    continue;
                }
                targets++;
                rays++;
                bool terrainClear;
                if (SightLine(grid, woods, eye, t, false, out terrainClear))
                {
                    seen++;
                }
            }
            return Share(seen, targets);
        }

        /// <summary>
        /// The one sight-line rule (archer spots, battle position, BattleTerrain.SightClear): false when terrain rises above
        /// the line, or it crosses SpotCoverRun m of no-navmesh cells (rocks, buildings: no height in the grid) or
        /// WoodsSightDepth m of woods-mask cells in a row. wantTerrain: keep walking after a cover block so terrainClear (the
        /// terrain alone does not block it) is known; without it a cover block returns at once and terrainClear means nothing.
        /// Pure grid reads, any thread.
        /// </summary>
        internal static bool SightLine(Grid grid, bool[] woods, Vec3 from, Vec3 to, bool wantTerrain, out bool terrainClear)
        {
            terrainClear = true;
            bool coverClear = true;
            float length = from.AsVec2.Distance(to.AsVec2);
            int samples = (int)(length / grid.Step);
            float run = 0f;
            float woodsRun = 0f;
            for (int s = 1; s < samples; s++)
            {
                float t = (float)s / samples;
                Vec2 p = new Vec2(from.x + (to.x - from.x) * t, from.y + (to.y - from.y) * t);
                int k = grid.IndexOf(p);
                if (coverClear)
                {
                    if (k >= 0 && grid.Cells[k] == Cell.NoNav)
                    {
                        run += grid.Step;
                        if (run >= SpotCoverRun)
                        {
                            coverClear = false;
                        }
                    }
                    else
                    {
                        run = 0f;
                    }
                    if (k >= 0 && woods != null && woods[k])
                    {
                        woodsRun += grid.Step;
                        if (woodsRun >= WoodsSightDepth)
                        {
                            coverClear = false;
                        }
                    }
                    else
                    {
                        woodsRun = 0f;
                    }
                    if (!coverClear && !wantTerrain)
                    {
                        return false;
                    }
                }
                float h = grid.SampleHeight(p);
                if (IsPlausible(h) && h > from.z + (to.z - from.z) * t)
                {
                    terrainClear = false;
                    return false;
                }
            }
            return coverClear;
        }

        /// <summary>Seconds for the side to walk to the cell (grid A* / walking pace); NaN = no route (or no snap).</summary>
        private static float WalkSeconds(Frame f, Side side, int cell)
        {
            if (side.Snap < 0)
            {
                return float.NaN;
            }
            float length = f.Search.Find(side.Snap, cell, null, null);
            return length / side.WalkSpeed;
        }
    }
}
