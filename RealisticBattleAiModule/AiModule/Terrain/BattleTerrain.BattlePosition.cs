using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>Battle position: where the defender should fight, scored on the worker (BattleTerrainRead.BattlePosition.cs). Raw data; TerrainBattlePosition turns it into the published plan. Immutable once published.</summary>
    internal sealed partial class BattleTerrain
    {
        /// <summary>The defender's battle position (Result null = the step did not run; Result.Winner null = no position, Result.Why says why).</summary>
        public BattlePositionInfo BattlePosition { get; } = new BattlePositionInfo();

        internal enum PositionKind
        {
            Current,
            /// <summary>Last plan's hold, scored again (re-pick only).</summary>
            Previous,
            Hill,
            Crest,
            /// <summary>Just behind a crest, away from the attacker.</summary>
            ReverseSlope,
            Choke,
            Lattice
        }

        internal static string PositionKindName(PositionKind kind)
        {
            switch (kind)
            {
                case PositionKind.Current:
                    return "current";
                case PositionKind.Previous:
                    return "previous";
                case PositionKind.Hill:
                    return "hill";
                case PositionKind.Crest:
                    return "crest";
                case PositionKind.ReverseSlope:
                    return "reverseSlope";
                case PositionKind.Choke:
                    return "choke";
                default:
                    return "lattice";
            }
        }

        /// <summary>One scored place for the defender's infantry line. Immutable once published.</summary>
        internal sealed class PositionCandidate
        {
            public PositionKind Kind { get; internal set; }

            /// <summary>Feature id, "line", "previous" or "grid".</summary>
            public string Source { get; internal set; }

            public Vec2 Hold { get; internal set; }

            /// <summary>Unit vector the line faces (a choke: through the gap).</summary>
            public Vec2 Facing { get; internal set; }

            /// <summary>Unit vector along the line, toward the defender's own left.</summary>
            public Vec2 LineDirection { get; internal set; }

            /// <summary>Walkable width across the facing through Hold (m) and its ends.</summary>
            public float Span { get; internal set; } = float.NaN;

            public End LeftEnd { get; internal set; }

            public End RightEnd { get; internal set; }

            /// <summary>min(span x 0.95, own infantry frontage x 1.2); a choke: its hold width.</summary>
            public float LineWidth { get; internal set; } = float.NaN;

            /// <summary>The line's left / right end is covered by water, blocked, steep, woods or rough.</summary>
            public bool LeftCovered { get; internal set; }

            public bool RightCovered { get; internal set; }

            /// <summary>"water", "blocked", "woods", ... or "-" (open).</summary>
            public string LeftCover { get; internal set; } = "-";

            public string RightCover { get; internal set; } = "-";

            public bool IsChoke => Kind == PositionKind.Choke;

            /// <summary>Grid walk to Hold (m); seconds for the defender / attacker to reach it (+inf = no route).</summary>
            public float Distance { get; internal set; } = float.NaN;

            public float DefenderSeconds { get; internal set; } = float.NaN;

            public float AttackerSeconds { get; internal set; } = float.NaN;

            /// <summary>Hold height minus the mean of the ground 50-200 m in front (m).</summary>
            public float HeightAdvantage { get; internal set; } = float.NaN;

            /// <summary>Share of approach points (30-180 m in front) seen from the line (with cover / terrain only).</summary>
            public float FireShare { get; internal set; } = float.NaN;

            public float TerrainSeenShare { get; internal set; } = float.NaN;

            /// <summary>Share of cells 10-150 m in front that slow an attacker.</summary>
            public float SlowShare { get; internal set; } = float.NaN;

            /// <summary>Share of the same cells that are cavalry bad ground.</summary>
            public float BadShare { get; internal set; } = float.NaN;

            /// <summary>Mean run-up an enemy rider has onto the line (m, cap 250).</summary>
            public float RunUp { get; internal set; } = float.NaN;

            // score terms (see BattleTerrainRead.BattlePosition.cs) and the total

            public float TermHeight { get; internal set; }

            public float TermFront { get; internal set; }

            public float TermFlanks { get; internal set; }

            public float TermFire { get; internal set; }

            public float TermApproach { get; internal set; }

            public float TermCavalry { get; internal set; }

            public float TermDead { get; internal set; }

            public float TermMarch { get; internal set; }

            public float TermCaught { get; internal set; }

            public float Total { get; internal set; } = float.NaN;

            // the winner's other places (invalid = none)

            /// <summary>Archers' place: an archer spot id, "behind" or the choke's.</summary>
            public Vec2 Archer { get; internal set; } = Vec2.Invalid;

            public string ArcherSource { get; internal set; }

            /// <summary>Archer place height above Hold (m).</summary>
            public float ArcherGain { get; internal set; } = float.NaN;

            /// <summary>Cavalry reserve's place behind the line.</summary>
            public Vec2 Reserve { get; internal set; } = Vec2.Invalid;

            // infantry reserve plug (RBMTacticReservePlug), winner only (BattleTerrainRead.PlacePositionPlug)

            /// <summary>Where the infantry reserve waits (invalid = none).</summary>
            public Vec2 Plug { get; internal set; } = Vec2.Invalid;

            /// <summary>Hidden from every attacker eye point; false = seen (a fallback tier).</summary>
            public bool PlugHidden { get; internal set; }

            /// <summary>Fallback tier that found the plug: hidden | seen | relaxed | beside | nearest; null = none.</summary>
            public string PlugTier { get; internal set; }

            /// <summary>Each scan tried, "name:tested/passed/hidden".</summary>
            public string PlugTiers { get; internal set; }

            /// <summary>Plug point to Hold (m), seconds to the farther line end, height drop (m), score.</summary>
            public float PlugLineDistance { get; internal set; } = float.NaN;

            public float PlugReach { get; internal set; } = float.NaN;

            public float PlugDrop { get; internal set; } = float.NaN;

            public float PlugScore { get; internal set; } = float.NaN;

            /// <summary>Cells tested, hidden cells, eye points used, rejects per reason.</summary>
            public int PlugCandidates { get; internal set; }

            public int PlugHiddenCells { get; internal set; }

            public int PlugEyes { get; internal set; }

            public string PlugRejects { get; internal set; }
        }

        /// <summary>One evaluation (publish or re-pick): winner, the line it was measured against, runners-up. Immutable.</summary>
        internal sealed class BattlePositionResult
        {
            /// <summary>Chosen place, or null (Why says why).</summary>
            public PositionCandidate Winner { get; internal set; }

            /// <summary>The current line's own score.</summary>
            public PositionCandidate Current { get; internal set; }

            /// <summary>Last plan's hold scored again (re-pick only), else null.</summary>
            public PositionCandidate Previous { get; internal set; }

            /// <summary>Up to three next best, each >= 40 m from the winner and each other.</summary>
            public IReadOnlyList<PositionCandidate> RunnersUp { get; internal set; } = new PositionCandidate[0];

            /// <summary>Winner total minus the current line's.</summary>
            public float Gain { get; internal set; } = float.NaN;

            /// <summary>The weights and limits this evaluation used (how outnumbered the defender was).</summary>
            public BattleTerrainRead.PositionWeights Weights { get; internal set; }

            /// <summary>Army centroids at this evaluation.</summary>
            public Vec2 DefenderAt { get; internal set; } = Vec2.Invalid;

            public Vec2 AttackerAt { get; internal set; } = Vec2.Invalid;

            /// <summary>Candidates generated / scored, rejects per reason.</summary>
            public int Generated { get; internal set; }

            public int Scored { get; internal set; }

            public string Rejects { get; internal set; }

            /// <summary>Why there is no winner, else null.</summary>
            public string Why { get; internal set; }

            /// <summary>Cells the walk floods expanded; ms of the evaluation.</summary>
            public long Expanded { get; internal set; }

            public float Ms { get; internal set; } = float.NaN;
        }

        /// <summary>The step's result and timing, and what a re-pick needs.</summary>
        internal sealed class BattlePositionInfo : Group
        {
            /// <summary>The publish's evaluation (null when the step did not get that far).</summary>
            public BattlePositionResult Result { get; internal set; }

            public string Why { get; internal set; }

            // Re-pick inputs: written once on the worker before the publish.
            internal BattleTerrainRead.PositionGround Ground;
            internal BattleTerrainRead.PositionArmies Armies;
        }
    }
}
