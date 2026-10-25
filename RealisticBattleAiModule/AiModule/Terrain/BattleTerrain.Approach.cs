using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>Attacker approach: candidate ways for the attacker's main infantry to reach the defender's line, scored on the worker (BattleTerrainRead.Approach.cs). Raw data for TerrainApproach. Immutable once published.</summary>
    internal sealed partial class BattleTerrain
    {
        /// <summary>The attacker's approach (Result null = the step did not run, Why says why).</summary>
        public ApproachInfo Approach { get; } = new ApproachInfo();

        internal enum ApproachKind
        {
            Straight,
            /// <summary>The A* route that keeps to open ground (clearance and bad-ground cost).</summary>
            Open,
            /// <summary>Round the defender's OWN left line end.</summary>
            Left,
            /// <summary>Round the defender's OWN right line end.</summary>
            Right,
            /// <summary>Round a held choke, to beside a line end.</summary>
            Bypass
        }

        internal static string ApproachKindName(ApproachKind kind)
        {
            return kind.ToString().ToLowerInvariant();
        }

        /// <summary>One scored way in. Immutable once published.</summary>
        internal sealed class ApproachCandidate
        {
            public ApproachKind Kind { get; internal set; }

            /// <summary>Where the route ends (snapped cell centre) and the point of the line the last straight leg goes to.</summary>
            public Vec2 Deploy { get; internal set; } = Vec2.Invalid;

            public Vec2 Target { get; internal set; } = Vec2.Invalid;

            /// <summary>Route waypoints about every ApproachWaypointSpacing m, start left out, deploy point last.</summary>
            public IReadOnlyList<Vec2> Points { get; internal set; } = new Vec2[0];

            /// <summary>Route (start to deploy), last leg (deploy to target) and both (m).</summary>
            public float RouteLength { get; internal set; } = float.NaN;

            public float AssaultLength { get; internal set; } = float.NaN;

            public float Length { get; internal set; } = float.NaN;

            /// <summary>Walk time with the bad-ground term (s): the detour time test.</summary>
            public float Seconds { get; internal set; } = float.NaN;

            /// <summary>Seconds within reach and sight of a defender shooter / on woods, wet, steep or cavalry bad ground.</summary>
            public float FireSeconds { get; internal set; } = float.NaN;

            public float BadSeconds { get; internal set; } = float.NaN;

            /// <summary>Height gained over the last ApproachClimbSpan m into the line (m, + = uphill).</summary>
            public float Climb { get; internal set; } = float.NaN;

            /// <summary>Narrowest walkable width along the route (2 x clearance, m), its ends left out.</summary>
            public float Bottleneck { get; internal set; } = float.NaN;

            /// <summary>The held choke the way crosses (id), else null.</summary>
            public string Choke { get; internal set; }

            /// <summary>Arrives at an open (not covered) line end.</summary>
            public bool FlankOpen { get; internal set; }

            // score terms (seconds, lower is better) and the total

            public float TermWalk { get; internal set; }

            public float TermFire { get; internal set; }

            public float TermClimb { get; internal set; }

            public float TermBad { get; internal set; }

            public float TermChoke { get; internal set; }

            public float TermNarrow { get; internal set; }

            public float TermFlank { get; internal set; }

            public float Total { get; internal set; } = float.NaN;

            /// <summary>Gain over straight ((straight - this) / straight), NaN for straight or unscored.</summary>
            public float Gain { get; internal set; } = float.NaN;

            /// <summary>Why it was not scored or not chosen, null for the chosen one.</summary>
            public string Why { get; internal set; }

            public long Expanded { get; internal set; }
        }

        /// <summary>One evaluation (publish or re-plan). Immutable.</summary>
        internal sealed class ApproachResult
        {
            public IReadOnlyList<ApproachCandidate> Candidates { get; internal set; } = new ApproachCandidate[0];

            public ApproachCandidate Straight { get; internal set; }

            /// <summary>The way to take; Straight when no detour qualifies, null without a straight route (Why says why).</summary>
            public ApproachCandidate Chosen { get; internal set; }

            public string Why { get; internal set; }

            /// <summary>The armies it was scored for.</summary>
            public Vec2 From { get; internal set; } = Vec2.Invalid;

            public Vec2 Line { get; internal set; } = Vec2.Invalid;

            public Vec2 Facing { get; internal set; } = Vec2.Invalid;

            public Vec2 LineDirection { get; internal set; } = Vec2.Invalid;

            public float HalfWidth { get; internal set; } = float.NaN;

            public string LineSource { get; internal set; }

            public bool LeftCovered { get; internal set; }

            public bool RightCovered { get; internal set; }

            public string HeldChoke { get; internal set; }

            public float Pace { get; internal set; } = float.NaN;

            public float ArcherScale { get; internal set; } = float.NaN;

            public int Shooters { get; internal set; }

            public float MinGain { get; internal set; } = float.NaN;

            public float Ms { get; internal set; } = float.NaN;
        }

        internal sealed class ApproachInfo : Group
        {
            public ApproachResult Result { get; internal set; }

            public string Why { get; internal set; }
        }

        /// <summary>TerrainApproach's live re-plan. Any thread: pure grid work with its own A* scratch, never writes to this snapshot.</summary>
        internal ApproachResult ReplanApproach(BattleTerrainRead.ApproachArmies armies)
        {
            return BattleTerrainRead.EvaluateApproach(this, _grid, armies);
        }
    }
}
