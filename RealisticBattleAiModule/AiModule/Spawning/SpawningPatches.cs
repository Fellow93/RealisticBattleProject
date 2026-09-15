using HarmonyLib;
using SandBox.Missions.MissionLogics;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.MissionSpawnHandlers;

namespace RBMAI.AiModule
{
    internal class SpawningPatches
    {
        /// <summary>
        /// Reinforcements arrive along vanilla's (v1.5.0+) reinforcement spawn paths, but a safe distance in from the
        /// map border rather than right at it.
        ///
        /// Vanilla resolves the reinforcement frame with GetSpawnFrame(offset, searchNearestValidFrame: true, Forward):
        /// it starts at the path's map-edge end and walks toward the centre until the first frame that is inside the
        /// playable area, and spawns there -- i.e. on the boundary line. That is the only call site with that argument
        /// pair (DefaultDeploymentPlan.PlanFieldBattleDeploymentFromSpawnPath), so keying on it isolates reinforcements.
        ///
        /// Relative path offsets run from -half (map edge) through 0 (path centre) to +half; the pivot is the path's
        /// midpoint for every reinforcement path (BattleSideSpawnPathSelector). So we find the first valid offset the
        /// way vanilla does, push it ReinforcementInsetMeters further toward the centre, and cap it so it never
        /// crosses into the middle of the field.
        /// </summary>
        [HarmonyPatch(typeof(SpawnPathData), "GetSpawnFrame")]
        private class ReinforcementSpawnInsetPatch
        {
            private const float ReinforcementInsetMeters = 50f;
            private const float MinDistanceFromPathCentreMeters = 40f;
            private const float SearchStepMeters = 2f;

            private static void Postfix(SpawnPathData __instance, float relativePathOffset, bool searchNearestValidFrame, SpawnPathData.SearchDirection searchDirection, ref MatrixFrame __result)
            {
                if (!searchNearestValidFrame || searchDirection != SpawnPathData.SearchDirection.Forward)
                {
                    return;
                }
                if (Mission.Current == null || Mission.Current.MissionTeamAIType != Mission.MissionTeamAITypeEnum.FieldBattle)
                {
                    return;
                }
                try
                {
                    float centreOffset = __instance.PathLength * 0.5f - __instance.PivotOffset;
                    float cap = centreOffset - MinDistanceFromPathCentreMeters;
                    float firstValid = relativePathOffset;
                    float limit = __instance.PathLength - __instance.PivotOffset;
                    while (firstValid < limit && !__instance.IsPathOffsetValid(firstValid))
                    {
                        firstValid += SearchStepMeters;
                    }
                    if (firstValid >= cap)
                    {
                        // Path too short (or boundary already near the centre): leave vanilla's frame alone.
                        return;
                    }
                    float target = MathF.Min(firstValid + ReinforcementInsetMeters, cap);
                    // The one-argument GetSpawnFrame below does no alpha/navmesh check, so the inset
                    // offset must be validated here: if the path leaves the playable area again within
                    // the inset, walk back toward firstValid (which is known-valid) until it is inside.
                    // Never call the searchNearestValidFrame overload here - it re-enters this postfix.
                    while (target > firstValid && !__instance.IsPathOffsetValid(target))
                    {
                        target -= SearchStepMeters;
                    }
                    if (target <= firstValid || !__instance.IsPathOffsetValid(target))
                    {
                        return;
                    }
                    __result = __instance.GetSpawnFrame(target);
                }
                catch (Exception)
                {
                    // Any surprise in the path data: keep vanilla's frame.
                }
            }
        }

        [HarmonyPatch(typeof(SandBoxSiegeMissionSpawnHandler))]
        private class OverrideSandBoxSiegeMissionSpawnHandler
        {
            [HarmonyPrefix]
            [HarmonyPatch("AfterStart")]
            private static bool PrefixAfterStart(ref MapEvent ____mapEvent, ref DefaultBattleMissionAgentSpawnLogic ____missionAgentSpawnLogic)
            {
                if (____mapEvent != null)
                {
                    int reinforcementWaveCount = BannerlordConfig.GetReinforcementWaveCount();
                    int battleSize = ____missionAgentSpawnLogic.BattleSize;

                    int numberOfInvolvedMen = ____mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Defender);
                    int numberOfInvolvedMen2 = ____mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Attacker);
                    int defenderInitialSpawn = numberOfInvolvedMen;
                    int attackerInitialSpawn = numberOfInvolvedMen2;

                    int totalBattleSize = defenderInitialSpawn + attackerInitialSpawn;

                    if (totalBattleSize > battleSize)
                    {
                        float defenderAdvantage = (float)battleSize / ((float)defenderInitialSpawn * ((battleSize * 2f) / (totalBattleSize)));
                        if (defenderInitialSpawn < (battleSize / 2f))
                        {
                            defenderAdvantage = (float)totalBattleSize / (float)battleSize;
                        }
                        ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Defender, false);
                        ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Attacker, false);

                        MissionSpawnSettings spawnSettings = new MissionSpawnSettings(MissionSpawnSettings.InitialSpawnMethod.BattleSizeAllocating,
                        MissionSpawnSettings.ReinforcementTimingMethod.GlobalTimer,
                        MissionSpawnSettings.ReinforcementSpawnMethod.Wave,
                        3f, 0f, 0f, 0.5f,
                        reinforcementWaveCount,
                        defenderAdvantageFactor: defenderAdvantage);

                        ____missionAgentSpawnLogic.InitWithSinglePhase(numberOfInvolvedMen, numberOfInvolvedMen2, defenderInitialSpawn, attackerInitialSpawn, spawnDefenders: true, spawnAttackers: true, in spawnSettings);
                        return false;
                    }
                    return true;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CustomSiegeMissionSpawnHandler))]
        private class OverrideCustomSiegeMissionSpawnHandler
        {
            [HarmonyPrefix]
            [HarmonyPatch("AfterStart")]
            private static bool PrefixAfterStart(ref DefaultBattleMissionAgentSpawnLogic ____missionAgentSpawnLogic, ref CustomBattleCombatant[] ____battleCombatants)
            {
                int battleSize = ____missionAgentSpawnLogic.BattleSize;

                int numberOfInvolvedMen = ____battleCombatants[0].NumberOfHealthyMembers;
                int numberOfInvolvedMen2 = ____battleCombatants[1].NumberOfHealthyMembers;
                int defenderInitialSpawn = numberOfInvolvedMen;
                int attackerInitialSpawn = numberOfInvolvedMen2;

                int totalBattleSize = defenderInitialSpawn + attackerInitialSpawn;

                if (totalBattleSize > battleSize)
                {
                    float defenderAdvantage = (float)battleSize / ((float)defenderInitialSpawn * ((battleSize * 2f) / (totalBattleSize)));
                    if (defenderInitialSpawn < (battleSize / 2f))
                    {
                        defenderAdvantage = (float)totalBattleSize / (float)battleSize;
                    }
                    ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Defender, false);
                    ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Attacker, false);

                    MissionSpawnSettings spawnSettings = new MissionSpawnSettings(MissionSpawnSettings.InitialSpawnMethod.BattleSizeAllocating,
                        MissionSpawnSettings.ReinforcementTimingMethod.GlobalTimer,
                        MissionSpawnSettings.ReinforcementSpawnMethod.Wave,
                        3f, 0f, 0f, 0.5f,
                        defenderAdvantageFactor: defenderAdvantage);

                    ____missionAgentSpawnLogic.InitWithSinglePhase(numberOfInvolvedMen, numberOfInvolvedMen2, defenderInitialSpawn, attackerInitialSpawn, spawnDefenders: true, spawnAttackers: true, in spawnSettings);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CustomBattleMissionSpawnHandler))]
        private class OverrideAfterStartCustomBattleMissionSpawnHandler
        {
            [HarmonyPrefix]
            [HarmonyPatch("AfterStart")]
            private static bool PrefixAfterStart(ref DefaultBattleMissionAgentSpawnLogic ____missionAgentSpawnLogic, ref CustomBattleCombatant ____defenderParty, ref CustomBattleCombatant ____attackerParty)
            {
                int battleSize = ____missionAgentSpawnLogic.BattleSize;

                int numberOfHealthyMembers = ____defenderParty.NumberOfHealthyMembers;
                int numberOfHealthyMembers2 = ____attackerParty.NumberOfHealthyMembers;
                int defenderInitialSpawn = numberOfHealthyMembers;
                int attackerInitialSpawn = numberOfHealthyMembers2;

                int totalBattleSize = defenderInitialSpawn + attackerInitialSpawn;

                if (totalBattleSize > battleSize)
                {
                    float defenderAdvantage = (float)battleSize / ((float)defenderInitialSpawn * ((battleSize * 2f) / (totalBattleSize)));
                    if (defenderInitialSpawn < (battleSize / 2f))
                    {
                        defenderAdvantage = (float)totalBattleSize / (float)battleSize;
                    }
                    ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Defender, !Mission.Current.IsSiegeBattle);
                    ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Attacker, !Mission.Current.IsSiegeBattle);

                    MissionSpawnSettings spawnSettings = new MissionSpawnSettings(MissionSpawnSettings.InitialSpawnMethod.BattleSizeAllocating,
                        MissionSpawnSettings.ReinforcementTimingMethod.GlobalTimer,
                        MissionSpawnSettings.ReinforcementSpawnMethod.Wave,
                        3f, 0f, 0f, 0.5f,
                        defenderAdvantageFactor: defenderAdvantage);

                    ____missionAgentSpawnLogic.InitWithSinglePhase(numberOfHealthyMembers, numberOfHealthyMembers2, defenderInitialSpawn, attackerInitialSpawn, spawnDefenders: true, spawnAttackers: true, in spawnSettings);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(SandBoxBattleMissionSpawnHandler))]
        private class OverrideAfterStartSandBoxBattleMissionSpawnHandler
        {
            [HarmonyPrefix]
            [HarmonyPatch("AfterStart")]
            private static bool PrefixAfterStart(ref DefaultBattleMissionAgentSpawnLogic ____missionAgentSpawnLogic, ref MapEvent ____mapEvent)
            {
                if (____mapEvent != null)
                {
                    int reinforcementWaveCount = BannerlordConfig.GetReinforcementWaveCount();
                    int battleSize = ____missionAgentSpawnLogic.BattleSize;

                    int numberOfInvolvedMen = ____mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Defender);
                    int numberOfInvolvedMen2 = ____mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Attacker);
                    int defenderInitialSpawn = numberOfInvolvedMen;
                    int attackerInitialSpawn = numberOfInvolvedMen2;

                    int totalBattleSize = defenderInitialSpawn + attackerInitialSpawn;

                    if (totalBattleSize > battleSize)
                    {
                        float defenderAdvantage = (float)battleSize / ((float)defenderInitialSpawn * ((battleSize * 2f) / (totalBattleSize)));
                        if (defenderInitialSpawn < (battleSize / 2f))
                        {
                            defenderAdvantage = (float)totalBattleSize / (float)battleSize;
                        }
                        ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Defender, !Mission.Current.IsSiegeBattle);
                        ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Attacker, !Mission.Current.IsSiegeBattle);

                        MissionSpawnSettings spawnSettings = new MissionSpawnSettings(MissionSpawnSettings.InitialSpawnMethod.BattleSizeAllocating,
                        MissionSpawnSettings.ReinforcementTimingMethod.GlobalTimer,
                        MissionSpawnSettings.ReinforcementSpawnMethod.Wave,
                        3f, 0f, 0f, 0.5f,
                        reinforcementWaveCount,
                        defenderAdvantageFactor: defenderAdvantage);

                        ____missionAgentSpawnLogic.InitWithSinglePhase(numberOfInvolvedMen, numberOfInvolvedMen2, defenderInitialSpawn, attackerInitialSpawn, spawnDefenders: true, spawnAttackers: true, in spawnSettings);
                        return false;
                    }
                    return true;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(PlayerEncounter))]
        [HarmonyPatch("CheckIfBattleShouldContinueAfterBattleMission")]
        private class SetRoutedPatch
        {
            private static bool Prefix(ref PlayerEncounter __instance, ref MapEvent ____mapEvent, ref CampaignBattleResult ____campaignBattleResult, ref bool __result)
            {
                if (____mapEvent != null && ____mapEvent.IsFieldBattle && ____campaignBattleResult != null && ____campaignBattleResult.PlayerVictory && ____campaignBattleResult.BattleResolved)
                {
                    List<UniqueTroopDescriptor> troopsList = null;
                    ____mapEvent.GetMapEventSide(____mapEvent.DefeatedSide).GetAllTroops(ref troopsList);
                    foreach (UniqueTroopDescriptor troop in troopsList)
                    {
                        if (troop.IsValid)
                        {
                            try
                            {
                                ____mapEvent.GetMapEventSide(____mapEvent.DefeatedSide).OnTroopWounded(troop);
                            }
                            catch (Exception e)
                            {
                                e.ToString();
                            }
                        }
                    }
                    __result = false;
                    return false;
                }
                return true;
            }
        }
    }
}
