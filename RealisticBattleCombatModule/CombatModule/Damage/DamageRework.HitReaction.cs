using HarmonyLib;
using RBMAI;
using SandBox.GameComponents;
using SandBox.Missions.MissionLogics;
using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMCombat
{
    internal partial class DamageRework
    {
        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        [HarmonyPatch("UpdateHumanStats")]
        private class SandboxAgentUpdateHumanStats
        {
            private static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties)
            {
                agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder = 0.01f;
            }
        }

        [HarmonyPatch(typeof(BattleAgentLogic))]
        [HarmonyPatch("OnAgentHit")]
        private class OnAgentHitPatch
        {
            private static bool Prefix(Agent affectedAgent, Agent affectorAgent, in MissionWeapon attackerWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
            {
                if (affectedAgent != null && blow.InflictedDamage > 1f && affectedAgent.IsActive() && attackCollisionData.CollisionResult == CombatCollisionResult.StrikeAgent && !blow.IsFallDamage)
                {
                    Utilities.initiateCheckForArmor(ref affectedAgent, attackCollisionData, blow, affectorAgent, attackerWeapon);
                    Utilities.numOfHits++;
                }
                if (affectedAgent.Character != null && affectorAgent != null && affectorAgent.Character != null && affectedAgent.IsActive())
                {
                    bool isFatal = affectedAgent.Health - (float)blow.InflictedDamage < 1f;
                    bool isTeamKill;
                    if (affectedAgent.Team != null && affectorAgent.Team != null)
                    {
                        isTeamKill = affectedAgent.Team.Side == affectorAgent.Team.Side;
                    }
                    else
                    {
                        isTeamKill = true;
                    }
                    affectorAgent.Origin.OnScoreHit(affectedAgent.Character, affectorAgent.Formation?.Captain?.Character, blow.InflictedDamage, isFatal, isTeamKill, attackerWeapon.CurrentUsageItem);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(CustomBattleAgentLogic))]
        [HarmonyPatch("OnAgentHit")]
        private class CustomBattleAgentLogicPatch
        {
            private static bool Prefix(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
            {
                if (affectedAgent != null && affectedAgent.IsActive() && blow.InflictedDamage > 1f && attackCollisionData.CollisionResult == CombatCollisionResult.StrikeAgent && !blow.IsFallDamage)
                {
                    Utilities.initiateCheckForArmor(ref affectedAgent, attackCollisionData, blow, affectorAgent, affectorWeapon);
                    Utilities.numOfHits++;
                }
                return true;
            }
        }
    }
}
