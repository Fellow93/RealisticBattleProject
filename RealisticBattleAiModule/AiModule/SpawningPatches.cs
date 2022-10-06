using HarmonyLib;
using SandBox.Missions.MissionLogics;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.MissionSpawnHandlers;

namespace RBMAI.AiModule
{
    class SpawningPatches
    {
        [HarmonyPatch(typeof(SandBoxSiegeMissionSpawnHandler))]
        class OverrideSandBoxSiegeMissionSpawnHandler
        {
            [HarmonyPrefix]
            [HarmonyPatch("AfterStart")]
            static bool PrefixAfterStart(ref MapEvent ____mapEvent, ref MissionAgentSpawnLogic ____missionAgentSpawnLogic)
            {
                if (____mapEvent != null)
                {
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

                        MissionSpawnSettings spawnSettings = MissionSpawnSettings.CreateDefaultSpawnSettings();
                        spawnSettings.DefenderAdvantageFactor = defenderAdvantage;
                        //public MissionSpawnSettings(float reinforcementInterval, float reinforcementIntervalChange, int reinforcementIntervalCount, InitialSpawnMethod initialTroopsSpawnMethod,
                        //ReinforcementSpawnMethod reinforcementTroopsSpawnMethod, float reinforcementBatchPercentage, float desiredReinforcementPercentage, float defenderReinforcementBatchPercentage = 0, float attackerReinforcementBatchPercentage = 0, float defenderAdvantageFactor = 1, float defenderRatioLimit = 0.6F);
                        //MissionSpawnSettings(10f, 0f, 0, InitialSpawnMethod.BattleSizeAllocating, ReinforcementSpawnMethod.Balanced, 0.05f, 0.166f); normal
                        //MissionSpawnSettings(90f, -15f, 5, InitialSpawnMethod.FreeAllocation, ReinforcementSpawnMethod.Fixed, 0f, 0f, 0f, 0.1f); sallyout

                        ____missionAgentSpawnLogic.InitWithSinglePhase(numberOfInvolvedMen, numberOfInvolvedMen2, defenderInitialSpawn, attackerInitialSpawn, spawnDefenders: true, spawnAttackers: true, in spawnSettings);
                        return false;
                    }
                    return true;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CustomSiegeMissionSpawnHandler))]
        class OverrideCustomSiegeMissionSpawnHandler
        {
            [HarmonyPrefix]
            [HarmonyPatch("AfterStart")]
            static bool PrefixAfterStart(ref MissionAgentSpawnLogic ____missionAgentSpawnLogic, ref CustomBattleCombatant[] ____battleCombatants)
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

                    MissionSpawnSettings spawnSettings = MissionSpawnSettings.CreateDefaultSpawnSettings();
                    spawnSettings.DefenderAdvantageFactor = defenderAdvantage;
                    //public MissionSpawnSettings(float reinforcementInterval, float reinforcementIntervalChange, int reinforcementIntervalCount, InitialSpawnMethod initialTroopsSpawnMethod,
                    //ReinforcementSpawnMethod reinforcementTroopsSpawnMethod, float reinforcementBatchPercentage, float desiredReinforcementPercentage, float defenderReinforcementBatchPercentage = 0, float attackerReinforcementBatchPercentage = 0, float defenderAdvantageFactor = 1, float defenderRatioLimit = 0.6F);
                    //MissionSpawnSettings(10f, 0f, 0, InitialSpawnMethod.BattleSizeAllocating, ReinforcementSpawnMethod.Balanced, 0.05f, 0.166f); normal
                    //MissionSpawnSettings(90f, -15f, 5, InitialSpawnMethod.FreeAllocation, ReinforcementSpawnMethod.Fixed, 0f, 0f, 0f, 0.1f); sallyout

                    ____missionAgentSpawnLogic.InitWithSinglePhase(numberOfInvolvedMen, numberOfInvolvedMen2, defenderInitialSpawn, attackerInitialSpawn, spawnDefenders: true, spawnAttackers: true, in spawnSettings);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CustomBattleMissionSpawnHandler))]
        class OverrideAfterStartCustomBattleMissionSpawnHandler
        {
            [HarmonyPrefix]
            [HarmonyPatch("AfterStart")]
            static bool PrefixAfterStart(ref MissionAgentSpawnLogic ____missionAgentSpawnLogic, ref CustomBattleCombatant ____defenderParty, ref CustomBattleCombatant ____attackerParty)
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

                    MissionSpawnSettings spawnSettings = MissionSpawnSettings.CreateDefaultSpawnSettings();
                    spawnSettings.DefenderAdvantageFactor = defenderAdvantage;
                    spawnSettings.ReinforcementBatchPercentage = 0.25f;
                    spawnSettings.DesiredReinforcementPercentage = 0.5f;
                    spawnSettings.ReinforcementTroopsSpawnMethod = MissionSpawnSettings.ReinforcementSpawnMethod.Fixed;

                    ____missionAgentSpawnLogic.InitWithSinglePhase(numberOfHealthyMembers, numberOfHealthyMembers2, defenderInitialSpawn, attackerInitialSpawn, spawnDefenders: true, spawnAttackers: true, in spawnSettings);
                    return false;
                }
                return true;
            }
        }

        //[HarmonyPatch(typeof(BaseMissionTroopSpawnHandler))]
        //class OverrideAfterStartBaseMissionTroopSpawnHandler
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch("AfterStart")]
        //    static bool PrefixAfterStart(ref MissionAgentSpawnLogic ____missionAgentSpawnLogic, ref MapEvent ____mapEvent)
        //    {
        //        if (____mapEvent != null)
        //        {
        //            int battleSize = ____missionAgentSpawnLogic.BattleSize;

        //            int numberOfInvolvedMen = ____mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Defender);
        //            int numberOfInvolvedMen2 = ____mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Attacker);
        //            int defenderInitialSpawn = numberOfInvolvedMen;
        //            int attackerInitialSpawn = numberOfInvolvedMen2;

        //            int totalBattleSize = defenderInitialSpawn + attackerInitialSpawn;

        //            if (totalBattleSize > battleSize)
        //            {

        //                float defenderAdvantage = (float)battleSize / ((float)defenderInitialSpawn * ((battleSize * 2f) / (totalBattleSize)));
        //                if (defenderInitialSpawn < (battleSize / 2f))
        //                {
        //                    defenderAdvantage = (float)totalBattleSize / (float)battleSize;
        //                }
        //                ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Defender, !Mission.Current.IsSiegeBattle);
        //                ____missionAgentSpawnLogic.SetSpawnHorses(BattleSideEnum.Attacker, !Mission.Current.IsSiegeBattle);

        //                MissionSpawnSettings spawnSettings = MissionSpawnSettings.CreateDefaultSpawnSettings();
        //                spawnSettings.DefenderAdvantageFactor = defenderAdvantage;
        //                spawnSettings.ReinforcementBatchPercentage = 0.25f;
        //                spawnSettings.DesiredReinforcementPercentage = 0.5f;
        //                spawnSettings.ReinforcementTroopsSpawnMethod = MissionSpawnSettings.ReinforcementSpawnMethod.Fixed;
        //                //public MissionSpawnSettings(float reinforcementInterval, float reinforcementIntervalChange, int reinforcementIntervalCount, InitialSpawnMethod initialTroopsSpawnMethod,
        //                //ReinforcementSpawnMethod reinforcementTroopsSpawnMethod, float reinforcementBatchPercentage, float desiredReinforcementPercentage, float defenderReinforcementBatchPercentage = 0, float attackerReinforcementBatchPercentage = 0, float defenderAdvantageFactor = 1, float defenderRatioLimit = 0.6F);
        //                //MissionSpawnSettings(10f, 0f, 0, InitialSpawnMethod.BattleSizeAllocating, ReinforcementSpawnMethod.Balanced, 0.05f, 0.166f); normal
        //                //MissionSpawnSettings(90f, -15f, 5, InitialSpawnMethod.FreeAllocation, ReinforcementSpawnMethod.Fixed, 0f, 0f, 0f, 0.1f); sallyout

        //                ____missionAgentSpawnLogic.InitWithSinglePhase(numberOfInvolvedMen, numberOfInvolvedMen2, defenderInitialSpawn, attackerInitialSpawn, spawnDefenders: true, spawnAttackers: true, in spawnSettings);
        //                return false;
        //            }
        //            return true;
        //        }
        //        return true;
        //    }
        //}

        [HarmonyPatch(typeof(MissionAgentSpawnLogic))]
        class OverrideBattleSizeSpawnTick
        {

            private static bool hasOneSideSpawnedReinforcements = false;
            private static bool hasOneSideSpawnedReinforcementsAttackers = false;
            private static int numOfDefWhenSpawning = -1;
            private static int numOfAttWhenSpawning = -1;

            private class SpawnPhase
            {
                public int TotalSpawnNumber;

                public int InitialSpawnedNumber;

                public int InitialSpawnNumber;

                public int RemainingSpawnNumber;

                public int NumberActiveTroops;

                public void OnInitialTroopsSpawned()
                {
                    InitialSpawnedNumber = InitialSpawnNumber;
                    InitialSpawnNumber = 0;
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("CheckReinforcementBatch")]
            static bool PrefixBattleSizeSpawnTick(ref MissionAgentSpawnLogic __instance, ref bool ____reinforcementSpawnEnabled, ref int ____battleSize, ref List<SpawnPhase>[] ____phases, ref MissionSpawnSettings ____spawnSettings)
            {
                if (Mission.Current.MissionTeamAIType != Mission.MissionTeamAITypeEnum.FieldBattle)
                {
                    return true;
                }
                int numberOfTroops = __instance.NumberOfAgents;
                for (int i = 0; i < 2; i++)
                {
                    int numberOfTroopsCanBeSpawned = ____phases[i][0].RemainingSpawnNumber;
                    if (numberOfTroops > 0 && numberOfTroopsCanBeSpawned > 0)
                    {
                        if (__instance.NumberOfRemainingTroops <= 0 || numberOfTroopsCanBeSpawned <= 0)
                        {
                            return true;
                        }
                        int activeAtt = __instance.NumberOfActiveAttackerTroops;
                        int activeDef = __instance.NumberOfActiveDefenderTroops;

                        float num4 = (float)(____phases[0][0].InitialSpawnedNumber - __instance.NumberOfActiveDefenderTroops) / (float)____phases[0][0].InitialSpawnedNumber;
                        float num5 = (float)(____phases[1][0].InitialSpawnedNumber - __instance.NumberOfActiveAttackerTroops) / (float)____phases[1][0].InitialSpawnedNumber;
                        if ((____battleSize * 0.5f > __instance.NumberOfActiveDefenderTroops + __instance.NumberOfActiveAttackerTroops) || num4 >= 0.5f || num5 >= 0.5f)
                        {
                            ____reinforcementSpawnEnabled = true;
                            numOfDefWhenSpawning = __instance.NumberOfActiveDefenderTroops;
                            numOfAttWhenSpawning = __instance.NumberOfActiveAttackerTroops;

                            int numberOfInvolvedMen = __instance.GetTotalNumberOfTroopsForSide(BattleSideEnum.Defender);
                            int numberOfInvolvedMen2 = __instance.GetTotalNumberOfTroopsForSide(BattleSideEnum.Attacker);

                            ____spawnSettings.DefenderReinforcementBatchPercentage = (____battleSize * 0.5f - numOfDefWhenSpawning) / (numberOfInvolvedMen + numberOfInvolvedMen2);
                            ____spawnSettings.AttackerReinforcementBatchPercentage = (____battleSize * 0.5f - numOfAttWhenSpawning) / (numberOfInvolvedMen + numberOfInvolvedMen2);
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }
    }
}
