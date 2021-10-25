using HarmonyLib;
using SandBox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.FormationAI;
using static TaleWorlds.MountAndBlade.HumanAIComponent;

namespace RealisticBattleAiModule
{
    class Behaviours
    {

        [HarmonyPatch(typeof(BehaviorSkirmishLine))]
        class OverrideBehaviorSkirmishLine
        {
            [HarmonyPostfix]
            [HarmonyPatch("CalculateCurrentOrder")]
            static void PostfixCalculateCurrentOrder(Formation ____mainFormation, ref FacingOrder ___CurrentFacingOrder)
            {
                if (____mainFormation != null)
                {
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(____mainFormation.Direction);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("OnBehaviorActivatedAux")]
            static void PostfixOnBehaviorActivatedAux(ref BehaviorSkirmishLine __instance)
            {
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
                __instance.Formation.FormOrder = FormOrder.FormOrderWide;
            }
        }

        [HarmonyPatch(typeof(BehaviorDefend))]
        class OverrideBehaviorDefend
        {
            public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

            [HarmonyPostfix]
            [HarmonyPatch("CalculateCurrentOrder")]
            static void PostfixCalculateCurrentOrder(ref BehaviorDefend __instance, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged, ref FacingOrder ___CurrentFacingOrder)
            {
                if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    WorldPosition medianPositionNew = __instance.Formation.QuerySystem.MedianPosition;
                    medianPositionNew.SetVec2(__instance.Formation.QuerySystem.AveragePosition);

                    Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false);

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
        class OverrideBehaviorHoldHighGround
        {
            public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

            [HarmonyPostfix]
            [HarmonyPatch("CalculateCurrentOrder")]
            static void PostfixCalculateCurrentOrder(ref BehaviorHoldHighGround __instance, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged, ref FacingOrder ___CurrentFacingOrder)
            {
                if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    WorldPosition medianPositionNew = __instance.Formation.QuerySystem.MedianPosition;
                    medianPositionNew.SetVec2(__instance.Formation.QuerySystem.AveragePosition);

                    Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false);

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
        class OverrideBehaviorScreenedSkirmish
        {

            [HarmonyPostfix]
            [HarmonyPatch("CalculateCurrentOrder")]
            static void PostfixCalculateCurrentOrder(Formation ____mainFormation, ref BehaviorScreenedSkirmish __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
            {
                if (____mainFormation != null && __instance.Formation != null)
                {
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(____mainFormation.Direction);

                    WorldPosition medianPosition = ____mainFormation.QuerySystem.MedianPosition;
                    //medianPosition.SetVec2(medianPosition.AsVec2 - ____mainFormation.Direction * ((____mainFormation.Depth + __instance.Formation.Depth) * 1.5f));
                    medianPosition.SetVec2(medianPosition.AsVec2 - ____mainFormation.Direction * ((____mainFormation.Depth + __instance.Formation.Depth) * 0.25f + 0.0f));
                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);

                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("TickOccasionally")]
            static bool PrefixTickOccasionally(Formation ____mainFormation, BehaviorScreenedSkirmish __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
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
            static void PostfixOnBehaviorActivatedAux(ref BehaviorScreenedSkirmish __instance)
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

    [HarmonyPatch(typeof(BehaviorSkirmish))]
    class OverrideBehaviorSkirmish
    {
        private enum BehaviorState
        {
            Approaching,
            Shooting,
            PullingBack
        }

        public static Dictionary<Formation, Vec2> approachingRangingStorage = new Dictionary<Formation, Vec2> { };
        public static Dictionary<Formation, int> waitCountShootingStorage = new Dictionary<Formation, int> { };
        public static Dictionary<Formation, int> waitCountApproachingStorage = new Dictionary<Formation, int> { };

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static void PostfixCalculateCurrentOrder(BehaviorSkirmish __instance, ref FacingOrder ___CurrentFacingOrder, ref BehaviorState ____behaviorState, ref MovementOrder ____currentOrder)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false);

                int waitCountShooting;
                int waitCountApproaching;
                Vec2 approachingRangingPos;
                if (!waitCountShootingStorage.TryGetValue(__instance.Formation, out waitCountShooting))
                {
                    waitCountShootingStorage[__instance.Formation] = 0;
                }
                if (!waitCountApproachingStorage.TryGetValue(__instance.Formation, out waitCountApproaching))
                {
                    waitCountApproachingStorage[__instance.Formation] = 0;
                }
                if (!waitCountApproachingStorage.TryGetValue(__instance.Formation, out waitCountApproaching))
                {
                    waitCountApproachingStorage[__instance.Formation] = 0;
                }
                if (!approachingRangingStorage.TryGetValue(__instance.Formation, out approachingRangingPos))
                {
                    approachingRangingStorage[__instance.Formation] = __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                }

                if (significantEnemy != null)
                {
                    Vec2 enemyDirection = significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                    float distance = enemyDirection.Normalize();
                    switch (____behaviorState)
                    {
                        case BehaviorState.Shooting:
                            if (waitCountShootingStorage[__instance.Formation] > 45)
                            {
                                if (distance > 100f)
                                {
                                    WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                                    medianPosition.SetVec2(medianPosition.AsVec2 + enemyDirection * 5f);
                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                                    waitCountShootingStorage[__instance.Formation] = 0;
                                    break;
                                }
                                if (distance < 40f && distance > 25f)
                                {
                                    WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                                    medianPosition.SetVec2(medianPosition.AsVec2 - enemyDirection * 5f);
                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(__instance.Formation.Direction);
                                    //___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                                    waitCountShootingStorage[__instance.Formation] = 0;
                                    break;
                                }
                                if (__instance.Formation.QuerySystem.MakingRangedAttackRatio < 0.3f && distance > 40f)
                                {
                                    WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                                    medianPosition.SetVec2(medianPosition.AsVec2 + enemyDirection * 5f);
                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                                    waitCountShootingStorage[__instance.Formation] = 0;
                                    break;
                                }

                            }
                            else
                            {
                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                                waitCountShootingStorage[__instance.Formation] = waitCountShootingStorage[__instance.Formation] + 1;
                            }

                            break;
                        case BehaviorState.Approaching:
                            if (waitCountApproachingStorage[__instance.Formation] > 20)
                            {
                                //if (distance < 200f)
                                //{
                                WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                                medianPosition.SetVec2(medianPosition.AsVec2 + enemyDirection * 5f);
                                approachingRangingStorage[__instance.Formation] = medianPosition.AsVec2;
                                ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                                //}
                                waitCountApproachingStorage[__instance.Formation] = 0;
                            }
                            else
                            {
                                WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                                medianPosition.SetVec2(approachingRangingStorage[__instance.Formation]);
                                ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                                waitCountApproachingStorage[__instance.Formation] = waitCountApproachingStorage[__instance.Formation] + 1;
                            }
                            break;

                    }
                }
            }

        }
    }

    [HarmonyPatch(typeof(BehaviorCautiousAdvance))]
    class OverrideBehaviorCautiousAdvance
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
        static void PostfixCalculateCurrentOrder(ref Vec2 ____shootPosition, ref Formation ____archerFormation, BehaviorCautiousAdvance __instance, ref BehaviorState ____behaviorState, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && ____archerFormation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false);

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
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                    waitCountShootingStorage[__instance.Formation] = waitCountShootingStorage[__instance.Formation] + 1;
                                }
                                break;
                            }
                        case BehaviorState.Approaching:
                            {
                                if (distance > 210f)
                                {
                                    WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                                    medianPosition.SetVec2(medianPosition.AsVec2 + vec * 10f);
                                    ____shootPosition = medianPosition.AsVec2 + vec * 10f;
                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                }
                                else
                                {
                                    if (waitCountApproachingStorage[__instance.Formation] > 30)
                                    {
                                        if (distance < 200f)
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
                                        if (distance < 200f)
                                        {
                                            WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                                            medianPosition.SetVec2(____shootPosition);
                                            ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                        }
                                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                        waitCountApproachingStorage[__instance.Formation] = waitCountApproachingStorage[__instance.Formation] + 1;
                                    }
                                }
                                break;
                            }
                        case BehaviorState.PullingBack:
                            {
                                if (waitCountApproachingStorage[__instance.Formation] > 30)
                                {
                                    if (distance < 200f)
                                    {
                                        WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                                        medianPosition.SetVec2(medianPosition.AsVec2 - vec * 10f);
                                        ____shootPosition = medianPosition.AsVec2 + vec * 5f;
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
                                    waitCountApproachingStorage[__instance.Formation] = 0;

                                }
                                else
                                {
                                    if (distance < 200f)
                                    {
                                        WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                                        medianPosition.SetVec2(____shootPosition);
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
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
    class OverrideBehaviorMountedSkirmish
    {
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

            public Vec2 GetTargetPos(Vec2 position, float distance)
            {
                Vec2 vec = _direction.LeftVec();
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
                double num = Math.PI * 2.0 * (double)_radius;
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
                        double num3 = Math.Acos(MBMath.ClampFloat(_direction.DotProduct(v), -1f, 1f));
                        double num4 = Math.PI * 2.0 * ((double)distance / num);
                        double num5 = ((num3 + num4 < Math.PI) ? (num3 + num4) : Math.PI);
                        double num6 = (num5 - num3) / Math.PI * (num / 2.0);
                        Vec2 direction = _direction;
                        direction.RotateCCW((float)num5);
                        position = vec2 + direction * _radius;
                        distance -= (float)num6;
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
                        double num8 = Math.Acos(MBMath.ClampFloat(_direction.DotProduct(vec8), -1f, 1f));
                        double num9 = Math.PI * 2.0 * ((double)distance / num);
                        double num10 = ((num8 - num9 > 0.0) ? (num8 - num9) : 0.0);
                        double num11 = num8 - num10;
                        double num12 = num11 / Math.PI * (num / 2.0);
                        Vec2 vec9 = vec8;
                        vec9.RotateCCW((float)num11);
                        position = vec3 + vec9 * _radius;
                        distance -= (float)num12;
                        flag2 = true;
                        flag = true;
                    }
                }
                return position;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static void PostfixCalculateCurrentOrder(BehaviorMountedSkirmish __instance, ref bool ____engaging, ref MovementOrder ____currentOrder, ref bool ____isEnemyReachable)
        {
            WorldPosition position = __instance.Formation.QuerySystem.MedianPosition;
            ____isEnemyReachable = __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && (!(__instance.Formation.Team.TeamAI is TeamAISiegeComponent) || !TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false));
            if (!____isEnemyReachable)
            {
                position.SetVec2(__instance.Formation.QuerySystem.AveragePosition);
            }
            else
            {
                bool num = (__instance.Formation.QuerySystem.AverageAllyPosition - __instance.Formation.Team.QuerySystem.AverageEnemyPosition).LengthSquared <= 3600f;
                bool engaging = ____engaging;
                engaging = (____engaging = (num || ((!____engaging) ? ((__instance.Formation.QuerySystem.AveragePosition - __instance.Formation.QuerySystem.AverageAllyPosition).LengthSquared <= 3600f) : (!(__instance.Formation.QuerySystem.UnderRangedAttackRatio > __instance.Formation.QuerySystem.MakingRangedAttackRatio) && ((!__instance.Formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.IsCavalryFormation && !__instance.Formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.IsRangedCavalryFormation) || (__instance.Formation.QuerySystem.AveragePosition - __instance.Formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.MedianPosition.AsVec2).LengthSquared / (__instance.Formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.MovementSpeed * __instance.Formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.MovementSpeed) >= 16f)))));
                if (!____engaging)
                {
                    position = new WorldPosition(Mission.Current.Scene, new Vec3(__instance.Formation.QuerySystem.AverageAllyPosition, __instance.Formation.Team.QuerySystem.MedianPosition.GetNavMeshZ() + 100f));
                }
                else
                {
                    Vec2 vec = (__instance.Formation.QuerySystem.AveragePosition - __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.AveragePosition).Normalized().LeftVec();
                    FormationQuerySystem closestSignificantlyLargeEnemyFormation = __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                    float num2 = 50f + (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.Width + __instance.Formation.Depth) * 0.5f;
                    float num3 = 0f;

                    Formation enemyFormation = __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation;

                    if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
                    {
                        enemyFormation = Utilities.FindSignificantEnemyToPosition(__instance.Formation, position, true, true, false, false, false, false);
                    }

                    foreach (Team team in Mission.Current.Teams.ToList())
                    {
                        if (!team.IsEnemyOf(__instance.Formation.Team))
                        {
                            continue;
                        }
                        foreach (Formation formation2 in team.FormationsIncludingSpecialAndEmpty.ToList())
                        {
                            if (formation2.CountOfUnits > 0 && formation2.QuerySystem != closestSignificantlyLargeEnemyFormation)
                            {
                                Vec2 v = formation2.QuerySystem.AveragePosition - closestSignificantlyLargeEnemyFormation.AveragePosition;
                                float num4 = v.Normalize();
                                if (vec.DotProduct(v) > 0.8f && num4 < num2 && num4 > num3)
                                {
                                    num3 = num4;
                                    enemyFormation = formation2;
                                }
                            }
                        }
                    }
                    if (__instance.Formation.QuerySystem.RangedCavalryUnitRatio > 0.95f && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation == enemyFormation)
                    {
                        ____currentOrder = MovementOrder.MovementOrderCharge;
                        return;
                    }
                    if (enemyFormation != null && enemyFormation.QuerySystem != null)
                    {
                        bool flag = enemyFormation.QuerySystem.IsCavalryFormation || enemyFormation.QuerySystem.IsRangedCavalryFormation;
                        float num5 = flag ? 35f : 20f;
                        num5 += (enemyFormation.Depth + __instance.Formation.Width) * 0.25f;
                        //num5 = Math.Min(num5, __instance.Formation.QuerySystem.MissileRange - __instance.Formation.Width * 0.5f);
                        if (__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            Ellipse ellipse = new Ellipse(enemyFormation.QuerySystem.MedianPosition.AsVec2, num5, enemyFormation.Width * 0.5f * (flag ? 1.5f : 1f), enemyFormation.Direction);
                            position.SetVec2(ellipse.GetTargetPos(__instance.Formation.QuerySystem.AveragePosition, 20f));
                        }
                        else
                        {
                            Ellipse ellipse = new Ellipse(enemyFormation.QuerySystem.MedianPosition.AsVec2, num5, enemyFormation.Width * 0.25f * (flag ? 1.5f : 1f), enemyFormation.Direction);
                            position.SetVec2(ellipse.GetTargetPos(__instance.Formation.QuerySystem.AveragePosition, 20f));
                        }
                    }
                    else
                    {
                        position.SetVec2(__instance.Formation.QuerySystem.AveragePosition);
                    }
                }
            }
            ____currentOrder = MovementOrder.MovementOrderMove(position);
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetAiWeight")]
        static void PostfixGetAiWeight(ref BehaviorMountedSkirmish __instance, ref float __result)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsCavalryFormation)
            {
                if (Utilities.CheckIfMountedSkirmishFormation(__instance.Formation, 0.6f))
                {
                    __result = 5f;
                }
                else
                {
                    __result = 0f;
                }
            }
            else if (__instance.Formation != null && __instance.Formation.QuerySystem.IsRangedCavalryFormation)
            {
                __result = 1.5f;
            }
            else
            {
                int countOfSkirmishers = 0;
                __instance.Formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    if (Utilities.CheckIfSkirmisherAgent(agent, 1))
                    {
                        countOfSkirmishers++;
                    }
                });
                if (countOfSkirmishers / __instance.Formation.CountOfUnits > 0.6f)
                {
                    __result = 1f;
                }
                else
                {
                    __result = 0f;
                }
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorProtectFlank))]
    class OverrideBehaviorProtectFlank
    {
        private enum BehaviorState
        {
            HoldingFlank,
            Charging,
            Returning
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static bool PrefixCalculateCurrentOrder(ref BehaviorProtectFlank __instance, ref FormationAI.BehaviorSide ___FlankSide, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref MovementOrder ____chargeToTargetOrder, ref MovementOrder ____movementOrder, ref BehaviorState ____protectFlankState, ref Formation ____mainFormation, ref FormationAI.BehaviorSide ___behaviorSide)
        {

            float distanceFromMainFormation = 80f;
            float closerDistanceFromMainFormation = 30f;
            float distanceOffsetFromMainFormation = 130f;

            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                distanceFromMainFormation = 10f;
                closerDistanceFromMainFormation = 5f;
                distanceOffsetFromMainFormation = 110f;
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
                if (___behaviorSide == FormationAI.BehaviorSide.Right || ___FlankSide == FormationAI.BehaviorSide.Right)
                {
                    vec = ____mainFormation.CurrentPosition + v.RightVec().Normalized() * (____mainFormation.Width + __instance.Formation.Width + distanceFromMainFormation);
                    vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                    vec += ____mainFormation.Direction * distanceOffsetFromMainFormation;
                    if (!Mission.Current.IsPositionInsideBoundaries(vec))
                    {
                        vec = ____mainFormation.CurrentPosition + v.RightVec().Normalized() * (____mainFormation.Width + __instance.Formation.Width + closerDistanceFromMainFormation);
                        vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                        vec += ____mainFormation.Direction;
                    }
                }
                else if (___behaviorSide == FormationAI.BehaviorSide.Left || ___FlankSide == FormationAI.BehaviorSide.Left)
                {
                    vec = ____mainFormation.CurrentPosition + v.LeftVec().Normalized() * (____mainFormation.Width + __instance.Formation.Width + distanceFromMainFormation);
                    vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                    vec += ____mainFormation.Direction * distanceOffsetFromMainFormation;
                    if (!Mission.Current.IsPositionInsideBoundaries(vec))
                    {
                        vec = ____mainFormation.CurrentPosition + v.LeftVec().Normalized() * (____mainFormation.Width + __instance.Formation.Width + closerDistanceFromMainFormation);
                        vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                        vec += ____mainFormation.Direction;
                    }
                }
                else
                {
                    vec = ____mainFormation.CurrentPosition + v * ((____mainFormation.Depth + __instance.Formation.Depth) * 0.5f + 10f);
                }
                WorldPosition medianPosition = ____mainFormation.QuerySystem.MedianPosition;
                medianPosition.SetVec2(vec);
                ____movementOrder = MovementOrder.MovementOrderMove(medianPosition);
                ____currentOrder = ____movementOrder;
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CheckAndChangeState")]
        static bool PrefixCheckAndChangeState(ref BehaviorProtectFlank __instance, ref FormationAI.BehaviorSide ___FlankSide, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref MovementOrder ____chargeToTargetOrder, ref MovementOrder ____movementOrder, ref BehaviorState ____protectFlankState, ref Formation ____mainFormation, ref FormationAI.BehaviorSide ___behaviorSide)
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
                                    float changeToChargeDistance = 90f + (__instance.Formation.Depth + closestFormation.Formation.Depth) / 2f;
                                    if (closestFormation.Formation.QuerySystem.MedianPosition.AsVec2.DistanceSquared(position) < changeToChargeDistance * changeToChargeDistance)
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
                                    float returnDistance = 100f + (__instance.Formation.Depth + closestFormation.Formation.Depth) / 2f;
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
        [HarmonyPatch("GetAiWeight")]
        static void PostfixGetAiWeight(ref float __result)
        {
            if (__result > 0f)
            {
                __result = __result + 0.5f;
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorHorseArcherSkirmish))]
    class OverrideBehaviorHorseArcherSkirmish
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        static bool PrefixGetAiWeight(ref float __result)
        {
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorPullBack))]
    class OverrideBehaviorPullBack
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        static bool PrefixGetAiWeight(ref float __result)
        {
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorVanguard))]
    class OverrideBehaviorVanguard
    {
        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        static bool PrefixTickOccasionally(ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder, BehaviorVanguard __instance)
        {
            MethodInfo method = typeof(BehaviorVanguard).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
            method.DeclaringType.GetMethod("CalculateCurrentOrder");
            method.Invoke(__instance, new object[] { });

            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.FacingOrder = ___CurrentFacingOrder;
            if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && __instance.Formation.QuerySystem.AveragePosition.DistanceSquared(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.MedianPosition.AsVec2) > 1600f && __instance.Formation.QuerySystem.UnderRangedAttackRatio > 0.2f - ((__instance.Formation.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Loose) ? 0.1f : 0f))
            {
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
            }
            else
            {
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorAssaultWalls))]
    class OverrideBehaviorAssaultWalls
    {

        private enum BehaviorState
        {
            Deciding,
            ClimbWall,
            AttackEntity,
            TakeControl,
            MoveToGate,
            Charging,
            Stop
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static bool PrefixCalculateCurrentOrder(ref BehaviorAssaultWalls __instance, ref MovementOrder ____chargeOrder)
        {
            //__instance.Formation.AI.SetBehaviorWeight<BehaviorCharge>(0f);
            //if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            //{
            //    ____chargeOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
            //}
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static void PostfixCalculateCurrentOrder(ref BehaviorAssaultWalls __instance, ref MovementOrder ____attackEntityOrderOuterGate, ref ArrangementOrder ___CurrentArrangementOrder, ref MovementOrder ____chargeOrder, ref TeamAISiegeComponent ____teamAISiegeComponent, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState, ref MovementOrder ____attackEntityOrderInnerGate)
        {

            //____attackEntityOrderInnerGate = MovementOrder.MovementOrderAttackEntity(____teamAISiegeComponent.InnerGate.GameEntity, surroundEntity: false);
            switch (____behaviorState)
            {
                case BehaviorState.ClimbWall:
                    {
                        //if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                        //{
                        //    ____currentOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                        //}
                        break;
                    }
                case BehaviorState.AttackEntity:
                    {
                        if (____attackEntityOrderInnerGate.TargetEntity != null)
                        {
                            __instance.Formation.FormAttackEntityDetachment(____attackEntityOrderInnerGate.TargetEntity);
                        }

                        ___CurrentArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
                        break;
                    }
                case BehaviorState.Charging:
                case BehaviorState.TakeControl:
                    {

                        if (__instance.Formation.AI.Side == BehaviorSide.Middle)
                        {
                            __instance.Formation.DisbandAttackEntityDetachment();

                            foreach (IDetachment detach in __instance.Formation.Detachments.ToList())
                            {
                                __instance.Formation.LeaveDetachment(detach);
                            }
                        }

                        if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                        {
                            //____attackEntityOrderInnerGate = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            //____attackEntityOrderOuterGate = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            ____chargeOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            ____chargeOrder.TargetEntity = null;
                            ____currentOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            ____currentOrder.TargetEntity = null;
                        }
                        break;
                    }
            }
        }
    }


    //[HarmonyPatch(typeof(AttackEntityOrderDetachment))]
    //class OverrideAttackEntityOrderDetachment
    //{

    //    [HarmonyPostfix]
    //    [HarmonyPatch("Initialize")]
    //    static void PostfixInitialize(ref BattleSideEnum managedSide, Vec3 managedDirection, ref float queueBeginDistance, ref int ____maxUserCount, ref float ____agentSpacing, ref float ____queueBeginDistance, ref float ____queueRowSize, ref float ____costPerRow, ref float ____baseCost)
    //    {

    //    }
    //}

    [HarmonyPatch(typeof(BehaviorShootFromCastleWalls))]
    class OverrideBehaviorShootFromCastleWalls
    {
        [HarmonyPrefix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        static bool PrefixOnBehaviorActivatedAux(ref BehaviorShootFromCastleWalls __instance, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder)
        {
            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.FacingOrder = ___CurrentFacingOrder;
            __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderScatter;
            __instance.Formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
            __instance.Formation.FormOrder = FormOrder.FormOrderCustom(40f);
            __instance.Formation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        static bool PrefixTickOccasionally(ref BehaviorShootFromCastleWalls __instance, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref TacticalPosition ____tacticalArcherPosition)
        {
            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.FacingOrder = ___CurrentFacingOrder;
            if (____tacticalArcherPosition != null)
            {
                __instance.Formation.FormOrder = FormOrder.FormOrderCustom(____tacticalArcherPosition.Width * 5f);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorUseSiegeMachines))]
    class OverrideBehaviorUseSiegeMachines
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        static bool PrefixGetAiWeight(ref BehaviorUseSiegeMachines __instance, ref float __result, ref TeamAISiegeComponent ____teamAISiegeComponent, List<UsableMachine> ____primarySiegeWeapons)
        {
            float result = 0f;
            if (____teamAISiegeComponent != null && ____primarySiegeWeapons.Any() && ____primarySiegeWeapons.All((UsableMachine psw) => !(psw as IPrimarySiegeWeapon).HasCompletedAction()))
            {
                result = (____teamAISiegeComponent.IsCastleBreached() ? 0.75f : 1.5f);
            }
            __result = result;
            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorWaitForLadders))]
    class OverrideBehaviorWaitForLadders
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        static bool PrefixOnGetAiWeight(ref BehaviorUseSiegeMachines __instance, MovementOrder ____followOrder, ref TacticalPosition ____followTacticalPosition, ref float __result, ref TeamAISiegeComponent ____teamAISiegeComponent)
        {
            if (____followTacticalPosition != null)
            {
                float result = 0f;
                if (____followTacticalPosition.Position.AsVec2.Distance(__instance.Formation.QuerySystem.AveragePosition) > 7f)
                {
                    if (____followOrder.OrderEnum != 0 && !____teamAISiegeComponent.AreLaddersReady)
                    {
                        result = ((!____teamAISiegeComponent.IsCastleBreached()) ? 2f : 1f);
                    }
                    __result = result;
                }
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BehaviorDefendCastleKeyPosition))]
    class OverrideBehaviorDefendCastleKeyPosition
    {

        private enum BehaviorState
        {
            UnSet,
            Waiting,
            Ready
        }

        [HarmonyPrefix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        static bool PrefixOnBehaviorActivatedAux(ref BehaviorDefendCastleKeyPosition __instance, ref FacingOrder ____waitFacingOrder, ref FacingOrder ____readyFacingOrder, ref TeamAISiegeComponent ____teamAISiegeDefender, ref bool ____isDefendingWideGap, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ___behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
        {
            MethodInfo method = typeof(BehaviorDefendCastleKeyPosition).GetMethod("ResetOrderPositions", BindingFlags.NonPublic | BindingFlags.Instance);
            method.DeclaringType.GetMethod("ResetOrderPositions");
            method.Invoke(__instance, new object[] { });

            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.FacingOrder = ___CurrentFacingOrder;
            __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
            __instance.Formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
            //formation.FormOrder = FormOrder.FormOrderWide;
            __instance.Formation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("ResetOrderPositions")]
        static bool PrefixResetOrderPositions(ref BehaviorDefendCastleKeyPosition __instance, ref FacingOrder ____waitFacingOrder, ref FacingOrder ____readyFacingOrder, ref TeamAISiegeComponent ____teamAISiegeDefender, ref bool ____isDefendingWideGap, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ___behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
        {
            ___behaviorSide = __instance.Formation.AI.Side;
            ____innerGate = null;
            ____outerGate = null;
            ____laddersOnThisSide.Clear();
            bool num = Mission.Current.ActiveMissionObjects.FindAllWithType<CastleGate>().Any((CastleGate cg) => cg.DefenseSide == ___behaviorSide && cg.GameEntity.HasTag("outer_gate"));
            ____isDefendingWideGap = false;
            WorldFrame worldFrame;
            WorldFrame worldFrame2;
            if (num)
            {
                CastleGate outerGate = ____teamAISiegeDefender.OuterGate;
                ____innerGate = ____teamAISiegeDefender.InnerGate;
                ____outerGate = ____teamAISiegeDefender.OuterGate;
                worldFrame = outerGate.MiddleFrame;
                worldFrame2 = outerGate.DefenseWaitFrame;
                ____tacticalMiddlePos = outerGate.MiddlePosition;
                ____tacticalWaitPos = outerGate.WaitPosition;
                ____isDefendingWideGap = false;
            }
            else
            {
                WallSegment wallSegment = (from ws in Mission.Current.ActiveMissionObjects.FindAllWithType<WallSegment>()
                                           where ws.DefenseSide == ___behaviorSide && ws.IsBreachedWall
                                           select ws).FirstOrDefault();
                if (wallSegment != null)
                {
                    worldFrame = wallSegment.MiddleFrame;
                    worldFrame2 = wallSegment.DefenseWaitFrame;
                    ____tacticalMiddlePos = wallSegment.MiddlePosition;
                    ____tacticalWaitPos = wallSegment.WaitPosition;
                    ____isDefendingWideGap = false;
                }
                else
                {
                    IEnumerable<SiegeWeapon> source = from sw in Mission.Current.ActiveMissionObjects.FindAllWithType<SiegeWeapon>()
                                                      where sw is IPrimarySiegeWeapon && (sw as IPrimarySiegeWeapon).WeaponSide == ___behaviorSide && (!sw.IsDestroyed)
                                                      select sw;
                    if (!source.Any())
                    {
                        worldFrame = WorldFrame.Invalid;
                        worldFrame2 = WorldFrame.Invalid;
                        ____tacticalMiddlePos = null;
                        ____tacticalWaitPos = null;
                    }
                    else
                    {
                        ICastleKeyPosition castleKeyPosition = (source.FirstOrDefault() as IPrimarySiegeWeapon).TargetCastlePosition as ICastleKeyPosition;
                        worldFrame = castleKeyPosition.MiddleFrame;
                        worldFrame2 = castleKeyPosition.DefenseWaitFrame;
                        ____tacticalMiddlePos = castleKeyPosition.MiddlePosition;
                        ____tacticalWaitPos = castleKeyPosition.WaitPosition;
                    }
                }
            }
            if (____tacticalMiddlePos != null)
            {
                ____readyFacingOrder = FacingOrder.FacingOrderLookAtDirection(____tacticalMiddlePos.Direction);
                ____readyOrder = MovementOrder.MovementOrderMove(____tacticalMiddlePos.Position);
            }
            else if (worldFrame.Origin.IsValid)
            {
                worldFrame.Rotation.f.Normalize();
                ____readyOrder = MovementOrder.MovementOrderMove(worldFrame.Origin);
                ____readyFacingOrder = FacingOrder.FacingOrderLookAtDirection(worldFrame.Rotation.f.AsVec2);
            }
            else
            {
                ____readyOrder = MovementOrder.MovementOrderStop;
                ____readyFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            }
            if (____tacticalWaitPos != null)
            {
                ____waitFacingOrder = FacingOrder.FacingOrderLookAtDirection(____tacticalWaitPos.Direction);
                ____waitOrder = MovementOrder.MovementOrderMove(____tacticalWaitPos.Position);
            }
            else if (worldFrame2.Origin.IsValid)
            {
                worldFrame2.Rotation.f.Normalize();
                ____waitOrder = MovementOrder.MovementOrderMove(worldFrame2.Origin);
                ____waitFacingOrder = FacingOrder.FacingOrderLookAtDirection(worldFrame2.Rotation.f.AsVec2);
            }
            else
            {
                ____waitOrder = MovementOrder.MovementOrderStop;
                ____waitFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            }

            if (____behaviorState == BehaviorState.Ready && ____tacticalMiddlePos != null)
            {
                __instance.Formation.FormOrder = FormOrder.FormOrderCustom(____tacticalMiddlePos.Width * 2f);
            }
            else if (____behaviorState == BehaviorState.Waiting && ____tacticalWaitPos != null)
            {
                __instance.Formation.FormOrder = FormOrder.FormOrderCustom(____tacticalWaitPos.Width * 2f);
            }
            else
            {
                //__instance.Formation.FormOrder = FormOrder.FormOrderCustom(30f);
            }

            ____currentOrder = ((____behaviorState == BehaviorState.Ready) ? ____readyOrder : ____waitOrder);
            ___CurrentFacingOrder = ((__instance.Formation.QuerySystem.ClosestEnemyFormation != null && TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestEnemyFormation.Formation, includeOnlyPositionedUnits: true)) ? FacingOrder.FacingOrderLookAtEnemy : ((____behaviorState == BehaviorState.Ready) ? ____readyFacingOrder : ____waitFacingOrder));
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch("ResetOrderPositions")]
        static void PostfixResetOrderPositions(ref BehaviorDefendCastleKeyPosition __instance, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
        {
            ____laddersOnThisSide.Clear();
            if (____tacticalMiddlePos != null)
            {
                if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    if (____innerGate == null)
                    {
                        if (____outerGate != null)
                        {
                            float distance = __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
                            if ((____outerGate.IsDestroyed || ____outerGate.IsGateOpen) && TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false) && distance < 35f)
                            {
                                ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                                ____currentOrder = ____readyOrder;
                            }
                        }
                    }
                    else
                    {
                        float distance = __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
                        if ((____innerGate.IsDestroyed || ____innerGate.IsGateOpen) && TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false) && distance < 35f)
                        {
                            ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            ____currentOrder = ____readyOrder;
                        }
                    }
                }


                if (____innerGate != null && !____innerGate.IsDestroyed)
                {
                    WorldPosition position = ____tacticalMiddlePos.Position;
                    if (____behaviorState == BehaviorState.Ready)
                    {
                        Vec2 direction = (____innerGate.GetPosition().AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2).Normalized();
                        WorldPosition newPosition = position;
                        newPosition.SetVec2(position.AsVec2 - direction * 2f);
                        ____readyOrder = MovementOrder.MovementOrderMove(newPosition);
                        ____currentOrder = ____readyOrder;
                    }
                }
            }

            if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && ____tacticalMiddlePos != null && ____innerGate == null && ____outerGate == null)
            {
                WorldPosition position = ____tacticalMiddlePos.Position;
                Formation correctEnemy = Utilities.FindSignificantEnemyToPosition(__instance.Formation, position, true, false, false, false, false, true);
                if (correctEnemy != null)
                {
                    if (TeamAISiegeComponent.IsFormationInsideCastle(correctEnemy, includeOnlyPositionedUnits: false, 0.15f))
                    {
                        ____readyOrder = MovementOrder.MovementOrderChargeToTarget(correctEnemy);
                        ____currentOrder = ____readyOrder;
                    }
                }
            }

            if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && ____tacticalWaitPos != null && ____tacticalMiddlePos == null)
            {
                float distance = __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
                if (TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false) && distance < 35f)
                {
                    ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                    ____currentOrder = ____readyOrder;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        static bool PrefixTickOccasionally(ref FacingOrder ____readyFacingOrder, ref FacingOrder ____waitFacingOrder, ref BehaviorDefendCastleKeyPosition __instance, ref bool ____isInShieldWallDistance, ref TeamAISiegeComponent ____teamAISiegeDefender, ref bool ____isDefendingWideGap, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ___behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
        {
            IEnumerable<SiegeWeapon> source = from sw in Mission.Current.ActiveMissionObjects.FindAllWithType<SiegeWeapon>()
                                              where sw is IPrimarySiegeWeapon && (((sw as IPrimarySiegeWeapon).WeaponSide == FormationAI.BehaviorSide.Middle && !(sw as IPrimarySiegeWeapon).HoldLadders) || (sw as IPrimarySiegeWeapon).WeaponSide != FormationAI.BehaviorSide.Middle && (sw as IPrimarySiegeWeapon).SendLadders)
                                              //where sw is IPrimarySiegeWeapon
                                              select sw;

            BehaviorState BehaviorState = ____teamAISiegeDefender == null || !source.Any() ? BehaviorState.Waiting : BehaviorState.Ready;
            if (BehaviorState != ____behaviorState)
            {
                ____behaviorState = BehaviorState;
                ____currentOrder = ((____behaviorState == BehaviorState.Ready) ? ____readyOrder : ____waitOrder);
                ___CurrentFacingOrder = ((____behaviorState == BehaviorState.Ready) ? ____readyFacingOrder : ____waitFacingOrder);
            }
            if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.Siege)
            {
                if (____outerGate != null && ____outerGate.State == CastleGate.GateState.Open && !____outerGate.IsDestroyed)
                {
                    if (!__instance.Formation.IsUsingMachine(____outerGate))
                    {
                        __instance.Formation.StartUsingMachine(____outerGate);
                    }
                }
                else if (____innerGate != null && ____innerGate.State == CastleGate.GateState.Open && !____innerGate.IsDestroyed && !__instance.Formation.IsUsingMachine(____innerGate))
                {
                    __instance.Formation.StartUsingMachine(____innerGate);
                }
            }

            MethodInfo method = typeof(BehaviorDefendCastleKeyPosition).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
            method.DeclaringType.GetMethod("CalculateCurrentOrder");
            method.Invoke(__instance, new object[] { });

            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.FacingOrder = ___CurrentFacingOrder;
            if (____behaviorState == BehaviorState.Ready && ____tacticalMiddlePos != null)
            {
                __instance.Formation.FormOrder = FormOrder.FormOrderCustom(____tacticalMiddlePos.Width * 2f);
            }
            else if (____behaviorState == BehaviorState.Waiting && ____tacticalWaitPos != null)
            {
                __instance.Formation.FormOrder = FormOrder.FormOrderCustom(____tacticalWaitPos.Width * 2f);
            }
            bool flag = ____isDefendingWideGap && ____behaviorState == BehaviorState.Ready && __instance.Formation.QuerySystem.ClosestEnemyFormation != null && (__instance.Formation.QuerySystem.IsUnderRangedAttack || __instance.Formation.QuerySystem.AveragePosition.DistanceSquared(____currentOrder.GetPosition(__instance.Formation)) < 25f + (____isInShieldWallDistance ? 75f : 0f));
            if (flag == ____isInShieldWallDistance)
            {
                return false;
            }
            ____isInShieldWallDistance = flag;
            if (____isInShieldWallDistance && __instance.Formation.QuerySystem.HasShield)
            {
                if (__instance.Formation.ArrangementOrder != ArrangementOrder.ArrangementOrderLine)
                {
                    __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                }
            }
            else if (__instance.Formation.ArrangementOrder == ArrangementOrder.ArrangementOrderLine)
            {
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(LadderQueueManager))]
    class OverrideLadderQueueManager
    {

        [HarmonyPostfix]
        [HarmonyPatch("Initialize")]
        static void PostfixInitialize(ref BattleSideEnum managedSide, Vec3 managedDirection, ref float queueBeginDistance, ref int ____maxUserCount, ref float ____agentSpacing, ref float ____queueBeginDistance, ref float ____queueRowSize, ref float ____costPerRow, ref float ____baseCost)
        {
            if (queueBeginDistance != 3f && ____maxUserCount > 1)
            {
                ____agentSpacing = 1.1f;
                ____queueBeginDistance = 7f;
                ____queueRowSize = 1.1f;
                ____maxUserCount = 16;
            }
            else
            {
                ____maxUserCount = 0;
            }
            //else if(queueBeginDistance == 3f)
            //{
            //    ____agentSpacing = 5f;
            //    ____queueBeginDistance = 0.2f;
            //    ____queueRowSize = 5f;
            //    ____maxUserCount = 10;
            //}

        }
    }

    [HarmonyPatch(typeof(SiegeTower))]
    class OverrideSiegeTower
    {

        //[HarmonyPostfix]
        //[HarmonyPatch("OnInit")]
        //static void PostfixOnInit(ref SiegeTower __instance, ref GameEntity ____gameEntity, ref GameEntity ____cleanState, ref List<LadderQueueManager> ____queueManagers, ref int ___DynamicNavmeshIdStart)
        //{
        //    __instance.ForcedUse = true;
        //    List<GameEntity> list2 = ____cleanState.CollectChildrenEntitiesWithTag("ladder");
        //    if (list2.Count == 3)
        //    {
        //        ____queueManagers.Clear();
        //        LadderQueueManager ladderQueueManager0 = list2.ElementAt(0).GetScriptComponents<LadderQueueManager>().FirstOrDefault();
        //        LadderQueueManager ladderQueueManager1 = list2.ElementAt(1).GetScriptComponents<LadderQueueManager>().FirstOrDefault();
        //        LadderQueueManager ladderQueueManager2 = list2.ElementAt(2).GetScriptComponents<LadderQueueManager>().FirstOrDefault();
        //        if (ladderQueueManager0 != null)
        //        {
        //            MatrixFrame identity = MatrixFrame.Identity;
        //            identity.rotation.RotateAboutSide((float)Math.PI / 2f);
        //            identity.rotation.RotateAboutForward((float)Math.PI / 8f);

        //            ladderQueueManager0.Initialize(list2.ElementAt(0).GetScriptComponents<LadderQueueManager>().FirstOrDefault().ManagedNavigationFaceId, identity, new Vec3(0f, -1f), BattleSideEnum.Attacker, 15, (float)Math.PI / 4f, 7f, 1.1f, 30f, 50f, blockUsage: false, 1.1f, 4f, 5f);
        //            ____queueManagers.Add(ladderQueueManager0);
        //        }
        //        if (ladderQueueManager1 != null)
        //        {
        //            MatrixFrame identity = MatrixFrame.Identity;
        //            identity.rotation.RotateAboutSide((float)Math.PI / 2f);
        //            identity.rotation.RotateAboutForward((float)Math.PI / 8f);

        //            ladderQueueManager1.Initialize(list2.ElementAt(1).GetScriptComponents<LadderQueueManager>().FirstOrDefault().ManagedNavigationFaceId, identity, new Vec3(0f, -1f), BattleSideEnum.Attacker, 15, (float)Math.PI / 4f, 7f, 1.1f, 30f, 50f, blockUsage: false, 1.1f, 4f, 5f);
        //            ____queueManagers.Add(ladderQueueManager1);
        //        }
        //        if (ladderQueueManager2 != null)
        //        {
        //            MatrixFrame identity = MatrixFrame.Identity;
        //            identity.rotation.RotateAboutSide((float)Math.PI / 2f);
        //            identity.rotation.RotateAboutForward((float)Math.PI / 8f);

        //            ladderQueueManager2.Initialize(list2.ElementAt(2).GetScriptComponents<LadderQueueManager>().FirstOrDefault().ManagedNavigationFaceId, identity, new Vec3(0f, -1f), BattleSideEnum.Attacker, 15, (float)Math.PI / 4f, 7f, 1.1f, 2f, 1f, blockUsage: false, 1.1f, 0f, 5f);
        //            ____queueManagers.Add(ladderQueueManager2);
        //        }
        //        foreach (LadderQueueManager queueManager in ____queueManagers)
        //        {
        //            ____cleanState.Scene.SetAbilityOfFacesWithId(queueManager.ManagedNavigationFaceId, isEnabled: false);
        //            queueManager.IsDeactivated = true;
        //        }
        //    }
        //    else if (list2.Count == 0)
        //    {
        //        ____queueManagers.Clear();
        //        LadderQueueManager ladderQueueManager2 = ____cleanState.GetScriptComponents<LadderQueueManager>().FirstOrDefault();
        //        if (ladderQueueManager2 != null)
        //        {
        //            MatrixFrame identity2 = MatrixFrame.Identity;
        //            identity2.origin.y += 4f;
        //            identity2.rotation.RotateAboutSide(-(float)Math.PI / 2f);
        //            identity2.rotation.RotateAboutUp((float)Math.PI);
        //            ladderQueueManager2.Initialize(___DynamicNavmeshIdStart + 2, identity2, new Vec3(0f, -1f), BattleSideEnum.Attacker, 16, (float)Math.PI / 4f, 7f, 1.1f, 2f, 1f, blockUsage: false, 1.1f, 0f, 5f);
        //            ____queueManagers.Add(ladderQueueManager2);
        //        }
        //        foreach (LadderQueueManager queueManager in ____queueManagers)
        //        {
        //            ____cleanState.Scene.SetAbilityOfFacesWithId(queueManager.ManagedNavigationFaceId, isEnabled: false);
        //            queueManager.IsDeactivated = true;
        //        }
        //    }
        //}

        [HarmonyPostfix]
        [HarmonyPatch("OnDeploymentStateChanged")]
        static void PostfixDeploymentStateChanged(ref SiegeTower __instance, ref List<SiegeLadder> ____sameSideSiegeLadders, ref GameEntity ____cleanState, ref List<LadderQueueManager> ____queueManagers)
        {
            if (XmlConfig.dict["Global.SiegeTowersEnabled"] == 0)
            {
                __instance.Disable();
                ____cleanState.SetVisibilityExcludeParents(false);
                if (____sameSideSiegeLadders != null)
                {
                    foreach (SiegeLadder sameSideSiegeLadder in ____sameSideSiegeLadders)
                    {
                        sameSideSiegeLadder.GameEntity.SetVisibilityExcludeParents(true);
                    }
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnDestroyed")]
        static void PostfixOnDestroyed(ref List<SiegeLadder> ____sameSideSiegeLadders)
        {
            //if(____sameSideSiegeLadders != null)
            //{
            //    foreach (SiegeLadder sameSideSiegeLadder in ____sameSideSiegeLadders)
            //    {
            //        sameSideSiegeLadder.GameEntity.SetVisibilityExcludeParents(true);
            //    }
            //}
        }
    }

    [HarmonyPatch(typeof(BehaviorCharge))]
    class OverrideBehaviorCharge
    {
        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };
        public static Dictionary<Formation, float> timeToMoveStorage = new Dictionary<Formation, float> { };

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static bool PrefixCalculateCurrentOrder(ref BehaviorCharge __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && (__instance.Formation.QuerySystem.IsInfantryFormation || __instance.Formation.QuerySystem.IsRangedFormation) && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false);

                if (Mission.Current.IsFieldBattle && __instance.Formation.QuerySystem.IsInfantryFormation && !Utilities.FormationFightingInMelee(__instance.Formation, 0.5f))
                {
                    Formation enemyCav = Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);

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

                    if ((enemyCav != null) && (cavDist <= signDist) && (enemyCav.CountOfUnits > __instance.Formation.CountOfUnits / 10) && (signDist > 35f || significantEnemy == enemyCav))
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
                            //    if (Utilities.CheckIfCanBrace(agent))
                            //    {
                            //        agent.SetFiringOrder(1);
                            //    }
                            //    else
                            //    {
                            //        agent.SetFiringOrder(0);
                            //    }
                            //});
                            __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                            return false;
                        }
                        positionsStorage.Remove(__instance.Formation);
                    }
                    else if (significantEnemy != null && signDist < 50f && Utilities.FormationActiveSkirmishersRatio(__instance.Formation, 0.30f))
                    {
                        WorldPosition positionNew = __instance.Formation.QuerySystem.MedianPosition;
                        positionNew.SetVec2(positionNew.AsVec2 - __instance.Formation.Direction * 7f);

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
                        __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                        return false;
                        //__instance.Formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent) {
                        //    agent.SetMaximumSpeedLimit(0.1f, true);
                        //});
                    }
                    positionsStorage.Remove(__instance.Formation);
                }

                if (significantEnemy != null)
                {
                    __instance.Formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
                    ____currentOrder = MovementOrder.MovementOrderChargeToTarget(significantEnemy);
                    if (__instance.Formation.TargetFormation != null && __instance.Formation.TargetFormation.ArrangementOrder == ArrangementOrder.ArrangementOrderShieldWall)
                    {
                        __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderShieldWall;
                    }
                    return false;
                }
            }

            if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                __instance.Formation.TargetFormation = __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation;

            }
            ____currentOrder = MovementOrder.MovementOrderCharge;
            return false;
        }

    }

    [HarmonyPatch(typeof(BehaviorTacticalCharge))]
    class OverrideBehaviorTacticalCharge
    {
        private enum ChargeState
        {
            Undetermined,
            Charging,
            ChargingPast,
            Reforming,
            Bracing
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static bool CalculateCurrentOrderPrefix(ref BehaviorTacticalCharge __instance, ref Vec2 ____initialChargeDirection, ref FormationQuerySystem ____lastTarget,
            ref ChargeState ____chargeState, ref Timer ____chargingPastTimer, ref Timer ____reformTimer, ref MovementOrder ____currentOrder, ref Vec2 ____bracePosition,
            ref float ____desiredChargeStopDistance, ref FacingOrder ___CurrentFacingOrder, ref WorldPosition ____lastReformDestination)
        {

            if (__instance.Formation.QuerySystem.ClosestEnemyFormation == null)
            {
                ____currentOrder = MovementOrder.MovementOrderCharge;
                return false;
            }

            //
            ____desiredChargeStopDistance = 120f;
            ChargeState result = ____chargeState;
            if (__instance.Formation.QuerySystem.ClosestEnemyFormation == null)
            {
                result = ChargeState.Undetermined;
            }
            else
            {
                switch (____chargeState)
                {
                    case ChargeState.Undetermined:
                        if (__instance.Formation.QuerySystem.ClosestEnemyFormation != null && ((!__instance.Formation.QuerySystem.IsCavalryFormation && !__instance.Formation.QuerySystem.IsRangedCavalryFormation) || __instance.Formation.QuerySystem.AveragePosition.Distance(__instance.Formation.QuerySystem.ClosestEnemyFormation.MedianPosition.AsVec2) / __instance.Formation.QuerySystem.MovementSpeedMaximum <= 5f))
                        {
                            result = ChargeState.Charging;
                        }
                        break;
                    case ChargeState.Charging:
                        if (!__instance.Formation.QuerySystem.IsCavalryFormation && !__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            if (!__instance.Formation.QuerySystem.IsInfantryFormation || !__instance.Formation.QuerySystem.ClosestEnemyFormation.IsCavalryFormation)
                            {
                                result = ChargeState.Charging;
                                break;
                            }
                            Vec2 vec2 = __instance.Formation.QuerySystem.AveragePosition - __instance.Formation.QuerySystem.ClosestEnemyFormation.AveragePosition;
                            float num3 = vec2.Normalize();
                            Vec2 currentVelocity2 = __instance.Formation.QuerySystem.ClosestEnemyFormation.CurrentVelocity;
                            float num4 = currentVelocity2.Normalize();
                            if (num3 / num4 <= 6f && vec2.DotProduct(currentVelocity2) > 0.5f)
                            {
                                ____chargeState = ChargeState.Bracing;
                            }
                        }
                        else if (____initialChargeDirection.DotProduct(__instance.Formation.QuerySystem.ClosestEnemyFormation.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.AveragePosition) <= 1f)
                        {
                            result = ChargeState.ChargingPast;
                        }
                        break;
                    case ChargeState.ChargingPast:
                        if (____chargingPastTimer.Check(Mission.Current.CurrentTime))
                        {
                            result = ChargeState.Reforming;
                        }
                        break;
                    case ChargeState.Reforming:
                        if (____reformTimer.Check(Mission.Current.CurrentTime))
                        {
                            result = ChargeState.Charging;
                        }
                        break;
                    case ChargeState.Bracing:
                        {
                            bool flag = false;
                            if (__instance.Formation.QuerySystem.IsInfantryFormation && __instance.Formation.QuerySystem.ClosestEnemyFormation.IsCavalryFormation)
                            {
                                Vec2 vec = __instance.Formation.QuerySystem.AveragePosition - __instance.Formation.QuerySystem.ClosestEnemyFormation.AveragePosition;
                                float num = vec.Normalize();
                                Vec2 currentVelocity = __instance.Formation.QuerySystem.ClosestEnemyFormation.CurrentVelocity;
                                float num2 = currentVelocity.Normalize();
                                if (num / num2 <= 8f && vec.DotProduct(currentVelocity) > 0.33f)
                                {
                                    flag = true;
                                }
                            }
                            if (!flag)
                            {
                                ____bracePosition = Vec2.Invalid;
                                ____chargeState = ChargeState.Charging;
                            }
                            break;
                        }
                }
            }
            ChargeState chargeState = result;

            //
            //MethodInfo method = typeof(BehaviorTacticalCharge).GetMethod("CheckAndChangeState", BindingFlags.NonPublic | BindingFlags.Instance);
            //method.DeclaringType.GetMethod("CheckAndChangeState");
            //ChargeState chargeState = (ChargeState)method.Invoke(__instance, new object[] { });

            if (chargeState != ____chargeState)
            {
                ____chargeState = chargeState;
                switch (____chargeState)
                {
                    case ChargeState.Undetermined:
                        ____currentOrder = MovementOrder.MovementOrderCharge;
                        break;
                    case ChargeState.Charging:
                        ____lastTarget = __instance.Formation.QuerySystem.ClosestEnemyFormation;
                        if (__instance.Formation.QuerySystem.IsCavalryFormation || __instance.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            ____initialChargeDirection = ____lastTarget.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.AveragePosition;
                            float value = ____initialChargeDirection.Normalize();
                            ____desiredChargeStopDistance = 120f;
                        }
                        break;
                    case ChargeState.ChargingPast:
                        ____chargingPastTimer = new Timer(Mission.Current.CurrentTime, 14f);
                        break;
                    case ChargeState.Reforming:
                        ____reformTimer = new Timer(Mission.Current.CurrentTime, 10f);
                        break;
                    case ChargeState.Bracing:
                        {
                            Vec2 vec = (__instance.Formation.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - __instance.Formation.QuerySystem.AveragePosition).Normalized();
                            ____bracePosition = __instance.Formation.QuerySystem.AveragePosition + vec * 5f;
                            break;
                        }
                }
            }

            switch (____chargeState)
            {
                case ChargeState.Undetermined:
                    if (__instance.Formation.QuerySystem.ClosestEnemyFormation != null && (__instance.Formation.QuerySystem.IsCavalryFormation || __instance.Formation.QuerySystem.IsRangedCavalryFormation))
                    {
                        ____currentOrder = MovementOrder.MovementOrderMove(__instance.Formation.QuerySystem.ClosestEnemyFormation.MedianPosition);
                    }
                    else
                    {
                        ____currentOrder = MovementOrder.MovementOrderCharge;
                    }
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                    break;
                case ChargeState.Charging:
                    {
                        if (!__instance.Formation.QuerySystem.IsCavalryFormation && !__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            WorldPosition medianPosition2 = __instance.Formation.QuerySystem.ClosestEnemyFormation.MedianPosition;
                            ____currentOrder = MovementOrder.MovementOrderMove(medianPosition2);
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                            break;
                        }
                        Vec2 vec4 = (____lastTarget.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.AveragePosition).Normalized();
                        WorldPosition medianPosition3 = ____lastTarget.MedianPosition;
                        Vec2 vec5 = medianPosition3.AsVec2 + vec4 * ____desiredChargeStopDistance;
                        medianPosition3.SetVec2(vec5);
                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition3);
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec4);
                        break;
                    }
                case ChargeState.ChargingPast:
                    {
                        Vec2 vec2 = __instance.Formation.QuerySystem.AveragePosition - ____lastTarget.MedianPosition.AsVec2;
                        if (!(vec2.Normalize() > 20f))
                        {
                            _ = ____initialChargeDirection;
                        }
                        ____lastReformDestination = ____lastTarget.MedianPosition;
                        Vec2 vec3 = ____lastTarget.MedianPosition.AsVec2 + vec2 * ____desiredChargeStopDistance;
                        ____lastReformDestination.SetVec2(vec3);
                        ____currentOrder = MovementOrder.MovementOrderMove(____lastReformDestination);
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec2);
                        break;
                    }
                case ChargeState.Reforming:
                    ____currentOrder = MovementOrder.MovementOrderMove(____lastReformDestination);
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                    break;
                case ChargeState.Bracing:
                    {
                        WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                        medianPosition.SetVec2(____bracePosition);
                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                        break;
                    }
            }
            return false;
        }

    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    class OverrideFormationMovementComponent
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
        internal enum MovementStateEnum
        {
            Charge,
            Hold,
            Retreat,
            StandGround
        }

        private static readonly MethodInfo IsUnitDetachedForDebug =
            typeof(Formation).GetMethod("IsUnitDetachedForDebug", BindingFlags.Instance | BindingFlags.NonPublic);

        [HarmonyPrefix]
        [HarmonyPatch("GetFormationFrame")]
        static bool PrefixGetFormationFrame(ref bool __result, ref Agent ___Agent, ref HumanAIComponent __instance, ref WorldPosition formationPosition, ref Vec2 formationDirection, ref float speedLimit, ref bool isSettingDestinationSpeed, ref bool limitIsMultiplier)
        {
            if (___Agent != null)
            {
                var formation = ___Agent.Formation;
                if (!___Agent.IsMount && formation != null && (formation.QuerySystem.IsCavalryFormation || formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && !(bool)IsUnitDetachedForDebug.Invoke(formation, new object[] { ___Agent }))
                {
                    if (formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
                    {
                        if (___Agent != null && formation != null)
                        {
                            isSettingDestinationSpeed = false;
                            formationPosition = formation.GetOrderPositionOfUnit(___Agent);
                            formationDirection = formation.GetDirectionOfUnit(___Agent);
                            limitIsMultiplier = true;
                            speedLimit = __instance != null && HumanAIComponent.FormationSpeedAdjustmentEnabled ? __instance.GetDesiredSpeedInFormation(false) : -1f;
                            __result = true;
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(MovementOrder))]
    class OverrideMovementOrder
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

        [HarmonyPrefix]
        [HarmonyPatch("SetChargeBehaviorValues")]
        static bool PrefixSetChargeBehaviorValues(Agent unit)
        {
            if (unit != null && unit.Formation != null)
            {
                if (unit.Formation.QuerySystem.IsRangedCavalryFormation)
                {
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0f, 7f, 4f, 20f, 6f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 1.5f, 4f, 20f, 1f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 2f, 7f, 4f, 20f, 5f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 2f, 25f, 5f, 30f, 5f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 8f, 15f, 10f, 30f, 10f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
                    return false;
                }
                if (unit.Formation.QuerySystem.IsCavalryFormation)
                {


                    if (unit.HasMount)
                    {
                        if (Utilities.GetHarnessTier(unit) > 3)
                        {
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 7f, 4f, 20f, 1f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
                        }
                        else
                        {
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 1f, 2f, 4f, 20f, 1f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
                        }
                    }
                    else
                    {
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 7f, 4f, 20f, 1f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
                    }
                    //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 1f, 2f, 4f, 20f, 1f);
                    //unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 25f, 5f, 30f, 5f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0f, 7f, 4f, 20f, 6f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 2f, 7f, 4f, 20f, 5f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0f, 10f, 3f, 20f, 6f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
                    return false;
                }
                if (unit.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
                {
                    if (unit.Formation.QuerySystem.IsInfantryFormation)
                    {
                        //podmienky: twohandedpolearm v rukach
                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0f, 40f, 4f, 50f, 6f);
                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 7f, 1f, 10f, 0.01f);
                        //if (Utilities.CheckIfTwoHandedPolearmInfantry(unit))
                        //{
                        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 3.5f, 5f, 20f, 6f);
                        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 3.5f, 4f, 20f, 0.01f);
                        //}
                        //else {
                        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
                        //}
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0.8f, 20f, 20f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 40f, 4f, 60f, 0f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 5f, 7f, 10f, 8, 20f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 1f, 12f, 1f, 30f, 0f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
                        return false;
                    }
                    if (unit.Formation.QuerySystem.IsRangedFormation)
                    {
                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0f, 40f, 4f, 50f, 6f);
                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 7f, 1f, 10f, 0.01f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 4f, 10f, 0.01f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 2f, 0f, 8f, 20f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 40f, 4f, 60f, 0f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 2f, 15f, 6.5f, 30f, 5.5f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 1f, 12f, 1f, 30f, 0f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
                        return false;
                    }
                }
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("SetFollowBehaviorValues")]
        static bool PrefixSetFollowBehaviorValues(Agent unit)
        {
            if (unit.Formation != null)
            {
                if (unit.Formation.QuerySystem.IsRangedCavalryFormation)
                {
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 5f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 6f, 7f, 4f, 20f, 0f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 0f, 7f, 0f, 30f, 0f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0.065f, 15f, 0.065f, 30f, 0.065f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
                    return false;
                }
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("SetDefaultMoveBehaviorValues")]
        static bool PrefixSetDefaultMoveBehaviorValues(Agent unit)
        {
            if (unit.Formation != null)
            {
                if (unit.Formation.QuerySystem.IsRangedCavalryFormation)
                {
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 5f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 7f, 5f, 20f, 0.01f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0.02f, 7f, 0.04f, 20f, 0.03f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 10f, 7f, 5f, 30f, 0.05f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0.065f, 15f, 0.065f, 30f, 0.065f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
                    return false;
                }
            }
            if (!Mission.Current.IsFieldBattle)
            {
                unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 6f);
                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 4f, 3f, 20f, 0.01f);
                unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0.02f, 7f, 0.04f, 20f, 0.03f);
                unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 10f, 7f, 5f, 30f, 0.05f);
                unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0.02f, 15f, 0.065f, 30f, 0.055f);
                unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
                unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetSubstituteOrder")]
        static bool PrefixGetSubstituteOrder(MovementOrder __instance, ref MovementOrder __result, Formation formation)
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
    class OverrideAgent
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetTargetAgent")]
        static bool PrefixGetTargetAgent(ref Agent __instance, ref Agent __result)
        {
            List<Formation> formations;
            if (__instance != null)
            {
                Formation formation = __instance.Formation;
                if (formation != null)
                {
                    if ((formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && (formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget))
                    {
                        formations = Utilities.FindSignificantFormations(formation);
                        if (formations.Count > 0)
                        {
                            __result = Utilities.NearestAgentFromMultipleFormations(__instance.Position.AsVec2, formations);
                            return false;
                        }
                        //Formation enemyFormation = formation.MovementOrder.TargetFormation;
                        //if(enemyFormation != null)
                        //{
                        //    __result = Utilities.NearestAgentFromFormation(__instance.Position.AsVec2, enemyFormation);
                        //    return false;
                        //}
                    }
                }
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("SetFiringOrder")]
        static bool PrefixSetFiringOrder(ref Agent __instance, ref int order)
        {
            if (__instance.Formation != null && __instance.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
            {
                if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false);

                    if (__instance.Formation.QuerySystem.IsInfantryFormation && !Utilities.FormationFightingInMelee(__instance.Formation, 0.5f))
                    {
                        Formation enemyCav = Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);

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
                                if (Utilities.CheckIfCanBrace(__instance))
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
    class OverrideUpdateFormationOrders
    {
        [HarmonyPrefix]
        [HarmonyPatch("UpdateFormationOrders")]
        static bool PrefixUpdateFormationOrders(ref Agent __instance)
        {
            if (__instance.Formation != null && __instance.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
            {
                __instance.EnforceShieldUsage(ArrangementOrder.GetShieldDirectionOfUnit(__instance.Formation, __instance, __instance.Formation.ArrangementOrder.OrderEnum));
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Formation))]
    class OverrideFormation
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetOrderPositionOfUnit")]
        static bool PrefixGetOrderPositionOfUnit(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
        {
            //if (__instance.MovementOrder.OrderType == OrderType.ChargeWithTarget && __instance.QuerySystem.IsInfantryFormation && !___detachedUnits.Contains(unit))
            if (unit != null && (__instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge) && (__instance.QuerySystem.IsInfantryFormation || __instance.QuerySystem.IsRangedFormation) && !____detachedUnits.Contains(unit))
            {
                var targetAgent = unit.GetTargetAgent();
                Mission mission = Mission.Current;
                if (targetAgent != null)
                {
                    Vec2 lookDirection = unit.LookDirection.AsVec2;
                    Vec2 unitPosition = unit.GetWorldPosition().AsVec2;

                    float distance = unitPosition.Distance(targetAgent.GetWorldPosition().AsVec2);
                    //if (targetAgent.HasMount) {
                    //if (distance > 100f){
                    //    __result = targetAgent.GetWorldPosition();
                    //    return false;
                    //}
                    //}else {
                    Vec2 direction = (targetAgent.GetWorldPosition().AsVec2 - unitPosition).Normalized();

                    if (distance > 30f)
                    {

                        __result = targetAgent.GetWorldPosition();
                        return false;
                    }
                    //}

                    IEnumerable<Agent> agents = mission.GetNearbyAllyAgents(unitPosition + direction * 0.8f, 1f, unit.Team);

                    if (agents.Count() > 3 && !unit.IsDoingPassiveAttack)
                    {
                        unit.LookDirection = direction.ToVec3();
                        //if (!unit.IsRetreating() && !unit.IsRunningAway)
                        //{
                        //    unit.SetLookAgent(targetAgent);
                        //}
                        if (MBRandom.RandomInt(75) == 0)
                        {
                            //if (targetAgent != null)
                            //{
                            //    __result = targetAgent.GetWorldPosition();
                            //    return false;
                            //}
                            //else
                            //{
                            return true;
                            //}
                        }
                        else
                        {
                            //float distancefr = unitPosition.Distance(agents.ElementAt(0).Position.AsVec2);
                            //float slowdown = Math.Min(distancefr/2f, 1f);
                            //if(slowdown < 0.6f)
                            //{
                            if (unit != null)
                            {
                                IEnumerable<Agent> agentsLeft = mission.GetNearbyAllyAgents(unitPosition + direction.LeftVec() * 0.8f, 1f, unit.Team);
                                IEnumerable<Agent> agentsRight = mission.GetNearbyAllyAgents(unitPosition + direction.RightVec() * 0.8f, 1f, unit.Team);
                                if (agentsLeft.Count() > 3 && agentsRight.Count() > 3)
                                {
                                    if (MBRandom.RandomInt(75) == 0)
                                    {
                                        if (MBRandom.RandomInt(2) == 0)
                                        {
                                            WorldPosition leftPosition = unit.GetWorldPosition();
                                            leftPosition.SetVec2(unitPosition + direction.LeftVec());
                                            __result = leftPosition;
                                            return false;
                                        }
                                        else
                                        {
                                            WorldPosition rightPosition = unit.GetWorldPosition();
                                            rightPosition.SetVec2(unitPosition + direction.RightVec());
                                            __result = rightPosition;
                                            return false;
                                        }
                                    }
                                }
                                else if (agentsLeft.Count() <= 3 && agentsRight.Count() <= 3)
                                {
                                    if (MBRandom.RandomInt(2) == 0)
                                    {
                                        WorldPosition leftPosition = unit.GetWorldPosition();
                                        leftPosition.SetVec2(unitPosition + direction.LeftVec());
                                        __result = leftPosition;
                                        return false;
                                    }
                                    else
                                    {
                                        WorldPosition rightPosition = unit.GetWorldPosition();
                                        rightPosition.SetVec2(unitPosition + direction.RightVec());
                                        __result = rightPosition;
                                        return false;
                                    }
                                }
                                else if (agentsLeft.Count() <= 3)
                                {
                                    WorldPosition leftPosition = unit.GetWorldPosition();
                                    leftPosition.SetVec2(unitPosition + direction.LeftVec());
                                    __result = leftPosition;
                                    return false;
                                }
                                else if (agentsRight.Count() <= 3)
                                {
                                    WorldPosition rightPosition = unit.GetWorldPosition();
                                    rightPosition.SetVec2(unitPosition + direction.RightVec());
                                    __result = rightPosition;
                                    return false;
                                }
                                if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
                                {
                                    int unitPower = (int)Math.Floor(unit.Character.GetPower() * 100);
                                    int randInt = MBRandom.RandomInt(unitPower + 10);
                                    int hasShieldBonus = 0;
                                    if (unit.HasShieldCached)
                                    {
                                        hasShieldBonus = 30;
                                    }
                                    if (randInt < (unitPower / 2 + hasShieldBonus))
                                    {
                                        WorldPosition closeAllyPosition = unit.GetWorldPosition();
                                        IEnumerable<Agent> nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 4f, unit.Team);
                                        if (nearbyAllyAgents.Count() > 0)
                                        {
                                            List<Agent> allyAgentList = nearbyAllyAgents.ToList();
                                            if (allyAgentList.Count() == 1)
                                            {
                                                __result = allyAgentList.ElementAt(0).GetWorldPosition();
                                                return false;
                                            }
                                            allyAgentList.Remove(unit);
                                            float dist = 10000f;
                                            foreach (Agent agent in allyAgentList)
                                            {
                                                float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                                                if (dist > newDist)
                                                {
                                                    __result = agent.GetWorldPosition();
                                                    dist = newDist;
                                                }
                                            }
                                            return false;
                                        }
                                        else
                                        {
                                            __result = mission.GetClosestAllyAgent(unit.Team, closeAllyPosition.GetGroundVec3(), 4f).GetWorldPosition();
                                            return false;
                                        }
                                    }
                                    else
                                    {
                                        if (MBRandom.RandomInt(unitPower / 4) == 0)
                                        {
                                            return true;
                                        }
                                        else
                                        {
                                            __result = unit.GetWorldPosition();
                                            return false;
                                        }
                                    }
                                }
                                else
                                {
                                    __result = unit.GetWorldPosition();
                                    return false;
                                }
                            }
                            else
                            {
                                return true;
                            }
                            //}
                        }
                    }
                    if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
                    {
                        IEnumerable<Agent> enemyAgents10f = mission.GetNearbyEnemyAgents(unitPosition + direction * 10f, 5f, unit.Team);
                        IEnumerable<Agent> enemyAgents0f = mission.GetNearbyEnemyAgents(unitPosition, 5f, unit.Team);
                        IEnumerable<Agent> enemyAgentsImmidiate = mission.GetNearbyEnemyAgents(unitPosition, 4f, unit.Team);
                        //if (targetAgent.HasMount)
                        //{

                        //    IEnumerable<Agent> enemyAgents20f = mission.GetNearbyEnemyAgents(unitPosition + direction * 20f, 5f, unit.Team);
                        //    IEnumerable<Agent> enemyAgents30f = mission.GetNearbyEnemyAgents(unitPosition + direction * 30f, 5f, unit.Team);
                        //    IEnumerable<Agent> enemyAgents40f = mission.GetNearbyEnemyAgents(unitPosition + direction * 40f, 5f, unit.Team);
                        //    IEnumerable<Agent> enemyAgents50f = mission.GetNearbyEnemyAgents(unitPosition + direction * 50f, 5f, unit.Team);
                        //    IEnumerable<Agent> enemyAgents60f = mission.GetNearbyEnemyAgents(unitPosition + direction * 60f, 5f, unit.Team);
                        //    IEnumerable<Agent> enemyAgents70f = mission.GetNearbyEnemyAgents(unitPosition + direction * 70f, 5f, unit.Team);
                        //    IEnumerable<Agent> enemyAgents80f = mission.GetNearbyEnemyAgents(unitPosition + direction * 80f, 5f, unit.Team);
                        //    IEnumerable<Agent> enemyAgents90f = mission.GetNearbyEnemyAgents(unitPosition + direction * 90f, 5f, unit.Team);

                        //    if (enemyAgents0f.Count() > 3 || enemyAgents10f.Count() > 3 || enemyAgents20f.Count() > 3 || enemyAgents30f.Count() > 3 || enemyAgents40f.Count() > 3 || enemyAgents50f.Count() > 3 || enemyAgents60f.Count() > 3
                        //        || enemyAgents70f.Count() > 3 || enemyAgents80f.Count() > 3 || enemyAgents90f.Count() > 3)
                        //    {
                        //        //unit.SetFiringOrder(1);
                        //        int unitPower = (int)Math.Floor(unit.Character.GetPower() * 100);
                        //        int randInt = MBRandom.RandomInt(unitPower + 10);
                        //        int hasShieldBonus = 0;
                        //        if (unit.HasShieldCached)
                        //        {
                        //            hasShieldBonus = 50;
                        //        }
                        //        if (randInt < 0)
                        //        {
                        //            __result = unit.GetWorldPosition();
                        //            return false;
                        //        }
                        //        if (enemyAgentsImmidiate.Count() > 7)
                        //        {
                        //            WorldPosition backPosition = unit.GetWorldPosition();
                        //            backPosition.SetVec2(unitPosition - lookDirection * 1.5f);
                        //            __result = backPosition;
                        //            return false;
                        //        }
                        //        else if (randInt < unitPower)
                        //        {
                        //            if (randInt < (unitPower / 2 + hasShieldBonus))
                        //            {

                        //                if (enemyAgents0f.Count() > 5)
                        //                {
                        //                    WorldPosition backPosition = unit.GetWorldPosition();
                        //                    backPosition.SetVec2(unitPosition - lookDirection * 1.5f);
                        //                    __result = backPosition;
                        //                    return false;
                        //                }
                        //                WorldPosition closeAllyPosition = unit.GetWorldPosition();
                        //                IEnumerable<Agent> nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition , 4f, unit.Team);
                        //                if(nearbyAllyAgents.Count() > 0)
                        //                {
                        //                    List<Agent> allyAgentList = nearbyAllyAgents.ToList();
                        //                    if (allyAgentList.Count() == 1)
                        //                    {
                        //                        __result = allyAgentList.ElementAt(0).GetWorldPosition();
                        //                        return false;
                        //                    }
                        //                    allyAgentList.Remove(unit);
                        //                    float dist = 10000f;
                        //                    foreach(Agent agent in allyAgentList)
                        //                    {
                        //                        float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                        //                        if (dist > newDist)
                        //                        {
                        //                            __result = agent.GetWorldPosition();
                        //                            dist = newDist;
                        //                        }
                        //                    }
                        //                    return false;
                        //                }
                        //                else
                        //                {
                        //                    __result = mission.GetClosestAllyAgent(unit.Team, closeAllyPosition.GetGroundVec3(), 4f).GetWorldPosition();
                        //                    return false;
                        //                }

                        //            }
                        //            else
                        //            {
                        //                if (MBRandom.RandomInt(unitPower / 4) == 0)
                        //                {
                        //                    __result = unit.GetWorldPosition();
                        //                    return false;
                        //                }
                        //                else
                        //                {
                        //                    WorldPosition backPosition = unit.GetWorldPosition();
                        //                    backPosition.SetVec2(unitPosition - lookDirection * 1.5f);
                        //                    __result = backPosition;
                        //                    return false;
                        //                }
                        //            }

                        //        }
                        //        else if (randInt < unitPower )
                        //        {
                        //            return true;
                        //        }
                        //    }

                        //}

                        if (enemyAgents0f.Count() > 3 || enemyAgents10f.Count() > 3)
                        {
                            unit.LookDirection = direction.ToVec3();
                            //if(!unit.IsRetreating() && !unit.IsRunningAway)
                            //{
                            //    unit.SetLookAgent(targetAgent);
                            //}

                            //unit.SetFiringOrder(1);
                            int unitPower = (int)Math.Floor(unit.Character.GetPower() * 100);
                            int randInt = MBRandom.RandomInt(unitPower + 10);
                            int hasShieldBonus = 0;
                            if (unit.HasShieldCached)
                            {
                                hasShieldBonus = 30;
                            }
                            if (randInt < 0)
                            {
                                __result = unit.GetWorldPosition();
                                return false;
                            }
                            if (enemyAgentsImmidiate.Count() > 7)
                            {
                                WorldPosition backPosition = unit.GetWorldPosition();
                                backPosition.SetVec2(unitPosition - lookDirection * 1.5f);
                                __result = backPosition;
                                return false;
                            }
                            else if (randInt < (unitPower / 2 + hasShieldBonus))
                            {

                                if (randInt < (unitPower / 2 + hasShieldBonus))
                                {

                                    if (enemyAgents0f.Count() > 5)
                                    {
                                        WorldPosition backPosition = unit.GetWorldPosition();
                                        backPosition.SetVec2(unitPosition - lookDirection * 1.5f);
                                        __result = backPosition;
                                        return false;
                                    }
                                    WorldPosition closeAllyPosition = unit.GetWorldPosition();
                                    IEnumerable<Agent> nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 4f, unit.Team);
                                    if (nearbyAllyAgents.Count() > 0)
                                    {
                                        List<Agent> allyAgentList = nearbyAllyAgents.ToList();
                                        if (allyAgentList.Count() == 1)
                                        {
                                            __result = allyAgentList.ElementAt(0).GetWorldPosition();
                                            return false;
                                        }
                                        allyAgentList.Remove(unit);
                                        float dist = 10000f;
                                        foreach (Agent agent in allyAgentList)
                                        {
                                            float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                                            if (dist > newDist)
                                            {
                                                __result = agent.GetWorldPosition();
                                                dist = newDist;
                                            }
                                        }
                                        return false;
                                    }
                                    else
                                    {
                                        __result = mission.GetClosestAllyAgent(unit.Team, closeAllyPosition.GetGroundVec3(), 4f).GetWorldPosition();
                                        return false;
                                    }
                                }
                                else
                                {
                                    if (MBRandom.RandomInt(unitPower / 4) == 0)
                                    {
                                        __result = unit.GetWorldPosition();
                                        return false;
                                    }
                                    else
                                    {
                                        WorldPosition backPosition = unit.GetWorldPosition();
                                        backPosition.SetVec2(unitPosition - lookDirection * 1.5f);
                                        __result = backPosition;
                                        return false;
                                    }
                                }

                            }
                            else if (randInt < unitPower)
                            {
                                return true;
                            }
                        }
                    }
                    else
                    {
                        return true;
                    }
                    //}
                }
                return false;
            }
            return true;
        }

        //[HarmonyPrefix]
        //[HarmonyPatch("GetOrderPositionOfUnitAux")]
        //static bool PrefixGetOrderPositionOfUnitAux(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
        //{
        //    //Mission.Current.IsFieldBattle &&
        //    if (unit != null && (__instance.QuerySystem.IsInfantryFormation) && (__instance.AI != null || __instance.IsAIControlled == false) && __instance.AI.ActiveBehavior != null)
        //    {
        //        //InformationManager.DisplayMessage(new InformationMessage(__instance.AI.ActiveBehavior.GetType().Name + " " + __instance.MovementOrder.OrderType.ToString()));
        //        bool exludedWhenAiControl = !(__instance.IsAIControlled && (__instance.AI.ActiveBehavior.GetType().Name.Contains("Regroup") || __instance.AI.ActiveBehavior.GetType().Name.Contains("Advance")));
        //        bool exludedWhenPlayerControl = !(!__instance.IsAIControlled && (__instance.GetReadonlyMovementOrderReference().OrderType.ToString().Contains("Advance")));

        //        if (exludedWhenAiControl && exludedWhenPlayerControl && !____detachedUnits.Contains(unit))
        //        {
        //            Mission mission = Mission.Current;
        //            if (mission.Mode != MissionMode.Deployment)
        //            {
        //                Vec2 direction = unit.LookDirection.AsVec2;
        //                Vec2 unitPosition = unit.GetWorldPosition().AsVec2;

        //                IEnumerable<Agent> agents = mission.GetNearbyAllyAgents(unitPosition + direction * 0.8f, 1f, unit.Team);

        //                if (agents.Count() > 3 && !unit.IsDoingPassiveAttack)
        //                {
        //                    //if (!unit.IsRetreating() && !unit.IsRunningAway)
        //                    //{
        //                    //    unit.SetLookAgent(targetAgent);
        //                    //}
        //                    if (MBRandom.RandomInt(75) == 0)
        //                    {
        //                        //if (targetAgent != null)
        //                        //{
        //                        //    __result = targetAgent.GetWorldPosition();
        //                        //    return false;
        //                        //}
        //                        //else
        //                        //{
        //                        return true;
        //                        //}
        //                    }
        //                    else
        //                    {
        //                        //float distancefr = unitPosition.Distance(agents.ElementAt(0).Position.AsVec2);
        //                        //float slowdown = Math.Min(distancefr/2f, 1f);
        //                        //if(slowdown < 0.6f)
        //                        //{
        //                        if (unit != null)
        //                        {
        //                            IEnumerable<Agent> agentsLeft = mission.GetNearbyAllyAgents(unitPosition + direction.LeftVec() * 0.8f, 1f, unit.Team);
        //                            IEnumerable<Agent> agentsRight = mission.GetNearbyAllyAgents(unitPosition + direction.RightVec() * 0.8f, 1f, unit.Team);
        //                            if (agentsLeft.Count() > 4 && agentsRight.Count() > 4)
        //                            {
        //                                if (MBRandom.RandomInt(75) == 0)
        //                                {
        //                                    if (MBRandom.RandomInt(2) == 0)
        //                                    {
        //                                        WorldPosition leftPosition = unit.GetWorldPosition();
        //                                        leftPosition.SetVec2(unitPosition + direction.LeftVec());
        //                                        __result = leftPosition;
        //                                        return false;
        //                                    }
        //                                    else
        //                                    {
        //                                        WorldPosition rightPosition = unit.GetWorldPosition();
        //                                        rightPosition.SetVec2(unitPosition + direction.RightVec());
        //                                        __result = rightPosition;
        //                                        return false;
        //                                    }
        //                                }
        //                            }
        //                            else if (agentsLeft.Count() <= 4 && agentsRight.Count() <= 4)
        //                            {
        //                                if (MBRandom.RandomInt(2) == 0)
        //                                {
        //                                    WorldPosition leftPosition = unit.GetWorldPosition();
        //                                    leftPosition.SetVec2(unitPosition + direction.LeftVec());
        //                                    __result = leftPosition;
        //                                    return false;
        //                                }
        //                                else
        //                                {
        //                                    WorldPosition rightPosition = unit.GetWorldPosition();
        //                                    rightPosition.SetVec2(unitPosition + direction.RightVec());
        //                                    __result = rightPosition;
        //                                    return false;
        //                                }
        //                            }
        //                            else if (agentsLeft.Count() <= 4)
        //                            {
        //                                WorldPosition leftPosition = unit.GetWorldPosition();
        //                                leftPosition.SetVec2(unitPosition + direction.LeftVec());
        //                                __result = leftPosition;
        //                                return false;
        //                            }
        //                            else if (agentsRight.Count() <= 4)
        //                            {
        //                                WorldPosition rightPosition = unit.GetWorldPosition();
        //                                rightPosition.SetVec2(unitPosition + direction.RightVec());
        //                                __result = rightPosition;
        //                                return false;
        //                            }
        //                            if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle || Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.Siege)
        //                            {
        //                                int unitPower = (int)Math.Floor(unit.Character.GetPower() * 100);
        //                                int randInt = MBRandom.RandomInt(unitPower + 10);
        //                                int hasShieldBonus = 0;
        //                                if (unit.HasShieldCached)
        //                                {
        //                                    hasShieldBonus = 30;
        //                                }
        //                                if (randInt < (unitPower / 2 + hasShieldBonus))
        //                                {
        //                                    WorldPosition closeAllyPosition = unit.GetWorldPosition();
        //                                    IEnumerable<Agent> nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 4f, unit.Team);
        //                                    if (nearbyAllyAgents.Count() > 0)
        //                                    {
        //                                        List<Agent> allyAgentList = nearbyAllyAgents.ToList();
        //                                        if (allyAgentList.Count() == 1)
        //                                        {
        //                                            __result = allyAgentList.ElementAt(0).GetWorldPosition();
        //                                            return false;
        //                                        }
        //                                        allyAgentList.Remove(unit);
        //                                        float dist = 10000f;
        //                                        foreach (Agent agent in allyAgentList)
        //                                        {
        //                                            float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
        //                                            if (dist > newDist)
        //                                            {
        //                                                __result = agent.GetWorldPosition();
        //                                                dist = newDist;
        //                                            }
        //                                        }
        //                                        return false;
        //                                    }
        //                                    else
        //                                    {
        //                                        __result = mission.GetClosestAllyAgent(unit.Team, closeAllyPosition.GetGroundVec3(), 4f).GetWorldPosition();
        //                                        return false;
        //                                    }
        //                                }
        //                                else
        //                                {
        //                                    if (MBRandom.RandomInt(unitPower / 4) == 0)
        //                                    {
        //                                        return true;
        //                                    }
        //                                    else
        //                                    {
        //                                        __result = unit.GetWorldPosition();
        //                                        return false;
        //                                    }
        //                                }
        //                            }
        //                            else
        //                            {
        //                                __result = unit.GetWorldPosition();
        //                                return false;
        //                            }
        //                        }
        //                        else
        //                        {
        //                            return true;
        //                        }
        //                        //}
        //                    }
        //                }
        //            }
        //        }
        //    }
        //    return true;
        //}
    }

    [HarmonyPatch(typeof(OrderController))]
    class OverrideOrderController
    {
        [HarmonyPostfix]
        [HarmonyPatch("SetOrder")]
        static void PostfixSetOrder(OrderController __instance, OrderType orderType, ref Mission ____mission)
        {
            if (orderType == OrderType.Charge)
                foreach (Formation selectedFormation in __instance.SelectedFormations)
                {
                    if ((selectedFormation.QuerySystem.IsInfantryFormation || selectedFormation.QuerySystem.IsRangedFormation) || ____mission.IsTeleportingAgents)
                    {
                        if (selectedFormation.QuerySystem.ClosestEnemyFormation == null)
                        {
                            selectedFormation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                        }
                        else
                        {
                            selectedFormation.SetMovementOrder(MovementOrder.MovementOrderChargeToTarget(selectedFormation.QuerySystem.ClosestEnemyFormation.Formation));
                        }
                    }
                }
        }
    }

    [HarmonyPatch(typeof(BehaviorRegroup))]
    class OverrideBehaviorRegroup
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        static bool PrefixGetAiWeight(ref BehaviorRegroup __instance, ref float __result)
        {
            if (__instance.Formation != null)
            {
                FormationQuerySystem querySystem = __instance.Formation.QuerySystem;
                if (__instance.Formation.AI.ActiveBehavior == null)
                {
                    __result = 0f;
                    return false;
                }

                //__result =  MBMath.Lerp(0.1f, 1.2f, MBMath.ClampFloat(behaviorCoherence * (querySystem.FormationIntegrityData.DeviationOfPositionsExcludeFarAgents + 1f) / (querySystem.IdealAverageDisplacement + 1f), 0f, 3f) / 3f);
                __result = MBMath.Lerp(0.1f, 1.2f, MBMath.ClampFloat(__instance.BehaviorCoherence * (querySystem.FormationIntegrityData.DeviationOfPositionsExcludeFarAgents + 1f) / (querySystem.IdealAverageDisplacement + 1f), 0f, 3f) / 3f);
                return false;

            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static bool PrefixCalculateCurrentOrder(ref BehaviorRegroup __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false);
                if (significantEnemy != null)
                {
                    __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                    __instance.Formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
                    __instance.Formation.FormOrder = FormOrder.FormOrderWide;
                    __instance.Formation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;

                    WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);

                    Vec2 direction = (significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.AveragePosition).Normalized();
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);

                    return false;
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(BehaviorAdvance))]
        class OverrideBehaviorAdvance
        {

            public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

            [HarmonyPrefix]
            [HarmonyPatch("CalculateCurrentOrder")]
            static bool PrefixCalculateCurrentOrder(ref BehaviorAdvance __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
            {
                if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {

                    Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false);

                    if (__instance.Formation.QuerySystem.IsInfantryFormation && !Utilities.FormationFightingInMelee(__instance.Formation, 0.5f))
                    {
                        Formation enemyCav = Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);

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
                                //    if (Utilities.CheckIfCanBrace(agent))
                                //    {
                                //        agent.SetFiringOrder(1);
                                //    }
                                //    else
                                //    {
                                //        agent.SetFiringOrder(0);
                                //    }
                                //});
                                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                                return false;
                            }
                            positionsStorage.Remove(__instance.Formation);
                            //medianPositionOld = WorldPosition.Invalid;
                        }
                        else if (significantEnemy != null && signDist < 60f && Utilities.FormationActiveSkirmishersRatio(__instance.Formation, 0.35f))
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
                            __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                            return false;
                            //__instance.Formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent) {
                            //    agent.SetMaximumSpeedLimit(0.1f, true);
                            //});
                        }
                        positionsStorage.Remove(__instance.Formation);
                    }

                    if (significantEnemy != null)
                    {
                        Vec2 vec = significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
                        WorldPosition positionNew = __instance.Formation.QuerySystem.MedianPosition;
                        positionNew.SetVec2(positionNew.AsVec2 + vec.Normalized() * 20f);
                        ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec.Normalized());
                        return false;
                    }
                }
                return true;
            }

            //[HarmonyPostfix]
            //[HarmonyPatch("OnBehaviorActivatedAux")]
            //static void PostfixOnBehaviorActivatedAux( ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
            //{
            //    __instance.Formation.FormOrder = FormOrder.FormOrderDeep;
            //}
        }

        [HarmonyPatch(typeof(SiegeLane))]
        class OverrideSiegeTower
        {

            [HarmonyPrefix]
            [HarmonyPatch("GetLaneCapacity")]
            static bool PrefixGetLaneCapacity(ref SiegeLane __instance, ref float __result)
            {
                if (__instance.DefensePoints.Any((ICastleKeyPosition dp) => dp is WallSegment && (dp as WallSegment).IsBreachedWall) || (__instance.HasGate && __instance.DefensePoints.Where((ICastleKeyPosition dp) => dp is CastleGate).All((ICastleKeyPosition cg) => (cg as CastleGate).IsGateOpen)))
                {
                    __result = 60f;
                    return false;
                }
                __result = __instance.PrimarySiegeWeapons.Where((IPrimarySiegeWeapon psw) => !(psw as SiegeWeapon).IsDestroyed).Sum((IPrimarySiegeWeapon psw) => psw.SiegeWeaponPriority);
                if (__result == 6f)
                {
                    __result = 15f;
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(SiegeMissionTroopSpawnHandler))]
        class OverrideAfterStart
        {
            [HarmonyPrefix]
            [HarmonyPatch("AfterStart")]
            static bool PrefixAfterStart(ref MapEvent ____mapEvent, ref MissionAgentSpawnLogic ____missionAgentSpawnLogic)
            {
                FieldInfo field = typeof(MissionAgentSpawnLogic).GetField("_battleSize", BindingFlags.NonPublic | BindingFlags.Instance);
                field.DeclaringType.GetField("_battleSize");
                int battleSize = (int)field.GetValue(____missionAgentSpawnLogic);

                int numberOfInvolvedMen = ____mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Defender);
                int numberOfInvolvedMen2 = ____mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Attacker);
                int defenderInitialSpawn = numberOfInvolvedMen;
                int attackerInitialSpawn = numberOfInvolvedMen2;

                int totalBattleSize = defenderInitialSpawn + attackerInitialSpawn;

                if (totalBattleSize > battleSize)
                {

                    float defenderAdvantage = (float)battleSize / ((float)defenderInitialSpawn * ((battleSize * 2f) / (totalBattleSize)));
                    if (defenderInitialSpawn < (battleSize / 2f))
                    {
                        defenderAdvantage = (float)totalBattleSize / (float)battleSize;
                    }
                    ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Defender, !____mapEvent.IsSiegeAssault);
                    ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Attacker, !____mapEvent.IsSiegeAssault);
                    ____missionAgentSpawnLogic.InitWithSinglePhase(numberOfInvolvedMen, numberOfInvolvedMen2, defenderInitialSpawn, attackerInitialSpawn, spawnDefenders: true, spawnAttackers: true, defenderAdvantage);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(BaseMissionTroopSpawnHandler))]
        class OverrideAfterStartBaseMissionTroopSpawnHandler
        {
            [HarmonyPrefix]
            [HarmonyPatch("AfterStart")]
            static bool PrefixAfterStart(ref MapEvent ____mapEvent, ref MissionAgentSpawnLogic ____missionAgentSpawnLogic)
            {
                FieldInfo field = typeof(MissionAgentSpawnLogic).GetField("_battleSize", BindingFlags.NonPublic | BindingFlags.Instance);
                field.DeclaringType.GetField("_battleSize");
                int battleSize = (int)field.GetValue(____missionAgentSpawnLogic);

                int numberOfInvolvedMen = MBMath.Floor(____mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Defender));
                int numberOfInvolvedMen2 = MBMath.Floor(____mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Attacker));
                int defenderInitialSpawn = numberOfInvolvedMen;
                int attackerInitialSpawn = numberOfInvolvedMen2;

                int totalBattleSize = defenderInitialSpawn + attackerInitialSpawn;

                if (totalBattleSize > battleSize)
                {

                    float defenderAdvantage = (float)battleSize / ((float)defenderInitialSpawn * ((battleSize * 2f) / (totalBattleSize)));
                    if (defenderInitialSpawn < (battleSize / 2f))
                    {
                        defenderAdvantage = (float)totalBattleSize / (float)battleSize;
                    }
                    ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Defender, !____mapEvent.IsSiegeAssault);
                    ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Attacker, !____mapEvent.IsSiegeAssault);
                    ____missionAgentSpawnLogic.InitWithSinglePhase(numberOfInvolvedMen, numberOfInvolvedMen2, defenderInitialSpawn, attackerInitialSpawn, spawnDefenders: true, spawnAttackers: true, defenderAdvantage);
                    return false;
                }
                return true;

            }
        }

        [HarmonyPatch(typeof(MissionAgentSpawnLogic))]
        class OverrideBattleSizeSpawnTick
        {

            private class SpawnPhase
            {
                public int TotalSpawnNumber;

                public int InitialSpawnedNumber;

                public int InitialSpawnNumber;

                public int RemainingSpawnNumber;

                public int NumberActiveTroops;

                public void OnInitialTroopsSpawned()
                {
                    InitialSpawnedNumber = InitialSpawnNumber;
                    InitialSpawnNumber = 0;
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("BattleSizeSpawnTick")]
            static bool PrefixBattleSizeSpawnTick(ref MissionAgentSpawnLogic __instance, ref int ____battleSize, ref List<SpawnPhase>[] ____phases)
            {
                if (!Mission.Current.IsFieldBattle)
                {
                    return true;
                }
                int numberOfTroopsCanBeSpawned = __instance.NumberOfTroopsCanBeSpawned;
                if (__instance.NumberOfRemainingTroops <= 0 || numberOfTroopsCanBeSpawned <= 0)
                {
                    return true;
                }
                float num4 = (float)(____phases[0][0].InitialSpawnedNumber - __instance.NumberOfActiveDefenderTroops) / (float)____phases[0][0].InitialSpawnedNumber;
                float num5 = (float)(____phases[1][0].InitialSpawnedNumber - __instance.NumberOfActiveAttackerTroops) / (float)____phases[1][0].InitialSpawnedNumber;
                if ((float)numberOfTroopsCanBeSpawned >= (float)____battleSize * 0.5f || num4 >= 0.5f || num5 >= 0.5f)
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
}