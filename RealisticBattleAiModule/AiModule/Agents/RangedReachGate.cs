using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static partial class AgentAi
    {
        /// <summary>
        /// AI archers, crossbowmen, slingers and javelin/throwing-axe throwers hold fire while their target is beyond
        /// what their missile can reach.
        /// The engine's own shoot-range judgement overshoots RBM's slow, draggy missiles: crossbowmen opened fire at
        /// ~255 m on targets their bolts reach only at ~170-210 m and landed 50-75 m short, 60 m/s bows fired at
        /// ~217 m with ~208 m of reach, and slingers fired at 200-320 m with 160-250 m of reach and landed 30-95 m
        /// short (MissileAimTrace log, 2026-09-29).
        /// Same split as WeaponPreference:
        ///  - TickRangedReach (HumanAIComponent tick, main thread) measures, throttled, and refreshes the agent's
        ///    properties when the answer flips.
        ///  - ApplyRangedReach (SetAiRelatedProperties postfix) only reads the recorded flag.
        /// </summary>
        public static class RangedReachGate
        {
            private const float CheckInterval = 0.25f;
            // How often the target's identity is polled between measurements. GetTargetAgent is a native call and
            // ran every frame for every shooter; a new target now waits at most this long to be measured.
            private const float TargetCheckInterval = 0.1f;
            // Leeway past the computed reach: the target keeps walking in while the missile is in the air.
            private const float ReachSlack = 3f;
            // Hysteresis. Fire is held once the target is past reach + ReachSlack, and a shooter held for range is
            // released only once the target is a band closer than that. Horse archers circling at the edge of reach
            // flipped the answer up to four times a second, each flip a full UpdateAgentProperties. The band sits
            // below the hold line, not above it, so a released shooter still never looses past reach + ReachSlack.
            // The cost is that a held one waits for the target to cross the band: wide when either side is mounted
            // (circling riders, and a horse crosses 6 m in under a second), narrow on foot, where a target standing
            // just inside the hold line would otherwise never be shot at.
            private const float ReleaseBandMounted = 6f;
            private const float ReleaseBandFoot = 2f;
            // A shooter who loses his target keeps his current state this long before he is held. Targets drop out
            // for a moment between kills and re-picks, and each null used to hold and then release again.
            private const float NoTargetHoldDelay = 0.5f;

            // Agent -> holding fire; the value is true when held for range (the release band applies) and false when
            // held for want of a target. Only agents currently held are stored.
            public static readonly Dictionary<Agent, bool> holding = new Dictionary<Agent, bool>();
            public static readonly Dictionary<Agent, float> nextCheck = new Dictionary<Agent, float>();
            // Agent -> the target the last check measured. A new target is measured within TargetCheckInterval, not
            // at the next interval: a loaded crossbowman looses at a freshly picked target well inside 0.25 s, which
            // is how the opening volley at ~259 m slipped through when the gate only re-checked on the timer.
            public static readonly Dictionary<Agent, Agent> checkedTarget = new Dictionary<Agent, Agent>();
            // Agent -> the wielded slot the last check saw. A thrower carries his melee weapon until he means to throw
            // and lets fly right after switching to the javelin/axe; checked only on the timer, the gate had cleared
            // him while he held the sword and he threw before the next check.
            public static readonly Dictionary<Agent, EquipmentIndex> checkedWielded = new Dictionary<Agent, EquipmentIndex>();
            public static readonly Dictionary<Agent, float> nextTargetCheck = new Dictionary<Agent, float>();
            // Agent -> when his target was first seen missing, removed once he has one again.
            public static readonly Dictionary<Agent, float> noTargetSince = new Dictionary<Agent, float>();

            // Spread over [0.75, 1.25] of the interval, so shooters spawned in one frame don't all check in one frame.
            private static float Jittered(float interval)
            {
                return interval * (0.75f + 0.5f * MBRandom.RandomFloat);
            }

            public static void TickRangedReach(Agent agent, float currentTime)
            {
                if (!RBMConfig.RBMConfig.rbmCombatEnabled || Mission.Current == null || !agent.IsActive() || !agent.IsHuman || !agent.IsAIControlled)
                {
                    return;
                }
                // GetTargetAgent is a native call polled every TargetCheckInterval, so only for shooters and throwers
                // (IsRangedCached is launchers only; javelins and throwing axes are consumable, HasThrownCached).
                if (!agent.IsRangedCached && !agent.HasThrownCached)
                {
                    if (holding.Remove(agent))
                    {
                        agent.UpdateAgentProperties();
                    }
                    return;
                }
                // The wielded slot is a pointer read, so a weapon switch is still caught the frame it happens; only
                // the native target poll is throttled.
                EquipmentIndex wielded = agent.GetPrimaryWieldedItemIndex();
                bool sameWielded = checkedWielded.TryGetValue(agent, out EquipmentIndex lastWielded) && lastWielded == wielded;
                if (sameWielded && nextTargetCheck.TryGetValue(agent, out float nextTarget) && currentTime < nextTarget)
                {
                    return;
                }
                nextTargetCheck[agent] = currentTime + Jittered(TargetCheckInterval);
                Agent target = agent.GetTargetAgent();
                bool everChecked = checkedTarget.TryGetValue(agent, out Agent lastTarget);
                bool sameTarget = everChecked && lastTarget == target;
                if (sameTarget && sameWielded && nextCheck.TryGetValue(agent, out float next) && currentTime < next)
                {
                    return;
                }
                nextCheck[agent] = currentTime + Jittered(CheckInterval);
                checkedTarget[agent] = target;
                checkedWielded[agent] = wielded;

                bool wasHolding = holding.TryGetValue(agent, out bool heldForRange);
                bool holdWithoutTarget = wasHolding || !everChecked;
                if (target == null || !target.IsActive())
                {
                    if (!noTargetSince.TryGetValue(agent, out float since))
                    {
                        since = currentTime;
                        noTargetSince[agent] = since;
                    }
                    holdWithoutTarget |= currentTime - since >= NoTargetHoldDelay;
                }
                else
                {
                    noTargetSince.Remove(agent);
                }
                bool hold = IsTargetOutOfReach(agent, target, wasHolding && heldForRange, holdWithoutTarget, out bool forRange);
                if (hold)
                {
                    // Managed only; the reason can change (no target -> out of range) without a property refresh.
                    holding[agent] = forRange;
                }
                if (hold != wasHolding)
                {
                    if (!hold)
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

            private static bool IsTargetOutOfReach(Agent agent, Agent target, bool heldForRange, bool holdWithoutTarget, out bool forRange)
            {
                forRange = false;
                MissionWeapon launcher = agent.WieldedWeapon;
                if (launcher.IsEmpty || launcher.CurrentUsageItem == null)
                {
                    return false;
                }
                WeaponClass launcherClass = launcher.CurrentUsageItem.WeaponClass;
                bool thrown = launcherClass == WeaponClass.Javelin || launcherClass == WeaponClass.ThrowingAxe;
                if (!thrown && launcherClass != WeaponClass.Crossbow && launcherClass != WeaponClass.Bow && launcherClass != WeaponClass.Sling)
                {
                    return false;
                }
                // No target yet: stay held, so the first target he picks is measured before he may shoot at it. A
                // shooter who only just lost his target keeps his current state for NoTargetHoldDelay first (one
                // never measured is held at once).
                if (target == null || !target.IsActive())
                {
                    return holdWithoutTarget;
                }
                // Outside RBMCombat's spawn and shot patches the launcher's MissileSpeed holds its draw weight. A throw
                // flies at the speed RBMCombat's WeaponEquipped prefix gave the engine (MissileBallistics.GetThrowSpeed).
                // AI javelins thrown at 30-40 m targets came down 10-23 m out (THROW/LAND log, 2026-10-01).
                int speed = thrown
                    ? RBMConfig.MissileBallistics.GetThrowSpeed(agent, launcher)
                    : launcherClass == WeaponClass.Sling
                        ? RBMConfig.MissileBallistics.GetSlingSpeed(agent, launcher, launcher.CurrentUsageItem, launcher.CurrentUsageItem.MissileSpeed)
                        : RBMConfig.MissileBallistics.GetLauncherSpeed(agent, launcher, launcher.CurrentUsageItem, launcher.CurrentUsageItem.MissileSpeed);
                if (speed <= 0)
                {
                    return false;
                }
                Vec3 from = agent.GetEyeGlobalPosition();
                Vec3 to = target.GetChestGlobalPosition();
                float distance = (to.AsVec2 - from.AsVec2).Length;
                // The drag the missile flies with is its ammo's (ItemObject.GetAirFrictionConstant): arrows and bolts
                // AirFrictionArrow, a sling's stone AirFrictionBullet (not the sling's own AirFrictionStone); a thrown
                // weapon is its own ammo.
                float friction = thrown
                    ? ItemObject.GetAirFrictionConstant(launcherClass, launcher.CurrentUsageItem.WeaponFlags)
                    : ItemObject.GetAirFrictionConstant(launcher.CurrentUsageItem.AmmoClass, (WeaponFlags)0);
                float holdLine = RBMConfig.MissileBallistics.MaxReach(speed, to.z - from.z, friction) + ReachSlack;
                if (heldForRange)
                {
                    holdLine -= agent.HasMount || target.HasMount ? ReleaseBandMounted : ReleaseBandFoot;
                }
                forRange = distance > holdLine;
                return forRange;
            }
        }
    }
}
