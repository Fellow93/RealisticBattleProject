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
        /// <summary>Merges unreferenced AI formations (stray reinforcements, leftover slots) into the referenced one of their kind; never under IsFormationReshufflingUnsafe, a dismount order or player formations.</summary>
        internal static void MergeStrayFormations(Team team, Formation main, Formation archers, Formation cavalry, Formation rangedCavalry, params Formation[] alsoReferenced)
        {
            if (team == null || IsFormationReshufflingUnsafe)
            {
                return;
            }
            foreach (Formation f in team.FormationsIncludingEmpty.ToList())
            {
                if (f == null || f.CountOfUnits <= 0 || !f.IsAIControlled || f.RidingOrder.OrderEnum == RidingOrder.RidingOrderEnum.Dismount
                    || f == main || f == archers || f == cavalry || f == rangedCavalry || (alsoReferenced != null && alsoReferenced.Contains(f)))
                {
                    continue;
                }
                List<Formation> targets = new List<Formation>();
                f.ApplyActionOnEachUnitViaBackupList((Agent a) =>
                {
                    Formation target = a.HasMount
                        ? (a.IsRangedCached ? (rangedCavalry ?? cavalry) : (cavalry ?? rangedCavalry))
                        : (a.IsRangedCached ? (archers ?? main) : (main ?? archers));
                    if (target != null && target != f && target.IsAIControlled)
                    {
                        a.Formation = target;
                        if (!targets.Contains(target))
                        {
                            targets.Add(target);
                        }
                    }
                });
                if (targets.Count > 0)
                {
                    team.TriggerOnFormationsChanged(f);
                    foreach (Formation target in targets)
                    {
                        team.TriggerOnFormationsChanged(target);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TacticComponent))]
        private class ManageFormationCountsPatch
        {
            // Ranged = carries a launcher with more than 5 rounds left, judged by the launcher's slot, not the wielded weapon (a drawn sidearm must not reclassify an archer).
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
                // Needs AI formations on both ends; off under IsFormationReshufflingUnsafe (RTS/minimap mods).
                if (Mission.Current != null && Mission.Current.IsFieldBattle && !IsFormationReshufflingUnsafe
                    && __instance.Team != null && __instance.Team.GetAIControlledFormationCount() > 0)
                {
                    foreach (Agent agent in __instance.Team.ActiveAgents)
                    {
                        // A formation the player ordered to dismount stays together (sorting by HasMount would split it).
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
