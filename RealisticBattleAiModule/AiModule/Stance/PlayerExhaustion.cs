using HarmonyLib;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;

namespace RBMAI
{
    // First-person posture tiredness. Any forced body clip moves the head bone the first-person camera rides, so a
    // first-person player on foot plays no animation at all: he is rooted with all input blocked for LockSeconds
    // (mouse look still works) while the 3D view pulses dark like a man fighting for breath, then clears over
    // RecoverSeconds. StanceLogic.forceTiredAnimation triggers it via TryBegin and falls back to its stagger when
    // TryBegin refuses (rider, no mission screen).
    //
    // The darkening is the native CameraFade prefab (a full-screen black square bound to a float) on a layer of
    // our own, ordered between the scene layer (-100) and the HUD (-1 and up), so only the world darkens.
    public class PlayerExhaustionLogic : MissionLogic
    {
        // TUNE in game.
        private const float LockSeconds = 1.6f;
        private const float RampInSeconds = 0.25f;
        private const float RecoverSeconds = 0.6f;
        private const float BreathPeriodSeconds = 1.6f;
        // Darkness floor while exhausted, and how much each breath adds on top. Peak is their sum.
        private const float BaseDarkness = 0.45f;
        private const float BreathDarkness = 0.47f;
        private const int LayerOrder = -50;

        internal static PlayerExhaustionLogic Instance;

        private MissionScreen _missionScreen;
        private GauntletLayer _layer;
        private BindingListFloatItem _darkness;

        private bool _active;
        private float _startTime;
        private float _lockUntil;

        public override void AfterStart()
        {
            base.AfterStart();
            _missionScreen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen as MissionScreen;
            if (_missionScreen == null)
            {
                return;
            }
            _darkness = new BindingListFloatItem(0f);
            _layer = new GauntletLayer("RBMExhaustionFade", LayerOrder);
            _missionScreen.AddLayer(_layer);
            _layer.LoadMovie("CameraFade", (ViewModel)_darkness);
            Instance = this;
        }

        public override void OnRemoveBehavior()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (_missionScreen != null && _layer != null)
            {
                _missionScreen.RemoveLayer(_layer);
            }
            _layer = null;
            _missionScreen = null;
            _darkness = null;
            _active = false;
            base.OnRemoveBehavior();
        }

        // Starts (or, while one runs, extends) the exhaustion on the first-person main agent. False when it does not
        // apply, so the caller plays its animation instead.
        internal static bool TryBegin(Agent agent)
        {
            PlayerExhaustionLogic logic = Instance;
            if (logic == null || logic._darkness == null || !agent.IsMainAgent || agent.HasMount ||
                agent.Mission == null || !agent.Mission.CameraIsFirstPerson)
            {
                return false;
            }
            float now = agent.Mission.CurrentTime;
            if (!logic._active)
            {
                logic._active = true;
                logic._startTime = now;
                // Stand-in for a breathing sound, which the game does not have: the character's own exertion voice.
                agent.MakeVoice(SkinVoiceManager.VoiceType.Grunt, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction);
            }
            logic._lockUntil = MathF.Max(logic._lockUntil, now + LockSeconds);
            return true;
        }

        internal static bool IsInputLocked(Mission mission)
        {
            PlayerExhaustionLogic logic = Instance;
            return logic != null && logic._active && mission.CurrentTime < logic._lockUntil;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (!_active)
            {
                return;
            }
            Agent main = Mission.MainAgent;
            float now = Mission.CurrentTime;
            float t = now - _startTime;
            // Only the recovery tail ends it: the ramp-in is 0 on the first tick (the hit can land on the same
            // mission time as this tick), so it must not be read as "done".
            float recovery = now > _lockUntil ? 1f - (now - _lockUntil) / RecoverSeconds : 1f;
            if (main == null || !main.IsActive() || recovery <= 0f)
            {
                _active = false;
                _darkness.Item = 0f;
                return;
            }
            float rampIn = MathF.Clamp(t / RampInSeconds, 0f, 1f);
            float breath = 0.5f * (1f - MathF.Cos(2f * MathF.PI * t / BreathPeriodSeconds));
            _darkness.Item = rampIn * recovery * (BaseDarkness + BreathDarkness * breath);
        }

        // ControlTick is where the player's keys become the agent's movement/action flags and weapon swaps; LookTick
        // runs separately, so skipping only this keeps mouse look. The skipped method is also what clears last
        // frame's flags, so they are cleared here or a held block/attack would stay held.
        [HarmonyPatch(typeof(MissionMainAgentController), "ControlTick")]
        private static class ControlTickPatch
        {
            private static bool Prefix(MissionMainAgentController __instance)
            {
                Mission mission = __instance.Mission;
                if (mission == null || !IsInputLocked(mission))
                {
                    return true;
                }
                Agent main = mission.MainAgent;
                if (main != null)
                {
                    main.EventControlFlags = Agent.EventControlFlag.None;
                    main.MovementFlags = Agent.MovementControlFlag.None;
                    main.MovementInputVector = Vec2.Zero;
                }
                return false;
            }
        }
    }
}
