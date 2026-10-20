using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace RBMAI
{
    /// <summary>
    /// In-mission debug view of which ground the AI can use, made to judge the deep-water blocker
    /// (<see cref="DeepWaterNavBlocker"/>). Toggled with Ctrl+Shift+W; until then it is completely inert: no layer, no
    /// view model, no engine query. Toggling it off removes the layer again. Added in RBM/SubModule.cs under
    /// rbmAiEnabled when deepWaterNavBlockEnabled or deepWaterNavLoggingEnabled is on, so it also works with the
    /// blocker off (then magenta previews what the blocker would wall off).
    ///
    /// What it draws: a world-aligned grid (<see cref="GridStep"/> m, <see cref="GridRadius"/> m radius, at most
    /// <see cref="MaxPoints"/> points, nearest first) around the ground point the camera looks at (camera ray against
    /// the scene; the camera's own x/y when the ray meets nothing), re-sampled when that point moves into another grid
    /// cell, or every <see cref="ResampleInterval"/> s, never more often than <see cref="MinResampleInterval"/> s;
    /// re-projected to the screen every frame. Classes, in priority order:
    /// - deep water, walled off (red): a cell of the blocker's live deep-water grid -- AI wall around it, no slots;
    /// - would be deep (magenta): the same rule while it is not live -- the blocker's grid after a revert, or, when the
    ///   blocker is off / inactive here, <see cref="DeepWaterNavBlocker.ProbeCell"/> run on the point (the one rule,
    ///   shared, not copied: water over terrain ≥ WallDepth, minus bridges);
    /// - face off for paths, RBM (dark red): a navmesh face the blocker switched off, at a point that is not deep;
    /// - blocked by the scene (orange): a face is there but no enabled one, and it is not ours (gates, scripted groups);
    /// - navigable, wet (cyan) / navigable (green): an enabled face, with or without water above the ground;
    /// - no navmesh: not drawn.
    /// White dots (yellow where the piece is open as a gate for a man under forced exit) run along the blocker's wall
    /// segments, both layers (every <see cref="WallDotSpacing"/> m, at the water/terrain surface, within the grid radius,
    /// at most <see cref="MaxWallDots"/>), only while the wall stands. A big blue dot marks each AI man the blocker is
    /// driving out of deep water, a small pale-blue one his exit point (live positions, re-projected every frame, at most
    /// <see cref="MaxForcedMarkers"/>; explained in the readout, no legend swatch).
    ///
    /// GetNavMeshFaceIndex's checkIfDisabled is native and undocumented. Vanilla treats an invalid record from
    /// checkIfDisabled: true as "no usable face" (SandBox MissionAgentHandler, standing points), which reads as "skip
    /// disabled faces"; the readout's "query check" line counts the points that contradict it.
    ///
    /// Rendering: screen-space Gauntlet dots placed with MBWindowManager.WorldToScreen (MBDebug.RenderDebug* is
    /// compiled out of Release), following RBMCombat's RangedAimArcView pool: built once, at the full size (grid points
    /// + wall dots), when the overlay is switched on, and every setter raises OnPropertyChangedWithValue.
    ///
    /// Cost: a re-sample is about 3-4 engine calls per grid point on navmesh, plus 2 (4 on deep points) for the
    /// ProbeCell preview when the blocker has no grid; wall dots cost none (the segments carry their height).
    /// Counted and timed in the readout. Nothing is logged.
    ///
    /// THREADING: main thread only, from OnMissionTick (after the camera update). Reads the blocker's state through its
    /// internal read-only members; never toggles a face. No statics hold an Agent; everything is cleared on toggle-off
    /// and at mission end.
    /// </summary>
    public class NavigableDebugOverlay : MissionLogic
    {
        // ---- tuning ----

        /// <summary>Grid spacing (m).</summary>
        private const float GridStep = 2f;

        /// <summary>Grid radius around the looked-at point (m).</summary>
        private const float GridRadius = 40f;

        /// <summary>Hard cap on grid points (the nearest ones are kept).</summary>
        private const int MaxPoints = 1500;

        /// <summary>Wall dots: spacing along a segment (m) and the cap.</summary>
        private const float WallDotSpacing = 1.5f;
        private const int MaxWallDots = 500;

        /// <summary>Forced-exit markers: men (and as many exit points) drawn at most.</summary>
        private const int MaxForcedMarkers = 30;
        private const float ForcedManDotSize = 12f;
        private const float ForcedExitDotSize = 6f;
        private const float ForcedManLift = 2f;

        /// <summary>Re-sample at least this often (s) even when the centre stays in its cell (catches a revert).</summary>
        private const float ResampleInterval = 0.5f;

        /// <summary>Never re-sample more often than this (s), so a fast camera pan costs at most 10 passes a second.</summary>
        private const float MinResampleInterval = 0.1f;

        /// <summary>Length of the camera ray that finds the looked-at ground point (m).</summary>
        private const float CentreRayLength = 500f;

        /// <summary>Height above the terrain the navmesh face is looked up at (m); the navmesh floats a little above it.</summary>
        private const float NavQueryLift = 0.3f;

        /// <summary>Height above ground / water surface each dot is drawn at (m); wall dots a little higher.</summary>
        private const float DotLift = 0.15f;
        private const float WallDotLift = 0.6f;

        private const float DotSize = 7f;
        private const float WallDotSize = 5f;
        private const float DotAlpha = 0.85f;

        // ---- classes ----

        private const byte ClassUnsampled = 0;
        private const byte ClassNoMesh = 1;
        private const byte ClassOpen = 2;
        private const byte ClassWet = 3;
        private const byte ClassDeepWalled = 4;
        private const byte ClassBlockedByScene = 5;
        private const byte ClassWouldBeDeep = 6;
        private const byte ClassFaceOffRbm = 7;
        private const int ClassCount = 8;

        // ARGB; the legend swatches in RBMNavigableDebug.xml use the same colours.
        private static readonly uint[] ClassColors =
        {
            0x00000000u, 0x00000000u, 0xFF30D040u, 0xFF30C8F0u, 0xFFE02828u, 0xFFF0A020u, 0xFFE040E0u, 0xFF801818u
        };

        private const uint WallColor = 0xFFFFFFFFu;
        private const uint OpenGateColor = 0xFFFFE030u;
        private const uint ForcedManColor = 0xFF2050FFu;
        private const uint ForcedExitColor = 0xFFA0C8FFu;

        private static readonly string[] ClassNames =
        {
            "not sampled", "no navmesh", "navigable", "navigable, wet", "deep water, walled off", "blocked by the scene",
            "would be deep (not live)", "face off for paths (RBM)"
        };

        // ---- grid layout, built once ----

        private readonly int _half;
        private readonly int _side;
        private readonly int _slotCount;
        private readonly int[] _offsetI;
        private readonly int[] _offsetJ;
        private readonly int[] _cellToSlot;

        // ---- per-slot sample ----

        private readonly Vec3[] _drawPos;
        private readonly byte[] _class;
        private readonly float[] _depth;
        private readonly int[] _face;

        private readonly Vec3[] _wallDots = new Vec3[MaxWallDots];
        private readonly uint[] _wallDotColors = new uint[MaxWallDots];
        private int _wallDotCount;

        private readonly int[] _counts = new int[ClassCount];

        private readonly Vec3[] _forcedMen = new Vec3[MaxForcedMarkers];
        private readonly Vec3[] _forcedExits = new Vec3[MaxForcedMarkers];
        private DeepWaterNavBlocker _blocker;

        // ---- state ----

        private bool _enabled;
        private string _failure;
        private NavigableDebugVM _dataSource;
        private GauntletLayer _gauntletLayer;
        private MissionScreen _missionScreen;

        private bool _hasSample;
        private int _sampleCi;
        private int _sampleCj;
        private float _lastResampleTime;
        private int _lastCentreSlot = -2;

        private int _lastCalls;
        private float _lastResampleMs;
        private double _totalResampleMs;
        private int _resampleCount;
        private int _oursSeenEnabled;
        private int _queryFacesDiffer;
        private string _blockerStatus = "";
        private string _deepSource = "";

        private readonly StringBuilder _readout = new StringBuilder(768);

        public NavigableDebugOverlay()
        {
            _half = (int)Math.Ceiling(GridRadius / GridStep);
            _side = 2 * _half + 1;
            float radiusCells = GridRadius / GridStep;
            float radiusCellsSq = radiusCells * radiusCells;

            int candidates = 0;
            int[] distSq = new int[_side * _side];
            int[] cells = new int[_side * _side];
            for (int di = -_half; di <= _half; di++)
            {
                for (int dj = -_half; dj <= _half; dj++)
                {
                    int d2 = di * di + dj * dj;
                    if (d2 > radiusCellsSq)
                    {
                        continue;
                    }
                    distSq[candidates] = d2;
                    cells[candidates] = (di + _half) * _side + (dj + _half);
                    candidates++;
                }
            }
            Array.Sort(distSq, cells, 0, candidates);
            _slotCount = Math.Min(candidates, MaxPoints);

            _offsetI = new int[_slotCount];
            _offsetJ = new int[_slotCount];
            _cellToSlot = new int[_side * _side];
            for (int c = 0; c < _cellToSlot.Length; c++)
            {
                _cellToSlot[c] = -1;
            }
            for (int s = 0; s < _slotCount; s++)
            {
                int cell = cells[s];
                _offsetI[s] = cell / _side - _half;
                _offsetJ[s] = cell % _side - _half;
                _cellToSlot[cell] = s;
            }

            _drawPos = new Vec3[_slotCount];
            _class = new byte[_slotCount];
            _depth = new float[_slotCount];
            _face = new int[_slotCount];
        }

        // ------------------------------------------------------------------
        // lifecycle
        // ------------------------------------------------------------------

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool shift = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);
            if (ctrl && shift && Input.IsKeyPressed(InputKey.W))
            {
                if (_enabled)
                {
                    TurnOff();
                }
                else
                {
                    TurnOn();
                }
            }
            if (!_enabled || _failure != null)
            {
                return;
            }
            try
            {
                Refresh();
            }
            catch (Exception e)
            {
                // Stop querying; the readout says why. Nothing is logged.
                _failure = e.GetType().Name + ": " + e.Message;
                if (_dataSource != null)
                {
                    _dataSource.HideAll();
                    _dataSource.Readout = "RBM navigable ground (Ctrl+Shift+W: off)\nFAILED, overlay stopped: " + _failure;
                }
            }
        }

        protected override void OnEndMission()
        {
            base.OnEndMission();
            TurnOff();
        }

        public override void OnRemoveBehavior()
        {
            TurnOff();
            base.OnRemoveBehavior();
        }

        private void TurnOn()
        {
            Mission mission = Mission;
            _missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (mission == null || mission.Scene == null || _missionScreen == null)
            {
                _missionScreen = null;
                return;
            }
            _dataSource = new NavigableDebugVM(_slotCount + MaxWallDots + 2 * MaxForcedMarkers);
            _gauntletLayer = new GauntletLayer("RBMNavigableDebugLayer", 2);
            _missionScreen.AddLayer(_gauntletLayer);
            _gauntletLayer.LoadMovie("RBMNavigableDebug", _dataSource);
            _dataSource.IsActive = true;
            _enabled = true;
            _failure = null;
            ResetSample();
        }

        private void TurnOff()
        {
            _enabled = false;
            if (_gauntletLayer != null)
            {
                _missionScreen?.RemoveLayer(_gauntletLayer);
                _gauntletLayer = null;
            }
            _dataSource?.OnFinalize();
            _dataSource = null;
            _missionScreen = null;
            _failure = null;
            ResetSample();
        }

        private void ResetSample()
        {
            _hasSample = false;
            _lastCentreSlot = -2;
            for (int s = 0; s < _slotCount; s++)
            {
                _class[s] = ClassUnsampled;
            }
            for (int c = 0; c < ClassCount; c++)
            {
                _counts[c] = 0;
            }
            _wallDotCount = 0;
            _blocker = null;
            _lastCalls = 0;
            _lastResampleMs = 0f;
            _totalResampleMs = 0.0;
            _resampleCount = 0;
            _oursSeenEnabled = 0;
            _queryFacesDiffer = 0;
            _blockerStatus = "";
            _deepSource = "";
        }

        // ------------------------------------------------------------------
        // per frame
        // ------------------------------------------------------------------

        private void Refresh()
        {
            Mission mission = Mission;
            Camera camera = _missionScreen != null ? _missionScreen.CombatCamera : null;
            if (mission == null || mission.Scene == null || camera == null || _dataSource == null || _gauntletLayer == null)
            {
                return;
            }
            Scene scene = mission.Scene;
            float now = mission.CurrentTime;

            Vec2 centre = LookedAtPoint(scene, camera);
            int ci = (int)Math.Round(centre.x / GridStep);
            int cj = (int)Math.Round(centre.y / GridStep);
            float sinceLast = now - _lastResampleTime;
            bool resampled = false;
            if (!_hasSample
                || sinceLast < 0f
                || sinceLast >= ResampleInterval
                || ((ci != _sampleCi || cj != _sampleCj) && sinceLast >= MinResampleInterval))
            {
                Resample(mission, scene, ci, cj);
                _lastResampleTime = now;
                resampled = true;
            }

            Project(camera);

            int centreSlot = SlotAt(ci, cj);
            if (resampled || centreSlot != _lastCentreSlot)
            {
                _lastCentreSlot = centreSlot;
                _dataSource.Readout = BuildReadout(mission, centreSlot);
            }
        }

        /// <summary>Where the camera ray meets the scene; the camera's own x/y if it meets nothing.</summary>
        private static Vec2 LookedAtPoint(Scene scene, Camera camera)
        {
            Vec3 from = camera.Position;
            Vec3 dir = camera.Direction;
            if (dir.Normalize() > 0.0001f)
            {
                float distance;
                Vec3 closest;
                if (scene.RayCastForClosestEntityOrTerrain(from, from + dir * CentreRayLength, out distance, out closest, 0.01f, BodyFlags.CommonFocusRayCastExcludeFlags))
                {
                    if (closest.IsValid)
                    {
                        return closest.AsVec2;
                    }
                    if (!float.IsNaN(distance))
                    {
                        return (from + dir * distance).AsVec2;
                    }
                }
            }
            return from.AsVec2;
        }

        /// <summary>The slot of the grid point at lattice cell (ci, cj) of the current sample, or -1.</summary>
        private int SlotAt(int ci, int cj)
        {
            if (!_hasSample)
            {
                return -1;
            }
            int di = ci - _sampleCi;
            int dj = cj - _sampleCj;
            if (di < -_half || di > _half || dj < -_half || dj > _half)
            {
                return -1;
            }
            return _cellToSlot[(di + _half) * _side + (dj + _half)];
        }

        private void Project(Camera camera)
        {
            float inverseScale = _gauntletLayer.UIContext.InverseScale;
            float width = TaleWorlds.Engine.Screen.RealScreenResolutionWidth;
            float height = TaleWorlds.Engine.Screen.RealScreenResolutionHeight;
            float margin = DotSize / Math.Max(inverseScale, 0.0001f);

            _dataSource.BeginFrame();
            for (int s = 0; s < _slotCount; s++)
            {
                byte c = _class[s];
                if (c < ClassOpen)
                {
                    continue;
                }
                PushIfOnScreen(camera, _drawPos[s], inverseScale, width, height, margin, DotSize, ClassColors[c]);
            }
            for (int w = 0; w < _wallDotCount; w++)
            {
                PushIfOnScreen(camera, _wallDots[w], inverseScale, width, height, margin, WallDotSize, _wallDotColors[w]);
            }
            // Forced-exit men and their exit points: live positions, every frame (no engine query beyond Agent.Position).
            int forced = _blocker != null ? _blocker.CopyForcedExitMarkers(_forcedMen, _forcedExits) : 0;
            for (int m = 0; m < forced; m++)
            {
                Vec3 man = _forcedMen[m];
                man.z += ForcedManLift;
                PushIfOnScreen(camera, man, inverseScale, width, height, margin, ForcedManDotSize, ForcedManColor);
                Vec3 exit = _forcedExits[m];
                exit.z += WallDotLift;
                PushIfOnScreen(camera, exit, inverseScale, width, height, margin, ForcedExitDotSize, ForcedExitColor);
            }
            _dataSource.EndFrame();
        }

        private void PushIfOnScreen(Camera camera, Vec3 world, float inverseScale, float width, float height, float margin, float size, uint color)
        {
            float sx = 0f;
            float sy = 0f;
            float sw = 0f;
            MBWindowManager.WorldToScreen(camera, world, ref sx, ref sy, ref sw);
            if (!(sw >= 0f) || float.IsNaN(sx) || float.IsNaN(sy)
                || sx < -margin || sy < -margin || sx > width + margin || sy > height + margin)
            {
                return;
            }
            _dataSource.PushDot(sx * inverseScale, sy * inverseScale, size, DotAlpha, color);
        }

        // ------------------------------------------------------------------
        // re-sample
        // ------------------------------------------------------------------

        private void Resample(Mission mission, Scene scene, int ci, int cj)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            DeepWaterNavBlocker blocker = mission.GetMissionBehavior<DeepWaterNavBlocker>();
            _blocker = blocker;
            bool blockerHasFaces = blocker != null && blocker.DisabledFaceCount > 0;
            DeepWaterGrid grid = blocker != null ? blocker.Grid : null;
            bool gridLive = grid != null && blocker.IsWallLive && !blocker.IsReverted;
            _deepSource = gridLive ? "blocker's grid (live)" : grid != null ? "blocker's grid (not live)" : "ProbeCell preview";
            int calls = 0;

            for (int c = 0; c < ClassCount; c++)
            {
                _counts[c] = 0;
            }
            _oursSeenEnabled = 0;
            _queryFacesDiffer = 0;

            for (int s = 0; s < _slotCount; s++)
            {
                float x = (ci + _offsetI[s]) * GridStep;
                float y = (cj + _offsetJ[s]) * GridStep;
                Vec2 xy = new Vec2(x, y);
                _depth[s] = float.NaN;
                _face[s] = -1;

                float ground = scene.GetTerrainHeight(xy, true);
                calls++;
                if (!DeepWaterNavBlocker.IsPlausible(ground))
                {
                    SetClass(s, ClassNoMesh);
                    continue;
                }
                _drawPos[s] = new Vec3(x, y, ground + DotLift, -1f);

                float level = scene.GetWaterLevelAtPosition(xy, true, true);
                calls++;
                if (DeepWaterNavBlocker.IsPlausible(level))
                {
                    _depth[s] = level - ground;
                    if (level > ground)
                    {
                        _drawPos[s] = new Vec3(x, y, level + DotLift, -1f);
                    }
                }

                // The deep-water rule first: it applies with or without navmesh under the point.
                bool deep;
                if (grid != null)
                {
                    deep = grid.IsDeepAt(x, y);
                }
                else
                {
                    float probeDepth;
                    deep = DeepWaterNavBlocker.IsDeepProbe(DeepWaterNavBlocker.ProbeCell(scene, xy, out probeDepth));
                    calls += 2;
                }
                if (deep)
                {
                    SetClass(s, gridLive ? ClassDeepWalled : ClassWouldBeDeep);
                    continue;
                }

                Vec3 query = new Vec3(x, y, ground + NavQueryLift, -1f);
                PathFaceRecord any = PathFaceRecord.NullFaceRecord;
                scene.GetNavMeshFaceIndex(ref any, query, false);
                calls++;
                if (!any.IsValid())
                {
                    SetClass(s, ClassNoMesh);
                    continue;
                }
                PathFaceRecord enabled = PathFaceRecord.NullFaceRecord;
                scene.GetNavMeshFaceIndex(ref enabled, query, true);
                calls++;

                bool enabledValid = enabled.IsValid();
                if (enabledValid && enabled.FaceIndex != any.FaceIndex)
                {
                    _queryFacesDiffer++;
                }
                bool anyOurs = blockerHasFaces && blocker.IsFaceDisabledByUs(any.FaceIndex);
                bool enabledOurs = blockerHasFaces && enabledValid && blocker.IsFaceDisabledByUs(enabled.FaceIndex);
                if (enabledOurs)
                {
                    // checkIfDisabled: true returned a face the blocker switched off: the flag does not mean what we read.
                    _oursSeenEnabled++;
                }

                if (anyOurs || enabledOurs)
                {
                    _face[s] = anyOurs ? any.FaceIndex : enabled.FaceIndex;
                    SetClass(s, ClassFaceOffRbm);
                }
                else if (!enabledValid)
                {
                    _face[s] = any.FaceIndex;
                    SetClass(s, ClassBlockedByScene);
                }
                else
                {
                    _face[s] = enabled.FaceIndex;
                    SetClass(s, _depth[s] > 0f ? ClassWet : ClassOpen);
                }
            }

            SampleWall(blocker, new Vec2(ci * GridStep, cj * GridStep));

            _hasSample = true;
            _sampleCi = ci;
            _sampleCj = cj;
            _lastCalls = calls;
            _lastResampleMs = (float)((DateTime.UtcNow.Ticks - startTicks) / (double)TimeSpan.TicksPerMillisecond);
            _totalResampleMs += _lastResampleMs;
            _resampleCount++;
            _blockerStatus = DescribeBlocker(blocker);
        }

        /// <summary>White dots along the live wall segments near the centre; no engine calls.</summary>
        private void SampleWall(DeepWaterNavBlocker blocker, Vec2 centre)
        {
            _wallDotCount = 0;
            if (blocker == null || blocker.WallEntityCount == 0)
            {
                return;
            }
            IList<WallSegment> segments = blocker.WallSegments;
            float reachSq = (GridRadius + 10f) * (GridRadius + 10f);
            for (int k = 0; k < segments.Count && _wallDotCount < MaxWallDots; k++)
            {
                WallSegment seg = segments[k];
                uint color = blocker.IsPieceOpen(seg.PieceId) ? OpenGateColor : WallColor;
                Vec2 mid = (seg.A + seg.B) * 0.5f;
                if (mid.DistanceSquared(centre) > reachSq)
                {
                    continue;
                }
                float length = seg.A.Distance(seg.B);
                int dots = Math.Max(1, (int)Math.Ceiling(length / WallDotSpacing));
                for (int d = 0; d <= dots && _wallDotCount < MaxWallDots; d++)
                {
                    float t = (float)d / dots;
                    Vec2 p = seg.A + (seg.B - seg.A) * t;
                    if (p.DistanceSquared(centre) > GridRadius * GridRadius)
                    {
                        continue;
                    }
                    _wallDotColors[_wallDotCount] = color;
                    _wallDots[_wallDotCount++] = new Vec3(p.x, p.y, seg.SurfaceZ + WallDotLift, -1f);
                }
            }
        }

        private void SetClass(int slot, byte c)
        {
            _class[slot] = c;
            _counts[c]++;
        }

        // ------------------------------------------------------------------
        // readout
        // ------------------------------------------------------------------

        private static string DescribeBlocker(DeepWaterNavBlocker blocker)
        {
            if (blocker == null)
            {
                return "not in this mission (\"Deep water blocks AI paths\" is off) -- magenta previews it";
            }
            if (!blocker.IsActive)
            {
                return "inactive (not a land field battle) -- magenta previews it";
            }
            return blocker.BlockResult + (blocker.IsReverted ? " (REVERTED: faces on, walls and slot rule gone)" : "")
                + ", " + blocker.DisabledFaceCount + " faces off now";
        }

        private string BuildReadout(Mission mission, int centreSlot)
        {
            DeepWaterNavBlocker blocker = mission.GetMissionBehavior<DeepWaterNavBlocker>();
            DeepWaterGrid grid = blocker != null ? blocker.Grid : null;

            _readout.Clear();
            _readout.Append("RBM navigable ground (Ctrl+Shift+W: off)\n");
            _readout.Append("Blocker: ").Append(_blockerStatus).Append("\n");
            _readout.Append("Rule: water over terrain >= ").Append(Fmt(DeepWaterNavBlocker.WallDepth))
                .Append(" m (walls, slots); face centre >= ").Append(Fmt(DeepWaterNavBlocker.BlockDepth))
                .Append(" m (paths). Deep from: ").Append(_deepSource).Append('\n');
            if (grid != null)
            {
                _readout.Append("Grid: step ").Append(Fmt(grid.Step)).Append(" m, ").Append(grid.CellCount).Append(" cells, ")
                    .Append(grid.DeepCount).Append(" deep, ").Append(grid.BlockedCount).Append(" no-slot\n");
            }
            if (blocker != null)
            {
                _readout.Append("Wall: ").Append(blocker.WallQuadCount).Append(" quads (2 layers) in ").Append(blocker.WallEntityCount)
                    .Append(" pieces, ").Append(blocker.OpenGateCount).Append(" open as gates (yellow); slot rejections ")
                    .Append(blocker.SlotRejections).Append(", batch slots patched ")
                    .Append(System.Threading.Interlocked.Read(ref DeepWaterSlots.PatchedUnavailable)).Append('\n');
                _readout.Append("Forced exit: ").Append(blocker.ForcedExitCount)
                    .Append(" AI men driven out of deep water now (big blue dot = man, small pale blue = his exit point)\n");
            }
            _readout.Append("Sample: ").Append(_slotCount).Append(" points, ").Append(Fmt(GridStep)).Append(" m grid, ")
                .Append(Fmt(GridRadius)).Append(" m radius; ").Append(_lastCalls).Append(" engine calls, ")
                .Append(Fmt(_lastResampleMs)).Append(" ms (avg ")
                .Append(Fmt(_resampleCount > 0 ? (float)(_totalResampleMs / _resampleCount) : 0f)).Append(" ms over ")
                .Append(_resampleCount).Append("); wall dots ").Append(_wallDotCount).Append('\n');
            _readout.Append("navigable ").Append(_counts[ClassOpen])
                .Append("  wet ").Append(_counts[ClassWet])
                .Append("  deep ").Append(_counts[ClassDeepWalled])
                .Append("  would be deep ").Append(_counts[ClassWouldBeDeep])
                .Append("  face off ").Append(_counts[ClassFaceOffRbm])
                .Append("  scene ").Append(_counts[ClassBlockedByScene])
                .Append("  no navmesh ").Append(_counts[ClassNoMesh]).Append('\n');
            _readout.Append("Query check: our faces returned by checkIfDisabled=true ").Append(_oursSeenEnabled)
                .Append(", the two calls gave different faces ").Append(_queryFacesDiffer).Append('\n');
            _readout.Append("Centre: ");
            if (centreSlot < 0)
            {
                _readout.Append("outside the sample");
            }
            else
            {
                _readout.Append(ClassNames[_class[centreSlot]]);
                float depth = _depth[centreSlot];
                _readout.Append(", depth ").Append(float.IsNaN(depth) ? "no water reading" : Fmt(depth) + " m");
                if (_face[centreSlot] >= 0)
                {
                    _readout.Append(", face ").Append(_face[centreSlot]);
                }
            }
            return _readout.ToString();
        }

        private static string Fmt(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Fixed pool of dots bound by RBMNavigableDebug.xml, built once at the full size when the overlay is switched
    /// on and never resized; unused dots are hidden. Setters use OnPropertyChangedWithValue (see the overlay's comment).
    /// </summary>
    public class NavigableDebugVM : ViewModel
    {
        private readonly MBBindingList<NavigableDebugDotVM> _dots = new MBBindingList<NavigableDebugDotVM>();
        private int _cursor;
        private int _lastUsed;
        private bool _isActive;
        private string _readout = "";

        public NavigableDebugVM(int dotCount)
        {
            for (int i = 0; i < dotCount; i++)
            {
                _dots.Add(new NavigableDebugDotVM());
            }
        }

        [DataSourceProperty]
        public MBBindingList<NavigableDebugDotVM> Dots => _dots;

        [DataSourceProperty]
        public bool IsActive
        {
            get { return _isActive; }
            set { if (_isActive != value) { _isActive = value; OnPropertyChangedWithValue(value, "IsActive"); } }
        }

        [DataSourceProperty]
        public string Readout
        {
            get { return _readout; }
            set { if (_readout != value) { _readout = value; OnPropertyChangedWithValue(value, "Readout"); } }
        }

        public void BeginFrame()
        {
            _cursor = 0;
        }

        public void PushDot(float x, float y, float size, float alpha, uint color)
        {
            if (_cursor >= _dots.Count)
            {
                return;
            }
            _dots[_cursor++].Set(x, y, size, alpha, color);
        }

        public void EndFrame()
        {
            for (int i = _cursor; i < _lastUsed; i++)
            {
                _dots[i].IsHidden = true;
            }
            _lastUsed = _cursor;
        }

        public void HideAll()
        {
            for (int i = 0; i < _lastUsed; i++)
            {
                _dots[i].IsHidden = true;
            }
            _cursor = 0;
            _lastUsed = 0;
        }
    }

    public class NavigableDebugDotVM : ViewModel
    {
        private float _screenX;
        private float _screenY;
        private float _size = 6f;
        private float _alpha = 1f;
        private uint _colorValue;
        private Color _dotColor = Color.FromUint(0xFFFFFFFFu);
        private bool _isHidden = true;

        // Widgets are placed by their top-left corner, so the centre goes half a size back.
        public void Set(float x, float y, float size, float alpha, uint color)
        {
            ScreenX = x - size * 0.5f;
            ScreenY = y - size * 0.5f;
            Size = size;
            Alpha = alpha;
            if (color != _colorValue)
            {
                _colorValue = color;
                DotColor = Color.FromUint(color);
            }
            IsHidden = false;
        }

        [DataSourceProperty]
        public float ScreenX
        {
            get { return _screenX; }
            set { if (_screenX != value) { _screenX = value; OnPropertyChangedWithValue(value, "ScreenX"); } }
        }

        [DataSourceProperty]
        public float ScreenY
        {
            get { return _screenY; }
            set { if (_screenY != value) { _screenY = value; OnPropertyChangedWithValue(value, "ScreenY"); } }
        }

        [DataSourceProperty]
        public float Size
        {
            get { return _size; }
            set { if (_size != value) { _size = value; OnPropertyChangedWithValue(value, "Size"); } }
        }

        [DataSourceProperty]
        public float Alpha
        {
            get { return _alpha; }
            set { if (_alpha != value) { _alpha = value; OnPropertyChangedWithValue(value, "Alpha"); } }
        }

        [DataSourceProperty]
        public Color DotColor
        {
            get { return _dotColor; }
            set { if (_dotColor != value) { _dotColor = value; OnPropertyChangedWithValue(value, "DotColor"); } }
        }

        [DataSourceProperty]
        public bool IsHidden
        {
            get { return _isHidden; }
            set { if (_isHidden != value) { _isHidden = value; OnPropertyChangedWithValue(value, "IsHidden"); } }
        }
    }
}
