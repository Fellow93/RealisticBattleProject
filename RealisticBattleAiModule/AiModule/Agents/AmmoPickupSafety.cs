using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static partial class AgentAi
    {
        /// <summary>
        /// Keeps AI missile troops from walking into the enemy to collect spent arrows, bolts, stones and javelins.
        /// Vanilla (HumanAIComponent.ItemPickupTick / SelectPickableItem) only looks at the agent's own target: it
        /// picks ammo up once that target is over 20 m away, and only skips items lying toward it. Every other enemy
        /// is ignored, so skirmishing archers chained pickup after pickup across the field into enemy infantry.
        ///  - The IsItemAvailableForAgent postfix refuses ammo with a non-routing enemy near it, while the agent still
        ///    holds 15% or more of its total ammo of that class, or while it is drawing, shooting or reloading.
        ///  - AbortIfUnsafe (HumanAIComponent.OnTick postfix) drops a pickup already under way on the same conditions.
        /// Shields and banners are left to vanilla.
        /// </summary>
        public static class AmmoPickupSafety
        {
            private const float FootDangerRadius = 25f;
            // Horsemen close the distance several times faster.
            private const float MountedDangerRadius = 45f;

            // Main thread only (both callers run from HumanAIComponent.OnTick); cleared after every use.
            private static readonly MBList<Agent> _nearbyEnemies = new MBList<Agent>();

            private static bool IsAmmo(SpawnedItemEntity item)
            {
                MissionWeapon weapon = item.WeaponCopy;
                if (weapon.IsEmpty || weapon.Item?.PrimaryWeapon == null)
                {
                    return false;
                }
                switch (weapon.Item.PrimaryWeapon.WeaponClass)
                {
                    case WeaponClass.Arrow:
                    case WeaponClass.Bolt:
                    case WeaponClass.SlingStone:
                    case WeaponClass.Javelin:
                    case WeaponClass.ThrowingAxe:
                    case WeaponClass.ThrowingKnife:
                        return true;
                    default:
                        return false;
                }
            }

            private static bool IsEnemyNear(Agent agent, Vec2 position)
            {
                if (agent.Team == null || Mission.Current == null)
                {
                    return false;
                }
                Mission.Current.GetNearbyEnemyAgents(position, MountedDangerRadius, agent.Team, _nearbyEnemies);
                bool danger = false;
                foreach (Agent enemy in _nearbyEnemies)
                {
                    if (!enemy.IsActive() || !enemy.IsHuman || enemy.IsRunningAway)
                    {
                        continue;
                    }
                    float radius = enemy.HasMount ? MountedDangerRadius : FootDangerRadius;
                    if (enemy.Position.AsVec2.DistanceSquared(position) < radius * radius)
                    {
                        danger = true;
                        break;
                    }
                }
                _nearbyEnemies.Clear();
                return danger;
            }

            private static bool IsUnsafe(Agent agent, SpawnedItemEntity item)
            {
                return agent.IsAIControlled && IsAmmo(item)
                    && (IsShooting(agent) || HasEnoughAmmo(agent, item) || IsEnemyNear(agent, item.GameEntityWithWorldPosition.AsVec2));
            }

            // Drawing/aiming, releasing or reloading a missile weapon. Vanilla only checks this before starting a scan,
            // so an agent could still break off a shot for a pickup it had already chosen.
            private static bool IsShooting(Agent agent)
            {
                switch (agent.GetCurrentActionType(1))
                {
                    case Agent.ActionCodeType.ReadyRanged:
                    case Agent.ActionCodeType.ReleaseRanged:
                    case Agent.ActionCodeType.ReleaseThrowing:
                    case Agent.ActionCodeType.Reload:
                        return true;
                    default:
                        return false;
                }
            }

            // Vanilla lets the AI refill any single slot at half or below. Only go looking once the agent is nearly out:
            // total ammo of this class across every slot below 15% of its total capacity.
            private const float PickupAmmoFraction = 0.15f;

            private static bool HasEnoughAmmo(Agent agent, SpawnedItemEntity item)
            {
                WeaponClass weaponClass = item.WeaponCopy.Item.PrimaryWeapon.WeaponClass;
                int amount = 0;
                int maxAmount = 0;
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.ExtraWeaponSlot; i++)
                {
                    MissionWeapon weapon = agent.Equipment[i];
                    if (!weapon.IsEmpty && weapon.Item?.PrimaryWeapon != null && weapon.Item.PrimaryWeapon.WeaponClass == weaponClass)
                    {
                        amount += weapon.Amount;
                        maxAmount += weapon.ModifiedMaxAmount;
                    }
                }
                // Thrown-weapon stacks are too small for the fraction alone (15% of 3 is 0.45): also refill at 1 left.
                bool isThrown = weaponClass == WeaponClass.Javelin || weaponClass == WeaponClass.ThrowingAxe || weaponClass == WeaponClass.ThrowingKnife;
                if (isThrown && amount <= 1)
                {
                    return false;
                }
                return maxAmount > 0 && amount >= maxAmount * PickupAmmoFraction;
            }

            // Called from the OnTick postfix while the agent is walking to a pickup. Returns true if it was stopped.
            public static bool AbortIfUnsafe(Agent agent, SpawnedItemEntity itemToPickUp)
            {
                if (!IsUnsafe(agent, itemToPickUp))
                {
                    return false;
                }
                agent.StopUsingGameObject(isSuccessful: false);
                return true;
            }

            [HarmonyPatch(typeof(DefaultItemPickupModel))]
            [HarmonyPatch("IsItemAvailableForAgent")]
            internal static class IsItemAvailableForAgentPatch
            {
                private static void Postfix(SpawnedItemEntity item, Agent agent, ref bool __result)
                {
                    if (__result && IsUnsafe(agent, item))
                    {
                        __result = false;
                    }
                }
            }
        }
    }
}
