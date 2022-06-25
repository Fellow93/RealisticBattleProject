using HarmonyLib;
using RealisticBattleAiModule.AiModule.RbmBehaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static RealisticBattleAiModule.Tactics;
using static TaleWorlds.MountAndBlade.ArrangementOrder;
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

                    Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

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

                    Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

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

    //[HarmonyPatch(typeof(BehaviorSkirmish))]
    //class OverrideBehaviorSkirmish
    //{
    //    private enum BehaviorState
    //    {
    //        Approaching,
    //        Shooting,
    //        PullingBack
    //    }

    //    public static Dictionary<Formation, Vec2> approachingRangingStorage = new Dictionary<Formation, Vec2> { };
    //    public static Dictionary<Formation, int> waitCountShootingStorage = new Dictionary<Formation, int> { };
    //    public static Dictionary<Formation, int> waitCountApproachingStorage = new Dictionary<Formation, int> { };

    //    [HarmonyPrefix]
    //    [HarmonyPatch("CalculateCurrentOrder")]
    //    static bool PrefixCalculateCurrentOrder(BehaviorSkirmish __instance, ref FacingOrder ___CurrentFacingOrder, ref BehaviorState ____behaviorState, ref MovementOrder ____currentOrder)
    //    {
    //        if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
    //        {
    //            Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
    //            if (significantEnemy != null)
    //            {
    //                float distance = significantEnemy.QuerySystem.MedianPosition.AsVec2.Distance(__instance.Formation.QuerySystem.MedianPosition.AsVec2);
    //                if (distance > 100f)
    //                {
    //                    ____behaviorState = BehaviorState.Approaching;
    //                }
    //            }
    //        }
    //        return true;
    //    }

    //    [HarmonyPostfix]
    //    [HarmonyPatch("CalculateCurrentOrder")]
    //    static void PostfixCalculateCurrentOrder(BehaviorSkirmish __instance, ref FacingOrder ___CurrentFacingOrder, ref BehaviorState ____behaviorState, ref MovementOrder ____currentOrder)
    //    {
    //        if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
    //        {
    //            Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

    //            int waitCountShooting;
    //            int waitCountApproaching;
    //            Vec2 approachingRangingPos;
    //            if (!waitCountShootingStorage.TryGetValue(__instance.Formation, out waitCountShooting))
    //            {
    //                waitCountShootingStorage[__instance.Formation] = 0;
    //            }
    //            if (!waitCountApproachingStorage.TryGetValue(__instance.Formation, out waitCountApproaching))
    //            {
    //                waitCountApproachingStorage[__instance.Formation] = 0;
    //            }
    //            if (!approachingRangingStorage.TryGetValue(__instance.Formation, out approachingRangingPos))
    //            {
    //                approachingRangingStorage[__instance.Formation] = __instance.Formation.QuerySystem.MedianPosition.AsVec2;
    //            }

    //            if (significantEnemy != null)
    //            {
    //                Vec2 enemyDirection = significantEnemy.QuerySystem.MedianPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2;
    //                float distance = enemyDirection.Normalize();
    //                if(distance > 100f)
    //                {
    //                    ____behaviorState = BehaviorState.Approaching;
    //                }
    //                switch (____behaviorState)
    //                {
    //                    case BehaviorState.Shooting:
    //                        {
    //                            if (waitCountShootingStorage[__instance.Formation] > 45)
    //                            {
    //                                if (distance > 80f)
    //                                {
    //                                    WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
    //                                    medianPosition.SetVec2(medianPosition.AsVec2 + enemyDirection * 5f);
    //                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
    //                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
    //                                    waitCountShootingStorage[__instance.Formation] = 0;
    //                                    break;
    //                                }
    //                                if (distance < 40f && distance > 25f)
    //                                {
    //                                    WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
    //                                    medianPosition.SetVec2(medianPosition.AsVec2 - enemyDirection * 5f);
    //                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
    //                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(__instance.Formation.Direction);
    //                                    //___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
    //                                    waitCountShootingStorage[__instance.Formation] = 0;
    //                                    break;
    //                                }
    //                                if (__instance.Formation.QuerySystem.MakingRangedAttackRatio < 0.3f && distance > 40f)
    //                                {
    //                                    WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
    //                                    medianPosition.SetVec2(medianPosition.AsVec2 + enemyDirection * 5f);
    //                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
    //                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
    //                                    waitCountShootingStorage[__instance.Formation] = 0;
    //                                    break;
    //                                }
    //                            }
    //                            else
    //                            {
    //                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
    //                                waitCountShootingStorage[__instance.Formation] = waitCountShootingStorage[__instance.Formation] + 1;
    //                            }

    //                            break;
    //                        }
    //                    case BehaviorState.Approaching:
    //                        if (waitCountApproachingStorage[__instance.Formation] > 20)
    //                        {
    //                            //if (distance < 200f)
    //                            //{
    //                            WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
    //                            medianPosition.SetVec2(medianPosition.AsVec2 + enemyDirection * 5f);
    //                            approachingRangingStorage[__instance.Formation] = medianPosition.AsVec2;
    //                            ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
    //                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
    //                            //}
    //                            waitCountApproachingStorage[__instance.Formation] = 0;
    //                        }
    //                        else
    //                        {
    //                            //WorldPosition medianPosition = __instance.Formation.QuerySystem.MedianPosition;
    //                            //medianPosition.SetVec2(approachingRangingStorage[__instance.Formation]);
    //                            //____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
    //                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
    //                            waitCountApproachingStorage[__instance.Formation] = waitCountApproachingStorage[__instance.Formation] + 1;
    //                        }
    //                        break;

    //                }
    //            }
    //        }

    //    }
    //}

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
                Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, false);

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
                                    if(distance > 100f)
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
                                    if (waitCountApproachingStorage[__instance.Formation] > 30)
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
        static void PostfixCalculateCurrentOrder(BehaviorMountedSkirmish __instance, ref bool ____engaging, ref MovementOrder ____currentOrder, ref bool ____isEnemyReachable)
        {
            WorldPosition position = __instance.Formation.QuerySystem.MedianPosition;
            Formation targetFormation = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
            FormationQuerySystem targetFormationQS = null;
            if(targetFormation != null)
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
                bool num = (__instance.Formation.QuerySystem.AverageAllyPosition - __instance.Formation.Team.QuerySystem.AverageEnemyPosition).LengthSquared <= 3600f;
                bool engaging = ____engaging;
                engaging = (____engaging = (num || ((!____engaging) ? ((__instance.Formation.QuerySystem.AveragePosition - __instance.Formation.QuerySystem.AverageAllyPosition).LengthSquared <= 3600f) : (!(__instance.Formation.QuerySystem.UnderRangedAttackRatio > __instance.Formation.QuerySystem.MakingRangedAttackRatio) && ((!__instance.Formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.IsCavalryFormation && !__instance.Formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.IsRangedCavalryFormation) || (__instance.Formation.QuerySystem.AveragePosition - __instance.Formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.MedianPosition.AsVec2).LengthSquared / (__instance.Formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.MovementSpeed * __instance.Formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.MovementSpeed) >= 16f)))));
                if (!____engaging)
                {
                    position = new WorldPosition(Mission.Current.Scene, new Vec3(__instance.Formation.QuerySystem.AverageAllyPosition, __instance.Formation.Team.QuerySystem.MedianPosition.GetNavMeshZ() + 100f));
                }
                else
                {
                    Vec2 vec = (__instance.Formation.QuerySystem.AveragePosition - targetFormationQS.AveragePosition).Normalized().LeftVec();
                    FormationQuerySystem closestSignificantlyLargeEnemyFormation = targetFormationQS;
                    float num2 = 50f + (targetFormationQS.Formation.Width + __instance.Formation.Depth) * 0.5f;
                    float num3 = 0f;

                    Formation enemyFormation = targetFormationQS.Formation;

                    if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
                    {
                        enemyFormation = Utilities.FindSignificantEnemyToPosition(__instance.Formation, position, true, true, false, false, false, false);
                    }

                    //foreach (Team team in Mission.Current.Teams.ToList())
                    //{
                    //    if (!team.IsEnemyOf(__instance.Formation.Team))
                    //    {
                    //        continue;
                    //    }
                    //    foreach (Formation formation2 in team.FormationsIncludingSpecialAndEmpty.ToList())
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
                        bool flag = enemyFormation.QuerySystem.IsCavalryFormation || enemyFormation.QuerySystem.IsRangedCavalryFormation;
                        float num5 = flag ? 50f : 50f;
                        num5 += (enemyFormation.Depth + __instance.Formation.Width) * 0.5f;
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
        static void PostfixGetAiWeight(ref BehaviorMountedSkirmish __instance, ref float __result, ref bool ____isEnemyReachable)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsCavalryFormation)
            {
                if (Utilities.CheckIfMountedSkirmishFormation(__instance.Formation, 0.6f))
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
                Formation enemyCav = Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);
                if(enemyCav != null && __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyCav.QuerySystem.MedianPosition.AsVec2) < 55f)
                {
                    __result = 0.1f;
                    return;
                }
                if (!____isEnemyReachable)
                {
                    __result = 0.1f;
                    return;
                }
                __result = 1f;
                return;
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
            float distanceOffsetFromMainFormation = 80f;

            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                distanceFromMainFormation = 10f;
                closerDistanceFromMainFormation = 5f;
                distanceOffsetFromMainFormation = 60f;
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
                    if (!Mission.Current.IsPositionInsideBoundaries(vec) || __instance.Formation.QuerySystem.UnderRangedAttackRatio > 0.1f)
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
                    if (!Mission.Current.IsPositionInsideBoundaries(vec) || __instance.Formation.QuerySystem.UnderRangedAttackRatio > 0.1f)
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
                                    float changeToChargeDistance = 110f + (__instance.Formation.Depth + closestFormation.Formation.Depth) / 2f;
                                    if (closestFormation.Formation.QuerySystem.MedianPosition.AsVec2.Distance(position) < changeToChargeDistance)
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
        [HarmonyPatch("GetAiWeight")]
        static void PostfixGetAiWeight(ref float __result)
        {
            if (__result > 0f)
            {
                __result = __result + 0.5f;
            }
        }
    }

    [MBCallback]
    [HarmonyPatch(typeof(HumanAIComponent))]
    class OnDismountPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("AdjustSpeedLimit")]
        static bool AdjustSpeedLimitPrefix(ref HumanAIComponent __instance,ref Agent agent,ref float desiredSpeed,ref bool limitIsMultiplier, ref Agent ___Agent)
        {
            if(agent.Formation != null && (agent.Formation.QuerySystem.IsRangedCavalryFormation || agent.Formation.QuerySystem.IsCavalryFormation))
            {
                if(agent.MountAgent != null)
                {
                    float speed = agent.MountAgent.AgentDrivenProperties.MountSpeed;
                    ___Agent.SetMaximumSpeedLimit(speed, false);
                    agent.MountAgent.SetMaximumSpeedLimit(speed, false);
                    return false;
                }
            }
            else if(agent.Formation != null && agent.Formation.AI != null && agent.Formation.AI.ActiveBehavior != null && 
                (agent.Formation.AI.ActiveBehavior.GetType() == typeof(RBMBehaviorForwardSkirmish) || agent.Formation.AI.ActiveBehavior.GetType() == typeof(RBMBehaviorInfantryFlank)))
            {
                if(limitIsMultiplier && desiredSpeed < 0.8f)
                {
                    desiredSpeed = 0.8f;
                }
                //___Agent.SetMaximumSpeedLimit(100f, false);
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

    [HarmonyPatch(typeof(BehaviorCharge))]
    class OverrideBehaviorCharge
    {
        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };
        public static Dictionary<Formation, float> timeToMoveStorage = new Dictionary<Formation, float> { };

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static bool PrefixCalculateCurrentOrder(ref BehaviorCharge __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation!= null && !(__instance.Formation.Team.IsPlayerTeam || __instance.Formation.Team.IsPlayerAlly) && Campaign.Current != null && MobileParty.MainParty != null && MobileParty.MainParty.MapEvent != null)
            {
                TextObject defenderName = MobileParty.MainParty.MapEvent.GetLeaderParty(BattleSideEnum.Defender).Name;
                TextObject attackerName = MobileParty.MainParty.MapEvent.GetLeaderParty(BattleSideEnum.Attacker).Name;
                if (defenderName.Contains("Looter") || defenderName.Contains("Bandit") || defenderName.Contains("Raider") || attackerName.Contains("Looter") || attackerName.Contains("Bandit") || attackerName.Contains("Raider"))
                {
                    return true;
                }
            }
            if (__instance.Formation != null && (__instance.Formation.QuerySystem.IsInfantryFormation || __instance.Formation.QuerySystem.IsRangedFormation) && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true, 0.7f);

                if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle && __instance.Formation.QuerySystem.IsInfantryFormation && !Utilities.FormationFightingInMelee(__instance.Formation, 0.5f))
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
                    bool isOnlyCavReamining = Utilities.CheckIfOnlyCavRemaining(__instance.Formation);
                    if ((enemyCav != null) && (cavDist <= signDist) && (enemyCav.CountOfUnits > __instance.Formation.CountOfUnits / 10) && ((signDist > 35f || significantEnemy == enemyCav) || isOnlyCavReamining))
                    {
                        if (isOnlyCavReamining)
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
                                float storedPositonDistance = (storedPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2).Normalize();
                                if(storedPositonDistance > 10f)
                                {
                                    positionsStorage.Remove(__instance.Formation);
                                    positionsStorage.Add(__instance.Formation, positionNew);
                                    ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                                }
                                else
                                {
                                    ____currentOrder = MovementOrder.MovementOrderMove(storedPosition);
                                }
                            }
                            if (cavDist > 85f)
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
                            //if (cavDist > 150f)
                            //{
                            //    positionsStorage.Remove(__instance.Formation);
                            //}
                            __instance.Formation.ArrangementOrder = ArrangementOrderLine;
                            return false;
                        }
                        else
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
                                    float storedPositonDistance = (storedPosition.AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2).Normalize();
                                    if (storedPositonDistance > 10f)
                                    {
                                        positionsStorage.Remove(__instance.Formation);
                                        positionsStorage.Add(__instance.Formation, positionNew);
                                        ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                                    }
                                    else
                                    {
                                        ____currentOrder = MovementOrder.MovementOrderMove(storedPosition);
                                    }

                                }
                                if (cavDist > 85f)
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
                                __instance.Formation.ArrangementOrder = ArrangementOrderLine;
                                return false;
                            }
                        }
                        positionsStorage.Remove(__instance.Formation);
                    }
                    else if (significantEnemy != null && !significantEnemy.QuerySystem.IsRangedFormation &&signDist < 50f && Utilities.FormationActiveSkirmishersRatio(__instance.Formation, 0.38f))
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
                    if (__instance.Formation.TargetFormation != null && __instance.Formation.TargetFormation.ArrangementOrder == ArrangementOrder.ArrangementOrderShieldWall
                        && Utilities.ShouldFormationCopyShieldWall(__instance.Formation))
                    {
                        __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderShieldWall;
                    }
                    else if(__instance.Formation.TargetFormation != null && __instance.Formation.TargetFormation.ArrangementOrder == ArrangementOrder.ArrangementOrderLine)
                    {
                        __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                    }
                    else if (__instance.Formation.TargetFormation != null && __instance.Formation.TargetFormation.ArrangementOrder == ArrangementOrder.ArrangementOrderLoose)
                    {
                        __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
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
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0.01f, 7f, 4f, 20f, 6f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0.55f, 2f, 0.55f, 20f, 0.55f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 2f, 7f, 4f, 20f, 5f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 4f, 2f, 0.55f, 30f, 0.55f);
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

                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 5f, 20f, 6f);
                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 20f, 1f);
                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 2f, 7f, 4f, 20f, 5f);
                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 40f, 4f, 60f, 0f);
                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 5f, 7f, 10f, 8, 20f);
                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 1f, 12f, 1f, 30f, 0f);
                        //unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);

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
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0.55f, 2f, 4f, 20f, 0.55f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0.55f, 7f, 0.55f, 20f, 0.55f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 8f, 2f, 0.55f, 30f, 0.55f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 10f, 15f, 0.065f, 30f, 0.065f);
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
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 15f, 5f, 20f, 5f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0.02f, 2f, 0.01f, 20f, 0.01f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0.02f, 7f, 0.04f, 20f, 0.03f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 5f, 2f, 0.55f, 30f, 0.55f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 10f, 15f, 0.065f, 30f, 0.065f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
                    return false;
                }
            }
            if (Mission.Current.MissionTeamAIType != Mission.MissionTeamAITypeEnum.FieldBattle)
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
            if (unit.Formation != null)
            {
                if (unit.Formation.GetReadonlyMovementOrderReference().OrderEnum == MovementOrder.MovementOrderEnum.FallBack)
                {
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 6f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0f, 4f, 0f, 20f, 0f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 10f, 7f, 5f, 30f, 0.05f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0.02f, 15f, 0.065f, 30f, 0.055f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
                    return false;
                }
                else
                {
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 6f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 4f, 3f, 20f, 0.01f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0.02f, 7f, 0.04f, 20f, 0.03f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 10f, 7f, 5f, 30f, 0.05f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0.02f, 15f, 0.065f, 30f, 0.055f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 9f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
                    return false;
                }
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

        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

        [HarmonyPostfix]
        [HarmonyPatch("GetPositionAux")]
        static void GetPositionAuxPostfix(ref MovementOrder __instance, ref WorldPosition __result, ref Formation f, ref WorldPosition.WorldPositionEnforcedCache worldPositionEnforcedCache)
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
                Formation enemyFormation = Utilities.FindSignificantEnemy(f, true, true, false, false, false, true);
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
                    float effectiveMissileRange = querySystem.MissileRange / 2.25f;
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
                    float num = 2f;
                    if (enemyQuerySystem.FormationPower < f.QuerySystem.FormationPower * 0.2f)
                    {
                        num = 0.1f;
                    }
                    newPosition.SetVec2(newPosition.AsVec2 - vec * num);

                    if (distance > 5f)
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
    class OverrideAgent
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
        //                formations = Utilities.FindSignificantFormations(formation);
        //                if (formations.Count > 0)
        //                {
        //                    __result = Utilities.NearestAgentFromMultipleFormations(__instance.Position.AsVec2, formations);
        //                    return false;
        //                }
        //                //Formation enemyFormation = formation.MovementOrder.TargetFormation;
        //                //if(enemyFormation != null)
        //                //{
        //                //    __result = Utilities.NearestAgentFromFormation(__instance.Position.AsVec2, enemyFormation);
        //                //    return false;
        //                //}
        //            }
        //        }
        //    }
        //    return true;
        //}

        [HarmonyPrefix]
        [HarmonyPatch("SetFiringOrder")]
        static bool PrefixSetFiringOrder(ref Agent __instance, ref int order)
        {
            if (__instance.Formation != null && __instance.Formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget)
            {
                if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

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

        private static int aiDecisionCooldownTime = 2;
        private static int aiDecisionCooldownTimeSiege = 0;

        [HarmonyPrefix]
        [HarmonyPatch("GetOrderPositionOfUnit")]
        static bool PrefixGetOrderPositionOfUnit(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
        {
            //if (__instance.MovementOrder.OrderType == OrderType.ChargeWithTarget && __instance.QuerySystem.IsInfantryFormation && !___detachedUnits.Contains(unit))
            if (unit != null && (__instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge) && (__instance.QuerySystem.IsInfantryFormation || __instance.QuerySystem.IsRangedFormation) && !____detachedUnits.Contains(unit))
            {
                bool isFieldBattle = Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle;
                AIDecision aiDecision;

                if (aiDecisionCooldownDict.TryGetValue(unit, out aiDecision))
                {
                    if (isFieldBattle)
                    {
                        if(aiDecision.customMaxCoolDown != -1)
                        {
                            if (aiDecision.cooldown < aiDecision.customMaxCoolDown)
                            {
                                __result = aiDecision.position;
                                aiDecisionCooldownDict[unit].cooldown += 1;
                                return false;
                            }
                            else
                            {
                                aiDecision.customMaxCoolDown = -1;
                                aiDecisionCooldownDict[unit].cooldown = 0;
                            }
                        }
                        if (aiDecision.cooldown < aiDecisionCooldownTime)
                        {
                            __result = aiDecision.position;
                            aiDecisionCooldownDict[unit].cooldown += 1;
                            return false;
                        }
                        else
                        {
                            aiDecisionCooldownDict[unit].cooldown = 0;
                        }
                    }
                    else
                    {
                        if (aiDecision.cooldown < aiDecisionCooldownTimeSiege)
                        {
                            __result = aiDecision.position;
                            aiDecisionCooldownDict[unit].cooldown += 1;
                            return false;
                        }
                        else
                        {
                            aiDecisionCooldownDict[unit].cooldown = 0;
                        }
                    }
                }
                else
                {
                    aiDecisionCooldownDict[unit] = new AIDecision();
                }
                bool isTargetArcher = false;
                var targetAgent = Utilities.GetCorrectTarget(unit);
                var vanillaTargetAgent = unit.GetTargetAgent();
                Mission mission = Mission.Current;

                if (targetAgent != null && vanillaTargetAgent != null)
                {
                    if(vanillaTargetAgent.Formation != null && vanillaTargetAgent.Formation == targetAgent.Formation)
                    {
                        targetAgent = vanillaTargetAgent;
                    }

                    Vec2 lookDirection = unit.LookDirection.AsVec2;
                    Vec2 unitPosition = unit.GetWorldPosition().AsVec2;

                    //float distance = unitPosition.Distance(targetAgent.GetWorldPosition().AsVec2);
                    Vec2 direction = (targetAgent.GetWorldPosition().AsVec2 - unitPosition).Normalized();
                    //var dot = __instance.Direction.x * direction.x + __instance.Direction.y * direction.y;
                    
                    if (targetAgent != vanillaTargetAgent && vanillaTargetAgent.HasMount)
                    {
                        __result = targetAgent.GetWorldPosition();
                        aiDecisionCooldownDict[unit].position = __result;
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 10f, 2f, 10f, 20f, 10f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 0f, 2f, 0f, 20f, 0f);
                        unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);
                        return false;
                    }
                    else
                    {
                        if((vanillaTargetAgent.Formation != null && vanillaTargetAgent.Formation.QuerySystem.IsRangedFormation))
                            // || (targetAgent.Formation != null && targetAgent.Formation.QuerySystem.IsRangedFormation))
                        {
                            isTargetArcher = true;
                        }
                        if (!isFieldBattle)
                        {
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0.03f);
                        }
                        else
                        {
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5f, 2f, 1f, 10f, 0.01f);
                            unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 8f, 0.8f, 20f, 20f);
                        }
                    }
                    //}

                    IEnumerable<Agent> agents;
                    if (isFieldBattle)
                    {
                        agents = mission.GetNearbyAllyAgents(unitPosition + direction * 1.1f, 1f, unit.Team);

                    }
                    else
                    {
                        agents = mission.GetNearbyAllyAgents(unitPosition + lookDirection * 1.1f, 1f, unit.Team);
                    }

                    int agentsCount = agents.Count();

                    if (!isFieldBattle)
                    {
                        if (agentsCount > 3)
                        {
                            int relevantAgentCount = 0;
                            foreach (Agent agent in agents)
                            {
                                if (Math.Abs(unit.VisualPosition.Z - agent.VisualPosition.Z) < 0.1f)
                                {
                                    relevantAgentCount++;
                                }
                            }
                            if (relevantAgentCount > 3)
                            {
                                __result = unit.GetWorldPosition();
                                aiDecisionCooldownDict[unit].position = __result; return false;
                            }
                            else
                            {
                                return true;
                            }
                        }
                        return true;
                    }
                    if (agentsCount > 3 && !unit.IsDoingPassiveAttack)
                    {
                        //if (MBRandom.RandomInt(100) == 0)
                        //{
                        //    return true;
                        //}
                        if (true)
                        {
                            if (unit != null)
                            {
                                Vec2 leftVec = direction.LeftVec() + direction * 1f;
                                Vec2 rightVec = direction.RightVec() + direction * 1f;
                                IEnumerable<Agent> agentsLeft = mission.GetNearbyAllyAgents(unitPosition + leftVec * 1.1f, 1f, unit.Team);
                                IEnumerable<Agent> agentsRight = mission.GetNearbyAllyAgents(unitPosition + rightVec * 1.1f, 1f, unit.Team);
                                IEnumerable<Agent> furtherAllyAgents = mission.GetNearbyAllyAgents(unitPosition + direction * 3f, 2f, unit.Team);

                                int agentsLeftCount = agentsLeft.Count();
                                int agentsRightCount = agentsRight.Count();
                                int furtherAllyAgentsCount = furtherAllyAgents.Count();

                                if (isFieldBattle && furtherAllyAgentsCount > 3)
                                {
                                    if (agentsLeftCount < agentsRightCount)
                                    {
                                        if (agentsLeftCount <= 3)
                                        {
                                            WorldPosition leftPosition = unit.GetWorldPosition();
                                            leftPosition.SetVec2(unitPosition + leftVec * 5f);
                                            __result = leftPosition;
                                            aiDecisionCooldownDict[unit].customMaxCoolDown = 10;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                    }
                                    else if (agentsLeftCount > agentsRightCount)
                                    {
                                        if (agentsRightCount <= 3)
                                        {
                                            WorldPosition rightPosition = unit.GetWorldPosition();
                                            rightPosition.SetVec2(unitPosition + rightVec * 5f);
                                            __result = rightPosition;
                                            aiDecisionCooldownDict[unit].customMaxCoolDown = 10;
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                    }
                                }
                                //if (agentsLeftCount > 4 && agentsRightCount > 4)
                                //{
                                //    __result = unit.GetWorldPosition();
                                //    aiDecisionCooldownDict[unit].position = __result; return false;
                                //}
                                else if (agentsLeftCount <= 3 && agentsRightCount <= 3)
                                {
                                    if (agentsLeftCount < agentsRightCount)
                                    {
                                        WorldPosition leftPosition = unit.GetWorldPosition();
                                        leftPosition.SetVec2(unitPosition + leftVec * 3f);
                                        __result = leftPosition;
                                        aiDecisionCooldownDict[unit].customMaxCoolDown = 8;
                                        aiDecisionCooldownDict[unit].position = __result; return false;
                                    }
                                    else if (agentsLeftCount > agentsRightCount)
                                    {
                                        WorldPosition rightPosition = unit.GetWorldPosition();
                                        rightPosition.SetVec2(unitPosition + rightVec * 3f);
                                        __result = rightPosition;
                                        aiDecisionCooldownDict[unit].customMaxCoolDown = 8;
                                        aiDecisionCooldownDict[unit].position = __result; return false;
                                    }
                                }
                                if (isFieldBattle)
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
                                        //WorldPosition closeAllyPosition = unit.GetWorldPosition();
                                        IEnumerable<Agent> nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 4f, unit.Team);
                                        if (nearbyAllyAgents.Count() > 0)
                                        {
                                            List<Agent> allyAgentList = nearbyAllyAgents.ToList();
                                            if (allyAgentList.Count() == 1)
                                            {
                                                __result = allyAgentList.ElementAt(0).GetWorldPosition();
                                                aiDecisionCooldownDict[unit].position = __result; return false;
                                            }
                                            allyAgentList.Remove(unit);
                                            float dist = 10000f;
                                            foreach (Agent agent in allyAgentList)
                                            {
                                                if(agent != unit)
                                                {
                                                    float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                                                    if (dist > newDist)
                                                    {
                                                        __result = agent.GetWorldPosition();
                                                        dist = newDist;
                                                    }
                                                }
                                            }
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                        else
                                        {
                                            //__result = mission.GetClosestAllyAgent(unit.Team, closeAllyPosition.GetGroundVec3(), 4f).GetWorldPosition();
                                            __result = unit.GetWorldPosition();
                                            aiDecisionCooldownDict[unit].position = __result; return false;
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
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                    }
                                }
                                else
                                {
                                    __result = unit.GetWorldPosition();
                                    aiDecisionCooldownDict[unit].position = __result; return false;
                                }
                            }
                            else
                            {
                                return true;
                            }
                            //}
                        }
                    }
                    if (isFieldBattle)
                    {
                        IEnumerable<Agent> enemyAgents10f;
                        IEnumerable<Agent> enemyAgents0f = mission.GetNearbyEnemyAgents(unitPosition, 5f, unit.Team);
                        //IEnumerable<Agent> enemyAgentsImmidiate = null;

                        int enemyAgentsImmidiateCount = 0;
                        int enemyAgents10fCount = 0;
                        int powerSumImmidiate = (int)Math.Floor(Utilities.GetPowerOfAgentsSum(enemyAgents0f) * 100);

                        if (!isTargetArcher)
                        {
                            enemyAgents10f = mission.GetNearbyEnemyAgents(unitPosition + direction * 5f, 5f, unit.Team);
                            //enemyAgentsImmidiate = mission.GetNearbyEnemyAgents(unitPosition, 3f, unit.Team);

                            enemyAgentsImmidiateCount = enemyAgents0f.Count();
                            enemyAgents10fCount = enemyAgents10f.Count();
                        }
                        else
                        {
                            enemyAgentsImmidiateCount = 0;
                            enemyAgents10fCount = 0;
                        }
                        if (enemyAgentsImmidiateCount > 3 || enemyAgents10fCount > 3)
                        {
                            unit.LookDirection = direction.ToVec3();
                            int unitPower = (int)Math.Floor(unit.Character.GetPower() * 100);
                            int randInt = MBRandom.RandomInt(unitPower + 10);
                            int hasShieldBonus = 0;

                            if (unit.HasShieldCached)
                            {
                                hasShieldBonus = 30;
                            }
                            if (isTargetArcher)
                            {
                                hasShieldBonus = -60;
                            }
                            if (randInt < 0)
                            {
                                __result = unit.GetWorldPosition();
                                aiDecisionCooldownDict[unit].position = __result; return false;
                            }

                            if (!isTargetArcher)
                            {
                                int randImmidiate = MBRandom.RandomInt(powerSumImmidiate);
                                if (unitPower * 2 < randImmidiate)
                                {
                                    WorldPosition backPosition = unit.GetWorldPosition();
                                    backPosition.SetVec2(unitPosition - lookDirection * 1.5f);
                                    __result = backPosition;
                                    aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                    aiDecisionCooldownDict[unit].position = __result; return false;
                                }
                            }
                            if (enemyAgentsImmidiateCount > 7)
                            {
                                //int randImmidiate = MBRandom.RandomInt(powerSumImmidiate);
                                //if(unitPower / 2 < randImmidiate)
                                //{
                                WorldPosition backPosition = unit.GetWorldPosition();
                                backPosition.SetVec2(unitPosition - lookDirection * 1.5f);
                                __result = backPosition;
                                aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                aiDecisionCooldownDict[unit].position = __result; return false;
                                //}
                            }
                            else if (randInt < (unitPower / 2 + hasShieldBonus))
                            {
                                if (randInt < (unitPower / 2 + hasShieldBonus))
                                {
                                    if (enemyAgentsImmidiateCount > 5)
                                    {
                                        //int randImmidiate = MBRandom.RandomInt(powerSumImmidiate);
                                        //if (unitPower / 2 < randImmidiate)
                                        //{
                                        WorldPosition backPosition = unit.GetWorldPosition();
                                        backPosition.SetVec2(unitPosition - lookDirection * 1.5f);
                                        __result = backPosition;
                                        aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                        aiDecisionCooldownDict[unit].position = __result; return false;
                                        //}
                                    }
                                    //WorldPosition closeAllyPosition = unit.GetWorldPosition();
                                    IEnumerable<Agent> nearbyAllyAgents = mission.GetNearbyAllyAgents(unitPosition, 5f, unit.Team);
                                    if (nearbyAllyAgents.Count() > 0)
                                    {
                                        List<Agent> allyAgentList = nearbyAllyAgents.ToList();
                                        if (allyAgentList.Count() == 1)
                                        {
                                            __result = allyAgentList.ElementAt(0).GetWorldPosition();
                                            aiDecisionCooldownDict[unit].position = __result; return false;
                                        }
                                        allyAgentList.Remove(unit);
                                        float dist = 10000f;
                                        foreach (Agent agent in allyAgentList)
                                        {
                                            if (agent != unit)
                                            {
                                                float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                                                if (dist > newDist)
                                                {
                                                    __result = agent.GetWorldPosition();
                                                    dist = newDist;
                                                }
                                            }
                                        }
                                        aiDecisionCooldownDict[unit].position = __result; return false;
                                    }
                                    else
                                    {
                                        __result = unit.GetWorldPosition();
                                        //__result = mission.GetClosestAllyAgent(unit.Team, closeAllyPosition.GetGroundVec3(), 4f).GetWorldPosition();
                                        aiDecisionCooldownDict[unit].position = __result; return false;
                                    }
                                }
                                else
                                {
                                    if (MBRandom.RandomInt(unitPower / 4) == 0)
                                    {
                                        __result = unit.GetWorldPosition();
                                        //__result = WorldPosition.Invalid;
                                        aiDecisionCooldownDict[unit].position = __result; return false;
                                    }
                                    else
                                    {
                                        WorldPosition backPosition = unit.GetWorldPosition();
                                        backPosition.SetVec2(unitPosition - lookDirection * 1.5f);
                                        __result = backPosition;
                                        aiDecisionCooldownDict[unit].customMaxCoolDown = 1;
                                        aiDecisionCooldownDict[unit].position = __result; return false;
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
                aiDecisionCooldownDict[unit].position = __result; return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetOrderPositionOfUnitAux")]
        static bool PrefixGetOrderPositionOfUnitAux(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ____detachedUnits, ref WorldPosition __result)
        {
            
            if (!Mission.Current.IsFieldBattle && unit != null && (__instance.QuerySystem.IsInfantryFormation) && (__instance.AI != null || __instance.IsAIControlled == false) && __instance.AI.ActiveBehavior != null)
            {
                if (__instance.QuerySystem.ClosestEnemyFormation != null)
                {
                    if(__instance.OrderPositionIsValid && __instance.OrderPosition.Distance(__instance.QuerySystem.AveragePosition) < 9f)
                    //if(__instance.QuerySystem.ClosestEnemyFormation.AveragePosition.Distance(__instance.QuerySystem.AveragePosition) < 25f)
                    {
                        //InformationManager.DisplayMessage(new InformationMessage(__instance.AI.ActiveBehavior.GetType().Name + " " + __instance.MovementOrder.OrderType.ToString()));
                        //bool exludedWhenAiControl = !(__instance.IsAIControlled && (__instance.AI.ActiveBehavior.GetType().Name.Contains("Regroup") || __instance.AI.ActiveBehavior.GetType().Name.Contains("Advance")));
                        //bool exludedWhenPlayerControl = !(!__instance.IsAIControlled && (__instance.GetReadonlyMovementOrderReference().OrderType.ToString().Contains("Advance")));

                        if (!____detachedUnits.Contains(unit))
                        {
                            Mission mission = Mission.Current;
                            if (mission.Mode != MissionMode.Deployment)
                            {
                                var targetAgent = unit.GetTargetAgent();
                                if (targetAgent != null)
                                {
                                    Vec2 unitPosition = unit.GetWorldPosition().AsVec2;
                                    //Vec2 direction = (targetAgent.GetWorldPosition().AsVec2 - unitPosition).Normalized();
                                    Vec2 direction = unit.LookDirection.AsVec2;

                                    IEnumerable<Agent> agents = mission.GetNearbyAllyAgents(unitPosition + direction * 0.8f, 1f, unit.Team);
                                    if(agents.Count() > 2)
                                    {
                                        int relevantAgentCount = 0;
                                        foreach (Agent agent in agents)
                                        {
                                            if (Math.Abs(unit.VisualPosition.Z - agent.VisualPosition.Z) < 0.1f && unit.Formation == agent.Formation)
                                            {
                                                relevantAgentCount++;
                                            }
                                        }

                                        if (relevantAgentCount > 2)
                                        {
                                            //if (MBRandom.RandomInt(100) == 0)
                                            //{
                                            //    return true;
                                            //}
                                            //else
                                            //{
                                            __result = unit.GetWorldPosition();
                                            return false;
                                            //}
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Formation))]
    class SetPositioningPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("SetPositioning")]
        static bool PrefixSetPositioning(ref Formation __instance,ref int? unitSpacing)
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
    class OverrideSetMovementOrder
    {
        [HarmonyPrefix]
        [HarmonyPatch("SetMovementOrder")]
        static bool PrefixSetOrder(Formation __instance, ref MovementOrder input)
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
                Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
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

                    Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

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
                        else if (significantEnemy != null && signDist < 60f && Utilities.FormationActiveSkirmishersRatio(__instance.Formation, 0.38f))
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

            [HarmonyPrefix]
            [HarmonyPatch("TickOccasionally")]
            static bool PrefixTickOccasionally(ref BehaviorAdvance __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder,
                ref bool ____isInShieldWallDistance, ref bool ____switchedToShieldWallRecently, ref Timer ____switchedToShieldWallTimer)
            {
                MethodInfo method = typeof(BehaviorAdvance).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
                method.DeclaringType.GetMethod("CalculateCurrentOrder");
                method.Invoke(__instance, new object[] { });

                __instance.Formation.SetMovementOrder(__instance.CurrentOrder);
                __instance.Formation.FacingOrder = ___CurrentFacingOrder;
                if (__instance.Formation.IsInfantry())
                {
                    Formation significantEnemy = Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true, 0.7f);
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
    }
}