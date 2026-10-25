using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>TERRAIN_SPACE: the shortest grid path, how wide the ground, the routes round each army, one ground or two. Background thread.</summary>
    internal static partial class BattleTerrainRead
    {
        internal const int GapLines = 21; // width-profile lines across the axis, attacker's centre (0) to defender's (1)
        private const float FlankBehind = 60f; // m behind the target's centre, along the axis, where a flank route ends
        private const float FlankLineClearance = 50f; // m kept from the target army's line
        private const float FlankSideSpan = 120f; // along the target's axis, the other side's half is closed to the route
        private const float FlankEdgeShare = 0.5f; // x EdgeMargin (30 m) kept from the map edge

        private static void ReadSpace(Frame f)
        {
            Grid grid = f.Grid;
            Side atk = f.Atk;
            Side def = f.Def;
            BattleTerrain.SpaceInfo space = f.Terrain.Space;

            bool snapped = atk.Snap >= 0 && def.Snap >= 0;
            if (snapped && atk.SnapPos.Distance(def.SnapPos) >= 1f)
            {
                f.Search.Find(atk.Snap, def.Snap, null, f.Path);
            }

            BattleTerrain.WidthLine[] profile = new BattleTerrain.WidthLine[GapLines];
            for (int s = 0; s < GapLines; s++)
            {
                float fraction = (float)s / (GapLines - 1);
                profile[s] = new BattleTerrain.WidthLine(fraction, FrontWidth(grid, f.Atk.C2 + f.Forward * (f.Distance * fraction), f.Left));
            }
            space.WidthProfile = Array.AsReadOnly(profile);

            // flank routes round each army (the attacker's left/right in both directions)
            atk.Info.FlankLeft = FlankRoute(f, atk, def, true);
            atk.Info.FlankRight = FlankRoute(f, atk, def, false);
            def.Info.FlankLeft = FlankRoute(f, def, atk, true);
            def.Info.FlankRight = FlankRoute(f, def, atk, false);

            // separate walkable grounds (a river with no crossing splits the map in two)
            List<int> sizes = new List<int>();
            int[] labels = LabelComponents(grid, sizes);
            space.SameGround = snapped ? (bool?)(labels[atk.Snap] == labels[def.Snap]) : null;
        }

        /// <summary>Shortest grid route from the mover's centre to a point FlankBehind m behind the target's centre passing it on one side: the target's line and the other side's half within FlankSideSpan are closed, as are cells near the map edge (a way round only along the boundary counts as unreachable, so does a goal that close to the edge). TerrainFlankPlan reads these routes. Null when there is nothing to measure.</summary>
        private static BattleTerrain.FlankRoute FlankRoute(Frame f, Side mover, Side target, bool leftSide)
        {
            return FlankRoute(f.Grid, f.Search, mover.Snap, mover.SnapPos, mover.Toward, mover.RideSpeed, target.C2, target.Frontage, f.Left, leftSide);
        }

        /// <summary>The flank route itself from explicit inputs (mover cell / centre / direction / pace, target centre / frontage, attacker's left). Pure grid work; also TerrainFlankPlan's live re-plan, on a background task with its own search.</summary>
        private static BattleTerrain.FlankRoute FlankRoute(Grid grid, PathSearch search, int moverSnap, Vec2 moverSnapPos, Vec2 toward, float rideSpeed,
            Vec2 targetCentre, float targetFrontage, Vec2 left, bool leftSide)
        {
            int goal = Snap(grid, targetCentre + toward * FlankBehind, SnapRadius);
            if (moverSnap < 0 || goal < 0)
            {
                // no walkable ground behind the target (or under the mover): nothing to measure
                return null;
            }
            Vec2 centre = targetCentre;
            Vec2 along = toward;
            float half = targetFrontage * 0.5f;
            float clearance2 = FlankLineClearance * FlankLineClearance;
            float sign = leftSide ? 1f : -1f;
            float edgeMargin = EdgeMargin * FlankEdgeShare;
            Func<int, bool> blocked = k =>
            {
                if (grid.NearEdge(k, edgeMargin))
                {
                    return true;
                }
                Vec2 rel = grid.CellCentre(k) - centre;
                float a = Vec2.DotProduct(rel, along);
                float c = Vec2.DotProduct(rel, left);
                float over = Math.Abs(c) - half;
                if (a * a + ((over > 0f) ? over * over : 0f) <= clearance2)
                {
                    return true;
                }
                return Math.Abs(a) <= FlankSideSpan && c * sign < 0f;
            };
            List<int> cells = new List<int>();
            float length = search.Find(moverSnap, goal, blocked, cells);
            if (float.IsNaN(length))
            {
                return new BattleTerrain.FlankRoute(false, float.NaN, float.NaN, float.NaN);
            }
            float straight = Math.Max(1f, moverSnapPos.Distance(grid.CellCentre(goal)));
            return new BattleTerrain.FlankRoute(true, length, length / straight, length / rideSpeed);
        }

        /// <summary>TerrainFlankPlan's live re-plan. BACKGROUND THREAD: pure grid work (own PathSearch, no engine call). The four flank features exactly as FindFlanks measures them, but from the armies' centres now; frontages and paces are the read's. Null when the centres coincide; a side that does not snap gets no route and its line ends are walked from its raw centre.</summary>
        internal static List<BattleTerrain.FlankFeature> ReplanFlanks(Grid grid, Vec2 attacker, Vec2 defender, float attackerFrontage, float defenderFrontage,
            float attackerRide, float defenderRide)
        {
            Vec2 forward = defender - attacker;
            if (forward.Normalize() < 1f)
            {
                return null;
            }
            Vec2 left = new Vec2(-forward.y, forward.x);
            int aSnap = Snap(grid, attacker, SnapRadius);
            int dSnap = Snap(grid, defender, SnapRadius);
            Vec2 aOrigin = (aSnap >= 0) ? grid.CellCentre(aSnap) : attacker;
            Vec2 dOrigin = (dSnap >= 0) ? grid.CellCentre(dSnap) : defender;
            PathSearch search = new PathSearch(grid);
            // routes named by the attacker's left / right for both movers, as SideInfo.FlankLeft / FlankRight
            BattleTerrain.FlankRoute atkLeft = FlankRoute(grid, search, aSnap, aOrigin, forward, attackerRide, defender, defenderFrontage, left, true);
            BattleTerrain.FlankRoute atkRight = FlankRoute(grid, search, aSnap, aOrigin, forward, attackerRide, defender, defenderFrontage, left, false);
            BattleTerrain.FlankRoute defLeft = FlankRoute(grid, search, dSnap, dOrigin, -forward, defenderRide, attacker, attackerFrontage, left, true);
            BattleTerrain.FlankRoute defRight = FlankRoute(grid, search, dSnap, dOrigin, -forward, defenderRide, attacker, attackerFrontage, left, false);
            List<BattleTerrain.FlankFeature> flanks = new List<BattleTerrain.FlankFeature>();
            flanks.Add(FlankEnd(grid, aOrigin, attackerFrontage, BattleTerrain.ArmySet.Attacker, true, left, defLeft));
            flanks.Add(FlankEnd(grid, aOrigin, attackerFrontage, BattleTerrain.ArmySet.Attacker, false, -left, defRight));
            flanks.Add(FlankEnd(grid, dOrigin, defenderFrontage, BattleTerrain.ArmySet.Defender, true, -left, atkRight));
            flanks.Add(FlankEnd(grid, dOrigin, defenderFrontage, BattleTerrain.ArmySet.Defender, false, left, atkLeft));
            return flanks;
        }

        /// <summary>One line end: the width walker from origin along direction (the read's FindFlanks and the live re-plan).</summary>
        private static BattleTerrain.FlankFeature FlankEnd(Grid grid, Vec2 origin, float frontage, BattleTerrain.ArmySet army, bool ownLeft, Vec2 direction,
            BattleTerrain.FlankRoute enemyRoute)
        {
            BattleTerrain.End end;
            float distance = HalfWidth(grid, origin, direction, out end);
            return new BattleTerrain.FlankFeature
            {
                Position = origin + direction * distance,
                Army = army,
                OwnLeft = ownLeft,
                End = end,
                EndDistance = distance,
                Spare = distance - frontage * 0.5f,
                EnemyRoute = enemyRoute
            };
        }

        /// <summary>The path's cell centres thinned to one about every <paramref name="spacing"/> m (start left out, goal last). Immutable.</summary>
        internal static IReadOnlyList<Vec2> RouteWaypoints(Grid grid, List<int> cells, float spacing)
        {
            List<Vec2> points = new List<Vec2>();
            if (cells.Count > 0)
            {
                Vec2 previous = grid.CellCentre(cells[0]);
                float run = 0f;
                for (int i = 1; i < cells.Count; i++)
                {
                    Vec2 p = grid.CellCentre(cells[i]);
                    run += p.Distance(previous);
                    previous = p;
                    if (run >= spacing && i < cells.Count - 1)
                    {
                        points.Add(p);
                        run = 0f;
                    }
                }
                points.Add(previous);
            }
            return Array.AsReadOnly(points.ToArray());
        }
    }
}
