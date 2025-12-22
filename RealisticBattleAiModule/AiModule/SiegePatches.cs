using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions.Handlers.Logic;

namespace RBMAI
{
    public static class SiegePatches
    {

        [HarmonyPatch(typeof(BehaviorAssaultWalls))]
        private class OverrideBehaviorAssaultWalls
        {
            [HarmonyPostfix]
            [HarmonyPatch("OnBehaviorActivatedAux")]
            private static void PostfixOnBehaviorActivatedAux(ref BehaviorShootFromCastleWalls __instance)
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderScatter);
                __instance.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            }


            [HarmonyPostfix]
            [HarmonyPatch("CalculateCurrentOrder")]
            private static void PostfixCalculateCurrentOrder(ref BehaviorAssaultWalls __instance, ref MovementOrder ____wallSegmentMoveOrder, ref MovementOrder ____attackEntityOrderOuterGate, ref ArrangementOrder ___CurrentArrangementOrder, ref MovementOrder ____chargeOrder, ref TeamAISiegeComponent ____teamAISiegeComponent, ref MovementOrder ____currentOrder, ref MovementOrder ____attackEntityOrderInnerGate)
            {
                ___CurrentArrangementOrder = ArrangementOrder.ArrangementOrderScatter;
            }
        }


        [HarmonyPatch(typeof(BehaviorShootFromCastleWalls))]
        private class OverrideBehaviorShootFromCastleWalls
        {
            [HarmonyPrefix]
            [HarmonyPatch("OnBehaviorActivatedAux")]
            private static bool PrefixOnBehaviorActivatedAux(ref BehaviorShootFromCastleWalls __instance, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder)
            {
                __instance.Formation.SetMovementOrder(____currentOrder);
                __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderScatter);
                __instance.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
                __instance.Formation.SetFormOrder(FormOrder.FormOrderWider);
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("TickOccasionally")]
            private static bool PrefixTickOccasionally(ref BehaviorShootFromCastleWalls __instance, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref TacticalPosition ____tacticalArcherPosition)
            {
                if (__instance.Formation.ArrangementOrder == ArrangementOrder.ArrangementOrderLine)
                {
                    __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderScatter);
                }
                __instance.Formation.SetMovementOrder(____currentOrder);
                __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
                if (____tacticalArcherPosition != null)
                {
                    __instance.Formation.SetFormOrder(FormOrder.FormOrderCustom(____tacticalArcherPosition.Width * 5f));
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(BehaviorUseSiegeMachines))]
        private class OverrideBehaviorUseSiegeMachines
        {
            [HarmonyPostfix]
            [HarmonyPatch("TickOccasionally")]
            private static void PrefixTickOccasionally(ref BehaviorUseSiegeMachines __instance)
            {
                //if (__instance.Formation.ArrangementOrder == ArrangementOrder.ArrangementOrderShieldWall || )
                //{
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderScatter);
                //}
            }
        }

        [HarmonyPatch(typeof(BehaviorWaitForLadders))]
        private class OverrideBehaviorWaitForLadders
        {
            [HarmonyPostfix]
            [HarmonyPatch("TickOccasionally")]
            private static void PrefixTickOccasionally(ref BehaviorWaitForLadders __instance)
            {
                //if (__instance.Formation.ArrangementOrder == ArrangementOrder.ArrangementOrderShieldWall)
                //{
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderScatter);
                //}
            }
        }

        //[HarmonyPatch(typeof(BehaviorDefendCastleKeyPosition))]
        //private class OverrideBehaviorDefendCastleKeyPosition
        //{
        //    private enum BehaviorState
        //    {
        //        UnSet,
        //        Waiting,
        //        Ready
        //    }

        //    [HarmonyPrefix]
        //    [HarmonyPatch("OnBehaviorActivatedAux")]
        //    private static bool PrefixOnBehaviorActivatedAux(ref BehaviorDefendCastleKeyPosition __instance, ref FacingOrder ____waitFacingOrder, ref FacingOrder ____readyFacingOrder, ref TeamAISiegeComponent ____teamAISiegeDefender, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ____behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
        //    {
        //        MethodInfo method = typeof(BehaviorDefendCastleKeyPosition).GetMethod("ResetOrderPositions", BindingFlags.NonPublic | BindingFlags.Instance);
        //        method.DeclaringType.GetMethod("ResetOrderPositions");
        //        method.Invoke(__instance, new object[] { });

        //        __instance.Formation.SetMovementOrder(____currentOrder);
        //        __instance.Formation.SetFacingOrder( ___CurrentFacingOrder);
        //        __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
        //        __instance.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
        //        //formation.FormOrder = FormOrder.FormOrderWide;
        //        return false;
        //    }

        //    [HarmonyPrefix]
        //    [HarmonyPatch("CalculateCurrentOrder")]
        //    private static bool PrefixCalculateCurrentOrder(ref BehaviorDefendCastleKeyPosition __instance, ref FacingOrder ____waitFacingOrder, ref FacingOrder ____readyFacingOrder, ref TeamAISiegeComponent ____teamAISiegeDefender, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ____behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
        //    {
        //        MethodInfo method = typeof(BehaviorDefendCastleKeyPosition).GetMethod("ResetOrderPositions", BindingFlags.NonPublic | BindingFlags.Instance);
        //        method.DeclaringType.GetMethod("ResetOrderPositions");
        //        method.Invoke(__instance, new object[] { });
        //        return true;
        //    }

        //    [HarmonyPostfix]
        //    [HarmonyPatch("ResetOrderPositions")]
        //    static void PostfixResetOrderPositions(ref BehaviorDefendCastleKeyPosition __instance, ref WorldPosition ____readyOrderPosition, ref TeamAISiegeComponent ____teamAISiegeDefender, ref FacingOrder ___CurrentFacingOrder, ref FacingOrder ____readyFacingOrder, ref FacingOrder ____waitFacingOrder, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, FormationAI.BehaviorSide ____behaviorSide, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
        //    {
        //        ____behaviorSide = __instance.Formation.AI.Side;
        //        ____innerGate = null;
        //        ____outerGate = null;
        //        ____laddersOnThisSide.Clear();
        //        WorldFrame worldFrame;
        //        WorldFrame worldFrame2;
        //        if (____teamAISiegeDefender.OuterGate.DefenseSide == ____behaviorSide)
        //        {
        //            CastleGate outerGate = ____teamAISiegeDefender.OuterGate;
        //            ____innerGate = ____teamAISiegeDefender.InnerGate;
        //            ____outerGate = ____teamAISiegeDefender.OuterGate;
        //            worldFrame = outerGate.MiddleFrame;
        //            worldFrame2 = outerGate.DefenseWaitFrame;
        //            ____tacticalMiddlePos = outerGate.MiddlePosition;
        //            ____tacticalWaitPos = outerGate.WaitPosition;
        //        }
        //        else
        //        {
        //            WallSegment wallSegment = ____teamAISiegeDefender.WallSegments.Where((WallSegment ws) => ws.DefenseSide == ____behaviorSide && ws.IsBreachedWall).FirstOrDefault();
        //            if (wallSegment != null)
        //            {
        //                worldFrame = wallSegment.MiddleFrame;
        //                worldFrame2 = wallSegment.DefenseWaitFrame;
        //                ____tacticalMiddlePos = wallSegment.MiddlePosition;
        //                ____tacticalWaitPos = wallSegment.WaitPosition;
        //            }
        //            else
        //            {
        //                IEnumerable<IPrimarySiegeWeapon> source = ____teamAISiegeDefender.PrimarySiegeWeapons.Where((IPrimarySiegeWeapon sw) => sw.WeaponSide == ____behaviorSide && ((sw is SiegeWeapon siegeWeapon && !siegeWeapon.IsDestroyed && !siegeWeapon.IsDeactivated) || sw.HasCompletedAction()));
        //                if (!source.Any())
        //                {
        //                    worldFrame = WorldFrame.Invalid;
        //                    worldFrame2 = WorldFrame.Invalid;
        //                    ____tacticalMiddlePos = null;
        //                    ____tacticalWaitPos = null;
        //                }
        //                else
        //                {
        //                    ____laddersOnThisSide = source.OfType<SiegeLadder>().ToList();
        //                    ICastleKeyPosition castleKeyPosition = source.FirstOrDefault().TargetCastlePosition as ICastleKeyPosition;
        //                    worldFrame = castleKeyPosition.MiddleFrame;
        //                    worldFrame2 = castleKeyPosition.DefenseWaitFrame;
        //                    ____tacticalMiddlePos = castleKeyPosition.MiddlePosition;
        //                    ____tacticalWaitPos = castleKeyPosition.WaitPosition;
        //                }
        //            }
        //        }
        //        if (____tacticalMiddlePos != null)
        //        {
        //            ____readyOrderPosition = ____tacticalMiddlePos.Position;
        //            ____readyOrder = MovementOrder.MovementOrderMove(____readyOrderPosition);
        //            ____readyFacingOrder = FacingOrder.FacingOrderLookAtDirection(____tacticalMiddlePos.Direction);
        //        }
        //        else if (worldFrame.Origin.IsValid)
        //        {
        //            worldFrame.Rotation.f.Normalize();
        //            ____readyOrderPosition = worldFrame.Origin;
        //            ____readyOrder = MovementOrder.MovementOrderMove(____readyOrderPosition);
        //            ____readyFacingOrder = FacingOrder.FacingOrderLookAtDirection(worldFrame.Rotation.f.AsVec2);
        //        }
        //        else
        //        {
        //            ____readyOrderPosition = WorldPosition.Invalid;
        //            ____readyOrder = MovementOrder.MovementOrderStop;
        //            ____readyFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        //        }
        //        if (____tacticalWaitPos != null)
        //        {
        //            ____waitOrder = MovementOrder.MovementOrderMove(____tacticalWaitPos.Position);
        //            ____waitFacingOrder = FacingOrder.FacingOrderLookAtDirection(____tacticalWaitPos.Direction);
        //        }
        //        else if (worldFrame2.Origin.IsValid)
        //        {
        //            worldFrame2.Rotation.f.Normalize();
        //            ____waitOrder = MovementOrder.MovementOrderMove(worldFrame2.Origin);
        //            ____waitFacingOrder = FacingOrder.FacingOrderLookAtDirection(worldFrame2.Rotation.f.AsVec2);
        //        }
        //        else
        //        {
        //            ____waitOrder = MovementOrder.MovementOrderStop;
        //            ____waitFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        //        }
        //        ____currentOrder = ((____behaviorState == BehaviorState.Ready) ? ____readyOrder : ____waitOrder);
        //        ___CurrentFacingOrder = ((__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: true)) ? FacingOrder.FacingOrderLookAtEnemy : ((____behaviorState == BehaviorState.Ready) ? ____readyFacingOrder : ____waitFacingOrder));

        //        if (____tacticalMiddlePos != null)
        //        {
        //            if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
        //            {
        //                if (____innerGate == null)
        //                {
        //                    if (____outerGate != null)
        //                    {
        //                        float distance = __instance.Formation.SmoothedAverageUnitPosition.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.SmoothedAverageUnitPosition);
        //                        if ((____outerGate.IsDestroyed || ____outerGate.IsGateOpen) && (TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f ||
        //                        TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false, 0.2f)))
        //                        {
        //                            ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
        //                            ____currentOrder = ____readyOrder;
        //                        }
        //                    }
        //                }
        //                else
        //                {
        //                    float distance = __instance.Formation.SmoothedAverageUnitPosition.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.SmoothedAverageUnitPosition);
        //                    if ((____innerGate != null && (____innerGate.IsDestroyed || ____innerGate.IsGateOpen)) && (TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f ||
        //                        TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false, 0.2f)))
        //                    {
        //                        ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
        //                        ____currentOrder = ____readyOrder;
        //                    }
        //                }
        //            }

        //            if (____innerGate != null && !____innerGate.IsDestroyed)
        //            {
        //                WorldPosition position = ____tacticalMiddlePos.Position;
        //                if (____behaviorState == BehaviorState.Ready)
        //                {
        //                    Vec2 direction = (____innerGate.GetPosition().AsVec2 - __instance.Formation.CachedMedianPosition.AsVec2).Normalized();
        //                    WorldPosition newPosition = position;
        //                    newPosition.SetVec2(position.AsVec2 - direction * 2f);
        //                    ____readyOrder = MovementOrder.MovementOrderMove(newPosition);
        //                    ____currentOrder = ____readyOrder;
        //                }
        //            }
        //        }

        //        if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && ____tacticalMiddlePos != null && ____innerGate == null && ____outerGate == null)
        //        {
        //            WorldPosition position = ____tacticalMiddlePos.Position;
        //            Formation correctEnemy = RBMAI.Utilities.FindSignificantEnemyToPosition(__instance.Formation, position, true, false, false, false, false, true);
        //            if (correctEnemy != null)
        //            {
        //                float distance = __instance.Formation.CachedMedianPosition.AsVec2.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.CachedMedianPosition.AsVec2);
        //                if (TeamAISiegeComponent.IsFormationInsideCastle(correctEnemy, includeOnlyPositionedUnits: false, 0.2f) || (TeamAISiegeComponent.IsFormationInsideCastle(correctEnemy, includeOnlyPositionedUnits: false, 0.05f) && TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f))
        //                {
        //                    ____readyOrder = MovementOrder.MovementOrderChargeToTarget(correctEnemy);
        //                    ____waitOrder = MovementOrder.MovementOrderChargeToTarget(correctEnemy);
        //                    ____currentOrder = ____readyOrder;
        //                }
        //            }
        //        }

        //        if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && ____tacticalWaitPos != null && ____tacticalMiddlePos == null)
        //        {
        //            float distance = __instance.Formation.CachedMedianPosition.AsVec2.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.CachedAveragePosition);
        //            if ((____innerGate != null && (____innerGate.IsDestroyed || ____innerGate.IsGateOpen)) && (TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f ||
        //                        TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false, 0.2f)))
        //            {
        //                ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
        //                ____currentOrder = ____readyOrder;
        //            }
        //        }

        //    }

        //    [HarmonyPrefix]
        //    [HarmonyPatch("TickOccasionally")]
        //    static bool PrefixTickOccasionally(ref FacingOrder ____readyFacingOrder, ref FacingOrder ____waitFacingOrder, ref BehaviorDefendCastleKeyPosition __instance, ref TeamAISiegeComponent ____teamAISiegeDefender, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ____behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
        //    {
        //        IEnumerable<SiegeWeapon> source = from sw in Mission.Current.ActiveMissionObjects.FindAllWithType<SiegeWeapon>()
        //                                          where sw is IPrimarySiegeWeapon && (((sw as IPrimarySiegeWeapon).WeaponSide == FormationAI.BehaviorSide.Middle && !(sw as IPrimarySiegeWeapon).HoldLadders) || (sw as IPrimarySiegeWeapon).WeaponSide != FormationAI.BehaviorSide.Middle && (sw as IPrimarySiegeWeapon).SendLadders)
        //                                          //where sw is IPrimarySiegeWeapon
        //                                          select sw;

        //        BehaviorState BehaviorState = ____teamAISiegeDefender == null || !source.Any() ? BehaviorState.Waiting : BehaviorState.Ready;
        //        if (BehaviorState != ____behaviorState)
        //        {
        //            ____behaviorState = BehaviorState;
        //            ____currentOrder = ((____behaviorState == BehaviorState.Ready) ? ____readyOrder : ____waitOrder);
        //            ___CurrentFacingOrder = ((____behaviorState == BehaviorState.Ready) ? ____readyFacingOrder : ____waitFacingOrder);
        //        }
        //        if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.Siege)
        //        {
        //            if (____outerGate != null && ____outerGate.State == CastleGate.GateState.Open && !____outerGate.IsDestroyed)
        //            {
        //                if (!____outerGate.IsUsedByFormation(__instance.Formation))
        //                {
        //                    __instance.Formation.StartUsingMachine(____outerGate);
        //                }
        //            }
        //            else if (____innerGate != null && ____innerGate.State == CastleGate.GateState.Open && !____innerGate.IsDestroyed && !____innerGate.IsUsedByFormation(__instance.Formation))
        //            {
        //                __instance.Formation.StartUsingMachine(____innerGate);
        //            }
        //        }

        //        MethodInfo method = typeof(BehaviorDefendCastleKeyPosition).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
        //        method.DeclaringType.GetMethod("CalculateCurrentOrder");
        //        method.Invoke(__instance, new object[] { });

        //        __instance.Formation.SetMovementOrder(____currentOrder);
        //        __instance.Formation.SetFacingOrder( ___CurrentFacingOrder);
        //        if (____behaviorState == BehaviorState.Ready && ____tacticalMiddlePos != null)
        //        {
        //            __instance.Formation.SetFormOrder( FormOrder.FormOrderCustom(____tacticalMiddlePos.Width * 2f));
        //        }
        //        else if (____behaviorState == BehaviorState.Waiting && ____tacticalWaitPos != null)
        //        {
        //            __instance.Formation.SetFormOrder( FormOrder.FormOrderCustom(____tacticalWaitPos.Width * 2f));
        //        }
        //        __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
        //        return false;
        //    }
        //}


        [HarmonyPatch(typeof(SiegeTower))]
        private class OverrideSiegeTower
        {
            [HarmonyPostfix]
            [HarmonyPatch("OnTick")]
            private static void PostfixOnTick(ref List<LadderQueueManager> ____queueManagers)
            {
                if (____queueManagers != null)
                {
                    foreach (var queue in ____queueManagers)
                    {
                        FieldInfo _agentSpacing = typeof(LadderQueueManager).GetField("_agentSpacing", BindingFlags.NonPublic | BindingFlags.Instance);
                        _agentSpacing.DeclaringType.GetField("_agentSpacing");
                        _agentSpacing.SetValue(queue, 1.5f);

                        FieldInfo _queueBeginDistance = typeof(LadderQueueManager).GetField("_queueBeginDistance", BindingFlags.NonPublic | BindingFlags.Instance);
                        _queueBeginDistance.DeclaringType.GetField("_queueBeginDistance");
                        _queueBeginDistance.SetValue(queue, 3f);

                        FieldInfo _queueRowSize = typeof(LadderQueueManager).GetField("_queueRowSize", BindingFlags.NonPublic | BindingFlags.Instance);
                        _queueRowSize.DeclaringType.GetField("_queueRowSize");
                        _queueRowSize.SetValue(queue, 1.5f);
                    }

                }
            }
        }

        [HarmonyPatch(typeof(SiegeLadder))]
        private class OverrideSiegeLadder
        {
            [HarmonyPrefix]
            [HarmonyPatch("IsDisabledForBattleSide")]
            private static bool PrefixIsDisabledForBattleSide(BattleSideEnum sideEnum, ref bool __result, LadderQueueManager ____queueManagerForAttackers)
            {
                if (____queueManagerForAttackers != null)
                {

                    FieldInfo _agentSpacing = typeof(LadderQueueManager).GetField("_agentSpacing", BindingFlags.NonPublic | BindingFlags.Instance);
                    _agentSpacing.DeclaringType.GetField("_agentSpacing");
                    _agentSpacing.SetValue(____queueManagerForAttackers, 1.5f);

                    FieldInfo _queueBeginDistance = typeof(LadderQueueManager).GetField("_queueBeginDistance", BindingFlags.NonPublic | BindingFlags.Instance);
                    _queueBeginDistance.DeclaringType.GetField("_queueBeginDistance");
                    _queueBeginDistance.SetValue(____queueManagerForAttackers, 3f);

                    FieldInfo _queueRowSize = typeof(LadderQueueManager).GetField("_queueRowSize", BindingFlags.NonPublic | BindingFlags.Instance);
                    _queueRowSize.DeclaringType.GetField("_queueRowSize");
                    _queueRowSize.SetValue(____queueManagerForAttackers, 1.5f);
                }

                if (sideEnum == BattleSideEnum.Defender)
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(AgentMoraleInteractionLogic))]
        private class AgentMoraleInteractionLogicPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("ApplyMoraleEffectOnAgentIncapacitated")]
            private static bool PrefixAfterStart(Agent affectedAgent, Agent affectorAgent, float affectedSideMaxMoraleLoss, float affectorSideMoraleMaxGain, float effectRadius)
            {
                if (affectedAgent != null)
                {
                    if (Mission.Current.IsSiegeBattle && affectedAgent.Team.IsDefender)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(TacticDefendCastle))]
        private class StopUsingStrategicAreasPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("StopUsingStrategicAreas")]
            private static bool Prefix()
            {
                return false;
            }
        }

        [HarmonyPatch(typeof(TacticComponent))]
        private class StopUsingAllMachinesPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("StopUsingAllMachines")]
            private static bool Prefix()
            {
                return false;
            }
        }

        [HarmonyPatch(typeof(TacticComponent))]
        private class StopUsingAllRangedSiegeWeaponsPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("StopUsingAllRangedSiegeWeapons")]
            private static bool Prefix()
            {
                return false;
            }
        }
    }
}