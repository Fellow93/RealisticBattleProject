using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>Area outlines for the map view (TerrainFeatureOverlay), built on the worker (BattleTerrainRead.Outlines.cs). Display data only. Immutable once published.</summary>
    internal sealed partial class BattleTerrain
    {
        internal enum OutlineKind
        {
            OpenField,
            Woods,
            Rough,
            Water,
            Hilltop,
            /// <summary>Line across a choke's bottleneck.</summary>
            Choke,
            /// <summary>Circle of an archer spot's room radius.</summary>
            Spot
        }

        /// <summary>One outline dot: world position (grid height + 1 m), kind and feature / spot id.</summary>
        internal struct OutlineDot
        {
            internal OutlineDot(Vec3 position, OutlineKind kind, string featureId)
            {
                Position = position;
                Kind = kind;
                FeatureId = featureId;
            }

            public Vec3 Position { get; }

            public OutlineKind Kind { get; }

            public string FeatureId { get; }
        }

        /// <summary>Every outline dot (empty when the step failed).</summary>
        public OutlinesInfo Outlines { get; } = new OutlinesInfo();

        internal sealed class OutlinesInfo : Group
        {
            public IReadOnlyList<OutlineDot> Dots { get; internal set; } = Empty<OutlineDot>();

            /// <summary>Outlines traced, and how many the dot cap cut short.</summary>
            public int Traced { get; internal set; }

            public int Capped { get; internal set; }

            /// <summary>Area cells flood-filled and boundary edges found (before thinning).</summary>
            public int MaskCells { get; internal set; }

            public int Edges { get; internal set; }

            /// <summary>Dots per kind, in OutlineKind order.</summary>
            public IReadOnlyList<int> DotsByKind { get; internal set; } = Empty<int>();
        }
    }
}
