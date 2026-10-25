using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The MAIN-THREAD part of the terrain read; every engine / game call of the read lives in this file.
    /// Build grids the whole map in AfterStart, then (pure math) slopes, summed-area tables, clearance and edge distance.
    /// Gather runs on the deployment-end tick and copies what the background part needs into an Inputs bundle (men, army
    /// axis, obstacle rays, tactical regions); Compute then reads only the grid and this bundle.
    /// </summary>
    internal static partial class BattleTerrainRead
    {
        private const int ObstacleRaysNoNav = 160; // through no-navmesh cells bordering walkable ground
        private const float ObstacleRayHeight = 1.2f;
        private const float ObstacleRayThickness = 0.01f;
        /// <summary>
        /// Static scene geometry, terrain and flora; excludes disabled, dynamic, AI-limiter, ragdoll, dropped-item,
        /// raycast-ignoring, water, agent-only (the deep-water walls), missile-only and stealth bodies.
        /// </summary>
        private const BodyFlags ObstacleRayExclude = BodyFlags.CommonCollisionExcludeFlags;
        private const float SameHitTolerance = 0.1f; // m; two casts along one ray hit the same thing within this

        /// <summary>What the background part reads besides the grid. Filled only by Gather on the main thread before the task starts, never written after; holds no engine object.</summary>
        internal sealed class Inputs
        {
            public Grid Grid;
            /// <summary>Mission time of the gather, and whether the deployment phase had ended.</summary>
            public float MissionTime;
            public bool AfterDeployment;
            public long StartTicks; // UtcNow at the gather's start; the publish measures wall time from it
            public long MainMs;
            public long RayMs;
            /// <summary>Trace the map view's outlines (developer mode only).</summary>
            public bool Outlines;

            public SideInput Atk;
            public SideInput Def;

            /// <summary>Each side's strength for the outnumbered weighting: ArmyScale's latched power and men (reinforcements included) when latched ("scale"), else the men on the field ("field"); power NaN when unread.</summary>
            public float AtkStrength = float.NaN;
            public float DefStrength = float.NaN;
            public int AtkStrengthMen;
            public int DefStrengthMen;
            public string StrengthSource = "-";

            public Vec3 AtkCentre;
            public Vec3 DefCentre;
            public float Distance;
            public Vec2 Forward;
            public Vec2 Left;
            public float AtkFrontage;
            public float DefFrontage;
            public float ZoneWidth;
            /// <summary>The walkable cells the centres snapped to (-1 = none within SnapRadius).</summary>
            public int AtkSnap = -1;
            public int DefSnap = -1;

            public RayRecord[] Rays;
            public int RayFailed;
            public string RayError;

            public TacticalInput[] Tactical;
            public string TacticalError;
        }

        /// <summary>One army's men, copied from the agents.</summary>
        internal sealed class SideInput
        {
            public Vec3[] Points;
            public int Foot;
            public int Mounted;
            public int Ranged;
            public double FootSpeedSum;
            public double MountedSpeedSum;
            /// <summary>Sum of the men's GetPower.</summary>
            public double FieldPower;
        }

        internal enum RayHit : byte
        {
            None,
            /// <summary>A scene entity.</summary>
            Entity,
            /// <summary>A body with no entity that the flora and terrain masks did not remove.</summary>
            NoEntity,
            Flora,
            Terrain
        }

        /// <summary>One obstacle ray through a no-navmesh cell bordering walkable ground.</summary>
        internal struct RayRecord
        {
            /// <summary>Grid index of the cell it was aimed through (the feature map maps rays to obstacle clusters by it).</summary>
            public int Target;
            public RayHit Hit;
            public bool Failed;
        }

        /// <summary>A scene-placed TacticalRegion: its type and where it is.</summary>
        internal struct TacticalInput
        {
            public bool Region;
            public string Type;
            public Vec2 Position;
            /// <summary>A region's radius (m, TacticalRegion.radius, as native's tactics read it); NaN for a position.</summary>
            public float Radius;
        }

        /// <summary>Grids the whole playable map. Main thread; null without a scene or bounds.</summary>
        public static Grid Build(Mission mission)
        {
            Scene scene = mission?.Scene;
            if (scene == null)
            {
                return null;
            }
            long startTicks = DateTime.UtcNow.Ticks;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            int boundaryPoints = 0;
            if (mission.Boundaries != null)
            {
                foreach (var boundary in mission.Boundaries)
                {
                    if (boundary.Value == null)
                    {
                        continue;
                    }
                    foreach (Vec2 p in boundary.Value)
                    {
                        boundaryPoints++;
                        if (p.x < minX) minX = p.x;
                        if (p.y < minY) minY = p.y;
                        if (p.x > maxX) maxX = p.x;
                        if (p.y > maxY) maxY = p.y;
                    }
                }
            }
            string bounds = "boundaries(" + boundaryPoints + " pts)";
            if (boundaryPoints == 0)
            {
                Vec3 min;
                Vec3 max;
                scene.GetBoundingBox(out min, out max);
                minX = min.x;
                minY = min.y;
                maxX = max.x;
                maxY = max.y;
                bounds = "scene";
            }
            if (!(minX < maxX) || !(minY < maxY))
            {
                return null;
            }

            float width = maxX - minX;
            float height = maxY - minY;
            float step = Math.Max(GridStep, (float)Math.Sqrt(width * height / MaxCells));
            int nx = Math.Max(1, (int)Math.Ceiling(width / step));
            int ny = Math.Max(1, (int)Math.Ceiling(height / step));

            Grid grid = new Grid
            {
                Scene = mission.SceneName,
                Bounds = bounds,
                MinX = minX,
                MinY = minY,
                Step = step,
                Nx = nx,
                Ny = ny,
                Cells = new Cell[nx * ny],
                Heights = new float[nx * ny],
                Wet = new bool[nx * ny],
                Bridge = new bool[nx * ny],
                Slopes = new float[nx * ny],
                SumHeights = new double[(nx + 1) * (ny + 1)],
                SumCounts = new int[(nx + 1) * (ny + 1)]
            };

            // deep water and bridges come from the deep-water blocker's grid when it has one: it probed in EarlyStart, before
            // switching the deep faces off (probing again here finds no bridge, the navmesh-at-the-water-line test runs after
            // that pass). Without the blocker the own probe decides.
            DeepWaterNavBlocker blocker = mission.GetMissionBehavior<DeepWaterNavBlocker>();
            DeepWaterGrid water = blocker?.Grid;
            WaterRead read = new WaterRead { Source = (water != null) ? "blocker" : "probe" };
            if (water != null)
            {
                read.BlockerStep = water.Step;
                read.BlockerDeepCells = water.DeepCount;
                read.BlockerBridgeCells = water.BridgeCount;
            }
            grid.Water = read;
            bool[] shallowDeck = new bool[nx * ny];
            List<int> deckSeeds = new List<int>();
            for (int j = 0; j < ny; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    Vec2 p = new Vec2(minX + (i + 0.5f) * step, minY + (j + 0.5f) * step);
                    float ground;
                    bool wet;
                    bool bridge;
                    bool shallow;
                    int k = j * nx + i;
                    grid.Cells[k] = Classify(mission, scene, p, step, water, blocker, read, out ground, out wet, out bridge, out shallow);
                    grid.Heights[k] = ground;
                    grid.Wet[k] = wet && grid.Cells[k] == Cell.Open;
                    grid.Bridge[k] = bridge && grid.Cells[k] == Cell.Open;
                    shallowDeck[k] = shallow && grid.Cells[k] == Cell.Open;
                    if (grid.Cells[k] == Cell.Deep)
                    {
                        read.DeepCells++;
                    }
                    if (grid.Bridge[k])
                    {
                        deckSeeds.Add(k);
                    }
                }
            }
            // a deck over shallow water is a bridge cell only when joined to a deck over deep water (a bridge's shallow ends, not a rock in a ford)
            while (deckSeeds.Count > 0)
            {
                int k = deckSeeds[deckSeeds.Count - 1];
                deckSeeds.RemoveAt(deckSeeds.Count - 1);
                read.BridgeCells++;
                int ci = k % nx;
                int cj = k / nx;
                for (int nj = Math.Max(0, cj - 1); nj <= Math.Min(ny - 1, cj + 1); nj++)
                {
                    for (int ni = Math.Max(0, ci - 1); ni <= Math.Min(nx - 1, ci + 1); ni++)
                    {
                        int nk = nj * nx + ni;
                        if (shallowDeck[nk] && !grid.Bridge[nk])
                        {
                            grid.Bridge[nk] = true;
                            read.DeckShallowBridge++;
                            deckSeeds.Add(nk);
                        }
                    }
                }
            }

            // slopes (central differences) and summed-area tables: pure grid work
            int w = nx + 1;
            for (int j = 0; j < ny; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    float slope = float.NaN;
                    bool walkable = grid.Cells[k] == Cell.Open;
                    if (walkable && i > 0 && j > 0 && i < nx - 1 && j < ny - 1)
                    {
                        float hx0 = grid.Heights[k - 1], hx1 = grid.Heights[k + 1], hy0 = grid.Heights[k - nx], hy1 = grid.Heights[k + nx];
                        if (IsPlausible(hx0) && IsPlausible(hx1) && IsPlausible(hy0) && IsPlausible(hy1))
                        {
                            float gx = (hx1 - hx0) / (2f * step);
                            float gy = (hy1 - hy0) / (2f * step);
                            slope = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180.0 / Math.PI);
                        }
                    }
                    grid.Slopes[k] = slope;

                    bool counted = walkable && IsPlausible(grid.Heights[k]);
                    int s = (j + 1) * w + i + 1;
                    grid.SumHeights[s] = (counted ? grid.Heights[k] : 0.0) + grid.SumHeights[s - 1] + grid.SumHeights[s - w] - grid.SumHeights[s - w - 1];
                    grid.SumCounts[s] = (counted ? 1 : 0) + grid.SumCounts[s - 1] + grid.SumCounts[s - w] - grid.SumCounts[s - w - 1];
                }
            }

            long clearTicks = DateTime.UtcNow.Ticks;
            ComputeClearance(grid);
            grid.ClearMs = (float)(DateTime.UtcNow.Ticks - clearTicks) / TimeSpan.TicksPerMillisecond;
            long edgeTicks = DateTime.UtcNow.Ticks;
            ComputeEdgeDistance(grid);
            grid.EdgeMs = (float)(DateTime.UtcNow.Ticks - edgeTicks) / TimeSpan.TicksPerMillisecond;
            grid.BuildMs = (DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
            return grid;
        }

        /// <summary>
        /// One cell (centre p, a square of side step): Out / Open / NoNav / Deep, its ground height, wet and bridge. With the
        /// blocker's grid: deep = its cell under p, bridge = any of its bridge cells in the square whose deck carries the
        /// navmesh (the deck is the ground); the own probe only says wet and decides alone without that grid. A wet cell with
        /// no navmesh on the bed but a navmeshed deck above (a bridge's shallow end) is walkable at the deck (shallowDeck).
        /// Outcomes are tallied in read.
        /// </summary>
        private static Cell Classify(Mission mission, Scene scene, Vec2 p, float step, DeepWaterGrid water, DeepWaterNavBlocker blocker,
            WaterRead read, out float ground, out bool wet, out bool bridge, out bool shallowDeck)
        {
            ground = float.NaN;
            wet = false;
            bridge = false;
            shallowDeck = false;
            if (!mission.IsPositionInsideBoundaries(p))
            {
                return Cell.Out;
            }
            float depth;
            int probe = DeepWaterNavBlocker.ProbeCell(scene, p, out depth);
            ground = scene.GetTerrainHeight(p, true);
            bool probeDeep = DeepWaterNavBlocker.IsDeepProbe(probe);
            if (probeDeep)
            {
                read.ProbeDeepCells++;
            }
            if (probe == DeepWaterNavBlocker.ProbeBridge)
            {
                read.ProbeBridgeCells++;
            }

            bool deep;
            Vec2 deckAt = Vec2.Invalid;
            if (water != null && water.Contains(p.x, p.y))
            {
                deep = water.IsDeepAt(p.x, p.y);
                if (deep != probeDeep)
                {
                    read.DeepDisagree++;
                }
                float half = step * 0.5f;
                Vec2 nearest;
                if (water.FindBridgeCells(p.x - half, p.y - half, p.x + half, p.y + half, p, out nearest) > 0)
                {
                    deckAt = nearest;
                }
            }
            else
            {
                deep = probeDeep;
                if (probe == DeepWaterNavBlocker.ProbeBridge)
                {
                    deckAt = p;
                }
            }

            if (deckAt.IsValid)
            {
                // a bridge deck over deep water: the terrain height is the river bed far below, where the plain navmesh query would miss the deck's face, so ask at the deck
                float deck;
                bool faceOff;
                if (DeckWalkable(scene, deckAt, blocker, out deck, out faceOff))
                {
                    if (faceOff)
                    {
                        read.DeckFacesOff++;
                    }
                    ground = deck;
                    bridge = true;
                    return Cell.Open;
                }
                read.DeckMissing++;
                if (probeDeep || probe == DeepWaterNavBlocker.ProbeBridge)
                {
                    deep = true;
                }
            }
            if (deep)
            {
                return Cell.Deep;
            }
            // not deep by the blocker although the probe says so (a pocket its clean-up dropped): wading ground
            wet = probe == DeepWaterNavBlocker.ProbeWet || probeDeep;
            if (!IsPlausible(ground))
            {
                return Cell.NoNav;
            }
            PathFaceRecord record = PathFaceRecord.NullFaceRecord;
            scene.GetNavMeshFaceIndex(ref record, new Vec3(p.x, p.y, ground + NavQueryLift, -1f), false);
            if (record.IsValid())
            {
                return Cell.Open;
            }
            if (wet)
            {
                float deck;
                bool faceOff;
                if (DeckWalkable(scene, p, blocker, out deck, out faceOff))
                {
                    read.DeckShallow++;
                    ground = deck;
                    wet = false;
                    shallowDeck = true;
                    return Cell.Open;
                }
            }
            return Cell.NoNav;
        }

        /// <summary>A deck counts only this far (m) above the terrain under it: below that the navmesh face found is the bed's.</summary>
        private const float DeckAboveBed = 0.5f;

        /// <summary>Deck query exclude mask: agent rule plus AgentOnly, so the deep-water walls are never the deck.</summary>
        private const BodyFlags DeckQueryExclude = BodyFlags.CommonCollisionExcludeFlagsForAgent | BodyFlags.AgentOnly;

        /// <summary>A walkable deck: the highest solid surface at xy carries a navmesh face (disabled faces included) at least DeckAboveBed above the terrain. faceOffByBlocker: that face is one the deep-water blocker switched off. Main thread.</summary>
        private static bool DeckWalkable(Scene scene, Vec2 xy, DeepWaterNavBlocker blocker, out float deck, out bool faceOffByBlocker)
        {
            deck = float.NaN;
            faceOffByBlocker = false;
            float surface = float.NaN;
            if (!scene.GetHeightAtPoint(xy, DeckQueryExclude, ref surface) || !IsPlausible(surface))
            {
                return false;
            }
            float bed = scene.GetTerrainHeight(xy, true);
            if (IsPlausible(bed) && surface < bed + DeckAboveBed)
            {
                return false;
            }
            PathFaceRecord record = PathFaceRecord.NullFaceRecord;
            scene.GetNavMeshFaceIndex(ref record, new Vec3(xy.x, xy.y, surface + NavQueryLift, -1f), false);
            if (!record.IsValid())
            {
                return false;
            }
            Vec3 centre = Vec3.Zero;
            scene.GetNavMeshCenterPosition(record.FaceIndex, ref centre);
            if (IsPlausible(bed) && IsPlausible(centre.z) && centre.z < bed + DeckAboveBed)
            {
                return false;
            }
            deck = surface;
            faceOffByBlocker = blocker != null && blocker.IsFaceDisabledByUs(record.FaceIndex);
            return true;
        }

        /// <summary>Main-thread gather: everything Compute needs besides the grid. Null (skipped says why) without a grid, a side with no men, or armies on one spot. The men and axis throw through; the rays and tactical regions each record their own error.</summary>
        public static Inputs Gather(Mission mission, Grid grid, out string skipped)
        {
            skipped = null;
            if (mission == null || grid == null)
            {
                skipped = "no map grid";
                return null;
            }
            long startTicks = DateTime.UtcNow.Ticks;

            List<Vec3> atkPoints = new List<Vec3>();
            List<Vec3> defPoints = new List<Vec3>();
            SideInput atk = new SideInput();
            SideInput def = new SideInput();
            foreach (Agent agent in mission.Agents)
            {
                if (agent == null || !agent.IsActive() || !agent.IsHuman || agent.Team == null)
                {
                    continue;
                }
                BattleSideEnum sideEnum = agent.Team.Side;
                SideInput side = (sideEnum == BattleSideEnum.Attacker) ? atk : (sideEnum == BattleSideEnum.Defender) ? def : null;
                if (side == null)
                {
                    continue;
                }
                ((side == atk) ? atkPoints : defPoints).Add(agent.Position);
                if (agent.HasMount)
                {
                    side.Mounted++;
                    side.MountedSpeedSum += Math.Max(agent.GetMaximumForwardUnlimitedSpeed(), agent.MountAgent.GetMaximumForwardUnlimitedSpeed());
                }
                else
                {
                    side.Foot++;
                    side.FootSpeedSum += agent.GetMaximumForwardUnlimitedSpeed();
                }
                if (agent.IsRangedCached)
                {
                    side.Ranged++;
                }
                if (agent.Character != null)
                {
                    side.FieldPower += agent.Character.GetPower();
                }
            }
            if (atkPoints.Count == 0 || defPoints.Count == 0)
            {
                skipped = "a side has no men";
                return null;
            }
            atk.Points = atkPoints.ToArray();
            def.Points = defPoints.ToArray();

            Vec3 a = Mean(atk.Points);
            Vec3 d = Mean(def.Points);
            Vec2 a2 = a.AsVec2;
            Vec2 d2 = d.AsVec2;
            float distance = a2.Distance(d2);
            if (distance < 1f)
            {
                skipped = "the armies stand on one spot";
                return null;
            }
            Vec2 forward = (d2 - a2) * (1f / distance);
            Vec2 left = new Vec2(-forward.y, forward.x);
            float attackerFrontage = Frontage(atk.Points, a2, left);
            float defenderFrontage = Frontage(def.Points, d2, left);
            Inputs input = new Inputs
            {
                Grid = grid,
                MissionTime = mission.CurrentTime,
                AfterDeployment = mission.IsDeploymentFinished,
                StartTicks = startTicks,
                Outlines = RBMConfig.RBMConfig.developerMode,
                Atk = atk,
                Def = def,
                AtkCentre = a,
                DefCentre = d,
                Distance = distance,
                Forward = forward,
                Left = left,
                AtkFrontage = attackerFrontage,
                DefFrontage = defenderFrontage,
                ZoneWidth = MBMath.ClampFloat(Math.Max(attackerFrontage, defenderFrontage) * ZoneWidthPerFrontage, ZoneMinWidth, ZoneMaxWidth),
                AtkSnap = Snap(grid, a2, SnapRadius),
                DefSnap = Snap(grid, d2, SnapRadius)
            };
            ArmyScale.SideScale atkScale = ArmyScale.For(BattleSideEnum.Attacker);
            ArmyScale.SideScale defScale = ArmyScale.For(BattleSideEnum.Defender);
            if (atkScale != null && defScale != null)
            {
                input.AtkStrength = atkScale.Power;
                input.DefStrength = defScale.Power;
                input.AtkStrengthMen = atkScale.Men;
                input.DefStrengthMen = defScale.Men;
                input.StrengthSource = "scale";
            }
            else
            {
                input.AtkStrength = (atk.FieldPower > 0.0) ? (float)atk.FieldPower : float.NaN;
                input.DefStrength = (def.FieldPower > 0.0) ? (float)def.FieldPower : float.NaN;
                input.AtkStrengthMen = atk.Points.Length;
                input.DefStrengthMen = def.Points.Length;
                input.StrengthSource = "field";
            }
            Scene scene = mission.Scene;

            long partTicks = DateTime.UtcNow.Ticks;
            try
            {
                if (scene != null)
                {
                    CastObstacleRays(input, scene);
                }
            }
            catch (Exception e)
            {
                input.RayError = e.GetType().Name + ":" + Sanitize(e.Message);
            }
            input.RayMs = (DateTime.UtcNow.Ticks - partTicks) / TimeSpan.TicksPerMillisecond;

            // tactical regions the scene's author placed (Forest / DifficultTerrain feed the woods and rough ground)
            try
            {
                List<TacticalInput> tactical = new List<TacticalInput>();
                foreach (TacticalRegion region in mission.ActiveMissionObjects.FindAllWithType<TacticalRegion>())
                {
                    tactical.Add(new TacticalInput { Region = true, Type = region.tacticalRegionType.ToString(), Position = region.Position.AsVec2, Radius = region.radius });
                }
                input.Tactical = tactical.ToArray();
            }
            catch (Exception e)
            {
                input.TacticalError = e.GetType().Name + ":" + Sanitize(e.Message);
            }

            input.MainMs = (DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
            return input;
        }

        /// <summary>Up to ObstacleRaysNoNav horizontal rays through field-strip cells with no navmesh that border walkable ground, evenly spread over the candidates.</summary>
        private static void CastObstacleRays(Inputs input, Scene scene)
        {
            Grid grid = input.Grid;
            Vec2 a2 = input.AtkCentre.AsVec2;
            List<int> noNav = new List<int>();
            for (int j = 0; j < grid.Ny; j++)
            {
                for (int i = 0; i < grid.Nx; i++)
                {
                    int k = j * grid.Nx + i;
                    if (grid.Cells[k] != Cell.NoNav
                        || !InStrip(grid.CellCentre(k), a2, input.Forward, input.Left, input.Distance, input.ZoneWidth))
                    {
                        continue;
                    }
                    if (OpenNeighbour(grid, k) >= 0)
                    {
                        noNav.Add(k);
                    }
                }
            }

            List<RayRecord> records = new List<RayRecord>();
            int count = Math.Min(ObstacleRaysNoNav, noNav.Count);
            for (int n = 0; n < count; n++)
            {
                int k = noNav[(int)((n + 0.5f) * noNav.Count / count)];
                int from = OpenNeighbour(grid, k);
                Vec2 start = grid.CellCentre(from);
                Vec2 dir = (grid.CellCentre(k) - start) * (1f / grid.Step);
                float z = grid.Heights[from] + ObstacleRayHeight;
                RayRecord record = CastObstacleRay(scene, new Vec3(start, z), new Vec3(start + dir * (2f * grid.Step), z));
                record.Target = k;
                records.Add(record);
            }
            int failed = 0;
            foreach (RayRecord record in records)
            {
                if (record.Failed)
                {
                    failed++;
                }
            }
            input.Rays = records.ToArray();
            input.RayFailed = failed;
        }

        /// <summary>One ray, classified by up to three casts: an entity hit is that entity; entity-less hits (flora and terrain bodies) are told apart by recasting with BodyOwnerFlora, then BodyOwnerTerrain added to the exclude mask (as native's OrderFlag / OrderTroopPlacer ignore trees).</summary>
        private static RayRecord CastObstacleRay(Scene scene, Vec3 from, Vec3 to)
        {
            RayRecord record = new RayRecord();
            try
            {
                float distance;
                Vec3 first;
                WeakGameEntity entity;
                if (!scene.RayCastForClosestEntityOrTerrain(from, to, out distance, out first, out entity, ObstacleRayThickness, ObstacleRayExclude))
                {
                    record.Hit = RayHit.None;
                    return record;
                }
                if (entity.IsValid)
                {
                    record.Hit = RayHit.Entity;
                    return record;
                }
                Vec3 second;
                if (!scene.RayCastForClosestEntityOrTerrain(from, to, out distance, out second, out entity, ObstacleRayThickness, ObstacleRayExclude | BodyFlags.BodyOwnerFlora)
                    || second.Distance(first) > SameHitTolerance)
                {
                    record.Hit = RayHit.Flora;
                    return record;
                }
                Vec3 third;
                if (!scene.RayCastForClosestEntityOrTerrain(from, to, out distance, out third, out entity, ObstacleRayThickness, ObstacleRayExclude | BodyFlags.BodyOwnerFlora | BodyFlags.BodyOwnerTerrain)
                    || third.Distance(second) > SameHitTolerance)
                {
                    record.Hit = RayHit.Terrain;
                    return record;
                }
                record.Hit = entity.IsValid ? RayHit.Entity : RayHit.NoEntity;
            }
            catch (Exception)
            {
                record.Failed = true;
            }
            return record;
        }

        /// <summary>First walkable 4-neighbour of cell k (-x, +x, -y, +y); -1 if none.</summary>
        private static int OpenNeighbour(Grid grid, int k)
        {
            int i = k % grid.Nx;
            int j = k / grid.Nx;
            if (i > 0 && grid.Cells[k - 1] == Cell.Open) return k - 1;
            if (i < grid.Nx - 1 && grid.Cells[k + 1] == Cell.Open) return k + 1;
            if (j > 0 && grid.Cells[k - grid.Nx] == Cell.Open) return k - grid.Nx;
            if (j < grid.Ny - 1 && grid.Cells[k + grid.Nx] == Cell.Open) return k + grid.Nx;
            return -1;
        }
    }
}
