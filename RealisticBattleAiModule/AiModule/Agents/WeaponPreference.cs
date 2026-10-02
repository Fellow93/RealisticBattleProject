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
            // Agent -> CarriesPolearmAndSidearm, valid while the four weapon slots hold the same items. Walking every
            // usage of every carried item was most of this check's cost, and the slots only change on a pickup or drop.
            public static readonly Dictionary<Agent, LoadoutCache> loadoutCache = new Dictionary<Agent, LoadoutCache>();

            public struct LoadoutCache
            {
                public ItemObject Slot0;
                public ItemObject Slot1;
                public ItemObject Slot2;
                public ItemObject Slot3;
                public bool CarriesBoth;
            }

            // A rider moving faster than this (m/s) is riding through, not fighting where he stands: no proximity
            // query, and no close contact counted toward the sidearm. Lancers used to swap to the sword and back as
            // they passed through a formation. Once he is bogged down below it the normal checks resume.
            private const float MountedPassingSpeed = 4f;

            // Main thread only (HumanAIComponent.OnTick); cleared after every use.
            private static readonly MBList<Agent> _nearbyEnemies = new MBList<Agent>();

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

            private static ItemObject SlotItem(Agent agent, EquipmentIndex index)
            {
                MissionWeapon weapon = agent.Equipment[index];
                return weapon.IsEmpty ? null : weapon.Item;
            }

            private static bool CarriesPolearmAndSidearmCached(Agent agent)
            {
                ItemObject slot0 = SlotItem(agent, EquipmentIndex.Weapon0);
                ItemObject slot1 = SlotItem(agent, EquipmentIndex.Weapon1);
                ItemObject slot2 = SlotItem(agent, EquipmentIndex.Weapon2);
                ItemObject slot3 = SlotItem(agent, EquipmentIndex.Weapon3);
                if (loadoutCache.TryGetValue(agent, out LoadoutCache cached) &&
                    cached.Slot0 == slot0 && cached.Slot1 == slot1 && cached.Slot2 == slot2 && cached.Slot3 == slot3)
                {
                    return cached.CarriesBoth;
                }
                bool carriesBoth = CarriesPolearmAndSidearm(agent);
                loadoutCache[agent] = new LoadoutCache { Slot0 = slot0, Slot1 = slot1, Slot2 = slot2, Slot3 = slot3, CarriesBoth = carriesBoth };
                return carriesBoth;
            }

            private static bool IsNonRoutingEnemyClose(Agent agent)
            {
                Mission.Current.GetNearbyEnemyAgents(agent.Position.AsVec2, CloseEnemyRadius, agent.Team, _nearbyEnemies);
                bool found = false;
                for (int i = 0; i < _nearbyEnemies.Count; i++)
                {
                    if (!_nearbyEnemies[i].IsRunningAway)
                    {
                        found = true;
                        break;
                    }
                }
                _nearbyEnemies.Clear();
                return found;
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
                // Jittered: an army spawned in one frame otherwise ran every one of these checks in the same frame,
                // forever. The delays below are timestamps, so the jitter only moves the check granularity.
                nextCheck[agent] = currentTime + CheckInterval * (0.75f + 0.5f * MBRandom.RandomFloat);

                if (agent.Equipment == null || !CarriesPolearmAndSidearmCached(agent))
                {
                    enemyCloseSince.Remove(agent);
                    lastEnemyCloseTime.Remove(agent);
                    if (enemyClose.Remove(agent))
                    {
                        agent.UpdateAgentProperties();
                    }
                    return;
                }
                Agent mount = agent.MountAgent;
                bool enemyNear = (mount == null || mount.GetCurrentVelocity().LengthSquared <= MountedPassingSpeed * MountedPassingSpeed)
                    && IsNonRoutingEnemyClose(agent);
                if (enemyNear)
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
