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

            private static void Postfix(ref SpawnedItemEntity ____itemToPickUp, ref Agent ___Agent)
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
                            // ___Agent is a ref parameter and cannot be captured by the lambda below.
                            Agent bannerBearer = ___Agent;
                            MBList<Agent> bannerNearbyAllies = new MBList<Agent>();
                            bannerNearbyAllies = Mission.Current.GetNearbyAllyAgents(bannerBearer.GetWorldPosition().AsVec2, 10f, bannerBearer.Team, bannerNearbyAllies);
                            Agent bannerFriendlyTarget = bannerNearbyAllies.FirstOrDefault((Agent a) => a != bannerBearer && a.IsHuman && a.IsActive() && a.Formation == bannerBearer.Formation);
                            if (bannerFriendlyTarget != null)
                            {
                                ___Agent.SetAutomaticTargetSelection(false);
                                ___Agent.SetTargetAgent(bannerFriendlyTarget);
                                bannerBearersWithHeldTarget.Add(___Agent);
                                bannerHoldTarget = true;
                            }
                            else
                            {
                                // No squadmate to park on - fall back to the one-shot clear.
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
                if (___Agent.IsActive() && ___Agent.HasMount)
                {
                    MBList<Agent> enemiesClose = new MBList<Agent>();
                    enemiesClose = Mission.Current.GetNearbyAgents(___Agent.GetWorldPosition().AsVec2, 1.25f, enemiesClose);
                    enemiesClose.RemoveAll((Agent a) => a.HasMount);
                    if (enemiesClose.Count() >= 3)
                    {
                        ___Agent.EventControlFlags &= ~Agent.EventControlFlag.DoubleTapToDirectionMask;
                        ___Agent.EventControlFlags |= Agent.EventControlFlag.DoubleTapToDirectionUp;

                        ___Agent.MovementInputVector = ___Agent.LookDirection.AsVec2 * 2f;
                    }
                }
                if (___Agent.GetMorale() > 0f && currentTime - ___Agent.LastRecievedMeleeHitTime > 10f)
                {
                    ___Agent.CommonAIComponent?.StopRetreating();
                }
                //if (___Agent.HasMount)
                //{
                //}
                if (____itemToPickUp != null && (___Agent.AIStateFlags & Agent.AIStateFlag.UseObjectMoving) != 0)
                {
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
                catch (Exception e)
                {
                    {
                        return true;
                    }
                }
            }
        }
    }
}
