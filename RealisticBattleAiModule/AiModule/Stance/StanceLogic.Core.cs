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
        private static float timeToCalc = 0.5f;
        private static float timeToCalcStaminaHealth = 10f;
        private static float timeToUpdateAgents = 3f;

        private static float currentDt = 0f;
        private static float currentDtToUpdateAgents = 0f;
        private static float currentDtToUpdateStaminaHealth = 0f;

        public static MBArrayList<Agent> agentsToDropShield = new MBArrayList<Agent> { };
        public static MBArrayList<Agent> agentsToDropWeapon = new MBArrayList<Agent> { };
        public static Dictionary<Agent, FormationClass> agentsToChangeFormation = new Dictionary<Agent, FormationClass> { };

        private readonly MBArrayList<Agent> _inactiveAgentsBuffer = new MBArrayList<Agent>();
        private readonly MBArrayList<Agent> _dropShieldBuffer = new MBArrayList<Agent>();
        private readonly MBArrayList<Agent> _dropWeaponBuffer = new MBArrayList<Agent>();

        public static void TryToDropShield(Agent victimAgent)
        {
            if (!agentsToDropShield.Contains(victimAgent))
            {
                agentsToDropShield.Add(victimAgent);
            }
        }

        //how much posture is regained after posture break
        private static float postureResetModifier = 0.75f;

        //how much posture is regained after posture break while holding shield
        private static float shieldPostureResetModifier = 0.4f;

        public static void ResetPostureForAgent(ref Stance stance, float resetModifier)
        {
            if (stance != null)
            {
                stance.posture += stance.maxPosture * resetModifier;
                stance.posture = Math.Max(0f, stance.posture);
            }
        }

        [ThreadStatic]
        private static bool _inMeleeHitContext;

        private static bool IsAgentInQuickStaminaRegen(Agent agent, Stance stance)
        {
            float quickStaminaThreshold = 0.7f;
            if (stance.stamina >= quickStaminaThreshold * stance.maxStamina)
            {
                return false;
            }
            float currentTime = MBCommon.GetTotalMissionTime();
            if (currentTime - agent.LastMeleeAttackTime > 10f &&
                currentTime - agent.LastMeleeHitTime > 10f &&
                currentTime - agent.LastRangedAttackTime > 10f &&
                currentTime - agent.LastRangedHitTime > 10f
                )
            {
                return true;
            }
            return false;
        }

        private static bool IsAgentInQuickPostureRegen(Agent agent)
        {
            float currentTime = MBCommon.GetTotalMissionTime();
            if (currentTime - agent.LastMeleeAttackTime > 10f &&
                currentTime - agent.LastMeleeHitTime > 10f &&
                currentTime - agent.LastRangedAttackTime > 10f &&
                currentTime - agent.LastRangedHitTime > 10f
                )
            {
                return true;
            }
            return false;
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            if (!Mission.Current.IsFieldBattle)
            {
                return;
            }
            //if horse is killed and has certain speed it affect units around
            if (affectedAgent != null && !affectedAgent.IsHuman)
            {
                Agent horse = affectedAgent;
                float speed = horse.MovementVelocity.Length;
                if (speed >= 5f)
                {
                    List<Agent> list = new List<Agent>();
                    Vec2 searchPosition = horse.Position.AsVec2 + horse.GetMovementDirection().Normalized();
                    AgentProximityMap.ProximityMapSearchStruct searchStruct = AgentProximityMap.BeginSearch(Mission.Current, searchPosition, 0f, extendRangeByBiggestAgentCollisionPadding: true);
                    while (searchStruct.LastFoundAgent != null)
                    {
                        Agent lastFoundAgent = searchStruct.LastFoundAgent;
                        if (lastFoundAgent.CurrentMortalityState != Agent.MortalityState.Invulnerable && !lastFoundAgent.HasMount)
                        {
                            list.Add(lastFoundAgent);
                        }
                        AgentProximityMap.FindNext(Mission.Current, ref searchStruct);
                    }
                    foreach (Agent agent in list)
                    {
                        agent.SetActionChannel(0, ActionIndexCache.act_stagger_backward_3, actionSpeed: 1f);
                    }
                }
                base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
                return;
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (RBMConfig.RBMConfig.postureEnabled && Mission.Current.AllowAiTicking)
            {
                if (currentDtToUpdateAgents < timeToUpdateAgents)
                {
                    currentDtToUpdateAgents += dt;
                }
                else
                {
                    foreach (Agent agent in Mission.Current.Agents)
                    {
                        if (agent.IsActive() && agent.IsHuman)
                        {
                            agent.UpdateAgentStats();
                        }
                    }
                    currentDtToUpdateAgents = 0f;
                }

                currentDtToUpdateStaminaHealth += dt;

                if (currentDt < timeToCalc)
                {
                    currentDt += dt;
                }
                else
                {
                    _inactiveAgentsBuffer.Clear();
                    foreach (KeyValuePair<Agent, Stance> entry in AgentStances.values)
                    {
                        if (entry.Key != null && entry.Key.Mission != null && !entry.Key.IsActive())
                        {
                            _inactiveAgentsBuffer.Add(entry.Key);
                            continue;
                        }
                        if (entry.Key.IsPlayerControlled)
                        {
                            if (AgentStances.postureVisual != null && AgentStances.postureVisual._dataSource.ShowPlayerPostureStatus)
                            {
                                AgentStances.postureVisual._dataSource.PlayerPosture = (int)entry.Value.posture;
                                AgentStances.postureVisual._dataSource.PlayerPostureMax = (int)entry.Value.maxPosture;
                                AgentStances.postureVisual._dataSource.PlayerPostureText = ((int)entry.Value.posture).ToString();
                                AgentStances.postureVisual._dataSource.PlayerPostureMaxText = ((int)entry.Value.maxPosture).ToString();

                                AgentStances.postureVisual._dataSource.PlayerStamina = (int)entry.Value.stamina;
                                AgentStances.postureVisual._dataSource.PlayerStaminaMax = (int)entry.Value.maxStamina;
                                AgentStances.postureVisual._dataSource.PlayerStaminaText = ((int)entry.Value.stamina).ToString();
                                AgentStances.postureVisual._dataSource.PlayerStaminaMaxText = ((int)entry.Value.maxStamina).ToString();
                            }
                        }
                        if (entry.Value.posture < entry.Value.maxPosture)
                        {
                            if (RBMConfig.RBMConfig.postureGUIEnabled)
                            {
                                if (AgentStances.postureVisual != null && AgentStances.postureVisual._dataSource.ShowEnemyStatus && AgentStances.postureVisual.affectedAgent == entry.Key)
                                {
                                    AgentStances.postureVisual._dataSource.EnemyPosture = (int)entry.Value.posture;
                                    AgentStances.postureVisual._dataSource.EnemyPostureMax = (int)entry.Value.maxPosture;

                                    AgentStances.postureVisual._dataSource.EnemyStamina = (int)entry.Value.stamina;
                                    AgentStances.postureVisual._dataSource.EnemyStaminaMax = (int)entry.Value.maxStamina;
                                }
                            }
                            bool isInQuickStaminaRegen = IsAgentInQuickPostureRegen(agent: entry.Key);
                            if (isInQuickStaminaRegen)
                            {
                                entry.Value.tickPostureRegen(multiplier: 3f);
                            }
                            else
                            {
                                entry.Value.tickPostureRegen();
                            }
                        }
                        if (RBMConfig.RBMConfig.staminaEnabled)
                        {
                            if (entry.Value.stamina < entry.Value.maxStamina)
                            {
                                bool isInQuickStaminaRegen = IsAgentInQuickStaminaRegen(agent: entry.Key, stance: entry.Value);
                                if (isInQuickStaminaRegen)
                                {
                                    entry.Value.tickStaminaRegen(multiplier: 3f);
                                }
                                else
                                {
                                    entry.Value.tickStaminaRegen();
                                }
                            }

                            //stamina health regen
                            float staminaLevel = entry.Value.stamina / entry.Value.maxStamina;
                            if (currentDtToUpdateStaminaHealth > timeToCalcStaminaHealth)
                            {
                                if (staminaLevel > 0.85f)
                                {
                                    entry.Key.Health = Math.Min(entry.Key.HealthLimit, entry.Key.Health + 0.9f);
                                }
                            }
                        }
                    }
                    if (currentDtToUpdateStaminaHealth > timeToCalcStaminaHealth)
                    {
                        currentDtToUpdateStaminaHealth = 0f;
                    }
                    foreach (Agent agent in _inactiveAgentsBuffer)
                    {
                        AgentStances.values.Remove(agent);
                    }

                    foreach (KeyValuePair<Agent, FormationClass> entry in agentsToChangeFormation)
                    {
                        if (entry.Key != null && entry.Key.Mission != null && entry.Key.IsActive() && entry.Key.Team != null)
                        {
                            entry.Key.Formation = entry.Key.Team.GetFormation(entry.Value);
                            entry.Key.DisableScriptedMovement();
                        }
                    }
                    agentsToChangeFormation.Clear();

                    //shield drop
                    _dropShieldBuffer.Clear();
                    for (int i = agentsToDropShield.Count - 1; i >= 0; i--)
                    {
                        if (agentsToDropShield[i] != null && agentsToDropShield[i].Mission != null && agentsToDropShield[i].IsActive())
                        {
                            ActionCodeType currentActionType = agentsToDropShield[i].GetCurrentActionType(1);
                            if (
                                currentActionType == ActionCodeType.ReleaseMelee ||
                                currentActionType == ActionCodeType.ReleaseRanged ||
                                currentActionType == ActionCodeType.ReleaseThrowing ||
                                currentActionType == ActionCodeType.WeaponBash)
                            {
                                continue;
                            }
                            else
                            {
                                _dropShieldBuffer.Add(agentsToDropShield[i]);
                            }
                        }
                        else
                        {
                            _dropShieldBuffer.Add(agentsToDropShield[i]);
                        }
                    }
                    foreach (Agent agent in _dropShieldBuffer)
                    {
                        if (agent != null && agent.Mission != null && agent.IsActive())
                        {
                            EquipmentIndex ei = agent.GetOffhandWieldedItemIndex();
                            if (ei != EquipmentIndex.None)
                            {
                                agent.DropItem(ei);
                                agent.UpdateAgentProperties();
                            }
                        }
                        agentsToDropShield.Remove(agent);
                    }

                    //weapon drop
                    _dropWeaponBuffer.Clear();
                    for (int i = agentsToDropWeapon.Count - 1; i >= 0; i--)
                    {
                        if (agentsToDropWeapon[i] != null && agentsToDropWeapon[i].Mission != null && agentsToDropWeapon[i].IsActive())
                        {
                            ActionCodeType currentActionType = agentsToDropWeapon[i].GetCurrentActionType(1);
                            if (
                                currentActionType == ActionCodeType.ReleaseMelee ||
                                currentActionType == ActionCodeType.ReleaseRanged ||
                                currentActionType == ActionCodeType.ReleaseThrowing ||
                                currentActionType == ActionCodeType.WeaponBash)
                            {
                                continue;
                            }
                            else
                            {
                                _dropWeaponBuffer.Add(agentsToDropWeapon[i]);
                            }
                        }
                        else
                        {
                            _dropWeaponBuffer.Add(agentsToDropWeapon[i]);
                        }
                    }
                    foreach (Agent agent in _dropWeaponBuffer)
                    {
                        if (agent != null && agent.Mission != null && agent.IsActive())
                        {
                            EquipmentIndex ei = agent.GetPrimaryWieldedItemIndex();
                            if (ei != EquipmentIndex.None)
                            {
                                agent.DropItem(ei);
                                agent.UpdateAgentProperties();
                            }
                        }
                        agentsToDropWeapon.Remove(agent);
                    }

                    currentDt = 0f;
                }
            }
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            base.OnAgentHit(affectedAgent, affectorAgent, in affectorWeapon, in blow, in attackCollisionData);
            if (affectedAgent == null || !affectedAgent.IsActive() || !affectedAgent.IsHuman)
            {
                return;
            }
            if (RBMConfig.RBMConfig.postureEnabled)
            {
                Stance affectedAgentPosture = null;
                AgentStances.values.TryGetValue(affectedAgent, out affectedAgentPosture);
                if (affectedAgentPosture != null)
                {
                    //missile hit posture/stamina loss
                    if (blow.IsMissile && affectorWeapon.CurrentUsageItem != null)
                    {
                        bool isDirectHit = !attackCollisionData.AttackBlockedWithShield;
                        WeaponClass missileWeaponClass = affectorWeapon.CurrentUsageItem.WeaponClass;

                        float arrowAgentPostureDamage = 25f;
                        float throwingAgentPostureDamage = 100f;

                        float arrowShieldPostureDamage = 20f;
                        float throwingShieldPostureDamage = 70f;

                        //agent hit
                        if (isDirectHit)
                        {
                            //headshot multiplier
                            if (blow.VictimBodyPart == BoneBodyPartType.Head || blow.VictimBodyPart == BoneBodyPartType.Head)
                            {
                                arrowAgentPostureDamage = 50f;
                                throwingAgentPostureDamage = 200f;
                            }
                            switch (missileWeaponClass)
                            {
                                case WeaponClass.Bow:
                                case WeaponClass.Crossbow:
                                case WeaponClass.Arrow:
                                case WeaponClass.Bolt:
                                case WeaponClass.ThrowingKnife:
                                    {
                                        affectedAgentPosture.posture = Math.Max(0f, affectedAgentPosture.posture - arrowAgentPostureDamage);
                                        if (RBMConfig.RBMConfig.staminaEnabled)
                                        {
                                            affectedAgentPosture.reduceStamina(arrowAgentPostureDamage * 2f);
                                        }
                                        break;
                                    }
                                case WeaponClass.Javelin:
                                case WeaponClass.ThrowingAxe:

                                    {
                                        affectedAgentPosture.posture = Math.Max(0f, affectedAgentPosture.posture - throwingAgentPostureDamage);
                                        if (RBMConfig.RBMConfig.staminaEnabled)
                                        {
                                            affectedAgentPosture.reduceStamina(throwingAgentPostureDamage * 3f);
                                        }
                                        break;
                                    }
                            }
                        }
                        //shield hit
                        else
                        {
                            switch (missileWeaponClass)
                            {
                                case WeaponClass.Bow:
                                case WeaponClass.Crossbow:
                                case WeaponClass.Arrow:
                                case WeaponClass.Bolt:
                                case WeaponClass.ThrowingKnife:

                                    {
                                        affectedAgentPosture.posture = Math.Max(0f, affectedAgentPosture.posture - arrowShieldPostureDamage);
                                        if (RBMConfig.RBMConfig.staminaEnabled)
                                        {
                                            affectedAgentPosture.reduceStamina(arrowShieldPostureDamage * 2f);
                                        }
                                        break;
                                    }
                                case WeaponClass.Javelin:
                                case WeaponClass.ThrowingAxe:
                                    {
                                        affectedAgentPosture.posture = Math.Max(0f, affectedAgentPosture.posture - throwingShieldPostureDamage);
                                        if (RBMConfig.RBMConfig.staminaEnabled)
                                        {
                                            affectedAgentPosture.reduceStamina(throwingShieldPostureDamage * 3f);
                                        }
                                        break;
                                    }
                            }
                        }

                        //ranged posture break
                        if (affectedAgentPosture.posture <= 0f)
                        {
                            affectedAgentPosture.posture = 0f;
                            forceStaggerAnimation(affectedAgent, attackCollisionData, 0.85f, false);
                            ResetPostureForAgent(ref affectedAgentPosture, postureResetModifier);
                        }

                        addPosturedamageVisual(affectorAgent, affectedAgent);
                    }
                }
            }
            //int ammoStuckInAgent = affectedAgent.GetAttachedWeaponsCount();
            //int arrowsBoltsStuckInAgent = 0;
            //int maxAmmoStuckInAgent = 5;
            //for (int i = 0; i < ammoStuckInAgent; i++)
            //{
            //    //remove stuck javelins
            //    WeaponClass ammoWeaponClass = affectedAgent.GetAttachedWeapon(i).CurrentUsageItem.WeaponClass;
            //    if (ammoWeaponClass == WeaponClass.Javelin || ammoWeaponClass == WeaponClass.ThrowingAxe || ammoWeaponClass == WeaponClass.ThrowingKnife)
            //    {
            //        affectedAgent.DeleteAttachedWeapon(i);
            //    }

            //    if (ammoWeaponClass == WeaponClass.Arrow || ammoWeaponClass == WeaponClass.Bolt)
            //    {
            //        arrowsBoltsStuckInAgent++;
            //    }

            //    //remove stuck arrows/bolts if there are too many of them
            //    if (arrowsBoltsStuckInAgent >= maxAmmoStuckInAgent)
            //    {
            //        affectedAgent.DeleteAttachedWeapon(i);
            //    }
            //}

            //drop shield if too many arrows/bolts or javelins/throwing axes/throwing knives are stuck in the shield, disabled for now
            //int maxAmmoStuckInShield = 15;
            //int maxJavelinsAxesKnivesStuckInShield = 3;
            //if (affectedAgent.WieldedOffhandWeapon.IsShield())
            //{
            //    int ammoStuckInShieldCount = affectedAgent.WieldedOffhandWeapon.GetAttachedWeaponsCount();
            //    int arrowsBoltsStuckInShieldCount = 0;
            //    int javelinsAxesKnivesStuckInShieldCount = 0;
            //    if (ammoStuckInShieldCount >= maxAmmoStuckInShield)
            //    {
            //        TryToDropShield(affectedAgent);
            //    }
            //    else
            //    {
            //        for (int i = 0; i < ammoStuckInShieldCount; i++)
            //        {
            //            WeaponClass ammoWeaponClass = affectedAgent.WieldedOffhandWeapon.GetAttachedWeapon(i).CurrentUsageItem.WeaponClass;
            //            if (ammoWeaponClass == WeaponClass.Arrow || ammoWeaponClass == WeaponClass.Bolt)
            //            {
            //                arrowsBoltsStuckInShieldCount++;
            //            }
            //            else if (ammoWeaponClass == WeaponClass.Javelin || ammoWeaponClass == WeaponClass.ThrowingAxe || ammoWeaponClass == WeaponClass.ThrowingKnife)
            //            {
            //                javelinsAxesKnivesStuckInShieldCount++;
            //            }
            //        }
            //        if (javelinsAxesKnivesStuckInShieldCount >= maxJavelinsAxesKnivesStuckInShield)
            //        {
            //            TryToDropShield(affectedAgent);
            //        }
            //    }
            //}
        }
    }
}
