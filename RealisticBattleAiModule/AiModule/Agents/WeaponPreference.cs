using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static partial class AgentAi
    {
        /// <summary>
        /// AI troops carrying a melee polearm prefer it, and only switch to their sidearm once an enemy is really
        /// close. This is the weapon-favor bias removed from SetAiRelatedProperties on 2026-09-15 (it ran a proximity
        /// query inside Agent.Build and deadlocked reinforcement waves), split in two so the stat pipeline never
        /// touches the proximity map:
        ///  - TickWeaponPreference (HumanAIComponent tick, main thread) does the nearby-enemy query, throttled, and
        ///    records whether an enemy is close; when that flips it refreshes the agent's properties.
        ///  - ApplyWeaponPreference (SetAiRelatedProperties postfix) only reads the recorded flag.
        /// </summary>
        public static class WeaponPreference
        {
            private const float CheckInterval = 0.5f;
            private const float CloseEnemyRadius = 2.5f;
            private const float PolearmFavor = 35f;
            private const float SidearmFavor = 55f;

            // Agent -> is a (non-fleeing) enemy within CloseEnemyRadius. Only agents carrying a melee polearm are tracked.
            public static readonly Dictionary<Agent, bool> enemyClose = new Dictionary<Agent, bool>();
            public static readonly Dictionary<Agent, float> nextCheck = new Dictionary<Agent, float>();

            private static bool CarriesMeleePolearm(Agent agent)
            {
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.ExtraWeaponSlot; i++)
                {
                    MissionWeapon weapon = agent.Equipment[i];
                    if (weapon.IsEmpty || weapon.Item == null)
                    {
                        continue;
                    }
                    foreach (WeaponComponentData usage in weapon.Item.Weapons)
                    {
                        if (usage.IsMeleeWeapon && !usage.IsConsumable && usage.RelevantSkill == DefaultSkills.Polearm)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            public static void TickWeaponPreference(Agent agent, float currentTime)
            {
                if (Mission.Current == null || !Mission.Current.IsDeploymentFinished || agent.Team == null ||
                    !agent.IsActive() || !agent.IsHuman || !agent.IsAIControlled)
                {
                    return;
                }
                if (nextCheck.TryGetValue(agent, out float next) && currentTime < next)
                {
                    return;
                }
                nextCheck[agent] = currentTime + CheckInterval;

                if (agent.Equipment == null || !CarriesMeleePolearm(agent))
                {
                    if (enemyClose.Remove(agent))
                    {
                        agent.UpdateAgentProperties();
                    }
                    return;
                }
                MBList<Agent> enemies = new MBList<Agent>();
                enemies = Mission.Current.GetNearbyEnemyAgents(agent.GetWorldPosition().AsVec2, CloseEnemyRadius, agent.Team, enemies);
                enemies.RemoveAll((Agent a) => a.IsRunningAway);
                bool close = enemies.Count > 0;
                if (!enemyClose.TryGetValue(agent, out bool wasClose) || wasClose != close)
                {
                    enemyClose[agent] = close;
                    agent.UpdateAgentProperties();
                }
            }

            public static void ApplyWeaponPreference(Agent agent, AgentDrivenProperties agentDrivenProperties)
            {
                if (agent == null || !enemyClose.TryGetValue(agent, out bool close))
                {
                    return;
                }
                if (close)
                {
                    agentDrivenProperties.AiWeaponFavorMultiplierMelee = SidearmFavor;
                }
                else
                {
                    agentDrivenProperties.AiWeaponFavorMultiplierPolearm = PolearmFavor;
                }
            }
        }
    }
}
