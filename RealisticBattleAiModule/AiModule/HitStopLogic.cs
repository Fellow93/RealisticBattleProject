using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Applies a brief slow-motion hit stop on key player combat events.
    /// Priority (low → high): Hit < Parry < Kill < Posture break
    /// A running stop can only be interrupted by a strictly higher-priority one.
    /// Same-priority stops never restart each other mid-play.
    /// Uses Mission.AddTimeSpeedRequest / RemoveTimeSpeedRequest.
    /// </summary>
    public class HitStopLogic : MissionLogic
    {
        public static HitStopLogic Instance { get; private set; }

        // ── Tuning constants ─────────────────────────────────────────────────

        // Hit: player lands any damaging strike
        //private const float HIT_DURATION  = 0.1f;
        //private const float HIT_SLOW      = 0.05f;
        //private const int   HIT_PRIORITY  = 0;

        //// Parry: player performs a perfect weapon or shield parry
        //private const float PARRY_DURATION = 0.1f;
        //private const float PARRY_SLOW     = 0.05f;
        //private const int   PARRY_PRIORITY = 1;

        // Kill: player kills an enemy
        private const float KILL_DURATION = 0.75f;

        private const float KILL_SLOW = 0.25f;
        private const int KILL_PRIORITY = 2;

        // Posture break: player breaks an enemy's posture
        private const float POSTURE_DURATION = 0.75f;

        private const float POSTURE_SLOW = 0.25f;
        private const int POSTURE_PRIORITY = 3;

        // Unique request ID – arbitrary, must not clash with other mods
        private const int TIME_REQUEST_ID = 741983;

        // ── State ────────────────────────────────────────────────────────────

        private DateTime _hitStopStart;
        private float _hitStopDuration = 0f;
        private bool _isInHitStop = false;
        private int _currentPriority = -1;

        // ── Lifecycle ────────────────────────────────────────────────────────

        public override void AfterStart()
        {
            Instance = this;
        }

        public override void OnRemoveBehavior()
        {
            if (Instance == this)
                Instance = null;
            EndHitStop();
        }

        // ── Per-frame: end hit stop once real time has elapsed ───────────────

        public override void OnMissionTick(float dt)
        {
            if (_isInHitStop && (DateTime.UtcNow - _hitStopStart).TotalSeconds >= _hitStopDuration)
                EndHitStop();
        }

        // ── Hit detection: player directly lands a damaging strike ───────────

        //public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent,
        //    in MissionWeapon attackerWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        //{
        //    if (affectorAgent == null || affectedAgent == null)
        //        return;
        //    if (!affectorAgent.IsPlayerControlled)
        //        return;
        //    if (affectedAgent.IsPlayerControlled || affectedAgent.IsMount)
        //        return;
        //    if (blow.IsFallDamage)
        //        return;
        //}

        // ── Kill detection: player kills an enemy ────────────────────────────

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent,
            AgentState agentState, KillingBlow killingBlow)
        {
            if (affectorAgent == null || affectedAgent == null)
                return;
            if (!affectorAgent.IsPlayerControlled)
                return;
            if (affectedAgent.IsPlayerControlled || affectedAgent.IsMount)
                return;
            if (agentState == AgentState.Killed)
                TriggerHitStop(KILL_DURATION, KILL_SLOW, KILL_PRIORITY);
        }

        // ── Static triggers called from StanceLogic ──────────────────────────

        public static void TriggerParryHitStop()
        {
            //Instance?.TriggerHitStop(PARRY_DURATION, PARRY_SLOW, PARRY_PRIORITY);
        }

        public static void TriggerPostureBreakHitStop()
        {
            Instance?.TriggerHitStop(POSTURE_DURATION, POSTURE_SLOW, POSTURE_PRIORITY);
        }

        // ── Internal helpers ─────────────────────────────────────────────────

        private void TriggerHitStop(float duration, float slowFactor, int priority)
        {
            if (_isInHitStop)
            {
                // Only a strictly higher-priority event may interrupt a running stop
                if (priority <= _currentPriority)
                    return;

                Mission.Current?.RemoveTimeSpeedRequest(TIME_REQUEST_ID);
            }

            _currentPriority = priority;
            _hitStopDuration = duration;
            _isInHitStop = true;
            _hitStopStart = DateTime.UtcNow;

            Mission.Current?.AddTimeSpeedRequest(new Mission.TimeSpeedRequest(slowFactor, TIME_REQUEST_ID));
        }

        private void EndHitStop()
        {
            if (!_isInHitStop)
                return;
            _isInHitStop = false;
            _currentPriority = -1;
            Mission.Current?.RemoveTimeSpeedRequest(TIME_REQUEST_ID);
        }
    }
}