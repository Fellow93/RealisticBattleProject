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
using static RBMStance.PostureDamage;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMStance
{
    public partial class StanceLogic : MissionLogic
    {
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
                ClearMissionState();
            }
        }
    }
}
