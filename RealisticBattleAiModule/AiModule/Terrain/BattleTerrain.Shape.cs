using System;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>Shape queries for TerrainShape: pure grid reads over the snapshot; any thread (array reads only, no engine call, no allocation).</summary>
    internal sealed partial class BattleTerrain
    {
        internal const float ShapeSteepSlope = 25f; // deg, rough above
        internal const float ShapeBlockedRun = 6f; // m of bad ground that ends a passable-width walk
        internal const float ShapeMaxHalfWidth = 250f; // m

        private const int ShapeMaxSamples = 4000;

        private const int ShapePassLines = 5;

        /// <summary>Share of land cells in the rectangle that are broken ground (woods, no navmesh, steep). NaN without land or with bad input.</summary>
        public float WoodsOrRoughShare(Vec2 centre, Vec2 forward, float width, float depth)
        {
            float woods;
            return WoodsOrRoughShare(centre, forward, width, depth, out woods);
        }

        /// <summary>The same; woodsShare = the woods mask's part alone.</summary>
        public float WoodsOrRoughShare(Vec2 centre, Vec2 forward, float width, float depth, out float woodsShare)
        {
            woodsShare = float.NaN;
            BattleTerrainRead.Grid grid = _grid;
            if (grid == null || grid.Cells == null || grid.Step <= 0f || !centre.IsValid || !forward.IsValid
                || float.IsNaN(width) || float.IsNaN(depth) || width <= 0f || depth <= 0f)
            {
                return float.NaN;
            }
            Vec2 f = forward;
            if (f.Normalize() < 0.001f)
            {
                return float.NaN;
            }
            Vec2 left = new Vec2(-f.y, f.x);
            float step = grid.Step;
            int nw = Math.Max(1, (int)Math.Ceiling(width / step));
            int nd = Math.Max(1, (int)Math.Ceiling(depth / step));
            if (nw * nd > ShapeMaxSamples)
            {
                float scale = (float)Math.Sqrt((double)(nw * nd) / ShapeMaxSamples);
                nw = Math.Max(1, (int)(nw / scale));
                nd = Math.Max(1, (int)(nd / scale));
            }
            WoodsMapInfo woodsMap = WoodsMap;
            int land = 0;
            int bad = 0;
            int woods = 0;
            for (int j = 0; j < nd; j++)
            {
                float along = ((j + 0.5f) / nd - 0.5f) * depth;
                for (int i = 0; i < nw; i++)
                {
                    float across = ((i + 0.5f) / nw - 0.5f) * width;
                    Vec2 p = centre + f * along + left * across;
                    int k = grid.IndexOf(p);
                    if (k < 0)
                    {
                        continue;
                    }
                    BattleTerrainRead.Cell cell = grid.Cells[k];
                    if (cell == BattleTerrainRead.Cell.Out || cell == BattleTerrainRead.Cell.Deep)
                    {
                        continue;
                    }
                    land++;
                    bool isWoods = woodsMap != null && woodsMap.IsWoods(p);
                    if (isWoods)
                    {
                        woods++;
                    }
                    if (isWoods || cell == BattleTerrainRead.Cell.NoNav || (grid.Slopes != null && grid.Slopes[k] > ShapeSteepSlope))
                    {
                        bad++;
                    }
                }
            }
            if (land == 0)
            {
                return float.NaN;
            }
            woodsShare = (float)woods / land;
            return (float)bad / land;
        }

        /// <summary>Walkable width (m) across forward through pos, each half capped at ShapeMaxHalfWidth. A non-walkable pos starts from the nearest walkable cell within searchHalf; NaN when none.</summary>
        public float PassableWidth(Vec2 pos, Vec2 forward, float searchHalf = 0f)
        {
            BattleTerrainRead.Grid grid = _grid;
            if (grid == null || grid.Cells == null || grid.Step <= 0f || !pos.IsValid || !forward.IsValid)
            {
                return float.NaN;
            }
            Vec2 f = forward;
            if (f.Normalize() < 0.001f)
            {
                return float.NaN;
            }
            Vec2 left = new Vec2(-f.y, f.x);
            Vec2 start;
            if (!WalkableStart(grid, pos, left, Math.Max(0f, searchHalf), out start))
            {
                return float.NaN;
            }
            return ShapeWalk(grid, start, left) + ShapeWalk(grid, start, -left);
        }

        /// <summary>Narrowest PassableWidth over ShapePassLines lines up to ahead m; at = metres ahead where it was. NaN when every line was blocked.</summary>
        public float MinPassableWidthAhead(Vec2 pos, Vec2 forward, float ahead, float searchHalf, out float at)
        {
            at = float.NaN;
            if (!pos.IsValid || !forward.IsValid || float.IsNaN(ahead))
            {
                return float.NaN;
            }
            Vec2 f = forward;
            if (f.Normalize() < 0.001f)
            {
                return float.NaN;
            }
            float best = float.NaN;
            for (int n = 0; n < ShapePassLines; n++)
            {
                float d = (ShapePassLines > 1) ? ahead * n / (ShapePassLines - 1) : 0f;
                float w = PassableWidth(pos + f * d, f, searchHalf);
                if (float.IsNaN(w))
                {
                    continue;
                }
                if (float.IsNaN(best) || w < best)
                {
                    best = w;
                    at = d;
                }
            }
            return best;
        }

        /// <summary>Eye / target heights (m) above ground.</summary>
        internal const float SightEyeHeight = 1.7f;

        internal const float SightTargetHeight = 1f;

        /// <summary>BattleTerrainRead.SightLine over the snapshot (the spots' rule) from an eye SightEyeHeight above from to a target SightTargetHeight above to. True with bad input or outside the grid. Any thread: array reads only.</summary>
        public bool SightClear(Vec2 from, Vec2 to)
        {
            BattleTerrainRead.Grid grid = _grid;
            if (grid == null || grid.Cells == null || grid.Step <= 0f || !from.IsValid || !to.IsValid)
            {
                return true;
            }
            float fromHeight = grid.SampleHeight(from);
            float toHeight = grid.SampleHeight(to);
            if (!DeepWaterNavBlocker.IsPlausible(fromHeight) || !DeepWaterNavBlocker.IsPlausible(toHeight))
            {
                return true;
            }
            bool terrainClear;
            return BattleTerrainRead.SightLine(grid, WoodsMap.Mask, new Vec3(from, fromHeight + SightEyeHeight), new Vec3(to, toHeight + SightTargetHeight),
                false, out terrainClear);
        }

        /// <summary>Walkable cell nearest pos along the line (+- searchHalf); false with none.</summary>
        private static bool WalkableStart(BattleTerrainRead.Grid grid, Vec2 pos, Vec2 left, float searchHalf, out Vec2 start)
        {
            start = pos;
            if (grid.IsWalkable(grid.IndexOf(pos)))
            {
                return true;
            }
            float step = grid.Step;
            for (float s = step; s <= searchHalf; s += step)
            {
                Vec2 a = pos + left * s;
                if (grid.IsWalkable(grid.IndexOf(a)))
                {
                    start = a;
                    return true;
                }
                Vec2 b = pos - left * s;
                if (grid.IsWalkable(grid.IndexOf(b)))
                {
                    start = b;
                    return true;
                }
            }
            return false;
        }

        /// <summary>BattleTerrainRead.Walk with no slope limit: metres of good ground from start.</summary>
        private static float ShapeWalk(BattleTerrainRead.Grid grid, Vec2 start, Vec2 direction)
        {
            float step = grid.Step;
            float run = 0f;
            for (float s = 0f; s <= ShapeMaxHalfWidth; s += step)
            {
                int k = grid.IndexOf(start + direction * s);
                BattleTerrainRead.Cell cell = (k < 0) ? BattleTerrainRead.Cell.Out : grid.Cells[k];
                if (cell == BattleTerrainRead.Cell.Open)
                {
                    run = 0f;
                    continue;
                }
                run += step;
                if (run >= ShapeBlockedRun || cell == BattleTerrainRead.Cell.Out)
                {
                    return Math.Max(0f, s + step - run);
                }
            }
            return ShapeMaxHalfWidth;
        }
    }
}
