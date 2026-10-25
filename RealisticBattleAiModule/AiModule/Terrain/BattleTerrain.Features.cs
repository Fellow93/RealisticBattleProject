using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// The feature map: the battle map as a short list of named places (Hill, Crest, Choke, OpenField, Flank, Woods, Rough,
    /// Water, Ford), built on the worker (BattleTerrainRead.Features.cs). Each kind is capped and sorted most important
    /// first; ids are kind name + rank ("hill1"). Raw measurements, not decisions. Never null: a failed kind is an empty
    /// list (FeatureMap.Error says why). Immutable once published.
    /// </summary>
    internal sealed partial class BattleTerrain
    {
        internal enum FeatureKind
        {
            Hill,
            Crest,
            Choke,
            OpenField,
            Flank,
            Woods,
            Rough,
            Water,
            Ford
        }

        /// <summary>Where a feature lies in the attacker's frame (A looking at D).</summary>
        internal enum FeatureSide
        {
            /// <summary>Near the axis, between the army centres.</summary>
            Centre,
            /// <summary>More than max(50 m, half the wider frontage) left of the axis.</summary>
            Left,
            Right,
            /// <summary>Beyond the defender's centre.</summary>
            Ahead,
            /// <summary>Behind the attacker's centre.</summary>
            Behind
        }

        [Flags]
        internal enum ArmySet
        {
            None = 0,
            Attacker = 1,
            Defender = 2,
            Both = 3
        }

        /// <summary>What a Woods / Rough cluster is made of.</summary>
        internal enum CoverType
        {
            /// <summary>No obstacle ray reached it (rays only in the field strip), or none hit anything.</summary>
            Unknown,
            /// <summary>Most rays hit flora (merged into the woods, never listed as Rough).</summary>
            Flora,
            /// <summary>Most rays hit the terrain itself: cliffs, rock ground.</summary>
            Terrain,
            /// <summary>Most rays hit an entity or an entity-less body that is neither flora nor terrain: rocks, buildings.</summary>
            Objects,
            /// <summary>Walkable but steeper than 25 degrees.</summary>
            Steep,
            /// <summary>Map-authored DifficultTerrain region.</summary>
            Difficult,
            /// <summary>The woods mask.</summary>
            Woods
        }

        /// <summary>Where a Woods (or map-authored Rough) area came from.</summary>
        internal enum AreaSource
        {
            None,
            /// <summary>Map-authored TacticalRegion.</summary>
            Map,
            /// <summary>Small-obstacle density alone.</summary>
            Density,
            /// <summary>Both a Forest region and the density.</summary>
            Both,
            /// <summary>A large no-navmesh cluster whose rays hit mostly flora.</summary>
            Rays
        }

        /// <summary>Which measurements found a choke (merged within 40 m).</summary>
        [Flags]
        internal enum ChokeSource
        {
            None = 0,
            /// <summary>Whole corridor's bottleneck: width = 2 x clearance.</summary>
            Corridor = 1,
            /// <summary>Mid-field corridor's bottleneck.</summary>
            MidCorridor = 2,
            /// <summary>An army's exit bottleneck.</summary>
            ExitAttacker = 4,
            ExitDefender = 8,
            /// <summary>Local minimum of the 21-line width profile.</summary>
            Profile = 16,
            /// <summary>Map-wide passage scan (BattleTerrainRead.Chokes.cs); its numbers win a merge.</summary>
            Scan = 32
        }

        /// <summary>What a choke is, from the ground in and beside its narrow stretch.</summary>
        internal enum ChokeType
        {
            /// <summary>Not measured.</summary>
            Unknown,
            /// <summary>No navmesh or the map edge on both sides: a gorge, a gap between rocks.</summary>
            Pass,
            /// <summary>Bridge deck over deep water.</summary>
            Bridge,
            /// <summary>Wet walkable ground across deep water.</summary>
            Ford,
            /// <summary>Dry land with deep water on both sides.</summary>
            Neck,
            /// <summary>Deep water on one side, no navmesh on the other.</summary>
            Mixed
        }

        /// <summary>What pinches a choke on its two sides.</summary>
        internal enum ChokeBound
        {
            Unknown,
            /// <summary>No navmesh on both sides.</summary>
            Blocked,
            /// <summary>Deep water on both sides.</summary>
            Water,
            /// <summary>Deep water on one side, no navmesh on the other.</summary>
            Mixed,
            /// <summary>The map edge on at least one side.</summary>
            Edge
        }

        /// <summary>Where the two armies stand relative to a choke.</summary>
        internal enum ChokeSides
        {
            Unknown,
            /// <summary>Same side: nobody has to pass it.</summary>
            Same,
            /// <summary>Opposite sides: the attacker comes through it (or round it).</summary>
            Opposite
        }

        /// <summary>The kind's name in ids and the log ("hill", "open", ...).</summary>
        internal static string FeatureKindName(FeatureKind kind)
        {
            return (kind == FeatureKind.OpenField) ? "open" : kind.ToString().ToLowerInvariant();
        }

        // the lists: worker sets them, CollectFeatures seals the whole list

        /// <summary>Timing / failures / path count / places dropped near the map edge of the feature step.</summary>
        public FeatureMapInfo FeatureMap { get; } = new FeatureMapInfo();

        /// <summary>Every feature, in kind order (Hill .. Ford), most important first within a kind.</summary>
        public IReadOnlyList<Feature> Features { get; private set; } = Empty<Feature>();

        public IReadOnlyList<HillFeature> Hills { get; internal set; } = Empty<HillFeature>();

        public IReadOnlyList<CrestFeature> Crests { get; internal set; } = Empty<CrestFeature>();

        public IReadOnlyList<ChokeFeature> Chokes { get; internal set; } = Empty<ChokeFeature>();

        public IReadOnlyList<OpenFieldFeature> OpenFields { get; internal set; } = Empty<OpenFieldFeature>();

        /// <summary>Always the attacker's own left, own right, then the defender's own left, own right (when measured).</summary>
        public IReadOnlyList<FlankFeature> Flanks { get; internal set; } = Empty<FlankFeature>();

        public IReadOnlyList<ObstacleFeature> Woods { get; internal set; } = Empty<ObstacleFeature>();

        public IReadOnlyList<ObstacleFeature> Rough { get; internal set; } = Empty<ObstacleFeature>();

        public IReadOnlyList<WaterFeature> Waters { get; internal set; } = Empty<WaterFeature>();

        public IReadOnlyList<FordFeature> Fords { get; internal set; } = Empty<FordFeature>();

        /// <summary>The features of one kind (an empty list when none or the step failed).</summary>
        public IReadOnlyList<Feature> Of(FeatureKind kind)
        {
            switch (kind)
            {
                case FeatureKind.Hill:
                    return Hills;
                case FeatureKind.Crest:
                    return Crests;
                case FeatureKind.Choke:
                    return Chokes;
                case FeatureKind.OpenField:
                    return OpenFields;
                case FeatureKind.Flank:
                    return Flanks;
                case FeatureKind.Woods:
                    return Woods;
                case FeatureKind.Rough:
                    return Rough;
                case FeatureKind.Water:
                    return Waters;
                case FeatureKind.Ford:
                    return Fords;
                default:
                    return Empty<Feature>();
            }
        }

        /// <summary>The feature with this id ("hill1"), or null.</summary>
        public Feature FeatureById(string id)
        {
            foreach (Feature feature in Features)
            {
                if (feature.Id == id)
                {
                    return feature;
                }
            }
            return null;
        }

        /// <summary>Worker, once every kind is set: builds Features.</summary>
        internal void CollectFeatures()
        {
            List<Feature> all = new List<Feature>();
            foreach (FeatureKind kind in (FeatureKind[])Enum.GetValues(typeof(FeatureKind)))
            {
                all.AddRange(Of(kind));
            }
            Features = Array.AsReadOnly(all.ToArray());
        }

        private static IReadOnlyList<T> Empty<T>()
        {
            return Array.AsReadOnly(Array.Empty<T>());
        }

        internal sealed class FeatureMapInfo : Group
        {
            /// <summary>A* searches and cells expanded by the feature step alone.</summary>
            public int Paths { get; internal set; }

            public int Expanded { get; internal set; }

            /// <summary>Ms per step; null when the step never ran.</summary>
            public IReadOnlyDictionary<string, float> StepMs { get; internal set; }

            /// <summary>Places dropped for lying within EdgeMargin of the map edge, by kind name; null when the step never ran.</summary>
            public IReadOnlyDictionary<string, int> DroppedNearEdge { get; internal set; }
        }

        /// <summary>Where a feature is from one army: distance from its centre and bearing.</summary>
        internal sealed class FeatureRelation
        {
            internal FeatureRelation(float distance, float bearing)
            {
                Distance = distance;
                Bearing = bearing;
            }

            /// <summary>Metres from the army's centre.</summary>
            public float Distance { get; }

            /// <summary>Degrees from the army's forward (toward the enemy): 0 = ahead, + = its left, -180..180.</summary>
            public float Bearing { get; }
        }

        internal abstract class Feature
        {
            protected Feature(FeatureKind kind)
            {
                Kind = kind;
            }

            /// <summary>Kind name + rank ("hill1" = the most important hill); stable for the battle.</summary>
            public string Id { get; internal set; }

            public FeatureKind Kind { get; }

            /// <summary>Representative point (see each kind).</summary>
            public Vec2 Position { get; internal set; }

            /// <summary>m2, NaN for point / line features.</summary>
            public float Area { get; internal set; } = float.NaN;

            /// <summary>Extent along / across the battle axis (m), NaN where none.</summary>
            public float Length { get; internal set; } = float.NaN;

            public float Width { get; internal set; } = float.NaN;

            /// <summary>Attacker's frame: metres from A along A->D, and across (+ = attacker's left).</summary>
            public float Along { get; internal set; } = float.NaN;

            public float Across { get; internal set; } = float.NaN;

            public FeatureRelation Attacker { get; internal set; }

            public FeatureRelation Defender { get; internal set; }

            /// <summary>In the field between the armies: Along in [0, distance] and |Across| within half the field strip.</summary>
            public bool Between { get; internal set; }

            public FeatureSide Side { get; internal set; }

            /// <summary>Rank key within its kind (larger = more important); NaN for flanks.</summary>
            public float Importance { get; internal set; } = float.NaN;
        }

        /// <summary>A feature that is a cluster of grid cells.</summary>
        internal abstract class AreaFeature : Feature
        {
            protected AreaFeature(FeatureKind kind) : base(kind)
            {
            }

            /// <summary>Metres from the nearest cell to the segment A->D (0 = touches the axis).</summary>
            public float AxisDistance { get; internal set; } = float.NaN;

            /// <summary>That nearest cell's centre.</summary>
            public Vec2 ClosestPoint { get; internal set; }
        }

        /// <summary>A local maximum of prominence (height minus the mean walkable height of a 120 m box) of at least 4 m, no other within 100 m. Position = the top cell. Importance = prominence / (1 + distance to A->D / 200 m).</summary>
        internal sealed class HillFeature : Feature
        {
            internal HillFeature() : base(FeatureKind.Hill)
            {
            }

            public float Height { get; internal set; } = float.NaN;

            /// <summary>Walkable width across the battle axis through the top.</summary>
            public float Crest { get; internal set; } = float.NaN;

            /// <summary>Seconds for each army to get there (grid A* for the 4 most important hills, else straight lines); NaN = no route.</summary>
            public float AttackerSeconds { get; internal set; } = float.NaN;

            public float DefenderSeconds { get; internal set; } = float.NaN;

            /// <summary>The army that gets there first (Both on a tie, None without times).</summary>
            public ArmySet First { get; internal set; }
        }

        /// <summary>A local maximum of the height profile along A->D between the army centres, at least 2 m above the lowest ground on each side. Position = point on the axis. Importance = the rise.</summary>
        internal sealed class CrestFeature : Feature
        {
            internal CrestFeature() : base(FeatureKind.Crest)
            {
            }

            public float Height { get; internal set; } = float.NaN;

            /// <summary>Fraction of the way A(0)->D(1).</summary>
            public float At { get; internal set; } = float.NaN;

            /// <summary>The smaller of its rises above the low ground on the A and D side (m).</summary>
            public float Rise { get; internal set; } = float.NaN;
        }

        /// <summary>
        /// A narrow passage anywhere on the map: the passage scan (ChokeSource.Scan) plus the route bottlenecks, each
        /// narrower than min(80 m, 1.5 x the wider frontage); a route one is merged into a scan choke within 40 m.
        /// Position = narrowest cell (middle of the narrow stretch), else the bottleneck cell; Width = narrowest walkable
        /// line; Length = narrow stretch (scan only). Importance = 1 / Ratio x proximity to A->D, x3 on the armies' route,
        /// x3 when the armies stand on its two sides.
        /// </summary>
        internal sealed class ChokeFeature : Feature
        {
            internal ChokeFeature() : base(FeatureKind.Choke)
            {
            }

            /// <summary>Width / the wider frontage.</summary>
            public float Ratio { get; internal set; } = float.NaN;

            /// <summary>Fraction of the way A(0)->D(1) (outside 0..1 behind an army).</summary>
            public float At { get; internal set; } = float.NaN;

            public ChokeSource Sources { get; internal set; }

            /// <summary>Whose exit bottleneck it is (from Sources).</summary>
            public ArmySet ExitOf { get; internal set; }

            public ChokeType Type { get; internal set; }

            public ChokeBound BoundedBy { get; internal set; }

            /// <summary>Narrowest walkable line through Position: ends (Invalid when not measured) and unit direction.</summary>
            public Vec2 LineStart { get; internal set; } = Vec2.Invalid;

            public Vec2 LineEnd { get; internal set; } = Vec2.Invalid;

            public Vec2 LineDirection { get; internal set; } = Vec2.Invalid;

            /// <summary>Unit vector along the passage (normal to LineDirection), from mouth A toward B.</summary>
            public Vec2 PassageDirection { get; internal set; } = Vec2.Invalid;

            /// <summary>Where the passage has opened up on each side (scan: clearance reached 1.5 x its narrowest; route choke: just past the cut).</summary>
            public Vec2 MouthA { get; internal set; } = Vec2.Invalid;

            public Vec2 MouthB { get; internal set; } = Vec2.Invalid;

            /// <summary>Side (0 = A, 1 = B, -1 = unknown) each army's centre lies on.</summary>
            public int AttackerSide { get; internal set; } = -1;

            public int DefenderSide { get; internal set; } = -1;

            public ChokeSides Sides { get; internal set; }

            /// <summary>With the passage cut the two sides meet nowhere else: the only way across.</summary>
            public bool Separates { get; internal set; }

            /// <summary>The A->D grid path passes through it.</summary>
            public bool OnRoute { get; internal set; }

            /// <summary>Share of wet cells in its narrow stretch.</summary>
            public float WetShare { get; internal set; } = float.NaN;

            /// <summary>The Water it is pinched by / the Ford it overlaps (ids; null = none).</summary>
            public string WaterId { get; internal set; }

            public string FordId { get; internal set; }

            /// <summary>The mouth on side 0 (A) / 1 (B); Invalid otherwise.</summary>
            public Vec2 MouthOf(int side)
            {
                return (side == 0) ? MouthA : (side == 1) ? MouthB : Vec2.Invalid;
            }
        }

        /// <summary>Region of cells with clearance >= 50 m (8-connected, >= 10,000 m2): the core of an open field. Position = its most open cell. Importance = area.</summary>
        internal sealed class OpenFieldFeature : AreaFeature
        {
            internal OpenFieldFeature() : base(FeatureKind.OpenField)
            {
            }

            /// <summary>Largest clearance in it (m), at Position.</summary>
            public float MaxClear { get; internal set; } = float.NaN;

            /// <summary>Armies whose centre lies in it or within 50 m of it.</summary>
            public ArmySet Armies { get; internal set; }
        }

        /// <summary>One end of one army's line: the width walker from its snapped centre along its own left / right. Position = where the walk ended.</summary>
        internal sealed class FlankFeature : Feature
        {
            internal FlankFeature() : base(FeatureKind.Flank)
            {
            }

            public ArmySet Army { get; internal set; }

            /// <summary>The army's own left (the defender's own left is the attacker's right).</summary>
            public bool OwnLeft { get; internal set; }

            /// <summary>What ended the walk: Blocked | Water | Edge | Max.</summary>
            public End End { get; internal set; }

            /// <summary>Metres from the centre to the end; and that minus half its frontage.</summary>
            public float EndDistance { get; internal set; } = float.NaN;

            public float Spare { get; internal set; } = float.NaN;

            /// <summary>The enemy's route round this army on this side; null = nothing measured.</summary>
            public FlankRoute EnemyRoute { get; internal set; }

            public bool Turnable => EnemyRoute != null && EnemyRoute.Reachable;
        }

        /// <summary>
        /// Woods: an 8-connected component (>= 2,000 m2) of the woods mask (Cover = Woods). Rough: a cluster (>= 1,500 m2) of
        /// no-navmesh cells not merged into the woods, steep walkable cells (Cover = Steep), a DifficultTerrain region
        /// (Cover = Difficult, Source = Map), or a density area whose rays hit mostly entities (Objects). Importance = area /
        /// (1 + AxisDistance / 200 m). Position = cell nearest the centroid.
        /// </summary>
        internal sealed class ObstacleFeature : AreaFeature
        {
            internal ObstacleFeature(FeatureKind kind) : base(kind)
            {
            }

            public CoverType Cover { get; internal set; }

            /// <summary>Mean slope (deg) of a Steep cluster; NaN otherwise.</summary>
            public float MeanSlope { get; internal set; } = float.NaN;

            public AreaSource Source { get; internal set; }

            public float RegionRadius { get; internal set; } = float.NaN; // m, Difficult only
        }

        /// <summary>The woods mask; set by the feature step's cover step.</summary>
        public WoodsMapInfo WoodsMap { get; } = new WoodsMapInfo();

        /// <summary>
        /// The woods mask over the grid (BattleTerrainRead.Features.cs FindCover). A cell is woods inside a map Forest
        /// region, or where small-obstacle density reaches WoodsDensity, or in a large no-navmesh cluster mostly inside
        /// those, minus density-only areas whose rays hit entities (villages). Raw: keeps cells near the map edge.
        /// Immutable once published; IsWoods is array reads only (any thread).
        /// </summary>
        internal sealed class WoodsMapInfo
        {
            // Mask over the grid (null when the step failed), same geometry as the grid. Written once on the worker.
            internal bool[] Mask;
            internal float MaskMinX;
            internal float MaskMinY;
            internal float MaskStep;
            internal int MaskNx;
            internal int MaskNy;

            /// <summary>The cell under p is woods. False outside the map or without a mask. Any thread: array reads only.</summary>
            public bool IsWoods(Vec2 p)
            {
                bool[] mask = Mask;
                if (mask == null || float.IsNaN(p.x) || float.IsNaN(p.y) || MaskStep <= 0f)
                {
                    return false;
                }
                int i = (int)Math.Floor((p.x - MaskMinX) / MaskStep);
                int j = (int)Math.Floor((p.y - MaskMinY) / MaskStep);
                if (i < 0 || j < 0 || i >= MaskNx || j >= MaskNy)
                {
                    return false;
                }
                return mask[j * MaskNx + i];
            }
        }

        /// <summary>An 8-connected region of deep water >= 1,000 m2. Importance = area / (1 + AxisDistance / 200 m), x10 when it crosses A->D.</summary>
        internal sealed class WaterFeature : AreaFeature
        {
            internal WaterFeature() : base(FeatureKind.Water)
            {
            }

            /// <summary>One of its cells lies in the field between the armies.</summary>
            public bool TouchesField { get; internal set; }

            /// <summary>The line A->D passes through it; first crossing as a fraction of the way.</summary>
            public bool CrossesAxis { get; internal set; }

            public float CrossAt { get; internal set; } = float.NaN;
        }

        /// <summary>A cluster of wet walkable cells that is on the A->D grid path, or touches deep water and is >= 100 m2. Position = its middle path cell when on the path. Importance = area / (1 + AxisDistance / 200 m), x10 on the path.</summary>
        internal sealed class FordFeature : AreaFeature
        {
            internal FordFeature() : base(FeatureKind.Ford)
            {
            }

            public bool OnPath { get; internal set; }

            /// <summary>One of its cells borders deep water (4-neighbour).</summary>
            public bool AtDeepWater { get; internal set; }

            /// <summary>Id of a Water feature it borders; null when none.</summary>
            public string WaterId { get; internal set; }
        }
    }
}
