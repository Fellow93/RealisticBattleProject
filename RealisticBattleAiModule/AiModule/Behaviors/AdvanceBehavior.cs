using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.ArrangementOrder;
using static TaleWorlds.MountAndBlade.HumanAIComponent;
namespace RBMAI
{
    [HarmonyPatch(typeof(BehaviorAdvance))]
    internal class OverrideBehaviorAdvance
    {
        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };
        public static Dictionary<Formation, int> waitCountStorage = new Dictionary<Formation, int> { };
        public static Dictionary<Formation, float> advanceTimerStorage = new Dictionary<Formation, float> { };
        public static Dictionary<Formation, float> advanceScaleStartStorage = new Dictionary<Formation, float> { };
        public static Dictionary<Formation, float> advanceLastTickStorage = new Dictionary<Formation, float> { };
        public static Dictionary<Formation, float> archerWaitStartStorage = new Dictionary<Formation, float> { };
        // FRONT-centre hold point fixed at the wait's start (a Move point is the front-centre; re-reading the centre walks the block back)
        public static Dictionary<Formation, WorldPosition> archerWaitHoldStorage = new Dictionary<Formation, WorldPosition> { };
        public static Dictionary<Formation, float> lastDispersalStorage = new Dictionary<Formation, float> { };
        private const float MaxArcherWaitSeconds = 30f;
        private const float ArcherWaitRearmSeconds = 90f;
        private const float BraceDistance = 150f; // m, same as BehaviorChargePatch
        private const float ReshapeWidthFraction = 0.15f;
        private const float ReshapeDispersalCooldown = 10f; // s; dispersal is a multi-ms main-thread stall
        private static readonly MethodInfo CalculateCurrentOrderMethod = typeof(BehaviorAdvance).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Per behavior: bracing now / on the previous tick (TickOccasionally leaves arrangement and width alone while bracing).</summary>
        private sealed class BraceState
        {
            public bool Bracing;
            public bool WasBracing;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorAdvance, BraceState> BraceStates =
            new System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorAdvance, BraceState>();
        private static readonly FieldInfo CurrentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float ParallelFacingDistance = 150f; // m, centre to centre
        private const float ParallelFacingMaxDegrees = 45f;
        private static readonly float ParallelFacingCos = MathF.Cos(ParallelFacingMaxDegrees * MathF.PI / 180f);
        private static readonly float FacingDeadbandCos = MathF.Cos(5f * MathF.PI / 180f);

        // far out face the way we walk; close in square up to the enemy line unless it stands obliquely to us
        private static Vec2 ApproachFacing(Vec2 directionToEnemy, Vec2 enemyFacing, float enemyDistance)
        {
            Vec2 mirror = -enemyFacing;
            if (enemyDistance > ParallelFacingDistance || !mirror.IsValid || mirror.LengthSquared < 0.01f)
            {
                return directionToEnemy;
            }
            mirror = mirror.Normalized();
            return (mirror.DotProduct(directionToEnemy) >= ParallelFacingCos) ? mirror : directionToEnemy;
        }

        // native re-applies the facing every tick; ours used to only within 150 m, so the activation facing stuck for the whole approach
        private static void ApplyFacing(Formation formation, FacingOrder facing)
        {
            FacingOrder current = formation.FacingOrder;
            if (facing.OrderEnum == FacingOrder.FacingOrderEnum.LookAtDirection && current.OrderEnum == FacingOrder.FacingOrderEnum.LookAtDirection
                && current.GetDirection(formation).DotProduct(facing.GetDirection(formation)) >= FacingDeadbandCos)
            {
                return;
            }
            formation.SetFacingOrder(facing);
        }

        /// <summary>AI thread: front-centre as a copy of the cached median moved ONCE; the caller stores it (native order-position cache).</summary>
        private static WorldPosition FrontHoldPosition(Formation formation)
        {
            WorldPosition hold = formation.CachedMedianPosition;
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            Vec2 facing = formation.Direction;
            if (!hold.IsValid || !centre.IsValid)
            {
                return hold;
            }
            if (facing.IsValid && facing.LengthSquared > 0.01f)
            {
                centre += facing.Normalized() * (formation.Depth * 0.5f);
            }
            hold.SetVec2(centre);
            return hold;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorAdvance __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            // runs for player-led formations too: don't force ShieldWall onto them
            BraceState brace = BraceStates.GetOrCreateValue(__instance);
            brace.Bracing = false;
            if (__instance.Formation != null && !__instance.Formation.IsAIControlled)
            {
                return true;
            }
            if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

                if (__instance.Formation.QuerySystem.IsInfantryFormation && !RBMAI.Utilities.FormationFightingInMelee(__instance.Formation, 0.5f))
                {
                    if (__instance.Formation?.Team?.TeamAI != null)
                    {
                        if (CurrentTacticField.GetValue(__instance.Formation?.Team?.TeamAI) != null && CurrentTacticField.GetValue(__instance.Formation?.Team?.TeamAI).ToString().Contains("SplitArchers"))
                        {
                            Formation allyArchers = Utilities.FindSignificantAlly(__instance.Formation, false, true, false, false, false);
                            bool archersLagging = false;
                            // archers holding a terrain spot release it themselves: never wait for them
                            if (allyArchers != null && !TerrainSpots.IsHolding(allyArchers))
                            {
                                Vec2 dir = RBMAI.Utilities.GetFormationCenter(allyArchers) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                                float allyArchersDist = dir.Normalize();
                                archersLagging = allyArchersDist - (allyArchers.Width / 2f) - (__instance.Formation.Width / 2f) > 60f;
                            }
                            if (archersLagging)
                            {
                                // wait for archers to close up, but capped; the clock resets once they catch up
                                float waitNow = Mission.Current.CurrentTime;
                                if (!archerWaitStartStorage.TryGetValue(__instance.Formation, out float waitStart))
                                {
                                    waitStart = waitNow;
                                    archerWaitStartStorage[__instance.Formation] = waitStart;
                                    archerWaitHoldStorage[__instance.Formation] = FrontHoldPosition(__instance.Formation);
                                }
                                if (waitNow - waitStart < MaxArcherWaitSeconds)
                                {
                                    if (!archerWaitHoldStorage.TryGetValue(__instance.Formation, out WorldPosition hold) || !hold.IsValid)
                                    {
                                        hold = FrontHoldPosition(__instance.Formation);
                                        archerWaitHoldStorage[__instance.Formation] = hold;
                                    }
                                    ____currentOrder = MovementOrder.MovementOrderMove(hold);
                                    return false;
                                }
                            }
                            else if (archerWaitStartStorage.TryGetValue(__instance.Formation, out float lastWaitStart)
                                && Mission.Current.CurrentTime - lastWaitStart >= ArcherWaitRearmSeconds)
                            {
                                // re-armed only ArcherWaitRearmSeconds after the last wait began
                                archerWaitStartStorage.Remove(__instance.Formation);
                                archerWaitHoldStorage.Remove(__instance.Formation);
                            }
                        }
                    }
                    Formation enemyCav = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);

                    if (enemyCav != null && !enemyCav.QuerySystem.IsCavalryFormation)
                    {
                        enemyCav = null;
                    }

                    // only horse archers left near: a leashed push toward them instead of advancing after riders (as BehaviorCharge)
                    // any enemy foot makes significantEnemy foot: the list walk only when it is not
                    bool onlyMountedLeft = (significantEnemy == null || ArmyComposition.IsMounted(significantEnemy) || ArmyComposition.IsTokenEnemyFormation(__instance.Formation.Team, significantEnemy))
                        && AiModule.RbmBehaviors.HorseArcherPush.OnlyMountedLeft(__instance.Formation);
                    if (AiModule.RbmBehaviors.HorseArcherPush.TryPush(__instance, __instance.Formation, "Advance", onlyMountedLeft, out WorldPosition haPoint, out Vec2 haFacing))
                    {
                        positionsStorage.Remove(__instance.Formation);
                        ____currentOrder = MovementOrder.MovementOrderMove(haPoint);
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(haFacing);
                        return false;
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
                    // any riders charging us, whatever else is nearer
                    Formation chargingCav = AiModule.RbmBehaviors.OverrideBehaviorCharge.ChargingCavalry(__instance.Formation, BraceDistance, out float chargingDist);
                    if (chargingCav != null)
                    {
                        enemyCav = chargingCav;
                        cavDist = chargingDist;
                    }

                    if (chargingCav != null || ((enemyCav != null) && (cavDist <= signDist) && AiModule.RbmBehaviors.OverrideBehaviorCharge.BraceWorthy(__instance.Formation, enemyCav) && (signDist > 35f)))
                    {
                        if (cavDist < BraceDistance && AiModule.RbmBehaviors.OverrideBehaviorCharge.ChargeTargetOf(enemyCav) == __instance.Formation)
                        {
                            Vec2 vec = RBMAI.Utilities.GetFormationCenter(enemyCav) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                            WorldPosition positionNew = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);

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
                            if (cavDist > 10f)
                            {
                                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec.Normalized());
                            }
                            // only while Advance is active (FindBestBehavior also runs this on a candidate; the flip re-applies behaviour values to every agent)
                            // never a ranged formation (user rule): it holds without the wall
                            if (__instance.Formation.AI != null && __instance.Formation.AI.ActiveBehavior == __instance && !RBMAI.Utilities.IsRangedForArrangement(__instance.Formation))
                            {
                                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderShieldWall);
                            }
                            brace.Bracing = true;
                            return false;
                        }
                        positionsStorage.Remove(__instance.Formation);
                    }
                    else if (significantEnemy != null && signDist < 60f && RBMAI.Utilities.FormationActiveSkirmishersRatio(__instance.Formation, 0.33f))
                    {
                        WorldPosition positionNew = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);

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
                        return false;
                    }
                    positionsStorage.Remove(__instance.Formation);
                }

                if (significantEnemy != null)
                {
                    FormationQuerySystem enemyQuerySystem = significantEnemy.QuerySystem;

                    Vec2 directionToEnemy = RBMAI.Utilities.GetFormationCenter(enemyQuerySystem.Formation) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                    float enemyDistance = directionToEnemy.Normalize();

                    float currentTime = Mission.Current.CurrentTime;

                    float lastTick;
                    advanceLastTickStorage.TryGetValue(__instance.Formation, out lastTick);
                    bool justApplied = currentTime - lastTick > 2f;
                    advanceLastTickStorage[__instance.Formation] = currentTime;

                    float scaleStartTime;
                    if (!advanceScaleStartStorage.TryGetValue(__instance.Formation, out scaleStartTime) || justApplied)
                    {
                        scaleStartTime = currentTime;
                        advanceScaleStartStorage[__instance.Formation] = scaleStartTime;
                    }

                    float invalidDetectedTime;
                    if (advanceTimerStorage.TryGetValue(__instance.Formation, out invalidDetectedTime))
                    {
                        if (currentTime - invalidDetectedTime < 20f)
                        {
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(ApproachFacing(directionToEnemy, enemyQuerySystem.Formation.Direction, enemyDistance));
                            return false;
                        }
                        advanceTimerStorage.Remove(__instance.Formation);
                        scaleStartTime = currentTime;
                        advanceScaleStartStorage[__instance.Formation] = scaleStartTime;
                    }

                    float scaleT = MBMath.ClampFloat((currentTime - scaleStartTime) / 10f, 0f, 1f);
                    // keep the order ahead of our centre at all times; the ramp may only ease the extra reach (offset ~0 collapses the leading ranks)
                    float baseOffset = MBMath.ClampFloat(enemyDistance * 0.3f, 10f, 50f) + __instance.Formation.Depth * 0.5f;
                    float advanceOffset = baseOffset * MBMath.Lerp(0.5f, 1f, scaleT);
                    // never order past the enemy; small standoff so contact is made by the charge
                    float maxOffset = enemyDistance - 5f;
                    if (advanceOffset > maxOffset)
                    {
                        advanceOffset = maxOffset;
                    }
                    if (advanceOffset < 3f)
                    {
                        advanceOffset = 3f;
                    }
                    // attacker's main infantry on a terrain approach route: same reach, along the route's leg
                    if (TerrainApproach.TryLeg(__instance, __instance.Formation, advanceOffset, out WorldPosition legTarget, out Vec2 legFacing, out bool lastLeg))
                    {
                        ____currentOrder = MovementOrder.MovementOrderMove(legTarget);
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(lastLeg ? directionToEnemy : legFacing);
                        return false;
                    }
                    Vec2 advanceVec2 = RBMAI.Utilities.GetFormationCenter(__instance.Formation) + directionToEnemy * advanceOffset;
                    WorldPosition advancePosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                    advancePosition.SetVec2(advanceVec2);

                    if (Mission.Current.IsPositionInsideBoundaries(advanceVec2) && advancePosition.GetNavMesh() != UIntPtr.Zero)
                    {
                        ____currentOrder = MovementOrder.MovementOrderMove(advancePosition);
                    }
                    else
                    {
                        WorldPosition enemyPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(enemyQuerySystem.Formation);
                        enemyPosition.SetVec2(enemyPosition.AsVec2 + enemyQuerySystem.Formation.Direction * enemyQuerySystem.Formation.Depth * 0.5f);
                        advanceTimerStorage[__instance.Formation] = currentTime;
                        ____currentOrder = MovementOrder.MovementOrderMove(enemyPosition);
                    }
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(ApproachFacing(directionToEnemy, enemyQuerySystem.Formation.Direction, enemyDistance));

                    return false;
                }
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static bool PrefixTickOccasionally(ref BehaviorAdvance __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder,
            ref bool ____isInShieldWallDistance, ref bool ____switchedToShieldWallRecently, ref Timer ____switchedToShieldWallTimer)
        {
            CalculateCurrentOrderMethod.Invoke(__instance, new object[] { });

            __instance.Formation.SetMovementOrder(__instance.CurrentOrder);
            BraceState brace = BraceStates.GetOrCreateValue(__instance);
            if (!brace.Bracing && __instance.Formation.IsAIControlled)
            {
                ApplyFacing(__instance.Formation, ___CurrentFacingOrder);
            }
            if (brace.Bracing && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                // bracing against charging cavalry: no width/shape/arrangement decision this tick
                if (!brace.WasBracing)
                {
                    AiModule.RbmBehaviors.OverrideBehaviorCharge.LogBrace(__instance.Formation, "Advance", Mission.Current.CurrentTime, BraceDistance);
                }
                brace.WasBracing = true;
                __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
                return false;
            }
            bool braceEnded = brace.WasBracing;
            brace.WasBracing = false;
            if (__instance.Formation.QuerySystem.IsInfantryFormation)
            {
                if (braceEnded && __instance.Formation.IsAIControlled && !TerrainShape.DecideArrangementInstead(__instance.Formation))
                {
                    // brace over: one arrangement decision before the width is picked
                    Utilities.DecideArrangementOrderForFormation(__instance.Formation);
                }
                float widthBefore = __instance.Formation.Width;
                // TerrainShape: Loose on broken ground, narrower width on narrow ground (before the width is picked)
                TerrainShape.Tick(__instance.Formation, "Advance");
                TerrainShape.ApplyArrangement(__instance.Formation);
                switch (__instance.Formation.ArrangementOrder.OrderType)
                {
                    case OrderType.ArrangementLine:
                        {
                            // divisor = rank count (width m = units / ranks)
                            __instance.Formation.SetFormOrder(FormOrder.FormOrderCustom(TerrainShape.AdjustWidth(__instance.Formation, RBMAI.Utilities.EnforceMinFileWidth(__instance.Formation, __instance.Formation.CountOfUnitsWithoutDetachedOnes / 4.0f))), true);
                            break;
                        }
                    case OrderType.ArrangementLoose:
                        {
                            __instance.Formation.SetFormOrder(FormOrder.FormOrderCustom(TerrainShape.AdjustWidth(__instance.Formation, RBMAI.Utilities.EnforceMinFileWidth(__instance.Formation, __instance.Formation.CountOfUnitsWithoutDetachedOnes / 2.75f))), true);
                            break;
                        }
                    case OrderType.ArrangementCloseOrder:
                        {
                            __instance.Formation.SetFormOrder(FormOrder.FormOrderCustom(TerrainShape.AdjustWidth(__instance.Formation, RBMAI.Utilities.EnforceMinFileWidth(__instance.Formation, __instance.Formation.CountOfUnitsWithoutDetachedOnes / 7f))), true);
                            break;
                        }
                }

                // widening re-lays the grid BY INDEX (men get far-flank cells); native dispersal reassigns cells to the closest man. Main thread.
                // NOT cheap (n^2 Agent.Position calls): only a real re-shape may disperse, never per-file drift
                if (MathF.Abs(__instance.Formation.Width - widthBefore) > widthBefore * ReshapeWidthFraction)
                {
                    float now = Mission.Current.CurrentTime;
                    if (!lastDispersalStorage.TryGetValue(__instance.Formation, out float lastDispersal) || now - lastDispersal >= ReshapeDispersalCooldown)
                    {
                        lastDispersalStorage[__instance.Formation] = now;
                        __instance.Formation.OnFormationDispersed();
                    }
                }

                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
                if (significantEnemy != null)
                {
                    float num = RBMAI.Utilities.GetFormationDistance(__instance.Formation, significantEnemy);
                    if (num < 150f && __instance.Formation.CountOfUnitsWithoutDetachedOnes >= 30)
                    {
                        __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
                        // broken ground: stays Loose (a shield wall still wins)
                        if (!TerrainShape.DecideArrangementInstead(__instance.Formation))
                        {
                            Utilities.DecideArrangementOrderForFormation(__instance.Formation);
                        }
                        __instance.Formation.SetMovementOrder(____currentOrder);
                    }
                }
            }
            else if (!__instance.Formation.QuerySystem.IsCavalryFormation && !__instance.Formation.QuerySystem.IsRangedCavalryFormation)
            {
                // foot archers: only the ground's loose / narrow adjustment
                TerrainShape.Tick(__instance.Formation, "Advance");
                TerrainShape.ApplyArrangement(__instance.Formation);
                TerrainShape.ApplyFormWidth(__instance.Formation);
            }
            __instance.Formation.SetMovementOrder(__instance.CurrentOrder);
            return false;
        }
    }
}
