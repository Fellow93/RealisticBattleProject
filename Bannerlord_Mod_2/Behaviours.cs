using HarmonyLib;
using System;
using System.Reflection;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.ArrangementOrder;

namespace RealisticBattle
{
    class Behaviours
    {

        //private static void SetDefensiveArrangementMoveBehaviorValues(Agent unit)
        //{
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.GoToPos, 3f, 8f, 5f, 20f, 6f);
        //    unit.SetAIBehaviorValues(AISimpleBehaviorKind.Melee, 4f, 5f, 0f, 20f, 0f);
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

        //[HarmonyPatch(typeof(MovementOrder))]
        //class OverrideMovementOrder
        //{
        //    [HarmonyPostfix]
        //    [HarmonyPatch("OnApply")]
        //    static void PostfixOnApply(ArrangementOrder __instance, ref Formation formation)
        //    {
        //        if(__instance != null)
        //        {
        //            if (formation.ArrangementOrder.OrderEnum == ArrangementOrderEnum.ShieldWall || formation.ArrangementOrder.OrderEnum == ArrangementOrderEnum.Line)
        //            {
        //                //for (int i = 0; i < formation.CountOfUnits; i++)
        //                //{
        //                //    Agent agent = formation.GetUnitWithIndex(i);
        //                //    //agent
        //                //}
        //                formation.ApplyActionOnEachUnit(SetDefensiveArrangementMoveBehaviorValues);
        //            }
        //        }
        //    }

        //    [HarmonyPostfix]
        //    [HarmonyPatch("OnUnitJoinOrLeave")]
        //    static void PostfixOnUnitJoinOrLeave(ArrangementOrder __instance, ref Formation formation)
        //    {
        //        if (__instance != null)
        //        {
        //            if (formation.ArrangementOrder.OrderEnum == ArrangementOrderEnum.ShieldWall || formation.ArrangementOrder.OrderEnum == ArrangementOrderEnum.Line)
        //            {
        //                //for (int i = 0; i < formation.CountOfUnits; i++)
        //                //{
        //                //    Agent agent = formation.GetUnitWithIndex(i);
        //                //    //agent
        //                //}
        //                formation.ApplyActionOnEachUnit(SetDefensiveArrangementMoveBehaviorValues);
        //            }
        //        }
        //    }
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
                    MethodInfo method = typeof(FacingOrder).GetMethod("FacingOrderLookAtDirection", BindingFlags.NonPublic | BindingFlags.Static);
                    method.DeclaringType.GetMethod("FacingOrderLookAtDirection");
                    ___CurrentFacingOrder = (FacingOrder)method.Invoke(___CurrentFacingOrder, new object[] { ____mainFormation.Direction });
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
            static void PostfixCalculateCurrentOrder(Formation ___formation, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged)
            {
                if (___formation != null)
                {
                    FormationQuerySystem mainEnemyformation = ___formation?.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                    if (mainEnemyformation != null)
                    {
                        WorldPosition medianPositionNew = ___formation.QuerySystem.MedianPosition;
                        medianPositionNew.SetVec2(___formation.QuerySystem.AveragePosition);

                        Formation rangedFormation = null;
                        foreach (Formation formation in ___formation.Team.Formations)
                        {
                            if (formation.QuerySystem.IsRangedFormation)
                            {
                                rangedFormation = formation;
                            }
                        }
                        if (rangedFormation != null)
                        {
                            if (___formation.QuerySystem.MedianPosition.AsVec2.Distance(mainEnemyformation.MedianPosition.AsVec2) < (rangedFormation.QuerySystem.MissileRange + 50f))
                            {
                                ____currentOrder = MovementOrder.MovementOrderMove(medianPositionOld);
                                ___IsCurrentOrderChanged = true;
                            }
                            else
                            {
                                medianPositionOld = medianPositionNew;
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
            static void PostfixCalculateCurrentOrder(Formation ___formation, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged)
            {
                if (___formation != null)
                {
                    FormationQuerySystem mainEnemyformation = ___formation?.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                    if (mainEnemyformation != null)
                    {
                        WorldPosition medianPositionNew = ___formation.QuerySystem.MedianPosition;
                        medianPositionNew.SetVec2(___formation.QuerySystem.AveragePosition);

                        Formation rangedFormation = null;
                        foreach (Formation formation in ___formation.Team.Formations)
                        {
                            if (formation.QuerySystem.IsRangedFormation)
                            {
                                rangedFormation = formation;
                            }
                        }
                        if (rangedFormation != null)
                        {
                            if (___formation.QuerySystem.MedianPosition.AsVec2.Distance(mainEnemyformation.MedianPosition.AsVec2) < (rangedFormation.QuerySystem.MissileRange + 50f))
                            {
                                ____currentOrder = MovementOrder.MovementOrderMove(medianPositionOld);
                                ___IsCurrentOrderChanged = true;
                            }
                            else
                            {
                                medianPositionOld = medianPositionNew;
                            }
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
                    MethodInfo method = typeof(FacingOrder).GetMethod("FacingOrderLookAtDirection", BindingFlags.NonPublic | BindingFlags.Static);
                    method.DeclaringType.GetMethod("FacingOrderLookAtDirection");
                    ___CurrentFacingOrder = (FacingOrder)method.Invoke(___CurrentFacingOrder, new object[] { ____mainFormation.Direction });

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
        }
    }
}