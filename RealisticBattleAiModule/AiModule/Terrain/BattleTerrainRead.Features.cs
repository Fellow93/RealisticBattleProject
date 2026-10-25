using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// Feature map (BattleTerrain.Features): turns the grid and the other groups' measurements into a short list of named
    /// places. BACKGROUND THREAD, last group: grid, gathered bundle and earlier snapshot values only, no engine call. Each
    /// step runs on its own: a throw leaves its kind empty and records "step:Type:message" in FeatureMap.Error. Thresholds
    /// are first guesses. Places within EdgeMargin of the map edge are dropped (counted in DroppedNearEdge); open fields
    /// lose their near-edge cells before labelling; water and flanks are kept.
    /// </summary>
    internal static partial class BattleTerrainRead
    {
        // caps per kind; flanks are always the four line ends
        internal const int MaxHills = 6;
        internal const int MaxCrests = 4;
        internal const int MaxChokes = 6;
        internal const int MaxOpenFields = 4;
        internal const int MaxWoods = 6;
        internal const int MaxRough = 6;
        internal const int MaxWater = 4;
        internal const int MaxFords = 4;

        private const float HillMinProminence = 4f; // m over a HillBox square
        private const float HillSuppress = 100f; // m; non-maximum suppression, the more prominent hill stays
        private const int HillPool = 48; // kept after suppression, before ranking
        private const float HillTopDrop = 3f; // m below the top: the hilltop's connected walkable cells
        private const int HillPathRuns = 4; // only the first hills get A* arrival times; the rest straight lines
        private const float CrestMinRise = 2f; // m above the low ground on each side...
        private const float CrestWindow = 150f; // ...looking at most this far to each side
        private const float HillBox = 120f; // m; prominence = height minus the mean walkable height in this square
        private const float OpenFieldMinArea = 10000f; // m²
        private const float ObstacleMinArea = 1500f; // m², no-navmesh and steep clusters
        private const float WaterMinArea = 1000f;
        private const float FordMinArea = 100f; // m², a wet cluster at deep water but off the path
        /// <summary>Area features rank by area / (1 + distance to the A->D segment / this (m)); hills by prominence the same way.</summary>
        private const float FeatureProximity = 200f;
        /// <summary>Water crossing the A->D line and fords on the path rank this many times higher.</summary>
        private const float OnAxisBonus = 10f;
        /// <summary>Within max(this, half the wider frontage) of the axis = centre / ahead / behind, else left / right.</summary>
        private const float SideCentreMin = 50f;

        private static void ReadFeatures(Frame f)
        {
            BattleTerrain terrain = f.Terrain;
            BattleTerrain.FeatureMapInfo info = terrain.FeatureMap;
            int searches = f.Search.Searches;
            long expanded = f.Search.Expanded;
            List<string> errors = new List<string>();
            Dictionary<string, float> stepMs = new Dictionary<string, float>();
            // places dropped within EdgeMargin of the map edge, per kind (water and flanks never)
            Dictionary<string, int> dropped = new Dictionary<string, int>();
            foreach (BattleTerrain.FeatureKind kind in new[] { BattleTerrain.FeatureKind.Hill, BattleTerrain.FeatureKind.Crest,
                BattleTerrain.FeatureKind.Choke, BattleTerrain.FeatureKind.OpenField, BattleTerrain.FeatureKind.Woods,
                BattleTerrain.FeatureKind.Rough, BattleTerrain.FeatureKind.Ford })
            {
                dropped[BattleTerrain.FeatureKindName(kind)] = 0;
            }
            WaterMap water = null;
            FordMap fords = null;

            FeatureStep(errors, stepMs, "hill", () => terrain.Hills = Finish(f, FindHills(f, dropped), BattleTerrain.FeatureKind.Hill));
            FeatureStep(errors, stepMs, "crest", () => terrain.Crests = Finish(f, FindCrests(f, dropped), BattleTerrain.FeatureKind.Crest));
            FeatureStep(errors, stepMs, "open", () => terrain.OpenFields = Finish(f, FindOpenFields(f, dropped), BattleTerrain.FeatureKind.OpenField));
            FeatureStep(errors, stepMs, "flank", () => terrain.Flanks = Finish(f, FindFlanks(f), BattleTerrain.FeatureKind.Flank));
            FeatureStep(errors, stepMs, "cover", () => FindCover(f, terrain, dropped));
            FeatureStep(errors, stepMs, "water", () => water = FindWater(f, terrain));
            FeatureStep(errors, stepMs, "ford", () =>
            {
                fords = FindFords(f, water, dropped);
                terrain.Fords = Finish(f, fords.Listed, BattleTerrain.FeatureKind.Ford);
            });
            // chokes last: the passage scan names the water / ford / open field each choke touches
            FeatureStep(errors, stepMs, "choke", () => terrain.Chokes = Finish(f, FindChokes(f, water, fords, dropped), BattleTerrain.FeatureKind.Choke));

            terrain.CollectFeatures();
            info.Paths = f.Search.Searches - searches;
            info.Expanded = (int)Math.Min(int.MaxValue, f.Search.Expanded - expanded);
            info.StepMs = stepMs;
            info.DroppedNearEdge = dropped;
            info.Error = (errors.Count > 0) ? string.Join(";", errors) : null;
        }

        private static void FeatureStep(List<string> errors, Dictionary<string, float> stepMs, string name, Action step)
        {
            long start = DateTime.UtcNow.Ticks;
            try
            {
                step();
            }
            catch (Exception e)
            {
                errors.Add(name + ":" + e.GetType().Name + ":" + Sanitize(e.Message));
            }
            stepMs[name] = (float)(DateTime.UtcNow.Ticks - start) / TimeSpan.TicksPerMillisecond;
        }

        /// <summary>Sorted, capped list in: ids by rank, the frame relations, sealed.</summary>
        private static IReadOnlyList<T> Finish<T>(Frame f, List<T> features, BattleTerrain.FeatureKind kind) where T : BattleTerrain.Feature
        {
            string prefix = BattleTerrain.FeatureKindName(kind);
            for (int r = 0; r < features.Count; r++)
            {
                features[r].Id = prefix + (r + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Place(f, features[r]);
            }
            return Array.AsReadOnly(features.ToArray());
        }

        /// <summary>Removes features within EdgeMargin of the map edge and counts them in dropped. Call before Rank so the cap is spent on inner places.</summary>
        private static void DropNearEdge<T>(Frame f, List<T> features, BattleTerrain.FeatureKind kind, Dictionary<string, int> dropped)
            where T : BattleTerrain.Feature
        {
            int removed = features.RemoveAll(x => f.Grid.NearEdge(x.Position));
            AddDropped(dropped, kind, removed);
        }

        private static void AddDropped(Dictionary<string, int> dropped, BattleTerrain.FeatureKind kind, int count)
        {
            string name = BattleTerrain.FeatureKindName(kind);
            int before;
            dropped.TryGetValue(name, out before);
            dropped[name] = before + count;
        }

        private static void Rank<T>(List<T> features, int cap) where T : BattleTerrain.Feature
        {
            features.Sort((x, y) => y.Importance.CompareTo(x.Importance));
            if (features.Count > cap)
            {
                features.RemoveRange(cap, features.Count - cap);
            }
        }

        /// <summary>The attacker-frame position, both armies' distance / bearing, Between and Side.</summary>
        private static void Place(Frame f, BattleTerrain.Feature x)
        {
            Vec2 rel = x.Position - f.Atk.C2;
            float along = Vec2.DotProduct(rel, f.Forward);
            float across = Vec2.DotProduct(rel, f.Left);
            x.Along = along;
            x.Across = across;
            x.Attacker = Relation(f.Atk, x.Position);
            x.Defender = Relation(f.Def, x.Position);
            x.Between = along >= 0f && along <= f.Distance && Math.Abs(across) <= f.ZoneWidth * 0.5f;
            float centre = Math.Max(SideCentreMin, f.WiderFrontage * 0.5f);
            x.Side = (across > centre) ? BattleTerrain.FeatureSide.Left
                : (across < -centre) ? BattleTerrain.FeatureSide.Right
                : (along < 0f) ? BattleTerrain.FeatureSide.Behind
                : (along > f.Distance) ? BattleTerrain.FeatureSide.Ahead
                : BattleTerrain.FeatureSide.Centre;
        }

        /// <summary>Distance from the army's centre and bearing from its forward (0 = toward the enemy, + = its own left).</summary>
        private static BattleTerrain.FeatureRelation Relation(Side side, Vec2 p)
        {
            Vec2 offset = p - side.C2;
            float distance = offset.Length;
            float bearing = 0f;
            if (distance > 0.01f)
            {
                Vec2 dir = offset * (1f / distance);
                float cross = side.Toward.x * dir.y - side.Toward.y * dir.x;
                bearing = (float)(Math.Atan2(cross, Vec2.DotProduct(side.Toward, dir)) * 180.0 / Math.PI);
            }
            return new BattleTerrain.FeatureRelation(distance, bearing);
        }

        private static float AxisDistance(Frame f, Vec2 p)
        {
            float along = Math.Max(0f, Math.Min(f.Distance, Vec2.DotProduct(p - f.Atk.C2, f.Forward)));
            return p.Distance(f.Atk.C2 + f.Forward * along);
        }

        private static float Proximity(float axisDistance)
        {
            return 1f / (1f + Math.Max(0f, axisDistance) / FeatureProximity);
        }

        private static BattleTerrain.ArmySet Armies(bool attacker, bool defender)
        {
            return (attacker ? BattleTerrain.ArmySet.Attacker : BattleTerrain.ArmySet.None)
                | (defender ? BattleTerrain.ArmySet.Defender : BattleTerrain.ArmySet.None);
        }

        /// <summary>Hills: prominence of every walkable cell (summed-area table), local maxima of at least HillMinProminence, suppression, ranking, then top region, crest width and arrival times. A top within EdgeMargin of the map edge is dropped and suppresses nothing.</summary>
        private static List<BattleTerrain.HillFeature> FindHills(Frame f, Dictionary<string, int> dropped)
        {
            Grid grid = f.Grid;
            int nx = grid.Nx;
            int ny = grid.Ny;
            int n = nx * ny;
            int box = Math.Max(1, (int)Math.Round(HillBox * 0.5f / grid.Step));
            float[] prominence = new float[n];
            for (int j = 0; j < ny; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    float h = grid.Heights[k];
                    prominence[k] = (grid.Cells[k] == Cell.Open && IsPlausible(h)) ? h - grid.BoxMean(i, j, box) : float.NaN;
                }
            }

            List<int> candidates = new List<int>();
            List<float> keys = new List<float>();
            for (int k = 0; k < n; k++)
            {
                float p = prominence[k];
                if (!(p >= HillMinProminence) || !IsLocalMax(grid, prominence, k))
                {
                    continue;
                }
                candidates.Add(k);
                keys.Add(-p);
            }
            int[] order = candidates.ToArray();
            Array.Sort(keys.ToArray(), order);

            List<int> tops = new List<int>();
            List<int> edgeTops = new List<int>();
            float suppress2 = HillSuppress * HillSuppress;
            foreach (int k in order)
            {
                if (tops.Count >= HillPool)
                {
                    break;
                }
                Vec2 p = grid.CellCentre(k);
                bool edge = grid.NearEdge(k);
                bool near = false;
                foreach (int t in tops)
                {
                    if (grid.CellCentre(t).DistanceSquared(p) < suppress2)
                    {
                        near = true;
                        break;
                    }
                }
                if (edge && !near)
                {
                    foreach (int t in edgeTops)
                    {
                        if (grid.CellCentre(t).DistanceSquared(p) < suppress2)
                        {
                            near = true;
                            break;
                        }
                    }
                }
                if (near)
                {
                    continue;
                }
                (edge ? edgeTops : tops).Add(k);
            }
            AddDropped(dropped, BattleTerrain.FeatureKind.Hill, edgeTops.Count);

            List<KeyValuePair<int, float>> ranked = new List<KeyValuePair<int, float>>();
            foreach (int k in tops)
            {
                ranked.Add(new KeyValuePair<int, float>(k, prominence[k] * Proximity(AxisDistance(f, grid.CellCentre(k)))));
            }
            ranked.Sort((x, y) => y.Value.CompareTo(x.Value));

            List<BattleTerrain.HillFeature> hills = new List<BattleTerrain.HillFeature>();
            int[] stamp = new int[n];
            int[] stack = new int[n];
            float cellArea = grid.Step * grid.Step;
            for (int r = 0; r < Math.Min(MaxHills, ranked.Count); r++)
            {
                int k = ranked[r].Key;
                Vec2 pos = grid.CellCentre(k);
                float h = grid.Heights[k];
                int cells;
                float length;
                float width;
                TopRegion(f, k, h - HillTopDrop, r + 1, stamp, stack, out cells, out length, out width);
                bool byPath = r < HillPathRuns && f.Atk.Snap >= 0 && f.Def.Snap >= 0;
                float atkSeconds = ArrivalSeconds(f, f.Atk, k, byPath);
                float defSeconds = ArrivalSeconds(f, f.Def, k, byPath);
                hills.Add(new BattleTerrain.HillFeature
                {
                    Position = pos,
                    Importance = ranked[r].Value,
                    Area = cells * cellArea,
                    Length = length,
                    Width = width,
                    Height = h,
                    Crest = FullWidth(grid, pos, f.Left),
                    AttackerSeconds = atkSeconds,
                    DefenderSeconds = defSeconds,
                    First = FirstThere(atkSeconds, defSeconds)
                });
            }
            return hills;
        }

        /// <summary>No 8-neighbour has a larger value (NaN neighbours are ignored).</summary>
        private static bool IsLocalMax(Grid grid, float[] values, int k)
        {
            int nx = grid.Nx;
            int i = k % nx;
            int j = k / nx;
            float v = values[k];
            for (int dj = -1; dj <= 1; dj++)
            {
                int nj = j + dj;
                if (nj < 0 || nj >= grid.Ny)
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
                    if (values[nj * nx + ni] > v)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>The hilltop: 8-connected walkable cells with height >= floor from the top cell; their count and extent along / across the axis. stamp marks visited cells with mark (one mark per hill, array shared).</summary>
        private static void TopRegion(Frame f, int top, float floor, int mark, int[] stamp, int[] stack, out int cells, out float length, out float width)
        {
            Grid grid = f.Grid;
            int nx = grid.Nx;
            int ny = grid.Ny;
            float minAlong = float.MaxValue, maxAlong = float.MinValue, minAcross = float.MaxValue, maxAcross = float.MinValue;
            cells = 0;
            int count = 0;
            stamp[top] = mark;
            stack[count++] = top;
            while (count > 0)
            {
                int c = stack[--count];
                cells++;
                Vec2 rel = grid.CellCentre(c) - f.Atk.C2;
                float along = Vec2.DotProduct(rel, f.Forward);
                float across = Vec2.DotProduct(rel, f.Left);
                minAlong = Math.Min(minAlong, along);
                maxAlong = Math.Max(maxAlong, along);
                minAcross = Math.Min(minAcross, across);
                maxAcross = Math.Max(maxAcross, across);
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
                        if (stamp[nk] != mark && grid.Cells[nk] == Cell.Open && IsPlausible(grid.Heights[nk]) && grid.Heights[nk] >= floor)
                        {
                            stamp[nk] = mark;
                            stack[count++] = nk;
                        }
                    }
                }
            }
            length = maxAlong - minAlong + grid.Step;
            width = maxAcross - minAcross + grid.Step;
        }

        /// <summary>Seconds for the side to reach the cell at its walking pace: grid A* when asked, else the straight line.</summary>
        private static float ArrivalSeconds(Frame f, Side side, int cell, bool byPath)
        {
            float length = (byPath && side.Snap >= 0) ? f.Search.Find(side.Snap, cell, null, null) : side.Origin.Distance(f.Grid.CellCentre(cell));
            return length / side.WalkSpeed;
        }

        private static BattleTerrain.ArmySet FirstThere(float attacker, float defender)
        {
            bool a = !float.IsNaN(attacker);
            bool d = !float.IsNaN(defender);
            if (!a && !d)
            {
                return BattleTerrain.ArmySet.None;
            }
            if (a && (!d || attacker < defender))
            {
                return BattleTerrain.ArmySet.Attacker;
            }
            if (d && (!a || defender < attacker))
            {
                return BattleTerrain.ArmySet.Defender;
            }
            return BattleTerrain.ArmySet.Both;
        }

        /// <summary>Crests: a sample on the A->D height profile (CrestWindow beyond each centre too) higher than the one before and not lower than the one after, with the lowest sample before higher ground (or CrestWindow) at least CrestMinRise below it on each side (also drops the lower of two tops with a shallow dip). Crests near the map edge are dropped.</summary>
        private static List<BattleTerrain.CrestFeature> FindCrests(Frame f, Dictionary<string, int> dropped)
        {
            Grid grid = f.Grid;
            List<BattleTerrain.CrestFeature> crests = new List<BattleTerrain.CrestFeature>();
            float step = grid.Step;
            int window = Math.Max(1, (int)Math.Ceiling(CrestWindow / step));
            int inside = (int)Math.Floor(f.Distance / step);
            int count = inside + 1 + 2 * window;
            float[] h = new float[count];
            for (int s = 0; s < count; s++)
            {
                float height = grid.SampleHeight(f.Atk.C2 + f.Forward * ((s - window) * step));
                h[s] = IsPlausible(height) ? height : float.NaN;
            }
            for (int s = window; s <= window + inside; s++)
            {
                float top = h[s];
                if (float.IsNaN(top) || !(top > h[s - 1]) || !(top >= h[s + 1]))
                {
                    continue;
                }
                float lowBefore = SideLow(h, s, -1, window);
                float lowAfter = SideLow(h, s, 1, window);
                if (float.IsNaN(lowBefore) || float.IsNaN(lowAfter))
                {
                    continue;
                }
                float rise = Math.Min(top - lowBefore, top - lowAfter);
                if (rise < CrestMinRise)
                {
                    continue;
                }
                float along = (s - window) * step;
                Vec2 pos = f.Atk.C2 + f.Forward * along;
                crests.Add(new BattleTerrain.CrestFeature
                {
                    Position = pos,
                    Importance = rise,
                    Width = FullWidth(grid, pos, f.Left),
                    Height = top,
                    At = along / f.Distance,
                    Rise = rise
                });
            }
            DropNearEdge(f, crests, BattleTerrain.FeatureKind.Crest, dropped);
            Rank(crests, MaxCrests);
            return crests;
        }

        /// <summary>Lowest sample walking from s in dir until a higher sample or window samples; NaN with none.</summary>
        private static float SideLow(float[] h, int s, int dir, int window)
        {
            float low = float.NaN;
            for (int t = 1; t <= window; t++)
            {
                int i = s + dir * t;
                if (i < 0 || i >= h.Length)
                {
                    break;
                }
                float v = h[i];
                if (float.IsNaN(v))
                {
                    continue;
                }
                if (v > h[s])
                {
                    break;
                }
                if (float.IsNaN(low) || v < low)
                {
                    low = v;
                }
            }
            return low;
        }

        /// <summary>Regions of clearance >= 50 m (8-connected), TRIMMED: cells within EdgeMargin of the map edge leave the mask before labelling (a field touching the edge may split); >= OpenFieldMinArea, largest first. Trimmed labels stay on the frame (Frame.OpenFieldLabels) for the outlines and cavalry orbit. Dropped = untrimmed regions with no listed-size part left.</summary>
        private static List<BattleTerrain.OpenFieldFeature> FindOpenFields(Frame f, Dictionary<string, int> dropped)
        {
            List<BattleTerrain.OpenFieldFeature> fields = new List<BattleTerrain.OpenFieldFeature>();
            Grid grid = f.Grid;
            float cellArea = grid.Step * grid.Step;
            int n = grid.Nx * grid.Ny;
            bool[] mask = new bool[n];
            for (int k = 0; k < n; k++)
            {
                mask[k] = grid.Cells[k] == Cell.Open && grid.Clearance[k] >= OpenClear50 && !grid.NearEdge(k);
            }
            List<int> sizes = new List<int>();
            int[] labels = LabelMask(grid, mask, sizes);
            f.OpenFieldLabels = labels;
            int[] index;
            List<Cluster> clusters = Clusters(f, labels, sizes, l => sizes[l] * cellArea >= OpenFieldMinArea, grid.Clearance, out index);

            int[] raw = f.OpenLabels;
            List<int> rawSizes = f.OpenSizes;
            if (raw != null && rawSizes != null)
            {
                bool[] kept = new bool[rawSizes.Count];
                for (int k = 0; k < n; k++)
                {
                    if (labels[k] >= 0 && index[labels[k]] >= 0 && raw[k] >= 0)
                    {
                        kept[raw[k]] = true;
                    }
                }
                int lost = 0;
                for (int r = 0; r < rawSizes.Count; r++)
                {
                    if (rawSizes[r] * cellArea >= OpenFieldMinArea && !kept[r])
                    {
                        lost++;
                    }
                }
                AddDropped(dropped, BattleTerrain.FeatureKind.OpenField, lost);
            }

            foreach (Cluster c in clusters)
            {
                BattleTerrain.OpenFieldFeature field = new BattleTerrain.OpenFieldFeature();
                SetArea(f, field, c);
                if (c.ValueMaxCell >= 0)
                {
                    field.Position = grid.CellCentre(c.ValueMaxCell);
                    field.MaxClear = c.ValueMax;
                }
                field.Armies = Armies(c.AtkDistance <= BigOpenReach, c.DefDistance <= BigOpenReach);
                field.Importance = c.Area;
                fields.Add(field);
            }
            Rank(fields, MaxOpenFields);
            return fields;
        }

        /// <summary>Each army's own left and right line end (width walker from its snapped centre). The enemy route round it is the other army's flank route on that side; SideInfo routes are named by the attacker's left/right for both movers (attacker own left = defender's FlankLeft, defender own left = attacker's FlankRight).</summary>
        private static List<BattleTerrain.FlankFeature> FindFlanks(Frame f)
        {
            List<BattleTerrain.FlankFeature> flanks = new List<BattleTerrain.FlankFeature>();
            flanks.Add(Flank(f, f.Atk, true, f.Left, f.Def.Info.FlankLeft));
            flanks.Add(Flank(f, f.Atk, false, -f.Left, f.Def.Info.FlankRight));
            flanks.Add(Flank(f, f.Def, true, -f.Left, f.Atk.Info.FlankRight));
            flanks.Add(Flank(f, f.Def, false, f.Left, f.Atk.Info.FlankLeft));
            return flanks;
        }

        private static BattleTerrain.FlankFeature Flank(Frame f, Side side, bool ownLeft, Vec2 direction, BattleTerrain.FlankRoute enemyRoute)
        {
            return FlankEnd(f.Grid, side.Origin, side.Frontage, (side == f.Atk) ? BattleTerrain.ArmySet.Attacker : BattleTerrain.ArmySet.Defender,
                ownLeft, direction, enemyRoute);
        }

        /// <summary>What the rays aimed through one cluster hit first.</summary>
        private sealed class RayCount
        {
            public int Flora, Ground, Objects, Entity;

            public int Hits => Flora + Ground + Objects;
        }

        /// <summary>The gathered no-navmesh rays counted per cluster: a ray counts for the cluster its target cell is labelled with in labels, mapped through index (-1 = not counted).</summary>
        private static RayCount[] CountRays(Frame f, int[] labels, int[] index, int clusters)
        {
            int n = f.Grid.Nx * f.Grid.Ny;
            RayCount[] counts = new RayCount[clusters];
            for (int c = 0; c < counts.Length; c++)
            {
                counts[c] = new RayCount();
            }
            if (f.Input.Rays == null)
            {
                return counts;
            }
            foreach (RayRecord ray in f.Input.Rays)
            {
                if (ray.Failed || ray.Target < 0 || ray.Target >= n || labels[ray.Target] < 0)
                {
                    continue;
                }
                int c = index[labels[ray.Target]];
                if (c < 0)
                {
                    continue;
                }
                RayCount count = counts[c];
                switch (ray.Hit)
                {
                    case RayHit.Flora:
                        count.Flora++;
                        break;
                    case RayHit.Terrain:
                        count.Ground++;
                        break;
                    case RayHit.Entity:
                        count.Objects++;
                        count.Entity++;
                        break;
                    case RayHit.NoEntity:
                        count.Objects++;
                        break;
                }
            }
            return counts;
        }

        // woods (first guesses)
        /// <summary>A no-navmesh component of at most this many m2 is a small obstacle (tree trunk / bush). Trees here are entity-less static bodies, not flagged flora, punching only small navmesh holes.</summary>
        private const float TreeMaxArea = 64f;
        private const float WoodsWindow = 24f; // m square, rounded to an odd number of whole cells
        /// <summary>A land cell is woods by density when this share of land cells in its window are small-obstacle cells. First guess: tune so the mask covers the map Forest regions without much outside them.</summary>
        internal const float WoodsDensity = 0.05f;
        private const float WoodsMinArea = 2000f; // m²; smaller components stay in the mask but are not listed
        /// <summary>A large no-navmesh cluster with more than this share in the woods mask joins the woods (not Rough).</summary>
        private const float WoodsClusterOverlap = 0.5f;
        /// <summary>Source: map / density when at least this share of the woods' cells come from it; both when both do.</summary>
        private const float WoodsSourceShare = 0.25f;
        /// <summary>A density-only area whose rays hit entities more than half the time (at least this many hits) is not woods.</summary>
        private const int WoodsVetoMinHits = 3;

        /// <summary>
        /// Woods and rough ground. The woods mask (BattleTerrain.WoodsMap) comes from: (1) map Forest regions (authoritative);
        /// (2) small-obstacle density (WoodsWindow square reaching WoodsDensity); (3) rays as confirmation: large no-navmesh
        /// clusters mostly inside 1 + 2 or hit mostly as flora join the woods unless hit mostly as terrain; a density-only
        /// area whose rays hit entities (houses, props) leaves the mask. Large clusters outside the woods are Rough
        /// (Terrain / Objects / Unknown). Rough also gets map DifficultTerrain regions and steep clusters. A woods / rough
        /// area whose centre lies within EdgeMargin of the map edge is dropped (the mask keeps it).
        /// </summary>
        private static void FindCover(Frame f, BattleTerrain terrain, Dictionary<string, int> dropped)
        {
            Grid grid = f.Grid;
            int nx = grid.Nx;
            int ny = grid.Ny;
            int n = nx * ny;
            float cellArea = grid.Step * grid.Step;
            BattleTerrain.WoodsMapInfo map = terrain.WoodsMap;
            List<BattleTerrain.ObstacleFeature> woods = new List<BattleTerrain.ObstacleFeature>();
            List<BattleTerrain.ObstacleFeature> rough = new List<BattleTerrain.ObstacleFeature>();
            bool[] land = new bool[n];
            for (int k = 0; k < n; k++)
            {
                land[k] = grid.Cells[k] == Cell.Open || grid.Cells[k] == Cell.NoNav;
            }

            // 1. map regions: Forest discs into the mask, DifficultTerrain regions kept for Rough
            bool[] inForest = new bool[n];
            List<TacticalInput> difficult = new List<TacticalInput>();
            if (f.Input.Tactical != null)
            {
                foreach (TacticalInput t in f.Input.Tactical)
                {
                    if (!t.Region)
                    {
                        continue;
                    }
                    if (t.Type == "Forest")
                    {
                        MarkDisc(grid, t.Position, t.Radius, land, inForest);
                    }
                    else if (t.Type == "DifficultTerrain")
                    {
                        difficult.Add(t);
                    }
                }
            }

            // 2. small obstacles and their density
            bool[] noNav = new bool[n];
            for (int k = 0; k < n; k++)
            {
                noNav[k] = grid.Cells[k] == Cell.NoNav;
            }
            List<int> noNavSizes = new List<int>();
            int[] noNavLabels = LabelMask(grid, noNav, noNavSizes);
            int treeMaxCells = Math.Max(1, (int)Math.Floor(TreeMaxArea / cellArea + 0.001f));
            bool[] small = new bool[n];
            for (int k = 0; k < n; k++)
            {
                int l = noNavLabels[k];
                if (l >= 0 && noNavSizes[l] <= treeMaxCells)
                {
                    small[k] = true;
                }
            }

            int r = Math.Max(1, (int)Math.Round(WoodsWindow * 0.5f / grid.Step));
            float[] density = SmallObstacleDensity(grid, land, small, r);
            bool[] dense = new bool[n];
            for (int k = 0; k < n; k++)
            {
                if (!land[k] || float.IsNaN(density[k]))
                {
                    continue;
                }
                dense[k] = density[k] >= WoodsDensity;
            }

            // 3. mask from 1 + 2, then large no-navmesh clusters: merged into the woods, or Rough
            bool[] mask = new bool[n];
            for (int k = 0; k < n; k++)
            {
                mask[k] = inForest[k] || dense[k];
            }
            int[] index;
            List<Cluster> clusters = Clusters(f, noNavLabels, noNavSizes, l => noNavSizes[l] * cellArea >= ObstacleMinArea, null, out index);
            RayCount[] counts = CountRays(f, noNavLabels, index, clusters.Count);
            int[] overlap = new int[clusters.Count];
            for (int k = 0; k < n; k++)
            {
                int l = noNavLabels[k];
                if (l >= 0 && index[l] >= 0 && mask[k])
                {
                    overlap[index[l]]++;
                }
            }
            bool[] mergedLabel = new bool[noNavSizes.Count];
            for (int c = 0; c < clusters.Count; c++)
            {
                RayCount count = counts[c];
                BattleTerrain.CoverType cover;
                if (count.Hits == 0)
                {
                    cover = BattleTerrain.CoverType.Unknown;
                }
                else if (count.Flora >= count.Ground && count.Flora >= count.Objects)
                {
                    cover = BattleTerrain.CoverType.Flora;
                }
                else
                {
                    cover = (count.Ground >= count.Objects) ? BattleTerrain.CoverType.Terrain : BattleTerrain.CoverType.Objects;
                }
                bool mostlyTerrain = count.Ground * 2 > count.Hits;
                if (cover == BattleTerrain.CoverType.Flora || (overlap[c] > clusters[c].Cells * WoodsClusterOverlap && !mostlyTerrain))
                {
                    mergedLabel[clusters[c].Label] = true;
                    continue;
                }
                BattleTerrain.ObstacleFeature feature = new BattleTerrain.ObstacleFeature(BattleTerrain.FeatureKind.Rough);
                SetArea(f, feature, clusters[c]);
                feature.Cover = cover;
                rough.Add(feature);
            }
            for (int k = 0; k < n; k++)
            {
                int l = noNavLabels[k];
                if (l >= 0 && mergedLabel[l] && !mask[k])
                {
                    mask[k] = true;
                }
            }

            // 4. woods components: the entity veto, then the listed woods
            List<int> woodsSizes = new List<int>();
            int[] woodsLabels = LabelMask(grid, mask, woodsSizes);
            int components = woodsSizes.Count;
            int[] mapCells = new int[components];
            int[] denseCells = new int[components];
            for (int k = 0; k < n; k++)
            {
                int w = woodsLabels[k];
                if (w < 0)
                {
                    continue;
                }
                if (inForest[k])
                {
                    mapCells[w]++;
                }
                if (dense[k])
                {
                    denseCells[w]++;
                }
            }
            int[] identity = new int[components];
            for (int w = 0; w < components; w++)
            {
                identity[w] = w;
            }
            RayCount[] woodsRays = CountRays(f, woodsLabels, identity, components);
            bool[] vetoed = new bool[components];
            int vetoes = 0;
            for (int w = 0; w < components; w++)
            {
                RayCount count = woodsRays[w];
                if (mapCells[w] == 0 && count.Hits >= WoodsVetoMinHits && count.Entity * 2 > count.Hits)
                {
                    vetoed[w] = true;
                    vetoes++;
                }
            }
            int[] woodsIndex;
            List<Cluster> areas = Clusters(f, woodsLabels, woodsSizes,
                w => vetoed[w] ? woodsSizes[w] * cellArea >= ObstacleMinArea : woodsSizes[w] * cellArea >= WoodsMinArea,
                null, out woodsIndex);
            foreach (Cluster c in areas)
            {
                int w = c.Label;
                BattleTerrain.ObstacleFeature feature = new BattleTerrain.ObstacleFeature(
                    vetoed[w] ? BattleTerrain.FeatureKind.Rough : BattleTerrain.FeatureKind.Woods);
                SetArea(f, feature, c);
                if (vetoed[w])
                {
                    feature.Cover = BattleTerrain.CoverType.Objects;
                    feature.Source = BattleTerrain.AreaSource.Density;
                    rough.Add(feature);
                    continue;
                }
                float mapShare = (float)mapCells[w] / c.Cells;
                float denseShare = (float)denseCells[w] / c.Cells;
                feature.Cover = BattleTerrain.CoverType.Woods;
                feature.Importance = c.Area;
                feature.Source = (mapShare >= WoodsSourceShare && denseShare >= WoodsSourceShare) ? BattleTerrain.AreaSource.Both
                    : (mapShare >= WoodsSourceShare) ? BattleTerrain.AreaSource.Map
                    : (denseShare >= WoodsSourceShare) ? BattleTerrain.AreaSource.Density
                    : (mapShare > 0f || denseShare > 0f) ? ((mapShare >= denseShare) ? BattleTerrain.AreaSource.Map : BattleTerrain.AreaSource.Density)
                    : BattleTerrain.AreaSource.Rays;
                woods.Add(feature);
            }
            bool[] vetoedCells = (vetoes > 0) ? new bool[n] : null;
            for (int k = 0; k < n; k++)
            {
                int w = woodsLabels[k];
                if (w >= 0 && vetoed[w])
                {
                    mask[k] = false;
                    vetoedCells[k] = true;
                }
            }
            map.MaskMinX = grid.MinX;
            map.MaskMinY = grid.MinY;
            map.MaskStep = grid.Step;
            map.MaskNx = nx;
            map.MaskNy = ny;
            // published only complete (readers null-check it)
            map.Mask = mask;
            f.WoodsMask = mask;
            f.VetoedWoods = vetoedCells;

            // 5. map DifficultTerrain regions: one Rough area each
            foreach (TacticalInput t in difficult)
            {
                bool[] disc = new bool[n];
                if (MarkDisc(grid, t.Position, t.Radius, land, disc) == 0)
                {
                    continue;
                }
                List<int> discSizes = new List<int>();
                int[] discLabels = new int[n];
                int discCount = 0;
                for (int k = 0; k < n; k++)
                {
                    discLabels[k] = disc[k] ? 0 : -1;
                    discCount += disc[k] ? 1 : 0;
                }
                discSizes.Add(discCount);
                int[] discIndex;
                List<Cluster> discCluster = Clusters(f, discLabels, discSizes, l => true, null, out discIndex);
                BattleTerrain.ObstacleFeature feature = new BattleTerrain.ObstacleFeature(BattleTerrain.FeatureKind.Rough);
                SetArea(f, feature, discCluster[0]);
                int centreCell = grid.IndexOf(t.Position);
                if (centreCell >= 0 && disc[centreCell])
                {
                    // the region's own centre when it lies on land (the outline redraws the disc round Position)
                    feature.Position = t.Position;
                }
                feature.Cover = BattleTerrain.CoverType.Difficult;
                feature.Source = BattleTerrain.AreaSource.Map;
                feature.RegionRadius = t.Radius;
                rough.Add(feature);
            }

            // 6. steep walkable clusters
            bool[] steep = new bool[n];
            for (int k = 0; k < n; k++)
            {
                steep[k] = grid.Cells[k] == Cell.Open && grid.Slopes[k] > SteepHard;
            }
            List<int> steepSizes = new List<int>();
            int[] steepLabels = LabelMask(grid, steep, steepSizes);
            foreach (Cluster c in Clusters(f, steepLabels, steepSizes, l => steepSizes[l] * cellArea >= ObstacleMinArea, grid.Slopes, out index))
            {
                BattleTerrain.ObstacleFeature feature = new BattleTerrain.ObstacleFeature(BattleTerrain.FeatureKind.Rough);
                SetArea(f, feature, c);
                feature.Cover = BattleTerrain.CoverType.Steep;
                feature.MeanSlope = (c.ValueCount > 0) ? (float)(c.ValueSum / c.ValueCount) : float.NaN;
                rough.Add(feature);
            }

            DropNearEdge(f, woods, BattleTerrain.FeatureKind.Woods, dropped);
            DropNearEdge(f, rough, BattleTerrain.FeatureKind.Rough, dropped);
            Rank(woods, MaxWoods);
            Rank(rough, MaxRough);
            terrain.Woods = Finish(f, woods, BattleTerrain.FeatureKind.Woods);
            terrain.Rough = Finish(f, rough, BattleTerrain.FeatureKind.Rough);
        }

        /// <summary>Marks the land cells whose centre lies within radius of centre; returns how many (0 for a NaN / non-positive radius or centre).</summary>
        private static int MarkDisc(Grid grid, Vec2 centre, float radius, bool[] land, bool[] marks)
        {
            if (!(radius > 0f) || float.IsNaN(centre.x) || float.IsNaN(centre.y))
            {
                return 0;
            }
            int ci = (int)Math.Floor((centre.x - grid.MinX) / grid.Step);
            int cj = (int)Math.Floor((centre.y - grid.MinY) / grid.Step);
            int reach = (int)Math.Ceiling(radius / grid.Step) + 1;
            float radius2 = radius * radius;
            int marked = 0;
            for (int j = Math.Max(0, cj - reach); j <= Math.Min(grid.Ny - 1, cj + reach); j++)
            {
                for (int i = Math.Max(0, ci - reach); i <= Math.Min(grid.Nx - 1, ci + reach); i++)
                {
                    int k = j * grid.Nx + i;
                    if (land[k] && grid.CellCentre(k).DistanceSquared(centre) <= radius2)
                    {
                        marks[k] = true;
                        marked++;
                    }
                }
            }
            return marked;
        }

        /// <summary>Per land cell: small-obstacle cells / land cells in the (2r + 1)^2 square round it (summed-area tables); NaN for other cells.</summary>
        private static float[] SmallObstacleDensity(Grid grid, bool[] land, bool[] small, int r)
        {
            int nx = grid.Nx;
            int ny = grid.Ny;
            int w = nx + 1;
            int[] sumSmall = new int[w * (ny + 1)];
            int[] sumLand = new int[w * (ny + 1)];
            for (int j = 0; j < ny; j++)
            {
                int rowSmall = 0;
                int rowLand = 0;
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    rowSmall += small[k] ? 1 : 0;
                    rowLand += land[k] ? 1 : 0;
                    sumSmall[(j + 1) * w + i + 1] = sumSmall[j * w + i + 1] + rowSmall;
                    sumLand[(j + 1) * w + i + 1] = sumLand[j * w + i + 1] + rowLand;
                }
            }
            float[] density = new float[nx * ny];
            for (int j = 0; j < ny; j++)
            {
                int j0 = Math.Max(0, j - r), j1 = Math.Min(ny - 1, j + r);
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    if (!land[k])
                    {
                        density[k] = float.NaN;
                        continue;
                    }
                    int i0 = Math.Max(0, i - r), i1 = Math.Min(nx - 1, i + r);
                    int a = j0 * w + i0, b = j0 * w + i1 + 1, c = (j1 + 1) * w + i0, d = (j1 + 1) * w + i1 + 1;
                    int landCount = sumLand[d] - sumLand[b] - sumLand[c] + sumLand[a];
                    int smallCount = sumSmall[d] - sumSmall[b] - sumSmall[c] + sumSmall[a];
                    density[k] = (landCount > 0) ? (float)smallCount / landCount : float.NaN;
                }
            }
            return density;
        }

        /// <summary>The most frequent name (alphabetical on a tie); null with none.</summary>
        private static string TopName(Dictionary<string, int> names)
        {
            if (names == null)
            {
                return null;
            }
            string best = null;
            int bestCount = 0;
            foreach (KeyValuePair<string, int> entry in names)
            {
                if (entry.Value > bestCount || (entry.Value == bestCount && string.CompareOrdinal(entry.Key, best) < 0))
                {
                    best = entry.Key;
                    bestCount = entry.Value;
                }
            }
            return best;
        }

        /// <summary>Deep-water labels, their cluster index, and the listed Water feature's id per cluster (null = not listed).</summary>
        private sealed class WaterMap
        {
            public int[] Labels;
            public int[] Index;
            public string[] Ids;
        }

        /// <summary>The ford step's result: listed fords and the wet-cluster labels with the listed ford per label (null = not listed), so the choke step can name the ford a choke crosses.</summary>
        private sealed class FordMap
        {
            public List<BattleTerrain.FordFeature> Listed;
            public int[] Labels;
            public BattleTerrain.FordFeature[] ByLabel;
        }

        /// <summary>Deep-water clusters (8-connected, >= WaterMinArea): touching the field, crossing the A->D line and where first; ranked by area x Proximity, x OnAxisBonus when crossing. Returns the label map for the fords.</summary>
        private static WaterMap FindWater(Frame f, BattleTerrain terrain)
        {
            Grid grid = f.Grid;
            int n = grid.Nx * grid.Ny;
            float cellArea = grid.Step * grid.Step;
            bool[] mask = new bool[n];
            for (int k = 0; k < n; k++)
            {
                mask[k] = grid.Cells[k] == Cell.Deep;
            }
            List<int> sizes = new List<int>();
            int[] labels = LabelMask(grid, mask, sizes);
            int[] index;
            List<Cluster> clusters = Clusters(f, labels, sizes, l => sizes[l] * cellArea >= WaterMinArea, null, out index);

            float[] crossAt = new float[clusters.Count];
            for (int c = 0; c < crossAt.Length; c++)
            {
                crossAt[c] = float.NaN;
            }
            float sampleStep = grid.Step * 0.5f;
            for (float s = 0f; s <= f.Distance; s += sampleStep)
            {
                int k = grid.IndexOf(f.Atk.C2 + f.Forward * s);
                if (k < 0 || labels[k] < 0)
                {
                    continue;
                }
                int c = index[labels[k]];
                if (c >= 0 && float.IsNaN(crossAt[c]))
                {
                    crossAt[c] = s / f.Distance;
                }
            }

            List<KeyValuePair<int, BattleTerrain.WaterFeature>> waters = new List<KeyValuePair<int, BattleTerrain.WaterFeature>>();
            for (int c = 0; c < clusters.Count; c++)
            {
                BattleTerrain.WaterFeature water = new BattleTerrain.WaterFeature();
                SetArea(f, water, clusters[c]);
                water.TouchesField = clusters[c].InField;
                water.CrossesAxis = !float.IsNaN(crossAt[c]);
                water.CrossAt = crossAt[c];
                if (water.CrossesAxis)
                {
                    water.Importance *= OnAxisBonus;
                }
                waters.Add(new KeyValuePair<int, BattleTerrain.WaterFeature>(c, water));
            }
            waters.Sort((x, y) => y.Value.Importance.CompareTo(x.Value.Importance));
            if (waters.Count > MaxWater)
            {
                waters.RemoveRange(MaxWater, waters.Count - MaxWater);
            }
            List<BattleTerrain.WaterFeature> listed = new List<BattleTerrain.WaterFeature>();
            foreach (KeyValuePair<int, BattleTerrain.WaterFeature> entry in waters)
            {
                listed.Add(entry.Value);
            }
            terrain.Waters = Finish(f, listed, BattleTerrain.FeatureKind.Water);

            WaterMap map = new WaterMap { Labels = labels, Index = index, Ids = new string[clusters.Count] };
            foreach (KeyValuePair<int, BattleTerrain.WaterFeature> entry in waters)
            {
                map.Ids[entry.Key] = entry.Value.Id;
            }
            return map;
        }

        /// <summary>Wet walkable clusters (8-connected) on the A->D grid path, or bordering deep water with at least FordMinArea; ranked by area x Proximity, x OnAxisBonus on the path. water (null when that step failed) names the Water each borders. Fords near the map edge are dropped (water bodies are kept). Returns the listed fords with the wet labels for the choke step.</summary>
        private static FordMap FindFords(Frame f, WaterMap water, Dictionary<string, int> dropped)
        {
            Grid grid = f.Grid;
            int nx = grid.Nx;
            int ny = grid.Ny;
            int n = nx * ny;
            float cellArea = grid.Step * grid.Step;
            List<int> sizes = new List<int>();
            int[] labels = LabelMask(grid, grid.Wet, sizes);
            bool[] onPath = new bool[sizes.Count];
            bool[] bank = new bool[sizes.Count];
            string[] waterId = new string[sizes.Count];
            foreach (int k in f.Path)
            {
                if (labels[k] >= 0)
                {
                    onPath[labels[k]] = true;
                }
            }
            for (int k = 0; k < n; k++)
            {
                int l = labels[k];
                if (l < 0)
                {
                    continue;
                }
                int i = k % nx;
                int j = k / nx;
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + ((d == 0) ? -1 : (d == 1) ? 1 : 0);
                    int nj = j + ((d == 2) ? -1 : (d == 3) ? 1 : 0);
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= ny)
                    {
                        continue;
                    }
                    int nk = nj * nx + ni;
                    if (grid.Cells[nk] != Cell.Deep)
                    {
                        continue;
                    }
                    bank[l] = true;
                    if (waterId[l] == null && water != null && water.Labels[nk] >= 0)
                    {
                        int c = water.Index[water.Labels[nk]];
                        if (c >= 0)
                        {
                            waterId[l] = water.Ids[c];
                        }
                    }
                }
            }

            List<BattleTerrain.FordFeature> fords = new List<BattleTerrain.FordFeature>();
            BattleTerrain.FordFeature[] byLabel = new BattleTerrain.FordFeature[sizes.Count];
            int[] index;
            foreach (Cluster c in Clusters(f, labels, sizes, l => onPath[l] || (bank[l] && sizes[l] * cellArea >= FordMinArea), null, out index))
            {
                BattleTerrain.FordFeature ford = new BattleTerrain.FordFeature();
                byLabel[c.Label] = ford;
                SetArea(f, ford, c);
                ford.OnPath = onPath[c.Label];
                ford.AtDeepWater = bank[c.Label];
                ford.WaterId = waterId[c.Label];
                if (ford.OnPath)
                {
                    List<int> pathCells = new List<int>();
                    foreach (int k in f.Path)
                    {
                        if (labels[k] == c.Label)
                        {
                            pathCells.Add(k);
                        }
                    }
                    if (pathCells.Count > 0)
                    {
                        ford.Position = grid.CellCentre(pathCells[pathCells.Count / 2]);
                    }
                    ford.Importance *= OnAxisBonus;
                }
                fords.Add(ford);
            }
            DropNearEdge(f, fords, BattleTerrain.FeatureKind.Ford, dropped);
            Rank(fords, MaxFords);
            // only listed fords keep their label entry
            HashSet<BattleTerrain.FordFeature> listed = new HashSet<BattleTerrain.FordFeature>(fords);
            for (int l = 0; l < byLabel.Length; l++)
            {
                if (byLabel[l] != null && !listed.Contains(byLabel[l]))
                {
                    byLabel[l] = null;
                }
            }
            return new FordMap { Listed = fords, Labels = labels, ByLabel = byLabel };
        }

        /// <summary>One connected cluster of cells, with what the area kinds need. Distances / extents in the attacker's frame.</summary>
        private sealed class Cluster
        {
            public int Label;
            public int Cells;
            public float Area;
            public double SumX, SumY;
            public float MinAlong = float.MaxValue, MaxAlong = float.MinValue, MinAcross = float.MaxValue, MaxAcross = float.MinValue;
            /// <summary>Its cell nearest the A->D segment and that distance.</summary>
            public float AxisDistance = float.MaxValue;
            public int AxisCell = -1;
            /// <summary>Its nearest cell's distance to each army's centre.</summary>
            public float AtkDistance = float.MaxValue, DefDistance = float.MaxValue;
            /// <summary>One of its cells lies between the armies (Feature.Between's rule).</summary>
            public bool InField;
            /// <summary>Its cell nearest its centroid (always inside it).</summary>
            public int Centre = -1;
            public float CentreDistance2 = float.MaxValue;
            /// <summary>Over the optional per-cell values (NaN skipped): sum, count, maximum and where.</summary>
            public double ValueSum;
            public int ValueCount;
            public float ValueMax = float.MinValue;
            public int ValueMaxCell = -1;
        }

        /// <summary>The clusters of labels that keep accepts, with their stats; index maps a label to its position in the result (-1 = not kept).</summary>
        private static List<Cluster> Clusters(Frame f, int[] labels, List<int> sizes, Func<int, bool> keep, float[] values, out int[] index)
        {
            Grid grid = f.Grid;
            float cellArea = grid.Step * grid.Step;
            index = new int[sizes.Count];
            List<Cluster> clusters = new List<Cluster>();
            for (int l = 0; l < sizes.Count; l++)
            {
                if (keep(l))
                {
                    index[l] = clusters.Count;
                    clusters.Add(new Cluster { Label = l, Cells = sizes[l], Area = sizes[l] * cellArea });
                }
                else
                {
                    index[l] = -1;
                }
            }
            if (clusters.Count == 0)
            {
                return clusters;
            }
            int n = grid.Nx * grid.Ny;
            float half = f.ZoneWidth * 0.5f;
            for (int k = 0; k < n; k++)
            {
                int l = labels[k];
                if (l < 0 || index[l] < 0)
                {
                    continue;
                }
                Cluster c = clusters[index[l]];
                Vec2 p = grid.CellCentre(k);
                Vec2 rel = p - f.Atk.C2;
                float along = Vec2.DotProduct(rel, f.Forward);
                float across = Vec2.DotProduct(rel, f.Left);
                c.SumX += p.x;
                c.SumY += p.y;
                c.MinAlong = Math.Min(c.MinAlong, along);
                c.MaxAlong = Math.Max(c.MaxAlong, along);
                c.MinAcross = Math.Min(c.MinAcross, across);
                c.MaxAcross = Math.Max(c.MaxAcross, across);
                float clamped = Math.Max(0f, Math.Min(f.Distance, along));
                float axis = p.Distance(f.Atk.C2 + f.Forward * clamped);
                if (axis < c.AxisDistance)
                {
                    c.AxisDistance = axis;
                    c.AxisCell = k;
                }
                c.AtkDistance = Math.Min(c.AtkDistance, p.Distance(f.Atk.C2));
                c.DefDistance = Math.Min(c.DefDistance, p.Distance(f.Def.C2));
                if (along >= 0f && along <= f.Distance && Math.Abs(across) <= half)
                {
                    c.InField = true;
                }
                if (values != null)
                {
                    float v = values[k];
                    if (!float.IsNaN(v))
                    {
                        c.ValueSum += v;
                        c.ValueCount++;
                        if (v > c.ValueMax)
                        {
                            c.ValueMax = v;
                            c.ValueMaxCell = k;
                        }
                    }
                }
            }
            for (int k = 0; k < n; k++)
            {
                int l = labels[k];
                if (l < 0 || index[l] < 0)
                {
                    continue;
                }
                Cluster c = clusters[index[l]];
                Vec2 centroid = new Vec2((float)(c.SumX / c.Cells), (float)(c.SumY / c.Cells));
                float d2 = grid.CellCentre(k).DistanceSquared(centroid);
                if (d2 < c.CentreDistance2)
                {
                    c.CentreDistance2 = d2;
                    c.Centre = k;
                }
            }
            return clusters;
        }

        /// <summary>An area feature's common fields from its cluster; Importance = area × Proximity.</summary>
        private static void SetArea(Frame f, BattleTerrain.AreaFeature x, Cluster c)
        {
            Grid grid = f.Grid;
            x.Position = grid.CellCentre(c.Centre);
            x.Area = c.Area;
            x.Length = c.MaxAlong - c.MinAlong + grid.Step;
            x.Width = c.MaxAcross - c.MinAcross + grid.Step;
            x.AxisDistance = c.AxisDistance;
            x.ClosestPoint = grid.CellCentre(c.AxisCell);
            x.Importance = c.Area * Proximity(c.AxisDistance);
        }

        /// <summary>Labels the 8-connected clusters of <paramref name="mask"/> cells (-1 = not in the mask); <paramref name="sizes"/> gets their cell counts.</summary>
        private static int[] LabelMask(Grid grid, bool[] mask, List<int> sizes)
        {
            int nx = grid.Nx;
            int ny = grid.Ny;
            int n = nx * ny;
            int[] labels = new int[n];
            for (int k = 0; k < n; k++)
            {
                labels[k] = -1;
            }
            int[] stack = new int[n];
            for (int k = 0; k < n; k++)
            {
                if (labels[k] >= 0 || !mask[k])
                {
                    continue;
                }
                int label = sizes.Count;
                int count = 0;
                int top = 0;
                labels[k] = label;
                stack[top++] = k;
                while (top > 0)
                {
                    int c = stack[--top];
                    count++;
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
                            if (labels[nk] < 0 && mask[nk])
                            {
                                labels[nk] = label;
                                stack[top++] = nk;
                            }
                        }
                    }
                }
                sizes.Add(count);
            }
            return labels;
        }
    }
}
