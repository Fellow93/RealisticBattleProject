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
            // Ranged = carries a bow/crossbow/sling with more than 5 rounds left for it. Judged by the launcher's
            // slot, NOT the wielded one: the old test read the ammo of whatever was in hand, so an archer who had
            // drawn his sidearm counted as 0 ammo and was moved to Infantry, and moving him back (on the next call,
            // which the emptied or reclassified formation itself triggers) re-laid-out both formations each time.
            // Native classes him by what he carries, so this also stops the two disagreeing.
            private static bool HasLauncherWithAmmo(Agent agent)
            {
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                {
                    MissionWeapon weapon = agent.Equipment[i];
                    if (weapon.IsEmpty)
                    {
                        continue;
                    }
                    WeaponComponentData usage = weapon.CurrentUsageItem;
                    if (usage != null && usage.IsRangedWeapon
                        && (usage.AmmoClass == WeaponClass.Arrow || usage.AmmoClass == WeaponClass.Bolt || usage.AmmoClass == WeaponClass.SlingStone)
                        && agent.Equipment.GetAmmoAmount(i) > 5)
                    {
                        return true;
                    }
                }
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("ManageFormationCounts", new Type[] { typeof(int), typeof(int), typeof(int), typeof(int) })]
            private static bool PrefixSetDefaultBehaviorWeights(ref TacticComponent __instance, ref int infantryCount, ref int rangedCount, ref int cavalryCount, ref int rangedCavalryCount)
            {
                // Every reassignment below needs AI-controlled formations on both ends, so a team with none (the
                // player's, when he commands) would only pay for the scan. And like every other RBM reshuffle it
                // stays off when an RTS/minimap mod drives the parallel formation path (IsFormationReshufflingUnsafe);
                // the TacticsState docs always said this prefix did, but the check was never added here.
                if (Mission.Current != null && Mission.Current.IsFieldBattle && !IsFormationReshufflingUnsafe
                    && __instance.Team != null && __instance.Team.GetAIControlledFormationCount() > 0)
                {
                    foreach (Agent agent in __instance.Team.ActiveAgents)
                    {
                        // A formation the player ordered to dismount stays together after he delegates it: sorting by
                        // HasMount would send its dismounted horse archers to the archers and the rest to the infantry.
                        if (agent != null && agent.IsHuman && !agent.IsRunningAway
                            && agent.Formation?.RidingOrder.OrderEnum != RidingOrder.RidingOrderEnum.Dismount)
                        {
                            //banner bearers should stay in their current formation type
                            if (RBMAI.Utilities.IsBannerBearer(agent))
                            {
                                agent.FormationPositionPreference = FormationPositionPreference.Back;
                                continue;
                            }
                            bool isRanged = HasLauncherWithAmmo(agent);
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
