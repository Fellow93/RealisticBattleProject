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
        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("OnShieldDamaged")]
        private class OnShieldDamagedPatch
        {
            private static bool Prefix(ref Agent __instance, ref EquipmentIndex slotIndex, ref int inflictedDamage)
            {
                int num = MathF.Max(0, __instance.Equipment[slotIndex].HitPoints - inflictedDamage);
                __instance.ChangeWeaponHitPoints(slotIndex, (short)num);
                if (num == 0)
                {
                    __instance.RemoveEquippedWeapon(slotIndex);
                }
                return false;
            }
        }

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
