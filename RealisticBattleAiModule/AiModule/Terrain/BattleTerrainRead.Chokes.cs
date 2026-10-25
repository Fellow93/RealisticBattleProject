using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// Chokes: the feature map's narrow passages (BattleTerrain.ChokeFeature), the last feature step. BACKGROUND THREAD:
    /// grid and earlier feature lists only, no engine call.
    ///
    /// A map-wide passage scan finds every narrow walkable passage between two larger areas:
    ///  1. Saddles of the clearance map: walkable cells are taken in descending clearance and joined to taken 8-neighbours
    ///     with union-find; a cell whose taken neighbours form two or more groups is a saddle (MERGE of two areas: bridge,
    ///     ford, neck, gorge; LOOP within one area: an obstacle in the way, e.g. a second bridge). Only 2 x clearance within
    ///     the width limit counts.
    ///  2. Both sides must open into a room (a dead-end inlet or small clearing is no choke).
    ///  3. Candidates (outside EdgeMargin; bridge saddles first, then merges, narrowest first, within a time budget), one
    ///     per passage; the passage is CUT and grid A* looks for a way round; a short way round means no choke (a rock in a
    ///     field), unless a wider second cut (an obstacle inside the passage) finds none.
    ///  4. Each kept one is measured: narrow stretch by clearance ascent into each side (its mouths), Position, the
    ///     narrowest walkable line, Width.
    /// Old route bottlenecks are merged into a scan choke within ChokeMerge, else listed on their own.
    ///
    /// Classification: BoundedBy, Type (bridge | ford | neck | pass | mixed), OnRoute; the most important ScanMeasureMax get
    /// their two sides by a walk flood from both mouths with the passage cut (army sides, Separates). Thresholds are first
    /// guesses.
    /// </summary>
    internal static partial class BattleTerrainRead
    {
        /// <summary>A route bottleneck is a choke only when narrower than this x the frontage that must pass it (corridor / exit bottlenecks are every route's narrowest point, however wide). Also the scan's width limit.</summary>
        private const float ChokeProfileFront = 1.5f;
        /// <summary>Route chokes closer than this (m) are one; a route choke this close to a scan choke merges into it.</summary>
        private const float ChokeMerge = 40f;
        private const float ScanMaxWidth = 80f; // m
        /// <summary>A side opens into a room: clearance at least max(this x r, r + ScanRoomMargin) somewhere on it...</summary>
        private const float ScanRoomFactor = 1.6f;
        private const float ScanRoomMargin = 8f;
        /// <summary>...and (a merge) this much walkable ground above the saddle's clearance on each side (m2).</summary>
        private const float ScanRoomMinArea = 5000f;
        /// <summary>The narrow stretch ends where clearance reaches max(this x r, r + ScanWidenMargin): its mouths.</summary>
        private const float ScanWidenFactor = 1.5f;
        private const float ScanWidenMargin = 6f;
        /// <summary>A loop saddle whose sides meet within this many cells is a ragged edge, not a passage.</summary>
        private const int ScanLocalLoop = 3;
        private const int ScanRoomFloodCap = 600; // cells
        /// <summary>The cut: cells within r + this many grid steps of the saddle are closed for the way-round search and side floods.</summary>
        private const float ScanCutPad = 1.5f;
        private const float ScanWideCutMax = 60f; // m
        private const int ScanBypassExpand = 6000; // cells; the way-round A* then gives up = no way round, a choke
        /// <summary>A way round shorter than max(ScanBypassMin, ScanBypassRatio x the straight gap across the cut) is short.</summary>
        private const float ScanBypassMin = 100f;
        private const float ScanBypassRatio = 4f;
        private const float ScanCheckBudgetMs = 10f; // candidate checks stop after this or ScanMaxChecks searches
        private const int ScanMaxChecks = 160;
        private const int ScanMaxAccepted = 16;
        /// <summary>One choke per passage: a candidate within max(this m, r1 + r2 + 2 steps) of a kept one is merged into it.</summary>
        private const float ScanMergeMin = 30f;
        private const int ScanAscentMax = 150; // cells
        private const int ScanSeedFloodCap = 400; // cells
        private const float ScanLineReach = 200f; // m each way
        private const int ScanMeasureMax = 8; // most important chokes that get the side flood
        /// <summary>Importance x this on the A->D path; x ScanSidesBonus with the armies on its two sides.</summary>
        private const float ScanRouteBonus = 3f;
        private const float ScanSidesBonus = 3f;

        /// <summary>The 8 ring neighbours in circular order (E, NE, N, NW, W, SW, S, SE).</summary>
        private static readonly int[] RingDi = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] RingDj = { 0, 1, 1, 1, 0, -1, -1, -1 };

        /// <summary>Per mask of taken ring neighbours: [mask x 9 + p] = group of ring cell p (255 = not taken), [mask x 9 + 8] = number of groups; ring cells are in one group when 8-adjacent.</summary>
        private static readonly byte[] RingGroups = BuildRingGroups();

        private static byte[] BuildRingGroups()
        {
            // own copies of the offsets: static initialisers of a partial class run in no guaranteed order
            int[] di = { 1, 1, 0, -1, -1, -1, 0, 1 };
            int[] dj = { 0, 1, 1, 1, 0, -1, -1, -1 };
            byte[] table = new byte[256 * 9];
            int[] group = new int[8];
            for (int mask = 0; mask < 256; mask++)
            {
                for (int p = 0; p < 8; p++)
                {
                    group[p] = -1;
                }
                int count = 0;
                for (int p = 0; p < 8; p++)
                {
                    if ((mask & (1 << p)) == 0 || group[p] >= 0)
                    {
                        continue;
                    }
                    group[p] = count;
                    bool grew = true;
                    while (grew)
                    {
                        grew = false;
                        for (int q = 0; q < 8; q++)
                        {
                            if ((mask & (1 << q)) == 0 || group[q] >= 0)
                            {
                                continue;
                            }
                            for (int s = 0; s < 8; s++)
                            {
                                if (group[s] == count && Math.Max(Math.Abs(di[q] - di[s]), Math.Abs(dj[q] - dj[s])) == 1)
                                {
                                    group[q] = count;
                                    grew = true;
                                    break;
                                }
                            }
                        }
                    }
                    count++;
                }
                for (int p = 0; p < 8; p++)
                {
                    table[mask * 9 + p] = (byte)((group[p] < 0) ? 255 : group[p]);
                }
                table[mask * 9 + 8] = (byte)count;
            }
            return table;
        }

        /// <summary>A saddle that passed the room test: its cell, clearance, one taken neighbour on each side, merge or loop.</summary>
        private sealed class ScanCandidate
        {
            public int Cell;
            public float R;
            public int SeedA;
            public int SeedB;
            public bool Merge;
            /// <summary>Within ScanBridgeNear cells of a bridge cell: checked first.</summary>
            public bool Bridge;
        }

        private const int ScanBridgeNear = 2;

        /// <summary>A bridge cell lies within ScanBridgeNear cells (Chebyshev) of cell c.</summary>
        private static bool NearBridge(Grid grid, int c)
        {
            if (grid.Bridge == null)
            {
                return false;
            }
            int ci = c % grid.Nx;
            int cj = c / grid.Nx;
            for (int nj = Math.Max(0, cj - ScanBridgeNear); nj <= Math.Min(grid.Ny - 1, cj + ScanBridgeNear); nj++)
            {
                for (int ni = Math.Max(0, ci - ScanBridgeNear); ni <= Math.Min(grid.Nx - 1, ci + ScanBridgeNear); ni++)
                {
                    if (grid.Bridge[nj * grid.Nx + ni])
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>A kept candidate: the cells just past the cut on each side, the cut.</summary>
        private sealed class ScanResult
        {
            public ScanCandidate Candidate;
            public int OutA;
            public int OutB;
            public float Cut;
            public bool WideCut;
        }

        /// <summary>Flood scratch shared by the step's floods (a run stamp per cell, a queue, a side label), and the way-round searches run (the check budget).</summary>
        private sealed class ScanScratch
        {
            public int[] Stamp;
            public int[] Queue;
            public int[] Label;
            public int Run;
            public int Checks;

            public ScanScratch(int n)
            {
                Stamp = new int[n];
                Queue = new int[n];
                Label = new int[n];
            }
        }

        /// <summary>The choke step: passage scan, route bottlenecks, measurements, ranking. water / fords are null when their step failed (no ids then).</summary>
        private static List<BattleTerrain.ChokeFeature> FindChokes(Frame f, WaterMap water, FordMap fords, Dictionary<string, int> dropped)
        {
            Grid grid = f.Grid;
            float limit = Math.Min(ScanMaxWidth, ChokeProfileFront * f.WiderFrontage);
            ScanScratch scratch = new ScanScratch(grid.Nx * grid.Ny);

            // 1.-2. saddles that open into rooms on both sides
            int[] rank;
            List<ScanCandidate> candidates = ScanSaddles(f, limit, scratch, out rank);
            int nearEdge = candidates.RemoveAll(x => grid.NearEdge(x.Cell));
            AddDropped(dropped, BattleTerrain.FeatureKind.Choke, nearEdge);
            long scanned = DateTime.UtcNow.Ticks;

            // 3. one per passage, no short way round
            List<ScanResult> kept = CheckCandidates(f, candidates, rank, scratch, scanned);

            // 4. features: scan chokes measured, route chokes merged into them or measured on their own
            List<BattleTerrain.ChokeFeature> chokes = new List<BattleTerrain.ChokeFeature>();
            Dictionary<BattleTerrain.ChokeFeature, float> cuts = new Dictionary<BattleTerrain.ChokeFeature, float>();
            Dictionary<BattleTerrain.ChokeFeature, int[]> outs = new Dictionary<BattleTerrain.ChokeFeature, int[]>();
            Dictionary<BattleTerrain.ChokeFeature, int> narrow = new Dictionary<BattleTerrain.ChokeFeature, int>();
            foreach (ScanResult result in kept)
            {
                BattleTerrain.ChokeFeature choke = MeasureScan(f, result, rank, water, fords);
                if (choke.Width > limit)
                {
                    continue;
                }
                chokes.Add(choke);
                cuts[choke] = result.Cut;
                outs[choke] = new[] { result.OutA, result.OutB };
                narrow[choke] = result.Candidate.Cell;
            }
            foreach (BattleTerrain.ChokeFeature route in RouteChokes(f))
            {
                BattleTerrain.ChokeFeature into = null;
                foreach (BattleTerrain.ChokeFeature choke in chokes)
                {
                    if ((choke.Sources & BattleTerrain.ChokeSource.Scan) != 0 && choke.Position.Distance(route.Position) <= ChokeMerge)
                    {
                        into = choke;
                        break;
                    }
                }
                if (into != null)
                {
                    into.Sources |= route.Sources;
                    continue;
                }
                int cell;
                float cut;
                int outA, outB;
                MeasureRoute(f, route, water, fords, out cell, out cut, out outA, out outB);
                if (cell >= 0)
                {
                    cuts[route] = cut;
                    outs[route] = new[] { outA, outB };
                    narrow[route] = cell;
                }
                chokes.Add(route);
            }

            // common numbers and first ranking (no side bonus yet), then the sides of the most important
            foreach (BattleTerrain.ChokeFeature choke in chokes)
            {
                choke.Ratio = choke.Width / f.WiderFrontage;
                choke.At = Vec2.DotProduct(choke.Position - f.Atk.C2, f.Forward) / f.Distance;
                choke.ExitOf = Armies((choke.Sources & BattleTerrain.ChokeSource.ExitAttacker) != 0,
                    (choke.Sources & BattleTerrain.ChokeSource.ExitDefender) != 0);
                choke.Importance = f.WiderFrontage / Math.Max(1f, choke.Width) * Proximity(AxisDistance(f, choke.Position))
                    * (choke.OnRoute ? ScanRouteBonus : 1f);
            }
            DropNearEdge(f, chokes, BattleTerrain.FeatureKind.Choke, dropped);
            chokes.Sort((x, y) => y.Importance.CompareTo(x.Importance));
            for (int c = 0; c < chokes.Count && c < ScanMeasureMax; c++)
            {
                BattleTerrain.ChokeFeature choke = chokes[c];
                int cell;
                float cut;
                int[] pair;
                if (narrow.TryGetValue(choke, out cell) && cuts.TryGetValue(choke, out cut) && outs.TryGetValue(choke, out pair)
                    && pair[0] >= 0 && pair[1] >= 0)
                {
                    MeasureSides(f, choke, cell, cut, pair[0], pair[1], scratch);
                    if (choke.Sides == BattleTerrain.ChokeSides.Opposite)
                    {
                        choke.Importance *= ScanSidesBonus;
                    }
                }
            }
            Rank(chokes, MaxChokes);
            return chokes;
        }

        private static float TicksMs(long from, long to)
        {
            return (float)(to - from) / TimeSpan.TicksPerMillisecond;
        }

        /// <summary>Union-find pass over walkable cells in descending clearance; returns the candidates. rank = each cell's place in that order (int.MaxValue = not walkable): cells "taken before" c have a smaller rank.</summary>
        private static List<ScanCandidate> ScanSaddles(Frame f, float limit, ScanScratch scratch, out int[] rank)
        {
            Grid grid = f.Grid;
            int nx = grid.Nx;
            int ny = grid.Ny;
            int n = nx * ny;
            float step = grid.Step;
            float cellArea = step * step;
            float[] clear = grid.Clearance;
            float rMax = (limit + step) * 0.5f;

            // descending order (clearance, then cell index): counting sort on the squared clearance in cells
            int[] key = new int[n];
            int maxKey = 0;
            int cells = 0;
            for (int k = 0; k < n; k++)
            {
                if (grid.Cells[k] != Cell.Open)
                {
                    key[k] = -1;
                    continue;
                }
                double c = clear[k] / step;
                int q = (int)Math.Round(c * c);
                key[k] = q;
                if (q > maxKey)
                {
                    maxKey = q;
                }
                cells++;
            }
            int[] next = new int[maxKey + 1];
            for (int k = 0; k < n; k++)
            {
                if (key[k] >= 0)
                {
                    next[key[k]]++;
                }
            }
            int offset = 0;
            for (int q = maxKey; q >= 0; q--)
            {
                int count = next[q];
                next[q] = offset;
                offset += count;
            }
            int[] order = new int[cells];
            for (int k = n - 1; k >= 0; k--)
            {
                if (key[k] >= 0)
                {
                    order[next[key[k]]++] = k;
                }
            }
            int[] ranks = new int[n];
            for (int k = 0; k < n; k++)
            {
                ranks[k] = int.MaxValue;
            }
            for (int o = 0; o < cells; o++)
            {
                ranks[order[o]] = o;
            }
            rank = ranks;

            int[] parent = new int[n];
            int[] size = new int[n];
            float[] peak = new float[n];
            for (int k = 0; k < n; k++)
            {
                parent[k] = -1;
            }
            int[] ring = new int[8];
            int[] roots = new int[8];
            int[] rootSeed = new int[8];
            int[] groupSeed = new int[8];
            List<ScanCandidate> candidates = new List<ScanCandidate>();
            for (int o = 0; o < cells; o++)
            {
                int c = order[o];
                int ci = c % nx;
                int cj = c / nx;
                int mask = 0;
                for (int p = 0; p < 8; p++)
                {
                    ring[p] = -1;
                    int ni = ci + RingDi[p];
                    int nj = cj + RingDj[p];
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= ny)
                    {
                        continue;
                    }
                    int nk = nj * nx + ni;
                    if (parent[nk] >= 0)
                    {
                        mask |= 1 << p;
                        ring[p] = nk;
                    }
                }
                float r = clear[c];
                int rootCount = 0;
                if (mask != 0)
                {
                    for (int p = 0; p < 8; p++)
                    {
                        if (ring[p] < 0)
                        {
                            continue;
                        }
                        int root = Root(parent, ring[p]);
                        int at = -1;
                        for (int x = 0; x < rootCount; x++)
                        {
                            if (roots[x] == root)
                            {
                                at = x;
                                break;
                            }
                        }
                        if (at < 0)
                        {
                            roots[rootCount] = root;
                            rootSeed[rootCount] = ring[p];
                            rootCount++;
                        }
                        else if (clear[ring[p]] > clear[rootSeed[at]])
                        {
                            rootSeed[at] = ring[p];
                        }
                    }
                    int groups = RingGroups[mask * 9 + 8];
                    if (groups >= 2 && r <= rMax)
                    {
                        float room = Math.Max(r * ScanRoomFactor, r + ScanRoomMargin);
                        if (rootCount >= 2)
                        {
                            // merge: the two areas with the highest peaks are its sides
                            int b1 = -1;
                            int b2 = -1;
                            for (int x = 0; x < rootCount; x++)
                            {
                                if (b1 < 0 || peak[roots[x]] > peak[roots[b1]])
                                {
                                    b2 = b1;
                                    b1 = x;
                                }
                                else if (b2 < 0 || peak[roots[x]] > peak[roots[b2]])
                                {
                                    b2 = x;
                                }
                            }
                            if (peak[roots[b2]] >= room && size[roots[b1]] * cellArea >= ScanRoomMinArea && size[roots[b2]] * cellArea >= ScanRoomMinArea)
                            {
                                candidates.Add(new ScanCandidate { Cell = c, R = r, SeedA = rootSeed[b1], SeedB = rootSeed[b2], Merge = true, Bridge = NearBridge(grid, c) });
                            }
                        }
                        else
                        {
                            // loop: the best cell of the two best groups are its sides
                            for (int g = 0; g < 8; g++)
                            {
                                groupSeed[g] = -1;
                            }
                            for (int p = 0; p < 8; p++)
                            {
                                if (ring[p] < 0)
                                {
                                    continue;
                                }
                                int g = RingGroups[mask * 9 + p];
                                if (groupSeed[g] < 0 || clear[ring[p]] > clear[groupSeed[g]])
                                {
                                    groupSeed[g] = ring[p];
                                }
                            }
                            int sa = -1;
                            int sb = -1;
                            for (int g = 0; g < groups; g++)
                            {
                                int s = groupSeed[g];
                                if (s < 0)
                                {
                                    continue;
                                }
                                if (sa < 0 || clear[s] > clear[sa])
                                {
                                    sb = sa;
                                    sa = s;
                                }
                                else if (sb < 0 || clear[s] > clear[sb])
                                {
                                    sb = s;
                                }
                            }
                            if (sa >= 0 && sb >= 0 && !JoinedNear(grid, ranks, scratch, c, sa, sb)
                                && RoomFlood(grid, ranks, scratch, c, sa, room) && RoomFlood(grid, ranks, scratch, c, sb, room))
                            {
                                candidates.Add(new ScanCandidate { Cell = c, R = r, SeedA = sa, SeedB = sb, Merge = false, Bridge = NearBridge(grid, c) });
                            }
                        }
                    }
                }

                parent[c] = c;
                size[c] = 1;
                peak[c] = r;
                for (int x = 0; x < rootCount; x++)
                {
                    int a = Root(parent, c);
                    int b = Root(parent, roots[x]);
                    if (a == b)
                    {
                        continue;
                    }
                    if (size[a] > size[b])
                    {
                        int t = a;
                        a = b;
                        b = t;
                    }
                    parent[a] = b;
                    size[b] += size[a];
                    peak[b] = Math.Max(peak[b], peak[a]);
                }
            }
            return candidates;
        }

        /// <summary>The two sides of loop saddle c meet within ScanLocalLoop cells through cells taken before it: a ragged edge, not a passage.</summary>
        private static bool JoinedNear(Grid grid, int[] rank, ScanScratch s, int c, int a, int b)
        {
            int nx = grid.Nx;
            int ci = c % nx;
            int cj = c / nx;
            int limit = rank[c];
            int run = ++s.Run;
            int head = 0;
            int tail = 0;
            s.Stamp[a] = run;
            s.Queue[tail++] = a;
            while (head < tail)
            {
                int k = s.Queue[head++];
                if (k == b)
                {
                    return true;
                }
                int i = k % nx;
                int j = k / nx;
                for (int p = 0; p < 8; p++)
                {
                    int ni = i + RingDi[p];
                    int nj = j + RingDj[p];
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= grid.Ny || Math.Abs(ni - ci) > ScanLocalLoop || Math.Abs(nj - cj) > ScanLocalLoop)
                    {
                        continue;
                    }
                    int nk = nj * nx + ni;
                    if (s.Stamp[nk] != run && rank[nk] < limit)
                    {
                        s.Stamp[nk] = run;
                        s.Queue[tail++] = nk;
                    }
                }
            }
            return false;
        }

        /// <summary>A flood from seed through cells taken before c reaches a cell with clearance >= room within ScanRoomFloodCap cells.</summary>
        private static bool RoomFlood(Grid grid, int[] rank, ScanScratch s, int c, int seed, float room)
        {
            int nx = grid.Nx;
            int limit = rank[c];
            float[] clear = grid.Clearance;
            int run = ++s.Run;
            int head = 0;
            int tail = 0;
            s.Stamp[seed] = run;
            s.Queue[tail++] = seed;
            while (head < tail && head < ScanRoomFloodCap)
            {
                int k = s.Queue[head++];
                if (clear[k] >= room)
                {
                    return true;
                }
                int i = k % nx;
                int j = k / nx;
                for (int p = 0; p < 8; p++)
                {
                    int ni = i + RingDi[p];
                    int nj = j + RingDj[p];
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= grid.Ny)
                    {
                        continue;
                    }
                    int nk = nj * nx + ni;
                    if (s.Stamp[nk] != run && rank[nk] < limit)
                    {
                        s.Stamp[nk] = run;
                        s.Queue[tail++] = nk;
                    }
                }
            }
            return false;
        }

        /// <summary>The kept candidates, each with its cut, the cells past it and the way round.</summary>
        private static List<ScanResult> CheckCandidates(Frame f, List<ScanCandidate> candidates, int[] rank,
            ScanScratch scratch, long startTicks)
        {
            Grid grid = f.Grid;
            float step = grid.Step;
            // bridge saddles first: many one-step shore saddles share the narrowest clearance and would use up the check budget
            candidates.Sort((x, y) =>
            {
                if (x.Bridge != y.Bridge)
                {
                    return x.Bridge ? -1 : 1;
                }
                if (x.Merge != y.Merge)
                {
                    return x.Merge ? -1 : 1;
                }
                int byR = x.R.CompareTo(y.R);
                return (byR != 0) ? byR : x.Cell.CompareTo(y.Cell);
            });
            List<ScanResult> kept = new List<ScanResult>();
            foreach (ScanCandidate cand in candidates)
            {
                if (kept.Count >= ScanMaxAccepted || scratch.Checks >= ScanMaxChecks || TicksMs(startTicks, DateTime.UtcNow.Ticks) > ScanCheckBudgetMs)
                {
                    continue;
                }
                Vec2 p = grid.CellCentre(cand.Cell);
                bool near = false;
                foreach (ScanResult r in kept)
                {
                    float merge = Math.Max(ScanMergeMin, cand.R + r.Candidate.R + 2f * step);
                    if (grid.CellCentre(r.Candidate.Cell).Distance(p) <= merge)
                    {
                        near = true;
                        break;
                    }
                }
                if (near)
                {
                    continue;
                }
                float cut = cand.R + ScanCutPad * step;
                int outA = SeedOutside(grid, rank, scratch, cand.Cell, cand.SeedA, cut);
                int outB = SeedOutside(grid, rank, scratch, cand.Cell, cand.SeedB, cut);
                if (outA < 0 || outB < 0)
                {
                    continue;
                }
                float bypass = BypassLength(f, scratch, cand.Cell, cut, outA, outB);
                bool wide = false;
                if (ShortWay(grid, bypass, outA, outB))
                {
                    // an obstacle inside the passage leaves two gaps that are each other's way round: retry with a wider cut
                    float wideCut = Math.Min(ScanWideCutMax, cut * 2f + step);
                    int wideA = (wideCut > cut) ? SeedOutside(grid, rank, scratch, cand.Cell, cand.SeedA, wideCut) : -1;
                    int wideB = (wideA >= 0) ? SeedOutside(grid, rank, scratch, cand.Cell, cand.SeedB, wideCut) : -1;
                    if (wideA >= 0 && wideB >= 0)
                    {
                        float wideBypass = BypassLength(f, scratch, cand.Cell, wideCut, wideA, wideB);
                        if (!ShortWay(grid, wideBypass, wideA, wideB))
                        {
                            wide = true;
                            outA = wideA;
                            outB = wideB;
                            cut = wideCut;
                        }
                    }
                    if (!wide)
                    {
                        continue;
                    }
                }
                kept.Add(new ScanResult { Candidate = cand, OutA = outA, OutB = outB, Cut = cut, WideCut = wide });
            }
            return kept;
        }

        /// <summary>Nearest cell more than cut m from c, flooding from seed through cells taken before c (stays on seed's side); -1 within ScanSeedFloodCap cells.</summary>
        private static int SeedOutside(Grid grid, int[] rank, ScanScratch s, int c, int seed, float cut)
        {
            int nx = grid.Nx;
            int limit = rank[c];
            Vec2 centre = grid.CellCentre(c);
            float cut2 = cut * cut;
            int run = ++s.Run;
            int head = 0;
            int tail = 0;
            s.Stamp[seed] = run;
            s.Queue[tail++] = seed;
            while (head < tail && head < ScanSeedFloodCap)
            {
                int k = s.Queue[head++];
                if (grid.CellCentre(k).DistanceSquared(centre) > cut2)
                {
                    return k;
                }
                int i = k % nx;
                int j = k / nx;
                for (int p = 0; p < 8; p++)
                {
                    int ni = i + RingDi[p];
                    int nj = j + RingDj[p];
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= grid.Ny)
                    {
                        continue;
                    }
                    int nk = nj * nx + ni;
                    if (s.Stamp[nk] != run && rank[nk] < limit)
                    {
                        s.Stamp[nk] = run;
                        s.Queue[tail++] = nk;
                    }
                }
            }
            return -1;
        }

        /// <summary>Metres of the shortest way from a to b with every cell within cut m of c closed (grid A*); NaN = none within reach.</summary>
        private static float BypassLength(Frame f, ScanScratch scratch, int c, float cut, int a, int b)
        {
            Grid grid = f.Grid;
            Vec2 centre = grid.CellCentre(c);
            float cut2 = cut * cut;
            float length = f.Search.Find(a, b, k => grid.CellCentre(k).DistanceSquared(centre) <= cut2, null, null, ScanBypassExpand);
            scratch.Checks++;
            return length;
        }

        /// <summary>A way round exists and is shorter than max(ScanBypassMin, ScanBypassRatio x the straight gap a -> b).</summary>
        private static bool ShortWay(Grid grid, float bypass, int a, int b)
        {
            if (float.IsNaN(bypass))
            {
                return false;
            }
            float straight = grid.CellCentre(a).Distance(grid.CellCentre(b));
            return bypass < Math.Max(ScanBypassMin, ScanBypassRatio * straight);
        }

        /// <summary>A kept scan candidate as a choke feature: narrow stretch, mouths, position, line, class.</summary>
        private static BattleTerrain.ChokeFeature MeasureScan(Frame f, ScanResult result, int[] rank, WaterMap water, FordMap fords)
        {
            Grid grid = f.Grid;
            float step = grid.Step;
            float[] clear = grid.Clearance;
            ScanCandidate cand = result.Candidate;
            int c = cand.Cell;
            float r = cand.R;
            float widen = Math.Max(r * ScanWidenFactor, r + ScanWidenMargin);
            List<int> chainA = Ascend(grid, rank, cand.SeedA);
            List<int> chainB = Ascend(grid, rank, cand.SeedB);
            int endA = WidenIndex(clear, chainA, widen);
            int endB = WidenIndex(clear, chainB, widen);

            // narrow stretch from mouth B through the saddle to mouth A, and its middle
            List<int> stretch = new List<int>();
            for (int i = endB; i >= 0; i--)
            {
                stretch.Add(chainB[i]);
            }
            stretch.Add(c);
            for (int i = 0; i <= endA; i++)
            {
                stretch.Add(chainA[i]);
            }
            float total = 0f;
            for (int i = 1; i < stretch.Count; i++)
            {
                total += grid.CellCentre(stretch[i - 1]).Distance(grid.CellCentre(stretch[i]));
            }
            int middle = c;
            int middleIndex = endB + 1;
            float run = 0f;
            for (int i = 1; i < stretch.Count; i++)
            {
                run += grid.CellCentre(stretch[i - 1]).Distance(grid.CellCentre(stretch[i]));
                if (run >= total * 0.5f)
                {
                    middle = stretch[i];
                    middleIndex = i;
                    break;
                }
            }
            int at = (clear[middle] <= r + 0.5f * step) ? middle : c;
            if (!grid.IsBridge(at))
            {
                // a bridge's choke stands on its deck: the stretch's bridge cell nearest its middle
                int best = -1;
                for (int i = 0; i < stretch.Count; i++)
                {
                    if (grid.IsBridge(stretch[i]) && (best < 0 || Math.Abs(i - middleIndex) < Math.Abs(best - middleIndex)))
                    {
                        best = i;
                    }
                }
                if (best >= 0)
                {
                    at = stretch[best];
                }
            }

            BattleTerrain.ChokeFeature choke = new BattleTerrain.ChokeFeature
            {
                Position = grid.CellCentre(at),
                Sources = BattleTerrain.ChokeSource.Scan,
                Length = total + step,
                MouthA = grid.CellCentre(chainA[endA]),
                MouthB = grid.CellCentre(chainB[endB])
            };
            float lineWidth = MeasureLine(f, choke, choke.Position);
            choke.Width = float.IsNaN(lineWidth) ? 2f * r + step : Math.Min(lineWidth, 2f * r + step);
            if (result.WideCut)
            {
                // kept only with the wider cut: the passage is all its gaps together
                float gaps = CutWidth(grid, grid.CellCentre(c), result.Cut);
                if (!float.IsNaN(gaps))
                {
                    choke.Width = Math.Max(choke.Width, gaps);
                }
            }
            ClassifyChoke(f, choke, at, r, stretch, water, fords);
            choke.OnRoute = OnRoute(f, c, result.Cut) || OnRoute(f, at, result.Cut);
            return choke;
        }

        /// <summary>A route choke not merged into a scan choke: snapped to walkable ground (12 m), narrowest line, mouths, class. cell = -1 (Type Unknown) without walkable ground.</summary>
        private static void MeasureRoute(Frame f, BattleTerrain.ChokeFeature choke, WaterMap water, FordMap fords,
            out int cell, out float cut, out int outA, out int outB)
        {
            Grid grid = f.Grid;
            float step = grid.Step;
            outA = -1;
            outB = -1;
            cut = float.NaN;
            cell = Snap(grid, choke.Position, 12f);
            if (cell < 0)
            {
                choke.Type = BattleTerrain.ChokeType.Unknown;
                return;
            }
            float r = grid.Clearance[cell];
            cut = r + ScanCutPad * step;
            Vec2 p = grid.CellCentre(cell);
            MeasureLine(f, choke, p);
            List<int> stretch = new List<int> { cell };
            if (choke.PassageDirection.IsValid)
            {
                Vec2 along = choke.PassageDirection;
                outA = Snap(grid, p - along * (cut + step), 2f * step);
                outB = Snap(grid, p + along * (cut + step), 2f * step);
                if (outA >= 0 && grid.CellCentre(outA).Distance(p) <= cut)
                {
                    outA = -1;
                }
                if (outB >= 0 && grid.CellCentre(outB).Distance(p) <= cut)
                {
                    outB = -1;
                }
                if (outA >= 0 && outB >= 0)
                {
                    choke.MouthA = grid.CellCentre(outA);
                    choke.MouthB = grid.CellCentre(outB);
                }
            }
            ClassifyChoke(f, choke, cell, r, stretch, water, fords);
            choke.OnRoute = OnRoute(f, cell, cut);
        }

        /// <summary>Narrowest walkable line through p (width walker every 10 degrees): sets LineStart / LineEnd / LineDirection and PassageDirection (normal, from mouth A to B when known); returns its width (NaN without one).</summary>
        private static float MeasureLine(Frame f, BattleTerrain.ChokeFeature choke, Vec2 p)
        {
            Grid grid = f.Grid;
            float best = float.MaxValue;
            Vec2 bestDir = Vec2.Invalid;
            float bestLeft = 0f;
            float bestRight = 0f;
            for (int deg = 0; deg < 180; deg += 10)
            {
                double a = deg * Math.PI / 180.0;
                Vec2 dir = new Vec2((float)Math.Cos(a), (float)Math.Sin(a));
                BattleTerrain.End leftEnd;
                BattleTerrain.End rightEnd;
                float left = Walk(grid, p, dir, ScanLineReach, float.NaN, out leftEnd);
                float right = Walk(grid, p, -dir, ScanLineReach, float.NaN, out rightEnd);
                if (left + right < best)
                {
                    best = left + right;
                    bestDir = dir;
                    bestLeft = left;
                    bestRight = right;
                }
            }
            if (!bestDir.IsValid)
            {
                return float.NaN;
            }
            choke.LineDirection = bestDir;
            choke.LineStart = p + bestDir * bestLeft;
            choke.LineEnd = p - bestDir * bestRight;
            Vec2 along = new Vec2(-bestDir.y, bestDir.x);
            if (choke.MouthA.IsValid && choke.MouthB.IsValid && Vec2.DotProduct(choke.MouthB - choke.MouthA, along) < 0f)
            {
                along = -along;
            }
            choke.PassageDirection = along;
            return best;
        }

        /// <summary>Walkable metres across a disc of radius round p, the least over every 10 degrees: all the gaps of a passage with an obstacle in it together. NaN without a sample.</summary>
        private static float CutWidth(Grid grid, Vec2 p, float radius)
        {
            float best = float.NaN;
            int samples = (int)Math.Floor(radius / grid.Step);
            for (int deg = 0; deg < 180; deg += 10)
            {
                double a = deg * Math.PI / 180.0;
                Vec2 dir = new Vec2((float)Math.Cos(a), (float)Math.Sin(a));
                int walkable = 0;
                for (int s = -samples; s <= samples; s++)
                {
                    if (grid.IsWalkable(grid.IndexOf(p + dir * (s * grid.Step))))
                    {
                        walkable++;
                    }
                }
                float width = walkable * grid.Step;
                if (float.IsNaN(best) || width < best)
                {
                    best = width;
                }
            }
            return best;
        }

        /// <summary>Sets BoundedBy, Type, WetShare, WaterId and FordId from the obstacle cells within r + 3 steps of cell at on each side, and the walkable cells of the stretch.</summary>
        private static void ClassifyChoke(Frame f, BattleTerrain.ChokeFeature choke, int at, float r, List<int> stretch, WaterMap water, FordMap fords)
        {
            Grid grid = f.Grid;
            float step = grid.Step;
            Vec2 atCentre = grid.CellCentre(at);

            // bridge cells on the stretch or within r + one step of it (a bridge's saddle can sit at its bank end); any = a bridge, never a ford
            HashSet<int> deck = new HashSet<int>();
            if (grid.Bridge != null && (grid.Water == null || grid.Water.BridgeCells > 0))
            {
                Action<int> addDeck = k =>
                {
                    if (grid.IsBridge(k))
                    {
                        deck.Add(k);
                    }
                };
                ForCellsWithin(grid, atCentre, r + step, addDeck);
                foreach (int s in stretch)
                {
                    ForCellsWithin(grid, grid.CellCentre(s), r + step, addDeck);
                }
            }
            // sides are read on the deck, at the bridge cell nearest the cell
            int sideCell = at;
            if (deck.Count > 0 && !grid.IsBridge(at))
            {
                float best = float.MaxValue;
                foreach (int k in deck)
                {
                    float d = grid.CellCentre(k).DistanceSquared(atCentre);
                    if (d < best)
                    {
                        best = d;
                        sideCell = k;
                    }
                }
            }
            Vec2 p = grid.CellCentre(sideCell);
            Vec2 across = choke.LineDirection.IsValid ? choke.LineDirection : f.Left;
            int[] deep = new int[2];
            int[] blocked = new int[2];
            int[] edge = new int[2];
            Dictionary<string, int> waterHits = new Dictionary<string, int>();
            ForCellsWithin(grid, p, r + 3f * step, k =>
            {
                Cell cell = grid.Cells[k];
                if (cell == Cell.Open)
                {
                    return;
                }
                int side = (Vec2.DotProduct(grid.CellCentre(k) - p, across) >= 0f) ? 0 : 1;
                if (cell == Cell.Deep)
                {
                    deep[side]++;
                    if (water != null && water.Labels[k] >= 0)
                    {
                        int w = water.Index[water.Labels[k]];
                        string id = (w >= 0) ? water.Ids[w] : null;
                        if (id != null)
                        {
                            int seen;
                            waterHits.TryGetValue(id, out seen);
                            waterHits[id] = seen + 1;
                        }
                    }
                }
                else if (cell == Cell.NoNav)
                {
                    blocked[side]++;
                }
                else
                {
                    edge[side]++;
                }
            });
            BattleTerrain.End sideA = SideObstacle(deep[0], blocked[0], edge[0]);
            BattleTerrain.End sideB = SideObstacle(deep[1], blocked[1], edge[1]);
            choke.BoundedBy = (sideA == BattleTerrain.End.None || sideB == BattleTerrain.End.None) ? BattleTerrain.ChokeBound.Unknown
                : (sideA == BattleTerrain.End.Edge || sideB == BattleTerrain.End.Edge) ? BattleTerrain.ChokeBound.Edge
                : (sideA == BattleTerrain.End.Water && sideB == BattleTerrain.End.Water) ? BattleTerrain.ChokeBound.Water
                : (sideA == BattleTerrain.End.Blocked && sideB == BattleTerrain.End.Blocked) ? BattleTerrain.ChokeBound.Blocked
                : BattleTerrain.ChokeBound.Mixed;
            choke.WaterId = TopName(waterHits);

            // walkable ground of the stretch: bridge decks, wet cells, the ford they belong to
            HashSet<int> core = new HashSet<int>();
            foreach (int k in stretch)
            {
                if (grid.IsWalkable(k))
                {
                    core.Add(k);
                }
            }
            ForCellsWithin(grid, atCentre, r + step, k =>
            {
                if (grid.IsWalkable(k))
                {
                    core.Add(k);
                }
            });
            int wet = 0;
            int bridge = deck.Count;
            Dictionary<string, int> fordHits = new Dictionary<string, int>();
            foreach (int k in core)
            {
                if (!grid.Wet[k])
                {
                    continue;
                }
                wet++;
                if (fords != null && fords.Labels[k] >= 0)
                {
                    BattleTerrain.FordFeature ford = fords.ByLabel[fords.Labels[k]];
                    if (ford != null && ford.Id != null)
                    {
                        int seen;
                        fordHits.TryGetValue(ford.Id, out seen);
                        fordHits[ford.Id] = seen + 1;
                    }
                }
            }
            choke.WetShare = (core.Count > 0) ? (float)wet / core.Count : float.NaN;
            choke.FordId = TopName(fordHits);
            bool waterBeside = sideA == BattleTerrain.End.Water || sideB == BattleTerrain.End.Water;
            choke.Type = (bridge > 0) ? BattleTerrain.ChokeType.Bridge
                : (choke.WetShare >= 0.5f && waterBeside) ? BattleTerrain.ChokeType.Ford
                : (choke.BoundedBy == BattleTerrain.ChokeBound.Water) ? BattleTerrain.ChokeType.Neck
                : (choke.BoundedBy == BattleTerrain.ChokeBound.Mixed) ? BattleTerrain.ChokeType.Mixed
                : BattleTerrain.ChokeType.Pass;
        }

        /// <summary>What one side of a choke is made of: the most common obstacle cell (water / blocked / edge), None without one.</summary>
        private static BattleTerrain.End SideObstacle(int deep, int blocked, int edge)
        {
            if (deep + blocked + edge == 0)
            {
                return BattleTerrain.End.None;
            }
            if (deep >= blocked && deep >= edge)
            {
                return BattleTerrain.End.Water;
            }
            return (blocked >= edge) ? BattleTerrain.End.Blocked : BattleTerrain.End.Edge;
        }

        /// <summary>The A->D grid path passes within cut + one step of cell c.</summary>
        private static bool OnRoute(Frame f, int c, float cut)
        {
            if (c < 0 || f.Path.Count == 0)
            {
                return false;
            }
            Vec2 p = f.Grid.CellCentre(c);
            float reach = cut + f.Grid.Step;
            float reach2 = reach * reach;
            foreach (int k in f.Path)
            {
                if (f.Grid.CellCentre(k).DistanceSquared(p) <= reach2)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The two sides of a choke: a breadth-first flood (8 neighbours, no corner cutting so a diagonal bridge deck stays passable) from both cells past the cut, the cut closed; each walkable cell gets the side reached first.</summary>
        private static void MeasureSides(Frame f, BattleTerrain.ChokeFeature choke, int c, float cut, int outA, int outB, ScanScratch s)
        {
            Grid grid = f.Grid;
            int nx = grid.Nx;
            int ny = grid.Ny;
            Vec2 centre = grid.CellCentre(c);
            float cut2 = cut * cut;
            int run = ++s.Run;
            int head = 0;
            int tail = 0;
            s.Stamp[outA] = run;
            s.Label[outA] = 0;
            s.Queue[tail++] = outA;
            if (s.Stamp[outB] != run)
            {
                s.Stamp[outB] = run;
                s.Label[outB] = 1;
                s.Queue[tail++] = outB;
            }
            bool met = false;
            while (head < tail)
            {
                int k = s.Queue[head++];
                int side = s.Label[k];
                int i = k % nx;
                int j = k / nx;
                for (int p = 0; p < 8; p++)
                {
                    int ni = i + RingDi[p];
                    int nj = j + RingDj[p];
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= ny)
                    {
                        continue;
                    }
                    int nk = nj * nx + ni;
                    if (s.Stamp[nk] == run)
                    {
                        if (s.Label[nk] != side)
                        {
                            met = true;
                        }
                        continue;
                    }
                    if (grid.Cells[nk] != Cell.Open || grid.CellCentre(nk).DistanceSquared(centre) <= cut2)
                    {
                        continue;
                    }
                    s.Stamp[nk] = run;
                    s.Label[nk] = side;
                    s.Queue[tail++] = nk;
                }
            }
            choke.Separates = !met;
            choke.AttackerSide = SideOf(f, f.Atk, s, run);
            choke.DefenderSide = SideOf(f, f.Def, s, run);
            choke.Sides = (choke.AttackerSide < 0 || choke.DefenderSide < 0) ? BattleTerrain.ChokeSides.Unknown
                : (choke.AttackerSide == choke.DefenderSide) ? BattleTerrain.ChokeSides.Same : BattleTerrain.ChokeSides.Opposite;
        }

        /// <summary>The side label of the army's snapped centre in the side flood; -1 when not reached.</summary>
        private static int SideOf(Frame f, Side side, ScanScratch s, int run)
        {
            int k = (side.Snap >= 0) ? side.Snap : Snap(f.Grid, side.C2, SnapRadius);
            return (k >= 0 && s.Stamp[k] == run) ? s.Label[k] : -1;
        }

        /// <summary>Clearance ascent from seed: each step to the 8-neighbour highest in the scan's order while higher than the current cell; at most ScanAscentMax cells, seed first.</summary>
        private static List<int> Ascend(Grid grid, int[] rank, int seed)
        {
            int nx = grid.Nx;
            List<int> chain = new List<int> { seed };
            int cur = seed;
            while (chain.Count < ScanAscentMax)
            {
                int i = cur % nx;
                int j = cur / nx;
                int best = -1;
                int bestRank = rank[cur];
                for (int p = 0; p < 8; p++)
                {
                    int ni = i + RingDi[p];
                    int nj = j + RingDj[p];
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= grid.Ny)
                    {
                        continue;
                    }
                    int nk = nj * nx + ni;
                    if (rank[nk] < bestRank)
                    {
                        bestRank = rank[nk];
                        best = nk;
                    }
                }
                if (best < 0)
                {
                    break;
                }
                chain.Add(best);
                cur = best;
            }
            return chain;
        }

        /// <summary>First index of the chain with clearance at least widen; the last index when none.</summary>
        private static int WidenIndex(float[] clear, List<int> chain, float widen)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (clear[chain[i]] >= widen)
                {
                    return i;
                }
            }
            return chain.Count - 1;
        }

        /// <summary>Corridor, mid-field corridor and exit bottlenecks, then width-profile local minima, each only when narrower than ChokeProfileFront x the frontage that must pass it; merged within ChokeMerge. Raw: the caller merges / measures / ranks.</summary>
        private static List<BattleTerrain.ChokeFeature> RouteChokes(Frame f)
        {
            List<BattleTerrain.ChokeFeature> chokes = new List<BattleTerrain.ChokeFeature>();
            BattleTerrain.OpenInfo open = f.Terrain.Open;
            float limit = ChokeProfileFront * f.WiderFrontage;
            AddChoke(chokes, open.CorridorPoint, open.CorridorWidth, limit, BattleTerrain.ChokeSource.Corridor);
            AddChoke(chokes, open.MidCorridorPoint, open.MidCorridorWidth, limit, BattleTerrain.ChokeSource.MidCorridor);
            AddChoke(chokes, f.Atk.Info.ExitPoint, f.Atk.Info.ExitWidth, ChokeProfileFront * Math.Max(1f, f.Atk.Frontage),
                BattleTerrain.ChokeSource.ExitAttacker);
            AddChoke(chokes, f.Def.Info.ExitPoint, f.Def.Info.ExitWidth, ChokeProfileFront * Math.Max(1f, f.Def.Frontage),
                BattleTerrain.ChokeSource.ExitDefender);

            IReadOnlyList<BattleTerrain.WidthLine> profile = f.Terrain.Space.WidthProfile;
            if (profile != null)
            {
                List<int> minima = new List<int>();
                for (int s = 0; s < profile.Count; s++)
                {
                    float total = profile[s].Total;
                    float before = (s > 0) ? profile[s - 1].Total : float.PositiveInfinity;
                    float after = (s < profile.Count - 1) ? profile[s + 1].Total : float.PositiveInfinity;
                    if (total < limit && total <= before && total <= after && (total < before || total < after))
                    {
                        minima.Add(s);
                    }
                }
                minima.Sort((x, y) => profile[x].Total.CompareTo(profile[y].Total));
                foreach (int s in minima)
                {
                    BattleTerrain.WidthLine line = profile[s];
                    Vec2 centre = f.Atk.C2 + f.Forward * (f.Distance * line.Fraction);
                    AddChoke(chokes, centre + f.Left * ((line.Left - line.Right) * 0.5f), line.Total, limit, BattleTerrain.ChokeSource.Profile);
                }
            }
            return chokes;
        }

        /// <summary>Adds a choke, or merges its source into one within ChokeMerge; nothing for a NaN width or one wider than limit.</summary>
        private static void AddChoke(List<BattleTerrain.ChokeFeature> chokes, Vec2 position, float width, float limit, BattleTerrain.ChokeSource source)
        {
            if (float.IsNaN(width) || float.IsNaN(position.x) || float.IsNaN(position.y) || !(width < limit))
            {
                return;
            }
            foreach (BattleTerrain.ChokeFeature choke in chokes)
            {
                if (choke.Position.Distance(position) <= ChokeMerge)
                {
                    choke.Sources |= source;
                    return;
                }
            }
            chokes.Add(new BattleTerrain.ChokeFeature { Position = position, Width = width, Sources = source });
        }
    }
}
