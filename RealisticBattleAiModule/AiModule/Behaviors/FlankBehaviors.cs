using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.ArrangementOrder;
using static TaleWorlds.MountAndBlade.HumanAIComponent;
namespace RBMAI
{
    [HarmonyPatch(typeof(BehaviorProtectFlank))]
    internal class OverrideBehaviorProtectFlank
    {
        private enum BehaviorState
        {
            HoldingFlank,
            Charging,
            Returning
        }

        // Cavalry flank guard sorties. The guard used to charge the closest enemy formation of any kind (or anything
        // at all once arrows landed) with a plain ChargeToTarget and only came back when the formation's AVERAGE
        // position was 80 m from its post. A charge order scatters riders onto individual targets and the average of
        // a scattered formation stays near the middle, so before the main lines even met the riders were chasing
        // cavalry and infantry over the whole field, then charged again the moment they got back. Now a sortie is
        // bounded:
        //  - Trigger: enemy melee cavalry within CavThreatDistance of the hold point, or any enemy formation within
        //    CloseThreatDistance. Horse archers and ranged fire no longer pull them out at range (under fire the hold
        //    point already tucks in beside the main line, see CalculateCurrentOrder).
        //  - Recall: the target is gone or has left ReleaseDistance, the formation centre is past SortieLeash, more
        //    than ScatterRecallRatio of the riders are past it, or MaxSortieSeconds ran out.
        //  - Returning ends once ReturnGatherRatio of the riders are back at the post (or ReturnTimeoutSeconds), then
        //    ReengageCooldownSeconds of holding before the next sortie, unless an enemy is right on top of them.
        // Guarding the post alone left the foot it is there to cover open: enemy cavalry and horse archers working the
        // infantry from the front or the far end of the line never came near the post. So a rested guard also sorties
        // against enemy melee cavalry within HarassCavDistance, or horse archers within HarassHorseArcherDistance, of
        // the edge of any friendly foot formation (on its own side of the main line when the other flank is guarded).
        // Such a sortie is leashed to the formation it protects as well as to the post, and is recalled once the
        // harasser is HarassReleaseDistance clear of it.
        private const float CavThreatDistance = 70f;
        private const float CloseThreatDistance = 35f;
        private const float ReleaseDistance = 100f;
        private const float SortieLeash = 60f;
        private const float ScatterRecallRatio = 0.35f;
        private const float MaxSortieSeconds = 25f;
        private const float ReturnGatherRadius = 25f;
        private const float ReturnGatherRatio = 0.75f;
        private const float ReturnTimeoutSeconds = 15f;
        private const float ReengageCooldownSeconds = 5f;
        private const float HarassCavDistance = 70f;
        private const float HarassHorseArcherDistance = 90f;
        private const float HarassReleaseDistance = 110f;
        private const float ProtectLeash = 50f;

        internal class FlankSortieState
        {
            public Formation Target;
            // The friendly foot formation a harassment sortie covers; null for a sortie against a threat to the post.
            public Formation Protected;
            public float SortieStartTime;
            public float ReturnStartTime;
            public float HoldStartTime = float.MinValue;
        }

        internal static readonly Dictionary<Formation, FlankSortieState> sortieStates = new Dictionary<Formation, FlankSortieState>();

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorProtectFlank __instance, ref FormationAI.BehaviorSide ___FlankSide, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref MovementOrder ____chargeToTargetOrder, ref MovementOrder ____movementOrder, ref BehaviorState ____protectFlankState, ref Formation ____mainFormation, ref FormationAI.BehaviorSide ____behaviorSide)
        {
            WorldPosition position = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;

            float distanceFromMainFormation = 90f;
            float closerDistanceFromMainFormation = 30f;
            float distanceOffsetFromMainFormation = 55f;

            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                distanceFromMainFormation = 30f;
                closerDistanceFromMainFormation = 10f;
                distanceOffsetFromMainFormation = 30f;
            }

            if (____mainFormation == null || __instance.Formation == null || __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
            {
                FormationQuerySystem closestEnemy = __instance.Formation?.QuerySystem?.ClosestSignificantlyLargeEnemyFormation;
                ____currentOrder = (closestEnemy?.Formation != null)
                    ? MovementOrder.MovementOrderChargeToTarget(closestEnemy.Formation)
                    : MovementOrder.MovementOrderCharge;
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            }
            else
            {
                Vec2 direction = ____mainFormation.Direction;
                Vec2 v = (__instance.Formation.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - RBMAI.Utilities.GetFormationCenter(____mainFormation)).Normalized();
                Vec2 vec;
                if (____behaviorSide == FormationAI.BehaviorSide.Right || ___FlankSide == FormationAI.BehaviorSide.Right)
                {
                    vec = ____mainFormation.CurrentPosition + v.RightVec().Normalized() * (____mainFormation.Width / 2f + __instance.Formation.Width / 2f + distanceFromMainFormation);
                    vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                    vec += ____mainFormation.Direction * (____mainFormation.Depth / 2f + __instance.Formation.Depth / 2f + distanceOffsetFromMainFormation);
                    position.SetVec2(vec);
                    if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(vec) || __instance.Formation.QuerySystem.UnderRangedAttackRatio > 0.1f)
                    {
                        vec = ____mainFormation.CurrentPosition + v.RightVec().Normalized() * (____mainFormation.Width / 2f + __instance.Formation.Width / 2f + closerDistanceFromMainFormation);
                        vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                        vec += ____mainFormation.Direction;
                        position.SetVec2(vec);
                        if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(vec))
                        {
                            vec = ____mainFormation.CurrentPosition + v.RightVec().Normalized();
                            vec -= ____mainFormation.Direction * 5f;
                            position.SetVec2(vec);
                        }
                    }
                }
                else if (____behaviorSide == FormationAI.BehaviorSide.Left || ___FlankSide == FormationAI.BehaviorSide.Left)
                {
                    vec = ____mainFormation.CurrentPosition + v.LeftVec().Normalized() * (____mainFormation.Width / 2f + __instance.Formation.Width / 2f + distanceFromMainFormation);
                    vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                    vec += ____mainFormation.Direction * (____mainFormation.Depth / 2f + __instance.Formation.Depth / 2f + distanceOffsetFromMainFormation);
                    position.SetVec2(vec);
                    if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(vec) || __instance.Formation.QuerySystem.UnderRangedAttackRatio > 0.1f)
                    {
                        vec = ____mainFormation.CurrentPosition + v.LeftVec().Normalized() * (____mainFormation.Width / 2f + __instance.Formation.Width / 2f + closerDistanceFromMainFormation);
                        vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                        vec += ____mainFormation.Direction;
                        position.SetVec2(vec);
                        if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(vec))
                        {
                            vec = ____mainFormation.CurrentPosition + v.LeftVec().Normalized();
                            vec -= ____mainFormation.Direction * 10f;
                            position.SetVec2(vec);
                        }
                    }
                }
                else
                {
                    vec = ____mainFormation.CurrentPosition + v * ((____mainFormation.Depth + __instance.Formation.Depth) * 0.5f + 10f);
                    position.SetVec2(vec);
                }
                // Recomputed while charging too, so the sortie leash follows the main line as it advances; only a
                // holding or returning guard takes it as its order.
                ____movementOrder = MovementOrder.MovementOrderMove(position);
                if (____protectFlankState != BehaviorState.Charging)
                {
                    ____currentOrder = ____movementOrder;
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);
                }
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CheckAndChangeState")]
        private static bool PrefixCheckAndChangeState(ref BehaviorProtectFlank __instance, ref FormationAI.BehaviorSide ___FlankSide, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref MovementOrder ____chargeToTargetOrder, ref MovementOrder ____movementOrder, ref BehaviorState ____protectFlankState, ref Formation ____mainFormation, ref FormationAI.BehaviorSide ____behaviorSide)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                if (__instance.Formation != null && ____movementOrder != null)
                {
                    Vec2 position = ____movementOrder.GetPosition(__instance.Formation);
                    switch (____protectFlankState)
                    {
                        case BehaviorState.HoldingFlank:
                            {
                                FormationQuerySystem closestFormation = __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                                if (closestFormation != null && closestFormation.Formation != null && (closestFormation.Formation.QuerySystem.IsInfantryFormation || closestFormation.Formation.QuerySystem.IsRangedFormation || closestFormation.Formation.QuerySystem.IsCavalryFormation))
                                {
                                    //float changeToChargeDistance = 30f + (__instance.Formation.Depth + closestFormation.Formation.Depth) / 2f;
                                    //if (closestFormation.Formation.QuerySystem.Formation.CachedMedianPosition.AsVec2.DistanceSquared(position) < changeToChargeDistance * changeToChargeDistance)
                                    //{
                                    //    ____chargeToTargetOrder = MovementOrder.MovementOrderChargeToTarget(closestFormation.Formation);
                                    //    ____currentOrder = ____chargeToTargetOrder;
                                    //    ____protectFlankState = BehaviorState.Charging;
                                    //}
                                }
                                break;
                            }
                        case BehaviorState.Charging:
                            {
                                FormationQuerySystem closestFormation = __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                                if (closestFormation == null || closestFormation.Formation == null)
                                {
                                    ____currentOrder = ____movementOrder;
                                    ____protectFlankState = BehaviorState.Returning;
                                    break;
                                }
                                float returnDistance = 40f + (__instance.Formation.Depth + closestFormation.Formation.Depth) / 2f;
                                if (__instance.Formation.QuerySystem.Formation.CachedAveragePosition.DistanceSquared(position) > returnDistance * returnDistance)
                                {
                                    ____currentOrder = ____movementOrder;
                                    ____protectFlankState = BehaviorState.Returning;
                                }
                                break;
                            }
                        case BehaviorState.Returning:
                            if (__instance.Formation.QuerySystem.Formation.CachedAveragePosition.DistanceSquared(position) < 400f)
                            {
                                ____protectFlankState = BehaviorState.HoldingFlank;
                            }
                            break;
                    }
                    return false;
                }
            }
            else
            {
                if (__instance.Formation != null && ____movementOrder != null)
                {
                    Formation formation = __instance.Formation;
                    Vec2 position = ____movementOrder.GetPosition(formation);
                    float now = Mission.Current.CurrentTime;
                    FlankSortieState sortie = GetSortieState(formation, now);
                    switch (____protectFlankState)
                    {
                        case BehaviorState.HoldingFlank:
                            {
                                bool rested = now - sortie.HoldStartTime >= ReengageCooldownSeconds;
                                Formation target = null;
                                Formation protectedFormation = null;
                                FormationQuerySystem closestFormation = formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                                if (closestFormation != null && closestFormation.Formation != null)
                                {
                                    float depths = (formation.Depth + closestFormation.Formation.Depth) / 2f;
                                    float distanceSq = RBMAI.Utilities.GetFormationCenter(closestFormation.Formation).DistanceSquared(position);
                                    float closeDistance = CloseThreatDistance + depths;
                                    float cavDistance = CavThreatDistance + depths;
                                    bool closeThreat = distanceSq < closeDistance * closeDistance;
                                    bool cavThreat = closestFormation.IsCavalryFormation && distanceSq < cavDistance * cavDistance;
                                    if (closeThreat || (cavThreat && rested))
                                    {
                                        target = closestFormation.Formation;
                                    }
                                }
                                if (target == null && rested && ____mainFormation != null)
                                {
                                    int sideSign = (____behaviorSide == FormationAI.BehaviorSide.Right || ___FlankSide == FormationAI.BehaviorSide.Right) ? 1
                                        : (____behaviorSide == FormationAI.BehaviorSide.Left || ___FlankSide == FormationAI.BehaviorSide.Left) ? -1 : 0;
                                    target = FindHarasser(formation, ____mainFormation, sideSign, out protectedFormation);
                                }
                                if (target != null)
                                {
                                    sortie.Target = target;
                                    sortie.Protected = protectedFormation;
                                    sortie.SortieStartTime = now;
                                    ____chargeToTargetOrder = MovementOrder.MovementOrderChargeToTarget(target);
                                    ____currentOrder = ____chargeToTargetOrder;
                                    ____protectFlankState = BehaviorState.Charging;
                                }
                                break;
                            }
                        case BehaviorState.Charging:
                            {
                                if (ShouldRecallSortie(formation, sortie, position, now))
                                {
                                    sortie.Target = null;
                                    sortie.Protected = null;
                                    sortie.ReturnStartTime = now;
                                    ____currentOrder = ____movementOrder;
                                    ____protectFlankState = BehaviorState.Returning;
                                }
                                break;
                            }
                        case BehaviorState.Returning:
                            if (now - sortie.ReturnStartTime >= ReturnTimeoutSeconds || RiderRatioWithin(formation, position, ReturnGatherRadius + formation.Width / 2f) >= ReturnGatherRatio)
                            {
                                sortie.HoldStartTime = now;
                                ____protectFlankState = BehaviorState.HoldingFlank;
                            }
                            break;
                    }
                    return false;
                }
            }
            return true;
        }

        // A guard found already Charging/Returning with no record (behavior re-activated, state left over from an
        // earlier activation) gets a fresh record: no target, so a stale charge is recalled on the spot.
        private static FlankSortieState GetSortieState(Formation formation, float now)
        {
            if (!sortieStates.TryGetValue(formation, out FlankSortieState sortie))
            {
                sortie = new FlankSortieState { SortieStartTime = now, ReturnStartTime = now };
                sortieStates[formation] = sortie;
            }
            return sortie;
        }

        private static bool ShouldRecallSortie(Formation formation, FlankSortieState sortie, Vec2 holdPoint, float now)
        {
            Formation target = sortie.Target;
            if (target == null || target.CountOfUnits == 0 || now - sortie.SortieStartTime >= MaxSortieSeconds)
            {
                return true;
            }
            float depths = (formation.Depth + target.Depth) / 2f;
            Vec2 targetCenter = RBMAI.Utilities.GetFormationCenter(target);
            Formation protectedFormation = sortie.Protected;
            if (protectedFormation != null)
            {
                if (protectedFormation.CountOfUnits == 0 || DistanceToFormationBox(protectedFormation, targetCenter) > HarassReleaseDistance + target.Depth / 2f)
                {
                    return true;
                }
            }
            else
            {
                float release = ReleaseDistance + depths;
                if (targetCenter.DistanceSquared(holdPoint) > release * release)
                {
                    return true;
                }
            }
            float leash = SortieLeash + depths;
            float protectLeash = ProtectLeash + formation.Depth / 2f;
            Vec2 center = RBMAI.Utilities.GetFormationCenter(formation);
            if (center.DistanceSquared(holdPoint) > leash * leash && (protectedFormation == null || DistanceToFormationBox(protectedFormation, center) > protectLeash))
            {
                return true;
            }
            return 1f - RiderRatioWithin(formation, holdPoint, leash, protectedFormation, protectLeash) > ScatterRecallRatio;
        }

        // Counted over every rider rather than read off the average position, which a scattered formation keeps
        // near the middle. A rider within protectRadius of the protected formation's edge counts as near too.
        private static float RiderRatioWithin(Formation formation, Vec2 point, float radius, Formation protectedFormation = null, float protectRadius = 0f)
        {
            float radiusSq = radius * radius;
            int total = 0;
            int near = 0;
            formation.ApplyActionOnEachUnit(agent =>
            {
                if (agent.IsDetachedFromFormation || agent.IsRunningAway)
                {
                    return;
                }
                total++;
                Vec2 agentPosition = agent.Position.AsVec2;
                if (agentPosition.DistanceSquared(point) <= radiusSq || (protectedFormation != null && DistanceToFormationBox(protectedFormation, agentPosition) <= protectRadius))
                {
                    near++;
                }
            });
            return total > 0 ? (float)near / total : 1f;
        }

        // The enemy cavalry or horse archer formation closest to the guard that is working a friendly foot formation.
        // With sideSign set (+1 right, -1 left of the main line facing the enemy), harassers clearly on the far side
        // of the main line are left to the other flank's guard, if there is one.
        private static Formation FindHarasser(Formation guard, Formation mainFormation, int sideSign, out Formation protectedFormation)
        {
            protectedFormation = null;
            Formation best = null;
            float bestDistanceSq = float.MaxValue;
            Vec2 guardCenter = RBMAI.Utilities.GetFormationCenter(guard);
            Vec2 mainCenter = RBMAI.Utilities.GetFormationCenter(mainFormation);
            Vec2 sideVec = (guard.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - mainCenter).Normalized().RightVec() * sideSign;
            bool otherFlankGuarded = sideSign != 0 && IsFlankGuarded(guard, sideSign > 0 ? FormationAI.BehaviorSide.Left : FormationAI.BehaviorSide.Right);
            foreach (Team team in Mission.Current.Teams)
            {
                if (!team.IsEnemyOf(guard.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits == 0)
                    {
                        continue;
                    }
                    bool horseArchers = enemy.QuerySystem.IsRangedCavalryFormation;
                    if (!horseArchers && !enemy.QuerySystem.IsCavalryFormation)
                    {
                        continue;
                    }
                    Vec2 enemyCenter = RBMAI.Utilities.GetFormationCenter(enemy);
                    if (otherFlankGuarded && (enemyCenter - mainCenter).DotProduct(sideVec) < -mainFormation.Width * 0.25f)
                    {
                        continue;
                    }
                    float threatDistance = (horseArchers ? HarassHorseArcherDistance : HarassCavDistance) + enemy.Depth / 2f;
                    Formation victim = ClosestFootFormationWithin(guard, enemyCenter, threatDistance);
                    if (victim == null)
                    {
                        continue;
                    }
                    float distanceSq = enemyCenter.DistanceSquared(guardCenter);
                    if (distanceSq < bestDistanceSq)
                    {
                        bestDistanceSq = distanceSq;
                        best = enemy;
                        protectedFormation = victim;
                    }
                }
            }
            return best;
        }

        private static Formation ClosestFootFormationWithin(Formation guard, Vec2 point, float maxDistance)
        {
            Formation closest = null;
            float closestDistance = maxDistance;
            foreach (Formation friendly in guard.Team.FormationsIncludingSpecialAndEmpty)
            {
                if (friendly == null || friendly == guard || friendly.CountOfUnits == 0 || friendly.QuerySystem.IsCavalryFormation || friendly.QuerySystem.IsRangedCavalryFormation)
                {
                    continue;
                }
                float distance = DistanceToFormationBox(friendly, point);
                if (distance <= closestDistance)
                {
                    closestDistance = distance;
                    closest = friendly;
                }
            }
            return closest;
        }

        private static bool IsFlankGuarded(Formation guard, FormationAI.BehaviorSide side)
        {
            foreach (Formation friendly in guard.Team.FormationsIncludingSpecialAndEmpty)
            {
                if (friendly != null && friendly != guard && friendly.CountOfUnits > 0 && friendly.AI.ActiveBehavior is BehaviorProtectFlank otherGuard
                    && (otherGuard.FlankSide == side || friendly.AI.Side == side))
                {
                    return true;
                }
            }
            return false;
        }

        // Distance from a point to the formation's footprint (0 inside it), so a long line is threatened along its
        // whole length rather than only near its centre.
        private static float DistanceToFormationBox(Formation formation, Vec2 point)
        {
            Vec2 local = point - RBMAI.Utilities.GetFormationCenter(formation);
            Vec2 direction = formation.Direction;
            float across = Math.Max(0f, Math.Abs(local.DotProduct(direction.RightVec())) - formation.Width * 0.5f);
            float along = Math.Max(0f, Math.Abs(local.DotProduct(direction)) - formation.Depth * 0.5f);
            return (float)Math.Sqrt(across * across + along * along);
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref BehaviorProtectFlank __instance)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        private static bool PrefixGetAiWeight(ref BehaviorProtectFlank __instance, ref float __result, ref Formation ____mainFormation)
        {
            if (____mainFormation == null || !____mainFormation.AI.IsMainFormation)
            {
                ____mainFormation = __instance.Formation.Team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).FirstOrDefault((Formation f) => f.AI.IsMainFormation);
            }
            if (__instance.Formation.AI.IsMainFormation)
            {
                __result = 0f;
                return false;
            }
            if (____mainFormation == null)
            {
                __result = 0.5f;
                return false;
            }
            __result = 1.2f;
            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorVanguard))]
    internal class OverrideBehaviorVanguard
    {
        private static readonly MethodInfo CalculateCurrentOrderMethod = typeof(BehaviorVanguard).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static bool PrefixTickOccasionally(ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder, BehaviorVanguard __instance)
        {
            CalculateCurrentOrderMethod.Invoke(__instance, new object[] { });

            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
            if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && RBMAI.Utilities.GetFormationCenter(__instance.Formation).DistanceSquared(RBMAI.Utilities.GetFormationCenter(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation)) > 1600f && __instance.Formation.QuerySystem.UnderRangedAttackRatio > 0.2f - ((__instance.Formation.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Loose) ? 0.1f : 0f))
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderSkein);
            }
            else
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderSkein);
            }

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder, BehaviorVanguard __instance)
        {
            __instance.Formation.SetFormOrder(FormOrder.FormOrderDeep);
            __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderSkein);
        }
    }

    [HarmonyPatch(typeof(BehaviorPullBack))]
    internal class OverrideBehaviorPullBack
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        private static bool PrefixGetAiWeight(ref float __result)
        {
            __result = 0f;
            return false;
        }
    }
}
