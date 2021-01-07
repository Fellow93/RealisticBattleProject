using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using System.Diagnostics;

namespace RealisticBattleAiModule
{
    class Behaviours
    {
        //internal static void SetFollowBehaviorValues(Agent unit)
        //{
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 6f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 6f, 7f, 4f, 20f, 0f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 0f, 7f, 0f, 30f, 0f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0f, 15f, 0f, 30f, 0f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //}
        //internal static void SetDefaultMoveBehaviorValues(Agent unit)
        //{
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 7f, 5f, 20f, 6f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 7f, 5f, 20f, 0.01f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0.02f, 7f, 0.04f, 20f, 0.03f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 10f, 7f, 5f, 30f, 0.05f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0.02f, 15f, 0.065f, 30f, 0.055f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //}
        //internal static void SetDefensiveArrangementMoveBehaviorValues(Agent unit)
        //{
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 8f, 5f, 20f, 6f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 4f, 5f, 0f, 20f, 0f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 0f, 20f, 0f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 0f, 7f, 0f, 30f, 0f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 0f, 15f, 0f, 30f, 0f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //}
        //private static void SetChargeBehaviorValues(Agent unit)
        //{
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0f, 7f, 4f, 20f, 6f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 8f, 7f, 4f, 20f, 1f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 2f, 7f, 4f, 20f, 5f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.ChargeHorseback, 2f, 25f, 5f, 30f, 5f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 2f, 15f, 6.5f, 30f, 5.5f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 5f, 12f, 7.5f, 30f, 4f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityRanged, 0.55f, 12f, 0.8f, 30f, 0.45f);
        //}

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
            static void PostfixOnBehaviorActivatedAux(ref Formation ___formation)
            {
                ___formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
                ___formation.FormOrder = FormOrder.FormOrderWide;
            }
        }

        [HarmonyPatch(typeof(BehaviorDefend))]
        class OverrideBehaviorDefend
        {
            static WorldPosition medianPositionOld;

            [HarmonyPostfix]
            [HarmonyPatch("CalculateCurrentOrder")]
            static void PostfixCalculateCurrentOrder(Formation ___formation, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged, ref WorldPosition ____defensePosition, ref FacingOrder ___CurrentFacingOrder)
            {
                if (___formation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    WorldPosition medianPositionNew = ___formation.QuerySystem.MedianPosition;
                    medianPositionNew.SetVec2(___formation.QuerySystem.AveragePosition);

                    Formation significantEnemy = Utilities.FindSignificantEnemy(___formation, true, true, false, false, false);

                    if (significantEnemy != null)
                    {
                        Vec2 enemyDirection = significantEnemy.QuerySystem.MedianPosition.AsVec2 - ___formation.QuerySystem.MedianPosition.AsVec2;
                        float distance = enemyDirection.Normalize();
                        if (distance < (180f))
                        {
                            ____currentOrder = MovementOrder.MovementOrderMove(medianPositionOld);
                            ___IsCurrentOrderChanged = true;
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                        }
                        else
                        {
                            if (____defensePosition.IsValid)
                            {
                                medianPositionOld = ____defensePosition;
                                medianPositionOld.SetVec2(medianPositionOld.AsVec2 + ___formation.Direction * 10f);
                                ____currentOrder = MovementOrder.MovementOrderMove(medianPositionOld);
                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                            }
                            else
                            {
                                medianPositionOld = medianPositionNew;
                                medianPositionOld.SetVec2(medianPositionOld.AsVec2 + ___formation.Direction * 10f);
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
            static WorldPosition medianPositionOld;

            [HarmonyPostfix]
            [HarmonyPatch("CalculateCurrentOrder")]
            static void PostfixCalculateCurrentOrder(Formation ___formation, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged, ref FacingOrder ___CurrentFacingOrder)
            {
                if (___formation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    WorldPosition medianPositionNew = ___formation.QuerySystem.MedianPosition;
                    medianPositionNew.SetVec2(___formation.QuerySystem.AveragePosition);

                    Formation significantEnemy = Utilities.FindSignificantEnemy(___formation, true, true, false, false, false);

                    if (significantEnemy != null)
                    {
                        Vec2 enemyDirection = significantEnemy.QuerySystem.MedianPosition.AsVec2 - ___formation.QuerySystem.MedianPosition.AsVec2;
                        float distance = enemyDirection.Normalize();

                        if (distance < (180f))
                        {
                            ____currentOrder = MovementOrder.MovementOrderMove(medianPositionOld);
                            ___IsCurrentOrderChanged = true;
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                        }
                        else
                        {
                            medianPositionOld = medianPositionNew;
                            medianPositionOld.SetVec2(medianPositionOld.AsVec2 + ___formation.Direction * 10f);
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
            static void PostfixCalculateCurrentOrder(Formation ____mainFormation, Formation ___formation, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
            {
                if (____mainFormation != null && ___formation != null)
                {
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(____mainFormation.Direction);

                    WorldPosition medianPosition = ____mainFormation.QuerySystem.MedianPosition;
                    //medianPosition.SetVec2(medianPosition.AsVec2 - ____mainFormation.Direction * ((____mainFormation.Depth + ___formation.Depth) * 1.5f));
                    medianPosition.SetVec2(medianPosition.AsVec2 - ____mainFormation.Direction * ((____mainFormation.Depth + ___formation.Depth) * 0.25f + 0.0f));
                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);

                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("TickOccasionally")]
            static bool PrefixTickOccasionally(Formation ____mainFormation, ref Formation ___formation, BehaviorScreenedSkirmish __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
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
                ___formation.MovementOrder = ____currentOrder;
                ___formation.FacingOrder = ___CurrentFacingOrder;
                return false;
            }

            [HarmonyPostfix]
            [HarmonyPatch("OnBehaviorActivatedAux")]
            static void PostfixOnBehaviorActivatedAux(ref Formation ___formation)
            {
                ___formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
            }

            //[HarmonyPostfix]
            //[HarmonyPatch("GetAiWeight")]
            //static void PostfixGetAiWeight(ref Formation ___formation, ref float __result)
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

        private static int waitCountShooting = 0;
        private static int waitCountApproaching = 0;

        private static Vec2 approachingRanging;

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static void PostfixCalculateCurrentOrder(ref Formation ___formation, BehaviorSkirmish __instance, ref FacingOrder ___CurrentFacingOrder, ref BehaviorState ____behaviorState, ref MovementOrder ____currentOrder)
        {
            if (___formation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = Utilities.FindSignificantEnemy(___formation, true, true, false, false, false);

                if (significantEnemy != null)
                {
                    Vec2 enemyDirection = significantEnemy.QuerySystem.MedianPosition.AsVec2 - ___formation.QuerySystem.MedianPosition.AsVec2;
                    float distance = enemyDirection.Normalize();

                    switch (____behaviorState)
                    {
                        case BehaviorState.Shooting:
                            if (waitCountShooting > 50)
                            {
                                if (___formation.QuerySystem.MakingRangedAttackRatio < 0.4f && distance > 30f)
                                {
                                    WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
                                    medianPosition.SetVec2(medianPosition.AsVec2 + enemyDirection * 5f);
                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                                }
                                waitCountShooting = 0;
                            }
                            else
                            {
                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                                waitCountShooting++;
                            }

                            break;
                        case BehaviorState.Approaching:
                            if (waitCountApproaching > 20)
                            {
                                if (distance < 200f)
                                {
                                    WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
                                    medianPosition.SetVec2(medianPosition.AsVec2 + enemyDirection * 5f);
                                    approachingRanging = medianPosition.AsVec2;
                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                                }
                                waitCountApproaching = 0;
                            }
                            else
                            {
                                WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
                                medianPosition.SetVec2(approachingRanging);
                                ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                                waitCountApproaching++;
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

        private static int waitCountApproaching = 0;
        private static int waitCountShooting = 0;

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static void PostfixCalculateCurrentOrder(ref Vec2 ____shootPosition, ref Formation ___formation, ref Formation ____archerFormation, BehaviorCautiousAdvance __instance, ref BehaviorState ____behaviorState, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (___formation != null && ____archerFormation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = Utilities.FindSignificantEnemy(___formation, true, true, false, false, false);

                if (significantEnemy != null)
                {
                    Vec2 vec = significantEnemy.QuerySystem.MedianPosition.AsVec2 - ___formation.QuerySystem.MedianPosition.AsVec2;
                    float distance = vec.Normalize();

                    switch (____behaviorState)
                    {
                        case BehaviorState.Shooting:
                            {
                                if (waitCountShooting > 75)
                                {
                                    if (distance > 100f)
                                    {
                                        WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
                                        medianPosition.SetVec2(medianPosition.AsVec2 + vec * 5f);
                                        ____shootPosition = medianPosition.AsVec2 + vec * 5f;
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                    waitCountShooting = 0;
                                    waitCountApproaching = 0;
                                }
                                else
                                {
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                    waitCountShooting++;
                                }
                                break;
                            }
                        case BehaviorState.Approaching:
                            {
                                if (distance > 210f)
                                {
                                    WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
                                    medianPosition.SetVec2(medianPosition.AsVec2 + vec * 10f);
                                    ____shootPosition = medianPosition.AsVec2 + vec * 10f;
                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                }
                                else
                                {
                                    if (waitCountApproaching > 30)
                                    {
                                        if (distance < 210f)
                                        {
                                            WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
                                            medianPosition.SetVec2(medianPosition.AsVec2 + vec * 5f);
                                            ____shootPosition = medianPosition.AsVec2 + vec * 5f;
                                            ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                        }

                                        waitCountApproaching = 0;
                                    }
                                    else
                                    {
                                        if (distance < 210f)
                                        {
                                            WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
                                            medianPosition.SetVec2(____shootPosition);
                                            ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                        }
                                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                        waitCountApproaching++;
                                    }
                                }
                                break;
                            }
                        case BehaviorState.PullingBack:
                            {
                                if (waitCountApproaching > 30)
                                {
                                    if (distance < 210f)
                                    {
                                        WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
                                        medianPosition.SetVec2(medianPosition.AsVec2 - vec * 10f);
                                        ____shootPosition = medianPosition.AsVec2 + vec * 5f;
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
                                    waitCountApproaching = 0;
                                }
                                else
                                {
                                    if (distance < 210f)
                                    {
                                        WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
                                        medianPosition.SetVec2(____shootPosition);
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
                                    waitCountApproaching++;
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
                Vec2 v = _direction.LeftVec();
                Vec2 vec = _center + v * _halfLength;
                Vec2 vec2 = _center - v * _halfLength;
                Vec2 vec3 = position - _center;
                bool flag = vec3.Normalized().DotProduct(_direction) > 0f;
                Vec2 v2 = vec3.DotProduct(v) * v;
                bool flag2 = v2.Length < _halfLength;
                bool flag3 = true;
                if (flag2)
                {
                    position = _center + v2 + _direction * (_radius * (float)(flag ? 1 : (-1)));
                }
                else
                {
                    flag3 = (v2.DotProduct(v) > 0f);
                    Vec2 v3 = (position - (flag3 ? vec : vec2)).Normalized();
                    position = (flag3 ? vec : vec2) + v3 * _radius;
                }
                Vec2 vec4 = _center + v2;
                double num = Math.PI * 2.0 * (double)_radius;
                while (distance > 0f)
                {
                    if (flag2 && flag)
                    {
                        float num2 = ((vec - vec4).Length < distance) ? (vec - vec4).Length : distance;
                        position = vec4 + (vec - vec4).Normalized() * num2;
                        position += _direction * _radius;
                        distance -= num2;
                        flag2 = false;
                        flag3 = true;
                    }
                    else if (!flag2 && flag3)
                    {
                        Vec2 v4 = (position - vec).Normalized();
                        double num3 = Math.Acos(MBMath.ClampFloat(_direction.DotProduct(v4), -1f, 1f));
                        double num4 = Math.PI * 2.0 * ((double)distance / num);
                        double num5 = (num3 + num4 < Math.PI) ? (num3 + num4) : Math.PI;
                        double num6 = (num5 - num3) / Math.PI * (num / 2.0);
                        Vec2 direction = _direction;
                        direction.RotateCCW((float)num5);
                        position = vec + direction * _radius;
                        distance -= (float)num6;
                        flag2 = true;
                        flag = false;
                    }
                    else if (flag2)
                    {
                        float num7 = ((vec2 - vec4).Length < distance) ? (vec2 - vec4).Length : distance;
                        position = vec4 + (vec2 - vec4).Normalized() * num7;
                        position -= _direction * _radius;
                        distance -= num7;
                        flag2 = false;
                        flag3 = false;
                    }
                    else
                    {
                        Vec2 vec5 = (position - vec2).Normalized();
                        double num8 = Math.Acos(MBMath.ClampFloat(_direction.DotProduct(vec5), -1f, 1f));
                        double num9 = Math.PI * 2.0 * ((double)distance / num);
                        double num10 = (num8 - num9 > 0.0) ? (num8 - num9) : 0.0;
                        double num11 = num8 - num10;
                        double num12 = num11 / Math.PI * (num / 2.0);
                        Vec2 v5 = vec5;
                        v5.RotateCCW((float)num11);
                        position = vec2 + v5 * _radius;
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
        static void PostfixCalculateCurrentOrder(ref Formation ___formation, BehaviorMountedSkirmish __instance, ref bool ____engaging, ref MovementOrder ____currentOrder)
        {
            WorldPosition position = ___formation.QuerySystem.MedianPosition;
            if (___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
            {
                position.SetVec2(___formation.QuerySystem.AveragePosition);
            }
            else
            {
                bool num = (___formation.QuerySystem.AverageAllyPosition - ___formation.Team.QuerySystem.AverageEnemyPosition).LengthSquared <= 3600f;
                bool engaging = ____engaging;
                engaging = (____engaging = (num || ((!____engaging) ? ((___formation.QuerySystem.AveragePosition - ___formation.QuerySystem.AverageAllyPosition).LengthSquared <= 3600f) : (!(___formation.QuerySystem.UnderRangedAttackRatio > ___formation.QuerySystem.MakingRangedAttackRatio) && ((!___formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.IsCavalryFormation && !___formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.IsRangedCavalryFormation) || (___formation.QuerySystem.AveragePosition - ___formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.MedianPosition.AsVec2).LengthSquared / (___formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.MovementSpeed * ___formation.QuerySystem.FastestSignificantlyLargeEnemyFormation.MovementSpeed) >= 16f)))));
                if (!____engaging)
                {
                    position = new WorldPosition(Mission.Current.Scene, new Vec3(___formation.QuerySystem.AverageAllyPosition, ___formation.Team.QuerySystem.MedianPosition.GetNavMeshZ() + 100f));
                }
                else
                {
                    Vec2 vec = (___formation.QuerySystem.AveragePosition - ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.AveragePosition).Normalized().LeftVec();
                    FormationQuerySystem closestSignificantlyLargeEnemyFormation = ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                    float num2 = 50f + (___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.Width + ___formation.Depth) * 0.5f;
                    float num3 = 0f;
                    Formation formation = ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation;

                    foreach (Team team in Mission.Current.Teams.ToList())
                    {
                        if (!team.IsEnemyOf(___formation.Team))
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
                                    formation = formation2;
                                }
                            }
                        }
                    }

                    if (___formation.QuerySystem.RangedCavalryUnitRatio > 0.95f && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation == formation)
                    {
                        ____currentOrder = MovementOrder.MovementOrderCharge;
                        return;
                    }
                    bool flag = formation.QuerySystem.IsCavalryFormation || formation.QuerySystem.IsRangedCavalryFormation;
                    float num5 = flag ? 35f : 20f;
                    num5 += (formation.Depth + ___formation.Width) * 0.25f;
                    //num5 = Math.Min(num5, ___formation.QuerySystem.MissileRange - ___formation.Width * 0.5f);
                    Ellipse ellipse = new Ellipse(formation.QuerySystem.MedianPosition.AsVec2, num5, formation.Width * 0.25f * (flag ? 1.5f : 1f), formation.Direction);
                    position.SetVec2(ellipse.GetTargetPos(___formation.QuerySystem.AveragePosition, 20f));
                }
            }
            ____currentOrder = MovementOrder.MovementOrderMove(position);
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetAiWeight")]
        static void PostfixGetAiWeight(ref Formation ___formation, ref float __result)
        {
            if (Utilities.CheckIfMountedSkirmishFormation(___formation))
            {
                __result = 5f;
            }
            else if(___formation != null && ___formation.QuerySystem.IsRangedCavalryFormation)
            {
                __result = 0f;
            }
            else
            {
                __result = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorProtectFlank))]
    class OverrideBehaviorProtectFlank
    {
        private enum BehaviourState
        {
            HoldingFlank,
            Charging,
            Returning
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static bool PrefixCalculateCurrentOrdert(ref FormationAI.BehaviorSide ___FlankSide, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref MovementOrder ____chargeToTargetOrder, ref MovementOrder ____movementOrder, ref BehaviourState ____protectFlankState, ref Formation ___formation, ref Formation ____mainFormation, ref FormationAI.BehaviorSide ___behaviorSide)
        {
            if (____mainFormation == null || ___formation.QuerySystem.ClosestEnemyFormation == null)
            {
                ____currentOrder = MovementOrder.MovementOrderStop;
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            }
            else if (____protectFlankState == BehaviourState.HoldingFlank || ____protectFlankState == BehaviourState.Returning)
            {
                Vec2 direction = ____mainFormation.Direction;
                Vec2 v = (___formation.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - ____mainFormation.QuerySystem.MedianPosition.AsVec2).Normalized();
                Vec2 vec;
                if (___behaviorSide == FormationAI.BehaviorSide.Right || ___FlankSide == FormationAI.BehaviorSide.Right)
                {
                    vec = ____mainFormation.CurrentPosition + v.RightVec().Normalized() * (____mainFormation.Width + ___formation.Width + 100f);
                    vec -= v * (____mainFormation.Depth + ___formation.Depth);
                    vec += ____mainFormation.Direction * 100f;
                }
                else if (___behaviorSide == FormationAI.BehaviorSide.Left || ___FlankSide == FormationAI.BehaviorSide.Left)
                {
                    vec = ____mainFormation.CurrentPosition + v.LeftVec().Normalized() * (____mainFormation.Width + ___formation.Width + 100f);
                    vec -= v * (____mainFormation.Depth + ___formation.Depth);
                    vec += ____mainFormation.Direction * 100f;
                }
                else
                {
                    vec = ____mainFormation.CurrentPosition + v * ((____mainFormation.Depth + ___formation.Depth) * 0.5f + 10f);
                }
                WorldPosition medianPosition = ____mainFormation.QuerySystem.MedianPosition;
                medianPosition.SetVec2(vec);
                ____movementOrder = MovementOrder.MovementOrderMove(medianPosition);
                ____currentOrder = ____movementOrder;
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);
            }
            return false;
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
        static bool PrefixTickOccasionally(ref Formation ___formation, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder, BehaviorVanguard __instance)
        {
            MethodInfo method = typeof(BehaviorVanguard).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
            method.DeclaringType.GetMethod("CalculateCurrentOrder");
            method.Invoke(__instance, new object[] { });

            ___formation.MovementOrder = ____currentOrder;
            ___formation.FacingOrder = ___CurrentFacingOrder;
            if (___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && ___formation.QuerySystem.AveragePosition.DistanceSquared(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.MedianPosition.AsVec2) > 1600f && ___formation.QuerySystem.UnderRangedAttackRatio > 0.2f - ((___formation.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Loose) ? 0.1f : 0f))
            {
                ___formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
            }
            else
            {
                ___formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorAssaultWalls))]
    class OverrideBehaviorAssaultWalls
    {
        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static bool PrefixCalculateCurrentOrder(ref Formation ___formation, ref MovementOrder ____chargeOrder)
        {
            if(___formation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                ____chargeOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BehaviorDefendCastleKeyPosition))]
    class OverrideBehaviorDefendCastleKeyPosition
    {

        private enum BehaviourState
        {
            UnSet,
            Waiting,
            Ready
        }

        [HarmonyPostfix]
        [HarmonyPatch("ResetOrderPositions")]
        static void PostfixResetOrderPositions(ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref Formation ___formation, ref TacticalPosition ____tacticalMiddlePos,ref TacticalPosition ____tacticalWaitPos , ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviourState ____behaviourState)
        {
            if (____tacticalMiddlePos != null )
            {
                if(___formation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    if (____innerGate == null)
                    {
                        if (____outerGate != null)
                        {
                            float distance = ___formation.QuerySystem.MedianPosition.AsVec2.Distance(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
                            if (____outerGate.IsDestroyed && TeamAISiegeComponent.IsFormationInsideCastle(___formation, includeOnlyPositionedUnits: false) && distance < 35f)
                            {
                                ____readyOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                                ____currentOrder = ____readyOrder;
                            }
                        }
                    }
                    else
                    {
                        float distance = ___formation.QuerySystem.MedianPosition.AsVec2.Distance(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
                        if (____innerGate.IsDestroyed && TeamAISiegeComponent.IsFormationInsideCastle(___formation, includeOnlyPositionedUnits: false) && distance < 35f)
                        {
                            ____readyOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            ____currentOrder = ____readyOrder;
                        }
                    }
                }

                if(____innerGate != null && !____innerGate.IsDestroyed)
                {
                    PropertyInfo property = typeof(TacticalPosition).GetProperty("Position", BindingFlags.NonPublic | BindingFlags.Instance);
                    property.DeclaringType.GetProperty("Position");
                    WorldPosition position = (WorldPosition)property.GetValue(____tacticalMiddlePos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                    if (____behaviourState == BehaviourState.Ready)
                    {
                        Vec2 direction = (____innerGate.GetPosition().AsVec2 - ___formation.QuerySystem.MedianPosition.AsVec2).Normalized();
                        WorldPosition newPosition = position;
                        newPosition.SetVec2(position.AsVec2 - direction * 3f);
                        ____readyOrder = MovementOrder.MovementOrderMove(newPosition);
                        ____currentOrder = ____readyOrder;
                    }
                }
            }
            if(____tacticalWaitPos != null && ____tacticalMiddlePos == null)
            {
                float distance = ___formation.QuerySystem.MedianPosition.AsVec2.Distance(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
                if (TeamAISiegeComponent.IsFormationInsideCastle(___formation, includeOnlyPositionedUnits: false) && distance < 35f)
                {
                    ____readyOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                    ____currentOrder = ____readyOrder;
                }
            }
        }
    }

    [HarmonyPatch(typeof(LadderQueueManager))]
    class OverrideLadderQueueManager
    {

        [HarmonyPostfix]
        [HarmonyPatch("Initialize")]
        static void PostfixInitialize(ref BattleSideEnum managedSide, Vec3 managedDirection,  ref float queueBeginDistance, ref int ____maxUserCount, ref float ____agentSpacing, ref float ____queueBeginDistance, ref float ____queueRowSize, ref float ____costPerRow, ref float ____baseCost)
        {
            if(managedSide == BattleSideEnum.Attacker && queueBeginDistance != 3f && managedDirection != new Vec3(0f, -1f))
            {
                ____agentSpacing = 1.25f;
                ____queueBeginDistance = 8f;
                ____queueRowSize = 1.25f;
                ____maxUserCount = 16;
            }
            
        }
    }

    [HarmonyPatch(typeof(SiegeTower))]
    class OverrideSiegeTower
    {

        [HarmonyPostfix]
        [HarmonyPatch("OnInit")]
        static void PostfixOnInit(ref SiegeTower __instance,ref GameEntity ____gameEntity,  ref GameEntity ____cleanState, ref List<LadderQueueManager> ____queueManagers, ref int ___DynamicNavmeshIdStart)
        {
            //__instance.ForcedUse = true;
            List<GameEntity> list2 = ____cleanState.CollectChildrenEntitiesWithTag("ladder");
            if (list2.Count == 3)
            {
                ____queueManagers.Clear();
                LadderQueueManager ladderQueueManager0 = list2.ElementAt(0).GetScriptComponents<LadderQueueManager>().FirstOrDefault();
                LadderQueueManager ladderQueueManager1 = list2.ElementAt(1).GetScriptComponents<LadderQueueManager>().FirstOrDefault();
                LadderQueueManager ladderQueueManager2 = list2.ElementAt(2).GetScriptComponents<LadderQueueManager>().FirstOrDefault();
                if (ladderQueueManager0 != null)
                {
                    MatrixFrame identity = MatrixFrame.Identity;
                    identity.rotation.RotateAboutSide((float)Math.PI / 2f);
                    identity.rotation.RotateAboutForward((float)Math.PI / 8f);

                    ladderQueueManager0.Initialize(list2.ElementAt(0).GetScriptComponents<LadderQueueManager>().FirstOrDefault().ManagedNavigationFaceId, identity, new Vec3(0f, 0f, 1f), BattleSideEnum.Attacker, 3, (float)Math.PI * 3f / 4f, 0.8f, 0.8f, 30f, 50f, blockUsage: false, 0.8f, 4f, 5f);
                    ____queueManagers.Add(ladderQueueManager0);
                }
                if (ladderQueueManager1 != null)
                {
                    MatrixFrame identity = MatrixFrame.Identity;
                    identity.rotation.RotateAboutSide((float)Math.PI / 2f);
                    identity.rotation.RotateAboutForward((float)Math.PI / 8f);

                    ladderQueueManager1.Initialize(list2.ElementAt(1).GetScriptComponents<LadderQueueManager>().FirstOrDefault().ManagedNavigationFaceId, identity, new Vec3(0f, 0f, 1f), BattleSideEnum.Attacker, 3, (float)Math.PI * 3f / 4f, 0.8f, 0.8f, 30f, 50f, blockUsage: false, 0.8f, 4f, 5f);
                    ____queueManagers.Add(ladderQueueManager1);
                }
                if (ladderQueueManager2 != null)
                {
                    MatrixFrame identity = MatrixFrame.Identity;
                    identity.rotation.RotateAboutSide((float)Math.PI / 2f);
                    identity.rotation.RotateAboutForward((float)Math.PI / 8f);

                    ladderQueueManager2.Initialize(list2.ElementAt(2).GetScriptComponents<LadderQueueManager>().FirstOrDefault().ManagedNavigationFaceId, identity, new Vec3(0f, 0f, 1f), BattleSideEnum.Attacker, 3, (float)Math.PI * 3f / 4f, 0.8f, 0.8f, 30f, 50f, blockUsage: false, 0.8f, 4f, 5f);
                    ____queueManagers.Add(ladderQueueManager2);
                }
                foreach (LadderQueueManager queueManager in ____queueManagers)
                {
                    ____cleanState.Scene.SetAbilityOfFacesWithId(queueManager.ManagedNavigationFaceId, isEnabled: false);
                    queueManager.IsDeactivated = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorCharge))]
    class OverrideBehaviorCharge
    {
        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static bool PrefixCalculateCurrentOrder(ref Formation ___formation, ref MovementOrder ____currentOrder)
        {
            if (___formation != null && (___formation.QuerySystem.IsInfantryFormation || ___formation.QuerySystem.IsRangedFormation) && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = Utilities.FindSignificantEnemy(___formation, true, true, false, false, false);
                if (significantEnemy != null)
                {
                    ____currentOrder = MovementOrder.MovementOrderChargeToTarget(significantEnemy);
                    return false;
                }
            }
            ____currentOrder = MovementOrder.MovementOrderCharge;
            return false;
        }

        //[HarmonyPostfix]
        //[HarmonyPatch("GetAiWeight")]
        //static void PostfixGetAiWeight(ref Formation ___formation, ref float __result)
        //{
        //    __result.ToString();
        //}
    }

    [HarmonyPatch(typeof(FormationMovementComponent))]
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

        private static readonly MethodInfo IsUnitDetached =
            typeof(Formation).GetMethod("IsUnitDetached", BindingFlags.Instance | BindingFlags.NonPublic);

        [HarmonyPrefix]
        [HarmonyPatch("GetFormationFrame")]
        static bool PrefixGetFormationFrame(ref bool __result,ref Agent ___Agent, ref FormationCohesionComponent ____cohesionComponent, ref WorldPosition formationPosition, ref Vec2 formationDirection, ref float speedLimit, ref bool isSettingDestinationSpeed, ref bool limitIsMultiplier)
        {
            if(___Agent != null)
            {
                var formation = ___Agent.Formation;
                if (!___Agent.IsMount && formation != null && (formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && !(bool)IsUnitDetached.Invoke(formation, new object[] { ___Agent }))
                {
                    if (formation.MovementOrder.OrderType == OrderType.ChargeWithTarget)
                    {
                        if (___Agent != null && formation != null)
                        {
                            isSettingDestinationSpeed = false;
                            formationPosition = formation.GetOrderPositionOfUnit(___Agent);
                            formationDirection = formation.GetDirectionOfUnit(___Agent);
                            limitIsMultiplier = true;
                            speedLimit = ____cohesionComponent != null && FormationCohesionComponent.FormationSpeedAdjustmentEnabled ? ____cohesionComponent.GetDesiredSpeedInFormation(false) : -1f;
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
            if (unit.Formation != null && unit.Formation.MovementOrder.OrderType == OrderType.ChargeWithTarget)
            {
                if (unit.Formation.QuerySystem.IsInfantryFormation)
                {
                    //unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 0f, 40f, 4f, 50f, 6f);
                    //unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 7f, 1f, 10f, 0.01f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 4f, 2f, 4f, 10f, 6f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 5.5f, 2f, 1f, 10f, 0.01f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Ranged, 0f, 7f, 1f, 10f, 20f);
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
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.RangedHorseback, 5f, 7f, 10f, 8, 20f);
                    unit.SetAIBehaviorValues(AISimpleBehaviorKind.AttackEntityMelee, 1f, 12f, 1f, 30f, 0f);
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
            if(__instance != null)
            {
                Formation formation = __instance.Formation;
                if(formation != null)
                {
                    if ((formation.QuerySystem.IsInfantryFormation ||  formation.QuerySystem.IsRangedFormation) && (formation.MovementOrder.OrderType == OrderType.ChargeWithTarget))
                    {
                        Formation enemyFormation = formation.MovementOrder.TargetFormation;
                        if(enemyFormation != null)
                        {
                            __result = Utilities.NearestAgentFromFormation(__instance.Position.AsVec2, enemyFormation);
                            return false;
                        }
                    }
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Formation))]
    class OverrideFormation
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetOrderPositionOfUnit")]
        static bool PrefixGetOrderPositionOfUnit(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ___detachedUnits, ref WorldPosition __result)
        {
            //if (__instance.MovementOrder.OrderType == OrderType.ChargeWithTarget && __instance.QuerySystem.IsInfantryFormation && !___detachedUnits.Contains(unit))
            if (unit != null && __instance.MovementOrder.OrderType == OrderType.ChargeWithTarget && (__instance.QuerySystem.IsInfantryFormation || __instance.QuerySystem.IsRangedFormation) && !___detachedUnits.Contains(unit))
            {
                Formation significantEnemy = __instance.TargetFormation;
                if (significantEnemy != null)
                {
                    var targetAgent = unit.GetTargetAgent();
                    Mission mission = Mission.Current;
                    if (targetAgent != null)
                    {

                        //float distance = unit.GetWorldPosition().AsVec2.Distance(targetAgent.GetWorldPosition().AsVec2);
                        //if (distance > 25f)
                        //{
                        //    __result = targetAgent.GetWorldPosition();
                        //    return false;
                        //}

                        Vec2 direction = (targetAgent.GetWorldPosition().AsVec2 - unit.GetWorldPosition().AsVec2).Normalized();
                        IEnumerable<Agent> agents = mission.GetNearbyAllyAgents(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2 * 0.8f, 1f, unit.Team);
                        if (agents.Count() > 3)
                        {
                            unit.LookDirection = direction.ToVec3();
                            if (MBRandom.RandomInt(75) == 0)
                            {
                                if (targetAgent != null)
                                {
                                    __result = targetAgent.GetWorldPosition();
                                    return false;
                                }
                                else
                                {
                                    return true;
                                }
                            }
                            else
                            {
                                //float distancefr = unit.GetWorldPosition().AsVec2.Distance(agents.ElementAt(0).Position.AsVec2);
                                //float slowdown = Math.Min(distancefr/2f, 1f);
                                //if(slowdown < 0.6f)
                                //{
                                if (unit != null)
                                {
                                    IEnumerable<Agent> agentsLeft = mission.GetNearbyAllyAgents(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.LeftVec() * 0.8f, 1f, unit.Team);
                                    IEnumerable<Agent> agentsRight = mission.GetNearbyAllyAgents(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.RightVec() * 0.8f, 1f, unit.Team);
                                    if (agentsLeft.Count() > 3 && agentsRight.Count() > 3)
                                    {
                                    }
                                    else if (agentsLeft.Count() <= 3 && agentsRight.Count() <= 3)
                                    {
                                        if (MBRandom.RandomInt(2) == 0)
                                        {
                                            WorldPosition leftPosition = unit.GetWorldPosition();
                                            leftPosition.SetVec2(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.LeftVec());
                                            __result = leftPosition;
                                            return false;
                                        }
                                        else
                                        {
                                            WorldPosition rightPosition = unit.GetWorldPosition();
                                            rightPosition.SetVec2(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.RightVec());
                                            __result = rightPosition;
                                            return false;
                                        }
                                    }
                                    else if (agentsLeft.Count() <= 3)
                                    {
                                        WorldPosition leftPosition = unit.GetWorldPosition();
                                        leftPosition.SetVec2(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.LeftVec());
                                        __result = leftPosition;
                                        return false;
                                    }
                                    else if (agentsRight.Count() <= 3)
                                    {
                                        WorldPosition rightPosition = unit.GetWorldPosition();
                                        rightPosition.SetVec2(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.RightVec());
                                        __result = rightPosition;
                                        return false;
                                    }
                                    __result = unit.GetWorldPosition();
                                    return false;
                                }
                                else
                                {
                                    return true;
                                }
                                //}
                            }
                        }
                        else
                        {
                            if (targetAgent != null)
                            {
                                __result = targetAgent.GetWorldPosition();
                                return false;
                            }
                            else
                            {
                                return true;
                            }
                        }

                    }
                }
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetOrderPositionOfUnitAux")]
        static bool PrefixGetOrderPositionOfUnitAux(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ___detachedUnits, ref WorldPosition __result)
        {
            if(unit != null && __instance.QuerySystem.IsInfantryFormation && (__instance.AI != null || __instance.IsAIControlled == false) && __instance.AI.ActiveBehavior != null )
            {
                //InformationManager.DisplayMessage(new InformationMessage(__instance.AI.ActiveBehavior.GetType().Name + " " + __instance.MovementOrder.OrderType.ToString()));
                bool exludedWhenAiControl = !(__instance.IsAIControlled && (__instance.AI.ActiveBehavior.GetType().Name.Contains("Regroup") || __instance.AI.ActiveBehavior.GetType().Name.Contains("Advance")));
                bool exludedWhenPlayerControl = !(!__instance.IsAIControlled && (__instance.MovementOrder.OrderType.ToString().Contains("Advance")));

                if (exludedWhenAiControl && exludedWhenPlayerControl && !___detachedUnits.Contains(unit))
                {
                    Mission mission = Mission.Current;
                    if (mission.Mode != MissionMode.Deployment)
                    {
                        IEnumerable<Agent> agents = mission.GetNearbyAllyAgents(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2 * 0.8f, 1f, unit.Team);
                        if (agents.Count() > 3)
                        {
                            if (MBRandom.RandomInt(75) == 0)
                            {
                                return true;
                            }
                            else
                            {
                                if (unit != null)
                                {
                                    IEnumerable<Agent> agentsLeft = mission.GetNearbyAllyAgents(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.LeftVec() * 0.8f, 1f, unit.Team);
                                    IEnumerable<Agent> agentsRight = mission.GetNearbyAllyAgents(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.RightVec() * 0.8f, 1f, unit.Team);
                                    if (agentsLeft.Count() > 3 && agentsRight.Count() > 3)
                                    {
                                    }
                                    else if (agentsLeft.Count() <= 3 && agentsRight.Count() <= 3)
                                    {
                                        if (MBRandom.RandomInt(2) == 0)
                                        {
                                            WorldPosition leftPosition = unit.GetWorldPosition();
                                            leftPosition.SetVec2(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.LeftVec());
                                            __result = leftPosition;
                                            return false;
                                        }
                                        else
                                        {
                                            WorldPosition rightPosition = unit.GetWorldPosition();
                                            rightPosition.SetVec2(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.RightVec());
                                            __result = rightPosition;
                                            return false;
                                        }
                                    }
                                    else if (agentsLeft.Count() <= 3)
                                    {
                                        WorldPosition leftPosition = unit.GetWorldPosition();
                                        leftPosition.SetVec2(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.LeftVec());
                                        __result = leftPosition;
                                        return false;
                                    }
                                    else if (agentsRight.Count() <= 3)
                                    {
                                        WorldPosition rightPosition = unit.GetWorldPosition();
                                        rightPosition.SetVec2(unit.GetWorldPosition().AsVec2 + unit.GetMovementDirection().AsVec2.RightVec());
                                        __result = rightPosition;
                                        return false;
                                    }
                                    __result = unit.GetWorldPosition();
                                    return false;
                                }
                                else
                                {
                                    return true;
                                }
                            }
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

    [HarmonyPatch(typeof(OrderController))]
    class OverrideOrderController
    {
        [HarmonyPostfix]
        [HarmonyPatch("SetOrder")]
        static void PostfixSetOrder(OrderController __instance, OrderType orderType, ref Mission ____mission)
        {
        if(orderType == OrderType.Charge)
		foreach (Formation selectedFormation in __instance.SelectedFormations)
            {
                if ((selectedFormation.QuerySystem.IsInfantryFormation || selectedFormation.QuerySystem.IsRangedFormation) || ____mission.IsTeleportingAgents)
                {
                    if (selectedFormation.QuerySystem.ClosestEnemyFormation == null)
                    {
                        selectedFormation.MovementOrder = MovementOrder.MovementOrderCharge;
                    }
                    else
                    {
                        selectedFormation.MovementOrder = MovementOrder.MovementOrderChargeToTarget(selectedFormation.QuerySystem.ClosestEnemyFormation.Formation);
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
        static bool PrefixGetAiWeight(ref Formation ___formation, ref float __result)
        {
            if (___formation != null)
            {
                FormationQuerySystem querySystem = ___formation.QuerySystem;
                if (___formation.AI.ActiveBehavior == null)
                {
                    __result = 0f;
                    return false;
                }
                PropertyInfo property = typeof(BehaviorComponent).GetProperty("BehaviorCoherence", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("BehaviorCoherence");
                float behaviorCoherence = (float)property.GetValue(___formation.AI.ActiveBehavior, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null) * 2.75f;

                //__result =  MBMath.Lerp(0.1f, 1.2f, MBMath.ClampFloat(behaviorCoherence * (querySystem.FormationIntegrityData.DeviationOfPositionsExcludeFarAgents + 1f) / (querySystem.IdealAverageDisplacement + 1f), 0f, 3f) / 3f);
                __result = MBMath.Lerp(0.1f, 1.2f, MBMath.ClampFloat(behaviorCoherence * (querySystem.FormationIntegrityData.DeviationOfPositionsExcludeFarAgents + 1f) / (querySystem.IdealAverageDisplacement + 1f), 0f, 3f) / 3f);
                return false;

            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static bool PrefixCalculateCurrentOrder(ref Formation ___formation, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (___formation != null && ___formation.QuerySystem.IsInfantryFormation && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = Utilities.FindSignificantEnemy(___formation, true, true, false, false, false);
                if (significantEnemy != null)
                {
                    ___formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                    ___formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
                    ___formation.FormOrder = FormOrder.FormOrderWide;
                    ___formation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;

                    WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);

                    Vec2 direction = (significantEnemy.QuerySystem.MedianPosition.AsVec2 - ___formation.QuerySystem.AveragePosition).Normalized();
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);

                    return false;
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(BehaviorAdvance))]
        class OverrideBehaviorAdvance
        {
            [HarmonyPrefix]
            [HarmonyPatch("CalculateCurrentOrder")]
            static bool PrefixCalculateCurrentOrder(ref Formation ___formation, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
            {
                if (___formation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    Formation significantEnemy = Utilities.FindSignificantEnemy(___formation, true, true, false, false, false);

                    if (significantEnemy != null)
                    {
                        Vec2 vec = significantEnemy.QuerySystem.MedianPosition.AsVec2 - ___formation.QuerySystem.MedianPosition.AsVec2;
                        WorldPosition positionNew = ___formation.QuerySystem.MedianPosition;
                        positionNew.SetVec2(positionNew.AsVec2 + vec.Normalized() * 20f);
                        ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec.Normalized());
                        return false;
                    }
                }
                return true;
            }

            [HarmonyPostfix]
            [HarmonyPatch("OnBehaviorActivatedAux")]
            static void PostfixOnBehaviorActivatedAux(ref Formation ___formation, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
            {
                ___formation.FormOrder = FormOrder.FormOrderDeep;
            }
        }
    }
}