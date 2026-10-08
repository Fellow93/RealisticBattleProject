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
            __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
            __instance.Formation.SetFormOrder(FormOrder.FormOrderCustom(110f));
        }
    }

    [HarmonyPatch(typeof(BehaviorScreenedSkirmish))]
    internal class OverrideBehaviorScreenedSkirmish
    {
        private static readonly MethodInfo CalculateCurrentOrderMethod = typeof(BehaviorScreenedSkirmish).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(ref Formation ____mainFormation, ref BehaviorScreenedSkirmish __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            if (____mainFormation != null && (____mainFormation.CountOfUnits == 0 || !____mainFormation.QuerySystem.IsInfantryFormation))
            {
                ____mainFormation = __instance.Formation.Team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).FirstOrDefault((Formation f) => f.AI.IsMainFormation);
            }
            if (____mainFormation != null && __instance.Formation != null && ____mainFormation.CountOfUnits > 0 && ____mainFormation.QuerySystem.IsInfantryFormation)
            {
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(____mainFormation.Direction);
                WorldPosition medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(____mainFormation);
                Vec2 calcPosition;
                if (__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                {
                    calcPosition = medianPosition.AsVec2 - ____mainFormation.Direction.Normalized() * (____mainFormation.Depth / 2f + __instance.Formation.Depth / 2f + 15f);
                }
                else
                {
                    calcPosition = medianPosition.AsVec2 - ____mainFormation.Direction.Normalized() * (____mainFormation.Depth / 2f + __instance.Formation.Depth / 2f + 5f);
                }
                medianPosition.SetVec2(calcPosition);
                if (!Mission.Current.IsPositionInsideBoundaries(calcPosition) || medianPosition.GetNavMesh() == UIntPtr.Zero)
                {
                    medianPosition = ____mainFormation.QuerySystem.Formation.CachedMedianPosition;
                }
                ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static bool PrefixTickOccasionally(Formation ____mainFormation, BehaviorScreenedSkirmish __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            CalculateCurrentOrderMethod.Invoke(__instance, new object[] { });
            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref BehaviorScreenedSkirmish __instance)
        {
            __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
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
        private const float HardRadiusMeleeCavalry = 50f;
        private const float HardRadiusHorseArchers = 40f;
        private const float HardRadiusOther = 20f;
        private const int DangerMinUnits = 5;

        // Fear of an enemy cavalry or horse archer formation is its formation power over the horse archers' own,
        // capped at CavalryFearMax: 1 against an equal, 2 against one twice as strong. It scales the hard radius kept
        // from it and how early they run from its charge. Below CavalryFearIgnoreBelow it is not feared at all: it
        // counts like any other formation and its charge is met with arrows (and, at the last moment, swords).
        private const float CavalryFearMax = 2f;
        private const float CavalryFearIgnoreBelow = 0.35f;

        private static float CavalryFear(Formation formation, Formation enemy)
        {
            float ownPower = formation.QuerySystem.FormationPower;
            if (ownPower <= 0f)
            {
                return CavalryFearMax;
            }
            return MBMath.ClampFloat(enemy.QuerySystem.FormationPower / ownPower, 0f, CavalryFearMax);
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
            return MathF.Max(HardRadiusOther, (meleeCavalry ? HardRadiusMeleeCavalry : HardRadiusHorseArchers) * fear);
        }

        private const float OrbitStep = 25f;
        private const float OrbitLookAhead = 60f;
        private const float BackOffDistance = 40f;

        // Enemy melee cavalry is a threat when it is riding at the horse archers at a charging pace and would reach
        // them within CavalryEscapeSeconds times its CavalryFear (native's 4 s against an equal, but at its actual
        // closing speed, not its top speed; 8 s against one twice as strong, never against a much weaker one).
        // Cavalry standing, milling in a melee or riding across them is not: running from it back to their own army
        // put them right next to the melee, standing still. Riders an enemy gets within ~15 m of evade on their own.
        private const float CavalryEscapeSeconds = 4f;
        private const float CavalryEscapeMinClosingSpeed = 3f;

        // Enemy formations are checked where they will be when the horse archers get there. The velocity is the mean
        // of the units' engine-smoothed AverageVelocity, cached per formation for VelocityCacheSeconds; the engine's
        // formation CachedCurrentVelocity is a ~0.1 s difference of the unit average that jumps whenever a man dies
        // or joins, which made cavalry fighting in a melee look like it was charging.
        private const float PredictMaxSpeed = 15f;
        private const float PredictMaxSeconds = 6f;
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
            return RBMAI.Utilities.GetFormationCenter(enemy) + FormationVelocity(enemy) * MathF.Min(seconds, PredictMaxSeconds);
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
                    bool pastTarget = distance < targetDistance && distance < orbitRadius;
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
        private static Formation FindOrbitDanger(Formation formation, Formation orbitTarget, Ellipse ellipse, float orbitRadius, Vec2 from, RotationDirection direction, out Vec2 orbitPoint)
        {
            orbitPoint = ellipse.GetTargetPos(from, OrbitStep, direction);
            float stepSeconds = SecondsToReach(formation, from, orbitPoint);
            float lookAheadSeconds = stepSeconds + (OrbitLookAhead - OrbitStep) / MathF.Max(formation.CachedMovementSpeed, MinOwnSpeed);
            Formation danger = FindDanger(formation, orbitTarget, orbitPoint, stepSeconds, orbitRadius)
                ?? FindDanger(formation, orbitTarget, ellipse.GetTargetPos(from, OrbitLookAhead, direction), lookAheadSeconds, orbitRadius);
            if (danger == null && from.Distance(orbitPoint) > OrbitLookAhead)
            {
                danger = FindDanger(formation, orbitTarget, Vec2.Lerp(from, orbitPoint, 0.5f), stepSeconds * 0.5f, 0f);
            }
            return danger;
        }

        // Rings searched for a safe firing spot when the orbit is blocked both ways: the orbit itself, then further out
        // (still within bow range), each sampled every 360/SafeRingSamples degrees.
        private static readonly float[] SafeRingExtraRadii = { 0f, 20f, 40f };
        private const int SafeRingSamples = 16;

        // The spot nearest the horse archers, on the innermost ring around the target that has one, where nothing
        // makes them unsafe (FindDanger) and the ride there passes no hard radius.
        private static bool TryFindSafeFiringPoint(Formation formation, Formation target, Vec2 targetCenter, float orbitRadius, Vec2 from, out Vec2 best)
        {
            best = from;
            Vec2 right = target.Direction.Normalized().RightVec();
            float halfWidth = target.Width * 0.5f;
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
                    if (distance >= bestDistance || !Mission.Current.IsPositionInsideBoundaries(point))
                    {
                        continue;
                    }
                    float seconds = SecondsToReach(formation, from, point);
                    if (FindDanger(formation, target, point, seconds, orbitRadius) != null
                        || FindDanger(formation, target, Vec2.Lerp(from, point, 0.5f), seconds * 0.5f, 0f) != null)
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
        private static Ellipse OrbitAround(Formation target, float radius, float ownSpeed, out Vec2 targetCenter)
        {
            targetCenter = PredictedCenter(target, MathF.Min(OrbitStep / ownSpeed, TargetLeadMaxSeconds));
            float halfLength = (target.ArrangementOrder == ArrangementOrder.ArrangementOrderLoose) ? target.Width * 0.25f : target.Width * 0.5f;
            return new Ellipse(targetCenter, radius, halfLength, target.Direction);
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
            AiBehaviorLog.Write("t=" + AiBehaviorLog.Fmt(Mission.Current.CurrentTime) + "\tCHANGE\tHA_ORBIT\t" + LogName(formation)
                + "\t" + mode + "\ttarget=" + LogName(target) + "\tdanger=" + LogName(danger) + "\tdir=" + state.rotationDirection);
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
                    if (closingSpeed > CavalryEscapeMinClosingSpeed && gap < closingSpeed * CavalryEscapeSeconds * fear)
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
            if (orbitTargetStorage.TryGetValue(formation, out Formation previous)
                && previous != null && previous.CountOfUnits > 0 && candidate != null && previous != candidate)
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
            Formation targetFormation = KeepOrbitTarget(__instance.Formation, RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true));
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
                if (cavalryClosing != null)
                {
                    LogOrbitMode(__instance.Formation, GetRotationState(__instance.Formation), "CAVALRY_ESCAPE", targetFormation, cavalryClosing);
                }
                ____engaging = cavalryClosing == null && (isEnemyClose || ((!____engaging) ? ((__instance.Formation.QuerySystem.Formation.CachedAveragePosition - __instance.Formation.QuerySystem.AverageAllyPosition).LengthSquared <= 160000f) : (!(__instance.Formation.QuerySystem.UnderRangedAttackRatio * 0.2f > __instance.Formation.QuerySystem.MakingRangedAttackRatio))));
                if (!____engaging)
                {
                    if (cavalryClosing == null && __instance.Formation.QuerySystem.IsRangedCavalryFormation)
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
                        float distance = 60f;
                        if (!__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            distance = 30f;
                        }

                        RotationChangeClass rotationDirection = GetRotationState(__instance.Formation);

                        float now = Mission.Current.CurrentTime;
                        Vec2 myPosition = __instance.Formation.SmoothedAverageUnitPosition;

                        // At the map edge the orbit turns back instead of pressing into the boundary.
                        float distanceFromBoundary = Mission.Current.GetClosestBoundaryPosition(__instance.Formation.CurrentPosition).Distance(__instance.Formation.CurrentPosition);
                        if (distanceFromBoundary <= __instance.Formation.Width / 2f)
                        {
                            rotationDirection.TryFlip(now);
                        }

                        if (__instance.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            if (__instance.Formation.IsAIControlled)
                            {
                                KeepSquare(__instance.Formation, rotationDirection);
                            }
                            float ownSpeed = MathF.Max(__instance.Formation.CachedMovementSpeed, MinOwnSpeed);
                            Formation orbitTarget = enemyFormation;
                            Ellipse ellipse = OrbitAround(orbitTarget, distance, ownSpeed, out Vec2 targetCenter);
                            string mode = "ORBIT";

                            // The orbit only knows its target. If it would carry the formation past it into another
                            // enemy formation (the next step of an echeloned line), close to enemy cavalry, or off the
                            // map, circle the other way. If both ways are blocked by a foot formation, circle that one
                            // instead, as native circles the outermost formation of a line. Only when that fails too,
                            // stand off the target and shoot, or back away when even that spot is too close.
                            Formation danger = FindOrbitDanger(__instance.Formation, orbitTarget, ellipse, distance, myPosition, rotationDirection.rotationDirection, out Vec2 orbitPoint);
                            bool blocked = danger != null || !Mission.Current.IsPositionInsideBoundaries(orbitPoint);
                            if (blocked && rotationDirection.TryFlip(now))
                            {
                                mode = "FLIP";
                                danger = FindOrbitDanger(__instance.Formation, orbitTarget, ellipse, distance, myPosition, rotationDirection.rotationDirection, out orbitPoint);
                                blocked = danger != null || !Mission.Current.IsPositionInsideBoundaries(orbitPoint);
                            }
                            if (blocked && danger != null && !danger.QuerySystem.IsCavalryFormation && rotationDirection.TrySwitchTarget(now))
                            {
                                mode = "SWITCH";
                                orbitTarget = danger;
                                orbitTargetStorage[__instance.Formation] = orbitTarget;
                                ellipse = OrbitAround(orbitTarget, distance, ownSpeed, out targetCenter);
                                danger = FindOrbitDanger(__instance.Formation, orbitTarget, ellipse, distance, myPosition, rotationDirection.rotationDirection, out orbitPoint);
                                blocked = danger != null || !Mission.Current.IsPositionInsideBoundaries(orbitPoint);
                            }
                            if (blocked)
                            {
                                // Ride to the nearest spot around the target that is safe and shoot from there. A
                                // first version backed off 40 m from wherever they were whenever the spot straight in
                                // front of the target was unsafe; that repeated every tick, so they rode all the way
                                // back to where they spawned, came forward again when the orbit next opened, and so
                                // on. Now they only back off when they themselves are in danger, and otherwise hold.
                                if (TryFindSafeFiringPoint(__instance.Formation, orbitTarget, targetCenter, distance, myPosition, out Vec2 safePoint))
                                {
                                    mode = "SAFE_POINT";
                                    orbitPoint = safePoint;
                                }
                                else
                                {
                                    Formation threat = FindDanger(__instance.Formation, orbitTarget, myPosition, 0f, 0f);
                                    if (threat != null)
                                    {
                                        // Away from where the threat will be, so a formation moving across their path
                                        // is not backed into.
                                        mode = "BACK_OFF";
                                        danger = threat;
                                        Vec2 threatCenter = PredictedCenter(threat, BackOffDistance / ownSpeed);
                                        Vec2 away = (myPosition - ClosestPointOnFormationLine(myPosition, threat, threatCenter)).Normalized();
                                        orbitPoint = myPosition + away * BackOffDistance;
                                    }
                                    else
                                    {
                                        mode = "HOLD";
                                        orbitPoint = myPosition;
                                    }
                                }
                            }
                            LogOrbitMode(__instance.Formation, rotationDirection, mode, orbitTarget, danger);
                            position.SetVec2(orbitPoint);
                        }
                        else
                        {
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
                if (countOfSkirmishers / __instance.Formation.CountOfUnits > 0.6f)
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
