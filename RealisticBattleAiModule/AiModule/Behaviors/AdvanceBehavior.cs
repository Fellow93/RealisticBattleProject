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
        private static readonly MethodInfo CalculateCurrentOrderMethod = typeof(BehaviorAdvance).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CurrentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorAdvance __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
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
                            if (allyArchers != null)
                            {
                                Vec2 dir = RBMAI.Utilities.GetFormationCenter(allyArchers) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                                float allyArchersDist = dir.Normalize();
                                if (allyArchersDist - (allyArchers.Width / 2f) - (__instance.Formation.Width / 2f) > 60f)
                                {
                                    ____currentOrder = MovementOrder.MovementOrderMove(RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation));
                                    return false;
                                }
                            }
                        }
                    }
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

                    if ((enemyCav != null) && (cavDist <= signDist) && (enemyCav.CountOfUnits > __instance.Formation.CountOfUnits / 10) && (signDist > 35f))
                    {
                        if (enemyCav.TargetFormation == __instance.Formation && (enemyCav.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget || enemyCav.GetReadonlyMovementOrderReference().OrderType == OrderType.Charge))
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
                            __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderShieldWall);
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
                    Vec2 enemyDirection = -enemyQuerySystem.Formation.Direction;

                    float currentTime = Mission.Current.CurrentTime;

                    // Detect if behavior was just (re)applied after being inactive
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
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                            return false;
                        }
                        advanceTimerStorage.Remove(__instance.Formation);
                        // Restart scale ramp after the fallback timer expires
                        scaleStartTime = currentTime;
                        advanceScaleStartStorage[__instance.Formation] = scaleStartTime;
                    }

                    float scaleT = MBMath.ClampFloat((currentTime - scaleStartTime) / 10f, 0f, 1f);
                    // Keep the ordered position ahead of our own centre at ALL times. The ramp eases only the
                    // extra reach (0.5x -> 1x over 10s); it must never scale the whole offset down to ~0.
                    // Advance re-activates after every Regroup interlude (Regroup out-weighs Advance while the
                    // line is spread), which resets scaleT to 0. If the offset went to 0 the order became
                    // "move onto our own centroid", collapsing the leading ranks backwards -- the line then
                    // accordions back and forth instead of advancing. Flooring the offset keeps it pressing forward.
                    float baseOffset = MBMath.ClampFloat(enemyDistance * 0.3f, 10f, 50f) + __instance.Formation.Depth * 0.5f;
                    float advanceOffset = baseOffset * MBMath.Lerp(0.5f, 1f, scaleT);
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
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);

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
            if (__instance.Formation.QuerySystem.IsInfantryFormation)
            {
                switch (__instance.Formation.ArrangementOrder.OrderType)
                {
                    case OrderType.ArrangementLine:
                        {
                            // Divisor is effectively the rank count (width in metres = units / ranks). Lower = wider
                            // line, fewer ranks. Widen the advancing line a touch (4.5 -> 4.0 ranks).
                            __instance.Formation.SetFormOrder(FormOrder.FormOrderCustom(__instance.Formation.CountOfUnitsWithoutDetachedOnes / 4.0f), true);
                            break;
                        }
                    case OrderType.ArrangementLoose:
                        {
                            __instance.Formation.SetFormOrder(FormOrder.FormOrderCustom(__instance.Formation.CountOfUnitsWithoutDetachedOnes / 2.75f), true);
                            break;
                        }
                    case OrderType.ArrangementCloseOrder:
                        {
                            __instance.Formation.SetFormOrder(FormOrder.FormOrderCustom(__instance.Formation.CountOfUnitsWithoutDetachedOnes / 7f), true);
                            break;
                        }
                }

                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
                if (significantEnemy != null)
                {
                    float num = RBMAI.Utilities.GetFormationDistance(__instance.Formation, significantEnemy);
                    if (num < 150f && __instance.Formation.CountOfUnitsWithoutDetachedOnes >= 30)
                    {
                        __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
                        Utilities.DecideArrangementOrderForFormation(__instance.Formation);
                        __instance.Formation.SetMovementOrder(____currentOrder);
                    }
                }
            }
            __instance.Formation.SetMovementOrder(__instance.CurrentOrder);
            return false;
        }
    }
}
