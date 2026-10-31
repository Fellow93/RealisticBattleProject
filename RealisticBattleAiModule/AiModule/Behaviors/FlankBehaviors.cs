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
    [HarmonyPatch(typeof(BehaviorProtectFlank))]
    internal class OverrideBehaviorProtectFlank
    {
        private enum BehaviorState
        {
            HoldingFlank,
            Charging,
            Returning
        }

        // Cavalry flank guard sorties, bounded:
        //  - Trigger: enemy melee cavalry within CavThreatDistance of the hold point, or any enemy formation within CloseThreatDistance.
        //  - Recall: target gone / beyond ReleaseDistance, centre past SortieLeash, > ScatterRecallRatio of riders past it, or MaxSortieSeconds.
        //  - Return ends at ReturnGatherRatio back at the post (or ReturnTimeoutSeconds), then ReengageCooldownSeconds of holding.
        //  - A rested guard also sorties at harassers (melee cavalry / horse archers) near any friendly foot formation's edge,
        //    leashed to that formation and the post, recalled at HarassReleaseDistance.
        private const float CavThreatDistance = 70f;
        private const float CloseThreatDistance = 35f;
        private const float ReleaseDistance = 100f;
        private const float SortieLeash = 60f;
        private const float ScatterRecallRatio = 0.35f;
        private const float MaxSortieSeconds = 25f;
        private const float ReturnGatherRadius = 25f;
        private const float ReturnGatherRatio = 0.75f;
        private const float ReturnTimeoutSeconds = 15f;
        private const float ReengageCooldownSeconds = 5f;
        private const float HarassCavDistance = 70f;
        private const float HarassHorseArcherDistance = 90f;
        private const float HarassReleaseDistance = 110f;
        private const float ProtectLeash = 50f;
        private const float InfantryGuardMeleeRatio = 0.15f;

        internal class FlankSortieState
        {
            public Formation Target;
            // The friendly foot formation a harassment sortie covers; null for a sortie against a threat to the post.
            public Formation Protected;
            public float SortieStartTime;
            public float ReturnStartTime;
            public float HoldStartTime = float.MinValue;
            public float Scale = 1f; // SortieScale at the sortie's start
            public bool Active; // grown by ActiveScale at the start: home leash applies
        }

        internal static readonly Dictionary<Formation, FlankSortieState> sortieStates = new Dictionary<Formation, FlankSortieState>();

        // the close post under fire is held: the hit ratio rises and falls over its 10 s window, which flipped the post every ~10 s
        private const float UnderFireEnter = 0.1f;
        private const float UnderFireLeave = 0.02f;
        private const float UnderFireHoldSeconds = 30f;

        internal static readonly Dictionary<Formation, float> closePostUntil = new Dictionary<Formation, float>();

        private static bool HoldsClosePost(Formation formation)
        {
            float now = Mission.Current.CurrentTime;
            float ratio = formation.QuerySystem.UnderRangedAttackRatio;
            closePostUntil.TryGetValue(formation, out float until);
            if (ratio > UnderFireEnter || (ratio >= UnderFireLeave && now < until))
            {
                until = now + UnderFireHoldSeconds;
                closePostUntil[formation] = until;
            }
            return now < until;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorProtectFlank __instance, ref FormationAI.BehaviorSide ___FlankSide, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref MovementOrder ____chargeToTargetOrder, ref MovementOrder ____movementOrder, ref BehaviorState ____protectFlankState, ref Formation ____mainFormation, ref FormationAI.BehaviorSide ____behaviorSide)
        {
            WorldPosition position = __instance.Formation.QuerySystem.Formation.CachedMedianPosition;

            float distanceFromMainFormation = 90f;
            float closerDistanceFromMainFormation = 30f;
            float distanceOffsetFromMainFormation = 55f;

            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                distanceFromMainFormation = 30f;
                closerDistanceFromMainFormation = 10f;
                distanceOffsetFromMainFormation = 30f;
            }

            RefreshMainFormation(__instance, ref ____mainFormation);
            // the archers took the initiative (no infantry main): mounted guards charge with them
            bool initiative = __instance.Formation != null && __instance.Formation.IsAIControlled && !__instance.Formation.QuerySystem.IsInfantryFormation
                && RBMBehaviorArcherSkirmish.HasInitiative(__instance.Formation.Team);
            if (initiative || ____mainFormation == null || __instance.Formation == null || __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
            {
                FormationQuerySystem closestEnemy = __instance.Formation?.QuerySystem?.ClosestSignificantlyLargeEnemyFormation;
                ____currentOrder = (closestEnemy?.Formation != null)
                    ? MovementOrder.MovementOrderChargeToTarget(closestEnemy.Formation)
                    : MovementOrder.MovementOrderCharge;
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            }
            else
            {
                Vec2 direction = ____mainFormation.Direction;
                bool closePost = HoldsClosePost(__instance.Formation);
                Vec2 v =(__instance.Formation.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - RBMAI.Utilities.GetFormationCenter(____mainFormation)).Normalized();
                Vec2 vec;
                if (____behaviorSide == FormationAI.BehaviorSide.Right || ___FlankSide == FormationAI.BehaviorSide.Right)
                {
                    vec = ____mainFormation.CurrentPosition + v.RightVec().Normalized() * (____mainFormation.Width / 2f + __instance.Formation.Width / 2f + distanceFromMainFormation);
                    vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                    vec += ____mainFormation.Direction * (____mainFormation.Depth / 2f + __instance.Formation.Depth / 2f + distanceOffsetFromMainFormation);
                    position.SetVec2(vec);
                    if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(vec) || closePost)
                    {
                        vec = ____mainFormation.CurrentPosition + v.RightVec().Normalized() * (____mainFormation.Width / 2f + __instance.Formation.Width / 2f + closerDistanceFromMainFormation);
                        vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                        vec += ____mainFormation.Direction;
                        position.SetVec2(vec);
                        if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(vec))
                        {
                            vec = ____mainFormation.CurrentPosition + v.RightVec().Normalized();
                            vec -= ____mainFormation.Direction * 5f;
                            position.SetVec2(vec);
                        }
                    }
                }
                else if (____behaviorSide == FormationAI.BehaviorSide.Left || ___FlankSide == FormationAI.BehaviorSide.Left)
                {
                    vec = ____mainFormation.CurrentPosition + v.LeftVec().Normalized() * (____mainFormation.Width / 2f + __instance.Formation.Width / 2f + distanceFromMainFormation);
                    vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                    vec += ____mainFormation.Direction * (____mainFormation.Depth / 2f + __instance.Formation.Depth / 2f + distanceOffsetFromMainFormation);
                    position.SetVec2(vec);
                    if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(vec) || closePost)
                    {
                        vec = ____mainFormation.CurrentPosition + v.LeftVec().Normalized() * (____mainFormation.Width / 2f + __instance.Formation.Width / 2f + closerDistanceFromMainFormation);
                        vec -= v * (____mainFormation.Depth + __instance.Formation.Depth);
                        vec += ____mainFormation.Direction;
                        position.SetVec2(vec);
                        if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(vec))
                        {
                            vec = ____mainFormation.CurrentPosition + v.LeftVec().Normalized();
                            vec -= ____mainFormation.Direction * 10f;
                            position.SetVec2(vec);
                        }
                    }
                }
                else
                {
                    vec = ____mainFormation.CurrentPosition + v * ((____mainFormation.Depth + __instance.Formation.Depth) * 0.5f + 10f);
                    position.SetVec2(vec);
                }
                // user rule: an infantry guard's place stays within the cohesion limit of its main line (FormationCohesion.ClampGuard; not while charging / in melee); cavalry guards ride wider
                if (__instance.Formation.QuerySystem.IsInfantryFormation)
                {
                    Vec2 pulled;
                    if (FormationCohesion.ClampGuard(__instance.Formation, ____mainFormation, position.AsVec2, direction, out pulled))
                    {
                        position.SetVec2(pulled);
                    }
                }
                // recomputed while charging too (the sortie leash follows the main line); only a holding/returning guard takes it as its order
                ____movementOrder = MovementOrder.MovementOrderMove(position);
                if (____protectFlankState != BehaviorState.Charging)
                {
                    ____currentOrder = ____movementOrder;
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);
                }
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CheckAndChangeState")]
        private static bool PrefixCheckAndChangeState(ref BehaviorProtectFlank __instance, ref FormationAI.BehaviorSide ___FlankSide, ref FacingOrder ___CurrentFacingOrder, ref MovementOrder ____currentOrder, ref MovementOrder ____chargeToTargetOrder, ref MovementOrder ____movementOrder, ref BehaviorState ____protectFlankState, ref Formation ____mainFormation, ref FormationAI.BehaviorSide ____behaviorSide)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                if (__instance.Formation != null && ____movementOrder != null)
                {
                    Vec2 position = ____movementOrder.GetPosition(__instance.Formation);
                    switch (____protectFlankState)
                    {
                        case BehaviorState.HoldingFlank:
                            {
                                FormationQuerySystem closestFormation = __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                                if (closestFormation != null && closestFormation.Formation != null && (closestFormation.Formation.QuerySystem.IsInfantryFormation || closestFormation.Formation.QuerySystem.IsRangedFormation || closestFormation.Formation.QuerySystem.IsCavalryFormation))
                                {
                                    // infantry guard charges the closest enemy at InfantryGuardMeleeRatio in melee or significant enemy foot within CloseThreatDistance; Charging recalls it past 40 m from its post
                                    Formation guard = __instance.Formation;
                                    Formation enemy = closestFormation.Formation;
                                    bool melee = RBMAI.Utilities.FormationFightingInMelee(guard, InfantryGuardMeleeRatio);
                                    bool footClose = false;
                                    if (!melee && !enemy.QuerySystem.IsCavalryFormation && !enemy.QuerySystem.IsRangedCavalryFormation
                                        && ThreatSignificance.IsSignificant(guard, enemy))
                                    {
                                        footClose = FormationGeometry.Gap(guard, FootprintMode.Current, enemy, FootprintMode.ScatterAware) < CloseThreatDistance;
                                    }
                                    if (melee || footClose)
                                    {
                                        ____chargeToTargetOrder = MovementOrder.MovementOrderChargeToTarget(enemy);
                                        ____currentOrder = ____chargeToTargetOrder;
                                        ____protectFlankState = BehaviorState.Charging;
                                    }
                                }
                                break;
                            }
                        case BehaviorState.Charging:
                            {
                                FormationQuerySystem closestFormation = __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                                if (closestFormation == null || closestFormation.Formation == null)
                                {
                                    ____currentOrder = ____movementOrder;
                                    ____protectFlankState = BehaviorState.Returning;
                                    break;
                                }
                                float returnDistance = 40f + (__instance.Formation.Depth + closestFormation.Formation.Depth) / 2f;
                                if (__instance.Formation.QuerySystem.Formation.CachedAveragePosition.DistanceSquared(position) > returnDistance * returnDistance)
                                {
                                    ____currentOrder = ____movementOrder;
                                    ____protectFlankState = BehaviorState.Returning;
                                }
                                break;
                            }
                        case BehaviorState.Returning:
                            if (__instance.Formation.QuerySystem.Formation.CachedAveragePosition.DistanceSquared(position) < 400f)
                            {
                                ____protectFlankState = BehaviorState.HoldingFlank;
                            }
                            break;
                    }
                    return false;
                }
            }
            else
            {
                if (__instance.Formation != null && ____movementOrder != null)
                {
                    Formation formation = __instance.Formation;
                    Vec2 position = ____movementOrder.GetPosition(formation);
                    float now = Mission.Current.CurrentTime;
                    FlankSortieState sortie = GetSortieState(formation, now);
                    switch (____protectFlankState)
                    {
                        case BehaviorState.HoldingFlank:
                            {
                                bool rested = now - sortie.HoldStartTime >= ReengageCooldownSeconds;
                                // outmatched by the enemy's melee cavalry: shorter sorties (kWeak); a cavalry-heavy defender: longer ones (kMount)
                                float scale = SortieScale(formation);
                                bool active = ActiveScale(formation) > 1f;
                                Formation target = null;
                                Formation protectedFormation = null;
                                string exposed = null;
                                FormationQuerySystem closestFormation = formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                                if (closestFormation != null && closestFormation.Formation != null)
                                {
                                    // the guard's footprint at its post (the order position is its front-centre)
                                    Footprint post = FormationGeometry.AtFront(formation, position, formation.Direction);
                                    Footprint enemyShape;
                                    float gap = FormationGeometry.TryGet(closestFormation.Formation, FootprintMode.ScatterAware, out enemyShape)
                                        ? FormationGeometry.Gap(post, enemyShape) : float.MaxValue;
                                    bool closeThreat = gap < CloseThreatDistance;
                                    bool cavThreat = closestFormation.IsCavalryFormation && gap < CavThreatDistance * scale;
                                    if (closeThreat || (cavThreat && rested))
                                    {
                                        target = closestFormation.Formation;
                                    }
                                }
                                if (target == null && rested && ____mainFormation != null)
                                {
                                    int sideSign = (____behaviorSide == FormationAI.BehaviorSide.Right || ___FlankSide == FormationAI.BehaviorSide.Right) ? 1
                                        : (____behaviorSide == FormationAI.BehaviorSide.Left || ___FlankSide == FormationAI.BehaviorSide.Left) ? -1 : 0;
                                    target = FindHarasser(formation, ____mainFormation, sideSign, scale, position, active, out protectedFormation, out exposed);
                                }
                                if (target != null)
                                {
                                    sortie.Target = target;
                                    sortie.Protected = protectedFormation;
                                    sortie.SortieStartTime = now;
                                    sortie.Scale = scale;
                                    sortie.Active = active;
                                    if (scale != 1f || exposed != null)
                                    {
                                        DecisionTrace.Note(formation, "ProtectFlank", "sortie", "scale=" + ArmyComposition.F2(scale)
                                            + ((exposed != null) ? " exposed=" + exposed + " " + TerrainCavalry.FormationName(target) : "")
                                            + " " + ArmyComposition.DescribeK(formation.Team) + " " + ArmyComposition.DescribeOwnK(formation.Team));
                                    }
                                    ____chargeToTargetOrder = MovementOrder.MovementOrderChargeToTarget(target);
                                    ____currentOrder = ____chargeToTargetOrder;
                                    ____protectFlankState = BehaviorState.Charging;
                                }
                                break;
                            }
                        case BehaviorState.Charging:
                            {
                                if (ShouldRecallSortie(formation, sortie, position, now))
                                {
                                    sortie.Target = null;
                                    sortie.Protected = null;
                                    sortie.ReturnStartTime = now;
                                    ____currentOrder = ____movementOrder;
                                    ____protectFlankState = BehaviorState.Returning;
                                }
                                break;
                            }
                        case BehaviorState.Returning:
                            if (now - sortie.ReturnStartTime >= ReturnTimeoutSeconds || RiderRatioWithin(formation, position, ReturnGatherRadius + formation.Width / 2f) >= ReturnGatherRatio)
                            {
                                sortie.HoldStartTime = now;
                                ____protectFlankState = BehaviorState.HoldingFlank;
                            }
                            break;
                    }
                    return false;
                }
            }
            return true;
        }

        // a guard found Charging/Returning with no record gets a fresh one (no target: a stale charge is recalled at once)
        private static FlankSortieState GetSortieState(Formation formation, float now)
        {
            if (!sortieStates.TryGetValue(formation, out FlankSortieState sortie))
            {
                sortie = new FlankSortieState { SortieStartTime = now, ReturnStartTime = now };
                sortieStates[formation] = sortie;
            }
            return sortie;
        }

        private const float ExposedScreenGap = 30f; // significant enemy melee foot this close to an exposed target screens it
        private const float ExposedRangedWeight = 1.5f; // exposed targets' distance / weight (harassers 1)
        private const float ExposedFlankWeight = 1.3f;
        private const float ExposedStragglerWeight = 1.2f;
        private const float ExposedFlankMargin = 10f; // m beyond the enemy main's half width / half depth

        /// <summary>AI thread: (1 - ArmyComposition.WeakShrink x kWeak) x ActiveScale (1 for a normal army against a normal enemy).</summary>
        private static float SortieScale(Formation formation)
        {
            return (1f - ArmyComposition.WeakShrink * ArmyComposition.KWeak(formation?.Team)) * ActiveScale(formation);
        }

        /// <summary>AI thread: a cavalry-heavy defender sorties farther and longer: Lerp(1, ArmyComposition.ActiveSortieGrowth, kMount); 1 for the attacker / kMount = 0.</summary>
        private static float ActiveScale(Formation formation)
        {
            Team team = formation?.Team;
            return (team != null && team.Side == BattleSideEnum.Defender) ? ArmyComposition.Lerp(1f, ArmyComposition.ActiveSortieGrowth, ArmyComposition.KMount(team)) : 1f;
        }

        /// <summary>Gap (m) from the team's position anchor to the point; MaxValue without an anchor.</summary>
        private static float HomeGap(Formation guard, Vec2 point)
        {
            Formation anchor = ArmyComposition.AnchorFor(guard);
            return (anchor != null) ? DistanceToFormationBox(anchor, point) : float.MaxValue;
        }

        private static bool ShouldRecallSortie(Formation formation, FlankSortieState sortie, Vec2 holdPoint, float now)
        {
            Formation target = sortie.Target;
            float scale = sortie.Scale;
            if (target == null || target.CountOfUnits == 0 || now - sortie.SortieStartTime >= MaxSortieSeconds * scale)
            {
                return true;
            }
            if (sortie.Active)
            {
                // a grown sortie keeps its home leash from the position anchor
                float home = HomeGap(formation, RBMAI.Utilities.GetFormationCenter(formation));
                if (home < float.MaxValue && home > ArmyComposition.ActiveHomeLeash)
                {
                    return true;
                }
            }
            float depths = (formation.Depth + target.Depth) / 2f;
            Vec2 targetCenter = RBMAI.Utilities.GetFormationCenter(target);
            Formation protectedFormation = sortie.Protected;
            if (protectedFormation != null)
            {
                if (protectedFormation.CountOfUnits == 0 || DistanceToFormationBox(protectedFormation, targetCenter) > HarassReleaseDistance * scale + target.Depth / 2f)
                {
                    return true;
                }
            }
            else
            {
                float release = ReleaseDistance * scale + depths;
                if (targetCenter.DistanceSquared(holdPoint) > release * release)
                {
                    return true;
                }
            }
            float leash = SortieLeash * scale + depths;
            float protectLeash = ProtectLeash + formation.Depth / 2f;
            Vec2 center = RBMAI.Utilities.GetFormationCenter(formation);
            if (center.DistanceSquared(holdPoint) > leash * leash && (protectedFormation == null || DistanceToFormationBox(protectedFormation, center) > protectLeash))
            {
                return true;
            }
            return 1f - RiderRatioWithin(formation, holdPoint, leash, protectedFormation, protectLeash) > ScatterRecallRatio;
        }

        // counted over every rider (the average position of a scattered formation stays mid-field); within protectRadius of the protected edge counts as near
        private static float RiderRatioWithin(Formation formation, Vec2 point, float radius, Formation protectedFormation = null, float protectRadius = 0f)
        {
            float radiusSq = radius * radius;
            int total = 0;
            int near = 0;
            formation.ApplyActionOnEachUnit(agent =>
            {
                if (agent.IsDetachedFromFormation || agent.IsRunningAway)
                {
                    return;
                }
                total++;
                Vec2 agentPosition = agent.Position.AsVec2;
                if (agentPosition.DistanceSquared(point) <= radiusSq || (protectedFormation != null && DistanceToFormationBox(protectedFormation, agentPosition) <= protectRadius))
                {
                    near++;
                }
            });
            return total > 0 ? (float)near / total : 1f;
        }

        // closest enemy cavalry / horse archer formation working a friendly foot formation; with sideSign (+1 right, -1 left) harassers clearly on the far side are left to the other guard.
        // active (grown defender sorties): also EXPOSED enemy foot within the sortie's reach of the post and the home leash - foot archers, a formation beside / behind
        // the enemy main, stragglers insignificant to the guard - unscreened; scored by distance / weight, harassers weight 1.
        private static Formation FindHarasser(Formation guard, Formation mainFormation, int sideSign, float scale, Vec2 holdPoint, bool active, out Formation protectedFormation, out string exposed)
        {
            protectedFormation = null;
            exposed = null;
            Formation best = null;
            float bestDistanceSq = float.MaxValue;
            Vec2 guardCenter = RBMAI.Utilities.GetFormationCenter(guard);
            Vec2 mainCenter = RBMAI.Utilities.GetFormationCenter(mainFormation);
            Vec2 sideVec = (guard.QuerySystem.Team.MedianTargetFormationPosition.AsVec2 - mainCenter).Normalized().RightVec() * sideSign;
            bool otherFlankGuarded = sideSign != 0 && IsFlankGuarded(guard, sideSign > 0 ? FormationAI.BehaviorSide.Left : FormationAI.BehaviorSide.Right);
            foreach (Team team in Mission.Current.Teams)
            {
                if (!team.IsEnemyOf(guard.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits == 0)
                    {
                        continue;
                    }
                    bool horseArchers = enemy.QuerySystem.IsRangedCavalryFormation;
                    if (!horseArchers && !enemy.QuerySystem.IsCavalryFormation)
                    {
                        if (!active)
                        {
                            continue;
                        }
                        string kind;
                        float weight = ExposedWeight(guard, enemy, out kind);
                        if (weight <= 0f)
                        {
                            continue;
                        }
                        Vec2 exposedCenter = RBMAI.Utilities.GetFormationCenter(enemy);
                        if (otherFlankGuarded && (exposedCenter - mainCenter).DotProduct(sideVec) < -mainFormation.Width * 0.25f)
                        {
                            continue;
                        }
                        // reachable inside the recall rules: the sortie leash from the post, the release distance, the home leash
                        float depths = (guard.Depth + enemy.Depth) / 2f;
                        float release = ReleaseDistance * scale + depths;
                        float home = HomeGap(guard, exposedCenter);
                        if (DistanceToFormationBox(enemy, holdPoint) > SortieLeash * scale + guard.Depth / 2f || exposedCenter.DistanceSquared(holdPoint) > release * release
                            || (home < float.MaxValue && home > ArmyComposition.ActiveHomeLeash) || ReactionMonitor.IsRetreating(enemy))
                        {
                            continue;
                        }
                        float screenGap;
                        if (ReactionMonitor.ScreeningFoot(Mission.Current, guard.Team, guard, enemy, ExposedScreenGap, out screenGap) != null)
                        {
                            continue;
                        }
                        float exposedScore = exposedCenter.DistanceSquared(guardCenter) / (weight * weight);
                        if (exposedScore < bestDistanceSq)
                        {
                            bestDistanceSq = exposedScore;
                            best = enemy;
                            protectedFormation = null;
                            exposed = kind;
                        }
                        continue;
                    }
                    Vec2 enemyCenter = RBMAI.Utilities.GetFormationCenter(enemy);
                    if (otherFlankGuarded && (enemyCenter - mainCenter).DotProduct(sideVec) < -mainFormation.Width * 0.25f)
                    {
                        continue;
                    }
                    float threatDistance = (horseArchers ? HarassHorseArcherDistance : HarassCavDistance) * scale + enemy.Depth / 2f;
                    Formation victim = ClosestFootFormationWithin(guard, enemyCenter, threatDistance);
                    if (victim == null)
                    {
                        continue;
                    }
                    float distanceSq = enemyCenter.DistanceSquared(guardCenter);
                    if (distanceSq < bestDistanceSq)
                    {
                        bestDistanceSq = distanceSq;
                        best = enemy;
                        protectedFormation = victim;
                        exposed = null;
                    }
                }
            }
            return best;
        }

        /// <summary>Enemy foot exposed to a sortie: foot archers (ranged), a formation beside / behind the enemy main (flank / rear), one insignificant to the guard (straggler); the best weight, 0 for none.</summary>
        private static float ExposedWeight(Formation guard, Formation enemy, out string kind)
        {
            kind = null;
            float weight = 0f;
            if (ArmyComposition.IsMounted(enemy))
            {
                return 0f;
            }
            if (ArmyComposition.IsArchers(enemy))
            {
                weight = ExposedRangedWeight;
                kind = "ranged";
            }
            Formation enemyMain = FormationCohesion.TeamMain(enemy.Team);
            if (weight < ExposedFlankWeight && enemyMain != null && enemyMain != enemy)
            {
                Vec2 rel = RBMAI.Utilities.GetFormationCenter(enemy) - RBMAI.Utilities.GetFormationCenter(enemyMain);
                Vec2 dir = enemyMain.Direction;
                if (dir.IsValid && dir.LengthSquared > 0.01f)
                {
                    dir = dir.Normalized();
                    if (rel.DotProduct(dir) < -(enemyMain.Depth * 0.5f + ExposedFlankMargin))
                    {
                        weight = ExposedFlankWeight;
                        kind = "rear";
                    }
                    else if (MathF.Abs(rel.DotProduct(dir.RightVec())) > enemyMain.Width * 0.5f + ExposedFlankMargin)
                    {
                        weight = ExposedFlankWeight;
                        kind = "flank";
                    }
                }
            }
            if (weight < ExposedStragglerWeight && !ThreatSignificance.IsSignificant(guard, enemy))
            {
                weight = ExposedStragglerWeight;
                kind = "straggler";
            }
            return weight;
        }

        private static Formation ClosestFootFormationWithin(Formation guard, Vec2 point, float maxDistance)
        {
            Formation closest = null;
            float closestDistance = maxDistance;
            foreach (Formation friendly in guard.Team.FormationsIncludingSpecialAndEmpty)
            {
                if (friendly == null || friendly == guard || friendly.CountOfUnits == 0 || friendly.QuerySystem.IsCavalryFormation || friendly.QuerySystem.IsRangedCavalryFormation)
                {
                    continue;
                }
                float distance = DistanceToFormationBox(friendly, point);
                if (distance <= closestDistance)
                {
                    closestDistance = distance;
                    closest = friendly;
                }
            }
            return closest;
        }

        private static bool IsFlankGuarded(Formation guard, FormationAI.BehaviorSide side)
        {
            foreach (Formation friendly in guard.Team.FormationsIncludingSpecialAndEmpty)
            {
                if (friendly != null && friendly != guard && friendly.CountOfUnits > 0 && friendly.AI.ActiveBehavior is BehaviorProtectFlank otherGuard
                    && (otherGuard.FlankSide == side || friendly.AI.Side == side))
                {
                    return true;
                }
            }
            return false;
        }

        // distance from a point to the formation's footprint (0 inside it)
        private static float DistanceToFormationBox(Formation formation, Vec2 point)
        {
            Footprint shape;
            return FormationGeometry.TryGet(formation, FootprintMode.Current, out shape) ? FormationGeometry.GapToPoint(shape, point) : float.MaxValue;
        }

        // An infantry guard charged head-on by melee cavalry braces as BehaviorCharge / BehaviorAdvance do (it used to march on in Line).
        private const float GuardBraceHoldSeconds = 8f;
        private const float GuardBraceMaxSeconds = 25f;
        private const float GuardBraceCooldownSeconds = 15f;

        private sealed class GuardBrace
        {
            public WorldPosition Hold = WorldPosition.Invalid;
            public float Start = float.NaN;
            public float LastCharged = float.MinValue;
            public float CooldownUntil = float.MinValue;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorProtectFlank, GuardBrace> GuardBraces =
            new System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorProtectFlank, GuardBrace>();

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static bool PrefixTickOccasionally(BehaviorProtectFlank __instance, ref BehaviorState ____protectFlankState, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            Formation guard = __instance.Formation;
            if (guard == null || !guard.IsAIControlled || !guard.QuerySystem.IsInfantryFormation || Mission.Current == null)
            {
                return true;
            }
            GuardBrace brace = GuardBraces.GetOrCreateValue(__instance);
            float now = Mission.Current.CurrentTime;
            Formation riders = null;
            float dist = float.MaxValue;
            // a charging guard stays committed; at InfantryGuardMeleeRatio the guard's own charge rule takes over
            if (____protectFlankState != BehaviorState.Charging && now >= brace.CooldownUntil
                && !RBMAI.Utilities.FormationFightingInMelee(guard, InfantryGuardMeleeRatio))
            {
                riders = AiModule.RbmBehaviors.OverrideBehaviorCharge.ChargingCavalry(guard, AiModule.RbmBehaviors.OverrideBehaviorCharge.ChargedAtDistance, out dist);
                if (riders != null)
                {
                    brace.LastCharged = now;
                }
                else if (!float.IsNaN(brace.Start) && now - brace.LastCharged < GuardBraceHoldSeconds)
                {
                    // the riders' reform Move between charges: keep the wall while they stay close
                    Formation near = RBMAI.Utilities.FindSignificantEnemy(guard, false, false, true, false, false);
                    if (near != null && near.QuerySystem.IsCavalryFormation)
                    {
                        float d = (RBMAI.Utilities.GetFormationCenter(near) - RBMAI.Utilities.GetFormationCenter(guard)).Length;
                        if (d < AiModule.RbmBehaviors.OverrideBehaviorCharge.ChargedAtDistance)
                        {
                            riders = near;
                            dist = d;
                        }
                    }
                }
            }
            if (riders != null && !float.IsNaN(brace.Start) && now - brace.Start > GuardBraceMaxSeconds)
            {
                brace.CooldownUntil = now + GuardBraceCooldownSeconds;
                riders = null;
            }
            if (riders == null)
            {
                brace.Start = float.NaN;
                brace.Hold = WorldPosition.Invalid;
                return true;
            }
            if (float.IsNaN(brace.Start))
            {
                brace.Start = now;
                brace.Hold = RBMAI.Utilities.GetFormationCenterWorldPosition(guard);
                AiModule.RbmBehaviors.OverrideBehaviorCharge.LogBrace(guard, "ProtectFlank", now, AiModule.RbmBehaviors.OverrideBehaviorCharge.ChargedAtDistance);
            }
            if (brace.Hold.IsValid)
            {
                ____currentOrder = MovementOrder.MovementOrderMove(brace.Hold);
                guard.SetMovementOrder(____currentOrder);
            }
            if (dist > 10f)
            {
                ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection((RBMAI.Utilities.GetFormationCenter(riders) - RBMAI.Utilities.GetFormationCenter(guard)).Normalized());
                guard.SetFacingOrder(___CurrentFacingOrder);
            }
            // never a ranged formation (user rule)
            if (!RBMAI.Utilities.IsRangedForArrangement(guard))
            {
                guard.SetArrangementOrder(ArrangementOrder.ArrangementOrderShieldWall);
            }
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref BehaviorProtectFlank __instance)
        {
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation)
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        private static bool PrefixGetAiWeight(ref BehaviorProtectFlank __instance, ref float __result, ref Formation ____mainFormation)
        {
            RefreshMainFormation(__instance, ref ____mainFormation);
            if (__instance.Formation.AI.IsMainFormation)
            {
                __result = 0f;
                return false;
            }
            if (____mainFormation == null)
            {
                __result = 0.5f;
                return false;
            }
            __result = 1.2f;
            return false;
        }

        // An emptied formation keeps AI.IsMainFormation, so the guard stood beside the dead main's last spot facing its last direction.
        // No melee-infantry main: the guard sits on the position anchor (archers) instead of charging the closest enemy at weight 0.5.
        private static void RefreshMainFormation(BehaviorProtectFlank instance, ref Formation mainFormation)
        {
            Formation guard = instance.Formation;
            if (guard?.Team != null && FormationCohesion.TeamMain(guard.Team) == null)
            {
                Formation anchor = ArmyComposition.AnchorFor(guard);
                if (anchor != null)
                {
                    mainFormation = anchor;
                    return;
                }
            }
            if (mainFormation == null || mainFormation.CountOfUnits <= 0 || !mainFormation.AI.IsMainFormation)
            {
                mainFormation = instance.Formation?.Team?.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).FirstOrDefault((Formation f) => f.AI.IsMainFormation);
            }
            // a token foot group never stands for the army (user rule): no main, the all-mounted path
            if (mainFormation != null && guard?.Team != null && FormationCohesion.TeamMain(guard.Team) == null
                && ArmyComposition.IsToken(guard.Team, ArmyComposition.CategoryOf(mainFormation)))
            {
                mainFormation = null;
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorVanguard))]
    internal class OverrideBehaviorVanguard
    {
        private static readonly MethodInfo CalculateCurrentOrderMethod = typeof(BehaviorVanguard).GetMethod("CalculateCurrentOrder", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static bool PrefixTickOccasionally(ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder, BehaviorVanguard __instance)
        {
            CalculateCurrentOrderMethod.Invoke(__instance, new object[] { });

            __instance.Formation.SetMovementOrder(____currentOrder);
            __instance.Formation.SetFacingOrder(___CurrentFacingOrder);
            if (__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null && RBMAI.Utilities.GetFormationCenter(__instance.Formation).DistanceSquared(RBMAI.Utilities.GetFormationCenter(__instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation)) > 1600f && __instance.Formation.QuerySystem.UnderRangedAttackRatio > 0.2f - ((__instance.Formation.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Loose) ? 0.1f : 0f))
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderSkein);
            }
            else
            {
                __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderSkein);
            }

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("OnBehaviorActivatedAux")]
        private static void PostfixOnBehaviorActivatedAux(ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder, BehaviorVanguard __instance)
        {
            __instance.Formation.SetFormOrder(FormOrder.FormOrderDeep);
            __instance.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderSkein);
        }
    }

    [HarmonyPatch(typeof(BehaviorPullBack))]
    internal class OverrideBehaviorPullBack
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
