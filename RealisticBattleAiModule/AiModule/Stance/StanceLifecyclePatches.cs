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
        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("MeleeHitCallback")]
        private class MeleeHitContextPatch
        {
            private static void Prefix() => _inMeleeHitContext = true;

            private static void Finalizer() => _inMeleeHitContext = false;
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("EquipItemsFromSpawnEquipment")]
        private class EquipItemsFromSpawnEquipmentPatch
        {
            private static void Prefix(ref Agent __instance)
            {
                if (RBMConfig.RBMConfig.postureEnabled && __instance.IsHuman)
                {
                    AgentStances.values[__instance] = new Stance();
                    Stance stance = AgentStances.values[__instance];
                    Stance.InitializeStamina(__instance, ref stance);
                }
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("OnWieldedItemIndexChange")]
        private class OnWieldedItemIndexChangePatch
        {
            private static void Postfix(ref Agent __instance, bool isOffHand, bool isWieldedInstantly, bool isWieldedOnSpawn)
            {
                if (RBMConfig.RBMConfig.postureEnabled)
                {
                    Stance stance = null;
                    AgentStances.values.TryGetValue(__instance, out stance);
                    if (stance == null)
                    {
                        AgentStances.values[__instance] = new Stance();
                        stance = AgentStances.values[__instance];
                        Stance.InitializeStamina(__instance, ref stance);
                    }
                    AgentStances.values.TryGetValue(__instance, out stance);
                    if (stance != null)
                    {
                        Stance.InitializePosture(__instance, ref stance);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("LoadMission")]
        public class LoadMissionPatch
        {
            private static void Postfix()
            {
                AgentStances.values.Clear();
                agentsToDropShield.Clear();
                agentsToDropWeapon.Clear();
                agentsToChangeFormation.Clear();
            }
        }

        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("OnDeactivate")]
        public class OnDeactivatePatch
        {
            private static void Postfix()
            {
                AgentStances.values.Clear();
                agentsToDropShield.Clear();
                agentsToDropWeapon.Clear();
                agentsToChangeFormation.Clear();
            }
        }
    }
}
