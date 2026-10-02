using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;
using MissileBallistics = RBMConfig.MissileBallistics;

namespace RBMCombat
{
    /// <summary>
    /// Player aim assist: while the main agent is drawing a bow or crossbow, winding up a sling or readying a javelin,
    /// throwing axe/knife or stone (action channel 1 is ReadyRanged, the same test vanilla's crosshair uses), draws the predicted flight as screen-space dots plus an
    /// impact marker. Gated on RBMConfig.rangedAimArcEnabled; RBM's SubModule does not even add it when that is off.
    ///
    /// Also the prediction source for the experimental RangedAimCamera, which is on whenever the arc is (same toggle):
    /// the camera reads the latest landing point through TryGetCameraTarget.
    ///
    /// The prediction mirrors the real shot: RBMCombat's shot prefix (RangedRework.OverrideOnAgentShootMissile) speed
    /// and Realistic Arrow Arc pitch when RBMCombat is on, the launcher's own missile speed when it is off, the ammo's
    /// air friction, and MissileBallistics' integrator (the one verified against real flight). The launch origin and
    /// direction are a model (eye + look direction); with the battle hit log on, every player shot writes an ARCSHOT
    /// line comparing that model with the real launch (and, with RBMCombat on, an ARCLAND line for where it came down).
    ///
    /// Drawing technique as FrontlineDebugOverlay: Gauntlet widgets placed with MBWindowManager.WorldToScreen
    /// (MBDebug.RenderDebug* is compiled out of Release). Runs on the main thread from OnMissionTick, which the mission
    /// ticks after the camera update, so the dots use this frame's camera.
    /// </summary>
    public class RangedAimArcView : MissionView
    {
        private const int DotCount = 36;
        // Sampled segment length in flight time; one scene raycast per segment. 0.1 s is ~7 m at bow speed, where the
        // chord of the arc sags a centimetre from the curve.
        private const float SampleSeconds = 0.1f;
        private const float MaxFlightSeconds = 6f;
        private const float MaxDrop = 150f;
        private static readonly int StepsPerSample = Math.Max(1, (int)Math.Round(SampleSeconds / MissileBallistics.StepSeconds));
        private static readonly int SampleCapacity = (int)(MaxFlightSeconds / SampleSeconds) + 2;

        // First dot this far along the arc (keeps the bow itself clear), dots no closer together than MinDotSpacing.
        private const float FirstDotDistance = 2f;
        private const float MinDotSpacing = 1f;

        // Launch origin relative to GetEyeGlobalPosition, in the look frame (metres). Zero until calibrated from the
        // ARCSHOT dFwd/dRight/dUp columns.
        private const float OriginForward = 0f;
        private const float OriginRight = 0f;
        private const float OriginUp = 0f;
        // The same for javelins, throwing axes/knives and stones, which leave from the hand: measured 2026-10-01 from 11
        // throwing-axe ARCSHOT lines, dFwd 0.96-1.08 (1.05 typical), dRight ~0, dUp -0.05..-0.21 (-0.08 typical); speed
        // and direction matched exactly. Javelins not yet measured separately.
        private const float ThrowOriginForward = 1.05f;
        private const float ThrowOriginRight = 0f;
        private const float ThrowOriginUp = -0.08f;
        // Seconds from the end of the aim (ReadyRanged) to the throw leaving the hand: ARCSHOT's age on every throw.
        private const float ThrowReleaseDelay = 0.4f;

        // RangedRework.OverrideOnAgentShootMissile's Realistic Arrow Arc pitch-up for the player's bow/crossbow.
        private const double RealisticArrowArcRadians = 0.083141f;

        private const uint DotColor = 0xFF000000u;
        private const uint RingColor = 0xFF000000u;
        private const uint ImpactColor = 0xFFFFB066u;
        private const uint EnemyImpactColor = 0xFFE01E1Eu;
        private const uint FriendlyImpactColor = 0xFF32D25Au;

        // Accuracy ring on the ground around the landing point: dot count and look, radius limits across the shot
        // (metres), how far it may stretch along a shallow shot, and how far above the impact the ground is probed from.
        private const int RingDotCount = 24;
        private const float RingDotSize = 5f;
        private const float RingDotAlpha = 0.8f;
        private const float RingMinRadius = 0.3f;
        private const float RingMaxRadius = 40f;
        private const float RingMaxStretch = 4f;
        private const float RingGroundProbeHeight = 1.5f;

        // Limb raycast thickness, roughly an arrow's.
        private const float AgentRayThickness = 0.02f;

        private RangedAimArcVM _dataSource;
        private GauntletLayer _gauntletLayer;
        private MissionScreen _missionScreen;

        private readonly Vec3[] _samples = new Vec3[SampleCapacity];
        private MissileBallistics.TrajectorySegmentTest _segmentTest;

        // The last prediction shown, for the ARCSHOT calibration line.
        private bool _hasPrediction;
        private float _predictionTime;
        private Vec3 _predOrigin;
        private Vec3 _predVelocity;
        private float _predFriction;
        private Vec3 _predImpact;
        private bool _predLanded;
        // The agent the arc ends on, null when it ends on terrain/objects (or not at all).
        private Agent _predHitAgent;

        // Where the aim camera should keep in frame: where the flight comes down to the terrain, or to the shooter's own
        // ground level when there is no terrain to meet. Unlike _hasPrediction (kept past the release for ARCSHOT),
        // cleared the moment the arc is not being predicted.
        private bool _cameraTargetValid;
        private float _cameraTargetTime;
        private Vec3 _cameraTarget;
        private const float CameraTargetMaxAge = 0.25f;

        // The camera's flight: the same one carried on through units and scene objects (no raycasts) until it meets the
        // terrain heightmap, at the arc's own sample spacing (one heightmap lookup per sample).
        private const float CameraMaxFlightSeconds = 20f;
        private readonly Vec3[] _terrainSamples = new Vec3[(int)(CameraMaxFlightSeconds / SampleSeconds) + 2];
        private MissileBallistics.TrajectorySegmentTest _terrainTest;

        // Camera target when the flight meets no terrain either (past the map edge, or a terrain hole): carried on until
        // it comes back down to the shooter's own ground level. Coarser samples, since only the crossing point is used.
        private const float FallbackSampleSeconds = 0.5f;
        private static readonly int FallbackStepsPerSample = Math.Max(1, (int)Math.Round(FallbackSampleSeconds / MissileBallistics.StepSeconds));
        private readonly Vec3[] _fallbackSamples = new Vec3[(int)(CameraMaxFlightSeconds / FallbackSampleSeconds) + 2];
        private MissileBallistics.TrajectorySegmentTest _groundPlaneTest;
        private float _groundPlaneZ;

        private bool _drawArc;
        private bool _cameraEnabled;

        public override void AfterStart()
        {
            base.AfterStart();
            _missionScreen = MissionScreen ?? ScreenManager.TopScreen as MissionScreen;
            if (_missionScreen == null)
            {
                return;
            }
            _segmentTest = SegmentHitsScene;
            _terrainTest = SegmentCrossesTerrain;
            _groundPlaneTest = SegmentCrossesGroundPlane;
            _drawArc = RBMConfig.RBMConfig.rangedAimArcEnabled;
            if (_drawArc)
            {
                _dataSource = new RangedAimArcVM(DotCount + RingDotCount);
                _gauntletLayer = new GauntletLayer("RBMRangedAimArcLayer", 1);
                _missionScreen.AddLayer(_gauntletLayer);
                _gauntletLayer.LoadMovie("RBMRangedAimArc", _dataSource);
            }
            if (RBMConfig.RBMConfig.rangedAimArcEnabled)
            {
                RangedAimCamera.Enable(this);
                _cameraEnabled = true;
            }
        }

        protected override void OnEndMission()
        {
            base.OnEndMission();
            Shutdown();
        }

        // Backstop for a mission torn down without OnEndMission (e.g. quitting from the menu): never leave the camera
        // patches on past this mission screen.
        public override void OnMissionScreenFinalize()
        {
            base.OnMissionScreenFinalize();
            Shutdown();
        }

        private void Shutdown()
        {
            if (_cameraEnabled)
            {
                RangedAimCamera.Disable();
                _cameraEnabled = false;
            }
            if (_gauntletLayer != null)
            {
                _missionScreen?.RemoveLayer(_gauntletLayer);
                _gauntletLayer = null;
            }
            _dataSource?.OnFinalize();
            _dataSource = null;
            _missionScreen = null;
            _hasPrediction = false;
            _cameraTargetValid = false;
        }

        /// <summary>
        /// The point the aim camera should keep on screen, from the latest prediction; false when the player is not
        /// drawing (or the arc could not be predicted) or the prediction is stale.
        /// </summary>
        public bool TryGetCameraTarget(out Vec3 target)
        {
            target = _cameraTarget;
            return _cameraTargetValid && Mission != null && Mission.CurrentTime - _cameraTargetTime <= CameraTargetMaxAge;
        }

        // Runs even when the mission tick does not (e.g. while the mouse is up), so the arc never lingers.
        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);
            if (_missionScreen != null && !CanShowOverlay())
            {
                _dataSource?.HideAll();
                _cameraTargetValid = false;
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_missionScreen == null || (_drawArc && (_dataSource == null || _gauntletLayer == null)))
            {
                return;
            }
            try
            {
                Refresh();
            }
            catch (Exception e)
            {
                _dataSource?.HideAll();
                _hasPrediction = false;
                _cameraTargetValid = false;
                TaleWorlds.Library.Debug.Print("[RBM] RangedAimArcView: " + e.Message);
            }
        }

        private bool CanShowOverlay()
        {
            Mission mission = Mission;
            if (mission == null || _missionScreen == null || IsViewSuspended)
            {
                return false;
            }
            if (mission.Mode == MissionMode.Conversation || mission.Mode == MissionMode.CutScene || mission.Mode == MissionMode.Deployment)
            {
                return false;
            }
            if (_missionScreen.IsPhotoModeEnabled || _missionScreen.CustomCamera != null || _missionScreen.IsViewingCharacter())
            {
                return false;
            }
            return !ScreenManager.GetMouseVisibility();
        }

        private void Refresh()
        {
            Camera camera = _missionScreen.CombatCamera;
            if (camera == null || !CanShowOverlay() || !TryPredictShot(out Vec3 origin, out Vec3 velocity, out float friction))
            {
                _dataSource?.HideAll();
                _cameraTargetValid = false;
                return;
            }

            _predHitAgent = null;
            int count = MissileBallistics.SampleTrajectory(origin, velocity, friction, StepsPerSample, MaxFlightSeconds, MaxDrop, _samples, _segmentTest, out bool landed);
            _hasPrediction = true;
            _predictionTime = Mission.CurrentTime;
            _predOrigin = origin;
            _predVelocity = velocity;
            _predFriction = friction;
            _predImpact = _samples[count - 1];
            _predLanded = landed;
            // The camera frames where the flight comes down to the terrain, never a unit or a scene object: the arc
            // flicking on and off men as the aim sweeps a formation, or between a wall face, its battlements, a ladder or
            // a siege tower and the ground beyond, would otherwise jerk the camera between the near hit and the far one.
            // So the flight is carried on through all of them to the terrain. And when it meets no terrain (past the map
            // edge, a terrain hole), the farthest point sampled can still be high in the air, which would let the camera
            // relax back to normal, so it is carried on down to the shooter's ground level instead.
            _cameraTarget = _predImpact;
            if (count > 1)
            {
                int terrainCount = MissileBallistics.SampleTrajectory(origin, velocity, friction, StepsPerSample, CameraMaxFlightSeconds, MaxDrop, _terrainSamples, _terrainTest, out bool onTerrain);
                if (onTerrain)
                {
                    _cameraTarget = _terrainSamples[terrainCount - 1];
                }
                else
                {
                    _groundPlaneZ = Mission.MainAgent.Position.z;
                    int fallbackCount = MissileBallistics.SampleTrajectory(origin, velocity, friction, FallbackStepsPerSample, CameraMaxFlightSeconds, MaxDrop, _fallbackSamples, _groundPlaneTest, out bool _);
                    if (fallbackCount > 1)
                    {
                        _cameraTarget = _fallbackSamples[fallbackCount - 1];
                    }
                }
            }
            _cameraTargetTime = _predictionTime;
            _cameraTargetValid = count > 1;

            if (!_drawArc)
            {
                return;
            }

            // WorldToScreen yields real pixels; widget offsets are in unscaled UI units.
            float inverseScale = _gauntletLayer.UIContext.InverseScale;
            _dataSource.BeginFrame();

            float total = 0f;
            for (int i = 1; i < count; i++)
            {
                total += _samples[i].Distance(_samples[i - 1]);
            }
            float usable = total - FirstDotDistance;
            if (usable > 0f)
            {
                float spacing = Math.Max(usable / DotCount, MinDotSpacing);
                int dots = Math.Min(DotCount, (int)(usable / spacing));
                int seg = 1;
                float segStart = 0f;
                float segLength = count > 1 ? _samples[1].Distance(_samples[0]) : 0f;
                for (int d = 0; d < dots; d++)
                {
                    float s = FirstDotDistance + spacing * d;
                    while (seg < count - 1 && s > segStart + segLength)
                    {
                        segStart += segLength;
                        seg++;
                        segLength = _samples[seg].Distance(_samples[seg - 1]);
                    }
                    float t = segLength > 0.0001f ? MBMath.ClampFloat((s - segStart) / segLength, 0f, 1f) : 0f;
                    Vec3 p = Vec3.Lerp(_samples[seg - 1], _samples[seg], t);
                    if (Project(camera, p, inverseScale, out float x, out float y))
                    {
                        // Shrink and fade toward the far end so the near arc reads first.
                        float along = dots > 1 ? (float)d / (dots - 1) : 0f;
                        _dataSource.PushDot(x, y, MBMath.Lerp(8f, 4f, along), MBMath.Lerp(0.9f, 0.35f, along), DotColor);
                    }
                }
            }
            uint impactColor = _predHitAgent == null ? ImpactColor
                : _predHitAgent.IsEnemyOf(Mission.MainAgent) ? EnemyImpactColor
                : FriendlyImpactColor;
            if (landed)
            {
                PushAccuracyRing(camera, inverseScale, origin, _samples[count - 2], _predImpact, RingColor);
            }
            _dataSource.EndFrame();

            if (landed && Project(camera, _predImpact, inverseScale, out float ix, out float iy))
            {
                _dataSource.SetImpact(ix, iy, 14f, 0.95f, impactColor);
            }
            else
            {
                _dataSource.HideImpact();
            }
        }

        /// <summary>
        /// The launch the main agent's shot would get now: false unless he is drawing a bow/crossbow/sling that has
        /// ammunition, or readying a javelin, throwing axe/knife or stone he still has some of.
        /// </summary>
        private bool TryPredictShot(out Vec3 origin, out Vec3 velocity, out float friction)
        {
            origin = Vec3.Zero;
            velocity = Vec3.Zero;
            friction = 0f;
            Agent agent = Mission.MainAgent;
            if (agent == null || !agent.IsActive() || agent.GetCurrentActionType(1) != Agent.ActionCodeType.ReadyRanged)
            {
                return false;
            }
            EquipmentIndex slot = agent.GetPrimaryWieldedItemIndex();
            if (slot == EquipmentIndex.None)
            {
                return false;
            }
            MissionWeapon launcher = agent.Equipment[slot];
            WeaponComponentData usage = launcher.CurrentUsageItem;
            if (launcher.IsEmpty || usage == null)
            {
                return false;
            }
            WeaponClass launcherClass = usage.WeaponClass;
            bool thrown = IsThrownClass(launcherClass);
            if (!thrown && !IsLauncherClass(launcherClass))
            {
                return false;
            }
            if (thrown ? launcher.Amount <= 0 : MissileBallistics.GetLauncherAmmoWeight(agent, launcher, usage) <= 0f)
            {
                return false;
            }

            Vec3 dir = agent.LookDirection;
            if (dir.Normalize() < 0.0001f)
            {
                return false;
            }

            float speed;
            if (thrown)
            {
                // The engine throws at the MissileSpeed RBMCombat's WeaponEquipped prefix handed it, and the shot
                // prefix leaves the player's throws alone; with RBMCombat off it is the weapon's own.
                speed = RBMConfig.RBMConfig.rbmCombatEnabled ? MissileBallistics.GetThrowSpeed(agent, launcher) : launcher.GetModifiedMissileSpeedForCurrentUsage();
            }
            else if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                // Same as the shot prefix: RBM's launch speed from the cached draw weight (the launcher's shared
                // MissileSpeed holds the draw weight outside the prefix), plus the shooter's speed along the shot.
                int drawWeight = RangedRework.rangedWeaponStats.TryGetValue(RangedRework.GetRangedWeaponKey(launcher), out RangedWeaponStats stats)
                    ? stats.getDrawWeight()
                    : usage.MissileSpeed;
                int launchSpeed = launcherClass == WeaponClass.Sling
                    ? MissileBallistics.GetSlingSpeed(agent, launcher, usage, drawWeight)
                    : MissileBallistics.GetLauncherSpeed(agent, launcher, usage, drawWeight);
                if (launchSpeed <= 0)
                {
                    return false;
                }
                speed = launchSpeed + Vec3.DotProduct(agent.Velocity, dir);
            }
            else
            {
                speed = launcher.GetModifiedMissileSpeedForCurrentUsage();
            }
            if (speed <= 1f)
            {
                return false;
            }
            // A throw also carries the thrower's own velocity, added as a vector (measured: real speed = throw speed +
            // his speed along the throw, and the throw flattens when running forward).
            velocity = thrown ? dir * speed + agent.Velocity : dir * speed;

            if (RBMConfig.RBMConfig.rbmCombatEnabled && RBMConfig.RBMConfig.realisticArrowArc && (launcherClass == WeaponClass.Bow || launcherClass == WeaponClass.Crossbow))
            {
                float vecLength = velocity.Length;
                double currentRad = Math.Acos(MBMath.ClampFloat(velocity.z / vecLength, -1f, 1f));
                velocity.z = vecLength * (float)Math.Cos(currentRad - RealisticArrowArcRadians);
            }

            // The drag the missile flies with is its ammo's, as in the shot prefix; a thrown weapon is its own ammo.
            MissionWeapon ammo = launcher.AmmoWeapon;
            friction = thrown
                ? ItemObject.GetAirFrictionConstant(launcherClass, usage.WeaponFlags)
                : !ammo.IsEmpty && ammo.CurrentUsageItem != null
                    ? ItemObject.GetAirFrictionConstant(ammo.CurrentUsageItem.WeaponClass, ammo.CurrentUsageItem.WeaponFlags)
                    : ItemObject.GetAirFrictionConstant(usage.AmmoClass, (WeaponFlags)0);

            LookFrame(dir, out Vec3 right, out Vec3 up);
            // The throw leaves ThrowReleaseDelay after the aim ends, by when a moving thrower has carried on that far.
            origin = thrown
                ? agent.GetEyeGlobalPosition() + dir * ThrowOriginForward + right * ThrowOriginRight + up * ThrowOriginUp + agent.Velocity * ThrowReleaseDelay
                : agent.GetEyeGlobalPosition() + dir * OriginForward + right * OriginRight + up * OriginUp;
            return true;
        }

        private static bool IsLauncherClass(WeaponClass c)
        {
            return c == WeaponClass.Bow || c == WeaponClass.Crossbow || c == WeaponClass.Sling;
        }

        private static bool IsThrownClass(WeaponClass c)
        {
            return c == WeaponClass.Javelin || c == WeaponClass.ThrowingAxe || c == WeaponClass.ThrowingKnife || c == WeaponClass.Stone;
        }


        private static void LookFrame(Vec3 forward, out Vec3 right, out Vec3 up)
        {
            right = Vec3.CrossProduct(forward, Vec3.Up);
            if (right.Normalize() < 0.0001f)
            {
                right = new Vec3(1f, 0f, 0f);
            }
            up = Vec3.CrossProduct(right, forward);
        }

        /// <summary>
        /// One arc segment: terrain/objects first, then agents' limbs up to wherever the scene stopped it, so the arc
        /// ends on the nearer of the two. Skips the main agent and his own mount.
        /// </summary>
        private bool SegmentHitsScene(Vec3 from, Vec3 to, out Vec3 hit)
        {
            hit = to;
            Mission mission = Mission;
            Scene scene = mission?.Scene;
            if (scene == null)
            {
                return false;
            }
            Vec3 dir = to - from;
            if (dir.Normalize() < 0.0001f)
            {
                return false;
            }
            bool hitScene = false;
            if (scene.RayCastForClosestEntityOrTerrain(from, to, out float distance, out Vec3 closest, out WeakGameEntity _, 0.01f, BodyFlags.CommonCollisionExcludeFlagsForMissile))
            {
                if (closest.IsValid)
                {
                    hit = closest;
                    hitScene = true;
                }
                else if (!float.IsNaN(distance))
                {
                    hit = from + dir * distance;
                    hitScene = true;
                }
            }

            Agent main = mission.MainAgent;
            if (main == null)
            {
                return hitScene;
            }
            Agent mount = main.MountAgent;
            Vec3 start = from;
            // A couple of passes so a ray that first clips the player's own horse carries on past it.
            for (int pass = 0; pass < 3; pass++)
            {
                if (Vec3.DotProduct(hit - start, dir) <= 0.01f)
                {
                    break;
                }
                Agent agent = mission.RayCastForClosestAgentsLimbs(start, hit, main.Index, AgentRayThickness, out float agentDistance, out sbyte _);
                if (agent == null || float.IsNaN(agentDistance))
                {
                    break;
                }
                Vec3 agentHit = start + dir * agentDistance;
                if (agent == mount)
                {
                    start = agentHit + dir * 0.05f;
                    continue;
                }
                hit = agentHit;
                _predHitAgent = agent;
                return true;
            }
            return hitScene;
        }

        /// <summary>
        /// Accuracy indicator: a ring of dots laid on the ground around the landing point, the footprint of the cone of
        /// the shot's current aiming error (what the crosshair's gap shows; the crosshair is hidden while the aim camera
        /// has moved). Across the shot its radius is the cone's at that range; along the shot it stretches by the
        /// descent angle, since a shallow arrow's spread smears out lengthwise. Shrinks as the aim settles.
        /// </summary>
        private void PushAccuracyRing(Camera camera, float inverseScale, Vec3 origin, Vec3 beforeImpact, Vec3 impact, uint color)
        {
            Agent shooter = Mission.MainAgent;
            Scene scene = Mission.Scene;
            float spreadAngle = MBMath.ClampFloat(shooter.CurrentAimingError + shooter.CurrentAimingTurbulance, 0f, 0.6f);
            float across = MBMath.ClampFloat(origin.Distance(impact) * (float)Math.Tan(spreadAngle), RingMinRadius, RingMaxRadius);

            Vec3 flight = impact - beforeImpact;
            if (flight.Normalize() < 0.0001f)
            {
                return;
            }
            Vec2 alongDir = flight.AsVec2;
            if (alongDir.Normalize() < 0.0001f)
            {
                alongDir = shooter.LookDirection.AsVec2;
                alongDir.Normalize();
            }
            Vec2 acrossDir = new Vec2(-alongDir.y, alongDir.x);
            float along = Math.Min(across / Math.Max(Math.Abs(flight.z), 1f / RingMaxStretch), RingMaxRadius * RingMaxStretch);

            for (int i = 0; i < RingDotCount; i++)
            {
                double a = i * (2.0 * Math.PI / RingDotCount);
                Vec2 offset = alongDir * (along * (float)Math.Cos(a)) + acrossDir * (across * (float)Math.Sin(a));
                Vec3 p = new Vec3(impact.x + offset.x, impact.y + offset.y, impact.z + RingGroundProbeHeight);
                float ground = scene.GetGroundHeightAtPosition(p);
                // No ground under the point (map edge) or it is far off the impact's level: keep the impact's height.
                p.z = !float.IsNaN(ground) && Math.Abs(ground - impact.z) < RingGroundProbeHeight * 3f ? ground + 0.05f : impact.z;
                if (Project(camera, p, inverseScale, out float x, out float y))
                {
                    _dataSource.PushDot(x, y, RingDotSize, RingDotAlpha, color);
                }
            }
        }

        /// <summary>
        /// Camera flight: the segment going from above the terrain heightmap to at or below it, at the point where it
        /// crosses (linear in height above the terrain, which is centimetres off over a 0.1 s segment). Walls, siege
        /// engines, buildings and units are not terrain, so the flight passes through them. A segment that starts below
        /// the terrain (under an overhang, through a hole) does not count, and no height (NaN/infinite) is no terrain.
        /// </summary>
        private bool SegmentCrossesTerrain(Vec3 from, Vec3 to, out Vec3 hit)
        {
            hit = to;
            Scene scene = Mission?.Scene;
            if (scene == null)
            {
                return false;
            }
            float toAbove = to.z - scene.GetTerrainHeight(to.AsVec2);
            if (!(toAbove <= 0f))
            {
                return false;
            }
            float fromAbove = from.z - scene.GetTerrainHeight(from.AsVec2);
            if (!(fromAbove > 0f) || float.IsInfinity(fromAbove) || float.IsInfinity(toAbove))
            {
                return false;
            }
            hit = Vec3.Lerp(from, to, MBMath.ClampFloat(fromAbove / (fromAbove - toAbove), 0f, 1f));
            return true;
        }

        /// <summary>Camera fallback: the flight coming back down through the shooter's own ground level.</summary>
        private bool SegmentCrossesGroundPlane(Vec3 from, Vec3 to, out Vec3 hit)
        {
            hit = to;
            if (to.z > _groundPlaneZ || from.z <= to.z)
            {
                return false;
            }
            float t = from.z - to.z > 0.0001f ? MBMath.ClampFloat((from.z - _groundPlaneZ) / (from.z - to.z), 0f, 1f) : 1f;
            hit = Vec3.Lerp(from, to, t);
            return true;
        }

        private static bool Project(Camera camera, Vec3 worldPos, float inverseScale, out float x, out float y)
        {
            float sx = 0f;
            float sy = 0f;
            float sz = 0f;
            MBWindowManager.WorldToScreen(camera, worldPos, ref sx, ref sy, ref sz);
            x = sx * inverseScale;
            y = sy * inverseScale;
            return sz >= 0f && !float.IsNaN(x) && !float.IsNaN(y) && Math.Abs(x) < 100000f && Math.Abs(y) < 100000f;
        }

        /// <summary>
        /// Calibration: compares the arc's last prediction with the launch the engine really gave the player's missile.
        /// Only written when the battle hit log is on (BattleHitLog self-gates).
        /// </summary>
        public override void OnAgentShootMissile(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, int forcedMissileIndex)
        {
            base.OnAgentShootMissile(shooterAgent, weaponIndex, position, velocity, orientation, hasRigidBody, forcedMissileIndex);
            if (!BattleHitLog.IsEnabled || shooterAgent == null || !shooterAgent.IsMainAgent || Mission == null)
            {
                return;
            }
            MissionWeapon launcher = shooterAgent.Equipment[weaponIndex];
            WeaponClass launcherClass = launcher.CurrentUsageItem != null ? launcher.CurrentUsageItem.WeaponClass : WeaponClass.Undefined;
            if (!IsLauncherClass(launcherClass) && !IsThrownClass(launcherClass))
            {
                return;
            }
            int missileIndex = Mission.MissilesList.Count > 0 ? Mission.MissilesList[Mission.MissilesList.Count - 1].Index : -1;
            float age = Mission.CurrentTime - _predictionTime;
            if (!_hasPrediction || age > 0.5f)
            {
                BattleHitLog.Write("ARCSHOT " + launcherClass + " missile=" + missileIndex + " no-prediction (arc was not shown within 0.5 s of the shot)");
                return;
            }

            Vec3 predDir = _predVelocity;
            float predSpeed = predDir.Normalize();
            Vec3 realDir = velocity;
            float realSpeed = realDir.Normalize();
            LookFrame(predDir, out Vec3 right, out Vec3 up);
            Vec3 d = position - _predOrigin;
            float predPitch = (float)Math.Asin(MBMath.ClampFloat(predDir.z, -1f, 1f)) * 57.29578f;
            float realPitch = (float)Math.Asin(MBMath.ClampFloat(realDir.z, -1f, 1f)) * 57.29578f;
            float dYaw = MBMath.WrapAngle(realDir.AsVec2.RotationInRadians - predDir.AsVec2.RotationInRadians) * 57.29578f;
            Vec2 shotDir = realDir.AsVec2;
            shotDir.Normalize();
            float predImpactRange = Vec2.DotProduct(_predImpact.AsVec2 - position.AsVec2, shotDir);

            BattleHitLog.Write("ARCSHOT " + launcherClass
                + " missile=" + missileIndex
                + " launcher=" + (launcher.Item != null ? launcher.Item.StringId : "?")
                + (shooterAgent.HasMount ? " mounted" : "")
                + " age=" + BattleHitLog.Fmt(age)
                + " rbmCombat=" + (RBMConfig.RBMConfig.rbmCombatEnabled ? 1 : 0)
                + " predSpeed=" + BattleHitLog.Fmt(predSpeed)
                + " realSpeed=" + BattleHitLog.Fmt(realSpeed)
                + " agentSpeedAlong=" + BattleHitLog.Fmt(Vec3.DotProduct(shooterAgent.Velocity, realDir))
                + " predPitch=" + BattleHitLog.Fmt(predPitch)
                + " realPitch=" + BattleHitLog.Fmt(realPitch)
                + " dPitch=" + BattleHitLog.Fmt(realPitch - predPitch)
                + " dYaw=" + BattleHitLog.Fmt(dYaw)
                + " aimErrDeg=" + BattleHitLog.Fmt((shooterAgent.CurrentAimingError + shooterAgent.CurrentAimingTurbulance) * 57.29578f)
                + " origin(real-pred) dFwd=" + BattleHitLog.Fmt(Vec3.DotProduct(d, predDir))
                + " dRight=" + BattleHitLog.Fmt(Vec3.DotProduct(d, right))
                + " dUp=" + BattleHitLog.Fmt(Vec3.DotProduct(d, up))
                + " dist=" + BattleHitLog.Fmt(d.Length)
                + " friction=" + _predFriction.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)
                + " predImpactRange=" + (_predLanded ? BattleHitLog.Fmt(predImpactRange) : "none")
                + " predHit=" + (!_predLanded ? "none" : _predHitAgent == null ? "scene" : _predHitAgent.IsEnemyOf(shooterAgent) ? "enemy" : "friend")
                // The prediction (and so every column above) is from the eye along LookDirection, never the camera, so
                // a tilted aim camera must not move them; logged to confirm exactly that.
                + (_cameraEnabled ? " camTiltDeg=" + BattleHitLog.Fmt(RangedAimCamera.CurrentTiltDegrees) : ""));

            if (missileIndex >= 0 && _predLanded)
            {
                MissileAimTrace.BeginArcShot(missileIndex, position, realDir, _predImpact, Mission.CurrentTime);
            }
        }
    }
}
