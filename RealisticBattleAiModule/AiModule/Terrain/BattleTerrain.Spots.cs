using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>Archer spots: per side, up to SpotsPerSide places near its line that see more of the enemy approach than the line does, scored on the worker (BattleTerrainRead.Spots.cs). Raw data for TerrainSpots. Immutable once published.</summary>
    internal sealed partial class BattleTerrain
    {
        internal enum SpotKind
        {
            /// <summary>A hill top, or a lattice point at least SpotHighGain above the line.</summary>
            Hill,
            /// <summary>A crest feature on the A->D axis.</summary>
            Crest,
            /// <summary>A lattice point, not clearly higher, with a good view of the approach.</summary>
            ClearView
        }

        internal static string SpotKindName(SpotKind kind)
        {
            return (kind == SpotKind.ClearView) ? "clearView" : kind.ToString().ToLowerInvariant();
        }

        /// <summary>Both sides' archer spots (empty lists when the step failed or found none).</summary>
        public SpotsInfo Spots { get; } = new SpotsInfo();

        /// <summary>One candidate place for an archer group. Immutable.</summary>
        internal sealed class ArcherSpot
        {
            /// <summary>"A1".."A4" / "D1".., best first.</summary>
            public string Id { get; internal set; }

            public SpotKind Kind { get; internal set; }

            /// <summary>Hill / crest feature id, or "grid".</summary>
            public string Source { get; internal set; }

            /// <summary>Cell centre and terrain height.</summary>
            public Vec2 Position { get; internal set; }

            public float Height { get; internal set; } = float.NaN;

            /// <summary>Height above the side's mean height (m).</summary>
            public float Gain { get; internal set; } = float.NaN;

            /// <summary>Share of the enemy's approach points it sees (0..1).</summary>
            public float Fire { get; internal set; } = float.NaN;

            public int FireTargets { get; internal set; }

            /// <summary>Its side of its own line, as BehaviorProtectFlank's.</summary>
            public FormationAI.BehaviorSide Side { get; internal set; }

            /// <summary>Metres from the side's centre; along its forward (+ = toward the enemy); across (+ = its left).</summary>
            public float DistLine { get; internal set; } = float.NaN;

            public float Along { get; internal set; } = float.NaN;

            public float Across { get; internal set; } = float.NaN;

            /// <summary>Clearance (m) and slope (deg) of its cell.</summary>
            public float Clearance { get; internal set; } = float.NaN;

            public float Slope { get; internal set; } = float.NaN;

            /// <summary>Seconds for its own side / the enemy to walk there; NaN = no route.</summary>
            public float OurSeconds { get; internal set; } = float.NaN;

            public float EnemySeconds { get; internal set; } = float.NaN;

            public float Score { get; internal set; } = float.NaN;

            /// <summary>"height", "view" or "both".</summary>
            public string Reason { get; internal set; }
        }

        internal sealed class SideSpots
        {
            public IReadOnlyList<ArcherSpot> Spots { get; internal set; } = Empty<ArcherSpot>();

            /// <summary>Fire share from the side's own centre: the bar a spot must beat.</summary>
            public float LineFire { get; internal set; } = float.NaN;

            /// <summary>Candidates looked at, and the reject counts per reason.</summary>
            public int Candidates { get; internal set; }

            public int RejectRoom { get; internal set; }

            public int RejectWoods { get; internal set; }

            public int RejectEdge { get; internal set; }

            public int RejectRegion { get; internal set; }

            public int RejectTargets { get; internal set; }

            public int RejectScore { get; internal set; }

            public int RejectSeparation { get; internal set; }

            public int RejectReach { get; internal set; }

            /// <summary>Sight lines cast and A* searches run for this side.</summary>
            public int Rays { get; internal set; }

            public int PathRuns { get; internal set; }
        }

        internal sealed class SpotsInfo : Group
        {
            public SideSpots Attacker { get; internal set; } = new SideSpots();

            public SideSpots Defender { get; internal set; } = new SideSpots();
        }
    }
}
