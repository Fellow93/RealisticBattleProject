using RBMConfig;
using System;
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
                    if ((formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && (movementOrder.OrderType == OrderType.ChargeWithTarget))
                    {
                        formations = RBMAI.Utilities.FindSignificantFormations(formation);
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
                    if (formation.QuerySystem.IsCavalryFormation && movementOrder.OrderType == OrderType.ChargeWithTarget)
                    {
                        formations = RBMAI.Utilities.FindSignificantFormations(formation);
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

        public static Agent NearestAgentFromFormation(Vec2 unitPosition, Formation targetFormation)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            targetFormation?.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
            {
                float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                if (newDist < distance)
                {
                    targetAgent = agent;
                    distance = newDist;
                }
            });
            return targetAgent;
        }

        public static Agent NearestAgentFromMultipleFormations(Vec2 unitPosition, List<Formation> formations, Formation priorityFormation = null)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            foreach (Formation formation in formations.ToList())
            {
                formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    if (agent.IsAIControlled)
                    {
                        if (!agent.IsRunningAway)
                        {
                            float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                            if (newDist < distance)
                            {
                                targetAgent = agent;
                                distance = newDist;
                            }
                        }
                    }
                    else
                    {
                        float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                        if (newDist < distance)
                        {
                            targetAgent = agent;
                            distance = newDist;
                        }
                    }
                });
            }
            if (priorityFormation != null && distance > 30f)
            {
                distance = 10000f;
                priorityFormation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                    if (newDist < distance)
                    {
                        targetAgent = agent;
                        distance = newDist;
                    }
                });
            }
            return targetAgent;
        }

        public static Agent NearestEnemyAgent(Agent unit)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            Vec2 unitPosition = unit.GetWorldPosition().AsVec2;
            foreach (Team team in Mission.Current.Teams.ToList())
            {
                if (team.IsEnemyOf(unit.Formation.Team))
                {
                    foreach (Formation enemyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                    {
                        enemyFormation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                        {
                            float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
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
