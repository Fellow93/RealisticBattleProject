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
        // Formation -> mission time enemy cavalry last charged it
        public static Dictionary<Formation, float> braceLastThreatTime = new Dictionary<Formation, float> { };

        // Bracing outlasts the charge order by this long (CavalryCharge alternates ChargeWithTarget with a reform Move; each arrangement flip re-lays the formation)
        private const float BraceHoldSeconds = 8f;

        // A brace episode lasts at most this long, then no bracing for BraceCooldownSeconds (repeated charge orders must not freeze the line)
        private const float BraceMaxSeconds = 25f;
        private const float BraceCooldownSeconds = 15f;
        private const float BraceGapSeconds = 2f;

        private sealed class BraceEpisode
        {
            public float Start = float.NaN;
            public float Last = float.NaN;
            public float CooldownUntil = float.MinValue;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorCharge, BraceEpisode> BraceEpisodes =
            new System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorCharge, BraceEpisode>();

        private static bool BraceCoolingDown(BehaviorCharge behavior, float now)
        {
            return now < BraceEpisodes.GetOrCreateValue(behavior).CooldownUntil;
        }

        /// <summary>False (and the cooldown starts) once the episode ran BraceMaxSeconds.</summary>
        private static bool BraceStillAllowed(BehaviorCharge behavior, float now)
        {
            BraceEpisode episode = BraceEpisodes.GetOrCreateValue(behavior);
            if (float.IsNaN(episode.Start) || float.IsNaN(episode.Last) || now - episode.Last > BraceGapSeconds)
            {
                episode.Start = now;
            }
            episode.Last = now;
            if (now - episode.Start > BraceMaxSeconds)
            {
                episode.Start = float.NaN;
                episode.CooldownUntil = now + BraceCooldownSeconds;
                return false;
            }
            return true;
        }

        public static ArrangementOrder ArrangementOrderLine { get; private set; }

        // Vanilla applies only the movement order, never CurrentFacingOrder: the hold sets facing itself and hands it back to the enemy when it ends
        private static void EndCavalryHold(Formation formation, bool isActiveBehavior)
        {
            if (cavHoldPositions.Remove(formation) && isActiveBehavior)
            {
                formation.SetFacingOrder(FacingOrder.FacingOrderLookAtEnemy);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorCharge __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            // FindBestBehavior runs CalculateCurrentOrder on every formation incl. player-led ones; the branches below write orders onto the formation
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

                // also runs on a candidate charge: touch the formation only while Charge is active (each flip re-applies behaviour values to all agents)
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
                    // within BraceHoldSeconds of the last charge at us: keep bracing while that cavalry is near
                    float currentTime = Mission.Current.CurrentTime;
                    bool braceCooldown = BraceCoolingDown(__instance, currentTime);
                    bool braceHeld = !braceCooldown && enemyCav != null && cavDist < 150f &&
                        braceLastThreatTime.TryGetValue(__instance.Formation, out float lastThreat) && currentTime - lastThreat < BraceHoldSeconds;
                    if (braceHeld || ((enemyCav != null) && (cavDist <= signDist) && (enemyCav.CountOfUnits > __instance.Formation.CountOfUnits / 10) && ((signDist > 35f || significantEnemy == enemyCav) || isOnlyCavRemaining)))
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
                                // never a ranged formation (user rule)
                                if (!RBMAI.Utilities.IsRangedForArrangement(__instance.Formation))
                                {
                                    __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderShieldWall);
                                }
                                if (cavDist > 10f)
                                {
                                    __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
                                }
                            }
                            return false;
                        }
                        else
                        {
                            // TargetFormation is sticky: brace only while that cavalry is actually charging us and close
                            OrderType enemyCavOrder = enemyCav.GetReadonlyMovementOrderReference().OrderType;
                            bool enemyCavCharging = enemyCavOrder == OrderType.ChargeWithTarget || enemyCavOrder == OrderType.Charge;
                            bool chargedAt = !(__instance.Formation.AI?.Side == FormationAI.BehaviorSide.Left || __instance.Formation.AI?.Side == FormationAI.BehaviorSide.Right) && enemyCav.TargetFormation == __instance.Formation && enemyCavCharging && cavDist < 150f;
                            if (chargedAt && !braceCooldown)
                            {
                                braceLastThreatTime[__instance.Formation] = currentTime;
                            }
                            if ((chargedAt || braceHeld) && !braceCooldown && !BraceStillAllowed(__instance, currentTime))
                            {
                                // episode ran out: charge as normal for the cooldown
                                braceLastThreatTime.Remove(__instance.Formation);
                                braceCooldown = true;
                            }
                            if ((chargedAt || braceHeld) && !braceCooldown)
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
                                    if (!RBMAI.Utilities.IsRangedForArrangement(__instance.Formation))
                                    {
                                        __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderShieldWall);
                                    }
                                    if (cavDist > 10f)
                                    {
                                        __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
                                    }
                                }
                                return false;
                            }
                        }
                        EndCavalryHold(__instance.Formation, isActiveBehavior);
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
                    EndCavalryHold(__instance.Formation, isActiveBehavior);
                    skirmisherRetreatPositions.Remove(__instance.Formation);
                }

                if (significantEnemy != null && __instance.Formation.QuerySystem.IsInfantryFormation && __instance.Formation.CountOfUnitsWithoutDetachedOnes >= 30)
                {
                    // Riders that clearly outweigh the foot target are charged instead.
                    Formation current = ____currentOrder != null && ____currentOrder.OrderEnum == MovementOrder.MovementOrderEnum.ChargeToTarget ? ____currentOrder.TargetFormation : null;
                    significantEnemy = ChargeTargetChoice.Prefer(__instance.Formation, significantEnemy, current);
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