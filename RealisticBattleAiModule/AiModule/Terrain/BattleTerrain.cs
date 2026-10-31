using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// The battlefield terrain of the current land field battle as typed, immutable data. Grid built at mission load; the
    /// main thread gathers engine data (BattleTerrainRead.Gather), a background task computes the rest (Compute), and
    /// BattleTerrainLogic publishes the result on the main thread a few frames after deployment.
    ///
    /// Current is null before the publish (the first tactic decision comes before it), when a side had no men or no grid,
    /// when the background part failed, outside field battles and after the mission ended: consumers must handle null.
    ///
    /// THREADING: immutable once published (volatile write on the main thread). AI-thread readers copy the reference to a
    /// local, null-check it and only read. Never call the engine for terrain off the main thread (Build and Gather do, main
    /// thread only; Compute reads managed data only).
    ///
    /// Conventions: positions are world x/y; the frame runs from the attacker's centre A to the defender's centre D
    /// (Forward), "left" is the attacker's left. float.NaN = no value; a group that threw keeps NaN/null (Group.Error says why).
    /// Places within EdgeMargin of the map edge are left out of the feature map, archer spots and cavalry ground; new
    /// consumers picking places should do the same (NearEdge, EdgeDistanceAt).
    /// </summary>
    internal sealed partial class BattleTerrain
    {
        private static volatile BattleTerrain _current;

        /// <summary>The current field battle's terrain, or null. Read once into a local.</summary>
        public static BattleTerrain Current
        {
            get { return _current; }
        }

        /// <summary>Main thread only, once the read is complete.</summary>
        internal static void Publish(BattleTerrain terrain)
        {
            _current = terrain;
        }

        /// <summary>Main thread: mission start (MissionStartReset) and mission end (BattleTerrainLogic).</summary>
        internal static void Clear()
        {
            _current = null;
        }

        internal enum End
        {
            None,
            /// <summary>No navmesh face: rocks, trees, buildings, cliffs.</summary>
            Blocked,
            Water,
            Edge,
            Steep,
            Max
        }

        private readonly BattleTerrainRead.Grid _grid;

        internal BattleTerrain(BattleTerrainRead.Grid grid)
        {
            _grid = grid;
        }

        public string Scene => _grid.Scene;

        /// <summary>Where the map bounds came from: "boundaries(N pts)" or "scene".</summary>
        public string Bounds => _grid.Bounds;

        public float GridStep => _grid.Step;

        public float GridMinX => _grid.MinX;

        public float GridMinY => _grid.MinY;

        public int GridNx => _grid.Nx;

        public int GridNy => _grid.Ny;

        public int GridCells => _grid.Nx * _grid.Ny;

        public float MapWidth => _grid.Nx * _grid.Step;

        public float MapLength => _grid.Ny * _grid.Step;

        /// <summary>Ms the grid took to build, clearance transform included.</summary>
        public long BuildMs => _grid.BuildMs;

        public float ClearMs => _grid.ClearMs;

        public float EdgeMs => _grid.EdgeMs;

        /// <summary>Where the grid's deep water / bridge cells came from and its tallies; may be null.</summary>
        internal BattleTerrainRead.WaterRead WaterRead => _grid.Water;

        /// <summary>Places closer than this (m) to the map edge are not used.</summary>
        public float EdgeMargin => BattleTerrainRead.EdgeMargin;

        public bool IsWalkable(Vec2 p)
        {
            return _grid.IsWalkable(_grid.IndexOf(p));
        }

        /// <summary>Terrain height of the cell under p; NaN outside the map.</summary>
        public float HeightAt(Vec2 p)
        {
            return _grid.HeightAt(p);
        }

        /// <summary>Terrain height between cell centres (bilinear).</summary>
        public float SampleHeight(Vec2 p)
        {
            return _grid.SampleHeight(p);
        }

        /// <summary>Degrees; NaN unless the cell is walkable with four plausible neighbours.</summary>
        public float SlopeAt(Vec2 p)
        {
            return _grid.SlopeAt(p);
        }

        /// <summary>Walkable under shallow water (fords, marsh).</summary>
        public bool IsWet(Vec2 p)
        {
            return _grid.IsWet(p);
        }

        /// <summary>Metres to the nearest cell outside the mission boundary; 0 outside the grid or boundary.</summary>
        public float EdgeDistanceAt(Vec2 p)
        {
            return _grid.EdgeDistanceAt(p);
        }

        /// <summary>Within EdgeMargin of the map edge (true outside the grid): not a place to use.</summary>
        public bool NearEdge(Vec2 p)
        {
            return _grid.NearEdge(p);
        }

        /// <summary>Mission time of the main-thread gather.</summary>
        public float ComputedAt { get; internal set; } = float.NaN;

        public float PublishedAt { get; internal set; } = float.NaN;

        public bool AfterDeployment { get; internal set; }

        /// <summary>Ms of the main-thread gather (RayMs is part of it), the background part, and wall time gather start to publish.</summary>
        public long MainMs { get; internal set; }

        public long RayMs { get; internal set; }

        public long BackgroundMs { get; internal set; }

        public long WallMs { get; internal set; }

        public float Distance { get; internal set; } = float.NaN; // m, between the army centres

        /// <summary>Unit vector from the attacker's centre to the defender's.</summary>
        public Vec2 Forward { get; internal set; }

        /// <summary>The attacker's left, perpendicular to Forward.</summary>
        public Vec2 Left { get; internal set; }

        /// <summary>Defender's mean height minus the attacker's (m).</summary>
        public float HeightDifference { get; internal set; } = float.NaN;

        /// <summary>The field strip between the armies: width across the axis (m).</summary>
        public float FieldWidth { get; internal set; } = float.NaN;

        public AreaStats Field { get; internal set; }

        public SideInfo Attacker { get; } = new SideInfo();

        public SideInfo Defender { get; } = new SideInfo();

        public SpaceInfo Space { get; } = new SpaceInfo();

        public OpenInfo Open { get; } = new OpenInfo();

        public MountedInfo Mounted { get; } = new MountedInfo();

        public SightInfo Sight { get; } = new SightInfo();

        /// <summary>Clutter / deep water / slope tallies of the field strip.</summary>
        internal sealed class AreaStats
        {
            internal AreaStats(float outShare, float clutter, float deepShare, float slopeMean, float steepShare, float hardShare)
            {
                OutShare = outShare;
                Clutter = clutter;
                DeepShare = deepShare;
                SlopeMean = slopeMean;
                SteepShare = steepShare;
                HardShare = hardShare;
            }

            /// <summary>Share of cells outside the boundary.</summary>
            public float OutShare { get; }

            /// <summary>Share of land (in bounds, not deep) with no navmesh: rocks, trees, buildings, cliffs.</summary>
            public float Clutter { get; }

            public float DeepShare { get; }

            /// <summary>Mean slope of walkable ground (degrees).</summary>
            public float SlopeMean { get; }

            /// <summary>Share of walkable ground steeper than 15° / 25°.</summary>
            public float SteepShare { get; }

            public float HardShare { get; }
        }

        /// <summary>Walkable width across the axis through a point: each half and where it ended.</summary>
        internal sealed class Width
        {
            internal Width(float left, End leftEnd, float right, End rightEnd)
            {
                Left = left;
                LeftEnd = leftEnd;
                Right = right;
                RightEnd = rightEnd;
            }

            public float Left { get; }

            public End LeftEnd { get; }

            public float Right { get; }

            public End RightEnd { get; }

            public float Total => Left + Right;
        }

        /// <summary>One line of the width profile across the axis: where along A(0)->D(1), each half and where it ended.</summary>
        internal struct WidthLine
        {
            internal WidthLine(float fraction, Width width)
            {
                Fraction = fraction;
                Left = width.Left;
                LeftEnd = width.LeftEnd;
                Right = width.Right;
                RightEnd = width.RightEnd;
            }

            public float Fraction { get; }

            public float Left { get; }

            public End LeftEnd { get; }

            public float Right { get; }

            public End RightEnd { get; }

            public float Total => Left + Right;
        }

        /// <summary>Shortest grid route from a side's centre to 60 m behind the other army, kept 50 m off its line. Null on SideInfo = nothing to measure.</summary>
        internal sealed class FlankRoute
        {
            internal FlankRoute(bool reachable, float length, float ratio, float seconds)
            {
                Reachable = reachable;
                Length = length;
                Ratio = ratio;
                Seconds = seconds;
            }

            public bool Reachable { get; }

            /// <summary>Route length (m), ratio to the straight distance, and time at the mover's mounted pace (foot pace with no riders).</summary>
            public float Length { get; }

            public float Ratio { get; }

            public float Seconds { get; }
        }

        /// <summary>A charge run-up: walkable ground flatter than 15 deg (cap 250 m) back from three points of a line, crossing bumps up to 8 m.</summary>
        internal sealed class RunUp
        {
            internal RunUp(float max, float min, End minEnd, int maxBumps, int minBumps, float centre)
            {
                Max = max;
                Min = min;
                MinEnd = minEnd;
                MaxBumps = maxBumps;
                MinBumps = minBumps;
                Centre = centre;
            }

            public float Max { get; }

            /// <summary>The run-up back from the line's centre along the axis: straight ahead of the charger's main body (the wedge forms in front of its main infantry).</summary>
            public float Centre { get; }

            public float Min { get; }

            public End MinEnd { get; }

            /// <summary>Bumps crossed on the longest lane.</summary>
            public int MaxBumps { get; }

            /// <summary>Bumps crossed on the shortest lane.</summary>
            public int MinBumps { get; }
        }

        /// <summary>One army.</summary>
        internal sealed class SideInfo
        {
            public Vec3 Centroid { get; internal set; }

            /// <summary>The centre snapped to the nearest walkable cell (max 40 m); HasSnap false = none.</summary>
            public bool HasSnap { get; internal set; }

            public Vec2 SnapPoint { get; internal set; }

            /// <summary>How far the centre was moved by the snap (m); NaN without one.</summary>
            public float SnapDistance => HasSnap ? SnapPoint.Distance(Centroid.AsVec2) : float.NaN;

            /// <summary>Width across the axis, 5th-95th percentile of its men.</summary>
            public float Frontage { get; internal set; } = float.NaN;

            public int Men { get; internal set; }

            public int Foot { get; internal set; }

            public int Mounted { get; internal set; }

            /// <summary>Men with a launcher and ammo.</summary>
            public int Ranged { get; internal set; }

            /// <summary>Mean max speed of the foot / of the riders (m/s); NaN with none.</summary>
            public float FootSpeed { get; internal set; } = float.NaN;

            public float MountedSpeed { get; internal set; } = float.NaN;

            /// <summary>Its route round the OTHER army on the attacker's left / right.</summary>
            public FlankRoute FlankLeft { get; internal set; }

            public FlankRoute FlankRight { get; internal set; }

            /// <summary>Median clearance (m) of the cells under its line (non-walkable count 0).</summary>
            public float ClearLine { get; internal set; } = float.NaN;

            /// <summary>
            /// Its exit: the widest way out of its own area toward the enemy. ExitWidth = 2 x the bottleneck clearance (m);
            /// ExitFront = ExitWidth / its own frontage (below 1: must squeeze out); ExitPoint = bottleneck cell. NaN without a
            /// snap or a route.
            /// </summary>
            public float ExitWidth { get; internal set; } = float.NaN;

            public float ExitFront { get; internal set; } = float.NaN;

            public Vec2 ExitPoint { get; internal set; }

            /// <summary>Its riders' run-up onto the ENEMY's line.</summary>
            public RunUp RunUpOntoEnemy { get; internal set; }

            /// <summary>Share of walkable field points (within 200 m of it) seen over the terrain from >= 1 of five eye points on its line. Grid heights only.</summary>
            public float FireAny { get; internal set; } = float.NaN;
        }

        /// <summary>Timing and failure of one group.</summary>
        internal abstract class Group
        {
            public float Ms { get; internal set; } = float.NaN;

            /// <summary>"Type:message" when the group threw, else null.</summary>
            public string Error { get; internal set; }
        }

        internal sealed class SpaceInfo : Group
        {
            /// <summary>All 21 width lines (fractions 0, 0.05 .. 1 from A to D); null when the group failed before it.</summary>
            public IReadOnlyList<WidthLine> WidthProfile { get; internal set; }

            /// <summary>A and D on one walkable ground (false = cut off, e.g. a river with no crossing); null = unknown.</summary>
            public bool? SameGround { get; internal set; }
        }

        /// <summary>The widest corridors between the armies, from the grid's clearance map. Per-side exits are on SideInfo.</summary>
        internal sealed class OpenInfo : Group
        {
            /// <summary>
            /// The widest corridor between the snapped centres A and D: the walkable route whose smallest clearance is largest.
            /// CorridorWidth = 2 x that bottleneck clearance; CorridorFront = width / wider frontage; CorridorPoint = the
            /// bottleneck cell. NaN without a snap or route. (Shortest paths hug obstacle edges, so this says how much room there is.)
            /// </summary>
            public float CorridorWidth { get; internal set; } = float.NaN;

            public float CorridorFront { get; internal set; } = float.NaN;

            public Vec2 CorridorPoint { get; internal set; }

            /// <summary>Mid-field corridor: widest route from behind the attacker's stretch line (20% of the distance, min 60 m) to beyond the defender's. Same units as Corridor*. NaN when the armies stand closer than two stretches, or no route.</summary>
            public float MidCorridorWidth { get; internal set; } = float.NaN;

            public float MidCorridorFront { get; internal set; } = float.NaN;

            public Vec2 MidCorridorPoint { get; internal set; }
        }

        /// <summary>The charge run-ups (SideInfo.RunUpOntoEnemy); only timing and failure here.</summary>
        internal sealed class MountedInfo : Group
        {
        }

        internal sealed class SightInfo : Group
        {
            /// <summary>Obstacle ray casts that threw.</summary>
            public int RayFailed { get; internal set; }
        }
    }
}
