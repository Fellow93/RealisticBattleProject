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
            // Two timers, so an enemy drifting across the radius doesn't make the soldier switch polearm <-> sidearm
            // over and over (a raw flag flipped every half second):
            //  - SidearmDelay: an enemy must stay within CloseEnemyRadius this long before the sidearm comes out, so a
            //    passing enemy (or a lance charge riding through) doesn't trigger a switch.
            //  - MainWeaponDelay: no enemy may be within CloseEnemyRadius for this long before the polearm comes back.
            //    Riders get it back sooner: they ride out of melee quickly and need the lance for the next charge.
            // Both are measured at CheckInterval granularity.
            private const float SidearmDelay = 2f;
            private const float MainWeaponDelay = 4f;
            private const float MountedMainWeaponDelay = 2f;
            private const float PolearmFavor = 35f;
            private const float SidearmFavor = 55f;

            // Agent -> in close combat (sidearm preferred), per the two timers above. Only agents carrying both a
            // melee polearm and a melee sidearm are tracked.
            public static readonly Dictionary<Agent, bool> enemyClose = new Dictionary<Agent, bool>();
            public static readonly Dictionary<Agent, float> nextCheck = new Dictionary<Agent, float>();
            // Agent -> when an enemy was first seen close, reset once none is. Drives SidearmDelay.
            public static readonly Dictionary<Agent, float> enemyCloseSince = new Dictionary<Agent, float>();
            // Agent -> when an enemy was last seen close. Drives MainWeaponDelay.
            public static readonly Dictionary<Agent, float> lastEnemyCloseTime = new Dictionary<Agent, float>();

            // A polearm to prefer and a sidearm to fall back to. A man with only a polearm has nothing to switch to,
            // and pushing his melee favor made the AI keep reaching for a sidearm he doesn't carry. Anything that can
            // be thrown (javelins, throwing axes/knives, throwable spears - all with a melee mode) counts as neither.
            private static bool IsThrowable(ItemObject item)
            {
                if (item.ItemType == ItemObject.ItemTypeEnum.Thrown)
                {
                    return true;
                }
                foreach (WeaponComponentData usage in item.Weapons)
                {
                    if (usage.IsConsumable || usage.IsRangedWeapon)
                    {
                        return true;
                    }
                }
                return false;
            }

            private static bool CarriesPolearmAndSidearm(Agent agent)
            {
                bool polearm = false;
                bool sidearm = false;
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.ExtraWeaponSlot; i++)
                {
                    MissionWeapon weapon = agent.Equipment[i];
                    if (weapon.IsEmpty || weapon.Item == null || IsThrowable(weapon.Item))
                    {
                        continue;
                    }
                    bool itemIsPolearm = false;
                    bool itemIsMelee = false;
                    foreach (WeaponComponentData usage in weapon.Item.Weapons)
                    {
                        if (usage.IsMeleeWeapon)
                        {
                            itemIsMelee = true;
                            if (usage.RelevantSkill == DefaultSkills.Polearm)
                            {
                                itemIsPolearm = true;
                            }
                        }
                    }
                    polearm |= itemIsPolearm;
                    sidearm |= itemIsMelee && !itemIsPolearm;
                }
                return polearm && sidearm;
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

                if (agent.Equipment == null || !CarriesPolearmAndSidearm(agent))
                {
                    enemyCloseSince.Remove(agent);
                    lastEnemyCloseTime.Remove(agent);
                    if (enemyClose.Remove(agent))
                    {
                        agent.UpdateAgentProperties();
                    }
                    return;
                }
                MBList<Agent> enemies = new MBList<Agent>();
                enemies = Mission.Current.GetNearbyEnemyAgents(agent.GetWorldPosition().AsVec2, CloseEnemyRadius, agent.Team, enemies);
                enemies.RemoveAll((Agent a) => a.IsRunningAway);
                if (enemies.Count > 0)
                {
                    lastEnemyCloseTime[agent] = currentTime;
                    if (!enemyCloseSince.ContainsKey(agent))
                    {
                        enemyCloseSince[agent] = currentTime;
                    }
                }
                else
                {
                    enemyCloseSince.Remove(agent);
                }
                enemyClose.TryGetValue(agent, out bool wasClose);
                bool close;
                if (wasClose)
                {
                    float mainWeaponDelay = agent.HasMount ? MountedMainWeaponDelay : MainWeaponDelay;
                    close = currentTime - lastEnemyCloseTime[agent] < mainWeaponDelay;
                }
                else
                {
                    close = enemyCloseSince.TryGetValue(agent, out float since) && currentTime - since >= SidearmDelay;
                }
                if (!enemyClose.ContainsKey(agent) || wasClose != close)
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
