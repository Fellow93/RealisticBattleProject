using System;
using System.Globalization;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using MathF = TaleWorlds.Library.MathF;

namespace RBMAI
{
    [Flags]
    internal enum FootprintMode
    {
        Current = 0,
        /// <summary>Own ranged block: half width = Utilities.RangedStableRadius (Loose doubles the real width).</summary>
        StableRanged = 1,
        /// <summary>Where it is heading (cached OrderPosition, front-centre rule); fails under Charge / Retreat or without one.</summary>
        Destination = 2,
        /// <summary>A charging, mounted or tiny formation is its men's robust bounds (FormationUnitSnapshot), else a disc of its larger side.</summary>
        ScatterAware = 4
    }

    /// <summary>Oriented rectangle (Dir = facing, HalfDepth along it) or a disc (radius HalfWidth).</summary>
    internal struct Footprint
    {
        public Vec2 Centre;
        public Vec2 Dir;
        public float HalfWidth;
        public float HalfDepth;
        public bool Disc;
        public bool Valid;

        public Vec2 Side => new Vec2(-Dir.y, Dir.x);

        public static Footprint Box(Vec2 centre, Vec2 dir, float halfWidth, float halfDepth)
        {
            return new Footprint { Centre = centre, Dir = dir, HalfWidth = halfWidth, HalfDepth = halfDepth, Disc = false, Valid = centre.IsValid && dir.IsValid };
        }

        public static Footprint Circle(Vec2 centre, float radius)
        {
            return new Footprint { Centre = centre, Dir = new Vec2(0f, 1f), HalfWidth = radius, HalfDepth = radius, Disc = true, Valid = centre.IsValid };
        }

        /// <summary>0 inside.</summary>
        public float DistanceTo(Vec2 p)
        {
            float rx = p.x - Centre.x;
            float ry = p.y - Centre.y;
            if (Disc)
            {
                float d = MathF.Sqrt(rx * rx + ry * ry) - HalfWidth;
                return (d > 0f) ? d : 0f;
            }
            float along = MathF.Abs(rx * Dir.x + ry * Dir.y) - HalfDepth;
            float across = MathF.Abs(-rx * Dir.y + ry * Dir.x) - HalfWidth;
            float a = (along > 0f) ? along : 0f;
            float b = (across > 0f) ? across : 0f;
            return MathF.Sqrt(a * a + b * b);
        }

        public Vec2 ClosestPoint(Vec2 p)
        {
            float rx = p.x - Centre.x;
            float ry = p.y - Centre.y;
            if (Disc)
            {
                float d = MathF.Sqrt(rx * rx + ry * ry);
                if (d <= HalfWidth || d < 0.0001f)
                {
                    return p;
                }
                float k = HalfWidth / d;
                return new Vec2(Centre.x + rx * k, Centre.y + ry * k);
            }
            float along = MBMath.ClampFloat(rx * Dir.x + ry * Dir.y, -HalfDepth, HalfDepth);
            float across = MBMath.ClampFloat(-rx * Dir.y + ry * Dir.x, -HalfWidth, HalfWidth);
            return Centre + Dir * along + Side * across;
        }

        /// <summary>Half extent along a unit vector.</summary>
        public float ExtentAlong(Vec2 u)
        {
            if (Disc)
            {
                return HalfWidth;
            }
            float d = Dir.x * u.x + Dir.y * u.y;
            float w = -Dir.y * u.x + Dir.x * u.y;
            return MathF.Abs(d) * HalfDepth + MathF.Abs(w) * HalfWidth;
        }

        public Vec2 Corner(int i)
        {
            float sa = ((i & 1) == 0) ? HalfDepth : -HalfDepth;
            float sw = ((i & 2) == 0) ? HalfWidth : -HalfWidth;
            return Centre + Dir * sa + Side * sw;
        }
    }

    /// <summary>
    /// Edge gaps between formation footprints: 0 when they overlap, else the exact distance between the shapes.
    /// THREADING: any thread; cached formation data and unit snapshots (ScatterAware builds one when none is fresh: managed reads only), no engine query.
    /// </summary>
    internal static class FormationGeometry
    {
        private const float MinSize = 1f; // m
        private const int ScatterFewMen = 3;
        private const float ScatterSnapshotMaxAge = 0.5f; // s
        private const float DetachedMaxGap = 50f; // m beyond the attached men's bounds; detached men farther out are not part of the block
        private const float TrimSigmas = 2f;
        private const float ScatterMinCapSide = 20f; // m: a scattered box side is capped at max(2x the nominal size, this)

        internal static bool TryGet(Formation formation, FootprintMode mode, out Footprint footprint)
        {
            footprint = default(Footprint);
            if (formation == null)
            {
                return false;
            }
            if ((mode & FootprintMode.Destination) != 0)
            {
                return TryGetDestination(formation, out footprint);
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            if (!centre.IsValid)
            {
                return false;
            }
            float width = MathF.Max(formation.Width, MinSize);
            float depth = MathF.Max(formation.Depth, MinSize);
            if ((mode & FootprintMode.ScatterAware) != 0 && IsScattered(formation))
            {
                if (TrySnapshotBounds(formation, out footprint))
                {
                    return true;
                }
                footprint = Footprint.Circle(centre, MathF.Max(width, depth) * 0.5f);
                return true;
            }
            if (formation.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Circle)
            {
                footprint = Footprint.Circle(centre, MathF.Max(width, depth) * 0.5f);
                return true;
            }
            Vec2 dir = Unit(formation.Direction);
            if (!dir.IsValid)
            {
                footprint = Footprint.Circle(centre, MathF.Max(width, depth) * 0.5f);
                return true;
            }
            float halfWidth = width * 0.5f;
            float halfDepth = depth * 0.5f;
            if ((mode & FootprintMode.StableRanged) != 0)
            {
                halfWidth = MathF.Max(RBMAI.Utilities.RangedStableRadius(formation), MinSize * 0.5f);
                halfDepth = MathF.Min(halfDepth, halfWidth);
            }
            footprint = Footprint.Box(centre, dir, halfWidth, halfDepth);
            return true;
        }

        /// <summary>Its own shape placed with its FRONT-centre at front (the Move point rule), facing facing; at least FriendlyOverlap.MinWidth x MinDepth.</summary>
        internal static Footprint AtFront(Formation formation, Vec2 front, Vec2 facing)
        {
            Vec2 f = Unit(facing);
            if (formation == null || !front.IsValid || !f.IsValid)
            {
                return default(Footprint);
            }
            float width = MathF.Max(formation.Width, FriendlyOverlap.MinWidth);
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            return Footprint.Box(front - f * (depth * 0.5f), f, width * 0.5f, depth * 0.5f);
        }

        /// <summary>The footprint at its cached OrderPosition (front-centre); none under Charge / ChargeWithTarget / Retreat or its charge advance.</summary>
        internal static bool TryGetDestination(Formation formation, out Footprint footprint)
        {
            footprint = default(Footprint);
            if (formation == null || IsLeaving(formation) || !formation.OrderPositionIsValid)
            {
                return false;
            }
            Vec2 front = formation.OrderPosition;
            if (!front.IsValid)
            {
                return false;
            }
            float width = MathF.Max(formation.Width, MinSize);
            float depth = MathF.Max(formation.Depth, MinSize);
            Vec2 dir = Unit(formation.Direction);
            if (!dir.IsValid)
            {
                footprint = Footprint.Circle(front, MathF.Max(width, depth) * 0.5f);
                return true;
            }
            footprint = Footprint.Box(front - dir * (depth * 0.5f), dir, width * 0.5f, depth * 0.5f);
            return true;
        }

        /// <summary>Edge gap (m): 0 when they overlap; MaxValue when either is invalid.</summary>
        internal static float Gap(in Footprint a, in Footprint b)
        {
            if (!a.Valid || !b.Valid)
            {
                return float.MaxValue;
            }
            if (a.Disc && b.Disc)
            {
                float d = a.Centre.Distance(b.Centre) - a.HalfWidth - b.HalfWidth;
                return (d > 0f) ? d : 0f;
            }
            if (a.Disc)
            {
                return Positive(b.DistanceTo(a.Centre) - a.HalfWidth);
            }
            if (b.Disc)
            {
                return Positive(a.DistanceTo(b.Centre) - b.HalfWidth);
            }
            if (!Separated(a, b, a.Dir) && !Separated(a, b, a.Side) && !Separated(a, b, b.Dir) && !Separated(a, b, b.Side))
            {
                return 0f;
            }
            // disjoint convex shapes: the nearest pair always includes a corner of one of them
            float best = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                float d = b.DistanceTo(a.Corner(i));
                if (d < best)
                {
                    best = d;
                }
                d = a.DistanceTo(b.Corner(i));
                if (d < best)
                {
                    best = d;
                }
            }
            return best;
        }

        /// <summary>Edge gap (m) between two formations; MaxValue without geometry.</summary>
        internal static float Gap(Formation a, FootprintMode modeA, Formation b, FootprintMode modeB)
        {
            Footprint fa;
            Footprint fb;
            if (!TryGet(a, modeA, out fa) || !TryGet(b, modeB, out fb))
            {
                return float.MaxValue;
            }
            return Gap(fa, fb);
        }

        internal static float GapToPoint(in Footprint a, Vec2 p)
        {
            return (a.Valid && p.IsValid) ? a.DistanceTo(p) : float.MaxValue;
        }

        /// <summary>Distance (m) from the footprint to the segment p-q: 0 when it crosses it; MaxValue without geometry.</summary>
        internal static float GapToSegment(in Footprint a, Vec2 p, Vec2 q)
        {
            if (!a.Valid || !p.IsValid || !q.IsValid)
            {
                return float.MaxValue;
            }
            if (a.Disc)
            {
                return Positive(PointSegment(a.Centre, p, q) - a.HalfWidth);
            }
            // segment in the box's frame: crossing by slab clipping
            Vec2 side = a.Side;
            float px = Vec2.DotProduct(p - a.Centre, side);
            float py = Vec2.DotProduct(p - a.Centre, a.Dir);
            float dx = Vec2.DotProduct(q - a.Centre, side) - px;
            float dy = Vec2.DotProduct(q - a.Centre, a.Dir) - py;
            float low = 0f;
            float high = 1f;
            if (ClipSlab(px, dx, a.HalfWidth, ref low, ref high) && ClipSlab(py, dy, a.HalfDepth, ref low, ref high))
            {
                return 0f;
            }
            float best = MathF.Min(a.DistanceTo(p), a.DistanceTo(q));
            for (int i = 0; i < 4; i++)
            {
                float d = PointSegment(a.Corner(i), p, q);
                if (d < best)
                {
                    best = d;
                }
            }
            return best;
        }

        /// <summary>Liang-Barsky step: narrows [low, high] to the t where |start + t*delta| <= half.</summary>
        private static bool ClipSlab(float start, float delta, float half, ref float low, ref float high)
        {
            if (MathF.Abs(delta) < 0.0001f)
            {
                return MathF.Abs(start) <= half;
            }
            float t1 = (-half - start) / delta;
            float t2 = (half - start) / delta;
            if (t1 > t2)
            {
                float t = t1;
                t1 = t2;
                t2 = t;
            }
            if (t1 > low)
            {
                low = t1;
            }
            if (t2 < high)
            {
                high = t2;
            }
            return low <= high;
        }

        private static float PointSegment(Vec2 c, Vec2 p, Vec2 q)
        {
            Vec2 d = q - p;
            float lengthSquared = d.LengthSquared;
            float t = (lengthSquared > 0.0001f) ? MBMath.ClampFloat(Vec2.DotProduct(c - p, d) / lengthSquared, 0f, 1f) : 0f;
            return c.Distance(p + d * t);
        }

        internal static float ExtentAlong(in Footprint a, Vec2 u)
        {
            Vec2 n = Unit(u);
            return (a.Valid && n.IsValid) ? a.ExtentAlong(n) : 0f;
        }

        /// <summary>Mover's cached velocity (m/s) toward the victim footprint's nearest point (its centre when inside).</summary>
        internal static float Closing(Formation mover, in Footprint victim)
        {
            if (mover == null || !victim.Valid)
            {
                return 0f;
            }
            Vec2 from = RBMAI.Utilities.GetFormationCenter(mover);
            Vec2 velocity = mover.CachedCurrentVelocity;
            if (!from.IsValid || !velocity.IsValid)
            {
                return 0f;
            }
            Vec2 to = victim.ClosestPoint(from) - from;
            if (to.LengthSquared < 0.0001f)
            {
                to = victim.Centre - from;
            }
            float length = to.Length;
            return (length > 0.01f) ? Vec2.DotProduct(velocity, to) / length : 0f;
        }

        /// <summary>Log text of a gap: "Xm", "none" for MaxValue / NaN.</summary>
        internal static string GapText(float gap)
        {
            if (gap == float.MaxValue || float.IsNaN(gap))
            {
                return "none";
            }
            return gap.ToString("0", CultureInfo.InvariantCulture) + "m";
        }

        private static bool Separated(in Footprint a, in Footprint b, Vec2 axis)
        {
            float t = MathF.Abs((b.Centre.x - a.Centre.x) * axis.x + (b.Centre.y - a.Centre.y) * axis.y);
            return t > a.ExtentAlong(axis) + b.ExtentAlong(axis);
        }

        private static bool IsScattered(Formation formation)
        {
            if (formation.CountOfUnits <= ScatterFewMen)
            {
                return true;
            }
            // a mounted block's nominal Width/Depth lags its riders after an arrangement or width change
            FormationQuerySystem query = formation.QuerySystem;
            if (query != null && (query.IsCavalryFormationReadOnly || query.IsRangedCavalryFormationReadOnly))
            {
                return true;
            }
            OrderType order = formation.GetReadonlyMovementOrderReference().OrderType;
            return order == OrderType.Charge || order == OrderType.ChargeWithTarget;
        }

        private static bool TrySnapshotBounds(Formation formation, out Footprint footprint)
        {
            footprint = default(Footprint);
            if (Mission.Current == null)
            {
                return false;
            }
            RBMAI.Utilities.FormationUnitSnapshot snapshot = RBMAI.Utilities.GetFormationUnitSnapshot(formation, ScatterSnapshotMaxAge);
            if (snapshot == null || snapshot.RobustCount == 0)
            {
                return false;
            }
            // USER RULE 2026-10-30: each side at most 2x the formation's own Width / Depth (>= ScatterMinCapSide), on the bulk of the men
            Vec2 dir = snapshot.RobustDir;
            Vec2 side = new Vec2(-dir.y, dir.x);
            Vec2 toBulk = snapshot.RobustBulk - snapshot.RobustCentre;
            float along = CappedOffset(snapshot.RobustHalfAlong, toBulk.x * dir.x + toBulk.y * dir.y,
                MathF.Max(formation.Depth, ScatterMinCapSide * 0.5f), out float halfAlong);
            float across = CappedOffset(snapshot.RobustHalfAcross, toBulk.x * side.x + toBulk.y * side.y,
                MathF.Max(formation.Width, ScatterMinCapSide * 0.5f), out float halfAcross);
            footprint = Footprint.Box(snapshot.RobustCentre + dir * along + side * across, dir, MathF.Max(halfAcross, MinSize * 0.5f),
                MathF.Max(halfAlong, MinSize * 0.5f));
            return true;
        }

        /// <summary>
        /// The men's box in the frame (dir, its side): selectable men only, detached ones only within DetachedMaxGap of the attached men's
        /// bounds, each axis trimmed to the men within mean +- TrimSigmas sigma; bulk = the kept men's mean per axis. Pure; false without a man.
        /// </summary>
        internal static bool RobustBox(Vec2[] positions, bool[] selectable, int attachedCount, Vec2 dir,
            out Vec2 centre, out Vec2 frameDir, out float halfAlong, out float halfAcross, out int count, out Vec2 bulk)
        {
            centre = Vec2.Invalid;
            bulk = Vec2.Invalid;
            halfAlong = 0f;
            halfAcross = 0f;
            count = 0;
            Vec2 u = Unit(dir);
            if (!u.IsValid)
            {
                u = new Vec2(0f, 1f);
            }
            frameDir = u;
            Vec2 s = new Vec2(-u.y, u.x);
            int n = (positions != null && selectable != null) ? Math.Min(positions.Length, selectable.Length) : 0;
            if (attachedCount < 0 || attachedCount > n)
            {
                attachedCount = n;
            }
            // sums relative to the first man keep the squares small
            Vec2 origin = Vec2.Invalid;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            double sumA = 0.0;
            double sumB = 0.0;
            double sqA = 0.0;
            double sqB = 0.0;
            for (int i = 0; i < attachedCount; i++)
            {
                if (!selectable[i] || !positions[i].IsValid)
                {
                    continue;
                }
                Vec2 p = positions[i];
                if (!origin.IsValid)
                {
                    origin = p;
                }
                minX = Math.Min(minX, p.x);
                minY = Math.Min(minY, p.y);
                maxX = Math.Max(maxX, p.x);
                maxY = Math.Max(maxY, p.y);
                Accumulate(p - origin, u, s, ref sumA, ref sumB, ref sqA, ref sqB);
                count++;
            }
            int attached = count;
            for (int i = attachedCount; i < n; i++)
            {
                if (!selectable[i] || !DetachedIncluded(positions[i], attached, minX, minY, maxX, maxY))
                {
                    continue;
                }
                if (!origin.IsValid)
                {
                    origin = positions[i];
                }
                Accumulate(positions[i] - origin, u, s, ref sumA, ref sumB, ref sqA, ref sqB);
                count++;
            }
            if (count == 0)
            {
                return false;
            }
            double meanA = sumA / count;
            double meanB = sumB / count;
            float limitA = TrimSigmas * (float)Math.Sqrt(Math.Max(0.0, sqA / count - meanA * meanA)) + 0.01f;
            float limitB = TrimSigmas * (float)Math.Sqrt(Math.Max(0.0, sqB / count - meanB * meanB)) + 0.01f;
            float lowA = float.MaxValue;
            float highA = float.MinValue;
            float lowB = float.MaxValue;
            float highB = float.MinValue;
            double keptSumA = 0.0;
            double keptSumB = 0.0;
            int keptA = 0;
            int keptB = 0;
            for (int i = 0; i < n; i++)
            {
                if (!selectable[i] || !positions[i].IsValid || (i >= attachedCount && !DetachedIncluded(positions[i], attached, minX, minY, maxX, maxY)))
                {
                    continue;
                }
                Vec2 r = positions[i] - origin;
                float a = r.x * u.x + r.y * u.y;
                float b = r.x * s.x + r.y * s.y;
                if (Math.Abs(a - meanA) <= limitA)
                {
                    lowA = Math.Min(lowA, a);
                    highA = Math.Max(highA, a);
                    keptSumA += a;
                    keptA++;
                }
                if (Math.Abs(b - meanB) <= limitB)
                {
                    lowB = Math.Min(lowB, b);
                    highB = Math.Max(highB, b);
                    keptSumB += b;
                    keptB++;
                }
            }
            if (lowA > highA || lowB > highB)
            {
                count = 0;
                return false;
            }
            centre = origin + u * ((lowA + highA) * 0.5f) + s * ((lowB + highB) * 0.5f);
            bulk = origin + u * (float)(keptSumA / keptA) + s * (float)(keptSumB / keptB);
            halfAlong = (highA - lowA) * 0.5f;
            halfAcross = (highB - lowB) * 0.5f;
            return true;
        }

        /// <summary>
        /// One axis of the men's box capped to maxHalf: the capped half size, and the returned offset of its centre from the box centre,
        /// on the bulk of the men (bulkOffset) but inside the box. A box within the cap is unchanged (offset 0). Pure.
        /// </summary>
        internal static float CappedOffset(float half, float bulkOffset, float maxHalf, out float cappedHalf)
        {
            if (half <= maxHalf)
            {
                cappedHalf = half;
                return 0f;
            }
            cappedHalf = maxHalf;
            float room = half - maxHalf;
            return Math.Max(-room, Math.Min(room, bulkOffset));
        }

        private static void Accumulate(Vec2 r, Vec2 u, Vec2 s, ref double sumA, ref double sumB, ref double sqA, ref double sqB)
        {
            double a = r.x * u.x + r.y * u.y;
            double b = r.x * s.x + r.y * s.y;
            sumA += a;
            sumB += b;
            sqA += a * a;
            sqB += b * b;
        }

        /// <summary>A detached man counts when there is no attached man, or within DetachedMaxGap of their bounds.</summary>
        private static bool DetachedIncluded(Vec2 p, int attached, float minX, float minY, float maxX, float maxY)
        {
            if (!p.IsValid)
            {
                return false;
            }
            if (attached == 0)
            {
                return true;
            }
            float dx = (p.x < minX) ? minX - p.x : (p.x > maxX) ? p.x - maxX : 0f;
            float dy = (p.y < minY) ? minY - p.y : (p.y > maxY) ? p.y - maxY : 0f;
            return dx * dx + dy * dy <= DetachedMaxGap * DetachedMaxGap;
        }

        private static bool IsLeaving(Formation formation)
        {
            OrderType order = formation.GetReadonlyMovementOrderReference().OrderType;
            return order == OrderType.Charge || order == OrderType.ChargeWithTarget || order == OrderType.Retreat || ChargeGuard.IsAdvancingToCharge(formation);
        }

        private static float Positive(float value)
        {
            return (value > 0f) ? value : 0f;
        }

        private static Vec2 Unit(Vec2 v)
        {
            if (!v.IsValid || v.LengthSquared < 0.0001f)
            {
                return Vec2.Invalid;
            }
            return v.Normalized();
        }
    }
}
