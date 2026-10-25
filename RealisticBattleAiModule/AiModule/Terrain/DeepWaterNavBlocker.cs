using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using RBMConfig.Logging;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using RC = RBMConfig.RBMConfig;

namespace RBMAI
{
    /// <summary>
    /// Deep water blocks AI paths (deepWaterNavBlockEnabled; log deepWaterNavLoggingEnabled to logs/deepwater). In a land
    /// field battle, swim-deep water becomes a no-go area "like a big rock": a hard invisible wall that stops walking,
    /// pushed and knocked-back men and horses, the player included (user rule). Missiles and the camera pass.
    ///
    /// FORCED EXIT (user rule: "they should simply not be there"; for THIS feature it overrides the older "no scripted
    /// swim-to-shore rescue" rule): every AI man (never the player) in a deep cell gets an exit point (BFS through blocked
    /// cells to the nearest non-blocked cell with an enabled navmesh face) and is driven there with
    /// Agent.SetScriptedPosition (precedent: NavalDLC AgentNavalAIComponent), released with DisableScriptedMovement once
    /// out, on removal (no native call then: StopUsingGameObject only calls it while Active), on player control, revert and
    /// mission end. The scripted move is re-issued when its GoToPosition flag is gone (ForcedFlagRecheck). A Frontline pin
    /// (SetTargetPosition, a movement lock) would override it, so the pin is cleared and Frontline skips these men
    /// (DeepWaterSlots.IsForcedExit, a concurrent set read on the worker thread).
    /// The wall piece(s) his line to the exit crosses (his "gate") open ONLY while he is in a deep cell within
    /// GateOpenReach of them and close once he is past, or after GateMaxOpenSeconds. A man OUTSIDE (the player counts)
    /// moving toward or leaning on the piece makes the gate wait; after GateOutsiderWaitSeconds the man is re-routed, and
    /// after GateOutsiderGiveUpSeconds in all the gate opens anyway. At most MaxForcedAtOnce men at once; an attempt times
    /// out after ForcedTimeoutSeconds (or GateOpensPerAttempt openings) and is retried through another piece (FAILED).
    ///
    /// Three layers, all built once in EarlyStart on the main thread (disabling navmesh faces alone only changes path
    /// searches; no runtime API removes single faces):
    ///
    /// 1. GRID: the playable area (navmesh face bbox intersected with Mission.Boundaries, + BoundsMargin) sampled on a
    ///    GridStepMin grid (raised until it fits MaxGridCells). A cell is deep when water level - terrain height >= WallDepth
    ///    (the swim start is 1.34-1.39 m above the feet; a threshold on it flickered on a swamp floor at 1.30 m). Bridge
    ///    check (ProbeCell): a deep cell is NOT deep when (a) Scene.GetHeightAtPoint with the agents' collision exclude
    ///    flags finds a solid surface no lower than the water level - BridgeSurfaceTolerance and (b) the navmesh height
    ///    there (WorldPosition.GetNavMeshZ, NaN when no face) is at that surface too. GetNavMeshFaceIndex is not used for
    ///    this: faces are huge, a shore face straddling the cell would read as a bridge. The grid (DeepWaterGrid: deep +
    ///    blocked = deep dilated by SlotMarginMeters) is immutable, published through a volatile field.
    ///
    /// 2. WALL: invisible collision along the shoreline as a smooth curve (BuildWallCurves: marching squares over the deep
    ///    grid, saddles resolved deep-connected -> closed polylines with deep water on the left -> Laplacian + Chaikin
    ///    smoothing -> Douglas-Peucker -> moved WallDeepBias toward deep water so the wall never cuts into dry cells). Parts
    ///    of deep shore cells lie OUTSIDE it (InsideWall: deep cell AND enclosed by the outer curve, even-odd ray test):
    ///    men there are not DEEPCELL entries and not forced out. Each leg is TWO vertical quads (outer on the curve, inner
    ///    WallLayerGap further in), split to WallMaxQuadLength, running from below the lowest terrain sample to
    ///    WallAboveSurface above water/terrain (nothing to step onto). Both layers of an edge sit in the same piece: the
    ///    quads of one PieceTile tile form one entity (one triangle-mesh body), so one toggle opens both as a gate.
    ///    FLAGS: Moveable | AgentOnly | TwoSided. AgentOnly (0x200000) is NOT in CommonCollisionExcludeFlagsForAgent, so every
    ///    agent collides with it, but it IS in the missile and camera exclude masks, so missiles and the camera pass. (The
    ///    AILimiter flag was tried first: it is in the agent exclude mask - it acts like AI steering, not collision - and did
    ///    not hold shoved men.) Built as NavalDLC ShipAttachmentMachine builds its barriers: GameEntity.CreateEmpty,
    ///    ReplacePhysicsBodyWithQuadPhysicsBody (pinned buffers), removed with Remove(78).
    ///    WINDING: TwoSided did NOT make the quads solid from both sides (crossings went through the back face along the
    ///    (A_top, B_top, B_bottom) normal), so every quad carries both windings (12 indices).
    ///    GATE TOGGLE: SetBodyFlags(BodyFlag | BodyFlags.Disabled) to open, clear Disabled + SetFrameChanged to close (as
    ///    CastleGate switches its door colliders); SetPhysicsState left pieces effectively open.
    ///    PIECE FRAME: each entity's frame sits at its piece with body vertices local to it (the NavalDLC pattern);
    ///    mesh-less entities have a point bounding box, so it is set by hand (SetManualLocalBoundingBox).
    ///
    /// 3. NO SLOTS IN DEEP WATER, two hooks: (a) DeepWaterBatchSlotPatch (DeepWaterSlotPatches.cs), a postfix on the native
    ///    slot batch every LineFormation-derived arrangement uses (the main leak; reads DeepWaterSlots.LiveGrid); (b)
    ///    Mission.IsFormationUnitPositionAvailable_AdditionalCondition, called on WORKER threads for every formation slot
    ///    and by IsOrderPositionAvailable, returns false for a position in a blocked cell (SlotCondition reads only the
    ///    immutable grid: no engine call, no allocation, Interlocked counters). It is a multicast Func: only the LAST
    ///    subscriber's result counts; native subscribes only in SiegeDeploymentHandler.
    ///
    /// PATHS: a navmesh face is switched off when its centre is >= BlockDepth under water or in a deep cell, or when at
    /// least FaceDeepShareBlock of its area near the water is deep (ComputeFaceDeepShares), so path searches route around.
    /// SetAbilityOfFaceWithIndex returns whether the face WAS ENABLED before the call (disable returning true is ours to
    /// record and undo). Faces are switched with updateIslandIndices false except the last. Known gap: a big face under the
    /// share threshold stays enabled (WALLSTUCK lines show men held against the wall). Also NOT a path: during a move,
    /// native HumanAIComponent.ParallelUpdateFormationMovement hands every man within ~22 m of his place in the MOVING block
    /// (never filtered by slots) to Agent.SetFormationIntegrityData, which steers him there directly - a block sweeping a
    /// swamp walks men at the wall. DeepWaterSteering (DeepWaterSlotPatches.cs) filters it: a transpiler swaps both native
    /// call sites and RBM's Move-order call in Frontline/FormationMovement.cs calls it directly; a target in a blocked cell,
    /// or a straight line to it through a deep cell, becomes Vec2.Zero ("not catching up").
    ///
    /// Added under rbmAiEnabled &amp;&amp; deepWaterNavBlockEnabled; acts only when Mission.IsFieldBattle (custom field
    /// battles included). Sieges, sally-outs, naval, raids and every NoTeamAI mission are left alone.
    /// TIMING: EarlyStart, main thread - after MissionCombatantsLogic.EarlyStart (sets MissionTeamAIType), before any
    /// AfterStart; troops spawn later and no AI tick has run yet.
    ///
    /// SAFETY: a failure anywhere re-enables the faces, removes the walls and drops the slot condition. On the first tick
    /// where both sides have active humans, one man per side is checked with DoesPathExistBetweenFaces (ignoreDisabled false
    /// AND true) and a 4-connected flood fill over the grid's non-deep cells (the walls' own connectivity); if any says no,
    /// everything is reverted (REVERTED). Walls and handles are removed at mission end.
    ///
    /// The player is labelled PLAYER; he counts in DEEPCELL but is never forced out, never opens a gate and is left out of the
    /// STUCK / WALLSTUCK diagnostics. Swimmers outside deep cells are only logged (STUCK), never moved.
    ///
    /// Leak audit: closed - LineFormation slots (batch postfix), single-slot checks / order positions (additional
    /// condition), Frontline pins (IsPositionOccupied). Left - ColumnFormation (no slot table; the wall stops the column),
    /// Charge (target is the enemy man), OrderReactionHold (returns an earlier, already filtered slot), and LineFormation's
    /// cached unit position indices (spawn-time lookups only). All per-agent state is instance fields cleared at mission
    /// end; nothing Agent-keyed is static.
    /// </summary>
    public class DeepWaterNavBlocker : MissionLogic
    {
        internal const float BlockDepth = 1.0f; // m of water over a face centre; keep in step with WallDepth

        /// <summary>Water depth (m) over the terrain at which a grid cell is deep. Under the observed swim start (1.34-1.39 m); 1.3 flickered on swamp floors.</summary>
        internal const float WallDepth = 1.0f;

        /// <summary>Faces are judged only on their part within FaceShareRadius of deep water, so a huge face is not diluted.</summary>
        private const float FaceDeepShareBlock = 0.25f;
        private const float FaceShareRadius = 40f;
        private const int FaceShareMaxSamples = 120000; // then every 2nd / 3rd cell
        private const float MaxSideOnDisabledShare = 0.5f;

        private const float BridgeSurfaceTolerance = 0.3f; // m
        private const float BridgeQueryLift = 1f; // m above the water level

        private const float GridStepMin = 1f; // m; raised by 25% steps until the grid fits MaxGridCells
        private const int MaxGridCells = 600000;

        private const float BoundsMargin = 20f; // m
        private const float SlotMarginMeters = 1f; // for the agent's radius

        private const float WallBelowGround = 0.5f; // m below the lower terrain end
        private const float WallAboveSurface = 2.5f; // m above max(water, terrain)

        private const float MaxWallSegmentLength = 6f; // m
        private const float PieceTile = 6f; // m; one tile = one piece / entity / gate

        private const float WallLayerGap = 0.3f; // m toward the deep side
        private const float WallEndOverlap = 0.25f; // m past both cell-edge ends, so seams overlap

        private const int IndicesPerQuad = 12; // both windings

        // wall curve (rounded so men slide instead of catching on staircase corners)

        private const int WallLaplacianPasses = 5; // corners become ~2-3 m radius
        private const int WallChaikinPasses = 2;
        private const float WallSimplifyTolerance = 0.15f; // m, Douglas-Peucker
        private const float WallDeepBias = 0.4f; // m toward deep water
        private const float WallDryPushMax = 0.6f; // m, in 0.1 m steps
        private const float WallMaxQuadLength = 3f; // m
        private const float WallPieceSplitOverlap = 0.1f; // m where consecutive quads fall in different pieces

        /// <summary>Smaller deep regions are dropped and smaller dry holes filled, so a floor at about WallDepth is not a maze of pockets.</summary>
        private const int MinDeepRegionCells = 16;
        private const int MinHoleCells = 16;

        // forced exit + gates

        private const float ForcedScanInterval = 0.5f;
        private const int MaxForcedAtOnce = 80;
        private const float ForcedTimeoutSeconds = 30f;
        private const int GateOpensPerAttempt = 4;
        private const float ForcedFlagRecheck = 0.5f;
        private const float NoExitRetrySeconds = 5f;
        private const float ExitSearchRadius = 60f; // m, BFS through blocked cells
        private const int ExitSearchMaxCells = 40000;
        private const int ExitCandidateCells = 48;
        private const int ExitCandidateGates = 6;
        private const float GateApproachSpan = 3.5f; // m of his line to the exit that picks the gate
        private const float BusyGatePenalty = 4f; // m
        private const float GateOpenReach = 0.6f; // m; he is past it once out of the deep cell and this clear
        private const float GateMaxOpenSeconds = 2f;
        private const float GateReopenCooldown = 1f;
        private const float GateOutsiderRadius = 1.5f; // m
        private const float GateLeaningDistance = 0.6f;
        private const float GateOutsiderMinSpeed = 0.2f;
        private const float GateOutsiderWaitSeconds = 2f; // then the forced man is re-routed
        /// <summary>Total wait over all his gates after which the gate opens even with a man outside: men pressing on the wall never clear.</summary>
        private const float GateOutsiderGiveUpSeconds = 6f;

        // wall diagnostics

        private const int MaxWallLeakLines = 150;
        private const int WallTestPieces = 24;
        private const float WallTestInterval = 60f;
        private const int MaxOpenGates = 20;
        private const int MaxGateLines = 600;
        private const int MaxForcedLines = 600;

        private const int HistoryLength = 5; // one position per sampler tick (0.25 s)

        private const int WallRemoveReason = 78; // what NavalDLC passes for its barrier entities

        private const float MaxDisabledShare = 0.6f;

        private const float ImplausibleWaterLevel = 100000f; // m, beyond = "no water"

        internal const int ProbeNoData = 0;
        internal const int ProbeDry = 1;
        internal const int ProbeWet = 2;
        internal const int ProbeDeep = 3;
        internal const int ProbeBridge = 4;
        internal const int ProbeDeepSurfaceNoNav = 5;

        private const float SampleInterval = 0.25f;
        private const float WatchInterval = 1f;
        private const float FlushInterval = 1f;
        private const float SlotsLogInterval = 10f;
        private const float StuckSeconds = 10f;
        private const float StuckMoveMeters = 2f;
        private const float WallStuckSeconds = 5f;
        private const float WallStuckMoveMeters = 1f;
        private const float WallStuckSlotDistance = 3f;
        private const int MaxSwimLines = 200;
        private const int MaxWatchLines = 600;
        private const int MaxStuckLines = 200;
        private const int MaxDeepCellLines = 200;
        private const int MaxWallStuckLines = 200;
        private const int MaxDepthSamples = 10000;
        private const int VariantStride = 25;
        private const int RepresentativeCandidates = 16;
        private const float ConnectivityWaitLogAt = 30f;

        private static readonly float[] HistogramEdges = { 0.5f, 1f, 1.3f, 2f };
        private static readonly string[] HistogramLabels = { "0-0.5", "0.5-1", "1-1.3", "1.3-2", "2+" };

        private sealed class AgentWaterState
        {
            public bool InWater;
            public bool Mounted;
            public float EnterTime;
            public float EnterDepth;
            public Vec2 AnchorPos;
            public float AnchorTime;
            public bool StuckLogged;
            public bool InDeepCell;
            public bool EverDeep;
            public bool NearWall;
            public Vec2 WallAnchorPos;
            public float WallAnchorTime;
            public bool WallStuckLogged;

            // DEEPCELL diagnostics: ring of the last positions, one per sampler tick.
            public readonly Vec2[] HistPos = new Vec2[HistoryLength];
            public readonly float[] HistTime = new float[HistoryLength];
            public int HistCount;
            public int HistHead;

            // forced exit: no exit found until then (s), and the pieces his failed attempts went through
            public float NoExitUntil;
            public bool NoExitLogged;
            public bool CapRefusedCounted;
            public readonly HashSet<int> ExcludedPieces = new HashSet<int>();

            public void PushHistory(Vec2 pos, float time)
            {
                HistPos[HistHead] = pos;
                HistTime[HistHead] = time;
                HistHead = (HistHead + 1) % HistoryLength;
                if (HistCount < HistoryLength)
                {
                    HistCount++;
                }
            }

            /// <summary>The position <paramref name="back"/> samples ago (1 = the latest pushed), false if not kept.</summary>
            public bool TryGetHistory(int back, out Vec2 pos, out float time)
            {
                pos = Vec2.Zero;
                time = 0f;
                if (back < 1 || back > HistCount)
                {
                    return false;
                }
                int k = (HistHead - back + HistoryLength * 2) % HistoryLength;
                pos = HistPos[k];
                time = HistTime[k];
                return true;
            }
        }

        /// <summary>One wall piece: the segments of one tile in one entity, switchable on its own (a gate).</summary>
        private sealed class WallPiece
        {
            public int Id;
            public GameEntity Entity;
            public GCHandle Vertices;
            public GCHandle Indices;
            public int FirstSegment;
            public int SegmentCount;
            public Vec2 Centre;
            public bool IsOpen;
            public float OpenedAt;
            public float ClosedAt = -100f;
            public string OpenedBy;
            public int TimesOpened;
            /// <summary>The entity's frame origin; the body's vertices are local to it (the NavalDLC pattern).</summary>
            public Vec3 Origin;
            /// <summary>The body's local bounds, set as the entity's manual bounding box (re-applied after a toggle).</summary>
            public BoundingBox LocalBox;

            // rebuilt every forced-exit tick: a forced man in a deep cell within reach wants it open; one just past it is still in the doorway
            public ForcedExit WantedBy;
            public bool Doorway;
        }

        /// <summary>One AI man being driven out of deep water (main thread only).</summary>
        private sealed class ForcedExit
        {
            public Agent Agent;
            public AgentWaterState State;
            public float StartedAt;
            public float AttemptStartedAt;
            public int Attempt;
            public bool HasAttempt;
            public Vec2 StartPos;
            public bool StartSwimming;
            public Vec3 Exit;
            public Vec2 GatePoint;
            public readonly List<int> GatePieces = new List<int>();
            public int GateOpens;
            public bool EverThroughGate;
            public float WaitingSince = -1f;
            public float NextFlagCheck;
            public string WaitingFor;

            // time split for the DONE line: first at his gate, seconds waited for men outside, first opening of a gate for him
            public float FirstReachAt = -1f;
            public float WaitedTotal;
            public float FirstOpenAt = -1f;
        }

        private bool _active;
        private bool _logging;
        private BufferedLogWriter _log;

        private readonly List<int> _disabled = new List<int>();
        private readonly HashSet<int> _disabledSet = new HashSet<int>();
        private string _blockResult = "not run";
        private bool _reverted;
        private bool _connectivityDone;
        private bool _connectivityWaitLogged;

        // read by SlotCondition on worker threads: the grid is immutable once published, the flag is a plain bool
        private volatile DeepWaterGrid _grid;
        private volatile bool _slotConditionLive;
        private long _slotRejections;
        private long _slotRejectionsLogged;
        private Func<WorldPosition, Team, bool> _slotCondition;
        private Mission _subscribedMission;

        private readonly List<WallSegment> _wallSegments = new List<WallSegment>();
        private readonly List<WallPiece> _wallEntities = new List<WallPiece>();
        private readonly Dictionary<long, int> _tileToPiece = new Dictionary<long, int>();
        private int _wallQuads;
        private int _wallEdges;
        private int _wallBoxViaGlobal;
        /// <summary>AI men seen in a deep cell but OUTSIDE the (deep-biased) wall: never forced out, no gate.</summary>
        private readonly HashSet<int> _outsideWallDeepAgents = new HashSet<int>();

        private float _nextForcedScan;
        private int _openGates;
        private int _maxOpenGates;
        private int _gatesOpened;
        private int _gatesClosedPast;
        private int _gatesClosedMax;
        private int _gatesRefusedCap;
        private int _gatesWaitedForOutsider;
        private int _gateLines;
        private int _deepEntriesViaOpenGate;
        private int _deepEntriesViaClosedPiece;
        private int _deepEntriesNoWallCrossed;
        private readonly MBList<Agent> _scratchAllies = new MBList<Agent>();
        private readonly MBList<Agent> _scratchNearby = new MBList<Agent>();

        // Forced exit (main thread).
        private readonly List<ForcedExit> _forced = new List<ForcedExit>();
        private readonly Dictionary<Agent, ForcedExit> _forcedByAgent = new Dictionary<Agent, ForcedExit>();
        private readonly List<WallPiece> _tickPieces = new List<WallPiece>();
        private readonly List<int> _scratchPieces = new List<int>();
        private int[] _bfsQueue;
        private int[] _bfsStamp;
        private int _bfsStampValue;
        private int _forcedLines;
        private int _forcedStarted;
        private int _forcedAgentsStarted;
        private readonly HashSet<int> _forcedAgentIndices = new HashSet<int>();
        private int _forcedDone;
        private int _forcedDoneThroughGate;
        private int _forcedFailed;
        private int _forcedRerouted;
        private int _forcedRemoved;
        private int _forcedReissued;
        private int _forcedNoExit;
        private int _forcedRefusedCap;
        private int _maxForcedAtOnce;
        private float _forcedSecondsTotal;
        private int _forcedDoneWithoutGate;
        private float _forcedToGateTotal;
        private int _forcedToGateCount;
        private float _forcedWaitedTotal;
        private float _forcedInGateTotal;
        private int _forcedInGateCount;
        private int _gatesOpenedDespiteOutsider;

        private readonly HashSet<int> _leakLoggedPieces = new HashSet<int>();
        private int _wallLeakLines;
        private float _nextWallTest;
        private int _wallTestRuns;
        private int _wallTestLastHitsOurs = -1;

        private int _facesByDeepShare;

        private float _nextSample;
        private float _nextWatch;
        private float _nextFlush;
        private float _nextSlotsLog;

        private readonly Dictionary<Agent, AgentWaterState> _states = new Dictionary<Agent, AgentWaterState>();
        private readonly List<float> _enterDepthFoot = new List<float>();
        private readonly List<float> _enterDepthMounted = new List<float>();
        private readonly HashSet<string> _reportedFailures = new HashSet<string>();

        private int _swimLines;
        private int _swimLinesDropped;
        private int _watchLines;
        private int _stuckLines;
        private int _deepCellLines;
        private int _wallStuckLines;
        private int _entriesFoot;
        private int _entriesMounted;
        private int _firstSeenInWater;
        private int _exits;
        private int _playerWaterEntries;
        private int _stuckEpisodes;
        private readonly HashSet<int> _stuckAgentIndices = new HashSet<int>();
        private int _deepCellEntries;
        private int _deepCellEntriesMounted;
        private readonly HashSet<int> _deepCellAgentIndices = new HashSet<int>();
        private int _playerDeepCellEntries;
        private int _wallStuckEpisodes;
        private int _wallStuckDrySlot;
        private int _swimEnterModeNone;
        private int _swimEnterModeWater;
        private int _removedInWater;
        private int _maxInWater;
        private int _maxOnDisabled;
        private int _maxInDeepCell;
        private int _lastWatchInWater;
        private int _lastWatchOnDisabled;
        private int _lastWatchInDeep;

        // read-only state for NavigableDebugOverlay (main thread only)

        /// <summary>True in a land field battle once EarlyStart ran (the pass may still have blocked nothing).</summary>
        internal bool IsActive => _active;

        /// <summary>The pass's outcome in words: "not run", "blocked N faces, M wall quads", "reverted", ...</summary>
        internal string BlockResult => _blockResult;

        /// <summary>True once everything this logic did (faces, walls, slot condition) was undone.</summary>
        internal bool IsReverted => _reverted;

        internal int DisabledFaceCount => _disabled.Count;

        /// <summary>True when this logic switched that navmesh face off (and has not re-enabled it).</summary>
        internal bool IsFaceDisabledByUs(int faceIndex)
        {
            return _disabledSet.Contains(faceIndex);
        }

        /// <summary>The deep-water grid, or null. Kept after a revert (it then shows what would be walled off).</summary>
        internal DeepWaterGrid Grid => _grid;

        /// <summary>True while the wall entities stand and the slot condition is subscribed.</summary>
        internal bool IsWallLive => _wallEntities.Count > 0 || _slotConditionLive;

        internal IList<WallSegment> WallSegments => _wallSegments;
        internal int WallQuadCount => _wallQuads;
        internal int WallEntityCount => _wallEntities.Count;
        internal long SlotRejections => Interlocked.Read(ref _slotRejections);
        internal int OpenGateCount => _openGates;

        /// <summary>True while that wall piece is switched off as a gate.</summary>
        internal bool IsPieceOpen(int pieceId)
        {
            return pieceId >= 0 && pieceId < _wallEntities.Count && _wallEntities[pieceId].IsOpen;
        }

        internal int ForcedExitCount => _forced.Count;

        /// <summary>Copies each forced man's position and his exit point (main thread); returns how many were copied.</summary>
        internal int CopyForcedExitMarkers(Vec3[] men, Vec3[] exits)
        {
            int n = 0;
            for (int k = 0; k < _forced.Count && n < men.Length && n < exits.Length; k++)
            {
                Agent agent = _forced[k].Agent;
                if (agent == null || !agent.IsActive())
                {
                    continue;
                }
                men[n] = agent.Position;
                exits[n] = _forced[k].Exit;
                n++;
            }
            return n;
        }

        public override void EarlyStart()
        {
            base.EarlyStart();
            ClearState();
            Mission mission = Mission;
            _active = mission != null && mission.Scene != null && mission.IsFieldBattle;
            if (!_active)
            {
                return;
            }

            _logging = RC.deepWaterNavLoggingEnabled;
            if (_logging)
            {
                OpenLog();
            }

            try
            {
                WriteHeader(mission);
            }
            catch (Exception e)
            {
                ReportOnce("header", e);
            }

            RunPass(mission);
            Flush();
        }

        public override void OnMissionTick(float dt)
        {
            if (!_active || Mission == null || Mission.Scene == null)
            {
                return;
            }
            float now = Mission.CurrentTime;

            if (!_connectivityDone)
            {
                try
                {
                    TryConnectivityCheck(Mission, now);
                }
                catch (Exception e)
                {
                    _connectivityDone = true;
                    ReportOnce("connectivity", e);
                    // a check that cannot run cannot vouch for the blocked navmesh
                    RevertAll("connectivity check threw " + e.GetType().Name);
                }
            }

            if (_wallEntities.Count > 0)
            {
                if (now >= _nextForcedScan)
                {
                    _nextForcedScan = now + ForcedScanInterval;
                    try
                    {
                        ForcedExitScan(Mission, now);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("forcedScan", e);
                    }
                }
                if (_forced.Count > 0 || _openGates > 0)
                {
                    try
                    {
                        ForcedExitTick(Mission, now);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("forcedTick", e);
                    }
                }
            }

            if (!_logging)
            {
                return;
            }

            if (now >= _nextWallTest && _wallEntities.Count > 0)
            {
                _nextWallTest = now + WallTestInterval;
                try
                {
                    WallTest(Mission.Scene, now);
                }
                catch (Exception e)
                {
                    ReportOnce("wallTest", e);
                }
            }

            if (now >= _nextSample)
            {
                _nextSample = now + SampleInterval;
                try
                {
                    SampleSwimmers(Mission, now);
                }
                catch (Exception e)
                {
                    ReportOnce("swimSampler", e);
                }
            }

            if (now >= _nextWatch)
            {
                _nextWatch = now + WatchInterval;
                try
                {
                    StuckWatch(Mission, now);
                }
                catch (Exception e)
                {
                    ReportOnce("stuckWatch", e);
                }
            }

            if (now >= _nextSlotsLog)
            {
                _nextSlotsLog = now + SlotsLogInterval;
                long total = Interlocked.Read(ref _slotRejections);
                if (total != _slotRejectionsLogged)
                {
                    Log("t=" + Fmt(now) + "\tSLOTS\trejected=" + (total - _slotRejectionsLogged) + " in the last "
                        + Fmt(SlotsLogInterval) + "s\ttotal=" + total + "\tconditionLive=" + (_slotConditionLive ? "1" : "0"));
                    _slotRejectionsLogged = total;
                }
            }

            if (now >= _nextFlush)
            {
                _nextFlush = now + FlushInterval;
                Flush();
            }
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
            if (affectedAgent == null)
            {
                return;
            }
            ForcedExit forced;
            if (_forcedByAgent.TryGetValue(affectedAgent, out forced))
            {
                // removed agent: no DisableScriptedMovement (native StopUsingGameObject only calls it while Active); tracking is dropped
                _forcedRemoved++;
                EndForcedExit(forced, CurrentTime(), "REMOVED", "agent " + agentState, false);
            }
            AgentWaterState state;
            if (_states.TryGetValue(affectedAgent, out state))
            {
                if (state.InWater)
                {
                    _removedInWater++;
                }
                _states.Remove(affectedAgent);
            }
        }

        protected override void OnEndMission()
        {
            base.OnEndMission();
            Finish();
        }

        public override void OnRemoveBehavior()
        {
            Finish();
            base.OnRemoveBehavior();
        }

        private void Finish()
        {
            ReleaseAllForced("mission end");
            if (_active)
            {
                try
                {
                    WriteEndSummary();
                }
                catch (Exception e)
                {
                    ReportOnce("endSummary", e);
                }
            }
            UnsubscribeSlotCondition();
            RemoveWalls();
            if (_log != null)
            {
                _log.Close();
                _log = null;
            }
            ClearState();
        }

        private void ClearState()
        {
            _active = false;
            _logging = false;
            _disabled.Clear();
            _disabledSet.Clear();
            _blockResult = "not run";
            _reverted = false;
            _connectivityDone = false;
            _connectivityWaitLogged = false;
            _grid = null;
            _slotConditionLive = false;
            Interlocked.Exchange(ref _slotRejections, 0L);
            _slotRejectionsLogged = 0;
            _wallSegments.Clear();
            _tileToPiece.Clear();
            _wallQuads = 0;
            _wallEdges = 0;
            _wallBoxViaGlobal = 0;
            _encA.Clear();
            _encB.Clear();
            _encBands.Clear();
            _outsideWallDeepAgents.Clear();
            _nextForcedScan = 0f;
            _openGates = 0;
            _maxOpenGates = 0;
            _gatesOpened = 0;
            _gatesClosedPast = 0;
            _gatesClosedMax = 0;
            _gatesRefusedCap = 0;
            _gatesWaitedForOutsider = 0;
            _gateLines = 0;
            _deepEntriesViaOpenGate = 0;
            _deepEntriesViaClosedPiece = 0;
            _deepEntriesNoWallCrossed = 0;
            _scratchAllies.Clear();
            _scratchNearby.Clear();
            _forced.Clear();
            _forcedByAgent.Clear();
            DeepWaterSlots.ClearForcedExits();
            _tickPieces.Clear();
            _scratchPieces.Clear();
            _bfsQueue = null;
            _bfsStamp = null;
            _bfsStampValue = 0;
            _forcedLines = 0;
            _forcedStarted = 0;
            _forcedAgentsStarted = 0;
            _forcedAgentIndices.Clear();
            _forcedDone = 0;
            _forcedDoneThroughGate = 0;
            _forcedFailed = 0;
            _forcedRerouted = 0;
            _forcedRemoved = 0;
            _forcedReissued = 0;
            _forcedNoExit = 0;
            _forcedRefusedCap = 0;
            _maxForcedAtOnce = 0;
            _forcedSecondsTotal = 0f;
            _forcedDoneWithoutGate = 0;
            _forcedToGateTotal = 0f;
            _forcedToGateCount = 0;
            _forcedWaitedTotal = 0f;
            _forcedInGateTotal = 0f;
            _forcedInGateCount = 0;
            _gatesOpenedDespiteOutsider = 0;
            _leakLoggedPieces.Clear();
            _wallLeakLines = 0;
            _nextWallTest = 0f;
            _wallTestRuns = 0;
            _wallTestLastHitsOurs = -1;
            _facesByDeepShare = 0;
            _wallStuckDrySlot = 0;
            _swimEnterModeNone = 0;
            _swimEnterModeWater = 0;
            _nextSample = 0f;
            _nextWatch = 0f;
            _nextFlush = 0f;
            _nextSlotsLog = 0f;
            _states.Clear();
            _enterDepthFoot.Clear();
            _enterDepthMounted.Clear();
            _reportedFailures.Clear();
            _swimLines = 0;
            _swimLinesDropped = 0;
            _watchLines = 0;
            _stuckLines = 0;
            _deepCellLines = 0;
            _wallStuckLines = 0;
            _entriesFoot = 0;
            _entriesMounted = 0;
            _firstSeenInWater = 0;
            _exits = 0;
            _playerWaterEntries = 0;
            _stuckEpisodes = 0;
            _stuckAgentIndices.Clear();
            _deepCellEntries = 0;
            _deepCellEntriesMounted = 0;
            _deepCellAgentIndices.Clear();
            _playerDeepCellEntries = 0;
            _wallStuckEpisodes = 0;
            _removedInWater = 0;
            _maxInWater = 0;
            _maxOnDisabled = 0;
            _maxInDeepCell = 0;
            _lastWatchInWater = 0;
            _lastWatchOnDisabled = 0;
            _lastWatchInDeep = 0;
        }

        // the pass: faces scan -> grid -> walls -> slot condition -> face disabling

        private void RunPass(Mission mission)
        {
            Scene scene = mission.Scene;
            long startTicks = DateTime.UtcNow.Ticks;
            try
            {
                int faceCount = scene.GetNavMeshFaceCount();
                Vec3[] centres = new Vec3[Math.Max(0, faceCount)];
                float[] levels = new float[centres.Length];
                ScanFaces(scene, centres, levels);

                DeepWaterGrid grid = null;
                try
                {
                    grid = BuildGrid(mission, scene, centres);
                }
                catch (Exception e)
                {
                    ReportOnce("grid", e);
                    grid = null;
                }
                _grid = grid;

                bool wallOk = grid != null && grid.DeepCount > 0 && grid.DeepCount <= MaxDisabledShare * grid.CellCount;
                if (grid != null && grid.DeepCount > MaxDisabledShare * grid.CellCount)
                {
                    Log("GRID\tguard\t" + grid.DeepCount + " of " + grid.CellCount + " cells deep, above the "
                        + Fmt(100f * MaxDisabledShare) + "% guard -- the water query is assumed wrong; no walls, no slot condition");
                }

                if (wallOk)
                {
                    BuildWalls(scene, grid);
                }

                if (wallOk)
                {
                    _slotCondition = SlotCondition;
                    mission.IsFormationUnitPositionAvailable_AdditionalCondition += _slotCondition;
                    _subscribedMission = mission;
                    _slotConditionLive = true;
                    // read by the native slot batch patch and Frontline's pin gate
                    Interlocked.Exchange(ref DeepWaterSlots.PatchedUnavailable, 0L);
                    Interlocked.Exchange(ref DeepWaterSteering.Calls, 0L);
                    Interlocked.Exchange(ref DeepWaterSteering.Nulled, 0L);
                    DeepWaterSlots.LiveGrid = grid;
                    Log("SLOTS\tcondition subscribed, batch slot patch live\tmargin=" + Fmt(SlotMarginMeters) + "m\tblockedCells=" + grid.BlockedCount);
                }

                int[] shareTotal = null;
                int[] shareDeep = null;
                if (wallOk)
                {
                    try
                    {
                        ComputeFaceDeepShares(scene, grid, centres.Length, out shareTotal, out shareDeep);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("faceShare", e);
                        shareTotal = null;
                        shareDeep = null;
                    }
                }
                DisableFaces(scene, centres, levels, wallOk ? grid : null, shareTotal, shareDeep);

                _blockResult = "faces off " + _disabled.Count + ", wall quads " + _wallQuads + ", slot condition "
                    + (_slotConditionLive ? "on" : "off");
                Log("PASS\tdone\t" + _blockResult + "\ttotalMs=" + Fmt(ElapsedMs(startTicks)));
            }
            catch (Exception e)
            {
                ReportOnce("pass", e);
                RevertAll("pass threw " + e.GetType().Name + ": " + e.Message);
                _blockResult = "failed (" + e.GetType().Name + ")";
            }
        }

        private void ScanFaces(Scene scene, Vec3[] centres, float[] levels)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            int wet = 0;
            int deep = 0;
            int implausible = 0;
            float maxDepth = 0f;
            int[] histogram = new int[HistogramLabels.Length];
            int variantSampled = 0;
            int[] variantWet = new int[4];
            int[] variantBlock = new int[4];

            for (int i = 0; i < centres.Length; i++)
            {
                Vec3 center = Vec3.Zero;
                scene.GetNavMeshCenterPosition(i, ref center);
                float level = scene.GetWaterLevelAtPosition(center.AsVec2, true, true);
                centres[i] = center;
                levels[i] = level;

                if (i % VariantStride == 0)
                {
                    variantSampled++;
                    CountVariant(0, level, center.z, variantWet, variantBlock);
                    CountVariant(1, scene.GetWaterLevelAtPosition(center.AsVec2, true, false), center.z, variantWet, variantBlock);
                    CountVariant(2, scene.GetWaterLevelAtPosition(center.AsVec2, false, true), center.z, variantWet, variantBlock);
                    CountVariant(3, scene.GetWaterLevelAtPosition(center.AsVec2, false, false), center.z, variantWet, variantBlock);
                }

                if (!IsPlausible(level))
                {
                    implausible++;
                    continue;
                }
                float depth = level - center.z;
                if (depth <= 0f)
                {
                    continue;
                }
                wet++;
                histogram[Bucket(depth)]++;
                if (depth > maxDepth)
                {
                    maxDepth = depth;
                }
                if (depth >= BlockDepth)
                {
                    deep++;
                }
            }

            Log("BLOCK\tscan\tfaces=" + centres.Length + "\twet=" + wet + "\tcentre deep(>=" + Fmt(BlockDepth) + "m)=" + deep
                + "\timplausibleLevel=" + implausible + "\tmaxDepth=" + Fmt(maxDepth) + "\tscanMs=" + Fmt(ElapsedMs(startTicks)));
            StringBuilder hist = new StringBuilder();
            hist.Append("BLOCK\thistogram(wet faces by centre depth, m)");
            for (int b = 0; b < histogram.Length; b++)
            {
                hist.Append('\t').Append(HistogramLabels[b]).Append('=').Append(histogram[b]);
            }
            Log(hist.ToString());
            Log("BLOCK\tvariants(every " + VariantStride + "th face, " + variantSampled + " sampled; wet/deep)"
                + "\trenderer+bodies=" + variantWet[0] + "/" + variantBlock[0]
                + "\trenderer only=" + variantWet[1] + "/" + variantBlock[1]
                + "\tbodies only=" + variantWet[2] + "/" + variantBlock[2]
                + "\tneither=" + variantWet[3] + "/" + variantBlock[3]
                + "\t(the pass uses renderer+bodies)");
        }

        /// <summary>The deep-water rule for one point, shared by the grid and the NavigableDebugOverlay preview (main thread). Returns a Probe* code; depth = water level - terrain height (NaN without a reading).</summary>
        internal static int ProbeCell(Scene scene, Vec2 xy, out float depth)
        {
            depth = float.NaN;
            float ground = scene.GetTerrainHeight(xy, true);
            if (!IsPlausible(ground))
            {
                return ProbeNoData;
            }
            float level = scene.GetWaterLevelAtPosition(xy, true, true);
            if (!IsPlausible(level))
            {
                return ProbeDry;
            }
            depth = level - ground;
            if (depth <= 0f)
            {
                return ProbeDry;
            }
            if (depth < WallDepth)
            {
                return ProbeWet;
            }

            // bridge check (a): a solid surface at the water line (deck or rock)
            float surface = float.NaN;
            bool hit = scene.GetHeightAtPoint(xy, BodyFlags.CommonCollisionExcludeFlagsForAgent, ref surface);
            if (!hit || !IsPlausible(surface) || surface < level - BridgeSurfaceTolerance)
            {
                return ProbeDeep;
            }
            // (b): the navmesh is up there too
            WorldPosition query = new WorldPosition(scene, UIntPtr.Zero, new Vec3(xy.x, xy.y, level + BridgeQueryLift, -1f), false);
            float navZ = query.GetNavMeshZ();
            if (IsPlausible(navZ) && navZ >= level - BridgeSurfaceTolerance)
            {
                return ProbeBridge;
            }
            return ProbeDeepSurfaceNoNav;
        }

        internal static bool IsDeepProbe(int probe)
        {
            return probe == ProbeDeep || probe == ProbeDeepSurfaceNoNav;
        }

        private DeepWaterGrid BuildGrid(Mission mission, Scene scene, Vec3[] centres)
        {
            long startTicks = DateTime.UtcNow.Ticks;

            // bounds: navmesh face centres, intersected with the mission boundaries when they have points
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < centres.Length; i++)
            {
                Vec3 c = centres[i];
                if (!IsPlausible(c.x) || !IsPlausible(c.y))
                {
                    continue;
                }
                if (c.x < minX) minX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.x > maxX) maxX = c.x;
                if (c.y > maxY) maxY = c.y;
            }
            if (minX > maxX || minY > maxY)
            {
                Log("GRID\tskipped\tno navmesh face centre to bound the grid");
                return null;
            }
            minX -= BoundsMargin;
            minY -= BoundsMargin;
            maxX += BoundsMargin;
            maxY += BoundsMargin;
            string boundsSource = "navmesh";

            float bMinX = float.MaxValue, bMinY = float.MaxValue, bMaxX = float.MinValue, bMaxY = float.MinValue;
            int boundaryPoints = 0;
            try
            {
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
                            if (p.x < bMinX) bMinX = p.x;
                            if (p.y < bMinY) bMinY = p.y;
                            if (p.x > bMaxX) bMaxX = p.x;
                            if (p.y > bMaxY) bMaxY = p.y;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ReportOnce("boundaries", e);
                boundaryPoints = 0;
            }
            if (boundaryPoints > 0)
            {
                float iMinX = Math.Max(minX, bMinX - BoundsMargin);
                float iMinY = Math.Max(minY, bMinY - BoundsMargin);
                float iMaxX = Math.Min(maxX, bMaxX + BoundsMargin);
                float iMaxY = Math.Min(maxY, bMaxY + BoundsMargin);
                if (iMinX < iMaxX && iMinY < iMaxY)
                {
                    minX = iMinX;
                    minY = iMinY;
                    maxX = iMaxX;
                    maxY = iMaxY;
                    boundsSource = "navmesh ∩ boundaries(" + boundaryPoints + " pts)";
                }
            }

            float width = maxX - minX;
            float height = maxY - minY;
            float step = GridStepMin;
            while ((long)Math.Ceiling(width / step) * (long)Math.Ceiling(height / step) > MaxGridCells)
            {
                step *= 1.25f;
            }
            int nx = Math.Max(1, (int)Math.Ceiling(width / step));
            int ny = Math.Max(1, (int)Math.Ceiling(height / step));

            bool[] deep = new bool[nx * ny];
            // bridge cells are kept for BattleTerrainRead.Build: its own probe runs in AfterStart, after this pass switched faces off, and misses them
            bool[] bridge = new bool[nx * ny];
            int deepCount = 0, wet = 0, bridges = 0, surfaceNoNav = 0, noData = 0;
            for (int j = 0; j < ny; j++)
            {
                float y = minY + (j + 0.5f) * step;
                for (int i = 0; i < nx; i++)
                {
                    float depth;
                    int probe = ProbeCell(scene, new Vec2(minX + (i + 0.5f) * step, y), out depth);
                    switch (probe)
                    {
                        case ProbeNoData: noData++; break;
                        case ProbeWet: wet++; break;
                        case ProbeBridge: bridges++; bridge[j * nx + i] = true; break;
                        case ProbeDeepSurfaceNoNav: surfaceNoNav++; deep[j * nx + i] = true; deepCount++; break;
                        case ProbeDeep: deep[j * nx + i] = true; deepCount++; break;
                    }
                }
            }

            // clean-up: drop tiny deep pockets and fill tiny dry holes (a floor lying flat at about WallDepth turns the cell rule into noise)
            int droppedRegions, droppedCells, filledHoles, filledCells;
            CleanSmallRegions(deep, nx, ny, true, MinDeepRegionCells, out droppedRegions, out droppedCells);
            CleanSmallRegions(deep, nx, ny, false, MinHoleCells, out filledHoles, out filledCells);
            deepCount += filledCells - droppedCells;

            int dilate = Math.Max(1, (int)Math.Ceiling(SlotMarginMeters / step));
            bool[] blocked = new bool[nx * ny];
            int blockedCount = 0;
            for (int j = 0; j < ny; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    if (!deep[j * nx + i])
                    {
                        continue;
                    }
                    for (int dj = -dilate; dj <= dilate; dj++)
                    {
                        int jj = j + dj;
                        if (jj < 0 || jj >= ny)
                        {
                            continue;
                        }
                        for (int di = -dilate; di <= dilate; di++)
                        {
                            int ii = i + di;
                            if (ii < 0 || ii >= nx)
                            {
                                continue;
                            }
                            int k = jj * nx + ii;
                            if (!blocked[k])
                            {
                                blocked[k] = true;
                                blockedCount++;
                            }
                        }
                    }
                }
            }

            // dry islands enclosed by deep water (walled all round): a man whose slot is on one cannot walk there
            int islands, islandCells, largestIsland;
            CountEnclosedDryRegions(deep, nx, ny, out islands, out islandCells, out largestIsland);

            DeepWaterGrid grid = new DeepWaterGrid(minX, minY, step, nx, ny, deep, blocked, deepCount, blockedCount, bridge, bridges);
            Log("GRID\tbounds=" + Fmt(minX) + "," + Fmt(minY) + " .. " + Fmt(maxX) + "," + Fmt(maxY) + " (" + boundsSource + ")"
                + "\tstep=" + Fmt(step) + "m\tcells=" + nx + "x" + ny + "=" + (nx * ny)
                + "\tdeep(>=" + Fmt(WallDepth) + "m)=" + deepCount
                + "\tof which surface-but-no-navmesh=" + surfaceNoNav
                + "\tbridgeExcluded=" + bridges
                + "\tshallowWet=" + wet
                + "\tnoTerrain=" + noData
                + "\tblocked(+" + dilate + " cell margin)=" + blockedCount
                + "\tsmallDeepDropped=" + droppedRegions + " regions/" + droppedCells + " cells"
                + "\tsmallHolesFilled=" + filledHoles + " holes/" + filledCells + " cells"
                + "\tenclosedDryIslandsLeft=" + islands + " (" + islandCells + " cells, largest " + largestIsland + ")"
                + "\tms=" + Fmt(ElapsedMs(startTicks)));
            return grid;
        }

        /// <summary>The 4-connected non-deep regions that do not touch the grid border (dry ground walled all round).</summary>
        private static void CountEnclosedDryRegions(bool[] deep, int nx, int ny, out int regions, out int cells, out int largest)
        {
            regions = 0;
            cells = 0;
            largest = 0;
            bool[] seen = new bool[deep.Length];
            int[] stack = new int[deep.Length];
            for (int start = 0; start < deep.Length; start++)
            {
                if (seen[start] || deep[start])
                {
                    continue;
                }
                int size = 0;
                bool touchesBorder = false;
                int sp = 0;
                stack[sp++] = start;
                seen[start] = true;
                while (sp > 0)
                {
                    int k = stack[--sp];
                    size++;
                    int i = k % nx;
                    int j = k / nx;
                    if (i == 0 || j == 0 || i == nx - 1 || j == ny - 1)
                    {
                        touchesBorder = true;
                    }
                    if (i > 0) Visit(deep, seen, stack, ref sp, k - 1, false);
                    if (i < nx - 1) Visit(deep, seen, stack, ref sp, k + 1, false);
                    if (j > 0) Visit(deep, seen, stack, ref sp, k - nx, false);
                    if (j < ny - 1) Visit(deep, seen, stack, ref sp, k + nx, false);
                }
                if (touchesBorder)
                {
                    continue;
                }
                regions++;
                cells += size;
                if (size > largest)
                {
                    largest = size;
                }
            }
        }

        /// <summary>Flood-fills 4-connected regions whose cells equal value and flips every region smaller than minCells. For dry holes (value false) a region touching the grid border is kept: it is the land around the water.</summary>
        private static void CleanSmallRegions(bool[] deep, int nx, int ny, bool value, int minCells, out int regions, out int cells)
        {
            regions = 0;
            cells = 0;
            bool[] seen = new bool[deep.Length];
            int[] stack = new int[deep.Length];
            List<int> region = new List<int>();
            for (int start = 0; start < deep.Length; start++)
            {
                if (seen[start] || deep[start] != value)
                {
                    continue;
                }
                region.Clear();
                bool touchesBorder = false;
                int sp = 0;
                stack[sp++] = start;
                seen[start] = true;
                while (sp > 0)
                {
                    int k = stack[--sp];
                    region.Add(k);
                    int i = k % nx;
                    int j = k / nx;
                    if (i == 0 || j == 0 || i == nx - 1 || j == ny - 1)
                    {
                        touchesBorder = true;
                    }
                    if (i > 0) Visit(deep, seen, stack, ref sp, k - 1, value);
                    if (i < nx - 1) Visit(deep, seen, stack, ref sp, k + 1, value);
                    if (j > 0) Visit(deep, seen, stack, ref sp, k - nx, value);
                    if (j < ny - 1) Visit(deep, seen, stack, ref sp, k + nx, value);
                }
                if (region.Count >= minCells || (!value && touchesBorder))
                {
                    continue;
                }
                regions++;
                cells += region.Count;
                foreach (int k in region)
                {
                    deep[k] = !value;
                }
            }
        }

        private static void Visit(bool[] deep, bool[] seen, int[] stack, ref int sp, int k, bool value)
        {
            if (!seen[k] && deep[k] == value)
            {
                seen[k] = true;
                stack[sp++] = k;
            }
        }

        private void BuildWalls(Scene scene, DeepWaterGrid g)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            // the wall follows the shoreline as a curve (not the cell-edge staircase whose corners caught men): marching squares -> polylines (deep on the left) -> smoothing -> simplification -> offset toward deep water -> quads of both layers
            WallContourStats stats = new WallContourStats();
            List<List<Vec2>> outerLines;
            List<List<Vec2>> innerLines;
            List<bool> closedFlags;
            BuildWallCurves(g, stats, out outerLines, out innerLines, out closedFlags);
            _encA.Clear();
            _encB.Clear();
            _encBands.Clear();

            // raw entry: outer A, outer B, inner A, inner B, deep side, (start overlap, end overlap); long legs are split to WallMaxQuadLength; overlap only where the neighbour lies in another piece
            List<Vec2[]> raw = new List<Vec2[]>();
            List<long> rawTile = new List<long>();
            float shortest = float.MaxValue;
            for (int l = 0; l < outerLines.Count; l++)
            {
                List<Vec2> outer = outerLines[l];
                List<Vec2> inner = innerLines[l];
                bool closed = closedFlags[l];
                int n = outer.Count;
                int legs = closed ? n : n - 1;
                int first = raw.Count;
                for (int k = 0; k < legs; k++)
                {
                    Vec2 oa = outer[k];
                    Vec2 ob = outer[(k + 1) % n];
                    Vec2 ia0 = inner[k];
                    Vec2 ib0 = inner[(k + 1) % n];
                    float length = oa.Distance(ob);
                    if (length < 1e-4f)
                    {
                        continue;
                    }
                    if (closed)
                    {
                        // the enclosure test (InsideWall) runs on the exact outer polyline, never on overlapped quads
                        AddEnclosureEdge(oa, ob);
                    }
                    int parts = Math.Max(1, (int)Math.Ceiling(length / WallMaxQuadLength));
                    for (int p = 0; p < parts; p++)
                    {
                        float t0 = p / (float)parts;
                        float t1 = (p + 1) / (float)parts;
                        Vec2 a = oa + (ob - oa) * t0;
                        Vec2 b = oa + (ob - oa) * t1;
                        Vec2 ia = ia0 + (ib0 - ia0) * t0;
                        Vec2 ib = ia0 + (ib0 - ia0) * t1;
                        Vec2 d = b - a;
                        Vec2 deepSide = new Vec2(-d.y, d.x).Normalized();
                        raw.Add(new[] { a, b, ia, ib, deepSide, Vec2.Zero });
                        rawTile.Add(TileKey((a + b) * 0.5f));
                        float sub = a.Distance(b);
                        if (sub < shortest)
                        {
                            shortest = sub;
                        }
                    }
                }
                int count = raw.Count - first;
                for (int q = 0; q < count; q++)
                {
                    int idx = first + q;
                    bool hasPrev = closed || q > 0;
                    bool hasNext = closed || q < count - 1;
                    int prev = first + (q - 1 + count) % count;
                    int next = first + (q + 1) % count;
                    float startExt = hasPrev && rawTile[prev] != rawTile[idx] ? WallPieceSplitOverlap : 0f;
                    float endExt = hasNext && rawTile[next] != rawTile[idx] ? WallPieceSplitOverlap : 0f;
                    raw[idx][5] = new Vec2(startExt, endExt);
                    if (startExt > 0f || endExt > 0f)
                    {
                        stats.PieceJoints++;
                    }
                }
            }

            float totalLength = 0f;
            float maxHeight = 0f;
            _wallEdges = 0;
            // segments grouped by the PieceTile tile of their outer quad's midpoint: one tile = one piece = one entity = one gate
            Dictionary<long, List<WallSegment>> byTile = new Dictionary<long, List<WallSegment>>();
            List<long> tileOrder = new List<long>();
            for (int r = 0; r < raw.Count; r++)
            {
                Vec2[] s = raw[r];
                Vec2 a = s[0];
                Vec2 b = s[1];
                Vec2 deepSide = s[4];
                Vec2 mid = (a + b) * 0.5f;
                Vec2 along = (b - a).Normalized();
                Vec2 ea = a - along * s[5].x;
                Vec2 eb = b + along * s[5].y;
                Vec2 innerAlong = (s[3] - s[2]).Normalized();
                Vec2 ia = s[2] - innerAlong * s[5].x;
                Vec2 ib = s[3] + innerAlong * s[5].y;
                Vec2 imid = (s[2] + s[3]) * 0.5f;
                float low = float.MaxValue;
                float high = float.MinValue;
                bool plausible = true;
                SampleTerrain(scene, ea, ref low, ref high, ref plausible);
                SampleTerrain(scene, eb, ref low, ref high, ref plausible);
                SampleTerrain(scene, mid, ref low, ref high, ref plausible);
                SampleTerrain(scene, ia, ref low, ref high, ref plausible);
                SampleTerrain(scene, ib, ref low, ref high, ref plausible);
                SampleTerrain(scene, imid, ref low, ref high, ref plausible);
                float level = scene.GetWaterLevelAtPosition(mid, true, true);
                if (!plausible)
                {
                    continue;
                }
                float surface = IsPlausible(level) ? Math.Max(level, high) : high;
                float bottom = low - WallBelowGround;
                float top = surface + WallAboveSurface;
                long key = rawTile[r];
                List<WallSegment> list;
                if (!byTile.TryGetValue(key, out list))
                {
                    list = new List<WallSegment>();
                    byTile[key] = list;
                    tileOrder.Add(key);
                }
                list.Add(new WallSegment(ea, eb, bottom, top, surface, -1, deepSide, 0));
                list.Add(new WallSegment(ia, ib, bottom, top, surface, -1, deepSide, 1));
                _wallEdges++;
                totalLength += a.Distance(b);
                if (top - bottom > maxHeight)
                {
                    maxHeight = top - bottom;
                }
            }

            PhysicsMaterial material = PhysicsMaterial.GetFromName("default");
            if (!material.IsValid)
            {
                material = PhysicsMaterial.GetFromName("wood_nonstick");
            }
            BodyFlags flags = BodyFlags.Moveable | BodyFlags.AgentOnly | BodyFlags.TwoSided;
            int physicsOnAfterBuild = 0;
            int trianglesTotal = 0;
            int bboxCoversWall = 0;
            int disabledFlagAfterBuild = 0;
            string samplePiece = "-";

            foreach (long key in tileOrder)
            {
                List<WallSegment> tileSegments = byTile[key];
                int pieceId = _wallEntities.Count;
                int count = tileSegments.Count;
                Vec3[] vertices = new Vec3[count * 4];
                int[] indices = new int[count * IndicesPerQuad];
                Vec2 centre = Vec2.Zero;
                float minBottom = float.MaxValue;
                for (int q = 0; q < count; q++)
                {
                    centre += (tileSegments[q].A + tileSegments[q].B) * 0.5f;
                    minBottom = Math.Min(minBottom, tileSegments[q].Bottom);
                }
                centre *= 1f / count;
                // the body's vertices are LOCAL to the entity, whose frame sits at the piece (as NavalDLC builds its bodies); a world-origin frame with world-space vertices let men through closed pieces
                Vec3 origin = new Vec3(centre.x, centre.y, minBottom, -1f);
                int firstSegment = _wallSegments.Count;
                for (int q = 0; q < count; q++)
                {
                    WallSegment raw0 = tileSegments[q];
                    WallSegment s = new WallSegment(raw0.A, raw0.B, raw0.Bottom, raw0.Top, raw0.SurfaceZ, pieceId, raw0.DeepSide, raw0.Layer);
                    _wallSegments.Add(s);
                    // corner order as NavalDLC's ArrangeBarrier: start top, end top, end bottom, start bottom
                    vertices[q * 4 + 0] = new Vec3(s.A.x - origin.x, s.A.y - origin.y, s.Top - origin.z, -1f);
                    vertices[q * 4 + 1] = new Vec3(s.B.x - origin.x, s.B.y - origin.y, s.Top - origin.z, -1f);
                    vertices[q * 4 + 2] = new Vec3(s.B.x - origin.x, s.B.y - origin.y, s.Bottom - origin.z, -1f);
                    vertices[q * 4 + 3] = new Vec3(s.A.x - origin.x, s.A.y - origin.y, s.Bottom - origin.z, -1f);
                    // both windings: BodyFlags.TwoSided gave one-sided collision (crossings went through the back face of the (A_top, B_top, B_bottom) normal); reversed triangles make every quad solid from both sides
                    int v = q * 4;
                    int o = q * IndicesPerQuad;
                    indices[o + 0] = v;
                    indices[o + 1] = v + 1;
                    indices[o + 2] = v + 2;
                    indices[o + 3] = v;
                    indices[o + 4] = v + 2;
                    indices[o + 5] = v + 3;
                    indices[o + 6] = v;
                    indices[o + 7] = v + 2;
                    indices[o + 8] = v + 1;
                    indices[o + 9] = v;
                    indices[o + 10] = v + 3;
                    indices[o + 11] = v + 2;
                }
                BoundingBox localBox = default(BoundingBox);
                localBox.BeginRelaxation();
                for (int vi = 0; vi < vertices.Length; vi++)
                {
                    localBox.RelaxMinMaxWithPoint(in vertices[vi]);
                }
                localBox.RecomputeRadius();

                WallPiece piece = new WallPiece();
                piece.Id = pieceId;
                piece.FirstSegment = firstSegment;
                piece.SegmentCount = count;
                piece.Centre = centre;
                piece.Origin = origin;
                piece.LocalBox = localBox;
                // kept pinned until the entity is removed, as NavalDLC does
                piece.Vertices = GCHandle.Alloc(vertices, GCHandleType.Pinned);
                piece.Indices = GCHandle.Alloc(indices, GCHandleType.Pinned);
                _wallEntities.Add(piece);
                _tileToPiece[key] = pieceId;

                MatrixFrame frame = MatrixFrame.Identity;
                frame.origin = origin;
                GameEntity entity = GameEntity.CreateEmpty(scene, false);
                piece.Entity = entity;
                entity.Name = "rbm_deepwater_wall_" + pieceId;
                entity.SetGlobalFrame(in frame);
                entity.ReplacePhysicsBodyWithQuadPhysicsBody(
                    (UIntPtr)(ulong)(long)piece.Vertices.AddrOfPinnedObject(), vertices.Length, material, flags,
                    (UIntPtr)(ulong)(long)piece.Indices.AddrOfPinnedObject(), indices.Length);
                entity.SetGlobalFrame(in frame);
                entity.SetVisibilityExcludeParents(true);
                entity.SetPhysicsState(true, false);
                try
                {
                    // a mesh-less entity's own bounds are a point at its origin: set the body's box by hand, as NavalDLC's ShipOarMachine / RopePileBaked do
                    entity.SetFrameChanged();
                    entity.SetManualLocalBoundingBox(in localBox);
                }
                catch (Exception e)
                {
                    ReportOnce("wallBounds", e);
                }
                if (entity.GetPhysicsState())
                {
                    physicsOnAfterBuild++;
                }
                if ((entity.BodyFlag & BodyFlags.Disabled) != 0)
                {
                    disabledFlagAfterBuild++;
                }
                int triangles = SafeTriangles(entity);
                trianglesTotal += Math.Max(0, triangles);
                Vec3 wantMin = origin + localBox.min;
                Vec3 wantMax = origin + localBox.max;
                Vec3 boxMin = entity.GlobalBoxMin;
                Vec3 boxMax = entity.GlobalBoxMax;
                if (!BoxCovers(boxMin, boxMax, wantMin, wantMax))
                {
                    // fallback: the global variant of the same native call
                    try
                    {
                        entity.SetManualGlobalBoundingBox(wantMin, wantMax);
                        _wallBoxViaGlobal++;
                    }
                    catch (Exception e)
                    {
                        ReportOnce("wallBoundsGlobal", e);
                    }
                    boxMin = entity.GlobalBoxMin;
                    boxMax = entity.GlobalBoxMax;
                }
                if (BoxCovers(boxMin, boxMax, wantMin, wantMax))
                {
                    bboxCoversWall++;
                }
                if (pieceId == 0)
                {
                    samplePiece = "#0 @" + Pos(centre) + " quads=" + count + " triangles=" + triangles
                        + " globalBox=" + Pos(boxMin.AsVec2) + "," + Fmt(boxMin.z) + " .. " + Pos(boxMax.AsVec2) + "," + Fmt(boxMax.z)
                        + " bodyFlags=0x" + ((uint)entity.BodyFlag).ToString("X") + " descFlags=0x" + ((uint)entity.PhysicsDescBodyFlag).ToString("X");
                }
                _wallQuads += count;
            }

            Log("WALL\tcontour\tpolylines=" + stats.Closed + " closed / " + stats.Open + " open (saddles: deep connected, "
                + stats.Saddles + " saddle squares)\tpoints raw=" + stats.RawPoints + " smoothed=" + stats.SmoothedPoints
                + " simplified=" + stats.SimplifiedPoints + "\tdroppedTinyLoops=" + stats.DroppedLoops
                + "\torientationFlipped=" + stats.Flipped
                + "\tsmoothing=" + WallLaplacianPasses + " Laplacian + " + WallChaikinPasses + " Chaikin, simplify " + Fmt(WallSimplifyTolerance)
                + "m\tbias toward deep=" + Fmt(WallDeepBias) + "m (+ up to " + Fmt(WallDryPushMax) + "m more where a vertex still sat in a dry cell: "
                + stats.PushedVertices + " pushed, " + stats.DryVerticesLeft + " left dry)"
                + "\tmaxQuadLength=" + Fmt(WallMaxQuadLength) + "m\tshortestQuad=" + Fmt(shortest == float.MaxValue ? 0f : shortest)
                + "m\tpieceJointOverlaps=" + stats.PieceJoints + " (" + Fmt(WallPieceSplitOverlap) + "m)"
                + "\tenclosureEdges=" + _encA.Count + "\tcontourMs=" + Fmt(stats.Ms));
            Log("WALL\tquads=" + _wallEdges + " per layer\tlayers=2 (gap " + Fmt(WallLayerGap) + "m toward deep)"
                + "\tsegments=" + _wallSegments.Count + "\tquads=" + _wallQuads + "\tpieces(entities)=" + _wallEntities.Count
                + " of " + Fmt(PieceTile) + "m tiles\tphysicsOnAfterBuild=" + physicsOnAfterBuild + "/" + _wallEntities.Count
                + "\tdisabledFlagAfterBuild=" + disabledFlagAfterBuild
                + "\ttriangles=" + trianglesTotal + " (expect " + (4 * _wallQuads) + ": both windings)"
                + "\tentityBoxCoversWall=" + bboxCoversWall + "/" + _wallEntities.Count
                + " (manual local box; global fallback used " + _wallBoxViaGlobal + ")"
                + "\tshoreLength=" + Fmt(totalLength) + "m\tmaxQuadHeight=" + Fmt(maxHeight) + "m"
                + "\tflags=Moveable|AgentOnly|TwoSided\tmaterial=" + (material.IsValid ? material.Name : "invalid")
                + "\tframe=at the piece, vertices local\tms=" + Fmt(ElapsedMs(startTicks)));
            Log("WALL\tsample " + samplePiece);
        }

        private sealed class WallContourStats
        {
            public int Closed;
            public int Open;
            public int Saddles;
            public int RawPoints;
            public int SmoothedPoints;
            public int SimplifiedPoints;
            public int DroppedLoops;
            public int Flipped;
            public int PushedVertices;
            public int DryVerticesLeft;
            public int PieceJoints;
            public float Ms;
        }

        // marching-squares segments per case (corner bits: 1 bl, 2 br, 4 tr, 8 tl) as pairs of square edges (0 bottom, 1 right, 2 top, 3 left); saddles 5 and 10 are resolved DEEP-CONNECTED, as the 4-connected dry flood fill (GridConnected) treats them
        private static readonly int[][] ContourCases =
        {
            new int[0], new[] { 3, 0 }, new[] { 0, 1 }, new[] { 3, 1 }, new[] { 1, 2 }, new[] { 0, 1, 2, 3 }, new[] { 0, 2 }, new[] { 2, 3 },
            new[] { 2, 3 }, new[] { 0, 2 }, new[] { 3, 0, 1, 2 }, new[] { 1, 2 }, new[] { 3, 1 }, new[] { 0, 1 }, new[] { 3, 0 }, new int[0]
        };

        /// <summary>The wall's two layers as polylines. Marching squares over the cell centres (outside the grid counts as not deep, so every curve closes), each piece oriented with deep water on its LEFT, chained by shared edge midpoints; then Laplacian (0.5 / 0.25 / 0.25) and Chaikin smoothing, Douglas-Peucker, and an offset along vertex normals toward deep water (further, in 0.1 m steps up to WallDryPushMax, where a vertex would still sit in a dry cell). The inner layer is the same curve WallLayerGap further in.</summary>
        private void BuildWallCurves(DeepWaterGrid g, WallContourStats stats, out List<List<Vec2>> outerLines,
            out List<List<Vec2>> innerLines, out List<bool> closedFlags)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            outerLines = new List<List<Vec2>>();
            innerLines = new List<List<Vec2>>();
            closedFlags = new List<bool>();
            int nx = g.Nx;
            int ny = g.Ny;
            float s = g.Step;

            Dictionary<long, long> next = new Dictionary<long, long>();
            HashSet<long> hasIncoming = new HashSet<long>();
            for (int j = -1; j < ny; j++)
            {
                for (int i = -1; i < nx; i++)
                {
                    int c = (CellDeep(g, i, j) ? 1 : 0) | (CellDeep(g, i + 1, j) ? 2 : 0)
                        | (CellDeep(g, i + 1, j + 1) ? 4 : 0) | (CellDeep(g, i, j + 1) ? 8 : 0);
                    int[] pairs = ContourCases[c];
                    if (pairs.Length == 0)
                    {
                        continue;
                    }
                    if (c == 5 || c == 10)
                    {
                        stats.Saddles++;
                    }
                    for (int p = 0; p < pairs.Length; p += 2)
                    {
                        int e1 = pairs[p];
                        int e2 = pairs[p + 1];
                        Vec2 p1 = EdgeMidpoint(g, i, j, e1);
                        Vec2 p2 = EdgeMidpoint(g, i, j, e2);
                        // orient so deep is on the left: test the corner the two edges share (or bl for opposite edges)
                        int ci, cj;
                        ReferenceCorner(i, j, e1, e2, out ci, out cj);
                        Vec2 corner = new Vec2(g.MinX + (ci + 0.5f) * s, g.MinY + (cj + 0.5f) * s);
                        Vec2 d = p2 - p1;
                        bool cornerLeft = d.x * (corner.y - p1.y) - d.y * (corner.x - p1.x) > 0f;
                        long k1 = EdgeKey(g, i, j, e1);
                        long k2 = EdgeKey(g, i, j, e2);
                        if (cornerLeft != CellDeep(g, ci, cj))
                        {
                            long t = k1;
                            k1 = k2;
                            k2 = t;
                        }
                        next[k1] = k2;
                        hasIncoming.Add(k2);
                    }
                }
            }

            // chain: open chains first (none expected, the padding closes all), then closed loops
            HashSet<long> used = new HashSet<long>();
            List<List<long>> chains = new List<List<long>>();
            List<bool> chainClosed = new List<bool>();
            foreach (long start in next.Keys)
            {
                if (hasIncoming.Contains(start))
                {
                    continue;
                }
                List<long> chain = new List<long>();
                long k = start;
                chain.Add(k);
                used.Add(k);
                long nk;
                while (next.TryGetValue(k, out nk) && !used.Contains(nk))
                {
                    chain.Add(nk);
                    used.Add(nk);
                    k = nk;
                }
                chains.Add(chain);
                chainClosed.Add(false);
            }
            foreach (long start in next.Keys)
            {
                if (used.Contains(start))
                {
                    continue;
                }
                List<long> chain = new List<long>();
                long k = start;
                while (!used.Contains(k))
                {
                    chain.Add(k);
                    used.Add(k);
                    long nk;
                    if (!next.TryGetValue(k, out nk))
                    {
                        break;
                    }
                    k = nk;
                }
                chains.Add(chain);
                chainClosed.Add(k == start);
            }

            for (int c = 0; c < chains.Count; c++)
            {
                bool closed = chainClosed[c];
                List<Vec2> pts = new List<Vec2>(chains[c].Count);
                foreach (long key in chains[c])
                {
                    pts.Add(KeyPoint(g, key));
                }
                if (pts.Count < (closed ? 3 : 2))
                {
                    stats.DroppedLoops++;
                    continue;
                }
                stats.RawPoints += pts.Count;

                // orientation check (deep on the left) by sampling: flip if most samples disagree
                int agree = 0, disagree = 0;
                int stride = Math.Max(1, pts.Count / 16);
                for (int k = 0; k + 1 < pts.Count; k += stride)
                {
                    Vec2 a = pts[k];
                    Vec2 b = pts[k + 1];
                    Vec2 d = b - a;
                    float len = d.Length;
                    if (len < 1e-4f)
                    {
                        continue;
                    }
                    Vec2 left = new Vec2(-d.y / len, d.x / len);
                    Vec2 m = (a + b) * 0.5f;
                    bool l = g.IsDeepAt(m.x + left.x * 0.4f * s, m.y + left.y * 0.4f * s);
                    bool r = g.IsDeepAt(m.x - left.x * 0.4f * s, m.y - left.y * 0.4f * s);
                    if (l && !r) agree++;
                    else if (r && !l) disagree++;
                }
                if (disagree > agree)
                {
                    pts.Reverse();
                    stats.Flipped++;
                }

                for (int pass = 0; pass < WallLaplacianPasses; pass++)
                {
                    pts = Laplacian(pts, closed);
                }
                for (int pass = 0; pass < WallChaikinPasses; pass++)
                {
                    pts = Chaikin(pts, closed);
                }
                stats.SmoothedPoints += pts.Count;
                pts = Simplify(pts, closed, WallSimplifyTolerance);
                if (pts.Count < (closed ? 3 : 2))
                {
                    stats.DroppedLoops++;
                    continue;
                }
                stats.SimplifiedPoints += pts.Count;

                List<Vec2> outer = new List<Vec2>(pts.Count);
                List<Vec2> inner = new List<Vec2>(pts.Count);
                int n = pts.Count;
                for (int k = 0; k < n; k++)
                {
                    Vec2 nrm = VertexLeftNormal(pts, k, closed);
                    Vec2 p = pts[k] + nrm * WallDeepBias;
                    if (!g.IsDeepAt(p.x, p.y))
                    {
                        float extra = 0f;
                        while (extra < WallDryPushMax - 1e-4f && !g.IsDeepAt(p.x, p.y))
                        {
                            extra += 0.1f;
                            p = pts[k] + nrm * (WallDeepBias + extra);
                        }
                        if (g.IsDeepAt(p.x, p.y))
                        {
                            stats.PushedVertices++;
                        }
                        else
                        {
                            stats.DryVerticesLeft++;
                            p = pts[k] + nrm * WallDeepBias;
                        }
                    }
                    outer.Add(p);
                    inner.Add(p + nrm * WallLayerGap);
                }
                outerLines.Add(outer);
                innerLines.Add(inner);
                closedFlags.Add(closed);
                if (closed)
                {
                    stats.Closed++;
                }
                else
                {
                    stats.Open++;
                }
            }
            stats.Ms = ElapsedMs(startTicks);
        }

        private static bool CellDeep(DeepWaterGrid g, int i, int j)
        {
            return i >= 0 && j >= 0 && i < g.Nx && j < g.Ny && g.Deep[j * g.Nx + i];
        }

        /// <summary>Edge e (0 bottom, 1 right, 2 top, 3 left) of the square whose bottom-left corner is cell (i, j).</summary>
        private static long EdgeKey(DeepWaterGrid g, int i, int j, int e)
        {
            // horizontal edge midpoints H(i, j) between cells (i, j) and (i+1, j); vertical V(i, j) between (i, j) and (i, j+1)
            switch (e)
            {
                case 0: return PointKey(g, i, j, false);
                case 1: return PointKey(g, i + 1, j, true);
                case 2: return PointKey(g, i, j + 1, false);
                default: return PointKey(g, i, j, true);
            }
        }

        private static long PointKey(DeepWaterGrid g, int i, int j, bool vertical)
        {
            return (((long)(i + 1)) * (g.Ny + 2) + (j + 1)) * 2 + (vertical ? 1 : 0);
        }

        private static Vec2 KeyPoint(DeepWaterGrid g, long key)
        {
            bool vertical = (key & 1) != 0;
            long rest = key >> 1;
            int i = (int)(rest / (g.Ny + 2)) - 1;
            int j = (int)(rest % (g.Ny + 2)) - 1;
            float s = g.Step;
            return vertical
                ? new Vec2(g.MinX + (i + 0.5f) * s, g.MinY + (j + 1) * s)
                : new Vec2(g.MinX + (i + 1) * s, g.MinY + (j + 0.5f) * s);
        }

        private static Vec2 EdgeMidpoint(DeepWaterGrid g, int i, int j, int e)
        {
            return KeyPoint(g, EdgeKey(g, i, j, e));
        }

        private static void ReferenceCorner(int i, int j, int e1, int e2, out int ci, out int cj)
        {
            int lo = Math.Min(e1, e2);
            int hi = Math.Max(e1, e2);
            if (lo == 0 && hi == 1) { ci = i + 1; cj = j; return; }          // bottom-right
            if (lo == 1 && hi == 2) { ci = i + 1; cj = j + 1; return; }      // top-right
            if (lo == 2 && hi == 3) { ci = i; cj = j + 1; return; }          // top-left
            ci = i; cj = j;                                                  // bottom-left (L-B, and the opposite pairs)
        }

        private static List<Vec2> Laplacian(List<Vec2> pts, bool closed)
        {
            int n = pts.Count;
            List<Vec2> result = new List<Vec2>(n);
            for (int k = 0; k < n; k++)
            {
                if (!closed && (k == 0 || k == n - 1))
                {
                    result.Add(pts[k]);
                    continue;
                }
                Vec2 prev = pts[(k - 1 + n) % n];
                Vec2 nxt = pts[(k + 1) % n];
                result.Add(pts[k] * 0.5f + (prev + nxt) * 0.25f);
            }
            return result;
        }

        private static List<Vec2> Chaikin(List<Vec2> pts, bool closed)
        {
            int n = pts.Count;
            List<Vec2> result = new List<Vec2>(n * 2);
            if (!closed)
            {
                result.Add(pts[0]);
            }
            int legs = closed ? n : n - 1;
            for (int k = 0; k < legs; k++)
            {
                Vec2 a = pts[k];
                Vec2 b = pts[(k + 1) % n];
                result.Add(a * 0.75f + b * 0.25f);
                result.Add(a * 0.25f + b * 0.75f);
            }
            if (!closed)
            {
                result.Add(pts[n - 1]);
            }
            return result;
        }

        /// <summary>Douglas-Peucker; a closed curve is split at its first point and the point farthest from it.</summary>
        private static List<Vec2> Simplify(List<Vec2> pts, bool closed, float tolerance)
        {
            int n = pts.Count;
            if (n < 4)
            {
                return pts;
            }
            bool[] keep = new bool[n];
            if (closed)
            {
                int far = 0;
                float best = -1f;
                for (int k = 1; k < n; k++)
                {
                    float d = pts[k].DistanceSquared(pts[0]);
                    if (d > best)
                    {
                        best = d;
                        far = k;
                    }
                }
                keep[0] = true;
                keep[far] = true;
                DouglasPeucker(pts, 0, far, tolerance, keep, n);
                DouglasPeucker(pts, far, n, tolerance, keep, n);
            }
            else
            {
                keep[0] = true;
                keep[n - 1] = true;
                DouglasPeucker(pts, 0, n - 1, tolerance, keep, n);
            }
            List<Vec2> result = new List<Vec2>();
            for (int k = 0; k < n; k++)
            {
                if (keep[k])
                {
                    result.Add(pts[k]);
                }
            }
            return result;
        }

        /// <summary>Keeps the points between first and last (index last may be n = wraps to 0) farther than tolerance.</summary>
        private static void DouglasPeucker(List<Vec2> pts, int first, int last, float tolerance, bool[] keep, int n)
        {
            // a List as the work stack (System.dll, home of Stack<T>, is not referenced by this project)
            List<int[]> work = new List<int[]>();
            work.Add(new[] { first, last });
            while (work.Count > 0)
            {
                int[] range = work[work.Count - 1];
                work.RemoveAt(work.Count - 1);
                int a = range[0];
                int b = range[1];
                if (b - a < 2)
                {
                    continue;
                }
                Vec2 pa = pts[a % n];
                Vec2 pb = pts[b % n];
                float best = -1f;
                int index = -1;
                for (int k = a + 1; k < b; k++)
                {
                    float d = PointSegmentDistance(pts[k % n], pa, pb);
                    if (d > best)
                    {
                        best = d;
                        index = k;
                    }
                }
                if (best > tolerance && index > 0)
                {
                    keep[index % n] = true;
                    work.Add(new[] { a, index });
                    work.Add(new[] { index, b });
                }
            }
        }

        private static float PointSegmentDistance(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            float lsq = ab.LengthSquared;
            float t = lsq > 0f ? Vec2.DotProduct(p - a, ab) / lsq : 0f;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return p.Distance(a + ab * t);
        }

        /// <summary>Unit normal toward the left (deep) side at vertex k: the mean of its two legs' left normals.</summary>
        private static Vec2 VertexLeftNormal(List<Vec2> pts, int k, bool closed)
        {
            int n = pts.Count;
            Vec2 sum = Vec2.Zero;
            if (closed || k > 0)
            {
                Vec2 d = pts[k] - pts[(k - 1 + n) % n];
                if (d.LengthSquared > 1e-8f)
                {
                    d = d.Normalized();
                    sum += new Vec2(-d.y, d.x);
                }
            }
            if (closed || k < n - 1)
            {
                Vec2 d = pts[(k + 1) % n] - pts[k];
                if (d.LengthSquared > 1e-8f)
                {
                    d = d.Normalized();
                    sum += new Vec2(-d.y, d.x);
                }
            }
            return sum.LengthSquared > 1e-8f ? sum.Normalized() : Vec2.Zero;
        }

        // enclosure: is a point on the deep side of the (closed) outer wall?

        private readonly List<Vec2> _encA = new List<Vec2>();
        private readonly List<Vec2> _encB = new List<Vec2>();
        private readonly Dictionary<int, List<int>> _encBands = new Dictionary<int, List<int>>();
        private const float EnclosureBand = 2f;

        private void AddEnclosureEdge(Vec2 a, Vec2 b)
        {
            int index = _encA.Count;
            _encA.Add(a);
            _encB.Add(b);
            int lo = (int)Math.Floor(Math.Min(a.y, b.y) / EnclosureBand);
            int hi = (int)Math.Floor(Math.Max(a.y, b.y) / EnclosureBand);
            for (int band = lo; band <= hi; band++)
            {
                List<int> list;
                if (!_encBands.TryGetValue(band, out list))
                {
                    list = new List<int>();
                    _encBands[band] = list;
                }
                list.Add(index);
            }
        }

        /// <summary>Even-odd test of a ray toward +x against the closed outer wall curves (main thread).</summary>
        private bool IsEnclosedByWall(Vec2 p)
        {
            List<int> list;
            if (!_encBands.TryGetValue((int)Math.Floor(p.y / EnclosureBand), out list))
            {
                return false;
            }
            bool inside = false;
            for (int k = 0; k < list.Count; k++)
            {
                Vec2 a = _encA[list[k]];
                Vec2 b = _encB[list[k]];
                if ((a.y > p.y) != (b.y > p.y))
                {
                    float x = a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y);
                    if (x > p.x)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        /// <summary>"Inside the wall": a deep cell AND, while the wall stands, enclosed by it. The wall is biased toward deep water, so parts of deep shore cells lie OUTSIDE it: men there never crossed a wall and are not forced out (main thread).</summary>
        private bool InsideWall(DeepWaterGrid grid, Vec2 p)
        {
            if (grid == null || !grid.IsDeepAt(p.x, p.y))
            {
                return false;
            }
            return _wallEntities.Count == 0 || _encA.Count == 0 || IsEnclosedByWall(p);
        }

        /// <summary>True when the box [min, max] covers [wantMin, wantMax] to within 5 cm.</summary>
        private static bool BoxCovers(Vec3 min, Vec3 max, Vec3 wantMin, Vec3 wantMax)
        {
            const float e = 0.05f;
            return min.x <= wantMin.x + e && min.y <= wantMin.y + e && min.z <= wantMin.z + e
                && max.x >= wantMax.x - e && max.y >= wantMax.y - e && max.z >= wantMax.z - e;
        }

        private int SafeTriangles(GameEntity entity)
        {
            try
            {
                return entity.GetPhysicsTriangleCount();
            }
            catch (Exception e)
            {
                ReportOnce("triangles", e);
                return -1;
            }
        }

        /// <summary>+1 when only the upper / right cell is deep, -1 when only the lower / left one is, 0 for no wall.</summary>
        private static int EdgeKind(bool lowerDeep, bool upperDeep)
        {
            if (upperDeep == lowerDeep)
            {
                return 0;
            }
            return upperDeep ? 1 : -1;
        }

        private static void SampleTerrain(Scene scene, Vec2 p, ref float low, ref float high, ref bool plausible)
        {
            float h = scene.GetTerrainHeight(p, true);
            if (!IsPlausible(h))
            {
                plausible = false;
                return;
            }
            if (h < low) low = h;
            if (h > high) high = h;
        }

        private static long TileKey(Vec2 p)
        {
            long tx = (long)Math.Floor(p.x / PieceTile);
            long ty = (long)Math.Floor(p.y / PieceTile);
            return (tx << 32) ^ (ty & 0xFFFFFFFFL);
        }

        private void RemoveWalls()
        {
            _tileToPiece.Clear();
            _openGates = 0;
            foreach (WallPiece wall in _wallEntities)
            {
                try
                {
                    if (wall.Entity != null)
                    {
                        wall.Entity.Remove(WallRemoveReason);
                    }
                }
                catch (Exception e)
                {
                    ReportOnce("removeWall", e);
                }
                if (wall.Vertices.IsAllocated)
                {
                    wall.Vertices.Free();
                }
                if (wall.Indices.IsAllocated)
                {
                    wall.Indices.Free();
                }
            }
            _wallEntities.Clear();
            _wallSegments.Clear();
            _wallQuads = 0;
            _encA.Clear();
            _encB.Clear();
            _encBands.Clear();
        }

        // forced exit + gates -- main thread (scan every ForcedScanInterval s, forced men every frame)

        /// <summary>Starts a forced exit for every AI man (never the player) in a deep cell who has none, up to MaxForcedAtOnce at once.</summary>
        private void ForcedExitScan(Mission mission, float now)
        {
            DeepWaterGrid grid = _grid;
            if (grid == null || _reverted)
            {
                return;
            }
            foreach (Agent agent in mission.Agents)
            {
                if (agent == null || !agent.IsHuman || !agent.IsActive() || agent.IsPlayerControlled || !agent.IsAIControlled)
                {
                    continue;
                }
                if (_forcedByAgent.ContainsKey(agent))
                {
                    continue;
                }
                Vec2 pos = agent.Position.AsVec2;
                if (!grid.IsDeepAt(pos.x, pos.y))
                {
                    continue;
                }
                if (!InsideWall(grid, pos))
                {
                    // a deep shore cell on the dry side of the (deep-biased) wall: he can walk off himself
                    _outsideWallDeepAgents.Add(agent.Index);
                    continue;
                }
                AgentWaterState state;
                if (!_states.TryGetValue(agent, out state))
                {
                    state = new AgentWaterState();
                    _states[agent] = state;
                }
                if (now < state.NoExitUntil)
                {
                    continue;
                }
                if (_forced.Count >= MaxForcedAtOnce)
                {
                    if (!state.CapRefusedCounted)
                    {
                        state.CapRefusedCounted = true;
                        _forcedRefusedCap++;
                    }
                    continue;
                }
                state.CapRefusedCounted = false;
                StartForcedExit(mission, agent, state, now);
            }
        }

        private void StartForcedExit(Mission mission, Agent agent, AgentWaterState state, float now)
        {
            ForcedExit f = new ForcedExit();
            f.Agent = agent;
            f.State = state;
            f.StartedAt = now;
            f.StartPos = agent.Position.AsVec2;
            f.StartSwimming = agent.IsInWater();
            string info;
            if (!PickExit(mission.Scene, agent, state.ExcludedPieces, f, out info))
            {
                _forcedNoExit++;
                state.NoExitUntil = now + NoExitRetrySeconds;
                if (!state.NoExitLogged)
                {
                    state.NoExitLogged = true;
                    ForcedLog("t=" + Fmt(now) + "\tFORCEDEXIT\tNOEXIT\t" + AgentId(agent) + "\t" + SideName(agent)
                        + "\t" + (agent.MountAgent != null ? "mounted" : "foot") + "\t" + (f.StartSwimming ? "swimming" : "wading")
                        + "\tpos=" + Pos(f.StartPos) + "\t" + info + "\tretry in " + Fmt(NoExitRetrySeconds) + "s");
                }
                return;
            }
            state.NoExitLogged = false;
            _forced.Add(f);
            _forcedByAgent[agent] = f;
            DeepWaterSlots.AddForcedExit(agent.Index);
            if (_forced.Count > _maxForcedAtOnce)
            {
                _maxForcedAtOnce = _forced.Count;
            }
            _forcedStarted++;
            if (_forcedAgentIndices.Add(agent.Index))
            {
                _forcedAgentsStarted++;
            }

            // a Frontline pin (Agent.SetTargetPosition) is a movement lock and would hold him in place
            bool unlocked = false;
            if (agent.MovementLockedState != AgentMovementLockedState.None)
            {
                agent.ClearTargetFrame();
                unlocked = true;
            }
            bool scripted = IssueScriptedMove(mission.Scene, f);
            ForcedLog("t=" + Fmt(now) + "\tFORCEDEXIT\tSTART\t" + AgentId(agent) + "\t" + SideName(agent)
                + "\t" + (agent.MountAgent != null ? "mounted" : "foot") + "\t" + (f.StartSwimming ? "swimming" : "wading")
                + "\tattempt=" + f.Attempt + "\tpos=" + Pos(f.StartPos) + "\texit=" + Pos(f.Exit.AsVec2)
                + "\tdistance=" + Fmt(f.StartPos.Distance(f.Exit.AsVec2)) + "m\tgate=" + PieceList(f.GatePieces)
                + "\tgatePoint=" + Pos(f.GatePoint) + "\t" + info
                + "\tscriptedGoTo=" + (scripted ? "1" : "0 (FLAG NOT SET?)") + (unlocked ? "\tclearedMovementLock" : "")
                + "\tslot=" + (SlotState(agent, _grid) ?? "-") + "\tforcedNow=" + _forced.Count);
        }

        /// <summary>SetScriptedPosition to his exit point; true when GoToPosition reads back set.</summary>
        private bool IssueScriptedMove(Scene scene, ForcedExit f)
        {
            WorldPosition target = new WorldPosition(scene, f.Exit);
            f.Agent.SetScriptedPosition(ref target, false, Agent.AIScriptedFrameFlags.None);
            f.NextFlagCheck = CurrentTime() + ForcedFlagRecheck;
            return (f.Agent.GetScriptedFlags() & Agent.AIScriptedFrameFlags.GoToPosition) != 0;
        }

        /// <summary>Every frame while anybody is forced or a gate is open: release men who are out, time out attempts, and open / close the gates exactly while their men need them.</summary>
        private void ForcedExitTick(Mission mission, float now)
        {
            DeepWaterGrid grid = _grid;
            Scene scene = mission.Scene;
            for (int k = 0; k < _tickPieces.Count; k++)
            {
                _tickPieces[k].WantedBy = null;
                _tickPieces[k].Doorway = false;
            }
            _tickPieces.Clear();
            foreach (WallPiece open in _wallEntities)
            {
                if (open.IsOpen)
                {
                    _tickPieces.Add(open);
                }
            }

            for (int k = _forced.Count - 1; k >= 0; k--)
            {
                ForcedExit f = _forced[k];
                Agent agent = f.Agent;
                if (agent == null || !agent.IsActive())
                {
                    _forcedRemoved++;
                    EndForcedExit(f, now, "REMOVED", "not active", false);
                    continue;
                }
                if (agent.IsPlayerControlled || !agent.IsAIControlled)
                {
                    EndForcedExit(f, now, "RELEASED", "now player controlled", true);
                    continue;
                }
                Vec2 pos = agent.Position.AsVec2;
                if (grid == null || !grid.IsBlockedAt(pos.x, pos.y))
                {
                    _forcedDone++;
                    EndWait(f, now);
                    if (f.EverThroughGate)
                    {
                        _forcedDoneThroughGate++;
                    }
                    else
                    {
                        _forcedDoneWithoutGate++;
                    }
                    _forcedSecondsTotal += now - f.StartedAt;
                    _forcedWaitedTotal += f.WaitedTotal;
                    if (f.FirstReachAt >= 0f)
                    {
                        _forcedToGateTotal += f.FirstReachAt - f.StartedAt;
                        _forcedToGateCount++;
                    }
                    if (f.FirstOpenAt >= 0f)
                    {
                        _forcedInGateTotal += now - f.FirstOpenAt;
                        _forcedInGateCount++;
                    }
                    f.State.ExcludedPieces.Clear();
                    EndForcedExit(f, now, "DONE", "in a non-blocked cell; toGate=" + (f.FirstReachAt >= 0f ? Fmt(f.FirstReachAt - f.StartedAt) + "s" : "never")
                        + " waited=" + Fmt(f.WaitedTotal) + "s firstOpenToOut=" + (f.FirstOpenAt >= 0f ? Fmt(now - f.FirstOpenAt) + "s" : "no gate opened")
                        + (f.EverThroughGate ? "" : " (OUT WITHOUT A GATE: through a closed piece?)"), true);
                    continue;
                }
                if (now - f.AttemptStartedAt > ForcedTimeoutSeconds || f.GateOpens >= GateOpensPerAttempt)
                {
                    _forcedFailed++;
                    Retry(scene, f, now, "FAILED", now - f.AttemptStartedAt > ForcedTimeoutSeconds
                        ? "timeout " + Fmt(ForcedTimeoutSeconds) + "s" : f.GateOpens + " gate openings");
                    continue;
                }
                if (now >= f.NextFlagCheck)
                {
                    f.NextFlagCheck = now + ForcedFlagRecheck;
                    if ((agent.GetScriptedFlags() & Agent.AIScriptedFrameFlags.GoToPosition) == 0)
                    {
                        // something dropped it (NavalDLC re-issues the same way): drive him again
                        _forcedReissued++;
                        IssueScriptedMove(scene, f);
                    }
                }

                // inside = a deep cell enclosed by the wall: once out through the gate he is outside even in a deep shore cell
                bool inDeep = InsideWall(grid, pos);
                float d = DistanceToPieces(f.GatePieces, pos);
                if (inDeep && d <= GateOpenReach && f.FirstReachAt < 0f)
                {
                    f.FirstReachAt = now;
                }
                for (int g = 0; g < f.GatePieces.Count; g++)
                {
                    WallPiece piece = _wallEntities[f.GatePieces[g]];
                    if (inDeep && d <= GateOpenReach)
                    {
                        if (piece.WantedBy == null)
                        {
                            piece.WantedBy = f;
                        }
                    }
                    else if (!inDeep && d < GateOpenReach)
                    {
                        piece.Doorway = true;
                    }
                    else
                    {
                        continue;
                    }
                    if (!_tickPieces.Contains(piece))
                    {
                        _tickPieces.Add(piece);
                    }
                }
                if (!inDeep && d >= GateOpenReach)
                {
                    EndWait(f, now);
                }
            }

            for (int k = 0; k < _tickPieces.Count; k++)
            {
                WallPiece piece = _tickPieces[k];
                if (piece.IsOpen)
                {
                    if (now - piece.OpenedAt >= GateMaxOpenSeconds)
                    {
                        _gatesClosedMax++;
                        ClosePiece(piece, now, "max " + Fmt(GateMaxOpenSeconds) + "s");
                    }
                    else if (piece.WantedBy == null && !piece.Doorway)
                    {
                        _gatesClosedPast++;
                        ClosePiece(piece, now, "past");
                    }
                    continue;
                }
                ForcedExit f = piece.WantedBy;
                if (f == null || now - piece.ClosedAt < GateReopenCooldown)
                {
                    continue;
                }
                if (_openGates >= MaxOpenGates)
                {
                    _gatesRefusedCap++;
                    continue;
                }
                string outsider;
                if (OutsiderNear(mission, piece, out outsider))
                {
                    float waitedNow = f.WaitingSince >= 0f ? now - f.WaitingSince : 0f;
                    if (f.WaitedTotal + waitedNow >= GateOutsiderGiveUpSeconds)
                    {
                        // waited long enough in all: open anyway (GateOutsiderGiveUpSeconds)
                        _gatesOpenedDespiteOutsider++;
                        if (f.WaitingSince < 0f)
                        {
                            f.WaitingSince = now;
                        }
                        f.WaitingFor = outsider;
                        OpenPiece(piece, now, f);
                        EndWait(f, now);
                        continue;
                    }
                    if (f.WaitingSince < 0f)
                    {
                        f.WaitingSince = now;
                        f.WaitingFor = outsider;
                        _gatesWaitedForOutsider++;
                        GateLog("t=" + Fmt(now) + "\tGATE\tWAIT\tpiece #" + piece.Id + " @" + Pos(piece.Centre) + "\tfor " + AgentId(f.Agent)
                            + "\toutside: " + outsider + "\twaitedSoFar=" + Fmt(f.WaitedTotal) + "s");
                    }
                    else if (waitedNow > GateOutsiderWaitSeconds)
                    {
                        _forcedRerouted++;
                        f.State.ExcludedPieces.Add(piece.Id);
                        string waitedFor = f.WaitingFor;
                        EndWait(f, now);
                        Retry(scene, f, now, "REROUTE", "waited " + Fmt(waitedNow) + "s at piece #" + piece.Id
                            + " for " + waitedFor + " (" + Fmt(f.WaitedTotal) + "s in all)");
                    }
                    continue;
                }
                EndWait(f, now);
                OpenPiece(piece, now, f);
            }
        }

        private static void EndWait(ForcedExit f, float now)
        {
            if (f.WaitingSince >= 0f)
            {
                f.WaitedTotal += Math.Max(0f, now - f.WaitingSince);
            }
            f.WaitingSince = -1f;
            f.WaitingFor = null;
        }

        /// <summary>Gives up the current attempt and starts another through other pieces (the failed gate is excluded).</summary>
        private void Retry(Scene scene, ForcedExit f, float now, string kind, string reason)
        {
            Agent agent = f.Agent;
            foreach (int id in f.GatePieces)
            {
                f.State.ExcludedPieces.Add(id);
            }
            Vec2 pos = agent.Position.AsVec2;
            string oldGate = PieceList(f.GatePieces);
            float attemptSeconds = now - f.AttemptStartedAt;
            string info;
            bool picked = PickExit(scene, agent, f.State.ExcludedPieces, f, out info);
            if (!picked && f.State.ExcludedPieces.Count > 0)
            {
                // every gate in reach failed once: start over with all of them
                f.State.ExcludedPieces.Clear();
                picked = PickExit(scene, agent, f.State.ExcludedPieces, f, out info);
            }
            ForcedLog("t=" + Fmt(now) + "\tFORCEDEXIT\t" + kind + "\t" + AgentId(agent) + "\t" + SideName(agent)
                + "\t" + (agent.MountAgent != null ? "mounted" : "foot") + "\t" + (agent.IsInWater() ? "swimming" : "wading")
                + "\treason=" + reason + "\tpos=" + Pos(pos) + "\tgate was " + oldGate + "\tattemptSeconds=" + Fmt(attemptSeconds)
                + "\tsince start " + Fmt(now - f.StartedAt) + "s"
                + (picked ? "\tnext attempt=" + f.Attempt + " exit=" + Pos(f.Exit.AsVec2) + " gate=" + PieceList(f.GatePieces) + "\t" + info
                          : "\tno other exit: " + info));
            if (!picked)
            {
                f.State.NoExitUntil = now + NoExitRetrySeconds;
                EndForcedExit(f, now, "GAVEUP", "no exit found", true);
                return;
            }
            EndWait(f, now);
            IssueScriptedMove(scene, f);
        }

        /// <summary>Stops tracking a forced man; with <paramref name="release"/> his scripted move is switched off.</summary>
        private void EndForcedExit(ForcedExit f, float now, string kind, string reason, bool release)
        {
            Agent agent = f.Agent;
            if (release && agent != null && agent.State == AgentState.Active)
            {
                try
                {
                    agent.DisableScriptedMovement();
                }
                catch (Exception e)
                {
                    ReportOnce("disableScripted", e);
                }
            }
            _forced.Remove(f);
            if (agent != null)
            {
                _forcedByAgent.Remove(agent);
                DeepWaterSlots.RemoveForcedExit(agent.Index);
            }
            Vec2 pos = Vec2.Zero;
            bool swimming = false;
            try
            {
                if (agent != null && agent.State == AgentState.Active)
                {
                    pos = agent.Position.AsVec2;
                    swimming = agent.IsInWater();
                }
            }
            catch
            {
            }
            if (kind == "RELEASED" || kind == "GAVEUP" || kind == "DONE" || kind == "REMOVED")
            {
                ForcedLog("t=" + Fmt(now) + "\tFORCEDEXIT\t" + kind + "\t" + AgentId(agent) + "\t" + SideName(agent)
                    + "\treason=" + reason + "\tpos=" + Pos(pos) + (swimming ? "\tswimming" : "")
                    + "\tseconds=" + Fmt(now - f.StartedAt) + "\tattempts=" + (f.Attempt + 1) + "\tgateOpens(last attempt)=" + f.GateOpens
                    + "\tthroughGate=" + (f.EverThroughGate ? "1" : "0") + "\tstartedSwimming=" + (f.StartSwimming ? "1" : "0")
                    + "\tfrom=" + Pos(f.StartPos) + "\tforcedNow=" + _forced.Count);
            }
        }

        private void ReleaseAllForced(string reason)
        {
            float now = CurrentTime();
            for (int k = _forced.Count - 1; k >= 0; k--)
            {
                EndForcedExit(_forced[k], now, "RELEASED", reason, true);
            }
            foreach (WallPiece piece in _wallEntities)
            {
                if (piece.IsOpen)
                {
                    ClosePiece(piece, now, reason);
                }
            }
            DeepWaterSlots.ClearForcedExits();
        }

        /// <summary>Finds his exit: a BFS from his cell through blocked cells collects the nearest non-blocked cells; those with an enabled navmesh face are compared by distance (+ BusyGatePenalty when a man is close outside the gate), one per distinct gate, skipping gates that failed for him. Fills f.Exit / GatePieces / GatePoint and bumps f.Attempt. Main thread.</summary>
        private bool PickExit(Scene scene, Agent agent, HashSet<int> excluded, ForcedExit f, out string info)
        {
            info = "";
            DeepWaterGrid g = _grid;
            if (g == null)
            {
                info = "no grid";
                return false;
            }
            Vec2 pos = agent.Position.AsVec2;
            int i0, j0;
            if (!g.TryGetCell(pos.x, pos.y, out i0, out j0))
            {
                info = "outside the grid";
                return false;
            }
            int cellCount = g.CellCount;
            if (_bfsQueue == null || _bfsQueue.Length != cellCount)
            {
                _bfsQueue = new int[cellCount];
                _bfsStamp = new int[cellCount];
                _bfsStampValue = 0;
            }
            _bfsStampValue++;
            if (_bfsStampValue == int.MaxValue)
            {
                Array.Clear(_bfsStamp, 0, _bfsStamp.Length);
                _bfsStampValue = 1;
            }
            int stamp = _bfsStampValue;
            int radiusCells = (int)Math.Ceiling(ExitSearchRadius / g.Step);
            int head = 0;
            int tail = 0;
            int start = j0 * g.Nx + i0;
            _bfsQueue[tail++] = start;
            _bfsStamp[start] = stamp;
            List<int> targets = new List<int>();
            int visited = 0;
            while (head < tail && targets.Count < ExitCandidateCells && visited < ExitSearchMaxCells)
            {
                int k = _bfsQueue[head++];
                visited++;
                int ci = k % g.Nx;
                int cj = k / g.Nx;
                for (int dj = -1; dj <= 1; dj++)
                {
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0)
                        {
                            continue;
                        }
                        int ni = ci + di;
                        int nj = cj + dj;
                        if (ni < 0 || nj < 0 || ni >= g.Nx || nj >= g.Ny
                            || Math.Abs(ni - i0) > radiusCells || Math.Abs(nj - j0) > radiusCells)
                        {
                            continue;
                        }
                        int nk = nj * g.Nx + ni;
                        if (_bfsStamp[nk] == stamp)
                        {
                            continue;
                        }
                        _bfsStamp[nk] = stamp;
                        if (g.Blocked[nk])
                        {
                            _bfsQueue[tail++] = nk;
                        }
                        else
                        {
                            targets.Add(nk);
                        }
                    }
                }
            }
            if (targets.Count == 0)
            {
                info = "no dry cell within " + Fmt(ExitSearchRadius) + "m (" + visited + " cells searched)";
                return false;
            }
            targets.Sort((a, b) => CellCentre(g, a).DistanceSquared(pos).CompareTo(CellCentre(g, b).DistanceSquared(pos)));

            int gatesCompared = 0;
            int noNav = 0;
            int skippedExcluded = 0;
            int busyGates = 0;
            float bestScore = float.MaxValue;
            Vec3 bestExit = Vec3.Zero;
            Vec2 bestGatePoint = Vec2.Zero;
            List<int> bestPieces = null;
            HashSet<string> seenGates = new HashSet<string>();
            foreach (int cell in targets)
            {
                if (gatesCompared >= ExitCandidateGates)
                {
                    break;
                }
                Vec2 c = CellCentre(g, cell);
                float ground = scene.GetTerrainHeight(c, true);
                if (!IsPlausible(ground))
                {
                    noNav++;
                    continue;
                }
                PathFaceRecord record = PathFaceRecord.NullFaceRecord;
                scene.GetNavMeshFaceIndex(ref record, new Vec3(c.x, c.y, ground + 0.3f, -1f), true);
                if (!record.IsValid())
                {
                    noNav++;
                    continue;
                }
                List<int> pieces = new List<int>();
                Vec2 gatePoint;
                GatePiecesFor(pos, c, pieces, out gatePoint);
                if (pieces.Count == 0)
                {
                    continue;
                }
                pieces.Sort();
                string key = PieceList(pieces);
                if (!seenGates.Add(key))
                {
                    continue;
                }
                bool skip = false;
                foreach (int id in pieces)
                {
                    if (excluded.Contains(id))
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip)
                {
                    skippedExcluded++;
                    continue;
                }
                gatesCompared++;
                float score = pos.Distance(c);
                string unused;
                bool busy = false;
                foreach (int id in pieces)
                {
                    if (OutsiderNear(Mission, _wallEntities[id], out unused))
                    {
                        busy = true;
                        break;
                    }
                }
                if (busy)
                {
                    busyGates++;
                    score += BusyGatePenalty;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    bestExit = new Vec3(c.x, c.y, ground, -1f);
                    bestGatePoint = gatePoint;
                    bestPieces = pieces;
                }
            }
            info = "candidates=" + targets.Count + " gatesCompared=" + gatesCompared + " busyGates=" + busyGates
                + " excludedSkipped=" + skippedExcluded + " noNavmesh=" + noNav + " searched=" + visited;
            if (bestPieces == null)
            {
                return false;
            }
            f.Exit = bestExit;
            f.GatePoint = bestGatePoint;
            f.GatePieces.Clear();
            f.GatePieces.AddRange(bestPieces);
            f.GateOpens = 0;
            f.Attempt = f.HasAttempt ? f.Attempt + 1 : 0;
            f.HasAttempt = true;
            f.AttemptStartedAt = CurrentTime();
            return true;
        }

        private static Vec2 CellCentre(DeepWaterGrid g, int k)
        {
            return new Vec2(g.MinX + (k % g.Nx + 0.5f) * g.Step, g.MinY + (k / g.Nx + 0.5f) * g.Step);
        }

        /// <summary>The pieces crossed by the last GateApproachSpan m of the line from `from` to the exit point (both layers, and a seam neighbour when it passes the overlap), or, when it crosses no wall, the piece nearest the exit point. gatePoint = where the line meets the wall.</summary>
        private void GatePiecesFor(Vec2 from, Vec2 exit, List<int> pieces, out Vec2 gatePoint)
        {
            pieces.Clear();
            gatePoint = exit;
            Vec2 toFrom = from - exit;
            float length = toFrom.Length;
            Vec2 approachStart = length > GateApproachSpan ? exit + toFrom * (GateApproachSpan / length) : from;
            float bestT = float.MaxValue;
            long tx = (long)Math.Floor(exit.x / PieceTile);
            long ty = (long)Math.Floor(exit.y / PieceTile);
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    int pieceId;
                    if (!_tileToPiece.TryGetValue(((tx + dx) << 32) ^ ((ty + dy) & 0xFFFFFFFFL), out pieceId))
                    {
                        continue;
                    }
                    WallPiece piece = _wallEntities[pieceId];
                    for (int k = piece.FirstSegment; k < piece.FirstSegment + piece.SegmentCount; k++)
                    {
                        float t;
                        if (!_wallSegments[k].Intersect(approachStart, exit, out t))
                        {
                            continue;
                        }
                        if (!pieces.Contains(pieceId))
                        {
                            pieces.Add(pieceId);
                        }
                        if (t < bestT)
                        {
                            bestT = t;
                            gatePoint = approachStart + (exit - approachStart) * t;
                        }
                    }
                }
            }
            if (pieces.Count > 0)
            {
                return;
            }
            // no crossing (a diagonal past a corner): the piece whose wall is nearest the exit point
            float bestD = float.MaxValue;
            int best = -1;
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    int pieceId;
                    if (!_tileToPiece.TryGetValue(((tx + dx) << 32) ^ ((ty + dy) & 0xFFFFFFFFL), out pieceId))
                    {
                        continue;
                    }
                    WallPiece piece = _wallEntities[pieceId];
                    for (int k = piece.FirstSegment; k < piece.FirstSegment + piece.SegmentCount; k++)
                    {
                        Vec2 closest;
                        float d = _wallSegments[k].DistanceTo(exit, out closest);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = pieceId;
                            gatePoint = closest;
                        }
                    }
                }
            }
            if (best >= 0 && bestD <= GateApproachSpan)
            {
                pieces.Add(best);
            }
        }

        private float DistanceToPieces(List<int> pieceIds, Vec2 p)
        {
            float best = float.MaxValue;
            for (int g = 0; g < pieceIds.Count; g++)
            {
                WallPiece piece = _wallEntities[pieceIds[g]];
                for (int k = piece.FirstSegment; k < piece.FirstSegment + piece.SegmentCount; k++)
                {
                    Vec2 closest;
                    float d = _wallSegments[k].DistanceTo(p, out closest);
                    if (d < best)
                    {
                        best = d;
                    }
                }
            }
            return best;
        }

        /// <summary>True when a man OUTSIDE (any human, the player too, not forced, not in a deep cell) is within GateOutsiderRadius of the piece's wall and moving toward it, or leaning on it (GateLeaningDistance): he would walk in through the gap. Riders count for their horse.</summary>
        private bool OutsiderNear(Mission mission, WallPiece piece, out string who)
        {
            who = null;
            DeepWaterGrid grid = _grid;
            if (mission == null || grid == null)
            {
                return false;
            }
            float reach = PieceTile * 0.75f + GateOutsiderRadius;
            mission.GetNearbyAgents(piece.Centre, reach, _scratchNearby);
            bool found = false;
            foreach (Agent other in _scratchNearby)
            {
                if (other == null || !other.IsHuman || !other.IsActive() || _forcedByAgent.ContainsKey(other))
                {
                    continue;
                }
                Vec2 p = other.Position.AsVec2;
                if (grid.IsDeepAt(p.x, p.y))
                {
                    continue;
                }
                float best = float.MaxValue;
                Vec2 bestClosest = p;
                for (int k = piece.FirstSegment; k < piece.FirstSegment + piece.SegmentCount; k++)
                {
                    Vec2 closest;
                    float d = _wallSegments[k].DistanceTo(p, out closest);
                    if (d < best)
                    {
                        best = d;
                        bestClosest = closest;
                    }
                }
                if (best > GateOutsiderRadius)
                {
                    continue;
                }
                bool leaning = best <= GateLeaningDistance;
                Vec2 v = other.Velocity.AsVec2;
                bool approaching = v.LengthSquared > GateOutsiderMinSpeed * GateOutsiderMinSpeed
                    && Vec2.DotProduct(v, bestClosest - p) > 0f;
                if (leaning || approaching)
                {
                    who = AgentId(other) + (other.IsPlayerControlled ? " PLAYER" : "") + " " + Fmt(best) + "m "
                        + (leaning ? "leaning" : "approaching " + Fmt(v.Length) + "m/s");
                    found = true;
                    break;
                }
            }
            _scratchNearby.Clear();
            return found;
        }

        private static string PieceList(List<int> pieces)
        {
            if (pieces == null || pieces.Count == 0)
            {
                return "-";
            }
            StringBuilder sb = new StringBuilder();
            for (int k = 0; k < pieces.Count; k++)
            {
                if (k > 0)
                {
                    sb.Append('+');
                }
                sb.Append('#').Append(pieces[k]);
            }
            return sb.ToString();
        }

        private void ForcedLog(string line)
        {
            if (_forcedLines >= MaxForcedLines)
            {
                return;
            }
            _forcedLines++;
            Log(line);
        }

        /// <summary>The piece whose wall the move p0 -> p1 crossed, or -1.</summary>
        private int FindCrossedPiece(Vec2 p0, Vec2 p1)
        {
            long tx = (long)Math.Floor(p1.x / PieceTile);
            long ty = (long)Math.Floor(p1.y / PieceTile);
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    int pieceId;
                    if (!_tileToPiece.TryGetValue(((tx + dx) << 32) ^ ((ty + dy) & 0xFFFFFFFFL), out pieceId))
                    {
                        continue;
                    }
                    WallPiece piece = _wallEntities[pieceId];
                    for (int k = piece.FirstSegment; k < piece.FirstSegment + piece.SegmentCount; k++)
                    {
                        if (_wallSegments[k].IsCrossedBy(p0, p1))
                        {
                            return pieceId;
                        }
                    }
                }
            }
            return -1;
        }

        private void OpenPiece(WallPiece piece, float now, ForcedExit f)
        {
            Agent agent = f.Agent;
            bool collidesAfter = true;
            try
            {
                // the native toggle for an agent-blocking collider: CastleGate's door colliders and NavalDLC's plank bridge set / clear BodyFlags.Disabled (SetBodyFlags)
                piece.Entity.SetBodyFlags(piece.Entity.BodyFlag | BodyFlags.Disabled);
                collidesAfter = (piece.Entity.BodyFlag & BodyFlags.Disabled) == 0;
            }
            catch (Exception e)
            {
                ReportOnce("openGate", e);
                return;
            }
            piece.IsOpen = true;
            piece.OpenedAt = now;
            piece.OpenedBy = AgentId(agent);
            piece.TimesOpened++;
            f.GateOpens++;
            f.EverThroughGate = true;
            if (f.FirstOpenAt < 0f)
            {
                f.FirstOpenAt = now;
            }
            bool physicsAfter = collidesAfter;
            _openGates++;
            _gatesOpened++;
            if (_openGates > _maxOpenGates)
            {
                _maxOpenGates = _openGates;
            }
            GateLog("t=" + Fmt(now) + "\tGATE\tOPEN\tpiece #" + piece.Id + " @" + Pos(piece.Centre) + " (" + piece.SegmentCount + " quads)"
                + "\tfor " + piece.OpenedBy + "\t" + SideName(agent) + "\t" + (agent.MountAgent != null ? "mounted" : "foot")
                + (agent.IsInWater() ? "\tswimming" : "") + "\twallDistance=" + Fmt(DistanceToPieces(f.GatePieces, agent.Position.AsVec2))
                + "m\treason=forced exit, attempt " + f.Attempt + ", opening " + f.GateOpens + "/" + GateOpensPerAttempt
                + (f.WaitingFor != null && f.WaitingSince >= 0f ? ", OPENED DESPITE " + f.WaitingFor : ", nobody close outside")
                + "\tcollidesAfter=" + (physicsAfter ? "1 (STILL ON?)" : "0") + "\tbodyFlags=0x" + ((uint)piece.Entity.BodyFlag).ToString("X")
                + "\topenNow=" + _openGates);
        }

        private void ClosePiece(WallPiece piece, float now, string reason)
        {
            bool collidesAfter = false;
            bool physicsState = false;
            try
            {
                // clear Disabled, keep the physics state on, and mark the frame changed as native does after re-enabling a collider
                piece.Entity.SetBodyFlags((BodyFlags)((uint)piece.Entity.BodyFlag & 0xFFFFFFFEu));
                if (!piece.Entity.GetPhysicsState())
                {
                    piece.Entity.SetPhysicsState(true, false);
                }
                piece.Entity.SetFrameChanged();
                // keep the hand-set bounds (a mesh-less entity's own are a point)
                piece.Entity.SetManualLocalBoundingBox(in piece.LocalBox);
                collidesAfter = (piece.Entity.BodyFlag & BodyFlags.Disabled) == 0;
                physicsState = piece.Entity.GetPhysicsState();
            }
            catch (Exception e)
            {
                ReportOnce("closeGate", e);
            }
            piece.IsOpen = false;
            piece.ClosedAt = now;
            _openGates--;
            GateLog("t=" + Fmt(now) + "\tGATE\tCLOSE\tpiece #" + piece.Id + " @" + Pos(piece.Centre)
                + "\topen " + Fmt(now - piece.OpenedAt) + "s\topenedFor=" + piece.OpenedBy + "\treason=" + reason
                + "\tcollidesAfter=" + (collidesAfter ? "1" : "0 (STILL OFF?)") + "\tphysicsState=" + (physicsState ? "1" : "0")
                + "\tbodyFlags=0x" + ((uint)piece.Entity.BodyFlag).ToString("X") + "\topenNow=" + _openGates);
        }

        private void GateLog(string line)
        {
            if (_gateLines >= MaxGateLines)
            {
                return;
            }
            _gateLines++;
            Log(line);
        }

        // slot condition (WORKER THREADS)

        /// <summary>Mission.IsFormationUnitPositionAvailable_AdditionalCondition. Runs on worker threads (IsFormationUnitPositionAvailableMT) and the main thread (IsOrderPositionAvailable): reads only the volatile immutable grid and the live flag; no engine call, no MBRandom, no allocation.</summary>
        private bool SlotCondition(WorldPosition position, Team team)
        {
            if (!_slotConditionLive)
            {
                return true;
            }
            DeepWaterGrid grid = _grid;
            if (grid == null)
            {
                return true;
            }
            Vec2 p = position.AsVec2;
            if (!grid.IsBlockedAt(p.x, p.y))
            {
                return true;
            }
            Interlocked.Increment(ref _slotRejections);
            return false;
        }

        private void UnsubscribeSlotCondition()
        {
            _slotConditionLive = false;
            DeepWaterSlots.LiveGrid = null;
            if (_subscribedMission != null && _slotCondition != null)
            {
                try
                {
                    _subscribedMission.IsFormationUnitPositionAvailable_AdditionalCondition -= _slotCondition;
                }
                catch (Exception e)
                {
                    ReportOnce("unsubscribe", e);
                }
            }
            _subscribedMission = null;
            _slotCondition = null;
        }

        /// <summary>Which navmesh face each grid cell near deep water lies on: cells within FaceShareRadius (Chebyshev) of a deep cell are sampled (every cell, or every 2nd/3rd when over FaceShareMaxSamples) with GetTerrainHeight + GetNavMeshFaceIndex (terrain + 0.3 m, checkIfDisabled false: the face whatever its state). total[f] / deep[f] count samples per face. Main thread, EarlyStart.</summary>
        private void ComputeFaceDeepShares(Scene scene, DeepWaterGrid g, int faceCount, out int[] total, out int[] deep)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            total = new int[Math.Max(0, faceCount)];
            deep = new int[total.Length];
            int nx = g.Nx;
            int ny = g.Ny;
            int r = Math.Max(1, (int)Math.Ceiling(FaceShareRadius / g.Step));

            // row pass then column pass of a "deep within r cells" test
            bool[] rowNear = new bool[nx * ny];
            for (int j = 0; j < ny; j++)
            {
                int last = int.MinValue / 2;
                for (int i = 0; i < nx; i++)
                {
                    if (g.Deep[j * nx + i]) last = i;
                    if (i - last <= r) rowNear[j * nx + i] = true;
                }
                int next = int.MaxValue / 2;
                for (int i = nx - 1; i >= 0; i--)
                {
                    if (g.Deep[j * nx + i]) next = i;
                    if (next - i <= r) rowNear[j * nx + i] = true;
                }
            }
            bool[] near = new bool[nx * ny];
            int nearCount = 0;
            for (int i = 0; i < nx; i++)
            {
                int last = int.MinValue / 2;
                for (int j = 0; j < ny; j++)
                {
                    if (rowNear[j * nx + i]) last = j;
                    if (j - last <= r) near[j * nx + i] = true;
                }
                int next = int.MaxValue / 2;
                for (int j = ny - 1; j >= 0; j--)
                {
                    if (rowNear[j * nx + i]) next = j;
                    if (next - j <= r) near[j * nx + i] = true;
                }
            }
            for (int k = 0; k < near.Length; k++)
            {
                if (near[k]) nearCount++;
            }
            int stride = 1;
            while ((long)nearCount / ((long)stride * stride) > FaceShareMaxSamples)
            {
                stride++;
            }

            int samples = 0;
            int calls = 0;
            int noFace = 0;
            for (int j = 0; j < ny; j += stride)
            {
                for (int i = 0; i < nx; i += stride)
                {
                    int k = j * nx + i;
                    if (!near[k])
                    {
                        continue;
                    }
                    Vec2 c = new Vec2(g.MinX + (i + 0.5f) * g.Step, g.MinY + (j + 0.5f) * g.Step);
                    float ground = scene.GetTerrainHeight(c, true);
                    calls++;
                    if (!IsPlausible(ground))
                    {
                        continue;
                    }
                    PathFaceRecord record = PathFaceRecord.NullFaceRecord;
                    scene.GetNavMeshFaceIndex(ref record, new Vec3(c.x, c.y, ground + 0.3f, -1f), false);
                    calls++;
                    samples++;
                    int face = record.FaceIndex;
                    if (!record.IsValid() || face < 0 || face >= total.Length)
                    {
                        noFace++;
                        continue;
                    }
                    total[face]++;
                    if (g.Deep[k])
                    {
                        deep[face]++;
                    }
                }
            }
            Log("BLOCK\tfaceShare\tradius=" + Fmt(FaceShareRadius) + "m\tcellsNearDeep=" + nearCount + "\tstride=" + stride
                + "\tsamples=" + samples + "\tnoFace=" + noFace + "\tengineCalls=" + calls + "\tms=" + Fmt(ElapsedMs(startTicks)));
        }

        private static readonly float[] ShareEdges = { 0.1f, 0.25f, 0.5f, 0.75f };
        private static readonly string[] ShareLabels = { "0-0.1", "0.1-0.25", "0.25-0.5", "0.5-0.75", "0.75-1" };

        private void DisableFaces(Scene scene, Vec3[] centres, float[] levels, DeepWaterGrid grid, int[] shareTotal, int[] shareDeep)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            List<int> candidates = new List<int>();
            int byCentre = 0;
            int byGrid = 0;
            int byShareOnly = 0;
            int byShare = 0;
            int[] shareHistogram = new int[ShareLabels.Length];
            int facesWithDeep = 0;
            for (int i = 0; i < centres.Length; i++)
            {
                bool centreDeep = IsPlausible(levels[i]) && levels[i] - centres[i].z >= BlockDepth;
                bool gridDeep = grid != null && grid.IsDeepAt(centres[i].x, centres[i].y);
                bool byShareRule = false;
                if (shareTotal != null && shareDeep != null && i < shareTotal.Length && shareTotal[i] > 0)
                {
                    float share = shareDeep[i] / (float)shareTotal[i];
                    if (shareDeep[i] > 0)
                    {
                        facesWithDeep++;
                        int b = 0;
                        while (b < ShareEdges.Length && share >= ShareEdges[b])
                        {
                            b++;
                        }
                        shareHistogram[b]++;
                    }
                    byShareRule = shareDeep[i] > 0 && share >= FaceDeepShareBlock;
                }
                if (centreDeep || gridDeep || byShareRule)
                {
                    candidates.Add(i);
                    if (centreDeep) byCentre++;
                    if (gridDeep) byGrid++;
                    if (byShareRule) byShare++;
                    if (byShareRule && !centreDeep && !gridDeep) byShareOnly++;
                }
            }
            _facesByDeepShare = byShareOnly;
            if (shareTotal != null)
            {
                StringBuilder hist = new StringBuilder();
                hist.Append("BLOCK\tdeepShare histogram(faces with any deep sample: ").Append(facesWithDeep).Append(')');
                for (int b = 0; b < shareHistogram.Length; b++)
                {
                    hist.Append('\t').Append(ShareLabels[b]).Append('=').Append(shareHistogram[b]);
                }
                hist.Append("\tblock at >=").Append(Fmt(FaceDeepShareBlock)).Append(": ").Append(byShare)
                    .Append(" (of which only by share: ").Append(byShareOnly).Append(')');
                Log(hist.ToString());
            }

            int islandsBefore = CountIslands(scene, centres.Length, null);
            if (candidates.Count == 0)
            {
                Log("BLOCK\tresult=NONE\tno face to switch off\tislands=" + islandsBefore);
                return;
            }
            if (centres.Length > 0 && candidates.Count > MaxDisabledShare * centres.Length)
            {
                Log("BLOCK\tresult=SKIPPED\twould disable " + candidates.Count + " of " + centres.Length + " faces, above the "
                    + Fmt(100f * MaxDisabledShare) + "% guard; faces untouched");
                return;
            }

            // the return value is the face's enabled state BEFORE the call: true = we changed it; island indices are refreshed by the last call only
            int alreadyDisabled = 0;
            for (int k = 0; k < candidates.Count; k++)
            {
                int face = candidates[k];
                if (scene.SetAbilityOfFaceWithIndex(face, false, k == candidates.Count - 1))
                {
                    _disabled.Add(face);
                    _disabledSet.Add(face);
                }
                else
                {
                    alreadyDisabled++;
                }
            }

            int disabledIslandMinusOne;
            int islandsAfter = CountIslands(scene, centres.Length, _disabledSet, out disabledIslandMinusOne);
            Log("BLOCK\tresult=BLOCKED\tdisabled=" + _disabled.Count
                + "\tbyCentreDepth=" + byCentre + "\tbyDeepCell=" + byGrid + "\tbyDeepShare(>=" + Fmt(FaceDeepShareBlock) + ")=" + byShare
                + " (only by share " + byShareOnly + ")"
                + "\talreadyDisabledBefore=" + alreadyDisabled
                + "\tislandsBefore=" + islandsBefore
                + "\tislandsAfter=" + islandsAfter
                + "\tdisabledFacesWithIsland-1=" + disabledIslandMinusOne + "/" + _disabled.Count
                + "\tfaceGroupIds=" + TopGroupIds(scene)
                + "\tms=" + Fmt(ElapsedMs(startTicks)));
        }

        private static float ElapsedMs(long startTicks)
        {
            return (float)((DateTime.UtcNow.Ticks - startTicks) / (double)TimeSpan.TicksPerMillisecond);
        }

        private static float Min3(float a, float b, float c)
        {
            return Math.Min(a, Math.Min(b, c));
        }

        private static float Max3(float a, float b, float c)
        {
            return Math.Max(a, Math.Max(b, c));
        }

        private static void CountVariant(int index, float level, float faceZ, int[] wet, int[] deep)
        {
            if (!IsPlausible(level))
            {
                return;
            }
            float depth = level - faceZ;
            if (depth > 0f)
            {
                wet[index]++;
            }
            if (depth >= BlockDepth)
            {
                deep[index]++;
            }
        }

        /// <summary>False for a water level that means "no water" (NaN, infinite, beyond 100 km). Shared with the overlay.</summary>
        internal static bool IsPlausible(float level)
        {
            return !float.IsNaN(level) && !float.IsInfinity(level) && Math.Abs(level) < ImplausibleWaterLevel;
        }

        private static int Bucket(float depth)
        {
            for (int b = 0; b < HistogramEdges.Length; b++)
            {
                if (depth < HistogramEdges[b])
                {
                    return b;
                }
            }
            return HistogramEdges.Length;
        }

        private int CountIslands(Scene scene, int faceCount, HashSet<int> disabled)
        {
            int unused;
            return CountIslands(scene, faceCount, disabled, out unused);
        }

        /// <summary>Distinct FaceIslandIndex values over all faces; -1 if the records could not be read.</summary>
        private int CountIslands(Scene scene, int faceCount, HashSet<int> disabled, out int disabledWithMinusOne)
        {
            disabledWithMinusOne = 0;
            if (faceCount <= 0)
            {
                return 0;
            }
            try
            {
                PathFaceRecord[] records = new PathFaceRecord[faceCount];
                scene.GetAllNavmeshFaceRecords(records);
                HashSet<int> islands = new HashSet<int>();
                for (int i = 0; i < records.Length; i++)
                {
                    islands.Add(records[i].FaceIslandIndex);
                    if (disabled != null && disabled.Contains(i) && records[i].FaceIslandIndex == -1)
                    {
                        disabledWithMinusOne++;
                    }
                }
                return islands.Count;
            }
            catch (Exception e)
            {
                ReportOnce("countIslands", e);
                return -1;
            }
        }

        /// <summary>The five most common face-group ids among the faces we disabled, as id:count.</summary>
        private string TopGroupIds(Scene scene)
        {
            try
            {
                Dictionary<int, int> counts = new Dictionary<int, int>();
                foreach (int face in _disabled)
                {
                    int id = scene.GetIdOfNavMeshFace(face);
                    int c;
                    counts.TryGetValue(id, out c);
                    counts[id] = c + 1;
                }
                return string.Join(",", counts.OrderByDescending(kv => kv.Value).Take(5)
                    .Select(kv => kv.Key + ":" + kv.Value).ToArray());
            }
            catch (Exception e)
            {
                ReportOnce("groupIds", e);
                return "?";
            }
        }

        /// <summary>Undoes everything: faces back on (last call refreshes islands), walls removed, slot condition off.
        /// The grid is kept, so the overlay can still show what was walled off.</summary>
        private void RevertAll(string reason)
        {
            bool hadCondition = _slotConditionLive;
            int walls = _wallQuads;
            int forcedReleased = _forced.Count;
            ReleaseAllForced("revert");
            UnsubscribeSlotCondition();
            RemoveWalls();

            Scene scene = (Mission != null) ? Mission.Scene : null;
            int reEnabled = 0;
            int failed = 0;
            int total = _disabled.Count;
            if (scene != null)
            {
                for (int k = 0; k < _disabled.Count; k++)
                {
                    try
                    {
                        // returns the state before the call: false = it was disabled, so this call re-enabled it
                        if (!scene.SetAbilityOfFaceWithIndex(_disabled[k], true, k == _disabled.Count - 1))
                        {
                            reEnabled++;
                        }
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }
            _disabled.Clear();
            _disabledSet.Clear();
            _reverted = true;
            _blockResult = "reverted";
            Log("t=" + Fmt(CurrentTime()) + "\tREVERTED\tfacesReEnabled=" + reEnabled + "/" + total
                + (failed > 0 ? "\tfailedCalls=" + failed : "")
                + "\twallQuadsRemoved=" + walls
                + "\tforcedExitsReleased=" + forcedReleased
                + "\tslotCondition=" + (hadCondition ? "removed" : "was off")
                + "\treason=" + reason);
            Flush();
        }

        private void TryConnectivityCheck(Mission mission, float now)
        {
            List<Agent> attackers = new List<Agent>();
            List<Agent> defenders = new List<Agent>();
            foreach (Agent agent in mission.Agents)
            {
                if (agent == null || !agent.IsHuman || !agent.IsActive() || agent.Team == null)
                {
                    continue;
                }
                if (agent.Team.Side == BattleSideEnum.Attacker)
                {
                    attackers.Add(agent);
                }
                else if (agent.Team.Side == BattleSideEnum.Defender)
                {
                    defenders.Add(agent);
                }
            }

            if (attackers.Count == 0 || defenders.Count == 0)
            {
                if (now >= ConnectivityWaitLogAt && !_connectivityWaitLogged)
                {
                    _connectivityWaitLogged = true;
                    Log("t=" + Fmt(now) + "\tCONNECTIVITY\twaiting: attackers=" + attackers.Count + " defenders="
                        + defenders.Count + " active humans (checked as soon as both sides have one)");
                }
                return;
            }
            _connectivityDone = true;

            if (_disabled.Count == 0 && _wallEntities.Count == 0)
            {
                Log("t=" + Fmt(now) + "\tCONNECTIVITY\tskipped: nothing blocked (" + _blockResult + ")");
                return;
            }

            Scene scene = mission.Scene;

            // each side's spawn: men standing on faces we switched off cannot plan paths from there
            if (_disabledSet.Count > 0)
            {
                int attackersOff = CountOnDisabledFaces(scene, attackers);
                int defendersOff = CountOnDisabledFaces(scene, defenders);
                float attackerShare = attackersOff / (float)attackers.Count;
                float defenderShare = defendersOff / (float)defenders.Count;
                Log("t=" + Fmt(now) + "\tCONNECTIVITY\tspawn on switched-off faces: attackers " + attackersOff + "/" + attackers.Count
                    + ", defenders " + defendersOff + "/" + defenders.Count + " (revert above " + Fmt(100f * MaxSideOnDisabledShare) + "%)");
                if (attackerShare > MaxSideOnDisabledShare || defenderShare > MaxSideOnDisabledShare)
                {
                    RevertAll("a side spawned mostly on switched-off faces (attackers " + attackersOff + "/" + attackers.Count
                        + ", defenders " + defendersOff + "/" + defenders.Count + ")");
                    return;
                }
            }

            Agent attackerRep;
            Agent defenderRep;
            int attackerFace = PickRepresentativeFace(scene, attackers, out attackerRep);
            int defenderFace = PickRepresentativeFace(scene, defenders, out defenderRep);
            if (attackerFace < 0 || defenderFace < 0)
            {
                Log("t=" + Fmt(now) + "\tCONNECTIVITY\tNO REPRESENTATIVE\tattacker=" + (attackerFace < 0 ? "none" : AgentId(attackerRep))
                    + "\tdefender=" + (defenderFace < 0 ? "none" : AgentId(defenderRep))
                    + "\t(no man on land on an enabled face among the " + RepresentativeCandidates
                    + " nearest each side's median); blocking stays, connectivity unverified");
                return;
            }

            bool pathRespect = scene.DoesPathExistBetweenFaces(attackerFace, defenderFace, false);
            bool pathIgnore = scene.DoesPathExistBetweenFaces(attackerFace, defenderFace, true);
            int attackerIsland = SafeIsland(scene, attackerFace);
            int defenderIsland = SafeIsland(scene, defenderFace);
            // the walls: a flood fill over the non-deep cells (the walls stand exactly on their edges)
            string gridResult = _wallEntities.Count > 0 ? GridConnected(attackerRep.Position.AsVec2, defenderRep.Position.AsVec2) : "no walls";
            bool gridOk = gridResult != "0";
            bool connected = pathRespect && pathIgnore && gridOk;

            Log("t=" + Fmt(now) + "\tCONNECTIVITY\t" + (connected ? "CONNECTED" : "DISCONNECTED")
                + "\tattacker=" + AgentId(attackerRep) + "@" + Pos(attackerRep.Position.AsVec2) + " face " + attackerFace + " island " + attackerIsland
                + "\tdefender=" + AgentId(defenderRep) + "@" + Pos(defenderRep.Position.AsVec2) + " face " + defenderFace + " island " + defenderIsland
                + "\tpathExists(ignoreDisabled=false)=" + (pathRespect ? "1" : "0")
                + "\tpathExists(ignoreDisabled=true)=" + (pathIgnore ? "1" : "0")
                + "\twallsLetThrough(grid flood)=" + gridResult);

            if (!connected)
            {
                RevertAll("sides not connected with deep water blocked (ignoreDisabled false=" + (pathRespect ? "1" : "0")
                    + ", true=" + (pathIgnore ? "1" : "0") + ", walls grid=" + gridResult + ")");
            }
        }

        /// <summary>"1" when a 4-connected flood fill over the non-deep cells gets from a to b, "0" when it does not (the walls cut the field), anything else = could not check. Main thread, at most one pass over the grid.</summary>
        private string GridConnected(Vec2 a, Vec2 b)
        {
            DeepWaterGrid g = _grid;
            if (g == null)
            {
                return "no grid";
            }
            int ia, ja, ib, jb;
            bool aIn = g.TryGetCell(a.x, a.y, out ia, out ja);
            bool bIn = g.TryGetCell(b.x, b.y, out ib, out jb);
            if (!aIn || !bIn)
            {
                return "rep outside the grid (unchecked)";
            }
            int start = ja * g.Nx + ia;
            int goal = jb * g.Nx + ib;
            if (g.Deep[start] || g.Deep[goal])
            {
                return "rep in a deep cell (unchecked)";
            }
            bool[] seen = new bool[g.CellCount];
            int[] stack = new int[g.CellCount];
            int sp = 0;
            stack[sp++] = start;
            seen[start] = true;
            while (sp > 0)
            {
                int k = stack[--sp];
                if (k == goal)
                {
                    return "1";
                }
                int i = k % g.Nx;
                int j = k / g.Nx;
                if (i > 0) Visit(g.Deep, seen, stack, ref sp, k - 1, false);
                if (i < g.Nx - 1) Visit(g.Deep, seen, stack, ref sp, k + 1, false);
                if (j > 0) Visit(g.Deep, seen, stack, ref sp, k - g.Nx, false);
                if (j < g.Ny - 1) Visit(g.Deep, seen, stack, ref sp, k + g.Nx, false);
            }
            return "0";
        }

        /// <summary>The face under the side's man nearest its median position, on land and not one we disabled.</summary>
        private int PickRepresentativeFace(Scene scene, List<Agent> side, out Agent picked)
        {
            picked = null;
            List<float> xs = new List<float>(side.Count);
            List<float> ys = new List<float>(side.Count);
            foreach (Agent agent in side)
            {
                Vec3 p = agent.Position;
                xs.Add(p.x);
                ys.Add(p.y);
            }
            xs.Sort();
            ys.Sort();
            Vec2 median = new Vec2(xs[xs.Count / 2], ys[ys.Count / 2]);

            List<Agent> ordered = side.OrderBy(a => a.Position.AsVec2.DistanceSquared(median))
                .Take(RepresentativeCandidates).ToList();
            foreach (Agent agent in ordered)
            {
                if (!agent.IsOnLand())
                {
                    continue;
                }
                int face = FaceUnder(scene, agent.Position);
                if (face >= 0 && !_disabledSet.Contains(face))
                {
                    picked = agent;
                    return face;
                }
            }
            return -1;
        }

        private int CountOnDisabledFaces(Scene scene, List<Agent> side)
        {
            int n = 0;
            foreach (Agent agent in side)
            {
                int face = FaceUnder(scene, agent.Position);
                if (face >= 0 && _disabledSet.Contains(face))
                {
                    n++;
                }
            }
            return n;
        }

        private static int FaceUnder(Scene scene, Vec3 position)
        {
            PathFaceRecord record = PathFaceRecord.NullFaceRecord;
            scene.GetNavMeshFaceIndex(ref record, position, false);
            return record.FaceIndex;
        }

        private int SafeIsland(Scene scene, int face)
        {
            try
            {
                return scene.GetNavMeshPathFaceRecord(face).FaceIslandIndex;
            }
            catch (Exception e)
            {
                ReportOnce("faceIsland", e);
                return -2;
            }
        }

        // swim-depth sampler + deep-cell watch (logging only)

        private void SampleSwimmers(Mission mission, float now)
        {
            Scene scene = mission.Scene;
            DeepWaterGrid grid = _grid;
            foreach (Agent agent in mission.Agents)
            {
                if (agent == null || !agent.IsHuman || !agent.IsActive())
                {
                    continue;
                }
                Agent mount = agent.MountAgent;
                bool mounted = mount != null;
                bool player = agent.IsPlayerControlled;

                AgentWaterState state;
                bool firstSeen = false;
                if (!_states.TryGetValue(agent, out state))
                {
                    state = new AgentWaterState();
                    _states[agent] = state;
                    firstSeen = true;
                }

                // deep cell INSIDE the wall: got past it (or was pushed / spawned there); deep shore cells outside the deep-biased wall do not count
                Vec3 pos = agent.Position;
                bool inDeep = InsideWall(grid, pos.AsVec2);
                if (inDeep && !state.InDeepCell)
                {
                    WriteDeepCellLine(scene, now, agent, mounted, player, pos, state);
                }
                state.InDeepCell = inDeep;
                state.PushHistory(pos.AsVec2, now);

                bool riderInWater = !agent.IsOnLand();
                bool mountInWater = mounted && !mount.IsOnLand();
                bool inWater = riderInWater || mountInWater;
                if (inWater == state.InWater)
                {
                    continue;
                }

                float level = scene.GetWaterLevelAtPosition(pos.AsVec2, true, true);
                float ground = scene.GetGroundHeightAtPosition(pos);
                float depth = level - ground;

                if (inWater)
                {
                    state.InWater = true;
                    state.Mounted = mounted;
                    state.EnterTime = now;
                    state.EnterDepth = depth;
                    state.AnchorPos = pos.AsVec2;
                    state.AnchorTime = now;
                    state.StuckLogged = false;
                    // "not on land" is mode & 3 != Land: WaterSurface/WaterDiving is real water, None is neither (see the SWIM line's mode= field)
                    int modeBits = (int)(agent.MovementMode & AgentMovementMode.MovementModeMask);
                    if (modeBits == (int)AgentMovementMode.None)
                    {
                        _swimEnterModeNone++;
                    }
                    else if (modeBits != (int)AgentMovementMode.Land)
                    {
                        _swimEnterModeWater++;
                    }
                    if (player)
                    {
                        _playerWaterEntries++;
                    }
                    else if (firstSeen)
                    {
                        _firstSeenInWater++;
                    }
                    else if (mounted)
                    {
                        _entriesMounted++;
                        AddCapped(_enterDepthMounted, depth);
                    }
                    else
                    {
                        _entriesFoot++;
                        AddCapped(_enterDepthFoot, depth);
                    }
                    WriteSwimLine(scene, now, firstSeen ? "ENTER(first seen)" : "ENTER", agent, mounted, player, riderInWater, mountInWater,
                        pos, level, ground, depth, -1f, inDeep, SlotState(agent, grid));
                }
                else
                {
                    float seconds = now - state.EnterTime;
                    state.InWater = false;
                    state.StuckLogged = false;
                    _exits++;
                    WriteSwimLine(scene, now, "EXIT", agent, mounted, player, riderInWater, mountInWater, pos, level, ground, depth, seconds,
                        inDeep, null);
                }
            }
        }

        private void WriteDeepCellLine(Scene scene, float now, Agent agent, bool mounted, bool player, Vec3 pos, AgentWaterState state)
        {
            // the player counts like anyone, and separately so the wall can be tested on foot
            _deepCellEntries++;
            if (mounted)
            {
                _deepCellEntriesMounted++;
            }
            _deepCellAgentIndices.Add(agent.Index);
            if (player)
            {
                _playerDeepCellEntries++;
            }
            state.EverDeep = true;

            // how did he get in? The move since the previous sample against the wall pieces: open gate (expected), closed piece (a wall failure), or no wall at all (spawned / teleported / a corner)
            Vec2 here = pos.AsVec2;
            Vec2 prev;
            float prevTime;
            bool hasPrev = state.TryGetHistory(1, out prev, out prevTime);
            Vec2 prev1s;
            float prev1sTime;
            bool hasPrev1s = state.TryGetHistory(4, out prev1s, out prev1sTime);
            string via;
            if (!hasPrev)
            {
                via = "unknown (first sample)";
            }
            else
            {
                int crossed = FindCrossedPiece(prev, here);
                if (crossed < 0)
                {
                    via = "NO_WALL_CROSSED";
                    _deepEntriesNoWallCrossed++;
                }
                else if (IsPieceOpen(crossed))
                {
                    via = "OPEN_GATE #" + crossed;
                    _deepEntriesViaOpenGate++;
                }
                else
                {
                    via = "CLOSED_PIECE #" + crossed + (_wallEntities.Count > 0 ? "" : " (walls gone)");
                    _deepEntriesViaClosedPiece++;
                    if (_wallEntities.Count > 0 && _wallLeakLines < MaxWallLeakLines && _leakLoggedPieces.Add(crossed))
                    {
                        try
                        {
                            WriteWallLeak(scene, now, agent, crossed, prev, here, pos.z, state);
                        }
                        catch (Exception e)
                        {
                            ReportOnce("wallLeak", e);
                        }
                    }
                }
            }

            if (_deepCellLines >= MaxDeepCellLines)
            {
                return;
            }
            _deepCellLines++;

            // shove or walk-through: friends within 1.5 m BEHIND him along his move
            int alliesBehind = -1;
            if (hasPrev && agent.Team != null && Mission != null)
            {
                Vec2 dir = here - prev;
                if (dir.LengthSquared > 0.0001f)
                {
                    dir = dir.Normalized();
                    alliesBehind = 0;
                    _scratchAllies.Clear();
                    Mission.GetNearbyAllyAgents(here, 1.5f, agent.Team, _scratchAllies);
                    foreach (Agent other in _scratchAllies)
                    {
                        if (other != null && other != agent && other.IsHuman
                            && Vec2.DotProduct(other.Position.AsVec2 - here, dir) < 0f)
                        {
                            alliesBehind++;
                        }
                    }
                    _scratchAllies.Clear();
                }
            }
            float velocity = -1f;
            try
            {
                velocity = agent.MovementVelocity.Length;
            }
            catch
            {
            }

            float level = scene.GetWaterLevelAtPosition(here, true, true);
            float ground = scene.GetGroundHeightAtPosition(pos);
            Log("t=" + Fmt(now) + "\tDEEPCELL\t" + AgentId(agent) + "\t" + SideName(agent)
                + "\t" + (mounted ? "mounted" : "foot")
                + (player ? "\tPLAYER" : "")
                + "\tvia=" + via
                + "\tdepth=" + Fmt(level - ground)
                + "\tinWater=" + (!agent.IsOnLand() ? "1" : "0")
                + "\twallLive=" + (_wallEntities.Count > 0 ? "1" : "0")
                + "\tslot=" + (SlotState(agent, _grid) ?? "-")
                + "\tpos=" + Pos(here) + "," + Fmt(pos.z)
                + "\tprev0.25s=" + (hasPrev ? Pos(prev) : "-")
                + "\tprev1s=" + (hasPrev1s ? Pos(prev1s) : "-")
                + "\tspeed=" + (velocity >= 0f ? Fmt(velocity) : "?")
                + "\tmoveSpeed=" + (hasPrev && now > prevTime ? Fmt(here.Distance(prev) / (now - prevTime)) : "-")
                + "\talliesBehind1.5m=" + (alliesBehind >= 0 ? alliesBehind.ToString() : "-")
                + "\tformation=" + (agent.Formation != null ? agent.Formation.FormationIndex.ToString() : "-")
                + "\torder=" + OrderName(agent.Formation));
        }

        /// <summary>WALLLEAK: the first crossing of a CLOSED piece, written so a log tells a geometric gap from collision that does not hold: the move, the piece's quads near it, terrain and water, the entity's physics state / flags / bounds, whether it was ever opened, ray casts along the move, and his slot plus the face under him.</summary>
        private void WriteWallLeak(Scene scene, float now, Agent agent, int pieceId, Vec2 prev, Vec2 here, float hereZ, AgentWaterState state)
        {
            _wallLeakLines++;
            WallPiece piece = _wallEntities[pieceId];
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("t=").Append(Fmt(now)).Append("\tWALLLEAK\tpiece #").Append(pieceId).Append(" @").Append(Pos(piece.Centre))
                .Append('\t').Append(AgentId(agent)).Append('\t').Append(SideName(agent))
                .Append('\t').Append(agent.MountAgent != null ? "mounted" : "foot")
                .Append("\tmove=").Append(Pos(prev)).Append(" -> ").Append(Pos(here)).Append(" (").Append(Fmt(prev.Distance(here))).Append("m)");

            Vec2 move = here - prev;
            int listed = 0;
            for (int k = piece.FirstSegment; k < piece.FirstSegment + piece.SegmentCount && listed < 6; k++)
            {
                WallSegment s = _wallSegments[k];
                Vec2 c0, c1;
                float d = Math.Min(s.DistanceTo(prev, out c0), s.DistanceTo(here, out c1));
                bool crossed = s.IsCrossedBy(prev, here);
                if (!crossed && d > 1f)
                {
                    continue;
                }
                listed++;
                // where the move line meets the quad's line, as a share along A->B (0..1 = within the span)
                string where = "parallel";
                Vec2 ab = s.B - s.A;
                float denom = move.x * ab.y - move.y * ab.x;
                if (Math.Abs(denom) > 1e-6f)
                {
                    Vec2 ap = prev - s.A;
                    float u = (move.x * ap.y - move.y * ap.x) / denom;
                    float length = ab.Length;
                    float along = u * length;
                    if (u < 0f || u > 1f)
                    {
                        where = "PAST END by " + Fmt(u < 0f ? -along : along - length) + "m";
                    }
                    else if (along < WallEndOverlap * 2f || length - along < WallEndOverlap * 2f)
                    {
                        where = "within span, at an end/overlap (" + Fmt(along) + "/" + Fmt(length) + "m)";
                    }
                    else
                    {
                        where = "within span (" + Fmt(along) + "/" + Fmt(length) + "m)";
                    }
                }
                sb.Append("\tquad[L").Append(s.Layer).Append(' ').Append(Pos(s.A)).Append("..").Append(Pos(s.B))
                    .Append(" z ").Append(Fmt(s.Bottom)).Append("..").Append(Fmt(s.Top))
                    .Append(" crossed=").Append(crossed ? "1" : "0").Append(" dist=").Append(Fmt(d)).Append(" ").Append(where)
                    // the (A_top, B_top, B_bottom) normal is (-ab.y, ab.x): "along" = through its back face
                    .Append(" move ").Append(Vec2.DotProduct(move, new Vec2(-ab.y, ab.x)) > 0f ? "along" : "against").Append(" quad normal")
                    .Append(']');
            }
            if (listed == 0)
            {
                sb.Append("\tquads=none within 1m");
            }

            Vec2 mid = (prev + here) * 0.5f;
            float terrain = scene.GetTerrainHeight(mid, true);
            float water = scene.GetWaterLevelAtPosition(mid, true, true);
            float groundPrev = scene.GetGroundHeightAtPosition(new Vec3(prev.x, prev.y, hereZ + 1f, -1f));
            sb.Append("\tatCrossing terrain=").Append(Fmt(terrain)).Append(" water=").Append(Fmt(water))
                .Append(" agentZ=").Append(Fmt(hereZ)).Append(" groundAtPrev=").Append(Fmt(groundPrev));

            GameEntity e = piece.Entity;
            Vec3 boxMin = e.GlobalBoxMin;
            Vec3 boxMax = e.GlobalBoxMax;
            sb.Append("\tentity physicsState=").Append(e.GetPhysicsState() ? "1" : "0")
                .Append(" bodyFlags=0x").Append(((uint)e.BodyFlag).ToString("X"))
                .Append(" descFlags=0x").Append(((uint)e.PhysicsDescBodyFlag).ToString("X"))
                .Append(" triangles=").Append(SafeTriangles(e))
                .Append(" box=").Append(Pos(boxMin.AsVec2)).Append(',').Append(Fmt(boxMin.z)).Append("..")
                .Append(Pos(boxMax.AsVec2)).Append(',').Append(Fmt(boxMax.z))
                .Append(" origin=").Append(Pos(piece.Origin.AsVec2)).Append(',').Append(Fmt(piece.Origin.z))
                .Append(" timesOpened=").Append(piece.TimesOpened)
                .Append(piece.TimesOpened > 0 ? " lastClosed " + Fmt(now - piece.ClosedAt) + "s ago" : "")
                .Append(piece.IsOpen ? " OPEN NOW" : "");

            // physics ray casts along the move (agent collision exclude flags, so an AgentOnly body counts)
            float zPrev = IsPlausible(groundPrev) ? groundPrev : hereZ;
            sb.Append("\trayKnee=").Append(RayProbe(scene, new Vec3(prev.x, prev.y, zPrev + 0.5f, -1f), new Vec3(here.x, here.y, hereZ + 0.5f, -1f)));
            sb.Append("\trayChest=").Append(RayProbe(scene, new Vec3(prev.x, prev.y, zPrev + 1.2f, -1f), new Vec3(here.x, here.y, hereZ + 1.2f, -1f)));

            float speed = -1f;
            try
            {
                speed = agent.MovementVelocity.Length;
            }
            catch
            {
            }
            sb.Append("\tspeed=").Append(speed >= 0f ? Fmt(speed) : "?")
                .Append("\talliesBehind1.5m=").Append(CountAlliesBehind(agent, prev, here));

            string slotText = "-";
            try
            {
                WorldPosition slot;
                if (agent.Formation != null && RBMAI.Utilities.TryGetUnitSlot(agent.Formation, agent, out slot))
                {
                    Vec2 s2 = slot.AsVec2;
                    slotText = Pos(s2) + " " + Fmt(here.Distance(s2)) + "m " + (SlotState(agent, _grid) ?? "?")
                        + " straightLineClear(CanMoveDirectlyToPosition)=" + (agent.CanMoveDirectlyToPosition(in s2) ? "1" : "0");
                }
            }
            catch (Exception ex)
            {
                ReportOnce("leakSlot", ex);
            }
            int face = FaceUnder(scene, agent.Position);
            sb.Append("\tslot=").Append(slotText)
                .Append("\tfaceUnder=").Append(face).Append(face >= 0 && _disabledSet.Contains(face) ? " (switched off by us)" : "")
                .Append("\torder=").Append(OrderName(agent.Formation));
            Log(sb.ToString());
        }

        /// <summary>A physics ray from a to b: "ours #piece d=..", "entity NAME d=..", "terrain d=.." or "none".</summary>
        private string RayProbe(Scene scene, Vec3 a, Vec3 b)
        {
            float distance;
            Vec3 closest;
            WeakGameEntity hit;
            if (!scene.RayCastForClosestEntityOrTerrain(a, b, out distance, out closest, out hit, 0.01f, BodyFlags.CommonCollisionExcludeFlagsForAgent))
            {
                return "none";
            }
            if (hit.IsValid)
            {
                int id = PieceOfEntity(hit);
                if (id >= 0)
                {
                    return "ours #" + id + " d=" + Fmt(distance);
                }
                string name = "?";
                try
                {
                    name = hit.Name;
                }
                catch
                {
                }
                return "entity '" + name + "' d=" + Fmt(distance);
            }
            return "terrain d=" + Fmt(distance);
        }

        private int PieceOfEntity(WeakGameEntity hit)
        {
            for (int k = 0; k < _wallEntities.Count; k++)
            {
                if (_wallEntities[k].Entity != null && _wallEntities[k].Entity.Pointer == hit.Pointer)
                {
                    return k;
                }
            }
            return -1;
        }

        private int CountAlliesBehind(Agent agent, Vec2 prev, Vec2 here)
        {
            if (agent.Team == null || Mission == null)
            {
                return -1;
            }
            Vec2 dir = here - prev;
            if (dir.LengthSquared <= 0.0001f)
            {
                return -1;
            }
            dir = dir.Normalized();
            int behind = 0;
            _scratchAllies.Clear();
            Mission.GetNearbyAllyAgents(here, 1.5f, agent.Team, _scratchAllies);
            foreach (Agent other in _scratchAllies)
            {
                if (other != null && other != agent && other.IsHuman && Vec2.DotProduct(other.Position.AsVec2 - here, dir) < 0f)
                {
                    behind++;
                }
            }
            _scratchAllies.Clear();
            return behind;
        }

        /// <summary>WALLTEST (logging only): for up to WallTestPieces pieces spread over the wall, a physics ray at half height across the middle of their first quad (agents' exclude flags). Hits on our own entity mean the body is in the physics scene; misses mean it is not. First tick, then every WallTestInterval s.</summary>
        private void WallTest(Scene scene, float now)
        {
            int pieces = _wallEntities.Count;
            if (pieces == 0)
            {
                return;
            }
            _wallTestRuns++;
            int tested = 0, ours = 0, otherEntity = 0, terrain = 0, none = 0, closedTested = 0, closedOurs = 0, reverseOurs = 0;
            string firstMiss = null;
            int stride = Math.Max(1, pieces / WallTestPieces);
            for (int p = 0; p < pieces && tested < WallTestPieces; p += stride)
            {
                WallPiece piece = _wallEntities[p];
                WallSegment s = _wallSegments[piece.FirstSegment];
                Vec2 mid = (s.A + s.B) * 0.5f;
                Vec2 n = s.DeepSide;
                float z = (s.Bottom + s.Top) * 0.5f;
                float ground = scene.GetTerrainHeight(mid, true);
                if (IsPlausible(ground) && ground + 1f > s.Bottom && ground + 1f < s.Top)
                {
                    z = ground + 1f;
                }
                Vec3 a = new Vec3(mid.x - n.x, mid.y - n.y, z, -1f);
                Vec3 b = new Vec3(mid.x + n.x, mid.y + n.y, z, -1f);
                string result = RayProbe(scene, a, b);
                // the other way too (deep -> dry): a one-sided body hits only one of the two
                if (RayProbe(scene, b, a).StartsWith("ours", StringComparison.Ordinal))
                {
                    reverseOurs++;
                }
                tested++;
                if (!piece.IsOpen)
                {
                    closedTested++;
                }
                if (result.StartsWith("ours", StringComparison.Ordinal))
                {
                    ours++;
                    if (!piece.IsOpen)
                    {
                        closedOurs++;
                    }
                }
                else
                {
                    if (result.StartsWith("entity", StringComparison.Ordinal)) otherEntity++;
                    else if (result.StartsWith("terrain", StringComparison.Ordinal)) terrain++;
                    else none++;
                    if (firstMiss == null)
                    {
                        firstMiss = "#" + p + " @" + Pos(mid) + " z=" + Fmt(z) + " -> " + result;
                    }
                }
            }
            _wallTestLastHitsOurs = ours;
            Log("t=" + Fmt(now) + "\tWALLTEST\ttested=" + tested + "\thitOurs(dry->deep)=" + ours + " (closed pieces " + closedOurs + "/" + closedTested + ")"
                + "\thitOurs(deep->dry)=" + reverseOurs
                + "\thitOtherEntity=" + otherEntity + "\thitTerrain=" + terrain + "\thitNothing=" + none
                + (firstMiss != null ? "\tfirstMiss=" + firstMiss : ""));
        }

        /// <summary>"deep" / "margin" / "dry" for the agent's own formation slot, or null when it has none (main thread).</summary>
        private string SlotState(Agent agent, DeepWaterGrid grid)
        {
            if (grid == null || agent == null || agent.Formation == null)
            {
                return null;
            }
            try
            {
                WorldPosition slot;
                if (!RBMAI.Utilities.TryGetUnitSlot(agent.Formation, agent, out slot))
                {
                    return null;
                }
                Vec2 p = slot.AsVec2;
                if (grid.IsDeepAt(p.x, p.y))
                {
                    return "deep";
                }
                return grid.IsBlockedAt(p.x, p.y) ? "margin" : "dry";
            }
            catch (Exception e)
            {
                ReportOnce("slotState", e);
                return "?";
            }
        }

        private void WriteSwimLine(Scene scene, float now, string kind, Agent agent, bool mounted, bool player, bool riderInWater, bool mountInWater,
            Vec3 pos, float level, float ground, float depth, float secondsInWater, bool inDeepCell, string slotState)
        {
            if (_swimLines >= MaxSwimLines)
            {
                _swimLinesDropped++;
                return;
            }
            // swim-on-dry-ground diagnostics (transitions only): raw movement mode (IsOnLand is "mode & 3 == Land", so None, e.g. airborne, reads as "in water"), IsInWater, the terrain heightmap under him and the other water-query variants
            string modeText = "?";
            string isInWater = "?";
            try
            {
                AgentMovementMode mode = agent.MovementMode;
                modeText = ((int)mode).ToString() + "(" + (mode & AgentMovementMode.MovementModeMask) + ")";
                isInWater = agent.IsInWater() ? "1" : "0";
            }
            catch
            {
            }
            float terrain = scene.GetTerrainHeight(pos.AsVec2, true);
            float renderer = scene.GetWaterLevelAtPosition(pos.AsVec2, true, false);
            float bodies = scene.GetWaterLevelAtPosition(pos.AsVec2, false, true);
            float neither = scene.GetWaterLevelAtPosition(pos.AsVec2, false, false);
            _swimLines++;
            StringBuilder sb = new StringBuilder();
            sb.Append("t=").Append(Fmt(now)).Append("\tSWIM\t").Append(kind)
                .Append('\t').Append(AgentId(agent))
                .Append('\t').Append(SideName(agent))
                .Append('\t').Append(mounted ? "mounted" : "foot");
            if (player)
            {
                sb.Append("\tPLAYER");
            }
            sb.Append("\triderWater=").Append(riderInWater ? "1" : "0")
                .Append("\tmountWater=").Append(mounted ? (mountInWater ? "1" : "0") : "-")
                .Append("\tdepth=").Append(Fmt(depth))
                .Append("\twaterAboveFeet=").Append(Fmt(level - pos.z))
                .Append("\twater=").Append(Fmt(level))
                .Append("\tground=").Append(Fmt(ground))
                .Append("\tinDeepCell=").Append(inDeepCell ? "1" : "0")
                .Append("\tmode=").Append(modeText)
                .Append("\tisInWater=").Append(isInWater)
                .Append("\taboveGround=").Append(Fmt(pos.z - ground))
                .Append("\tterrain=").Append(Fmt(terrain))
                .Append("\twaterRenderer=").Append(Fmt(renderer))
                .Append("\twaterBodies=").Append(Fmt(bodies))
                .Append("\twaterNeither=").Append(Fmt(neither));
            if (slotState != null)
            {
                sb.Append("\tslot=").Append(slotState);
            }
            sb.Append("\tpos=").Append(Pos(pos.AsVec2)).Append(',').Append(Fmt(pos.z));
            if (secondsInWater >= 0f)
            {
                sb.Append("\tsecondsInWater=").Append(Fmt(secondsInWater));
            }
            Log(sb.ToString());
        }

        private static void AddCapped(List<float> list, float value)
        {
            if (list.Count < MaxDepthSamples && !float.IsNaN(value))
            {
                list.Add(value);
            }
        }

        // stuck watch (logging only -- never moves anybody)

        private void StuckWatch(Mission mission, float now)
        {
            Scene scene = mission.Scene;
            DeepWaterGrid grid = _grid;
            bool checkFaces = _disabledSet.Count > 0;
            int inWater = 0;
            int onDisabled = 0;
            int inDeep = 0;

            foreach (Agent agent in mission.Agents)
            {
                if (agent == null || !agent.IsHuman || !agent.IsActive())
                {
                    continue;
                }
                bool player = agent.IsPlayerControlled;

                bool onDisabledFace = false;
                if (checkFaces)
                {
                    int face = FaceUnder(scene, agent.Position);
                    onDisabledFace = face >= 0 && _disabledSet.Contains(face);
                    if (onDisabledFace)
                    {
                        onDisabled++;
                    }
                }

                AgentWaterState state;
                if (!_states.TryGetValue(agent, out state))
                {
                    continue;
                }
                if (state.InDeepCell && !player)
                {
                    inDeep++;
                }

                Vec2 pos = agent.Position.AsVec2;
                if (!player && grid != null && _wallEntities.Count > 0)
                {
                    WallStuckCheck(scene, now, agent, state, grid, pos);
                }

                if (!state.InWater)
                {
                    continue;
                }
                inWater++;
                if (player)
                {
                    continue;
                }

                float moved = pos.Distance(state.AnchorPos);
                if (moved >= StuckMoveMeters)
                {
                    // still swimming somewhere: restart the window so a later stall is caught
                    state.AnchorPos = pos;
                    state.AnchorTime = now;
                    continue;
                }
                if (state.StuckLogged || now - state.AnchorTime < StuckSeconds)
                {
                    continue;
                }

                state.StuckLogged = true;
                _stuckEpisodes++;
                _stuckAgentIndices.Add(agent.Index);
                if (_stuckLines >= MaxStuckLines)
                {
                    continue;
                }
                _stuckLines++;

                Vec3 p3 = agent.Position;
                float level = scene.GetWaterLevelAtPosition(p3.AsVec2, true, true);
                float ground = scene.GetGroundHeightAtPosition(p3);
                Log("t=" + Fmt(now) + "\tSTUCK\t" + AgentId(agent) + "\t" + SideName(agent)
                    + "\t" + (agent.MountAgent != null ? "mounted" : "foot")
                    + "\tinWater=" + Fmt(now - state.EnterTime) + "s"
                    + "\tmoved=" + Fmt(moved) + "m in " + Fmt(now - state.AnchorTime) + "s"
                    + "\tdepth=" + Fmt(level - ground)
                    + "\tenterDepth=" + Fmt(state.EnterDepth)
                    + "\tinDeepCell=" + (state.InDeepCell ? "1" : "0")
                    + "\tslot=" + (SlotState(agent, grid) ?? "-")
                    + "\tonDisabledFace=" + (checkFaces ? (onDisabledFace ? "1" : "0") : "-")
                    + "\tpos=" + Pos(pos) + "," + Fmt(p3.z)
                    + "\tformation=" + (agent.Formation != null ? agent.Formation.FormationIndex.ToString() : "-")
                    + "\torder=" + OrderName(agent.Formation)
                    + "\taiControlled=" + (agent.IsAIControlled ? "1" : "0"));
            }

            if (inWater > _maxInWater) _maxInWater = inWater;
            if (onDisabled > _maxOnDisabled) _maxOnDisabled = onDisabled;
            if (inDeep > _maxInDeepCell) _maxInDeepCell = inDeep;

            bool interesting = inWater > 0 || onDisabled > 0 || inDeep > 0
                || _lastWatchInWater > 0 || _lastWatchOnDisabled > 0 || _lastWatchInDeep > 0;
            if (interesting && _watchLines < MaxWatchLines)
            {
                _watchLines++;
                Log("t=" + Fmt(now) + "\tWATCH\tinWater=" + inWater
                    + "\tinDeepCell(AI)=" + inDeep
                    + "\tonDisabledFace=" + (checkFaces ? onDisabled.ToString() : "-"));
            }
            _lastWatchInWater = inWater;
            _lastWatchOnDisabled = onDisabled;
            _lastWatchInDeep = inDeep;
        }

        /// <summary>An AI man in the margin next to the wall who has not moved for a while although his slot is elsewhere:
        /// held against the wall (typically by a path over a big face that straddles it).</summary>
        private void WallStuckCheck(Scene scene, float now, Agent agent, AgentWaterState state, DeepWaterGrid grid, Vec2 pos)
        {
            bool nearWall = grid.IsBlockedAt(pos.x, pos.y) && !grid.IsDeepAt(pos.x, pos.y);
            if (!nearWall)
            {
                state.NearWall = false;
                state.WallStuckLogged = false;
                return;
            }
            if (!state.NearWall || pos.Distance(state.WallAnchorPos) >= WallStuckMoveMeters)
            {
                state.NearWall = true;
                state.WallAnchorPos = pos;
                state.WallAnchorTime = now;
                state.WallStuckLogged = false;
                return;
            }
            if (state.WallStuckLogged || now - state.WallAnchorTime < WallStuckSeconds)
            {
                return;
            }
            float slotDistance = -1f;
            WorldPosition slot;
            if (agent.Formation != null && RBMAI.Utilities.TryGetUnitSlot(agent.Formation, agent, out slot))
            {
                slotDistance = pos.Distance(slot.AsVec2);
            }
            if (slotDistance >= 0f && slotDistance < WallStuckSlotDistance)
            {
                return;
            }
            state.WallStuckLogged = true;
            _wallStuckEpisodes++;
            // counts episodes of a man held at the wall whose own slot is on dry ground (his path runs into the wall)
            string slotState = SlotState(agent, grid);
            if (slotState == "dry")
            {
                _wallStuckDrySlot++;
            }
            if (_wallStuckLines >= MaxWallStuckLines)
            {
                return;
            }
            _wallStuckLines++;
            Log("t=" + Fmt(now) + "\tWALLSTUCK\t" + AgentId(agent) + "\t" + SideName(agent)
                + "\t" + (agent.MountAgent != null ? "mounted" : "foot")
                + "\theld " + Fmt(now - state.WallAnchorTime) + "s within " + Fmt(WallStuckMoveMeters) + "m"
                + "\tslotDistance=" + (slotDistance >= 0f ? Fmt(slotDistance) : "no slot")
                + "\tslot=" + (slotState ?? "-")
                + "\tpos=" + Pos(pos)
                + "\tformation=" + (agent.Formation != null ? agent.Formation.FormationIndex.ToString() : "-")
                + "\torder=" + OrderName(agent.Formation));
        }

        private static string OrderName(Formation formation)
        {
            try
            {
                return formation != null ? formation.GetReadonlyMovementOrderReference().OrderEnum.ToString() : "-";
            }
            catch
            {
                return "?";
            }
        }

        private static string LogFolderPath
        {
            get { return System.IO.Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "deepwater"); }
        }

        private void OpenLog()
        {
            try
            {
                System.IO.Directory.CreateDirectory(LogFolderPath);
                LogRetention.PruneOldest(LogFolderPath, "deepwater_*.log");
                string path = System.IO.Path.Combine(LogFolderPath, "deepwater_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".log");
                _log = BufferedLogWriter.Open(path);
            }
            catch
            {
                _log = null;
                _logging = false;
            }
        }

        private void Log(string line)
        {
            if (_log != null && !_log.WriteLine(line))
            {
                _log = null;
                _logging = false;
            }
        }

        private void Flush()
        {
            if (_log != null)
            {
                _log.Flush();
            }
        }

        private void WriteHeader(Mission mission)
        {
            if (_log == null)
            {
                return;
            }
            Scene scene = mission.Scene;
            StringBuilder sb = new StringBuilder();
            sb.Append("# RBM deep water blocker --hard invisible wall at swim-deep water (everyone, the player too;\n");
            sb.Append("# missiles and the camera pass; two layers), no formation slots there, its navmesh faces off for paths, AI men\n");
            sb.Append("# inside forced out (scripted move to the nearest shore cell, their gate open only while they pass).\n");
            sb.Append("# log opened ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                sb.Append("  RBMAI.dll built ").Append(System.IO.File.GetLastWriteTime(typeof(DeepWaterNavBlocker).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch
            {
            }
            sb.Append("\n");
            sb.Append("# scene=").Append(mission.SceneName).Append(" (").Append(SafeString(() => scene.GetName())).Append(")")
                .Append("  mode=").Append(mission.Mode)
                .Append("  teamAI=").Append(mission.MissionTeamAIType)
                .Append("  faces=").Append(SafeString(() => scene.GetNavMeshFaceCount().ToString()))
                .Append("  wallDepth=").Append(Fmt(WallDepth)).Append("m")
                .Append("  faceBlockDepth=").Append(Fmt(BlockDepth)).Append("m")
                .Append("  maxDisabledShare=").Append(Fmt(MaxDisabledShare))
                .Append("\n");

            try
            {
                Vec3 min;
                Vec3 max;
                scene.GetBoundingBox(out min, out max);
                Vec3 center = new Vec3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, max.z);
                float ground = scene.GetGroundHeightAtPosition(center);
                sb.Append("# water query sample: GetWaterLevel()=").Append(Fmt(scene.GetWaterLevel()))
                    .Append("  at scene centre ").Append(Pos(center.AsVec2))
                    .Append(" (ground ").Append(Fmt(ground)).Append("): renderer+bodies=")
                    .Append(Fmt(scene.GetWaterLevelAtPosition(center.AsVec2, true, true)))
                    .Append(" renderer=").Append(Fmt(scene.GetWaterLevelAtPosition(center.AsVec2, true, false)))
                    .Append(" bodies=").Append(Fmt(scene.GetWaterLevelAtPosition(center.AsVec2, false, true)))
                    .Append(" neither=").Append(Fmt(scene.GetWaterLevelAtPosition(center.AsVec2, false, false)))
                    .Append("  bbox ").Append(Pos(min.AsVec2)).Append(" .. ").Append(Pos(max.AsVec2))
                    .Append("\n");
            }
            catch (Exception e)
            {
                sb.Append("# water query sample FAILED ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append("\n");
            }

            sb.Append("#\n");
            sb.Append("# Tab separated. Lines:\n");
            sb.Append("#   BLOCK         navmesh faces: centre-depth scan, histogram, water-query flag variants, the deep-share\n");
            sb.Append("#                 pass (cells near deep water mapped to faces) and its histogram, then the faces switched\n");
            sb.Append("#                 off for paths (centre >= faceBlockDepth, centre in a deep cell, or deep share >= ")
                .Append(Fmt(FaceDeepShareBlock)).Append(")\n");
            sb.Append("#   GRID          the deep-water grid: bounds, step, cells, deep cells (water over terrain >= wallDepth),\n");
            sb.Append("#                 bridge-excluded cells (solid surface + navmesh at the water line), blocked = deep + margin\n");
            sb.Append("#   WALL          hard (AgentOnly) wall along the smoothed shoreline curve (contour line: polylines, points\n");
            sb.Append("#                 raw/smoothed/simplified, bias, shortest quad, ms), two layers ").Append(Fmt(WallLayerGap))
                .Append(" m apart: quads, segments, pieces, triangles, bounds, ms\n");
            sb.Append("#   WALLTEST      physics ray casts across sample pieces (first tick, then every ").Append(Fmt(WallTestInterval))
                .Append(" s): hits on our own bodies\n");
            sb.Append("#   WALLLEAK      the first crossing of each CLOSED piece: move, nearby quads (layer, ends, z, where it\n");
            sb.Append("#                 crossed), terrain/water, entity physics state / flags / triangles / bounds, ray casts\n");
            sb.Append("#                 along the move, speed, friends behind, slot + CanMoveDirectlyToPosition, face under him\n");
            sb.Append("#   SLOTS         formation slots / order positions refused in blocked cells (every 10 s while any)\n");
            sb.Append("#   PASS          the outcome of the EarlyStart pass\n");
            sb.Append("#   CONNECTIVITY  first tick with both sides on the field: DoesPathExistBetweenFaces (ignoreDisabled\n");
            sb.Append("#                 false and true) and a flood fill over the non-deep grid cells (the walls); all must\n");
            sb.Append("#                 pass or everything is reverted\n");
            sb.Append("#   REVERTED      faces back on, walls removed, slot condition dropped, and why\n");
            sb.Append("#   SWIM          land->water (ENTER) / water->land (EXIT): depth = water - ground under him, inDeepCell,\n");
            sb.Append("#                 slot = his formation slot deep / margin / dry; mode = raw movement mode (mask: 0 None,\n");
            sb.Append("#                 1 Land, 2 WaterSurface, 3 WaterDiving), isInWater, aboveGround, terrain heightmap, the\n");
            sb.Append("#                 other water-query variants; first ").Append(MaxSwimLines).Append(" lines\n");
            sb.Append("#   DEEPCELL      a man entering a deep cell: via OPEN_GATE (expected) / CLOSED_PIECE (wall failure) /\n");
            sb.Append("#                 NO_WALL_CROSSED, his position 0.25 s and 1 s earlier, speed, friends within 1.5 m behind\n");
            sb.Append("#                 him (a shove), slot state; PLAYER labelled and counted like anyone\n");
            sb.Append("#   FORCEDEXIT    START / DONE / FAILED (timeout or too many openings: retried elsewhere) / REROUTE (men\n");
            sb.Append("#                 outside kept the gate shut) / NOEXIT / GAVEUP / RELEASED / REMOVED for an AI man in a deep\n");
            sb.Append("#                 cell driven (SetScriptedPosition) to the nearest dry cell outside the wall: pos, exit, gate\n");
            sb.Append("#                 pieces, seconds, swimming or wading, scriptedGoTo = GoToPosition flag read back\n");
            sb.Append("#   GATE          OPEN / CLOSE (past / max ").Append(Fmt(GateMaxOpenSeconds)).Append(" s) / WAIT (a man outside within ")
                .Append(Fmt(GateOutsiderRadius)).Append(" m) of a\n");
            sb.Append("#                 forced man's gate piece; physicsAfter = GetPhysicsState() read back after the toggle\n");
            sb.Append("#   WATCH         once a second while anybody is in water / in a deep cell / on a disabled face\n");
            sb.Append("#   STUCK         an AI man in water ").Append(Fmt(StuckSeconds)).Append("+ s who moved under ")
                .Append(Fmt(StuckMoveMeters)).Append(" m (once per episode; nobody is moved)\n");
            sb.Append("#   WALLSTUCK     an AI man next to the wall, held ").Append(Fmt(WallStuckSeconds)).Append(" s within ")
                .Append(Fmt(WallStuckMoveMeters)).Append(" m, his slot ").Append(Fmt(WallStuckSlotDistance)).Append("+ m away\n");
            sb.Append("#   END           totals; PLAYER lines are labelled and never counted\n");
            sb.Append("#\n");
            Log(sb.ToString().Replace("\n", Environment.NewLine).TrimEnd());
        }

        private void WriteEndSummary()
        {
            if (_log == null)
            {
                return;
            }
            float now = CurrentTime();
            Log("t=" + Fmt(now) + "\tEND\tresult=" + _blockResult + (_reverted ? " (reverted)" : "")
                + "\tfacesStillDisabled=" + _disabled.Count
                + "\twallQuads=" + _wallQuads
                + "\tslotRejections=" + Interlocked.Read(ref _slotRejections)
                + "\tconnectivityChecked=" + (_connectivityDone ? "1" : "0"));
            Log("t=" + Fmt(now) + "\tEND\tswimEntries(AI) foot=" + _entriesFoot + " mounted=" + _entriesMounted
                + "\tfirstSeenInWater=" + _firstSeenInWater
                + "\tplayerWaterEntries=" + _playerWaterEntries
                + "\texits=" + _exits
                + "\tremovedWhileInWater=" + _removedInWater
                + "\tmaxInWaterAtOnce=" + _maxInWater
                + "\tmaxOnDisabledFaceAtOnce=" + _maxOnDisabled
                + "\tswimLines=" + _swimLines + " (dropped " + _swimLinesDropped + ")");
            Log("t=" + Fmt(now) + "\tEND\tdeepCell entries=" + _deepCellEntries + " (mounted " + _deepCellEntriesMounted
                + ", player " + _playerDeepCellEntries + ")"
                + "\tvia openGate=" + _deepEntriesViaOpenGate + " closedPiece=" + _deepEntriesViaClosedPiece
                + " noWallCrossed=" + _deepEntriesNoWallCrossed
                + "\tagentsEverInDeepCell=" + _deepCellAgentIndices.Count
                + "\tmaxInDeepCellAtOnce(AI)=" + _maxInDeepCell
                + "\twallStuckEpisodes=" + _wallStuckEpisodes
                + "\tmenInDeepShoreCellsOutsideWall(no forced exit)=" + _outsideWallDeepAgents.Count
                + "\twallStuckWithDrySlot=" + _wallStuckDrySlot + " (men whose path ran into the wall; the deep-share face pass, option #1, is meant to lower it)");
            Log("t=" + Fmt(now) + "\tEND\tforcedExit started=" + _forcedStarted + " (" + _forcedAgentsStarted + " men)"
                + "\tdone=" + _forcedDone + " (through a gate " + _forcedDoneThroughGate + ", avg "
                + Fmt(_forcedDone > 0 ? _forcedSecondsTotal / _forcedDone : 0f) + "s)"
                + "\tfailed=" + _forcedFailed + "\trerouted=" + _forcedRerouted + "\tremoved(died/fled inside)=" + _forcedRemoved
                + "\tnoExit=" + _forcedNoExit + "\trefusedAtCap(" + MaxForcedAtOnce + ")=" + _forcedRefusedCap
                + "\tmaxAtOnce=" + _maxForcedAtOnce + "\tscriptedReissued=" + _forcedReissued);
            Log("t=" + Fmt(now) + "\tEND\tforcedExit time split (DONE men): avg toGate=" + Fmt(_forcedToGateCount > 0 ? _forcedToGateTotal / _forcedToGateCount : 0f)
                + "s (n=" + _forcedToGateCount + ")\tavg waitedForOutsiders=" + Fmt(_forcedDone > 0 ? _forcedWaitedTotal / _forcedDone : 0f)
                + "s\tavg firstOpenToOut=" + Fmt(_forcedInGateCount > 0 ? _forcedInGateTotal / _forcedInGateCount : 0f)
                + "s (n=" + _forcedInGateCount + ")\toutWithoutAnyGate=" + _forcedDoneWithoutGate
                + "\tgatesOpenedDespiteOutsider(after " + Fmt(GateOutsiderGiveUpSeconds) + "s waited)=" + _gatesOpenedDespiteOutsider);
            Log("t=" + Fmt(now) + "\tEND\twallLeakLines=" + _wallLeakLines + " (pieces crossed while closed: " + _leakLoggedPieces.Count + ")"
                + "\twallTests=" + _wallTestRuns + " lastHitsOurs=" + _wallTestLastHitsOurs + "/" + WallTestPieces
                + "\tfacesByDeepShare=" + _facesByDeepShare);
            Log("t=" + Fmt(now) + "\tEND\tgates opened=" + _gatesOpened + "\tclosedPast=" + _gatesClosedPast
                + "\tclosedAtMax(" + Fmt(GateMaxOpenSeconds) + "s)=" + _gatesClosedMax
                + "\twaitedForOutsider=" + _gatesWaitedForOutsider
                + "\trefusedAtCap(" + MaxOpenGates + ")=" + _gatesRefusedCap
                + "\tmaxOpenAtOnce=" + _maxOpenGates
                + "\tbatchSlotsPatchedUnavailable=" + Interlocked.Read(ref DeepWaterSlots.PatchedUnavailable)
                + "\tbatchCallsSeen=" + Interlocked.Read(ref DeepWaterSlots.BatchCalls));
            Log("t=" + Fmt(now) + "\tEND\tsteering(keep-with-formation) callsWithTarget=" + Interlocked.Read(ref DeepWaterSteering.Calls)
                + "\tpositionsZeroed(target or line in deep water)=" + Interlocked.Read(ref DeepWaterSteering.Nulled)
                + "\tnativeCallSitesTranspiled=" + DeepWaterSteering.TranspiledCallSites + " (expect 2)");
            Log("t=" + Fmt(now) + "\tEND\tswimEnter mode: water=" + _swimEnterModeWater + " none(neither land nor water)=" + _swimEnterModeNone);
            Log("t=" + Fmt(now) + "\tEND\tenterDepth foot\t" + Describe(_enterDepthFoot));
            Log("t=" + Fmt(now) + "\tEND\tenterDepth mounted\t" + Describe(_enterDepthMounted));
            Log("t=" + Fmt(now) + "\tEND\tstuckEpisodes(AI)=" + _stuckEpisodes + "\tstuckAgents=" + _stuckAgentIndices.Count
                + "\tstuckLines=" + _stuckLines);
            Log("# mission ended");
        }

        private static string Describe(List<float> values)
        {
            if (values.Count == 0)
            {
                return "n=0";
            }
            List<float> sorted = new List<float>(values);
            sorted.Sort();
            return "n=" + sorted.Count
                + "\tmin=" + Fmt(sorted[0])
                + "\tp10=" + Fmt(Percentile(sorted, 0.1f))
                + "\tmedian=" + Fmt(Percentile(sorted, 0.5f))
                + "\tp90=" + Fmt(Percentile(sorted, 0.9f))
                + "\tmax=" + Fmt(sorted[sorted.Count - 1]);
        }

        private static float Percentile(List<float> sorted, float q)
        {
            int index = (int)(q * (sorted.Count - 1) + 0.5f);
            if (index < 0) index = 0;
            if (index >= sorted.Count) index = sorted.Count - 1;
            return sorted[index];
        }

        private void ReportOnce(string key, Exception e)
        {
            if (_reportedFailures.Contains(key))
            {
                return;
            }
            _reportedFailures.Add(key);
            Log("# FAILED [" + key + "] " + e.GetType().Name + ": " + e.Message);
        }

        private float CurrentTime()
        {
            try
            {
                return Mission != null ? Mission.CurrentTime : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static string AgentId(Agent agent)
        {
            if (agent == null)
            {
                return "-";
            }
            try
            {
                string id = (agent.Character != null) ? agent.Character.StringId : "?";
                return id + "#" + agent.Index;
            }
            catch
            {
                return "?#" + agent.Index;
            }
        }

        private static string SideName(Agent agent)
        {
            return (agent != null && agent.Team != null) ? agent.Team.Side.ToString() : "-";
        }

        private static string SafeString(Func<string> read)
        {
            try
            {
                return read() ?? "-";
            }
            catch
            {
                return "?";
            }
        }

        private static string Pos(Vec2 p)
        {
            return Fmt(p.x) + "," + Fmt(p.y);
        }

        private static string Fmt(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The deep-water grid, immutable once built (EarlyStart, main thread) and published through a volatile field, so
    /// worker threads (the formation-slot condition) read it without locks. Cell (i, j) covers
    /// [MinX + i·Step, MinX + (i+1)·Step) × [MinY + j·Step, ...); index j·Nx + i. Outside the grid nothing is deep.
    /// </summary>
    internal sealed class DeepWaterGrid
    {
        public readonly float MinX;
        public readonly float MinY;
        public readonly float Step;
        public readonly int Nx;
        public readonly int Ny;
        public readonly bool[] Deep;
        public readonly bool[] Blocked;
        public readonly int DeepCount;
        public readonly int BlockedCount;
        /// <summary>
        /// Cells ProbeCell called ProbeBridge (a bridge deck over deep water: the "bridgeExcluded" of the GRID line), as
        /// probed in EarlyStart before any face was switched off; read by BattleTerrainRead.Build. Not used by the blocker.
        /// </summary>
        public readonly bool[] Bridge;
        public readonly int BridgeCount;

        public DeepWaterGrid(float minX, float minY, float step, int nx, int ny, bool[] deep, bool[] blocked, int deepCount, int blockedCount,
            bool[] bridge, int bridgeCount)
        {
            MinX = minX;
            MinY = minY;
            Step = step;
            Nx = nx;
            Ny = ny;
            Deep = deep;
            Blocked = blocked;
            DeepCount = deepCount;
            BlockedCount = blockedCount;
            Bridge = bridge;
            BridgeCount = bridgeCount;
        }

        public int CellCount => Nx * Ny;

        public bool Contains(float x, float y)
        {
            return IndexOf(x, y) >= 0;
        }

        /// <summary>
        /// The bridge cells whose centre lies in the rectangle [minX, maxX) x [minY, maxY) (a coarser grid's cell): how many,
        /// and the centre of the one nearest <paramref name="near"/> (Invalid with none). Array reads only.
        /// </summary>
        public int FindBridgeCells(float minX, float minY, float maxX, float maxY, Vec2 near, out Vec2 nearest)
        {
            nearest = Vec2.Invalid;
            if (Bridge == null || BridgeCount == 0)
            {
                return 0;
            }
            int i0 = Math.Max(0, (int)Math.Ceiling((minX - MinX) / Step - 0.5f));
            int j0 = Math.Max(0, (int)Math.Ceiling((minY - MinY) / Step - 0.5f));
            int i1 = Math.Min(Nx - 1, (int)Math.Ceiling((maxX - MinX) / Step - 0.5f) - 1);
            int j1 = Math.Min(Ny - 1, (int)Math.Ceiling((maxY - MinY) / Step - 0.5f) - 1);
            int count = 0;
            float best = float.MaxValue;
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    if (!Bridge[j * Nx + i])
                    {
                        continue;
                    }
                    count++;
                    Vec2 c = new Vec2(MinX + (i + 0.5f) * Step, MinY + (j + 0.5f) * Step);
                    float d = c.DistanceSquared(near);
                    if (d < best)
                    {
                        best = d;
                        nearest = c;
                    }
                }
            }
            return count;
        }

        private int IndexOf(float x, float y)
        {
            if (float.IsNaN(x) || float.IsNaN(y))
            {
                return -1;
            }
            int i = (int)Math.Floor((x - MinX) / Step);
            int j = (int)Math.Floor((y - MinY) / Step);
            if (i < 0 || j < 0 || i >= Nx || j >= Ny)
            {
                return -1;
            }
            return j * Nx + i;
        }

        public bool IsDeepAt(float x, float y)
        {
            int k = IndexOf(x, y);
            return k >= 0 && Deep[k];
        }

        public bool TryGetCell(float x, float y, out int i, out int j)
        {
            i = -1;
            j = -1;
            int k = IndexOf(x, y);
            if (k < 0)
            {
                return false;
            }
            i = k % Nx;
            j = k / Nx;
            return true;
        }

        public bool IsBlockedAt(float x, float y)
        {
            int k = IndexOf(x, y);
            return k >= 0 && Blocked[k];
        }
    }

    /// <summary>One wall quad: A-B (the cell edge extended past both ends, the inner layer shifted toward the deep side),
    /// the quad's bottom and top z, the water/terrain surface z, the piece (entity / gate) it belongs to, the unit
    /// vector toward the deep side and the layer (0 = on the cell edge, 1 = the inner one).</summary>
    internal struct WallSegment
    {
        public readonly Vec2 A;
        public readonly Vec2 B;
        public readonly float Bottom;
        public readonly float Top;
        public readonly float SurfaceZ;
        public readonly int PieceId;
        public readonly Vec2 DeepSide;
        public readonly byte Layer;

        public WallSegment(Vec2 a, Vec2 b, float bottom, float top, float surfaceZ, int pieceId, Vec2 deepSide, byte layer)
        {
            A = a;
            B = b;
            Bottom = bottom;
            Top = top;
            SurfaceZ = surfaceZ;
            PieceId = pieceId;
            DeepSide = deepSide;
            Layer = layer;
        }

        /// <summary>True when the segment p0 -> p1 crosses this one (endpoints included); t = where along p0 -> p1.</summary>
        public bool Intersect(Vec2 p0, Vec2 p1, out float t)
        {
            t = 0f;
            Vec2 r = p1 - p0;
            Vec2 s = B - A;
            float denom = Cross(r, s);
            if (Math.Abs(denom) < 1e-6f)
            {
                return false;
            }
            Vec2 qp = A - p0;
            float tt = Cross(qp, s) / denom;
            float u = Cross(qp, r) / denom;
            if (tt < 0f || tt > 1f || u < 0f || u > 1f)
            {
                return false;
            }
            t = tt;
            return true;
        }

        /// <summary>Distance from <paramref name="p"/> to the segment, and the closest point on it.</summary>
        public float DistanceTo(Vec2 p, out Vec2 closest)
        {
            Vec2 ab = B - A;
            float lengthSq = ab.LengthSquared;
            float t = lengthSq > 0f ? Vec2.DotProduct(p - A, ab) / lengthSq : 0f;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            closest = A + ab * t;
            return p.Distance(closest);
        }

        public bool IsCrossedBy(Vec2 p0, Vec2 p1)
        {
            float d1 = Cross(B - A, p0 - A);
            float d2 = Cross(B - A, p1 - A);
            float d3 = Cross(p1 - p0, A - p0);
            float d4 = Cross(p1 - p0, B - p0);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(Vec2 u, Vec2 v)
        {
            return u.x * v.y - u.y * v.x;
        }
    }
}
