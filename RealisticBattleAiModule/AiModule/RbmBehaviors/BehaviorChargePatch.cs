using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI.AiModule.RbmBehaviors
{
    [HarmonyPatch(typeof(BehaviorCharge))]
    internal class OverrideBehaviorCharge
    {
        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };
        public static Dictionary<Formation, float> timeToMoveStorage = new Dictionary<Formation, float> { };

        public static ArrangementOrder ArrangementOrderLine { get; private set; }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorCharge __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && !(__instance.Formation.Team.IsPlayerTeam || __instance.Formation.Team.IsPlayerAlly) && Campaign.Current != null && MobileParty.MainParty != null && MobileParty.MainParty.MapEvent != null &&
                MapEvent.PlayerMapEvent.DefenderSide.LeaderParty.MobileParty != null &&
                MapEvent.PlayerMapEvent.AttackerSide.LeaderParty.MobileParty != null)
            {
                MobileParty defender = MapEvent.PlayerMapEvent.DefenderSide.LeaderParty.MobileParty;
                MobileParty attacker = MapEvent.PlayerMapEvent.AttackerSide.LeaderParty.MobileParty;
                if (defender.IsBandit || attacker.IsBandit)
                {
                    return true;
                }
            }
            if (__instance.Formation != null && (__instance.Formation.QuerySystem.IsInfantryFormation || __instance.Formation.QuerySystem.IsRangedFormation) && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

                if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle && __instance.Formation.QuerySystem.IsInfantryFormation && !RBMAI.Utilities.FormationFightingInMelee(__instance.Formation, 0.5f))
                {
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
                    bool isOnlyCavReamining = RBMAI.Utilities.CheckIfOnlyCavRemaining(__instance.Formation);
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
                                if (storedPositonDistance > (__instance.Formation.Depth / 2f) + 10f)
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
                            //    if (RBMAI.Utilities.CheckIfCanBrace(agent))
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
                            if (!(__instance.Formation.AI?.Side == FormationAI.BehaviorSide.Left || __instance.Formation.AI?.Side == FormationAI.BehaviorSide.Right) && enemyCav.TargetFormation == __instance.Formation)
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
                                    if (storedPositonDistance > (__instance.Formation.Depth / 2f) + 10f)
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
                                //    if (RBMAI.Utilities.CheckIfCanBrace(agent))
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
                        }
                        positionsStorage.Remove(__instance.Formation);
                    }
                    else if (significantEnemy != null && !significantEnemy.QuerySystem.IsRangedFormation && signDist < 50f && RBMAI.Utilities.FormationActiveSkirmishersRatio(__instance.Formation, 0.38f))
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
                        && RBMAI.Utilities.ShouldFormationCopyShieldWall(__instance.Formation))
                    {
                        __instance.Formation.ArrangementOrder = ArrangementOrder.ArrangementOrderShieldWall;
                    }
                    else if (__instance.Formation.TargetFormation != null && __instance.Formation.TargetFormation.ArrangementOrder == ArrangementOrder.ArrangementOrderLine)
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

            //if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            //{
            //    __instance.Formation.SetTargetFormation(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation);
            //}
            //____currentOrder = MovementOrder.MovementOrderCharge;
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetAiWeight")]
        private static void PrefixGetAiWeight(ref BehaviorCharge __instance, ref float __result)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsRangedCavalryFormation)
            {
                __result = __result * 0.2f;
            }
            //__result = __result;
        }
    }
}