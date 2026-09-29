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

            // Agents whose automatic target selection we turned off in the banner-bearer block below. We cannot key the
            // restore off IsBannerBearer, because that reads the live wielded slots and the engine empties them mid-mission
            // (siege ladders and standing points sheath both hands, item pickup overwrites the main hand, weapons get
            // knocked away). Without this set an agent that stops holding its banner never gets automatic selection back
            // and stays permanently passive with its target parked on a squadmate.
            public static HashSet<Agent> bannerBearersWithHeldTarget = new HashSet<Agent>();

            // Agents routed by a cavalry charge (ChargeDamageCallbackPatch). Only these are rallied below: an ordered
            // retreat or RBM's keep fallback must not be un-retreated, and a vanilla morale rout stays a rout.
            public static HashSet<Agent> chargeRoutedAgents = new HashSet<Agent>();

            // Next mission time each agent may scan for a dropped melee weapon (see TrySeekMeleeWeapon).
            public static Dictionary<Agent, float> meleePickupNextScan = new Dictionary<Agent, float>();

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

            // True if the agent still has something to fight with: any melee weapon, a throwing weapon with
            // ammo left, or a launcher together with ammo.
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

            // Vanilla AI only ever picks up shields, banners, stuck missiles and quivers (HumanAIComponent.SelectPickableItem),
            // and only while its target is over 20m away, so an agent whose weapon the posture system knocked out of its
            // hands punched for the rest of the fight. RBM 3.8.8 had an OnTickAsAI prefix for this; it was commented out
            // in 3.8.9 when the method was renamed. This restores it for agents that spawned with a melee weapon and now
            // have nothing to fight with, ignoring the target-distance gate: the dropped weapon is usually at their feet.
            // Runs after vanilla's own pickup tick and on its own timer, so vanilla's shield/ammo pickup is untouched.
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

            // CommonAIComponent.StopRetreating only clears the retreat flags. Retreat() had dropped the shield
            // stance (EnforceShieldUsage(None)) and the flee left a stale target, so a rallied agent came back
            // standing idle or swinging at whoever it last targeted. Rebuild what the formation would have given it.
            private static void RestoreAiAfterRally(Agent agent)
            {
                // Banner bearers holding a parked target keep automatic selection off; the banner block owns that.
                if (!bannerBearersWithHeldTarget.Contains(agent))
                {
                    agent.SetAutomaticTargetSelection(true);
                }
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
                // Banner bearers (Raise Your Banner) lock onto a distant enemy as their melee target and the native
                // combat AI swings at it regardless of range - "attacking air". It is not gated by AIAttackOnDecideChance
                // nor by the wielded weapon (an empty-handed bearer just punches). InvalidateTargetAgent alone does not
                // hold: the engine's automatic target selection re-acquires the same distant enemy on the next tick.
                // So while no enemy is in melee range we turn automatic selection off and park the target on a squadmate
                // - a friendly target gives the combat AI nothing to swing at. Once an enemy closes within melee range
                // we hand automatic selection back so the bearer can still fight.
                if (Mission.Current != null)
                {
                    bool bannerHoldTarget = false;
                    if (___Agent.IsActive() && RBMAI.Utilities.IsBannerBearer(___Agent))
                    {
                        MBList<Agent> bannerNearbyEnemies = new MBList<Agent>();
                        bannerNearbyEnemies = Mission.Current.GetNearbyEnemyAgents(___Agent.GetWorldPosition().AsVec2, 5f, ___Agent.Team, bannerNearbyEnemies);
                        // Fleeing routers run through our lines and end up within 5m; a banner bearer shouldn't chase-swing
                        // at them (it can't catch them = "attacking air"), so treat only non-routing enemies as a reason to fight.
                        bannerNearbyEnemies.RemoveAll((Agent a) => a.IsRunningAway);
                        if (bannerNearbyEnemies.Count == 0 && ___Agent.Formation != null)
                        {
                            // Park on a grid neighbour rather than the nearest ally so bearers don't pull toward each other.
                            // Detached / removed units carry file-rank index -1 and the arrangement's neighbour lookup would
                            // index _units2D[i, -1], so only query the grid for a positioned unit. Skipping neighbours that
                            // are banner bearers themselves stops two bearers from targeting (and following) each other.
                            // Mounted bearers are never parked: a mounted agent's combat AI lines up attack runs on its
                            // target, and a target a couple of metres away has it riding out and back in endlessly.
                            Agent bannerFriendlyTarget = null;
                            IFormationUnit bannerUnit = ___Agent;
                            if (!___Agent.HasMount && ___Agent.Detachment == null && bannerUnit.FormationFileIndex >= 0 && bannerUnit.FormationRankIndex >= 0)
                            {
                                IFormationArrangement arrangement = ___Agent.Formation.Arrangement;
                                Agent left = arrangement.GetNeighborUnitOfLeftSide(bannerUnit) as Agent;
                                Agent right = arrangement.GetNeighborUnitOfRightSide(bannerUnit) as Agent;
                                if (left != null && !RBMAI.Utilities.IsBannerBearer(left))
                                {
                                    bannerFriendlyTarget = left;
                                }
                                else if (right != null && !RBMAI.Utilities.IsBannerBearer(right))
                                {
                                    bannerFriendlyTarget = right;
                                }
                            }
                            if (bannerFriendlyTarget != null)
                            {
                                ___Agent.SetAutomaticTargetSelection(false);
                                ___Agent.SetTargetAgent(bannerFriendlyTarget);
                                bannerBearersWithHeldTarget.Add(___Agent);
                                bannerHoldTarget = true;
                            }
                            else
                            {
                                // Mounted, or no squadmate to park on - fall back to the one-shot clear.
                                ___Agent.InvalidateTargetAgent();
                            }
                        }
                    }
                    // Hand automatic selection back the moment we stop holding this agent's target, whatever the reason:
                    // an enemy closed to melee range, the formation went away, or the agent is no longer wielding a banner.
                    if (!bannerHoldTarget && bannerBearersWithHeldTarget.Remove(___Agent))
                    {
                        ___Agent.SetAutomaticTargetSelection(true);
                        ___Agent.InvalidateTargetAgent();
                    }
                }
                //___Agent.MovementInputVector = new Vec2(30f, 30f);
                float currentTime = MBCommon.GetTotalMissionTime();
                WeaponPreference.TickWeaponPreference(___Agent, currentTime);
                RangedReachGate.TickRangedReach(___Agent, currentTime);
                // Ranged AI judges range without its ammo limit. Set here, on the main thread, rather than in the
                // SetAiRelatedProperties postfix (AgentStats.cs): that one can run on a parallel worker and this is a
                // native write. Re-checked each tick so a newly picked-up bow or a script resetting the flags is covered.
                // Without it archers open fire only at ~110-130 m, far inside their reach. Crossbowmen ignore the
                // limit either way; RangedReachGate holds both back from targets beyond reach.
                if (___Agent.IsRangedCached && ___Agent.IsActive())
                {
                    Agent.AISpecialCombatModeFlags combatFlags = ___Agent.GetScriptedCombatFlags();
                    if ((combatFlags & Agent.AISpecialCombatModeFlags.IgnoreAmmoLimitForRangeCalculation) == 0)
                    {
                        ___Agent.SetScriptedCombatFlags(combatFlags | Agent.AISpecialCombatModeFlags.IgnoreAmmoLimitForRangeCalculation);
                    }
                }
                // A rider hemmed in by 3+ foot soldiers (either side, at least one an enemy) pushes forward. Horses,
                // ridden or not, are agents too and are not counted. A crowd of only friends is left alone, so reserve
                // cavalry packed against its own infantry holds instead of surging through the line. Not for a
                // formation ordered to dismount: the engine only lets a rider get off once he has nearly stopped
                // (Agent.DismountVelocityLimit), so the shove kept the last riders, crowded by the comrades already
                // on foot, moving and they never dismounted.
                if (___Agent.IsActive() && ___Agent.HasMount
                    && ___Agent.Formation?.RidingOrder.OrderEnum != RidingOrder.RidingOrderEnum.Dismount)
                {
                    MBList<Agent> footSoldiersClose = new MBList<Agent>();
                    footSoldiersClose = Mission.Current.GetNearbyAgents(___Agent.GetWorldPosition().AsVec2, 1.25f, footSoldiersClose);
                    footSoldiersClose.RemoveAll((Agent a) => !a.IsHuman || a.HasMount);
                    Agent rider = ___Agent;
                    if (footSoldiersClose.Count >= 3 && footSoldiersClose.Any((Agent a) => a.IsEnemyOf(rider)))
                    {
                        ___Agent.EventControlFlags &= ~Agent.EventControlFlag.DoubleTapToDirectionMask;
                        ___Agent.EventControlFlags |= Agent.EventControlFlag.DoubleTapToDirectionUp;

                        ___Agent.MovementInputVector = ___Agent.LookDirection.AsVec2 * 2f;
                    }
                }
                // Rally only agents routed by a cavalry charge. An agent obeying a Retreat order (or RBM's
                // keep-battle fallback) has IsRetreating set with full morale and takes no melee hits, so an
                // ungated rally un-retreated it every tick. A charge-routed agent whose formation has since been
                // ordered to retreat is left alone too: the Retreat order is applied once and never re-applied,
                // so rallying it would leave it standing with no movement target.
                CommonAIComponent rallyAi = ___Agent.CommonAIComponent;
                if (rallyAi != null && chargeRoutedAgents.Contains(___Agent) && ___Agent.GetMorale() > 0f && currentTime - ___Agent.LastMeleeHitTime > 10f
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
                //if (___Agent.HasMount)
                //{
                //}
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
            private static bool Prefix(ref Action<Agent> action, ref Agent ignoreAgent, ref Formation __instance)
            {
                try
                {
                    __instance.ApplyActionOnEachUnitViaBackupList(action);
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
