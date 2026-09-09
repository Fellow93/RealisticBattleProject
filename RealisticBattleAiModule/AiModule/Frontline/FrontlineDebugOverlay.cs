using System.Collections.Generic;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;
using static RBMAI.Frontline.AIMindset;

namespace RBMAI
{
    /// <summary>
    /// In-mission visual debugger for the per-agent Frontline decision system. Toggled with
    /// Ctrl+Shift+F, off by default, and completely inert until then.
    ///
    /// Technique: screen-space Gauntlet widgets positioned with MBWindowManager.WorldToScreen, the
    /// same approach RBM's agent status bars use (RBM/AgentStatusBar). MBDebug.RenderDebugSphere /
    /// RenderDebugLine are NOT usable here -- every one of them carries
    /// [Conditional("_RGL_KEEP_ASSERTS")], so the call sites are stripped from our Release build.
    ///
    /// THREADING: everything below runs on the main thread from OnMissionTick. It only READS the
    /// Frontline decision dictionary and the AIDecisionState fields; those are written by the
    /// parallel movement job, so the values are racy-but-benign (plain 32-bit/reference fields on
    /// long-lived objects -- worst case the marker shows last tick's decision). Nothing here calls
    /// into the worker path or mutates AI state.
    /// </summary>
    public class FrontlineDebugOverlay : MissionLogic
    {
        // Dots used to draw one formation's direction line, and how far ahead it reaches.
        private const int DotsPerFormationLine = 10;

        private const float FormationLineLength = 12f;

        private FrontlineDebugVM _dataSource;
        private GauntletLayer _gauntletLayer;
        private MissionScreen _missionScreen;

        private bool _enabled;

        // Reused every tick so the per-tick allocation is one StringBuilder clear, not a list churn.
        private readonly int[] _decisionCounts = new int[6];
        private readonly StringBuilder _readout = new StringBuilder(256);
        private readonly List<Formation> _formationScratch = new List<Formation>();

        public override void AfterStart()
        {
            base.AfterStart();
            _missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (_missionScreen == null)
            {
                return;
            }
            _dataSource = new FrontlineDebugVM();
            _gauntletLayer = new GauntletLayer("RBMFrontlineDebugLayer", 2);
            _missionScreen.AddLayer(_gauntletLayer);
            _gauntletLayer.LoadMovie("RBMFrontlineDebug", _dataSource);
        }

        protected override void OnEndMission()
        {
            base.OnEndMission();
            if (_gauntletLayer != null)
            {
                _missionScreen?.RemoveLayer(_gauntletLayer);
                _gauntletLayer = null;
            }
            _dataSource?.OnFinalize();
            _dataSource = null;
            _missionScreen = null;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_dataSource == null || _missionScreen == null)
            {
                return;
            }
            bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool shift = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);
            if (ctrl && shift && Input.IsKeyPressed(InputKey.F))
            {
                _enabled = !_enabled;
                _dataSource.IsActive = _enabled;
                if (!_enabled)
                {
                    _dataSource.HideAll();
                }
            }
            if (!_enabled)
            {
                return;
            }
            Refresh();
        }

        private void Refresh()
        {
            Mission mission = Mission.Current;
            Camera camera = _missionScreen.CombatCamera;
            if (mission == null || camera == null || _gauntletLayer == null)
            {
                return;
            }
            // WorldToScreen yields real pixels; PositionXOffset is in unscaled UI units.
            float inverseScale = _gauntletLayer.UIContext.InverseScale;

            _dataSource.BeginFrame();
            for (int i = 0; i < _decisionCounts.Length; i++)
            {
                _decisionCounts[i] = 0;
            }

            // Iterate the live agent list, not the dictionary -- dead agents linger in the dictionary.
            // Every agent with a decision is drawn: no distance cutoff and no cap, so neither side
            // ever drops out depending on where the camera is.
            foreach (Agent agent in mission.Agents)
            {
                if (agent == null || !agent.IsHuman || !agent.IsActive())
                {
                    continue;
                }
                // The frontline system only acts under charge orders; a decision left over from an
                // earlier charge is stale once the formation is given anything else.
                Formation agentFormation = agent.Formation;
                if (agentFormation == null)
                {
                    continue;
                }
                OrderType orderType = agentFormation.GetReadonlyMovementOrderReference().OrderType;
                if (orderType != OrderType.Charge && orderType != OrderType.ChargeWithTarget)
                {
                    continue;
                }
                Frontline.AIDecisionState state;
                if (!Frontline.aiDecisionCooldownDict.TryGetValue(agent, out state) || state == null)
                {
                    continue;
                }
                AIDecision decision = state.AIMindset.currentDecision;
                int decisionIndex = (int)decision;
                if (decisionIndex >= 0 && decisionIndex < _decisionCounts.Length)
                {
                    _decisionCounts[decisionIndex]++;
                }
                Vec3 pos = agent.Position;
                pos.z += agent.HasMount ? 3.2f : 2.3f;
                if (Project(camera, pos, inverseScale, out float x, out float y))
                {
                    _dataSource.PushMarker(x, y, ColorOf(decision), 10);
                }
            }

            // Per AI infantry formation: its Direction, drawn as a dotted line out of the median position.
            _formationScratch.Clear();
            foreach (Team team in mission.Teams)
            {
                if (team == null)
                {
                    continue;
                }
                foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (formation == null || !formation.IsAIControlled || formation.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    if (formation.QuerySystem == null || !formation.QuerySystem.IsInfantryFormation)
                    {
                        continue;
                    }
                    Vec2 dir = formation.Direction;
                    if (dir.LengthSquared < 0.0001f)
                    {
                        continue;
                    }
                    dir = dir.Normalized();
                    Vec3 origin = formation.CachedMedianPosition.GetGroundVec3();
                    uint lineColor = team.IsPlayerTeam || team.IsPlayerAlly ? 0xFF60FF60u : 0xFFFF6060u;
                    for (int d = 1; d <= DotsPerFormationLine; d++)
                    {
                        float t = FormationLineLength * d / DotsPerFormationLine;
                        Vec3 p = new Vec3(origin.x + dir.x * t, origin.y + dir.y * t, origin.z + 1.2f, -1f);
                        if (Project(camera, p, inverseScale, out float lx, out float ly))
                        {
                            _dataSource.PushMarker(lx, ly, lineColor, 6);
                        }
                    }
                }
            }

            _dataSource.EndFrame();

            _readout.Clear();
            _readout.Append("RBM Frontline debug (Ctrl+Shift+F)\n");
            _readout.Append("Attack ").Append(_decisionCounts[(int)AIDecision.Attack]);
            _readout.Append("  BackStep ").Append(_decisionCounts[(int)AIDecision.BackStep]).Append('\n');
            _readout.Append("FindAlly ").Append(_decisionCounts[(int)AIDecision.FindAlly]);
            _readout.Append("  FlankL ").Append(_decisionCounts[(int)AIDecision.FlankAllyLeft]);
            _readout.Append("  FlankR ").Append(_decisionCounts[(int)AIDecision.FlankAllyRight]);
            _readout.Append("  Rest ").Append(_decisionCounts[(int)AIDecision.Rest]).Append('\n');
            _dataSource.Readout = _readout.ToString();
        }

        private static bool Project(Camera camera, Vec3 worldPos, float inverseScale, out float x, out float y)
        {
            float sx = 0f;
            float sy = 0f;
            float sz = 0f;
            MBWindowManager.WorldToScreen(camera, worldPos, ref sx, ref sy, ref sz);
            x = sx * inverseScale;
            y = sy * inverseScale;
            return sz >= 0f;
        }

        private static uint ColorOf(AIDecision decision)
        {
            switch (decision)
            {
                case AIDecision.Attack: return 0xFFE03030u;   // red
                case AIDecision.BackStep: return 0xFFF0E020u; // yellow
                case AIDecision.FindAlly: return 0xFF30D030u; // green
                case AIDecision.FlankAllyLeft: return 0xFF30E0E0u;  // cyan
                case AIDecision.FlankAllyRight: return 0xFFE030E0u; // magenta
                default: return 0xFF909090u;                  // Rest -- grey
            }
        }
    }

    /// <summary>
    /// Fixed pool of marker VMs. The binding list is never resized after warm-up; markers beyond the
    /// count pushed this frame are simply hidden, so no widget is created or destroyed per tick.
    /// </summary>
    public class FrontlineDebugVM : ViewModel
    {
        private const int PoolSize = FrontlineDebugMarkerVM.PoolSize;

        private readonly MBBindingList<FrontlineDebugMarkerVM> _markers = new MBBindingList<FrontlineDebugMarkerVM>();
        private int _cursor;
        private int _lastUsed;
        private bool _isActive;
        private string _readout = "";

        public FrontlineDebugVM()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                _markers.Add(new FrontlineDebugMarkerVM());
            }
        }

        [DataSourceProperty]
        public MBBindingList<FrontlineDebugMarkerVM> Markers => _markers;

        [DataSourceProperty]
        public bool IsActive
        {
            get { return _isActive; }
            set { if (_isActive != value) { _isActive = value; OnPropertyChanged("IsActive"); } }
        }

        [DataSourceProperty]
        public string Readout
        {
            get { return _readout; }
            set { if (_readout != value) { _readout = value; OnPropertyChanged("Readout"); } }
        }

        public void BeginFrame()
        {
            _cursor = 0;
        }

        public void PushMarker(float x, float y, uint color, int size)
        {
            // Grow on demand; the list only ever grows, so widget churn is bounded by the peak count.
            if (_cursor >= _markers.Count)
            {
                _markers.Add(new FrontlineDebugMarkerVM());
            }
            FrontlineDebugMarkerVM marker = _markers[_cursor++];
            marker.ScreenX = x;
            marker.ScreenY = y;
            marker.MarkerColor = Color.FromUint(color);
            marker.Size = size;
            marker.IsHidden = false;
        }

        public void EndFrame()
        {
            for (int i = _cursor; i < _lastUsed; i++)
            {
                _markers[i].IsHidden = true;
            }
            _lastUsed = _cursor;
        }

        public void HideAll()
        {
            for (int i = 0; i < _markers.Count; i++)
            {
                _markers[i].IsHidden = true;
            }
            _cursor = 0;
            _lastUsed = 0;
        }
    }

    public class FrontlineDebugMarkerVM : ViewModel
    {
        // 300 agent markers + 12 formation lines * 10 dots.
        public const int PoolSize = 420;

        private float _screenX;
        private float _screenY;
        private Color _markerColor = Color.FromUint(0xFFFFFFFFu);
        private float _size = 10f;
        private bool _isHidden = true;

        [DataSourceProperty]
        public float ScreenX
        {
            get { return _screenX; }
            set { if (_screenX != value) { _screenX = value; OnPropertyChanged("ScreenX"); } }
        }

        [DataSourceProperty]
        public float ScreenY
        {
            get { return _screenY; }
            set { if (_screenY != value) { _screenY = value; OnPropertyChanged("ScreenY"); } }
        }

        [DataSourceProperty]
        public Color MarkerColor
        {
            get { return _markerColor; }
            set { if (_markerColor != value) { _markerColor = value; OnPropertyChanged("MarkerColor"); } }
        }

        [DataSourceProperty]
        public float Size
        {
            get { return _size; }
            set { if (_size != value) { _size = value; OnPropertyChanged("Size"); } }
        }

        [DataSourceProperty]
        public bool IsHidden
        {
            get { return _isHidden; }
            set { if (_isHidden != value) { _isHidden = value; OnPropertyChanged("IsHidden"); } }
        }
    }
}
