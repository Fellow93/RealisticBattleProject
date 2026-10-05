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
                        // A targeted cavalry charge rides at its target formation only. Picking the nearest man
                        // across every significant formation (the target is normally one of them, so it got no
                        // priority) sent riders at whatever stood closest -- skirmishers in front, the next block
                        // over -- while the charge behavior measured contact and pass-through against its target.
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

        // One formation's units as of one frame, shared by every caller in that frame. The nearest-agent scans below
        // run per charging agent (GetCorrectTarget, every 0.5-1 s each, from the parallel movement job) and each used
        // to walk every enemy formation's roster with a fresh ToArray; the cavalry free-charge gate in
        // FrontlinePositioning reads the bounds every frame. Same scheme as SignificantFormationsEntry: built once
        // per formation per frame and never mutated after it is published, so workers only ever read a finished
        // one; two workers racing on the same frame both build it, which is harmless. Cleared in MissionStartReset.
        internal sealed class FormationUnitSnapshot
        {
            public readonly float Time;
            // Roster order of ApplyActionOnEachUnitViaBackupList (attached, then detached), so a distance tie
            // resolves to the same agent the old per-call walk picked.
            public readonly Agent[] Agents;
            // Agent.Position is a direct read of the engine's position pointer; GetWorldPosition is an interop
            // call (it also resolves the navmesh face). Only X/Y are compared, and they are the same.
            public readonly Vec2[] Positions;
            // Who NearestAgentFromMultipleFormations may pick: a player-controlled unit always, an AI one unless routing.
            public readonly bool[] Selectable;
            // Bounds of Positions; meaningless when Agents is empty.
            public readonly float MinX;
            public readonly float MinY;
            public readonly float MaxX;
            public readonly float MaxY;

            public FormationUnitSnapshot(float time, Agent[] agents)
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

        // The returned snapshot is shared between callers: read it, never modify it. A unit can die after the snapshot
        // was taken in the same frame, so pickers re-check IsActive (the live roster they replace never held the dead).
        internal static FormationUnitSnapshot GetFormationUnitSnapshot(Formation formation)
        {
            Mission mission = Mission.Current;
            float now = mission != null ? mission.CurrentTime : 0f;
            if (mission != null && formationUnitSnapshots.TryGetValue(formation, out FormationUnitSnapshot entry) && entry.Time == now)
            {
                return entry;
            }
            List<Agent> units = new List<Agent>(formation.CountOfUnits);
            formation.ApplyActionOnEachUnitViaBackupList(units.Add);
            FormationUnitSnapshot snapshot = new FormationUnitSnapshot(now, units.ToArray());
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

        // `formations` may be the shared FindSignificantFormationsCached list: it is only read here.
        public static Agent NearestAgentFromMultipleFormations(Vec2 unitPosition, List<Formation> formations, Formation priorityFormation = null)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            for (int f = 0; f < formations.Count; f++)
            {
                FormationUnitSnapshot snapshot = GetFormationUnitSnapshot(formations[f]);
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
