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
    [HarmonyPatch(typeof(BehaviorSkirmishLine))]
    internal class OverrideBehaviorSkirmishLine
    {
        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(Formation ____mainFormation, ref FacingOrder ___CurrentFacingOrder)
        {
            if (____mainFormation != null)
            {
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(____mainFormation.Direction);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref BehaviorSkirmishLine __instance)
        {
            __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
            __instance.Formation.SetFormOrder(FormOrder.FormOrderCustom(110f));
        }
    }

    [HarmonyPatch(typeof(BehaviorScreenedSkirmish))]
    internal class OverrideBehaviorScreenedSkirmish
    {
        private static readonly MethodInfo CalculateCurrentOrderMethod = typeof(BehaviorScreenedSkirmish).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(ref Formation ____mainFormation, ref BehaviorScreenedSkirmish __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (____mainFormation != null && (____mainFormation.CountOfUnits == 0 || !____mainFormation.QuerySystem.IsInfantryFormation))
            {
                ____mainFormation = __instance.Formation.Team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).FirstOrDefault((Formation f) => f.AI.IsMainFormation);
            }
            if (____mainFormation != null && __instance.Formation != null && ____mainFormation.CountOfUnits > 0 && ____mainFormation.QuerySystem.IsInfantryFormation)
            {
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(____mainFormation.Direction);
                WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(____mainFormation);
                Vec2 calcPosition;
                if (__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                {
                    calcPosition = medianPosition.AsVec2 - ____mainFormation.Direction.Normalized() * (____mainFormation.Depth / 2f + __instance.Formation.Depth / 2f + 15f);
                }
                else
                {
                    calcPosition = medianPosition.AsVec2 - ____mainFormation.Direction.Normalized() * (____mainFormation.Depth / 2f + __instance.Formation.Depth / 2f + 5f);
                }
                medianPosition.SetVec2(calcPosition);
                if (!Mission.Current.IsPositionInsideBoundaries(calcPosition) || medianPosition.GetNavMesh() == UIntPtr.Zero)
                {
                    medianPosition = ____mainFormation.QuerySystem.Formation.CachedMedianPosition;
                }
                ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static bool PrefixTickOccasionally(Formation ____mainFormation, BehaviorScreenedSkirmish __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            CalculateCurrentOrderMethod.Invoke(__instance, new object[] { });
            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref BehaviorScreenedSkirmish __instance)
        {
            __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
        }
    }

    [HarmonyPatch(typeof(BehaviorCautiousAdvance))]
    internal class OverrideBehaviorCautiousAdvance
    {
        private enum BehaviorState
        {
            Approaching,
            Shooting,
            PullingBack
        }

        public static Dictionary<Formation, int> waitCountShootingStorage = new Dictionary<Formation, int> { };
        public static Dictionary<Formation, int> waitCountApproachingStorage = new Dictionary<Formation, int> { };

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(ref Vec2 ____shootPosition, ref Formation ____archerFormation, BehaviorCautiousAdvance __instance, ref BehaviorState ____behaviorState, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && ____archerFormation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, false);

                if (significantEnemy != null)
                {
                    int waitCountShooting = 0;
                    int waitCountApproaching = 0;
                    if (!waitCountShootingStorage.TryGetValue(__instance.Formation, out waitCountShooting))
                    {
                        waitCountShootingStorage[__instance.Formation] = 0;
                    }
                    if (!waitCountApproachingStorage.TryGetValue(__instance.Formation, out waitCountApproaching))
                    {
                        waitCountApproachingStorage[__instance.Formation] = 0;
                    }

                    Vec2 vec = RBMAI.Utilities.GetFormationCenter(significantEnemy) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                    float distance = vec.Normalize();

                    switch (____behaviorState)
                    {
                        case BehaviorState.Shooting:
                            {
                                if (waitCountShootingStorage[__instance.Formation] > 70)
                                {
                                    if (distance > 100f)
                                    {
                                        WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                                        medianPosition.SetVec2(medianPosition.AsVec2 + vec * 5f);
                                        ____shootPosition = medianPosition.AsVec2 + vec * 5f;
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                    waitCountShootingStorage[__instance.Formation] = 0;
                                    waitCountApproachingStorage[__instance.Formation] = 0;
                                }
                                else
                                {
                                    if (distance > 100f)
                                    {
                                        waitCountShootingStorage[__instance.Formation] = waitCountShootingStorage[__instance.Formation] + 2;
                                    }
                                    else
                                    {
                                        waitCountShootingStorage[__instance.Formation] = waitCountShootingStorage[__instance.Formation] + 1;
                                    }
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                }
                                break;
                            }
                        case BehaviorState.Approaching:
                            {
                                if (distance > 160f)
                                {
                                    WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                                    medianPosition.SetVec2(medianPosition.AsVec2 + vec * 10f);
                                    ____shootPosition = medianPosition.AsVec2 + vec * 10f;
                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                }
                                else
                                {
                                    if (waitCountApproachingStorage[__instance.Formation] > 35)
                                    {
                                        if (distance < 150f)
                                        {
                                            WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                                            medianPosition.SetVec2(medianPosition.AsVec2 + vec * 5f);
                                            ____shootPosition = medianPosition.AsVec2 + vec * 5f;
                                            ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                        }

                                        waitCountApproachingStorage[__instance.Formation] = 0;
                                    }
                                    else
                                    {
                                        if (distance < 150f)
                                        {
                                            WorldPosition medianPosition = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;
                                            medianPosition.SetVec2(____shootPosition);
                                            ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                        }
                                        waitCountApproachingStorage[__instance.Formation] = waitCountApproachingStorage[__instance.Formation] + 1;
                                    }
                                }
                                break;
                            }
                        case BehaviorState.PullingBack:
                            {
                                if (waitCountApproachingStorage[__instance.Formation] > 30)
                                {
                                    if (distance < 150f)
                                    {
                                        WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                                        medianPosition.SetVec2(medianPosition.AsVec2 - vec * 10f);
                                        ____shootPosition = medianPosition.AsVec2 + vec * 5f;
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                    waitCountApproachingStorage[__instance.Formation] = 0;
                                }
                                else
                                {
                                    if (distance < 150f)
                                    {
                                        WorldPosition medianPosition = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;
                                        medianPosition.SetVec2(____shootPosition);
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                    waitCountApproachingStorage[__instance.Formation] = waitCountApproachingStorage[__instance.Formation] + 1;
                                }
                                break;
                            }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorMountedSkirmish))]
    internal class OverrideBehaviorMountedSkirmish
    {
        public enum RotationDirection
        {
            Left,
            Right
        }

        public class RotationChangeClass
        {
            public int waitbeforeChangeCooldownMax = 100;
            public int waitbeforeChangeCooldownCurrent = 0;
            public RotationDirection rotationDirection = RotationDirection.Left;

            public RotationChangeClass()
            { }
        }

        public static Dictionary<Formation, RotationChangeClass> rotationDirectionDictionary = new Dictionary<Formation, RotationChangeClass> { };

        private struct Ellipse
        {
            private readonly Vec2 _center;

            private readonly float _radius;

            private readonly float _halfLength;

            private readonly Vec2 _direction;

            public Ellipse(Vec2 center, float radius, float halfLength, Vec2 direction)
            {
                _center = center;
                _radius = radius;
                _halfLength = halfLength;
                _direction = direction;
            }

            public Vec2 GetTargetPos(Vec2 position, float distance, RotationDirection rotationDirection)
            {
                Vec2 vec;
                if (rotationDirection == RotationDirection.Left)
                {
                    vec = _direction.LeftVec();
                }
                else
                {
                    vec = _direction.RightVec();
                }
                Vec2 vec2 = _center + vec * _halfLength;
                Vec2 vec3 = _center - vec * _halfLength;
                Vec2 vec4 = position - _center;
                bool flag = vec4.Normalized().DotProduct(_direction) > 0f;
                Vec2 vec5 = vec4.DotProduct(vec) * vec;
                bool flag2 = vec5.Length < _halfLength;
                bool flag3 = true;
                if (flag2)
                {
                    position = _center + vec5 + _direction * (_radius * (float)(flag ? 1 : (-1)));
                }
                else
                {
                    flag3 = vec5.DotProduct(vec) > 0f;
                    Vec2 vec6 = (position - (flag3 ? vec2 : vec3)).Normalized();
                    position = (flag3 ? vec2 : vec3) + vec6 * _radius;
                }
                Vec2 vec7 = _center + vec5;
                float num = MathF.PI * 2f * _radius;
                while (distance > 0f)
                {
                    if (flag2 && flag)
                    {
                        float num2 = (((vec2 - vec7).Length < distance) ? (vec2 - vec7).Length : distance);
                        position = vec7 + (vec2 - vec7).Normalized() * num2;
                        position += _direction * _radius;
                        distance -= num2;
                        flag2 = false;
                        flag3 = true;
                    }
                    else if (!flag2 && flag3)
                    {
                        Vec2 v = (position - vec2).Normalized();
                        float num3 = TaleWorlds.Library.MathF.Acos(MBMath.ClampFloat(_direction.DotProduct(v), -1f, 1f));
                        float num4 = MathF.PI * 2f * (distance / num);
                        float num5 = ((num3 + num4 < MathF.PI) ? (num3 + num4) : MathF.PI);
                        float num6 = (num5 - num3) / MathF.PI * (num / 2f);
                        Vec2 direction = _direction;
                        direction.RotateCCW(num5);
                        position = vec2 + direction * _radius;
                        distance -= num6;
                        flag2 = true;
                        flag = false;
                    }
                    else if (flag2)
                    {
                        float num7 = (((vec3 - vec7).Length < distance) ? (vec3 - vec7).Length : distance);
                        position = vec7 + (vec3 - vec7).Normalized() * num7;
                        position -= _direction * _radius;
                        distance -= num7;
                        flag2 = false;
                        flag3 = false;
                    }
                    else
                    {
                        Vec2 vec8 = (position - vec3).Normalized();
                        float num8 = MathF.Acos(MBMath.ClampFloat(_direction.DotProduct(vec8), -1f, 1f));
                        float num9 = MathF.PI * 2f * (distance / num);
                        float num10 = ((num8 - num9 > 0f) ? (num8 - num9) : 0f);
                        float num11 = num8 - num10;
                        float num12 = num11 / MathF.PI * (num / 2f);
                        Vec2 vec9 = vec8;
                        vec9.RotateCCW(num11);
                        position = vec3 + vec9 * _radius;
                        distance -= num12;
                        flag2 = true;
                        flag = true;
                    }
                }
                return position;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(BehaviorMountedSkirmish __instance, ref bool ____engaging, ref MovementOrder ____currentOrder, ref bool ____isEnemyReachable, ref FacingOrder ___CurrentFacingOrder)
        {
            WorldPosition position = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;
            WorldPosition position2 = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;
            Formation targetFormation = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
            FormationQuerySystem targetFormationQS = null;
            if (targetFormation != null)
            {
                targetFormationQS = targetFormation.QuerySystem;
            }
            else
            {
                targetFormationQS = __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
            }
            ____isEnemyReachable = targetFormationQS != null && (!(__instance.Formation.Team.TeamAI is TeamAISiegeComponent) || !TeamAISiegeComponent.IsFormationInsideCastle(targetFormationQS.Formation, includeOnlyPositionedUnits: false));
            if (!____isEnemyReachable)
            {
                position.SetVec2(RBMAI.Utilities.GetFormationCenter(__instance.Formation));
            }
            else
            {
                bool isEnemyClose = (__instance.Formation.QuerySystem.AverageAllyPosition - __instance.Formation.Team.QuerySystem.AverageEnemyPosition).LengthSquared <= 160000f;
                ____engaging = (isEnemyClose || ((!____engaging) ? ((__instance.Formation.QuerySystem.Formation.CachedAveragePosition - __instance.Formation.QuerySystem.AverageAllyPosition).LengthSquared <= 160000f) : (!(__instance.Formation.QuerySystem.UnderRangedAttackRatio * 0.2f > __instance.Formation.QuerySystem.MakingRangedAttackRatio))));
                if (!____engaging)
                {
                    position = new WorldPosition(Mission.Current.Scene, new Vec3(__instance.Formation.QuerySystem.AverageAllyPosition.x, __instance.Formation.QuerySystem.AverageAllyPosition.y, __instance.Formation.Team.GetMedianPosition(__instance.Formation.Team.GetAveragePosition()).GetNavMeshZ() + 100f));
                }
                else
                {
                    Formation enemyFormation = targetFormationQS.Formation;

                    if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
                    {
                        enemyFormation = RBMAI.Utilities.FindSignificantEnemyToPosition(__instance.Formation, position, true, true, false, false, false, false);
                    }

                    //if (closestSignificantlyLargeEnemyFormation != null && closestSignificantlyLargeEnemyFormation.AveragePosition.Distance(__instance.Formation.CurrentPosition) < __instance.Formation.Depth / 2f + (
                    //    (closestSignificantlyLargeEnemyFormation.Formation.QuerySystem.FormationPower / __instance.Formation.QuerySystem.FormationPower) * 20f + 10f))
                    //{
                    //    ____currentOrder = MovementOrder.MovementOrderChargeToTarget(closestSignificantlyLargeEnemyFormation.Formation);
                    //    return;
                    //}

                    if (enemyFormation != null && enemyFormation.QuerySystem != null)
                    {
                        bool isEnemyCav = enemyFormation.QuerySystem.IsCavalryFormation || enemyFormation.QuerySystem.IsRangedCavalryFormation;
                        float distance = 60f;
                        if (!__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            distance = 30f;
                        }

                        RotationChangeClass rotationDirection;
                        if (!rotationDirectionDictionary.TryGetValue(__instance.Formation, out rotationDirection))
                        {
                            rotationDirection = new RotationChangeClass();
                            rotationDirectionDictionary.Add(__instance.Formation, rotationDirection);
                        }

                        if (__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            Ellipse ellipse = new Ellipse(RBMAI.Utilities.GetFormationCenter(enemyFormation), distance, (enemyFormation.ArrangementOrder == ArrangementOrder.ArrangementOrderLoose) ? enemyFormation.Width * 0.25f : enemyFormation.Width * 0.5f, enemyFormation.Direction);
                            position.SetVec2(ellipse.GetTargetPos(__instance.Formation.SmoothedAverageUnitPosition, 25f, rotationDirection.rotationDirection));
                        }
                        else
                        {
                            Ellipse ellipse = new Ellipse(RBMAI.Utilities.GetFormationCenter(enemyFormation), distance, enemyFormation.Width * 0.5f, enemyFormation.Direction);
                            position.SetVec2(ellipse.GetTargetPos(__instance.Formation.SmoothedAverageUnitPosition, 25f, rotationDirection.rotationDirection));
                        }
                        if (rotationDirection.waitbeforeChangeCooldownCurrent > 0)
                        {
                            if (rotationDirection.waitbeforeChangeCooldownCurrent > rotationDirection.waitbeforeChangeCooldownMax)
                            {
                                rotationDirection.waitbeforeChangeCooldownCurrent = 0;
                                rotationDirectionDictionary[__instance.Formation] = rotationDirection;
                            }
                            else
                            {
                                rotationDirection.waitbeforeChangeCooldownCurrent++;
                                rotationDirectionDictionary[__instance.Formation] = rotationDirection;
                            }
                            position.SetVec2(enemyFormation.CurrentPosition + enemyFormation.Direction.Normalized() * (__instance.Formation.Depth / 2f + enemyFormation.Depth / 2f + 50f));
                            if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(position.AsVec2))
                            {
                                position.SetVec2(enemyFormation.CurrentPosition + enemyFormation.Direction.Normalized() * -(__instance.Formation.Depth / 2f + enemyFormation.Depth / 2f + 50f));
                            }
                        }
                        float distanceFromBoundary = Mission.Current.GetClosestBoundaryPosition(__instance.Formation.CurrentPosition).Distance(__instance.Formation.CurrentPosition);
                        if (distanceFromBoundary <= __instance.Formation.Width / 2f)
                        {
                            if (rotationDirection.waitbeforeChangeCooldownCurrent > rotationDirection.waitbeforeChangeCooldownMax)
                            {
                                rotationDirection.waitbeforeChangeCooldownCurrent = 0;
                                rotationDirectionDictionary[__instance.Formation] = rotationDirection;
                            }
                            else
                            {
                                rotationDirection.waitbeforeChangeCooldownCurrent++;
                                rotationDirectionDictionary[__instance.Formation] = rotationDirection;
                            }
                        }
                    }
                    else
                    {
                        position.SetVec2(RBMAI.Utilities.GetFormationCenter(__instance.Formation));
                    }
                }
            }
            if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(position.AsVec2))
            {
                position = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;
                ____currentOrder = MovementOrder.MovementOrderMove(position);
            }
            else
            {
                ____currentOrder = MovementOrder.MovementOrderMove(position);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetAiWeight")]
        private static void PostfixGetAiWeight(ref BehaviorMountedSkirmish __instance, ref float __result, ref bool ____isEnemyReachable)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsCavalryFormation)
            {
                if (RBMAI.Utilities.CheckIfMountedSkirmishFormation(__instance.Formation, 0.6f))
                {
                    __result = 5f;
                    return;
                }
                else
                {
                    __result = 0f;
                    return;
                }
            }
            else if (__instance.Formation != null && __instance.Formation.QuerySystem.IsRangedCavalryFormation)
            {
                //Formation enemyCav = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);
                //if (enemyCav != null && enemyCav.QuerySystem.IsCavalryFormation && __instance.Formation.QuerySystem.Formation.CachedMedianPosition.AsVec2.Distance(enemyCav.QuerySystem.Formation.CachedMedianPosition.AsVec2) < 55f && enemyCav.CountOfUnits >= __instance.Formation.CountOfUnits * 0.5f)
                //{
                //    __result = 1000f;
                //    return;
                //}
                if (!____isEnemyReachable)
                {
                    __result = 0.01f;
                    return;
                }

                float powerSum = 0f;
                if (!Utilities.HasBattleBeenJoined(__instance.Formation, false, 75f))
                {
                    foreach (Formation enemyArcherFormation in Utilities.FindSignificantArcherFormations(__instance.Formation))
                    {
                        powerSum += enemyArcherFormation.QuerySystem.FormationPower;
                    }
                    if (powerSum > 0f && __instance.Formation.QuerySystem.FormationPower > 0f && (__instance.Formation.QuerySystem.FormationPower / powerSum) < 0.75f)
                    {
                        __result = 1000f;
                        return;
                    }
                }
                __result = 1000f;
                return;
            }
            else
            {
                int countOfSkirmishers = 0;
                __instance.Formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    if (RBMAI.Utilities.CheckIfSkirmisherAgent(agent, 1))
                    {
                        countOfSkirmishers++;
                    }
                });
                if (countOfSkirmishers / __instance.Formation.CountOfUnits > 0.6f)
                {
                    __result = 1f;
                    return;
                }
                else
                {
                    __result = 0f;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorHorseArcherSkirmish))]
    internal class OverrideBehaviorHorseArcherSkirmish
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
