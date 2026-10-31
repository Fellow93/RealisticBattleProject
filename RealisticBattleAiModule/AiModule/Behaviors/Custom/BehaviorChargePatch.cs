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
            public bool Logged;
        }

        /// <summary>True once per episode, for the brace line (the candidate run starts the episode, the active run logs it).</summary>
        private static bool BraceLogDue(BehaviorCharge behavior)
        {
            BraceEpisode episode = BraceEpisodes.GetOrCreateValue(behavior);
            if (episode.Logged)
            {
                return false;
            }
            episode.Logged = true;
            return true;
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
                episode.Logged = false;
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

        internal const float ChargedAtDistance = 150f; // m centre distance

        /// <summary>
        /// The formation <paramref name="enemy"/>'s charge order aims at, null when it is not charging. Native Formation.SetMovementOrder
        /// ends with SetTargetFormation(null), so an AI formation's Formation.TargetFormation is always null: the target lives in the order.
        /// </summary>
        internal static Formation ChargeTargetOf(Formation enemy)
        {
            if (enemy == null)
            {
                return null;
            }
            MovementOrder order = enemy.GetReadonlyMovementOrderReference();
            if (order.OrderType == OrderType.ChargeWithTarget)
            {
                return order.TargetFormation ?? enemy.TargetFormation;
            }
            if (order.OrderType == OrderType.Charge)
            {
                // a plain charge runs at the nearest enemy
                return enemy.TargetFormation ?? enemy.CachedClosestEnemyFormation?.Formation;
            }
            // AI riders in a formed approach (a Move: CavalryCharge, React strike) are charging too
            return FormedApproach.Target(enemy);
        }

        internal const int BraceMinRiders = 5;

        /// <summary>Riders worth bracing for: more than a tenth of own men, at least BraceMinRiders, not an enemy token force (user rule 2026-10-30: 2 riders made 19 men shield-wall).</summary>
        internal static bool BraceWorthy(Formation own, Formation enemy)
        {
            return enemy != null && enemy.CountOfUnits > own.CountOfUnits / 10 && enemy.CountOfUnits >= BraceMinRiders
                && !ArmyComposition.IsTokenEnemyFormation(own.Team, enemy);
        }

        private static bool IsChargingAt(Formation enemy, Formation own, Vec2 ownCentre, float maxDist, out float dist)
        {
            dist = float.MaxValue;
            if (enemy == null || !BraceWorthy(own, enemy) || !enemy.QuerySystem.IsCavalryFormation || ChargeTargetOf(enemy) != own)
            {
                return false;
            }
            dist = (RBMAI.Utilities.GetFormationCenter(enemy) - ownCentre).Length;
            return dist < maxDist;
        }

        /// <summary>
        /// The nearest significant enemy melee cavalry formation charging <paramref name="own"/> within <paramref name="maxDist"/>, null with none.
        /// Any of them, whatever else is nearer: the nearest-enemy pick missed riders charging past nearer archers or a second group.
        /// </summary>
        internal static Formation ChargingCavalry(Formation own, float maxDist, out float dist)
        {
            dist = float.MaxValue;
            Mission mission = Mission.Current;
            if (own == null || own.Team == null || mission == null)
            {
                return null;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(own);
            Formation best = null;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(own.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (IsChargingAt(enemy, own, centre, maxDist, out float d) && d < dist)
                    {
                        dist = d;
                        best = enemy;
                    }
                }
            }
            return best;
        }

        /// <summary>Log text: every formation ChargingCavalry counts, with its distance.</summary>
        internal static string DescribeChargingCavalry(Formation own, float maxDist)
        {
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(own);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (Team team in Mission.Current.Teams)
            {
                if (team == null || !team.IsEnemyOf(own.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (IsChargingAt(enemy, own, centre, maxDist, out float d))
                    {
                        sb.Append(sb.Length > 0 ? "," : "").Append(TerrainCavalry.FormationName(enemy)).Append('@')
                            .Append(d.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append('m');
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>BEHAVIOR "brace" when a brace starts, naming the riders charging it.</summary>
        internal static void LogBrace(Formation own, string behavior, float now, float maxDist)
        {
            TerrainSpots.EnqueueBehavior(own.Team, own, now, behavior, "brace", DescribeChargingCavalry(own, maxDist), "chargedBy");
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

                    if (enemyCav != null && (!enemyCav.QuerySystem.IsCavalryFormation || !BraceWorthy(__instance.Formation, enemyCav)))
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
                    Formation chargingCav = ChargingCavalry(__instance.Formation, ChargedAtDistance, out float chargingDist);
                    if (chargingCav != null)
                    {
                        enemyCav = chargingCav;
                        cavDist = chargingDist;
                    }
                    bool isOnlyCavRemaining = RBMAI.Utilities.CheckIfOnlyCavRemaining(__instance.Formation);
                    // only horse archers near: no brace and no chase, a leashed push toward them
                    if (HorseArcherPush.TryPush(__instance, __instance.Formation, "Charge", HorseArcherPush.OnlyMountedLeft(__instance.Formation), out WorldPosition haPoint, out Vec2 haFacing))
                    {
                        EndCavalryHold(__instance.Formation, isActiveBehavior);
                        braceLastThreatTime.Remove(__instance.Formation);
                        ____currentOrder = MovementOrder.MovementOrderMove(haPoint);
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(haFacing);
                        if (isActiveBehavior)
                        {
                            __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
                        }
                        return false;
                    }
                    // within BraceHoldSeconds of the last charge at us: keep bracing while that cavalry is near
                    float currentTime = Mission.Current.CurrentTime;
                    bool braceCooldown = BraceCoolingDown(__instance, currentTime);
                    bool braceHeld = !braceCooldown && enemyCav != null && cavDist < 150f &&
                        braceLastThreatTime.TryGetValue(__instance.Formation, out float lastThreat) && currentTime - lastThreat < BraceHoldSeconds;
                    if (braceHeld || chargingCav != null || ((enemyCav != null) && (cavDist <= signDist) && BraceWorthy(__instance.Formation, enemyCav) && ((signDist > 35f || significantEnemy == enemyCav) || isOnlyCavRemaining)))
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
                            // brace only while that cavalry is actually charging us and close
                            // Any side: a flank group charged head-on braces like the main line (the old Left/Right exemption left them to be ridden down).
                            bool chargedAt = chargingCav != null || (ChargeTargetOf(enemyCav) == __instance.Formation && cavDist < 150f);
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
                                    if (BraceLogDue(__instance))
                                    {
                                        LogBrace(__instance.Formation, "Charge", currentTime, ChargedAtDistance);
                                    }
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
                    bool chargingNow = ____currentOrder != null && ____currentOrder.OrderEnum == MovementOrder.MovementOrderEnum.ChargeToTarget;
                    // held riders stay the current choice (KeepRatio): without it hold and foot charge swapped every tick (log 17-59-47)
                    Formation current = chargingNow ? ____currentOrder.TargetFormation : RiderHold.HeldRiders(__instance);
                    significantEnemy = ChargeTargetChoice.Prefer(__instance.Formation, significantEnemy, current);
                    // riders it cannot reach: hold formed facing them instead of chasing (the block scattered over ~270 s, log 16-33-20)
                    if (ChargeTargetChoice.IsMounted(significantEnemy) && ChargeTargetChoice.RidersAllowed(__instance.Formation)
                        && !ChargeTargetChoice.Catchable(__instance.Formation, significantEnemy, chargingNow && significantEnemy == current)
                        && RiderHold.TryHold(__instance, __instance.Formation, significantEnemy, isActiveBehavior, out WorldPosition holdPoint, out Vec2 holdFacing))
                    {
                        ____currentOrder = MovementOrder.MovementOrderMove(holdPoint);
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(holdFacing);
                        if (isActiveBehavior)
                        {
                            if (!RBMAI.Utilities.IsRangedForArrangement(__instance.Formation))
                            {
                                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
                            }
                            __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
                        }
                        return false;
                    }
                    RiderHold.End(__instance, __instance.Formation, isActiveBehavior);
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
            // the shared commit gate (every tactic) on a formation RBM's cavalry charge serves: wear down 0, stay home the kWeak factor
            Formation f = __instance.Formation;
            if (__result > 0f && f != null && f.AI != null)
            {
                global::RBMBehaviorCavalryCharge rbmCharge = f.AI.GetBehavior<global::RBMBehaviorCavalryCharge>();
                if (rbmCharge != null && rbmCharge.WeightFactor > 0f)
                {
                    CavalryCommit.Verdict commit = CavalryCommit.For(f);
                    __result *= (commit.Gate == CavalryCommit.Gate.WearDown) ? 0f : commit.Factor;
                }
            }
        }
    }

    /// <summary>
    /// A charging foot block whose chosen target is riders it cannot reach (ChargeTargetChoice.Catchable) holds formed where it stands,
    /// facing them: the engine still fights riders that ride in, and the charge resumes once they are catchable or foot wins the choice.
    /// Point = its centre when the hold began, moved once the block drifted past half its depth + ReanchorExtra. AI thread, cached data
    /// only; per-behavior state.
    /// </summary>
    internal static class RiderHold
    {
        private const float ReanchorExtra = 10f; // m

        private sealed class State
        {
            public bool Active;
            public Formation Riders;
            public readonly CachedMovePoint Point = new CachedMovePoint();
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorCharge, State> States =
            new System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorCharge, State>();

        internal static bool TryHold(BehaviorCharge owner, Formation formation, Formation riders, bool log, out WorldPosition point, out Vec2 facing)
        {
            point = WorldPosition.Invalid;
            facing = Vec2.Invalid;
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            if (!centre.IsValid || Mission.Current == null)
            {
                return false;
            }
            State state = States.GetOrCreateValue(owner);
            float now = Mission.Current.CurrentTime;
            bool fresh = !state.Active;
            if (!state.Point.TryGet(centre, now, formation.CachedMedianPosition, fresh, formation.Depth / 2f + ReanchorExtra, 0f, out point, out bool rebuilt))
            {
                return false;
            }
            if (log && (fresh || state.Riders != riders))
            {
                TerrainSpots.EnqueueBehavior(formation.Team, formation, now, "Charge", "riderHold", ChargeTargetChoice.Describe(formation, riders), "notCatchable");
            }
            state.Active = true;
            state.Riders = riders;
            Vec2 look = RBMAI.Utilities.GetFormationCenter(riders) - centre;
            facing = (look.LengthSquared > 0.01f) ? look.Normalized() : formation.Direction;
            return true;
        }

        /// <summary>The riders held against now, null when not holding.</summary>
        internal static Formation HeldRiders(BehaviorCharge owner)
        {
            return (States.TryGetValue(owner, out State state) && state.Active) ? state.Riders : null;
        }

        /// <summary>Ends a hold; the active behavior hands its facing back to the enemy.</summary>
        internal static void End(BehaviorCharge owner, Formation formation, bool isActiveBehavior)
        {
            if (States.TryGetValue(owner, out State state) && state.Active)
            {
                state.Active = false;
                state.Riders = null;
                state.Point.Reset();
                if (isActiveBehavior)
                {
                    formation.SetFacingOrder(FacingOrder.FacingOrderLookAtEnemy);
                }
            }
        }
    }

    /// <summary>
    /// Foot against an enemy of horse archers only (only mounted left, no significant melee cavalry within BraceDistance): bracing
    /// does nothing against them and a charge chases riders it never catches, so the formation walks toward the nearest
    /// significant horse-archer formation instead, leashed LeashDistance from the team's position anchor (the anchor itself: from
    /// where its push began: one origin per formation, kept until no push for OriginResetSeconds or enemy foot is back), and holds at
    /// the leash facing them. AI thread, cached data only; per-behavior state + the Formation-keyed origins (cleared in MissionStartReset).
    /// </summary>
    internal static class HorseArcherPush
    {
        internal const float BraceDistance = 150f; // m centre distance, as the brace's
        internal const float LeashDistance = 150f;

        private sealed class State
        {
            public bool Active;
            public bool AtLeash;
            public Formation Target;
            public readonly CachedMovePoint Point = new CachedMovePoint();
        }

        /// <summary>A formation's own leash origin, shared by its Advance and Charge owners: set once, kept through re-takes.</summary>
        private sealed class OriginState
        {
            public Vec2 Origin = Vec2.Invalid;
            public float EndedAt = float.NaN; // NaN while pushing
        }

        internal const float OriginResetSeconds = 15f;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, State> States =
            new System.Runtime.CompilerServices.ConditionalWeakTable<object, State>();

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Formation, OriginState> Origins =
            new System.Collections.Concurrent.ConcurrentDictionary<Formation, OriginState>();

        internal static void Clear()
        {
            Origins.Clear();
        }

        private static Vec2 OwnOrigin(Formation formation, Vec2 centre, float now)
        {
            OriginState o = Origins.GetOrAdd(formation, f => new OriginState());
            lock (o)
            {
                if (!o.Origin.IsValid || (!float.IsNaN(o.EndedAt) && now - o.EndedAt >= OriginResetSeconds))
                {
                    ArmyComposition.Note(formation.Team, formation, ArmyComposition.KindGate, "haPush origin", (o.Origin.IsValid ? "reset after " + ((int)(now - o.EndedAt)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "s idle" : "set")
                        + " at " + ((int)centre.x).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + ((int)centre.y).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    o.Origin = centre;
                }
                o.EndedAt = float.NaN;
                return o.Origin;
            }
        }

        private static void EndOrigin(Formation formation, float now)
        {
            OriginState o;
            if (Origins.TryGetValue(formation, out o))
            {
                lock (o)
                {
                    if (float.IsNaN(o.EndedAt))
                    {
                        o.EndedAt = now;
                    }
                }
            }
        }

        /// <summary>A significant enemy foot formation exists for the formation (the push condition has really ended).</summary>
        private static bool EnemyFootBack(Formation formation)
        {
            foreach (Team team in Mission.Current.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (e != null && e.CountOfUnits > 0 && !ArmyComposition.IsMounted(e) && ThreatSignificance.IsSignificant(formation, e)
                        && !ArmyComposition.IsTokenEnemyFormation(formation.Team, e))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Only mounted enemies left, a token enemy foot group not counting (user rule: a token force never flips an "exists" test).</summary>
        internal static bool OnlyMountedLeft(Formation formation)
        {
            return RBMAI.Utilities.CheckIfOnlyCavRemaining(formation) || (formation?.Team != null && ArmyComposition.EnemyFootTokenOnly(formation.Team));
        }

        internal static bool TryPush(object owner, Formation formation, string behavior, bool onlyMountedLeft, out WorldPosition point, out Vec2 facing)
        {
            point = WorldPosition.Invalid;
            facing = Vec2.Invalid;
            if (owner == null || formation == null || formation.Team == null || Mission.Current == null)
            {
                return false;
            }
            State state = States.GetOrCreateValue(owner);
            float now = Mission.Current.CurrentTime;
            // candidates are calculated too (FindBestBehavior): only the active behavior logs
            bool log = formation.AI != null && formation.AI.ActiveBehavior == owner;
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            Formation target = null;
            string why = null;
            if (onlyMountedLeft && centre.IsValid)
            {
                float best = float.MaxValue;
                bool meleeCavalryNear = false;
                foreach (Team team in Mission.Current.Teams)
                {
                    if (team == null || !team.IsEnemyOf(formation.Team))
                    {
                        continue;
                    }
                    foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
                    {
                        if (e == null || e.CountOfUnits <= 0 || !ThreatSignificance.IsSignificant(formation, e))
                        {
                            continue;
                        }
                        Vec2 c = RBMAI.Utilities.GetFormationCenter(e);
                        if (!c.IsValid)
                        {
                            continue;
                        }
                        float d = c.Distance(centre);
                        if (ArmyComposition.IsHorseArchers(e))
                        {
                            if (d < best)
                            {
                                best = d;
                                target = e;
                            }
                        }
                        else if (ArmyComposition.IsMounted(e) && d <= BraceDistance)
                        {
                            meleeCavalryNear = true;
                        }
                    }
                }
                if (meleeCavalryNear)
                {
                    target = null;
                    why = "meleeCavalryNear";
                }
            }
            if (target == null)
            {
                if (Origins.ContainsKey(formation))
                {
                    if (EnemyFootBack(formation))
                    {
                        if (Origins.TryRemove(formation, out _))
                        {
                            ArmyComposition.Note(formation.Team, formation, ArmyComposition.KindGate, "haPush origin", "cleared enemyFootBack");
                        }
                    }
                    else
                    {
                        EndOrigin(formation, now);
                    }
                }
                if (state.Active)
                {
                    state.Active = false;
                    state.Point.Reset();
                    if (log)
                    {
                        TerrainBattlePosition.Enqueue(formation.Team, formation, now, behavior, "haPush end", "haPush", why ?? (onlyMountedLeft ? "noHorseArchers" : "footBack"));
                    }
                }
                return false;
            }
            Formation anchor = ArmyComposition.AnchorFor(formation);
            Vec2 origin;
            string originName;
            if (anchor != null && RBMAI.Utilities.GetFormationCenter(anchor).IsValid)
            {
                origin = RBMAI.Utilities.GetFormationCenter(anchor);
                originName = "anchor";
            }
            else
            {
                origin = OwnOrigin(formation, centre, now);
                originName = "start";
            }
            Vec2 targetCentre = RBMAI.Utilities.GetFormationCenter(target);
            Vec2 desired = targetCentre;
            Vec2 out_ = desired - origin;
            float reach = out_.Normalize();
            bool atLeash = reach > LeashDistance;
            if (atLeash)
            {
                desired = origin + out_ * LeashDistance;
            }
            bool fresh = !state.Active || state.Target != target;
            // the target's median, else the formation's; no source: no push
            WorldPosition pushPoint;
            if (!state.Point.TryGet(desired, now, target.CachedMedianPosition, formation.CachedMedianPosition, true, false, fresh, out pushPoint))
            {
                return false;
            }
            if (log && (fresh || atLeash != state.AtLeash))
            {
                TerrainBattlePosition.Enqueue(formation.Team, formation, now, behavior, fresh ? "haPush take" : (atLeash ? "haPush leash" : "haPush follow"), "haPush",
                    ThreatSignificance.Describe(formation, target) + " from=" + originName + " reach=" + ((int)reach).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "m leash=" + ((int)LeashDistance).ToString(System.Globalization.CultureInfo.InvariantCulture) + "m " + ArmyComposition.DescribeK(formation.Team));
            }
            state.Active = true;
            state.Target = target;
            state.AtLeash = atLeash;
            point = pushPoint;
            Vec2 look = targetCentre - centre;
            facing = (look.LengthSquared > 0.01f) ? look.Normalized() : formation.Direction;
            return true;
        }
    }
}