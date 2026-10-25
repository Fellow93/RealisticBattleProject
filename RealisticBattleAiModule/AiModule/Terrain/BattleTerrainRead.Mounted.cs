using System;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>TERRAIN_MOUNTED: charge run-ups onto each line.</summary>
    internal static partial class BattleTerrainRead
    {
        private const float RunUpSlope = 15f; // deg; steeper ends the run-up
        private const float RunUpCap = 250f;
        private const float RunUpBumpMax = 8f; // m; a shorter steep / blocked stretch is crossed as a bump

        private static void ReadMounted(Frame f)
        {
            f.Atk.Info.RunUpOntoEnemy = RunUp(f, f.Def);
            f.Def.Info.RunUpOntoEnemy = RunUp(f, f.Atk);
        }

        /// <summary>From the centre and +-1/3 frontage of the target's line, walking back toward the charger, the walkable stretch flatter than RunUpSlope (cap RunUpCap) crossing bumps up to RunUpBumpMax: longest, shortest, end of the shortest, and the bumps crossed by those two.</summary>
        private static BattleTerrain.RunUp RunUp(Frame f, Side target)
        {
            float max = float.NaN;
            float min = float.NaN;
            int maxBumps = 0;
            int minBumps = 0;
            BattleTerrain.End minEnd = BattleTerrain.End.None;
            for (int o = -1; o <= 1; o++)
            {
                Vec2 start = target.C2 + f.Left * (target.Frontage * o / 3f);
                BattleTerrain.End end;
                int bumps;
                float length = RunUpWalk(f.Grid, start, target.Toward, out end, out bumps);
                if (float.IsNaN(max) || length > max)
                {
                    max = length;
                    maxBumps = bumps;
                }
                if (float.IsNaN(min) || length < min)
                {
                    min = length;
                    minEnd = end;
                    minBumps = bumps;
                }
            }
            return new BattleTerrain.RunUp(max, min, minEnd, maxBumps, minBumps);
        }

        /// <summary>Like Walk for a charge lane: good ground is walkable and flatter than RunUpSlope; a steep or blocked stretch up to RunUpBumpMax m is crossed (counted in bumps) once good ground follows, a longer one ends the lane. Deep water and the map edge end it at once. end = steep | blocked | water | edge | max.</summary>
        private static float RunUpWalk(Grid grid, Vec2 start, Vec2 direction, out BattleTerrain.End end, out int bumps)
        {
            float step = grid.Step;
            float bad = 0f;
            BattleTerrain.End badEnd = BattleTerrain.End.Steep;
            bumps = 0;
            for (float s = 0f; s <= RunUpCap; s += step)
            {
                int k = grid.IndexOf(start + direction * s);
                Cell cell = (k < 0) ? Cell.Out : grid.Cells[k];
                if (cell == Cell.Out || cell == Cell.Deep)
                {
                    end = (cell == Cell.Out) ? BattleTerrain.End.Edge : BattleTerrain.End.Water;
                    return Math.Max(0f, s - bad);
                }
                if (cell == Cell.Open && !(grid.Slopes[k] >= RunUpSlope))
                {
                    if (bad > 0f)
                    {
                        bumps++;
                        bad = 0f;
                    }
                    continue;
                }
                if (bad == 0f)
                {
                    badEnd = (cell == Cell.NoNav) ? BattleTerrain.End.Blocked : BattleTerrain.End.Steep;
                }
                bad += step;
                if (bad > RunUpBumpMax)
                {
                    end = badEnd;
                    return Math.Max(0f, s + step - bad);
                }
            }
            end = BattleTerrain.End.Max;
            return RunUpCap;
        }
    }
}
