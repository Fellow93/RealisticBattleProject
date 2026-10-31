using RBMConfig;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static partial class Utilities
    {

        public static Agent GetCorrectTarget(Agent agent)
        {
            List<Formation> formations;
            if (agent != null)
            {
                Formation formation = agent.Formation;
                if (formation != null)
                {
                    MovementOrder movementOrder = formation.GetReadonlyMovementOrderReference();
                    // *ReadOnly class flags: this runs on the parallel movement job (see FrontlinePositioning).
                    if ((formation.QuerySystem.IsInfantryFormationReadOnly || formation.QuerySystem.IsRangedFormationReadOnly) && (movementOrder.OrderType == OrderType.ChargeWithTarget))
                    {
                        formations = RBMAI.Utilities.FindSignificantFormationsCached(formation);
                        // The formation picked its target by threat (riders included): the men follow it.
                        Formation focus = movementOrder.TargetFormation;
                        if (focus != null && focus.CountOfUnits > 0)
                        {
                            Agent focused = RBMAI.Utilities.NearestAgentFocused(agent.Position.AsVec2, formations, focus);
                            if (focused != null)
                            {
                                return focused;
                            }
                        }
                        Formation priorityFormation = null;
                        if (movementOrder.OrderType == OrderType.ChargeWithTarget && movementOrder.TargetFormation != null && !formations.Contains(movementOrder.TargetFormation))
                        {
                            priorityFormation = movementOrder.TargetFormation;
                        }
                        if (formations.Count > 0 || priorityFormation != null)
                        {
                            return RBMAI.Utilities.NearestAgentFromMultipleFormations(agent.Position.AsVec2, formations, priorityFormation);
                        }
                    }
                    if (formation.QuerySystem.IsCavalryFormationReadOnly && movementOrder.OrderType == OrderType.ChargeWithTarget)
                    {
                        // A targeted cavalry charge rides at its target formation only (the charge behavior measures contact against it).
                        Formation chargeTarget = movementOrder.TargetFormation;
                        if (chargeTarget != null && chargeTarget.CountOfUnits > 0)
                        {
                            Agent inTarget = RBMAI.Utilities.NearestAgentFromFormation(agent.Position.AsVec2, chargeTarget, skipRouting: true);
                            if (inTarget != null)
                            {
                                return inTarget;
                            }
                        }
                        formations = RBMAI.Utilities.FindSignificantFormationsCached(formation);
                        Formation priorityFormation = null;
                        if (movementOrder.OrderType == OrderType.ChargeWithTarget && movementOrder.TargetFormation != null && !formations.Contains(movementOrder.TargetFormation))
                        {
                            priorityFormation = movementOrder.TargetFormation;
                        }
                        if (formations.Count > 0 || priorityFormation != null)
                        {
                            return RBMAI.Utilities.NearestAgentFromMultipleFormations(agent.Position.AsVec2, formations, priorityFormation);
                        }
                    }
                }
            }
            return null;
        }

        // One formation's units as of one frame, shared by every caller (per-charging-agent scans from the parallel movement job, and the
        // cavalry free-charge gate, which accepts one up to SnapshotMaxAge old). Built at most once per formation per frame and never
        // mutated after publishing (a racing double build is harmless). Cleared in MissionStartReset.
        internal sealed class FormationUnitSnapshot
        {
            public readonly float Time;
            // Roster order of ApplyActionOnEachUnitViaBackupList (attached, then detached) so distance ties resolve the same.
            public readonly Agent[] Agents;
            // Agent.Position is a direct pointer read; GetWorldPosition is an interop call (resolves the navmesh face). Only X/Y are compared.
            public readonly Vec2[] Positions;
            // Who NearestAgentFromMultipleFormations may pick: a player-controlled unit always, an AI one unless routing.
            public readonly bool[] Selectable;
            // Bounds of Positions; meaningless when Agents is empty.
            public readonly float MinX;
            public readonly float MinY;
            public readonly float MaxX;
            public readonly float MaxY;
            // FormationGeometry.RobustBox in the formation's facing frame (outliers, routing and far detached men left out); RobustCount 0 = none.
            public readonly int RobustCount;
            public readonly Vec2 RobustCentre;
            public readonly Vec2 RobustDir;
            public readonly float RobustHalfAlong;
            public readonly float RobustHalfAcross;
            public readonly Vec2 RobustBulk; // the kept men's mean per axis

            public FormationUnitSnapshot(float time, Agent[] agents, int attachedCount = -1, Vec2 frameDir = default(Vec2))
            {
                Time = time;
                Agents = agents;
                Positions = new Vec2[agents.Length];
                Selectable = new bool[agents.Length];
                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;
                for (int i = 0; i < agents.Length; i++)
                {
                    Agent agent = agents[i];
                    Vec2 position = agent.Position.AsVec2;
                    Positions[i] = position;
                    Selectable[i] = !agent.IsAIControlled || !agent.IsRunningAway;
                    minX = Math.Min(minX, position.x);
                    minY = Math.Min(minY, position.y);
                    maxX = Math.Max(maxX, position.x);
                    maxY = Math.Max(maxY, position.y);
                }
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
                FormationGeometry.RobustBox(Positions, Selectable, attachedCount, frameDir, out RobustCentre, out RobustDir, out RobustHalfAlong,
                    out RobustHalfAcross, out RobustCount, out RobustBulk);
            }

            // Squared distance from a point to the bounds; 0 inside them. Only valid when Agents is not empty.
            public float DistanceSquaredToBounds(Vec2 point)
            {
                float dx = point.x < MinX ? MinX - point.x : (point.x > MaxX ? point.x - MaxX : 0f);
                float dy = point.y < MinY ? MinY - point.y : (point.y > MaxY ? point.y - MaxY : 0f);
                return dx * dx + dy * dy;
            }
        }

        internal static readonly ConcurrentDictionary<Formation, FormationUnitSnapshot> formationUnitSnapshots = new ConcurrentDictionary<Formation, FormationUnitSnapshot>();

        // Max age of a snapshot the cavalry free-charge gate (bounds only) accepts; target pickers stay on this frame's (they read Selectable).
        internal const float SnapshotMaxAge = 0.1f;

        // The snapshot is shared: read only. A unit can die after it was taken, so pickers re-check IsActive.
        internal static FormationUnitSnapshot GetFormationUnitSnapshot(Formation formation)
        {
            return GetFormationUnitSnapshot(formation, 0f);
        }

        // maxAge > 0 also accepts an earlier-frame snapshot younger than maxAge, unless the unit count changed.
        internal static FormationUnitSnapshot GetFormationUnitSnapshot(Formation formation, float maxAge)
        {
            Mission mission = Mission.Current;
            float now = mission != null ? mission.CurrentTime : 0f;
            if (mission != null && formationUnitSnapshots.TryGetValue(formation, out FormationUnitSnapshot entry))
            {
                if (entry.Time == now)
                {
                    return entry;
                }
                if (maxAge > 0f && entry.Time < now && now - entry.Time < maxAge && entry.Agents.Length == formation.CountOfUnits)
                {
                    return entry;
                }
            }
            // Same content/order as ApplyActionOnEachUnitViaBackupList; each live list is copied with ToArray first (movement
            // workers: indexing a live list another thread shrinks could throw).
            IFormationUnit[] attachedUnits = formation.Arrangement.GetAllUnits().ToArray();
            Agent[] detachedUnits = formation.DetachedUnits.ToArray();
            Agent[] units = new Agent[attachedUnits.Length + detachedUnits.Length];
            for (int i = 0; i < attachedUnits.Length; i++)
            {
                units[i] = (Agent)attachedUnits[i];
            }
            Array.Copy(detachedUnits, 0, units, attachedUnits.Length, detachedUnits.Length);
            FormationUnitSnapshot snapshot = new FormationUnitSnapshot(now, units, attachedUnits.Length, formation.Direction);
            if (mission != null)
            {
                formationUnitSnapshots[formation] = snapshot;
            }
            return snapshot;
        }

        public static Agent NearestAgentFromFormation(Vec2 unitPosition, Formation targetFormation, bool skipRouting = false)
        {
            if (targetFormation == null)
            {
                return null;
            }
            Agent targetAgent = null;
            float distance = 10000f;
            FormationUnitSnapshot snapshot = GetFormationUnitSnapshot(targetFormation);
            for (int i = 0; i < snapshot.Agents.Length; i++)
            {
                if (skipRouting && !snapshot.Selectable[i])
                {
                    continue;
                }
                float newDist = unitPosition.Distance(snapshot.Positions[i]);
                if (newDist < distance && snapshot.Agents[i].IsActive())
                {
                    targetAgent = snapshot.Agents[i];
                    distance = newDist;
                }
            }
            return targetAgent;
        }

        // m; a formation whose bounds lie this far beyond the best so far is skipped (margin covers float rounding).
        private const float NearestBoundsSkipMargin = 0.01f;

        // `formations` may be the shared cached list: read only.
        public static Agent NearestAgentFromMultipleFormations(Vec2 unitPosition, List<Formation> formations, Formation priorityFormation = null)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            for (int f = 0; f < formations.Count; f++)
            {
                FormationUnitSnapshot snapshot = GetFormationUnitSnapshot(formations[f]);
                if (snapshot.Agents.Length > 0
                    && MathF.Sqrt(snapshot.DistanceSquaredToBounds(unitPosition)) >= distance + NearestBoundsSkipMargin)
                {
                    continue;
                }
                for (int i = 0; i < snapshot.Agents.Length; i++)
                {
                    if (!snapshot.Selectable[i])
                    {
                        continue;
                    }
                    float newDist = unitPosition.Distance(snapshot.Positions[i]);
                    if (newDist < distance && snapshot.Agents[i].IsActive())
                    {
                        targetAgent = snapshot.Agents[i];
                        distance = newDist;
                    }
                }
            }
            if (priorityFormation != null && distance > 30f)
            {
                distance = 10000f;
                FormationUnitSnapshot snapshot = GetFormationUnitSnapshot(priorityFormation);
                for (int i = 0; i < snapshot.Agents.Length; i++)
                {
                    float newDist = unitPosition.Distance(snapshot.Positions[i]);
                    if (newDist < distance && snapshot.Agents[i].IsActive())
                    {
                        targetAgent = snapshot.Agents[i];
                        distance = newDist;
                    }
                }
            }
            return targetAgent;
        }

        // A foot charge stays on its target formation: a man of another formation is taken only when he is this close, or this much nearer.
        private const float TargetFocusCloseRange = 4f; // m
        private const float TargetFocusSlack = 8f; // m

        // `formations` may be the shared cached list: read only. Null when the focus has nobody selectable.
        public static Agent NearestAgentFocused(Vec2 unitPosition, List<Formation> formations, Formation focus)
        {
            Agent focused = null;
            float focusDistance = float.MaxValue;
            FormationUnitSnapshot focusSnapshot = GetFormationUnitSnapshot(focus);
            for (int i = 0; i < focusSnapshot.Agents.Length; i++)
            {
                if (!focusSnapshot.Selectable[i])
                {
                    continue;
                }
                float newDist = unitPosition.Distance(focusSnapshot.Positions[i]);
                if (newDist < focusDistance && focusSnapshot.Agents[i].IsActive())
                {
                    focused = focusSnapshot.Agents[i];
                    focusDistance = newDist;
                }
            }
            if (focused == null)
            {
                return null;
            }
            float limit = MathF.Max(TargetFocusCloseRange, focusDistance - TargetFocusSlack);
            Agent other = null;
            for (int f = 0; f < formations.Count; f++)
            {
                if (formations[f] == focus)
                {
                    continue;
                }
                FormationUnitSnapshot snapshot = GetFormationUnitSnapshot(formations[f]);
                if (snapshot.Agents.Length == 0 || MathF.Sqrt(snapshot.DistanceSquaredToBounds(unitPosition)) >= limit)
                {
                    continue;
                }
                for (int i = 0; i < snapshot.Agents.Length; i++)
                {
                    if (!snapshot.Selectable[i])
                    {
                        continue;
                    }
                    float newDist = unitPosition.Distance(snapshot.Positions[i]);
                    if (newDist < limit && snapshot.Agents[i].IsActive())
                    {
                        other = snapshot.Agents[i];
                        limit = newDist;
                    }
                }
            }
            return other ?? focused;
        }

        public static Agent NearestEnemyAgent(Agent unit)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            Vec2 unitPosition = unit.Position.AsVec2;
            foreach (Team team in Mission.Current.Teams.ToList())
            {
                if (team.IsEnemyOf(unit.Formation.Team))
                {
                    foreach (Formation enemyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                    {
                        enemyFormation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                        {
                            float newDist = unitPosition.Distance(agent.Position.AsVec2);
                            if (newDist < distance)
                            {
                                targetAgent = agent;
                                distance = newDist;
                            }
                        });
                    }
                }
            }
            return targetAgent;
        }
    }
}
