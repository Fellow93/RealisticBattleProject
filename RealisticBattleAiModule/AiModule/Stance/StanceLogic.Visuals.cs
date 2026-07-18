using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static RBMAI.PostureDamage;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMAI
{
    public partial class StanceLogic : MissionLogic
    {
        private static void addPosturedamageVisual(Agent attackerAgent, Agent victimAgent)
        {
            if (RBMConfig.RBMConfig.postureEnabled)
            {
                if (victimAgent != null && attackerAgent != null && (victimAgent.IsPlayerControlled || attackerAgent.IsPlayerControlled))
                {
                    Agent enemyAgent = null;
                    if (victimAgent.IsPlayerControlled)
                    {
                        enemyAgent = attackerAgent;
                        Stance stance = null;
                        if (AgentStances.values.TryGetValue(victimAgent, out stance))
                        {
                            if (AgentStances.postureVisual != null && AgentStances.postureVisual._dataSource.ShowPlayerPostureStatus)
                            {
                                AgentStances.postureVisual._dataSource.PlayerPosture = (int)stance.posture;
                                AgentStances.postureVisual._dataSource.PlayerPostureMax = (int)stance.maxPosture;
                                AgentStances.postureVisual._dataSource.PlayerPostureText = ((int)stance.posture).ToString();
                                AgentStances.postureVisual._dataSource.PlayerPostureMaxText = ((int)stance.maxPosture).ToString();

                                AgentStances.postureVisual._dataSource.PlayerStamina = (int)stance.stamina;
                                AgentStances.postureVisual._dataSource.PlayerStaminaMax = (int)stance.maxStamina;
                                AgentStances.postureVisual._dataSource.PlayerStaminaText = ((int)stance.stamina).ToString();
                                AgentStances.postureVisual._dataSource.PlayerStaminaMaxText = ((int)stance.maxStamina).ToString();
                            }
                        }
                    }
                    else
                    {
                        enemyAgent = victimAgent;
                        Stance stance = null;
                        if (AgentStances.values.TryGetValue(attackerAgent, out stance))
                        {
                            if (AgentStances.postureVisual != null && AgentStances.postureVisual._dataSource.ShowPlayerPostureStatus)
                            {
                                AgentStances.postureVisual._dataSource.PlayerPosture = (int)stance.posture;
                                AgentStances.postureVisual._dataSource.PlayerPostureMax = (int)stance.maxPosture;
                                AgentStances.postureVisual._dataSource.PlayerPostureText = ((int)stance.posture).ToString();
                                AgentStances.postureVisual._dataSource.PlayerPostureMaxText = ((int)stance.maxPosture).ToString();

                                AgentStances.postureVisual._dataSource.PlayerStamina = (int)stance.stamina;
                                AgentStances.postureVisual._dataSource.PlayerStaminaMax = (int)stance.maxStamina;
                                AgentStances.postureVisual._dataSource.PlayerStaminaText = ((int)stance.stamina).ToString();
                                AgentStances.postureVisual._dataSource.PlayerStaminaMaxText = ((int)stance.maxStamina).ToString();
                            }
                        }
                    }
                    if (AgentStances.postureVisual != null)
                    {
                        Stance stance = null;
                        if (AgentStances.values.TryGetValue(enemyAgent, out stance))
                        {
                            AgentStances.postureVisual._dataSource.ShowEnemyStatus = true;
                            AgentStances.postureVisual.affectedAgent = enemyAgent;
                            if (AgentStances.postureVisual._dataSource.ShowEnemyStatus && AgentStances.postureVisual.affectedAgent == enemyAgent)
                            {
                                AgentStances.postureVisual.timer = AgentStances.postureVisual.DisplayTime;
                                AgentStances.postureVisual._dataSource.EnemyPosture = (int)stance.posture;
                                AgentStances.postureVisual._dataSource.EnemyPostureMax = (int)stance.maxPosture;
                                AgentStances.postureVisual._dataSource.EnemyStamina = (int)stance.stamina;
                                AgentStances.postureVisual._dataSource.EnemyStaminaMax = (int)stance.maxStamina;
                                AgentStances.postureVisual._dataSource.EnemyHealth = (int)enemyAgent.Health;
                                AgentStances.postureVisual._dataSource.EnemyHealthMax = (int)enemyAgent.HealthLimit;
                                if (enemyAgent.IsMount)
                                {
                                    AgentStances.postureVisual._dataSource.EnemyName = enemyAgent.RiderAgent?.Name + " (" + new TextObject("{=mountnoun}Mount").ToString() + ")";
                                }
                                else
                                {
                                    AgentStances.postureVisual._dataSource.EnemyName = enemyAgent.Name;
                                }
                            }
                        }
                    }
                }
            }
        }

        public static ActionIndexCache DecideAnimation(AttackCollisionData collisionData, bool isAttacker)
        {
            switch (collisionData.AttackDirection)
            {
                case UsageDirection.AttackLeft:
                    {
                        if (isAttacker)
                        {
                            return ActionIndexCache.act_stagger_left;
                        }
                        else
                        {
                            return ActionIndexCache.act_stagger_right;
                        }
                    }
                case UsageDirection.AttackRight:
                    {
                        if (isAttacker)
                        {
                            return ActionIndexCache.act_stagger_right;
                        }
                        else
                        {
                            return ActionIndexCache.act_stagger_left;
                        }
                    }
                case UsageDirection.AttackUp:
                case UsageDirection.AttackDown:
                    {
                        if (isAttacker)
                        {
                            return ActionIndexCache.act_stagger_forward;
                        }
                        else
                        {
                            return ActionIndexCache.act_stagger_backward;
                        }
                    }
                default:
                    {
                        return ActionIndexCache.act_stagger_left;
                    }
            }
        }

        public static void forceStaggerAnimation(Agent agent, AttackCollisionData collisionData, float actionSpeed, bool isAttacker)
        {
            agent.SetActionChannel(agent.HasMount ? 1 : 0, DecideAnimation(collisionData, isAttacker), actionSpeed: actionSpeed);
        }

        public static void forceTiredAnimation(Agent agent, AttackCollisionData collisionData, float actionSpeed, bool isAttacker)
        {
            agent.SetActionChannel(agent.HasMount ? 1 : 0, ActionIndexCache.act_pickup_down_begin_left_stance, actionSpeed: actionSpeed);
        }
    }
}
