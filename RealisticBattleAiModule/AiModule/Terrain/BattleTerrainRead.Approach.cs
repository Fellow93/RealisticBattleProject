using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// Attacker approach (worker group after the battle position; pure over the grid, the snapshot and an immutable
    /// ApproachArmies). From the attacker's main infantry to a DEPLOY point, then a straight last leg onto the defender's line:
    /// straight (shortest A* to ApproachDeployGap in front of the line: the baseline, what the AI does anyway), open (A* with
    /// the open-route clearance cost plus bad ground), left / right (deploy beside the defender's OWN left / right line end,
    /// only when that end is not covered) and bypass (round a held choke the straight way crosses, to beside a line end).
    /// A* cells within ApproachDefenderClear of the line are closed (not near the deploy point). Score (seconds, lower wins) =
    /// walk + WApproachFire x seconds within reach and sight of a defender shooter x archer scale + WApproachClimb x metres
    /// climbed over the last ApproachClimbSpan + WApproachBad x seconds on bad ground + WApproachChoke for a held choke crossed
    /// + WApproachNarrow x bottleneck shortfall below the attacker frontage - WApproachFlank arriving at an open line end.
    /// A detour wins only with gain >= MinGain over straight (KeepGain for the kind already followed), length <= ApproachMaxDetour
    /// x straight's and time <= ApproachMaxTime x straight's; else straight (no change in behaviour).
    /// </summary>
    internal static partial class BattleTerrainRead
    {
        internal const float ApproachDeployGap = 150f; // m in front of the defender's line
        internal const float ApproachFlankOut = 60f; // m beyond a line end
        internal const float ApproachFlankAhead = 75f; // m in front of the line
        private const float ApproachDeploySnap = 30f;
        private const float ApproachDefenderClear = 70f;
        private const float ApproachLineDepth = 25f; // m of ranks behind the line's front
        private const float ApproachGoalFree = 40f;
        private const float ApproachEdgeMargin = 30f;
        private const float ApproachStartFree = 40f;
        private const int ApproachMaxExpanded = 60000;
        internal const float ApproachWaypointSpacing = 50f;
        private const float ApproachSampleStep = 8f;
        internal const float ApproachFireReach = 120f;
        private const float ApproachClimbSpan = 150f;
        private const float ApproachBadSlope = 15f;
        private const float ApproachNarrowSkip = 30f;
        private const float ApproachChokeMinRadius = 15f;
        private const float ApproachChokeMargin = 10f; // + half the choke's line width = "crossed"
        private const float ApproachChokeAvoid = 30f; // the bypass closes cells this much beyond that
        private const float ApproachBypassChokeGap = 60f;
        private static readonly float[] ApproachBypassOut = { 60f, 120f, 180f };
        private static readonly float[] ApproachBypassAhead = { 75f, 0f, -75f };
        private const float ApproachBypassSnap = 12f;
        internal const float ApproachChokeHeldDistance = 150f; // choke hold point within this of the scored line: held
        internal const float ApproachMinGain = 0.25f;
        internal const float ApproachKeepGain = 0.10f;
        internal const float ApproachMaxDetour = 1.5f;
        internal const float ApproachMaxTime = 1.6f;
        internal const float ApproachDefaultPace = 3f;
        private const float ApproachMinFrontage = 20f;
        private const float ApproachArcherFull = 0.2f;
        internal const float WApproachFire = 1f; // s per s under fire, x archer scale
        internal const float WApproachClimb = 2f; // s per m climbed into the line
        internal const float WApproachBad = 0.75f; // s per s on bad ground
        internal const float WApproachChoke = 150f; // s for crossing a held choke
        internal const float WApproachNarrow = 40f; // s at a zero-width bottleneck, linear up to the frontage
        internal const float WApproachFlank = 15f; // s off for arriving at an open line end

        /// <summary>What an approach is scored for. Immutable once handed to an evaluation.</summary>
        internal sealed class ApproachArmies
        {
            public Vec2 Attacker = Vec2.Invalid;
            public float AttackerFrontage = ApproachMinFrontage;
            public float Pace = ApproachDefaultPace;
            /// <summary>The defender's line: front-centre, facing (toward the attacker), along the line toward its OWN left, half width.</summary>
            public Vec2 Line = Vec2.Invalid;
            public Vec2 Facing = Vec2.Invalid;
            public Vec2 LineDirection = Vec2.Invalid;
            public float HalfWidth = 10f;
            public string LineSource = "-";
            public bool LeftCovered;
            public bool RightCovered;
            public Vec2[] Shooters = new Vec2[0];
            public float RangedShare;
            /// <summary>A choke the defender holds or can hold (Invalid = none) and its line width.</summary>
            public Vec2 Choke = Vec2.Invalid;
            public float ChokeWidth;
            public string ChokeId;
            public float MinGain = ApproachMinGain;
            /// <summary>The kind already followed (re-plan): it needs only ApproachKeepGain. Null = none.</summary>
            public BattleTerrain.ApproachKind? KeepKind;
        }

        private sealed class ApproachContext
        {
            public Grid Grid;
            public BattleTerrain Terrain;
            public ApproachArmies Armies;
            public PathSearch Search;
            public int StartCell;
            public Vec2 StartCentre;
            public float ArcherScale;
            public float ChokeRadius;
            public float[] OpenCost;
        }

        private static void ReadApproach(Frame f)
        {
            BattleTerrain.ApproachInfo info = f.Terrain.Approach;
            Side atk = f.Atk;
            Side def = f.Def;
            if (atk.Snap < 0 || def.Snap < 0)
            {
                info.Why = "noSnap";
                return;
            }
            ApproachArmies armies = new ApproachArmies
            {
                Attacker = atk.Origin,
                AttackerFrontage = (float.IsNaN(atk.Frontage) || atk.Frontage < ApproachMinFrontage) ? ApproachMinFrontage : atk.Frontage,
                Pace = ApproachPace(atk.WalkSpeed),
                RangedShare = (def.Men > 0) ? (float)def.Ranged / def.Men : 0f
            };
            List<Vec2> shooters = new List<Vec2>();
            BattleTerrain.BattlePositionResult position = f.Terrain.BattlePosition.Result;
            BattleTerrain.PositionCandidate winner = (position != null) ? position.Winner : null;
            if (winner != null && winner.Hold.IsValid && winner.Facing.IsValid && winner.LineDirection.IsValid)
            {
                armies.Line = winner.Hold;
                armies.Facing = winner.Facing;
                armies.LineDirection = winner.LineDirection;
                float width = float.IsNaN(winner.LineWidth) ? def.Frontage : winner.LineWidth;
                armies.HalfWidth = Math.Max(10f, float.IsNaN(width) ? 20f : width * 0.5f);
                armies.LeftCovered = winner.LeftCovered;
                armies.RightCovered = winner.RightCovered;
                armies.LineSource = "position:" + BattleTerrain.PositionKindName(winner.Kind);
                if (winner.Archer.IsValid)
                {
                    shooters.Add(winner.Archer);
                }
            }
            else
            {
                armies.Line = def.Origin;
                armies.Facing = -f.Forward;
                armies.LineDirection = -f.Left;
                armies.HalfWidth = Math.Max(10f, float.IsNaN(def.Frontage) ? 20f : def.Frontage * 0.5f);
                TerrainFlankPlan.SidePlan ignored;
                TerrainFlankPlan.SidePlan flanks;
                TerrainFlankPlan.Compute(f.Terrain, out ignored, out flanks);
                if (flanks != null)
                {
                    armies.LeftCovered = flanks.OwnLeftCovered;
                    armies.RightCovered = flanks.OwnRightCovered;
                }
                armies.LineSource = "army";
            }
            IReadOnlyList<BattleTerrain.ArcherSpot> spots = f.Terrain.Spots.Defender.Spots;
            for (int i = 0; i < spots.Count && i < 2; i++)
            {
                shooters.Add(spots[i].Position);
            }
            shooters.Add(armies.Line);
            armies.Shooters = shooters.ToArray();
            // held only where the defender's line is (the choke hold is the defender's option, not its place)
            BattleTerrain.ChokeHoldPlace choke = f.Terrain.ChokeHold.Defender;
            if (choke != null && choke.Choke.IsValid && choke.Hold.IsValid && choke.Hold.Distance(armies.Line) <= ApproachChokeHeldDistance)
            {
                armies.Choke = choke.Choke;
                armies.ChokeWidth = float.IsNaN(choke.LineWidth) ? 0f : choke.LineWidth;
                armies.ChokeId = choke.ChokeId;
            }
            info.Result = EvaluateApproach(f.Terrain, f.Grid, armies);
        }

        internal static float ApproachPace(float speed)
        {
            return (float.IsNaN(speed) || speed < 0.5f) ? ApproachDefaultPace : speed;
        }

        /// <summary>Scores the candidates and picks the approach. Pure math over the grid, the snapshot and armies (publish task and re-plan task).</summary>
        internal static BattleTerrain.ApproachResult EvaluateApproach(BattleTerrain terrain, Grid grid, ApproachArmies a)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            BattleTerrain.ApproachResult result = new BattleTerrain.ApproachResult
            {
                From = a.Attacker,
                Line = a.Line,
                Facing = a.Facing,
                LineDirection = a.LineDirection,
                HalfWidth = a.HalfWidth,
                LineSource = a.LineSource,
                LeftCovered = a.LeftCovered,
                RightCovered = a.RightCovered,
                HeldChoke = a.ChokeId,
                Pace = a.Pace,
                Shooters = a.Shooters.Length,
                MinGain = a.MinGain
            };
            if (!a.Attacker.IsValid || !a.Line.IsValid || !a.Facing.IsValid || !a.LineDirection.IsValid)
            {
                result.Why = "noArmies";
                result.Ms = ApproachElapsed(startTicks);
                return result;
            }
            int startCell = Snap(grid, a.Attacker, SnapRadius);
            if (startCell < 0)
            {
                result.Why = "noStartCell";
                result.Ms = ApproachElapsed(startTicks);
                return result;
            }
            ApproachContext ctx = new ApproachContext
            {
                Grid = grid,
                Terrain = terrain,
                Armies = a,
                Search = new PathSearch(grid),
                StartCell = startCell,
                StartCentre = grid.CellCentre(startCell),
                ArcherScale = 0.25f + 0.75f * Clamp01(a.RangedShare / ApproachArcherFull),
                ChokeRadius = Math.Max(ApproachChokeMinRadius, a.ChokeWidth * 0.5f + ApproachChokeMargin)
            };
            result.ArcherScale = ctx.ArcherScale;

            List<BattleTerrain.ApproachCandidate> list = new List<BattleTerrain.ApproachCandidate>();
            Vec2 front = a.Line + a.Facing * ApproachDeployGap;
            BattleTerrain.ApproachCandidate straight = ApproachRoute(ctx, BattleTerrain.ApproachKind.Straight, front, a.Line, null, false, null);
            list.Add(straight);
            result.Straight = straight;
            if (straight.Why != null)
            {
                result.Candidates = list.AsReadOnly();
                result.Why = "straight " + straight.Why;
                result.Ms = ApproachElapsed(startTicks);
                return result;
            }
            ctx.OpenCost = ApproachOpenCost(ctx);
            list.Add(ApproachRoute(ctx, BattleTerrain.ApproachKind.Open, front, a.Line, ctx.OpenCost, false, null));
            foreach (int sign in new[] { 1, -1 })
            {
                BattleTerrain.ApproachKind kind = (sign > 0) ? BattleTerrain.ApproachKind.Left : BattleTerrain.ApproachKind.Right;
                bool covered = (sign > 0) ? a.LeftCovered : a.RightCovered;
                if (covered)
                {
                    list.Add(new BattleTerrain.ApproachCandidate { Kind = kind, Why = "covered" });
                    continue;
                }
                Vec2 end = a.Line + a.LineDirection * (sign * a.HalfWidth);
                Vec2 deploy = end + a.LineDirection * (sign * ApproachFlankOut) + a.Facing * ApproachFlankAhead;
                list.Add(ApproachRoute(ctx, kind, deploy, end, null, true, null));
            }
            list.Add(ApproachBypass(ctx, straight));

            // choose: detour rules first, then the lowest total
            BattleTerrain.ApproachCandidate best = straight;
            BattleTerrain.ApproachCandidate kept = null;
            foreach (BattleTerrain.ApproachCandidate c in list)
            {
                if (c == straight || c.Why != null)
                {
                    continue;
                }
                c.Gain = (straight.Total > 0f) ? (straight.Total - c.Total) / straight.Total : float.NaN;
                float need = (a.KeepKind.HasValue && a.KeepKind.Value == c.Kind) ? ApproachKeepGain : a.MinGain;
                if (c.Length > ApproachMaxDetour * straight.Length)
                {
                    c.Why = "tooLong(x" + ApproachFmt(c.Length / straight.Length) + ">" + ApproachFmt(ApproachMaxDetour) + ")";
                }
                else if (c.Seconds > ApproachMaxTime * straight.Seconds)
                {
                    c.Why = "tooSlow(x" + ApproachFmt(c.Seconds / straight.Seconds) + ">" + ApproachFmt(ApproachMaxTime) + ")";
                }
                else if (float.IsNaN(c.Gain) || c.Gain < need)
                {
                    c.Why = "gain(" + ApproachFmt(c.Gain) + "<" + ApproachFmt(need) + ")";
                }
                else
                {
                    if (a.KeepKind.HasValue && a.KeepKind.Value == c.Kind)
                    {
                        kept = c;
                    }
                    if (best == straight || c.Total < best.Total)
                    {
                        best = c;
                    }
                }
            }
            // the kind followed now keeps its route unless another beats it by ApproachKeepGain too (no left / right flip-flop)
            if (kept != null && best != kept && best.Total > kept.Total * (1f - ApproachKeepGain))
            {
                best = kept;
            }
            foreach (BattleTerrain.ApproachCandidate c in list)
            {
                if (c != best && c.Why == null)
                {
                    c.Why = (c == straight) ? "baseline" : "beaten";
                }
            }
            result.Candidates = list.AsReadOnly();
            result.Chosen = best;
            if (best == straight)
            {
                result.Why = "noDetour";
            }
            result.Ms = ApproachElapsed(startTicks);
            return result;
        }

        /// <summary>One candidate: A* from the start to the snapped deploy point, then the straight last leg to target. Why set when it fails.</summary>
        private static BattleTerrain.ApproachCandidate ApproachRoute(ApproachContext ctx, BattleTerrain.ApproachKind kind, Vec2 deploy, Vec2 target, float[] cost,
            bool flankOpen, Func<Vec2, bool> extraBlocked)
        {
            Grid grid = ctx.Grid;
            BattleTerrain.ApproachCandidate c = new BattleTerrain.ApproachCandidate { Kind = kind, Target = target, FlankOpen = flankOpen };
            int goal = Snap(grid, deploy, ApproachDeploySnap);
            if (goal < 0)
            {
                c.Why = "noDeploy";
                return c;
            }
            if (grid.NearEdge(goal, ApproachEdgeMargin))
            {
                c.Why = "deployNearEdge";
                return c;
            }
            Vec2 deployPos = grid.CellCentre(goal);
            c.Deploy = deployPos;
            ApproachArmies a = ctx.Armies;
            Vec2 startCentre = ctx.StartCentre;
            float startFree2 = ApproachStartFree * ApproachStartFree;
            float goalFree2 = ApproachGoalFree * ApproachGoalFree;
            Func<int, bool> blocked = k =>
            {
                Vec2 p = grid.CellCentre(k);
                if (grid.NearEdge(k, ApproachEdgeMargin) && p.DistanceSquared(startCentre) > startFree2)
                {
                    return true;
                }
                if (p.DistanceSquared(deployPos) <= goalFree2)
                {
                    return false;
                }
                if (LineGap(a, p) < ApproachDefenderClear)
                {
                    return true;
                }
                return extraBlocked != null && extraBlocked(p);
            };
            List<int> cells = new List<int>();
            long before = ctx.Search.Expanded;
            float found = ctx.Search.Find(ctx.StartCell, goal, blocked, cells, cost, ApproachMaxExpanded);
            c.Expanded = ctx.Search.Expanded - before;
            if (float.IsNaN(found) || cells.Count == 0)
            {
                c.Why = ctx.Search.Capped ? "capped" : "noPath";
                return c;
            }
            ApproachScore(ctx, c, cells);
            return c;
        }

        /// <summary>Round a held choke the straight way crosses: cells near it closed, deploy beside either line end (first walkable point per side), the better side.</summary>
        private static BattleTerrain.ApproachCandidate ApproachBypass(ApproachContext ctx, BattleTerrain.ApproachCandidate straight)
        {
            ApproachArmies a = ctx.Armies;
            if (!a.Choke.IsValid)
            {
                return new BattleTerrain.ApproachCandidate { Kind = BattleTerrain.ApproachKind.Bypass, Why = "noHeldChoke" };
            }
            if (straight.Choke == null)
            {
                return new BattleTerrain.ApproachCandidate { Kind = BattleTerrain.ApproachKind.Bypass, Why = "notCrossed" };
            }
            Grid grid = ctx.Grid;
            Vec2 choke = a.Choke;
            float avoid = ctx.ChokeRadius + ApproachChokeAvoid;
            float avoid2 = avoid * avoid;
            Func<Vec2, bool> nearChoke = p => p.DistanceSquared(choke) <= avoid2;
            BattleTerrain.ApproachCandidate best = null;
            string why = null;
            foreach (int sign in new[] { 1, -1 })
            {
                Vec2 end = a.Line + a.LineDirection * (sign * a.HalfWidth);
                int deploy = -1;
                foreach (float outward in ApproachBypassOut)
                {
                    foreach (float ahead in ApproachBypassAhead)
                    {
                        int k = Snap(grid, end + a.LineDirection * (sign * outward) + a.Facing * ahead, ApproachBypassSnap);
                        if (k >= 0 && !grid.NearEdge(k, ApproachEdgeMargin) && grid.CellCentre(k).Distance(choke) >= ApproachBypassChokeGap)
                        {
                            deploy = k;
                            break;
                        }
                    }
                    if (deploy >= 0)
                    {
                        break;
                    }
                }
                if (deploy < 0)
                {
                    why = (why == null ? "" : why + "+") + ((sign > 0) ? "left" : "right") + ":noDeploy";
                    continue;
                }
                BattleTerrain.ApproachCandidate c = ApproachRoute(ctx, BattleTerrain.ApproachKind.Bypass, grid.CellCentre(deploy), end, null, true, nearChoke);
                if (c.Why == null && c.Choke != null)
                {
                    c.Why = "stillCrosses";
                }
                if (c.Why != null)
                {
                    why = (why == null ? "" : why + "+") + ((sign > 0) ? "left" : "right") + ":" + c.Why;
                    continue;
                }
                if (best == null || c.Total < best.Total)
                {
                    best = c;
                }
            }
            return best ?? new BattleTerrain.ApproachCandidate { Kind = BattleTerrain.ApproachKind.Bypass, Why = "noWayRound(" + (why ?? "-") + ")" };
        }

        /// <summary>Metres from p to the defender's line block (front at Line, ApproachLineDepth of ranks behind it).</summary>
        private static float LineGap(ApproachArmies a, Vec2 p)
        {
            Vec2 rel = p - a.Line;
            float along = Math.Abs(Vec2.DotProduct(rel, a.LineDirection)) - a.HalfWidth;
            float across = Vec2.DotProduct(rel, a.Facing);
            float dx = Math.Max(0f, along);
            float dy = (across > 0f) ? across : Math.Max(0f, -across - ApproachLineDepth);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>The open route's clearance cost plus 1 on bad ground (each >= 1, so the heuristic stays admissible).</summary>
        private static float[] ApproachOpenCost(ApproachContext ctx)
        {
            Grid grid = ctx.Grid;
            float[] clearance = grid.Clearance;
            int n = grid.Nx * grid.Ny;
            float[] cost = new float[n];
            for (int k = 0; k < n; k++)
            {
                float c = 1f + OpenRoutePenalty * Math.Max(0f, (OpenRouteClear - clearance[k]) / OpenRouteClear);
                if (grid.Cells[k] == Cell.Open && ApproachBad(ctx, grid.CellCentre(k)))
                {
                    c += 1f;
                }
                cost[k] = c;
            }
            return cost;
        }

        private static bool ApproachBad(ApproachContext ctx, Vec2 p)
        {
            Grid grid = ctx.Grid;
            BattleTerrain terrain = ctx.Terrain;
            if (grid.IsWet(p))
            {
                return true;
            }
            float slope = grid.SlopeAt(p);
            if (slope > ApproachBadSlope)
            {
                return true;
            }
            if (terrain != null && terrain.WoodsMap != null && terrain.WoodsMap.IsWoods(p))
            {
                return true;
            }
            return terrain != null && terrain.Cavalry != null && terrain.Cavalry.IsBadGround(p);
        }

        private static bool ApproachExposed(ApproachContext ctx, Vec2 p)
        {
            Vec2[] shooters = ctx.Armies.Shooters;
            float reach2 = ApproachFireReach * ApproachFireReach;
            for (int i = 0; i < shooters.Length; i++)
            {
                Vec2 s = shooters[i];
                if (s.IsValid && s.DistanceSquared(p) <= reach2 && (ctx.Terrain == null || ctx.Terrain.SightClear(s, p)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Fills a routed candidate's measures, terms and total from its cells (start first, deploy last) and the last leg.</summary>
        private static void ApproachScore(ApproachContext ctx, BattleTerrain.ApproachCandidate c, List<int> cells)
        {
            Grid grid = ctx.Grid;
            ApproachArmies a = ctx.Armies;
            List<Vec2> poly = new List<Vec2>(cells.Count + 1);
            foreach (int k in cells)
            {
                poly.Add(grid.CellCentre(k));
            }
            float route = 0f;
            for (int i = 1; i < poly.Count; i++)
            {
                route += poly[i].Distance(poly[i - 1]);
            }
            float assault = c.Deploy.Distance(c.Target);
            poly.Add(c.Target);
            c.RouteLength = route;
            c.AssaultLength = assault;
            c.Length = route + assault;
            c.Points = RouteWaypoints(grid, cells, ApproachWaypointSpacing);

            // bottleneck along the route, its first / last ApproachNarrowSkip m left out
            float bottleneck = float.NaN;
            float walked = 0f;
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                {
                    walked += grid.CellCentre(cells[i]).Distance(grid.CellCentre(cells[i - 1]));
                }
                if (walked < ApproachNarrowSkip || route - walked < ApproachNarrowSkip)
                {
                    continue;
                }
                float width = 2f * grid.Clearance[cells[i]];
                if (float.IsNaN(bottleneck) || width < bottleneck)
                {
                    bottleneck = width;
                }
            }
            c.Bottleneck = bottleneck;

            // samples along route + last leg: fire, bad ground, held choke
            float fireM = 0f;
            float badM = 0f;
            float chokeR2 = ctx.ChokeRadius * ctx.ChokeRadius;
            for (int i = 1; i < poly.Count; i++)
            {
                Vec2 p0 = poly[i - 1];
                Vec2 p1 = poly[i];
                float length = p0.Distance(p1);
                int n = Math.Max(1, (int)Math.Ceiling(length / ApproachSampleStep));
                float step = length / n;
                for (int s = 1; s <= n; s++)
                {
                    Vec2 p = p0 + (p1 - p0) * ((float)s / n);
                    if (ApproachExposed(ctx, p))
                    {
                        fireM += step;
                    }
                    if (ApproachBad(ctx, p))
                    {
                        badM += step;
                    }
                    if (c.Choke == null && a.Choke.IsValid && p.DistanceSquared(a.Choke) <= chokeR2)
                    {
                        c.Choke = a.ChokeId ?? "choke";
                    }
                }
            }

            // climb over the last ApproachClimbSpan m into the line
            float back = 0f;
            Vec2 from = poly[0];
            for (int i = poly.Count - 1; i > 0; i--)
            {
                float length = poly[i].Distance(poly[i - 1]);
                if (back + length >= ApproachClimbSpan)
                {
                    from = poly[i] + (poly[i - 1] - poly[i]) * ((ApproachClimbSpan - back) / Math.Max(0.01f, length));
                    break;
                }
                back += length;
            }
            float hEnd = grid.SampleHeight(c.Target);
            float hFrom = grid.SampleHeight(from);
            c.Climb = (IsPlausible(hEnd) && IsPlausible(hFrom)) ? hEnd - hFrom : float.NaN;

            float pace = a.Pace;
            c.FireSeconds = fireM / pace;
            c.BadSeconds = badM / pace;
            c.TermWalk = c.Length / pace;
            c.TermFire = WApproachFire * c.FireSeconds * ctx.ArcherScale;
            c.TermClimb = float.IsNaN(c.Climb) ? 0f : WApproachClimb * Math.Max(0f, c.Climb);
            c.TermBad = WApproachBad * c.BadSeconds;
            c.TermChoke = (c.Choke != null) ? WApproachChoke : 0f;
            c.TermNarrow = (!float.IsNaN(bottleneck) && bottleneck < a.AttackerFrontage) ? WApproachNarrow * (1f - bottleneck / a.AttackerFrontage) : 0f;
            c.TermFlank = c.FlankOpen ? -WApproachFlank : 0f;
            c.Seconds = c.TermWalk + c.TermBad;
            c.Total = c.TermWalk + c.TermFire + c.TermClimb + c.TermBad + c.TermChoke + c.TermNarrow + c.TermFlank;
        }

        private static float ApproachElapsed(long startTicks)
        {
            return (float)(DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
        }

        private static string ApproachFmt(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
