using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static partial class AgentAi
    {
        /// <summary>
        /// AI archers and crossbowmen hold fire while their target is beyond what their missile can reach. The
        /// engine's own shoot-range judgement overshoots RBM's slow, draggy missiles: crossbowmen opened fire at
        /// ~255 m on targets their bolts reach only at ~170-210 m and landed 50-75 m short, and 60 m/s bows fired at
        /// ~217 m with ~208 m of reach (MissileAimTrace log, 2026-09-29).
        /// Same split as WeaponPreference:
        ///  - TickRangedReach (HumanAIComponent tick, main thread) measures, throttled, and refreshes the agent's
        ///    properties when the answer flips.
        ///  - ApplyRangedReach (SetAiRelatedProperties postfix) only reads the recorded flag.
        /// </summary>
        public static class RangedReachGate
        {
            private const float CheckInterval = 0.25f;
            // Leeway past the computed reach: the target keeps walking in while the missile is in the air.
            private const float ReachSlack = 3f;

            // Agent -> holding fire. Only agents currently held are stored.
            public static readonly Dictionary<Agent, bool> holding = new Dictionary<Agent, bool>();
            public static readonly Dictionary<Agent, float> nextCheck = new Dictionary<Agent, float>();
            // Agent -> the target the last check measured. A new target is measured at once, not at the next
            // interval: a loaded crossbowman looses at a freshly picked target well inside 0.25 s, which is how the
            // opening volley at ~259 m slipped through when the gate only re-checked on the timer.
            public static readonly Dictionary<Agent, Agent> checkedTarget = new Dictionary<Agent, Agent>();

            public static void TickRangedReach(Agent agent, float currentTime)
            {
                if (!RBMConfig.RBMConfig.rbmCombatEnabled || Mission.Current == null || !agent.IsActive() || !agent.IsHuman || !agent.IsAIControlled)
                {
                    return;
                }
                // GetTargetAgent is a native call and runs every tick, so only for shooters.
                if (!agent.IsRangedCached)
                {
                    if (holding.Remove(agent))
                    {
                        agent.UpdateAgentProperties();
                    }
                    return;
                }
                Agent target = agent.GetTargetAgent();
                bool sameTarget = checkedTarget.TryGetValue(agent, out Agent lastTarget) && lastTarget == target;
                if (sameTarget && nextCheck.TryGetValue(agent, out float next) && currentTime < next)
                {
                    return;
                }
                nextCheck[agent] = currentTime + CheckInterval;
                checkedTarget[agent] = target;

                bool hold = IsTargetOutOfReach(agent, target);
                if (hold != holding.ContainsKey(agent))
                {
                    if (hold)
                    {
                        holding[agent] = true;
                    }
                    else
                    {
                        holding.Remove(agent);
                    }
                    agent.UpdateAgentProperties();
                }
            }

            public static void ApplyRangedReach(Agent agent, AgentDrivenProperties agentDrivenProperties)
            {
                if (agent != null && holding.ContainsKey(agent))
                {
                    agentDrivenProperties.AiShootFreq = 0f;
                }
            }

            private static bool IsTargetOutOfReach(Agent agent, Agent target)
            {
                MissionWeapon launcher = agent.WieldedWeapon;
                if (launcher.IsEmpty || launcher.CurrentUsageItem == null ||
                    (launcher.CurrentUsageItem.WeaponClass != WeaponClass.Crossbow && launcher.CurrentUsageItem.WeaponClass != WeaponClass.Bow))
                {
                    return false;
                }
                // No target yet: stay held, so the first target he picks is measured before he may shoot at it.
                if (target == null || !target.IsActive())
                {
                    return true;
                }
                // Outside RBMCombat's spawn and shot patches the launcher's MissileSpeed holds its draw weight.
                int speed = RBMConfig.MissileBallistics.GetLauncherSpeed(agent, launcher, launcher.CurrentUsageItem, launcher.CurrentUsageItem.MissileSpeed);
                if (speed <= 0)
                {
                    return false;
                }
                Vec3 from = agent.GetEyeGlobalPosition();
                Vec3 to = target.GetChestGlobalPosition();
                float distance = (to.AsVec2 - from.AsVec2).Length;
                // Bows, crossbows, arrows and bolts all fly with AirFrictionArrow (ItemObject.GetAirFrictionConstant).
                float friction = ManagedParameters.Instance.GetManagedParameter(ManagedParametersEnum.AirFrictionArrow);
                return distance > RBMConfig.MissileBallistics.MaxReach(speed, to.z - from.z, friction) + ReachSlack;
            }
        }
    }
}
