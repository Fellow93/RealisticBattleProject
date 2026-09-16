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

        /// <summary>
        /// Field battles: when one side's reinforcement wave triggers, the other side's wave triggers too.
        ///
        /// Vanilla checks both sides on the same global timer tick, but the Wave method's per-side gate
        /// (ComputeWaveBatch) only fires once THAT side has lost ReinforcementWavePercentage of its initial spawn.
        /// The side taking fewer losses therefore never gets its wave while the other keeps refilling.
        ///
        /// Two patches:
        ///  - Postfix on DefaultBattleMissionAgentSpawnLogic.CheckGlobalReinforcementBatch: after vanilla's loop, if
        ///    exactly one side became active and the other still has remaining spawns and an empty reserve, re-run the
        ///    other side's CheckReinforcementBatch with the force flag set, then recompute _spawningReinforcements the
        ///    way vanilla does (agent-cap quota still applies).
        ///  - Postfix on MissionBattleSideSpawnContext.ComputeWaveBatch: with the force flag set and a zero result,
        ///    return the side's normal wave size so the casualty gate is bypassed for that single call.
        /// Siege, sally-out and naval use other spawn methods and are excluded.
        /// </summary>
        internal static class SyncedReinforcementWaves
        {
            [ThreadStatic]
            internal static bool ForceWave;

            private static readonly AccessTools.FieldRef<DefaultBattleMissionAgentSpawnLogic, MissionBattleSideSpawnContext[]> Contexts =
                AccessTools.FieldRefAccess<DefaultBattleMissionAgentSpawnLogic, MissionBattleSideSpawnContext[]>("_battleSideSpawnContexts");
            private static readonly AccessTools.FieldRef<DefaultBattleMissionAgentSpawnLogic, bool> SpawningReinforcements =
                AccessTools.FieldRefAccess<DefaultBattleMissionAgentSpawnLogic, bool>("_spawningReinforcements");
            private static readonly AccessTools.FieldRef<DefaultBattleMissionAgentSpawnLogic, BasicMissionTimer> GlobalTimer =
                AccessTools.FieldRefAccess<DefaultBattleMissionAgentSpawnLogic, BasicMissionTimer>("_globalReinforcementSpawnTimer");
            private static readonly AccessTools.FieldRef<DefaultBattleMissionAgentSpawnLogic, float> GlobalInterval =
                AccessTools.FieldRefAccess<DefaultBattleMissionAgentSpawnLogic, float>("_globalReinforcementInterval");

            private static readonly System.Reflection.MethodInfo CheckMinimumBatchQuotaRequirement =
                AccessTools.Method(typeof(DefaultBattleMissionAgentSpawnLogic), "CheckMinimumBatchQuotaRequirement");

            private static bool IsFieldBattle()
            {
                Mission mission = Mission.Current;
                return mission != null && mission.IsFieldBattle && !mission.IsSiegeBattle && !mission.IsSallyOutBattle && !mission.IsNavalBattle;
            }

            [HarmonyPatch(typeof(DefaultBattleMissionAgentSpawnLogic), "CheckGlobalReinforcementBatch")]
            private static class CheckGlobalReinforcementBatchPatch
            {
                // Vanilla resets the timer inside the method, so the prefix records whether this call is the tick
                // that actually evaluated the batches.
                private static bool Prefix(DefaultBattleMissionAgentSpawnLogic __instance, out bool __state)
                {
                    __state = false;
                    try
                    {
                        BasicMissionTimer timer = GlobalTimer(__instance);
                        __state = timer != null && timer.ElapsedTime >= GlobalInterval(__instance);
                    }
                    catch (Exception) { }
                    return true;
                }

                private static void Postfix(DefaultBattleMissionAgentSpawnLogic __instance, bool __state)
                {
                    if (!__state || !IsFieldBattle())
                    {
                        return;
                    }
                    try
                    {
                        MissionSpawnSettings settings = __instance.SpawnSettings;
                        if (settings.ReinforcementTroopsSpawnMethod != MissionSpawnSettings.ReinforcementSpawnMethod.Wave)
                        {
                            return;
                        }
                        MissionBattleSideSpawnContext[] contexts = Contexts(__instance);
                        if (contexts == null || contexts.Length < 2 || contexts[0] == null || contexts[1] == null)
                        {
                            return;
                        }
                        bool defenderActive = contexts[0].ReinforcementSpawnActive;
                        bool attackerActive = contexts[1].ReinforcementSpawnActive;
                        if (defenderActive == attackerActive)
                        {
                            return;
                        }

                        int laggingIndex = defenderActive ? 1 : 0;
                        MissionBattleSideSpawnContext lagging = contexts[laggingIndex];
                        MissionSpawnPhase phase = laggingIndex == 0 ? __instance.DefenderActivePhase : __instance.AttackerActivePhase;
                        if (phase == null || phase.RemainingSpawnNumber <= 0 || lagging.ReservedTroopsCount > 0)
                        {
                            return;
                        }

                        bool forced;
                        ForceWave = true;
                        try
                        {
                            forced = lagging.CheckReinforcementBatch();
                        }
                        finally
                        {
                            ForceWave = false;
                        }

                        if (forced)
                        {
                            bool quotaOk = (bool)CheckMinimumBatchQuotaRequirement.Invoke(__instance, null);
                            SpawningReinforcements(__instance) = quotaOk;
                        }
                    }
                    catch (Exception)
                    {
                        // Any surprise in the spawn internals: keep vanilla's per-side behaviour.
                    }
                }
            }

            /// <summary>
            /// Two jobs, both at reservation time so a wave arrives as one burst and the reserve empties:
            ///  - with the force flag set and a zero result, return the side's wave size (casualty gate bypassed);
            ///  - clamp the batch so the side never exceeds half the battle size. Vanilla only checks the total agent
            ///    cap; clamping here (rather than at spawn time) avoids a leftover reserve that trickles in one man at
            ///    a time as losses free room. The next wave then needs the side to fall below half again.
            /// </summary>
            [HarmonyPatch(typeof(MissionBattleSideSpawnContext), "ComputeWaveBatch")]
            private static class ComputeWaveBatchPatch
            {
                private static readonly AccessTools.FieldRef<MissionBattleSideSpawnContext, int> BatchSize =
                    AccessTools.FieldRefAccess<MissionBattleSideSpawnContext, int>("_reinforcementBatchSize");
                private static readonly AccessTools.FieldRef<MissionBattleSideSpawnContext, IBattleMissionAgentSpawnLogic> SpawnLogic =
                    AccessTools.FieldRefAccess<MissionBattleSideSpawnContext, IBattleMissionAgentSpawnLogic>("_spawnLogic");

                private static void Postfix(MissionBattleSideSpawnContext __instance, MissionSpawnPhase activePhase, ref int __result)
                {
                    if (activePhase == null || activePhase.RemainingSpawnNumber <= 0 || __instance.ReservedTroopsCount > 0)
                    {
                        return;
                    }
                    try
                    {
                        if (ForceWave && __result <= 0)
                        {
                            // Vanilla already recomputed _reinforcementBatchSize (and its quota) before applying the
                            // casualty gate, so the field holds the correct wave size for this side.
                            int size = BatchSize(__instance);
                            if (size > 0)
                            {
                                __result = size;
                            }
                        }
                        if (__result > 0 && IsFieldBattle() && SpawnLogic(__instance) is DefaultBattleMissionAgentSpawnLogic logic)
                        {
                            int sideCap = logic.BattleSize / 2;
                            if (sideCap > 0)
                            {
                                __result = Math.Max(0, Math.Min(__result, sideCap - __instance.NumberOfActiveTroops));
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Keep vanilla's result on any surprise.
                    }
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
