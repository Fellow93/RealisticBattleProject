using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// Reads the battlefield so tactics can weigh the ground into the typed BattleTerrain snapshot, published by
    /// BattleTerrainLogic as BattleTerrain.Current. This class computes; it never formats (BattleTerrainLogFormat does).
    ///
    /// - Build (main thread, Gather.cs) grids the whole playable map (4 m or coarser, under MaxCells): walkable / wet /
    ///   no-navmesh / deep water per cell with terrain height, slope, summed-area table, clearance map and edge distance.
    ///   Army-independent, so built in AfterStart where the cost hides in the mission load.
    /// - Gather (main thread) runs once the armies are placed and copies every engine value into an immutable Inputs bundle.
    /// - Compute (this file and the group files, on a background task) reads only the grid and that bundle: no Mission,
    ///   Agent, Scene, MBRandom or other engine call, only managed math. Frame: attacker's centre A looking at defender's
    ///   centre D; left / right are the attacker's. Base part measures the field strip's clutter / deep / slope tallies; the
    ///   group files fill one group each (Space, Open, Mounted, Sight), then Features, Spots, Cavalry, ChokeHold,
    ///   BattlePosition, Approach and (developer mode only) Outlines.
    /// </summary>
    internal static partial class BattleTerrainRead
    {
        private const float GridStep = 4f;
        /// <summary>The grid step grows past GridStep to stay under this many cells (5 engine reads each).</summary>
        private const int MaxCells = 80000;
        private const float ZoneMargin = 50f;
        private const float ZoneWidthPerFrontage = 1.5f;
        /// <summary>Wide enough to judge room for flanking even when both armies stand in narrow blocks.</summary>
        private const float ZoneMinWidth = 300f;
        private const float ZoneMaxWidth = 600f;
        private const float SteepGentle = 15f;
        private const float SteepHard = 25f;
        private const float FrontBlockedRun = 6f; // m of no walkable ground that ends a side of the front
        private const float NavQueryLift = 0.3f; // m above the terrain, as in DeepWaterNavBlocker
        private const float SnapRadius = 40f; // m; an army centre off walkable ground moves to the nearest walkable cell within this
        /// <summary>Places closer than this (m) to the map edge are not used (men there are pushed back and flee). Applies to the feature map (water and flanks kept), archer spots, cavalry points and (at half the margin) flank routes. Raw open-land tallies still count the whole map.</summary>
        internal const float EdgeMargin = 60f;

        internal enum Cell : byte
        {
            Out,
            Open,
            NoNav,
            Deep
        }

        /// <summary>The whole-map grid. Immutable once built.</summary>
        internal sealed class Grid
        {
            public string Scene;
            public string Bounds;
            public float MinX;
            public float MinY;
            public float Step;
            public int Nx;
            public int Ny;
            public Cell[] Cells;
            public float[] Heights;
            /// <summary>Walkable (Open) cells under shallow water (ProbeWet): fords, marsh.</summary>
            public bool[] Wet;
            /// <summary>Walkable cells on a bridge deck over deep water (a cell whose square holds a blocker-grid bridge cell with a navmeshed deck, else the own probe), plus a wet cell walkable only on a deck joined to those (a bridge's shallow end). Their height is the deck's. May be null.</summary>
            public bool[] Bridge;
            /// <summary>Where deep water and bridges came from and the tallies of TERRAIN_WATER; null on an old grid.</summary>
            public WaterRead Water;
            /// <summary>Degrees; NaN unless the cell is walkable with four plausible neighbours.</summary>
            public float[] Slopes;
            /// <summary>Summed-area tables, (Nx + 1) x (Ny + 1), of walkable heights and their count.</summary>
            public double[] SumHeights;
            public int[] SumCounts;
            /// <summary>Per walkable cell: exact distance (m) from its centre to the nearest non-walkable cell's centre (NoNav, Deep, Out; the ring beyond the grid counts as Out); 0 for non-walkable cells. Centre distances, so free ground's edge lies about half a step closer than the value.</summary>
            public float[] Clearance;
            /// <summary>Per cell: exact distance (m) to the nearest Out cell's centre (the clearance transform with Out as the only obstacle); 0 for Out cells.</summary>
            public float[] EdgeDistance;
            /// <summary>Whole build (ms), both transforms included; the clearance / edge transform alone (ms).</summary>
            public long BuildMs;
            public float ClearMs;
            public float EdgeMs;

            public int IndexOf(Vec2 p)
            {
                if (float.IsNaN(p.x) || float.IsNaN(p.y))
                {
                    return -1;
                }
                int i = (int)Math.Floor((p.x - MinX) / Step);
                int j = (int)Math.Floor((p.y - MinY) / Step);
                if (i < 0 || j < 0 || i >= Nx || j >= Ny)
                {
                    return -1;
                }
                return j * Nx + i;
            }

            public float HeightAt(Vec2 p)
            {
                int k = IndexOf(p);
                return (k < 0) ? float.NaN : Heights[k];
            }

            public bool IsWalkable(int k)
            {
                return k >= 0 && Cells[k] == Cell.Open;
            }

            public float SlopeAt(Vec2 p)
            {
                int k = IndexOf(p);
                return (k < 0) ? float.NaN : Slopes[k];
            }

            public bool IsWet(Vec2 p)
            {
                int k = IndexOf(p);
                return k >= 0 && Wet[k];
            }

            /// <summary>Cell <paramref name="k"/> is a bridge deck over deep water (false outside the grid or without the array).</summary>
            public bool IsBridge(int k)
            {
                return k >= 0 && Bridge != null && Bridge[k];
            }

            /// <summary>Distance (m) of the cell under p to the map edge; 0 outside the grid (or before the transform).</summary>
            public float EdgeDistanceAt(Vec2 p)
            {
                int k = IndexOf(p);
                return (k < 0 || EdgeDistance == null) ? 0f : EdgeDistance[k];
            }

            /// <summary>Cell <paramref name="k"/> lies within EdgeMargin of the map edge (true outside the grid).</summary>
            public bool NearEdge(int k)
            {
                return NearEdge(k, EdgeMargin);
            }

            /// <summary>Cell <paramref name="k"/> lies within <paramref name="margin"/> (m) of the map edge (true outside the grid).</summary>
            public bool NearEdge(int k, float margin)
            {
                return k < 0 || (EdgeDistance != null && EdgeDistance[k] < margin);
            }

            public bool NearEdge(Vec2 p)
            {
                return NearEdge(IndexOf(p));
            }

            public Vec2 CellCentre(int k)
            {
                return new Vec2(MinX + (k % Nx + 0.5f) * Step, MinY + (k / Nx + 0.5f) * Step);
            }

            /// <summary>Terrain height between cell centres (bilinear); the nearest cell's where a corner has none.</summary>
            public float SampleHeight(Vec2 p)
            {
                float fx = (p.x - MinX) / Step - 0.5f;
                float fy = (p.y - MinY) / Step - 0.5f;
                int i0 = (int)Math.Floor(fx);
                int j0 = (int)Math.Floor(fy);
                if (i0 < 0 || j0 < 0 || i0 + 1 >= Nx || j0 + 1 >= Ny)
                {
                    return HeightAt(p);
                }
                int k = j0 * Nx + i0;
                float h00 = Heights[k], h10 = Heights[k + 1], h01 = Heights[k + Nx], h11 = Heights[k + Nx + 1];
                if (!IsPlausible(h00) || !IsPlausible(h10) || !IsPlausible(h01) || !IsPlausible(h11))
                {
                    return HeightAt(p);
                }
                float tx = fx - i0;
                float ty = fy - j0;
                return (h00 * (1f - tx) + h10 * tx) * (1f - ty) + (h01 * (1f - tx) + h11 * tx) * ty;
            }

            /// <summary>Mean walkable height in the (2r + 1)-cell square around cell (i, j); NaN with none.</summary>
            public float BoxMean(int i, int j, int r)
            {
                int i0 = Math.Max(0, i - r), j0 = Math.Max(0, j - r);
                int i1 = Math.Min(Nx - 1, i + r), j1 = Math.Min(Ny - 1, j + r);
                if (i0 > i1 || j0 > j1)
                {
                    return float.NaN;
                }
                int w = Nx + 1;
                int a = j0 * w + i0, b = j0 * w + i1 + 1, c = (j1 + 1) * w + i0, d = (j1 + 1) * w + i1 + 1;
                int count = SumCounts[d] - SumCounts[b] - SumCounts[c] + SumCounts[a];
                return (count > 0) ? (float)((SumHeights[d] - SumHeights[b] - SumHeights[c] + SumHeights[a]) / count) : float.NaN;
            }
        }

        /// <summary>How Build classified deep water and bridges. Written once in Build (main thread), read only after it.</summary>
        internal sealed class WaterRead
        {
            /// <summary>"blocker" (DeepWaterNavBlocker's grid decided) or "probe" (this grid's own ProbeCell).</summary>
            public string Source;
            /// <summary>The blocker grid's step (m), deep cells and bridge cells; 0 without it.</summary>
            public float BlockerStep;
            public int BlockerDeepCells;
            public int BlockerBridgeCells;
            /// <summary>This grid's Deep cells and Bridge cells.</summary>
            public int DeepCells;
            public int BridgeCells;
            /// <summary>What this grid's own probe said: deep, bridge.</summary>
            public int ProbeDeepCells;
            public int ProbeBridgeCells;
            /// <summary>Cells where the probe's deep and the blocker cell disagree.</summary>
            public int DeepDisagree;
            /// <summary>Bridge squares whose deck had no navmesh face.</summary>
            public int DeckMissing;
            /// <summary>Bridge cells whose deck face the blocker switched off (the AI cannot path over it).</summary>
            public int DeckFacesOff;
            /// <summary>Wet cells walkable only on a deck above them, and of them those joined to a deck over deep water (made bridge cells).</summary>
            public int DeckShallow;
            public int DeckShallowBridge;
        }

        /// <summary>Clutter / deep water / slope tallies of the field between the armies.</summary>
        private sealed class Area
        {
            public int Cells, InBounds, NoNav, Deep, Sloped, Gentle, Hard;
            public double SlopeSum;

            public void Add(Cell cell, float slopeDegrees)
            {
                Cells++;
                if (cell == Cell.Out)
                {
                    return;
                }
                InBounds++;
                if (cell == Cell.NoNav)
                {
                    NoNav++;
                }
                else if (cell == Cell.Deep)
                {
                    Deep++;
                }
                if (!float.IsNaN(slopeDegrees))
                {
                    Sloped++;
                    SlopeSum += slopeDegrees;
                    if (slopeDegrees > SteepGentle)
                    {
                        Gentle++;
                    }
                    if (slopeDegrees > SteepHard)
                    {
                        Hard++;
                    }
                }
            }

            public float Clutter => Share(NoNav, InBounds - Deep);

            public float DeepShare => Share(Deep, InBounds);

            public float SteepShare => Share(Gentle, Sloped);

            public BattleTerrain.AreaStats ToStats()
            {
                return new BattleTerrain.AreaStats(Share(Cells - InBounds, Cells), Clutter, DeepShare,
                    Sloped > 0 ? (float)(SlopeSum / Sloped) : float.NaN, SteepShare, Share(Hard, Sloped));
            }
        }

        /// <summary>One army as the read sees it.</summary>
        private sealed class Side
        {
            public int Men, Foot, Mounted, Ranged;
            public double FootSpeedSum, MountedSpeedSum;
            public Vec3 Centre;
            public Vec2 C2;
            public float Frontage;
            /// <summary>Unit vector toward the enemy's centre.</summary>
            public Vec2 Toward;
            /// <summary>The walkable cell the centre snapped to (-1 = none within SnapRadius).</summary>
            public int Snap = -1;
            public Vec2 SnapPos;
            public Side Enemy;
            /// <summary>Direction the army's line runs across (unit, frame's Left way), along which cavalry lane aims spread; Invalid = the frame's Left. A re-plan sets it from the main formation's facing.</summary>
            public Vec2 Across = Vec2.Invalid;
            /// <summary>Where the groups put what they measure for this army (null in a re-plan).</summary>
            public BattleTerrain.SideInfo Info;

            public Side(SideInput input)
            {
                Men = input.Points.Length;
                Foot = input.Foot;
                Mounted = input.Mounted;
                Ranged = input.Ranged;
                FootSpeedSum = input.FootSpeedSum;
                MountedSpeedSum = input.MountedSpeedSum;
            }

            /// <summary>A re-plan's army: the read's counts and paces from the published SideInfo.</summary>
            public Side(BattleTerrain.SideInfo info)
            {
                Men = info.Men;
                Foot = info.Foot;
                Mounted = info.Mounted;
                Ranged = info.Ranged;
                FootSpeedSum = (info.Foot > 0 && !float.IsNaN(info.FootSpeed)) ? (double)info.FootSpeed * info.Foot : 0.0;
                MountedSpeedSum = (info.Mounted > 0 && !float.IsNaN(info.MountedSpeed)) ? (double)info.MountedSpeed * info.Mounted : 0.0;
            }

            public float FootSpeed => (Foot > 0) ? (float)(FootSpeedSum / Foot) : float.NaN;

            public float MountedSpeed => (Mounted > 0) ? (float)(MountedSpeedSum / Mounted) : float.NaN;

            /// <summary>Walking pace: the foot mean, the riders' when nobody is on foot.</summary>
            public float WalkSpeed => (Foot > 0) ? FootSpeed : MountedSpeed;

            /// <summary>Flanking pace: the riders' mean, the foot mean when there are no riders.</summary>
            public float RideSpeed => (Mounted > 0) ? MountedSpeed : FootSpeed;

            /// <summary>The snapped centre when there is one, the real centre otherwise.</summary>
            public Vec2 Origin => (Snap >= 0) ? SnapPos : C2;
        }

        /// <summary>Everything the groups share. Values one group leaves for a later one stay NaN if it failed.</summary>
        private sealed class Frame
        {
            /// <summary>The main thread's gather: the only source of anything that came from the engine.</summary>
            public Inputs Input;
            public Grid Grid;
            /// <summary>The snapshot being filled; published only after the last group.</summary>
            public BattleTerrain Terrain;
            public Side Atk;
            public Side Def;
            public float Distance;
            public Vec2 Forward;
            public Vec2 Left;
            public float ZoneWidth;
            public PathSearch Search;

            // left by Space for later groups: the shortest grid path A -> D
            public readonly List<int> Path = new List<int>();

            // left by Open for Features: 8-connected regions of clearance >= 50 m (-1 = none) and their cell counts
            public int[] OpenLabels;
            public List<int> OpenSizes;

            // left by Features for Outlines: open-field labels with near-edge cells taken out before labelling (-1 = none)
            public int[] OpenFieldLabels;

            // left by Features for Spots / Cavalry / Outlines: the woods mask (null when the cover step failed) and the cells of density areas its rays vetoed
            public bool[] WoodsMask;
            public bool[] VetoedWoods;

            public float WiderFrontage => Math.Max(1f, Math.Max(Atk.Frontage, Def.Frontage));

            public bool InField(Vec2 p)
            {
                return InStrip(p, Atk.C2, Forward, Left, Distance, ZoneWidth);
            }
        }

        /// <summary>In the field strip: from ZoneMargin behind A to ZoneMargin beyond D along the axis, zoneWidth wide.</summary>
        private static bool InStrip(Vec2 p, Vec2 a2, Vec2 forward, Vec2 left, float distance, float zoneWidth)
        {
            Vec2 rel = p - a2;
            float along = Vec2.DotProduct(rel, forward);
            return along >= -ZoneMargin && along <= distance + ZoneMargin && Math.Abs(Vec2.DotProduct(rel, left)) <= zoneWidth * 0.5f;
        }

        /// <summary>
        /// Frame of a live re-plan (TerrainCavalry / TerrainSpots). BACKGROUND THREAD: built from the published snapshot and
        /// the armies' centres now (snapped; line height = grid ground there, where the read used the men's mean height).
        /// Kept from the read: frontages, men, paces, field width. attackerAcross / defenderAcross = direction each line runs
        /// now (Invalid = across the axis). Only groups that read the frame alone (FindSpots, FindCavalry) may run on it;
        /// nothing on it is published. Null when the centres coincide.
        /// </summary>
        private static Frame ReplanFrame(BattleTerrain terrain, Grid grid, Vec2 attacker, Vec2 defender, Vec2 attackerAcross, Vec2 defenderAcross)
        {
            Vec2 forward = defender - attacker;
            float distance = forward.Normalize();
            if (!(distance >= 1f) || terrain.Attacker == null || terrain.Defender == null)
            {
                return null;
            }
            Vec2 left = new Vec2(-forward.y, forward.x);
            Side atk = ReplanSide(grid, terrain.Attacker, attacker, forward, attackerAcross, left);
            Side def = ReplanSide(grid, terrain.Defender, defender, -forward, defenderAcross, left);
            atk.Enemy = def;
            def.Enemy = atk;
            float zoneWidth = terrain.FieldWidth;
            if (float.IsNaN(zoneWidth) || zoneWidth <= 0f)
            {
                zoneWidth = ZoneMinWidth;
            }
            return new Frame
            {
                Grid = grid,
                Terrain = terrain,
                Atk = atk,
                Def = def,
                Distance = distance,
                Forward = forward,
                Left = left,
                ZoneWidth = zoneWidth,
                Search = new PathSearch(grid),
                WoodsMask = terrain.WoodsMap.Mask
            };
        }

        private static Side ReplanSide(Grid grid, BattleTerrain.SideInfo info, Vec2 centre, Vec2 toward, Vec2 across, Vec2 left)
        {
            Side side = new Side(info);
            side.C2 = centre;
            side.Frontage = info.Frontage;
            side.Toward = toward;
            side.Snap = Snap(grid, centre, SnapRadius);
            if (side.Snap >= 0)
            {
                side.SnapPos = grid.CellCentre(side.Snap);
            }
            float h = grid.SampleHeight(side.Origin);
            if (!IsPlausible(h))
            {
                h = grid.HeightAt(side.Origin);
            }
            side.Centre = new Vec3(centre, IsPlausible(h) ? h : info.Centroid.z);
            if (across.IsValid && across.LengthSquared > 0.25f)
            {
                Vec2 a = across.Normalized();
                side.Across = (Vec2.DotProduct(a, left) < 0f) ? -a : a;
            }
            return side;
        }

        /// <summary>The typed snapshot (not yet published) from the grid and the gather. BACKGROUND THREAD: reads only input and its grid and allocates managed objects; never call the engine or touch Mission/Agent/Scene/MBRandom here or from a group.</summary>
        public static BattleTerrain Compute(Inputs input)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            Grid grid = input.Grid;
            Side atk = new Side(input.Atk);
            Side def = new Side(input.Def);
            Vec3 a = input.AtkCentre;
            Vec3 d = input.DefCentre;
            Vec2 a2 = a.AsVec2;
            Vec2 d2 = d.AsVec2;
            float distance = input.Distance;
            Vec2 forward = input.Forward;
            Vec2 left = input.Left;
            float attackerFrontage = input.AtkFrontage;
            float defenderFrontage = input.DefFrontage;

            // the field strip's tallies
            float zoneWidth = input.ZoneWidth;
            Area field = new Area();
            float step = grid.Step;
            for (int j = 0; j < grid.Ny; j++)
            {
                for (int i = 0; i < grid.Nx; i++)
                {
                    int k = j * grid.Nx + i;
                    Vec2 rel = new Vec2(grid.MinX + (i + 0.5f) * step, grid.MinY + (j + 0.5f) * step) - a2;
                    float along = Vec2.DotProduct(rel, forward);
                    float across = Vec2.DotProduct(rel, left);
                    if (along >= -ZoneMargin && along <= distance + ZoneMargin && Math.Abs(across) <= zoneWidth * 0.5f)
                    {
                        field.Add(grid.Cells[k], grid.Slopes[k]);
                    }
                }
            }

            BattleTerrain terrain = new BattleTerrain(grid)
            {
                ComputedAt = input.MissionTime,
                AfterDeployment = input.AfterDeployment,
                MainMs = input.MainMs,
                RayMs = input.RayMs,
                Distance = distance,
                Forward = forward,
                Left = left,
                HeightDifference = d.z - a.z,
                FieldWidth = zoneWidth,
                Field = field.ToStats()
            };
            atk.Info = terrain.Attacker;
            def.Info = terrain.Defender;

            // the groups: each times itself; one that throws records its error without stopping the others
            atk.Centre = a;
            atk.C2 = a2;
            atk.Frontage = attackerFrontage;
            atk.Toward = forward;
            atk.Enemy = def;
            def.Centre = d;
            def.C2 = d2;
            def.Frontage = defenderFrontage;
            def.Toward = -forward;
            def.Enemy = atk;
            Frame frame = new Frame
            {
                Input = input,
                Grid = grid,
                Terrain = terrain,
                Atk = atk,
                Def = def,
                Distance = distance,
                Forward = forward,
                Left = left,
                ZoneWidth = zoneWidth,
                Search = new PathSearch(grid)
            };
            atk.Snap = input.AtkSnap;
            def.Snap = input.DefSnap;
            foreach (Side side in new[] { atk, def })
            {
                if (side.Snap >= 0)
                {
                    side.SnapPos = grid.CellCentre(side.Snap);
                }
                BattleTerrain.SideInfo info = side.Info;
                info.Centroid = side.Centre;
                info.HasSnap = side.Snap >= 0;
                info.SnapPoint = side.SnapPos;
                info.Frontage = side.Frontage;
                info.Men = side.Men;
                info.Foot = side.Foot;
                info.Mounted = side.Mounted;
                info.Ranged = side.Ranged;
                info.FootSpeed = side.FootSpeed;
                info.MountedSpeed = side.MountedSpeed;
            }
            RunGroup(frame, terrain.Space, ReadSpace);
            RunGroup(frame, terrain.Open, ReadOpen);
            RunGroup(frame, terrain.Mounted, ReadMounted);
            RunGroup(frame, terrain.Sight, ReadSight);
            // the feature map reads what every group above left; a step that fails leaves its kind empty
            RunGroup(frame, terrain.FeatureMap, ReadFeatures);
            // order matters: spots read the feature map; cavalry, choke hold and battle position read spots / chokes; outlines read all
            RunGroup(frame, terrain.Spots, ReadSpots);
            RunGroup(frame, terrain.Cavalry, ReadCavalry);
            RunGroup(frame, terrain.ChokeHold, ReadChokeHold);
            RunGroup(frame, terrain.BattlePosition, ReadBattlePosition);
            // reads the battle position, choke hold and spots
            RunGroup(frame, terrain.Approach, ReadApproach);
            // display data for the developer-mode map view only
            if (input.Outlines)
            {
                RunGroup(frame, terrain.Outlines, ReadOutlines);
            }
            terrain.BackgroundMs = (DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
            return terrain;
        }

        private static void RunGroup(Frame frame, BattleTerrain.Group status, Action<Frame> group)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            try
            {
                group(frame);
            }
            catch (Exception e)
            {
                status.Error = e.GetType().Name + ":" + Sanitize(e.Message);
            }
            status.Ms = (float)(DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
        }

        /// <summary>Walkable width across the axis through centre: each half and where it ended.</summary>
        private static BattleTerrain.Width FrontWidth(Grid grid, Vec2 centre, Vec2 left)
        {
            BattleTerrain.End leftEnd, rightEnd;
            float l = HalfWidth(grid, centre, left, out leftEnd);
            float r = HalfWidth(grid, centre, -left, out rightEnd);
            return new BattleTerrain.Width(l, leftEnd, r, rightEnd);
        }

        private static float FullWidth(Grid grid, Vec2 centre, Vec2 left)
        {
            BattleTerrain.End end;
            return HalfWidth(grid, centre, left, out end) + HalfWidth(grid, centre, -left, out end);
        }

        /// <summary>Walks the grid from the centre until a blocked run; leaving the grid counts as the map edge.</summary>
        private static float HalfWidth(Grid grid, Vec2 centre, Vec2 direction, out BattleTerrain.End end)
        {
            return Walk(grid, centre, direction, (grid.Nx + grid.Ny) * grid.Step, float.NaN, out end);
        }

        /// <summary>Walks from start along direction (unit) and returns how far the ground stays good (walkable and, with maxSlope set, flatter). A bad run of FrontBlockedRun m ends it; leaving the grid ends it at once. end = edge | water | blocked | steep (the bad run's first cell) | max (reached limit).</summary>
        private static float Walk(Grid grid, Vec2 start, Vec2 direction, float limit, float maxSlope, out BattleTerrain.End end)
        {
            float step = grid.Step;
            float run = 0f;
            BattleTerrain.End runEnd = BattleTerrain.End.Blocked;
            for (float s = 0f; s <= limit; s += step)
            {
                int k = grid.IndexOf(start + direction * s);
                Cell cell = (k < 0) ? Cell.Out : grid.Cells[k];
                if (cell == Cell.Open && (float.IsNaN(maxSlope) || !(grid.Slopes[k] >= maxSlope)))
                {
                    run = 0f;
                    continue;
                }
                if (run == 0f)
                {
                    runEnd = (cell == Cell.Out) ? BattleTerrain.End.Edge : (cell == Cell.Deep) ? BattleTerrain.End.Water
                        : (cell == Cell.NoNav) ? BattleTerrain.End.Blocked : BattleTerrain.End.Steep;
                }
                run += step;
                if (run >= FrontBlockedRun || cell == Cell.Out)
                {
                    end = (cell == Cell.Out) ? BattleTerrain.End.Edge : runEnd;
                    return Math.Max(0f, s + step - run);
                }
            }
            end = BattleTerrain.End.Max;
            return limit;
        }

        /// <summary>The walkable cell nearest to p within radius; -1 if none.</summary>
        internal static int Snap(Grid grid, Vec2 p, float radius)
        {
            int k = grid.IndexOf(p);
            if (grid.IsWalkable(k))
            {
                return k;
            }
            if (float.IsNaN(p.x) || float.IsNaN(p.y))
            {
                return -1;
            }
            int ci = (int)Math.Floor((p.x - grid.MinX) / grid.Step);
            int cj = (int)Math.Floor((p.y - grid.MinY) / grid.Step);
            int r = (int)Math.Ceiling(radius / grid.Step);
            int best = -1;
            float bestDistance = radius;
            for (int j = Math.Max(0, cj - r); j <= Math.Min(grid.Ny - 1, cj + r); j++)
            {
                for (int i = Math.Max(0, ci - r); i <= Math.Min(grid.Nx - 1, ci + r); i++)
                {
                    int c = j * grid.Nx + i;
                    if (grid.Cells[c] != Cell.Open)
                    {
                        continue;
                    }
                    float dist = grid.CellCentre(c).Distance(p);
                    if (dist <= bestDistance)
                    {
                        bestDistance = dist;
                        best = c;
                    }
                }
            }
            return best;
        }

        private static void ComputeClearance(Grid grid)
        {
            grid.Clearance = DistanceTransform(grid, cell => cell != Cell.Open);
        }

        /// <summary>Fills Grid.EdgeDistance: distance of every cell to the nearest Out cell; the clearance transform with Out as the only obstacle.</summary>
        private static void ComputeEdgeDistance(Grid grid)
        {
            grid.EdgeDistance = DistanceTransform(grid, cell => cell == Cell.Out);
        }

        /// <summary>Per cell, distance (m) to the nearest obstacle cell's centre (0 for obstacles): exact Euclidean distance transform of Felzenszwalb and Huttenlocher, O(cells). The grid is padded by one ring of obstacle cells, so the map's edge counts as one. Pure math.</summary>
        private static float[] DistanceTransform(Grid grid, Func<Cell, bool> obstacle)
        {
            int nx = grid.Nx;
            int ny = grid.Ny;
            int longest = Math.Max(nx, ny) + 2;
            double[] f = new double[longest];
            double[] d = new double[longest];
            int[] v = new int[longest];
            double[] z = new double[longest + 1];
            double[] columnSq = new double[nx * ny];
            float[] distance = new float[nx * ny];

            // columns: squared distance (cells) to the nearest obstacle in the column, padding row at each end
            for (int i = 0; i < nx; i++)
            {
                f[0] = 0.0;
                f[ny + 1] = 0.0;
                for (int j = 0; j < ny; j++)
                {
                    f[j + 1] = obstacle(grid.Cells[j * nx + i]) ? 0.0 : double.PositiveInfinity;
                }
                SquaredDistance1D(f, ny + 2, d, v, z);
                for (int j = 0; j < ny; j++)
                {
                    columnSq[j * nx + i] = d[j + 1];
                }
            }
            // rows: combine the column results; the padding columns are obstacles all the way down
            for (int j = 0; j < ny; j++)
            {
                f[0] = 0.0;
                f[nx + 1] = 0.0;
                for (int i = 0; i < nx; i++)
                {
                    f[i + 1] = columnSq[j * nx + i];
                }
                SquaredDistance1D(f, nx + 2, d, v, z);
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    distance[k] = obstacle(grid.Cells[k]) ? 0f : (float)Math.Sqrt(d[i + 1]) * grid.Step;
                }
            }
            return distance;
        }

        /// <summary>One 1D pass: d[q] = min over p of (q - p)^2 + f[p]. Only finite f[p] build the envelope (f[0] is always 0, the padding), keeping the intersections exact.</summary>
        private static void SquaredDistance1D(double[] f, int n, double[] d, int[] v, double[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                if (double.IsPositiveInfinity(f[q]))
                {
                    continue;
                }
                // z[0] is -infinity: stops at k = 0 at the latest
                double s = Intersection(f, q, v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = Intersection(f, q, v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q)
                {
                    k++;
                }
                double dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        /// <summary>Where the parabolas rooted at q and p (p &lt; q) cross.</summary>
        private static double Intersection(double[] f, int q, int p)
        {
            return ((f[q] + (double)q * q) - (f[p] + (double)p * p)) / (2.0 * (q - p));
        }

        /// <summary>Labels the 4-connected walkable components (-1 = not walkable); sizes gets their cell counts.</summary>
        private static int[] LabelComponents(Grid grid, List<int> sizes)
        {
            int n = grid.Nx * grid.Ny;
            int[] labels = new int[n];
            for (int k = 0; k < n; k++)
            {
                labels[k] = -1;
            }
            int[] stack = new int[n];
            for (int k = 0; k < n; k++)
            {
                if (labels[k] >= 0 || grid.Cells[k] != Cell.Open)
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
                    int i = c % grid.Nx;
                    int j = c / grid.Nx;
                    if (i > 0) Visit(grid, labels, stack, ref top, c - 1, label);
                    if (i < grid.Nx - 1) Visit(grid, labels, stack, ref top, c + 1, label);
                    if (j > 0) Visit(grid, labels, stack, ref top, c - grid.Nx, label);
                    if (j < grid.Ny - 1) Visit(grid, labels, stack, ref top, c + grid.Nx, label);
                }
                sizes.Add(count);
            }
            return labels;
        }

        private static void Visit(Grid grid, int[] labels, int[] stack, ref int top, int c, int label)
        {
            if (labels[c] < 0 && grid.Cells[c] == Cell.Open)
            {
                labels[c] = label;
                stack[top++] = c;
            }
        }

        /// <summary>A* over walkable cells, 8 neighbours, no corner cutting, octile heuristic. Scratch arrays are allocated once and reset by a run stamp. An instance is never shared between threads.</summary>
        internal sealed class PathSearch
        {
            private const float Diagonal = 1.41421356f;
            private readonly Grid _grid;
            private readonly float[] _g;
            private readonly int[] _parent;
            private readonly int[] _seen;
            private readonly int[] _closed;
            private readonly int[] _passStamp;
            private readonly bool[] _pass;
            private int _run;
            private int[] _heapCell = new int[4096];
            private float[] _heapKey = new float[4096];
            private int _heapCount;

            public int Searches;
            public long Expanded;
            /// <summary>The last Find stopped at its maxExpanded cap (and returned NaN).</summary>
            public bool Capped;

            public PathSearch(Grid grid)
            {
                _grid = grid;
                int n = grid.Nx * grid.Ny;
                _g = new float[n];
                _parent = new int[n];
                _seen = new int[n];
                _closed = new int[n];
                _passStamp = new int[n];
                _pass = new bool[n];
            }

            /// <summary>Shortest walkable path length (m) from start to goal, NaN when none. blocked closes more cells (never the goal); path gets the cells, start first; cost (per cell, each >= 1 so the heuristic stays admissible) multiplies every move INTO that cell (the result is then a cost, not a length); maxExpanded (> 0) stops the search: NaN and Capped set.</summary>
            public float Find(int start, int goal, Func<int, bool> blocked, List<int> path, float[] cost = null, int maxExpanded = 0)
            {
                path?.Clear();
                Capped = false;
                if (start < 0 || goal < 0 || !_grid.IsWalkable(goal))
                {
                    return float.NaN;
                }
                _run++;
                Searches++;
                _heapCount = 0;
                int expandedHere = 0;
                int nx = _grid.Nx;
                int ny = _grid.Ny;
                float step = _grid.Step;
                float diagonal = step * Diagonal;
                int gi = goal % nx;
                int gj = goal / nx;
                _seen[start] = _run;
                _g[start] = 0f;
                _parent[start] = -1;
                Push(start, Heuristic(start % nx, start / nx, gi, gj, step));
                while (_heapCount > 0)
                {
                    int k = Pop();
                    if (_closed[k] == _run)
                    {
                        continue;
                    }
                    _closed[k] = _run;
                    Expanded++;
                    if (maxExpanded > 0 && ++expandedHere > maxExpanded)
                    {
                        Capped = true;
                        return float.NaN;
                    }
                    if (k == goal)
                    {
                        if (path != null)
                        {
                            for (int c = goal; c >= 0; c = _parent[c])
                            {
                                path.Add(c);
                            }
                            path.Reverse();
                        }
                        return _g[goal];
                    }
                    int i = k % nx;
                    int j = k / nx;
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
                            if (_closed[nk] == _run || !Passable(nk, goal, blocked))
                            {
                                continue;
                            }
                            bool diagonalMove = di != 0 && dj != 0;
                            if (diagonalMove && (!Passable(j * nx + ni, goal, blocked) || !Passable(nj * nx + i, goal, blocked)))
                            {
                                continue;
                            }
                            float move = diagonalMove ? diagonal : step;
                            float ng = _g[k] + ((cost != null) ? move * cost[nk] : move);
                            if (_seen[nk] != _run)
                            {
                                _seen[nk] = _run;
                                _g[nk] = float.MaxValue;
                            }
                            if (ng < _g[nk])
                            {
                                _g[nk] = ng;
                                _parent[nk] = k;
                                Push(nk, ng + Heuristic(ni, nj, gi, gj, step));
                            }
                        }
                    }
                }
                return float.NaN;
            }

            private bool Passable(int k, int goal, Func<int, bool> blocked)
            {
                if (_passStamp[k] != _run)
                {
                    _passStamp[k] = _run;
                    _pass[k] = _grid.Cells[k] == Cell.Open && (k == goal || blocked == null || !blocked(k));
                }
                return _pass[k];
            }

            private static float Heuristic(int i, int j, int gi, int gj, float step)
            {
                int dx = Math.Abs(i - gi);
                int dy = Math.Abs(j - gj);
                return step * (Math.Max(dx, dy) + (Diagonal - 1f) * Math.Min(dx, dy));
            }

            private void Push(int cell, float key)
            {
                if (_heapCount == _heapCell.Length)
                {
                    Array.Resize(ref _heapCell, _heapCount * 2);
                    Array.Resize(ref _heapKey, _heapCount * 2);
                }
                int c = _heapCount++;
                while (c > 0)
                {
                    int parent = (c - 1) >> 1;
                    if (_heapKey[parent] <= key)
                    {
                        break;
                    }
                    _heapCell[c] = _heapCell[parent];
                    _heapKey[c] = _heapKey[parent];
                    c = parent;
                }
                _heapCell[c] = cell;
                _heapKey[c] = key;
            }

            private int Pop()
            {
                int top = _heapCell[0];
                int lastCell = _heapCell[--_heapCount];
                float lastKey = _heapKey[_heapCount];
                int c = 0;
                while (true)
                {
                    int child = 2 * c + 1;
                    if (child >= _heapCount)
                    {
                        break;
                    }
                    if (child + 1 < _heapCount && _heapKey[child + 1] < _heapKey[child])
                    {
                        child++;
                    }
                    if (_heapKey[child] >= lastKey)
                    {
                        break;
                    }
                    _heapCell[c] = _heapCell[child];
                    _heapKey[c] = _heapKey[child];
                    c = child;
                }
                if (_heapCount > 0)
                {
                    _heapCell[c] = lastCell;
                    _heapKey[c] = lastKey;
                }
                return top;
            }
        }

        private static Vec3 Mean(Vec3[] points)
        {
            Vec3 sum = Vec3.Zero;
            foreach (Vec3 p in points)
            {
                sum += p;
            }
            return sum * (1f / points.Length);
        }

        /// <summary>Spread of the men across the axis, 5th to 95th percentile, so a few outriders don't count.</summary>
        private static float Frontage(Vec3[] points, Vec2 centre, Vec2 left)
        {
            List<float> across = new List<float>(points.Length);
            foreach (Vec3 p in points)
            {
                across.Add(Vec2.DotProduct(p.AsVec2 - centre, left));
            }
            across.Sort();
            int lo = (int)(0.05f * (across.Count - 1));
            int hi = (int)(0.95f * (across.Count - 1));
            return across[hi] - across[lo];
        }

        private static float Percentile(List<float> sorted, float q)
        {
            return (sorted.Count > 0) ? sorted[(int)(q * (sorted.Count - 1))] : float.NaN;
        }

        private static float Share(int part, int whole)
        {
            return (whole > 0) ? (float)part / whole : float.NaN;
        }

        private static bool IsPlausible(float height)
        {
            return DeepWaterNavBlocker.IsPlausible(height);
        }

        /// <summary>A name or message safe for a tab-separated name=value field, printable ASCII only (the log is ASCII).</summary>
        internal static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "?";
            }
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                sb.Append((c < 33 || c > 126) ? ((c > 126 && !char.IsWhiteSpace(c)) ? '?' : '_')
                    : (c == '=' || c == ',' || c == ':' || c == '/') ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
