using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// Outlines: the feature map's areas as dot chains for the map view (BattleTerrain.Outlines). BACKGROUND THREAD, last
    /// group: grid, frame, feature map and archer spots only, no engine call. Display data only.
    ///
    /// Each area is flood-filled (8-connected) from its feature's position with the mask the feature step used (open field:
    /// the trimmed region; woods mask component; rough cluster / steep / DifficultTerrain disc / density area vetoed by its
    /// rays; deep cells; hilltop). Its boundary (cell edges to a 4-neighbour outside) is thinned to about one dot every
    /// OutlineDotSpacing m, evenly spaced but unordered. Chokes get a dotted line across the gap, archer spots a circle of
    /// their room radius. At most OutlineMaxDots in all: choke lines and spot circles first, then areas biggest first.
    /// </summary>
    internal static partial class BattleTerrainRead
    {
        internal const float OutlineDotSpacing = 5f; // m
        /// <summary>Cap on all dots (the overlay projects each every frame); the overlay's cavalry lanes / circles have their own budget.</summary>
        internal const int OutlineMaxDots = 4000;
        /// <summary>Boundaries are thinned to dots at least this x OutlineDotSpacing apart (candidates are cell-edge middles 4 m apart straight / 2.8 m diagonal; 0.8 gives ~5 m overall).</summary>
        private const float OutlineThinFactor = 0.8f;
        private const float OutlineLift = 1f;
        internal const float SpotFootprintMax = 40f; // m; also the map view's circles of re-planned spots

        private sealed class OutlineTrace
        {
            public BattleTerrain.OutlineKind Kind;
            public string Id;
            public float Area;
            public List<Vec2> Points;
        }

        private static void ReadOutlines(Frame f)
        {
            Grid grid = f.Grid;
            BattleTerrain terrain = f.Terrain;
            BattleTerrain.OutlinesInfo info = terrain.Outlines;
            int n = grid.Nx * grid.Ny;
            int[] stamp = new int[n];
            int[] stack = new int[n];
            int mark = 0;
            int maskCells = 0;
            int edges = 0;
            List<OutlineTrace> first = new List<OutlineTrace>();
            List<OutlineTrace> areas = new List<OutlineTrace>();

            foreach (BattleTerrain.ChokeFeature choke in terrain.Chokes)
            {
                Vec2 a;
                Vec2 b;
                if (choke.LineStart.IsValid && choke.LineEnd.IsValid)
                {
                    a = choke.LineStart;
                    b = choke.LineEnd;
                }
                else
                {
                    BattleTerrain.End end;
                    float left = HalfWidth(grid, choke.Position, f.Left, out end);
                    float right = HalfWidth(grid, choke.Position, -f.Left, out end);
                    a = choke.Position + f.Left * left;
                    b = choke.Position - f.Left * right;
                }
                List<Vec2> line = new List<Vec2>();
                float length = a.Distance(b);
                int count = Math.Max(2, (int)Math.Ceiling(length / OutlineDotSpacing) + 1);
                for (int d = 0; d < count; d++)
                {
                    float t = (float)d / (count - 1);
                    line.Add(a + (b - a) * t);
                }
                first.Add(new OutlineTrace { Kind = BattleTerrain.OutlineKind.Choke, Id = choke.Id, Area = 0f, Points = line });
            }
            foreach (BattleTerrain.SideSpots side in new[] { terrain.Spots.Attacker, terrain.Spots.Defender })
            {
                foreach (BattleTerrain.ArcherSpot spot in side.Spots)
                {
                    float radius = Math.Min(SpotFootprintMax, Math.Max(SpotMinClear, float.IsNaN(spot.Clearance) ? SpotMinClear : spot.Clearance));
                    int count = Math.Max(8, (int)Math.Ceiling(2.0 * Math.PI * radius / OutlineDotSpacing));
                    List<Vec2> circle = new List<Vec2>();
                    for (int d = 0; d < count; d++)
                    {
                        double angle = 2.0 * Math.PI * d / count;
                        circle.Add(spot.Position + new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius);
                    }
                    first.Add(new OutlineTrace { Kind = BattleTerrain.OutlineKind.Spot, Id = spot.Id, Area = 0f, Points = circle });
                }
            }

            Func<int, string, BattleTerrain.OutlineKind, Func<int, bool>, OutlineTrace> trace = (start, id, kind, inMask) =>
            {
                mark++;
                int cells;
                int found;
                List<Vec2> points = TraceRegion(grid, start, inMask, stamp, stack, mark, out cells, out found);
                maskCells += cells;
                edges += found;
                return (points == null) ? null : new OutlineTrace { Kind = kind, Id = id, Area = cells * grid.Step * grid.Step, Points = points };
            };
            List<OutlineTrace> traced = new List<OutlineTrace>();
            int[] openLabels = f.OpenFieldLabels;
            if (openLabels != null)
            {
                foreach (BattleTerrain.OpenFieldFeature open in terrain.OpenFields)
                {
                    int k = grid.IndexOf(open.Position);
                    int label = (k >= 0) ? openLabels[k] : -1;
                    if (label >= 0)
                    {
                        traced.Add(trace(k, open.Id, BattleTerrain.OutlineKind.OpenField, c => openLabels[c] == label));
                    }
                }
            }
            bool[] woodsMask = f.WoodsMask;
            if (woodsMask != null)
            {
                foreach (BattleTerrain.ObstacleFeature woods in terrain.Woods)
                {
                    traced.Add(trace(grid.IndexOf(woods.Position), woods.Id, BattleTerrain.OutlineKind.Woods, c => woodsMask[c]));
                }
            }
            foreach (BattleTerrain.ObstacleFeature rough in terrain.Rough)
            {
                Func<int, bool> mask;
                if (rough.Cover == BattleTerrain.CoverType.Steep)
                {
                    mask = c => grid.Cells[c] == Cell.Open && grid.Slopes[c] > SteepHard;
                }
                else if (rough.Cover == BattleTerrain.CoverType.Difficult)
                {
                    Vec2 centre = rough.Position;
                    float radius2 = rough.RegionRadius * rough.RegionRadius;
                    mask = c => (grid.Cells[c] == Cell.Open || grid.Cells[c] == Cell.NoNav) && grid.CellCentre(c).DistanceSquared(centre) <= radius2;
                }
                else if (rough.Source == BattleTerrain.AreaSource.Density)
                {
                    bool[] vetoed = f.VetoedWoods;
                    mask = c => vetoed != null && vetoed[c];
                }
                else
                {
                    mask = c => grid.Cells[c] == Cell.NoNav;
                }
                traced.Add(trace(grid.IndexOf(rough.Position), rough.Id, BattleTerrain.OutlineKind.Rough, mask));
            }
            foreach (BattleTerrain.WaterFeature water in terrain.Waters)
            {
                traced.Add(trace(grid.IndexOf(water.Position), water.Id, BattleTerrain.OutlineKind.Water, c => grid.Cells[c] == Cell.Deep));
            }
            foreach (BattleTerrain.HillFeature hill in terrain.Hills)
            {
                float floor = hill.Height - HillTopDrop;
                traced.Add(trace(grid.IndexOf(hill.Position), hill.Id, BattleTerrain.OutlineKind.Hilltop,
                    c => grid.Cells[c] == Cell.Open && IsPlausible(grid.Heights[c]) && grid.Heights[c] >= floor));
            }
            foreach (OutlineTrace t in traced)
            {
                if (t != null)
                {
                    areas.Add(t);
                }
            }
            areas.Sort((x, y) => y.Area.CompareTo(x.Area));
            first.AddRange(areas);

            List<BattleTerrain.OutlineDot> dots = new List<BattleTerrain.OutlineDot>();
            int[] byKind = new int[Enum.GetValues(typeof(BattleTerrain.OutlineKind)).Length];
            int capped = 0;
            foreach (OutlineTrace t in first)
            {
                if (dots.Count >= OutlineMaxDots)
                {
                    capped++;
                    continue;
                }
                bool cut = false;
                foreach (Vec2 p in t.Points)
                {
                    if (dots.Count >= OutlineMaxDots)
                    {
                        cut = true;
                        break;
                    }
                    float h = grid.SampleHeight(p);
                    if (!IsPlausible(h))
                    {
                        h = grid.HeightAt(p);
                    }
                    if (!IsPlausible(h))
                    {
                        continue;
                    }
                    dots.Add(new BattleTerrain.OutlineDot(new Vec3(p, h + OutlineLift), t.Kind, t.Id));
                    byKind[(int)t.Kind]++;
                }
                if (cut)
                {
                    capped++;
                }
            }
            info.Dots = Array.AsReadOnly(dots.ToArray());
            info.Traced = first.Count;
            info.Capped = capped;
            info.MaskCells = maskCells;
            info.Edges = edges;
            info.DotsByKind = Array.AsReadOnly(byKind);
        }

        /// <summary>Flood-fills the 8-connected region of inMask cells from start (stamped with mark) and returns its boundary as dots thinned to OutlineDotSpacing; null when the start is not in the mask.</summary>
        private static List<Vec2> TraceRegion(Grid grid, int start, Func<int, bool> inMask, int[] stamp, int[] stack, int mark, out int cells, out int edges)
        {
            cells = 0;
            edges = 0;
            if (start < 0 || !inMask(start))
            {
                return null;
            }
            int nx = grid.Nx;
            int ny = grid.Ny;
            List<int> region = new List<int>();
            int top = 0;
            stamp[start] = mark;
            stack[top++] = start;
            while (top > 0)
            {
                int c = stack[--top];
                region.Add(c);
                int i = c % nx;
                int j = c / nx;
                for (int dj = -1; dj <= 1; dj++)
                {
                    int nj = j + dj;
                    if (nj < 0 || nj >= ny)
                    {
                        continue;
                    }
                    for (int di = -1; di <= 1; di++)
                    {
                        int ni = i + di;
                        if ((di == 0 && dj == 0) || ni < 0 || ni >= nx)
                        {
                            continue;
                        }
                        int nk = nj * nx + ni;
                        if (stamp[nk] != mark && inMask(nk))
                        {
                            stamp[nk] = mark;
                            stack[top++] = nk;
                        }
                    }
                }
            }
            cells = region.Count;

            float half = grid.Step * 0.5f;
            float thin = OutlineDotSpacing * OutlineThinFactor;
            float spacing2 = thin * thin;
            Dictionary<long, List<Vec2>> buckets = new Dictionary<long, List<Vec2>>();
            List<Vec2> points = new List<Vec2>();
            int found = 0;
            foreach (int c in region)
            {
                int i = c % nx;
                int j = c / nx;
                Vec2 centre = grid.CellCentre(c);
                for (int e = 0; e < 4; e++)
                {
                    int di = (e == 0) ? -1 : (e == 1) ? 1 : 0;
                    int dj = (e == 2) ? -1 : (e == 3) ? 1 : 0;
                    int ni = i + di;
                    int nj = j + dj;
                    bool outside = ni < 0 || nj < 0 || ni >= nx || nj >= ny || stamp[nj * nx + ni] != mark;
                    if (!outside)
                    {
                        continue;
                    }
                    found++;
                    Vec2 p = centre + new Vec2(di * half, dj * half);
                    long bx = (long)Math.Floor(p.x / OutlineDotSpacing);
                    long by = (long)Math.Floor(p.y / OutlineDotSpacing);
                    bool near = false;
                    for (long y = by - 1; y <= by + 1 && !near; y++)
                    {
                        for (long x = bx - 1; x <= bx + 1 && !near; x++)
                        {
                            List<Vec2> bucket;
                            if (buckets.TryGetValue((y << 32) ^ (x & 0xFFFFFFFFL), out bucket))
                            {
                                foreach (Vec2 q in bucket)
                                {
                                    if (q.DistanceSquared(p) < spacing2)
                                    {
                                        near = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    if (near)
                    {
                        continue;
                    }
                    long key = (by << 32) ^ (bx & 0xFFFFFFFFL);
                    List<Vec2> own;
                    if (!buckets.TryGetValue(key, out own))
                    {
                        own = new List<Vec2>();
                        buckets[key] = own;
                    }
                    own.Add(p);
                    points.Add(p);
                }
            }
            edges = found;
            return points;
        }
    }
}
