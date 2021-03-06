﻿using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;

namespace RealisticBattleAiModule
{
    class Tactics
    {

        //[HarmonyPatch(typeof(Team))]
        //[HarmonyPatch("Tick")]
        //class OverrideTick
        //{

        //    static int i = 0;

        //    static void Postfix(Team __instance)
        //    {
        //        if (__instance.Banner != null)
        //        {
        //            if (i == 500)
        //            {

        //                if (__instance.IsAttacker)
        //                {
        //                    FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
        //                    _currentTacticField.DeclaringType.GetField("_currentTactic");
        //                    TacticComponent _currentTactic = (TacticComponent)_currentTacticField.GetValue(__instance.TeamAI);

        //                    InformationManager.DisplayMessage(new InformationMessage("Attacker " + __instance.TeamIndex + " : " + _currentTactic));

        //                    FieldInfo _availableTacticsField = typeof(TeamAIComponent).GetField("_availableTactics", BindingFlags.NonPublic | BindingFlags.Instance);
        //                    _availableTacticsField.DeclaringType.GetField("_availableTactics");
        //                    List<TacticComponent> _availableTactics = (List<TacticComponent>)_availableTacticsField.GetValue(__instance.TeamAI);
        //                    foreach (TacticComponent tc in _availableTactics)
        //                    {
        //                        MethodInfo method = typeof(TacticComponent).GetMethod("GetTacticWeight", BindingFlags.NonPublic | BindingFlags.Instance);
        //                        method.DeclaringType.GetMethod("GetTacticWeight");
        //                        float weight = (float)method.Invoke(tc, new object[] { });
        //                        InformationManager.DisplayMessage(new InformationMessage(tc + ": " + weight));
        //                    }

        //                    foreach (Formation formation in __instance.Formations)
        //                    {
        //                        if (formation.QuerySystem.IsMeleeFormation)
        //                        {
        //                            InformationManager.DisplayMessage(new InformationMessage("infantry: " + formation.AI.ActiveBehavior));
        //                        }
        //                        else if (formation.QuerySystem.IsCavalryFormation)
        //                        {
        //                            InformationManager.DisplayMessage(new InformationMessage("cavalry: " + formation.AI.ActiveBehavior));
        //                        }
        //                        else if (formation.QuerySystem.IsRangedCavalryFormation)
        //                        {
        //                            InformationManager.DisplayMessage(new InformationMessage("ranged cavalry: " + formation.AI.ActiveBehavior));
        //                        }
        //                        else if (formation.QuerySystem.IsRangedFormation)
        //                        {
        //                            InformationManager.DisplayMessage(new InformationMessage("ranged: " + formation.AI.ActiveBehavior + " " + formation.QuerySystem.MissileRange));
        //                        }
        //                    }
        //                }
        //                else
        //                {
        //                    FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
        //                    _currentTacticField.DeclaringType.GetField("_currentTactic");
        //                    TacticComponent _currentTactic = (TacticComponent)_currentTacticField.GetValue(__instance.TeamAI);

        //                    InformationManager.DisplayMessage(new InformationMessage("Defender " + __instance.TeamIndex + " : " + _currentTactic));

        //                    FieldInfo _availableTacticsField = typeof(TeamAIComponent).GetField("_availableTactics", BindingFlags.NonPublic | BindingFlags.Instance);
        //                    _availableTacticsField.DeclaringType.GetField("_availableTactics");
        //                    List<TacticComponent> _availableTactics = (List<TacticComponent>)_availableTacticsField.GetValue(__instance.TeamAI);
        //                    foreach (TacticComponent tc in _availableTactics)
        //                    {
        //                        MethodInfo method = typeof(TacticComponent).GetMethod("GetTacticWeight", BindingFlags.NonPublic | BindingFlags.Instance);
        //                        method.DeclaringType.GetMethod("GetTacticWeight");
        //                        float weight = (float)method.Invoke(tc, new object[] { });
        //                        InformationManager.DisplayMessage(new InformationMessage(tc + ": " + weight));
        //                    }

        //                    foreach (Formation formation in __instance.Formations)
        //                    {
        //                        if (formation.QuerySystem.IsMeleeFormation)
        //                        {
        //                            InformationManager.DisplayMessage(new InformationMessage("infantry: " + formation.AI.ActiveBehavior));
        //                        }
        //                        else if (formation.QuerySystem.IsCavalryFormation)
        //                        {
        //                            InformationManager.DisplayMessage(new InformationMessage("cavalry: " + formation.AI.ActiveBehavior));
        //                        }
        //                        else if (formation.QuerySystem.IsRangedCavalryFormation)
        //                        {
        //                            InformationManager.DisplayMessage(new InformationMessage("ranged cavalry: " + formation.AI.ActiveBehavior));
        //                        }
        //                        else if (formation.QuerySystem.IsRangedFormation)
        //                        {
        //                            InformationManager.DisplayMessage(new InformationMessage("ranged: " + formation.AI.ActiveBehavior + " " + formation.QuerySystem.MissileRange));
        //                        }
        //                    }
        //                }
        //                i = 0;
        //            }
        //            else
        //            {
        //                i++;
        //            }
        //        }
        //    }
        //}

        //[HarmonyPatch(typeof(MissionAgentSpawnLogic))]
        //[HarmonyPatch("BattleSizeSpawnTick")]
        //class OverrideBattleSizeSpawnTick
        //{

        //    static bool Prefix(MissionAgentSpawnLogic __instance, int ____battleSize)
        //    {
        //        int numberOfActiveDefenderTroops = __instance.NumberOfActiveDefenderTroops;
        //        int numberOfActiveAttackerTroops = __instance.NumberOfActiveAttackerTroops;
        //        int numberOfActiveTroops = __instance.NumberOfActiveTroops;
        //        if ((float)numberOfActiveTroops < (float)____battleSize * 0.2f || (float)numberOfActiveDefenderTroops < (float)____battleSize * 0.2f || (float)numberOfActiveAttackerTroops < (float)____battleSize * 0.2f)
        //        {
        //            return true;
        //        }
        //        else
        //        {
        //            return false;
        //        }
        //    }
        //}

        [HarmonyPatch(typeof(TaleWorlds.MountAndBlade.ViewModelCollection.HUD.MissionFormationMarkerTargetVM))]
        [HarmonyPatch("Refresh")]
        class OverrideRefresh
        {
            private static string chooseIcon(Formation formation)
            {
                if (formation != null)
                {
                    if (formation.QuerySystem.IsInfantryFormation)
                    {
                        return TargetIconType.Special_Swordsman.ToString();
                    }
                    if (formation.QuerySystem.IsRangedFormation)
                    {
                        return TargetIconType.Archer_Heavy.ToString();
                    }
                    if (formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        return TargetIconType.HorseArcher_Light.ToString();
                    }
                    if (formation.QuerySystem.IsCavalryFormation && !Utilities.CheckIfMountedSkirmishFormation(formation))
                    {
                        return TargetIconType.Cavalry_Light.ToString();
                    }
                    if (formation.QuerySystem.IsCavalryFormation && Utilities.CheckIfMountedSkirmishFormation(formation))
                    {
                        return TargetIconType.Special_JavelinThrower.ToString();
                    }
                }
                return TargetIconType.None.ToString();
            }

            static void Postfix(TaleWorlds.MountAndBlade.ViewModelCollection.HUD.MissionFormationMarkerTargetVM __instance)
            {
                __instance.FormationType = chooseIcon(__instance.Formation);
            }
        }

        [HarmonyPatch(typeof(MissionCombatantsLogic))]
        [HarmonyPatch("EarlyStart")]
        class TeamAiFieldBattle
        {
            static void Postfix()
            {
                ManagedParameters.SetParameter(ManagedParametersEnum.BipedalRadius, 0.49f);
                if (Mission.Current.Teams.Any())
                {
                    if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
                    {
                        foreach (Team team in Mission.Current.Teams.Where((Team t) => t.HasTeamAi).ToList())
                        {
                            if (team.Side == BattleSideEnum.Attacker)
                            {
                                team.ClearTacticOptions();
                                team.AddTacticOption(new TacticFullScaleAttack(team));
                                team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                                team.AddTacticOption(new TacticFrontalCavalryCharge(team));
                                team.AddTacticOption(new TacticCoordinatedRetreat(team));
                                //team.AddTacticOption(new TacticCharge(team));
                            }
                            if (team.Side == BattleSideEnum.Defender)
                            {
                                team.ClearTacticOptions();
                                team.AddTacticOption(new TacticDefensiveEngagement(team));
                                team.AddTacticOption(new TacticDefensiveLine(team));
                                team.AddTacticOption(new TacticFullScaleAttack(team));
                                team.AddTacticOption(new TacticCharge(team));
                                //team.AddTacticOption(new TacticHoldChokePoint(team));
                                //team.AddTacticOption(new TacticHoldTheHill(team));
                                //team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                                //team.AddTacticOption(new TacticCoordinatedRetreat(team));
                                //team.AddTacticOption(new TacticFrontalCavalryCharge(team));
                                //team.AddTacticOption(new TacticDefensiveRing(team));
                                //team.AddTacticOption(new TacticArchersOnTheHill(team));
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TacticFullScaleAttack))]
        class OverrideTacticFullScaleAttack
        {

            [HarmonyPostfix]
            [HarmonyPatch("Advance")]
            static void PostfixAdvance(ref Formation ____mainInfantry, ref Formation ____archers, ref Formation ____rightCavalry, ref Formation ____leftCavalry)
            {
                if (____mainInfantry != null)
                {
                    ____mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
                }
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
                }
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(3f);
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(3f);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____archers, ref Formation ____rightCavalry, ref Formation ____leftCavalry)
            {

                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }

                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined( ____mainInfantry, ____hasBattleBeenJoined, 16f);
            }

            [HarmonyPostfix]
            [HarmonyPatch("GetTacticWeight")]
            static void PostfixGetAiWeight(TacticFullScaleAttack __instance, ref float __result)
            {
                FieldInfo teamField = typeof(TacticFullScaleAttack).GetField("team", BindingFlags.NonPublic | BindingFlags.Instance);
                teamField.DeclaringType.GetField("team");
                Team currentTeam = (Team)teamField.GetValue(__instance);
                if (currentTeam.Side == BattleSideEnum.Defender)
                {
                    if (currentTeam.QuerySystem.InfantryRatio > 0.9f)
                    {
                        __result = 100f;
                    }
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("ManageFormationCounts")]
            static void PostfixManageFormationCounts(ref Formation ____leftCavalry, ref Formation ____rightCavalry)
            {
                if (____leftCavalry != null && ____rightCavalry != null)
                {
                    List<Agent> mountedSkirmishersList = new List<Agent>();
                    List<Agent> mountedMeleeList = new List<Agent>();
                    ____leftCavalry.ApplyActionOnEachUnit(delegate (Agent agent)
                    {
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
                                {
                                    ismountedSkrimisher = true;
                                }
                            }
                        }
                        if (ismountedSkrimisher)
                        {
                            mountedSkirmishersList.Add(agent);
                        }
                        else
                        {
                            mountedMeleeList.Add(agent);
                        }
                    });

                    ____rightCavalry.ApplyActionOnEachUnit(delegate (Agent agent)
                    {
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
                                {
                                    ismountedSkrimisher = true;
                                }
                            }
                        }
                        if (ismountedSkrimisher)
                        {
                            mountedSkirmishersList.Add(agent);
                        }
                        else
                        {
                            mountedMeleeList.Add(agent);
                        }
                    });
                    int j = 0;
                    int cavalryCount = ____leftCavalry.CountOfUnits + ____rightCavalry.CountOfUnits;
                    foreach (Agent agent in mountedSkirmishersList.ToList())
                    {
                        if (j < cavalryCount / 2)
                        {
                            agent.Formation = ____leftCavalry;
                        }
                        else
                        {
                            agent.Formation = ____rightCavalry;
                        }
                        j++;
                    }
                    foreach (Agent agent in mountedMeleeList.ToList())
                    {
                        if (j < cavalryCount / 2)
                        {
                            agent.Formation = ____leftCavalry;
                        }
                        else
                        {
                            agent.Formation = ____rightCavalry;
                        }
                        j++;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TacticFrontalCavalryCharge))]
        class OverrideTacticFrontalCavalryCharge
        {

            [HarmonyPostfix]
            [HarmonyPatch("Advance")]
            static void PostfixAdvance(ref Formation ____cavalry)
            {
                if (____cavalry != null)
                {
                    ____cavalry.AI.SetBehaviorWeight<BehaviorVanguard>(1.5f);
                    ____cavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____cavalry, ref Formation ____archers)
            {

                if (____cavalry != null)
                {
                    ____cavalry.AI.ResetBehaviorWeights();
                    ____cavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____cavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____cavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____cavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____cavalry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined( ____cavalry, ____hasBattleBeenJoined, 22f);
            }
        }

        [HarmonyPatch(typeof(TacticRangedHarrassmentOffensive))]
        class OverrideTacticRangedHarrassmentOffensive
        {

            [HarmonyPostfix]
            [HarmonyPatch("Advance")]
            static void PostfixAdvance(ref Formation ____leftCavalry, ref Formation ____rightCavalry)
            {
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____leftCavalry, ref Formation ____rightCavalry, ref Formation ____archers)
            {

                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined( ____mainInfantry, ____hasBattleBeenJoined, 16f);
            }

            [HarmonyPostfix]
            [HarmonyPatch("ManageFormationCounts")]
            static void PostfixManageFormationCounts(ref Formation ____leftCavalry, ref Formation ____rightCavalry)
            {
                if (____leftCavalry != null && ____rightCavalry != null)
                {
                    List<Agent> mountedSkirmishersList = new List<Agent>();
                    List<Agent> mountedMeleeList = new List<Agent>();
                    ____leftCavalry.ApplyActionOnEachUnit(delegate (Agent agent)
                    {
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
                                {
                                    ismountedSkrimisher = true;
                                }
                            }
                        }
                        if (ismountedSkrimisher)
                        {
                            mountedSkirmishersList.Add(agent);
                        }
                        else
                        {
                            mountedMeleeList.Add(agent);
                        }
                    });

                    ____rightCavalry.ApplyActionOnEachUnit(delegate (Agent agent)
                    {
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
                                {
                                    ismountedSkrimisher = true;
                                }
                            }
                        }
                        if (ismountedSkrimisher)
                        {
                            mountedSkirmishersList.Add(agent);
                        }
                        else
                        {
                            mountedMeleeList.Add(agent);
                        }
                    });
                    int j = 0;
                    int cavalryCount = ____leftCavalry.CountOfUnits + ____rightCavalry.CountOfUnits;
                    foreach (Agent agent in mountedSkirmishersList.ToList())
                    {
                        if (j < cavalryCount / 2)
                        {
                            agent.Formation = ____leftCavalry;
                        }
                        else
                        {
                            agent.Formation = ____rightCavalry;
                        }
                        j++;
                    }
                    foreach (Agent agent in mountedMeleeList.ToList())
                    {
                        if (j < cavalryCount / 2)
                        {
                            agent.Formation = ____leftCavalry;
                        }
                        else
                        {
                            agent.Formation = ____rightCavalry;
                        }
                        j++;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TacticDefensiveLine))]
        class OverrideTacticDefensiveLine
        {
            [HarmonyPrefix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static bool PrefixHasBattleBeenJoined(ref Formation ____mainInfantry, ref bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined( ____mainInfantry, ____hasBattleBeenJoined, 14f);
                return false;
            }

            [HarmonyPostfix]
            [HarmonyPatch("Defend")]
            static void PostfixDefend(ref Formation ____archers, ref Formation ____mainInfantry)
            {
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
                }
                if (____mainInfantry != null)
                {
                    ____mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Engage")]
            static void PostfixEngage(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry)
            {
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                Utilities.FixCharge(ref ____mainInfantry);
            }

        }

        [HarmonyPatch(typeof(TacticDefensiveEngagement))]
        class OverrideTacticDefensiveEngagement
        {
            [HarmonyPrefix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static bool PrefixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined, 14f);
                return false;
            }

            [HarmonyPostfix]
            [HarmonyPatch("Defend")]
            static void PostfixDefend(ref Formation ____archers, ref Formation ____mainInfantry)
            {
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
                }
                if (____mainInfantry != null)
                {
                    ____mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Engage")]
            static void PostfixAttack(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry)
            {
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                Utilities.FixCharge(ref ____mainInfantry);
            }
        }

        [HarmonyPatch(typeof(TacticComponent))]
        class OverrideTacticComponent
        {
            [HarmonyPrefix]
            [HarmonyPatch("SetDefaultBehaviorWeights")]
            static bool PrefixSetDefaultBehaviorWeights(ref Formation f)
            {
                if (f != null)
                {
                    if (f.QuerySystem.IsRangedFormation)
                    {
                        f.AI.SetBehaviorWeight<BehaviorCharge>(0.3f);
                    }
                    else
                    {
                        f.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    }
                    f.AI.SetBehaviorWeight<BehaviorPullBack>(1f);
                    f.AI.SetBehaviorWeight<BehaviorStop>(1f);
                    f.AI.SetBehaviorWeight<BehaviorReserve>(0f);
                }
                return false;
            }
        }

    }
}