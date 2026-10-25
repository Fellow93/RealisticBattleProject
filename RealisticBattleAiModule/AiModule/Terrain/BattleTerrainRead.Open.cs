using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>Open ground from the grid's clearance map: the widest corridors between the armies, each army's exit and line clearance, and the open regions the feature map labels. Background thread.</summary>
    internal static partial class BattleTerrainRead
    {
        private const float OpenClear50 = 50f; // m, clearance of an open region's cells
        private const float BigOpenReach = 50f; // m; an army "has" an open field within this of one of its cells
        /// <summary>Open route cost (the attacker approach's "open" route): a cell with clearance below OpenRouteClear costs up to 1 + OpenRoutePenalty times its length (linear, full penalty at clearance 0).</summary>
        private const float OpenRouteClear = 25f;
        private const float OpenRoutePenalty = 4f;

        private static void ReadOpen(Frame f)
        {
            Grid grid = f.Grid;
            BattleTerrain.OpenInfo open = f.Terrain.Open;
            float[] clearance = grid.Clearance;
            int n = grid.Nx * grid.Ny;

            // 8-connected regions of clearance >= 50 m, for the feature map's dropped-near-edge count
            bool[] mask = new bool[n];
            for (int k = 0; k < n; k++)
            {
                mask[k] = grid.Cells[k] == Cell.Open && clearance[k] >= OpenClear50;
            }
            List<int> sizes = new List<int>();
            f.OpenLabels = LabelMask(grid, mask, sizes);
            f.OpenSizes = sizes;

            // widest corridor between the armies (the shortest path hugs obstacle edges, so its clearance says little about room to pass)
            Corridor(f, open);

            foreach (Side side in new[] { f.Atk, f.Def })
            {
                side.Info.ClearLine = LineClearance(f, side);
            }
        }

        /// <summary>Each army's end of the corridor: every walkable cell within this many metres of its centre, or half its frontage when more (a single centre cell let one tree next to it cap the whole corridor).</summary>
        private const float CorridorEndMin = 30f;

        /// <summary>First and last stretch of A->D: this share of the distance, min ExitStretchMin m. An army's exit is the narrowest point on its way out past its stretch; the mid-field corridor leaves both out so a tight deployment pocket does not make the field look confined.</summary>
        private const float ExitStretchShare = 0.2f;
        private const float ExitStretchMin = 60f;
        /// <summary>An army's exit line lies at least this far (m) beyond the edge of its own area.</summary>
        private const float ExitBeyondArea = 20f;

        /// <summary>Three exact maximin (widest route) bottleneck measures: the whole corridor between the armies' areas, each army's exit to its exit line, and the mid-field corridor between the stretch lines. NaN without a snap or route; the mid-field one also when the armies are too close for two stretches.</summary>
        private static void Corridor(Frame f, BattleTerrain.OpenInfo open)
        {
            Grid grid = f.Grid;
            if (f.Atk.Snap < 0 || f.Def.Snap < 0)
            {
                return;
            }
            float atkReach = Math.Max(CorridorEndMin, f.Atk.Frontage * 0.5f);
            float defReach = Math.Max(CorridorEndMin, f.Def.Frontage * 0.5f);
            float atkReach2 = atkReach * atkReach;
            float defReach2 = defReach * defReach;
            float distance = f.Distance;
            float stretch = Math.Max(ExitStretchMin, distance * ExitStretchShare);
            Vec2 a = f.Atk.C2;
            Vec2 d = f.Def.C2;
            Vec2 forward = f.Forward;
            Func<int, float> along = k => Vec2.DotProduct(grid.CellCentre(k) - a, forward);
            Func<int, bool> inAtk = k => grid.CellCentre(k).DistanceSquared(a) <= atkReach2;
            Func<int, bool> inDef = k => grid.CellCentre(k).DistanceSquared(d) <= defReach2;
            float atkExitLine = Math.Max(stretch, atkReach + ExitBeyondArea);
            float defExitLine = distance - Math.Max(stretch, defReach + ExitBeyondArea);

            int[] order = SortByClearance(grid);
            int[] parent = new int[grid.Nx * grid.Ny + 2];

            int joined = Bottleneck(grid, order, parent, inAtk, inDef);
            if (joined >= 0)
            {
                open.CorridorWidth = 2f * grid.Clearance[joined];
                open.CorridorFront = open.CorridorWidth / f.WiderFrontage;
                open.CorridorPoint = grid.CellCentre(joined);
            }

            SetExit(f, f.Atk, Bottleneck(grid, order, parent, inAtk, k => along(k) >= atkExitLine));
            SetExit(f, f.Def, Bottleneck(grid, order, parent, inDef, k => along(k) <= defExitLine));

            if (distance > 2f * stretch)
            {
                int mid = Bottleneck(grid, order, parent, k => along(k) <= stretch, k => along(k) >= distance - stretch);
                if (mid >= 0)
                {
                    open.MidCorridorWidth = 2f * grid.Clearance[mid];
                    open.MidCorridorFront = open.MidCorridorWidth / f.WiderFrontage;
                    open.MidCorridorPoint = grid.CellCentre(mid);
                }
            }
        }

        /// <summary>An army's exit: the bottleneck cell (or -1) as width, ÷ its own frontage, and the cell.</summary>
        private static void SetExit(Frame f, Side side, int cell)
        {
            if (cell < 0)
            {
                return;
            }
            BattleTerrain.SideInfo info = side.Info;
            info.ExitWidth = 2f * f.Grid.Clearance[cell];
            info.ExitFront = info.ExitWidth / Math.Max(1f, side.Frontage);
            info.ExitPoint = f.Grid.CellCentre(cell);
        }

        /// <summary>The walkable cells, most open first.</summary>
        private static int[] SortByClearance(Grid grid)
        {
            float[] clearance = grid.Clearance;
            int n = grid.Nx * grid.Ny;
            int walkable = 0;
            for (int k = 0; k < n; k++)
            {
                if (grid.Cells[k] == Cell.Open)
                {
                    walkable++;
                }
            }
            int[] order = new int[walkable];
            float[] keys = new float[walkable];
            int w = 0;
            for (int k = 0; k < n; k++)
            {
                if (grid.Cells[k] == Cell.Open)
                {
                    order[w] = k;
                    keys[w] = -clearance[k];
                    w++;
                }
            }
            Array.Sort(keys, order);
            return order;
        }

        /// <summary>The widest (maximin) route between two sets of walkable cells, exact: cells of order are switched on most open first and joined to switched-on 8-neighbours in a union-find (a diagonal only when both orthogonal cells are walkable, the A* corner rule); set cells also join that set's virtual end node. Stops when the two end nodes share a set and returns the cell that joined them (its clearance is the best smallest-clearance); -1 when they never join. parent is scratch (cells + 2).</summary>
        private static int Bottleneck(Grid grid, int[] order, int[] parent, Func<int, bool> inStart, Func<int, bool> inGoal)
        {
            int nx = grid.Nx;
            int ny = grid.Ny;
            int n = nx * ny;
            // parent: -1 = not switched on; a root points at itself; n and n + 1 are the virtual end nodes
            int start = n;
            int goal = n + 1;
            for (int k = 0; k < n; k++)
            {
                parent[k] = -1;
            }
            parent[start] = start;
            parent[goal] = goal;
            int joined = -1;
            for (int o = 0; o < order.Length && joined < 0; o++)
            {
                int k = order[o];
                parent[k] = k;
                if (inStart(k))
                {
                    int ra = Root(parent, k);
                    int rs = Root(parent, start);
                    if (ra != rs)
                    {
                        parent[rs] = ra;
                    }
                }
                if (inGoal(k))
                {
                    int ra = Root(parent, k);
                    int rg = Root(parent, goal);
                    if (ra != rg)
                    {
                        parent[rg] = ra;
                    }
                }
                int i = k % nx;
                int j = k / nx;
                for (int dj = -1; dj <= 1; dj++)
                {
                    int nj = j + dj;
                    if (nj < 0 || nj >= ny)
                    {
                        continue;
                    }
                    for (int di = -1; di <= 1; di++)
                    {
                        int ni = i + di;
                        if ((di == 0 && dj == 0) || ni < 0 || ni >= nx)
                        {
                            continue;
                        }
                        int nk = nj * nx + ni;
                        if (parent[nk] < 0)
                        {
                            continue;
                        }
                        if (di != 0 && dj != 0 && (grid.Cells[j * nx + ni] != Cell.Open || grid.Cells[nj * nx + i] != Cell.Open))
                        {
                            continue;
                        }
                        int ra = Root(parent, k);
                        int rb = Root(parent, nk);
                        if (ra != rb)
                        {
                            parent[rb] = ra;
                        }
                    }
                }
                if (Root(parent, start) == Root(parent, goal))
                {
                    joined = k;
                }
            }
            return joined;
        }

        /// <summary>Union-find root of a switched-on cell, halving the path on the way (also the passage scan's).</summary>
        private static int Root(int[] parent, int k)
        {
            while (parent[k] != k)
            {
                parent[k] = parent[parent[k]];
                k = parent[k];
            }
            return k;
        }

        /// <summary>Median clearance of the cells under the side's line (non-walkable count 0, off-grid skipped); NaN with none.</summary>
        private static float LineClearance(Frame f, Side side)
        {
            Grid grid = f.Grid;
            float half = float.IsNaN(side.Frontage) ? 0f : Math.Max(0f, side.Frontage * 0.5f);
            int samples = (int)Math.Ceiling(2f * half / grid.Step);
            HashSet<int> cells = new HashSet<int>();
            List<float> values = new List<float>();
            for (int s = 0; s <= samples; s++)
            {
                float offset = (samples > 0) ? -half + 2f * half * s / samples : 0f;
                int k = grid.IndexOf(side.C2 + f.Left * offset);
                if (k >= 0 && cells.Add(k))
                {
                    values.Add(grid.Clearance[k]);
                }
            }
            values.Sort();
            return Percentile(values, 0.5f);
        }
    }
}
