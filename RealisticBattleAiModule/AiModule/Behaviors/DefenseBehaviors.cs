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
    /// <summary>Facing of a holding infantry line (Defend, HoldHighGround, Regroup; other formations are left
    /// alone). A defending side's infantry faces the enemy's main infantry as it approaches. Otherwise the target
    /// comes from FindSignificantEnemy without cavalry, so the line kept facing the enemy foot while horsemen rode
    /// round behind it; while nothing is in contact with that front, a closer melee cavalry formation takes over.</summary>
    internal static class HoldFacing
    {
        /// <summary>Gap (m) between the two formations' edges that still counts as the front being in contact.</summary>
        private const float FrontContactMargin = 10f;

        public static Vec2 Direction(Formation formation, Formation frontEnemy, Vec2 frontDirection)
        {
            // Infantry only: archers and cavalry also run Defend/HoldHighGround and keep their old facing.
            if (!formation.QuerySystem.IsInfantryFormation)
            {
                return frontDirection;
            }
            if (formation.Team.Side == BattleSideEnum.Defender)
            {
                Formation mainInfantry = FindEnemyMainInfantry(formation);
                if (mainInfantry != null)
                {
                    Vec2 toMain = RBMAI.Utilities.GetFormationCenter(mainInfantry) - RBMAI.Utilities.GetFormationCenter(formation);
                    return toMain.IsNonZero() ? toMain.Normalized() : frontDirection;
                }
            }
            float frontDistance = RBMAI.Utilities.GetFormationDistance(formation, frontEnemy);
            if (frontDistance < (formation.Depth + frontEnemy.Depth) * 0.5f + FrontContactMargin)
            {
                return frontDirection;
            }
            Formation closestCavalry = null;
            float closestDistance = frontDistance;
            foreach (Team team in Mission.Current.Teams)
            {
                if (!team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingEmpty)
                {
                    if (enemy == frontEnemy || enemy.CountOfUnits <= 0 || !enemy.QuerySystem.IsCavalryFormation || enemy.QuerySystem.IsRangedCavalryFormation)
                    {
                        continue;
                    }
                    float distance = RBMAI.Utilities.GetFormationDistance(formation, enemy);
                    if (distance < closestDistance)
                    {
                        closestCavalry = enemy;
                        closestDistance = distance;
                    }
                }
            }
            if (closestCavalry == null)
            {
                return frontDirection;
            }
            Vec2 direction = RBMAI.Utilities.GetFormationCenter(closestCavalry) - RBMAI.Utilities.GetFormationCenter(formation);
            return direction.IsNonZero() ? direction.Normalized() : frontDirection;
        }

        // The enemy's main infantry: its AI main formation if that is infantry, else its largest infantry formation.
        private static Formation FindEnemyMainInfantry(Formation formation)
        {
            Formation largest = null;
            foreach (Team team in Mission.Current.Teams)
            {
                if (!team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingEmpty)
                {
                    if (enemy.CountOfUnits <= 0 || !enemy.QuerySystem.IsInfantryFormation)
                    {
                        continue;
                    }
                    if (enemy.AI != null && enemy.AI.IsMainFormation)
                    {
                        return enemy;
                    }
                    if (largest == null || enemy.CountOfUnits > largest.CountOfUnits)
                    {
                        largest = enemy;
                    }
                }
            }
            return largest;
        }
    }

    [HarmonyPatch(typeof(BehaviorDefend))]
    internal class OverrideBehaviorDefend
    {
        public static Dictionary<Formation, WorldPosition> positionsStorage = new Dictionary<Formation, WorldPosition> { };

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(ref BehaviorDefend __instance, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged, ref FacingOrder ___CurrentFacingOrder)
        {
            // Gap fix 2026-10-22 (as OverrideBehaviorHoldHighGround): the stored point is committed per activation - dropped
            // while Defend is not the active behavior and stored only by the active one - so an activation with the enemy
            // already under 200 m no longer moves to (0,0) (no entry) or to a stale point from an earlier activation.
            bool active = __instance.Formation != null && __instance.Formation.AI != null && __instance.Formation.AI.ActiveBehavior == __instance;
            if (!active && __instance.Formation != null)
            {
                positionsStorage.Remove(__instance.Formation);
            }
            if (__instance.Formation != null && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                WorldPosition medianPositionNew = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);

                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);

                if (significantEnemy != null)
                {
                    Vec2 enemyDirection = RBMAI.Utilities.GetFormationCenter(significantEnemy) - RBMAI.Utilities.GetFormationCenter(__instance.Formation);
                    float distance = enemyDirection.Normalize();
                    enemyDirection = HoldFacing.Direction(__instance.Formation, significantEnemy, enemyDirection);
                    if (distance < (200f))
                    {
                        WorldPosition newPosition;
                        if (!positionsStorage.TryGetValue(__instance.Formation, out newPosition) || !newPosition.IsValid)
                        {
                            // No point committed this activation (the enemy was already near): stand where it is.
                            newPosition = medianPositionNew;
                            if (active)
                            {
                                positionsStorage[__instance.Formation] = newPosition;
                            }
                        }
                        ____currentOrder = MovementOrder.MovementOrderMove(newPosition);
                        ___IsCurrentOrderChanged = true;
                        ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                    }
                    else
                    {
                        if (__instance.DefensePosition.IsValid)
                        {
                            WorldPosition newPosition = __instance.DefensePosition;
                            newPosition.SetVec2(newPosition.AsVec2 + __instance.Formation.Direction * 10f);
                            ____currentOrder = MovementOrder.MovementOrderMove(newPosition);
                            if (active)
                            {
                                positionsStorage[__instance.Formation] = newPosition;
                            }

                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                        }
                        else
                        {
                            WorldPosition newPosition = medianPositionNew;
                            newPosition.SetVec2(newPosition.AsVec2 + __instance.Formation.Direction * 10f);
                            if (active)
                            {
                                positionsStorage[__instance.Formation] = newPosition;
                            }
                            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Hold high ground commits to ONE spot. Native re-picks its spot (QuerySystem.HighGroundCloseToForeseenBattleGround)
    /// every 10 s from a square centred on the formation's CURRENT position, half the distance to the enemy wide, sloping
    /// toward the enemy's moving median formation, and only stops once an enemy is inside archer range. A line walking to
    /// its spot therefore moved the next search with it: the 2026-10-20 21:56 log shows the defender's target jump
    /// 150-230 m four times in 60 s (once straight back the way it came) while the enemy closed from 540 m.
    /// Now the spot is picked when the behavior becomes active and kept; it is picked again only while the enemy is still
    /// at least <see cref="RepickMinEnemyDistance"/> away and has swung more than 45 degrees off the bearing it had at the pick.
    /// A spot the line cannot reach well before the enemy does is given up for the ground it stands on.
    /// The old code went to a stored position under 200 m, which was never stored if the behavior started with the enemy
    /// already that close (or the line spent the approach 70-100 m from the map edge): Move to (0,0).</summary>
    [HarmonyPatch(typeof(BehaviorHoldHighGround))]
    internal class OverrideBehaviorHoldHighGround
    {
        internal sealed class HighGroundCommit
        {
            public WorldPosition Position;
            public Vec2 EnemyBearing;
            public float PickedAt;
        }

        /// <summary>The committed spot of each formation whose HoldHighGround is ACTIVE; dropped when it stops being active,
        /// so the next activation picks fresh. Only the AI tick touches it (CalculateCurrentOrder also runs from
        /// FormationAI.FindBestBehavior's PrecalculateMovementOrder, same thread).</summary>
        public static Dictionary<Formation, HighGroundCommit> commits = new Dictionary<Formation, HighGroundCommit> { };

        /// <summary>Under this (centre to centre, to the significant enemy) the spot is never picked again.</summary>
        private const float RepickMinEnemyDistance = 200f;
        /// <summary>cos 45 degrees: the enemy bearing swing that allows a re-pick while it is still far.</summary>
        private const float RepickBearingCos = 0.707f;
        /// <summary>No re-pick sooner than this after the last pick (the significant enemy can alternate between two
        /// formations on different bearings).</summary>
        private const float RepickMinSeconds = 20f;
        /// <summary>The spot is kept only while the line gets there in under this share of the enemy's time to arrive.</summary>
        private const float ReachTimeShare = 0.6f;
        /// <summary>Within this of its spot the line has arrived (no more reach checks).</summary>
        private const float ArrivedDistance = 10f;

        [HarmonyPostfix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static void PostfixCalculateCurrentOrder(ref BehaviorHoldHighGround __instance, ref MovementOrder ____currentOrder, ref Boolean ___IsCurrentOrderChanged, ref FacingOrder ___CurrentFacingOrder)
        {
            Formation formation = __instance.Formation;
            if (formation == null)
            {
                return;
            }
            bool active = formation.AI.ActiveBehavior == __instance;
            commits.TryGetValue(formation, out HighGroundCommit commit);
            if (!active && commit != null)
            {
                // The behavior stopped being active (charge, regroup, tactic change): its next activation picks again.
                commits.Remove(formation);
                commit = null;
            }
            if (formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation == null)
            {
                return;
            }
            Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(formation, true, true, false, false, false, true);
            if (significantEnemy == null)
            {
                return;
            }
            WorldPosition center = RBMAI.Utilities.GetFormationCenterWorldPosition(formation);
            Vec2 bearing = RBMAI.Utilities.GetFormationCenter(significantEnemy) - center.AsVec2;
            float distance = bearing.Normalize();
            Vec2 enemyDirection = HoldFacing.Direction(formation, significantEnemy, bearing);

            float now = Mission.Current.CurrentTime;
            if (commit != null && distance >= RepickMinEnemyDistance && now - commit.PickedAt >= RepickMinSeconds
                && commit.EnemyBearing.DotProduct(bearing) < RepickBearingCos)
            {
                commit = null;
            }
            if (commit == null)
            {
                commit = new HighGroundCommit
                {
                    Position = distance >= RepickMinEnemyDistance ? PickSpot(formation, center) : center,
                    EnemyBearing = bearing,
                    PickedAt = now
                };
                if (active)
                {
                    commits[formation] = commit;
                }
            }
            float distanceToSpot = center.AsVec2.Distance(commit.Position.AsVec2);
            if (distanceToSpot > ArrivedDistance)
            {
                float timeToSpot = distanceToSpot / Math.Max(formation.QuerySystem.MovementSpeedMaximum, 0.5f);
                float timeToEnemy = distance / Math.Max(significantEnemy.QuerySystem.MovementSpeedMaximum, 1f);
                if (timeToSpot > timeToEnemy * ReachTimeShare)
                {
                    // Out of reach, or caught on the march: stand here instead (a later bearing re-pick may still move it).
                    commit.Position = center;
                }
            }
            ____currentOrder = MovementOrder.MovementOrderMove(commit.Position);
            ___IsCurrentOrderChanged = true;
            ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
        }

        /// <summary>Native's high-ground pick (searched around where the line stands now), kept 100 m clear of the map edge.</summary>
        private static WorldPosition PickSpot(Formation formation, WorldPosition center)
        {
            WorldPosition spot = center;
            spot.SetVec2(formation.QuerySystem.HighGroundCloseToForeseenBattleGround);
            Vec2 posVec2 = spot.AsVec2;
            Vec2 closestBoundary = Mission.Current.GetClosestBoundaryPosition(posVec2);
            float distFromBoundary = closestBoundary.Distance(posVec2);
            if (distFromBoundary <= 70f)
            {
                Vec2 awayFromBoundary = (posVec2 - closestBoundary).Normalized();
                spot.SetVec2(posVec2 + awayFromBoundary * (100f - distFromBoundary));
            }
            return spot;
        }
    }

    [HarmonyPatch(typeof(BehaviorRegroup))]
    internal class OverrideBehaviorRegroup
    {
        /// <summary>USER RULE (2026-10-04): an attacker's advance phase must actually advance. Ordinary (non-rally)
        /// Regroup holds the formation on its own centre, and on a ragged line it out-weighs Advance and only lets go
        /// once the line is almost tidy, so it could hold indefinitely (worst after a reinforcement wave). For a
        /// field-battle attacker in its advance phase it may stay active at most this long...</summary>
        private const float AttackerRegroupMaxSeconds = 10f;

        /// <summary>...and may not take over again until the forward behavior has run at least this long.</summary>
        private const float AttackerAdvanceMinSeconds = 20f;

        /// <summary>FormationAI.ActiveBehavior's setter stamps PreserveExpireTime = now + this on activation (its only writer).</summary>
        private const float NativePreserveSeconds = 10f;

        /// <summary>A Regroup tick this long (s) after its last one is a new regroup: its hold point is taken again.</summary>
        private const float RegroupHoldResetSeconds = 2f;

        /// <summary>Where an active Regroup holds: the formation's FRONT-centre when the regroup began, fixed for it. A Move
        /// order's point is the front-centre (native lays the ranks out behind it), so holding at the formation's own centre,
        /// re-read every tick, walked the whole block backwards (2026-10-22 logs: attacker infantry 47-54 m back during its
        /// opening regroup). Keyed on the behavior instance (dies with the mission, no reset needed); AI thread only.</summary>
        private sealed class RegroupHold
        {
            public WorldPosition Hold = WorldPosition.Invalid;
            public float LastTick = float.MinValue;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorRegroup, RegroupHold> RegroupHolds =
            new System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorRegroup, RegroupHold>();

        /// <summary>AI thread: the formation's front-centre (centre + facing x half its depth) on a copy of the cached median,
        /// moved once (the AdvanceBehavior.FrontHoldPosition rule).</summary>
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

        /// <summary>The hold point: fixed while Regroup is the active behavior, taken again after a break; a mere candidate
        /// (FindBestBehavior) gets a fresh one and drops the stored point.</summary>
        private static WorldPosition RegroupHoldPosition(BehaviorRegroup regroup)
        {
            Formation formation = regroup.Formation;
            RegroupHold state = RegroupHolds.GetOrCreateValue(regroup);
            if (formation.AI == null || formation.AI.ActiveBehavior != regroup)
            {
                state.Hold = WorldPosition.Invalid;
                state.LastTick = float.MinValue;
                return FrontHoldPosition(formation);
            }
            float now = Mission.Current.CurrentTime;
            if (!state.Hold.IsValid || now - state.LastTick > RegroupHoldResetSeconds)
            {
                state.Hold = FrontHoldPosition(formation);
            }
            state.LastTick = now;
            return state.Hold;
        }

        /// <summary>Regroup tidies a formation into Line, but only what it must change: never a player-led or ranged formation
        /// (USER RULE 2026-10-21, IsRangedForArrangement), a shield wall or a Loose one (TerrainShape's broken-ground Loose,
        /// the missile-fire Loose) - forcing Line there flipped it back and forth with the advance every interlude.</summary>
        private static void ApplyRegroupArrangement(Formation formation)
        {
            if (formation == null || !formation.IsAIControlled || RBMAI.Utilities.IsRangedForArrangement(formation))
            {
                return;
            }
            ArrangementOrder.ArrangementOrderEnum current = formation.ArrangementOrder.OrderEnum;
            if (current == ArrangementOrder.ArrangementOrderEnum.ShieldWall || current == ArrangementOrder.ArrangementOrderEnum.Loose)
            {
                return;
            }
            formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
        }

        // Advance phase = a forward behavior is weighted. The split tactics' deliberate opening waits weight only
        // Charge + Regroup, and the attack phase (FixCharge) zeroes Regroup, so neither is touched.
        private static bool IsAttackerAdvancing(Formation formation)
        {
            Mission mission = Mission.Current;
            if (mission == null || !mission.IsFieldBattle || formation.Team == null || !formation.Team.IsAttacker)
            {
                return false;
            }
            BehaviorAdvance advance = formation.AI.GetBehavior<BehaviorAdvance>();
            BehaviorCautiousAdvance cautious = formation.AI.GetBehavior<BehaviorCautiousAdvance>();
            return (advance != null && advance.WeightFactor > 0f) || (cautious != null && cautious.WeightFactor > 0f);
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetAiWeight")]
        private static bool PrefixGetAiWeight(ref BehaviorRegroup __instance, ref float __result)
        {
            if (__instance.Formation.AI != null &&
                __instance.Formation.AI.ActiveBehavior != null &&
                (__instance.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorHoldHighGround) || __instance.Formation.AI.ActiveBehavior.GetType() == typeof(BehaviorDefend)))
            {
                __result = 0f;
                return false;
            }
            if (__instance.Formation != null)
            {
                FormationQuerySystem querySystem = __instance.Formation.QuerySystem;
                if (__instance.Formation.AI.ActiveBehavior == null || querySystem.IsRangedFormation)
                {
                    __result = 0f;
                    return false;
                }
                // Rally: a chunk of the formation is far from the main body (reinforcement wave + surviving
                // veterans). Native's deviation-based weight excludes exactly those men; force Regroup to win.
                if (RallyLogic.NeedsRally(__instance.Formation))
                {
                    __result = RallyLogic.RallyWeight;
                    return false;
                }
                if (IsAttackerAdvancing(__instance.Formation))
                {
                    BehaviorComponent active = __instance.Formation.AI.ActiveBehavior;
                    float activeFor = Mission.Current.CurrentTime - (active.PreserveExpireTime - NativePreserveSeconds);
                    bool regroupActive = active == __instance;
                    if (regroupActive ? activeFor >= AttackerRegroupMaxSeconds : activeFor < AttackerAdvanceMinSeconds)
                    {
                        __result = 0f;
                        return false;
                    }
                }
                float coherence = __instance.Formation.AI.ActiveBehavior?.BehaviorCoherence ?? __instance.BehaviorCoherence;
                __result = MBMath.Lerp(0.1f, 1.2f, MBMath.ClampFloat(coherence * (querySystem.Formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents + 1f) / (querySystem.IdealAverageDisplacement + 1f), 0f, 3f) / 3f);
                // For a while after a reinforcement wave lands, lean toward closing up before pressing on.
                if (RallyLogic.RecentlyReinforced(__instance.Formation))
                {
                    __result *= RallyLogic.ReinforcedWeightScale;
                }
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("CalculateCurrentOrder")]
        private static bool PrefixCalculateCurrentOrder(ref BehaviorRegroup __instance, ref MovementOrder ____currentOrder, ref FacingOrder ___CurrentFacingOrder)
        {
            // Runs for player-led formations too (see OverrideBehaviorCharge) -- don't force Line onto them.
            if (__instance.Formation != null && !__instance.Formation.IsAIControlled)
            {
                return true;
            }
            if (__instance.Formation != null && __instance.Formation.QuerySystem.IsInfantryFormation && __instance.Formation.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
            {
                // Only while Regroup is the active behaviour: FindBestBehavior also runs this on a mere candidate,
                // and the flip to Line and back re-applied behaviour values to every agent (see BehaviorChargePatch).
                // Once active, the TickOccasionally postfix below sets Line every tick anyway.
                if (__instance.Formation.AI != null && __instance.Formation.AI.ActiveBehavior == __instance)
                {
                    ApplyRegroupArrangement(__instance.Formation);
                }
                Formation significantEnemy = RBMAI.Utilities.FindSignificantEnemy(__instance.Formation, true, true, false, false, false, true);
                if (significantEnemy != null)
                {
                    WorldPosition medianPosition;
                    Vec2 mainBody = RallyLogic.IsRallying(__instance.Formation) ? RallyLogic.MainBodyCenter(__instance.Formation) : Vec2.Invalid;
                    if (mainBody.IsValid)
                    {
                        // Hold at the rally anchor -- the wave's spawn point after reinforcements, else the MAIN
                        // BODY -- not the average (which the far men drag toward the enemy).
                        medianPosition = RBMAI.Utilities.GetFormationCenterWorldPosition(__instance.Formation);
                        medianPosition.SetVec2(mainBody);
                    }
                    else
                    {
                        // Hold at the front-centre fixed when the regroup began (RegroupHold), not the own centre.
                        medianPosition = RegroupHoldPosition(__instance);
                    }
                    ____currentOrder = MovementOrder.MovementOrderMove(medianPosition);

                    Vec2 direction = (RBMAI.Utilities.GetFormationCenter(significantEnemy) - RBMAI.Utilities.GetFormationCenter(__instance.Formation)).Normalized();
                    direction = HoldFacing.Direction(__instance.Formation, significantEnemy, direction);
                    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(direction);

                    return false;
                }
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("TickOccasionally")]
        private static void PrefixTickOccasionally(ref BehaviorRegroup __instance)
        {
            ApplyRegroupArrangement(__instance.Formation);
            if (RallyLogic.NeedsRally(__instance.Formation))
            {
                // Give each far man the nearest cell so he runs straight at the body, not a flank diagonal.
                RallyLogic.PullFarMenToNearestCells(__instance.Formation);
            }
        }
    }
}
