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
    [HarmonyPatch(typeof(BehaviorSkirmishLine))]
    internal class OverrideBehaviorSkirmishLine
    {
        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(Formation ____mainFormation, ref FacingOrder ___CurrentFacingOrder)
        {
            if (____mainFormation != null)
            {
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(____mainFormation.Direction);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref BehaviorSkirmishLine __instance)
        {
            // Loose as always, Line / Loose by the situation for a ranged formation (Utilities.ChooseRangedArrangement).
            if (!RBMAI.Utilities.TryApplyRangedArrangement(__instance.Formation, ArrangementOrder.ArrangementOrderEnum.Loose))
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
            }
            // ~4 ranks deep for foot archers (Utilities.TrySetRangedWidth; was a fixed 110 m), else 110 m as before.
            if (!RBMAI.Utilities.TrySetRangedWidth(__instance.Formation))
            {
                __instance.Formation.SetFormOrder(FormOrder.FormOrderCustom(110f));
            }
        }

        // Native's tick sets no arrangement: re-check RBM's ranged Line / Loose choice (ranged formations only).
        [HarmonyPostfix]
        [HarmonyPatch("TickOccasionally")]
        private static void PostfixTickOccasionally(BehaviorSkirmishLine __instance)
        {
            if (__instance.Formation != null)
            {
                RBMAI.Utilities.TryApplyRangedArrangement(__instance.Formation, ArrangementOrder.ArrangementOrderEnum.Loose);
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorScreenedSkirmish))]
    internal class OverrideBehaviorScreenedSkirmish
    {
        private static readonly MethodInfo CalculateCurrentOrderMethod = typeof(BehaviorScreenedSkirmish).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);

        // The flank side flips at 60 deg and only back inside ~45 deg, at most once per FlankHoldSeconds.
        private const float FlankEnterDot = 0.5f;
        private const float FlankExitDot = 0.7f;
        private const float FlankHoldSeconds = 10f;
        private const float FacingKeepDot = 0.985f; // 10 deg
        private const float MoveKeepMin = 5f; // m; native re-orders only after the point moved its own depth

        private sealed class ScreenState
        {
            public readonly CachedMovePoint Move = new CachedMovePoint();
            public bool Flanked;
            public float FlankChangedAt = float.NegativeInfinity;
            public Vec2 Facing = Vec2.Invalid;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorScreenedSkirmish, ScreenState> States =
            new System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorScreenedSkirmish, ScreenState>();

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(ref Formation ____mainFormation, ref BehaviorScreenedSkirmish __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            Compute(__instance, ref ____mainFormation, ref ____currentOrder, ref ___CurrentFacingOrder);
        }

        /// <summary>AI thread: the screen point behind the main infantry. Cached data only, one SetVec2MT per rebuild (CachedMovePoint).
        /// False when there is no infantry main (native's own order then stands).</summary>
        private static bool Compute(BehaviorScreenedSkirmish behavior, ref Formation main, ref MovementOrder currentOrder, ref FacingOrder facingOrder)
        {
            Formation formation = behavior.Formation;
            if (formation == null)
            {
                return false;
            }
            if (main != null && (main.CountOfUnits == 0 || !main.QuerySystem.IsInfantryFormation))
            {
                main = formation.Team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).FirstOrDefault((Formation f) => f.AI.IsMainFormation);
            }
            if (main == null || main.CountOfUnits <= 0 || !main.QuerySystem.IsInfantryFormation)
            {
                return false;
            }
            ScreenState state = States.GetOrCreateValue(behavior);
            float now = Mission.Current.CurrentTime;

            // Screen direction (gap audit R1): the main's facing, or the bearing main -> the closest enemy once that enemy is off
            // the main's front, so flanked archers stand on the main's far side from it. Measured from the main so the archers' own
            // move cannot flip it. Horse archers circle and never set the side.
            Vec2 front = main.Direction.Normalized();
            Vec2 toEnemy = Vec2.Invalid;
            FormationQuerySystem closestEnemy = formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
            if (closestEnemy != null && closestEnemy.Formation != null && !closestEnemy.IsRangedCavalryFormation)
            {
                Vec2 d = closestEnemy.Formation.CachedMedianPosition.AsVec2 - main.CachedMedianPosition.AsVec2;
                if (d.LengthSquared > 1f)
                {
                    toEnemy = d.Normalized();
                }
            }
            bool wantFlanked = toEnemy.IsValid && front.DotProduct(toEnemy) < (state.Flanked ? FlankExitDot : FlankEnterDot);
            bool sideChanged = false;
            if (wantFlanked != state.Flanked && now - state.FlankChangedAt >= FlankHoldSeconds)
            {
                state.Flanked = wantFlanked;
                state.FlankChangedAt = now;
                sideChanged = true;
            }
            Vec2 screen = (state.Flanked && toEnemy.IsValid) ? toEnemy : front;

            if (!state.Facing.IsValid || state.Facing.DotProduct(screen) < FacingKeepDot)
            {
                state.Facing = screen;
            }
            facingOrder = FacingOrder.FacingOrderLookAtDirection(state.Facing);

            Vec2 centre = RBMAI.Utilities.GetFormationCenter(main);
            float gap = formation.QuerySystem.IsRangedCavalryFormation ? 15f : 5f;
            Vec2 desired = centre - screen * (main.Depth / 2f + formation.Depth / 2f + gap);
            float keep = Math.Max(MoveKeepMin, formation.Depth);
            if ((sideChanged || !state.Move.IsFresh(desired, now, false, keep, 0f)) && !Mission.Current.IsPositionInsideBoundaries(desired))
            {
                // off the map: keep the held point, else stand on the main
                desired = state.Move.Desired.IsValid ? state.Move.Desired : centre;
            }
            WorldPosition point;
            bool rebuilt;
            if (!state.Move.TryGet(desired, now, main.CachedMedianPosition, sideChanged, keep, 0f, out point, out rebuilt))
            {
                return false;
            }
            currentOrder = MovementOrder.MovementOrderMove(point);
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static bool PrefixTickOccasionally(BehaviorScreenedSkirmish __instance, ref Formation ____mainFormation, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (!Compute(__instance, ref ____mainFormation, ref ____currentOrder, ref ___CurrentFacingOrder))
            {
                CalculateCurrentOrderMethod.Invoke(__instance, new object[] { });
            }
            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
            RBMAI.Utilities.TryApplyRangedArrangement(__instance.Formation, ArrangementOrder.ArrangementOrderEnum.Loose);
            return false;
        }

        // A fresh activation places the screen anew (no held point, side or facing from an earlier stint). Foot ranged under RBM's
        // arrangement rule skip native's activation: its Scatter + Wide right before the postfix's Line / Loose + width reshaped them twice.
        [HarmonyPrefix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static bool PrefixOnBehaviorActivatedAux(BehaviorScreenedSkirmish __instance, ref Formation ____mainFormation, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            States.Remove(__instance);
            Formation formation = __instance.Formation;
            if (!RBMAI.Utilities.IsRangedArrangementSubject(formation))
            {
                return true;
            }
            if (!Compute(__instance, ref ____mainFormation, ref ____currentOrder, ref ___CurrentFacingOrder))
            {
                CalculateCurrentOrderMethod.Invoke(__instance, new object[] { });
            }
            formation.SetMovementOrder(____currentOrder);
            formation.SetFacingOrder(___CurrentFacingOrder);
            formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref BehaviorScreenedSkirmish __instance)
        {
            // Loose as always, Line / Loose by the situation for a ranged formation (Utilities.ChooseRangedArrangement).
            if (!RBMAI.Utilities.TryApplyRangedArrangement(__instance.Formation, ArrangementOrder.ArrangementOrderEnum.Loose))
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
            }
            // ~4 ranks deep for foot archers instead of native Wide (Utilities.TrySetRangedWidth).
            RBMAI.Utilities.TrySetRangedWidth(__instance.Formation);
        }
    }

    [HarmonyPatch(typeof(BehaviorCautiousAdvance))]
    internal class OverrideBehaviorCautiousAdvance
    {
        private enum BehaviorState
        {
            Approaching,
            Shooting,
            PullingBack
        }

        public static Dictionary<Formation, int> waitCountShootingStorage = new Dictionary<Formation, int> { };
        public static Dictionary<Formation, int> waitCountApproachingStorage = new Dictionary<Formation, int> { };

        // Attacker stepping state: when the next step is due and where the current step leads.
        public static Dictionary<Formation, float> attackerNextStepTime = new Dictionary<Formation, float> { };
        public static Dictionary<Formation, Vec2> attackerStepTarget = new Dictionary<Formation, Vec2> { };

        /// <summary>USER RULE (2026-10-04): an attacker's advance phase must actually advance. While its archers shoot,
        /// the attacker's infantry still closes this far every StepInterval (about 1.5 m/s), at any distance.</summary>
        private const float StepMeters = 6f;
        private const float StepInterval = 4f;

        /// <summary>With no usable archer formation the infantry advances like BehaviorAdvance: dist * this, clamped.</summary>
        private const float PlainAdvanceFraction = 0.3f;
        private const float PlainAdvanceMin = 10f;
        private const float PlainAdvanceMax = 50f;

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(ref Vec2 ____shootPosition, ref Formation ____archerFormation, BehaviorCautiousAdvance __instance, ref BehaviorState ____behaviorState, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (__instance.Formation != null && __instance.Formation.Team != null && __instance.Formation.Team.IsAttacker
                && Mission.Current != null && Mission.Current.IsFieldBattle)
            {
                AttackerCautiousAdvance(__instance.Formation, ref ____shootPosition, ____archerFormation, ref ____behaviorState, ref ____currentOrder, ref ___CurrentFacingOrder);
                return;
            }
            if (__instance.Formation != null && ____archerFormation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, false);

                if (significantEnemy != null)
                {
                    int waitCountShooting = 0;
                    int waitCountApproaching = 0;
                    if (!waitCountShootingStorage.TryGetValue(__instance.Formation, out waitCountShooting))
                    {
                        waitCountShootingStorage[__instance.Formation] = 0;
                    }
                    if (!waitCountApproachingStorage.TryGetValue(__instance.Formation, out waitCountApproaching))
                    {
                        waitCountApproachingStorage[__instance.Formation] = 0;
                    }

                    Vec2 vec = RBMAI.Utilities.GetFormationCenter(significantEnemy) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                    float distance = vec.Normalize();

                    switch (____behaviorState)
                    {
                        case BehaviorState.Shooting:
                            {
                                if (waitCountShootingStorage[__instance.Formation] > 70)
                                {
                                    if (distance > 100f)
                                    {
                                        WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                                        medianPosition.SetVec2(medianPosition.AsVec2 + vec * 5f);
                                        ____shootPosition = medianPosition.AsVec2 + vec * 5f;
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                    waitCountShootingStorage[__instance.Formation] = 0;
                                    waitCountApproachingStorage[__instance.Formation] = 0;
                                }
                                else
                                {
                                    if (distance > 100f)
                                    {
                                        waitCountShootingStorage[__instance.Formation] = waitCountShootingStorage[__instance.Formation] + 2;
                                    }
                                    else
                                    {
                                        waitCountShootingStorage[__instance.Formation] = waitCountShootingStorage[__instance.Formation] + 1;
                                    }
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                }
                                break;
                            }
                        case BehaviorState.Approaching:
                            {
                                if (distance > 160f)
                                {
                                    WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                                    medianPosition.SetVec2(medianPosition.AsVec2 + vec * 10f);
                                    ____shootPosition = medianPosition.AsVec2 + vec * 10f;
                                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                }
                                else
                                {
                                    if (waitCountApproachingStorage[__instance.Formation] > 35)
                                    {
                                        if (distance < 150f)
                                        {
                                            WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                                            medianPosition.SetVec2(medianPosition.AsVec2 + vec * 5f);
                                            ____shootPosition = medianPosition.AsVec2 + vec * 5f;
                                            ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                        }

                                        waitCountApproachingStorage[__instance.Formation] = 0;
                                    }
                                    else
                                    {
                                        // Native invalidates _shootPosition on entering Approaching; never move to NaN.
                                        if (distance < 150f && ____shootPosition.IsValid)
                                        {
                                            WorldPosition medianPosition = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;
                                            medianPosition.SetVec2(____shootPosition);
                                            ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                        }
                                        waitCountApproachingStorage[__instance.Formation] = waitCountApproachingStorage[__instance.Formation] + 1;
                                    }
                                }
                                break;
                            }
                        case BehaviorState.PullingBack:
                            {
                                if (waitCountApproachingStorage[__instance.Formation] > 30)
                                {
                                    if (distance < 150f)
                                    {
                                        WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                                        medianPosition.SetVec2(medianPosition.AsVec2 - vec * 10f);
                                        ____shootPosition = medianPosition.AsVec2 + vec * 5f;
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                    waitCountApproachingStorage[__instance.Formation] = 0;
                                }
                                else
                                {
                                    if (distance < 150f && ____shootPosition.IsValid)
                                    {
                                        WorldPosition medianPosition = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;
                                        medianPosition.SetVec2(____shootPosition);
                                        ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
                                    }
                                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                                    waitCountApproachingStorage[__instance.Formation] = waitCountApproachingStorage[__instance.Formation] + 1;
                                }
                                break;
                            }
                    }
                }
            }
        }

        // Formation shape by ground (TerrainShape, terrainTacticsEnabled; off = no-op), after native's own arrangement
        // choice: broken ground turns a Line Loose (native's shield wall is never replaced), narrow ground narrows the
        // width. The order itself is not touched, so the attacker's stepping advance goes on unchanged.
        [HarmonyPostfix]
        [HarmonyPatch("TickOccasionally")]
        private static void PostfixTickOccasionally(BehaviorCautiousAdvance __instance)
        {
            Formation formation = __instance?.Formation;
            if (formation == null)
            {
                return;
            }
            TerrainShape.Tick(formation, "CautiousAdvance");
            TerrainShape.ApplyArrangement(formation);
            TerrainShape.ApplyFormWidth(formation);
        }

        // Field-battle attacker: the infantry may shoot-and-wait with its archers, but never stands still or
        // retreats. Native Approaching (march at the enemy) is kept; Shooting (hold at the shoot position, even
        // forever if no archer formation was found at activation) and PullingBack are replaced by a steady
        // StepMeters-per-StepInterval walk forward. Main thread (team tick).
        private static void AttackerCautiousAdvance(Formation formation, ref Vec2 shootPosition, Formation archerFormation,
            ref BehaviorState behaviorState, ref MovementOrder currentOrder, ref FacingOrder facingOrder)
        {
            Formation enemy = RBMAI.Utilities.FindSignificantEnemy(formation, true, true, false, false, false, false)
                ?? formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation?.Formation
                ?? formation.CachedClosestEnemyFormation?.Formation;
            if (enemy == null || enemy.CountOfUnits <= 0)
            {
                return;
            }
            Vec2 center = RBMAI.Utilities.GetFormationCenter(formation);
            Vec2 vec = RBMAI.Utilities.GetFormationCenter(enemy) - center;
            float distance = vec.Normalize();
            if (!vec.IsValid || distance < 0.1f)
            {
                return;
            }

            bool archersUsable = archerFormation != null && archerFormation.CountOfUnits > 0
                && archerFormation.QuerySystem.IsRangedFormation;
            WorldPosition target = RBMAI.Utilities.GetFormationCenterWorldPosition(formation);
            if (!archersUsable)
            {
                // Nothing to wait for: advance.
                target.SetVec2(center + vec * MathF.Clamp(distance * PlainAdvanceFraction, PlainAdvanceMin, PlainAdvanceMax));
                currentOrder = MovementOrder.MovementOrderMove(target);
                facingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
                return;
            }

            if (behaviorState == BehaviorState.Approaching)
            {
                return;
            }
            if (behaviorState == BehaviorState.PullingBack)
            {
                behaviorState = BehaviorState.Shooting;
            }

            float now = Mission.Current.CurrentTime;
            float nextStep;
            Vec2 stepTarget;
            if (!attackerNextStepTime.TryGetValue(formation, out nextStep) || now >= nextStep
                || !attackerStepTarget.TryGetValue(formation, out stepTarget) || !stepTarget.IsValid)
            {
                stepTarget = center + vec * StepMeters;
                attackerStepTarget[formation] = stepTarget;
                attackerNextStepTime[formation] = now + StepInterval;
            }
            shootPosition = stepTarget;
            target.SetVec2(stepTarget);
            currentOrder = MovementOrder.MovementOrderMove(target);
            facingOrder = FacingOrder.FacingOrderLookAtDirection(vec);
        }
    }

    [HarmonyPatch(typeof(BehaviorMountedSkirmish))]
    internal class OverrideBehaviorMountedSkirmish
    {
        public enum RotationDirection
        {
            Left,
            Right
        }

        public class RotationChangeClass
        {
            public RotationDirection rotationDirection = RotationDirection.Left;

            private float lastFlipTime = float.MinValue;

            private float lastSwitchTime = float.MinValue;

            // The last orbit decision written to the AI log, so only changes are written.
            public string lastLoggedMode;

            // The file count the square form order was last set to (0 = not set by this behavior).
            public int squareFiles;

            // The side the current escape curves to (+1 left, -1 right, 0 = not escaping) and when it was last used.
            public int escapeSide;

            public float lastEscapeTime = float.MinValue;

            // When KeepOrbitFacing last turned the block toward its target.
            public float lastFacingTime = float.MinValue;

            // The target the orbit direction was last chosen for (ChooseOrbitDirection).
            public Formation directionTarget;

            // An open target taken while the usual one was blocked (FindOpenTarget), kept until retargetUntil; the
            // next search not before retargetAt.
            public Formation retargetTarget;
            public float retargetUntil = float.MinValue;
            public float retargetAt = float.MinValue;

            // The last blocked-orbit answer (SAFE_POINT / HOLD) and its point, kept until this time while its point stays
            // safe, so a danger flickering in and out does not swap ORBIT and the stand-off every tick.
            public float blockedUntil = float.MinValue;

            public string lastBlockedMode;

            public Vec2 lastBlockedPoint = Vec2.Invalid;

            public RotationChangeClass()
            { }

            // Moves the orbit onto the formation that blocks it, at most once per FlipCooldownSeconds.
            public bool TrySwitchTarget(float now)
            {
                if (now - lastSwitchTime < FlipCooldownSeconds)
                {
                    return false;
                }
                lastSwitchTime = now;
                return true;
            }

            // Reverses the orbit, at most once per FlipCooldownSeconds so a formation boxed in on both sides does not
            // turn back and forth every tick.
            public bool TryFlip(float now)
            {
                if (now - lastFlipTime < FlipCooldownSeconds)
                {
                    return false;
                }
                rotationDirection = rotationDirection == RotationDirection.Left ? RotationDirection.Right : RotationDirection.Left;
                lastFlipTime = now;
                return true;
            }
        }

        private const float FlipCooldownSeconds = 10f;

        // A blocked orbit's stand-off (SAFE_POINT / HOLD) is kept this long (s) after the orbit last read blocked.
        private const float BlockedHoldSeconds = 2f;

        // While the orbit stays blocked, a safe spot is kept this long after it was last used, as long as it stays
        // safe and within the widest safe ring (+ slack) of the moving target.
        private const float SafePointKeepSeconds = 6f;
        private const float SafePointKeepSlack = 50f;

        // An orbit point is dangerous when, by the time the horse archers get there, another enemy formation is
        //  - nearer to it than the orbit's target and within the orbit radius: the orbit has carried them past their
        //    target into a neighbour (the next step of an echeloned line). A formation supporting the target from
        //    behind, or archers on its wing, is farther away than the target on the front of the orbit, so circling
        //    in front of an ordinary enemy line is not blocked; or
        //  - within the hard radius of its line, whatever the target: enemy cavalry or horse archers they have reason
        //    to fear (see CavalryFear), or riding into anything.
        // Distances are from the horse archers' centre to the edge of the other formation. A first version also
        // subtracted half the horse archers' width and used 25-60 m radii regardless of the target: in a normal
        // enemy army that blocked the orbit both ways almost everywhere, and the horse archers stood still.
        // Melee cavalry was 50 m (user 2026-10-22: horse archers should be a bit more afraid of it).
        private const float HardRadiusMeleeCavalry = 60f;
        private const float HardRadiusHorseArchers = 40f;
        private const float HardRadiusOther = 20f;
        private const int DangerMinUnits = 5;

        // Fear of an enemy cavalry or horse archer formation is the mounted threat it stands for over the horse
        // archers' own formation power, capped at CavalryFearMax: 1 against an equal, 2 against one twice as strong.
        // It scales the hard radius kept from it and how early they run from its charge. Below CavalryFearIgnoreBelow
        // it is not feared at all: it counts like any other formation and its charge is met with arrows (and, at the
        // last moment, swords).
        private const float CavalryFearMax = 2f;
        private const float CavalryFearIgnoreBelow = 0.35f;

        // The threat is the enemy's mounted force near the horse archers, not one formation alone (user 2026-10-22:
        // two enemy cavalry formations of half their strength each were each ignored or barely feared). Each enemy
        // mounted formation weighs its formation power, melee cavalry times MeleeCavalryFearFactor (it wins in
        // contact, which formation power does not show). The MAIN threat is the heaviest one, enemy horse archers
        // counting only MainThreatHorseArcherShare of their weight for that choice, so it is almost always the
        // strongest melee cavalry; it is feared with its own weight plus MainThreatSupportShare of all the others',
        // every other mounted formation with its own plus OtherSupportShare of the rest. Others count fully within
        // ThreatPoolFullRange of the horse archers (centre to centre), fading out by ThreatPoolRange.
        private const float MeleeCavalryFearFactor = 1.3f;
        private const float MainThreatHorseArcherShare = 0.6f;
        private const float MainThreatSupportShare = 0.75f;
        private const float OtherSupportShare = 0.25f;
        private const float ThreatPoolFullRange = 150f;
        private const float ThreatPoolRange = 300f;
        private const float ThreatCacheSeconds = 0.5f;

        public sealed class MountedThreat
        {
            public float Time;
            public Vec2 Center;
            public float OwnPower;
            // Sum of every enemy mounted formation's weight × its range fade.
            public float Pool;
            public Formation Main;
        }

        public static Dictionary<Formation, MountedThreat> mountedThreatCache = new Dictionary<Formation, MountedThreat>();

        private static bool IsMounted(Formation enemy)
        {
            return enemy.QuerySystem.IsCavalryFormation || enemy.QuerySystem.IsRangedCavalryFormation;
        }

        private static float ThreatWeight(Formation enemy)
        {
            float power = MathF.Max(enemy.QuerySystem.FormationPower, 0f);
            return enemy.QuerySystem.IsCavalryFormation ? power * MeleeCavalryFearFactor : power;
        }

        private static float ThreatFade(Vec2 center, Formation enemy)
        {
            float distance = center.Distance(RBMAI.Utilities.GetFormationCenter(enemy));
            return 1f - MBMath.ClampFloat((distance - ThreatPoolFullRange) / (ThreatPoolRange - ThreatPoolFullRange), 0f, 1f);
        }

        private static MountedThreat GetMountedThreat(Formation formation)
        {
            float now = Mission.Current.CurrentTime;
            if (mountedThreatCache.TryGetValue(formation, out MountedThreat cached) && now - cached.Time < ThreatCacheSeconds)
            {
                return cached;
            }
            MountedThreat threat = cached ?? new MountedThreat();
            threat.Time = now;
            threat.Center = RBMAI.Utilities.GetFormationCenter(formation);
            threat.OwnPower = formation.QuerySystem.FormationPower;
            threat.Pool = 0f;
            threat.Main = null;
            float mainScore = 0f;
            foreach (Team team in Mission.Current.Teams)
            {
                if (!team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy.CountOfUnits < DangerMinUnits || !IsMounted(enemy))
                    {
                        continue;
                    }
                    float contribution = ThreatWeight(enemy) * ThreatFade(threat.Center, enemy);
                    if (contribution <= 0f)
                    {
                        continue;
                    }
                    threat.Pool += contribution;
                    float score = enemy.QuerySystem.IsCavalryFormation ? contribution : contribution * MainThreatHorseArcherShare;
                    if (score > mainScore)
                    {
                        mainScore = score;
                        threat.Main = enemy;
                    }
                }
            }
            mountedThreatCache[formation] = threat;
            return threat;
        }

        private static float CavalryFear(Formation formation, Formation enemy)
        {
            MountedThreat threat = GetMountedThreat(formation);
            if (threat.OwnPower <= 0f)
            {
                return CavalryFearMax;
            }
            float weight = ThreatWeight(enemy);
            // The others' part of the pool: everything but this formation's own (range-faded) contribution.
            float others = MathF.Max(0f, threat.Pool - weight * ThreatFade(threat.Center, enemy));
            float support = enemy == threat.Main ? MainThreatSupportShare : OtherSupportShare;
            return MBMath.ClampFloat((weight + others * support) / threat.OwnPower, 0f, CavalryFearMax);
        }

        private static float HardRadius(Formation formation, Formation enemy)
        {
            bool meleeCavalry = enemy.QuerySystem.IsCavalryFormation;
            if (!meleeCavalry && !enemy.QuerySystem.IsRangedCavalryFormation)
            {
                return HardRadiusOther;
            }
            float fear = CavalryFear(formation, enemy);
            if (fear < CavalryFearIgnoreBelow)
            {
                return HardRadiusOther;
            }
            return MathF.Max(HardRadiusOther, (meleeCavalry ? HardRadiusMeleeCavalry : HardRadiusHorseArchers) * DistanceScale(fear));
        }

        // Fear scales the distances kept only up to FearDistanceMaxScale: melee cavalry ≤ 75 m, horse archers
        // ≤ 50 m, a charge run from ≤ 60 m.
        private const float FearDistanceMaxScale = 1.25f;
        private const float CavalryEscapeMaxGap = 60f;

        private static float DistanceScale(float fear)
        {
            return MathF.Min(fear, FearDistanceMaxScale);
        }

        // An enemy cavalry or horse archer formation weaker than the horse archers themselves.
        private const float WeakerFearBelow = 1f;

        private static bool IsWeakerMounted(Formation formation, Formation enemy)
        {
            return (enemy.QuerySystem.IsCavalryFormation || enemy.QuerySystem.IsRangedCavalryFormation)
                && CavalryFear(formation, enemy) < WeakerFearBelow;
        }

        // Clearly weaker enemy horse archers (CavalryFear below HuntFearBelow) within HuntRange (centre to centre) and
        // much nearer than the foot target (HuntFootRatio of its distance) become the orbit's target: the horse
        // archers circle and shoot them instead of only ever circling foot formations, to which every enemy horse
        // archer formation was something to keep clear of. Foot formations stay the main target: a first version
        // (fear < 1, 150 m, merely nearer than the foot) had them chasing enemy horse archers most of the battle.
        // The one already hunted is kept out to HuntKeepFactor times each limit, so the target does not flicker.
        private const float HuntFearBelow = 0.7f;
        private const float HuntRange = 100f;
        private const float HuntFootRatio = 0.6f;
        private const float HuntKeepFactor = 1.3f;

        private static Formation FindHuntableHorseArchers(Formation formation, Formation footTarget)
        {
            Vec2 center = RBMAI.Utilities.GetFormationCenter(formation);
            orbitTargetStorage.TryGetValue(formation, out Formation previous);
            float footDistance = footTarget != null ? center.Distance(RBMAI.Utilities.GetFormationCenter(footTarget)) : float.MaxValue;
            Formation best = null;
            float bestDistance = float.MaxValue;
            foreach (Team team in Mission.Current.Teams)
            {
                if (!team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy.CountOfUnits < DangerMinUnits || !enemy.QuerySystem.IsRangedCavalryFormation)
                    {
                        continue;
                    }
                    float keep = enemy == previous ? HuntKeepFactor : 1f;
                    float distance = center.Distance(RBMAI.Utilities.GetFormationCenter(enemy));
                    if (distance > HuntRange * keep || distance > footDistance * HuntFootRatio * keep || distance >= bestDistance
                        || CavalryFear(formation, enemy) >= HuntFearBelow * keep)
                    {
                        continue;
                    }
                    best = enemy;
                    bestDistance = distance;
                }
            }
            return best;
        }

        // Running from a threat (charging cavalry, or a formation they have got too close to) curves to one side
        // instead of running straight away from it: EscapeAngleNear degrees off straight away while the threat is
        // within EscapeNearGap, opening to EscapeAngleFar by EscapeFarGap, re-aimed every tick, so the riders swing
        // round the threat in an arc and stay near the fight, and a chaser is drawn along it. The side is kept for the
        // whole escape (until EscapeEpisodeSeconds without one); it is the one leaning toward their own army, and the
        // other when that side runs off the map or near another danger. Straight away only when both sides do.
        private const float EscapeAngleNear = 35f;
        private const float EscapeAngleFar = 75f;
        private const float EscapeNearGap = 15f;
        private const float EscapeFarGap = 60f;
        private const float EscapeStep = 40f;
        private const float EscapeEpisodeSeconds = 3f;

        private static Vec2 CurvedEscapePoint(Formation formation, RotationChangeClass state, Formation threat, Vec2 myPosition, WorldPosition probe, float now, out string side)
        {
            float ownSpeed = MathF.Max(formation.CachedMovementSpeed, MinOwnSpeed);
            Vec2 threatCenter = PredictedCenter(threat, EscapeStep / ownSpeed);
            Vec2 away = myPosition - ClosestPointOnFormationLine(myPosition, threat, threatCenter);
            float length = away.Length;
            away = length > 0.1f ? away * (1f / length) : threat.Direction.Normalized();
            float gap = length - threat.Depth * 0.5f;
            float t = MBMath.ClampFloat((gap - EscapeNearGap) / (EscapeFarGap - EscapeNearGap), 0f, 1f);
            float angle = (EscapeAngleNear + (EscapeAngleFar - EscapeAngleNear) * t) * MathF.PI / 180f;

            if (now - state.lastEscapeTime > EscapeEpisodeSeconds)
            {
                state.escapeSide = 0;
            }
            state.lastEscapeTime = now;
            if (state.escapeSide == 0)
            {
                Vec2 toAllies = formation.QuerySystem.AverageAllyPosition - myPosition;
                state.escapeSide = EscapeDirection(away, angle, 1).DotProduct(toAllies) >= EscapeDirection(away, angle, -1).DotProduct(toAllies) ? 1 : -1;
            }
            // The side with clearly more room ahead goes first, also mid-escape, so a chase turns away from the map
            // edge instead of along it into a corner.
            if (EscapeRoom(myPosition, away, angle, -state.escapeSide) > EscapeRoom(myPosition, away, angle, state.escapeSide) + EscapeRoomMargin)
            {
                state.escapeSide = -state.escapeSide;
            }
            // The curve both ways, then square across the threat's line (EscapeAngleWide), then back past its flank
            // (EscapeAngleBack, may end up to EscapeBackSlack nearer, only with room to spare): pressed against the
            // edge or a rock, the curve and the straight run both point into it.
            float seconds = EscapeStep / ownSpeed;
            float[] angles = { angle, EscapeAngleWide * MathF.PI / 180f, EscapeAngleBack * MathF.PI / 180f };
            foreach (float candidateAngle in angles)
            {
                bool back = candidateAngle >= EscapeAngleBack * MathF.PI / 180f - 0.001f;
                if (back && gap < EscapeBackMinGap)
                {
                    continue;
                }
                float minGap = back ? gap - EscapeBackSlack : gap;
                for (int i = 0; i < 2; i++)
                {
                    int candidate = i == 0 ? state.escapeSide : -state.escapeSide;
                    Vec2 point = myPosition + EscapeDirection(away, candidateAngle, candidate) * EscapeStep;
                    if (EdgeDistance(point, threat, seconds) >= minGap && IsEscapeClear(formation, threat, myPosition, point, probe))
                    {
                        state.escapeSide = candidate;
                        side = (candidate > 0 ? "LEFT" : "RIGHT") + (back ? "_BACK" : candidateAngle > angle ? "_WIDE" : "");
                        return point;
                    }
                }
            }
            Vec2 straight = myPosition + away * EscapeStep;
            side = IsEscapeClear(formation, threat, myPosition, straight, probe) ? "STRAIGHT" : "STRAIGHT_BLOCKED";
            return straight;
        }

        private const float EscapeAngleWide = 90f;
        private const float EscapeAngleBack = 120f;
        private const float EscapeBackSlack = 10f;
        private const float EscapeBackMinGap = 30f;
        private const float EscapeRoomProbe = 120f;
        private const float EscapeRoomMargin = 40f;

        // Metres from the map edge EscapeRoomProbe along that escape way (negative outside the map).
        private static float EscapeRoom(Vec2 from, Vec2 away, float angle, int side)
        {
            Vec2 point = from + EscapeDirection(away, angle, side) * EscapeRoomProbe;
            return BoundaryRoom(point);
        }

        // With nowhere safe round their target, the nearest other enemy FOOT formations (up to RetargetCandidates,
        // within RetargetRange centre to centre) are tried, each both ways round; the first with an orbit point that
        // passes the usual tests becomes the target for RetargetHoldSeconds. Searched at most every RetargetSeconds.
        private const float RetargetRange = 400f;
        private const int RetargetCandidates = 3;
        private const float RetargetHoldSeconds = 15f;
        private const float RetargetSeconds = 3f;

        private static bool FindOpenTarget(Formation formation, RotationChangeClass state, Formation current, float maxRing, float ownSpeed, Vec2 from, WorldPosition probe, float now, out Formation target, out Vec2 point, out float ring)
        {
            state.retargetAt = now + RetargetSeconds;
            target = null;
            point = from;
            ring = maxRing;
            List<KeyValuePair<float, Formation>> candidates = new List<KeyValuePair<float, Formation>>();
            foreach (Team team in Mission.Current.Teams)
            {
                if (!team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == current || enemy.CountOfUnits < DangerMinUnits || IsMounted(enemy))
                    {
                        continue;
                    }
                    float distance = from.Distance(RBMAI.Utilities.GetFormationCenter(enemy));
                    if (distance <= RetargetRange)
                    {
                        candidates.Add(new KeyValuePair<float, Formation>(distance, enemy));
                    }
                }
            }
            candidates.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = 0; i < candidates.Count && i < RetargetCandidates; i++)
            {
                Formation candidate = candidates[i].Value;
                float candidateRing = OrbitRing(formation, candidate, maxRing);
                RotationDirection first = state.rotationDirection;
                RotationDirection second = first == RotationDirection.Left ? RotationDirection.Right : RotationDirection.Left;
                foreach (RotationDirection direction in new[] { first, second })
                {
                    if (FindUsableOrbit(formation, candidate, candidateRing, ownSpeed, from, direction, probe, out Vec2 candidatePoint, out bool usable) == null && usable)
                    {
                        state.rotationDirection = direction;
                        state.directionTarget = candidate;
                        state.retargetTarget = candidate;
                        state.retargetUntil = now + RetargetHoldSeconds;
                        target = candidate;
                        point = candidatePoint;
                        ring = candidateRing;
                        return true;
                    }
                }
            }
            return false;
        }

        // A holding block closer than its own size + HoldEdgeRoom to the map edge steps HoldInwardStep away from it,
        // when that point has more room and no danger.
        private const float HoldEdgeRoom = 40f;
        private const float HoldInwardStep = 30f;

        private static bool TryStepInward(Formation formation, Formation target, Vec2 myPosition, out Vec2 point)
        {
            point = myPosition;
            float room = BoundaryRoom(myPosition);
            if (room >= BlockSize(formation) + HoldEdgeRoom)
            {
                return false;
            }
            Vec2 inward = myPosition - Mission.Current.GetClosestBoundaryPosition(myPosition);
            if (room < 0f)
            {
                inward = -inward;
            }
            if (inward.LengthSquared < 0.01f)
            {
                return false;
            }
            Vec2 candidate = myPosition + inward.Normalized() * HoldInwardStep;
            if (BoundaryRoom(candidate) <= room
                || FindDanger(formation, target, candidate, SecondsToReach(formation, myPosition, candidate), 0f) != null)
            {
                return false;
            }
            point = candidate;
            return true;
        }

        private static float BoundaryRoom(Vec2 point)
        {
            float distance = Mission.Current.GetClosestBoundaryPosition(point).Distance(point);
            return Mission.Current.IsPositionInsideBoundaries(point) ? distance : -distance;
        }

        private static Vec2 EscapeDirection(Vec2 away, float angle, int side)
        {
            Vec2 direction = away;
            direction.RotateCCW(angle * side);
            return direction;
        }

        // In bounds with room for half the block, clear of every other formation's hard radius by the time they get
        // there, and on the navmesh (an escape point off it would leave them standing). Navmesh lookup last.
        private static bool IsEscapeClear(Formation formation, Formation threat, Vec2 from, Vec2 point, WorldPosition probe)
        {
            if (BoundaryRoom(point) < BlockSize(formation) * 0.5f)
            {
                return false;
            }
            if (FindDanger(formation, threat, point, SecondsToReach(formation, from, point), 0f) != null)
            {
                return false;
            }
            probe.SetVec2(point);
            return probe.GetNavMesh() != UIntPtr.Zero;
        }

        private const float OrbitStep = 25f;
        private const float OrbitLookAhead = 60f;

        // Enemy melee cavalry is a threat when it is riding at the horse archers at a charging pace and would reach
        // them within CavalryEscapeSeconds times its CavalryFear (5 s against an equal, at its actual closing speed,
        // not its top speed; 10 s against one twice as strong, never against a much weaker one; was native's 4 s,
        // raised 2026-10-22 so they run a little earlier).
        // Cavalry standing, milling in a melee or riding across them is not: running from it back to their own army
        // put them right next to the melee, standing still. Riders an enemy gets within ~15 m of evade on their own.
        private const float CavalryEscapeSeconds = 5f;
        private const float CavalryEscapeMinClosingSpeed = 3f;

        // Enemy formations are checked where they will be when the horse archers get there. The velocity is the mean
        // of the units' engine-smoothed AverageVelocity, cached per formation for VelocityCacheSeconds; the engine's
        // formation CachedCurrentVelocity is a ~0.1 s difference of the unit average that jumps whenever a man dies
        // or joins, which made cavalry fighting in a melee look like it was charging.
        private const float PredictMaxSpeed = 15f;
        private const float PredictMaxSeconds = 6f;
        // Horse archers circle, so their velocity turns within seconds: extrapolated 6 s it put them 60-90 m off, on
        // points they never ride to, and those points blocked the orbit.
        private const float PredictMaxSecondsHorseArchers = 2f;
        private const float TargetLeadMaxSeconds = 3f;
        private const float MinOwnSpeed = 3f;
        private const float VelocityCacheSeconds = 0.5f;

        public struct CachedVelocity
        {
            public float Time;
            public Vec2 Velocity;
        }

        public static Dictionary<Formation, CachedVelocity> velocityCache = new Dictionary<Formation, CachedVelocity>();

        private static Vec2 FormationVelocity(Formation formation)
        {
            float now = Mission.Current.CurrentTime;
            if (velocityCache.TryGetValue(formation, out CachedVelocity cached) && now - cached.Time < VelocityCacheSeconds)
            {
                return cached.Velocity;
            }
            Vec2 sum = Vec2.Zero;
            int count = 0;
            formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
            {
                Vec2 v = agent.AverageVelocity.AsVec2;
                if (v.IsValid)
                {
                    sum += v;
                    count++;
                }
            });
            Vec2 velocity = count > 0 ? sum * (1f / count) : Vec2.Zero;
            if (velocity.LengthSquared > PredictMaxSpeed * PredictMaxSpeed)
            {
                velocity = velocity.Normalized() * PredictMaxSpeed;
            }
            velocityCache[formation] = new CachedVelocity { Time = now, Velocity = velocity };
            return velocity;
        }

        private static Vec2 PredictedCenter(Formation enemy, float seconds)
        {
            float maxSeconds = enemy.QuerySystem.IsRangedCavalryFormation ? PredictMaxSecondsHorseArchers : PredictMaxSeconds;
            return RBMAI.Utilities.GetFormationCenter(enemy) + FormationVelocity(enemy) * MathF.Min(seconds, maxSeconds);
        }

        // Seconds the horse archers need to ride from one point to another at their formation's speed.
        private static float SecondsToReach(Formation formation, Vec2 from, Vec2 to)
        {
            return from.Distance(to) / MathF.Max(formation.CachedMovementSpeed, MinOwnSpeed);
        }

        private static Vec2 ClosestPointOnFormationLine(Vec2 point, Formation enemy, Vec2 center)
        {
            Vec2 right = enemy.Direction.Normalized().RightVec();
            float halfWidth = enemy.Width * 0.5f;
            float along = MBMath.ClampFloat((point - center).DotProduct(right), -halfWidth, halfWidth);
            return center + right * along;
        }

        // Distance from the point to the edge of the formation, at the formation's position after the given seconds.
        private static float EdgeDistance(Vec2 point, Formation enemy, float seconds)
        {
            Vec2 center = PredictedCenter(enemy, seconds);
            return point.Distance(ClosestPointOnFormationLine(point, enemy, center)) - enemy.Depth * 0.5f;
        }

        // The enemy formation, other than the orbit's target, that makes the point dangerous (see the rules above the
        // constants) once every formation has moved on for the given number of seconds, or null. When several do,
        // the nearest.
        private static Formation FindDanger(Formation formation, Formation orbitTarget, Vec2 point, float seconds, float orbitRadius)
        {
            float targetDistance = orbitTarget != null ? EdgeDistance(point, orbitTarget, seconds) : float.MaxValue;
            Formation worst = null;
            float worstDistance = float.MaxValue;
            foreach (Team team in Mission.Current.Teams)
            {
                if (!team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == orbitTarget || enemy.CountOfUnits < DangerMinUnits)
                    {
                        continue;
                    }
                    float distance = EdgeDistance(point, enemy, seconds);
                    float hardRadius = HardRadius(formation, enemy);
                    // A weaker mounted formation riding about nearby is not a neighbour to ride into: it only counts
                    // within its hard radius.
                    bool pastTarget = distance < targetDistance && distance < orbitRadius && !IsWeakerMounted(formation, enemy);
                    if ((distance < hardRadius || pastTarget) && distance < worstDistance)
                    {
                        worstDistance = distance;
                        worst = enemy;
                    }
                }
            }
            return worst;
        }

        // Checks the next orbit point, a point further along the orbit, and (when far off) the ride to it, each against
        // where the other enemy formations will be by the time the horse archers get there. The ride itself is only
        // checked against the hard radii: on the way in from afar, every formation is nearer than the target.
        private static Formation FindOrbitDanger(Formation formation, Formation orbitTarget, Orbit orbit, float orbitRadius, Vec2 from, RotationDirection direction, out Vec2 orbitPoint)
        {
            orbitPoint = orbit.GetTargetPos(from, OrbitStep, direction);
            float stepSeconds = SecondsToReach(formation, from, orbitPoint);
            float lookAheadSeconds = stepSeconds + (OrbitLookAhead - OrbitStep) / MathF.Max(formation.CachedMovementSpeed, MinOwnSpeed);
            Formation danger = FindDanger(formation, orbitTarget, orbitPoint, stepSeconds, orbitRadius)
                ?? FindDanger(formation, orbitTarget, orbit.GetTargetPos(from, OrbitLookAhead, direction), lookAheadSeconds, orbitRadius);
            if (danger == null && from.Distance(orbitPoint) > OrbitLookAhead)
            {
                danger = FindDanger(formation, orbitTarget, Vec2.Lerp(from, orbitPoint, 0.5f), stepSeconds * 0.5f, 0f);
            }
            return danger;
        }

        // A point the riders can actually be sent to: inside the map and on the navmesh. An orbit point on a rock, a
        // cliff or a building used to fall through to HOLD_NO_NAVMESH at the end of the postfix: the riders stood
        // still next to the obstacle and were caught, the same as at the map edge.
        private static bool IsOrbitPointUsable(Formation formation, Vec2 point, Vec2 targetCenter, WorldPosition probe)
        {
            if (!IsBlockInside(formation, point, targetCenter))
            {
                return false;
            }
            probe.SetVec2(point);
            return probe.GetNavMesh() != UIntPtr.Zero;
        }

        // The whole block inside the map, not just its front: the order point is its front and it faces the target
        // (KeepOrbitFacing), so its ranks reach Depth further out, away from the target - toward the edge when the
        // target pins them against it (500 riders in a loose square are ~100 m deep). Cheap: no engine call.
        private static bool IsBlockInside(Formation formation, Vec2 point, Vec2 targetCenter)
        {
            if (!Mission.Current.IsPositionInsideBoundaries(point))
            {
                return false;
            }
            Vec2 outward = point - targetCenter;
            if (outward.LengthSquared < 1f)
            {
                return true;
            }
            outward = outward.Normalized();
            float size = BlockSize(formation);
            return Mission.Current.IsPositionInsideBoundaries(point + outward * (size * 0.5f))
                && Mission.Current.IsPositionInsideBoundaries(point + outward * size);
        }

        // The block's depth for the orbit's geometry. KeepSquare keeps it about as deep as it is wide, and the
        // width (a custom form order) is steady, while Depth swings 22-98 m whenever the block turns or bunches.
        private static float BlockSize(Formation formation)
        {
            return MathF.Max(formation.Width, 1f);
        }

        // Wider rings tried, in order, when the orbit point itself is not usable, so the riders swing out round an
        // obstacle by the target instead of turning back or stopping.
        private static readonly float[] OrbitWidenExtraRadii = { 20f, 40f };

        // FindOrbitDanger on the orbit at its radius; when that point is not usable (IsOrbitPointUsable), the first
        // wider ring whose point is usable and safe. usable = false when neither is: the orbit is blocked this way.
        // The look-ahead point (OrbitLookAhead on) must have the block inside the map too, so they turn before the
        // edge rather than riding into a corner and only then finding the next 25 m step out of the map. Engine
        // navmesh lookups only for points that passed every cheap test (danger, map).
        private static Formation FindUsableOrbit(Formation formation, Formation orbitTarget, float orbitRadius, float ownSpeed, Vec2 from, RotationDirection direction, WorldPosition probe, out Vec2 orbitPoint, out bool usable)
        {
            Orbit orbit = OrbitAround(orbitTarget, orbitRadius, ownSpeed, out Vec2 targetCenter);
            Formation danger = FindOrbitDanger(formation, orbitTarget, orbit, orbitRadius, from, direction, out orbitPoint);
            if (danger != null)
            {
                usable = false;
                return danger;
            }
            if (!IsBlockInside(formation, orbit.GetTargetPos(from, OrbitLookAhead, direction), targetCenter))
            {
                // Widening only takes them further out toward the edge.
                usable = false;
                return null;
            }
            usable = IsOrbitPointUsable(formation, orbitPoint, targetCenter, probe);
            if (usable)
            {
                return null;
            }
            foreach (float extra in OrbitWidenExtraRadii)
            {
                Orbit wide = OrbitAround(orbitTarget, orbitRadius + extra, ownSpeed, out _);
                Formation wideDanger = FindOrbitDanger(formation, orbitTarget, wide, orbitRadius, from, direction, out Vec2 widePoint);
                if (wideDanger == null && IsOrbitPointUsable(formation, widePoint, targetCenter, probe))
                {
                    orbitPoint = widePoint;
                    usable = true;
                    return null;
                }
            }
            return null;
        }

        // On a new target the direction is reset, checked DirectionProbeArc ahead each way, so a side that runs into
        // the edge is not tried first and flipped out of only after the 10 s cooldown.
        private const float DirectionProbeArc = 120f;

        private static void ChooseOrbitDirection(Formation formation, RotationChangeClass state, Formation target, float orbitRadius, float ownSpeed, Vec2 from)
        {
            if (state.directionTarget == target)
            {
                return;
            }
            state.directionTarget = target;
            Orbit orbit = OrbitAround(target, orbitRadius, ownSpeed, out Vec2 targetCenter);
            float leftRoom = DirectionRoom(formation, target, orbit, targetCenter, orbitRadius, from, RotationDirection.Left);
            float rightRoom = DirectionRoom(formation, target, orbit, targetCenter, orbitRadius, from, RotationDirection.Right);
            // Left keeps the target on the riders' bow side; Right only when Left is closed.
            state.rotationDirection = leftRoom < 0f && rightRoom >= 0f ? RotationDirection.Right : RotationDirection.Left;
        }

        // AI riders only shoot within AgentAi.HorsebackMissileRangeShare of their bow's range, so the ring (target
        // edge to the block's front) keeps the middle of the block inside it.
        private const float OrbitRingMax = 60f;
        private const float ShootRangeMargin = 5f;
        private const float MinRingFoot = 30f;
        private const float MinRingMounted = 40f;

        private static float OrbitRing(Formation formation, Formation target, float maxRing)
        {
            float range = formation.QuerySystem.MaximumMissileRange * AgentAi.HorsebackMissileRangeShare;
            float ring = range - BlockSize(formation) * 0.5f - ShootRangeMargin;
            return MBMath.ClampFloat(ring, IsMounted(target) ? MinRingMounted : MinRingFoot, maxRing);
        }

        // Metres from the map edge of the block at the probe point; -1 when it is out of the map or in danger.
        private static float DirectionRoom(Formation formation, Formation target, Orbit orbit, Vec2 targetCenter, float orbitRadius, Vec2 from, RotationDirection direction)
        {
            Vec2 point = orbit.GetTargetPos(from, DirectionProbeArc, direction);
            if (!IsBlockInside(formation, point, targetCenter)
                || FindDanger(formation, target, point, SecondsToReach(formation, from, point), orbitRadius) != null)
            {
                return -1f;
            }
            Vec2 outward = point - targetCenter;
            Vec2 rear = outward.LengthSquared > 1f ? point + outward.Normalized() * BlockSize(formation) : point;
            return Mission.Current.GetClosestBoundaryPosition(rear).Distance(rear);
        }

        // The orbit's own target closing in on the riders. FindDanger never counts the target (it is what they
        // circle), so with the orbit blocked both ways and no safe spot they used to HOLD while a chasing infantry
        // block walked into them (the chaser was the target in the 2026-10-23 08-26 log). Pressing = its edge within
        // TargetPressGap of their block's front, or within TargetPressSeconds at its closing speed.
        private const float TargetPressGap = 25f;
        private const float TargetPressSeconds = 8f;

        private static bool IsTargetPressing(Formation formation, Formation target, Vec2 myPosition)
        {
            if (target == null)
            {
                return false;
            }
            Vec2 toMe = myPosition - RBMAI.Utilities.GetFormationCenter(target);
            float gap = EdgeDistance(myPosition, target, 0f) - BlockSize(formation) * 0.5f;
            float closing = toMe.LengthSquared > 1f ? FormationVelocity(target).DotProduct(toMe.Normalized()) : 0f;
            return gap < MathF.Max(TargetPressGap, closing * TargetPressSeconds);
        }

        // Rings searched for a safe firing spot when the orbit is blocked both ways: the orbit itself, then further out
        // (still within bow range), each sampled every 360/SafeRingSamples degrees.
        private static readonly float[] SafeRingExtraRadii = { 0f, 20f, 40f };
        private const int SafeRingSamples = 16;

        // The spot nearest the horse archers, on the innermost ring around the target that has one, where nothing
        // makes them unsafe (FindDanger) and the ride there passes no hard radius.
        private static bool TryFindSafeFiringPoint(Formation formation, Formation target, Vec2 targetCenter, float orbitRadius, Vec2 from, WorldPosition probe, out Vec2 best)
        {
            best = from;
            Vec2 right = target.Direction.Normalized().RightVec();
            float halfWidth = target.Width * 0.5f;
            Vec2 side = from - targetCenter;
            foreach (float extra in SafeRingExtraRadii)
            {
                float radius = orbitRadius + extra + target.Depth * 0.5f;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < SafeRingSamples; i++)
                {
                    float angle = MathF.PI * 2f * i / SafeRingSamples;
                    Vec2 direction = new Vec2(MathF.Cos(angle), MathF.Sin(angle));
                    // Measured from the target's line rather than its centre, so a wide formation is ringed at the
                    // same distance along its whole front.
                    Vec2 point = targetCenter + right * (direction.DotProduct(right) * halfWidth) + direction * radius;
                    float distance = from.Distance(point);
                    // Only the riders' own side of the target: the nearest safe spot used to be 300-420 m away
                    // across the enemy army, and the ride there was never checked against the target itself.
                    if (distance >= bestDistance || direction.DotProduct(side) <= 0f || !IsBlockInside(formation, point, targetCenter))
                    {
                        continue;
                    }
                    float seconds = SecondsToReach(formation, from, point);
                    if (FindDanger(formation, target, point, seconds, orbitRadius) != null
                        || FindDanger(formation, target, Vec2.Lerp(from, point, 0.5f), seconds * 0.5f, 0f) != null)
                    {
                        continue;
                    }
                    // The engine navmesh lookup last, only for a spot that would otherwise be the new best (a fresh
                    // copy of the probe each time, as the other probes get).
                    WorldPosition spot = probe;
                    spot.SetVec2(point);
                    if (spot.GetNavMesh() == UIntPtr.Zero)
                    {
                        continue;
                    }
                    bestDistance = distance;
                    best = point;
                }
                if (bestDistance < float.MaxValue)
                {
                    return true;
                }
            }
            return false;
        }

        // The orbit around a target, centred where the target will be when the riders reach their next orbit point,
        // so they do not trail a marching formation or ride into one coming at them.
        private static Orbit OrbitAround(Formation target, float radius, float ownSpeed, out Vec2 targetCenter)
        {
            targetCenter = PredictedCenter(target, MathF.Min(OrbitStep / ownSpeed, TargetLeadMaxSeconds));
            float halfLength = (target.ArrangementOrder == ArrangementOrder.ArrangementOrderLoose) ? target.Width * 0.25f : target.Width * 0.5f;
            return new Orbit(targetCenter, radius, halfLength, target.Depth * 0.5f, target.Direction);
        }

        // The horse archers' orbit, stepped round the target in the WORLD frame: from the riders' bearing to the
        // target's centre on by the step's arc, at the orbit radius beyond the target's edge in that direction.
        // Native's ellipse is laid out along the target's own facing, so a target that turns to face the riders
        // (a player's infantry chasing them) kept them "in front" for good: every step was ~20 deg off its
        // facing, it walked on, and they backed away from it in a straight line into the map edge (log
        // 2026-10-23 08-26: their bearing round a 500-man block stayed 7-21 deg for 150 s). Left = counter-clockwise.
        private struct Orbit
        {
            private readonly Vec2 _center;

            private readonly float _radius;

            private readonly float _halfLength;

            private readonly float _halfDepth;

            private readonly Vec2 _direction;

            public Orbit(Vec2 center, float radius, float halfLength, float halfDepth, Vec2 direction)
            {
                _center = center;
                _radius = radius;
                _halfLength = halfLength;
                _halfDepth = halfDepth;
                _direction = direction.Normalized();
            }

            // The target's extent from its centre along a unit direction (its rectangle's support).
            private float Extent(Vec2 unit)
            {
                return MathF.Abs(unit.DotProduct(_direction.RightVec())) * _halfLength + MathF.Abs(unit.DotProduct(_direction)) * _halfDepth;
            }

            public Vec2 GetTargetPos(Vec2 position, float distance, RotationDirection rotationDirection)
            {
                Vec2 bearing = position - _center;
                bearing = bearing.LengthSquared > 1f ? bearing.Normalized() : _direction;
                float circle = Extent(bearing) + _radius;
                float angle = MathF.Min(distance / MathF.Max(circle, 1f), MathF.PI * 0.5f);
                bearing.RotateCCW(rotationDirection == RotationDirection.Left ? angle : -angle);
                return _center + bearing * (Extent(bearing) + _radius);
            }
        }

        // While circling, the block faces the target (re-set once it is OrbitFacingDegrees off, at most every
        // OrbitFacingSeconds): its order point is its front, and the ranks lie behind it. Native's LookAtEnemy stops
        // turning a formation once the enemy is nearer than 0.2 m per man (100 m for 500 riders), so the block kept
        // its old facing and, ridden round to the far side, would have laid its ranks out into the target.
        private const float OrbitFacingDegrees = 30f;
        private const float OrbitFacingSeconds = 2f;

        private static void KeepOrbitFacing(Formation formation, RotationChangeClass state, Vec2 targetCenter, Vec2 orbitPoint, float now)
        {
            Vec2 toTarget = targetCenter - orbitPoint;
            if (toTarget.LengthSquared < 1f || now - state.lastFacingTime < OrbitFacingSeconds)
            {
                return;
            }
            toTarget = toTarget.Normalized();
            Vec2 facing = formation.Direction.Normalized();
            if (facing.DotProduct(toTarget) > MathF.Cos(OrbitFacingDegrees * MathF.PI / 180f))
            {
                return;
            }
            formation.SetFacingOrder(FacingOrder.FacingOrderLookAtDirection(toTarget));
            state.lastFacingTime = now;
        }

        private static string LogName(Formation formation)
        {
            return formation == null ? "-" : formation.Team.Side + "#" + formation.Team.TeamIndex + "/" + formation.FormationIndex;
        }

        // Writes the horse archers' orbit decision to the AI behavior log (RBM Debug & Logging) when it changes.
        private static void LogOrbitMode(Formation formation, RotationChangeClass state, string mode, Formation target, Formation danger)
        {
            if (!AiBehaviorLog.IsEnabled || state == null)
            {
                return;
            }
            string key = mode + "|" + LogName(target) + "|" + LogName(danger);
            if (state.lastLoggedMode == key)
            {
                return;
            }
            state.lastLoggedMode = key;
            MountedThreat threat = GetMountedThreat(formation);
            string threatText = threat.Main == null ? "\tmainThreat=-"
                : "\tmainThreat=" + LogName(threat.Main) + "\tmainFear=" + AiBehaviorLog.Fmt(CavalryFear(formation, threat.Main));
            if (danger != null && danger != threat.Main && IsMounted(danger))
            {
                threatText += "\tdangerFear=" + AiBehaviorLog.Fmt(CavalryFear(formation, danger));
            }
            float shootRange = formation.QuerySystem.MaximumMissileRange * AgentAi.HorsebackMissileRangeShare;
            AiBehaviorLog.Write("t=" + AiBehaviorLog.Fmt(Mission.Current.CurrentTime) + "\tCHANGE\tHA_ORBIT\t" + LogName(formation)
                + "\t" + mode + "\ttarget=" + LogName(target) + "\tdanger=" + LogName(danger) + "\tdir=" + state.rotationDirection + threatText
                + "\tring=" + (target != null ? AiBehaviorLog.Fmt(OrbitRing(formation, target, OrbitRingMax)) : "-")
                + "\tshootRange=" + AiBehaviorLog.Fmt(shootRange) + "\tshooting=" + AiBehaviorLog.Fmt(formation.QuerySystem.MakingRangedAttackRatio));
        }

        private static RotationChangeClass GetRotationState(Formation formation)
        {
            if (!rotationDirectionDictionary.TryGetValue(formation, out RotationChangeClass state))
            {
                state = new RotationChangeClass();
                rotationDirectionDictionary.Add(formation, state);
            }
            return state;
        }

        // The enemy melee cavalry formation charging the horse archers, or null.
        private static Formation FindEnemyCavalryClosing(Formation formation)
        {
            Vec2 center = RBMAI.Utilities.GetFormationCenter(formation);
            foreach (Team team in Mission.Current.Teams)
            {
                if (!team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy.CountOfUnits < DangerMinUnits || !enemy.QuerySystem.IsCavalryFormation)
                    {
                        continue;
                    }
                    float fear = CavalryFear(formation, enemy);
                    if (fear < CavalryFearIgnoreBelow)
                    {
                        continue;
                    }
                    Vec2 toMe = center - RBMAI.Utilities.GetFormationCenter(enemy);
                    float gap = toMe.Normalize() - (enemy.Depth + formation.Depth) * 0.5f;
                    // Only the part of its velocity aimed at the horse archers counts.
                    float closingSpeed = FormationVelocity(enemy).DotProduct(toMe);
                    if (closingSpeed > CavalryEscapeMinClosingSpeed && gap < MathF.Min(closingSpeed * CavalryEscapeSeconds * DistanceScale(fear), CavalryEscapeMaxGap))
                    {
                        return enemy;
                    }
                }
            }
            return null;
        }

        public static Dictionary<Formation, RotationChangeClass> rotationDirectionDictionary = new Dictionary<Formation, RotationChangeClass> { };

        // The orbit's target formation, kept until it is gone or another candidate is clearly closer. Re-picked fresh
        // every tick, the orbit re-centred on whichever enemy infantry/archer formation was nearest at that moment,
        // and the circling itself kept changing which one that was, so the order point jumped tens of metres and back.
        public static Dictionary<Formation, Formation> orbitTargetStorage = new Dictionary<Formation, Formation> { };

        private const float OrbitTargetSwitchRatio = 0.7f;

        private static Formation KeepOrbitTarget(Formation formation, Formation candidate)
        {
            // Hunted horse archers are re-chosen every tick by FindHuntableHorseArchers, never kept here: once they
            // are stronger or out of range they must stop being the target (as the target they are no danger).
            if (orbitTargetStorage.TryGetValue(formation, out Formation previous)
                && previous != null && previous.CountOfUnits > 0 && candidate != null && previous != candidate
                && !previous.QuerySystem.IsRangedCavalryFormation)
            {
                Vec2 center = RBMAI.Utilities.GetFormationCenter(formation);
                float previousDistance = center.Distance(RBMAI.Utilities.GetFormationCenter(previous));
                float candidateDistance = center.Distance(RBMAI.Utilities.GetFormationCenter(candidate));
                if (candidateDistance > previousDistance * OrbitTargetSwitchRatio)
                {
                    candidate = previous;
                }
            }
            orbitTargetStorage[formation] = candidate;
            return candidate;
        }

        private struct Ellipse
        {
            private readonly Vec2 _center;

            private readonly float _radius;

            private readonly float _halfLength;

            private readonly Vec2 _direction;

            public Ellipse(Vec2 center, float radius, float halfLength, Vec2 direction)
            {
                _center = center;
                _radius = radius;
                _halfLength = halfLength;
                _direction = direction;
            }

            public Vec2 GetTargetPos(Vec2 position, float distance, RotationDirection rotationDirection)
            {
                Vec2 vec;
                if (rotationDirection == RotationDirection.Left)
                {
                    vec = _direction.LeftVec();
                }
                else
                {
                    vec = _direction.RightVec();
                }
                Vec2 vec2 = _center + vec * _halfLength;
                Vec2 vec3 = _center - vec * _halfLength;
                Vec2 vec4 = position - _center;
                bool flag = vec4.Normalized().DotProduct(_direction) > 0f;
                Vec2 vec5 = vec4.DotProduct(vec) * vec;
                bool flag2 = vec5.Length < _halfLength;
                bool flag3 = true;
                if (flag2)
                {
                    position = _center + vec5 + _direction * (_radius * (float)(flag ? 1 : (-1)));
                }
                else
                {
                    flag3 = vec5.DotProduct(vec) > 0f;
                    Vec2 vec6 = (position - (flag3 ? vec2 : vec3)).Normalized();
                    position = (flag3 ? vec2 : vec3) + vec6 * _radius;
                }
                Vec2 vec7 = _center + vec5;
                float num = MathF.PI * 2f * _radius;
                while (distance > 0f)
                {
                    if (flag2 && flag)
                    {
                        float num2 = (((vec2 - vec7).Length < distance) ? (vec2 - vec7).Length : distance);
                        position = vec7 + (vec2 - vec7).Normalized() * num2;
                        position += _direction * _radius;
                        distance -= num2;
                        flag2 = false;
                        flag3 = true;
                    }
                    else if (!flag2 && flag3)
                    {
                        Vec2 v = (position - vec2).Normalized();
                        float num3 = TaleWorlds.Library.MathF.Acos(MBMath.ClampFloat(_direction.DotProduct(v), -1f, 1f));
                        float num4 = MathF.PI * 2f * (distance / num);
                        float num5 = ((num3 + num4 < MathF.PI) ? (num3 + num4) : MathF.PI);
                        float num6 = (num5 - num3) / MathF.PI * (num / 2f);
                        Vec2 direction = _direction;
                        direction.RotateCCW(num5);
                        position = vec2 + direction * _radius;
                        distance -= num6;
                        flag2 = true;
                        flag = false;
                    }
                    else if (flag2)
                    {
                        float num7 = (((vec3 - vec7).Length < distance) ? (vec3 - vec7).Length : distance);
                        position = vec7 + (vec3 - vec7).Normalized() * num7;
                        position -= _direction * _radius;
                        distance -= num7;
                        flag2 = false;
                        flag3 = false;
                    }
                    else
                    {
                        Vec2 vec8 = (position - vec3).Normalized();
                        float num8 = MathF.Acos(MBMath.ClampFloat(_direction.DotProduct(vec8), -1f, 1f));
                        float num9 = MathF.PI * 2f * (distance / num);
                        float num10 = ((num8 - num9 > 0f) ? (num8 - num9) : 0f);
                        float num11 = num8 - num10;
                        float num12 = num11 / MathF.PI * (num / 2f);
                        Vec2 vec9 = vec8;
                        vec9.RotateCCW(num11);
                        position = vec3 + vec9 * _radius;
                        distance -= num12;
                        flag2 = true;
                        flag = true;
                    }
                }
                return position;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(BehaviorMountedSkirmish __instance, ref bool ____engaging, ref MovementOrder ____currentOrder, ref bool ____isEnemyReachable, ref FacingOrder ___CurrentFacingOrder)
        {
            WorldPosition position = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;
            WorldPosition position2 = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;
            Formation footTarget = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
            Formation huntTarget = __instance.Formation.QuerySystem.IsRangedCavalryFormation ? FindHuntableHorseArchers(__instance.Formation, footTarget) : null;
            Formation targetFormation;
            if (huntTarget != null)
            {
                targetFormation = huntTarget;
                orbitTargetStorage[__instance.Formation] = huntTarget;
            }
            else
            {
                targetFormation = KeepOrbitTarget(__instance.Formation, footTarget);
                if (__instance.Formation.QuerySystem.IsRangedCavalryFormation
                    && rotationDirectionDictionary.TryGetValue(__instance.Formation, out RotationChangeClass retargetState)
                    && retargetState.retargetTarget != null && retargetState.retargetTarget.CountOfUnits >= DangerMinUnits
                    && Mission.Current.CurrentTime < retargetState.retargetUntil)
                {
                    targetFormation = retargetState.retargetTarget;
                    orbitTargetStorage[__instance.Formation] = targetFormation;
                }
            }
            FormationQuerySystem targetFormationQS = null;
            if (targetFormation != null)
            {
                targetFormationQS = targetFormation.QuerySystem;
            }
            else
            {
                targetFormationQS = __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
            }
            ____isEnemyReachable = targetFormationQS != null && (!(__instance.Formation.Team.TeamAI is TeamAISiegeComponent) || !TeamAISiegeComponent.IsFormationInsideCastle(targetFormationQS.Formation, includeOnlyPositionedUnits: false));
            if (!____isEnemyReachable)
            {
                position.SetVec2(RBMAI.Utilities.GetFormationCenter(__instance.Formation));
            }
            else
            {
                bool isEnemyClose = (__instance.Formation.QuerySystem.AverageAllyPosition - __instance.Formation.Team.QuerySystem.AverageEnemyPosition).LengthSquared <= 160000f;
                // Horse archers fall back on their own army while enemy melee cavalry is about to reach them, as native
                // does; the orbit would otherwise keep them circling into the charge.
                Formation cavalryClosing = __instance.Formation.QuerySystem.IsRangedCavalryFormation ? FindEnemyCavalryClosing(__instance.Formation) : null;
                // Native breaks off while taking more arrows than it shoots (armies still far apart); against weaker
                // horse archers that only handed them the duel, so it does not while hunting them.
                ____engaging = cavalryClosing == null && (isEnemyClose || huntTarget != null || ((!____engaging) ? ((__instance.Formation.QuerySystem.Formation.CachedAveragePosition - __instance.Formation.QuerySystem.AverageAllyPosition).LengthSquared <= 160000f) : (!(__instance.Formation.QuerySystem.UnderRangedAttackRatio * 0.2f > __instance.Formation.QuerySystem.MakingRangedAttackRatio))));
                if (cavalryClosing != null)
                {
                    // Was a straight run back to their own army (native); now curves away from the charge.
                    RotationChangeClass state = GetRotationState(__instance.Formation);
                    position.SetVec2(CurvedEscapePoint(__instance.Formation, state, cavalryClosing, __instance.Formation.SmoothedAverageUnitPosition, position2, Mission.Current.CurrentTime, out string side));
                    LogOrbitMode(__instance.Formation, state, "CAVALRY_ESCAPE_" + side, targetFormation, cavalryClosing);
                }
                else if (!____engaging)
                {
                    if (__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        LogOrbitMode(__instance.Formation, GetRotationState(__instance.Formation), "FALL_BACK", targetFormation, null);
                    }
                    position = new WorldPosition(Mission.Current.Scene, new Vec3(__instance.Formation.QuerySystem.AverageAllyPosition.x, __instance.Formation.QuerySystem.AverageAllyPosition.y, __instance.Formation.Team.GetMedianPosition(__instance.Formation.Team.GetAveragePosition()).GetNavMeshZ() + 100f));
                }
                else
                {
                    Formation enemyFormation = targetFormationQS.Formation;

                    if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
                    {
                        enemyFormation = RBMAI.Utilities.FindSignificantEnemyToPosition(__instance.Formation, position, true, true, false, false, false, false);
                    }

                    //if (closestSignificantlyLargeEnemyFormation != null && closestSignificantlyLargeEnemyFormation.AveragePosition.Distance(__instance.Formation.CurrentPosition) < __instance.Formation.Depth / 2f + (
                    //    (closestSignificantlyLargeEnemyFormation.Formation.QuerySystem.FormationPower / __instance.Formation.QuerySystem.FormationPower) * 20f + 10f))
                    //{
                    //    ____currentOrder = MovementOrder.MovementOrderChargeToTarget(closestSignificantlyLargeEnemyFormation.Formation);
                    //    return;
                    //}

                    if (enemyFormation != null && enemyFormation.QuerySystem != null)
                    {
                        float distance = OrbitRingMax;
                        if (!__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            distance = 30f;
                        }

                        RotationChangeClass rotationDirection = GetRotationState(__instance.Formation);

                        float now = Mission.Current.CurrentTime;
                        Vec2 myPosition = __instance.Formation.SmoothedAverageUnitPosition;

                        if (__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            if (__instance.Formation.IsAIControlled)
                            {
                                KeepSquare(__instance.Formation, rotationDirection);
                            }
                            float ownSpeed = MathF.Max(__instance.Formation.CachedMovementSpeed, MinOwnSpeed);
                            Formation orbitTarget = enemyFormation;
                            float maxRing = distance;
                            distance = OrbitRing(__instance.Formation, orbitTarget, maxRing);
                            OrbitAround(orbitTarget, distance, ownSpeed, out Vec2 targetCenter);
                            string mode = "ORBIT";
                            Formation skirmisher = __instance.Formation;
                            ChooseOrbitDirection(skirmisher, rotationDirection, orbitTarget, distance, ownSpeed, myPosition);

                            // The orbit only knows its target. If it would carry the formation past it into another
                            // enemy formation (the next step of an echeloned line), close to enemy cavalry, off the
                            // map or onto ground they cannot ride (a rock, a cliff - after trying wider rings round
                            // it), circle the other way. If both ways are blocked by a foot formation, circle that one
                            // instead, as native circles the outermost formation of a line. Only when that fails too,
                            // stand off the target and shoot, or back away when even that spot is too close.
                            // The map edge no longer flips the orbit blindly whenever the riders are near it (the old
                            // rule, still used by the melee skirmishers below, could turn them toward the edge too):
                            // an orbit point beyond it is a blocked point like any other.
                            // (The ground is not read here: a terrain orbit round the side's open-ground centre was
                            // tried on smart-tactics and removed 2026-10-22, user: "orbiting should not be based on
                            // terrain".)
                            Formation danger = FindUsableOrbit(__instance.Formation, orbitTarget, distance, ownSpeed, myPosition, rotationDirection.rotationDirection, position2, out Vec2 orbitPoint, out bool usable);
                            bool blocked = danger != null || !usable;
                            if (blocked && rotationDirection.TryFlip(now))
                            {
                                mode = "FLIP";
                                danger = FindUsableOrbit(__instance.Formation, orbitTarget, distance, ownSpeed, myPosition, rotationDirection.rotationDirection, position2, out orbitPoint, out usable);
                                blocked = danger != null || !usable;
                            }
                            if (blocked && danger != null && !IsMounted(danger) && rotationDirection.TrySwitchTarget(now))
                            {
                                mode = "SWITCH";
                                orbitTarget = danger;
                                orbitTargetStorage[__instance.Formation] = orbitTarget;
                                distance = OrbitRing(__instance.Formation, orbitTarget, maxRing);
                                OrbitAround(orbitTarget, distance, ownSpeed, out targetCenter);
                                danger = FindUsableOrbit(__instance.Formation, orbitTarget, distance, ownSpeed, myPosition, rotationDirection.rotationDirection, position2, out orbitPoint, out usable);
                                blocked = danger != null || !usable;
                            }
                            if (blocked)
                            {
                                // Ride to the nearest spot around the target that is safe and shoot from there. A
                                // first version backed off 40 m from wherever they were whenever the spot straight in
                                // front of the target was unsafe; that repeated every tick, so they rode all the way
                                // back to where they spawned, came forward again when the orbit next opened, and so
                                // on. Now they only back off when they themselves are in danger, and otherwise hold.
                                // A safe spot already chosen is kept while it stays safe, so the order does not swap
                                // between a fresh spot and HOLD every tick.
                                if (rotationDirection.lastBlockedMode == "SAFE_POINT" && now < rotationDirection.blockedUntil + SafePointKeepSeconds
                                    && rotationDirection.lastBlockedPoint.IsValid
                                    && EdgeDistance(rotationDirection.lastBlockedPoint, orbitTarget, 0f) <= distance + SafePointKeepSlack
                                    && FindDanger(skirmisher, orbitTarget, rotationDirection.lastBlockedPoint, SecondsToReach(skirmisher, myPosition, rotationDirection.lastBlockedPoint), distance) == null)
                                {
                                    mode = "SAFE_POINT";
                                    orbitPoint = rotationDirection.lastBlockedPoint;
                                }
                                else if (TryFindSafeFiringPoint(__instance.Formation, orbitTarget, targetCenter, distance, myPosition, position2, out Vec2 safePoint))
                                {
                                    mode = "SAFE_POINT";
                                    orbitPoint = safePoint;
                                }
                                // Nowhere safe round this target: circle another enemy formation that is open instead
                                // of holding far out of range (log 2026-10-23 15-27: minutes of HOLD 150-220 m away).
                                else if (__instance.Formation.IsAIControlled && now >= rotationDirection.retargetAt
                                    && FindOpenTarget(skirmisher, rotationDirection, orbitTarget, maxRing, ownSpeed, myPosition, position2, now, out Formation openTarget, out Vec2 openPoint, out float openRing))
                                {
                                    mode = "RETARGET";
                                    orbitTarget = openTarget;
                                    orbitTargetStorage[__instance.Formation] = openTarget;
                                    distance = openRing;
                                    OrbitAround(orbitTarget, distance, ownSpeed, out targetCenter);
                                    orbitPoint = openPoint;
                                    danger = null;
                                }
                                else
                                {
                                    Formation threat = FindDanger(__instance.Formation, orbitTarget, myPosition, 0f, 0f);
                                    if (threat == null && IsTargetPressing(__instance.Formation, orbitTarget, myPosition))
                                    {
                                        threat = orbitTarget;
                                    }
                                    if (threat != null)
                                    {
                                        // Away from where the threat will be, so a formation moving across their path
                                        // is not backed into, curving to one side (see CurvedEscapePoint).
                                        danger = threat;
                                        orbitPoint = CurvedEscapePoint(__instance.Formation, rotationDirection, threat, myPosition, position2, now, out string side);
                                        mode = "BACK_OFF_" + side;
                                    }
                                    else
                                    {
                                        mode = "HOLD";
                                        orbitPoint = myPosition;
                                        // Never wait against the map edge: a charge there leaves no way out.
                                        if (TryStepInward(skirmisher, orbitTarget, myPosition, out Vec2 inward))
                                        {
                                            mode = "HOLD_INWARD";
                                            orbitPoint = inward;
                                        }
                                    }
                                }
                                if (mode == "SAFE_POINT" || mode == "HOLD")
                                {
                                    rotationDirection.blockedUntil = now + BlockedHoldSeconds;
                                    rotationDirection.lastBlockedMode = mode;
                                    rotationDirection.lastBlockedPoint = orbitPoint;
                                }
                                else
                                {
                                    rotationDirection.lastBlockedMode = null;
                                }
                            }
                            else if (mode == "ORBIT" && now < rotationDirection.blockedUntil && rotationDirection.lastBlockedMode != null
                                && rotationDirection.lastBlockedPoint.IsValid
                                && !(rotationDirection.lastBlockedMode == "HOLD" && IsTargetPressing(skirmisher, orbitTarget, myPosition))
                                && FindDanger(skirmisher, orbitTarget, rotationDirection.lastBlockedPoint, SecondsToReach(skirmisher, myPosition, rotationDirection.lastBlockedPoint), distance) == null)
                            {
                                // Hysteresis: the orbit only just cleared; keep the stand-off a moment while its point stays safe.
                                mode = rotationDirection.lastBlockedMode;
                                orbitPoint = rotationDirection.lastBlockedPoint;
                            }
                            // Only while circling: turned toward stand-off and hold points on either side of the
                            // target, the block re-laid itself every 2 s.
                            if (__instance.Formation.IsAIControlled && (mode == "ORBIT" || mode == "FLIP" || mode == "SWITCH" || mode == "RETARGET"))
                            {
                                KeepOrbitFacing(__instance.Formation, rotationDirection, targetCenter, orbitPoint, now);
                            }
                            LogOrbitMode(__instance.Formation, rotationDirection, mode, orbitTarget, danger);
                            position.SetVec2(orbitPoint);
                        }
                        else
                        {
                            // At the map edge the orbit turns back instead of pressing into the boundary.
                            float distanceFromBoundary = Mission.Current.GetClosestBoundaryPosition(__instance.Formation.CurrentPosition).Distance(__instance.Formation.CurrentPosition);
                            if (distanceFromBoundary <= __instance.Formation.Width / 2f)
                            {
                                rotationDirection.TryFlip(now);
                            }
                            Ellipse ellipse = new Ellipse(RBMAI.Utilities.GetFormationCenter(enemyFormation), distance, enemyFormation.Width * 0.5f, enemyFormation.Direction);
                            position.SetVec2(ellipse.GetTargetPos(myPosition, OrbitStep, rotationDirection.rotationDirection));
                        }
                    }
                    else
                    {
                        position.SetVec2(RBMAI.Utilities.GetFormationCenter(__instance.Formation));
                    }
                }
            }
            if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(position.AsVec2))
            {
                if (__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                {
                    LogOrbitMode(__instance.Formation, GetRotationState(__instance.Formation), "HOLD_NO_NAVMESH", targetFormation, null);
                }
                position = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;
                ____currentOrder = MovementOrder.MovementOrderMove(position);
            }
            else
            {
                ____currentOrder = MovementOrder.MovementOrderMove(position);
            }
        }

        // Native forms Line + Deep, a compact block. Since horse archers ride the orbit in their slots, that block
        // re-laid itself every time its facing swung round the target and the riders packed together. Loose keeps
        // them spread out, and a custom width makes the block about as deep as it is wide, so it turns round the
        // target without a long flank swinging out. (FormOrderWider was tried first: for a line it means 64 files,
        // a single rank for any normal horse archer formation.)
        [HarmonyPostfix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(BehaviorMountedSkirmish __instance)
        {
            if (__instance.Formation != null && __instance.Formation.IsAIControlled && __instance.Formation.QuerySystem.IsRangedCavalryFormation)
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
                SetSquare(__instance.Formation, GetRotationState(__instance.Formation));
            }
        }

        // Re-squares the block once losses have changed the square file count by more than this fraction (and at
        // least one file); every re-form re-lays the whole formation, so it is not done for every man lost.
        private const float SquareFilesTolerance = 0.2f;

        // The file count at which a loose line of horse archers has as many metres of depth as of width: files and
        // ranks in the ratio of the engine's rank step to its file step for loose cavalry.
        private static int SquareFileCount(Formation formation)
        {
            int spacing = ArrangementOrder.GetUnitSpacingOf(ArrangementOrderEnum.Loose);
            float diameter = Formation.GetDefaultUnitDiameter(true);
            float fileStep = diameter + Formation.GetDefaultUnitInterval(true, spacing);
            float rankStep = diameter + Formation.GetDefaultUnitDistance(true, spacing);
            int count = Math.Max(formation.CountOfUnits, 1);
            return Math.Max(1, MathF.Round(MathF.Sqrt(count * rankStep / fileStep)));
        }

        // The width is built from the formation's own unit diameter and interval, the same values the engine turns
        // a custom width back into a file count with, so it gets exactly this many files.
        private static void SetSquare(Formation formation, RotationChangeClass state)
        {
            int files = SquareFileCount(formation);
            float width = (files - 1) * (formation.UnitDiameter + formation.Interval) + formation.UnitDiameter;
            formation.SetFormOrder(FormOrder.FormOrderCustom(width));
            state.squareFiles = files;
        }

        // Compares against the file count it set itself: the engine rewrites the custom width to the arrangement's
        // actual width on every re-form, so reading that back would trigger a re-form every tick.
        private static void KeepSquare(Formation formation, RotationChangeClass state)
        {
            if (state.squareFiles <= 0 || formation.ArrangementOrder != ArrangementOrder.ArrangementOrderLoose || formation.FormOrder.OrderEnum != FormOrder.FormOrderEnum.Custom)
            {
                return;
            }
            int files = SquareFileCount(formation);
            if (Math.Abs(files - state.squareFiles) > Math.Max(1f, state.squareFiles * SquareFilesTolerance))
            {
                SetSquare(formation, state);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetAiWeight")]
        private static void PostfixGetAiWeight(ref BehaviorMountedSkirmish __instance, ref float __result, ref bool ____isEnemyReachable)
        {
            if (RBMBehaviorArcherStand.MountedEscorts(__instance.Formation))
            {
                __result = 0f; // a token mounted force rides with the main body
                return;
            }
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsCavalryFormation)
            {
                if (RBMAI.Utilities.CheckIfMountedSkirmishFormation(__instance.Formation, 0.6f))
                {
                    __result = 5f;
                    return;
                }
                else
                {
                    __result = 0f;
                    return;
                }
            }
            else if (__instance.Formation != null && __instance.Formation.QuerySystem.IsRangedCavalryFormation)
            {
                //Formation enemyCav = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, false, false, true, false, false);
                //if (enemyCav != null && enemyCav.QuerySystem.IsCavalryFormation && __instance.Formation.QuerySystem.Formation.CachedMedianPosition.AsVec2.Distance(enemyCav.QuerySystem.Formation.CachedMedianPosition.AsVec2) < 55f && enemyCav.CountOfUnits >= __instance.Formation.CountOfUnits * 0.5f)
                //{
                //    __result = 1000f;
                //    return;
                //}
                if (!____isEnemyReachable)
                {
                    __result = 0.01f;
                    return;
                }

                float powerSum = 0f;
                if (!Utilities.HasBattleBeenJoined(__instance.Formation, false, 75f))
                {
                    foreach (Formation enemyArcherFormation in Utilities.FindSignificantArcherFormations(__instance.Formation))
                    {
                        powerSum += enemyArcherFormation.QuerySystem.FormationPower;
                    }
                    if (powerSum > 0f && __instance.Formation.QuerySystem.FormationPower > 0f && (__instance.Formation.QuerySystem.FormationPower / powerSum) < 0.75f)
                    {
                        __result = 1000f;
                        return;
                    }
                }
                __result = 1000f;
                return;
            }
            else
            {
                int countOfSkirmishers = 0;
                __instance.Formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    if (RBMAI.Utilities.CheckIfSkirmisherAgent(agent, 1))
                    {
                        countOfSkirmishers++;
                    }
                });
                // Float division: the int one was only true for a formation of nothing but skirmishers.
                int countOfUnits = __instance.Formation.CountOfUnits;
                if (countOfUnits > 0 && (float)countOfSkirmishers / countOfUnits > 0.6f)
                {
                    __result = 1f;
                    return;
                }
                else
                {
                    __result = 0f;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorHorseArcherSkirmish))]
    internal class OverrideBehaviorHorseArcherSkirmish
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        private static bool PrefixGetAiWeight(ref float __result)
        {
            __result = 0f;
            return false;
        }
    }
}
