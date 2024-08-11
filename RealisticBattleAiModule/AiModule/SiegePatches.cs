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
                        foreach (Formation formation in Mission.Current.Teams.Defender.FormationsIncludingEmpty)
                        {
                            if (formation != null && formation.QuerySystem != null && formation.QuerySystem.IsRangedFormation)
                            {
                                WorldPosition medianPosition = formation.QuerySystem.MedianPosition;
                                WorldPosition averagePosition = new WorldPosition(Mission.Current.Scene, formation.QuerySystem.AveragePosition.ToVec3());
                                if (medianPosition.IsValid)
                                {
                                    if(formation.OrderPosition.IsValid && formation.OrderPosition.Distance(medianPosition.AsVec2) > 15f)
                                    {
                                        formation.SetMovementOrder(MovementOrder.MovementOrderMove(medianPosition));
                                        return false;
                                    }
                                }
                                else if (averagePosition.IsValid)
                                {
                                    if (formation.OrderPosition.IsValid && formation.OrderPosition.Distance(averagePosition.AsVec2) > 15f)
                                    {
                                        formation.SetMovementOrder(MovementOrder.MovementOrderMove(averagePosition));
                                        return false;
                                    }
                                }
                                else
                                {
                                    foreach (Formation lastOptionFormation in Mission.Current.Teams.Defender.FormationsIncludingEmpty)
                                    {
                                        if (lastOptionFormation != null && lastOptionFormation.QuerySystem != null && lastOptionFormation.QuerySystem.MedianPosition.IsValid)
                                        {
                                            if (formation.OrderPosition.IsValid && formation.OrderPosition.Distance(lastOptionFormation.QuerySystem.MedianPosition.AsVec2) > 15f)
                                            {
                                                formation.SetMovementOrder(MovementOrder.MovementOrderMove(lastOptionFormation.QuerySystem.MedianPosition));
                                                return false;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        return false;
                    }
                }
                return true;
            }

            [HarmonyPrefix]
            [HarmonyPatch("BalanceLaneDefenders")]
            static bool PrefixBalanceLaneDefenders(ref TacticDefendCastle __instance)
            {
                if (Mission.Current.Mode != MissionMode.Deployment)
                {
                    bool balanceLaneDefendersEnabledOut;
                    if (!balanceLaneDefendersEnabled.TryGetValue(__instance.Team, out balanceLaneDefendersEnabledOut))
                    {
                        balanceLaneDefendersEnabled[__instance.Team] = false;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                return true;
            }
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
                            if (__instance.Formation != null && __instance.Formation.QuerySystem.MedianPosition.AsVec2.Distance(____wallSegmentMoveOrder.GetPosition(__instance.Formation)) > 60f)
                            {
                                ____currentOrder = ____wallSegmentMoveOrder;
                                break;
                            }
                            if (__instance.Formation != null)
                            {
                                Formation enemyFormation = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, false, false, false, false, false);
                                if (enemyFormation != null)
                                {
                                    ____currentOrder = MovementOrder.MovementOrderChargeToTarget(enemyFormation);
                                    break;
                                }
                            }
                            if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                            {
                                ____currentOrder = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                            }
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
                                ____attackEntityOrderInnerGate = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
                                ____attackEntityOrderOuterGate = MovementOrder.MovementOrderChargeToTarget(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
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
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("CalculateCurrentOrder")]
            private static bool PrefixCalculateCurrentOrder(ref BehaviorDefendCastleKeyPosition __instance, ref FacingOrder ____waitFacingOrder, ref FacingOrder ____readyFacingOrder, ref TeamAISiegeComponent ____teamAISiegeDefender, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ____behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
            {
                MethodInfo method = typeof(BehaviorDefendCastleKeyPosition).GetMethod("ResetOrderPositions", BindingFlags.NonPublic | BindingFlags.Instance);
                method.DeclaringType.GetMethod("ResetOrderPositions");
                method.Invoke(__instance, new object[] { });
                return true;
            }

            [HarmonyPostfix]
            [HarmonyPatch("ResetOrderPositions")]
            static void PostfixResetOrderPositions(ref BehaviorDefendCastleKeyPosition __instance, ref WorldPosition ____readyOrderPosition, ref TeamAISiegeComponent ____teamAISiegeDefender, ref FacingOrder ___CurrentFacingOrder, ref FacingOrder ____readyFacingOrder, ref FacingOrder ____waitFacingOrder, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, FormationAI.BehaviorSide ____behaviorSide, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
            {
                ____behaviorSide = __instance.Formation.AI.Side;
                ____innerGate = null;
                ____outerGate = null;
                ____laddersOnThisSide.Clear();
                WorldFrame worldFrame;
                WorldFrame worldFrame2;
                if (____teamAISiegeDefender.OuterGate.DefenseSide == ____behaviorSide)
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
                    WallSegment wallSegment = ____teamAISiegeDefender.WallSegments.Where((WallSegment ws) => ws.DefenseSide == ____behaviorSide && ws.IsBreachedWall).FirstOrDefault();
                    if (wallSegment != null)
                    {
                        worldFrame = wallSegment.MiddleFrame;
                        worldFrame2 = wallSegment.DefenseWaitFrame;
                        ____tacticalMiddlePos = wallSegment.MiddlePosition;
                        ____tacticalWaitPos = wallSegment.WaitPosition;
                    }
                    else
                    {
                        IEnumerable<IPrimarySiegeWeapon> source = ____teamAISiegeDefender.PrimarySiegeWeapons.Where((IPrimarySiegeWeapon sw) => sw.WeaponSide == ____behaviorSide && ((sw is SiegeWeapon siegeWeapon && !siegeWeapon.IsDestroyed && !siegeWeapon.IsDeactivated) || sw.HasCompletedAction()));
                        if (!source.Any())
                        {
                            worldFrame = WorldFrame.Invalid;
                            worldFrame2 = WorldFrame.Invalid;
                            ____tacticalMiddlePos = null;
                            ____tacticalWaitPos = null;
                        }
                        else
                        {
                            ____laddersOnThisSide = source.OfType<SiegeLadder>().ToList();
                            ICastleKeyPosition castleKeyPosition = source.FirstOrDefault().TargetCastlePosition as ICastleKeyPosition;
                            worldFrame = castleKeyPosition.MiddleFrame;
                            worldFrame2 = castleKeyPosition.DefenseWaitFrame;
                            ____tacticalMiddlePos = castleKeyPosition.MiddlePosition;
                            ____tacticalWaitPos = castleKeyPosition.WaitPosition;
                        }
                    }
                }
                if (____tacticalMiddlePos != null)
                {
                    ____readyOrderPosition = ____tacticalMiddlePos.Position;
                    ____readyOrder = MovementOrder.MovementOrderMove(____readyOrderPosition);
                    ____readyFacingOrder = FacingOrder.FacingOrderLookAtDirection(____tacticalMiddlePos.Direction);
                }
                else if (worldFrame.Origin.IsValid)
                {
                    worldFrame.Rotation.f.Normalize();
                    ____readyOrderPosition = worldFrame.Origin;
                    ____readyOrder = MovementOrder.MovementOrderMove(____readyOrderPosition);
                    ____readyFacingOrder = FacingOrder.FacingOrderLookAtDirection(worldFrame.Rotation.f.AsVec2);
                }
                else
                {
                    ____readyOrderPosition = WorldPosition.Invalid;
                    ____readyOrder = MovementOrder.MovementOrderStop;
                    ____readyFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                }
                if (____tacticalWaitPos != null)
                {
                    ____waitOrder = MovementOrder.MovementOrderMove(____tacticalWaitPos.Position);
                    ____waitFacingOrder = FacingOrder.FacingOrderLookAtDirection(____tacticalWaitPos.Direction);
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
                ____currentOrder = ((____behaviorState == BehaviorState.Ready) ? ____readyOrder : ____waitOrder);
                ___CurrentFacingOrder = ((__instance.Formation.QuerySystem.ClosestEnemyFormation != null && TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestEnemyFormation.Formation, includeOnlyPositionedUnits: true)) ? FacingOrder.FacingOrderLookAtEnemy : ((____behaviorState == BehaviorState.Ready) ? ____readyFacingOrder : ____waitFacingOrder));

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

            }

            [HarmonyPrefix]
            [HarmonyPatch("TickOccasionally")]
            static bool PrefixTickOccasionally(ref FacingOrder ____readyFacingOrder, ref FacingOrder ____waitFacingOrder, ref BehaviorDefendCastleKeyPosition __instance, ref TeamAISiegeComponent ____teamAISiegeDefender, ref FacingOrder ___CurrentFacingOrder, FormationAI.BehaviorSide ____behaviorSide, ref List<SiegeLadder> ____laddersOnThisSide, ref CastleGate ____innerGate, ref CastleGate ____outerGate, ref TacticalPosition ____tacticalMiddlePos, ref TacticalPosition ____tacticalWaitPos, ref MovementOrder ____waitOrder, ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState)
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
                        if (!____outerGate.IsUsedByFormation(__instance.Formation))
                        {
                            __instance.Formation.StartUsingMachine(____outerGate);
                        }
                    }
                    else if (____innerGate != null && ____innerGate.State == CastleGate.GateState.Open && !____innerGate.IsDestroyed && !____innerGate.IsUsedByFormation(__instance.Formation))
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
                //bool flag = ____isDefendingWideGap && ____behaviorState == BehaviorState.Ready && __instance.Formation.QuerySystem.ClosestEnemyFormation != null && (__instance.Formation.QuerySystem.IsUnderRangedAttack || __instance.Formation.QuerySystem.AveragePosition.DistanceSquared(____currentOrder.GetPosition(__instance.Formation)) < 25f + (____isInShieldWallDistance ? 75f : 0f));
                //if (flag == ____isInShieldWallDistance)
                //{
                //    return false;
                //}
                //____isInShieldWallDistance = flag;
                //if (____isInShieldWallDistance && __instance.Formation.QuerySystem.HasShield)
                //{
                //    if (__instance.Formation.ArrangementOrder != ArrangementOrder.ArrangementOrderLine)
                //    {
                //        __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                //    }
                //}
                //else if (__instance.Formation.ArrangementOrder == ArrangementOrder.ArrangementOrderLine)
                //{
                __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderLine;
                //}
                return false;
            }
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