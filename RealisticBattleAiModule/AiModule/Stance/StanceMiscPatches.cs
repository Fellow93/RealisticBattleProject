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
        // (Removed) OnShieldDamagedPatch: it was a byte-identical reimplementation of vanilla
        // Agent.OnShieldDamaged returning false, i.e. a no-op that only blocked other mods' patches.

        [HarmonyPatch(typeof(TournamentRound))]
        [HarmonyPatch("EndMatch")]
        private class EndMatchPatch
        {
            private static void Postfix(ref TournamentRound __instance)
            {
                foreach (KeyValuePair<Agent, Stance> entry in AgentStances.values)
                {
                    entry.Value.posture = entry.Value.maxPosture;
                    if (RBMConfig.RBMConfig.postureGUIEnabled)
                    {
                        if (entry.Key.IsPlayerControlled)
                        {
                            //InformationManager.DisplayMessage(new InformationMessage(entry.Value.stance.ToString()));
                            PushPlayerStanceBars(entry.Value);
                        }

                        if (AgentStances.postureVisual != null && AgentStances.postureVisual._dataSource.ShowEnemyStatus && AgentStances.postureVisual.affectedAgent == entry.Key)
                        {
                            AgentStances.postureVisual._dataSource.EnemyPosture = (int)entry.Value.posture;
                            AgentStances.postureVisual._dataSource.EnemyPostureMax = (int)entry.Value.maxPosture;

                            AgentStances.postureVisual._dataSource.EnemyStamina = (int)entry.Value.stamina;
                            AgentStances.postureVisual._dataSource.EnemyStaminaMax = (int)entry.Value.maxStamina;
                        }
                    }
                }
                agentsToDropShield.Clear();
                agentsToDropWeapon.Clear();
                agentsToChangeFormation.Clear();
                AgentStances.values.Clear();
            }
        }
    }
}
