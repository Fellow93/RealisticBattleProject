using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// Cavalry ground per side against the enemy army (BattleTerrain.Cavalry). BACKGROUND THREAD: grid, frame and feature
    /// map only, no engine call.
    ///
    /// Bad ground mask: a cell is raw bad when not walkable, wet, steeper than BadGroundSlope or cramped (clearance below
    /// BadGroundClear); the mask marks it bad when BadGroundShare of the cells in its window are raw bad (a lone rock does
    /// not), and every woods-mask cell is bad (forest trunks punch only small navmesh holes).
    ///
    /// Charge lanes: run-up walker back from LanePoints points across the enemy line, at least LaneMinLength; scored by
    /// length, downhill drop, bumps, wet and bad share; best LanesPerSide kept, aims LaneSeparation apart; run-up points
    /// ("entries") along each, walkable and not bad. (No reform points or orbit centre since 2026-10-23: the riders pick
    /// those from the live situation.)
    ///
    /// Map edge (EdgeMargin: men there are pushed back and flee): lane starts are pulled back off it.
    ///
    /// Live re-plan (ReplanCavalry): the same from the armies' centres now; the mask stays the read's; lane aims spread
    /// along each enemy line's current direction (Side.Across).
    /// </summary>
    internal static partial class BattleTerrainRead
    {
        // bad ground
        internal const float BadGroundSlope = 25f; // deg, raw bad above
        internal const float BadGroundClear = 6f; // m, raw bad below (cramped)
        private const float BadGroundWindow = 8f; // m, square window around each cell
        private const float BadGroundShare = 0.5f; // raw-bad share of the window that marks the cell bad

        // charge lanes
        internal const int LanesPerSide = 3;
        private const int LanePoints = 5; // walked across the enemy line (odd)
        private const float LaneMinLength = 60f; // m, shorter is no run-up
        private const float LaneDropWeight = 0.3f;
        private const float LaneDropCap = 10f;
        private const float LaneBumpPenalty = 0.1f;
        private const float LaneWetPenalty = 0.5f;
        private const float LaneBadPenalty = 0.5f;
        private const float LaneSeparation = 20f; // m between kept aims
        private const float LaneEntryStep = 50f; // m between run-up points from the start...
        internal const float LaneEntryMinRun = 100f; // ...down to this far from the aim

        private static void ReadCavalry(Frame f)
        {
            Grid grid = f.Grid;
            BattleTerrain.CavalryInfo info = f.Terrain.Cavalry;
            List<string> errors = new List<string>();
            bool[] mask = null;
            try
            {
                mask = BadGroundMask(grid, f.WoodsMask);
                info.MaskMinX = grid.MinX;
                info.MaskMinY = grid.MinY;
                info.MaskStep = grid.Step;
                info.MaskNx = grid.Nx;
                info.MaskNy = grid.Ny;
                int field = 0, fieldBad = 0, map = 0, mapBad = 0;
                for (int k = 0; k < mask.Length; k++)
                {
                    if (grid.Cells[k] == Cell.Out)
                    {
                        continue;
                    }
                    map++;
                    if (mask[k])
                    {
                        mapBad++;
                    }
                    if (f.InField(grid.CellCentre(k)))
                    {
                        field++;
                        if (mask[k])
                        {
                            fieldBad++;
                        }
                    }
                }
                info.BadShareField = Share(fieldBad, field);
                info.BadShareMap = Share(mapBad, map);
            }
            catch (Exception e)
            {
                errors.Add("mask:" + e.GetType().Name + ":" + Sanitize(e.Message));
                mask = null;
            }
            try
            {
                info.Attacker = FindCavalry(f, f.Atk, "A", mask);
            }
            catch (Exception e)
            {
                errors.Add("attacker:" + e.GetType().Name + ":" + Sanitize(e.Message));
            }
            try
            {
                info.Defender = FindCavalry(f, f.Def, "D", mask);
            }
            catch (Exception e)
            {
                errors.Add("defender:" + e.GetType().Name + ":" + Sanitize(e.Message));
            }
            // published only complete (readers null-check it)
            info.BadMask = mask;
            info.Error = (errors.Count > 0) ? string.Join(";", errors) : null;
        }

        /// <summary>The cavalry ground's live re-plan (ReplanArmyPlans). BACKGROUND THREAD: FindCavalry for both sides on the re-plan frame (null = the centres coincide) with the read's mask. Returns a new CavalryInfo (the published one is never touched); a side that threw is left null. No engine call.</summary>
        private static BattleTerrain.CavalryInfo ReplanCavalry(BattleTerrain terrain, Frame f)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            BattleTerrain.CavalryInfo read = terrain.Cavalry;
            BattleTerrain.CavalryInfo info = new BattleTerrain.CavalryInfo
            {
                BadShareField = read.BadShareField,
                BadShareMap = read.BadShareMap
            };
            info.BadMask = read.BadMask;
            info.MaskMinX = read.MaskMinX;
            info.MaskMinY = read.MaskMinY;
            info.MaskStep = read.MaskStep;
            info.MaskNx = read.MaskNx;
            info.MaskNy = read.MaskNy;
            if (f == null)
            {
                info.Attacker = null;
                info.Defender = null;
                info.Error = "sameCentre";
                info.Ms = (float)(DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
                return info;
            }
            List<string> errors = new List<string>();
            try
            {
                info.Attacker = FindCavalry(f, f.Atk, "A", read.BadMask);
            }
            catch (Exception e)
            {
                info.Attacker = null;
                errors.Add("attacker:" + e.GetType().Name + ":" + Sanitize(e.Message));
            }
            try
            {
                info.Defender = FindCavalry(f, f.Def, "D", read.BadMask);
            }
            catch (Exception e)
            {
                info.Defender = null;
                errors.Add("defender:" + e.GetType().Name + ":" + Sanitize(e.Message));
            }
            info.Error = (errors.Count > 0) ? string.Join(";", errors) : null;
            info.Ms = (float)(DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
            return info;
        }

        /// <summary>The bad-ground mask: raw bad cells, then a box share through a summed-area table; every woods-mask cell is bad too.</summary>
        private static bool[] BadGroundMask(Grid grid, bool[] woods)
        {
            int nx = grid.Nx;
            int ny = grid.Ny;
            int w = nx + 1;
            int[] sum = new int[w * (ny + 1)];
            for (int j = 0; j < ny; j++)
            {
                int row = 0;
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    bool bad = grid.Cells[k] != Cell.Open || grid.Wet[k] || grid.Slopes[k] > BadGroundSlope || grid.Clearance[k] < BadGroundClear;
                    row += bad ? 1 : 0;
                    sum[(j + 1) * w + i + 1] = sum[j * w + i + 1] + row;
                }
            }
            int r = Math.Max(1, (int)Math.Round(BadGroundWindow / grid.Step));
            bool[] mask = new bool[nx * ny];
            for (int j = 0; j < ny; j++)
            {
                int j0 = Math.Max(0, j - r), j1 = Math.Min(ny - 1, j + r);
                for (int i = 0; i < nx; i++)
                {
                    int i0 = Math.Max(0, i - r), i1 = Math.Min(nx - 1, i + r);
                    int cells = (j1 - j0 + 1) * (i1 - i0 + 1);
                    int bad = sum[(j1 + 1) * w + i1 + 1] - sum[j0 * w + i1 + 1] - sum[(j1 + 1) * w + i0] + sum[j0 * w + i0];
                    mask[j * nx + i] = bad >= cells * BadGroundShare || (woods != null && woods[j * nx + i]);
                }
            }
            return mask;
        }

        private static bool IsBad(bool[] mask, int k)
        {
            return k < 0 || (mask != null && mask[k]);
        }

        private static BattleTerrain.SideCavalry FindCavalry(Frame f, Side own, string prefix, bool[] mask)
        {
            BattleTerrain.SideCavalry result = new BattleTerrain.SideCavalry();
            FindLanes(f, own, prefix, mask, result);
            return result;
        }

        private static void FindLanes(Frame f, Side own, string prefix, bool[] mask, BattleTerrain.SideCavalry result)
        {
            Grid grid = f.Grid;
            Side enemy = own.Enemy;
            // enemy.Toward points from the enemy toward this side; the lane runs back along it
            Vec2 back = enemy.Toward;
            // aims spread along the enemy line: across the axis at the read, along its current line in a re-plan
            Vec2 across = enemy.Across.IsValid ? enemy.Across : f.Left;
            List<BattleTerrain.ChargeLane> lanes = new List<BattleTerrain.ChargeLane>();
            int tried = 0, tooShort = 0, edgeTrimmed = 0, edgeDropped = 0;
            int half = LanePoints / 2;
            for (int o = -half; o <= half; o++)
            {
                tried++;
                float offset = (half > 0) ? 0.5f * o / half : 0f;
                Vec2 aim = enemy.C2 + across * (enemy.Frontage * offset);
                BattleTerrain.End end;
                int bumps;
                float length = RunUpWalk(grid, aim, back, out end, out bumps);
                // pull the start off bad ground and back from the map edge (EdgeMargin)
                bool edgeHit = false;
                while (length >= LaneMinLength)
                {
                    int startCell = grid.IndexOf(aim + back * length);
                    bool nearEdge = grid.NearEdge(startCell);
                    if (!nearEdge && !IsBad(mask, startCell))
                    {
                        break;
                    }
                    edgeHit |= nearEdge;
                    length -= grid.Step;
                }
                if (!(length >= LaneMinLength))
                {
                    tooShort++;
                    if (edgeHit)
                    {
                        edgeDropped++;
                    }
                    continue;
                }
                if (edgeHit)
                {
                    edgeTrimmed++;
                }
                Vec2 start = aim + back * length;
                float wet = 0f, badMetres = 0f;
                for (float s = 0f; s <= length; s += grid.Step)
                {
                    int k = grid.IndexOf(aim + back * s);
                    if (k >= 0 && grid.Wet[k])
                    {
                        wet += grid.Step;
                    }
                    if (IsBad(mask, k))
                    {
                        badMetres += grid.Step;
                    }
                }
                float aimHeight = GroundHeight(grid, aim);
                float startHeight = GroundHeight(grid, start);
                float drop = (IsPlausible(aimHeight) && IsPlausible(startHeight)) ? startHeight - aimHeight : float.NaN;
                float dropTerm = float.IsNaN(drop) ? 0f : LaneDropWeight * MBMath.ClampFloat(drop / LaneDropCap, -1f, 1f);
                float score = Math.Min(length, RunUpCap) / RunUpCap + dropTerm - LaneBumpPenalty * bumps
                    - LaneWetPenalty * Math.Min(1f, wet / length) - LaneBadPenalty * Math.Min(1f, badMetres / length);
                // run-up points: the start, then every LaneEntryStep down to LaneEntryMinRun; a shorter lane has only its start
                List<float> distances = new List<float>();
                if (length < LaneEntryMinRun)
                {
                    distances.Add(length);
                }
                else
                {
                    for (float d = length; d >= LaneEntryMinRun; d -= LaneEntryStep)
                    {
                        distances.Add(d);
                    }
                    if (distances[distances.Count - 1] > LaneEntryMinRun + 1f)
                    {
                        distances.Add(LaneEntryMinRun);
                    }
                }
                List<float> entries = new List<float>();
                foreach (float d in distances)
                {
                    int k = grid.IndexOf(aim + back * d);
                    if (grid.IsWalkable(k) && !IsBad(mask, k) && !grid.NearEdge(k))
                    {
                        entries.Add(d);
                    }
                }
                if (entries.Count == 0)
                {
                    tooShort++;
                    continue;
                }
                lanes.Add(new BattleTerrain.ChargeLane
                {
                    Aim = aim,
                    AimHeight = aimHeight,
                    Offset = offset,
                    Start = start,
                    StartHeight = startHeight,
                    Direction = back,
                    Length = length,
                    Bumps = bumps,
                    End = end,
                    Drop = drop,
                    WetMetres = wet,
                    BadMetres = badMetres,
                    Score = score,
                    Entries = Array.AsReadOnly(entries.ToArray())
                });
            }
            lanes.Sort((x, y) => y.Score.CompareTo(x.Score));
            List<BattleTerrain.ChargeLane> kept = new List<BattleTerrain.ChargeLane>();
            foreach (BattleTerrain.ChargeLane lane in lanes)
            {
                if (kept.Count >= LanesPerSide)
                {
                    break;
                }
                bool near = false;
                foreach (BattleTerrain.ChargeLane k in kept)
                {
                    if (k.Aim.Distance(lane.Aim) < LaneSeparation)
                    {
                        near = true;
                        break;
                    }
                }
                if (near)
                {
                    continue;
                }
                lane.Id = prefix + "L" + (kept.Count + 1).ToString(CultureInfo.InvariantCulture);
                kept.Add(lane);
            }
            result.Lanes = Array.AsReadOnly(kept.ToArray());
            result.LanesTried = tried;
            result.LanesShort = tooShort;
            result.LanesEdgeTrimmed = edgeTrimmed;
            result.LanesEdgeDropped = edgeDropped;
        }

        /// <summary>Grid height under p (bilinear, else the cell's); NaN outside.</summary>
        private static float GroundHeight(Grid grid, Vec2 p)
        {
            float h = grid.SampleHeight(p);
            return IsPlausible(h) ? h : grid.HeightAt(p);
        }

    }
}
