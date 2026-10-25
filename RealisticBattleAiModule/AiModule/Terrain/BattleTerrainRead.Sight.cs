using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>Fields of fire over the terrain, and the gather's own ray / tactical-region failures. Background thread.</summary>
    internal static partial class BattleTerrainRead
    {
        private const float FireRange = 200f;
        private const float EyeHeight = 1.7f;
        private const float TargetHeight = 1f;
        private const int FireTargets = 300;
        private static readonly float[] FireSources = { -0.5f, -0.25f, 0f, 0.25f, 0.5f };

        private static void ReadSight(Frame f)
        {
            BattleTerrain.SightInfo sight = f.Terrain.Sight;

            foreach (Side shooter in new[] { f.Def, f.Atk })
            {
                shooter.Info.FireAny = FieldsOfFire(f, shooter);
            }

            sight.RayFailed = f.Input.RayFailed;
            // the gather's own failures (rays, tactical lookup)
            string error = (f.Input.RayError != null) ? "rays:" + f.Input.RayError : null;
            if (f.Input.TacticalError != null)
            {
                error = ((error != null) ? error + ";" : "") + "tactical:" + f.Input.TacticalError;
            }
            sight.Error = error;
        }

        /// <summary>Share of target points (walkable strip cells within FireRange of the shooter, max FireTargets on a stride) seen from at least one of five eye points along its frontage; blocked where terrain rises above the line. NaN without an eye point or a target.</summary>
        private static float FieldsOfFire(Frame f, Side shooter)
        {
            Grid grid = f.Grid;
            List<Vec3> sources = new List<Vec3>();
            foreach (float o in FireSources)
            {
                Vec2 p = shooter.C2 + f.Left * (shooter.Frontage * o);
                float h = grid.SampleHeight(p);
                if (IsPlausible(h))
                {
                    sources.Add(new Vec3(p, h + EyeHeight));
                }
            }
            if (sources.Count == 0)
            {
                return float.NaN;
            }

            List<int> candidates = new List<int>();
            int r = (int)Math.Ceiling(FireRange / grid.Step);
            int ci = (int)Math.Floor((shooter.C2.x - grid.MinX) / grid.Step);
            int cj = (int)Math.Floor((shooter.C2.y - grid.MinY) / grid.Step);
            for (int j = Math.Max(0, cj - r); j <= Math.Min(grid.Ny - 1, cj + r); j++)
            {
                for (int i = Math.Max(0, ci - r); i <= Math.Min(grid.Nx - 1, ci + r); i++)
                {
                    int k = j * grid.Nx + i;
                    if (grid.Cells[k] != Cell.Open)
                    {
                        continue;
                    }
                    Vec2 c = grid.CellCentre(k);
                    if (c.Distance(shooter.C2) > FireRange)
                    {
                        continue;
                    }
                    Vec2 rel = c - f.Atk.C2;
                    float along = Vec2.DotProduct(rel, f.Forward);
                    if (along < 0f || along > f.Distance || Math.Abs(Vec2.DotProduct(rel, f.Left)) > f.ZoneWidth * 0.5f)
                    {
                        continue;
                    }
                    candidates.Add(k);
                }
            }
            if (candidates.Count == 0)
            {
                return float.NaN;
            }
            int stride = Math.Max(1, (candidates.Count + FireTargets - 1) / FireTargets);
            int targets = 0;
            int seenAny = 0;
            for (int n = 0; n < candidates.Count; n += stride)
            {
                int k = candidates[n];
                Vec3 target = new Vec3(grid.CellCentre(k), grid.Heights[k] + TargetHeight);
                targets++;
                foreach (Vec3 source in sources)
                {
                    if (TerrainClear(grid, source, target))
                    {
                        seenAny++;
                        break;
                    }
                }
            }
            return Share(seenAny, targets);
        }

        /// <summary>True when the terrain (grid, sampled every cell step) never rises above the line between the two points.</summary>
        private static bool TerrainClear(Grid grid, Vec3 from, Vec3 to)
        {
            float length = from.AsVec2.Distance(to.AsVec2);
            int samples = (int)(length / grid.Step);
            for (int s = 1; s < samples; s++)
            {
                float t = (float)s / samples;
                Vec2 p = new Vec2(from.x + (to.x - from.x) * t, from.y + (to.y - from.y) * t);
                float h = grid.SampleHeight(p);
                if (IsPlausible(h) && h > from.z + (to.z - from.z) * t)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
