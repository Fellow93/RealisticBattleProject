using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using RBMAI;
using StoryMode.GameComponents;
using StoryMode.Missions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.CampaignSystem.ComponentInterfaces.CombatXpModel;
using static TaleWorlds.CampaignSystem.MapEvents.MapEvent;

namespace RBMCombat
{
    internal partial class CampaignChanges
    {
        [HarmonyPatch(typeof(CommonAIComponent))]
        [HarmonyPatch("InitializeMorale")]
        private class InitializeMoralePatch
        {
            private static bool Prefix(ref CommonAIComponent __instance, ref Agent ___Agent, ref float ____initialMorale, ref float ____recoveryMorale)
            {
                //int num = MBRandom.RandomInt(30);
                int num = 30;
                float num2 = ___Agent.Components.Sum((AgentComponent c) => c.GetMoraleAddition());
                float baseMorale = 35f + (float)num + num2;
                baseMorale = MissionGameModels.Current.BattleMoraleModel.GetEffectiveInitialMorale(___Agent, baseMorale);
                baseMorale = (____initialMorale = MBMath.ClampFloat(baseMorale, 15f, 100f));
                ____recoveryMorale = ____initialMorale * 0.5f;
                __instance.Morale = ____initialMorale;
                return false;
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("InitializeSpawnEquipment")]
        private class InitializeSpawnEquipmentPatch
        {
            private static bool Prefix(Equipment spawnEquipment, ref Agent __instance)
            {
                if (Campaign.Current != null && __instance.IsHuman && __instance.IsHero && !__instance.Character.IsPlayerCharacter)
                {
                    Equipment spawnEquipment2 = spawnEquipment.Clone();
                    bool shoudReceiveUpgradedGear = false;
                    Hero hero = ((CharacterObject)__instance.Character).HeroObject;
                    foreach (Kingdom kingdom in Kingdom.All)
                    {
                        if (kingdom.Leader != null && kingdom.Leader == hero)
                        {
                            shoudReceiveUpgradedGear = true;
                            break;
                        }
                        foreach (Hero lord in kingdom.AliveLords)
                        {
                            if (lord != null && lord == hero)
                            {
                                shoudReceiveUpgradedGear = true;
                                break;
                            }
                        }
                    }
                    if (shoudReceiveUpgradedGear)
                    {
                        for (int i = 0; i < 12; i++)
                        {
                            if (spawnEquipment2[(EquipmentIndex)i].Item != null)
                            {
                                IReadOnlyList<ItemModifier> itemModifiers = spawnEquipment2[(EquipmentIndex)i].Item?.ItemComponent?.ItemModifierGroup?.ItemModifiers;
                                if (itemModifiers != null)
                                {
                                    EquipmentElement equipmentFromSlot = spawnEquipment2[(EquipmentIndex)i];
                                    equipmentFromSlot.SetModifier(itemModifiers[0]);
                                    spawnEquipment2[(EquipmentIndex)i] = equipmentFromSlot;
                                }
                            }
                        }
                    }
                    PropertyInfo propertySpawnEquipment = typeof(Agent).GetProperty("SpawnEquipment");
                    propertySpawnEquipment.DeclaringType.GetProperty("SpawnEquipment");
                    propertySpawnEquipment.SetValue(__instance, spawnEquipment2, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Equipment))]
        [HarmonyPatch("GetRandomizedEquipment")]
        private class GetRandomizedEquipmentPatch
        {
            private static bool Prefix(ref List<Equipment> equipmentSets, ref EquipmentIndex weaponSlot, ref int weaponSetNo, ref bool randomEquipmentModifier, ref EquipmentElement __result)
            {
                EquipmentElement equipmentFromSlot = equipmentSets[weaponSetNo].GetEquipmentFromSlot(weaponSlot);
                //if(equipmentSets.Count > 1)
                //{
                //    bool testik = false;
                //}
                __result = equipmentFromSlot;
                return false;
            }
        }
    }
}
