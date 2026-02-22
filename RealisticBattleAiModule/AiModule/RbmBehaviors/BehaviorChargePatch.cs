using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI.AiModule.RbmBehaviors
{
    [HarmonyPatch(typeof(BehaviorCharge))]
    internal class OverrideBehaviorCharge
    {
        public static Dictionary<Formation, WorldPosition> cavHoldPositions = new Dictionary<Formation, WorldPosition> { };
        public static Dictionary<Formation, WorldPosition> skirmisherRetreatPositions = new Dictionary<Formation, WorldPosition> { };

        public static ArrangementOrder ArrangementOrderLine { get; private set; }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorCharge __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && __instance.Formation.Team != null &&
                !(__instance.Formation.Team.IsPlayerTeam || __instance.Formation.Team.IsPlayerAlly) &&
                Campaign.Current != null && MobileParty.MainParty != null && MobileParty.MainParty.MapEvent != null &&
                MapEvent.PlayerMapEvent != null &&
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
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
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
                        Vec2 signDirection = significantEnemy.CachedMedianPosition.AsVec2 - __instance.Formation.CachedMedianPosition.AsVec2;
                        signDist = signDirection.Normalize();
                    }

                    if (enemyCav != null)
                    {
                        Vec2 cavDirection = enemyCav.CachedMedianPosition.AsVec2 - __instance.Formation.CachedMedianPosition.AsVec2;
                        cavDist = cavDirection.Normalize();
                    }
                    bool isOnlyCavRemaining = RBMAI.Utilities.CheckIfOnlyCavRemaining(__instance.Formation);
                    if ((enemyCav != null) && (cavDist <= signDist) && (enemyCav.CountOfUnits > __instance.Formation.CountOfUnits / 10) && ((signDist > 35f || significantEnemy == enemyCav) || isOnlyCavRemaining))
                    {
                        if (isOnlyCavRemaining)
                        {
                            Vec2 vec = enemyCav.CachedMedianPosition.AsVec2 - __instance.Formation.CachedMedianPosition.AsVec2;
                            WorldPosition positionNew = __instance.Formation.CachedMedianPosition;

                            WorldPosition storedPosition = WorldPosition.Invalid;
                            cavHoldPositions.TryGetValue(__instance.Formation, out storedPosition);

                            if (!storedPosition.IsValid)
                            {
                                cavHoldPositions[__instance.Formation] = positionNew;
                                ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                            }
                            else
                            {
                                float storedPositonDistance = (storedPosition.AsVec2 - __instance.Formation.CachedMedianPosition.AsVec2).Normalize();
                                if (storedPositonDistance > (__instance.Formation.Depth / 2f) + 10f)
                                {
                                    cavHoldPositions[__instance.Formation] = positionNew;
                                    ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                                }
                                else
                                {
                                    ____currentOrder = MovementOrder.MovementOrderMove(storedPosition);
                                }
                            }
                            if (cavDist > 10f)
                            {
                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec.Normalized());
                            }
                            __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderShieldWall);
                            return false;
                        }
                        else
                        {
                            if (!(__instance.Formation.AI?.Side == FormationAI.BehaviorSide.Left || __instance.Formation.AI?.Side == FormationAI.BehaviorSide.Right) && enemyCav.TargetFormation == __instance.Formation)
                            {
                                Vec2 vec = enemyCav.CachedMedianPosition.AsVec2 - __instance.Formation.CachedMedianPosition.AsVec2;
                                WorldPosition positionNew = __instance.Formation.CachedMedianPosition;

                                WorldPosition storedPosition = WorldPosition.Invalid;
                                cavHoldPositions.TryGetValue(__instance.Formation, out storedPosition);

                                if (!storedPosition.IsValid)
                                {
                                    cavHoldPositions[__instance.Formation] = positionNew;
                                    ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                                }
                                else
                                {
                                    float storedPositonDistance = (storedPosition.AsVec2 - __instance.Formation.CachedMedianPosition.AsVec2).Normalize();
                                    if (storedPositonDistance > (__instance.Formation.Depth / 2f) + 10f)
                                    {
                                        cavHoldPositions[__instance.Formation] = positionNew;
                                        ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                                    }
                                    else
                                    {
                                        ____currentOrder = MovementOrder.MovementOrderMove(storedPosition);
                                    }
                                }
                                if (cavDist > 10f)
                                {
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec.Normalized());
                                }
                                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderShieldWall);
                                return false;
                            }
                        }
                        cavHoldPositions.Remove(__instance.Formation);
                    }
                    else if (significantEnemy != null && !significantEnemy.QuerySystem.IsRangedFormation && signDist < 50f && RBMAI.Utilities.FormationActiveSkirmishersRatio(__instance.Formation, 0.38f))
                    {
                        WorldPosition positionNew = __instance.Formation.CachedMedianPosition;
                        positionNew.SetVec2(positionNew.AsVec2 - __instance.Formation.Direction * 7f);

                        WorldPosition storedPosition = WorldPosition.Invalid;
                        skirmisherRetreatPositions.TryGetValue(__instance.Formation, out storedPosition);

                        if (!storedPosition.IsValid)
                        {
                            skirmisherRetreatPositions[__instance.Formation] = positionNew;
                            ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                        }
                        else
                        {
                            ____currentOrder = MovementOrder.MovementOrderMove(storedPosition);
                        }
                        return false;
                    }
                    cavHoldPositions.Remove(__instance.Formation);
                    skirmisherRetreatPositions.Remove(__instance.Formation);
                }

                if (significantEnemy != null && __instance.Formation.QuerySystem.IsInfantryFormation && __instance.Formation.CountOfUnitsWithoutDetachedOnes >= 30)
                {
                    __instance.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
                    ____currentOrder = MovementOrder.MovementOrderChargeToTarget(significantEnemy);
                    Utilities.DecideArrangementOrderForFormation(__instance.Formation);
                    __instance.Formation.SetMovementOrder(____currentOrder);
                    return false;
                }
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetAiWeight")]
        private static void PostfixGetAiWeight(ref BehaviorCharge __instance, ref float __result)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsRangedCavalryFormation)
            {
                __result = __result * 0.2f;
            }
        }
    }
}