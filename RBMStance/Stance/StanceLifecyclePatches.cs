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
        // The patches below share their targets with RBMCombat's and run after them, the order they always had while
        // they lived in RBMAI (patched later), so the momentum recorded here is inOutMomentumRemaining as RBMCombat's
        // prefix left it.
        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("MeleeHitCallback")]
        [HarmonyAfter("com.rbmcombat")]
        private class MeleeHitContextPatch
        {
            private static void Prefix(ref float inOutMomentumRemaining)
            {
                _inMeleeHitContext = true;
                _meleeHitMomentumRemaining = inOutMomentumRemaining;
            }

            private static void Finalizer() => _inMeleeHitContext = false;
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("EquipItemsFromSpawnEquipment")]
        [HarmonyAfter("com.rbmcombat")]
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
        [HarmonyAfter("com.rbmcombat")]
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
                    Stance.InitializePosture(__instance, ref stance);
                }
            }
        }

        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("LoadMission")]
        public class LoadMissionPatch
        {
            private static void Postfix()
            {
                ClearMissionState();
                AgentStances.postureVisual = null;
            }
        }

        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("OnDeactivate")]
        public class OnDeactivatePatch
        {
            private static void Postfix()
            {
                ClearMissionState();
                AgentStances.postureVisual = null;
            }
        }
    }
}
