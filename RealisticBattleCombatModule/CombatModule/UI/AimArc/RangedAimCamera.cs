using HarmonyLib;
using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace RBMCombat
{
    /// <summary>
    /// Experimental camera assist for the player's high lobs (on with RBMConfig.rangedAimArcEnabled): while he draws, the
    /// third-person camera tilts down so the landing point RangedAimArcView predicts stays held a little below the
    /// screen centre, orbiting up and over behind him around his eye (the higher he aims, the higher it rises). The
    /// view therefore holds still while the mouse moves the arc. Engaging and letting go are eased; tracking is not.
    ///
    /// Only the rendered frame moves. The player's aim is MissionMainAgentController setting LookDirection from
    /// MissionScreen.CameraBearing/CameraElevation, which this never touches, so the shot is unchanged; the arc is
    /// predicted from the agent's eye and LookDirection, so the camera does not feed back into the prediction either.
    ///
    /// Hook: MissionScreen.UpdateCamera (private, the third-person/first-person camera builder). The postfix takes the
    /// frame vanilla just wrote to CombatCamera, adjusts it and re-sends it (CombatCamera.Frame, SceneView.SetCamera,
    /// Mission.SetCameraFrame with vanilla's zoom factor and listener blend). UpdateCamera reads CombatCamera.Frame back
    /// next frame (spectator lookup, the free/first-person fallbacks and, for ranged weapons, the vertical aim
    /// correction's height and pitch), so the prefix puts vanilla's own frame back first; otherwise the lift and tilt
    /// would be fed into vanilla's correction and accumulate. The prefix only restores when the camera still holds
    /// exactly the frame the postfix wrote, so a custom/overridden camera that set its own frame in between is left be.
    /// Mission.Tick runs the camera update (IMissionSystemHandler.UpdateCamera) before the behaviours' OnMissionTick,
    /// so the arc dots are projected with this frame's adjusted camera; the prediction the camera frames is last
    /// frame's (it is computed in OnMissionTick), which is fine at 1 frame of lag.
    ///
    /// Patched by hand into its own Harmony instance, not by attribute, so RBMCombat's PatchAll never picks it up and
    /// it works whether or not RBMCombat is on. RangedAimArcView enables it in AfterStart and disables it at mission end;
    /// with the toggle off nothing is patched. Any exception disables the assist for the rest of the mission.
    /// </summary>
    public static class RangedAimCamera
    {
        private const string HarmonyId = "com.rbm.aimcamera";

        // Where the landing point is held on screen while drawing: this share of the half vertical FOV below the centre
        // (0 = dead centre, 1 = bottom edge). The camera only ever tilts down, so low/flat shots are left alone.
        // Lower = the tilt starts sooner as he aims up.
        private const float TargetBelowCentreFraction = 0.15f;
        // Largest downward tilt, radians (60 deg).
        private const float MaxTiltRadians = 1.0471976f;
        // Straight lift on top of the orbit (metres), ramped in by how far he aims up: none at or below LiftStartPitch,
        // full from LiftFullPitch (radians; 2 and 12 deg). It drops his head/helmet down the screen so it does not sit
        // over the held landing point, and helps see over the front ranks. Level/downward shots keep vanilla's camera.
        private const float ExtraRaise = 1f;
        private const float LiftStartPitchRadians = 0.034906585f;
        private const float LiftFullPitchRadians = 0.20943951f;
        // Below both of these the frame is left as vanilla built it (and the crosshair stays).
        private const float RestTilt = 0.0005f;
        private const float RestLift = 0.005f;
        // Easing rate (1/s) for engaging/disengaging only. While engaged the tilt follows the landing point exactly,
        // so the view holds still and the mouse moves the arc instead of the picture.
        private const float EngageRate = 4f;
        // Easing rate (1/s) of the view pitch toward the landing point. Lower = calmer when the landing point jumps,
        // but slower to bring a new landing point into place.
        private const float ViewEaseRate = 2.5f;
        // After the shot (or any loss of the prediction) the camera stays where it was this long before easing back,
        // so nocking and drawing the next arrow does not bounce it down and up again.
        private const float HoldSeconds = 1.2f;
        // Stop the camera this far short of a ceiling, wall or overhang.
        private const float CeilingClearance = 0.3f;
        // Below this the assist counts as fully eased out and stops touching the camera.
        private const float RestEngage = 0.002f;

        private static Harmony _harmony;
        private static bool _patched;
        private static RangedAimArcView _view;
        private static bool _failed;

        // Engage blend 0..1 (eased), the tilt the landing point last asked for (held while disengaging), and the tilt
        // actually applied (_heldTilt * _engage).
        private static float _engage;
        private static float _heldTilt;
        // The lift share (0..1) the aim pitch last asked for, held while disengaging like _heldTilt.
        private static float _heldLift;
        private static float _tilt;
        // Time left of the post-shot hold, and the eased pitch the adjusted camera looks along (vanilla's pitch minus
        // the tilt), which is what smooths sudden jumps of the landing point.
        private static float _holdLeft;
        private static float _viewPitch;

        // What the postfix last wrote, and vanilla's frame it replaced, for the prefix to undo.
        private static bool _applied;
        private static MatrixFrame _baseFrame;
        private static MatrixFrame _adjustedFrame;

        private static AccessTools.FieldRef<MissionScreen, float> _cameraHeightLimit;
        private static bool _heightLimitLookupDone;

        /// <summary>Current downward tilt in degrees (0 when the assist is off or at rest), for the ARCSHOT log.</summary>
        public static float CurrentTiltDegrees => _applied ? _tilt * 57.29578f : 0f;

        public static bool IsPatched => _patched;

        public static void Enable(RangedAimArcView view)
        {
            ResetState();
            _view = view;
            _failed = false;
            if (_patched)
            {
                return;
            }
            try
            {
                if (_harmony == null)
                {
                    _harmony = new Harmony(HarmonyId);
                }
                // Stale patches from a mission that never reached its end.
                _harmony.UnpatchAll(HarmonyId);

                MethodInfo updateCamera = AccessTools.Method(typeof(MissionScreen), "UpdateCamera", new[] { typeof(float) });
                if (updateCamera == null)
                {
                    TaleWorlds.Library.Debug.Print("[RBM] RangedAimCamera: MissionScreen.UpdateCamera(float) not found; aim camera off.");
                    return;
                }
                _harmony.Patch(updateCamera,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(RangedAimCamera), nameof(UpdateCameraPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(RangedAimCamera), nameof(UpdateCameraPostfix))));
                _patched = true;

                // The Gauntlet crosshair lives in an assembly RBMCombat does not reference; resolve it by name.
                Type crosshair = AccessTools.TypeByName("TaleWorlds.MountAndBlade.GauntletUI.Mission.MissionGauntletCrosshair");
                MethodInfo shouldShow = crosshair != null ? AccessTools.Method(crosshair, "GetShouldCrosshairBeVisible") : null;
                if (shouldShow != null)
                {
                    _harmony.Patch(shouldShow, postfix: new HarmonyMethod(AccessTools.Method(typeof(RangedAimCamera), nameof(CrosshairVisiblePostfix))));
                }
                else
                {
                    TaleWorlds.Library.Debug.Print("[RBM] RangedAimCamera: MissionGauntletCrosshair.GetShouldCrosshairBeVisible not found; crosshair stays visible.");
                }
            }
            catch (Exception e)
            {
                TaleWorlds.Library.Debug.Print("[RBM] RangedAimCamera: patching failed: " + e.Message);
                Disable();
            }
        }

        public static void Disable()
        {
            try
            {
                if (_harmony != null)
                {
                    _harmony.UnpatchAll(HarmonyId);
                }
            }
            catch (Exception e)
            {
                TaleWorlds.Library.Debug.Print("[RBM] RangedAimCamera: unpatching failed: " + e.Message);
            }
            _patched = false;
            _view = null;
            ResetState();
        }

        private static void ResetState()
        {
            _engage = 0f;
            _heldTilt = 0f;
            _heldLift = 0f;
            _tilt = 0f;
            _holdLeft = 0f;
            _viewPitch = 0f;
            _applied = false;
        }

        private static void Fail(Exception e)
        {
            _failed = true;
            ResetState();
            TaleWorlds.Library.Debug.Print("[RBM] RangedAimCamera: disabled for this mission: " + e.Message);
        }

        private static void UpdateCameraPrefix(MissionScreen __instance)
        {
            if (!_applied)
            {
                return;
            }
            _applied = false;
            try
            {
                Camera camera = __instance.CombatCamera;
                if (camera != null && NearlyEqual(camera.Frame, _adjustedFrame))
                {
                    camera.Frame = _baseFrame;
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static void UpdateCameraPostfix(MissionScreen __instance, float dt)
        {
            if (_failed)
            {
                return;
            }
            try
            {
                Apply(__instance, dt);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static void CrosshairVisiblePostfix(ref bool __result)
        {
            // Hidden whenever the camera is moved at all (the lift alone already puts the screen centre off the aim).
            if (__result && _applied)
            {
                __result = false;
            }
        }

        private static void Apply(MissionScreen screen, float dt)
        {
            Mission mission = screen.Mission;
            Camera camera = screen.CombatCamera;
            Agent main = mission?.MainAgent;
            // Camera modes the assist never applies to: snap to rest rather than ease, since the view is jumping anyway.
            if (camera == null || main == null || !main.IsActive() || mission.CameraIsFirstPerson
                || screen.IsPhotoModeEnabled || screen.CustomCamera != null || screen.LastFollowedAgent != main
                || mission.Mode == MissionMode.Conversation || mission.Mode == MissionMode.Barter
                || mission.Mode == MissionMode.CutScene || mission.Mode == MissionMode.Deployment)
            {
                ResetState();
                return;
            }

            float step = 1f - (float)Math.Exp(-EngageRate * MBMath.ClampFloat(dt, 0f, 0.1f));
            MatrixFrame baseFrame = camera.Frame;
            Vec3 origin = baseFrame.origin;
            Vec3 side = baseFrame.rotation.s;
            Vec3 forward = -baseFrame.rotation.u;
            Vec3 up = baseFrame.rotation.f;

            // The tilt that puts the landing point TargetBelowCentreFraction below the screen centre, worked out from
            // vanilla's camera position (the orbit moves the camera a few metres; the target is tens to hundreds away).
            // The tilt itself is not eased: that is what made the view swing with the mouse and then drift back.
            float basePitch = (float)Math.Asin(MBMath.ClampFloat(forward.z, -1f, 1f));
            bool hasTarget = false;
            if (_view != null && _view.TryGetCameraTarget(out Vec3 target) && !ScreenManager.GetMouseVisibility())
            {
                Vec3 rel = target - origin;
                float below = (float)Math.Atan2(-Vec3.DotProduct(rel, up), Vec3.DotProduct(rel, forward));
                float halfFov = camera.GetFovVertical() * 0.5f;
                if (!(halfFov > 0.05f && halfFov < 1.5f))
                {
                    halfFov = screen.CameraViewAngle * 0.5f * (MathF.PI / 180f);
                }
                float wantedTilt = MBMath.ClampFloat(below - halfFov * TargetBelowCentreFraction, 0f, MaxTiltRadians);
                float aimPitch = (float)Math.Asin(MBMath.ClampFloat(main.LookDirection.z, -1f, 1f));
                float lift = MBMath.ClampFloat((aimPitch - LiftStartPitchRadians) / (LiftFullPitchRadians - LiftStartPitchRadians), 0f, 1f);
                float wantedLift = lift * lift * (3f - 2f * lift);
                if (_engage < RestEngage)
                {
                    _viewPitch = basePitch - wantedTilt;
                    _heldLift = wantedLift;
                }
                else
                {
                    // Eased in view space, not tilt space: the pitch the camera ends up looking along (vanilla's pitch
                    // minus the tilt) hardly changes while the mouse sweeps the aim, so this adds no mouse lag, but a
                    // landing point that jumps (over a ridge, onto a wall, off a ledge) is followed smoothly instead
                    // of snapping. Also what blends a redraw during the hold onto the new aim.
                    float viewStep = 1f - (float)Math.Exp(-ViewEaseRate * MBMath.ClampFloat(dt, 0f, 0.1f));
                    _viewPitch += (basePitch - wantedTilt - _viewPitch) * viewStep;
                    _heldLift += (wantedLift - _heldLift) * viewStep;
                }
                _heldTilt = MBMath.ClampFloat(basePitch - _viewPitch, 0f, MaxTiltRadians);
                _viewPitch = basePitch - _heldTilt;
                _holdLeft = HoldSeconds;
                hasTarget = true;
            }
            else
            {
                // Prediction gone (shot loosed, reloading): hold the camera for a while, as long as he still has a
                // ranged weapon in hand, before easing back. While he keeps the attack key down (reloading with the
                // next shot already asked for) the hold does not run out. The tilt is kept, so the view pitch follows
                // the mouse.
                _viewPitch = basePitch - _heldTilt;
                WeaponComponentData wielded = main.WieldedWeapon.CurrentUsageItem;
                if (wielded == null || !wielded.IsRangedWeapon || ScreenManager.GetMouseVisibility())
                {
                    _holdLeft = 0f;
                }
                else if (_holdLeft > 0f && screen.SceneLayer != null && screen.SceneLayer.Input.IsGameKeyDown(CombatHotKeyCategory.Attack))
                {
                    _holdLeft = HoldSeconds;
                }
                else
                {
                    _holdLeft -= dt;
                }
            }
            // Only engaging and letting go are eased; while holding or letting go the last requested tilt is kept.
            bool engaged = hasTarget || _holdLeft > 0f;
            _engage += ((engaged ? 1f : 0f) - _engage) * step;
            if (!engaged && _engage < RestEngage)
            {
                ResetState();
                return;
            }
            _tilt = _heldTilt * _engage;
            float liftMetres = ExtraRaise * _heldLift * _engage;
            if (_tilt < RestTilt && liftMetres < RestLift)
            {
                // Level or low aim: vanilla's frame stands (so does the crosshair); keep the engage state.
                return;
            }

            // Pitch down about the camera's side axis: forward toward -up, up toward forward.
            float cos = (float)Math.Cos(_tilt);
            float sin = (float)Math.Sin(_tilt);
            Vec3 newForward = forward * cos - up * sin;
            Vec3 newUp = up * cos + forward * sin;

            // Orbit around the player's eye rather than turning in place: keep the eye at the same camera-space
            // position, so tilting down swings the camera up and over behind him (the higher he aims, the higher it
            // goes) and he stays where he was on screen. Plus the straight lift, ramped in by aim pitch.
            Vec3 pivot = main.GetEyeGlobalPosition();
            Vec3 pr = pivot - origin;
            float ps = Vec3.DotProduct(pr, side);
            float pf = Vec3.DotProduct(pr, forward);
            float pu = Vec3.DotProduct(pr, up);
            Vec3 orbited = pivot - (side * ps + newForward * pf + newUp * pu);
            orbited.z += liftMetres;
            orbited = ClampToScene(screen, mission, pivot, orbited);

            MatrixFrame adjusted = baseFrame;
            adjusted.origin = orbited;
            adjusted.rotation.f = newUp;
            adjusted.rotation.u = -newForward;

            camera.Frame = adjusted;
            screen.SceneView?.SetCamera(camera);
            // As vanilla's tail of UpdateCamera, for the listener/attenuation and the mission's own camera frame.
            Vec3 attenuation = main.GetEyeGlobalPosition();
            if (mission.ListenerAndAttenuationPosBlendFactor > 0f)
            {
                attenuation += (adjusted.origin - attenuation) * mission.ListenerAndAttenuationPosBlendFactor;
            }
            MatrixFrame missionFrame = adjusted;
            float viewAngle = screen.CameraViewAngle;
            mission.SetCameraFrame(ref missionFrame, viewAngle > 0.001f ? 65f / viewAngle : 1f, ref attenuation);

            _baseFrame = baseFrame;
            _adjustedFrame = camera.Frame;
            _applied = true;
        }

        /// <summary>
        /// Pulls the orbited camera position in toward the player's eye so it stays CeilingClearance short of any
        /// ceiling, wall or overhang between them (as vanilla's own camera collision casts from the followed agent), and
        /// under the scene's camera height limiter when it has one.
        /// </summary>
        private static Vec3 ClampToScene(MissionScreen screen, Mission mission, Vec3 pivot, Vec3 wanted)
        {
            Vec3 result = wanted;
            Scene scene = mission.Scene;
            Vec3 dir = wanted - pivot;
            float length = dir.Normalize();
            if (scene != null && length > 0.01f)
            {
                Vec3 end = pivot + dir * (length + CeilingClearance);
                if (scene.RayCastForClosestEntityOrTerrain(pivot, end, out float distance, 0.1f, BodyFlags.CameraCollisionRayCastExludeFlags | BodyFlags.DontCollideWithCamera)
                    && !float.IsNaN(distance) && distance < length + CeilingClearance)
                {
                    result = pivot + dir * Math.Max(0.2f, distance - CeilingClearance);
                }
            }
            float limit = GetCameraHeightLimit(screen);
            if (limit > 0f && result.z > limit)
            {
                result.z = limit;
            }
            return result;
        }

        private static float GetCameraHeightLimit(MissionScreen screen)
        {
            if (!_heightLimitLookupDone)
            {
                _heightLimitLookupDone = true;
                try
                {
                    _cameraHeightLimit = AccessTools.FieldRefAccess<MissionScreen, float>("_cameraHeightLimit");
                }
                catch (Exception)
                {
                    _cameraHeightLimit = null;
                }
            }
            return _cameraHeightLimit != null ? _cameraHeightLimit(screen) : 0f;
        }

        private static bool NearlyEqual(MatrixFrame a, MatrixFrame b)
        {
            return NearlyEqual(a.origin, b.origin) && NearlyEqual(a.rotation.s, b.rotation.s)
                && NearlyEqual(a.rotation.f, b.rotation.f) && NearlyEqual(a.rotation.u, b.rotation.u);
        }

        private static bool NearlyEqual(Vec3 a, Vec3 b)
        {
            return Math.Abs(a.x - b.x) < 0.001f && Math.Abs(a.y - b.y) < 0.001f && Math.Abs(a.z - b.z) < 0.001f;
        }
    }
}
