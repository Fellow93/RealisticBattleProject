using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions.Handlers.Logic;
using static TaleWorlds.MountAndBlade.FormationAI;

namespace RBMAI.AiModule
{
    internal class SiegePatches
    {
        public static Dictionary<Team, bool> carryOutDefenceEnabled = new Dictionary<Team, bool>();
        public static Dictionary<Team, bool> archersShiftAroundEnabled = new Dictionary<Team, bool>();
        public static Dictionary<Team, bool> balanceLaneDefendersEnabled = new Dictionary<Team, bool>();

        [HarmonyPatch(typeof(MissionCombatantsLogic))]
        [HarmonyPatch("EarlyStart")]
        public class TeamAiFieldBattle
        {
            public static void Postfix()
            {
                balanceLaneDefendersEnabled.Clear();
                archersShiftAroundEnabled.Clear();
                carryOutDefenceEnabled.Clear();
            }
        }

        [HarmonyPatch(typeof(TacticDefendCastle))]
        private class TacticDefendCastlePatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("CarryOutDefense")]
            private static bool PrefixCarryOutDefense(ref TacticDefendCastle __instance, ref bool doRangedJoinMelee)
            {
                if (Mission.Current.Mode != MissionMode.Deployment)
                {
                    //List<Formation> archerFormations = new List<Formation>();
                    //foreach (Formation formation in ___team.Formations.ToList())
                    //{
                    //    if (formation.QuerySystem.IsRangedFormation)
                    //    {
                    //        archerFormations.Add(formation);
                    //    }
                    //}
                    //if (archerFormations.Count > 1)
                    //{Team ___team
                    //    foreach (Formation archerFormation in archerFormations.ToList())
                    //    {
                    //        archerFormation.AI.ResetBehaviorWeights();
                    //        archerFormation.AI.SetBehaviorWeight<BehaviorShootFromCastleWalls>(100f);
                    //        return false;
                    //    }
                    //}
                    ////bool carryOutDefenceEnabledOut;
                    //if (!carryOutDefenceEnabled.TryGetValue(___team, out carryOutDefenceEnabledOut))
                    //{
                    //    carryOutDefenceEnabled[___team] = false;
                    doRangedJoinMelee = false;
                    //    return true;
                    //}
                    //else
                    //{
                    //    return false;
                    //}
                }
                return true;
            }

            //[HarmonyPostfix]
            //[HarmonyPatch("CarryOutDefense")]
            //static void PostfixCarryOutDefense(ref TacticDefendCastle __instance, ref bool doRangedJoinMelee, ref Team ___team)
            //{
            //    if (Mission.Current.Mode != MissionMode.Deployment)
            //    {
            //        List<Formation> archerFormations = new List<Formation>();
            //        foreach (Formation formation in ___team.Formations.ToList())
            //        {
            //            if (formation.QuerySystem.IsRangedFormation)
            //            {
            //                archerFormations.Add(formation);
            //            }
            //        }
            //        if (archerFormations.Count > 1)
            //        {
            //            foreach (Formation archerFormation in archerFormations.ToList())
            //            {
            //                archerFormation.AI.ResetBehaviorWeights();
            //                archerFormation.AI.SetBehaviorWeight<BehaviorShootFromCastleWalls>(100f);
            //            }
            //        }
            //    }
            //}

            [HarmonyPrefix]
            [HarmonyPatch("ArcherShiftAround")]
            private static bool PrefixArcherShiftAround(ref TacticDefendCastle __instance)
            {
                if (Mission.Current.Mode != MissionMode.Deployment)
                {
                    bool archersShiftAroundEnabledOut;
                    if (!archersShiftAroundEnabled.TryGetValue(__instance.Team, out archersShiftAroundEnabledOut))
                    {
                        archersShiftAroundEnabled[__instance.Team] = false;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                return true;
            }

            //[HarmonyPrefix]
            //[HarmonyPatch("BalanceLaneDefenders")]
            //static bool PrefixBalanceLaneDefenders(ref TacticDefendCastle __instance, ref Team ___team)
            //{
            //    if (Mission.Current.Mode != MissionMode.Deployment)
            //    {
            //        bool balanceLaneDefendersEnabledOut;
            //        if (!balanceLaneDefendersEnabled.TryGetValue(___team, out balanceLaneDefendersEnabledOut))
            //        {
            //            balanceLaneDefendersEnabled[___team] = false;
            //            return true;
            //        }
            //        else
            //        {
            //            return false;
            //        }
            //    }
            //    return true;
            //}
        }

        [HarmonyPatch(typeof(Mission))]
        private class MissionPatch
        {
            [HarmonyPostfix]
            [HarmonyPatch("OnObjectDisabled")]
            private static void PostfixOnObjectDisabled(DestructableComponent destructionComponent)
            {
                if (destructionComponent.GameEntity.GetFirstScriptOfType<UsableMachine>() != null && destructionComponent.GameEntity.GetFirstScriptOfType<UsableMachine>().GetType().Equals(typeof(BatteringRam)) && destructionComponent.GameEntity.GetFirstScriptOfType<UsableMachine>().IsDestroyed)
                {
                    balanceLaneDefendersEnabled.Clear();
                    carryOutDefenceEnabled.Clear();
                }
            }
        }

        [HarmonyPatch(typeof(BehaviorAssaultWalls))]
        private class OverrideBehaviorAssaultWalls
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
            private static bool PrefixCalculateCurrentOrder(ref BehaviorAssaultWalls __instance, ref MovementOrder ____chargeOrder)
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
            private static void PostfixCalculateCurrentOrder(ref BehaviorAssaultWalls __instance, ref MovementOrder ____wallSegmentMoveOrder, ref MovementOrder ____attackEntityOrderOuterGate, ref ArrangementOrder ___CurrentArrangementOrder, ref MovementOrder ____chargeOrder, ref TeamAISiegeComponent ____teamAISiegeComponent, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState, ref MovementOrder ____attackEntityOrderInnerGate)
            {
                //____attackEntityOrderInnerGate = MovementOrder.MovementOrderAttackEntity(____teamAISiegeComponent.InnerGate.GameEntity, surroundEntity: false);
                //___CurrentArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                switch (____behaviorState)
                {
                    case BehaviorState.ClimbWall:
                        {
                            //if (__instance.Formation != null && __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(____wallSegmentMoveOrder.GetPosition(__instance.Formation)) > 60f)
                            //{
                            //    ____currentOrder = ____wallSegmentMoveOrder;
                            //    break;
                            //}
                            //if (__instance.Formation != null)
                            //{
                            //    Formation enemyFormation = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, false, false, false, false, false);
                            //    if (enemyFormation != null)
                            //    {
                            //        ____currentOrder = MovementOrder.MovementOrderChargeToTarget(enemyFormation);
                            //        break;
                            //    }
                            //}
                            //if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                            //{
                            //    ____currentOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            //}
                            break;
                        }
                    case BehaviorState.AttackEntity:
                        {
                            //if (____attackEntityOrderInnerGate.TargetEntity != null)
                            //{
                            //    __instance.Formation.FormAttackEntityDetachment(____attackEntityOrderInnerGate.TargetEntity);
                            //}

                            //___CurrentArrangementOrder = ArrangementOrder.ArrangementOrderLoose;
                            break;
                        }
                    case BehaviorState.Charging:
                        {
                            if (__instance.Formation.AI.Side == BehaviorSide.Left || __instance.Formation.AI.Side == BehaviorSide.Right)
                            {
                                //__instance.Formation.DisbandAttackEntityDetachment();

                                //foreach (IDetachment detach in __instance.Formation.Detachments.ToList())
                                //{
                                //    __instance.Formation.LeaveDetachment(detach);
                                //}
                            }
                            break;
                        }
                    case BehaviorState.TakeControl:
                        {
                            if (__instance.Formation.AI.Side == BehaviorSide.Middle)
                            {
                                //__instance.Formation.DisbandAttackEntityDetachment();

                                //foreach (IDetachment detach in __instance.Formation.Detachments.ToList())
                                //{
                                //    __instance.Formation.LeaveDetachment(detach);
                                //}
                            }

                            if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                            {
                                //____attackEntityOrderInnerGate = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                                //____attackEntityOrderOuterGate = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                                //____chargeOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                                //____chargeOrder.TargetEntity = null;
                                //____currentOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                                //____currentOrder.TargetEntity = null;
                            }
                            break;
                        }
                }
            }
        }

        ////[HarmonyPatch(typeof(AttackEntityOrderDetachment))]
        ////class OverrideAttackEntityOrderDetachment
        ////{
        ////    [HarmonyPostfix]
        ////    [HarmonyPatch("Initialize")]
        ////    static void PostfixInitialize(ref BattleSideEnum managedSide, Vec3 managedDirection, ref float queueBeginDistance, ref int ____maxUserCount, ref float ____agentSpacing, ref float ____queueBeginDistance, ref float ____queueRowSize, ref float ____costPerRow, ref float ____baseCost)
        ////    {
        ////    }
        ////}

        [HarmonyPatch(typeof(BehaviorShootFromCastleWalls))]
        private class OverrideBehaviorShootFromCastleWalls
        {
            [HarmonyPrefix]
            [HarmonyPatch("OnBehaviorActivatedAux")]
            private static bool PrefixOnBehaviorActivatedAux(ref BehaviorShootFromCastleWalls __instance, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder)
            {
                __instance.Formation.SetMovementOrder(____currentOrder);
                __instance.Formation.FacingOrder = ___CurrentFacingOrder;
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderScatter;
                __instance.Formation.FiringOrder = FiringOrder.FiringOrderFireAtWill;
                __instance.Formation.FormOrder = FormOrder.FormOrderWider;
                __instance.Formation.WeaponUsageOrder = WeaponUsageOrder.WeaponUsageOrderUseAny;
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("TickOccasionally")]
            private static bool PrefixTickOccasionally(ref BehaviorShootFromCastleWalls __instance, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref TacticalPosition ____tacticalArcherPosition)
            {
                if (__instance.Formation.ArrangementOrder == ArrangementOrder.ArrangementOrderLine)
                {
                    __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderScatter;
                }
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
        private class OverrideBehaviorUseSiegeMachines
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetAiWeight")]
            private static bool PrefixGetAiWeight(ref BehaviorUseSiegeMachines __instance, ref float __result, ref TeamAISiegeComponent ____teamAISiegeComponent, List<UsableMachine> ____primarySiegeWeapons)
            {
                float result = 0f;
                if (____teamAISiegeComponent != null && ____primarySiegeWeapons.Any() && ____primarySiegeWeapons.All((UsableMachine psw) => !(psw as IPrimarySiegeWeapon).HasCompletedAction()))
                {
                    result = (____teamAISiegeComponent.IsCastleBreached() ? 0.75f : 1.5f);
                }
                __result = result;
                return false;
            }

            [HarmonyPostfix]
            [HarmonyPatch("TickOccasionally")]
            private static void PrefixTickOccasionally(ref BehaviorUseSiegeMachines __instance)
            {
                if (__instance.Formation.ArrangementOrder == ArrangementOrder.ArrangementOrderShieldWall)
                {
                    __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                }
            }
        }

        //[HarmonyPatch(typeof(BehaviorWaitForLadders))]
        private class OverrideBehaviorWaitForLadders
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetAiWeight")]
            private static bool PrefixOnGetAiWeight(ref BehaviorWaitForLadders __instance, MovementOrder ____followOrder, ref TacticalPosition ____followTacticalPosition, ref float __result, ref TeamAISiegeComponent ____teamAISiegeComponent)
            {
                if (____followTacticalPosition != null)
                {
                    //foreach (SiegeLane sl in TeamAISiegeComponent.SiegeLanes)
                    //{
                    //    if (sl.IsBreach && (sl.LaneSide == __instance.Formation.AI.Side))
                    //    {
                    //        __result = 0f;
                    //        return false;
                    //    }
                    //}
                    //if (____followTacticalPosition.Position.AsVec2.Distance(__instance.Formation.QuerySystem.AveragePosition) > 7f)
                    if (____followTacticalPosition.Position.AsVec2.Distance(__instance.Formation.QuerySystem.AveragePosition) > 10f)
                    {
                        if (____followOrder.OrderEnum != 0 && !____teamAISiegeComponent.AreLaddersReady)
                        {
                            __result = ((!____teamAISiegeComponent.IsCastleBreached()) ? 2f : 1f);
                            return false;
                        }
                    }
                }
                return true;
            }

            [HarmonyPostfix]
            [HarmonyPatch("TickOccasionally")]
            private static void PrefixTickOccasionally(ref BehaviorWaitForLadders __instance)
            {
                if (__instance.Formation.ArrangementOrder == ArrangementOrder.ArrangementOrderShieldWall)
                {
                    __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                }
            }
        }

        [HarmonyPatch(typeof(BehaviorDefendCastleKeyPosition))]
        private class OverrideBehaviorDefendCastleKeyPosition
        {
            private enum BehaviorState
            {
                UnSet,
                Waiting,
                Ready
            }

            [HarmonyPrefix]
            [HarmonyPatch("OnBehaviorActivatedAux")]
            private static bool PrefixOnBehaviorActivatedAux(ref BehaviorDefendCastleKeyPosition __instance, ref FacingOrder ____waitFacingOrder, ref FacingOrder ____readyFacingOrder, ref TeamAISiegeComponent ____teamAISiegeDefender, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ____behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
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
            [HarmonyPatch("CalculateCurrentOrder")]
            private static bool PrefixCalculateCurrentOrder(ref BehaviorDefendCastleKeyPosition __instance, ref FacingOrder ____waitFacingOrder, ref FacingOrder ____readyFacingOrder, ref TeamAISiegeComponent ____teamAISiegeDefender, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ____behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
            {
                ____behaviorSide = __instance.Formation.AI.Side;
                ____innerGate = null;
                ____outerGate = null;
                ____laddersOnThisSide.Clear();
                bool num = Mission.Current.ActiveMissionObjects.FindAllWithType<CastleGate>().Any((CastleGate cg) => cg.DefenseSide == ____behaviorSide && cg.GameEntity.HasTag("outer_gate"));
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
                }
                else
                {
                    WallSegment wallSegment = (from ws in Mission.Current.ActiveMissionObjects.FindAllWithType<WallSegment>()
                                               where ws.DefenseSide == ____behaviorSide && ws.IsBreachedWall
                                               select ws).FirstOrDefault();
                    if (wallSegment != null)
                    {
                        worldFrame = wallSegment.MiddleFrame;
                        worldFrame2 = wallSegment.DefenseWaitFrame;
                        ____tacticalMiddlePos = wallSegment.MiddlePosition;
                        ____tacticalWaitPos = wallSegment.WaitPosition;
                    }
                    else
                    {
                        IEnumerable<SiegeWeapon> source = from sw in Mission.Current.ActiveMissionObjects.FindAllWithType<SiegeWeapon>()
                                                          where sw is IPrimarySiegeWeapon && (sw as IPrimarySiegeWeapon).WeaponSide == ____behaviorSide && (!sw.IsDestroyed)
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

                ____laddersOnThisSide.Clear();
                if (____tacticalMiddlePos != null)
                {
                    if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                    {
                        if (____innerGate == null)
                        {
                            if (____outerGate != null)
                            {
                                float distance = __instance.Formation.SmoothedAverageUnitPosition.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.SmoothedAverageUnitPosition);
                                if ((____outerGate.IsDestroyed || ____outerGate.IsGateOpen) && (TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f ||
                                TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false, 0.2f)))
                                {
                                    ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                                    ____currentOrder = ____readyOrder;
                                }
                            }
                        }
                        else
                        {
                            float distance = __instance.Formation.SmoothedAverageUnitPosition.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.SmoothedAverageUnitPosition);
                            if ((____innerGate.IsDestroyed || ____innerGate.IsGateOpen) && (TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f ||
                                TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false, 0.2f)))
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
                    Formation correctEnemy = RBMAI.Utilities.FindSignificantEnemyToPosition(__instance.Formation, position, true, false, false, false, false, true);
                    if (correctEnemy != null)
                    {
                        float distance = __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.MedianPosition.AsVec2);
                        if (TeamAISiegeComponent.IsFormationInsideCastle(correctEnemy, includeOnlyPositionedUnits: false, 0.2f) || (TeamAISiegeComponent.IsFormationInsideCastle(correctEnemy, includeOnlyPositionedUnits: false, 0.05f) && TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f))
                        {
                            ____readyOrder = MovementOrder.MovementOrderChargeToTarget(correctEnemy);
                            ____waitOrder = MovementOrder.MovementOrderChargeToTarget(correctEnemy);
                            ____currentOrder = ____readyOrder;
                        }
                    }
                }

                if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && ____tacticalWaitPos != null && ____tacticalMiddlePos == null)
                {
                    float distance = __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
                    if ((____innerGate.IsDestroyed || ____innerGate.IsGateOpen) && (TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f ||
                                TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false, 0.2f)))
                    {
                        ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                        ____currentOrder = ____readyOrder;
                    }
                }

                return true;
            }

            //[HarmonyPostfix]
            //[HarmonyPatch("ResetOrderPositions")]
            //static void PostfixResetOrderPositions(ref BehaviorDefendCastleKeyPosition __instance, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
            //{
            //    ____laddersOnThisSide.Clear();
            //    if (____tacticalMiddlePos != null)
            //    {
            //        if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            //        {
            //            if (____innerGate == null)
            //            {
            //                if (____outerGate != null)
            //                {
            //                    float distance = __instance.Formation.SmoothedAverageUnitPosition.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.SmoothedAverageUnitPosition);
            //                    if ((____outerGate.IsDestroyed || ____outerGate.IsGateOpen) && (TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f ||
            //                    TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false, 0.2f)))
            //                    {
            //                        ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
            //                        ____currentOrder = ____readyOrder;
            //                    }
            //                }
            //            }
            //            else
            //            {
            //                float distance = __instance.Formation.SmoothedAverageUnitPosition.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.SmoothedAverageUnitPosition);
            //                if ((____innerGate.IsDestroyed || ____innerGate.IsGateOpen) && (TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f ||
            //                    TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false, 0.2f)))
            //                {
            //                    ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
            //                    ____currentOrder = ____readyOrder;
            //                }
            //            }
            //        }

            //        if (____innerGate != null && !____innerGate.IsDestroyed)
            //        {
            //            WorldPosition position = ____tacticalMiddlePos.Position;
            //            if (____behaviorState == BehaviorState.Ready)
            //            {
            //                Vec2 direction = (____innerGate.GetPosition().AsVec2 - __instance.Formation.QuerySystem.MedianPosition.AsVec2).Normalized();
            //                WorldPosition newPosition = position;
            //                newPosition.SetVec2(position.AsVec2 - direction * 2f);
            //                ____readyOrder = MovementOrder.MovementOrderMove(newPosition);
            //                ____currentOrder = ____readyOrder;
            //            }
            //        }
            //    }

            //    if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && ____tacticalMiddlePos != null && ____innerGate == null && ____outerGate == null)
            //    {
            //        WorldPosition position = ____tacticalMiddlePos.Position;
            //        Formation correctEnemy = RBMAI.Utilities.FindSignificantEnemyToPosition(__instance.Formation, position, true, false, false, false, false, true);
            //        if (correctEnemy != null)
            //        {
            //            float distance = __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.MedianPosition.AsVec2);
            //            if (TeamAISiegeComponent.IsFormationInsideCastle(correctEnemy, includeOnlyPositionedUnits: false, 0.2f) || (TeamAISiegeComponent.IsFormationInsideCastle(correctEnemy, includeOnlyPositionedUnits: false, 0.05f) && TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f))
            //            {
            //                ____readyOrder = MovementOrder.MovementOrderChargeToTarget(correctEnemy);
            //                ____waitOrder = MovementOrder.MovementOrderChargeToTarget(correctEnemy);
            //                ____currentOrder = ____readyOrder;
            //            }

            //        }
            //    }

            //    if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && ____tacticalWaitPos != null && ____tacticalMiddlePos == null)
            //    {
            //        float distance = __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation.QuerySystem.AveragePosition);
            //        if ((____innerGate.IsDestroyed || ____innerGate.IsGateOpen) && (TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation, includeOnlyPositionedUnits: false, 0.25f) && distance < 35f ||
            //                    TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation, includeOnlyPositionedUnits: false, 0.2f)))
            //        {
            //            ____readyOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
            //            ____currentOrder = ____readyOrder;
            //        }
            //    }
            //}

            //[HarmonyPrefix]
            //[HarmonyPatch("TickOccasionally")]
            //static bool PrefixTickOccasionally(ref FacingOrder ____readyFacingOrder, ref FacingOrder ____waitFacingOrder, ref BehaviorDefendCastleKeyPosition __instance, ref TeamAISiegeComponent ____teamAISiegeDefender, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ____behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
            //{
            //    IEnumerable<SiegeWeapon> source = from sw in Mission.Current.ActiveMissionObjects.FindAllWithType<SiegeWeapon>()
            //                                      where sw is IPrimarySiegeWeapon && (((sw as IPrimarySiegeWeapon).WeaponSide == FormationAI.BehaviorSide.Middle && !(sw as IPrimarySiegeWeapon).HoldLadders) || (sw as IPrimarySiegeWeapon).WeaponSide != FormationAI.BehaviorSide.Middle && (sw as IPrimarySiegeWeapon).SendLadders)
            //                                      //where sw is IPrimarySiegeWeapon
            //                                      select sw;

            //    BehaviorState BehaviorState = ____teamAISiegeDefender == null || !source.Any() ? BehaviorState.Waiting : BehaviorState.Ready;
            //    if (BehaviorState != ____behaviorState)
            //    {
            //        ____behaviorState = BehaviorState;
            //        ____currentOrder = ((____behaviorState == BehaviorState.Ready) ? ____readyOrder : ____waitOrder);
            //        ___CurrentFacingOrder = ((____behaviorState == BehaviorState.Ready) ? ____readyFacingOrder : ____waitFacingOrder);
            //    }
            //    if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.Siege)
            //    {
            //        if (____outerGate != null && ____outerGate.State == CastleGate.GateState.Open && !____outerGate.IsDestroyed)
            //        {
            //            if (!____outerGate.IsUsedByFormation(__instance.Formation))
            //            {
            //                __instance.Formation.StartUsingMachine(____outerGate);
            //            }
            //        }
            //        else if (____innerGate != null && ____innerGate.State == CastleGate.GateState.Open && !____innerGate.IsDestroyed && !____innerGate.IsUsedByFormation(__instance.Formation))
            //        {
            //            __instance.Formation.StartUsingMachine(____innerGate);
            //        }
            //    }

            //    MethodInfo method = typeof(BehaviorDefendCastleKeyPosition).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
            //    method.DeclaringType.GetMethod("CalculateCurrentOrder");
            //    method.Invoke(__instance, new object[] { });

            //    __instance.Formation.SetMovementOrder(____currentOrder);
            //    __instance.Formation.FacingOrder = ___CurrentFacingOrder;
            //    if (____behaviorState == BehaviorState.Ready && ____tacticalMiddlePos != null)
            //    {
            //        __instance.Formation.FormOrder = FormOrder.FormOrderCustom(____tacticalMiddlePos.Width * 2f);
            //    }
            //    else if (____behaviorState == BehaviorState.Waiting && ____tacticalWaitPos != null)
            //    {
            //        __instance.Formation.FormOrder = FormOrder.FormOrderCustom(____tacticalWaitPos.Width * 2f);
            //    }
            //    //bool flag = ____isDefendingWideGap && ____behaviorState == BehaviorState.Ready && __instance.Formation.QuerySystem.ClosestEnemyFormation != null && (__instance.Formation.QuerySystem.IsUnderRangedAttack || __instance.Formation.QuerySystem.AveragePosition.DistanceSquared(____currentOrder.GetPosition(__instance.Formation)) < 25f + (____isInShieldWallDistance ? 75f : 0f));
            //    //if (flag == ____isInShieldWallDistance)
            //    //{
            //    //    return false;
            //    //}
            //    //____isInShieldWallDistance = flag;
            //    //if (____isInShieldWallDistance && __instance.Formation.QuerySystem.HasShield)
            //    //{
            //    //    if (__instance.Formation.ArrangementOrder != ArrangementOrder.ArrangementOrderLine)
            //    //    {
            //    //        __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
            //    //    }
            //    //}
            //    //else if (__instance.Formation.ArrangementOrder == ArrangementOrder.ArrangementOrderLine)
            //    //{
            //    __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
            //    //}
            //    return false;
            //}
        }

        [HarmonyPatch(typeof(LadderQueueManager))]
        private class OverrideLadderQueueManager
        {
            [HarmonyPostfix]
            [HarmonyPatch("Initialize")]
            private static void PostfixInitialize(ref BattleSideEnum managedSide, Vec3 managedDirection, ref float ____arcAngle, ref float queueBeginDistance, ref int ____maxUserCount, ref float ____agentSpacing, ref float ____queueBeginDistance, ref float ____queueRowSize, ref float ____costPerRow, ref float ____baseCost)
            {
                if (____maxUserCount == 3)
                {
                    ____arcAngle = (float)Math.PI * 1f / 2f;
                    ____agentSpacing = 1f;
                    ____queueBeginDistance = 3f;
                    ____queueRowSize = 1f;
                    ____maxUserCount = 15;
                }
                if (____maxUserCount == 1)
                {
                    ____maxUserCount = 0;
                }
                //else
                //{
                //    ____maxUserCount = 0;
                //}
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
        private class OverrideSiegeTower
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

            //[HarmonyPostfix]
            //[HarmonyPatch("OnDeploymentStateChanged")]
            //static void PostfixDeploymentStateChanged(ref SiegeTower __instance, ref List<SiegeLadder> ____sameSideSiegeLadders, ref GameEntity ____cleanState, ref List<LadderQueueManager> ____queueManagers)
            //{
            //    if (!RBMConfig.RBMConfig.siegeTowersEnabled)
            //    {
            //        __instance.Disable();
            //        ____cleanState.SetVisibilityExcludeParents(false);
            //        if (____sameSideSiegeLadders != null)
            //        {
            //            foreach (SiegeLadder sameSideSiegeLadder in ____sameSideSiegeLadders)
            //            {
            //                sameSideSiegeLadder.GameEntity.SetVisibilityExcludeParents(true);
            //            }
            //        }
            //    }
            //}

            //[HarmonyPostfix]
            //[HarmonyPatch("OnDestroyed")]
            //static void PostfixOnDestroyed(ref List<SiegeLadder> ____sameSideSiegeLadders)
            //{
            //    if (____sameSideSiegeLadders != null)
            //    {
            //        foreach (SiegeLadder sameSideSiegeLadder in ____sameSideSiegeLadders)
            //        {
            //            sameSideSiegeLadder.GameEntity.SetVisibilityExcludeParents(true);
            //        }
            //    }
            //}
        }

        //[HarmonyPatch(typeof(SiegeLane))]
        //class OverrideSiegeLane
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch("GetLaneCapacity")]
        //    static bool PrefixGetLaneCapacity(ref SiegeLane __instance, ref float __result)
        //    {
        //        if (__instance.DefensePoints.Any((ICastleKeyPosition dp) => dp is WallSegment && (dp as WallSegment).IsBreachedWall))
        //        {
        //            __result = 60f;
        //            return false;
        //        }
        //        if ((__instance.HasGate && __instance.DefensePoints.Where((ICastleKeyPosition dp) => dp is CastleGate).All((ICastleKeyPosition cg) => (cg as CastleGate).IsGateOpen)))
        //        {
        //            __result = 60f;
        //            return false;
        //        }
        //        __result = __instance.PrimarySiegeWeapons.Where((IPrimarySiegeWeapon psw) => !(psw as SiegeWeapon).IsDestroyed).Sum((IPrimarySiegeWeapon psw) => psw.SiegeWeaponPriority);
        //        if (__result == 6f)
        //        {
        //            __result = 15f;
        //        }
        //        if (__result == 15f)
        //        {
        //            __result = 15f;
        //        }
        //        if (__result == 25f)
        //        {
        //            __result = 60f;
        //        }
        //        return false;
        //    }

        //    [HarmonyPostfix]
        //    [HarmonyPatch("DetermineLaneState")]
        //    static void postfixDetermineLaneState(ref SiegeLane __instance)
        //    {
        //        if (__instance.LaneState == LaneStateEnum.Used)
        //        {
        //            PropertyInfo property2 = typeof(SiegeLane).GetProperty("LaneState");
        //            property2.DeclaringType.GetProperty("LaneState");
        //            property2.SetValue(__instance, LaneStateEnum.Active, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
        //            //___LaneState = LaneStateEnum.Active;
        //        }
        //    }
        //}

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

        //[HarmonyPatch(typeof(TacticDefendCastle))]
        //class IsSallyOutApplicablePatch
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch("IsSallyOutApplicable")]
        //    static bool Prefix(ref bool __result)
        //    {
        //        __result = false;
        //        return false;
        //    }
        //}

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

        [HarmonyPatch(typeof(TacticDefendCastle))]
        private class StopUsingAllMachinesPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("StopUsingAllMachines")]
            private static bool Prefix()
            {
                return false;
            }
        }

        [HarmonyPatch(typeof(TacticDefendCastle))]
        private class StopUsingAllRangedSiegeWeaponsPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("StopUsingAllRangedSiegeWeapons")]
            private static bool Prefix()
            {
                return false;
            }
        }

        //[HarmonyPatch(typeof(SiegeMissionController))]
        //class SetupTeamsOfSidePatch
        //{
        //    [HarmonyPostfix]
        //    [HarmonyPatch("SetupTeamsOfSide")]
        //    static void Postfix(BattleSideEnum side)
        //    {
        //        if(side == BattleSideEnum.Defender)
        //        {
        //            foreach (Formation item2 in Mission.Current.DefenderTeam.FormationsIncludingSpecial)
        //            {
        //                Mission.Current.AllowAiTicking = true;
        //                item2.ApplyActionOnEachUnit(delegate (Agent agent)
        //                {
        //                    if (agent.IsAIControlled)
        //                    {
        //                        agent.AIStateFlags |= Agent.AIStateFlag.Alarmed;
        //                        agent.SetIsAIPaused(isPaused: false);
        //                    }
        //                });
        //            }
        //        }
        //    }
        //}

        //[HarmonyPatch(typeof(TacticBreachWalls))]
        //class StartTacticalRetreatPatch
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch("StartTacticalRetreat")]
        //    static bool Prefix(ref TacticBreachWalls __instance, ref Team ___team)
        //    {
        //        float enemyPower = Mission.Current.Teams.GetEnemiesOf(___team).Sum((Team t) => t.QuerySystem.TeamPower);
        //        float allyPower = Mission.Current.Teams.GetAlliesOf(___team, true).Sum((Team t) => t.QuerySystem.TeamPower);
        //        if (allyPower >= enemyPower * 0.3f)
        //        {
        //            return false;
        //        }
        //        return true;
        //    }
        //}
    }
}