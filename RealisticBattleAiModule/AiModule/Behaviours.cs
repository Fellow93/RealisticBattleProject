using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.SiegeTower;
using SandBox;
using TaleWorlds.CampaignSystem;
using static TaleWorlds.MountAndBlade.FormationAI;
using static TaleWorlds.MountAndBlade.HumanAIComponent;

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
                ___formation.SetMovementOrder(____currentOrder);
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
                    if (___formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        Ellipse ellipse = new Ellipse(formation.QuerySystem.MedianPosition.AsVec2, num5, formation.Width * 0.5f * (flag ? 1.5f : 1f), formation.Direction);
                        position.SetVec2(ellipse.GetTargetPos(___formation.QuerySystem.AveragePosition, 20f));
                    }
                    else
                    {
                        Ellipse ellipse = new Ellipse(formation.QuerySystem.MedianPosition.AsVec2, num5, formation.Width * 0.25f * (flag ? 1.5f : 1f), formation.Direction);
                        position.SetVec2(ellipse.GetTargetPos(___formation.QuerySystem.AveragePosition, 20f));
                    }
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
            else if (___formation != null && ___formation.QuerySystem.IsRangedCavalryFormation)
            {
                __result = 1f;
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
        static bool PrefixCalculateCurrentOrder(ref FormationAI.BehaviorSide ___FlankSide, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref MovementOrder ____chargeToTargetOrder, ref MovementOrder ____movementOrder, ref BehaviourState ____protectFlankState, ref Formation ___formation, ref Formation ____mainFormation, ref FormationAI.BehaviorSide ___behaviorSide)
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
                    vec += ____mainFormation.Direction * 130f;
                }
                else if (___behaviorSide == FormationAI.BehaviorSide.Left || ___FlankSide == FormationAI.BehaviorSide.Left)
                {
                    vec = ____mainFormation.CurrentPosition + v.LeftVec().Normalized() * (____mainFormation.Width + ___formation.Width + 100f);
                    vec -= v * (____mainFormation.Depth + ___formation.Depth);
                    vec += ____mainFormation.Direction * 130f;
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

        [HarmonyPrefix]
        [HarmonyPatch("CheckAndChangeState")]
        static bool PrefixCheckAndChangeState( ref FormationAI.BehaviorSide ___FlankSide, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref MovementOrder ____chargeToTargetOrder, ref MovementOrder ____movementOrder, ref BehaviourState ____protectFlankState, ref Formation ___formation, ref Formation ____mainFormation, ref FormationAI.BehaviorSide ___behaviorSide)
        {
            WorldPosition position = ____movementOrder.GetPosition(___formation);
            switch (____protectFlankState)
            {
                case BehaviourState.HoldingFlank:
                    if (___formation.QuerySystem.ClosestEnemyFormation != null)
                    {
                        float num = 150f + (___formation.Depth + ___formation.QuerySystem.ClosestEnemyFormation.Formation.Depth) / 2f;
                        if (___formation.QuerySystem.ClosestEnemyFormation.MedianPosition.AsVec2.DistanceSquared(position.AsVec2) < num * num)
                        {
                            ____chargeToTargetOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestEnemyFormation.Formation);
                            ____currentOrder = ____chargeToTargetOrder;
                            ____protectFlankState = BehaviourState.Charging;
                        }
                    }
                    break;
                case BehaviourState.Charging:
                    {
                        if (___formation.QuerySystem.ClosestEnemyFormation == null)
                        {
                            ____currentOrder = ____movementOrder;
                            ____protectFlankState = BehaviourState.Returning;
                            break;
                        }
                        float num2 = 160f + (___formation.Depth + ___formation.QuerySystem.ClosestEnemyFormation.Formation.Depth) / 2f;
                        if (___formation.QuerySystem.AveragePosition.DistanceSquared(position.AsVec2) > num2 * num2)
                        {
                            ____currentOrder = ____movementOrder;
                            ____protectFlankState = BehaviourState.Returning;
                        }
                        break;
                    }
                case BehaviourState.Returning:
                    if (___formation.QuerySystem.AveragePosition.DistanceSquared(position.AsVec2) < 400f)
                    {
                        ____protectFlankState = BehaviourState.HoldingFlank;
                    }
                    break;
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

            ___formation.SetMovementOrder(____currentOrder);
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
        static bool PrefixCalculateCurrentOrder(ref Formation ___formation, ref MovementOrder ____chargeOrder)
        {
            ___formation.AI.SetBehaviorWeight<BehaviorCharge>(0f);
            if (___formation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                ____chargeOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        static void PostfixCalculateCurrentOrder( ref MovementOrder ____attackEntityOrderOuterGate, ref ArrangementOrder ___CurrentArrangementOrder, ref MovementOrder ____chargeOrder, ref TeamAISiegeComponent ____teamAISiegeComponent, ref Formation ___formation, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviourState, ref MovementOrder ____attackEntityOrderInnerGate)
        {

            //____attackEntityOrderInnerGate = MovementOrder.MovementOrderAttackEntity(____teamAISiegeComponent.InnerGate.GameEntity, surroundEntity: false);
            switch (____behaviourState)
            {
                case BehaviorState.ClimbWall:
                    {
                        if (___formation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                        {
                            ____currentOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                        }
                        break;
                    }
                case BehaviorState.AttackEntity:
                    {
                        MethodInfo method = typeof(Formation).GetMethod("FormAttackEntityDetachment", BindingFlags.NonPublic | BindingFlags.Instance);
                        method.DeclaringType.GetMethod("FormAttackEntityDetachment");
                        if (____attackEntityOrderInnerGate.TargetEntity != null)
                        {
                            method.Invoke(___formation, new object[] { ____attackEntityOrderInnerGate.TargetEntity });
                        }

                        //___CurrentArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
                        break;
                    }
                case BehaviorState.Charging:
                case BehaviorState.TakeControl:
                    {
                        
                        if(___formation.AI.Side == BehaviorSide.Middle)
                        {
                            MethodInfo method = typeof(Formation).GetMethod("DisbandAttackEntityDetachment", BindingFlags.NonPublic | BindingFlags.Instance);
                            method.DeclaringType.GetMethod("DisbandAttackEntityDetachment");
                            method.Invoke(___formation, new object[] { });

                            FieldInfo field = typeof(Formation).GetField("_detachments", BindingFlags.NonPublic | BindingFlags.Instance);
                            field.DeclaringType.GetField("_detachments");
                            List<IDetachment> detachments = (List<IDetachment>)field.GetValue(___formation);

                            foreach (IDetachment detach in detachments.ToList())
                            {
                                MethodInfo method2 = typeof(Formation).GetMethod("LeaveDetachment", BindingFlags.NonPublic | BindingFlags.Instance);
                                method2.DeclaringType.GetMethod("LeaveDetachment");
                                method2.Invoke(___formation, new object[] { detach });
                            }

                        }

                        if (___formation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                        {
                            //____attackEntityOrderInnerGate = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            //____attackEntityOrderOuterGate = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            ____chargeOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            ____chargeOrder.TargetEntity = null;
                            ____currentOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
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
        static bool PrefixOnBehaviorActivatedAux(ref Formation ___formation, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder)
        {
            ___formation.SetMovementOrder(____currentOrder);
            ___formation.FacingOrder = ___CurrentFacingOrder;
            ___formation.ArrangementOrder = ArrangementOrder.ArrangementOrderScatter;
            ___formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
            ___formation.FormOrder = FormOrder.FormOrderCustom(40f);
            ___formation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        static bool PrefixTickOccasionally(ref Formation ___formation, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref TacticalPosition ____tacticalArcherPosition)
        {
            ___formation.SetMovementOrder(____currentOrder);
            ___formation.FacingOrder = ___CurrentFacingOrder;
            if (____tacticalArcherPosition != null)
            {
                PropertyInfo property = typeof(TacticalPosition).GetProperty("Width", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("Width");
                float Width = (float)property.GetValue(____tacticalArcherPosition, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                ___formation.FormOrder = FormOrder.FormOrderCustom(Width*5f);
            }
            return false;
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

        [HarmonyPrefix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        static bool PrefixOnBehaviorActivatedAux(ref BehaviorDefendCastleKeyPosition __instance, ref FacingOrder ____waitFacingOrder, ref FacingOrder ____readyFacingOrder, ref TeamAISiegeComponent ____teamAISiegeDefender, ref bool ____isDefendingWideGap, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ___behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref Formation ___formation, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviourState ____behaviourState)
        {
            MethodInfo method = typeof(BehaviorDefendCastleKeyPosition).GetMethod("ResetOrderPositions", BindingFlags.NonPublic | BindingFlags.Instance);
            method.DeclaringType.GetMethod("ResetOrderPositions");
            method.Invoke(__instance, new object[] { });

            ___formation.SetMovementOrder(____currentOrder);
            ___formation.FacingOrder = ___CurrentFacingOrder;
            ___formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
            ___formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
            //formation.FormOrder = FormOrder.FormOrderWide;
            ___formation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("ResetOrderPositions")]
        static bool PrefixResetOrderPositions(ref FacingOrder ____waitFacingOrder, ref FacingOrder ____readyFacingOrder, ref TeamAISiegeComponent ____teamAISiegeDefender, ref bool ____isDefendingWideGap, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ___behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref Formation ___formation, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviourState ____behaviourState)
        {
            ___behaviorSide = ___formation.AI.Side;
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
                PropertyInfo property = typeof(TacticalPosition).GetProperty("Direction", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("Direction");
                Vec2 direction = (Vec2)property.GetValue(____tacticalMiddlePos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                ____readyFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);

                PropertyInfo property2 = typeof(TacticalPosition).GetProperty("Position", BindingFlags.NonPublic | BindingFlags.Instance);
                property2.DeclaringType.GetProperty("Position");
                WorldPosition position = (WorldPosition)property2.GetValue(____tacticalMiddlePos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                ____readyOrder = MovementOrder.MovementOrderMove(position);
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
                PropertyInfo property = typeof(TacticalPosition).GetProperty("Direction", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("Direction");
                Vec2 direction = (Vec2)property.GetValue(____tacticalWaitPos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                ____waitFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);

                PropertyInfo property2 = typeof(TacticalPosition).GetProperty("Position", BindingFlags.NonPublic | BindingFlags.Instance);
                property2.DeclaringType.GetProperty("Position");
                WorldPosition position = (WorldPosition)property2.GetValue(____tacticalWaitPos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                ____waitOrder = MovementOrder.MovementOrderMove(position);
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

            if (____behaviourState == BehaviourState.Ready && ____tacticalMiddlePos != null)
            {
                PropertyInfo property = typeof(TacticalPosition).GetProperty("Width", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("Width");
                float Width = (float)property.GetValue(____tacticalMiddlePos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                ___formation.FormOrder = FormOrder.FormOrderCustom(Width * 2f);
            }
            else if (____behaviourState == BehaviourState.Waiting && ____tacticalWaitPos != null)
            {
                PropertyInfo property = typeof(TacticalPosition).GetProperty("Width", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("Width");
                float Width = (float)property.GetValue(____tacticalWaitPos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                ___formation.FormOrder = FormOrder.FormOrderCustom(Width * 2f);
            }
            else
            {
                //___formation.FormOrder = FormOrder.FormOrderCustom(30f);
            }

             ____currentOrder = ((____behaviourState == BehaviourState.Ready) ? ____readyOrder : ____waitOrder);
            ___CurrentFacingOrder = ((___formation.QuerySystem.ClosestEnemyFormation != null && TeamAISiegeComponent.IsFormationInsideCastle(___formation.QuerySystem.ClosestEnemyFormation.Formation, includeOnlyPositionedUnits: true)) ? FacingOrder.FacingOrderLookAtEnemy : ((____behaviourState == BehaviourState.Ready) ? ____readyFacingOrder : ____waitFacingOrder));
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch("ResetOrderPositions")]
        static void PostfixResetOrderPositions(ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref Formation ___formation, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviourState ____behaviourState)
        {
            ____laddersOnThisSide.Clear();
            if (____tacticalMiddlePos != null)
            {
                if (___formation != null && ___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                {
                    if (____innerGate == null)
                    {
                        if (____outerGate != null)
                        {
                            float distance = ___formation.QuerySystem.MedianPosition.AsVec2.Distance(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
                            if ((____outerGate.IsDestroyed || ____outerGate.IsGateOpen) && TeamAISiegeComponent.IsFormationInsideCastle(___formation, includeOnlyPositionedUnits: false) && distance < 35f)
                            {
                                ____readyOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                                ____currentOrder = ____readyOrder;
                            }
                        }
                    }
                    else
                    {
                        float distance = ___formation.QuerySystem.MedianPosition.AsVec2.Distance(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
                        if ((____innerGate.IsDestroyed || ____innerGate.IsGateOpen) && TeamAISiegeComponent.IsFormationInsideCastle(___formation, includeOnlyPositionedUnits: false) && distance < 35f)
                        {
                            ____readyOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            ____currentOrder = ____readyOrder;
                        }
                    }
                }


                if (____innerGate != null && !____innerGate.IsDestroyed)
                {
                    PropertyInfo property = typeof(TacticalPosition).GetProperty("Position", BindingFlags.NonPublic | BindingFlags.Instance);
                    property.DeclaringType.GetProperty("Position");
                    WorldPosition position = (WorldPosition)property.GetValue(____tacticalMiddlePos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                    if (____behaviourState == BehaviourState.Ready)
                    {
                        Vec2 direction = (____innerGate.GetPosition().AsVec2 - ___formation.QuerySystem.MedianPosition.AsVec2).Normalized();
                        WorldPosition newPosition = position;
                        newPosition.SetVec2(position.AsVec2 - direction * 2f);
                        ____readyOrder = MovementOrder.MovementOrderMove(newPosition);
                        ____currentOrder = ____readyOrder;
                    }
                }
            }

            if (___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && ____tacticalMiddlePos != null && ____innerGate == null && ____outerGate == null)
            {
                PropertyInfo property = typeof(TacticalPosition).GetProperty("Position", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("Position");
                WorldPosition position = (WorldPosition)property.GetValue(____tacticalMiddlePos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                Formation correctEnemy = Utilities.FindSignificantEnemyToPosition(___formation, position, true, false, false, false, false, true);
                if(correctEnemy != null)
                {
                    if (TeamAISiegeComponent.IsFormationInsideCastle(correctEnemy, includeOnlyPositionedUnits: false, 0.01f))
                    {
                        ____readyOrder = MovementOrder.MovementOrderChargeToTarget(correctEnemy);
                        ____currentOrder = ____readyOrder;
                    }
                }
            }

            if (___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && ____tacticalWaitPos != null && ____tacticalMiddlePos == null)
            {
                float distance = ___formation.QuerySystem.MedianPosition.AsVec2.Distance(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
                if (TeamAISiegeComponent.IsFormationInsideCastle(___formation, includeOnlyPositionedUnits: false) && distance < 35f)
                {
                    ____readyOrder = MovementOrder.MovementOrderChargeToTarget(___formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                    ____currentOrder = ____readyOrder;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        static bool PrefixTickOccasionally(ref FacingOrder ____readyFacingOrder, ref FacingOrder ____waitFacingOrder, ref BehaviorDefendCastleKeyPosition __instance, ref bool ____isInShieldWallDistance, ref TeamAISiegeComponent ____teamAISiegeDefender, ref bool ____isDefendingWideGap, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ___behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref Formation ___formation, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviourState ____behaviourState)
        {
            IEnumerable<SiegeWeapon> source = from sw in Mission.Current.ActiveMissionObjects.FindAllWithType<SiegeWeapon>()
                                              where sw is IPrimarySiegeWeapon && (((sw as IPrimarySiegeWeapon).WeaponSide == FormationAI.BehaviorSide.Middle && !(sw as IPrimarySiegeWeapon).HoldLadders) || (sw as IPrimarySiegeWeapon).WeaponSide != FormationAI.BehaviorSide.Middle && (sw as IPrimarySiegeWeapon).SendLadders)
                                              //where sw is IPrimarySiegeWeapon
                                              select sw;

            BehaviourState behaviourState = ____teamAISiegeDefender == null || !source.Any() ? BehaviourState.Waiting : BehaviourState.Ready;
            if (behaviourState != ____behaviourState)
            {
                ____behaviourState = behaviourState;
                ____currentOrder = ((____behaviourState == BehaviourState.Ready) ? ____readyOrder : ____waitOrder);
                ___CurrentFacingOrder = ((____behaviourState == BehaviourState.Ready) ? ____readyFacingOrder : ____waitFacingOrder);
            }
            if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.Siege)
            {
                if (____outerGate != null && ____outerGate.State == CastleGate.GateState.Open && !____outerGate.IsDestroyed)
                {
                    if (!___formation.IsUsingMachine(____outerGate))
                    {
                        ___formation.StartUsingMachine(____outerGate);
                    }
                }
                else if (____innerGate != null && ____innerGate.State == CastleGate.GateState.Open && !____innerGate.IsDestroyed && !___formation.IsUsingMachine(____innerGate))
                {
                    ___formation.StartUsingMachine(____innerGate);
                }
            }

            MethodInfo method = typeof(BehaviorDefendCastleKeyPosition).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
            method.DeclaringType.GetMethod("CalculateCurrentOrder");
            method.Invoke(__instance, new object[] { });

            ___formation.SetMovementOrder(____currentOrder);
            ___formation.FacingOrder = ___CurrentFacingOrder;
            if (____behaviourState == BehaviourState.Ready && ____tacticalMiddlePos != null)
            {
                PropertyInfo property = typeof(TacticalPosition).GetProperty("Width", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("Width");
                float Width = (float)property.GetValue(____tacticalMiddlePos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                ___formation.FormOrder = FormOrder.FormOrderCustom(Width * 2f);
            }
            else if (____behaviourState == BehaviourState.Waiting && ____tacticalWaitPos != null)
            {
                PropertyInfo property = typeof(TacticalPosition).GetProperty("Width", BindingFlags.NonPublic | BindingFlags.Instance);
                property.DeclaringType.GetProperty("Width");
                float Width = (float)property.GetValue(____tacticalWaitPos, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
                ___formation.FormOrder = FormOrder.FormOrderCustom(Width * 2f);
            }
            bool flag = ____isDefendingWideGap && ____behaviourState == BehaviourState.Ready && ___formation.QuerySystem.ClosestEnemyFormation != null && (___formation.QuerySystem.IsUnderRangedAttack || ___formation.QuerySystem.AveragePosition.DistanceSquared(____currentOrder.GetPosition(___formation).AsVec2) < 25f + (____isInShieldWallDistance ? 75f : 0f));
            if (flag == ____isInShieldWallDistance)
            {
                return false;
            }
            ____isInShieldWallDistance = flag;
            if (____isInShieldWallDistance && ___formation.QuerySystem.HasShield)
            {
                if (___formation.ArrangementOrder != ArrangementOrder.ArrangementOrderLine)
                {
                    ___formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                }
            }
            else if (___formation.ArrangementOrder == ArrangementOrder.ArrangementOrderLine)
            {
                ___formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(LadderQueueManager))]
    class OverrideLadderQueueManager
    {

        [HarmonyPostfix]
        [HarmonyPatch("Initialize")]
        static void PostfixInitialize(ref BattleSideEnum managedSide, Vec3 managedDirection,  ref float queueBeginDistance, ref int ____maxUserCount, ref float ____agentSpacing, ref float ____queueBeginDistance, ref float ____queueRowSize, ref float ____costPerRow, ref float ____baseCost)
        {
            if(queueBeginDistance != 3f && ____maxUserCount > 1)
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

        [HarmonyPostfix]
        [HarmonyPatch("OnInit")]
        static void PostfixOnInit(ref SiegeTower __instance, ref GameEntity ____gameEntity, ref GameEntity ____cleanState, ref List<LadderQueueManager> ____queueManagers, ref int ___DynamicNavmeshIdStart)
        {
            __instance.ForcedUse = true;
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

                    ladderQueueManager0.Initialize(list2.ElementAt(0).GetScriptComponents<LadderQueueManager>().FirstOrDefault().ManagedNavigationFaceId, identity, new Vec3(0f, 0f, 1f), BattleSideEnum.Attacker, 3, (float)Math.PI * 3f / 4f, 7f, 1.1f, 30f, 50f, blockUsage: false, 1.1f, 4f, 5f);
                    ____queueManagers.Add(ladderQueueManager0);
                }
                if (ladderQueueManager1 != null)
                {
                    MatrixFrame identity = MatrixFrame.Identity;
                    identity.rotation.RotateAboutSide((float)Math.PI / 2f);
                    identity.rotation.RotateAboutForward((float)Math.PI / 8f);

                    ladderQueueManager1.Initialize(list2.ElementAt(1).GetScriptComponents<LadderQueueManager>().FirstOrDefault().ManagedNavigationFaceId, identity, new Vec3(0f, 0f, 1f), BattleSideEnum.Attacker, 3, (float)Math.PI * 3f / 4f, 7f, 1.1f, 30f, 50f, blockUsage: false, 1.1f, 4f, 5f);
                    ____queueManagers.Add(ladderQueueManager1);
                }
                if (ladderQueueManager2 != null)
                {
                    MatrixFrame identity = MatrixFrame.Identity;
                    identity.rotation.RotateAboutSide((float)Math.PI / 2f);
                    identity.rotation.RotateAboutForward((float)Math.PI / 8f);

                    ladderQueueManager2.Initialize(list2.ElementAt(2).GetScriptComponents<LadderQueueManager>().FirstOrDefault().ManagedNavigationFaceId, identity, new Vec3(0f, 0f, 1f), BattleSideEnum.Attacker, 3, (float)Math.PI * 3f / 4f, 7f, 1.1f, 30f, 50f, blockUsage: false, 1.1f, 4f, 5f);
                    ____queueManagers.Add(ladderQueueManager2);
                }
                foreach (LadderQueueManager queueManager in ____queueManagers)
                {
                    ____cleanState.Scene.SetAbilityOfFacesWithId(queueManager.ManagedNavigationFaceId, isEnabled: false);
                    queueManager.IsDeactivated = true;
                }
            }
            else if (list2.Count == 0)
            {
                ____queueManagers.Clear();
                LadderQueueManager ladderQueueManager2 = ____cleanState.GetScriptComponents<LadderQueueManager>().FirstOrDefault();
                if (ladderQueueManager2 != null)
                {
                    MatrixFrame identity2 = MatrixFrame.Identity;
                    identity2.origin.y += 4f;
                    identity2.rotation.RotateAboutSide(-(float)Math.PI / 2f);
                    identity2.rotation.RotateAboutUp((float)Math.PI);
                    ladderQueueManager2.Initialize(___DynamicNavmeshIdStart + 2, identity2, new Vec3(0f, -1f), BattleSideEnum.Attacker, 16, (float)Math.PI / 4f, 7f, 1.1f, 2f, 1f, blockUsage: false, 1.1f, 0f, 5f);
                    ____queueManagers.Add(ladderQueueManager2);
                }
                foreach (LadderQueueManager queueManager in ____queueManagers)
                {
                    ____cleanState.Scene.SetAbilityOfFacesWithId(queueManager.ManagedNavigationFaceId, isEnabled: false);
                    queueManager.IsDeactivated = true;
                }
        }
    }

        [HarmonyPostfix]
        [HarmonyPatch("OnDeploymentStateChanged")]
        static void PostfixDeploymentStateChanged(ref SiegeTower __instance, ref List<SiegeLadder> ____sameSideSiegeLadders, ref GameEntity ____cleanState, ref List<LadderQueueManager> ____queueManagers)
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
        static bool CalculateCurrentOrderPrefix(ref BehaviorTacticalCharge __instance, ref Vec2 ____initialChargeDirection,  ref FormationQuerySystem ____lastTarget,  ref Formation ___formation,
            ref ChargeState ____chargeState, ref Timer ____chargingPastTimer, ref Timer ____reformTimer, ref MovementOrder ____currentOrder, ref Vec2 ____bracePosition,
            ref float ____desiredChargeStopDistance, ref FacingOrder ___CurrentFacingOrder, ref WorldPosition ____lastReformDestination)
        {
            
            if (___formation.QuerySystem.ClosestEnemyFormation == null)
            {
                ____currentOrder = MovementOrder.MovementOrderCharge;
                return false;
            }

            //
            ____desiredChargeStopDistance = 120f;
            ChargeState result = ____chargeState;
            if (___formation.QuerySystem.ClosestEnemyFormation == null)
            {
                result = ChargeState.Undetermined;
            }
            else
            {
                switch (____chargeState)
                {
                    case ChargeState.Undetermined:
                        if (___formation.QuerySystem.ClosestEnemyFormation != null && ((!___formation.QuerySystem.IsCavalryFormation && !___formation.QuerySystem.IsRangedCavalryFormation) || ___formation.QuerySystem.AveragePosition.Distance(___formation.QuerySystem.ClosestEnemyFormation.MedianPosition.AsVec2) / ___formation.QuerySystem.MovementSpeedMaximum <= 5f))
                        {
                            result = ChargeState.Charging;
                        }
                        break;
                    case ChargeState.Charging:
                        if (!___formation.QuerySystem.IsCavalryFormation && !___formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            if (!___formation.QuerySystem.IsInfantryFormation || !___formation.QuerySystem.ClosestEnemyFormation.IsCavalryFormation)
                            {
                                result = ChargeState.Charging;
                                break;
                            }
                            Vec2 vec2 = ___formation.QuerySystem.AveragePosition - ___formation.QuerySystem.ClosestEnemyFormation.AveragePosition;
                            float num3 = vec2.Normalize();
                            Vec2 currentVelocity2 = ___formation.QuerySystem.ClosestEnemyFormation.CurrentVelocity;
                            float num4 = currentVelocity2.Normalize();
                            if (num3 / num4 <= 6f && vec2.DotProduct(currentVelocity2) > 0.5f)
                            {
                                ____chargeState = ChargeState.Bracing;
                            }
                        }
                        else if (____initialChargeDirection.DotProduct(___formation.QuerySystem.ClosestEnemyFormation.MedianPosition.AsVec2 - ___formation.QuerySystem.AveragePosition) <= 1f)
                        {
                            result = ChargeState.ChargingPast;
                        }
                        break;
                    case ChargeState.ChargingPast:
                        if (____chargingPastTimer.Check(MBCommon.GetTime(MBCommon.TimeType.Mission)))
                        {
                            result = ChargeState.Reforming;
                        }
                        break;
                    case ChargeState.Reforming:
                        if (____reformTimer.Check(MBCommon.GetTime(MBCommon.TimeType.Mission)) )
                        {
                            result = ChargeState.Charging;
                        }
                        break;
                    case ChargeState.Bracing:
                        {
                            bool flag = false;
                            if (___formation.QuerySystem.IsInfantryFormation && ___formation.QuerySystem.ClosestEnemyFormation.IsCavalryFormation)
                            {
                                Vec2 vec = ___formation.QuerySystem.AveragePosition - ___formation.QuerySystem.ClosestEnemyFormation.AveragePosition;
                                float num = vec.Normalize();
                                Vec2 currentVelocity = ___formation.QuerySystem.ClosestEnemyFormation.CurrentVelocity;
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
                        ____lastTarget = ___formation.QuerySystem.ClosestEnemyFormation;
                        if (___formation.QuerySystem.IsCavalryFormation || ___formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            ____initialChargeDirection = ____lastTarget.MedianPosition.AsVec2 - ___formation.QuerySystem.AveragePosition;
                            float value = ____initialChargeDirection.Normalize();
                            ____desiredChargeStopDistance = 120f;
                        }
                        break;
                    case ChargeState.ChargingPast:
                        ____chargingPastTimer = new Timer(MBCommon.GetTime(MBCommon.TimeType.Mission), 14f);
                        break;
                    case ChargeState.Reforming:
                        ____reformTimer = new Timer(MBCommon.GetTime(MBCommon.TimeType.Mission), 10f);
                        break;
                    case ChargeState.Bracing:
                        {
                            Vec2 vec = (___formation.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - ___formation.QuerySystem.AveragePosition).Normalized();
                            ____bracePosition = ___formation.QuerySystem.AveragePosition + vec * 5f;
                            break;
                        }
                }
            }

            switch (____chargeState)
            {
                case ChargeState.Undetermined:
                    if (___formation.QuerySystem.ClosestEnemyFormation != null && (___formation.QuerySystem.IsCavalryFormation || ___formation.QuerySystem.IsRangedCavalryFormation))
                    {
                        ____currentOrder = MovementOrder.MovementOrderMove(___formation.QuerySystem.ClosestEnemyFormation.MedianPosition);
                    }
                    else
                    {
                        ____currentOrder = MovementOrder.MovementOrderCharge;
                    }
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                    break;
                case ChargeState.Charging:
                    {
                        if (!___formation.QuerySystem.IsCavalryFormation && !___formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            WorldPosition medianPosition2 = ___formation.QuerySystem.ClosestEnemyFormation.MedianPosition;
                            ____currentOrder = MovementOrder.MovementOrderMove(medianPosition2);
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                            break;
                        }
                        Vec2 vec4 = (____lastTarget.MedianPosition.AsVec2 - ___formation.QuerySystem.AveragePosition).Normalized();
                        WorldPosition medianPosition3 = ____lastTarget.MedianPosition;
                        Vec2 vec5 = medianPosition3.AsVec2 + vec4 * ____desiredChargeStopDistance;
                        medianPosition3.SetVec2(vec5);
                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition3);
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec4);
                        break;
                    }
                case ChargeState.ChargingPast:
                    {
                        Vec2 vec2 = ___formation.QuerySystem.AveragePosition - ____lastTarget.MedianPosition.AsVec2;
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
                        WorldPosition medianPosition = ___formation.QuerySystem.MedianPosition;
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
        static bool PrefixGetFormationFrame(ref bool __result,ref Agent ___Agent, ref HumanAIComponent __instance, ref WorldPosition formationPosition, ref Vec2 formationDirection, ref float speedLimit, ref bool isSettingDestinationSpeed, ref bool limitIsMultiplier)
        {
            if(___Agent != null)
            {
                var formation = ___Agent.Formation;
                if (!___Agent.IsMount && formation != null && (formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && !(bool)IsUnitDetachedForDebug.Invoke(formation, new object[] { ___Agent }))
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
                        if(Utilities.GetHarnessTier(unit) > 3)
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
            if(__instance != null)
            {
                Formation formation = __instance.Formation;
                if(formation != null)
                {
                    if ((formation.QuerySystem.IsInfantryFormation ||  formation.QuerySystem.IsRangedFormation) && (formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget))
                    {
                        formations = Utilities.FindSignificantFormations(formation);
                        if(formations.Count > 0)
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
    }

    [HarmonyPatch(typeof(Formation))]
    class OverrideFormation
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetOrderPositionOfUnit")]
        static bool PrefixGetOrderPositionOfUnit(Formation __instance, ref WorldPosition ____orderPosition, ref IFormationArrangement ____arrangement, ref Agent unit, List<Agent> ___detachedUnits, ref WorldPosition __result)
        {
            //if (__instance.MovementOrder.OrderType == OrderType.ChargeWithTarget && __instance.QuerySystem.IsInfantryFormation && !___detachedUnits.Contains(unit))
            if (unit != null && (__instance.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || __instance.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge) && (__instance.QuerySystem.IsInfantryFormation || __instance.QuerySystem.IsRangedFormation || __instance.QuerySystem.IsCavalryFormation) && !___detachedUnits.Contains(unit))
            {
                Formation significantEnemy = __instance.TargetFormation;
                if (significantEnemy != null)
                {
                    var targetAgent = unit.GetTargetAgent();
                    Mission mission = Mission.Current;
                    if (targetAgent != null)
                    {
                        Vec2 lookDirection = unit.LookDirection.AsVec2;
                        Vec2 unitPosition = unit.GetWorldPosition().AsVec2;

                        float distance = unitPosition.Distance(targetAgent.GetWorldPosition().AsVec2);
                        if (distance > 25f)
                        {
                            __result = targetAgent.GetWorldPosition();
                            return false;
                        }

                        Vec2 direction = (targetAgent.GetWorldPosition().AsVec2 - unitPosition).Normalized();
                        IEnumerable<Agent> agents = mission.GetNearbyAllyAgents(unitPosition + lookDirection * 0.8f, 1f, unit.Team);
                        if (agents.Count() > 3)
                        {
                            unit.LookDirection = direction.ToVec3();
                            if (MBRandom.RandomInt(50) == 0)
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
                                    IEnumerable<Agent> agentsLeft = mission.GetNearbyAllyAgents(unitPosition + lookDirection.LeftVec() * 0.8f, 1f, unit.Team);
                                    IEnumerable<Agent> agentsRight = mission.GetNearbyAllyAgents(unitPosition + lookDirection.RightVec() * 0.8f, 1f, unit.Team);
                                    if (agentsLeft.Count() > 3 && agentsRight.Count() > 3)
                                    {
                                        if (MBRandom.RandomInt(50) == 0)
                                        {
                                            if (MBRandom.RandomInt(2) == 0)
                                            {
                                                WorldPosition leftPosition = unit.GetWorldPosition();
                                                leftPosition.SetVec2(unitPosition + lookDirection.LeftVec());
                                                __result = leftPosition;
                                                return false;
                                            }
                                            else
                                            {
                                                WorldPosition rightPosition = unit.GetWorldPosition();
                                                rightPosition.SetVec2(unitPosition + lookDirection.RightVec());
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
                                            leftPosition.SetVec2(unitPosition + lookDirection.LeftVec());
                                            __result = leftPosition;
                                            return false;
                                        }
                                        else
                                        {
                                            WorldPosition rightPosition = unit.GetWorldPosition();
                                            rightPosition.SetVec2(unitPosition + lookDirection.RightVec());
                                            __result = rightPosition;
                                            return false;
                                        }
                                    }
                                    else if (agentsLeft.Count() <= 3)
                                    {
                                        WorldPosition leftPosition = unit.GetWorldPosition();
                                        leftPosition.SetVec2(unitPosition + lookDirection.LeftVec());
                                        __result = leftPosition;
                                        return false;
                                    }
                                    else if (agentsRight.Count() <= 3)
                                    {
                                        WorldPosition rightPosition = unit.GetWorldPosition();
                                        rightPosition.SetVec2(unitPosition + lookDirection.RightVec());
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
            //Mission.Current.IsFieldBattle &&
            if (Mission.Current.IsFieldBattle && unit != null && (__instance.QuerySystem.IsInfantryFormation || __instance.QuerySystem.IsRangedFormation || __instance.QuerySystem.IsCavalryFormation) && (__instance.AI != null || __instance.IsAIControlled == false) && __instance.AI.ActiveBehavior != null )
            {
                //InformationManager.DisplayMessage(new InformationMessage(__instance.AI.ActiveBehavior.GetType().Name + " " + __instance.MovementOrder.OrderType.ToString()));
                bool exludedWhenAiControl = !(__instance.IsAIControlled && (__instance.AI.ActiveBehavior.GetType().Name.Contains("Regroup") || __instance.AI.ActiveBehavior.GetType().Name.Contains("Advance")));
                bool exludedWhenPlayerControl = !(!__instance.IsAIControlled && (__instance.GetReadonlyMovementOrderReference().OrderType.ToString().Contains("Advance")));

                if (exludedWhenAiControl && exludedWhenPlayerControl && !___detachedUnits.Contains(unit))
                {
                    Mission mission = Mission.Current;
                    if (mission.Mode != MissionMode.Deployment)
                    {
                        Vec2 lookDirection = unit.LookDirection.AsVec2;
                        Vec2 unitPosition = unit.GetWorldPosition().AsVec2;

                        IEnumerable<Agent> agents = mission.GetNearbyAllyAgents(unitPosition + lookDirection * 0.8f, 1f, unit.Team);
                        if (agents.Count() > 3)
                        {
                            if (MBRandom.RandomInt(50) == 0)
                            {
                                return true;
                            }
                            else
                            {
                                
                                if (unit != null)
                                {
                                    IEnumerable<Agent> agentsLeft = mission.GetNearbyAllyAgents(unitPosition + lookDirection.LeftVec() * 0.8f, 1f, unit.Team);
                                    IEnumerable<Agent> agentsRight = mission.GetNearbyAllyAgents(unitPosition + lookDirection.RightVec() * 0.8f, 1f, unit.Team);
                                    if (agentsLeft.Count() > 3 && agentsRight.Count() > 3)
                                    {
                                        if (MBRandom.RandomInt(50) == 0)
                                        {
                                            if (MBRandom.RandomInt(2) == 0)
                                            {
                                                WorldPosition leftPosition = unit.GetWorldPosition();
                                                leftPosition.SetVec2(unitPosition + lookDirection.LeftVec());
                                                __result = leftPosition;
                                                return false;
                                            }
                                            else
                                            {
                                                WorldPosition rightPosition = unit.GetWorldPosition();
                                                rightPosition.SetVec2(unitPosition + lookDirection.RightVec());
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
                                            leftPosition.SetVec2(unitPosition + lookDirection.LeftVec());
                                            __result = leftPosition;
                                            return false;
                                        }
                                        else
                                        {
                                            WorldPosition rightPosition = unit.GetWorldPosition();
                                            rightPosition.SetVec2(unitPosition + lookDirection.RightVec());
                                            __result = rightPosition;
                                            return false;
                                        }
                                    }
                                    else if (agentsLeft.Count() <= 3)
                                    {
                                        WorldPosition leftPosition = unit.GetWorldPosition();
                                        leftPosition.SetVec2(unitPosition + lookDirection.LeftVec());
                                        __result = leftPosition;
                                        return false;
                                    }
                                    else if (agentsRight.Count() <= 3)
                                    {
                                        WorldPosition rightPosition = unit.GetWorldPosition();
                                        rightPosition.SetVec2(unitPosition + lookDirection.RightVec());
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

            //[HarmonyPostfix]
            //[HarmonyPatch("OnBehaviorActivatedAux")]
            //static void PostfixOnBehaviorActivatedAux(ref Formation ___formation, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
            //{
            //    ___formation.FormOrder = FormOrder.FormOrderDeep;
            //}
        }

        //[HarmonyPatch(typeof(SiegeTower))]
        //class OverrideSiegeTower
        //{

        //    [HarmonyPostfix]
        //    [HarmonyPatch("OnTick")]
        //    static void PostfixOnTick(ref SiegeTower __instance, ref GameEntity ____cleanState, ref GateState ____state)
        //    {
        //        List<GameEntity> list2 = ____cleanState.CollectChildrenEntitiesWithTag("ladder");

        //        if (____state == GateState.Open)
        //        {
        //            FieldInfo property = typeof(SiegeTower).GetField("ActiveWaitStandingPoint", BindingFlags.NonPublic | BindingFlags.Instance);
        //            property.DeclaringType.GetField("ActiveWaitStandingPoint");
        //            GameEntity sp = (GameEntity)property.GetValue(__instance);

        //            PropertyInfo property2 = typeof(SiegeTower).GetProperty("WaitStandingPoints", BindingFlags.NonPublic | BindingFlags.Instance);
        //            property2.DeclaringType.GetProperty("WaitStandingPoints");
        //            List<GameEntity> WaitStandingPoints = (List<GameEntity>)property2.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);
        //            //WaitStandingPoints[0].SetLocalPosition(new Vec3(-100, -100, 0f));
        //            sp = WaitStandingPoints[0];
        //            property.SetValue(__instance, sp, BindingFlags.NonPublic | BindingFlags.SetField, null, null);
        //            //property2.SetValue(__instance, WaitStandingPoints, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
        //            //property.SetValue(__instance, WaitStandingPoints[0], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null);

        //            //Vec3 boundingBoxMin = list2.ElementAt(2).GetBoundingBoxMin();
        //            //Vec3 boundingBoxMax = list2.ElementAt(2).GetBoundingBoxMax();
        //            //Vec2 vec = (boundingBoxMax.AsVec2 + boundingBoxMin.AsVec2) * 0.5f;
        //            //Vec2 asVec = list2.ElementAt(2).GetGlobalFrame().TransformToParent(vec.ToVec3()).AsVec2;
        //            //foreach (Agent item in Mission.Current.GetAgentsInRange(asVec, 0.1f, true))
        //            //{
        //            //    Vec3 pos = item.Position;
        //            //    {
        //            //        int random = MBRandom.RandomInt(3);
        //            //        if (random == 0)
        //            //        {
        //            //            pos.y += 0.1f;
        //            //            pos.x += 0.1f;
        //            //            item.TeleportToPosition(pos);

        //            //        }
        //            //        else if (random == 1)
        //            //        {
        //            //            pos.y -= 0.1f;
        //            //            pos.x -= 0.1f;
        //            //            item.TeleportToPosition(pos);
        //            //        }
        //            //    }
        //            //}
        //        }
        //    }
        //}

        [HarmonyPatch(typeof(SiegeMissionTroopSpawnHandler))]
        class OverrideAfterStart
        {
            [HarmonyPrefix]
            [HarmonyPatch("AfterStart")]
            static bool PrefixAfterStart(ref MapEvent ____mapEvent,ref MissionAgentSpawnLogic ____missionAgentSpawnLogic)
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
                    
                    float defenderAdvantage = (float)battleSize / ((float)defenderInitialSpawn*((battleSize*2f)/(totalBattleSize)));
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
            static bool PrefixBattleSizeSpawnTick(ref MissionAgentSpawnLogic __instance, ref int ____battleSize,ref List<SpawnPhase>[] ____phases)
            {
                
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