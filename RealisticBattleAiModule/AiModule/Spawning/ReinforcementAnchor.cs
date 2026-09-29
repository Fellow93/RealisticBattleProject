using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI.AiModule
{
    /// <summary>
    /// Field battles: reinforcements enter from the map border behind where their side is standing NOW.
    ///
    /// Vanilla builds the reinforcement plan once at deployment, on a scene spawn path, and never moves it (it only
    /// swaps paths when an enemy is within 50 m). When a side moves the fight -- a defender taking a hill off to one
    /// side -- reinforcements keep entering at the old spot, which can be beside or in front of the army.
    ///
    /// At the start of each wave we look from the enemy's centre through our side's centre, walk that ray to the
    /// mission boundary and step back in a little. A few rays rotated off that line are tried too, and the first one
    /// whose anchor keeps clear of enemy formations wins. Vanilla's per-formation layout (infantry centre, archers,
    /// cavalry flanks) is kept by re-expressing each formation frame relative to the plan's mean, then placing it
    /// around the new anchor facing the enemy. Vanilla still spreads each man into his own slot around that frame.
    ///
    /// The anchor is cached per team and reused while the wave keeps spawning (one man per tick), so a wave arrives as
    /// one block. Any failure keeps vanilla's frame. Uses only cached formation data plus navmesh/boundary queries of
    /// the kind vanilla already makes on this path -- never the proximity map (see agent-build deadlock).
    /// </summary>
    internal static class ReinforcementAnchor
    {
        private const float BorderInsetMeters = 40f;
        private const float StepMeters = 5f;
        private const float MaxSearchMeters = 2000f;
        private const float SafeEnemyDistance = 100f;
        private const float WaveGapSeconds = 3f;
        /// <summary>How far in front of a wave's spawn frame (toward the enemy) RallyLogic gathers the formation.</summary>
        private const float RallyForwardMeters = 200f;
        /// <summary>The gathering point keeps at least this far from every enemy formation.</summary>
        private const float RallyEnemyClearance = 200f;
        private static readonly float[] CandidateAngles = { 0f, 0.44f, -0.44f, 0.87f, -0.87f };

        private class Anchor
        {
            public Vec2 Position;
            public Vec2 Direction;
            public float LastUsed;
            public readonly Dictionary<FormationClass, (WorldPosition, Vec2)> Frames = new Dictionary<FormationClass, (WorldPosition, Vec2)>();
        }

        private static Mission _mission;
        private static readonly Dictionary<Team, Anchor> _anchors = new Dictionary<Team, Anchor>();

        /// <summary>
        /// Where each (team, formation class)'s latest wave should gather, and when the wave was placed: a little
        /// in front of its spawn frame, toward the battle. RallyLogic holds a freshly reinforced formation there.
        /// Written from the spawn logic's mission tick, read from the formation AI tick, so it is never mutated:
        /// every update swaps in a new map and readers take the reference once.
        /// </summary>
        private static Dictionary<(Team, FormationClass), (Vec2 Position, float Time)> _spawnPoints =
            new Dictionary<(Team, FormationClass), (Vec2, float)>();

        /// <summary>The gathering point of this formation class's latest wave, if the wave is at most <paramref name="maxAge"/> seconds old.</summary>
        internal static bool TryGetRecentRallyPoint(Team team, FormationClass formationClass, float maxAge, out Vec2 position)
        {
            position = Vec2.Invalid;
            Mission mission = Mission.Current;
            if (team == null || mission == null || mission != _mission)
            {
                return false;
            }
            Dictionary<(Team, FormationClass), (Vec2 Position, float Time)> points = _spawnPoints;
            if (!points.TryGetValue((team, formationClass), out (Vec2 Position, float Time) point)
                || mission.CurrentTime - point.Time > maxAge || !point.Position.IsValid)
            {
                return false;
            }
            position = point.Position;
            return true;
        }

        /// <summary>Called from MissionStartReset: drops the last mission's teams.</summary>
        internal static void Reset()
        {
            _mission = null;
            _anchors.Clear();
            _spawnPoints = new Dictionary<(Team, FormationClass), (Vec2, float)>();
        }

        private static void PublishSpawnPoint(Team team, FormationClass formationClass, Vec2 position, float time)
        {
            Dictionary<(Team, FormationClass), (Vec2 Position, float Time)> next =
                new Dictionary<(Team, FormationClass), (Vec2, float)>(_spawnPoints);
            next[(team, formationClass)] = (position, time);
            _spawnPoints = next;
        }

        [HarmonyPatch(typeof(Mission), nameof(Mission.GetFormationSpawnFrame))]
        private static class GetFormationSpawnFramePatch
        {
            private static void Postfix(Mission __instance, Team team, FormationClass formationClass, bool isReinforcement, ref WorldPosition spawnPosition, ref Vec2 spawnDirection)
            {
                if (!isReinforcement || team == null || __instance.MissionTeamAIType != Mission.MissionTeamAITypeEnum.FieldBattle
                    || __instance.IsSiegeBattle || __instance.IsSallyOutBattle || __instance.IsNavalBattle)
                {
                    return;
                }
                try
                {
                    if (_mission != __instance)
                    {
                        _mission = __instance;
                        _anchors.Clear();
                        _spawnPoints = new Dictionary<(Team, FormationClass), (Vec2, float)>();
                    }
                    float now = __instance.CurrentTime;
                    if (!_anchors.TryGetValue(team, out Anchor anchor) || now - anchor.LastUsed > WaveGapSeconds)
                    {
                        anchor = ComputeAnchor(__instance, team);
                        if (anchor == null)
                        {
                            _anchors.Remove(team);
                            return;
                        }
                        _anchors[team] = anchor;
                    }
                    anchor.LastUsed = now;

                    if (!anchor.Frames.TryGetValue(formationClass, out (WorldPosition pos, Vec2 dir) frame))
                    {
                        frame = PlaceFormation(__instance, team, anchor, spawnPosition.AsVec2, spawnDirection);
                        anchor.Frames[formationClass] = frame;
                        if (frame.pos.IsValid)
                        {
                            PublishSpawnPoint(team, formationClass, RallyPointInFront(__instance, team, frame.pos.AsVec2, frame.dir), now);
                        }
                    }
                    if (frame.pos.IsValid)
                    {
                        spawnPosition = frame.pos;
                        spawnDirection = frame.dir;
                    }
                }
                catch (Exception)
                {
                    // Keep vanilla's frame.
                }
            }
        }

        private static Anchor ComputeAnchor(Mission mission, Team team)
        {
            if (!TryGetSideCentre(mission, team, enemies: false, out Vec2 own) || !TryGetSideCentre(mission, team, enemies: true, out Vec2 enemy))
            {
                return null;
            }
            Vec2 away = own - enemy;
            if (away.LengthSquared < 1f || !mission.IsPositionInsideBoundaries(own))
            {
                return null;
            }
            away = away.Normalized();

            Vec2 bestPos = Vec2.Invalid;
            float bestClearance = float.MinValue;
            foreach (float angle in CandidateAngles)
            {
                Vec2 dir = Rotate(away, angle);
                float travelled = 0f;
                while (travelled + StepMeters < MaxSearchMeters && mission.IsPositionInsideBoundaries(own + dir * (travelled + StepMeters)))
                {
                    travelled += StepMeters;
                }
                // Walk back toward our own side until the ground is walkable and out of the water.
                float along = Math.Max(0f, travelled - Math.Min(BorderInsetMeters, travelled * 0.5f));
                Vec2 candidate = Vec2.Invalid;
                for (; along >= 0f; along -= StepMeters)
                {
                    if (TryGetDryGround(mission, own + dir * along, dir, out WorldPosition ground))
                    {
                        candidate = ground.AsVec2;
                        break;
                    }
                }
                if (!candidate.IsValid)
                {
                    continue;
                }
                float clearance = NearestEnemyFormationDistance(mission, team, candidate);
                if (clearance >= SafeEnemyDistance)
                {
                    bestPos = candidate;
                    break;
                }
                if (clearance > bestClearance)
                {
                    bestClearance = clearance;
                    bestPos = candidate;
                }
            }
            if (!bestPos.IsValid)
            {
                return null;
            }
            Vec2 facing = enemy - bestPos;
            if (facing.LengthSquared < 1f)
            {
                facing = -away;
            }
            return new Anchor { Position = bestPos, Direction = facing.Normalized() };
        }

        /// <summary>
        /// <see cref="RallyForwardMeters"/> in front of the spawn frame along its facing (toward the enemy), stepped
        /// back toward the frame until it is dry, walkable ground inside the boundary and at least
        /// <see cref="RallyEnemyClearance"/> from every enemy formation; the frame itself if none is.
        /// </summary>
        private static Vec2 RallyPointInFront(Mission mission, Team team, Vec2 spawn, Vec2 facing)
        {
            if (facing.LengthSquared < 1e-4f)
            {
                return spawn;
            }
            facing = facing.Normalized();
            for (float along = RallyForwardMeters; along > 0f; along -= StepMeters)
            {
                Vec2 point = spawn + facing * along;
                if (mission.IsPositionInsideBoundaries(point)
                    && NearestEnemyFormationDistance(mission, team, point) >= RallyEnemyClearance
                    && TryGetDryGround(mission, point, facing, out WorldPosition ground))
                {
                    return ground.AsVec2;
                }
            }
            return spawn;
        }

        /// <summary>Moves one formation's vanilla reinforcement frame from the plan's mean onto the anchor.</summary>
        private static (WorldPosition, Vec2) PlaceFormation(Mission mission, Team team, Anchor anchor, Vec2 vanillaPos, Vec2 vanillaDir)
        {
            Vec2 target = anchor.Position;
            if (vanillaDir.LengthSquared > 1e-4f && mission.GetDeploymentPlan(out DefaultMissionDeploymentPlan plan))
            {
                vanillaDir = vanillaDir.Normalized();
                Vec2 offset = vanillaPos - plan.GetMeanPosition(team, isReinforcement: true).AsVec2;
                float forward = offset.DotProduct(vanillaDir);
                float right = offset.DotProduct(vanillaDir.RightVec());
                // Plans are laid out for a full-width deployment; keep flanks from spilling far past the anchor.
                forward = MBMath.ClampFloat(forward, -60f, 60f);
                right = MBMath.ClampFloat(right, -120f, 120f);
                target = anchor.Position + anchor.Direction * forward + anchor.Direction.RightVec() * right;
                if (!mission.IsPositionInsideBoundaries(target))
                {
                    target = anchor.Position;
                }
            }
            // A flank that lands in water or off the navmesh falls back to the anchor, which is known dry.
            if (TryGetDryGround(mission, target, anchor.Direction, out WorldPosition position)
                || TryGetDryGround(mission, anchor.Position, anchor.Direction, out position))
            {
                return (position, anchor.Direction);
            }
            return (WorldPosition.Invalid, anchor.Direction);
        }

        /// <summary>A navmesh position at (or snapped near) <paramref name="point"/> whose ground is above the water.</summary>
        private static bool TryGetDryGround(Mission mission, Vec2 point, Vec2 direction, out WorldPosition position)
        {
            Scene scene = mission.Scene;
            position = new WorldPosition(scene, UIntPtr.Zero, new Vec3(point, scene.GetTerrainHeight(point)), hasValidZ: false);
            if (position.GetNavMesh() == UIntPtr.Zero)
            {
                float penalty = 0f;
                position = mission.GetAlternatePositionForNavmeshlessOrOutOfBoundsPosition(direction, position, ref penalty);
                if (position.GetNavMesh() == UIntPtr.Zero)
                {
                    return false;
                }
            }
            return IsDry(scene, position.GetGroundVec3());
        }

        /// <summary>Same test the engine's own water code uses: a point is submerged when it is below the water level there.</summary>
        private static bool IsDry(Scene scene, Vec3 ground)
        {
            return ground.z >= scene.GetWaterLevelAtPosition(ground.AsVec2, useWaterRenderer: true, checkWaterBodyEntities: true);
        }

        /// <summary>
        /// Per man: vanilla spreads each reinforcement into a slot around the formation frame, and a slot can still land
        /// in a stream or lake next to a dry frame. Walk such a man back toward the wave's anchor until he is on dry
        /// ground. Only runs for the teams whose anchor this wave is using.
        /// </summary>
        [HarmonyPatch(typeof(Mission), nameof(Mission.GetTroopSpawnFrameWithIndex))]
        private static class GetTroopSpawnFrameWithIndexPatch
        {
            private const float WalkStepMeters = 2f;

            private static void Postfix(Mission __instance, AgentBuildData buildData, ref Vec3 troopSpawnPosition, ref Vec2 troopSpawnDirection)
            {
                if (buildData == null || !buildData.AgentIsReinforcement || buildData.AgentTeam == null || !troopSpawnPosition.IsValid
                    || _mission != __instance || !_anchors.TryGetValue(buildData.AgentTeam, out Anchor anchor))
                {
                    return;
                }
                try
                {
                    Scene scene = __instance.Scene;
                    if (IsDry(scene, troopSpawnPosition))
                    {
                        return;
                    }
                    Vec2 from = troopSpawnPosition.AsVec2;
                    Vec2 toAnchor = anchor.Position - from;
                    float distance = toAnchor.Length;
                    if (distance < 0.01f)
                    {
                        return;
                    }
                    toAnchor *= 1f / distance;
                    for (float walked = WalkStepMeters; walked <= distance + WalkStepMeters; walked += WalkStepMeters)
                    {
                        Vec2 point = walked >= distance ? anchor.Position : from + toAnchor * walked;
                        if (TryGetDryGround(__instance, point, toAnchor, out WorldPosition ground))
                        {
                            troopSpawnPosition = ground.GetGroundVec3();
                            return;
                        }
                    }
                }
                catch (Exception)
                {
                    // Keep vanilla's slot.
                }
            }
        }

        private static bool TryGetSideCentre(Mission mission, Team team, bool enemies, out Vec2 centre)
        {
            Vec2 sum = Vec2.Zero;
            int count = 0;
            foreach (Team other in mission.Teams)
            {
                bool match = enemies ? other.IsEnemyOf(team) : other.Side == team.Side;
                if (!match)
                {
                    continue;
                }
                foreach (Formation formation in other.FormationsIncludingEmpty)
                {
                    int units = formation.CountOfUnits;
                    if (units > 0)
                    {
                        sum += formation.CachedAveragePosition * units;
                        count += units;
                    }
                }
            }
            centre = count > 0 ? sum * (1f / count) : Vec2.Invalid;
            return count > 0;
        }

        private static float NearestEnemyFormationDistance(Mission mission, Team team, Vec2 position)
        {
            float nearest = float.MaxValue;
            foreach (Team other in mission.Teams)
            {
                if (!other.IsEnemyOf(team))
                {
                    continue;
                }
                foreach (Formation formation in other.FormationsIncludingEmpty)
                {
                    if (formation.CountOfUnits > 0)
                    {
                        nearest = Math.Min(nearest, formation.CachedAveragePosition.Distance(position));
                    }
                }
            }
            return nearest;
        }

        private static Vec2 Rotate(Vec2 v, float radians)
        {
            float c = (float)Math.Cos(radians);
            float s = (float)Math.Sin(radians);
            return new Vec2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
