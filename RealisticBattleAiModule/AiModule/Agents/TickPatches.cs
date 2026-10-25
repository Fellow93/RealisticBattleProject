using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using SandBox.GameComponents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.ArrangementOrder;

namespace RBMAI
{
    public static partial class AgentAi
    {
        [HarmonyPatch(typeof(HumanAIComponent))]
        [HarmonyPatch("OnTick")]
        public static class OnTickPatch
        {
            public static Dictionary<Agent, float> itemPickupDistanceStorage = new Dictionary<Agent, float> { };

            // Agents routed by a cavalry charge; only these are rallied (ordered retreats / morale routs stay).
            public static HashSet<Agent> chargeRoutedAgents = new HashSet<Agent>();

            public static Dictionary<Agent, float> meleePickupNextScan = new Dictionary<Agent, float>();

            public sealed class RiderCrowdState
            {
                public float nextCheck = float.MinValue;
                public bool crowded = false;
            }

            public static Dictionary<Agent, RiderCrowdState> riderCrowdStates = new Dictionary<Agent, RiderCrowdState>();

            // Main thread only: one shared buffer serves every rider.
            private static readonly MBList<Agent> _riderCrowdScratch = new MBList<Agent>();

            // Rider hemmed in by 3+ foot soldiers, at least one an enemy (horses are agents too, not counted).
            private static bool IsRiderCrowdedByFoot(Agent rider)
            {
                MBList<Agent> nearby = _riderCrowdScratch;
                Mission.Current.GetNearbyAgents(rider.Position.AsVec2, 1.25f, nearby);
                int footCount = 0;
                bool anyEnemy = false;
                for (int i = 0; i < nearby.Count; i++)
                {
                    Agent other = nearby[i];
                    if (!other.IsHuman || other.HasMount)
                    {
                        continue;
                    }
                    footCount++;
                    if (!anyEnemy && other.IsEnemyOf(rider))
                    {
                        anyEnemy = true;
                    }
                }
                // Don't keep agents reachable from a static between frames.
                nearby.Clear();
                return footCount >= 3 && anyEnemy;
            }

            private const float MeleePickupSearchRadius = 15f;
            private static readonly WeakGameEntity[] _meleePickupEntities = new WeakGameEntity[64];
            private static readonly UIntPtr[] _meleePickupIds = new UIntPtr[64];

            private static bool HasMeleeUsage(ItemObject item)
            {
                if (item?.Weapons == null)
                {
                    return false;
                }
                foreach (WeaponComponentData usage in item.Weapons)
                {
                    if (usage.IsMeleeWeapon)
                    {
                        return true;
                    }
                }
                return false;
            }

            // True if the agent still has something to fight with (melee weapon, throwing weapon with ammo, launcher + ammo).
            private static bool IsArmed(Agent agent)
            {
                bool hasLauncher = false;
                bool hasAmmo = false;
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.ExtraWeaponSlot; i++)
                {
                    MissionWeapon weapon = agent.Equipment[i];
                    if (weapon.IsEmpty)
                    {
                        continue;
                    }
                    if (HasMeleeUsage(weapon.Item))
                    {
                        return true;
                    }
                    WeaponComponentData usage = weapon.CurrentUsageItem;
                    if (usage == null)
                    {
                        continue;
                    }
                    if (usage.IsRangedWeapon)
                    {
                        if (!usage.IsConsumable)
                        {
                            hasLauncher = true;
                        }
                        else if (weapon.Amount > 0)
                        {
                            return true;
                        }
                    }
                    else if (usage.IsAmmo && weapon.Amount > 0)
                    {
                        hasAmmo = true;
                    }
                }
                return hasLauncher && hasAmmo;
            }

            private static bool SpawnedWithMeleeWeapon(Agent agent)
            {
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.ExtraWeaponSlot; i++)
                {
                    if (HasMeleeUsage(agent.SpawnEquipment[i].Item))
                    {
                        return true;
                    }
                }
                return false;
            }

            // Vanilla AI never picks up a dropped melee weapon, so a disarmed agent punched all fight. This picks one
            // up for agents that spawned with a melee weapon and now have nothing; runs after vanilla's pickup on its own timer.
            private static void TrySeekMeleeWeapon(HumanAIComponent humanAi, Agent agent, ref SpawnedItemEntity itemToPickUp, bool forceDisableItemPickup, float currentTime)
            {
                if (itemToPickUp != null || forceDisableItemPickup || !agent.IsActive() || !agent.IsHuman || !agent.IsAIControlled
                    || agent.Mission == null || !agent.Mission.AllowAiTicking || agent.Mission.MissionEnded || agent.MountAgent != null)
                {
                    return;
                }
                if (meleePickupNextScan.TryGetValue(agent, out float nextScan) && currentTime < nextScan)
                {
                    return;
                }
                meleePickupNextScan[agent] = currentTime + 2f + MBRandom.RandomFloat;

                if (!agent.IsAlarmed() || agent.IsRunningAway || (agent.GetAgentFlags() & AgentFlag.CanAttack) == 0 || !agent.CanBeAssignedForScriptedMovement()
                    || humanAi.IsInImportantCombatAction() || agent.IsInWater() || IsArmed(agent) || !SpawnedWithMeleeWeapon(agent))
                {
                    return;
                }

                Vec3 bMin = agent.Position - new Vec3(MeleePickupSearchRadius, MeleePickupSearchRadius, 1f);
                Vec3 bMax = agent.Position + new Vec3(MeleePickupSearchRadius, MeleePickupSearchRadius, 1.8f);
                int count = agent.Mission.Scene.SelectEntitiesInBoxWithScriptComponent<SpawnedItemEntity>(ref bMin, ref bMax, _meleePickupEntities, _meleePickupIds, isFixedTick: false);
                SpawnedItemEntity best = null;
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    SpawnedItemEntity item = _meleePickupEntities[i].GetFirstScriptOfType<SpawnedItemEntity>();
                    if (item == null)
                    {
                        continue;
                    }
                    MissionWeapon weapon = item.WeaponCopy;
                    if (weapon.IsEmpty || weapon.IsBanner() || item.IsStuckMissile() || item.IsQuiverAndNotEmpty()
                        || weapon.Item.ItemFlags.HasAnyFlag(ItemFlags.CannotBePickedUp | ItemFlags.DropOnWeaponChange | ItemFlags.DropOnAnyAction)
                        || !HasMeleeUsage(weapon.Item))
                    {
                        continue;
                    }
                    if (item.HasUser || (item.HasAIMovingTo && !item.IsAIMovingTo(agent)) || item.IsDisabledForAgent(agent)
                        || item.GameEntityWithWorldPosition.GetNavMesh() == UIntPtr.Zero)
                    {
                        continue;
                    }
                    EquipmentIndex slot = MissionEquipment.SelectWeaponPickUpSlot(agent, weapon, isStuckMissile: false);
                    if (slot == EquipmentIndex.None || !agent.Equipment[slot].IsEmpty)
                    {
                        continue;
                    }
                    float distSq = item.GameEntityWithWorldPosition.AsVec2.DistanceSquared(agent.Position.AsVec2);
                    if (distSq >= bestDistSq || !agent.CanMoveDirectlyToPosition(item.GameEntityWithWorldPosition.AsVec2))
                    {
                        continue;
                    }
                    best = item;
                    bestDistSq = distSq;
                }
                if (best != null)
                {
                    itemToPickUp = best;
                    best.MovingAgent?.StopUsingGameObject(isSuccessful: false);
                    humanAi.MoveToUsableGameObject(best, null);
                }
            }

            // StopRetreating only clears the flags; rebuild the shield stance and target the formation would have given.
            private static void RestoreAiAfterRally(Agent agent)
            {
                agent.SetAutomaticTargetSelection(true);
                agent.InvalidateTargetAgent();
                agent.ResetEnemyCaches();

                Formation formation = agent.Formation;
                HumanAIComponent humanAi = agent.HumanAIComponent;
                if (formation != null && humanAi != null)
                {
                    humanAi.RefreshBehaviorValues(formation.GetReadonlyMovementOrderReference().OrderEnum, formation.ArrangementOrder.OrderEnum);
                    agent.UpdateFormationOrders();
                }
            }

            private static void Postfix(HumanAIComponent __instance, ref SpawnedItemEntity ____itemToPickUp, ref Agent ___Agent, bool ____forceDisableItemPickup)
            {
                // Native never releases a Frontline pin for a man in the water, so drop it as soon as he is in the water.
                if (___Agent.IsActive() && !___Agent.IsOnLand()
                    && Frontline.aiDecisionCooldownDict.TryGetValue(___Agent, out Frontline.AIDecisionState frontlinePin)
                    && frontlinePin != null && frontlinePin.AIMindset.shouldClearTargetFrame)
                {
                    ___Agent.ClearTargetFrame();
                    frontlinePin.AIMindset.shouldClearTargetFrame = false;
                }
                // Bearers are normally held by BannerBearerAttackGate; one who picked a banner up mid-battle has no gate, so clear his target.
                // Never park a bearer's target on a squadmate: he faces it and walks sideways/backward.
                if (Mission.Current != null && ___Agent.IsActive() && RBMAI.Utilities.IsBannerBearer(___Agent)
                    && ___Agent.GetComponent<BannerBearerAttackGate>() == null
                    && !BannerBearerAttackGate.HasFightingEnemyClose(___Agent))
                {
                    ___Agent.InvalidateTargetAgent();
                }
                float currentTime = MBCommon.GetTotalMissionTime();
                WeaponPreference.TickWeaponPreference(___Agent, currentTime);
                RangedReachGate.TickRangedReach(___Agent, currentTime);
                // Ranged AI judges range without its ammo limit. Set here on the main thread (native write), not in the
                // SetAiRelatedProperties postfix, which can run on a parallel worker. Re-checked each tick.
                if (___Agent.IsRangedCached && ___Agent.IsActive())
                {
                    Agent.AISpecialCombatModeFlags combatFlags = ___Agent.GetScriptedCombatFlags();
                    if ((combatFlags & Agent.AISpecialCombatModeFlags.IgnoreAmmoLimitForRangeCalculation) == 0)
                    {
                        ___Agent.SetScriptedCombatFlags(combatFlags | Agent.AISpecialCombatModeFlags.IgnoreAmmoLimitForRangeCalculation);
                    }
                }
                // A rider hemmed in by 3+ foot soldiers (at least one an enemy) pushes forward; a crowd of only friends is left alone.
                // Not for a formation ordered to dismount (the engine only dismounts a nearly stopped rider).
                // The crowd test takes Mission's global agent-query lock, so the verdict is re-taken every 0.2-0.3 s (jittered)
                // and re-applied every frame in between.
                if (___Agent.IsActive() && ___Agent.HasMount
                    && ___Agent.Formation?.RidingOrder.OrderEnum != RidingOrder.RidingOrderEnum.Dismount)
                {
                    if (!riderCrowdStates.TryGetValue(___Agent, out RiderCrowdState crowdState))
                    {
                        crowdState = new RiderCrowdState();
                        riderCrowdStates[___Agent] = crowdState;
                    }
                    if (currentTime >= crowdState.nextCheck)
                    {
                        crowdState.crowded = IsRiderCrowdedByFoot(___Agent);
                        crowdState.nextCheck = currentTime + MBRandom.RandomFloatRanged(0.2f, 0.3f);
                    }
                    if (crowdState.crowded)
                    {
                        ___Agent.EventControlFlags &= ~Agent.EventControlFlag.DoubleTapToDirectionMask;
                        ___Agent.EventControlFlags |= Agent.EventControlFlag.DoubleTapToDirectionUp;

                        ___Agent.MovementInputVector = ___Agent.LookDirection.AsVec2 * 2f;
                    }
                }
                // Rally only charge-routed agents: an ordered retreat has IsRetreating with full morale and would be un-retreated every tick.
                // A charge-routed agent whose formation was later ordered to retreat is left alone (the order is not re-applied).
                CommonAIComponent rallyAi = ___Agent.CommonAIComponent;
                if (rallyAi != null && chargeRoutedAgents.Contains(___Agent) && ___Agent.GetMorale() > 0f && currentTime - ___Agent.LastRecievedMeleeHitTime > 10f
                    && ___Agent.Formation?.GetReadonlyMovementOrderReference().OrderEnum != MovementOrder.MovementOrderEnum.Retreat)
                {
                    chargeRoutedAgents.Remove(___Agent);
                    bool wasRetreating = rallyAi.IsRetreating;
                    rallyAi.StopRetreating();
                    if (wasRetreating && !rallyAi.IsRetreating)
                    {
                        RestoreAiAfterRally(___Agent);
                    }
                }
                TrySeekMeleeWeapon(__instance, ___Agent, ref ____itemToPickUp, ____forceDisableItemPickup, currentTime);
                if (____itemToPickUp != null && (___Agent.AIStateFlags & Agent.AIStateFlag.UseObjectMoving) != 0)
                {
                    if (AmmoPickupSafety.AbortIfUnsafe(___Agent, ____itemToPickUp))
                    {
                        itemPickupDistanceStorage.Remove(___Agent);
                        return;
                    }
                    float num = MissionGameModels.Current.AgentStatCalculateModel.GetInteractionDistance(___Agent) * 3f;
                    WorldFrame userFrameForAgent = ____itemToPickUp.GetUserFrameForAgent(___Agent);
                    ref WorldPosition origin = ref userFrameForAgent.Origin;
                    Vec3 targetPoint = ___Agent.Position;
                    float distanceSq = origin.DistanceSquaredWithLimit(in targetPoint, num * num + 1E-05f);
                    if (!itemPickupDistanceStorage.TryGetValue(___Agent, out float newDist))
                    {
                        itemPickupDistanceStorage[___Agent] = distanceSq;
                    }
                    else
                    {
                        if (Math.Abs(distanceSq - newDist) < 1E-05f)
                        {
                            ___Agent.StopUsingGameObject(isSuccessful: false);
                            itemPickupDistanceStorage.Remove(___Agent);
                        }
                        itemPickupDistanceStorage[___Agent] = distanceSq;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Formation))]
        [HarmonyPatch("ApplyActionOnEachUnit", new Type[] { typeof(Action<Agent>), typeof(Agent) })]
        internal class ApplyActionOnEachUnitPatch
        {
            // Iterates a copy of the roster (as ApplyActionOnEachUnitViaBackupList) so actions may move units between formations.
            // Pooled per-thread lists indexed by nesting depth; [ThreadStatic] because the parallel movement job reaches this too.
            [ThreadStatic] private static List<List<Agent>> _snapshotPool;
            [ThreadStatic] private static int _snapshotDepth;

            private static bool Prefix(ref Action<Agent> action, ref Agent ignoreAgent, ref Formation __instance, List<Agent> ____detachedUnits)
            {
                try
                {
                    List<List<Agent>> pool = _snapshotPool ?? (_snapshotPool = new List<List<Agent>>());
                    int depth = _snapshotDepth;
                    if (depth == pool.Count)
                    {
                        pool.Add(new List<Agent>());
                    }
                    List<Agent> snapshot = pool[depth];
                    _snapshotDepth = depth + 1;
                    try
                    {
                        // ignoreAgent is honoured, as native does.
                        snapshot.Clear();
                        MBReadOnlyList<IFormationUnit> attachedUnits = __instance.Arrangement.GetAllUnits();
                        for (int i = 0; i < attachedUnits.Count; i++)
                        {
                            Agent unit = (Agent)attachedUnits[i];
                            if (unit != ignoreAgent)
                            {
                                snapshot.Add(unit);
                            }
                        }
                        for (int i = 0; i < snapshot.Count; i++)
                        {
                            action(snapshot[i]);
                        }
                        snapshot.Clear();
                        if (____detachedUnits != null)
                        {
                            for (int i = 0; i < ____detachedUnits.Count; i++)
                            {
                                Agent unit = ____detachedUnits[i];
                                if (unit != ignoreAgent)
                                {
                                    snapshot.Add(unit);
                                }
                            }
                        }
                        for (int i = 0; i < snapshot.Count; i++)
                        {
                            action(snapshot[i]);
                        }
                    }
                    finally
                    {
                        // Don't keep agents reachable from a static.
                        snapshot.Clear();
                        _snapshotDepth = depth;
                    }
                    return false;
                }
                catch (Exception)
                {
                    {
                        return true;
                    }
                }
            }
        }
    }
}
