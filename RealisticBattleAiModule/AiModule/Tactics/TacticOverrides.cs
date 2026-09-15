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
        [HarmonyPatch(typeof(TacticCoordinatedRetreat))]
        private class OverrideTacticCoordinatedRetreat
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetTacticWeight")]
            private static bool PrefixGetTacticWeight(ref TacticCoordinatedRetreat __instance, ref float __result)
            {
                bool defenderHasOnlyCavalry = true;
                bool attackerHasOnlyCavalry = true;
                if (Mission.Current != null)
                {
                    foreach (Team team in Mission.Current.Teams)
                    {
                        if (team.IsDefender && team.ActiveAgents.Any(a => !a.HasMount))
                        {
                            defenderHasOnlyCavalry = false;
                        }
                        if (team.IsAttacker && team.ActiveAgents.Any(a => !a.HasMount))
                        {
                            attackerHasOnlyCavalry = false;
                        }
                    }
                }
                if (__instance.Team.IsDefender)
                {
                    if (__instance.Team.IsDefender && __instance.Team.QuerySystem.RemainingPowerRatio < 0.1f && defenderHasOnlyCavalry)
                    {
                        foreach (Team team in Mission.Current.Teams)
                        {
                            if (!team.IsDefender) continue;
                            foreach (Agent agent in team.ActiveAgents)
                            {
                                agent.SetMorale(0f);
                            }
                        }
                        __result = 0f;
                        return false;
                    }
                }
                else
                {
                    if (__instance.Team.IsAttacker && __instance.Team.QuerySystem.RemainingPowerRatio < 0.1f && attackerHasOnlyCavalry)
                    {
                        foreach (Team team in Mission.Current.Teams)
                        {
                            if (!team.IsAttacker) continue;
                            foreach (Agent agent in team.ActiveAgents)
                            {
                                agent.SetMorale(0f);
                            }
                        }
                        __result = 0f;
                        return false;
                    }
                }
                __result = 0f;
                return false;
            }
        }

        [HarmonyPatch(typeof(TacticFrontalCavalryCharge))]
        private class OverrideTacticFrontalCavalryCharge
        {
            [HarmonyPostfix]
            [HarmonyPatch("Advance")]
            private static void PostfixAdvance(ref Formation ____cavalry)
            {
                if (____cavalry != null)
                {
                    ____cavalry.AI.SetBehaviorWeight<BehaviorVanguard>(1.5f);
                    ____cavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            private static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____cavalry, ref Formation ____archers)
            {
                if (____cavalry != null)
                {
                    ____cavalry.AI.ResetBehaviorWeights();
                    ____cavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____cavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
                    ____cavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f);
                }
                if (____archers != null)
                {
                    ____archers.AI.ResetBehaviorWeights();
                    ____archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                RBMAI.Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            private static void PostfixHasBattleBeenJoined(Formation ____cavalry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = RBMAI.Utilities.HasBattleBeenJoined(____cavalry, ____hasBattleBeenJoined, 125f);
            }

            [HarmonyPostfix]
            [HarmonyPatch("GetTacticWeight")]
            private static void PostfixGetTacticWeight(ref float __result)
            {
                __result *= 0.75f;
            }
        }

        [HarmonyPatch(typeof(TacticRangedHarrassmentOffensive))]
        private class OverrideTacticRangedHarrassmentOffensive
        {
            [HarmonyPostfix]
            [HarmonyPatch("Advance")]
            private static void PostfixAdvance(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
            {
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;
                }
                if (____rangedCavalry != null)
                {
                    ____rangedCavalry.AI.ResetBehaviorWeights();
                    TacticRangedHarrassmentOffensive.SetDefaultBehaviorWeights(____rangedCavalry);
                    ____rangedCavalry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    ____rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            private static void PostfixAttack(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
            {
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    TacticComponent.SetDefaultBehaviorWeights(____rightCavalry);
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    TacticComponent.SetDefaultBehaviorWeights(____leftCavalry);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
                }
                if (____rangedCavalry != null)
                {
                    ____rangedCavalry.AI.ResetBehaviorWeights();
                    TacticRangedHarrassmentOffensive.SetDefaultBehaviorWeights(____rangedCavalry);
                    ____rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
                if (____archers != null)
                {
                    ____archers.AI.ResetBehaviorWeights();
                    ____archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                RBMAI.Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            private static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = RBMAI.Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined);
            }

            [HarmonyPostfix]
            [HarmonyPatch("ManageFormationCounts")]
            private static void PostfixManageFormationCounts(ref Formation ____leftCavalry, ref Formation ____rightCavalry)
            {
                if (____leftCavalry != null && ____rightCavalry != null && ____leftCavalry.IsAIControlled && ____rightCavalry.IsAIControlled)
                {
                    List<Agent> mountedSkirmishersList = new List<Agent>();
                    List<Agent> mountedMeleeList = new List<Agent>();
                    ____leftCavalry.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                    {
                        ClassifyMountedAgent(agent, mountedSkirmishersList, mountedMeleeList);
                    });
                    ____rightCavalry.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                    {
                        ClassifyMountedAgent(agent, mountedSkirmishersList, mountedMeleeList);
                    });
                    int j = 0;
                    int cavalryCount = ____leftCavalry.CountOfUnits + ____rightCavalry.CountOfUnits;
                    foreach (Agent agent in mountedSkirmishersList)
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
                    foreach (Agent agent in mountedMeleeList)
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

            [HarmonyPostfix]
            [HarmonyPatch("GetTacticWeight")]
            private static void PostfixGetTacticWeight(ref TacticRangedHarrassmentOffensive __instance, ref float __result)
            {
                if (__instance.Team?.Leader?.Character?.Culture?.StringId == "khuzait")
                {
                    __result *= 1.1f;
                }
                else
                {
                    __result *= 0.6f;
                }
            }
        }

        [HarmonyPatch(typeof(TacticComponent))]
        private class OverrideTacticComponent
        {
            [HarmonyPrefix]
            [HarmonyPatch("SetDefaultBehaviorWeights")]
            private static bool PrefixSetDefaultBehaviorWeights(ref Formation f)
            {
                if (f != null)
                {
                    if (f.QuerySystem.IsRangedFormation)
                    {
                        f.AI.SetBehaviorWeight<BehaviorCharge>(0.2f);
                    }
                    else
                    {
                        f.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    }
                    f.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                    f.AI.SetBehaviorWeight<BehaviorStop>(0f);
                    f.AI.SetBehaviorWeight<BehaviorReserve>(0f);
                }
                return false;
            }
        }
    }
}
