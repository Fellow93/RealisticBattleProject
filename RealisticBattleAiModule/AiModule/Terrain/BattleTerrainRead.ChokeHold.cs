using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// Choke hold: the defender's best narrow passage to hold (BattleTerrain.ChokeHold). BACKGROUND THREAD: grid, frame,
    /// feature-map chokes and archer spots only, no engine call.
    ///
    /// A choke qualifies when, in order (the first failed test is its "why"): the armies are not on the same side of it
    /// (opposite sides = a CROSSING the attacker must come through); it lies between the armies or on the attacker's way
    /// (a crossing anywhere); its narrowest line can be measured and (not a crossing) runs along the battle axis; (not a
    /// crossing) the armies are on its two sides by geometry; it is narrow for the defender's infantry frontage; a hold
    /// point exists ("gap": ChokeHoldBack behind the line, centred in the span; else for a crossing at its MOUTH, a line
    /// across the exit); it is within ChokeMaxDistance of the defender; the defender reaches it ChokeReachMargin s before
    /// the attacker (grid A*; an outnumbered defender looks up to OutChokeMaxDistance out with a margin down to
    /// OutChokeReachMargin, see the battle position's PositionWeights). Score = min(infantry frontage / line width, 2) - distance penalty (- ChokeOtherWayPenalty
    /// for a crossing that does not separate the armies); the best is kept.
    ///
    /// For the kept choke: hold width, both-flanks-covered, an archer place (best archer spot near the hold, else the
    /// highest roomy cell behind) and a cavalry reserve place.
    /// </summary>
    internal static partial class BattleTerrainRead
    {
        internal const float ChokeMinAt = -0.1f;
        internal const float ChokeMaxAt = 1.1f;
        /// <summary>On the attacker's way: within this (m, or half the choke's width + 10 m) of the A -> D grid path.</summary>
        internal const float ChokePathNear = 30f;
        /// <summary>... or Between the armies and at most this far (m) off the A -> D axis.</summary>
        internal const float ChokeAxisMaxAcross = 80f;
        /// <summary>The gap axis must run at least this much along the battle axis (dot product).</summary>
        internal const float ChokeMinAlongAxis = 0.4f;
        internal const float ChokeAttackerMinAhead = 40f;
        internal const float ChokeDefenderMaxAhead = 30f;
        /// <summary>The line's width: at least this (m), at most ChokeMaxFront x the defender's infantry frontage.</summary>
        internal const float ChokeMinWidth = 20f;
        internal const float ChokeMaxFront = 1.3f;
        /// <summary>Infantry frontage estimate: men / ranks x spacing (m), at least ChokeMinFrontage.</summary>
        internal const float ChokeRanks = 4f;
        internal const float ChokeFileSpacing = 1.1f;
        internal const float ChokeMinFrontage = 20f;
        internal const float ChokeHoldBack = 20f; // m behind the narrowest line, first try
        private static readonly float[] ChokeHoldBacks = { 20f, 15f, 25f, 10f, 30f, 5f, 35f };
        /// <summary>Mouth hold: metres out from the defender's mouth of a crossing (first that is walkable, dry, off the edge).</summary>
        private static readonly float[] ChokeMouthBacks = { 10f, 15f, 5f, 20f, 30f, 0f, 40f };
        /// <summary>A mouth hold's line reaches this far (m) past each wall of the gap.</summary>
        internal const float ChokeMouthWing = 12f;
        private const float ChokeOtherWayPenalty = 0.3f;
        internal const float ChokeMaxHoldSpan = 1.6f; // x infantry frontage
        internal const float ChokeHoldWidthShare = 0.95f;
        internal const float ChokeCoverSpan = 1.3f; // both flanks covered only at or below this x infantry frontage
        internal const float ChokeMaxDistance = 300f;
        internal const float ChokeReachMargin = 20f;
        private const int ChokePathRuns = 16;
        private const float ChokeDistancePenalty = 0.5f;
        internal const float ChokeArcherSpotRange = 120f;
        private const float ChokeArcherMaxAhead = 5f;
        internal const float ChokeArcherBehind = 35f;
        private const float ChokeArcherSearch = 20f;
        private const float ChokeArcherMinBehind = 15f;
        private const float ChokeArcherMaxSlope = 20f;
        internal const float ChokeReserveBehind = 60f;
        private const float ChokeReserveSearch = 25f;

        private static void ReadChokeHold(Frame f)
        {
            BattleTerrain.ChokeHoldInfo info = f.Terrain.ChokeHold;
            Grid grid = f.Grid;
            Side atk = f.Atk;
            Side def = f.Def;
            int infantry = Math.Max(0, def.Foot - Math.Min(def.Ranged, def.Foot));
            float infantryFrontage = Math.Max(ChokeMinFrontage, infantry / ChokeRanks * ChokeFileSpacing);
            info.Infantry = infantry;
            info.InfantryFrontage = infantryFrontage;
            // outnumbered: looks farther and takes a smaller reach margin (never one the attacker's riders beat)
            string source;
            float ratio = FrameOutnumber(f, out source);
            float k = OutnumberedK(ratio);
            info.Outnumbered = ratio;
            info.OutnumberedK = k;
            info.OutnumberedSource = source;
            info.MaxDistance = ChokeMaxDistance + (OutChokeMaxDistance - ChokeMaxDistance) * k;
            info.ReachMargin = Math.Max(PosMinReachMargin, ChokeReachMargin + (OutChokeReachMargin - ChokeReachMargin) * k);
            info.CavalryCheck = atk.Men > 0 && (float)atk.Mounted / atk.Men >= OutCavalryShare;
            if (f.Terrain.Chokes.Count == 0)
            {
                info.Why = "noChokes";
                return;
            }
            if (def.Snap < 0)
            {
                info.Why = "noDefenderSnap";
                return;
            }

            StringBuilder why = new StringBuilder();
            BattleTerrain.ChokeHoldPlace best = null;
            int pathRuns = 0;
            foreach (BattleTerrain.ChokeFeature choke in f.Terrain.Chokes)
            {
                string note;
                BattleTerrain.ChokeHoldPlace place = TryChoke(f, choke, infantryFrontage, info, ref pathRuns, out note);
                if (why.Length > 0)
                {
                    why.Append(' ');
                }
                why.Append(choke.Id).Append(':').Append(note);
                if (place != null && (best == null || place.Score > best.Score))
                {
                    best = place;
                }
            }
            if (best != null)
            {
                PlaceArchers(f, best);
                PlaceReserve(grid, best);
            }
            info.Defender = best;
            info.Why = why.ToString();
            info.PathRuns = pathRuns;
        }

        private static BattleTerrain.ChokeHoldPlace TryChoke(Frame f, BattleTerrain.ChokeFeature choke, float infantryFrontage, BattleTerrain.ChokeHoldInfo limits,
            ref int pathRuns, out string note)
        {
            Grid grid = f.Grid;
            Side atk = f.Atk;
            Side def = f.Def;

            // same side = nobody has to pass it; opposite sides = a crossing, wherever it lies
            if (choke.Sides == BattleTerrain.ChokeSides.Same)
            {
                note = "sameSide";
                return null;
            }
            bool crossing = choke.Sides == BattleTerrain.ChokeSides.Opposite && choke.DefenderSide >= 0;

            if (!crossing && (float.IsNaN(choke.At) || choke.At < ChokeMinAt || choke.At > ChokeMaxAt))
            {
                note = "notBetween(at=" + F2(choke.At) + ")";
                return null;
            }
            float pathDistance = PathDistance(f, choke.Position);
            float pathNear = Math.Max(ChokePathNear, (float.IsNaN(choke.Width) ? 0f : choke.Width * 0.5f) + 10f);
            bool onPath = !float.IsNaN(pathDistance) && pathDistance <= pathNear;
            bool onAxis = choke.Between && !float.IsNaN(choke.Across) && Math.Abs(choke.Across) <= ChokeAxisMaxAcross;
            if (!onPath && !onAxis && !crossing)
            {
                note = "offPath(path=" + F0(pathDistance) + "m across=" + F0(choke.Across) + "m)";
                return null;
            }

            int cell = Snap(grid, choke.Position, 12f);
            if (cell < 0)
            {
                note = "noGround";
                return null;
            }
            Vec2 p = grid.CellCentre(cell);
            float lineWidth = float.MaxValue;
            Vec2 lineDir = Vec2.Invalid;
            for (int deg = 0; deg < 180; deg += 10)
            {
                double a = deg * Math.PI / 180.0;
                Vec2 dir = new Vec2((float)Math.Cos(a), (float)Math.Sin(a));
                float w = FullWidth(grid, p, dir);
                if (w < lineWidth)
                {
                    lineWidth = w;
                    lineDir = dir;
                }
            }
            if (!lineDir.IsValid || lineWidth == float.MaxValue)
            {
                note = "noGround";
                return null;
            }
            // facing through the gap toward the attacker (a crossing: toward its mouth)
            Vec2 facing = new Vec2(-lineDir.y, lineDir.x);
            Vec2 attackerMouth = crossing ? choke.MouthOf(1 - choke.DefenderSide) : Vec2.Invalid;
            Vec2 toward = attackerMouth.IsValid ? attackerMouth : atk.C2;
            if (Vec2.DotProduct(toward - p, facing) < 0f)
            {
                facing = -facing;
            }
            if (!crossing)
            {
                if (Math.Abs(Vec2.DotProduct(facing, f.Forward)) < ChokeMinAlongAxis)
                {
                    note = "sideways(" + F2(Math.Abs(Vec2.DotProduct(facing, f.Forward))) + ")";
                    return null;
                }
                float attackerAhead = Vec2.DotProduct(atk.C2 - p, facing);
                float defenderAhead = Vec2.DotProduct(def.C2 - p, facing);
                if (attackerAhead < ChokeAttackerMinAhead)
                {
                    note = "attackerThrough(" + F0(attackerAhead) + "m)";
                    return null;
                }
                if (defenderAhead > ChokeDefenderMaxAhead)
                {
                    note = "defenderBeyond(" + F0(defenderAhead) + "m)";
                    return null;
                }
            }

            // a crossing too narrow to stand in is held at its mouth
            if (lineWidth < ChokeMinWidth && !crossing)
            {
                note = "tooNarrow(" + F0(lineWidth) + "m)";
                return null;
            }
            if (lineWidth > ChokeMaxFront * infantryFrontage)
            {
                note = "wide(" + F0(lineWidth) + ">" + F0(ChokeMaxFront * infantryFrontage) + "m)";
                return null;
            }

            Vec2 hold = Vec2.Invalid;
            float holdBack = float.NaN;
            float span = float.NaN;
            float holdWidth = float.NaN;
            Vec2 holdLine = lineDir;
            BattleTerrain.End leftEnd = BattleTerrain.End.Max;
            BattleTerrain.End rightEnd = BattleTerrain.End.Max;
            int holdCell = -1;
            bool atMouth = false;
            if (lineWidth >= ChokeMinWidth)
            {
                foreach (float back in ChokeHoldBacks)
                {
                    Vec2 q = p - facing * back;
                    int k = grid.IndexOf(q);
                    if (!grid.IsWalkable(k) || grid.Wet[k] || grid.NearEdge(k) || !StraightWalkable(grid, p, q))
                    {
                        continue;
                    }
                    BattleTerrain.End l, r;
                    float left = HalfWidth(grid, q, lineDir, out l);
                    float right = HalfWidth(grid, q, -lineDir, out r);
                    float s = left + right;
                    if (s > ChokeMaxHoldSpan * infantryFrontage)
                    {
                        continue;
                    }
                    Vec2 centre = q + lineDir * ((left - right) * 0.5f);
                    int c = grid.IndexOf(centre);
                    if (!grid.IsWalkable(c) || grid.Wet[c] || grid.NearEdge(c))
                    {
                        continue;
                    }
                    hold = centre;
                    holdBack = back;
                    span = s;
                    leftEnd = l;
                    rightEnd = r;
                    holdCell = c;
                    holdWidth = s * ChokeHoldWidthShare;
                    break;
                }
            }
            if (holdCell < 0 && crossing)
            {
                // mouth: the line stands across the defender's mouth, ChokeMouthBacks m further out, wide enough to close the exit, facing back into the passage
                Vec2 mouth = choke.MouthOf(choke.DefenderSide);
                if (!mouth.IsValid)
                {
                    note = "noMouth";
                    return null;
                }
                Vec2 exit = mouth - p;
                if (exit.Normalize() < 1f)
                {
                    exit = -facing;
                }
                foreach (float back in ChokeMouthBacks)
                {
                    Vec2 q = mouth + exit * back;
                    int k = grid.IndexOf(q);
                    if (!grid.IsWalkable(k) || grid.Wet[k] || grid.NearEdge(k) || !StraightWalkable(grid, mouth, q))
                    {
                        continue;
                    }
                    hold = q;
                    holdCell = k;
                    holdBack = q.Distance(p);
                    break;
                }
                if (holdCell < 0)
                {
                    note = "noMouthGround";
                    return null;
                }
                atMouth = true;
                facing = -exit;
                holdLine = new Vec2(-facing.y, facing.x);
                HalfWidth(grid, p, lineDir, out leftEnd);
                HalfWidth(grid, p, -lineDir, out rightEnd);
                span = lineWidth;
                holdWidth = Math.Min(infantryFrontage * ChokeHoldWidthShare, Math.Max(ChokeMinWidth, lineWidth + 2f * ChokeMouthWing));
            }
            if (holdCell < 0)
            {
                note = (lineWidth < ChokeMinWidth) ? "tooNarrow(" + F0(lineWidth) + "m)" : "noHoldGround";
                return null;
            }

            float distance = hold.Distance(def.C2);
            if (distance > limits.MaxDistance)
            {
                note = "far(" + F0(distance) + "m)";
                return null;
            }
            if (pathRuns + 2 > ChokePathRuns)
            {
                note = "noBudget";
                return null;
            }
            float ours = WalkSeconds(f, def, holdCell);
            float theirs = WalkSeconds(f, atk, cell);
            pathRuns += 2;
            if (float.IsNaN(ours))
            {
                note = "noRoute";
                return null;
            }
            if (!float.IsNaN(theirs) && ours + ChokeReachMargin > theirs
                && !(limits.ReachMargin < ChokeReachMargin && ours + limits.ReachMargin <= theirs
                     && BeatsRiders(limits.CavalryCheck, ours, theirs, atk.WalkSpeed, atk.MountedSpeed)))
            {
                note = "late(def=" + F0(ours) + "s atk=" + F0(theirs) + "s)";
                return null;
            }

            // a crossing with another way across can be turned: worth less
            float score = Math.Min(infantryFrontage / Math.Max(1f, lineWidth), 2f) - ChokeDistancePenalty * distance / ChokeMaxDistance
                - ((crossing && !choke.Separates) ? ChokeOtherWayPenalty : 0f);
            note = "ok(" + F2(score) + (atMouth ? " mouth" : "") + ")";
            return new BattleTerrain.ChokeHoldPlace
            {
                ChokeId = choke.Id,
                Choke = p,
                FeatureWidth = choke.Width,
                LineWidth = lineWidth,
                LineDirection = holdLine,
                Facing = facing,
                Hold = hold,
                HoldBack = holdBack,
                HoldSpan = span,
                HoldLeftEnd = leftEnd,
                HoldRightEnd = rightEnd,
                HoldWidth = holdWidth,
                AtMouth = atMouth,
                Crossing = crossing,
                // at a mouth the passage itself covers the flanks while it is the only way across
                BothFlanksCovered = atMouth
                    ? choke.Separates && Closed(leftEnd) && Closed(rightEnd)
                    : Closed(leftEnd) && Closed(rightEnd) && span <= ChokeCoverSpan * infantryFrontage,
                InfantryFrontage = infantryFrontage,
                AttackerFrontage = atk.Frontage,
                DefenderDistance = distance,
                DefenderSeconds = ours,
                AttackerSeconds = theirs,
                Score = score
            };
        }

        /// <summary>The archers' place; left invalid when none fits.</summary>
        private static void PlaceArchers(Frame f, BattleTerrain.ChokeHoldPlace place)
        {
            Grid grid = f.Grid;
            float holdHeight = grid.HeightAt(place.Hold);
            BattleTerrain.ArcherSpot bestSpot = null;
            foreach (BattleTerrain.ArcherSpot spot in f.Terrain.Spots.Defender.Spots)
            {
                Vec2 rel = spot.Position - place.Hold;
                if (rel.Length > ChokeArcherSpotRange || Vec2.DotProduct(rel, place.Facing) > ChokeArcherMaxAhead
                    || Vec2.DotProduct(spot.Position - place.Choke, place.Facing) >= 0f)
                {
                    continue;
                }
                if (bestSpot == null || spot.Score > bestSpot.Score)
                {
                    bestSpot = spot;
                }
            }
            if (bestSpot != null)
            {
                place.Archer = bestSpot.Position;
                place.ArcherSource = bestSpot.Id;
                place.ArcherGain = IsPlausible(holdHeight) ? bestSpot.Height - holdHeight : float.NaN;
                return;
            }
            Vec2 centre = place.Hold - place.Facing * ChokeArcherBehind;
            int bestCell = -1;
            float bestHeight = float.NegativeInfinity;
            ForCellsWithin(grid, centre, ChokeArcherSearch, k =>
            {
                if (!grid.IsWalkable(k) || grid.Wet[k] || grid.NearEdge(k) || !IsPlausible(grid.Heights[k]))
                {
                    return;
                }
                float slope = grid.Slopes[k];
                if (float.IsNaN(slope) || slope > ChokeArcherMaxSlope)
                {
                    return;
                }
                if (Vec2.DotProduct(grid.CellCentre(k) - place.Hold, place.Facing) > -ChokeArcherMinBehind)
                {
                    return;
                }
                if (grid.Heights[k] > bestHeight)
                {
                    bestHeight = grid.Heights[k];
                    bestCell = k;
                }
            });
            if (bestCell >= 0)
            {
                place.Archer = grid.CellCentre(bestCell);
                place.ArcherSource = "behind";
                place.ArcherGain = IsPlausible(holdHeight) ? bestHeight - holdHeight : float.NaN;
            }
        }

        /// <summary>The cavalry reserve's place: the dry walkable cell nearest the point ChokeReserveBehind m behind the hold point.</summary>
        private static void PlaceReserve(Grid grid, BattleTerrain.ChokeHoldPlace place)
        {
            Vec2 centre = place.Hold - place.Facing * ChokeReserveBehind;
            int bestCell = -1;
            float bestDistance = float.MaxValue;
            ForCellsWithin(grid, centre, ChokeReserveSearch, k =>
            {
                if (!grid.IsWalkable(k) || grid.Wet[k] || grid.NearEdge(k))
                {
                    return;
                }
                float d = grid.CellCentre(k).Distance(centre);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestCell = k;
                }
            });
            if (bestCell >= 0)
            {
                place.Reserve = grid.CellCentre(bestCell);
            }
        }

        /// <summary>Calls <paramref name="visit"/> for every cell whose centre lies within <paramref name="radius"/> of <paramref name="centre"/>.</summary>
        private static void ForCellsWithin(Grid grid, Vec2 centre, float radius, Action<int> visit)
        {
            if (float.IsNaN(centre.x) || float.IsNaN(centre.y))
            {
                return;
            }
            int ci = (int)Math.Floor((centre.x - grid.MinX) / grid.Step);
            int cj = (int)Math.Floor((centre.y - grid.MinY) / grid.Step);
            int r = (int)Math.Ceiling(radius / grid.Step);
            for (int j = Math.Max(0, cj - r); j <= Math.Min(grid.Ny - 1, cj + r); j++)
            {
                for (int i = Math.Max(0, ci - r); i <= Math.Min(grid.Nx - 1, ci + r); i++)
                {
                    int k = j * grid.Nx + i;
                    if (grid.CellCentre(k).Distance(centre) <= radius)
                    {
                        visit(k);
                    }
                }
            }
        }

        /// <summary>A width walk that ended at something closing the line's end (no navmesh, water, steep, the map edge).</summary>
        private static bool Closed(BattleTerrain.End end)
        {
            return end != BattleTerrain.End.Max && end != BattleTerrain.End.None;
        }

        /// <summary>Every cell on the straight line a -> b (one sample per grid step) is walkable.</summary>
        private static bool StraightWalkable(Grid grid, Vec2 a, Vec2 b)
        {
            float length = a.Distance(b);
            int samples = Math.Max(1, (int)Math.Ceiling(length / grid.Step));
            for (int s = 1; s <= samples; s++)
            {
                if (!grid.IsWalkable(grid.IndexOf(a + (b - a) * ((float)s / samples))))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Metres from p to the nearest cell of the A -> D grid path (every cell); NaN without a path.</summary>
        private static float PathDistance(Frame f, Vec2 p)
        {
            if (f.Path.Count == 0)
            {
                return float.NaN;
            }
            float best = float.MaxValue;
            foreach (int k in f.Path)
            {
                float d = f.Grid.CellCentre(k).Distance(p);
                if (d < best)
                {
                    best = d;
                }
            }
            return best;
        }

        private static string F0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string F2(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
