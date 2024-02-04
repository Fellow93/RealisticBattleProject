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
    internal class Behaviours
    {
        [HarmonyPatch(typeof(BehaviorSkirmishLine))]
        private class OverrideBehaviorSkirmishLine
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
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
                //__instance.Formation.FormOrder = FormOrder.FormOrderWide;
                __instance.Formation.FormOrder = FormOrder.FormOrderCustom(110f);
            }
        }

        [HarmonyPatch(typeof(BehaviorDefend))]
        private class OverrideBehaviorDefend
        {
            public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

            [HarmonyPostfix]
            [HarmonyPatch("CalculateCurrentOrder")]
            private static void PostfixCalculateCurrentOrder(ref BehaviorDefend __instance, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged, ref FacingOrder ___CurrentFacingOrder)
            {
                if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    WorldPosition medianPositionNew = __instance.Formation.QuerySystem.MedianPosition;
                    medianPositionNew.SetVec2(__instance.Formation.QuerySystem.AveragePosition);

                    Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

                    if (significantEnemy != null)
                    {
                        Vec2 enemyDirection = significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                        float distance = enemyDirection.Normalize();
                        if (distance < (200f))
                        {
                            WorldPosition newPosition = WorldPosition.Invalid;
                            positionsStorage.TryGetValue(__instance.Formation, out newPosition);
                            ____currentOrder = MovementOrder.MovementOrderMove(newPosition);
                            ___IsCurrentOrderChanged = true;
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                        }
                        else
                        {
                            if (__instance.DefensePosition.IsValid)
                            {
                                WorldPosition newPosition = __instance.DefensePosition;
                                newPosition.SetVec2(newPosition.AsVec2 + __instance.Formation.Direction * 10f);
                                ____currentOrder = MovementOrder.MovementOrderMove(newPosition);
                                positionsStorage[__instance.Formation] = newPosition;

                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                            }
                            else
                            {
                                WorldPosition newPosition = medianPositionNew;
                                newPosition.SetVec2(newPosition.AsVec2 + __instance.Formation.Direction * 10f);
                                positionsStorage[__instance.Formation] = newPosition;
                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(BehaviorHoldHighGround))]
        private class OverrideBehaviorHoldHighGround
        {
            public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

            [HarmonyPostfix]
            [HarmonyPatch("CalculateCurrentOrder")]
            private static void PostfixCalculateCurrentOrder(ref BehaviorHoldHighGround __instance, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged, ref FacingOrder ___CurrentFacingOrder)
            {
                if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    WorldPosition medianPositionNew = __instance.Formation.QuerySystem.MedianPosition;
                    medianPositionNew.SetVec2(__instance.Formation.QuerySystem.AveragePosition);

                    Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

                    if (significantEnemy != null)
                    {
                        Vec2 enemyDirection = significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                        float distance = enemyDirection.Normalize();

                        if (distance < (200f))
                        {
                            WorldPosition newPosition = WorldPosition.Invalid;
                            positionsStorage.TryGetValue(__instance.Formation, out newPosition);
                            ____currentOrder = MovementOrder.MovementOrderMove(newPosition);
                            ___IsCurrentOrderChanged = true;
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                        }
                        else
                        {
                            WorldPosition newPosition = medianPositionNew;
                            newPosition.SetVec2(newPosition.AsVec2 + __instance.Formation.Direction * 10f);
                            positionsStorage[__instance.Formation] = newPosition;
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(BehaviorScreenedSkirmish))]
        private class OverrideBehaviorScreenedSkirmish
        {
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
                    WorldPosition medianPosition = ____mainFormation.QuerySystem.MedianPosition;
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
                        medianPosition = ____mainFormation.QuerySystem.MedianPosition;
                    }
                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("TickOccasionally")]
            private static bool PrefixTickOccasionally(Formation ____mainFormation, BehaviorScreenedSkirmish __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
            {
                MethodInfo method = typeof(BehaviorScreenedSkirmish).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
                method.DeclaringType.GetMethod("CalculateCurrentOrder");
                method.Invoke(__instance, new object[] { });
                //bool flag = formation.QuerySystem.ClosestEnemyFormation == null || _mainFormation.QuerySystem.MedianPosition.AsVec2.DistanceSquared(formation.QuerySystem.ClosestEnemyFormation.MedianPosition.AsVec2) <= formation.QuerySystem.AveragePosition.DistanceSquared(formation.QuerySystem.ClosestEnemyFormation.MedianPosition.AsVec2) || formation.QuerySystem.AveragePosition.DistanceSquared(position.AsVec2) <= (_mainFormation.Depth + formation.Depth) * (_mainFormation.Depth + formation.Depth) * 0.25f;
                //if (flag != _isFireAtWill)
                //{
                //    _isFireAtWill = flag;
                //    formation.FiringOrder = (_isFireAtWill ? FiringOrder.FiringOrderFireAtWill : FiringOrder.FiringOrderHoldYourFire);
                //}
                __instance.Formation.SetMovementOrder(____currentOrder);
                __instance.Formation.FacingOrder = ___CurrentFacingOrder;
                return false;
            }

            [HarmonyPostfix]
            [HarmonyPatch("OnBehaviorActivatedAux")]
            private static void PostfixOnBehaviorActivatedAux(ref BehaviorScreenedSkirmish __instance)
            {
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
            }

            //[HarmonyPostfix]
            //[HarmonyPatch("GetAiWeight")]
            //static void PostfixGetAiWeight( ref float __result)
            //{
            //    __result.ToString();
            //}
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

                    Vec2 vec = significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                    float distance = vec.Normalize();

                    switch (____behaviorState)
                    {
                        case BehaviorState.Shooting:
                            {
                                if (waitCountShootingStorage[__instance.Formation] > 70)
                                {
                                    if (distance > 100f)
                                    {
                                        WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
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
                                    WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
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
                                            WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
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
                                            WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
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
                                        WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
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
                                        WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
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
            WorldPosition position = __instance.Formation.QuerySystem.MedianPosition;
            WorldPosition position2 = __instance.Formation.QuerySystem.MedianPosition;
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
                position.SetVec2(__instance.Formation.QuerySystem.AveragePosition);
            }
            else
            {
                bool num = (__instance.Formation.QuerySystem.AverageAllyPosition - __instance.Formation.Team.QuerySystem.AverageEnemyPosition).LengthSquared <= 160000f;
                bool engaging = ____engaging;
                engaging = (____engaging = (num || ((!____engaging) ? ((__instance.Formation.QuerySystem.AveragePosition - __instance.Formation.QuerySystem.AverageAllyPosition).LengthSquared <= 160000f) : (!(__instance.Formation.QuerySystem.UnderRangedAttackRatio * 0.2f > __instance.Formation.QuerySystem.MakingRangedAttackRatio)))));
                if (!____engaging)
                {
                    position = new WorldPosition(Mission.Current.Scene, new Vec3(__instance.Formation.QuerySystem.AverageAllyPosition, __instance.Formation.Team.QuerySystem.MedianPosition.GetNavMeshZ() + 100f));
                }
                else
                {
                    Vec2 vec = (__instance.Formation.QuerySystem.MedianPosition.AsVec2 - targetFormationQS.MedianPosition.AsVec2).Normalized().LeftVec();
                    FormationQuerySystem closestSignificantlyLargeEnemyFormation = __instance.Formation.QuerySystem.ClosestEnemyFormation;
                    float num2 = 50f + (targetFormationQS.Formation.Width + __instance.Formation.Depth) * 0.5f;
                    float num3 = 0f;

                    Formation enemyFormation = targetFormationQS.Formation;

                    if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
                    {
                        enemyFormation = RBMAI.Utilities.FindSignificantEnemyToPosition(__instance.Formation, position, true, true, false, false, false, false);
                    }

                    if (closestSignificantlyLargeEnemyFormation != null && closestSignificantlyLargeEnemyFormation.AveragePosition.Distance(__instance.Formation.CurrentPosition) < __instance.Formation.Depth / 2f + (
                        (closestSignificantlyLargeEnemyFormation.Formation.QuerySystem.FormationPower / __instance.Formation.QuerySystem.FormationPower) * 20f + 10f))
                    {
                        ____currentOrder = MovementOrder.MovementOrderChargeToTarget(closestSignificantlyLargeEnemyFormation.Formation);
                        return;
                    }

                    //foreach (Team team in Mission.Current.Teams.ToList())
                    //{
                    //    if (!team.IsEnemyOf(__instance.Formation.Team))
                    //    {
                    //        continue;
                    //    }
                    //    foreach (Formation formation2 in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0)IncludingSpecialAndEmpty.ToList())
                    //    {
                    //        if (formation2.CountOfUnits > 0 && formation2.QuerySystem != closestSignificantlyLargeEnemyFormation)
                    //        {
                    //            Vec2 v = formation2.QuerySystem.AveragePosition - closestSignificantlyLargeEnemyFormation.AveragePosition;
                    //            float num4 = v.Normalize();
                    //            if (vec.DotProduct(v) > 0.8f && num4 < num2 && num4 > num3)
                    //            {
                    //                num3 = num4;
                    //                enemyFormation = formation2;
                    //            }
                    //        }
                    //    }
                    //}

                    //if (__instance.Formation.QuerySystem.RangedCavalryUnitRatio > 0.95f && targetFormationQS.Formation == enemyFormation)
                    //{
                    //    ____currentOrder = MovementOrder.MovementOrderCharge;
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
                            Ellipse ellipse = new Ellipse(enemyFormation.QuerySystem.MedianPosition.AsVec2, distance, (enemyFormation.ArrangementOrder == ArrangementOrder.ArrangementOrderLoose) ? enemyFormation.Width * 0.25f : enemyFormation.Width * 0.5f, enemyFormation.Direction);
                            position.SetVec2(ellipse.GetTargetPos(__instance.Formation.SmoothedAverageUnitPosition, 25f, rotationDirection.rotationDirection));
                        }
                        else
                        {
                            Ellipse ellipse = new Ellipse(enemyFormation.QuerySystem.MedianPosition.AsVec2, distance, enemyFormation.Width * 0.5f, enemyFormation.Direction);
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
                        float distanceFromBoudnary = Mission.Current.GetClosestBoundaryPosition(__instance.Formation.CurrentPosition).Distance(__instance.Formation.CurrentPosition);
                        if (distanceFromBoudnary <= __instance.Formation.Width / 2f)
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
                        position.SetVec2(__instance.Formation.QuerySystem.AveragePosition);
                    }
                }
            }
            if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(position.AsVec2))
            {
                position = __instance.Formation.QuerySystem.MedianPosition;
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
                Formation enemyCav = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);
                if (enemyCav != null && enemyCav.QuerySystem.IsCavalryFormation && __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyCav.QuerySystem.MedianPosition.AsVec2) < 55f && enemyCav.CountOfUnits >= __instance.Formation.CountOfUnits * 0.5f)
                {
                    __result = 0.01f;
                    return;
                }
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
                        __result = 0.01f;
                        return;
                    }
                }
                __result = 100f;
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

    [HarmonyPatch(typeof(BehaviorProtectFlank))]
    internal class OverrideBehaviorProtectFlank
    {
        private enum BehaviorState
        {
            HoldingFlank,
            Charging,
            Returning
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorProtectFlank __instance, ref FormationAI.BehaviorSide ___FlankSide, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref MovementOrder ____chargeToTargetOrder, ref MovementOrder ____movementOrder, ref BehaviorState ____protectFlankState, ref Formation ____mainFormation, ref FormationAI.BehaviorSide ____behaviorSide)
        {
            WorldPosition position = __instance.Formation.QuerySystem.MedianPosition;
            Vec2 averagePosition = __instance.Formation.QuerySystem.AveragePosition;

            float distanceFromMainFormation = 90f;
            float closerDistanceFromMainFormation = 30f;
            float distanceOffsetFromMainFormation = 55f;

            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                distanceFromMainFormation = 30f;
                closerDistanceFromMainFormation = 10f;
                distanceOffsetFromMainFormation = 30f;
            }

            if (____mainFormation == null || __instance.Formation == null || __instance.Formation.QuerySystem.ClosestEnemyFormation == null)
            {
                ____currentOrder = MovementOrder.MovementOrderStop;
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            }
            else if (____protectFlankState == BehaviorState.HoldingFlank || ____protectFlankState == BehaviorState.Returning)
            {
                Vec2 direction = ____mainFormation.Direction;
                Vec2 v = (__instance.Formation.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - ____mainFormation.QuerySystem.MedianPosition.AsVec2).Normalized();
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
                //WorldPosition medianPosition = ____mainFormation.QuerySystem.MedianPosition;
                //medianPosition.SetVec2(vec);
                ____movementOrder = MovementOrder.MovementOrderMove(position);
                ____currentOrder = ____movementOrder;
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);
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
                                FormationQuerySystem closestFormation = __instance.Formation.QuerySystem.ClosestEnemyFormation;
                                if (closestFormation != null && closestFormation.Formation != null && (closestFormation.Formation.QuerySystem.IsInfantryFormation || closestFormation.Formation.QuerySystem.IsRangedFormation || closestFormation.Formation.QuerySystem.IsCavalryFormation))
                                {
                                    //float changeToChargeDistance = 30f + (__instance.Formation.Depth + closestFormation.Formation.Depth) / 2f;
                                    //if (closestFormation.Formation.QuerySystem.MedianPosition.AsVec2.DistanceSquared(position) < changeToChargeDistance * changeToChargeDistance)
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
                                FormationQuerySystem closestFormation = __instance.Formation.QuerySystem.ClosestEnemyFormation;
                                if (closestFormation != null && closestFormation.Formation != null)
                                {
                                    if (closestFormation == null)
                                    {
                                        ____currentOrder = ____movementOrder;
                                        ____protectFlankState = BehaviorState.Returning;
                                        break;
                                    }
                                    float returnDistance = 40f + (__instance.Formation.Depth + closestFormation.Formation.Depth) / 2f;
                                    if (__instance.Formation.QuerySystem.AveragePosition.DistanceSquared(position) > returnDistance * returnDistance)
                                    {
                                        ____currentOrder = ____movementOrder;
                                        ____protectFlankState = BehaviorState.Returning;
                                    }
                                }
                                break;
                            }
                        case BehaviorState.Returning:
                            if (__instance.Formation.QuerySystem.AveragePosition.DistanceSquared(position) < 400f)
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
                    Vec2 position = ____movementOrder.GetPosition(__instance.Formation);
                    switch (____protectFlankState)
                    {
                        case BehaviorState.HoldingFlank:
                            {
                                FormationQuerySystem closestFormation = __instance.Formation.QuerySystem.ClosestEnemyFormation;
                                if (closestFormation != null && closestFormation.Formation != null && (closestFormation.Formation.QuerySystem.IsCavalryFormation || closestFormation.Formation.QuerySystem.IsRangedCavalryFormation))
                                {
                                    float changeToChargeDistance = 110f + (__instance.Formation.Depth + closestFormation.Formation.Depth) / 2f;
                                    if (closestFormation.Formation.QuerySystem.MedianPosition.AsVec2.Distance(position) < changeToChargeDistance || __instance.Formation.QuerySystem.UnderRangedAttackRatio > 0.1f)
                                    {
                                        ____chargeToTargetOrder = MovementOrder.MovementOrderChargeToTarget(closestFormation.Formation);
                                        ____currentOrder = ____chargeToTargetOrder;
                                        ____protectFlankState = BehaviorState.Charging;
                                    }
                                }
                                break;
                            }
                        case BehaviorState.Charging:
                            {
                                FormationQuerySystem closestFormation = __instance.Formation.QuerySystem.ClosestEnemyFormation;
                                if (closestFormation != null && closestFormation.Formation != null)
                                {
                                    if (closestFormation == null)
                                    {
                                        ____currentOrder = ____movementOrder;
                                        ____protectFlankState = BehaviorState.Returning;
                                        break;
                                    }
                                    float returnDistance = 80f + (__instance.Formation.Depth + closestFormation.Formation.Depth) / 2f;
                                    if (__instance.Formation.QuerySystem.AveragePosition.DistanceSquared(position) > returnDistance * returnDistance)
                                    {
                                        ____currentOrder = ____movementOrder;
                                        ____protectFlankState = BehaviorState.Returning;
                                    }
                                }
                                break;
                            }
                        case BehaviorState.Returning:
                            if (__instance.Formation.QuerySystem.AveragePosition.DistanceSquared(position) < 400f)
                            {
                                ____protectFlankState = BehaviorState.HoldingFlank;
                            }
                            break;
                    }
                    return false;
                }
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref BehaviorProtectFlank __instance)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        private static bool PostfixGetAiWeight(ref BehaviorProtectFlank __instance, ref float __result, ref Formation ____mainFormation)
        {
            if (____mainFormation == null || !____mainFormation.AI.IsMainFormation)
            {
                ____mainFormation = __instance.Formation.Team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).FirstOrDefault((Formation f) => f.AI.IsMainFormation);
            }
            if (____mainFormation == null || __instance.Formation.AI.IsMainFormation)
            {
                __result = 0f;
                return false;
            }
            __result = 10f;
            return false;
        }
    }

    [MBCallback]
    [HarmonyPatch(typeof(HumanAIComponent))]
    internal class AdjustSpeedLimitPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("AdjustSpeedLimit")]
        private static bool AdjustSpeedLimitPrefix(ref HumanAIComponent __instance, ref Agent agent, ref float desiredSpeed, ref bool limitIsMultiplier, ref Agent ___Agent)
        {
            //FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
            //_currentTacticField.DeclaringType.GetField("_currentTactic");
            //if (agent.Formation != null && agent.Formation.QuerySystem.IsCavalryFormation && _currentTacticField.GetValue(agent.Formation?.Team?.TeamAI) != null && _currentTacticField.GetValue(agent.Formation?.Team?.TeamAI).ToString().Contains("Embolon"))
            //{
            //    if (limitIsMultiplier && desiredSpeed < 0.6f)
            //    {
            //        desiredSpeed = 0.6f;
            //    }
            //    return true;
            //}
            //if(agent != null && agent.Formation != null)
            //{
            //    float currentTime = MBCommon.GetTotalMissionTime();
            //    if (agent.Formation.QuerySystem.IsInfantryFormation)
            //    {
            //        float lastMeleeAttackTime = agent.LastMeleeAttackTime;
            //        float lastMeleeHitTime = agent.LastMeleeHitTime;
            //        if ((currentTime - lastMeleeAttackTime < 4f) || (currentTime - lastMeleeHitTime < 4f))
            //        {
            //            if(desiredSpeed > 0.65f)
            //            {
            //                desiredSpeed = 0.65f;
            //            }
            //            return true;
            //        }
            //    }
            //}
            if (agent.Formation != null && (agent.Formation.QuerySystem.IsRangedCavalryFormation || agent.Formation.QuerySystem.IsCavalryFormation))
            {
                if (agent.MountAgent != null)
                {
                    float speed = agent.MountAgent.AgentDrivenProperties.MountSpeed;
                    ___Agent.SetMaximumSpeedLimit(speed, false);
                    agent.MountAgent.SetMaximumSpeedLimit(speed, false);
                    return false;
                }
            }
            else if (agent.Formation != null && agent.Formation.AI != null && agent.Formation.AI.ActiveBehavior != null &&
                (agent.Formation.AI.ActiveBehavior.GetType() == typeof(RBMBehaviorForwardSkirmish) ||
                agent.Formation.AI.ActiveBehavior.GetType() == typeof(RBMBehaviorInfantryAttackFlank)))
            {
                if (limitIsMultiplier && desiredSpeed < 0.85f)
                {
                    desiredSpeed = 0.85f;
                }
                //___Agent.SetMaximumSpeedLimit(100f, false);
            }
            if (agent.Formation != null && agent.Formation.AI != null && agent.Formation.AI.ActiveBehavior != null &&
                (agent.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorProtectFlank)))
            {
                if (desiredSpeed < 0.85f)
                {
                    limitIsMultiplier = true;
                    desiredSpeed = 0.85f;
                }
                //___Agent.SetMaximumSpeedLimit(100f, false);
            }
            if (agent.Formation != null && agent.Formation.AI != null && agent.Formation.AI.ActiveBehavior != null &&
                (agent.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorRegroup)))
            {
                if (limitIsMultiplier && desiredSpeed < 0.95f)
                {
                    desiredSpeed = 0.95f;
                }
                //___Agent.SetMaximumSpeedLimit(100f, false);
            }
            if (agent.Formation != null && agent.Formation.AI != null && agent.Formation.AI.ActiveBehavior != null &&
                (agent.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorCharge)))
            {
                if (limitIsMultiplier && desiredSpeed < 0.85f)
                {
                    desiredSpeed = 0.85f;
                }
            }
            if (agent.Formation != null && agent.Formation.AI != null && agent.Formation.AI.ActiveBehavior != null &&
                (agent.Formation.AI.ActiveBehavior.GetType() == typeof(RBMBehaviorArcherFlank)))
            {
                if (limitIsMultiplier && desiredSpeed < 0.9f)
                {
                    desiredSpeed = 0.9f;
                }
            }
            if (agent.Formation != null && agent.Formation.AI != null && agent.Formation.AI.ActiveBehavior != null &&
                (agent.Formation.AI.ActiveBehavior.GetType() == typeof(RBMBehaviorArcherSkirmish)))
            {
                if (limitIsMultiplier && desiredSpeed < 0.9f)
                {
                    desiredSpeed = 0.9f;
                }
            }
            //else if(agent.Formation != null && agent.Formation.Team.HasTeamAi)
            //{
            //    FieldInfo field = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
            //    field.DeclaringType.GetField("_currentTactic");
            //    TacticComponent currentTactic = (TacticComponent)field.GetValue(agent.Formation.Team.TeamAI);

            //    if(agent.Formation.GetReadonlyMovementOrderReference().OrderEnum == MovementOrder.MovementOrderEnum.ChargeToTarget)
            //    {
            //        if (currentTactic != null && currentTactic.GetType() != null && (currentTactic.GetType() == typeof(RBMTacticAttackSplitInfantry) || currentTactic.GetType() == typeof(RBMTacticAttackSplitInfantry)))
            //        {
            //            if (limitIsMultiplier && desiredSpeed < 0.8f)
            //            {
            //                desiredSpeed = 0.8f;
            //            }
            //        }
            //    }

            //}
            return true;
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

    [HarmonyPatch(typeof(BehaviorVanguard))]
    internal class OverrideBehaviorVanguard
    {
        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static bool PrefixTickOccasionally(ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder, BehaviorVanguard __instance)
        {
            MethodInfo method = typeof(BehaviorVanguard).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
            method.DeclaringType.GetMethod("CalculateCurrentOrder");
            method.Invoke(__instance, new object[] { });

            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.FacingOrder = ___CurrentFacingOrder;
            if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && __instance.Formation.QuerySystem.AveragePosition.DistanceSquared(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.MedianPosition.AsVec2) > 1600f && __instance.Formation.QuerySystem.UnderRangedAttackRatio > 0.2f - ((__instance.Formation.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Loose) ? 0.1f : 0f))
            {
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderSkein;
            }
            else
            {
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderSkein;
            }

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder, BehaviorVanguard __instance)
        {
            __instance.Formation.FormOrder = FormOrder.FormOrderDeep;
            __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderSkein;
        }
    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    internal class OverrideHumanAIComponent
    {
        [HarmonyPostfix]
        [HarmonyPatch("SetBehaviorValueSet")]
        private static void SetBehaviorValueSet(HumanAIComponent __instance, BehaviorValueSet behaviorValueSet, Agent ___Agent)
        {
            if(Mission.Current.SceneName.Contains("arena"))
            {
                if(___Agent != null && ___Agent.IsRangedCached)
                {
                    __instance.SetBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                    __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 5.5f, 3f, 4f, 10f, 0.01f);
                    __instance.SetBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 3f, 2f, 10f, 20f);
                }
            }
            if(___Agent != null && ___Agent.Formation != null)
            {
                if (behaviorValueSet == BehaviorValueSet.Charge)
                {
                    if (___Agent.Formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.GoToPos, 0.01f, 7f, 4f, 20f, 6f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 0.55f, 2f, 0.55f, 20f, 0.55f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 4f, 2f, 0.55f, 30f, 0.55f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 8f, 15f, 10f, 30f, 10f);
                        return;
                    }
                    if (___Agent.Formation.QuerySystem.IsCavalryFormation)
                    {
                        if (___Agent.HasMount)
                        {
                            if (RBMAI.Utilities.GetHarnessTier(___Agent) > 3)
                            {
                                __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 8f, 7f, 4f, 20f, 1f);
                                __instance.SetBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
                            }
                            else
                            {
                                __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 1f, 2f, 1f, 20f, 1f);
                                __instance.SetBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
                            }
                        }
                        else
                        {
                            __instance.SetBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
                        }
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.GoToPos, 1f, 7f, 4f, 20f, 6f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Ranged, 2f, 7f, 4f, 20f, 5f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 0f, 10f, 3f, 20f, 6f);
                        return;
                    }
                    if (___Agent.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || ___Agent.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge)
                    {
                        if (___Agent.Formation.QuerySystem.IsInfantryFormation)
                        {
                            __instance.SetBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                            __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
                            __instance.SetBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 7f, 0.8f, 20f, 20f);
                        }
                        if (___Agent.Formation.QuerySystem.IsRangedFormation)
                        {
                            __instance.SetBehaviorParams(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                            __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 5.5f, 3f, 4f, 10f, 0.01f);
                            __instance.SetBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 3f, 2f, 10f, 20f);
                        }
                        return;
                    }
                }
                if (behaviorValueSet == BehaviorValueSet.Follow)
                {
                    if (___Agent.Formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 0.55f, 2f, 4f, 20f, 0.55f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Ranged, 0.55f, 7f, 0.55f, 20f, 0.55f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 8f, 2f, 0.55f, 30f, 0.55f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 10f, 15f, 0.065f, 30f, 0.065f);
                        return;
                    }
                }
                if (behaviorValueSet == BehaviorValueSet.DefaultMove)
                {
                    if (___Agent.Formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.GoToPos, 3f, 15f, 5f, 20f, 5f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 0f, 2f, 0f, 20f, 0f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.ChargeHorseback, 0.01f, 2f, 0.01f, 30f, 0.01f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.RangedHorseback, 1f, 15f, 0.065f, 30f, 0.065f);
                        return;
                    }
                    if (Mission.Current.IsSiegeBattle || Mission.Current.IsSallyOutBattle)
                    {
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 8f, 4f, 3f, 20f, 0.01f);
                        return;
                    }
                    if (___Agent.Formation.GetReadonlyMovementOrderReference().OrderEnum == MovementOrder.MovementOrderEnum.FallBack)
                    {
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 0f, 4f, 0f, 20f, 0f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);
                    }
                    else
                    {
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 8f, 5f, 3f, 20f, 0.01f);
                    }
                    return;
                }
                if (Mission.Current.IsSiegeBattle || Mission.Current.IsSallyOutBattle)
                {
                    if (___Agent.Formation.QuerySystem.IsRangedFormation)
                    {
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Melee, 8f, 5f, 5f, 15f, 0.01f);
                        __instance.SetBehaviorParams(AISimpleBehaviorKind.Ranged, 0.02f, 5f, 0.04f, 15f, 0.03f);
                        return;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(MovementOrder))]
    internal class OverrideMovementOrder
    {
        internal enum MovementOrderEnum
        {
            Invalid,
            Attach,
            AttackEntity,
            Charge,
            ChargeToTarget,
            Follow,
            FollowEntity,
            Guard,
            Move,
            Retreat,
            Stop,
            Advance,
            FallBack
        }

        //[HarmonyPrefix]
        //[HarmonyPatch("SetChargeBehaviorValues")]
        //private static bool PrefixSetChargeBehaviorValues(Agent unit)
        //{
        //    if (unit != null && unit.Formation != null)
        //    {
        //        if (unit.Formation.QuerySystem.IsRangedCavalryFormation)
        //        {
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0.01f, 7f, 4f, 20f, 6f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0.55f, 2f, 0.55f, 20f, 0.55f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 2f, 7f, 4f, 20f, 5f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 4f, 2f, 0.55f, 30f, 0.55f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 8f, 15f, 10f, 30f, 10f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //            return false;
        //        }
        //        if (unit.Formation.QuerySystem.IsCavalryFormation)
        //        {
        //            if (unit.HasMount)
        //            {
        //                if (RBMAI.Utilities.GetHarnessTier(unit) > 3)
        //                {
        //                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 7f, 4f, 20f, 1f);
        //                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
        //                }
        //                else
        //                {
        //                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 1f, 2f, 1f, 20f, 1f);
        //                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
        //                }
        //            }
        //            else
        //            {
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 7f, 4f, 20f, 1f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
        //            }
        //            //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 1f, 2f, 4f, 20f, 1f);
        //            //unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 1f, 7f, 4f, 20f, 6f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 2f, 7f, 4f, 20f, 5f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0f, 10f, 3f, 20f, 6f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //            return false;
        //        }
        //        if (unit.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
        //        {
        //            if (unit.Formation.QuerySystem.IsInfantryFormation)
        //            {
        //                //podmienky: twohandedpolearm v rukach
        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0f, 40f, 4f, 50f, 6f);
        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 7f, 1f, 10f, 0.01f);
        //                //if (RBMAI.Utilities.CheckIfTwoHandedPolearmInfantry(unit))
        //                //{
        //                //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 3.5f, 5f, 20f, 6f);
        //                //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 3.5f, 4f, 20f, 0.01f);
        //                //}
        //                //else {
        //                //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
        //                //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
        //                //}

        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 5f, 20f, 6f);
        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 20f, 1f);
        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 2f, 7f, 4f, 20f, 5f);
        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 40f, 4f, 60f, 0f);
        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 5f, 7f, 10f, 8, 20f);
        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 1f, 12f, 1f, 30f, 0f);
        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);

        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0.8f, 20f, 20f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 40f, 4f, 60f, 0f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 5f, 7f, 10f, 8, 20f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 1f, 12f, 1f, 30f, 0f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //                return false;
        //            }
        //            if (unit.Formation.QuerySystem.IsRangedFormation)
        //            {
        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0f, 40f, 4f, 50f, 6f);
        //                //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 7f, 1f, 10f, 0.01f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 4f, 10f, 0.01f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 2f, 0f, 8f, 20f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 40f, 4f, 60f, 0f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 2f, 15f, 6.5f, 30f, 5.5f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 1f, 12f, 1f, 30f, 0f);
        //                unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //                return false;
        //            }
        //        }
        //    }
        //    return true;
        //}

        //[HarmonyPrefix]
        //[HarmonyPatch("SetFollowBehaviorValues")]
        //private static bool PrefixSetFollowBehaviorValues(Agent unit)
        //{
        //    if (unit.Formation != null)
        //    {
        //        if (unit.Formation.QuerySystem.IsRangedCavalryFormation)
        //        {
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 5f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0.55f, 2f, 4f, 20f, 0.55f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0.55f, 7f, 0.55f, 20f, 0.55f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 8f, 2f, 0.55f, 30f, 0.55f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 10f, 15f, 0.065f, 30f, 0.065f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //            return false;
        //        }
        //    }
        //    return true;
        //}

        //[HarmonyPrefix]
        //[HarmonyPatch("SetDefaultMoveBehaviorValues")]
        //private static bool PrefixSetDefaultMoveBehaviorValues(Agent unit)
        //{
        //    if (unit.Formation != null)
        //    {
        //        if (unit.Formation.QuerySystem.IsRangedCavalryFormation)
        //        {
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 15f, 5f, 20f, 5f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0f, 2f, 0f, 20f, 0f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0.02f, 7f, 0.04f, 20f, 0.03f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 0.01f, 2f, 0.01f, 30f, 0.01f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 1f, 15f, 0.065f, 30f, 0.065f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //            return false;
        //        }
        //    }
        //    if (Mission.Current.MissionTeamAIType != Mission.MissionTeamAITypeEnum.FieldBattle)
        //    {
        //        unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 6f);
        //        unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 4f, 3f, 20f, 0.01f);
        //        unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0.02f, 7f, 0.04f, 20f, 0.03f);
        //        unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 10f, 7f, 5f, 30f, 0.05f);
        //        unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0.02f, 15f, 0.065f, 30f, 0.055f);
        //        unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
        //        unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //        return false;
        //    }
        //    if (unit.Formation != null)
        //    {
        //        if (unit.Formation.GetReadonlyMovementOrderReference().OrderEnum == MovementOrder.MovementOrderEnum.FallBack)
        //        {
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 6f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0f, 4f, 0f, 20f, 0f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 10f, 7f, 5f, 30f, 0.05f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0.02f, 15f, 0.065f, 30f, 0.055f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //            return false;
        //        }
        //        else
        //        {
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 6f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 5f, 3f, 20f, 0.01f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0.02f, 7f, 0.04f, 20f, 0.03f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 10f, 7f, 5f, 30f, 0.05f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0.02f, 15f, 0.065f, 30f, 0.055f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 9f);
        //            unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //            return false;
        //        }
        //    }
        //    return true;
        //}

        [HarmonyPrefix]
        [HarmonyPatch("GetSubstituteOrder")]
        private static bool PrefixGetSubstituteOrder(MovementOrder __instance, ref MovementOrder __result, Formation formation)
        {
            if (formation != null && (formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && __instance.OrderType == OrderType.ChargeWithTarget)
            {
                if (formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    __result = MovementOrder.MovementOrderChargeToTarget(formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                }
                else
                {
                    __result = MovementOrder.MovementOrderCharge;
                }
                //var position = formation.QuerySystem.MedianPosition;
                //position.SetVec2(formation.CurrentPosition);
                //__result = MovementOrder.MovementOrderMove(position);
                return false;
            }

            return true;
        }

        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

        [HarmonyPostfix]
        [HarmonyPatch("GetPositionAux")]
        private static void GetPositionAuxPostfix(ref MovementOrder __instance, ref WorldPosition __result, ref Formation f, ref WorldPosition.WorldPositionEnforcedCache worldPositionEnforcedCache)
        {
            if (__instance.OrderEnum == MovementOrder.MovementOrderEnum.FallBack)
            {
                Vec2 directionAux;
                if ((uint)(__instance.OrderEnum - 10) <= 1u)
                {
                    FormationQuerySystem querySystem = f.QuerySystem;
                    FormationQuerySystem closestEnemyFormation = querySystem.ClosestEnemyFormation;
                    if (closestEnemyFormation == null)
                    {
                        directionAux = Vec2.One;
                    }
                    else
                    {
                        directionAux = (closestEnemyFormation.MedianPosition.AsVec2 - querySystem.AveragePosition).Normalized();
                    }
                }
                else
                {
                    directionAux = Vec2.One;
                }

                WorldPosition medianPosition = f.QuerySystem.MedianPosition;
                medianPosition.SetVec2(f.QuerySystem.AveragePosition - directionAux * 0.35f);
                __result = medianPosition;

                return;
            }
            if (__instance.OrderEnum == MovementOrder.MovementOrderEnum.Advance)
            {
                Formation enemyFormation = RBMAI.Utilities.FindSignificantEnemy(f, true, true, false, false, false, true);
                FormationQuerySystem querySystem = f.QuerySystem;
                FormationQuerySystem enemyQuerySystem;
                if (enemyFormation != null)
                {
                    enemyQuerySystem = enemyFormation.QuerySystem;
                }
                else
                {
                    enemyQuerySystem = querySystem.ClosestEnemyFormation;
                }
                if (enemyQuerySystem == null)
                {
                    __result = f.CreateNewOrderWorldPosition(worldPositionEnforcedCache);
                    return;
                }
                WorldPosition oldPosition = enemyQuerySystem.MedianPosition;
                WorldPosition newPosition = enemyQuerySystem.MedianPosition;
                if (querySystem.IsRangedFormation || querySystem.IsRangedCavalryFormation)
                {
                    float effectiveMissileRange = querySystem.MissileRangeAdjusted / 2.25f;
                    if (!(newPosition.AsVec2.DistanceSquared(querySystem.AveragePosition) > effectiveMissileRange * effectiveMissileRange))
                    {
                        Vec2 directionAux2 = (enemyQuerySystem.MedianPosition.AsVec2 - querySystem.MedianPosition.AsVec2).Normalized();

                        newPosition.SetVec2(newPosition.AsVec2 - directionAux2 * effectiveMissileRange);
                    }

                    if (oldPosition.AsVec2.Distance(newPosition.AsVec2) > 7f)
                    {
                        positionsStorage[f] = newPosition;
                        __result = newPosition;
                    }
                    else
                    {
                        WorldPosition tempPos = WorldPosition.Invalid;
                        if (positionsStorage.TryGetValue(f, out tempPos))
                        {
                            __result = tempPos;
                            return;
                        }
                        __result = oldPosition;
                    }
                    return;
                }
                else
                {
                    Vec2 vec = (enemyQuerySystem.AveragePosition - f.QuerySystem.AveragePosition).Normalized();
                    float distance = enemyQuerySystem.AveragePosition.Distance(f.QuerySystem.AveragePosition);
                    float num = 5f;
                    if (enemyQuerySystem.FormationPower < f.QuerySystem.FormationPower * 0.2f)
                    {
                        num = 0.1f;
                    }
                    newPosition.SetVec2(newPosition.AsVec2 - vec * num);

                    if (distance > 7f)
                    {
                        positionsStorage[f] = newPosition;
                        __result = newPosition;
                    }
                    else
                    {
                        __instance = MovementOrder.MovementOrderChargeToTarget(enemyFormation);
                        WorldPosition tempPos = WorldPosition.Invalid;
                        if (positionsStorage.TryGetValue(f, out tempPos))
                        {
                            __result = tempPos;
                            return;
                        }
                        __result = oldPosition;
                    }
                    return;
                }
            }
        }

        //[HarmonyPrefix]
        //[HarmonyPatch("GetPosition")]
        //static bool PrefixGetPosition(Formation f, ref WorldPosition __result)
        //{
        //    if(f == null)
        //    {
        //        __result = WorldPosition.Invalid;
        //        return false;
        //    }
        //    else
        //    {
        //        InformationManager.DisplayMessage(new InformationMessage(f.Team.IsAttacker + " " + f.AI.Side.ToString() + " " + f.PrimaryClass.GetName()));
        //        return true;
        //    }
        //}
    }

    [HarmonyPatch(typeof(Agent))]
    internal class OverrideAgent
    {
        //[HarmonyPrefix]
        //[HarmonyPatch("GetTargetAgent")]
        //static bool PrefixGetTargetAgent(ref Agent __instance, ref Agent __result)
        //{
        //    List<Formation> formations;
        //    if (__instance != null)
        //    {
        //        Formation formation = __instance.Formation;
        //        if (formation != null)
        //        {
        //            if ((formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && (formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget))
        //            {
        //                formations = RBMAI.Utilities.FindSignificantFormations(formation);
        //                if (formations.Count > 0)
        //                {
        //                    __result = RBMAI.Utilities.NearestAgentFromMultipleFormations(__instance.Position.AsVec2, formations);
        //                    return false;
        //                }
        //                //Formation enemyFormation = formation.MovementOrder.TargetFormation;
        //                //if(enemyFormation != null)
        //                //{
        //                //    __result = RBMAI.Utilities.NearestAgentFromFormation(__instance.Position.AsVec2, enemyFormation);
        //                //    return false;
        //                //}
        //            }
        //        }
        //    }
        //    return true;
        //}

        [HarmonyPrefix]
        [HarmonyPatch("SetFiringOrder")]
        private static bool PrefixSetFiringOrder(ref Agent __instance, ref int order)
        {
            if (__instance.Formation != null && __instance.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
            {
                if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

                    if (__instance.Formation.QuerySystem.IsInfantryFormation && !RBMAI.Utilities.FormationFightingInMelee(__instance.Formation, 0.5f))
                    {
                        Formation enemyCav = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);

                        if (enemyCav != null && !enemyCav.QuerySystem.IsCavalryFormation)
                        {
                            enemyCav = null;
                        }

                        float cavDist = 0f;
                        float signDist = 1f;
                        if (enemyCav != null && significantEnemy != null)
                        {
                            Vec2 cavDirection = enemyCav.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                            cavDist = cavDirection.Normalize();

                            Vec2 signDirection = significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                            signDist = signDirection.Normalize();
                        }

                        if ((enemyCav != null) && (cavDist <= signDist) && (enemyCav.CountOfUnits > __instance.Formation.CountOfUnits / 10) && (signDist > 35f))
                        {
                            if (enemyCav.TargetFormation == __instance.Formation && (enemyCav.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || enemyCav.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge))
                            {
                                if (RBMAI.Utilities.CheckIfCanBrace(__instance))
                                {
                                    //__instance.SetLookAgent(__instance.GetTargetAgent());
                                    order = 1;
                                }
                                else
                                {
                                    order = 0;
                                }
                            }
                        }
                    }
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Agent))]
    internal class OverrideUpdateFormationOrders
    {
        [HarmonyPrefix]
        [HarmonyPatch("UpdateFormationOrders")]
        private static bool PrefixUpdateFormationOrders(ref Agent __instance)
        {
            if (__instance.Formation != null && __instance.IsAIControlled && __instance.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
            {
                if (__instance.Formation.ArrangementOrder.OrderEnum == ArrangementOrderEnum.Square ||
                    __instance.Formation.ArrangementOrder.OrderEnum == ArrangementOrderEnum.Circle ||
                    __instance.Formation.ArrangementOrder.OrderEnum == ArrangementOrderEnum.ShieldWall)
                {
                    __instance.EnforceShieldUsage(ArrangementOrder.GetShieldDirectionOfUnit(__instance.Formation, __instance, __instance.Formation.ArrangementOrder.OrderEnum));
                }
                else
                {
                    if (!__instance.WieldedOffhandWeapon.IsEmpty)
                    {
                        bool hasnotusableonehand = __instance.Equipment.HasAnyWeaponWithFlags(WeaponFlags.NotUsableWithOneHand);
                        bool hasranged = __instance.IsRangedCached;
                        //bool hasranged = __instance.Equipment.HasAnyWeaponWithFlags(WeaponFlags.RangedWeapon);
                        float distance = __instance.GetTargetAgent() != null ? __instance.Position.Distance(__instance.GetTargetAgent().Position) : 100f;
                        if (!hasnotusableonehand && !hasranged && __instance.GetTargetAgent() != null && distance < 7f)
                        {
                            __instance.EnforceShieldUsage(Agent.UsageDirection.DefendDown);
                        }
                        else
                        {
                            __instance.EnforceShieldUsage(Agent.UsageDirection.None);
                        }
                    }
                }
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Formation))]
    internal class SetPositioningPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("SetPositioning")]
        private static bool PrefixSetPositioning(ref Formation __instance, ref int? unitSpacing)
        {
            if (__instance.ArrangementOrder == ArrangementOrderScatter)
            {
                if (unitSpacing == null)
                {
                    unitSpacing = 2;
                }
                unitSpacing = 2;
            }
            return true;
        }
    }

    //[HarmonyPatch(typeof(OrderController))]
    //class OverrideOrderController
    //{
    //    [HarmonyPostfix]
    //    [HarmonyPatch("SetOrder")]
    //    static void PostfixSetOrder(OrderController __instance, OrderType orderType, ref Mission ____mission)
    //    {
    //        if (orderType == OrderType.Charge)
    //        {
    //            foreach (Formation selectedFormation in __instance.SelectedFormations)
    //            {
    //                //if ((selectedFormation.QuerySystem.IsInfantryFormation || selectedFormation.QuerySystem.IsRangedFormation) || ____mission.IsTeleportingAgents)
    //                //{
    //                if (selectedFormation.QuerySystem.ClosestEnemyFormation == null)
    //                {
    //                    selectedFormation.SetMovementOrder(MovementOrder.MovementOrderCharge);
    //                }
    //                else
    //                {
    //                    selectedFormation.SetMovementOrder(MovementOrder.MovementOrderChargeToTarget(selectedFormation.QuerySystem.ClosestEnemyFormation.Formation));
    //                }
    //                //}
    //            }
    //        }
    //    }
    //}

    [HarmonyPatch(typeof(Formation))]
    internal class OverrideSetMovementOrder
    {
        [HarmonyPrefix]
        [HarmonyPatch("SetMovementOrder")]
        private static bool PrefixSetOrder(Formation __instance, ref MovementOrder input)
        {
            if (input.OrderType == OrderType.Charge)
            {
                if (__instance.QuerySystem.ClosestEnemyFormation != null)
                {
                    input = MovementOrder.MovementOrderChargeToTarget(__instance.QuerySystem.ClosestEnemyFormation.Formation);
                }
            }
            return true;
        }
    }

    //[HarmonyPatch(typeof(BehaviorComponent))]
    //class OverrideFindBestBehavior
    //{
    //    [HarmonyPrefix]
    //    [HarmonyPatch("GetAIWeight")]
    //    static bool PrefixFindBestBehavior(ref BehaviorComponent __instance, ref float __result)
    //    {
    //        __instance.NavmeshlessTargetPositionPenalty = 1f;
    //        return true;
    //    }
    //}

    //[HarmonyPatch(typeof(FormationAI))]
    //class OverrideFindBestBehavior
    //{
    //    [HarmonyPrefix]
    //    [HarmonyPatch("FindBestBehavior")]
    //    static bool PrefixFindBestBehavior(FormationAI __instance, ref bool __result,
    //        ref List<BehaviorComponent> ____behaviors, ref Formation ____formation, ref BehaviorComponent ____activeBehavior)
    //    {
    //        BehaviorComponent behaviorComponent = null;
    //        float num = float.MinValue;
    //        foreach (BehaviorComponent behavior in ____behaviors)
    //        {
    //            if (!(behavior.WeightFactor > 1E-07f))
    //            {
    //                continue;
    //            }
    //            float num2 = behavior.GetAIWeight() * behavior.WeightFactor;
    //            if (behavior == __instance.ActiveBehavior)
    //            {
    //                num2 *= MBMath.Lerp(1.2f, 2f, MBMath.ClampFloat((behavior.PreserveExpireTime - Mission.Current.CurrentTime) / 5f, 0f, 1f), float.MinValue);
    //            }
    //            if (num2 > num)
    //            {
    //                if (behavior.NavmeshlessTargetPositionPenalty > 0f)
    //                {
    //                    num2 /= behavior.NavmeshlessTargetPositionPenalty;
    //                }
    //                behavior.PrecalculateMovementOrder();
    //                num2 *= behavior.NavmeshlessTargetPositionPenalty;
    //                if (num2 > num)
    //                {
    //                    behaviorComponent = behavior;
    //                    num = num2;
    //                }
    //            }
    //        }
    //        if (behaviorComponent != null)
    //        {
    //            typeof(FormationAI).GetProperty("ActiveBehavior").SetValue(__instance, behaviorComponent, null);
    //            if (behaviorComponent != ____behaviors[0])
    //            {
    //                ____behaviors.Remove(behaviorComponent);
    //                ____behaviors.Insert(0, behaviorComponent);
    //            }
    //            __result = true;
    //            return false;
    //        }
    //        __result = false;
    //        return false;
    //    }
    //}

    [HarmonyPatch(typeof(BehaviorRegroup))]
    internal class OverrideBehaviorRegroup
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        private static bool PrefixGetAiWeight(ref BehaviorRegroup __instance, ref float __result)
        {
            if (__instance.Formation != null)
            {
                FormationQuerySystem querySystem = __instance.Formation.QuerySystem;
                if (__instance.Formation.AI.ActiveBehavior == null || querySystem.IsRangedFormation)
                {
                    __result = 0f;
                    return false;
                }
                //if(__instance.Formation.QuerySystem.FormationIntegrityData.DeviationOfPositionsExcludeFarAgents > 15f)
                //{
                //__result = 10f;
                //return false;
                //}
                //__result =  MBMath.Lerp(0.1f, 1.2f, MBMath.ClampFloat(behaviorCoherence * (querySystem.FormationIntegrityData.DeviationOfPositionsExcludeFarAgents + 1f) / (querySystem.IdealAverageDisplacement + 1f), 0f, 3f) / 3f);
                __result = MBMath.Lerp(0.1f, 1.2f, MBMath.ClampFloat(__instance.BehaviorCoherence * (querySystem.FormationIntegrityData.DeviationOfPositionsExcludeFarAgents + 1f) / (querySystem.IdealAverageDisplacement + 1f), 0f, 3f) / 3f);
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorRegroup __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
                if (significantEnemy != null)
                {
                    WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);

                    Vec2 direction = (significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.AveragePosition).Normalized();
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);

                    return false;
                }
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("TickOccasionally")]
        private static void PrefixTickOccasionally(ref BehaviorRegroup __instance)
        {
            __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
        }
    }

    [HarmonyPatch(typeof(BehaviorAdvance))]
    internal class OverrideBehaviorAdvance
    {
        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };
        public static Dictionary<Formation, int> waitCountStorage = new Dictionary<Formation, int> { };

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorAdvance __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

                if (__instance.Formation.QuerySystem.IsInfantryFormation && !RBMAI.Utilities.FormationFightingInMelee(__instance.Formation, 0.5f))
                {
                    FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
                    _currentTacticField.DeclaringType.GetField("_currentTactic");
                    //TacticComponent _currentTactic = (TacticComponent);
                    if (__instance.Formation?.Team?.TeamAI != null)
                    {
                        if (_currentTacticField.GetValue(__instance.Formation?.Team?.TeamAI) != null && _currentTacticField.GetValue(__instance.Formation?.Team?.TeamAI).ToString().Contains("SplitArchers"))
                        {
                            Formation allyArchers = Utilities.FindSignificantAlly(__instance.Formation, false, true, false, false, false);
                            if (allyArchers != null)
                            {
                                Vec2 dir = allyArchers.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                                float allyArchersDist = dir.Normalize();
                                if (allyArchersDist - (allyArchers.Width / 2f) - (__instance.Formation.Width / 2f) > 60f)
                                {
                                    ____currentOrder = MovementOrder.MovementOrderMove(__instance.Formation.QuerySystem.MedianPosition);
                                    return false;
                                }
                            }
                        }
                    }
                    Formation enemyCav = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);

                    if (enemyCav != null && !enemyCav.QuerySystem.IsCavalryFormation)
                    {
                        enemyCav = null;
                    }

                    float cavDist = 0f;
                    float signDist = 1f;

                    if (significantEnemy != null)
                    {
                        Vec2 signDirection = significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                        signDist = signDirection.Normalize();
                    }

                    if (enemyCav != null)
                    {
                        Vec2 cavDirection = enemyCav.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                        cavDist = cavDirection.Normalize();
                    }

                    if ((enemyCav != null) && (cavDist <= signDist) && (enemyCav.CountOfUnits > __instance.Formation.CountOfUnits / 10) && (signDist > 35f))
                    {
                        if (enemyCav.TargetFormation == __instance.Formation && (enemyCav.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || enemyCav.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge))
                        {
                            Vec2 vec = enemyCav.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                            WorldPosition positionNew = __instance.Formation.QuerySystem.MedianPosition;

                            WorldPosition storedPosition = WorldPosition.Invalid;
                            positionsStorage.TryGetValue(__instance.Formation, out storedPosition);

                            if (!storedPosition.IsValid)
                            {
                                positionsStorage.Add(__instance.Formation, positionNew);
                                ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                            }
                            else
                            {
                                ____currentOrder = MovementOrder.MovementOrderMove(storedPosition);
                            }
                            if (cavDist > 70f)
                            {
                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec.Normalized());
                            }
                            //__instance.Formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent) {
                            //    if (RBMAI.Utilities.CheckIfCanBrace(agent))
                            //    {
                            //        agent.SetFiringOrder(1);
                            //    }
                            //    else
                            //    {
                            //        agent.SetFiringOrder(0);
                            //    }
                            //});
                            //__instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                            return false;
                        }
                        positionsStorage.Remove(__instance.Formation);
                        //medianPositionOld = WorldPosition.Invalid;
                    }
                    else if (significantEnemy != null && signDist < 60f && RBMAI.Utilities.FormationActiveSkirmishersRatio(__instance.Formation, 0.33f))
                    {
                        WorldPosition positionNew = __instance.Formation.QuerySystem.MedianPosition;

                        WorldPosition storedPosition = WorldPosition.Invalid;
                        positionsStorage.TryGetValue(__instance.Formation, out storedPosition);

                        if (!storedPosition.IsValid)
                        {
                            positionsStorage.Add(__instance.Formation, positionNew);
                            ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                        }
                        else
                        {
                            ____currentOrder = MovementOrder.MovementOrderMove(storedPosition);
                        }
                        //__instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                        return false;
                        //__instance.Formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent) {
                        //    agent.SetMaximumSpeedLimit(0.1f, true);
                        //});
                    }
                    positionsStorage.Remove(__instance.Formation);
                }

                if (significantEnemy != null)
                {
                    //int storedWaitCount;
                    //if (Mission.Current.AllowAiTicking)
                    //{
                    //    if (waitCountStorage.TryGetValue(__instance.Formation, out storedWaitCount))
                    //    {
                    //        if (storedWaitCount < 100)
                    //        {
                    //            storedWaitCount++;
                    //            Vec2 direction = significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                    //            WorldPosition pos = __instance.Formation.QuerySystem.MedianPosition;
                    //            ____currentOrder = MovementOrder.MovementOrderMove(pos);
                    //            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction.Normalized());
                    //            waitCountStorage[__instance.Formation] = storedWaitCount;
                    //            return false;
                    //        }
                    //    }
                    //    else
                    //    {
                    //        waitCountStorage.Add(__instance.Formation, 0);
                    //    }
                    //}

                    Vec2 vec = significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                    WorldPosition positionNew = __instance.Formation.QuerySystem.MedianPosition;

                    //if (!Mission.Current.IsPositionInsideBoundaries(positionNew.AsVec2) || positionNew.GetNavMesh() == UIntPtr.Zero)
                    //{
                    //}
                    float disper = __instance.Formation.QuerySystem.FormationIntegrityData.DeviationOfPositionsExcludeFarAgents;
                    if (disper > 10f)
                    {
                        positionNew.SetVec2(positionNew.AsVec2 + vec.Normalized() * (20f + __instance.Formation.Depth));
                        if (!Mission.Current.IsPositionInsideBoundaries(positionNew.AsVec2) || positionNew.GetNavMesh() == UIntPtr.Zero)
                        {
                            positionNew.SetVec2(significantEnemy.CurrentPosition);
                        }
                    }
                    else
                    {
                        positionNew.SetVec2(significantEnemy.CurrentPosition);
                    }
                    ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec.Normalized());
                    return false;
                }
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static bool PrefixTickOccasionally(ref BehaviorAdvance __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder,
            ref bool ____isInShieldWallDistance, ref bool ____switchedToShieldWallRecently, ref Timer ____switchedToShieldWallTimer)
        {
            MethodInfo method = typeof(BehaviorAdvance).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
            method.DeclaringType.GetMethod("CalculateCurrentOrder");
            method.Invoke(__instance, new object[] { });

            __instance.Formation.SetMovementOrder(__instance.CurrentOrder);
            __instance.Formation.FacingOrder = ___CurrentFacingOrder;
            if (__instance.Formation.QuerySystem.IsInfantryFormation)
            {
                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
                if (significantEnemy != null)
                {
                    float num = __instance.Formation.QuerySystem.AveragePosition.Distance(significantEnemy.QuerySystem.MedianPosition.AsVec2);
                    if (num < 150f)
                    {
                        if (significantEnemy.ArrangementOrder == ArrangementOrder.ArrangementOrderLine)
                        {
                            __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                        }
                        else if (significantEnemy.ArrangementOrder == ArrangementOrder.ArrangementOrderLoose)
                        {
                            __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
                        }
                    }
                }
                //if (flag != ____isInShieldWallDistance)
                //{
                //    ____isInShieldWallDistance = flag;
                //    if (____isInShieldWallDistance)
                //    {
                //        if (__instance.Formation.QuerySystem.HasShield)
                //        {
                //            __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderShieldWall;
                //        }
                //        else
                //        {
                //            __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
                //        }
                //        ____switchedToShieldWallRecently = true;
                //        ____switchedToShieldWallTimer.Reset(Mission.Current.CurrentTime, 5f);
                //    }
                //    else
                //    {
                //        __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                //    }
                //}
            }
            __instance.Formation.SetMovementOrder(__instance.CurrentOrder);
            return false;
        }
    }

    [HarmonyPatch(typeof(LineFormation))]
    internal class OverrideLineFormation
    {
        [HarmonyPrefix]
        [HarmonyPatch("SwitchUnitLocations")]
        private static bool PrefixSwitchUnitLocations(ref BehaviorRegroup __instance, IFormationUnit firstUnit, IFormationUnit secondUnit)
        {
            if(firstUnit != null && ((Agent)firstUnit).Formation != null && ((Agent)firstUnit).IsActive() && 
                secondUnit != null && ((Agent)secondUnit).Formation != null && ((Agent)secondUnit).IsActive() )
            {
                return true;
            }
            else
            {
                return false;
            }

        }

        [HarmonyPrefix]
        [HarmonyPatch("RemoveUnit", new Type[] { typeof(IFormationUnit), typeof(bool), typeof(bool) })]
        private static bool PrefixRemoveUnit(IFormationUnit unit, bool fillInTheGap, bool isRemovingFromAnUnavailablePosition = false)
        {
            int formationFileIndex = unit.FormationFileIndex;
            int formationRankIndex = unit.FormationRankIndex;
            if (unit != null && ((Agent)unit).Formation != null && formationFileIndex != -1 && formationRankIndex != -1)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

    }

}