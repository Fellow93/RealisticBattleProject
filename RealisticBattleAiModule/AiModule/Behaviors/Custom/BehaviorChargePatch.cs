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
            // FormationAI.FindBestBehavior calls CalculateCurrentOrder (via PrecalculateMovementOrder) on every
            // weighted behavior of EVERY formation, including one the player leads as a non-general captain.
            // The branches below write orders straight onto the formation, so for a player-owned formation they
            // overrode the player's shieldwall/square with charge + line. Vanilla's version is side-effect free.
            if (__instance.Formation != null && !__instance.Formation.IsAIControlled)
            {
                return true;
            }
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

                // FindBestBehavior (via PrecalculateMovementOrder) and the BehaviorCharge constructor also run this
                // on a charge that is only a candidate. Writing shieldwall/firing/arrangement onto the formation then
                // flipped it to charge settings and back whenever another behaviour won, and every flip re-applies
                // behaviour values to all its agents (a visible hitch). So the formation is only touched while
                // Charge is the active behaviour; the order itself is always computed, it is the behaviour's answer.
                // The movement order needs no write here: once active, BehaviorCharge.TickOccasionally runs this
                // and then applies CurrentOrder itself.
                bool isActiveBehavior = __instance.Formation.AI != null && __instance.Formation.AI.ActiveBehavior == __instance;

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
                        Vec2 signDirection = RBMAI.Utilities.GetFormationCenter(significantEnemy) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                        signDist = signDirection.Normalize();
                    }

                    if (enemyCav != null)
                    {
                        Vec2 cavDirection = RBMAI.Utilities.GetFormationCenter(enemyCav) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                        cavDist = cavDirection.Normalize();
                    }
                    bool isOnlyCavRemaining = RBMAI.Utilities.CheckIfOnlyCavRemaining(__instance.Formation);
                    if ((enemyCav != null) && (cavDist <= signDist) && (enemyCav.CountOfUnits > __instance.Formation.CountOfUnits / 10) && ((signDist > 35f || significantEnemy == enemyCav) || isOnlyCavRemaining))
                    {
                        if (isOnlyCavRemaining)
                        {
                            Vec2 vec = RBMAI.Utilities.GetFormationCenter(enemyCav) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                            WorldPosition positionNew = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);

                            WorldPosition storedPosition = WorldPosition.Invalid;
                            cavHoldPositions.TryGetValue(__instance.Formation, out storedPosition);

                            if (!storedPosition.IsValid)
                            {
                                cavHoldPositions[__instance.Formation] = positionNew;
                                ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                            }
                            else
                            {
                                float storedPositonDistance = (storedPosition.AsVec2 - RBMAI.Utilities.GetFormationCenter(__instance.Formation)).Normalize();
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
                            if (isActiveBehavior)
                            {
                                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderShieldWall);
                            }
                            return false;
                        }
                        else
                        {
                            // TargetFormation is sticky (vanilla never clears it after a targeted order), so on its own
                            // it braced us against cavalry parked on a Move/Stop order for the whole battle -- a stronger
                            // attacker would never leave its spawn against a handful of idle horse. Brace only while that
                            // cavalry is actually charging us and close enough to matter, as BehaviorAdvance already does.
                            OrderType enemyCavOrder = enemyCav.GetReadonlyMovementOrderReference().OrderType;
                            bool enemyCavCharging = enemyCavOrder == OrderType.ChargeWithTarget || enemyCavOrder == OrderType.Charge;
                            if (!(__instance.Formation.AI?.Side == FormationAI.BehaviorSide.Left || __instance.Formation.AI?.Side == FormationAI.BehaviorSide.Right) && enemyCav.TargetFormation == __instance.Formation && enemyCavCharging && cavDist < 150f)
                            {
                                Vec2 vec = RBMAI.Utilities.GetFormationCenter(enemyCav) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                                WorldPosition positionNew = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);

                                WorldPosition storedPosition = WorldPosition.Invalid;
                                cavHoldPositions.TryGetValue(__instance.Formation, out storedPosition);

                                if (!storedPosition.IsValid)
                                {
                                    cavHoldPositions[__instance.Formation] = positionNew;
                                    ____currentOrder = MovementOrder.MovementOrderMove(positionNew);
                                }
                                else
                                {
                                    float storedPositonDistance = (storedPosition.AsVec2 - RBMAI.Utilities.GetFormationCenter(__instance.Formation)).Normalize();
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
                                if (isActiveBehavior)
                                {
                                    __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderShieldWall);
                                }
                                return false;
                            }
                        }
                        cavHoldPositions.Remove(__instance.Formation);
                    }
                    else if (significantEnemy != null && !significantEnemy.QuerySystem.IsRangedFormation && signDist < 50f && RBMAI.Utilities.FormationActiveSkirmishersRatio(__instance.Formation, 0.38f))
                    {
                        WorldPosition positionNew = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
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
                    ____currentOrder = MovementOrder.MovementOrderChargeToTarget(significantEnemy);
                    if (isActiveBehavior)
                    {
                        __instance.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
                        Utilities.DecideArrangementOrderForFormation(__instance.Formation);
                    }
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