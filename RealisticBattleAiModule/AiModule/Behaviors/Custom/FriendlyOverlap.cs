using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Would a formation standing at a planned point overlap its friends? (user rule). Shared by every planned point:
    /// TerrainSpots (archer spots), TerrainCavalry (lanes), CavalryReform (reform points) and the fixed per-plan places
    /// (PlaceShift: HoldPosition / HoldChoke Archers + Reserve roles, ReservePlug). Not for the main line's own hold point
    /// nor the hedgehog's archers inside the square.
    ///
    /// Footprints by the front-centre rule: the Move point is the FRONT-centre, ranks stand BEHIND it along the facing. A friend
    /// counts with its CURRENT footprint and its DESTINATION (cached OrderPosition as front-centre; skipped under Charge /
    /// ChargeWithTarget / Retreat). Friends = non-empty formations of every team on the side, minus the formation and <c>ignore</c>;
    /// rectangles grow by Margin.
    ///
    /// Share = part of the formation's own footprint (at least MinWidth x MinDepth) inside one friend's footprint (now OR heading),
    /// sampled on an Across x Along grid; the worst friend is returned. TAKEN at share &lt;= TakeMaxShare, KEPT while &lt;= KeepMaxShare.
    ///
    /// PlaceShift (one per behavior instance): when a fixed place overlaps a friend's DESTINATION by more than TakeMaxShare the
    /// target is moved ONCE along the plan's facing away from that friend (overlap + ShiftGap, at most ShiftMax) onto walkable ground:
    /// a copy of the plan's cached WorldPosition with ONE SetVec2MT, cached, rebuilt on plan/friend moves. Destinations only (a
    /// friend passing through would leave it shifted for good). Cohesion: a shift beyond the FormationCohesion limit is shortened
    /// (" cohesionCut") or refused.
    ///
    /// THREADING: AI-thread safe (cached data, immutable terrain grid); no engine call, no MBRandom; PlaceShift fields are AI-thread only.
    /// </summary>
    internal static class FriendlyOverlap
    {

        internal const float TakeMaxShare = 0.3f;
        internal const float KeepMaxShare = 0.5f; // above TakeMaxShare: hysteresis
        internal const float Margin = 2f; // m, each friend's footprint grows on every side
        internal const float MinWidth = 4f; // m; a tiny formation still has a place
        internal const float MinDepth = 2f;

        /// <summary>Sample grid over the footprint, Across x Along cell centres.</summary>
        private const int Across = 5;

        private const int Along = 3;

        // PlaceShift
        internal const float ShiftGap = 5f; // m clear of the friend's footprint
        internal const float ShiftMax = 80f; // m; farther = no shift ("noShift tooFar")
        internal const float ShiftRebuild = 5f; // m; friend's order position / depth or the plan's point moved
        private const float ShiftTurnCos = 0.985f; // plan facing turned ~10 deg
        private const float ShiftEdgeMin = 10f; // m from the map edge
        private const float ShiftCohesionStep = 5f; // m; a shift beyond the cohesion limit is shortened in these steps

        internal enum Mode
        {
            /// <summary>Picking / holding a point.</summary>
            NowAndHeading,
            /// <summary>Its current footprint when it has no order position: the fixed places.</summary>
            HeadingOnly
        }

        /// <summary>AI thread: the worst share (0..1) one friend covers (point is front-centre; facing invalid = its Direction); that friend in friend (null at 0).</summary>
        internal static float Share(Formation formation, Vec2 point, Vec2 facing, Formation ignore, Mode mode, out Formation friend)
        {
            friend = null;
            Mission mission = Mission.Current;
            if (mission == null || formation == null || formation.Team == null || !point.IsValid)
            {
                return 0f;
            }
            Vec2 f = Unit(facing);
            if (!f.IsValid)
            {
                f = Unit(formation.Direction);
                if (!f.IsValid)
                {
                    return 0f;
                }
            }
            Vec2 side = new Vec2(-f.y, f.x);
            float width = MathF.Max(formation.Width, MinWidth);
            float depth = MathF.Max(formation.Depth, MinDepth);
            BattleSideEnum own = formation.Team.Side;
            float best = 0f;
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != own || own == BattleSideEnum.None)
                {
                    continue;
                }
                foreach (Formation other in team.FormationsIncludingSpecialAndEmpty)
                {
                    Footprints prints;
                    if (other == null || other == formation || other == ignore || other.CountOfUnits <= 0 || !Read(other, mode, out prints))
                    {
                        continue;
                    }
                    int inside = 0;
                    for (int i = 0; i < Across; i++)
                    {
                        float x = ((i + 0.5f) / Across - 0.5f) * width;
                        for (int j = 0; j < Along; j++)
                        {
                            float y = (j + 0.5f) / Along * depth;
                            Vec2 s = point + side * x - f * y;
                            if (prints.Contains(s))
                            {
                                inside++;
                            }
                        }
                    }
                    float share = inside / (float)(Across * Along);
                    if (share > best)
                    {
                        best = share;
                        friend = other;
                    }
                }
            }
            return best;
        }

        internal static string Describe(Formation friend, float share)
        {
            return "friendOverlap " + TerrainCavalry.FormationName(friend) + " share=" + share.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>No spaces (per-point refusal lists).</summary>
        internal static string RefusalText(Formation friend, float share)
        {
            return "friendOverlap(" + TerrainCavalry.FormationName(friend) + "," + share.ToString("0.##", CultureInfo.InvariantCulture) + ")";
        }


        /// <summary>Oriented rectangle, Margin included.</summary>
        private struct Rect
        {
            public Vec2 Centre;
            public Vec2 Dir;
            public float HalfWidth;
            public float HalfDepth;

            public bool Contains(Vec2 s)
            {
                float rx = s.x - Centre.x;
                float ry = s.y - Centre.y;
                float along = rx * Dir.x + ry * Dir.y;
                if (along > HalfDepth || along < -HalfDepth)
                {
                    return false;
                }
                float across = -rx * Dir.y + ry * Dir.x;
                return across <= HalfWidth && across >= -HalfWidth;
            }

            public float ExtentAlong(Vec2 f)
            {
                float d = Dir.x * f.x + Dir.y * f.y;
                float w = -Dir.y * f.x + Dir.x * f.y;
                return MathF.Abs(d) * HalfDepth + MathF.Abs(w) * HalfWidth;
            }
        }

        private struct Footprints
        {
            public bool HasNow;
            public Rect Now;
            public bool HasHeading;
            public Rect Heading;

            public bool Contains(Vec2 s)
            {
                return (HasNow && Now.Contains(s)) || (HasHeading && Heading.Contains(s));
            }
        }

        /// <summary>Cached data only; false when it has none.</summary>
        private static bool Read(Formation other, Mode mode, out Footprints prints)
        {
            prints = default(Footprints);
            Vec2 dir = Unit(other.Direction);
            float width = MathF.Max(other.Width, 1f);
            float depth = MathF.Max(other.Depth, 1f);
            float halfWidth;
            float halfDepth;
            if (dir.IsValid)
            {
                halfWidth = width * 0.5f + Margin;
                halfDepth = depth * 0.5f + Margin;
            }
            else
            {
                // no facing: a square as large as its larger side
                dir = new Vec2(0f, 1f);
                halfWidth = halfDepth = MathF.Max(width, depth) * 0.5f + Margin;
            }
            bool heading = HeadingPoint(other, out Vec2 front);
            if (heading)
            {
                prints.HasHeading = true;
                prints.Heading = new Rect { Centre = front - dir * (depth * 0.5f), Dir = dir, HalfWidth = halfWidth, HalfDepth = halfDepth };
            }
            if (mode == Mode.NowAndHeading || !heading)
            {
                if (mode == Mode.HeadingOnly && IsLeaving(other))
                {
                    return prints.HasHeading;
                }
                Vec2 centre = RBMAI.Utilities.GetFormationCenter(other);
                if (centre.IsValid)
                {
                    prints.HasNow = true;
                    prints.Now = new Rect { Centre = centre, Dir = dir, HalfWidth = halfWidth, HalfDepth = halfDepth };
                }
            }
            return prints.HasNow || prints.HasHeading;
        }

        /// <summary>Cached order position (front-centre); none when it charges / retreats. Shared with FormationCohesion.</summary>
        internal static bool HeadingPoint(Formation other, out Vec2 front)
        {
            front = Vec2.Invalid;
            if (IsLeaving(other) || !other.OrderPositionIsValid)
            {
                return false;
            }
            front = other.OrderPosition;
            return front.IsValid;
        }

        /// <summary>Under Charge / ChargeWithTarget / Retreat or its charge advance (= FormationCohesion.OrderExemption != null).</summary>
        private static bool IsLeaving(Formation other)
        {
            OrderType order = other.GetReadonlyMovementOrderReference().OrderType;
            // ChargeGuard's advance Move is a charge on its way: its order position sits at the enemy
            return order == OrderType.Charge || order == OrderType.ChargeWithTarget || order == OrderType.Retreat || ChargeGuard.IsAdvancingToCharge(other);
        }

        /// <summary>Normalized, or Invalid below 0.01 m. Shared with FormationCohesion.</summary>
        internal static Vec2 Unit(Vec2 v)
        {
            if (!v.IsValid || v.LengthSquared < 0.0001f)
            {
                return Vec2.Invalid;
            }
            return v.Normalized();
        }


        /// <summary>One behavior's shifted fixed place. AI thread only. Resolve every order calculation with the plan's place; Reset on re-assignment.</summary>
        internal sealed class PlaceShift
        {
            private enum ShiftState
            {
                None,
                Shifted,
                Refused
            }

            private ShiftState _state = ShiftState.None;
            private Vec2 _source = Vec2.Invalid;
            private Vec2 _facing = Vec2.Invalid;
            private Formation _friend;
            private Vec2 _friendAt = Vec2.Invalid;
            private float _friendDepth = float.NaN;
            private Vec2 _shifted = Vec2.Invalid;
            private WorldPosition _target = WorldPosition.Invalid;
            private FormationCohesion.Kind _cohesionKind = FormationCohesion.Kind.Auto;
            private Formation _cohesionAnchor;

            internal void Reset()
            {
                _state = ShiftState.None;
                _source = Vec2.Invalid;
                _facing = Vec2.Invalid;
                _friend = null;
                _friendAt = Vec2.Invalid;
                _friendDepth = float.NaN;
                _shifted = Vec2.Invalid;
                _target = WorldPosition.Invalid;
            }

            /// <summary>AI thread: the place to move to for the plan's place: the plan's own, or a cached shifted copy. what / why = a log note (shift | unshift | noShift) when the decision changed. cohesionKind / cohesionAnchor: the FormationCohesion limit the shift keeps to (anchor null = the main infantry).</summary>
            internal WorldPosition Resolve(Formation formation, WorldPosition planned, Vec2 facing, out Vec2 point, out string what, out string why,
                FormationCohesion.Kind cohesionKind = FormationCohesion.Kind.Auto, Formation cohesionAnchor = null)
            {
                what = null;
                why = null;
                _cohesionKind = cohesionKind;
                _cohesionAnchor = cohesionAnchor;
                Vec2 source = planned.AsVec2;
                point = source;
                Vec2 f = Unit(facing);
                if (formation == null || !source.IsValid || !f.IsValid)
                {
                    return planned;
                }
                bool rebuild = !_source.IsValid || _source.DistanceSquared(source) > ShiftRebuild * ShiftRebuild
                    || !_facing.IsValid || Vec2.DotProduct(_facing, f) < ShiftTurnCos;
                Formation seen;
                if (!rebuild && _state != ShiftState.None)
                {
                    Formation friend = _friend;
                    Vec2 friendAt;
                    rebuild = friend == null || friend.CountOfUnits <= 0 || !FriendAnchor(friend, out friendAt)
                        || friendAt.DistanceSquared(_friendAt) > ShiftRebuild * ShiftRebuild || MathF.Abs(friend.Depth - _friendDepth) > ShiftRebuild;
                    if (!rebuild && _state == ShiftState.Shifted)
                    {
                        rebuild = Share(formation, _shifted, f, null, Mode.HeadingOnly, out seen) > KeepMaxShare;
                    }
                }
                else if (!rebuild)
                {
                    rebuild = Share(formation, source, f, null, Mode.HeadingOnly, out seen) > TakeMaxShare;
                }
                if (rebuild)
                {
                    Rebuild(formation, planned, source, f, out what, out why);
                }
                if (_state == ShiftState.Shifted)
                {
                    point = _shifted;
                    return _target;
                }
                return planned;
            }

            private void Rebuild(Formation formation, WorldPosition planned, Vec2 source, Vec2 f, out string what, out string why)
            {
                what = null;
                why = null;
                ShiftState old = _state;
                _source = source;
                _facing = f;
                Formation friend;
                float share = Share(formation, source, f, null, Mode.HeadingOnly, out friend);
                if (share <= TakeMaxShare || friend == null)
                {
                    _state = ShiftState.None;
                    _friend = null;
                    if (old != ShiftState.None)
                    {
                        what = "unshift";
                        why = "share=" + share.ToString("0.##", CultureInfo.InvariantCulture) + " (<= " + TakeMaxShare.ToString("0.##", CultureInfo.InvariantCulture) + ")";
                    }
                    return;
                }
                _friend = friend;
                Vec2 friendAt;
                _friendAt = FriendAnchor(friend, out friendAt) ? friendAt : Vec2.Invalid;
                _friendDepth = friend.Depth;
                string refused = null;
                float offset = 0f;
                Footprints prints;
                if (!Read(friend, Mode.HeadingOnly, out prints))
                {
                    refused = "noFootprint";
                }
                else
                {
                    Rect r = prints.HasHeading ? prints.Heading : prints.Now;
                    float depth = MathF.Max(formation.Depth, MinDepth);
                    float centre = Vec2.DotProduct(r.Centre - source, f);
                    float extent = r.ExtentAlong(f);
                    // our footprint runs from the place (0) back to -depth: go behind the friend when level with or ahead of our middle, else in front
                    offset = (centre >= -depth * 0.5f) ? -(ShiftGap - (centre - extent)) : (centre + extent) + ShiftGap + depth;
                    Vec2 moved = source + f * offset;
                    BattleTerrain terrain = BattleTerrain.Current;
                    Formation after;
                    float shareAfter;
                    string cut = null;
                    if (MathF.Abs(offset) > ShiftMax)
                    {
                        refused = "tooFar " + TerrainBattlePosition.Fmt0(MathF.Abs(offset)) + "m";
                    }
                    else if (!CohesionCap(formation, source, f, ref offset, ref moved, out cut))
                    {
                        refused = cut;
                        cut = null;
                    }
                    else if (terrain == null || !terrain.IsWalkable(moved) || terrain.EdgeDistanceAt(moved) < ShiftEdgeMin)
                    {
                        refused = (terrain == null) ? "noTerrain" : "ground";
                    }
                    else if ((shareAfter = Share(formation, moved, f, null, Mode.HeadingOnly, out after)) >= share)
                    {
                        refused = "noGain " + shareAfter.ToString("0.##", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        // ONE SetVec2MT on a fresh copy, kept until a rebuild (native caches the order position by its Vec2)
                        bool same = old == ShiftState.Shifted && _shifted.IsValid && _shifted.DistanceSquared(moved) < 0.25f;
                        if (!same)
                        {
                            WorldPosition target = planned;
                            target.SetVec2MT(moved);
                            _target = target;
                            _shifted = moved;
                        }
                        _state = ShiftState.Shifted;
                        if (!same)
                        {
                            what = "shift";
                            why = ((offset < 0f) ? "back " : "forward ") + TerrainBattlePosition.Fmt0(MathF.Abs(offset)) + "m " + Describe(friend, share)
                                + "->" + shareAfter.ToString("0.##", CultureInfo.InvariantCulture) + (cut ?? "");
                        }
                        return;
                    }
                }
                _state = ShiftState.Refused;
                if (old != ShiftState.Refused)
                {
                    what = "noShift";
                    why = Describe(friend, share) + " " + refused;
                }
            }

            /// <summary>Shortens the shift to stay within the cohesion limit; false with a note when no length fits.</summary>
            private bool CohesionCap(Formation formation, Vec2 source, Vec2 f, ref float offset, ref Vec2 moved, out string note)
            {
                note = null;
                Formation anchor;
                float gap;
                float limit;
                if (FormationCohesion.Within(formation, moved, f, _cohesionKind, _cohesionAnchor, 0f, out anchor, out gap, out limit))
                {
                    return true;
                }
                float sourceGap = FormationCohesion.Gap(formation, source, f, anchor);
                if (gap <= sourceGap)
                {
                    // no farther than the plan's own place: FormationCohesion.Pull deals with that one
                    return true;
                }
                float sign = (offset < 0f) ? -1f : 1f;
                for (float length = MathF.Abs(offset) - ShiftCohesionStep; length >= ShiftCohesionStep; length -= ShiftCohesionStep)
                {
                    Vec2 p = source + f * (sign * length);
                    float g = FormationCohesion.Gap(formation, p, f, anchor);
                    if (g <= limit || g <= sourceGap)
                    {
                        note = " cohesionCut " + TerrainBattlePosition.Fmt0(MathF.Abs(offset)) + "->" + TerrainBattlePosition.Fmt0(length) + "m";
                        offset = sign * length;
                        moved = p;
                        return true;
                    }
                }
                note = "cohesion " + FormationCohesion.Describe(anchor, gap, limit);
                return false;
            }

            private static bool FriendAnchor(Formation friend, out Vec2 at)
            {
                if (HeadingPoint(friend, out at))
                {
                    return true;
                }
                at = RBMAI.Utilities.GetFormationCenter(friend);
                return at.IsValid;
            }
        }
    }
}
