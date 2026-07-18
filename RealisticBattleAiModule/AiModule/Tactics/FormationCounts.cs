using HarmonyLib;
using SandBox.Missions.MissionLogics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer;
using TaleWorlds.MountAndBlade.ViewModelCollection.HUD.FormationMarker;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static partial class Tactics
    {
        [HarmonyPatch(typeof(TacticComponent))]
        private class ManageFormationCountsPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("ManageFormationCounts", new Type[] { typeof(int), typeof(int), typeof(int), typeof(int) })]
            private static bool PrefixSetDefaultBehaviorWeights(ref TacticComponent __instance, ref int infantryCount, ref int rangedCount, ref int cavalryCount, ref int rangedCavalryCount)
            {
                if (Mission.Current != null && Mission.Current.IsFieldBattle)
                {
                    foreach (Agent agent in __instance.Team.ActiveAgents)
                    {
                        if (agent != null && agent.IsHuman && !agent.IsRunningAway)
                        {
                            //banner bearers should stay in their current formation type
                            if (RBMAI.Utilities.IsBannerBearer(agent))
                            {
                                agent.FormationPositionPreference = FormationPositionPreference.Back;
                                continue;
                            }
                            EquipmentIndex wieldedItemIndex = agent.GetPrimaryWieldedItemIndex();
                            bool isRanged = (wieldedItemIndex != EquipmentIndex.None && agent.Equipment.HasRangedWeapon(WeaponClass.Arrow) && agent.Equipment.GetAmmoAmount(wieldedItemIndex) > 5) ||
                                (wieldedItemIndex != EquipmentIndex.None && agent.Equipment.HasRangedWeapon(WeaponClass.Bolt) && agent.Equipment.GetAmmoAmount(wieldedItemIndex) > 5) ||
                                (wieldedItemIndex != EquipmentIndex.None && agent.Equipment.HasRangedWeapon(WeaponClass.SlingStone) && agent.Equipment.GetAmmoAmount(wieldedItemIndex) > 5);
                            if (agent.HasMount && isRanged)
                            {
                                if (__instance.Team.GetFormation(FormationClass.HorseArcher) != null && __instance.Team.GetFormation(FormationClass.HorseArcher).IsAIControlled && agent.Formation != null && agent.Formation.IsAIControlled)
                                {
                                    agent.Formation = __instance.Team.GetFormation(FormationClass.HorseArcher);
                                }
                            }
                            if (agent.HasMount && !isRanged)
                            {
                                if (__instance.Team.GetFormation(FormationClass.Cavalry) != null && __instance.Team.GetFormation(FormationClass.Cavalry).IsAIControlled && agent.Formation != null && agent.Formation.IsAIControlled)
                                {
                                    agent.Formation = __instance.Team.GetFormation(FormationClass.Cavalry);
                                }
                            }
                            if (!agent.HasMount && isRanged)
                            {
                                if (__instance.Team.GetFormation(FormationClass.Ranged) != null && __instance.Team.GetFormation(FormationClass.Ranged).IsAIControlled && agent.Formation != null && agent.Formation.IsAIControlled)
                                {
                                    agent.Formation = __instance.Team.GetFormation(FormationClass.Ranged);
                                }
                            }
                            if (!agent.HasMount && !isRanged)
                            {
                                if (__instance.Team.GetFormation(FormationClass.Infantry) != null && __instance.Team.GetFormation(FormationClass.Infantry).IsAIControlled && agent.Formation != null && agent.Formation.IsAIControlled)
                                {
                                    agent.Formation = __instance.Team.GetFormation(FormationClass.Infantry);
                                }
                            }
                        }
                    }
                }
                if (Mission.Current != null && Mission.Current.MainAgent != null && Mission.Current.PlayerTeam != null && Mission.Current.IsSiegeBattle)
                {
                    Mission.Current.MainAgent.Formation = Mission.Current.PlayerTeam.GetFormation(FormationClass.Infantry);
                }
                return true;
            }
        }
    }
}
