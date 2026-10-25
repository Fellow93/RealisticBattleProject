using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>Cavalry ground per side (charge lanes, bad-ground mask), scored on the worker (BattleTerrainRead.Cavalry.cs). Raw data for TerrainCavalry. Immutable once published; IsBadGround is array reads only.</summary>
    internal sealed partial class BattleTerrain
    {
        /// <summary>Both sides' cavalry ground and the bad-ground mask (empty lists / null mask when the step failed).</summary>
        public CavalryInfo Cavalry { get; } = new CavalryInfo();

        /// <summary>One charge lane onto the enemy line, from Start (end of the clear run) to Aim. Immutable.</summary>
        internal sealed class ChargeLane
        {
            /// <summary>"AL1".. / "DL1".., best first.</summary>
            public string Id { get; internal set; }

            /// <summary>Point of the enemy line it runs onto; Offset = fraction of the enemy frontage (-0.5..0.5, + = attacker's left).</summary>
            public Vec2 Aim { get; internal set; }

            public float AimHeight { get; internal set; } = float.NaN;

            public float Offset { get; internal set; } = float.NaN;

            /// <summary>Where the clear run starts and its terrain height.</summary>
            public Vec2 Start { get; internal set; }

            public float StartHeight { get; internal set; } = float.NaN;

            /// <summary>Unit vector from the aim back to the start.</summary>
            public Vec2 Direction { get; internal set; }

            /// <summary>Clear length (m, max 250): walkable, flatter than 15 deg; bumps up to 8 m crossed.</summary>
            public float Length { get; internal set; } = float.NaN;

            public int Bumps { get; internal set; }

            /// <summary>What ended the run: steep | blocked | water | edge | max.</summary>
            public End End { get; internal set; }

            /// <summary>Start height minus aim height (m).</summary>
            public float Drop { get; internal set; } = float.NaN;

            /// <summary>Metres on wet ground and on bad ground.</summary>
            public float WetMetres { get; internal set; }

            public float BadMetres { get; internal set; }

            public float Score { get; internal set; } = float.NaN;

            /// <summary>Run-up points, metres from the aim (start first), each walkable and not bad ground.</summary>
            public IReadOnlyList<float> Entries { get; internal set; } = Empty<float>();
        }

        /// <summary>One side's cavalry ground against the enemy army, and what the search did.</summary>
        internal sealed class SideCavalry
        {
            public IReadOnlyList<ChargeLane> Lanes { get; internal set; } = Empty<ChargeLane>();

            /// <summary>Lanes walked / dropped as too short or not kept.</summary>
            public int LanesTried { get; internal set; }

            public int LanesShort { get; internal set; }

            /// <summary>Lanes pulled back from the map edge margin, and those left too short.</summary>
            public int LanesEdgeTrimmed { get; internal set; }

            public int LanesEdgeDropped { get; internal set; }
        }

        /// <summary>The cavalry step's result and timing, and the bad-ground mask.</summary>
        internal sealed class CavalryInfo : Group
        {
            public SideCavalry Attacker { get; internal set; } = new SideCavalry();

            public SideCavalry Defender { get; internal set; } = new SideCavalry();

            /// <summary>Share of the field strip's / in-bounds map's cells that are bad ground.</summary>
            public float BadShareField { get; internal set; } = float.NaN;

            public float BadShareMap { get; internal set; } = float.NaN;

            // Mask over the grid (null when the step failed) with its own copy of the grid geometry. Written once on the worker before the publish.
            internal bool[] BadMask;
            internal float MaskMinX;
            internal float MaskMinY;
            internal float MaskStep;
            internal int MaskNx;
            internal int MaskNy;

            /// <summary>Feature step's open-field labels (edge-trimmed, -1 = none), kept for the live re-plans' frame; null when unknown.</summary>
            internal int[] OpenFieldLabels;

            /// <summary>Bad ground for a charge target (steep, wet, no navmesh, cramped, woods). False outside the map or without a mask. Any thread: array reads only.</summary>
            public bool IsBadGround(Vec2 p)
            {
                bool[] mask = BadMask;
                if (mask == null || float.IsNaN(p.x) || float.IsNaN(p.y) || MaskStep <= 0f)
                {
                    return false;
                }
                int i = (int)System.Math.Floor((p.x - MaskMinX) / MaskStep);
                int j = (int)System.Math.Floor((p.y - MaskMinY) / MaskStep);
                if (i < 0 || j < 0 || i >= MaskNx || j >= MaskNy)
                {
                    return false;
                }
                return mask[j * MaskNx + i];
            }
        }
    }
}
