using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>Choke hold: the defender's best narrow passage to hold, scored on the worker (BattleTerrainRead.ChokeHold.cs). Raw data for TerrainChokeHold. Immutable once published.</summary>
    internal sealed partial class BattleTerrain
    {
        /// <summary>The defender's choke hold (Defender null = no choke qualifies, Why says why).</summary>
        public ChokeHoldInfo ChokeHold { get; } = new ChokeHoldInfo();

        /// <summary>One choke the defender can hold, and where its men stand. Immutable.</summary>
        internal sealed class ChokeHoldPlace
        {
            /// <summary>Feature map choke id and the point the narrowest line was measured through.</summary>
            public string ChokeId { get; internal set; }

            public Vec2 Choke { get; internal set; }

            /// <summary>Feature width and the narrowest walkable line through Choke (m).</summary>
            public float FeatureWidth { get; internal set; } = float.NaN;

            public float LineWidth { get; internal set; } = float.NaN;

            /// <summary>Unit vector along the infantry line (across the gap, or across the exit at a mouth).</summary>
            public Vec2 LineDirection { get; internal set; }

            /// <summary>Unit vector the line faces: through the gap toward the attacker.</summary>
            public Vec2 Facing { get; internal set; }

            /// <summary>Infantry place: HoldBack m behind the narrowest line, centred in the walkable span.</summary>
            public Vec2 Hold { get; internal set; }

            public float HoldBack { get; internal set; } = float.NaN;

            /// <summary>Walkable width through Hold (m), each end, and the width the line forms (x 0.95).</summary>
            public float HoldSpan { get; internal set; } = float.NaN;

            public End HoldLeftEnd { get; internal set; }

            public End HoldRightEnd { get; internal set; }

            public float HoldWidth { get; internal set; } = float.NaN;

            /// <summary>Both ends of the hold line are closed (no navmesh, water, steep, map edge).</summary>
            public bool BothFlanksCovered { get; internal set; }

            /// <summary>Held at the defender's mouth of a crossing (line across the exit, HoldWidth = gap + 2 x ChokeMouthWing); false = held in the gap.</summary>
            public bool AtMouth { get; internal set; }

            /// <summary>The armies are on the passage's two sides: the attacker comes through it.</summary>
            public bool Crossing { get; internal set; }

            /// <summary>Defender's estimated infantry frontage and the attacker's measured frontage (m).</summary>
            public float InfantryFrontage { get; internal set; } = float.NaN;

            public float AttackerFrontage { get; internal set; } = float.NaN;

            /// <summary>Metres from the defender's centre to Hold; seconds for it to walk there / for the attacker to reach Choke.</summary>
            public float DefenderDistance { get; internal set; } = float.NaN;

            public float DefenderSeconds { get; internal set; } = float.NaN;

            public float AttackerSeconds { get; internal set; } = float.NaN;

            /// <summary>Archers' place (invalid = none) and its source: archer spot id or "behind".</summary>
            public Vec2 Archer { get; internal set; } = Vec2.Invalid;

            public string ArcherSource { get; internal set; }

            /// <summary>Archer place height above Hold (m).</summary>
            public float ArcherGain { get; internal set; } = float.NaN;

            /// <summary>Cavalry reserve's place (invalid = none).</summary>
            public Vec2 Reserve { get; internal set; } = Vec2.Invalid;

            public float Score { get; internal set; } = float.NaN;
        }

        /// <summary>The step's result and timing.</summary>
        internal sealed class ChokeHoldInfo : Group
        {
            public ChokeHoldPlace Defender { get; internal set; }

            /// <summary>Per-choke verdicts, or the reason there was none.</summary>
            public string Why { get; internal set; }

            /// <summary>Defender's infantry estimate (men) and frontage (m) the widths were judged against.</summary>
            public int Infantry { get; internal set; }

            public float InfantryFrontage { get; internal set; } = float.NaN;

            public int PathRuns { get; internal set; }

            /// <summary>Attacker / defender strength (NaN = unread), its k (0 even .. 1 at 2.5x) and source; the distance / reach limits it gave; the reach also judged against the attacker's riders.</summary>
            public float Outnumbered { get; internal set; } = float.NaN;

            public float OutnumberedK { get; internal set; }

            public string OutnumberedSource { get; internal set; } = "-";

            public float MaxDistance { get; internal set; } = float.NaN;

            public float ReachMargin { get; internal set; } = float.NaN;

            public bool CavalryCheck { get; internal set; }
        }
    }
}
