using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core;
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
    /// Developer view of the feature map on the battlefield: a marker and short label at each feature, flank ends coloured
    /// covered / open by TerrainFlankPlan, each army's centre with its flank-attack side, archer spots, cavalry ground
    /// (lanes, reform points), the choke hold, the battle position and reserve plug, and area
    /// outlines (dots). Added under rbmAiEnabled &amp;&amp; developerMode. Toggled with Ctrl+Shift+D (cycles off -> terrain ->
    /// terrain + decisions -> decisions only -> off); until then inert: no layer, no view model, no per-frame work. The
    /// decision layer is in TerrainFeatureOverlay.Decisions.cs.
    ///
    /// Heights come from the snapshot's grid + LabelLift, armies from their gathered centroid: no engine query except the
    /// per-frame WorldToScreen. Widgets are pooled: lists are sized and texts / colours set once when the overlay is switched
    /// on or the snapshot / plans change, then only screen positions and hidden flags change per frame (the
    /// NavigableDebugOverlay pattern). The claim / use lines' dots (DynamicDotCapacity) and the use labels
    /// (UseLabelCapacity) are fixed tails of their pools, re-placed every frame. Cavalry, choke hold and battle
    /// position have dot budgets of their own, apart from the outlines' OutlineMaxDots. Re-planned spot / cavalry /
    /// battle-position versions are drawn as the CURRENT version (" v3" on labels).
    ///
    /// THREADING: main thread only, from OnMissionTick (after the camera update). Reads the immutable published
    /// BattleTerrain / plan objects; computes nothing about the terrain. Instance fields only, cleared on toggle-off, mission
    /// end and OnRemoveBehavior.
    /// </summary>
    public partial class TerrainFeatureOverlay : MissionLogic
    {
        private const float LabelLift = 2f; // m above the grid's terrain
        private const float ArmyLift = 3f; // m above the centroid

        private const int CrestDots = 7;
        private const float CrestDotSpan = 300f;
        private const float CrestDotDefaultSpan = 60f;

        private const int ArrowDots = 5;
        private const float ArrowDotSpacing = 12f;

        private const float MarkerSize = 10f; // px, dot beside each label
        private const float LabelHalfHeight = 10f; // px, for centring the label on the point
        private const float DotSize = 7f;
        private const float DotAlpha = 0.9f;

        // colours, ARGB; the text uses the same colour
        private const uint HillColor = 0xFFE8B040u;
        private const uint CrestColor = 0xFFF0E070u;
        private const uint ChokeColor = 0xFFFF7040u;
        private const uint OpenColor = 0xFF60E060u;
        private const uint CoveredColor = 0xFF50A8FFu;
        private const uint ExposedColor = 0xFFFF4848u;
        private const uint UnplannedFlankColor = 0xFFFFFFFFu;
        private const uint WoodsColor = 0xFF50C050u;
        private const uint RoughColor = 0xFFC89870u;
        private const uint WaterColor = 0xFF40C8FFu;
        private const uint FordColor = 0xFFA0E8FFu;
        private const uint AttackerColor = 0xFFFF6060u;
        private const uint DefenderColor = 0xFF6090FFu;
        private const uint AttackerSpotColor = 0xFFFFA0E0u;
        private const uint DefenderSpotColor = 0xFFA0E0FFu;

        private const float OutlineDotSize = 5f;
        private const uint OutlineOpenColor = 0xFF60E060u;
        private const uint OutlineRoughColor = 0xFFA0703Cu;
        private const uint OutlineWoodsColor = 0xFF2E8B3Au;
        private const uint OutlineWaterColor = 0xFF3C7CFFu;
        private const uint OutlineHilltopColor = 0xFFFFE040u;
        private const uint OutlineChokeColor = 0xFFFF8C20u;
        private const uint OutlineSpotColor = 0xFF9AD8FFu;

        private const float CostRefreshSeconds = 1f; // legend's projection cost refresh, not every frame

        private const int MaxClaimLines = 8;
        private const int ClaimLineDots = 8;

        internal const int CavalryMaxDots = 600; // own budget, apart from OutlineMaxDots
        private const float CavalryLaneDotSpacing = 10f;
        private const uint AttackerCavalryColor = 0xFFFF9070u;
        private const uint DefenderCavalryColor = 0xFF60E8E8u;
        private const int MaxUseLines = 8;
        private const int UseLabelCapacity = MaxUseLines;

        private const int ApproachMaxDots = 200;
        private const float ApproachDotSpacing = 10f;
        private const uint ApproachColor = 0xFFFF80FFu;

        private const int ChokeHoldMaxDots = 80;
        private const float ChokeHoldDotSpacing = 3f;
        private const uint ChokeHoldColor = 0xFFFF60D0u;

        private const int BattlePositionMaxDots = 80;
        private const float BattlePositionDotSpacing = 3f;
        private const uint BattlePositionColor = 0xFFB8FF40u;
        private const uint BattlePositionAltColor = 0xFF90B060u;
        private const uint BattlePositionPlugColor = 0xFFE0FF90u;
        private const int PlugLineMaxDots = 24;
        private const float PlugLineDotSpacing = 6f;

        /// <summary>The pool tail for the per-frame lines: archer-spot claims, then cavalry holds.</summary>
        private const int DynamicDotCapacity = (MaxClaimLines + MaxUseLines) * ClaimLineDots;

        private struct Point
        {
            public Vec3 World;
            public uint Color;
            /// <summary>Dot size (px); 0 = DotSize.</summary>
            public float Size;
        }

        private readonly List<Point> _labelPoints = new List<Point>();
        private readonly List<string> _labelTexts = new List<string>();
        private readonly List<Point> _dotPoints = new List<Point>();

        /// <summary>The claim / use lines' dots, recomputed every frame after the static dots (at most DynamicDotCapacity).</summary>
        private readonly List<Point> _claimDotPoints = new List<Point>();
        private readonly List<KeyValuePair<Formation, TerrainSpots.Spot>> _claims = new List<KeyValuePair<Formation, TerrainSpots.Spot>>();
        private readonly List<TerrainCavalry.Hold> _holds = new List<TerrainCavalry.Hold>();

        /// <summary>The cavalry use labels, recomputed every frame (at most UseLabelCapacity, the label pool's tail).</summary>
        private readonly List<Point> _useLabelPoints = new List<Point>();
        private readonly List<string> _useLabelTexts = new List<string>();
        /// <summary>What each tail label slot shows now, so its content is set only when it changes.</summary>
        private readonly string[] _useSlotTexts = new string[UseLabelCapacity];
        private readonly uint[] _useSlotColors = new uint[UseLabelCapacity];

        /// <summary>Static cavalry dots added at the build (CavalryMaxDots at most) and dots on screen last frame.</summary>
        private int _cavalryDots;
        private int _approachDots;
        private TerrainApproach.SidePlan _builtApproach;
        private int _onScreen;

        /// <summary>What the view shows; Ctrl+Shift+D steps to the next one (NextMode), Off removes the layer.</summary>
        private enum OverlayMode
        {
            Off,
            Terrain,
            TerrainAndDecisions,
            Decisions
        }

        private OverlayMode _mode;

        /// <summary>The terrain layer's legend part (scene, labels, dots, projection cost); the decision layer has its own.</summary>
        private string _terrainLegend;

        private bool _enabled;
        private string _failure;
        private TerrainFeatureOverlayVM _dataSource;
        private GauntletLayer _gauntletLayer;
        private MissionScreen _missionScreen;

        private bool _built;
        private BattleTerrain _builtFor;
        private TerrainFlankPlan.SidePlan _builtPlanAttacker;
        private TerrainFlankPlan.SidePlan _builtPlanDefender;
        private TerrainSpots.SidePlan _builtSpotsAttacker;
        private TerrainSpots.SidePlan _builtSpotsDefender;
        private TerrainCavalry.SidePlan _builtCavalryAttacker;
        private TerrainCavalry.SidePlan _builtCavalryDefender;
        private TerrainChokeHold.Plan _builtChokeHold;
        /// <summary>The battle position drawn (a re-pick publishes a new plan: redrawn).</summary>
        private TerrainBattlePosition.Plan _builtBattlePosition;

        /// <summary>The legend as built; the projection cost is appended to it (CostRefreshSeconds).</summary>
        private string _legendBase;
        private long _projectTicks;
        private int _projectFrames;
        private float _costSince;

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool shift = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);
            if (ctrl && shift && Input.IsKeyPressed(InputKey.D))
            {
                StepMode();
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
                // stop drawing; the legend says why
                _failure = e.GetType().Name + ": " + e.Message;
                if (_dataSource != null)
                {
                    _dataSource.HideAll();
                    _dataSource.Legend = ModeHeader() + "\nFAILED, overlay stopped: " + _failure;
                }
            }
        }

        /// <summary>Ctrl+Shift+D: off -> terrain -> terrain + decisions -> decisions only -> off.</summary>
        private void StepMode()
        {
            switch (_mode)
            {
                case OverlayMode.Off:
                    TurnOn();
                    if (_enabled)
                    {
                        _mode = OverlayMode.Terrain;
                    }
                    break;
                case OverlayMode.Terrain:
                    _mode = OverlayMode.TerrainAndDecisions;
                    break;
                case OverlayMode.TerrainAndDecisions:
                    _mode = OverlayMode.Decisions;
                    // terrain pools are no longer projected: hide them once
                    _dataSource?.HideTerrain();
                    break;
                default:
                    TurnOff();
                    break;
            }
            UpdateLegend();
        }

        private bool ShowsTerrain => _mode == OverlayMode.Terrain || _mode == OverlayMode.TerrainAndDecisions;

        private bool ShowsDecisions => _mode == OverlayMode.TerrainAndDecisions || _mode == OverlayMode.Decisions;

        private static OverlayMode NextMode(OverlayMode mode)
        {
            switch (mode)
            {
                case OverlayMode.Off:
                    return OverlayMode.Terrain;
                case OverlayMode.Terrain:
                    return OverlayMode.TerrainAndDecisions;
                case OverlayMode.TerrainAndDecisions:
                    return OverlayMode.Decisions;
                default:
                    return OverlayMode.Off;
            }
        }

        private static string ModeName(OverlayMode mode)
        {
            switch (mode)
            {
                case OverlayMode.Terrain:
                    return "terrain";
                case OverlayMode.TerrainAndDecisions:
                    return "terrain + decisions";
                case OverlayMode.Decisions:
                    return "decisions only";
                default:
                    return "off";
            }
        }

        /// <summary>The legend's first line: the current mode and what the next press shows.</summary>
        private string ModeHeader()
        {
            return "Map view: " + ModeName(_mode) + "  (Ctrl+Shift+D: " + ModeName(NextMode(_mode)) + ")";
        }

        /// <summary>The mode line, then the shown layers' own lines.</summary>
        private void UpdateLegend()
        {
            if (_dataSource == null || _failure != null)
            {
                return;
            }
            string text = ModeHeader();
            if (ShowsTerrain)
            {
                text += "\n" + (_terrainLegend ?? "Terrain features: -");
            }
            if (ShowsDecisions)
            {
                text += "\n" + (_decisionLegend ?? "Decisions: -");
            }
            _dataSource.Legend = text;
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
            if (mission == null || _missionScreen == null)
            {
                _missionScreen = null;
                return;
            }
            _dataSource = new TerrainFeatureOverlayVM();
            _gauntletLayer = new GauntletLayer("RBMTerrainFeaturesLayer", 2);
            _missionScreen.AddLayer(_gauntletLayer);
            _gauntletLayer.LoadMovie("RBMTerrainFeatures", _dataSource);
            _dataSource.IsActive = true;
            _enabled = true;
            _failure = null;
            ResetBuild();
        }

        private void TurnOff()
        {
            _enabled = false;
            _mode = OverlayMode.Off;
            if (_gauntletLayer != null)
            {
                _missionScreen?.RemoveLayer(_gauntletLayer);
                _gauntletLayer = null;
            }
            _dataSource?.OnFinalize();
            _dataSource = null;
            _missionScreen = null;
            _failure = null;
            ResetBuild();
        }

        private void ResetBuild()
        {
            _built = false;
            _builtFor = null;
            _builtPlanAttacker = null;
            _builtPlanDefender = null;
            _builtSpotsAttacker = null;
            _builtSpotsDefender = null;
            _builtCavalryAttacker = null;
            _builtCavalryDefender = null;
            _builtChokeHold = null;
            _builtBattlePosition = null;
            _builtApproach = null;
            _legendBase = null;
            _terrainLegend = null;
            _projectTicks = 0;
            _projectFrames = 0;
            _costSince = float.NegativeInfinity;
            ResetDecisions();
            _labelPoints.Clear();
            _labelTexts.Clear();
            _dotPoints.Clear();
            _claimDotPoints.Clear();
            _claims.Clear();
            _holds.Clear();
            _useLabelPoints.Clear();
            _useLabelTexts.Clear();
            Array.Clear(_useSlotTexts, 0, _useSlotTexts.Length);
            Array.Clear(_useSlotColors, 0, _useSlotColors.Length);
            _cavalryDots = 0;
            _onScreen = 0;
        }

        private void Refresh()
        {
            Camera camera = _missionScreen != null ? _missionScreen.CombatCamera : null;
            if (Mission == null || camera == null || _dataSource == null || _gauntletLayer == null)
            {
                return;
            }
            BattleTerrain terrain = BattleTerrain.Current;
            if (ShowsTerrain)
            {
                RefreshTerrain(camera, terrain);
            }
            if (ShowsDecisions)
            {
                RefreshDecisions(camera, terrain);
            }
        }

        /// <summary>The terrain layer: rebuilt when a published snapshot / plan changes, projected every frame.</summary>
        private void RefreshTerrain(Camera camera, BattleTerrain terrain)
        {
            TerrainFlankPlan.SidePlan planAttacker = TerrainFlankPlan.Published(BattleSideEnum.Attacker);
            TerrainFlankPlan.SidePlan planDefender = TerrainFlankPlan.Published(BattleSideEnum.Defender);
            TerrainSpots.SidePlan spotsAttacker = TerrainSpots.Published(BattleSideEnum.Attacker);
            TerrainSpots.SidePlan spotsDefender = TerrainSpots.Published(BattleSideEnum.Defender);
            TerrainCavalry.SidePlan cavalryAttacker = TerrainCavalry.Published(BattleSideEnum.Attacker);
            TerrainCavalry.SidePlan cavalryDefender = TerrainCavalry.Published(BattleSideEnum.Defender);
            TerrainChokeHold.Plan chokeHold = TerrainChokeHold.Published;
            TerrainBattlePosition.Plan battlePosition = TerrainBattlePosition.Published;
            TerrainApproach.SidePlan approach = TerrainApproach.Published;
            if (!_built || terrain != _builtFor || planAttacker != _builtPlanAttacker || planDefender != _builtPlanDefender
                || spotsAttacker != _builtSpotsAttacker || spotsDefender != _builtSpotsDefender
                || cavalryAttacker != _builtCavalryAttacker || cavalryDefender != _builtCavalryDefender
                || chokeHold != _builtChokeHold || battlePosition != _builtBattlePosition
                || approach != _builtApproach)
            {
                _builtApproach = approach;
                _builtChokeHold = chokeHold;
                _builtBattlePosition = battlePosition;
                Build(terrain, planAttacker, planDefender, spotsAttacker, spotsDefender, cavalryAttacker, cavalryDefender);
            }
            BuildClaimLines(terrain);
            long startTicks = DateTime.UtcNow.Ticks;
            Project(camera);
            _projectTicks += DateTime.UtcNow.Ticks - startTicks;
            _projectFrames++;
            float now = Mission.CurrentTime;
            if (_legendBase != null && now - _costSince >= CostRefreshSeconds)
            {
                double ms = (_projectFrames > 0) ? (double)_projectTicks / TimeSpan.TicksPerMillisecond / _projectFrames : 0.0;
                _terrainLegend = _legendBase + "  | " + (_dotPoints.Count + _claimDotPoints.Count) + " dots (" + _cavalryDots + " cavalry, "
                    + _onScreen + " on screen), projection " + ms.ToString("0.00", CultureInfo.InvariantCulture) + " ms/frame";
                _projectTicks = 0;
                _projectFrames = 0;
                _costSince = now;
                UpdateLegend();
            }
        }

        /// <summary>Every frame: a dotted line from each formation holding an archer spot (cached average position) to the spot; claims from TerrainSpots.CopyClaims (thread-safe snapshot).</summary>
        private void BuildClaimLines(BattleTerrain terrain)
        {
            _claimDotPoints.Clear();
            _useLabelPoints.Clear();
            _useLabelTexts.Clear();
            if (terrain == null)
            {
                return;
            }
            BuildUseLines(terrain);
            TerrainSpots.CopyClaims(_claims);
            int lines = 0;
            foreach (KeyValuePair<Formation, TerrainSpots.Spot> claim in _claims)
            {
                if (lines >= MaxClaimLines)
                {
                    break;
                }
                Formation formation = claim.Key;
                Vec2 from = formation.CachedAveragePosition;
                Vec2 to = claim.Value.Data.Position;
                if (!from.IsValid)
                {
                    continue;
                }
                uint color = (formation.Team != null && formation.Team.Side == BattleSideEnum.Attacker) ? AttackerSpotColor : DefenderSpotColor;
                for (int d = 0; d < ClaimLineDots; d++)
                {
                    Vec3 world;
                    if (WorldPoint(terrain, from + (to - from) * ((d + 0.5f) / ClaimLineDots), out world))
                    {
                        _claimDotPoints.Add(new Point { World = world, Color = color });
                    }
                }
                lines++;
            }
        }

        /// <summary>Every frame: a dotted line from each formation riding to a cavalry lane (TerrainCavalry.CopyHolds) to that point, and a short label at the formation.</summary>
        private void BuildUseLines(BattleTerrain terrain)
        {
            TerrainCavalry.CopyHolds(_holds);
            int lines = 0;
            foreach (TerrainCavalry.Hold hold in _holds)
            {
                if (lines >= MaxUseLines)
                {
                    break;
                }
                Formation formation = hold.Formation;
                Vec2 from = formation.CachedAveragePosition;
                Vec2 to = hold.Position;
                if (!from.IsValid || !to.IsValid)
                {
                    continue;
                }
                uint color = (hold.Team != null && hold.Team.Side == BattleSideEnum.Attacker) ? AttackerCavalryColor : DefenderCavalryColor;
                for (int d = 0; d < ClaimLineDots; d++)
                {
                    Vec3 world;
                    if (WorldPoint(terrain, from + (to - from) * ((d + 0.5f) / ClaimLineDots), out world))
                    {
                        _claimDotPoints.Add(new Point { World = world, Color = color });
                    }
                }
                Vec3 at;
                if (WorldPoint(terrain, from, out at))
                {
                    _useLabelPoints.Add(new Point { World = at, Color = color });
                    _useLabelTexts.Add("lane " + hold.Id);
                }
                lines++;
            }
        }

        private void Project(Camera camera)
        {
            float inverseScale = _gauntletLayer.UIContext.InverseScale;
            float width = TaleWorlds.Engine.Screen.RealScreenResolutionWidth;
            float height = TaleWorlds.Engine.Screen.RealScreenResolutionHeight;
            int onScreen = 0;

            for (int i = 0; i < _labelPoints.Count; i++)
            {
                float sx, sy;
                TerrainFeatureLabelVM label = _dataSource.Labels[i];
                if (ToScreen(camera, _labelPoints[i].World, width, height, out sx, out sy))
                {
                    label.Place(sx * inverseScale - MarkerSize * 0.5f, sy * inverseScale - LabelHalfHeight);
                }
                else
                {
                    label.IsHidden = true;
                }
            }
            // the cavalry use labels use the label pool's tail (UseLabelCapacity)
            for (int j = 0; j < UseLabelCapacity; j++)
            {
                int i = _labelPoints.Count + j;
                if (i >= _dataSource.Labels.Count)
                {
                    break;
                }
                float sx, sy;
                TerrainFeatureLabelVM label = _dataSource.Labels[i];
                if (j < _useLabelPoints.Count && ToScreen(camera, _useLabelPoints[j].World, width, height, out sx, out sy))
                {
                    if (_useSlotTexts[j] != _useLabelTexts[j] || _useSlotColors[j] != _useLabelPoints[j].Color)
                    {
                        _useSlotTexts[j] = _useLabelTexts[j];
                        _useSlotColors[j] = _useLabelPoints[j].Color;
                        label.SetContent(_useLabelTexts[j], _useLabelPoints[j].Color);
                    }
                    label.Place(sx * inverseScale - MarkerSize * 0.5f, sy * inverseScale - LabelHalfHeight);
                }
                else
                {
                    label.IsHidden = true;
                }
            }
            for (int i = 0; i < _dotPoints.Count; i++)
            {
                float sx, sy;
                NavigableDebugDotVM dot = _dataSource.Dots[i];
                if (ToScreen(camera, _dotPoints[i].World, width, height, out sx, out sy))
                {
                    dot.Set(sx * inverseScale, sy * inverseScale, (_dotPoints[i].Size > 0f) ? _dotPoints[i].Size : DotSize, DotAlpha, _dotPoints[i].Color);
                    onScreen++;
                }
                else
                {
                    dot.IsHidden = true;
                }
            }
            // the claim / use lines use the dot pool's tail (DynamicDotCapacity)
            for (int j = 0; j < DynamicDotCapacity; j++)
            {
                int i = _dotPoints.Count + j;
                if (i >= _dataSource.Dots.Count)
                {
                    break;
                }
                float sx, sy;
                NavigableDebugDotVM dot = _dataSource.Dots[i];
                if (j < _claimDotPoints.Count && ToScreen(camera, _claimDotPoints[j].World, width, height, out sx, out sy))
                {
                    dot.Set(sx * inverseScale, sy * inverseScale, DotSize, DotAlpha, _claimDotPoints[j].Color);
                    onScreen++;
                }
                else
                {
                    dot.IsHidden = true;
                }
            }
            _onScreen = onScreen;
        }

        /// <summary>Screen pixels of a world point; false behind the camera or off screen.</summary>
        private static bool ToScreen(Camera camera, Vec3 world, float width, float height, out float sx, out float sy)
        {
            sx = 0f;
            sy = 0f;
            float sw = 0f;
            if (!world.IsValid)
            {
                return false;
            }
            MBWindowManager.WorldToScreen(camera, world, ref sx, ref sy, ref sw);
            return sw >= 0f && !float.IsNaN(sx) && !float.IsNaN(sy) && sx >= 0f && sy >= 0f && sx <= width && sy <= height;
        }

        private void Build(BattleTerrain terrain, TerrainFlankPlan.SidePlan planAttacker, TerrainFlankPlan.SidePlan planDefender,
            TerrainSpots.SidePlan spotsAttacker, TerrainSpots.SidePlan spotsDefender,
            TerrainCavalry.SidePlan cavalryAttacker, TerrainCavalry.SidePlan cavalryDefender)
        {
            _built = true;
            _builtFor = terrain;
            _builtPlanAttacker = planAttacker;
            _builtPlanDefender = planDefender;
            _builtSpotsAttacker = spotsAttacker;
            _builtSpotsDefender = spotsDefender;
            _builtCavalryAttacker = cavalryAttacker;
            _builtCavalryDefender = cavalryDefender;
            _legendBase = null;
            _labelPoints.Clear();
            _labelTexts.Clear();
            _dotPoints.Clear();
            _cavalryDots = 0;
            _approachDots = 0;
            Array.Clear(_useSlotTexts, 0, _useSlotTexts.Length);
            Array.Clear(_useSlotColors, 0, _useSlotColors.Length);

            if (terrain == null)
            {
                _dataSource.SetLabels(_labelTexts, _labelPoints.Count, null);
                _dataSource.SetDotCount(0);
                _terrainLegend = "Terrain features: no terrain snapshot yet (land field battles, a few frames after deployment)";
                UpdateLegend();
                return;
            }

            foreach (BattleTerrain.HillFeature hill in terrain.Hills)
            {
                AddLabel(terrain, hill.Position, HillColor, hill.Id + " first=" + ArmyName(hill.First));
            }
            foreach (BattleTerrain.CrestFeature crest in terrain.Crests)
            {
                AddLabel(terrain, crest.Position, CrestColor, crest.Id + " rise " + Fmt1(crest.Rise) + "m");
                float span = float.IsNaN(crest.Width) ? CrestDotDefaultSpan : Math.Min(crest.Width, CrestDotSpan);
                for (int d = 0; d < CrestDots; d++)
                {
                    float offset = span * ((float)d / (CrestDots - 1) - 0.5f);
                    AddDot(terrain, crest.Position + terrain.Left * offset, CrestColor);
                }
            }
            foreach (BattleTerrain.ChokeFeature choke in terrain.Chokes)
            {
                string text = choke.Id;
                if (choke.Type != BattleTerrain.ChokeType.Unknown)
                {
                    text += " (" + choke.Type.ToString().ToLowerInvariant() + ")";
                }
                text += " " + Fmt0(choke.Width) + "m (" + Fmt1(choke.Ratio) + "x)";
                if (choke.Sides == BattleTerrain.ChokeSides.Opposite)
                {
                    text += choke.Separates ? " crossing (only way)" : " crossing";
                }
                if (choke.OnRoute)
                {
                    text += " on route";
                }
                if (choke.ExitOf != BattleTerrain.ArmySet.None)
                {
                    text += " exitOf=" + ArmyName(choke.ExitOf);
                }
                AddLabel(terrain, choke.Position, ChokeColor, text);
            }
            foreach (BattleTerrain.OpenFieldFeature open in terrain.OpenFields)
            {
                AddLabel(terrain, open.Position, OpenColor, open.Id + " " + Area(open.Area) + " maxClear " + Fmt0(open.MaxClear) + "m");
            }
            // each side's line ends from its CURRENT flank plan (a re-plan's own), else the snapshot's
            AddFlanks(terrain, BattleTerrain.ArmySet.Attacker, planAttacker);
            AddFlanks(terrain, BattleTerrain.ArmySet.Defender, planDefender);
            foreach (BattleTerrain.ObstacleFeature woods in terrain.Woods)
            {
                AddLabel(terrain, woods.Position, WoodsColor, ObstacleText(woods));
            }
            foreach (BattleTerrain.ObstacleFeature rough in terrain.Rough)
            {
                AddLabel(terrain, rough.Position, RoughColor, ObstacleText(rough));
            }
            foreach (BattleTerrain.WaterFeature water in terrain.Waters)
            {
                string text = water.Id + " " + Area(water.Area);
                if (water.CrossesAxis)
                {
                    text += " crosses axis";
                }
                else if (water.TouchesField)
                {
                    text += " in field";
                }
                AddLabel(terrain, water.Position, WaterColor, text);
            }
            foreach (BattleTerrain.FordFeature ford in terrain.Fords)
            {
                string text = ford.Id;
                if (ford.OnPath)
                {
                    text += " on path";
                }
                if (ford.AtDeepWater)
                {
                    text += " at deep water";
                }
                AddLabel(terrain, ford.Position, FordColor, text);
            }
            AddArmy(terrain, terrain.Attacker, true, planAttacker);
            AddArmy(terrain, terrain.Defender, false, planDefender);
            AddSpots(terrain, spotsAttacker, AttackerSpotColor);
            AddSpots(terrain, spotsDefender, DefenderSpotColor);
            AddOutlines(terrain, Replanned(spotsAttacker), Replanned(spotsDefender));
            // after the outlines, with a dot budget of their own
            AddCavalry(terrain, cavalryAttacker, AttackerCavalryColor);
            AddCavalry(terrain, cavalryDefender, DefenderCavalryColor);
            // defender's choke hold
            AddChokeHold(terrain, _builtChokeHold);
            // defender's battle position
            AddBattlePosition(terrain, _builtBattlePosition);
            // attacker's approach route, own budget
            AddApproach(terrain, _builtApproach);

            // label pool tail: placeholders for the cavalry use labels (set every frame)
            List<uint> colors = _labelPoints.ConvertAll(p => p.Color);
            List<string> texts = new List<string>(_labelTexts);
            for (int j = 0; j < UseLabelCapacity; j++)
            {
                texts.Add("");
                colors.Add(0xFFFFFFFFu);
            }
            _dataSource.SetLabels(texts, texts.Count, colors);
            // dot pool tail: the claim / use lines (set every frame)
            _dataSource.SetDotCount(_dotPoints.Count + DynamicDotCapacity);

            _legendBase = "Terrain features: scene " + terrain.Scene
                + "  " + _labelPoints.Count + " labels"
                + ((spotsAttacker != null || spotsDefender != null) && !RBMConfig.RBMConfig.terrainTacticsEnabled ? "  (archer spots not used: terrain tactics off)" : "")
                + ((cavalryAttacker != null || cavalryDefender != null) && !RBMConfig.RBMConfig.terrainTacticsEnabled ? "  (cavalry ground not used: terrain tactics off)" : "")
                + (terrain.Cavalry.Error != null ? "  cavalry errors: " + terrain.Cavalry.Error : "")
                + (terrain.FeatureMap.Error != null ? "  feature errors: " + terrain.FeatureMap.Error : "")
                + (terrain.Outlines.Error != null ? "  outline errors: " + terrain.Outlines.Error : "");
            _terrainLegend = _legendBase;
            UpdateLegend();
        }

        private void AddFlanks(BattleTerrain terrain, BattleTerrain.ArmySet army, TerrainFlankPlan.SidePlan plan)
        {
            IEnumerable<BattleTerrain.FlankFeature> flanks = (plan != null && plan.Flanks != null) ? plan.Flanks : terrain.Flanks;
            foreach (BattleTerrain.FlankFeature flank in flanks)
            {
                if (flank != null && flank.Army == army && flank.Position.IsValid)
                {
                    AddFlank(terrain, flank, plan);
                }
            }
        }

        private void AddFlank(BattleTerrain terrain, BattleTerrain.FlankFeature flank, TerrainFlankPlan.SidePlan plan)
        {
            string text = (flank.Army == BattleTerrain.ArmySet.Attacker ? "ATK " : "DEF ") + (flank.OwnLeft ? "left: " : "right: ");
            uint color;
            if (plan == null)
            {
                color = UnplannedFlankColor;
                text += "end " + flank.End.ToString().ToLowerInvariant();
            }
            else if (flank.OwnLeft ? plan.OwnLeftCovered : plan.OwnRightCovered)
            {
                color = CoveredColor;
                text += "COVERED (" + (flank.OwnLeft ? plan.OwnLeftReason : plan.OwnRightReason) + ")";
            }
            else
            {
                color = ExposedColor;
                text += "OPEN (" + (flank.OwnLeft ? plan.OwnLeftReason : plan.OwnRightReason) + ")";
            }
            text += ", " + EnemyRouteText(flank.EnemyRoute);
            if (plan != null && plan.Version > 1)
            {
                text += " v" + plan.Version.ToString(CultureInfo.InvariantCulture) + "(" + plan.Note + ")";
            }
            AddLabel(terrain, flank.Position, color, text);
        }

        private void AddArmy(BattleTerrain terrain, BattleTerrain.SideInfo side, bool attacker, TerrainFlankPlan.SidePlan plan)
        {
            if (side == null || side.Men <= 0)
            {
                return;
            }
            uint color = attacker ? AttackerColor : DefenderColor;
            string text = (attacker ? "ATTACKER" : "DEFENDER") + "  attack side: ";
            Vec2 sideDir = Vec2.Zero;
            // a re-planned flank plan carries the axis and army centre it was made from; the publish's are the snapshot's
            Vec2 axisForward = (plan != null && plan.Forward.IsValid) ? plan.Forward : terrain.Forward;
            Vec2 axisLeft = (plan != null && plan.Forward.IsValid) ? new Vec2(-plan.Forward.y, plan.Forward.x) : terrain.Left;
            if (plan == null)
            {
                text += "- (no flank plan)";
            }
            else if (plan.HasAttackSide)
            {
                bool left = plan.AttackSide == FormationAI.BehaviorSide.Left;
                text += (left ? "left" : "right") + " (" + plan.AttackReason + ")";
                // own left: the attacker's is BattleTerrain.Left, the defender's the opposite
                sideDir = (left == attacker) ? axisLeft : -axisLeft;
            }
            else
            {
                text += "none (" + plan.AttackReason + ")";
            }
            if (plan != null && plan.Version > 1)
            {
                text += "  v" + plan.Version.ToString(CultureInfo.InvariantCulture) + "(" + plan.Note + ")";
            }

            Vec3 centroid = side.Centroid;
            if (plan != null && plan.Centre.IsValid)
            {
                float h = terrain.SampleHeight(plan.Centre);
                if (float.IsNaN(h) || float.IsInfinity(h))
                {
                    h = centroid.z;
                }
                centroid = new Vec3(plan.Centre, h, -1f);
            }
            Vec3 at = new Vec3(centroid.x, centroid.y, centroid.z + ArmyLift, -1f);
            _labelPoints.Add(new Point { World = at, Color = color });
            _labelTexts.Add(text);

            if (sideDir.LengthSquared > 0.0001f)
            {
                Vec2 forward = attacker ? axisForward : -axisForward;
                Vec2 dir = sideDir + forward * 0.5f;
                dir.Normalize();
                Vec2 origin = centroid.AsVec2;
                for (int d = 1; d <= ArrowDots; d++)
                {
                    AddDot(terrain, origin + dir * (ArrowDotSpacing * d), color);
                }
            }
        }

        /// <summary>The snapshot's area outline dots, coloured by kind. The read's archer-spot circles of a side whose spots were re-planned are left out (AddSpots draws the current version's; spot ids start with the side's letter).</summary>
        private void AddOutlines(BattleTerrain terrain, bool attackerSpotsReplanned, bool defenderSpotsReplanned)
        {
            foreach (BattleTerrain.OutlineDot dot in terrain.Outlines.Dots)
            {
                if (!dot.Position.IsValid)
                {
                    continue;
                }
                if (dot.Kind == BattleTerrain.OutlineKind.Spot && !string.IsNullOrEmpty(dot.FeatureId)
                    && ((dot.FeatureId[0] == 'A' && attackerSpotsReplanned) || (dot.FeatureId[0] == 'D' && defenderSpotsReplanned)))
                {
                    continue;
                }
                _dotPoints.Add(new Point { World = dot.Position, Color = OutlineColor(dot.Kind), Size = OutlineDotSize });
            }
        }

        private static uint OutlineColor(BattleTerrain.OutlineKind kind)
        {
            switch (kind)
            {
                case BattleTerrain.OutlineKind.OpenField:
                    return OutlineOpenColor;
                case BattleTerrain.OutlineKind.Woods:
                    return OutlineWoodsColor;
                case BattleTerrain.OutlineKind.Rough:
                    return OutlineRoughColor;
                case BattleTerrain.OutlineKind.Water:
                    return OutlineWaterColor;
                case BattleTerrain.OutlineKind.Hilltop:
                    return OutlineHilltopColor;
                case BattleTerrain.OutlineKind.Choke:
                    return OutlineChokeColor;
                default:
                    return OutlineSpotColor;
            }
        }

        /// <summary>One side's charge lanes: dots start to aim with a label at the start. Dots stop at CavalryMaxDots (both sides together); labels are always added.</summary>
        private void AddCavalry(BattleTerrain terrain, TerrainCavalry.SidePlan plan, uint color)
        {
            if (plan == null)
            {
                return;
            }
            // a re-plan's version on every label of the side
            string version = (plan.Version > 1) ? " v" + plan.Version.ToString(CultureInfo.InvariantCulture) : "";
            foreach (TerrainCavalry.Lane lane in plan.Lanes)
            {
                BattleTerrain.ChargeLane data = lane.Data;
                AddLabel(terrain, data.Start, color, "lane " + data.Id + " " + Fmt0(data.Length) + "m drop" + Signed(data.Drop)
                    + (data.Bumps > 0 ? " bumps " + data.Bumps : "") + version);
                int count = Math.Max(2, (int)Math.Ceiling(data.Length / CavalryLaneDotSpacing) + 1);
                for (int d = 0; d < count; d++)
                {
                    AddCavalryDot(terrain, data.Start + (data.Aim - data.Start) * ((float)d / (count - 1)), color);
                }
            }
        }

        /// <summary>The defender's choke hold: a dotted line across the gap at the hold point as wide as the hold width (max ChokeHoldMaxDots), labelled with its width, plus labels at the archer place and cavalry reserve when they exist.</summary>
        private void AddChokeHold(BattleTerrain terrain, TerrainChokeHold.Plan plan)
        {
            if (plan == null)
            {
                return;
            }
            BattleTerrain.ChokeHoldPlace data = plan.Data;
            float width = float.IsNaN(data.HoldWidth) ? 0f : data.HoldWidth;
            int count = Math.Min(ChokeHoldMaxDots, Math.Max(2, (int)Math.Ceiling(width / ChokeHoldDotSpacing) + 1));
            for (int d = 0; d < count; d++)
            {
                float offset = width * ((float)d / (count - 1) - 0.5f);
                Vec3 world;
                if (WorldPoint(terrain, data.Hold + data.LineDirection * offset, out world))
                {
                    _dotPoints.Add(new Point { World = world, Color = ChokeHoldColor, Size = OutlineDotSize });
                }
            }
            AddLabel(terrain, data.Hold, ChokeHoldColor, "choke hold (def) " + Fmt0(data.HoldWidth) + "m");
            if (plan.HasArcher)
            {
                AddLabel(terrain, data.Archer, ChokeHoldColor, "choke archers (def) " + data.ArcherSource);
            }
            if (plan.HasReserve && data.BothFlanksCovered)
            {
                AddLabel(terrain, data.Reserve, ChokeHoldColor, "choke reserve (def)");
            }
        }

        /// <summary>The defender's battle position (current version): a dotted line across the facing at the hold point (max BattlePositionMaxDots) labelled with the score gain over the current line, runners-up as small labels, labels at the archer place and (both ends covered) cavalry reserve, in lime.</summary>
        private void AddBattlePosition(BattleTerrain terrain, TerrainBattlePosition.Plan plan)
        {
            if (plan == null)
            {
                return;
            }
            BattleTerrain.PositionCandidate data = plan.Data;
            float width = float.IsNaN(data.LineWidth) ? 0f : data.LineWidth;
            int count = Math.Min(BattlePositionMaxDots, Math.Max(2, (int)Math.Ceiling(width / BattlePositionDotSpacing) + 1));
            for (int d = 0; d < count; d++)
            {
                float offset = width * ((float)d / (count - 1) - 0.5f);
                Vec3 world;
                if (WorldPoint(terrain, data.Hold + data.LineDirection * offset, out world))
                {
                    _dotPoints.Add(new Point { World = world, Color = BattlePositionColor, Size = OutlineDotSize });
                }
            }
            float gain = plan.Result != null ? plan.Result.Gain : float.NaN;
            string text = "battle position (" + TerrainBattlePosition.CandidateName(data) + ") "
                + (float.IsNaN(gain) ? "?" : ((gain >= 0f ? "+" : "") + gain.ToString("0.00", CultureInfo.InvariantCulture)))
                + " " + Fmt0(data.LineWidth) + "m" + (plan.IsStay ? " stay" : "") + (plan.Version > 1 ? " v" + plan.Version : "")
                + (RBMConfig.RBMConfig.terrainTacticsEnabled ? "" : " (not used: terrain tactics off)");
            AddLabel(terrain, data.Hold, BattlePositionColor, text);
            if (plan.Result != null)
            {
                foreach (BattleTerrain.PositionCandidate alt in plan.Result.RunnersUp)
                {
                    if (alt == data)
                    {
                        continue;
                    }
                    AddLabel(terrain, alt.Hold, BattlePositionAltColor, "alt " + TerrainBattlePosition.CandidateName(alt) + " " + alt.Total.ToString("0.00", CultureInfo.InvariantCulture));
                }
            }
            if (plan.HasArcher)
            {
                AddLabel(terrain, data.Archer, BattlePositionColor, "position archers " + (data.ArcherSource ?? "-"));
            }
            if (plan.HasReserve && plan.BothFlanksCovered)
            {
                AddLabel(terrain, data.Reserve, BattlePositionColor, "position reserve");
            }
            // infantry reserve plug point and a dotted line from it to the line's middle
            BattleTerrain.PositionCandidate plugData = plan.PlugData ?? data;
            if (plugData.Plug.IsValid)
            {
                AddLabel(terrain, plugData.Plug, BattlePositionPlugColor, "reserve (" + (plugData.PlugHidden ? "hidden" : "seen") + " " + (plugData.PlugTier ?? "-") + ")"
                    + (plan.HasPlug ? "" : " dropped"));
                float length = plugData.Plug.Distance(data.Hold);
                int dots = Math.Min(PlugLineMaxDots, Math.Max(2, (int)(length / PlugLineDotSpacing)));
                for (int d = 1; d < dots; d++)
                {
                    Vec3 world;
                    if (WorldPoint(terrain, plugData.Plug + (data.Hold - plugData.Plug) * ((float)d / dots), out world))
                    {
                        _dotPoints.Add(new Point { World = world, Color = BattlePositionPlugColor, Size = OutlineDotSize });
                    }
                }
            }
        }

        /// <summary>The attacker's approach route (current version): dots from its start along the waypoints (max ApproachMaxDots), a label at the deploy point; a straight plan gets only a label at the line.</summary>
        private void AddApproach(BattleTerrain terrain, TerrainApproach.SidePlan plan)
        {
            if (plan == null)
            {
                return;
            }
            string off = RBMConfig.RBMConfig.terrainTacticsEnabled ? "" : " (not used: terrain tactics off)";
            if (!plan.IsRoute)
            {
                if (plan.Result != null && plan.Result.Line.IsValid)
                {
                    AddLabel(terrain, plan.Result.Line + plan.Result.Facing * BattleTerrainRead.ApproachDeployGap, ApproachColor,
                        "approach (straight) v" + plan.Version + " " + (plan.Why ?? "-") + off);
                }
                return;
            }
            Vec2 previous = plan.From;
            foreach (TerrainApproach.Waypoint w in plan.Waypoints)
            {
                Vec2 p = w.Position;
                if (previous.IsValid)
                {
                    float length = previous.Distance(p);
                    int count = Math.Max(1, (int)Math.Ceiling(length / ApproachDotSpacing));
                    for (int d = 1; d <= count && _approachDots < ApproachMaxDots; d++)
                    {
                        Vec3 world;
                        if (WorldPoint(terrain, previous + (p - previous) * ((float)d / count), out world))
                        {
                            _dotPoints.Add(new Point { World = world, Color = ApproachColor, Size = OutlineDotSize });
                            _approachDots++;
                        }
                    }
                }
                previous = p;
            }
            BattleTerrain.ApproachCandidate chosen = (plan.Result != null) ? plan.Result.Chosen : null;
            AddLabel(terrain, plan.Waypoints[plan.Waypoints.Count - 1].Position, ApproachColor, "approach (" + plan.KindName + ") v" + plan.Version
                + ((chosen != null && !float.IsNaN(chosen.Gain)) ? " gain " + chosen.Gain.ToString("0.00", CultureInfo.InvariantCulture) : "") + off);
        }

        private void AddCavalryDot(BattleTerrain terrain, Vec2 position, uint color)
        {
            Vec3 world;
            if (_cavalryDots >= CavalryMaxDots || !WorldPoint(terrain, position, out world))
            {
                return;
            }
            _dotPoints.Add(new Point { World = world, Color = color, Size = OutlineDotSize });
            _cavalryDots++;
        }

        /// <summary>One label per usable archer spot (+ " v3" once re-planned). A re-planned side's spots also get their room circle here (the snapshot's outline circles are the read's spots).</summary>
        private void AddSpots(BattleTerrain terrain, TerrainSpots.SidePlan plan, uint color)
        {
            if (plan == null)
            {
                return;
            }
            bool replanned = Replanned(plan);
            string version = replanned ? " v" + plan.Version.ToString(CultureInfo.InvariantCulture) : "";
            foreach (TerrainSpots.Spot spot in plan.Spots)
            {
                BattleTerrain.ArcherSpot data = spot.Data;
                AddLabel(terrain, data.Position, color,
                    "archer spot " + data.Id + " " + BattleTerrain.SpotKindName(data.Kind) + " " + Signed(data.Gain) + "m fire " + Percent(data.Fire) + version);
                if (!replanned)
                {
                    continue;
                }
                float radius = Math.Min(BattleTerrainRead.SpotFootprintMax,
                    Math.Max(BattleTerrainRead.SpotMinClear, float.IsNaN(data.Clearance) ? BattleTerrainRead.SpotMinClear : data.Clearance));
                int count = Math.Max(8, (int)Math.Ceiling(2.0 * Math.PI * radius / BattleTerrainRead.OutlineDotSpacing));
                for (int d = 0; d < count; d++)
                {
                    double angle = 2.0 * Math.PI * d / count;
                    Vec3 world;
                    if (WorldPoint(terrain, data.Position + new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius, out world))
                    {
                        _dotPoints.Add(new Point { World = world, Color = OutlineSpotColor, Size = OutlineDotSize });
                    }
                }
            }
        }

        /// <summary>A spot plan of a re-plan (version 2+): its labels carry the version and it draws its own room circles.</summary>
        private static bool Replanned(TerrainSpots.SidePlan plan)
        {
            return plan != null && plan.Version > 1;
        }

        private void AddLabel(BattleTerrain terrain, Vec2 position, uint color, string text)
        {
            Vec3 world;
            if (!WorldPoint(terrain, position, out world))
            {
                return;
            }
            _labelPoints.Add(new Point { World = world, Color = color });
            _labelTexts.Add(text);
        }

        private void AddDot(BattleTerrain terrain, Vec2 position, uint color)
        {
            Vec3 world;
            if (!WorldPoint(terrain, position, out world))
            {
                return;
            }
            _dotPoints.Add(new Point { World = world, Color = color });
        }

        /// <summary>The grid's terrain height under p (bilinear, else the cell's) + LabelLift; false outside the map.</summary>
        private static bool WorldPoint(BattleTerrain terrain, Vec2 position, out Vec3 world)
        {
            float z = terrain.SampleHeight(position);
            if (float.IsNaN(z) || float.IsInfinity(z))
            {
                z = terrain.HeightAt(position);
            }
            if (float.IsNaN(z) || float.IsInfinity(z))
            {
                world = Vec3.Invalid;
                return false;
            }
            world = new Vec3(position.x, position.y, z + LabelLift, -1f);
            return true;
        }

        // text helpers (also used by BattleStatsLogic's flank / map lines)
        /// <summary>The flank feature of one army's own left / right, or null.</summary>
        internal static BattleTerrain.FlankFeature FindFlank(BattleTerrain terrain, BattleTerrain.ArmySet army, bool ownLeft)
        {
            if (terrain == null)
            {
                return null;
            }
            foreach (BattleTerrain.FlankFeature flank in terrain.Flanks)
            {
                if (flank != null && flank.Army == army && flank.OwnLeft == ownLeft)
                {
                    return flank;
                }
            }
            return null;
        }

        private static string EnemyRouteText(BattleTerrain.FlankRoute route)
        {
            if (route == null)
            {
                return "enemy route not measured";
            }
            return route.Reachable ? "enemy route " + Fmt0(route.Seconds) + "s" : "no enemy route";
        }

        private static string ObstacleText(BattleTerrain.ObstacleFeature obstacle)
        {
            string text = obstacle.Id;
            if (obstacle.Cover == BattleTerrain.CoverType.Steep)
            {
                return text + " steep " + Fmt0(obstacle.MeanSlope) + "deg";
            }
            if (obstacle.Kind == BattleTerrain.FeatureKind.Rough && obstacle.Cover != BattleTerrain.CoverType.Unknown)
            {
                text += " " + ((obstacle.Cover == BattleTerrain.CoverType.Difficult) ? "difficult (map)" : obstacle.Cover.ToString().ToLowerInvariant());
            }
            else if (obstacle.Kind == BattleTerrain.FeatureKind.Woods && obstacle.Source != BattleTerrain.AreaSource.None)
            {
                text += " " + obstacle.Source.ToString().ToLowerInvariant();
            }
            return text + " " + Area(obstacle.Area);
        }

        private static string ArmyName(BattleTerrain.ArmySet army)
        {
            switch (army)
            {
                case BattleTerrain.ArmySet.Attacker:
                    return "atk";
                case BattleTerrain.ArmySet.Defender:
                    return "def";
                case BattleTerrain.ArmySet.Both:
                    return "both";
                default:
                    return "-";
            }
        }

        /// <summary>m² as "235k m2" from 10,000 m² up, else "850 m2"; "-" for NaN.</summary>
        internal static string Area(float squareMetres)
        {
            if (float.IsNaN(squareMetres) || float.IsInfinity(squareMetres))
            {
                return "- m2";
            }
            return squareMetres >= 10000f
                ? (squareMetres / 1000f).ToString("0", CultureInfo.InvariantCulture) + "k m2"
                : squareMetres.ToString("0", CultureInfo.InvariantCulture) + " m2";
        }

        private static string Signed(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "?";
            }
            return (value >= 0f ? "+" : "") + value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Percent(float share)
        {
            return (float.IsNaN(share) || float.IsInfinity(share)) ? "-" : (share * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        internal static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        internal static string Fmt1(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The pools bound by RBMTerrainFeatures.xml: labels (marker + text) and dots. Sized and filled once per snapshot; per frame only positions / hidden flags change.</summary>
    public class TerrainFeatureOverlayVM : ViewModel
    {
        private readonly MBBindingList<TerrainFeatureLabelVM> _labels = new MBBindingList<TerrainFeatureLabelVM>();
        private readonly MBBindingList<NavigableDebugDotVM> _dots = new MBBindingList<NavigableDebugDotVM>();
        private readonly MBBindingList<TerrainFeatureLabelVM> _decisionLabels = new MBBindingList<TerrainFeatureLabelVM>();
        private readonly MBBindingList<NavigableDebugDotVM> _decisionDots = new MBBindingList<NavigableDebugDotVM>();
        private bool _isActive;
        private string _legend = "";

        [DataSourceProperty]
        public MBBindingList<TerrainFeatureLabelVM> Labels => _labels;

        [DataSourceProperty]
        public MBBindingList<NavigableDebugDotVM> Dots => _dots;

        /// <summary>The decision layer's own pools (formation / team labels, order-line dots), sized once to their capacity.</summary>
        [DataSourceProperty]
        public MBBindingList<TerrainFeatureLabelVM> DecisionLabels => _decisionLabels;

        [DataSourceProperty]
        public MBBindingList<NavigableDebugDotVM> DecisionDots => _decisionDots;

        [DataSourceProperty]
        public bool IsActive
        {
            get { return _isActive; }
            set { if (_isActive != value) { _isActive = value; OnPropertyChangedWithValue(value, "IsActive"); } }
        }

        [DataSourceProperty]
        public string Legend
        {
            get { return _legend; }
            set { if (_legend != value) { _legend = value; OnPropertyChangedWithValue(value, "Legend"); } }
        }

        /// <summary>Resizes the label pool to count at the tail and sets each label's text / colour; all start hidden.</summary>
        internal void SetLabels(IList<string> texts, int count, IList<uint> colors)
        {
            while (_labels.Count > count)
            {
                _labels.RemoveAt(_labels.Count - 1);
            }
            while (_labels.Count < count)
            {
                _labels.Add(new TerrainFeatureLabelVM());
            }
            for (int i = 0; i < count; i++)
            {
                _labels[i].SetContent(texts[i], colors[i]);
                _labels[i].IsHidden = true;
            }
        }

        /// <summary>Resizes the dot pool to count at the tail; all start hidden.</summary>
        internal void SetDotCount(int count)
        {
            while (_dots.Count > count)
            {
                _dots.RemoveAt(_dots.Count - 1);
            }
            while (_dots.Count < count)
            {
                _dots.Add(new NavigableDebugDotVM());
            }
            for (int i = 0; i < count; i++)
            {
                _dots[i].IsHidden = true;
            }
        }

        /// <summary>Grows the decision pools to their capacity (once; they are never shrunk); new items start hidden.</summary>
        internal void EnsureDecisionPools(int labels, int dots)
        {
            while (_decisionLabels.Count < labels)
            {
                _decisionLabels.Add(new TerrainFeatureLabelVM());
            }
            while (_decisionDots.Count < dots)
            {
                _decisionDots.Add(new NavigableDebugDotVM());
            }
        }

        /// <summary>Hides the terrain layer's labels and dots (the decisions-only mode stops projecting them).</summary>
        public void HideTerrain()
        {
            foreach (TerrainFeatureLabelVM label in _labels)
            {
                label.IsHidden = true;
            }
            foreach (NavigableDebugDotVM dot in _dots)
            {
                dot.IsHidden = true;
            }
        }

        public void HideAll()
        {
            HideTerrain();
            foreach (TerrainFeatureLabelVM label in _decisionLabels)
            {
                label.IsHidden = true;
            }
            foreach (NavigableDebugDotVM dot in _decisionDots)
            {
                dot.IsHidden = true;
            }
        }
    }

    /// <summary>One feature label: a marker dot and a text on a dark band, placed by its top-left corner.</summary>
    public class TerrainFeatureLabelVM : ViewModel
    {
        private float _screenX;
        private float _screenY;
        private string _text = "";
        private string _textColor = "#FFFFFFFF";
        private Color _dotColor = Color.FromUint(0xFFFFFFFFu);
        private bool _isHidden = true;

        internal void SetContent(string text, uint argb)
        {
            Text = text;
            DotColor = Color.FromUint(argb);
            // "#RRGGBBAA": the form Color.ConvertStringToColor reads (Brush.FontColor is bound to a string)
            TextColor = "#" + ((argb >> 16) & 0xFF).ToString("X2") + ((argb >> 8) & 0xFF).ToString("X2") + (argb & 0xFF).ToString("X2") + "FF";
        }

        internal void Place(float x, float y)
        {
            ScreenX = x;
            ScreenY = y;
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
        public string Text
        {
            get { return _text; }
            set { if (_text != value) { _text = value; OnPropertyChangedWithValue(value, "Text"); } }
        }

        [DataSourceProperty]
        public string TextColor
        {
            get { return _textColor; }
            set { if (_textColor != value) { _textColor = value; OnPropertyChangedWithValue(value, "TextColor"); } }
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
