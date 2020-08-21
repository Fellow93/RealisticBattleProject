using HarmonyLib;
using System;
using System.Reflection;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace RealisticBattle
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
                    MethodInfo method = typeof(FacingOrder).GetMethod("FacingOrderLookAtDirection", BindingFlags.NonPublic | BindingFlags.Static);
                    method.DeclaringType.GetMethod("FacingOrderLookAtDirection");
                    ___CurrentFacingOrder = (FacingOrder)method.Invoke(___CurrentFacingOrder, new object[] { ____mainFormation.Direction });
                }
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
                            if (___formation.QuerySystem.MedianPosition.AsVec2.Distance(mainEnemyformation.MedianPosition.AsVec2) < (rangedFormation.QuerySystem.MissileRange + 30f))
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
                            if (___formation.QuerySystem.MedianPosition.AsVec2.Distance(mainEnemyformation.MedianPosition.AsVec2) < (rangedFormation.QuerySystem.MissileRange + 30f))
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
                    medianPosition.SetVec2(medianPosition.AsVec2 - ____mainFormation.Direction * ((____mainFormation.Depth + ___formation.Depth) * 1.5f));
                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);

                }
            }
        }
    }
}